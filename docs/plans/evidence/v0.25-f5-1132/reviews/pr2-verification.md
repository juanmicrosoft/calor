# PR 2 (narrowed) verification-only pass, Codex (#1132 F5)

- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
- **Budget:** invocation 4 of 4, the verification-only pass. **PR 2's review budget is now spent.**
- **Reviewed:** HEAD `e9901bba2`; round-3 fixes `6002c19da` against `reviews/pr2-round-3.md`.
- **Verdict:** REQUEST CHANGES.

| Round-3 item | This pass |
|---|---|
| 1 BLOCKING (contested, not changed): `var xs = new[] { t }` is now a `Task[]` receiver | ACCEPTED (corrected receiver type; same result as a `Task[]` on main) |
| 2 MAJOR: declared array types (`object[]?`, `object[][,]`) | VERIFIED (revert-sensitive) |
| 3 MAJOR: constant earlier argument stays in place | VERIFIED (revert-sensitive), but see the new finding |
| 4 MAJOR: `Array.Empty<int[]>()` | VERIFIED (revert-sensitive) |

## Open finding (not fixed: the budget is spent)

**MAJOR, introduced by the round-3 fix for finding 3.** The constant exemption in
`HoistComplexArguments.InPlace` (`GetConstantValue(...).HasValue`) also keeps a constant whose
conversion to the parameter is user-defined. `L.Use(L.C, new int[] { L.S() }, new Cell())` with
`Use(X x, …)` and `implicit operator X(int)` that logs `c` returns `cs` in C# and `sc` converted:
the array argument is hoisted ahead of the call while the conversion of `L.C` still runs at the
call. Before round 3, `KeepsMeaningInTemp` rejected that user-defined conversion and preserved the
statement.

Proposed fix (not applied, not reviewed): apply the constant exemption only when the argument's
conversion is not user-defined, i.e. `GetConstantValue(e).HasValue && !GetConversion(e).IsUserDefined`.
This is a 1-line non-test change; F5's non-test source is at 878 of the 880 lines EXC-F5-NONTEST-LINES
allows.

Per the PR 2 rules ("if the budget runs out with BLOCKING or MAJOR findings, stop and report"), the
PR was not opened. The maintainer decides: record an exception for one more fix-and-verify cycle,
accept the finding as a known residual, or re-scope.

## Review output (verbatim)

Read-only, code-traced verification at `e9901bba2`; no Calor commands or tests executed. Named witnesses are in [ArrayExpressionConversionTests.cs](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/tests/Calor.Compiler.Tests/Migration/ArrayExpressionConversionTests.cs).

| Item | Result | Witness, reasoning, and reversion check |
|---|---|---|
| Finding 1 — contested effect parity | **ACCEPTED** | `var xs = new[] { t }; xs.Initialize();`, with `t: Task`, correctly creates and declares `Task[]`. The unchanged resolver uses the existing `System.Threading.Tasks` namespace default, yielding the same result as an explicitly typed `Task[]` on main. README’s “What the parity claim covers” explicitly permits corrected receiver types. Rows 21–23 document main’s manifest/default exceptions. The conversion’s effect result changes, but this satisfies the narrowed claim. No round-3 fix to revert. |
| Finding 2 — declared array type | **VERIFIED** | `ImplicitArray_KeepsTheDeclaredStaticType`: `object[]? zs = new[] { S() }` and `object[][,] js = new[] { new string[,] { { "d" } } }` now fail whole-type equality and use the typed binding path; they cannot silently declare the created type. Unsupported round trips fail loudly. **Reverting the guard would fail the assertions excluding `§ARR{zs:` and `§ARR{js:`.** For `int[] one = new int[] { 3, 1, 2 }`, both compared types map to `i32[]`, retaining `§ARR{one:i32}` and the R0 output checked by `StatementPositionControl_IsUnchanged`. The explicit `ARRAY[` alternative retains block emission for parser-internal TypeNames. |
| Finding 3 — constant earlier argument | **VERIFIED** | `RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal`, specifically `L.UseB(L.C, new int[] { 1 }, new Cell())`: `GetConstantValue(...).HasValue` keeps `L.C` constant in the call, preserving the `byte?` overload and `3,4,1\|cs`. **Reverting this addition would hoist a nonconstant `int`, select `long?`, and fail the generated/original result comparison.** A separate regression from this guard appears below. |
| Finding 4 — empty element ranks | **VERIFIED** | `EmptyJaggedArray_KeepsItsElementRank`: `§R §ARR{a:i32[]} §/ARR{a}` emits `Array.Empty<int[]>()`. **Reverting the fix emits `Array.Empty<int>()`, fails the exact-output assertion, and gives the wrong return type.** Concatenating the split base and ranks reconstructs the complete element type; sized and initialized branches are unchanged. |

**New MAJOR regression introduced by round 3:** the constant exemption at [RoslynSyntaxVisitor.cs:11055](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11055) bypasses the user-defined conversion protection.

```csharp
public class Cell { }
public class X
{
    public static implicit operator X(int value)
    {
        L.Log = L.Log + "c";
        return new X();
    }
}
public static class L
{
    public const int C = 1;
    public static string Log = "";
    public static int S() { Log = Log + "s"; return 1; }
    public static void Use(X x, int[] xs, Cell c) { }
}
public static class Probe
{
    public static string Run()
    {
        L.Use(L.C, new int[] { L.S() }, new Cell());
        return L.Log;
    }
}
```

Original result: **`cs`**. The constant expression itself evaluates nothing, but its implicit conversion to `X` runs code. Roslyn’s constant query examines the expression’s lower bound, independently of that contextual conversion. ([Roslyn implementation](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/CSharp/Portable/Compilation/MemberSemanticModel.cs))

With the new guard, `L.C` stays in place while the earlier array argument is hoisted ahead of the later `new Cell()`. The generated equivalent evaluates `L.S()` before calling `Use` and performing the conversion, producing **`sc`**. Before this addition, `KeepsMeaningInTemp` rejected the user-defined conversion and triggered preservation. The constant exemption must also exclude user-defined contextual conversions.

No other new BLOCKING or MAJOR regression was identified in the round-3 touched code.

VERDICT: REQUEST CHANGES
