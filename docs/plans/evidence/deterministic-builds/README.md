# Reproducible publication builds — evidence

Branch `fix/deterministic-publication-builds`. One of the five approved C2-blocker fixes for the
0.24 candidate re-freeze.

## Why

0.24 C2 (#1424, PR #1517, `docs/plans/evidence/c2-1424/regeneration.md` on its branch) found that
two builds of one commit are not byte-identical:

- packages: two packs of one build differed in every zip entry timestamp; a build in another
  checkout path also changed the DLLs and PDBs (absolute PDB path embedded);
- release metadata embeds the package hashes and inherited the difference;
- website: a random Next.js build id put 200 of 253 files in a different state on every build.

The R2 release gate (#1410: `scripts/verify_release_adjudication.py`, run by
`publish-nuget.yml` and `nextjs-gh-pages.yml`) rebuilds these at publication and compares
SHA-256 with the adjudicated hashes, so no adjudicated release could be published. The
maintainer decided to store hashes, not binaries, as evidence; this change makes those hashes
checkable. **The gate's comparison logic and the publication workflows are unchanged.**

## Nondeterminism sources and fixes

| # | Source (where it showed) | Fix | Where |
|---|---|---|---|
| 1 | NuGet pack writes the wall-clock time into every `.nupkg` zip entry | `DeterministicTimestamp` = `2000-01-01T00:00:00Z`, the .NET SDK's own pack property (NuGet `PackTask`). Unconditional constant: no git input, and neither an ambient `SOURCE_DATE_EPOCH` nor an ambient `DeterministicTimestamp` environment variable changes it (MSBuild imports environment variables as properties; a project assignment wins). Only an explicit `-p:` global property overrides it | `Directory.Build.props` |
| 2 | Release DLLs embed the absolute PDB path (debug directory); PDBs embed absolute document paths | `DeterministicSourcePaths=true` with `SourceRoot=$(CalorRepoRoot)` for **Release builds of projects under `src/`** (SourceLink and SCM queries are disabled in this repo, so the root is declared explicitly). The SDK turns this into `PathMap` (`<repo>/` → `/_/`, NuGet root → `/_1/`). Debug builds and test/tool projects keep real paths | `Directory.Build.props` |
| 3 | Next.js random build id (in every HTML page, `_next/static/<id>/`) | `generateBuildId` → `calor-<website version>-<commit[:12]>` (`git rev-parse HEAD`; `nogit` without git): fixed per commit, new for every deployed commit, so Next's router still detects a new deployment | `website/next.config.js` |
| 4 | webpack module ids: Next 14's `next-flight-client-entry-loader?modules=…` identifiers hold the loader options JSON-stringified then URL-encoded, with **absolute** paths; webpack's contextify does not relativize a query, so deterministic module ids differed per checkout path (seen as `2892` vs `2812` in `app/layout-*.js`) | `PathIndependentModuleIdsPlugin`: runs before webpack's `DeterministicModuleIdsPlugin`, hashes each identifier with every form of the repo root (given and realpath; both separators; raw and JSON-escaped; each raw, `encodeURIComponent`, `encodeURI`) replaced by `<root>`, assigns ids in code-point order with linear probing; webpack then finds every module numbered | `website/reproducible-module-ids.js` |
| 5 | App-router entry chunks named by `[chunkhash]`, which hashes module build inputs (absolute import paths in the entry loader source): byte-identical chunks got different file names | client production `output.filename` uses `[contenthash]` and `optimization.realContentHash=true`, so the name is the hash of the emitted bytes | `website/next.config.js` |
| 6 | `fs.readdirSync` order (file-system dependent: APFS vs ext4) decides sitemap order, search-index order and nav ties | sorted by code point | `website/src/lib/docs.ts` |

Found by running the proof, not by inspection: #1–#3 were C2's findings; #4 and #5 appeared only
after #3 was fixed (two clones still differed in 4 chunk files and every HTML page referencing
them); #6 is latent (macOS-only runs agree; a Linux checkout may order differently).

What is **not** changed: compared entry by entry with C2's own pack of the frozen candidate
`696ab824` (`c2-1424/publication/candidate-packages.json`, `worktree-pack-1`; `src/` is identical
between that candidate and this branch's base), both packages have the same entry names (65 and
32), and every entry except Calor's own assemblies is byte-identical — nuspec, README, icon,
`deps.json`, every third-party DLL and every native Z3 library. The only differing entries are
`calor.dll`, `calor.pdb`, `Calor.Runtime.dll`, `Calor.Runtime.pdb` in `calor`, and `calor.dll`,
`Calor.Runtime.dll`, `Calor.Tasks.dll` in `Calor.Sdk` (embedded paths). `check-packaged-z3.py
--all-rids` also runs inside every proof build. These test projects pass on a Release build with
the new settings: Tasks 134, ILAnalysis 46, LanguageServer 532, Verification 473, Enforcement 694
(+1 pre-existing skip).

The website keeps its 253 files; only the file names of 4 entry chunks, module numbers inside the
bundles, the build-id directory, and the order of sitemap / search-index entries change. All 57
Playwright tests in `website/tests` (navigation, search, hydration in three time zones, theme,
public claims) pass against a reproducible build (commit `296c618e`, before the build id gained
the commit suffix).

## Proof: two fresh clones at different paths

Command (from this branch at commit `1be1a95d`; the later commits add only this evidence):

```bash
python3 scripts/reproducible_builds.py run --workdir <empty dir> \
  --evidence docs/plans/evidence/deterministic-builds/proof
```

Each build is a `git clone --no-local` at the commit, Z3 bootstrapped by the owned
`download-z3.sh` and checked by `verify-z3-assets.py`, then `scripts/reproducible-build.sh`:
exactly the `publish-nuget.yml` publish-job commands (locked restore, Release build, CLI pack
`--no-build`, SDK pack with `CalorSdkRequireAllRids=true`, `check-packaged-z3.py`,
`generate-release-metadata.py`) and the `nextjs-gh-pages.yml` site build (`npm ci`,
`npm run build`).

| | build-1 | build-2 |
|---|---|---|
| tree | `<work>/a/calor` | `<work>/second-build/nested/elsewhere/calor-clone` |
| `HOME`, `NUGET_PACKAGES`, `DOTNET_CLI_HOME`, npm cache, `TMPDIR` | own, empty | own, empty |
| `TZ` | `UTC` | `Pacific/Kiritimati` (UTC+14) |
| `LANG`, `LC_ALL` | `C.UTF-8` | `tr_TR.UTF-8` |
| `SOURCE_DATE_EPOCH`, `DeterministicTimestamp` (environment) | unset | `1700000000`, `2025-02-03T00:00:00Z` |

Result (`proof/comparison.md`; per-file and per-entry hashes and the toolchain in
`proof/build-*.hashes.json`; build logs in `proof/build-*.log`):

| Published file | build-1 SHA-256 | build-2 SHA-256 | Same |
|---|---|---|---|
| `Calor.Sdk.0.24.0.nupkg` | `939ff0ce1f6ab2ad61d78a3ed999c3ca86f5b517c3ba535c0f6a66f01ad6cdc9` | `939ff0ce…cdc9` | yes |
| `calor.0.24.0.nupkg` | `2e8b842777b59fcf99877c3b7a467366dc8ed3b4050935680b31e92461cc14f1` | `2e8b8427…14f1` | yes |
| `calor-nuget-1be1a95d….provenance.json` | `9d9fbb25706092864579e27f2a13242821562bf59e06969a7849186e88f7c714` | `9d9fbb25…c714` | yes |
| `calor-nuget-1be1a95d….sbom.spdx.json` | `f933aee8b1181191960b353a09296ca5b7e0dc5cb4d316a2f73f162b188ec9fa` | `f933aee8…c9fa` | yes |
| website tree (253 files, the gate's `tree_digest`) | `4ed4ca5492c61779fcfeda44970a2f2949ae36e6596b0e6047d5d4e4d9aa7981` | `4ed4ca54…7981` | yes |

97 package entries compared (content hash and zip timestamp), 253 website files compared:
**0 differences**. The release-metadata files name the commit, so their hashes (and, through the
build id, the website tree) are per commit by design; the package hashes are not.

Corroborating runs on the same toolchain, in other directories and at other times, give the same
package hashes: two earlier two-clone runs (commits `62b7bab4` and `296c618e`, same package
sources) and a plain pack in the developer worktree (non-isolated `HOME` and NuGet cache).

Toolchain: macOS arm64, .NET SDK 10.0.401, Node v26.10.0, npm 11.19.1.

## What this does not prove

- **Cross-toolchain identity.** Bytes are a function of the toolchain: the .NET SDK (Roslyn,
  NuGet), Node, and the npm lockfile. The publish workflows use `ubuntu-latest`,
  `dotnet-version: 10.0.x` (resolved to 10.0.401 in CI on 2026-10-07) and Node 20 (v20.20.2).
  Hashes adjudicated on one toolchain match the gate's rebuild only if the gate's toolchain
  produces the same bytes. Every hash set now records its toolchain, and `compare` prints any
  toolchain difference. `ci-comparison.md` records whether this PR's Linux CI hashes equal the
  macOS hashes above. Pinning the SDK for good needs `global.json` `rollForward: disable` (a
  `setup-dotnet` pin alone loses to a newer preinstalled SDK under `latestMinor`); that changes
  every developer's and CI job's SDK and is left to the maintainer.
- Windows builds were not tested (the publish jobs do not pack on Windows).

## Guards

- `.github/workflows/reproducible-builds.yml`, on every PR to `main` and `release/**` (no paths
  filter: package and site inputs live all over the tree): builds the packages and the site in
  the checkout and in a fresh clone at another path on `ubuntu-latest`, the second build with its
  own `HOME`, caches, time zone, locale, and ambient `SOURCE_DATE_EPOCH` / `DeterministicTimestamp`.
  It fails on any byte difference or on an empty or partial output. The package job seeds Z3 only
  through the owned bootstrap action; the clone receives only the six registered, pinned assets
  (`reproducible_builds.py clone --z3 copy`), which `verify-z3-assets.py` and the clone's build
  re-verify.
- `scripts/test_reproducible_builds.py` (in `test.yml`'s guard job): the props (unconditional
  timestamp, Release `src/` source-path mapping, nothing re-enabling nondeterminism), the build
  id (evaluated with Node against `git rev-parse HEAD`), the module-id plugin (its `moduleIds`
  hook run on Next-style loader identifiers from different roots, including a root with `"` and
  Windows roots), the sorted docs walk, the workflow's environment variation, that the build
  script runs the publish job's pack commands, the pinned-only Z3 copy (a stray `.cs` file is not
  copied), and the hash/compare tool (zip-timestamp-only, content, missing-file, empty-output and
  partial-output differences all fail; the website digest is the gate's own `tree_digest`).

## Reviews

`reviews/` holds the Codex adversarial review rounds and the verification pass.
