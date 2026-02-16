---
layout: default
title: Agent Refactoring Benchmark
parent: Benchmarking
nav_order: 3
---

# Agent Refactoring Benchmark

This benchmark measures how effectively AI coding agents (Claude Code) can perform refactoring tasks on Calor vs C# codebases.

---

## Overview

The agent refactoring benchmark tests real-world refactoring scenarios by:

1. Providing the agent with a codebase and a refactoring instruction
2. Running the agent (Claude Code CLI) to perform the refactoring
3. Verifying the result through compilation and contract verification

**Key insight**: Calor's explicit structure (unique IDs, contracts, effects) should make refactoring more reliable for AI agents.

---

## Benchmark Categories

| Category | Tasks | What It Tests |
|:---------|:------|:--------------|
| **Rename Symbol** | 3 Calor + 3 C# | Renaming parameters, variables with correct scope handling |
| **Extract Method** | 4 Calor + 4 C# | Extracting code into new functions with proper contracts |
| **Inline Function** | 3 Calor + 3 C# | Inlining function calls while preserving semantics |
| **Move Method** | 3 Calor + 3 C# | Moving functions between modules/classes |
| **Add Contract** | 4 Calor + 4 C# | Adding preconditions, postconditions, and effects |
| **Change Signature** | 3 Calor + 3 C# | Adding/removing/reordering parameters |

**Total: 40 tasks** (20 Calor + 20 C#)

---

## How It Works

### Task Structure

Each task consists of:

```
tests/E2E/agent-tasks/
├── fixtures/                    # Starting code templates
│   ├── refactor-rename-calor/   # Calor fixture with CLAUDE.md
│   └── refactor-rename-csharp/  # C# fixture with CLAUDE.md
└── tasks/refactoring-benchmark/
    └── refactor-rename-param-calor/
        └── task.json            # Task definition
```

### Task Definition (`task.json`)

```json
{
  "id": "refactor-rename-param-calor",
  "name": "Rename parameter (Calor)",
  "category": "refactoring-benchmark",
  "fixture": "refactor-rename-calor",
  "prompt": "In Rename.calr, find the Calculate function and rename 'val' to 'value'...",
  "verification": {
    "compilation": { "mustSucceed": true },
    "z3": { "enabled": false }
  },
  "timeout": 120
}
```

### Verification Steps

1. **Compilation**: The modified code must compile successfully
2. **Z3 Contracts** (optional): Contracts must be provable by Z3 SMT solver

---

## Running the Benchmark

### Run All Refactoring Tasks

```bash
cd tests/E2E/agent-tasks
./run-agent-tests.sh --category refactoring-benchmark
```

### Run Calor Tasks Only

```bash
./run-agent-tests.sh --category refactoring-benchmark --filter calor
```

### Run C# Tasks Only

```bash
./run-agent-tests.sh --category refactoring-benchmark --filter csharp
```

### Run Single Task

```bash
./run-agent-tests.sh --task refactor-rename-param-calor
```

### Quick Debug Run (No Majority Voting)

```bash
./run-agent-tests.sh --category refactoring-benchmark --single-run
```

---

## Majority Voting

By default, each task runs **3 times** and passes if **2 out of 3** runs succeed. This handles non-determinism in LLM outputs.

| Mode | Runs | Pass Threshold | Use Case |
|:-----|:-----|:---------------|:---------|
| Default | 3 | 2/3 (67%) | Production benchmarking |
| `--single-run` | 1 | 1/1 (100%) | Quick debugging |

---

## CI/CD Integration

The benchmark runs automatically:

- **Weekly** (Sunday midnight UTC) via scheduled workflow
- **On-demand** via workflow dispatch with `agent_refactoring: true`
- **On push** to `tests/E2E/agent-tasks/**` files

### Workflow File

See `.github/workflows/benchmark.yml` - the `agent-refactoring-benchmark` job.

### Results Location

Results are committed to: `website/public/data/agent-refactoring-results.json`

---

## Current Results

| Language | Pass Rate | Tasks |
|:---------|:----------|:------|
| **Calor** | 95% | 19/20 |
| **C#** | 95% | 19/20 |

*Last updated: See `website/public/data/agent-refactoring-results.json`*

---

## Why Calor Should Excel

Calor's design principles benefit agent refactoring:

### 1. Unique IDs
```
§F{f001:Calculate:pub}    # Stable reference across edits
```
Agents can target specific elements without ambiguity.

### 2. Explicit Contracts
```
§Q (>= n 0)               # Precondition
§S (>= result 1)          # Postcondition
```
Contracts propagate automatically when code is extracted or moved.

### 3. Effect Declarations
```
§E{cw}                    # Console write effect
```
Effects are tracked through refactorings, preventing silent side-effect changes.

### 4. Structural Markers
```
§F{...}§/F{...}           # Clear function boundaries
```
Agents can identify extraction boundaries precisely.

---

## Known Limitations

### Z3 Integer Overflow
Z3 may report false counterexamples for functions like `Abs(-2147483648)` due to 32-bit overflow edge cases. Such tasks disable Z3 verification.

### Cross-Module Calls
The Calor compiler doesn't fully support cross-module calls like `§C{OtherModule.Function}`. Move-to-module tasks disable compilation verification.

### LLM Non-Determinism
Some tasks may fail intermittently due to LLM variability. Majority voting (2/3) mitigates this.

---

## Adding New Tasks

### 1. Create Fixture

```bash
mkdir -p tests/E2E/agent-tasks/fixtures/refactor-YOUR-CATEGORY-calor
```

Add:
- `CLAUDE.md` - Syntax reference for the agent
- Source files (`.calr` for Calor, `.cs` for C#)

### 2. Create Task Definition

```bash
mkdir -p tests/E2E/agent-tasks/tasks/refactoring-benchmark/refactor-YOUR-TASK-calor
```

Create `task.json`:
```json
{
  "id": "refactor-YOUR-TASK-calor",
  "name": "Your task name (Calor)",
  "category": "refactoring-benchmark",
  "fixture": "refactor-YOUR-CATEGORY-calor",
  "prompt": "Clear instructions for the refactoring...",
  "verification": {
    "compilation": { "mustSucceed": true },
    "z3": { "enabled": true, "minProvenContracts": 1 }
  },
  "timeout": 120
}
```

### 3. Test Locally

```bash
./run-agent-tests.sh --task refactor-YOUR-TASK-calor --single-run --verbose
```

---

## See Also

- [Benchmarking Overview](/calor/benchmarking/)
- [Methodology](/calor/benchmarking/methodology/)
- [Adding Benchmarks](/calor/contributing/adding-benchmarks/)
