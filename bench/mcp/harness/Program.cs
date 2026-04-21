using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;

/// <summary>
/// MCP Benchmark Harness — measures round-trips, tokens, and diagnostic counts
/// for a corpus of Calor tasks. Used to baseline and validate MCP tool improvements.
///
/// Usage: dotnet run -- [--tasks-dir path] [--runs N] [--feature-flag name]
/// </summary>

var tasksDir = args.Length > 1 && args[0] == "--tasks-dir" ? args[1]
    : Path.Combine(FindRepoRoot(), "bench", "mcp", "tasks");
var runs = args.Length > 3 && args[2] == "--runs" ? int.Parse(args[3]) : 5;

Console.WriteLine("MCP Benchmark Harness");
Console.WriteLine("=====================");

// Pre-flight: validate MCP resource JSON
Console.WriteLine("Validating MCP resources...");
ValidateJsonResource("calor://effects", Calor.Compiler.Mcp.McpResourceValidator.GetEffectCatalog());
ValidateJsonResource("calor://tags", Calor.Compiler.Mcp.McpResourceValidator.GetTagCatalog());
ValidateJsonResource("calor://id-prefixes", Calor.Compiler.Mcp.McpResourceValidator.GetIdPrefixCatalog());
ValidateJsonResource("calor://workflows", Calor.Compiler.Mcp.McpResourceValidator.GetWorkflows());
Console.WriteLine("All MCP resources valid.");
Console.WriteLine();

Console.WriteLine($"Tasks dir: {tasksDir}");
Console.WriteLine($"Runs per task: {runs}");
Console.WriteLine();

var taskDirs = Directory.GetDirectories(tasksDir).OrderBy(d => d).ToArray();
Console.WriteLine($"Found {taskDirs.Length} tasks");
Console.WriteLine();

var allResults = new List<TaskResult>();

foreach (var taskDir in taskDirs)
{
    var taskJsonPath = Path.Combine(taskDir, "task.json");
    if (!File.Exists(taskJsonPath))
    {
        Console.WriteLine($"SKIP: {Path.GetFileName(taskDir)} (no task.json)");
        continue;
    }

    var taskSpec = JsonSerializer.Deserialize<TaskSpec>(File.ReadAllText(taskJsonPath));
    if (taskSpec == null) continue;

    var taskName = Path.GetFileName(taskDir);
    var inputPath = Path.Combine(taskDir, taskSpec.Input ?? "input.calr");
    var expectedPath = Path.Combine(taskDir, taskSpec.Expected ?? "expected.calr");

    // Skip green-field tasks (no input file — agent writes from scratch)
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"SKIP: {taskName} (green-field — requires agent)");
        allResults.Add(new TaskResult
        {
            TaskId = taskSpec.Id ?? taskName,
            Category = taskSpec.Category ?? "unknown",
            Status = "skipped",
            Reason = "green-field task requires agent"
        });
        continue;
    }

    // Skip conversion tasks (input is C#, not Calor — needs calor_convert first)
    if (inputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"SKIP: {taskName} (conversion — requires calor_convert)");
        allResults.Add(new TaskResult
        {
            TaskId = taskSpec.Id ?? taskName,
            Category = taskSpec.Category ?? "unknown",
            Status = "skipped",
            Reason = "conversion task requires calor_convert pipeline"
        });
        continue;
    }

    var source = File.ReadAllText(inputPath);

    // Run baseline (no autoFix)
    var baselineMetrics = new List<RunMetrics>();
    for (var run = 0; run < runs; run++)
        baselineMetrics.Add(RunCompileFixLoop(source, taskSpec, useAutoFix: false));

    // Run with autoFix
    var autoFixMetrics = new List<RunMetrics>();
    for (var run = 0; run < runs; run++)
        autoFixMetrics.Add(RunCompileFixLoop(source, taskSpec, useAutoFix: true));

    // Compute median metrics for both
    var baselineSuccess = baselineMetrics.Count(m => m.Success) / (double)baselineMetrics.Count * 100;
    var autoFixSuccess = autoFixMetrics.Count(m => m.Success) / (double)autoFixMetrics.Count * 100;
    var medianRoundTrips = Median(autoFixMetrics.Select(m => (double)m.RoundTrips));
    var medianDiagnosticsBefore = Median(baselineMetrics.Select(m => (double)m.DiagnosticsBefore));
    var medianDiagnosticsAfter = Median(autoFixMetrics.Select(m => (double)m.DiagnosticsAfter));
    var medianTimeMs = Median(autoFixMetrics.Select(m => m.TotalTimeMs));
    var successRate = autoFixSuccess;

    // Check against expected output if available
    var matchesExpected = false;
    if (File.Exists(expectedPath) && autoFixMetrics.Count > 0 && autoFixMetrics[0].FinalSource != null)
    {
        var expected = File.ReadAllText(expectedPath).Trim();
        matchesExpected = autoFixMetrics[0].FinalSource!.Trim() == expected;
    }

    var result = new TaskResult
    {
        TaskId = taskSpec.Id ?? taskName,
        Category = taskSpec.Category ?? "unknown",
        Status = successRate >= 100 ? "pass" : successRate > 0 ? "partial" : "fail",
        MedianRoundTrips = medianRoundTrips,
        MedianDiagnosticsBefore = (int)medianDiagnosticsBefore,
        MedianDiagnosticsAfter = (int)medianDiagnosticsAfter,
        MedianTimeMs = medianTimeMs,
        SuccessRate = successRate,
        MatchesExpected = matchesExpected,
        InputSizeBytes = source.Length,
    };
    allResults.Add(result);

    var statusIcon = result.Status == "pass" ? "OK" : result.Status == "partial" ? "~" : "FAIL";
    var improvement = baselineSuccess < autoFixSuccess ? " (IMPROVED)" : "";
    Console.WriteLine($"[{statusIcon}] {taskName}: baseline={baselineSuccess:F0}% → autoFix={autoFixSuccess:F0}%{improvement}, " +
        $"{medianRoundTrips:F0} round-trips, {medianDiagnosticsBefore:F0}→{medianDiagnosticsAfter:F0} diags, " +
        $"{medianTimeMs:F1}ms");
}

// Summary
Console.WriteLine();
Console.WriteLine("Summary");
Console.WriteLine("=======");

var byCategory = allResults.Where(r => r.Status != "skipped")
    .GroupBy(r => r.Category)
    .OrderBy(g => g.Key);

foreach (var group in byCategory)
{
    var tasks = group.ToList();
    var avgRoundTrips = tasks.Average(t => t.MedianRoundTrips);
    var avgSuccess = tasks.Average(t => t.SuccessRate);
    Console.WriteLine($"  {group.Key}: {tasks.Count} tasks, " +
        $"avg {avgRoundTrips:F1} round-trips, {avgSuccess:F0}% success");
}

var totalTasks = allResults.Count(r => r.Status != "skipped");
var totalPass = allResults.Count(r => r.Status == "pass");
var totalMedianRT = allResults.Where(r => r.Status != "skipped")
    .Select(r => r.MedianRoundTrips).DefaultIfEmpty(0).Average();

Console.WriteLine();
Console.WriteLine($"Total: {totalPass}/{totalTasks} pass, avg {totalMedianRT:F1} round-trips/task");

// Write results to JSON for CI consumption
var resultsPath = Path.Combine(tasksDir, "..", "results.json");
var resultsJson = JsonSerializer.Serialize(allResults, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(resultsPath, resultsJson);
Console.WriteLine($"\nResults written to: {resultsPath}");

return 0;

// ── Compile-fix loop ──────────────────────────────────────────────

static RunMetrics RunCompileFixLoop(string source, TaskSpec spec, bool useAutoFix = false)
{
    var sw = Stopwatch.StartNew();
    var currentSource = source;
    var totalRoundTrips = 0;

    // Initial compile
    var result = CompileCalorSource(currentSource);
    totalRoundTrips++;
    var diagnosticsBefore = result.Diagnostics.Count(d => d.IsError);

    if (useAutoFix && result.HasErrors)
    {
        // AutoFix loop: apply fixes from DiagnosticsWithFixes, recompile, repeat
        const int maxPasses = 3;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            var fixes = result.Diagnostics.DiagnosticsWithFixes;
            if (fixes.Count == 0) break;

            // Apply all fixes in reverse line order
            var allEdits = fixes
                .SelectMany(d => d.Fix.Edits)
                .OrderByDescending(e => e.StartLine)
                .ThenByDescending(e => e.StartColumn)
                .ToList();

            if (allEdits.Count == 0) break;

            var lines = currentSource.Split('\n');
            foreach (var edit in allEdits)
            {
                var startLine = edit.StartLine - 1;
                var startCol = edit.StartColumn - 1;
                var endLine = edit.EndLine - 1;
                var endCol = edit.EndColumn - 1;
                if (startLine < 0 || startLine >= lines.Length) continue;
                if (endLine < 0 || endLine >= lines.Length) endLine = startLine;

                var before = startCol >= 0 && startCol <= lines[startLine].Length
                    ? lines[startLine][..startCol] : lines[startLine];
                var after = endCol >= 0 && endCol <= lines[endLine].Length
                    ? lines[endLine][endCol..] : "";

                var newContent = before + edit.NewText + after;
                var newLines = newContent.Split('\n');
                var lineList = lines.ToList();
                lineList.RemoveRange(startLine, endLine - startLine + 1);
                lineList.InsertRange(startLine, newLines);
                lines = lineList.ToArray();
            }

            var newSource = string.Join('\n', lines);
            if (newSource == currentSource) break; // No changes — bail

            currentSource = newSource;
            result = CompileCalorSource(currentSource);
            totalRoundTrips++;

            if (!result.HasErrors) break;
        }
    }

    sw.Stop();

    return new RunMetrics
    {
        RoundTrips = totalRoundTrips,
        DiagnosticsBefore = diagnosticsBefore,
        DiagnosticsAfter = result.Diagnostics.Count(d => d.IsError),
        TotalTimeMs = sw.Elapsed.TotalMilliseconds,
        Success = !result.HasErrors,
        FinalSource = result.HasErrors ? null : currentSource,
        DiagnosticCodes = result.Diagnostics
            .Where(d => d.IsError)
            .Select(d => d.Code)
            .Distinct()
            .ToList()
    };
}

static void ValidateJsonResource(string name, string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        Console.WriteLine($"  [OK] {name} ({json.Length} bytes, valid JSON)");
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
        Environment.Exit(1);
    }
}

static CompilationResult CompileCalorSource(string source)
{
    return Calor.Compiler.Program.Compile(source, "benchmark.calr", new CompilationOptions
    {
        EnforceEffects = true,
        UnknownCallPolicy = Calor.Compiler.Effects.UnknownCallPolicy.Strict
    });
}

static double Median(IEnumerable<double> values)
{
    var sorted = values.OrderBy(v => v).ToArray();
    if (sorted.Length == 0) return 0;
    return sorted[sorted.Length / 2];
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "Calor.sln")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return Directory.GetCurrentDirectory();
}

// ── Data types ────────────────────────────────────────────────────

class TaskSpec
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("targetPhase")] public string? TargetPhase { get; set; }
    [JsonPropertyName("input")] public string? Input { get; set; }
    [JsonPropertyName("expected")] public string? Expected { get; set; }
    [JsonPropertyName("expectedDiagnostics")] public List<string>? ExpectedDiagnostics { get; set; }
    [JsonPropertyName("successCriteria")] public string? SuccessCriteria { get; set; }
}

class RunMetrics
{
    public int RoundTrips { get; set; }
    public int DiagnosticsBefore { get; set; }
    public int DiagnosticsAfter { get; set; }
    public double TotalTimeMs { get; set; }
    public bool Success { get; set; }
    public string? FinalSource { get; set; }
    public List<string> DiagnosticCodes { get; set; } = [];
}

class TaskResult
{
    [JsonPropertyName("taskId")] public string TaskId { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("medianRoundTrips")] public double MedianRoundTrips { get; set; }
    [JsonPropertyName("medianDiagnosticsBefore")] public int MedianDiagnosticsBefore { get; set; }
    [JsonPropertyName("medianDiagnosticsAfter")] public int MedianDiagnosticsAfter { get; set; }
    [JsonPropertyName("medianTimeMs")] public double MedianTimeMs { get; set; }
    [JsonPropertyName("successRate")] public double SuccessRate { get; set; }
    [JsonPropertyName("matchesExpected")] public bool MatchesExpected { get; set; }
    [JsonPropertyName("inputSizeBytes")] public int InputSizeBytes { get; set; }
}
