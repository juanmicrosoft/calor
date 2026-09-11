<!-- THIS FILE IS AUTO-GENERATED. DO NOT EDIT MANUALLY. -->
<!-- Generated from website/public/data/benchmark-results.json by CI/CD -->

---
layout: default
title: Results
parent: Benchmarking
nav_order: 2
---

# Benchmark Results

This fixed-source artifact contains 217 paired programs
and 8 deterministic metrics. It was recorded on
2026-09-11 from source `be488238` with
30 repetitions.

**Legacy composite direction-normalized ratio:** 1.32x

Each metric is normalized so values above 1 favor Calor and values below 1
favor C#. Lower-is-better metrics invert their raw score ratio. These static
calculator outputs do not establish a language, coding-agent productivity,
correctness, or safety advantage.

## Metric Ratios

| Metric | Direction-normalized ratio | Favored language | Reported 95% interval |
|:-------|:---------------------------|:-----------------|:----------------------|
| Token Economics | 1.42x | Calor | [1.417, 1.417] |
| Generation Accuracy | 1.02x | Calor | [1.017, 1.017] |
| Comprehension | 1.84x | Calor | [1.836, 1.836] |
| Edit Precision | 1.36x | Calor | [1.358, 1.358] |
| Error Detection | 1.49x | Calor | [1.492, 1.492] |
| Information Density | 0.97x | C# | [0.970, 0.970] |
| Refactoring Stability | 1.38x | Calor | [1.378, 1.378] |
| Correctness | 1.29x | Calor | [1.295, 1.295] |

## Parse Checks

- Calor parser accepted: 217
- Roslyn syntax parser accepted: 217
- Parse acceptance does not establish generated-code build success or runtime correctness.

## Interpretation Limits

- The repetitions repeat deterministic observations over a fixed corpus; they
  are not independent program samples.
- The source pairs are not all behaviorally equivalent.
- Direction-normalized ratios are not raw Calor/C# score ratios.
- No agent executed these programs as part of this static artifact.

## Next

- [Website methodology](../../website/content/benchmarking/methodology.mdx)
- [Website results and provenance](../../website/content/benchmarking/results.mdx)
