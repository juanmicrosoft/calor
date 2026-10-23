# C1 #1423 review round 2 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **BLOCKING — tests/Calor.Compiler.Tests/EvidenceContract/CandidateInvalidation.cs:248:** `diff-tree` honors `submodule.<name>.ignore=all`, silently omitting registered gitlink changes. Verified against `5de804cc`: setting `submodule.bench/corpus/MediatR.ignore=all` hides its gitlink addition. A gitlink-only change can therefore receive `NOT-INVALIDATED`. Add `--ignore-submodules=none` and a real-git regression with this configuration; the current control tests only the pure path rule.

2. **MAJOR — docs/plans/evidence/c1-1423/candidate-manifest.json:1094:** Round-1 item 4’s disposition is unacceptable without an accepted amendment. Recording SDK/image binding as deferred does not satisfy #1423’s required environment identity or invalidate environment drift. Freeze exact environment identities and test their comparison, or merge an explicit #1407 amendment authorizing deferred binding before claiming C1 is frozen.

3. **MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/CandidateManifestTests.cs:192:** Round-1 item 6 remains incomplete. Emptying G1’s `mergedPrs` passes these checks; an in-memory generator run with that omission also returned no findings. Moreover, line 209 checks S2 repairs against the global PR dictionary, allowing them under another child despite claiming S2 membership. Bind the required PR set per gate, restrict the stacked exception to #1474 through #1475, check repairs within S2, and add omission/misassignment controls.

## Dispositions

1. **Fixed.** Both `diff-tree` calls pass `--ignore-submodules=none`. Real-git regression
   `AGitlinkBumpInvalidatesDespiteSubmoduleIgnoreConfig` sets `submodule.bench/corpus/MediatR.ignore=all`
   and `diff.ignoreSubmodules=all`, commits a gitlink, and expects `INVALID` naming exactly that path.
2. **Not resolvable by C1; escalated.** Hosted runner images cannot be pinned, and C1 has no
   amendment authority (§9: the maintainer holds it). C1 now records the environment actually
   observed for the candidate (SDK 10.0.401, runtime 10.0.12, and the five runner images with
   versions, from the candidate's own push run 37560874017) as **observed, not pinned**, says the
   git classifier cannot see environment drift, requires C2 to compare its realized environment
   with the observation, and states that C1's acceptance depends on a maintainer decision or a
   #1407 amendment. C1 does not claim the environment is frozen.
3. **Fixed.** Each gate's PR set must equal a table in `CandidateManifestTests` (reviewed with this
   PR); R0's set must also equal the contract's acceptance PR plus amendment PRs; each S2 repair
   must be in the S2 child itself; the stacked exception is limited to #1474 through #1475. New
   tamper controls: an emptied G1 PR set and a repair PR removed from S2.
