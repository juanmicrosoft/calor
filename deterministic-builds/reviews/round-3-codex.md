# Review round 3 — Codex (adversarial, final round)

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`
- Input: `git diff origin/main...HEAD` at `6ff14029` (proof and cross-toolchain hash files
  excluded), asking it to verify the round-2 fixes and look for remaining blockers/majors.
- Verdict: **CHANGES REQUESTED** (1 major, 2 minor). Round-2 fixes verified: `/app`, `/src`,
  `/website` roots, hollow-output rejection, target scoping, SDK inspection; recorded macOS/Linux
  hashes compare equal.

## Disposition

| # | Finding | Disposition |
|---|---|---|
| 1 | A literal `%XX` in the checkout path (`/tmp/calor%20x`) survived normalization: roots were URI-decoded, query text was not | **Fixed.** Each root contributes two forms: literal (as it reads in a decoded loader query) and decoded once (as a raw resource path reads after canonicalization). Hook test now includes `/tmp/calor%20x` and `/tmp/a%2Fb/calor`; both give the same ids as the other roots. |
| 2 | `toolchain` records only the SDK version, which cannot tell Homebrew's source-built 10.0.401 from Microsoft's; CHANGELOG said "the same every time" | **Fixed.** `toolchain.dotnetSdkBuild` = sha256 over the SDK's `Microsoft.CodeAnalysis.CSharp.dll` and `NuGet.Packaging.dll` (Homebrew and Microsoft builds on this Mac differ: `930306df…` vs `52a28f74…`); unit test with two fake SDKs of one version. `ci-comparison.md` says which earlier hash sets lack the field and which SDK build they used. CHANGELOG now says "from the same commit" and names the SDK-build and Google Fonts limits. |
| 3 | `ci-comparison.md` named the supplementary run `1be1a95d` (it was `4600d900`) and said unchanged `src/` implies unchanged packages | **Fixed.** File renamed `macos-ms-sdk-4600d900-packages.hashes.json`; the text lists all package inputs (`src/`, root `README.md`, icon, embedded `bench/` and `tests/` resources, props, Z3 assets). |

## Review text

Round-2’s `/app`, `/src`, and `/website` regressions pass. Hollow-output rejection, target scoping, and SDK inspection are fixed. The recorded macOS/Linux hashes compare equal for packages, metadata, and all 253 website files. Nineteen selected read-only tests passed; full builds were unavailable in this sandbox.

1. **Major — Literal percent escapes in checkout paths still change published module IDs.**  
   [website/reproducible-module-ids.js:25](website/reproducible-module-ids.js:25) URI-decodes both raw roots and encoded identifiers. For a literal root `/tmp/calor%20x`, `rootForms()` becomes `/tmp/calor x`, while decoding the loader query leaves `/tmp/calor%20x`. The root therefore survives normalization.

   Through the actual `moduleIds` hook, identical layout/page modules received IDs `[180, 809]` under `/tmp/calor`, versus `[254, 455]` under `/tmp/calor%20x`. Those IDs enter published JavaScript, breaking checkout-path reproducibility.

   **Fix:** decode loader-query fields according to their encoding, preserve literal characters in filesystem roots, and add `%20`/`%2F` hook regression tests.

2. **Minor — The recorded toolchain cannot identify the SDK build claimed by the documentation.**  
   [ci-comparison.md:47](docs/plans/evidence/deterministic-builds/ci-comparison.md:47) says each hash set’s `toolchain` identifies which SDK build was used. However, [scripts/reproducible_builds.py:285](scripts/reproducible_builds.py:285) records only the SDK version. The Homebrew and Microsoft macOS records have identical toolchain fields; `toolchain_notes()` returns no differences despite different package hashes.

   **Fix:** record an SDK distribution/build identifier or checksum, or explicitly document that these fields cannot distinguish distributions. Qualify [CHANGELOG.md:9](CHANGELOG.md:9)’s “same every time” claim with the documented toolchain and font-input limits.

3. **Minor — The supplementary comparison names the wrong commit and overstates source independence.**  
   [ci-comparison.md:30](docs/plans/evidence/deterministic-builds/ci-comparison.md:30) calls the supplementary run `1be1a95d`, but its [hash record:1193](docs/plans/evidence/deterministic-builds/cross-toolchain/macos-ms-sdk-1be1a95d-packages.hashes.json:1193) records `4600d900`; its metadata filenames agree. Also, unchanged `src/` alone does not imply unchanged package bytes: the SDK packages root `README.md`, and other publication inputs live outside `src/`.

   **Fix:** correct the commit and filename, and limit the conclusion to commits with identical publication inputs.

VERDICT: CHANGES REQUESTED
