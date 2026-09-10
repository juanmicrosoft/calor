# Independent suite-evidence review

## Prepared evidence, round 1

Reviewer: `suite-freeze-review-r1`, independent read-only code-review agent,
model **gpt-5.6-terra**.

Reviewed head `219c63614315d4ed4551ae2754b48cb09708cb4d` against
`d2c6d71bae9619f535b7638eb3fbbbbff9fd466c`.

**Disposition: no significant issue found in the reviewed changes.**
Acceptance is explicitly bounded to the **prepared-not-frozen** evidence.
It is not final task-freeze acceptance, methods credentialing, spending
authorization, or an experimental result.

### Executed

- All 17 targeted suite-evidence, precision, and original-candidate tests
  passed.
- Independently cross-checked all 38 recorded suite TRX counter sets against
  `results.json`: zero mismatches.

### Inspected, not re-executed

- Branch diff, current and archived validation runners, and evidence guards.
- All three task definitions and held-out state observers.
- Representative raw build/TRX/emitted-C# records.
- Immutable instrument/product commit identities and the stated Release
  assembly hashes.

The reader **did not re-execute the SDK matrix**. This record does not
attribute the author's 26 builds or 240 runtime cases to that reader.

The documented lexical-call and public-API integrity obligations remain
unresolved at this checkpoint. Final freeze requires their actual integration,
independent review of material changes, and complete final-head CI.

## Independent issue-boundary adjudication

Reviewer: `suite-boundary-review`, independent read-only code-review agent,
model **gpt-5.5**. Read #1257/#1256/#1258 acceptance criteria and the frozen
protocol's R1–R8, §7, and §9 requirements.

**Disposition: a scoped #1257 source-and-suite artifact freeze is legitimate.**
The prior global hold was broader than the written issue/protocol requires.

- #1257's done criterion is the actual committed laundering solution and
  both suites, with observed visible passes and held-out effect failures.
- Protocol §7 requires those observations and the suite freeze before any
  collection. §9 separately requires task freeze, operational indicator
  validation, exact pins, and collection admission.
- R8 is required before a **full task freeze**; separate #1258 must commit
  both compiler invocations, output, and verdict difference beside the task.
- Code-only indicator detection, public-API enforcement, generated-source
  integrity and funding remain collection-admission requirements. The reader
  found no explicit rule that they must precede this limited #1257 freeze.

The reader required narrowly scoped status wording, unchanged historical
observation hashes, separate R8 completion, continued disclosure of the
instrument defects, and no new effect-forbidding prompt. Those conditions
are adopted. This adjudication does not approve collection or the unfinished
instrument; final review must verify the actual scoped changes.

## Scoped-freeze review: clean-checkout finding

Reviewer: `scoped-freeze-final-review`, independent read-only code-review
agent, model **claude-opus-4.8**, reviewing
`5241f704165d97d8e5f0451912ecc875e6ba5ff5`.

The reader executed all 19 guards, recomputed the 64 source/suite hashes and
both report hashes, and independently compared prior Git blobs. They then
created a clean tracked-only checkout and found a **medium reproducibility
defect**: the root `[Oo]bj/` ignore rule excluded 19 emitted-C# text snapshots
from each evidence tree. The original worktree had all 266 preparation files;
the committed tree had only 247. The new preservation guard correctly failed
on the clean checkout.

The scoped source/suite content, R4/R5 observations, and limited readiness
wording were otherwise verified. The review did **not** grant final acceptance
with that guard failure.

Remediation preserves all observation bytes rather than dropping the missing
evidence. A narrowly scoped ignore exception tracks only recorded `.cs.txt`
snapshots under these two evidence trees' `generated-csharp/obj/` paths.
The 19 preparation snapshots and 19 fresh snapshots are now added to version
control. The previously tracked 247 preparation files are unchanged; the
additional 19 preserve the original locally observed bytes and manifest hashes.
Clean-checkout validation and independent remediation review are required
before acceptance.
