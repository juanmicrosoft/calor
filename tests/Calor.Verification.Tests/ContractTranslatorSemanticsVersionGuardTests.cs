using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// Checkpoint guard against silent drift in <see cref="ContractTranslator"/> semantics.
///
/// <para>
/// The Z3 verification cache key includes <c>ContractTranslator.SemanticsVersion</c>
/// (see <c>Verification/Z3/Cache/VerificationCache.cs</c>). That constant is bumped
/// by hand. If a translator change alters the SMT output for the same input AST but
/// nobody bumps <c>SemanticsVersion</c>, on-disk cache entries produced by the old
/// translator are silently served for AST fingerprints that now translate differently.
/// This regression happened for <c>#961</c> in 0.13.2, which is what motivated
/// audit finding F4 and recommendation R1c (issue <c>#997</c>).
/// </para>
///
/// <para>
/// This test drives a small fixture set of representative contract fragments through
/// <see cref="ContractTranslator"/>, canonicalises each Z3 expression as a string, hashes
/// the concatenation with SHA-256, and asserts the hex hash matches a committed baseline
/// alongside a committed <c>SemanticsVersion</c> snapshot. Any translator output change
/// forces the author to either revert the diff or bump <c>SemanticsVersion</c> AND update
/// the baseline hash below in the same commit.
/// </para>
/// </summary>
public class ContractTranslatorSemanticsVersionGuardTests
{
    // If this test fails, follow the guidance in the assertion message:
    //   1. If the translator change is UNINTENTIONAL, revert it.
    //   2. If it IS intentional, bump ContractTranslator.SemanticsVersion (so old
    //      on-disk cache entries are invalidated) AND replace the two constants
    //      below with the reported new values, in the SAME commit.
    private const string ExpectedSemanticsVersion = "z3-executable-semantics-v2";
    private const string ExpectedFixtureHash =
        "95e8039c7ea350581bc5c32b530d75576ae296c26850eec2056235d6847f0d86";

    [SkippableFact]
    public void TranslatorOutputMatchesCommittedBaseline()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        TranslatorOutputMatchesCommittedBaselineCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TranslatorOutputMatchesCommittedBaselineCore()
    {
        var actualHash = ComputeFixtureHash();
        var actualVersion = ContractTranslator.SemanticsVersion;

        // The version constant and the translator output must move together.
        // We assert both in one shot so the failure message is unambiguous.
        if (actualVersion == ExpectedSemanticsVersion && actualHash == ExpectedFixtureHash)
            return;

        var message =
            $"ContractTranslator semantics-version guard tripped.\n" +
            $"  Committed SemanticsVersion: '{ExpectedSemanticsVersion}'\n" +
            $"  Current   SemanticsVersion: '{actualVersion}'\n" +
            $"  Committed fixture hash:     '{ExpectedFixtureHash}'\n" +
            $"  Current   fixture hash:     '{actualHash}'\n" +
            "\n" +
            "The translator output hash and/or SemanticsVersion has changed since\n" +
            "this checkpoint was recorded.\n" +
            "\n" +
            "To decide the right fix, ask:\n" +
            "  * Did this PR touch src/Calor.Compiler/Verification/Z3/ContractTranslator.cs\n" +
            "    or any of its collaborators (e.g., ExpressionSimplifier)?\n" +
            "    → INTENTIONAL translator change. Bump `SemanticsVersion` (so existing\n" +
            "      Z3 verification cache entries are invalidated) AND update\n" +
            "      `ExpectedSemanticsVersion` + `ExpectedFixtureHash` in this test\n" +
            "      to the current values shown above, in the same commit.\n" +
            "  * Did this PR bump the pinned Microsoft.Z3 native version (see\n" +
            "    `src/Calor.Compiler/scripts/download-z3.sh` and\n" +
            "    `.github/z3-binaries-*.sha256`)?\n" +
            "    → Z3 UPGRADE. `Expr.ToString()` output depends on the Z3 native\n" +
            "      version, so a version bump changes the fixture hash without\n" +
            "      any translator change. Bumping `SemanticsVersion` is the safe\n" +
            "      move (Z3 upgrades reset the proof cache anyway); update this\n" +
            "      test's baseline in the same commit.\n" +
            "  * Neither?\n" +
            "    → UNINTENTIONAL drift. Something upstream changed under you;\n" +
            "      `git bisect` starting from the last passing commit will point\n" +
            "      at the offending change. Do NOT ship a green build until you\n" +
            "      understand why the translator output moved.\n" +
            "\n" +
            "Cached proofs would otherwise be served for AST fingerprints that\n" +
            "now translate differently (see #961 / #997).";

        Assert.Fail(message);
    }

    /// <summary>
    /// Translates each fixture through <see cref="ContractTranslator"/>, canonicalises
    /// the resulting Z3 expression, joins them with a separator, and returns SHA-256 hex.
    /// A fresh translator + context is created per fixture so that per-instance state
    /// (declared variables, cached sorts) cannot leak between fixtures.
    /// </summary>
    private static string ComputeFixtureHash()
    {
        var buffer = new StringBuilder();
        foreach (var fixture in Fixtures)
        {
            using var ctx = Z3ContextFactory.Create();
            var translator = new ContractTranslator(ctx);
            var rendered = fixture.Build(translator);
            buffer.Append(fixture.Name);
            buffer.Append(" :: ");
            buffer.AppendLine(rendered);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString()));
        return Convert.ToHexString(bytes).ToLower(CultureInfo.InvariantCulture);
    }

    private sealed record Fixture(string Name, Func<ContractTranslator, string> Build);

    /// <summary>
    /// A deliberately small, semantics-diverse fixture set. Each item exercises a
    /// distinct part of the translator's surface (bit-vector arithmetic, mixed
    /// signedness / width promotion, comparisons, string theory, quantification).
    /// </summary>
    private static readonly IReadOnlyList<Fixture> Fixtures = new[]
    {
        // 1. Simple integer postcondition: result == a + b
        new Fixture("post_add_i32", t =>
        {
            t.DeclareVariable("a", "i32");
            t.DeclareVariable("b", "i32");
            t.DeclareVariable("result", "i32");
            var expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.Equal,
                new ReferenceNode(TextSpan.Empty, "result"),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "a"),
                    new ReferenceNode(TextSpan.Empty, "b")));
            return Render(t.Translate(expr));
        }),

        // 2. Mixed-width arithmetic promotion: (i32) a < (i64) b + 1
        new Fixture("cmp_mixed_width", t =>
        {
            t.DeclareVariable("a", "i32");
            t.DeclareVariable("b", "i64");
            var expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.LessThan,
                new ReferenceNode(TextSpan.Empty, "a"),
                new BinaryOperationNode(
                    TextSpan.Empty,
                    BinaryOperator.Add,
                    new ReferenceNode(TextSpan.Empty, "b"),
                    new IntLiteralNode(TextSpan.Empty, 1)));
            return Render(t.Translate(expr));
        }),

        // 3. Signed / unsigned mix: (u32) x >= (i32) y
        new Fixture("cmp_signed_unsigned", t =>
        {
            t.DeclareVariable("x", "u32");
            t.DeclareVariable("y", "i32");
            var expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterOrEqual,
                new ReferenceNode(TextSpan.Empty, "x"),
                new ReferenceNode(TextSpan.Empty, "y"));
            return Render(t.Translate(expr));
        }),

        // 4. String theory: s.Contains("hi") && s.Length > 0
        new Fixture("string_contains_and_length", t =>
        {
            t.DeclareVariable("s", "string");
            var contains = new StringOperationNode(
                TextSpan.Empty,
                StringOp.Contains,
                new ExpressionNode[]
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                    new StringLiteralNode(TextSpan.Empty, "hi"),
                });
            var length = new StringOperationNode(
                TextSpan.Empty,
                StringOp.Length,
                new ExpressionNode[]
                {
                    new ReferenceNode(TextSpan.Empty, "s"),
                });
            var lengthPositive = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.GreaterThan,
                length,
                new IntLiteralNode(TextSpan.Empty, 0));
            var expr = new BinaryOperationNode(
                TextSpan.Empty,
                BinaryOperator.And,
                contains,
                lengthPositive);
            return Render(t.Translate(expr));
        }),

        // 5. Boolean literal / unary not: !true (canonicalises to false via Z3)
        new Fixture("unary_not_bool", t =>
        {
            var expr = new UnaryOperationNode(
                TextSpan.Empty,
                UnaryOperator.Not,
                new BoolLiteralNode(TextSpan.Empty, true));
            return Render(t.Translate(expr));
        }),
    };

    private static string Render(Expr? expr) =>
        expr is null ? "<null>" : expr.ToString() ?? "<null-string>";
}
