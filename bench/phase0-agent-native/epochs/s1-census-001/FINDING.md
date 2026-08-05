# s1-census-001 — WS-S1 failure-cause census: **PP-S1 = MISS** by the pre-committed rule

**Run:** 2026-08-05. Full round-trip over the three pinned subjects, then `failure-census`.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Gate:** gates **A-1.6(b)** / substrate plan **§10**, pre-committed and encoded in
`FailureCensus.Top3ContinueThreshold` **before** these numbers existed:
**top-3 causes ≥ 50% → continue WS-S1; otherwise → PP-S1 = miss** (exhaustive).

## Verdict: **PP-S1 = MISS**

> top-3 causes cover **40.4%** < 50% — long tail, not a work-list.

| | |
|---|---:|
| Failures classified | **141** |
| Distinct causes | **33** |
| Top-3 share | **40.4%** |
| Top-10 share | 72.3% |
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

No single cause exceeds 16.3%. The distribution is genuinely diffuse: **33 distinct causes across 141
files**, spanning C# resolution errors after round-trip (`CS0103`, `CS0246`), Calor binding/type
inference, parser-level structure errors, emitter syntax errors, and indentation handling. These are
not one bug with 141 faces.

## Robustness: the verdict does not depend on the normalization

The number this verdict is most sensitive to is how diagnostics are bucketed into "causes", so the
sensitivity is published rather than asserted. Recomputed over the same 141 failures:

| Bucketing | top-3 | Verdict |
|---|---:|---|
| Exact verbatim first message (no collapsing) | 12.8% | MISS |
| **Defect-level** (unit-of-fix, hand-classified) | **29.8%** | **MISS** |
| Status + full set of codes per file | 27.0% | MISS |
| Last-code-wins / lex-first / most-frequent | 32.6–37.6% | MISS |
| Denominator = 148 (including the 7 loss-ledger files) | 36.5% | MISS |
| **Shipped** | **40.4%** | **MISS** |
| Merge all 6 C# name/type-resolution codes into one | 46.8% | MISS |
| + merge shape families as well | 49.6% | MISS *(one file from flipping)* |
| All `Reverted:CS*` as one bucket / status only | 76.6–100% | CONTINUE |

**The verdict flips only above the granularity of "a cause"** — every flipping variant merges by
diagnostic *family* or by pipeline *status*, and `Reverted` is not a cause, it is which stage noticed.
The shipped normalization is **not** the concentration-minimising choice: six alternatives, including
the most gate-faithful one, score *lower*.

**The cascade hypothesis was tested and it backfires.** If the top two buckets (`CS0103` 23,
`CS0246` 20) were cascade symptoms of one root, true concentration would be higher. They are not:
17/20 of the `CS0246` files are one defect (`using`/namespace resolution), but the `CS0103` files
split across ≥4 unrelated defects (`Calor` ×10, `default` ×7, `_chain*` ×2, plus singletons).
Bucketing by actual unit-of-fix gives **top-3 = 29.8%** — *worse* for CONTINUE than the shipped rule.

**And the verdict survives a bar-derived threshold too.** M-S1 needs 65 of 141 files = **46.1%**, so
the frozen 50% is *stricter* than the question strictly demands — but 40.4% < 46.1% as well. You must
reach **five** causes to clear half (top-4 = 48.2%, top-5 = 53.2%).

**Disclosed, because pooling hides it:** per project, top-3 is FluentValidation **56.2%** (which alone
would read CONTINUE), Serilog 48.4%, MediatR 46.2% — all above the pooled 40.4%, because pooling three
projects with different dominant causes fragments the aggregate. Pooling over the 141 was the
pre-registered denominator and governs; a successor plan wanting a per-project gate must register one.

## The datum that would tempt an override, corrected and not acted on

**Top-10 causes cover 72.3% of primary causes — but fixing them would make at most 74 files native,
not 102.** An earlier draft of this record quoted the primary-cause count and called it "about 96
files against a need of 65". That was an upper bound assuming one blocking diagnostic per file: of
the 102 files whose *primary* cause is in the top-10, **28 carry a further diagnostic outside it** and
would still not go native. Realistic ceiling **74 against a need of 65** — and still optimistic, since
the 10-diagnostics-per-file cap hides more on some files.

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
  shape (paths, positions, quoted identifiers, bare token names and column numbers collapsed). An
  earlier version collapsed only the first three, which split one parser bug three ways
  (`but found Class/Interface/Enum` = 10+3+1) and one indent bug three ways
  (`to column 4/2/6` = 4+1+1) — an inconsistent application of its own principle that *understated*
  concentration at 38.3%/38 causes. Corrected to 40.4%/33. The sensitivity table above is the
  honest statement of how much this choice matters.
