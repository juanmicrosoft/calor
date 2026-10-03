using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Tests.Shared;

/// <summary>
/// 0.24 G1 (#1420): every registered test host must load the bootstrapped Z3,
/// or fail. Z3-backed tests elsewhere use <c>Skip.IfNot(Z3ContextFactory.IsAvailable)</c>,
/// so a host that never received the natives used to go green with hundreds of
/// skips. These facts never skip: on a host where Z3 is missing, wrong, or
/// unsupported, the run fails.
///
/// The expected RID set, native names, and pins come from
/// <c>eng/z3-consumers.json</c> and <c>.github/z3-binaries-4.15.7.sha256</c>,
/// so this file carries no second copy of either. It is linked into every
/// project in <c>eng/test-manifest.json</c>; <c>scripts/test_z3_hermetic.py</c>
/// fails if a project is missing it.
/// </summary>
public sealed class Z3ConsumerGuardTests
{
    [Fact]
    public void HostRidIsASupportedZ3Rid()
    {
        var registry = Z3Registry.Load();
        var rid = Z3Registry.HostRid();

        Assert.True(
            registry.Supported.ContainsKey(rid),
            $"Test host RID {rid} is not in the supported Z3 RID set "
            + $"({string.Join(", ", registry.Supported.Keys)}) in eng/z3-consumers.json. "
            + "Z3 verification cannot run here, so this host fails instead of skipping.");

        var expected = Environment.GetEnvironmentVariable(registry.ExpectedRidVariable);
        if (!string.IsNullOrEmpty(expected))
            Assert.Equal(expected, rid);
    }

    [Fact]
    public void Z3LoadsWithThePinnedVersion()
    {
        var registry = Z3Registry.Load();

        Assert.True(
            Z3ContextFactory.IsAvailable,
            $"Z3 did not load in this test host ({AppContext.BaseDirectory}). Run "
            + "src/Calor.Compiler/scripts/download-z3.sh (or .ps1) before building; "
            + "set CALOR_Z3_DEBUG=1 to print the load failure.");

        var version = Z3ContextFactory.GetZ3Version();
        Assert.NotNull(version);
        // Microsoft.Z3.Version.FullVersion reads "Z3 4.15.7.0" from most upstream
        // natives and "4.15.7.0 <git hash>" from the arm64-win one.
        Assert.Matches($@"(^|\s){Regex.Escape(registry.Version)}(\.|\s|$)", version);
    }

    [Fact]
    public void HostNativeInOutputMatchesItsPin()
    {
        var registry = Z3Registry.Load();
        var rid = Z3Registry.HostRid();
        Assert.True(registry.Supported.TryGetValue(rid, out var entry), $"unsupported RID {rid}");

        var (expectedHash, expectedSize) = registry.Pins[entry.Asset];
        var baseDir = AppContext.BaseDirectory;
        var ridCopy = Path.Combine(baseDir, "runtimes", rid, "native", entry.Native);
        Assert.True(
            File.Exists(ridCopy),
            $"{ridCopy} is missing: the bootstrapped native did not propagate to this test host.");

        // The output-root copy is probed first, so it must be the same bytes.
        foreach (var path in new[] { ridCopy, Path.Combine(baseDir, entry.Native) })
        {
            if (!File.Exists(path))
                continue;
            Assert.Equal(expectedSize, new FileInfo(path).Length);
            Assert.Equal(expectedHash, Z3Registry.Sha256(path));
        }
    }

    [Fact]
    public void ManagedWrapperInOutputMatchesItsPin()
    {
        var registry = Z3Registry.Load();
        var path = Path.Combine(AppContext.BaseDirectory, "Microsoft.Z3.dll");
        Assert.True(File.Exists(path), $"{path} is missing from this test host.");

        var (expectedHash, expectedSize) = registry.Pins[registry.ManagedAsset];
        Assert.Equal(expectedSize, new FileInfo(path).Length);
        Assert.Equal(expectedHash, Z3Registry.Sha256(path));
    }

    private sealed record RidEntry(string Asset, string Native);

    private sealed record Z3Registry(
        string Version,
        string ManagedAsset,
        string ExpectedRidVariable,
        IReadOnlyDictionary<string, RidEntry> Supported,
        IReadOnlyDictionary<string, (string Hash, long Size)> Pins)
    {
        public static Z3Registry Load()
        {
            var root = FindRepoRoot();
            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "eng", "z3-consumers.json")));
            var json = document.RootElement;

            var supported = new Dictionary<string, RidEntry>(StringComparer.Ordinal);
            foreach (var rid in json.GetProperty("supportedRids").EnumerateArray())
            {
                supported[rid.GetProperty("rid").GetString()!] = new RidEntry(
                    rid.GetProperty("asset").GetString()!,
                    rid.GetProperty("native").GetString()!);
            }

            var pinPath = Path.Combine(
                root, json.GetProperty("pins").GetProperty("assets").GetString()!);
            var pins = new Dictionary<string, (string, long)>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(pinPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(3, parts.Length);
                pins[parts[1]] = (parts[0].ToLowerInvariant(), long.Parse(parts[2]));
            }

            return new Z3Registry(
                json.GetProperty("z3Version").GetString()!,
                json.GetProperty("managed").GetProperty("asset").GetString()!,
                json.GetProperty("testHosts").GetProperty("expectedRidVariable").GetString()!,
                supported,
                pins);
        }

        public static string HostRid()
        {
            var os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : OperatingSystem.IsLinux() ? "linux"
                : RuntimeInformation.OSDescription;
            return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        }

        public static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "eng", "z3-consumers.json")))
                    return dir.FullName;
            }

            throw new InvalidOperationException(
                $"eng/z3-consumers.json not found above {AppContext.BaseDirectory}; "
                + "the Z3 consumer guard runs only inside a repository checkout.");
        }
    }
}
