namespace Calor.Tasks.Tests;

/// <summary>
/// The one place these tests locate the repository from the test binary's directory.
/// <see cref="EffectDefaultEquivalenceTests"/> pins Sdk.targets' seeded defaults against
/// <c>CompilationOptions</c>, and <see cref="PermissiveEffectsTests"/> additionally builds
/// a generated project against the real targets; two copies of the walk-up loop drift.
/// </summary>
internal static class RepoPaths
{
    /// <summary>Repository root: the nearest ancestor holding src/Calor.Sdk/Sdk/Sdk.targets.</summary>
    internal static string Root { get; } = FindRoot();

    internal static string SdkFile(string name)
        => Path.Combine(Root, "src", "Calor.Sdk", "Sdk", name);

    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "src", "Calor.Sdk", "Sdk", "Sdk.targets")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "repository root (src/Calor.Sdk/Sdk/Sdk.targets) not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Test classes that touch process-wide or repository state join this collection. xUnit runs
/// it alone, after every parallel class: <see cref="BuildStateCacheTests"/> sets
/// <c>CALOR_NO_TYPE_CHECK</c>, and the MSBuild classes build temporary projects that reference
/// this repository's Calor.Runtime and Calor.Sdk, whose bin and obj directories two concurrent
/// builds would both write.
/// </summary>
[Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection
{
    public const string Name = "Process and repository state";
}
