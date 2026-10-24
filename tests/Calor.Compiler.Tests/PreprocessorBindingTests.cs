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

    private static string Fn(string body, string signature = "() -> i32") =>
        "§M{m1:T}\n  §F{f1:Run:pub} " + signature + "\n" + Indent(body, "    ") + "\n";

    /// <summary>A method on a class with a writable field <c>x</c>.</summary>
    private static string MethodWithField(string body) =>
        "§M{m1:T}\n  §CL{c1:K:pub}\n    §FLD{i32:x:pub}\n    §MT{m2:Run:pub} () -> i32\n" +
        Indent(body, "      ") + "\n";

    // ===================================================================
    // Finding 1: a §PP local with the same name as a writable field
    // ===================================================================

    [Theory]
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§ASSIGN x 2\n§R x")]
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§B{y} (inc x)\n§R y")]
    [InlineData("§PP{DEBUG}\n  §B{a:i32} 0\n§PPE\n  §B{x:i32} 1\n§ASSIGN x 2\n§R x")]
    public void Binder_WriteToPreprocessorLocalShadowingAWritableField_IsCalor0213(string body)
    {
        var diagnostics = Bind(MethodWithField(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§ASSIGN x 2\n§R x")]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§ASSIGN x 2\n§R x")]
    [InlineData("§PP{DEBUG}\n  §ASSIGN x 1\n§ASSIGN x 2\n§R x")]
    public void Binder_WriteToWritableFieldOrMutablePreprocessorLocal_IsAccepted(string body)
    {
        var diagnostics = Bind(MethodWithField(body));

        Assert.DoesNotContain(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    // ===================================================================
    // Finding 2: a nested §PP's bindings are visible after it
    // ===================================================================

    [Theory]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{x:i32} 1\n  §ASSIGN x 2\n§R 0")]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{x:i32} 1\n§ASSIGN x 2\n§R 0")]
    [InlineData("§PP{A}\n  §B{a:i32} 0\n§PPE\n  §PP{B}\n    §B{x:i32} 1\n  §PPE\n    §B{x:i32} 2\n§ASSIGN x 3\n§R 0")]
    [InlineData("§PP{A}\n  §PP{B}\n    §PP{C}\n      §B{x:i32} 1\n    §ASSIGN x 2\n§R 0")]
    public void Binder_WriteToNestedPreprocessorImmutableBinding_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{x:i32} 1\n  §B{y} (+ x 1)\n§R 0")]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{~x:i32} 1\n  §ASSIGN x 2\n§R 0")]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{~x:i32} 1\n§ASSIGN x 2\n§R x")]
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
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§B{~x} 2\n§R x")]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§PPE\n  §B{x:i32} 2\n§B{~x} 3\n§R x")]
    [InlineData("§PP{A}\n  §PP{B}\n    §B{x:i32} 1\n§B{~x} 2\n§R x")]
    [InlineData("§B{x:i32} 1\n§PP{DEBUG}\n  §B{~x} 2\n§R x")]
    public void Binder_MutableRebindOfPreprocessorImmutableBinding_IsCalor0260(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.BindReassignsImmutable, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§B{~x} 2\n§R x")]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§PPE\n  §B{~x:i32} 2\n§B{~x} 3\n§R x")]
    [InlineData("§B{~x:i32} 1\n§PP{DEBUG}\n  §B{~x} 2\n§B{~x} 3\n§R x")]
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
    [InlineData("§B{y:i32} 1\n§PP{DEBUG}\n  §B{a:i32} 0\n§PPE\n  §ASSIGN y 2\n§R y")]
    [InlineData("§B{y:i32} 1\n§PP{DEBUG}\n  §ASSIGN y 2\n§PPE\n  §B{a:i32} 0\n§R y")]
    [InlineData("§PP{DEBUG}\n  §B{a:i32} 0\n§PPE\n  §B{z:i32} 1\n  §ASSIGN z 2\n§R 0")]
    [InlineData("§PP{DEBUG}\n  §B{a:i32} 0\n§PPE\n  §L{l1:j:0:3:1}\n    §ASSIGN j 2\n§R 0")]
    [InlineData("§B{y:i32} 1\n§PP{A}\n  §B{a:i32} 0\n§PPE\n  §PP{B}\n    §B{b:i32} 0\n  §PPE\n    §ASSIGN y 2\n§R y")]
    public void Binder_WriteToReadOnlyLocalInEitherBranch_IsCalor0213(string body)
    {
        var diagnostics = Bind(Fn(body));

        Assert.Contains(DiagnosticCode.WriteToReadOnlyLocal, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_ElseBranchDoesNotSeeTheIfBranchBinding()
    {
        // The #else branch's §B{~x} is a fresh declaration, not a rebind of the #if branch's x.
        var diagnostics = Bind(Fn("§PP{DEBUG}\n  §B{x:i32} 1\n§PPE\n  §B{~x:i32} 2\n§R 0"));

        Assert.DoesNotContain(DiagnosticCode.BindReassignsImmutable, ErrorCodes(diagnostics));
        Assert.DoesNotContain(DiagnosticCode.DuplicateDefinition, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Binder_BranchReadOfAnUndefinedName_IsReported()
    {
        // The branch is bound now, so a typo inside it is no longer silently accepted.
        var diagnostics = Bind(Fn("§PP{DEBUG}\n  §B{y} (+ nope 1)\n§R 0"));

        Assert.Contains(DiagnosticCode.UndefinedReference, ErrorCodes(diagnostics));
    }

    [Fact]
    public void Compile_BranchBindingThatRedeclaresAnEnclosingLocal_IsRejected()
    {
        // `int x = 0; #if A int x = 1; #endif` is CS0128 whenever A is defined.
        var result = Compile(Fn("§B{x:i32} 0\n§PP{A}\n  §B{x:i32} 1\n§R x"));

        Assert.Contains(result.Diagnostics.Errors, e => e.Code == DiagnosticCode.BindDuplicateInScope);
    }

    // ===================================================================
    // Positive controls: valid §PP code compiles and the C# is right
    // ===================================================================

    [Theory]
    [InlineData("§PP{DEBUG}\n  §B{x:i32} 1\n§PPE\n  §B{x:i32} 2\n§B{y} (+ x 1)\n§R y", "int x = 2;")]
    [InlineData("§PP{DEBUG}\n  §B{~x:i32} 1\n§PPE\n  §B{~x:i32} 2\n§ASSIGN x 3\n§R x", "int x = 2;")]
    [InlineData("§PP{A}\n  §B{~x:i32} 1\n§PPE\n  §PP{B}\n    §B{~x:i32} 2\n  §PPE\n    §B{~x:i32} 3\n§ASSIGN x 4\n§B{~x} 5\n§R x", "int x = 3;")]
    [InlineData("§B{~n:i32} 0\n§PP{DEBUG}\n  §ASSIGN n 1\n§PPE\n  §B{~n} 2\n§R n", "n = 2;")]
    [InlineData("§L{l1:i:0:3:1}\n  §PP{DEBUG}\n    §B{k:i32} i\n  §PPE\n    §B{k:i32} 0\n§R 0", "int k = 0;")]
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
        var result = Compile(Fn("§PP{DEBUG}\n  §B{~x:i32} 1\n§PPE\n  §B{~x:i32} 2\n§R x"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains("int x = 1;", result.GeneratedCode);
        Assert.Contains("int x = 2;", result.GeneratedCode);
        Assert.Empty(ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    [Fact]
    public void Compile_WriteToFieldAfterPreprocessorWithoutLocal_Compiles()
    {
        var result = Compile(MethodWithField("§PP{DEBUG}\n  §ASSIGN x 1\n§ASSIGN x 2\n§R x"));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    // ===================================================================
    // Short-arity operator tolerances are Calor0110
    // ===================================================================

    [Theory]
    [InlineData("(?? s)", "(str?:s) -> str?")]
    [InlineData("(? b 1)", "(bool:b) -> i32")]
    [InlineData("(? b)", "(bool:b) -> i32")]
    [InlineData("(? b 1 2 3)", "(bool:b) -> i32")]
    [InlineData("(== s)", "(str?:s) -> bool")]
    [InlineData("(!= s)", "(str?:s) -> bool")]
    public void Parser_TooFewOperands_AreCalor0110(string expression, string signature)
    {
        Parse(Fn($"§R {expression}", signature), out var diagnostics);

        Assert.Contains(DiagnosticCode.OperatorArgumentCount, ErrorCodes(diagnostics));
    }

    [Theory]
    [InlineData("(?? s \"d\")", "(str?:s) -> str", "s ?? \"d\"")]
    [InlineData("(? b 1 2)", "(bool:b) -> i32", "b ? 1 : 2")]
    [InlineData("(== s null)", "(str?:s) -> bool", "s == null")]
    [InlineData("(!= s null)", "(str?:s) -> bool", "s != null")]
    public void Parser_FullOperandCounts_AreAccepted(string expression, string signature, string expected)
    {
        var result = Compile(Fn($"§R {expression}", signature));

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Contains(expected, result.GeneratedCode);
    }

    [Fact]
    public void Parser_EqualsWithOneOperand_SuggestsComparingWithNull()
    {
        Parse(Fn("§R (== s)", "(str?:s) -> bool"), out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, e => e.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("(== x null)", error.Message);
    }

    // ===================================================================
    // End-to-end CLI: diagnostic and exit code
    // ===================================================================

    public static IEnumerable<object[]> CliCases() => new[]
    {
        new object[] { "pp-shadows-field", MethodWithField("§PP{DEBUG}\n  §B{x:i32} 1\n§ASSIGN x 2\n§R x"), "Calor0213" },
        new object[] { "pp-nested", Fn("§PP{A}\n  §PP{B}\n    §B{x:i32} 1\n  §ASSIGN x 2\n§R 0"), "Calor0213" },
        new object[] { "pp-rebind-immutable", Fn("§PP{DEBUG}\n  §B{x:i32} 1\n§B{~x} 2\n§R x"), "Calor0260" },
        new object[] { "pp-else-branch-write", Fn("§B{y:i32} 1\n§PP{DEBUG}\n  §B{a:i32} 0\n§PPE\n  §ASSIGN y 2\n§R y"), "Calor0213" },
        new object[] { "coalesce-one-operand", Fn("§R (?? s)", "(str?:s) -> str?"), "Calor0110" },
        new object[] { "ternary-two-operands", Fn("§R (? b 1)", "(bool:b) -> i32"), "Calor0110" },
        new object[] { "equals-one-operand", Fn("§R (== s)", "(str?:s) -> bool"), "Calor0110" },
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
        new object[] { "pp-both-branches", Fn("§PP{DEBUG}\n  §B{~x:i32} 1\n§PPE\n  §B{~x:i32} 2\n§ASSIGN x 3\n§R x"), "int x = 2;" },
        new object[] { "pp-field-write", MethodWithField("§PP{DEBUG}\n  §ASSIGN x 1\n§ASSIGN x 2\n§R x"), "x = 2;" },
        new object[] { "pp-elif-chain", Fn("§PP{A}\n  §B{x:i32} 1\n§PPE\n  §PP{B}\n    §B{x:i32} 2\n  §PPE\n    §B{x:i32} 3\n§R x"), "int x = 3;" },
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
