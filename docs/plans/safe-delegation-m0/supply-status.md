# Safe delegation M0 source-task and recruitment status

**Recorded:** 2026-09-09. **Issue:** #1282. **Authority:** existing
repository records only under [authorization.md](authorization.md); no
recruitment contact, participant commitment, or private-data collection.

## Recruitment evidence

The
[commit-pinned Call W adjudication](https://github.com/juanmicrosoft/calor/blob/fc55e3cf065560f49133beb794cd63403bceabf7/docs/plans/call-w-adjudication.md)
was approved on
2026-08-04. The overall Call W decision was **PROCEED to v0.12** on the
substrate path. Separately, its PP-A2 demand input was **DEMAND UNPROVEN**:
no named adopter met the registered condition, and Call 3 remained closed.
The proceed decision is not an adopter result.

The reviewed repository does not contain a dated recruitment log after
2026-08-04. Therefore:

| Period | Evidence state | Permitted interpretation |
|---|---|---|
| Through Call W, 2026-08-04 | Evidenced | No secured adopter; DEMAND UNPROVEN |
| After Call W through 2026-09-09 | Unknown | No claim about whether outreach was attempted, declined, unanswered, or successful |

Unknown activity is not a failed search, zero demand, or evidence that nine
releases were offered to adopters. No person or organization is inferred from
missing records.

## Preceding-12-month source-request inventory

No adopter-owned, preceding-12-month inventory with provenance was found in
the reviewed repository. The repository's authored benchmarks, fixtures,
issues, and migration corpora are not interchangeable with real adopter change
requests and are not counted as historical source-task supply.

| Required inventory field | Current record |
|---|---|
| Adopter and workload boundary | Unavailable |
| Inventory extraction date and preceding-12-month window | Unavailable |
| Source-request IDs or privacy-safe hashes | Unavailable |
| Provenance and consent basis | Unavailable |
| Eligibility rule applied to each request | Unavailable |
| Exclusion reasons and counts | Unavailable |
| Independent source-cluster linkage | Unavailable |
| Distinct eligible historical clusters `H` | Unmeasured |
| Pilot reservation | Not registered |
| Final-pool reservation | Not registered |
| Future assigned-request horizon | Unconfirmed |

`H=50` is an illustration from the reference design, not a measured inventory.
The separate requirement for at least 50 future assigned requests concerns the
adoption workload horizon; it is not permission to reuse `H=50` as historical
supply.

## Pool construction rules if evidence is supplied

Any later authorized inventory must:

1. Give at least 75% of primary task weight in **each** pilot and final pool to
   distinct replayed historical requests.
2. Count independent source clusters, not authored variants, repetitions, or
   multiple tickets for the same underlying change, as primary tasks.
3. Keep pilot and final source clusters disjoint.
4. Subtract pilot reservations from both eligible task supply and reviewer
   capacity before reporting final capacity.
5. Freeze eligibility, exclusions, cluster linkage, within-task weights, and
   pool assignment before outcome-bearing execution.
6. Store only privacy-safe identifiers and approved artifacts; personal or
   confidential source material stays in authorized access-controlled storage.

Under the 75% rule, a measured `H=50` would permit at most 66 equal-weight
clusters across the two pools because `50 / 0.75 = 66.6`; this remains an
illustration. It does not prove that 50 eligible requests exist, that all can
be disclosed, or that 66 independent clusters can be formed.

## Smallest missing information requests

The smallest bounded requests that could begin replacing the current unknowns,
without starting recruitment or collecting source content, are:

| Request | Owner | Minimum fields | Consent/privacy need | Deadline |
|---|---|---|---|---|
| Post-Call-W recruitment status | Maintainer @juanmicrosoft | Dated contact or decision events; channel category; outcome category; whether any adopter condition was met | Do not publish names or messages without consent; aggregate or privacy-safe references are sufficient | No accepted inquiry deadline exists |
| Source-request inventory summary | A consenting adopter engineering lead, currently vacant | Adopter/workload boundary; future-request horizon; window and extraction date; privacy-safe source IDs/hashes; provenance; eligibility rule and per-request disposition; exclusion reasons/counts; historical source-cluster linkage/count; independently authored cluster count; pilot/final reservability | Organizational authorization and participant/data-owner consent before inspecting or exporting request content | No accepted inquiry deadline exists |

If either owner cannot supply the bounded summary, record that field as
unavailable. Do not replace it with repository issue counts, benchmark tasks,
or an inferred zero.

## Capacity disposition

Historical supply, future workload, pilot reservations, final reservations,
and reviewer capacity are all unverified. No powered sample size or collection
schedule can be reconciled from the current records. The result is
**INSUFFICIENT INFORMATION for supply and recruitment**, with no recruitment
commitment and no scientific inference about demand.
