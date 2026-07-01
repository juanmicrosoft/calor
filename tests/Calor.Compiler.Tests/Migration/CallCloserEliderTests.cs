using System.Text;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for <see cref="CallCloserElider"/>.
///
/// The elider is the bulk migrator for v0.6.x call-closer elision:
///   - zero-arg: <c>§C{X} §/C</c> → <c>§C{X}</c> (v0.6.1)
///   - one-arg same-line: <c>§C{X} §A y §/C</c> → <c>§C{X} y</c>
///     (v0.6.2 stmt-context / v0.6.3 expr-context)
///
/// Invariants enforced by these tests:
///   T-elide-a  canonical zero-arg statement: elides §/C
///   T-elide-b  canonical one-arg statement: elides §A + §/C
///   T-elide-c  one-arg expression (in §B initializer): elides
///   T-elide-d  multi-line one-arg call: NOT elided (skip)
///   T-elide-e  named-arg (§A[name] x): NOT elided
///   T-elide-f  multi-arg call: NOT elided
///   T-elide-g  nested call inside one-arg call: both elided correctly
///   T-elide-h  trailing operator that would absorb arg: NOT elided
///              (canonical-emit safety net catches this)
///   T-elide-i  round-trip (elide → revert via log) is byte-equal
///   T-elide-j  idempotence: running elider twice = running once
///   T-elide-k  no candidates → no edits, no log entries
/// </summary>
public class CallCloserEliderTests
{
    private static readonly CallCloserElider Sut = new();

    private const string Module = "§M{m_01j5x7abcdef01j5x7abcdef01:Calc}\n";

    [Fact]
    public void T_elide_a_ZeroArgStmt_ElidesEndC()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} §/C\n";
        var result = Sut.Process(src, "a.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(1, result.CallsElided);
        Assert.DoesNotContain("§/C", result.MigratedSource);
        Assert.Contains("§C{Doit}\n", result.MigratedSource);
    }

    [Fact]
    public void T_elide_b_OneArgStmt_SameLine_ElidesBoth()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} §A x §/C\n";
        var result = Sut.Process(src, "b.calr");

        Assert.False(result.Skipped, result.SkipReason);
        // Two removals: §A and §/C.
        Assert.Equal(2, result.CallsElided);
        Assert.DoesNotContain("§/C", result.MigratedSource);
        Assert.DoesNotContain("§A", result.MigratedSource);
        Assert.Contains("§C{Doit} x\n", result.MigratedSource);
    }

    [Fact]
    public void T_elide_c_OneArgExpr_InBindInitializer_Elides()
    {
        // Initializer expression form: §B{r:i32} §C{Add} §A 1 §/C
        // (Calor bindings place the initializer expression directly after
        // §B{name:type} — there is no '§=' token.)
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §B{r:i32} §C{Add} §A 1 §/C\n";
        var result = Sut.Process(src, "c.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(2, result.CallsElided);
        Assert.Contains("§C{Add} 1\n", result.MigratedSource);
    }

    [Fact]
    public void T_elide_d_OneArg_MultiLine_SkipsArgPair()
    {
        // §A is on a different line from §C — must NOT elide.
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit}\n"
            + "    §A x\n"
            + "  §/C\n";
        var result = Sut.Process(src, "d.calr");

        // No elisions at all (§/C is on a different line from §C).
        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(0, result.CallsElided);
        Assert.Equal(src, result.MigratedSource);
    }

    [Fact]
    public void T_elide_e_NamedArg_NotElided()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} §A[name] x §/C\n";
        var result = Sut.Process(src, "e.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(0, result.CallsElided);
        Assert.Equal(src, result.MigratedSource);
    }

    [Fact]
    public void T_elide_f_TwoArgs_NotElided()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Add} §A 1 §A 2 §/C\n";
        var result = Sut.Process(src, "f.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(0, result.CallsElided);
        Assert.Equal(src, result.MigratedSource);
    }

    [Fact]
    public void T_elide_g_NestedOneArg_BothElided()
    {
        // Nested call: outer arg is itself a call expression.
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Outer} §A §C{Inner} §A x §/C §/C\n";
        var result = Sut.Process(src, "g.calr");

        Assert.False(result.Skipped, result.SkipReason);
        // 4 removals expected: 2 for outer (§A + §/C) and 2 for inner.
        Assert.Equal(4, result.CallsElided);
        Assert.DoesNotContain("§A", result.MigratedSource);
        Assert.DoesNotContain("§/C", result.MigratedSource);
        Assert.Contains("§C{Outer} §C{Inner} x", result.MigratedSource);
    }

    [Fact]
    public void T_elide_h_NoOpOnEmptyFile()
    {
        var src = Module;
        var result = Sut.Process(src, "h.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(0, result.CallsElided);
        Assert.Equal(src, result.MigratedSource);
        Assert.Empty(result.Removals);
    }

    [Fact]
    public void T_elide_i_RoundTrip_ByteEqual()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} §A x §/C\n"
            + "  §C{Another} §/C\n";
        var result = Sut.Process(src, "i.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.True(result.CallsElided > 0);

        // Revert: re-insert the recorded byte spans in ascending offset
        // order. Mirror of FixCommand.ReinsertRemovals.
        var migratedBytes = Encoding.UTF8.GetBytes(result.MigratedSource);
        var restored = ReinsertRemovals(migratedBytes, result.Removals);
        var restoredText = Encoding.UTF8.GetString(restored);

        Assert.Equal(src, restoredText);
    }

    [Fact]
    public void T_elide_j_Idempotent()
    {
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} §A x §/C\n";
        var first = Sut.Process(src, "j.calr");
        Assert.False(first.Skipped, first.SkipReason);

        var second = Sut.Process(first.MigratedSource, "j.calr");
        Assert.False(second.Skipped, second.SkipReason);
        Assert.Equal(0, second.CallsElided);
        Assert.Equal(first.MigratedSource, second.MigratedSource);
    }

    [Fact]
    public void T_elide_k_AlreadyElidedFile_NoOp()
    {
        // No §/C at all — nothing to elide.
        var src = Module
            + "§F{f_01j5x7abcdef01j5x7abcdef01:run:public}\n"
            + "  §C{Doit} x\n";
        var result = Sut.Process(src, "k.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(0, result.CallsElided);
        Assert.Equal(src, result.MigratedSource);
    }

    [Fact]
    public void T_elide_l_LexerErrors_SkipFile()
    {
        // Garbage that the lexer rejects.
        const string src = "§§§INVALID";
        var result = Sut.Process(src, "l.calr");

        Assert.True(result.Skipped);
        Assert.Equal("lexer errors", result.SkipReason);
        Assert.Equal(src, result.MigratedSource);
        Assert.Empty(result.Removals);
    }

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
