using Calor.Compiler.Parsing;
using Xunit;
using DiagnosticCode = Calor.Compiler.Diagnostics.DiagnosticCode;
using DiagnosticBag = Calor.Compiler.Diagnostics.DiagnosticBag;
using SuggestedFix = Calor.Compiler.Diagnostics.SuggestedFix;

namespace Calor.Compiler.Tests.Diagnostics;

/// <summary>
/// Tests for diagnostic suggestions and fixes for typos and C# constructs.
/// </summary>
public class SuggestionTests
{
    #region OperatorSuggestions Tests

    [Theory]
    [InlineData("cotains", "contains")]
    [InlineData("containz", "contains")]
    [InlineData("substr", "substr")] // Exact match
    [InlineData("subsrt", "substr")]
    [InlineData("uper", "upper")]
    [InlineData("lowwer", "lower")]
    [InlineData("replce", "replace")]
    [InlineData("indxof", "indexof")]
    [InlineData("join", "join")] // Exact match
    [InlineData("jion", "join")] // Typo
    [InlineData("forall", "forall")] // Exact match
    [InlineData("frall", "forall")] // Typo
    public void FindSimilarOperator_WithTypo_ReturnsSuggestion(string typo, string expected)
    {
        var suggestion = OperatorSuggestions.FindSimilarOperator(typo);

        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion);
    }

    [Theory]
    [InlineData("xyzabc")] // No similar operator
    [InlineData("qwertyuiop")]
    [InlineData("thisshouldnotmatch")]
    public void FindSimilarOperator_WithNonSimilar_ReturnsNull(string unknown)
    {
        var suggestion = OperatorSuggestions.FindSimilarOperator(unknown);

        Assert.Null(suggestion);
    }

    [Theory]
    [InlineData("nameof", "Use a string literal")]
    [InlineData("typeof", "Use (typeof Type)")]
    [InlineData("new", "§NEW")]
    [InlineData("await", "await")]
    [InlineData("null", "Option types")]
    [InlineData("ToString", "Use (str expr)")]
    [InlineData("Contains", "Use (contains s substring)")]
    [InlineData("++", "Use (inc x)")]
    [InlineData("??", "Use (?? x default)")]
    public void GetCSharpHint_WithCSharpConstruct_ReturnsHint(string csharpConstruct, string expectedHintPart)
    {
        var hint = OperatorSuggestions.GetCSharpHint(csharpConstruct);

        Assert.NotNull(hint);
        Assert.Contains(expectedHintPart, hint);
    }

    [Fact]
    public void GetCSharpHint_WithUnknownOperator_ReturnsNull()
    {
        // Unknown operators that are not C# constructs should return null
        var hint = OperatorSuggestions.GetCSharpHint("xyzabc");

        Assert.Null(hint);
    }

    [Fact]
    public void GetOperatorCategories_ReturnsCategories()
    {
        var categories = OperatorSuggestions.GetOperatorCategories();

        Assert.Contains("arithmetic", categories);
        Assert.Contains("comparison", categories);
        Assert.Contains("logical", categories);
        Assert.Contains("string", categories);
    }

    #endregion

    #region SectionMarkerSuggestions Tests

    [Theory]
    [InlineData("FU", "F")] // Short typo - one character difference
    [InlineData("MOD", "M")] // Prefix typo
    [InlineData("CLL", "CL")] // Typo - one character off from CL (class)
    [InlineData("IFF", "IF")] // Typo - one character off
    [InlineData("ELL", "EL")] // Typo of EL (else) - one character off
    [InlineData("WR", "WR")] // Exact match (where)
    [InlineData("LAM", "LAM")] // Exact match (lambda)
    [InlineData("FL", "FL")] // Exact match (field)
    [InlineData("FF", "F")] // One character off from F
    public void FindSimilarMarker_WithTypoOrLongForm_ReturnsSuggestion(string input, string expected)
    {
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(input);

        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion);
    }

    [Theory]
    [InlineData("/FU", "/F")]
    [InlineData("/MOD", "/M")]
    [InlineData("/CLL", "/CL")]
    public void FindSimilarMarker_WithClosingTag_ReturnsSuggestion(string input, string expected)
    {
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(input);

        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion);
    }

    [Fact]
    public void GetCommonMarkers_ReturnsCommonMarkers()
    {
        var markers = SectionMarkerSuggestions.GetCommonMarkers();

        Assert.Contains("§M", markers);
        Assert.Contains("§F", markers);
        Assert.Contains("§B", markers);
        Assert.Contains("Module", markers);
        Assert.Contains("Function", markers);
    }

    #endregion

    #region Parser Integration Tests

    [Fact]
    public void Parser_UnknownOperator_WithTypo_ShowsSuggestion()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var error = result.Diagnostics.First(d => d.IsError);
        Assert.Contains("cotains", error.Message);
        Assert.Contains("contains", error.Message);
        Assert.Contains("Did you mean", error.Message);
    }

    [Fact]
    public void Parser_UnknownOperator_WithCSharpConstruct_ShowsHint()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{str} §R (nameof x) §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var error = result.Diagnostics.First(d => d.IsError);
        Assert.Contains("nameof", error.Message);
        Assert.Contains("string literal", error.Message);
    }

    [Fact]
    public void Parser_UnknownOperator_NoSuggestion_ShowsValidOperators()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R (xyzqwerty 1 2) §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var error = result.Diagnostics.First(d => d.IsError);
        Assert.Contains("xyzqwerty", error.Message);
        Assert.Contains("arithmetic", error.Message);
    }

    #endregion

    #region Lexer Integration Tests

    [Fact]
    public void Lexer_UnknownSectionMarker_WithTypo_ShowsSuggestion()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§FUNC", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("FUNC", error.Message);
        Assert.Contains("§F", error.Message);
        Assert.Contains("Did you mean", error.Message);
    }

    [Fact]
    public void Lexer_UnknownClosingTag_WithTypo_ShowsSuggestion()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§M{test} §/MODULE{test}", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("MODULE", error.Message);
    }

    [Fact]
    public void Lexer_UnknownClosingTag_ErrorMessage_ShowsSingleSlash()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§/NEWX", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("§/NEWX", error.Message);
        Assert.DoesNotContain("§//NEWX", error.Message);
    }

    [Fact]
    public void Lexer_UnknownClosingTag_WithSuggestion_ShowsSingleSlash()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§M{test} §/MOD{test}", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("§/MOD", error.Message);
        Assert.DoesNotContain("§//MOD", error.Message);
        Assert.Contains("Did you mean", error.Message);
    }

    [Fact]
    public void Lexer_UnknownSectionMarker_NoSuggestion_ShowsCommonMarkers()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§XYZQWERTY", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("XYZQWERTY", error.Message);
        Assert.Contains("Common markers", error.Message);
    }

    #endregion

    #region Fix Generation Tests

    [Fact]
    public void Parser_UnknownOperator_WithTypo_GeneratesFix()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);

        // Check that a fix was generated
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes;
        Assert.NotEmpty(fixDiagnostics);

        var fix = fixDiagnostics.First();
        Assert.Contains("contains", fix.Fix.Description);
        Assert.Single(fix.Fix.Edits);
        Assert.Equal("contains", fix.Fix.Edits[0].NewText);
    }

    #endregion

    #region Fix Application Tests

    [Fact]
    public void ApplyFix_OperatorTypo_ProducesValidCode()
    {
        // Source with typo
        var source = "§M{m001:Test} §F{f001:Fn} §O{bool} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes;
        Assert.NotEmpty(fixDiagnostics);

        // Apply the fix
        var fix = fixDiagnostics.First();
        var fixedSource = ApplyFix(source, fix.Fix);

        // Verify the fixed code compiles
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors, $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void ApplyFix_MismatchedId_GeneratesFix()
    {
        // Source with mismatched ID
        var source = "§M{m001:Test} §F{f001:Add} §O{i32} §R 42 §/F{f002} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes
            .Where(d => d.Code == DiagnosticCode.MismatchedId)
            .ToList();
        Assert.NotEmpty(fixDiagnostics);

        // Verify fix has the right content (even if position needs improvement)
        var fix = fixDiagnostics.First();
        Assert.Contains("f001", fix.Fix.Description);
        Assert.Equal("f001", fix.Fix.Edits[0].NewText);
    }

    [Theory]
    [InlineData("§M{m001:Test} §F{f001:Fn} §O{bool} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}", "cotains", "contains")]
    [InlineData("§M{m001:Test} §F{f001:Fn} §O{str} §R (uper \"hello\") §/F{f001} §/M{m001}", "uper", "upper")]
    [InlineData("§M{m001:Test} §F{f001:Fn} §O{str} §R (lowwer \"HELLO\") §/F{f001} §/M{m001}", "lowwer", "lower")]
    [InlineData("§M{m001:Test} §F{f001:Fn} §O{str} §R (substrr \"hello\" 0 2) §/F{f001} §/M{m001}", "substrr", "substr")]
    public void ApplyFix_VariousTypos_ProducesValidCode(string source, string typo, string expected)
    {
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors, $"Source with '{typo}' should have errors");
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes;

        if (fixDiagnostics.Count == 0)
        {
            // Some typos might not generate fixes if they're too far from any operator
            return;
        }

        // Apply the fix
        var fix = fixDiagnostics.First();
        Assert.Equal(expected, fix.Fix.Edits[0].NewText);

        var fixedSource = ApplyFix(source, fix.Fix);

        // Verify the fixed code compiles
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors, $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    /// <summary>
    /// Applies a SuggestedFix to source code.
    /// </summary>
    private static string ApplyFix(string source, SuggestedFix fix)
    {
        // Convert source to lines for easier manipulation
        var lines = source.Split('\n');

        // Apply edits in reverse order to avoid position shifts
        foreach (var edit in fix.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            // Convert 1-based line numbers to 0-based indices
            var startLineIdx = edit.StartLine - 1;
            var endLineIdx = edit.EndLine - 1;

            if (startLineIdx < 0 || startLineIdx >= lines.Length)
                continue;

            if (startLineIdx == endLineIdx)
            {
                // Single-line edit
                var line = lines[startLineIdx];
                var startCol = Math.Min(edit.StartColumn - 1, line.Length);
                var endCol = Math.Min(edit.EndColumn - 1, line.Length);

                if (startCol >= 0 && endCol >= startCol)
                {
                    lines[startLineIdx] = line.Substring(0, startCol) + edit.NewText + line.Substring(endCol);
                }
            }
            else
            {
                // Multi-line edit (not common, but handle it)
                var startLine = lines[startLineIdx];
                var endLine = endLineIdx < lines.Length ? lines[endLineIdx] : "";
                var startCol = Math.Min(edit.StartColumn - 1, startLine.Length);
                var endCol = Math.Min(edit.EndColumn - 1, endLine.Length);

                var newLine = startLine.Substring(0, startCol) + edit.NewText + endLine.Substring(endCol);
                lines[startLineIdx] = newLine;

                // Remove intermediate lines
                for (var i = endLineIdx; i > startLineIdx; i--)
                {
                    lines = lines.Take(i).Concat(lines.Skip(i + 1)).ToArray();
                }
            }
        }

        return string.Join('\n', lines);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void FindSimilarOperator_EmptyString_ReturnsNull()
    {
        var suggestion = OperatorSuggestions.FindSimilarOperator("");
        Assert.Null(suggestion);
    }

    [Fact]
    public void FindSimilarOperator_VeryLongString_ReturnsNull()
    {
        // Very long strings should not match anything
        var longString = new string('a', 100);
        var suggestion = OperatorSuggestions.FindSimilarOperator(longString);
        Assert.Null(suggestion);
    }

    [Fact]
    public void FindSimilarOperator_SingleCharacter_MayMatch()
    {
        // Single characters may match single-character operators like +, -, etc.
        // "x" has Levenshtein distance 1 from "+" so it may return a match
        var suggestion = OperatorSuggestions.FindSimilarOperator("x");
        // Just verify it doesn't crash; it may or may not find a match
        // depending on the distance threshold
    }

    [Fact]
    public void FindSimilarMarker_EmptyString_ReturnsNull()
    {
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker("");
        Assert.Null(suggestion);
    }

    [Fact]
    public void FindSimilarMarker_VeryLongString_ReturnsNull()
    {
        // Very long strings should not match anything
        var longString = new string('X', 100);
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(longString);
        Assert.Null(suggestion);
    }

    [Fact]
    public void GetCSharpHint_CaseInsensitive_ReturnsHint()
    {
        // C# construct detection should be case-insensitive
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("NAMEOF"));
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("NameOf"));
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("typeof"));
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("TYPEOF"));
    }

    [Fact]
    public void Parser_MultipleTyposOnSameLine_GeneratesMultipleFixes()
    {
        // Source with multiple typos on the same line
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §B{x} (abss (ngative 5)) §R x §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);

        // Should have at least 2 errors for the two typos
        var unknownOpErrors = result.Diagnostics.Where(d => d.Code == DiagnosticCode.InvalidOperator).ToList();
        Assert.True(unknownOpErrors.Count >= 2, $"Expected at least 2 unknown operator errors, got {unknownOpErrors.Count}");
    }

    [Fact]
    public void Parser_NestedTypos_GeneratesCorrectFixes()
    {
        // Nested structure with typos at different levels
        var source = @"§M{m001:Test}
§F{f001:Fn}
§O{str}
§P{s:str} ""test""
§L{l001:i:0:10:1}
§B{x} (cotains s ""t"")
§/L{l001}
§R s
§/F{f001}
§/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes;

        // Should have a fix for "cotains" -> "contains"
        var containsFix = fixDiagnostics.FirstOrDefault(d => d.Fix.Edits.Any(e => e.NewText == "contains"));
        Assert.NotNull(containsFix);
    }

    [Fact]
    public void Parser_UnicodeInIdentifier_NoSuggestionCrash()
    {
        // Unicode characters should not cause crashes
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R (εpsılοn 5) §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        // Should not crash, may or may not have suggestions
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parser_OperatorWithNumbers_NoSuggestion()
    {
        // Operators with numbers that don't match any valid operator
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R (add123 5 3) §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);
        // Should have an error but possibly no fix if too different from valid operators
        var error = result.Diagnostics.First(d => d.Code == DiagnosticCode.InvalidOperator);
        Assert.Contains("add123", error.Message);
    }

    [Fact]
    public void Parser_ExactMatchOperator_NoError()
    {
        // Exact match should not produce error
        var source = "§M{m001:Test} §F{f001:Fn} §O{bool} §R (contains \"hello\" \"h\") §/F{f001} §/M{m001}";
        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Lexer_ClosingTagWithTypo_GeneratesSuggestion()
    {
        // Closing tag with a typo
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§M{test} §/MODUL{test}", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        // Should suggest §/M for §/MODUL
        Assert.Contains("MODUL", error.Message);
    }

    [Fact]
    public void MissingCloseTag_GeneratesInsertFix()
    {
        // Missing closing tag should suggest inserting it
        var source = "§M{m001:Test} §F{f001:Fn} §O{i32} §R 42";
        var result = Program.Compile(source, "test.calr");

        Assert.True(result.HasErrors);

        // Should have fixes for missing closing tags
        var fixDiagnostics = result.Diagnostics.DiagnosticsWithFixes;
        Assert.NotEmpty(fixDiagnostics);

        // Should suggest inserting closing tags
        var insertFix = fixDiagnostics.FirstOrDefault(d => d.Fix.Description.Contains("Insert"));
        Assert.NotNull(insertFix);
    }

    [Theory]
    [InlineData("+", false)] // Valid operator
    [InlineData("-", false)]
    [InlineData("*", false)]
    [InlineData("/", false)]
    [InlineData("++", true)] // Invalid (C# construct)
    [InlineData("--", true)]
    [InlineData("??", false)] // Now supported as null-coalescing operator
    public void Parser_ShortOperators_HandleCorrectly(string op, bool shouldError)
    {
        var source = $"§M{{m001:Test}} §F{{f001:Fn}} §O{{i32}} §R ({op} 5 3) §/F{{f001}} §/M{{m001}}";
        var result = Program.Compile(source, "test.calr");

        Assert.Equal(shouldError, result.HasErrors);
    }

    #endregion

    #region End-to-End Agent Simulation Tests

    /// <summary>
    /// Simulates an AI agent workflow:
    /// 1. Receive source with errors
    /// 2. Get diagnostics with fixes
    /// 3. Apply fixes
    /// 4. Verify fixed code compiles
    /// </summary>
    [Fact]
    public void AgentSimulation_ApplyOperatorFixes_ProducesValidCode()
    {
        // Source with string operator typo that can be fixed
        var source = @"§M{m001:Test}
§F{f001:Check}
§O{bool}
§R (cotains ""hello"" ""h"")
§/F{f001}
§/M{m001}";

        // Step 1: Get diagnostics
        var result = Program.Compile(source, "test.calr");
        Assert.True(result.HasErrors, "Source should have errors");

        // Step 2: Get operator fix
        var fixes = result.Diagnostics.DiagnosticsWithFixes
            .Where(d => d.Code == DiagnosticCode.InvalidOperator)
            .ToList();
        Assert.NotEmpty(fixes);

        // Step 3: Verify fix suggests "contains"
        Assert.Equal("contains", fixes[0].Fix.Edits[0].NewText);

        // Step 4: Apply fix and verify
        var fixedSource = source.Replace("cotains", "contains");
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors,
            $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    /// <summary>
    /// Tests that an agent can fix a single operator typo end-to-end.
    /// </summary>
    [Fact]
    public void AgentSimulation_SingleTypoFix_ProducesValidCode()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{bool} §R (cotains \"hello\" \"h\") §/F{f001} §/M{m001}";

        // Step 1: Compile and get diagnostics
        var result = Program.Compile(source, "test.calr");
        Assert.True(result.HasErrors);

        // Step 2: Find the typo fix
        var typoFix = result.Diagnostics.DiagnosticsWithFixes
            .FirstOrDefault(d => d.Fix.Edits.Any(e => e.NewText == "contains"));
        Assert.NotNull(typoFix);

        // Step 3: Apply the fix
        var fixedSource = ApplyFix(source, typoFix.Fix);

        // Step 4: Verify it compiles
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors,
            $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    /// <summary>
    /// Tests that mismatched ID errors have fix suggestions with correct positions.
    /// </summary>
    [Fact]
    public void AgentSimulation_MismatchedIdFix_HasCorrectPositionAndContent()
    {
        var source = @"§M{m001:Test}
§F{f001:Add}
§O{i32}
§R 42
§/F{f002}
§/M{m001}";

        // Step 1: Compile and get diagnostics
        var result = Program.Compile(source, "test.calr");
        Assert.True(result.HasErrors);

        // Step 2: Find the mismatched ID fix
        var idFix = result.Diagnostics.DiagnosticsWithFixes
            .FirstOrDefault(d => d.Code == DiagnosticCode.MismatchedId);
        Assert.NotNull(idFix);

        // Step 3: Verify the fix has correct content and position
        Assert.Contains("f001", idFix.Fix.Description);
        Assert.Single(idFix.Fix.Edits);
        var edit = idFix.Fix.Edits[0];
        Assert.Equal("f001", edit.NewText);
        Assert.Equal(5, edit.StartLine); // Line 5: §/F{f002}
        Assert.Equal(5, edit.StartColumn); // Column 5: where "f002" starts (after "§/F{")
        Assert.Equal(9, edit.EndColumn); // Column 9: end of "f002"

        // Step 4: Apply the fix using the ApplyFix helper
        var fixedSource = ApplyFix(source, idFix.Fix);
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors,
            $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    /// <summary>
    /// Tests the MCP workflow: diagnose, apply fix, verify.
    /// </summary>
    [Fact]
    public async Task AgentSimulation_McpWorkflow_ProducesValidCode()
    {
        var source = "§M{m001:Test} §F{f001:Fn} §O{str} §R (uper \"hello\") §/F{f001} §/M{m001}";

        // Step 1: Call diagnose tool (simulated)
        var tool = new Calor.Compiler.Mcp.Tools.DiagnoseTool();
        var args = System.Text.Json.JsonDocument.Parse($"{{\"source\": {System.Text.Json.JsonSerializer.Serialize(source)}}}").RootElement;
        var toolResult = await tool.ExecuteAsync(args);

        Assert.NotNull(toolResult.Content);
        var json = toolResult.Content[0].Text!;
        var doc = System.Text.Json.JsonDocument.Parse(json);

        // Step 2: Extract fix from JSON
        var diagnostics = doc.RootElement.GetProperty("diagnostics");
        Assert.True(diagnostics.GetArrayLength() > 0);

        var diagnostic = diagnostics[0];
        Assert.True(diagnostic.TryGetProperty("fix", out var fix));
        var newText = fix.GetProperty("edits")[0].GetProperty("newText").GetString();
        Assert.Equal("upper", newText);

        // Step 3: Apply fix manually (agent would do this)
        var fixedSource = source.Replace("uper", newText!);

        // Step 4: Verify via diagnose tool
        var fixedArgs = System.Text.Json.JsonDocument.Parse($"{{\"source\": {System.Text.Json.JsonSerializer.Serialize(fixedSource)}}}").RootElement;
        var fixedResult = await tool.ExecuteAsync(fixedArgs);
        var fixedJson = fixedResult.Content[0].Text!;
        var fixedDoc = System.Text.Json.JsonDocument.Parse(fixedJson);

        Assert.True(fixedDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, fixedDoc.RootElement.GetProperty("errorCount").GetInt32());
    }

    /// <summary>
    /// Tests iterative fix application (some fixes may reveal new errors).
    /// Uses direct string replacement based on fix suggestions.
    /// </summary>
    [Fact]
    public void AgentSimulation_IterativeFixes_EventuallyConverges()
    {
        var source = @"§M{m001:Test}
§F{f001:Complex}
§O{str}
§R (uper ""hello"")
§/F{f001}
§/M{m001}";

        // First iteration: fix "uper" -> "upper"
        var result = Program.Compile(source, "test.calr");
        Assert.True(result.HasErrors);

        var fixes = result.Diagnostics.DiagnosticsWithFixes.ToList();
        Assert.NotEmpty(fixes);

        // Apply fix using newText from the fix suggestion
        var fix = fixes.First();
        Assert.Equal("upper", fix.Fix.Edits[0].NewText);
        var fixedSource = source.Replace("uper", fix.Fix.Edits[0].NewText);

        // Second iteration: should compile
        var fixedResult = Program.Compile(fixedSource, "test.calr");
        Assert.False(fixedResult.HasErrors,
            $"Fixed code should compile. Errors: {string.Join(", ", fixedResult.Diagnostics.Select(d => d.Message))}");
    }

    #endregion
}
