<!-- THIS FILE IS AUTO-GENERATED. DO NOT EDIT MANUALLY. -->
<!-- Generated from website/public/data/benchmark-results.json by CI/CD -->
<!-- Last generated: 2026-02-21T20:12:12.701Z -->

---
layout: default
title: Results
parent: Benchmarking
nav_order: 2
---

# Benchmark Results

Evaluated across 40 paired Calor/C# programs.

**Last updated:** February 21, 2026 (commit: 7f76e6e)

---

## Summary Table

| Category | Calor vs C# | Winner | Interpretation |
|:---------|:-----------|:-------|:---------------|
| Comprehension | **1.51x** | Calor | Explicit structure aids understanding |
| Error Detection | **1.22x** | Calor | Contracts surface invariant violations |
| Edit Precision | **1.37x** | Calor | Unique IDs enable targeted changes |
| Refactoring Stability | **1.36x** | Calor | Structural IDs preserve refactoring intent |
| Generation Accuracy | 0.97x | C# | Mature tooling, familiar patterns |
| Task Completion | **1.00x** | Calor | Better task completion rate |
| Token Economics | 0.83x | C# | Calor's explicit syntax uses more tokens |
| Information Density | 1.00x | C# |  |

---

## Category Breakdown

### Where Calor Wins

#### Comprehension (1.51x)

Calor's explicit structure provides clear signals for understanding:

| Factor | Calor | C# |
|:-------|:-----|:---|
| Module boundaries | `§M{id:name}...§/M{id}` | `namespace Name { }` |
| Function signatures | `§F{id:name:vis}` with `§I`, `§O` | Method declarations |
| Side effects | Explicit `§E{cw,db:rw}` | Must infer from code |
| Contracts | First-class `§Q`, `§S` | Comments or assertions |

#### Error Detection (1.22x)

Contracts make invariants explicit:

```
// Calor: Contracts are syntax
§Q (>= x 0)
§S (>= result 0)

// C#: Contracts are implementation detail
if (x < 0) throw new ArgumentException();
Debug.Assert(result >= 0);
```

#### Edit Precision (1.37x)

Unique IDs enable precise targeting:

```
// "Modify loop for1" - unambiguous
§L{for1:i:1:100:1}

// "Modify the for loop" - which one?
for (int i = 0; i < 100; i++)
```

#### Refactoring Stability (1.36x)

Structural IDs maintain references across refactoring operations, enabling reliable multi-step transformations.

#### Task Completion (1.00x)

Calor benefits when tasks require understanding code behavior through contracts.

---

### Where C# Wins

#### Generation Accuracy (0.97x)

C# benefits from extensive LLM training data and familiar patterns.

#### Token Economics (0.83x)

C# is more compact:

| Operation | Calor | C# |
|:----------|:-----|:---|
| Return sum | `§R (+ a b)` | `return a + b;` |
| Print value | `§P x` | `Console.WriteLine(x);` |
| Function def | 5-7 lines | 3-5 lines |

Average: Calor uses ~1.5x more tokens than C#.

---

## The Tradeoff Visualized

```
                    Calor better <-  -> C# better
                         |
Comprehension     ████████████░░░░  1.51x
Error Detection   ██████████░░░░░░  1.22x
Edit Precision    ███████████░░░░░  1.37x
Refactoring Stability███████████░░░░░  1.36x
                         |
Generation Accuracy░░░░░░░░████████  0.97x
Task Completion   ████████░░░░░░░░  1.00x
                         |
Token Economics   ░░░░░░░░░███████  0.83x
Information Density████████░░░░░░░░  1.00x
```

---

## Key Findings

### 1. Explicitness Has Value

Calor's comprehension advantage suggests explicit structure genuinely aids understanding, even at token cost.

### 2. Contracts Matter

The error detection advantage comes directly from first-class contracts—not from implementation complexity.

### 3. IDs Enable Precision

The edit precision advantage validates the unique ID design decision.

### 4. The Cost is Real

The token economics ratio means Calor consumes more context window. This is the price of explicitness.

### 5. Not a Universal Win

C# still wins on generation and completion metrics, reflecting ecosystem maturity and LLM training data bias toward familiar languages.

---

## When to Use Calor

Based on results, Calor is most valuable when:

- Agent comprehension is critical
- Contract verification matters
- Edit precision is important
- Token budget is flexible

Use C# when:

- Token efficiency is paramount
- Ecosystem libraries are needed
- Human readability is priority

---

## Next

- [Methodology](/calor/benchmarking/methodology/) - How these were measured
- [Individual Metrics](/calor/benchmarking/metrics/comprehension/) - Deep dive into each metric
