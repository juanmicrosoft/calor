# Review round 2 — Codex (adversarial)

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`
- Input: `git diff origin/main...HEAD` at `e66f27bc` (proof hash JSON and logs excluded), with a
  prompt asking it to verify each round-1 fix and then hunt for new problems.
- Verdict: **CHANGES REQUESTED** (3 findings). Round-1 findings 2, 3, 5 (top level), 6, 7, 8
  verified fixed; 1 left to the maintainer as documented.

## Disposition

| # | Finding | Disposition |
|---|---|---|
| 1 | Root replacement also rewrote paths inside the repo (checkout at `/app` turned `src/app` into `src<root>`), so ids still depended on the checkout path | **Fixed.** The identifier is canonicalized (URL-escape runs decoded, JSON escapes and backslashes folded to `/`), and the root is replaced only where it starts an absolute path (start of text or after a non-path character, followed by `/`, end, or a non-path character). Tests: plugin hook from `/app`, `/src`, `/website` and `/tmp/...` roots gives identical ids; `normalize` leaves `src/app` and a sibling `/tmp/calor-other` alone. |
| 2 | Inventory accepted hollow outputs (any package entry; site without its scripts); helper omitted the SDK inspector | **Fixed.** `compare` requires each package's essential entries (nuspec, tool `calor.dll`, `DotnetToolSettings.xml`, `Microsoft.Z3.dll`; SDK `Sdk.props`/`Sdk.targets`, `Calor.Tasks.dll`, `calor.dll`, `Microsoft.Z3.dll`) and every `_next/static` asset `index.html` loads (at least one script). `reproducible-build.sh` now also runs the publish job's `inspect-sdk-nupkg.sh --all-rids`. Tests for hollow outputs identical on both sides. |
| 3 | README claimed a `ci-comparison.md` that did not exist | **Fixed.** `ci-comparison.md` now records the Linux CI vs macOS comparison (see that file for the result). |

## Review text

Round-1 verification:

| Finding | Result |
|---|---|
| 1 — Toolchain pinning | Left to the maintainer as requested. Version recording works; the documentation issue is reported below. |
| 2 — Z3 directory copying | Fixed: only the six registered assets are copied, then verified in the destination. |
| 3 — Deployment build ID | Fixed for publication: Pages checks out the candidate before building. Website tests also build their checked-out HEAD. |
| 4 — Escaped path normalization | The quoted-root and Windows tests pass. A separate normalization defect remains below. |
| 5 — Empty/partial outputs | Empty outputs and missing top-level surfaces are rejected. Validation of partially populated surfaces remains incomplete below. |
| 6 — Ambient timestamp override | Fixed. MSBuild evaluation confirms environment variables lose to the constant; explicit `-p:` overrides still work. |
| 7 — Environment variation | The second builds receive the configured HOME, caches, timezone, locale and timestamp variables. Ubuntu execution was not independently verified. |
| 8 — Missing paths coverage | Fixed: the workflow has no paths filter. |

Concrete findings:

1. **Major — Root replacement also changes paths inside the repository.**  
   [website/reproducible-module-ids.js:49](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a059f7dbd6fc7f3bc/website/reproducible-module-ids.js:49) replaces every substring matching the checkout root. For a checkout at `/app`, `/app/website/src/app/layout.tsx` becomes `<root>/website/src<root>/layout.tsx`; at `/tmp/calor`, the same module becomes `<root>/website/src/app/layout.tsx`.

   I reproduced this through the plugin’s actual hook using Next-style encoded loader queries: layout/page IDs were `[746, 767]` at `/app`, versus `[15, 691]` at `/tmp/calor`. `/app` is a practical container checkout location. These IDs enter published JavaScript, defeating checkout-path reproducibility. The existing test uses roots that never overlap repository-relative paths.

   **Fix:** parse resource paths and loader-query fields, then replace only an absolute path’s leading repository prefix. Add `/app` and `/website` regression cases.

2. **Minor — The partial-output guard still accepts hollow publication surfaces.**  
   [scripts/reproducible_builds.py:180](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a059f7dbd6fc7f3bc/scripts/reproducible_builds.py:180) accepts any nonempty package entry list. Website validation accepts just the four required files, without executable assets. Correspondingly, [scripts/test_reproducible_builds.py:193](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a059f7dbd6fc7f3bc/scripts/test_reproducible_builds.py:193) calls packages containing only `lib/demo.dll` “complete.”

   The build helper checks Z3, but omits publication’s SDK inspector. Removing `Sdk/Sdk.props` from both builds can therefore pass this proof while publication rejects the package.

   **Fix:** run `inspect-sdk-nupkg.sh --all-rids` in the helper, require essential CLI entries, and add controls where both builds lack required package files or referenced website assets.

3. **Minor — The toolchain disposition claims comparison evidence that is absent.**  
   [docs/plans/evidence/deterministic-builds/README.md:106](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a059f7dbd6fc7f3bc/docs/plans/evidence/deterministic-builds/README.md:106) says `ci-comparison.md` records the Linux-versus-macOS comparison. That file is absent; the supplied proof records Darwin/Node 26.

   **Fix:** add the comparison for matching candidate commits, or explicitly mark it pending and remove the claim that it is recorded. This does not request toolchain pinning.

Twenty-three selected read-only guard tests passed. Full builds and temporary-file tests could not be rerun in this read-only workspace. I found no additional confirmed Z3 supply-chain regression or candidate-HEAD error.

VERDICT: CHANGES REQUESTED
