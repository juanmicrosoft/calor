# Soundness sweep

`Calor.Soundness.Sweep` is the harness from the 0.24 soundness sweep (#1311, #1419). It
generates 710 single-function cases from the packet in `tests/TestData/SoundnessSweep/`,
compiles each one with a compiler loaded by path, and compares the verifier's claim with an
independent oracle. A **false proof** is a contract the verifier reports as proven while the
oracle finds an input that violates it.

Since #1589 it runs against `main` every day in `.github/workflows/soundness-sweep.yml`. All
three baseline slots of the harness (B1, N1, P845) point at the same build of `main`.

## Run it locally

The harness runs on macOS ARM64 only: it loads the osx-arm64 native Z3 and checks its
registered hash.

```bash
src/Calor.Compiler/scripts/download-z3.sh          # once per clone
dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release
dotnet build bench/Calor.Soundness.Sweep/Calor.Soundness.Sweep.csproj -c Release
scripts/run-soundness-sweep.sh --out /tmp/sweep-$(date +%s)
```

The run takes about 5 minutes (1,448 case executions). The script points `HOME`, `CALOR_HOME`
and `DOTNET_CLI_HOME` at a fresh directory under `--out`, so it does not touch your home
directory. It then runs `scripts/soundness_sweep_gate.py gate`, prints the summary, and exits
1 on failure. The harness's own `run` exits 0 even with findings, so always read the gate.

The full report is in `<out>/results/`:

- `summary.json`: row status counts and finding counts.
- `controls.json`: control results, including CTRL-RETRO-845.
- `B1/row-status.json`, `B1/findings/`: per-row status and one record per finding.
- `B1/case-results.jsonl`, `B1/attempts/`: every case and attempt.

`--compiler DIR` sweeps another build, for example one with a fault injected.

## What fails the gate

- **An invalid run.** A nonzero `run` or `report` exit, `invalid-run.jsonl`, or a run that is
  not `pinned`.
- **A failed control.** The 13 oracle controls, solver availability, the mutation pairs, or a
  CTRL-RETRO-845 or CTRL-RETRO-FIXED token that differs from its registered expected outcome.
- **Any finding.** A false proof, stale-cache proof, spurious refutation, missing demotion, and
  so on, or a row with status FALSE-PROOF or FINDING. The gate reads every retained attempt
  of a case, including the late result of an attempt that ran over time. A finding in a retry
  therefore fails the job even when the harness combines the attempts into `flaky`.
- **A changed verdict.** A case whose `(class, token)` differs from
  `tests/TestData/SoundnessSweep/expected-verdicts.json`, or a case that did not run.

Timeouts count as verdicts. Main has 4 timed-out cases today. A case that newly times out, or
an expected timeout that now completes, fails the job. The summary marks the changes that
involve a timeout; check the runner's speed before suspecting the compiler.

CTRL-RETRO-845 checks the #845 fix (PR #961). With every slot on `main`, the control cannot
show the pre-fix bug, so rows NUM-LITERAL-TYPED and NUM-ARITH-ADD stay INCOMPLETE. That is
expected. The gate still requires each of the 6 control cases to give its post-fix outcome.

## When the scheduled sweep fails

A failed scheduled run opens one issue labeled `soundness-sweep`, or comments on the open one.

1. Open the run's job summary. It lists every failure and the timeout drift.
2. Download the `soundness-sweep-*` artifact for the full report.
3. Find the cause:
   - **A finding or a changed verdict.** Treat it as a soundness bug in the compiler. Find the
     case in `results/B1/findings/` or `case-results.jsonl`, reproduce it locally with the
     script above, and bisect the merges since the last green run. File it with the `p0` or
     `p1` label. Do not edit the fixture to make the job green.
   - **A failed control or an invalid run.** The run cannot be trusted; nothing was measured.
     Check the runner image, the .NET SDK and the Z3 bootstrap first.
   - **Only timeout changes.** Usually a slower or faster runner. Rerun once. If it persists,
     compare the case durations in `ledger.jsonl` with a green run, and update the fixture in
     a reviewed PR if the new timing is the norm.
4. Close the tracking issue when `main` is green again.

### Updating the expected verdicts

When a compiler change is meant to change a verdict (for example, a new proof or a new
refusal), update the fixture in the same reviewed PR:

```bash
scripts/run-soundness-sweep.sh --out /tmp/sweep-new
python3 scripts/soundness_sweep_gate.py freeze --out /tmp/sweep-new \
  --commit "$(git rev-parse HEAD)" --runner "local macOS arm64"
```

`freeze` runs every gate check except the fixture comparison, on all three slots, using the
real `run` and `report` exit statuses the script recorded in `exit-status.json`. It refuses the
run if any check fails or if the slots disagree, so a finding can never become an expected
verdict. Say in the PR body which cases changed and why. A PR that touches the fixture, the
gate or the harness runs the full sweep in CI.

## Pull requests

Every PR runs `harness-check`: the harness builds, the fixture covers exactly the cases in
`cases-index.json`, and the gate's self-tests (`scripts/test_soundness_sweep_gate.py`) pass.
