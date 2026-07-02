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

## Status / what remains before the baseline

- [x] Category registry pre-registered (`categories.json`)
- [x] Pair schema + calor-arm execution template
- [x] Seed pair W3-001 (demonstrates the format; NOT yet difficulty-validated)
- [ ] Gates doc feasibility calculation (dry run ≥3 runs/arm on ≥5 pairs) → freeze N and thresholds
- [ ] Author remaining pairs to the §5 proportions (W1–W3 ≥4 each, N1 ≥8, C1–C3 ≥2 each)
- [ ] Two-arm runner extension of `run-agent-tests.sh`
- [ ] Difficulty-equivalence pass (gates doc §3) → re-author/drop out-of-band pairs
- [ ] **Freeze** (suite + gates doc together) → record baseline epoch → publish including the losses
