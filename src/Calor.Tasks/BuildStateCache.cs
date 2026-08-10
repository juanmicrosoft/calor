// The incremental build-state cache is shared with the CLI compile path and now
// lives in Calor.Compiler (Calor.Compiler.Incremental.BuildStateCache). These
// aliases preserve the historical unqualified names inside Calor.Tasks.
global using BuildState = Calor.Compiler.Incremental.BuildState;
global using BuildFileEntry = Calor.Compiler.Incremental.BuildFileEntry;
global using CachedDiagnostic = Calor.Compiler.Incremental.CachedDiagnostic;
global using BuildStateCache = Calor.Compiler.Incremental.BuildStateCache;
global using CacheLoadStatus = Calor.Compiler.Incremental.CacheLoadStatus;
global using GlobalCacheInvalidationReason =
    Calor.Compiler.Incremental.GlobalCacheInvalidationReason;
