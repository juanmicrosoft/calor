using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Migration;

/// <summary>
/// A required unsupported C# semantic detected before native conversion.
/// </summary>
public sealed record SyntaxCapabilityDetection(
    string FeatureName,
    string SyntaxKind,
    int Line,
    int Column,
    int SpanStart,
    int SpanLength);

/// <summary>
/// Central classifier for syntax whose semantics require exact C# interop
/// preservation rather than native Calor conversion.
/// </summary>
public static class SyntaxCapabilityClassifier
{
    /// <summary>The modern syntax features this classifier guarantees to detect.</summary>
    public static IReadOnlySet<string> RequiredUnsupportedFeatures { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "await-foreach",
            "await-using",
            "using-declaration",
            "ref-struct",
            "file-scoped-type",
            "scoped-parameter",
            "interface-semantics",
            "interface-method-semantics",
            "interface-property-semantics",
            "interface-indexer-semantics",
            "interface-member",
            "delegate-semantics"
        };

    /// <summary>
    /// Detects required unsupported semantics in source order. Callers choose
    /// the syntax boundary to inspect so preservation occurs at the correct
    /// lifetime or contract boundary.
    /// </summary>
    public static IReadOnlyList<SyntaxCapabilityDetection> Detect(SyntaxNode node)
    {
        var detections = new List<SyntaxCapabilityDetection>();

        foreach (var current in node.DescendantNodesAndSelf())
        {
            switch (current)
            {
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    AddTypeModifierDetection(interfaceDeclaration, detections);
                    if (!IsRepresentableInterfaceDeclaration(interfaceDeclaration))
                    {
                        Add(
                            detections,
                            interfaceDeclaration,
                            "interface-semantics");
                    }
                    foreach (var member in interfaceDeclaration.Members)
                    {
                        var feature = GetUnsupportedInterfaceFeature(member);
                        if (feature != null)
                            Add(detections, member, feature);
                    }
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration:
                    AddTypeModifierDetection(typeDeclaration, detections);
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    if (delegateDeclaration.Modifiers.Any(SyntaxKind.FileKeyword))
                        Add(detections, delegateDeclaration, "file-scoped-type");
                    if (!IsRepresentableDelegate(delegateDeclaration))
                        Add(detections, delegateDeclaration, "delegate-semantics");
                    break;
                case ForEachStatementSyntax forEach
                    when !forEach.AwaitKeyword.IsKind(SyntaxKind.None):
                    Add(detections, forEach, "await-foreach");
                    break;
                case ForEachVariableStatementSyntax forEachVariable
                    when !forEachVariable.AwaitKeyword.IsKind(SyntaxKind.None):
                    Add(detections, forEachVariable, "await-foreach");
                    break;
                case UsingStatementSyntax usingStatement
                    when !usingStatement.AwaitKeyword.IsKind(SyntaxKind.None):
                    Add(detections, usingStatement, "await-using");
                    break;
                case LocalDeclarationStatementSyntax declaration
                    when !declaration.UsingKeyword.IsKind(SyntaxKind.None):
                    Add(
                        detections,
                        declaration,
                        declaration.AwaitKeyword.IsKind(SyntaxKind.None)
                            ? "using-declaration"
                            : "await-using");
                    break;
                case ParameterSyntax parameter
                    when parameter.Modifiers.Any(SyntaxKind.ScopedKeyword):
                    Add(detections, parameter, "scoped-parameter");
                    break;
            }
        }

        return detections
            .DistinctBy(item => (item.FeatureName, item.SpanStart))
            .OrderBy(item => item.SpanStart)
            .ToList();
    }

    /// <summary>Returns the first required unsupported semantic, if any.</summary>
    public static SyntaxCapabilityDetection? FindFirst(SyntaxNode node)
        => Detect(node).FirstOrDefault();

    /// <summary>
    /// Detects unsupported modifiers applied to this declaration only, without
    /// attributing unsupported nested types to their containing type.
    /// </summary>
    public static SyntaxCapabilityDetection? FindDeclaredTypeModifier(
        BaseTypeDeclarationSyntax declaration)
    {
        var detections = new List<SyntaxCapabilityDetection>();
        AddTypeModifierDetection(declaration, detections);
        return detections.FirstOrDefault();
    }

    /// <summary>
    /// Returns the first unsupported interface declaration or member semantic.
    /// </summary>
    public static SyntaxCapabilityDetection? FindUnsupportedInterface(
        InterfaceDeclarationSyntax declaration)
    {
        var modifier = FindDeclaredTypeModifier(declaration);
        if (modifier != null)
            return modifier;
        if (!IsRepresentableInterfaceDeclaration(declaration))
            return CreateDetection(declaration, "interface-semantics");
        return FindFirst(declaration);
    }

    private static void AddTypeModifierDetection(
        BaseTypeDeclarationSyntax declaration,
        ICollection<SyntaxCapabilityDetection> detections)
    {
        if (declaration.Modifiers.Any(SyntaxKind.FileKeyword))
            Add(detections, declaration, "file-scoped-type");
        if (declaration is StructDeclarationSyntax
            && declaration.Modifiers.Any(SyntaxKind.RefKeyword))
        {
            Add(detections, declaration, "ref-struct");
        }
    }

    private static string? GetUnsupportedInterfaceFeature(
        MemberDeclarationSyntax member)
        => member switch
        {
            MethodDeclarationSyntax method
                when !IsRepresentableInterfaceMethod(method)
                => "interface-method-semantics",
            PropertyDeclarationSyntax property
                when !IsRepresentableInterfaceProperty(property)
                => "interface-property-semantics",
            IndexerDeclarationSyntax indexer
                when !IsRepresentableInterfaceIndexer(indexer)
                => "interface-indexer-semantics",
            MethodDeclarationSyntax or PropertyDeclarationSyntax
                or IndexerDeclarationSyntax => null,
            RecordDeclarationSyntax => "record",
            _ => "interface-member"
        };

    private static bool IsRepresentableInterfaceMethod(MethodDeclarationSyntax method)
        => method.Body == null
            && method.ExpressionBody == null
            && method.SemicolonToken.IsKind(SyntaxKind.SemicolonToken)
            && method.ExplicitInterfaceSpecifier == null
            && method.ReturnType is not RefTypeSyntax
            && HasOnlyAbstractPublicModifiers(method.Modifiers);

    private static bool IsRepresentableInterfaceProperty(PropertyDeclarationSyntax property)
        => property.ExpressionBody == null
            && property.Initializer == null
            && property.ExplicitInterfaceSpecifier == null
            && property.AccessorList != null
            && property.AccessorList.Accessors.All(accessor =>
                accessor.Body == null
                && accessor.ExpressionBody == null
                && accessor.SemicolonToken.IsKind(SyntaxKind.SemicolonToken))
            && HasOnlyAbstractPublicModifiers(property.Modifiers);

    private static bool IsRepresentableInterfaceIndexer(IndexerDeclarationSyntax indexer)
        => indexer.ExpressionBody == null
            && indexer.ExplicitInterfaceSpecifier == null
            && indexer.AccessorList != null
            && indexer.AccessorList.Accessors.All(accessor =>
                accessor.Body == null
                && accessor.ExpressionBody == null
                && accessor.SemicolonToken.IsKind(SyntaxKind.SemicolonToken))
            && HasOnlyAbstractPublicModifiers(indexer.Modifiers);

    private static bool HasOnlyAbstractPublicModifiers(SyntaxTokenList modifiers)
        => modifiers.All(modifier =>
            modifier.IsKind(SyntaxKind.PublicKeyword)
            || modifier.IsKind(SyntaxKind.AbstractKeyword));

    /// <summary>
    /// Returns whether the interface declaration itself can be emitted exactly.
    /// The current emitter always writes a public, non-partial interface.
    /// </summary>
    public static bool IsRepresentableInterfaceDeclaration(
        InterfaceDeclarationSyntax node)
        => node.Modifiers.Count == 1
            && node.Modifiers[0].IsKind(SyntaxKind.PublicKeyword);

    /// <summary>
    /// Returns whether the delegate surface can be represented exactly by the
    /// current native DelegateDefinitionNode.
    /// </summary>
    public static bool IsRepresentableDelegate(DelegateDeclarationSyntax node)
        => node.TypeParameterList == null
            && node.ConstraintClauses.Count == 0
            && node.AttributeLists.Count == 0
            && node.ReturnType is not RefTypeSyntax
            && node.Modifiers.Count == 1
            && node.Modifiers[0].IsKind(SyntaxKind.PublicKeyword);

    private static void Add(
        ICollection<SyntaxCapabilityDetection> detections,
        SyntaxNode node,
        string featureName)
        => detections.Add(CreateDetection(node, featureName));

    private static SyntaxCapabilityDetection CreateDetection(
        SyntaxNode node,
        string featureName)
    {
        var position = node.GetLocation().GetLineSpan().StartLinePosition;
        return new SyntaxCapabilityDetection(
            featureName,
            node.Kind().ToString(),
            position.Line + 1,
            position.Character + 1,
            node.SpanStart,
            node.Span.Length);
    }
}
