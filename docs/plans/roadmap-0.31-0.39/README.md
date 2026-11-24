# Calor roadmap 0.31 to 0.39, reworked

Written 2026-11-23 against Calor HEAD `bc80daeb` (v0.30) and the 89 issues that were open that day. Filed on GitHub on 2026-11-24; `placeholder-map.json` maps each drafting placeholder to its issue number.

## What changed, in short

The old roadmap put the first test of whether Calor helps agents in 0.37, behind an external pilot nobody has been authorised to recruit, and built features in the meantime. The reworked roadmap measures first and freezes early:

1. **0.31 makes the instruments run on the 0.30 language and measures it once.** The benchmark harness, the soundness sweep and the end-to-end suite are all still written in the syntax 0.30 deleted. Until they run, nothing later can be sized.
2. **0.32 is the last release allowed to break the language.** Every pending breaking decision lands through one rewriter in one change, the instruments are re-registered, and the syntax is declared an edition. 0.32 also runs the cheapest fair benefit test available: the nine seeded-defect tasks again, with a third arm of C# that carries executable specifications.
3. **0.33 launches on other agents only after the freeze,** takes one Rust cost reading, and records whether an external maintainer exists. If none does, a fallback written in advance applies and the later milestones shrink instead of drifting.
4. **0.34 and 0.35 keep their themes** (conversion coverage, verification) but are re-baselined against what the code does today and reordered so that proofs reach class members before anything is built on them.
5. **0.36 to 0.39 are unchanged in content** and gain entry criteria, so they are held, not silently started, when their preconditions fail.

Rust stays the reference bar in two forms: a capability scorecard checked at every release (bug classes rejected at build, diagnostics with fixes, tests written in the language, one command, warm latency, a stable language, cost per accepted task), and one same-harness cost reading in 0.33. It is reported, never a gate.

## The milestones

| Milestone | Theme | Epics | Issues | Of which new | Off the exit chain |
|---|---|---|---|---|---|
| [0.31](0.31.md) | Instruments run; the 0.30 language measured once | 8 | 39 | 22 | 14 |
| [0.32](0.32.md) | Last breaking window, then freeze; first fair benefit test | 7 | 45 | 30 | 9 |
| [0.33](0.33.md) | Agent reach on a frozen language; Rust cost reference | 6 | 23 | 14 | 7 |
| [0.34](0.34.md) | Native conversion coverage, re-baselined; .NET 11 completed | 3 | 25 | 10 | 8 |
| [0.35](0.35.md) | Proofs that reach real code | 3 | 19 | 14 | 1 |
| [0.36](0.36.md) | Maintainable beyond its creator | 1 | 8 | 0 | 0 |
| [0.37](0.37.md) | Earn the language | 1 | 9 | 3 | 0 |
| [0.38](0.38.md) | Earn greater autonomy (held behind the 0.37 verdict) | 1 | 6 | 0 | 0 |
| [0.39](0.39.md) | Scale demonstrated value (held behind the 0.37 verdict) | 1 | 4 | 0 | 0 |

In total: 31 epics (21 new), 93 new issues, and a disposition for each of the 89 open issues (13 kept as they are, 46 amended in place, 21 moved to another milestone, 7 rewritten because their bodies describe the pre-0.30 compiler, 2 closed).

"Off the exit chain" counts issues that may slip to the next milestone without holding the release. The issue counts are higher than the old roadmap's because oversized items were split until each has one finish line; about a quarter of the new issues are decision records or text-only changes.

### 0.31 — Instruments run; the 0.30 language measured once

Make every measuring instrument run on the 0.30 language, then measure that language with an agent once, against a concurrent C# arm. Nothing later can be sized until this publishes.

Exit:

- #1605 stage 1 published on 0.30 with a concurrent C# arm on the same pinned model; or, if only the full design is declined, the reduced design registered in #1770 published; or "no paid run authorised" recorded, with the rows that then read "not measured" listed in the spend record of #1770 and in the plan of record (#1765). Stage 2 (delivery variants) and the end-to-end leg do not hold the release; stage 2 must publish, or be recorded on #1605 as not run, before #1823 is decided.
- Soundness sweep green on a registered 0.30-syntax packet for the 0.31 run count stated in #1783.
- Evidence ledger published and the README benchmark paragraph corrected (#1774).
- With the released packages (built by the .NET 10 SDK), on a machine with only the .NET 11 SDK, calor init, calor check and dotnet build of a Calor.Sdk consumer succeed (#1639; may ship earlier as a 0.30.x point release).
- Adopter-outreach decision recorded (#1777), or "not decided, treated as decline until changed".
- Release CI flakes are fixed, or the reruns of the 0.31 release are recorded (#1749 is a release-process prerequisite, not a gate).

| Epic | Issues |
|---|---|
| #1758 · [0.31] Epic: tracker and plan of record re-baselined against 0.30 (new) | #1759, #1765, #673, #1555, #1670 |
| #1761 · [0.31] Epic: the benchmark harness runs the 0.30 language in isolation (new) | #1766, #1762, #1764, #1767, #1760, #1763, #1736 |
| #1775 · [0.31] Epic: measure the 0.30 language once, including how the reference reaches the agent (new) | #1770, #1605, #1773, #1771, #1772, #1745, #1774 |
| #1768 · [0.31] Epic: the proof-soundness instrument is restored (new) | #1694, #1769, #1781, #1778, #1783 |
| #1780 · [0.31] Epic: default migration yields Calor again, measured by a corpus job (new) | #1782, #1779 |
| #1644 · [0.31] Epic: .NET 11 minimum (first part of #1644) (existing #1644) | #1638, #1639 |
| #1594 · [0.31] Epic: test and CI health (existing #1594) | #1590, #1738, #1749, #1726, #1728 |
| #1776 · [0.31] Epic: adoption decisions that gate 0.36 and later (new) | #1777, #1648, #1791, #1786 |

### 0.32 — Last breaking window, then freeze; first fair benefit test

Act on the 0.31 measurement, batch every pending breaking decision into one release with one rewrite done by a shipped rewriter, re-register the instruments, re-run the seeded-defect comparison with an equipped C# arm on the post-rewrite language, then declare the result an edition.

Exit:

- The breaking-window decision record (#1784) is closed: every listed decision was decided or deferred to a future edition by the cut-off date, every row decided "land" has merged, and the rewrite has landed or the inventory records that none was needed.
- One repository rewrite: .calr files migrated by the shipped rewriter; embedded Calor text (test literals, sweep templates, documentation fences) listed with how each was migrated; instruments re-registered (#1807). Or the inventory records that no rewrite was needed because no decision changed source.
- The edition is declared with a per-module marker and a written compatibility promise, or the decision to defer is recorded with its effect on 0.33 (#1805).
- Seeded-defect replication with three conditions published on the post-rewrite commit, or the maintainer's decline recorded; if a collection is authorised but unpublished at release, the edition declaration is deferred (#1805) and this criterion is recorded as carried to 0.33.
- Agents may create test files through the sanctioned path: the write-hook exemption, and the rule text in whichever of the agent file and the MCP server instructions the #1823 record leaves carrying the rules (#1809).
- Unknown-effect share reported per corpus project (the corpus part of #1802; its agent-run table may follow).
- calor init delivery default decided (#1823).
- #1647 closed.

| Epic | Issues |
|---|---|
| #1649 · [0.32] Epic: stabilize the 0.30 language (existing #1649) | #1645, #1646, #1647, #1790, #1788, #1789, #1746, #909, #1511, #1142 |
| #1787 · [0.32] Epic: last breaking window — the decisions (new) | #1784, #1785, #1799, #1794, #1797, #1796, #1800, #1743, #1741, #1735, #1795 |
| #1792 · [0.32] Epic: one rewrite, instruments re-registered, then the edition (new) | #1084, #1793, #1806, #1803, #1807, #1805 |
| #1808 · [0.32] Epic: effect precision on everyday .NET code (new) | #1731, #1802, #1798, #1801, #1814 |
| #1812 · [0.32] Epic: seeded-defect replication on the edition syntax with three conditions (new) | #1815, #1813, #1816, #1810 |
| #1804 · [0.32] Epic: one toolchain — write, test and check in Calor (part 1) (new) | #1809, #1732, #1818, #1822 |
| #1821 · [0.32] Epic: act on the delivery measurement — calor init default, budgets, teaching reference (new) | #1820, #1823, #1824, #1734 |

### 0.33 — Agent reach on a frozen language; Rust cost reference

Launch on agents beyond Claude Code only after the edition is declared. Abstract the harness arm and oracle so other languages and the 0.37 conditions can run, take one Rust cost reading, finish the toolchain gaps, and record the adopter checkpoint.

Exit:

- Per-agent results for Codex, Gemini and Copilot published on one harness (#1811), or the maintainer's decline of that spend recorded.
- Rust cost reference reported, or the decision not to run it recorded with the reason.
- Adopter checkpoint recorded (#1833), or "not decided, treated as the fallback outcome until changed".
- Edition re-affirmed after #1650 and the per-agent results report, or declared here after a 0.32 deferral, or a second deferral recorded with its effect on 0.36 entry (#1846).
- calor test and calor check discover tests written in .calr (#1825).
- If #1846 records that a spelling changes, or #1825 is held because the promise covers tool layout, the record states what the release does; the criterion is then met by that record.

| Epic | Issues |
|---|---|
| #1653 · [0.33] Epic: agent reach (existing #1653) | #1650, #1651, #1811, #1652, #709, #711 |
| #1817 · [0.33] Epic: arm abstraction, a language-neutral oracle, and the Rust cost reference (new) | #1827, #1830, #1829, #1828, #1832, #1831 |
| #1819 · [0.33] Epic: one toolchain (part 2) — tests in Calor, incremental reuse, fewer unknown effects (new) | #1825, #1836, #1841 |
| #1838 · [0.33] Epic: the in-context example bank (new) | #1835, #1839 |
| #1840 · [0.33] Epic: identity and rename tooling (new) | #1744, #922, #1748 |
| #1826 · [0.33] Epic: checkpoints at the close of 0.33 (new) | #1833, #1846 |

### 0.34 — Native conversion coverage, re-baselined; .NET 11 completed

Raise the native share of converted code against a corrected baseline, complete .NET 11 and C# 15 support, and take the decisions and conditional steps that depend on converted code.

Exit:

- Every row of the coverage record docs/plans/0.34-native-coverage.md (the unsupported registry entries at 0.34 entry: 58 at 0.30 plus the C# 15 entries #1638 adds; checked-block, which is labelled Full but preserved by default conversion; and the family shapes preserved under an entry already labelled Full, such as record struct) is native or preserved with a recorded reason. "Native" means a registry promotion with a green D-S1.5 fixture (checked-block is demoted first, then promoted); for a family shape it means its preservation test is replaced and a runtime test added. A C# 15 entry whose output needs .NET 11 types may close as preserved with the reason kind not-reached, cause "needs .NET 11 hosts", linking #1643, while the fixture hosts stay on .NET 10.
- Native file fraction per project (written files with an empty losses list over all files, under default project-mode calor migrate) reported by the corpus report job (#1779).
- Guard-precondition decision recorded (#1847).
- Epic #1852 (.NET 11 remainder) is off the exit chain except for its coverage rows; its done-when is a green .NET 11-only consumer leg on all five RIDs and a recorded release-SDK decision (#1855).

| Epic | Issues |
|---|---|
| #1659 · [0.34] Epic: native conversion coverage (existing #1659) | #1654, #1655, #1656, #1657, #1658, #1850, #1733, #1747, #1742, #1844 |
| #1845 · [0.34] Epic: decisions and conditional steps on converted code (new) | #1848, #1847, #1834, #1842, #1854, #851, #1177, #1859 |
| #1852 · [0.34] Epic: complete .NET 11 and C# 15 support (remainder of #1644) (new) | #1643, #1640, #1641, #1642, #1855, #1856 |

### 0.35 — Proofs that reach real code

Before deepening the verifier, make its proofs count on the code agents and converters actually produce: inventory what is Proven, lift the demotions that can be lifted soundly, and give preconditions consumers on class members.

Exit:

- Proof yield (Proven, Assumed, Unsupported) reported before and after on a named set of native modules; or, if fewer than six corpus modules qualify under the selection rule of #1843, the outcome the maintainer recorded on #1662 (a relaxed rule fixed before measuring, or "not measurable on the corpus" with the yield reported under that caveat).
- Every new verifier step has seeded unsound controls and safe controls in the registered instrument for that analysis (the sweep for the contract and obligation engines; BenchmarkSubjectRegressionTests and BugPatternRuntimeOracleTests for the bug-pattern and taint analyses), and that instrument is green.
- The release is not earlier than the last freeze in the milestone that changed an existing sweep verdict plus the proof-claim count in days.

| Epic | Issues |
|---|---|
| #1662 · [0.35] Epic: verification depth (existing #1662) | #1853, #1843, #875, #1851, #1862, #1868, #1660, #1661 |
| #1861 · [0.35] Epic: contract consumers (new) | #1864, #1863, #1723, #1858, #1849, #1860, #1866 |
| #1872 · [0.35] Epic: base-rate-gated guarantees (conditional) (new) | #1869, #1871, #1870 |

### 0.36 — Maintainable beyond its creator

The external pilot, entered only through the adopter checkpoint.

Exit:

- The exit record #1697 defines is posted as a comment on the epic and entered in the evidence ledger.

| Epic | Issues |
|---|---|
| #1697 · [0.36] Epic: maintainable beyond its creator (existing #1697) | #1701, #1702, #1703, #1704, #1705, #1706, #1707 |

### 0.37 — Earn the language

The three-condition comparison, described as confirmation under fairer controls of a probe-scale positive, with stratified tasks and the inputs the September stop listed as missing.

Exit:

- The language investment verdict published, or the #1867 decline recorded, or the fallback disposition recorded, as #1698 defines.

| Epic | Issues |
|---|---|
| #1698 · [0.37] Epic: earn the language (existing #1698) | #1708, #1709, #1710, #1711, #1712, #1865, #1857, #1867 |

### 0.38 — Earn greater autonomy (held behind the 0.37 verdict)

Unchanged in content. Entered only on a positive 0.37 verdict; held if the fallback applied.

Exit:

- One of the three exit records #1699 defines is posted (decision published; stop or redirect; budget declined). A milestone that was never entered has no exit record and stays held.

| Epic | Issues |
|---|---|
| #1699 · [0.38] Epic: earn greater autonomy (existing #1699) | #1713, #1714, #1715, #1716, #1717 |

### 0.39 — Scale demonstrated value (held behind the 0.37 verdict)

Unchanged in content. Entered only on demonstrated value.

Exit:

- The selected capability delivered, or an investment decision or stop recorded, as #1700 defines.

| Epic | Issues |
|---|---|
| #1700 · [0.39] Epic: scale demonstrated value (existing #1700) | #1718, #1719, #1720 |

## Decisions only the maintainer can take

The plan files each of these as a decision issue with the options, their cost and a recommendation. It does not assume the answer. They are batched so that one sitting covers several.

**0.31**

- Evaluation registration (#1770): arms, pairs, n, model and commit pin, spend, close rule, class thresholds, delivery rule, MCP rule. Covers #1605, #1764, #1760, #1773, the paid leg of #1736.
- Tracker sign-off: #1759 with #673, #1555, #1670, #1728 and the allowlist classification in #1763.
- Toolchain and sweep record: #1639 (tool framework, SDK pin, roll-forward), #1769 (packet design, the frozen-whitelist disposition, the procedure for adding rows), #1778 (verdict classification), #1783 (run counts).
- Adoption session: #1777, #1648, #1786.

**0.32**

- Window record (#1784), covering every spelling decision under the amended rule #1785: #1646 flips, #1645 respellings, #1794, #1797, #1796, the edition-marker spelling (#1084). One payload manifest and one token-count dispatch per tokenizer for the whole batch. The payload date precedes the cut-off; the opt-in mechanism is the one the #1084 design record defines.
- Semantic record: #1800 (rejecting guarantees, including making the bool exhaustiveness check effective and whether obligation verification may ever be a default-build check), #1735, any effect spelling #1731 needs.
- Spend record (#1816) for both paid collections in 0.32: the replication and the #1645 re-run. Part B also registers the base-rate reading for the #1789 pairs (the #1770 rule applied to the re-run) with a run count that reaches its 30-run floor.
- Delivery and budget record: #1823, #1824, the template budget, and the division of the remaining always-loaded token headroom among #1809, #1822, #1650 and #1825.
- Edition declaration (#1805).
- Converter record (#1647): the lossy floor that cannot be restored for files preserved by design; whether the corpus report job gets blocking native floors; whether the directory-mode native-fraction column is removed.

**0.33**

- Spend record (#1832) for the per-agent runs and the Rust reading.
- Adopter checkpoint (#1833) and edition re-affirmation (#1846).

**0.34**

- Guard-precondition form (#1847), overlay and #1177 (#1859), venue feasibility (#1854), disposal go/hold/drop (#1842).

**0.35**

- Reference-null modelling (#1860); an opt-in surface for obligation verification (#1868); which null gaps clear the gate (#1869).
- Guard lift re-read at the 0.35 close: if #1866 is held, the census reading is re-read from the corpus job and the maintainer adds a dated entry to the #1847 record.

**0.37**

- Methods reviewer named (#1777) and reviews cleared (#1857); collection budget (#1867).

**0.38**

- Protocol, cost limits and spend for the bounded-search collection (#1713).

## What happens to each open issue

| Issue | Was in | Now in | Action | Title after the plan is applied |
|---|---|---|---|---|
| #673 | none | closed | close | v0.7 — MCP agent-loop: doc-truth patch (0.6.6) + calor_scaffold spine (0.7.0) |
| #709 | 0.33 | 0.33 | amend | [0.33] Codex support — authored and validated BY Codex |
| #711 | 0.33 | 0.33 | amend | [0.33] Gemini support — validated by Gemini |
| #851 | none | 0.34 | amend | [0.34] WS-W4 Slice-E hardening: task-gen filter precision (prefix-anchor + custom-DisplayName exclusion rate) |
| #875 | none | 0.35 | rewrite | [0.35] Lift the string-proof demotion where non-null is established |
| #909 | 0.32 | 0.32 | amend | [0.32] DivisionByZeroChecker double-reports divisions nested under bound wrapper nodes; Calor0926 suggests over-strong contracts for deferred divisions |
| #922 | none | 0.33 | move-in | [0.33] LSP rename cannot rename declarations that span files (partial types, multi-file modules) — needs identity groups in the workspace index |
| #1084 | none | 0.32 | rewrite | [0.32] Per-module edition marker |
| #1142 | 0.32 | 0.32 | keep | [0.32] Delete the configured-vs-defaulted trap in EffectEnforcementPass's caps (follow-up to #1141) |
| #1177 | 0.32 | 0.34 | move-in | [0.34] EffectViolation's Calor0410 differential was a converter artifact and #1173 retired it — pin the contract, or move the stratum |
| #1254 | paused | paused | keep | Epic: run the redesigned PP-W-rows experiment, or establish it cannot be built |
| #1262 | paused | paused | keep | PP-W-rows: register and run stage 2, adjudicate the proof point, re-arm gate 10 |
| #1267 | paused | paused | keep | PP-W-rows: run and adjudicate stage 1 against the off-ramp |
| #1436 | paused | paused | keep | PP-W-rows: diagnose the real unknown-charge halt and preserve a reviewed continuation |
| #1511 | 0.32 | 0.32 | keep | [0.32] Converter: lossy partial-class merge output reports Calor0410 for cross-file types |
| #1555 | 0.32 | closed | close | Parser: hand-written §ARR with a type-like id and a non-type-like element type swaps id and type |
| #1590 | 0.31 | 0.31 | amend | [0.31] Compiler test-host memory growth is capped by sharding, not fixed (#1150 follow-up) |
| #1594 | 0.31 | 0.31 | amend | [0.31] Epic: test and CI health |
| #1605 | 0.31 | 0.31 | amend | [0.31] Before/after agent evaluation of the simplified language |
| #1638 | 0.31 | 0.31 | amend | [0.31] Converter parses C# 15 (Roslyn upgrade); new constructs native or preserved with a reason |
| #1639 | 0.31 | 0.31 | amend | [0.31] Build and target net11.0: Calor.Sdk, Calor.Tasks, generated code; decide tool TFM and SDK pin |
| #1640 | 0.31 | 0.34 | move-in | [0.34] Effect manifests and nullability metadata for .NET 11 BCL additions; CI check for missing entries |
| #1641 | 0.31 | 0.34 | move-in | [0.34] CI legs for .NET 11 alongside .NET 10 |
| #1642 | 0.31 | 0.34 | move-in | [0.34] Dependency refresh: System.CommandLine beta4 → stable, Roslyn/MSBuild packages, drop unused old test TFMs |
| #1643 | 0.31 | 0.34 | move-in | [0.34] Native C# 15 unions and closed hierarchies with exhaustive match |
| #1644 | 0.31 | 0.31 | amend | [0.31] Epic: .NET 11 minimum |
| #1645 | 0.32 | 0.32 | amend | [0.32] Act on the 0.30 agent evaluation: fix or revisit every regressed construct |
| #1646 | 0.32 | 0.32 | rewrite | [0.32] Apply the amended fewest-tokens rule to every 0.30 spelling across the named tokenizer set |
| #1647 | 0.32 | 0.32 | amend | [0.32] Default-path converter regressions after 0.30 |
| #1648 | 0.32 | 0.31 | move-in | [0.31] Decide the paused PP-W-rows research: resume with a plan or close |
| #1649 | 0.32 | 0.32 | amend | [0.32] Epic: stabilize the 0.30 language |
| #1650 | 0.33 | 0.33 | rewrite | [0.33] calor init per agent generated from the single reference, budgeted and tested |
| #1651 | 0.33 | 0.33 | amend | [0.33] Agent evaluation harness beyond the claude CLI: driver contract and zero-cost pass for Codex, Gemini, Copilot |
| #1652 | 0.33 | 0.33 | amend | [0.33] MCP server compatibility tests across agent clients |
| #1653 | 0.33 | 0.33 | amend | [0.33] Epic: agent reach |
| #1654 | 0.34 | 0.34 | rewrite | [0.34] Native record structs and the record shapes still preserved |
| #1655 | 0.34 | 0.34 | rewrite | [0.34] Native using declarations, await using, checked blocks and standalone blocks |
| #1656 | 0.34 | 0.34 | amend | [0.34] Native interface member semantics and delegate declarations |
| #1657 | 0.34 | 0.34 | amend | [0.34] Native conditional compilation and directives in every position |
| #1658 | 0.34 | 0.34 | rewrite | [0.34] Native coverage for the remaining smaller C# constructs |
| #1659 | 0.34 | 0.34 | amend | [0.34] Epic: native conversion coverage |
| #1660 | 0.35 | 0.35 | amend | [0.35] Model more BCL in Z3: string ops, collections, Math |
| #1661 | 0.35 | 0.35 | amend | [0.35] Promote k-induction and loop-invariant synthesis into the verification pipeline |
| #1662 | 0.35 | 0.35 | amend | [0.35] Epic: verification depth |
| #1670 | none | 0.31 | amend | [0.31] Re-test on 0.30: module-level documentation text and the generated C# |
| #1694 | none | 0.31 | move-in | Soundness sweep failing on main |
| #1697 | 0.36 | 0.36 | amend | [0.36] Epic: maintainable beyond its creator |
| #1698 | 0.37 | 0.37 | amend | [0.37] Epic: earn the language |
| #1699 | 0.38 | 0.38 | amend | [0.38] Epic: earn greater autonomy |
| #1700 | 0.39 | 0.39 | amend | [0.39] Epic: scale demonstrated value |
| #1701 | 0.36 | 0.36 | amend | [0.36] Select an external pilot and define component ownership and acceptance |
| #1702 | 0.36 | 0.36 | amend | [0.36] Enforce separate authorization for obligation and implementation changes |
| #1703 | 0.36 | 0.36 | amend | [0.36] Bind property-level evidence to the exact built and deployed artifact |
| #1704 | 0.36 | 0.36 | keep | [0.36] Establish pilot evidence readiness and fail closed on invalid validation runs |
| #1705 | 0.36 | 0.36 | amend | [0.36] Deliver mixed C#/Calor integration, diagnostics, and supported-platform deployment |
| #1706 | 0.36 | 0.36 | keep | [0.36] Ship one useful pilot improvement with independent acceptance and measured costs |
| #1707 | 0.36 | 0.36 | keep | [0.36] Rehearse agent-unavailable incident recovery and explicit C# divergence |
| #1708 | 0.37 | 0.37 | amend | [0.37] Define independent engineering tasks, held-out acceptance, and economic targets |
| #1709 | 0.37 | 0.37 | amend | [0.37] Build fair conventional C#, semantically equipped C#, and Calor conditions |
| #1710 | 0.37 | 0.37 | amend | [0.37] Extend the existing agent harness for three conditions and full-effort accounting |
| #1711 | 0.37 | 0.37 | amend | [0.37] Add blinded implementation, specification, and boundary-defect controls |
| #1712 | 0.37 | 0.37 | amend | [0.37] Execute the approved no-search comparison and publish the language investment verdict |
| #1713 | 0.38 | 0.38 | amend | [0.38] Approve the bounded-search task class, six-condition protocol, and cost limits |
| #1714 | 0.38 | 0.38 | keep | [0.38] Implement bounded candidate search with cancellation and complete cost accounting |
| #1715 | 0.38 | 0.38 | keep | [0.38] Independently validate candidates and prevent held-out benchmark overfitting |
| #1716 | 0.38 | 0.38 | amend | [0.38] Evaluate staged evidence-assisted review and independent defect detection |
| #1717 | 0.38 | 0.38 | amend | [0.38] Run the approved search comparison and publish the autonomy decision |
| #1718 | 0.39 | 0.39 | amend | [0.39] Select one customer-backed investment and publish alternatives and stop criteria |
| #1719 | 0.39 | 0.39 | keep | [0.39] Conditional: evaluate native deployment only against an unmet pilot requirement |
| #1720 | 0.39 | 0.39 | keep | [0.39] Deliver the selected capability and validate external maintenance outcomes |
| #1723 | none | 0.35 | move-in | [0.35] Import numeric-range and array-length preconditions into the bug-pattern checkers (repaired safe controls) |
| #1726 | none | 0.31 | move-in | [0.31] Website cold-mobile asset test intermittently times out waiting for networkidle |
| #1728 | 0.31 | 0.31 | amend | [0.31] Release: promote a validated immutable bundle instead of repeating full validation |
| #1731 | none | 0.32 | move-in | [0.32] Sound effect contracts for culture-sensitive BCL members, LINQ and Object dispatch |
| #1732 | none | 0.32 | amend | [0.32] MSBuild task: port the CLI's cross-module independence proof, and accept namespaced static partial module wrappers |
| #1733 | 0.31 | 0.34 | move-in | [0.34] Re-check marked passthrough: stale-block report and re-conversion (#1621 phase B2) |
| #1734 | 0.31 | 0.32 | move-in | [0.32] Bring the full teaching reference to the #1597 token estimate (6-7K) |
| #1735 | 0.31 | 0.32 | move-in | [0.32] Inherited parameter refinements: same exception and contract-mode behavior as direct ones |
| #1736 | 0.31 | 0.31 | amend | [0.31] Migrate the E2E agent-task refactoring benchmark to the 0.30 language before its next run |
| #1738 | 0.31 | 0.31 | amend | [0.31] Index build is 8-15x slower than 0.29 (symbol-index binding and the per-module effect pass) |
| #1741 | 0.31 | 0.32 | move-in | [0.32] Remove 0.30 residue: legacy dbr/dbw effect codes, Lisp-era operator hints, inline-sibling counter |
| #1742 | 0.31 | 0.34 | move-in | [0.34] Converter: emit nested expressions instead of _chain/_new/_elem/_idx statement hoists |
| #1743 | 0.31 | 0.32 | move-in | [0.32] Converter never emits an else whose body is only an if; record the decision |
| #1744 | 0.31 | 0.33 | move-in | [0.33] Sidecar identity through MCP class rename, LSP rename and cross-class member moves |
| #1745 | 0.31 | 0.31 | amend | [0.31] Run every runnable teaching-reference example in CI, not only compile it |
| #1746 | 0.31 | 0.32 | move-in | [0.32] Code density: migrated Calor token ratio reported per project |
| #1747 | 0.31 | 0.34 | move-in | [0.34] Canonical-usage lint (Calor1305/1306) on converted output for every round-trip project |
| #1748 | 0.31 | 0.33 | move-in | [0.33] Website: explain declaration identity (ids sidecar) and how to rename through tooling |
| #1749 | 0.31 | 0.31 | amend | [0.31] Release CI flakes: 0.1 ms CFG smoke budget, formatter replay losing files, compiler shard starving the runner |

## New issues

Issue numbers are the filed ones.

**0.31**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1759 | #1758 | [0.31] Apply the plan of record to the tracker | text only |
| #1765 | #1758 | [0.31] Publish the 0.31-0.39 plan of record: rules, dependency edges, fallback, scorecard, release close-out | text only |
| #1766 | #1761 | [0.31] run-pair.sh runs the 0.30 Calor arm and the pinned pre-0.30 arm A | engineering |
| #1762 | #1761 | [0.31] Benchmark arm isolation: the agent can neither name nor read the checkout | engineering |
| #1764 | #1761 | [0.31] Loop telemetry: fix flags on diagnostic entries, and build diagnostics captured for the C# arm | engineering |
| #1767 | #1761 | [0.31] Harness support for delivery variants: variant selection, CLI-only switch, primer-read log, starter-less fixtures | engineering |
| #1760 | #1761 | [0.31] Report script for the 0.30 evaluation: failure classes, opportunity counts, dead-end rate, repeat rate, blocked test writes | engineering · may slip |
| #1763 | #1761 | [0.31] Legacy-dialect allowlist and self-check | engineering · may slip |
| #1770 | #1775 | [0.31] Decision: register and authorise the 0.30 evaluation | decision |
| #1773 | #1775 | [0.31] calor init options for primer delivery (pointer or inline) and an optional starter file; default unchanged | engineering |
| #1771 | #1775 | [0.31] Serve the primer outside MCP resources: a calor_help action and a CLI command | engineering |
| #1772 | #1775 | [0.31] Correct the "remains unknown" statements in the teaching reference against the effect manifests | text only |
| #1774 | #1775 | [0.31] Evidence ledger, README correction, and a claims check that reads README.md | text only |
| #1769 | #1768 | [0.31] Design record for the successor soundness packet | decision |
| #1781 | #1768 | [0.31] Port the sweep templates, generator and harness literals to 0.30 syntax | engineering |
| #1778 | #1768 | [0.31] Register the successor packet and freeze expected verdicts | engineering |
| #1783 | #1768 | [0.31] Release rule: green-sweep run counts for the 0.31 exit, for 0.35 entry, and for any "proven" claim | text only |
| #1782 | #1780 | [0.31] Preserved C# originals compile under their own nullable context: project-mode migrate verifies and the migrated project builds (CS8669) | engineering · may slip |
| #1779 | #1780 | [0.31] Corpus report job: buildable copies of the three corpus projects migrated by default calor migrate, one JSON report per project | engineering · may slip |
| #1777 | #1776 | [0.31] Decision: authorise and log outreach — an external adopter and an independent methods reviewer | decision |
| #1791 | #1776 | [0.31] Open the long-lead drafts: the #1708 protocol and, if outreach is authorised, the #1701 recruitment record | text only · may slip |
| #1786 | #1776 | [0.31] One-page sizing and budget request for #1708 and #1712 | text only · may slip |

**0.32**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1790 | #1649 | [0.32] Fix coverage for agent-hit diagnostics | engineering |
| #1788 | #1649 | [0.32] Calor0006 names the replacement for every retired § marker | engineering · may slip |
| #1789 | #1649 | [0.32] Author two modify-a-converted-module pairs on one migrated corpus project | engineering |
| #1784 | #1787 | [0.32] Breaking-window decision record | decision |
| #1785 | #1787 | [0.32] Amend the fewest-tokens rule: a minimum win and a named tokenizer set | decision |
| #1799 | #1787 | [0.32] Tokenizer-set tooling: count the same payloads with each tokenizer named by the rule | engineering |
| #1794 | #1787 | [0.32] Decide the brace-header forms accepted "pending a separate decision" | decision |
| #1797 | #1787 | [0.32] Union declaration spelling: token-measured decision before the freeze | decision |
| #1796 | #1787 | [0.32] Decide whether a redundant public is canonical | decision |
| #1800 | #1787 | [0.32] Decide every rejecting guarantee inside the window: null remainder, enum-match exhaustiveness, and what counts as breaking | decision |
| #1795 | #1787 | [0.32] Round-trip leg compiled with the default contract mode and with effects enforced | engineering |
| #1793 | #1792 | [0.32] Compiler support for the forms the window decided (conditional) | conditional |
| #1806 | #1792 | [0.32] calor fix: a rewriter for the breaking-window batch, with tests | engineering |
| #1803 | #1792 | [0.32] Apply the rewrite once across the inventory | engineering |
| #1807 | #1792 | [0.32] Re-register the instruments after the rewrite | engineering |
| #1805 | #1792 | [0.32] Decision: declare the edition and publish the compatibility promise | decision |
| #1802 | #1808 | [0.32] Report the unknown-effect share per corpus project and in every agent-run report | engineering |
| #1798 | #1808 | [0.32] Compile-check every "remains unknown" statement in the teaching text | engineering |
| #1801 | #1808 | [0.32] Pin: effect-row and contract changes are never marked safe to apply | engineering |
| #1814 | #1808 | [0.32] calor effects suggest --merge never turns an unresolved call into a pure manifest entry | engineering |
| #1815 | #1812 | [0.32] Harness for three conditions | engineering |
| #1813 | #1812 | [0.32] Equipped C# fixtures for the nine seeded-defect pairs, with a fairness review | engineering |
| #1816 | #1812 | [0.32] Decision: register and authorise the 0.32 paid runs — the three-condition replication and the #1645 re-run | decision |
| #1810 | #1812 | [0.32] Run and publish the replication | engineering |
| #1809 | #1804 | [0.32] Agents may create test files under tests/: hook exemption and template text | engineering |
| #1818 | #1804 | [0.32] Measure warm calor check and MCP write latency on 0.30; CI budget for edit-to-diagnostic time | engineering |
| #1822 | #1804 | [0.32] Define and test what an agent does with a preserved C# file | engineering · may slip |
| #1820 | #1821 | [0.32] Resource-read capability check for Codex, Gemini CLI and Copilot | engineering |
| #1823 | #1821 | [0.32] Decision: set the calor init default and the token budgets per the registered delivery rule | decision |
| #1824 | #1821 | [0.32] Set the always-on MCP budget from the registered MCP rule | conditional |

**0.33**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1811 | #1653 | [0.33] Run and publish per-agent results (conditional) | conditional |
| #1827 | #1817 | [0.33] Arm abstraction in the pair harness | engineering |
| #1830 | #1817 | [0.33] Language-neutral, process-level oracle for a small headroom task set | engineering |
| #1829 | #1817 | [0.33] External anchor: published per-language agent results and what they do not show | text only |
| #1828 | #1817 | [0.33] Rust fixtures and iteration shim for the oracle tasks, with a zero-cost pass | conditional |
| #1832 | #1817 | [0.33] Decision: register and authorise the 0.33 paid runs — per-agent runs and the Rust reading | decision |
| #1831 | #1817 | [0.33] Run and report the Rust cost reading | conditional |
| #1825 | #1819 | [0.33] calor test and calor check discover tests written in .calr, with one teaching example | engineering |
| #1836 | #1819 | [0.33] Incremental build: the remaining reuse tier | conditional · may slip |
| #1841 | #1819 | [0.33] Exact effect contracts for the callee families that account for most unknown-charged call sites | engineering · may slip |
| #1835 | #1838 | [0.33] Example bank: compiler-checked programs by construct and task shape, served through calor_help and calor init | engineering · may slip |
| #1839 | #1838 | [0.33] Admission rule for public Calor: original, formatted, lint-clean, run in CI | text only · may slip |
| #1833 | #1826 | [0.33] Decision: adopter checkpoint — consent recorded, or the fallback applies | decision |
| #1846 | #1826 | [0.33] Re-affirm the edition after #1650 and the per-agent results (#1811) report | decision |

**0.34**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1850 | #1659 | [0.34] Disposition for the unsupported registry entries outside the five families | engineering |
| #1844 | #1659 | [0.34] Density target: migrated Calor below 1.0x C# tokens on the default path | engineering · may slip |
| #1848 | #1845 | [0.34] Guard census on the pinned corpus and on agent-authored outputs | engineering |
| #1847 | #1845 | [0.34] Decision: guard-derived preconditions — which form? | decision |
| #1834 | #1845 | [0.34] Count converted modules that carry the nullable-oblivious header, and set a path to shrink the number | engineering · may slip |
| #1842 | #1845 | [0.34] Disposal spike after #1655: design note and a count in agent outputs | conditional · may slip |
| #1854 | #1845 | [0.34] Feasibility gate: can the real-scale venue reopen under its two registered re-entry conditions? | conditional · may slip |
| #1859 | #1845 | [0.34] Decision: the authored-contract overlay — a feasibility record on one module, or explicit deferral | decision · may slip |
| #1855 | #1852 | [0.34] Complete net11.0 support (remainder of #1639) | engineering |
| #1856 | #1852 | [0.34] A C# 15 project in the round-trip corpus | engineering |

**0.35**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1853 | #1662 | [0.35] Proof-status inventory on the conversion corpus | engineering |
| #1843 | #1662 | [0.35] Where the 0.35 contracts come from, and a proof-yield metric on real programs | engineering |
| #1851 | #1662 | [0.35] Lift the reference-model demotion for arrays where non-null is established, or record why not | engineering |
| #1862 | #1662 | [0.35] Scope proof demotion and unsupported-type refusal to the variables a query reads | engineering |
| #1868 | #1662 | [0.35] Decision: an opt-in CLI and build surface for obligation verification | decision |
| #1864 | #1861 | [0.35] Call-site refinement obligations for class methods and constructors (operator bodies as callers) | engineering |
| #1863 | #1861 | [0.35] Widen call-site obligation argument types to the integer types the translator already models | engineering |
| #1858 | #1861 | [0.35] Taint-analysis precision on the two repaired safe controls (split from #1723) | engineering · may slip |
| #1849 | #1861 | [0.35] Fall-through fact after a dominating if (c) throw (conditional) | conditional |
| #1860 | #1861 | [0.35] Decision: model reference null in the contract translator, or record why not | decision |
| #1866 | #1861 | [0.35] Opt-in guard lift in calor convert (conditional) | conditional |
| #1869 | #1872 | [0.35] Decision: which documented null gaps clear the base-rate gate; file one issue per cleared gap | conditional |
| #1871 | #1872 | [0.35] Exhaustiveness over enum matches (conditional) | conditional |
| #1870 | #1872 | [0.35] Must-dispose bug-pattern checker (conditional on the 0.34 spike) | conditional |

**0.37**

| Placeholder | Epic | Title | Kind |
|---|---|---|---|
| #1865 | #1698 | [0.37] Task supply inventory | engineering |
| #1857 | #1698 | [0.37] Independent methods review of the protocol and the fairness check | decision |
| #1867 | #1698 | [0.37] Decision: authorised collection budget | decision |


## How this was produced, and what was not checked

1. An adversarial review of my assessment: seven reviewers, each finding checked against the repository by a second reader (`adversarial-review.md`). My response is in `dispositions-and-revised-path.md`.
2. A milestone skeleton, attacked by three critics before any issue was written (3 blockers, 45 major problems fixed).
3. One agent drafted each of the 31 epic files; a second agent checked every baseline statement and file citation against the repository and repaired the file (457 corrections).
4. Six reviewers each read one chain of issues across all files (measurement, breaking window, proofs, conversion, adoption fallback, toolchain) and reported contradictions; a final pass applied 321 changes.
5. A structural lint checks every file against the skeleton: all 178 issues present once, metadata complete, no dependency on a later milestone. It reports 0 problems.

Limits:

- **Nothing was built or run.** No .NET SDK was available. Every statement about behaviour comes from reading code, tests and CI configuration; where reading could not settle a point the issue says "not verified" and usually makes settling it the first step.
- **The corpus submodules were not checked out**, so nothing was counted in MediatR, Serilog or FluentValidation.
- **The final pass was not re-checked across files.** `open-questions.md` lists the edits one file's agent asked of another file; run it as a checklist when filing.
- **Issue bodies are long** (about 1.7 MB in total). They were written to stand alone for an agent handed one issue and the repository.

## Files

| File | What it is |
|---|---|
| `README.md` | This overview |
| `0.31.md` to `0.39.md` | One document per milestone: criteria, decisions, every epic and issue |
| `issues.json` | Every issue and epic body with metadata, ready to file |
| `open-questions.md` | Questions for the maintainer, work no issue owns, residual cross-file edits |
| `adversarial-review.md` | The review of the original assessment |
| `dispositions-and-revised-path.md` | What I accepted from the review and the revised path |
