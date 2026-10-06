using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Test classes that mutate process-wide state (current directory, Console streams) join this
/// collection. Parallelization is disabled for it, so xUnit runs it after all parallel
/// collections finish and never alongside another test class.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessGlobalStateCollection
{
    public const string Name = "Process-global state";
}
