using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for effect subtyping relationships (e.g., rw encompasses r and w).
/// </summary>
public class EffectSubtypingTests
{
    [Fact]
    public void FilesystemReadWrite_Encompasses_FilesystemRead()
    {
        var declared = (EffectKind.IO, "filesystem_readwrite");
        var required = (EffectKind.IO, "filesystem_read");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void FilesystemReadWrite_Encompasses_FilesystemWrite()
    {
        var declared = (EffectKind.IO, "filesystem_readwrite");
        var required = (EffectKind.IO, "filesystem_write");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void NetworkReadWrite_Encompasses_NetworkRead()
    {
        var declared = (EffectKind.IO, "network_readwrite");
        var required = (EffectKind.IO, "network_read");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void NetworkReadWrite_Encompasses_NetworkWrite()
    {
        var declared = (EffectKind.IO, "network_readwrite");
        var required = (EffectKind.IO, "network_write");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void DatabaseReadWrite_Encompasses_DatabaseRead()
    {
        var declared = (EffectKind.IO, "database_readwrite");
        var required = (EffectKind.IO, "database_read");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void DatabaseReadWrite_Encompasses_DatabaseWrite()
    {
        var declared = (EffectKind.IO, "database_readwrite");
        var required = (EffectKind.IO, "database_write");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void EnvironmentReadWrite_Encompasses_EnvironmentRead()
    {
        var declared = (EffectKind.IO, "environment_readwrite");
        var required = (EffectKind.IO, "environment_read");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void EnvironmentReadWrite_Encompasses_EnvironmentWrite()
    {
        var declared = (EffectKind.IO, "environment_readwrite");
        var required = (EffectKind.IO, "environment_write");

        Assert.True(EffectSubtyping.Encompasses(declared, required));
    }

    // -------------------------------------------------------------- P7 -----
    // Design-doc §4.1, 0.15's WIDENING: a bare family code encompasses its
    // narrow siblings. 0.14 did not relate them at all, which under rows would
    // surface at every binding site instead of only at a declaration.
    //
    // Discriminating revert: remove the "io:database" entry from
    // EffectRow.FamilySubtypes and FamilyCodeEncompassesNarrowCode goes red
    // while the fs:rw regression below stays green.

    [Theory]
    // database
    [InlineData("database", "database_read")]
    [InlineData("database", "database_write")]
    [InlineData("database", "database_readwrite")]
    // network
    [InlineData("network", "network_read")]
    [InlineData("network", "network_write")]
    [InlineData("network", "network_readwrite")]
    // environment
    [InlineData("environment", "environment_read")]
    [InlineData("environment", "environment_write")]
    [InlineData("environment", "environment_readwrite")]
    public void FamilyCodeEncompassesNarrowCode(string family, string narrow)
    {
        Assert.True(EffectSubtyping.Encompasses(
            (EffectKind.IO, family),
            (EffectKind.IO, narrow)));
    }

    [Fact]
    public void FamilyCodeWidening_IsOneWay()
    {
        // The narrow code does NOT encompass the family — a §E{db:r} declaration
        // must not admit an operation that does arbitrary database work.
        Assert.False(EffectSubtyping.Encompasses(
            (EffectKind.IO, "database_read"),
            (EffectKind.IO, "database")));
    }

    [Fact]
    public void FilesystemHasNoBareFamilyCode_SoReadWriteStaysItsTop()
    {
        // §4.1: `filesystem` is not in the registry, so fs:rw remains the
        // filesystem top. This is the regression half of P7 — it must keep
        // passing while the three families above start passing.
        Assert.True(EffectSubtyping.Encompasses(
            (EffectKind.IO, "filesystem_readwrite"),
            (EffectKind.IO, "filesystem_write")));
        Assert.False(EffectSubtyping.Encompasses(
            (EffectKind.IO, "filesystem"),
            (EffectKind.IO, "filesystem_write")));
    }

    [Fact]
    public void ProcAndHttpHaveNoNarrowSiblings()
    {
        // §4.1 names them explicitly. Nothing to widen, and nothing that widens
        // into them.
        Assert.False(EffectSubtyping.Encompasses(
            (EffectKind.IO, "process"),
            (EffectKind.IO, "environment_read")));
        Assert.False(EffectSubtyping.Encompasses(
            (EffectKind.IO, "network"),
            (EffectKind.IO, "http")));
    }

    [Fact]
    public void FamilyWidening_ReachesEffectSetIsSubsetOf()
    {
        // The widening is not a private fact about Encompasses: it is what a
        // §E{db} declaration ADMITS. This is the sentence §4.1 writes.
        Assert.True(EffectSet.From("db:r").IsSubsetOf(EffectSet.From("db")));
        Assert.True(EffectSet.From("net:w", "env:r").IsSubsetOf(EffectSet.From("net", "env")));
        Assert.False(EffectSet.From("db").IsSubsetOf(EffectSet.From("db:r")));
    }

    [Fact]
    public void ExactMatch_IsEncompassed()
    {
        var effect = (EffectKind.IO, "console_write");

        Assert.True(EffectSubtyping.Encompasses(effect, effect));
    }

    [Fact]
    public void ReadEffect_DoesNotEncompass_WriteEffect()
    {
        var declared = (EffectKind.IO, "filesystem_read");
        var required = (EffectKind.IO, "filesystem_write");

        Assert.False(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void DifferentCategories_DoNotEncompass()
    {
        var declared = (EffectKind.IO, "filesystem_readwrite");
        var required = (EffectKind.IO, "network_read");

        Assert.False(EffectSubtyping.Encompasses(declared, required));
    }

    [Fact]
    public void EffectSet_IsSubsetOf_WithSubtyping()
    {
        // Declared: fs:rw
        var declared = EffectSet.From("fs:rw");
        // Required: fs:r
        var required = EffectSet.From("fs:r");

        Assert.True(required.IsSubsetOf(declared));
    }

    [Fact]
    public void EffectSet_IsSubsetOf_WithMultipleEffects()
    {
        // Declared: net:rw, db:rw
        var declared = EffectSet.From("net:rw", "db:rw");
        // Required: net:r, db:w
        var required = EffectSet.From("net:r", "db:w");

        Assert.True(required.IsSubsetOf(declared));
    }

    [Fact]
    public void EffectSet_IsNotSubsetOf_WhenMissing()
    {
        // Declared: fs:rw
        var declared = EffectSet.From("fs:rw");
        // Required: net:r
        var required = EffectSet.From("net:r");

        Assert.False(required.IsSubsetOf(declared));
    }

    [Fact]
    public void EffectSet_Except_WithSubtyping()
    {
        // Declared: fs:rw, cw
        var declared = EffectSet.From("fs:rw", "cw");
        // Required: fs:r, net:r
        var required = EffectSet.From("fs:r", "net:r");

        var forbidden = required.Except(declared).ToList();

        // fs:r is covered by fs:rw, but net:r is not
        Assert.Single(forbidden);
        Assert.Equal((EffectKind.IO, "network_read"), forbidden[0]);
    }

    [Fact]
    public void GetEncompassedEffects_ReturnsAllSubtypes()
    {
        var effect = (EffectKind.IO, "filesystem_readwrite");
        var encompassed = EffectSubtyping.GetEncompassedEffects(effect).ToList();

        Assert.Contains((EffectKind.IO, "filesystem_readwrite"), encompassed);
        Assert.Contains((EffectKind.IO, "filesystem_read"), encompassed);
        Assert.Contains((EffectKind.IO, "filesystem_write"), encompassed);
    }

    [Fact]
    public void GetBroadestEncompassing_ReturnsParentEffect()
    {
        var effect = (EffectKind.IO, "filesystem_read");
        var broadest = EffectSubtyping.GetBroadestEncompassing(effect);

        Assert.Equal((EffectKind.IO, "filesystem_readwrite"), broadest);
    }

    [Theory]
    // v0.15 §4.1, review round 1 MAJOR 1. On 0.14 nothing covered a *_readwrite
    // code, so GetBroadestEncompassing returned it UNCHANGED. The bare family
    // codes now cover them, so the answer moves — and that is correct: `db` really
    // is broader than `db:rw`. Suppressing it would mean dropping db:rw from db's
    // subtype list, which would make §E{db} stop admitting db:rw and undo the
    // widening. The method has no production caller, so this is the only place
    // the change is observable; it is pinned rather than left to be discovered.
    [InlineData("database_readwrite", "database")]
    [InlineData("network_readwrite", "network")]
    [InlineData("environment_readwrite", "environment")]
    public void GetBroadestEncompassing_OfAReadWriteCode_IsNowItsBareFamily(
        string readWrite, string family)
    {
        Assert.Equal(
            (EffectKind.IO, family),
            EffectSubtyping.GetBroadestEncompassing((EffectKind.IO, readWrite)));
    }

    [Fact]
    public void GetBroadestEncompassing_OfANarrowCode_IsUnchangedFrom014()
    {
        // The half the :rw-first ordering DOES protect: database_read is covered
        // by both database_readwrite and database, and still answers with the
        // former, exactly as on 0.14. Reorder FamilySubtypes so the bare families
        // come first and this goes red while the theory above stays green.
        Assert.Equal(
            (EffectKind.IO, "database_readwrite"),
            EffectSubtyping.GetBroadestEncompassing((EffectKind.IO, "database_read")));
        Assert.Equal(
            (EffectKind.IO, "filesystem_readwrite"),
            EffectSubtyping.GetBroadestEncompassing((EffectKind.IO, "filesystem_read")));
    }

    [Fact]
    public void GetBroadestEncompassing_ReturnsSelf_WhenNoParent()
    {
        var effect = (EffectKind.IO, "console_write");
        var broadest = EffectSubtyping.GetBroadestEncompassing(effect);

        Assert.Equal(effect, broadest);
    }

    [Fact]
    public void IsGranularEffect_IdentifiesReadWriteEffects()
    {
        Assert.True(EffectSubtyping.IsGranularEffect("filesystem_read"));
        Assert.True(EffectSubtyping.IsGranularEffect("network_write"));
        Assert.False(EffectSubtyping.IsReadWriteEffect("filesystem_read"));
        Assert.False(EffectSubtyping.IsReadWriteEffect("network_write"));
    }

    [Fact]
    public void IsReadWriteEffect_IdentifiesCombinedEffects()
    {
        Assert.True(EffectSubtyping.IsReadWriteEffect("filesystem_readwrite"));
        Assert.True(EffectSubtyping.IsReadWriteEffect("network_readwrite"));
        Assert.False(EffectSubtyping.IsReadWriteEffect("filesystem_read"));
        Assert.False(EffectSubtyping.IsReadWriteEffect("console_write"));
    }
}
