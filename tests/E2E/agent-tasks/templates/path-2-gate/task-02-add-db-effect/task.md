# task-02: Add `db` effect to function and 2 transitive callers

## Prompt to the agent

The function `LoadUserSettings` in `setup/Db.calr` performs a database
read but does not declare the `db` effect. Its caller
`PrepareDashboard` (in `setup/Service.calr`) calls
`LoadUserSettings` and is itself called by `RenderHome` (in
`setup/Web.calr`).

Add the `db` effect to all three functions so the effect propagates
correctly through the call chain. Do not change the function bodies
or signatures otherwise.

## Acceptance

- All three functions declare `§E{db}` (possibly alongside other
  effects already present).
- No other function in the three files acquires `§E{db}` accidentally.

## Why this stresses the ID system

Cross-file symbol references: in Phase 2, callers reference compact
IDs of `LoadUserSettings` and `PrepareDashboard`. Editing the effect
declaration on three different functions in three files exercises
multi-file edit patterns.

## Multi-edit qualifier

3 files × 1 edit per file = 3 files, 3 sites. Meets the "≥3 files OR
≥5 sites" criterion via the ≥3 files branch.
