# Website Refresh Plan for Calor 0.13.2

**Date:** 2026-08-18
**Status:** Proposal (v3, after two adversarial review rounds)
**Scope:** `website/` — the public docs site at `calor.dev` (Next.js, ~92 MDX docs)

---

## Why this plan exists

The Calor site is thorough and honest but shows meaningful drift from what the
compiler now does. Three problems dominate:

1. **Version drift.** Multiple pages are pinned to `v0.12` while the shipped
   compiler is `0.13.2`. The `refinement-types` reference still narrates a
   "v0.12.1 vs current main" comparison. There is no version indicator in the
   site chrome.
2. **A landing section actively promotes a withdrawn product.**
   `website/src/components/landing/VSCodeExtension.tsx` is 105 lines that link to
   `marketplace.visualstudio.com/items?itemName=calor-dev.calor` and tell
   visitors to "install the Calor Language extension" — while
   `content/changelog.mdx:33` says "VS Code extension support is withdrawn."
3. **The strongest evidence is buried.** Calor found real defects in ILSpy,
   Mapster, Avalonia, FluentFTP, ASP.NET Core, and Newtonsoft.Json
   (`content/cli/static-analysis.mdx:114–123`) — but only as a small table on a
   CLI subpage, and without upstream issue links to source the claims.

This plan fixes drift, kills the misleading section, and *earns* the
compelling-story upgrade by sourcing the findings before promoting them. It
deliberately reverses several consolidation ideas from earlier drafts that would
have destroyed SEO surface or broken inbound links.

---

## What this plan does *not* do

Preserved as explicit non-goals to prevent scope creep:

- **Do not collapse the 10 benchmark metric pages** into an expandable-section
  catalog. Eight of them are 190–320 lines of differentiated content; long-tail
  search traffic depends on them; `<details>`-wrapped content indexes poorly.
- **Do not merge the four AI-agent integration pages** into one tabbed page.
  Word counts are asymmetric (Claude 401 / Codex 116 / Gemini 199 / GitHub 190)
  and users search each agent name as a direct landing query.
- **Do not move any CLI page URLs.** Regroup the sidebar only.
- **Do not use third-party logos** (Microsoft-owned ASP.NET Core / Newtonsoft.Json
  especially) on the landing page. Text names + links only.
- **Do not change the hero tagline.** The current wording ("A programming
  language for coding agents. Fewer errors. Better refactors. Cleaner merges.")
  is short, honest, and does not date.
- **Do not add fake case studies or aspirational quotes.**

---

## Section A — Site vs. code gaps (fix in place)

Each item cites `path:line` so the fix is unambiguous.

### A1 — Bench dashboard mislabeled v0.12 (P0)
**Where:** `website/content/benchmarking/index.mdx:9`, `:17` ("Published v0.12 Metrics").
**Truth:** `CHANGELOG.md` lines 9–18 report the 1.32× headline on a **30-run
statistical** suite in 0.13.2.
**Fix:** Retitle to 0.13.2. Add one sentence explaining the switch from
single-run to 30-run mode.

### A2 — Doc landing has a "v0.12 Adoption and Trust" section header (P0)
**Where:** `website/content/index.mdx:43`.
**Fix:** Drop the version from the heading; renumber the section.

### A3 — Refinement-types page narrates v0.12.1 vs main (P0)
**Where:** `website/content/syntax-reference/refinement-types.mdx` lines **153,
169, 172–175, and 256**. (v1 of this plan said "two rows" — reviewer flagged
line 256, which is inside the walkthrough example.)
**Fix:** Rewrite all four locations to state 0.13.2 behavior directly:
"0.13.2 keeps runtime guards by default; `--elide-proven-guards` removes them
when the verdict is `Proven`." Delete the v0.12.1-vs-main narration entirely
— it is stale in every location.
**Do not** add an "experimental" banner: the page already discloses
Discharged / Failed / Timeout / Boundary status behavior.

### A4 — Agent-task benchmark is six months old (P1)
**Where:** `website/content/benchmarking/index.mdx:65` (artifact dated 2026-02-16).
**Two-part fix:**
1. **This PR:** relabel the row as "point-in-time: February 2026 (pre-0.13.x)"
   and add a note that the composite headline is separate.
2. **Follow-up ticket in the same PR body:** regenerate for 0.13.x. If the
   regenerate cannot land within one release cycle, delete the row — a stale
   number that lingers past that reads as abandonment.

### A5 — Landing "Rules That Enforce Themselves" copy ambiguous about default (P0)
**Where:** `website/src/components/landing/FeatureGrid.tsx:11–13`.
**Truth:** Verification is diagnostic by default in 0.13.2. Runtime guards
remain unless `--elide-proven-guards` is set on a `Proven` verdict.
**Fix:** Replace description with:

> Contracts you write are proved by Z3 and guarded at runtime by default;
> `--elide-proven-guards` removes guards on proven forms.

### A6 — (Withdrawn)
Round-1 adversarial review correctly noted that
`syntax-reference/refinement-types.mdx` already discloses obligation statuses.
A6 as originally scoped (add "Experimental" banner) is unnecessary once A3
lands.

### A7 — Delete the landing VS Code extension section (P0, blocker)
**Where:** `website/src/components/landing/VSCodeExtension.tsx` (105 lines).
**Truth:** `content/changelog.mdx:33` states extension support is withdrawn.
`WhatsNewBanner.tsx` for 0.13.2 does not mention VS Code. Meanwhile the
landing component still says "Install the Calor Language extension" and links
to a Marketplace listing stuck at v0.3.8.
**Fix — three edits, must ship together:**
1. Delete `website/src/components/landing/VSCodeExtension.tsx`.
2. Remove the `<VSCodeExtension />` import and render from
   `website/src/app/page.tsx` (and `website/src/components/landing/index.ts`
   if it re-exports).
3. Delete `trackVSCodeExtensionClick` from `website/src/lib/analytics.ts:54` —
   deleting the component alone leaves a dead export.

**Analytics note:** the deletion removes the `vscode_extension_click` GA
event. Add a one-line note to the PR description so whoever maintains GA
dashboards knows the event will stop firing.

**Related:** `ProjectStatus.tsx:9` lists "VS Code support" as a completed
milestone. Change to "LSP support (any editor)". Same PR.

### A8 — Landing self-contradicts on stable IDs (P0)
**Where:** `FeatureGrid.tsx:28` says "Every function has a stable ID" while
`CompetitivePositioning.tsx:18,75` correctly says "Optional stable IDs".
**Fix:** Change FeatureGrid copy to:

> Add an ID to any declaration and refactoring stops breaking cross-agent
> references.

Preserves the pitch, drops the false universal.

### A9 — No version indicator anywhere (P0)
**Where:** `website/src/components/Header.tsx` and site chrome generally.
**Truth:** 0.13.1 was abandoned; 0.13.2 invalidated Z3 caches. Versionless
docs are a correctness problem, not a cosmetic one.
**Fix:** Add a static "Docs for v0.13.2" pill next to the site title in
`Header.tsx`.

**Mechanism (specified because reviewer flagged this as under-scoped):**
Do **not** import `package.json` from a client component. Add a constant to
`website/src/lib/version.ts` (new file, one export: `SITE_VERSION = '0.13.2'`).
Update it as part of the release script (`scripts/bump-version.sh` or wherever
`Directory.Build.props` is bumped). This decouples the pill from any Next.js
build machinery and makes "bump the pill" a one-line grep-friendly change.

The stale-pill risk is real but small: the pill is intentionally coupled to
`Directory.Build.props`, and bumping the pill is enumerated in the release
checklist. Prefer this coupling over an unversioned site.

### A10 — Hero video autoplays without reduced-motion gate (P1)
**Where:** `website/src/components/landing/Hero.tsx:40–48` autoplays
`calor-lava.mp4` full-bleed. The `prefers-reduced-motion` check at line 19
gates only the stagger animation, not the video.
**Fix:** Two edits in one file:
1. Conditionally render `<video>` vs. just the poster image based on a
   `useMediaQuery('(prefers-reduced-motion: reduce)')` hook.
2. Set `preload="metadata"` on the `<video>` element (not the current
   implicit `auto`) to reduce Core Web Vitals impact.

This is a P1 accessibility + performance fix, not a P3 taste call.

---

## Section B — Sprawl trims (in place, no URL moves)

### B1 — AI-integration pages: accept asymmetry, pad Codex (P2)
**Where:** four pages under `website/content/getting-started/`, line counts
Claude 401 / Codex 116 / Gemini 199 / GitHub 190.
**Fix:** Do not build a partials system in MDX — round-2 reviewer correctly
flagged that as engineering for a $10 problem. Instead: manually pad
`codex-integration.mdx` (the runt) to cover the same three subsections the
others have (`calor init` output, hook configuration, MCP surface). Aim for
~180 lines, not 400. Accept the asymmetry between Claude (the most-used
integration) and the others.

### B2 — CLI reference: regroup sidebar (P2)
**Where:** 30 pages under `website/content/cli/`.
**Fix:** No URL changes. Introduce six sidebar headings:
- **Core** — `compile`, `run`, `watch`, `test`, `init`
- **Analysis & Verification** — `verify`, `analyze`, `static-analysis`,
  `effects`
- **Migration** — `convert`, `migrate`, `import`, `assess`,
  `analyze-convertibility`, `coverage`, `feature-check`
- **Tooling & Integration** — `lsp`, `mcp`, `hook`, `fix`, `format`, `lint`,
  `ids`
- **Diagnostics & Output** — `review-packet`, `envelope-schema`,
  `structured-output` (integrator-facing, not internal — round-1 reviewer
  corrected this)
- **Contributor Tools** — `self-check`, `self-test`, `evaluation`, `benchmark`

### B3 — Benchmark thin pages: source the padding or drop the item (P2)
**Where:** `benchmarking/metrics/refactoring-stability.mdx` (43 lines) and
`edit-precision.mdx` (107 lines) are runts compared to their siblings.
**Fix:** Pull data from `benchmarks/results/` (see the JSON artifacts committed
alongside CHANGELOG updates) and render a per-release trend table on each
page. If the JSON artifacts do not exist for these two metrics in the current
form, **drop this item** — do not turn it into an open-ended "write more prose"
to-do.

Also: add a compact summary table with one row per metric to
`benchmarking/index.mdx` so a reader sees all 8 without clicking through.

### B4 & B5 — Effect and verification story: trim duplicates only (P2)
Keep all pages at their URLs. Audit `philosophy/effects-contracts-enforcement.mdx`
and `philosophy/static-verification.mdx` for material that duplicates the
syntax-reference and guides; delete the duplicates and add cross-links. Net
result: same page count, less redundant reading.

### B6 — Link-check pass (P1)
**Tool:** add `linkinator` to `website/package.json` devDependencies. Add a
script:

```json
"linkcheck": "linkinator out --recurse --skip 'localhost|linkedin|twitter'"
```

Run once after `next build` in this PR to establish baseline; wire into CI in a
follow-up PR. This gives a concrete, runnable tool — round-2 reviewer flagged
that "run a link-check pass" without naming a tool is aspirational.

### B7 — Audit remaining landing components (P2, added round 2)
Round-2 reviewer flagged that only `WhatsNewBanner.tsx` and `ProjectStatus.tsx`
were spot-checked in v2. Full-sweep audit:
- **`WhatsNewBanner.tsx:18`** — already accurate for 0.13.2. No change.
- **`ProjectStatus.tsx:9`** — "VS Code support" milestone is stale. Fix under A7.
- **`Story.tsx`, `CatchBugs.tsx`, `CompetitivePositioning.tsx`,
  `CodeComparison.tsx`, `AskCalor.tsx`, `QuickStart.tsx`, `BenchmarkChart.tsx`**
  — verify each mentions 0.13.2 correctly or is version-agnostic. Fix as
  needed with the same P0 label as A1/A2 (string edits).

---

## Section C — Compelling-story upgrades (earn them)

### C1 — Source the real-world findings, then surface (P1 for step 1, P2 for step 2)

**Step 1 (P1, this PR or the immediate follow-up):** For each of the six
findings in `content/cli/static-analysis.mdx:114–123` (ILSpy, Mapster,
Avalonia, FluentFTP, ASP.NET Core, Newtonsoft.Json), add columns for:
- **File:line** in the upstream repo where the defect lives
- **Diagnostic code** (Calor05xx or Calor09xx range)
- **Upstream status** — one of: "reported and fixed (link)", "reported
  (link, not yet fixed)", or "detected in our converted corpus of vX.Y.Z; not
  filed upstream (rationale)"

For unfiled findings, use the phrasing:

> Detected on our synthetic conversion of {project} at commit {sha}. Not
> validated against upstream head or filed as an upstream issue.

This is accurate without implying negligence. It also creates a shortlist of
"file these upstream" work that a maintainer or contributor can pick up.

Realistic effort: **one week** for a solo maintainer to source six OSS
findings, verify against current head, and file where appropriate — not the "2
days" v1 assumed. Split the sourcing into its own PR if this PR is otherwise
ready to ship.

**Step 2 (P2, only after step 1 lands):** Add a modest "Findings in real .NET
projects" text callout to the landing page (below the existing feature grid).
Link to the sourced table. **No logos.** No hero-tagline changes.

### C2 — Do not change the tagline
Explicit non-goal, restated for clarity.

### C3 — Put the concrete diagnostic in `CatchBugs`, not `FeatureGrid` (P0)
Round-2 reviewer correctly flagged that dropping a raw `Calor0410` block into
`FeatureGrid.tsx` breaks the visual rhythm of the four short-prose cards.

**Fix:** `CatchBugs.tsx` already exists on the landing and is the right home
for a concrete diagnostic. Replace the current body with:

> Your AI forgot a network call. The compiler didn't.
>
> ```
> error Calor0410: Function 'ProcessOrder' uses effect 'network' but does
> not declare it. Call chain: ProcessOrder → NotifyCustomer → SendEmail →
> HttpClient.PostAsync
> ```

Source: `content/philosophy/effects-contracts-enforcement.mdx:210`. Real,
verifiable, concrete.

Leave `FeatureGrid` cards as prose.

### C4 — "What changes on day one" section: ship the GIF or drop the item (P2)
Round-2 reviewer correctly flagged that a "GIF-or-fallback" plan is a
disguised way of shipping the fallback and never doing the GIF.

**Fix — commit to one:** produce a short terminal-cast GIF
(`asciinema rec` → `agg` → `.gif`) showing:
1. `dotnet tool install -g calor`
2. `calor init --ai claude`
3. A `write_file .cs` call being blocked with the actual `calor hook` stderr

Add the GIF to `website/public/` and include on a new "What changes on day
one" panel between `Hero` and `FeatureGrid`. If the GIF cannot land within
this PR cycle, drop C4 entirely.

### C5 — "For you if / Not for you if" table (P1)
Lift from `guides/adoption-playbook.mdx:14–17`, drop into
`content/philosophy/index.mdx` as the first table on the page. Pure-win
change; source material already exists.

### C6 — (Merged into A10)
Video autoplay is A10.

---

## Prioritized checklist

| Prio | Items | Est. effort |
|---|---|---|
| **P0** — Ship in this PR or immediate follow-up | A1, A2, A3, A5, A7 (with dead-export cleanup), A8, A9 (with `src/lib/version.ts`), B7 (audit remaining landing components), C3 (in `CatchBugs.tsx`, not `FeatureGrid`) | 1 day |
| **P1** — Ship within the release cycle | A4 (label + follow-up ticket), A10 (reduced-motion + preload), B6 (`linkinator`), C1 step 1 (source findings), C5 | 3–4 days |
| **P2** — Ship when quiet | B1 (pad Codex), B2 (sidebar regroup), B3 (source data or drop), B4/B5 (trim duplicates), C1 step 2 (landing callout), C4 (GIF or drop) | 3–5 days |
| **P3** | Regenerate agent-task benchmark for 0.13.x — separate work | — |

---

## Adversarial review history

This plan is v3 after two rounds of adversarial review. Both reviewers'
findings were treated as first-class inputs.

**Round 1 — Rejected consolidation-heavy v1.** Key findings incorporated:
- v1 wanted to collapse 10 benchmark metric pages into an expandable-section
  catalog — reviewer correctly noted this destroys long-tail SEO on 8 pages
  of 190–320 lines each. Reversed.
- v1 wanted to merge 4 AI-integration pages into a tabbed view — reviewer
  correctly noted 4x line-count asymmetry (Claude 401 vs. Codex 116) and
  loss of per-agent search hits. Reversed.
- v1 wanted to promote the real-world findings to a landing "Found in the
  wild" section with logos — reviewer flagged legal/reputation risk given no
  upstream issue links exist and the projects include Microsoft-owned marks.
  Downgraded to "source first, then modest text callout".
- v1 missed the entire `VSCodeExtension.tsx` landing section — reviewer
  found it. Escalated to blocker.
- v1 missed the missing version indicator, and understated the hero-video
  accessibility issue. Both promoted.

**Round 2 — Fixed execution details in v2.** Key findings incorporated:
- A9 "read from `package.json`" was under-specified — reviewer noted the
  client-component / Next.js implications. Now specifies a hardcoded
  constant in `src/lib/version.ts` maintained by the release script.
- A7 deletion would leave `trackVSCodeExtensionClick` dead in
  `src/lib/analytics.ts:54`. Now includes the analytics cleanup and a GA
  dashboard note.
- A3 was scoped to "two rows"; the actual file has stale narration at
  four locations including line 256 in the worked example. Scope corrected.
- C3 in `FeatureGrid` would break the four-card visual rhythm. Moved to
  `CatchBugs.tsx` which already exists for exactly this purpose.
- B1 "extract a shared partial" was engineering for a $10 problem in
  `next-mdx-remote`. Downgraded to manual padding of the Codex page.
- B6 named no tool. Now names `linkinator` with an exact script.
- B3 "beef up thin pages" had no source material. Now conditional on
  benchmark JSON artifacts existing, drop otherwise.
- Landing component sweep was incomplete. Added B7.

---

## Open questions

1. **Should A4 escalate to "delete the row if not regenerated within this
   release"?** Round-2 reviewer argued labeling a six-month-old artifact as
   "point-in-time" reads as abandonment. Proposal is to keep the label for
   this PR and open a hard-deadline follow-up.
2. **Who owns C1 step 1?** Sourcing six OSS findings across ILSpy, Mapster,
   Avalonia, FluentFTP, ASP.NET Core, Newtonsoft.Json is ~1 week of work.
   Realistic to defer if maintainer bandwidth is elsewhere.
3. **Is the WhatsNewBanner text at `WhatsNewBanner.tsx:20` accurate for
   0.13.2, or should it be replaced with 0.13.2-specific highlights (Z3
   semantics versioning, MCP memory admission scope fix)?** Currently it
   reads like 0.13.0 language.
