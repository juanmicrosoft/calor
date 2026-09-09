using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis;

internal static class MatchExpressionValidator
{
    internal static bool IsSupported(MatchCaseNode arm) =>
        arm.Body is [ReturnStatementNode { Expression: not null }];

    internal static void ReportUnsupported(MatchCaseNode arm, DiagnosticBag diagnostics) =>
        diagnostics.ReportError(arm.Span, DiagnosticCode.ExpressionMatchBlockUnsupported,
            "Expression-match arms must contain exactly one value-return expression. "
            + "Multi-statement, empty, and valueless arms are not supported; use a statement match instead.");

    internal static void Validate(AstNode node, DiagnosticBag diagnostics)
    {
        if (node is MatchExpressionNode match)
            foreach (var arm in match.Cases.Where(arm => !IsSupported(arm)))
                ReportUnsupported(arm, diagnostics);
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            Validate(child, diagnostics);
    }
}
