# Independent technical review record — #1255

These are separate-context, read-only AI adversarial reviews, not author
self-review and **not human independent methods countersignatures**. They
authorize no collection, funding, recruitment, protocol amendment, or ledger
change. PR comments will link these records and the resulting disposition.

## Round 1 — first five candidates

Reviewed snapshot: `b0adeb90`, initial 93 additions. Reviewer tool identity:
`code-review`, review name `review-1255-r1`; the exact model was not exposed.
The reviewer inspected actual sources and local raw artifacts, ran all
eight then-existing evidence tests, and performed in-memory adversarial
substitutions. It did not re-run the full compilation matrix.

### Findings and resolutions

1. **Medium — summary/transcript inconsistency accepted by CI.** Changing an
   honest compilation transcript to a parser failure, changing a held-out
   transcript to success, dropping executable hashes, or removing the command
   inventory still passed the tests.
   **Fixed:** independently required complete command/input inventories;
   exact arm invocation assertions; compile exit/diagnostic reconciliation;
   retained raw per-test TRX reconciled with every runtime summary and exit.

2. **Medium — runtime build inputs not recorded.** Generated projects inherited
   the enclosing checkout's build and package settings.
   **Fixed:** explicit runtime build properties and package versions; inherited
   build/central-package imports disabled; explicit NuGet config; host runtime,
   originating checkout and resolved package/content hashes recorded. NuGet
   audit is disabled for this isolated deterministic build, not Calor warnings.

3. **Medium — repeatability re-created every instance.**
   **Fixed:** `Adapter.Create()` constructs once outside the invocation delegate;
   repeatability uses the same delegate twice. Separate tests remain isolated.

### Scientific recommendation

**INCONCLUSIVE**, neither Exit A nor B. The three actual R4/R5/R8 witnesses
still supplied an R1 inspection signal in A's `Calor0410 unknown` warning.
Two named-effect candidates failed R8. The candidate floor did not justify
an empty-intersection claim, and the reduced retry/checkpoint defaults
did not exercise their extra branches. The reviewer identified a further
permitted route worth testing: permissive unresolved calls can be assumed
pure with no diagnostic.

**Applied:** no issue/epic closure, no threshold or rule change. Executed the
sixth candidate using registered shape 7 rather than extrapolating a B result.

## Round 2 — warning-free console candidate and instrument fixes

Reviewer tool identity: `code-review`, review name `review-1255-r2`; exact
model not exposed. The reviewer ran all 11 corrected evidence tests, compared
all 107 retained evidence files with the raw local run, verified compiler
checkout and assembly hash, evaluated runtime MSBuild import settings, and
independently re-ran the sixth candidate's visible, held-out, and honest
assemblies. It did not re-run the file-writing compilation pipeline.

### Resolved prior findings

All three round-1 fixes were verified. Counts reconciled: 36 CLI invocations,
84/84 honest runtime checks, 20 laundering visible passes, and eight actual
console-specific held-out failures. Same-instance repetition and disabled
ancestor imports were independently verified.

### New finding and resolution

**High — R1 omitted passing visible-test output.** A's compiler emitted no
diagnostics, but the visible suite printed six `lookup` lines. They were
retained in TRX and appeared under normal console logging. The reviewer
independently reproduced 5/5 visible passes with all six lines visible and
2/2 held-out failures. Quiet terminal output was an incomplete signal boundary.

**Applied:** reject the sixth candidate on R1. Keep all of its observations.
Require explicit normal console logging for every current runtime invocation.
Do not hide output or equate a potentially ignored signal with no signal.
Execute a seventh candidate using an in-memory mutation effect instead of
console output, with a real state-observing held-out suite and no observer
leaked into the visible adapter.

### Scientific recommendation

Again **INCONCLUSIVE**. Shape 7 genuinely fits the frozen denominator and flag
contrast, and readable dependency rows alone do not defeat §1.4's abstraction
premise. The concrete visible runtime output, rather than a hypothetical
general inspection habit, defeated this attempted Exit A.

## Round 3 — state-mutation candidate

Reviewer tool identity: `code-review`, review name `review-1255-r3`,
explicit model **claude-opus-4.8**. This was a new separate-context reviewer.
It independently recompiled all six quota-adapter arm/variant combinations
on the pinned release, checked the actual mutation diagnostic by removing
the helper's row in an unregistered review probe, inspected full normal
visible output and retained TRX, verified honest and laundering runtime
outcomes, and ran all twelve evidence tests. The review probe is not a
candidate observation, pilot, or added registered shape.

### Findings and resolutions

1. **Medium interpretation caveat — same compiler channel.** The changed
   effect channel is state-versus-console at runtime. Both quota candidates
   still discriminate through the same generic unresolved-call path; the
   `mut` row is not propagated to the call site.
   **Applied:** explicitly recorded in the decision. No second shape or
   independent enforcement mechanism is claimed.

2. **Low registered-assumption caveat — R1 remains argued.** The referenced,
   short dependency reveals the effect and honest alternative if opened.
   **Applied:** the decision states the exact missing-signal argument,
   source readability, and §1.4 abstraction premise. No claim of inaccessible
   information or measured agent ignorance is made.

### Scientific recommendation

**Exit A — buildable under the frozen criteria as written.** The reviewer
found no remaining concrete agent-visible effect cue: no A diagnostic, no
visible callback output, no effect-related visible assertion/name, and no
task statement about telemetry. It independently reproduced the actual
R4/R5/R8 witnesses and confirmed the helper's `mut` declaration is truthful.
It distinguished the frozen source-available premise from a requirement
to measure agent behavior, which this unpaid spike neither supplies nor claims.

The reviewer explicitly did **not** approve a paid run, recruitment,
participant involvement, epoch registration, ledger mutation, causal
agent-effect claim, or human independent methods signoff. All such gates
remain separate.
