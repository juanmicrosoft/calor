---
layout: default
title: Benchmarking
nav_order: 5
has_children: true
permalink: /benchmarking/
---

# Benchmarking

Calor is evaluated against C# across 7 static metrics plus an agent refactoring benchmark designed to measure what matters for AI coding agents.

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

## Summary

Calor wins on comprehension and precision metrics. C# wins on efficiency metrics.

**Where Calor excels:**
- **Comprehension** - Explicit structure aids understanding
- **Error Detection** - Contracts surface invariant violations
- **Edit Precision** - Unique IDs enable targeted changes
- **Refactoring Stability** - Structural IDs preserve refactoring intent

**Where C# wins:**
- **Token Economics** - Calor's explicit syntax uses more tokens
- **Information Density** - C# packs more per token

This reflects a fundamental tradeoff: **explicit semantics require more tokens but enable better agent reasoning**.

[See full benchmark results →](/calor/benchmarking/results/)

---

## Learn More

- [Methodology](/calor/benchmarking/methodology/) - How benchmarks work
- [Results](/calor/benchmarking/results/) - Detailed results table
- [Individual Metrics](/calor/benchmarking/metrics/comprehension/) - Deep dive into each metric
- [Agent Refactoring Benchmark](/calor/benchmarking/agent-refactoring/) - Real-world refactoring tests with Claude Code
