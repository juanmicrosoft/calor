// The build-state cache moved to Calor.Compiler (shared with the CLI compile
// path); these aliases keep the historical unqualified names in the tests.
global using BuildState = Calor.Compiler.Incremental.BuildState;
global using BuildFileEntry = Calor.Compiler.Incremental.BuildFileEntry;
global using BuildStateCache = Calor.Compiler.Incremental.BuildStateCache;
global using BuildStateJsonContext = Calor.Compiler.Incremental.BuildStateJsonContext;
global using CacheLoadStatus = Calor.Compiler.Incremental.CacheLoadStatus;

// Calor.Tasks.Tests manipulates CALOR_NO_TYPE_CHECK to pin the MSBuild task's canonical inputs
// against the environment escape hatch. Environment variables are process-wide, so in-assembly
// parallelism has to be off for that to be sound. It costs nothing here (under a
// second for 66 tests) and cannot affect other test projects, which run in their own testhost processes.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
