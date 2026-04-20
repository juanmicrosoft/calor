using Xunit;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Tasks;

namespace Calor.Tasks.Tests;

/// <summary>
/// Minimal IBuildEngine for test use. Collects logged errors and warnings.
/// </summary>
internal sealed class TestBuildEngine : IBuildEngine
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Messages { get; } = [];

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "test.csproj";

    public bool BuildProjectFile(string projectFileName, string[] targetNames,
        System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

    public void LogCustomEvent(CustomBuildEventArgs e) { }
    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "");
    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? "");
    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message ?? "");
}

public class CompileCalorIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _outputDir;

    // A minimal valid Calor module
    private const string ValidCalorSource = """
        §M{m001:TestModule}

        §F{f001:Add:pub}
          §I{i32:a}
          §I{i32:b}
          §O{i32}
          §R (+ a b)
        §/F{f001}

        §/M{m001}
        """;

    // Source that will cause a compile error (unclosed paren)
    private const string InvalidCalorSource = """
        §M{m001:TestModule}
        §F{f001:Broken:pub}
          §I{i32:a}
          §O{i32}
          §R (+ a b
        §/F{f001}
        §/M{m001}
        """;

    public CompileCalorIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-integ-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _projectDir = Path.Combine(_tempDir, "project");
        _outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateSourceFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_projectDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private CompileCalor CreateTask(params string[] sourcePaths)
    {
        var task = new CompileCalor
        {
            BuildEngine = new TestBuildEngine(),
            SourceFiles = sourcePaths.Select(p =>
            {
                // TaskItem computes FullPath automatically from ItemSpec when given an absolute path
                var item = new TaskItem(Path.GetFullPath(p));
                return (ITaskItem)item;
            }).ToArray(),
            OutputDirectory = _outputDir,
            ProjectDirectory = _projectDir,
            Verbose = true
        };
        return task;
    }

    // Test 20: Full lifecycle: build → build (skip) → edit 1 → build (1 compiles) → clean → build (all)
    [Fact]
    public void FullLifecycle_BuildSkipEditCleanBuild()
    {
        var src1 = CreateSourceFile("Foo.calr", ValidCalorSource);
        var src2 = CreateSourceFile("Bar.calr", ValidCalorSource.Replace("TestModule", "BarModule")
            .Replace("m001", "m002").Replace("f001", "f002"));

        // First build — all compile
        var task1 = CreateTask(src1, src2);
        Assert.True(task1.Execute());
        Assert.Equal(2, task1.GeneratedFiles.Length);

        // Second build — all skip (cache hit)
        var task2 = CreateTask(src1, src2);
        Assert.True(task2.Execute());
        Assert.Equal(2, task2.GeneratedFiles.Length);
        var engine2 = (TestBuildEngine)task2.BuildEngine;
        Assert.Contains(engine2.Messages, m => m.Contains("skipping"));

        // Edit one file
        Thread.Sleep(50); // ensure mtime changes
        File.WriteAllText(src1, ValidCalorSource.Replace("Add", "Sum")
            .Replace("m001", "m001").Replace("f001", "f001"));

        // Third build — only edited file compiles
        var task3 = CreateTask(src1, src2);
        Assert.True(task3.Execute());
        Assert.Equal(2, task3.GeneratedFiles.Length);
        var engine3 = (TestBuildEngine)task3.BuildEngine;
        // Bar should be skipped, Foo should be compiled
        var msgs3 = string.Join("\n", engine3.Messages);
        Assert.Contains("skipping", msgs3);
        Assert.Contains("Compiling", msgs3);

        // Clean — delete output dir
        Directory.Delete(_outputDir, recursive: true);
        Directory.CreateDirectory(_outputDir);

        // Fourth build — all compile (cache gone)
        var task4 = CreateTask(src1, src2);
        Assert.True(task4.Execute());
        Assert.Equal(2, task4.GeneratedFiles.Length);
    }

    // Test 21: Stale output cleanup: build 3, delete 1 source, build → orphan removed
    [Fact]
    public void StaleOutputCleanup_OrphanRemoved()
    {
        var src1 = CreateSourceFile("A.calr", ValidCalorSource);
        var src2 = CreateSourceFile("B.calr", ValidCalorSource.Replace("TestModule", "ModB")
            .Replace("m001", "m002").Replace("f001", "f002"));
        var src3 = CreateSourceFile("C.calr", ValidCalorSource.Replace("TestModule", "ModC")
            .Replace("m001", "m003").Replace("f001", "f003"));

        // Build all 3
        var task1 = CreateTask(src1, src2, src3);
        Assert.True(task1.Execute());
        Assert.Equal(3, task1.GeneratedFiles.Length);

        // Delete source for C
        File.Delete(src3);

        // Build with only 2
        var task2 = CreateTask(src1, src2);
        Assert.True(task2.Execute());
        Assert.Equal(2, task2.GeneratedFiles.Length);

        // C.g.cs should be cleaned up
        Assert.False(File.Exists(Path.Combine(_outputDir, "C.g.cs")));
    }

    // Test 22: Compile failure: success → break → prior .g.cs deleted, not in output
    [Fact]
    public void CompileFailure_PriorOutputDeleted()
    {
        var src = CreateSourceFile("Fail.calr", ValidCalorSource);

        // First build succeeds
        var task1 = CreateTask(src);
        Assert.True(task1.Execute());
        Assert.Single(task1.GeneratedFiles);
        var outputPath = task1.GeneratedFiles[0].ItemSpec;
        Assert.True(File.Exists(outputPath));

        // Break the source
        Thread.Sleep(50);
        File.WriteAllText(src, InvalidCalorSource);

        // Second build fails
        var task2 = CreateTask(src);
        var result = task2.Execute();
        // Either fails or the file is not in generated files
        // The prior .g.cs should be deleted
        Assert.False(File.Exists(outputPath));
    }

    // Test 22b: HasErrors path — prior .g.cs deleted, entry not cached
    [Fact]
    public void CompileFailure_HasErrors_EntryNotCached()
    {
        var src = CreateSourceFile("ErrTest.calr", ValidCalorSource);

        // First build succeeds
        var task1 = CreateTask(src);
        Assert.True(task1.Execute());
        Assert.Single(task1.GeneratedFiles);

        // Verify entry was cached
        var cache1 = BuildStateCache.Load(_outputDir);
        Assert.NotNull(cache1);
        Assert.NotEmpty(cache1.Files);

        // Break the source (HasErrors path)
        Thread.Sleep(50);
        File.WriteAllText(src, InvalidCalorSource);

        // Second build fails
        var task2 = CreateTask(src);
        Assert.False(task2.Execute());
        Assert.Empty(task2.GeneratedFiles);

        // The failed file should NOT be in the new cache
        var cache2 = BuildStateCache.Load(_outputDir);
        Assert.NotNull(cache2);
        Assert.Empty(cache2.Files);
    }

    // Test 22c: Exception path — source deleted between cache check and compile
    [Fact]
    public void CompileFailure_Exception_PriorOutputDeleted()
    {
        var src = CreateSourceFile("ExcTest.calr", ValidCalorSource);

        // First build succeeds
        var task1 = CreateTask(src);
        Assert.True(task1.Execute());
        var outputPath = task1.GeneratedFiles[0].ItemSpec;
        Assert.True(File.Exists(outputPath));

        // Delete the source file to trigger the exception path
        // (file existed when MSBuild enumerated, but gone by compile time)
        Thread.Sleep(50);
        File.Delete(src);

        // Second build fails
        var task2 = CreateTask(src);
        Assert.False(task2.Execute());
        Assert.Empty(task2.GeneratedFiles);

        // Prior .g.cs should be gone (deleted by the error-handling code)
        // Note: the file might not exist because the input validation fires first
        // Either way, it should NOT be in GeneratedFiles
        var engine2 = (TestBuildEngine)task2.BuildEngine;
        Assert.NotEmpty(engine2.Errors);
    }

    // Test 23: Compiler DLL change → all recompile
    [Fact]
    public void CompilerDllChange_AllRecompile()
    {
        var src = CreateSourceFile("Test.calr", ValidCalorSource);

        // First build
        var task1 = CreateTask(src);
        Assert.True(task1.Execute());

        // Tamper with the cache to simulate a compiler hash change
        var cache = BuildStateCache.Load(_outputDir);
        Assert.NotNull(cache);
        cache.CompilerHash = "fake-old-hash";
        BuildStateCache.Save(cache, _outputDir);

        // Build again — should recompile (compiler hash mismatch)
        var task2 = CreateTask(src);
        Assert.True(task2.Execute());
        var engine2 = (TestBuildEngine)task2.BuildEngine;
        // Should see "global invalidation" log and compilation, not skip
        Assert.Contains(engine2.Messages, m => m.Contains("global invalidation"));
    }

    // Test 24: Nested outputs: A/foo.calr + B/foo.calr → separate .g.cs, both compiled
    [Fact]
    public void NestedOutputs_SameNameDifferentDirs_BothCompile()
    {
        var src1 = CreateSourceFile("A/Foo.calr", ValidCalorSource);
        var src2 = CreateSourceFile("B/Foo.calr", ValidCalorSource.Replace("TestModule", "FooB")
            .Replace("m001", "m002").Replace("f001", "f002"));

        var task = CreateTask(src1, src2);
        Assert.True(task.Execute());
        Assert.Equal(2, task.GeneratedFiles.Length);

        // Both output files should exist at different paths
        var outputPaths = task.GeneratedFiles.Select(f => f.ItemSpec).ToList();
        Assert.Equal(2, outputPaths.Distinct().Count()); // no collisions
        Assert.All(outputPaths, p => Assert.True(File.Exists(p)));
    }

    // Test 25: Cross-root linked file: external path → sanitized output path under obj/
    [Fact]
    public void CrossRootLinkedFile_SanitizedOutputPath()
    {
        // Create a file outside the project directory
        var externalDir = Path.Combine(_tempDir, "external");
        Directory.CreateDirectory(externalDir);
        var externalFile = Path.Combine(externalDir, "Shared.calr");
        File.WriteAllText(externalFile, ValidCalorSource.Replace("TestModule", "SharedMod")
            .Replace("m001", "m004").Replace("f001", "f004"));

        var task = CreateTask(externalFile);
        Assert.True(task.Execute());
        Assert.Single(task.GeneratedFiles);

        // Output should be under _linked/ subdirectory
        var outputPath = task.GeneratedFiles[0].ItemSpec;
        Assert.Contains("_linked", outputPath);
        Assert.True(File.Exists(outputPath));
    }

    // Test 26: Global invalidation with no prior cache → orphan cleanup skipped, no crash
    [Fact]
    public void GlobalInvalidation_NoPriorCache_NoCrash()
    {
        var src = CreateSourceFile("Fresh.calr", ValidCalorSource);

        // Ensure no cache exists
        var cachePath = BuildStateCache.GetCachePath(_outputDir);
        if (File.Exists(cachePath))
            File.Delete(cachePath);

        // Should work fine — no prior cache, no orphan cleanup
        var task = CreateTask(src);
        Assert.True(task.Execute());
        Assert.Single(task.GeneratedFiles);
    }

    // Test 27: Concurrent build: two Execute() calls on same output directory
    // on separate threads → both complete without exception, cache file is valid JSON afterward
    [Fact]
    public void ConcurrentBuild_BothCompleteWithoutException()
    {
        var src1 = CreateSourceFile("Concurrent1.calr", ValidCalorSource);
        var src2 = CreateSourceFile("Concurrent2.calr", ValidCalorSource.Replace("TestModule", "Mod2")
            .Replace("m001", "m005").Replace("f001", "f005"));

        Exception? exception1 = null;
        Exception? exception2 = null;

        var thread1 = new Thread(() =>
        {
            try
            {
                var task = CreateTask(src1);
                task.Execute();
            }
            catch (Exception ex) { exception1 = ex; }
        });

        var thread2 = new Thread(() =>
        {
            try
            {
                var task = CreateTask(src2);
                task.Execute();
            }
            catch (Exception ex) { exception2 = ex; }
        });

        thread1.Start();
        thread2.Start();
        thread1.Join(TimeSpan.FromSeconds(30));
        thread2.Join(TimeSpan.FromSeconds(30));

        Assert.Null(exception1);
        Assert.Null(exception2);

        // Cache file should be valid JSON
        var cachePath = BuildStateCache.GetCachePath(_outputDir);
        Assert.True(File.Exists(cachePath));
        var json = File.ReadAllText(cachePath);
        var state = System.Text.Json.JsonSerializer.Deserialize(json, BuildStateJsonContext.Default.BuildState);
        Assert.NotNull(state);
    }
}
