using Calor.Compiler.Experiments;
using Xunit;

namespace Calor.Compiler.Tests.Experiments;

/// <summary>
/// Tests for <see cref="RegistryValidator"/> (§5.0f tamper-evidence check).
/// The scenario matrix covers every way a registry diff could be wrong or right.
/// </summary>
public class RegistryValidatorTests
{
    private static RegistryEntry Entry(string id, string tag = "Dataflow", string codeClass = "unwrap",
        string dir = "up", string status = RegistryStatus.PreRegisteredStage1,
        string? supersedes = null, string? holdOwner = null) => new()
    {
        Id = id,
        Tag = tag,
        Hypothesis = $"hypothesis-for-{id}",
        TupleCodeClass = codeClass,
        TupleEffectDirection = dir,
        Status = status,
        Supersedes = supersedes,
        HoldOwner = holdOwner
    };

    private static RegistryDocument Doc(params RegistryEntry[] entries)
        => new() { Entries = entries.ToList() };

    // ========================================================================
    // The happy path: additions only
    // ========================================================================

    [Fact]
    public void NoChanges_ReportsValid()
    {
        var baseDoc = Doc(Entry("A"));
        var headDoc = Doc(Entry("A"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.True(result.IsValid);
        Assert.Equal(0, result.EntriesAdded);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void AddNewEntry_ReportsValid()
    {
        var baseDoc = Doc(Entry("A"));
        var headDoc = Doc(Entry("A"), Entry("B"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.True(result.IsValid);
        Assert.Equal(1, result.EntriesAdded);
    }

    [Fact]
    public void AddMultipleEntries_ReportsValid_AndCountsAll()
    {
        var baseDoc = Doc(Entry("A"));
        var headDoc = Doc(Entry("A"), Entry("B"), Entry("C"), Entry("D"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.True(result.IsValid);
        Assert.Equal(3, result.EntriesAdded);
    }

    [Fact]
    public void EmptyBase_AddingFirstEntries_ReportsValid()
    {
        var baseDoc = Doc();
        var headDoc = Doc(Entry("A"), Entry("B"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.EntriesAdded);
    }

    [Fact]
    public void NewSupersedingEntry_ReportsValid()
    {
        // Legitimate correction: add a new entry that supersedes an existing one,
        // leaving the original entry's fields intact.
        var baseDoc = Doc(Entry("A", status: RegistryStatus.PreRegisteredStage1));
        var headDoc = Doc(
            Entry("A", status: RegistryStatus.PreRegisteredStage1), // unchanged
            Entry("A-stage2", status: RegistryStatus.PreRegisteredStage2, supersedes: "A"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.True(result.IsValid);
        Assert.Equal(1, result.EntriesAdded);
    }

    // ========================================================================
    // Tamper: field modifications
    // ========================================================================

    [Fact]
    public void ModifyStatus_Rejected()
    {
        var baseDoc = Doc(Entry("A", status: RegistryStatus.PreRegisteredStage1));
        var headDoc = Doc(Entry("A", status: RegistryStatus.Promoted));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);
        Assert.Equal("field-modified", result.Violations[0].Kind);
        Assert.Equal("A", result.Violations[0].EntryId);
        Assert.Contains("status", result.Violations[0].Message);
    }

    [Fact]
    public void ModifyTag_Rejected()
    {
        var baseDoc = Doc(Entry("A", tag: "Dataflow"));
        var headDoc = Doc(Entry("A", tag: "Pattern"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Message.Contains("tag"));
    }

    [Fact]
    public void ModifyMultipleFields_ReportsAllViolations()
    {
        var baseDoc = Doc(Entry("A", tag: "Dataflow", status: RegistryStatus.PreRegisteredStage1));
        var headDoc = Doc(Entry("A", tag: "Pattern", status: RegistryStatus.Promoted));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Violations.Count);
    }

    [Fact]
    public void ModifyHypothesisText_Rejected()
    {
        var baseDoc = Doc(Entry("A"));
        var headDoc = Doc(new RegistryEntry
        {
            Id = "A",
            Tag = "Dataflow",
            Hypothesis = "CHANGED",
            TupleCodeClass = "unwrap",
            TupleEffectDirection = "up",
            Status = RegistryStatus.PreRegisteredStage1
        });
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Message.Contains("hypothesis"));
    }

    [Fact]
    public void ModifyCommitShaOrMergedAt_Rejected()
    {
        // CI-filled fields are still "fields" — once set, they cannot change.
        var baseEntry = Entry("A");
        baseEntry.CommitSha = "abc123";
        baseEntry.MergedAt = "2026-04-21T00:00:00Z";

        var headEntry = Entry("A");
        headEntry.CommitSha = "def456";
        headEntry.MergedAt = "2026-04-21T00:00:00Z";

        var result = RegistryValidator.Validate(Doc(baseEntry), Doc(headEntry));
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Message.Contains("commit_sha"));
    }

    [Fact]
    public void AddHoldOwnerToPreviouslyNullField_Rejected()
    {
        // Setting a previously-null field is still a modification.
        var baseDoc = Doc(Entry("A", holdOwner: null));
        var headDoc = Doc(Entry("A", holdOwner: "alice"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Message.Contains("hold_owner"));
    }

    // ========================================================================
    // Tamper: deletion
    // ========================================================================

    [Fact]
    public void DeleteExistingEntry_Rejected()
    {
        var baseDoc = Doc(Entry("A"), Entry("B"));
        var headDoc = Doc(Entry("A"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Single(result.Violations);
        Assert.Equal("entry-deleted", result.Violations[0].Kind);
        Assert.Equal("B", result.Violations[0].EntryId);
    }

    [Fact]
    public void DeleteAllEntries_ReportsEachAsViolation()
    {
        var baseDoc = Doc(Entry("A"), Entry("B"), Entry("C"));
        var headDoc = Doc();
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Violations.Count);
        Assert.All(result.Violations, v => Assert.Equal("entry-deleted", v.Kind));
    }

    // ========================================================================
    // Malformed: duplicate ids / missing ids
    // ========================================================================

    [Fact]
    public void DuplicateIdInHead_Rejected()
    {
        var baseDoc = Doc();
        var headDoc = Doc(Entry("A"), Entry("A"));
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Kind == "duplicate-id");
    }

    [Fact]
    public void MissingIdInHead_Rejected()
    {
        var baseDoc = Doc();
        var headDoc = Doc(new RegistryEntry
        {
            Id = "",
            Tag = "Dataflow",
            TupleCodeClass = "unwrap",
            TupleEffectDirection = "up",
            Status = RegistryStatus.PreRegisteredStage1
        });
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Kind == "missing-id");
    }

    // ========================================================================
    // Combination: additions + tamper — both violations reported
    // ========================================================================

    [Fact]
    public void AdditionPlusTamper_BothReported()
    {
        var baseDoc = Doc(Entry("A", status: RegistryStatus.PreRegisteredStage1));
        var headDoc = Doc(
            Entry("A", status: RegistryStatus.Promoted), // TAMPER
            Entry("B", status: RegistryStatus.PreRegisteredStage1)); // legitimate add
        var result = RegistryValidator.Validate(baseDoc, headDoc);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.EntriesAdded);
        Assert.Single(result.Violations);
    }

    // ========================================================================
    // Integration: real JSON files on disk — simulates the CI use case
    // ========================================================================

    [Fact]
    public void IntegrationFromJsonFiles_DetectsTamperInRealJsonRoundTrip()
    {
        var baseJson = """{"entries":[{"id":"A","tag":"Dataflow","hypothesis":"test","tuple_code_class":"unwrap","tuple_effect_direction":"up","status":"pre-registered-stage-1"}]}""";
        var headJson = """{"entries":[{"id":"A","tag":"Dataflow","hypothesis":"test","tuple_code_class":"unwrap","tuple_effect_direction":"up","status":"promoted"}]}""";

        var basePath = Path.GetTempFileName();
        var headPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(basePath, baseJson);
            File.WriteAllText(headPath, headJson);

            var baseDoc = LoadForTest(basePath);
            var headDoc = LoadForTest(headPath);
            var result = RegistryValidator.Validate(baseDoc, headDoc);

            Assert.False(result.IsValid);
            var violation = Assert.Single(result.Violations);
            Assert.Equal("field-modified", violation.Kind);
            Assert.Equal("A", violation.EntryId);
            Assert.Contains("status", violation.Message);
        }
        finally
        {
            try { File.Delete(basePath); } catch { }
            try { File.Delete(headPath); } catch { }
        }
    }

    [Fact]
    public void IntegrationFromJsonFiles_CleanAdditionPasses()
    {
        var baseJson = """{"entries":[{"id":"A","tag":"Dataflow","hypothesis":"test","tuple_code_class":"unwrap","tuple_effect_direction":"up","status":"pre-registered-stage-1"}]}""";
        var headJson = """{"entries":[{"id":"A","tag":"Dataflow","hypothesis":"test","tuple_code_class":"unwrap","tuple_effect_direction":"up","status":"pre-registered-stage-1"},{"id":"B","tag":"Pattern","hypothesis":"test","tuple_code_class":"option-match","tuple_effect_direction":"up","status":"pre-registered-stage-1"}]}""";

        var basePath = Path.GetTempFileName();
        var headPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(basePath, baseJson);
            File.WriteAllText(headPath, headJson);

            var result = RegistryValidator.Validate(LoadForTest(basePath), LoadForTest(headPath));
            Assert.True(result.IsValid);
            Assert.Equal(1, result.EntriesAdded);
        }
        finally
        {
            try { File.Delete(basePath); } catch { }
            try { File.Delete(headPath); } catch { }
        }
    }

    private static RegistryDocument LoadForTest(string path)
    {
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<RegistryDocument>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower })
            ?? new RegistryDocument();
    }
}
