using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NullabilityDocumentationTests
{
    public static IEnumerable<object[]> Examples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Calor.sln")))
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "website", "content")))
                break;
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var guide = File.ReadAllText(Path.Combine(directory.FullName, "website", "content",
            "guides", "nullability-and-dotnet-interop.mdx"));
        var examples = Regex.Matches(guide, @"```calor\r?\n(?<source>[\s\S]*?)\r?\n```");
        Assert.Equal(10, examples.Count);
        foreach (Match example in examples)
        {
            var source = example.Groups["source"].Value.Replace("\r\n", "\n");
            var name = Regex.Match(source, @"§M\{m1:(\w+)\}").Groups[1].Value;
            Assert.NotEmpty(name);
            foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
                yield return [name, source, mode];
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void CompleteGuideExampleUsesProductionDiagnosticsAndEmission(string name, string source, string mode)
    {
        var result = Program.Compile(source, $"{name}.calr", new CompilationOptions
        {
            EnableTypeChecking = mode != "type-off",
            EnforceEffects = mode != "effects-off",
            UnsafeTranspileOnly = mode == "transpile",
            VerifyContracts = mode == "verify",
            StatusWriter = TextWriter.Null
        });
        var expected = name switch
        {
            "InitRejected" => (Code: "Calor0272", Line: 4, Column: 22),
            "ReturnRejected" => (Code: "Calor0273", Line: 4, Column: 8),
            "ArgumentRejected" => (Code: "Calor0274", Line: 6, Column: 17),
            _ => (Code: "", Line: 0, Column: 0)
        };
        if (expected.Code.Length != 0)
        {
            Assert.True(result.HasErrors);
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(expected.Code, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(expected.Line, diagnostic.Span.Line);
            Assert.Equal(expected.Column, diagnostic.Span.Column);
            Assert.Equal("input", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
            Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
        }
        else
        {
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.False(string.IsNullOrWhiteSpace(result.GeneratedCode));
            Assert.DoesNotContain("Calor.Runtime.Option", result.GeneratedCode);
            if (name is "InitSafe" or "ReturnSafe" or "ArgumentSafe" or "EnvironmentSafe")
                Assert.Contains("??", result.GeneratedCode);
            if (name == "ThrowSafe")
                Assert.Contains("throw new ArgumentNullException", result.GeneratedCode);
            if (name == "PatternSafe")
                Assert.Contains("string text", result.GeneratedCode);
        }
    }
}
