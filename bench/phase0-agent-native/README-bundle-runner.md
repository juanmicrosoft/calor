# WS-W4 Slice-C bundle dry-run runner

Real-scale epoch runner for the D-W4.5 (provisional-i, mechanical-only) dry-run.
It runs agents on gen-tasks **Slice-C bundles** (whole-OSS-project working copies
carrying one injected defect) and adjudicates **caught vs escaped** via the
held-out oracle. It is an **adapter over the battle-tested `run-pair.sh`
machinery** — it does not reimplement the run loop; it reuses the proven building
blocks and swaps in a bundle-shaped workspace + an OSS-project oracle.

## Files

| File | Role |
|------|------|
| `run-bundle.sh` | The runner. One bundle, one arm, N runs. `--null-agent` / `--null-agent-noop` for zero-spend plumbing checks. |
| `bundle-helpers.py` | Derives the held-out `--filter` and the null-agent **reference (the fix)** from provenance + the SHA-pinned corpus. |
| `run-bundle-epoch.sh` | Epoch driver: every bundle × both arms × N runs, then aggregate. |
| `analyze-bundle-epoch.py` | Per-(bundle, arm) metrics + the D-W4.4 ceiling-recurrence signal. |

## Reused vs added

**Reused from `run-pair.sh` (copied verbatim / all-but-verbatim, marked in-source):**
`detect_invalid_run` (§0.2 invalid-run markers), the invalid-run **detect +
retry-on-fresh-workspace + record-as-failure** main loop, `kill_agent_tree`
(watchdog process-group SIGKILL), `archive_final_src` (declared-done source
archival), the **`dotnet` PATH shim** design (journal build/test invocations,
hash-based edit detection, 1-based iteration ordinals, silent held-out run after
each build/test), the **`--null-agent`** reference-application pattern, the
`claude --print --output-format json` invocation with `timeout`/`gtimeout` +
bash-watchdog fallback, and the per-run `agent.json`/`result.json` +
cost/token/iteration extraction.

**Added (what a bundle forces):**

1. **Two-copy whole-project workspace (the oracle-leak fix).** `materialize`
   makes TWO copies of the bundle arm (`csharp-arm/` or `calor-arm/` — both plain
   `.cs`; the calor arm is round-tripped C#):
   - **`$ws/src` (agent-visible):** the held-out test method(s) are **physically
     stripped** from the test sources (`bundle-helpers.py strip-heldout`), so the
     agent's `dotnet test` — filtered or not — runs only the visible suite. The
     agent can neither read nor run the oracle.
   - **`$ws_oracle` (harness-only):** a separate mktemp tree the agent has no path
     to (never named in the prompt/spec/cwd), with the held-out test PRESENT. Its
     library is kept in sync with the agent's edits (`sync_to_oracle` copies every
     non-test `.cs` from `$ws/src`, excluding the test-project dir, so held-out
     tests are never overwritten).

   No synthesized `Src.csproj`: each copy builds itself through its own test
   project (the regression net), using the Slice-B build knobs (`TargetFramework`
   + `ExtraBuildProperties`) keyed by `ProjectName` (mirrors `ProjectConfigs.cs`).
   The corpus `global.json` SDK pin is already dropped in the bundle copy.
2. **OSS-project held-out oracle** (`run_oracle`, always on `$ws_oracle`, run
   **entirely in the harness process**). At declared-done, `run-bundle.sh` syncs
   the agent's edits in, builds the regression-net project, then runs the
   **held-out filter** (must PASS = defect fixed) and the **visible/regression
   filter** (must stay green). Both filters use **exact** `FullyQualifiedName=` /
   `!=` matching computed from `HeldOut[].FilterName` — NOT substring `~`/`!~`
   (which would sweep a prefix-sibling method into the held-out leg and out of the
   regression net; observed on Serilog cand10). `strip-heldout` is **fail-loud**:
   a held-out method that cannot be located/uniquely removed — or an
   expression-bodied member whose end can't be balance-matched — aborts the run (a
   silent miss/corrupt cut = the leak persists / a red baseline = a fabricated
   measurement).

   **The agent-facing `dotnet` shim contains NO oracle information** — no oracle
   tree path, no held-out `--filter`. It only runs the real dotnet for the agent's
   own build/test in `$ws/src` and journals invocation metadata (cmd/exit,
   edit-hash, iteration ordinal, latency). All oracle work — the path and the
   held-out names — lives only in `run-bundle.sh`'s own process variables, off the
   agent's PATH and out of every agent-readable file. (Per-iteration held-out
   journaling was removed with the shim's oracle; the verdict needs only the
   declared-done run, and `iterations` = edited build/test cycles is unaffected.)
3. **Prompt = the scrubbed failing-behavior report** (`FailingBehavior.Symptom`).
   The held-out test is never shown; the agent is told the visible filter and to
   work the visible suite only, and a PreToolUse hook blocks edits to test files.
4. **Derived reference.** A bundle ships no `reference/` solution, so
   `bundle-helpers.py apply-fix` derives it: for the **csharp** arm the fix is
   exact (copy the pristine corpus file over the one-line mutation); for the
   **calor** arm it reverses the single mutated line by content match in the
   round-tripped copy (provenance Line/Column address the C# arm only), failing
   loud if the line is missing or non-unique.

## Adjudication (per run, at declared-done)

| Outcome | Condition |
|---|---|
| `caught` | held-out now PASSES **and** the visible suite is still green (no new failures vs the starting baseline) |
| `escaped` | held-out still FAILS, or the final build fails (non-building declared-done ⇒ not fixed) |
| `broke-regression` | held-out passes but a previously-green visible test regressed |
| `invalid` | **any nonzero agent exit** (crash/timeout/API error), or a plumbing error; retried up to the cap, then recorded `invalid:true` (never scored escaped) |
| `skipped` | a null-agent smoke whose reference could not be derived (e.g. calor arm, mutated line not uniquely locatable) — not a verdict |

The starting state is oracle-checked at `materialize` (the mutated arm must
present held-out FAILING and the visible suite green); the visible-fail baseline
is stored so a declared-done visible failure is scored as a regression, not a
pre-existing red.

## Metrics (`result.json`, aggregated by `analyze-bundle-epoch.py`)

- `outcome`, `escapedBugs` (held-out fail count)
- `costUsd` — **summed `total_cost_usd`** from the agent envelope (never
  hand-priced from tokens), `tokens.{input,output}`
- `iterations` / `iterationsToDeclaredDone` (edited build/test cycles),
  `wallClockSeconds`
- **D-W4.4 ceiling-recurrence signal**: the C#-arm escaped incidence across the
  epoch. If the C# arm catches ~all injected bugs itself (incidence ≈ 0), the
  ceiling persists at real scale.

## Generating bundles

```bash
git submodule update --init --depth 1 bench/corpus/serilog bench/corpus/FluentValidation
dotnet build tools/Calor.RoundTrip.Harness -c Release
HARNESS=tools/Calor.RoundTrip.Harness/bin/Release/net10.0/Calor.RoundTrip.Harness.dll
dotnet "$HARNESS" gen-tasks FluentValidation --output <BUNDLES> --target 2 --max-candidates 6
dotnet "$HARNESS" gen-tasks Serilog          --output <BUNDLES> --target 2 --max-candidates 40
# bundles land under <BUNDLES>/bundles/<taskId>/  (csharp-arm/, calor-arm/, provenance.json)
```

Bundles + workspaces are **ephemeral and never committed** (task constraint).

## Zero-spend plumbing verification (run before any spend)

```bash
BUNDLE=<BUNDLES>/bundles/<taskId>
# CAUGHT: null agent applies the reference (the fix)
./run-bundle.sh --bundle "$BUNDLE" --arm csharp --null-agent      --out /tmp/verify
./run-bundle.sh --bundle "$BUNDLE" --arm calor  --null-agent      --out /tmp/verify
# ESCAPED (negative control): null agent does nothing, defect remains
./run-bundle.sh --bundle "$BUNDLE" --arm csharp --null-agent-noop --out /tmp/verify
```

Or the whole epoch in zero-spend noop mode (must adjudicate ESCAPED everywhere):

```bash
./run-bundle-epoch.sh --bundles <BUNDLES> --out epochs/dry-run-smoke --null-noop
```

## Launching the REAL (spend-authorized) dry-run

After spend authorization, from the repo root, with the corpus checked out and
the CLI/harness built:

```bash
./bench/phase0-agent-native/run-bundle-epoch.sh \
    --bundles <BUNDLES> \
    --out bench/phase0-agent-native/epochs/w4-dryrun-001 \
    --runs 5 --real --corpus bench/corpus
```

This runs every bundle × {csharp, calor} × 5 runs, spawning `claude` per run
(honoring `CLAUDE_MODEL`), and writes `epoch-summary.json`. Start with `--runs 1`
on a single bundle to sanity-check spend, then scale up.

## Known gaps / biases (for the dry-run to weigh)

- **Presentation asymmetry (recorded, bias against Calor):** the calor arm works
  on machine-converted round-tripped C#, the C# arm on the idiomatic original.
  Carried in every `result.json`.
- **Oracle isolation (CLOSED):** the held-out test is physically stripped from the
  agent copy and present only in a separate harness-only tree; the agent-facing
  shim carries no oracle path and no held-out filter (the oracle runs in the
  parent harness process). Verified: the shim greps to 0 for the oracle path and
  for any `FullyQualifiedName` filter; the agent tree + shim + final-src grep to 0
  for the oracle path and the held-out method names; and `dotnet test` (no filter)
  in the agent copy runs the visible suite green with the row count dropped by
  exactly the held-out method(s). `strip-heldout` fails loud on any un-removable
  method or unbalanced expression body.
- **Residual (honest):** isolation is *path-and-toolchain* isolation, not a
  sandbox. The oracle tree and the parent run from the same UID, so a maximally
  adversarial agent could in principle enumerate `/proc` (Linux) or sibling
  processes to find the parent's oracle build. The decisive, verified fix is
  removing the plaintext oracle path/filter from the agent's own workspace and
  toolchain; a fully hermetic run (separate UID/container per arm) is the
  belt-and-suspenders follow-up if the dry-run shows it matters.
- **calor-arm reference derivation** requires the mutated line to appear exactly
  once in the round-tripped file; `apply-fix` fails loud otherwise (that bundle's
  calor null-agent smoke is skipped, not mis-adjudicated). The real agent path
  does not use the reference.
- **Corpus required for null-agent only.** The reference derivation needs the
  SHA-pinned corpus; the real agent dry-run does not.
