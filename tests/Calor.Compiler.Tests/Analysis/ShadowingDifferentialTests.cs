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
        var result = Program.Compile(
            source,
            "diff.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true });
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
            "module S S block\n" +
            "  func Fact (int:n) → int\n" +
            "    !\n" +
            "    var result = 1\n" +
            "    var i = 2\n" +
            "    while (i <= n)\n" +
            "      var result = (result * i)\n" +
            "      var i = (i + 1)\n" +
            "    return result\n" },
        // Sibling (non-nested) blocks each declaring an immutable local of the same name.
        new object[] { "sibling-immutable",
            "module S S block\n" +
            "  func Do (bool:a, bool:b) → int\n" +
            "    !\n" +
            "    if (a == true)\n" +
            "      let int:x = 1\n" +
            "      return x\n" +
            "    if (b == true)\n" +
            "      let int:x = 2\n" +
            "      return x\n" +
            "    return 0\n" },
        // A local legally shadowing a field.
        new object[] { "local-shadows-field",
            "module S S block\n" +
            "  class Box\n" +
            "    field int:x private\n" +
            "    func Do () → int\n" +
            "      !\n" +
            "      let int:x = 5\n" +
            "      return x\n" },
        // A loop variable used normally (no reuse).
        new object[] { "loop-var-ok",
            "module S S block\n" +
            "  func Sum (int:n) → int\n" +
            "    !\n" +
            "    var acc = 0\n" +
            "    for i from 0 to n step 1\n" +
            "      var acc = (acc + i)\n" +
            "    return acc\n" },
        // Mutable §B reusing a name across sibling blocks — each re-declares (#732).
        new object[] { "sibling-mutable-rebind",
            "module S S block\n" +
            "  func Do (bool:a, bool:b) → int\n" +
            "    !\n" +
            "    if (a == true)\n" +
            "      var int:x = 1\n" +
            "      return x\n" +
            "    if (b == true)\n" +
            "      var int:x = 2\n" +
            "      return x\n" +
            "    return 0\n" },
        // Mutable §B rebinding a parameter — parameters are reassignable, so `x = 9;`.
        new object[] { "parameter-rebind",
            "module S S block\n  func Do (int:x) → int\n    !\n    var int:x = 9\n    return x\n" },
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
            "module S S block\n" +
            "  func Do (bool:flag) → int\n" +
            "    !\n" +
            "    var int:x = 0\n" +
            "    if (flag == true)\n" +
            "      let string:x = \"hi\"\n" +
            "      return 0\n" +
            "    return x\n" },
        new object[] { "loop-var-shadow",
            "module S S block\n  func Do () → int\n    !\n    for i from 0 to 3 step 1\n      let int:i = 9\n      return i\n    return 0\n" },
        new object[] { "param-reuse",
            "module S S block\n  func Do (int:x) → int\n    !\n    if (x > 0)\n      let int:x = 9\n      return x\n    return x\n" },
        new object[] { "array-to-list",
            "module S S block\n" +
            "  func Do (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let List<string>:lines = File.ReadAllLines(path)\n" +
            "    return lines.Length\n" },
        // Type-changing mutable rebind (#733): rejected with Calor0256 instead of
        // emitting `int x = 0; x = "hi";` (CS0029).
        new object[] { "type-changing-mutable-rebind",
            "module S S block\n  func Do () → int\n    !\n    var int:x = 0\n    var string:x = \"hi\"\n    return 0\n" },
        // Rebinding a §EACH iteration variable (#738): read-only in C#, so rejected
        // with Calor0257 instead of emitting `x = "y";` inside the foreach (CS1656).
        new object[] { "foreach-var-rebind",
            "module S S block\n" +
            "  func Do (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let string[]:arr = File.ReadAllLines(path)\n" +
            "    each x in arr\n" +
            "      var string:x = \"y\"\n" +
            "    return 0\n" },
        // Same defect via §ASSIGN rather than §B (#743 review): a write to a §EACH
        // iteration variable is CS1656 whichever assignment form spells it (Calor0257).
        new object[] { "foreach-var-assign",
            "module S S block\n" +
            "  func Do (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let string[]:arr = File.ReadAllLines(path)\n" +
            "    each x in arr\n" +
            "      set x \"y\"\n" +
            "    return 0\n" },
        // Construct-variable-shaped row (#743 review finding 4): the LOOP variable is
        // the shadower — `int x = 0;` then `for (var x = …)` is CS0136 (Calor0255).
        new object[] { "loop-var-shadows-enclosing",
            "module S S block\n" +
            "  func Do () → int\n" +
            "    !\n" +
            "    var x = 0\n" +
            "    for x from 0 to 3 step 1\n" +
            "      set x (x + 1)\n" +
            "    return x\n" },
        // Unannotated non-literal rebind of a mismatched value (#740): value-type
        // inference resolves the string-returning call, so `x = File.ReadAllText(p)`
        // (string → int, CS0029) is rejected with Calor0256 instead of exiting 0.
        new object[] { "unannotated-nonliteral-rebind",
            "module S S block\n" +
            "  func Do (string:p) → int\n" +
            "    ! fs:r\n" +
            "    var int:x = 0\n" +
            "    var x = File.ReadAllText(p)\n" +
            "    return 0\n" },
        // Same-scope duplicate §B (#731): `int x = 1; int x = 2;` is CS0128, rejected with
        // Calor0258 instead of exiting 0. (Safe now the converter emits reassignments as
        // §ASSIGN, not a fresh same-name §B.)
        new object[] { "same-scope-duplicate",
            "module S S block\n  func Do () → int\n    !\n    let int:x = 1\n    let int:x = 2\n    return x\n" },
        // Mutable §B rebinding a §L for-loop variable (#1550): a §L loop variable is
        // read-only in Calor (only the loop advances it), so `i = 9;` is Calor0213. Before
        // 0.27 this was accepted and silently changed the iteration.
        new object[] { "for-loop-var-rebind",
            "module S S block\n" +
            "  func Do () → int\n" +
            "    !\n" +
            "    var s = 0\n" +
            "    for i from 0 to 3 step 1\n" +
            "      var int:i = 9\n" +
            "      set s (s + i)\n" +
            "    return s\n" },
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

    // #740 (unannotated non-literal rebind → CS0029) is FIXED: value-type inference now
    // resolves references and known call return types, so calor -i rejects it with
    // Calor0256; it moved to RejectedIdioms above ("unannotated-nonliteral-rebind").

    // #738 (§EACH iteration-variable rebind → CS1656) is FIXED: calor -i now rejects it
    // with Calor0257, so it moved to RejectedIdioms above ("foreach-var-rebind").

    // #733 (type-changing mutable rebind → CS0029) is FIXED: calor -i now rejects it
    // with Calor0256, so it moved to RejectedIdioms above ("type-changing-mutable-rebind").

    // #731 (same-scope duplicate §B → CS0128) is FIXED: calor -i now rejects it with
    // Calor0258, so it moved to RejectedIdioms above ("same-scope-duplicate"). This became
    // safe once the C#→Calor converter stopped emitting `arr = new T[]{…}` reassignments as
    // a fresh same-name §B creation block (it now emits §ASSIGN via a temp).

    [Fact]
    public void KnownGap_DecimalToFloatRebind_EmitsCS0266() // #740 within-Numeric miss
    {
        // decimal and float/double both classify as the single Numeric category, so a
        // rebind across them is not flagged (the conservative-miss trade the Calor0256
        // category model makes to avoid widening false positives). There is no *implicit*
        // conversion between decimal and double, but an *explicit* one exists, so
        // `double x = 0; x = d;` (d: decimal) is CS0266 ("a cast is missing"), NOT CS0029
        // — this miss is the same cast-needed bucket as numeric narrowing. Pins the gap
        // the AreDefinitelyIncompatible doc names.
        var (accepted, roslynErrors) = Compile(
            "module S S block\n" +
            "  func Do (decimal:d) → int\n" +
            "    !\n" +
            "    var double:x = 0.0\n" +
            "    var x = d\n" +
            "    return 0\n");

        Assert.True(accepted);
        Assert.Contains(roslynErrors, e => e.StartsWith("CS0266", StringComparison.Ordinal));
    }
}
