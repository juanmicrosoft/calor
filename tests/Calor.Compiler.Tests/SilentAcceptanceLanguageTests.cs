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

    private static string Fn(string body, string signature = "() -> i32") =>
        "§M{m1:T}\n  §F{f1:Run:pub} " + signature + "\n" +
        string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n";

    // ===================================================================
    // #1607 — unknown visibility words
    // ===================================================================

    [Theory]
    [InlineData("§MT{m2:A:bogus} () -> i32")]
    [InlineData("§MT{m2:A:prv} () -> i32")]
    [InlineData("§MT{m2:A:i32} () -> i32")]
    public void Parser_UnknownMethodVisibility_IsCalor0122ListingValidWords(string header)
    {
        Parse($"§M{{m1:V}}\n  §CL{{c1:K:pub}}\n    {header}\n      §R 1\n", out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.UnknownVisibility);
        Assert.Contains("pub/public", error.Message);
        Assert.Contains("priv-prot/private-protected", error.Message);
    }

    [Theory]
    [InlineData("§F{f1:Run:bogus} () -> i32\n    §R 1")]
    [InlineData("§CL{c1:K:pub}\n    §FLD{i32:x:bogus}")]
    [InlineData("§CL{c1:K:pub}\n    §CTOR{ctor1:bogus}")]
    [InlineData("§CL{c1:K:pub}\n    §PROP{p1:X:i32:bogus:get}")]
    [InlineData("§CL{c1:K:pub}\n    §EVT{e1:Click:bogus:EventHandler}")]
    public void Parser_UnknownVisibility_IsRejectedOnEveryDeclarationKind(string member)
    {
        Parse($"§M{{m1:V}}\n  {member}\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("pub", Visibility.Public)]
    [InlineData("public", Visibility.Public)]
    [InlineData("int", Visibility.Internal)]
    [InlineData("prot", Visibility.Protected)]
    [InlineData("pro", Visibility.Protected)]
    [InlineData("protected", Visibility.Protected)]
    [InlineData("priv", Visibility.Private)]
    [InlineData("pri", Visibility.Private)]
    [InlineData("private", Visibility.Private)]
    [InlineData("prot-int", Visibility.ProtectedInternal)]
    [InlineData("protected-internal", Visibility.ProtectedInternal)]
    [InlineData("priv-prot", Visibility.PrivateProtected)]
    [InlineData("private-protected", Visibility.PrivateProtected)]
    public void Parser_EveryValidVisibilityWord_ParsesToItsLevel(string word, Visibility expected)
    {
        var module = Parse($"§M{{m1:V}}\n  §CL{{c1:K:pub}}\n    §MT{{m2:A:{word}}} () -> i32\n      §R 1\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(expected, module.Classes.Single().Methods.Single().Visibility);
    }

    // Review round 1: compact headers must not swallow an unknown visibility word.
    [Theory]
    [InlineData("§MT{Run:prv} () -> i32\n      §R 1")]
    [InlineData("§MT{Run:prv:virt} () -> i32\n      §R 1")]
    [InlineData("§MT{Run:prv:} () -> i32\n      §R 1")]
    [InlineData("§CTOR{prv}")]
    public void Parser_CompactHeaderWithUnknownVisibility_IsCalor0122(string member)
    {
        Parse($"§M{{m1:V}}\n  §CL{{c1:K:pub}}\n    {member}\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§MT{m1:Run:bogus} () -> i32")]
    [InlineData("§MT{m1:Run:priv} () -> i32")]
    [InlineData("§MT{Run:prv} () -> i32")]
    public void Parser_InterfaceMethodNonPublicOrUnknownVisibility_IsCalor0122(string member)
    {
        Parse($"§M{{m1:V}}\n  §IFACE{{i1:IRun}}\n    {member}\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§MT{m1:Run} () -> i32")]
    [InlineData("§MT{m1:Run:pub} () -> i32")]
    [InlineData("§MT{Run:pub} () -> i32")]
    public void Parser_InterfaceMethodPublicOrOmittedVisibility_Parses(string member)
    {
        var module = Parse($"§M{{m1:V}}\n  §IFACE{{i1:IRun}}\n    {member}\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal("Run", module.Interfaces.Single().Methods.Single().Name);
    }

    [Fact]
    public void Parser_CompactAbstractMethodWithUnknownVisibility_IsCalor0122()
    {
        Parse("§M{m1:V}\n  §CL{c1:K:pub:abs}\n    §AMT{Run:prv} () -> i32\n", out var diagnostics);

        Assert.Contains(DiagnosticCode.UnknownVisibility, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§MT{Run:pub} () -> i32\n      §R 1", "Run", Visibility.Public)]
    [InlineData("§MT{m2:Run} () -> i32\n      §R 1", "Run", Visibility.Private)]
    [InlineData("§MT{Run:prot:virt} () -> i32\n      §R 1", "Run", Visibility.Protected)]
    public void Parser_CompactMethodHeaders_PositiveControls(string member, string name, Visibility visibility)
    {
        var module = Parse($"§M{{m1:V}}\n  §CL{{c1:K:pub}}\n    {member}\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        var method = module.Classes.Single().Methods.Single();
        Assert.Equal(name, method.Name);
        Assert.Equal(visibility, method.Visibility);
    }

    [Fact]
    public void Parser_ModifierInVisibilitySlot_PointsAtTheModifierField()
    {
        Parse("§M{m1:V}\n  §CL{c1:K:pub:abs}\n    §MT{m2:A:abs} () -> i32\n", out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.UnknownVisibility);
        Assert.Contains("':pub:abs'", error.Message);
    }

    [Fact]
    public void Parser_OmittedVisibility_StillDefaultsWithoutDiagnostic()
    {
        var module = Parse("§M{m1:V}\n  §CL{c1:K:pub}\n    §FLD{i32:x}\n    §MT{m2:A} () -> i32\n      §R 1\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(Visibility.Private, module.Classes.Single().Fields.Single().Visibility);
    }

    [Fact]
    public void Parser_CompactEventHeader_AsDocumented_Parses()
    {
        var module = Parse("§M{m1:V}\n  §CL{c1:K:pub}\n    §EVT{Click:pub:EventHandler}\n", out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors.Select(e => e.Message)));
        var evt = module.Classes.Single().Events.Single();
        Assert.Equal("Click", evt.Name);
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.Equal("EventHandler", evt.DelegateType);
    }

    [Fact]
    public void Emitter_ProtectedShortForm_EmitsProtectedNotPrivate()
    {
        var result = Compile("§M{m1:V}\n  §CL{c1:K:pub}\n    §MT{m2:A:pro} () -> i32\n      §R 1\n");

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
    [InlineData("long", "t")]
    [InlineData("nint", "p")]
    [InlineData("T[]", "items")]
    [InlineData("i32?", "x")]
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
    [InlineData("t:W", "t", "W")]           // name-first, the suggested fix
    [InlineData("~t:W", "t", "W")]          // mutable name-first
    [InlineData("Widget:w", "w", "Widget")] // PascalCase type-first
    [InlineData("i32:x", "x", "INT")]       // built-in type-first
    [InlineData("x:T", "x", "T")]           // generic parameter, name-first
    [InlineData("N:i32", "N", "INT")]       // upper-case name, built-in type
    [InlineData("N:long", "N", "long")]     // upper-case name, keyword type
    [InlineData("W:t:const", "t", "W")]     // explicit type-first form
    public void Parser_UnambiguousHeaders_ParseWithoutCalor0123(string header, string name, string type)
    {
        var module = Parse(Fn($"§B{{{header}}} default\n§R 1"), out var diagnostics);

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
        Assert.Contains("§B{item:T}", conversion.CalorSource);
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains("T item =", result.GeneratedCode);
    }

    // ===================================================================
    // #1550 — writes to immutable bindings and loop variables
    // ===================================================================

    [Theory]
    [InlineData("§B{x:i32} 1\n§ASSIGN x 2\n§R x")]
    [InlineData("§B{x} 1\n§ASSIGN x (+ x 1)\n§R x")]
    [InlineData("§B{x:i32} 1\n§B{y} (inc x)\n§R x")]
    [InlineData("§B{x:i32} 1\n§B{y} (post-dec x)\n§R x")]
    [InlineData("§L{l1:i:0:3:1}\n  §ASSIGN i 5\n§R 0")]
    [InlineData("§L{l1:i:0:3:1}\n  §B{~i:i32} 9\n§R 0")]
    [InlineData("§L{l1:i:0:3:1}\n  §B{y} (inc i)\n§R 0")]
    public void Binder_WriteToReadOnlyLocal_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // Review round 1: statements the binder does not bind still may not write a read-only local.
    [Theory]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §ASSIGN x 2\n§R x")]
    [InlineData("§L{l1:i:0:3:1}\n  §PP{DEBUG}\n    §ASSIGN i 5\n§R 0")]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §B{~x} 2\n§R x")]
    [InlineData("§E{mut}\n§B{x:i32} 1\n§B{~xs:List<i32>} §NEW{List<i32>}\n§PUSH{xs} (inc x)\n§R x")]
    public void Binder_WriteInsideUnboundStatement_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // Review round 2: tuple targets, and locals declared inside an unbound statement.
    [Theory]
    [InlineData("§B{x:i32} 1\n§B{~y:i32} 2\n§ASSIGN (x, y) (3, 4)\n§R x")]
    [InlineData("§B{~y:i32} 2\n§L{l1:i:0:3:1}\n  §ASSIGN (y, i) (3, 4)\n§R y")]
    [InlineData("§PP{DEBUG}\n  §B{z:i32} 1\n  §ASSIGN z 2\n§R 0")]
    [InlineData("§PP{DEBUG}\n  §L{l1:j:0:3:1}\n    §ASSIGN j 2\n§R 0")]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §ASSIGN (x, x) (2, 3)\n§R x")]
    public void Binder_TupleAndNestedDeclarationWrites_AreCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§B{~x:i32} 1\n§B{~y:i32} 2\n§ASSIGN (x, y) (y, x)\n§R x")]
    [InlineData("§PP{DEBUG}\n  §B{~z:i32} 1\n  §ASSIGN z 2\n§R 0")]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §B{~w:i32} 1\n  §ASSIGN w x\n§R x")]
    public void Binder_TupleAndNestedDeclarationWritesToMutable_AreAccepted(string body)
    {
        var diagnostics = Bind(Fn(body));

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
        Assert.Contains("§B{~m", conversion.CalorSource);
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
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§ASSIGN x 2\n§R 0")]
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§B{y} (inc x)\n§R 0")]
    [InlineData("§PP{DEBUG}\n  §B{v:i32} 1\n§C{Bump} §A{ref} v §/C\n§R 0")]
    public void Binder_WriteAfterPreprocessorBlockToItsImmutableBinding_IsCalor0213(string body)
    {
        var module = Parse(WithBump(body, "ref"), out var diagnostics);
        new Binder(diagnostics).Bind(module);

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§ASSIGN x 2\n§R 0")]
    [InlineData("§PP{DEBUG}\n  §B{~v:i32} 1\n§C{Bump} §A{ref} v §/C\n§R 0")]
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
    [InlineData("§B{~x:i32} 1\n§PP{DEBUG}\n  §ASSIGN x 2\n§R x")]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §B{~y:i32} 0\n  §ASSIGN y x\n§R x")]
    [InlineData("§E{mut}\n§B{~x:i32} 1\n§B{~xs:List<i32>} §NEW{List<i32>}\n§PUSH{xs} (inc x)\n§R x")]
    public void Binder_WriteInsideUnboundStatementToMutableLocal_IsAccepted(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§B{~x:i32} 1\n§ASSIGN x 2\n§R x")]
    [InlineData("§B{~x} 1\n§ASSIGN x (+ x 1)\n§R x")]
    [InlineData("§B{~x:i32} 1\n§B{y} (inc x)\n§R x")]
    [InlineData("§B{~x:i32} 1\n§B{~x} 5\n§R x")]
    [InlineData("§B{~s} 0\n§L{l1:i:0:3:1}\n  §ASSIGN s (+ s i)\n§R s")]
    [InlineData("§LIST{xs:i32}\n  1\n§ASSIGN xs xs\n§R 0")]
    public void Binder_WritesToMutableLocals_AreAccepted(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_AssignToParameter_IsAccepted()
    {
        var diagnostics = Bind(Fn("§ASSIGN n 2\n§R n", "(i32:n) -> i32"));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    private static string WithBump(string callerBody, string modifier) =>
        "§M{m1:T}\n" +
        $"  §F{{f0:Bump:pub}} (i32:v:{modifier}) -> void\n" +
        (modifier == "in" ? "    §R\n" : "    §ASSIGN v 1\n") +
        "  §F{f1:Run:pub} () -> i32\n" +
        string.Join("\n", callerBody.Split('\n').Select(line => "    " + line)) + "\n";

    [Theory]
    [InlineData("§B{v:i32} 1\n§C{Bump} §A{ref} v §/C\n§R v", "ref")]
    [InlineData("§B{v:i32} 1\n§C{Bump} §A{out} v §/C\n§R v", "out")]
    [InlineData("§L{l1:i:0:3:1}\n  §C{Bump} §A{ref} i §/C\n§R 0", "ref")]
    public void Binder_RefOrOutOfReadOnlyLocal_IsCalor0213(string body, string modifier)
    {
        var diagnostics = Bind(WithBump(body, modifier));

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
        Assert.Contains($"as a '{modifier}' argument", error.Message);
    }

    [Theory]
    [InlineData("§B{~v:i32} 1\n§C{Bump} §A{ref} v §/C\n§R v", "ref")]
    [InlineData("§B{~v:i32} 1\n§C{Bump} §A{out} v §/C\n§R v", "out")]
    [InlineData("§B{v:i32} 1\n§C{Bump} §A{in} v §/C\n§R v", "in")]
    public void Binder_RefOutOfMutableOrInOfImmutable_IsAccepted(string body, string modifier)
    {
        var diagnostics = Bind(WithBump(body, modifier));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Compile_AssignToImmutable_FailsAndEmitsNothingWrong()
    {
        var result = Compile(Fn("§B{x:i32} 1\n§ASSIGN x 2\n§R x"));

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
        Assert.Contains("§B{~xs:", conversion.CalorSource);
        var result = Compile(conversion.CalorSource!);
        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    // ===================================================================
    // #1608 — operand counts of built-in operations
    // ===================================================================

    [Theory]
    [InlineData("(indexof s \"@\" 3)", "s.IndexOf(\"@\", 3)")]
    [InlineData("(indexof s \"@\" 1 4)", "s.IndexOf(\"@\", 1, 4)")]
    [InlineData("(indexof s \"@\" 1 :ordinal)", "s.IndexOf(\"@\", 1, StringComparison.Ordinal)")]
    [InlineData("(indexof s \"@\")", "s.IndexOf(\"@\")")]
    public void Emitter_IndexOfStartAndCount_AreEmitted(string expression, string expected)
    {
        var result = Compile(Fn($"§R {expression}", "(str:s) -> i32"));

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
    [InlineData("(+ a 2 3)", "(i32:a) -> i32")]
    [InlineData("(&& b false true)", "(bool:b) -> bool")]
    [InlineData("(contains s \"@\")", "(str:s) -> bool")]
    [InlineData("(< a 2)", "(i32:a) -> bool")]
    public void Parser_ValidOperandCounts_AreAccepted(string expression, string signature)
    {
        var result = Compile(Fn($"§R {expression}", signature));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Theory]
    [InlineData("(sb-new)", "new System.Text.StringBuilder()")]
    [InlineData("(sb-new \"a\")", "new System.Text.StringBuilder(\"a\")")]
    [InlineData("(sb-new \"a\" 64)", "new System.Text.StringBuilder(\"a\", 64)")]
    [InlineData("(sb-new 16 64)", "new System.Text.StringBuilder(16, 64)")]
    public void Emitter_StringBuilderNew_EmitsEveryOperand(string expression, string expected)
    {
        var result = Compile(Fn($"§E{{alloc}}\n§B{{b}} {expression}\n§R (sb-length b)"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.Contains(expected, result.GeneratedCode);
    }

    // ===================================================================
    // End-to-end CLI: diagnostic and exit code
    // ===================================================================

    public static IEnumerable<object[]> CliCases() => new[]
    {
        new object[] { "assign-immutable", Fn("§B{x:i32} 1\n§ASSIGN x 2\n§R x"), "Calor0213" },
        new object[] { "assign-loop-var", Fn("§L{l1:i:0:3:1}\n  §ASSIGN i 5\n§R 0"), "Calor0213" },
        new object[] { "unknown-visibility", "§M{m1:V}\n  §CL{c1:K:pub}\n    §MT{m2:A:bogus} () -> i32\n      §R 1\n", "Calor0122" },
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
            File.WriteAllText(path, Fn("§B{~x:i32} 1\n§ASSIGN x 2\n§R (indexof \"a@b@\" \"@\" x)"));

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
