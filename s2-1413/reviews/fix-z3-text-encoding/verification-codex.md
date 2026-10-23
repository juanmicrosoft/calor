# S2 #1413 fix-z3-text-encoding — Codex verification-only pass

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: APPROVE

Static review of `d99256f8` against stacked base `fd5e8cd6`. No new BLOCKING, MAJOR, MINOR, or NIT findings identified within the requested verification scope. Dotnet was not run.

| Round-3 finding | Verification |
|---|---|
| **1 — Promotion-changing substitution** | Resolved by refusal. Collection uses original expressions at `Z3Verifier.cs:1043,1059`. Untranslatable divisor/range operands fail at `1140–1142,1208–1210`; the verifier returns `Unsupported` at `419–424`. The mixed-width witness is present at `S2Z3TextEncodingTests.cs:222`. |
| **2 — Eager initializers moved into branches** | Resolved. Initializers are collected once at their binding site (`Z3Verifier.cs:1051–1062`); reads no longer expand them. The exact eager-substring witness requires `Assumed` at `S2Z3TextEncodingTests.cs:241–253`. |
| **3 — Surrogate cache collisions** | Accurate external dependency, **still unresolved in this checkout** (`ContractHasher.cs:384`). #1494 supplies lossless hashing at its `ContractHasher.cs:409,416–417` and `VerificationCache.cs:312`. The current [disposition record:129](https://github.com/juanmicrosoft/calor/blob/milestone-0.24/s2-1413-dispositions/docs/plans/evidence/s2-1413/dispositions.json#L129) explicitly requires #1494 first; `dependsOn` includes both #1495 and #1494. |
| **4 — Boolean symbols mistaken for nullable values** | Resolved. `ContractTranslator.cs:1283–1288` restricts the detector to reference-sort terms. The reported literal-only conditional witness requires vacuity at `S2Z3TextEncodingTests.cs:257–271`. |
| **5 — Ineffective quantifier regression** | Resolved. The control translates at `S2Z3TextEncodingTests.cs:180`; both binder and body reference are renamed at `185–187`. The factory refuses `$` at `ContractTranslator.cs:1115–1116`. |
| **6 — Inaccurate assumption envelopes** | Honestly documented residual. Canonical entries remain stale at `Z3Verifier.cs:28,37,70`; explanatory reasons are corrected at `565,570,575` and `ObligationSolver.cs:242–243`. The disposition record explicitly acknowledges this. The document hash pin is enforced at `DifferentialGate.cs:705–714`. |

The reviewed fixes introduce no identified false proof, false `Discharged`, or fabricated counterexample. Collector failures stop verification before a solver verdict; valid eager initialization remains modeled. String proofs still demote at `Z3Verifier.cs:543–585` and `ObligationSolver.cs:225–255`. The revised CHANGELOG claim is appropriately scoped at `CHANGELOG.md:29–33`.

Counts reconcile: **18 facts + 6 theory rows = 24 new cases**, matching **417 → 441** at `eng/test-manifest.json:106`. Verification skips remain 0. Compiler total remains **12605**, with no added cases there; #1495’s cases are already in the base. `python3 -B scripts/check_test_quality.py` passed. The author’s dotnet pass claims were not independently verified.
