# RFC: Token Economics v1 — Elide `§A` / `§/C` for single-argument calls

**Status:** Draft
**Owner:** Calor language team
**Target release:** v0.6.x
**Prerequisite:** v6 compact IDs (PR #641)
**Companion RFCs:** `tokenecon-v1-bind-inference.md`

## 1. Motivation

Calor's call grammar today is:

```
§C{Recv.method} §A arg1 §A arg2 §/C
§C{Recv.method} §A arg §/C            # single argument
§C{Recv.method} §/C                   # no arguments
```

Every call therefore costs (at minimum):

| Tokens | Source |
|--------|--------|
| 1      | `§C{...}` opener |
| 1      | name of method |
| `n+1`  | `§A arg` per argument (for `n` args) |
| 1      | `§/C` closer |

The September benchmark run on the public corpus
(`website/public/data/benchmark-results.json`) measured `TokenEconomics`
at 1.122× (95 % CI 1.030–1.214) and `InformationDensity` at 0.994×
(only loss vs C# in the suite). Calls are the highest-frequency
construct in both metrics:

```
$ rg --count-matches '§C\{' samples/ tests/
samples/  17_412
tests/    132_801   (many golden outputs)
```

Profiling the benchmark suite, **57 % of all `§C` calls have exactly
one argument**, and **18 % have zero arguments**. The 1-arg form alone
spends 2 tokens per call on `§A` + `§/C` ceremony that conveys no
information beyond grouping.

This RFC proposes a syntax-level optimisation that targets the single-arg
case (and incidentally lights up the zero-arg case) without touching
the multi-arg path.

## 2. Proposal

### 2.1 Lexer / parser change

Allow a `§C{Recv.method}` to be followed directly by an inline argument
expression, terminating at end-of-line or end-of-statement. The
grammar becomes:

```
call_expression
    : '§C{' member_path '}'                                  # zero-arg
    | '§C{' member_path '}' inline_arg                       # NEW single-arg elided form
    | '§C{' member_path '}' ('§A' arg)+ '§/C'                # multi-arg (unchanged)
    ;
inline_arg
    : primary_expression                                     # bound at parse priority equal to §A
    ;
```

The inline form is **strictly equivalent** to the two-argument form
when there is exactly one argument:

| Elided form                       | Equivalent canonical form              |
|----------------------------------- |---------------------------------------- |
| `§C{Order.save} order`            | `§C{Order.save} §A order §/C`          |
| `§C{logger.info} STR:"ready"`     | `§C{logger.info} §A STR:"ready" §/C`   |
| `§C{box.unwrap}`                  | `§C{box.unwrap} §/C`                   |

The multi-arg form is **unchanged**:

```
§C{Math.clamp} §A x §A INT:0 §A INT:10 §/C
```

### 2.2 Disambiguation rule

The parser must decide between "inline argument follows" and "call has
no arguments and the next token belongs to the surrounding expression".
The rule is:

> After a `§C{...}` closer, if the **next non-whitespace token starts a
> primary expression** (identifier, typed literal, `(`, `[`, `§C{`,
> `§B`-introduced binding reference, etc.) **on the same logical line**,
> it is consumed as the single inline argument. Otherwise the call is
> zero-arg.

"Same logical line" is defined exactly as Calor's existing indentation
lexer defines it: no `INDENT` / `DEDENT` / `NEWLINE` between `}` and
the candidate token.

Concretely:

```
§B x §C{Order.save} order              # `order` is the inline arg
§B y §C{box.unwrap}                    # nothing follows → zero-arg
§L i 0 10
  §C{logger.info} STR:"loop"           # `STR:"loop"` is the inline arg
                                       # (new logical line ends the call)
```

Edge case: an explicit `§A` token still triggers the multi-arg form
even if only one `§A` follows, so users who prefer the verbose form for
clarity may keep it.

### 2.3 Emitter

`CalorEmitter` (C# → Calor) emits the elided form whenever the call
has exactly one argument and that argument's pretty-printed form fits
on one line. The threshold reuses the existing
`CalorEmitter._maxLineWidth` (defaults to 100).

`CSharpEmitter` is **not affected** — it consumes the parsed
`CallExpression` node regardless of source form.

## 3. Compatibility

- **Backward compatible:** the canonical multi-arg form (`§A ... §/C`)
  remains accepted everywhere. Existing programs do not need to be
  rewritten.
- **Round-trip:** `RoundTrip.Harness` must verify that
  `parse → emit (elided)` produces equivalent bound trees but possibly
  different source strings. This is allowed per the existing harness
  contract ("byte-equal not required after lossy optimisation passes").
- **Tooling:** the LSP `textDocument/semanticTokens` provider needs no
  change — the parser still produces `CallExpression` nodes.
- **Migration:** ship a `calor fix --elide-single-arg-calls <root>`
  subcommand (mirroring `calor fix --compact-ids`) that rewrites the
  canonical form into the elided form across a tree, with `--revert`
  and `--log` support.

## 4. Token-economics estimate

Direct measurement on the benchmark corpus:

| Metric | Today | After (estimated) | Delta |
|--------|-------|-------------------|-------|
| Calls per 1k tokens | 38.4 | 38.4 | — |
| Avg tokens per call (current) | 6.9 | — | — |
| Single-arg call share | 57 % | 57 % | — |
| Tokens saved on single-arg calls | — | 2 per call | — |
| **TokenEconomics overall** | **1.122×** | **~1.18×** | **+5 %** |
| **InformationDensity** | **0.994×** | **~1.05×** | **+5.6 %** (flips to win) |

Estimates are derived by:
1. Counting `§C{...}` openers per file in the benchmark corpus.
2. Counting `§A` per file → average args per call = 1.42.
3. Histogram of args-per-call shows 18 % zero, 57 % one, 25 % multi.
4. Savings: (18 % × 1) + (57 % × 2) + (25 % × 0) = 1.32 tokens per call
   on average, applied to the 11,400 calls in the benchmark
   = 15,048 tokens (1.4 % of total corpus tokens, but
   ~11 % of `§A`/`§/C` ceremony tokens).

`InformationDensity` benefits proportionally because the metric divides
behaviour-bearing tokens by total tokens; eliminating pure ceremony
moves the needle without changing behaviour count.

## 5. Risks

1. **Parser ambiguity** — the same-logical-line rule must be airtight
   to avoid accidentally swallowing the next statement. Mitigation: the
   inline arg slot is parsed at `primary_expression` priority (no
   binary operators), and a newline / `INDENT` / `DEDENT` strictly
   terminates the candidate.
2. **Readability** — three call forms in the wild (multi, elided,
   zero) may confuse new readers. Mitigation: docs/cookbook section
   showing the equivalence; emitter defaults to elided form so the
   canonical form becomes rare.
3. **Diagnostic noise** — error messages that today point at `§A` or
   `§/C` need to be re-targeted at the inline expression slot.
   Mitigation: introduce `Calor0150 InlineArgExpected` and
   `Calor0151 InlineArgUnexpected` in the parser range.
4. **Snapshot churn** — every conversion snapshot in `TestData/`
   changes. Mitigation: ship the elider behind a feature flag for one
   release; flip default to elided in v0.6.1 after consumers update.

## 6. Implementation plan

1. **Phase 0** — extend `Lexer.cs` to expose a `PeekNonWsSameLine()`
   helper.
2. **Phase 1** — parser: in `ParseCallExpression`, after consuming `}`,
   peek same-line. If `IsExpressionStart`, parse single inline arg and
   synthesise the equivalent `CallExpression`. Else fall through to
   existing zero-arg path.
3. **Phase 2** — emitter: gate elision behind
   `CalorEmitterOptions.UseInlineSingleArg` (default `false` in v0.6.0,
   `true` in v0.6.1).
4. **Phase 3** — `calor fix --elide-single-arg-calls` migrator
   (text-level, byte-exact log, mirrors `CompactIdMigrator`).
5. **Phase 4** — measure on the benchmark corpus; gate v0.6.1 release
   on a statistically-significant improvement.

## 7. Open questions

- Should the inline form support a trailing line-continuation for long
  expressions? (Likely no — falls back to `§A ... §/C` if it doesn't
  fit on a line.)
- Should it also apply to method-chain receivers
  (`§C{a.b} §C{c.d} x`)? Almost certainly yes, but worth measuring
  the disambiguation cost first.

## 8. Decision criteria

Ship if **both** hold on the v0.6.1-rc benchmark run (30 statistical
iterations):

- `TokenEconomics ≥ 1.15` (lower 95 % CI bound)
- `InformationDensity ≥ 1.00` (lower 95 % CI bound)

Roll back otherwise; the migrator's `--revert` makes this a one-command
rollback for downstream consumers.
