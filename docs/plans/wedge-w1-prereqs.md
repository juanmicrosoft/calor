# W1 Kickoff — Adoption & Measurement Prerequisites (v0.11 WS-W1)

**Status:** Kickoff record — D-W1.1 triage complete; PP-A1 release-policy list frozen (§3); W1 box corrected (§5). Adversarially reviewed pre-merge (verdict: needs-fixes → applied; 25+ anchors independently re-verified clean, zero fabrications; findings C1 exit-criteria contradiction, M1 D8/D6/D7 under-crediting, M2 "differentially-clean" overclaim, M3 LSP-formatting containment gap, m1–m4 — all dispositions marked inline). Maintainer sign-off happens by merging this PR (bus factor 1, self-asserted per program convention).
**Author:** Juan Rivera (with Claude Code)
**Created:** 2026-07-31
**Parent:** [`wedge-plan-v0.11.md`](wedge-plan-v0.11.md) §2 WS-W1 (merged `18687840`). Governs deliverables D-W1.1–D-W1.5 + the W1 housekeeping item.
**Triage basis:** three parallel research passes over the #793 epic at main `18687840` (2026-07-31): the verification cluster vs v0.10's shipped work, the SDK/CI/telemetry cluster, and the conversion/migration cluster — every claim below carries a grep-verified anchor from those passes.

---

## 1. D-W1.1 — Epic triage: dispositions

**Headline finding, first:** the triage surfaced **two live soundness defects** that outrank the packaging work and enter W1 as its first slice (§4):

- **T1 — `string.Replace` is a live false-`Proven`-that-elides vector.** The contract translator models Z3 `MkReplace` = **first occurrence** (`Verification/Z3/ContractTranslator.cs:1129–1143`) while the emitter generates .NET `.Replace()` = **all occurrences** (`CodeGen/CSharpEmitter.cs:4453`). The form is on the ModeledForms whitelist (`:1696`) and absent from the D1–D8 divergence table (the first-occurrence modeling *is* parenthetically disclosed at `verification-modeled-forms.md:55` — documented-but-untabled, review m2) — a whitelisted shape that can mint a false `Proven` and delete a runtime guard, contradicting PP-G1's own release-blocker wording. Same class, also live on the whitelist: narrow-type promotion (D1 — `i8 100+100` wraps at 8 bits at runtime, verified at 32 in Z3), mixed signed/unsigned equality, **and D8 — contract-expression `/` and `%` are still totalized with no divisor side-conditions** (`ContractTranslator.cs:373–374` `MkBVSDiv`; the doc's own row: "a proof may rely on `x/0 = -1` while the emitted runtime check would throw" — v0.10's division side-conditions covered *body* division only; review M1). D6/D7 (array-element/field width defaults) are the same class at lower confidence and are adjudicated in the Slice-1 PR against the same bar.
- **T2 — postcondition return-lowering (#764), now closed.** One structural single-exit mechanism serves every callable owner. Structured returns assign the expression once and jump to a shared exit after `finally`/`using` cleanup; exceptional exits bypass checks. The exact `result` reference is bound during AST emission rather than rewritten in generated text. Iterator postconditions remain explicitly rejected until deferred-completion semantics are defined.

### 1.1 Verification cluster (vs v0.10's shipped work)

| Issue | Verdict | v0.10 fraction | W1 action |
|---|---|---|---|
| **#779** guards until soundness gate | **PARTIAL — reconcile (§2)** | ~60–70 %, via the *gate* branch: precondition elision deleted outright (D-G1.2, pinned `IntegrationTests.cs:47,71`); postcondition elision requires non-vacuous ∀-proof (`CSharpEmitter.cs:1889–1894`); seven statuses, `Assumed` never Proven-equivalent (`ProofOutcome.cs:354–360`); cache ledger 1.3→1.7. **Not shipped:** differential suite, elision capability flag — and the issue's DoD ("elision disabled until children complete") is violated by live elision over a whitelist carrying T1 | Adjudicate per §2 (decision D1); comment + retitle to the residual |
| **#780** Z3 ↔ C# semantics | **PARTIAL** | ~40–50 %: bvsrem, wide-literal refusal, division side-conditions (**body divisions only**), positive whitelist, unsigned refusal in the bug-pattern translator (cache 1.4–1.7). **Live on the whitelisted proof surface:** T1 trio (Replace, D1 narrow ints, mixed-signedness equality) **+ D8 contract-expression division** (headline/Slice 1) | **W1-blocking subset (S–M):** un-whitelist the divergent forms → `unsupported`, add the Replace row to the divergence table, cache bump per ledger rule. Full D1/D2 fixes + differential suite + shared semantics model: defer (L) |
| **#778** cache keys | **PARTIAL** | ~70 %: string-op/field-access content hashing (`ContractHasher.cs:268–296`), unsupported-never-cached (`VerificationCache.cs:98–101`), format ledger + Z3-version check + hash-integrity recheck | Not W1-blocking. Batched S follow-up (serializer-coverage architecture test, compiler-version in `IsValidFor`, verbose hit/miss); comment + retitle |
| **#782** obligation guards | **OPEN-UNTOUCHED** (~0 %; `ObligationPolicy` still has no emitter consumer; obligation path explicitly outside the v0.10 whitelist gate) | Not W1-blocking (refinements are off the wedge surface). **S honesty item rides D-W3.4:** playbook + a diagnostic must state refinement types are currently unenforced at runtime | Defer (L); honesty item scheduled at W3 |
| **#781** contract inheritance + module preservation | **OPEN-UNTOUCHED** (~0 %; name-only keying `ContractInheritanceChecker.cs:59,78,124`; positional `new ModuleNode(` in `ContractSimplificationPass.cs:76`) | Not W1-blocking, with one check done at kickoff: `ContractSimplificationPass` reachability from shipped commands decides whether the module-preservation half (M) rides D-W1.3 — **resolved in the Slice-1 PR** (if reachable → preservation fix joins the batch; if not → defer whole) | Comment recorded |
| **#764** return lowering | **CLOSED** | Shared structural lowering covers functions, methods, enum extensions, operator methods/overloads, nested control flow, async value returns, and lambda-context isolation. Iterator postconditions fail explicitly | Retain Roslyn/runtime regressions and the generated-C# validation gate |

### 1.2 SDK / CI / telemetry cluster

Packaging facts established: the `calor` CLI **is** on nuget.org at 0.10.0 (`publish-nuget.yml`, packs only `Calor.Compiler.csproj`); **`Calor.Sdk` is not published anywhere**; the GitHub v0.10.0 release has zero assets; and `publish-nuget.yml` has **no test-gate dependency** — a release event publishes unconditionally.

| Issue | Verdict | PP-A1-required subset | Size |
|---|---|---|---|
| **#787** functional Sdk package | **OPEN — the W1 critical path (D-W1.2)** | Package topology decision; pack tasks + compiler closure + `Calor.Runtime` + Z3 natives for CI-tested RIDs (the csproj still carries every audited defect: `SuppressDependenciesWhenPacking` + `IncludeBuildOutput=false` at `Calor.Sdk.csproj:19–20`, the late-glob `IncludeTaskAssemblies` target at `:42–48`); a fresh consumer fixture restoring from a **local nupkg feed** = the M-A1 CI check (the existing `samples/SdkSample` is a source-tree consumer, not a package test); package-content inspection; atomic same-version publish with the CLI | **L** |
| **#790** truthful release gates | **OPEN (D-W1.2)** | (a) unmask id-validation (`test.yml:364,371` — `\|\| true`, `\|\| echo`, and it scans a nonexistent `examples/` while `samples/` goes unchecked); (b) `publish-nuget.yml` gated on tests + M-A1; (c) test-manifest honesty (`Calor.Performance.Tests` is in **no workflow**; Synthetic/E2E/DebugRT declared or wired). Round-trip blocking is counted under #776, not here. Deferred: coverage ratchets, mutation testing, vacuous-assertion sweep, LSP E2E revival | **M** |
| **#788** incremental determinism | **PARTIAL** (v0.10 paid some: `Verify` in the options token; compiler-hash claim partially stale — `ComputeCompilerHash` covers the tasks-path closure, `BuildStateCache.cs:139–143`) | Complete the options hash (`ExperimentalFlags`, `EnableILAnalysis` still omitted — `CompileCalor.cs:123–124`); fail-closed enforcement init/exec; cheap output-content check | **S–M** |
| **#789** hermetic natives | **OPEN** | The acute hazard: `publish-nuget.yml:44–53` fetches Z3 with bare `curl -L` — no `-f`, no checksum — a 404 HTML page would ship inside the nupkg as `libz3.so`. Subset: committed SHA-256 manifest + `-f` + verify on every release/publish download and in `download-z3.sh`; corrupt/missing native fails **before** pack. Deferred: hermetic offline builds, SBOM, lock files | **S** |
| **#792** telemetry opt-in | **OPEN (D-W1.5)** — worst adopter-facing trust item: **default-on** (`CalorTelemetry.cs:121–125`), sends **raw diagnostic messages** (`:221–241`) and **full exceptions** (`:246–268`) to an **embedded App Insights key** (`:17–18`), fires on every CLI invocation, zero documentation | Opt-in default (default invocation sends nothing) + strip raw payloads (code/category/count only) + payload doc + default-silent regression test; rotate-and-externalize the key as follow-up | **S** |

### 1.3 Conversion / migration cluster (feeds D-W1.3, D-W1.4, and W4's fidelity gate)

Neither `Migration/` nor the round-trip harness has any commit since #750 — every audit claim reproduced.

| Issue | Verdict | W1-relevant subset (gate = D-W4.3 substrate; eject = D-W3.4) | Size |
|---|---|---|---|
| **#776** harness coverage-aware/failure-safe | **OPEN — this IS D-W1.4** | Near-whole issue: per-project coverage % with reverted files **in the denominator**; separated verdict dimensions; populate `Gaps`/`InteropBlocks` (declared but **never assigned** — false zeros today, `RoundTripReport.cs:35–36`); aggregate **all** TRX (today: newest-file-only, `TrxParser.cs:30–35`); inventory-shrink = fail; threshold config. CI stays `continue-on-error` until a baseline exists, then flips blocking (#790 item) | **M–L** |
| **#770** lossless-conversion contract | **OPEN** | Gate: per-region conversion classification (structured counts + **locations**), fallback = countable loss never silent success (today: `§ERR "TODO…"` expressions in all modes, `CalorEmitter.cs:3806–3824`; CLI writes output **before** validating and prints "✓ Conversion successful" unconditionally, `ConvertCommand.cs:316–350`), expression-level interop escalation. Eject: the contract definition + degradation spec the D-W3.4 suite tests against | **M** / **S–M** |
| **#774** silent substitutions | **OPEN** | Gate-critical (Risk 3 — attribution corruption in either direction): char→string literal (`RoslynSyntaxVisitor.cs:6004`), unknown-binary-op→`Add` (`:6108`), pattern defaults→`Equal`/`And`/wildcard (`:6194,6204,5543`), compound-assign map→null beyond `+ - * / %` (`:4123–4129`). Corpus exposure real: ~96 char-literal lines in Serilog, ~79 in FluentValidation; 16 exotic compound-assigns across both. Subset: exhaustive switches, no substituting fallbacks, escalate to member interop | **M** |
| **#773** FeatureSupport as executable contract | **OPEN** | The capability classifier is the mechanical basis of W4's per-task eligibility predicate; conformance fixtures for corpus-hit features only. Registry overclaims confirmed: `record` = Full while emitting a broken class; `preprocessor-directive` = Full while directives are deleted pre-parse | **M** |
| **#771** round-trip tests semantically valid | **OPEN** | Shared Roslyn compilation helper + split syntax/compilation statuses + honest `FullyConverted` (today: `RoslynSuccess` computed, never asserted, `RoundTripTests.cs:80–104`; scorecard Stage 3 is parse-only, `ConversionScorecardRunner.cs:182–190`; aggregate `>= 86` floor hides per-fixture regressions). The eject suite builds on exactly this infra | **S–M** |
| **#761** compile ⇒ Roslyn-valid | **OPEN** | W1 subset: the shared Roslyn helper only (with #771). The default-on `EnableTypeChecking` flip and `#line`-mapped surfacing are adopter-facing — placed on the §2 list as a W2/W3-window item, not gate-blocking (the harness's real `dotnet build` validates at the gate) | **S** |

**Phase-4 per-item go/no-go (the D-W1.3 scope gate), decided on corpus evidence** (fresh clones of MediatR / Serilog / FluentValidation):

| Issue | Corpus evidence | Decision |
|---|---|---|
| **#772** conditional compilation | Serilog: **160 `#if` directives across 27/113 files**; FluentValidation: 9 + **83 `#nullable`/`#pragma`** (all currently deleted pre-parse; first-branch-kept unevaluated) | **GO — containment only**: registry downgrade + declared-loss reporting + directive-bearing files counted interop/excluded-from-native-coverage. Without this, Serilog cannot honestly pass any fidelity bar. Full branchful `§PP` preservation: NO-GO |
| **#775** records | 3 files total corpus-wide, but output is **actively broken** (positional record → class with getter-only properties and **no constructor**, `RoslynSyntaxVisitor.cs:1156–1194`) | **GO — containment only** (registry downgrade + preserve-as-interop, S). Full native records: NO-GO |
| **#777** local functions | Confirmed in MediatR + Serilog (one inside an `#if` block); current hoisting drops captures/type-params → build-breakers absorbed by revert-recovery | **GO — containment only** (member-level interop, S–M). Full closure conversion: NO-GO |
| **#769** namespace topology | **Zero** multi-namespace files in all three projects | **NO-GO** for v0.11; #770 whole-file interop covers the residue |
| **#751** standalone block scope | No corpus instances; fails loud (`Calor0258`) post-#731 | **NO-GO**; becomes an honest coverage exclusion under fixed #776 |
| **#774** silent substitutions | ~96 char-literal lines (Serilog) + ~79 (FluentValidation); 16 exotic compound-assigns | **GO — gate subset** (§1.3 cluster table row; Slice 3, size M) — listed here so the D-W1.3 per-item format is complete (review m1) |

### 1.4 Release-policy containment gaps (new finding — T3)

The #793 release policy's "must remain disabled" was **never enforced in code** for **three** surfaces: `calor format --write` is fully operational (`FormatCommand.cs:31–33,89+`, with the id-corrupting regexes live at `CalorFormatter.cs:56,76` and comments still discarded by the lexer), the LSP `RenameHandler` is still registered (`Calor.LanguageServer/Program.cs:35`), and the LSP `FormattingHandler` is also registered (`Program.cs:30`) and applies the same `CalorFormatter` machinery as whole-document TextEdits (review M3). **W1 action (S, Slice 2):** enforce containment — gate `format --write`, rename, and LSP formatting behind an explicit `--experimental` acknowledgment (or disable), so the policy is code, not prose.

---

## 2. Decision D1 — the #779 reconciliation (elision stance, recorded)

**Decision:** postcondition guard elision **remains enabled, but only over the narrowed whitelist** that Slice 1 produces (T1 forms + D8 removed → `unsupported` or routed through the D-G2.5 assumed producer; D6/D7 adjudicated in the slice PR); precondition elision stays deleted (strictly safer than #779's containment ask). Rationale: v0.10 built the *permanent gate* branch of #779 (typed outcomes, eligibility rules, never-elide-on-assumed/vacuous, M-G2 CI pins) rather than the *containment* branch, and the sound response to the DoD conflict is to make the whitelist match the gate — remove the divergent forms — not to flip elision off and forfeit the shipped, pinned soundness work. **#779 stays open**, retitled to its residual: the solver-vs-runtime differential suite, which #779's gate item 4 requires for **every form that remains on the whitelist**, not only re-expansion candidates. Stated plainly *(review M2)*: the narrowed whitelist is **known-divergence-free, which is weaker than differentially clean** — no differential suite exists yet, so keeping elision live over the residual whitelist is a **disclosed risk acceptance**, bounded by the M-G2 CI pins and the divergence-table audit, until the differential suite closes #779. The #793 release-policy bullet is amended by this record accordingly: "proof-driven runtime guard elision" is permitted over the known-divergence-free narrowed whitelist as a recorded risk acceptance, disabled elsewhere by construction; the amendment is posted to #793 per its Reporting section (Slice 6). This decision is maintainer-approved by merging this PR; at bus factor 1 that approval is self-asserted, as always.

---

## 3. The frozen PP-A1 release-policy list (resolves the plan's "at minimum")

PP-A1 (wedge plan §5) requires exactly the following before v0.11.0 offers the Wedge — frozen now, before any results exist:

1. **#787 subset** (§1.2) — functional published `Calor.Sdk`; M-A1 green in CI.
2. **#790 subset** (§1.2) — unmasked gates; publish workflow test-gated; test-manifest honesty.
3. **#789 subset** — checksummed natives on every publish path.
4. **#788 subset** — complete options hash + fail-closed enforcement.
5. **#792 subset** — telemetry opt-in default, stripped payloads, documented.
6. **Slice-1 soundness batch** — T1 trio un-whitelisted + D8 closed (un-whitelisted or D-G2.5-routed, §2) (#780 subset), D6/D7 adjudicated against the same bar in the slice PR, + T2 stopgap (#764 bar) + D1 recorded: **no known false-`Proven`-elides vector** (the known set = the divergence table + T1; the bar is known-divergence-free, per §2's disclosed risk acceptance) and no silently-skipped postcondition check ships.
7. **T3 containment** — `format --write`, LSP rename, **and LSP formatting** gated/disabled per the release policy (three surfaces, §1.4).
8. **#770 eject-contract subset** — the documented degradation spec the D-W3.4 eject suite tests (the suite itself is W3 scope; the *contract* is W1's).
9. **#761 flip stance** — `EnableTypeChecking` default-on lands in the W2/W3 window with a CHANGELOG note; recorded here so the flip has a slot, not gate-blocking for W1 exit.

Items NOT on the list (explicitly): coverage ratchets, mutation testing, LSP E2E revival, SBOM/hermetic builds, #782/#781 full fixes, #764 full lowering, D1/D2 numeric-semantics fixes — all tracked in their issues with the W1 triage comments.

---

## 4. W1 slices (PR plan, ordered)

| Slice | Contents | Feeds | Size |
|---|---|---|---|
| **1 — soundness batch** | T1: un-whitelist Replace/narrow-int/mixed-signedness in contracts → `unsupported`, Replace row in divergence table, cache bump per ledger; **D8**: contract-expression `/`/`%` side-conditioned via the D-G2.5 producer or un-whitelisted; **D6/D7 adjudicated** (eliding-false-Proven possible? → un-whitelist or record why not); T2: #764 structural return lowering to the §1.1 bar; #781 `ContractSimplificationPass` reachability check → include preservation fix or defer | PP-A1 item 6; the verify surface adopters see | M |
| **2 — trust surface** | #792 telemetry opt-in subset; T3 containment (`format --write`, LSP rename, LSP formatting); #789 checksummed natives | PP-A1 items 3, 5, 7 | S |
| **3 — shared compile helper + conversion honesty** | #771/#761 shared Roslyn compilation helper; #770 gate subset (classification, counts+locations, no false "✓"); #774 gate subset; #773 classifier | D-W1.3; W4 eligibility predicate | M–L |
| **4 — SDK package + gates** | #787 subset (topology decision first, then pack + local-feed consumer fixture = M-A1); #790 subset; #788 subset | D-W1.2; PP-A1 items 1, 2, 4 | L |
| **5a — fidelity core (W1-exit-blocking)** | #776 minimal implement-and-report subset: per-project coverage % (reverted files in denominator), populated `Gaps`/`InteropBlocks`, all-TRX aggregation, verdict dimensions — reporting on the 4-project corpus | D-W1.4; the plan's W1 exit criterion | M |
| **5b — fidelity completion** | #772/#775/#777 containments; threshold config; inventory-shrink=fail; corpus vendoring prep (feeds D-W4.2); CI flip to blocking (#790 leg) | The A-1.4 fidelity gate | M |
| **6 — housekeeping** | Latency-fixture re-baseline (both fired triggers, one regeneration per its README policy); epic checkbox reconciliation comments on all triaged issues; **post the D1 release-policy amendment on #793 per its Reporting section** (review m4) | Register hygiene | S |

Slices 1–2 are ordered first because they are live-defect and trust-critical; 3→5 have a stated dependency chain (#770 contract → #773/#774 consume → #776 reports against it); 4 is the calendar-critical path and parallelizes with 3/5.

## 5. Box confirmation (the §6.1 scope gate, exercised)

The plan's W1 box was **3–4 wk**. The triage grew the floor: the Slice-1 soundness batch, T3 containment, and the #774/#773 gate subsets were not priced in the plan's floor (the plan anticipated growth: "the items named below are the floor…; triage may grow it"). **Corrected box: 4–6 wk calendar-equivalent**, with the note that realized program velocity has run far under boxes (wedge plan §6.3). W1 **exit** does not require every slice merged — exit criteria remain the plan's: M-A1 green (Slice 4); **fidelity core implemented and reporting on the 4-project corpus (Slice 5a — W1-exit-blocking, per the plan's own exit criterion; only Slice 5b's completion items may slip into the W4-entry window** — the C1 fix: the fidelity *gate criterion* cannot slip because W4 depends on it, `wedge-plan-v0.11.md` §3); the §3 list frozen (done here); triage closures/comments merged. Slice 3 may complete inside the W3/W4 window without blocking W1 exit, but Slice 1 and Slice 2 **must** land before any W2 strictness-batch merge (they share the verify/trust surface PP-W5's control comparison reads).

**Control arm recorded:** tag `v0.10.0` = `e24a6832` (immutable, on origin) — the PP-W5 parity control, per wedge plan §3. **PP-W5 A-1.4 tranche 1 must freeze before the WS-W2 batch merges** (sequencing pin restated). **Adopter search opens with this record** (deadline: Call W; kill wording pre-committed in the plan).
