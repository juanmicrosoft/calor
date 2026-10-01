# M0-R2A′ replacement gate: public-proxy authority

**Recorded:** 2026-10-01. **Issue:** #1373 (parent epic #1370).
**Authority:** maintainer decision of 2026-10-01, recorded by R0 (#1371) in
`r0-authorization.md` and `amendment-001-public-proxy.md` in this directory.
**Original R2A value:** `UNAVAILABLE` — see
[r2a-adopter-unavailable.md](r2a-adopter-unavailable.md).
**Result label (mandatory on every downstream use):**
**"AI-adjudicated, public-proxy domain."**

This document defines R2A′ and selects its proxy repositories from metadata
only. It does **not** measure task supply, count eligible tasks, or inspect
issue/PR/commit content. Those steps belong to R2B′ (#1375) after R1 (#1372)
freezes decision rules and R3 (#1374) freezes the domain boundary.

## 1. What R2A′ is

R2A′ replaces the absent adopter with a fixed set of public .NET open-source
repositories. Their OSI licenses and the public GitHub terms replace the
signed conditional agreement. Only public data is used. No person is
contacted, recruited, enrolled, or asked for consent.

| R0 parameter (from the 2026-10-01 decision) | Value applied here |
|---|---|
| Data | Public GitHub repositories and public metadata only |
| Human contacts | None (no maintainers, contributors, or users contacted) |
| Participants / recruitment | None |
| Window | Ends 2026-10-29 |
| Cash cap | ≈USD 200 of API spend for the whole v0.23 inquiry (shared; R2A′ spend is logged in [spend-ledger.md](spend-ledger.md)) |
| Adversarial review cap | ≤5 rounds per artifact |
| Labeling | "AI-adjudicated, public-proxy domain"; no claim about organizational adoption |

## 2. Gate value and conditions

**Current R2A′ gate value: `NOT_REACHED` (pending R0 evidence).**
**Value on activation: `MET`.**

The R0 records that carry the 2026-10-01 maintainer decision
(`r0-authorization.md`, `amendment-001-public-proxy.md`) are being written
in a parallel R0 (#1371) change and are not in this branch. A conditional
statement here cannot prove that R0 holds. Therefore:

1. **Activation.** R2A′ becomes `MET` only when both R0 records are merged to
   `main` with R0 value `MET`, and their scope covers: public GitHub
   metadata access, no human contact, the 2026-10-29 window, and the
   USD 200 cap. The activating change must cite the R0 merge commit in the
   Section 7 log. Until then, nothing in this document unlocks R1, R3, or
   R2B′ work.
2. **Provisional evidence.** The Section 4 snapshot was collected on
   2026-10-01 under the maintainer decision as relayed to the implementing
   agent that day (public data only; no contacts). It is provisional
   evidence. If R0 closes `NOT_AUTHORIZED`, R2A′ stays `NOT_REACHED`, the
   snapshot is marked void in Section 7, and it may not be reused by any
   gate. If R0 closes `MET` with a narrower scope than Section 1, the
   snapshot is re-checked against that scope before activation.
3. **Later loss of R0.** If R0 later becomes `EXPIRED` or `REVOKED`, R2A′
   becomes `INVALIDATED` under the #1370 lifecycle amendments.
4. **Revalidation points.** Before R2B′ starts, before each R2B′ data pull,
   and before #1373 closes, re-check: R0 status; each primary repo's
   license, visibility, and archive state (Section 7).
5. **Scope of `MET`.** `MET` means only that a public-proxy data-access basis
   and a frozen, bias-controlled repository set exist. It does not say that
   any repository has enough eligible tasks, that Calor supports their code,
   or that any organization would adopt Calor.

## 3. Frozen selection rule

Sections 3.1–3.3 were committed (`15fba333`) before any enumeration query
ran; the commit adding [r2a-prime-repos.json](r2a-prime-repos.json) follows
it. Limitation: these commits were created locally and first pushed together,
so the ordering is attested by commit parentage, not by a server timestamp.
Changes made after the snapshot (Section 3.4, the continuation and removal
rules in 3.1) only restrict later discretion; none changes C1–C11, the seed,
or the counts. The executable form is
[tools/r2a_prime_enumerate.py](tools/r2a_prime_enumerate.py); its constants
must equal the values below.

### 3.1 Bias controls

The selection must not favor code Calor already handles well.

- **No code-shape criteria.** No criterion refers to language features,
  project type, domain, async usage, nullability, LINQ, unsafe code, test
  framework, or past Calor conversion results. Domain fit is R3's job and
  must be applied by R3's frozen rule, not by choosing repositories.
- **Calor-exposed repositories are excluded (C10).** Any `owner/repo` that
  appears as a `github.com/owner/repo` string in a tracked Calor file at the
  criteria-freeze commit (excluding this v0.23 directory) is excluded. This
  removes the three vendored round-trip corpora (MediatR, Serilog,
  FluentValidation) and every repository cloned by
  `tests/E2E/project-init/run-tests.sh` (Humanizer, Polly, Newtonsoft.Json,
  Dapper, and others); the exact list is `calor_exposed_repos` in the JSON. The compiler and converter have been
  tuned against these; including them would favor Calor. They are not a
  secondary stratum and carry no R2A′ role.
- **Selection by seeded hash order, not by judgment.** Candidates are ordered
  by `sha256(seed + "\n" + lower(owner/repo))` ascending. The seed is
  `calor-1373-r2a-prime-v1:72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1`, where
  the SHA is the `main` HEAD (v0.22.0 release) that existed before this
  work began. The selector cannot choose the seed after seeing candidates.
- **Fixed counts.** The first 12 eligible repositories in walk order form the
  primary set. The next 12 eligible form an ordered reserve.
- **Replacement only in order.** Any later exclusion (Section 7, or the
  deferred build check C12) is filled by the next reserve repository in
  order. No other replacement is allowed.
- **Continuation after reserve exhaustion.** If the 12 reserves run out, a
  versioned continuation run (`--continue-from`, output
  `r2a-prime-repos-cont-<n>.json`) walks the *frozen* candidate order of
  the Section 4 snapshot from the next unwalked rank (132) with the same
  criteria and the same fixed 12-month window. It never re-runs the
  candidate search. Its metadata, pins, and licenses are live at its own
  retrieval time; this comparability limit is recorded in its output. A
  continuation run starts only when a recorded exclusion leaves fewer
  repositories than required, never at a time chosen for other reasons.
- **Growth only in order.** If R1 requires more repositories, they are taken
  from the reserve and then by continuation, in order.
- **Removal only by global rule.** The selected names and their activity
  counts are now public, so a repository-specific exclusion could be
  tailored to them. R1/R3 may remove repositories only by a rule that
  (a) is stated without naming any repository, (b) is frozen and committed
  before it is applied, (c) is applied to all 24 primary and reserve
  repositories at once, and (d) has no exceptions. Every removal is logged
  in Section 7 with the rule and commit. All 12 original primary
  repositories stay in the reported denominator, with their exclusion
  reason if removed.
- **Global rules can still be tailored.** A rule that names no repository
  (for example, "exclude Unity projects") can still be written with the
  public sample in mind. Two further controls apply. (a) Before
  application, each removal rule is reviewed by the R1 methods reviewer
  against the disclosed 24-repository sample, with the list of repositories
  it would remove shown to the reviewer. (b) Any exclusion that depends on
  what Calor supports (R3's support intersection) is a coverage cost of
  Calor, not a neutral filter: R2B′ and R4 must report, per repository, the
  fraction of otherwise-eligible tasks removed by Calor-support rules,
  alongside and separate from comparative results.

### 3.2 Candidate query (GitHub search, metadata only)

```text
language:csharp stars:<lo>..<hi> pushed:>=2026-07-03 created:<=2023-10-01
fork:false archived:false is:public
```

Star partitions: 500–999, 1000–1999, 2000–4999, 5000–9999, ≥10000. Any
partition reporting more than 1000 results is split in half recursively.
The union of names is the frozen candidate list.

### 3.3 Eligibility criteria (checked per repository in walk order)

| ID | Criterion | Evidence source |
|---|---|---|
| C1 | GitHub primary language is `C#` | GraphQL `primaryLanguage` |
| C2 | Public; not a fork, mirror, template, archived, or disabled | GraphQL repository flags |
| C3 | Created on or before 2023-10-01 (≥3 years of history) | `createdAt` |
| C4 | Pushed on or after 2026-07-03 (activity within 90 days) | `pushedAt` |
| C5 | ≥500 stars (a coarse signal of real users) | `stargazerCount` |
| C6 | License SPDX in {`MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`} **and** the license detected at the pinned SHA equals it | GraphQL `licenseInfo`; REST `GET /repos/{r}/license?ref={sha}` |
| C7 | ≥50 commits on the default branch in [2025-10-01, 2026-10-01) | GraphQL `history(since, until).totalCount` |
| C8 | Issues enabled; ≥24 PRs merged and ≥12 issues opened in the same window | GraphQL search `issueCount` (count only) |
| C9 | Not authored by the Calor maintainer: owner not `juanmicrosoft`/`calor-lang`; zero issues/PRs involving, and zero commits authored by, either maintainer-associated account (`juanmicrosoft`, and `juanatjcx`, the account that runs the enumeration) | owner; GraphQL search `issueCount`; REST commit search `total_count` |
| C10 | Not Calor-exposed (Section 3.1) | `git grep` at the freeze commit |
| C11 | Repository disk usage ≤1 GB | GraphQL `diskUsage` |
| C12 | **Deferred, mechanical:** the pinned SHA builds under the frozen protocol in Section 3.4 | Run at R2B′ preflight; failures replaced in reserve order (Section 3.1) |

Window and pin: the pinned SHA is the default-branch HEAD at retrieval.
The preceding-12-month window is [2025-10-01, 2026-10-01).

Why permissive licenses only (C6): R2B′ and later gates may need to copy
or translate code into derived artifacts. Copyleft licenses (GPL, LGPL, MPL,
MS-RL) would attach obligations to those artifacts. This excludes some
repositories; the exclusion is independent of code shape and is stated as
a known coverage limit.

Why activity counts (C7, C8) are not task inspection: they are gross counts
of all merged PRs, opened issues, and commits. No title, body, label, diff,
author list, or link is requested. R2B′ eligibility under the R1/R3 rules
is a different, later measurement and may find zero eligible tasks in a
repository that passes C8.

### 3.4 C12 build protocol (frozen in review rounds 1–2, before any build)

No build has run. This protocol was added after the Section 4 snapshot was
public, in response to review objection R1-2; it names no repository.

1. **Environment.** Linux x64, Docker image
   `mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29`
   (multi-arch index digest retrieved 2026-10-01; the linux/amd64 entry is
   used). Container limits: 4 CPUs, 16 GB memory, 64 GB disk. No extra
   workloads installed. Network access to `api.nuget.org` and to package
   sources declared in the repository's own `NuGet.config`. If the
   repository's root `global.json` pins an SDK major.minor other than 10.0,
   use `mcr.microsoft.com/dotnet/sdk:<major.minor>` at the digest that tag
   resolves to on 2026-10-01 (recorded in the C12 output); if that tag does
   not exist, the result is a failure.
2. **Entry point.** The single `.sln`/`.slnx` file at the repository root; if
   there are several, the one whose base name equals the repository name
   (case-insensitive), else the lexicographically first; if none, the
   lexicographically first `.csproj` at minimum directory depth.
3. **Command.** `dotnet build <entry> -c Release -p:TreatWarningsAsErrors=false
   -p:EnableWindowsTargeting=true -p:NuGetAudit=false`, 30-minute limit. No
   source edits, no workload installs, no repository build scripts.
4. **Infrastructure failures.** A failed attempt is classed *infrastructure*
   only if its log matches at least one of these case-insensitive regexes
   **and** contains no `error CS\d{4}`, `error MSB\d{4}` other than
   `MSB3073`/`MSB4181`, or `error NETSDK\d{4}` line:
   `Response status code does not indicate success: 5\d\d`,
   `NU1301`, `The SSL connection could not be established`,
   `Name or service not known`, `Resource temporarily unavailable`,
   `No space left on device`, `OutOfMemoryException`, `exit code 137`.
   Infrastructure failures are retried, up to 3 attempts in total. Any
   other failure, or an infrastructure failure on all 3 attempts, is a
   repository failure. A compiler/MSBuild/SDK error line takes precedence
   over any infrastructure match.
5. **Outcome.** Pass = exit code 0. The log is kept in the restricted store;
   only pass/fail, attempt count, failure class, image digest, and duration
   are published.
6. **Disclosed coverage bias.** This excludes projects needing Unity, MAUI,
   Xamarin, Windows-only native tooling, or custom build scripts. The
   restriction applies to all three arms, but repository survival may still
   correlate with Calor suitability (plain SDK-style projects are closer to
   what Calor targets). It is therefore reported as a possible selection
   bias, with the list of C12 failures and their failure classes.

Known biases left in place (stated, not corrected):

- Stars, age, and activity thresholds favor popular, long-lived libraries
  and frameworks over application code and internal-style codebases.
- GitHub primary language is a byte-count heuristic; repositories with large
  non-C# components may pass.
- GitHub search is eventually consistent; the committed snapshot, not a
  re-run, is authoritative.

## 4. Enumeration result

Run on 2026-10-01 (retrieval `2026-10-01T18:28:29Z`) against criteria-freeze
commit `15fba333792c8c1949ee38a3ee35f3b44a6e82ca`. Full evidence, including
every walked repository and its failed criteria, is in
[r2a-prime-repos.json](r2a-prime-repos.json).

Process notes (post-freeze changes, both mechanical):

- The first run crashed on the first walked repository while parsing the
  `gh --jq` license output, before any eligibility decision was recorded.
  The fix (`tojson`) is a separate commit; no selection parameter changed.
- `derive_views()` adds per-repository summary records that are a pure
  projection of the `walked` evidence.
- License-file identity (path, blob SHA) for the 24 selected repositories
  was added after enumeration (latest retrieval `2026-10-01T18:50:43Z`,
  recorded per record as `license_identity_retrieval_utc`); all lookups
  returned status `ok` with no SPDX mismatch.
- **C6 error audit.** The enumeration-time code mapped any license-lookup
  error to "no license". An audit of all 50 C6 failures found none caused by
  a retrieval error: 47 have a repository-level license outside the
  allowlist (`NOASSERTION`, GPL/AGPL, MS-PL), 2 have no license at either
  level, and 1 (`CesiumGS/cesium-unity`) reports `Apache-2.0` at repository
  level but `NOASSERTION` at the pinned SHA, which fails C6's equality
  test as written. The script now aborts on any non-404 lookup error.

| Quantity | Value |
|---|---:|
| Candidates from the five star partitions (531 + 344 + 311 + 120 + 81) | 1,387 |
| Search results flagged `incomplete_results` | 0 partitions |
| Candidates walked (hash order) to fill 12 + 12 | 131 |
| Eligible among walked | 24 |
| Failed C8 public history / C6 license / C7 commits / C11 size / C10 exposed / C2 state | 76 / 50 / 48 / 8 / 2 / 1 (a repo can fail several) |
| Failed C9 maintainer association | 0 |

### 4.1 Primary set (12)

Rationale for every entry is identical: it is among the first 12 hash-ordered
candidates that pass C1–C11. No other reason was used.

| Rank | Repository | License (SPDX) | Pinned SHA | Stars | Commits / merged PRs / issues (12 mo) |
|---:|---|---|---|---:|---|
| 21 | [kimmknight/raweb](https://github.com/kimmknight/raweb) | MIT | `2f0dc16a6d` | 857 | 793 / 119 / 65 |
| 26 | [STranslate/STranslate](https://github.com/STranslate/STranslate) | MIT | `75f616a257` | 8,153 | 821 / 29 / 219 |
| 29 | [quartznet/quartznet](https://github.com/quartznet/quartznet) | Apache-2.0 | `15d90a9c26` | 7,090 | 1,422 / 771 / 344 |
| 30 | [microsoft/semantic-kernel](https://github.com/microsoft/semantic-kernel) | MIT | `ee23e07cb9` | 28,621 | 320 / 337 / 253 |
| 39 | [featbit/featbit](https://github.com/featbit/featbit) | MIT | `21b2af4feb` | 1,909 | 156 / 162 / 31 |
| 41 | [icsharpcode/CodeConverter](https://github.com/icsharpcode/CodeConverter) | MIT | `2606077264` | 913 | 252 / 39 / 19 |
| 56 | [dotnet-outdated/dotnet-outdated](https://github.com/dotnet-outdated/dotnet-outdated) | MIT | `5002ab07f4` | 1,691 | 65 / 61 / 27 |
| 61 | [dotnet/dotnet-monitor](https://github.com/dotnet/dotnet-monitor) | MIT | `862c58e491` | 726 | 438 / 954 / 13 |
| 65 | [googleads/googleads-mobile-unity](https://github.com/googleads/googleads-mobile-unity) | Apache-2.0 | `8465b4c82a` | 1,550 | 256 / 228 / 60 |
| 68 | [SubtitleEdit/subtitleedit](https://github.com/SubtitleEdit/subtitleedit) | MIT | `ace272afe0` | 14,389 | 9,119 / 3,327 / 1,848 |
| 79 | [apache/lucenenet](https://github.com/apache/lucenenet) | Apache-2.0 | `b447bbf04d` | 2,417 | 182 / 171 / 73 |
| 83 | [irihitech/Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) | MIT | `0036334ae5` | 1,952 | 151 / 86 / 79 |

Full 40-character SHAs are in the JSON.

### 4.2 Ordered reserve (12)

In order: microsoft/fluentui-blazor (86), helix-toolkit/helix-toolkit (87),
tixl3d/tixl (98), dotnet/ClangSharp (101), MarimerLLC/csla (103), dotnet/iot
(104), microsoft/EventLogExpert (109), ScottPlot/ScottPlot (118),
robinrodricks/FluentFTP (121), grpc/grpc-dotnet (124),
testcontainers/testcontainers-dotnet (128), MapsterMapper/Mapster (131).
All MIT except grpc/grpc-dotnet (Apache-2.0).

### 4.3 Observations recorded without action

These are noted so that no one later removes a repository by judgment:

- `googleads/googleads-mobile-unity` is a Unity plugin and may fail C12
  (build on the .NET SDK). If so, C12 replaces it in reserve order.
- `microsoft/semantic-kernel` is a multi-language repository (C# primary by
  GitHub's byte count). It stays; R3 decides which parts are in domain.
- `icsharpcode/CodeConverter` is itself a code-translation tool. It stays;
  this is not a reason to exclude under any frozen criterion.
- Very high-activity repositories (e.g. SubtitleEdit, 3,327 merged PRs)
  would dominate pooled task counts. R1 must decide per-repository caps or
  clustering before R2B′ counts; R2A′ does not.

## 5. Data-access terms

R2A′ has no signed agreement. Its data-access basis is the combination below.
Every downstream gate that touches proxy data must stay inside it.

### 5.1 GitHub terms and rate limits

- **Terms of Service and Acceptable Use Policies.** Access uses the
  maintainer's authenticated `gh` CLI and the official REST/GraphQL APIs.
  No scraping of HTML, no account automation beyond API calls, no attempt
  to bypass rate limits. The GitHub Acceptable Use Policies ("Information
  Usage Restrictions") permit researchers to use *public, non-personal*
  information for research only if resulting publications are open access,
  and forbid use for spam or for selling personal information. All
  R2A′/R2B′ outputs are published in this public repository, and R2A′
  retains no personal information beyond project identifiers (below).
- **Rate limits.** Authenticated REST: 5,000 requests/hour; search: 30
  requests/minute (the script sleeps 2.5 s between search calls); GraphQL:
  5,000 points/hour. The script backs off on rate-limit or secondary-limit
  errors and does not parallelize requests.
- **Collection boundary (what the API returns versus what is kept).**
  Not every endpoint supports server-side field selection, so the record
  states exactly what is received:
  - GraphQL issue/PR searches select only `issueCount`; the server returns
    no item fields.
  - REST repository search returns full repository objects, including owner
    profile fields (login, avatar URL, account type). The script keeps only
    `full_name` in memory and writes nothing else.
  - REST commit search is restricted to `author:` the two maintainer
    accounts. Any items it returns are the maintainer's own commits; they are
    discarded inside the `gh` process by `--jq .total_count`. In the snapshot
    every such count was 0.
  - REST license endpoint returns the license file; only SPDX id, path, and
    blob SHA are kept.
  - Nothing is cached to disk except the committed JSON. No issue, PR, or
    commit title, body, label, diff, or third-party author was requested.
- **Personal information.** The GitHub Privacy Statement treats usernames
  and profile data as personal information. R2A′ stores no contributor-level
  data. Repository `owner/name` identifiers are published because they are
  required for reproducibility and license attribution. Some owners are
  individuals, so their login *is* personal information here. It is used for
  one purpose only: identifying the project.
- **Basis for each data class (stated separately).** The AUP research
  permission covers *public, non-personal* information only; it is not
  cited for personal data. For repository identifiers that contain an
  individual's login, the basis is narrower: AUP §8 allows use of personal
  information for the purpose the user authorized, and an owner who
  publishes a repository under `owner/name` authorizes that name to identify
  the public project. R2A′ uses it for nothing else. **Contributor-level
  data** (authors, reviewers, assignees, even pseudonymized) has no basis
  under R2A′. It may be collected later only if R0 or the R2B′ record
  establishes a separate basis first; otherwise R2B′ works without it.

### 5.2 License obligations

- Each selected repository's detected SPDX id, license-file path, and
  license-file blob SHA are recorded at its pinned SHA (JSON
  `license_file_*_at_pinned_sha`).
- **Scope of that finding.** GitHub's license endpoint detects one top-level
  license file. It does not certify that every file, vendored directory,
  submodule, asset, or historical revision carries the same license. Before
  any later gate copies or translates code from a repository (including
  translation into Calor or into protected C#), it must check the license of
  the specific files and revisions used: file headers, `NOTICE`,
  third-party/vendored directories, and submodules. A HEAD relicense never
  substitutes for that check; the grant at the pinned or task revision
  governs.
- **Issue and PR text is not covered by the code license.** It is user
  content under the GitHub Terms of Service. Later gates link to it in the
  restricted store; they do not copy it into public artifacts.
- Analysis and published aggregates (counts, rates, distributions) carry
  no license obligation under MIT, Apache-2.0, or BSD-2/3-Clause.
- Derived artifacts that contain copied or translated code (including
  Calor translations) are derivative works. They must keep the license text
  and copyright notices (all four licenses), keep applicable `NOTICE`
  content (Apache-2.0 §4(d)), and carry a modification notice (Apache-2.0
  §4(b)).
- No repository name, logo, or trademark is used to imply endorsement of
  Calor (Apache-2.0 §6; BSD-3-Clause clause 3).

### 5.3 What may be published

| Data class | Public repository | Restricted store | Never collected |
|---|---|---|---|
| Repository identifiers, URLs, pinned SHAs, licenses, repo-level metadata and counts | Yes | — | — |
| Per-repository aggregate task counts and eligibility tallies (R2B′) | Yes (aggregates only) | — | — |
| Issue/PR numbers or links used as task IDs (R2B′) | **No** — they lead directly to authors | Yes, under R0 rules | — |
| Contributor usernames, reviewer/assignee identities | **No** | Not under R2A′. Only if R0/R2B′ first establishes a basis (§5.1), and then only as keyed-HMAC pseudonyms (pseudonymous, not anonymous) with the key outside the repository | — |
| Emails, profile data, organization membership, location | — | — | Yes (never collected) |
| Verbatim issue/PR text | No | Only if R2B′ requires it, under R0 retention rules | — |
| Build logs (C12) | No (pass/fail summary only) | Yes | — |

### 5.4 Retention and deletion

Retention follows the R0 privacy boundary (#1371 `r0-authorization.md`). R2A′
adds these rules:

- **Purpose limitation.** Repository identifiers, including individual
  owner logins, are retained only to identify the proxy repositories for
  #1370 gates and their audit trail. They are not joined with any other
  personal data.
- **Raw responses.** Raw API responses are not written to disk; there is no
  cache to delete.
- **Removal request or loss of public status.** If an owner asks for removal,
  or a repository becomes private or is deleted: (1) the entry is replaced
  by a placeholder `withdrawn-<n>` with the reason and date in every current
  file this project controls (`candidate_walk_order`, `walked`, summary
  records, tables in this document, and any R2B′ outputs); (2) restricted
  copies of its data are deleted; (3) the event is logged in Section 7. A
  hash of the name is **not** used as a placeholder, because a hash of a
  publicly enumerable name is reversible.
- **Limits of removal.** Removal does not erase the name from the version
  history of this public repository or from third-party copies (forks,
  mirrors, archives). Rewriting public history is a separate maintainer
  decision. This limit is stated, not hidden.
- **Pseudonym keys.** Any HMAC key created by later gates is deleted at
  milestone 0.23 close or at the R0 retention deadline, whichever is first.
  Key deletion prevents re-linking pseudonyms to usernames, but restricted
  task links could still identify authors; those links are deleted under
  the same deadline.

## 6. What R2A′ replaces and what it cannot replace

| Original R2A element | R2A′ substitute | What is lost |
|---|---|---|
| Organizational signatory with authority | Maintainer decision (R0 amendment) + OSI license grant | No one with organizational authority consents. No party is accountable for the proxy's fitness. |
| Conditional planning-support commitment | None | No organization commits to anything. Results cannot show willingness to adopt. |
| Permitted metadata access | Public GitHub API data under GitHub terms | No private trackers, internal code review, incident data, or private backlogs. |
| Recruitment authority boundaries | No recruitment at all | No participants, reviewers, or adopter staff. Reviewer capacity must come from elsewhere (AI adjudication) and is labeled so. |
| Internal constraints (security, compliance, toolchain, release process) | Public build/test configuration only | No real organizational constraints, approval gates, or deployment risk. |
| Real workload horizon | Historical public issues/PRs in a fixed window | No forward assigned-request commitment; public OSS work is volunteer-driven and differently scoped. |
| Confidentiality terms | Not needed (public data) | Public tasks may already be in model training data (contamination). R2B′/R4 must treat this as a threat to validity. |
| Expiry/withdrawal by the adopter | License change, archive, deletion (Section 7) | No negotiated notice period. |
| Ownership of derived results | Calor repository, subject to Section 5.2 | — |
| Adoption signal | **None** | R2A′ produces no evidence about organizational adoption, demand, or willingness to pay. |

## 7. Expiry, withdrawal, and revalidation

| Event | Detected by | Effect |
|---|---|---|
| R0 or its public-proxy amendment becomes `EXPIRED`/`REVOKED` | R0 record | R2A′ → `INVALIDATED`; stop; descendants per #1370 |
| Window passes 2026-10-29 without #1373 closure | Date | R2A′ → `EXPIRED` |
| A primary repo's license at its pinned SHA differs from the recorded SPDX | Revalidation | Not possible by Git semantics; if detected, treat as a data error and exclude |
| A primary repo relicenses at HEAD after pinning | Revalidation | Pinned SHA keeps its original license grant; repo stays. Recorded. |
| A primary repo becomes private, deleted, or blocked | Revalidation | Exclude; replace from reserve in order; delete any non-public cached data |
| A primary repo is archived | Revalidation | Stays (history is still public); recorded |
| Owner asks for exclusion | Inbound request (no outreach) | Exclude; replace from reserve in order |
| C12 build check fails at R2B′ preflight | R2B′ | Exclude; replace from reserve in order |
| Reserve and frozen walk exhausted below the R1-required count | R2B′ | R2B′ reports the shortfall; no new query and no criteria change without a versioned R2A′ amendment |

Change log (append-only):

| Date | Repository | Event | Action |
|---|---|---|---|
| 2026-10-01 | — | Gate defined | Criteria frozen (`15fba333`) |
| 2026-10-01 | — | Enumeration | 12 primary + 12 reserve selected (Section 4) |
| 2026-10-01 | 24 selected | License-file identity added (path, blob SHA) | No SPDX mismatch found |
| 2026-10-01 | — | Review round 1 | Gate value set to `NOT_REACHED` pending R0; C12 protocol, removal and continuation rules frozen ([round 1](reviews/r2a/round-1-codex.md)) |
| 2026-10-01 | — | Review round 2 (0 blocking) | C12 digest/limits/retry matchers frozen; removal-rule review and Calor-support coverage reporting; per-class data basis; continuation chaining and license-error handling fixed ([round 2](reviews/r2a/round-2-codex.md)). Review loop stopped at zero blocking |

## 8. Non-authorization boundary

R2A′ creates no participant roster, recruitment, task execution, paid
collection, acceptance service, starter set, or task pool. It does not
satisfy #1284–#1309, #1254, or #1259. A `MET` value here does not transfer
to the original R2A, which remains `UNAVAILABLE`.
