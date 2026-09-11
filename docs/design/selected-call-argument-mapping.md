# Selected method-argument mapping (#1382)

This is a bounded N3 implementation under #1082, not Stage A/B activation.
Copilot/GPT-6 Astra context `e9c6935e-da28-4a3b-94d4-0a5bbb596332` accepted the
slot after E1 adjudication, from `be488238d3aac374995174aa47526c7f3d09fd51`.
The provisional checkpoint/review target2026-09-12 is not a human staffing,
delivery or release promise. Two non-author final-head contexts and parent
adjudication are required; separate AI contexts are not human or statistically
independent review.

## Data flow

`Scope.TryMatch` retains the **winning existing map** and its effective parameter
types after generic substitutions. `OverloadResolutionResult.Matches` carries
each selected function identity plus source argument index, formal parameter
index, effective target type and normal/expanded params flag. Existing overload
scoring, applicability, named-argument rules and resolution failures are unchanged.
Conditional-compilation alternatives retain their separate selected maps.

`MetadataBinder` carries supplied names and supported `ref`/`out`/`in` modifiers
into its existing Roslyn probe. Both metadata probe entry points share argument
syntax and correctly declared by-reference locals. `IInvocationOperation` supplies
the actual selected formal parameter, not a declaration-order zip. An implicit
default argument is omitted from the supplied map. An expanded array or supported
`Span<T>`/`ReadOnlySpan<T>` params collection maps each supplied element separately.
Normal and named-array arguments retain the whole formal array target.

Roslyn can report a supplied cast argument as `IsImplicit=true`; that flag does
not mean the source argument was omitted. The implementation uses `ArgumentKind`
and correlation to the actual synthetic argument syntax instead. This defect was
observed and corrected during implementation, with failed runs retained.

Both Binder call forms invoke `ValidateCallArguments`. That method consumes the
selected mappings without changing the executable argument lists, names, modifiers
or their order. BCL formal parameter types, rather than source-order argument types,
are retained as the overload-sensitive signature. `out` is not an ordinary input.
Repeated identical findings across conditional alternatives are deduplicated.
Failed, ambiguous and inaccessible native selections do not acquire diagnostics
from a rejected overload or a metadata fallback.

The single argument emitter calls `NullabilityChecker.IsPossiblyNullAssignedTo`
and `SemanticsVersion.NullabilitySeverityFor`. It reports the actual supplied span
and formal name, with explicit expanded-element wording and structured
`MethodArgument`/receiving-shape provenance. Nullable0272/0273/0274 remain
**AnalysisOnly for every shape**. API acceptance is separate from an editor
`calor (analysis only)` Error; neither absence nor filtered output proves safety.
The source-site catalog changes from33 to32 routes at this base because two
argument emitters become one. The catalog's18 codes and disposition rules do not
change. #1397's independently developed275 must be reconciled if it merges.

## Executable controls

The76 new compiler cases and2 editor cases use existing runners and maintenance
files; no benchmark framework, product C# path exemption or test tool was added.

| Control | Evidence |
|---|---|
| Native reordered nullable/non-null inputs | Real Calor source, both forms, safe and unsafe arrangements; selected function and exact supplied span |
| BCL reordered names | Real `Path.Combine`, both forms; actual formal name, source span, API/editor distinction |
| Native params | Converted source and actual binding; empty, normal array, named array and expanded mappings; later nullable nominal element |
| BCL params | Actual .NET10 `Path.Combine` resolution and source binding; empty, explicit/named `GetCommandLineArgs()` array, five expanded strings with a nullable last input |
| Omitted optionals | Native converted caller and real `File.ReadAllTextAsync`; defaults are not supplied inputs |
| Modifiers | Converted native `out`/`ref`, both forms, production compilation; real named/reordered `Int32.TryParse` metadata |
| Rejected selections | Both forms retain active0207/0208; metadata rejects unknown/duplicate names and selects the named object `Concat` overload rather than its string competitor |
| Evaluation order | Original C# → Calor → public compilation → emitted assembly execution; four mixed/reordered statement/expression cases retain trace12 |
| Existing behavior | Native overload, metadata shape/context, nullability predicate and routing/source-catalog suites |
| Exact taint identities | Eight real binding/taint cases plus four `--analyze` CLI cases preserve `System.IO.File` sink recognition for tainted inputs and reject false alarms on constants; nullable reference syntax is not part of the overload identity |
| Named taint roles | Twelve statement/expression and direct/native-wrapper cases distinguish a tainted path from a tainted encoding; both inner BCL and outer native named calls retain formal roles |
| Native summary application | Four real-source return-flow cases and four actual `Scope.ResolveOverload` conditional-alternative controls; each selected function's map, not the first alternative's map, drives sink and return substitution |

The initial117-case selection passed. Six new source controls then reproduced
five failures before implementation: missing statement checks, wrongly accused
safe native input, and wrong named formal attribution. Full failure output/TRX
remains in the worktree's `.n3-evidence`; successful compiler selections reached
227 cases before the last eight matrix cases were added.

The first CI run found an additional required product-ratchet update:
[the per-file audit](../plans/evidence/n3-mapping-2026-09-11/corpus-ratchet-audit.json)
accounts for exactly four new statement274 findings in Serilog's
`JsonValueFormatter.cs` (52→56 raw binding Errors, zero propagated before/after).
Removing exactly those four identities reproduces the prior52-error SHA256.
All other fields for all364 source files are unchanged:35179 binder visits,
zero incomplete diagnostics, unchanged source/representation/opacity identities
and unchanged conversion outcomes. The existing regeneration command updated
only the two diagnostic fields; the normal ratchet then passed without update
mode. No skip, comparison, denominator or opaque budget was changed. This is the
product compiler coverage ratchet, not paused research collection. One newly
visible finding is the already-owned #1398 coalesce-transfer limitation; none
of the four is classified as an accepted compatibility break or safe result.

A final integration reviewer found a separate regression in the new BCL signature
field: Roslyn's default `string` spelling no longer matched exact taint rules
expecting `STRING` or `System.String`. Typed source bindings exposed the regression
that an earlier inferred-spelling probe did not. The correction uses qualified
CLR type identities without reference-nullability display syntax; it changes
neither taint policy nor the annotated types used by nullability checking.
The before-fix selection failed eight of ten cases, including two actual CLI
missed findings. The correction adds nullable-formal `WriteAllText` identity
controls as well. Source errors/coverage remain pinned by the normal corpus
ratchet, and fresh final-head review and CI are required after this material fix.

Follow-up falsification found a related consumer defect: after recognizing the
correct signature, taint rules still indexed source-order arguments as formal
positions. A reordered `File.ReadAllText(encoding: user_input, path: "safe.txt")`
incorrectly flagged the encoding as a path. Bound calls now retain the actual
metadata argument-to-formal indices, and the existing sink selector and summary
builder consume those indices. The original CLI false positive and its
correction are retained, including the separate unchanged0200 TypeChecker warning
for the externally resolved encoding type. Taint rules themselves are unchanged.

A fresh integration review of `d6ac62` found that the outer native wrapper still
applied summary formal indices directly to source-order arguments. The previous
test matrix reordered the inner BCL call but left the outer wrapper positional;
it did not cover this counterexample. The actual CLI false positive was retained
before repair. Both bound call forms now retain `SelectedOverloadMatches` directly
from native resolution. Summary sinks and return flows consume the map belonging
to the specific selected function, including conditional alternatives with
different formal orders. Direct configured sinks use the same selector. Mapping
selects supplied arguments in source order; omitted arguments are not synthesized.
Legacy/unresolved calls with no retained map keep their prior positional fallback.
The correction adds twelve cases and does not change taint rules. Earlier
no-finding reviews do not waive this later finding; fresh final-head reviews and
CI are required again.

## Explicit limits and acceptance gap

Mapping a normal array is **not** the same as checking an expanded element.
The selected metadata map preserves array-container and element annotations,
and the existing element predicate consumes the bounded array target. However:

* `NullabilityChecker`'s existing array branch checks element annotations, not
  nullable-container assignment.
* Declared native array reads still use the pre-existing nominal representation.
  Mapping does not repair lost array-element/container source annotations.
* BCL type-resolution enrichment retains its existing supported receiver boundary
  and unknown-type fallback. It is not proof of complete semantic resolution.
  Arbitrary custom params collections are not newly supported.

Therefore normal-array mapping/safe controls do **not** establish rejection of
every nullable array-container or nullable-element source. That precise gap needs
a separately bounded annotation/predicate repair and production negatives before
Stage B ([bounded proposed split #1444](https://github.com/juanmicrosoft/calor/issues/1444),
coordinated with #1380/#1381/#1402); it must not be waived or labeled green. If #1382's
container wording requires that repair here, full issue closure is blocked until
the parent adjudicates the split or accepts the additional scope. This document
does not silently weaken the acceptance criteria.

A runtime-order fixture using a nested nullable reference initially hit the
existing Option/type-applicability failure. Its failure is retained; the executable
order control uses non-null values instead. Nullable named-argument behavior is
covered separately with real supported native source and BCL/editor controls.
Native scalar STRING applicability remains #1383; typing/representation remains
#1397, and safe-consumer transfer remains #1398.

No constructor-input enforcement, new ref/out syntax, blanket activation,
suppression, proof-demotion lift, runtime-guard change, converter-inserted default
or throw, release, or paused research work is included.
