#!/usr/bin/env bash
# ============================================================================
# E1a analysis: per-pair and pooled iterations-to-green means, ratios, and
# the pre-registered decision rule (machine-zone.md §12.4):
#   H1 SURVIVES iff (R_base - R_ex) / (R_base - 1) < 0.30, with R_base > 1.
#   If pooled R_base < 1.3: "baseline regression to mean — 2.7x not
#   reproduced" is its own verdict.
# Censored runs (never green within budget) carry itg = budget+1 = 11, per
# the harness metric definition. Invalid-after-retry-cap slots are included
# the same way (taskSuccess=false, censored) and reported separately.
# ============================================================================
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

find . -name result.json -exec cat {} + | jq -s '
  group_by(.pair) | map({
    pair: .[0].pair,
    arms: (group_by(.arm) | map({
      arm: .[0].arm,
      n: length,
      meanItg: (map(.iterationsToGreen) | add / length),
      censored: (map(select(.censored)) | length),
      invalid: (map(select(.invalid)) | length),
      successRate: ((map(select(.taskSuccess)) | length) / length)
    }))
  })' > per-pair.json

find . -name result.json -exec cat {} + | jq -s '
  def mean(f): (map(f) | add / length);
  {
    perPair: (group_by(.pair) | map({
      pair: .[0].pair,
      csharp:   (map(select(.arm=="csharp").iterationsToGreen)         | {n: length, mean: (add/length)}),
      calor:    (map(select(.arm=="calor").iterationsToGreen)          | {n: length, mean: (add/length)}),
      exemplar: (map(select(.arm=="calor+exemplar").iterationsToGreen) | {n: length, mean: (add/length)})
    } | . + {
      R_base: (.calor.mean / .csharp.mean),
      R_ex:   (.exemplar.mean / .csharp.mean)
    } | . + {
      reductionFraction: (if .R_base > 1 then ((.R_base - .R_ex) / (.R_base - 1)) else null end)
    })),
    pooled: ({
      csharp:   (map(select(.arm=="csharp").iterationsToGreen)         | {n: length, mean: (add/length)}),
      calor:    (map(select(.arm=="calor").iterationsToGreen)          | {n: length, mean: (add/length)}),
      exemplar: (map(select(.arm=="calor+exemplar").iterationsToGreen) | {n: length, mean: (add/length)})
    } | . + {
      R_base: (.calor.mean / .csharp.mean),
      R_ex:   (.exemplar.mean / .csharp.mean)
    } | . + {
      reductionFraction: (if .R_base > 1 then ((.R_base - .R_ex) / (.R_base - 1)) else null end)
    })
  } | . + {
    verdict: (
      if .pooled.R_base < 1.3 then
        "baseline regression to mean — 2.7x not reproduced"
      elif .pooled.reductionFraction < 0.30 then
        "H1 SURVIVES (exemplar reduced the ratio by < 30%)"
      else
        "H1 KILLED (exemplar reduced the ratio by >= 30%)"
      end)
  }' > analysis.json

cat analysis.json
