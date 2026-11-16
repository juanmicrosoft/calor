using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Places a suggested contract clause (<c>requires …</c> or <c>=&gt; …</c>) in a native function.
/// Contract clauses may appear in any order after the header and before the body, so the clause
/// goes on its own line directly above the first body statement, at that statement's indent.
/// TextEdit columns are 1-based.
/// </summary>
internal static class ContractFixEdit
{
    public static TextEdit Insert(BoundFunction function, string clause)
    {
        var first = function.Body
            .Where(statement => statement.Span.Line > function.Span.Line)
            .OrderBy(statement => statement.Span.Line)
            .ThenBy(statement => statement.Span.Column)
            .FirstOrDefault();
        var (line, indent) = first != null
            ? (first.Span.Line, first.Span.Column - 1)
            : (function.Span.Line + 1, function.Span.Column - 1 + 2);
        return TextEdit.Insert("", line, 1, new string(' ', Math.Max(indent, 0)) + clause + "\n");
    }
}
