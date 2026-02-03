---
layout: default
title: Token Economics
parent: Benchmarking
nav_order: 8
---

# Token Economics Metric

**Category:** Token Economics
**Result:** C# wins (0.67x)
**What it measures:** Tokens required to represent equivalent logic

---

## Overview

The Token Economics metric measures how many tokens each language requires to express the same logic. Fewer tokens means less context window usage.

---

## Why It Matters

LLM context windows are finite. Every token used for code is a token not available for:
- Instructions
- Examples
- Conversation history
- Reasoning

Token efficiency directly impacts what agents can accomplish.

---

## How It's Measured

### Tokenization

Simple tokenization splits on:
- Whitespace
- Punctuation
- Symbols

```csharp
foreach (var ch in source)
{
    if (char.IsWhiteSpace(ch))
    {
        if (inToken) { tokens++; inToken = false; }
    }
    else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
    {
        if (inToken) { tokens++; inToken = false; }
        tokens++; // Punctuation is its own token
    }
    else { inToken = true; }
}
```

### Metrics Collected

| Metric | Description |
|:-------|:------------|
| Token count | Number of tokens |
| Character count | Non-whitespace characters |
| Line count | Number of lines |
| Token ratio | C# tokens / Calor tokens |
| Char ratio | C# chars / Calor chars |
| Line ratio | C# lines / Calor lines |

### Composite Score

```
CompositeAdvantage = (TokenRatio × CharRatio × LineRatio)^(1/3)
```

Geometric mean of all three ratios.

---

## Detailed Comparison

### Hello World

**Calor:**
```
§M[m001:Hello]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §P "Hello World"
§/F[f001]
§/M[m001]
```
- Tokens: ~25
- Lines: 7

**C#:**
```csharp
class Program
{
    static void Main()
    {
        Console.WriteLine("Hello World");
    }
}
```
- Tokens: ~15
- Lines: 7

**Ratio:** 25/15 = **1.67x** (Calor uses more)

### FizzBuzz

**Calor:**
```
§M[m001:FizzBuzz]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]
```
- Tokens: ~80
- Lines: 13

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
- Tokens: ~50
- Lines: 7

**Ratio:** 80/50 = **1.60x** (Calor uses more)

### Function with Contract

**Calor:**
```
§F[f001:Divide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q (!= b 0)
  §S (>= result 0)
  §R (/ a b)
§/F[f001]
```
- Tokens: ~40
- Lines: 8

**C#:**
```csharp
public static int Divide(int a, int b)
{
    if (b == 0) throw new ArgumentException();
    Debug.Assert(a / b >= 0);
    return a / b;
}
```
- Tokens: ~35
- Lines: 6

**Ratio:** 40/35 = **1.14x** (Closer when contracts matter)

---

## Token Breakdown by Element

| Element | Calor Tokens | C# Tokens |
|:--------|:------------|:----------|
| Module declaration | 5-7 | 3-4 |
| Function declaration | 8-10 | 6-8 |
| Parameter | 4 | 3 |
| Return type | 2 | 1-2 |
| Effect declaration | 3-5 | 0 (implicit) |
| Contract | 4-6 | 5-10 |
| Return statement | 4+ | 3+ |
| Closing tags | 2-4 | 1 |

---

## Why Calor Uses More Tokens

### 1. Explicit Tags

```
§M[m001:Name]    // Module requires tag + ID + name
§/M[m001]        // Closing tag required

// vs C#
namespace Name   // Just keyword + name
```

### 2. Effect Declarations

```
§E[cw,fr,net]    // Explicit effects

// C#: No equivalent - effects are implicit
```

### 3. Closing Tags

Every structure requires explicit closing:
```
§F[f001]...§/F[f001]
§L[for1]...§/L[for1]
§IF[if1]...§/I[if1]
```

### 4. Contract Syntax

Contracts add lines:
```
§Q (>= x 0)
§S (>= result 0)
```

Though C# contracts can be verbose too:
```csharp
Contract.Requires(x >= 0);
Contract.Ensures(Contract.Result<int>() >= 0);
```

---

## V2 Improvement

V1 syntax was extremely verbose:

```
// V1 (legacy)
§OP[kind=add]
  §REF[name=a]
  §REF[name=b]
§/OP
```
- Tokens: ~15 for addition

```
// V2 (current)
(+ a b)
```
- Tokens: ~4 for addition

**Improvement:** ~40% token reduction.

---

## Context Window Impact

| Context Size | Calor Programs | C# Programs |
|:-------------|:--------------|:------------|
| 4K tokens | ~40-50 | ~65-80 |
| 8K tokens | ~80-100 | ~130-160 |
| 32K tokens | ~320-400 | ~520-640 |

---

## When Token Efficiency Matters Less

Token efficiency is less critical when:

1. **Context is plentiful** - Large context windows reduce pressure
2. **Comprehension matters** - Worth extra tokens for clarity
3. **Contracts add value** - Security/correctness worth the cost
4. **Precision editing** - IDs save tokens in edit instructions

---

## Interpretation

The 0.67x ratio means Calor uses ~1.5x more tokens than C# on average.

This is the price of:
- Explicit structure
- First-class contracts
- Effect declarations
- Closing tags

The V2 syntax improved this from ~0.4x (V1 was ~2.5x more tokens).

---

## Next

- [Information Density](/calor/benchmarking/metrics/information-density/) - Semantic content per token
