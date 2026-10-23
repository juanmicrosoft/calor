# PR 2 (narrowed) Codex round 1 (#1132 F5)

- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
- **Budget:** PR 2 has a fresh budget of 3 adversarial rounds plus 1 verification-only pass. This is
  invocation 1 of 4 (round 1 of 3).
- **Reviewed:** `git diff origin/main...HEAD` at `04b6316f5` (code `241006e3a`, evidence `04b6316f5`).
- **Verdict:** REQUEST CHANGES (0 BLOCKING, 3 MAJOR, 1 MINOR).

## Dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | A local read before a hoisted argument stays in place even when a later argument writes it: `Use(x, new int[] { Bump(ref x) }, new Cell())` returns 1, not 0 | Fixed in `d3e3f238d` (`b10805d2d` after the rebase onto `c020de748`): in `HoistComplexArguments`, a local/parameter read stays in place only when no later argument up to the last hoisted one runs code (invocation, assignment, increment, object creation, or a `ref`/`out` argument). Witness: `pr2-args` row of `ArrayExpressions_ConvertNatively_AndRunLikeTheOriginal`, 8 surfaces, run matches (`1,1\|1`) |
| 2 | MAJOR | `WritesInPlace` ignored `_pendingHoistedLines`, so a block-lambda element was bound to an untyped temp ahead of the array and lost its delegate type (`D[] { (int x) => { … } }`) | Fixed in `d3e3f238d` (`b10805d2d` after the rebase onto `c020de748`): the probe also rejects any queued binding; the statement is preserved as `array-element-block`. Witness: `W.Lambda` in `BlockElements_ArePreservedWithAReport_AndRunLikeTheOriginal`, 8 surfaces. The probe's fixed member depth now only makes it more conservative (a context that would emit the lambda in place, multi-line, is rejected for a §ROW too) |
| 3 | MAJOR | The resolver key ledger was not regenerated (826/137 committed; 825/136 measured) | Fixed: regenerated at `d3e3f238d` (825 string-origin, tests 136; cause: the `13-11` snapshot's `Array.Empty<str>` call became a fresh `§ARR`), with a pending identity in `commit-stamp-index.json` |
| 4 | MINOR | Docs say every array member is an unknown call, but a `System.Array`- or `ValueTask[]`-typed receiver gives no diagnostic (main's behavior) | Fixed: CHANGELOG, `convert.mdx` and the evidence README qualify the claim by receiver type |

## Review output (verbatim, findings section)

1. **MAJOR — Array arguments still run before an earlier local is read.** Location: `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11053` and `:11081`.

   ```csharp
   public class Cell { }
   public static class P
   {
       static int Bump(ref int x) { x = x + 1; return x; }
       static int Use(int first, int[] xs, Cell c) => first;
       public static int Run()
       {
           int x = 0;
           return Use(x, new int[] { Bump(ref x) }, new Cell());
       }
   }
   ```

   Original result: `0`. The retained conversion produces the equivalent of:

   ```csharp
   var array = new int[] { Bump(ref x) };
   var cell = new Cell();
   return Use(x, array, cell); // 1
   ```

   The later constructor causes the earlier array argument to be hoisted. `InPlace` exempts the earlier local read, although the array mutates that local. This surviving conversion defect receives no preservation loss; compilation validation cannot detect the changed result. The ordering tests do not cover this witness.

2. **MAJOR — The preservation probe accepts deferred lambda blocks that lose their target delegate type.** Location: `src/Calor.Compiler/Migration/CalorEmitter.cs:2312`, with the deferred emission at `:3720`.

   ```csharp
   public delegate int D(int x);
   public static class P
   {
       public static D[] Make()
       {
           return new D[] {
               (int x) => {
                   int y = x;
                   y = y + 1;
                   return y;
               }
           };
       }
   }
   ```

   The three-statement lambda queues a `§B{_hoistLam000}` block and returns its name. `WritesInPlace` checks only `_builder` and the returned string; it ignores `_pendingHoistedLines`, so it accepts the element.

   Real emission flushes an **untyped** lambda binding before the array. Its generated C# has the equivalent of:

   ```csharp
   var temp = (int x) => { /* body */ };
   return new D[] { temp };
   ```

   `temp` has the natural type `Func<int,int>`, which cannot implicitly convert to `D`. Default library conversion therefore fails round-trip validation instead of preserving this valid input with a loss. CLI rescue can conceal the failure. The probe also hardcodes member depth, so it does not reproduce field or nested-lambda contexts.

3. **MAJOR — The committed key ledger was not regenerated; the reported green test state is not reproducible from HEAD.** Location: `tests/Calor.Conversion.Tests/Snapshots/13-11.approved.calr:9` and `docs/plans/evidence/v0.25-f5-1132/README.md:134`.

   The changed Calor witness is:

   ```calor
   §MT{m004:CreateEmpty:pub} () -> str[]
     §E{alloc}
     §R §ARR{str:arrStr005:0} §/ARR{arrStr005}
   ```

   This replaces the previous `Array.Empty<str>` call and removes its resolver key. The README records the resulting measurement as **825 aggregate / 136 tests** and says regeneration was completed.

   HEAD nevertheless retains **826 / 137** in `bench/phase0-agent-native/effect-resolver-key-ledger.json:9` and `:90`; that file has no branch diff. `KeyOrigins_OverCommittedCalorCorpus_MatchLedgerExactly` asserts exact equality at `tests/Calor.Compiler.Tests/Effects/EffectResolverKeyLedgerTests.cs:174` and `:184`. The documented post-regeneration green run requires an artifact missing from these commits.

4. **MINOR — Public documentation overstates unknown-call behavior for array members.** Location: `website/content/cli/convert.mdx:241`, `CHANGELOG.md:103`, and `docs/plans/evidence/v0.25-f5-1132/README.md:75`.

   ```calor
   §M{m1:T}
     §CL{c1:G:pub:stat}
       §MT{m2:Go:pub:stat} (System.Array:g) -> void
         §E{}
         §B{x} §C{g.GetLength} §A 0 §/C
   ```

   This produces no `Calor04xx` diagnostic under the behavior pinned by `ArrayMemberEffectParityTests.cs:39`. The `ValueTask[].Initialize` row likewise expects none. These are unchanged main behaviors, but they contradict the blanket claim that array-member calls are unknown and that every array receiver produces `Calor0411`/`Calor0410`. Qualify the claims by receiver type.

VERDICT: REQUEST CHANGES
