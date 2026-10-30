using System.Text.Json;
using System.Text.Json.Nodes;
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
public class DS15FixtureRegistryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly HashSet<string> CurrentExpectationFixtures =
        ["local-function", "conditional-expression-hoisting"];

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

    [Theory]
    [InlineData("local-function")]
    [InlineData("conditional-expression-hoisting")]
    public void CurrentExpectation_ChangesOnlyApprovedClassificationFields(string name)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var original = $"bench/phase0-agent-native/fixtures/d-s1.5/{name}/fixture.json";
        var resolved = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentTypedInput(repoRoot, original,
            Calor.Compiler.Tests.ActiveSyntaxInputs.InputKind.Structured);
        var historical = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, original)))!.AsObject();
        var current = JsonNode.Parse(File.ReadAllText(resolved))!.AsObject();
        Assert.False(historical.ContainsKey("currentObservation"));
        Assert.NotNull(current["currentObservation"]);
        foreach (var field in new[]
        {
            "lossKindCertifiedAbsent", "expectedLossKinds", "allowedLossKinds", "currentObservation"
        })
        {
            historical.Remove(field);
            current.Remove(field);
        }
        Assert.True(JsonNode.DeepEquals(historical, current),
            "Only explicit current classification fields may differ; fixture identity, runtime results and symbols stay frozen.");
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

    private void ValidateFixture(string dir, string name)
    {

        // 1. Structure: all four schema components present.
        var manifestPath = Path.Combine(dir, "fixture.json");
        Assert.True(File.Exists(manifestPath), $"{name}: fixture.json missing");
        var inputPath = Path.Combine(dir, "input.cs");
        Assert.True(File.Exists(inputPath), $"{name}: input.cs missing");
        var originalExpectedPath = Path.Combine(dir, "expected.calr");
        Assert.True(File.Exists(originalExpectedPath), $"{name}: expected.calr missing");
        var originalTestPath = Path.Combine(dir, "test.calr");
        Assert.True(File.Exists(originalTestPath), $"{name}: test.calr missing");
        var repoRoot = Path.GetFullPath(Path.Combine(RegistryPath(), "../../../.."));
        var expectedPath = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, originalExpectedPath);
        var testPath = Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, originalTestPath);

        // 2. Manifest: featureKey named; the DIRECTORY is the sanitized form of the key
        // (keys may contain spaces/parens/slashes — see README) and must match it.
        var relativeManifest = Path.GetRelativePath(repoRoot, manifestPath).Replace('\\', '/');
        var resolvedManifest = CurrentExpectationFixtures.Contains(name)
            ? Calor.Compiler.Tests.ActiveSyntaxInputs.ForCurrentTypedInput(repoRoot, relativeManifest,
                Calor.Compiler.Tests.ActiveSyntaxInputs.InputKind.Structured)
            : manifestPath;
        using var doc = JsonDocument.Parse(File.ReadAllText(resolvedManifest));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            fixture = name,
            inputContract = "current-compiler-active-syntax",
            originalExpectationPath = relativeManifest,
            resolvedExpectationPath = Path.GetRelativePath(repoRoot, resolvedManifest).Replace('\\', '/'),
            csharpPath = Path.GetRelativePath(repoRoot, inputPath).Replace('\\', '/'),
            originalCalorPath = Path.GetRelativePath(repoRoot, originalExpectedPath).Replace('\\', '/'),
            resolvedCalorPath = Path.GetRelativePath(repoRoot, expectedPath).Replace('\\', '/'),
            originalTestPath = Path.GetRelativePath(repoRoot, originalTestPath).Replace('\\', '/'),
            resolvedTestPath = Path.GetRelativePath(repoRoot, testPath).Replace('\\', '/')
        }));
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
        var support = FeatureSupport.GetSupportLevel(featureKey!);
        var declaredSupport = doc.RootElement.TryGetProperty(
                "supportLevel",
                out var supportElement)
            ? Enum.Parse<SupportLevel>(
                supportElement.GetString()!,
                ignoreCase: true)
            : support;
        Assert.Equal(support, declaredSupport);
        var expectedLosses = ReadLossKinds(
            doc.RootElement,
            "expectedLossKinds");
        var allowedLosses = ReadLossKinds(
            doc.RootElement,
            "allowedLossKinds");

        // 3. Conversion match: input.cs converts to exactly expected.calr.
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "Fixture",
            GracefulFallback = true,
            AutoGenerateIds = true,
            PreprocessorMode =
                PreprocessorConversionMode.PreserveAllBranches
        });
        var converted = converter.Convert(File.ReadAllText(inputPath), $"{name}.cs");
        Assert.True(
            converted.Success,
            $"{name}: conversion failed: "
            + string.Join("; ", converted.Issues.Select(issue => issue.Message)));
        Assert.NotNull(converted.CalorSource);
        if (CurrentExpectationFixtures.Contains(name))
        {
            var observation = doc.RootElement.GetProperty("currentObservation");
            Assert.Equal("current-compiler-active-syntax", observation.GetProperty("inputContract").GetString());
            Assert.False(string.IsNullOrWhiteSpace(observation.GetProperty("reason").GetString()));
            Assert.Equal(observation.GetProperty("nativeConversionCount").GetInt32(), converted.NativeConversionCount);
            Assert.Equal(observation.GetProperty("fullyNative").GetBoolean(), converted.Losses.Count == 0);
            var expectedInterop = observation.GetProperty("interopFeatureCounts").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetInt32());
            var actualInterop = converted.Losses.Where(loss => loss.Kind == ConversionLossKind.InteropPreserved)
                .GroupBy(loss => loss.Feature).ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(expectedInterop.OrderBy(pair => pair.Key), actualInterop.OrderBy(pair => pair.Key));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                fixture = name,
                fullyNative = converted.Losses.Count == 0,
                converted.NativeConversionCount,
                interopFeatureCounts = actualInterop,
                issues = converted.Issues.Select(issue => new { issue.Feature, issue.Message })
            }));
            Assert.Equal(observation.GetProperty("requiredIssueFeatures").EnumerateArray()
                .Select(feature => feature.GetString()).Order(),
                converted.Issues.Select(issue => issue.Feature).Distinct().Order());
            AssertValueTestPasses(name, File.ReadAllText(inputPath), runtimeEntryPoint!, runtimeExpectedInt);
        }
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
        var actualLosses = converted.Losses
            .Select(loss => loss.Kind)
            .Distinct()
            .ToHashSet();
        if (support == SupportLevel.Full)
        {
            Assert.Empty(
                converted.Losses);
        }
        else
        {
            if (expectedLosses.Count == 0
                && allowedLosses.Count == 0)
            {
                Assert.NotEqual(
                    JsonValueKind.Null,
                    certifiedAbsent.ValueKind);
                Assert.Empty(converted.Losses);
            }
            else
            {
                Assert.NotEmpty(
                    expectedLosses);
                Assert.NotEmpty(
                    allowedLosses);
                Assert.Subset(
                    allowedLosses,
                    actualLosses);
                Assert.Subset(
                    actualLosses,
                    expectedLosses);
            }
            Assert.DoesNotContain(
                converted.Losses,
                loss => loss.Kind is
                    ConversionLossKind.Dropped
                    or ConversionLossKind.FallbackTodo
                    or ConversionLossKind.PreprocessorStripped
                    or ConversionLossKind.DirectiveRemoved);
        }
        Assert.Equal(
            Normalize(File.ReadAllText(expectedPath)),
            Normalize(converted.CalorSource!));
        if (doc.RootElement.TryGetProperty(
                "convertedRuntimeEntryPoint",
                out var convertedEntry)
            && doc.RootElement.TryGetProperty(
                "convertedExpectedUndefinedInt",
                out var undefinedExpected)
            && doc.RootElement.TryGetProperty(
                "convertedExpectedDefinedInt",
                out var definedExpected))
        {
            var compilation = Program.Compile(
                converted.CalorSource!,
                $"{name}.calr",
                new Calor.Compiler.CompilationOptions
                {
                    DeferGeneratedOutputValidation = true,
                    EnforceEffects = false
                });
            Assert.False(
                compilation.HasErrors,
                string.Join("; ", compilation.Diagnostics
                    .Where(diagnostic => diagnostic.IsError)
                    .Select(diagnostic => diagnostic.Message)));
            var symbols = doc.RootElement.TryGetProperty(
                    "definedSymbols",
                    out var symbolArray)
                ? symbolArray.EnumerateArray()
                    .Select(symbol => symbol.GetString()!)
                    .ToArray()
                : Array.Empty<string>();
            AssertValueTestPasses(
                name,
                compilation.GeneratedCode,
                convertedEntry.GetString()!,
                undefinedExpected.GetInt32(),
                []);
            AssertValueTestPasses(
                name,
                compilation.GeneratedCode,
                convertedEntry.GetString()!,
                definedExpected.GetInt32(),
                symbols);
        }

        // 4. Both Calor files compile with zero errors (indeterminate = failing).
        foreach (var calr in new[] { expectedPath, testPath })
        {
            var result = Program.Compile(File.ReadAllText(calr),
                Path.GetFileName(calr),
                new Calor.Compiler.CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive
                });
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
        int expected,
        IReadOnlyList<string>? symbols = null)
    {
        var tree = CSharpSyntaxTree.ParseText(
            generatedCode,
            new CSharpParseOptions(
                LanguageVersion.Latest,
                preprocessorSymbols: symbols ?? []));
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

    private static HashSet<ConversionLossKind> ReadLossKinds(
        JsonElement manifest,
        string property)
    {
        if (!manifest.TryGetProperty(
                property,
                out var values))
            return [];
        return values.EnumerateArray()
            .Select(value =>
            {
                Assert.True(
                    Enum.TryParse<ConversionLossKind>(
                        value.GetString(),
                        out var parsed),
                    $"Unknown conversion loss kind "
                    + $"'{value.GetString()}'.");
                return parsed;
            })
            .ToHashSet();
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
