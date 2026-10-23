# docs/plans/evidence (archived)

This directory held the evidence records of milestones 0.24 and 0.25: review rounds, freeze and
candidate manifests, scope packets, registers, TRX files and transcripts (2,990 files, about
140 MB). #1584 removed it from `main`.

- **Archive:** orphan branch `evidence/archive-2026-10`, commit
  [`d9dbd5a7`](https://github.com/juanmicrosoft/calor/tree/d9dbd5a7270aff3a2c3e1efa6f85625f278474aa).
  Each directory that was `docs/plans/evidence/<dir>/` is `<dir>/` at the root of that commit.
  The branch's first commit, `bd326a76`, keeps the original paths.
- **Last `main` commit with the tree:** `d89ffcbc840d4c1d90ee8dd3b445a5021b559eb4`.
- **Inputs that still guard behavior** moved to ordinary fixtures: `tests/TestData/InteropScope/`,
  `tests/TestData/SoundnessSweep/`, `tests/TestData/DeterminismProtocol/` and
  `tests/TestData/ReleaseCredentials/`. The B1 benchmark packets are in
  `eng/benchmark-publication/` (#1585).

Do not add files here. Review records belong in the pull request; large run outputs belong in
workflow artifacts or an archive branch; test inputs belong in `tests/TestData/`.
`EvidenceTreeGuardTests` fails if any tracked file other than this README appears under this
directory.
