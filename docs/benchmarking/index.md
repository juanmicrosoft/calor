---
layout: default
title: Benchmarking
nav_order: 5
has_children: true
permalink: /benchmarking/
---

# Benchmarking

Calor is evaluated against C# across 7 metrics designed to measure what matters for AI coding agents.

---

## The Seven Metrics

| Category | What It Measures | Why It Matters |
|:---------|:-----------------|:---------------|
| [**Comprehension**](/calor/benchmarking/metrics/comprehension/) | Structural clarity, semantic extractability | Can agents understand code without deep analysis? |
| [**Error Detection**](/calor/benchmarking/metrics/error-detection/) | Bug identification, contract violation detection | Can agents find issues using explicit semantics? |
| [**Edit Precision**](/calor/benchmarking/metrics/edit-precision/) | Targeting accuracy, change isolation | Can agents make precise edits using unique IDs? |
| [**Generation Accuracy**](/calor/benchmarking/metrics/generation-accuracy/) | Compilation success, structural correctness | Can agents produce valid code? |
| [**Task Completion**](/calor/benchmarking/metrics/task-completion/) | End-to-end success rates | Can agents complete full tasks? |
| [**Token Economics**](/calor/benchmarking/metrics/token-economics/) | Tokens required to represent logic | How much context window does code consume? |
| [**Information Density**](/calor/benchmarking/metrics/information-density/) | Semantic elements per token | How much meaning per token? |

---

## Summary Results

| Category | Calor vs C# | Winner |
|:---------|:-----------|:-------|
| Comprehension | **1.33x** | Calor |
| Error Detection | **1.19x** | Calor |
| Edit Precision | **1.15x** | Calor |
| Generation Accuracy | 0.94x | C# |
| Task Completion | 0.93x | C# |
| Token Economics | 0.67x | C# |
| Information Density | 0.22x | C# |

**Pattern:** Calor wins on comprehension and precision metrics. C# wins on efficiency metrics.

---

## Key Insight

Calor excels where explicitness matters:
- **Comprehension** (1.33x) - Explicit structure aids understanding
- **Error Detection** (1.19x) - Contracts surface invariant violations
- **Edit Precision** (1.15x) - Unique IDs enable targeted changes

C# wins on token efficiency:
- **Token Economics** (0.67x) - Calor's explicit syntax uses more tokens
- **Information Density** (0.22x) - C# packs more per token

This reflects a fundamental tradeoff: **explicit semantics require more tokens but enable better agent reasoning**.

---

## Learn More

- [Methodology](/calor/benchmarking/methodology/) - How benchmarks work
- [Results](/calor/benchmarking/results/) - Detailed results table
- [Individual Metrics](/calor/benchmarking/metrics/comprehension/) - Deep dive into each metric
