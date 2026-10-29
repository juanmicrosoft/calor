using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CliTestHarnessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("calor-cli-resolution-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void NoContentMatch_PrefersHostConfiguration(string configuration)
    {
        var debug = WriteCompiler("Debug", "debug compiler");
        var release = WriteCompiler("Release", "release compiler");
        var loaded = Path.Combine(Directory.CreateDirectory(HostDirectory(configuration)).FullName, "calor.dll");
        File.WriteAllText(loaded, "instrumented compiler");

        var resolved = CliTestHarness.ResolveCalorDll(
            _root, HostDirectory(configuration), loaded, profilerAttached: true);

        Assert.Equal(configuration == "Debug" ? debug : release, resolved);
    }

    [Fact]
    public void MatchingContent_TakesPrecedenceOverHostConfiguration()
    {
        WriteCompiler("Debug", "stale compiler");
        var release = WriteCompiler("Release", "loaded compiler");
        var loaded = Path.Combine(Directory.CreateDirectory(HostDirectory("Debug")).FullName, "calor.dll");
        File.WriteAllText(loaded, "loaded compiler");

        var resolved = CliTestHarness.ResolveCalorDll(
            _root, HostDirectory("Debug"), loaded, profilerAttached: false);

        Assert.Equal(release, resolved);
    }

    [Theory]
    [InlineData("Debug", "Release")]
    [InlineData("Release", "Debug")]
    public void MissingHostConfiguration_FallsBackToOtherBuild(string host, string other)
    {
        var compiler = WriteCompiler(other, "fallback compiler");

        var resolved = CliTestHarness.ResolveCalorDll(
            _root, HostDirectory(host), Path.Combine(_root, "absent.dll"), profilerAttached: false);

        Assert.Equal(compiler, resolved);
    }

    [Fact]
    public void MissingBothBuilds_ThrowsWithBuildGuidance()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CliTestHarness.ResolveCalorDll(
                _root, HostDirectory("Debug"), Path.Combine(_root, "absent.dll"), profilerAttached: false));

        Assert.Contains("calor.dll not found", error.Message);
        Assert.Contains("build the compiler first", error.Message);
    }

    private string HostDirectory(string configuration)
        => Path.Combine(_root, "tests", "Calor.Compiler.Tests", "bin", configuration, "net10.0");

    private string WriteCompiler(string configuration, string content)
    {
        var directory = Path.Combine(_root, "src", "Calor.Compiler", "bin", configuration, "net10.0");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "calor.dll");
        File.WriteAllText(path, content);
        return path;
    }
}
