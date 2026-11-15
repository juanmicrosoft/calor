using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.27 epic #1592 ("no silent wrong answers"), language front end. Each of these inputs used
/// to compile with exit 0 and produce wrong C#; each is now correct or a hard diagnostic:
/// <list type="bullet">
/// <item>#1550 — writes to an immutable <c>§B</c> or a <c>§L</c> loop variable (Calor0213).</item>
/// <item>#1607 — an unknown visibility word compiled as <c>private</c> (Calor0122).</item>
/// <item>#1608 — built-in operations dropped extra operands (Calor0110, or the operand is emitted).</item>
/// <item>#1635 — <c>§B{W:t}</c> read the type <c>W</c> as the binding name (Calor0123).</item>
/// </list>
/// Every negative case has a positive control next to it.
/// </summary>
public class SilentAcceptanceLanguageTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return new Parser(tokens, diagnostics).Parse();
    }

    private static DiagnosticBag Bind(string source)
    {
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        new Binder(diagnostics).Bind(module);
        return diagnostics;
    }

    private static CompilationResult Compile(string source) =>
        Program.Compile(source, "test.calr", new CompilationOptions { DeferGeneratedOutputValidation = true });

    private static string[] ErrorCodes(DiagnosticBag diagnostics) =>
        diagnostics.Errors.Select(e => e.Code).ToArray();

    private static string NativeFn(string body, string signature = "() → int") =>
        "module T T block\n  func Run " + signature + "\n" +
        (body.StartsWith('!') ? "" : "    !\n") +
        string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n";

    private static string Fn(string body, string signature = "() -> i32") =>
        "§M{T}\n  §F{Run:pub} " + signature + "\n" +
        string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n";

    // ===================================================================
    // #1607 — unknown visibility words
    // ===================================================================

    [Theory]
    [InlineData("§MT{A:bogus} () -> i32")]
    [InlineData("§MT{A:prv} () -> i32")]
    [InlineData("§MT{A:i32} () -> i32")]
    public void Parser_UnknownMethodVisibility_IsCalor0122ListingValidWords(string header)
    {
        Parse($"§M{{V}}\n  §CL{{K:pub}}\n    {header}\n      §R 1\n", out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.UnknownVisibility);
        Assert.Contains("pub, int, prot, priv, prot-int, priv-prot", error.Message);
    }

    [Theory]
    [InlineData("§F{Run:bogus} () -> i32\n    §R 1")]
    [InlineData("§CL{K:pub}\n    §FLD{i32:x:bogus}")]
    [InlineData("§CL{K:pub}\n    §CTOR{bogus}")]
    [InlineData("§CL{K:pub}\n    §PROP{X:i32:bogus:get}")]
    [InlineData("§CL{K:pub}\n    §EVT{Click:bogus:EventHandler}")]
    public void Parser_UnknownVisibility_IsRejectedOnEveryDeclarationKind(string member)
    {
        Parse($"§M{{V}}\n  {member}\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("public", Visibility.Public)]
    [InlineData("internal", Visibility.Internal)]
    [InlineData("protected", Visibility.Protected)]
    [InlineData("private", Visibility.Private)]
    [InlineData("protected internal", Visibility.ProtectedInternal)]
    [InlineData("private protected", Visibility.PrivateProtected)]
    public void Parser_EveryValidVisibilityWord_ParsesToItsLevel(string word, Visibility expected)
    {
        // Public and protected boundaries carry an explicit effect row; private/internal bodies infer theirs.
        var row = word.Contains("public") || word.Contains("protected") ? "      !\n" : "";
        var module = Parse($"module V V block\n  class K\n    func A {word} () → int\n{row}      return 1\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(expected, module.Classes.Single().Methods.Single().Visibility);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("internal")]
    [InlineData("protected")]
    [InlineData("pri")]
    [InlineData("pro")]
    [InlineData("protected-internal")]
    [InlineData("private-protected")]
    [InlineData("PUB")]
    [InlineData("Priv")]
    public void Parser_VisibilitySynonymsAndCaseVariants_AreErrors(string word)
    {
        Parse($"§M{{V}}\n  §CL{{K:pub}}\n    §MT{{A:{word}}} () -> i32\n      §R 1\n",
            out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownVisibility);
    }

    // Review round 1: compact headers must not swallow an unknown visibility word.
    [Theory]
    [InlineData("§MT{Run:prv} () -> i32\n      §R 1")]
    [InlineData("§MT{Run:prv:virt} () -> i32\n      §R 1")]
    [InlineData("§MT{Run:prv:} () -> i32\n      §R 1")]
    [InlineData("§CTOR{prv}")]
    public void Parser_CompactHeaderWithUnknownVisibility_IsCalor0122(string member)
    {
        Parse($"§M{{V}}\n  §CL{{K:pub}}\n    {member}\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§MT{Run:bogus} () -> i32")]
    [InlineData("func Run private () → int")]
    [InlineData("§MT{Run:prv} () -> i32")]
    public void Parser_InterfaceMethodNonPublicOrUnknownVisibility_IsCalor0122(string member)
    {
        var source = member.StartsWith("func ", StringComparison.Ordinal)
            ? $"module V V block\n  interface IRun\n    {member}\n"
            : $"§M{{V}}\n  §IFACE{{IRun}}\n    {member}\n";
        Parse(source, out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("func Run () → int\n      !")]
    [InlineData("func Run public () → int\n      !")]
    public void Parser_InterfaceMethodPublicOrOmittedVisibility_Parses(string member)
    {
        var module = Parse($"module V V block\n  interface IRun\n    {member}\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal("Run", module.Interfaces.Single().Methods.Single().Name);
    }

    [Fact]
    public void Parser_CompactAbstractMethodWithUnknownVisibility_IsCalor0122()
    {
        Parse("§M{V}\n  §CL{K:pub:abs}\n    §AMT{Run:prv} () -> i32\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("func Run public () → int\n      !\n      return 1", "Run", Visibility.Public)]
    [InlineData("func Run private () → int\n      return 1", "Run", Visibility.Private)]
    [InlineData("func Run protected virtual () → int\n      !\n      return 1", "Run", Visibility.Protected)]
    [InlineData("func prv private () → int\n      return 1", "prv", Visibility.Private)]
    public void Parser_CompactMethodHeaders_PositiveControls(string member, string name, Visibility visibility)
    {
        var module = Parse($"module V V block\n  class K\n    {member}\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        var method = module.Classes.Single().Methods.Single();
        Assert.Equal(name, method.Name);
        Assert.Equal(visibility, method.Visibility);
    }

    [Fact]
    public void Parser_ModifierInVisibilitySlot_PointsAtTheModifierField()
    {
        Parse("§M{V}\n  §CL{K:pub:abs}\n    §MT{A:abs} () -> i32\n", out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.UnknownVisibility);
        Assert.Contains("':pub:abs'", error.Message);
    }

    [Fact]
    public void Parser_OmittedVisibility_StillDefaultsWithoutDiagnostic()
    {
        var module = Parse("module V V block\n  class K\n    field int:x private\n    func A private () → int\n      return 1\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Visibility.Private, module.Classes.Single().Fields.Single().Visibility);
    }

    [Fact]
    public void Parser_CompactEventHeader_AsDocumented_Parses()
    {
        var module = Parse("module V V block\n  class K\n    event EventHandler:Click\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        var evt = module.Classes.Single().Events.Single();
        Assert.Equal("Click", evt.Name);
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.Equal("EventHandler", evt.DelegateType);
    }

    [Fact]
    public void Emitter_ProtectedShortForm_EmitsProtectedNotPrivate()
    {
        var result = Compile("module V V block\n  class K\n    func A protected () → int\n      !\n      return 1\n");

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Contains("protected int A()", result.GeneratedCode);
    }

    // ===================================================================
    // #1635 — type-first binding header
    // ===================================================================

    [Theory]
    [InlineData("W", "t")]
    [InlineData("T", "item")]
    [InlineData("URL", "u")]
    [InlineData("T[]", "items")]
    public void Parser_TypeFirstHeaderReadNameFirst_IsCalor0123(string pos0, string pos1)
    {
        Parse(Fn($"§B{{{pos0}:{pos1}}} default\n§R 1"), out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.TypeFirstBindingHeader);
        Assert.Contains($"§B{{{pos1}:{pos0}}}", error.Message);
    }

    [Theory]
    [InlineData("W:t:bogus")]
    [InlineData("b1:result:string")]
    [InlineData("W:t:const:x")]
    [InlineData("x:List<i32>:bogus")]
    [InlineData("List<i32>:x:const:y")]
    public void Parser_UnknownThirdHeaderField_IsCalor0123(string header)
    {
        Parse(Fn($"§B{{{header}}} default\n§R 1"), out var diagnostics);

        Assert.Contains(DiagnosticCode.TypeFirstBindingHeader, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("let W:t", "t", "W")]           // name-first, the suggested fix
    [InlineData("var W:t", "t", "W")]           // mutable name-first
    [InlineData("let Widget:w", "w", "Widget")] // PascalCase type-first
    [InlineData("let int:x", "x", "int")]       // built-in type-first
    [InlineData("let T:x", "x", "T")]           // generic parameter, name-first
    [InlineData("let int:N", "N", "int")]       // upper-case name, built-in type
    [InlineData("let long:N", "N", "long")]
    [InlineData("let long:t", "t", "long")]
    [InlineData("let nint:p", "p", "nint")]
    [InlineData("let int?:x", "x", "int?")]
    [InlineData("let int?:x ", "x", "int?")] // legacy `int?` alias row; native has one spelling (trailing space keeps the row distinct)
    [InlineData("let W:t ", "t", "W")]          // legacy explicit type-first `:const` row; native headers are always type-first
    public void Parser_UnambiguousHeaders_ParseWithoutCalor0123(string header, string name, string type)
    {
        var module = Parse(NativeFn($"{header} = default\nreturn 1"), out var diagnostics);

        Assert.DoesNotContain(DiagnosticCode.TypeFirstBindingHeader, ErrorCodes(diagnostics));
        var bind = module.Functions.Single().Body.OfType<BindStatementNode>().Single();
        Assert.Equal(name, bind.Name);
        Assert.Equal(type, bind.TypeName);
    }

    [Theory]
    [InlineData("item", "T")]
    [InlineData("t", "W")]
    [InlineData("x", "i32")]
    [InlineData("w", "Widget")]
    [InlineData("p", "nint")]
    [InlineData("X", "foo")]
    [InlineData("Foo", "T")]
    [InlineData("xs", "T[]")]
    [InlineData("n", "?str")]
    [InlineData("N", "long")]
    [InlineData("t", "long")]
    [InlineData("x", "i32?")]
    [InlineData("URL", "u8")]
    public void FormatImmutableBindHeader_ReadsBackAsTheSameNameAndType(string name, string type)
    {
        var header = AttributeHelper.FormatImmutableBindHeader(name, type);
        var module = Parse(Fn($"§B{{{header}}} default\n§R 1"), out var diagnostics);

        Assert.DoesNotContain(DiagnosticCode.TypeFirstBindingHeader, ErrorCodes(diagnostics));
        var bind = module.Functions.Single().Body.OfType<BindStatementNode>().Single();
        Assert.Equal(name, bind.Name);
        Assert.Equal(AttributeHelper.ExpandType(type), bind.TypeName);
        Assert.False(bind.IsMutable);
    }

    [Fact]
    public void Converter_GenericLocalOfOneLetterType_RoundTripsWithoutSwap()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            public class Box
            {
                public static T First<T>(T[] items)
                {
                    T item = items[0];
                    return item;
                }
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Contains("let T:item", conversion.CalorSource);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains("T item =", result.GeneratedCode);
    }

    // ===================================================================
    // #1550 — writes to immutable bindings and loop variables
    // ===================================================================

    [Theory]
    [InlineData("let int:x = 1\nset x 2\nreturn x")]
    [InlineData("let x = 1\nset x (x + 1)\nreturn x")]
    [InlineData("let int:x = 1\nset x (x + 1)\nlet y = x\nreturn x")]
    [InlineData("let int:x = 1\nlet y = x\nset x (x - 1)\nreturn x")]
    [InlineData("for i from 0 to 3 step 1\n  set i 5\nreturn 0")]
    [InlineData("for i from 0 to 3 step 1\n  var int:i = 9\nreturn 0")]
    [InlineData("for i from 0 to 3 step 1\n  set i (i + 1)\n  let y = i\nreturn 0")]
    public void Binder_WriteToReadOnlyLocal_IsCalor0213(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // Review round 1: statements the binder does not bind still may not write a read-only local.
    [Theory]
    [InlineData("let int:x = 1\npp DEBUG\n  set x 2\nreturn x")]
    [InlineData("for i from 0 to 3 step 1\n  pp DEBUG\n    set i 5\nreturn 0")]
    // #1678: `§B{x} 1` then `§PP` `§B{~x} 2` is now bound, so it is Calor0260 like the same
    // rebind outside §PP (PreprocessorBindingTests.Binder_MutableRebindOfPreprocessorImmutableBinding_IsCalor0260).
    [InlineData("! mut\nlet int:x = 1\nvar List<int>:xs = new List<int>()\npp DEBUG\n  set x (x + 1)\n  xs.Add(x)\nreturn x")]
    public void Binder_WriteInsideUnboundStatement_IsCalor0213(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // Review round 2: tuple targets, and locals declared inside an unbound statement.
    [Theory]
    [InlineData("let int:x = 1\nvar int:y = 2\nset (x, y) (3, 4)\nreturn x")]
    [InlineData("var int:y = 2\nfor i from 0 to 3 step 1\n  set (y, i) (3, 4)\nreturn y")]
    [InlineData("pp DEBUG\n  let int:z = 1\n  set z 2\nreturn 0")]
    [InlineData("pp DEBUG\n  for j from 0 to 3 step 1\n    set j 2\nreturn 0")]
    [InlineData("let int:x = 1\npp DEBUG\n  set (x, x) (2, 3)\nreturn x")]
    public void Binder_TupleAndNestedDeclarationWrites_AreCalor0213(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("var int:x = 1\nvar int:y = 2\nset (x, y) (y, x)\nreturn x")]
    [InlineData("pp DEBUG\n  var int:z = 1\n  set z 2\nreturn 0")]
    [InlineData("let int:x = 1\npp DEBUG\n  var int:w = 1\n  set w x\nreturn x")]
    public void Binder_TupleAndNestedDeclarationWritesToMutable_AreAccepted(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // Ledger regeneration: FluentValidation's TestValidationContinuation.MatchedFailures getter
    // reassigns a local after a method was converted; the getter used that method's set.
    [Theory]
    [InlineData("get { var m = 1; m = m + _n; return m; }")]
    [InlineData("get { var m = 1; m += _n; return m; }")]
    public void Converter_AccessorThatReassignsALocal_EmitsAMutableBinding(string accessor)
    {
        var conversion = new CSharpToCalorConverter().Convert($$"""
            public class Holder
            {
                private int _n = 2;
                public int Run() { var k = 0; return k; }
                public int Value { {{accessor}} }
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Contains("var m", conversion.CalorSource);
        var result = Compile(conversion.CalorSource!);
        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    // Review round 3: a block lambda in an expression-bodied member converted after a method.
    [Fact]
    public void Converter_BlockLambdaInExpressionBodiedProperty_EmitsAMutableBinding()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            public class Lambdas
            {
                public int Run() => 0;
                public System.Func<int> Next => () => { int x = 0; x++; return x; };
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        var result = Compile(conversion.CalorSource!);
        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    // Review round 3: `#if` opens no C# scope, so a §PP binding stays visible after the block.
    [Theory]
    [InlineData("pp DEBUG\n  let int:x = 1\nset x 2\nreturn 0")]
    [InlineData("pp DEBUG\n  let int:x = 1\nset x (x + 1)\nlet y = x\nreturn 0")]
    [InlineData("pp DEBUG\n  let int:v = 1\nBump(ref v)\nreturn 0")]
    public void Binder_WriteAfterPreprocessorBlockToItsImmutableBinding_IsCalor0213(string body)
    {
        var module = Parse(WithBump(body, "ref"), out var diagnostics);
        new Binder(diagnostics).Bind(module);

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("pp DEBUG\n  var int:x = 1\nset x 2\nreturn 0")]
    [InlineData("pp DEBUG\n  var int:v = 1\nBump(ref v)\nreturn 0")]
    public void Binder_WriteAfterPreprocessorBlockToItsMutableBinding_IsAccepted(string body)
    {
        var module = Parse(WithBump(body, "ref"), out var diagnostics);
        new Binder(diagnostics).Bind(module);

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("int x = 0; ++(x); return x;")]
    [InlineData("int x = 0; (x)++; return x;")]
    [InlineData("int x = 0; int y = 0; (x, y) = (1, 2); return x + y;")]
    public void Converter_ParenthesizedAndTupleWrites_BecomeMutableBindings(string body)
    {
        var conversion = new CSharpToCalorConverter().Convert($$"""
            public class Writes
            {
                public static int Run()
                {
                    {{body}}
                }
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        var result = Compile(conversion.CalorSource!);
        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    [Theory]
    [InlineData("var int:x = 1\npp DEBUG\n  set x 2\nreturn x")]
    [InlineData("let int:x = 1\npp DEBUG\n  var int:y = 0\n  set y x\nreturn x")]
    [InlineData("! mut\nvar int:x = 1\nvar List<int>:xs = new List<int>()\npp DEBUG\n  set x (x + 1)\n  xs.Add(x)\nreturn x")]
    public void Binder_WriteInsideUnboundStatementToMutableLocal_IsAccepted(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("var int:x = 1\nset x 2\nreturn x")]
    [InlineData("var x = 1\nset x (x + 1)\nreturn x")]
    [InlineData("var int:x = 1\nset x (x + 1)\nlet y = x\nreturn x")]
    [InlineData("var int:x = 1\nvar x = 5\nreturn x")]
    [InlineData("var s = 0\nfor i from 0 to 3 step 1\n  set s (s + i)\nreturn s")]
    [InlineData("var List<int>:xs = new List<int>() { 1 }\nset xs xs\nreturn 0")]
    public void Binder_WritesToMutableLocals_AreAccepted(string body)
    {
        var diagnostics = Bind(NativeFn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_AssignToParameter_IsAccepted()
    {
        var diagnostics = Bind(NativeFn("set n 2\nreturn n", "(int:n) → int"));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    private static string WithBump(string callerBody, string modifier) =>
        "module T T block\n" +
        $"  func Bump ({modifier} int:v) → void\n" +
        "    !\n" +
        (modifier == "in" ? "    return\n" : "    set v 1\n") +
        "  func Run () → int\n" +
        "    !\n" +
        string.Join("\n", callerBody.Split('\n').Select(line => "    " + line)) + "\n";

    [Theory]
    [InlineData("let int:v = 1\nBump(ref v)\nreturn v", "ref")]
    [InlineData("let int:v = 1\nBump(out v)\nreturn v", "out")]
    [InlineData("for i from 0 to 3 step 1\n  Bump(ref i)\nreturn 0", "ref")]
    public void Binder_RefOrOutOfReadOnlyLocal_IsCalor0213(string body, string modifier)
    {
        var diagnostics = Bind(WithBump(body, modifier));

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
        Assert.Contains($"as a '{modifier}' argument", error.Message);
    }

    [Theory]
    [InlineData("var int:v = 1\nBump(ref v)\nreturn v", "ref")]
    [InlineData("var int:v = 1\nBump(out v)\nreturn v", "out")]
    [InlineData("let int:v = 1\nBump(in v)\nreturn v", "in")]
    public void Binder_RefOutOfMutableOrInOfImmutable_IsAccepted(string body, string modifier)
    {
        var diagnostics = Bind(WithBump(body, modifier));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Compile_AssignToImmutable_FailsAndEmitsNothingWrong()
    {
        var result = Compile(NativeFn("let int:x = 1\nset x 2\nreturn x"));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    [Fact]
    public void Converter_ReassignedArrayLocal_BecomesMutableBinding()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            public class Arrays
            {
                public static int Size(int n)
                {
                    int[] xs = new int[3];
                    xs = new int[n];
                    return xs.Length;
                }
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.Contains("var int[]:xs", conversion.CalorSource);
        var result = Compile(conversion.CalorSource!);
        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    // ===================================================================
    // #1608 — operand counts of built-in operations
    // ===================================================================

    [Theory]
    [InlineData("s.IndexOf(\"@\", 3)", "s.IndexOf(\"@\", 3)")]
    [InlineData("s.IndexOf(\"@\", 1, 4)", "s.IndexOf(\"@\", 1, 4)")]
    [InlineData("s.IndexOf(\"@\", 1, StringComparison.Ordinal)", "s.IndexOf(\"@\", 1, StringComparison.Ordinal)")]
    [InlineData("s.IndexOf(\"@\")", "s.IndexOf(\"@\")")]
    public void Emitter_IndexOfStartAndCount_AreEmitted(string expression, string expected)
    {
        var result = Compile(NativeFn($"return {expression}", "(string:s) → int"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Contains(expected, result.GeneratedCode);
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Theory]
    [InlineData("(indexof s \"@\" 1 2 3)", "(str:s) -> i32")]
    [InlineData("(contains s \"@\" 3)", "(str:s) -> bool")]
    [InlineData("(starts s \"@\" 3)", "(str:s) -> bool")]
    [InlineData("(ends s \"@\" 3)", "(str:s) -> bool")]
    [InlineData("(equals s \"@\" 3)", "(str:s) -> bool")]
    [InlineData("(str s 3)", "(str:s) -> str")]
    [InlineData("(< 1 2 3)", "() -> bool")]
    [InlineData("(== 1 1 1)", "() -> bool")]
    [InlineData("(** 2 3 2)", "() -> f64")]
    public void Parser_ExtraOperands_AreCalor0110(string expression, string signature)
    {
        Parse(Fn($"§R {expression}", signature), out var diagnostics);

        Assert.Contains(DiagnosticCode.OperatorArgumentCount, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("a + 2 + 3", "(int:a) → int")]
    [InlineData("b && false && true", "(bool:b) → bool")]
    [InlineData("s.Contains(\"@\")", "(string:s) → bool")]
    [InlineData("a < 2", "(int:a) → bool")]
    public void Parser_ValidOperandCounts_AreAccepted(string expression, string signature)
    {
        var result = Compile(NativeFn($"return {expression}", signature));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Theory]
    [InlineData("new System.Text.StringBuilder()", "new System.Text.StringBuilder()")]
    [InlineData("new System.Text.StringBuilder(\"a\")", "new System.Text.StringBuilder(\"a\")")]
    [InlineData("new System.Text.StringBuilder(\"a\", 64)", "new System.Text.StringBuilder(\"a\", 64)")]
    [InlineData("new System.Text.StringBuilder(16, 64)", "new System.Text.StringBuilder(16, 64)")]
    public void Emitter_StringBuilderNew_EmitsEveryOperand(string expression, string expected)
    {
        var result = Compile(NativeFn($"! alloc\nlet b = {expression}\nreturn b.Length"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Contains(expected, result.GeneratedCode);
    }

    // ===================================================================
    // End-to-end CLI: diagnostic and exit code
    // ===================================================================

    public static IEnumerable<object[]> CliCases() => new[]
    {
        new object[] { "assign-immutable", NativeFn("let int:x = 1\nset x 2\nreturn x"), "Calor0213" },
        new object[] { "assign-loop-var", NativeFn("for i from 0 to 3 step 1\n  set i 5\nreturn 0"), "Calor0213" },
        new object[] { "unknown-visibility", "§M{V}\n  §CL{K:pub}\n    §MT{A:bogus} () -> i32\n      §R 1\n", "Calor0122" },
        new object[] { "extra-operand", Fn("§R (contains s \"@\" 3)", "(str:s) -> bool"), "Calor0110" },
        new object[] { "type-first-header", Fn("§B{W:t} default\n§R 1"), "Calor0123" },
    };

    [Theory]
    [MemberData(nameof(CliCases))]
    public void Cli_RejectedInput_ReportsDiagnosticAndFails(string name, string source, string code)
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-lang27-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, name + ".calr");
            File.WriteAllText(path, source);

            var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(dir, "--input", path, "--output", Path.Combine(dir, name + ".g.cs"));

            Assert.NotEqual(0, exitCode);
            Assert.Contains(code, stdOut + stdErr);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Cli_PositiveControl_CompilesWithExitZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-lang27-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "ok.calr");
            File.WriteAllText(path, NativeFn("var int:x = 1\nset x 2\nreturn \"a@b@\".IndexOf(\"@\", x)"));

            var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(dir, "--input", path, "--output", Path.Combine(dir, "ok.g.cs"));

            Assert.True(exitCode == 0, stdOut + stdErr);
            Assert.Contains("IndexOf(\"@\", x)", File.ReadAllText(Path.Combine(dir, "ok.g.cs")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
