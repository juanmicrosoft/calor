#!/usr/bin/env python3
"""Frozen calculator for r1-decision-rules/v1 section 7.4 (method-independent
necessary cluster count for the +1 pp safety non-inferiority gate).

For subject-arm per-cluster serious-escape probability c under the alternative
(difference delta_alt = c - c_comp) and the null configuration that shifts only
the subject arm to c + (margin - delta_alt), the Neyman-Pearson most powerful
level-alpha test of that simple null against that simple alternative bounds the
power of ANY valid level-alpha test of the composite null at that alternative.
Each source cluster contributes one independent binary observation per arm
(Draft v3 section 5.1 counting rule). n_min(c) is the smallest n at which the MP
power reaches the target; N_MIN is the minimum of n_min(c) over the frozen grid.

Standard library only. Usage: python3 r1-safety-nmin.py
"""
import math

ALPHA = 0.025          # per positive branch
TARGET_POWER = 0.80
MARGIN = 0.01          # +1 percentage point
DELTA_ALT = -0.01      # most-favorable registered safety alternative
# A serious escape requires an accepted-but-not-correct artifact, so under the
# most-favorable registered scenario (subject correctly accepted fraction 0.95)
# the subject-arm escape probability c is at most 1 - 0.95 = 0.05.
C_MAX = 0.05
GRID = [i / 1000 for i in range(0, int(C_MAX * 1000) + 1)]  # step 0.001


def pmf(k, n, p):
    if p <= 0.0:
        return 1.0 if k == 0 else 0.0
    if p >= 1.0:
        return 1.0 if k == n else 0.0
    return math.exp(math.lgamma(n + 1) - math.lgamma(k + 1) - math.lgamma(n - k + 1)
                    + k * math.log(p) + (n - k) * math.log1p(-p))


def mp_power(n, c, shift):
    """Randomized MP test of p0 = c + shift vs p1 = c; rejects small counts."""
    p0 = c + shift
    cum0 = 0.0
    cum1 = 0.0
    for k in range(n + 1):
        m0 = pmf(k, n, p0)
        m1 = pmf(k, n, c)
        if cum0 + m0 > ALPHA:
            gamma = (ALPHA - cum0) / m0 if m0 > 0 else 0.0
            return cum1 + gamma * m1
        cum0 += m0
        cum1 += m1
    return cum1


def n_min(c, shift, cap=20000):
    lo, hi = 1, cap
    if mp_power(hi, c, shift) < TARGET_POWER:
        return None
    # Power of a discrete test need not be monotone in n, so bisection only
    # proposes a candidate; a linear scan below it confirms the minimum.
    while lo < hi:
        mid = (lo + hi) // 2
        if mp_power(mid, c, shift) >= TARGET_POWER:
            hi = mid
        else:
            lo = mid + 1
    n = lo
    # Linear confirmation: no smaller n reaches the target.
    for m in range(1, n):
        if mp_power(m, c, shift) >= TARGET_POWER:
            return m
    return n


def main():
    shift = MARGIN - DELTA_ALT  # 0.02
    results = []
    for c in GRID:
        if c + shift > 1.0:
            continue
        results.append((n_min(c, shift), c))
    valid = [r for r in results if r[0] is not None]
    best = min(valid)
    print(f"alpha={ALPHA} power={TARGET_POWER} margin={MARGIN} delta_alt={DELTA_ALT}")
    print(f"N_MIN={best[0]} attained at c={best[1]}")
    for n, c in results[::10]:
        print(f"  c={c}: n_min={n}")


if __name__ == "__main__":
    main()
