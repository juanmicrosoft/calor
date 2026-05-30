# Phase 3 — Off-protocol comprehension micro-smoke

**NOT validation plan Tier 1.5.** Directional real-LLM signal that
upgrades Tier 0's mechanical measurement with actual model behavior.

- Model: `claude-haiku-4-5`
- Files: 5
- Questions per file: 3
- Arms: closer (original), indent (closer lines stripped via regex)
- Repeats per (file × arm): 2
- Total trials: 20 (=5 files × 2 arms × 2 repeats)

## Aggregate

`mean_body_tokens` = `input_tokens + cache_creation_tokens` (the actual NEW content the model ingested per trial; `cache_read` is the shared CLI baseline ~24K tokens that's identical for both arms).

| Arm | n_trials | n_errors | accuracy | mean_body_tokens | mean_output_tokens | mean_cost_usd | total_cost_usd |
|---|---:|---:|---:|---:|---:|---:|---:|
| closer | 10 | 0 | 1.00 | 978.20 | 607.30 | 0.0078 | 0.0784 |
| indent | 10 | 0 | 1.00 | 695.00 | 725.70 | 0.0081 | 0.0806 |

**Body-token delta (indent vs closer):** -283.2 tokens (-28.95%)

**Output-token delta (indent vs closer):** +118.4 tokens (+19.50%)

## Per-trial detail

| File | Arm | Repeat | Correct | Answers | body_tokens | out_tokens | Cost | Err |
|---|---|---:|---:|---|---:|---:|---:|---|
| violation-detected.calr | closer | 0 | 3/3 | 3 / ImpossibleContract / 2 | 9 | 493 | $0.0059 |  |
| violation-detected.calr | indent | 0 | 3/3 | 3 / ImpossibleContract / 2 | 2053 | 707 | $0.0093 |  |
| violation-detected.calr | closer | 1 | 3/3 | 3 / ImpossibleContract / 2 | 9 | 420 | $0.0056 |  |
| violation-detected.calr | indent | 1 | 3/3 | 3 / ImpossibleContract / 2 | 9 | 570 | $0.0063 |  |
| contracts.calr | closer | 0 | 3/3 | 3 / Main / 0 | 9 | 456 | $0.0059 |  |
| contracts.calr | indent | 0 | 3/3 | 3 / Main / 0 | 9 | 290 | $0.0050 |  |
| contracts.calr | closer | 1 | 3/3 | 3 / Main / 0 | 9 | 574 | $0.0065 |  |
| contracts.calr | indent | 1 | 3/3 | 3 / Main / 0 | 9 | 437 | $0.0058 |  |
| mixed-contracts.calr | closer | 0 | 3/3 | 4 / ClampPositive / 1 | 2173 | 801 | $0.0101 |  |
| mixed-contracts.calr | indent | 0 | 3/3 | 4 / ClampPositive / 1 | 9 | 713 | $0.0071 |  |
| mixed-contracts.calr | closer | 1 | 3/3 | 4 / ClampPositive / 1 | 9 | 472 | $0.0059 |  |
| mixed-contracts.calr | indent | 1 | 3/3 | 4 / ClampPositive / 1 | 9 | 552 | $0.0063 |  |
| proven-contracts.calr | closer | 0 | 3/3 | 5 / 2 / none | 2337 | 805 | $0.0105 |  |
| proven-contracts.calr | indent | 0 | 3/3 | 5 / 2 / none | 2280 | 1855 | $0.0156 |  |
| proven-contracts.calr | closer | 1 | 3/3 | 5 / 2 / none | 9 | 953 | $0.0085 |  |
| proven-contracts.calr | indent | 1 | 3/3 | 5 / 2 / none | 9 | 1169 | $0.0095 |  |
| matching.calr | closer | 0 | 3/3 | 5 / 1 / cool | 2611 | 531 | $0.0097 |  |
| matching.calr | indent | 0 | 3/3 | 5 / 1 / cool | 2554 | 504 | $0.0094 |  |
| matching.calr | closer | 1 | 3/3 | 5 / 1 / cool | 2607 | 568 | $0.0099 |  |
| matching.calr | indent | 1 | 3/3 | 5 / 1 / cool | 9 | 460 | $0.0063 |  |
