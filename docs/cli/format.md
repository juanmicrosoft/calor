---
layout: default
title: format
parent: CLI Reference
nav_order: 6
permalink: /cli/format/
---

# calor format

Format Calor source files to canonical style.

```bash
calor format <files...> [options]
```

---

## Overview

The `format` command formats Calor source files according to the canonical Calor style guide. This ensures consistent formatting across your codebase and makes code easier to read and maintain.

---

## Quick Start

```bash
# Format a single file (output to stdout)
calor format MyModule.calr

# Format and overwrite the file
calor format MyModule.calr --write

# Check if files are formatted (for CI)
calor format src/*.calr --check

# Show diff of changes
calor format MyModule.calr --diff
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `files` | Yes | One or more Calor source files to format |

---

## Options

| Option | Short | Default | Description |
|:-------|:------|:--------|:------------|
| `--check` | `-c` | `false` | Check if files are formatted without modifying (exit 1 if not) |
| `--write` | `-w` | `false` | Write formatted output back to the file(s) |
| `--diff` | `-d` | `false` | Show diff of formatting changes |
| `--verbose` | `-v` | `false` | Enable verbose output |
| `--heal` | — | `false` | Best-effort source-level repair of files too broken for the AST formatter. **Not semantics-preserving** — see [Heal Mode](#heal-mode) |
| `--format` | — | `text` | Output format: `text` or `json` (envelope document on stdout) — see [JSON Output](#json-output---format-json). No `-f` short alias |

---

## JSON Output (`--format json`)

With `--format json` stdout carries exactly one
[envelope document](/calor/cli/envelope-schema/) — all human-oriented status
moves to stderr, and a document is emitted on every path (including missing
files). Exit codes are unchanged.

```json
{
  "version": "1.1",
  "command": "format",
  "diagnostics": [
    { "code": "Calor1340", "message": "File not found: …", "severity": "error",
      "location": { "file": "…", "line": 1, "column": 1, "length": 0 } }
  ],
  "summary": { "total": 1, "errors": 1, "warnings": 0, "info": 0 },
  "data": {
    "files": [
      { "path": "/abs/Good.calr", "changed": true, "status": "formatted",
        "residualParseErrors": false }
    ],
    "totals": { "processed": 2, "formatted": 1, "errors": 1, "stillFailingAfterHeal": 0 }
  }
}
```

- `diagnostics[]` — the real parse/format diagnostics, plus CLI-level entries:
  `Calor1340` (file not found), `Calor1341` (non-`.calr` input, warning),
  `Calor1342` (processing error). Heal-mode residual parse errors appear with
  their own parser codes.
- `data.files[].status` — `formatted` | `already-formatted` | `would-reformat`
  (`--check`) | `healed` (`--heal`) | `error` | `skipped` | `not-found`.
- `data.files[].formatted` — the formatted source; present only in preview
  mode (neither `--write` nor `--check`) for files that changed.
- `data.files[].ambiguities` — heal mode: `[{ "line", "message" }]`
  control-flow guesses; `residualParseErrors` is true when the healed output
  still fails to parse.

---

## Default Behavior

By default (no flags), the formatted output is written to stdout:

```bash
calor format MyModule.calr
```

This allows you to preview changes before applying them.

---

## Write Mode

Use `--write` to format files in place:

```bash
# Format single file
calor format MyModule.calr --write

# Format multiple files
calor format src/*.calr --write
```

---

## Check Mode

Use `--check` in CI/CD to verify formatting:

```bash
calor format src/*.calr --check
```

Exit codes:
- `0` - All files are formatted correctly
- `1` - One or more files need formatting

Example CI configuration:

```yaml
# GitHub Actions
- name: Check Calor formatting
  run: calor format src/**/*.calr --check
```

---

## Heal Mode

The regular format path requires a file that parses. `--heal` works on raw
text, so it can repair exactly the class of files the AST formatter must
reject:

```bash
# Preview the healed source on stdout
calor format Broken.calr --heal

# Repair in place
calor format Broken.calr --heal --write

# CI / agent loop: report only (prints an ambiguousDecisions count per file)
calor format Broken.calr --heal --check
```

What it repairs:

- **Forbidden structural closers** (`§/F`, `§/M`, …, `Calor0830`) are stripped; lines left empty by the strip are deleted
- **Indentation** is re-derived from the file's own relative nesting and normalized to 2 spaces per level (tabs expanded, 3-/4-space levels collapsed, misaligned dedents snapped to the nearest enclosing level)
- **Chain clauses** (`§EI`/`§EL` for `§IF`, `§CA`/`§FI` for `§TR`) are re-aligned to the column of the opener they belong to
- **Whitespace**: trailing whitespace stripped, CRLF → LF

`§RAW`/`§CSHARP` payloads and multi-line bracketed expressions are preserved
verbatim. The transform is idempotent: healing already-healed output changes
nothing.

> **Warning — heal is NOT semantics-preserving.**
> Healing guesses the author's intent. In particular, a statement written at
> the same column as a misplaced `§EI`/`§EL` is *re-anchored into that
> clause's body* — it could equally have been meant as a statement after the
> if-chain. Every such control-flow guess is reported as a warning with its
> `file:line` (and `--check` prints an `ambiguousDecisions` count per file).
> **Always review the healed output before committing it.**

If the healed output still fails to parse — including when heal changed
nothing — a `heal could not repair` note with the remaining errors is printed
to stderr and the command exits `1`. Heal never silently succeeds on a file
that is still broken.

---

## Diff Mode

Use `--diff` to see what would change:

```bash
calor format MyModule.calr --diff
```

Output:
```
MyModule.calr
--- original
+++ formatted
@@ -5,7 +5,7 @@
 §F{f001:Calculate:pub}
   §I{i32:a}
   §I{i32:b}
-  §O{i32}
+  §O{i32}
   §Q (> a 0)
-§Q(>b 0)
+  §Q (> b 0)
   §R (+ a b)
```

Changes are highlighted:
- Lines starting with `-` (red) are removed
- Lines starting with `+` (green) are added

---

## Formatting Rules

The Calor formatter applies these rules:

### Indentation

- 2 spaces per indentation level
- Consistent indentation for nested structures

### Spacing

- Single space after structure tags: `§F{f001:Name:pub}`
- Single space around operators: `(+ a b)` not `(+a b)`
- No trailing whitespace

### Line Breaks

- One blank line between functions
- No multiple consecutive blank lines
- Newline at end of file

### Alignment

- Consistent alignment of matching tags
- Input/output parameters aligned

### Before and After

```calor
// Before (inconsistent)
§M{m001:Math}
  §F{f001:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R(+ a b)
```

```calor
// After (formatted)
§M{m001:Math}

  §F{f001:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (+ a b)

```

---

## Processing Multiple Files

When formatting multiple files, errors in one file don't stop processing of others:

```bash
calor format src/*.calr --write --verbose
```

Output:
```
Formatting 5 files...
  [OK] src/Calculator.calr
  [OK] src/UserService.calr
  [ERR] src/Broken.calr: Parse error at line 12
  [OK] src/OrderService.calr
  [OK] src/PaymentService.calr

Summary: 4 formatted, 1 error
```

---

## Verbose Mode

Use `--verbose` to see detailed processing information:

```bash
calor format MyModule.calr --write --verbose
```

Output:
```
Formatting MyModule.calr...
  Parsing: OK
  Changes: 3 lines modified
  Writing: OK
```

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Success (or all files formatted in `--check` mode) |
| `1` | Unformatted files found (`--check` mode), or `--heal` output still fails to parse |
| `2` | Error processing files |

---

## Examples

### Format All Calor Files

```bash
# Find and format all .calr files
find . -name "*.calr" -exec calor format {} --write \;

# Or use shell globbing
calor format **/*.calr --write
```

### Pre-Commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

# Check if any Calor files are staged
Calor_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep '\.calr$')

if [ -n "$Calor_FILES" ]; then
  # Format staged files
  echo "$Calor_FILES" | xargs calor format --check
  if [ $? -ne 0 ]; then
    echo "Calor files are not formatted. Run 'calor format --write' to fix."
    exit 1
  fi
fi
```

### Integration with Editors

Most editors can be configured to run formatters on save:

**VS Code (settings.json):**
```json
{
  "[calor]": {
    "editor.formatOnSave": true
  },
  "calor.formatCommand": "calor format --write"
}
```

---

## See Also

- [calor compile](/calor/cli/compile/) - Compile Calor to C#
- [Syntax Reference](/calor/syntax-reference/) - Calor language reference
