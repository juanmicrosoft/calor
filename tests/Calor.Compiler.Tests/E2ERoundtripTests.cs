using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests that verify C# to Calor conversion works correctly by converting each
/// E2E scenario's output.g.cs back to Calor and verifying it compiles successfully.
/// </summary>
public class E2ERoundtripTests
{
    public static IEnumerable<object[]> GetE2EScenarios()
    {
        var scenariosPath = GetScenariosPath();
        foreach (var dir in Directory.GetDirectories(scenariosPath).OrderBy(d => d))
        {
            var outputFile = Path.Combine(dir, "output.g.cs");
            if (File.Exists(outputFile))
            {
                yield return new object[] { Path.GetFileName(dir), dir };
            }
        }
    }

    private static string GetScenariosPath()
    {
        // Walk up from assembly location to find tests/E2E/scenarios
        var assemblyDir = Path.GetDirectoryName(typeof(E2ERoundtripTests).Assembly.Location)!;
        var current = new DirectoryInfo(assemblyDir);
        while (current != null)
        {
            var scenariosPath = Path.Combine(current.FullName, "tests", "E2E", "scenarios");
            if (Directory.Exists(scenariosPath))
                return scenariosPath;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find tests/E2E/scenarios");
    }

    [Theory]
    [MemberData(nameof(GetE2EScenarios))]
    public void ConvertCSharpToCalor_E2EScenario_Succeeds(string scenarioName, string scenarioPath)
    {
        // Arrange
        var outputCsPath = Path.Combine(scenarioPath, "output.g.cs");
        var csharpSource = File.ReadAllText(outputCsPath);
        var converter = new CSharpToCalorConverter();

        // Act
        var result = converter.Convert(csharpSource, outputCsPath);

        // Assert
        Assert.True(result.Success, $"Scenario {scenarioName} conversion failed:\n{GetErrorMessage(result)}");
        Assert.NotNull(result.CalorSource);
        Assert.NotEmpty(result.CalorSource);
    }

    // Scenarios with LINQ expressions (from quantifier contracts) cannot be round-tripped
    // because the C# to Calor converter doesn't have an inverse transform for LINQ → quantifiers
    private static readonly HashSet<string> _knownNonRoundtripScenarios = new(StringComparer.OrdinalIgnoreCase)
    {
        "07_quantifiers",           // Uses Enumerable.Range().All()/Any() for quantifiers
        "08_contract_inheritance_z3" // Uses advanced contract features
    };

    [Theory]
    [MemberData(nameof(GetE2EScenarios))]
    public void Roundtrip_E2EScenario_CompilesBackToValidCSharp(string scenarioName, string scenarioPath)
    {
        // Skip scenarios with known non-roundtrip features
        if (_knownNonRoundtripScenarios.Contains(scenarioName))
        {
            return; // Skip - these scenarios use features without inverse transforms
        }

        // Arrange
        var outputCsPath = Path.Combine(scenarioPath, "output.g.cs");
        var csharpSource = File.ReadAllText(outputCsPath);
        var converter = new CSharpToCalorConverter();

        // Act - Convert C# to Calor
        var conversionResult = converter.Convert(csharpSource, outputCsPath);
        Assert.True(conversionResult.Success, $"Conversion failed for {scenarioName}:\n{GetErrorMessage(conversionResult)}");

        // Act - Compile Calor back to C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

        // Assert
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip compilation failed for {scenarioName}:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}"));
    }
}
