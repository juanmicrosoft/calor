# 0.25 pre-freeze PR (#1576): Codex round 2

- **Invocation 2 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `2db8e17ec` (the round-1 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 4 MAJOR, 2 MINOR. No BLOCKING.

Codex confirmed: 14,493 + 46 + 4 + 37 = 14,580; the pinned 1.3.0 digest and the D1 seal reproduce;
earlier amendments and the 0.24 map entry are unchanged; the G2 controls, protocol validation and the
generator self-test pass.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | READY's cases and digests came from T1 without checking T1 against the scope (T003); the approval seal does not cover them. | Fixed: the gate recomputes `RequirementDigest` from `scope.json` (fixture, note, cells, observations as `UnsafeRelaxedJsonEscaping` JSON, families, website example); all 65 recompute equal to the T1 values. Each T1 family must list exactly the scope's cases and digests, and a READY record is checked against the scope's cases. Control: a case dropped from T1. |
| 2 | MAJOR | No S007 staleness check. | Fixed: `touched_versions` ports the amendment-based family staleness; a READY record assessed before an amendment that added, removed, changed or moved one of its family's cases fails. Control: F1 at 1.0.0 (stale since 1.1.0). |
| 3 | MAJOR | Duplicate case rows overwrite earlier ones. | Fixed: a duplicate case id fails, and the first row is the one judged (as in C#). Control: a failed row followed by a passing duplicate. |
| 4 | MAJOR | W1 validation was shallow. | Fixed: each W1 family's status mirrors scope readiness and is never READY, with ready needs, and every transcript, page (with its heading anchor, the site's slug rule) and shared page exists. Controls: W1 without schema, and W1 families reduced to id and issue. |
| 5 | MINOR | Python coercions (booleans as integers, blank strings, unparsed dates). | Fixed: non-boolean integers, non-blank strings, and a calendar-date check. Controls: a boolean test total, a blank reviewer, and a decision date `2026-99-99garbage`. |
| 6 | MINOR | The `ready/run.py` control exercised modification, not addition. | Fixed: an added `ready/run.py` is not exempt in either phase; an added `ready/run.json` is. |

`ReleaseGateScopePacketTests`: 37 → 44 cases; `ReleaseCandidateInvalidationTests` stays at 50; test
manifest 14,580 → 14,587.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. MAJOR — scripts/verify_release_adjudication.py:651: READY's denominator and requirement digests come from T1 without enforcing T003 against scope. T1's approval seal excludes these fields. Read-only probes deleting F1's registered cases, then supplying READY with cases: [], produced zero packet or READY violations. Fix: validate T1's cases against scope; derive READY requirements from scope and recompute their digests. Add isolated controls.

2. MAJOR — scripts/verify_release_adjudication.py:645: Registered scope versions are accepted without S007's staleness check. Changing an otherwise valid F1 READY record to scopeVersion: "1.0.0" passes, although amendment 1.1.0 added F1 cases. C# explicitly rejects this. Fix: port the amendment-based family staleness calculation and test stale versus still-valid historical versions.

3. MAJOR — scripts/verify_release_adjudication.py:652: Duplicate case IDs silently overwrite earlier results. A failed case followed by its passing duplicate produces no violation; C# selects the first row and rejects it. Fix: reject duplicate READY case IDs before constructing the dictionary; add an independent negative control.

4. MAJOR — scripts/verify_release_adjudication.py:579: W1 validation remains shallow. Keeping only schema/version/independence and each family's ID/issue passes, with statuses, readyNeeds, pages, transcripts and shared pages removed. The new hollow-W1 control removes schema too, so it misses this bypass. Fix: enforce W1's existing scope/status and retained-reference rules; test hollow records with valid schema and family coverage.

5. MINOR — scripts/verify_release_adjudication.py:661 and :614: Python coercions weaken S007/S014. READY accepts total: true, failed: false and whitespace-only project/reviewer/approver names. Preconditions accept whitespace decision text/author and date: "2026-99-99garbage". Probes returned zero violations. Fix: require non-boolean integers, nonblank strings and a parsed calendar date, with parity controls.

6. MINOR — tests/Calor.Compiler.Tests/EvidenceContract/ReleaseCandidateInvalidationTests.cs:26: The ready/run.py negative uses Modify, so it exercises the modification prohibition rather than excluding newly added code. Removing the data-extension restriction would leave this control passing. Fix: test an added regular ready/run.py file.

Confirmed: 14,493 + 46 prep + 4 classifier + 37 scope-gate = 14,580. The pinned 1.3.0 digest and D1 seal reproduce; earlier amendments and the 0.24 map entry are unchanged. New G2 controls, protocol validation and generator self-test passed. Full .NET tests were not run under the read-only sandbox.
```
