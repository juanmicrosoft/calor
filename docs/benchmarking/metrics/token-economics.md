---
layout: default
title: Token Economics
parent: Benchmarking
nav_order: 8
---

# Token Economics Metric

**Category:** Token Economics
**Result:** Calor wins (composite) — [see current ratio](/calor/benchmarking/results/)
**What it measures:** Composite compactness — the geometric mean of the token, character, and line ratios between equivalent Calor and C# programs

---

## Overview

The Token Economics metric measures how compact each language is at expressing
the same logic. It is a **composite** of three dimensions — token count,
non-whitespace character count, and line count — combined as a geometric mean so
that no single dimension dominates. Greater compactness means less context-window
usage and lower edit cost.

> **Metric correction (v0.6.5, #668):** earlier releases reported this category as
> "C# wins" using a **raw token count only**. The calculator computed the composite
> (token × char × line) and then discarded it, reporting the token ratio alone. That
> bug is fixed: the category now reports the composite it always computed. On the
> representative benchmark corpus the composite **favors Calor** (currently **1.42×**),
> because Calor's character- and line-level compactness outweighs its higher raw token
> count. Raw token count *alone* — especially on tiny programs — still tends to favor
> C# (see the per-program examples below, which illustrate that single dimension).

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

Geometric mean of all three ratios. **This composite is the value the metric
reports** (as of v0.6.5 / #668). Because the underlying counts are deterministic,
the composite has no run-to-run variance — its 95% confidence interval equals its
point estimate.

---

## Detailed Comparison

> The per-program ratios below report the **raw token dimension only** — one of the
> three inputs to the composite. They show that on small programs Calor often uses
> *more* raw tokens than C#. The composite metric additionally credits Calor's
> character- and line-level compactness, which is why the aggregate category favors
> Calor on the representative corpus.

### Hello World

**Calor:**
```
§M{m001:Hello}
  §F{f001:Main:pub}
    §O{void}
    §E{cw}
    §P "Hello World"
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
§M{m001:FizzBuzz}
  §F{f001:Main:pub}
    §O{void}
    §E{cw}
    §L{for1:i:1:100:1}
      §IF{if1} (== (% i 15) 0) → §P "FizzBuzz"
      §EI (== (% i 3) 0) → §P "Fizz"
      §EI (== (% i 5) 0) → §P "Buzz"
      §EL → §P i
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
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (>= result 0)
  §R (/ a b)
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
| Block indentation | 0 lexical tokens | 0 lexical tokens |

---

## Why Calor Uses More Raw Tokens

These factors raise Calor's **raw token count** (one of the three composite
dimensions). They are outweighed in the composite by Calor's character- and
line-level compactness on the representative corpus.

### 1. Explicit Tags

```
§M{m001:Name}    // Module requires tag + ID + name

// vs C#
namespace Name   // Just keyword + name
```

### 2. Effect Declarations

```
§E{cw,fs:r,net:rw}    // Explicit effects

// C#: No equivalent - effects are implicit
```

### 3. Block Indentation

Nested structures use indentation instead of structural closers:
```
§F{f001}
  §L{for1}
    §IF{if1}
      §P i
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

On the representative benchmark corpus the **composite** Token Economics ratio
favors Calor (currently **1.42×**): Calor is more compact overall once character
and line counts are included alongside tokens.

Calor does pay a **raw token** premium for:
- Explicit structure
- First-class contracts
- Effect declarations
- `§`-sigil markers

That premium is real but is outweighed by Calor's character/line compactness in
the composite. Lisp-style expressions help keep the raw-token premium manageable.

---

## Next

- [Information Density](/calor/benchmarking/metrics/information-density/) - Semantic content per token
