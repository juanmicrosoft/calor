# Evidence archive, 2026-10 (orphan branch, never merged)

This orphan branch holds the whole `docs/plans/evidence/` tree as it was on `main` at commit
d89ffcbc840d4c1d90ee8dd3b445a5021b559eb4, the last `main` commit before #1584 removed it. The
branch is never merged.

Layout: each top-level directory here (for example `v0.25-w1-1429/`) was
`docs/plans/evidence/<directory>/` on `main`. The branch's first commit,
bd326a76118bf546b1e85711a1b7daf8d3ddd375, keeps the original `docs/plans/evidence/` paths; the
second commit moves the same files, byte for byte, to the branch root so that links to them are
short.

#1584 (0.26) moved the tree off `main` so that a clone no longer carries about 140 MB of review
records, TRX files, transcripts and freeze manifests. The release contract adopted in #1548 no
longer reads adjudication records, freeze manifests, scope packets or claim registries.

The inputs that still guard compiler, converter, verifier or package behavior were copied to
ordinary test fixtures on `main` (under `tests/TestData/`). The #1584 pull request lists each
reader and where its inputs went.

The older evidence branches (`evidence/v0.24-archives`, `evidence/v0.25-archives`) keep their own
large files, as recorded in the `archives.json` files in this tree.

To read a file: `git show evidence/archive-2026-10:<directory>/<path>`.
