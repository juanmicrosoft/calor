# #1191: reconcile binder visits without claiming native source coverage

## Scope and decision

`Binder.ExpressionsBound` increments on **every `BindExpression` invocation**.
It counts generated references, repeated visits, and `RawCSharpExpressionNode`
interop. It is not the number of distinct, faithfully translated C# expressions.
Adding dummy nodes to restore its old lower bound would reward an implementation
artifact rather than preserve coverage.

Keep the exact baseline equality, incomplete-diagnostic ratchet, named parse
failures, file counts, conversion exception/empty-output/parse-failure counters,
and preserve-mode identity checks. The proposed amendment changes the visit count from **34,942 to 34,703**
(through the intermediate 34,734 measurement) only with the independent
source/opacity checks below. This is an explicit
measurement amendment, **not an improvement in native coverage**.

**Acceptance status: proposed coverage tradeoff, not independently approved.**
The 20 additional opaque boundaries / 180 source expressions require explicit
parent/user acceptance, or faithful native repair of those sites before adopting
the expanded opaque budget. #1191 permitting counted interop does not itself
approve a larger budget. The independent reviewer has not approved this budget
or the proposed number. Passing the technical guards does not constitute that
acceptance.

Any interpretation that the original 208-visit decline is **solely removed
artifacts is withdrawn**. That visit decline and the opacity increase are
separate measurements. No unchanged-native-fidelity claim is made.

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

**Initial review verification:** the complete `src/` tree of `000d2f38` was
installed in the isolated worktree and rebuilt. Every file's binder-attempt
count and selected-source classification matched the `2738d99b` attribution.
The intervening production changes only affect documentation self-checks.
The first binding-failure comparison below was independently measured again on
clean `639fa93a` and this exact candidate, without count instrumentation.
The subsequent `67d4c259` verification is recorded separately below.

## What changed

The initial section of `binder-expression-attribution.json` records every changed file, exact
`BindExpression`-visit deltas by runtime node type, generated-reference attempts,
new opaque boundaries, and source-expression identity transitions. Its 59 files
with changed visit totals sum to **-208**. Another eight files have
representation changes without a change in visit total.

Count-only probe instrumentation incremented a node-type dictionary immediately
after the existing `ExpressionsBound++` and separately counted `ReferenceNode.Name`.
The sum of type counts was checked against `ExpressionsBound` for every file on
both revisions. No nodes, bindings, or binding behavior were added. The
instrumentation is not a production change.

The following table is the **initial 639fa93a → 2738d99b/000d2f38 comparison**,
not the later eager-operand result:

| Instrument | Before | After | Delta |
|---|---:|---:|---:|
| Binder expression attempts | 34,942 | 34,734 | -208 |
| Generated-reference attempts¹ | 4,405 | 4,282 | -123 |
| Selected files parsed and bound | 364 | 364 | 0 |
| Incomplete diagnostics | 0 | 0 | 0 |
| Selected source `ExpressionSyntax` nodes | 88,158 | 88,158 | 0 |
| Exact non-opaque expression-span matches² | 32,276 | 32,212 | -64 |
| Exact matches containing opaque descendants (subset) | 0 | 25 | +25 |
| Source expressions covered by opaque spans | 3,149 | 3,329 | +180 |
| Source expressions without an exact/opaque mapping | 52,733 | 52,617 | -116 |
| Converter interop boundaries / `InteropPreserved` losses | 45 | 65 | +20 |
| Serialized raw expressions | 17 | 37 | +20 |
| `EmitterFallback` losses | 7 | 7 | 0 |
| Raw binder errors / files containing them | 5,085 / 258 | 4,964 / 254 | -121 / -4 |
| Propagated binder errors / files containing them | 117 / 40 | 109 / 38 | -8 / -2 |

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

The preliminary +130 opaque expressions (54 formerly exact / 76 unmapped) and
50 lost exact matches used the wrong coordinate space: original-file positions
instead of the selected source to which conversion AST spans refer. They are
superseded by **+180 (79 / 101)** and **72**, not separate coverage changes.

### Resolving the 72 lost exact matches

`binder-source-carrier-evidence.json` records each file, original selected-source
kind/span/text, and surviving AST carrier kind/span/emitted C#:

| Identities | Concrete resolution |
|---|---|
| 47 generic receiver names | The type-qualified receiver survives in a direct `CallExpressionNode` target; the separate `ReferenceNode` is gone. |
| 23 conditional expressions | The whole conditional expression survives with the same C# tokens, but its carrier's own source span is narrower than the full expression. |
| 1 conditional call in `ObjectDetails.cs` | The full call survives; predefined `string.Empty` is normalized to `""`. |
| 1 chain in `ServiceRegistrar.cs` | The receiver is captured once using an always-matching `is var` pattern. The predicate, `?.GetGenericArguments()`, and final `FirstOrDefault()` survive; the lambda's inferred `System.Type` parameter is explicit. |

This resolves the identities as representation/provenance changes rather than
missing operations; it is not a whole-program equivalence proof. Every corpus
run now asserts both the original source text and the surviving carrier's C#
tokens at its recorded kind/span. The carrier fixture is **not regenerated by**
`CALOR_UPDATE_BINDER_BASELINE`. A lost/changed carrier fails even if the aggregate
and source-count baselines are regenerated. A same-span carrier-token mutation
is an explicit negative control.

These 72 identities are separate from the 79 formerly exact identities that are
now opaque. Resolving the former does not approve the tradeoff for the latter.

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

## Final eager-operand follow-up: 67d4c259

The final production repair keeps eager binary operands inside their execution
regions during Calor serialization, rather than hoisting a right-hand operand
ahead of the left. Its complete `src/` tree was installed and rebuilt in
isolation before measurement.

This changes **34,734 → 34,703** visits across **19 files**. All 364 files retain
exactly the same selected-source classifications, converter AST kind counts,
converter opaque boundaries, and conversion losses as `000d2f38`. No additional
source expression is made opaque or loses its provenance mapping.

The reparsed Calor AST loses 32 reference nodes and 32 bindings, and gains one
field-access expression. For every changed file, the reference/field-access
delta equals the independently measured binder-visit delta: **-32 +1 = -31**.
`MessageTemplateRenderer.cs` additionally represents an `else if` directly
rather than as a nested `if`; that statement-shape change adds no expression
attempts. Parsed-node counts are not presented as independent instrumentation
of `BindExpression`. The JSON's `EagerOperandFollowup` names every file and
records both quantities.

Raw binding errors change **4,964 → 4,962**, still across 254 files. Propagated
errors remain **109 across 38 files**. The final comparison with release639fa93a
is therefore **34,942 → 34,703 visits**, **5,085 → 4,962 raw errors**, and
**117 → 109 propagated errors**. The source-identity ledger pins this final
candidate; the earlier measurements remain explicit historical records.

**Latest-candidate revalidation (`d08d7878`):** after the stack-allocation repair
and proof-fixture commits, the complete candidate `src/` tree was installed and
rebuilt in isolation. All eight focused
ratchet/control tests passed in ordinary mode, **without regeneration**.
Every per-file source/error identity and count remains unchanged: **34,703
visits**. The new D-S1.5 fixtures live under `bench/`, outside the registered
in-repo F-2 roots; they do not silently enlarge that denominator.

## New safeguards and limits

`binder-source-coverage.json` pins each file independently, including:

* Normalized original source hash and selected-source hash. Branch stripping
  removes text; conversion AST spans refer to the **selected** source. Comparing
  them to original-file offsets produces incorrect attributions.
* Binder attempts, source expression count, exact-expression source identities,
  opaque expression identities, unmapped identities, and opaque boundary
  identities. Exact matches enclosing opaque descendants are identified
  separately, not presented as fully native. Equal-count source swaps or a move
  from represented to opaque/unmapped still fail.
* Serialized opaque-code token hashes and multiplicities, including raw
  expressions introduced by the emitter rather than the converter AST.
  Preprocessor directives and disabled-branch text are included, not discarded
  with ordinary trivia.
* Structured conversion-loss identities and the converter's reported success.
* Raw binder-error counts and identities, separately from errors propagated by
  `BindingDiagnosticPolicy.PropagateCompilationErrors`. These diagnostic
  positions belong to reparsed **Calor output**; they are not C# source identities.

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
Likewise, “364 modules bound” means the binder returned for 364 modules, **not**
that they all bound successfully: 254 current files have raw binder errors, and
38 contain errors the shipping compiler propagates. Failure identities and
counts now participate in exact per-file equality; they cannot silently change
while the visit total stays constant.

Mutation controls detect deleted/changed/misattributed raw expressions,
equal-count source-identity swaps, a native-to-opaque substitution, mixed
native/opaque ancestry, and binding failures at unchanged attempt counts.
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
