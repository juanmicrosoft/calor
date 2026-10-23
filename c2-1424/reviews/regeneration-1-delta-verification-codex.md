# C2 verification pass on the regeneration-1 delta (Codex)

After the maintainer decisions of 2026-10-07 (fix all 5, re-freeze, regenerate; store hashes only; claim registry stays a draft) the branch was rewritten to 0868abb7 without the package binaries. One verification-only pass on the delta 2fc26788 -> 0868abb7 plus classifier.txt and classifier-record.json. Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" <prompt> < <delta diff>` (the first attempt hit the Codex usage limit and was re-run after the reset).

## Prompt

> Verification-only pass on a delta, gate C2 (#1424), Calor 0.24. Repository root is the current directory: branch milestone-0.24/c2-1424-regeneration rewritten to commit 0868abb7 plus two uncommitted new files (docs/plans/evidence/c2-1424/classifier.txt, classifier-record.json). The previous PR head was 2fc26788 (reviewed in reviews/round-1-codex.md, round-2-codex.md, verification-pass-codex.md). The stdin diff is 2fc26788 -> current for docs/plans/evidence/c2-1424 (binary .gz files and classifier.txt omitted; plan.json and claim-registry.json were re-serialized by a JSON writer, so most of their diff is formatting).
> 
> Maintainer decisions of 2026-10-07 this delta must implement: (1) 'Fix all 5, re-freeze, regenerate' - this PR becomes the record of regeneration 1 (incomplete); the BLOCKED rows are stated together with that decision; this incomplete regeneration counts as regeneration 1 of 2 unless the contract says otherwise, with the author's reading of docs/plans/v0.24-evidence-contract.md sections 9 and 10 stated; (2) 'Store hashes only' - the ~118 MB package binaries are removed from the branch history, keeping SHA-256 hashes, sizes, and the producing commands; (3) the claim registry stays DRAFT-marked, not frozen, because the release notes are changing.
> 
> Verify: each decision is implemented consistently across regeneration.md, results.json, ledger.json, plan.json, claim-registry.json, regeneration-1-manifest.json, classifier-record.json; no stale reference remains to removed files (publication/packages/, raw-artifact-freeze.json) or to the registry being frozen; the hashes, sizes, and commands for the packages are still present (publication/candidate-packages.json, publication/40-pack-1.log, 41-pack-2.log); regeneration-1-manifest.json hashes match the files; no publication leak; every file under c2-1424 is a new regular data file the C1 classifier (tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs) exempts. Output 'VERDICT: APPROVE' or 'VERDICT: REQUEST-CHANGES', then findings tagged BLOCKING/MAJOR/MINOR/NIT with file and fix.

## Response

VERDICT: APPROVE

No findings. The three decisions are consistently recorded. All 188 manifest hashes and the classifier-output hash match. Package hashes, sizes, and commands remain; package binaries are absent from rewritten branch history. All 194 files satisfy C1’s added regular data-file exemption. Removed-path references occur only in historical reviews. No publication leak found.

## Disposition

APPROVE; nothing to change.
