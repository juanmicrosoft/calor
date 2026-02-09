---
layout: default
title: Information Density
parent: Benchmarking
nav_order: 9
---

# Information Density Metric

**Category:** Information Density
**Result:** C# wins ([see current ratio](/calor/benchmarking/results/))
**What it measures:** Semantic elements per token

---

## Overview

The Information Density metric measures how much semantic content is packed into each token. Higher density means more meaning per token.

---

## Why It Matters

Not all tokens carry equal semantic weight:

- `public` carries visibility information
- `{` carries only structural information
- `§E{cw}` carries effect information

Languages that pack more meaning into fewer tokens use context windows more efficiently.

---

## How It's Measured

### Semantic Elements Counted

**Calor elements:**
| Element | What it represents |
|:--------|:-------------------|
| Modules (`§M{`) | Namespace/module |
| Functions (`§F{`) | Function definition |
| Variables (`§V{`) | Variable binding |
| Type annotations (`§I{`, `§O{`) | Type information |
| Contracts (`§Q`, `§S`) | Preconditions/postconditions |
| Effects (`§E{`) | Side effect declarations |
| Control flow (`§IF`, `§L`) | Branches and loops |
| Expressions (`§C{`, operators) | Computations |

**C# elements (via Roslyn):**
| Element | What it represents |
|:--------|:-------------------|
| Namespaces | Namespace declarations |
| Methods | Function definitions |
| Constructors | Constructor definitions |
| Variables | Variable declarations |
| Parameters | Function parameters |
| Type annotations | Type syntax nodes |
| Control flow | If/for/while/switch |
| Expressions | Invocations, binary ops |

### Density Calculation

```
Density = TotalSemanticElements / TokenCount
```

Higher density = more semantic content per token.

---

## Why C# Wins (Significantly)

### 1. Implicit Information

C# encodes information implicitly:

```csharp
public static int Add(int a, int b) => a + b;
```

This single line conveys:
- Visibility (public)
- Static binding
- Return type (int)
- Two parameters with types
- Expression body

**Calor equivalent:**
```
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
```

Same semantics, but spread across more tokens.

### 2. No Explicit Closing Tags

C# closing braces carry minimal semantic weight but are counted as tokens.

Calor closing tags (`§/F{f001}`) also carry minimal semantic weight but use more characters.

### 3. Effect Information is "Extra"

Calor's effect declarations add semantic content that C# doesn't have:

```
§E{cw,fs:r,net:rw}    // 3+ semantic elements
```

But C# has no equivalent, so it doesn't get penalized for missing this information.

---

## Detailed Breakdown

### Example: FizzBuzz

**Calor:**
```
§M{m001:FizzBuzz}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §L{for1:i:1:100:1}
    §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I{if1}
  §/L{for1}
§/F{f001}
§/M{m001}
```

| Element Type | Count |
|:-------------|:------|
| Module | 1 |
| Function | 1 |
| Output type | 1 |
| Effect | 1 |
| Loop | 1 |
| Conditionals | 4 |
| Expressions | 4 |
| **Total** | **13** |

Tokens: ~80
Density: 13/80 = **0.16**

**C#:**
```csharp
for (int i = 1; i <= 100; i++)
{
    if (i % 15 == 0) Console.WriteLine("FizzBuzz");
    else if (i % 3 == 0) Console.WriteLine("Fizz");
    else if (i % 5 == 0) Console.WriteLine("Buzz");
    else Console.WriteLine(i);
}
```

| Element Type | Count |
|:-------------|:------|
| For statement | 1 |
| Variable decl | 1 |
| If statements | 4 |
| Method calls | 4 |
| Binary exprs | 7 |
| **Total** | **17** |

Tokens: ~50
Density: 17/50 = **0.34**

**Ratio:** 0.16/0.34 = **0.47x**

---

## Sub-Metrics

The calculator provides detailed breakdowns:

### Overall Density
Total semantic elements / total tokens

### Type Density
Type annotations / total tokens

### Contract Density
Contract elements / total tokens

### Effect Density
Effect declarations / total tokens

---

## Interpretation Nuance

The low ratio seems dramatic, but consider:

### Calor's "Extra" Semantics

Calor includes semantic elements C# doesn't have:
- Explicit effects
- First-class contracts
- Unique IDs

These are counted as semantic elements but have no C# equivalent.

### Quality vs Quantity

Information density measures *quantity* of semantic content per token, not *quality* or *usefulness*.

Calor's explicit effects might be more valuable for agent reasoning than multiple C# type annotations, even if effects contribute fewer semantic elements.

---

## When Density Matters Less

Low density is acceptable when:

1. **Explicitness has value** - Contracts, effects worth the tokens
2. **Comprehension matters** - Clear structure aids understanding
3. **Context is available** - Large context windows reduce pressure
4. **Precision is needed** - IDs enable precise targeting

---

## The Tradeoff

This metric captures Calor's fundamental tradeoff:

| Approach | Density | Explicitness |
|:---------|:--------|:-------------|
| C# | High | Low (implicit) |
| Calor | Low | High (explicit) |

Calor deliberately trades density for explicitness.

---

## Improving Calor's Density

Potential improvements (with tradeoffs):

1. **Shorter tags** - `§F` could become `@F` (less distinctive)
2. **Implicit closing** - Remove closing tags (lose verification)
3. **Remove IDs** - Skip unique identifiers (lose precision)

Each improvement would sacrifice a design principle.

---

## Summary

The low ratio reflects Calor's design choice to prioritize explicit semantics over token efficiency.

This is not a flaw but a tradeoff. The value of explicitness must be weighed against the cost of lower density.

[See current benchmark results →](/calor/benchmarking/results/)

---

## Next

- [Results](/calor/benchmarking/results/) - Summary of all metrics
- [Tradeoffs](/calor/philosophy/tradeoffs/) - Understanding the design decisions
