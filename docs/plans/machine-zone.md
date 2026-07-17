# The Machine Zone: A Language Humans Are Not Expected to Read

**Status:** Draft v3 — round-1 panel (DA 25/PL 55/Op 35, dispositions §10); round-2 verification panel (DA 80/PL 80/Op 88, dispositions §11) round-3: independent human-sourced review found 9 findings the panels missed (§12) — status is therefore **internally consistent as far as review can establish**, with the §12 amendments binding. The panel score trajectory is self-assessed and should be read as such. **Explicitly a research bet**: built ahead of external adoption, at bus factor 1, downstream of a parked thesis — labeled as such per DA-7.
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-16 (v2 same day)
**Related:** [`agent-native-strategy.md`](agent-native-strategy.md) (v4.1 + §9; the `Justified`-keying design is its §5.1), [`real-scale-benchmark-design.md`](real-scale-benchmark-design.md), `epochs/hardness-check-001..002/VERDICT.md`
**Refines (not supersedes):** the strategy's deferred "canonical non-file program store" — files remain the container here; what changes is the rules governing their content.

---

## 0. The premise

> **Thesis: Calor function bodies are compiler output in the trust sense — validated through evidence, never through human reading. The human-facing artifact is the specification and the verifier's verdict. The body zone may be optimized exclusively for machines.**

The disciplining analogy: nobody reviews `-O2` output, because trust has structure — a human-authored **intent artifact**, a **deterministic, battle-tested translator**, and therefore inherited trust. Agents break the middle link (non-deterministic, unverified). The compiler world's answer to untrusted translators is **translation validation**: check every output against the input independently. Calor's contracts, effects, and behavioral checks *are* translation validation for agent codegen:

```
human authors INTENT → agent TRANSLATES (untrusted) → verifier VALIDATES → human reviews VERDICT
```

Two honesty notes the panel forced, stated up front:

- **The validator itself is not yet trustworthy enough for this role.** This repo found a soundness hole in its own obligation machinery (the FactCollector sibling-guard vacuity bug) *this year*, by humans reading things. Promoting the verifier to sole safety net requires a TCB-hardening program (§6.4) — this document does not assume a verifier that doesn't exist yet.
- **The verifier's marginal value over agents-plus-tests is unmeasured.** ~165 epoch runs produced zero escaped bugs in either arm; the machinery has never been observed catching what a human would have missed. §7's red-team gate exists to measure exactly this before the human layer is removed.

## 1. Evidence and hypotheses — separated honestly (v2 correction)

Round 1's most important finding (DA-1): v1 presented an *inference* as measurement. Corrected ledger:

**Measured:**
- Green-field Calor cost 2.7× iterations **on 2 of 5 wave-2 pairs** (csv parsing, fs-journal); the other three green-field pairs were at 1.0× parity. All modification pairs: 1.0× (`hardness-check-001..002/VERDICT.md`).
- The VERDICT's own attribution: the cost tracks **thin training prior** ("the fixture acts as in-context syntax reference"), i.e., *domain familiarity*, not emission mechanics.
- Agent-facing docs drifted four review rounds running until `self-check docs` made drift a CI failure.

**Hypothesis H1 (was v1's "finding"):** some fraction of the green-field cost is a **text-serialization tax** (indentation/closer/spacing errors) that structured edits would eliminate. *Contradicting evidence exists*: modification also emits whitespace-significant text and was at parity; the 2.7× was not uniform across green-field pairs. **H1 is decided by experiment E1 before any M-stage that depends on it (§9):** rerun the two 2.7× pairs under three prompts — baseline, baseline + in-context syntax exemplar, structured-edit interface — with transcripts archived. If the cheap exemplar alone collapses the ratio, H1 is dead, M2's rationale with it, and this document's scope shrinks to the spec-surface/evidence work (which stands on its own).

**Hypothesis H2:** spec-diff + evidence review detects real defects at a rate comparable to human body-review. Decided by the §7 red-team gate.

What stands regardless of H1/H2: docs rot without machine checking (measured), the artifact-as-manual effect (measured), and the trust-chain argument (§0) — which motivates the spec surface and packet independent of any serialization claim.

## 2. The core design: two zones, two constitutions

| | **Spec surface** (header) | **Machine zone** (bodies) |
|---|---|---|
| Reader | humans + agents | agents + verifier only |
| Optimized for | legibility, intent fidelity | verifiability, edit reliability, canonical identity |
| Contents | `§DOC` intent, contracts, effects, signatures, verified examples, **model/abstract state (§6.1)** | implementation |
| Review | header diff + evidence packet | never read by humans; verifier-validated |
| Stability | API-like | agents may rewrite wholesale if the spec surface re-validates |

**Zone assignments the panel forced (PL-1, PL-2):**
- **Contracts may not reference concrete representation.** v1's own example (`result.debits`) leaked the body into the spec. The spec surface needs **spec-only vocabulary** — model fields / abstraction functions relating spec state to representation (SPARK abstract state, Dafny ghost functions). This is a language feature, added to the roadmap as §6.1, and a *prerequisite* for the sufficiency metric: without it, body-reads hit a floor that would be misattributed to tooling.
- **Object/class invariants are spec surface** (they give public contracts meaning), expressed over model state so they don't leak representation.
- **Private helpers are machine zone**, including their contracts; the packet renders helper contracts *on demand* when a verdict or counterexample references one — an evidence-surface responsibility, not a human-reading exception.

## 3. The spec surface

Unchanged from v1 in essence — sufficiency pressure, checkability by construction, renderability (`calor spec`, header-only diffs) — with two v2 additions:

- **Quantified attestation (Operator-4):** every packet carries a residue line — fraction of spec claims machine-verified, behavioral-example coverage, count and staleness of `ASSUMED` items. The release signer attests to a *measured* lit zone and a *bounded, quantified* dark zone, not "the lit part plus unquantified risk."
- **Intent-prose rule (Operator-6):** every `§DOC` intent claim requires ≥1 verified example, and examples counted as evidence must be **independently sourced** (different agent/session than the implementation, or human-authored) — the same agent that misread the spec must not grade its own understanding.

## 4. The machine zone

### 4.1 Canonical form
One serialization per program: deterministic formatting, **dependency-topological declaration ordering** (not hash order — preserves the narrative locality behind the parity finding; PL-12), no stylistic freedom. `calor format` becomes the canonicalizer (any parseable dialect in, canonical out, on every write).

**Comments (PL-3):** body comments are allowed — they are agent-legibility content — but constitutionally regulated: canonical placement (attached to the following declaration/statement), **excluded from the content hash**, and staleness-checked by `self-check` (a comment referencing a symbol that no longer exists is drift). Free-floating prose that can't be attached canonically is rejected by the canonicalizer.

**Sugar and eject (PL-4):** canonical form does **not** desugar to a minimal core — it normalizes *within* the surface language. Rationale: eject-to-C# is the constitutional exit (§5), and desugared canonical code ejects into decompiler-grade C#. "One way to write everything" is scoped to formatting/ordering/normalization, not construct elimination.

**Semantic diff, defined (PL-10):** two programs are semantically identical iff their canonical forms are byte-identical *excluding* hash-excluded content (comments, graffiti). No alpha-normalization — local names are agent-legibility content and stay significant. A "semantic diff" is a diff of canonical forms.

### 4.2 Write path: accept dialects, store canonical — with the claim scoped honestly (DA-3)
Structured, declaration-addressed edits (transactional: parse → bind → affected-proof check → canonical serialize) are the *candidate* primary path; text remains an accepted input dialect, parsed once, never stored. v2 corrections:
- The "indentation bugs become unrepresentable" claim holds **only on the structured path**, which is unproven for LLMs and — today — largely unbuilt (the shipped tool is `calor_edit_preview`, preview-only; M2 is construction, not promotion). **E1's structured-edit arm and a text-vs-structured edit-success measurement gate M2.** Until then, `--heal` and the fixable-indent diagnostics are core infrastructure, not a compatibility layer.
- **Draft mode (PL-8):** transactional validation forbids broken intermediate states — the projectional-editing failure mode that *does* transfer to agents mid-refactor. The design includes an explicit unvalidated workspace (edits accumulate; validation runs at checkpoint, not per-write), also avoiding proof-latency-per-write (Dafny's chronic IDE complaint).

### 4.3 Compiler metadata — sidecar, not graffiti (DA-2, PL-9)
v1's inline "graffiti" contradicted its own semantic-diff and merge-conflict claims (regenerated digests churn stored text) and created a second driftable channel. v2: computed metadata (effect summaries, proof digests, callee-spec digests, provenance incl. **model/prompt lineage for forensics** — Operator-8) splits by nature (v2's single sidecar destroyed forensic lineage on regeneration and git merge drivers don't run server-side): **derived data** (effect summaries, digests) is an *uncommitted build cache*, recomputed everywhere, never merged; **forensic lineage** (model/prompt provenance) is an **append-only log** keyed by (declaration address, content hash), never regenerated, verified by `self-check metadata` in CI. Agents read it through the same tooling that serves the semantic index. The stored `.calr` stays clean; diffs stay semantic.

### 4.4 Identity, fully specified (PL-5, PL-6)
- **References are by name** (no Unison-style hash references: no transitive body-hash invalidation, no SCC hashing, no name-resolution layer; files stay self-contained).
- **A declaration's identity** = hash of its canonical form minus hash-excluded content.
- **Proof/cache/disposition keys** = `own-hash × callee-spec-digests × canonical-form-version`. Callee *spec* digests (not body hashes) bound invalidation: a callee body rewrite that preserves its contract invalidates nothing upstream — by constitutional design, specs are the stable layer. This applies the strategy §5.1 `Justified`-keying lesson (v1 mis-cited this to PR #691; corrected) as the global scheme.
- **The canonical-form spec is versioned**; the version salts all keys, and a canonicalizer change ships with a mechanical re-keying migration. (The §SEMVER-salt lesson, applied — not the "hash algorithm is forever" trap.)
- Mutual recursion: fine under name references; co-recursive groups share a proof-cache key composed of member spec digests.

## 5. Operations: incident-shaped, not review-shaped (the Operator round)

v1's escape hatches were "review-time tools wearing incident-response costumes." v2 requirements, all load-bearing:

1. **The mirror is a first-class, deployable emergency artifact** — buildable and shippable on its own, precisely because §5.3 admits the agent path is correlated-down during incidents. Patch-back: hotfix lands on the mirror and **deploys from it**; the fork may stay open with the machine zone **write-frozen** until the agent path returns; re-ingestion then closes the fork with **behavioral reconciliation** (test suite + differential run against the incident reproducer — hand-edited C# never byte-matches, so hash reconciliation was a comforting word, not a step). Incident **game days** are an M0 gate with a real baseline: the same injected-failure drill runs against the pre-migration repo first (grep-and-read MTTR), the bound is pre-registered relative to it with stated rationale, scenarios are externally drawn, transcripts archived. Red-team injections likewise: independent authorship and blinding (the reviewer must not know the injections), or the gate is ceremony at n=1.
2. **The residue emits telemetry — with defined failure semantics.** Today's emitter hard-fails (`throw ContractViolationException`), which would convert latent wrongness into outages on exactly the residue incidents select for. Production contract execution therefore requires a **per-contract policy (enforce / observe / sample)** defaulting to observe, a perf budget, and a defined runtime-checkable subset (model-state and quantified contracts are not executable) — this gets its own stage gate before any prod rollout. Contracts execute in production (canary at minimum): the unverified zone stops being dark and starts paging. Runtime stack traces map to `.calr` declarations and from there to spec-surface entries (extending #696's compile-time maps to runtime — currently missing). This is the cheapest floodlight available and it reuses machinery that already exists.
3. **Availability honesty:** "agents debug it" assumes the model API is up during your incident — a correlated-failure assumption (shared cloud infrastructure). The mirror + game-day drills are the answer *because* the agent path can be down exactly when needed.
4. **Scope exclusion:** regulated and safety-critical regimes (DO-178C, IEC 61508, SR 11-7) are **out of scope** until those regimes admit non-human review; the doc's ledger example is illustrative of mechanism, not a domain claim. SOC2-style control regimes are in scope (review-as-control is writable).
5. **Incident-time body-reads are counted separately** from review-time in the §7 metric (they measure the operations story, not spec sufficiency), and packets are retained as compliance records.

## 6. Roadmap consequences

1. **Spec-abstraction machinery** (model fields, abstraction functions, object invariants over model state) joins frames/`old()` on the verification roadmap — now a *prerequisite* for this design's sufficiency metric, not an eventual nicety.
2. **Verification depth**: each prover increment shrinks the unwatched residue of a human-free workflow — demand now comes from the workflow.
3. **The evidence packet** radicalizes from review aid to review interface (unchanged from v1).
4. **Verifier TCB hardening becomes a stage (PL-11):** vacuity checks as release gates, proof logging (verdicts carry replayable justifications), differential testing of the prover, and a small-trusted-core refactor direction. The FactCollector episode is the standing exhibit for why this precedes human-layer removal.

## 7. Falsifiable core — hardened (DA-5)

v1's body-reads metric was gameable (n=1 counter, definitional discretion, and zero achievable by complacency). v2 keeps it but pairs it with outcome measures:

- **Body-reads per change → 0**, with the definition pre-registered (opening any machine-zone body region in any tool while a change decision is pending), review-time vs incident-time counted separately.
- **Red-team detection rate (H2's gate):** known-subtle bugs injected into machine-zone bodies of the dogfood module; measure whether spec-diff + packet review catches them at a rate ≥ human body-review baseline. This is the falsifiable heart: it directly tests whether removing the human layer loses detection power.
- **Escaped defects on the dogfood module** (the outcome variable v1 omitted).
- **E1 attribution experiment** (§1) and text-vs-structured edit success (gates M2).
- **Eject fidelity**: round-trip behavioral equivalence, continuously tested from M0.

## 8. Honest limits

1. **The residue is the risk**, and incidents select for it (Operator-1): verified properties don't page. Mitigations: §5.2's telemetry floodlight, quantified attestation, verification-depth roadmap. The v1 claim "smaller and better-lit than the status quo" is downgraded to a hypothesis H2 tests — at *review* time; at *incident* time the status quo's grep-and-read is genuinely lost and §5.1's mirror is the compensating control.
2. **Accountability floor**: the signer attests to a quantified lit zone (§3); whether that satisfies counsel and auditors outside excluded domains is a social question dogfooding only partially tests.
3. **Structured-edit ergonomics unproven**; hedged by accept-dialects and gated by E1.
4. **Adoption and bus factor (DA-7):** this deepens bus-factor-1 (knowledge concentrates in one person's spec conventions) and is built ahead of any external user. It is a research bet on where agent-native development goes; the tested eject mirror is the exit ramp that keeps the bet reversible, which is why it moved to M0.
5. **Knowledge decay (Operator-7):** forbidding body-reads deletes the side channel where reviewers reading implementations notice *spec* errors. Partial compensation: the red-team gate measures what detection is lost; independent-sourcing for examples; and nothing forbids *agents* from flagging spec-implementation tension — an explicit packet section ("implementation observations") gives that channel a home.

## 9. Staging (revised gates)

| Stage | Content | Gate (falsifiable) |
|---|---|---|
| **E1a** (days) | Attribution experiment, two arms runnable NOW: baseline vs +syntax-exemplar on the 2.7× pairs; **pre-registered**: superseded; see §12.4 (protocol: n=30/arm/pair, baseline-relative <30% rule) and §13 (verdict) | H1 kept/killed on pre-registered numbers, not eyeballs |
| **E1b** (with M2 prototype) | Structured-edit arm of the attribution experiment | Gates M2 promotion (the structured path cannot be tested before it exists — v2's E1 was circular) |
| **M0** (small) | `calor spec` rendering + header diffs; body-read counting (pre-registered defn); **eject mirror in CI + fidelity tests + one game day**; red-team injection protocol | Spec-only review survives real changes; red-team detection ≥ baseline; game-day MTTR within pre-set bound |
| **M1** (large — DA-6's blast radius owned: ~1,267 `.calr`, 285 goldens, doc blocks, samples, **frozen benchmark suite conflict resolved before migration**: the frozen pairs are either exempted as a pinned dialect the canonicalizer accepts forever, or the suite is versioned with re-baselining declared — decided in a one-page addendum first) | Canonicalizer + repo migration + canonical-form-spec v1 + identity keys | Formatter-is-identity holds repo-wide; no proof-key churn beyond re-keying migration |
| **M2** (gated on E1 + edit-success measurement) | Structured edit surface (greenfield build — `calor_edit_preview` is preview-only today) + draft mode | Structured ≥ text on edit success; green-field ratio improves |
| **M3** | Sidecar metadata + `self-check metadata` + merge driver | Drift checks green; diffs stay semantic under callee changes |
| **M4** | Packet-as-interface (semantic index, blast radius, honest taxonomy, quantified attestation) | §7 metrics on the dogfood module |

## 10. Round-1 panel dispositions

| Finding | Source | v2 disposition |
|---|---|---|
| Serialization-tax attribution is inference contradicted by cited data (2/5 pairs; VERDICT says training prior; modification also emits text) | DA-1 | §1 rewritten: measured vs hypothesis split; E1 gates dependents; transcripts archived henceforth |
| §4.1 semantic-diffs vs §4.3 inline graffiti contradiction | DA-2, PL-9 | Metadata moved to content-hash-keyed sidecar with merge driver + CI check |
| Accept-dialects keeps text failures on the write path; "unrepresentable" only under unproven structured edits | DA-3 | Claim scoped; `--heal` stays core; edit-success measurement gates M2 |
| Verifier promoted on evidence that measured it catching nothing | DA-4 | §0 honesty note; H2 red-team gate at M0 |
| Body-reads metric gameable | DA-5 | §7: pre-registered defn + red-team detection rate + escaped defects |
| M1 blast radius undersold; frozen-suite conflict omitted | DA-6 | §9 M1 re-scoped "large" with the conflict resolved-first requirement |
| Adoption/bus-factor omission; research-bet labeling | DA-7 | Header label; §8.4; eject-as-exit-ramp at M0 |
| `calor_edit` doesn't exist; eject "tested" overstated; #691 mis-citation; "supersedes" oversell; typo | DA-8..10, factual | All corrected |
| No spec-abstraction machinery (contracts leak representation) | PL-1 | §2 zone rules + §6.1 roadmap prerequisite |
| Helpers/invariants have no zone | PL-2 | §2 assignments |
| Comments break canonical triad | PL-3 | §4.1 comment constitution |
| Desugaring vs eject collision | PL-4 | §4.1: normalize, don't desugar |
| Reference representation unspecified (Unison traps) | PL-5 | §4.4: name refs + spec-digest keying |
| Hash stability across canonicalizer versions | PL-6 | §4.4: versioned canonical-form spec salts keys |
| Broken-intermediate-states transfers to agents | PL-8 | §4.2 draft mode |
| Semantic diff undefined; ordering unspecified | PL-10, PL-12 | §4.1: defined; topological order |
| Verifier is unauditable single point of trust | PL-11 | §6.4 TCB stage |
| Incidents are residue-selected; eject is capability-not-artifact; agent availability correlated; MTTR walkthrough | Op-1..3 | §5 rewritten incident-shaped: CI mirror + patch-back + game days + runtime contract telemetry |
| Attestation unbounded; regulatory bimodality; example self-grading; knowledge decay; forensic provenance; retention | Op-4..9 | §3 quantified attestation + independent sourcing; §5.4 scope exclusion; §8.5; §4.3 lineage; §5.5 |

Panel credit recorded: accept-dialects-store-canonical judged "the correct hedge" (PL-7); staging discipline and kill-metric instinct judged the doc's best features (DA, PL) — both retained and hardened rather than replaced.

## 11. Round-2 dispositions (v3 delta)

| Finding | Source | v3 resolution |
|---|---|---|
| E1 circular (structured arm unbuilt) + threshold-free | DA-C1 | §9: split E1a/E1b; pre-registered n and collapse threshold |
| Sidecar mixed recomputable + forensic; merge driver server-side no-op | DA-M2 | §4.3: derived = uncommitted cache; lineage = append-only log, (address, hash)-keyed |
| M0 gates self-graded at n=1 | DA-M3, Op-M4 | §5.1: baseline-relative pre-registered bounds; blinded, independently-authored injections |
| Abstraction functions unauditable (spec-TCB) | PL-C1 | **Spec-support tier**: human-readable, representation-referencing, exempt from the no-rep rule, feasibility/consistency-checked by the prover — added to §2 zones and §6.1 |
| Spec digests not transitively closed (model fields/spec fns) | PL-M | §4.4 amended: digests close transitively over referenced spec definitions, spec-level SCCs digested as a unit — before M1 keys ship |
| Topological order not canonical; byte-diff granularity | PL-M | §4.1: SCC-collapse + name tie-break; semantic diff is declaration-set-based |
| Comment fate on deletion; trailing comments | PL-M/m | Comment dies with its anchor (explicit move to preserve); end-of-block comments attach to the enclosing block |
| Mirror not deployable; patch-back needs agent during correlated downtime; hash reconciliation not a mechanism | Op-C1, Op-M3 | §5.1 rewritten: deployable mirror, fork-open with frozen machine zone, behavioral reconciliation |
| Prod contract failure semantics undefined (emitter hard-fails today) | Op-C2 | §5.2: enforce/observe/sample policy, perf budget, runtime-checkable subset, own gate |
| Retention unspecified; fork-window ownership; body-read observability | Op-m5/6, DA-m4 | Packets retained immutably with a stated period (dogfood default: repo-committed, indefinite); `.calr` frozen during open fork; body-read counting acknowledged as honor-system in dogfood, tool-mediated at M4 |

**Loop closed per the unanimous round-2 ruling.** Trajectory: DA 25→80, PL 55→80, Op 35→88. Remaining risk is empirical and lives in E1a, M0's red-team/game-day gates, and the dogfood module — not in this document. Next artifact: run E1a.

## 12. Round-3 dispositions (independent human-sourced review; v4 delta — BINDING amendments)

1. **Wrong-spec failure mode untested (the dominant risk):** §7's red-team MUST include spec-defect injections — subtly-wrong contracts that verify cleanly — and measure spec-defect detection under spec-only review, separately from body-bug detection. H2 cannot pass on body-bugs alone.
2. **Gate-graph fixes to §9:** (a) M0's sufficiency metric is DESCRIPTIVE ONLY until §6.1 spec-abstraction machinery exists — body-read counts before then are logged but cannot pass/fail a gate (the floor would be misattributed, per the doc's own §2); (b) §6.4 TCB hardening and §5.2 runtime-policy get explicit stage rows, TCB before any human-layer removal claim, runtime-policy before any prod rollout; (c) **M1 is conditioned on E1a**: H1 dead → M1's structured-edit-motivated portions (canonicalizer-as-write-path) are descoped to the identity/keying subset that the evidence work needs.
3. **Bus-factor-1 gate honesty:** the red-team baseline arm (human body-review by the author at max familiarity, n=1) is acknowledged unmeasurable as specified; the gate is REDEFINED as absolute spec-only detection rate against blinded injections authored by an external party (recruited before M0; if none can be, the gate is explicitly marked non-discriminating and the design stays research-only). Escaped-defects on one module is acknowledged as near-zero-power per the project's own 165-run evidence.
4. **E1a re-registered:** threshold is BASELINE-RELATIVE (exemplar arm must reduce the concurrently-measured ratio by <30% for H1 to survive), not the historical 1.8× absolute (regression-to-the-mean trap); n = 30 runs/arm/pair matching the repo's own benchmark discipline.
5. **§2/§5.2 interaction (the disjoint-safety-nets bug):** the quantified attestation adds the **runtime-checkable fraction** as a first-class number; the §6.1 design must include executable model-state contracts (abstraction functions compiled into runtime checks) or the floodlight/vocabulary conflict stands unresolved and is reported in every packet.
6. **Ordering edit-stability:** canonical order = INSERTION-STABLE (existing declarations keep position; topological placement for new declarations only); full topological reflow rejected for whole-file diff/merge churn.
7. **Under-determination named honestly in §0/§8:** specs under-determine bodies by construction; the space specs don't constrain (performance, allocation, structure) is where agents optimize unsupervised. Non-functional regression detection (perf budgets in the packet) is added to the M4 scope; the -O2 analogy is explicitly scoped to functional correctness only.
8. **Self-certification acknowledged:** the review loop's scores are self-assessed; independent review is now the standard for any 'loop closed' claim on this document line.
9. **Blast radius corrected:** source-tree .calr count is **506** (the 1,267 figure counted bin/obj build outputs — the exact unchecked-number drift class self-check docs exists to catch; noted with due irony).
10. **Smaller:** machine-zone comment staleness downgraded from CI failure to compiler-fixed-on-write (no human ceremony in the unwatched zone); packet-rendered helper contracts COUNT as body-reads (the peephole is closed; they are machine-zone content); public-promotion of a helper requires spec-surface re-authorship + attestation (zone transitions are events).

## 13. E1a verdict (2026-07-17) — H1 KILLED; binding descope

180 runs, n=30/cell, pre-registered baseline-relative rule (§12.4), transcripts archived (`epochs/e1a-attribution/`, PR #706). Pooled: R_base 2.38 (effect reproduced concurrently), exemplar reduction of excess **55%** ≥ 30% kill line; W3-003 collapsed to **1.00 — full parity** from a 60-line syntax exemplar. Robustness: the exemplar itself contained a `[str]` error biasing *against* the kill (29/30 N1 runs copied it); H1 died anyway.

**Binding consequences (§12.2c applied):** M2 (structured editing) and M1's canonicalizer-as-write-path are **descoped**. The document's remaining scope is the spec-surface/evidence line (M0, packet, dogfood) plus identity/keying work only as the evidence layer requires it. The green-field tax is missing in-context syntax knowledge; the remedy is a correct, parse-checked, drift-guarded exemplar shipped in every agent-facing surface (init templates, CLAUDE.md, MCP prompt) — exemplars are load-bearing infrastructure and get `self-check` treatment.
