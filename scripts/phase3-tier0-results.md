# Phase 3 Tier 0 — source-size delta (closer-mode vs indent-mode)

**Corpus:** 1552 `.calr` files

## Aggregate (sum across corpus)

| Metric | Closer-mode | Indent-mode | Reduction |
|---|---:|---:|---:|
| Lines | 31,120 | 24,539 | **21.15%** |
| Bytes | 548,489 | 473,398 | **13.69%** |
| Tokens (proxy) | 90,473 | 77,419 | **14.43%** |

Total closer lines removable: **6,581**

## Per-file distribution

| Metric | p10 | median | p90 |
|---|---:|---:|---:|
| Lines reduction % | 16.67 | 22.22 | 30.0 |
| Bytes reduction % | 9.98 | 14.62 | 22.22 |
| Tokens reduction % | 10.1 | 16.0 | 25.45 |

- Files with 0% reduction: **0** / 1552
- Files with ≥10% lines reduction: **1552** / 1552
- Files with ≥20% lines reduction: **1120** / 1552
