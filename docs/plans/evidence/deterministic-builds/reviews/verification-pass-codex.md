# Verification pass — Codex

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`
- Input: `git diff origin/main...HEAD` at `6ca5e1cb` (proof, proof-final and cross-toolchain hash
  files excluded), asking for a per-finding VERIFIED / PARTIAL / NOT FIXED status for all 14
  findings of rounds 1–3 and any new blocker.
- Verdict: **VERIFIED** — all 14 findings verified; no new blocker. Items left to the maintainer
  (SDK pinning, self-hosting Google Fonts) judged accurately documented.

## Review text

All 14 findings are VERIFIED. SDK pinning and Google Fonts self-hosting remain maintainer decisions, accurately documented.

| Finding | Status | Reason and evidence |
|---|---|---|
| R1.1 — Floating toolchain | VERIFIED | Versions and SDK fingerprint are recorded; comparison reports differences; SDK pinning limits are documented. [README.md:113](docs/plans/evidence/deterministic-builds/README.md:113) |
| R1.2 — Unchecked Z3 directory copying | VERIFIED | Only registered assets are copied, then destination verification regenerates provenance; the stray-file regression test is present. [reproducible_builds.py:113](scripts/reproducible_builds.py:113), [test_reproducible_builds.py:334](scripts/test_reproducible_builds.py:334) |
| R1.3 — Deployment build ID | VERIFIED | The ID includes website version and the checked-out commit’s first 12 characters. [next.config.js:38](website/next.config.js:38) |
| R1.4 — Composed escaping and Windows roots | VERIFIED | URI decoding and JSON/native separator folding handle these forms; the plugin-hook regression passes. [reproducible-module-ids.js:37](website/reproducible-module-ids.js:37) |
| R1.5 — Empty/partial outputs | VERIFIED | Both inventories are validated before comparison; missing packages, metadata, and required website files fail. [reproducible_builds.py:190](scripts/reproducible_builds.py:190) |
| R1.6 — Ambient timestamp override | VERIFIED | The timestamp assignment is unconditional, overriding ambient properties while permitting explicit global-property overrides. [Directory.Build.props:35](Directory.Build.props:35) |
| R1.7 — Identical CI environments | VERIFIED | Second builds receive isolated HOME/caches and varied timezone, locale, and timestamp variables. [reproducible-builds.yml:59](.github/workflows/reproducible-builds.yml:59) |
| R1.8 — Incomplete paths filter | VERIFIED | No paths filter remains; every PR targeting main or release branches triggers the workflow. [reproducible-builds.yml:13](.github/workflows/reproducible-builds.yml:13) |
| R2.1 — Replacing repository-relative substrings | VERIFIED | Replacement respects absolute-path boundaries; `/app`, `/src`, `/website`, and sibling-directory regressions pass. [reproducible-module-ids.js:78](website/reproducible-module-ids.js:78) |
| R2.2 — Hollow outputs and missing SDK inspection | VERIFIED | Essential package entries and referenced website assets are required; the helper runs SDK inspection with `--all-rids`. [reproducible_builds.py:201](scripts/reproducible_builds.py:201), [reproducible-build.sh:37](scripts/reproducible-build.sh:37) |
| R2.3 — Missing CI comparison evidence | VERIFIED | The document and hash records exist; recorded macOS/Linux packages, metadata, and website compare with zero differences. [ci-comparison.md:8](docs/plans/evidence/deterministic-builds/ci-comparison.md:8) |
| R3.1 — Literal `%XX` roots | VERIFIED | Each root contributes literal and once-decoded forms; the `%20` and `%2F` plugin-hook regression passes. [reproducible-module-ids.js:59](website/reproducible-module-ids.js:59), [test_reproducible_builds.py:142](scripts/test_reproducible_builds.py:142) |
| R3.2 — SDK-build identification and CHANGELOG claim | VERIFIED | `dotnetSdkBuild` fingerprints compiler/packer assemblies; the same-version fake-SDK test is present; CHANGELOG qualifies the claim by commit, SDK build, and font inputs. [reproducible_builds.py:287](scripts/reproducible_builds.py:287), [test_reproducible_builds.py:304](scripts/test_reproducible_builds.py:304), [CHANGELOG.md:9](CHANGELOG.md:9) |
| R3.3 — Wrong supplementary commit and input claim | VERIFIED | `macos-ms-sdk-4600d900-packages.hashes.json` matches its commit and metadata filenames; the text lists publication inputs beyond `src/`. [ci-comparison.md:31](docs/plans/evidence/deterministic-builds/ci-comparison.md:31) |

Google Fonts’ build-time dependency and deferred self-hosting are accurately documented at [README.md:123](docs/plans/evidence/deterministic-builds/README.md:123).

Validation: 13 read-only guard tests passed; final two-clone and macOS/Linux recorded hashes compare with zero differences. In-memory empty/partial/hollow controls reject incomplete outputs. Full builds and temporary-file tests were not rerun in this read-only sandbox. No new blocker found.

VERDICT: VERIFIED
