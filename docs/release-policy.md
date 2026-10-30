---
layout: default
title: Release Safety Policy
nav_order: 12
---

# Release Safety Policy

## Immutable candidate promotion

`publish-nuget.yml` uses one `main` dispatch per candidate. Release-specific validation runs
once, packages and the built site are sealed into a v4 immutable artifact, and `publish` waits
for explicit approval in `nuget-release`. Do not dispatch a diagnostic dry run and then a
second real run. `dry_run=true` remains diagnostic-only, not a promotable candidate.

The dispatch SHA fixes the candidate, including on failed-job reruns after `main` advances.
Validation retains every release test project, RID consumer, package inspection, site check,
quality ratchet and expensive corpus/flake check. Required PR checks stay unchanged; passing
PR CI does not establish that its head equals a squash-merged release candidate.

### Trust and approval boundary

Before requesting approval, `scripts/release_promotion.py` reads authenticated GitHub API
responses and checks the repository, workflow path/ID, original run ID/event/main ref/SHA and
active status. Required job conclusions come from GitHub's `needs` context, not artifact
contents: all six prerequisites must succeed. It checks the candidate checkout's version,
CHANGELOG and tag, main's protection, and the approval environment's live settings.

`nuget-release` must require reviewers, prevent self-review, disable administrator bypass,
and use exactly one deployment branch policy: branch `main`, not a tag or wildcard.
GitHub enforces approval when admitting the publish job; scripts cannot manufacture an
approval. No workflow creates or configures this environment. A missing setting or denied API
read blocks promotion. Reviewers must be trusted release maintainers; the maintainer owns that
reviewer selection and the nuget.org Trusted Publishing policy.

Independent review and preventing self-review are this implementation's policy choice, stronger
than requiring explicit human approval alone. They remain fail-closed pending maintainer
selection of the approval policy; no live environment setting change is authorized by this
implementation.

The boundary uses the package job's immutable artifact **ID and SHA-256**, not its mutable
display name alone. API metadata must bind that ID to the same run and candidate, be unexpired
and report the expected digest. The downloaded ZIP must hash to that digest. Before extraction,
duplicate, traversal, unexpected and symlink ZIP entries are refused. `SHA256SUMS` must hash to
the package job's output and cover every file, with no extras: exactly two versioned packages,
candidate metadata, rendered notes and the built site. No artifact can replace checked-out
scripts; downloads live outside the checkout.

The protected publish job repeats these checks before attestation or push. Only that job can
obtain a NuGet OIDC key, after GitHub environment approval. The existing pinned login and
empty-key guard remain fail-closed. Validation has no `id-token` permission, NuGet secret,
publishing environment or write token. The separate `website-deploy` job uses Pages OIDC in
`github-pages`; that environment/workflow job does not match the NuGet publishing policy.

Later jobs download by artifact ID and check the same trusted inventory hash. Publication
retains package attestations, blocking existing-version entry comparisons, served-package
verification, installed-tool verification on three platforms, and tag/release/metadata
identity checks. The site job checks tag identity and protected-main ancestry, then deploys
the validated site's bytes without rebuilding. Its Pages deployment is part of the same
workflow, so deployment failure no longer hides behind a successful dispatch step. Manual
historical deployments through `nextjs-gh-pages.yml` remain separate and unchanged.

### Retries and limits

After partial publication, rerun **failed jobs of the same run**, never all jobs or a new
dispatch. Fresh real dispatches check registry presence before building and refuse if either
package is already served; unknown HTTP responses fail closed. This presence check is not a
substitute for the content comparisons after packaging and around the push.
Successful prerequisite outputs retain the original artifact ID and digests.
Identical existing packages are accepted, pushes skip duplicates, existing releases must
match, and metadata assets are compared rather than overwritten. Retried package-report
artifacts have attempt-specific names; they cannot replace the sealed release artifact.
No retry rebuilds packages or the site once packaging succeeded.

A changed candidate requires new validation. A missing, expired or tampered artifact always
blocks publication; there is no silent rebuild fallback for a partially published version.
An operator must use a separately reviewed recovery procedure or a new version. Artifact
retention is 90 days, but GitHub's approval and rerun windows are shorter (currently 30 days);
this design deliberately does not support promoting arbitrary old runs or importing another
run's artifact. A failed gate never reaches the approval job. A rejected approval publishes
nothing. Benchmark publication remains a separate explicit dispatch.

### Intentional duplicate checks

- Required PR checks and candidate release checks both run: the merged SHA may differ, and
  release validation adds all-RID packaging, quality and expensive coverage.
- RID consumer jobs build local SDK test packages; the package job builds the final distributable
  once. Cross-platform consumer behavior and final package inspection prove different properties.
- Existing-version comparison runs during packaging and immediately before push to cover
  approval-time registry changes. Served-package comparison detects registry races/signatures.
- Artifact identity and inventory checks repeat across trust boundaries and retries. They are
  cheap integrity checks, not rebuilds or reruns of expensive validation.
- Installed-tool checks remain post-publication because local tests cannot prove what a user
  installs from nuget.org. Tag/release/asset checks remain immediately before their writes.
- Font and wording checks repeat on extracted deployment bytes. They do not reinstall npm
  dependencies or rebuild the site.

### Timing evidence and estimate

<!-- drift:ignore -->
The v0.29.0 baseline is historical GitHub API evidence, read on 2026-10-30:

| Existing run | Elapsed minutes | Sum of job-minutes |
|---|---:|---:|
| [Dry validation 40965664099](https://github.com/juanmicrosoft/calor/actions/runs/40965664099) | 36.62 | 122.22 |
| [Real publication 40970444248](https://github.com/juanmicrosoft/calor/actions/runs/40970444248) | 49.73 | 134.15 |
| Sequential total, excluding human gap | 86.35 | 256.37 |

Elapsed is `updated_at - run_started_at` for these completed runs. Job-minutes sum
`completed_at - started_at` across non-skipped jobs from each run's paginated `/jobs` API.
These are runner occupancy minutes, not billable minutes: no OS multipliers or idle/queue
cost model is applied. The old detached website workflow is outside these totals.

**Estimate, not a new measured result:** removing the preliminary dry dispatch avoids its
36.62 elapsed minutes and 122.22 job-minutes in this baseline. Using the real run as a proxy
suggests roughly 50 elapsed minutes plus approval wait and roughly 135 job-minutes, before
new identity checks/downloads and the now-included Pages deployment. Runner/image variability,
NuGet indexing lag and approval latency can dominate. No new release was dispatched to time
this implementation, and no performance improvement is claimed as a hosted measurement.

### Activation constraints

On 2026-10-30, read-only API inspection found `main` protected and `nuget-release` main-only,
with admin bypass disabled, **but no required reviewers**. Promotion intentionally refuses
that configuration. A maintainer must add appropriate reviewers and prevent self-review before
using this path; this implementation changes no live settings. Acceptance of an actual
environment approval, API reads using the workflow token, artifact download digest behavior,
OIDC exchange, publication, retry and Pages deployment still requires an explicitly authorized
hosted run. Offline positive/negative boundary tests are not evidence that these live actions
have executed.

## Formatter write-path gate

`calor format --write`, `calor lint --fix`, and LSP formatting may ship enabled
only while the issue #760 gates remain green:

- lossless preservation of comments, documentation comments, blank-line intent,
  strings, raw C#, user identifiers, types, and member targets;
- exact semantic-token equivalence and formatter idempotence across every
  checked-in parseable `.calr` fixture;
- byte-identical generated C#, Roslyn-clean compilation, and identical public
  API for supported inputs;
- original encoding/BOM and per-line newline preservation;
- same-directory temporary write, revalidation, flush, and atomic replacement;
- failure injection proving the original remains byte-identical;
- an applied LSP formatting edit that recompiles.

No formatter may use a generic regex to infer or rewrite identifiers. Lexical
identifiers are source-level names and must be preserved verbatim; formatting
must not migrate or compact them.

If any gate regresses, CLI and LSP writes must fail closed. Parseable inputs that
already have semantic or generated-C# errors may use the documented conservative
fallback: report unsupported, change no bytes, and return non-zero.
