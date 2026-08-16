using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class PreprocessorStripperTests
{
    [Fact]
    public void SelectActiveBranchLossy_FalseConditionKeepsElse()
    {
        const string source = """
            #if false
            class Dead { }
            #else
            class Live { }
            #endif
            """;

        var result = Select(source);

        Assert.DoesNotContain("class Dead", result.Source);
        Assert.Contains("class Live", result.Source);
        Assert.DoesNotContain("#if", result.Source);
        Assert.Equal(3, result.RemovedConditionalDirectives.Count);
    }

    [Fact]
    public void SelectActiveBranchLossy_UsesProvidedSymbols()
    {
        const string source = """
            #if FEATURE
            class Enabled { }
            #else
            class Disabled { }
            #endif
            """;

        var enabled = Select(source, "FEATURE");
        var disabled = Select(source);

        Assert.Contains("class Enabled", enabled.Source);
        Assert.DoesNotContain("class Disabled", enabled.Source);
        Assert.DoesNotContain("class Enabled", disabled.Source);
        Assert.Contains("class Disabled", disabled.Source);
    }

    [Fact]
    public void SelectActiveBranchLossy_NestedIfElifElseUsesRoslynSemantics()
    {
        const string source = """
            #if OUTER
            #if FIRST
            class First { }
            #elif SECOND
            class Second { }
            #else
            class Fallback { }
            #endif
            #else
            class OuterFallback { }
            #endif
            """;

        var result = Select(source, "OUTER", "SECOND");

        Assert.Contains("class Second", result.Source);
        Assert.DoesNotContain("class First", result.Source);
        Assert.DoesNotContain("class Fallback", result.Source);
        Assert.DoesNotContain("class OuterFallback", result.Source);
    }

    [Theory]
    [InlineData("#nullable enable")]
    [InlineData("#pragma warning disable CS0618")]
    [InlineData("#warning preserved")]
    [InlineData("#error preserved")]
    [InlineData("#line 100 \"source.cs\"")]
    public void SelectActiveBranchLossy_PreservesNonconditionalDirectives(string directive)
    {
        var result = Select($"{directive}\nclass C {{ }}");

        Assert.Contains(directive, result.Source);
        Assert.Empty(result.RemovedConditionalDirectives);
    }

    [Fact]
    public void LegacyStripApi_RemainsSourceCompatibleAndUsesRoslyn()
    {
#pragma warning disable CS0618
        var result = PreprocessorStripper.StripWithReport(
            "#if false\nclass Dead { }\n#else\nclass Live { }\n#endif");
#pragma warning restore CS0618

        Assert.Contains("class Live", result.Source);
        Assert.DoesNotContain("class Dead", result.Source);
        Assert.Equal(3, result.ConditionalDirectives.Count);
    }

    private static SelectedBranchResult Select(string source, params string[] symbols)
        => PreprocessorStripper.SelectActiveBranchLossy(
            source,
            new CSharpParseOptions(
                LanguageVersion.Preview,
                preprocessorSymbols: symbols));
}
