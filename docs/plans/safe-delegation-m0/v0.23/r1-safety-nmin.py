#!/usr/bin/env python3
"""Frozen calculator for r1-decision-rules/v1 section 7.4.

Method-independent necessary number of independent source clusters for the
+1 pp safety non-inferiority gate (G3.1), by a contamination argument:

  P0 = (1 - eps) * P1 + eps * Q, with Q putting D = +1 almost surely and
  eps = (MARGIN - DELTA_ALT) / (1 - DELTA_ALT), so E_P0[D] = MARGIN.
  For n i.i.d. clusters, P0^n >= (1 - eps)^n * P1^n on every event, so any
  level-ALPHA test has power at P1 of at most ALPHA / (1 - eps)^n.
  Power TARGET_POWER therefore needs n >= ln(ALPHA / TARGET_POWER) / ln(1 - eps).

The bound is uniform over every cluster distribution P1 with mean difference
DELTA_ALT, every base rate, and every within-cluster dependence structure.

Standard library only. Usage: python3 r1-safety-nmin.py
"""
import math

ALPHA = 0.025          # per positive branch
TARGET_POWER = 0.80
MARGIN = 0.01          # +1 percentage point (null: E[D] >= MARGIN)
DELTA_ALT = -0.01      # registered most-favorable safety alternative
HIST_WEIGHT_FLOOR = 0.75  # minimum historical share of primary task weight


def n_min():
    eps = (MARGIN - DELTA_ALT) / (1.0 - DELTA_ALT)
    return math.ceil(math.log(ALPHA / TARGET_POWER) / math.log(1.0 - eps)), eps


def max_infeasible_h_high(n):
    """Largest H_high with floor(H_high / 0.75) < n."""
    h = 0
    while math.floor((h + 1) / HIST_WEIGHT_FLOOR) < n:
        h += 1
    return h


def main():
    n, eps = n_min()
    print(f"alpha={ALPHA} power={TARGET_POWER} margin={MARGIN} delta_alt={DELTA_ALT}")
    print(f"eps={eps:.6f}")
    print(f"N_MIN={n}")
    print(f"SIZED_INFEASIBLE iff H_high <= {max_infeasible_h_high(n)}")


if __name__ == "__main__":
    main()
