# s1-census-001 — WS-S1 failure-cause census: **PP-S1 = MISS** by the pre-committed rule

**Run:** 2026-08-05. Full round-trip over the three pinned subjects, then `failure-census`.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Gate:** gates **A-1.6(b)** / substrate plan **§10**, pre-committed and encoded in
`FailureCensus.Top3ContinueThreshold` **before** these numbers existed:
**top-3 causes ≥ 50% → continue WS-S1; otherwise → PP-S1 = miss** (exhaustive).

## Verdict: **PP-S1 = MISS**

> top-3 causes cover **38.3%** < 50% — long tail, not a work-list.

| | |
|---|---:|
| Failures classified | **141** |
| Distinct causes | **38** |
| Top-3 share | **38.3%** |
| Top-10 share | 68.1% |
| **Unattributed** | **0** |

The denominator reconciles exactly with `s1-ledger-001` (FluentValidation 64, Serilog 64, MediatR 13
= 141), and **zero failures were unattributed** — so this census measures the converter, not the
harness's record-keeping, which was the failure mode most likely to make it meaningless.

## The ranked causes

| # | Cause | Files | Share |
|---:|---|---:|---:|
| 1 | `Reverted:CS0103` (name does not exist) | 23 | 16.3% |
| 2 | `Reverted:CS0246` (type not found) | 20 | 14.2% |
| 3 | `CompileError:` binding has no type annotation and no initializer | 11 | 7.8% |
| 4 | `CompileError:` expected EXT/METHOD/PROP/IXER/END_IFACE, found Class | 10 | 7.1% |
| 5 | `EmitSyntaxError:` unexpected token | 7 | 5.0% |
| 6–12 | `Reverted:CS1503`, `CS8917`, `CS0738`, `CS0106`, `CS1729`; dedent mismatch; identifier expected | 3–6 each | |

No single cause exceeds 16.3%. The distribution is genuinely diffuse: **38 distinct causes across 141
files**, spanning C# resolution errors after round-trip (`CS0103`, `CS0246`), Calor binding/type
inference, parser-level structure errors, emitter syntax errors, and indentation handling. These are
not one bug with 141 faces.

## The datum that would tempt an override, recorded and not acted on

**Top-10 causes cover 68.1% — about 96 files, against a need of 65.** On that arithmetic a
ten-item work-list would clear the M-S1 bar, and it is genuinely tempting to read the result as
"reachable, just not in three items."

**It is not being acted on.** The rule asked a specific question — *is fidelity a work-list?* — and
fixed **top-3 ≥ 50%** as the answer, exhaustively, precisely so the `top-3 < 50% ∧ top-10 ≥ 50%` case
could not be relitigated once visible. That case is exactly what occurred. Overriding here would be
the third time this program set aside a fired trigger, and the first two had justifications
independent of which way the number pointed (the ceiling of 9 was an operator artifact; the ledger
covered 4.7% of the gap). **This one would not: the only new information is that the answer is
unwelcome.** A control overridden whenever it binds is not a control.

The observation is recorded because a successor plan may legitimately re-open fidelity with a
*different, pre-registered* threshold. It may not be used to re-decide this one.

## What PP-S1 = miss does and does not mean

- **It does not retire the real-scale venue.** Plan §6.2's decision table is explicit: PP-S1 is a
  diagnostic input, and **supply, not fidelity, decides the venue**. The (PP-S1 miss, PP-S3 hit) cell
  reads "authorize anyway".
- **It resolves the plan's own miss branch:** the 40–53% native fraction is **structural at v0.12
  maturity, not a work-list**. WS-S1 does not proceed as a ranked-fix workstream.
- **It does not say the converter cannot be improved** — it says there is no small set of fixes that
  moves `NativeFraction` to 0.70 on this corpus, which is what the L-sized box was scoped to do.
- **Converter fidelity keeps its product value.** `NativeFraction` remains the quality metric of the
  shipped `calor import` path (gates A-1.6(b)); that justification never depended on the epoch.

## What this record does not claim

- **No adjudication of PP-S3, PP-W2, or the §4 go/no-go.** PP-S1's value is registered; Call S owns
  the rest.
- **Not that the 141 are individually hard.** No effort estimate was made — only that they do not
  concentrate.
- **Not a claim about C# generally** — three pinned subjects, one converter version.
- The cause key is `Status:CS####` where a compiler code was recoverable, else a normalized message
  shape (paths, positions and quoted identifiers collapsed). A coarser or finer normalization would
  move the shares; the chosen one buckets identical defect shapes together and is applied uniformly.
