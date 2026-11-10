using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeEffectSuggestionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("    ! // pure row\n")]
    [InlineData("    ! alloc // retained code\n")]
    public void MultipleMissingCodes_KeepNativeSeparatorsAndRowComments(string row)
    {
        var source = "module Native\n  func Echo () → void\n" + row
            + "    System.Console.ReadLine()\n    System.Console.Write(\"effect\")\n";
        var compiled = Program.Compile(source, "native.calr");
        var diagnostic = Assert.Single(compiled.Diagnostics.DiagnosticsWithFixes,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var authorized = new DiagnosticWithFix(diagnostic.Code, diagnostic.Message, diagnostic.Span,
            new SuggestedFix(diagnostic.Fix.Description, diagnostic.Fix.Edits, isSafeToApply: true));
        var amended = DiagnosticFixes.Apply(source, source, "native.calr", [authorized], out _);
        var final = Program.Compile(amended, "native.calr");
        Assert.False(final.HasErrors, string.Join("\n", final.Diagnostics));
        if (row.Contains("//", StringComparison.Ordinal))
            Assert.Contains(row[row.IndexOf("//", StringComparison.Ordinal)..].TrimEnd(), amended);
        Assert.Equal(2, compiled.Diagnostics.Count(diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task KnownEffects_OfferApplicableNativeEditsWithoutAutomaticPolicyChanges(
        bool existingRow, bool crlf)
    {
        var source = "module Native\n  func Echo (int:value) → int\n"
            + (existingRow ? "    ! alloc // keep this row comment\n" : "")
            + "    // keep the contract comment\n"
            + "    requires value >= 0\n"
            + "    => result == value\n"
            + "    System.Console.Write(\"effect\")\n"
            + "    return value";
        if (crlf)
            source = source.Replace("\n", "\r\n", StringComparison.Ordinal);
        var compiled = Program.Compile(source, "native.calr");
        Assert.True(compiled.HasErrors);
        var diagnostic = Assert.Single(compiled.Diagnostics.DiagnosticsWithFixes,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.False(diagnostic.Fix.IsSafeToApply);
        Assert.DoesNotContain("§", diagnostic.Fix.Description);
        Assert.All(diagnostic.Fix.Edits, edit => Assert.DoesNotContain("§", edit.NewText));

        var automatic = DiagnosticFixes.Apply(source, source, "native.calr",
            compiled.Diagnostics.DiagnosticsWithFixes, out var automaticallyApplied);
        Assert.Equal(source, automatic);
        Assert.Equal(0, automaticallyApplied);
        var response = await new FixTool().ExecuteAsync(JsonSerializer.SerializeToElement(new { source }));
        Assert.True(response.IsError);
        using var output = JsonDocument.Parse(response.Content[0].Text!);
        Assert.Equal(source, output.RootElement.GetProperty("fixedSource").GetString());
        Assert.Equal(0, output.RootElement.GetProperty("fixCount").GetInt32());

        // A caller can explicitly authorize a declaration change, unlike safe repair.
        var authorized = new DiagnosticWithFix(diagnostic.Code, diagnostic.Message, diagnostic.Span,
            new SuggestedFix(diagnostic.Fix.Description, diagnostic.Fix.Edits, isSafeToApply: true));
        var amended = DiagnosticFixes.Apply(source, source, "native.calr", [authorized], out var applied);
        Assert.Equal(1, applied);
        Assert.Contains("// keep the contract comment", amended);
        if (existingRow)
            Assert.Contains("! cw, alloc // keep this row comment", amended);
        else
            Assert.Contains("    ! cw" + (crlf ? "\r\n" : "\n") + "    requires", amended);
        var final = Program.Compile(amended, "native.calr");
        Assert.False(final.HasErrors, string.Join("\n", final.Diagnostics));
        Assert.Equal("7", InteropScope.RefOutImplementationTests.Run(final.GeneratedCode + """

            public static class Probe
            {
                public static object Run() => GlobalModule.Echo(7);
            }
            """));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void MissingNestedRow_IsInsertedInsideTheMethodBeforeContracts(bool nested, int indentation)
    {
        var unit = new string(' ', indentation);
        var owner = nested ? unit + "class Box\n" : "";
        var indent = nested ? unit + unit : unit;
        var source = $"module Native\n{owner}{indent}func Echo public (int:value) → int\n"
            + $"{indent}{unit}requires value >= 0\n"
            + $"{indent}{unit}System.Console.Write(value)\n"
            + $"{indent}{unit}return value\n";
        var compiled = Program.Compile(source, "native.calr");
        var diagnostic = Assert.Single(compiled.Diagnostics.DiagnosticsWithFixes,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var edit = Assert.Single(diagnostic.Fix.Edits);
        Assert.Equal(indent + unit + "! cw\n", edit.NewText);
        var authorized = new DiagnosticWithFix(diagnostic.Code, diagnostic.Message, diagnostic.Span,
            new SuggestedFix(diagnostic.Fix.Description, edit, isSafeToApply: true));
        var amended = DiagnosticFixes.Apply(source, source, "native.calr", [authorized], out _);
        var final = Program.Compile(amended, "native.calr");
        Assert.False(final.HasErrors, string.Join("\n", final.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownConstructorEffects_RemainErrorsWithoutAnInventedDeclaration(bool existingRow)
    {
        var source = "module Native\n  func Make () → object\n"
            + (existingRow ? "    ! alloc\n" : "")
            + "    return new object()\n";
        var compiled = Program.Compile(source);
        Assert.True(compiled.HasErrors);
        var diagnostic = Assert.Single(compiled.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains("unknown is not a declarable effect", diagnostic.Message);
        Assert.DoesNotContain(compiled.Diagnostics.DiagnosticsWithFixes,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var response = await new FixTool().ExecuteAsync(JsonSerializer.SerializeToElement(new { source }));
        Assert.True(response.IsError);
        using var output = JsonDocument.Parse(response.Content[0].Text!);
        Assert.Equal(source, output.RootElement.GetProperty("fixedSource").GetString());
        Assert.Equal(0, output.RootElement.GetProperty("fixCount").GetInt32());
        Assert.DoesNotContain("§", response.Content[0].Text!);
    }
}
