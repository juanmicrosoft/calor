# v0.24 evidence archives (orphan branch, never merged)

This orphan branch holds the large evidence files for gate C2 (#1424) of the Calor 0.24 evidence
contract. They are kept here so that `main` does not carry them. The branch is never merged.

Each file sits at the same relative path it would have on `main`, under
`docs/plans/evidence/c2-1424/`. On `main`, `docs/plans/evidence/c2-1424/archives.json` lists
every file stored here with its SHA-256, its size, and this branch's commit SHA.

To verify one: `git show <commit>:<path> | shasum -a 256`, then compare with `archives.json`.

Regeneration 2 adds its archives here the same way, as new files in a new commit.
