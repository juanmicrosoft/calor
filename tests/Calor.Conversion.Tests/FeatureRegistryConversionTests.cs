using Calor.Compiler.Migration;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Verifies that features claimed as SupportLevel.Full in FeatureSupport.cs
/// actually convert C# snippets without errors.
/// </summary>
public class FeatureRegistryConversionTests
{
    private readonly ITestOutputHelper _output;

    public FeatureRegistryConversionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> FullSupportFeatureSnippets()
    {
        yield return new object[]
        {
            "goto-labeled-statement",
            @"
public class GotoTest
{
    public int Run()
    {
        goto end;
        end: return 42;
    }
}"
        };

        yield return new object[]
        {
            "postfix-operator",
            @"
public class PostfixTest
{
    public void Run()
    {
        int i = 0;
        i++;
        i--;
    }
}"
        };

        yield return new object[]
        {
            "binary-pattern-and-or",
            @"
public class BinaryPatternTest
{
    public bool Run(int x)
    {
        return x is > 0 and < 100;
    }
}"
        };

        yield return new object[]
        {
            "unary-pattern-not",
            @"
public class UnaryPatternTest
{
    public bool Run(object? x)
    {
        return x is not null;
    }
}"
        };

        yield return new object[]
        {
            "is-type-pattern",
            @"
public class IsTypePatternTest
{
    public string Run(object obj)
    {
        if (obj is string s)
        {
            return s;
        }
        return string.Empty;
    }
}"
        };

        yield return new object[]
        {
            "declaration-pattern",
            @"
public class DeclarationPatternTest
{
    public int Run(object obj)
    {
        switch (obj)
        {
            case int n:
                return n;
            default:
                return 0;
        }
    }
}"
        };

        yield return new object[]
        {
            "throw-expression",
            @"
using System;

public class ThrowExpressionTest
{
    public string Run(string? value)
    {
        var x = value ?? throw new ArgumentNullException(nameof(value));
        return x;
    }
}"
        };

        yield return new object[]
        {
            "nested-generic-type",
            @"
using System.Collections.Generic;

public class NestedGenericTest
{
    public Dictionary<string, List<int>> Run()
    {
        Dictionary<string, List<int>> d = new Dictionary<string, List<int>>();
        return d;
    }
}"
        };
    }

    [Theory]
    [MemberData(nameof(FullSupportFeatureSnippets))]
    public void FeatureRegistryEntries_ClaimedFull_ConvertSuccessfully(string featureId, string csharpSource)
    {
        var result = TestHelpers.ConvertCSharp(csharpSource, $"Test_{featureId.Replace("-", "_")}");

        // Log output for diagnostics
        _output.WriteLine($"=== Feature: {featureId} ===");
        _output.WriteLine($"Success: {result.Success}");
        if (result.CalorSource != null)
            _output.WriteLine($"Calor output:\n{result.CalorSource}");

        foreach (var issue in result.Issues)
            _output.WriteLine($"[{issue.Severity}] {issue.Message}");

        // Assert non-empty output
        Assert.False(string.IsNullOrWhiteSpace(result.CalorSource),
            $"Feature '{featureId}': conversion produced empty output");

        // Assert no Error-level issues (warnings are OK)
        var errors = result.Issues
            .Where(i => i.Severity == ConversionIssueSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Feature '{featureId}': conversion produced {errors.Count} error(s): " +
            string.Join("; ", errors.Select(e => e.Message)));
    }
}
