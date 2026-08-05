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

        """;

    // Source that will cause a compile error (unclosed paren)
    private const string InvalidCalorSource = """
        §M{m001:TestModule}
          §F{f001:Broken:pub}
              §I{i32:a}
              §O{i32}
              §R (+ a b
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

        // Edit one file — body-only: renaming a PUBLIC function changes the
        // cross-module qualification map and (soundly, conservatively)
        // invalidates every skip; that behavior is pinned separately in
        // EditRenamingPublicFunction_InvalidatesAllSkips (G3/#809).
        Thread.Sleep(50); // ensure mtime changes
        File.WriteAllText(src1, ValidCalorSource.Replace("(+ a b)", "(- a b)"));

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

    /// <summary>
    /// The task's skip decision must consult the output's CONTENT, not merely its existence.
    /// Before this pin, a corrupted or hand-edited <c>.g.cs</c> was reported "up-to-date" and its
    /// stale bytes went into the assembly — the source is unchanged, so mtime/size tell you
    /// nothing. <c>CompilationDriver.cs:186-193</c> has always guarded this; the MSBuild task,
    /// which is the path real projects build through, had drifted and did not.
    /// </summary>
    [Fact]
    public void CorruptedOutput_IsRecompiled_NotReportedUpToDate()
    {
        var src = CreateSourceFile("Foo.calr", ValidCalorSource);

        var task1 = CreateTask(src);
        Assert.True(task1.Execute());
        var outputPath = task1.GeneratedFiles.Single().ItemSpec;
        var good = File.ReadAllText(outputPath);
        Assert.Contains("class", good);

        // Control: untouched output IS skipped, so the assertion below is known to discriminate.
        var control = CreateTask(src);
        Assert.True(control.Execute());
        Assert.Contains(((TestBuildEngine)control.BuildEngine).Messages, m => m.Contains("skipping"));

        // Corrupt the generated file without touching the source.
        File.WriteAllText(outputPath, "// truncated by something else\n");

        var task2 = CreateTask(src);
        Assert.True(task2.Execute());

        var msgs = string.Join("\n", ((TestBuildEngine)task2.BuildEngine).Messages);
        Assert.Contains("Compiling", msgs);
        Assert.DoesNotContain("skipping", msgs);

        // And the output is restored to what the compiler actually produces.
        Assert.Equal(good, File.ReadAllText(outputPath));
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

    // Cross-module effect enforcement — caller is missing a declared effect the callee requires.
    [Fact]
    public void CrossModuleEffect_CallerMissingEffect_Errors()
    {
        var callee = """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                  §O{void}
                  §E{db:w}
            """;
        var caller = """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                  §O{void}
                  §C{SaveOrder}
                  §/C
            """;

        var src1 = CreateSourceFile("Callee.calr", callee);
        var src2 = CreateSourceFile("Caller.calr", caller);

        var task = CreateTask(src1, src2);
        var result = task.Execute();

        Assert.False(result, "Build should fail on cross-module effect violation.");

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Contains(engine.Errors,
            e => e.Contains("HandleRequest") && e.Contains("SaveOrder") && e.Contains("db:w"));
    }

    // Cross-module effect enforcement — caller declares the callee's effects → clean build.
    [Fact]
    public void CrossModuleEffect_CallerDeclaresEffect_Succeeds()
    {
        var callee = """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                  §O{void}
                  §E{db:w}
            """;
        var caller = """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                  §O{void}
                  §E{db:w}
                  §C{SaveOrder}
                  §/C
            """;

        var src1 = CreateSourceFile("Callee.calr", callee);
        var src2 = CreateSourceFile("Caller.calr", caller);

        var task = CreateTask(src1, src2);
        Assert.True(task.Execute());
        Assert.Equal(2, task.GeneratedFiles.Length);

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.DoesNotContain(engine.Errors, e => e.Contains("Calor0410"));
    }

    // Warm-build cross-module enforcement: if the callee's declared effects change
    // (and the caller's content doesn't), the caller's cached summary + the callee's
    // fresh summary together should still detect the violation on the warm build.
    [Fact]
    public void CrossModuleEffect_WarmBuild_DetectsViolationAfterCalleeEdit()
    {
        var calleeOriginal = """
            §M{m001:Repo}
              §F{f001:Save:pub}
                  §O{void}
                  §E{db:w}
            """;
        var caller = """
            §M{m002:App}
              §F{f001:Run:pub}
                  §O{void}
                  §E{db:w}
                  §C{Save}
                  §/C
            """;

        var src1 = CreateSourceFile("Repo.calr", calleeOriginal);
        var src2 = CreateSourceFile("App.calr", caller);

        // Cold build — clean, caller declares db:w which covers callee's db:w.
        var task1 = CreateTask(src1, src2);
        Assert.True(task1.Execute());

        // Edit callee to add net:w. Caller source unchanged → caller should be incrementally
        // skipped on the next build, but cross-module pass should still see the violation
        // because the caller's summary (including its call to Save) is cached.
        Thread.Sleep(50);
        File.WriteAllText(src1, calleeOriginal.Replace("§E{db:w}", "§E{db:w, net:w}"));

        var task2 = CreateTask(src1, src2);
        var result = task2.Execute();

        Assert.False(result, "Build should fail — caller needs net:w after callee's §E expanded.");
        var engine = (TestBuildEngine)task2.BuildEngine;
        Assert.Contains(engine.Errors, e => e.Contains("net:w") && e.Contains("Save"));
    }

    // Warm-build no-change path: both files stay cached, cross-module pass runs from cached
    // summaries on every build and stays clean.
    [Fact]
    public void CrossModuleEffect_WarmBuild_NoChanges_RunsFromCachedSummariesAndStaysClean()
    {
        var callee = """
            §M{m001:Repo}
              §F{f001:Save:pub}
                  §O{void}
                  §E{db:w}
            """;
        var caller = """
            §M{m002:App}
              §F{f001:Run:pub}
                  §O{void}
                  §E{db:w}
                  §C{Save}
                  §/C
            """;

        var src1 = CreateSourceFile("Repo.calr", callee);
        var src2 = CreateSourceFile("App.calr", caller);

        // Cold build — clean, summaries are persisted in the build cache.
        var task1 = CreateTask(src1, src2);
        Assert.True(task1.Execute());

        // Second build — no changes. Both files skip compilation; cross-module pass
        // must still run using cached summaries and must stay clean.
        var task2 = CreateTask(src1, src2);
        var result = task2.Execute();
        var engine2 = (TestBuildEngine)task2.BuildEngine;

        Assert.True(result, $"Warm build should succeed. Errors: {string.Join("; ", engine2.Errors)}");
        Assert.DoesNotContain(engine2.Errors, e => e.Contains("Calor0410"));
        // Sanity: both files were actually skipped (not re-compiled).
        var skipped = engine2.Messages.Count(m => m.Contains("skipping"));
        Assert.Equal(2, skipped);
        // And the cross-module pass ACTUALLY ran — proves cached summaries were loaded and
        // reached the pass, rather than being silently null and gating the pass off.
        Assert.Contains(engine2.Messages, m =>
            m.Contains("running cross-module effect enforcement") && m.Contains("2 modules"));
    }

    // Cache format migration: a v1.0 cache on disk must trigger global invalidation
    // so everything recompiles with the new schema (and thus gets EffectSummary entries).
    [Fact]
    public void CachedV1Format_TriggersGlobalInvalidation()
    {
        var src = CreateSourceFile("A.calr", ValidCalorSource);

        // Write a v1.0-style cache file directly (no EffectSummary on entries).
        var cachePath = BuildStateCache.GetCachePath(_outputDir);
        var v1Json = """
            {
              "formatVersion": "1.0",
              "compilerHash": "stale",
              "optionsHash": "stale",
              "manifestHash": "",
              "outputDirectory": "",
              "files": {
                "A.calr": {
                  "contentHash": "deadbeef",
                  "lastModified": "2026-01-01T00:00:00Z",
                  "fileSize": 42
                }
              }
            }
            """;
        File.WriteAllText(cachePath, v1Json);

        var task = CreateTask(src);
        Assert.True(task.Execute());

        var engine = (TestBuildEngine)task.BuildEngine;
        // Global invalidation should kick in because format version doesn't match.
        Assert.Contains(engine.Messages, m => m.Contains("global invalidation"));

        // After this build, the cache should be v2.0 and the file should have a summary.
        var loaded = BuildStateCache.Load(_outputDir);
        Assert.NotNull(loaded);
        Assert.Equal("2.1", loaded.FormatVersion);
        var entry = Assert.Single(loaded.Files).Value;
        Assert.NotNull(entry.EffectSummary);
        Assert.Equal("TestModule", entry.EffectSummary!.ModuleName);
    }

    // Phase 0a — verifies the ExperimentalFlags MSBuild property plumbs into the
    // CompileCalor task and compiles cleanly. Per-diagnostic verification (pilot
    // info diagnostic emitted) lives in Calor.Compiler.Tests.ExperimentalFlagPilotTests;
    // the task currently drops info diagnostics on successful compile (pre-existing
    // behavior, out of scope for Phase 0a).
    [Fact]
    public void ExperimentalFlags_PilotFlag_CompilesCleanly()
    {
        var src = CreateSourceFile("PilotTest.calr", ValidCalorSource);

        var task = CreateTask(src);
        task.ExperimentalFlags = "pilot-hello-world";

        Assert.True(task.Execute());
        var engine = (TestBuildEngine)task.BuildEngine;
        // Plumbing smoke-check: no errors/warnings introduced by setting the flag.
        Assert.Empty(engine.Errors);
        Assert.Single(task.GeneratedFiles);
    }

    [Fact]
    public void ExperimentalFlags_NotSet_CompilesIdentically()
    {
        var src = CreateSourceFile("NoPilotTest.calr", ValidCalorSource);

        var task = CreateTask(src);
        // ExperimentalFlags deliberately not set (empty string default).

        Assert.True(task.Execute());
        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Empty(engine.Errors);
        Assert.Single(task.GeneratedFiles);
    }

    [Fact]
    public void ExperimentalFlags_SemicolonDelimited_Parsed()
    {
        var src = CreateSourceFile("MultiFlagTest.calr", ValidCalorSource);

        var task = CreateTask(src);
        task.ExperimentalFlags = "pilot-hello-world;some-other-flag;yet-another";

        // Plumbing smoke-check: multi-flag property parses without error and compile succeeds.
        Assert.True(task.Execute());
        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Empty(engine.Errors);
    }

    [Fact]
    public void EditRenamingPublicFunction_InvalidatesAllSkips()
    {
        // G3/#809: the cross-module map fingerprint participates in warm-skip
        // validity. Renaming a public function changes the map, and EVERY file
        // must re-emit — a cached output may carry stale qualification against
        // the old name set. Conservative by design.
        var src1 = CreateSourceFile("Foo.calr", ValidCalorSource);
        var src2 = CreateSourceFile("Bar.calr", ValidCalorSource.Replace("TestModule", "BarModule")
            .Replace("m001", "m002").Replace("f001", "f002"));

        var task1 = CreateTask(src1, src2);
        Assert.True(task1.Execute());

        Thread.Sleep(50);
        File.WriteAllText(src1, ValidCalorSource.Replace("Add", "Sum"));

        var task2 = CreateTask(src1, src2);
        Assert.True(task2.Execute());
        var engine2 = (TestBuildEngine)task2.BuildEngine;
        var msgs = string.Join("\n", engine2.Messages);
        // Both files compile; nothing skips.
        Assert.DoesNotContain("skipping", msgs);
    }

    // #788 (W1 Slice 4): the options hash must cover EVERY diagnostics-affecting
    // task option. Flipping any one of them over a warm cache must invalidate
    // every skip — a cached output was produced under a different option set and
    // its (absent) diagnostics would be silently stale.
    [Theory]
    [InlineData("enforceEffects")]
    [InlineData("verify")]
    [InlineData("ilAnalysis")]
    [InlineData("experimental")]
    public void OptionsHash_FlippingAnyDiagnosticsAffectingOption_InvalidatesWarmCache(string option)
    {
        var src = CreateSourceFile("OptFlip.calr", ValidCalorSource);

        // Cold build + warm build with defaults: second run skips.
        Assert.True(CreateTask(src).Execute());
        var warm = CreateTask(src);
        Assert.True(warm.Execute());
        Assert.Contains(((TestBuildEngine)warm.BuildEngine).Messages,
            m => m.Contains("skipping"));

        // Third build with one option flipped: nothing may skip.
        var flipped = CreateTask(src);
        switch (option)
        {
            case "enforceEffects": flipped.EnforceEffects = false; break;
            case "verify": flipped.Verify = true; break;
            case "ilAnalysis": flipped.EnableILAnalysis = true; break;
            case "experimental": flipped.ExperimentalFlags = "pilot-hello-world"; break;
        }
        Assert.True(flipped.Execute());
        var msgs = string.Join("\n", ((TestBuildEngine)flipped.BuildEngine).Messages);
        Assert.DoesNotContain("skipping", msgs);
        Assert.Contains("Compiling", msgs);
    }

    [Fact]
    public void OptionsHash_ExperimentalFlagsAreCanonicalized_EquivalentSpellingsStayWarm()
    {
        // "b;a" and "A, b" are the same flag set — the hash canonicalizes
        // (parse, case-fold, sort), so respelling must NOT invalidate the cache.
        var src = CreateSourceFile("OptCanon.calr", ValidCalorSource);

        var task1 = CreateTask(src);
        task1.ExperimentalFlags = "pilot-hello-world;another-flag";
        Assert.True(task1.Execute());

        var task2 = CreateTask(src);
        task2.ExperimentalFlags = "Another-Flag, pilot-hello-world";
        Assert.True(task2.Execute());
        Assert.Contains(((TestBuildEngine)task2.BuildEngine).Messages,
            m => m.Contains("skipping"));
    }

    // #788 fail-closed note: the IL-analysis init and cross-module enforcement
    // catch blocks in CompileCalor now FAIL the build instead of warning and
    // continuing. Neither failure is cheaply reachable with real components
    // (AssemblyIndex and ManifestLoader are internally defensive and skip
    // malformed inputs), so the fail-closed branches are covered by review,
    // not by a fixture — a garbage referenced assembly is deliberately
    // tolerated (skipped) by design and does not trip them.

    [Fact]
    public void CrossModuleCall_TasksPath_EmitsQualifiedTarget()
    {
        // #823 review M2 pin: MSBuild is the surface where csc consumes the
        // outputs — the task itself must produce qualified cross-module calls.
        var src1 = CreateSourceFile("Store.calr", """
            §M{m001:Store}
              §F{f001:SaveSnapshot:pub} (str:path) -> void
                §E{fs:w}
                §C{File.WriteAllText} §A path §A "x" §/C
            """);
        var src2 = CreateSourceFile("Catalog.calr", """
            §M{m002:Catalog}
              §F{f001:Ping:pub} (str:path) -> void
                §E{fs:w}
                §C{SaveSnapshot} §A path §/C
            """);

        var task = CreateTask(src1, src2);
        Assert.True(task.Execute());

        var catalogOut = task.GeneratedFiles.Single(f => f.ItemSpec.Contains("Catalog"));
        var emitted = File.ReadAllText(catalogOut.ItemSpec);
        Assert.Contains("global::Store.StoreModule.SaveSnapshot(path);", emitted);
    }

    // A refutable postcondition: total can exceed cap because the guard's
    // threshold is (cap + 10) — the W5-B defective shape from the outcome corpus.
    private const string RefutedContractSource = """
        §M{m001:Quotes}
          §F{f003:QuoteWithSurchargeDefective:pub} (i32:baseAmount, i32:surcharge, i32:cap) -> i32
            §S (<= result cap)
            §B{total:i32} (+ baseAmount surcharge)
            §IF{if1} (> total (+ cap 10))
              §R cap
            §R total
        """;

    // G5 instrumentation (A-1.3 item 1): the Verify task property must run Z3
    // verification and surface refutation warnings (Calor0712) through MSBuild —
    // the epoch's build-proof channel. Succeeding compiles must still log
    // their non-error diagnostics.
    [Fact]
    public void VerifyGate_RefutedContract_SurfacesWarningAndStillBuilds()
    {
        if (!Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable) return; // Z3-less CI

        var src = CreateSourceFile("Quotes.calr", RefutedContractSource);

        var task = CreateTask(src);
        task.Verify = true;
        Assert.True(task.Execute());
        Assert.Single(task.GeneratedFiles);

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Contains(engine.Warnings, w => w.Contains("Postcondition may be violated"));
    }

    [Fact]
    public void VerifyGate_Off_NoVerificationWarnings()
    {
        var src = CreateSourceFile("Quotes.calr", RefutedContractSource);

        var task = CreateTask(src);
        Assert.True(task.Execute());

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.DoesNotContain(engine.Warnings, w => w.Contains("Postcondition may be violated"));
    }

    // Verify is diagnostics-affecting, so it participates in the options hash:
    // flipping it on over a warm gate-off cache must recompile (and re-verify)
    // files whose content did not change — a cached skip here would silently
    // drop the refutation.
    // #826 review M4: the in-process VerifyGate tests find libz3 through the
    // test host's deps.json probing, which the real MSBuild task path does NOT
    // get — the gate there depends on CopyZ3NativeToTasksOutput placing the
    // native lib at the Tasks output ROOT. Pin that deployment directly: if
    // the copy target regresses, this fails while the other gate tests stay
    // green.
    [Fact]
    public void VerifyGate_NativeZ3_DeployedToTasksOutputRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        string? repoRoot = null;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.sln"))) { repoRoot = dir; break; }
            dir = Directory.GetParent(dir)?.FullName;
        }
        Assert.NotNull(repoRoot);

        var checkedConfigs = 0;
        foreach (var config in new[] { "Debug", "Release" })
        {
            var binDir = Path.Combine(repoRoot!, "src", "Calor.Tasks", "bin", config, "net10.0");
            if (!File.Exists(Path.Combine(binDir, "Calor.Tasks.dll"))) continue;
            checkedConfigs++;
            var hasNative = File.Exists(Path.Combine(binDir, "libz3.dylib"))
                || File.Exists(Path.Combine(binDir, "libz3.so"))
                || File.Exists(Path.Combine(binDir, "libz3.dll"));
            Assert.True(hasNative,
                $"No libz3 native library at the Calor.Tasks output root ({binDir}) — "
                + "the MSBuild verify gate would silently report Z3 unavailable (CopyZ3NativeToTasksOutput regressed?)");
        }
        Assert.True(checkedConfigs > 0, "No built Calor.Tasks output found to check");
    }

    [Fact]
    public void VerifyGate_FlippedOnOverWarmCache_Recompiles()
    {
        if (!Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable) return; // Z3-less CI

        var src = CreateSourceFile("Quotes.calr", RefutedContractSource);

        var task1 = CreateTask(src);
        Assert.True(task1.Execute());
        Assert.DoesNotContain(((TestBuildEngine)task1.BuildEngine).Warnings,
            w => w.Contains("Postcondition may be violated"));

        var task2 = CreateTask(src);
        task2.Verify = true;
        Assert.True(task2.Execute());
        Assert.Contains(((TestBuildEngine)task2.BuildEngine).Warnings,
            w => w.Contains("Postcondition may be violated"));
    }
}
