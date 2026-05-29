# Phase 3 RFC — Indentation-Delimited Blocks

**Status:** Draft RFC. Pre-implementation. Not pre-registered for a §10 gate yet — see [`phase-3-indentation-validation-plan.md`](phase-3-indentation-validation-plan.md) for the measurement plan that must run *before* any compiler surgery is undertaken.
**Author:** drafted during Phase 1 ship-out (PR #624) after the user proposal: *"I'm been thinking of implementing the approach python uses with tabs to track inner sections in code rather than open and closing brackets. Wondering if that is something that could be done on top of phase 1?"*
**Related:**
- Phase 1 (shipped): optional closing-tag IDs — [`docs/syntax-reference/structure-tags.md`](../syntax-reference/structure-tags.md).
- Phase 2 (rejected): compact stable identifiers — [`phase-2-measurement-results.md`](phase-2-measurement-results.md) on `feature/compact-ids-v6`.

> **Scope discipline:** This RFC proposes a syntax change. The implementation
> plan, the measurement protocol, and the decision rule live in the
> validation plan. If the validation pilot does not show a directional
> signal, this RFC is shelved and no compiler surgery happens.

---

## §1 — TL;DR

Replace the explicit closing tags `§/F`, `§/L`, `§/IF`, `§/CL` (and friends) with **indentation**. Blocks open exactly as today (`§F{Main:pub}`, `§L{i:1:100:1}`, etc.) and close when the dedent drops the cursor back to the opener's column. Inline forms (`§IF (cond) → §R expr`) are unchanged.

This composes with Phase 1: Phase 1 made closing tags optional in form (the closer can drop its ID). Phase 3 makes the closer optional in *existence*.

---

## §2 — Hypothesis

> **Indentation-delimited blocks reduce per-construct character count and
> tag-mismatch error noise without measurably increasing whitespace-related
> bug rates, and they make agent-authored Calor measurably more compact and
> readable to humans.**

The expected gains are:

1. **Fewer characters per block.** Each closer line (`§/F`, `§/L`, etc.) is ~3–6 chars + newline. A real module has 10–30 closers. Removing them is a ~5–10% char-count reduction on a typical file.
2. **Fewer mismatch-class diagnostics.** `Calor0101` (mismatched id) and the "missing close tag" family stop firing when there is no closing tag.
3. **Less visual noise / better readability.** Indentation already carries the structure for human readers; explicit closers are redundant cognitive surface.

The expected costs:

1. **Whitespace-sensitive parse errors** (mixed tab/space, inconsistent indent depth) become a new diagnostic class that did not exist before.
2. **Greppability loss.** Today `rg '§/M\{[a-z0-9_]+\}'` enumerates every module close. Post-Phase-3 the closer is whitespace, not a token.
3. **Refactoring tax.** Re-indenting a block becomes semantically meaningful, not cosmetic. Bulk edits (e.g. "wrap this block in a try/finally") must adjust every inner line's indentation.
4. **Round-trip fragility.** Editor auto-format must be indent-aware; pasting C# snippets into a Calor file requires re-indent rather than just retag.

§6 of this RFC enumerates these trade-offs at length. §3 of the validation
plan makes them quantitatively testable.

---

## §3 — Concrete syntax: FizzBuzz before/after

**Before** (today, [`samples/FizzBuzz/fizzbuzz.calr`](../../samples/FizzBuzz/fizzbuzz.calr), 14 lines):

```
§M{m001:FizzBuzz}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §L{for1:i:1:100:1}
    §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

**After Phase 1 only** (today, opt-in compact form, 13 lines):

```
§M{FizzBuzz}
  §F{Main:pub}
    §O{void}
    §E{cw}
    §L{i:1:100:1}
      §IF (== (% i 15) 0) → §P "FizzBuzz"
      §EI (== (% i 3) 0) → §P "Fizz"
      §EI (== (% i 5) 0) → §P "Buzz"
      §EL → §P i
      §/I
    §/L
  §/F
§/M
```

**After Phase 3** (proposed, 9 lines):

```
§M{FizzBuzz}
  §F{Main:pub}
    §O{void}
    §E{cw}
    §L{i:1:100:1}
      §IF (== (% i 15) 0) → §P "FizzBuzz"
      §EI (== (% i 3) 0) → §P "Fizz"
      §EI (== (% i 5) 0) → §P "Buzz"
      §EL → §P i
```

Note the `§/I` is also gone: the if/elseif/else chain closes when the `§EL` body dedents (or when the cursor reaches column < `§IF`'s column, whichever comes first). This is the *only* surprising rule — see §4.2.

---

## §4 — Detailed proposal

### 4.1 Lexer changes — `Parsing/Lexer.cs`

Add **INDENT** and **DEDENT** synthetic tokens, emitted by an indent-tracking
post-pass over the raw token stream (Python's approach: maintain a stack of
indent columns; when a line's leading whitespace column is greater than the
top, push and emit INDENT; less, pop and emit one DEDENT per popped level).

- Indent unit: **two spaces** (matches the existing samples).
- Tabs are **rejected with `Calor0099 MixedIndentation`** rather than
  implicitly expanded. This is the python-3 lesson learned the hard way.
- Blank lines and comment-only lines do not affect the indent stack.
- Inline forms (`§IF cond → expr`) emit no INDENT/DEDENT.

### 4.2 Parser changes — `Parsing/Parser.cs`

Each structural opener parser is rewritten from:

```
ConsumeOpener -> ParseBody -> ExpectCloser
```

to:

```
ConsumeOpener -> ExpectIndent -> ParseBody -> ExpectDedent
```

Two subtleties:

1. **If/elseif/else chain.** `§EL` is at the same column as `§IF`, not
   inside it. So the chain is: parse `§IF` body indented; on dedent, peek
   for `§EI` or `§EL` at the if's column; if found, consume and parse its
   body; loop until none found. The chain ends implicitly.
2. **Backward compatibility window.** During the transition (one minor
   version), the parser accepts *either* an explicit closer *or* a dedent.
   If both are present (`§/F` *plus* the dedent), the closer is silently
   consumed and ignored. This lets pre-Phase-3 files keep compiling while
   `calor fix --to-indent` is rolled out.

### 4.3 `Migration/CalorEmitter.cs` (writer)

When emitting a Calor source from an AST, the visitor now tracks an
indent level and writes `"\n" + indent` instead of `"\n" + indent + closer`
on block boundaries. This is the single most surgical change in the writer
path — the rest of the AST → Calor mapping is unchanged.

### 4.4 `Migration/RoslynSyntaxVisitor.cs` (C# → Calor)

The C# converter generates Calor AST then runs it through `CalorEmitter`.
So if 4.3 is done, the converter automatically produces indent-style
output. Verify by snapshot regression in `Calor.Conversion.Tests` (golden
files in `TestData/`).

### 4.5 `CodeGen/CSharpEmitter.cs` — **unchanged**

C# generation goes AST → C#. Indentation has no representation in the
AST, so the C# emitter does not need to change. This is the load-bearing
property that makes Phase 3 implementable: the indentation lives only in
the *source* surface, not in the AST.

### 4.6 `calor fix --to-indent`

A new migration subcommand under `calor fix` analogous to the Phase 1
`--drop-structural-ids`. Reads `.calr` files, deletes structural closers,
verifies parse-equivalence via re-parse + AST diff. Reversible via
`--revert --log <file>` exactly like Phase 1.

---

## §5 — Compatibility

### 5.1 With Phase 1

Phase 1 made the closer's *ID* optional. Phase 3 makes the closer itself
optional. They compose:

| Era       | Opener            | Closer            |
|-----------|-------------------|-------------------|
| pre-Phase-1 | `§F{f001:Main:pub}` | `§/F{f001}` (mandatory) |
| Phase 1     | `§F{Main:pub}`      | `§/F` (optional ID; closer still required) |
| Phase 3     | `§F{Main:pub}`      | *(dedent)* — closer optional |

A file written for Phase 1 still parses under Phase 3 because of §4.2.2's
transition window. A file written for Phase 3 will NOT parse under
Phase 1 (no migration backward).

### 5.2 With downstream tooling

- **MCP tools** (`calor_compile`, `calor_check`, etc.): unaffected; they
  speak the parser, which speaks indent.
- **Language Server** (`Calor.LanguageServer`): folding ranges, outline,
  goto-definition all derive from the AST; unaffected. Indent-aware
  *new line* formatting is a new affordance.
- **Round-trip harness** (`tools/Calor.RoundTrip.Harness`): the
  byte-preservation property is broken by definition (we are removing
  bytes). Replace with *AST-preservation* check — parse old, parse new,
  diff ASTs.

### 5.3 With existing `.calr` files in the wild

`calor fix --to-indent` handles the migration. The transition window in
§4.2.2 means there is no flag-day; files migrate at the user's pace.

---

## §6 — Trade-offs (honest)

### 6.1 Wins

| Win | Magnitude |
|-----|-----------|
| Char count per module | -5 to -10% on typical files (modeled on `samples/`) |
| `Calor0101` diagnostic frequency | -100% (the diagnostic class disappears) |
| "Missing close tag" agent error class | -100% |
| Visual structural density | qualitatively cleaner; matches Python intuition |

### 6.2 Losses

| Loss | Magnitude | Mitigation |
|------|-----------|------------|
| Greppability of block boundaries | High — `rg '§/M'` no longer enumerates modules | Use `ast-grep` or `calor structure --outline` instead |
| Whitespace-sensitive parse errors | New class (currently 0) | `Calor0099 MixedIndentation`; require 2-space indent; reject tabs hard |
| Refactoring tax (re-indent on wrap) | Medium — touched-line count goes up | Editor extension handles it; CLI `calor fix --reindent <range>` helper |
| Copy/paste from C# / Markdown | Medium — leading whitespace becomes meaningful | `calor format` already exists; extend to fix indent |
| AI-agent prompt drift | **Unknown** — agents may produce mixed-form output | Pre-flight this in the validation pilot (§2 of the validation plan) |
| Migration cost of existing samples | Low — `calor fix --to-indent` handles it byte-perfect | Run once; commit |

### 6.3 The asymmetric risk

Mismatched tag bugs are **loud** (Calor0101 fires at compile time).
Whitespace bugs can be **silent** (a single misindented line can rebind
to the wrong block scope and still parse). This is the canonical
Python-vs-Ruby objection.

Mitigations:
- 2-space-only, tabs-rejected lexer policy (§4.1).
- `calor format` always normalises indent on save.
- New diagnostic `Calor0099a SuspiciousIndentJump` flags any indent that
  jumps by more than one level — almost always a paste error.

---

## §7 — Open questions

1. **Tab handling.** Reject hard, or expand tabs as 2 spaces with a
   warning? Recommendation: reject hard. Easier to get right than to undo.
2. **Continuation lines.** A long `§B{name:type} (big-expr)` may span
   lines. Does the continuation indent column matter? Recommendation:
   require explicit continuation marker (`\` at EOL) and ignore indent
   on the continuation. This matches Python.
3. **Empty blocks.** `§F{Stub:pub}` with no body — what closes it?
   Recommendation: require an explicit `§PASS` line at the body indent,
   matching Python's `pass`.
4. **Multi-file behaviour.** Does each `.calr` file have its own indent
   stack, or do `§USE` boundaries reset it? Recommendation: per-file,
   trivially.
5. **Should `§/I` (if-block close) survive even with Phase 3?** It is
   load-bearing in the current grammar for distinguishing block-if from
   inline-if. Recommendation: kill it. The lexer can disambiguate from
   the presence of `→` on the opener.

These questions are answered in stone *only* after the validation pilot
returns directional signal. See [`phase-3-indentation-validation-plan.md`](phase-3-indentation-validation-plan.md).

---

## §8 — Explicitly out of scope

- **No CSharpEmitter changes.** AST → C# is unaffected.
- **No runtime changes.** `Calor.Runtime` is unaffected.
- **No verifier (Z3) changes.** Verification operates on the AST.
- **No new analyses.** Bug-pattern checkers, taint analysis, refinement
  types — all unaffected.
- **No `samples/` migration in the RFC PR.** Migration is a follow-up,
  scoped under `calor fix --to-indent`.
- **No commitment to ship.** This RFC's purpose is to clarify the
  proposal so the validation plan can be designed against it. If the
  pilot fails its decision rule (§5 of the validation plan), this RFC is
  shelved as `phase-3-indentation-rfc-rejected.md` with the empirical
  evidence captured.
