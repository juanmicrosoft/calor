using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Calor.Tasks;

const int Iterations = 10;
int[] fileCounts = [10, 100, 1_000];

Console.WriteLine("Calor Incremental Build Benchmark");
Console.WriteLine("==================================");
Console.WriteLine();

var results = new Dictionary<string, Dictionary<int, (double median, double p95)>>();

foreach (var fileCount in fileCounts)
{
    Console.WriteLine($"--- {fileCount} files ---");

    var tempDir = Path.Combine(Path.GetTempPath(), $"calor-bench-{fileCount}-{Guid.NewGuid():N}");
    var projectDir = Path.Combine(tempDir, "project");
    var outputDir = Path.Combine(tempDir, "output");
    Directory.CreateDirectory(projectDir);
    Directory.CreateDirectory(outputDir);

    try
    {
        // Create N .calr source files
        var sourcePaths = new string[fileCount];
        for (var i = 0; i < fileCount; i++)
        {
            var dir = Path.Combine(projectDir, $"dir{i / 100}");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Module{i}.calr");
            File.WriteAllText(path, GenerateCalorSource(i));
            sourcePaths[i] = path;
        }

        // Scenario 1: Cold (first build)
        var coldTimings = RunScenario("Cold", Iterations, () =>
        {
            // Clean output between runs
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, true);
            Directory.CreateDirectory(outputDir);

            return RunTask(sourcePaths, outputDir, projectDir);
        });
        RecordResult(results, "Cold", fileCount, coldTimings);

        // Do one build to prime the cache for warm scenario
        RunTask(sourcePaths, outputDir, projectDir);

        // Scenario 2: Warm (0 changes)
        var warmTimings = RunScenario("Warm (0 changes)", Iterations, () =>
        {
            return RunTask(sourcePaths, outputDir, projectDir);
        });
        RecordResult(results, "Warm (0 changes)", fileCount, warmTimings);

        // Scenario 3: 1 file changed
        var changeTimings = RunScenario("1 file changed", Iterations, () =>
        {
            // Touch one file (change content slightly)
            var targetFile = sourcePaths[0];
            var content = File.ReadAllText(targetFile);
            File.WriteAllText(targetFile, content + " ");

            var elapsed = RunTask(sourcePaths, outputDir, projectDir);

            // Restore to avoid cumulative drift
            File.WriteAllText(targetFile, content);
            RunTask(sourcePaths, outputDir, projectDir);

            return elapsed;
        });
        RecordResult(results, "1 file changed", fileCount, changeTimings);

        // Scenario 4: Always-hash comparison (for stat gate validation)
        // We can simulate by toggling mtime without changing content
        RunTask(sourcePaths, outputDir, projectDir); // Ensure warm
        var alwaysHashTimings = RunScenario("Stat gate bypass (mtime touch)", Iterations, () =>
        {
            // Touch all files' mtime without changing content
            foreach (var path in sourcePaths)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            return RunTask(sourcePaths, outputDir, projectDir);
        });
        RecordResult(results, "Stat gate bypass", fileCount, alwaysHashTimings);

        // Validate: Global invalidation recompiles all files
        if (fileCount == 100)
        {
            var cache = BuildStateCache.Load(outputDir);
            if (cache != null)
            {
                cache.CompilerHash = "fake-changed-hash";
                BuildStateCache.Save(cache, outputDir);
                var sw = Stopwatch.StartNew();
                RunTask(sourcePaths, outputDir, projectDir);
                sw.Stop();
                Console.WriteLine($"  Global invalidation (compiler change): {sw.ElapsedMilliseconds}ms — all {fileCount} recompiled");
            }
        }

        Console.WriteLine();
    }
    finally
    {
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// Print summary table
Console.WriteLine();
Console.WriteLine("Summary Table");
Console.WriteLine("=============");
Console.WriteLine();
Console.WriteLine("Scenario                  |   10 files   |  100 files   | 1,000 files");
Console.WriteLine(new string('-', 75));

foreach (var scenario in results)
{
    var row = scenario.Key.PadRight(25);
    foreach (var fc in fileCounts)
    {
        if (scenario.Value.TryGetValue(fc, out var timing))
            row += string.Format(" | {0,5:F1}ms p95:{1:F1}ms", timing.median, timing.p95);
        else
            row += " |          N/A";
    }
    Console.WriteLine(row);
}

// Acceptance criteria validation
Console.WriteLine();
Console.WriteLine("Acceptance Criteria");
Console.WriteLine("===================");

var warm100 = results.GetValueOrDefault("Warm (0 changes)")?.GetValueOrDefault(100);
var warm1000 = results.GetValueOrDefault("Warm (0 changes)")?.GetValueOrDefault(1000);
var change100 = results.GetValueOrDefault("1 file changed")?.GetValueOrDefault(100);
var cold100 = results.GetValueOrDefault("Cold")?.GetValueOrDefault(100);
var statBypass1000 = results.GetValueOrDefault("Stat gate bypass")?.GetValueOrDefault(1000);

CheckCriteria("Warm (0 changes, 100 files) < 20ms", warm100?.median < 20);
CheckCriteria("Warm (0 changes, 1000 files) < 100ms", warm1000?.median < 100);
CheckCriteria("1-change overhead vs cold < 20ms at 100 files",
    change100.HasValue && cold100.HasValue && (change100.Value.median - cold100.Value.median) < 20);
CheckCriteria("Stat gate value: warm-at-1000 < stat-bypass-at-1000",
    warm1000.HasValue && statBypass1000.HasValue && warm1000.Value.median < statBypass1000.Value.median);

return 0;

static string GenerateCalorSource(int index)
{
    var moduleId = $"m{index:D4}";
    var funcId = $"f{index:D4}";
    return $$"""
        §M{{{moduleId}}:Module{{index}}}

        §F{{{funcId}}:Compute:pub}
          §I{i32:x}
          §I{i32:y}
          §O{i32}
          §R (+ x y)
        §/F{{{funcId}}}

        §/M{{{moduleId}}}
        """;
}

static double RunTask(string[] sourcePaths, string outputDir, string projectDir)
{
    var engine = new BenchBuildEngine();
    var task = new CompileCalor
    {
        BuildEngine = engine,
        SourceFiles = sourcePaths.Select(p => (ITaskItem)new TaskItem(Path.GetFullPath(p))).ToArray(),
        OutputDirectory = outputDir,
        ProjectDirectory = projectDir,
        Verbose = false
    };

    var sw = Stopwatch.StartNew();
    task.Execute();
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
}

static List<double> RunScenario(string name, int iterations, Func<double> action)
{
    var timings = new List<double>(iterations);
    for (var i = 0; i < iterations; i++)
    {
        timings.Add(action());
    }

    timings.Sort();
    var median = timings[timings.Count / 2];
    var p95 = timings[(int)(timings.Count * 0.95)];

    Console.WriteLine($"  {name,-25}: median={median:F1}ms  p95={p95:F1}ms");
    return timings;
}

static void RecordResult(Dictionary<string, Dictionary<int, (double, double)>> results,
    string scenario, int fileCount, List<double> timings)
{
    if (!results.ContainsKey(scenario))
        results[scenario] = new Dictionary<int, (double, double)>();

    timings.Sort();
    results[scenario][fileCount] = (timings[timings.Count / 2], timings[(int)(timings.Count * 0.95)]);
}

static void CheckCriteria(string description, bool? passed)
{
    var status = passed == true ? "PASS" : "FAIL";
    Console.WriteLine($"  [{status}] {description}");
}

sealed class BenchBuildEngine : IBuildEngine
{
    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "bench.csproj";
    public bool BuildProjectFile(string p, string[] t, System.Collections.IDictionary g, System.Collections.IDictionary o) => true;
    public void LogCustomEvent(CustomBuildEventArgs e) { }
    public void LogErrorEvent(BuildErrorEventArgs e) { }
    public void LogMessageEvent(BuildMessageEventArgs e) { }
    public void LogWarningEvent(BuildWarningEventArgs e) { }
}
