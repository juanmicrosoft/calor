---
layout: default
title: format
parent: CLI Reference
nav_order: 6
permalink: /cli/format/
---

# calorc format

Format Calor source files to canonical style.

```bash
calorc format <files...> [options]
```

---

## Overview

The `format` command formats Calor source files according to the canonical Calor style guide. This ensures consistent formatting across your codebase and makes code easier to read and maintain.

---

## Quick Start

```bash
# Format a single file (output to stdout)
calorc format MyModule.calor

# Format and overwrite the file
calorc format MyModule.calor --write

# Check if files are formatted (for CI)
calorc format src/*.calor --check

# Show diff of changes
calorc format MyModule.calor --diff
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

---

## Default Behavior

By default (no flags), the formatted output is written to stdout:

```bash
calorc format MyModule.calor
```

This allows you to preview changes before applying them.

---

## Write Mode

Use `--write` to format files in place:

```bash
# Format single file
calorc format MyModule.calor --write

# Format multiple files
calorc format src/*.calor --write
```

---

## Check Mode

Use `--check` in CI/CD to verify formatting:

```bash
calorc format src/*.calor --check
```

Exit codes:
- `0` - All files are formatted correctly
- `1` - One or more files need formatting

Example CI configuration:

```yaml
# GitHub Actions
- name: Check Calor formatting
  run: calorc format src/**/*.calor --check
```

---

## Diff Mode

Use `--diff` to see what would change:

```bash
calorc format MyModule.calor --diff
```

Output:
```
MyModule.calor
--- original
+++ formatted
@@ -5,7 +5,7 @@
 §F[f001:Calculate:pub]
   §I[i32:a]
   §I[i32:b]
-  §O[i32]
+  §O[i32]
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

- Single space after structure tags: `§F[f001:Name:pub]`
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
§M[m001:Math]
§F[f001:Add:pub]
§I[i32:a]
  §I[i32:b]
§O[i32]
§R(+ a b)
§/F[f001]
§/M[m001]
```

```calor
// After (formatted)
§M[m001:Math]

§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §R (+ a b)
§/F[f001]

§/M[m001]
```

---

## Processing Multiple Files

When formatting multiple files, errors in one file don't stop processing of others:

```bash
calorc format src/*.calor --write --verbose
```

Output:
```
Formatting 5 files...
  [OK] src/Calculator.calor
  [OK] src/UserService.calor
  [ERR] src/Broken.calor: Parse error at line 12
  [OK] src/OrderService.calor
  [OK] src/PaymentService.calor

Summary: 4 formatted, 1 error
```

---

## Verbose Mode

Use `--verbose` to see detailed processing information:

```bash
calorc format MyModule.calor --write --verbose
```

Output:
```
Formatting MyModule.calor...
  Parsing: OK
  Changes: 3 lines modified
  Writing: OK
```

---

## Exit Codes

| Code | Meaning |
|:-----|:--------|
| `0` | Success (or all files formatted in `--check` mode) |
| `1` | Unformatted files found (`--check` mode) |
| `2` | Error processing files |

---

## Examples

### Format All Calor Files

```bash
# Find and format all .calor files
find . -name "*.calor" -exec calorc format {} --write \;

# Or use shell globbing
calorc format **/*.calor --write
```

### Pre-Commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

# Check if any Calor files are staged
Calor_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep '\.calor$')

if [ -n "$Calor_FILES" ]; then
  # Format staged files
  echo "$Calor_FILES" | xargs calorc format --check
  if [ $? -ne 0 ]; then
    echo "Calor files are not formatted. Run 'calorc format --write' to fix."
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
  "calor.formatCommand": "calorc format --write"
}
```

---

## See Also

- [calorc diagnose](/calor/cli/diagnose/) - Check for errors and warnings
- [calorc compile](/calor/cli/compile/) - Compile Calor to C#
- [Syntax Reference](/calor/syntax-reference/) - Calor language reference
