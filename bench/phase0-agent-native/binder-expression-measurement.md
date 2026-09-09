# #1191: reconcile binder visits without claiming native source coverage

## Scope and decision

`Binder.ExpressionsBound` increments on **every `BindExpression` invocation**.
It counts generated references, repeated visits, and `RawCSharpExpressionNode`
interop. It is not the number of distinct, faithfully translated C# expressions.
Adding dummy nodes to restore its old lower bound would reward an implementation
artifact rather than preserve coverage.

Keep the exact baseline equality, incomplete-diagnostic ratchet, named parse
failures, file counts, conversion exception/empty-output/parse-failure counters,
and preserve-mode identity checks. Amend the visit count from **34,942 to 34,734**
only with the independent source/opacity checks below. This is an explicit
measurement amendment, **not an improvement in native coverage**.

The comparison used clean release commit `639fa93a` and #1191 commit `2738d99b`
in isolated worktrees. All 364 files were read from the same pinned corpus:

| Subject | Commit |
|---|---|
| MediatR | `fb309026775ef953a64fb5339d074426c1ad2c37` |
| serilog | `0597ddfbd4ec594d9c42edd745fe728a2198bad9` |
| FluentValidation | `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c` |

Both runs used C# Preview, regular source, documentation parsing, genuinely empty
preprocessor symbols, and `SelectActiveBranchLossy`. CRLF is normalized to LF.
The separately built baseline reproduces the earlier 34,942 probe log. The old
34,442 current-counts log is stale and is not used.

## What changed

`binder-expression-attribution.json` records every changed file, exact
`BindExpression`-visit deltas by runtime node type, generated-reference attempts,
new opaque boundaries, and source-expression identity transitions. Its 59 files
with changed visit totals sum to **-208**. Another eight files have
representation changes without a change in visit total.

Count-only probe instrumentation incremented a node-type dictionary immediately
after the existing `ExpressionsBound++` and separately counted `ReferenceNode.Name`.
The sum of type counts was checked against `ExpressionsBound` for every file on
both revisions. No nodes, bindings, or binding behavior were added. The
instrumentation is not a production change.

| Instrument | Before | After | Delta |
|---|---:|---:|---:|
| Binder expression attempts | 34,942 | 34,734 | -208 |
| Generated-reference attempts¹ | 4,405 | 4,282 | -123 |
| Selected files parsed and bound | 364 | 364 | 0 |
| Incomplete diagnostics | 0 | 0 | 0 |
| Selected source `ExpressionSyntax` nodes | 88,158 | 88,158 | 0 |
| Exact non-opaque expression-span matches² | 32,276 | 32,212 | -64 |
| Source expressions covered by opaque spans | 3,149 | 3,329 | +180 |
| Converter interop boundaries / `InteropPreserved` losses | 45 | 65 | +20 |
| Serialized raw expressions | 17 | 37 | +20 |
| `EmitterFallback` losses | 7 | 7 | 0 |

¹ A generated-reference name starts with `_` and is absent from all Roslyn source
identifier tokens in that file. The remaining reference-visit delta is -112;
non-reference visits change by +27. Thus `-123 -112 +27 = -208`.
The detailed type ledger includes **+19 visits to raw expressions**; one
serialized raw expression is outside the binder's expression traversal.

² An exact span match is provenance, not proof of native fidelity. Generic
callees can live in target strings; generated references can reuse source spans.
Of the old exact matches, 79 become opaque and 72 become unmapped. Those 72 are
47 `GenericName` and 25 `ConditionalAccessExpression` identities whose old
generated-reference/call-chain spans no longer have a standalone matching
expression node. Another 87 source identities gain an exact match.
An additional 101 previously unmapped source identities become opaque.
“Unmapped” does **not** mean proven omitted: type names, callee strings, and
lowering representations are not one-to-one AST expression nodes.

The 20 new opaque boundaries are **real coverage exclusions**, not deleted
temporaries. They preserve expression-local execution for declaration-bearing
`out`/pattern operands, chained invocation/receiver shapes, and conditional
initializer/coalescing expressions. They remain explicitly charged as interop:

| Corpus-relative file (subject prefix abbreviated) | New boundaries | Newly opaque source expressions |
|---|---:|---:|
| FV / `FluentValidation.Tests/StreetNumberComparer.cs` | 1 | 7 |
| FV / `FluentValidation/AbstractValidator.cs` | 1 | 9 |
| FV / `FluentValidation/TestHelper/ValidatorTestExtensions.cs` | 1 | 15 |
| FV / `FluentValidation/Validators/PolymorphicValidator.cs` | 1 | 10 |
| MediatR / `MediatR/Registration/ServiceRegistrar.cs` | 1 | 7 |
| serilog / `Serilog/Capturing/TrimConfiguration.cs` | 1 | 7 |
| serilog / `Serilog/Core/Logger.cs` | 2 | 12 |
| serilog / `Serilog/Core/Sinks/Batching/BatchingSink.cs` | 6 | 41 |
| serilog / `Serilog/Debugging/SelfLog.cs` | 1 | 8 |
| serilog / `Serilog/Filters/Matching.cs` | 1 | 9 |
| serilog / `Serilog/Parsing/PropertyToken.cs` | 1 | 13 |
| serilog / `Serilog/Settings/KeyValuePairs/KeyValuePairSettings.cs` | 3 | 42 |

FV means `FluentValidation/src`; the other prefixes likewise include `/src`.
The JSON contains full paths and individual selected-source UTF-16 spans.

Preserve-all mode previously ignored `RawCSharpExpressionNode`.
Including it changes the accounted opaque boundary total **91 → 103** and
source expressions **6,471 → 6,598**. Enclosing preserve-all blocks absorb some
expression boundaries, so this is not the selected-mode +20/+180.
Unmapped opaque spans, unconverted files, conversion exceptions, empty outputs,
and output parse failures remain zero.

## New safeguards and limits

`binder-source-coverage.json` pins each file independently, including:

* Normalized original source hash and selected-source hash. Branch stripping
  removes text; conversion AST spans refer to the **selected** source. Comparing
  them to original-file offsets produces incorrect attributions.
* Binder attempts, source expression count, exact-expression source identities,
  opaque expression identities, and opaque boundary identities. Equal-count
  source swaps or a move from represented to opaque/unmapped still fail.
* Serialized opaque-code token hashes and multiplicities, including raw
  expressions introduced by the emitter rather than the converter AST.
  Preprocessor directives and disabled-branch text are included, not discarded
  with ordinary trivia.
* Structured conversion-loss identities and the converter's reported success.

Additionally, every converter raw expression must token-match the expression at
its selected/original source span, as appropriate to the conversion mode. Extra
outer parentheses and trivia are permitted; changed tokens are not. Existing
opaque payloads must survive Calor serialization and reparsing with their token
sequence and multiplicity intact, in both conversion legs.

These checks are regression instruments, **not a whole-corpus semantic proof**.
The historical selected leg counted parse-and-bind completion, not
`ConversionResult.Success`: 274 baseline files and 272 current files report
conversion failure despite having parseable, bindable output. That fact is now
recorded per file rather than hidden behind “364 native conversions.”
The existing two `Dropped` and one `FallbackTodo` losses remain present and
unchanged. Do not describe this ledger as a zero-loss conversion benchmark.

Mutation controls detect deleted/changed/misattributed raw expressions,
equal-count source-identity swaps, and a native-to-opaque substitution.
Three native conditional controls reject **both** `InteropPreserved` and
`EmitterFallback` and require no raw nodes before or after serialization.

Regeneration remains explicit:

```sh
CALOR_UPDATE_BINDER_BASELINE=1 dotnet test tests/Calor.Compiler.Tests/ \
  --filter FullyQualifiedName~BinderIncompleteRatchet
dotnet test tests/Calor.Compiler.Tests/ \
  --filter FullyQualifiedName~BinderIncompleteRatchet
```

Review source-identity and opacity changes before accepting regenerated files.
Do not treat the update switch as permission to increase native-coverage claims.
The two diagnostic ledgers already amended in `5971541a` are not changed here.
