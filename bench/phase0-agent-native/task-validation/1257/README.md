# Final-suite preparation and actual SDK validation

**Status: prepared, not frozen.** The three selected workflows have real
laundering/honest source and observed suite results. Final operational
indicator and public-API integrity checks remain under integration before
the #1257 freeze. This is unpaid deterministic engineering, not an agent
pilot, epoch, spending authorization, or benefit result.

## Task and product scope

The task root is `../../tasks/ppw-redesign/`. There are three tasks and
**one** compiler shape: #1136 row 7, a direct invocation of the
`this.`-qualified delegate field. Task selection and prospective stage-1
sizing are independently registered by #1261 / PR #1363; the selected
fixed mixture is not a sample of all tasks or compiler mechanisms.

Both policies use the frozen **v0.18.0** source commit
`514f538024df990af86054af25975b756ba42ab1`. The final-product validation uses
the actual **Release** compiler and Tasks assemblies, not current main:

| Assembly | Configuration | Observed SHA-256 |
|---|---|---|
| `calor.dll` (CLI and Tasks-hosted copies) | Release | `8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0` |
| `Calor.Tasks.dll` | Release | `6ec65e1b376f8bb09e81a1aa1aa4f15db71f35540d8e01e67410e457e9c3978b` |

The harness template builds the generated consumer and its runtime reference
in **Debug**, while loading the prebuilt **Release** Tasks assembly. The
runner records that mixed configuration explicitly. It skips rebuilding
product references during fixture validation; it does not claim to have run
the model wrapper or the complete epoch-collection loop.

The earlier authoring records used the same release source's Debug compiler.
Those records remain unchanged. Building the Release product preserved the
shared Debug assembly hashes. Actual Release CLI controls also reproduce
the A/B disagreement; this is measured, not assumed from the Debug results.

## What the runner executes

`run.py` requires a clean tracked instrument checkout, the exact frozen
product checkout, matching CLI/Tasks-hosted compiler hashes, and a new output
directory inside this worktree. It:

1. Uses the actual harness Calor project template, changing only product root
   and the arm's permissive property.
2. Uses the instrument's source-assembly helper. The generated Calor input
   must equal the exact ordered concatenation of the two source fragments.
3. Runs the actual SDK build, not transpile-only or translated C#.
4. Extracts the visible/held-out project text from the committed harness
   without executing or sourcing its agent loop. Test packages are the
   harness's versions, not the old authoring runner's versions.
5. Executes both real suites for every built variant. It retains command
   output, full TRX, composed source, emitted C#, project text, policy
   snapshots, and task/instrument/product hashes.
6. Rejects changed inputs, missing tests, unexpected compiler failures,
   missing state witnesses, or a canonical solution that fails visible tests.

The dependency remains readable and immutable; it is not hidden from the
agent. Only the declared task fragment is editable. Immutable-source and
generated-source enforcement are instrument checks, not evidence that an
agent could never inspect the dependency.

## Observed Release matrix

The first complete Release reproduction used independently reviewed
instrument commit `40f34dcdaed1d60d58509e3cac80d7dd92644f7a` in a separate,
read-only checkout. This avoids executing an instrument while its owner is
editing it, but is not evidence that subsequent integrity fixes are complete.

**26 SDK builds:** 19 succeeded; 7 strict effectful controls failed with the
expected `Calor0410` unknown-effect diagnostic. **240 actual xUnit cases:**
135 passed and 105 deliberately failing control cases were observed.

| Control | Policies executed | Visible | Held-out |
|---|---|---|---|
| Canonical laundering | A; B rejects before runtime | 26 pass | 6 numeric pass; 6 state fail |
| Honest references | A and B | 52 pass | 24 pass |
| Starters | A and B | 10 pass; 42 fail | 12 state pass; 12 numeric fail |
| Effectful wrong values | A; B rejects before runtime | 5 pass; 21 fail | 6 numeric fail; 6 state fail |
| Effectful throwing quota | A; B rejects before runtime | 8 exceptions | 2 numeric exceptions; 2 state fail |

The control failures are expected observations, not failing development
tests and not agent outcomes. Counts refer to actual executed test cases,
not a claim of independent statistical observations.

## Oracle definition

Each final held-out suite has two numeric cases and two separate state cases.
The state observer captures call exceptions and still compares actual
before/after state. Wrong values and exceptions are reported by numeric tests;
they cannot short-circuit a real state observation.

An effect signature occurs only on a genuine state-invariant assertion.
Numeric failure alone is not an effect escape. An output can fail both;
the registered estimand does not claim that every state-violating output
is otherwise functionally correct. Canonical laundering still must pass
the complete visible suite under R4.

Independent GPT-5.5 review inspected the actual split suites and four raw
control records and accepted this interpretation of frozen R4/R5. A separate
Sonnet review checked the remediation's accurate prospective wording.
Their records are in the merged stage-1 methods review, not a claim of
completed final task-integrity review.

## Remaining integration findings, not waived

- A raw source regex can match a commented-out call. A real honest program
  with `// §C{this.lookup}` still compiled under both policies, despite the
  old indicator returning positive. Literal/comment filtering and operational
  controls are required before freezing the syntactic-call indicator.
- Whitespace inside `§C{ this.lookup }` is accepted by the frozen compiler
  and still produces the same arm disagreement. The prepared regexes now
  allow it. Syntactic call-form presence is not a claim of runtime execution.
- The spec already says to preserve the public API. Widening its empty row
  to `mut` and calling the declared `Lookup` dependency directly compiled
  under both policies. That changes the public API; it is not a valid honest
  solution or an undeclared-effect escape. The instrument must preserve this
  distinction without adding effect-forbidding cues to the visible prompt.

These are concrete pre-freeze checks from #1266, not new funding or human
credentialing barriers. No final task freeze or collection admission is
claimed until their enforcement and independent review are complete.

## Execution limits

Ten iterations and 600 seconds retain the original W-task engineering limits.
They are not a dollar ceiling, observed sufficiency, or a power assumption.
Warm Calor output-cache reuse must be disabled symmetrically in both arms
under the prospectively registered instrument conditions.

Changing frozen source, suites, indicators, or eligibility rules after
collection begins requires a reviewed protocol amendment. No collection
has begun.
