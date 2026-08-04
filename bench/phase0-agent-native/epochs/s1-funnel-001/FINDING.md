# s1-funnel-001 — the funnel probe: first non-zero eligible supply, and fidelity is the binding lever

**Run:** 2026-08-04, **8 minutes** wall clock. Full evaluation — conversion, build, recovery,
held-out runs, attribution, bundle writing.
**Configuration:** the A-1.5-pinned adjudication config — `--stratum expressible
--max-candidates 0` (unbounded) `--target 0 --native-bar 0.70`.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Invocation:** `gen-tasks MediatR Serilog FluentValidation --stratum expressible --max-candidates 0 --target 0 --native-bar 0.70 --output bench/phase0-agent-native/epochs/s1-funnel-001`

## The funnel

| Stage | Count |
|---|---:|
| Candidates evaluated | **26** (MediatR 2, Serilog 14, FluentValidation 10) |
| — excluded `NotNativeRegion` (clause a) | 1 |
| — excluded `MutatedFileReverted` (clause a) | 7 |
| — excluded `NoObservableDefect` (clause b) | 10 |
| — excluded `ArmsDiverge` (clause b) | 3 |
| — excluded `NotVerificationAddressable` | **0** |
| **ELIGIBLE** | **5** — FluentValidation 3, Serilog 2 |

Stage rates: clause (a) kills **31%** (8/26); of the 18 reaching clause (b), it kills **72%**;
end-to-end **19%** of evaluated candidates become eligible.

## Two findings, and the second is the one that matters

### 1. The program has non-zero eligible supply for the first time

Every prior measurement was **0 eligible** — 0/3 at the close-out, 0 across the whole v0.11
measurement half. This run produces **5 eligible tasks across 2 projects**, with bundles written.
The expressible-defect mechanism does end-to-end what it was built to do.

**Addressability excluded nothing.** Every candidate that reached the addressability clause passed
it, which is the strongest confirmation yet that the widened corruption forms preserved the
`Calor0410` differential — a claim previously argued, then measured at ~74% on a sample, now 100%
on the candidates that actually reach it.

### 2. The upper bound was 8× loose, and build+recovery is why

| | pre-pass (conversion-only) | this run (build + recovery) |
|---|---:|---:|
| MediatR native candidates | 5 | **2** |
| Serilog | 93 | **14** |
| FluentValidation | 105 | **10** |
| **Total** | **203** | **26** |

The pre-pass deliberately skipped build and `RecoverBuildAsync` and labelled its output an upper
bound. It is a **far looser** bound than the framing conveyed: **87% of the apparent supply does not
survive compilation.** Native *files* drop 109 → 74 for FluentValidation, but *candidates* drop
105 → 10 — disproportionate, which means **the candidate-dense files are disproportionately the ones
the converter breaks.**

That is a direct, quantified statement that **converter fidelity is the binding constraint on
supply** — not corpus shape, not checker breadth, and no longer the mutation operator. WS-S1 is the
lever, and it is unstarted.

## Consequences

1. **M-S3 (≥ 70, frozen at A-1.5) is not met: realized supply is 5.** This run does not adjudicate
   PP-S3 — that is D-S5.1 and Call S — but nothing here suggests 70 is reachable without WS-S1.
2. **A-1.5.4's registered sensitivity is now measured.** The freeze recorded that the §4 go/no-go
   turns on attrition and that "at 25% attrition the eligible ceiling is ~37, and against 'hundreds'
   the trigger would fire". Realized end-to-end attrition is **19%**, close to that hypothesis — but
   applied to a realized candidate pool of 26, not the 203 upper bound. The go/no-go was adjudicated
   at A-1.5 against 203 and did not fire; **this run shows the number it was adjudicated against was
   8× the realizable one.** Whether that changes the call belongs to Call S, with this record as
   input.
3. **The funnel-probe-first ordering was right, and so was the fidelity-first instinct it replaced.**
   The probe was commissioned because n = 3 could not distinguish which lever binds. It now answers:
   clause (a) — fidelity — kills 31% outright, and recovery destroys 87% of apparent supply upstream
   of the funnel entirely. The maintainer's original converter-fidelity-first reading is what the
   data supports; the probe was the cheap way to establish it rather than assume it.
4. **Cost is a non-issue.** 8 minutes for the full unbounded run across three subjects, against a
   pre-registered 12 h budget. The expensive-probe framing in the S1 kickoff was wrong by two orders
   of magnitude, because most candidates die early at `NoObservableDefect`, before the Calor arm.

## What this record does not claim

- **No adjudication of PP-S3, PP-S1, or the §4 go/no-go.** All belong to D-S5.1 / Call S.
- **Not that 5 tasks are epoch-ready.** They have not passed the D-S5.1 determinism screen
  (gates §0.2's 5-consecutive-green rule); M-S3 counts only screened tasks.
- **Not a claim about real C# generally** — three pinned subjects, one frozen operator set.
- Bundles are gitignored (~4 MB each, full working copies). The reports and this record are the
  artifact; bundles regenerate from the pinned config and corpus SHAs.
