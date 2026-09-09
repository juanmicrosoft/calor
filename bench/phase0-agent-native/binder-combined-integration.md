# Combined migration measurement: accepted M1 to frozen M2-M5

This report compares accepted production `7d218c59` with exactly
`46baabf8dfbb32d512ecf3f8350454ecbe4bce59`. The latter is a local combined
candidate, not a published release. It contains the reviewed tuple, dictionary,
loop, and LINQ repairs. Later production changes require a new measurement.

**M1 remains accepted:** its additional 20 boundaries / 180 expressions were
explicitly authorized in [the parent decision](https://github.com/juanmicrosoft/calor/pull/1252#issuecomment-5603520220).
`binder-expression-attribution.json` is byte-for-byte unchanged. This report
does not reopen that decision or reuse its authorization for the new sites.

**Separate accepted decision:** the combined candidate adds **11 opaque
boundaries covering 268 source expressions**, across five files. Of these
expressions, 139 previously had an exact non-opaque AST span and 129 were
unmapped. After inspecting every source/payload pair and all four renamed
carrier definitions, the parent explicitly accepted this additional opacity in
[PR1252 comment5604729878](https://github.com/juanmicrosoft/calor/pull/1252#issuecomment-5604729878).
This decision is separate from the earlier M1 budget and from independent
implementation review.

## Counts and interpretation

| Instrument | Accepted | Combined | Delta |
|---|---:|---:|---:|
| Binder expression visits | 34,703 | 35,179 | +476 |
| Exact non-opaque source-span matches | 32,212 | 32,227 | +15 |
| Source expressions inside opaque boundaries | 3,329 | 3,597 | +268 |
| Unmapped source expressions | 52,617 | 52,334 | -283 |
| Selected converter opaque boundaries | 65 | 76 | +11 |
| Selected serialized opaque payloads, all raw kinds | 82 | 93 | +11 |
| Raw binding errors | 4,962 | 4,928 | -34 |
| Propagated binding errors | 109 | 109 | 0 |
| Preserve-all opaque boundaries | 103 | 114 | +11 |
| Preserve-all opaque source expressions | 6,598 | 6,866 | +268 |

All 364 original and selected source hashes are unchanged. All 364 selected
outputs parse and reach the binder. There are no new conversion exceptions,
empty outputs, parse failures, incomplete diagnostics, or removed old opaque
boundaries. The source transitions reconcile as 139 exact-to-opaque,
129 unmapped-to-opaque, and 154 unmapped-to-exact. **No exact-to-unmapped
transition occurs in this comparison.** Exact spans are attribution, not native
fidelity or a semantic-equivalence proof.

The visit total changes in 70 files; 71 files change at least one source-ledger
field. `binder-combined-integration.json` records all 364 files, their original
and selected hashes, visit totals, changed ledger fields, source identities,
loss additions/removals, serialized payload changes, and raw/propagated error
identity changes. Error spans refer to reparsed Calor, not original C#.

`InteropPreserved` losses rise from 65 to 76, exactly charging the 11 sites.
`EmitterFallback` stays at seven, `Dropped` at two, `FallbackTodo` at one, and
`PreprocessorStripped` at 390. Conversion still reports failure for 272 files;
254 files still have raw binding errors, and 38 have propagated errors.
All 34 fewer raw errors are `Calor0200`, in the four files with newly opaque
LINQ/dictionary expressions. This decline is **not evidence of improved native
binding**: opaque regions hide operations from ordinary binding analysis.

## Exact new sites

Offsets are half-open UTF-16 positions in LF-normalized **selected C# source**.
FV abbreviates `FluentValidation/src/`; Serilog abbreviates `serilog/src/Serilog/`.
Full paths, source snippets, payloads, token hashes, individual expression
identities, and the matching structured loss are in `NewlyOpaqueSites`.

| File | Selected span | Feature | Expressions | Formerly exact / unmapped |
|---|---|---|---:|---:|
| FV `FluentValidation.Tests/LanguageManagerTests.cs` | 7899..8256 | LINQ query | 50 | 31 / 19 |
| FV `FluentValidation/AssemblyScanner.cs` | 2967..3358 | LINQ query | 41 | 24 / 17 |
| FV `FluentValidation/ValidatorDescriptor.cs` | 1942..2059 | LINQ query | 11 | 7 / 4 |
| FV `FluentValidation/ValidatorDescriptor.cs` | 2687..2712 | Deferred query predicate | 5 | 4 / 1 |
| FV `FluentValidation/ValidatorDescriptor.cs` | 2723..2727 | Deferred query selector | 1 | 0 / 1 |
| FV `FluentValidation/ValidatorDescriptor.cs` | 3393..3525 | LINQ grouping | 13 | 8 / 5 |
| Serilog `Capturing/PropertyBinder.cs` | 4347..4350 | `for` increment | 2 | 0 / 2 |
| Serilog `Settings/KeyValuePairs/KeyValuePairSettings.cs` | 2432..2750 | Dictionary initializer | 27 | 6 / 21 |
| Serilog `Settings/KeyValuePairs/KeyValuePairSettings.cs` | 2854..3205 | Dictionary initializer | 42 | 16 / 26 |
| Serilog `Settings/KeyValuePairs/KeyValuePairSettings.cs` | 6074..6748 | LINQ query | 47 | 28 / 19 |
| Serilog `Settings/KeyValuePairs/KeyValuePairSettings.cs` | 7935..8463 | LINQ query | 29 | 15 / 14 |

Eight LINQ boundaries cover 197 expressions, two dictionary boundaries cover
69, and the loop increment covers two. All ten raw-expression payloads exactly
equal their normalized source slices. The statement payload for `++i` adds only
the statement terminator: `++i;`. Every payload survives serialization with the
same tokens. Preserve-all attribution uses its own original-source coordinates;
its added sites and payloads are recorded separately per file.

## Generated-node population is not the visit denominator

The retained read-only probe also counts distinct **reparsed AST objects**.
Those populations are deliberately not relabeled as `BindExpression` visits:
the binder need not visit every object, and may revisit objects.

| Parsed population | Accepted | Combined | Delta |
|---|---:|---:|---:|
| Expression objects | 35,476 | 35,937 | +461 |
| Reference objects | 14,517 | 14,648 | +131 |
| Generated-reference objects (heuristic) | 4,261 | 4,325 | +64 |

A generated-reference name starts with `_` and is absent from that file's
original Roslyn identifier tokens. Per-file generated names and their
multiplicities are in the data. Other reference objects rise by 67.

The parsed population includes 39 fewer `ForStatementNode`s and 39 more
`WhileStatementNode`s and `BreakStatementNode`s; 156 additional `IfStatementNode`s,
195 boolean literals, and 48 bindings accompany explicit loop control.
Inline lambda-call parsing contributes to the 231 fewer call statements and
200 more call expressions. Raw expression objects rise by ten and raw statement
objects by one. The complete per-kind population delta is recorded.

The +461 distinct-expression delta is **not an exact decomposition of the +476
visit delta**. No production instrumentation, generated dummy nodes, or
asserted one-to-one correspondence was used. The direct per-file visit sums
reconcile exactly; population counts explain representation changes without
claiming faithful source coverage.

## Four carrier renames, with the 72 guards still active

The first unchanged-baseline run failed the non-regenerated carrier guard:
`AbstractValidator.cs` still emitted the same generic call but referenced a
different generated lambda name. The four changes are:

| Source span | Old generated name | New generated name |
|---|---|---|
| 8602..8628 | `_lam034` | `_lam039` |
| 9283..9318 | `_lam037` | `_lam042` |
| 14551..14565 | `_lam068` | `_lam073` |
| 14916..14930 | `_lam071` | `_lam076` |

Each call retains its kind, source span, target, and arguments except that one
name. Each renamed binding retains its original source span and complete
emitted lambda tokens, `() => RuleLevelCascadeMode`. `CarrierChanges` records
both definitions, both calls, and the original source. The four concrete
carrier-code pins were updated; the other 68 and all 72 original identities
remain unchanged. No wildcard name matching or regeneration bypass was added.

## F-2 and guard outcome

In-repository F-2 keeps 517 parsed files, the same nine named parse failures,
and zero incomplete diagnostics. Visits change from 4,790 to 4,794:

| Snapshot under `tests/Calor.Conversion.Tests/Snapshots/` | Visits | Explanation |
|---|---|---|
| `03-02.approved.calr` | 11 to 21 | Updated explicit-loop lowering |
| `13-03.approved.calr` | 10 to 11 | Unchanged source; inline lambda call now parses as an expression |
| `13-08.approved.calr` | 19 to 12 | Updated dictionary initializer preserved as expression interop |

Eight focused ratchet cases pass in regeneration mode, then the same eight pass
in ordinary mode with the update variable absent. The 72 carrier checks run
before the regeneration return. Both-mode payload/multiplicity guards,
original/selected source identities, raw/propagated error pins, unmapped/mixed
identities, native controls rejecting interop and emitter fallback, and the
preprocessing-offset regression remain active.

The report does not claim whole-corpus semantic equivalence or zero losses.
Companion diagnostic ledgers, their index and
documentation, production sources, and the feature manifest are outside this
measurement change. The parent decision for the 11 new sites is recorded above;
final independent review remains a separate gate.
