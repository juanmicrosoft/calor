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
        "An effect row must be on the same line as the type it annotates. "
        + "Move it onto the end of the 'count' field line, or — if this is meant to be "
        + "the function's own effect row — onto its own line at the top of the function body.";

    private const string DuplicateMessage =
        "A type can carry only one effect row. Combine the effects into a single "
        + "row — ! cw, net — or, if this is meant to be the function's own effect "
        + "row, move it onto its own line at the top of the function body.";

    [Theory]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("binding")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void DuplicateNativeTypeRows_RetainOnlyTheFirstRowAndTheSharedDiagnostic(string surface)
    {
        var source = DuplicateSource(surface);
        var (module, diagnostics) = Parse(source);
        var error = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.EffectRowMisplaced, error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DuplicateMessage, error.Message);
        Assert.Equal(source.IndexOf("! fs:r", StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(1, error.Span.Length);
        var first = TypeRow(module, surface);
        Assert.Single(first.Effects);
        Assert.Contains("io", first.Effects.Keys);
        if (surface is "field" or "property")
            Assert.Equal("after", Assert.Single(module.Classes).Fields.Last().Name);
        Assert.Equal("After", module.Functions.Last().Name);
        Assert.All(module.Functions, function => Assert.Empty(function.Effects!.Effects));
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Equal(DiagnosticCode.EffectRowMisplaced, Assert.Single(compiled.Diagnostics.Errors).Code);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("binding")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void SingleNativeTypeRows_DoNotConsumeDistinctFunctionRows(string surface)
    {
        var source = DuplicateSource(surface).Replace(" ! fs:r", "", StringComparison.Ordinal);
        var (module, diagnostics) = Parse(source);
        Assert.Empty(diagnostics);
        Assert.Contains("io", TypeRow(module, surface).Effects.Keys);
        Assert.All(module.Functions, function => Assert.Empty(function.Effects!.Effects));
        if (surface == "parameter")
            Assert.Equal(["next", "value"], module.Functions[0].Parameters.Select(parameter => parameter.Name));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics));
    }

    [Theory]
    [InlineData("field int:other private")]
    [InlineData("func Next () → int\n      !\n      return 7")]
    public void MalformedMisplacedNativeCode_DoesNotConsumeTheFollowingMember(string member)
    {
        var source = $$"""
            module MalformedRow
              class Counter
                field int:count private
                ! fs:
                {{member}}
              func After () → int
                !
                return 9
            """;
        var (module, diagnostics) = Parse(source);
        var misplaced = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.EffectRowMisplaced));
        AssertMisplaced(misplaced, source);
        var malformed = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken));
        Assert.Equal(DiagnosticSeverity.Error, malformed.Severity);
        Assert.Equal(source.IndexOf("fs:", StringComparison.Ordinal) + 2, malformed.Span.Start);
        Assert.Equal(1, malformed.Span.Length);
        Assert.Equal(2, diagnostics.Count);
        var counter = Assert.Single(module.Classes);
        Assert.Null(counter.Fields[0].Row);
        if (member.StartsWith("field", StringComparison.Ordinal))
            Assert.Equal(["count", "other"], counter.Fields.Select(field => field.Name));
        else
            Assert.Equal("Next", Assert.Single(counter.Methods).Name);
        Assert.Equal("After", Assert.Single(module.Functions).Name);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("! cw ! fs:")]
    [InlineData("! cw, fs:")]
    public void MalformedNativeTypeCode_RejectsRatherThanAcceptingAPartialCode(string row)
    {
        var source = $$"""
            module MalformedTypeRow
              class Counter
                field Func<int>:next private {{row}}
                field int:after private
            """;
        var (module, diagnostics) = Parse(source);
        var malformed = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken));
        Assert.Equal(source.IndexOf("fs:", StringComparison.Ordinal) + 2, malformed.Span.Start);
        var counter = Assert.Single(module.Classes);
        Assert.Equal(["next", "after"], counter.Fields.Select(field => field.Name));
        Assert.Single(counter.Fields[0].Row!.Effects);
        Assert.Contains("io", counter.Fields[0].Row!.Effects.Keys);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void MalformedParameterCode_LeavesTheClosingParenthesisForItsOwner()
    {
        const string source = """
            module MalformedParameter
              func Probe (Func<int>:next ! fs:) → int
                !
                return 7
              func After () → int
                !
                return 9
            """;
        var (module, diagnostics) = Parse(source);
        var error = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.UnexpectedToken, error.Code);
        Assert.Equal(source.IndexOf("fs:", StringComparison.Ordinal) + 2, error.Span.Start);
        Assert.Equal(["Probe", "After"], module.Functions.Select(function => function.Name));
        Assert.Empty(module.Functions[0].Parameters[0].Row!.Effects);
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        Assert.Equal(DiagnosticCode.UnexpectedToken, Assert.Single(compiled.Diagnostics.Errors).Code);
    }

    private static string DuplicateSource(string surface) => surface switch
    {
        "field" => """
            module Duplicate
              class Counter
                field Func<int>:next private ! cw ! fs:r
                field int:after private
              func After () → int
                !
                return 9
            """,
        "property" => """
            module Duplicate
              class Counter
                property Func<int>:Next ! cw ! fs:r
                  get
                  set
                field int:after private
              func After () → int
                !
                return 9
            """,
        "binding" => """
            module Duplicate
              func Probe () → int
                !
                let Func<int>:next ! cw ! fs:r = () => 1
                return 7
              func After () → int
                !
                return 9
            """,
        "parameter" => """
            module Duplicate
              func Probe (Func<int>:next ! cw ! fs:r, int:value) → int
                !
                return value
              func After () → int
                !
                return 9
            """,
        "return" => """
            module Duplicate
              func Probe (Func<int>:next ! cw) → Func<int> ! cw ! fs:r
                !
                return next
              func After () → int
                !
                return 9
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(surface))
    };

    private static EffectsNode TypeRow(ModuleNode module, string surface) => surface switch
    {
        "field" => module.Classes[0].Fields[0].Row!,
        "property" => module.Classes[0].Properties[0].Row!,
        "binding" => Assert.IsType<BindStatementNode>(module.Functions[0].Body[0]).Row!,
        "parameter" => module.Functions[0].Parameters[0].Row!,
        "return" => module.Functions[0].Output!.Row!,
        _ => throw new ArgumentOutOfRangeException(nameof(surface))
    };

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

    // A row line directly under the class header, before any field, belongs to no field. Before
    // 0.30 (D1) it was an unowned row; it is now the type's static-initialization row (a header
    // clause). Either way it must not be read as a separated row of the field below it.
    [Fact]
    public void ClassHeaderRow_IsTheTypeRowAndDoesNotAcquireAFieldPlacementDiagnostic()
    {
        const string source = """
            module Unowned
              class Counter
                ! cw
                field int:count private
            """;
        var (module, diagnostics) = Parse(source);
        Assert.Empty(diagnostics);
        var counter = Assert.Single(module.Classes);
        Assert.NotNull(counter.StaticInitializerEffects);
        Assert.Null(Assert.Single(counter.Fields).Row);
        var compiled = Program.Compile(source);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Message == MisplacedMessage);
        Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics));
    }

    // A class-level row after a method belongs to neither the method (its row is the first line of
    // its body) nor a field; it is a misplaced type row and says so, not the field-placement text.
    [Fact]
    public void ClassRowAfterAMethod_IsAMisplacedTypeRowNotAFieldPlacementDiagnostic()
    {
        const string source = """
            module Unowned
              class Counter
                func Read () → int
                  !
                  return 7
                ! cw
                field int:count private
            """;
        var (module, diagnostics) = Parse(source);
        var error = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.EffectRowMisplaced, error.Code);
        Assert.NotEqual(MisplacedMessage, error.Message);
        Assert.Contains("before the first member", error.Message, StringComparison.Ordinal);
        var counter = Assert.Single(module.Classes);
        Assert.Null(counter.StaticInitializerEffects);
        Assert.Null(Assert.Single(counter.Fields).Row);
        Assert.Empty(Assert.Single(counter.Methods).Effects!.Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidFieldRow_CliJsonRejectsWithTheSameDiagnosticAndNoGeneratedFile(bool duplicate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-native-field-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "BadRow.calr");
            var output = Path.Combine(directory, "BadRow.g.cs");
            File.WriteAllText(input, duplicate ? DuplicateSource("field") : BadRow);
            var result = CliTestHarness.RunCli(directory, "--no-telemetry", "--input", input,
                "--output", output, "--format", "json");
            Assert.Equal(1, result.ExitCode);
            using var json = JsonDocument.Parse(result.StdOut);
            var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(DiagnosticCode.EffectRowMisplaced, diagnostic.GetProperty("code").GetString());
            Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
            Assert.Equal(duplicate ? DuplicateMessage : MisplacedMessage, diagnostic.GetProperty("message").GetString());
            var location = diagnostic.GetProperty("location");
            Assert.Equal(duplicate ? 3 : 4, location.GetProperty("line").GetInt32());
            Assert.Equal(duplicate ? 39 : 5, location.GetProperty("column").GetInt32());
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
