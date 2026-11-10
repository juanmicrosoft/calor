using System.Text.Json;
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
        using var authored = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(directory.FullName, TeachingSourcePath)));
        var authoredSources = authored.RootElement.GetProperty("programs").EnumerateArray()
            .Select(program => program.GetProperty("calor").GetString()!.TrimEnd()).ToArray();
        Assert.Equal(87, authoredSources.Length);
        Assert.Equal(authoredSources, programs.Select(program => program.Source));
        Assert.All(programs, program => Assert.False(program.Wrapped));
        foreach (var name in new[] { "TryDouble", "SafeDivide", "HasNegative", "DigitValue", "Offset", "ClampScore" })
            Assert.Contains(programs, program => program.Source.Contains($"func {name} ", StringComparison.Ordinal));
        var diagnostics = CheckAgentTaskReference(new(path, content));
        Assert.True(diagnostics.Count == 0, string.Join(Environment.NewLine, diagnostics));
    }

    [Fact]
    public void Extraction_HandlesBareAndCalorFencesAndExplicitFragments()
    {
        var source = Wrap("""
            ```
            func Value () → int
              !
              return 1
            ```
            ```calor
            module Complete
              func Value () → int
                !
                return 2
            ```
            ```calor-fragment
            func Name () → int
              return ...
            ```
            """);
        Assert.Equal(2, ExtractAgentTaskPrograms(source).Count);
        Assert.Empty(CheckAgentTaskReference(new("helpers.sh", source)));
    }

    [Theory]
    [InlineData("no examples")]
    [InlineData("```calor\nmodule Empty")]
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
            func Wrong () → int
              !
              return "wrong"
            ```
            """);
        Assert.Contains(CheckAgentTaskReference(new("helpers.sh", source)),
            diagnostic => diagnostic.Code == DiagnosticCode.DocDriftExampleCompileError);
    }

    [Theory]
    [InlineData("```\nmodule Empty")]
    [InlineData("```calor\nmodule Empty\n```csharp")]
    [InlineData("```calorr\nmodule Empty\n```")]
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
            module Wrong
              func Value () → int
                return "wrong"
            ```
            """);
        var program = Assert.Single(ExtractAgentTaskPrograms(source));
        Assert.False(program.Wrapped);
        Assert.Equal(2, program.FirstContentLine);
        Assert.StartsWith("\n// A complete module", program.Source);
        var diagnostics = CheckAgentTaskReference(new("helpers.sh", source));
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticCode.DocDriftExampleCompileError, diagnostic.Code);
            Assert.Equal(6, diagnostic.Span.Line);
        });
    }

    [Fact]
    public void WindowsNewlines_PreserveReferenceExtraction()
    {
        var source = Wrap("""
            ```calor
            module Complete
              func Value () → int
                !
                return 2
            ```
            """).Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Single(ExtractAgentTaskPrograms(source));
        Assert.Empty(CheckAgentTaskReference(new("helpers.sh", source)));
    }

    private static string Wrap(string reference) => reference + "\n";
}
