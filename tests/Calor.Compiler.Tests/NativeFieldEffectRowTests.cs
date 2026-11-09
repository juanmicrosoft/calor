using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeFieldEffectRowTests
{
    private const string BadRow = """
        module BadRow
          class Counter
            field int:count private
            ! cw // Wrong: count is not a function value, and this is a separate line.
        """;

    private const string MisplacedMessage =
        "A §E{…} effect row must be on the same line as the type it annotates. "
        + "Move it onto the end of the 'count' field line, or — if this is meant to be "
        + "the function's own effect declaration — onto its own line in the §F body.";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SeparateFieldRow_RetainsTheSharedPlacementDiagnostic(string newline)
    {
        var source = BadRow.Replace("\n", newline, StringComparison.Ordinal);
        var (module, diagnostics) = Parse(source);
        AssertMisplaced(Assert.Single(diagnostics), source);
        Assert.Null(Assert.Single(Assert.Single(module.Classes).Fields).Row);

        var compiled = Program.Compile(source, "BadRow.calr");
        Assert.True(compiled.HasErrors);
        AssertMisplaced(Assert.Single(compiled.Diagnostics.Errors), source);
        Assert.Empty(compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("int", "", "! cw")]
    [InlineData("int", " = 0", "! cw, fs:r")]
    [InlineData("Func<int>", "", "! cw")]
    [InlineData("int", "", "!")]
    [InlineData("int", "", "! notAnEffect")]
    public void MisplacedFieldRows_AreConsumedWithoutAttachingOrLosingFollowingMembers(
        string type, string initializer, string row)
    {
        var source = $$"""
            module Recovery
              class Counter
                field {{type}}:count private{{initializer}}
                {{row}}
                field int:other private
                func Read () → int
                  !
                  return 7
              func After () → int
                !
                return 9
            """;
        var (module, diagnostics) = Parse(source);
        AssertMisplaced(Assert.Single(diagnostics), source);
        var counter = Assert.Single(module.Classes);
        Assert.Equal(["count", "other"], counter.Fields.Select(field => field.Name));
        Assert.All(counter.Fields, field => Assert.Null(field.Row));
        Assert.Equal("Read", Assert.Single(counter.Methods).Name);
        Assert.Empty(counter.Methods[0].Effects!.Effects);
        Assert.Equal("After", Assert.Single(module.Functions).Name);
        Assert.Empty(module.Functions[0].Effects!.Effects);
    }

    [Fact]
    public void SameLineNonCallableFieldRow_StillFailsInBindingRatherThanParsing()
    {
        const string source = """
            module WrongTarget
              class Counter
                field int:count private ! cw
            """;
        var (module, diagnostics) = Parse(source);
        Assert.Empty(diagnostics);
        Assert.NotNull(Assert.Single(Assert.Single(module.Classes).Fields).Row);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.EffectRowMisplaced, error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(source.IndexOf('!'), error.Span.Start);
        Assert.Equal(1, error.Span.Length);
    }

    [Fact]
    public void CallableFieldRowsAndSeparateFunctionRows_RemainValid()
    {
        const string source = """
            module ValidRows
              class Counter
                field Func<int>:next private ! cw
                field int:count private
                func Read () → int
                  ! cw
                  return 7
              func Print () → void
                ! cw
                System.Console.WriteLine("ok")
            """;
        var (module, diagnostics) = Parse(source);
        Assert.Empty(diagnostics);
        var fields = Assert.Single(module.Classes).Fields;
        Assert.NotNull(fields[0].Row);
        Assert.Null(fields[1].Row);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void UnownedClassRow_DoesNotAcquireAFieldPlacementDiagnostic()
    {
        const string source = """
            module Unowned
              class Counter
                ! cw
                field int:count private
            """;
        var (_, diagnostics) = Parse(source);
        Assert.True(diagnostics.HasErrors);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowMisplaced);
    }

    [Fact]
    public void SeparateFieldRow_CliJsonRejectsWithTheSameDiagnosticAndNoGeneratedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-native-field-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "BadRow.calr");
            var output = Path.Combine(directory, "BadRow.g.cs");
            File.WriteAllText(input, BadRow);
            var result = CliTestHarness.RunCli(directory, "--no-telemetry", "--input", input,
                "--output", output, "--format", "json");
            Assert.Equal(1, result.ExitCode);
            using var json = JsonDocument.Parse(result.StdOut);
            var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(DiagnosticCode.EffectRowMisplaced, diagnostic.GetProperty("code").GetString());
            Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
            Assert.Equal(MisplacedMessage, diagnostic.GetProperty("message").GetString());
            var location = diagnostic.GetProperty("location");
            Assert.Equal(4, location.GetProperty("line").GetInt32());
            Assert.Equal(5, location.GetProperty("column").GetInt32());
            Assert.Equal(1, location.GetProperty("length").GetInt32());
            Assert.DoesNotContain(DiagnosticCode.UnexpectedToken, result.StdOut + result.StdErr);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertMisplaced(Diagnostic diagnostic, string source)
    {
        Assert.Equal(DiagnosticCode.EffectRowMisplaced, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MisplacedMessage, diagnostic.Message);
        Assert.Equal(source.IndexOf('!'), diagnostic.Span.Start);
        Assert.Equal(4, diagnostic.Span.Line);
        Assert.Equal(5, diagnostic.Span.Column);
        Assert.Equal(1, diagnostic.Span.Length);
    }

    private static (ModuleNode Module, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        return (module, diagnostics);
    }
}
