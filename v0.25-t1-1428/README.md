# 0.25 T1 (#1428): shared interop controls and readiness register

**Register version:** 1.0.4 (see "Amendments"). **Consumes:** R0 scope 1.6.0. **Status:** register. Every family is
READY: A1 (#1430) recorded ACCEPTED on the candidate `d1d7afde`, 0.25.0 shipped from it on 2026-10-22, and amendment
1.0.4 mirrors the post-release readiness write-back in `scope.json` (R0 scope 1.6.0). **Independence:** reduced (R0 §9). Codex reviewed this
work; no reviewer independent of the maintainer did.

| File | Content |
|---|---|
| `register.json` | Versioned per-family register: readiness (mirrors `scope.json`), blockers, every registered case with its role, expected outcome and `requirementSha256`, approved evidence hashes, the F2-INTERP-09 corpus counts, what READY still needs, the surface list with actual defaults, and the shared-control registry |
| `controls-results.json` | Committed observation for every control on every measured surface (19 controls x 7 surfaces = 133 cells) |
| `fixtures/` | 13 C# control fixtures (`.cs.txt`, kept out of the Calor-first guard and the corpus ledgers). The other 6 controls reuse R0 and F1 fixtures |
| `reproduce/aggregate.py` | Collects per-family evidence into one register for a given commit |
| `reviews/` | Codex review records for the T1 PR |

**Enforced by** `tests/Calor.Compiler.Tests/InteropScope/`:
`SharedInteropControlsTests.cs` (the oracle), `T1RegisterValidator.cs` and `T1RegisterTests.cs`
(the register), and `InteropScopeValidator.cs` S014 (the scope amendment).

## Shared controls (the oracle)

Each fixture's `Probe.Run()` returns a trace: values, aliasing (writes seen through a second
reference), evaluation order (side-effect logs), exception types and messages, and iterator
timing (laziness, interleaving, `finally` on dispose, a throw on a later `MoveNext`). Each cell
converts the fixture, compiles the Calor output, runs the generated C# and compares the trace
with the original C#. The run uses the invariant culture. Compile success alone never passes:

- a cell whose trace differs is `-mismatch` and **always fails**;
- the outcome must be one the control allows;
- the outcome must equal the committed observation, so a reclassified cell fails.

Outcomes: `native` (no `§CSHARP`/`§CS`/`§RAW`, and no reported interop, rescue, passthrough,
lossy or dropped member), `preserved` (some C# kept, nothing lossy or dropped), `rejected`
(an explicit refusal: no output, or output that still compiles and runs like the original). Every
emitted output is compiled and run, even after a refusal. Output that fails a non-effect compile is
`invalid-output`; it, `lossy` and any `-mismatch` are never allowed. Each cell also records
`surfaceError`, the surface's own failure signal (CLI exit code, MCP `isError`, library
`Success`). Compiles use built-in effect manifests only, and CLI children run with
`HOME` set to a temp directory. In-process conversion has no switch to skip `~/.calor/manifests`,
so every cell fails closed when that directory holds a manifest. `compile` records `default` when
the default compile passed, or `permissive-effects:<codes>` when only effect diagnostics
(`Calor04xx`, here always `Calor0410`, an undeclared effect) failed it and the comparison used
`EnforceEffects = false`, as R0 did.

**Surfaces** (`register.json` `surfaces`). Defaults are what each surface reports in its
`conversionPaths` (the library and CLI ones are also checked against the code):

| Surface | Entry | rescueUnusableMembers | passthroughOnError |
|---|---|---|---|
| library | `new CSharpToCalorConverter()` | false | false |
| cli-convert | `calor convert Input.cs -o Input.calr --format json` | **true** | false |
| cli-migrate | `calor migrate <dir> --skip-verify --skip-analyze --report <json>` | false | false |
| mcp-convert | `calor_convert {source}` | false | false |
| mcp-convert-passthroughOnError | `calor_convert {source, passthroughOnError: true}` | false | **true** |
| mcp-migrate | `calor_migrate {projectPath, phase: convert}` | false | false |
| mcp-batch | `calor_batch {action: convert, projectPath}` | false | false |
| sdk | not measured: `src/Calor.Sdk` has no conversion entry point (F6 D4); the library row stands for it | | |
| msbuild | not measured: `Calor.Tasks` does not call the converter | | |
| lsp | not measured: `Calor.LanguageServer` does not call the converter | | |

Project migration still rescues a member whose native output does not parse, through lossless
fidelity rather than `rescueUnusableMembers` (F6). T1-RESCUE-COMBO shows it: every surface rescues
1 member, including the ones with `rescueUnusableMembers = false`.

## Per-row results (measured at this PR's head, macOS arm64)

Every surface gave the same outcome for every control, and every trace matched the original.

| Control | Covers | Expected | Outcome (all 7 surfaces) | Compile |
|---|---|---|---|---|
| T1-REFOUT-NULLABLE | ref/out with nullable locals; aliasing; out before a throw | native-or-preserved | native | default |
| T1-LOCAL-INTERP | local functions with interpolation and calls | native-or-preserved | native | default |
| T1-ARRAY-ARGS-RETURNS | arrays in arguments and returns; callee aliasing | native-or-preserved | native | default |
| T1-ARRAY-CONDITIONAL | arrays in `?:` branches; unselected branch never runs; throwing initializer | native-or-preserved | native | default |
| T1-ITER-ACCESSOR | iterator-accessor fallback: laziness, `finally`, late throw | preserved | preserved (2 interop) | default |
| T1-RESCUE-COMBO | parse-failure rescue + iterator interop + native members in one file | preserved-or-rejected | preserved (1 interop, 1 rescue) | permissive-effects:Calor0410 |
| T1-INTERP-STRING | string target: format, alignment, braces, null hole, hole order | native-or-preserved | native | default |
| T1-INTERP-FORMATTABLE | FormattableString and IFormattable: Format, ArgumentCount, culture | native-or-preserved | native | permissive-effects:Calor0410 |
| T1-INTERP-OVERLOADS | competing overloads (string/FormattableString/IFormattable/object) | native-or-preserved | native | permissive-effects:Calor0410 |
| T1-INTERP-HANDLER | custom conditional handler (`out bool`, holes skipped when false) | preserved-or-rejected | preserved (3 interop) | permissive-effects:Calor0410 |
| T1-INTERP-CULTURE | de-DE current culture, `FormattableString.Invariant`, `string.Create(provider, ...)` | native-or-preserved | preserved (1 interop: the `string.Create` handler target) | permissive-effects:Calor0410 |
| X-INT-01 | F1 x F5: `Bump(ref pts[0].X)` (F1-REFOUT-08) | native | native (result 2, like C#) | permissive-effects:Calor0410 |
| X-INT-02 | F5 x F6: F5-ARRAY-01 | native | native, no rescue | permissive-effects:Calor0410 |
| X-INT-03 | F4 x F6: F4-ITER-01 | preserved | preserved (2 interop, no rescue) | default |
| X-INT-04 | F2 x F6: F2-INTERP-03 handler | preserved | preserved (2 interop, no rescue) | permissive-effects:Calor0410 |
| X-INT-05 | F1 x F3: ref/out local function, out before a throw | native-or-preserved | preserved (1 interop) | default |
| X-INT-06 | F2 x F3: hole calling a native local function | native-or-preserved | native | default |
| X-INT-07 | F3 x F5: 1-D and 2-D arrays returned from native local functions, an array argument, a `_hoist000` user local | native | native | default |
| X-INT-08 | F1 x F2: `out var` in an interpolation hole | native-or-preserved | preserved (1 interop) | permissive-effects:Calor0410 |

**Not measured by T1.** X-INT-05 `analysis-fact-kill` and X-INT-06 `hole-binding` and
`hole-effects` are analysis cells, not conversion behavior. F1 and F2 tests cover them; the
register records them in `notMeasuredCells`. SDK, MSBuild and LSP have no conversion entry point.

**Regenerating.** `CALOR_REGENERATE_T1_CONTROLS=1 dotnet test tests/Calor.Compiler.Tests --filter
"FullyQualifiedName~SharedInteropControlsTests.Control_"` rewrites `controls-results.json`. The
register then fails T007 until its `results.sha256` is updated, and T009 until a register amendment
records the new `approvalSeal`. A changed
outcome is a reviewer decision, not a regeneration.

## Register validator (`T1RegisterValidator`)

| Code | Fails closed when |
|---|---|
| `T001` | The register does not consume the current R0 `scopeVersion` |
| `T002` | A family is missing, reordered or added; issue, readiness status or blockers differ from `scope.json` (READY is recorded only there); a non-READY family does not say what READY needs |
| `T003` | A registered case is removed, added, moved to another family or duplicated; its role or expected outcome differs; its `requirementSha256` is not the digest the scope's case yields |
| `T004` | A family has no evidence file, or one is missing, outside the family's own `v0.25-f<n>-<issue>/` directory, or not at its approved hash |
| `T005` | The counts do not cite F2's approved `rows.json`, or a before or after count is missing or differs from it |
| `T006` | A control is malformed or its fixture hash is not approved; a mandatory shared control or a registered X-INT row has no control with the scope's expected outcome and families |
| `T007` | `controls-results.json` is not at the approved hash; a control or cell is missing, or a committed outcome is not one the control allows |
| `T008` | The measured surfaces differ from the oracle's; a measured surface has no recorded defaults; an unmeasured one (SDK, MSBuild, LSP must be listed) has no reason |
| `T009` | `registerVersion` is not the latest amendment (1.0.0 with none); an amendment lacks a version, UTC date, PR or justification; the `approvalSeal` over every approved evidence, fixture and results hash differs from the latest amendment's (with none, the 1.0.0 seal, also pinned in `T1RegisterTests`); independence is not `reduced` |

**F-2/corpus counts** (F2-INTERP-09). F2's branch measured incomplete diagnostics 0 -> 0, in-repo expressions
bound 4,820 -> 4,827, conversion visits 35,159 -> 35,160, opaque boundaries 129 -> 137 and opaque expressions
6,944 -> 6,994. Amendment 1.0.3 (`D-F2-INTERP-09-RECANDIDATE`) re-registers 'after' to the values measured on the
0.25 candidate: 0, 4,828, 34,371, 148 and 7,746. `corpusCounts.reRegistration` keeps F2's values, cites the
re-proof's measurement (`v0.25-f2-906/ready/cases/F2-INTERP-09.json`) by hash, and attributes each change to the
merge that committed it to `bench/phase0-agent-native/binder-incomplete-baseline.json`:

| PR | Gate | Bound | Visits | Boundaries | Opaque |
|---|---|---|---|---|---|
| #1510 | F4 | +6 | −7 | +3 | +15 |
| #1554 | F5 | +1 | +24 | 0 | 0 |
| #1522 | F2 | +1 | +1 | +8 | +50 |
| #1558 | F1 | 0 | −816 | +7 | +733 |
| #1571 | F1 | 0 | +10 | +1 | +4 |

No other merge from the 0.25 base (`c020de74`, #1520) to the candidate changed the counts (F3 #1519 and #1531,
F6 #1513, W0 #1512 and the others). `T005` checks that 'after' equals the measurement, 'before' and the previous
'after' equal F2's `rows.json`, and before plus the deltas gives after.

## Aggregation

`python3 docs/plans/evidence/v0.25-t1-1428/reproduce/aggregate.py <sha> [--evidence-rev REV] [--out PATH]`
reads the scope, the register, every family evidence file and the control results **at a commit**
(`git show`): the candidate by default, or REV, a later commit that contains it (evidence measured
on a frozen candidate always lands after it). It reports, per family, the scope readiness, evidence hashes against the approved ones,
which commit the evidence was measured on, per-case READY result records and mapped-blocker
witness runs for that SHA, and the remaining gaps. A record counts only on its content: candidate,
case, current requirement digest, every registered cell and `passed` (a witness run: candidate,
repair, witness list, `passed`). It looks at every path a scope readiness record cites and every
evidence JSON file whose path or content names the SHA. It writes no READY record and refuses
an output path under `docs/plans/evidence/`. Run on this PR's head, every family reports evidence
measured on its own branch (not the candidate), no per-case result records and no witness runs.

## What READY still needs, per family

Every family (all are IN_PROGRESS):

1. A 0.25 candidate SHA frozen in the 0.24 C1 form, after W1 and the release-gap fixes, that
   passes the scope 1.3.0 `acceptancePreconditions` re-proof on that SHA.
2. For every registered case, including its interaction, doc-example and not-measured rows, a
   retained result record naming the candidate, the case, its current `requirementSha256` and
   `passed` for every cell (S007).
3. Test totals with `failed = 0` on the candidate; W1 website pages checked by W0 on the candidate;
   Codex review records; @juanmicrosoft's pull-request review URL as approver; `independence:
   "reduced"`; a resolution for every blocker, including #1423 (the 0.25 manifest) and #1426.
4. The T1 controls re-run on the candidate, with the register approving that results hash.

Family-specific:

| Family | Cases | Also needs |
|---|---|---|
| F1 #943 | 10 + X-INT-01, 05, 08 + DOC-F1-01 | #1427 at its current version; witness runs of R-OBL #1496 (`S2ObligationStateTests`) and R-OBL-RESIDUALS #1503 (`S2ObligationResidualTests`) on the candidate; per-case records for A1/A2 and X-INT-05's fact-kill cell |
| F2 #906 | 10 + X-INT-04, 06, 08 + DOC-F2-01 | witness runs of R-TEXT #1497 (`S2Z3TextEncodingTests`), #1496 and #1503; F2-INTERP-09 corpus counts re-measured on the candidate with submodules; X-INT-06 binder and effect cells |
| F3 #847 | 6 + DOC-F3-01, 02 | witness runs of #1496 and #1503; a per-case record for F3-LOCAL-A1 |
| F4 #1139 | 4 + X-INT-03 + DOC-F4-01 | F4-ITER-04 (FluentValidation modules with references) is still not measured: a measurement or an approved amendment |
| F5 #1132 | 6 + X-INT-02, 07 + DOC-F5-01 | witness runs of #1496 and #1503; a per-case record for the F5-ARRAY-06 boundary |
| F6 #1144 | 13 + DOC-F6-01, 02 | the per-surface matrix (F6-REPORT-04..07, 10, 12, 13) re-run on the candidate |

Case counts are from `register.json` (65 registered cases: 49 at 1.0.0 plus 16 rows from 1.1.0).

## Amendments

| Version | Date | PR | Change |
|---|---|---|---|
| 1.0.1 | 2026-10-18 | #1571 (F1 PR 2, #943) | F1's re-run `results.json` (five new rows for the W1 findings; D1-CS-04 and D1-CS-19 re-attributed from `in-parameter` to `ref-parameter`) changes F1's approved evidence hash `7efaf64a…` -> `8d64b186…`. Controls, fixtures, `controls-results.json` and the corpus counts are unchanged: `SharedInteropControlsTests` reproduces every committed cell, so nothing was regenerated. New approval seal `12a33773…`, pinned in `T1RegisterTests.AmendmentApprovalsMatchThePinnedSeals`; the top-level `approvalSeal` stays the 1.0.0 seal |
| 1.0.2 | 2026-10-19 | #1576 (pre-freeze decisions) | Consumes R0 scope 1.4.0 (T001). No approved hash changes, so the seal stays `12a33773…`. Under `D-READY-EVIDENCE-DIRS` the readiness mirrored here is written back after A1, with `scope.json`'s |
| 1.0.3 | 2026-10-20 | #1610 (Step A amendment) | Consumes R0 scope 1.5.0. `D-F2-INTERP-09-RECANDIDATE`: the corpus 'after' counts are re-registered to the 0.25 candidate, with `corpusCounts.reRegistration` (sealed with the counts). `D-F4-ITER-04-NOT-MEASURED`: F4's ready needs say the case is recorded as skipped. New approval seal `65330c86…` |
| 1.0.4 | 2026-10-22 | #1627 (post-A1 readiness write-back) | Consumes R0 scope 1.6.0. Under `D-READY-EVIDENCE-DIRS` each family's readiness mirrors `scope.json`'s READY and cites the family's A1 READY record (`readyRecord`, path and SHA-256, equal to the record #1430's terminal record cites); `readyNeeds` is empty. No approved hash changes, so the seal stays `65330c86…` |
