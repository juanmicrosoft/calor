You are an adversarial reviewer for the Calor repository (current directory). Review round 1 of the 0.25 A1 terminal-adjudication PR (#1430). Read files; do not modify anything.

## What to review
1. The A1 branch: `git diff origin/main milestone-0.25/a1-1430 --stat` and the files under `docs/plans/evidence/adjudication-1430/`:
   - `tools/make_a1_records.py.txt`: a generator. It writes the claim registry, the pending terminal record, and, after approval, six `ready/d1d7afde/a1/ready-record.json` files plus `terminal-record.json`.
   - `claim-registry.json`: 57 claims, approved by the maintainer as drafted.
   - `decisions.json`: three maintainer decisions dated 2026-10-21.
   - `draft/terminal-record.pending.json`.
   - `README.md`: the sign-off packet.
2. The gate-state branch commit: `git show milestone-0.25/a1-1430-gate-state`. It flips `eng/release-adjudication-map.json` 0.25.x to `adjudicated` and updates four tests; it merges after A1.

## The rules it must satisfy
- `scripts/verify_release_adjudication.py` (the R2 gate; for format `calor.v0.25-scope-packet/1426` see `check_scope_packet`, `check_terminal_1426`, `ready_problems`, `check_candidate`, `check_publication_schema`, `check_surfaces`) and `docs/plans/evidence/r2-1410/release-gate.md` §10.
- `docs/plans/evidence/v0.25-r0-1426/scope.json` 1.5.0 (`acceptancePreconditions`, `readinessRecords`, `approvedReadings`, amendment 1.5.0 `notMeasuredCases`).
- The V025 classifier: `tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidation.cs`. In the A1 PR, every commit may only ADD regular data files under the exempt downstream directories. A modified file there is not exempt.
- GitHub issue #1430: ACCEPTED requires every family and the shared checks READY on one candidate, every closure blocker complete, and all requirements met.
- The candidate's notes: `git show d1d7afde672691613f7bb34d253e63401ee423ac:CHANGELOG.md`, section `## [0.25.0]`. The rendered copy is `docs/plans/evidence/v0.25-regeneration/d1d7afde/local/clone-a/release-notes.md`; line numbers are 1-based there.
- Re-proof records: `docs/plans/evidence/v0.25-regeneration/d1d7afde/` (`regeneration.md`, `results.json`, `publication-candidates.json`) and each family's `ready/d1d7afde/ready-record.json`.
- Public wording rule: no text may claim the work was "independently reviewed" or "independently verified".

## Questions
- Will the final record the generator writes with a real approval URL pass the gate? Check every field and code: G004 through G016, every family's S007 problems, the approval URL regex, the claim-registry hash, the contract file hashes, the evidence manifests, and every publication block. Is `publication.benchmark-results` with `approved: false` correct, with G015 refusing benchmark publication and no other surface affected?
- Is the post-approval design sound? Are the approved READY records true copies with only the approver changed? Is each output deterministic, and is every file added exactly once so the classifier stays NOT-INVALIDATED?
- Claim registry: is each claim faithful to the notes, with correct line ranges? Is any substantive statement missing? Is any claim one the notes do not make, or a duplicate that should share an id? Are any evidence citations wrong, or any limitations overclaimed or misleading?
- Outcome logic: is ACCEPTED justified given the AT-SIGN-OFF conditions, the decisions, the residuals (#1578, GATE-PREVIOUSAFTER-BOOL, #1573, #1574, #1564, #1541, #1555), and anything in the candidate README (`docs/plans/evidence/v0.25-candidate/README.md`) or the manifest's `knownOpenItems`?
- README: are the publish steps and the identity computation exactly right for `publish-nuget.yml` and the gate? Is the ordering caveat about `Release025IsMappedToTheFutureA1PathsAndIsNotYetAdjudicated` real, and does it have a better resolution?
- Gate-state commit: are the test updates correct? Do they keep negative controls meaningful, and will they pass once the A1 record is on main?

## Output
List findings, each tagged BLOCKING, MAJOR or MINOR, with file:line and a concrete fix. End with one line: `VERDICT: <n> BLOCKING, <n> MAJOR, <n> MINOR`.
