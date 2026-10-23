# #1419 R1 registration — adversarial review round 2 (Codex)

**Reviewer:** Codex CLI 0.159.2, same invocation as round 1, with the instruction to verify each
round-1 fix in code and data. The first round-2 attempt hit the account usage limit; the review was
re-run after the limit reset (no paid credits were bought).
**Reviewed commit:** `9625f1c7`. **Verdict:** 10 BLOCKING, 6 MAJOR, 0 MINOR.

No verifier outcome for B1 or N1 was produced or read while addressing these findings.

| # | Severity | Finding (abridged) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Cache key hashes integer literals by value only; width change not probed | **Fixed.** Row `CACHE-LITERAL-WIDTH`: prime and final differ only in `LONG:1` vs `INT:1` under `overflow=unchecked`; warm must equal cold |
| 2 | BLOCKING | No probe of a `§Q`-constrained parameter reassigned before a proof | **Fixed.** `T-OBL-KILL-PRE` (entry `§Q` on x, `§ASSIGN x`, `§PROOF` on x; O1 evaluates the proof-site value) |
| 3 | BLOCKING | Implication prover bypasses string/reference/checked-arithmetic demotions; no probes | **Fixed.** Row `IMPL-ASSUMPTION-FORMS` (string length/null and checked-overflow implementer preconditions under a satisfiable interface precondition) |
| 4 | BLOCKING | `--weakening-check` claimed but no channel exercises it | **Fixed by exclusion.** `verifyCmd` removed from `IMPL-DIVISION-TOTALIZED`; new not-investigated row `XCL-WEAKENING-CHECK` with its reason (CLI surface over the prover core that the IMPL rows cover) |
| 5 | BLOCKING | Implication unknown warnings and heuristic fallback unmapped | **Fixed.** `Calor0816` -> `TimeoutOrUnavailable` (retry, then INCOMPLETE); no diagnostic -> `Unsupported` |
| 6 | BLOCKING | Exists adjudication relied on a model B1/N1 never record | **Fixed.** Table keyed on the O1 witness: `Proven` + witness -> validated; exhaustive no-witness -> false proof; sampled no-witness -> unconfirmed (blocks clean); nothing instrumented |
| 7 | BLOCKING | Result classes not exhaustive (correct ProvenVacuous, sampled vacuous Proven, genuine refutation on assumed rows) | **Fixed.** Frozen `classificationTable`: exactly one class per (row classification, token, O1 verdict); unmatched = harness error |
| 8 | BLOCKING | H1 signed cases could not turn a wrong fold into a false proof | **Fixed.** `T-SIMP-H1-PINNED` pins x to the value a wrong fold would produce (e.g. `§Q (== x INT:-1)`, `§S (== x (~ UINT:0))`) |
| 9 | BLOCKING | Inherited-guard template claimed a postcondition outcome that cannot exist | **Fixed.** `claimSite = guard-emission`; no token expected; the inherited guard must be present in both emissions |
| 10 | BLOCKING | After-if probes could not discriminate leaked facts | **Fixed.** Deterministic `T-OBL-AFTER-IF` and `T-OBL-SIBLING`: the goal equals the out-of-scope guard, false on reached inputs |
| 11 | MAJOR | Loop-bound facts mention the undeclared loop variable | **Fixed.** Loop-variable claims moved to refusal row `OBL-LOOP-VARIABLE`; `OBL-LOOP-FACTS` re-described as parameter claims judged against `§Q` alone |
| 12 | MAJOR | H1 forms with no C# typing sat in a modeled row | **Fixed.** Refusal row `SIMP-H1-INVALID-TYPING` (`(- ULONG:1)`, `(- UINT:1)` against u64); `frontEndRefusalCodes ["*"]` = any non-lexer/parser rejection, because R1 does not compile SIMP rows |
| 13 | MAJOR | O1 rendered inputs after the body mutated them (alias cases) | **Fixed.** `IndependentOracle` renders entry values before `Body` runs |
| 14 | MAJOR | Cache variant A would be classified as a finding | **Fixed.** Variant A is a tamper-reachability control, never a finding; only variant B is adjudicated |
| 15 | MAJOR | Spurious-refutation rule treated any satisfying input as refuting a counterexample | **Fixed.** Only an invalid replayed model, or no model with O1 holds-exhaustive; a model-less sampled refutation is no-claim |
| 16 | MAJOR | `i16` bound variable never rendered; non-numeric bound sorts unregistered | **Fixed.** Bound-type hole cycles all four types; per-type coverage entries; non-numeric bound sorts excluded as `XCL-QNT-NONNUMERIC-BOUND` |

Net effect: 99 rows (was 93), 710 cases, 1,500 executions (unchanged allocation).
