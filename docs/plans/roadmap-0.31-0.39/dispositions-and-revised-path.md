# Response to the adversarial review, and the revised path

Written 2026-11-23 against Calor HEAD `bc80daeb` (v0.30). Companion to `adversarial-review.md` (findings AR-1 to AR-27) and `assessment-under-review.md` (my original claims H1, P1-P6, M1-M2, Z, R1-R6, PRIO).

## What the review changed

The review upheld the conclusion (no decisive evidence of an agent advantage yet, and the decisive test is far away) and overturned most of how I proposed to act on it.

- **Both things I said to start with were wrong as written.** A Rust arm cannot be added to #1605: the harness accepts only `calor` and `csharp` arms, grades through .NET, and cannot yet run a 0.30 Calor arm at all. Lifting `throw` guards into refinements changes which exception a program throws, and the converter's default mode and round-trip tests exist to reject exactly that.
- **I misstated the evidence.** One Calor-versus-C# result was adjudicated a hit (9 of 9 seeded defects removed against 4 of 9, July 2026, old syntax, against C# with no executable specification). The real-scale venue was adjudicated a miss and retired. The 2.7x figure came from three runs per arm; the replicate measured 2.38x, and 1.62x with a syntax exemplar in the prompt. Nothing has been measured with an agent on the 0.30 language.
- **I left out the only lever the repository has measured.** A 60-line exemplar in the prompt removed 55% of the green-field penalty. As shipped, `calor init` gives agents a pointer to it and nobody has measured whether they follow the pointer.
- **I missed what actually gates the plan.** The soundness sweep is failing, unscheduled and written in the retired syntax. Milestones 0.36 to 0.39 depend on an external maintainer nobody has been authorised to recruit. Agents cannot add tests without breaking the rules `calor init` gives them. I added six workstreams and removed none.

## Dispositions

"Accept" means the finding and its proposed change go into the plan as written. Where I depart, the reason is in the last column.

| Finding | Disposition | What I do with it |
|---|---|---|
| AR-1 Rust arm cannot go into #1605 | Accept | No Rust work in 0.31. #1605 is repaired and becomes the 0.31 exit gate. A blocking issue wires the harness to the 0.30 pair copies. The Rust reference moves to 0.33 behind an arm abstraction and a language-neutral oracle. I take the "cost distance only" branch up front, so the run is planned and is not conditional on finding tasks off the C# floor. The Rust bar is also tracked as a capability scorecard that needs no benchmark. |
| AR-2 Guard lifting changes behaviour | Accept | Removed from the priorities and from the default conversion path. One design-decision issue (0.34, after a census; none of its options changes an existing spelling, so it does not need the 0.32 window); preferred form is verifier-only facts from dominating guards. A round-trip leg compiled with default contract mode and effects enforced is added in 0.32 so contract and effect regressions become visible. |
| AR-3 The measured lever is missing from R2 | Accept | Exemplar delivery becomes the centre of the 0.31 measurement: three delivery variants and two `calor init` arms inside #1605, with the decision rule fixed before the run. |
| AR-4 "Every attempt ended undecided" is false | Accept | H1 and P4 restated (below). An evidence ledger and a README correction go into 0.31. 0.37 is described as confirmation under fairer controls. |
| AR-5 Seeded-defect replication with equipped C# | Accept | New 0.32 epic, after #1605 publishes. It is the cheapest informative benefit test before 0.37 and a rehearsal for #1709 and #1711. |
| AR-6 Soundness sweep failing and stale | Accept | #1694 goes to 0.31 as p1 with a successor packet in 0.30 syntax. "Sweep green for a stated number of scheduled runs" becomes an entry criterion for 0.35 and for any release note that says "proven". |
| AR-7 Cost figures stale and conflated | Accept | P5 restated. #1605 is the re-measurement gate for all green-field work, with a close rule on both iterations and cost stated before the first run. |
| AR-8 Retired syntax in the tree | Accept | A legacy-dialect issue blocks #1605: classify, allowlist, self-check, benchmark workspaces outside the checkout. |
| AR-9 The long pole is the external maintainer | Accept, with one limit | The outreach authorisation is the maintainer's decision, so it is filed as a decision issue with both branches written, not assumed. A dated checkpoint at the close of 0.33 applies a fallback written in advance. |
| AR-10 A lifted contract needs a consumer | Accept | A read-only guard census in 0.34 on the corpus job. A "contract consumers" epic in 0.35 ahead of any converter lift. Any lift is conditional, opt-in, and the last row of that epic. |
| AR-11 R4's history was wrong | Accept | Justification reworded. Overlay decision recorded in 0.34 together with #1177 and the venue feasibility gate. |
| AR-12 Freeze at the end of 0.32 | Accept | 0.32 becomes the last breaking window, with one repository rewrite and a mechanical rewriter, and ends by declaring the 0.30 syntax an edition. |
| AR-13 Six workstreams added, none displaced | Accept, tightened | I adopt the displacement rule and record it per row. I cut 0.31 and 0.32 further than the review proposed: the union spelling is decided in the 0.32 window but #1643 is implemented in 0.34 with the rest of .NET 11; #1734, #1746 and the Calor0006 guidance move to 0.32 so they land after the measurement they would otherwise change; #1744, #1748 and #1747 go to 0.33 and 0.34 because they are on no exit chain. Rows off a milestone's exit chain are marked "may slip". |
| AR-14 Effect precision | Accept | New 0.32 epic: #1731, an unknown-share report, teaching text reconciled with the manifests, exact contracts for the most frequent unknown callees. |
| AR-15 Null safety misreported | Accept | #875 is rewritten as "lift the string-proof demotion where non-null is established" and becomes a prerequisite of #1660 in 0.35. The rejecting remainder is decided inside the 0.32 window. No null ban without an edition. No return of Option and Result. |
| AR-16 No base rate for new guarantees | Accept | #1605 tags every failed iteration and escaped defect by class. New guarantees are built only for classes that clear a threshold registered in advance. Disposal is one spike after #1655. A standing acceptance rule applies to every new hard error. |
| AR-17 R6 as task selection | Accept | R6 becomes one pre-registered stratum in #1708. Equipped C# is the single gating comparator. Discrimination comes from #1711's blinded controls. |
| AR-18 Proofs do not reach strings, arrays, user types | Accept | 0.35 opens with a proof-status inventory. The Rust comparison is reworded to "enforced by default in one build". |
| AR-19 Density caveats and the inverted dependency | Accept | A named 0.31 issue fixes the CS8669 whole-file collapse in project-mode migration, measured by a new corpus report job. #1746 is re-scoped to per-project reporting (0.32); the below-1.0x target moves to 0.34 behind #1647 and the coverage work. |
| AR-20 Preserved files leave agents without instructions | Accept | 0.32 issue (after the measurement, because it changes template text that is at its token budget): define and test what an agent does with a preserved file, and make the template and the teaching reference agree. |
| AR-21 Toolchain: agents cannot add tests | Accept | "One toolchain" epic across 0.32 and 0.33. Test authoring first. #1605 additionally reports how often an agent tried to create a test file and was blocked. |
| AR-22 Corpus cannot affect this roadmap | Accept | The corpus is re-scoped as an in-context example bank. No acceptance criterion depends on a training prior. |
| AR-23 "Agents do well in Rust" is unsupported | Accept | Premise reworded as a hypothesis. A documentation-only external anchor issue goes into 0.33, ahead of the Rust reading that first uses it. Rust stays named as the maintainer's reference bar. |
| AR-24 Safe-patch share is the wrong target | Accept | Not a target. Fix coverage is reported descriptively; effect-row and contract changes are pinned as never safe to apply. |
| AR-25 Amend issues instead of opening epics | Accept in part | No new epic for R2 or R3: that content becomes amendments to #1605, #1645, #875, #1708. New epics are opened only where the work is new (instruments, exemplar delivery, breaking window and edition, effect precision, seeded replication, toolchain, contract consumers). The maintainer asked for full epics and issues per milestone, so every milestone still lists all of its epics, including the existing ones, with the amendment text. |
| AR-26 Tracker stale against 0.30 | Accept | The first 0.31 epic is a tracker re-baseline, including a triage of the eleven issues with no milestone. |
| AR-27 Unions are C# 15 parity | Accept | #1643 is described as parity. Its spelling decision is a token-measured sub-issue inside the breaking window. Enum exhaustiveness is conditional on the base rate. |

## The assessment, restated

- **H1.** A catch-rate advantage was shown once, at fixture scale, on nine maintainer-seeded defects against conventional C# (July 2026, pre-0.30 syntax). It has not been shown against C# with executable specifications, on natural defects, at real scale, on independently authored tasks, or on 0.30. The roadmap's decisive comparison (0.37) is gated on an external pilot with no secured maintainer.
- **P1.** 0.30 replaced the markers with lowercase keywords, indentation and plain C# expressions. Spellings were chosen by fewest tokens, with the C# spelling as a tie-break only, so some forms are not C# (`func`, `each`, `set a[k] v`, `type:name`) and two look like C# and mean something else (`var`, `=>` in a header). No agent has been measured on them.
- **P2.** Migrated Calor is 1.016x C# tokens overall and 0.859x on native files, but that sample's native files are FluentValidation alone; MediatR and Serilog came out entirely as preserved C# because of one regression (CS8669). Native files are the easier half and carry no contracts.
- **P3.** Effect coverage widened in 0.30 (unknown framework code, indexers, dotted chains, static initialisers, pattern guards). Known-unsound built-in contracts are tracked in #1731 with no milestone, preserved C# gets no effect checking, and nothing measures how much real code ends up `unknown`.
- **P4.** Adjudicated outcomes: the seeded-defect hit (above); the real-scale miss and venue retirement (August 2026; that arm never invoked the Calor compiler); the benefit gate parked as unfalsifiable at authorable scale; and two Calor-versus-Calor hits that say nothing about other languages.
- **P5.** Last measured July 2026 on the old syntax with an unpinned model: 2.38x iterations pooled over two pairs at 30 runs, 1.62x with a 60-line exemplar; 6x to 8x the cost of C# without the exemplar and 2.3x to 3.7x with it; three of five green-field pairs and all modification pairs at parity. Not re-measured on 0.30.
- **P6.** Unchanged: 0.30 was a no-compatibility rewrite with no migrator. 853 of 1,823 `.calr` files in the tree are still in the retired syntax.
- **Premise Z.** Rust is the maintainer's reference bar. Published agent results for Rust are mixed and no same-harness Rust-versus-C# result exists. A training corpus is out of reach on this horizon. The available levers are the in-context reference, diagnostics and the toolchain; only the first has a measured effect.

## The revised path

The path is renamed from "close the gap with Rust" to: **reach equipped-C# parity on authoring cost, then show a benefit against the fairest comparator available, with Rust kept in view as a capability scorecard and one cost reference.**

It has seven steps, in dependency order.

1. **Make the instruments run on 0.30 and measure the language once.** Harness wiring, the legacy-dialect quarantine, the end-to-end suite, the soundness packet, and then #1605 with a concurrent C# arm. Nothing else in the plan can be sized until this publishes. (0.31)
2. **Deliver the in-context reference the way that measurably works.** Test pointer against inlined primer against primer plus starter file, decide by a rule fixed in advance, and change `calor init` accordingly. (0.31 measurement, 0.32 action)
3. **Use one last breaking window, then freeze.** Batch every pending breaking decision into 0.32 with one rewrite and a rewriter, then declare an edition. Agent reach (0.33) starts on a language that will not move. (0.32)
4. **Run the cheapest fair benefit test early.** Re-run the nine seeded-defect pairs on 0.30 with a third arm, C# equipped with executable specifications. If the Calor delta survives, the claim is a language effect; if it does not, the July result was "specifications were present" and 0.35 and 0.37 are resized. (0.32)
5. **Close the gaps a Rust user would not accept in the toolchain.** Tests written in the language, one command, measured warm latency, diagnostics with fixes where the rewrite is syntactic. Effects made precise on everyday .NET code so `unknown` does not swallow the one feature other languages lack. (0.32 to 0.33)
6. **Make proofs reach real code before deepening them.** Proof-status inventory, the string-proof demotion lifted where non-null is established, and consumers for preconditions on class members. Guard-derived facts enter through the verifier, not through the converter. New guarantees (null gaps, enum exhaustiveness, disposal) are built only for defect classes that agents are measured to produce. (0.34 to 0.35)
7. **Decide the adopter question now, not at 0.36.** A maintainer decision on outreach in 0.31, a checkpoint at the close of 0.33, and a fallback written in advance that rescopes 0.37 and holds 0.38 and 0.39 if no external maintainer has consented.

### The Rust bar as a scorecard

Each line is checked at every milestone close and needs no benchmark.

| Capability a Rust user takes for granted | Calor at 0.30 | Where the plan addresses it |
|---|---|---|
| Whole bug classes rejected at build | Null at three boundaries; effects; refinements. The `bool` exhaustiveness check exists but is inert in a default build (it gets no type information). No disposal check. | Rejecting guarantees decided in the 0.32 window; #1643 unions (0.34); base-rate-gated guarantees (0.35) |
| Diagnostics that carry a fix | 5 safe fix sites out of 248 diagnostic codes | #1645 amendment (0.32) |
| Tests written in the language | None; `calor init` rules block new test files | Test files allowed (0.32); tests in `.calr` (0.33) |
| One command that builds, tests and checks | `calor check` exists | Latency budget (0.32) |
| Fast warm feedback | Unmeasured on 0.30; index build 8-15x its 0.29 cost | #1738 (0.31), latency issue (0.32) |
| A stable language | None; 0.30 was a rewrite | Edition (0.32 exit) |
| A reference the agent already knows | No training prior | Exemplar delivery (0.31-0.32), example bank (0.33) |
| Cost per accepted task | Unknown on 0.30; unknown for Rust | #1605 (0.31), Rust cost reference (0.33) |

## Rules for the milestone rework

1. **Displacement.** Every new item names the issue it displaces or states that it is a text-only change.
2. **New hard errors.** Any issue that adds or tightens a hard error must show that the conversion scorecard's native count and the density ratio do not regress, and must report how many files newly fall to whole-file preservation.
3. **Base rate first.** No new guarantee is built without a measured count of the defect class in agent output and a hold branch for the zero-finding case.
4. **Paid runs.** Every paid agent run has a written spend ceiling, abort triggers, a zero-cost dry pass, one pinned model, and a reading written in advance for each outcome.
5. **No training-prior criteria.** No acceptance criterion depends on models having learned Calor.
6. **Breaking changes.** After the 0.32 edition declaration, existing spellings do not change. Additive syntax is allowed. Any later breaking change ships with a rewriter that migrates fixtures, pairs and documentation in the same change.
7. **Comparators.** Conventional C# and equipped C# are the only gating comparators through 0.37. Rust is reported, not adjudicated.
8. **Maintainer decisions are filed as decisions.** Outreach, spend authorisation and edition declaration are issues with the options written out; the plan does not assume their answers.

## Milestone themes after the rework

| Milestone | Was | Becomes | Exit |
|---|---|---|---|
| 0.31 | .NET 11, cleanup, density, deferred evaluation | Instruments run; the 0.30 language measured once | #1605 published with a concurrent C# arm; sweep green on a 0.30 packet; evidence ledger published |
| 0.32 | Stabilize 0.30 | Last breaking window, then freeze; first fair benefit test | Edition declared; seeded-defect replication published |
| 0.33 | Agent reach | Agent reach on a frozen language; Rust cost reference | Per-agent results published; adopter checkpoint recorded |
| 0.34 | Native conversion coverage | Same, re-baselined; the rest of .NET 11 and C# 15; the decisions that depend on converted code | Every unsupported registry entry native or with a recorded reason |
| 0.35 | Verification depth | Proofs that reach real code | Proof yield on named native modules reported before and after |
| 0.36 | External pilot | Same, entered only through the adopter checkpoint | Unchanged |
| 0.37 | Three-arm comparison | Same, as confirmation of a probe-scale positive, with stratified tasks | Unchanged |
| 0.38-0.39 | Autonomy, scale | Held behind the 0.37 verdict | Unchanged |

## What the skeleton critique changed

Before any issue was drafted, three critics checked the first milestone skeleton for completeness against the review, dependency order, and load. They found 3 blockers and 45 major problems. The main changes:

- **The CS8669 fix had the wrong finish line.** It pointed at the round-trip harness floors, which fell for a different cause. It now targets project-mode `calor migrate`, and a corpus report job measures it.
- **Benchmark isolation would not have isolated.** Workspaces are already outside the checkout; the leak is the repository path embedded in the Calor arm's project file. The issue now requires a staged product copy, a read sandbox, and a canary.
- **Paid runs had no authorisation rows.** Each milestone with a paid run now has one registration-and-spend decision (0.31, 0.32, 0.33, 0.37), with a decline branch, and exit criteria read "published, or the decline recorded".
- **The 0.32 rewrite would have invalidated the instruments restored in 0.31.** The window is now ordered: decisions, edition marker, rewriter, one rewrite, instruments re-registered, paid runs on the post-rewrite commit, then the declaration.
- **Guarantees that reject working programs were scheduled after the freeze.** They are now all decided inside the 0.32 window (land, warn, or wait behind a per-module opt-in).
- **Base-rate gates would have read zero by construction.** The benchmark pairs contain no `match`, `enum`, `using` or nullable types. Each defect class now has an opportunity count and a "not measurable" outcome that holds the item instead of dropping it.
- **.NET 11 minimum was too small.** Roll-forward alone makes every compile throw; the 0.31 scope now includes the metadata manifest and the generated project files.
- **Dependencies that pointed backwards were turned round.** #1648 moves to 0.31 (it precedes the #1708 draft); the guard lift moves behind its consumers in 0.35.
- **0.31 and 0.32 were overloaded.** Rows off the exit chain moved later or are marked "may slip"; oversized issues were split; maintainer decisions are batched into a few decision records per milestone.
