# s1-funnel-001 — the funnel probe: first non-zero eligible supply, and fidelity is the binding lever

**Run:** 2026-08-04, ~8 minutes wall clock. Full evaluation — conversion, build, recovery, held-out
runs, attribution, bundle writing.
**Configuration:** the A-1.5-pinned adjudication config — `--stratum expressible --max-candidates 0`
(unbounded) `--target 0 --native-bar 0.70`.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Invocation:** `gen-tasks MediatR Serilog FluentValidation --stratum expressible --max-candidates 0 --target 0 --native-bar 0.70 --output bench/phase0-agent-native/epochs/s1-funnel-001`
**Revision 2** — adversarial review proved the first run's eligible set was **not reproducible**. The
predicate defect it found is fixed; the numbers below are post-fix and verified stable across two
independent runs.

## The funnel

| Stage | Count |
|---|---:|
| Candidates evaluated | **26** (MediatR 2, Serilog 14, FluentValidation 10) |
| — excluded `NotNativeRegion` (clause a) | 1 |
| — excluded `MutatedFileReverted` (clause a) | 7 |
| — excluded `NoObservableDefect` (clause b) | 10 |
| — excluded `ArmsDiverge` (clause b) | **0** |
| — excluded `NotVerificationAddressable` | 0 |
| **ELIGIBLE** | **8** |

Stage rates: clause (a) kills **31%** (8/26); of the 18 reaching clause (b), it kills **56%**;
**31%** of evaluated candidates become eligible.

**Stability:** two independent runs of the fixed binary at the pinned config produced the
**identical 8-candidate eligible set** (compared by `CandidateId` + file). The pre-fix run did not:
4 of 26 candidates flipped verdict between runs.

## The defect this run found in its own instrument

The first version of this record reported **5 eligible**. Review re-ran the pinned invocation and got
a *different* eligible set — 2 in, 2 out — with the total landing on 5 only because the flips
balanced.

**Root cause:** the arms' failure signatures were compared **positionally**. The C# arm took
`covering[0].ErrorMessage` from a **full-suite** run; the Calor arm took the first `Failed` result
from a **held-out-filtered** run. Both are TRX document order, i.e. xUnit completion order under
parallelism — two independently shuffled draws. A candidate whose held-out set spans several failure
signatures (one measured case: 16 failures across 3 distinct signatures) could be admitted or
excluded as `ArmsDiverge` at random.

**All three `ArmsDiverge` exclusions in the first run were this artifact** — both arms failed the
*same* test set; only the reported order differed. After the fix, `ArmsDiverge` is **0** and the
three candidates are eligible, which is exactly the predicted correction and is why supply moved
5 → 8.

**Fix:** both arms now select a canonical representative — the failure whose test `Identity` sorts
first — so the comparison is like-for-like and stable. Pinned by a regression test over a
signature-heterogeneous failing set.

## Two findings

### 1. The program has non-zero eligible supply for the first time

Every prior measurement was **0 eligible** — 0/3 at the close-out, 0 across the whole v0.11
measurement half. This run produces **8 eligible tasks across 2 projects**. The defects are real:
review hand-verified 2 of them end-to-end (clean file passes its held-out class; mutated file fails
exactly the recorded set on **both** arms; compiles in both; deterministic; single-point) and
independently reproduced the `Calor0410` differential for all of the original 5.

**On addressability, correcting the first version of this record.** It claimed "0
`NotVerificationAddressable`" was "the strongest confirmation yet" that the widened corruption
preserved the differential. **That was circular.** The predicate is a ladder and addressability is
its *last* clause, so every one of the 9 non-addressable candidates was killed earlier by clause (a)
or (b) and never reached it. "100% of reachers passed" is a restatement of "0 excluded", on a handful
of candidates. **The honest comparable number is the base rate the run's own report prints:
17/26 = 65%**, *down* from the ~74% measured on the earlier 35-candidate sample.

### 2. The upper bound was 8× loose, and build+recovery is why

| | pre-pass (conversion-only) | this run (build + recovery) |
|---|---:|---:|
| MediatR | 5 | **2** |
| Serilog | 93 | **14** |
| FluentValidation | 105 | **10** |
| **Total** | **203** | **26** |

**87% of apparent supply does not survive compilation**, and disproportionately so. Stated with the
right comparison (the first version used the wrong one): FluentValidation's **candidate-bearing**
native files drop **35 → 8 (77% reverted)** against an overall file revert rate of 35/109 (**32%**).
Under proportional reverting you would expect ~71 surviving candidates; **10** were observed.

**Alternative explanation, conceded rather than ruled out:** the site predicate and the converter's
failure modes are correlated by construction — a file has `EffectViolation` sites only if it has
substantive method bodies with corruptible returns, which is the same population the converter
breaks. The honest framing is "the converter survives trivial files and breaks substantive ones".
That supports the same conclusion — **converter fidelity is the binding constraint on supply** — but
it is not independent evidence about candidate *density*.

## Consequences (recorded, none adjudicated here)

1. **M-S3 (≥ 70, frozen at A-1.5) is not met: realized supply is 8.**
2. **A-1.5.4's registered sensitivity now has a measurement.** The freeze recorded that the §4
   go/no-go turns on attrition. End-to-end **69% attrition** (31% survive) — but the go/no-go was
   adjudicated against 203, **8× the realizable candidate pool**. Whether that changes the call is a
   Call S input.
3. **The probe cost ~8 minutes** against a pre-registered 12 h budget, because most candidates die
   early at `NoObservableDefect`, before the Calor arm runs.
4. **The funnel-probe-first ordering was right, and it vindicates the fidelity-first instinct it
   replaced.** Clause (a) kills 31% outright and recovery destroys 87% upstream of the funnel.

## What this record does not claim

- **No adjudication of PP-S3, PP-S1, or the §4 go/no-go** — all belong to D-S5.1 / Call S.
- **Not that 8 tasks are epoch-ready.** They have not passed the D-S5.1 determinism screen
  (gates §0.2's 5-consecutive-green rule); M-S3 counts only screened tasks.
- **The "calor arm" contains no Calor.** Bundles ship round-tripped **C#** (0 `.calr` files, identical
  `.csproj`), as `run-bundle.sh` documents. `Calor0410` is established by an out-of-band single-file
  probe and **is not reachable by an agent working the bundle**. So this run demonstrates the
  *measurement* end-to-end, not the agent-facing diagnostic channel.
- **The injected defect is greppable in both arms** (`__calorTaint`). That collapses the C# arm's
  difficulty and is a live threat to the discriminating power of any epoch built on these bundles.
  Pre-existing, disclosed here because a record feeding Call S should carry it.
- **The run's own report prints `DoD met: False`** and marks all three subjects as failing the
  fidelity gate. That gate is registered **inert** at A-1.5.3 (report-only; `options.Fidelity` is read
  once after generation and `FidelityGateBelowBar` is never assigned), so "realized supply is 8"
  stands — but the reconciliation is stated here rather than left to a reader.
- **Not a claim about real C# generally** — three pinned subjects, one frozen operator set.

**Artifacts.** Bundle *working copies* are gitignored (~4 MB each); each bundle's `provenance.json`
(absolute paths scrubbed), `README.md`, and a `mutation.diff` showing the exact single-point change
**are committed** — ~220 KB total. The earlier claim that bundles "regenerate from the pinned config
and corpus SHAs" was falsified by the nondeterminism above and is withdrawn; regeneration is now
verified stable, but the artifacts are committed regardless.
