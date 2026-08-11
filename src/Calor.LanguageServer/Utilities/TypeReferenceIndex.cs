using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;

namespace Calor.LanguageServer.Utilities;

public readonly record struct IndexedTypeReference(
    SymbolId SymbolId,
    string Name,
    TextSpan Span);

/// <summary>
/// Builds an identity-aware index from parser-recorded type annotation spans.
/// </summary>
public static class TypeReferenceIndex
{
    public static IReadOnlyList<IndexedTypeReference> Build(
        ModuleNode ast,
        BoundModule boundModule,
        string source)
    {
        ArgumentNullException.ThrowIfNull(ast);
        ArgumentNullException.ThrowIfNull(boundModule);
        ArgumentNullException.ThrowIfNull(source);

        var typeSymbols = boundModule.SymbolsById.Values
            .OfType<TypeSymbol>()
            .Where(symbol => !symbol.Id.IsNone)
            .ToArray();
        if (typeSymbols.Length == 0)
            return Array.Empty<IndexedTypeReference>();

        var references = new List<IndexedTypeReference>();
        foreach (var creation in BoundNodeHelpers.DescendantsAndSelf(boundModule)
                     .OfType<BoundNewExpression>())
        {
            AddBoundTypeReference(creation.TypeReference, source, references);
        }

        foreach (var node in DescendantsAndSelf(ast))
        {
            switch (node)
            {
                case OutputNode output:
                    AddAnnotation(output.TypeNameSpan, source, typeSymbols, references);
                    break;
                case ParameterNode parameter:
                    AddAnnotation(parameter.TypeNameSpan, source, typeSymbols, references);
                    break;
                case ClassFieldNode field:
                    AddAnnotation(field.TypeNameSpan, source, typeSymbols, references);
                    break;
                case PropertyNode property:
                    AddAnnotation(property.TypeNameSpan, source, typeSymbols, references);
                    break;
                case BindStatementNode bind when bind.TypeName != null:
                    AddAnnotation(bind.TypeNameSpan, source, typeSymbols, references);
                    break;
                case ClassDefinitionNode cls:
                    if (cls.BaseClassSpan is { } baseSpan)
                        AddAnnotation(baseSpan, source, typeSymbols, references);
                    foreach (var interfaceSpan in cls.ImplementedInterfaceSpans)
                        AddAnnotation(interfaceSpan, source, typeSymbols, references);
                    break;
            }
        }

        return references
            .Distinct()
            .OrderBy(reference => reference.Span.Start)
            .ThenBy(reference => reference.Span.End)
            .ToArray();
    }

    private static void AddAnnotation(
        TextSpan annotationSpan,
        string source,
        IReadOnlyList<TypeSymbol> typeSymbols,
        ICollection<IndexedTypeReference> references)
    {
        if (annotationSpan.Length <= 0
            || annotationSpan.Start < 0
            || annotationSpan.End > source.Length)
        {
            return;
        }

        var annotation = source.AsSpan(annotationSpan.Start, annotationSpan.Length);
        var identifiers = ScanIdentifiers(annotation);
        for (var index = 0; index < identifiers.Count; index++)
        {
            var identifier = identifiers[index];
            var symbol = ResolveTypeSymbol(annotation, identifiers, index, typeSymbols);
            if (symbol == null)
                continue;

            references.Add(new IndexedTypeReference(
                symbol.Id,
                identifier.Text,
                CreateSubspan(annotationSpan, source, identifier.Start, identifier.Length)));
        }
    }

    private static void AddBoundTypeReference(
        BoundTypeReference reference,
        string source,
        ICollection<IndexedTypeReference> references)
    {
        if (reference.ResolvedTypeSymbolId is { IsNone: false } symbolId
            && reference.Span.Length > 0
            && reference.Span.Start >= 0
            && reference.Span.End <= source.Length)
        {
            references.Add(new IndexedTypeReference(
                symbolId,
                source.Substring(reference.Span.Start, reference.Span.Length),
                reference.Span));
        }

        foreach (var typeArgument in reference.TypeArguments)
            AddBoundTypeReference(typeArgument, source, references);
    }

    private static TypeSymbol? ResolveTypeSymbol(
        ReadOnlySpan<char> annotation,
        IReadOnlyList<IdentifierPart> identifiers,
        int index,
        IReadOnlyList<TypeSymbol> typeSymbols)
    {
        var identifier = identifiers[index];
        var chainStart = index;
        while (chainStart > 0
               && IsQualifiedSeparator(
                   annotation,
                   identifiers[chainStart - 1].End,
                   identifiers[chainStart].Start))
        {
            chainStart--;
        }

        var chainEnd = index;
        while (chainEnd + 1 < identifiers.Count
               && IsQualifiedSeparator(
                   annotation,
                   identifiers[chainEnd].End,
                   identifiers[chainEnd + 1].Start))
        {
            chainEnd++;
        }

        if (chainStart != chainEnd)
        {
            if (index != chainEnd)
                return null;

            var qualifiedName = string.Join(
                ".",
                identifiers.Skip(chainStart).Take(chainEnd - chainStart + 1)
                    .Select(part => part.Text));
            var qualifiedMatches = typeSymbols
                .Where(symbol =>
                    string.Equals(symbol.Name, identifier.Text, StringComparison.Ordinal)
                    && (string.Equals(
                            symbol.QualifiedName,
                            qualifiedName,
                            StringComparison.Ordinal)
                        || symbol.QualifiedName.EndsWith(
                            "." + qualifiedName,
                            StringComparison.Ordinal)))
                .ToArray();
            return qualifiedMatches.Length == 1 ? qualifiedMatches[0] : null;
        }

        var simpleMatches = typeSymbols
            .Where(symbol => string.Equals(symbol.Name, identifier.Text, StringComparison.Ordinal))
            .ToArray();
        return simpleMatches.Length == 1 ? simpleMatches[0] : null;
    }

    private static bool IsQualifiedSeparator(
        ReadOnlySpan<char> annotation,
        int previousEnd,
        int nextStart)
    {
        var sawDot = false;
        for (var i = previousEnd; i < nextStart; i++)
        {
            if (annotation[i] == '.')
            {
                if (sawDot)
                    return false;
                sawDot = true;
            }
            else if (!char.IsWhiteSpace(annotation[i]))
            {
                return false;
            }
        }

        return sawDot;
    }

    private static List<IdentifierPart> ScanIdentifiers(ReadOnlySpan<char> annotation)
    {
        var result = new List<IdentifierPart>();
        for (var index = 0; index < annotation.Length;)
        {
            if (!IsIdentifierStart(annotation[index]))
            {
                index++;
                continue;
            }

            var start = index++;
            while (index < annotation.Length && IsIdentifierPart(annotation[index]))
                index++;

            result.Add(new IdentifierPart(
                annotation[start..index].ToString(),
                start,
                index - start));
        }

        return result;
    }

    private static TextSpan CreateSubspan(
        TextSpan annotationSpan,
        string source,
        int relativeStart,
        int length)
    {
        var line = annotationSpan.Line;
        var column = annotationSpan.Column;
        for (var offset = annotationSpan.Start;
             offset < annotationSpan.Start + relativeStart;
             offset++)
        {
            if (source[offset] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new TextSpan(annotationSpan.Start + relativeStart, length, line, column);
    }

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private readonly record struct IdentifierPart(
        string Text,
        int Start,
        int Length)
    {
        public int End => Start + Length;
    }
}
