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
Findings use the `Calor1320`–`Calor1328` band (see
[Structured Output](/calor/cli/structured-output/)).

## Covered files

- `CLAUDE.md`
- every `docs/syntax-reference/*.md`
- every `docs/cli/*.md`
- the **version scan only** additionally covers all of `docs/**/*.md`,
  excluding dated records under `docs/plans/`, `docs/experiments/`,
  `docs/design/`, and `docs/process/`

## Checks

| Check | Finding |
|:------|:--------|
| Every documented `§`-keyword (e.g. `§EACH`, `§/C`) exists in the lexer's keyword table | `Calor1320` |
| Every cited `CalorNNNN` diagnostic code is defined in the compiler | `Calor1321` |
| Every cited diagnostic band (e.g. `Calor0800`–`0899`) contains at least one implemented code | `Calor1322` |
| Every effect code in `docs/syntax-reference/effects.md`'s "Effect Codes" table is known to the effect registry | `Calor1323` |
| Every implemented (non-legacy) effect code appears in that table | `Calor1324` |
| No covered doc hardcodes the current `Directory.Build.props` version | `Calor1325` |
| Required files/sections are present and readable | `Calor1326` |
| Every implemented `Calor13xx` code is listed in `docs/cli/structured-output.md`'s table | `Calor1327` |
| Every complete-program example still parses (see below) | `Calor1328` |

## Parse-checked examples

Fenced code blocks tagged `calor` whose **first non-blank line starts with
`§M`** declare a complete program by convention and are lexed and parsed
with the real compiler on every run — if the syntax rots, the check fails
with `Calor1328` at the offending doc line. Blocks that do not start with
`§M` are treated as deliberate fragments and are skipped.

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
counts or file paths, outdated flag defaults, incorrect *output* examples,
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

`--fix` rewrites `AGENTS.md` from `CLAUDE.md` (idempotent; writes only on change).
The CI step "Check agent-facing docs against the implementation (spec drift)" runs `self-check docs` without `--fix`, so an un-regenerated
`AGENTS.md` fails the build.
