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
        module R1Case R1Case block
          interface IProbe
            func Run (int:x, string:s) → int
              !
              requires (x >= 0)

          class Impl
            implements IProbe

            func Run (int:x, string:s) → int
              !
              requires IMPLPRE
              return 0
        """;

    private const string DivisionSource = """
        module R1Case R1Case block
          interface IProbe
            func Run (int:x, int:y) → int
              !
              requires (x == 3)

          class Impl
            implements IProbe

            func Run (int:x, int:y) → int
              !
              requires ((x % y) > -2)
              return 0
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
    [InlineData("(s.Length >= 0)")]                     // R1-IMPL-ASSUMPTION-FORMS-001 and -005
    [InlineData("((!string.IsNullOrEmpty(s)) || (s == \"\"))")]   // R1-IMPL-ASSUMPTION-FORMS-003
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

    // 0.30: `s.Length` is a native member read, which the verifier lowers to the same string
    // length (and string-model assumption) the retired `(len s)` operation had. The lowered
    // length is a .NET int, so it also carries the bound `Length <= int.MaxValue`; the
    // implication prover must assert that bound, or the solver answers `unknown` and the
    // weakening falls back to a Calor0810 error instead of an Assumed verdict.
    [Fact]
    public void NativeStringLength_StrongerPrecondition_IsNotAssumed()
    {
        // Control for the bound: (x >= 0) does not imply (s.Length > 3) even for non-null s,
        // so the weakening must still fail, never become Assumed or proven.
        var diagnostics = Compile(StringSourceTemplate.Replace("IMPLPRE", "(s.Length > 3)"));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        var violation = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
        Assert.Equal(DiagnosticSeverity.Error, violation.Severity);
    }

    [Fact]
    public void NullableReferenceString_NativeLength_IsRejectedNotAssumed()
    {
        // On a `string?` parameter the read itself is rejected (Calor0274), and the verifier
        // reads `s` as a nullable reference, not a string, so nothing is proven or assumed.
        var source = StringSourceTemplate.Replace("string:s", "string?:s")
            .Replace("IMPLPRE", "(s.Length >= 0)");
        var diagnostics = Compile(source);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
    }

    [Fact]
    public void NullableReferenceString_ComparedWithLiteral_IsRefusedNotACrash()
    {
        // A `string?` is a reference sort in the solver and "" is a string-sort literal.
        // Equating them used to throw a Z3Exception out of Program.Compile; it is refused.
        var source = StringSourceTemplate.Replace("string:s", "string?:s")
            .Replace("IMPLPRE", "((!string.IsNullOrEmpty(s)) || (s == \"\"))");
        var diagnostics = Compile(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationUnknown
            && d.Message.StartsWith("Could not establish precondition weakening", StringComparison.Ordinal));
        var violation = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.StrongerPrecondition);
        Assert.Equal(DiagnosticSeverity.Error, violation.Severity);
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
        module M M block
          interface IProbe
            func Run (int:x, int:y) → int
              !
              requires IFACEPRE

          class Impl
            implements IProbe

            func Run (int:x, int:y) → int
              !
              requires IMPLPRE
              return 0
        """;

    private static List<Diagnostic> CompileInteger(string ifacePre, string implPre, string? overflow = null)
    {
        var source = IntegerTemplate.Replace("IFACEPRE", ifacePre).Replace("IMPLPRE", implPre);
        if (overflow != null)
            source = source.Replace("module M M block", $"module M M block overflow {overflow}");
        return Compile(source);
    }

    [Fact]
    public void Control_IntegerWeakening_StillProven()
    {
        var diagnostics = CompileInteger("(x >= 1)", "(x >= 0)");
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
        var diagnostics = CompileInteger("((y > 0) && (x >= 0))", "((x % y) >= 0)");
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

        AssertPreconditionWeakeningRefuted(CompileInteger("(x > 0)", "((x * 2) != 1)"), "x=");
    }

    [Fact]
    public void ThrowingInterfacePrecondition_DoesNotYieldAFalseRefutation()
    {
        // Review round 1 witness: x + 1 < 0 completes true only for x < -1, where x < 0 holds.
        // At x = int.MaxValue the wrapped sum is negative, but the checked contract throws, so
        // that input is not one the interface accepts.
        var diagnostics = CompileInteger("((x + 1) < 0)", "(x < 0)");
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
            "(((x == 0) || ((x % y) == 0)) == true)",
            "(((x == 0) || ((x % y) == 0)) != true)");
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
            "(exists int:i where (i >= 0) : ((i < 2) && ((x + i) >= 0)))",
            "(x < 2147483647)");
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
            module M M block
              interface IProbe
                func Run (int:x) → int
                  !
                  requires ((x + 1) > 0)
                  => (result == 0)

              class Impl
                implements IProbe

                func Run (int:x) → int
                  !
                  requires ((x + 1) > 0)
                  => (result == 0)
                  return 0
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
            module M M block
              interface IA
                func Run (int:x) → string
                  !
                  => string.IsNullOrEmpty(result)

              interface IB
                func Run (int:x) → string
                  !
                  => (result != "")

              class Impl
                implements IA
                implements IB

                func Run (int:x) → string
                  !
                  return ""
            """;
        var diagnostics = Compile(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.IncompatibleInheritedContracts);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void WithoutTheSolver_HeuristicNeverClaimsValidityAndInequalityIsNotWeaker()
    {
        // Review round 2 witness: (!= x 0) is not a weakening of (== x 0).
        var source = IntegerTemplate.Replace("IFACEPRE", "(x == 0)").Replace("IMPLPRE", "(x != 0)");
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
            module M M block
              interface IProbe
                func Run (int:x, string:s) → int
                  !
                  requires (x >= 1)

              class Impl
                implements IProbe

                func Run (int:x, string:s) → int
                  !
                  requires (x >= 0)
                  return 0
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
            module M M block
              interface IA
                func Run (string:s) → int
                  !
                  => (result == 0)

              interface IB
                func Run (string:s) → int
                  !
                  => (result == 1)

              class Impl
                implements IA
                implements IB

                func Run (string:s) → int
                  !
                  return 0
            """;
        Assert.Contains(Compile(source), d => d.Code == DiagnosticCode.IncompatibleInheritedContracts);
    }

    private const string QuantifiedPredicate =
        "(exists int:i where (i >= 0) : ((i < 2) && ((VAR + i) >= 0)))";

    [Fact]
    public void IdenticalQuantifiedContracts_AreValid()
    {
        // Review round 3 witness: at x = int.MaxValue both identical expressions complete true at
        // i = 0, while the consequent's universal overflow safety fails at i = 1. Identity
        // decides the implication; it must not reach the heuristic's Calor0810/Calor0811.
        var pre = QuantifiedPredicate.Replace("VAR", "x");
        var post = QuantifiedPredicate.Replace("VAR", "result");
        var source = $$"""
            module M M block
              interface IProbe
                func Run (int:x) → int
                  !
                  requires {{pre}}
                  => {{post}}

              class Impl
                implements IProbe

                func Run (int:x) → int
                  !
                  requires {{pre}}
                  => {{post}}
                  return 0
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
            module M M block
              interface IA
                func Run (int:y) → int
                  !
                  => (y == 0)

              interface IB
                func Run (int:y) → int
                  !
                  => ((100 / y) == -1)

              class Impl
                implements IA
                implements IB

                func Run (int:y) → int
                  !
                  return 0
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
            module M M block
              func Run (int:x) → int
                !
                requires ((x * 2) > 0)
                return x
            """;
        var dir = Path.Combine(Path.GetTempPath(), "calor-s2-overflow-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var frozenRoot = Path.Combine(dir, "frozen");
            var finalRoot = Path.Combine(dir, "final");
            Directory.CreateDirectory(frozenRoot);
            Directory.CreateDirectory(finalRoot);
            var frozenPath = Path.Combine(frozenRoot, "input.calr");
            var finalPath = Path.Combine(finalRoot, "input.calr");
            File.WriteAllText(frozenPath, frozen);
            File.WriteAllText(finalPath, frozen.Replace("module M M block", "module M M block overflow unchecked"));
            var identities = new Calor.Compiler.Ids.DeclarationIdentityStore(frozenRoot);
            var state = identities.Sync([frozenPath]);
            var id = Assert.Single(state.Declarations, declaration =>
                declaration.Kind == Calor.Compiler.Ids.IdKind.Function && declaration.Name == "Run").Id;
            Calor.Compiler.Ids.DeclarationIdentityStore.CopyToSnapshot(
                new Dictionary<string, string> { [frozenPath] = finalPath });
            var (exit, stdOut, _) = CliTestHarness.RunCli(dir, "verify", frozenPath, finalPath, "--weakening-check", id);
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
        var diagnostics = CompileInteger("(x > 0)", "((x * 2) != 1)", overflow: "unchecked");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }
}
