# M5 — comparison epoch (kickoff / scoping)

**Loop plan v0.9 milestone M5.** One simultaneous A/B comparison epoch that
adjudicates **PP-L5** (does the WS2+WS3 loop tooling reduce tokens-to-green?)
and **PP-L6** (did loop work corrupt the science?), then makes **Call 2** — the
program go/no-go — on **PP-L5 + PP-W1 together**. This doc pins the design and
enumerates the prerequisites; it does **not** authorize spend (see §7). Nothing
here changes a frozen threshold — the numbers below are quoted from
`agent-native-gates.md` (Annex A, D4.4) and `loop-plan-v0.9.md` (§5, §6, §10).

Status: **KICKOFF — prerequisites not yet built.** PP-L3 is retired
(`loop-m2-baseline.md`); M5 runs PP-L5 + PP-L6 only.

## 1. What M5 measures

| Proof point | Question | Frozen threshold (source) |
|-------------|----------|---------------------------|
| **PP-L5** | WS2+WS3 tooling reduces convergence cost | **≥ 15 % relative reduction in median per-pair tokens-to-green**, arm A vs arm B, simultaneous, ≥ 7 pairs × ≥ 5 runs/arm, §6.1 adjudication (gates Annex A row PP-L5) |
| **PP-L6** | Loop work didn't corrupt the science | (a) harness-config invariance (gates §0.2) on a smoke epoch → zero config drift; (b) **neutral-task** iterations-to-green parity arm A vs arm B, §6.1 bootstrap → no significant regression. **Release blocker regardless of PP-L1–L5** (loop plan §5) |

**Metric — M-L5 tokens-to-green** (gates Annex A): per-run agent output tokens
(`agent.json` `usage.output_tokens`); per-pair means, then the **median of
paired per-pair ratios** (§2: each pair's per-arm mean first, then the ratio —
*not* a ratio of arm medians). Iterations-to-green stays **recorded /
observational** — it is floor-bound (M2 dry-run: median 1, 94 % at floor →
undetectable at any N), which is exactly why the primary measure moved to
tokens-to-green.

**§6.1 adjudication:** cluster bootstrap over pairs (runs nested within pairs),
**one-sided**, α = 0.05, on the paired per-pair ratios against the 0.85
threshold constant (= 15 % reduction). "Significant" always means this rule,
never eyeballing.

## 2. Arm design (attribution — loop plan §10 C2, the hard rule)

The delta must be attributable to **WS2 + WS3 and nothing else**. Both arms run
**simultaneously, same day, same pins, same tasks**; per-task paired ratios.

- **Arm A** — the archived WS1-only build: tag **`loop-baseline-ws1`**
  (`4f235cdc`, D4.3a). Has the envelope (WS1) but **no** MCP write path (WS2)
  and **no** warm feedback (WS3) → the agent works via raw file edits with cold
  (full-rebuild) envelope feedback.
- **Arm B** — Arm A's commit **+ WS2 + WS3 merged in isolation**. The agent gets
  the transactional MCP write path (WS2) and warm incremental feedback (WS3).
- **v0.9 HEAD is NOT arm B.** main carries unrelated M2/M4b/WS5 changes; using it
  would confound the attribution (loop plan §10 C2, explicitly). It may serve
  only as an optional, separately-labelled product-claim arm — not the
  attribution arm, and not part of this adjudication.

Arm-B isolation recipe (to be built + verified in the follow-up, §6): base
`4f235cdc` + WS2 (`#796`→`#800`) + WS3 (`#801`,`#802`,`#804`,`#805`,`#806`),
**excluding** M2 telemetry-only (#758), M4b/WS5 (#808/#810), and any unrelated
main changes. Exact cherry-pick/merge set and conflict resolution are the
first engineering step; the build must compile clean in Release before it can
run.

## 3. Pairs and N

- **PP-L5 set:** the "warm" loop tasks where the WS2 multi-edit write path and
  WS3 warm feedback plausibly help — **W2 (×5) + W3 (×4) = 9 pairs** (≥ 7
  satisfied; W1 ×3 available as substitutes if any W2/W3 pair proves
  degenerate). N = **5 runs/arm** (the frozen floor).
- **PP-L6(b) neutral set:** the **N1** pairs (×4) — neutral tasks with no
  wedge, for the parity check.
- Same fixtures for both arms (only the calor build differs — §4), so tasks and
  pins are identical by construction.

## 4. Required harness capability (does not exist yet)

`run-pair.sh` today loads `calor.dll` from a single fixed path
(`src/Calor.Compiler/bin/$cfg/net10.0/calor.dll`, line ~176) — the current
checkout's build. M5 needs each arm pinned to a **different** build:

- Add a per-arm **build-pin** (e.g. `--calor-dll <path>` / `--arm-build`) that
  sets `CALOR_CLI_DLL` (envelope generation) and, for arm B, the `calor mcp`
  write-server registration, to the arm's build output.
- Arm A → the `loop-baseline-ws1` build (no `calor_file_write` → edit mechanism
  `raw`, cold feedback). Arm B → the isolation build (edit mechanism
  `mcp-file`, warm feedback). This is the intended, isolated mechanism delta —
  the two arms *should* differ in edit mechanism, because shipping that
  mechanism is what WS2/WS3 did.
- Keep the pairs, task specs, held-out suites, shims, and telemetry schema at
  current main so tasks/pins are byte-identical across arms; only the pinned
  build varies.

This is the D4.2 arm-constraint / D4.3 control-checkout capability the plan
defers to "M5 time." It is harness work, not a compiler change.

## 5. Censoring, decidability, and honest-miss handling (frozen guards)

- **Neutral censored cap (gates §2):** a gate is invalid if **either arm exceeds
  40 % censored** on neutral tasks (a ratio of mutual failure is not a pass).
- **Differential censored cap (loop plan §4/m6):** an arm's censored fraction
  may not exceed the other's by **> 5 points absolute**; beyond that the epoch is
  **reported but PP-L5 is not adjudicated**.
- **Underpowered-not-missed (gates Annex A, PP-L5 basis):** the 15 % MDE is a
  design-stage simulation estimate over only 7 clusters (400 sims × 200-resample
  bootstrap) and carries wide uncertainty. **If the M5 epoch's realized variance
  is materially higher than the dry-run's, a non-significant result is reported
  as *underpowered*, not adjudicated as a clean miss.**
- **PP-L6 limit (loop plan §5):** the escaped-bugs dimension is unmonitorable at
  authorable fixture scale (zero-vs-zero); PP-L6 does not pretend to cover it.

## 6. Sequencing (this doc is step 0)

0. **This kickoff doc → PR, reviewed.** (you are here)
1. **Build arm B** — the WS2/WS3 isolation build off `4f235cdc`; verify Release
   compiles and the `calor mcp` write path + warm feedback are present. Report
   feasibility (conflicts?) before proceeding.
2. **Harness build-pin capability** (§4) + a zero-spend null-agent shakedown
   proving each arm loads its pinned build and produces telemetry.
3. **Pre-run feasibility confirm** (D4.5 discipline): the M2 dry-run
   (`loop-feasibility-dry-002`) already sized N=5×7 for a 15 % MDE and froze the
   threshold; re-confirm the design still holds and, per the frozen fallback,
   note that a materially-higher realized variance reports underpowered.
4. **Spend authorization + live epoch** (§7).
5. **Adjudicate PP-L5 + PP-L6, publish, make Call 2** (§8).

## 7. Spend (NOT authorized by this doc)

The live epoch is **≥ 7 pairs × 5 runs × 2 arms = ≥ 70 agent runs of full loop
tasks** (multi-iteration feature-completion work — materially heavier per run
than the WS5 probe's ~5 k output tokens). Estimated spend is well above the WS5
epoch and must stay under the gates-doc **$1,500/epoch ceiling**. This will need
its own explicit authorization after prerequisites (§6 steps 1–3) are in place,
alongside the model pin (expected `claude-opus-4-8` for program consistency).

## 8. Call 2 disposition (loop plan §6.2)

Call 2 is adjudicated on **PP-L5 + PP-W1 together**, PP-W1 carrying more weight.
**PP-W1 is already measured (WS5 / ws5-probe-001): Calor − C# delta = +5, a
clear HIT — not zero-vs-zero.** So the pre-committed kill signal (PP-W1
zero-vs-zero → pivot or stop) is **not** triggered: the program proceeds on the
thesis. PP-L5 then shapes v0.10:

- **PP-L5 hit** → loop program continues into v0.10 with the same discipline.
- **PP-L5 miss** → freeze loop investment pending transcript analysis (if
  iterations are spent on verification `unknown`s rather than bad diagnostics,
  v0.10's priority flips from loop tooling to verification tiers) — loop plan §5.

## 9. Non-blockers

- **#807** (verifier unconstrained-`result` refutations) and **#809**
  (cross-module emission) do **not** block PP-L5/PP-L6 — those measure tooling
  token cost and neutral parity, not verification catch.
- **PP-L3** is retired (`loop-m2-baseline.md`) — no sub-epoch.
