# Persistent index + `calor query` v1 — scoping

Scopes the §2.2 deliverable that has now been deferred three times and is
pre-committed against a fourth. Gates 3 and 8 hang off it, as does gate 2's
index-contents leg and the review-packet integration.

This document fixes boundaries and names the risks. It does not design the
schema field-by-field; that lands with S1.

## 1. What v1 answers, and what it refuses to

**Facets in v1:** `symbol`, `callers`, `callees`, `impact`, `contract-outcomes`,
`assumptions`.

**Excluded, by the roadmap and now by evidence: `effects`.** §2.2 excludes it
because effect resolution string-guesses receiver types. #925 is the measured
form of that: a qualified `§C{Module.Function}` resolves as an *unknown external
call*, so the callee's declared effects never reach the answer, and two shipped
tests could not have failed if propagation were entirely broken. Effects join
the index in 0.15, derived from 0.14's typed signatures. **A release titled
"trustworthy" does not ship a facet the same roadmap declares unsound.**

## 2. The soundness posture, stated before anything is built

Calor binds **one file at a time**. Gate 4 established what that costs: a call
into another module resolves to nothing locally, so cross-file edges come from a
project-wide match on the bare callee name that requires exactly one candidate
and otherwise yields nothing. `ReviewPacketBuilder` and
`CompilationDriver.BuildCrossModuleFunctionMap` already drop ambiguous names the
same way.

That is a real limit, and v1's job is to **report it, not hide it**:

- Every answer carries a **residual block**: call sites that did not resolve,
  files that did not parse or bind, and names dropped for ambiguity — with
  counts, and the paths behind them on request.
- `callers` is therefore *"the callers we can name"*, never *"the callers"*. The
  CLI says so in its output, not only in documentation.
- A query whose residual would change the answer's meaning (e.g. `impact` where
  an unresolved edge could reach the changed declaration) is reported as
  **partial**, with the reason.

The alternative — emitting a clean-looking caller list with silent holes — is
the exact failure shape this repo has paid for repeatedly, most recently in
#924's ES-05 and in the first rename corpus.

## 3. Staleness is the primary risk, and it has a known shape

An index that answers confidently from stale state is #788/#883 in a new
component: an input changes, nothing invalidates, and the answer reflects the
previous world.

**Requirement:** the index's global invalidation inputs are *the same set*
`BuildStateCache` uses — compiler hash, options token, manifest hash, semantics
version, plus per-file content hashes — and the index records them in its own
header. A `calor query` against an index whose inputs no longer match **refuses
or rebuilds**; it never answers from the mismatch. This is checked by a
discriminating test (drop one input from the header, watch a query answer
change) before S2 ships.

Two facts from #924 constrain the design:

- Cache reuse today is **all-or-nothing per build**: any file-set change moves
  the cross-module map hash, and any options-token change invalidates globally.
- Only diagnostic-clean files are cached; cross-module diagnostics are
  recomputed from summaries every run.

**Decision: v1 rebuilds the index wholesale.** Incremental indexing is not in
v1. Gate 8's budget is 30s on the largest pinned subject, and wholesale is the
honest starting point; if the measured number fails, incrementality becomes a
priced follow-up rather than an assumed capability. This also keeps gate 2's
index-contents leg trivially satisfiable rather than satisfiable-by-accident.

## 4. Storage

- Location `obj/calor/` (alongside `.calor-build-state.json`), single file
  `.calor-index.json`.
- Header mirrors `BuildStateCache`: `FormatVersion`, `CompilerSemanticsVersion`,
  compiler/options/manifest hashes, per-file content hashes.
- Contents: declarations, occurrences, call edges, contract outcomes,
  assumptions, semantic hashes — canonically ordered, because gate 2 compares
  index contents byte-for-byte.
- Never committed. The dogfood guard's precedent applies: a CI assertion that no
  index artifact is tracked.

## 5. Reusing what exists

`ProjectSymbolIndex` (shipped in #927 for gate 4) is already the in-memory core:
it parses and binds a file set, maps `SymbolId` to occurrences, resolves
cross-file calls with the unique-match rule, and skips-and-reports files it
cannot read. **S1 persists that model rather than writing a second one.**

Known gaps it carries into this work, both already recorded:

- **Type references are not indexed** (`§B` bindings, `§NEW`, parameter types),
  which is why gate 4 refuses type renames. The index inherits this: a `symbol`
  query on a type reports declarations but not uses, and must say so.
- **Split declarations** (a module or type declared across files) carry one
  identity per file — #922. `symbol` must group them or label them; it may not
  silently pick one.

## 6. Gate design

**Gate 3 — index/query correctness.** A golden query-answer corpus, because
gate 2's identity alone would pass an identically-wrong index.

- Denominator: a purpose-built in-repo fixture project (deterministic,
  hand-reviewable ground truth) **plus** one pinned conversion subject for
  scale. Ground truth for the fixture is authored by hand and reviewed; for the
  subject, spot-audited with the audited fraction published.
- **Discrimination is part of the gate, not an afterthought:** the corpus must
  fail when a call edge is dropped, when a caller is attributed to the wrong
  overload, and when the residual is under-reported. Each injection is recorded
  in the corpus README, in the shape #924 and #927 now use.

**Gate 8 — performance envelope.** Index build ≤ 30s, warm `calor query`
≤ 500ms, measured in CI.

- **Denominator: FluentValidation** — 208 non-test `.cs` files, ~25.7k lines,
  the largest of the three pinned subjects (serilog 112/14.0k, MediatR 76/4.1k).
  Named here so "largest" is not re-decided later against a smaller subject.
- Numbers are published per release whether or not they pass.

**Gate 2's index-contents leg** rides along: the ES-01..ES-07 corpus and
`EditScriptIdentityTests` already exist, so the leg is an added assertion that
canonically-ordered index contents match between full and incremental runs — not
a new harness.

## 7. Slices

| slice | contents | gate touched |
|---|---|---|
| S1 | index model over `ProjectSymbolIndex`, persistence with invalidation parity, `calor index` build/refresh, no-tracked-artifact assertion | gate 2 index leg |
| S2 | `calor query symbol\|callers\|callees`, residual reporting, staleness refusal + its discriminating test, gate-3 fixture corpus | gate 3 (fixture) |
| S3 | `impact` facet; review-packet reads the index (§2.2) | gate 3 (extended) |
| S4 | `contract-outcomes`, `assumptions` facets | gate 3 (extended) |
| S5 | perf harness on FluentValidation, numbers published | gate 8 |

S1 and S2 are the ones that must land for the deferral to be honoured; S3–S5
complete it. **`effects` is not a slice.**

## 8. What this does not include

- Incremental index updates (§3, priced only if gate 8 fails).
- The effects facet (0.15, after typed signatures).
- Type-reference indexing (follow-up to #927; would lift the type-rename
  refusal and complete the `symbol` facet).
- Cross-repository or package-level indexing.
- Any query surface in the LSP or MCP — both are consumers, and §2.3 already
  sequences them after the spine.

## 9. Decisions and open questions

### 9.1 Semantic hash granularity — REVISED to per declaration

**First decision (2026-08-11): per file.** Coherent with the wholesale rebuild in
§3 and the cheaper build. Recorded with its cost stated — `impact` would answer
at file granularity — and with an S3 checkpoint to show a real answer before
calling the facet done.

**The checkpoint fired, and the decision was reversed (2026-08-12).** Measured on
the 106-file / 11k-line `fixture-10k` corpus (1,366 functions, 587 call edges,
zero residual), file granularity gave:

| | file-grained | declaration-grained |
|---|---|---|
| answer exactly right | **10/988 functions (1%)** | 988/988 (100%) |
| mean reported impact | 13.17 declarations | 1.04 |
| true impact empty, answer non-empty | **683/988 (69%)** | 0 |

For roughly two functions in three, the honest answer was "nothing is affected"
and the tool said otherwise — a ~13x over-report. That is not a blurry answer,
it is one that trains its reader to ignore it, while still looking like it
works.

**What this cost, and why the checkpoint was worth having:** the reversal was a
format revision, not a rewrite. `IndexedDeclaration` gained a `SemanticHash` over
the declaration's own definition text, `Files` was narrowed to its invalidation
role, and the format version went 1.0 → 2.0 — which the versioned header (§4)
existed to permit. The argument would not have been settled by discussion: the
small fixture made file granularity look tight (6 declarations), and only real
code exposed the 1%.

**Whole-file impact is retained** as `--file`, because "I rewrote this file" is a
real question. It is no longer how a change to one declaration is answered.

### 9.2 Still open, to settle in S1

1. Does `calor query` build the index on demand when absent, or fail with a
   pointer to `calor index`? (Recommendation: build on demand, with `--no-build`
   for CI timing runs, so gate 8 measures a warm query rather than a build.)
2. Is the fixture project for gate 3 a new corpus, or an existing one
   (`tests/TestData/EditScripts` is already registered and small)? Reusing it
   keeps one denominator; a purpose-built one gives better `impact` shapes.
