# B6 Dry-Run Protocol

Single trial of T1.B on Opus 4.7, on the **annotated** arm. Validates per-run cost envelope before committing to N=5×3×2 = 30 trials.

## Why a fresh Claude Code session

The dry run must be ecologically valid — it should match what a real T1.B trial looks like. Running it as a sub-agent inside the autonomous-research conversation conflates token accounting (the parent absorbs the sub-agent's tokens) and uses a different system prompt than vanilla Claude Code. A fresh CC session gives a clean cost number.

## Procedure

1. **Snapshot the workspace.**
   ```bash
   cd /c/Users/juanrivera/sources/repos/juanmicrosoft/calor-2
   cp -r bench/research-phase-0/csharp-baseline bench/research-phase-0/runs/dry-run-T1B-annotated
   rm -rf bench/research-phase-0/runs/dry-run-T1B-annotated/src/*/bin
   rm -rf bench/research-phase-0/runs/dry-run-T1B-annotated/src/*/obj
   rm -rf bench/research-phase-0/runs/dry-run-T1B-annotated/tests/*/bin
   rm -rf bench/research-phase-0/runs/dry-run-T1B-annotated/tests/*/obj
   ```

2. **Open a fresh Claude Code session pointed at that directory.**
   ```bash
   cd bench/research-phase-0/runs/dry-run-T1B-annotated
   claude
   ```
   - Use Opus 4.7 (`/model opus` if not already).
   - No carryover from the autonomous-research conversation.

3. **Paste the T1.B prompt verbatim** (from `docs/plans/research-phase-0/t1-maintenance-prompts.md` § T1.B):

   > Inventory reservations should expire if not confirmed within 30 minutes. When a reservation expires:
   >
   > - Its status becomes "Released"
   > - The reserved quantity returns to the inventory item's available pool
   > - A notification is sent (use the existing NotificationService)
   >
   > Add a background mechanism that processes expirations. You can use a hosted service, a timer, or anything reasonable — the test suite checks behavior, not implementation choice.
   >
   > When you're done, run the test suite and confirm everything passes.

4. **Hard caps (enforce manually):**
   - 60 minutes wall clock
   - 50 turns
   - $25 spent
   - Any of the above → halt and record the halt reason

5. **When the run completes, capture:**
   - Total turns: from `/cost` or session metadata
   - Tokens in/out: from `/cost`
   - Dollar cost: from `/cost`
   - Wall clock time
   - Halt reason: `done` / `turn_cap` / `time_cap` / `cost_cap`
   - Final diff: `git diff` from the snapshot start
   - Test results: run `dotnet test` and capture pass/fail counts

6. **Write the metrics file:**
   ```bash
   cd bench/research-phase-0/runs/dry-run-T1B-annotated
   cat > metrics.json <<EOF
   {
     "run_type": "dry-run",
     "arm": "annotated",
     "prompt": "T1.B",
     "turns": <int>,
     "tokens_in": <int>,
     "tokens_out": <int>,
     "dollar_cost": <float>,
     "wall_clock_s": <int>,
     "model": "claude-opus-4-7",
     "completed_naturally": <true|false>,
     "halt_reason": "done|turn_cap|time_cap|cost_cap"
   }
   EOF
   git diff > final-diff.patch
   dotnet test --logger "json;LogFileName=test-results.json"
   ```

7. **Run the T1.B grader** on the result:
   ```bash
   dotnet add tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
   mkdir -p tests/WholesaleOrders.Tests/Acceptance/T1B
   cp ../../graders/T1.B/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/T1B/
   dotnet test --filter "FullyQualifiedName~T1B" --logger "json;LogFileName=acceptance-results.json"
   ```

## Decision

| Outcome | Action |
|---------|--------|
| Run completes naturally, $5–$25, all acceptance tests pass | **Proceed to N=5×3×2 trials.** Cost envelope confirmed. |
| Run completes naturally but acceptance fails | **Diagnose first.** Is it the prompt's fault (ambiguous spec)? The grader's fault (probing failed)? Or did Opus genuinely miss the task? Adjust before scaling. |
| Run hits cost cap ($25) | **Halt. Re-budget.** N=5 at $25/run × 6 cells = $750 — exceeds the v3 $450 budget. Decide: raise budget, simplify prompt, or pivot. |
| Run hits turn cap (50) without completion | **Halt. Investigate.** Most likely indicates the scaffold is too complex for one-shot completion. May need scaffold simplification or prompt clarification. |
| Run hits time cap (60 min) | **Halt. Investigate.** Same as above; orthogonal signal. |

## Reporting back

After completion (success or halt), send a short note:

```
Dry-run T1.B on annotated arm:
- Halt reason: <reason>
- Cost: $<amount>
- Turns: <n>
- Tokens: <in> in / <out> out
- Wall clock: <minutes>m
- Acceptance tests: <passed>/<total>
- Existing tests: <passed>/<total> (regression: <count>)
```

That's enough to make the proceed/halt/diagnose decision.

## Why I (the autonomous agent) am not doing this myself

Three reasons:

1. **Cost accounting integrity.** Sub-agent tokens flow into the parent context's cost meter, conflating the dry-run's cost with everything else this conversation has done.
2. **System prompt differs.** A general-purpose sub-agent has a different system prompt than vanilla Claude Code. The cost envelope I'd measure isn't the cost envelope you'd hit running the real trials.
3. **Context contamination.** A 30-turn coding session in a sub-agent dumps ~100K+ tokens of tool-call output into my parent context, degrading subsequent reasoning quality.

This is one of the cases the autonomy memo flags as legitimate user-input-needed: I genuinely cannot do this step myself without invalidating its purpose. The user runs the fresh CC session; I resume from the reported numbers.
