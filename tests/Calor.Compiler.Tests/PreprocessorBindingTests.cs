using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1678 (0.27, epic #1592): the binder binds both branches of a statement-level <c>§PP</c>
/// (<c>#if</c>) block instead of scanning them. Each branch is bound in its own scope (the
/// branches are alternatives); because <c>#if</c> opens no C# scope, the bindings a branch
/// declares stay visible after the block, and when branches disagree the strictest declaration
/// (immutable over mutable) is the one checked. Also: the three short-arity operator tolerances
/// (<c>(?? x)</c>, <c>(? c a)</c>, <c>(== x)</c>) are Calor0110.
/// </summary>
public class PreprocessorBindingTests
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

    private static string Indent(string body, string prefix) =>
        string.Join("\n", body.Split('\n').Select(line => prefix + line));

    private static string Fn(string body, string signature = "() → int") =>
        "module T T block\n  func Run " + signature + "\n" +
        (body.StartsWith('!') ? "" : "    !\n") + Indent(body, "    ") + "\n";

    /// <summary>A method on a class with a writable field <c>x</c>.</summary>
    private static string MethodWithField(string body) =>
        "module T T block\n  class K\n    field int:x\n    func Run () → int\n      ! mut\n" +
        Indent(body, "      ") + "\n";

    // ===================================================================
    // Finding 1: a §PP local with the same name as a writable field
    // ===================================================================

    [Theory]
    [InlineData("pp DEBUG\n  let int:x = 1\nset x 2\nreturn x")]
    [InlineData("pp DEBUG\n  let int:x = 1\nlet y = x\nset x (x + 1)\nreturn y")]
    [InlineData("pp DEBUG\n  let int:a = 0\npp else\n  let int:x = 1\nset x 2\nreturn x")]
    public void Binder_WriteToPreprocessorLocalShadowingAWritableField_IsCalor0213(string body)
    {
        var diagnostics = Bind(MethodWithField(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("set x 2\nreturn x")]
    [InlineData("pp DEBUG\n  var int:x = 1\npp else\n  var int:x = 3\nset x 2\nreturn x")]
    [InlineData("pp DEBUG\n  set x 1\nset x 2\nreturn x")]
    public void Binder_WriteToWritableFieldOrMutablePreprocessorLocal_IsAccepted(string body)
    {
        var diagnostics = Bind(MethodWithField(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // ===================================================================
    // Finding 2: a nested §PP's bindings are visible after it
    // ===================================================================

    [Theory]
    [InlineData("pp A\n  pp B\n    let int:x = 1\n  set x 2\nreturn 0")]
    [InlineData("pp A\n  pp B\n    let int:x = 1\nset x 2\nreturn 0")]
    [InlineData("pp A\n  let int:a = 0\npp else\n  pp B\n    let int:x = 1\n  pp else\n    let int:x = 2\nset x 3\nreturn 0")]
    [InlineData("pp A\n  pp B\n    pp C\n      let int:x = 1\n    set x 2\nreturn 0")]
    public void Binder_WriteToNestedPreprocessorImmutableBinding_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("pp A\n  pp B\n    let int:x = 1\n  let y = (x + 1)\nreturn 0")]
    [InlineData("pp A\n  pp B\n    var int:x = 1\n  set x 2\nreturn 0")]
    [InlineData("pp A\n  pp B\n    var int:x = 1\nset x 2\nreturn x")]
    public void Binder_NestedPreprocessorBindings_AreVisibleAfterward(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
        Assert.DoesNotContain(DiagnosticCode.UndefinedReference, ErrorCodes(diagnostics));
    }

    // ===================================================================
    // Finding 3: §B{~x} after a §PP that declared an immutable x
    // ===================================================================

    [Theory]
    [InlineData("pp DEBUG\n  let int:x = 1\nvar x = 2\nreturn x")]
    [InlineData("pp DEBUG\n  var int:x = 1\npp else\n  let int:x = 2\nvar x = 3\nreturn x")]
    [InlineData("pp A\n  pp B\n    let int:x = 1\nvar x = 2\nreturn x")]
    [InlineData("let int:x = 1\npp DEBUG\n  var x = 2\nreturn x")]
    public void Binder_MutableRebindOfPreprocessorImmutableBinding_IsCalor0260(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.BindReassignsImmutable, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("pp DEBUG\n  var int:x = 1\nvar x = 2\nreturn x")]
    [InlineData("pp DEBUG\n  var int:x = 1\npp else\n  var int:x = 2\nvar x = 3\nreturn x")]
    [InlineData("var int:x = 1\npp DEBUG\n  var x = 2\nvar x = 3\nreturn x")]
    public void Binder_MutableRebindOfPreprocessorMutableBinding_IsAccepted(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.DoesNotContain(DiagnosticCode.BindReassignsImmutable, ErrorCodes(diagnostics));
        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // ===================================================================
    // Both branches are checked, each in its own scope
    // ===================================================================

    [Theory]
    [InlineData("let int:y = 1\npp DEBUG\n  let int:a = 0\npp else\n  set y 2\nreturn y")]
    [InlineData("let int:y = 1\npp DEBUG\n  set y 2\npp else\n  let int:a = 0\nreturn y")]
    [InlineData("pp DEBUG\n  let int:a = 0\npp else\n  let int:z = 1\n  set z 2\nreturn 0")]
    [InlineData("pp DEBUG\n  let int:a = 0\npp else\n  for j from 0 to 3 step 1\n    set j 2\nreturn 0")]
    [InlineData("let int:y = 1\npp A\n  let int:a = 0\npp else\n  pp B\n    let int:b = 0\n  pp else\n    set y 2\nreturn y")]
    public void Binder_WriteToReadOnlyLocalInEitherBranch_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_ElseBranchDoesNotSeeTheIfBranchBinding()
    {
        // The #else branch's §B{~x} is a fresh declaration, not a rebind of the #if branch's x.
        var diagnostics = Bind(Fn("pp DEBUG\n  let int:x = 1\npp else\n  var int:x = 2\nreturn 0"));

        Assert.DoesNotContain(DiagnosticCode.BindReassignsImmutable, ErrorCodes(diagnostics));
        Assert.DoesNotContain(DiagnosticCode.DuplicateDefinition, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_BranchReadOfAnUndefinedName_IsReported()
    {
        // The branch is bound now, so a typo inside it is no longer silently accepted.
        var diagnostics = Bind(Fn("pp DEBUG\n  let y = (nope + 1)\nreturn 0"));

        Assert.Contains(DiagnosticCode.UndefinedReference, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Compile_BranchBindingThatRedeclaresAnEnclosingLocal_IsRejected()
    {
        // `int x = 0; #if A int x = 1; #endif` is CS0128 whenever A is defined.
        var result = Compile(Fn("let int:x = 0\npp A\n  let int:x = 1\nreturn x"));

        Assert.Contains(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.BindDuplicateInScope);
    }

    // Review round 1: an immutable §B wins over a collection statement declaring the same name.
    [Theory]
    [InlineData("! alloc\npp A\n  let List<int>:x = new System.Collections.Generic.List<int>() { 1 }\npp else\n  let List<int>:x = new List<int>()\nset x x\nreturn 0")]
    [InlineData("! alloc\npp A\n  let List<int>:x = new List<int>()\npp else\n  let List<int>:x = new System.Collections.Generic.List<int>() { 1 }\nset x x\nreturn 0")]
    public void Compile_WriteAfterBranchesMixingCollectionAndImmutableBinding_IsRejected(string body)
    {
        var result = Compile(Fn(body));

        Assert.True(result.HasErrors, "expected a §PP-related error");
        Assert.Contains(result.Diagnostics.Errors, e =>
            e.Code is DiagnosticCode.WriteToReadOnlyLocal or DiagnosticCode.DuplicateDefinition);
    }

    // Review round 1: the code after the block has one variable, so its type cannot depend on
    // the active branch (a refinement guard would be emitted for only one of them).
    [Theory]
    [InlineData("pp A\n  var Nat:x = n\npp else\n  var int:x = 2\nset x -1\nreturn x")]
    [InlineData("pp A\n  var int:x = 2\npp else\n  var Nat:x = n\nset x -1\nreturn x")]
    [InlineData("pp A\n  let string:x = \"a\"\npp else\n  let int:x = 2\nreturn 0")]
    public void Binder_BranchesDeclaringOneNameWithDifferentTypes_AreCalor0201(string body)
    {
        var source = "module T T block\n  func Run (Nat:n) → int\n    !\n" +
            Indent(body, "    ") + "\n";
        var diagnostics = Bind(source);

        Assert.Contains(diagnostics.Errors, e =>
            e.Code == DiagnosticCode.DuplicateDefinition && e.Message.Contains("different types"));
    }

    // Review round 1: a branch local may shadow a flow-narrowed field, as it may outside §PP.
    [Fact]
    public void Converter_PreprocessorLocalShadowingANarrowedField_Compiles()
    {
        var conversion = new Migration.CSharpToCalorConverter().Convert("""
            public class K
            {
                public string? x;
                public int Run()
                {
                    if (x != null)
                    {
            #if A
                        var x = "a";
            #else
                        var x = "b";
            #endif
                        return x.Length;
                    }
                    return 0;
                }
            }
            """);

        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        var result = Compile(conversion.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    // Review round 1: a call through a binding hoisted from §PP may run either branch's value.
    [Theory]
    [InlineData("let Action:later = Noop", "let Action:later = (mut) => Mutate(ref value)")]
    [InlineData("let Action:later = (mut) => Mutate(ref value)", "let Action:later = Noop")]
    public void Binder_CallThroughHoistedCallable_SeesEveryBranchMutation(string first, string second)
    {
        var source = $$"""
            module PpCallable PpCallable block
              func Noop () → void
                !
                return
              func Mutate (ref string?:value) → void
                !
                set value null
              func Take (string:value) → void
                !
                return
              func Probe (string?:value) → void
                !
                if (value != null)
                  pp A
                    {{first}}
                  pp else
                    {{second}}
                  later()
                  Take(value)
            """;
        var module = Parse(source, out var diagnostics);
        new Binder(diagnostics).Bind(module);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    // Review round 2: a fact every declaring branch established for its binding survives the block.
    [Fact]
    public void Binder_BranchNarrowingOfAHoistedBinding_SurvivesWhenEveryBranchEstablishesIt()
    {
        var diagnostics = Bind(NarrowingProbe(
            "pp A\n  let string?:x = input\n  if string.IsNullOrEmpty(x)\n    return\npp else\n  let string?:x = input\n  if string.IsNullOrEmpty(x)\n    return"));

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("pp A\n  let string?:x = input\n  if string.IsNullOrEmpty(x)\n    return\npp else\n  let string?:x = input")]
    [InlineData("pp A\n  let string?:x = input\npp else\n  let string?:x = input\n  if string.IsNullOrEmpty(x)\n    return")]
    [InlineData("pp A\n  var string?:x = input\n  if string.IsNullOrEmpty(x)\n    return\npp else\n  var string?:x = input\n  if string.IsNullOrEmpty(x)\n    return\nset x null")]
    public void Binder_BranchNarrowingNotEstablishedEverywhereOrInvalidated_IsCalor0274(string body)
    {
        var diagnostics = Bind(NarrowingProbe(body));

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    private static string NarrowingProbe(string body) =>
        "module P P block\n  func Take (string:value) → void\n    !\n    return\n  func Probe (string?:input) → void\n    !\n" +
        Indent(body + "\nTake(x)", "    ") + "\n";

    // Review round 2: a write inside a §PP inside a deferred callable is part of its summary.
    [Theory]
    [InlineData("pp A\n  set value null\npp else\n  set value null")]
    [InlineData("pp A\n  set value null")]
    [InlineData("pp A\n  let int:k = 0\npp else\n  pp B\n    set value null")]
    public void Binder_WriteInsidePreprocessorInsideLambda_ReachesTheCallSite(string lambdaBody)
    {
        var source = "module P P block\n  func Take (string:value) → int\n    !\n    return value.Length\n" +
            "  func Probe (string?:input) → void\n    !\n    var string?:value = input\n" +
            "    if (value != null)\n      let Action:later = (mut) =>\n" +
            Indent(lambdaBody, "        ") + "\n      later()\n      Take(value)\n";
        var module = Parse(source, out var diagnostics);
        new Binder(diagnostics).Bind(module);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    // Review round 2: §PP blocks beneath an opaque parent statement still hoist their bindings.
    [Theory]
    [InlineData("! unsafe\nunsafe\n  pp A\n    let int:x = 1\n  pp else\n    let int:x = 2\n  set x 3\nreturn 0")]
    [InlineData("! unsafe\nunsafe\n  pp A\n    pp B\n      let int:x = 1\n  set x 3\nreturn 0")]
    [InlineData("! unsafe\nunsafe\n  pp A\n    var int:x = 1\n  pp else\n    let int:x = 2\n  set x 3\nreturn 0")]
    public void Compile_WriteAfterPreprocessorBeneathOpaqueParent_IsCalor0213(string body)
    {
        var result = Compile(Fn(body));

        Assert.Contains(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    [Fact]
    public void Compile_WriteAfterPreprocessorBeneathOpaqueParentToMutableBinding_IsAccepted()
    {
        var result = Compile(Fn("! unsafe\nunsafe\n  pp A\n    var int:x = 1\n  pp else\n    var int:x = 2\n  set x 3\nreturn 0"));

        Assert.DoesNotContain(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.WriteToReadOnlyLocal);
    }

    // Review round 3: a branch binding that hides an outer name must be declared on every path,
    // or after the block the name means the outer variable on the other path.
    [Theory]
    [InlineData("pp A\n  let string:x = \"safe\"\nreturn Take(x)")]
    [InlineData("pp A\n  var string:x = \"safe\"\nreturn 0")]
    [InlineData("pp A\n  let int:k = 0\npp else\n  let string:x = \"safe\"\nreturn 0")]
    public void Binder_BranchBindingHidingAMemberOnSomePaths_IsCalor0201(string body)
    {
        var source = "module T T block\n  class K\n    field string?:x\n    func Take (string:value) → int\n      !\n      return value.Length\n" +
            "    func Run () → int\n      !\n" + Indent(body, "      ") + "\n";
        var diagnostics = Bind(source);

        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.DuplicateDefinition && e.Message.Contains("not every branch"));
    }

    [Fact]
    public void Binder_BranchBindingRedeclaringAParameter_IsCalor0201()
    {
        var diagnostics = Bind(Fn("pp A\n  let int:n = 1\nreturn n", "(int:n) → int"));

        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.DuplicateDefinition);
    }

    [Fact]
    public void Binder_BranchBindingsHidingAMemberOnEveryPath_AreAccepted()
    {
        var source = "module T T block\n" +
            "  class K\n" +
            "    field string?:x\n" +
            "    func Take (string:value) → int\n" +
            "      !\n" +
            "      return value.Length\n" +
            "\n" +
            "    func Run () → int\n" +
            "      !\n" +
            "      pp A\n" +
            "        let string:x = \"a\"\n" +
            "      pp else\n" +
            "        let string:x = \"b\"\n" +
            "      return Take(x)\n";
        var diagnostics = Bind(source);

        Assert.DoesNotContain(diagnostics.Errors, e => e.Code == DiagnosticCode.DuplicateDefinition);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    // Review round 3: a callable captured in a branch that was not picked still mutates the
    // hoisted variable.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Binder_CallableCapturedInEitherBranch_InvalidatesTheHoistedNarrowing(bool captureInElse)
    {
        var capture = "set later (mut) =>\n  set x null";
        var ifBranch = "var string?:x = input\nif string.IsNullOrEmpty(x)\n  return 0" + (captureInElse ? "" : "\n" + capture);
        var elseBranch = "var string?:x = input\nif string.IsNullOrEmpty(x)\n  return 0" + (captureInElse ? "\n" + capture : "");
        var source = "module T T block\n  func Take (string:value) → int\n    !\n    return value.Length\n" +
            "  func Probe (string?:input) → int\n    !\n    var Action:later = (nop) =>\n      let int:k = 0\n" +
            "    pp A\n" + Indent(ifBranch, "      ") + "\n    pp else\n" + Indent(elseBranch, "      ") + "\n" +
            "    later()\n    return Take(x)\n";
        var diagnostics = Bind(source);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    // ===================================================================
    // Positive controls: valid §PP code compiles and the C# is right
    // ===================================================================

    [Theory]
    [InlineData("pp DEBUG\n  let int:x = 1\npp else\n  let int:x = 2\nlet y = (x + 1)\nreturn y", "int x = 2;")]
    [InlineData("pp DEBUG\n  var int:x = 1\npp else\n  var int:x = 2\nset x 3\nreturn x", "int x = 2;")]
    [InlineData("pp A\n  var int:x = 1\npp else\n  pp B\n    var int:x = 2\n  pp else\n    var int:x = 3\nset x 4\nvar x = 5\nreturn x", "int x = 3;")]
    [InlineData("var int:n = 0\npp DEBUG\n  set n 1\npp else\n  var n = 2\nreturn n", "n = 2;")]
    [InlineData("for i from 0 to 3 step 1\n  pp DEBUG\n    let int:k = i\n  pp else\n    let int:k = 0\nreturn 0", "int k = 0;")]
    public void Compile_ValidPreprocessorCode_CompilesWithBothBranches(string body, string elseBranchCode)
    {
        var result = Compile(Fn(body));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains("#else", result.GeneratedCode);
        Assert.Contains(elseBranchCode, result.GeneratedCode);
        // The generated C# compiles with no preprocessor symbols defined (the #else side).
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Fact]
    public void Emitter_ElseBranchMutableBinding_IsADeclarationNotAnAssignment()
    {
        var result = Compile(Fn("pp DEBUG\n  var int:x = 1\npp else\n  var int:x = 2\nreturn x"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains("int x = 1;", result.GeneratedCode);
        Assert.Contains("int x = 2;", result.GeneratedCode);
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Fact]
    public void Compile_WriteToFieldAfterPreprocessorWithoutLocal_Compiles()
    {
        var result = Compile(MethodWithField("pp DEBUG\n  set x 1\nset x 2\nreturn x"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Theory]
    [InlineData("s ?? \"d\"", "(string?:s) → string", "s ?? \"d\"")]
    [InlineData("b ? 1 : 2", "(bool:b) → int", "b ? 1 : 2")]
    [InlineData("s == null", "(string?:s) → bool", "s == null")]
    [InlineData("s != null", "(string?:s) → bool", "s != null")]
    public void Parser_FullOperandCounts_AreAccepted(string expression, string signature, string expected)
    {
        var result = Compile(Fn($"return {expression}", signature));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains(expected, result.GeneratedCode);
    }

    // ===================================================================
    // End-to-end CLI: diagnostic and exit code
    // ===================================================================

    public static IEnumerable<object[]> CliCases() => new[]
    {
        new object[] { "pp-shadows-field", MethodWithField("pp DEBUG\n  let int:x = 1\nset x 2\nreturn x"), "Calor0213" },
        new object[] { "pp-nested", Fn("pp A\n  pp B\n    let int:x = 1\n  set x 2\nreturn 0"), "Calor0213" },
        new object[] { "pp-rebind-immutable", Fn("pp DEBUG\n  let int:x = 1\nvar x = 2\nreturn x"), "Calor0260" },
        new object[] { "pp-else-branch-write", Fn("let int:y = 1\npp DEBUG\n  let int:a = 0\npp else\n  set y 2\nreturn y"), "Calor0213" },
    };

    [Theory]
    [MemberData(nameof(CliCases))]
    public void Cli_RejectedInput_ReportsDiagnosticAndFails(string name, string source, string code)
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-pp27-" + Guid.NewGuid().ToString("N")[..8]);
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

    public static IEnumerable<object[]> CliPositiveCases() => new[]
    {
        new object[] { "pp-both-branches", Fn("pp DEBUG\n  var int:x = 1\npp else\n  var int:x = 2\nset x 3\nreturn x"), "int x = 2;" },
        new object[] { "pp-field-write", MethodWithField("pp DEBUG\n  set x 1\nset x 2\nreturn x"), "x = 2;" },
        new object[] { "pp-elif-chain", Fn("pp A\n  let int:x = 1\npp else\n  pp B\n    let int:x = 2\n  pp else\n    let int:x = 3\nreturn x"), "int x = 3;" },
    };

    [Theory]
    [MemberData(nameof(CliPositiveCases))]
    public void Cli_PositiveControl_CompilesWithExitZero(string name, string source, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "calor-pp27-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, name + ".calr");
            File.WriteAllText(path, source);

            var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(dir, "--input", path, "--output", Path.Combine(dir, name + ".g.cs"));

            Assert.True(exitCode == 0, stdOut + stdErr);
            Assert.Contains(expected, File.ReadAllText(Path.Combine(dir, name + ".g.cs")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
