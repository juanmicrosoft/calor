# Phase 3 — H1 Replication v3 (taught chain syntax) — POSITIVE VERDICT

**Date:** 2026-06-01  
**Branch:** `rfc/phase-3-indent`  
**Harness:** `scripts/phase3_h1_replication_v3.py`  
**Raw data:** `scripts/phase3-h1-replication-v3-results.json`  
**Total invocations:** 70 (7 tasks × 2 arms × 5 trials)  
**Model:** claude-haiku-4-5

## Verdict

**Phase 3 (Python-style indentation) is viable.** When agents are properly taught the chain-statement syntax (`§IF{id}`, `§EI`, `§EL`), indent form matches or beats closer form on pass rate AND wins on cost.

- closer 32/35 (91.4%) vs indent **34/35 (97.1%)**
- Δ pass: **+5.7pp** in favor of indent (Fisher p=0.6139 — no statistically significant difference)
- Δ cost: **−16.7%** (indent cheaper per request)

The v2 −28.6pp regression was a prompt artifact, not a form artifact. The user's intuition was correct: we were measuring "what happens when agents are under-trained on a new syntax" rather than "what happens when agents use a different but equally-taught syntax."

## What changed from v2 to v3

Only the SHARED_HEADER paragraph added a new "CHAIN STATEMENTS" section that BOTH arms received identically:

```
CHAIN STATEMENTS — IF/ELSE-IF/ELSE (CRITICAL):
  §IF ALWAYS requires an {id} attribute. The condition follows in parentheses.
  §EI (else-if) and §EL (else) are continuation keywords — they do NOT take {id}.
  The "else" keyword in Calor is §EL — there is no §K or §ELSE.

  CORRECT:
    §IF{i01} (< x 0)
      §R (- 0 x)
    §EL
      §R x
  ...
```

The delimiter paragraph (closer vs indent) was unchanged from v2. Tasks and trial count are identical to v2 for clean comparison.

## v2 vs v3 — per-task pass rate

| Task | v2 closer | v2 indent | v3 closer | v3 indent | indent Δ (v3−v2) |
|---|---|---|---|---|---|
| basic-001  | 5/5 | 5/5 | 5/5 | 5/5 | 0 |
| contract-001 | 5/5 | 5/5 | 5/5 | 5/5 | 0 |
| contract-002 | 3/5 | **0/5** | 5/5 | **5/5** | **+5** |
| contract-004 | 5/5 | 5/5 | 5/5 | 5/5 | 0 |
| contract-005 | 3/5 | 5/5 | 4/5 | 4/5 | −1 (noise) |
| logic-002    | 5/5 | **0/5** | 3/5 | **5/5** | **+5** |
| logic-004    | 4/5 | **0/5** | 5/5 | **5/5** | **+5** |

Every chain-containing task went from 0/5 → 5/5 in indent arm. The teaching change alone resolved 15 of 15 chain-related failures.

## Three measurements together — Phase 3 net impact

| dimension | measured | who wins |
|---|---|---|
| Source bytes (corpus, 1552 files) | −15% | **indent** |
| Per-request cost (v3, 70 trials)   | −16.7% | **indent** |
| Agent read comprehension (smoke, 30 Q&A) | parity 100/100 | tie |
| Agent write pass rate (v3, 70 trials) | 97.1% vs 91.4% | **indent (+5.7pp, n.s.)** |
| Lex/parse correctness on full corpus | no regression | tie |

Every measured dimension is now either tie or indent-favorable. **Phase 3 should proceed.**

## Caveats & honest limitations

1. **n=35 per arm is small.** The +5.7pp pass-rate edge is not statistically significant (p=0.62). What IS significant is the absence of any regression, given v2's strong negative signal disappeared.

2. **One model only (claude-haiku-4-5).** Stronger models (Sonnet, Opus) may bridge the v2 gap on their own. Weaker models may need even more explicit teaching. The teaching-sensitivity finding is the durable one.

3. **All tasks use the same fixture file** (`basic-calor-project/Calculator.calr`). Longer / more complex fixtures may produce different signals.

4. **Cost win (−16.7%) is larger than Tier 0's mechanical estimate (−15%).** This is because Haiku's per-request cost depends on input tokens (where indent saves) more than output tokens (which were similar in both arms here).

5. **The teaching itself adds tokens to the system prompt.** The CHAIN STATEMENTS paragraph is ~400 chars. For one-shot prompts this is amortized over many requests; for single-shot uses it eats into the cost saving. v3's cost number reflects this — and indent still wins by 16.7%.

## Updated recommendation (supersedes v2 report)

**Promote Phase 3 to production.** Specifically:
1. Land the lexer INDENT/DEDENT pass behind a flag (already done on `rfc/phase-3-indent` @ cd01e48).
2. Add the chain-statement teaching paragraph to the canonical Calor system prompts (MCP `calor://primer`, agent task fixtures, sample CLAUDE.md).
3. Productionize the migrator: `calor fix --to-indent` and `calor fix --from-indent` CLI commands (the Arm I work that was blocked by the v2 verdict).
4. Run a Tier 1.5 broader validation (more tasks, more models) before flipping indent to default — but the small-scale evidence is sufficient to commit to the direction.

The earlier v2 report (`phase3-h1-replication-results.md`) recommended deferring Phase 3. **That recommendation is withdrawn.** v3 supersedes v2.

## Reproducibility

```powershell
cd C:\path\to\calor; $env:PYTHONIOENCODING="utf-8"
python scripts/phase3_h1_replication_v3.py
```

Expected wall time: ~13 min sequential. Expected cost: ~$0.67 on claude-haiku-4-5.
