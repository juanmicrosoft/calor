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
