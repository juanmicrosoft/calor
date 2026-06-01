# Phase 3 — H1 Replication (v2) — Equalized prompts, 7 tasks, 5 trials/cell

**Date:** 2025-11-21  
**Branch:** `rfc/phase-3-indent`  
**Harness:** `scripts/phase3_h1_replication.py`  
**Raw data:** `scripts/phase3-h1-replication-results.json`  
**Total invocations:** 70 (7 tasks × 2 arms × 5 trials)  
**Model:** claude-haiku-4-5

## Verdict

**Phase 3 (Python-style indentation) HURTS agent write performance.**

- Aggregate pass rate: **closer 30/35 (85.7%) vs indent 20/35 (57.1%)**
- Δ pass: **−28.6 percentage points AGAINST indent**
- Fisher's exact (two-sided) p = **0.0161** — significant at α = 0.05
- Δ cost: −8.7% (indent still slightly cheaper per request, ~at the previously-measured Tier-0 boundary)

## Comparison with v1 micro-smoke

| Metric | v1 (3 tasks, 3 trials, asymmetric prompts) | v2 (7 tasks, 5 trials, equalized prompts) |
|---|---|---|
| Indent pass rate | 67% (6/9) | **57.1% (20/35)** |
| Closer pass rate | 33% (3/9) | **85.7% (30/35)** |
| Δ (indent − closer) | **+33pp** | **−28.6pp** |
| contract-001 indent | 3/3 (100%) | 5/5 (100%) |
| contract-001 closer | 0/3 (0%) | **5/5 (100%)** |

**The v1 signal was a prompt artifact.** With both arms told that `§Q`/`§S` take Lisp expressions (without braces), the closer-arm contract-001 failure mode (`§Q{x >= 0}`) disappears completely — going from 0/3 to 5/5. The +33pp v1 advantage came not from indent's form benefits but from v1's indent-arm prompt incidentally being more helpful.

## Per-task breakdown (v2)

| Task | Has §IF/§EL chain? | Closer | Indent | Δ |
|---|---|---|---|---|
| basic-001 (Multiply, control) | no | 5/5 | 5/5 | 0pp |
| contract-001 (SquareRoot, §Q) | no | 5/5 | 5/5 | 0pp |
| contract-004 (SafeDivide, §Q+§S) | no | 5/5 | 5/5 | 0pp |
| contract-005 (SumToN, §Q+§S, Gauss formula) | no | 3/5 | 5/5 | **+40pp indent** |
| contract-002 (Abs, §S) | **yes** | 3/5 | **0/5** | **−60pp** |
| logic-002 (Clamp, §Q+§S, nested §IF/§EL) | **yes (nested)** | 5/5 | **0/5** | **−100pp** |
| logic-004 (Max, §S, §IF/§EI) | **yes** | 4/5 | **0/5** | **−80pp** |

**The split is perfect.** Every task that requires the agent to write `§IF`/`§EI`/`§EL` chains fails catastrophically in indent form. Every task without branches is parity or favors indent.

## Root cause: indent removes a memory aid for required IDs

Calor requires every `§IF` opener to have an `{id}` attribute (`Calor0102: Missing required attribute 'id' on IF`). In closer form, the agent sees that closers reference the opener id (`§/I{i01}` pairs with `§IF{i01}`), so it remembers to put the id on the opener. In indent form there is no closer — so the agent writes `§IF (< x 0)` and the parser rejects it.

Failure mode example (logic-004, indent trial 1 — verbatim model output):

```
§F{f003:Max:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §S (>= result a)
  §S (>= result b)
  §S (|| (== result a) (== result b))
  §IF (>= a b)         ← MISSING {i01}
    §R a
  §EI                   ← MISSING (cond) and {i02}
    §R b
```

Compiler errors:
```
Missing required attribute 'id' on IF
Expected expression but found Return
Expected statement but found Identifier
```

Same failure pattern across all three "indent loses big" tasks. In logic-002 the agent also drops the ids on nested §IFs. In contract-002 the agent additionally invents a non-existent `§K` keyword (it was unsure how to spell "else") — but closer-arm got that one mostly right because the structural skeleton from the closer pattern kept it more careful.

## Cost finding

Even when the indent arm is failing, it spends only ~9% less than the closer arm — a much smaller delta than Tier 0's measured −15% input-token reduction. This is because the **output** of every successful request includes the writeback of the existing file, and Haiku's output happens to be roughly equal in length in both arms for these small files. Tier 0's cost savings do not translate proportionally to per-request agent cost on small files.

## What this means for the RFC

This is strong evidence against Phase 3 as currently designed. Three options:

### Option A — Defer Phase 3 indefinitely (recommended)
- Phase 1 already shipped the compact-id wins (~15% byte savings, +visual signal-to-noise improvements).
- The per-request token savings of Phase 3 (−9% in this measurement, −15% in Tier 0) are small relative to the −28.6pp pass-rate regression.
- Cost of broken agent writes >> savings from cheaper requests.
- Action: keep this branch (`rfc/phase-3-indent`) as a research artifact; do NOT promote to PR #625's plan; mark Phase 3 as "validated negative" in `docs/plans/2025-11-19-rfc-id-system-overhaul.md`.

### Option B — Redesign Phase 3 to make `§IF` IDs optional
- If `§IF` (and other branch openers) didn't require `{id}`, the indent arm's failure mode disappears.
- This is a deeper compiler change (parser + binder + id-scanner + diagnostic changes).
- After that change, re-run v2 — but note that even without the id issue, the agent's "made-up keyword" failure (§K instead of §EL) is independent of form.

### Option C — Ship indent form as an optional dialect, NOT default
- Power users / human authors might prefer indent form.
- Agents get closer form by default.
- Adds long-term migrator maintenance burden, doubles the language surface, and creates round-trip drift risk.

**Recommendation: Option A.** The signal is too negative (p=0.016, −28.6pp) and the failure mode is fundamental to how agents reason about paired tokens.

## What we proved (and didn't)

### Proven
1. Indent form is mechanically viable (Tier 0: −15% tokens, no lex errors).
2. Indent form does NOT degrade comprehension (off-protocol smoke: 100% in both arms).
3. With equalized prompts, indent form does NOT win on agent writes — it loses by 28.6pp, p=0.016.
4. The v1 +33pp signal was driven entirely by asymmetric prompts; once equalized, that signal flips to −28.6pp.

### Not proven (open questions)
- Whether removing the `{id}` requirement from §IF would fix the regression (Option B).
- Whether stronger models (e.g., Sonnet 4.6) would resolve the id-omission problem on their own.
- Whether longer fixtures / multi-function tasks would change the balance.
- Whether contract-005's +40pp indent win is reproducible at higher n.

## Reproducibility

To re-run from scratch:

```powershell
cd C:\path\to\calor; $env:PYTHONIOENCODING="utf-8"
python scripts/phase3_h1_replication.py
```

Expected wall time: ~15–18 min on a single connection (sequential).  
Expected total cost: ~$0.80 on claude-haiku-4-5.
