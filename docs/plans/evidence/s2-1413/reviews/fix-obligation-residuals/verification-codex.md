# S2 #1413 fix-obligation-residuals — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static verification at `9b54c9c3` against stacked base `cdb6c9df` (#1496). No `dotnet` execution or new adversarial witnesses.

- **MAJOR — Finding 7’s documentation fix does not satisfy exception A.** The revised CHANGELOG honestly describes a restored Discharged outcome (`CHANGELOG.md:69–71`). However, exception A requires: “The PR makes no outcome stronger: no obligation, contract, or claim moves to Proven or Discharged” (`docs/plans/evidence/evidence-contract-1407/contract.json:335`).

  On the stacked base, the later `Math.Abs` call prevents collecting the initial guard (`cdb6c9df:src/Calor.Compiler/Verification/Obligations/FactCollector.cs:270–273`), yielding Unsupported for the existing discharge control. This PR restores that fact (`src/Calor.Compiler/Verification/Obligations/FactCollector.cs:381–386`), and the control explicitly expects Discharged (`tests/Calor.Compiler.Tests/S2ObligationResidualTests.cs:445`). The discharge is legitimate, but it strengthens an outcome across this PR. Being unchanged between rounds 2 and 3 does not establish compliance against #1496.

- **MINOR — Finding 6’s requested validation remains incomplete.** The fixtures now invoke the conversion and valid paired static comparison operators (`tests/Calor.Compiler.Tests/S2ObligationResidualTests.cs:246–256`, `267–283`). Their assertions still discard generated C# and diagnostic codes. The helper checks only that obligation results exist (`:28–31`); neither fixture checks emitted-code validity or the requested production diagnostics.

The remaining code dispositions are confirmed:

| Round-3 finding | Verification |
|---|---|
| 1 — Entry preconditions | Throwing-precondition and entry-guard checks now apply outside `!isEntry`: `ObligationSolver.cs:321–326`. |
| 2 — Constructor initializer | Any explicit initializer sets `EntryMayThrow`: `ObligationSolver.cs:472–476`. |
| 3 — Default-null locals | Length/string exemptions now require modeled operands; ordinary locals fail that test: `FactCollector.cs:300–302`, `322–327`. |
| 4 — `§LEN` property | `ArrayLengthNode` recognizes declared `Length` properties: `FactCollector.cs:363–369`. |
| 5 — Raw members | Class/interface interop blocks activate conservative throwing and member-read classification: `FactCollector.cs:347–369`; `ObligationSolver.cs:322`. |
| 7 — Documentation/control | The wording is corrected and the discharge control is present, subject to the gate finding above. |

Compiler paths in that table are relative to `src/Calor.Compiler/Verification/Obligations/`.

I found no new false proof, false Discharged, or fabricated counterexample introduced by the round-3 changes. They broaden throwing/property classification and route inexact SAT results to Unsupported (`ObligationSolver.cs:327–336`); they add no proof assumptions.

Counts are consistent: **17 → 23 facts**, six additions in round 3; **12,682 → 12,705** against #1496, with expected skips unchanged at **3** (`eng/test-manifest.json:8–9`). The author’s full-suite pass claims cannot be independently confirmed without execution.
