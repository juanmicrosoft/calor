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

    private static string ConvertRow(ConversionOptions? options = null)
    {
        var result = new CSharpToCalorConverter(options).Convert(Source, "Sample.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        var row = result.CalorSource!.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("§E{", StringComparison.Ordinal));
        return row ?? "<no row>";
    }

    private static bool WritesConsole(string row) => row.Contains("cw", StringComparison.Ordinal);

    [Fact]
    public void InjectedRoot_IsHonoured()
    {
        var injected = Path.Combine(CalorHomeWithManifest("injected"), "manifests");
        Environment.SetEnvironmentVariable("CALOR_HOME", EmptyDirectory("env-empty"));

        Assert.True(WritesConsole(ConvertRow(new ConversionOptions { UserManifestRoot = injected })),
            "the injected root's manifest should widen Math.Abs to console write");
        Assert.False(WritesConsole(ConvertRow(new ConversionOptions { UserManifestRoot = EmptyDirectory("injected-empty") })));
        Assert.False(WritesConsole(ConvertRow(new ConversionOptions { UserManifestRoot = "" })));

        // An injected root wins over the environment in both directions.
        Environment.SetEnvironmentVariable("CALOR_HOME", CalorHomeWithManifest("env-full"));
        Assert.False(WritesConsole(ConvertRow(new ConversionOptions { UserManifestRoot = EmptyDirectory("injected-empty-2") })));
    }

    [Fact]
    public void ChangingCalorHome_BetweenConversions_ChangesTheRoot()
    {
        var converter = new CSharpToCalorConverter();
        Environment.SetEnvironmentVariable("CALOR_HOME", CalorHomeWithManifest("first"));
        Assert.Equal(Path.Combine(_temp, "first", "manifests"), converter.ResolveUserManifestRoot());
        Assert.True(WritesConsole(ConvertRow()));

        Environment.SetEnvironmentVariable("CALOR_HOME", EmptyDirectory("second"));
        Assert.Equal(Path.Combine(_temp, "second", "manifests"), converter.ResolveUserManifestRoot());
        Assert.False(WritesConsole(ConvertRow()), "the second conversion must not reuse the first root's manifests");

        // And back: the first root's pooled resolver is still keyed by that root.
        Environment.SetEnvironmentVariable("CALOR_HOME", Path.Combine(_temp, "first"));
        Assert.True(WritesConsole(ConvertRow()));
    }

    [Fact]
    public void WithoutCalorHome_TheRootFollowsHome()
    {
        // Unix only: on Windows the home comes from USERPROFILE, which UserHome already reads per call.
        if (OperatingSystem.IsWindows())
            return;
        Environment.SetEnvironmentVariable("CALOR_HOME", null);
        var home = EmptyDirectory("home");
        Environment.SetEnvironmentVariable("HOME", home);
        Assert.Equal(Path.Combine(home, ".calor", "manifests"), ManifestLoader.GetUserManifestsDirectory());
        Assert.False(WritesConsole(ConvertRow()));

        var otherHome = EmptyDirectory("home2");
        Directory.CreateDirectory(Path.Combine(otherHome, ".calor", "manifests"));
        File.WriteAllText(Path.Combine(otherHome, ".calor", "manifests", "math.calor-effects.json"), ConsoleWriteManifest);
        Environment.SetEnvironmentVariable("HOME", otherHome);
        Assert.Equal(Path.Combine(otherHome, ".calor", "manifests"), ManifestLoader.GetUserManifestsDirectory());
        Assert.True(WritesConsole(ConvertRow()));
    }

    /// <summary>
    /// The case the T1 shared controls used to fail closed on: a user manifest exists in the home the
    /// process started with. A harness that injects an empty root no longer sees it.
    /// </summary>
    [Fact]
    public void UserManifestInHome_IsNotPickedUp_ByAHarnessThatInjectsAnEmptyRoot()
    {
        // Before #1564 the first conversion fixed ~/.calor/manifests for the process. Simulate a developer
        // machine: CALOR_HOME unset and HOME holding a manifest that changes the row.
        Environment.SetEnvironmentVariable("CALOR_HOME", null);
        var home = EmptyDirectory("developer-home");
        Directory.CreateDirectory(Path.Combine(home, ".calor", "manifests"));
        File.WriteAllText(Path.Combine(home, ".calor", "manifests", "math.calor-effects.json"), ConsoleWriteManifest);
        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("USERPROFILE", home);

        // The default library call still reads the user's manifest: CLI and library behavior is unchanged.
        Assert.True(WritesConsole(ConvertRow()));

        // The hermetic harness configuration does not.
        Assert.False(WritesConsole(ConvertRow(new ConversionOptions { UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot })));
    }

    [Fact]
    public void TestAssembly_PinsCalorHome_ToAnEmptyDirectory()
    {
        Assert.Equal(global::Calor.Tests.Shared.HermeticCalorHome.Root, Environment.GetEnvironmentVariable("CALOR_HOME"));
        Assert.Equal(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot, ManifestLoader.GetUserManifestsDirectory());
        Assert.False(Directory.Exists(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot)
            && Directory.EnumerateFileSystemEntries(global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot).Any());
    }
}
