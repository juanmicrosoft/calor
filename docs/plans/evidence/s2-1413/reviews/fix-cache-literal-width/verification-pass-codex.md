# S2 #1413 fix-cache-literal-width — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: APPROVE

**NIT — Stale versions in the manifest note.** [eng/test-manifest.json:10](eng/test-manifest.json:10) still describes “1.18 eviction” and “1.19 format bump.” Update these to **1.19 rejection** and **1.20 format bump**. This affects documentation only.

Confirmed:

- The round-3 NIT is fixed: the comment correctly names `p\uFFFD` as the prime and `p\uD800` as the probe (test lines 180–191).
- All reviewed repairs and regression coverage remain present. The merge preserves #1492’s UserHome, IsolatedSolver, Windows string handling, and 1.19 rationale. Production format is **1.20**; the old-format test stamps **1.19**.
- The manifest changes only the compiler total, **12587 → 12602**, and appends its note. The added class contains **8 facts + 7 theory rows = 15 cases**; expected skips remain unchanged.
- CHANGELOG retains one Unreleased `### Fixed` section, the #1492 entry, and the accurate 1.20 invalidation entry.
- `python3 -B scripts/check_test_quality.py` passed.

No new code defect identified. Compared head `cf67d729` against its #1492 parent `0142438f` and the supplied diff; local `origin/main` is stale. Dotnet was not run.

## Response

NIT fixed: a zero-delta manifest note records that the eviction case rejects 1.19 entries and the bump is 1.20 (the manifest is edited only through the bump script, which appends).
