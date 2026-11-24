# Adversarial review of the Calor progress assessment (2026-11-23)

Subject: `assessment-under-review.md`, written against Calor HEAD `bc80daeb` (v0.30). Its claims are labelled H1, P1-P6, M1-M2, Z, R1-R6 and PRIO, and this review uses those labels.

How this review was produced: seven reviewers attacked the assessment from different angles (facts, the Rust bar, language soundness, measurement, roadmap and capacity, adoption and corpus, omissions). Every finding was then checked against the repository and the issue snapshot by a second reader, who also argued the assessment's side. This document merges the results. Where two checks disagreed, the finding says so and says which the evidence favours. Four points were re-checked by the editor and are marked "Editor's check".

Limits that apply to everything below: nothing was built or run. The three corpus submodules (MediatR, Serilog, FluentValidation) are not checked out, so nobody has counted guard clauses in them. Published agent benchmarks for Rust and C# could only be read through search summaries, because arxiv.org and swebench.com were unreachable.

All paths are relative to the repository root. Issue numbers refer to the open-issue snapshot of 2026-11-23.

Terms used here:

- **Arm**: one condition in an agent experiment (for example the C# arm and the Calor arm run the same task).
- **Pair**: one benchmark task under `bench/phase0-agent-native/pairs/`, with a C# version and a Calor version and a shared hidden test suite.
- **Seeded-defect pairs (W5)**: nine pairs whose starter code contains a planted defect; the question is whether the agent removes it.
- **Native / preserved**: a converted file is native when it is real Calor, and preserved when the converter keeps the whole file as C#.
- **Refinement**: the only precondition form in 0.30, written on a parameter.
- **Equipped C#**: the comparison arm planned in #1709, C# with comparable explicit specifications.
- **Soundness sweep**: the daily job that checks the verifier's "proven" claims against an independent oracle.
- **Proven / Assumed**: the verifier's two outcomes for a contract it accepts; only Proven removes the runtime check.

## Verdict

The assessment's direction is defensible and its conclusion (no decisive evidence yet, decisive test far away) survives. Its description of the evidence is wrong in ways the maintainer would spot immediately, and both things it says to start with fail as written.

What is wrong with it:

1. **The evidence record is misstated.** P4 says every attempt to measure an advantage ended undecided. One Calor-versus-C# result was adjudicated a hit (9 of 9 seeded defects removed against 4 of 9), and the real-scale venue was adjudicated a miss and retired. P5 quotes a July figure from three runs per arm that the repository itself replicated, attributed and partly closed, all on a syntax 0.30 deleted. Nothing has been measured with an agent on the 0.30 language.
2. **Both starting priorities (PRIO: R1 and R4) fail as written.** A Rust arm cannot be added to #1605: the harness is bound to .NET, #1605 gates two later milestones, and the harness cannot yet run a 0.30 Calor arm at all. Lifting throw guards into refinements changes the exception a program throws, which the converter's own tests and its default mode are built to reject.
3. **The other recommendations need rewriting.** R2 leaves out the only lever the repository has measured (an in-context exemplar). R3 treats null enforcement as absent when it has been enforced at three boundaries since 0.22. R5 gives the freeze no date while 0.32 schedules breaking changes. R6, used as a task-selection rule, conflicts with the 0.37 design and repeats a documented trap.
4. **It misses what actually gates the plan.** The only instrument that checks proof soundness is failing, unscheduled and written in the retired syntax. Milestones 0.36 to 0.39 depend on an external maintainer whom nobody has been authorised to recruit. Agents cannot add tests without breaking the rules `calor init` gives them. Known-unsound built-in effect contracts have no milestone. The recommendations add six workstreams and remove none.

What survives: P1's first sentence, P3's list of newly charged constructs, P6, P2's numbers and its attribution for the measured sample, the milestone table as a summary of the tracker, M1's count, M2, the narrow premise of R4 (the converter emits no contracts), the substance of R5 (stop breaking the syntax), R6's negative advice (small well-specified functions cannot discriminate), and H1's working conclusion.

What the findings add up to for the plan: 0.31 should make the instruments run and measure the 0.30 language once; 0.32 should be the last breaking window, carry a re-run of the seeded-defect comparison with an equipped C# arm, and end with a declared freeze; 0.33 to 0.35 keep their order with amendments; adopter outreach needs a maintainer decision now and a dated checkpoint at the end of 0.33.

## Findings

Severity: **critical** means the finding defeats something the assessment says to do first, or would misdirect the next milestone. **Major** means it changes what a milestone should contain or how a claim must be worded. **Minor** means wording or tracker hygiene.

### AR-1. A Rust arm cannot go into #1605, and #1605 cannot yet run on the 0.30 language

**Severity:** critical. **Targets:** R1, PRIO, M2.

**The attack.** R1 says to add a "small and cheap" Rust arm to #1605. Four verified facts defeat that placement.

- *#1605 is the wrong vehicle.* It is a before/after study of Calor itself: arm A is the old compiler, arm B' the new compiler with a mechanically translated primer, arm B the new compiler with the new primer. All three arms are Calor; C# appears only as a report line. It says it uses "no registration or freeze process". #1645 (0.32) and #1651 (0.33) both state "Depends on #1605", and #1710 (0.37) reuses #1651's drivers. It already slipped out of 0.30, is labelled p2, is still titled "[0.30]", and its done-when clause "revisited before 0.30 ships" can no longer be met.
- *The harness is bound to .NET.* `bench/phase0-agent-native/run-pair.sh` (1,893 lines) rejects any arm other than `calor` or `csharp` (line 282), counts iterations through a `dotnet` PATH shim (lines 20-23), tells the agent to run `dotnet build` (line 1257), and grades with a generated xUnit project that references the arm's `Src.dll` (lines 780-801). `task-spec-schema.json:20` allows only the `dotnet-test` runner. Each pair's hidden suite reaches the arm through `tests/shims/TestShim.calor.cs` and `TestShim.csharp.cs`. `docs/plans/agent-native-strategy.md:130` prohibits writing tests per arm, because that makes escaped bugs incomparable. A Rust arm therefore needs a new arm type, a cargo shim, Rust starters and references, and an oracle that does not depend on .NET.
- *The Calor arm cannot run on 0.30 today.* All 102 `.calr` files under `pairs/` are in the retired syntax, which 0.30 rejects with lexer error Calor0006 (`CHANGELOG.md:28-32`). The harness's own arm check ("canary") is also retired syntax (`run-pair.sh:553` and the inline text at line 575), so a 0.30 compiler is refused before any agent starts.
- *On the existing tasks C# is already at the floor.* The C# arm was first-try green in 60 of 60 runs in the July attribution epoch (`epochs/e1a-attribution/VERDICT.md:62-66`) and at 1.0 iterations with zero escaped bugs in `hardness-check-002`. On first-build rate and iterations a Rust arm can only tie or lose there. "First-compile rate" is not a defined metric in `docs/plans/agent-native-gates.md:33-43`.

Only the API spend would be small (40 to 90 runs, roughly $20 to $200), and this program has recorded overruns of 2.8x and about 4x (`docs/plans/pp-w5-spend-authorisation.md:84-93`, `epochs/guarantees-probe-001/VERDICT.md:20-24`).

*Editor's check.* Several reviewers said no pair exists in 0.30 syntax and that the pairs must be translated by hand. That is wrong for 25 of the 31 pair directories. Translated copies of 25 starters and 25 reference solutions, including all nine seeded-defect pairs, already sit under `tests/TestData/ActiveSyntax/bench/phase0-agent-native/pairs/`, none contains a retired marker, and `tests/Calor.Compiler.Tests/BenchCorpusCompilesTests.cs` compiles the 25 references through that mirror. `run-pair.sh` never refers to the mirror. The remaining work is wiring and validation, not translation. The six `W-00x` directories (52 files, the paused effect-rows research fixtures) have no translated copy; their fate belongs to #1648.

M2 is confirmed: the only open issue that mentions Rust is #1719, as a possible emission target.

**Conflicts between checks.** (a) Two reviewers called a Rust reading "not informative". The checks agree that this overstates it: the run would show whether Rust also sits at the C# floor on cost, which locates the bar, but it cannot show a Calor benefit. (b) One check proposed two scalar pairs for a later Rust run; three proposed tasks with headroom (file and CSV tasks, where Calor measured 2.27x to 2.50x). The evidence favours headroom, because scalar pairs are at the floor for every arm; such tasks need a language-neutral oracle. (c) One reviewer suggested the real-scale revert tasks as a venue. That venue was retired on 2026-08-05 with zero eligible revert tasks (`docs/plans/call-s-adjudication.md:3`).

**The defence.** The maintainer named Rust as the bar, and M2 is right that nothing measures against it. Knowing whether Rust sits at the same floor as C# is a legitimate question. The error is in the cost estimate and the choice of #1605, not in wanting the comparison.

**Proposed change.**

1. Keep #1605's three-arm design. Relabel it p1, retitle it "[0.31]", and replace the dead clause with "each regressed construct is filed against #1645". Name an explicit C# arm run at the same time on the same pinned model. Add a written spend ceiling, abort triggers, and a zero-cost dry pass (`--null-agent`) before any paid run. Make "results published" the exit criterion of 0.31.
2. Add a 0.31 issue that blocks #1605: "run-pair.sh runs the 0.30 Calor arm". Point the harness at the translated copies without touching the frozen originals, add a 0.30 arm check, verify each `TestShim.calor.cs` against the names 0.30 generates, re-validate the nine seeded-defect pairs with `check-probe-pair.sh`, and pass `--null-agent` on every non-frozen pair.
3. Attach no Rust work to #1605 or to 0.31.
4. File one non-gating issue in 0.33 beside #1651, where the agent driver is being abstracted anyway: an arm abstraction plus a language-neutral, process-level oracle (shared test vectors or a command-line protocol), which #1709 and #1710 need as well; then a Rust reference on two to four tasks with headroom. Entry criteria before any spend: the tasks are shown not to be at the floor for C#, or the issue states in advance that it measures cost distance only; the primary measure is tokens and cost per accepted task, with iterations and first-build rate reported descriptively; each outcome, including "Rust equals C# at the floor", has a reading written in advance; a spend ceiling is approved. The result is reported, not adjudicated, and gates nothing. If the criteria cannot be met, drop the paid run.
5. Optionally offer Rust as a fourth, non-gating condition in #1709 and #1712 on #1708 tasks with measured headroom, with its budget inside #1712's authorisation.
6. Express the Rust bar in the plan as a capability checklist that needs no benchmark: whole bug classes rejected at build, diagnostics that carry fixes, tests written in the language, one toolchain command, warm feedback latency.

### AR-2. Lifting throw guards into refinements changes program behaviour, and the converter's gates are built to reject that

**Severity:** critical. **Targets:** R4, PRIO.

**The attack.** R4's narrow premise is true: the converter builds every function with empty contract lists (16 sites in `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs`, the first at lines 2887, 3505 and 3970), and `CHANGELOG.md:39-40` says conversion does not recover contracts written as throw guards. The recommendation fails on what a lift would do.

- *A refinement is not a neutral respelling of a guard.* It always emits `throw new ArgumentOutOfRangeException(nameof(p), "Violation of ...")`, in every contract mode (`src/Calor.Compiler/CodeGen/CSharpEmitter.cs:6496-6497`; `docs/syntax-reference/contracts.md:37-41`). Custom messages "have no replacement" (`CHANGELOG.md:47-50`). The contracts reference tells authors to keep a body guard when an API needs a particular exception type (`contracts.md:299-301`). Any guard that throws something else changes type when lifted, even if the body guard is kept, because the refinement fires first.
- *The gates pin this.* In `tests/Calor.RoundTrip.Synthetic/SyntheticLib`, the only guard-bearing round-trip subject in this checkout, five guards are pinned by exact-type assertions and four would fail after a lift (DivideByZeroException, ArgumentException twice, ArgumentNullException). The member-level oracle compares thrown exception types (`bench/corpus/README.md`). Default `calor convert` is Lossless and refuses recorded semantic loss. `CHANGELOG.md:1903-1906` states "Conversion does not invent defaults, throws, or implicit unwraps".
- *Four further differences, each documented or pinned by a test.* (1) A refinement is re-checked on every assignment to the parameter (`docs/syntax-reference/refinement-types.md:17-19`), so C# that validates and then reassigns would start throwing mid-body. (2) A refinement on an implementing method whose interface has none is a stronger-precondition error (`src/Calor.Compiler/Verification/ContractInheritanceChecker.cs:1631-1643`); an inherited one throws a different exception and disappears under `--contract-mode off` (#1735, open in 0.31). Both round-trip compile paths use contracts off and effects unenforced (`tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs:677-680`, `:1596-1599`), so CI would not see this. (3) Replacing a body guard removes `throw` from the effect row of code that still throws. (4) A `!= null` refinement on a nullable parameter does not narrow its type, so replacing such a guard can introduce new Calor0272-0274 errors.
- *Null guards mostly restate an existing check.* For nullable-enabled sources a null guard on a non-null parameter repeats what Calor0274 already enforces at call sites.
- *It is new inference work.* `ContractInferencePass` infers refinements only from divisor and array-index use, and only on top-level functions (`src/Calor.Compiler/Analysis/ContractInference/ContractInferencePass.cs:43-45`, `:64-73`).

**Conflict between checks.** One check proposed lifting "preserving exception type and message behaviour". With refinements as 0.30 defines them that is possible only for guards that already throw ArgumentOutOfRangeException on the named parameter. The evidence favours the narrow reading.

**The defence.** R4 is one sentence of direction. It does not say the lift replaces the guard, is on by default, or runs under Lossless. Its goal can be met without tripping any gate, and CI would reject the naive form on the first pull request, so the cost of the error is a mis-scoped epic and not shipped unsoundness. The language reference itself says "Use a refinement, not an if-throw guard" (`docs/syntax-reference/current-language.md:388`).

**Proposed change.**

1. Remove "lift contracts during conversion" from PRIO and from the default conversion path.
2. Add one design-decision issue in 0.32 next to #1735: "Guard-derived preconditions: which form?" Record the options in this order: (a) verifier-only facts taken from guards that dominate the rest of the body, with no source or runtime change (preferred; see AR-10); (b) an opt-in convert switch, never part of Lossless, limited to leading guards that already throw ArgumentOutOfRangeException for one named parameter, recorded in the loss ledger; (c) letting a refinement carry an exception type and message, which reopens #1618 one release after it closed, so the recorded default is no.
3. If option (b) is ever built, write the exclusions into its specification as tests: never lift when the parameter is assigned, incremented or passed by ref; never on a virtual, abstract, override or interface-implementing member until #1735 lands; never a null guard; only leading guards with no side effect before them. Acceptance: zero new member-oracle differences, round-trip floors and pass counts unchanged in both legs, byte-identical default output with the switch off.
4. Independently, add a round-trip leg (the synthetic library is enough) that compiles with the default contract mode and with effects enforced, so that contract-inheritance and effect-row regressions become visible. Place it in 0.31 or 0.32 beside #1735.

### AR-3. R2 leaves out the only lever the repository has measured, and that lever ships behind a pointer nobody has tested

**Severity:** critical. **Targets:** R2, Z, P5.

**The attack.** R2 calls the green-field penalty the largest measured loss and then names two remedies: finish the teaching-reference budget (#1734) and track a safe-patch share. The repository has measured one lever on that penalty and it is neither.

- In the July attribution epoch (30 runs per arm), appending a roughly 60-line syntax exemplar to the prompt removed 55% of the excess iterations (2.38x to 1.62x pooled; one pair went to 1.00) and cut mean input from 1.69M to 0.59M tokens per run (`epochs/e1a-attribution/VERDICT.md:13-28`; #1605). The verdict attributes the cost to "thin training prior / missing in-context syntax reference". `docs/plans/machine-zone.md:196` made it binding that the exemplar ship "in every agent-facing surface".
- As shipped in 0.30 the exemplar is reachable only as the MCP resource `calor://primer` (`src/Calor.Compiler/Mcp/McpMessageHandler.cs:124-130`). The four `calor init` templates are byte-identical, 317 tokens, and say "Read calor://primer" plus five bullets. `calor init` writes no copy of the primer, `calor_help` does not serve it, and no CLI command prints it. The epoch measured the sheet appended to the prompt (`run-pair.sh:1261-1262`). Whether agents read it behind a pointer has never been measured.
- #1734 trims the on-demand full reference (16,750 tokens). #1605's own arithmetic says cutting 1.7K always-on tokens saves 30K to 55K per run, against about 1.1M from the exemplar.
- The always-on MCP surface is 7,900 tokens for 19 tools (`tests/Calor.Compiler.Tests/TokenBudgetTests.cs:65-71`). Scaling #1605's arithmetic gives roughly 140K to 255K tokens per run; that is an estimate, not a measurement. The benchmark harness loads no template, primer or MCP server, so the path users get from `calor init` has no cost number at all, and the 35% tokens-to-green gain attributed to the MCP path is a July figure on the old syntax.
- Premise Z says diagnostics and toolchain "have to carry it". The in-context reference is a third lever and the only one with a measured effect. The other two were not in the measured loop, so they are untested, not refuted.

One correction to the reviewers: 0.30 did not move the primer behind a pointer. It has been an MCP resource since 0.6.x (`CHANGELOG.md:4041`); 0.30 trimmed the inline guidance in the templates.

**Conflict between checks.** The same point was rated critical as a defect in R2 and minor as a wording problem in Z. It is kept critical for R2: R2 addresses what the assessment itself calls the largest measured loss and names two levers the repository's own numbers rank far below the omitted one. The Z half is a wording correction.

**The defence.** The project has not ignored this. #1605 design item 5 calls for an arm inside a `calor init` workspace, and `run-pair.sh` already has an `--initialized-workspace` mode for it. A pointer costs one 1.6K-token read if agents follow the template's rule. The exemplar result covers two pairs on the old syntax, 45% of the penalty remained with it, and 0.30's C#-like forms may shrink both the penalty and the exemplar's benefit.

**Proposed change.** Replace R2's body with an exemplar-delivery epic in 0.31, run inside #1605.

1. Use the existing `--initialized-workspace` mode for three variants: pointer only (as shipped), primer inlined in the generated agent file, and primer plus a starter `.calr` file. Log whether `calor://primer` was read. Add two `calor init` arms on the same pairs, MCP server enabled and CLI only, and report total input tokens, cached against uncached.
2. Fix the decision rule before the run: if the pointer-only variant reads the primer in fewer than 9 of 10 runs, or costs more than 10% above the inlined variant, `calor init` inlines the primer (raising the 600-token template budget to about 1.6K and removing the rules duplicated between the template and the MCP instructions) and writes a starter module.
3. Serve the primer outside MCP resources: a `calor_help` action and a CLI command.
4. Pull only the resource-read check from #1652 forward to 0.31 for Claude Code, Codex, Gemini CLI and Copilot.
5. Order #1745 (run every example) before #1734, add to #1734 that it may not remove example programs, and rank #1734 below this measurement.
6. Set the always-on MCP budget from the result. If the MCP path does not earn its per-turn cost on 0.30, file a 0.32 issue to merge or lazy-load tools.
7. Reword Z: "A training corpus is out of reach. The available levers are the in-context reference, diagnostics and the toolchain. The first removed 55% of the green-field penalty on the old syntax; the other two were not in the measured loop."

### AR-4. P4's "every attempt ended undecided" is false, and H1 is too absolute

**Severity:** major (the reviewers rated it critical; all three checks lowered it because the corrected headline narrows H1 without reversing it). **Targets:** H1, P4.

**The attack.** The epoch directory the assessment cites holds one adjudicated Calor-versus-C# result and one adjudicated negative that P4 omits.

- *The hit.* `epochs/ws5-probe-001` (2026-07-28, pinned model, nine seeded-defect pairs, two arms, three runs each, 54 of 54 valid): the Calor arm removed 9 of 9 seeded defects (27 of 27 runs), the C# arm 4 of 9 (14 of 27). The delta of +5 cleared a frozen threshold of 3 (`VERDICT.md:21-29`) and was adjudicated a hit at the maintainer's Call 2 decision (`epochs/m5-compare-001/VERDICT.md:83-89`).
- *The miss.* P4 quotes the 2026-08-04 close-out ("not adjudicated"). One day later the real-scale venue was adjudicated a miss on two counts (8 eligible tasks against 70 needed) and retired; the record says "the real-scale arm never contained Calor", because that arm shipped round-tripped C# and never invoked the compiler (`docs/plans/call-s-adjudication.md:3-4`, `:117-139`). The "August real-scale run" was a 36-run dry run, never an epoch.
- *The limits of the hit.* It measures detection of maintainer-seeded defects, and the registration says it "Measures detection capability, not organic incidence" (`docs/plans/agent-native-gates.md:323`). The C# arm held each contract only as a doc comment (`pairs/W5B-001-shipping-quote/csharp/Quote.cs:12-15`), and the fixture requires that the smoke suite pass on the defective C# starter and fail on the Calor one, so three of the five delta defects are close to guaranteed by construction. Only 2 of 6 effect-class defects produced a delta. The Calor arm took 1.63 iterations against 1.0 and 3.6 times the output tokens. It ran on the retired syntax, and the C# arm was never re-run.
- *"0.30 published no benchmark results" is not a 0.30 event.* The same sentence appears in every release from 0.24 to 0.30, and the latest live agent epoch started on 2026-09-01.
- *The README contradicts the project's own position.* `README.md:95` says "Calor shows measurable advantages in AI agent comprehension, error detection, edit precision, and refactoring stability"; `docs/benchmarking/results.md:20-23` and the website's adoption playbook say no such advantage is established. The website's claims test does not read the README.

Two further adjudicated hits exist, both comparing Calor with Calor (a tooling comparison and a build-time-proof comparison). They do not bear on an advantage over another language.

**The defence.** H1's working conclusion is the repository's own position: the results page disclaims any agent, correctness or safety advantage; the benefit gate is parked as unfalsifiable at authorable scale; #1698 calls 0.37 a falsifiable hypothesis. The one hit used a comparison arm the 0.37 design treats as insufficient.

**Proposed change.**

1. Restate H1: "A catch-rate advantage was shown once, at fixture scale, on nine maintainer-seeded defects against conventional C# (July 2026, pre-0.30 syntax). It has not been shown against C# with executable specifications, on natural defects, at real scale, on independently authored tasks, or on 0.30."
2. Restate P4 as a list of adjudicated outcomes: the seeded-defect hit; the real-scale miss and venue retirement; the parked benefit gate; and the Calor-versus-Calor results marked as such.
3. Describe 0.37 (#1709, #1711, #1712) as a confirmation under fairer controls of a probe-scale positive, not a first test.
4. Add one 0.31 documentation issue: an evidence ledger listing every adjudicated result with its comparison type, scope, model pin, syntax version and staleness. In the same issue, replace the README "Benchmark Results" paragraph with what is and is not established, and extend the claims test (or a self-check) to read `README.md`.
5. Structure the measurement track as three separate deliverables: an equipped C# arm on the existing seeded-defect set (AR-5); replication on 0.30 with a current model pin; a natural-defect comparison on independently authored tasks, which stays behind the pilot.

### AR-5. The cheapest informative test before 0.37 is missing: the seeded-defect pairs re-run with an equipped C# arm

**Severity:** major. **Targets:** omission; bears on H1, M1, R1.

**The attack.** The assessment's early measurement is a Rust arm. The known weakness of the one positive result is its control: the C# arm had no executable specification. That control can be added to the nine seeded-defect pairs without the pilot, without a new harness arm type (`run-pair.sh` already accepts `--arm-config` for a second C# configuration), and with a result that is informative either way.

- *Cost.* 135 runs at the recorded rates come to roughly $85 to $95; at this program's 4x overrun that is about $340 to $380, against a $1,500 epoch ceiling (`docs/plans/agent-native-gates.md:81`).
- *Likely outcome (an inference, not a result).* An executable C# assertion would probably reproduce the three contract-defect catches, leaving the two effect-class deltas, below the frozen bar of 3. That would say the July delta was mostly "specifications were present", and would redirect 0.35 toward what only build-time proof gives.
- *Limits to state in advance.* Fixture scale, seeded, detection only, and not independent: the equipped C# fixtures would be written by the same maintainer and agents, and #1708 says a different agent session is not independence.

*Editor's check.* One check said porting the 18 seeded-defect `.calr` files is a rewrite. Translated copies of all nine starters and references already exist in the mirror tree (AR-1); the copy of `W5B-001-shipping-quote` keeps both the seeded defect and the postcondition. What remains is validation with `check-probe-pair.sh` and harness wiring. The July result was registered for two arms, so a third condition needs an additive registration.

**The defence.** This is an omission, not an error. The predictable result limits its information value, the Calor arm is already at a 9 of 9 ceiling, and the work competes with 0.31 and 0.32 stabilisation.

**Proposed change.** Add an epic to 0.32, after #1605 has published: "Seeded-defect replication on 0.30 with three conditions (conventional C#, equipped C#, Calor)".

1. Validate the nine pairs on 0.30 with `check-probe-pair.sh`.
2. Author equipped C# fixtures as a second C# arm configuration: postconditions as executable assertions for the contract defects, a banned-API or purity analyzer for the effect defects where one exists, and a written list of what cannot be matched. Have a second reader review fairness.
3. Register thresholds and every outcome branch before any run: a Calor minus equipped-C# delta of at least 3 of 9 counts as a language effect; anything less counts as a "specifications were present" effect.
4. Authorise spend of at most $400 with abort triggers and one pinned model.
5. Run a zero-cost dry pass, then the run (three to five runs per arm per pair), then publish whatever results, labelled fixture-scale, seeded and non-independent, with catches reported by channel (build-time proof, build block, runtime guard, agent noticed) and cost per arm.
6. Record the result as an input to #1709 and #1711 and to the sizing of 0.35 and 0.37. It is a rehearsal for those issues, not a replacement.

### AR-6. The only instrument that checks proof soundness is failing, unscheduled, and cannot run on 0.30; the cheaper instruments are stale

**Severity:** major (one reviewer rated it minor; the check raised it). **Targets:** omission; bears on R4, R6 and the 0.35 row.

**The attack.**

- *The soundness sweep.* It generates 710 single-function cases from `tests/TestData/SoundnessSweep/` and compares each "proven" claim with an independent oracle, daily, in about five minutes. Issue #1694 ("Soundness sweep failing on main") was opened on 2026-10-28 with 10 gate failures, has no milestone and no priority label, and its recorded run predates 0.29. The sweep's own README says a changed verdict is to be treated as a compiler soundness bug.
- *It cannot run on 0.30.* The 136 templates are in the retired syntax (799 lines with a retired marker, none with `func`); `tests/Calor.Compiler.Tests/SoundnessRegistration/SweepCaseGenerator.cs:152` emits a retired module header; `bench/Calor.Soundness.Sweep/Program.cs:301` compiles a retired-syntax probe; `CaseExecutor.cs:172` searches for the retired postcondition marker. 0.30 makes every such marker a lexer error and merged the two precondition verification paths (#1618). The one compiler test that checks the cases still parse returns early once the front end changes (`SoundnessRegistrationTests.cs:126-137`), so CI does not notice. This is established by reading; no scheduled run after 2026-11-21 was observed.
- *Later milestones depend on it.* #1660, #1661 and #1662 (0.35) measure success by the sweep; #1697 requires #1694 "resolved and demonstrated healthy"; #1704 says it must not be postponed.
- *The cheaper agent instruments are stale too.* The end-to-end agent-task suite (129 tasks) has 63 live files in the retired syntax and counts contracts, effects and functions by searching for retired markers (`tests/E2E/agent-tasks/lib/helpers.sh:974-990`), so a run today would be biased against Calor (#1736). Its published 77 of 89 came from text-pattern scripts on compiler 0.2.3. The static evaluation found only 17 of 226 registered pairs equivalent under its registered oracle (`docs/plans/v0.24-b1-results.md:26-39`).

Scope corrections: the sweep tests contract proofs, not effect charging or null safety, so P3 and R3 do not rest on it. The claims that do are R6's "proven contracts", R4's "gives the verifier something to check", and the whole 0.35 milestone. Saying there is "no working regression net" is too strong; the ordinary compiler test suites still run.

**Conflicts between checks.** (a) Three checks put #1694 in 0.31 and two in 0.32. The evidence favours 0.31: the tracker says it must not be postponed, and nothing has been able to check proofs since the 0.30 rewrite. (b) Two checks said to port the packet in place; one said to register a successor. *Editor's check:* `tests/TestData/SoundnessSweep/registration.json` pins a hash per case and the README forbids editing the fixture to make the job green, so the evidence favours a successor packet, with expected verdicts set through the documented `freeze` command in a reviewed pull request.

**The defence.** The assessment was about agent benefit and claimed no advantage from any of these instruments. #1736 is already in 0.31, and #1697 and #1704 already name #1694 as a blocker for release claims.

**Proposed change.**

1. Assign #1694 to 0.31 as p1. First step: record the result of the most recent scheduled sweep on main. Then diagnose the original ten failures, which predate 0.30.
2. Add a sibling 0.31 issue: "Register a successor soundness packet in 0.30 syntax". Scope: the 136 templates, the case generator, the three literals named above, a new mapping for precondition cases now that preconditions are refinements, and re-arming the validity test. Set expected verdicts in a reviewed pull request that classifies each changed verdict as port-only or real, so the port cannot hide a regression.
3. Make "sweep green on main for a stated number of consecutive scheduled runs" an entry criterion for 0.35 and for any release note that says "proven".
4. Keep #1736 in 0.31 as a prerequisite of #1605's end-to-end leg, with compile-and-behaviour verifiers in place of text searches.
5. Exclude the end-to-end refactoring benchmark and the static evaluation from any advantage claim until the first has a neutral design and the second is restricted to oracle-equivalent pairs. Record both in the evidence ledger (AR-4).

### AR-7. P5's cost figures are stale, conflated, and describe a syntax that no longer exists

**Severity:** major. **Targets:** P5, R2.

**The attack.**

- "2.7x the iterations" was three runs per arm on 2 of 5 green-field pairs (CSV parsing and a file journal); the other three were at 1.0x (`epochs/hardness-check-001/VERDICT.md:3-10`).
- The registered replicate (2026-07-16, 30 runs per arm, unpinned model) measured 2.38x pooled, and 1.62x with the exemplar: 1.00 on the journal pair and 2.23 on the CSV pair.
- "$1.17-4.20" merges two conditions. Recomputed from the archived runs: without the exemplar the Calor arm cost $3.77 and $4.20 per run against $0.60 and $0.50 for C# (6.3x and 8.4x); with it, $2.23 and $1.17 (3.7x and 2.3x). At iteration parity the journal pair still used 3.4 times the output tokens. The gap is larger in cost than in iterations.
- A later epoch on a pinned newer model, with no exemplar and no concurrent C# arm, ran both pairs at 1, 1, 1, 1, 3 iterations (mean 1.4). The protocol forbids comparing across epochs, so this is an indication and nothing more.
- Everything predates 0.30, and `CHANGELOG.md:185-187` says before/after iterations-to-green are not measured. The penalty on the current language is unknown.
- At least 12 of 60 baseline-arm agents in that epoch said they learned the syntax by reading the Calor repository itself, and one found the task's answer key under the pair's `reference/` directory. Any re-measurement inside the checkout is contaminated (AR-8).

Corrections to the reviewers: "the penalty is largely closed" overreads five runs on another model; "the driver is context size, not language" is unsupported; the project did not retire iterations-to-green (#1605 still names it); the remaining CSV-pair cost was not traced to a typo in the exemplar; and P5 does state the dollar range, so the assessment did not hide cost.

**The defence.** P5 quotes the repository's own headline (`docs/plans/agent-native-strategy.md:370-375`), the effect reproduced at 30 runs, it is the only measured Calor-versus-C# loss, and P4 already says 0.30 published nothing.

**Proposed change.**

1. Restate P5: "Last measured July 2026 on the pre-0.30 syntax with an unpinned model: 2.38x iterations pooled over two pairs at 30 runs (2.7x at 3 runs), 1.62x with a 60-line exemplar; 6x to 8x the cost of C# without the exemplar and 2.3x to 3.7x with it; three of five green-field pairs and all modification pairs at parity. Not re-measured on 0.30."
2. Make #1605 the explicit re-measurement gate for all green-field work. Include the two penalised pairs and the three that were at parity. Report cost, input and output tokens, turns and iterations as ratios to the concurrent C# arm. State the close rule before the first run, on both units (for example: with the primer, a pooled iterations ratio below 1.3 and a cost ratio below 1.5 closes the green-field workstream; otherwise rank the remaining cost drivers from transcripts).
3. Fund green-field work beyond #1734 only if #1605 shows a penalty on 0.30 with the shipped primer, and scope it from the per-construct error log that #1645 already calls for.

### AR-8. Nearly half the `.calr` files in the tree are in the retired syntax, and benchmark agents are known to read the tree

**Severity:** major. **Targets:** R5, P6; precondition for #1605.

**The attack.** 853 of 1,823 `.calr` files contain retired markers: 497 under `bench/phase0-agent-native` (264 archived epoch outputs, 106 latency fixtures, 102 pair files, 24 fixtures), 286 under `tests/TestData`, 48 conversion snapshots, 15 under `docs/design`, 7 under `benchmarks`. About 135 of 494 Markdown files and 897 places in `CHANGELOG.md` contain them as well. The agent-teaching surfaces (primer, reference, `samples/`, `docs/syntax-reference`) are clean.

The hazard is in-context, not long-term: July's agents read the repository to learn the syntax (AR-7), so a 0.30 run inside this checkout will hand agents the dead dialect. The guard against relapse is thin: Calor0006 names a replacement for 15 markers and gives one generic sentence for every other (`src/Calor.Compiler/Parsing/Lexer.cs:741-764`), carries no fix, and no rewriter shipped (`CHANGELOG.md:12-13`).

This is not a cheap clean-up. The 264 epoch outputs and 15 hash-pinned spike fixtures are evidence and must not be edited, and the originals under `tests/TestData` are kept on purpose behind 767 translated copies.

**The defence.** P6 already records the rewrite and the missing migrator, so the finding supports R5. The legacy files are kept deliberately, and translating the live inputs is already in #1736 and #1605.

**Proposed change.** Add a 0.31 issue that blocks #1605.

1. Classify the 853 files: live inputs to translate with #1605 and #1736, and retained originals and frozen evidence to leave byte-identical.
2. List the retained paths in one allowlist, add a legacy-dialect note per directory, and add a self-check that fails on a retired marker in any `.calr` file outside the allowlist.
3. Run benchmark workspaces outside the checkout, or with the allowlisted paths unreadable, and log every read outside the workspace.
4. Generate Calor0006's guidance from the full inventory of retired forms.
5. Report Calor0006 hits per run in #1605.
6. Decide on a retired-to-current rewriter together with the stability policy (AR-12).

### AR-9. "Seven milestones out" counts correctly but names the wrong risk: the long pole is an external maintainer nobody has been authorised to recruit

**Severity:** major. **Targets:** M1, H1.

**The attack.**

- *The count is right and a weak clock.* 0.37 is the seventh open milestone. But 0.24.0 to 0.30 took 45 days (gaps of 13, 3, 4, 1, 1 and 23 days), 0.33 to 0.35 hold 6, 6 and 3 open issues, and `docs/plans/roadmap-v0.20.md:150-152` calls version numbers "containers, not execution dates".
- *The order encodes stated dependencies.* #1653 puts agent reach after 0.32 because "launching earlier would teach agents a language that's about to change". #1651 depends on #1605, #1710 reuses #1651, #1708 takes final task approval from the pilot issues #1701, #1706 and #1707, and #1712 needs #1697, an independent fairness review and separately authorised paid collection.
- *Cost comparisons with C# do exist earlier.* #1605 (0.31) and #1651 (0.33) both report against C#. What is missing before 0.37 is any measure of benefit.
- *The binding constraint is the adopter.* #1701 says inability to recruit an external maintainer is a blocker that creator self-evaluation may not replace. The record shows the adopter missing on three occasions and outreach attempted on none: the maintainer's demand decision of 2026-08-04 recorded "DEMAND UNPROVEN"; the study stop of 2026-09-09 recorded recruitment activity as unknown, "not a failed search"; the gate of 2026-10-01 recorded "Contacts made: 0" because no contacts were authorised (`docs/plans/safe-delegation-m0/v0.23/r2a-adopter-unavailable.md`). Naming an external adopter is a decision the maintainer reserved (`docs/plans/call-s-adjudication.md:187-190`), and the recorded policy is "do not fund new adoption depth ahead of demand". The tracker itself says #1701 recruitment and #1708 protocol drafting can start immediately.
- *A similar three-arm study was the 0.20 roadmap* and was parked with no adopter, no evidenced affordable route to adequate statistical power, no budget and no methods reviewer (`roadmap-v0.20.md:36-48`). The same record says this is not proof that every paired design is infeasible.

Corrections to the reviewers: "twice failed to secure an adopter" is wrong, since outreach was never attempted; the claim that 0.34 to 0.36 are the three August remedies in order is a loose reading that ignores the venue's retirement; the assessment never proposed pulling #1712 forward; and a real-scale open-source probe cannot simply be scheduled, because that venue has registered re-entry conditions (at least 70 evaluable candidates against 8 realised, and a Calor arm that actually invokes the compiler).

**The defence.** M1 is a true statement, and this evidence makes it stronger. The assessment drew a modest conclusion and asked for an early measurement for exactly this reason.

**Proposed change.**

1. Keep the 0.31 to 0.37 order and leave #1712 in 0.37. Restate M1: "The decisive comparison is gated on an external pilot (0.36) with no secured maintainer; the last adjudicated record is demand unproven."
2. Add non-engineering items to 0.31 that run beside compiler work: a maintainer decision issue "Authorise and log adopter outreach", with a named contact list and an outreach window; the start of #1701 recruitment and baseline work and #1708 protocol drafting; and a one-page sizing and budget request for #1708 and #1712 (outcome measures, repetitions the variability from #1605 supports, spend ceiling, an explicit statement of limited power), refreshed when #1605 publishes.
3. Add a dated checkpoint at the close of 0.33: either a named external maintainer has consented, or "demand unproven" is recorded and a fallback written in advance applies: re-scope 0.37 to a fixture-scale seeded comparison labelled non-independent, hold 0.38 and 0.39, and cut 0.34 and 0.35 to what the #1605 and #1651 results justify.
4. Add the inputs the September stop listed as missing as issues on the 0.37 epic: a task supply inventory, a named independent methods reviewer, an authorised collection budget.
5. Add a feasibility gate for reopening the real-scale venue under its two registered re-entry conditions, to be attempted only if #1647 and 0.34 raise the native share.
6. Write the dependency edges into the epics, so the order reads as a chain and not as delay.

### AR-10. A lifted contract pays off only where code is native and something consumes it; neither holds for most converted code today

**Severity:** major. **Targets:** R4, PRIO, M1.

**The attack.**

- *Where a lift can land.* Only in natively converted members. Under default `calor migrate` at 0.30, MediatR and Serilog came out entirely as whole-file preserved C# (#1746). In the round-trip harness the native share of files fell to 10 of 32 (MediatR), 58 of 139 (FluentValidation) and 36 of 112 (Serilog) in lossy mode, and to 5 of 32, 5 of 139 and 10 of 112 in default mode (`eng/roundtrip-baselines.json`; `docs/plans/2026-11-16-milestone-0.30-ci-triage.md:363-367`). Recovery is #1647 (0.32, p1), and the converter visitor is 17,630 lines with that regression open.
- *What consumes a precondition.* Call-site obligations are generated only for module-level functions calling same-module functions (`src/Calor.Compiler/Verification/Obligations/ObligationGenerator.cs:58-62`), and only when every argument is exactly `int` or `bool` (lines 118-132). Class methods, constructors and operators get none, and converted C# is almost entirely class members. A parameter's own entry obligation is a boundary assumption that is never solver-checked. The contract translator refuses null literals (`Verification/Z3/ContractTranslator.cs:837-841`); string, array and user-type proofs are demoted to Assumed. The obligation engine's fact collector gives a guard condition scope only inside the guarded body, so `if (c) throw` contributes no fact to the code after it.
- *What already works without any lift.* The bug-pattern pass imports provably non-zero parameters from preconditions on methods (`Analysis/VerificationAnalysisPass.cs:218-275`), index-bounds obligations inside a method use the parameter's refinement as a fact, and the control-flow engine already narrows on conditional edges, so body guards reach those checkers today. Numeric ranges and array lengths are not imported (#1723, no milestone).
- *"0.35 has little to prove" is half right.* Index-bounds obligations are generated for every array access without any authored contract, so #1661 has work today. #1660 models base-library calls inside contract bodies, which converted code lacks. Neither waits on R4: #1660 and #1661 are accepted on test pairs and the sweep, the 0.37 arms use authored specifications (#1709), and from 0.36 obligations are defined and governed by people (#1701, #1702).
- *Nobody has counted guards.* One reviewer's "well over 90% are null guards" comes from the Calor compiler's own source, where 569 of 683 null sites are `?? throw` expressions in constructors. The August statement that guard sites sit "disproportionately" in the non-native half carries no count.

**Conflict between checks.** Two checks held that a lifted precondition is not vacuous because it creates obligations at call sites. A third read the generator and found almost none are created for class members. *Editor's check:* the generator calls `GenerateCallObligations` only inside the loop over `module.Functions`. The evidence favours the third: the point is true in principle and nearly empty in converted code today. A related conflict is timing: two checks place an implementation in 0.34 so it lands before 0.35, one places it after the consumers exist. Without consumers the lift yields little, so a 0.34 commitment is not supported.

**The defence.** The premise is correct, and preconditions are a necessary first input for any later proof. The gaps listed are properties of the verifier that 0.35 could close, which argues for doing both in order. About a third of files are native in lossy mode, enough for a census and a prototype, and the true prerequisite is one bug-fix issue.

**Proposed change.**

1. Add a read-only census issue, finished before the 0.32 breaking window closes: on the pinned corpus source and on agent-authored outputs, count guards by shape (if-throw, `?? throw`, ThrowIf helpers), exception type, custom message, container kind, position, whether the parameter is reassigned, and native against preserved file. Register go, hold and drop thresholds in advance.
2. Put a "contract consumers" epic in 0.35 beside #1660 and #1661 and ahead of any converter lift: call-site refinement obligations for class methods, constructors and operators (same module first); argument types widened to the integer types the translator already models; #1723 scheduled; the fall-through fact after a dominating `if (c) throw`; a recorded decision on modelling reference null. Each step gets seeded unsound controls in the sweep.
3. Add to #1662 an issue that says where 0.35's contracts come from (agent-authored and hand-authored on a named set of native modules, not lifted) and adds a proof-yield metric on real programs: obligations Proven, Assumed and Unsupported, before and after.
4. Make any converter lift conditional on #1647 closed, a census "go", and the design decision in AR-2. Place it no earlier than 0.34 under #1659, as an opt-in. Its done-when reports lifted contracts per project, caller-side obligations created, and how many are Proven.

### AR-11. R4's history is wrong on mechanism: neither past failure was caused by missing guard lifting

**Severity:** major (two checks; a third rated it minor). **Targets:** R4, PRIO.

**The attack.** R4 says missing contract lifting "starved both the April flow checker and the August benchmark".

- *April.* The reverted flow checker tracked unwrap-before-check on Option and Result. It found nothing because 262,142 converted files contained zero `.unwrap()`, `.expect()` or `.is_some()` calls (`docs/design/tier1a-postmortem.md:42-44`; `docs/design/calor-direction.md:92-98`). Lifting guards would not have produced one such site, and 0.30 has since removed Option and Result (#1617).
- *August.* The venue was retired for lack of supply (8 screened tasks against 70) and because the Calor arm never invoked the compiler. The dry-run verdict says its injected arithmetic, off-by-one and boundary mutations are a class that "a mechanical (non-authored) contract" cannot catch (`epochs/w4-dryrun-001/VERDICT.md:58-63`). The close-out names converter fidelity, checker breadth, corpus shape, and a missing authored-contract overlay.
- *The remedy the project registered was different.* The overlay (hand-authored contracts laid over converted code, required to be "blind by construction") was planned in `docs/plans/substrate-plan-v0.12.md:107-114`, closed unresolved, and called "the single largest open question" handed forward (`docs/plans/call-s-adjudication.md:169-173`). No open issue mentions it.
- *A contract derived from the code under test fails the blindness rule.* If a defect is seeded in the guard before conversion, the lifted contract moves with it. Benchmark use needs contracts pinned from the clean baseline, which is the option #1177 already poses.

What is right in R4's sentence: both efforts did lack checkable material in converted code, and the April post-mortem lists "the pipeline is changed to translate idioms" as a remedy.

**Conflict between checks.** One check proposed reviving the overlay as an epic now; another would finish it only if the real-scale venue reopens. Given the capacity finding (AR-13), the evidence favours the smaller version: authored contracts on a named set of native modules are needed for 0.35 in any case (AR-10, item 3); the overlay format and re-apply tooling wait on a decision to reopen the venue.

**The defence.** "That" in R4 most naturally refers to "converted code gives the verifier nothing to check", and on that reading the sentence is true of both episodes.

**Proposed change.**

1. Reword R4's justification: "converted code carries no contracts or postconditions; the April and August efforts both lacked checkable material, for different reasons (no Option/Result sites; no authored overlay, a supply collapse, and a Calor arm that was never compiled)".
2. Record a decision on the overlay in 0.32: either a feasibility record on one FluentValidation module (deterministic, re-appliable, derivable without reference to any injected defect) sequenced after #1647, or an explicit deferral tied to the venue's re-entry conditions. Decide #1177 in the same issue.
3. Add one rule to #1709 and #1711: contracts derived mechanically from the implementation are labelled "derived", reported separately, never counted as specification-backed proofs, and always accompanied by a mutated-guard control.

### AR-12. The freeze belongs at the end of 0.32, after one last breaking window, with an edition marker and a rewriter rule

**Severity:** major (three checks; a fourth rated it minor). **Targets:** R5, P1, P6.

**The attack.** R5 gives no date. The tracker keeps the syntax open on purpose.

- *The 0.30 spellings have never been measured with an agent.* The decision record lists its own weak choices: the arrow and `func` win by one token in one context, the sample was "hand-picked ... speculative hand-written text", and the pattern spellings are "a provisional inventory gate" (`docs/design/0.30-syntax-token-decision.md:15`, `:570-572`).
- *Breaking work is scheduled.* #1646 (0.32): "Flip any choice whose ranking changes ... a parse-error change plus a repo rewrite". #1645 (0.32): "if the form itself is the problem, revisit it". #1743 and #1741 (0.31) may add an error and remove legacy effect codes. #1735 is still aligning refinement exceptions. Brace-header forms are accepted pending "a separate decision".
- *#1653 already assumes stability at the start of 0.33*, which supports a freeze at the end of 0.32.
- *P1 overstates the borrowed C# prior.* The recorded rule is "fewest tokens, not the most readable one", with the C# spelling only as a tie-break. The result includes forms that are not C# (the arrow, `func`, `each`, `set a[k] v`, `type:name`) and two that look like C# and mean something else: `var` marks mutability, and `=>` in a header is a postcondition (`docs/syntax-reference/current-language.md:59`, `:403`). #1645 confines any remedy to the fewest-tokens rule, so familiarity cannot win a revisit.
- *There is no edition mechanism.* The strategy committed to "Syntax editions before any further breaking change" (`docs/plans/agent-native-strategy.md:210`), which 0.30 bypassed. The per-module `semver` line requires an exact major match, so it is a version gate. #1084's body is stale: the fail-closed version check it asks for is already wired (`src/Calor.Compiler/Parsing/Parser.cs:980`).
- *The measured cost of 0.30's churn is internal:* 1,130 failing compiler tests at the cut-over, and orphaned benchmark pairs, end-to-end files and the soundness packet.
- *R3 interacts with it.* Null tightening rejects programs that compile today, so it must land before the freeze or wait for an edition. Additive syntax (#1643, #1654 to #1658) does not conflict with freezing existing spellings.

One stale premise to fix: #1646 says the choices were first estimated with a proxy tokenizer. They were counted with Anthropic's `count_tokens`; what is still on the proxy is the density ratio (#1746) and the teaching budgets (#1734).

**The defence.** R5 and P6 are correct, and the reviewers agree with the freeze. The assessment never said to freeze today, and R5 is not in PRIO. The finding gives R5 a date and a mechanism.

**Proposed change.**

1. Add a 0.32 issue, "Last breaking window": batch every pending breaking decision into one release with one repository rewrite (#1646 flips, #1645 revisits, #1743, #1741, #1735, the brace-header forms, confirmation of the #1643 union spelling, any breaking outcome of the guard decision in AR-2, and the decision on the rejecting remainder of #875 in AR-15). Ship a mechanical rewriter for that batch.
2. Add a 0.32-exit issue, "Declare the 0.30 syntax an edition": no breaking change to existing spellings; additive syntax stays allowed; a per-module edition marker; any later breaking change ships with a `calor fix` rewriter that migrates fixtures, pairs and documentation in the same change. Make it an entry criterion for 0.33, re-affirm it at the end of 0.33 after #1650 and #1651 report, and list the written compatibility promise in #1701's sign-off. Re-scope #1084 to the edition marker and the migrator.
3. Amend the fewest-tokens rule with a minimum win and a named tokenizer set covering the agents in #1651. Amend #1645 so a form whose agent errors trace to C# habit (`var`, `=>`, `set`, `type:name`, `each`) may be respelled toward C# at a stated token cost, using the error counts from #1605.

### AR-13. The recommendations add six workstreams and displace nothing, in a plan constrained by CI time and maintainer decisions

**Severity:** major. **Targets:** R1-R6, PRIO, the 0.31 row.

**The attack.** 0.31 holds 25 open issues, two of them carried over (#1594, still titled "[0.29]", and #1605, still titled "[0.30]"). 0.30 took 23 days, opened with 1,130 failing compiler tests, roughly doubled compiler test time, had its compiler shard cancelled at the 40-minute limit, and ended with the maintainer delegating the finish. The performance ceiling went from 24 to 48 to 41 seconds with about 5% headroom; the index build is still 8 to 15 times its 0.29 cost (#1738); each release flake cost a rerun of a roughly 60-minute job (#1749); the test-host leak behind #1590 has been open since early September.

The ".NET 11 / C# 15" row is not one block. #1643 (the unions R3 asks for) depends on the Roslyn upgrade #1638, and #1705 (the pilot) reuses #1639 to #1641. The toolchain has already moved under the project: the package manager moved `dotnet` to 11.0.100 on 2026-11-16, `calor` is a net10.0 tool with no roll-forward setting, and `src/Calor.Sdk/Sdk/Sdk.targets:8` hard-codes a net10.0 path. The rest is less urgent: #1641 adds parallel CI legs while the shard is at its limit, and #1642 rewrites the argument layer of every command.

Corrections to the reviewers: "four to five milestone-equivalents" is one reviewer's own estimate, and a milestone has lasted anywhere from 1 to 23 days; nobody proposed displacing .NET 11; and a CLI rewrite during #1605's collection is not a confound, because arms are pinned to commits.

**The defence.** Displacement is a planning step the maintainer asked for after the review. PRIO names two items to start, and several recommendations are text changes to existing issues. By the project's own record 24 issues closed in four days, so issue count is not the binding limit.

**Proposed change.**

1. Adopt one rule: every new item names the issue it displaces or states that it is a text-only change.
2. Cap 0.31 at a measure-ready set: #1605 as the exit gate; the harness-wiring issue (AR-1); #1736; the legacy-dialect issue (AR-8); #1734 with #1745 ahead of it; #1738; #1749; #1590; #1694 and the successor-packet issue (AR-6); #1638; a minimal #1639; #1643; the whole-file preservation fix (AR-19) with #1746; the guard census (AR-10); the non-engineering adoption items (AR-9).
3. Move to 0.32: #1741, #1743 and #1735 (into the breaking window); #1742, #1744, #1747, #1748; #1640, #1641 and the command-line library migration split out of #1642. Move #1733 to 0.34. Close or re-home the remaining children of #1594 instead of carrying the epic a third time.
4. Split #1644. Keep in 0.31: #1638, #1643, and a minimal #1639 (framework and SDK pin decision recorded; tool roll-forward decided and tested by running `calor` on a machine with only .NET 11; the Sdk.targets path reviewed; one net11.0 consumer smoke test). Land the command-line migration after #1605's collection ends.

One conflict: a check proposed moving #1745 to 0.32 while another needs it ahead of #1734. Trimming a reference whose examples are not executed risks cutting the measured lever's content, so #1745 should stay with #1734 in whichever milestone holds both.

### AR-14. "Effect soundness" overstates 0.30: known-unsound built-in contracts have no milestone, nothing measures how much code ends up `unknown`, and the teaching text contradicts the manifests

**Severity:** major. **Targets:** P3, R6.

**The attack.** P3's list is accurate item by item. Four things are missing beside it.

- *Known unsoundness.* During 0.30 the maintainer restored built-in contracts that declare culture-sensitive formatting, LINQ operators (which call user delegates), `Object.Equals`/`GetHashCode` and `Nullable<T>.Value` pure, although they "can run code the effect system does not see" (`CHANGELOG.md:144`). The fix is #1731, with no milestone. The history matters: withdrawing those contracts made `n.ToString()` stop compiling, the converter then preserved such files as C#, "which has no effect checking at all", and the change was reverted on 2026-11-14.
- *Coverage.* Preserved C# gets no effect checking, which in the #1746 measurement is every file of two corpus projects. On converted code a row derived from a body cannot contradict that body (#1177).
- *The price of charging unknown code.* Calls into dependency injection, options, MediatR, AutoMapper, FluentValidation, Polly, or any unlisted framework member are charged `unknown` (`CHANGELOG.md:64`). `! unknown` stands alone and forces every caller to declare it (`docs/syntax-reference/effects.md:293-305`); the converter writes it itself; async rows "remain limited". No issue measures how much real code carries an `unknown` row.
- *The teaching text is wrong or the manifests are.* The teaching reference, the language reference and the primer tell agents that `Contains(string)`, `Substring`, trimming, `Enumerable.Where` and `ToArray` "remain unknown under strict checking" and to hand-write loops instead (`docs/syntax-reference/current-language.md:580`, `:1153`). The manifests declare them pure (`src/Calor.Compiler/Manifests/bcl-system.calor-effects.json:42-47`, `:461-520`) and a test asserts no error for them. Which one the compiler follows was not verified by running it; agents read the text.

P3 also says effects are "the one thing" neither C# nor Rust has, while R6 names two things and `docs/design/calor-direction.md:112` lists five.

Correction to a reviewer: the claim that everyday string and LINQ calls are charged `unknown` is contradicted at HEAD by the manifests, the test and #1731.

**The defence.** Each item P3 lists is confirmed. Failing closed on unknown code is the sound design, and restoring the old contracts was a recorded decision with a tracking issue. "Effect soundness" is a section label, not a claim that the system is sound.

**Proposed change.** Add an epic to 0.32, "Effect precision on everyday .NET code".

1. Move #1731 into 0.32; any new effect spelling it needs goes into the breaking window (AR-12).
2. Report an unknown share in CI per corpus project and in every agent-run report (#1605, #1651): functions whose own row is `! unknown`, call sites charged `unknown`, and the most frequent unknown callees.
3. Reconcile the teaching reference, `current-language.md` and the primer with the manifests, and extend #1745 so every "remains unknown" statement is backed by a compile check that fails when it goes stale.
4. Add exact contracts for the most frequent unknown callees found in item 2, starting with dependency injection, options, logging and async members.
5. Reword P3: "effect coverage widened in 0.30; known unsound built-in contracts tracked in #1731".

### AR-15. R3 misreports null safety: enforcement shipped in 0.22, and #875 is now a proof-trust fix

**Severity:** major. **Targets:** R3.

**The attack.** R3 says binder-level null enforcement (#875) is unscheduled, which reads as absent.

- *It shipped.* Since 0.22.0 (2026-09-15) a possibly-null value stops compilation at initialization, native return and resolved method input (Calor0272 to Calor0274, errors by default; `CHANGELOG.md:1871-1876`), and since 0.30 dereferencing a `string?` without a check is a hard error (`CHANGELOG.md:360-363`). The documentation for Calor0272 names #875's reproduction as its canonical case and a 0.30-syntax test pins it. This already goes beyond C#'s advisory nullable warnings. Getting there took a 19-issue milestone after an earlier attempt whose release notes were publicly corrected as overstated.
- *What is documented as missing* is elsewhere: later assignment and rebinding, member writes, constructor inputs and generic payloads are unchecked ("bounded receiving-boundary enforcement, not whole-program null safety", `CHANGELOG.md:1908-1915`), and 0.30 added "nullable oblivious" modules, which the converter emits for nullable-disabled C# and in which the checks are relaxed.
- *What #875 now is.* The verifier demotes every proof that touches its string theory to Assumed and keeps the runtime check, because it cannot trust non-null strings (`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:576-628`; lines 57-70 say lifting this is tracked by #875). #875's own reproduction shows `calor verify` reporting a 100% proven rate on a program that throws. The issue was last updated on 2026-09-10 and is written in the retired syntax. It says the general remainder "will reject code that is fine today" and "needs a migration story".
- *Option and Result* were removed by a decided, closed issue (#1617). R3 cites the removal as a loss; it does not propose re-adding them.

**Conflict between checks.** Three checks place #875 in 0.35, one puts the design and first lift in 0.32, and one wants a decision in the 0.32 window. The evidence fixes the ordering and not the milestone: the lift must precede #1660's string work, which will otherwise report Assumed; the lift is not a breaking change, so it does not need the window; the rejecting remainder is breaking and must be decided inside the window (land it, or defer it to an edition).

**The defence.** The sentence is literally true: #875 is open with no milestone and is titled as binder enforcement. The repository itself says the guarantee is not whole-program. R3 names the right properties and cites the right union issue.

**Proposed change.**

1. Retitle #875 "Lift the string-proof demotion where non-null is established" and rewrite its body in 0.30 syntax. Scope: values whose non-nullness an enforced boundary already establishes (an immutable `let` initialised under Calor0272, a result returned under Calor0273, a parameter with a retained entry null guard); keep the demotion for mutable locals, fields and generic payloads. Done when: the reproduction no longer reports a false Proven or deletes the check; the number of string proofs moved from Assumed to Proven on the corpus is reported; the conversion scorecard (101 of 106 native at 0.30) loses nothing; the vestigial nullable flag in the translator is wired up or deleted; and the disagreement between `Z3Verifier.cs:62-64` and `docs/verification-modeled-forms.md:167` about count and index operations is resolved in writing. Record the scope in 0.32 and make it a stated prerequisite of #1660.
2. Add a small epic for native (non-oblivious) modules with one issue per documented gap. Each carries seeded-defect and safe controls and reports newly rejected corpus files and agent fixtures; acceptance is zero newly rejected files that are not real defects, with a hold branch. Schedule it only if the base rate in AR-16 clears its threshold, and decide in the 0.32 window whether it lands there or rides an edition.
3. Add an issue that counts converted modules carrying the nullable-oblivious header and sets a path to shrink the number.
4. State two non-goals: no null ban without an edition mechanism, and no return of Option and Result.

### AR-16. R3 proposes new guarantees with no base rate for the defects they target; disposal checking has nothing to build on; strictness has a measured cost

**Severity:** major. **Targets:** R3, Z, R5.

**The attack.**

- *No base rate.* The post-mortem of the reverted April checker set process lessons: confirm a validation corpus exists, cite an example from the target corpus, give the zero-finding case an explicit hold, and run a 30-minute base-rate check first (`docs/design/tier1a-postmortem.md:248-256`). R3 meets none. No measurement in the repository says how often agents ship null, missing-case or leaked-resource defects: the July epoch reported zero escaped bugs in any arm across 180 runs, and all six August tasks were injected arithmetic or boundary mutations.
- *Strictness has a recorded cost.* When 0.30 withdrew pure contracts, ordinary code stopped compiling and fell back to preserved C# with no checking (#1731). Relaxing nullability for oblivious modules gained 8 native conversions (`CHANGELOG.md:159-168`).
- *Disposal has no substrate.* The obligation engine has five kinds, none about resources (`src/Calor.Compiler/Verification/Obligations/ObligationTracker.cs:33-49`); none of the six bug-pattern checkers is about resources; the control-flow graph models the dispose step of a block-form `using`, and nothing consumes it. `using var` and `await using` are not converted natively until #1655 (0.34). The strategy defers "full ownership/linear types" to after 1.0 (`docs/plans/agent-native-strategy.md:215-217`). The equipped C# arm in #1709 could be given disposal analyzers.

Corrections to the reviewers: a static must-dispose check does not need ownership types and is more than "a lint"; the block-form `using` is already native, so a check is not strictly blocked until 0.34; R3's tension with R5 is an ordering problem; and the April result that 9 of 9 agents chose Option/Result (old syntax, one model family, nine tasks chosen to be nullability-heavy) is weak grounds for reopening anything.

**Conflict between checks.** One check proposed implementing disposal obligations in 0.34 after #1655; four proposed a design spike at most. With no existing analysis, no base rate, and the post-1.0 deferral, the evidence favours the spike.

**The defence.** R3 is a list of candidate guarantees in answer to a question about Rust, not a ship plan. Its value claim is about agent-authored code, where the assessment already proposes the instrument. For null there is prior corpus measurement from 0.22.

**Proposed change.**

1. Put a base-rate measurement in front of all R3 feature work, inside #1605: tag every failed iteration and every escaped defect, per arm, by class (unknown or removed syntax, including attempts to write Option or Result; type error; null; missing case; resource leak; effect; contract; logic). Register thresholds and a hold branch in advance, and build an R3 feature only for classes that clear the threshold.
2. Adopt a standing acceptance rule for every issue that adds or tightens a hard error, starting with #875, #1643 and #1731: the conversion scorecard's native count and the #1746 density ratio do not regress, and the number of files that newly fall to whole-file preservation is reported.
3. Keep disposal out of every milestone before 0.37 except one spike after #1655: a short design note plus a count of undisposed disposable locals in agent outputs from existing epochs and the #1605 runs, with a go, hold or drop rule. If it says go, implement it as a bug-pattern checker over the existing using-dispose operations, and record that the equipped C# arm gets disposal analyzers.
4. Order any R3 change that rejects currently accepted programs into the 0.32 breaking window, or defer it to an edition (AR-12).

### AR-17. R6 cannot be the task-selection rule for 0.37; it can be one pre-registered stratum

**Severity:** major. **Targets:** R6.

**The attack.** R6 says the 0.37 tasks should be ones where effect rows or proven contracts decide the outcome.

- *It conflicts with the 0.37 design.* #1708 roots tasks in pilot needs and requires one harder task that exposes Calor's limits; #1698 says "Do not handicap C#" and lists reactivating the paused effect-rows research as a non-goal; `CLAUDE.md:406-408` says benchmarks are "not biased towards Calor".
- *The repository has documented why such tasks are hard to build.* The paused effect-rows study got zero escapes on both arms across 28 valid runs, with the intended shape realised in about 20% of first-collection runs. Its analysis found that where behavioural tests catch what the effect system catches, rows are "redundant by construction" at any sample size, and wrote eight admission rules under which "Rejection is the expected outcome for most candidate tasks" (`docs/plans/2026-09-05-ppw-rows-fixture-redesign.md:28-33`, `:51-84`, `:105-137`; #1254). The redesigned pilot halted with no completed trial (#1436), and #1648 (0.32) has yet to decide whether the design survives 0.30.
- *On the seeded-defect set* only 2 of 6 effect-class defects produced a Calor delta; the C# agent caught the other four unaided.
- *On real framework code* many boundaries are charged `unknown` (AR-14), the compiler's suggested fix for an undeclared effect is to add it to the row (`src/Calor.Compiler/Effects/EffectEnforcementPass.cs:1068`), and the approval boundary that stops an agent widening a row is not built until 0.36 (#1702).

What supports R6: the strategy pre-registers an effect-centric task category, as a share alongside C#-favoured and neutral tasks (`docs/plans/agent-native-strategy.md:132`); #1711 already defines seeded boundary-defect controls; and R6's negative advice matches the repository's finding that fixtures up to 16 functions saturate in every arm.

Corrections to the reviewers: R6 does not contradict R1 (one concerns cost, the other benefit); it is not the paused study under a new name (that compared two Calor compilers on small fixtures); and the pre-registered categories do not forbid tasks Calor is expected to win.

**The defence.** R6 names an example task class, not a registered design, and "lead with" can be read as making sure the favourable stratum exists at scale.

**Proposed change.**

1. Rewrite R6: keep "do not use small well-specified functions"; drop "lead with what Rust lacks"; name equipped C# (#1709) as the single gating comparator.
2. Keep #1648 in 0.32 as a decision-only issue and make it a stated predecessor of #1708 and #1711 drafting. Its closing record says whether the registered design survives 0.30 and carries the admission rule forward: a task counts as effect or contract evidence only if a wrong implementation exists that passes the visible tests and is no more work than the honest one.
3. Amend #1708: a pre-registered stratification (effect-and-contract, neutral, C#-favoured) with proportions fixed before collection and results reported per stratum; candidates screened against the admission rule with rejections recorded; one task where effect rows or contracts decide the outcome beside the performance tasks; and a quantified task-card field giving the share of functions on the change path whose row is `unknown` or that sit in preserved C#, with a threshold above which the task is excluded from effect evidence.
4. Get discrimination primarily from #1711's blinded seeded controls, not from choosing favourable tasks.
5. Make #1702 a prerequisite of any 0.37 task counted as effect-boundary evidence. Require such tasks to be native Calor with rows and contracts that predate the change under test, and exclude LINQ, delegate and culture-sensitive paths until #1731 closes, or disclose them as assumed.

### AR-18. Calor's proofs do not reach strings, arrays or user types today, and "Rust has no proven contracts" needs rewording

**Severity:** major. **Targets:** R6, P3.

**The attack.** R6 expects "proven contracts" to decide large changes across I/O boundaries. At HEAD any successful proof that touches the solver's string theory or its nullable-reference, array or user-type sorts is returned as Assumed with the runtime check kept; "Only a signature made entirely of modeled primitives still elides" (`src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:576-628`; `docs/verification-modeled-forms.md:74`, `:83`, `:160-169`). Method calls inside contracts are Unsupported. I/O-boundary code is heavy in strings, collections and objects, so there the verifier contributes checked contracts, not proofs. The string and collection modelling planned for 0.35 (#1660) will report Assumed, not the Proven its done-when asks for, until the demotions are lifted.

On Rust: stable Rust has no built-in contracts, but nightly has experimental contract attributes and external verifiers exist (Kani, Verus, Creusot, Prusti, Flux). Calor's difference is integration and default-on enforcement in one build.

Corrections to the reviewers: neither the README nor the website carries the Rust claim (only an unreferenced component, `website/src/components/landing/CompetitivePositioning.tsx`), so there is nothing to reword there; and a proof inventory on the pilot component cannot be a 0.35 acceptance item, because the pilot is chosen in 0.36.

**The defence.** R6 plainly means the language as agents use it through the default toolchain, where Rust has no contracts. The effect half of R6 is undisputed and is the half that matters at I/O boundaries. Runtime-checked contracts that are on by default are still more than C# gives.

**Proposed change.**

1. In the plan, replace "Rust has no effect rows or proven contracts" with "effects and contracts enforced by default in one build, against bolt-on analyzers and verifiers elsewhere".
2. In #1709, define the equipped C# arm as the bolt-on analogue and state that a Rust-plus-verifier arm is out of scope.
3. Make a proof-status inventory the opening deliverable of 0.35 (#1662), run on the existing conversion corpus: counts of Proven, Assumed by named assumption, Unsupported and runtime-only. Prioritise #1660 and #1661 by what moves Assumed to Proven. Repeat the inventory on the pilot component under #1703 in 0.36 and size the 0.37 claim to it.
4. Record in #1660 and #1662 that string and collection proofs stay Assumed while the demotions stand, with the #875 lift (AR-15) as a prerequisite.

### AR-19. P2's numbers are right, but two of three corpus projects contain no native Calor and the density issue is scheduled before its fix

**Severity:** major. **Targets:** P2.

**The attack.** The figures match #1746 (overall 1.016x; native subset 0.859x) and the assessment kept the tokenizer and single-measurement caveats. It dropped the ones that change the reading.

- In that measurement MediatR and Serilog were entirely whole-file preserved C# because of a compile error on preserved originals (CS8669; `CHANGELOG.md:120-124`). The 0.859x comes from FluentValidation alone, and 149,211 of 291,307 measured tokens (51%) are preserved C#.
- Natively converted files are the easier half by the repository's own account (`docs/plans/wedge-real-scale-closeout.md:119-125`), so 0.859x cannot be projected onto the rest.
- The native output carries no contracts, so contract work would add tokens to this number.
- The CI report meant to track the ratio shows zero native files by construction.
- The dependency is inverted: #1746 is in 0.31 and names #1647 as the fix, #1647 is in 0.32, and #1647's body does not mention CS8669.

As accounting for this one sample, "the gap is preservation" is correct and is the tracker's own attribution; one reviewer's claim that the conclusion is unsupported overreaches.

**The defence.** P2 states the number, its source and two caveats, draws the tracker's own conclusion, and claims no density advantage.

**Proposed change.**

1. Add a named issue for the CS8669 whole-file regression in 0.31, done when the MediatR and Serilog native fractions are back at the start-of-0.30 baseline. Move the rest of #1647 into 0.31 with it if capacity allows; otherwise move #1746's ratio target to 0.32.
2. Change #1746's done-when to report, per project and with Anthropic's `count_tokens`: native fraction, ratio on native files, ratio on preserved files, and a "with contracts" variant. Require buildable corpus copies in CI so the report cannot show zero native files by construction.

### AR-20. The adopter path has a hole: default migration can leave an agent with no Calor to read and contradictory instructions

**Severity:** major. **Targets:** omission; R2.

**The attack.** The strategy's adoption workflow is per-module migration, the one place the measured penalty was 1.0x, because existing Calor serves as the in-context reference. At 0.30 that workflow breaks on the default path: for two of three corpus projects the agent gets whole-file preserved C# (AR-19). The `calor init` template tells it to convert C# and "then edit the `.calr` file" (`src/Calor.Compiler/Resources/Templates/CLAUDE.md.template:10-11`), the teaching reference says "Do not handwrite raw C# passthrough", and no document under `docs/cli` or the website guides says what to do with a preserved file. The public playbook meanwhile tells adopters there is no compatibility promise, one maintainer and unmeasured savings.

One reviewer's broader claim, that the assessment ignores adoption, is overstated: it mentions the pilot, modification parity, preservation and conversion. Funding adoption work also cuts against a recorded maintainer decision, so that part is the maintainer's choice (AR-9).

**The defence.** The maintainer asked how to improve the benefit for coding agents, not how to find users, and R4 and P2 already address the migration path.

**Proposed change.**

1. Add a 0.31 issue: define and test what an agent does with a preserved file (edit the original C# and re-convert, or an MCP tool that rewrites the raw payload), and make the template rule and the teaching reference agree.
2. Once the CS8669 fix lands, add modify-a-converted-module tasks on a migrated corpus project to #1605's pair set.
3. Carry the recruitment decision and the compatibility promise as in AR-9 and AR-12.

### AR-21. Premise Z names the toolchain as a lever and no recommendation touches it; agents cannot add tests without breaking the rules `calor init` gives them

**Severity:** major. **Targets:** Z, omission.

**The attack.** The uncredited strengths are real: one `calor check` call that builds, tests and verifies and returns failures only, `calor run` and `calor test`, source maps into generated C#, an MCP write path, and an index with effect-row impact queries. The gaps are ones a Rust user would not accept.

- *Tests cannot be written through the sanctioned path.* `calor check` and `calor test` discover only top-level `tests/*.cs` xUnit files (`src/Calor.Compiler/Commands/TestCommand.cs:399-418`), and `calor check` exits 3 when there are none. The template's first rule says never create new `.cs` files, and the write hook blocks every `.cs` path except generated ones, with no exemption for `tests/` (`src/Calor.Compiler/Commands/HookCommand.cs:277-312`). The strategy planned a test construct (`docs/plans/agent-native-strategy.md:145`, `:183`); none exists and no open issue mentions test authoring.
- *Feedback speed is unmeasured on 0.30.* The index build went from 0.38 s to about 10 s and is still 8 to 15 times its 0.29 cost after a first fix (#1738, whose title still says 26x). The 2 ms figure for the MCP write path predates 0.30. No warm `calor check` latency exists for the 0.30 compiler.
- *Second-stage errors.* Calor emits C# and then runs the C# compiler, so C# errors can reach the agent. The gates document defines a rate for this (`docs/plans/agent-native-gates.md:42`); the harness does not compute it.

*Editor's check on a conflict.* One check said any edit recompiles every Calor file in `dotnet build` and that the fix, #1732, has no milestone. Another said #1732 is recorded as done but still open. `CHANGELOG.md:188`, in the 0.30 "Fixed" list, says `dotnet build` is incremental again and cites #1732; the triage document agrees (line 416). The older statement at `CHANGELOG.md:354-359`, in the same release notes, is stale. The fix shipped. What remains is narrower and has no issue: a caller still recompiles on any edit anywhere, and a derived class on any edit to its base (triage document, lines 374-380).

Correction to a reviewer: "Calor cannot have a single-compiler loop" overstates the problem, since `calor check` gives one command.

**The defence.** Z is a framing statement, not a claim that the toolchain is deficient. In the documented adoption model tests live in the existing C# test project and are edited, not created. The index and suite numbers measure CI cost; nothing shows the agent-visible loop got slower.

**Proposed change.** Add an epic across 0.32 and 0.33, "One toolchain: write, test, check in Calor".

1. 0.32: let agents write tests. Minimum: exempt `tests/` in the write hook and say so in the four init templates and the primer. Target: `calor test` and `calor check` discover tests written in `.calr`, with one teaching example.
2. Verify and close #1732, correct `CHANGELOG.md:354-359`, and file the remaining reuse tier as its own 0.32 issue.
3. 0.32: measure warm `calor check` and MCP write latency on the 0.30 compiler, and add a CI budget for edit-to-diagnostic time on the MSBuild path.
4. Keep #1738 in 0.31 until its target (within 2x of 0.29) is met, and update its title and numbers.
5. Add a second-stage C# error rate to the #1605 report, computed from run transcripts.
6. 0.33: include the test-authoring path in #1650's per-agent init tests.

### AR-22. A published corpus cannot affect any result on this roadmap; its near-term use is as in-context examples, and converted output is the wrong source

**Severity:** major. **Targets:** R5, Z.

**The attack.**

- *Horizon.* Ten minor releases shipped in the 72 days from 0.20.0 to 0.30, so 0.31 to 0.39 is a matter of months, shorter than a model training cycle. The strategy already concedes the prior: "~zero Calor; Calor cannot win on familiarity" (`docs/plans/agent-native-strategy.md:80`).
- *Scale.* The available material is 291,307 tokens of converted open-source code (half preserved C#), 970 current-syntax `.calr` files in the whole repository (31,503 lines), 11 sample files, and 87 compiler-checked teaching programs. The only large body the project ever produced was April's 262,142 machine-converted files in the retired syntax.
- *Converted output is a poor source.* It carries converter artefacts (#1742), has passed the canonical-usage lint on one project only (#1747), and has no contracts. The three projects are Apache-2.0 at the pinned tags, so translations are republishable, but with licence text, copyright, NOTICE content and a modification notice, and the converter carries only XML documentation comments, not header comments (`src/Calor.Compiler/Migration/CSharpToCalorConverter.cs:130-131`).
- *Hand-written material is not uniform.* 27 of 31 function headers in `samples/` write an explicit `public` that the teaching reference never uses, and no lint flags it.

Corrections to the reviewers: R5 does not contradict Z (Z says "not soon", R5 names the long-run route); R5 never said the corpus should come from converter output; "not freely republishable" should read "republishable with notice obligations"; and the external studies cited for these points could not be checked.

**The defence.** R5's claim is about the long run and is true; the finding offers no other route to a training prior. Publishing canonical Calor is nearly free, and every month without it delays any prior.

**Conflict between checks.** One check places publication in 0.33 after the freeze; another no earlier than the end of 0.34, on the ground that a corpus that is 60% to 70% preserved C# teaches little. Nothing gates on it either way; publish after the edition is declared, when the example bank is large enough to be worth it.

**Proposed change.**

1. Make no acceptance criterion in the plan depend on a training prior.
2. Re-scope R5's corpus as an in-context example bank: grow the compiler-checked teaching programs by construct and task shape, execute each in CI (#1745), serve them through `calor_help`, seed `calor init` starters from them, and measure by first-build pass rate and cost per task in the #1605 arms.
3. Admit only original Calor under the project's own licence that passes the formatter, the canonical-usage lint and a run-in-CI check. Keep converted open-source code unpublished; if that changes, require a per-file licence and notice manifest and a converter test that header comments survive.
4. Add a small issue to decide whether a redundant `public` is canonical, enforce the answer, and bring `samples/` and the website into line.
5. Record the training prior as a long-run by-product with one hygiene rule: public `.calr` outside the legacy allowlist (AR-8) is in the current syntax and canonical form.

### AR-23. Premise Z's "agents do well in Rust" is an unsupported hypothesis

**Severity:** major. **Targets:** Z, M2.

**The attack.** Z turns the maintainer's statement about popularity into a claim about success, gives three causes, and cites nothing. At the level of search summaries the published record is mixed: one multilingual benchmark ranks Rust first of nine languages on about 43 of 300 tasks and has no C#; another reports Rust mostly in single digits; a Rust-specific benchmark's best baseline resolves 21.2% of 500 tasks; a C# benchmark reports about 40% against about 70% for Python in the same setups. No source found runs Rust and C# in the same agent harness. Three figures offered by one reviewer could not be confirmed and should not be cited. Z's corpus reason also predicts C# at or above Rust, so it cannot explain a Rust lead.

In the repository Rust is never a measured comparator, and `docs/plans/agent-native-strategy.md:16` says "the competitor is not C#-the-language, it is agent-generated evidence in C#".

**The defence.** The Rust bar is the maintainer's own framing, and the assessment answered the question it was asked. R1 was its proposed test of the premise. The literature cannot refute the maintainer either, and R2, R4 and R5 do not depend on Rust at all.

**Proposed change.**

1. Reword Z as a hypothesis: "Rust is the maintainer's reference bar; published agent results are mixed and no same-harness Rust-versus-C# result exists." Rename the path from "close the gap with Rust" to "reach equipped-C# parity on authoring cost, then show a benefit". Keep Rust named, with the evidence that would confirm or retire it.
2. Record M2 as an open question. Conventional C# and equipped C# stay the only gating comparators through 0.37.
3. Add one documentation-only issue to 0.31, with no paid runs: "External anchor: published per-language agent results and what they do not show", written from the primary papers and omitting any number not read at source.
4. Any Rust datapoint is the conditional, non-gating item in AR-1.

### AR-24. The safe-patch share R2 wants to track is about 2%, cannot be computed from archived runs, and is silent where the remaining cost sits

**Severity:** major. **Targets:** R2.

**The attack.** On the repository's loop telemetry 4 of 189 agent-hit diagnostics carried a safe patch (4 of 81 outside the two effect-probe epochs). The figure cannot be recomputed from archived runs, because the telemetry records only a code and a declaration id per diagnostic (`bench/phase0-agent-native/loop-telemetry-schema.md:32`). Safe patches exist at five sites, all lexical, formatting or lint (`Parsing/Parser.cs:621`, `Parsing/Lexer.cs:398` and `:407`, `Formatting/CalorFormatter.cs:441` and `:481`), out of 21 fix sites and 248 diagnostic codes.

Outside the probes, agents hit parser errors (67 of 81), which have no deterministic fix. Inside them they hit the undeclared-effect error Calor0410 (111 of 189), whose fix is deliberately not marked safe; marking it safe would mean widening effect rows automatically, which undoes P3. The metric is also blind where cost remains: the journal pair with the exemplar was 30 of 30 first-try green and still cost 2.3 times C#. All of this telemetry predates 0.30.

Corrections to the reviewer: "near zero by construction" and "can only be raised by weakening effect checking" are too strong; the share could rise through unambiguous syntactic rewrites.

**The defence.** R2 says "track", not "target". The low coverage is the very gap with the Rust compiler that Z points at, and 0.30's wrong-form errors already suggest the C# word, a class where a safe patch is sound.

**Proposed change.**

1. Do not make the safe-patch share a target. Per #1605 arm, track cost and output tokens to green against C#, first-build pass rate, turns before the first build, and the repeat rate of each diagnostic code within a run.
2. Add `hasFix` and `isSafeToApply` to the loop-telemetry diagnostic entries (the diagnostic envelope already exposes the flag) and report fix coverage of agent-hit diagnostics as a descriptive number, with today's five safe sites as the baseline.
3. Write into the plan, and pin with a test, that effect-row and contract changes are never marked safe to apply.
4. After the 0.30 re-baseline, give the three most frequent agent-hit codes a fix, marked safe only where the rewrite is syntactic and unambiguous. Add to #1645's done-when: every wrong-form error without a fix gets one or a recorded reason.

### AR-25. Most of R2, R3 and R6 is already filed or shipped; the rework should amend issues, not open epics

**Severity:** major (one check; a second rated the same point minor). **Targets:** R2, R3, R6.

**The attack.** R2's safe-patch theme is closed 0.30 work (#1613, #1614) plus #1734, #1743 and #1645, whose second step already requires reading agent error logs and classing each error. R3's unions are #1643 (p1, 0.31), its null enforcement shipped in 0.22 (AR-15), and the Option/Result removal was a decided issue (#1617); only disposal has no issue. R6 is partly covered: #1711's boundary-defect controls include external calls, nullability and passthrough, while #1708's task classes are performance-oriented, so R6 is an amendment to #1708.

In fairness the assessment cites #1734, #875 and #1643 itself. Its faults are the description of #875 and the missing pointers for R6.

**The defence.** These are recommendations about emphasis, not claims of novelty. Being already filed does not make a priority wrong, and the assessment never proposed opening new epics.

**Proposed change.** Add no R2 epic and no R3 epic. Carry the new content as amendments: the report columns and telemetry field in AR-24 on #1605 and #1645; the #875 rewrite in AR-15; the #1708 amendment in AR-17; the disposal spike in AR-16; and leave #1711 as it is.

### AR-26. The milestone table repeats a tracker that is stale against 0.30, and eleven open issues have no milestone

**Severity:** minor. **Targets:** the milestone table, P2.

**The attack.** The table is a faithful summary of the tracker; the staleness is the tracker's, and the reworked plan will be built from the same issues.

- The 0.34 row says "records, directives, lock, using". At HEAD record classes and the `lock` statement are already fully supported (`src/Calor.Compiler/Migration/FeatureSupport.cs:60-66`, `:621-626`), while #1654 still says "Today record is NotSupported" and #1655 still lists `lock`. The registry has 58 unsupported entries against the epic's stated baseline of 48 (#1659).
- The 0.32 row's "confirm spellings with the real tokenizer" is mostly done (AR-12).
- The 0.31 row omits that #1643 is already there.
- #1555 concerns a retired form that is now a lexer error. #1573 is closed but still in #1649's table. #1643's title and the bodies of #1661 and #1662 use retired markers. #1728 asks for release-bundle promotion that `docs/release-policy.md` documents as implemented. #1738's title says 26x.
- Eleven open issues have no milestone: #673, #851, #875, #922, #1084, #1670, #1694, #1723, #1726, #1731, #1732.

**The defence.** The table is headed "as read from the issue tracker", and none of the stale items changes a claim in the assessment.

**Proposed change.** Make the first epic of the reworked plan a short tracker re-baseline against 0.30: close #1555; drop #1573 from #1649's table; rewrite #1654 to record structs and the record shapes still preserved; rewrite #1655 to using declarations, `await using`, standalone blocks and the checked-block registry entry; restate #1659's baseline at 58; retitle #1643 and update #1661 and #1662 to current syntax; rewrite #1646 to what is still on the proxy tokenizer; resolve #1728 to "activate the implemented path and time one release" or close it; update #1738. Triage all eleven unmilestoned issues in one pass: #1694 to 0.31 and #1731 to 0.32 (AR-6, AR-14); #875 per AR-15; verify and close #1732 (AR-21); close #673 as #1653 and #1652 propose; close the first item of #1084 as done and re-scope the rest (AR-12); give #1726 to the CI-health epic; decide #851 and #1723 against the retired venue and the consumers epic (AR-10); re-test #1670 on 0.30 syntax or close it; give #922 a milestone as tooling backlog.

### AR-27. Exhaustive unions are already scheduled and match C# 15; in 0.30 exhaustiveness exists only for `bool`

**Severity:** minor. **Targets:** R3.

**The attack.** #1643 is p1 in 0.31, depends on the Roslyn upgrade #1638, defines exhaustiveness as "matching C# 15's compiler-checked exhaustive `switch`", and must pick one new spelling. It is parity with the C# arm, not an advantage over it, and R3 adds nothing new by listing it.

One reviewer claimed exhaustive matching "already exists". That is wrong and the correction favours the assessment: no parser path builds a union declaration, the emitter says "UnionTypeDefinitionNode has no 0.30 source form" (`src/Calor.Compiler/Migration/CalorEmitter.cs:2838-2839`), and the pattern checker analyses only `bool` and treats every other type, enums included, as exhaustive (`src/Calor.Compiler/Analysis/PatternChecker.cs:258-263`). So for 0.30 users the guarantee is missing, as R3 implies.

**The defence.** R3 cites #1643 by number, and parity with C# 15 on exhaustiveness is a precondition for any claim against Rust.

**Proposed change.** Keep #1643 where it is and describe it as C# 15 parity. Add the union-spelling decision as a token-measured sub-issue completed before the freeze, and fix the title and body to the current `match` form. Optionally, and only if the "missing case" class clears its base-rate threshold (AR-16), add one small issue for exhaustiveness over enum matches.

## Attacks that failed

Each of these was raised by a reviewer and did not survive checking.

1. "Three adjudicated hits contradict H1." Two of the three compare Calor with Calor and say nothing about an advantage over another language.
2. "The green-field penalty is largely closed." This rests on five runs per pair on a different model with no concurrent C# arm.
3. "The cost driver is context size, not language." Unsupported; the epoch's own verdict attributes the cost to thin training prior.
4. "The project retired iterations-to-green." It made the metric observational for one tooling comparison; #1605 still names it.
5. "The remaining CSV-pair cost was a typo in the exemplar." The record says agents copied a correct array-typed line into list-typed signatures.
6. "P5 hides cost." P5 states the dollar range.
7. "A Rust arm would not be informative." It would show whether Rust also sits at the C# floor on cost; it cannot show a Calor benefit.
8. "Run the Rust comparison on the real-scale revert tasks." That venue was retired with zero eligible revert tasks, and the tasks are C# libraries.
9. "18 of 31 pairs are ineligible, so R1 has no tasks." That flag concerns the escaped-bug gate, not cost metrics.
10. "Claude is the agent most favourable to Calor because spellings were chosen on its tokenizer." An inference no run supports.
11. "Calor's CLI, MCP server and fix-carrying diagnostics were in the July loop and did not help." They were not in the measured loop.
12. "No benchmark pair exists in 0.30 syntax; the pairs must be translated by hand." Translated copies of 25 starters and 25 references already exist and the references are compiled in CI; the harness is simply not wired to them.
13. "R6 contradicts R1." R1 measures authoring cost and R6 concerns where to look for a benefit.
14. "R6 is the paused effect-rows study under a new name." That study compared two Calor compilers on small fixtures.
15. "The pre-registered categories forbid tasks Calor should win." They require such a stratum; they forbid choosing it after the fact or dropping the others.
16. "R3 says #1643 is unscheduled." R3 cites it; it is in 0.31.
17. "R3 proposes re-adding Option and Result." It cites their removal as a loss and proposes other remedies.
18. "R3 contradicts R5." It is an ordering constraint that sequencing solves.
19. "Disposal checking needs ownership types" and "a language that compiles to C# can at best add a lint." A static must-dispose check is a property of Calor's own checker.
20. "Null safety and exhaustive matching are not what distinguishes Rust." Overreach; both are widely cited parts of its reputation.
21. "Exhaustive matching already exists for sum types." In 0.30 it exists only for `bool`.
22. "Lifted contracts are as empty as body-derived effect rows." A lifted precondition is checked in other bodies; it is circular only for a defect seeded in the guard itself.
23. "Nothing consumes a precondition on a class method" and "the control-flow checkers import no preconditions." Non-zero facts are imported and index-bounds checks use refinement facts.
24. "Well over 90% of guards are null guards." Counted on the Calor compiler's own source, not on the corpus.
25. "R4 cannot start now." A census and a decision record need no compiler change.
26. "R4 buys no measurement." The assessment justified R4 by verification value, not by a measurement.
27. "P2's conclusion is unsupported." The attribution to preservation holds for the measured sample.
28. "Everyday string and LINQ calls are charged unknown." The manifests declare them pure at HEAD.
29. "#1694 is an effect-system failure." It is the contract-verifier sweep.
30. "There is no working regression net." The ordinary compiler suites run; the dedicated false-proof detector is what is missing.
31. "P3 and R3 rest on the soundness sweep." The sweep tests contract proofs only.
32. "The project twice failed to secure an adopter" and "failed across nine releases." Outreach was never authorised; the record says the activity is unknown.
33. "The 0.20 three-arm study was triaged as infeasible." The record says it is not proof of infeasibility, and 0.37 dropped the fixed margin that drove the arithmetic.
34. "Milestones 0.34 to 0.36 are the three August remedies in order." A loose mapping that ignores the venue's retirement.
35. "The assessment wants #1712 pulled forward." It does not.
36. "R5 says to freeze now." R5 gives no date and is not in PRIO.
37. "#1643 conflicts with a freeze." It adds a spelling; a freeze of existing spellings allows that.
38. "0.33 re-decides spellings per agent." #1650 budgets instruction text per tokenizer.
39. "R5 contradicts Z and P1." Z says not soon; R5 names the long-run route.
40. "Converted corpus code is not freely republishable." It is republishable with notice obligations.
41. "The safe-patch share is near zero by construction and can only rise by weakening effect checking." Syntactic rewrites can raise it soundly.
42. "R1-R6 add four to five milestone-equivalents." The reviewer's own estimate, in a unit that has lasted 1 to 23 days.
43. "0.31 holds more issues than any milestone closed since October." 25 against 24, and that one closed in four days.
44. "PRIO would displace .NET 11." Nobody proposed that.
45. "A CLI rewrite during #1605's collection confounds it." Arms are pinned to commits.
46. "Calor cannot have a single-compiler loop." `calor check` is one command.
47. "The incremental-build fix (#1732) is missing." It shipped in 0.30; the issue is merely still open.
48. "#1084 shows the version check is unwired." The check is called at HEAD; the issue body is stale.
49. "The direction document says not to chase cost metrics." That is an April statement about static token counts, superseded by 0.30's own rule.
50. "C# finished 94% of runs at the floor." That figure describes Calor-arm runs on modification pairs.
51. "Reword the README and website on Rust contracts." Neither carries the claim.
52. "The 30K-55K per-run saving prices #1734." It prices always-on text; the tracker gives no per-run price for #1734.
53. "0.30 moved the primer behind a pointer." It has been an MCP resource since 0.6.x.
54. "The assessment ignores adoption." It mentions the pilot, modification parity, preservation and conversion.
55. "The legacy files teach the wrong language and are a cheap fix." The teaching surfaces are clean, and many legacy files are pinned evidence.
56. Three external benchmark figures (a per-language triple, one multilingual leaderboard row, one translation-benchmark percentage) could not be confirmed and are not used.

## Assessment claims upheld

- **H1 (working conclusion).** No outcome-level advantage against a fair comparator has been shown; the results page, the parked benefit gate and #1698 all say so.
- **P1 (first sentence).** 0.30 did replace the markers with lowercase keywords, indentation and plain C# expressions, and the spellings were counted with Anthropic's tokenizer.
- **P2 (numbers and attribution for the sample).** 1.016x and 0.859x match #1746, and the gap in that sample is whole-file preservation.
- **P3 (the list).** Every construct listed is charged as of 0.30.
- **P4 (three of its facts).** The July authored-fixture benefit gate was zero-versus-zero; 0.30 published no benchmark results; #1605 slipped to 0.31.
- **P5 (parity on modification, and provenance).** Modifying existing Calor was at 1.0x, and 2.7x is the repository's own banked figure, reproduced in direction at 30 runs.
- **P6.** 0.30 was a no-compatibility rewrite with no shipped migrator.
- **Milestone table.** A faithful summary of the tracker as filed.
- **M1 (the count).** 0.37 is the seventh open milestone.
- **M2.** No open issue names Rust as a comparator; the only mention is #1719, as an emission target.
- **Z (first half).** A training corpus is out of reach on this horizon.
- **R2 (direction).** The green-field penalty is the only measured Calor-versus-C# loss, and larger in cost than in iterations.
- **R3 (two facts).** #875 is open with no milestone, and 0.30 has no exhaustiveness guarantee for unions.
- **R4 (premise).** The converter emits no preconditions or postconditions, and the changelog says so.
- **R5 (substance).** Stop breaking the syntax; the strategy document made the same commitment, and the repository shows the cost of the 0.30 rewrite.
- **R6 (negative advice).** Small well-specified functions saturate in every arm and cannot discriminate.
