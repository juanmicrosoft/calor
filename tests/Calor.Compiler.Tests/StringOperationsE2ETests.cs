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
            module Test Test block
              func ToUpperCase (string:s) → string
                !
                return (s).ToUpper()
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
            module Test Test block
              func ToLowerCase (string:s) → string
                !
                return (s).ToLower()
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
            module Test Test block
              func TrimString (string:s) → string
                !
                return (s).Trim()
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
            module Test Test block
              func ContainsString (string:s, string:search) → bool
                !
                return (s).Contains(search)
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
            module Test Test block
              func GetSubstring (string:s, int:start, int:len) → string
                !
                return (s).Substring(start, len)
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
            module Test Test block
              func ContainsIgnoreCase (string:s, string:search) → bool
                !
                return (s).Contains(search, StringComparison.OrdinalIgnoreCase)
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
            module Test Test block
              func EqualsIgnoreCase (string:a, string:b) → bool
                !
                return (a).Equals(b, StringComparison.OrdinalIgnoreCase)
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
            module Test Test block
              func EqualsOrdinal (string:a, string:b) → bool
                !
                return (a).Equals(b, StringComparison.Ordinal)
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
            module Test Test block
              func ContainsInvariant (string:s, string:search) → bool
                !
                return (s).Contains(search, StringComparison.InvariantCultureIgnoreCase)
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
            module Test Test block
              func StartsOrdinal (string:s, string:prefix) → bool
                !
                return (s).StartsWith(prefix, StringComparison.Ordinal)
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
            module Test Test block
              func EndsIgnoreCase (string:s, string:suffix) → bool
                !
                return (s).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
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
            module Test Test block
              func IndexOfIgnoreCase (string:s, string:search) → int
                !
                return (s).IndexOf(search, StringComparison.OrdinalIgnoreCase)
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
            module Test Test block
              func TestRegex (string:s) → bool
                !
                return System.Text.RegularExpressions.Regex.IsMatch(s, "PATTERN")
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
            module Test Test block
              func ReplaceRegex (string:s) → string
                !
                return System.Text.RegularExpressions.Regex.Replace(s, "PATTERN", "REPLACEMENT")
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
            module Test Test block
              func MatchRegex (string:s) → object
                !
                return System.Text.RegularExpressions.Regex.Match(s, "PATTERN")
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
            module Test Test block
              func SplitRegex (string:s) → int
                !
                return (System.Text.RegularExpressions.Regex.Split(s, ",")).Length
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
            module Test Test block
              func GetCharAt (string:s, int:idx) → char
                !
                return (s)[idx]
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
            module Test Test block
              func CheckIsLetter (char:c) → bool
                !
                return char.IsLetter(c)
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
            module Test Test block
              func CheckIsDigit (char:c) → bool
                !
                return char.IsDigit(c)
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
            module Test Test block
              func ToUpperChar (char:c) → char
                !
                return char.ToUpper(c)
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
            module Test Test block
              func GetCharCode (char:c) → int
                !
                return (int)c
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
            module Test Test block
              func CharFromCode (int:code) → char
                !
                return (char)code
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
            module Test Test block
              func CheckIsWhiteSpace (char:c) → bool
                !
                return char.IsWhiteSpace(c)
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
            module Test Test block
              func CheckIsUpper (char:c) → bool
                !
                return char.IsUpper(c)
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
            module Test Test block
              func CheckIsLower (char:c) → bool
                !
                return char.IsLower(c)
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
            module Test Test block
              func ToLowerChar (char:c) → char
                !
                return char.ToLower(c)
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
            module Test Test block
              func CreateBuilder () → string
                ! alloc, mut
                return new System.Text.StringBuilder().ToString()
            """;

        var result = Execute(source, "CreateBuilder");

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderAppend_AppendsText()
    {
        var source = """
            module Test Test block
              func BuildString () → string
                ! alloc, mut
                return new System.Text.StringBuilder().Append("Hello").Append(" World").ToString()
            """;

        var result = Execute(source, "BuildString");

        Assert.Null(result.Exception);
        Assert.Equal("Hello World", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderWithInit_InitializesWithValue()
    {
        var source = """
            module Test Test block
              func BuildWithInit () → string
                ! alloc, mut
                return new System.Text.StringBuilder("Start: ").Append("End").ToString()
            """;

        var result = Execute(source, "BuildWithInit");

        Assert.Null(result.Exception);
        Assert.Equal("Start: End", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderLength_ReturnsLength()
    {
        var source = """
            module Test Test block
              func GetLength () → int
                ! alloc, mut
                return new System.Text.StringBuilder().Append("Hello").Length
            """;

        var result = Execute(source, "GetLength");

        Assert.Null(result.Exception);
        Assert.Equal(5, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderClear_ClearsContent()
    {
        var source = """
            module Test Test block
              func ClearBuilder () → int
                ! alloc, mut
                return new System.Text.StringBuilder().Append("Hello").Clear().Length
            """;

        var result = Execute(source, "ClearBuilder");

        Assert.Null(result.Exception);
        Assert.Equal(0, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderAppendLine_AppendsWithNewline()
    {
        var source = """
            module Test Test block
              func BuildWithLines () → string
                ! alloc, mut
                return new System.Text.StringBuilder().AppendLine("Line1").AppendLine("Line2").ToString()
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
            module Test Test block
              func InsertText () → string
                ! alloc, mut
                return new System.Text.StringBuilder().Append("HelloWorld").Insert(5, " ").ToString()
            """;

        var result = Execute(source, "InsertText");

        Assert.Null(result.Exception);
        Assert.Equal("Hello World", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderRemove_RemovesCharacters()
    {
        var source = """
            module Test Test block
              func RemoveText () → string
                ! alloc, mut
                return new System.Text.StringBuilder().Append("Hello World").Remove(5, 6).ToString()
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
            module Test Test block
              func IsFirstCharLetter (string:s) → bool
                !
                return char.IsLetter(((s).ToUpper())[0])
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
            module Test Test block
              func TrimmedHasDigits (string:s) → bool
                !
                return System.Text.RegularExpressions.Regex.IsMatch((s).Trim(), "\\d")
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
            module Test Test block
              func Capitalize (string:s) → string
                ! alloc, mut
                let System.Text.StringBuilder:sb1 = new System.Text.StringBuilder()
                let System.Text.StringBuilder:sb2 = sb1.Append(char.ToUpper((s)[0]).ToString())
                let System.Text.StringBuilder:sb3 = sb2.Append((s).Substring(1).ToLower())
                return sb3.ToString()
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
            module Test Test block
              func GetCharAt (string:s) → char
                !
                return (s)[100]
            """;

        var result = Execute(source, "GetCharAt", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<IndexOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_CharAt_NegativeIndex_ThrowsException()
    {
        var source = """
            module Test Test block
              func GetCharAt (string:s) → char
                !
                return (s)[(0 - 1)]
            """;

        var result = Execute(source, "GetCharAt", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<IndexOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_Substring_IndexOutOfBounds_ThrowsException()
    {
        var source = """
            module Test Test block
              func GetSubstr (string:s) → string
                !
                return (s).Substring(10, 5)
            """;

        var result = Execute(source, "GetSubstr", new object[] { "hello" });

        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_StringBuilderRemove_InvalidRange_ThrowsException()
    {
        var source = """
            module Test Test block
              func RemoveText () → string
                ! alloc, mut
                return new System.Text.StringBuilder().Append("Hi").Remove(0, 100).ToString()
            """;

        var result = Execute(source, "RemoveText");

        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentOutOfRangeException>(result.Exception);
    }

    [Fact]
    public void E2E_StringBuilderInsert_NegativeIndex_ThrowsException()
    {
        var source = """
            module Test Test block
              func InsertText () → string
                ! alloc, mut
                return new System.Text.StringBuilder().Insert((0 - 1), "x").ToString()
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
            module Test Test block
              func TestRegex (string:s) → bool
                !
                return System.Text.RegularExpressions.Regex.IsMatch(s, "[invalid")
            """;

        var result = Execute(source, "TestRegex", new object[] { "test" });

        Assert.NotNull(result.Exception);
        Assert.IsType<System.Text.RegularExpressions.RegexParseException>(result.Exception);
    }

    [Fact]
    public void E2E_CharFromCode_NegativeCode_Traps()
    {
        var source = """
            module Test Test block
              func CharFromCode (int:code) → char
                !
                return (char)code
            """;

        var result = Execute(source, "CharFromCode", new object[] { -1 });

        Assert.IsType<OverflowException>(result.Exception);
    }

    #endregion

    #region Negative Tests - Edge Cases

    [Fact]
    public void E2E_StringUpper_EmptyString_ReturnsEmpty()
    {
        var source = """
            module Test Test block
              func ToUpper (string:s) → string
                !
                return (s).ToUpper()
            """;

        var result = Execute(source, "ToUpper", new object[] { "" });

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_StringLen_EmptyString_ReturnsZero()
    {
        var source = """
            module Test Test block
              func GetLen (string:s) → int
                !
                return s.Length
            """;

        var result = Execute(source, "GetLen", new object[] { "" });

        Assert.Null(result.Exception);
        Assert.Equal(0, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringContains_EmptySearch_ReturnsTrue()
    {
        var source = """
            module Test Test block
              func ContainsEmpty (string:s) → bool
                !
                return (s).Contains("")
            """;

        var result = Execute(source, "ContainsEmpty", new object[] { "hello" });

        Assert.Null(result.Exception);
        Assert.Equal(true, result.ReturnValue);
    }

    [Fact]
    public void E2E_RegexSplit_NoMatches_ReturnsSingleElement()
    {
        var source = """
            module Test Test block
              func SplitRegex (string:s) → int
                !
                return (System.Text.RegularExpressions.Regex.Split(s, "NOMATCH")).Length
            """;

        var result = Execute(source, "SplitRegex", new object[] { "hello world" });

        Assert.Null(result.Exception);
        Assert.Equal(1, result.ReturnValue);
    }

    [Fact]
    public void E2E_StringBuilderNew_EmptyToString_ReturnsEmpty()
    {
        var source = """
            module Test Test block
              func EmptyBuilder () → string
                ! alloc, mut
                return new System.Text.StringBuilder().ToString()
            """;

        var result = Execute(source, "EmptyBuilder");

        Assert.Null(result.Exception);
        Assert.Equal("", result.ReturnValue);
    }

    [Fact]
    public void E2E_IndexOf_NotFound_ReturnsNegativeOne()
    {
        var source = """
            module Test Test block
              func FindIndex (string:s) → int
                !
                return (s).IndexOf("xyz")
            """;

        var result = Execute(source, "FindIndex", new object[] { "hello" });

        Assert.Null(result.Exception);
        Assert.Equal(-1, result.ReturnValue);
    }

    #endregion
}
