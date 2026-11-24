# Open questions, unowned follow-ups and residual edits

Collected from the final repair pass over the 31 epic files and from the six cross-file reviews. Each issue was written to hold whichever way its question is answered; the recommendation is the plan's, and the decision is the maintainer's.

## Questions for the maintainer

### Q1. #1765 / #1702, #1703, #1704

Under the adopter fallback, in which milestone are the pilot-independent parts of #1702, #1703 and #1704 delivered, and do the three issues move in the tracker?

Options: (a) Delivered in 0.37 ahead of any 0.37 collection, under F1 as well as F2, with the issues left open in 0.36 because their pilot parts are held; (b) move the three issues to 0.37 in the tracker; (c) deliver them in 0.36 as exceptions to the hold, as the #1697 entry section already allows them to proceed.

Recommendation: (a), as written in the fallback table. It is marked as a proposal accepted by approving the #1765 pull request, and the section 2 line says the 0.36-to-0.37 edges are met by the parts being merged on main, so the text holds under (b) or (c) with a one-row change.

### Q2. #1759 / #1728

Should #1728 carry a fixed new title, or does its title keep following the option the maintainer picks in #1594 (close as completed, or retitle and narrow)?

Options: (a) Leave as is: the checklist row says state and title follow the option chosen and the count check allows for it; (b) add a new_title for the narrow option to the row list and the checklist.

Recommendation: (a). The prefix check passes with either outcome and no text depends on the title.

### Q3. #1765 section 2, #1770

Which rows read 'not measured' if no paid run is authorised in 0.31? The 0.31 exit criterion requires the list in both the #1770 spend record and the plan of record, and no file held it.

Options: (a) The list now drafted in #1765: the scorecard rows 'Cost per accepted task' and 'A reference the agent already knows', #1605, #1767, #1645, #1790, #1788, #1800, the #1645 re-run part of #1816, #1823, #1824, #1651, #1832; (b) a shorter or longer list fixed by the maintainer in #1770.

Recommendation: (a), derived from every row whose dependencies name #1605. The body says to copy the spend record's list if the two differ when the document is written.

### Q4. #1767, #1770

Does an MCP calor_compile call after an edit count as an iteration? The generated agent file tells agents to compile that way (CLAUDE.md.template:13), and those calls are not journalled, so Calor iteration counts in initialized workspaces can be understated against C#.

Options: (a) Yes: #1767 journals each one as a record with cmd 'compile'. (b) No: iterations stay as build, test, run and check records, and the per-run count of calor_compile calls is reported beside them.

Recommendation: (a), stated as a rule in the #1770 design entry, so that stage 2 and the 0.33 per-agent arms count Calor and C# the same way. #1767 now records the calls in result.json either way (new step 6 and a done-when bullet) and journals them only if the entry says yes.

### Q5. #1762, #1770

If the second pull request of #1762 (sandbox, canary, read log) starts before the #1770 design entry names the platform, is building for macOS sandbox-exec the accepted default?

Options: (a) Confirm the default as written. (b) Make the sandbox part wait for the design entry (adds #1770 as a dependency of an exit-critical row).

Recommendation: (a). macOS is the plan's recommendation in #1770 item 4 and the only sandbox precedent in the tree; the body states the assumption in the pull request.

### Q6. #1763, #1648

Should the legacy-dialect allowlist and self-check also cover the 220 tracked .calr.inc fragments of the paused PP-W research (all hold retired markers)?

Options: (a) Out of scope by decision: #1648's closing record classifies them and nothing enforces it (as written now). (b) Extend the check's glob and the allowlist to *.calr.inc, adding five directory-prefix entries.

Recommendation: (b) is cheap and closes the gap, but the plan scopes the check to .calr files, so the file records (a) and names the extension as a follow-up. If (b) is chosen, #1763 needs one sentence in step 4 and five rows in its table.

### Q7. #1763, #1648, #1803

Is #1648 a hard dependency of #1763? The issue may close with the 52 W-00x files listed as pending-1648, so the edge is an ordering preference only.

Options: (a) Keep the dep and the soft wording (as written). (b) Drop #1648 from #1763's deps. (c) Make it hard: #1763 may not close until #1648 records the fate of the directories.

Recommendation: (b), or (a). (c) would put #1648 on the 0.32 exit chain through #1803. The body now says an open #1648 holds neither this issue nor #1803.

### Q8. #1770 item 4, #1605, #1771, #1745

Must the stage 1 commit pin contain #1771 and all of #1745, or only what a stage 1 arm can reach? Stage 1 arms load no template and no MCP server and read only the primer (nine programs), so #1771 and the 72 non-primer expectations of #1745 change nothing stage 1 measures, yet they sit on the 0.31 exit chain.

Options: (a) Keep as the epic sequence states: #1772, all of #1745 and #1771 in the stage 1 pin. (b) Stage 1 pin requires #1772 and part 1 of #1745 (mechanism, nine primer programs, the 15 already executed); #1771 and part 2 of #1745 are required only in the stage 2 pin and before #1734. Under (b) the skeleton deps of #1605 and the epic sequence line need the same change.

Recommendation: The file recommends (a), matching the version 5 sequence, and is written so that (b) needs only a line in the design entry: item 4 lets the entry say which, #1745 is now two ordered parts, and #1771's done-when refers to the first stage whose pin rule names it. (b) takes one engineering issue and most of #1745 off the exit chain at no cost to stage 1.

### Q9. #1770 item 5, #1767

Does an MCP calor_compile call that follows an edit count as an iteration in stage 2? The shipped template's Rule 4 tells agents to compile that way, and neither those calls nor calor check are journalled today.

Options: (a) Yes: counted like calor check, journalled by #1767 with cmd 'compile'. (b) No: iterations unchanged, MCP compiles reported beside the count.

Recommendation: (a), as now written in item 5: without it the Calor arms that follow the template are understated against C#. #1767 already implements either answer.

### Q10. #1770 (What is produced), ruling 2, #1765

Ruling 2 and #1765 say a registration is recorded in Annex A and nowhere else, but #1770 produces three artefacts: the annex entry, a hashed machine-readable thresholds file read by #1760, and a spend record modelled on pp-w5-spend-authorisation.md. Is that permitted?

Options: (a) Amend ruling 2: the annex entry is the registration; a hashed machine-readable copy and a separate spend-authorisation record are permitted when the entry cites them. (b) Fold thresholds and spend authorisation into annex entries only, and have #1760 parse the annex.

Recommendation: (a). The file already states that the entry governs where the thresholds file differs, and the spend record must be appendable by #1605, which a frozen annex entry is not.

### Q11. 0.31 decisions list, #1770 item 13, #1736

The milestone decision line says the registration 'covers the paid leg of #1736'. Is that leg authorised by the first signature, or only by a later entry?

Options: (a) Only through a later entry under the same registration, signed after #1736 closes, with a ceiling inside the unspent remainder. (b) Authorised by the first signature with its own ceiling.

Recommendation: (a), as item 13 now states explicitly; the task subset and cost of the end-to-end leg are unknown until #1736 closes.

### Q12. #1694

V2: The run date (2026-10-28), commit 3c04f111 and run 40691314916 cannot be checked from the repository. If the date is right, the artifact holding nine of the ten failures expires about 2026-11-27, three days from today, which is before this plan is likely to be filed.

Options: (a) The maintainer downloads and stores the artifact of run 40691314916 now, independently of filing the plan. (b) Wait for #1694 step 1; if the artifact has expired, the nine other failures are recovered only from the job summary, or by bisecting on pre-0.30 commits.

Recommendation: (a). The issue text holds either way: step 1 records the date first and says to keep a copy that outlives the retention.

### Q13. #1694, #1769 (decision 9), #1781

V3: Is the `sweep` job a required status check on main? It is not visible in the repository.

Options: (a) Not required: step 0, the port and the unfrozen-fixture interval merge over a red, non-blocking check. (b) Required: each of those merges needs a maintainer override, or #1769 decision 9 takes option (b) (port and freeze in one pull request).

Recommendation: Check branch protection before #1769 is decided; keep decision 9 option (a) with an override if it is required. The issues are written to hold either way.

### Q14. #1769 (decision 10)

V5: Confirm that decision 10 (the two baseline manifests) and the requirement that the record define the successor's registration fields belong in the design record, and the recommended option.

Options: (a) Keep both manifests and regenerate them with derive-manifests.py (needs the v0.22.0 and 0.21.0 commits in the clone; no harness change). (b) Replace them with one row list without blob pins (report, three tests and the script change on the 0.31 exit chain).

Recommendation: (a), as the issue recommends. The port cannot pass CaseIdsAreUniqueAndMatchEachManifest without one of the two.

### Q15. #1783 (decision 2), #1662, #1843, #1853, #1703

C10: Is a published count of obligations with a `proven` column (the proof-yield table, the #1703 inventory) a "proven" claim that requires the proof-claim streak?

Options: (a) Yes: the 0.35 release waits for the proof-claim count after its last verdict-changing freeze, and the 0.36 exit record states the streak. (b) No: only prose that calls user-code contracts proven, verified or discharged triggers the count; the tables publish without it.

Recommendation: (a). #1783 now writes (a) as the plan's recommendation inside decision 2 and requires the release-policy section to state whichever answer the maintainer gives.

### Q16. #1782, #1779, #1746, #1647

Does 'default calor migrate' on a multi-targeted corpus library mean with one declared framework selected (--framework <tfm>) or with no selection? The two give different native counts, because without a selection configuration-sensitive files are preserved exactly (docs/cli/migrate.md:221).

Options: (a) One declared framework is selected for a library that declares several, and the command is recorded in bench/corpus/README.md; every later reader uses that recorded command. (b) No selection ever: counts are lower on multi-target libraries but the command is literally the default.

Recommendation: (a). #1782 and #1779 are written this way: #1782 records the exact command per project and #1779 runs the recorded command, so both hold under either answer; only the recorded command changes. State the rule once in docs/plans/roadmap-0.31-0.39.md so #1746 and #1647 compare like with like.

### Q17. #1728, #1759, #1749

What happens to #1728: close as completed, narrow to timing one release and correcting the policy record, or leave as filed? The promotion design is implemented and published 0.30.

Options: (a) Close now as completed: no cost, but docs/release-policy.md keeps an estimate and a stale 'Activation constraints' paragraph that no issue owns; the #1749 dependency lapses and ', #1728' is dropped from #1749's closing line. (b) Retitle to '[0.31] Release: time one release on the promotion path and correct the policy record': no engineering, stays open until the first release after #1749 closes (normally just after 0.31.0), and needs the date reviewers were set on nuget-release from the maintainer. (c) Leave as filed: an agent would re-implement a merged design.

Recommendation: (b). The file holds either way: both the closing comment and the replacement body are written out, and the choice is recorded in the #1759 sign-off. The row has no new_title, so under (b) the retitle is applied by hand at sign-off.

### Q18. #1590

Should #1590 keep the title '... memory growth is capped by sharding, not fixed'? scripts/compiler-shard-filter.sh:8-16 records the 0.29 leak sources as repaired; what is open is verification at the 0.30 inventory and the shard p90.

Options: Keep the current title with only the [0.31] prefix (as filed now); or retitle to something like '[0.31] Compiler test host: verify the 0.29 memory repairs at the 0.30 inventory and bring the shard p90 under 30 minutes'.

Recommendation: Retitle. The amended body already says this, and the current title sends a reader looking for an unfixed leak. It needs a new_title on the row; this file uses whatever the row gives.

### Q19. #1749, #1728

Will the maintainer dispatch the three consecutive publish-nuget.yml dry runs that close #1749 before the 0.31.0 candidate is chosen? Each repeats the release validation (36.62 elapsed minutes and 122.22 job-minutes at v0.29.0, docs/release-policy.md:105).

Options: Dispatch three dry runs on main commits that are not the candidate, so #1749 closes before 0.31.0; or dispatch none, ship 0.31.0 with its reruns recorded on #1749, and move #1749 to 0.32 (then #1728 option (b) times a later release).

Recommendation: Dispatch them once the three fixes have landed, on non-candidate commits. The file holds either way: #1749 is written as a release-process prerequisite that may slip with the reruns recorded.

### Q20. #1647, #1731, #1802

Is 'the migrated corpus libraries build with effect enforcement at its default' an absolute closing criterion for #1647, and so for the 0.32 exit, or may effect diagnostics that epic #1808 owns remain as a recorded exception?

Options: (a) Absolute: exit code 0 with CalorEnforceEffects on; the 0.32 exit may then wait on effect-precision work in #1808. (b) Exit code 0, or every remaining error is an effect-enforcement diagnostic listed by code and file and assigned to a named open #1808 issue.

Recommendation: (b). The issue is written to hold either way: the criterion is exit code 0 unless the maintainer records such an exception in the issue.

### Q21. #1647, #1779

Must #1647 run the corpus subjects' own test suites against the project-mode migrated libraries? #1779 puts that out of its scope and points at #1647, but no row owns it.

Options: (a) Yes: add it as a part and a done-when bullet of #1647, which enlarges a 0.32 exit criterion. (b) No: test evidence stays the two round-trip legs and the member oracle, and the pointer in #1779 is removed or turned into a named follow-up.

Recommendation: (b). The issue now says the subjects' suites are not run under it unless the maintainer adds that as a part, and the epic lists the work as a follow-up with no issue.

### Q22. #1800, #1779

Who produces the static opportunity count (enum-typed match targets without a catch-all; null-gap sites) that #1800 reads before the cut-off? No row owns it and #1800 is decision-only.

Options: (a) #1800 carries a small labelled counting script or recorded grep commands over the corpus job's migrated output; (b) the counts become columns of the corpus report job #1779 (as #1746 and #1802 add theirs), which changes a 0.31 row; (c) drop the static count and decide on the #1605 reading alone.

Recommendation: (a). The issue is written to hold either way: the record carries the commands that produced the counts, cites columns if the job has them by then, and falls back to a hand run of calor migrate.

### Q23. #1785, #1645

Part C: a respelling toward C# is worse on tokens by design, so the combination rule in B (clear M on the primary and not worse on any other member) can never admit it. Is a C2 respelling exempt from B's rule and bounded only by the per-occurrence cap?

Options: (a) Exempt: bounded by the cap on the primary tokenizer, costs on other members recorded; (b) not exempt, which makes C2 unusable; (c) a separate cap per tokenizer.

Recommendation: (a). The body states the conflict and marks the exemption as the plan's recommendation inside the maintainer decision; it does not assume the answer.

### Q24. #1800

Is 'newly allocated reference array slots can hold null' (CHANGELOG.md:1913-1914) a fifth null-gap row?

Options: Add it as a row with its own outcome, or record it as out of scope of the null remainder.

Recommendation: Record it as a row with outcome 'opt-in' like the other gaps; the body leaves it as a question the record must answer.

### Q25. #1814

After `calor effects suggest --merge`, should the Calor0411 prompt for an unfilled call persist? Under (a) the entry is written as ["unknown"], the call then resolves, Calor0411 stops and a later `suggest` no longer lists it; only Calor0410 on pure callers remains. Under (b) the live manifest is never touched and Calor0411 keeps firing.

Options: (a) write ["unknown"] for every unfilled entry, in the merged manifest and the template; (b) --merge adds no unfilled entry to .calor-effects.json, which retires --merge in all but name.

Recommendation: (a): it keeps the to-do list in the file the user edits and uses the code the built-in manifests already use. The issue is written so either closes it; the pull request states the choice.

### Q26. #1731, #1784, #1805

For the members of each of the five groups that cannot be given exact pure entries, which outcome is recorded in the window record before the cut-off: (a) nothing remains, (b) an explicit effect callers declare, or (c) the contract stays for this edition as a documented assumption?

Options: (a) per-overload analysis only, rejects nothing; (b) an existing or new effect code, rejects today's programs, lands in #1803, and a new code goes through the #1785 rule and the payload manifest; (c) documented hole, revisited under a future edition, the #1084 opt-in, or a soundness exemption if #1805 adopts one.

Recommendation: (c) for the remaining members of each group unless the acceptance figures show (b) loses no native conversion and does not raise the density ratio; a group with no recorded outcome at the cut-off takes (c).

### Q27. #1816 (Part B, 'Too few valid runs to decide')

What validity floor applies to a regressed pair in the #1645 re-run? #1770 as drafted registers none, so the 8-of-10 floor here is the plan's own proposal.

Options: (a) 8 valid runs of 10 per arm, as written; (b) all 10 valid after retries; (c) a floor registered in #1770, which this issue then inherits (the text already says such a floor wins).

Recommendation: (a), 8 of 10. The body marks it as recommended and holds under any of the three.

### Q28. #1816 (outcome S, 'specifications were present'); #1866; epics #1662, #1861, #1872; #1698

If the replication reads 'specifications were present', is the consequence the one written: #1866 held (not closed), the #1662 exit report counting only Proven obligations as a difference from equipped C#, #1872 unchanged, and the #1698 hypothesis narrowed to unmatched properties, earliness of catch and specification cost?

Options: (a) adopt as written; (b) keep every 0.35 row and narrow only the #1698 hypothesis; (c) a wider cut of #1861. Whichever is chosen must be in the registration before the first paid run.

Recommendation: (a). It is consistent with how #1662, #1861, #1872 and #1698 read the registration, but nothing in the repository can confirm it; the text is marked as recommended and the registration 'adopts or amends it before it is merged'.

### Q29. #1816 (Part A threshold and expected outcome); #1813

The d >= 3 threshold is close to decided by the fixtures: the three contract defects surface wherever a check exists and the six effect pairs are probably unmatched in C#, so the expected reading is d = 2 whatever the agent does. Keep the threshold as the primary reading?

Options: (a) keep d >= 3 and lead every publication with the split by defect class and by matched or unmatched pair (now written into #1816 and #1810); (b) register the per-class split as the primary reading and d as secondary; (c) decline Part A if #1813 finds no public purity analyzer, since the equipped arm then differs from conventional C# only on three pairs.

Recommendation: (a). Whether a public per-function purity or transitive-effect analyzer exists was not checked; #1813's baseline now says so, and its record reports the matched count before Part A is signed.

### Q30. #1818

Which budgets apply to the CompileCalor task time after edits (a), (b), (c) and to the in-process segment of calor check on the 106-file project? Only the MCP write budget (PP-L1) is registered today.

Options: Accept 1 s for each; state another value with its reason; or leave a path without a budget, which leaves #1836 with no gate on that path.

Recommendation: 1 s each: it is the only warm-feedback bound the project has registered (the P99 bound of PP-L1), placed on the part of the wait Calor code controls. The issue requires the maintainer's comment before the first CI figures are read, or the record says the budgets were set afterwards.

### Q31. #1809

Should the write-hook exemption match the tests directory segment ordinally (only `tests`) or case-insensitively (also `Tests`)? calor check finds Tests/ on a case-insensitive file system, and the validator has no project root.

Options: Ordinal: Tests/ stays blocked and the messages name tests/. Case-insensitive: Tests/Foo.cs is allowed, at the cost of exempting a directory that calor check does not find on Linux.

Recommendation: Ordinal. The issue is written to hold either way: if the maintainer asks for case-insensitive matching before part 1 merges, one test case moves from blocked to allowed and one Not in scope entry is dropped.

### Q32. #1732

When #1732 is closed, should the already drafted additive text for section 7 of F-3' (docs/plans/2026-11-16-milestone-0.30-ci-triage.md:385-393, marked "not applied") be applied to docs/plans/v0.13-freeze-registrations.md?

Options: Apply it in the release-note correction pull request, which discharges criterion 5 as "revisited, caller still recompiled"; or leave the section unchanged, in which case the closing comment is the only record that criterion 5 lapsed.

Recommendation: Apply it. No issue builds the row-only reuse tier, so otherwise criterion 5 lapses with no written outcome in the registration document. The closing comment template carries a placeholder for either choice.

### Q33. #1822

Which procedure does an agent follow to change converter-preserved C#, and through which single entry point?

Options: A: change the C# and convert again (through calor_convert over MCP, or calor convert / calor migrate on the command line). B: an MCP operation rewrites the raw payload (as a parameter of an existing tool; the always-on MCP surface has 100 tokens left).

Recommendation: A, through an existing MCP tool where possible so that no .cs file has to be written. The issue names one entry point and documents what the other does with the file; it does not make the receipt handling of the two agree.

### Q34. #1820, #1816

Do the maintainer-run client capability sessions of #1820 (vendor model calls; at most ten prompts and $5 in total) need an entry in the 0.32 spend record #1816, which covers only the replication and the #1645 re-run?

Options: (a) No entry: a maintainer-run check of this size is authorised by the maintainer running it, under the limits in #1820. (b) Add one line to #1816 acknowledging the sessions and their ceiling.

Recommendation: (a). #1820 now states that #1816 does not cover these sessions and that, if the maintainer lists them there, the lower limit governs, so the issue holds either way.

### Q35. #1824, #1805

Does the edition compatibility promise cover MCP tool names, so that merging or renaming a tool in the #1824 reduction branch after the edition is declared counts as a breaking change?

Options: (a) Not covered: tool renames are listed in release notes as tool-surface changes. (b) Covered: tool renames after the declaration wait for the next edition, which leaves only schema and description shortening (option a of the reduction target) available.

Recommendation: (a), recorded as a scope point of #1805. #1824 already requires the pull request to state whether the promise text covers MCP tool names and to follow it, so it holds either way.

### Q36. #1084, #1800, #1805 point 3, #1869, #1871

What does a module write to opt in to a rejecting check: a later edition, or a named opt-in on the marker line inside one edition?

Options: (i) Opt-in means declaring a later edition, so any opt-in check waits for a second edition (new marker value, rewriter rules, compiler accepting both). (ii) The marker line carries named opt-ins inside one edition; the spelling is proposed in the #1084 design record and added to the window payload manifest as an additive form; cost is one more form to teach and two behaviours to test per opt-in.

Recommendation: (ii), because the opt-in checks the plan anticipates (#1800 opt-in outcomes, the 0.35 checks) come before any second edition is planned. The #1084 body is written as a decision and #1805 point 3 now reads 'through the per-module opt-in the #1084 design record defines', so both hold either way. Implementation is not in #1084 under either option.

### Q37. #1805 point 6, #1731 outcome (c)

If the soundness exemption is adopted, may it later be used to tighten an assumption that #1731 kept and listed in the effects reference (outcome c), or only an unsoundness found after the declaration?

Options: (a) Listed assumptions may be tightened under the exemption, with a release note and suggested fix. (b) Only unsoundness found after the declaration; a listed assumption changes only in a new edition.

Recommendation: The file records no recommendation beyond the existing one to adopt the exemption; the answer is now a required field of the declaration record. #1731 names the exemption as one route to the later fix, which is only coherent under (a).

### Q38. #1084 optional-or-required, #1806 rule contract, #1645 step 3

If a window row gives an existing spelling a new meaning (for example a var or header => respelling), is the marker made required so the rewriter can tell 0.30 source from migrated source, or does it stay optional with the rule running only under an explicit 0.30-source option?

Options: Required marker: inserted into about 970 .calr files and every embedded module by #1803, converter must emit it. Optional marker: the rule runs only with an explicit option and refuses files it cannot classify; users must state that their source is 0.30.

Recommendation: Optional, with the explicit option (the plan's standing recommendation). The design record must state the answer for both outcomes of any such candidate row, since it merges before the cut-off.

### Q39. #1832 (V4)

What ceilings should the 0.33 paid-run record carry? The recommended ceilings are 1.4 to 1.7 times the upper projection, while the sibling 0.32 record (#1816) uses roughly 4 times. Projections rest on July 2026 Claude Code means (pre-0.30 syntax, unpinned model); no per-run cost exists for Codex, Gemini, Copilot or Rust.

Options: (a) keep $120 per vendor (A1) and $250 (B1); a collection that hits the ceiling stops and is reported incomplete. (b) raise to the #1816 ratio (about $860 for Part A in total and about $720 for Part B). (c) set after recomputing from #1605 realised costs.

Recommendation: (a), recomputed from #1605 realised costs before signing: a low ceiling costs runs, not money, and this program has overrun twice. The body holds for any value the maintainer writes.

### Q40. #1832 Part B, #1830, #1831 (V5)

Does the Calor arm of the Rust reading run in the workspace calor init writes (--initialized-workspace) or in a plain harness workspace?

Options: (a) calor init workspace at the #1823 default: Calor as shipped, but needs calor check journalled as an iteration (#1767 item 5 or the same change under #1651) and the mode proven on the new tasks. (b) plain harness workspace: no dependency on that change, but not what an adopter gets; then change the Arms row and drop the initialized-workspace clause from the #1830 done-when.

Recommendation: (a). It is written as a recommended field; the calor-check rule and the #1831 entry criterion are conditional on the registered arm using a calor init workspace, so the issues hold either way.

### Q41. #1830 (V8)

Should the optional file-class tasks (from W3-001-audit-log and W3-002-config-store) be kept, relabelled or dropped? At 3 runs per arm they measured Calor 2,1,1 and 1,1,1 iterations against C# at 1, the same sample size that is treated as 'measured 1.0' for the three parity pairs.

Options: (a) keep as optional with the label 'headroom not measured' plus the stated evidence (current text). (b) relabel 'headroom not established (3 runs)'. (c) drop them: on the only evidence they sit at or near the floor.

Recommendation: (c) unless #1709 wants a file-class task; they are optional, no reading in #1832 uses them, and the body now says so. The label text was kept as the row note gives it, with the evidence added.

### Q42. #1832, #1831 (V10)

Confirm that #1832 and #1831 carry p1 (applied from the row labels) while #1827, -02 and -04, which every route to the Rust exit criterion passes through, carry no priority.

Options: (a) leave as is. (b) add p1 to #1827, #1830 and #1828.

Recommendation: (b) for #1827 and #1830, which also block #1651, #1709 and #1710; not applied because the row labels give 'tests' only.

### Q43. #1841

The standing acceptance rule says the density ratio must not regress, but this issue replaces `! unknown` with exact rows in what `calor convert` writes, and exact rows are usually longer. Does a rise caused only by effect rows block the issue?

Options: (1) Strict: any rise in the whole ratio blocks. (2) The ratio is measured with effect-row lines excluded and only that figure must not rise. (3) The whole ratio is reported, and a rise attributed to effect rows is accepted by the maintainer per pull request.

Recommendation: Options 2 and 3 together, which is how the issue is now written: both figures are reported, the figure without effect-row lines must not rise, and a rise in the whole ratio merges only with the maintainer's acceptance recorded in the pull request. Under option 1 the maintainer simply withholds that acceptance, so the text holds either way.

### Q44. #1836, #1732

Criterion 5 of #1732 (ES-04 with the caller a cache hit again) and the clause "while the caller is skipped" of its criterion 3 need a tier that spares a caller when only a callee's declared effect row changes. No row builds it, and it contradicts the pinned test CrossModuleViolation_AppearsAndDisappearsWithTheCalleeRow_OnBothSurfaces. Is that tier closed as not planned by design, or kept as an unscheduled item?

Options: (a) Record in the #1732 closing text that criterion 5 is closed as not planned by design. (b) Keep it as a named, unscheduled follow-up with no issue.

Recommendation: (b) as written here: #1819 lists it under Not in this epic and #1836 states in its body and closing comment that it is not built. Whichever is chosen, the #1732 amendment in #1804 should stop saying the criterion continues in #1836.

### Q45. #1744

A member moved by text edit into a class in another file gets a new ID from calor ids sync with no message (by reading Reconcile; sync prints only a count, IdsCommand.cs:52). The original done-when allowed only 'keeps identity' or 'refuses with a clear message'. Is pinning and documenting that outcome enough to close #1744?

Options: (a) Pin and document as a limit; the tool paths (MCP move, explicit relocation) keep the ID. (b) Have ids sync print a line for each dropped and newly minted ID; this is a small behaviour change and needs an owner. (c) Have ids sync refuse; this changes the reconcile rules and would also fire on an ordinary delete plus add.

Recommendation: (a). The issue is written that way and names the report-or-refuse change as a follow-up with no issue. If (b) is wanted, add it to #1744 as a seventh done-when item rather than a new row.

### Q46. #1744

The amendment adds two finish lines beyond the three paths in the title (a CLI test for calor rename on a registered project, and a protocol-level test of LSP capability negotiation). Outcome A of MCP class rename needs TypeReferenceIndex shared into the compiler project, which is probably two pull requests. Accept the widened scope under the current title?

Options: (a) Accept; keep the title; outcome A is delivered as listed parts. (b) Accept and retitle to '[0.33] Sidecar identity through MCP class rename, calor rename, LSP rename and member moves'. (c) Strike items 2 and 3. (d) Split the type-reference indexing into its own row.

Recommendation: (a). Both added paths carry identity and have no test at HEAD, and the body already allows outcome B if sharing the index costs too much. If outcome B is chosen, type rename through MCP and calor rename becomes an unowned follow-up, and the epic body says so.

### Q47. #922

The v5 title still says '(partial types, multi-file modules)', but since 0.30 a root module header is a per-file label and both engines already rename one. Should the title change?

Options: (a) Keep the title; the amendment and Baseline say the module half is retired. (b) Set new_title to '[0.33] Editor rename cannot rename a partial type declared across files — needs identity groups in the workspace index'.

Recommendation: (b). It is a change to architecture.json only; the heading and one sentence of the amendment in this file then follow.

### Q48. #1833; 0.33 exit criterion 3; 0.34 entry criterion 2; #1765 section 4

The 0.33 exit criterion now allows the default "not decided, treated as the fallback outcome until changed", but no file says what that default does. Does it only hold the issues the fallback table holds, or does it also cut 0.34 scope, close or re-scope issues and pick F1 or F2? And does #1833 close on the default line or stay open?

Options: (a) Holds only: issues are held, "demand unproven" is not written, nothing is closed or re-scoped, the 0.34 cut and the F1/F2 choice wait for a merged P or F, and #1833 stays open past the 0.33 release. (b) Full fallback: the default line applies the whole table, including the 0.34 cut and closures, without a maintainer finding, and #1833 closes. (c) Holds plus the 0.34 cut, with closures and F1/F2 waiting for a merged record.

Recommendation: (a), which is what the #1833 body now says. It mirrors the rule the plan of record already gives the #1777 default line (not a maintainer finding, so nothing is closed, narrowed or re-scoped). Its cost is that 0.34 starts at full scope with no adopter until the maintainer merges an outcome, and that an issue marked may_slip: no stays open after the release. If (b) or (c) is chosen, only the Default paragraph and one done-when bullet of #1833 change.

### Q49. #1846, #1825

#1846 depends on #1825, but #1825 may be held when the promise covers tool layout. May #1846 be prepared and merged while #1825 is held and still open?

Options: (a) Yes: the dependency is met by #1825's closure or its recorded hold, and the re-affirmation record carries the dated entry stating the hold and what the 0.33 release does. (b) No: #1846 waits for #1825 to close, and the hold entry goes only in the declaration record.

Recommendation: (a), as the file now reads. Exit criterion 6 says the record states what the release does when #1825 is held, and #1819 names the re-affirmation record as one of the two places for that entry. Under (b) a held #1825 would block the edition re-affirmation and with it 0.36 entry criterion 3.

### Q50. #1654, #1659

Should the record shapes of #1654 (record struct, partial record, nested record and the others) get their own registry entries in FeatureSupport.cs, or stay as 'Family shapes' rows of the coverage record that no registry test covers?

Options: (a) Keep as drafted: family-shape rows, native meaning preservation test replaced plus a runtime test (this is what the 0.34 exit criterion now says); the #1850 registry test does not guard them. (b) Add registry entries for the shapes so the registry test and the d-s1.5-fixtures job cover them; costs a registry change, fixture work and a reworded exit criterion.

Recommendation: (a). The exit criterion already defines native for a family shape, and the file is written to that definition.

### Q51. #1844, #1647

Does the #1647 decision on blocking native floors for the corpus report job also govern the per-project density reference values #1844 commits, or does #1844 need its own decision?

Options: (a) One decision: the density ceilings block exactly when the native floors block (as #1844 is written). (b) A separate decision in #1844, so ratios can stay report-only while native floors block.

Recommendation: (a), with one line added to the #1647 decision table so the link is visible from both issues. #1844 holds either way: its test requires a non-zero exit only if floors on the job are blocking.

### Q52. #1659, #1785

Where are spelling decisions for new 0.34 source forms recorded: the 'New source forms' part of docs/plans/0.34-native-coverage.md, or the syntax decision record that #1785 amends?

Options: (a) The coverage record (as drafted; one file per milestone, next to the counts). (b) The syntax decision record, with the coverage record linking each entry.

Recommendation: (a). The children already write their counts there, and a link from the syntax decision record can be added later without changing any issue.

### Q53. #1848, #1854 (V1)

Confirm or replace the planning thresholds that have no measurement behind them: guard census 'go' at 25 liftable guards in at least 2 of 3 projects and 10% of guards in native files, 'drop' under 10, 'not measurable' under 100 guards; venue attempt floor at the August native counts (15/32, 44/110, 74/139) in at least two projects. (The disposal thresholds are no longer the plan's own: #1842 now uses the #1770 item 8 rule.)

Options: (1) Confirm as written. (2) Replace the numbers before filing. (3) Leave them as recommended values; each issue already requires the maintainer to confirm or edit them in a rule commit that precedes any count.

Recommendation: Option 3: the bodies hold either way, because each registers the values in a first commit before counting. Note that liftable is now narrower (it excludes interface implementations, virtual members and custom-message throws), so 25 is harder to reach than when it was first proposed.

### Q54. #1854 (V11)

Should the gate on attempting the venue measurement exist at all? It is shut at HEAD (10/32, 36/112, 58/139 against the August counts), the measurement took about 8 minutes in August, and a shut gate closes #851 with no measurement.

Options: (a) Keep the floor (more native files than August in at least two projects). (b) Remove the gate and always run the measurement once #1647 is closed. (c) Replace the floor with another value.

Recommendation: (b): the measurement is cheap and a measured 'not feasible' is a better record than 'not attempted'. The body now lets the maintainer confirm, replace or remove the threshold in the rule commit that precedes any gen-tasks run, so it holds whichever is chosen.

### Q55. #1834 (V4)

Which conversion-scorecard figure is authoritative: CHANGELOG.md:166-168 (106 inputs, 101 native), the tree (100 inputs in tests/TestData/CSharpImport/manifest.json and BASELINE_FULLY_CONVERTED = 100), or tests/Calor.Evaluation/Scorecard/baseline.json (fullyConverted 96)?

Options: (1) The tree's 100-input manifest and the constant in ConversionScorecardTests.cs. (2) The CHANGELOG's 106-input set, if it exists outside the tree. (3) Leave unsettled; the result names the set it measured.

Recommendation: (1), with a correction to the CHANGELOG or baseline.json filed separately. The body states the discrepancy and requires the result to name the set measured, so it holds either way.

### Q56. #1842, #1770 (C1, C4)

#1842 now reads the #1770 item 8 resource-leak rule with a deviation: an event is a run whose final sources hold a hand-checked leak (not a failed iteration), a run is a valid run on a pair with at least one opportunity in agent-written code, and the rule is read on the Calor arm with the C# arm reported beside it. Is that mapping acceptable, or should Annex A register a separate rule for the spike?

Options: (1) Accept the mapping as a stated deviation in the note. (2) Have #1770 item 8 register the spike's reading explicitly (event defined on final sources).

Recommendation: (1) for this file; (2) is cleaner if #1770 is still editable, and is returned as an other-file request. The 'read on the Calor arm' sentence is my adaptation of item 8's 'on the 0.30 Calor arm'; neither C item stated it.

### Q57. #1643

What does the compiler emit for a union or closed hierarchy when the consumer's language version is older than C# 15 or not supplied? The body records this as an open decision the maintainer confirms on the issue before the second pull request; it cannot be settled here because no SDK is installed (unknown: whether C# 15 union output compiles and runs against .NET 10 framework assemblies, and whether a lowering preserves the behaviour of a converted C# 15 union).

Options: (a) error unless C# 15 or later: no lowering, but entries cannot be promoted while fixture hosts are on .NET 10 (rows close as not-reached / needs .NET 11 hosts, issue stays open past 0.34). (b) lower to ordinary C# types by default, C# 15 syntax when C# 15+ is supplied: two emission paths, promotion possible on .NET 10 hosts. (c) always lower: one path, but a converted union no longer round-trips to a union.

Recommendation: (b), falling back to (a) with the stop rule if no behaviour-preserving lowering exists. The issue is written to hold under any of the three.

### Q58. #1642

Should the title drop 'Roslyn/MSBuild packages, drop unused old test TFMs'? At HEAD there is no old target framework left to drop (350 of 351 projects are net10.0; the one netstandard2.0 project is a Roslyn analyzer), the Roslyn row belongs to #1638 and the MSBuild row to #1855, so the skeleton title describes work the issue no longer contains.

Options: Keep the skeleton new_title (current state of the file), or change new_title in architecture.json to '[0.34] Dependency refresh: System.CommandLine beta4 → stable; no prerelease dependencies' and update the heading, meta and amendment line here.

Recommendation: Retitle; the body already says only the prefix changes, so it is a three-line edit once the skeleton row changes.

### Q59. #1851, #1661, #875

Does a dominating dereference (an earlier read of xs.Length on every path, or the indexed access itself) count as establishing an array non-null? Without it, unguarded array parameters stay Assumed and the lift is likely to move few index-bounds obligations on converted code, so #1661's 'proven for arrays' could be near-empty.

Options: (a) No: only creation expressions, kept null guards and immutable lets establish non-null. (b) Yes for arrays only, with three extra unsound controls and section 4.1 recording why strings differ. (c) Yes for arrays and strings, with the controls in both forms and the #875 tests updated.

Recommendation: Leave it as a decision inside #1851 with default (a). The issue now requires tracing which guard a discharged index-bounds obligation removes before deciding, and a yes must either extend to strings or record the difference. The body holds under all three answers.

### Q60. #1660, #1851, #1860

List<T> is a user-type sort and no issue lifts its demotion, so Count, Contains and indexing on lists can only report Assumed in 0.35. Is that acceptable for the milestone?

Options: (a) Accept: lists stay Assumed in 0.35 and the lift is a named follow-up. (b) Widen #1851 to lists, which needs heap-state reasoning for Count. (c) Fold it into the #1860 reference-null decision.

Recommendation: (a). The #1660 amended done-when already states Assumed for every list operation, and the epic body names the follow-up under 'Not in this epic'.

### Q61. #1662, #1660, #1661

With #1660 and #1661 sequenced last and both able to change registered verdicts (rows STR-UNMODELED-OPS, CALL-IN-CONTRACT, OBL-UNGATED-FORMS, BODY-LOOP-CALL-MUTABLE, OBL-LOOP-VARIABLE), the 0.35 release date is their last verdict-changing freeze plus the proof-claim count in days. Should those rows be moved in one early pull request per issue, or may the release wait?

Options: (a) Each issue lands its row-changing pull request first, as early as its dependencies allow (current text). (b) Batch all verdict-changing freezes of the milestone into one re-registration. (c) Accept a later release.

Recommendation: (a), as written in Sequence step 6. (b) would couple five issues into one freeze and is not required by the release policy.

### Q62. #1866, #1847

How is a switch-on conversion labelled and judged? The loss-ledger entry that records a lift would, as the repository stands, waive member-oracle differences in that file (OracleRunner.cs:293, :399-402), remove the file from the native count, and (if the new kind is a semantic loss) make Lossless conversion refuse the file.

Options: (1) #1847's record states: a new ConversionLossKind that is not a semantic loss, oracle differences counted before the loss-recorded waiver for lifted files, and switch-on acceptance as pass counts equal to switch-off with native fraction reported, not gated. (2) A fidelity value of its own for switch-on runs. (3) Leave it to the #1866 pull request.

Recommendation: Option 1. #1866 is written with exactly that default and gives the #1847 record precedence, so it holds whichever way this is answered.

### Q63. #1858

Should the execution check of the two safe controls (step 1) be held in 0.35 even though the row may slip? SafeEscapedCommand is very likely exploitable (simulated, not run on .NET), so a registered benchmark control may be mislabelled as safe.

Options: (a) Keep the whole row slippable, with step 1 allowed as an early separate pull request (as now written). (b) Make step 1 non-slipping, which needs a row or may_slip change.

Recommendation: (a) as written, with the plan's stated recommendation that step 1 lands in 0.35; (b) only if the maintainer wants the benchmark label corrected as a release condition.

### Q64. #1866

The title says 'in calor convert', but the option must also exist on calor migrate, the round-trip harness commands and as a dispatch input of the corpus report workflow for the ruled acceptance and report. Keep the title or widen it?

Options: Keep '[0.35] Opt-in guard lift in calor convert (conditional)'; or retitle to '... in conversion (calor convert, calor migrate)'.

Recommendation: Keep the title; the body lists the full plumbing.

### Q65. #1860

Confirm or replace the thresholds of the recommended rule (choose B if N1 is at least 20% and N2 is at least 25). They have no measurement behind them and N2 is published in 0.34 before the rule can be committed.

Options: Confirm; replace with other values; or register the N2 threshold in the #1848 registration document so it precedes the census count.

Recommendation: Confirm in the decision record, which already states for each input whether the rule preceded it; registering N2 in the census registration is cleaner if the maintainer edits that document anyway.

### Q66. #1869, #1871, #1870; 0.35 exit list

All three rows are slip: false, yet each has a hold branch that moves it to a later milestone without holding the 0.35 release, and the expected first readings are "not measurable". How should the plan say that a held conditional row does not block the 0.35 exit?

Options: (a) Add a sentence to the 0.35 exit list: conditional rows of #1872 are closed or held at exit, and a held row does not block it. (b) Set slip: true on #1871 and #1870 and keep #1869 on the chain as a named milestone decision. (c) Leave both as they are and rely on the sentence now in each issue.

Recommendation: (a). The file is written to hold under any of the three: each issue says recording the hold is all the 0.35 close requires.

### Q67. #1871

The gate has a second condition that the row note does not: at least one counted missing-case event must be over an enum, because events over int, string or type patterns cannot be caught by this check. Keep it, or let the class threshold alone open the gate?

Options: (a) Keep the condition and add it to the row note. (b) Strike gate condition 2 and the matching done-when bullet; the class threshold alone opens the gate.

Recommendation: (a). Without it the gate can open on defects this check does not address, which is the mistake the April 2026 post-mortem describes. The condition is kept in the file.

### Q68. #1869

Option A applies the registered null-class rule per gap. #1770 states that rule in runs (events in at least 5 distinct runs and 10% of runs; at least 30 valid runs on pairs with an opportunity; at least 5 static opportunities across the pair set). Is reusing the class numbers unchanged for a single gap what the maintainer wants, given a single gap will rarely reach 5 reference-solution sites?

Options: (a) Reuse the class rule unchanged per gap, as the file now says; gaps with few sites stay held. (b) Have #1770 register a separate per-gap minimum. (c) Decide by class (Option B in the issue).

Recommendation: (a). It adds no threshold after the registration, and the issue already presents A, B and C as the maintainer's decision.

### Q69. #1706, #1701

#1706 (keep row, no text filed) has an implementation agent produce the pilot improvement with inference cost recorded under the #1701 cost ceiling, but its body carries no paid-run terms (money ceiling, pinned model, abort triggers, zero-cost dry pass). Is that agent work ordinary-session authoring, or a paid run that needs those terms?

Options: (a) Ordinary-session authoring, as #1843 treats agent-authored contracts: #1706 stays keep, no clause. (b) Paid run: change #1706 to amend and add the paid-run clause (ceiling, abort triggers, null-agent dry pass, one pinned model, readings written in advance, filing authorises no spend).

Recommendation: (a), with the ceiling the maintainer and the external maintainer agree in #1701 acting as the spend limit; it is a single delivery, not a measured collection. The keep paragraph now states the gap, says the maintainer settles it when the #1701 ceiling is agreed, and says keeping the issue authorises no spend, so it holds either way.

### Q70. #1765, #1697, #1702, #1703, #1704

Under the adopter fallback, are the pilot-independent parts of #1702, #1703 and #1704 delivered in 0.37 (ahead of any collection) with the three issues left open in 0.36, or are the issues moved on the tracker?

Options: (a) Delivered in 0.37, issues stay open in 0.36 (what #1765 section 4 proposes, accepted by approving its pull request). (b) Move the three issues to 0.37 on the tracker. (c) Split each into a closable pilot-independent issue and a held pilot issue (new rows).

Recommendation: (a). #1697 now says exactly this in the epic body, the #1697 amendment and the #1702 and #1703 Dependencies text, each citing the fallback table as the authority, so a row the maintainer changes when reviewing #1765 needs only the parenthesis '(in 0.37, ahead of any 0.37 collection)' changed in those four places.

### Q71. #1867

Should a decline of the collection budget (option D, or the dated default) also be appended to Annex A of docs/plans/agent-native-gates.md, or recorded only in docs/plans/0.37-collection-spend-authorisation.md?

Options: (1) File only: the decline is recorded in the spend record and linked from #1698; the annex holds designs only. (2) Also in the annex: a one-line revision-log entry naming the record, as #1816 does for a Part A decline.

Recommendation: File only, because a decline has no design to pre-register and its consequences are already written in #1698 and #1712. The issue holds either way: part 10 says the same pull request appends a one-line entry if the maintainer wants declines in the annex.

### Q72. #1865, #1857, #1867, #1713 (#1699)

Should new decision records under docs/plans/ be named 0.NN-... or v0.NN-...? The repository's existing files use the v prefix (v0.24-evidence-contract.md and others); the rest of this plan uses the unprefixed form.

Options: (1) Unprefixed everywhere in the plan (0.37-methods-review.md), matching the 0.31 to 0.36 records and 0.31-toolchain-and-sweep-record.md. (2) v-prefixed everywhere, matching existing repository files; every other epic file would then change.

Recommendation: Unprefixed. #1698 now uses 0.37-task-supply-inventory.md, 0.37-methods-review.md and 0.37-collection-spend-authorisation.md; #1699 is the only file still on the v prefix.

### Q73. #1718, #1700

Should #1718's own body say where the investment decision is recorded? The amendment to #1718 is limited to one Dependencies replacement, so the venue (a document under docs/plans/, linked from a comment on #1718, nothing under docs/plans/evidence/) is now stated only in the #1700 epic body, in the first bullet of "Conditional work and delivery gate". Someone handed only #1718 will not see it.

Options: (a) Leave as is: the venue lives in the epic. (b) Allow one more Work line in the #1718 amendment with the same sentence and the two precedents (docs/plans/call-w-adjudication.md, docs/plans/safe-delegation-m0/decision.md).

Recommendation: (b). It is one pasted line and makes #1718 stand on its own. The file is correct either way.

## Questions raised by the cross-file reviews

Some of these were settled by rulings applied in the final pass; they are listed so nothing is lost.

### Q74. #1766, #1605, #1815

Which issue owns the driver that runs the four interleaved arms of #1605 stage 1 and the five of stage 2? None does today: `run-epoch.sh` at bc80daeb runs two fixed arms one after the other and has no spend stop.

Options: (a) #1766 gives `run-epoch.sh` an arm list with interleaving (requested above); the spend stop stays manual in 0.31. (b) Part 3 of #1815 (the driver with `--spend-ceiling`) is pulled into 0.31. (c) The operator writes a one-off loop and #1770 records that, with abort triggers checked by hand.

Recommendation: (a). It is the smallest change, keeps the 0.31 exit chain inside #1761, and #1815 can then build its spend stop on the same arm list instead of a second script.

### Q75. #1770, #1816, #1789, #1869, #1871, #1842

How do the null, missing-case and resource-leak classes ever become measurable? The registered floor is 30 valid Calor-arm runs on pairs with opportunities; the only such pairs get 20 runs.

Options: (a) Raise the #1789 pairs to 15 runs per arm in Part B (about $48 more at July means, inside the $600 ceiling). (b) Lower the floor in #1770 item 8 to 20 runs. (c) Leave both and accept that epic #1872 stays held through 0.35.

Recommendation: (a). The floor was set before any data, and lowering it to fit a later run count is the after-the-fact move the registration is meant to stop.

### Q76. #1842, #1770, #1760, #1870

Which measurement gates the must-dispose checker: the registered event rule (a failed iteration or escaped defect tagged resource leak) or the spike's static count of leaks in agent-written final sources? A leak rarely fails a test, so the first will read near zero whatever agents do.

Options: (a) The spike's static count, read against the registered run floor and rates, stated as a deviation (requested above). (b) The registered event rule only; the spike's count is descriptive. (c) #1770 registers a separate rule for static leak counts.

Recommendation: (a), or (c) if the maintainer wants the deviation written before #1605 runs. (b) makes #1870 unreachable.

### Q77. #1770, #1824, #1651, #1832

If the stage 2 MCP reading is Not resolved, is the MCP-against-CLI-only question measured again anywhere? Two issues say #1651 does it; #1651 runs nothing paid and #1832 has no CLI-only arm.

Options: (a) Not re-measured; both issues say so (requested above). (b) #1832 Part C lists a CLI-only arm for Claude Code on the two penalised pairs, with its own ceiling and reading.

Recommendation: (a) by default, with (b) left to the maintainer as a Part C line. The ratchet already holds the surface from growing.

### Q78. #1770, #1816, #1645

If the close rule is Not resolved, should the #1645 re-run always include the two penalised pairs so the rule is read a second time?

Options: (a) Only when #1605 filed them as regressed (requested above). (b) Always add both pairs with `calor` and `csharp` arms at 10 runs: about $96 more at July means, which with the 15-run change takes the Part B projection to about $510 against a $600 ceiling.

Recommendation: (a). (b) leaves too little room under the ceiling in a program with two recorded overruns; the maintainer can raise the ceiling with a new decision line if the second reading matters.

### Q79. #1767, #1770, #1651, #1832, #1831, #1835

When #1770 registers no stage 2, #1767 is closed as not planned, but its `calor check` journal record and primer-read log are still needed by every 0.33 run in a `calor init` workspace. Who builds them then?

Options: (a) #1651 builds both when #1767 was closed (requested above; #1831 and #1835 then need #1651 merged first). (b) #1767 keeps items 3 and 5 on every branch and only items 1, 2 and 4 are gated on stage 2.

Recommendation: (b) is cleaner, because #1831 and #1835 do not depend on #1651, but it changes the scope of a gated issue, so it is the plan owner's call. The requests above implement (a).

### Q80. #1770, #1765, #1605

Under option D (no paid run), where is the list of rows that read 'not measured' kept? The 0.31 exit criterion says 'the fallback table of the plan of record (#1765)', but #1765 only has the adopter fallback table and its 0.31 to 0.32 edge has no third branch.

Options: (a) The #1770 spend record lists the rows and the exit criterion points there. (b) #1765 adds a 'no paid run' fallback table to the plan of record.

Recommendation: (a). The decision that creates the situation is the natural place for its consequences, and it needs no change to #1765.

### Q81. #1084, #1800, #1869, #1871, #1805, epic #1792

What is the per-module opt-in, and who builds it? The promise, #1800 and two 0.35 issues treat 'behind the per-module marker' as a landing form distinct from 'a new edition', but #1084 implements the first edition only and no row implements an opt-in.

Options: (a) Opt-in means declaring a later edition: then 'opt-in' and 'new edition' are one form, and #1800 can only offer 'warn' for this edition. (b) Named opt-ins on the marker line inside one edition: #1084's design record fixes the spelling in the window, and the first 0.35 issue that needs it implements it. (c) As (b), with #1084's second pull request implementing the hook now.

Recommendation: (b). The spelling is frozen with the batch, no capacity is spent unless a 0.35 gate opens, and the 0.35 issues say they carry the implementation. If (a) is chosen, #1800's 'opt-in' outcome and promise point 3 should be reworded to say so.

### Q82. #1805, #1731, #1846, epic #1808

If the soundness exemption is adopted, may it be used to tighten a contract that #1731 knowingly left as a documented assumption (outcome c), or only an unsoundness found after the declaration?

Options: (a) Only what is found later: listed assumptions wait for a new edition or the opt-in. (b) Listed assumptions too, with the same release note and suggested fix. (c) No exemption at all.

Recommendation: (b), with a notice period of one minor release for a listed assumption. #1731 recommends (c) for most groups, so under (a) the known holes last the whole edition, which is the cost the exemption exists to avoid.

### Q83. 0.31 exit, 0.32 entry, #1645, #1790, #1785, #1800, #1765

What does 0.32 do if the 0.31 exit was met by 'no paid run authorised'? The 0.32 entry criterion and four chain issues read a published #1605 report.

Options: (a) 0.32 proceeds: part C of the rule is unavailable, #1800 reads 'not measurable', #1645 and #1790 record 'no report', and the #1645 re-run covers only the #1789 pairs or is declined. (b) 0.32 does not open until a run is authorised.

Recommendation: (a), written into the fallback table of the plan of record, because the window's other decisions do not need the report and the edition gate already accepts a recorded decline.

### Q84. #1805, #1784, #1846

Under a deferred declaration (option C), is the breaking window open again?

Options: (a) No: cut-off deferrals stand; only forms named in the deferral record may change, each under the standing rule plus a repeat of the #1807 procedure. (b) Yes: any row may be reopened until the declaration, with a second rewrite that no issue owns today.

Recommendation: (a). It keeps 'one rewrite' true for everything except named forms and keeps the #1846 evidence base valid.

### Q85. #1735, #1807, #1805, #1846

What happens to a 'land' row that is not merged when #1807 is ready to post the commit?

Options: (a) It blocks #1807. (b) It is re-recorded as deferred to a future edition. (c) It merges later and the declaration names a later commit, with the #1807 checks repeated on it.

Recommendation: (a) for #1735, because the promise would otherwise be written over two refinement behaviours; (b) for any other row. Avoid (c): the paid runs would have pinned a commit that is not the edition.

### Q86. #1645, #1785, #1806, #1803, #1084

May a window respelling give an existing spelling a new meaning (`var` as inference, header `=>` as an expression body) while the marker is optional?

Options: (a) Allowed, and the marker becomes required so files are distinguishable. (b) Allowed with an optional marker, and the rewriter needs an explicit 'this is 0.30 source' option, so users can silently skip migration. (c) Not allowed in this window: a respelling toward C# must not reuse an accepted spelling with a different meaning.

Recommendation: (c) as the default written into part C of the amended rule, with (a) as the stated price of an exception. A silent meaning change on unmigrated user files is the worst failure the rewrite can have.

### Q87. #1783, #1662, #1843, #1703

Is a published count of `proven` obligations (the 0.35 proof-yield table, the 0.36 pilot inventory) a 'proven' claim that requires the proof-claim count of green runs? #1662 requires the table in the release notes and makes the count conditional on 'if the release notes use the word proven'.

Options: (a) Yes: the 0.35 release waits for the proof-claim count (recommended 14 days) after the last freeze that changed an existing verdict, which is #1660's. (b) No: only prose that calls a user contract proven triggers it; the table ships with a caveat line and the scheduled sweep merely green at the release commit.

Recommendation: (a). The table is the milestone's headline claim about the verifier, and the count exists to stand behind exactly that. Then make #1662's third done-when bullet unconditional and plan the 0.35 release date from #1660's last verdict-changing freeze.

### Q88. #1660, #1662, #1783

#1660 is sequenced last and changes existing sweep verdicts (STR-UNMODELED-OPS, CALL-IN-CONTRACT), so every one of its up to three pull requests can restart the count late in the milestone. Should the parts of #1660 that touch already-registered rows be ordered first?

Options: (a) Leave the order and accept release = last #1660 freeze + count. (b) Require the #1660 pull request for `ToUpper`/`ToLower`/`Trim` and `Math.Abs` to merge first among its groups, right after #875, so that later groups are additive. (c) Split those operations into their own issue.

Recommendation: (b). It needs one sentence in #1660 and none of the operations in question depends on #1851.

### Q89. #1694, #1768, #1769, #1781, #1778, #1783, #1807

The label ruling (no `soundness-sweep` label on the new sweep issues) and the drafted bodies and metadata (all five carry it; #1694 step 0 exists because they carry it) disagree. Which stands?

Options: (a) The ruling: remove the label from the five issues; step 0 stays as a guard. (b) The drafts: keep the label for discoverability, make step 0 mandatory before the issues are filed, and change #1697 entry criterion 2 from 'no open issue carries the label' to 'no open issue titled "Soundness sweep failing on main"'.

Recommendation: (a). It is the written ruling, it keeps #1697's label-based criterion true, and it removes the window in which failure reports land on the wrong issue before step 0 merges.

### Q90. #1851, #875

If #1851 accepts a dereference (`xs.Length` read earlier, or the access itself) as evidence of non-null for arrays, must the same source apply to strings?

Options: (a) One rule: accept or reject for both. (b) Allow a recorded difference with its reason in section 4.1.

Recommendation: (a) with the default no for both in 0.35. A dereference source for strings would reopen #875's controls after it has merged and would be another verdict-changing freeze.

### Q91. #1843, #1662

Under #1816 outcome S, 'the 0.35 exit report counts only Proven obligations as a difference from equipped C#'. Neither #1843 nor #1662's done-when carries that sentence. Should the proof-yield report format change under outcome S?

Options: (a) Add a line to #1662 Done when: under outcome S the release notes state that only the `proven` column is a difference from equipped C#. (b) Leave it to the entry comment on #1662.

Recommendation: (a). It is one sentence, and the entry comment is not where a release-notes author looks.

### Q92. #1848, #1842, #1854 (ruling 2; #1765)

Ruling 2 says every registration in the plan is appended to Annex A of docs/plans/agent-native-gates.md, which CI holds append-only, and lists six issues. The three 0.34 gates register their thresholds elsewhere: the guard census in docs/plans/0.34-guard-census-registration.md, the disposal rule in the first commit of the spike note (or by quoting #1770), and the venue gate nowhere before measurement. Are these registrations in the sense of ruling 2?

Options: (a) Append each as an Annex A entry (the annex-freeze job then enforces 'before, not after'); the per-issue files keep the results only. (b) Keep per-issue files and rely on git log order, and say in the plan of record that ruling 2 covers paid-run registrations only.

Recommendation: (a) for the guard census and the venue gate, since both gate later work on thresholds 'with no measurement behind them' and Annex A is the only place CI protects; the disposal spike already defers to the Annex A entry of #1770.

### Q93. #1779, #1746, #1802, #1848, #1834, #1853, #1866

The corpus report job has a 60-minute limit and is sized by #1779 with zero columns. Five columns (token count, an enforcement-on measurement compile, a Roslyn guard pass, a strip-header probe build per project, a Z3 proof inventory) and a switch-on dispatch are added later, and no issue owns the total wall time or says what happens when the limit is hit.

Options: (a) #1779's column rule requires each column pull request to state the wall time it adds and the new total, with a stated ceiling (for example 45 minutes) above which the column moves to its own job in the same workflow. (b) Leave it to whichever column first times out.

Recommendation: (a); one sentence in #1779 item 4 and one done-when line in each column issue.

### Q94. #1654, #1655, #1656, #1657, #1658, #1742, #1733, #1850, #1643

These issues read the corpus report job in their done-when (standing-rule ratio, newly preserved files, per-file losses) but have no dependency on #1779; only the epic #1659 does. Ruling 5 allows extra depends_on entries 'where the body states a real edge'. Should each carry the edge?

Options: (a) Leave as is: #1779 running is a 0.32 entry criterion, #1647 closed is the 0.34 entry criterion, and the epic carries the dependency. (b) Add #1779 to depends_on of each (and to #1779's blocks).

Recommendation: (a) for the #1659 children, which are all 'Part of #1659'; (b) for #1643 only, because it sits in epic #1852 and inherits nothing from #1659.

### Q95. #1854, #851, #1177, #1859

#1854 step 3 records the instrument condition as 'not met' when step 1 generated no task, and expected eligible supply at HEAD is zero, so a verdict that 'failed on eligible supply alone' (the branch on which #851 waits and #1859 recommends 'pin') is reachable only if the instrument check can pass without an eligible task. Is that branch meant to be reachable?

Options: (a) Evaluate step 3 on any generated candidate bundle (eligible or not), so the instrument condition is independent of eligible supply. (b) Accept that zero eligible supply always fails two steps, and delete the 'eligible supply alone' branch from #1854, #851, #1177 and #1859.

Recommendation: (a): it keeps the three conditions independent, which is what the verdict wording and the #1177 recommendation assume.

### Q96. #1770, #1605, #1809, #1822, #1734, #1788

Stage 2 of #1605 gets its own commit (sub-entry A-1.N.2) and does not hold the 0.31 release, so it may be pinned during 0.32. The registration says what that commit must contain (#1767, #1773) but not what it must not. May the stage 2 commit include 0.32 changes to text the arms read (hook messages, rule text, Calor0006 guidance, a trimmed reference)?

Options: (a) Stage 2 pins the stage 1 commit plus #1767 and #1773 only (a branch if main has moved). (b) Stage 2 pins any main commit that contains them, and the report lists every agent-facing change since the stage 1 commit. (c) Leave it to the maintainer at pin time.

Recommendation: (a), written into item 4 of the #1770 design entry. #1823 applies the delivery rule to the shipped 0.30 text; a stage 2 run on later text measures something the rule was not registered for.

### Q97. #1823, #1811, #1650, #1846

#1811 registers a reading for 'the primer is read in fewer Calor-arm runs than the delivery rule requires' for Codex, Gemini or Copilot, but no issue acts on it: #1823 closes in 0.32 and #1650 lands before #1811 runs. Who changes that agent's calor init default afterwards?

Options: (a) The #1823 record states that a later per-agent reading is applied by a dated entry appended to the record plus a small follow-up pull request, tracked on #1846. (b) #1811 itself flips the default for that agent. (c) Nothing changes until a later milestone; the report only states the reading.

Recommendation: (a). It keeps one record for the default and keeps #1811 a measurement that changes no product behaviour.

### Q98. #1809, #1650, #1825

Three rules about test files have different scopes. The hook exemption (#1809) allows a .cs path with any directory segment spelled exactly tests, at any depth. calor check discovers only top-level tests/*.cs. The repository's own CI guard (scripts/check-calor-first-diff.sh, is_test_source) exempts only paths that begin with tests/ at the repository root (verified). The rule text and primer line say 'under tests/'. Should the agent-facing text say top-level tests/, or should the hook be narrowed?

Options: (a) Keep the hook wide (it has no project root to anchor on), and make the rule text and primer say 'tests/<Name>Tests.cs, directly in tests/' so that what the agent is told matches what calor check runs. (b) Leave the text as 'under tests/' and accept that a file in tests/sub/ passes the hook and is not run. (c) Narrow the hook to a tests segment that is the file's parent directory.

Recommendation: (a). It costs no validator change, and the 'No tests found' message #1809 writes already names tests/<Name>Tests.cs.

### Q99. #1855, #1639, #1641

The release workflow repeats the SDK consumer matrix (publish-nuget.yml sdk-consumer) on .NET 10 only. #1641 excludes the release workflow and #1855 extends only the test.yml consumer jobs. Under #1855 option A (releases stay on the .NET 10 SDK) the packages actually published are never consumed on a .NET 11-only machine before the push. Is that intended?

Options: (a) #1855 part 1 adds one Linux x64 .NET 11-only leg to the release workflow's sdk-consumer. (b) State in #1855 that the test.yml legs on locally packed packages are the evidence and the release workflow is unchanged.

Recommendation: (a) with one leg only. The 0.31 exit criterion is worded 'with the released packages', and one leg adds little to a 60-minute release run; if declined, write (b) into the Not in scope list so the gap is recorded.

### Q100. #1833, milestone 0.33 exit, milestone 0.34 entry

What does a non-decision at the adopter checkpoint mean? #1777, #1867 and #1713 each carry a dated default so their milestone is not held. #1833 has none, so an unmerged checkpoint holds the 0.33 release and leaves the 0.34 scope undecided, which the issue itself recommends against.

Options: (a) Add a default line to #1833, written by the agent preparing the 0.33 release: "Adopter checkpoint: not decided, treated as outcome F until changed", with holds applied, nothing closed and 0.34 at full scope until an explicit F. (b) Same default but 0.34 at fallback scope. (c) No default: the 0.33 release waits for the maintainer.

Recommendation: (a). It mirrors the #1777 default the plan owner already ruled on, and it keeps the 0.34 cut and the F1/F2 closures as explicit maintainer acts.

### Q101. #1833, #1765, #1697, #1699, #1700

What happens when consent is withdrawn after outcome P? #1833 says the fallback then "applies from that entry's date", but the plan of record has no withdrawal case. It is undefined for a 0.36 pilot in flight, for a 0.37 comparison already collecting on pilot tasks, and for 0.38 after a positive verdict (#1699 hold case (a) reads only the original checkpoint outcome; #1700 has case (d)).

Options: (a) The table applies only to milestones not yet entered; a 0.37 collection already authorised finishes and is labelled with the withdrawal; 0.38 continues only if the #1697 exit record was posted before the withdrawal. (b) The table applies in full from the withdrawal date, including stopping an in-flight 0.37 and holding 0.38. (c) Leave it to a maintainer entry in the #1833 record at the time.

Recommendation: (a), written as one more bullet under "Rules that hold under either option" in section 4, with a matching hold case in #1699.

### Q102. #1777, #1857, #1867, #1765 (F2 rule), #1708, #1709

A vacant reviewer seat is known from the #1777 record in 0.31, but it is only acted on after #1708 and #1709 are complete (#1857 depends on both) and after #1710 and #1711 (#1867 depends on both). Should the vacancy stop work earlier, and should the F2 rule look at it?

Options: (a) Keep as written: all preparation runs, and the decline is recorded at #1867. (b) Let #1867 record D on the ground of vacancy as soon as #1857 records it, without waiting for #1710 and #1711, and add to the plan-of-record rule "F2 only if the #1777 record names a reviewer or reviewer outreach is authorised and its window is open; otherwise F1". (c) Hold #1709 to #1711 while the seat is vacant.

Recommendation: (b). It avoids building a harness and controls for a collection that cannot start, and it stops F2 being chosen when it is certain to revert to F1.

### Q103. #1765 section 4, #1702, #1703, #1704, #1697

Under F1 there is no 0.37 collection and 0.38 and 0.39 are held, yet the table still schedules the pilot-independent parts of #1702, #1703 and #1704 "in 0.37, ahead of any 0.37 collection". Should they be built with no consumer?

Options: (a) Build them under F1 and F2 alike. (b) Build them only under F2 or the consent path; under F1 they are held with the rest of 0.36. (c) Build only #1702 under F1, because its command also closes the gap in `calor verify --weakening-check`.

Recommendation: (b), unless the maintainer wants #1702's command for its own sake, in which case (c). The plan of record already lists this delivery as one of the three things nobody has decided.

### Q104. #1865, #1857, #1867, #1713, #1717, #1791, #1786, #1833

Record paths use two conventions. The 0.37 and 0.38 decision records are `docs/plans/v0.37-task-supply-inventory.md`, `v0.37-methods-review.md`, `v0.37-collection-spend-authorisation.md` and `v0.38-collection-spend-authorisation.md`; every other record in the plan is `docs/plans/0.NN-...` (for example `0.37-comparison-protocol.md` and `0.37-sizing-and-budget-request.md`). Each path is cited consistently, so nothing breaks, but a reader looking for the 0.37 records finds two prefixes.

Options: (a) Rename the four to `0.37-` and `0.38-`. (b) Leave them; existing repository files do use `v0.NN-`.

Recommendation: (a). Four issues change (#1865, #1857, #1867, #1713, plus the citations in #1699 and #1717), and the plan's own records then sort together.

## Work that no issue owns

Named in the epic bodies under "Not in this epic". None of it is on an exit chain.

- **#1761, near #1763**: A check that enforces the class of the 220 retired-syntax .calr.inc fragments under bench/phase0-agent-native (tasks/ppw-redesign 52, epochs/w-rows-pilot-001 52, epochs/w-rows-001 52, task-candidates/1256 36, buildability/1255 28). Named in the epic's 'Not in this epic' and in #1763's 'Not in scope'.
- **#1761, near #1767**: The rule on whether an MCP calor_compile after an edit is an iteration. No row states it; #1767 records the calls either way. Named in the epic's 'Not in this epic'.
- **#1761, near #1766**: A CI job that runs run-pair.sh against a built compiler. None exists and none is added in 0.31 (already listed in the epic's 'Not in this epic').
- **#1775, near #1770 items 6 and 11**: A second reading when the close rule or the MCP rule comes out 'not resolved'. The #1645 re-run re-reads the close rule only if both penalised pairs are in its pair set (#1816 Part B), and no later run compares an MCP arm with a CLI-only arm unless #1832 Part C lists one. Named in the epic body under 'Not in this epic'.
- **#1780, near #1779**: Running each corpus subject's own test suite against its project-mode migrated copy. #1647 judges tests on the round-trip harness legs and judges the migrated copies only by their build, so the corpus report's `tests` field stays `not-run`. Named under 'Not in this epic' and in #1779 'Not in scope'.
- **#1594, near #1749**: Dispatching publish-nuget.yml (the three closing dry runs for #1749, and the release itself) is a maintainer action that no issue authorises. It is named in the epic body under 'Not in this epic' and as a maintainer action in the epic's Done when.
- **#1594, near #1728**: If #1728 is closed under option (a), no issue owns replacing the timing estimate (docs/release-policy.md:114-119) or the stale 'Activation constraints' paragraph (:121-130). The closing comment names both so the next change to that file (#1765 or #1783) can correct them.
- **#1776, near #1648**: Under option (b), change the published status from "paused by the user" to "closed without a verdict" on three website pages (results.mdx, effect-rows-study.mdx, methodology.mdx), together with the assertions in website/tests/public-claims.spec.ts:214-291 and :751-755. The #1648 closing record must name the follow-up issue; no row owns the edit. #1774 is a possible owner.
- **#1776, near #1648**: If the maintainer wants the six W-00x pair directories removed (77 tracked files, 52 of them .calr): remove them together with the blob-SHA pins, PpWRowsRegistrationTests.cs:781-804, test_ppw_pairs.py:179-190 and the tooling. To be filed as a new issue outside 0.31; no row owns it.
- **#1776, near #1648 / #1763**: Enforce a class for the 220 tracked .calr.inc fragments of the redesigned PP-W experiment. The #1763 self-check reads .calr files only, so the class #1648 records for them is not enforced by any check. #1761 already lists the same follow-up.
- **#1649, near #1647**: Run the corpus subjects' own test suites against the project-mode migrated libraries (#1779 reports tests: not-run and points at #1647, which does not own it). Listed under 'Not in this epic'.
- **#1787, near #1779 / #1800**: Report the static opportunity counts (enum-typed match targets and how many lack a catch-all arm; null-gap sites per gap shape) as columns of the corpus report job. Named in the epic body under 'Not in this epic'; until owned, #1800 takes the counts by commands written into its record.
- **#1808, near #1801**: Effect diagnostics in the editor: the language server does not run EffectEnforcementPass (DocumentState.cs:99-106), so no Calor0410 fix reaches an editor and the preferred-flag change is preventive. Named under 'Not in this epic'; no row owns running the pass in the server.
- **#1808, near #1801 / #1790**: Recording whether the insert-a-row fix site (EffectEnforcementPass.cs:1084) is reachable from 0.30 source text, or is dead code because a public callable without a row stops at Calor0124. #1801 lists it as unreachable in its test if no source reaches it; nobody owns removing or repairing the site.
- **#1812, near #1810**: A second collection after a not-adjudicated or aborted replication. No issue owns it; it needs a new decision line in the spend record and, if a registered value changes, a new Annex A sub-entry. Named in the epic body under 'Not in this epic'.
- **#1804, near #1732 / #1836**: A reuse tier that spares a caller from recompilation when only a callee's declared effect row changes (criterion 5 of #1732, ES-04 "with the caller a cache hit again", and the "while the caller is skipped" clause of criterion 3). #1836 covers body-only edits and states it does not build this. Listed in the epic body under "Not in this epic".
- **#1804, near #1822**: Receipt handling in the MCP tools calor_convert and calor_migrate: both write .calr files with a plain atomic write (ConvertTool.cs:353-361, :678-686; MigrateTool.cs:335, :568) and neither reads nor writes the conversion receipt that calor convert and calor migrate check, so the command-line refresh refuses a file the MCP tool wrote. Read from code, not run. Listed in the epic body and in #1822 Not in scope.
- **#1821, near #1824**: A second measurement of an MCP-enabled arm against a CLI-only arm after stage 2 of #1605 (needed to learn whether a reduced tool surface earns its cost, or to settle a 'not resolved' reading). #1651 is drivers only and #1811 runs C# and Calor arms only; it exists only if the maintainer lists it under Part C of the #1832 record. Named in the epic body under 'Not in this epic'.
- **#1792, near #1084**: Implementing the per-module opt-in mechanism. #1084 only records what a module writes; no row builds it. Named in the epic's 'Not in this epic' as falling to the first issue that ships an opt-in check (an implementation child of #1800, or the 0.35 issues that put a rejecting check behind an opt-in).
- **#1792, near #1803**: No pull-request check runs fixture outcomes under tests/ on main (only registration is checked; outcomes are checked for samples/ alone). The rewrite relies on a manual Tier 2 dispatch compared with the parent commit. A job that runs the tests/ fixture check on pull requests to main has no row.
- **#1819, near #1825**: Test files in an MSBuild project: Calor.Sdk compiles every **/*.calr (src/Calor.Sdk/Sdk/Sdk.props:19) and the .NET SDK every **/*.cs, so a test under tests/ (C# under #1809, Calor under #1825) is compiled into the product project by dotnet build, where xUnit is not referenced. calor check can pass while dotnet build fails. Needed: exclude top-level tests/ from the SDK's default items or state the supported layout for calor init projects, and run dotnet build beside calor check in the #1650 per-agent case.
- **#1819, near #1836**: A reuse tier that spares a caller when only a callee's declared effect row changes (criterion 5 of #1732 and the "while the caller is skipped" clause of its criterion 3). Not in the triage follow-up, not built by #1836, and it would replace a pinned soundness test.
- **#1838, near #1839**: A CI step that runs calor format --check and calor lint --check over samples/ and over the complete programs in docs/ and website/content/. The admission rule is text-only, so outside the bank nothing enforces its formatter and lint conditions.
- **#1838, near #1839**: Correcting the public files that the #1839 audit finds failing the formatter or the lint. #1839 files one follow-up issue for them but no row owns the fix.
- **#1838, near #1839**: The per-file licence and notice manifest and the converter test that a source header comment survives conversion. Needed only if the maintainer decides to publish converted open-source code.
- **#1840, near #922**: Renaming a namespace (the second name on a module header and the using directives that name it). By reading, neither rename index records a namespace occurrence. Listed under 'Not in this epic' and in #922 'Not in scope'.
- **#1840, near #1744**: Making calor ids sync report or refuse when a text edit drops an ID and mints a new one (today it prints only a count). #1744 pins and documents the outcome and does not change it. Listed under 'Not in this epic'.
- **#1840, near #1744**: Only if #1744 closes on outcome B: renaming a type through MCP calor_file_write or calor rename, which needs type references in ProjectSymbolIndex. Listed under 'Not in this epic'.
- **#1826, near #1846 (or #1805 in #1792)**: A standing CI check of the edition promise: a job that compiles a pinned snapshot of edition sources (for example the teaching programs at the edition commit) with the current compiler and fails on a new rejection. No row owns it; #1846 does the check once by hand and cannot see a check tightened under an existing diagnostic code with no changelog entry. Named in the epic body under "Not in this epic" and in #1846's "Not in scope".
- **#1826, near #1846 (Decision B)**: The second-edition proposal issue that Decision B files. It is outside the 0.31 to 0.39 plan, with no row and no displacement entry. The file now states it is backlog, has no milestone and claims no capacity until a later milestone names what it displaces.
- **#1659, near #1659**: Converting the coverage-record rows that close 0.34 as `not-reached` (including C# 15 entries that need .NET 11 hosts). Each row must link an open issue in a later milestone, but no row provides those issues; they have to be filed at the 0.34 close-out and their capacity recorded. Named in the epic body under 'Not in this epic'.
- **#1659, near #1850**: Implementing each entry the maintainer decides to convert under option (a). The row notes say each gets its own issue under #1659, filed when the decision is made; none exists in the plan today.
- **#1845, near #1834**: The steps the nullable-oblivious path document may choose have no row: dropping the header where the probe compiles clean (which also changes the module's public nullability metadata from None to NotAnnotated, a mismatch under the converter's own certification), and giving single-file calor convert the project's nullable context. Expect at least one 0.35-or-later row if the count is material. Named in the epic body under 'Not in this epic'.
- **#1845, near #1859**: A storage and re-apply mechanism for authored contracts (where they live, how they survive a second calor migrate) has no design and no row; needed only if option A is chosen and the one-module finding is positive. Named in the epic body under 'Not in this epic'.
- **#1845, near #1854**: A successor plan for the real-scale venue (registration, spend authorisation, a bundle runner whose Calor arm builds .calr) has no row; needed only on a 'feasible' verdict plus a maintainer decision to reopen. Already stated in the issue's option table and in the epic's 'Not in this epic' (no reopening of the venue).
- **#1852, near #1643 / #1855**: Moving the hosts of the D-S1.5 fixture test (d-s1.5-fixtures job, conversion shard), the conversion scorecard and the release test job to an 11.x runtime. #1855 decides only which SDK builds releases; #1641 excludes those jobs. If #1643 leaves a C# 15 entry preserved as 'needs .NET 11 hosts', its promotion (and the closing of #1643) waits for this move. Named in the epic body under 'Not in this epic'.
- **#1662, near #1851**: Lifting the reference-model demotion for List<T> and other user-type sorts (needs heap-state reasoning for Count). Named under 'Not in this epic' as a follow-up to file after 0.35 if the #1853 inventory shows list obligations in number.
- **#1662, near #1868**: Implementing whichever surface #1868 chooses (the calor verify option, or the build property). The decision record files that issue; no row owns it now.
- **#1662, near #1862**: Dropping a precondition or fact that shares no variable with the goal. Left out of scope in #1862, to be filed only if the #1843 numbers show the need.
- **#1861, near #1864**: Cross-module call-site obligations; operator and conversion applications, constructor initializers and property/indexer accessors as call sites; obligations for members of nested classes; arguments that read locals, fields or properties and callee refinements that read instance state.
- **#1861, near #1849**: Fall-through facts after an early return, break or continue (same mechanism as the throwing guard).
- **#1861, near #1860**: The implementation issue for reference-null modelling, filed only if the decision is B or C; it is a verifier step under the 0.35 controls criterion and has no row or milestone in the plan.
- **#1861, near #1858**: If SafeEscapedCommand proves exploitable, benchmarks/README.md and the original (non-ActiveSyntax) copy under benchmarks/ also mislabel it; #1858 corrects the control it tests, but correcting the legacy copy is not explicitly owned if the row slips.
- **#1872, near #1871**: Fix the false Calor0502 "duplicate pattern" warning on the second dotted enum-member case of a statement match in a module function (PatternChecker.cs:142, :195, :202-212; read, not executed). Unconditional defect; the only row touching it is gated and may never start. Candidate home: the 0.32 patch stream (#1790).
- **#1872, near #1869**: Archive the exception type of each failing hidden test and each failing agent-run test in the harness, so null-gap events need no hand attribution or local re-run of hidden suites. Candidate home: #1764 or #1760.
- **#1872, near #1870**: Correct benchmarks/README.md (lines 3 and 21-23 say 25 seeded and 25 safe cases) to the 24 and 26 that BenchmarkSubjectRegressionTests.cs:30-36 runs on the 0.30 copies. #1870 corrects it only if its gate opens. Candidate home: #1647 or a documentation row.
- **#1697, near #1704**: Delete the stale sentence in #1704's Dependencies that asks to coordinate with #1115, #1157 and #1159 (all closed 2026-10-29). #1704 is a keep row, so no text is filed for it; the keep paragraph notes the line and that the maintainer may delete it when applying the plan. Its body also asks for #1694's 'fixing change', which does not exist when #1694 closes on a no-fix disposition; the keep paragraph says the linked disposition evidence stands in. Neither was added to Non-goals because they are one-line body edits, not work.
- **#1700, near #1700**: Lifting hold case (c), a 0.37 verdict that is not positive, needs a further comparison under the #1708 protocol with its own budget decision. No row owns it; the epic body says the plan of record schedules none and that it would be filed as new work.

## Residual cross-file edits

The final pass edited one file per agent. These are changes an agent asked for in a file it could not touch. Some were made by the agent that owned that file in the same pass; they were not re-checked afterwards, so treat this as a checklist to run when filing.

- **from #1758 → #1786 (#1776)**: In the refresh-log item (part 7), replace the pre-filled row 2 text '0.32 close-out, after #1605 has published: due' with 'the first close-out after #1605 stage 1 has published: due', to match step 4 of the close-out checklist in #1765.
- **from #1758 → #1770 (#1775)**: Option D still says 'The first 0.31 exit criterion cannot be met as written; the plan of record has to be amended'. Under the current exit criterion, 'no paid run authorised' is its third form. Replace with: the criterion is met by the spend record recording 'no paid run authorised' and listing the rows that then read 'not measured', the same rows as the table 'If no paid run is authorised in 0.31' in section 2 of docs/plans/roadmap-0.31-0.39.md: the scorecard rows 'Cost per accepted task' and 'A reference the agent already knows', #1605, #1767, #1645, #1790, #1788, #1800, the #1645 re-run part of #1816, #1823, #1824, #1651, #1832.
- **from #1758 → #1645, #1790, #1788, #1800, #1816, #1824, #1651, #1832, #1767**: Each body should state what the issue does when #1605 is recorded as 'no paid run authorised' (not measured). The #1765 table says each 'follows the branch its body gives for a missing #1605 result'; I did not verify that every one of these bodies has such a branch (#1823 does).
- **from #1758 → #1772 (#1775)**: Make sure its text fix to calor://workflows covers all three defects the #673 closing comment now names (verified at bc80daeb): includeEffectSummary passed to calor_compile (reference.json:2697, :2732, :2763, :2782) and effectSummary.missing (:2768), neither defined by the tool; includeEffects passed to calor_convert (:2744), not in that tool's schema; autoFix passed at the top level (:2708, :2720, :2751, :2781) although calor_compile reads it under options (CompileTool.cs:85, :189).
- **from #1758 → #1698, #1699, #1700 (#1698, #1699, #1700)**: Their Amendment says 'Replace the body with the text under "Epic body" above'. #1759 step 3 now defines that phrase as the body of the epic's `epics` entry in issues.json; optionally reword the three amendments to name the `epics` entry directly, as #1697 does.
- **from #1758 → #1659 epic body ('If the adopter fallback applies')**: Align with the fallback row in #1765: the constructs that cause preservation are read from the per-file `losses` lists of the migrate reports the corpus report job (#1779) produces, and where a loss does not name the registry entry, from the per-entry corpus counts of the coverage record (#1659).
- **from #1758 → #1779 (#1780)**: Confirm that the corpus report job keeps (or publishes) the per-file `losses` lists of the migrate report, since the 0.34 fallback scope in #1765 reads them to decide which constructs cause preservation.
- **from #1761 → #1770 (#1775)**: In the design entry (near item 4 or the stage-2 items), add a rule stating whether an MCP calor_compile call that follows an edit of a .calr file counts as an iteration in initialized-workspace arms; #1767 step 6 reads that rule and treats silence as 'no'.
- **from #1761 → #1770 (#1775)**: Item 8 registers four readings per defect class (above threshold, below threshold, held, not measurable); #1760 now prints exactly those four names. Keep the names in the thresholds file identical, and make the item 9 outcomes read 'admissible', 'not admissible' and 'not measurable', which the new C#-habit column of #1760 prints.
- **from #1761 → #1770 (#1775)**: Optional: in item 4, where the arm-A tag and commit are named, cite tests/E2E/agent-tasks/test-logic-old-release.py:16 (OLD_COMMIT d82f3ae9, described there as v0.29) and its build procedure at :39-58 as a precedent to verify against the 0.29.0 tag.
- **from #1761 → #1605 (#1775)**: Design item 1 or 5 can now cite #1766 as the source of the interleaving driver: run-epoch.sh gains an arm list (label plus run-pair.sh options per entry), runs one run per entry per pair in turn, writes one pins.json naming every entry, and supports --null-agent for the <epoch>-null pass.
- **from #1761 → #1815 (#1812)**: Its baseline says run-epoch.sh passes --arm only and cannot interleave. After #1766 the driver takes an arm list and interleaves; reword the baseline as 'at bc80daeb' and scope #1815 to what remains (the equipped C# arm, the spend stop, the catch-channel analyzer).
- **from #1761 → #1767 (architecture.json)**: Add #1764 to deps: the file's depends_on carries it because the calor check record is written in the schema #1764 bumps.
- **from #1761 → #1763 (architecture.json)**: Decide the #1648 dep (see open question); if it is dropped, remove '#1648' from depends_on and from the closing line in #1761.
- **from #1761 → #1785 (#1787)**: Where it reads 'the form's wrong-form error count in the #1605 report', cite the column 'C#-habit events per form' of the #1760 report script as the source.
- **from #1775 → architecture.json, milestone 0.31 decisions (first line)**: Replace 'the paid leg of #1736' with 'the paid leg of #1736 only through a later entry under the same registration', to match #1770 item 13 and #1736 in #1761.
- **from #1775 → #1765 (#1758), fallback table**: For the 'no paid run authorised' branch of the first 0.31 exit criterion, list the same rows #1770 option D names as reading 'not measured': #1645 steps 1, 4 and 5, #1790, #1788, the #1605 inputs of #1785 and #1800, #1823 and #1824 (not-run branches), Part B of #1816, part 2 of #1802, and the cost recomputation in #1816 and #1832; say the spend record of #1770 carries the same list.
- **from #1775 → #1767 (#1761), step 6 and its done-when**: #1770 item 5 now recommends that an MCP calor_compile call after an edit counts as an iteration. Change 'if it says no or is silent' so that the recommended default is yes (journal with cmd 'compile'), keeping the no branch for a maintainer change.
- **from #1775 → #1785 and #1800 (#1787), Inputs**: Read the class readings (item 8) and the C#-habit counts (item 9) from the #1605 stage 1 report or, where the report says 'pending #1760', from the addendum committed beside it when #1760 lands.
- **from #1775 → #1816 Part B (#1812)**: State whether the pair set contains both penalised pairs; if it does, register a reading of the #1770 close rule (item 6) and of the item 8 base-rate rule on its Calor arm, since #1770 now points there for the only possible second reading.
- **from #1775 → #1734 and #1835 (rows that depend on #1745)**: #1745 is now delivered as two ordered parts; these rows wait for the whole issue (both parts), not part 1.
- **from #1775 → drafting-conventions.md, content rule 9 and the meta template**: Add 'decision-only' to the permitted values of 'displaces:' (used by #1770 and given by the skeleton), or change the skeleton rows that use it to 'text-only'.
- **from #1775 → architecture.json, epic #1775 sequence and #1605 deps**: Either add #1771 to the deps of #1605 (the file's depends_on carries it, following the sequence line 'Before the #1605 commit pin: #1772, #1745, #1771'), or, if the open question on the stage 1 pin is answered (b), move #1771 to the stage 2 pin in the sequence line.
- **from #1768 → #1807 (milestones/0.32/#1792.md)**: Its issue-meta still reads `labels: soundness-sweep, tests, p1` (line 446). Change to `labels: tests, p1`: the label is reserved for #1694, and #1768's epic Done when now says no issue other than #1694 carries it.
- **from #1768 → #1803 / #1807 (milestones/0.32/#1792.md, instrument table, 'Soundness packet' row)**: Replace the site list (SweepCaseGenerator.cs:152, Program.cs:301, CaseExecutor.cs:172, Program.cs:418) with a pointer to the site table in #1781, which also covers the replay-driver class literal (OracleRuntime.cs:172), the guard markers (CaseExecutor.cs:206-218), RenderLiteral prefixes, the two manifests and derive-manifests.py.
- **from #1768 → architecture.json notes for #1769 and #1781; skeleton-changes.md ruling 7; #1759 note (#1758)**: #1769 note: replace 'on the order of 115 precondition lines, 12 proof lines and 5 refinement-type lines' with the measured 112, 11 and 5 (template Calor text), and add the indexed-type template (4 cases), the 30-case execution-ceiling headroom, the baseline manifests and the frozen whitelist to what the record decides. #1781 note: point to the issue's site table instead of three sites. Ruling 7 / #1759: reword '#1694 keeps its exact title because the sweep workflow matches it' to 'the workflow creates its tracker with this title (soundness-sweep.yml:187) and, after #1694 step 0, finds it by it; today the lookup is by label (:183)'.
- **from #1768 → #875, #1862, #1660, #1860 (0.35 files)**: Where each changes the live modeled-forms whitelist or the classification of an existing row (for #1660: STR-UNMODELED-OPS and CALL-IN-CONTRACT), say that whether this is additive or a re-registration is read from the frozen-whitelist disposition and the row-addition procedure in the #1769 record, and that a changed classification or expected verdict of an existing row is a re-registration that restarts the proof-claim count (#1783).
- **from #1768 → #1864 (#1861)**: Where it says extending the harness is part of the issue, cite the #1783 restart rule: a harness change that only adds a code path used by new case ids keeps the streak if the pull request's sweep run passes against unchanged expected verdicts and the pull request names the existing claim sites it left untouched; a change to how an existing case's claim, guard marker or replay is read restarts it.
- **from #1768 → #1662 epic (Done when, bullets 2 and 3) and #1703 (#1697)**: State that a published obligation-status table with a `proven` column is a proof claim under #1783 decision 2 (plan recommendation) and that the publishing release or exit record states the streak; or cite #1783 decision 2 as recorded if the maintainer decides otherwise.
- **from #1780 → #1647 (milestones/0.32/#1649.md)**: Its done-when reads the build with `CalorEnforceEffects` at its default 'as shown by the build status in the corpus report job's three reports'. #1779 previously ran only the enforcement-off build. #1779 now runs both and reports them as `build` (effect enforcement off) and `build_effects_default`. Change the #1647 bullet to name the field: '... as shown by `build_effects_default.status` in the corpus report job's three reports'.
- **from #1780 → #1647 (milestones/0.32/#1649.md)**: #1782 now delivers its handover as a comment on #1647 covering MediatR, Serilog and FluentValidation (remaining project-validation diagnostic ids with file counts; errors of the enforcement-off build by code and file; errors of the default-enforcement build by code with file counts), and may include diagnostic ids caused by NoWarn/WarningsNotAsErrors not being passed through if that pass-through could not be made at all three compile sites. The 'what #1782 hands over' bullet should say the handover is that comment and that it can include suppressed-warning diagnostics.
- **from #1780 → #1848, #1834, #1853 (#1845.md, #1662.md)**: `per_file.class` in the corpus report is now exactly one of `native`, `partly_preserved`, `whole_file_preserved`, `unwritten`. Each column should say what it does with `unwritten` files (recommended: excluded and counted). #1853's 'an `unavailable` count above zero fails the job' is carried by the new rule in #1779: a column function reports a failure of its own, it is recorded as `columns.<key>.error`, the report is still written and the job exits non-zero; #1853 should cite that rule.
- **from #1780 → #1765 (milestones/0.31/#1758.md line 291)**: The 0.31 to 0.32 transition line says 'the corpus report job running (#1779)'. The 0.32 entry criterion now reads 'The #1782 baseline is recorded, and the corpus report job (#1779) is running before #1802, #1784 and #1800 start.' Also add #1647 to the example cross-milestone dependency list at line 300 (#1779 -> #1647, #1782 -> #1647).
- **from #1780 → blocks regeneration (regen_blocks.py)**: The closing lines of #1782 and #1779 now name #1647 among the issues they block (the skeleton gives #1647 deps #1782 and #1779); the `blocks:` meta lines were left untouched and still omit #1647 until regenerated.
- **from #1644 → #1855 (milestones/0.34/#1852.md)**: Add the disposition of calor-lsp to the body and done-when: the record states what happens to `calor lsp` on a .NET 11-only machine, given that it spawns calor-lsp (src/Calor.Compiler/Commands/LspCommand.cs:19), a separate net10.0 executable built from src/Calor.LanguageServer that no workflow packs or publishes. #1852.md has no mention of calor-lsp today, while #1639 and the #1644 epic body now point to #1855 for it.
- **from #1644 → #1765 (milestones/0.31/#1758.md, plan of record)**: Add one line to the plan of record: if #1639 records that Roslyn 5.3.0 cannot read .NET 11 framework assemblies, #1638 is added to #1639's depends_on and Work item 1 of #1638 (the package upgrade) is on the 0.31 exit chain and may no longer slip; items 2 to 4 of #1638 stay free to move. Both issues in #1644 now say this in prose; the meta lines stay 'depends_on: none' and 'may_slip: yes' until the finding is made.
- **from #1644 → #1643 (milestones/0.34/#1852.md, Work item 5)**: Extend the fixture sentence: each D-S1.5 fixture directory registered for a promoted entry also needs a case in the ExpectedOwner switch of tests/Calor.Conversion.Tests/DS15FixtureRegistryTests.cs:664-690, which throws 'Unregistered fixture owner' for an unlisted name. #1638 now says this; #1643 does not.
- **from #1644 → architecture.json row #1638 (#1644) notes**: Replace 'its consumers are #1643 and #1705' with 'its consumers are #1643 and #1856': #1705 depends on #1639 only, and no row but #1643 and #1856 has #1638 in its deps.
- **from #1644 → architecture.json row #1639 (#1644) notes**: Replace 'Adds one Linux x64 .NET 11-only consumer job, which #1641 and #1855 later extend' with 'which #1855 later extends to the other four RIDs; #1641 reuses its way of installing an 11.0.x SDK and does not change the job', matching #1641's body and the text now in #1639. Also add that choosing tool-framework option 1(b) or 1(c) moves the release-pin part of #1855 into 0.31 (#1855 part 2 already handles that case).
- **from #1594 → #1759 (#1758)**: In the sign-off text, state that when #1728 option (b) is chosen the retitle '[0.31] Release: time one release on the promotion path and correct the policy record' is applied by hand, because the row has no new_title and the assembler would otherwise file the old title with a [0.31] prefix. When option (a) is chosen, drop ', #1728' from the closing line that the #1749 amendment adds.
- **from #1594 → #1765 (#1758)**: List among the maintainer's 0.31 actions (or in the release close-out checklist): three consecutive publish-nuget.yml dry-run dispatches for #1749 on main commits before the 0.31.0 candidate is chosen, none on the candidate commit; and, if #1749 is still open at release, a comment on #1749 listing every job of the 0.31.0 publish run that needed more than one attempt (run id, attempt count, failure).
- **from #1594 → #1818 and #1836**: Describe the 'Fast warm feedback' scorecard line as carrying several values (the index build ratio from #1738 and the edit-to-diagnostic figures from #1818), to match the wording #1738 now uses; neither should call itself the single value of that line.
- **from #1776 → architecture.json, row #1777 (notes)**: Replace "vacant since 2026-09-09" with "recorded vacant on 2026-09-09 and never filled".
- **from #1776 → architecture.json, row #1648 (notes)**: Replace "the six untranslated W-00x pair directories (52 files)" with "52 .calr files in six W-00x directories (77 tracked files), pinned by blob SHA: keep as frozen evidence or remove with their pins".
- **from #1776 → architecture.json, row #1786 (notes)**: Delete "it is refreshed at the 0.32 close-out when #1605 has published", which contradicts the next sentence (first refresh at the first close-out after #1605 stage 1 has published). Add that the 0.31 draft sizes cost ratios only and its per-stratum task minimum is a stated assumption.
- **from #1776 → #1765 (#1758), section 4, 0.36 held row**: The row says a held issue carries "a comment that links the #1833 record". On early application (a #1777 decline or default line) the comment links the #1777 record, docs/plans/0.31-outreach-decision.md; say "links the record that applied the hold (#1777 or #1833)".
- **from #1776 → #1857 (#1698), Vacancy paragraph**: No change needed. It is now consistent with #1777, which allows a later dated reviewer entry at any time; listed so the chain can be confirmed.
- **from #1649 → #1805 (#1792)**: In the Recommendation, replace 'B, only for a form that has a regression on record and an open fix under #1645' with 'B, only for a form that the #1645 re-run reads as not recovered and that has the open follow-up issue #1645 filed'. #1645 is a dependency of #1805 and is closed by then, so no fix can be open under it.
- **from #1649 → #1782 and #1779 (#1780)**: #1647 now has depends_on #1782, #1779; both blocks lines must list #1647 (the mechanical regeneration should do this). In #1779 'Not in scope', change 'Running the subjects' own test suites against the migrated projects (#1647)' to match the answer to the open question; if (b), drop the '(#1647)' pointer.
- **from #1649 → #1766 (#1761)**: State that the 0.30 source-selection option reads a pair's Calor sources from the pair directory when the pair has no ActiveSyntax mirror entry, with a harness test. Otherwise #1789 step 9 does this extension itself.
- **from #1649 → #1803 and #1807 (#1792)**: State that the two #1789 pairs are required in the tree, in post-rewrite syntax and re-validated, before the pin commit is posted, not 'if they have merged'. #1645 step 4 and #1816 Part B need them at that commit.
- **from #1649 → #1790 row note (architecture.json)**: Replace 'Fix the hint that spells protected internal where the parser and the reference spell protected-internal' with 'Make the compound-visibility text in the teaching reference agree with the parser and the hints (two-word C# spelling); test every hinted replacement'. Parser.cs:4231-4234 and :5576-5577 accept the two-word form; reference.json:926-927 is the outlier.
- **from #1649 → #1746 row (architecture.json)**: Record labels ci, enhancement; the tracker has none and the file proposes these.
- **from #1649 → #909 row (architecture.json)**: Add a conditional new_title note: if the double report does not reproduce, retitle to '[0.32] Calor0926 suggests an over-strong refinement for a conditionally executed division'. The #1758 checklist row already says amend.
- **from #1649 → #1816 Part B (#1812)**: Add the no-report case to match #1645 step 6: if #1605 recorded 'no paid run authorised', the pair set is the two #1789 pairs only, or Part B is declined. Also state the base-rate reading on the #1789 pairs, which the 0.32 decision list assigns to Part B; its Readings row currently says those pairs are reported 'with no threshold'.
- **from #1787 → #1803 (#1792)**: Add: 'A row the window record marks as lint-only (#1796: the non-canonical form stays legal) is rewritten but not cut over to an error.' Also: a #1800 'land' row is a check that warns until this pull request and becomes an error here; files it reports are hand-migrated and listed.
- **from #1787 → #1806 (#1792)**: Add to the rule contract: lint-only rows (#1796) get a rewrite rule but no rejection; if #1797 reserves a word, the rewriter gets a rule renaming identifiers that collide with it.
- **from #1787 → #1793 (#1792)**: State that it carries the word reservation if the #1797 row is marked 'changes .calr source: yes' (switched on in #1803), and the lint finding, safe fix and formatter rule for #1796.
- **from #1787 → #1785 (architecture.json row)**: deps [] -> ['#1770']; the file now has depends_on: #1770 because part C copies the threshold that registration holds.
- **from #1787 → #1784 (architecture.json row)**: deps ['#1779'] -> ['#1779','#1785','#1799']; the file now carries both. Note text 'Fixes the order: decisions, #1084 (marker)' should read 'the #1084 design record before the cut-off; decisions; the #1084 implementation and #1793'.
- **from #1787 → #1796 (architecture.json row)**: deps [] -> ['#1785','#1799']; the file now carries both.
- **from #1787 → #1787 epic (architecture.json sequence and notes)**: Sequence line '#1784 closes at its cut-off date' -> '#1784 is decided at its cut-off date and closed after #1803 or a no-rewrite inventory'. #1735 note: replace 'Prerequisite for any guard lift on virtual, override or interface-implementing members' with '#1866 depends on it; the exclusion of virtual, abstract, override and interface-implementing members stays either way'. #1743 and #1741 notes: drop the opening 'Into the breaking window; decided in #1784'. #1797 note: 'Additive syntax' -> 'additive unless the chosen spelling reserves a word'.
- **from #1787 → #1741 (architecture.json new_title)**: The title names three leftovers but the amendment adds a fourth (dead AttributeHelper code). Either append ', dead AttributeHelper code' to new_title or accept the shorter title; the file uses the current new_title.
- **from #1787 → #1084 (#1792)**: Confirm its body says its first pull request (design record docs/design/0.32-edition-marker.md) is what #1800 depends on, and that #1800's dependency is on that merge, not on #1084's closure; #1787 now states this in #1800 and in #1784 part 6.
- **from #1787 → #1805 (#1792)**: Promise points 1 to 3 are now named by #1800 as the winning wording of 'breaking' (removed or re-meant spelling; run-time behaviour change of a compiling program; newly reserved word; new default-on hard error). Check that the three points cover all four kinds.
- **from #1787 → #1846 (#1826)**: Accept as an input finding: '#1832 pinned a Gemini or Copilot model different from the one named in the #1785 record'; #1785 now routes that difference here without reopening a window decision.
- **from #1787 → #1646 and #1645 (#1649)**: State that their candidate payloads must be in the window manifest by the payload date in docs/design/0.32-breaking-window.md (earlier than the cut-off); a row whose payloads miss it is deferred unless the maintainer records a second dispatch. #1645: a C2 respelling is not admitted when #1605 recorded 'no paid run authorised' or reads 'not measurable' for the form.
- **from #1787 → #1871 and #1643**: Cite #1800 candidate 5 (making the bool exhaustiveness check effective; Program.cs:913 builds the pattern checker with no type environment) as the decision that governs giving the checker a target type.
- **from #1808 → #1803 (#1792)**: In the inventory or staging text, add: 'If #1731 recorded outcome (b) for any member group, the built-in contract change that makes today's programs report Calor0410 is part of this pull request, after the rewriter rule from #1806 has been applied; #1731 closes only after this issue merges.' The #1792 text currently names #1731 only as the owner of compiler support (lines 58, 189, 268), not the rejecting flip.
- **from #1808 → architecture.json note for #1731 (#1808)**: Drop the citation 'CHANGELOG.md:144' for the 2026-11-14 revert: that line belongs to the accessor-row entry and CHANGELOG.md records neither the withdrawal nor the restoration. The evidence is the issue's History and the 'restored 0.29' test comments (StringInterpolationTests.cs:270-272, NativeMetadataClosureTests.cs:774).
- **from #1808 → architecture.json note for #1798 (#1808)**: Replace 'the hand-written docs/syntax-reference/effects.md member-level statements (:381, :609, :616)' with 'the member-level statements of website/content/syntax-reference/effects.mdx (:374-376, :603-604, :608-611), from which docs/syntax-reference/effects.md is generated'. Any other epic that tells an agent to edit docs/syntax-reference/*.md directly (other than through reference.json for current-language.md) should point at the .mdx source.
- **from #1808 → architecture.json note for #1802 (#1808) and #1708 (#1698)**: Add to the #1802 note: 'the per-file JSON lists each callable with its flags (declares ! unknown; effects assumed, Calor0419)'. #1802 step 1 now promises this; #1708's unverified-boundary share can cite it as the source for the per-function figure.
- **from #1808 → architecture.json note for #1814 (#1808)**: Correct the line numbers: the Calor0411 message is at EffectEnforcementPass.cs:5949-5953 (string at :5952), and the confidence value is kept because MergeManifest returns the existing manifest (EffectsCommand.cs:777). Add that the issue now also refuses to overwrite an unparseable .calor-effects.json (EffectsCommand.cs:779-783, :639).
- **from #1812 → architecture.json #1815**: Add #1764 to deps (the file's depends_on already carries it: the build-block channel of a C# arm needs `build_diagnostics`, and a labelled Calor arm needs the language gating).
- **from #1812 → architecture.json #1816 (notes)**: Replace '135 runs at recorded rates are roughly $85 to $95; at a 4x overrun about $340 to $380' with '135 runs at archived per-run costs project to $59 to $94 (summed total_cost_usd over the epochs' agent.json files); four times that is $236 to $377'.
- **from #1812 → architecture.json #1810 (notes)**: Replace 'three to five runs per arm per pair' with 'the odd run count #1816 registers (3 or 5)'.
- **from #1812 → #1805 (milestones/0.32/#1792.md, Gate paragraph and done-when)**: Replace 'Both are registered and authorised, or declined, in `docs/plans/0.32-paid-run-registration.md` (#1816)' with 'Both are registered in Annex A of `docs/plans/agent-native-gates.md` and authorised, or declined, in `docs/plans/0.32-paid-run-registration.md` (#1816)'.
- **from #1812 → #1764 (milestones/0.31/#1761.md)**: In the schema change to `loop-telemetry/3`, also record on each `diagnostics` and `build_diagnostics` entry the file and line (or the enclosing function name) that the compiler's JSON diagnostic already carries in `location`, so there is one schema bump. #1815 now reads: 'If the journal as #1764 left it already records a location on each entry, use it and change no schema', so it holds either way.
- **from #1812 → #1766 (milestones/0.31/#1761.md)**: If the interleaving arm list for `run-epoch.sh` is added there, no further change is needed here: #1815 part 3 now reads 'A new script, or an extension of `run-epoch.sh` if #1766 gave that script an interleaving arm list'. Keep the arm-list entry shape compatible with an arm definition's product binding (`--arm-repo-root`, `--calor-dll`, `--exemplar`, source-selection option).
- **from #1812 → #1645 (milestones/0.32/#1649.md, step 4)**: State the run counts as Part B of #1816 now registers them: 10 per arm per pair for the regressed pairs and 15 per arm per pair for the two #1789 pairs (30 Calor-arm runs); say that the base-rate rule of #1770 item 8 is applied to this epoch's Calor arm on the #1789 pairs, that the close rule of #1770 item 6 is re-read only when both penalised pairs are in the set, and that if #1605 filed no regression the re-run is the two #1789 pairs only.
- **from #1812 → #1805 (milestones/0.32/#1792.md)**: Confirm it carries the branch #1810 now names 'Carried': Part A authorised and the result unpublished at the 0.32 release means the edition declaration is deferred and the fourth exit criterion is recorded as carried to 0.33.
- **from #1804 → #1836 (milestones/0.33/#1819.md)**: In the Gate, choose between step 1 and step 2 from the files-compiled counts that the #1818 comment now reports (for each edit: files compiled, skipped, and how many compiled files call nothing in the edited module), not from (a) and (b) differing in time. On the latency fixture (a) and (b) are expected to recompile nearly the same 72 files, so the branch "(a) over and neither (b) nor (c) over" is practically unreachable, and (c) on P2 recompiles every file.
- **from #1804 → #1836 (milestones/0.33/#1819.md)**: In "What #1732 passes on", replace "points two leftovers here" with wording that matches #1732 as now drafted: #1732 states that criterion 5 and the "while the caller is skipped" clause are delivered by no issue and that #1836 records this in its closing comment; if the maintainer applied the F-3' section 7 text at the #1732 close, the draft amendment #1836 writes after step 2 amends that applied text.
- **from #1804 → #1823 (milestones/0.32/#1821.md)**: Under decision 3, require the record to say, if option c is taken, how Copilot (which has no hook and whose reading of MCP server instructions is unverified) receives the test-file rule; #1809's last done-when bullet is not met without it. Also have the record state the share of always-loaded headroom given to #1809 and to #1822 (both issues now cite that share).
- **from #1804 → #1605 (0.31) and #1760**: State that the stage 2 sub-entry records its commit pin, or that stage 2 is recorded as not run, because part 1 of #1809 (the hook exemption and the changed "No tests found" and block messages) is ordered after that pin so the blocked-attempt count is taken on the unexempted hook.
- **from #1804 → #1650 (milestones/0.33/#1653.md)**: #1809 part 1 now owns replacing the '/calor', '/calor-semantics' and '@calor' references in the hook block reason and the Gemini systemMessage (HookCommand.cs:310, :230) with things calor init installs; #1650 part 4 should cite that and not repeat it.
- **from #1804 → architecture.json, row #1809 notes**: Correct the line references: the validator is HookCommand.cs:256-323, the .cs block :297-313, edits are blocked through :347-348; FindTestFiles is TestCommand.cs:404-431. Add that part 1 merges after the stage 2 commit pin of #1605.
- **from #1804 → architecture.json, row #1822 notes**: Narrow "no document under docs/cli or the website guides says what to do with a whole-file preserved file" to "no page says how preserved C# is changed after conversion" (docs/cli/convert.md:510-546 covers what to do with the original .cs). Add that the procedure names a single converter entry point and leaves the MCP/CLI receipt mismatch as an unowned follow-up.
- **from #1804 → architecture.json, row #1818 notes**: Note that the row is delivered as two ordered pull requests (the durationMs field with its test; then the scripts, CI job, budget file and record), and that its comment on #1836 carries files-compiled counts as well as times.
- **from #1804 → architecture.json, row #1732 notes**: Replace "The remaining reuse tier is #1836" with "The body-only reuse tiers are #1836; criterion 5 (caller a cache hit on a row-only callee edit) is delivered by no row and is recorded as lapsed at the close, with the F-3' section 7 text applied or not as the maintainer decides".
- **from #1821 → architecture.json #1823 (notes)**: Replace 'template budget from 600 to about 1.6K tokens' with 'from 600 to about 1.9K-2.2K (agent file plus server instructions), depending on where the rule block is carried'.
- **from #1821 → architecture.json #1820 (notes)**: Replace 'with the transcript archived' with 'with the server-side journal and sanitised evidence archived'; add 'if #1767 is not built, Claude Code is added here'; replace 'is measured in #1651' with 'is measured in #1811 (drivers in #1651)'.
- **from #1821 → architecture.json #1824 (notes)**: Replace '19 tools cost 7,900 always-on tokens' with '19 tools plus server instructions cost 7,900 (tools/list 7,625)'.
- **from #1821 → #1652 (milestones/0.33/#1653.md, lines 394 and 399)**: Where it says 'the transcript #1820 archived', say 'the journal #1820 archived (the client's JSON-RPC requests and the server's verbose log)'; #1820 commits no raw client transcript.
- **from #1821 → #1805 (milestones/0.32/#1792.md, scope point 7 'Tool layout')**: Add MCP tool names to the scope points the declaration settles: whether merging or renaming an MCP tool (#1824 reduction branch) is covered by the promise; recommended not covered, listed in release notes.
- **from #1821 → #1659 (milestones/0.34/#1659.md, item 3 on documenting a new form at line 71)**: State that where a conversion-only form is documented follows decision 5 of the #1823 record, and that if the record chose 'outside the rendered reference' and #1734 has not landed, the first pull request adding such a form adds the generator marking itself.
- **from #1792 → #1784 (#1787)**: Allow, in the closed-state rules, a 'land' row that has not merged when #1807 is ready to be re-recorded as deferred to a future edition after the cut-off (#1807 step 5 now does this). State that #1805 depends on the record being closed, and that the opt-in mechanism is an option (later edition, or named opt-in on the marker line) decided in the #1084 design record.
- **from #1792 → #1800 (#1787)**: The 'Opt-in' outcome row should read so that it holds under either #1084 option: 'opts in by the mechanism the #1084 design record states (a later edition, or a named opt-in on the marker line); the first issue that ships such a check implements the mechanism'.
- **from #1792 → #1869 and #1871 (35-x)**: Replace 'the mechanism #1084 shipped' with 'the mechanism the #1084 design record states'; add that if no earlier issue has implemented it, this issue implements it.
- **from #1792 → #1816, #1810 (#1812) and #1645 (#1649)**: Add to each dry pass: repeat the arm canary (run-pair.sh --canary-only) for any Calor arm configuration registered after #1807 ran, since #1807 falls back to the #1770 configurations when #1816 is not yet signed.
- **from #1792 → #1645 (#1649)**: Confirm the 'not recovered' reading says the follow-up issue is the 'resolving issue' #1805 option B names, and that an exception is available only for a form with such a row.
- **from #1792 → #1731 (#1808)**: Say that #1803 step 4 applies each outcome (b) contract change and carries a test that the previously compiling program is rejected; and that #1805 records whether the soundness exemption may tighten an outcome (c) assumption.
- **from #1792 → #1735 (#1787)**: Note that #1805 now depends on it and that #1807 checks it is an ancestor of the posted commit (or re-recorded as deferred).
- **from #1792 → #1846 (#1826)**: Part 2 should read its starting list from the #1805 record field 'language-surface changes merged after the posted commit, with class', and on a 0.32 deferral from the per-form list of deciding issues.
- **from #1792 → architecture.json notes for #1084, #1806, #1793, #1803, #1807**: #1084: add 'design record before the #1784 cut-off; implementation after the spelling row is decided; the record also decides the opt-in mechanism'. #1806: replace '(a rule for #1741 edits effect rows ...)' with '(a rule may edit effect rows, for example a new effect code under #1731, which #1801 pins as never safe)'. #1793: add that the emitter default is unchanged until #1803, the formatter never converts forms, and a new reserved word enters the keyword table only in #1803. #1803: add the manual Tier 2 dispatch compared with the parent commit. #1807: reword to 'the commit it posts (the merge commit of its pull request) includes the #1645 fixes, #1790 and every land row'; add deps #1778, #1766, #1736. #1805: add deps #1784, #1735.
- **from #1653 → #1832 (milestones/0.33/#1817.md)**: In Done when, replace 'Part C one per listed item' with 'Part C carries a dated Authorised or Declined line for each of the three known items (Codex smoke run #709, Gemini smoke run #711, example-bank measurement #1835) and for any further item listed', so that a smoke run cannot be omitted silently. #709 and #711 in #1653 now read: held while #1832 is open with no line for the run; closed as not planned on a Declined line or when #1832 has closed without listing it.
- **from #1653 → #1832 (milestones/0.33/#1817.md)**: Keep the names #1653 now cites: 'Part A' for the per-agent runs with one decision line per agent, 'Part C' for the smoke runs, the record path docs/plans/0.33-paid-run-registration.md, and the decision-line words 'Authorised' / 'Declined'. In the Part A 'Copilot client' row, add 'the #1651 Copilot driver targets this same client' (#1653 #1651 item 3 now targets the client the #1820 record checked, or its optional headless row, without waiting for #1832).
- **from #1653 → #1815 (milestones/0.32/#1812.md)**: No text change required, but its blocks line must gain #1651: #1653 #1651 now lists #1815 in depends_on (it adds the agent parameter and a run-count stop to the #1815 epoch driver). Regenerated mechanically.
- **from #1653 → #1820 (milestones/0.32/#1821.md)**: blocks line must gain #1651 (#1653 #1651 now lists #1820 in depends_on, because the Copilot driver targets the client that record checked and may take sample transcripts from it). Regenerated mechanically.
- **from #1653 → #1773 (milestones/0.31/#1775.md)**: blocks line must gain #1650 (#1653 #1650 now lists #1773 in depends_on: the per-agent entry stores only the default of the delivery option #1773 adds, and test (e) asserts the non-default form still produces the #1773 output). Regenerated mechanically.
- **from #1653 → #1767 (milestones/0.31/#1761.md)**: In the Shut branch, add: 'Item 5 (journal each calor check as an iteration with cmd: "check") is then made under #1651, whose done-when carries the stub test for it.' #1653 #1651 item 2 and its done-when now own that fallback.
- **from #1653 → #1835 (milestones/0.33/#1838.md)**: No change required; for information: #1811 step 6 now carries the option (b) reporting that #1835 expects (the 'example uses per run' column and first-build pass rate with the sentence 'no comparison arm; no effect is claimed', when the pinned commit contains the bank), and its Not in scope now excludes only a paid example-bank comparison.
- **from #1817 → architecture.json, rows #1828 and #1831 (notes)**: Add to the notes: 'The dependency on #1832 is on its Part B decision line only; #1832 itself closes when every part has a line.' The bodies of #1828, #1832 and #1831 already say this.
- **from #1817 → #1811 (#1653)**: Add to its entry conditions or spend-control step: no Calor arm runs paid in a calor init workspace until a calor check that follows an edit is journalled as an iteration (item 5 of #1767, or the same change under #1651 if #1767 was closed as not planned), evidenced by a no-provider run with a cmd "check" record; and to its primer reading: 'If #1770 registered no delivery rule (no stage 2), the primer-read rate is reported with no reading.' Both now stand in the #1832 rules it runs under.
- **from #1817 → #1835 (#1838)**: For option (a), cite the #1832 rule that a Calor arm in a calor init workspace is not run paid until calor check after an edit is journalled as an iteration; note that --null-agent runs dotnet build only (run-pair.sh:1275, :1294), so its zero-cost pass does not show that record and a no-provider stub run is the evidence.
- **from #1817 → #1651 (#1653)**: State that if #1767 was closed as not planned, #1651 journals calor check after an edit as an iteration (cmd "check" in loop-telemetry.schema.json and validate-telemetry.sh) for the calor init workspace, because #1832 blocks paid Calor arms in that workspace until it exists.
- **from #1817 → #1828 (this file; note for the plan owner, no edit made)**: If #1767 item 5 lands, the journal's cmd enum gains "check"; #1828 keeps journalling cargo check as "build" by design. No change requested unless the plan owner wants cargo check recorded as "check".
- **from #1819 → #1732 (#1804)**: In the amendment text and the closing comment, replace "Criterion 5 ... needs the declaration-surface reuse tier ...; it continues in #1836" and "That work, criterion 5 and the 'while the caller is skipped' clause of criterion 3 move to #1836" with: criterion 5 and that clause need a tier that spares a caller when only a callee's declared effect row changes; ES-04 edits a declared row, so the declaration-surface tier of #1836 does not deliver them; the row-only tier has no issue and is listed as a follow-up in epic #1819. Only the declaration-surface and base-class body-edit work moves to #1836.
- **from #1819 → #1818 (#1804)**: #1836 now chooses its step from the files-compiled counts, not from the times of (a) and (b) differing. Keep in Done when that the record gives files compiled and skipped for (a), (b) on P1 and (c) on P2, and add the same counts to the comment posted on #1836. If the names ml1-004/VERDICT.md, eng/latency-budgets.json, P1 or P2 change, #1836 needs the same change.
- **from #1819 → #1802 (#1808)**: In part 2 (the agent-run table), report files under a run's top-level tests/ separately from the rest of final-src/, or exclude them, and say which; after #1825, Calor tests that declare `! unknown` would otherwise raise the unknown-effect share.
- **from #1819 → architecture.json, row #1836**: Consider a new_title that covers the widening, for example "[0.33] Warm feedback: the remaining reuse tier and reuse in calor check (conditional)". #1819 keeps the current title until the row carries one.
- **from #1819 → #1835 (#1838)**: Part 1 should say that the store it creates accepts the test-form teaching example #1825 adds (with the #1745 field and a lookup query of `test`), since #1825 item 5 stores the example there when part 1 has merged.
- **from #1819 → #1846 (#1826)**: Parts 2 and the done-when should cover the case where #1825 is held because the declaration covers tool layout: the record then carries a dated entry stating the hold and what the 0.33 release does, which is what meets the exit criterion. Today the body assumes #1825 is closed when the record is prepared.
- **from #1838 → #1832 (#1817.md, Part C row for the example-bank measurement)**: Add to option (b): 'Option (b) requires parts 1 to 4 of #1835 in the commit #1811 pins; if they are not, the item reads (c), not measured.' Also state in the record whether the pinned commit includes the bank and which pairs' task shapes the bank covers; that sentence is absent from #1817.md.
- **from #1838 → #1811 (#1653.md)**: Add one sentence: 'If the #1832 record chose observation of the example bank (option (b) of its Part C), the report gives example uses per run and first-build pass rate per agent with the sentence "no comparison arm; no effect is claimed"; this applies only when parts 1 to 4 of #1835 are in the pinned commit, otherwise the report says "example bank: not measured".' No dependency on #1835 is added, because #1835 may slip.
- **from #1838 → #1839 (architecture.json row)**: Add #1763 to deps (the file's depends_on carries #1765 and #1763, because the page names the legacy-dialect allowlist by path), so the regenerated blocks line of #1763 names #1839.
- **from #1838 → #1835 (architecture.json row notes)**: Reword 'served through calor_help; calor init starters seeded from them' to 'to be served through calor_help (action=example returns ten self-test scenarios today) and used as the calor init starter (calor init writes no .calr file today)', so the note is not read as baseline.
- **from #1838 → #1825 (#1819.md, item 5)**: No change needed; confirm it keeps the sentence 'If part 1 of #1835 has merged, store it in the bank store that issue created'. #1835 now states the mirror: every programs[] entry at its starting commit (87 at HEAD, 88 if #1825 added its example) becomes a bank entry.
- **from #1840 → architecture.json, row #922 (epic #1840)**: Replace the note 'Body uses retired syntax; brought to current syntax under #1759.' with 'Body is replaced after its first line by the #1840 amendment; the #1759 syntax edit for #922 is recorded as superseded (#1758 already allows this).' If the maintainer picks option (b) of the title question, also add new_title: '[0.33] Editor rename cannot rename a partial type declared across files — needs identity groups in the workspace index'.
- **from #1826 → architecture.json, row #1846**: Add #1825 to deps (currently #1650, #1805, #1811). The row's own notes say the record lists the #1825 tool-layout change, the drafted issue has it in depends_on, and #1819's #1825 already carries 'blocks: #1846'.
- **from #1826 → architecture.json, row #1833 (notes)**: Add to the notes: the record names which fallback-0.37 option applies (F1 or F2) by the rule in the plan of record; a candidate who is interested but has not agreed in writing counts as the fallback outcome; withdrawal of consent after a pass is a later dated entry from which the fallback applies; and the default line "not decided, treated as the fallback outcome until changed" holds issues only.
- **from #1826 → #1765 (#1758), section 4 'Adopter fallback', Early application bullet and the Checkpoint log sentence**: State what the #1833 default line does, matching #1833: "If the #1833 record is not merged when the 0.33 release is prepared, the agent preparing the release appends `Checkpoint <date>: not decided, treated as the fallback outcome until changed.` to the checkpoint log. The issues this table holds are held from that date; 'demand unproven' is not written and nothing is closed, narrowed or re-scoped (the 0.34 cut and F1/F2 included) until the maintainer merges an outcome in the #1833 record." Also change "#1833 appends one dated line to it" to allow that default line as a second line (#1833's done-when says the plan-of-record diff of its own pull request is one added line).
- **from #1826 → #1765 (#1758), plan of record**: Add one sentence: a second-edition proposal filed under #1846 Decision B is backlog; it has no milestone and claims no capacity until a later milestone names what it displaces.
- **from #1826 → #1659 (#1659) and the 0.34 entry criterion 'Adopter checkpoint recorded; scope is cut per the fallback table if it applies'**: Say that under the #1833 default line 0.34 starts at full scope and the cut applies only from a merged outcome F (or change this if the maintainer answers the open question differently). #1833 comments on #1659 only after a merged P or F.
- **from #1826 → #1697 (#1697), entry criterion 1**: Confirm it reads the merged #1833 record with outcome P only; the checkpoint-log default line does not pass it (#1833 now says '0.36 is not entered' under the default).
- **from #1659 → #1765 (milestones/0.31/#1758.md)**: Add to the milestone close-out checklist: 'At the 0.34 close, every row of docs/plans/0.34-native-coverage.md with the reason kind not-reached links an open issue in a later milestone; file the missing issues and record the capacity they take.' In the #1659 fallback row, add after `no-demand`: 'recorded in docs/plans/0.34-native-coverage.md'.
- **from #1659 → #1647 (milestones/0.32/#1649.md)**: In the decision row 'Blocking native floors on the corpus report job', add: 'The same choice governs the per-project density reference values #1844 commits: blocking if floors are blocking, report-only otherwise.'
- **from #1659 → #1779 (milestones/0.31/#1780.md)**: No text change needed if `workflow_dispatch` on any branch and the file names `corpus-report.yml` and `scripts/corpus_report.py` stay as written; #1659 rule 5 and the #1733 done-when now cite them by name. If either is renamed, update #1659 to match.
- **from #1845 → architecture.json notes of #1848 and #1842**: Change '(#1605, #1651)' / 'existing epochs, #1605 and #1651' to name #1811 as the 0.33 source of agent outputs (on the drivers of #1651), and add the #1645 re-run as a source for #1848, matching the bodies.
- **from #1845 → architecture.json title of #1854**: Optional: retitle to '[0.34] Feasibility gate: can the real-scale venue reopen? Its two registered re-entry conditions and its eligible supply', since the verdict has three parts. The body is correct with either title.
- **from #1845 → #1866 (milestones/0.35/#1861.md)**: Align with the #1847 record as now specified: (1) the record states three conditions on (b) (a lift ConversionLossKind that is not a semantic loss or its own fidelity value; oracle differences counted before the loss-recorded waiver in files with a lift; switch-on acceptance is pass counts equal to switch-off, native fraction reported not gated) and answers whether custom-message / actualValue guards and ThrowIf helpers may be lifted (recommended no); the 'If the record is silent' fallback can stay. (2) The record is append-only and #1866 reads the latest dated entry; the 0.35-close census re-read is done under #1866. (3) The census now reports `liftable` (exactly #1866's qualifying rules, including static or non-virtual dispatch, if-throw shape, no custom message, condition without calls) and `liftable_wide`; the 'go' reading uses `liftable`.
- **from #1845 → #1860 (milestones/0.35/#1861.md)**: Its sentence '25 is the "go" count recommended for the census in #1848' still holds numerically, but the census's liftable count is now the narrow one; no change needed unless the plan owner changes the 25.
- **from #1845 → #1770 item 8 (milestones/0.31/#1775.md)**: Add one sentence so the two rules map without a deviation: 'For the disposal spike (#1842) an event is a run whose final sources hold at least one hand-checked leak, and a run is a valid run on a pair with at least one opportunity; the #1605 class reading does not gate #1842, whose count is the next measurement that reading waits for.' Also soften 'Not measurable and held both keep the dependent item (… #1842 …) waiting' so that it names #1870 as the item kept waiting on the disposal side.
- **from #1845 → #1870 (milestones/0.35/#1872.md)**: No text change required: its gate reads the #1842 verdict words (go, drop, hold, not measurable), which are unchanged. Confirm only that 'The rule and its thresholds are in the note' is still true: the note now quotes #1770 item 8 instead of stating its own values.
- **from #1845 → #1709 (milestones/0.37/#1698.md)**: No change required; #1842 still comments the disposal analyzers on #1709 with candidates CA2000 and CA2213.
- **from #1852 → architecture.json row #1642 (#1852)**: deps is empty; set to #1605, #1810, #1645, #1831, #1811, #1835 to match the gate in the body (the file's depends_on already lists them). If the title question is answered yes, change new_title to '[0.34] Dependency refresh: System.CommandLine beta4 → stable; no prerelease dependencies'.
- **from #1852 → architecture.json rows #1640 and #1641 (#1852)**: Add #1639 to deps of both (both bodies read the decisions #1639 records and reuse its net11-only-consumer job); add #1641 to deps of #1643 (its C# 15 emission tests need an 11.x leg). The file's depends_on already carries these edges.
- **from #1852 → #1639 (milestones/0.31/#1644.md)**: Keep the job name `net11-only-consumer`; #1640, #1641 and #1855 cite it. #1855 now also records the calor-lsp disposition in docs/plans/0.31-toolchain-and-sweep-record.md and refers to 'the roll-forward setting #1639 chose for calor'; #1639's record should state that setting explicitly so it can be applied to Calor.LanguageServer.
- **from #1852 → #1797 / #1803 (milestones/0.32/#1787.md, #1792.md)**: #1643 item 1 now reads the window record to learn whether the union declaration word was reserved in #1803 (otherwise it is recognised by position). The #1797 row of the window record must state 'reserved: <word>' or 'recognised by position' in those terms.
- **from #1852 → #1800 (milestones/0.32/#1787.md)**: #1643 item 2 pins the recorded outcome of candidate 5 (making the bool exhaustiveness check effective) with a test; the #1800 record must state that outcome explicitly (land / not land) so the test has something to pin.
- **from #1852 → #1590 (milestones/0.31/#1594.md)**: No text change required; note that #1641 constraint 4 now tells the implementer to post on #1590 if a matrix entry in remaining-tests is unavoidable while #1590 is open, restarting its 30-run window.
- **from #1662 → #1783 (#1768), #1778 (#1768), #1807 (#1792), #1816 (#1812)**: Add #1662 to the blocks line of each; #1662 now depends on all four (its entry gate reads them). The mechanical regeneration should cover this.
- **from #1662 → #1800 (#1787)**: Add #1868 to its blocks line; #1868 now depends on it and quotes the candidate 4 option it records.
- **from #1662 → #1864 (#1861)**: In item 6 and the done-when, replace 'if that counter has not merged when this issue does, a comment on #1853 names the field' with the rule #1853 now states: if #1864 merges first, #1853 adds the call-site row to the by-kind table; otherwise #1864 adds it in the counter (tools/Calor.ProofInventory).
- **from #1662 → #1864, #1863, #1849, #1866 (#1861)**: Where they report obligations as 'Discharged' or 'Failed' from the #1853 counter, use its column names: Discharged is `proven`, Failed is `refuted`. A file whose only errors are Calor1121 or Calor1140 is counted as verified, with those obligations `refuted`.
- **from #1662 → #1783 (#1768)**: Its table says the proof-claim count 'is counted from the latest registration (#1778, or #1807 ...), which precedes 0.35 by at least two milestones, so 0.35 entry is unlikely to wait on it'. Add that inside 0.35 five issues can change registered verdicts (#875, #1851, #1862, #1660, #1661), with the rows named at HEAD bc80daeb: STR-UNMODELED-OPS, CALL-IN-CONTRACT, OBL-UNGATED-FORMS, BODY-LOOP-CALL-MUTABLE, OBL-LOOP-VARIABLE.
- **from #1662 → Any file whose chain review states that the sweep packet has no loop template**: Correct it: tests/TestData/SoundnessSweep/templates.json at HEAD bc80daeb has T-BODY-LOOP (row BODY-LOOP-CALL-MUTABLE, 8 cases) and T-OBL-LOOP (row OBL-LOOP-VARIABLE, 2 cases), all expected Unsupported.
- **from #1662 → #1765 (#1758)**: Confirm that the release close-out checklist in docs/plans/roadmap-0.31-0.39.md carries the item 'publish the proof-yield after value' at the 0.35 close, and add 'record the 0.35 entry commit and the counted sweep runs as a comment on #1662'. #1662's Done when and #1843 now cite the first item as the owner of the after value.
- **from #1861 → #1847 (#1845)**: In option (b) and the Done-when list of record contents, add: the lift gets its own ConversionLossKind that is not a semantic loss (or the record names a separate fidelity value); member-oracle differences are counted before the loss-recorded waiver for files that contain a lift; switch-on round-trip acceptance is pass counts equal to the switch-off run with native fraction reported, not gated; and whether guards with a custom message or actualValue argument, and ThrowIf helpers, may be lifted.
- **from #1861 → #1847 (#1845)**: Replace '"Not now" for (b) means #1866 stays open and unstarted until the census is read again from the corpus job at the 0.35 close.' with a sentence that names the owner and outcome: 'the assignee of #1866 re-reads columns.guards at the 0.35 close (also after a "not measurable" reading), appends the dated reading to docs/plans/0.34-guard-census-result.md, and the maintainer adds a dated entry to this record; (b) becomes yes only through that entry.'
- **from #1861 → #1853 (#1662)**: State that the counter's by-kind table splits Subtype obligations into call-site and assignment rows using the field #1864 adds to Obligation, once that field exists (until then call-site obligations are identified only by the 'Call to' description).
- **from #1861 → architecture.json row #1866**: Add #1816 and #1795 to deps (both now in depends_on in #1861.md: #1816 outcome S holds the issue; the #1795 strict leg is part of its acceptance), and regenerate blocks for #1816 (#1812) and #1795 (#1787).
- **from #1861 → #1816 (#1812)**: In outcome S, name the lifting condition so #1866 can cite it exactly: 'held until a dated line in docs/plans/0.32-paid-run-registration.md lifts the hold' and add #1866 to the issues that carry a comment linking the registration.
- **from #1861 → skeleton-changes.md rulings / #1662 rows #875, #1851, #1862**: Add the general rule that any issue editing docs/verification-modeled-forms.md updates PinnedWhitelistSha256 (DifferentialGate.cs:21-22) and adds an additive F-4 re-pin entry in docs/plans/v0.13-freeze-registrations.md; in #1662 only #1660 and #1851 state it explicitly, so check #875 and #1862.
- **from #1861 → #1723 / #1769 (#1768)**: #1769 decision 4 posts its answer on #1723 only; also post it on #1858, whose done-when now has the same conditional sentence for a sweep diagnostics channel.
- **from #1872 → architecture.json, epic #1872 purpose**: Add the fourth reading: a class "held between the thresholds" also keeps a row held for the next measurement, matching #1770 item 8 and the #1760 outcome column, which already name four readings.
- **from #1872 → architecture.json, #1871 note**: Replace "PatternChecker handles bool and union types ... Calor0500 is an error in the default pipeline" with: in a default build the checker has no type environment (Program.cs:913), so the bool check reaches a verdict only for a literal target; it visits only module-level functions and statement-form matches. If gate condition 2 is accepted, add "at least one counted event must be over an enum".
- **from #1872 → architecture.json, #1870 note**: Replace "Built over the existing using-dispose control-flow operations" with "a new bug-pattern check that uses the using-dispose operation as one disposal signal; lock blocks yield the same operation and must be excluded".
- **from #1872 → #1842 (#1845)**: Have the spike's note estimate #1870 as a new analysis (disposable-type query, explicit Dispose calls, escapes, lock exclusion), not a thin consumer of the using-dispose operation.
- **from #1872 → #1643 (#1852)**: State that the typed-target plumbing for the union check must not start reporting Calor0500 on bool matches that compile at the end of 0.32 unless #1800 recorded that outcome for its fifth candidate. #1852.md:133 and :154 partly cover this; #1871 rule 8 now relies on it.
- **from #1872 → #1816 (#1812)**: #1869, #1871 and #1870 now list #1816 in depends_on, because each applies the consequence it registered for 0.35 before starting; the blocks line of #1816 will gain the three refs when regenerated.
- **from #1872 → #1760 or #1764 (#1761)**: Add an archived field or report column for the exception type of each failing hidden test and each failing agent-run test; #1869 and #1871 otherwise depend on hand attribution.
- **from #1872 → Any row in #1662, #1861 or #1723 that states the size of the bug-pattern instrument**: Cite 24 seeded and 26 safe cases (BenchmarkSubjectRegressionTests.cs:30-36), not the 25 and 25 of benchmarks/README.md:3.
- **from #1872 → 0.35 exit list (architecture.json)**: If open question 1 is answered (a): add "Conditional rows of epic #1872 are closed or held at exit; a held row does not block it."
- **from #1697 → #1768 epic body (Done when) and #1694**: For one reading of 'no tracker open' across files: where #1768 says 'no issue titled "Soundness sweep failing on main" is open', add 'and no open issue carries the `soundness-sweep` label'. #1697 entry criterion 2 now names both the label and that title.
- **from #1697 → #1791 (#1776)**: #1697's #1701 amendment now adds a heading `Sign-off` to `docs/plans/0.36-pilot-recruitment.md` and takes over opening the file if outreach is authorised after #1791 has closed. No change is required in #1791; if its text says the record has exactly eleven headings as a closed list, add 'until #1701 adds `Sign-off`'.
- **from #1697 → #1846 (#1826)**: #1697 criterion 3 now depends on #1846's record naming, under Decision C, 'the issue that carries the second edition' (already in its done-when) and, where the declaration is taken there after a 0.32 deferral, carrying the #1805 decision line as option A or B. Keep both phrasings stable; no edit needed if they stay as they are.
- **from #1698 → #1713 and #1717 (#1699)**: Rename docs/plans/v0.38-collection-spend-authorisation.md to docs/plans/0.38-collection-spend-authorisation.md wherever it appears (#1699.md lines 83, 158, 170, 194, 294), to match the unprefixed record names used by every other epic, #1698 now included.
- **from #1698 → #1765 (#1758)**: In the entry-criteria line for 0.36 to 0.37 (#1758.md line 298), replace "pilot acceptance under #1697" with "#1697 closed on its exit record (0.36 pilot acceptance)", so it matches the 0.37 milestone entry and entry criterion 1 of #1698.
- **from #1698 → #1765 (#1758), fallback table option F2**: Confirm the F2 text says #1698 stays open and exits on the #1712 verdict labelled "non-independent, fixture scale", or on the #1867 decline when F2 reverts to F1. Exit record 3 of #1698 now says this and names the options F1 and F2 as the table does.
- **from #1698 → #1832 (#1817)**: No text change needed if the Decline rule stays as it is. #1709 now drops the optional Rust condition when #1828 was closed as not planned, so the "dated line keeping the Rust fixtures" in the #1832 record is the only way that condition stays available. Say so in that Decline rule if it does not already.
- **from #1698 → #1867 depends_on (architecture.json)**: No edge change requested. Note for the regenerated blocks and any rendered dependency graph: #1867 now states that its deps (#1865, #1857, #1710, #1711) gate an authorise outcome only, and a decline may be recorded without them.
- **from #1700 → architecture.json, epic #1700 rows**: Set deps to #1718: ['#1712', '#1697']; #1719: ['#1718']; #1720: ['#1718'], so the assembler can check blocks mirroring. Leave #1717 and #1699 out. The file's depends_on lines already carry these.
- **from #1700 → #1699 (milestones/0.38/#1699.md)**: Optional wording alignment. Line 59 says 0.39 "does not wait for one of these records" and avoids "or held". #1700 now names the reachable held case: 0.39 may be entered while 0.38 is held because #1712's decision was `revise` or `stop`, or because the task class with demonstrated value is not one 0.38 could search. #1699 hold case (c) is consistent with that; adding the same sentence would make the two epics read alike.
- **from #1700 → #1699 and #1713 (milestones/0.38/#1699.md)**: The Why sections still cite "Review finding AR-9" (line 132), and "critique finding SEQ-06" and "review finding AR-9, proposed change 3" (line 208). Finding ids are to be removed from issue text.
