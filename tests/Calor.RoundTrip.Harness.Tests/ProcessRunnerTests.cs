using System.Reflection;
using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Regression tests for <see cref="ProcessRunner.ResolveDotnetRoot"/> — the DOTNET_ROOT
/// resolution used by every spawned dotnet invocation. The bare-name case
/// (<c>--dotnet dotnet</c>) is the documented and CI-invoked form, so it must resolve
/// via PATH and MUST NOT throw on a non-existent path.
/// </summary>
public class ProcessRunnerTests
{
    private static string? ResolveDotnetRoot(string fileName)
    {
        var method = typeof(ProcessRunner).GetMethod(
            "ResolveDotnetRoot",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, [fileName]);
    }

    [Fact]
    public void BareDotnetName_DoesNotThrow_AndResolvesViaPath()
    {
        // The documented/CI form: `--dotnet dotnet`. Must not throw (the previous
        // FileInfo(...).ResolveLinkTarget crash on a bare name is what this guards).
        var root = ResolveDotnetRoot("dotnet");

        // On any machine where the harness can actually run, `dotnet` is on PATH, so we
        // expect a real directory back. If a stripped environment lacks it, null is the
        // graceful (non-throwing) fallback — never an exception.
        if (root != null)
            Assert.True(Directory.Exists(root), $"resolved DOTNET_ROOT should exist: {root}");
    }

    [Fact]
    public void BareNameNotOnPath_ReturnsNull_WithoutThrowing()
    {
        // Ends with "dotnet" but is not a real executable — must resolve to null, not throw.
        var root = ResolveDotnetRoot("zzz-not-a-real-executable-dotnet");
        Assert.Null(root);
    }

    [Fact]
    public void NonDotnetName_ReturnsNull()
    {
        Assert.Null(ResolveDotnetRoot("git"));
        Assert.Null(ResolveDotnetRoot("/usr/bin/env"));
    }

    [Fact]
    public void AbsoluteNonExistentDotnetPath_ReturnsNull_WithoutThrowing()
    {
        var root = ResolveDotnetRoot("/no/such/dir/dotnet");
        Assert.Null(root);
    }

    [Fact]
    public void ExistingDotnetAbsolutePath_ResolvesToADirectory()
    {
        // If we can locate a real dotnet via PATH, feeding its absolute path back in
        // must resolve to an existing directory (following any symlink).
        var viaPath = ResolveDotnetRoot("dotnet");
        if (viaPath == null)
            return; // no dotnet on PATH in this environment — nothing to assert
        var exe = Path.Combine(viaPath, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (!File.Exists(exe))
            return;
        var root = ResolveDotnetRoot(exe);
        Assert.NotNull(root);
        Assert.True(Directory.Exists(root));
    }
}
