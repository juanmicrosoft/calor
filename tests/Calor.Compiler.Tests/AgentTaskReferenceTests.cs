using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

public class AgentTaskReferenceTests
{
    [Fact]
    public void ActualReference_AllCompleteExamplesCompile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, AgentTaskReferenceChecker.RelativePath)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, AgentTaskReferenceChecker.RelativePath);
        var content = File.ReadAllText(path);
        var programs = AgentTaskReferenceChecker.ExtractPrograms(content);
        Assert.Equal(38, programs.Count);
        foreach (var name in new[] { "TryDouble", "SafeDivide", "HasNegative", "DigitValue", "Offset" })
            Assert.Contains(programs, program => program.Source.Contains($":{name}:", StringComparison.Ordinal));
        var diagnostics = AgentTaskReferenceChecker.Check(new(path, content));
        Assert.True(diagnostics.Count == 0, string.Join(Environment.NewLine, diagnostics));
    }

    [Fact]
    public void Extraction_HandlesBareAndCalorFencesAndExplicitFragments()
    {
        var source = Wrap("""
            ```
            §F{f1:Value:pub} () -> i32
              §R 1
            ```
            ```calor
            §M{m1:Complete}
              §F{f2:Value:pub} () -> i32
                §R 2
            ```
            ```calor-fragment
            §F{id:Name:pub}
              §R ...
            ```
            """);
        Assert.Equal(2, AgentTaskReferenceChecker.ExtractPrograms(source).Count);
        Assert.Empty(AgentTaskReferenceChecker.Check(new("helpers.sh", source)));
    }

    [Theory]
    [InlineData("missing markers")]
    [InlineData("cat << 'CALOR_REFERENCE'\nno closing marker")]
    [InlineData("cat << 'CALOR_REFERENCE'\nno examples\nCALOR_REFERENCE\n")]
    [InlineData("cat << 'CALOR_REFERENCE'\n```\n§M{m1:Empty}\n```\nCALOR_REFERENCE_wrong\n")]
    public void MissingReferenceOrExamples_FailsClosed(string source)
    {
        Assert.Contains(AgentTaskReferenceChecker.Check(new("helpers.sh", source)),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Fact]
    public void SemanticError_FailsCheckEvenWhenSourceParses()
    {
        var source = Wrap("""
            ```
            §F{f1:Wrong:pub} () -> i32
              §R "wrong"
            ```
            """);
        Assert.Contains(AgentTaskReferenceChecker.Check(new("helpers.sh", source)),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Theory]
    [InlineData("```\n§M{m1:Empty}")]
    [InlineData("```calor\n§M{m1:Empty}\n```csharp")]
    [InlineData("```calorr\n§M{m1:Empty}\n```")]
    public void MalformedFences_CannotSilentlySkipExamples(string reference)
    {
        Assert.Contains(AgentTaskReferenceChecker.Check(new("helpers.sh", Wrap(reference))),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Fact]
    public void LeadingComments_CannotHideAnInvalidModule()
    {
        var source = Wrap("""
            ```calor

            // A complete module still needs semantic checking.
            §M{m1:Wrong}
              §F{f1:Value:pub} () -> i32
                §R "wrong"
            ```
            """);
        var program = Assert.Single(AgentTaskReferenceChecker.ExtractPrograms(source));
        Assert.False(program.Wrapped);
        Assert.Equal(5, program.FirstContentLine);
        Assert.NotEmpty(AgentTaskReferenceChecker.Check(new("helpers.sh", source)));
    }

    [Fact]
    public void WindowsNewlines_PreserveReferenceExtraction()
    {
        var source = Wrap("""
            ```calor
            §M{m1:Complete}
              §F{f1:Value:pub} () -> i32
                §R 2
            ```
            """).Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Single(AgentTaskReferenceChecker.ExtractPrograms(source));
        Assert.Empty(AgentTaskReferenceChecker.Check(new("helpers.sh", source)));
    }

    private static string Wrap(string reference) =>
        "cat << 'CALOR_REFERENCE'\n" + reference + "\nCALOR_REFERENCE\n";
}
