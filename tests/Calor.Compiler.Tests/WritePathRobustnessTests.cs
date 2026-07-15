using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Phase 1 item 5 — write-path robustness. Covers:
///   1. Fault-tolerant indentation diagnostics with machine-applicable fixes
///      (tabs, mixed whitespace, non-standard widths, misaligned dedents,
///      misaligned §EI/§EL clauses).
///   2. <c>SourceHealer</c> (calor format --heal): closer stripping,
///      indentation re-derivation, chain-clause re-alignment, idempotence.
///   3. The MCP calor_check auto-heal path.
/// </summary>
public class WritePathRobustnessTests
{
    private const string CanonicalFizzBuzz = """
        §M{m1:FizzBuzz}
          §F{f1:Main:pub} () -> void
            §E{cw}
            §L{l1:i:1:100:1}
              §IF{i1} (== (% i 15) 0)
                §P "FizzBuzz"
              §EI (== (% i 3) 0)
                §P "Fizz"
              §EI (== (% i 5) 0)
                §P "Buzz"
              §EL
                §P i
        """;

    private static DiagnosticBag ParseSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        if (!diagnostics.HasErrors)
        {
            var parser = new Parser(tokens, diagnostics);
            parser.Parse();
        }
        return diagnostics;
    }

    /// <summary>
    /// Applies every fix edit in a diagnostic bag to the source (bottom-up so
    /// coordinates stay valid) — mirrors the MCP calor_check apply path.
    /// </summary>
    private static string ApplyAllFixes(string source, DiagnosticBag diagnostics)
    {
        var lines = source.Split('\n').ToList();
        var edits = diagnostics.DiagnosticsWithFixes
            .SelectMany(d => d.Fix.Edits)
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn);

        foreach (var edit in edits)
        {
            Assert.Equal(edit.StartLine, edit.EndLine); // all our fixes are single-line
            var line = lines[edit.StartLine - 1];
            var before = line[..(edit.StartColumn - 1)];
            var after = edit.EndColumn - 1 <= line.Length ? line[(edit.EndColumn - 1)..] : "";
            lines[edit.StartLine - 1] = before + edit.NewText + after;
        }

        return string.Join('\n', lines);
    }

    // ===== 1. Indentation diagnostics with fixes =====

    [Fact]
    public void TabIndentation_ReportsSingleWarning_WithEditsForEveryTabLine()
    {
        var source = "§M{m1:T}\n\t§F{f1:Main:pub} () -> void\n\t\t§P \"x\"";

        var diagnostics = ParseSource(source);

        Assert.False(diagnostics.HasErrors);
        var warning = Assert.Single(diagnostics.Warnings,
            d => d.Code == DiagnosticCode.TabIndentation);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.TabIndentation);
        Assert.Equal(2, withFix.Fix.Edits.Count);
        Assert.Contains("2 line(s)", warning.Message);

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"", healed);

        var recheck = ParseSource(healed);
        Assert.False(recheck.HasErrors);
        Assert.Empty(recheck.Warnings);
    }

    [Fact]
    public void MixedIndentation_ReportsError_WithFixThatHeals()
    {
        // Body line indented with tab + 2 spaces (3 columns, mixed).
        var source = "§M{m1:T}\n  §F{f1:Main:pub} () -> void\n\t  §P \"x\"";

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MixedIndentation);
        Assert.Single(withFix.Fix.Edits);

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"", healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void NonStandardIndentWidth_FourSpaceFile_FixNormalizesInOnePass()
    {
        var source = "§M{m1:T}\n    §F{f1:Main:pub} () -> void\n        §P \"a\"\n        §P \"b\"";

        var diagnostics = ParseSource(source);

        Assert.False(diagnostics.HasErrors); // 4-space parses; canonical width is advisory
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.NonStandardIndentWidth);
        Assert.Equal(3, withFix.Fix.Edits.Count); // §F line + both §P siblings

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"a\"\n    §P \"b\"", healed);

        var recheck = ParseSource(healed);
        Assert.False(recheck.HasErrors);
        Assert.Empty(recheck.Warnings);
    }

    [Fact]
    public void DedentMismatch_ReportsError_WithFixSnappingToNearestLevel()
    {
        // Third statement dedents to column 3 — matches no enclosing level
        // (levels are 0/2/4). The fix snaps to the deeper level (4).
        var source = "§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"a\"\n   §P \"b\"";

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MixedIndentation);
        Assert.Equal("    ", withFix.Fix.Edits[0].NewText);

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void MisalignedElseClause_InStatementPosition_ReportsFixAligningWithIf()
    {
        // §EI dedented to the loop-body level (statement position): the
        // if-chain closed before the clause was seen.
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §L{l1:i:1:10:1}",
            "      §IF{i1} (== i 1)",
            "        §P \"a\"",
            "    §EI (== i 2)",
            "        §P \"b\"");

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MisalignedElseClause);
        Assert.Equal("      ", withFix.Fix.Edits[0].NewText); // aligned with §IF at column 7

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.False(ParseSource(healed).HasErrors,
            string.Join("\n", ParseSource(healed).Errors.Select(e => e.Message)));
    }

    [Fact]
    public void MisalignedElseClause_InFunctionWithNoIf_EmitsNoFix()
    {
        // Reviewer probe: f1 contains a §IF at the same column as f2's stray
        // §EI. Before the _lastIfToken function-boundary reset, the fix
        // referenced f1's §IF — and because the clause already sat at that
        // column, applying it was a no-op: apply → identical file → same
        // error, forever (a probe-confirmed infinite agent loop).
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:A:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "  §F{f2:B:pub} () -> void",
            "    §EI (== 1 2)",
            "      §P \"b\"");

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.MisalignedElseClause);
        // No fix: there is no §IF in this function to align with.
        Assert.DoesNotContain(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MisalignedElseClause);
    }

    [Fact]
    public void MisalignedElseClause_ClauseAlreadyAtIfColumn_EmitsNoNoOpFix()
    {
        // Same-function variant of the no-op hazard: the stray clause is
        // already at its §IF's column, but the chain closed early because a
        // statement at the same column ended the if. Re-indenting to the §IF
        // column would not change the file, so no fix may be emitted.
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:A:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "    §P \"x\"",
            "    §EI (== 1 2)",
            "      §P \"b\"");

        var diagnostics = ParseSource(source);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.MisalignedElseClause);
        Assert.DoesNotContain(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MisalignedElseClause);
    }

    // ===== 2. SourceHealer =====

    [Fact]
    public void Heal_CanonicalFile_IsUnchanged()
    {
        var healer = new SourceHealer();
        Assert.Equal(CanonicalFizzBuzz, healer.Heal(CanonicalFizzBuzz));
    }

    [Fact]
    public void Heal_TabIndentation_NormalizesToTwoSpaces()
    {
        var source = "§M{m1:T}\n\t§F{f1:Main:pub} () -> void\n\t\t§P \"x\"";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"", healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_FourSpaceIndentation_NormalizesToTwoSpaces()
    {
        var source = "§M{m1:T}\n    §F{f1:Main:pub} () -> void\n        §P \"a\"\n        §P \"b\"";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"a\"\n    §P \"b\"", healed);
    }

    [Fact]
    public void Heal_StripsStructuralClosers_AndDropsEmptiedLines()
    {
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §P \"x\"",
            "  §/F{f1}",
            "§/M{m1}");

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"", healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_ElseClauseAtBodyLevel_RealignsWithIf()
    {
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "      §EI (== 1 2)", // written at body level
            "        §P \"b\"");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "    §EI (== 1 2)",
            "      §P \"b\"");
        Assert.Equal(expected, healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_ElseClauseDedentedTooFar_RealignsWithIf()
    {
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "§EI (== 1 2)", // dedented to module level
            "      §P \"b\"");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "    §EI (== 1 2)",
            "      §P \"b\"");
        Assert.Equal(expected, healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_AmbiguousTrailingStatement_IsReportedWithOriginalLine()
    {
        // Reviewer probe: §P "b" sits at the same column as the body-level
        // §EI above it. Heal keeps it inside the clause body — a control-flow
        // GUESS that must be surfaced, not silently applied.
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "      §EI (== 1 2)", // written at body level
            "      §P \"b\"");    // ambiguous: clause body or statement after the chain?

        var healer = new SourceHealer();
        var healed = healer.Heal(source);

        var ambiguity = Assert.Single(healer.Ambiguities);
        Assert.Equal(6, ambiguity.Line);
        Assert.Contains("§EI", ambiguity.Message);
        Assert.Contains("line 5", ambiguity.Message);

        // The guess itself: §P "b" healed into the else-if body.
        var expected = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub} () -> void",
            "    §IF{i1} (== 1 1)",
            "      §P \"a\"",
            "    §EI (== 1 2)",
            "      §P \"b\"");
        Assert.Equal(expected, healed);
    }

    [Fact]
    public void Heal_UnambiguousFile_ReportsNoAmbiguities()
    {
        var healer = new SourceHealer();
        healer.Heal(CanonicalFizzBuzz);
        Assert.Empty(healer.Ambiguities);
    }

    [Fact]
    public void Heal_UnclosedHeaderBrace_DoesNotVerbatimizeRestOfFile()
    {
        // Reviewer probe: `§F{f1:Main:pub ( )` leaves the header brace
        // unclosed. Before the continuation cap, every following line was
        // classified as bracket continuation and heal silently no-opped on
        // the whole file. A continuation must end at a line starting with §.
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub ( ) -> void", // missing closing }
            "      §P \"x\"",               // over-indented: must still normalize
            "      §P \"y\"");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:Main:pub ( ) -> void",
            "    §P \"x\"",
            "    §P \"y\"");
        Assert.Equal(expected, healed);
    }

    [Fact]
    public void Heal_BracketContinuation_IsCappedAtTenLines()
    {
        // An unclosed bracket followed by many non-§ lines: only the first
        // ten remain verbatim continuations; past the cap, lines are
        // structural again (an unclosed bracket is assumed to be a typo).
        var continuation = Enumerable.Range(1, 11).Select(n => $"      line{n}");
        var source = string.Join('\n', new[]
        {
            "§M{m1:T}",
            "  §B{x:str} (concat \"a\"", // unclosed ( — continuation begins
        }.Concat(continuation));

        var healed = new SourceHealer().Heal(source).Split('\n');

        // Lines 3..12 (continuations 1..10) are verbatim: indent preserved.
        Assert.Equal("      line1", healed[2]);
        Assert.Equal("      line10", healed[11]);
        // Line 13 (continuation 11) is past the cap: releveled.
        Assert.Equal("    line11", healed[12]);
    }

    [Fact]
    public void Heal_TrailingWhitespaceAndCrlf_Normalized()
    {
        var source = "§M{m1:T}\r\n  §F{f1:Main:pub} () -> void   \r\n    §P \"x\"\t\r\n";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"\n", healed);
    }

    [Fact]
    public void Heal_RawBlockContent_IsPreservedVerbatim()
    {
        var source = string.Join('\n',
            "§M{m1:T}",
            "    §F{f1:Main:pub} () -> void",
            "        §RAW",
            "   int x = 1;   ",
            "\tConsole.WriteLine(x);",
            "        §/RAW");

        var healed = new SourceHealer().Heal(source);

        // Structure normalized, raw payload untouched.
        Assert.Contains("\n   int x = 1;   \n", healed);
        Assert.Contains("\n\tConsole.WriteLine(x);\n", healed);
        Assert.StartsWith("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §RAW\n", healed);
    }

    [Theory]
    [InlineData("§M{m1:T}\n\t§F{f1:Main:pub} () -> void\n\t\t§P \"x\"")]
    [InlineData("§M{m1:T}\n    §F{f1:Main:pub} () -> void\n        §P \"a\"\n        §P \"b\"")]
    [InlineData("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §P \"x\"\n  §/F{f1}\n§/M{m1}")]
    [InlineData("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §IF{i1} (== 1 1)\n      §P \"a\"\n      §EI (== 1 2)\n        §P \"b\"")]
    [InlineData("§M{m1:T}\n  §F{f1:Main:pub} () -> void\n    §IF{i1} (== 1 1)\n      §P \"a\"\n§EI (== 1 2)\n      §P \"b\"")]
    public void Heal_IsIdempotent(string source)
    {
        var healer = new SourceHealer();
        var once = healer.Heal(source);
        var twice = healer.Heal(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Heal_MangledFizzBuzz_RestoresCompilingFile()
    {
        // 4-space indentation, tab lines, a body-level §EI, and legacy closers.
        var mangled = string.Join('\n',
            "§M{m1:FizzBuzz}",
            "    §F{f1:Main:pub} () -> void",
            "        §E{cw}",
            "        §L{l1:i:1:100:1}",
            "            §IF{i1} (== (% i 15) 0)",
            "                §P \"FizzBuzz\"",
            "                §EI (== (% i 3) 0)",
            "                §P \"Fizz\"",
            "            §EL",
            "                §P i",
            "        §/L{l1}",
            "    §/F{f1}",
            "§/M{m1}");

        var healed = new SourceHealer().Heal(mangled);

        var diagnostics = ParseSource(healed);
        Assert.False(diagnostics.HasErrors,
            $"healed output should parse:\n{healed}\n---\n{string.Join("\n", diagnostics.Errors.Select(e => e.Message))}");
        Assert.Equal(healed, new SourceHealer().Heal(healed));
        Assert.DoesNotContain("§/F", healed);
        Assert.DoesNotContain("§/M", healed);
    }

    // ===== 3. MCP calor_check auto-heal =====

    [Fact]
    public async Task CheckTool_DiagnoseApply_HealsSourceWhenFixEditsAreNotEnough()
    {
        // Two dedent mismatches: only the first is reported with a fix (the
        // lexer gates indentation errors to one per file), so applying fix
        // edits alone still leaves a broken file — only the source healer
        // can finish the repair.
        var source = string.Join('\n',
            "§M{m1:T}",
            "  §F{f1:A:pub} () -> void",
            "    §E{cw}",
            "    §P \"a\"",
            "   §P \"b\"",
            "  §F{f2:B:pub} () -> void",
            "    §E{cw}",
            "    §P \"c\"",
            "   §P \"d\"");

        var tool = new Calor.Compiler.Mcp.Tools.CheckTool();
        var args = System.Text.Json.JsonDocument.Parse(
            $"{{\"action\": \"diagnose\", \"apply\": true, \"source\": {System.Text.Json.JsonSerializer.Serialize(source)}}}").RootElement;
        var toolResult = await tool.ExecuteAsync(args);

        var doc = System.Text.Json.JsonDocument.Parse(toolResult.Content![0].Text!);
        Assert.True(doc.RootElement.GetProperty("healed").GetBoolean());

        var fixedSource = doc.RootElement.GetProperty("fixedSource").GetString();
        Assert.NotNull(fixedSource);
        Assert.False(ParseSource(fixedSource!).HasErrors,
            $"healed fixedSource should parse:\n{fixedSource}");

        // When healed==true the response must describe fixedSource, not the
        // pre-heal input: the heal fully repaired this file, so the recheck
        // is clean and the tool result is not an error.
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("errorCount").GetInt32());
        Assert.DoesNotContain(doc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("severity").GetString() == "error");
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public async Task CheckTool_DiagnoseApply_CleanSource_DoesNotHeal()
    {
        var tool = new Calor.Compiler.Mcp.Tools.CheckTool();
        var args = System.Text.Json.JsonDocument.Parse(
            $"{{\"action\": \"diagnose\", \"apply\": true, \"source\": {System.Text.Json.JsonSerializer.Serialize(CanonicalFizzBuzz)}}}").RootElement;
        var toolResult = await tool.ExecuteAsync(args);

        var doc = System.Text.Json.JsonDocument.Parse(toolResult.Content![0].Text!);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("healed", out _));
    }
}
