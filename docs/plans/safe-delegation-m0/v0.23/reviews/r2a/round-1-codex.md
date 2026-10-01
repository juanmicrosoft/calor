# R2A′ adversarial review — round 1 (Codex)

**Date:** 2026-10-01. **Reviewer:** Codex CLI 0.159.2 (`codex exec -s read-only`),
model `gpt-6.1-sol`, prompted as a hostile reviewer for Calor-favoring
selection bias, license problems, and task-content leakage.
**Reviewed commit:** `fb2a610f` (criteria freeze `15fba333`, parse fix
`502845dd`, snapshot `fb2a610f`).
**Verdict from reviewer:** 1 blocking / 6 major / 0 minor.

Objections are summarized; the reviewer's wording is condensed but each
point is preserved.

| # | Severity | Objection | Disposition |
|---|---|---|---|
| 1 | blocking | `MET` lacks authority evidence: `r0-authorization.md` and `amendment-001-public-proxy.md` are cited but absent from the branch; a conditional cannot show R0 holds. Keep R2A′ `NOT_REACHED` until the evidence exists. | **Accepted.** §2 now records current value `NOT_REACHED` (pending R0 evidence) with value `MET` on activation, defines the activation condition (R0 records merged with `MET` and matching scope, cited in §7), and marks the snapshot as provisional evidence that is voided if R0 closes `NOT_AUTHORIZED`. |
| 2 | major | Deferred C12 build check leaves discretion (OS, configuration, workloads, retries, SDK choice); §4.3 anticipates removing Unity code. | **Accepted.** New §3.4 freezes the C12 protocol (Linux x64 SDK image, entry-point rule, exact command, retry/infrastructure classification, published fields) before any build, names no repository, and discloses the toolchain coverage bias. |
| 3 | major | Later R1/R3 removals can be tailored to the now-public sample, because the rule only requires that removal precede inspection of *that repository's* task content. | **Accepted.** §3.1 "Removal only by global rule": rules must name no repository, be frozen and committed before application, apply to all 24 primary+reserve repos at once, have no exceptions, and be logged; all 12 original primaries stay in the reported denominator. |
| 4 | major | "Count only" is false at the REST boundary: commit search and repository search return item data that is filtered locally. | **Accepted.** Script docstring, JSON `content_inspected`, and new §5.1 "Collection boundary" now state exactly what each endpoint returns versus what is kept. Commit search is restricted to the maintainer's own accounts (all counts 0); repository-search objects are projected to `full_name`; no raw responses are written. |
| 5 | major | Detected SPDX is presented as broader license authority than it is; no license-file identity; derived (translated) artifacts not covered. | **Accepted.** License-file path and blob SHA at the pinned SHA were added for all 24 selected repositories (`--add-license-identity`, no mismatch). §5.2 now limits the finding to the detected top-level file, requires file/revision-specific checks before copying or translating, states that issue/PR text is not under the code license, and covers Calor/protected-C# translations as derivative works. |
| 6 | major | Privacy/deletion contradictions: individual owner logins are personal data yet "no deletion obligation"; hashes of public names are reversible; history and copies persist; HMAC key deletion does not remove identifying links. | **Accepted.** §5.4 rewritten: purpose limitation; placeholder `withdrawn-<n>` (not a hash) across all controlled files; explicit limits for version history and third-party copies; pseudonyms described as pseudonymous, not anonymous; restricted task links deleted on the same deadline as keys. §5.3 no longer publishes issue/PR numbers. |
| 7 | major | Reserve exhaustion does not preserve a frozen eligibility snapshot; only 131 of 1,387 candidates were evaluated, and the script would re-run live search. | **Accepted in part.** The script gains a versioned `--continue-from` mode that walks the frozen candidate order from the next unwalked rank without re-running search, with the fixed 12-month window; its output records the live-metadata comparability limit. §3.1 limits when a continuation may start (only after a logged exclusion leaves a shortfall). Pre-evaluating all 1,387 candidates was rejected: it would multiply API calls ~10x for repositories that are unlikely to be needed, and it would still not freeze later pins. |

**Outcome:** 1 blocking objection, resolved by changing the recorded value
rather than by arguing it away. Proceed to round 2 to check the fixes.
