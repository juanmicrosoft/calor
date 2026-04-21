using System.Text.Json;
using Calor.Compiler.Experiments;
using Xunit;

namespace Calor.Compiler.Tests.Experiments;

/// <summary>
/// Tests for the Registry query API (§5.0h of the Calor-native type-system research plan).
/// Uses fixture registries with known ground-truth so assertions are mechanical.
/// </summary>
public class RegistryTests
{
    // ========================================================================
    // Fixture helpers
    // ========================================================================

    private static Registry FromEntries(params RegistryEntry[] entries)
    {
        var doc = new RegistryDocument { Entries = entries.ToList() };
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            return Registry.Load(tmp);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    private static RegistryEntry Stage1(string id, string tag = "Dataflow", string codeClass = "unwrap", string dir = "up") => new()
    {
        Id = id,
        Tag = tag,
        Hypothesis = "test",
        TupleCodeClass = codeClass,
        TupleEffectDirection = dir,
        Status = RegistryStatus.PreRegisteredStage1
    };

    private static RegistryEntry Terminal(string id, string status, string? supersedes = null, string tag = "Dataflow", string codeClass = "unwrap", string dir = "up")
        => new()
        {
            Id = id,
            Tag = tag,
            Hypothesis = "test",
            TupleCodeClass = codeClass,
            TupleEffectDirection = dir,
            Status = status,
            Supersedes = supersedes
        };

    // ========================================================================
    // Empty / missing-file behavior
    // ========================================================================

    [Fact]
    public void Empty_ReturnsZeroEntries()
    {
        var r = Registry.Empty;
        Assert.Equal(0, r.Count);
        Assert.Empty(r.Entries);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var r = Registry.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void CurrentState_EmptyRegistry_NoMatch()
    {
        var result = Registry.Empty.CurrentState("anything");
        Assert.Null(result.CurrentStatus);
        Assert.Empty(result.Chain);
    }

    // ========================================================================
    // Query 1: current-state — chain walking
    // ========================================================================

    [Fact]
    public void CurrentState_SingleEntry_ReturnsStatusAndSingletonChain()
    {
        var r = FromEntries(Stage1("TIER1A"));
        var result = r.CurrentState("TIER1A");
        Assert.Equal(RegistryStatus.PreRegisteredStage1, result.CurrentStatus);
        Assert.Equal("TIER1A", result.HeadEntryId);
        Assert.Single(result.Chain);
    }

    [Fact]
    public void CurrentState_ChainOfThree_ReturnsLatestStatusAndOrderedChain()
    {
        var r = FromEntries(
            Stage1("TIER1A"),
            Terminal("TIER1A-stage2", RegistryStatus.PreRegisteredStage2, supersedes: "TIER1A"),
            Terminal("TIER1A-promoted", RegistryStatus.Promoted, supersedes: "TIER1A-stage2"));

        var result = r.CurrentState("TIER1A");
        Assert.Equal(RegistryStatus.Promoted, result.CurrentStatus);
        Assert.Equal("TIER1A-promoted", result.HeadEntryId);
        Assert.Equal(3, result.Chain.Count);
        // Chain ordered latest → origin
        Assert.Equal("TIER1A-promoted", result.Chain[0].Id);
        Assert.Equal("TIER1A-stage2", result.Chain[1].Id);
        Assert.Equal("TIER1A", result.Chain[2].Id);
    }

    [Fact]
    public void CurrentState_QueriedByMiddleEntry_ReturnsHeadChain()
    {
        var r = FromEntries(
            Stage1("TIER1A"),
            Terminal("TIER1A-stage2", RegistryStatus.PreRegisteredStage2, supersedes: "TIER1A"),
            Terminal("TIER1A-promoted", RegistryStatus.Promoted, supersedes: "TIER1A-stage2"));

        // Querying by middle entry's ID should still find the head chain.
        var result = r.CurrentState("TIER1A-stage2");
        Assert.Equal(RegistryStatus.Promoted, result.CurrentStatus);
        Assert.Equal(3, result.Chain.Count);
    }

    [Fact]
    public void CurrentState_UnknownId_ReturnsNull()
    {
        var r = FromEntries(Stage1("TIER1A"));
        var result = r.CurrentState("TIER-UNKNOWN");
        Assert.Null(result.CurrentStatus);
        Assert.Empty(result.Chain);
    }

    [Fact]
    public void CurrentState_CycleDetection_TerminatesWithoutInfiniteLoop()
    {
        // Deliberate cycle: A supersedes B supersedes A. Should not hang.
        var r = FromEntries(
            new RegistryEntry { Id = "A", Tag = "Dataflow", TupleCodeClass = "x", TupleEffectDirection = "up", Status = RegistryStatus.Held, Supersedes = "B" },
            new RegistryEntry { Id = "B", Tag = "Dataflow", TupleCodeClass = "x", TupleEffectDirection = "up", Status = RegistryStatus.Held, Supersedes = "A" });

        var result = r.CurrentState("A");
        // A is superseded by B; B is superseded by A — neither is a "head" by the strict
        // "not superseded by anyone" rule, so the chain is empty. Ensures no infinite loop either way.
        Assert.Empty(result.Chain);
    }

    // ========================================================================
    // Query 2: two-kill-risk
    // ========================================================================

    [Fact]
    public void TwoKillRisk_FindsDroppedMatchingTuple()
    {
        var r = FromEntries(
            Terminal("H1", RegistryStatus.Dropped, tag: "Dataflow", codeClass: "unwrap", dir: "up"),
            Terminal("H2", RegistryStatus.Promoted, tag: "Dataflow", codeClass: "unwrap", dir: "up"),
            Terminal("H3", RegistryStatus.Dropped, tag: "Pattern", codeClass: "unwrap", dir: "up"),
            Terminal("H4", RegistryStatus.Dropped, tag: "Dataflow", codeClass: "option-match", dir: "up"));

        var matches = r.TwoKillRisk("Dataflow", "unwrap", "up");
        Assert.Single(matches);
        Assert.Equal("H1", matches[0].Id);
    }

    [Fact]
    public void TwoKillRisk_CaseInsensitiveTuple()
    {
        var r = FromEntries(Terminal("H1", RegistryStatus.Dropped, tag: "Dataflow", codeClass: "unwrap", dir: "up"));
        var matches = r.TwoKillRisk("DATAFLOW", "UNWRAP", "UP");
        Assert.Single(matches);
    }

    [Fact]
    public void TwoKillRisk_NoDroppedEntries_ReturnsEmpty()
    {
        var r = FromEntries(
            Terminal("H1", RegistryStatus.Promoted),
            Terminal("H2", RegistryStatus.Held));
        Assert.Empty(r.TwoKillRisk("Dataflow", "unwrap", "up"));
    }

    // ========================================================================
    // Query 3: held-owned-by
    // ========================================================================

    [Fact]
    public void HeldOwnedBy_FiltersByStatusAndOwner_SortsByDueDate()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = "2026-06-01" },
            new RegistryEntry { Id = "H2", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = "2026-05-01" },
            new RegistryEntry { Id = "H3", Status = RegistryStatus.Held, HoldOwner = "bob", QuarterlyReviewDue = "2026-04-01" },
            new RegistryEntry { Id = "H4", Status = RegistryStatus.Promoted, HoldOwner = "alice" });

        var result = r.HeldOwnedBy("alice");
        Assert.Equal(2, result.Count);
        // Sorted by due date ascending.
        Assert.Equal("H2", result[0].Id);
        Assert.Equal("H1", result[1].Id);
    }

    [Fact]
    public void HeldOwnedBy_CaseInsensitive()
    {
        var r = FromEntries(new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = "Alice" });
        Assert.Single(r.HeldOwnedBy("ALICE"));
    }

    [Fact]
    public void HeldOwnedBy_NullDueDates_SortLast()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = null },
            new RegistryEntry { Id = "H2", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = "2026-05-01" });

        var result = r.HeldOwnedBy("alice");
        Assert.Equal(2, result.Count);
        Assert.Equal("H2", result[0].Id); // has due date → sorts first
        Assert.Equal("H1", result[1].Id); // no due date → sorts last
    }

    // ========================================================================
    // Query 4: stale-holds
    // ========================================================================

    [Fact]
    public void StaleHolds_FlagsMissingOwner()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = null, QuarterlyReviewDue = "2099-01-01" },
            new RegistryEntry { Id = "H2", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = "2099-01-01" });

        var stale = r.StaleHolds(new DateTime(2026, 4, 21));
        Assert.Single(stale);
        Assert.Equal("H1", stale[0].Entry.Id);
        Assert.Equal("no-owner", stale[0].Reason);
    }

    [Fact]
    public void StaleHolds_FlagsOverdueReview()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = "alice", QuarterlyReviewDue = "2026-01-01" },
            new RegistryEntry { Id = "H2", Status = RegistryStatus.Held, HoldOwner = "bob", QuarterlyReviewDue = "2099-01-01" });

        var stale = r.StaleHolds(new DateTime(2026, 4, 21));
        Assert.Single(stale);
        Assert.Equal("H1", stale[0].Entry.Id);
        Assert.Equal("review-overdue", stale[0].Reason);
    }

    [Fact]
    public void StaleHolds_IgnoresNonHeldEntries()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Promoted, HoldOwner = null },
            new RegistryEntry { Id = "H2", Status = RegistryStatus.Dropped, HoldOwner = null });
        Assert.Empty(r.StaleHolds(new DateTime(2026, 4, 21)));
    }

    [Fact]
    public void StaleHolds_NoOwnerWins_OverdueStillFlaggedAsNoOwner()
    {
        // If an entry has no owner AND an overdue date, we report it once, with no-owner reason.
        var r = FromEntries(
            new RegistryEntry { Id = "H1", Status = RegistryStatus.Held, HoldOwner = null, QuarterlyReviewDue = "2026-01-01" });

        var stale = r.StaleHolds(new DateTime(2026, 4, 21));
        var reason = Assert.Single(stale);
        Assert.Equal("no-owner", reason.Reason);
    }

    // ========================================================================
    // Query 5: audit-trail
    // ========================================================================

    [Fact]
    public void AuditTrail_ReturnsChainInChronologicalOrder()
    {
        var r = FromEntries(
            new RegistryEntry { Id = "H1-promoted", Status = RegistryStatus.Promoted, Supersedes = "H1-stage2", MergedAt = "2026-06-01T00:00:00Z" },
            new RegistryEntry { Id = "H1-stage2", Status = RegistryStatus.PreRegisteredStage2, Supersedes = "H1", MergedAt = "2026-05-01T00:00:00Z" },
            new RegistryEntry { Id = "H1", Status = RegistryStatus.PreRegisteredStage1, MergedAt = "2026-04-01T00:00:00Z" });

        var trail = r.AuditTrail("H1");
        Assert.Equal(3, trail.Count);
        // Oldest → newest
        Assert.Equal("H1", trail[0].Id);
        Assert.Equal("H1-stage2", trail[1].Id);
        Assert.Equal("H1-promoted", trail[2].Id);
    }

    [Fact]
    public void AuditTrail_UnknownHypothesis_ReturnsEmpty()
    {
        var r = FromEntries(Stage1("X"));
        Assert.Empty(r.AuditTrail("nonexistent"));
    }

    // ========================================================================
    // Performance — §5.0h acceptance: all queries under 200ms on 1000 entries
    // ========================================================================

    [Fact]
    public void Performance_AllQueriesUnder200msOn1000EntryRegistry()
    {
        // Build 1000 entries in a mix of terminal states.
        var rnd = new Random(42);
        var statuses = new[] { RegistryStatus.Held, RegistryStatus.Promoted, RegistryStatus.Dropped, RegistryStatus.BehindFlag };
        var entries = new List<RegistryEntry>(1000);
        for (int i = 0; i < 1000; i++)
        {
            entries.Add(new RegistryEntry
            {
                Id = $"H{i:D4}",
                Tag = i % 5 == 0 ? "Dataflow" : "Pattern",
                Hypothesis = "test",
                TupleCodeClass = i % 3 == 0 ? "unwrap" : "option-match",
                TupleEffectDirection = i % 2 == 0 ? "up" : "down",
                Status = statuses[rnd.Next(statuses.Length)],
                HoldOwner = i % 4 == 0 ? "alice" : "bob",
                QuarterlyReviewDue = DateTime.UtcNow.AddDays(rnd.Next(-200, 200)).ToString("yyyy-MM-dd"),
                MergedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 365)).ToString("O")
            });
        }
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(new RegistryDocument { Entries = entries }));
            var registry = Registry.Load(tmp);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            registry.CurrentState("H0500");
            registry.TwoKillRisk("Dataflow", "unwrap", "up");
            registry.HeldOwnedBy("alice");
            registry.StaleHolds();
            registry.AuditTrail("H0500");
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 200,
                $"All 5 queries took {sw.ElapsedMilliseconds}ms on a 1000-entry registry (budget: 200ms).");
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
