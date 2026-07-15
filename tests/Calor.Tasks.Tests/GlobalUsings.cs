// The build-state cache moved to Calor.Compiler (shared with the CLI compile
// path); these aliases keep the historical unqualified names in the tests.
global using BuildState = Calor.Compiler.Incremental.BuildState;
global using BuildFileEntry = Calor.Compiler.Incremental.BuildFileEntry;
global using BuildStateCache = Calor.Compiler.Incremental.BuildStateCache;
global using BuildStateJsonContext = Calor.Compiler.Incremental.BuildStateJsonContext;
