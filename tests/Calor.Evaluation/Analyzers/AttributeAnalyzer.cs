using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Calor.Evaluation.Analyzers;

/// <summary>
/// Roslyn analyzer that checks for proper use of purity attributes.
/// Suggests adding [Pure] attribute to methods that appear to be pure.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AttributeAnalyzer : DiagnosticAnalyzer
{
    // ED007: Missing [Pure] attribute
    public const string MissingPureDiagnosticId = "ED007";
    private static readonly LocalizableString MissingPureTitle = "Pure method missing [Pure] attribute";
    private static readonly LocalizableString MissingPureMessage = "Method '{0}' appears to be pure but is missing the [Pure] attribute. Add [Pure] to document the method's contract.";
    private static readonly LocalizableString MissingPureDescription = "Pure methods should be marked with [Pure] to document their side-effect-free contract.";

    private static readonly DiagnosticDescriptor MissingPureRule = new(
        MissingPureDiagnosticId,
        MissingPureTitle,
        MissingPureMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: MissingPureDescription);

    // Side effect indicators
    private static readonly HashSet<string> SideEffectIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        // I/O types
        "Console",
        "File",
        "Directory",
        "Stream",
        "HttpClient",
        "WebClient",
        "Socket",

        // State mutation
        "Random",
        "DateTime",
        "Guid",

        // Logging
        "Logger",
        "Log",
        "ILogger"
    };

    // Side effect method patterns
    private static readonly HashSet<string> SideEffectMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Write",
        "WriteLine",
        "Read",
        "ReadLine",
        "Send",
        "Receive",
        "Log",
        "Save",
        "Delete",
        "Create",
        "Update",
        "Insert"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingPureRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        // Skip if already has [Pure] attribute
        if (HasPureAttribute(methodDeclaration))
        {
            return;
        }

        // Skip if method has void return type (likely side-effecting)
        if (methodDeclaration.ReturnType is PredefinedTypeSyntax predefined &&
            predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return;
        }

        // Skip if method is not static (might use instance state)
        var isStatic = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Skip async methods (typically have side effects)
        var isAsync = methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword);
        if (isAsync)
        {
            return;
        }

        // Check if method body appears pure
        if (methodDeclaration.Body != null && AppearsPure(methodDeclaration.Body, context))
        {
            // Only suggest [Pure] for static methods that appear pure
            // Non-static methods might have implicit state dependencies
            if (isStatic)
            {
                var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
                if (methodSymbol != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingPureRule,
                        methodDeclaration.Identifier.GetLocation(),
                        methodSymbol.Name));
                }
            }
        }
    }

    private static bool HasPureAttribute(MethodDeclarationSyntax method)
    {
        return method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name == "Pure" ||
                       name == "PureAttribute" ||
                       name.EndsWith(".Pure") ||
                       name.EndsWith(".PureAttribute");
            });
    }

    private static bool AppearsPure(BlockSyntax body, SyntaxNodeAnalysisContext context)
    {
        // Check all descendant nodes for side effect indicators
        foreach (var node in body.DescendantNodes())
        {
            // Check for assignment to fields or properties outside method
            if (node is AssignmentExpressionSyntax assignment)
            {
                // Allow local variable assignments
                if (assignment.Left is IdentifierNameSyntax identifier)
                {
                    var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
                    if (symbol is IFieldSymbol || symbol is IPropertySymbol)
                    {
                        return false; // Modifies state
                    }
                }
            }

            // Check for method calls that might have side effects
            if (node is InvocationExpressionSyntax invocation)
            {
                var expression = invocation.Expression.ToString();

                // Check for known side effect indicators
                foreach (var indicator in SideEffectIndicators)
                {
                    if (expression.IndexOf(indicator, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }

                // Check for side effect method names
                var methodName = GetMethodName(invocation);
                if (methodName != null)
                {
                    foreach (var sideEffectMethod in SideEffectMethods)
                    {
                        if (methodName.IndexOf(sideEffectMethod, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return false;
                        }
                    }
                }
            }

            // Check for object creation of side effect types
            if (node is ObjectCreationExpressionSyntax objectCreation)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation);
                if (typeInfo.Type != null)
                {
                    var typeName = typeInfo.Type.Name;
                    if (SideEffectIndicators.Contains(typeName))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }
}
