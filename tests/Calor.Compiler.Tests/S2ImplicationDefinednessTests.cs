using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for the S1 sweep rows IMPL-ASSUMPTION-FORMS
/// (F-B1/N1-027..029) and IMPL-DIVISION-TOTALIZED (F-B1/N1-030). The inheritance checker's
/// implication prover reported "Precondition weakening proven" (Calor0815) for implementer
/// preconditions that throw or are false on inputs the interface accepts: a string
/// operation on a null string (the solver's strings are never null) and a modulo by zero
/// (the solver's bvsrem is total). The registered S1 sources are used verbatim; the C#
/// assertions reproduce the independent oracle's witness. The tests need Z3 and fail,
/// not skip, without it.
/// </summary>
public sealed class S2ImplicationDefinednessTests
{
    private const string StringSourceTemplate = """
        §M{m1:R1Case}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §I{str:s}
              §O{i32}
              §Q (>= x INT:0)
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{str:s}
              §O{i32}
              §Q IMPLPRE
              §R INT:0
        """;

    private const string DivisionSource = """
        §M{m1:R1Case}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §O{i32}
              §I{i32:y}
              §Q (== x INT:3)
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q (> (% x y) INT:-2)
              §R INT:0
        """;

    private static List<Diagnostic> Compile(string source)
    {
        var options = new CompilationOptions { ContractMode = ContractMode.Debug };
        var result = Program.Compile(source, "case.calr", options);
        // Lexer and parser diagnostics (Calor0001-0199) mean the witness never reached the checker.
        Assert.DoesNotContain(result.Diagnostics, d => d.IsError && string.CompareOrdinal(d.Code, "Calor0200") < 0);
        return result.Diagnostics.ToList();
    }

    private static void AssertPreconditionWeakeningAssumed(List<Diagnostic> diagnostics, string assumption)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        var assumed = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
        Assert.Equal(DiagnosticSeverity.Warning, assumed.Severity);
        Assert.StartsWith("Precondition weakening for 'Impl.Run' is Assumed, not proven", assumed.Message);
        Assert.Contains(assumption, assumed.Message);
    }

    [Theory]
    [InlineData("(>= (len s) INT:0)")]                    // R1-IMPL-ASSUMPTION-FORMS-001 and -005
    [InlineData("(|| (! (isempty s)) (== s STR:\"\"))")]   // R1-IMPL-ASSUMPTION-FORMS-003
    public void NullableStringPrecondition_IsAssumedNotProven(string implementerPrecondition)
    {
        // Oracle witness (x = 0, s = null): the interface accepts it; the implementer's
        // precondition throws (s.Length) or is false (!IsNullOrEmpty(null) || null == "").
        string? s = null;
        Assert.Throws<NullReferenceException>(() => s!.Length >= 0);
        Assert.False(!string.IsNullOrEmpty(s) || s == "");

        var diagnostics = Compile(StringSourceTemplate.Replace("IMPLPRE", implementerPrecondition));
        AssertPreconditionWeakeningAssumed(diagnostics, "string-model");
    }

    private static void AssertPreconditionWeakeningRefuted(List<Diagnostic> diagnostics, string witness)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        var violation = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
        Assert.Equal(DiagnosticSeverity.Error, violation.Severity);
        Assert.Contains(witness, violation.Message);
    }

    [Fact]
    public void ModuloByPossiblyZeroDivisor_IsRefutedAtTheThrowingInput()
    {
        // R1-IMPL-DIVISION-TOTALIZED-001, oracle witness (x = 3, y = 0): the interface accepts
        // it and the implementer's precondition throws there — a real LSP violation, now
        // reported with that input as the counterexample instead of "proven".
        int x = 3, y = 0;
        Assert.Throws<DivideByZeroException>(() => x % y > -2);

        AssertPreconditionWeakeningRefuted(Compile(DivisionSource), "y=0");
    }

    private const string IntegerTemplate = """
        §M{m1:M}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q IFACEPRE
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q IMPLPRE
              §R INT:0
        """;

    private static List<Diagnostic> CompileInteger(string ifacePre, string implPre, string? overflow = null)
    {
        var source = IntegerTemplate.Replace("IFACEPRE", ifacePre).Replace("IMPLPRE", implPre);
        if (overflow != null)
            source = source.Replace("§M{m1:M}", $"§M{{m1:M:overflow={overflow}}}");
        return Compile(source);
    }

    [Fact]
    public void Control_IntegerWeakening_StillProven()
    {
        var diagnostics = CompileInteger("(>= x INT:1)", "(>= x INT:0)");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void Control_DivisionWhoseDivisorTheInterfaceRulesOut_StillProven()
    {
        // The interface admits only y > 0 and x >= 0: x % y cannot throw, and is >= 0.
        var diagnostics = CompileInteger("(&& (> y INT:0) (>= x INT:0))", "(>= (% x y) INT:0)");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void CheckedOverflowInImplementerPrecondition_IsRefuted()
    {
        // x * 2 is always even under wrapping, so the solver alone "proved" (x*2 != 1); under
        // the default checked module x * 2 throws for x > int.MaxValue / 2, which the
        // interface accepts.
        int x = int.MaxValue;
        Assert.Throws<OverflowException>(() => checked(x * 2) != 1);

        AssertPreconditionWeakeningRefuted(CompileInteger("(> x INT:0)", "(!= (* x INT:2) INT:1)"), "x=");
    }

    [Fact]
    public void ThrowingInterfacePrecondition_DoesNotYieldAFalseRefutation()
    {
        // Review round 1 witness: x + 1 < 0 completes true only for x < -1, where x < 0 holds.
        // At x = int.MaxValue the wrapped sum is negative, but the checked contract throws, so
        // that input is not one the interface accepts.
        var diagnostics = CompileInteger("(< (+ x INT:1) INT:0)", "(< x INT:0)");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
    }

    [Fact]
    public void ConditionalDivisor_NoProofAndNoHeuristicValidityClaim()
    {
        // Review round 1 witness: the interface accepts x = 0, y = 1 and the implementer rejects
        // it. The divisor is conditionally evaluated, so no solver verdict is established, and
        // a syntactic heuristic must not turn that into "contract inheritance valid".
        var diagnostics = CompileInteger(
            "(== (|| (== x INT:0) (== (% x y) INT:0)) BOOL:true)",
            "(!= (|| (== x INT:0) (== (% x y) INT:0)) BOOL:true)");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        // The refusal is visible (review round 2).
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationUnknown
            && d.Message.StartsWith("Could not establish precondition weakening", StringComparison.Ordinal));
    }

    [Fact]
    public void QuantifiedInterfacePrecondition_DefinednessDoesNotRestrictTheAssumption()
    {
        // Review round 2 witness: at x = int.MaxValue the exists completes true at i = 0, the
        // interface accepts, and the implementer rejects. The universal overflow safety of the
        // quantifier must not exclude that input and "prove" the weakening.
        var diagnostics = CompileInteger(
            "(exists ((i i32)) (&& (>= i INT:0) (&& (< i INT:2) (>= (+ x i) INT:0))))",
            "(< x INT:2147483647)");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
    }

    [Fact]
    public void IdenticalContracts_WithAPartialInterfacePrecondition_AreNotAViolation()
    {
        // Review round 2 witness: the interface precondition throws at x = int.MaxValue; that
        // input is never accepted, so it creates no postcondition obligation.
        const string source = """
            §M{m1:M}
              §IFACE{i1:IProbe}
                §MT{m1:Run}
                  §I{i32:x}
                  §O{i32}
                  §Q (> (+ x INT:1) INT:0)
                  §S (== result INT:0)
              §CL{c1:Impl:pub}
                §IMPL{IProbe}
                §MT{mt1:Run:pub}
                  §I{i32:x}
                  §O{i32}
                  §Q (> (+ x INT:1) INT:0)
                  §S (== result INT:0)
                  §R INT:0
            """;
        var diagnostics = Compile(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.WeakerPostcondition);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [Fact]
    public void NullCompatibleInheritedGuarantees_AreNotReportedIncompatible()
    {
        // Review round 2 witness: result = null satisfies both IsNullOrEmpty(result) and
        // result != "", which the solver's non-null strings cannot represent.
        string? result = null;
        Assert.True(string.IsNullOrEmpty(result) && result != "");
        const string source = """
            §M{m1:M}
              §IFACE{i1:IA}
                §MT{m1:Run}
                  §I{i32:x}
                  §O{str}
                  §S (isempty result)
              §IFACE{i2:IB}
                §MT{m2:Run}
                  §I{i32:x}
                  §O{str}
                  §S (!= result STR:"")
              §CL{c1:Impl:pub}
                §IMPL{IA}
                §IMPL{IB}
                §MT{mt1:Run:pub}
                  §I{i32:x}
                  §O{str}
                  §R STR:""
            """;
        var diagnostics = Compile(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.IncompatibleInheritedContracts);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void WithoutTheSolver_HeuristicNeverClaimsValidityAndInequalityIsNotWeaker()
    {
        // Review round 2 witness: (!= x 0) is not a weakening of (== x 0).
        var source = IntegerTemplate.Replace("IFACEPRE", "(== x INT:0)").Replace("IMPLPRE", "(!= x INT:0)");
        var diagnostics = new DiagnosticBag();
        var module = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        using var checker = new ContractInheritanceChecker(diagnostics, useZ3: false);
        checker.Check(module);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [Fact]
    public void UnusedStringParameter_DoesNotDemoteAnIntegerImplication()
    {
        const string source = """
            §M{m1:M}
              §IFACE{i1:IProbe}
                §MT{m1:Run}
                  §I{i32:x}
                  §I{str:s}
                  §O{i32}
                  §Q (>= x INT:1)
              §CL{c1:Impl:pub}
                §IMPL{IProbe}
                §MT{mt1:Run:pub}
                  §I{i32:x}
                  §I{str:s}
                  §O{i32}
                  §Q (>= x INT:0)
                  §R INT:0
            """;
        var diagnostics = Compile(source);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void IncompatibleInheritedGuarantees_StillReportedWithAStringParameter()
    {
        const string source = """
            §M{m1:M}
              §IFACE{i1:IA}
                §MT{m1:Run}
                  §I{str:s}
                  §O{i32}
                  §S (== result INT:0)
              §IFACE{i2:IB}
                §MT{m2:Run}
                  §I{str:s}
                  §O{i32}
                  §S (== result INT:1)
              §CL{c1:Impl:pub}
                §IMPL{IA}
                §IMPL{IB}
                §MT{mt1:Run:pub}
                  §I{str:s}
                  §O{i32}
                  §R INT:0
            """;
        Assert.Contains(Compile(source), d => d.Code == DiagnosticCode.IncompatibleInheritedContracts);
    }

    private const string QuantifiedPredicate =
        "(exists ((i i32)) (&& (>= i INT:0) (&& (< i INT:2) (>= (+ VAR i) INT:0))))";

    [Fact]
    public void IdenticalQuantifiedContracts_AreValid()
    {
        // Review round 3 witness: at x = int.MaxValue both identical expressions complete true at
        // i = 0, while the consequent's universal overflow safety fails at i = 1. Identity
        // decides the implication; it must not reach the heuristic's Calor0810/Calor0811.
        var pre = QuantifiedPredicate.Replace("VAR", "x");
        var post = QuantifiedPredicate.Replace("VAR", "result");
        var source = $$"""
            §M{m1:M}
              §IFACE{i1:IProbe}
                §MT{m1:Run}
                  §I{i32:x}
                  §O{i32}
                  §Q {{pre}}
                  §S {{post}}
              §CL{c1:Impl:pub}
                §IMPL{IProbe}
                §MT{mt1:Run:pub}
                  §I{i32:x}
                  §O{i32}
                  §Q {{pre}}
                  §S {{post}}
                  §R INT:0
            """;
        var diagnostics = Compile(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.WeakerPostcondition);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
    }

    [Fact]
    public void UndecidedInheritedConflict_IsVisibleAndNotClaimedValid()
    {
        // Review round 3 witness: y != 0 fails IA, and y == 0 makes IB throw, so no input satisfies
        // both. The conditional divisor is not modeled: the check must say it could not decide.
        Assert.Throws<DivideByZeroException>(() => { var y = 0; return 100 / y == -1; });
        const string source = """
            §M{m1:M}
              §IFACE{i1:IA}
                §MT{m1:Run}
                  §I{i32:y}
                  §O{i32}
                  §S (== y INT:0)
              §IFACE{i2:IB}
                §MT{m2:Run}
                  §I{i32:y}
                  §O{i32}
                  §S (== (/ INT:100 y) INT:-1)
              §CL{c1:Impl:pub}
                §IMPL{IA}
                §IMPL{IB}
                §MT{mt1:Run:pub}
                  §I{i32:y}
                  §O{i32}
                  §R INT:0
            """;
        var diagnostics = Compile(source);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationUnknown
            && d.Message.StartsWith("Could not establish whether the inherited contracts", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
    }

    [Fact]
    public void WeakeningCheck_DifferentOverflowPolicies_AreIndeterminate()
    {
        const string frozen = """
            §M{m1:M}
              §F{f1:Run:pub} (i32:x) -> i32
                §Q (> (* x INT:2) INT:0)
                §R x
            """;
        var dir = Path.Combine(Path.GetTempPath(), "calor-s2-overflow-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var frozenPath = Path.Combine(dir, "frozen.calr");
            var finalPath = Path.Combine(dir, "final.calr");
            File.WriteAllText(frozenPath, frozen);
            File.WriteAllText(finalPath, frozen.Replace("§M{m1:M}", "§M{m1:M:overflow=unchecked}"));
            var (exit, stdOut, _) = CliTestHarness.RunCli(dir, "verify", frozenPath, finalPath, "--weakening-check", "f1");
            Assert.Equal(0, exit);
            var line = stdOut.Split('\n').First(l => l.TrimStart().StartsWith('{'));
            var json = System.Text.Json.JsonDocument.Parse(line).RootElement;
            Assert.True(json.GetProperty("indeterminate").GetBoolean());
            Assert.Contains("overflow policies", json.GetProperty("reason").GetString());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void UncheckedModule_WrappingArithmetic_StillProven()
    {
        // Under overflow=unchecked the same contract wraps and never throws: proven.
        var diagnostics = CompileInteger("(> x INT:0)", "(!= (* x INT:2) INT:1)", overflow: "unchecked");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }
}
