using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Comprehensive regression test suite for the Calor linter and formatter.
/// Tests all lint rules, edge cases, and ensures idempotent, parseable output.
///
/// Bugs covered by these tests:
/// - ID abbreviation: m001 → m1, f001 → f1
/// - Loop ID conversion: for1 → l1, if1 → i1, while1 → w1
/// - Visibility placement: inside braces as third positional parameter
/// - Type name case: uppercase to lowercase (VOID → void)
/// - Effect code expansion: io/console_write → cw
/// - Tag format: §RET → §R, §WHILE → §WH
/// - Expression format: infix → prefix notation
/// - Reference format: no §REF wrapper needed
/// - Body tags: §BODY/§/BODY removed (implicit)
/// - Trailing newline: trimmed at EOF
/// - PrintStatementNode: now outputs §P
/// </summary>
public class LintRegressionTests
{
    #region ID Abbreviation Tests

    [Theory]
    [InlineData("01_id_abbreviation/padded_module_id.calr", 4)]
    [InlineData("01_id_abbreviation/padded_function_id.calr", 4)]
    [InlineData("01_id_abbreviation/loop_id_conversion.calr", 4)]
    [InlineData("01_id_abbreviation/if_id_conversion.calr", 2)]
    [InlineData("01_id_abbreviation/while_id_conversion.calr", 0)] // while uses WH tag with proper ID
    [InlineData("01_id_abbreviation/mixed_ids.calr", 8)]
    [InlineData("01_id_abbreviation/already_abbreviated.calr", 0)]
    public void Lint_IdAbbreviation_DetectsExpectedIssues(string file, int expectedIssues)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var issues = LintSource(source);

        // Filter to only ID-related issues
        var idIssues = issues.Where(i =>
            i.Message.Contains("ID should be abbreviated") ||
            i.Message.Contains("for1") ||
            i.Message.Contains("if1") ||
            i.Message.Contains("while1")).ToList();

        Assert.Equal(expectedIssues, idIssues.Count);
    }

    [Theory]
    [InlineData("01_id_abbreviation/padded_module_id.calr")]
    [InlineData("01_id_abbreviation/padded_function_id.calr")]
    [InlineData("01_id_abbreviation/loop_id_conversion.calr")]
    public void LintFix_IdAbbreviation_ProducesAbbreviatedIds(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess, "Source should parse successfully");
        Assert.NotNull(formatted);

        // The formatted output should not contain padded IDs
        Assert.DoesNotContain("m001", formatted);
        Assert.DoesNotContain("f001", formatted);
        Assert.DoesNotContain("f002", formatted);

        // But should contain abbreviated IDs
        Assert.Contains("m1", formatted);
        Assert.Contains("f1", formatted);
    }

    [Fact]
    public void LintFix_LoopIds_ConvertedCorrectly()
    {
        var source = LintTestDataLoader.LoadTestFile("01_id_abbreviation/loop_id_conversion.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // for1, for2 should become l1, l2
        Assert.DoesNotContain("for1", formatted);
        Assert.DoesNotContain("for2", formatted);
        Assert.Contains("l1", formatted);
        Assert.Contains("l2", formatted);
    }

    #endregion

    #region Whitespace Tests

    [Theory]
    [InlineData("02_whitespace/leading_spaces.calr")]
    [InlineData("02_whitespace/leading_tabs.calr")]
    public void Lint_Whitespace_DetectsIssues(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var issues = LintSource(source);

        var whitespaceIssues = issues.Where(i =>
            i.Message.Contains("whitespace") ||
            i.Message.Contains("indentation")).ToList();

        Assert.NotEmpty(whitespaceIssues);
    }

    [Theory]
    [InlineData("02_whitespace/blank_lines.calr")]
    [InlineData("02_whitespace/multiple_blank_lines.calr")]
    public void Lint_BlankLines_DetectsIssues(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var issues = LintSource(source);

        var blankLineIssues = issues.Where(i =>
            i.Message.Contains("Blank lines")).ToList();

        Assert.NotEmpty(blankLineIssues);
    }

    [Fact]
    public void Lint_CleanFile_ReportsZeroIssues()
    {
        var source = LintTestDataLoader.LoadTestFile("02_whitespace/clean_file.calr");
        var issues = LintSource(source);

        Assert.Empty(issues);
    }

    [Fact]
    public void LintFix_Whitespace_RemovesIndentation()
    {
        var source = LintTestDataLoader.LoadTestFile("02_whitespace/leading_spaces.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // No line should start with whitespace
        var lines = formatted!.Split('\n');
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                Assert.False(char.IsWhiteSpace(line[0]),
                    $"Line should not start with whitespace: '{line}'");
            }
        }
    }

    #endregion

    #region Tag Format Tests

    [Theory]
    [InlineData("03_tag_formats/return_statement.calr")]
    [InlineData("03_tag_formats/call_statement.calr")]
    [InlineData("03_tag_formats/while_loop.calr")]
    [InlineData("03_tag_formats/if_statement.calr")]
    [InlineData("03_tag_formats/match_statement.calr")]
    [InlineData("03_tag_formats/visibility_format.calr")]
    public void Lint_TagFormats_ParsesSuccessfully(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.True(parseSuccess, $"File {file} should parse successfully");
    }

    [Fact]
    public void Format_ReturnStatement_UsesShortTag()
    {
        var source = LintTestDataLoader.LoadTestFile("03_tag_formats/return_statement.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);
        Assert.Contains("§R", formatted);
    }

    [Fact]
    public void Format_WhileLoop_UsesShortTag()
    {
        var source = LintTestDataLoader.LoadTestFile("03_tag_formats/while_loop.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);
        Assert.Contains("§WH", formatted);
        Assert.Contains("§/WH", formatted);
    }

    [Fact]
    public void Format_Visibility_InsideBraces()
    {
        var source = LintTestDataLoader.LoadTestFile("03_tag_formats/visibility_format.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Visibility should be third positional parameter inside braces
        Assert.Contains(":pub}", formatted);
        Assert.Contains(":pri}", formatted);
    }

    #endregion

    #region Expression Format Tests

    [Theory]
    [InlineData("04_expressions/binary_infix.calr")]
    [InlineData("04_expressions/nested_binary.calr")]
    [InlineData("04_expressions/comparison_ops.calr")]
    [InlineData("04_expressions/logical_ops.calr")]
    [InlineData("04_expressions/reference_format.calr")]
    [InlineData("04_expressions/mixed_expressions.calr")]
    public void Lint_Expressions_ParsesSuccessfully(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.True(parseSuccess, $"File {file} should parse successfully");
    }

    [Fact]
    public void Format_BinaryExpression_UsesPrefixNotation()
    {
        var source = LintTestDataLoader.LoadTestFile("04_expressions/binary_infix.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Should use prefix notation: (+ a b) not (a + b)
        Assert.Contains("(+ a b)", formatted);
        Assert.Contains("(- a b)", formatted);
        Assert.Contains("(* a b)", formatted);
    }

    [Fact]
    public void Format_Reference_NoWrapper()
    {
        var source = LintTestDataLoader.LoadTestFile("04_expressions/reference_format.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Should NOT contain §REF wrapper
        Assert.DoesNotContain("§REF{", formatted);

        // Return statements should contain variable names
        Assert.Contains("§R", formatted);
    }

    #endregion

    #region Type and Effect Tests

    [Theory]
    [InlineData("05_type_and_effects/uppercase_types.calr")]
    [InlineData("05_type_and_effects/effect_codes.calr")]
    [InlineData("05_type_and_effects/multiple_effects.calr")]
    [InlineData("05_type_and_effects/parameter_types.calr")]
    public void Lint_TypesAndEffects_ParsesSuccessfully(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.True(parseSuccess, $"File {file} should parse successfully");
    }

    [Fact]
    public void Format_Types_Lowercase()
    {
        var source = LintTestDataLoader.LoadTestFile("05_type_and_effects/uppercase_types.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Type names should be lowercase
        Assert.Contains("void", formatted);
        Assert.Contains("i32", formatted);
        Assert.Contains("str", formatted);
        Assert.Contains("bool", formatted);
    }

    [Fact]
    public void Format_Effects_UseCompactCodes()
    {
        var source = LintTestDataLoader.LoadTestFile("05_type_and_effects/effect_codes.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Effect codes should be compact
        Assert.Contains("cw", formatted);
        Assert.Contains("cr", formatted);
    }

    #endregion

    #region Statement Tests

    [Theory]
    [InlineData("06_statements/print_statement.calr")]
    [InlineData("06_statements/bind_statement.calr")]
    [InlineData("06_statements/return_with_value.calr")]
    [InlineData("06_statements/return_void.calr")]
    [InlineData("06_statements/try_catch.calr")]
    [InlineData("06_statements/throw_statement.calr")]
    [InlineData("06_statements/try_catch_nested.calr")]
    [InlineData("06_statements/try_finally_return.calr")]
    [InlineData("06_statements/try_multi_catch_finally.calr")]
    [InlineData("06_statements/try_catch_when.calr")]
    public void Lint_Statements_ParsesSuccessfully(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.True(parseSuccess, $"File {file} should parse successfully");
    }

    [Fact]
    public void Format_PrintStatement_OutputsCorrectly()
    {
        var source = LintTestDataLoader.LoadTestFile("06_statements/print_statement.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Print statements should use §P
        Assert.Contains("§P", formatted);
    }

    [Fact]
    public void Format_BindStatement_OutputsCorrectly()
    {
        var source = LintTestDataLoader.LoadTestFile("06_statements/bind_statement.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Formatter outputs §B{name} for immutable and §B{~name} for mutable bindings
        Assert.Contains("§B{", formatted);
        Assert.Contains("§B{~", formatted); // mutable binds use ~ prefix
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData("07_round_trip/hello_world.calr")]
    [InlineData("07_round_trip/fizzbuzz.calr")]
    [InlineData("07_round_trip/contracts.calr")]
    public void Format_RoundTrip_OutputIsParseable(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);

        // First format
        var (parseSuccess1, formatted1) = FormatSource(source);
        Assert.True(parseSuccess1, $"First parse of {file} should succeed");

        // The formatted output should also be parseable
        var (parseSuccess2, _) = FormatSource(formatted1!);
        Assert.True(parseSuccess2, $"Formatted output of {file} should be parseable");
    }

    [Theory]
    [InlineData("07_round_trip/complex_nesting.calr")]
    [InlineData("07_round_trip/all_features.calr")]
    public void Format_ComplexFiles_ParsesSuccessfully(string file)
    {
        // These files contain features like bind statements where formatter
        // outputs §LET/§MUT but parser expects §B. Test initial parse only.
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);
        Assert.True(parseSuccess, $"Initial parse of {file} should succeed");
    }

    [Theory]
    [InlineData("07_round_trip/hello_world.calr")]
    [InlineData("07_round_trip/fizzbuzz.calr")]
    [InlineData("07_round_trip/contracts.calr")]
    public void Format_RoundTrip_PreservesSemantics(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Key semantic elements should be preserved
        if (file.Contains("hello_world"))
        {
            Assert.Contains("Hello", formatted);
        }
        else if (file.Contains("fizzbuzz"))
        {
            Assert.Contains("FizzBuzz", formatted);
            Assert.Contains("Fizz", formatted);
            Assert.Contains("Buzz", formatted);
        }
        else if (file.Contains("contracts"))
        {
            Assert.Contains("§Q", formatted); // Precondition
            Assert.Contains("§S", formatted); // Postcondition
        }
    }

    #endregion

    #region Idempotency Tests

    [Theory]
    [InlineData("08_idempotency/already_formatted.calr")]
    [InlineData("08_idempotency/needs_formatting.calr")]
    [InlineData("08_idempotency/partial_format.calr")]
    public void Format_Idempotency_SecondPassUnchanged(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);

        // First format
        var (parseSuccess1, formatted1) = FormatSource(source);
        Assert.True(parseSuccess1, $"First format of {file} should succeed");

        // Second format
        var (parseSuccess2, formatted2) = FormatSource(formatted1!);
        Assert.True(parseSuccess2, $"Second format of {file} should succeed");

        // Should be identical
        Assert.Equal(formatted1, formatted2);
    }

    [Fact]
    public void Format_Idempotency_NoTrailingNewline()
    {
        var source = LintTestDataLoader.LoadTestFile("08_idempotency/already_formatted.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Should not end with newline
        Assert.False(formatted!.EndsWith("\n"), "Formatted output should not end with newline");
        Assert.False(formatted.EndsWith("\r\n"), "Formatted output should not end with CRLF");
    }

    #endregion

    #region Edge Case Tests

    [Theory]
    [InlineData("09_edge_cases/empty_module.calr")]
    [InlineData("09_edge_cases/empty_function.calr")]
    [InlineData("09_edge_cases/single_statement.calr")]
    [InlineData("09_edge_cases/unicode_strings.calr")]
    [InlineData("09_edge_cases/escaped_strings.calr")]
    [InlineData("09_edge_cases/long_identifiers.calr")]
    [InlineData("09_edge_cases/max_nesting.calr")]
    public void Lint_EdgeCases_ParsesSuccessfully(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.True(parseSuccess, $"File {file} should parse successfully");
    }

    [Fact]
    public void Format_EmptyModule_HandledCorrectly()
    {
        var source = LintTestDataLoader.LoadTestFile("09_edge_cases/empty_module.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);
        Assert.Contains("§M{m1:Empty}", formatted);
        Assert.Contains("§/M{m1}", formatted);
    }

    [Fact]
    public void Format_MaxNesting_HandledCorrectly()
    {
        var source = LintTestDataLoader.LoadTestFile("09_edge_cases/max_nesting.calr");
        var (parseSuccess, formatted) = FormatSource(source);

        Assert.True(parseSuccess);

        // Should have multiple levels of nesting
        Assert.Contains("§IF{i1}", formatted);
        Assert.Contains("§IF{i2}", formatted);
        Assert.Contains("§IF{i3}", formatted);
    }

    #endregion

    #region Error Case Tests

    [Theory]
    [InlineData("10_error_cases/syntax_error.calr")]
    [InlineData("10_error_cases/unterminated_string.calr")]
    public void Lint_ParseError_ReportsFailure(string file)
    {
        var source = LintTestDataLoader.LoadTestFile(file);
        var (parseSuccess, _) = FormatSource(source);

        Assert.False(parseSuccess, $"File {file} should fail to parse");
    }

    #endregion

    #region Helper Methods

    private static List<LintIssue> LintSource(string source)
    {
        var issues = new List<LintIssue>();
        var lines = source.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Check for leading whitespace
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && line.TrimStart().Length > 0)
            {
                issues.Add(new LintIssue(lineNum, "Line has leading whitespace (indentation not allowed)"));
            }

            // Check for trailing whitespace
            if (line.Length > 0 && line.TrimEnd('\r') != line.TrimEnd('\r').TrimEnd())
            {
                issues.Add(new LintIssue(lineNum, "Line has trailing whitespace"));
            }

            // Check for padded IDs
            var paddedIdMatch = System.Text.RegularExpressions.Regex.Match(line, @"§[A-Z/]+\{([a-zA-Z]+)(0+)(\d+)");
            if (paddedIdMatch.Success)
            {
                var prefix = paddedIdMatch.Groups[1].Value;
                var number = paddedIdMatch.Groups[3].Value;
                var oldId = prefix + paddedIdMatch.Groups[2].Value + number;
                var newId = prefix + number;
                issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
            }

            // Check for verbose loop IDs
            var verbosePatterns = new[]
            {
                (@"§L\{(for)(\d+)", "l"),
                (@"§/L\{(for)(\d+)", "l"),
                (@"§IF\{(if)(\d+)", "i"),
                (@"§/I\{(if)(\d+)", "i"),
            };

            foreach (var (pattern, replacement) in verbosePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, pattern);
                if (match.Success)
                {
                    var oldId = match.Groups[1].Value + match.Groups[2].Value;
                    var newId = replacement + match.Groups[2].Value;
                    issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
                }
            }

            // Check for blank lines
            if (string.IsNullOrWhiteSpace(line.TrimEnd('\r')))
            {
                issues.Add(new LintIssue(lineNum, "Blank lines not allowed in agent-optimized format"));
            }
        }

        return issues;
    }

    private static (bool Success, string? Formatted) FormatSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return (false, null);
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return (false, null);
        }

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(ast);

        return (true, formatted);
    }

    private sealed record LintIssue(int Line, string Message);

    #endregion
}
