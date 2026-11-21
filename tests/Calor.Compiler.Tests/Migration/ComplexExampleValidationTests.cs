using System.Text.Json;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests.PostConversion;

/// <summary>Composed examples are read from the authored source, never copied into tests.</summary>
public class ComplexExampleValidationTests
{
    [Theory]
    [InlineData("complex_class")]
    [InlineData("complex_async")]
    [InlineData("complex_enum")]
    [InlineData("complex_yield")]
    public void NativeComposedExample_Compiles(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, ExemplarCompileChecker.TeachingSourcePath)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, ExemplarCompileChecker.TeachingSourcePath)));
        var example = Assert.Single(source.RootElement.GetProperty("programs").EnumerateArray()
            .Where(item => item.GetProperty("id").GetString() == id));
        var diagnostics = ExemplarCompileChecker.Check(new DocFile(id,
            "```calor\n" + example.GetProperty("calor").GetString() + "\n```\n"));
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
    }
}
