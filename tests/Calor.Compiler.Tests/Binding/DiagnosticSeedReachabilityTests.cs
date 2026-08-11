using Calor.Compiler.Diagnostics;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 required regression coverage (B8): "a diagnostic seed nested inside every
/// expression wrapper and proof that it remains reachable." The seed is a literal
/// division by zero; the proof is Calor0920 firing through the analyze pipeline with
/// ReportOnlyVerified — i.e., the wrapper's bound node exposes the seed to traversals
/// (BoundChildren.Of) and Z3 confirms it. A wrapper reverting to a zero-child opaque
/// binding turns its row red.
///
/// Wrappers NOT here, each with its reason (the honest exclusion list):
/// - Match-arm BODIES and lambda STATEMENT bodies: statement-level, reachable only
///   through their nodes — the #786 consumer gap, pinned as current-state in the B6
///   family tests.
/// - SizeOf: no expression children (type-name only).
/// - Interop (§CS / fallback): content is verbatim C# text, not Calor AST.
/// - NullConditional: its single child is the target (member access has no
///   expression argument to seed).
/// </summary>
public class DiagnosticSeedReachabilityTests
{
    public static IEnumerable<object[]> Wrappers()
    {
        yield return new object[] { "binary-operand", "§R (+ 1 (/ 10 0))" };
        yield return new object[] { "coalesce-fallback", "§R (?? x (/ 10 0))" };
        yield return new object[] { "implication-consequent", "§R (-> (> x 0) (== (/ 10 0) 1))" };
        yield return new object[] { "forall-body", "§R (forall ((i i32)) (> (/ 10 0) i))" };
        yield return new object[] { "exists-body", "§R (exists ((i i32)) (> (/ 10 0) i))" };
        yield return new object[] { "some-payload", "§R §SM (/ 10 0)" };
        yield return new object[] { "ok-payload", "§R §OK (/ 10 0)" };
        yield return new object[] { "conversion-operand", "§R (as (/ 10 0) i64)" };
        yield return new object[] { "await-operand", "§R §AWAIT (/ 10 0)" };
        yield return new object[] { "lambda-expression-body", "§R §LAM{l1:y:i32} (/ 10 0) §/LAM{l1}" };
        yield return new object[] { "bind-initializer", "§B{y:i32} (/ 10 0)\n    §R y" };
        // Tier-B retained children (B8's D2 extractors): the wrapper stays incomplete
        // (Calor0259) but the seed inside it must STILL reach the checker.
        yield return new object[] { "addressof-operand", "§R §ADDR (/ 10 0)" };
        yield return new object[] { "deref-operand", "§R §DEREF (/ 10 0)" };
    }

    [Fact]
    public void UninitializedUseInsideTierBWrapper_ReachesTheDataflowAnalysis()
    {
        // #911 review F2: the division seed rows pin the CheckExpression +
        // ContainsDivision arms only as a PAIR; the GetUsedVariables RetainedChildren
        // arm needs its own discriminating consumer. An uninitialized local read
        // inside §ADDR fires Calor0900 only if GetUsedVariables walks the retained
        // children (revert that arm and this fails).
        const string source = """
            §M{m001:Test}
              §F{f001:Probe:pub} () -> OBJECT
                §B{~y:i32}
                §R §ADDR y
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
        });

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UninitializedVariable && d.Message.Contains("'y'"));
        // #911 review F1 pin: the B8 severity promotion must reach THIS surface too —
        // the analyze path re-reports the binder's 0259 with its original severity.
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.AnalysisIncomplete
                && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void SeedInsideWrapper_ReachesTheDivisionChecker(string wrapper, string statement)
    {
        var source = $$"""
            §M{m001:Test}
              §F{f001:Probe:pub} (i32:x) -> OBJECT
                {{statement}}
            """;

        var result = Compiler.Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnableVerificationAnalyses = true,
            VerificationAnalysisOptions = new Compiler.Analysis.VerificationAnalysisOptions
            {
                BugPatternOptions = new Compiler.Analysis.BugPatterns.BugPatternOptions
                {
                    ReportOnlyVerified = true
                }
            }
        });

        Assert.True(
            result.Diagnostics.Any(d => d.Code == DiagnosticCode.DivisionByZero),
            $"Seed inside '{wrapper}' did not reach the division checker — its wrapper " +
            "no longer exposes children to traversals. Diagnostics: " +
            string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
    }
}
