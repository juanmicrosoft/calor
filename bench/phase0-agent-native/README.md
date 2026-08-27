# Phase 0 — Agent-Native Benchmark Suite

**Parent docs:** [`docs/plans/agent-native-strategy.md`](../../docs/plans/agent-native-strategy.md) (v4.1, §4 Phase 0) and [`docs/plans/agent-native-gates.md`](../../docs/plans/agent-native-gates.md) (thresholds, freeze rules).
**Status:** Scaffold. The suite is **not frozen**; nothing here is gate evidence yet.

This suite measures the strategy's narrow bet: *machine-checked proofs and enforced effect discipline vs agent-generated evidence in C#* — same tasks, two arms, arm-shared held-out tests, pinned models, adversarially pre-registered categories.

## Layout

```
categories.json          Pre-registered category registry (machine-readable; freezes with the suite)
task-spec-schema.json    Schema every pair manifest must validate against
templates/calor-arm/     Execution-path template for the Calor arm (Calor.Sdk csproj + test project)
pairs/<ID>/              One directory per fixture pair:
  pair.json              Pair manifest (schema above)
  spec.md                Behavioral specification (arm-neutral; the ONLY task statement)
  tests/                 Arm-shared held-out tests (black-box, run via dotnet test in either arm)
  csharp/                Idiomatic C# starting fixture
  calor/                 Idiomatic Calor starting fixture
epochs/<epoch-id>/       Per-epoch pins.json + raw results (created by runs, never edited)
```

## Construction rules (from the gates doc — enforced, not aspirational)

1. **Spec first.** `spec.md` and `tests/` are written before either fixture. Fixtures are authored independently, idiomatically per language, from the spec alone.
2. **Tests are arm-shared and black-box.** One suite, two runners. Per-arm test authorship is prohibited (it would make the escaped-bugs metric incomparable).
3. **Structural parity at authoring:** declaration count and cyclomatic-complexity sum within ±30% across arms; recorded in `pair.json`.
4. **Wedge pairs (W1–W3) are authored against** [`docs/verification-modeled-forms.md`](../../docs/verification-modeled-forms.md): intended contracts must fall inside the modeled-forms whitelist, and W3 effect boundaries must be manifest-covered (BCL manifests + `calor-runtime.calor-effects.json`). A wedge pair whose contracts fall outside the whitelist is invalid at authoring time.
5. **Calor-arm config is pinned** (gates doc §1): SDK path, enforcement on, permissive off, contract mode debug, Z3 present. Runs violating the pin are invalid by automated check.

## Runner

Extends `tests/E2E/agent-tasks/run-agent-tests.sh` (live-agent harness, majority voting). Additions needed (tracked below): pair-manifest support, two-arm dispatch, held-out test execution via the calor-arm template, transcript capture for the `.g.cs` dead-end metric, per-epoch pins.

### Per-run capture (v0.16 W1 — roadmap §3.1, gate 8, #1094)

`run-pair.sh` and `run-bundle.sh` run the agent as
`claude --print --verbose --output-format stream-json --forward-subagent-text …`
streamed through `tee transcript.jsonl | jq -c 'select(.type=="result")' > agent.json`,
so `agent.json` is exactly the result envelope it always was (token accounting and
invalid-run detection are unchanged) and every run additionally archives:

| File (per `run-N/`) | What it is |
|---|---|
| `transcript.jsonl` | every stream-json event of the run (assistant content blocks, tool calls, tool results, the result). **A run without it is `invalid`** (`detect_invalid_run`, both runners; `result.json` `invalid: true`). Null-agent runs write a one-line synthetic transcript so the rule holds uniformly. |
| `agent-builds.jsonl` | one record per Bash tool call whose command mentions `dotnet build`, `dotnet test` or `calor`, joined to its tool result: `{index, toolCallOrdinal, toolUseId, messageId, parentToolUseId, command, kind, exitCode, isError, output, outputTruncated}` — the agent's own build stdout, which §0.2 noted was never archived. The harness's own strict CLI compile of every build-time state (`journal.jsonl` diagnostics) is unchanged. |
| `calor-build-state.json` | the workspace's `obj/calor/.calor-build-state.json`, copied right after the agent stops (before the harness's final build and the workspace delete). |

New `result.json` fields:

| Field | Meaning |
|---|---|
| `turns.assistantMessages` | number of **distinct assistant `message.id`** values in the transcript — the per-turn field A-1.12 registers. stream-json emits one event per content block, so events are not turns; and it is **not** `num_turns`. Subagent messages (visible because of `--forward-subagent-text`, `parent_tool_use_id` set) are counted in the total and split out as `turns.assistantMessagesSubagent` / `turns.assistantMessagesTopLevel`, matching the corrected-token rule (A-1.9.1) which already sums subagent tokens. |
| `turns.numTurns` | the envelope's `num_turns`, archived beside it. |
| `agentBuilds.count` | records in `agent-builds.jsonl`. |
| `compilerHash` / `buildState` | from `calor-build-state.json` (#1094): the compiler's own attestation of the product that built the agent's code, so an analyzer can assert both arms of a parity epoch differ. `buildState.archivedFrom` is `agent-workspace`, or `harness-final-build` when the agent never built and the harness's final build produced the state; `null` on the C# arm. |
| `armConfigKey`, `controlArmKind`, `permissiveEffects`, `fixture`, `templateSource` | provenance of the arm entry the run used (below). |

All of it is derived by `harness-capture.py` (`turns`, `builds`, `build-state`, `pair-config`, `leg-b-pairs`, `self-test`), tested by `tests/test_harness_capture.py`.

### Pre-rows control arm (v0.16 §4.1 — additive to the §1 pin)

The calor-arm config pin (`enforceEffects: true, permissiveEffects: false, contractMode: "debug", z3Required: true`) stays; `run-pair.sh` exits 3 on any other config. The one **additive** exception is the registered pre-rows control arm for PP-W-rows: an `arms.<key>` entry whose config is the pin with `permissiveEffects: true` **together with** `controlArmKind: "pre-rows"`. `permissiveEffects: true` without the kind, the kind without permissive, an unknown kind, or any other deviation is rejected.

Because PP-W-rows runs **two calor arms from one pair** with different starters (arm A from the row-less `before/` programs, arm B from `after/`), a pair.json can carry several calor entries and `run-pair.sh --arm-config <key>` selects one (default: the arm language). Each entry's `fixture` names the starter directory under the pair (default: the arm language); `reference/<fixture>/` is the null-agent solution. The contract the `W-00x` pairs must follow:

```json
"arms": {
  "calor-pre-rows": { "fixture": "calor-pre-rows",
                      "config": { "enforceEffects": true, "permissiveEffects": true,
                                  "contractMode": "debug", "z3Required": true,
                                  "controlArmKind": "pre-rows" } },
  "calor":          { "fixture": "calor",
                      "config": { "enforceEffects": true, "permissiveEffects": false,
                                  "contractMode": "debug", "z3Required": true } }
}
```

The template (`templates/calor-arm/CalorArm.csproj.template`) carries
`<CalorPermissiveEffects>__CALOR_PERMISSIVE_EFFECTS__</CalorPermissiveEffects>`, substituted from the
arm config. For the pre-rows arm the template is taken from the **harness** checkout (the
`v0.14.3` tag's template predates the property) with `__REPO_ROOT__` still bound to the arm's
product; `result.json` records `templateSource`. Before any spend, `run-pair.sh` compiles
`templates/calor-arm/permissive-canary.calr` through the arm's own Calor.Tasks build and requires a
successful build carrying `warning Calor0410` — proof the property is honoured, not just set. At
`7d621c0d` neither `src/Calor.Sdk/Sdk/Sdk.targets` nor `src/Calor.Tasks/CompileCalor.cs` threaded a
permissive knob into `CompilationOptions.UnknownCallPolicy`, on main or on the `v0.14.3` tag. The main
side is the `feat/calor-tasks-permissive-effects` PR (`CalorPermissiveEffects` MSBuild property); the
`v0.14.3` tag cannot receive it, so the registered control arm is a one-commit branch off the tag.

**The exact statement A-1.12 registers for arm A:** *arm A = tag `v0.14.3` + commit
`283ec9f9964ddd5b21da15b646a0dd77d53de99e` (branch `arm/v0.14.3-pre-rows`, never merged), whose diff
is confined to `src/Calor.Tasks/CompileCalor.cs` and `src/Calor.Sdk/Sdk/Sdk.targets` and only threads
the existing `--permissive-effects` policy through MSBuild (`<CalorPermissiveEffects>`); compiler
semantics are v0.14.3's — nothing under `src/Calor.Compiler/` is touched.* `run-ppw-epoch.sh` refuses
any other arm-A commit and re-verifies the diff confinement against the tag before spend; the canary
against that build emits `warning Calor0410` (permissive) / `error Calor0410` (strict).

**What the pre-rows arm's waiver covers, so nobody later blames the policy.** Permissive
suppresses `Calor0425` and demotes `Calor0410` / `Calor0411` (single-module and cross-module) to
warnings. It does **not** waive `Calor0424`, `Calor0420` / `Calor0421` or `Calor0418` — those stay
errors under every flag, by design. Two harness-relevant asymmetries between the MSBuild path the
agent builds through and the CLI, both inert for PP-W-rows and recorded here rather than
rediscovered mid-epoch: the `CompileCalor` task has no `StrictEffects` parameter (the CLI's
`--strict-effects` has no MSBuild form), and the task gates its cross-module pass on
`EnforceEffects` while the CLI runs cross-module enforcement unconditionally. Both arms build with
`CalorEnforceEffects=true`, so neither difference is exercised.

`run-ppw-epoch.sh` drives the six `W-001 … W-006` pairs (exact-id directory match; the `W1-`/`W2-`/`W3-`/`W5A-` directories cannot collide), interleaved, arm A = `v0.14.3` under `arms["calor-pre-rows"]`, arm B = `v0.15.0` under `arms.calor`, with the same rails as `run-ppe1-epoch.sh` (`--confirm-paid-epoch`, distinct `Calor.Tasks` hashes, run-once epoch ids, null-agent ids suffixed `-null`). It fails before any spend listing every missing or malformed pair. Its `pins.json` `ppW` block carries the registered leg-B denominator `legBPairs` (default `W-001 W-002 W-003 W-004 W-006`; W-005 is leg A only) and `blindPairs` (`W-001 W-004 W-006`); `ppw-analyze.py` (W2) reads them from there, never from a script default.

`ppe1-margin-derivation.py` gained `--population {w5-parity-002,e1-rows-parity-001,pooled}`, `--sims`, `--boot`, `--seed`, `--grid {frozen,extended}` (adds 1.15/1.20) and `--half-width`; the defaults reproduce the committed `ppe1-margin-derivation.txt` byte for byte, and `ppw-margin-derivation.txt` is the PP-W-rows run (`--population e1-rows-parity-001 --sims 3000 --seed 4537 --grid extended`).

## Status / what remains before the baseline

- [x] Category registry pre-registered (`categories.json`)
- [x] Pair schema + calor-arm execution template
- [x] Seed pair W3-001 (demonstrates the format; NOT yet difficulty-validated)
- [ ] Gates doc feasibility calculation (dry run ≥3 runs/arm on ≥5 pairs) → freeze N and thresholds
- [ ] Author remaining pairs to the §5 proportions (W1–W3 ≥4 each, N1 ≥8, C1–C3 ≥2 each)
- [ ] Two-arm runner extension of `run-agent-tests.sh`
- [ ] Difficulty-equivalence pass (gates doc §3) → re-author/drop out-of-band pairs
- [ ] **Freeze** (suite + gates doc together) → record baseline epoch → publish including the losses
