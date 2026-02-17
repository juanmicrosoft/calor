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
                Name = "relational-pattern",
                Description = "Relational patterns (is > x, is < x) not yet supported",
                Count = visitor.RelationalPatternCount,
                Examples = visitor.RelationalPatternExamples
            });
        }

        if (visitor.CompoundPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "compound-pattern",
                Description = "Compound patterns (and/or) not yet supported",
                Count = visitor.CompoundPatternCount,
                Examples = visitor.CompoundPatternExamples
            });
        }

        if (visitor.RangeExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "range-expression",
                Description = "Range expressions (0..5, ..5, 5..) not yet supported",
                Count = visitor.RangeExpressionCount,
                Examples = visitor.RangeExpressionExamples
            });
        }

        if (visitor.IndexExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "index-from-end",
                Description = "Index from end expressions (^1) not yet supported",
                Count = visitor.IndexExpressionCount,
                Examples = visitor.IndexExpressionExamples
            });
        }

        if (visitor.ImplicitObjectCreationCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "target-typed-new",
                Description = "Target-typed new expressions (new(...)) not yet supported",
                Count = visitor.ImplicitObjectCreationCount,
                Examples = visitor.ImplicitObjectCreationExamples
            });
        }

        if (visitor.ConditionalAccessMethodCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "null-conditional-method",
                Description = "Null-conditional method calls (?.Method()) not yet supported",
                Count = visitor.ConditionalAccessMethodCount,
                Examples = visitor.ConditionalAccessMethodExamples
            });
        }

        if (visitor.NamedArgumentCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "named-argument",
                Description = "Named arguments (param: value) not yet supported",
                Count = visitor.NamedArgumentCount,
                Examples = visitor.NamedArgumentExamples
            });
        }

        if (visitor.PrimaryConstructorCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "primary-constructor",
                Description = "Primary constructors (class Foo(int x)) not yet supported",
                Count = visitor.PrimaryConstructorCount,
                Examples = visitor.PrimaryConstructorExamples
            });
        }

        if (visitor.OutRefParameterCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "ref-parameter",
                Description = "out/ref parameters not yet supported",
                Count = visitor.OutRefParameterCount,
                Examples = visitor.OutRefParameterExamples
            });
        }

        if (visitor.VarDeclarationInArgumentCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "out-var",
                Description = "Inline variable declarations (out var x) not yet supported",
                Count = visitor.VarDeclarationInArgumentCount,
                Examples = visitor.VarDeclarationInArgumentExamples
            });
        }

        if (visitor.DeclarationPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "declaration-pattern",
                Description = "Declaration patterns (is Type varName) not yet supported",
                Count = visitor.DeclarationPatternCount,
                Examples = visitor.DeclarationPatternExamples
            });
        }

        if (visitor.NestedGenericTypeCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "nested-generic-type",
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
                Name = "throw-expression",
                Description = "Throw expressions (?? throw new ...) not yet supported",
                Count = visitor.ThrowExpressionCount,
                Examples = visitor.ThrowExpressionExamples
            });
        }

        if (visitor.GenericTypeConstraintCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "generic-type-constraint",
                Description = "Generic type constraints (where T : class) not yet supported",
                Count = visitor.GenericTypeConstraintCount,
                Examples = visitor.GenericTypeConstraintExamples
            });
        }

        // Additional unsupported constructs
        if (visitor.YieldStatementCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "yield-return",
                Description = "Yield statements (yield return, yield break) not supported",
                Count = visitor.YieldStatementCount,
                Examples = visitor.YieldStatementExamples
            });
        }

        if (visitor.GotoStatementCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "goto",
                Description = "Goto statements not supported",
                Count = visitor.GotoStatementCount,
                Examples = visitor.GotoStatementExamples
            });
        }

        if (visitor.LabeledStatementCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "labeled-statement",
                Description = "Labeled statements not supported",
                Count = visitor.LabeledStatementCount,
                Examples = visitor.LabeledStatementExamples
            });
        }

        if (visitor.UnsafeBlockCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "unsafe",
                Description = "Unsafe code blocks and methods not supported",
                Count = visitor.UnsafeBlockCount,
                Examples = visitor.UnsafeBlockExamples
            });
        }

        if (visitor.PointerTypeCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "pointer",
                Description = "Pointer types not supported",
                Count = visitor.PointerTypeCount,
                Examples = visitor.PointerTypeExamples
            });
        }

        if (visitor.StackAllocCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "stackalloc",
                Description = "stackalloc expressions not supported",
                Count = visitor.StackAllocCount,
                Examples = visitor.StackAllocExamples
            });
        }

        if (visitor.FixedStatementCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "fixed",
                Description = "Fixed statements not supported",
                Count = visitor.FixedStatementCount,
                Examples = visitor.FixedStatementExamples
            });
        }

        if (visitor.VolatileFieldCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "volatile",
                Description = "Volatile fields not supported",
                Count = visitor.VolatileFieldCount,
                Examples = visitor.VolatileFieldExamples
            });
        }

        if (visitor.OperatorOverloadCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "operator-overload",
                Description = "Operator overloading requires manual conversion",
                Count = visitor.OperatorOverloadCount,
                Examples = visitor.OperatorOverloadExamples
            });
        }

        if (visitor.ConversionOperatorCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "implicit-conversion",
                Description = "Implicit/explicit conversion operators require manual conversion",
                Count = visitor.ConversionOperatorCount,
                Examples = visitor.ConversionOperatorExamples
            });
        }

        if (visitor.ExtensionMethodCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "extension-method",
                Description = "Extension methods require manual conversion to regular methods or traits",
                Count = visitor.ExtensionMethodCount,
                Examples = visitor.ExtensionMethodExamples
            });
        }

        // Phase 2 constructs
        if (visitor.InParameterCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "in-parameter",
                Description = "in parameters (readonly ref) not yet supported",
                Count = visitor.InParameterCount,
                Examples = visitor.InParameterExamples
            });
        }

        if (visitor.CheckedBlockCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "checked-block",
                Description = "checked/unchecked blocks not yet supported",
                Count = visitor.CheckedBlockCount,
                Examples = visitor.CheckedBlockExamples
            });
        }

        if (visitor.WithExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "with-expression",
                Description = "with expressions (record copying) not yet supported",
                Count = visitor.WithExpressionCount,
                Examples = visitor.WithExpressionExamples
            });
        }

        if (visitor.InitAccessorCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "init-accessor",
                Description = "init accessors not yet supported",
                Count = visitor.InitAccessorCount,
                Examples = visitor.InitAccessorExamples
            });
        }

        if (visitor.RequiredMemberCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "required-member",
                Description = "required members (C# 11) not yet supported",
                Count = visitor.RequiredMemberCount,
                Examples = visitor.RequiredMemberExamples
            });
        }

        if (visitor.ListPatternCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "list-pattern",
                Description = "list/slice patterns ([a, b, ..rest]) not yet supported",
                Count = visitor.ListPatternCount,
                Examples = visitor.ListPatternExamples
            });
        }

        if (visitor.StaticAbstractMemberCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "static-abstract-member",
                Description = "static abstract/virtual interface members not yet supported",
                Count = visitor.StaticAbstractMemberCount,
                Examples = visitor.StaticAbstractMemberExamples
            });
        }

        if (visitor.RefStructCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "ref-struct",
                Description = "ref struct types not yet supported",
                Count = visitor.RefStructCount,
                Examples = visitor.RefStructExamples
            });
        }

        // Phase 3 constructs
        if (visitor.LockStatementCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "lock-statement",
                Description = "lock statements not yet supported",
                Count = visitor.LockStatementCount,
                Examples = visitor.LockStatementExamples
            });
        }

        if (visitor.AwaitForeachCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "await-foreach",
                Description = "await foreach statements not yet supported",
                Count = visitor.AwaitForeachCount,
                Examples = visitor.AwaitForeachExamples
            });
        }

        if (visitor.AwaitUsingCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "await-using",
                Description = "await using statements not yet supported",
                Count = visitor.AwaitUsingCount,
                Examples = visitor.AwaitUsingExamples
            });
        }

        if (visitor.ScopedParameterCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "scoped-parameter",
                Description = "scoped parameters/locals not yet supported",
                Count = visitor.ScopedParameterCount,
                Examples = visitor.ScopedParameterExamples
            });
        }

        if (visitor.CollectionExpressionCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "collection-expression",
                Description = "collection expressions [1, 2, 3] not yet supported",
                Count = visitor.CollectionExpressionCount,
                Examples = visitor.CollectionExpressionExamples
            });
        }

        if (visitor.ReadonlyStructCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "readonly-struct",
                Description = "readonly struct types not yet supported",
                Count = visitor.ReadonlyStructCount,
                Examples = visitor.ReadonlyStructExamples
            });
        }

        // Phase 4 constructs (C# 11-13)
        if (visitor.DefaultLambdaParameterCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "default-lambda-parameter",
                Description = "default lambda parameters (C# 12) not yet supported",
                Count = visitor.DefaultLambdaParameterCount,
                Examples = visitor.DefaultLambdaParameterExamples
            });
        }

        if (visitor.FileScopedTypeCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "file-scoped-type",
                Description = "file-scoped types (C# 11) not yet supported",
                Count = visitor.FileScopedTypeCount,
                Examples = visitor.FileScopedTypeExamples
            });
        }

        if (visitor.Utf8StringLiteralCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "utf8-string-literal",
                Description = "UTF-8 string literals (C# 11) not yet supported",
                Count = visitor.Utf8StringLiteralCount,
                Examples = visitor.Utf8StringLiteralExamples
            });
        }

        if (visitor.GenericAttributeCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "generic-attribute",
                Description = "generic attributes (C# 11) not yet supported",
                Count = visitor.GenericAttributeCount,
                Examples = visitor.GenericAttributeExamples
            });
        }

        if (visitor.UsingTypeAliasCount > 0)
        {
            result.Add(new UnsupportedConstruct
            {
                Name = "using-type-alias",
                Description = "using type aliases (C# 12) not yet supported",
                Count = visitor.UsingTypeAliasCount,
                Examples = visitor.UsingTypeAliasExamples
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

    // Additional unsupported constructs
    public int YieldStatementCount { get; private set; }
    public List<string> YieldStatementExamples { get; } = new();

    public int GotoStatementCount { get; private set; }
    public List<string> GotoStatementExamples { get; } = new();

    public int LabeledStatementCount { get; private set; }
    public List<string> LabeledStatementExamples { get; } = new();

    public int UnsafeBlockCount { get; private set; }
    public List<string> UnsafeBlockExamples { get; } = new();

    public int PointerTypeCount { get; private set; }
    public List<string> PointerTypeExamples { get; } = new();

    public int StackAllocCount { get; private set; }
    public List<string> StackAllocExamples { get; } = new();

    public int FixedStatementCount { get; private set; }
    public List<string> FixedStatementExamples { get; } = new();

    public int VolatileFieldCount { get; private set; }
    public List<string> VolatileFieldExamples { get; } = new();

    public int OperatorOverloadCount { get; private set; }
    public List<string> OperatorOverloadExamples { get; } = new();

    public int ConversionOperatorCount { get; private set; }
    public List<string> ConversionOperatorExamples { get; } = new();

    public int ExtensionMethodCount { get; private set; }
    public List<string> ExtensionMethodExamples { get; } = new();

    // Additional C# constructs (phase 2)
    public int InParameterCount { get; private set; }
    public List<string> InParameterExamples { get; } = new();

    public int CheckedBlockCount { get; private set; }
    public List<string> CheckedBlockExamples { get; } = new();

    public int WithExpressionCount { get; private set; }
    public List<string> WithExpressionExamples { get; } = new();

    public int InitAccessorCount { get; private set; }
    public List<string> InitAccessorExamples { get; } = new();

    public int RequiredMemberCount { get; private set; }
    public List<string> RequiredMemberExamples { get; } = new();

    public int ListPatternCount { get; private set; }
    public List<string> ListPatternExamples { get; } = new();

    public int StaticAbstractMemberCount { get; private set; }
    public List<string> StaticAbstractMemberExamples { get; } = new();

    public int RefStructCount { get; private set; }
    public List<string> RefStructExamples { get; } = new();

    // Phase 3 constructs
    public int LockStatementCount { get; private set; }
    public List<string> LockStatementExamples { get; } = new();

    public int AwaitForeachCount { get; private set; }
    public List<string> AwaitForeachExamples { get; } = new();

    public int AwaitUsingCount { get; private set; }
    public List<string> AwaitUsingExamples { get; } = new();

    public int ScopedParameterCount { get; private set; }
    public List<string> ScopedParameterExamples { get; } = new();

    public int CollectionExpressionCount { get; private set; }
    public List<string> CollectionExpressionExamples { get; } = new();

    public int ReadonlyStructCount { get; private set; }
    public List<string> ReadonlyStructExamples { get; } = new();

    // Phase 4 constructs (C# 11-13)
    public int DefaultLambdaParameterCount { get; private set; }
    public List<string> DefaultLambdaParameterExamples { get; } = new();

    public int FileScopedTypeCount { get; private set; }
    public List<string> FileScopedTypeExamples { get; } = new();

    public int Utf8StringLiteralCount { get; private set; }
    public List<string> Utf8StringLiteralExamples { get; } = new();

    public int GenericAttributeCount { get; private set; }
    public List<string> GenericAttributeExamples { get; } = new();

    public int UsingTypeAliasCount { get; private set; }
    public List<string> UsingTypeAliasExamples { get; } = new();

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

        // Check for extension methods (first parameter has 'this' modifier)
        var firstParam = node.ParameterList.Parameters.FirstOrDefault();
        if (firstParam != null && firstParam.Modifiers.Any(SyntaxKind.ThisKeyword))
        {
            ExtensionMethodCount++;
            var extendedType = firstParam.Type?.ToString() ?? "?";
            AddExample(ExtensionMethodExamples, $"this {extendedType}.{node.Identifier.Text}()");
        }

        // Check for unsafe modifier on method
        if (node.Modifiers.Any(SyntaxKind.UnsafeKeyword))
        {
            UnsafeBlockCount++;
            AddExample(UnsafeBlockExamples, $"unsafe method {node.Identifier.Text}");
        }

        // Check for static abstract/virtual interface members (C# 11)
        if (node.Modifiers.Any(SyntaxKind.StaticKeyword) &&
            (node.Modifiers.Any(SyntaxKind.AbstractKeyword) || node.Modifiers.Any(SyntaxKind.VirtualKeyword)))
        {
            StaticAbstractMemberCount++;
            AddExample(StaticAbstractMemberExamples, $"static abstract {node.ReturnType} {node.Identifier}()");
        }

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

        // Check for file-scoped type (C# 11)
        if (node.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            FileScopedTypeCount++;
            AddExample(FileScopedTypeExamples, $"file interface {node.Identifier}");
        }

        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);

        // Check for file-scoped type (C# 11)
        if (node.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            FileScopedTypeCount++;
            AddExample(FileScopedTypeExamples, $"file record {node.Identifier}");
        }

        base.VisitRecordDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        TypeCount++;
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);

        // Check for ref struct
        if (node.Modifiers.Any(SyntaxKind.RefKeyword))
        {
            RefStructCount++;
            AddExample(RefStructExamples, $"ref struct {node.Identifier}");
        }

        // Check for readonly struct
        if (node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
        {
            ReadonlyStructCount++;
            AddExample(ReadonlyStructExamples, $"readonly struct {node.Identifier}");
        }

        // Check for file-scoped type (C# 11)
        if (node.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            FileScopedTypeCount++;
            AddExample(FileScopedTypeExamples, $"file struct {node.Identifier}");
        }

        base.VisitStructDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        CheckPublicMember(node.Modifiers, node.Identifier.Text, node);

        // Check for required modifier (C# 11)
        if (node.Modifiers.Any(SyntaxKind.RequiredKeyword))
        {
            RequiredMemberCount++;
            AddExample(RequiredMemberExamples, $"required {node.Type} {node.Identifier}");
        }

        // Check for init accessor
        if (node.AccessorList != null)
        {
            foreach (var accessor in node.AccessorList.Accessors)
            {
                if (accessor.Keyword.IsKind(SyntaxKind.InitKeyword))
                {
                    InitAccessorCount++;
                    AddExample(InitAccessorExamples, $"{node.Type} {node.Identifier} {{ get; init; }}");
                    break;
                }
            }
        }

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
    // Track file-scoped types (C# 11): file class Foo { }
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

        // Check for file-scoped type (C# 11)
        if (node.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            FileScopedTypeCount++;
            AddExample(FileScopedTypeExamples, $"file class {node.Identifier}");
        }

        base.VisitClassDeclaration(node);
    }

    // Track ref/out/in/scoped parameters in method declarations
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
            else if (modifier.IsKind(SyntaxKind.InKeyword))
            {
                InParameterCount++;
                AddExample(InParameterExamples, $"in {node.Type} {node.Identifier}");
                break;
            }
            else if (modifier.IsKind(SyntaxKind.ScopedKeyword))
            {
                ScopedParameterCount++;
                AddExample(ScopedParameterExamples, $"scoped {node.Type} {node.Identifier}");
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
    // Also detect default lambda parameters (C# 12)
    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        LambdaExpressionCount++;
        AddExample(LambdaExpressionExamples, $"{node.Parameter} => ...");

        // Check for default parameter value
        if (node.Parameter.Default != null)
        {
            DefaultLambdaParameterCount++;
            AddExample(DefaultLambdaParameterExamples, $"{node.Parameter.Identifier} = {node.Parameter.Default.Value}");
        }

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

        // Check for default parameter values (C# 12)
        foreach (var param in node.ParameterList.Parameters)
        {
            if (param.Default != null)
            {
                DefaultLambdaParameterCount++;
                AddExample(DefaultLambdaParameterExamples, $"{param.Identifier} = {param.Default.Value}");
            }
        }

        // Check for async lambda
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            AsyncPatterns++;
            AddExample(AsyncExamples, $"async ({paramList}) => ...");
        }

        base.VisitParenthesizedLambdaExpression(node);
    }

    // Track yield statements (yield return, yield break)
    public override void VisitYieldStatement(YieldStatementSyntax node)
    {
        YieldStatementCount++;
        var keyword = node.ReturnOrBreakKeyword.Text;
        AddExample(YieldStatementExamples, $"yield {keyword}");
        base.VisitYieldStatement(node);
    }

    // Track goto statements
    public override void VisitGotoStatement(GotoStatementSyntax node)
    {
        GotoStatementCount++;
        var target = node.Expression?.ToString() ?? node.CaseOrDefaultKeyword.Text;
        AddExample(GotoStatementExamples, $"goto {target}");
        base.VisitGotoStatement(node);
    }

    // Track labeled statements
    public override void VisitLabeledStatement(LabeledStatementSyntax node)
    {
        LabeledStatementCount++;
        AddExample(LabeledStatementExamples, $"{node.Identifier.Text}:");
        base.VisitLabeledStatement(node);
    }

    // Track unsafe blocks
    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
    {
        UnsafeBlockCount++;
        AddExample(UnsafeBlockExamples, "unsafe { ... }");
        base.VisitUnsafeStatement(node);
    }

    // Track pointer types
    public override void VisitPointerType(PointerTypeSyntax node)
    {
        PointerTypeCount++;
        AddExample(PointerTypeExamples, $"{node.ElementType}*");
        base.VisitPointerType(node);
    }

    // Track stackalloc expressions
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        StackAllocCount++;
        AddExample(StackAllocExamples, $"stackalloc {node.Type}");
        base.VisitStackAllocArrayCreationExpression(node);
    }

    // Track implicit stackalloc (stackalloc [] { ... })
    public override void VisitImplicitStackAllocArrayCreationExpression(ImplicitStackAllocArrayCreationExpressionSyntax node)
    {
        StackAllocCount++;
        AddExample(StackAllocExamples, "stackalloc [] { ... }");
        base.VisitImplicitStackAllocArrayCreationExpression(node);
    }

    // Track fixed statements
    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        FixedStatementCount++;
        var decl = node.Declaration.ToString();
        if (decl.Length > 40) decl = decl.Substring(0, 40) + "...";
        AddExample(FixedStatementExamples, $"fixed ({decl})");
        base.VisitFixedStatement(node);
    }

    // Track operator overloads
    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        OperatorOverloadCount++;
        AddExample(OperatorOverloadExamples, $"operator {node.OperatorToken.Text}");
        base.VisitOperatorDeclaration(node);
    }

    // Track implicit/explicit conversion operators
    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        ConversionOperatorCount++;
        var kind = node.ImplicitOrExplicitKeyword.Text;
        AddExample(ConversionOperatorExamples, $"{kind} operator {node.Type}");
        base.VisitConversionOperatorDeclaration(node);
    }

    // Track volatile fields and required members
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        if (node.Modifiers.Any(SyntaxKind.VolatileKeyword))
        {
            VolatileFieldCount++;
            var fieldName = node.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field";
            AddExample(VolatileFieldExamples, $"volatile {node.Declaration.Type} {fieldName}");
        }

        // Check for required modifier (C# 11)
        if (node.Modifiers.Any(SyntaxKind.RequiredKeyword))
        {
            RequiredMemberCount++;
            var fieldName = node.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field";
            AddExample(RequiredMemberExamples, $"required {node.Declaration.Type} {fieldName}");
        }

        base.VisitFieldDeclaration(node);
    }

    // Track checked/unchecked blocks
    public override void VisitCheckedStatement(CheckedStatementSyntax node)
    {
        CheckedBlockCount++;
        var keyword = node.Keyword.Text;
        AddExample(CheckedBlockExamples, $"{keyword} {{ ... }}");
        base.VisitCheckedStatement(node);
    }

    // Track with expressions (records)
    public override void VisitWithExpression(WithExpressionSyntax node)
    {
        WithExpressionCount++;
        var preview = node.ToString();
        if (preview.Length > 50) preview = preview.Substring(0, 50) + "...";
        AddExample(WithExpressionExamples, preview);
        base.VisitWithExpression(node);
    }

    // Track list patterns [a, b, ..rest]
    public override void VisitListPattern(ListPatternSyntax node)
    {
        ListPatternCount++;
        AddExample(ListPatternExamples, $"[{string.Join(", ", node.Patterns.Select(p => p.ToString()))}]");
        base.VisitListPattern(node);
    }

    // Track slice patterns ..
    public override void VisitSlicePattern(SlicePatternSyntax node)
    {
        ListPatternCount++; // Count as list pattern variant
        AddExample(ListPatternExamples, $"..{node.Pattern?.ToString() ?? ""}");
        base.VisitSlicePattern(node);
    }

    // Track lock statements
    public override void VisitLockStatement(LockStatementSyntax node)
    {
        LockStatementCount++;
        var expr = node.Expression.ToString();
        if (expr.Length > 30) expr = expr.Substring(0, 30) + "...";
        AddExample(LockStatementExamples, $"lock ({expr})");
        base.VisitLockStatement(node);
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

    // Track await foreach (async iteration + blocker)
    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.AwaitKeyword != default)
        {
            AwaitForeachCount++;
            AddExample(AwaitForeachExamples, $"await foreach ({node.Type} {node.Identifier} in ...)");
            AsyncPatterns++;
            AddExample(AsyncExamples, $"await foreach ({node.Identifier})");
        }
        base.VisitForEachStatement(node);
    }

    // Track await using statements (async disposal + blocker)
    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        if (node.AwaitKeyword != default)
        {
            AwaitUsingCount++;
            AddExample(AwaitUsingExamples, "await using (...)");
            AsyncPatterns++;
            AddExample(AsyncExamples, "await using statement");
        }
        base.VisitUsingStatement(node);
    }

    // Track await using declarations (C# 8+) - async pattern + blocker
    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (node.AwaitKeyword != default && node.UsingKeyword != default)
        {
            AwaitUsingCount++;
            var varName = node.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "var";
            AddExample(AwaitUsingExamples, $"await using var {varName} = ...");
            AsyncPatterns++;
            AddExample(AsyncExamples, "await using declaration");
        }

        // Track scoped locals
        if (node.Modifiers.Any(SyntaxKind.ScopedKeyword))
        {
            ScopedParameterCount++;
            var varName = node.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "var";
            AddExample(ScopedParameterExamples, $"scoped {node.Declaration.Type} {varName}");
        }

        base.VisitLocalDeclarationStatement(node);
    }

    // Track collection expressions [1, 2, 3]
    public override void VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        CollectionExpressionCount++;
        var preview = node.ToString();
        if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
        AddExample(CollectionExpressionExamples, preview);
        base.VisitCollectionExpression(node);
    }

    // Track UTF-8 string literals (C# 11): "text"u8
    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.Utf8StringLiteralExpression))
        {
            Utf8StringLiteralCount++;
            var preview = node.ToString();
            if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
            AddExample(Utf8StringLiteralExamples, preview);
        }
        base.VisitLiteralExpression(node);
    }

    // Track generic attributes (C# 11): [Attr<T>]
    public override void VisitAttribute(AttributeSyntax node)
    {
        if (node.Name is GenericNameSyntax genericAttr)
        {
            GenericAttributeCount++;
            AddExample(GenericAttributeExamples, $"[{genericAttr}]");
        }
        base.VisitAttribute(node);
    }

    // Track using type aliases (C# 12): using Point = (int x, int y)
    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        // Check if this is a type alias (has Alias and the target is not a simple namespace)
        if (node.Alias != null && node.NamespaceOrType != null)
        {
            // If the target contains special characters like parentheses (tuples) or angle brackets (generics)
            // or if it's a predefined type, it's a type alias not a namespace alias
            var targetText = node.NamespaceOrType.ToString();
            if (targetText.Contains('(') || targetText.Contains('<') ||
                node.NamespaceOrType is PredefinedTypeSyntax ||
                node.NamespaceOrType is TupleTypeSyntax ||
                node.NamespaceOrType is ArrayTypeSyntax ||
                node.NamespaceOrType is NullableTypeSyntax)
            {
                UsingTypeAliasCount++;
                AddExample(UsingTypeAliasExamples, $"using {node.Alias.Name} = {targetText}");
            }
        }
        base.VisitUsingDirective(node);
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
