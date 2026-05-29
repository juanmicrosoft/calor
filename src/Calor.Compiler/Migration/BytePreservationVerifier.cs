using System.Text;
using Calor.Compiler.Migration;

namespace Calor.Compiler.Migration;

/// <summary>
/// Replays a <see cref="StructuralIdDropper.MigrationLog"/> against the
/// migrated files and asserts that every byte outside the recorded
/// removal ranges matches the original. This is the in-process
/// equivalent of the Python <c>byte_preservation_check.py</c> harness
/// from PR-0d.
///
/// The verifier reconstructs the original by splicing the recorded
/// <c>removed_bytes_base64</c> chunks back into the migrated file. If
/// the reconstruction matches the on-disk pre-migration backup (or
/// equals what was recorded as the original), the migration is
/// declared byte-preserving.
/// </summary>
public sealed class BytePreservationVerifier
{
    public sealed record FileResult(string File, bool Ok, string? Reason);

    /// <summary>
    /// Verify that <paramref name="migrated"/> + recorded removals
    /// reconstructs <paramref name="original"/> exactly. Returns
    /// <c>true</c> when every byte not covered by a recorded entry is
    /// identical, and the removed ranges replay byte-for-byte.
    /// </summary>
    public static bool Verify(
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> migrated,
        IEnumerable<StructuralIdDropper.LogEntry> entries,
        out string? reason)
    {
        var sorted = entries
            .OrderBy(e => e.RemovedOffset)
            .ToList();

        long expectedLen = (long)migrated.Length + sorted.Sum(e => (long)e.RemovedLength);
        if (expectedLen != original.Length)
        {
            reason = $"length mismatch: original={original.Length} expected={expectedLen}";
            return false;
        }

        int origIdx = 0;
        int migIdx = 0;
        foreach (var entry in sorted)
        {
            int preLen = entry.RemovedOffset - origIdx;
            if (preLen < 0)
            {
                reason = $"overlapping or out-of-order removal at offset {entry.RemovedOffset}";
                return false;
            }
            if (preLen > 0)
            {
                if (!original.Slice(origIdx, preLen).SequenceEqual(migrated.Slice(migIdx, preLen)))
                {
                    reason = $"byte mismatch in preserved range [{origIdx},{origIdx + preLen})";
                    return false;
                }
                origIdx += preLen;
                migIdx += preLen;
            }

            var removed = Convert.FromBase64String(entry.RemovedBytesBase64);
            if (removed.Length != entry.RemovedLength)
            {
                reason = $"removed_bytes_base64 length {removed.Length} != removed_length {entry.RemovedLength} at offset {entry.RemovedOffset}";
                return false;
            }
            if (!original.Slice(origIdx, entry.RemovedLength).SequenceEqual(removed))
            {
                reason = $"recorded removed bytes do not match original at offset {entry.RemovedOffset}";
                return false;
            }
            origIdx += entry.RemovedLength;
        }

        // Tail.
        int tail = original.Length - origIdx;
        if (tail != migrated.Length - migIdx)
        {
            reason = $"trailing length mismatch: origTail={tail} migTail={migrated.Length - migIdx}";
            return false;
        }
        if (tail > 0 && !original.Slice(origIdx, tail).SequenceEqual(migrated.Slice(migIdx, tail)))
        {
            reason = $"byte mismatch in trailing preserved range [{origIdx},{origIdx + tail})";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Verify a whole migration log against pairs of original/migrated
    /// files on disk. <paramref name="originalProvider"/> returns the
    /// pre-migration bytes for a given relative file path; tests pass
    /// in-memory snapshots and the CLI passes a <c>.bak</c>-suffixed
    /// reader.
    /// </summary>
    public static IReadOnlyList<FileResult> VerifyLog(
        StructuralIdDropper.MigrationLog log,
        string migratedRoot,
        Func<string, ReadOnlyMemory<byte>> originalProvider)
    {
        var byFile = log.Entries
            .GroupBy(e => e.File)
            .ToList();

        var results = new List<FileResult>();
        foreach (var group in byFile)
        {
            var rel = group.Key;
            var migPath = Path.Combine(migratedRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(migPath))
            {
                results.Add(new FileResult(rel, false, $"migrated file not found: {migPath}"));
                continue;
            }
            ReadOnlyMemory<byte> original;
            try
            {
                original = originalProvider(rel);
            }
            catch (Exception ex)
            {
                results.Add(new FileResult(rel, false, $"original provider failed: {ex.Message}"));
                continue;
            }
            var migrated = File.ReadAllBytes(migPath);
            var ok = Verify(original.Span, migrated, group, out var reason);
            results.Add(new FileResult(rel, ok, reason));
        }
        return results;
    }
}
