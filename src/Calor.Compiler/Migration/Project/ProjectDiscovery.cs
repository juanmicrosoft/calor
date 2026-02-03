using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharpExtensions;

namespace Calor.Compiler.Migration.Project;

/// <summary>
/// Discovers files to migrate in a project or directory.
/// </summary>
public sealed class ProjectDiscovery
{
    private readonly MigrationPlanOptions _options;

    public ProjectDiscovery(MigrationPlanOptions? options = null)
    {
        _options = options ?? new MigrationPlanOptions();
    }

    /// <summary>
    /// Discovers all C# files in a project directory.
    /// </summary>
    public async Task<MigrationPlan> DiscoverCSharpFilesAsync(string projectPath, MigrationDirection direction)
    {
        var entries = new List<MigrationPlanEntry>();

        // Determine if projectPath is a .csproj file or directory
        string searchPath;
        if (File.Exists(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            searchPath = Path.GetDirectoryName(projectPath) ?? projectPath;
        }
        else if (Directory.Exists(projectPath))
        {
            searchPath = projectPath;
        }
        else
        {
            throw new DirectoryNotFoundException($"Project path not found: {projectPath}");
        }

        // Find all C# files
        var csFiles = Directory.EnumerateFiles(searchPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldExclude(f))
            .ToList();

        foreach (var csFile in csFiles)
        {
            var entry = await AnalyzeFileAsync(csFile, direction);
            entries.Add(entry);
        }

        return new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = direction,
            Entries = entries,
            Options = _options
        };
    }

    /// <summary>
    /// Discovers all Calor files in a project directory.
    /// </summary>
    public async Task<MigrationPlan> DiscoverCalorFilesAsync(string projectPath)
    {
        var entries = new List<MigrationPlanEntry>();

        string searchPath = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath) ?? projectPath;

        var calorFiles = Directory.EnumerateFiles(searchPath, "*.calr", SearchOption.AllDirectories)
            .ToList();

        foreach (var calorFile in calorFiles)
        {
            var entry = await AnalyzeCalorFileAsync(calorFile);
            entries.Add(entry);
        }

        return new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = MigrationDirection.CalorToCSharp,
            Entries = entries,
            Options = _options
        };
    }

    private bool ShouldExclude(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var relativePath = filePath.Replace('\\', '/');

        // Check exclude patterns
        foreach (var pattern in _options.ExcludePatterns)
        {
            if (MatchesPattern(fileName, pattern) || MatchesPattern(relativePath, pattern))
            {
                return true;
            }
        }

        // Check if it's in a common exclude directory
        var excludeDirs = new[] { "bin", "obj", "node_modules", ".git", ".vs" };
        foreach (var dir in excludeDirs)
        {
            if (relativePath.Contains($"/{dir}/") || relativePath.Contains($"\\{dir}\\"))
            {
                return true;
            }
        }

        // Check file size
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > _options.MaxFileSizeBytes)
            {
                return true;
            }
        }
        catch
        {
            // If we can't check file size, include it
        }

        return false;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        // Simple wildcard matching
        if (pattern.StartsWith("*"))
        {
            return fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.EndsWith("*"))
        {
            return fileName.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MigrationPlanEntry> AnalyzeFileAsync(string filePath, MigrationDirection direction)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var fileInfo = new FileInfo(filePath);

        var detectedFeatures = new List<string>();
        var potentialIssues = new List<string>();
        var estimatedIssues = 0;
        var convertibility = FileConvertibility.Full;
        string? skipReason = null;

        try
        {
            // Parse the file to detect features
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var root = syntaxTree.GetCompilationUnitRoot();

            // Check for parse errors
            var errors = root.GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToList();

            if (errors.Count > 0)
            {
                convertibility = FileConvertibility.Skip;
                skipReason = $"Parse errors: {errors.Count}";
                estimatedIssues = errors.Count;
            }
            else
            {
                // Detect features
                var detector = new FeatureDetector();
                detector.Visit(root);

                detectedFeatures = detector.DetectedFeatures.ToList();

                // Check for unsupported features
                foreach (var feature in detectedFeatures)
                {
                    var info = FeatureSupport.GetFeatureInfo(feature);
                    if (info != null)
                    {
                        switch (info.Support)
                        {
                            case SupportLevel.NotSupported:
                                potentialIssues.Add($"Unsupported feature: {feature}");
                                estimatedIssues++;
                                convertibility = FileConvertibility.Partial;
                                break;
                            case SupportLevel.Partial:
                            case SupportLevel.ManualRequired:
                                potentialIssues.Add($"Partially supported: {feature}");
                                if (convertibility == FileConvertibility.Full)
                                    convertibility = FileConvertibility.Partial;
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            convertibility = FileConvertibility.Skip;
            skipReason = $"Analysis failed: {ex.Message}";
            estimatedIssues = 1;
        }

        var outputPath = direction == MigrationDirection.CSharpToCalor
            ? Path.ChangeExtension(filePath, ".calr")
            : Path.ChangeExtension(filePath, ".g.cs");

        return new MigrationPlanEntry
        {
            SourcePath = filePath,
            OutputPath = outputPath,
            Convertibility = convertibility,
            DetectedFeatures = detectedFeatures,
            PotentialIssues = potentialIssues,
            EstimatedIssues = estimatedIssues,
            FileSizeBytes = fileInfo.Length,
            SkipReason = skipReason
        };
    }

    private Task<MigrationPlanEntry> AnalyzeCalorFileAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        return Task.FromResult(new MigrationPlanEntry
        {
            SourcePath = filePath,
            OutputPath = Path.ChangeExtension(filePath, ".g.cs"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = fileInfo.Length
        });
    }
}

/// <summary>
/// Detects C# features used in source code.
/// </summary>
internal sealed class FeatureDetector : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxWalker
{
    public HashSet<string> DetectedFeatures { get; } = new();

    public override void VisitClassDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax node)
    {
        DetectedFeatures.Add("class");
        base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax node)
    {
        DetectedFeatures.Add("interface");
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax node)
    {
        DetectedFeatures.Add("record");
        base.VisitRecordDeclaration(node);
    }

    public override void VisitStructDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax node)
    {
        DetectedFeatures.Add("struct");
        base.VisitStructDeclaration(node);
    }

    public override void VisitMethodDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax node)
    {
        DetectedFeatures.Add("method");

        if (node.Modifiers.Any(m => CSharpExtensions.IsKind(m, AsyncKeyword)))
        {
            DetectedFeatures.Add("async-await");
        }

        base.VisitMethodDeclaration(node);
    }

    public override void VisitPropertyDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax node)
    {
        DetectedFeatures.Add("property");
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitFieldDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax node)
    {
        DetectedFeatures.Add("field");
        base.VisitFieldDeclaration(node);
    }

    public override void VisitConstructorDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax node)
    {
        DetectedFeatures.Add("constructor");
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitIfStatement(Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax node)
    {
        DetectedFeatures.Add("if");
        base.VisitIfStatement(node);
    }

    public override void VisitForStatement(Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax node)
    {
        DetectedFeatures.Add("for");
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax node)
    {
        DetectedFeatures.Add("foreach");
        base.VisitForEachStatement(node);
    }

    public override void VisitWhileStatement(Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax node)
    {
        DetectedFeatures.Add("while");
        base.VisitWhileStatement(node);
    }

    public override void VisitSwitchStatement(Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax node)
    {
        DetectedFeatures.Add("switch");
        base.VisitSwitchStatement(node);
    }

    public override void VisitTryStatement(Microsoft.CodeAnalysis.CSharp.Syntax.TryStatementSyntax node)
    {
        DetectedFeatures.Add("try-catch");
        base.VisitTryStatement(node);
    }

    public override void VisitSimpleLambdaExpression(Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax node)
    {
        DetectedFeatures.Add("lambda");
        base.VisitSimpleLambdaExpression(node);
    }

    public override void VisitParenthesizedLambdaExpression(Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax node)
    {
        DetectedFeatures.Add("lambda");
        base.VisitParenthesizedLambdaExpression(node);
    }

    public override void VisitAwaitExpression(Microsoft.CodeAnalysis.CSharp.Syntax.AwaitExpressionSyntax node)
    {
        DetectedFeatures.Add("async-await");
        base.VisitAwaitExpression(node);
    }

    public override void VisitGenericName(Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax node)
    {
        DetectedFeatures.Add("generics");
        base.VisitGenericName(node);
    }

    public override void VisitInterpolatedStringExpression(Microsoft.CodeAnalysis.CSharp.Syntax.InterpolatedStringExpressionSyntax node)
    {
        DetectedFeatures.Add("string-interpolation");
        base.VisitInterpolatedStringExpression(node);
    }

    public override void VisitBinaryExpression(Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax node)
    {
        if (CSharpExtensions.IsKind(node, CoalesceExpression))
        {
            DetectedFeatures.Add("null-coalescing");
        }
        base.VisitBinaryExpression(node);
    }

    public override void VisitConditionalAccessExpression(Microsoft.CodeAnalysis.CSharp.Syntax.ConditionalAccessExpressionSyntax node)
    {
        DetectedFeatures.Add("null-conditional");
        base.VisitConditionalAccessExpression(node);
    }

    public override void VisitQueryExpression(Microsoft.CodeAnalysis.CSharp.Syntax.QueryExpressionSyntax node)
    {
        DetectedFeatures.Add("linq-query");
        base.VisitQueryExpression(node);
    }

    public override void VisitGotoStatement(Microsoft.CodeAnalysis.CSharp.Syntax.GotoStatementSyntax node)
    {
        DetectedFeatures.Add("goto");
        base.VisitGotoStatement(node);
    }

    public override void VisitLabeledStatement(Microsoft.CodeAnalysis.CSharp.Syntax.LabeledStatementSyntax node)
    {
        DetectedFeatures.Add("labeled-statement");
        base.VisitLabeledStatement(node);
    }

    public override void VisitUnsafeStatement(Microsoft.CodeAnalysis.CSharp.Syntax.UnsafeStatementSyntax node)
    {
        DetectedFeatures.Add("unsafe");
        base.VisitUnsafeStatement(node);
    }

    public override void VisitPointerType(Microsoft.CodeAnalysis.CSharp.Syntax.PointerTypeSyntax node)
    {
        DetectedFeatures.Add("pointer");
        base.VisitPointerType(node);
    }

    public override void VisitStackAllocArrayCreationExpression(Microsoft.CodeAnalysis.CSharp.Syntax.StackAllocArrayCreationExpressionSyntax node)
    {
        DetectedFeatures.Add("stackalloc");
        base.VisitStackAllocArrayCreationExpression(node);
    }

    public override void VisitFixedStatement(Microsoft.CodeAnalysis.CSharp.Syntax.FixedStatementSyntax node)
    {
        DetectedFeatures.Add("fixed");
        base.VisitFixedStatement(node);
    }

    public override void VisitParameter(Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax node)
    {
        if (node.Modifiers.Any(m => CSharpExtensions.IsKind(m, RefKeyword)))
        {
            DetectedFeatures.Add("ref-parameter");
        }
        if (node.Modifiers.Any(m => CSharpExtensions.IsKind(m, OutKeyword)))
        {
            DetectedFeatures.Add("out-parameter");
        }
        base.VisitParameter(node);
    }

    public override void VisitConversionOperatorDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.ConversionOperatorDeclarationSyntax node)
    {
        if (CSharpExtensions.IsKind(node.ImplicitOrExplicitKeyword, ImplicitKeyword))
        {
            DetectedFeatures.Add("implicit-conversion");
        }
        else
        {
            DetectedFeatures.Add("explicit-conversion");
        }
        base.VisitConversionOperatorDeclaration(node);
    }

    public override void VisitOperatorDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.OperatorDeclarationSyntax node)
    {
        DetectedFeatures.Add("operator-overload");
        base.VisitOperatorDeclaration(node);
    }
}
