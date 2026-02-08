using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

/// <summary>
/// Edge case tests for ID validation.
/// Tests boundary conditions, unusual ID formats, and case sensitivity.
/// </summary>
public class IdValidatorEdgeCaseTests
{
    #region Case Sensitivity Tests

    [Theory]
    [InlineData("f_01j5x7k9m2npqrstabwxyz1234")]  // All lowercase ULID
    [InlineData("f_01J5x7K9m2NpQrStAbWxYz1234")]  // Mixed case ULID
    public void MixedCaseUlid_IsValid(string id)
    {
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.Valid, result);
    }

    [Fact]
    public void UppercasePrefix_IsInvalid()
    {
        // Uppercase prefix (F_) is invalid - prefixes must be lowercase
        var id = "F_01J5X7K9M2NPQRSTABWXYZ1234";
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Theory]
    [InlineData("01j5x7k9m2npqrstabwxyz1234")]  // All lowercase ULID
    [InlineData("01J5X7K9M2NPQRSTABWXYZ1234")]  // All uppercase ULID
    public void IsValidUlid_CaseInsensitive(string ulid)
    {
        var result = IdValidator.IsValidUlid(ulid);

        Assert.True(result);
    }

    [Theory]
    [InlineData("F001")]  // Uppercase prefix
    [InlineData("f001")]  // Lowercase prefix
    [InlineData("F0001")] // Uppercase with more digits
    public void TestId_CaseInsensitive(string id)
    {
        var isTestId = IdValidator.IsTestId(id);

        Assert.True(isTestId);
    }

    #endregion

    #region Prefix Validation Tests

    [Fact]
    public void PrefixOnly_Function_IsInvalid()
    {
        // f_ prefix only (matching kind Function) -> InvalidFormat (no ULID)
        var result = IdValidator.Validate("f_", IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Theory]
    [InlineData("m_", IdKind.Function)]  // Module prefix for Function -> WrongPrefix
    [InlineData("c_", IdKind.Function)]  // Class prefix for Function -> WrongPrefix
    public void PrefixOnly_WrongKind_IsWrongPrefix(string id, IdKind kind)
    {
        // Prefix-only IDs where prefix doesn't match kind -> WrongPrefix
        // (The prefix is recognized but wrong for the expected kind)
        var result = IdValidator.Validate(id, kind, isTestPath: false);

        Assert.Equal(IdValidationResult.WrongPrefix, result);
    }

    [Theory]
    [InlineData("x_01J5X7K9M2NPQRSTABWXYZ1234")]  // Unknown prefix x_
    [InlineData("z_01J5X7K9M2NPQRSTABWXYZ1234")]  // Unknown prefix z_
    [InlineData("foo_01J5X7K9M2NPQRSTABWXYZ1234")] // Long unknown prefix
    public void UnknownPrefix_IsInvalid(string id)
    {
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Theory]
    [InlineData("_01J5X7K9M2NPQRSTABWXYZ1234")]   // Leading underscore only
    [InlineData("01J5X7K9M2NPQRSTABWXYZ1234")]    // No prefix
    public void NoPrefix_IsInvalid(string id)
    {
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Fact]
    public void DoublePrefix_IsInvalid()
    {
        // m_ctor_ is not a valid prefix combination
        var id = "m_ctor_01J5X7K9M2NPQRSTABWXYZ1234";
        var result = IdValidator.Validate(id, IdKind.Module, isTestPath: false);

        // This should be invalid - the kind extraction would fail
        Assert.NotEqual(IdValidationResult.Valid, result);
    }

    #endregion

    #region ULID Length Tests

    [Fact]
    public void UlidTooShort_IsInvalid()
    {
        // 25 characters (missing 1)
        var id = "f_01J5X7K9M2NPQRSTABWXYZ123";
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Fact]
    public void UlidTooLong_IsInvalid()
    {
        // 27 characters (extra 1)
        var id = "f_01J5X7K9M2NPQRSTABWXYZ12345";
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.InvalidFormat, result);
    }

    [Fact]
    public void UlidExactlyRightLength_IsValid()
    {
        // Exactly 26 characters
        var id = "f_01J5X7K9M2NPQRSTABWXYZ1234";
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.Valid, result);
    }

    #endregion

    #region Invalid ULID Characters Tests

    [Theory]
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123I")]  // Contains I
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123L")]  // Contains L
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123O")]  // Contains O
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123U")]  // Contains U
    public void UlidWithInvalidChars_IsInvalid(string ulid)
    {
        // Crockford Base32 excludes I, L, O, U
        var result = IdValidator.IsValidUlid(ulid);

        Assert.False(result);
    }

    [Theory]
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123!")]  // Special char
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123.")]  // Dot
    [InlineData("01J5X7K9M2NPQRSTABWXYZ123 ")]  // Space
    public void UlidWithSpecialChars_IsInvalid(string ulid)
    {
        var result = IdValidator.IsValidUlid(ulid);

        Assert.False(result);
    }

    [Theory]
    [InlineData("0123456789ABCDEFGHJKMNPQRS")]   // 26 chars - all valid
    [InlineData("VWXYZ01234ABCDEFGHJKMNPQRS")]   // 26 chars - V, W, X, Y, Z are valid
    public void UlidWithValidChars_IsValid(string ulid)
    {
        // ULIDs must be exactly 26 characters
        var result = IdValidator.IsValidUlid(ulid);

        Assert.True(result);
    }

    #endregion

    #region Test ID Edge Cases

    [Theory]
    [InlineData("f000000001")]  // Many digits
    [InlineData("f999")]        // Three digits
    [InlineData("f0000000000000000001")]  // Very many digits
    public void TestIdWithManyDigits_IsValid(string id)
    {
        var result = IdValidator.IsTestId(id);

        Assert.True(result);
    }

    [Theory]
    [InlineData("f00")]   // Only 2 digits
    [InlineData("f0")]    // Only 1 digit
    [InlineData("f")]     // No digits
    public void TestIdWithFewDigits_IsInvalid(string id)
    {
        var result = IdValidator.IsTestId(id);

        Assert.False(result);
    }

    [Theory]
    [InlineData("mt001")]   // Method test ID
    [InlineData("ctor001")] // Constructor test ID
    public void TestIdWithMultiCharPrefix_IsValid(string id)
    {
        var result = IdValidator.IsTestId(id);

        Assert.True(result);
    }

    [Theory]
    [InlineData("m001", IdKind.Module, true, IdValidationResult.Valid)]
    [InlineData("f001", IdKind.Function, true, IdValidationResult.Valid)]
    [InlineData("c001", IdKind.Class, true, IdValidationResult.Valid)]
    [InlineData("i001", IdKind.Interface, true, IdValidationResult.Valid)]
    [InlineData("p001", IdKind.Property, true, IdValidationResult.Valid)]
    [InlineData("mt001", IdKind.Method, true, IdValidationResult.Valid)]
    [InlineData("ctor001", IdKind.Constructor, true, IdValidationResult.Valid)]
    [InlineData("e001", IdKind.Enum, true, IdValidationResult.Valid)]
    public void TestIdPrefix_MatchesKind(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("m001", IdKind.Function, true, IdValidationResult.WrongPrefix)]  // Module ID for function
    [InlineData("f001", IdKind.Module, true, IdValidationResult.WrongPrefix)]    // Function ID for module
    public void TestIdPrefix_WrongKind_ReportsWrongPrefix(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Path Recognition Tests

    [Theory]
    [InlineData("tests/foo.calr")]
    [InlineData("TESTS/foo.calr")]
    [InlineData("Tests/foo.calr")]
    public void IsTestPath_CaseInsensitive(string path)
    {
        var result = IdValidator.IsTestPath(path);

        Assert.True(result);
    }

    [Theory]
    [InlineData("tests\\foo.calr")]    // Windows backslash
    [InlineData("path\\tests\\foo.calr")]
    public void IsTestPath_HandlesBackslashes(string path)
    {
        var result = IdValidator.IsTestPath(path);

        Assert.True(result);
    }

    [Theory]
    [InlineData("contestant/foo.calr")]     // Contains "test" but not in correct position
    [InlineData("testdata/foo.calr")]        // Starts with "test" but not "tests/"
    [InlineData("foo/testing/bar.calr")]     // Contains "testing" not "tests"
    public void IsTestPath_RequiresExactMatch(string path)
    {
        var result = IdValidator.IsTestPath(path);

        // These should NOT be recognized as test paths
        Assert.False(result);
    }

    #endregion

    #region Canonical ID Tests

    [Theory]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", true)]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", true)]
    [InlineData("c_01J5X7K9M2NPQRSTABWXYZ1234", true)]
    public void IsCanonicalId_RecognizesProductionIds(string id, bool expected)
    {
        var result = IdValidator.IsCanonicalId(id);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f001", false)]      // Test ID
    [InlineData("", false)]          // Empty
    [InlineData(null, false)]        // Null
    [InlineData("f_INVALID", false)] // Invalid ULID
    public void IsCanonicalId_RejectsNonCanonical(string? id, bool expected)
    {
        var result = IdValidator.IsCanonicalId(id);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Whitespace Handling Tests

    [Theory]
    [InlineData(" f_01J5X7K9M2NPQRSTABWXYZ1234")]   // Leading space
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234 ")]   // Trailing space
    [InlineData(" f_01J5X7K9M2NPQRSTABWXYZ1234 ")]  // Both
    public void WhitespaceAroundId_IsInvalid(string id)
    {
        // IDs with whitespace are invalid
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.NotEqual(IdValidationResult.Valid, result);
    }

    [Theory]
    [InlineData("f_ 01J5X7K9M2NPQRSTABWXYZ1234")]  // Space after prefix
    [InlineData("f _01J5X7K9M2NPQRSTABWXYZ1234")]  // Space before underscore
    public void WhitespaceInId_IsInvalid(string id)
    {
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.NotEqual(IdValidationResult.Valid, result);
    }

    #endregion

    #region Empty/Null Tests

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyOrNull_IsMissing(string? id)
    {
        var result = IdValidator.Validate(id, IdKind.Function, isTestPath: false);

        Assert.Equal(IdValidationResult.Missing, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidUlid_EmptyOrNull_IsFalse(string? ulid)
    {
        var result = IdValidator.IsValidUlid(ulid!);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsTestId_EmptyOrNull_IsFalse(string? id)
    {
        var result = IdValidator.IsTestId(id);

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsCanonicalId_EmptyOrNull_IsFalse(string? id)
    {
        var result = IdValidator.IsCanonicalId(id);

        Assert.False(result);
    }

    #endregion
}
