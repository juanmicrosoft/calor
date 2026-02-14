using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Calor.Evaluation.Analyzers;

/// <summary>
/// Roslyn analyzer that detects purity violations that cause non-deterministic behavior.
/// These violations lead to flaky tests and cache safety issues.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PurityAnalyzer : DiagnosticAnalyzer
{
    // ED001: DateTime.Now/UtcNow usage
    public const string DateTimeNowDiagnosticId = "ED001";
    private static readonly LocalizableString DateTimeNowTitle = "DateTime.Now usage detected";
    private static readonly LocalizableString DateTimeNowMessage = "Direct call to DateTime.Now or DateTime.UtcNow causes non-deterministic behavior. Pass time as a parameter instead.";
    private static readonly LocalizableString DateTimeNowDescription = "Functions that use DateTime.Now are not deterministic and can cause flaky tests.";

    private static readonly DiagnosticDescriptor DateTimeNowRule = new(
        DateTimeNowDiagnosticId,
        DateTimeNowTitle,
        DateTimeNowMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: DateTimeNowDescription);

    // ED002: Unseeded Random usage
    public const string RandomDiagnosticId = "ED002";
    private static readonly LocalizableString RandomTitle = "Unseeded Random usage detected";
    private static readonly LocalizableString RandomMessage = "Creating Random without a seed causes non-deterministic behavior. Use a seeded Random or pass randomness as a parameter.";
    private static readonly LocalizableString RandomDescription = "Functions that use unseeded Random are not deterministic and can cause flaky tests.";

    private static readonly DiagnosticDescriptor RandomRule = new(
        RandomDiagnosticId,
        RandomTitle,
        RandomMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: RandomDescription);

    // ED003: Guid.NewGuid usage
    public const string GuidDiagnosticId = "ED003";
    private static readonly LocalizableString GuidTitle = "Guid.NewGuid usage detected";
    private static readonly LocalizableString GuidMessage = "Guid.NewGuid() generates non-deterministic values. Use deterministic ID generation from parameters instead.";
    private static readonly LocalizableString GuidDescription = "Functions that use Guid.NewGuid() are not deterministic and can cause flaky tests.";

    private static readonly DiagnosticDescriptor GuidRule = new(
        GuidDiagnosticId,
        GuidTitle,
        GuidMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: GuidDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DateTimeNowRule, RandomRule, GuidRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for member access expressions (DateTime.Now, Guid.NewGuid)
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);

        // Register for object creation expressions (new Random())
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        // Check for DateTime.Now or DateTime.UtcNow
        if (memberName == "Now" || memberName == "UtcNow" || memberName == "Today")
        {
            var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (expressionType?.Name == "DateTime" &&
                expressionType.ContainingNamespace?.ToDisplayString() == "System")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DateTimeNowRule,
                    memberAccess.GetLocation()));
            }
        }

        // Check for Guid.NewGuid()
        if (memberName == "NewGuid")
        {
            var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (expressionType?.Name == "Guid" &&
                expressionType.ContainingNamespace?.ToDisplayString() == "System")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GuidRule,
                    memberAccess.GetLocation()));
            }
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation);

        // Check for new Random() without seed
        if (typeInfo.Type?.Name == "Random" &&
            typeInfo.Type.ContainingNamespace?.ToDisplayString() == "System")
        {
            // Check if constructor has no arguments (unseeded)
            if (objectCreation.ArgumentList == null ||
                objectCreation.ArgumentList.Arguments.Count == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RandomRule,
                    objectCreation.GetLocation()));
            }
        }
    }
}
