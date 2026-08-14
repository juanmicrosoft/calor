using System.Text.Json;
using Calor.Compiler;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Conversion.Tests;

/// <summary>
/// PP-S4's fixture registry (A-1.5.7; roadmap v0.13 §2.5 gate 6). Every entry under
/// bench/phase0-agent-native/fixtures/d-s1.5/ must be structurally complete, its C# input
/// must convert to exactly the expected Calor, and both Calor files must compile.
/// Indeterminate (a fixture that will not build) counts as FAILING, per the registration —
/// which these tests implement by failing on any defect rather than skipping.
/// The registry is legitimately empty until the first SupportLevel promotion; the
/// enumeration test pins the location exists either way.
/// </summary>
public class DS15FixtureRegistryTests
{
    private static string RegistryPath()
    {
        // Walk up from the test bin dir to the repo root (marker: Directory.Build.props).
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "bench", "phase0-agent-native", "fixtures", "d-s1.5");
    }

    [Fact]
    public void Registry_ExistsAtRegisteredLocation()
    {
        var root = RegistryPath();
        Assert.True(Directory.Exists(root),
            $"A-1.5.7 registry location missing: {root}");
        Assert.True(File.Exists(Path.Combine(root, "README.md")),
            "Registry schema doc (README.md) missing");
    }

    [Fact]
    public void AllFixtures_AreCompleteConvertExactlyAndCompile()
    {
        // A single fact rather than a MemberData theory: the registry is legitimately
        // empty until the first SupportLevel promotion, and xUnit fails a theory with
        // zero data rows. Errors are COLLECTED across all fixtures (not fail-fast), so
        // one bad fixture does not hide the others' defects.
        var root = RegistryPath();
        if (!Directory.Exists(root)) return;

        var failures = new List<string>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                ValidateFixture(dir, Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(dir)}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0,
            "Fixture defects (indeterminate = failing):\n" + string.Join("\n", failures));
    }

    private static void ValidateFixture(string dir, string name)
    {

        // 1. Structure: all four schema components present.
        var manifestPath = Path.Combine(dir, "fixture.json");
        Assert.True(File.Exists(manifestPath), $"{name}: fixture.json missing");
        var inputPath = Path.Combine(dir, "input.cs");
        Assert.True(File.Exists(inputPath), $"{name}: input.cs missing");
        var expectedPath = Path.Combine(dir, "expected.calr");
        Assert.True(File.Exists(expectedPath), $"{name}: expected.calr missing");
        var testPath = Path.Combine(dir, "test.calr");
        Assert.True(File.Exists(testPath), $"{name}: test.calr missing");

        // 2. Manifest: featureKey named; the DIRECTORY is the sanitized form of the key
        // (keys may contain spaces/parens/slashes — see README) and must match it.
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var featureKey = doc.RootElement.GetProperty("featureKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(featureKey), $"{name}: featureKey empty");
        Assert.Equal(SanitizeKey(featureKey!), name);
        var certifiedAbsent = doc.RootElement
            .GetProperty("lossKindCertifiedAbsent");
        var runtimeEntryPoint = doc.RootElement
            .GetProperty("runtimeEntryPoint")
            .GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(runtimeEntryPoint),
            $"{name}: runtimeEntryPoint empty");
        var runtimeExpectedInt = doc.RootElement
            .GetProperty("runtimeExpectedInt")
            .GetInt32();

        // 3. Conversion match: input.cs converts to exactly expected.calr.
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "Fixture",
            GracefulFallback = true,
            AutoGenerateIds = true,
            StripPreprocessor = false
        });
        var converted = converter.Convert(File.ReadAllText(inputPath), $"{name}.cs");
        Assert.True(
            converted.Success,
            $"{name}: conversion failed: "
            + string.Join("; ", converted.Issues.Select(issue => issue.Message)));
        Assert.NotNull(converted.CalorSource);
        if (certifiedAbsent.ValueKind != JsonValueKind.Null)
        {
            var lossName = certifiedAbsent.GetString();
            Assert.True(
                Enum.TryParse<ConversionLossKind>(lossName, out var lossKind),
                $"{name}: unknown loss kind '{lossName}'");
            Assert.DoesNotContain(
                converted.Losses,
                loss => loss.Kind == lossKind);
        }
        Assert.Equal(
            Normalize(File.ReadAllText(expectedPath)),
            Normalize(converted.CalorSource!));

        // 4. Both Calor files compile with zero errors (indeterminate = failing).
        foreach (var calr in new[] { expectedPath, testPath })
        {
            var result = Program.Compile(File.ReadAllText(calr),
                Path.GetFileName(calr), new Calor.Compiler.CompilationOptions());
            Assert.False(result.HasErrors,
                $"{name}/{Path.GetFileName(calr)} does not compile: " +
                string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
            if (calr == testPath)
                AssertValueTestPasses(
                    name,
                    result.GeneratedCode,
                    runtimeEntryPoint!,
                    runtimeExpectedInt);
        }
    }

    private static void AssertValueTestPasses(
        string name,
        string generatedCode,
        string entryPoint,
        int expected)
    {
        var tree = CSharpSyntaxTree.ParseText(
            generatedCode,
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            $"DS15_{name}_{Guid.NewGuid():N}",
            [tree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            $"{name}/test.calr generated C# failed: "
            + string.Join("; ", emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var methods = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static))
            .Where(method =>
                method.GetParameters().Length == 0
                && !method.IsSpecialName)
            .ToArray();
        foreach (var method in methods)
            _ = method.Invoke(null, null);
        var entry = Assert.Single(methods.Where(method =>
            method.Name == entryPoint && method.ReturnType == typeof(int)));
        Assert.Equal(expected, Assert.IsType<int>(entry.Invoke(null, null)));
    }

    private static string Normalize(string s) =>
        s.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>README rule: every char outside [a-z0-9-] becomes '-', runs collapsed.</summary>
    private static string SanitizeKey(string key)
    {
        var chars = key.ToLowerInvariant()
            .Select(c => (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') ? c : '-');
        var collapsed = new System.Text.StringBuilder();
        foreach (var c in chars)
        {
            if (c == '-' && collapsed.Length > 0 && collapsed[^1] == '-') continue;
            collapsed.Append(c);
        }
        return collapsed.ToString().Trim('-');
    }
}
