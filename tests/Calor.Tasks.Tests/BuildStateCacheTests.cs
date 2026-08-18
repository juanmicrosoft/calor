using Xunit;
using Calor.Tasks;
using Calor.Compiler.Effects;
using Microsoft.Build.Utilities;

namespace Calor.Tasks.Tests;

public class BuildStateCacheTests : IDisposable
{
    private readonly string _tempDir;

    public BuildStateCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cache-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateTempFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // Test 1: Hash determinism — same content → same hash
    [Fact]
    public void ComputeFileHash_SameContent_ReturnsSameHash()
    {
        var file1 = CreateTempFile("a.txt", "hello world");
        var file2 = CreateTempFile("b.txt", "hello world");

        var hash1 = BuildStateCache.ComputeFileHash(file1);
        var hash2 = BuildStateCache.ComputeFileHash(file2);

        Assert.Equal(hash1, hash2);
    }

    // Test 2: Hash sensitivity — different content → different hash
    [Fact]
    public void ComputeFileHash_DifferentContent_ReturnsDifferentHash()
    {
        var file1 = CreateTempFile("a.txt", "hello world");
        var file2 = CreateTempFile("b.txt", "hello world!");

        var hash1 = BuildStateCache.ComputeFileHash(file1);
        var hash2 = BuildStateCache.ComputeFileHash(file2);

        Assert.NotEqual(hash1, hash2);
    }

    // Test 3: Matching metadata never overrides a byte mismatch.
    [Fact]
    public void IsFileUpToDate_MatchingStatFieldsButDifferentBytes_ReturnsFalse()
    {
        var filePath = CreateTempFile("test.calr", "content");
        var fileInfo = new FileInfo(filePath);

        var entry = new BuildFileEntry
        {
            ContentHash = "wrong-hash-should-not-matter",
            LastModified = fileInfo.LastWriteTimeUtc,
            FileSize = fileInfo.Length
        };

        Assert.False(BuildStateCache.IsFileUpToDate(entry, filePath));
    }

    // Test 4: Metadata changed, content same → skip
    [Fact]
    public void IsFileUpToDate_MtimeChanged_ContentSame_ReturnsTrue()
    {
        var filePath = CreateTempFile("test.calr", "same content");
        var actualHash = BuildStateCache.ComputeFileHash(filePath);

        var entry = new BuildFileEntry
        {
            ContentHash = actualHash,
            LastModified = DateTime.UtcNow.AddHours(-1), // different mtime triggers stat miss
            FileSize = new FileInfo(filePath).Length
        };

        Assert.True(BuildStateCache.IsFileUpToDate(entry, filePath));
    }

    // Test 5: Stat gate miss, hash miss — content changed → recompile
    [Fact]
    public void IsFileUpToDate_ContentChanged_ReturnsFalse()
    {
        var filePath = CreateTempFile("test.calr", "original content");

        var entry = new BuildFileEntry
        {
            ContentHash = "old-hash-that-does-not-match",
            LastModified = DateTime.UtcNow.AddHours(-1), // different mtime
            FileSize = new FileInfo(filePath).Length + 100 // different size
        };

        Assert.False(BuildStateCache.IsFileUpToDate(entry, filePath));
    }

    // Test 6: Load/save round-trip
    [Fact]
    public void LoadSave_RoundTrip_PreservesState()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var state = new BuildState
        {
            CompilerHash = "abc123",
            OptionsHash = "def456",
            ManifestHash = "ghi789",
            OutputDirectory = "obj/Debug/net10.0/calor/",
            Files =
            {
                ["src/Foo.calr"] = new BuildFileEntry
                {
                    ContentHash = "hash1",
                    LastModified = new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc),
                    FileSize = 4096
                }
            }
        };

        BuildStateCache.Save(state, outputDir);
        var loaded = BuildStateCache.Load(outputDir);

        Assert.NotNull(loaded);
        Assert.Equal(BuildStateCache.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal(BuildStateCache.CurrentCompilerSemanticsVersion, loaded.CompilerSemanticsVersion);
        Assert.Equal("abc123", loaded.CompilerHash);
        Assert.Equal("def456", loaded.OptionsHash);
        Assert.Equal("ghi789", loaded.ManifestHash);
        Assert.Equal("obj/Debug/net10.0/calor/", loaded.OutputDirectory);
        Assert.Single(loaded.Files);
        Assert.True(loaded.Files.ContainsKey("src/Foo.calr"));
        Assert.Equal("hash1", loaded.Files["src/Foo.calr"].ContentHash);
        Assert.Equal(4096, loaded.Files["src/Foo.calr"].FileSize);
    }

    // Verifies the EffectSummary payload survives JSON round-trip intact — guards against
    // System.Text.Json source-gen failing to discover a nested type after schema changes.
    [Fact]
    public void LoadSave_RoundTrip_PreservesEffectSummary()
    {
        var outputDir = Path.Combine(_tempDir, "output-summary");
        Directory.CreateDirectory(outputDir);

        var summary = new EffectSummary
        {
            ModuleName = "OrderService",
            InternalFunctionNames = new List<string> { "SaveOrder", "helperPrivate" },
            InternalMethodNames = new List<string> { "Apply", "Validate" },
            PublicFunctions = new List<EffectFunctionSummary>
            {
                new()
                {
                    Name = "SaveOrder",
                    ClassName = null,
                    HasEffectDeclaration = true,
                    DeclaredEffects = new List<EffectEntry>
                    {
                        new() { Kind = "IO", Value = "database_write" },
                        new() { Kind = "IO", Value = "console_write" }
                    },
                    DeclarationLine = 3,
                    DeclarationColumn = 1
                }
            },
            PublicMethods = new List<EffectFunctionSummary>
            {
                new()
                {
                    Name = "Apply",
                    ClassName = "OrderRepo",
                    HasEffectDeclaration = false,
                    DeclaredEffects = new List<EffectEntry>(),
                    DeclarationLine = 12,
                    DeclarationColumn = 5
                }
            },
            Callers = new List<EffectCallerSummary>
            {
                new()
                {
                    CallerName = "SaveOrder",
                    DiagnosticLine = 4,
                    DiagnosticColumn = 3,
                    DeclaredEffects = new List<EffectEntry>
                    {
                        new() { Kind = "IO", Value = "database_write" }
                    },
                    Calls = new List<EffectCallSummary>
                    {
                        new() { Target = "DbContext.SaveChanges", IsConstructor = false },
                        new() { Target = "Logger", IsConstructor = true }
                    }
                }
            }
        };

        var state = new BuildState
        {
            CompilerHash = "ch",
            OptionsHash = "oh",
            ManifestHash = "mh",
            OutputDirectory = "obj/",
            Files =
            {
                ["OrderService.calr"] = new BuildFileEntry
                {
                    ContentHash = "chash",
                    LastModified = new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc),
                    FileSize = 500,
                    EffectSummary = summary
                }
            }
        };

        BuildStateCache.Save(state, outputDir);
        var loaded = BuildStateCache.Load(outputDir);

        Assert.NotNull(loaded);
        var entry = loaded.Files["OrderService.calr"];
        Assert.NotNull(entry.EffectSummary);

        var s = entry.EffectSummary!;
        Assert.Equal("OrderService", s.ModuleName);
        Assert.Equal(new[] { "SaveOrder", "helperPrivate" }, s.InternalFunctionNames);
        Assert.Equal(new[] { "Apply", "Validate" }, s.InternalMethodNames);

        var pf = Assert.Single(s.PublicFunctions);
        Assert.Equal("SaveOrder", pf.Name);
        Assert.Null(pf.ClassName);
        Assert.True(pf.HasEffectDeclaration);
        Assert.Equal(2, pf.DeclaredEffects.Count);
        Assert.Contains(pf.DeclaredEffects, e => e.Kind == "IO" && e.Value == "database_write");
        Assert.Contains(pf.DeclaredEffects, e => e.Kind == "IO" && e.Value == "console_write");
        Assert.Equal(3, pf.DeclarationLine);

        var pm = Assert.Single(s.PublicMethods);
        Assert.Equal("Apply", pm.Name);
        Assert.Equal("OrderRepo", pm.ClassName);
        Assert.False(pm.HasEffectDeclaration);
        Assert.Empty(pm.DeclaredEffects);

        var caller = Assert.Single(s.Callers);
        Assert.Equal("SaveOrder", caller.CallerName);
        Assert.Equal(4, caller.DiagnosticLine);
        Assert.Single(caller.DeclaredEffects);
        Assert.Equal(2, caller.Calls.Count);
        Assert.Contains(caller.Calls, c => c.Target == "DbContext.SaveChanges" && !c.IsConstructor);
        Assert.Contains(caller.Calls, c => c.Target == "Logger" && c.IsConstructor);
    }

    // Test 7: Compiler hash invalidation → all recompile
    [Fact]
    public void IsGlobalInvalidation_CompilerHashChanged_ReturnsTrue()
    {
        var cached = new BuildState
        {
            CompilerHash = "old-compiler",
            OptionsHash = "opts",
            ManifestHash = "manifest",
            OutputDirectory = "out/"
        };

        Assert.True(BuildStateCache.IsGlobalInvalidation(
            cached, "new-compiler", "opts", "manifest", "out/"));
    }

    // Test 8: Options hash invalidation → all recompile
    [Fact]
    public void IsGlobalInvalidation_OptionsHashChanged_ReturnsTrue()
    {
        var cached = new BuildState
        {
            CompilerHash = "compiler",
            OptionsHash = "old-opts",
            ManifestHash = "manifest",
            OutputDirectory = "out/"
        };

        Assert.True(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "new-opts", "manifest", "out/"));
    }

    // Test 9: Manifest hash invalidation → all recompile
    [Fact]
    public void IsGlobalInvalidation_ManifestHashChanged_ReturnsTrue()
    {
        var cached = new BuildState
        {
            CompilerHash = "compiler",
            OptionsHash = "opts",
            ManifestHash = "old-manifest",
            OutputDirectory = "out/"
        };

        Assert.True(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "new-manifest", "out/"));
    }

    // Test 10: Output directory invalidation → all recompile
    [Fact]
    public void IsGlobalInvalidation_OutputDirectoryChanged_ReturnsTrue()
    {
        var cached = new BuildState
        {
            CompilerHash = "compiler",
            OptionsHash = "opts",
            ManifestHash = "manifest",
            OutputDirectory = "obj/Debug/net10.0/calor/"
        };

        Assert.True(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "manifest", "obj/Release/net10.0/calor/"));
    }

    // Test 11: Format version invalidation → all recompile
    [Fact]
    public void IsGlobalInvalidation_FormatVersionChanged_ReturnsTrue()
    {
        var cached = new BuildState
        {
            FormatVersion = "0.9", // old version
            CompilerHash = "compiler",
            OptionsHash = "opts",
            ManifestHash = "manifest",
            OutputDirectory = "out/"
        };

        Assert.True(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "manifest", "out/"));
    }

    [Fact]
    public void GlobalInvalidationReasons_AreDeterministic_AndTrackCompilerSemantics()
    {
        var cached = new BuildState
        {
            FormatVersion = "old-schema",
            CompilerSemanticsVersion = "old-semantics",
            CompilerHash = "old-compiler",
            OptionsHash = "old-options",
            ManifestHash = "old-manifest",
            OutputDirectory = "old-output"
        };

        var reasons = BuildStateCache.GetGlobalInvalidationReasons(
            cached, "compiler", "options", "manifest", "output");

        Assert.Equal(new[]
        {
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.SchemaVersionChanged,
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.CompilerSemanticsChanged,
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.CompilerChanged,
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.OptionsOrInputsChanged,
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.ManifestChanged,
            Calor.Compiler.Incremental.GlobalCacheInvalidationReason.OutputDirectoryChanged
        }, reasons);
    }

    // Test 12: Corrupt cache → recompile all, no exception
    [Fact]
    public void Load_CorruptCache_ReturnsNull()
    {
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, ".calor-build-state.json"), "NOT VALID JSON {{{");

        var result = BuildStateCache.Load(outputDir);

        Assert.Null(result);
    }

    [Fact]
    public void Load_CurrentSchemaMissingSemanticVersion_IsRejectedAsPartial()
    {
        var outputDir = Path.Combine(_tempDir, "partial-current");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(
            BuildStateCache.GetCachePath(outputDir),
            $$"""
              {
                "formatVersion": "{{BuildStateCache.CurrentFormatVersion}}",
                "compilerHash": "compiler",
                "optionsHash": "options",
                "manifestHash": "",
                "outputDirectory": ".",
                "files": {}
              }
              """);

        var result = BuildStateCache.LoadWithStatus(outputDir);

        Assert.Equal(CacheLoadStatus.CorruptOrPartial, result.Status);
        Assert.Null(result.State);
    }

    // Test 13: Missing cache → all compile
    [Fact]
    public void Load_MissingCache_ReturnsNull()
    {
        var outputDir = Path.Combine(_tempDir, "nonexistent-output");

        var result = BuildStateCache.Load(outputDir);

        Assert.Null(result);
    }

    // Test 14: Missing output → recompile (IsFileUpToDate returns true but output missing triggers recompile)
    [Fact]
    public void IsFileUpToDate_FileDoesNotExist_ReturnsFalse()
    {
        var entry = new BuildFileEntry
        {
            ContentHash = "hash",
            LastModified = DateTime.UtcNow,
            FileSize = 100
        };

        Assert.False(BuildStateCache.IsFileUpToDate(entry, Path.Combine(_tempDir, "nonexistent.calr")));
    }

    // Test 15: New file → compiles (null cached entry)
    [Fact]
    public void IsFileUpToDate_NullEntry_ReturnsFalse()
    {
        var filePath = CreateTempFile("new.calr", "new content");

        Assert.False(BuildStateCache.IsFileUpToDate(null, filePath));
    }

    // Test 16: File removed → entry dropped (covered by orphan cleanup in integration tests,
    // but we test the path normalization needed for matching)
    [Fact]
    public void NormalizeRelativePath_ConsistentAcrossPlatforms()
    {
        Assert.Equal("src/Foo.calr", BuildStateCache.NormalizeRelativePath("src\\Foo.calr"));
        Assert.Equal("src/Foo.calr", BuildStateCache.NormalizeRelativePath("src/Foo.calr"));
        Assert.Equal("src/sub/Foo.calr", BuildStateCache.NormalizeRelativePath("src\\sub\\Foo.calr"));
    }

    // Test 17: Out-of-project file → sanitized, no escape (covers .. AND rooted paths)
    [Fact]
    public void ComputeRelativePath_OutOfProjectFile_Sanitized()
    {
        var projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectDir);

        // File outside project root (.. path)
        var outsideFile = Path.Combine(_tempDir, "shared", "Foo.calr");
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        File.WriteAllText(outsideFile, "content");

        var (relativePath, isOutOfProject) = BuildStateCache.ComputeRelativePath(outsideFile, projectDir);

        Assert.True(isOutOfProject);
        Assert.StartsWith("_linked/", relativePath);
        Assert.EndsWith("Foo.calr", relativePath);
        Assert.DoesNotContain("..", relativePath);
    }

    [Fact]
    public void ComputeRelativePath_OutOfProjectFile_RootedPath_Sanitized()
    {
        // On Windows, a file on a different drive is rooted relative to the project
        // On Linux, use a distant absolute path
        var projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectDir);

        // Simulate a rooted path by using a path that's far enough away to produce ".." or rooted
        var distantDir = Path.Combine(Path.GetTempPath(), "calor-distant-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(distantDir);
        var distantFile = Path.Combine(distantDir, "Remote.calr");
        File.WriteAllText(distantFile, "content");

        try
        {
            var (relativePath, isOutOfProject) = BuildStateCache.ComputeRelativePath(distantFile, projectDir);

            Assert.True(isOutOfProject);
            Assert.StartsWith("_linked/", relativePath);
            Assert.EndsWith("Remote.calr", relativePath);
            Assert.DoesNotContain("..", relativePath);
        }
        finally
        {
            try { Directory.Delete(distantDir, true); } catch { }
        }
    }

    [Fact]
    public void ComputeRelativePath_InProjectFile_NotSanitized()
    {
        var projectDir = Path.Combine(_tempDir, "project");
        var sourceFile = Path.Combine(projectDir, "src", "Foo.calr");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "content");

        var (relativePath, isOutOfProject) = BuildStateCache.ComputeRelativePath(sourceFile, projectDir);

        Assert.False(isOutOfProject);
        Assert.Equal("src/Foo.calr", relativePath);
    }

    // Test 18: Both failure paths delete prior .g.cs
    // (This is tested in integration tests via CompileCalor.Execute())
    // Here we test that CreateFileEntry creates correct entries
    [Fact]
    public void CreateFileEntry_CapturesAllFields()
    {
        var filePath = CreateTempFile("test.calr", "some content");
        var fileInfo = new FileInfo(filePath);

        var entry = BuildStateCache.CreateFileEntry(filePath);

        Assert.NotEmpty(entry.ContentHash);
        Assert.Equal(fileInfo.LastWriteTimeUtc, entry.LastModified);
        Assert.Equal(fileInfo.Length, entry.FileSize);
    }

    // Test 19: Manifest scan — finds .calor-effects.json files, hash changes on content change
    [Fact]
    public void ComputeManifestHash_FindsManifestFiles_HashChangesOnContentChange()
    {
        var projectDir = Path.Combine(_tempDir, "manifest-project");
        Directory.CreateDirectory(projectDir);

        // No manifests → empty string
        var hash0 = BuildStateCache.ComputeManifestHash(projectDir);
        Assert.Equal("", hash0);

        // Add a manifest
        var manifestPath = Path.Combine(projectDir, "test.calor-effects.json");
        File.WriteAllText(manifestPath, """{"version":"1.0","mappings":[]}""");

        var hash1 = BuildStateCache.ComputeManifestHash(projectDir);
        Assert.NotEmpty(hash1);

        // Same content → same hash
        var hash1b = BuildStateCache.ComputeManifestHash(projectDir);
        Assert.Equal(hash1, hash1b);

        // Change content → different hash
        File.WriteAllText(manifestPath, """{"version":"1.0","mappings":[{"type":"Foo"}]}""");

        var hash2 = BuildStateCache.ComputeManifestHash(projectDir);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void IsGlobalInvalidation_NoChange_ReturnsFalse()
    {
        var cached = new BuildState
        {
            CompilerHash = "compiler",
            OptionsHash = "opts",
            ManifestHash = "manifest",
            OutputDirectory = "out/"
        };

        Assert.False(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "manifest", "out/"));
    }

    [Fact]
    public void IsGlobalInvalidation_NullCache_ReturnsTrue()
    {
        Assert.True(BuildStateCache.IsGlobalInvalidation(
            null, "compiler", "opts", "manifest", "out/"));
    }

    [Fact]
    public void ComputePathHash_Deterministic()
    {
        var hash1 = BuildStateCache.ComputePathHash("/some/path/file.calr");
        var hash2 = BuildStateCache.ComputePathHash("/some/path/file.calr");
        var hash3 = BuildStateCache.ComputePathHash("/different/path/file.calr");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void ComputeCompilerHash_TracksResolvedCompilerAndRuntimeClosure()
    {
        var dir = Path.Combine(_tempDir, "compiler-closure");
        Directory.CreateDirectory(dir);
        var resolved = Calor.Tasks.CompileCalor.ResolveCompilerClosurePaths(
            typeof(Calor.Tasks.CompileCalor).Assembly.Location);
        Assert.Contains(typeof(Calor.Compiler.Program).Assembly.Location, resolved);
        Assert.Contains(typeof(Calor.Runtime.Option<int>).Assembly.Location, resolved);

        Assert.Contains(resolved, path =>
            path.Contains(
                $"{Path.DirectorySeparatorChar}runtimes{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && path.EndsWith(
                OperatingSystem.IsWindows() ? "libz3.dll"
                    : OperatingSystem.IsMacOS() ? "libz3.dylib" : "libz3.so",
                StringComparison.Ordinal));

        var copied = resolved.Select((path, index) =>
        {
            var destination = Path.Combine(dir, $"{index:D2}-{Path.GetFileName(path)}");
            if (File.Exists(path))
                File.Copy(path, destination);
            return destination;
        }).ToList();
        var compiler = copied.Single(path =>
            Path.GetFileName(path).EndsWith("-calor.dll", StringComparison.OrdinalIgnoreCase));
        var runtime = copied.Single(path =>
            Path.GetFileName(path).EndsWith(
                "-Calor.Runtime.dll", StringComparison.OrdinalIgnoreCase));

        var baseline = BuildStateCache.ComputeCompilerHash(copied);
        File.AppendAllText(compiler, "compiler-change");
        var compilerChanged = BuildStateCache.ComputeCompilerHash(copied);
        File.AppendAllText(runtime, "runtime-change");
        var runtimeChanged = BuildStateCache.ComputeCompilerHash(copied);

        Assert.NotEqual(baseline, compilerChanged);
        Assert.NotEqual(compilerChanged, runtimeChanged);
    }

    [Fact]
    public void Save_AtomicWrite_NoCorruptionOnRead()
    {
        var outputDir = Path.Combine(_tempDir, "atomic-test");
        Directory.CreateDirectory(outputDir);

        var state = new BuildState
        {
            CompilerHash = "test",
            OptionsHash = "test",
            ManifestHash = "",
            OutputDirectory = "out/"
        };

        // Write and read multiple times to test stability
        for (var i = 0; i < 5; i++)
        {
            state.CompilerHash = $"test-{i}";
            BuildStateCache.Save(state, outputDir);
            var loaded = BuildStateCache.Load(outputDir);
            Assert.NotNull(loaded);
            Assert.Equal($"test-{i}", loaded.CompilerHash);
        }
    }

    // Cross-platform: GetPathComparer returns case-insensitive on Windows, case-sensitive on Linux
    [Fact]
    public void GetPathComparer_ReturnsCorrectComparerForPlatform()
    {
        var comparer = BuildStateCache.GetPathComparer();

        if (OperatingSystem.IsWindows())
        {
            // Windows: case-insensitive
            Assert.True(comparer.Equals("src/Foo.calr", "src/foo.calr"));
            Assert.True(comparer.Equals("SRC/FOO.CALR", "src/foo.calr"));
        }
        else
        {
            // Linux/macOS: case-sensitive
            Assert.False(comparer.Equals("src/Foo.calr", "src/foo.calr"));
        }
    }

    // Verify NormalizeRelativePath handles edge cases
    [Fact]
    public void NormalizeRelativePath_EdgeCases()
    {
        // Empty string
        Assert.Equal("", BuildStateCache.NormalizeRelativePath(""));
        // Already normalized
        Assert.Equal("src/foo.calr", BuildStateCache.NormalizeRelativePath("src/foo.calr"));
        // Multiple separators
        Assert.Equal("a/b/c", BuildStateCache.NormalizeRelativePath("a\\b\\c"));
        // Trailing separator variants
        Assert.Equal("obj/calor", BuildStateCache.NormalizeRelativePath("obj\\calor\\"));
        Assert.Equal("obj/calor", BuildStateCache.NormalizeRelativePath("obj/calor/"));
    }

    // Verify the cache file is inside the output directory (so dotnet clean removes it)
    [Fact]
    public void GetCachePath_IsInsideOutputDirectory()
    {
        var outputDir = Path.Combine(_tempDir, "obj", "Debug", "net10.0", "calor");
        var cachePath = BuildStateCache.GetCachePath(outputDir);

        Assert.StartsWith(outputDir, cachePath);
        Assert.EndsWith(".calor-build-state.json", cachePath);
    }

    // OutputDirectory comparison uses normalized paths (relative)
    [Fact]
    public void IsGlobalInvalidation_OutputDirectory_NormalizedComparison()
    {
        var cached = new BuildState
        {
            CompilerHash = "compiler",
            OptionsHash = "opts",
            ManifestHash = "manifest",
            OutputDirectory = "obj/Debug/net10.0/calor"
        };

        // Trailing slash difference should not trigger invalidation
        Assert.False(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "manifest", "obj/Debug/net10.0/calor/"));

        // Backslash vs forward slash should not trigger invalidation
        Assert.False(BuildStateCache.IsGlobalInvalidation(
            cached, "compiler", "opts", "manifest", "obj\\Debug\\net10.0\\calor"));
    }
    /// <summary>
    /// The canonical input record must carry the EFFECTIVE type-check setting, not the task property.
    /// v0.12 added a CALOR_NO_TYPE_CHECK escape hatch read at the CompilationOptions default; the
    /// first cut hashed `TypeCheck` alone, so flipping the variable against a warm cache changed
    /// what would be reported without invalidating anything and every unchanged file was silently
    /// skipped — the exact #788 failure the call site's own comment warns about. Found by the
    /// second round of release review.
    /// </summary>
    [Fact]
    public void OptionsToken_DistinguishesEffectiveTypeCheck()
    {
        // A SHAPE pin: the token must carry a typeCheck component that tracks the task property.
        // It is explicitly NOT the regression guard — driving TypeCheck true/false produces
        // typeCheck:True/False whether or not the call site folds in TypeCheckingDefault, so it
        // stays green with the fix reverted (confirmed). OptionsToken_EnvironmentOptOut_MovesTheToken
        // below is the guard that fails. Two earlier revisions of this comment claimed otherwise;
        // a pin's docstring asserting a discrimination it does not have is how the first two
        // versions of this test survived review.
        var on = new Calor.Tasks.CompileCalor { TypeCheck = true }.ComputeCacheInputs();
        var off = new Calor.Tasks.CompileCalor { TypeCheck = false }.ComputeCacheInputs();

        Assert.NotEqual(on.Serialize(), off.Serialize());
        Assert.NotEqual(
            BuildStateCache.ComputeOptionsHash(on.Serialize()),
            BuildStateCache.ComputeOptionsHash(off.Serialize()));
        Assert.True(on.EnableTypeChecking);
        Assert.False(off.EnableTypeChecking);
    }

    /// <summary>
    /// The half that actually caught the defect. `CALOR_NO_TYPE_CHECK` changes what the task will
    /// report, so it MUST move the canonical fingerprint — otherwise a warm cache serves the other
    /// setting's findings and every unchanged file is silently skipped (#788). Two earlier
    /// revisions of this pin passed with the fix reverted: the first fed literal booleans straight
    /// to the token function, the second drove the task but never set the variable, so the value it
    /// was meant to observe was never in play. Verified by reverting: this one fails.
    /// </summary>
    [Fact]
    public void OptionsToken_EnvironmentOptOut_MovesTheToken()
    {
        var previous = Environment.GetEnvironmentVariable("CALOR_NO_TYPE_CHECK");
        try
        {
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", null);
            var checking = new Calor.Tasks.CompileCalor { TypeCheck = true }.ComputeCacheInputs();

            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", "1");
            var optedOut = new Calor.Tasks.CompileCalor { TypeCheck = true }.ComputeCacheInputs();

            Assert.True(checking.EnableTypeChecking);
            Assert.False(optedOut.EnableTypeChecking);
            Assert.NotEqual(
                BuildStateCache.ComputeOptionsHash(checking.Serialize()),
                BuildStateCache.ComputeOptionsHash(optedOut.Serialize()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CALOR_NO_TYPE_CHECK", previous);
        }
    }

    /// <summary>
    /// And the guard that keeps that honest: every other diagnostics-affecting option must still
    /// move the token, so the parameter above was not simply added to a token nobody consults.
    /// </summary>
    [Fact]
    public void CanonicalInputs_DistinguishEveryTaskInput_AndDoNotPersistPaths()
    {
        var reference = CreateTempFile("refs/Fake.dll", "reference-v1");
        var deps = CreateTempFile("project.deps.json", """{"runtimeTarget":{"name":"x"}}""");
        var baselineTask = new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir };
        var baseline = baselineTask.ComputeCacheInputs().Serialize();

        var variants = new[]
        {
            new Calor.Tasks.CompileCalor
            {
                ProjectDirectory = Path.Combine(_tempDir, "other-project")
            },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, Verbose = true },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, EnforceEffects = false },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, TypeCheck = false },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, Verify = true },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, EnableILAnalysis = true },
            new Calor.Tasks.CompileCalor { ProjectDirectory = _tempDir, ExperimentalFlags = "flag-a" },
            new Calor.Tasks.CompileCalor
            {
                ProjectDirectory = _tempDir,
                EnableILAnalysis = true,
                ReferencedAssemblies = [new TaskItem(reference)]
            },
            new Calor.Tasks.CompileCalor
            {
                ProjectDirectory = _tempDir,
                EnableILAnalysis = true,
                RuntimeDirectory = _tempDir
            },
            new Calor.Tasks.CompileCalor
            {
                ProjectDirectory = _tempDir,
                EnableILAnalysis = true,
                NuGetPackageRoot = _tempDir
            },
            new Calor.Tasks.CompileCalor
            {
                ProjectDirectory = _tempDir,
                EnableILAnalysis = true,
                DepsFilePath = deps
            }
        };

        Assert.All(variants, task => Assert.NotEqual(baseline, task.ComputeCacheInputs().Serialize()));
        Assert.DoesNotContain(_tempDir, variants[^1].ComputeCacheInputs().Serialize());
        var record = baselineTask.ComputeCacheInputs();
        Assert.Equal(BuildStateCache.CurrentOptionsSerializerVersion, record.SerializerVersion);
        Assert.Equal(BuildStateCache.CurrentFormatVersion, record.CacheSchemaVersion);
        Assert.Equal(BuildStateCache.CurrentCompilerSemanticsVersion, record.CompilerSemanticsVersion);
    }

    [Fact]
    public void CanonicalInputs_ReferenceOrderIsStable_AndContentChangesFingerprint()
    {
        var referenceA = CreateTempFile("refs/A.dll", "a-v1");
        var referenceB = CreateTempFile("refs/B.dll", "b-v1");
        var first = new Calor.Tasks.CompileCalor
        {
            ProjectDirectory = _tempDir,
            EnableILAnalysis = true,
            ReferencedAssemblies = [new TaskItem(referenceA), new TaskItem(referenceB)]
        };
        var reordered = new Calor.Tasks.CompileCalor
        {
            ProjectDirectory = _tempDir,
            EnableILAnalysis = true,
            ReferencedAssemblies = [new TaskItem(referenceB), new TaskItem(referenceA)]
        };

        var baseline = first.ComputeCacheInputs().Serialize();
        Assert.Equal(baseline, reordered.ComputeCacheInputs().Serialize());

        File.WriteAllText(referenceA, "a-v2");
        Assert.NotEqual(baseline, first.ComputeCacheInputs().Serialize());
    }

    [Fact]
    public void ManifestHash_IncludesUserLevelManifests_AndDistinguishesScopes()
    {
        var project = Path.Combine(_tempDir, "manifest-project");
        var user = Path.Combine(_tempDir, "manifest-user");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(project, "same.calor-effects.json"), "project-v1");
        File.WriteAllText(Path.Combine(user, "same.calor-effects.json"), "user-v1");

        var baseline = BuildStateCache.ComputeManifestHash([project], user);
        File.WriteAllText(Path.Combine(user, "same.calor-effects.json"), "user-v2");
        var userChanged = BuildStateCache.ComputeManifestHash([project], user);
        File.WriteAllText(Path.Combine(project, "same.calor-effects.json"), "project-v2");
        var projectChanged = BuildStateCache.ComputeManifestHash([project], user);

        Assert.NotEqual(baseline, userChanged);
        Assert.NotEqual(userChanged, projectChanged);
    }

    [Fact]
    public void ManifestLoader_LoadsSamePriorityManifestsInCanonicalPathOrder()
    {
        var directory = Path.Combine(_tempDir, "ordered-manifests");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "z.calor-effects.json"),
            """{"version":"1.0","mappings":[]}""");
        File.WriteAllText(
            Path.Combine(directory, "a.calor-effects.json"),
            """{"version":"1.0","mappings":[]}""");
        File.WriteAllText(
            Path.Combine(directory, "A.calor-effects.json"),
            """{"version":"1.0","mappings":[]}""");

        var loader = new Calor.Compiler.Effects.Manifests.ManifestLoader();
        loader.LoadManifestsFromDirectory(
            directory,
            Calor.Compiler.Effects.Manifests.ManifestPriority.UserLevel);

        var expected = Directory.GetFiles(directory, "*.calor-effects.json")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(
            expected,
            loader.LoadedManifests.Select(item => Path.GetFileName(item.Source.FilePath)));
    }

    [Fact]
    public void CanonicalInputs_HashResolvedRuntimeImplementationContent()
    {
        var runtimeSourceDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimeAssembly = Path.Combine(runtimeSourceDirectory, "System.Runtime.dll");
        var referenceAssembly = FindFrameworkReferenceAssembly("System.Runtime.dll");
        Assert.True(File.Exists(runtimeAssembly));

        var runtimeDirectory = Path.Combine(_tempDir, "runtime-implementations");
        Directory.CreateDirectory(runtimeDirectory);
        var copiedRuntimeAssembly = Path.Combine(runtimeDirectory, "System.Runtime.dll");
        File.Copy(runtimeAssembly, copiedRuntimeAssembly);
        var resolved = Calor.Compiler.Effects.IL.AssemblyIndex
            .ResolveImplementationAssemblyPaths(
                [referenceAssembly],
                new Calor.Compiler.Effects.IL.ILAnalysisOptions
                {
                    RuntimeDirectory = runtimeDirectory
                });
        Assert.Equal(copiedRuntimeAssembly, Assert.Single(resolved));

        var task = new Calor.Tasks.CompileCalor
        {
            ProjectDirectory = _tempDir,
            EnableILAnalysis = true,
            RuntimeDirectory = runtimeDirectory,
            ReferencedAssemblies = [new TaskItem(referenceAssembly)]
        };
        var baseline = task.ComputeCacheInputs().Serialize();

        File.AppendAllText(copiedRuntimeAssembly, "runtime-implementation-change");

        Assert.NotEqual(baseline, task.ComputeCacheInputs().Serialize());
    }

    // Regression guard for #883 (tracked under #998 in the F4/R2 test-suite audit).
    //
    // #883: "MSBuild incremental build: IL-analysis inputs are not in the options
    // hash." When EnableILAnalysis=true, mutating ReferencedAssemblies,
    // RuntimeDirectory, NuGetPackageRoot, or DepsFilePath against a warm cache
    // must invalidate the cache — otherwise diagnostics the new input set would
    // produce (effect discovery from IL, for example) are silently skipped.
    //
    // Status: #883 was closed on 2026-08-11 by PR #890. The 2026-08-18
    // test-suite audit's F4 finding was stale on this point; existing tests
    // (CanonicalInputs_MutatingAnalysisInput_InvalidatesWarmCache and the
    // *ContentMutation_InvalidatesWarmCache pair in CompileCalorIntegrationTests)
    // already cover related shapes. This test is kept as a live green guard
    // that pins the four-input matrix explicitly, so a future refactor that
    // silently drops one of them from the options hash fails here with a
    // named sub-case rather than requiring a re-read of the audit trail.
    //
    // If this test starts failing, the fix regressed — do not delete this test
    // to make it pass. Instead, restore the four inputs to the options-hash
    // path and verify with `git log --oneline src/Calor.Tasks/` for the #890
    // fix as reference.
    //
    // The four sub-cases are asserted individually so a partial regression
    // (e.g., only DepsFilePath drops out of the hash) surfaces as a specific
    // failure rather than a boolean pass/fail on the whole set.
    [Fact]
    public void ILAnalysisInputs_MutatingAnyOfTheFour_MustChangeOptionsHash_Issue883()
    {
        // Establish a warm baseline: EnableILAnalysis=true with one reference,
        // one runtime dir, one package root, and one deps file — the exact
        // configuration a real consumer produces via MSBuild ResolveAssemblyReferences.
        var reference = CreateTempFile("refs/Baseline.dll", "reference-v1");
        var runtimeDir = Path.Combine(_tempDir, "runtime");
        var packageRoot = Path.Combine(_tempDir, "packages");
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(packageRoot);
        var deps = CreateTempFile("project.deps.json", """{"runtimeTarget":{"name":"v1"}}""");

        CompileCalor MakeTask()
            => new()
            {
                ProjectDirectory = _tempDir,
                EnableILAnalysis = true,
                ReferencedAssemblies = [new TaskItem(reference)],
                RuntimeDirectory = runtimeDir,
                NuGetPackageRoot = packageRoot,
                DepsFilePath = deps
            };

        var baselineHash = BuildStateCache.ComputeOptionsHash(
            MakeTask().ComputeCacheInputs().Serialize());

        // 1. Swapping the referenced assembly to a different file must move the hash.
        var otherReference = CreateTempFile("refs/Other.dll", "reference-v2-different-content");
        var withDifferentReference = MakeTask();
        withDifferentReference.ReferencedAssemblies = [new TaskItem(otherReference)];
        Assert.NotEqual(
            baselineHash,
            BuildStateCache.ComputeOptionsHash(
                withDifferentReference.ComputeCacheInputs().Serialize()));

        // 2. Pointing at a different RuntimeDirectory must move the hash.
        var otherRuntimeDir = Path.Combine(_tempDir, "runtime-other");
        Directory.CreateDirectory(otherRuntimeDir);
        var withDifferentRuntime = MakeTask();
        withDifferentRuntime.RuntimeDirectory = otherRuntimeDir;
        Assert.NotEqual(
            baselineHash,
            BuildStateCache.ComputeOptionsHash(
                withDifferentRuntime.ComputeCacheInputs().Serialize()));

        // 3. Pointing at a different NuGetPackageRoot must move the hash.
        var otherPackageRoot = Path.Combine(_tempDir, "packages-other");
        Directory.CreateDirectory(otherPackageRoot);
        var withDifferentPackages = MakeTask();
        withDifferentPackages.NuGetPackageRoot = otherPackageRoot;
        Assert.NotEqual(
            baselineHash,
            BuildStateCache.ComputeOptionsHash(
                withDifferentPackages.ComputeCacheInputs().Serialize()));

        // 4. Editing DepsFilePath contents (same path) must move the hash —
        // content, not just path, must be fingerprinted, since a NuGet upgrade
        // that rewrites project.deps.json is the classic silent-skip scenario.
        var withDifferentDeps = MakeTask();
        File.WriteAllText(deps, """{"runtimeTarget":{"name":"v2"}}""");
        Assert.NotEqual(
            baselineHash,
            BuildStateCache.ComputeOptionsHash(
                withDifferentDeps.ComputeCacheInputs().Serialize()));
    }

    private static string FindFrameworkReferenceAssembly(string fileName)
    {
        var runtimeDirectory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent
            ?? throw new DirectoryNotFoundException("Could not locate the dotnet root.");
        var referencePackRoot = Path.Combine(
            dotnetRoot.FullName, "packs", "Microsoft.NETCore.App.Ref");
        var referenceAssembly = Directory.GetDirectories(referencePackRoot)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Select(path => Path.Combine(path, "ref", "net10.0", fileName))
            .FirstOrDefault(File.Exists);
        return referenceAssembly
            ?? throw new FileNotFoundException($"Could not locate framework reference {fileName}.");
    }

}
