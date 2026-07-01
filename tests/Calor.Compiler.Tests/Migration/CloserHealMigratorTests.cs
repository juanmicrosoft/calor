using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="CloserHealMigrator"/> and the lexer-backed
/// <see cref="LegacyCloserFormLint.ScanForHeal"/> it relies on (v0.6.8, the CLI
/// <c>calor fix --heal-closers</c> backend).
///
/// Invariants:
///   T-heal-a  bare structural closers (§/F, §/M) are removed
///   T-heal-b  id-bearing closers (§/F{f1}, §/M{m1}) removed incl. payload
///   T-heal-c  non-structural inline closer (§/C) is NOT removed
///   T-heal-d  a §/F inside a STRING literal is NOT removed (safety)
///   T-heal-e  a §/F inside a // COMMENT is NOT removed (safety)
///   T-heal-f  mixed genuine + string-embedded closers: heals only genuine
///   T-heal-g  round-trip (heal → revert via byte log) is byte-equal
///   T-heal-h  round-trip is byte-equal with Unicode-before + CRLF
///   T-heal-i  no closers → no edits, no log entries
///   T-heal-j  healed source re-parses with no Calor0830 and no errors
///   T-heal-k  idempotent: healing twice = healing once
/// </summary>
public class CloserHealMigratorTests
{
    private static readonly CloserHealMigrator Sut = new();

    private const string CloserModule =
        "§M{m1:Calc}\n" +
        "  §F{f1:Add:pub}\n" +
        "    §I{i32:a}\n" +
        "    §I{i32:b}\n" +
        "    §O{i32}\n" +
        "    §R (+ a b)\n" +
        "  §/F{f1}\n" +
        "§/M{m1}\n";

    [Fact]
    public void T_heal_a_RemovesBareStructuralClosers()
    {
        const string src = "§M{Calc}\n  §F{add}\n    §R INT:0\n  §/F\n§/M\n";
        var (migrated, removals) = Sut.Process(src, "a.calr");

        Assert.Equal(2, removals.Count);
        Assert.DoesNotContain("§/F", migrated);
        Assert.DoesNotContain("§/M", migrated);
        Assert.Contains("§F{add}", migrated);
    }

    [Fact]
    public void T_heal_b_RemovesIdBearingClosersWithPayload()
    {
        var (migrated, removals) = Sut.Process(CloserModule, "b.calr");

        Assert.Equal(2, removals.Count);
        Assert.DoesNotContain("§/F", migrated);
        Assert.DoesNotContain("§/M", migrated);
        // Payload braces went with the closer.
        Assert.DoesNotContain("{f1}\n", migrated.Replace("§F{f1", ""));
    }

    [Fact]
    public void T_heal_c_LeavesNonStructuralInlineCloser()
    {
        // §/C (call closer) is inline, not structural — must stay.
        const string src = "§M{Calc}\n  §F{run}\n    §C{Math.Abs} §A INT:-5 §/C\n  §/F\n";
        var (migrated, removals) = Sut.Process(src, "c.calr");

        Assert.Single(removals); // only §/F
        Assert.Contains("§/C", migrated);
        Assert.DoesNotContain("§/F", migrated);
    }

    [Fact]
    public void T_heal_d_DoesNotTouchCloserInsideStringLiteral()
    {
        const string src = "§M{Calc}\n  §F{run:pub}\n    §P \"see §/F in docs\"\n";
        // Raw scan (unsafe) WOULD flag the embedded §/F...
        Assert.NotEmpty(LegacyCloserFormLint.Scan(src, "d.calr"));
        // ...but the safe heal scan must not, so nothing is removed.
        var (migrated, removals) = Sut.Process(src, "d.calr");
        Assert.Empty(removals);
        Assert.Equal(src, migrated);
    }

    [Fact]
    public void T_heal_e_DoesNotTouchCloserInsideLineComment()
    {
        const string src = "§M{Calc}\n  §F{run:pub}\n    §R INT:0 // note about §/F here\n";
        Assert.NotEmpty(LegacyCloserFormLint.Scan(src, "e.calr"));
        var (migrated, removals) = Sut.Process(src, "e.calr");
        Assert.Empty(removals);
        Assert.Equal(src, migrated);
    }

    [Fact]
    public void T_heal_f_MixedStringAndGenuine_HealsOnlyGenuine()
    {
        const string src =
            "§M{m1:Calc}\n" +
            "  §F{f1:run:pub}\n" +
            "    §P \"literal §/F stays\"\n" +
            "    §R INT:0\n" +
            "  §/F{f1}\n" +
            "§/M{m1}\n";

        // Raw scan sees 3 (string §/F + two genuine); safe scan sees 2.
        Assert.Equal(3, LegacyCloserFormLint.Scan(src, "f.calr").Count);
        Assert.Equal(2, LegacyCloserFormLint.ScanForHeal(src, "f.calr").Count);

        var (migrated, removals) = Sut.Process(src, "f.calr");
        Assert.Equal(2, removals.Count);
        Assert.Contains("literal §/F stays", migrated);
    }

    [Fact]
    public void T_heal_g_RoundTrip_ByteEqual()
    {
        var (migrated, removals) = Sut.Process(CloserModule, "g.calr");
        Assert.NotEmpty(removals);

        var restored = Encoding.UTF8.GetString(
            ReinsertRemovals(Encoding.UTF8.GetBytes(migrated), removals));
        Assert.Equal(CloserModule, restored);
    }

    [Fact]
    public void T_heal_h_RoundTrip_ByteEqual_UnicodeAndCrlf()
    {
        // Unicode before the closer (é, 世) exercises the char→byte offset
        // conversion; CRLF line endings exercise \r handling.
        var src =
            "§M{m1:Café}\r\n" +
            "  §F{f1:世界:pub}\r\n" +
            "    §R INT:0\r\n" +
            "  §/F{f1}\r\n" +
            "§/M{m1}\r\n";
        var (migrated, removals) = Sut.Process(src, "h.calr");
        Assert.Equal(2, removals.Count);

        var restored = Encoding.UTF8.GetString(
            ReinsertRemovals(Encoding.UTF8.GetBytes(migrated), removals));
        Assert.Equal(src, restored);
    }

    [Fact]
    public void T_heal_i_NoClosers_NoOp()
    {
        const string src = "§M{Calc}\n  §F{run:pub}\n    §R INT:0\n";
        var (migrated, removals) = Sut.Process(src, "i.calr");
        Assert.Empty(removals);
        Assert.Equal(src, migrated);
    }

    [Fact]
    public void T_heal_j_HealedSourceReparsesCleanly()
    {
        var (migrated, _) = Sut.Process(CloserModule, "j.calr");
        var (diags, hasErrors) = Parse(migrated);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
        Assert.False(hasErrors, "Healed source must parse without errors");
    }

    [Fact]
    public void T_heal_k_Idempotent()
    {
        var (first, _) = Sut.Process(CloserModule, "k.calr");
        var (second, removals2) = Sut.Process(first, "k.calr");

        Assert.Empty(removals2);
        Assert.Equal(first, second);
    }

    // ---- helpers ----------------------------------------------------------

    private static (IList<Diagnostic> Diagnostics, bool HasErrors) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        _ = parser.Parse();
        return (diagnostics.ToList(), diagnostics.HasErrors);
    }

    // Mirror of FixCommand.ReinsertRemovals (byte-based revert).
    private static byte[] ReinsertRemovals(
        byte[] migrated,
        IEnumerable<StructuralIdDropper.LogEntry> entries)
    {
        var sorted = entries.OrderBy(e => e.RemovedOffset).ToList();
        var totalLen = migrated.Length + sorted.Sum(e => e.RemovedLength);
        var result = new byte[totalLen];

        var srcIdx = 0;
        var dstIdx = 0;
        foreach (var entry in sorted)
        {
            var preLen = entry.RemovedOffset - dstIdx;
            if (preLen > 0)
            {
                Array.Copy(migrated, srcIdx, result, dstIdx, preLen);
                srcIdx += preLen;
                dstIdx += preLen;
            }
            var removed = Convert.FromBase64String(entry.RemovedBytesBase64);
            Array.Copy(removed, 0, result, dstIdx, removed.Length);
            dstIdx += removed.Length;
        }
        var tail = migrated.Length - srcIdx;
        if (tail > 0)
        {
            Array.Copy(migrated, srcIdx, result, dstIdx, tail);
        }
        return result;
    }
}
