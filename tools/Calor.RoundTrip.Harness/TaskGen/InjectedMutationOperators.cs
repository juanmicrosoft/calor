using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Standard semantic mutation operators (D-W4.1 supplement): boundary <c>&lt;</c>↔<c>&lt;=</c>,
/// arithmetic <c>+</c>↔<c>-</c>/<c>*</c>↔<c>/</c>, negate condition, off-by-one, swap return.
/// Each enumerated candidate isolates ONE change on an otherwise-verbatim file, and is applied
/// in C# (mutate-then-convert). Pure over source text — unit-testable without the corpus.
/// </summary>
public static class InjectedMutationOperators
{
    /// <summary>Enumerate all sited mutation candidates for a C# file, in document order.</summary>
    public static IReadOnlyList<MutationCandidate> Enumerate(string source, string fileRelPath)
    {
        var results = new List<MutationCandidate>();
        SyntaxNode root;
        try
        {
            root = CSharpSyntaxTree.ParseText(source).GetRoot();
        }
        catch
        {
            return results;
        }

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BinaryExpressionSyntax bin:
                    TryOperatorFlip(source, fileRelPath, root, bin, results);
                    break;
                case IfStatementSyntax ifs:
                    TryNegateCondition(source, fileRelPath, root, ifs.Condition, results);
                    break;
                case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.NumericLiteralExpression):
                    TryOffByOne(source, fileRelPath, root, lit, results);
                    break;
                case ReturnStatementSyntax { Expression: LiteralExpressionSyntax retLit }:
                    TrySwapReturn(source, fileRelPath, root, retLit, results);
                    break;
            }
        }

        return results;
    }

    private static void TryOperatorFlip(
        string source, string rel, SyntaxNode root, BinaryExpressionSyntax bin, List<MutationCandidate> acc)
    {
        var op = bin.OperatorToken;
        var (flipped, kind, desc) = op.Kind() switch
        {
            SyntaxKind.LessThanToken => (SyntaxKind.LessThanEqualsToken, MutationOperatorKind.Boundary, "< → <="),
            SyntaxKind.LessThanEqualsToken => (SyntaxKind.LessThanToken, MutationOperatorKind.Boundary, "<= → <"),
            SyntaxKind.GreaterThanToken => (SyntaxKind.GreaterThanEqualsToken, MutationOperatorKind.Boundary, "> → >="),
            SyntaxKind.GreaterThanEqualsToken => (SyntaxKind.GreaterThanToken, MutationOperatorKind.Boundary, ">= → >"),
            SyntaxKind.PlusToken => (SyntaxKind.MinusToken, MutationOperatorKind.Arithmetic, "+ → -"),
            SyntaxKind.MinusToken => (SyntaxKind.PlusToken, MutationOperatorKind.Arithmetic, "- → +"),
            SyntaxKind.AsteriskToken => (SyntaxKind.SlashToken, MutationOperatorKind.Arithmetic, "* → /"),
            SyntaxKind.SlashToken => (SyntaxKind.AsteriskToken, MutationOperatorKind.Arithmetic, "/ → *"),
            _ => (SyntaxKind.None, MutationOperatorKind.Boundary, ""),
        };
        if (flipped == SyntaxKind.None) return;

        var newOp = SyntaxFactory.Token(op.LeadingTrivia, flipped, op.TrailingTrivia);
        var mutatedRoot = root.ReplaceToken(op, newOp);
        var mutatedBin = bin.ReplaceToken(bin.OperatorToken, newOp);
        AddCandidate(rel, kind, desc, op, bin.ToString(), mutatedBin.ToString(), mutatedRoot, acc);
    }

    private static void TryNegateCondition(
        string source, string rel, SyntaxNode root, ExpressionSyntax cond, List<MutationCandidate> acc)
    {
        var negated = SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                SyntaxFactory.ParenthesizedExpression(cond.WithoutTrivia()))
            .WithTriviaFrom(cond);
        var mutatedRoot = root.ReplaceNode(cond, negated);
        AddCandidate(rel, MutationOperatorKind.NegateCondition, "cond → !(cond)",
            cond.GetFirstToken(), cond.ToString(), negated.ToString(), mutatedRoot, acc);
    }

    private static void TryOffByOne(
        string source, string rel, SyntaxNode root, LiteralExpressionSyntax lit, List<MutationCandidate> acc)
    {
        // Only integer literals; skip floats, hex we cannot round-trip cleanly, and overflow.
        if (!int.TryParse(lit.Token.ValueText, out var value)) return;
        if (value == int.MaxValue) return;
        var newVal = value + 1;
        var newToken = SyntaxFactory.Literal(lit.Token.LeadingTrivia, newVal.ToString(), newVal, lit.Token.TrailingTrivia);
        var mutatedRoot = root.ReplaceToken(lit.Token, newToken);
        AddCandidate(rel, MutationOperatorKind.OffByOne, $"{value} → {newVal}",
            lit.Token, lit.ToString(), newVal.ToString(), mutatedRoot, acc);
    }

    private static void TrySwapReturn(
        string source, string rel, SyntaxNode root, LiteralExpressionSyntax retLit, List<MutationCandidate> acc)
    {
        var (flippedKind, keyword, desc) = retLit.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression => (SyntaxKind.FalseLiteralExpression, SyntaxKind.FalseKeyword, "return true → return false"),
            SyntaxKind.FalseLiteralExpression => (SyntaxKind.TrueLiteralExpression, SyntaxKind.TrueKeyword, "return false → return true"),
            _ => (SyntaxKind.None, SyntaxKind.None, ""),
        };
        if (flippedKind == SyntaxKind.None) return;

        var newLit = SyntaxFactory.LiteralExpression(
                flippedKind,
                SyntaxFactory.Token(retLit.Token.LeadingTrivia, keyword, retLit.Token.TrailingTrivia))
            .WithTriviaFrom(retLit);
        var mutatedRoot = root.ReplaceNode(retLit, newLit);
        AddCandidate(rel, MutationOperatorKind.SwapReturn, desc,
            retLit.GetFirstToken(), retLit.ToString(), newLit.ToString(), mutatedRoot, acc);
    }

    private static void AddCandidate(
        string rel, MutationOperatorKind kind, string desc, SyntaxToken anchor,
        string originalSnippet, string mutatedSnippet, SyntaxNode mutatedRoot, List<MutationCandidate> acc)
    {
        var pos = anchor.GetLocation().GetLineSpan().StartLinePosition;
        acc.Add(new MutationCandidate
        {
            FileRelPath = rel,
            Source = MutationSource.InjectedMutation,
            Operator = kind,
            OperatorDescription = desc,
            Line = pos.Line + 1,
            Column = pos.Character + 1,
            OriginalSnippet = originalSnippet.Trim(),
            MutatedSnippet = mutatedSnippet.Trim(),
            MutatedSource = mutatedRoot.ToFullString(),
        });
    }
}
