# Experiments Registry

This directory holds the hypothesis registry for the Calor-native type-system
research plan (`docs/plans/calor-native-type-system-v2.md`).

## `registry.json` — append-only

The registry is **append-only**. Once an entry is committed, no field of that entry
may change — ever.

- To record a correction, reversal, or promotion, **add a new entry** whose
  `supersedes` field points at the predecessor. Do not edit the predecessor.
- Removing an entry is also forbidden.
- Field-level modifications are rejected by CI (see
  `.github/workflows/experiment-registry-tamper-check.yml`). Client-side
  pre-commit hook in `.githooks/pre-commit-registry` catches the same violations
  for fast feedback — CI is the authoritative enforcement layer.

## Entry schema

See `src/Calor.Compiler/Experiments/RegistryEntry.cs` for the authoritative schema.
Key fields:

| Field | Required at | Notes |
|---|---|---|
| `id` | Always | Unique; `TIER1A-short-name` or `TIER1A-short-name-stage2`, etc. |
| `tag` | Stage 1 | `Dataflow` \| `Pattern` \| `Elaboration` \| `TypeSystem` \| `Codegen` |
| `hypothesis` | Stage 1 | Plain-English claim |
| `tuple_code_class` | Stage 1 | Part of two-kill identity tuple |
| `tuple_effect_direction` | Stage 1 | `up` \| `down` — part of tuple |
| `status` | Always | Lifecycle state (see below) |
| `supersedes` | When correcting | Predecessor entry `id`, or null |
| `hold_owner` | Held entries | Auto-drop if missing (§4.4) |
| `quarterly_review_due` | Held entries | ISO date; stale-holds uses this |
| `metric_change_rationale` | Metric-change re-proposals | Required for two-kill anti-evasion (§4.5) |
| `commit_sha` | CI-filled | Do not set manually |
| `merged_at` | CI-filled | Do not set manually — ISO timestamp from GitHub API |

## Lifecycle statuses

1. `pre-registered-stage-1` — design locked, threshold still TBD
2. `pre-registered-stage-2` — threshold set post-variance
3. `behind-flag` — implementation landed, awaiting benchmark
4. `promoted` — passed the gate, graduating per §4.6
5. `held` — gate inconclusive or failed with recoverable cause
6. `dropped` — failed with evidence contradicting hypothesis, or held twice

## Adding an entry

1. File the registry entry **in its own PR** (separate from the feature implementation).
2. Omit `commit_sha` and `merged_at` — CI fills these post-merge.
3. Reference the Stage 0 triage issue in the PR body (Stage 1 entries only).
4. Wait for CI approval; merge; the feature PR references the merged registry commit.

## Querying

Use the CLI:

```bash
calor evaluation registry --query current-state --hypothesis TIER1A-flow-option-tracking
calor evaluation registry --query stale-holds
calor evaluation registry --query two-kill-risk --tuple "Dataflow/unwrap-sites/up"
calor evaluation registry --query held-owned-by --user alice
calor evaluation registry --query audit-trail --hypothesis TIER1A-flow-option-tracking
```

## Validating locally

```bash
# Compare current HEAD against main:
git show main:docs/experiments/registry.json > /tmp/base.json
calor evaluation registry-validate --base-file /tmp/base.json --head-file docs/experiments/registry.json
```

Optional pre-commit hook: see `.githooks/README.md` for installation.
