# 0.25 candidate freeze PR (#1579): Codex round 3

- **Invocation 3 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `12a5818d9` (the round-2 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 4 MAJOR, 1 MINOR.

Codex confirmed: the round-2 gate-copy, text-only fingerprint and three T1 controls discriminate; landing discovery
and this PR's exemptions are correct; 14,639 + 6 = 14,645.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | The trust anchor missed ignored files that the SDK's default `Compile` glob still compiles (e.g. an ignored `tests/Calor.Compiler.Tests/tmp/Injected.cs`). | Fixed: `TrustAnchorProblems` also lists ignored files under `src/`, `tests/`, `tools/` and the root (build output `bin/` and `obj/` excepted) and refuses any C# or MSBuild input (`.cs`, `.csproj`, `.props`, `.targets`, `.rsp`, `.editorconfig`, `.globalconfig`, `.resx`, `.runsettings`, `Directory.Build.*`). The ignored Z3 assets are binaries checked against the download script's pins. Control: `TheTrustAnchorRefusesAnIgnoredSourceFile` (a scratch repository). |
| 2 | MAJOR | Lockfile and producer inventories tolerated deletion. | Fixed: the lockfile set is recomputed from the candidate tree (same rule as the generator), node lockfiles are a fixed pair, and producers equal a reviewed table (ids in order; each file set equal to its directory listing plus extra files at the candidate). Five controls (remove a lockfile, empty the lockfiles, remove a node lockfile, remove a producer, remove a producer file). |
| 3 | MAJOR | Toolchain: `globalJson`, `determinismProtocol`, seeds and Z3 upstream archives unchecked; workflow pins flattened across jobs. | Fixed: per-job pins parsed as the generator does and compared with exact job keys; `globalJson` equals `global.json`; `determinismProtocol` and seeds equal the candidate's G2 protocol; Z3 upstream archives and version equal their candidate files. Five controls, including a renamed job. |
| 4 | MAJOR | Closure times, D-ACCEPT-FILE-FREEZE and live-check metadata could be changed consistently. | Fixed: issue states equal a reviewed snapshot (19 issues); the three closures equal it and precede `freezeObservedUtc`; D-ACCEPT-FILE-FREEZE's decision, author, date, issue #1578 and record (present at the candidate) are pinned; the live check's command, tree, exit code, time and A001 attestation are pinned, the attestation file exists at the candidate, and the retained output ends with the recorded verdict. Seven controls, including the both-copies closure move. |
| 5 | MINOR | `RefOutContractValidator.Validate` never ran. | Fixed: it runs on the candidate's blobs and reports each violation. |

Test manifest 14,645 → 14,663 (+1 fact, +17 tamper rows).

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — V025CandidateManifestTests.cs:85:** The trust anchor still permits extra compiled sources. `git check-ignore` confirms `tests/Calor.Compiler.Tests/tmp/Injected.cs` is ignored, while the SDK's default `Compile` glob includes it. A module initializer can replace `CALOR_V025_CLASSIFY_TARGET` with the landing SHA; HEAD and status checks then pass. Build from a fresh landing checkout or reject ignored source/build inputs before compilation. Add an ignored-source control.

2. **MAJOR — V025CandidateManifestTests.cs:180:** Dependency and producer inventories remain deletion-tolerant. Removing the compiler lockfile entry, emptying `dependencies.lockfiles`, or removing a producer leaves these loops without anything to reject. Recompute the required inventories from candidate git objects and require exact group/path equality. Add deletion and empty-inventory controls.

3. **MAJOR — V025CandidateManifestTests.cs:251:** Toolchain verification remains incomplete. Changing `toolchain.globalJson.version`, `determinismProtocol`, seeds, or Z3 upstream hashes escapes verification. Workflow checking also flattens jobs: renaming `publish-nuget.yml:publish` to a nonexistent job preserves the comparison. Compare all derived toolchain fields against candidate blobs, including exact workflow/job keys, and add isolated controls.

4. **MAJOR — V025CandidateManifestTests.cs:43:** Freeze authorization and closure facts are insufficiently checked. Moving #1545's closure to `2099-01-01T00:00:00Z` in both recorded copies satisfies every closure predicate. `D-ACCEPT-FILE-FREEZE` is checked only by ID, so its author, decision and review reference can change unchecked. The live-check tree, exit code and attestation metadata are also ignored. Validate the reviewed snapshot, require closures before the freeze, and bind the decision and live-check metadata to their retained evidence.

5. **MINOR — V025CandidateManifestTests.cs:207:** The promised D1 validator never runs. This code computes `RefOutContractValidator.Seal(d1)` but never calls `Validate`, despite the README claiming candidate-blob validation. Call `Validate(d1, scope, Blob)` and report its violations.

By inspection, the new gate-copy, text-only fingerprint and three T1 controls discriminate correctly. Landing discovery and this PR's exemptions look correct. **14,639 + 6 = 14,645** is correct. Offline regeneration matched with remote-tag lookup stubbed; .NET tests were blocked by sandbox temp-directory permissions. No files modified.
```
