using Calor.Compiler;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Differential guard for the shadowing / rebinding family (PR #730 review
/// finding 4): the real specification is "the emitted C# compiles", not "the pass
/// agrees with the emitter". For every idiom, this asserts the load-bearing
/// invariant — <b>if <c>calor -i</c> accepts a program, its generated C# compiles
/// under Roslyn</b> (no exit-0-then-broken-build). Idioms where that invariant is
/// currently violated are pinned as explicit known gaps referencing their issues,
/// so the map of what is and isn't covered is executable, and any NEW hole (an
/// accepted program that Roslyn rejects) fails a test instead of shipping.
/// </summary>
public class ShadowingDifferentialTests
{
    private static (bool accepted, IReadOnlyList<string> roslynErrors) Compile(string source)
    {
        var result = Program.Compile(source, "diff.calr");
        if (result.Diagnostics.HasErrors)
        {
            return (false, Array.Empty<string>());
        }

        return (true, ExemplarCompileChecker.RoslynErrors(result.GeneratedCode));
    }

    // Idioms calor -i ACCEPTS that must emit Roslyn-clean C#.
    public static IEnumerable<object[]> CleanWhenAccepted() => new[]
    {
        // Mutable-rebind accumulator (the reason #727 mirrors the emitter).
        new object[] { "accumulator",
            "§M{m:S}\n  §F{f:Fact:pub} (i32:n) -> i32\n    §B{~result} 1\n    §B{~i} 2\n" +
            "    §WH{w1} (<= i n)\n      §B{~result} (* result i)\n      §B{~i} (+ i 1)\n    §R result\n" },
        // Sibling (non-nested) blocks each declaring an immutable local of the same name.
        new object[] { "sibling-immutable",
            "§M{m:S}\n  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n      §B{x:i32} 1\n      §R x\n" +
            "    §IF{i2} (== b true)\n      §B{x:i32} 2\n      §R x\n    §R 0\n" },
        // A local legally shadowing a field.
        new object[] { "local-shadows-field",
            "§M{m:S}\n  §CL{c1:Box:pub}\n    §FLD{i32:x:priv}\n" +
            "    §MT{mt1:Do:pub} () -> i32\n      §B{x:i32} 5\n      §R x\n" },
        // A loop variable used normally (no reuse).
        new object[] { "loop-var-ok",
            "§M{m:S}\n  §F{f:Sum:pub} (i32:n) -> i32\n    §B{~acc} 0\n" +
            "    §L{l1:i:0:n:1}\n      §B{~acc} (+ acc i)\n    §R acc\n" },
        // Mutable §B reusing a name across sibling blocks — each re-declares (#732).
        new object[] { "sibling-mutable-rebind",
            "§M{m:S}\n  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n      §B{~x:i32} 1\n      §R x\n" +
            "    §IF{i2} (== b true)\n      §B{~x:i32} 2\n      §R x\n    §R 0\n" },
        // Mutable §B rebinding a §L for-loop variable — the loop var IS reassignable,
        // so both pass and emitter treat it as `i = 9;` (valid), not a re-declaration (#732).
        new object[] { "for-loop-var-rebind",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~s} 0\n" +
            "    §L{l1:i:0:3:1}\n      §B{~i:i32} 9\n      §ASSIGN s (+ s i)\n    §R s\n" },
        // Mutable §B rebinding a parameter — parameters are reassignable, so `x = 9;`.
        new object[] { "parameter-rebind",
            "§M{m:S}\n  §F{f:Do:pub} (i32:x) -> i32\n    §B{~x:i32} 9\n    §R x\n" },
    };

    [Theory]
    [MemberData(nameof(CleanWhenAccepted))]
    public void AcceptedIdiom_EmitsRoslynCleanCSharp(string name, string source)
    {
        var (accepted, roslynErrors) = Compile(source);
        Assert.True(accepted, $"[{name}] expected calor -i to accept this program");
        Assert.True(
            roslynErrors.Count == 0,
            $"[{name}] accepted by calor -i but the emitted C# fails Roslyn:\n" +
            string.Join("\n", roslynErrors));
    }

    // Idioms calor -i correctly REJECTS (true positives — Calor025x).
    public static IEnumerable<object[]> RejectedIdioms() => new[]
    {
        new object[] { "immutable-inner-shadow",
            "§M{m:S}\n  §F{f:Do:pub} (bool:flag) -> i32\n    §B{~x:i32} 0\n" +
            "    §IF{i1} (== flag true)\n      §B{x:str} \"hi\"\n      §R 0\n    §R x\n" },
        new object[] { "loop-var-shadow",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §L{l1:i:0:3:1}\n      §B{i:i32} 9\n      §R i\n    §R 0\n" },
        new object[] { "param-reuse",
            "§M{m:S}\n  §F{f:Do:pub} (i32:x) -> i32\n    §IF{i1} (> x 0)\n      §B{x:i32} 9\n      §R x\n    §R x\n" },
        new object[] { "array-to-list",
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r}\n" +
            "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n    §R (len lines)\n" },
    };

    [Theory]
    [MemberData(nameof(RejectedIdioms))]
    public void BrokenIdiom_IsRejectedByCalor(string name, string source)
    {
        var (accepted, _) = Compile(source);
        Assert.False(accepted, $"[{name}] expected calor -i to reject this program");
    }

    // ── Known gaps: calor -i accepts these but Roslyn rejects them. Each is tracked
    // by an issue; the assertion pins the CURRENT behavior so the gap is documented
    // and its eventual fix (accepted→clean, or rejected) trips this test to be flipped.

    // #732 (sibling mutable rebind → CS0103) is FIXED: the emitter is now scope-aware,
    // so that case moved to CleanWhenAccepted above ("sibling-mutable-rebind").

    [Fact]
    public void KnownGap_ForeachVariableRebind_EmitsCS1656() // #738
    {
        // A §EACH iteration variable is not assignable in C#; the pass and emitter now
        // agree it is in scope (so it's a reassignment), but the reassignment itself is
        // invalid. The correct fix is a reject diagnostic (#738).
        var (accepted, roslynErrors) = Compile(
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r}\n" +
            "    §B{arr:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §EACH{e1:x} arr\n      §B{~x:str} \"y\"\n    §R 0\n");

        Assert.True(accepted);
        Assert.Contains(roslynErrors, e => e.StartsWith("CS1656", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownGap_TypeChangingMutableRebind_EmitsCS0029() // #733
    {
        var (accepted, roslynErrors) = Compile(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:str} \"hi\"\n    §R 0\n");

        Assert.True(accepted);
        Assert.Contains(roslynErrors, e => e.StartsWith("CS0029", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownGap_SameScopeDuplicate_EmitsCS0128() // #731
    {
        var (accepted, roslynErrors) = Compile(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{x:i32} 1\n    §B{x:i32} 2\n    §R x\n");

        Assert.True(accepted);
        Assert.Contains(roslynErrors, e => e.StartsWith("CS0128", StringComparison.Ordinal));
    }
}
