using System.Text;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="StructuralIdDropper"/> and
/// <see cref="BytePreservationVerifier"/> (PR-1c, PR-1d).
///
/// These tests guard the invariants the v6 RFC §5 relies on:
///   T-5.7-a  drop on §M{id:Name}
///   T-5.7-b  drop on §F{id:name:vis} with extra positionals
///   T-5.7-c  drop on closing §/M{id}, §/F{id}
///   T-5.7-d  no-op when first positional is not ID-shaped
///   T-5.7-e  round-trip (drop → verify → revert) is byte-equal
/// </summary>
public class StructuralIdDropperTests
{
    private static readonly StructuralIdDropper Sut = new();
    private static readonly UTF8Encoding Utf8 = new(false);

    [Fact]
    public void T_5_7_a_DropsModuleId()
    {
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}\n";
        var (out_, removals) = Sut.Process(src, "a.calr");

        Assert.Equal("§M{Calc}\n", out_);
        Assert.Single(removals);
        Assert.All(removals, r => Assert.Equal("a.calr", r.File));
    }

    [Fact]
    public void T_5_7_b_DropsFunctionIdKeepsNameAndVis()
    {
        const string src = "§F{f_01j5x7abcdef01j5x7abcdef01:divide:i32:public}";
        var (out_, removals) = Sut.Process(src, "f.calr");

        Assert.Equal("§F{divide:i32:public}", out_);
        Assert.Single(removals);
    }

    [Fact]
    public void T_5_7_c_DropsClosingTagWholeBlock()
    {
        // Closing tag carries only an ID; the whole {…} should disappear.
        const string src = "§/F{f_01j5x7abcdef01j5x7abcdef01}";
        var (out_, removals) = Sut.Process(src, "c.calr");

        Assert.Equal("§/F", out_);
        Assert.Single(removals);
    }

    [Fact]
    public void T_5_7_d_LeavesNonIdFirstPositionalUntouched()
    {
        // First positional `Calc` is a bare name — already compact form.
        const string src = "§M{Calc}\n§/M";
        var (out_, removals) = Sut.Process(src, "n.calr");

        Assert.Equal(src, out_);
        Assert.Empty(removals);
    }

    [Fact]
    public void T_5_7_e_RoundTripIsByteEqual()
    {
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}\n§F{f_01j5x7abcdef01j5x7abcdef01:add:i32:public}\n\n\n";
        var (migrated, removals) = Sut.Process(src, "rt.calr");

        var originalBytes = Utf8.GetBytes(src);
        var migratedBytes = Utf8.GetBytes(migrated);

        var ok = BytePreservationVerifier.Verify(
            originalBytes, migratedBytes, removals, out var reason);
        Assert.True(ok, reason);
    }

    [Fact]
    public void DoesNotTouchExpressionBraces()
    {
        // Braces that aren't part of a structural opener must be left alone.
        const string src = "§P {not_an_attr_block}";
        var (out_, removals) = Sut.Process(src, "x.calr");

        Assert.Equal(src, out_);
        Assert.Empty(removals);
    }

    [Fact]
    public void CompactIdIsAlsoRecognised()
    {
        // 12-char Crockford lowercase compact ID.
        const string src = "§M{m_0123456789ab:Calc}";
        var (out_, removals) = Sut.Process(src, "compact.calr");

        Assert.Equal("§M{Calc}", out_);
        Assert.Single(removals);
    }

    [Fact]
    public void HandlesMultipleSectionsOnSameLine()
    {
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:X} §F{f_01j5x7abcdef01j5x7abcdef01:y:i32:public}";
        var (out_, removals) = Sut.Process(src, "m.calr");

        Assert.Equal("§M{X} §F{y:i32:public}", out_);
        Assert.Equal(2, removals.Count);
    }

    [Fact]
    public void RevertingViaReinsertRestoresExactBytes()
    {
        // We need to exercise the full drop → reinsert pipeline. The
        // reinsert logic lives in FixCommand.ReinsertRemovals so we
        // re-implement the inverse here using the recorded bytes.
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}\n";
        var (migrated, removals) = Sut.Process(src, "r.calr");

        var migratedBytes = Utf8.GetBytes(migrated);
        var rebuilt = Reinsert(migratedBytes, removals);

        Assert.Equal(Utf8.GetBytes(src), rebuilt);
    }

    [Fact]
    public void LogSerialisesToSnakeCase()
    {
        var log = new StructuralIdDropper.MigrationLog();
        log.Entries.Add(new StructuralIdDropper.LogEntry
        {
            File = "a.calr",
            RemovedOffset = 5,
            RemovedLength = 7,
            RemovedBytesBase64 = "QUJDREVGRw==",
        });
        var json = StructuralIdDropper.SerializeLog(log);

        Assert.Contains("\"entries\"", json);
        Assert.Contains("\"file\"", json);
        Assert.Contains("\"removed_offset\"", json);
        Assert.Contains("\"removed_length\"", json);
        Assert.Contains("\"removed_bytes_base64\"", json);

        // Round-trip
        var parsed = StructuralIdDropper.DeserializeLog(json);
        Assert.Single(parsed.Entries);
        Assert.Equal("a.calr", parsed.Entries[0].File);
        Assert.Equal(5, parsed.Entries[0].RemovedOffset);
        Assert.Equal(7, parsed.Entries[0].RemovedLength);
    }

    [Fact]
    public void VerifierDetectsCorruption()
    {
        const string src = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}";
        var (migrated, removals) = Sut.Process(src, "v.calr");

        var originalBytes = Utf8.GetBytes(src);
        var corruptedMigrated = Utf8.GetBytes(migrated.Replace("Calc", "CalX"));

        var ok = BytePreservationVerifier.Verify(
            originalBytes, corruptedMigrated, removals, out var reason);
        Assert.False(ok);
        Assert.NotNull(reason);
    }

    private static byte[] Reinsert(
        ReadOnlySpan<byte> migrated,
        IEnumerable<StructuralIdDropper.LogEntry> entries)
    {
        var sorted = entries.OrderBy(e => e.RemovedOffset).ToList();
        int totalLen = migrated.Length + sorted.Sum(e => e.RemovedLength);
        var result = new byte[totalLen];

        int srcIdx = 0;
        int dstIdx = 0;
        foreach (var entry in sorted)
        {
            int preLen = entry.RemovedOffset - dstIdx;
            if (preLen > 0)
            {
                migrated.Slice(srcIdx, preLen).CopyTo(result.AsSpan(dstIdx, preLen));
                srcIdx += preLen;
                dstIdx += preLen;
            }
            var removed = Convert.FromBase64String(entry.RemovedBytesBase64);
            removed.CopyTo(result.AsSpan(dstIdx));
            dstIdx += removed.Length;
        }
        int tail = migrated.Length - srcIdx;
        if (tail > 0)
        {
            migrated.Slice(srcIdx, tail).CopyTo(result.AsSpan(dstIdx, tail));
        }
        return result;
    }
}
