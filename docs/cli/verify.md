---
layout: default
title: verify
parent: CLI Reference
nav_order: 17
permalink: /cli/verify/
---

# `calor verify` — static contract verification

Runs Z3 contract verification over one or more `.calr` files and reports every
obligation's proof status in the closed five-status vocabulary
(`proven | refuted | unknown | timeout | unsupported` — see
[Envelope Schema](/calor/cli/envelope-schema/)).

```bash
calor verify src/math.calr                    # human-readable text
calor verify src/math.calr --format json      # envelope v1.1 document
calor verify a.calr b.calr --timeout 10000    # per-contract solver budget (ms)
```

## JSON output (`--format json`)

The document is the envelope: `diagnostics[]` carries the compiler
diagnostics (refuted contracts as `Calor0712`/`Calor0711` warnings with
`declarationId` and the full `verification` payload), and `data` carries the
verify-specific payload:

```json
{
  "version": "1.1",
  "command": "verify",
  "diagnostics": [ /* envelope entries with verification payloads */ ],
  "summary": { "total": 1, "errors": 0, "warnings": 1, "info": 0 },
  "data": {
    "verifiedAt": "2026-07-23T21:14:00Z",
    "files": [
      {
        "fileName": "demo.calr",
        "summary": { "proven": 1, "refuted": 1, "unknown": 0, "timeout": 0,
                     "unsupported": 0, "disproven": 1, "unproven": 0, "skipped": 0 },
        "functions": [
          {
            "functionId": "f001",
            "functionName": "Dec",
            "contracts": [
              { "type": "postcondition", "index": 0,
                "status": "refuted", "legacyStatus": "Disproven",
                "counterexample": {
                  "rendered": "Counterexample: x=1, result=10",
                  "bindings": [ { "name": "x", "value": "1" },
                                { "name": "result", "value": "10" } ] } }
            ]
          }
        ]
      }
    ],
    "summary": { "proven": 1, "refuted": 1, "unknown": 0, "timeout": 0, "unsupported": 0 }
  }
}
```

- `status` is the five-status wire name from the choke point
  (`ProofOutcome`); `legacyStatus` is the pre-1.1 enum name, kept for one
  release.
- `counterexample` is present whenever the solver produced a concrete model;
  model-less refutations (e.g. an unsatisfiable precondition) carry `reason`.
- Per-file summaries report both vocabularies during the transition; the
  top-level `data.summary` is five-status only.

## Exit codes

| Code | Meaning |
|:-----|:--------|
| 0 | all contracts proven, or only inconclusive outcomes (`unknown` / `timeout` / `unsupported` — runtime checks are kept, so inconclusive is not failure) |
| 1 | any contract **refuted**, any compile error, or a missing input file |

A CI gate can therefore key on the exit code alone; a refuted contract is
never exit 0.

## Notes

- A `proven` **postcondition** is a genuine ∀-proof (UNSAT on negation) and
  elides its runtime check. A `proven` **precondition** currently means
  *satisfiable* (∃), which is weaker — see issue #755 before relying on
  precondition guard elision.
- Verification results are cached (`.calor/verification-cache`, format 1.2);
  cache hits keep the five-status granularity and counterexample bindings.
- `--timeout <ms>` sets the per-obligation solver budget; exceeding it yields
  `timeout` (distinct from `unknown`).
