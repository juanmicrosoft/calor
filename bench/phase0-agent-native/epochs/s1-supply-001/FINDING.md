# s1-supply-001 — S1 enumeration-only pre-pass: the corpus supply ceiling

**Run:** 2026-08-04, conversion-only, no builds/tests/recovery/bundles, **no eligibility evaluated**.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Invocation:** `enumerate-supply MediatR Serilog FluentValidation --output bench/phase0-agent-native/epochs/s1-supply-001`
**Governing doc:** [`substrate-s1-kickoff.md`](../../../../docs/plans/substrate-s1-kickoff.md) D-1, D-5.

## The numbers

| Project | Native files | With-loss files | Supply (native) | Supply (with-loss) | ratio |
|---|---:|---:|---:|---:|---:|
| MediatR | 20 | 5 | **0** | 0 | n/a |
| Serilog | 72 | 17 | **3** | 7 | 2.33 |
| FluentValidation | 109 | 4 | **6** | 0 | 0.00 |
| **TOTAL** | 201 | 26 | **9** | 7 | 0.78 |

By operator: Serilog native = 3 `EffectViolation`; FluentValidation native = 3 `EffectViolation` + 3 `NullDeref`; Serilog with-loss = 1 `EffectViolation` + 6 `NullDeref`. `IndexOutOfBounds` and `DivByZero` contributed **zero** candidates anywhere.

**These are upper bounds.** Build and `RecoverBuildAsync` were deliberately skipped, so files that convert but do not compile are still counted native — recovery is exactly what would revert them. Every eligibility clause downstream only removes candidates.

## D-5 — RESOLVED by its pre-committed rule: **ACCEPT**

Pooled with-loss/native = **0.78 ≥ 0.50**, so site-level clause (a) is **accepted** and priced into WS-S1's box, per the rule fixed in the S1 kickoff before these numbers existed.

The per-project split matters and is not hidden by the pooled figure: the entire with-loss supply is **Serilog's**, and 6 of its 7 are `NullDeref`. FluentValidation's ratio is 0.00 and MediatR's is undefined. So the accept rests on one subject.

## The finding that dominates everything else

**The kickoff's D-3 target of n ≈ 30 is unreachable. The whole corpus supplies 9 native candidates — an upper bound — and 16 even if site-level clause (a) lands.**

Consequences, stated without adjudicating anything this record is not entitled to adjudicate:

1. **The probe's cap is moot.** `--max-candidates 10` per project would bind on nothing: MediatR 0, Serilog 3, FluentValidation 6. The lexicographic-prefix bias D-3 worried about is irrelevant at this n — there is no truncation to bias.
2. **D-3's power statement collapses.** At n = 9 pooled (and 0/3/6 per project, against a rule that forbids pooling), a clause-(b) pass rate of 25% yields zero observations about 7.5% of the time pooled — and per project the probe cannot distinguish anything at all. The funnel probe as designed cannot resolve the ambiguity it was commissioned to resolve.
3. **Two of three subjects are structurally unable to contribute.** MediatR yields **0** — consistent with the close-out's "MediatR already yielded 0". The M-S3 shape "≥2 from each of ≥2 projects" has, at most, two candidate-bearing projects before eligibility attrition begins.
4. **The plan §4 go/no-go now has its input.** The ceiling is 9 (16 with site-level clause (a)). Against the dry-run's required-N — "hundreds of clustered tasks" for the registered 20–40% effect, ~15–40 under the most optimistic deterministic-catch assumption — the ceiling is **more than an order of magnitude short of the registered effect**, and at or below the optimistic case *before* any eligibility attrition. **The adjudication of that go/no-go belongs to the A-1.5 freeze, not to this record**, which reports the measurement only.

## What this record does not claim

- It does not claim 0 eligible tasks. Eligibility was not evaluated; that is the point of the pass.
- It does not adjudicate PP-S3, PP-S1, or the §4 go/no-go.
- It does not claim the corpus is representative of real C# — only of these three pinned subjects.
