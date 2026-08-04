# v0.12 S1 — expressible-stratum supply enumeration (pre-pass)

**Conversion-only.** No builds, no test runs, no recovery, no bundles, and no
eligibility evaluation — which is what permits this to run *before* the A-1.5 freeze
(plan §3 constraint (a)).

**Upper bound, not realized supply.** Build and `RecoverBuildAsync` are skipped, so a
file that converted but does not compile is still counted native here — recovery is
precisely what would revert it. Every later eligibility clause only removes candidates,
so these figures bound eligibility from above. That is the correct direction for a
"is there conceivably enough supply?" go/no-go, and the wrong number to quote as supply.

## Supply by project

| Project | Sites (all) | Lost to conversion | Supply (native) | Supply (with-loss) | with-loss / native |
|---|---:|---:|---:|---:|---:|
| MediatR | 2 | 2 | **0** | 0 | n/a |
| Serilog | 15 | 5 | **3** | 7 | 2.33 |
| FluentValidation | 8 | 2 | **6** | 0 | 0.00 |
| **TOTAL** | **25** | 9 | **9** | 7 | 0.78 |

**Read the first two columns before the third.** "Sites (all)" is the corpus-side
supply the frozen operator set can enumerate anywhere; "lost to conversion" is the part
the converter could not reach. A low native figure with a high lost figure is a
**converter-fidelity** result, not a corpus result — opposite remedies. Any downstream
use of the native number as "the corpus's supply ceiling" must account for both.

## D-5 — region-granularity clause (a)

Pre-committed rule (S1 kickoff): accept site-level clause (a) iff with-loss/native ≥ 0.50.

**SPLIT — pooled ratio 0.78 says ACCEPT, but that is carried by 'NullDeref' (its own ratio 2.00); excluding it the ratio is 0.17 → REJECT. The pre-committed threshold does not settle this; decide explicitly and record why.**

Per-project ratios are in the table above; a pooled figure must not conceal a split.

Per-operator, because a pooled ratio can be carried by a single operator:

| Operator | With-loss | Native | ratio |
|---|---:|---:|---:|
| EffectViolation | 1 | 6 | 0.17 |
| NullDeref | 6 | 3 | 2.00 |

## Candidates by operator

### MediatR

_No expressible candidates enumerated in either population._

### Serilog

| Operator | Native | With-loss |
|---|---:|---:|
| EffectViolation | 3 | 1 |
| NullDeref | 0 | 6 |

### FluentValidation

| Operator | Native | With-loss |
|---|---:|---:|
| EffectViolation | 3 | 0 |
| NullDeref | 3 | 0 |

## Clustering (native candidates by file)

The probe's `--max-candidates` cap takes a **lexicographic prefix**, not a sample, so
candidates concentrated in few files mean the probe's effective *n* is far below its
nominal *n* (S1 kickoff D-3). This table is what makes that visible before the probe runs.

### MediatR — 0 native file(s) carrying candidates

_none_

### Serilog — 2 native file(s) carrying candidates

- `src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs` — 2
- `src/Serilog/Events/EventProperty.cs` — 1

### FluentValidation — 5 native file(s) carrying candidates

- `src/FluentValidation/Internal/ValidationStrategy.cs` — 2
- `src/FluentValidation/Internal/AccessorCache.cs` — 1
- `src/FluentValidation/Validators/ComparableComparer.cs` — 1
- `src/FluentValidation/Validators/NotEqualValidator.cs` — 1
- `src/FluentValidation/Validators/RangeValidator.cs` — 1

