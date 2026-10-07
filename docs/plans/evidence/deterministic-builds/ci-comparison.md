# Cross-toolchain comparison: macOS vs this PR's Linux CI

Question: do hashes recorded on one machine match a rebuild on the publish toolchain
(`ubuntu-latest`, `dotnet-version: 10.0.x`, Node 20)? The R2 gate needs exactly that.

## Same commit, different OS, CPU and Node — identical

Subject: `d6ba6909` — the `refs/pull/1526/merge` commit that CI run
[37695920469](https://github.com/juanmicrosoft/calor/actions/runs/37695920469) (reproducible-builds
workflow, PR head `4600d900`) built. Locally it was built with
`reproducible_builds.py run --ref d6ba6909…` (two fresh clones, varied environment, 0 differences
between them: `cross-toolchain/macos-ms-sdk-two-clone-comparison.md`).

| | macOS (local) | Linux (CI) |
|---|---|---|
| OS / CPU | Darwin arm64 | Linux x86_64 |
| .NET SDK | 10.0.401, Microsoft build (`dotnet-install.sh --version 10.0.401`) | 10.0.401 (`setup-dotnet` 10.0.x) |
| Node / npm (site) | v26.10.0 / 11.19.1 | v20.20.2 / 10.8.2 |

| Published file | macOS | Linux CI | Same |
|---|---|---|---|
| `Calor.Sdk.0.24.0.nupkg` | `3e0123cbcf326d3b53604289678f5a464e6c04f727db250af3464dacf4e5eb02` | `3e0123cb…eb02` | yes |
| `calor.0.24.0.nupkg` | `84d1c1ab6a1d206b274ee8ed8d42f020f44dd0935a7a7641a860648c85f16ad3` | `84d1c1ab…6ad3` | yes |
| `calor-nuget-d6ba6909….provenance.json` | `5580f5b828f7c90f46496ceb723cb3a7b9574c161a8f932f5bdd86e05ab447f6` | `5580f5b8…47f6` | yes |
| `calor-nuget-d6ba6909….sbom.spdx.json` | `7bd4c79e27fe90ba8fe8fe13680957f17d4a3f9fa226516f5b51625687822fbd` | `7bd4c79e…2fbd` | yes |
| website tree (253 files, gate `tree_digest`) | `168806ccade2a007a54f5bf2797bc022a6285d53831dc755f720273f1a2fe9c3` | `168806cc…e9c3` | yes |

All 97 package entries (bytes and zip timestamps) and all 253 website files: **0 differences**
(`cross-toolchain/macos-vs-linux-{packages,website}.md`; full hash sets in
`cross-toolchain/*.hashes.json`). The package hashes also match a macOS run at this branch's
`1be1a95d` (`cross-toolchain/macos-ms-sdk-1be1a95d-packages.hashes.json`) and CI's run at
`e66f27bc`: package bytes do not depend on the commit when `src/` is unchanged.

## The same version number is not always the same toolchain

The main proof (`proof/`) used Homebrew's .NET SDK, which also reports `10.0.401` but is
**source-built**. Its packages are reproducible among themselves (`2e8b8427…` / `939ff0ce…`)
but differ from Microsoft's 10.0.401 build in:

- `package/services/metadata/core-properties/nuget.psmdcp`: `lastModifiedBy` records the NuGet
  build's target framework (`.NET 10.0` source-built vs `.NET 8.0` Microsoft);
- `calor.dll`, `Calor.Runtime.dll`, `Calor.Tasks.dll` and the two PDBs (same sizes for the DLLs;
  the compiler and runtime version strings in the PDBs are identical, so the difference is in
  other toolchain inputs, not isolated further).

So: **adjudicated hashes must be produced with the same SDK build as the publish job**
(Microsoft's, as `setup-dotnet` installs), and each hash set's `toolchain` field shows which was
used. Node did not matter here (v20 vs v26, identical site).

## Not covered

- A future `10.0.x` patch: `setup-dotnet 10.0.x` and `global.json` `latestMinor` float, so a
  rebuild after a new SDK patch ships may differ. Pinning is a maintainer decision (README).
- Google Fonts: `next/font/google` downloads the four site fonts at build time. Their file
  names are content hashes, so they are reproducible while Google serves the same font
  versions (identical across every run here, hours apart and on two OSes), but a font update
  between adjudication and publication would change the site. Self-hosting the fonts
  (`next/font/local`) would remove this network input; it changes the site's source, so it is
  reported, not done here. A Google Fonts fetch failure also fails the build outright (seen once
  in this PR's `website-tests` browser job, a transient network error).
