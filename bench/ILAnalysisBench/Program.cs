using System.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.IL;

const int Iterations = 10;

Console.WriteLine("IL Analysis Validation Benchmark");
Console.WriteLine("================================");
Console.WriteLine();

// Find test assemblies from the sibling test project
var repoRoot = FindRepoRoot();
var dataAccessDll = Path.Combine(repoRoot,
    "tests/Calor.ILAnalysis.Tests/TestAssemblies/TestAssembly.DataAccess/bin/Release/net10.0/TestAssembly.DataAccess.dll");
var scenariosDll = Path.Combine(repoRoot,
    "tests/Calor.ILAnalysis.Tests/TestAssemblies/TestAssembly.Scenarios/bin/Release/net10.0/TestAssembly.Scenarios.dll");

if (!File.Exists(dataAccessDll) || !File.Exists(scenariosDll))
{
    Console.WriteLine("ERROR: Test assemblies not found. Build them first:");
    Console.WriteLine("  dotnet build tests/Calor.ILAnalysis.Tests/TestAssemblies/TestAssembly.DataAccess -c Release");
    Console.WriteLine("  dotnet build tests/Calor.ILAnalysis.Tests/TestAssemblies/TestAssembly.Scenarios -c Release");
    return 1;
}

var assemblyPaths = new[] { dataAccessDll, scenariosDll };
var callSites = new (string Type, string Method)[]
{
    ("TestAssembly.DataAccess.UserService", "CreateUser"),
    ("TestAssembly.DataAccess.UserService", "GetUser"),
    ("TestAssembly.DataAccess.MathHelper", "Add"),
    ("TestAssembly.Scenarios.AsyncService", "SaveAsync"),
    ("TestAssembly.Scenarios.DeepChain", "Level0"),
    ("TestAssembly.Scenarios.CircularCalls", "A"),
    ("TestAssembly.Scenarios.DelegateService", "ProcessWithDelegate"),
    ("TestAssembly.Scenarios.OverloadService", "Process"),
};

Console.WriteLine($"Assemblies: {assemblyPaths.Length}");
Console.WriteLine($"Call sites: {callSites.Length}");
Console.WriteLine();

// Benchmark: Assembly index construction
var indexTimings = new List<double>();
for (var i = 0; i < Iterations; i++)
{
    var sw = Stopwatch.StartNew();
    using var index = new AssemblyIndex(assemblyPaths);
    sw.Stop();
    indexTimings.Add(sw.Elapsed.TotalMilliseconds);
}
indexTimings.Sort();
Console.WriteLine($"Assembly index construction: median={indexTimings[Iterations / 2]:F1}ms  p95={indexTimings[(int)(Iterations * 0.95)]:F1}ms");

// Benchmark: Full analysis (index + propagation)
var analysisTimings = new List<double>();
var cachedMethods = 0;
var resolvedCount = 0;
var pureCount = 0;
var incompleteCount = 0;

for (var i = 0; i < Iterations; i++)
{
    var resolver = new EffectResolver();
    resolver.Initialize();

    var sw = Stopwatch.StartNew();
    using var analyzer = new ILEffectAnalyzer(assemblyPaths, resolver);
    analyzer.AnalyzeFromCallSites(callSites);
    sw.Stop();

    analysisTimings.Add(sw.Elapsed.TotalMilliseconds);

    if (i == 0)
    {
        cachedMethods = analyzer.CachedMethodCount;

        // Count resolution outcomes
        foreach (var (type, method) in callSites)
        {
            var result = analyzer.TryResolve(type, method);
            if (result == null)
                incompleteCount++;
            else if (result.Status == EffectResolutionStatus.PureExplicit)
                pureCount++;
            else if (result.Status == EffectResolutionStatus.Resolved)
                resolvedCount++;
        }
    }
}
analysisTimings.Sort();

Console.WriteLine($"Full analysis (index + propagation): median={analysisTimings[Iterations / 2]:F1}ms  p95={analysisTimings[(int)(Iterations * 0.95)]:F1}ms");
Console.WriteLine();

// Resolution summary
Console.WriteLine("Resolution Summary");
Console.WriteLine("==================");
Console.WriteLine($"  Call sites analyzed:  {callSites.Length}");
Console.WriteLine($"  Methods in cache:     {cachedMethods}");
Console.WriteLine($"  Resolved (effects):   {resolvedCount}");
Console.WriteLine($"  Resolved (pure):      {pureCount}");
Console.WriteLine($"  Incomplete/Unknown:   {incompleteCount}");
Console.WriteLine();

// Per-call-site resolution details
Console.WriteLine("Per-Call-Site Results");
Console.WriteLine("====================");
{
    var resolver = new EffectResolver();
    resolver.Initialize();
    using var analyzer = new ILEffectAnalyzer(assemblyPaths, resolver);
    analyzer.AnalyzeFromCallSites(callSites);

    foreach (var (type, method) in callSites)
    {
        var result = analyzer.TryResolve(type, method);
        var status = result == null ? "Incomplete"
            : result.Status == EffectResolutionStatus.PureExplicit ? "Pure"
            : $"Resolved: {result.Effects}";
        Console.WriteLine($"  {type}.{method}() → {status}");
    }
}

Console.WriteLine();

// Acceptance criteria
Console.WriteLine("Acceptance Criteria");
Console.WriteLine("===================");
var analysisMedian = analysisTimings[Iterations / 2];
var indexMedian = indexTimings[Iterations / 2];

Check("Assembly index construction < 250ms", indexMedian < 250);
Check("Full analysis < 2000ms", analysisMedian < 2000);
Check("Cached methods > 0 (analysis ran)", cachedMethods > 0);
Check("No false purity (resolved + pure + incomplete = total)", resolvedCount + pureCount + incompleteCount == callSites.Length);

return 0;

static void Check(string description, bool passed)
{
    Console.WriteLine($"  [{(passed ? "PASS" : "FAIL")}] {description}");
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
    // Fallback: relative to working directory
    return Directory.GetCurrentDirectory();
}
