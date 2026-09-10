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
        Assert.True(AgentTaskReferenceChecker.ExtractPrograms(content).Count >= 32);
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

    private static string Wrap(string reference) =>
        "cat << 'CALOR_REFERENCE'\n" + reference + "\nCALOR_REFERENCE\n";
}
