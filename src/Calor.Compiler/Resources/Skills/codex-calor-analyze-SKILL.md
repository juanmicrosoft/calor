---
name: calor-analyze
description: Run deep security and bug analysis on Calor code using Z3 verification, taint analysis, and bug pattern detection.
---

# $calor-analyze - Deep Code Analysis

Use this skill to find security vulnerabilities, bugs, and code quality issues in Calor code.

## Command
```bash
calor --input <file.calr> --verify --analyze --verbose
```

**Note:** Use `--verbose` to see analysis results. Without it, warnings are only shown if compilation fails.

## What It Detects

### Security Vulnerabilities (Taint Analysis)
| Code | Issue | Description |
|------|-------|-------------|
| **Calor0981** | SQL Injection | User input flowing to database queries |
| **Calor0982** | Command Injection | User input in shell commands |
| **Calor0983** | Path Traversal | User input in file paths |
| **Calor0984** | XSS | User input in HTML output |

### Bug Patterns
| Code | Issue | Description |
|------|-------|-------------|
| **Calor0920** | Division by Zero | Divisor can be zero at runtime |
| **Calor0921** | Index Out of Bounds | Array access may exceed bounds |
| **Calor0922** | Null Dereference | Potential null/None access |
| **Calor0923** | Integer Overflow | Arithmetic may overflow |

### Dataflow Issues
| Code | Issue | Description |
|------|-------|-------------|
| **Calor0900** | Uninitialized Variable | Variable used before assignment |
| **Calor0901** | Dead Code | Unreachable code path |
| **Calor0902** | Dead Store | Assignment never read |

### Contract Verification (Z3)
- Proves preconditions (`§Q`) and postconditions (`§S`) using Z3 SMT solver
- Reports counterexamples when proofs fail
- Verifies loop invariants and termination

## When to Use

1. **Before committing security-sensitive code** - Catch injection vulnerabilities
2. **After writing functions with complex control flow** - Find dead code and unreachable paths
3. **When handling user input** - Track tainted data to sinks
4. **To verify contracts** - Prove preconditions and postconditions

## Workflow

```bash
# 1. Write or modify .calr file
# 2. Run deep analysis
calor --input myfile.calr --verify --analyze --verbose

# 3. Fix reported issues
# 4. Re-run until clean
```

## Example Output

```
file.calr(12,5): warning Calor0981: Potential SQL injection: tainted data from 'user_input' flows to database sink
file.calr(25,10): warning Calor0920: Potential division by zero: divisor can be zero
file.calr(30,3): warning Calor0901: Dead code: this branch is unreachable
```

## Taint Sources and Sinks

### Sources (User Input)
- Function parameters marked with user input effects
- Console/stdin reads
- HTTP request data
- File reads from user-specified paths

### Sinks (Dangerous Operations)
- Database queries (`§E{db:w}`)
- Shell commands (`§E{exec}`)
- File system writes (`§E{fs:w}`)
- HTML output (`§E{html}`)

## Contract Verification Example

```calor
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)           // Precondition: b cannot be zero
  §S (>= result 0)      // Postcondition: result is non-negative
  §R (/ a b)
§/F{f001}
```

Running `calor --verify --analyze` will:
1. Verify the precondition is sufficient to prevent division by zero
2. Check if the postcondition can be violated with valid inputs
3. Report counterexamples if verification fails

## Note

Codex does not support hooks, so always run analysis manually after writing code.
