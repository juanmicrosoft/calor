---
layout: default
title: self-check
parent: CLI Reference
nav_order: 16
permalink: /cli/self-check/
---

# calor self-check docs

Machine-checks agent-facing documentation against the compiler
implementation and exits nonzero when they contradict each other
("doc drift"). This is **drift detection, not single-sourcing**: docs are
still written by hand, and prose or behavioral inaccuracies that don't
take one of the checked forms below will not be caught.

```bash
calor self-check docs                 # check the enclosing repository
calor self-check docs --root /path    # explicit repository root
calor self-check docs --format json   # unified diagnostic schema on stdout
calor self-check docs --format sarif  # SARIF 2.1.0 on stdout
```

Exit codes: `0` no drift, `1` drift findings, `2` no repository root found.
Findings use the `Calor1320`–`Calor1334` band (see
[Structured Output](/calor/cli/structured-output/)).

## Covered files

- `CLAUDE.md`
- `.github/copilot-instructions.md`
- every `docs/syntax-reference/*.md`
- every `docs/cli/*.md`
- every undated, top-level `docs/semantics/*.md` page; dated planning records
  and nested planning directories are excluded from normative checks
- the agent syntax exemplar and the `CALOR_REFERENCE` heredoc in
  `tests/E2E/agent-tasks/lib/helpers.sh`, which agents receive during tasks
- the **version scan only** additionally covers all of `docs/**/*.md`,
  excluding dated records under `docs/plans/`, `docs/experiments/`,
  `docs/design/`, and `docs/process/`
- every `website/content/**/*.mdx` page except the historical records listed
  in `WebsiteExampleChecker.HistoricalExclusions` (today: `changelog.mdx`).
  These pages get the keyword, diagnostic-code, version and forward-only
  effect-table checks, plus the website example checks below

## Checks

| Check | Finding |
|:------|:--------|
| Every documented `§`-keyword (e.g. `§EACH`, `§/C`) exists in the lexer's keyword table | `Calor1320` |
| Every cited `CalorNNNN` diagnostic code is defined in the compiler | `Calor1321` |
| Every cited diagnostic band (e.g. `Calor0800`–`0899`) contains at least one implemented code | `Calor1322` |
| Every effect code in `docs/syntax-reference/effects.md`'s "Effect Codes" table is known to the effect registry | `Calor1323` |
| Every implemented (non-legacy) effect code appears in that table | `Calor1324` |
| No covered doc hardcodes the current `Directory.Build.props` version | `Calor1325` |
| Current normative semantics-version claims match `SemanticsVersion.VersionString`, independently of package versions | `Calor1325` |
| Required files/sections are present and readable | `Calor1326` |
| Every implemented `Calor13xx` code is listed in `docs/cli/structured-output.md`'s table | `Calor1327` |
| Every complete-program example still parses (see below) | `Calor1328` |
| Generated mirrors (`AGENTS.md`, semantics AST inventory) match their sources (`CLAUDE.md`, `eng/ast-schema.json`) | `Calor1329` |
| Every complete `§M` program in the agent syntax exemplar compiles to valid C# (Roslyn-semantic-checked) | `Calor1330` |
| Every complete declaration example in the agent-task reference compiles to valid C# | `Calor1330` |
| The exemplar never binds an array-returning BCL call to a generic collection type (the E1a trap) | `Calor1331` |
| Every complete website example compiles with the CLI defaults, or reports exactly the codes its `expect=` annotation declares | `Calor1332` |
| Every website `output` fence matches the diagnostics its example actually produces; output-shaped fences are labelled `output` or `illustrative` | `Calor1333` |
| Website fence annotations are well-formed, and a negative example's codes and claimed line/column appear in its adjacent prose | `Calor1334` |

## Parse-checked examples

Fenced code blocks tagged `calor` whose **first non-blank line starts with
`§M`** declare a complete program by convention and are lexed and parsed
with the real compiler on every run — if the syntax rots, the check fails
with `Calor1328` at the offending doc line. Blocks that do not start with
`§M` are treated as deliberate fragments and are skipped.

## Website examples (#1143)

`website/content/**/*.mdx` uses the same complete-program rule, but compiles
each program **exactly as `calor --input file.calr` does** with default
options: lexer, parser, binder, type and effect checks, and Roslyn validation
of the generated C#. The fence info string carries the page's claim:

| Fence | Meaning | Check |
|:------|:--------|:------|
| ```` ```calor ```` starting with `§M` | Complete program | No errors (warnings allowed) |
| ```` ```calor expect=Calor0272 ```` | Intended negative example | The error **and** warning codes equal the listed set exactly; the adjacent prose (nearest heading to the next fence, plus any linked `output` fence) cites every listed code; a "line N, column M" claim before the fence matches a reported location |
| ```` ```calor group=orders ```` | One file of a multi-file example | All members of the group compile together, as `calor --input a.calr --input b.calr` does, so cross-module effect checks run. A fence may join several groups (`group=a,b`); its expectations must hold in each. When some members report errors, the members that reported none are compiled again without the failing ones and must still report no errors, because a failing file can stop generated-C# validation for the whole set |
| ```` ```text output ```` | Real diagnostics of the nearest preceding complete program | Every quoted `[file(line,col): ][error\|warning ]CalorNNNN: message` entry (continuation lines are joined) must match an actual diagnostic, and every actual error or warning must be quoted |
| ```` ```text illustrative ```` (or `json illustrative`) | Output CI does not check | None. The site shows the block as "Example output (not checked)" |

A bare or `text` fence that looks like tool output (it contains `CalorNNNN:`,
a `file.calr:line:col` location, an `=== … ===` banner, or a `BLOCKED:` line),
or a `json` fence whose keys are those of a CLI envelope or MCP response
(`success`, `diagnostics`, `schemaVersion`, `suggestions`, `obligations`,
`guards`, `patches`, `isError`, `decision`), and that carries neither label
fails with `Calor1333`. Fences are backtick or tilde fences of three or more
characters, optionally indented (under a list item or inside JSX). A fence on a
list-marker or blockquote line, an indented fence whose body dedents past its
opener, and an unclosed fence fail with `Calor1334`: their extent depends on
the enclosing container, so the check refuses to guess. Repeated annotations
(`expect=` twice) also fail. Unknown annotations, an
annotation on a fragment, and a complete program fenced with another language
fail with `Calor1334`. MDX cannot hold HTML comments, so website pages write
the suppression marker as `{/* drift:ignore */}`. On website pages it applies
to the keyword and diagnostic-code scans of prose only; it never exempts a
complete program or an output fence. On website pages the
generic closer placeholder (section sign, slash, X) is accepted without a
marker.

Limits: the output-shape test is a heuristic, so CLI output that has none of
those shapes is only labelled by review. Output of commands other than
compilation (`calor query`, `calor analyze`, MCP responses) is labelled
`illustrative`, not checked.

## Deep-checked exemplar

The agent syntax exemplar
(`src/Calor.Compiler/Resources/agent-syntax-exemplar.md`, served to agents as
`calor://primer`) gets a stronger check than parse-only: every complete `§M`
program in it is emitted to C# and the **generated C# is compiled with Roslyn's
full semantic model** (`Calor1330`). That is the only layer that catches type
errors — e.g. binding `File.ReadAllLines` (an array) to `List<str>`, which Calor
emits but the C# compiler rejects (`CS0029`). The copyable fragment reference
lines cannot be compiled standalone (they intermix prose and free identifiers),
so that one recurring trap is additionally caught by a lint (`Calor1331`):
array-returning BCL calls must bind to the array form `[T]`.

## Agent-task reference

The same full compilation check runs on the actual `CALOR_REFERENCE` heredoc
in `tests/E2E/agent-tasks/lib/helpers.sh`, not a separate copy. Bare and `calor`
fences starting with a module, function, class, interface, or delegate are
compiled independently. Non-module declarations get a synthetic module wrapper.
This checks typing and generated C#, not only parsing.

Use `calor-fragment` only for schemas with placeholders or examples that require
external declarations. A complete Calor declaration with another language label,
an unterminated fence, a missing heredoc, or an empty example inventory fails
with `Calor1330`. The tests also pin the complete-example count and the six
regression examples: `TryDouble`, `SafeDivide`, `HasNegative`, `DigitValue`,
`Offset`, and `ClampScore`. Update the count when deliberately adding or removing examples.
The existing CI `self-check docs` step enforces this guard.

## Meta-notation policy

Docs legitimately talk *about* notation: placeholders such as a generic
closer tag written as slash-X, hypothetical diagnostic codes, or foreign
code snippets. Two escapes exist:

1. **Foreign fences are never scanned.** A fenced block whose info string
   is anything other than `calor` (for example ```` ```text ````,
   ```` ```csharp ````, ```` ```bash ````) is invisible to the keyword and
   diagnostic-code scans. Bare ```` ``` ```` fences and ```` ```calor ````
   fences **are** scanned. The version scan looks inside all fences — a
   hardcoded version in an install snippet is exactly what it exists to
   catch — and honors only the marker below.

2. **The suppression marker.** A line containing `<!-- drift:ignore -->`
   suppresses all drift findings on the **next** line. Placed on the line
   before a ```` ```calor ```` fence, it exempts that block from the parse
   check. The HTML comment is invisible in rendered docs and may trail
   existing text (useful inside tables):

   ```text
   <!-- drift:ignore -->
   Closer tags were removed — an explicit §/X raises Calor0830.
   ```

Prefer the foreign-fence escape for whole blocks and the marker for single
lines; both should be rare. If the checker flags something real, fix the
doc (or the registry it is checked against) instead of suppressing.

## What it cannot catch

Semantic and prose drift: wrong descriptions of behavior, stale line
counts or file paths, outdated flag defaults, incorrect *output* examples
(except website `output` fences),
rotted `calor` fragments (blocks not starting with `§M`), undocumented
features (other than effect codes and the `Calor13xx` table, which are
checked for completeness), and anything in files outside the covered set.

## CI

The check runs on every PR as a step of the `test` job in
`.github/workflows/test.yml`, reusing that job's build.

## Generated mirror docs (AGENTS.md)

`AGENTS.md` is a **generated** derivative of `CLAUDE.md` — identical content with
the H1 title swapped and a "generated" banner — so the two agent manuals cannot
drift. It is single-sourced from `CLAUDE.md` and checked by `self-check docs`
(`Calor1329` when out of sync or missing). Do not hand-edit `AGENTS.md`; edit
`CLAUDE.md` and regenerate:

```
calor self-check docs --fix
```

`--fix` rewrites `AGENTS.md` from `CLAUDE.md` and
`docs/semantics/inventory.md` from `eng/ast-schema.json` (idempotent; writes
only on change). The complete node list and count are generated; hand edits
to either fail the mirror gate. Malformed or missing schema input is an error,
not an empty generated inventory.
The CI step "Check agent-facing docs against the implementation (spec drift)" runs `self-check docs` without `--fix`, so an un-regenerated
mirror fails the build.
