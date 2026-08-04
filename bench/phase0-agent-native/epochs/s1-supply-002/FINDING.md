# s1-supply-002 — D-S0.5.2 operator widening: the ceiling was the operator, not the corpus

**Run:** 2026-08-04, conversion-only, no builds/tests/recovery, **no eligibility evaluated**.
**Corpus pins:** unchanged — MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Predecessor:** [`s1-supply-001`](../s1-supply-001/FINDING.md), same corpus, same command, pre-widening operator set.
**Change under test:** D-S0.5.2 — `EffectViolation`'s corruption generalized beyond `int`/`long`.

## The result

| Project | Sites (all) | Lost to conversion | Supply (native) | Supply (with-loss) |
|---|---:|---:|---:|---:|
| MediatR | 48 | 41 | **5** | 2 |
| Serilog | 181 | 66 | **93** | 22 |
| FluentValidation | 298 | 144 | **106** | 48 |
| **TOTAL** | **527** | **251** | **204** | **72** |

Against s1-supply-001 on the identical corpus:

| | before | after | × |
|---|---:|---:|---:|
| Corpus sites | 25 | **527** | 21× |
| Native supply | 9 | **204** | 23× |

## What this settles

**The supply ceiling reported at S1 was an artifact of the mutation operator, not a property of the corpus.** s1-supply-001 measured 9 native candidates and the first draft of its record attributed that to the corpus. The adversarial review argued the binding constraint was the operator's site predicate — `EffectViolation` required a **predefined `int`/`long`** return, because the corruption was arithmetic (`return ORIGINAL + taint`). That restriction was never about addressability: the `using`-nested `Directory.*` effect that makes `Calor0410` fire is independent of the return type.

Generalizing the *corruption* — `bool` flips, everything else `taint == 1 ? default(T)! : ORIGINAL`, with `void`/`ref`/pointer/`async` excluded because the corruption would not compile or would not be a single deterministic point — raised native supply **23×** with **no change to the mechanism under measurement**.

**This is not a bar relaxation, and the distinction is load-bearing.** The injected defect is the same undeclared `fs` effect; the corruption is still deterministic, still single-point, and still intrinsic to the effect, so the papering-over residual that makes the measurement meaningful is unchanged. Per the plan's M-S4 population, an operator change that raised supply by making the injected defect *less real* would count against the honesty invariant. This one changes only which return types the corruption can be expressed for.

## Consequences

1. **The §4 go/no-go input has moved by more than an order of magnitude.** The trigger asks whether required-N exceeds the corpus's candidate-supply ceiling by >10×. Against required-N of ~15–40 (optimistic deterministic-catch) or "hundreds" (the registered 20–40% effect), a ceiling of **204 native / 527 total** is no longer obviously short. **Adjudicating "the venue is unreachable" on the pre-widening 9 would have retired the venue on an artifact.** A-1.5 still owns that adjudication; this record supplies the corrected input.
2. **The funnel probe becomes worth running.** At n = 9 it could not resolve the ambiguity it was commissioned to resolve. At n = 204 the cap (`--max-candidates`) binds again, so D-3's lexicographic-prefix concern is live once more and sampling matters.
3. **Converter fidelity is now measurable as a lever, not an anecdote.** 251 of 527 sites (48%) are in files the converter could not convert — a direct, quantified WS-S1 target on the same instrument.
4. **D-5 flips to the other kind of SPLIT.** Pooled ratio 0.35 → REJECT, but that is carried by `EffectViolation` (0.33); excluding it, `NullDeref` is 2.00 → ACCEPT. In s1-supply-001 the split ran the other way (pooled ACCEPT carried by `NullDeref`). The pre-committed threshold has now failed to settle D-5 under both operator sets, which is itself evidence the statistic is too operator-sensitive to decide the question. **D-5 is referred to A-1.5 for an explicit decision, with both runs on the record.**

## What this record does not claim

- **Not 204 eligible tasks.** Eligibility was not evaluated — no clause (a) beyond conversion status, no clause (b), no addressability probe. The prior run's 3 candidates were 3/3 addressable and **0/3 eligible**; attrition here is unmeasured and could be severe.
- **Not that the widened corruption is addressable.** The `Calor0410` mechanism is unchanged by construction, but that is an argument, not a measurement. The differential probe must confirm it, and a `default(T)!` returning null for reference types is a plausible new source of `ArmsDiverge`.
- No adjudication of PP-S1, PP-S3, or the §4 go/no-go.
