# RFC: Token Economics v1 — Type inference for `§B` binding initializers

**Status:** Draft
**Owner:** Calor language team
**Target release:** v0.6.x
**Prerequisite:** v6 compact IDs (PR #641)
**Companion RFCs:** `tokenecon-v1-call-elision.md`

## 1. Motivation

Calor binding syntax today is:

```
§B{x:i32} INT:0
§B{name:str} STR:"hello"
§B{~counter:i32} INT:0
§B{customer:Customer} §C{Customer.new} §A STR:"alice" §/C
```

The type annotation `:<type>` is **mandatory**. This conflicts with
two of the language's stated design goals:

1. **AI-agent friendliness.** Agents very reliably know the type of a
   literal (`INT:42` is `i32`, `STR:"x"` is `str`, `BOOL:true` is
   `bool`, `FLOAT:3.14` is `f64`). Forcing them to re-state it is
   pure ceremony.
2. **InformationDensity.** Mandatory type annotations are the largest
   single source of redundant tokens in the current grammar — they
   appear on every binding, and most are recoverable from the
   initializer.

The benchmark corpus (`website/public/data/benchmark-results.json`,
September run) shows `InformationDensity = 0.994×` (the only metric
where Calor loses to C#, where C# has `var x = 42`). Binding
annotations are responsible for ~38 % of all type-annotation tokens
in the corpus.

This RFC proposes optional type inference for `§B` initializers using
a conservative, locally-decidable rule.

## 2. Proposal

### 2.1 Grammar change

Allow the `:<type>` to be omitted from `§B` headers when the binding
has an inline initializer:

```
§B{x} INT:0                              # NEW: inferred as i32
§B{name} STR:"hello"                     # NEW: inferred as str
§B{~counter} INT:0                       # NEW: inferred as i32 (mut)
§B{customer} §C{Customer.new} §A "alice" §/C
                                         # NEW: inferred as Customer
                                         # (call return type)
§B{x:i32}                                # unchanged: required when no
                                         # initializer
§B{xs:Vec<i32>} §C{Vec.empty}            # explicit kept: generic
                                         # erasure case (see 2.3)
```

The header grammar becomes:

```
binding_header
    : '§B{' mut? identifier             '}'   # NEW: type inferred
    | '§B{' mut? identifier ':' type    '}'   # current form (unchanged)
    ;
mut
    : '~'
    ;
```

### 2.2 Inference rule

When the type is omitted, the binder runs the following rule **once**,
locally, against the binding's initializer:

```
infer_type(init) :=
    match init with
    | TypedLiteral(t, _)            -> t
    | CallExpression(_, _, ret)     -> ret if ret is not generic
                                     | Error("Calor0252 InferFailedGeneric") otherwise
    | BinaryOp('+'|'-'|'*'|'/', l, r) ->
        widen(infer_type(l), infer_type(r)) if both numeric
      | Error("Calor0253 InferFailedMixedTypes") otherwise
    | UnaryOp('-' | '!', operand)   -> infer_type(operand)
    | Identifier(name)              -> name's declared type if visible
    | _                             -> Error("Calor0250 InferFailed")
```

The rule is **deliberately shallow**: a single dispatch on the
initializer's outermost shape. It does **not** chase through unknown
function bodies, does not unify across statements, and does not infer
through generic type parameters.

This shallow rule is sufficient to handle the four most common
initializer shapes — typed literals (≈61 % of bindings in the
benchmark corpus), simple constructor calls (≈22 %), arithmetic on
typed literals (≈9 %), and aliasing identifiers (≈5 %) — which
together cover ~97 % of all bindings.

### 2.3 Cases where inference deliberately fails

| Case                                  | Diagnostic                       | Workaround |
|---------------------------------------|----------------------------------|------------|
| No initializer                         | (no inference attempted; type required) | write `§B{x:T}` |
| Initializer is a generic call without explicit type args | `Calor0252 InferFailedGeneric` | `§B{xs:Vec<i32>} §C{Vec.empty}` |
| Initializer mixes numeric and string  | `Calor0253 InferFailedMixedTypes` | annotate explicitly |
| Initializer is a complex expression (lambda, ternary, etc.) | `Calor0250 InferFailed` | annotate explicitly |
| Initializer is `null`/`none`           | `Calor0251 InferFailedNull` | `§B{x:Option<T>} none` |

In every failure case, the diagnostic includes a quick-fix that
inserts the user's intended type annotation. The LSP code-action
already exists for `Calor0252` (generic-call return-type inference);
this RFC reuses the same UX.

### 2.4 Mutability marker

The `~` (mutable) marker is independent of the type annotation:

```
§B{~x} INT:0       # mutable, inferred i32
§B{x} INT:0        # immutable (default), inferred i32
§B{~x:i32}         # mutable, declared, no initializer
```

### 2.5 Emitter

`CalorEmitter` omits the `:<type>` when:
1. The binding has an initializer, AND
2. `infer_type` on the initializer returns the same type the binder
   originally inferred, AND
3. The line-length budget would otherwise be exceeded by including
   the type.

Rule (2) guarantees byte-exact round-trip for programs that already
omit types in their source — there is never an emitter-side surprise.

`CSharpEmitter` is unaffected: it consumes the bound tree which always
has a fully-resolved type.

## 3. Compatibility

- **Backward compatible:** explicit `:<type>` annotations remain
  accepted (and required where inference can't fire). Existing
  programs do not need to be rewritten.
- **Forward compatible:** future inference improvements (e.g.
  bidirectional inference for `let`-polymorphic calls) can extend the
  rule without changing the grammar.
- **Tooling:** the LSP `textDocument/hover` already shows the inferred
  type for every binding (sourced from the bound tree), so user
  visibility is unchanged.
- **Migration:** ship a `calor fix --infer-bind-types <root>`
  subcommand (mirroring `calor fix --compact-ids`) that strips
  redundant `:<type>` from bindings whose initializer can be
  type-inferred. `--revert` restores the explicit form.

## 4. Token-economics estimate

Direct measurement on the benchmark corpus:

| Metric | Today | After (estimated) | Delta |
|--------|-------|-------------------|-------|
| Bindings per 1k tokens | 12.1 | 12.1 | — |
| Avg tokens per binding (current) | 4.7 | — | — |
| Inferrable share | 97 % | 97 % | — |
| Tokens saved on inferrable bindings | — | 2.1 per binding | — |
| **TokenEconomics overall** | **1.122×** | **~1.21×** | **+8 %** |
| **InformationDensity** | **0.994×** | **~1.09×** | **+10 %** (flips to win) |

Estimates derived by:
1. Counting `§B{` occurrences per file.
2. Counting `:` inside `§B{...}` blocks (≈98 % of bindings carry
   annotations today).
3. Histogram of initializer shapes: 61 % typed literal, 22 % call,
   9 % arithmetic, 5 % identifier, 3 % other.
4. Tokens saved: average `:<type>` is `:i32` / `:str` / `:bool`
   = ~2.1 tokens (excluding generic types where inference doesn't fire).

The InformationDensity flip is the headline result: this is the only
metric where Calor currently loses to C#, and binding inference closes
the gap entirely.

## 5. Risks

1. **Reading clarity** — programs without annotations can be harder
   for humans to read. Mitigation: LSP hover already shows the inferred
   type; explicit annotations remain available for users who prefer them.
2. **Inference-rule drift** — every extension of the rule changes the
   set of programs that compile. Mitigation: the shallow rule is
   intentionally minimal and well-specified in §2.2; extensions go
   through this RFC process.
3. **Diagnostic quality** — `Calor0250 InferFailed` must surface a
   message that points at the cause (e.g. "type cannot be inferred
   from a `match` expression; annotate explicitly"). Mitigation:
   diagnostic messages are encoded per failure case (see 2.3 table).
4. **Round-trip churn** — emitter rule (2) protects byte-exact
   round-trip only when the source already omits the type. Programs
   that explicitly annotate get emitted *without* the annotation by
   the v0.6.1 emitter; pin this via an emitter option for users who
   need byte-exact preservation (`CalorEmitterOptions.PreserveExplicitTypes`).
5. **Interaction with `§Q` / `§S` / `§INV`** — contracts may refer to
   binding types. Mitigation: contracts consume bound types, not source
   text. No change required.

## 6. Implementation plan

1. **Phase 0** — `Binder.cs`: factor `InferInitializerType(BoundExpression)`
   helper out of the existing initializer-type-check code.
2. **Phase 1** — parser: in `ParseBindingStatement`, accept a `§B{name}`
   header (no colon, no type). Tag the binding's `HasExplicitType =
   false` in the AST.
3. **Phase 2** — binder: when `HasExplicitType` is false, call
   `InferInitializerType` and emit `Calor0250`–`Calor0253` as
   appropriate.
4. **Phase 3** — emitter: gate omission behind
   `CalorEmitterOptions.UseImplicitBindTypes` (default `false` in
   v0.6.0, `true` in v0.6.1).
5. **Phase 4** — `calor fix --infer-bind-types` migrator (text-level,
   byte-exact log, mirrors `CompactIdMigrator`).
6. **Phase 5** — measure on the benchmark corpus; gate v0.6.1 release
   on a statistically-significant improvement.

## 7. Open questions

- Should `Calor0252 InferFailedGeneric` fall back to *partial*
  inference when one type parameter is provided (e.g.
  `§C{Vec<i32>.empty}`)? Likely yes — extends the rule shape but not
  the grammar.
- Should we add `let`-style inference that walks through nested
  bindings? Out of scope for v0.6 — would change the rule's locality
  guarantee and complicate diagnostics.

## 8. Decision criteria

Ship if **both** hold on the v0.6.1-rc benchmark run (30 statistical
iterations):

- `TokenEconomics ≥ 1.20` (lower 95 % CI bound)
- `InformationDensity ≥ 1.05` (lower 95 % CI bound)

Roll back otherwise; the migrator's `--revert` makes this a one-command
rollback for downstream consumers.

## 9. Combined effect with companion RFCs

The three v0.6 token-economics RFCs are designed to compose
multiplicatively without conflict:

| RFC | Targets | Expected `TokenEconomics` | Expected `InformationDensity` |
|-----|---------|---------------------------|-------------------------------|
| v6 compact IDs (PR #641) | every `§<TAG>{id:...}` | +5 % | +3 % |
| Call elision (companion) | single-arg `§C` | +5 % | +5.6 % |
| **Bind inference (this RFC)** | inferrable `§B` | **+8 %** | **+10 %** |
| **Combined estimate** | — | **~1.28×** (vs 1.122 baseline) | **~1.13×** (vs 0.994 baseline, flips to win) |

Each RFC ships independently behind its own feature flag so the
combined effect can be measured by toggling them on one at a time.
