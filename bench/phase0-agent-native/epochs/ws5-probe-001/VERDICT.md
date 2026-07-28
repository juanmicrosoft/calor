# ws5-probe-001 — WS5 wedge-probe epoch VERDICT

**Loop plan M4b / D5.2.** Measures **M-W1** (injected-defect catch rate) per
gates-doc **Annex A-1.2** (A.1 M-W1 semantics + A.2 PP-W1 row). This epoch
produces the *measurement*; **PP-W1 adjudication is DEFERRED to Call 2 (M5)**
per the registration — it is reported here, not adjudicated.

## Pins / provenance

| Field | Value |
|-------|-------|
| Model | `claude-opus-4-8` (program-consistency pin, decided with the user) |
| Compiler commit | `5f3fd839` (branch `ws5-probe-001-epoch`; = main `267aee94` + the smoke-hash harness fix below) |
| Agent tool | Claude Code 2.1.212 |
| Suite | the 9 W5 pairs (W5A/B/C-001/002/003) |
| Design | 9 pairs × 2 arms × 3 runs = 54 runs; edit mechanism = raw (both arms; WS5 registers no mechanism constraint) |
| Started | 2026-07-28T14:09:13Z |

## Result

| Arm | **M-W1** | Unanimity (all-3-runs agree) | Run-level catch |
|-----|----------|------------------------------|-----------------|
| **calor** | **9/9** | 9/9 | 27/27 runs |
| **csharp** | **4/9** | 7/9 | 14/27 runs |

**PP-W1 input = Calor − C# delta = +5** (frozen threshold ≥ 3/9). The delta is
strongly thesis-favorable, but per Annex A-1.2 **PP-W1 is not adjudicated in
this epoch** — the pass/fail call is made at Call 2 (M5), which also needs the
M5 comparison epoch.

**Decidability: the pre-registered fallback does NOT trigger.** The fallback
(non-unanimous run outcomes for < 7 of 9 defects in *either* arm → PP-W1
reported-not-adjudicated) requires ≥ 7/9 unanimous in both arms. calor is 9/9
unanimous; csharp is 7/9 unanimous (exactly at the bar). Both arms clear it, so
the measurement is decidable. The two non-unanimous C# defects are W5B-001 and
W5C-003 (each `[caught, not-caught, not-caught]` → majority not-caught).

## Per-defect (per-arm majority of 3 runs)

| Defect | Class | csharp | calor | Δ |
|--------|-------|--------|-------|---|
| W5A-001 report-audit | W5-A | caught (3/3) | caught (3/3) | 0 |
| W5A-002 inventory-log | W5-A | caught (3/3) | caught (3/3) | 0 |
| W5A-003 config-echo | W5-A | **miss (0/3)** | caught (3/3) | +1 |
| W5B-001 shipping-quote | W5-B | **miss (1/3)** | caught (3/3) | +1 |
| W5B-002 loyalty-points | W5-B | **miss (0/3)** | caught (3/3) | +1 |
| W5B-003 request-quota | W5-B | **miss (0/3)** | caught (3/3) | +1 |
| W5C-001 catalog-cache | W5-C | caught (3/3) | caught (3/3) | 0 |
| W5C-002 ledger-view | W5-C | caught (3/3) | caught (3/3) | 0 |
| W5C-003 session-index | W5-C | **miss (1/3)** | caught (3/3) | +1 |

The wedge is entirely in the **W5-B runtime-contract channel** (3/3 defects,
C# 0–1/3 vs calor 3/3) plus one W5-A effect defect (W5A-003) and the W5-C
length-3 chain (W5C-003). Where both arms catch (W5A-001/002, W5C-001/002) the
agent noticed the defect without the compiler forcing it, so no delta.

## Catch-channel attribution (observational)

- **W5-A / W5-C → `Calor0410`** effect build-block: the defective `.calr`
  starter fails to build (declared-pure function performing I/O; W5-C launders
  the effect through an intra-module call chain), forcing the agent to fix it.
  `Calor0410` appears in 8/9 calor-arm run telemetry files per class (the 9th
  run's *final* envelope is clean because the agent had already fixed it).
- **W5-B → `ContractViolationException`** from the emitted `§S` postcondition
  guard, thrown during the agent's first smoke-test run (the starter compiles;
  the guard is a Debug-mode runtime check, per Annex A-1.2 — the static-verify
  channel is excluded until #807). The C# arm carries the same contract only as
  a doc comment, so it ships all three W5-B defects.

## Cost / effort

- Tokens (recorded, valid runs): calor 812 in / 247,304 out; csharp 321 in /
  69,545 out. Mean iterations-to-green: calor 1.63, csharp 1.0.
- Spend (recorded epoch, `claude-opus-4-8` @ $5/$25 per 1M): **≈ $7.93**
  (calor $6.19, csharp $1.74). Including the two aborted attempts (see below),
  total session spend ≈ $15 — far under the $1,500/epoch ceiling.

## Run integrity

- **Harness fix (this branch): smoke-integrity hash excluded build
  artifacts.** The `smokeTampered` check hashed `find $ws/smoke -name '*.cs'`
  at both the pre-build baseline and the post-build re-check; compiling
  `smoke/Smoke.csproj` emits `smoke/obj/**/*.g.cs`, which the re-check counted
  but the baseline never saw → `smokeTampered:true` on every arm that compiles
  smoke (all live runs; null-agent never builds smoke, hence its false
  negative). Per frozen A.1 that would have invalidated all 54 runs. Fixed by
  excluding `obj/`/`bin/` at both find sites (`run-pair.sh`), preserving genuine
  tamper detection. Verified: null-agent still clean, and the previously-failing
  W5A-001/calor cell now reports `smokeTampered:false`.
- **Final run: 54/54 valid, zero cap-exhaustions.** Six failed attempts across
  four slots (three calor: W5B-003/run-2, W5C-001/run-2, W5C-002/run-1; one
  csharp: W5C-003/run-3) were absorbed by the invalid-run retry cap — no slot
  exhausted it, so no defect's majority was affected. Most were transient
  API-error markers; the exception is W5C-001/calor/run-2, whose two attempts
  both ended in a watchdog SIGKILL (`rc=137`). Failures skewed toward the calor
  arm (its longer runs give more error-window exposure) but were not exclusive
  to it. An earlier *full* attempt was aborted after a sustained Anthropic API
  incident cap-exhausted 2 calor slots; it was discarded and the epoch re-run
  cleanly once the API recovered (status operational + a clean live probe).
- `smokeTampered:false` and `invalid:false` on all 54 recorded runs.

## Disposition

M-W1 measured and decidable: **calor 9/9, C# 4/9, Δ +5**. M4b closes on this
record's merge. **PP-W1 adjudication is deferred to Call 2 (M5)**, which also
requires the M5 comparison epoch (arm A = `loop-baseline-ws1`; arm B = baseline
+ WS2/WS3 isolation merge; ≥7 pairs × ≥5 runs/arm; tokens-to-green ≥15%).
