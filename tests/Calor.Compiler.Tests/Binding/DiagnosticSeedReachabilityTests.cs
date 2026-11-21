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
        yield return new object[] { "coalesce-fallback", "return x ?? (10 / 0)" };
        yield return new object[] { "conditional-value", "return x > 0 ? (10 / 0) : null" };
        yield return new object[] { "throw-argument", "return throw new Exception(10 / 0)" };
        yield return new object[] { "conversion-operand", "return (10 / 0) as long" };
        yield return new object[] { "await-operand", "return await System.Threading.Tasks.Task.FromResult(10 / 0)" };
        yield return new object[] { "lambda-expression-body", "return (int:y) => (10 / 0)" };
        yield return new object[] { "bind-initializer", "let int:y = 10 / 0\n    return y" };
    }

    [Theory]
    [MemberData(nameof(Wrappers))]
    public void SeedInsideWrapper_ReachesTheDivisionChecker(string wrapper, string statement)
    {
        var inputType = wrapper == "coalesce-fallback" ? "int?" : "int";
        var source = $$"""
            module Test Test block
              func Probe ({{inputType}}:x) → object
                ! alloc,throw
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
