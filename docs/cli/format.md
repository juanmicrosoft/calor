---
layout: default
title: format
parent: CLI Reference
nav_order: 6
permalink: /cli/format/
---

# opalc format

Format OPAL source files to canonical style.

```bash
opalc format <files...> [options]
```

---

## Overview

The `format` command formats OPAL source files according to the canonical OPAL style guide. This ensures consistent formatting across your codebase and makes code easier to read and maintain.

---

## Quick Start

```bash
# Format a single file (output to stdout)
opalc format MyModule.opal

# Format and overwrite the file
opalc format MyModule.opal --write

# Check if files are formatted (for CI)
opalc format src/*.opal --check

# Show diff of changes
opalc format MyModule.opal --diff
```

---

## Arguments

| Argument | Required | Description |
|:---------|:---------|:------------|
| `files` | Yes | One or more OPAL source files to format |

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
opalc format MyModule.opal
```

This allows you to preview changes before applying them.

---

## Write Mode

Use `--write` to format files in place:

```bash
# Format single file
opalc format MyModule.opal --write

# Format multiple files
opalc format src/*.opal --write
```

---

## Check Mode

Use `--check` in CI/CD to verify formatting:

```bash
opalc format src/*.opal --check
```

Exit codes:
- `0` - All files are formatted correctly
- `1` - One or more files need formatting

Example CI configuration:

```yaml
# GitHub Actions
- name: Check OPAL formatting
  run: opalc format src/**/*.opal --check
```

---

## Diff Mode

Use `--diff` to see what would change:

```bash
opalc format MyModule.opal --diff
```

Output:
```
MyModule.opal
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

The OPAL formatter applies these rules:

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

```opal
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

```opal
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
opalc format src/*.opal --write --verbose
```

Output:
```
Formatting 5 files...
  [OK] src/Calculator.opal
  [OK] src/UserService.opal
  [ERR] src/Broken.opal: Parse error at line 12
  [OK] src/OrderService.opal
  [OK] src/PaymentService.opal

Summary: 4 formatted, 1 error
```

---

## Verbose Mode

Use `--verbose` to see detailed processing information:

```bash
opalc format MyModule.opal --write --verbose
```

Output:
```
Formatting MyModule.opal...
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

### Format All OPAL Files

```bash
# Find and format all .opal files
find . -name "*.opal" -exec opalc format {} --write \;

# Or use shell globbing
opalc format **/*.opal --write
```

### Pre-Commit Hook

```bash
#!/bin/bash
# .git/hooks/pre-commit

# Check if any OPAL files are staged
OPAL_FILES=$(git diff --cached --name-only --diff-filter=ACM | grep '\.opal$')

if [ -n "$OPAL_FILES" ]; then
  # Format staged files
  echo "$OPAL_FILES" | xargs opalc format --check
  if [ $? -ne 0 ]; then
    echo "OPAL files are not formatted. Run 'opalc format --write' to fix."
    exit 1
  fi
fi
```

### Integration with Editors

Most editors can be configured to run formatters on save:

**VS Code (settings.json):**
```json
{
  "[opal]": {
    "editor.formatOnSave": true
  },
  "opal.formatCommand": "opalc format --write"
}
```

---

## See Also

- [opalc diagnose](/opal/cli/diagnose/) - Check for errors and warnings
- [opalc compile](/opal/cli/compile/) - Compile OPAL to C#
- [Syntax Reference](/opal/syntax-reference/) - OPAL language reference
