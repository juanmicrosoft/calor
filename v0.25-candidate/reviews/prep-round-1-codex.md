# 0.25 release prep: Codex round 1

- **Invocation 1 of at most 4** (3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `dc181e303` (the first prep commit on `origin/main` `e075f848a`).
- **Verdict:** REQUEST-CHANGES: 1 BLOCKING, 5 MAJOR, 1 MINOR.

Codex also verified: the two release sections are byte-identical, the MDX compiles, G012 passes, all
version spots are updated, the test-manifest bump is exactly 41, the maintainer decisions are
recorded faithfully, leaving `scope.json` unamended for them is defensible, and no 0.24 C1 file
changed. It could not run .NET tests in its read-only sandbox.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Acceptance needs changes that invalidate this freeze: READY must be recorded in `scope.json`, which the classifier treats as a frozen input; and the release gate rejects the 0.25 contract packet format with `G016`, while `publish-nuget.yml` runs the gate from the candidate checkout. | **Escalated to the maintainer**, not fixable by this PR: both are decisions (READY protocol; #1548's choice of the contract that governs 0.25). README "Open questions" 1-2 now say they block the freeze, and that the freeze moves to a later merge if a code change is needed. |
| 2 | MAJOR | Manifest lacks C1 identities: `durableIdentity` without `status`/`commit`/`resolvedVia`; no producers, seeds, determinism environment/configuration; `merged()` accepts an empty gate list. | Fixed: P1-form identity; 7 producer groups hashed; G2 protocol toolchain, environments and seeds recorded; the required gate set (15 gates, the prep PR last) is enforced and each needs a merged PR. `DurableProvenance.Verify` runs in the freeze PR's C# test. |
| 3 | MAJOR | Pinned seals recorded, not enforced (a mutation probe emptied the scope denominator and D1 rules without a failure); T1 amendment pins omitted. | Fixed: scope, D1, T1 baseline and T1 amendment seals are recomputed with a System.Text.Json-compatible serializer and compared with the test pins; three controls on the real packets (two sealed mutations, one unsealed). Dry run on `e075f848a`: all four equal their pins. |
| 4 | MAJOR | SDK check reads only each workflow's first version. | Fixed: per job, the first install must be 10.0.401 and later ones only 8.0.426; `global-json-file` fails; four mutation controls in `--self-test`. |
| 5 | MAJOR | Classifier ignores `rev-list` exit status (fail-open). | Fixed: `rev-list` failures and `merge-base` errors (exit other than 0/1) on the target and protected-main sides give `UNDECIDED`; two real-git controls (a deleted commit object on each side). |
| 6 | MAJOR | Notes say the type checker "rejects today" named-refinement arithmetic, which #1515 in the same release fixed. | Fixed in both changelog files. The two website pages that said the same (`syntax-reference/refinement-types.mdx`, `guides/dependent-types-tutorial.mdx`) are corrected too. |
| 7 | MINOR | #1545 wording ("half done", "older tags can still read") will go stale. | Fixed: stated as what closing #1545 needs, plus a dated freeze-time observation. |

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. BLOCKING — v0.25-candidate/README.md:66: acceptance requires changes that invalidate this freeze. READY must be recorded in `scope.json`, but changing it invalidates the candidate. The release gate also rejects the registered 0.25 contract format with G016; publication re-runs that script from the candidate checkout. Resolve the readiness-record protocol and implement the selected contract format before freezing this merge commit.

2. MAJOR — generate_candidate_manifest.py:133: the manifest lacks required C1 identities and completeness checks. `durableIdentity` omits `status`, `commit`, and `resolvedVia`, so the existing P1 verifier rejects it. The output also omits C1's producer identities, seeds and determinism-environment/configuration records. `merged()` accepts `gates: []` without failure. Restore the applicable C1 fields, validate the complete required gate/PR set, and verify the identity through `DurableProvenance`.

3. MAJOR — generate_candidate_manifest.py:238: pinned seals are recorded without being enforced. A read-only mutation probe emptied the scope denominator and D1 rules; `packets()` reported no failures. `pinned()` merely extracts constants, and the T1 amendment pins are omitted. Recompute and compare the scope/D1 seals and baseline/amendment approval pins using the C# validators' exact serialization rules. Add discriminating mutation controls.

4. MAJOR — generate_candidate_manifest.py:174: SDK validation checks only the workflow's first version. Changing later `publish-nuget.yml` jobs from `10.0.401` to `10.0.999` produced no failure. Validate each job's first SDK against the release SDK, allowing only the registered auxiliary .NET 8 installation, as `ReleaseSdkPinTests` does.

5. MAJOR — ReleaseCandidateInvalidation.cs:137: history enumeration can fail open. Both `rev-list` calls discard exit status. Failed range enumeration can produce `NOT-INVALIDATED` with no diffs; failed landing enumeration can leave every change classified `BeforeLanding`, exempting frozen classifier edits. Return `UNDECIDED` on either failure and add controls for these error paths.

6. MAJOR — CHANGELOG.md:351, mirrored in MDX: a stated current limitation is already fixed. "The type checker rejects today" contradicts this release's #1515 entry, `TypeChecker.cs`, and `NamedRefinementArithmeticTests`. Describe the rejection as historical and state that #1515 fixes it.

7. MINOR — CHANGELOG.md:407, mirrored in MDX: the credential-status wording will become stale. "Half done" and "Older tags can still read" describe the freeze-time state, although acceptance requires closure. Date the entire statement explicitly as a freeze-time observation; describe the environment as configured but awaiting creation.

Verified: release sections are byte-identical, MDX compiles, G012 passes, version spots are updated, and the test bump is exactly 41. The maintainer decisions are faithful; leaving `scope.json` unchanged for those two decisions is defensible. This commit changes none of 0.24's frozen C1 files. .NET tests were not run under the read-only filesystem restriction.
```
