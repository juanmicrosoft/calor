---
layout: default
title: Structured Output
parent: CLI Reference
nav_order: 15
permalink: /cli/structured-output/
---

# Structured Diagnostic Output (`--format json|sarif`)

The root compile command (`calor --input …`) and `calor lint` support a
`--format` option that switches diagnostic output from human-readable text to a
machine-readable document on **stdout**:

```bash
calor --input file.calr --format json     # unified JSON schema (below)
calor --input file.calr --format sarif    # SARIF 2.1.0
calor lint file.calr --format json        # lint findings in the same schema
```

| Value | Output |
|:------|:-------|
| `text` (default) | Human-readable diagnostics on **stderr**; nothing structured on stdout |
| `json` | Unified JSON diagnostic document on **stdout** |
| `sarif` | [SARIF 2.1.0](https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html) log on **stdout** |

On the root command `--format` has the short alias `-f`. On `calor lint` there
is **no short alias** — `-f` means `--fix` there; spell out `--format`.

## Output-stream contract

In a structured format (`json` or `sarif`):

- **stdout carries exactly one parseable document** — nothing else. Verbose
  phase messages (`--verbose`), status lines ("Compilation successful: …"),
  and other human-oriented output are routed to **stderr**.
- **A document is always emitted**, including early-exit error paths that never
  reach the compiler pipeline: a missing `--input` file, an invalid argument
  combination, an unhandled crash, a `lint` target that doesn't exist or isn't
  a `.calr` file. These paths inject CLI-level diagnostics (`Calor1300`–`Calor1399`)
  into the document so `calor … --format json | jq .` never fails to parse.
- All severities are serialized, including `info`. Text mode prints the same
  set of diagnostics (to stderr), so text and structured output never disagree
  about what was reported.

## JSON schema

```json
{
  "version": "1.0",
  "diagnostics": [
    {
      "code": "Calor0250",
      "message": "Binding 'x' requires a type or an initializer",
      "severity": "error",
      "location": {
        "file": "/abs/path/broken.calr",
        "line": 3,
        "column": 5,
        "length": 4
      },
      "suggestion": "Change 'wrong' to 'right'",
      "fix": {
        "description": "Change 'wrong' to 'right'",
        "edits": [
          {
            "filePath": "/abs/path/broken.calr",
            "startLine": 4, "startColumn": 7,
            "endLine": 4, "endColumn": 12,
            "newText": "right"
          }
        ]
      }
    }
  ],
  "summary": { "total": 1, "errors": 1, "warnings": 0, "info": 0 }
}
```

Field notes:

- `version` — schema version of this document, currently `"1.0"`.
- `diagnostics[]` — one entry per diagnostic, aggregated across all input files.
  - `code` — stable `CalorNNNN` diagnostic code (see the band table below).
  - `severity` — `"error"`, `"warning"`, or `"info"`.
  - `location.file` — may be `null` for diagnostics without a file (e.g. some
    usage errors); `line`/`column` are 1-based; `length` is the span length in
    characters.
  - `suggestion` / `fix` — present only when the compiler attaches a suggested
    fix. `fix.edits[]` are machine-applicable text edits (1-based line/column,
    end-exclusive replace of the region with `newText`).
- `summary` — counts over `diagnostics[]`; `total` always equals the array length.

Fields that are `null` are omitted from the serialized output.

## SARIF mapping

`--format sarif` emits a SARIF 2.1.0 log with a single run:

| Unified concept | SARIF location |
|:----------------|:---------------|
| Tool | `runs[0].tool.driver.name` = `"calor"` (`"calor-assess"` for `calor assess`) |
| Diagnostic code | `results[].ruleId`, with metadata in `tool.driver.rules[]` (`id`, `shortDescription`, `helpUri`) |
| Severity | `results[].level`: error → `"error"`, warning → `"warning"`, info → `"note"` |
| Message | `results[].message.text` |
| Location | `results[].locations[0].physicalLocation` — `artifactLocation.uri` (file), `region.startLine`/`startColumn`/`endColumn` |
| Fix | `results[].fixes[]` — `description.text` plus `artifactChanges[].replacements[]` (`deletedRegion` + `insertedContent.text`) |

Only rules for codes actually present in the results are enumerated in
`tool.driver.rules[]`.

## Exit codes

Exit codes are independent of `--format` — a structured document is emitted
*and* the process exits nonzero on failure:

| Command | Exit code | Meaning |
|:--------|:----------|:--------|
| `calor --input …` | 0 | compiled without errors (warnings/info allowed) |
| | 1 | compilation errors, missing input file, usage error, or crash |
| `calor lint` | 0 | all files clean |
| | 1 | lint issues found (and not `--fix`ing them) |
| | 2 | file-level errors: file not found, non-`.calr` input, parse failure, or a processing exception |

## CLI diagnostic codes (Calor1300–Calor1399)

Diagnostics raised by the CLI commands themselves (not the compilation
pipeline) so they can flow through the structured formats:

| Code | Meaning |
|:-----|:--------|
| `Calor1300` | Lint: line has trailing whitespace |
| `Calor1301` | Lint: construct ID is not abbreviated (e.g. `f001` → `f1`, `for1` → `l1`) |
| `Calor1302` | Lint: input file not found |
| `Calor1303` | Lint: input file is not a `.calr` file |
| `Calor1304` | Lint: unexpected error while processing a file |
| `Calor1310` | Compile: `--input` file not found |
| `Calor1311` | Compile: invalid argument combination (e.g. `--output` with multiple inputs) |
| `Calor1312` | Compile: unhandled internal error |
| `Calor1320` | Docs drift (`calor self-check docs`): documented §-keyword does not exist in the lexer |
| `Calor1321` | Docs drift: cited diagnostic code is not defined in the compiler |
| `Calor1322` | Docs drift: documented diagnostic band contains no implemented codes |
| `Calor1323` | Docs drift: documented effect code is unknown to the effect registry |
| `Calor1324` | Docs drift: implemented effect code missing from the effect-code docs |
| `Calor1325` | Docs drift: doc file hardcodes the current compiler version |
| `Calor1326` | Docs drift: a file or doc section the self-check needs is missing |
| `Calor1327` | Docs drift: CLI diagnostic code missing from this table |

## Notes on specific commands

- **`calor lint --fix --format json`** — the document lists the issues that
  were *found* in this run; when `--fix` succeeds those issues have been
  rewritten in place by the time the command exits (the "Fixed: file" status
  line goes to stderr). There is no per-diagnostic `fixed` marker yet.
- **`calor assess --format sarif`** uses the same shared SARIF serializer with
  tool name `calor-assess` and per-dimension rule IDs (`Calor-<Dimension>`).
- **`calor verify --format json`** embeds this schema's `diagnostics[]` array
  per file (alongside its legacy flat `errors`/`warnings` string arrays), but
  its top-level document is command-specific.
- **`calor self-check docs --format json`** emits the unified schema on stdout
  with docs-drift findings (`Calor1320`–`Calor1327`) and exits 1 when drift is
  found (text mode reports the same findings on stderr).
