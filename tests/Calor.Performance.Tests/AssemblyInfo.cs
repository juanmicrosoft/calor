using Xunit;

// Performance tests measure time and memory, and both measurements are only
// meaningful in a quiet process. xUnit parallelises across test CLASSES by
// default, so a second class in this assembly runs concurrently with the first
// and its allocations land between another test's before/after memory samples.
//
// That is not hypothetical: adding IndexPerformanceTests made
// VerificationPerformanceTests.Memory_SmallModule_Under10MB fail, while it
// passed when run alone. The measurement was contaminated, not the code
// regressed.
//
// Disabling parallelisation assembly-wide is the fix that keeps every
// measurement here honest, rather than making one test tolerant of noise it
// should never have seen.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
