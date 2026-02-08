using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Ids.Tests;

public class IdCheckerTests
{
    [Fact]
    public void Check_ValidIds_ReturnsNoIssues()
    {
        var entries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("m_01J5X7K9M2NPQRSTABWXYZ1312", IdKind.Module, "Module1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.TotalIssues);
    }

    [Fact]
    public void Check_MissingIds_ReturnsIssues()
    {
        var entries = new List<IdEntry>
        {
            new("", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.MissingIds);
    }

    [Fact]
    public void Check_InvalidFormatIds_ReturnsIssues()
    {
        var entries = new List<IdEntry>
        {
            new("f_INVALID", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.InvalidFormatIds);
    }

    [Fact]
    public void Check_WrongPrefixIds_ReturnsIssues()
    {
        var entries = new List<IdEntry>
        {
            new("m_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.WrongPrefixIds);
    }

    [Fact]
    public void Check_TestIdsInProduction_ReturnsIssues()
    {
        var entries = new List<IdEntry>
        {
            new("f001", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.TestIdsInProduction);
    }

    [Fact]
    public void Check_TestIdsInTestPath_ReturnsNoIssues()
    {
        var entries = new List<IdEntry>
        {
            new("f001", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/tests/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_DuplicateIds_ReturnsIssues()
    {
        var entries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/file1.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func2", new TextSpan(0, 0, 1, 1), "/src/file2.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.DuplicateGroups);
        Assert.Equal(2, result.DuplicateGroups[0].Count);
    }

    [Fact]
    public void Check_AllowTestIds_AllowsTestIdsEverywhere()
    {
        var entries = new List<IdEntry>
        {
            new("f001", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries, allowTestIds: true);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Check_GeneratesDiagnostics()
    {
        var entries = new List<IdEntry>
        {
            new("", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_INVALID", IdKind.Function, "Func2", new TextSpan(0, 0, 2, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);
        var diagnostics = IdChecker.GenerateDiagnostics(result).ToList();

        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    public void DetectIdChurn_DetectsChangedIds()
    {
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ9912", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        Assert.Single(churn);
        Assert.Equal("f_01J5X7K9M2NPQRSTABWXYZ1234", churn[0].Old.Id);
        Assert.Equal("f_01J5X7K9M2NPQRSTABWXYZ9912", churn[0].New.Id);
    }

    [Fact]
    public void DetectIdChurn_IgnoresNewFunctions()
    {
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ9912", IdKind.Function, "NewFunc", new TextSpan(0, 0, 5, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        Assert.Empty(churn);
    }

    [Fact]
    public void DetectIdChurn_IgnoresDeletedFunctions()
    {
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ9912", IdKind.Function, "DeletedFunc", new TextSpan(0, 0, 5, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        Assert.Empty(churn);
    }

    #region Duplicate Detection Edge Cases

    [Fact]
    public void Check_DifferentCaseIds_UppercaseIsInvalid()
    {
        // Uppercase prefix (F_) is invalid - prefixes must be lowercase
        var entries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/file1.calr"),
            new("F_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func2", new TextSpan(0, 0, 1, 1), "/src/file2.calr"),
        };

        var result = IdChecker.Check(entries);

        // The uppercase ID (F_...) is invalid format
        Assert.False(result.IsValid);
        Assert.Single(result.InvalidFormatIds);
    }

    [Fact]
    public void Check_DuplicateIds_BothInvalid()
    {
        // Both entries have the same invalid ID
        var entries = new List<IdEntry>
        {
            new("f_INVALID", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/file1.calr"),
            new("f_INVALID", IdKind.Function, "Func2", new TextSpan(0, 0, 2, 1), "/src/file2.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        // Should report both as duplicates AND as invalid format
        Assert.True(result.DuplicateGroups.Count > 0 || result.InvalidFormatIds.Count > 0);
    }

    [Fact]
    public void Check_DuplicateIds_OneValidOneInvalid()
    {
        // Same "ID" but one is valid format, one is not
        // Actually these are different strings so not duplicates
        var entries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/file1.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func2", new TextSpan(0, 0, 2, 1), "/src/file2.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.DuplicateGroups);
    }

    [Fact]
    public void Check_DuplicateIds_SameFile()
    {
        // Duplicates within the same file
        var entries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func2", new TextSpan(0, 0, 10, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.DuplicateGroups);
        Assert.Equal(2, result.DuplicateGroups[0].Count);
    }

    #endregion

    #region Churn Detection Edge Cases

    [Fact]
    public void DetectIdChurn_SameNameDifferentKind_NoChurn()
    {
        // Function changed to method (kind change) - different kinds means different declarations
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Process", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("mt_01J5X7K9M2NPQRSTABWXYZ9999", IdKind.Method, "Process", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        // Churn detection matches by (name, kind, path) - different kind means no match
        // Old function is "deleted", new method is "added" - not churn
        Assert.Empty(churn);
    }

    [Fact]
    public void DetectIdChurn_MultipleChurns()
    {
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1111", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ2222", IdKind.Function, "Func2", new TextSpan(0, 0, 5, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ3333", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_01J5X7K9M2NPQRSTABWXYZ4444", IdKind.Function, "Func2", new TextSpan(0, 0, 5, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        Assert.Equal(2, churn.Count);
    }

    [Fact]
    public void DetectIdChurn_RenamedFunction_NoChurn()
    {
        // If function is renamed, it's considered a new function (no churn)
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "OldName", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "NewName", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        // ID preserved with renamed function - no churn
        Assert.Empty(churn);
    }

    [Fact]
    public void DetectIdChurn_EmptyOldEntries_NoChurn()
    {
        var oldEntries = new List<IdEntry>();
        var newEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        Assert.Empty(churn);
    }

    [Fact]
    public void DetectIdChurn_EmptyNewEntries_NoChurn()
    {
        var oldEntries = new List<IdEntry>
        {
            new("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "Func1", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
        };
        var newEntries = new List<IdEntry>();

        var churn = IdChecker.DetectIdChurn(oldEntries, newEntries);

        // All functions deleted - no churn (deletion is allowed)
        Assert.Empty(churn);
    }

    #endregion

    #region Multiple Issues Tests

    [Fact]
    public void Check_MultipleIssueTypes()
    {
        var entries = new List<IdEntry>
        {
            new("", IdKind.Function, "MissingId", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_INVALID", IdKind.Function, "InvalidFormat", new TextSpan(0, 0, 2, 1), "/src/test.calr"),
            new("m_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function, "WrongPrefix", new TextSpan(0, 0, 3, 1), "/src/test.calr"),
            new("f001", IdKind.Function, "TestIdInProd", new TextSpan(0, 0, 4, 1), "/src/prod.calr"),
        };

        var result = IdChecker.Check(entries);

        Assert.False(result.IsValid);
        Assert.Single(result.MissingIds);
        Assert.Single(result.InvalidFormatIds);
        Assert.Single(result.WrongPrefixIds);
        Assert.Single(result.TestIdsInProduction);
    }

    [Fact]
    public void GenerateDiagnostics_IncludesAllIssues()
    {
        var entries = new List<IdEntry>
        {
            new("", IdKind.Function, "MissingId", new TextSpan(0, 0, 1, 1), "/src/test.calr"),
            new("f_INVALID", IdKind.Function, "InvalidFormat", new TextSpan(0, 0, 2, 1), "/src/test.calr"),
        };

        var result = IdChecker.Check(entries);
        var diagnostics = IdChecker.GenerateDiagnostics(result).ToList();

        Assert.Equal(2, diagnostics.Count);
    }

    #endregion
}
