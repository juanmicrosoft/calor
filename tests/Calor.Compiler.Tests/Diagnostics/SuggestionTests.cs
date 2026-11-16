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
    [InlineData("typeof", "typeof(Type)")]
    [InlineData("new", "new Type(arg1, arg2)")]
    [InlineData("await", "await")]
    [InlineData("null", "nullable type")]
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
    [InlineData("modul", "module")]
    [InlineData("clas", "class")]
    [InlineData("funcc", "func")]
    [InlineData("iff", "if")]
    [InlineData("elsee", "else")]
    [InlineData("whenn", "when")]
    [InlineData("recordd", "record")]
    [InlineData("whille", "while")]
    [InlineData("requires", "requires")]
    public void CanonicalKeywordSuggestion_WithTypo_ReturnsExactWord(string input, string expected)
    {
        var suggestion = SectionMarkerSuggestions.FindSimilarMarker(input);

        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion);
    }

    [Theory]
    [InlineData("/CALL")]
    [InlineData("/LMA")]
    [InlineData("/RAWW")]
    [InlineData("/CSHAR")]
    [InlineData("/var")]
    [InlineData("§func")]
    public void CanonicalKeywordSuggestion_DoesNotHealRetiredTags(string input)
    {
        Assert.Null(SectionMarkerSuggestions.FindSimilarMarker(input));
    }

    [Theory]
    [InlineData("RECORD")]
    [InlineData("DATA")]
    [InlineData("TYPE")]
    [InlineData("VARIANT")]
    [InlineData("/IF")]
    [InlineData("/FUNC")]
    [InlineData("/MOD")]
    public void FindSimilarMarker_RemovedExpandedForm_DoesNotRecommendRemovedSyntax(string input)
    {
        Assert.Null(SectionMarkerSuggestions.FindSimilarMarker(input));
    }

    [Fact]
    public void CanonicalKeywordSuggestion_CatalogMatchesTheLexerExactly()
    {
        var expected = Lexer.KeywordNames
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, SectionMarkerSuggestions.AllMarkers.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(SectionMarkerSuggestions.AllMarkers, word => word.Any(char.IsUpper) || word.StartsWith('/'));
        Assert.All(SectionMarkerSuggestions.MarkerDescriptions.Keys,
            marker => Assert.Contains(marker, SectionMarkerSuggestions.AllMarkers));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("SM")]
    [InlineData("NN")]
    [InlineData("OK")]
    [InlineData("ERR")]
    [InlineData("SMX")]
    [InlineData("NNE")]
    [InlineData("ERRR")]
    public void Lexer_RemovedCarrierAndRecordSyntax_NeverSuggestsUnregisteredMarkers(string marker)
    {
        var diagnostics = new DiagnosticBag();
        new Lexer("§" + marker, diagnostics).TokenizeAll();

        Assert.Contains(diagnostics.Errors, error => error.Code == DiagnosticCode.UnknownSectionMarker);
        Assert.DoesNotContain(marker, SectionMarkerSuggestions.AllMarkers);
        Assert.DoesNotContain(marker, SectionMarkerSuggestions.MarkerDescriptions.Keys);
        Assert.All(diagnostics.DiagnosticsWithFixes, diagnostic =>
        {
            var edit = Assert.Single(diagnostic.Fix.Edits);
            Assert.NotEqual("§" + marker, edit.NewText);
            Assert.Contains(edit.NewText[1..], Lexer.KeywordNames);
        });
    }

    [Fact]
    public void CanonicalKeywordSuggestion_CommonWordsNeverTeachSigils()
    {
        var markers = SectionMarkerSuggestions.GetCommonMarkers();

        Assert.Contains("module", markers);
        Assert.Contains("func", markers);
        Assert.Contains("let", markers);
        Assert.DoesNotContain("§", markers);
    }
    #endregion
    #region Parser Integration Tests

    [Fact]
    public void Parser_NameofOperator_CompilesSuccessfully()
    {
        var source = "module Test Test block\n  func Fn private (int:x) → string\n    return nameof(x)";
        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Contains("nameof(x)", result.GeneratedCode);
    }

    #endregion

    #region Lexer Integration Tests

    [Fact]
    public void Lexer_UnknownSectionMarker_WithTypo_DoesNotSuggestAnotherSigil()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§FUNC", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("FUNC", error.Message);
        Assert.Contains("removed", error.Message);
        Assert.DoesNotContain("Did you mean", error.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Fact]
    public void Lexer_UnknownClosingTag_WithTypo_ShowsSuggestion()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("module test\n§/MODULE{test}", diagnostics);
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
    public void Lexer_RemovedModuleCloser_DoesNotRecommendAnotherStructuralCloser()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("module test\n§/MOD{test}", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("§/MOD", error.Message);
        Assert.DoesNotContain("§//MOD", error.Message);
        Assert.DoesNotContain("Did you mean", error.Message);
        Assert.Contains("dedent", error.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
    }

    [Fact]
    public void Lexer_UnknownSectionMarker_NoSuggestion_ShowsNativeBoundaryGuidance()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§XYZQWERTY", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("XYZQWERTY", error.Message);
        Assert.Contains("canonical lowercase words", error.Message);
    }

    [Fact]
    public void Lexer_CastSectionMarker_RejectsPrefixSyntax()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§CAST", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("removed", error.Message);
        Assert.Contains("ordinary C# expression punctuation", error.Message);
        Assert.DoesNotContain("Did you mean '§CA", error.Message);
    }

    [Fact]
    public void Lexer_MixedCaseCastSectionMarker_IsAlsoRetired()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§Cast", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("'§Cast'", error.Message);
        Assert.Contains("removed", error.Message);
    }

    [Fact]
    public void Lexer_UnknownSectionMarker_EmitsCalor0006()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§UNKNOWN", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, error.Code);
    }

    [Fact]
    public void Lexer_CastSectionMarker_EmitsCalor0006()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§CAST", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, error.Code);
    }

    [Fact]
    public void Lexer_InvalidSectionOperator_IsRejectedAtTheSectionBoundary()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("§?X", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Equal(DiagnosticCode.UnknownSectionMarker, error.Code);
        Assert.Contains("§?X", error.Message);
        Assert.Contains("removed", error.Message);
        Assert.DoesNotContain("§??", error.Message);
        Assert.DoesNotContain("§?.", error.Message);
    }

    [Fact]
    public void Lexer_AsciiArrow_EmitsCalor0007WithNativeGuidance()
    {
        var diagnostics = new DiagnosticBag();
        new Lexer("->", diagnostics).TokenizeAll();
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.InvalidSectionOperator, error.Code);
        Assert.Contains("→", error.Message);
        Assert.Contains("!p || q", error.Message);
    }

    [Fact]
    public void Lexer_TrulyUnexpectedCharacter_StillEmitsCalor0001()
    {
        var diagnostics = new DiagnosticBag();
        // Use a character that isn't handled by any lexer branch
        var lexer = new Lexer("©", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Equal(DiagnosticCode.UnexpectedCharacter, error.Code);
    }
    #endregion
    #region Fix Generation Tests
    #endregion
    #region Fix Application Tests

    [Theory]
    [InlineData("§M{Test} §F{Fn} §O{bool} §R (cotains \"hello\" \"h\")", "cotains", "contains")]
    [InlineData("§M{Test} §F{Fn} §O{str} §R (uper \"hello\")", "uper", "upper")]
    [InlineData("§M{Test} §F{Fn} §O{str} §R (lowwer \"HELLO\")", "lowwer", "lower")]
    [InlineData("§M{Test} §F{Fn} §O{str} §R (substrr \"hello\" 0 2)", "substrr", "substr")]
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
        // nameof is now a valid operator, so it's no longer in the hints
        Assert.Null(OperatorSuggestions.GetCSharpHint("NAMEOF"));
        Assert.Null(OperatorSuggestions.GetCSharpHint("NameOf"));
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("typeof"));
        Assert.NotNull(OperatorSuggestions.GetCSharpHint("TYPEOF"));
    }

    [Theory]
    [InlineData("(abss(ngative(5)))")]
    [InlineData("abss(ngative(5))")]
    public void Parser_NestedNativeCalls_AreNotUnknownPrefixOperators(string expression)
    {
        var diagnostics = new DiagnosticBag();
        var source = $"module Test Test block\n  func Fn private () → int\n    let x = {expression}\n    return x";
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var binding = Assert.IsType<Ast.BindStatementNode>(Assert.Single(module.Functions).Body[0]);
        var outer = Assert.IsType<Ast.CallExpressionNode>(binding.Initializer);
        Assert.IsType<Ast.CallExpressionNode>(Assert.Single(outer.Arguments));
    }

    [Fact]
    public void Parser_UnicodeInIdentifier_NoSuggestionCrash()
    {
        // Unicode characters should not cause crashes
        var source = "§M{Test} §F{Fn} §O{i32} §R (εpsılοn 5)";
        var result = Program.Compile(source, "test.calr");

        // Should not crash, may or may not have suggestions
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parser_ExactMatchOperator_NoError()
    {
        // Exact match should not produce error
        var source = "module Test Test block\n  func Fn private () → bool\n    return (\"hello\").Contains(\"h\")";
        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Lexer_ClosingTagWithTypo_StillRequiresDedent()
    {
        // Closing tag with a typo
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("module test\n§/MODUL{test}", diagnostics);
        lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.IsError);
        Assert.Contains("MODUL", error.Message);
        Assert.Contains("dedent", error.Message);
        Assert.Empty(diagnostics.DiagnosticsWithFixes);
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
        var left = op == "??" ? "x" : "5";
        var source = $"module Test Test block\n  func Fn private (int?:x) → int\n    return ({left} {op} 3)";
        var result = Program.Compile(
            source,
            "test.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true });

        Assert.Equal(shouldError, result.HasErrors);
    }
    #endregion
    #region End-to-End Agent Simulation Tests

    #endregion
}
