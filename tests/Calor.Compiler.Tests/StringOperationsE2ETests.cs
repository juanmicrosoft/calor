using Calor.Compiler;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end tests that compile Calor code with string operations to C#,
/// then compile and execute the C# to verify correct behavior.
/// </summary>
public class StringOperationsE2ETests
{
    private static RuntimeResult Execute(string calorSource, string methodName, object?[]? args = null)
    {
        var options = new CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = ContractMode.Debug
        };
        return TestHarness.Execute(calorSource, methodName, args, options);
    }

    #region Basic String Operations E2E

    [Theory]
    [InlineData("hello", "HELLO")]
    [InlineData("World", "WORLD")]
    [InlineData("", "")]
    public void E2E_StringUpper_ReturnsUppercased(string input, string expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ToUpperCase:pub}
              §I{string:s}
              §O{string}
              §R (upper s)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ToUpperCase", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("HELLO", "hello")]
    [InlineData("World", "world")]
    public void E2E_StringLower_ReturnsLowercased(string input, string expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ToLowerCase:pub}
              §I{string:s}
              §O{string}
              §R (lower s)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ToLowerCase", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("  hello  ", "hello")]
    [InlineData("no spaces", "no spaces")]
    public void E2E_StringTrim_ReturnsTrimmed(string input, string expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:TrimString:pub}
              §I{string:s}
              §O{string}
              §R (trim s)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "TrimString", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("hello world", "world", true)]
    [InlineData("hello world", "WORLD", false)]
    [InlineData("hello world", "xyz", false)]
    public void E2E_StringContains_ReturnsCorrectResult(string input, string search, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ContainsString:pub}
              §I{string:s}
              §I{string:search}
              §O{bool}
              §R (contains s search)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ContainsString", new object[] { input, search });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("hello world", 0, 5, "hello")]
    [InlineData("hello world", 6, 5, "world")]
    public void E2E_StringSubstring_ReturnsSubstring(string input, int start, int length, string expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetSubstring:pub}
              §I{string:s}
              §I{i32:start}
              §I{i32:len}
              §O{string}
              §R (substr s start len)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetSubstring", new object[] { input, start, length });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    #endregion

    #region StringComparison Mode E2E

    [Theory]
    [InlineData("Hello", "HELLO", true)]
    [InlineData("Hello", "hello", true)]
    [InlineData("Hello", "World", false)]
    public void E2E_StringContainsIgnoreCase_ReturnsCorrectResult(string input, string search, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ContainsIgnoreCase:pub}
              §I{string:s}
              §I{string:search}
              §O{bool}
              §R (contains s search :ignore-case)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ContainsIgnoreCase", new object[] { input, search });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("YES", "yes", true)]
    [InlineData("YES", "YES", true)]
    [InlineData("YES", "no", false)]
    public void E2E_StringEqualsIgnoreCase_ReturnsCorrectResult(string a, string b, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:EqualsIgnoreCase:pub}
              §I{string:a}
              §I{string:b}
              §O{bool}
              §R (equals a b :ignore-case)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "EqualsIgnoreCase", new object[] { a, b });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("Hello", "Hello", true)]
    [InlineData("Hello", "hello", false)]
    public void E2E_StringEqualsOrdinal_ReturnsCorrectResult(string a, string b, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:EqualsOrdinal:pub}
              §I{string:a}
              §I{string:b}
              §O{bool}
              §R (equals a b :ordinal)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "EqualsOrdinal", new object[] { a, b });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("Hello World", "HELLO", true)]
    [InlineData("Hello World", "hello", true)]
    [InlineData("Hello World", "xyz", false)]
    public void E2E_StringContainsInvariantIgnoreCase_ReturnsCorrectResult(string input, string search, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ContainsInvariant:pub}
              §I{string:s}
              §I{string:search}
              §O{bool}
              §R (contains s search :invariant-ignore-case)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ContainsInvariant", new object[] { input, search });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("Hello World", "Hello", true)]
    [InlineData("Hello World", "hello", false)]
    public void E2E_StringStartsWithOrdinal_ReturnsCorrectResult(string input, string prefix, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:StartsOrdinal:pub}
              §I{string:s}
              §I{string:prefix}
              §O{bool}
              §R (starts s prefix :ordinal)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "StartsOrdinal", new object[] { input, prefix });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("Hello World", "WORLD", true)]
    [InlineData("Hello World", "world", true)]
    public void E2E_StringEndsWithIgnoreCase_ReturnsCorrectResult(string input, string suffix, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:EndsIgnoreCase:pub}
              §I{string:s}
              §I{string:suffix}
              §O{bool}
              §R (ends s suffix :ignore-case)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "EndsIgnoreCase", new object[] { input, suffix });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("Hello World", "WORLD", 6)]
    [InlineData("Hello World", "xyz", -1)]
    public void E2E_StringIndexOfIgnoreCase_ReturnsCorrectResult(string input, string search, int expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:IndexOfIgnoreCase:pub}
              §I{string:s}
              §I{string:search}
              §O{i32}
              §R (indexof s search :ignore-case)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "IndexOfIgnoreCase", new object[] { input, search });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    #endregion

    #region Regex Operations E2E

    [Theory]
    [InlineData("hello123", "\\d+", true)]
    [InlineData("hello", "\\d+", false)]
    [InlineData("abc123xyz", "[0-9]+", true)]
    public void E2E_RegexTest_ReturnsCorrectResult(string input, string pattern, bool expected)
    {
        // Escape backslashes for Calor string literal (Calor needs \\ for a literal \)
        var escapedPattern = pattern.Replace("\\", "\\\\");
        var source = """
            §M{m001:Test}
            §F{f001:TestRegex:pub}
              §I{string:s}
              §O{bool}
              §R (regex-test s "PATTERN")
            §/F{f001}
            §/M{m001}
            """.Replace("PATTERN", escapedPattern);

        var result = Execute(source, "TestRegex", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("hello world", "\\s+", "-", "hello-world")]
    [InlineData("a1b2c3", "\\d", "X", "aXbXcX")]
    public void E2E_RegexReplace_ReturnsCorrectResult(string input, string pattern, string replacement, string expected)
    {
        // Escape backslashes for Calor string literal (Calor needs \\ for a literal \)
        var escapedPattern = pattern.Replace("\\", "\\\\");
        var source = """
            §M{m001:Test}
            §F{f001:ReplaceRegex:pub}
              §I{string:s}
              §O{string}
              §R (regex-replace s "PATTERN" "REPLACEMENT")
            §/F{f001}
            §/M{m001}
            """.Replace("PATTERN", escapedPattern).Replace("REPLACEMENT", replacement);

        var result = Execute(source, "ReplaceRegex", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData("hello123world", "\\d+", true)]
    [InlineData("abc", "\\d+", false)]
    public void E2E_RegexMatch_ReturnsMatchObject(string input, string pattern, bool expectedSuccess)
    {
        // regex-match returns a Match object; test via Match.Success property
        var escapedPattern = pattern.Replace("\\", "\\\\");
        var source = """
            §M{m001:Test}
            §F{f001:MatchRegex:pub}
              §I{string:s}
              §O{object}
              §R (regex-match s "PATTERN")
            §/F{f001}
            §/M{m001}
            """.Replace("PATTERN", escapedPattern);

        var result = Execute(source, "MatchRegex", new object[] { input });

        Assert.Null(result.Exception);
        var match = result.ReturnValue as System.Text.RegularExpressions.Match;
        Assert.NotNull(match);
        Assert.Equal(expectedSuccess, match!.Success);
    }

    [Fact]
    public void E2E_RegexSplit_ReturnsSplitArray()
    {
        var source = """
            §M{m001:Test}
            §F{f001:SplitRegex:pub}
              §I{string:s}
              §O{i32}
              §R (len (regex-split s ","))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "SplitRegex", new object[] { "a,b,c,d" });

        Assert.Null(result.Exception);
        Assert.Equal(4, result.ReturnValue);
    }

    #endregion

    #region Char Operations E2E

    [Theory]
    [InlineData("hello", 0, 'h')]
    [InlineData("hello", 4, 'o')]
    [InlineData("ABC", 1, 'B')]
    public void E2E_CharAt_ReturnsCorrectChar(string input, int index, char expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetCharAt:pub}
              §I{string:s}
              §I{i32:idx}
              §O{char}
              §R (char-at s idx)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetCharAt", new object[] { input, index });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('A', true)]
    [InlineData('z', true)]
    [InlineData('5', false)]
    [InlineData(' ', false)]
    public void E2E_IsLetter_ReturnsCorrectResult(char input, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CheckIsLetter:pub}
              §I{char:c}
              §O{bool}
              §R (is-letter c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CheckIsLetter", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('0', true)]
    [InlineData('9', true)]
    [InlineData('A', false)]
    [InlineData(' ', false)]
    public void E2E_IsDigit_ReturnsCorrectResult(char input, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CheckIsDigit:pub}
              §I{char:c}
              §O{bool}
              §R (is-digit c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CheckIsDigit", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('a', 'A')]
    [InlineData('Z', 'Z')]
    [InlineData('5', '5')]
    public void E2E_CharUpper_ReturnsUppercased(char input, char expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ToUpperChar:pub}
              §I{char:c}
              §O{char}
              §R (char-upper c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ToUpperChar", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('A', 65)]
    [InlineData('a', 97)]
    [InlineData('0', 48)]
    public void E2E_CharCode_ReturnsAsciiCode(char input, int expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetCharCode:pub}
              §I{char:c}
              §O{i32}
              §R (char-code c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetCharCode", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData(65, 'A')]
    [InlineData(97, 'a')]
    [InlineData(48, '0')]
    public void E2E_CharFromCode_ReturnsChar(int input, char expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CharFromCode:pub}
              §I{i32:code}
              §O{char}
              §R (char-from-code code)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CharFromCode", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('\n', true)]
    [InlineData('A', false)]
    [InlineData('5', false)]
    public void E2E_IsWhiteSpace_ReturnsCorrectResult(char input, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CheckIsWhiteSpace:pub}
              §I{char:c}
              §O{bool}
              §R (is-whitespace c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CheckIsWhiteSpace", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData('a', false)]
    [InlineData('5', false)]
    public void E2E_IsUpper_ReturnsCorrectResult(char input, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CheckIsUpper:pub}
              §I{char:c}
              §O{bool}
              §R (is-upper c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CheckIsUpper", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('z', true)]
    [InlineData('A', false)]
    [InlineData('5', false)]
    public void E2E_IsLower_ReturnsCorrectResult(char input, bool expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:CheckIsLower:pub}
              §I{char:c}
              §O{bool}
              §R (is-lower c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CheckIsLower", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    [Theory]
    [InlineData('A', 'a')]
    [InlineData('z', 'z')]
    [InlineData('5', '5')]
    public void E2E_CharLower_ReturnsLowercased(char input, char expected)
    {
        var source = """
            §M{m001:Test}
            §F{f001:ToLowerChar:pub}
              §I{char:c}
              §O{char}
              §R (char-lower c)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ToLowerChar", new object[] { input });

        Assert.Null(result.Exception);
        Assert.Equal(expected, result.ReturnValue);
    }

    #endregion

    #region StringBuilder Operations E2E

    [Fact]
    public void E2E_StringBuilderNew_CreatesEmptyBuilder()
    {
        var source = """
            §M{m001:Test}
            §F{f001:CreateBuilder:pub}
              §O{string}
              §R (sb-tostring (sb-new))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "CreateBuilder");

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderAppend_AppendsText()
    {
        var source = """
            §M{m001:Test}
            §F{f001:BuildString:pub}
              §O{string}
              §R (sb-tostring (sb-append (sb-append (sb-new) "Hello") " World"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "BuildString");

        Assert.Null(result.Exception);
        Assert.Equal("Hello World", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderWithInit_InitializesWithValue()
    {
        var source = """
            §M{m001:Test}
            §F{f001:BuildWithInit:pub}
              §O{string}
              §R (sb-tostring (sb-append (sb-new "Start: ") "End"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "BuildWithInit");

        Assert.Null(result.Exception);
        Assert.Equal("Start: End", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderLength_ReturnsLength()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetLength:pub}
              §O{i32}
              §R (sb-length (sb-append (sb-new) "Hello"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetLength");

        Assert.Null(result.Exception);
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderClear_ClearsContent()
    {
        var source = """
            §M{m001:Test}
            §F{f001:ClearBuilder:pub}
              §O{i32}
              §R (sb-length (sb-clear (sb-append (sb-new) "Hello")))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ClearBuilder");

        Assert.Null(result.Exception);
        Assert.Equal(0, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderAppendLine_AppendsWithNewline()
    {
        var source = """
            §M{m001:Test}
            §F{f001:BuildWithLines:pub}
              §O{string}
              §R (sb-tostring (sb-appendline (sb-appendline (sb-new) "Line1") "Line2"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "BuildWithLines");

        Assert.Null(result.Exception);
        // AppendLine adds Environment.NewLine after each line
        var expected = "Line1" + System.Environment.NewLine + "Line2" + System.Environment.NewLine;
        Assert.Equal(expected, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderInsert_InsertsAtPosition()
    {
        var source = """
            §M{m001:Test}
            §F{f001:InsertText:pub}
              §O{string}
              §R (sb-tostring (sb-insert (sb-append (sb-new) "HelloWorld") 5 " "))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "InsertText");

        Assert.Null(result.Exception);
        Assert.Equal("Hello World", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderRemove_RemovesCharacters()
    {
        var source = """
            §M{m001:Test}
            §F{f001:RemoveText:pub}
              §O{string}
              §R (sb-tostring (sb-remove (sb-append (sb-new) "Hello World") 5 6))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "RemoveText");

        Assert.Null(result.Exception);
        Assert.Equal("Hello", result.ReturnValue);
    }

    #endregion

    #region Cross-Category Composition E2E

    [Fact]
    public void E2E_CharWithStringOp_ComposesCorrectly()
    {
        // Test: is first char of uppercased string a letter?
        var source = """
            §M{m001:Test}
            §F{f001:IsFirstCharLetter:pub}
              §I{string:s}
              §O{bool}
              §R (is-letter (char-at (upper s) 0))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "IsFirstCharLetter", new object[] { "hello" });

        Assert.Null(result.Exception);
        Assert.Equal(true, result.ReturnValue);
    }

    [Fact]
    public void E2E_RegexWithStringOp_ComposesCorrectly()
    {
        // Test: does trimmed string contain digits?
        // Raw string \\d becomes Calor source "\\d", Calor parses as \d regex
        var source = """
            §M{m001:Test}
            §F{f001:TrimmedHasDigits:pub}
              §I{string:s}
              §O{bool}
              §R (regex-test (trim s) "\\d")
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "TrimmedHasDigits", new object[] { "  abc123  " });

        Assert.Null(result.Exception);
        Assert.Equal(true, result.ReturnValue);
    }

    [Fact]
    public void E2E_ComplexComposition_WorksCorrectly()
    {
        // Test: convert first char to uppercase, rest to lowercase
        // This tests multiple operations working together
        // Use unique names for each binding since Calor generates new 'var' for each
        var source = """
            §M{m001:Test}
            §F{f001:Capitalize:pub}
              §I{string:s}
              §O{string}
              §B{sb1} (sb-new)
              §B{sb2} (sb-append sb1 (str (char-upper (char-at s 0))))
              §B{sb3} (sb-append sb2 (lower (substr s 1)))
              §R (sb-tostring sb3)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "Capitalize", new object[] { "hELLO" });

        Assert.Null(result.Exception);
        Assert.Equal("Hello", result.ReturnValue);
    }

    #endregion

    #region Negative Tests - Runtime Errors

    [Fact]
    public void E2E_CharAt_IndexOutOfBounds_ThrowsException()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetCharAt:pub}
              §I{string:s}
              §O{char}
              §R (char-at s 100)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetCharAt", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<IndexOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_CharAt_NegativeIndex_ThrowsException()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetCharAt:pub}
              §I{string:s}
              §O{char}
              §R (char-at s (- 0 1))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetCharAt", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<IndexOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_Substring_IndexOutOfBounds_ThrowsException()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetSubstr:pub}
              §I{string:s}
              §O{string}
              §R (substr s 10 5)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetSubstr", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_StringBuilderRemove_InvalidRange_ThrowsException()
    {
        var source = """
            §M{m001:Test}
            §F{f001:RemoveText:pub}
              §O{string}
              §R (sb-tostring (sb-remove (sb-append (sb-new) "Hi") 0 100))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "RemoveText");

        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_StringBuilderInsert_NegativeIndex_ThrowsException()
    {
        var source = """
            §M{m001:Test}
            §F{f001:InsertText:pub}
              §O{string}
              §R (sb-tostring (sb-insert (sb-new) (- 0 1) "x"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "InsertText");

        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_RegexTest_InvalidPattern_ThrowsException()
    {
        // Invalid regex pattern: unclosed bracket
        var source = """
            §M{m001:Test}
            §F{f001:TestRegex:pub}
              §I{string:s}
              §O{bool}
              §R (regex-test s "[invalid")
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "TestRegex", new object[] { "test" });

        Assert.NotNull(result.Exception);
        Assert.IsType<System.Text.RegularExpressions.RegexParseException>(result.Exception);
    }

    [Fact]
    public void E2E_CharFromCode_NegativeCode_ReturnsChar()
    {
        // Negative values wrap around in char conversion (implementation-defined behavior)
        var source = """
            §M{m001:Test}
            §F{f001:CharFromCode:pub}
              §I{i32:code}
              §O{char}
              §R (char-from-code code)
            §/F{f001}
            §/M{m001}
            """;

        // -1 wraps to 65535 (0xFFFF) in unchecked char conversion
        var result = Execute(source, "CharFromCode", new object[] { -1 });

        Assert.Null(result.Exception);
        Assert.Equal((char)65535, result.ReturnValue);
    }

    #endregion

    #region Negative Tests - Edge Cases

    [Fact]
    public void E2E_StringUpper_EmptyString_ReturnsEmpty()
    {
        var source = """
            §M{m001:Test}
            §F{f001:ToUpper:pub}
              §I{string:s}
              §O{string}
              §R (upper s)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ToUpper", new object[] { "" });

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringLen_EmptyString_ReturnsZero()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetLen:pub}
              §I{string:s}
              §O{i32}
              §R (len s)
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "GetLen", new object[] { "" });

        Assert.Null(result.Exception);
        Assert.Equal(0, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringContains_EmptySearch_ReturnsTrue()
    {
        var source = """
            §M{m001:Test}
            §F{f001:ContainsEmpty:pub}
              §I{string:s}
              §O{bool}
              §R (contains s "")
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "ContainsEmpty", new object[] { "hello" });

        Assert.Null(result.Exception);
        Assert.Equal(true, result.ReturnValue);
    }

    [Fact]
    public void E2E_RegexSplit_NoMatches_ReturnsSingleElement()
    {
        var source = """
            §M{m001:Test}
            §F{f001:SplitRegex:pub}
              §I{string:s}
              §O{i32}
              §R (len (regex-split s "NOMATCH"))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "SplitRegex", new object[] { "hello world" });

        Assert.Null(result.Exception);
        Assert.Equal(1, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderNew_EmptyToString_ReturnsEmpty()
    {
        var source = """
            §M{m001:Test}
            §F{f001:EmptyBuilder:pub}
              §O{string}
              §R (sb-tostring (sb-new))
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "EmptyBuilder");

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_IndexOf_NotFound_ReturnsNegativeOne()
    {
        var source = """
            §M{m001:Test}
            §F{f001:FindIndex:pub}
              §I{string:s}
              §O{i32}
              §R (indexof s "xyz")
            §/F{f001}
            §/M{m001}
            """;

        var result = Execute(source, "FindIndex", new object[] { "hello" });

        Assert.Null(result.Exception);
        Assert.Equal(-1, result.ReturnValue);
    }

    #endregion
}
