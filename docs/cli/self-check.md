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
("doc drift"). Current agent teaching resources are generated from
`src/Calor.Compiler/Resources/Teaching/reference.json`. Other documentation
is still authored separately. Prose or behavioral inaccuracies outside the
checked forms below will not be caught.

```bash
calor self-check docs                 # check the enclosing repository
calor self-check docs --root /path    # explicit repository root
calor self-check docs --format json   # unified diagnostic schema on stdout
calor self-check docs --format sarif  # SARIF 2.1.0 on stdout
calor self-check docs --teaching-only # generated teaching resources only
calor self-check docs --teaching-only --fix # regenerate, then check those resources
```

`--teaching-only` verifies all generated teaching mirrors, current keyword
references, and compilation of the primer and every complete reference program.
E2E reference loading uses this scoped gate before copying the reference.
It is not a repository-wide documentation result. Without this option,
`--fix` also regenerates the agent manual and AST inventory, then runs the full check.

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
- the generated agent syntax exemplar and current-language reference copied by
  `tests/E2E/agent-tasks/lib/helpers.sh` into task workspaces
- teaching mirrors generated from
  `src/Calor.Compiler/Resources/Teaching/reference.json`: syntax-query/hover JSON,
  the primer, templates, MCP instructions/catalogs, and the `current-language`
  pages in `docs/` and the website
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
| Generated mirrors (agent manual, AST inventory and teaching resources) match their authored sources | `Calor1329` |
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
| ```` ```calor group=orders ```` | One file of a multi-file example | All members of the group compile together, as `calor --input a.calr --input b.calr` does, so cross-module effect checks run. A fence may join several groups (`group=a,b`); its expectations must hold in each. When a member fails its own compile (so it produces no C#), the other members are compiled again without it and may not report any new error code, because a failing file can stop generated-C# validation and the cross-module pass for the whole set |
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

### Landing-page snippets

The checker also loads `website/src/components/landing/snippets.json` and all
five registered consumers: `CodeComparison`, `FirstProgram`, `CatchBugs`,
`FeatureGrid`, and `CompetitivePositioning`. Missing files fail with `Calor1326`.
Each actual Calor display uses `landingSnippets.<id>.code` from that JSON file.
Every entry must have exactly one reference in its registered component;
unknown references, unused entries, and inline Calor source fail with
`Calor1334`. The guard is a conservative source convention, not a TypeScript
interpreter: only the five explicitly registered prose literals mentioning
tags are exempt. Code-display shapes and literal CLI/output exceptions are
registered too, so expression-only fragments cannot bypass the check by omitting
tags. New display shapes and prose mentions must be reviewed and registered.
CLI commands and the comparison's C# code are not Calor programs.

Each entry's `code` is the displayed source, without a second checked copy.
`before`, `after`, and `indent` provide explicit compilation context for a
fragment; `inputs` supplies any additional complete Calor modules. The checker
compiles this context with the same pipeline as MDX examples, including generated
C# validation. `expectedDiagnostics` must match every error and warning by code,
severity, message, and multiplicity; positives use an empty array.

There are two complete programs and four fragments. The contract fragment gains
an `i32` parameter, a pure function, and an identity return; the effect fragment
gains a function; the stable-ID declaration gains a void, pure body. CatchBugs
gains an `Order` class and a separate module containing declared-effect stubs for
`SaveOrder` and `NotifyCustomer`. Those stubs accept `object`, allowing the local
`Order` type without inventing a database or network implementation. The
cross-module declaration check reports exactly the missing `net:rw` effect
(`Calor0410`). This validates the stated assumption, not the omitted real-world
implementations. The abridged diagnostic displayed on the page remains explicitly
illustrative, not captured output.

After a website build, `cd website && npm run test:landing` also checks that
the exported landing page displays each mounted component's shared strings
exactly once and contains no extra Calor code block. Currently three snippets
are mounted; `FeatureGrid` and `CompetitivePositioning` are not mounted by
`website/src/app/page.tsx`, but their sources remain registered and checked.

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

The same full compilation check runs on
`src/Calor.Compiler/Resources/Teaching/Generated/current-language-reference.md`,
the actual reference copied into E2E workspaces and packaged for current help
and evaluation prompts. The harness checks documentation drift before copying;
it never launches with a stale reference. Complete `calor` modules compile
independently, including generated C# validation.

Use `calor-fragment` only for schemas with placeholders or examples that require
external declarations. A complete Calor declaration with another language label,
an unterminated fence or an empty example inventory fails
with `Calor1330`. The tests also pin the complete-example count and the six
regression examples: `TryDouble`, `SafeDivide`, `HasNegative`, `DigitValue`,
`Offset`, and `ClampScore`. Update the count when deliberately adding or removing examples.
The existing CI `self-check docs` step enforces this guard.

Edit `Resources/Teaching/reference.json`, not its generated outputs. Run
`dotnet run --project src/Calor.Compiler -- self-check docs --fix`, then rebuild
to package the changed resources. The same command without `--fix` checks every
mirror without writing. Generation is deterministic UTF-8 without a BOM.
Final grammar spellings and token budgets are separate release gates: this
generator makes no tokenizer substitution or measured-budget claim.

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
