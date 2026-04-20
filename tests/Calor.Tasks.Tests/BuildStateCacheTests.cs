using Xunit;
using Calor.Tasks;

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

    // Test 3: Stat gate hit — matching (mtime, size) → skip without hashing
    [Fact]
    public void IsFileUpToDate_MatchingStatFields_ReturnsTrue()
    {
        var filePath = CreateTempFile("test.calr", "content");
        var fileInfo = new FileInfo(filePath);

        var entry = new BuildFileEntry
        {
            ContentHash = "wrong-hash-should-not-matter",
            LastModified = fileInfo.LastWriteTimeUtc,
            FileSize = fileInfo.Length
        };

        Assert.True(BuildStateCache.IsFileUpToDate(entry, filePath));
    }

    // Test 4: Stat gate miss, hash match — mtime changed, content same → skip
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

        // Stat gate misses (mtime differs), falls through to hash check, hash matches → skip
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
        Assert.Equal("1.0", loaded.FormatVersion);
        Assert.Equal("abc123", loaded.CompilerHash);
        Assert.Equal("def456", loaded.OptionsHash);
        Assert.Equal("ghi789", loaded.ManifestHash);
        Assert.Equal("obj/Debug/net10.0/calor/", loaded.OutputDirectory);
        Assert.Single(loaded.Files);
        Assert.True(loaded.Files.ContainsKey("src/Foo.calr"));
        Assert.Equal("hash1", loaded.Files["src/Foo.calr"].ContentHash);
        Assert.Equal(4096, loaded.Files["src/Foo.calr"].FileSize);
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
}
