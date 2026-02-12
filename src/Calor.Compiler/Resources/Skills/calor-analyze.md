---
name: calor-analyze
description: Run deep security and bug analysis on Calor code
---

# /calor-analyze - Deep Code Analysis

Use this skill to find security vulnerabilities, bugs, and code quality issues.

## Command
```bash
calor --input <file.calr> --verify --analyze --verbose
```

**Note:** Use `--verbose` to see analysis results. Without it, warnings are only shown if compilation fails.

## What It Detects

### Security Vulnerabilities (Taint Analysis)
- **Calor0981** SQL Injection - User input flowing to database queries
- **Calor0982** Command Injection - User input in shell commands
- **Calor0983** Path Traversal - User input in file paths
- **Calor0984** Cross-Site Scripting (XSS) - User input in HTML output

### Bug Patterns
- **Calor0920** Division by Zero
- **Calor0921** Index Out of Bounds
- **Calor0922** Null/None Dereference
- **Calor0923** Integer Overflow

### Dataflow Issues
- **Calor0900** Uninitialized Variable
- **Calor0901** Dead Code (unreachable)
- **Calor0902** Dead Store (unused assignment)

### Contract Verification
- Proves preconditions and postconditions with Z3 SMT solver
- Reports counterexamples for failed proofs

## When to Use
1. Before committing security-sensitive code
2. After writing functions with complex control flow
3. When handling user input that flows to sinks
4. To verify loop termination and bounds

## Example Output
```
file.calr(12,5): warning Calor0981: Potential SQL injection: tainted data from 'user_input' flows to database sink
file.calr(25,10): warning Calor0920: Potential division by zero: divisor can be zero
```
