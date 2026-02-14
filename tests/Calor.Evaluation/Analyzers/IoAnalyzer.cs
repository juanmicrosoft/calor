using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Calor.Evaluation.Analyzers;

/// <summary>
/// Roslyn analyzer that detects I/O operations that violate security boundaries.
/// These violations can cause unauthorized network access, file operations, or console output.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IoAnalyzer : DiagnosticAnalyzer
{
    // ED004: Network access
    public const string NetworkDiagnosticId = "ED004";
    private static readonly LocalizableString NetworkTitle = "Network access detected";
    private static readonly LocalizableString NetworkMessage = "Network access via {0} violates security boundaries. Remove network calls for offline/sandboxed operations.";
    private static readonly LocalizableString NetworkDescription = "Functions that make network calls violate security boundaries and can leak data.";

    private static readonly DiagnosticDescriptor NetworkRule = new(
        NetworkDiagnosticId,
        NetworkTitle,
        NetworkMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: NetworkDescription);

    // ED005: Console output
    public const string ConsoleDiagnosticId = "ED005";
    private static readonly LocalizableString ConsoleTitle = "Console output detected";
    private static readonly LocalizableString ConsoleMessage = "Console.{0} in utility functions is a side effect. Remove console output for pure operations.";
    private static readonly LocalizableString ConsoleDescription = "Utility functions should not write to the console as it's a side effect.";

    private static readonly DiagnosticDescriptor ConsoleRule = new(
        ConsoleDiagnosticId,
        ConsoleTitle,
        ConsoleMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: ConsoleDescription);

    // ED006: File operations
    public const string FileDiagnosticId = "ED006";
    private static readonly LocalizableString FileTitle = "File operation detected";
    private static readonly LocalizableString FileMessage = "File.{0} violates security boundaries. Remove file operations for sandboxed operations.";
    private static readonly LocalizableString FileDescription = "Functions that perform file I/O violate security boundaries.";

    private static readonly DiagnosticDescriptor FileRule = new(
        FileDiagnosticId,
        FileTitle,
        FileMessage,
        "EffectDiscipline",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: FileDescription);

    // Network types to detect
    private static readonly HashSet<string> NetworkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HttpClient",
        "WebClient",
        "WebRequest",
        "HttpWebRequest",
        "Socket",
        "TcpClient",
        "TcpListener",
        "UdpClient"
    };

    // Console methods to detect
    private static readonly HashSet<string> ConsoleMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Write",
        "WriteLine",
        "Error",
        "ReadLine",
        "Read",
        "ReadKey"
    };

    // File methods to detect
    private static readonly HashSet<string> FileMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReadAllText",
        "ReadAllBytes",
        "ReadAllLines",
        "WriteAllText",
        "WriteAllBytes",
        "WriteAllLines",
        "Open",
        "Create",
        "Delete",
        "Copy",
        "Move",
        "Exists",
        "AppendAllText",
        "AppendAllLines"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(NetworkRule, ConsoleRule, FileRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for object creation (network types)
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);

        // Register for member access (Console, File methods)
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation);

        if (typeInfo.Type == null) return;

        var typeName = typeInfo.Type.Name;

        // Check for network types
        if (NetworkTypes.Contains(typeName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NetworkRule,
                objectCreation.GetLocation(),
                typeName));
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (expressionType == null) return;

        var typeName = expressionType.Name;
        var namespaceName = expressionType.ContainingNamespace?.ToDisplayString() ?? "";

        // Check for Console methods
        if (typeName == "Console" && namespaceName == "System")
        {
            if (ConsoleMethods.Contains(memberName) ||
                memberName == "Error" ||
                memberName == "Out")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ConsoleRule,
                    memberAccess.GetLocation(),
                    memberName));
            }
        }

        // Check for File methods
        if (typeName == "File" && namespaceName == "System.IO")
        {
            if (FileMethods.Contains(memberName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FileRule,
                    memberAccess.GetLocation(),
                    memberName));
            }
        }

        // Check for Directory methods
        if (typeName == "Directory" && namespaceName == "System.IO")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FileRule,
                memberAccess.GetLocation(),
                $"Directory.{memberName}"));
        }

        // Check for network async methods (GetAsync, PostAsync, etc.)
        if (NetworkTypes.Contains(typeName) ||
            (memberName.EndsWith("Async") &&
             (memberName.StartsWith("Get") || memberName.StartsWith("Post") ||
              memberName.StartsWith("Put") || memberName.StartsWith("Delete") ||
              memberName.StartsWith("Send"))))
        {
            // Additional check for HttpClient methods
            if (typeName == "HttpClient" ||
                memberAccess.Expression.ToString().Contains("HttpClient"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NetworkRule,
                    memberAccess.GetLocation(),
                    memberName));
            }
        }
    }
}
