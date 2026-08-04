# s1-supply-002 — D-S0.5.2 operator widening: the ceiling was the operator, not the corpus

**Run:** 2026-08-04, conversion-only, no builds/tests/recovery, **no eligibility evaluated**.
**Corpus pins:** unchanged — MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Predecessor:** [`s1-supply-001`](../s1-supply-001/FINDING.md), same corpus, same command, pre-widening operator set.
**Invocation:** `enumerate-supply MediatR Serilog FluentValidation --output bench/phase0-agent-native/epochs/s1-supply-002`
**Change under test:** D-S0.5.2 — `EffectViolation`'s corruption generalized beyond `int`/`long`.
**Revision 2** — adversarial review compiled all 511 candidates and ran the addressability probe this record had declined to run. Numbers below are post-fix.

## The result

| Project | Sites (all) | Lost to conversion | Supply (native) | Supply (with-loss) |
|---|---:|---:|---:|---:|
| MediatR | 46 | 39 | **5** | 2 |
| Serilog | 177 | 65 | **93** | 19 |
| FluentValidation | 296 | 143 | **105** | 48 |
| **TOTAL** | **519** | **247** | **203** | **69** |

Against s1-supply-001 on the identical corpus:

| | before | after | × |
|---|---:|---:|---:|
| Corpus sites | 25 | **519** | 21× |
| Native supply | 9 | **203** | 23× |

## What this settles

**The supply ceiling reported at S1 was an artifact of the mutation operator, not a property of the corpus.** s1-supply-001 measured 9 native candidates and the first draft of its record attributed that to the corpus. The adversarial review argued the binding constraint was the operator's site predicate — `EffectViolation` required a **predefined `int`/`long`** return, because the corruption was arithmetic (`return ORIGINAL + taint`). That restriction was never about addressability: the `using`-nested `Directory.*` effect that makes `Calor0410` fire is independent of the return type.

Generalizing the *corruption* — `bool` flips, everything else `taint == 1 ? default(T)! : ORIGINAL`, with `void`/`ref`/pointer/`async` excluded because the corruption would not compile or would not be a single deterministic point — raised native supply **23×** with **no change to the mechanism under measurement** — a claim now *measured* rather than argued (see below), not merely asserted.

## Composition, and a defect-class change the first draft did not disclose

| corruption form | native candidates | share |
|---|---:|---:|
| `AddOne` (`+ taint`, pre-existing) | 6 | 3% |
| `FlipBool` (`^`) | 38 | 19% |
| `DefaultWhenTainted` (`default(T)!`) | ~157 | 78% |

**78% of the post-widening supply uses a corruption class with no pre-widening representative, so `9 → 203` is a comparison of operator REACH, not of one experiment's subject count.** Two consequences the first draft asserted away with "still single-point":

- **The `Default` form never evaluates the original expression.** The taint is always 1 by construction, so the `ORIGINAL` branch of the conditional is dead in every execution. On the corpus a majority of those sites elide a call — the defect is "skip the computation and return null", not "perturb the returned value". `AddOne` and `FlipBool` both still evaluate `ORIGINAL`.
- **It therefore propagates differently.** A null at a distance surfaces as an `NRE` in another frame — plausibly another file — which is direct input to `ArmsDiverge` and to `ConverterAttributed`. Whether that is easier or harder for an agent to fix than an arithmetic mismatch is an open question this widening silently changes the answer to. Downstream clause-(b) attrition is expected to differ by class and is unmeasured.

## Addressability — MEASURED, not argued

The first draft said this was "an argument, not a measurement". The measurement costs minutes on the artifact already in hand, and review ran it over 35 native candidates:

| form | n | addressable |
|---|---:|---:|
| `Default` | 17 | 12 (71%) |
| `FlipBool` | 12 | 9 (75%) |
| `AddOne` (unchanged) | 6 | 5 (83%) |

**No kind-specific gap** — the failures are kind-independent (converter-baseline `Calor0410` already firing, or not firing at all) and afflict the pre-existing form equally. At ~74% pooled, **the addressable ceiling is ≈150, not 203**, before any clause (a)/(b) attrition.

**This is not a bar relaxation, and the distinction is load-bearing.** The injected defect is the same undeclared `fs` effect; the corruption is still deterministic, still single-point, and still intrinsic to the effect, so the papering-over residual that makes the measurement meaningful is unchanged. Per the plan's M-S4 population, an operator change that raised supply by making the injected defect *less real* would count against the honesty invariant. This one changes only which return types the corruption can be expressed for.

## Consequences

1. **The §4 go/no-go input has moved by more than an order of magnitude.** The trigger asks whether required-N exceeds the corpus's candidate-supply ceiling by >10×. Against required-N of ~15–40 (optimistic deterministic-catch) or "hundreds" (the registered 20–40% effect), a ceiling of **203 native / 519 total** is no longer obviously short. **Adjudicating "the venue is unreachable" on the pre-widening 9 would have retired the venue on an artifact.** A-1.5 still owns that adjudication; this record supplies the corrected input.
2. **The funnel probe becomes worth running.** At n = 9 it could not resolve the ambiguity it was commissioned to resolve. At n = 203 the cap (`--max-candidates`) binds again, so D-3's lexicographic-prefix concern is live once more and sampling matters.
3. **Converter fidelity is now measurable as a lever, not an anecdote.** 247 of 519 sites (48%) are in files the converter could not convert — a direct, quantified WS-S1 target on the same instrument.
4. **D-5 flips to the other kind of SPLIT.** Pooled ratio 0.35 → REJECT, but that is carried by `EffectViolation` (0.33); excluding it, `NullDeref` is 2.00 → ACCEPT. In s1-supply-001 the split ran the other way (pooled ACCEPT carried by `NullDeref`). The pre-committed threshold has now failed to settle D-5 under both operator sets, which is itself evidence the statistic is too operator-sensitive to decide the question. **D-5 is referred to A-1.5 for an explicit decision, with both runs on the record.**

## What this record does not claim

- **Not 203 eligible tasks.** Eligibility was not evaluated — no clause (a) beyond conversion status, no clause (b), no addressability probe. The prior run's 3 candidates were 3/3 addressable and **0/3 eligible**; attrition here is unmeasured and could be severe.
- **Not 150 eligible tasks either.** ~74% addressability is measured, but clause (a)/(b) attrition on top of it is not.
- **Still operator-capped.** The operator visits only `MethodDeclarationSyntax` bodies: **277 expression-bodied members and 7 block accessors (+54%) remain unvisited** on this corpus. "The ceiling was ours" is *still* true of 203 — it is smaller than it was, not resolved.
- No adjudication of PP-S1, PP-S3, or the §4 go/no-go.

## A fix that was rejected, recorded because it nearly shipped

Review found 4 candidates that do not compile: 3 from `#pragma`/`#if` trivia landing mid-line after
the injected block (`CS1040`), and 1 from Serilog's `TimeProvider`, which declares
`public static TimeProvider System { get; }` and so shadows the namespace for simple-name lookup
(`CS1061`). None was in the native set, so the headline was unaffected; both are now fixed.

The recommended fix for the second was to qualify the injection as `global::System.IO.…`. **That was
tried and reverted: it compiles more widely and it silently destroys the mechanism.** The converter's
`§E`-inference does not recognise the `global::`-qualified call, so `Calor0410` stops firing and
*every* candidate loses addressability — trading the entire measurement for one site.
`EffectViolation_IsAddressable_Calor0410_IntroducedByTheMutation` fails under it, which is how it was
caught. The shipped fix instead **skips** types that shadow `System` (one site on this corpus), and a
test now pins that choice with the reasoning, so it is not "corrected" back later.
