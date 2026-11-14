using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Calor.Compiler.LegacySyntax;

/// <summary>
/// The C# half of the oracle. Two emitted C# texts are equivalent when, after masking
/// the source-position metadata the emitter embeds in contract checks, they have the
/// same Roslyn token sequence (kind + exact text, including every literal) and the same
/// preprocessor directives. Whitespace and comments are ignored; nothing else is.
/// </summary>
internal static partial class CSharpEquivalence
{
    public static bool Equivalent(string a, string b)
        => string.Equals(a, b, StringComparison.Ordinal)
           || Fingerprint(Mask(a)).SequenceEqual(Fingerprint(Mask(b)));

    /// <summary>
    /// Contract checks carry the declaring function's id and the contract's span
    /// (startOffset/length/line/column) in the source text. Legacy ids (`f001`, or
    /// legacy auto ids) cannot be written in native source ("tooling owns source
    /// identities") and spans necessarily move when the text is rewritten.
    /// </summary>
    internal static string Mask(string cs)
    {
        cs = ContractHead().Replace(cs, "$1\"<function-id>\", Calor.Runtime.ContractKind.");
        cs = ContractSpan().Replace(cs, "startOffset: 0, length: 0, sourceFile: $1, line: 0, column: 0");
        return cs;
    }

    private static IEnumerable<string> Fingerprint(string cs)
    {
        var root = new EmptyNamespacePruner().Visit(CSharpSyntaxTree.ParseText(cs).GetRoot());
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
            yield return ((int)token.Kind()).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + token.Text;
    }

    /// <summary>An empty namespace declaration declares nothing; the legacy emitter omitted it.</summary>
    private sealed class EmptyNamespacePruner : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitNamespaceDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax node)
            => IsEmpty(node) ? null : base.VisitNamespaceDeclaration(node);

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax node)
            => IsEmpty(node) ? null : base.VisitFileScopedNamespaceDeclaration(node);

        private static bool IsEmpty(Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax node)
            => node.Members.Count == 0 && node.Usings.Count == 0 && node.Externs.Count == 0
               && node.AttributeLists.Count == 0
               && !node.DescendantTrivia().Any(t => t.IsDirective);
    }

    [GeneratedRegex("""(new Calor\.Runtime\.ContractViolationException\((?:"(?:[^"\\]|\\.)*", )?)"(?:[^"\\]|\\.)*", Calor\.Runtime\.ContractKind\.""")]
    private static partial Regex ContractHead();

    [GeneratedRegex(@"startOffset: \d+, length: \d+, sourceFile: (null|""(?:[^""\\]|\\.)*""), line: \d+, column: \d+")]
    private static partial Regex ContractSpan();
}
