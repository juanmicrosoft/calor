using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Options for migration analysis.
/// </summary>
public sealed class MigrationAnalysisOptions
{
    public bool Verbose { get; init; }
    public AnalysisThresholds Thresholds { get; init; } = new();
    public IProgress<string>? Progress { get; init; }
}

/// <summary>
/// Analyzes C# code to score its potential benefit from Calor migration.
/// </summary>
public sealed class MigrationAnalyzer
{
    private readonly MigrationAnalysisOptions _options;

    private static readonly string[] GeneratedFileSuffixes = { ".g.cs", ".generated.cs", ".Designer.cs" };
    private static readonly string[] SkippedDirectories = { "obj", "bin", ".git", "node_modules" };

    public MigrationAnalyzer(MigrationAnalysisOptions? options = null)
    {
        _options = options ?? new MigrationAnalysisOptions();
    }

    /// <summary>
    /// Analyzes a single C# file for Calor migration potential.
    /// </summary>
    public async Task<FileMigrationScore> AnalyzeFileAsync(string filePath, string? basePath = null)
    {
        var relativePath = basePath != null
            ? Path.GetRelativePath(basePath, filePath)
            : Path.GetFileName(filePath);

        // Check if file should be skipped
        var skipReason = GetSkipReason(filePath);
        if (skipReason != null)
        {
            return new FileMigrationScore
            {
                FilePath = filePath,
                RelativePath = relativePath,
                WasSkipped = true,
                SkipReason = skipReason,
                Priority = MigrationPriority.Low
            };
        }

        try
        {
            var source = await File.ReadAllTextAsync(filePath);
            return AnalyzeSource(source, filePath, relativePath);
        }
        catch (Exception ex)
        {
            return new FileMigrationScore
            {
                FilePath = filePath,
                RelativePath = relativePath,
                WasSkipped = true,
                SkipReason = $"Error reading file: {ex.Message}",
                Priority = MigrationPriority.Low
            };
        }
    }

    /// <summary>
    /// Analyzes C# source code for Calor migration potential.
    /// </summary>
    public FileMigrationScore AnalyzeSource(string source, string filePath, string relativePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        // Check for parse errors
        var errors = root.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            return new FileMigrationScore
            {
                FilePath = filePath,
                RelativePath = relativePath,
                WasSkipped = true,
                SkipReason = $"Parse errors: {errors.Count}",
                Priority = MigrationPriority.Low
            };
        }

        // Run the analysis visitor
        var visitor = new MigrationAnalysisVisitor(_options.Verbose);
        visitor.Visit(root);

        // Calculate dimension scores
        var dimensions = CalculateDimensionScores(visitor, source);

        // Build list of unsupported constructs
        var unsupportedConstructs = BuildUnsupportedConstructsList(visitor);

        // Calculate total weighted score
        var totalScore = dimensions.Values.Sum(d => d.WeightedScore);
        totalScore = Math.Min(100, totalScore); // Cap at 100

        // Apply severe penalty for unsupported constructs
        // Files with unsupported constructs are not viable for conversion
        if (unsupportedConstructs.Count > 0)
        {
            var totalUnsupported = unsupportedConstructs.Sum(c => c.Count);

            // Strategy: Each unsupported construct type reduces score by 50%
            // Multiple construct types compound the reduction
            // E.g., 2 types = 75% reduction, 3 types = 87.5% reduction
            var penaltyMultiplier = Math.Pow(0.5, unsupportedConstructs.Count);

            // Also apply per-instance penalty (5 points each)
            var instancePenalty = totalUnsupported * 5;

            totalScore = Math.Max(0, (totalScore * penaltyMultiplier) - instancePenalty);
        }

        return new FileMigrationScore
        {
            FilePath = filePath,
            RelativePath = relativePath,
            TotalScore = totalScore,
            Priority = FileMigrationScore.GetPriority(totalScore),
            Dimensions = dimensions,
            LineCount = source.Split('\n').Length,
            MethodCount = visitor.MethodCount,
            TypeCount = visitor.TypeCount,
            UnsupportedConstructs = unsupportedConstructs,
            WasSkipped = false
        };
    }

    private static List<UnsupportedConstruct> BuildUnsupportedConstructsList(MigrationAnalysisVisitor visitor)
    {
        var result = new List<UnsupportedConstruct>();

        // Switch expressions are now supported - no longer tracking as unsupported

        if (visitor.RelationalPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "RelationalPattern",
                Description = "Relational patterns (is > x, is < x) not yet supported",
                Count = visitor.RelationalPatternCount,
                Examples = visitor.RelationalPatternExamples
            });
        }

        if (visitor.CompoundPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "CompoundPattern",
                Description = "Compound patterns (and/or) not yet supported",
                Count = visitor.CompoundPatternCount,
                Examples = visitor.CompoundPatternExamples
            });
        }

        if (visitor.RangeExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "RangeExpression",
                Description = "Range expressions (0..5, ..5, 5..) not yet supported",
                Count = visitor.RangeExpressionCount,
                Examples = visitor.RangeExpressionExamples
            });
        }

        if (visitor.IndexExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "IndexFromEnd",
                Description = "Index from end expressions (^1) not yet supported",
                Count = visitor.IndexExpressionCount,
                Examples = visitor.IndexExpressionExamples
            });
        }

        if (visitor.ImplicitObjectCreationCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "ImplicitObjectCreation",
                Description = "Target-typed new expressions (new(...)) not yet supported",
                Count = visitor.ImplicitObjectCreationCount,
                Examples = visitor.ImplicitObjectCreationExamples
            });
        }

        if (visitor.ConditionalAccessMethodCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "ConditionalAccessMethod",
                Description = "Null-conditional method calls (?.Method()) not yet supported",
                Count = visitor.ConditionalAccessMethodCount,
                Examples = visitor.ConditionalAccessMethodExamples
            });
        }

        if (visitor.NamedArgumentCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "NamedArgument",
                Description = "Named arguments (param: value) not yet supported",
                Count = visitor.NamedArgumentCount,
                Examples = visitor.NamedArgumentExamples
            });
        }

        if (visitor.PrimaryConstructorCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "PrimaryConstructor",
                Description = "Primary constructors (class Foo(int x)) not yet supported",
                Count = visitor.PrimaryConstructorCount,
                Examples = visitor.PrimaryConstructorExamples
            });
        }

        if (visitor.OutRefParameterCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "OutRefParameter",
                Description = "out/ref parameters not yet supported",
                Count = visitor.OutRefParameterCount,
                Examples = visitor.OutRefParameterExamples
            });
        }

        if (visitor.VarDeclarationInArgumentCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "VarDeclarationInArgument",
                Description = "Inline variable declarations (out var x) not yet supported",
                Count = visitor.VarDeclarationInArgumentCount,
                Examples = visitor.VarDeclarationInArgumentExamples
            });
        }

        if (visitor.DeclarationPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "DeclarationPattern",
                Description = "Declaration patterns (is Type varName) not yet supported",
                Count = visitor.DeclarationPatternCount,
                Examples = visitor.DeclarationPatternExamples
            });
        }

        if (visitor.NestedGenericTypeCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "NestedGenericType",
                Description = "Nested generic types (Expression<Func<T, U>>) not yet supported",
                Count = visitor.NestedGenericTypeCount,
                Examples = visitor.NestedGenericTypeExamples
            });
        }

        // Lambda expressions are now supported - no longer tracking as unsupported

        if (visitor.ThrowExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "ThrowExpression",
                Description = "Throw expressions (?? throw new ...) not yet supported",
                Count = visitor.ThrowExpressionCount,
                Examples = visitor.ThrowExpressionExamples
            });
        }

        if (visitor.GenericTypeConstraintCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "GenericTypeConstraint",
                Description = "Generic type constraints (where T : class) not yet supported",
                Count = visitor.GenericTypeConstraintCount,
                Examples = visitor.GenericTypeConstraintExamples
            });
        }

        return result;
    }

    /// <summary>
    /// Analyzes all C# files in a directory recursively.
    /// </summary>
    public async Task<ProjectAnalysisResult> AnalyzeDirectoryAsync(string directoryPath)
    {
        var startTime = DateTime.UtcNow;

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var absolutePath = Path.GetFullPath(directoryPath);
        var files = GetCSharpFiles(absolutePath);
        var results = new List<FileMigrationScore>();
        var skipped = new List<FileMigrationScore>();

        var totalFiles = files.Count;
        var processed = 0;

        foreach (var file in files)
        {
            processed++;
            _options.Progress?.Report($"Analyzing ({processed}/{totalFiles}): {Path.GetFileName(file)}");

            var result = await AnalyzeFileAsync(file, absolutePath);

            if (result.WasSkipped)
            {
                skipped.Add(result);
            }
            else
            {
                results.Add(result);
            }
        }

        return new ProjectAnalysisResult
        {
            RootPath = absolutePath,
            Duration = DateTime.UtcNow - startTime,
            Files = results,
            SkippedFiles = skipped
        };
    }

    private static string? GetSkipReason(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // Check for generated file suffixes
        foreach (var suffix in GeneratedFileSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return "Generated file";
            }
        }

        // Check if in skipped directory
        var normalizedPath = filePath.Replace('\\', '/');
        foreach (var dir in SkippedDirectories)
        {
            if (normalizedPath.Contains($"/{dir}/", StringComparison.OrdinalIgnoreCase))
            {
                return $"In {dir}/ directory";
            }
        }

        return null;
    }

    private static List<string> GetCSharpFiles(string directory)
    {
        var files = new List<string>();

        try
        {
            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                files.Add(file);
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                if (!SkippedDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                {
                    files.AddRange(GetCSharpFiles(subDir));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }

        return files;
    }

    private Dictionary<ScoreDimension, DimensionScore> CalculateDimensionScores(
        MigrationAnalysisVisitor visitor,
        string source)
    {
        var lineCount = Math.Max(1, source.Split('\n').Length);
        var methodCount = Math.Max(1, visitor.MethodCount);

        return new Dictionary<ScoreDimension, DimensionScore>
        {
            [ScoreDimension.ContractPotential] = CreateDimensionScore(
                ScoreDimension.ContractPotential,
                visitor.ContractPatterns,
                methodCount,
                visitor.ContractExamples),

            [ScoreDimension.EffectPotential] = CreateDimensionScore(
                ScoreDimension.EffectPotential,
                visitor.EffectPatterns,
                methodCount,
                visitor.EffectExamples),

            [ScoreDimension.NullSafetyPotential] = CreateDimensionScore(
                ScoreDimension.NullSafetyPotential,
                visitor.NullSafetyPatterns,
                Math.Max(1, lineCount / 10),
                visitor.NullSafetyExamples),

            [ScoreDimension.ErrorHandlingPotential] = CreateDimensionScore(
                ScoreDimension.ErrorHandlingPotential,
                visitor.ErrorHandlingPatterns,
                methodCount,
                visitor.ErrorHandlingExamples),

            [ScoreDimension.PatternMatchPotential] = CreateDimensionScore(
                ScoreDimension.PatternMatchPotential,
                visitor.PatternMatchPatterns,
                methodCount,
                visitor.PatternMatchExamples),

            [ScoreDimension.ApiComplexityPotential] = CreateDimensionScore(
                ScoreDimension.ApiComplexityPotential,
                visitor.ApiComplexityPatterns,
                Math.Max(1, visitor.PublicMemberCount),
                visitor.ApiComplexityExamples),

            [ScoreDimension.AsyncPotential] = CreateDimensionScore(
                ScoreDimension.AsyncPotential,
                visitor.AsyncPatterns,
                methodCount,
                visitor.AsyncExamples),

            [ScoreDimension.LinqPotential] = CreateDimensionScore(
                ScoreDimension.LinqPotential,
                visitor.LinqPatterns,
                methodCount,
                visitor.LinqExamples)
        };
    }

    private static DimensionScore CreateDimensionScore(
        ScoreDimension dimension,
        int patternCount,
        int normalizer,
        List<string> examples)
    {
        // Calculate raw score (0-100) based on pattern density
        var density = (double)patternCount / normalizer;
        var rawScore = Math.Min(100, density * 100);

        return new DimensionScore
        {
            Dimension = dimension,
            RawScore = rawScore,
            Weight = DimensionScore.GetWeight(dimension),
            PatternCount = patternCount,
            Examples = examples.Take(5).ToList()
        };
    }
}

/// <summary>
/// Roslyn syntax walker that detects patterns relevant to Calor migration.
/// </summary>
internal sealed class MigrationAnalysisVisitor : CSharpSyntaxWalker
{
    private readonly bool _verbose;

    public int MethodCount { get; private set; }
    public int TypeCount { get; private set; }
    public int PublicMemberCount { get; private set; }

    // Contract patterns
    public int ContractPatterns { get; private set; }
    public List<string> ContractExamples { get; } = new();

    // Effect patterns
    public int EffectPatterns { get; private set; }
    public List<string> EffectExamples { get; } = new();

    // Null safety patterns
    public int NullSafetyPatterns { get; private set; }
    public List<string> NullSafetyExamples { get; } = new();

    // Error handling patterns
    public int ErrorHandlingPatterns { get; private set; }
    public List<string> ErrorHandlingExamples { get; } = new();

    // Pattern matching patterns
    public int PatternMatchPatterns { get; private set; }
    public List<string> PatternMatchExamples { get; } = new();

    // API complexity patterns
    public int ApiComplexityPatterns { get; private set; }
    public List<string> ApiComplexityExamples { get; } = new();

    // Async patterns
    public int AsyncPatterns { get; private set; }
    public List<string> AsyncExamples { get; } = new();

    // LINQ patterns
    public int LinqPatterns { get; private set; }
    public List<string> LinqExamples { get; } = new();

    // Unsupported C# constructs (converter limitations)
    public int SwitchExpressionCount { get; private set; }
    public List<string> SwitchExpressionExamples { get; } = new();

    public int RelationalPatternCount { get; private set; }
    public List<string> RelationalPatternExamples { get; } = new();

    public int CompoundPatternCount { get; private set; }
    public List<string> CompoundPatternExamples { get; } = new();

    public int RangeExpressionCount { get; private set; }
    public List<string> RangeExpressionExamples { get; } = new();

    public int IndexExpressionCount { get; private set; }
    public List<string> IndexExpressionExamples { get; } = new();

    public int ImplicitObjectCreationCount { get; private set; }
    public List<string> ImplicitObjectCreationExamples { get; } = new();

    public int ConditionalAccessMethodCount { get; private set; }
    public List<string> ConditionalAccessMethodExamples { get; } = new();

    public int NamedArgumentCount { get; private set; }
    public List<string> NamedArgumentExamples { get; } = new();

    public int PrimaryConstructorCount { get; private set; }
    public List<string> PrimaryConstructorExamples { get; } = new();

    public int OutRefParameterCount { get; private set; }
    public List<string> OutRefParameterExamples { get; } = new();

    public int VarDeclarationInArgumentCount { get; private set; }
    public List<string> VarDeclarationInArgumentExamples { get; } = new();

    public int DeclarationPatternCount { get; private set; }
    public List<string> DeclarationPatternExamples { get; } = new();

    public int NestedGenericTypeCount { get; private set; }
    public List<string> NestedGenericTypeExamples { get; } = new();

    // Lambda expressions are now supported - kept for informational purposes only
    public int LambdaExpressionCount { get; private set; }
    public List<string> LambdaExpressionExamples { get; } = new();

    public int ThrowExpressionCount { get; private set; }
    public List<string> ThrowExpressionExamples { get; } = new();

    public int GenericTypeConstraintCount { get; private set; }
    public List<string> GenericTypeConstraintExamples { get; } = new();

    private static readonly HashSet<string> ArgumentExceptionTypes = new(StringComparer.Ordinal)
    {
        "ArgumentNullException",
        "ArgumentException",
        "ArgumentOutOfRangeException"
    };

    private static readonly HashSet<string> FileIoTypes = new(StringComparer.Ordinal)
    {
        "File", "Directory", "Stream", "StreamReader", "StreamWriter",
        "FileStream", "TextReader", "TextWriter", "BinaryReader", "BinaryWriter",
        "Path"
    };

    private static readonly HashSet<string> NetworkTypes = new(StringComparer.Ordinal)
    {
        "HttpClient", "WebRequest", "WebResponse", "Socket", "TcpClient",
        "UdpClient", "HttpWebRequest", "WebClient"
    };

    private static readonly HashSet<string> DatabaseTypes = new(StringComparer.Ordinal)
    {
        "SqlCommand", "SqlConnection", "DbCommand", "DbConnection",
        "IDbCommand", "IDbConnection", "DbContext", "DbSet"
    };

    private static readonly HashSet<string> DatabaseMethods = new(StringComparer.Ordinal)
    {
        "ExecuteNonQuery", "ExecuteReader", "ExecuteScalar",
        "SaveChanges", "SaveChangesAsync"
    };

    private static readonly HashSet<string> LinqMethods = new(StringComparer.Ordinal)
    {
        "Where", "Select", "SelectMany", "First", "FirstOrDefault",
        "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Any", "All", "Count", "Sum", "Average", "Min", "Max",
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "Join", "GroupJoin", "Distinct", "Union", "Intersect", "Except",
        "Skip", "Take", "SkipWhile", "TakeWhile", "Reverse",
        "Concat", "Zip", "Aggregate", "ToList", "ToArray", "ToDictionary", "ToHashSet"
    };

    private static readonly HashSet<string> AsyncMethods = new(StringComparer.Ordinal)
    {
        "ConfigureAwait", "WhenAll", "WhenAny", "Run", "Delay",
        "FromResult", "FromException", "FromCanceled",
        "ContinueWith", "GetAwaiter", "AsTask"
    };

    private static readonly HashSet<string> AsyncTypes = new(StringComparer.Ordinal)
    {
        "Task", "ValueTask", "IAsyncEnumerable", "IAsyncEnumerator",
        "CancellationToken", "CancellationTokenSource", "IAsyncDisposable"
    };

    public MigrationAnalysisVisitor(bool verbose = false) : base(SyntaxWalkerDepth.Node)
    {
        _verbose = verbose;
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        MethodCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);

        // Check for async modifier
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"async {node.Identifier.Text}");
        }

        // Check for Task/Task<T>/IAsyncEnumerable return type
        var returnType = GetTypeName(node.ReturnType);
        if (returnType is "Task" or "ValueTask" ||
            returnType.StartsWith("Task<", StringComparison.Ordinal) ||
            returnType.StartsWith("ValueTask<", StringComparison.Ordinal) ||
            returnType.StartsWith("IAsyncEnumerable<", StringComparison.Ordinal) ||
            returnType.StartsWith("IAsyncEnumerator<", StringComparison.Ordinal))
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"{returnType} return type");
        }

        base.VisitMethodDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);
        base.VisitStructDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);
        base.VisitPropertyDeclaration(node);
    }

    // Contract patterns: Argument validation throws
    public override void VisitThrowStatement(ThrowStatementSyntax node)
    {
        if (node.Expression is ObjectCreationExpressionSyntax creation)
        {
            var typeName = GetTypeName(creation.Type);
            if (ArgumentExceptionTypes.Contains(typeName))
            {
                ContractPatterns++;
                AddExample(ContractExamples, $"throw new {typeName}(...)");
            }
        }

        ErrorHandlingPatterns++;
        AddExample(ErrorHandlingExamples, "throw statement");
        base.VisitThrowStatement(node);
    }

    public override void VisitThrowExpression(ThrowExpressionSyntax node)
    {
        ErrorHandlingPatterns++;
        AddExample(ErrorHandlingExamples, "throw expression");

        // Track as unsupported (throw as expression, not statement)
        ThrowExpressionCount++;
        AddExample(ThrowExpressionExamples, "?? throw new ...");

        base.VisitThrowExpression(node);
    }

    // Track generic type constraints: where T : struct, Enum
    public override void VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
    {
        GenericTypeConstraintCount++;
        var constraints = string.Join(", ", node.Constraints.Select(c => c.ToString()));
        AddExample(GenericTypeConstraintExamples, $"where {node.Name}: {constraints}");
        base.VisitTypeParameterConstraintClause(node);
    }

    // Contract patterns: Validation if statements at method start
    public override void VisitIfStatement(IfStatementSyntax node)
    {
        // Check if this looks like argument validation (if at start of method checking param == null)
        if (IsArgumentValidationPattern(node))
        {
            ContractPatterns++;
            AddExample(ContractExamples, "if (...) throw validation");
        }

        base.VisitIfStatement(node);
    }

    // Effect patterns: I/O, Network, Database
    // LINQ patterns: Where, Select, OrderBy, etc.
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var memberAccess = node.Expression as MemberAccessExpressionSyntax;
        if (memberAccess != null)
        {
            var typeName = GetExpressionTypeName(memberAccess.Expression);
            var methodName = memberAccess.Name.Identifier.Text;

            // File I/O
            if (FileIoTypes.Contains(typeName))
            {
                EffectPatterns++;
                AddExample(EffectExamples, $"{typeName}.{methodName}");
            }
            // Network
            else if (NetworkTypes.Contains(typeName) || methodName.EndsWith("Async") && IsNetworkContext(memberAccess))
            {
                EffectPatterns++;
                AddExample(EffectExamples, $"Network: {typeName}.{methodName}");
            }
            // Database
            else if (DatabaseTypes.Contains(typeName) || DatabaseMethods.Contains(methodName))
            {
                EffectPatterns++;
                AddExample(EffectExamples, $"Database: {methodName}");
            }
            // Console
            else if (typeName == "Console")
            {
                EffectPatterns++;
                AddExample(EffectExamples, $"Console.{methodName}");
            }

            // LINQ methods
            if (LinqMethods.Contains(methodName))
            {
                LinqPatterns++;
                AddExample(LinqExamples, $".{methodName}()");
            }

            // Async methods (ConfigureAwait, WhenAll, WhenAny, etc.)
            if (AsyncMethods.Contains(methodName) ||
                (typeName == "Task" && AsyncMethods.Contains(methodName)))
            {
                AsyncPatterns++;
                AddExample(AsyncExamples, $".{methodName}()");
            }
        }

        base.VisitInvocationExpression(node);
    }

    // Null safety patterns
    public override void VisitNullableType(NullableTypeSyntax node)
    {
        NullSafetyPatterns++;
        AddExample(NullSafetyExamples, $"{node.ElementType}?");
        base.VisitNullableType(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // Check for null comparisons: == null, != null
        if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression))
        {
            if (node.Right.IsKind(SyntaxKind.NullLiteralExpression) ||
                node.Left.IsKind(SyntaxKind.NullLiteralExpression))
            {
                NullSafetyPatterns++;
                AddExample(NullSafetyExamples, "== null / != null check");
            }
        }
        // Check for null coalescing: ??
        else if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            NullSafetyPatterns++;
            AddExample(NullSafetyExamples, "?? operator");
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        // Check for "is null" or "is not null"
        if (node.Pattern is ConstantPatternSyntax constant && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            NullSafetyPatterns++;
            AddExample(NullSafetyExamples, "is null pattern");
        }
        else if (node.Pattern is UnaryPatternSyntax { Pattern: ConstantPatternSyntax innerConstant }
                 && innerConstant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            NullSafetyPatterns++;
            AddExample(NullSafetyExamples, "is not null pattern");
        }

        base.VisitIsPatternExpression(node);
    }

    // Error handling patterns
    public override void VisitTryStatement(TryStatementSyntax node)
    {
        ErrorHandlingPatterns += node.Catches.Count + 1;
        AddExample(ErrorHandlingExamples, $"try-catch ({node.Catches.Count} catch blocks)");
        base.VisitTryStatement(node);
    }

    // Pattern matching
    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        PatternMatchPatterns++;
        AddExample(PatternMatchExamples, $"switch statement ({node.Sections.Count} cases)");
        base.VisitSwitchStatement(node);
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        PatternMatchPatterns++;
        AddExample(PatternMatchExamples, $"switch expression ({node.Arms.Count} arms)");

        // Switch expressions are now supported by the Calor converter
        // Keeping SwitchExpressionCount for informational purposes only (not used for penalty)
        SwitchExpressionCount++;
        AddExample(SwitchExpressionExamples, $"switch expression with {node.Arms.Count} arms");

        base.VisitSwitchExpression(node);
    }

    // Track relational patterns: is > x, is < x, is >= x, is <= x
    public override void VisitRelationalPattern(RelationalPatternSyntax node)
    {
        RelationalPatternCount++;
        var op = node.OperatorToken.Text;
        var expr = node.Expression.ToString();
        AddExample(RelationalPatternExamples, $"is {op} {expr}");
        base.VisitRelationalPattern(node);
    }

    // Track compound patterns: pattern and pattern, pattern or pattern
    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        CompoundPatternCount++;
        var keyword = node.OperatorToken.Text; // "and" or "or"
        AddExample(CompoundPatternExamples, $"{keyword} pattern");
        base.VisitBinaryPattern(node);
    }

    // Track range expressions: 0..5, ..5, 5..
    public override void VisitRangeExpression(RangeExpressionSyntax node)
    {
        RangeExpressionCount++;
        AddExample(RangeExpressionExamples, $"range: {node}");
        base.VisitRangeExpression(node);
    }

    // Track index from end expressions: ^1
    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.IndexExpression))
        {
            IndexExpressionCount++;
            AddExample(IndexExpressionExamples, $"index: {node}");
        }
        base.VisitPrefixUnaryExpression(node);
    }

    // Track implicit object creation (target-typed new): new("arg")
    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        ImplicitObjectCreationCount++;
        var preview = node.ToString();
        if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
        AddExample(ImplicitObjectCreationExamples, $"new(...): {preview}");
        base.VisitImplicitObjectCreationExpression(node);
    }

    // Track conditional access method calls: obj?.Method()
    public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        NullSafetyPatterns++;
        AddExample(NullSafetyExamples, "?. operator");

        // Also check if this is a method invocation (which converter doesn't handle well)
        if (node.WhenNotNull is InvocationExpressionSyntax ||
            (node.WhenNotNull is MemberBindingExpressionSyntax binding &&
             node.Parent is InvocationExpressionSyntax))
        {
            ConditionalAccessMethodCount++;
            var preview = node.ToString();
            if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
            AddExample(ConditionalAccessMethodExamples, $"?.Method(): {preview}");
        }

        base.VisitConditionalAccessExpression(node);
    }

    // Track named arguments: Method(paramName: value)
    public override void VisitArgument(ArgumentSyntax node)
    {
        if (node.NameColon != null)
        {
            NamedArgumentCount++;
            AddExample(NamedArgumentExamples, $"{node.NameColon.Name}: ...");
        }

        // Track out var, ref var declarations in arguments
        if (node.Expression is DeclarationExpressionSyntax decl)
        {
            VarDeclarationInArgumentCount++;
            AddExample(VarDeclarationInArgumentExamples, $"out var {decl.Designation}");
        }

        base.VisitArgument(node);
    }

    // Track primary constructors: class Foo(int x) { }
    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);

        if (node.ParameterList != null && node.ParameterList.Parameters.Count > 0)
        {
            PrimaryConstructorCount++;
            var paramList = string.Join(", ", node.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "var"));
            AddExample(PrimaryConstructorExamples, $"class {node.Identifier.Text}({paramList})");
        }

        base.VisitClassDeclaration(node);
    }

    // Track ref/out parameters in method declarations
    public override void VisitParameter(ParameterSyntax node)
    {
        foreach (var modifier in node.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.OutKeyword) || modifier.IsKind(SyntaxKind.RefKeyword))
            {
                OutRefParameterCount++;
                AddExample(OutRefParameterExamples, $"{modifier.Text} {node.Type} {node.Identifier}");
                break;
            }
        }

        // Track CancellationToken parameters (async pattern)
        if (node.Type != null)
        {
            var paramTypeName = GetTypeName(node.Type);
            if (paramTypeName is "CancellationToken" or "CancellationTokenSource")
            {
                AsyncPatterns++;
                AddExample(AsyncExamples, $"{paramTypeName} parameter");
            }
        }

        // Track nested generic types in parameters (like Expression<Func<T, U>>)
        if (node.Type is GenericNameSyntax genericName)
        {
            if (HasNestedGenericTypes(genericName))
            {
                NestedGenericTypeCount++;
                var typeName = genericName.ToString();
                if (typeName.Length > 50) typeName = typeName.Substring(0, 50) + "...";
                AddExample(NestedGenericTypeExamples, typeName);
            }
        }

        base.VisitParameter(node);
    }

    // Track declaration patterns: if (obj is Type varName)
    public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
    {
        DeclarationPatternCount++;
        AddExample(DeclarationPatternExamples, $"is {node.Type} {node.Designation}");
        base.VisitDeclarationPattern(node);
    }

    // Track lambda expressions (informational only - lambdas are now supported)
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        LambdaExpressionCount++;
        AddExample(LambdaExpressionExamples, $"{node.Parameter} => ...");

        // Check for async lambda
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"async {node.Parameter} => ...");
        }

        base.VisitSimpleLambdaExpression(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        LambdaExpressionCount++;
        var paramList = string.Join(", ", node.ParameterList.Parameters.Select(p => p.ToString()));
        AddExample(LambdaExpressionExamples, $"({paramList}) => ...");

        // Check for async lambda
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"async ({paramList}) => ...");
        }

        base.VisitParenthesizedLambdaExpression(node);
    }

    // Track await expressions
    public override void VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        AsyncPatterns++;
        var preview = node.Expression.ToString();
        if (preview.Length > 30) preview = preview.Substring(0, 30) + "...";
        AddExample(AsyncExamples, $"await {preview}");
        base.VisitAwaitExpression(node);
    }

    // Track LINQ query syntax (from x in collection where... select...)
    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        LinqPatterns++;
        var preview = node.ToString();
        if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
        AddExample(LinqExamples, $"query: {preview}");
        base.VisitQueryExpression(node);
    }

    // Also count individual query clauses for more granular tracking
    public override void VisitWhereClause(WhereClauseSyntax node)
    {
        LinqPatterns++;
        AddExample(LinqExamples, "where clause");
        base.VisitWhereClause(node);
    }

    public override void VisitOrderByClause(OrderByClauseSyntax node)
    {
        LinqPatterns++;
        AddExample(LinqExamples, "orderby clause");
        base.VisitOrderByClause(node);
    }

    public override void VisitGroupClause(GroupClauseSyntax node)
    {
        LinqPatterns++;
        AddExample(LinqExamples, "group clause");
        base.VisitGroupClause(node);
    }

    public override void VisitJoinClause(JoinClauseSyntax node)
    {
        LinqPatterns++;
        AddExample(LinqExamples, "join clause");
        base.VisitJoinClause(node);
    }

    public override void VisitLetClause(LetClauseSyntax node)
    {
        LinqPatterns++;
        AddExample(LinqExamples, "let clause");
        base.VisitLetClause(node);
    }

    // Track await foreach (async iteration)
    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.AwaitKeyword != default)
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"await foreach ({node.Identifier})");
        }
        base.VisitForEachStatement(node);
    }

    // Track await using (async disposal)
    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        if (node.AwaitKeyword != default)
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, "await using statement");
        }
        base.VisitUsingStatement(node);
    }

    // Track await using declarations (C# 8+)
    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (node.AwaitKeyword != default && node.UsingKeyword != default)
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, "await using declaration");
        }
        base.VisitLocalDeclarationStatement(node);
    }

    private static bool HasNestedGenericTypes(GenericNameSyntax genericName)
    {
        foreach (var arg in genericName.TypeArgumentList.Arguments)
        {
            if (arg is GenericNameSyntax)
            {
                return true;
            }
        }
        return false;
    }

    private void CheckPublicMember(SyntaxTokenList modifiers, string name, SyntaxNode node)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            PublicMemberCount++;

            // Check if it has XML documentation
            var hasDocComment = node.GetLeadingTrivia()
                .Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                          t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

            if (!hasDocComment)
            {
                ApiComplexityPatterns++;
                AddExample(ApiComplexityExamples, $"Undocumented public: {name}");
            }
        }
    }

    private static bool IsArgumentValidationPattern(IfStatementSyntax node)
    {
        // Check if the if statement throws an argument exception
        if (node.Statement is BlockSyntax block)
        {
            var throwStatement = block.Statements.OfType<ThrowStatementSyntax>().FirstOrDefault();
            if (throwStatement?.Expression is ObjectCreationExpressionSyntax creation)
            {
                var typeName = GetTypeName(creation.Type);
                return ArgumentExceptionTypes.Contains(typeName);
            }
        }
        else if (node.Statement is ThrowStatementSyntax throwStmt)
        {
            if (throwStmt.Expression is ObjectCreationExpressionSyntax creation)
            {
                var typeName = GetTypeName(creation.Type);
                return ArgumentExceptionTypes.Contains(typeName);
            }
        }

        return false;
    }

    private static string GetTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => type.ToString()
        };
    }

    private static string GetExpressionTypeName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => expression.ToString()
        };
    }

    private static bool IsNetworkContext(MemberAccessExpressionSyntax memberAccess)
    {
        var text = memberAccess.ToString();
        return text.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Web", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Socket", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddExample(List<string> examples, string example)
    {
        if (examples.Count < 10)
        {
            examples.Add(example);
        }
    }
}
