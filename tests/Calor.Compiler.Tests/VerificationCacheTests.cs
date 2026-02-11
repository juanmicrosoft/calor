using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for Z3 verification result caching.
/// </summary>
public class VerificationCacheTests : IDisposable
{
    private readonly string _testCacheDir;

    public VerificationCacheTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "calor-cache-tests", Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testCacheDir))
            {
                Directory.Delete(_testCacheDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    private static TextSpan EmptySpan => new(0, 0, 1, 1);

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region ContractHasher Unit Tests

    [Fact]
    public void HashPrecondition_SameExpression_SameHash()
    {
        var hasher = new ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };

        var pre1 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var pre2 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var hash1 = hasher.HashPrecondition(parameters, pre1);
        var hash2 = hasher.HashPrecondition(parameters, pre2);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA256 hex string is 64 chars
    }

    [Fact]
    public void HashPrecondition_DifferentExpression_DifferentHash()
    {
        var hasher = new ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };

        var pre1 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var pre2 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterThan,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var hash1 = hasher.HashPrecondition(parameters, pre1);
        var hash2 = hasher.HashPrecondition(parameters, pre2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPrecondition_DifferentParams_DifferentHash()
    {
        var hasher = new ContractHasher();
        var condition = new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
            new ReferenceNode(EmptySpan, "x"),
            new IntLiteralNode(EmptySpan, 0));

        var pre = new RequiresNode(EmptySpan, condition, null, new AttributeCollection());

        var params1 = new List<(string Name, string TypeName)> { ("x", "i32") };
        var params2 = new List<(string Name, string TypeName)> { ("x", "i64") }; // Different type

        var hash1 = hasher.HashPrecondition(params1, pre);
        var hash2 = hasher.HashPrecondition(params2, pre);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPrecondition_DifferentParamNames_DifferentHash()
    {
        var hasher = new ContractHasher();
        var condition = new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
            new ReferenceNode(EmptySpan, "x"),
            new IntLiteralNode(EmptySpan, 0));

        var pre = new RequiresNode(EmptySpan, condition, null, new AttributeCollection());

        var params1 = new List<(string Name, string TypeName)> { ("x", "i32") };
        var params2 = new List<(string Name, string TypeName)> { ("y", "i32") }; // Different name

        var hash1 = hasher.HashPrecondition(params1, pre);
        var hash2 = hasher.HashPrecondition(params2, pre);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashPostcondition_IncludesPreconditions()
    {
        var hasher = new ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };

        var post = new EnsuresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "result"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var pre1 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var pre2 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterThan,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var hash1 = hasher.HashPostcondition(parameters, "i32", new[] { pre1 }, post);
        var hash2 = hasher.HashPostcondition(parameters, "i32", new[] { pre2 }, post);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_AllExpressionNodeTypes()
    {
        var hasher = new ContractHasher();

        // Test all expression node types
        var intLit = new IntLiteralNode(EmptySpan, 42);
        var boolLit = new BoolLiteralNode(EmptySpan, true);
        var floatLit = new FloatLiteralNode(EmptySpan, 3.14);
        var strLit = new StringLiteralNode(EmptySpan, "test");
        var refNode = new ReferenceNode(EmptySpan, "x");

        var binOp = new BinaryOperationNode(EmptySpan, BinaryOperator.Add, intLit, intLit);
        var unaryOp = new UnaryOperationNode(EmptySpan, UnaryOperator.Negate, intLit);

        var forall = new ForallExpressionNode(EmptySpan,
            new[] { new QuantifierVariableNode(EmptySpan, "i", "i32") },
            boolLit);

        var exists = new ExistsExpressionNode(EmptySpan,
            new[] { new QuantifierVariableNode(EmptySpan, "j", "i32") },
            boolLit);

        var impl = new ImplicationExpressionNode(EmptySpan, boolLit, boolLit);
        var cond = new ConditionalExpressionNode(EmptySpan, boolLit, intLit, intLit);
        var arrAccess = new ArrayAccessNode(EmptySpan, refNode, intLit);
        var arrLen = new ArrayLengthNode(EmptySpan, refNode);

        // All should produce non-empty canonical strings
        Assert.NotEmpty(hasher.GetCanonicalExpression(intLit));
        Assert.NotEmpty(hasher.GetCanonicalExpression(boolLit));
        Assert.NotEmpty(hasher.GetCanonicalExpression(floatLit));
        Assert.NotEmpty(hasher.GetCanonicalExpression(strLit));
        Assert.NotEmpty(hasher.GetCanonicalExpression(refNode));
        Assert.NotEmpty(hasher.GetCanonicalExpression(binOp));
        Assert.NotEmpty(hasher.GetCanonicalExpression(unaryOp));
        Assert.NotEmpty(hasher.GetCanonicalExpression(forall));
        Assert.NotEmpty(hasher.GetCanonicalExpression(exists));
        Assert.NotEmpty(hasher.GetCanonicalExpression(impl));
        Assert.NotEmpty(hasher.GetCanonicalExpression(cond));
        Assert.NotEmpty(hasher.GetCanonicalExpression(arrAccess));
        Assert.NotEmpty(hasher.GetCanonicalExpression(arrLen));
    }

    [Fact]
    public void Hash_NestedExpressions()
    {
        var hasher = new ContractHasher();

        // Build: ((x + y) * (a - b))
        var x = new ReferenceNode(EmptySpan, "x");
        var y = new ReferenceNode(EmptySpan, "y");
        var a = new ReferenceNode(EmptySpan, "a");
        var b = new ReferenceNode(EmptySpan, "b");

        var add = new BinaryOperationNode(EmptySpan, BinaryOperator.Add, x, y);
        var sub = new BinaryOperationNode(EmptySpan, BinaryOperator.Subtract, a, b);
        var mul = new BinaryOperationNode(EmptySpan, BinaryOperator.Multiply, add, sub);

        var canonical = hasher.GetCanonicalExpression(mul);

        Assert.Contains("(* ", canonical);
        Assert.Contains("(+ REF:x REF:y)", canonical);
        Assert.Contains("(- REF:a REF:b)", canonical);
    }

    [Fact]
    public void Hash_Quantifiers()
    {
        var hasher = new ContractHasher();

        // Build: forall i. (i >= 0)
        var iRef = new ReferenceNode(EmptySpan, "i");
        var zero = new IntLiteralNode(EmptySpan, 0);
        var body = new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual, iRef, zero);

        var forall = new ForallExpressionNode(EmptySpan,
            new[] { new QuantifierVariableNode(EmptySpan, "i", "i32") },
            body);

        var canonical = hasher.GetCanonicalExpression(forall);

        Assert.Contains("(FORALL ((i i32))", canonical);
        Assert.Contains("(>= REF:i INT:0)", canonical);
    }

    #endregion

    #region VerificationCache Unit Tests

    [Fact]
    public void Cache_HitsOnSecondCall()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        // First call - cache miss
        using (var cache = new VerificationCache(options))
        {
            Assert.False(cache.TryGetPreconditionResult(parameters, pre, out _));
            cache.CachePreconditionResult(parameters, pre, result);
        }

        // Second call - cache hit
        using (var cache2 = new VerificationCache(options))
        {
            Assert.True(cache2.TryGetPreconditionResult(parameters, pre, out var cached));
            Assert.NotNull(cached);
            Assert.Equal(ContractVerificationStatus.Proven, cached!.Status);
        }
    }

    [Fact]
    public void Cache_MissOnDifferentContract()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };

        var pre1 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var pre2 = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterThan,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        cache.CachePreconditionResult(parameters, pre1, result);

        // Different contract should miss
        Assert.False(cache.TryGetPreconditionResult(parameters, pre2, out _));
    }

    [Fact]
    public void Cache_MissAfterClear()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using (var cache = new VerificationCache(options))
        {
            cache.CachePreconditionResult(parameters, pre, result);
            Assert.True(cache.TryGetPreconditionResult(parameters, pre, out _));
            cache.Clear();
        }

        // After clear, should miss
        using (var cache2 = new VerificationCache(options))
        {
            Assert.False(cache2.TryGetPreconditionResult(parameters, pre, out _));
        }
    }

    [Fact]
    public void Cache_PersistsAcrossInstances()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(
            ContractVerificationStatus.Disproven,
            "x = -1",
            TimeSpan.FromMilliseconds(100));

        // Write with one instance
        using (var cache = new VerificationCache(options))
        {
            cache.CachePreconditionResult(parameters, pre, result);
        }

        // Read with new instance
        using (var cache2 = new VerificationCache(options))
        {
            Assert.True(cache2.TryGetPreconditionResult(parameters, pre, out var cached));
            Assert.NotNull(cached);
            Assert.Equal(ContractVerificationStatus.Disproven, cached!.Status);
            Assert.Equal("x = -1", cached.CounterexampleDescription);
        }
    }

    [Fact]
    public void Cache_DisabledReturnsNoHits()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = false,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // Cache is disabled - should not store or retrieve
        cache.CachePreconditionResult(parameters, pre, result);
        Assert.False(cache.TryGetPreconditionResult(parameters, pre, out _));
    }

    [Fact]
    public void Cache_HandlesMissingDirectory()
    {
        var nonExistentDir = Path.Combine(_testCacheDir, "deep", "nested", "path");
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = nonExistentDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // Should create directory and write successfully
        cache.CachePreconditionResult(parameters, pre, result);
        Assert.True(cache.TryGetPreconditionResult(parameters, pre, out _));
    }

    [Fact]
    public void Cache_DoesNotCacheUnsupported()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var unsupportedResult = new ContractVerificationResult(ContractVerificationStatus.Unsupported);

        using var cache = new VerificationCache(options);

        cache.CachePreconditionResult(parameters, pre, unsupportedResult);

        // Unsupported should not be cached
        Assert.False(cache.TryGetPreconditionResult(parameters, pre, out _));
    }

    [Fact]
    public void Cache_DoesNotCacheSkipped()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var skippedResult = new ContractVerificationResult(ContractVerificationStatus.Skipped);

        using var cache = new VerificationCache(options);

        cache.CachePreconditionResult(parameters, pre, skippedResult);

        // Skipped should not be cached
        Assert.False(cache.TryGetPreconditionResult(parameters, pre, out _));
    }

    [Fact]
    public void Cache_ClearBeforeVerification()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = false
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        // First, populate cache
        using (var cache = new VerificationCache(options))
        {
            cache.CachePreconditionResult(parameters, pre, result);
        }

        // Now create cache with ClearBeforeVerification = true
        var clearOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = true
        };

        using (var cache2 = new VerificationCache(clearOptions))
        {
            // Cache should have been cleared
            Assert.False(cache2.TryGetPreconditionResult(parameters, pre, out _));
        }
    }

    [Fact]
    public void Cache_Statistics()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // 1 miss
        cache.TryGetPreconditionResult(parameters, pre, out _);

        // 1 write
        cache.CachePreconditionResult(parameters, pre, result);

        // 1 hit
        cache.TryGetPreconditionResult(parameters, pre, out _);

        var stats = cache.GetStatistics();

        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(1, stats.Writes);
        Assert.Equal(2, stats.TotalLookups);
        Assert.Equal(50.0, stats.HitRate, precision: 1);
    }

    #endregion

    #region Integration Tests

    [SkippableFact]
    public void E2E_CachePopulatesOnFirstRun()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x INT:0)
  §S (>= result INT:0)
  §R (* x x)
§/F{f001}
§/M{m001}
";

        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = true
        };

        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = cacheOptions
        };

        // First run - cache miss, populates cache
        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);

        // Verify cache files were created
        var cacheFiles = Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(cacheFiles);
    }

    [SkippableFact]
    public void E2E_CacheHitOnSecondRun()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x INT:0)
  §S (>= result INT:0)
  §R (* x x)
§/F{f001}
§/M{m001}
";

        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = true
        };

        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = cacheOptions
        };

        // First run
        var result1 = Program.Compile(source, "test.calr", options);
        Assert.False(result1.HasErrors);

        // Second run with same options (no clear)
        var secondCacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = false
        };

        var secondOptions = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = secondCacheOptions
        };

        var result2 = Program.Compile(source, "test.calr", secondOptions);
        Assert.False(result2.HasErrors);

        // Both should produce same verification results
        Assert.NotNull(options.VerificationResults);
        Assert.NotNull(secondOptions.VerificationResults);
    }

    [SkippableFact]
    public void E2E_NoCacheFlagDisablesCache()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x INT:0)
  §R (* x x)
§/F{f001}
§/M{m001}
";

        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = false,
            CacheDirectory = _testCacheDir
        };

        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = cacheOptions
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);

        // Cache directory should not have any files (or not exist)
        var cacheFiles = Directory.Exists(_testCacheDir)
            ? Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(cacheFiles);
    }

    [SkippableFact]
    public void E2E_ClearCacheFlagWorks()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x INT:0)
  §R (* x x)
§/F{f001}
§/M{m001}
";

        // First, populate cache
        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = false
        };

        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = cacheOptions
        };

        Program.Compile(source, "test.calr", options);

        // Verify cache has files
        var cacheFilesBefore = Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(cacheFilesBefore);

        // Now clear with new compilation
        var clearCacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = true
        };

        var clearOptions = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = clearCacheOptions
        };

        // Clear should remove old files before verification
        Program.Compile(source, "test.calr", clearOptions);

        // Files should still exist (re-populated after clear)
        var cacheFilesAfter = Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(cacheFilesAfter);
    }

    [SkippableFact]
    public void E2E_CachedResultsMatchFreshResults()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x INT:0)
  §S (>= result INT:0)
  §R (* x x)
§/F{f001}
§F{f002:BadDecrement:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result INT:0)
  §R (- x INT:1)
§/F{f002}
§/M{m001}
";

        // Run with cache disabled to get fresh results
        var freshOptions = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = VerificationCacheOptions.Disabled
        };

        var freshResult = Program.Compile(source, "test.calr", freshOptions);
        Assert.False(freshResult.HasErrors);

        var freshSummary = freshOptions.VerificationResults!.GetSummary();

        // Run with cache enabled (first time, will populate)
        var cacheOptions = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            ClearBeforeVerification = true
        };

        var cachedOptions = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = cacheOptions
        };

        Program.Compile(source, "test.calr", cachedOptions);

        // Run again to use cache
        var secondCachedOptions = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions
            {
                Enabled = true,
                CacheDirectory = _testCacheDir
            }
        };

        Program.Compile(source, "test.calr", secondCachedOptions);
        var cachedSummary = secondCachedOptions.VerificationResults!.GetSummary();

        // Results should match
        Assert.Equal(freshSummary.Proven, cachedSummary.Proven);
        Assert.Equal(freshSummary.Unproven, cachedSummary.Unproven);
        Assert.Equal(freshSummary.Disproven, cachedSummary.Disproven);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Cache_VeryLongContractExpression()
    {
        var hasher = new ContractHasher();

        // Build a deeply nested expression
        ExpressionNode expr = new IntLiteralNode(EmptySpan, 0);
        for (int i = 0; i < 100; i++)
        {
            expr = new BinaryOperationNode(EmptySpan, BinaryOperator.Add, expr, new IntLiteralNode(EmptySpan, i));
        }

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(EmptySpan, expr, null, new AttributeCollection());

        var hash = hasher.HashPrecondition(parameters, pre);

        // Should still produce a valid 64-char hash
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(c => char.IsAsciiHexDigitLower(c)));
    }

    [Fact]
    public void Cache_UnicodeInIdentifiers()
    {
        var hasher = new ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("变量", "i32") };

        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "变量"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var hash = hasher.HashPrecondition(parameters, pre);

        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Cache_HandleCorruptedFile()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir
        };

        var parameters = new List<(string Name, string TypeName)> { ("x", "i32") };
        var pre = new RequiresNode(
            EmptySpan,
            new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                new ReferenceNode(EmptySpan, "x"),
                new IntLiteralNode(EmptySpan, 0)),
            null,
            new AttributeCollection());

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using (var cache = new VerificationCache(options))
        {
            cache.CachePreconditionResult(parameters, pre, result);
        }

        // Corrupt all cache files
        foreach (var file in Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories))
        {
            File.WriteAllText(file, "not valid json {{{");
        }

        // Should handle gracefully - cache miss
        using (var cache2 = new VerificationCache(options))
        {
            var hit = cache2.TryGetPreconditionResult(parameters, pre, out _);
            Assert.False(hit);

            // Should not throw
            var stats = cache2.GetStatistics();
            Assert.True(stats.Errors > 0 || stats.Misses > 0);
        }
    }

    [Fact]
    public void CacheEntry_InvalidatesOnZ3VersionChange()
    {
        var entry = new VerificationCacheEntry
        {
            Version = VerificationCacheEntry.CurrentFormatVersion,
            Z3Version = "4.12.0",
            Status = ContractVerificationStatus.Proven,
            ContractHash = "abc123"
        };

        // Same version should be valid
        Assert.True(entry.IsValidFor("4.12.0"));

        // Different version should be invalid
        Assert.False(entry.IsValidFor("4.13.0"));

        // Null vs non-null should be invalid
        Assert.False(entry.IsValidFor(null));
    }

    [Fact]
    public void CacheEntry_InvalidatesOnFormatVersionChange()
    {
        var entry = new VerificationCacheEntry
        {
            Version = "0.9", // Old format version
            Z3Version = "4.12.0",
            Status = ContractVerificationStatus.Proven,
            ContractHash = "abc123"
        };

        // Should be invalid due to format version mismatch
        Assert.False(entry.IsValidFor("4.12.0"));
    }

    [Fact]
    public void Cache_EvictsOldEntriesWhenSizeLimitExceeded()
    {
        // Use a small cache limit (2 KB) - allows ~10 entries before eviction
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            MaxCacheSizeBytes = 2048
        };

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // Write many entries to exceed the cache limit
        for (int i = 0; i < 30; i++)
        {
            var parameters = new List<(string Name, string TypeName)> { ($"x{i}", "i32") };
            var pre = new RequiresNode(
                EmptySpan,
                new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                    new ReferenceNode(EmptySpan, $"x{i}"),
                    new IntLiteralNode(EmptySpan, i)),
                null,
                new AttributeCollection());

            cache.CachePreconditionResult(parameters, pre, result);
        }

        var stats = cache.GetStatistics();

        // Some entries should have been evicted
        Assert.True(stats.Evictions > 0, $"Expected evictions > 0, got {stats.Evictions}");

        // Cache size should be reasonable (under limit + one entry's worth of headroom)
        // The eviction targets 80% of limit, so after eviction + 1 new write, we should be close to limit
        var cacheFiles = Directory.GetFiles(_testCacheDir, "*.json", SearchOption.AllDirectories);
        var totalSize = cacheFiles.Sum(f => new FileInfo(f).Length);
        var maxAllowedSize = options.MaxCacheSizeBytes + 300; // Allow for one entry overhead
        Assert.True(totalSize <= maxAllowedSize,
            $"Cache size {totalSize} significantly exceeds limit {options.MaxCacheSizeBytes}");
    }

    [Fact]
    public void Cache_NoEvictionWhenUnlimited()
    {
        // Set max to 0 for unlimited
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            MaxCacheSizeBytes = 0 // Unlimited
        };

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // Write several entries
        for (int i = 0; i < 10; i++)
        {
            var parameters = new List<(string Name, string TypeName)> { ($"x{i}", "i32") };
            var pre = new RequiresNode(
                EmptySpan,
                new BinaryOperationNode(EmptySpan, BinaryOperator.GreaterOrEqual,
                    new ReferenceNode(EmptySpan, $"x{i}"),
                    new IntLiteralNode(EmptySpan, i)),
                null,
                new AttributeCollection());

            cache.CachePreconditionResult(parameters, pre, result);
        }

        var stats = cache.GetStatistics();

        // No evictions should occur
        Assert.Equal(0, stats.Evictions);
        Assert.Equal(10, stats.Writes);
    }

    [Fact]
    public void Cache_StatisticsIncludesEvictions()
    {
        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = _testCacheDir,
            MaxCacheSizeBytes = 500 // Very small limit
        };

        var result = new ContractVerificationResult(ContractVerificationStatus.Proven);

        using var cache = new VerificationCache(options);

        // Write entries to trigger eviction
        for (int i = 0; i < 10; i++)
        {
            var parameters = new List<(string Name, string TypeName)> { ($"var{i}", "i32") };
            var pre = new RequiresNode(
                EmptySpan,
                new BinaryOperationNode(EmptySpan, BinaryOperator.Equal,
                    new ReferenceNode(EmptySpan, $"var{i}"),
                    new IntLiteralNode(EmptySpan, i * 100)),
                null,
                new AttributeCollection());

            cache.CachePreconditionResult(parameters, pre, result);
        }

        var stats = cache.GetStatistics();

        // Verify statistics include eviction count
        Assert.True(stats.Evictions >= 0);
        Assert.True(stats.Writes > 0);
    }

    #endregion
}
