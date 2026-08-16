using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Migration;

/// <summary>
/// One conditional-compilation directive removed by explicitly lossy branch selection.
/// </summary>
public sealed record RemovedConditionalDirective(int Line, string Directive);
public sealed record RemovedNonconditionalDirective(
    int Line,
    string Directive,
    string Feature);

/// <summary>Deprecated compatibility shape for the former stripping API.</summary>
[Obsolete("Use RemovedConditionalDirective and SelectActiveBranchLossy.")]
public sealed record StrippedDirective(int Line, string Directive, int DroppedLines);

/// <summary>
/// Result of Roslyn-selected conditional-compilation branch removal.
/// </summary>
public sealed record SelectedBranchResult(
    string Source,
    IReadOnlyList<RemovedConditionalDirective> RemovedConditionalDirectives,
    IReadOnlyList<RemovedNonconditionalDirective> RemovedNonconditionalDirectives);

/// <summary>Deprecated compatibility shape for the former stripping API.</summary>
[Obsolete("Use SelectedBranchResult and SelectActiveBranchLossy.")]
public sealed record PreprocessorStripResult(
    string Source,
    IReadOnlyList<StrippedDirective> ConditionalDirectives);

/// <summary>
/// Explicitly lossy conditional-compilation branch selection.
/// </summary>
public static class PreprocessorStripper
{
    /// <summary>
    /// Deprecated source-compatible wrapper. Uses Roslyn with no externally
    /// defined symbols and preserves nonconditional compiler directives.
    /// </summary>
    [Obsolete("Use SelectActiveBranchLossy with explicit CSharpParseOptions.")]
    public static string Strip(string source)
        => SelectActiveBranchLossy(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)).Source;

    /// <summary>
    /// Deprecated source-compatible wrapper. Uses Roslyn rather than source-order
    /// selection; dropped-line counts are no longer approximated and remain zero.
    /// </summary>
    [Obsolete("Use SelectActiveBranchLossy with explicit CSharpParseOptions.")]
    public static PreprocessorStripResult StripWithReport(string source)
    {
        var selected = SelectActiveBranchLossy(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        return new PreprocessorStripResult(
            selected.Source,
            selected.RemovedConditionalDirectives
                .Select(directive => new StrippedDirective(
                    directive.Line,
                    directive.Directive,
                    DroppedLines: 0))
                .ToArray());
    }

    /// <summary>
    /// Uses Roslyn's active/inactive trivia for the supplied parse options, removes
    /// conditional directives and disabled text, and leaves all nonconditional
    /// compiler directives intact for the normal preservation pipeline.
    /// </summary>
    public static SelectedBranchResult SelectActiveBranchLossy(
        string source,
        CSharpParseOptions parseOptions,
        CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var removedConditional = root.DescendantTrivia(descendIntoTrivia: true)
            .Where(IsConditionalDirective)
            .Select(trivia =>
            {
                var line = tree.GetLineSpan(trivia.Span, cancellationToken)
                    .StartLinePosition.Line + 1;
                return new RemovedConditionalDirective(
                    line,
                    trivia.ToFullString().Trim());
            })
            .ToArray();
        var removedNonconditional = root
            .DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia => trivia.GetStructure() is DirectiveTriviaSyntax directive
                && !IsConditionalDirective(trivia)
                && !directive.IsActive)
            .Select(trivia =>
            {
                var directive = (DirectiveTriviaSyntax)trivia.GetStructure()!;
                var line = tree.GetLineSpan(trivia.Span, cancellationToken)
                    .StartLinePosition.Line + 1;
                return new RemovedNonconditionalDirective(
                    line,
                    trivia.ToFullString().Trim(),
                    GetDirectiveFeature(directive));
            })
            .ToArray();
        var inactiveDirectiveStarts = removedNonconditional
            .Select(directive => directive.Line)
            .ToHashSet();
        var selectedRoot = new ActiveBranchRewriter(
            tree,
            inactiveDirectiveStarts).Visit(root)
            ?? throw new InvalidOperationException("Roslyn did not produce a selected syntax root.");
        return new SelectedBranchResult(
            selectedRoot.ToFullString(),
            removedConditional,
            removedNonconditional);
    }

    private static bool IsConditionalDirective(SyntaxTrivia trivia)
        => trivia.GetStructure() is IfDirectiveTriviaSyntax
            or ElifDirectiveTriviaSyntax
            or ElseDirectiveTriviaSyntax
            or EndIfDirectiveTriviaSyntax;

    private sealed class ActiveBranchRewriter : CSharpSyntaxRewriter
    {
        private readonly SyntaxTree _tree;
        private readonly IReadOnlySet<int> _inactiveDirectiveLines;

        public ActiveBranchRewriter(
            SyntaxTree tree,
            IReadOnlySet<int> inactiveDirectiveLines)
        {
            _tree = tree;
            _inactiveDirectiveLines = inactiveDirectiveLines;
        }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (IsConditionalDirective(trivia)
                || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                return default;
            }
            if (trivia.GetStructure() is DirectiveTriviaSyntax
                && _inactiveDirectiveLines.Contains(
                    _tree.GetLineSpan(trivia.Span)
                        .StartLinePosition.Line + 1))
            {
                return default;
            }

            return base.VisitTrivia(trivia);
        }
    }

    private static string GetDirectiveFeature(DirectiveTriviaSyntax directive)
        => directive switch
        {
            NullableDirectiveTriviaSyntax => "nullable-directive",
            PragmaWarningDirectiveTriviaSyntax or PragmaChecksumDirectiveTriviaSyntax => "pragma",
            WarningDirectiveTriviaSyntax => "warning-directive",
            ErrorDirectiveTriviaSyntax => "error-directive",
            LineDirectiveTriviaSyntax or LineSpanDirectiveTriviaSyntax => "line-directive",
            DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax => "symbol-directive",
            _ => "compiler-directive"
        };
}
