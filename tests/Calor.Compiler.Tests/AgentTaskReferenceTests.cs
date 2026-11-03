using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;
using static Calor.Compiler.SelfCheck.ExemplarCompileChecker;

namespace Calor.Compiler.Tests;

public class AgentTaskReferenceTests
{
    [Fact]
    public void ActualReference_AllCompleteExamplesCompile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, AgentTaskReferencePath)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, AgentTaskReferencePath);
        var content = File.ReadAllText(path);
        var programs = ExtractAgentTaskPrograms(content);
        Assert.Equal(81, programs.Count);
        foreach (var name in new[] { "TryDouble", "SafeDivide", "HasNegative", "DigitValue", "Offset", "ClampScore" })
            Assert.Contains(programs, program => program.Source.Contains($"{{{name}:", StringComparison.Ordinal));
        var diagnostics = CheckAgentTaskReference(new(path, content));
        Assert.True(diagnostics.Count == 0, string.Join(Environment.NewLine, diagnostics));
    }

    [Fact]
    public void Extraction_HandlesBareAndCalorFencesAndExplicitFragments()
    {
        var source = Wrap("""
            ```
            §F{Value:pub} () -> i32
              §R 1
            ```
            ```calor
            §M{Complete}
              §F{Value:pub} () -> i32
                §R 2
            ```
            ```calor-fragment
            §F{Name:pub}
              §R ...
            ```
            """);
        Assert.Equal(2, ExtractAgentTaskPrograms(source).Count);
        Assert.Empty(CheckAgentTaskReference(new("helpers.sh", source)));
    }

    [Theory]
    [InlineData("no examples")]
    [InlineData("```calor\n§M{Empty}")]
    public void MissingReferenceOrExamples_FailsClosed(string source)
    {
        Assert.Contains(CheckAgentTaskReference(new("helpers.sh", source)),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Fact]
    public void SemanticError_FailsCheckEvenWhenSourceParses()
    {
        var source = Wrap("""
            ```
            §F{Wrong:pub} () -> i32
              §R "wrong"
            ```
            """);
        Assert.Contains(CheckAgentTaskReference(new("helpers.sh", source)),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Theory]
    [InlineData("```\n§M{Empty}")]
    [InlineData("```calor\n§M{Empty}\n```csharp")]
    [InlineData("```calorr\n§M{Empty}\n```")]
    public void MalformedFences_CannotSilentlySkipExamples(string reference)
    {
        Assert.Contains(CheckAgentTaskReference(new("helpers.sh", Wrap(reference))),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Fact]
    public void LeadingComments_CannotHideAnInvalidModule()
    {
        var source = Wrap("""
            ```calor

            // A complete module still needs semantic checking.
            §M{Wrong}
              §F{Value:pub} () -> i32
                §R "wrong"
            ```
            """);
        var program = Assert.Single(ExtractAgentTaskPrograms(source));
        Assert.False(program.Wrapped);
        Assert.Equal(4, program.FirstContentLine);
        Assert.NotEmpty(CheckAgentTaskReference(new("helpers.sh", source)));
    }

    [Fact]
    public void WindowsNewlines_PreserveReferenceExtraction()
    {
        var source = Wrap("""
            ```calor
            §M{Complete}
              §F{Value:pub} () -> i32
                §R 2
            ```
            """).Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Single(ExtractAgentTaskPrograms(source));
        Assert.Empty(CheckAgentTaskReference(new("helpers.sh", source)));
    }

    private static string Wrap(string reference) => reference + "\n";
}
