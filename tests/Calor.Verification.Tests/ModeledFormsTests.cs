using System.Runtime.CompilerServices;
using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// D-G2.3 (guarantees plan): the positive modeled-forms whitelist. Three
/// guarantees pinned here: (1) the whitelist is the single enumeration — the
/// doc's generated appendix must byte-match <see cref="ModeledForms.RenderWhitelist"/>;
/// (2) no drift — every whitelist-accepted operator/kind actually translates;
/// (3) out-of-whitelist forms gate to `unsupported` with a reason naming the
/// offending construct, before any translator branch is consulted.
/// </summary>
public class ModeledFormsTests
{
    private static ReferenceNode Ref(string name) => new(TextSpan.Empty, name);
    private static IntLiteralNode Int(int value) => new(TextSpan.Empty, value);
    private static BinaryOperationNode Bin(BinaryOperator op, ExpressionNode l, ExpressionNode r)
        => new(TextSpan.Empty, op, l, r);

    // ------------------------------------------------------------------
    // (1) Doc conformance: the generated appendix in
    // docs/verification-modeled-forms.md must match the code enumeration.
    // ------------------------------------------------------------------

    [Fact]
    public void Doc_GeneratedAppendix_MatchesCodeWhitelist()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var docPath = Path.Combine(projectRoot, "docs", "verification-modeled-forms.md");
        var doc = File.ReadAllText(docPath).Replace("\r\n", "\n");

        const string begin = "<!-- BEGIN GENERATED WHITELIST (ModeledForms.RenderWhitelist) — do not edit by hand -->";
        const string end = "<!-- END GENERATED WHITELIST -->";
        var beginIdx = doc.IndexOf(begin, StringComparison.Ordinal);
        var endIdx = doc.IndexOf(end, StringComparison.Ordinal);
        Assert.True(beginIdx >= 0 && endIdx > beginIdx,
            "docs/verification-modeled-forms.md must contain the generated-whitelist markers");

        var docBlock = doc[(beginIdx + begin.Length)..endIdx].Trim('\n');
        var expected = ("```\n" + ModeledForms.RenderWhitelist() + "```").Trim('\n');

        Assert.True(expected == docBlock,
            "Doc whitelist block is out of date. Regenerate it to exactly:\n\n" + expected);
    }

    // ------------------------------------------------------------------
    // (2) Drift detector: whitelist-accepted forms must translate.
    // ------------------------------------------------------------------

    [SkippableFact]
    public void EveryWhitelistedBinaryOperator_Translates()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        BinaryOperatorDriftCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BinaryOperatorDriftCore()
    {
        using var ctx = Z3ContextFactory.Create();
        foreach (var op in ModeledForms.Operators)
        {
            var translator = new ContractTranslator(ctx);
            Assert.True(translator.DeclareVariable("x", "i32"));
            Assert.True(translator.DeclareVariable("y", "i32"));
            Assert.True(translator.DeclareVariable("p", "bool"));
            Assert.True(translator.DeclareVariable("q", "bool"));

            // And/Or are boolean connectives; everything else operates on bit-vectors.
            var expr = op is BinaryOperator.And or BinaryOperator.Or
                ? Bin(op, Ref("p"), Ref("q"))
                : Bin(op, Ref("x"), Ref("y"));
            Assert.True(ModeledForms.TryValidate(expr, out _));
            Assert.True(translator.Translate(expr) != null,
                $"whitelisted operator {op} failed to translate (whitelist drift)");
        }
    }

    [SkippableFact]
    public void WhitelistedUnaryAndConditionalForms_Translate()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        UnaryConditionalDriftCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnaryConditionalDriftCore()
    {
        using var ctx = Z3ContextFactory.Create();
        var translator = new ContractTranslator(ctx);
        Assert.True(translator.DeclareVariable("x", "i32"));
        Assert.True(translator.DeclareVariable("b", "bool"));

        var negate = new UnaryOperationNode(TextSpan.Empty, UnaryOperator.Negate, Ref("x"));
        var not = new UnaryOperationNode(TextSpan.Empty, UnaryOperator.Not, Ref("b"));
        var conditional = new ConditionalExpressionNode(TextSpan.Empty, Ref("b"), Ref("x"), Int(0));

        foreach (var expr in new ExpressionNode[] { negate, not, conditional })
        {
            Assert.True(ModeledForms.TryValidate(expr, out _));
            Assert.NotNull(translator.Translate(expr));
        }
    }

    // ------------------------------------------------------------------
    // (3) The gate: out-of-whitelist forms are unsupported BY the whitelist,
    // with a reason naming the construct.
    // ------------------------------------------------------------------

    [Fact]
    public void TryValidate_NamesTheOffendingConstruct()
    {
        Assert.False(ModeledForms.TryValidate(new FloatLiteralNode(TextSpan.Empty, 1.5), out var floatWhy));
        Assert.Equal("floating-point literal", floatWhy);

        var powerExpr = Bin(BinaryOperator.Power, Ref("x"), Int(2));
        Assert.False(ModeledForms.TryValidate(powerExpr, out var powerWhy));
        Assert.Contains("Power", powerWhy);

        var nested = Bin(BinaryOperator.Add, Ref("x"), new FloatLiteralNode(TextSpan.Empty, 2.0));
        Assert.False(ModeledForms.TryValidate(nested, out var nestedWhy));
        Assert.Equal("floating-point literal", nestedWhy);
    }

    [SkippableFact]
    public void Verifier_GatesOutOfWhitelistContract_AsUnsupportedWithReason()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");
        VerifierGateCore();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifierGateCore()
    {
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);

        var parameters = new List<(string Name, string Type)> { ("x", "i32") };
        var powerPost = new EnsuresNode(
            TextSpan.Empty,
            Bin(BinaryOperator.GreaterOrEqual,
                Bin(BinaryOperator.Power, Ref("x"), Int(2)),
                Int(0)),
            null,
            new AttributeCollection());

        var result = verifier.VerifyPostcondition(parameters, "i32", [], powerPost);

        Assert.Equal(ProofStatus.Unsupported, result.EffectiveOutcome.Status);
        Assert.Contains("outside the modeled whitelist", result.EffectiveOutcome.Reason);
        Assert.Contains("Power", result.EffectiveOutcome.Reason);
    }
}
