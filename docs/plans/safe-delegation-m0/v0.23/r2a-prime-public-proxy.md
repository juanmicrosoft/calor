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

**R2A′ gate value: `MET`**, effective 2026-10-01, under these conditions:

1. **R0 dependency.** The value holds only while #1371 and its public-proxy
   amendment are `MET`. If R0 is not `MET` at merge, or later becomes
   `EXPIRED`/`REVOKED`, R2A′ is `NOT_REACHED` (never started) or
   `INVALIDATED` (after start) under the #1370 lifecycle amendments.
2. **Revalidation points.** Before R2B′ starts, before each R2B′ data pull,
   and before #1373 closes, re-check: R0 status; each primary repo's
   license, visibility, and archive state (Section 7).
3. **Scope of `MET`.** `MET` means only that a public-proxy data-access basis
   and a frozen, bias-controlled repository set exist. It does not say that
   any repository has enough eligible tasks, that Calor supports their code,
   or that any organization would adopt Calor.

## 3. Frozen selection rule

This section was committed before any enumeration query ran (see commit
history: the criteria commit precedes the commit adding
[r2a-prime-repos.json](r2a-prime-repos.json)). The executable form is
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
  order. If the reserve runs out, the walk continues through the frozen
  candidate list with the same criteria. No other replacement is allowed.
- **Growth only in order.** If R1 requires more repositories, they are taken
  from the reserve and then the frozen walk, in order. R1/R3 may *remove* a
  repository only by a rule written before inspecting that repository's task
  content, and every removal is recorded.

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
| C12 | **Deferred, mechanical:** the pinned SHA restores and builds on the .NET 10 SDK (10.0.100, rollForward latestMinor) or on the SDK its own `global.json` pins, within 30 minutes, with no source edits | Run at R2B′ preflight; failures replaced in reserve order (Section 3.1) |

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
- **Personal information.** The GitHub Privacy Statement treats usernames
  and profile data as personal information. R2A′ stores no contributor-level
  data. Repository `owner/name` identifiers are published because they are
  required for reproducibility and license attribution; some owners are
  individuals, and their owner login is used only as part of the project
  identifier.

### 5.2 License obligations

- Each primary repository's SPDX identifier is recorded at its pinned SHA.
- Analysis and published aggregates (counts, rates, distributions) carry
  no license obligation under MIT, Apache-2.0, or BSD-2/3-Clause.
- If a later gate stores or publishes copied source, it must keep the
  license and copyright notice with it (all four licenses), keep NOTICE
  content (Apache-2.0 §4(d)), and mark modified files (Apache-2.0 §4(b)).
- No repository name, logo, or trademark is used to imply endorsement of
  Calor (Apache-2.0 §6; BSD-3-Clause clause 3).

### 5.3 What may be published

| Data class | Public repository | Restricted store | Never collected |
|---|---|---|---|
| Repository identifiers, URLs, pinned SHAs, licenses, repo-level metadata and counts | Yes | — | — |
| Per-repository aggregate task counts and eligibility tallies (R2B′) | Yes | — | — |
| Issue/PR numbers used as task IDs (R2B′) | Yes, as public links or hashes | — | — |
| Contributor usernames, reviewer/assignee identities | **No** — omitted, or pseudonymized with a keyed HMAC whose key is kept out of the repository | Key only | — |
| Emails, profile data, organization membership, location | — | — | Yes (never collected) |
| Verbatim issue/PR text | No (link to the public source instead) | Only if R2B′ requires it, under R0 retention rules | — |

### 5.4 Retention and deletion

Retention follows the R0 privacy boundary (#1371 `r0-authorization.md`). For
R2A′ specifically:

- R2A′ collected only repository-level public metadata. It is retained in
  this repository as the gate's evidence; it contains no contributor-level
  data, so no deletion obligation applies to it.
- Any raw API response caches created during enumeration are temporary,
  are not committed, and are deleted at the end of the run.
- If a repository owner asks for removal, or a repository becomes private or
  is deleted, its entry is reduced to a hash of `owner/repo` plus the reason,
  and the change is recorded in Section 7's log.
- Any HMAC key created by later gates is deleted at the earlier of milestone
  0.23 close or 2026-10-29 plus the R0 retention period, making
  pseudonyms irreversible.

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

## 8. Non-authorization boundary

R2A′ creates no participant roster, recruitment, task execution, paid
collection, acceptance service, starter set, or task pool. It does not
satisfy #1284–#1309, #1254, or #1259. A `MET` value here does not transfer
to the original R2A, which remains `UNAVAILABLE`.
