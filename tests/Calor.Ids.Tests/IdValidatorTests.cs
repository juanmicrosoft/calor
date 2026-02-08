using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

public class IdValidatorTests
{
    [Theory]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, false, IdValidationResult.Valid)]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Module, false, IdValidationResult.Valid)]
    [InlineData("c_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Class, false, IdValidationResult.Valid)]
    public void Validate_ValidCanonicalIds(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", IdKind.Function, false, IdValidationResult.Missing)]
    [InlineData(null, IdKind.Function, false, IdValidationResult.Missing)]
    public void Validate_MissingIds(string? id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f001", IdKind.Function, true, IdValidationResult.Valid)]
    [InlineData("m001", IdKind.Module, true, IdValidationResult.Valid)]
    [InlineData("c001", IdKind.Class, true, IdValidationResult.Valid)]
    public void Validate_TestIdsAllowedInTestPath(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f001", IdKind.Function, false, IdValidationResult.TestIdInProduction)]
    [InlineData("m001", IdKind.Module, false, IdValidationResult.TestIdInProduction)]
    public void Validate_TestIdsNotAllowedInProduction(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, false, IdValidationResult.WrongPrefix)]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Module, false, IdValidationResult.WrongPrefix)]
    public void Validate_WrongPrefix(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f_INVALID", IdKind.Function, false, IdValidationResult.InvalidFormat)]
    [InlineData("f_01J5X7", IdKind.Function, false, IdValidationResult.InvalidFormat)] // Too short
    public void Validate_InvalidFormat(string id, IdKind kind, bool isTestPath, IdValidationResult expected)
    {
        var result = IdValidator.Validate(id, kind, isTestPath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("01J5X7K9M2NPQRSTABWXYZ1234", true)]
    [InlineData("01ABC2DEF3GHJ4KNM5NPR6STAB", true)]  // Replaced L with N (valid Crockford Base32)
    public void IsValidUlid_ValidUlids(string ulid, bool expected)
    {
        var result = IdValidator.IsValidUlid(ulid);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("01J5X7K9M2NPQRSTUVWXYZ1", false)] // 25 chars
    [InlineData("01J5X7K9M2NPQRSTUVWXYZ123", false)] // 27 chars
    public void IsValidUlid_InvalidUlids(string ulid, bool expected)
    {
        var result = IdValidator.IsValidUlid(ulid);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f001", true)]
    [InlineData("m001", true)]
    [InlineData("c001", true)]
    [InlineData("mt001", true)]
    [InlineData("ctor001", true)]
    [InlineData("p001", true)]
    [InlineData("i001", true)]
    [InlineData("e001", true)]
    public void IsTestId_RecognizesTestIds(string id, bool expected)
    {
        var result = IdValidator.IsTestId(id);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", false)]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTestId_RejectsNonTestIds(string? id, bool expected)
    {
        var result = IdValidator.IsTestId(id);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/path/to/tests/file.calr", true)]
    [InlineData("/path/to/docs/file.calr", true)]
    [InlineData("/path/to/examples/file.calr", true)]
    [InlineData("tests/file.calr", true)]
    [InlineData("docs/file.calr", true)]
    [InlineData("examples/file.calr", true)]
    public void IsTestPath_RecognizesTestPaths(string path, bool expected)
    {
        var result = IdValidator.IsTestPath(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/path/to/src/file.calr", false)]
    [InlineData("src/file.calr", false)]
    [InlineData("file.calr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTestPath_RejectsProductionPaths(string? path, bool expected)
    {
        var result = IdValidator.IsTestPath(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", true)]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", true)]
    public void IsCanonicalId_RecognizesCanonicalIds(string id, bool expected)
    {
        var result = IdValidator.IsCanonicalId(id);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("f001", false)]
    [InlineData("m001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCanonicalId_RejectsNonCanonicalIds(string? id, bool expected)
    {
        var result = IdValidator.IsCanonicalId(id);

        Assert.Equal(expected, result);
    }
}
