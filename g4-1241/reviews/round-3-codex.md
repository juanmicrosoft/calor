# G4 review round 3 — Codex (adversarial)

Reviewed: `git diff origin/main...05df1137`. Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` with the diff on stdin.

## Findings (verbatim)

1. **MAJOR — Deleted root and linked inputs still bypass pinning.** `scripts/checkout_compiler.py:88–95` omits staged deletions from discovery, skips nonexistent paths, and checks directories only inside the two source projects. Deleting `bench/phase0-agent-native/metadata-references-manifest.json`, which the compiler embeds, therefore leaves the old DLL accepted. I reproduced acceptance in memory with the declared linked input missing. The new control tests only editing that resource. **Fix:** reject missing declared inputs and compare a build-time input inventory against the checkout, including deletions. Add staged/committed linked-resource and root-build-file deletion controls.

2. **MAJOR — Mandatory steps can disappear while the driver regression test stays green.** `scripts/test_tier2_verification.py:335–343` discovers its expected steps from the driver itself and requires only two calls. I removed both mandatory migrator blocks from `verify_corpus.main` in memory; `test_each_failed_or_unavailable_step_fails_its_driver` still passed. This leaves round 2’s promised assertion that required steps were invoked unimplemented. **Fix:** independently register the required script commands and arguments for each driver mode, assert every required invocation, then fail each individually. Add mutations that remove each mandatory invocation.

3. **MINOR — The inventory still documents the discarded signature format.** `eng/tier2-fixture-expectations.json:4` defines signatures as “sorted unique error-severity codes.” The implementation and registrations now use `file|code|declaration|message` and retain duplicates. **Fix:** update the machine-readable description to match the actual format.

Verdict: **REQUEST CHANGES — deletion pinning and mandatory-step regression coverage remain incomplete.**

## Response

1. **Partly fixed; the remainder is recorded as a limitation.** A declared linked input (`Include="..\.."` in either project) that no longer exists now refuses the build. The directories that hold linked inputs are checked too. A new control covers a committed deletion of a linked resource; the `missing` check is the only check that catches it, and mutation testing confirms this. Not fixed: deletion of a root build file such as `Directory.Packages.props`. The only mtime signal for that deletion is the repository root directory, and CI writes `tier2.log` there before the check runs. Checking the root would therefore refuse every CI run. A build-time input inventory would close the gap but needs MSBuild changes beyond G4's remaining line capacity. The README states the limitation.
2. **Fixed.** `test_each_failed_or_unavailable_step_fails_its_driver` now registers, independently of the drivers, the commands each mode must run and asserts that each one ran:
   - Tier 1: `fixture_compile_check.py --root samples`.
   - Tier 1 `--self-test`: `byte_preservation_check.py --self-test` and `fixture_compile_check.py --self-test`.
   - Tier 2: `verify_phase1.py --corpus all`, `migrator_corpus_dryrun.py`, and `migrator_revert_roundtrip`.

   It then fails each step alone. New mutations that replace either Tier 2 migrator invocation, or the Tier 1 fixture-check self-test, with a no-op are all killed.
3. **Fixed.** The `description` in `eng/tier2-fixture-expectations.json` now documents the `file|code|declarationId|message` signature format with duplicates kept.

Mutation check: 32 of 32 killed.
