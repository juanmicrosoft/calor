using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;

namespace Calor.Compiler.Verification;

/// <summary>
/// #1413 (S1 row QNT-NESTED, registered unsupported-refused): a quantifier inside another
/// quantifier's body has no runtime lowering — the emitter rejects the contract with
/// <c>Calor0326</c> — yet the verifier reported it <c>Proven</c> (the claim exists even though
/// compilation then fails). Every verifier channel refuses such an expression instead, so no
/// claim is made about a form the runtime cannot check.
/// </summary>
internal static class QuantifierNesting
{
    public const string Refusal =
        "nested quantifiers are refused: the runtime check cannot evaluate a quantifier inside another (Calor0326). Runtime check kept.";

    public static bool ContainsNestedQuantifier(ExpressionNode expression)
        => DescendantsAndSelf(expression)
            .Where(IsQuantifier)
            .Any(quantifier => DescendantsAndSelf(quantifier).Skip(1).Any(IsQuantifier));

    private static bool IsQuantifier(AstNode node)
        => node is ForallExpressionNode or ExistsExpressionNode;

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
