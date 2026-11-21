using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>Tests that change CALOR_HOME or HOME run alone, so they cannot move the root under other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UserManifestEnvironmentCollection
{
    public const string Name = "UserManifestEnvironment";
}

/// <summary>
/// #1564: the converter's user-level manifest root is injectable through
/// <see cref="ConversionOptions.UserManifestRoot"/>, and otherwise resolved from CALOR_HOME, then HOME,
/// when each conversion starts. Only temporary directories are used; the real home is never touched.
/// </summary>
[Collection(UserManifestEnvironmentCollection.Name)]
public sealed class UserManifestRootTests : IDisposable
{
    // Math.Abs is pure in the built-in manifests; the user manifest below says it writes to the console.
    private const string Source = """
        public static class Sample
        {
            public static int Magnitude(int x) => System.Math.Abs(x);
        }
        """;

    private const string ConsoleWriteManifest = """
        {
          "version": "1.0",
          "mappings": [{ "type": "System.Math", "methods": { "Abs": ["cw"] } }]
        }
        """;

    private readonly string _temp = Directory.CreateTempSubdirectory("calor-1564-").FullName;
    private readonly string? _calorHome = Environment.GetEnvironmentVariable("CALOR_HOME");
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _userProfile = Environment.GetEnvironmentVariable("USERPROFILE");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CALOR_HOME", _calorHome);
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("USERPROFILE", _userProfile);
        try { Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    /// <summary>Creates <c>{dir}/manifests</c> holding the console-write manifest and returns <c>dir</c>.</summary>
    private string CalorHomeWithManifest(string name)
    {
        var calorHome = Path.Combine(_temp, name);
        Directory.CreateDirectory(Path.Combine(calorHome, "manifests"));
        File.WriteAllText(Path.Combine(calorHome, "manifests", "math.calor-effects.json"), ConsoleWriteManifest);
        return calorHome;
    }

    private string EmptyDirectory(string name) => Directory.CreateDirectory(Path.Combine(_temp, name)).FullName;

    private static bool WritesConsole(string row) => row.Contains("cw", StringComparison.Ordinal);

    [Fact]
    public void TestAssembly_PinsCalorHome_ToAnEmptyDirectory()
    {
        Assert.Equal(global::Calor.Tests.Shared.HermeticCalorHome.Root, Environment.GetEnvironmentVariable("CALOR_HOME"));
        Assert.Equal(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot, ManifestLoader.GetUserManifestsDirectory());
        Assert.False(Directory.Exists(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot)
            && Directory.EnumerateFileSystemEntries(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot).Any());
    }
}
