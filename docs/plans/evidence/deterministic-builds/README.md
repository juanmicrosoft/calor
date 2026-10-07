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
checkable. **The gate's comparison logic is unchanged.**

## Nondeterminism sources and fixes

| # | Source (where it showed) | Fix | Where |
|---|---|---|---|
| 1 | NuGet pack writes the wall-clock time into every `.nupkg` zip entry | `DeterministicTimestamp` = `2000-01-01T00:00:00Z`, the .NET SDK's own pack property (NuGet `PackTask`). A constant: no git or environment input; an ambient `SOURCE_DATE_EPOCH` is ignored because the property is already set | `Directory.Build.props` |
| 2 | Release DLLs embed the absolute PDB path (debug directory); PDBs embed absolute document paths | `DeterministicSourcePaths=true` with `SourceRoot=$(CalorRepoRoot)` for **Release builds of projects under `src/`** (SourceLink and SCM queries are disabled in this repo, so the root is declared explicitly). The SDK turns this into `PathMap` (`<repo>/` → `/_/`, NuGet root → `/_1/`). Debug builds and test/tool projects keep real paths | `Directory.Build.props` |
| 3 | Next.js random build id (in every HTML page, `_next/static/<id>/`) | `generateBuildId` → `calor-<website/package.json version>`: fixed per commit, still changes every release | `website/next.config.js` |
| 4 | webpack module ids: Next 14's `next-flight-client-entry-loader?modules=…` identifiers hold URL-encoded **absolute** paths in the loader query, which webpack's contextify does not relativize, so deterministic module ids differed per checkout path (seen as `2892` vs `2812` in `app/layout-*.js`) | `PathIndependentModuleIdsPlugin`: runs before webpack's `DeterministicModuleIdsPlugin`, hashes each identifier with every form (raw, URL-encoded, JSON-escaped; given and realpath) of the repo root replaced by `<root>`, assigns ids in code-point order with linear probing; webpack then finds every module numbered | `website/reproducible-module-ids.js` |
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
--all-rids` also runs inside every proof build. These test projects pass on the Release build with the new settings (Tasks 134, ILAnalysis 46,
LanguageServer 532, Verification 473, Enforcement 694 + 1 pre-existing skip). The website keeps its 253 files; only file names of 4 entry chunks,
module numbers inside the bundles, the build-id directory, and the order of sitemap /
search-index entries change. All 57 Playwright tests in `website/tests` (navigation, search,
hydration, theme, public claims) pass against the reproducible build of build-1.

## Proof: two fresh clones at different paths

Command (from this branch, commit `296c618e`, the code commit of this PR):

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

Result (`proof/comparison.md`, full per-file and per-entry hashes in `proof/build-*.hashes.json`):

| Published file | build-1 SHA-256 | build-2 SHA-256 | Same |
|---|---|---|---|
| `Calor.Sdk.0.24.0.nupkg` | `939ff0ce1f6ab2ad61d78a3ed999c3ca86f5b517c3ba535c0f6a66f01ad6cdc9` | `939ff0ce…cdc9` | yes |
| `calor.0.24.0.nupkg` | `2e8b842777b59fcf99877c3b7a467366dc8ed3b4050935680b31e92461cc14f1` | `2e8b8427…14f1` | yes |
| `calor-nuget-296c618e….provenance.json` | `1d5fce069d66aba890f5cfa6622afbeff2083f0bf46d9a75d4a520619a3d37a8` | `1d5fce06…37a8` | yes |
| `calor-nuget-296c618e….sbom.spdx.json` | `70b18ad3f42ea2a36925e3b2a34f4899c6d4cb3f3bdb6547cb7af387ec5c9de0` | `70b18ad3…9de0` | yes |
| website tree (253 files, the gate's `tree_digest`) | `7b46a8b8891e2d7e332477f5d5f82a21ac7a9b831f8be81218f81bc0dc8a078e` | `7b46a8b8…078e` | yes |

97 package entries compared (content hash and zip timestamp), 253 website files compared:
**0 differences**.

Corroborating runs on the same toolchain, different directories and times:

- the same two package hashes from an earlier two-clone run (commit `62b7bab4`, same package
  sources) and from a plain pack in the developer worktree (non-isolated `HOME` and NuGet cache);
- the same website tree digest `7b46a8b8…078e` from an earlier two-clone website-only run
  (commit `7d9ad59a`, same website sources).

Toolchain: macOS arm64, .NET SDK 10.0.401, Node v26.10.0, npm 11.19.1.

## What this does not prove

- **Cross-toolchain identity.** Bytes are a function of the toolchain: the .NET SDK (Roslyn,
  NuGet), Node and the npm lockfile. The publish workflows use `ubuntu-latest`,
  `dotnet-version: 10.0.x` (latest patch at run time) and Node 20. Hashes adjudicated on another
  toolchain may not match the gate's rebuild even though each toolchain is reproducible on its
  own. The CI job (below) proves reproducibility on the publish toolchain; the cross-toolchain
  comparison with the hashes above is recorded in `ci-comparison.md` once it has run. To make
  adjudicated hashes checkable, produce them with the publish toolchain (or pin it).
- Windows builds were not tested (the publish jobs do not pack on Windows).

## Guards

- `.github/workflows/reproducible-builds.yml` (PRs touching `src/**`, `website/**`,
  `Directory.*`, `global.json`, the scripts): builds the packages and the site in the checkout and
  in a fresh clone at another path on `ubuntu-latest` and fails on any byte difference. The
  package job seeds Z3 only through the owned bootstrap action; the clone receives those
  verified assets (`reproducible_builds.py clone --z3 copy`) and its build re-verifies them.
- `scripts/test_reproducible_builds.py` (in `test.yml`'s guard job): the props, the build-id
  and module-id configuration (evaluated with Node), the sorted docs walk, that the build script
  runs the publish job's pack commands, and the hash/compare tool itself (zip-timestamp-only,
  content, missing-file and empty-build differences all fail; the website digest is the gate's
  own `tree_digest`).

## Reviews

`reviews/` holds the Codex adversarial review rounds and the verification pass.
