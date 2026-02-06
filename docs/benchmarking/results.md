---
layout: default
title: Results
parent: Benchmarking
nav_order: 2
---

# Benchmark Results

Evaluated across 20 paired Calor/C# programs (100% compilation success for both).

---

## Summary Table

| Category | Calor vs C# | Winner | Interpretation |
|:---------|:-----------|:-------|:---------------|
| Comprehension | **1.33x** | Calor | Explicit structure aids understanding |
| Error Detection | **1.19x** | Calor | Contracts surface invariant violations |
| Edit Precision | **1.15x** | Calor | Unique IDs enable targeted changes |
| Generation Accuracy | 0.94x | C# | Mature tooling, familiar patterns |
| Task Completion | 0.93x | C# | Ecosystem maturity advantage |
| Token Economics | 0.67x | C# | Calor's explicit syntax uses more tokens |
| Information Density | 0.22x | C# | Calor trades density for explicitness |

---

## Category Breakdown

### Where Calor Wins

#### Comprehension (1.33x)

Calor's explicit structure provides clear signals for understanding:

| Factor | Calor | C# |
|:-------|:-----|:---|
| Module boundaries | `§M[id:name]...§/M[id]` | `namespace Name { }` |
| Function signatures | `§F[id:name:vis]` with `§I`, `§O` | Method declarations |
| Side effects | Explicit `§E[cw,db]` | Must infer from code |
| Contracts | First-class `§Q`, `§S` | Comments or assertions |

#### Error Detection (1.19x)

Contracts make invariants explicit:

```
// Calor: Contracts are syntax
§Q (>= x 0)
§S (>= result 0)

// C#: Contracts are implementation detail
if (x < 0) throw new ArgumentException();
Debug.Assert(result >= 0);
```

#### Edit Precision (1.15x)

Unique IDs enable precise targeting:

```
// "Modify loop for1" - unambiguous
§L[for1:i:1:100:1]

// "Modify the for loop" - which one?
for (int i = 0; i < 100; i++)
```

---

### Where C# Wins

#### Token Economics (0.67x)

C# is more compact:

| Operation | Calor | C# |
|:----------|:-----|:---|
| Return sum | `§R (+ a b)` | `return a + b;` |
| Print value | `§P x` | `Console.WriteLine(x);` |
| Function def | 5-7 lines | 3-5 lines |

Average: Calor uses ~1.5x more tokens than C#.

#### Information Density (0.22x)

C# packs more semantic content per token:

| Metric | Calor | C# | Ratio |
|:-------|:-----|:---|:------|
| Avg semantic elements | 8.2 | 12.5 | 0.66x |
| Avg tokens | 45 | 28 | 1.6x |
| Density | 0.18 | 0.45 | 0.40x |

---

## The Tradeoff Visualized

```
                    Calor better ←  → C# better
                         |
Comprehension     ████████████░░░░  1.33x
Error Detection   ██████████░░░░░░  1.19x
Edit Precision    █████████░░░░░░░  1.15x
                         |
Gen. Accuracy     ░░░░░░░█████████  0.94x
Task Completion   ░░░░░░░█████████  0.93x
                         |
Token Economics   ░░░░░░░░░░██████  0.67x
Info Density      ░░░░░░░░░░░░░░██  0.22x
```

---

## Per-Program Results

| Program | Comprehension | Error Det. | Edit Prec. | Tokens (Calor/C#) |
|:--------|:--------------|:-----------|:-----------|:-----------------|
| HelloWorld | 0.75 / 0.55 | 0.30 / 0.30 | 0.85 / 0.65 | 25 / 15 |
| FizzBuzz | 0.95 / 0.70 | 0.35 / 0.30 | 0.90 / 0.70 | 80 / 50 |
| Factorial | 0.90 / 0.65 | 0.75 / 0.50 | 0.85 / 0.70 | 45 / 30 |
| Divide | 0.95 / 0.70 | 0.90 / 0.60 | 0.85 / 0.70 | 50 / 35 |
| FileRead | 0.90 / 0.65 | 0.50 / 0.40 | 0.85 / 0.70 | 60 / 40 |
| ... | ... | ... | ... | ... |

*Scores are 0-1, higher is better for Calor metrics.*

---

## Key Findings

### 1. Explicitness Has Value

The 1.33x comprehension advantage suggests explicit structure genuinely aids understanding, even at token cost.

### 2. Contracts Matter

The 1.19x error detection advantage comes directly from first-class contracts—not from implementation complexity.

### 3. IDs Enable Precision

The 1.15x edit precision advantage validates the unique ID design decision.

### 4. The Cost is Real

The 0.67x token economics ratio means Calor consumes more context window. This is the price of explicitness.

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
