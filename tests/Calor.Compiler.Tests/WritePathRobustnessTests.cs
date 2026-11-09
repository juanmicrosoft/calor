using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Phase 1 item 5 — write-path robustness. Covers:
///   1. Fault-tolerant indentation diagnostics with machine-applicable fixes
///      (tabs, mixed whitespace, non-standard widths, misaligned dedents,
///      misaligned else clauses).
///   2. <c>SourceHealer</c> (calor format --heal): legacy-error preservation,
///      indentation re-derivation, chain-clause re-alignment, idempotence.
///   3. MCP calor_check refuses heuristic-only repairs.
/// </summary>
public class WritePathRobustnessTests
{
    [Fact]
    public void CanonicalHealing_AlignsWordClausesAndReportsAmbiguity()
    {
        const string source = "module M\n    func F () → int\n        if true\n            return 1\n            else\n            return 2";
        var healer = new SourceHealer();
        var healed = healer.Heal(source);
        Assert.Equal("module M\n  func F () → int\n    if true\n      return 1\n    else\n      return 2", healed);
        Assert.Contains(healer.Ambiguities, ambiguity => ambiguity.Line == 6);
        Assert.Equal(healed, healer.Heal(healed));
        Assert.Empty(healer.Ambiguities);
    }

    [Fact]
    public void CanonicalHealing_DoesNotEraseLegacyCloserErrors()
    {
        const string source = "module M\n  func F () → int\n    return 1\n§/M";
        var healed = new SourceHealer().Heal(source);
        Assert.Contains("§/M", healed);
        var diagnostics = new DiagnosticBag();
        new Lexer(healed, diagnostics).TokenizeAll();
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, Assert.Single(diagnostics.Errors).Code);
    }

    [Fact]
    public void CanonicalHealing_PreservesRawUtf8BytesAndEscapedStringText()
    {
        const string payload = "§/M\r\nif (true) { /* 😀 */ }\r\n/// else";
        var source = $"module M\n    raw member {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n    func F () → string\n        return \"§/M \\${{0}}\"";
        var healed = new SourceHealer().Heal(source);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(healed, diagnostics).TokenizeAll();
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(payload, Assert.IsType<RawCSharpFrame>(tokens.Single(token => token.Value is RawCSharpFrame).Value).Code);
        Assert.Contains("return \"§/M \\${0}\"", healed);
        Assert.Equal(healed, new SourceHealer().Heal(healed));
    }

    [Fact]
    public void CanonicalHealing_MultilineStringsRemainByteExact()
    {
        const string literal = "\"\"\"first\r\n       else\r\n§/M ${n}\r\nlast\"\"\"";
        var source = "module M\n    func F (int:n) → string\n        return " + literal;
        var healed = new SourceHealer().Heal(source);
        Assert.Equal("module M\n  func F (int:n) → string\n    return " + literal, healed);
        Assert.Equal(healed, new SourceHealer().Heal(healed));
    }

    [Fact]
    public void CanonicalHealing_DoTailAlignsWithoutCapturingNestedWhileLoops()
    {
        const string source = "module M\n    func F () → void\n        do\n            while true\n                break\n            while false\n        return";
        const string expected = "module M\n  func F () → void\n    do\n      while true\n        break\n    while false\n    return";
        var healer = new SourceHealer();
        Assert.Equal(expected, healer.Heal(source));
        Assert.Equal(expected, healer.Heal(expected));
    }

    private const string CanonicalFizzBuzz = """
        module FizzBuzz
          func Main () → void
            ! cw
            for i from 1 to 100 step 1
              if i % 15 == 0
                System.Console.WriteLine("FizzBuzz")
              else if i % 3 == 0
                System.Console.WriteLine("Fizz")
              else if i % 5 == 0
                System.Console.WriteLine("Buzz")
              else
                System.Console.WriteLine(i)
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
        var source = "module T\n\tfunc Main () → void\n\t\tSystem.Console.WriteLine(\"x\")";

        var diagnostics = ParseSource(source);

        Assert.False(diagnostics.HasErrors);
        var warning = Assert.Single(diagnostics.Warnings,
            d => d.Code == DiagnosticCode.TabIndentation);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.TabIndentation);
        Assert.Equal(2, withFix.Fix.Edits.Count);
        Assert.Contains("2 line(s)", warning.Message);

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"x\")", healed);

        var recheck = ParseSource(healed);
        Assert.False(recheck.HasErrors);
        Assert.Empty(recheck.Warnings);
    }

    [Fact]
    public void MixedIndentation_ReportsError_WithFixThatHeals()
    {
        // Body line indented with tab + 2 spaces (3 columns, mixed).
        var source = "module T\n  func Main () → void\n\t  System.Console.WriteLine(\"x\")";

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MixedIndentation);
        Assert.Single(withFix.Fix.Edits);

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"x\")", healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void NonStandardIndentWidth_FourSpaceFile_FixNormalizesInOnePass()
    {
        var source = "module T\n    func Main () → void\n        System.Console.WriteLine(\"a\")\n        System.Console.WriteLine(\"b\")";

        var diagnostics = ParseSource(source);

        Assert.False(diagnostics.HasErrors); // 4-space parses; canonical width is advisory
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.NonStandardIndentWidth);
        Assert.Equal(3, withFix.Fix.Edits.Count); // function line + both statement siblings

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"a\")\n    System.Console.WriteLine(\"b\")", healed);

        var recheck = ParseSource(healed);
        Assert.False(recheck.HasErrors);
        Assert.Empty(recheck.Warnings);
    }

    [Fact]
    public void DedentMismatch_ReportsError_WithFixSnappingToNearestLevel()
    {
        // Third statement dedents to column 3 — matches no enclosing level
        // (levels are 0/2/4). The fix snaps to the deeper level (4).
        var source = "module T\n  func Main () → void\n    System.Console.WriteLine(\"a\")\n   System.Console.WriteLine(\"b\")";

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
        // Else-if dedented to the loop-body level (statement position): the
        // if-chain closed before the clause was seen.
        var source = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    for i from 1 to 10 step 1",
            "      if i == 1",
            "        System.Console.WriteLine(\"a\")",
            "    else if i == 2",
            "        System.Console.WriteLine(\"b\")");

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        var withFix = Assert.Single(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MisalignedElseClause);
        Assert.Equal("      ", withFix.Fix.Edits[0].NewText); // aligned with if at column 7

        var healed = ApplyAllFixes(source, diagnostics);
        Assert.False(ParseSource(healed).HasErrors,
            string.Join("\n", ParseSource(healed).Errors.Select(e => e.Message)));
    }

    [Fact]
    public void MisalignedElseClause_InFunctionWithNoIf_EmitsNoFix()
    {
        // Reviewer probe: f1 contains an if at the same column as f2's stray
        // else-if. Before the _lastIfToken function-boundary reset, the fix
        // referenced f1's if — and because the clause already sat at that
        // column, applying it was a no-op: apply → identical file → same
        // error, forever (a probe-confirmed infinite agent loop).
        var source = string.Join('\n',
            "module T",
            "  func A () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "  func B () → void",
            "    else if 1 == 2",
            "      System.Console.WriteLine(\"b\")");

        var diagnostics = ParseSource(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.MisalignedElseClause);
        // No fix: there is no if in this function to align with.
        Assert.DoesNotContain(diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.MisalignedElseClause);
    }

    [Fact]
    public void MisalignedElseClause_ClauseAlreadyAtIfColumn_EmitsNoNoOpFix()
    {
        // Same-function variant of the no-op hazard: the stray clause is
        // already at its if's column, but the chain closed early because a
        // statement at the same column ended the if. Re-indenting to the if
        // column would not change the file, so no fix may be emitted.
        var source = string.Join('\n',
            "module T",
            "  func A () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "    System.Console.WriteLine(\"x\")",
            "    else if 1 == 2",
            "      System.Console.WriteLine(\"b\")");

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
        var source = "module T\n\tfunc Main () → void\n\t\tSystem.Console.WriteLine(\"x\")";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"x\")", healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_FourSpaceIndentation_NormalizesToTwoSpaces()
    {
        var source = "module T\n    func Main () → void\n        System.Console.WriteLine(\"a\")\n        System.Console.WriteLine(\"b\")";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"a\")\n    System.Console.WriteLine(\"b\")", healed);
    }

    [Fact]
    public void Heal_RetiredStructuralClosers_RemainExplicitErrors()
    {
        var source = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    System.Console.WriteLine(\"x\")",
            "  §/F",
            "§/M");

        var healed = new SourceHealer().Heal(source);

        Assert.Equal(source, healed);
        Assert.Equal(2, ParseSource(healed).Errors.Count(error => error.Code == DiagnosticCode.UnknownSectionMarker));
    }

    [Fact]
    public void Heal_ElseClauseAtBodyLevel_RealignsWithIf()
    {
        var source = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "      else if 1 == 2", // written at body level
            "        System.Console.WriteLine(\"b\")");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "    else if 1 == 2",
            "      System.Console.WriteLine(\"b\")");
        Assert.Equal(expected, healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_ElseClauseDedentedTooFar_RealignsWithIf()
    {
        var source = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "else if 1 == 2", // dedented to module level
            "      System.Console.WriteLine(\"b\")");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "    else if 1 == 2",
            "      System.Console.WriteLine(\"b\")");
        Assert.Equal(expected, healed);
        Assert.False(ParseSource(healed).HasErrors);
    }

    [Fact]
    public void Heal_AmbiguousTrailingStatement_IsReportedWithOriginalLine()
    {
        // Reviewer probe: the call printing "b" sits at the same column as the body-level
        // else-if above it. Heal keeps it inside the clause body — a control-flow
        // GUESS that must be surfaced, not silently applied.
        var source = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "      else if 1 == 2", // written at body level
            "      System.Console.WriteLine(\"b\")"); // ambiguous: clause body or statement after the chain?

        var healer = new SourceHealer();
        var healed = healer.Heal(source);

        var ambiguity = Assert.Single(healer.Ambiguities);
        Assert.Equal(6, ambiguity.Line);
        Assert.Contains("'else'", ambiguity.Message);
        Assert.Contains("line 5", ambiguity.Message);

        // The guess itself: the call printing "b" healed into the else-if body.
        var expected = string.Join('\n',
            "module T",
            "  func Main () → void",
            "    if 1 == 1",
            "      System.Console.WriteLine(\"a\")",
            "    else if 1 == 2",
            "      System.Console.WriteLine(\"b\")");
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
    public void Heal_UnclosedSignatureParenthesis_DoesNotVerbatimizeRestOfFile()
    {
        // Reviewer probe: `func Main ( → void` leaves the signature parenthesis
        // unclosed. Before the continuation cap, every following line was
        // classified as bracket continuation and heal silently no-opped on
        // the whole file. A continuation must end at a canonical structural-word head.
        var source = string.Join('\n',
            "module T",
            "  func Main ( → void", // missing closing )
            "      return",         // over-indented: must still normalize
            "      return");

        var healed = new SourceHealer().Heal(source);

        var expected = string.Join('\n',
            "module T",
            "  func Main ( → void",
            "    return",
            "    return");
        Assert.Equal(expected, healed);
    }

    [Fact]
    public void Heal_BracketContinuation_IsCappedAtTenLines()
    {
        // An unclosed bracket followed by many non-header lines: only the first
        // ten remain verbatim continuations; past the cap, lines are
        // structural again (an unclosed bracket is assumed to be a typo).
        var continuation = Enumerable.Range(1, 11).Select(n => $"      line{n}");
        var source = string.Join('\n', new[]
        {
            "module T",
            "  let string:x = string.Concat(\"a\"", // unclosed ( — continuation begins
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
        var source = "module T\r\n  func Main () → void   \r\n    System.Console.WriteLine(\"x\")\t\r\n";

        var healed = new SourceHealer().Heal(source);

        Assert.Equal("module T\n  func Main () → void\n    System.Console.WriteLine(\"x\")\n", healed);
    }

    [Fact]
    public void Heal_RawBlockContent_IsPreservedVerbatim()
    {
        const string payload = "   int x = 1;   \n\tConsole.WriteLine(x);\n";
        var length = System.Text.Encoding.UTF8.GetByteCount(payload);
        var source = $"module T\n    func Main () → void\n        raw statement {length}\n{payload}";

        var healed = new SourceHealer().Heal(source);

        // Structure normalized, raw payload untouched.
        Assert.Contains("\n   int x = 1;   \n", healed);
        Assert.Contains("\n\tConsole.WriteLine(x);\n", healed);
        Assert.StartsWith($"module T\n  func Main () → void\n    raw statement {length}\n", healed);
    }

    [Theory]
    [InlineData("module T\n\tfunc Main () → void\n\t\tSystem.Console.WriteLine(\"x\")")]
    [InlineData("module T\n    func Main () → void\n        System.Console.WriteLine(\"a\")\n        System.Console.WriteLine(\"b\")")]
    [InlineData("module T\n  func Main () → void\n    System.Console.WriteLine(\"x\")\n  §/F\n§/M")]
    [InlineData("module T\n  func Main () → void\n    if 1 == 1\n      System.Console.WriteLine(\"a\")\n      else if 1 == 2\n        System.Console.WriteLine(\"b\")")]
    [InlineData("module T\n  func Main () → void\n    if 1 == 1\n      System.Console.WriteLine(\"a\")\nelse if 1 == 2\n      System.Console.WriteLine(\"b\")")]
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
        // Four-space indentation and a body-level else-if with an ambiguous following statement.
        var mangled = string.Join('\n',
            "module FizzBuzz",
            "    func Main () → void",
            "        ! cw",
            "        for i from 1 to 100 step 1",
            "            if i % 15 == 0",
            "                System.Console.WriteLine(\"FizzBuzz\")",
            "                else if i % 3 == 0",
            "                System.Console.WriteLine(\"Fizz\")",
            "            else",
            "                System.Console.WriteLine(i)");

        var healed = new SourceHealer().Heal(mangled);

        var diagnostics = ParseSource(healed);
        Assert.False(diagnostics.HasErrors,
            $"healed output should parse:\n{healed}\n---\n{string.Join("\n", diagnostics.Errors.Select(e => e.Message))}");
        Assert.Equal(healed, new SourceHealer().Heal(healed));
        Assert.DoesNotContain("§/F", healed);
        Assert.DoesNotContain("§/M", healed);
    }

    // ===== 3. MCP calor_check safe patches =====

    [Fact]
    public async Task CheckTool_DiagnoseApply_LeavesAmbiguousDedentsForExplicitHeal()
    {
        // Choosing either enclosing indent level changes control flow. Keep
        // the suggestion, but reserve the heuristic repair for explicit heal.
        var source = string.Join('\n',
            "module T",
            "  func A () → void",
            "    ! cw",
            "    System.Console.WriteLine(\"a\")",
            "   System.Console.WriteLine(\"b\")",
            "  func B () → void",
            "    ! cw",
            "    System.Console.WriteLine(\"c\")",
            "   System.Console.WriteLine(\"d\")");

        var tool = new Calor.Compiler.Mcp.Tools.CheckTool();
        var args = System.Text.Json.JsonDocument.Parse(
            $"{{\"action\": \"diagnose\", \"apply\": true, \"source\": {System.Text.Json.JsonSerializer.Serialize(source)}}}").RootElement;
        var toolResult = await tool.ExecuteAsync(args);

        var doc = System.Text.Json.JsonDocument.Parse(toolResult.Content![0].Text!);
        Assert.False(doc.RootElement.TryGetProperty("healed", out _));

        var fixedSource = doc.RootElement.GetProperty("fixedSource").GetString();
        Assert.Equal(source, fixedSource);
        Assert.Equal(0, doc.RootElement.GetProperty("fixesApplied").GetInt32());
        Assert.True(ParseSource(fixedSource!).HasErrors);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errorCount").GetInt32() > 0);
        var diagnostic = doc.RootElement.GetProperty("diagnostics").EnumerateArray()
            .First(d => d.GetProperty("code").GetString() == DiagnosticCode.MixedIndentation);
        Assert.True(diagnostic.TryGetProperty("suggestion", out _));
        Assert.False(diagnostic.GetProperty("fix").GetProperty("isSafeToApply").GetBoolean());
        Assert.True(toolResult.IsError);
        Assert.False(ParseSource(new SourceHealer().Heal(source)).HasErrors);
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
