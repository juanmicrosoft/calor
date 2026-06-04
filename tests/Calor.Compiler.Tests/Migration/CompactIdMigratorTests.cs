using System.Text;
using Calor.Compiler.Ids;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="CompactIdMigrator"/>. These guard the
/// invariants the v6 RFC §16.F relies on for the
/// <c>calor fix --compact-ids</c> migration:
///
/// <list type="bullet">
///   <item>T-CIM-a  Rewrites a single ULID in a module opener.</item>
///   <item>T-CIM-b  Preserves additional positionals after the ID.</item>
///   <item>T-CIM-c  Rewrites closing-tag IDs.</item>
///   <item>T-CIM-d  Leaves compact IDs untouched (idempotent).</item>
///   <item>T-CIM-e  Leaves bare names (non-ID first positional) untouched.</item>
///   <item>T-CIM-f  Resolves per-file collisions (two ULIDs deriving to same compact).</item>
///   <item>T-CIM-g  Resolves cross-file collisions.</item>
///   <item>T-CIM-h  Avoids collision with existing compact IDs.</item>
///   <item>T-CIM-i  Round-trip migrate → revert is byte-exact.</item>
///   <item>T-CIM-j  Re-running migrate on already-migrated source is a no-op.</item>
///   <item>T-CIM-k  Does not rewrite ULID-shaped strings outside section markers.</item>
/// </list>
/// </summary>
public class CompactIdMigratorTests
{
    private static readonly CompactIdMigrator Sut = new();
    private static readonly UTF8Encoding Utf8 = new(false);

    private const string Ulid1 = "01J5X7K9M2NPQRSTABWXYZ1234";  // payload only
    private const string Ulid2 = "01J5X7K9M2NPQRSTABWXYZ9999";
    private const string Ulid3 = "01HZZZZZZZZZZZZZZZZZZZZZZZ";

    [Fact]
    public void T_CIM_a_RewritesModuleId()
    {
        var src = $"§M{{m_{Ulid1}:Calc}}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        var newText = migrated["a.calr"];
        Assert.StartsWith("§M{m_", newText);
        // Last 12 of ULID1 lowercased = "tabwxyz1234".  Wait that's 11.
        // ULID1 is 26 chars; last 12 = "STABWXYZ1234" → "stabwxyz1234".
        Assert.Contains("m_stabwxyz1234:Calc", newText);
        Assert.Single(log.Entries);
        var entry = log.Entries[0];
        Assert.Equal("a.calr", entry.File);
        Assert.Equal($"m_{Ulid1}", Encoding.UTF8.GetString(Convert.FromBase64String(entry.OriginalBytesBase64)));
    }

    [Fact]
    public void T_CIM_b_PreservesExtraPositionalsAfterId()
    {
        var src = $"§F{{f_{Ulid1}:divide:i32:public}}";
        var (migrated, _) = Sut.Migrate(Dict(("f.calr", src)));

        Assert.Equal("§F{f_stabwxyz1234:divide:i32:public}", migrated["f.calr"]);
    }

    [Fact]
    public void T_CIM_c_RewritesClosingTagId()
    {
        var src = $"§/F{{f_{Ulid1}}}";
        var (migrated, _) = Sut.Migrate(Dict(("c.calr", src)));

        Assert.Equal("§/F{f_stabwxyz1234}", migrated["c.calr"]);
    }

    [Fact]
    public void T_CIM_d_LeavesCompactIdsUntouched()
    {
        const string src = "§M{m_abc123def456:Calc}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        Assert.Equal(src, migrated["a.calr"]);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void T_CIM_e_LeavesBareNameUntouched()
    {
        const string src = "§M{Calc}\n§/M";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        Assert.Equal(src, migrated["a.calr"]);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void T_CIM_f_ResolvesPerFileCollision()
    {
        // Two distinct ULIDs sharing identical last-12 chars → the
        // deterministic derivation would map them to the same compact
        // payload. The migrator must mint a fresh compact for one of them.
        const string head1 = "01AAAAAAAAAAAA"; // 14 chars
        const string head2 = "01BBBBBBBBBBBB"; // 14 chars
        const string tail  = "XYZ123456789";   // 12 chars
        var u1 = head1 + tail;  // 26 chars
        var u2 = head2 + tail;  // 26 chars
        Assert.Equal(26, u1.Length);
        Assert.Equal(26, u2.Length);

        var src = $"§F{{f_{u1}:a:i32:public}}\n§F{{f_{u2}:b:i32:public}}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        // Both replaced (collision resolved), both 12-char compact payloads.
        Assert.Equal(2, log.Entries.Count);
        var newText = migrated["a.calr"];
        Assert.DoesNotContain(u1, newText);
        Assert.DoesNotContain(u2, newText);
        var lines = newText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var compact1 = ExtractPayloadFromLine(lines[0]);
        var compact2 = ExtractPayloadFromLine(lines[1]);
        Assert.NotEqual(compact1, compact2);
        Assert.Equal(12, compact1.Length);
        Assert.Equal(12, compact2.Length);
    }

    [Fact]
    public void T_CIM_g_ResolvesCrossFileCollision()
    {
        const string head1 = "01AAAAAAAAAAAA";
        const string head2 = "01BBBBBBBBBBBB";
        const string tail  = "XYZ123456789";
        var u1 = head1 + tail;
        var u2 = head2 + tail;

        var srcA = $"§M{{m_{u1}:Mod}}\n";
        var srcB = $"§M{{m_{u2}:Other}}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", srcA), ("b.calr", srcB)));

        Assert.Equal(2, log.Entries.Count);
        var newA = migrated["a.calr"];
        var newB = migrated["b.calr"];
        Assert.DoesNotContain(u1, newA);
        Assert.DoesNotContain(u2, newB);
        var compactA = ExtractPayloadFromLine(newA.TrimEnd());
        var compactB = ExtractPayloadFromLine(newB.TrimEnd());
        Assert.NotEqual(compactA, compactB);
    }

    [Fact]
    public void T_CIM_h_AvoidsCollisionWithExistingCompactId()
    {
        // The derived compact for ULID1 is "stabwxyz1234" (last 12,
        // lowercased). Plant a pre-existing canonical compact ID with
        // exactly that payload in another file — the migrator should
        // mint a fresh one for the ULID instead.
        const string existing = "f_stabwxyz1234";  // 12-char compact
        Assert.True(IdValidator.IsCompactId(existing));

        var srcA = $"§M{{m_{Ulid1}:Mod}}\n";
        var srcB = $"§F{{{existing}:fn:i32:public}}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", srcA), ("b.calr", srcB)));

        var newA = migrated["a.calr"];
        // The originally-derived "stabwxyz1234" was used by `existing`,
        // so ULID1 must have been re-minted to something else.
        Assert.DoesNotContain("m_stabwxyz1234", newA);
        // existing should be untouched.
        Assert.Equal(srcB, migrated["b.calr"]);
        Assert.Single(log.Entries);
        Assert.Equal("a.calr", log.Entries[0].File);
    }

    [Fact]
    public void T_CIM_i_RoundTripIsByteExact()
    {
        var src = $"§M{{m_{Ulid1}:Calc}}\n§F{{f_{Ulid2}:divide:i32:public}}\n§/F\n§/M\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        var restored = Sut.Revert(migrated, log);
        Assert.Equal(src, restored["a.calr"]);
    }

    [Fact]
    public void T_CIM_j_IdempotentOnAlreadyMigratedSource()
    {
        var src = $"§M{{m_{Ulid1}:Calc}}\n";
        var (migrated1, _) = Sut.Migrate(Dict(("a.calr", src)));
        var (migrated2, log2) = Sut.Migrate(migrated1);

        Assert.Equal(migrated1["a.calr"], migrated2["a.calr"]);
        Assert.Empty(log2.Entries);
    }

    [Fact]
    public void T_CIM_k_DoesNotRewriteUlidsOutsideSectionMarkers()
    {
        // ULID-shaped string in a comment-like position or string literal
        // (here: just embedded prose text). Migrator must not touch.
        var src = $"# see f_{Ulid1} for details\n§M{{Calc}}\n";
        var (migrated, log) = Sut.Migrate(Dict(("a.calr", src)));

        Assert.Equal(src, migrated["a.calr"]);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Mapping_IsDeterministicAcrossRuns()
    {
        // Same input → same output. Verify by running twice and comparing.
        var src = $"§M{{m_{Ulid1}:Calc}}\n§F{{f_{Ulid2}:fn:i32:public}}\n";
        var (m1, _) = Sut.Migrate(Dict(("a.calr", src)));
        var (m2, _) = Sut.Migrate(Dict(("a.calr", src)));
        Assert.Equal(m1["a.calr"], m2["a.calr"]);
    }

    private static IReadOnlyDictionary<string, string> Dict(
        params (string Key, string Value)[] entries)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in entries) d[k] = v;
        return d;
    }

    private static string ExtractPayloadFromLine(string line)
    {
        // Lines like "§M{m_abc123def456:Calc}" or "§F{f_<payload>:..."
        var open = line.IndexOf('{');
        var inner = line.Substring(open + 1);
        var id = inner.Split(':')[0].TrimEnd('}');
        var underscore = id.IndexOf('_');
        return id.Substring(underscore + 1);
    }
}
