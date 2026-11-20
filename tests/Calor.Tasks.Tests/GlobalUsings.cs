// The build-state cache moved to Calor.Compiler (shared with the CLI compile
// path); these aliases keep the historical unqualified names in the tests.
global using BuildState = Calor.Compiler.Incremental.BuildState;
global using BuildFileEntry = Calor.Compiler.Incremental.BuildFileEntry;
global using BuildStateCache = Calor.Compiler.Incremental.BuildStateCache;
global using BuildStateJsonContext = Calor.Compiler.Incremental.BuildStateJsonContext;
global using CacheLoadStatus = Calor.Compiler.Incremental.CacheLoadStatus;

// 0.30 (#1730): test classes run in parallel. Most of this project's time is CLI and build
// processes started one at a time (the binding-routing CLI matrix, edit-script identity, the
// W1 transcripts, SDK surface agreement), which left CI's other cores idle and the job at its
// 15-minute limit on main as well. Classes that touch process-wide or repository state join
// ProcessStateCollection (Calor.Tasks.Tests.RepoPaths.cs), which xUnit runs alone, after the
// parallel classes: BuildStateCacheTests sets CALOR_NO_TYPE_CHECK, and the MSBuild classes
// build temporary projects that reference this repository's Calor.Runtime and Calor.Sdk, so two
// of them at once would write the same bin and obj directories.
