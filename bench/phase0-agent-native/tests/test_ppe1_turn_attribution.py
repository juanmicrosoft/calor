#!/usr/bin/env python3
"""Turn-gap attribution, observed (v0.16 S1 step 3 / W4; roadmap §0.2, §2.1, §5 gate 12).

`ppe1-turn-attribution.py` reproduces N:S1.1 and N:S1.2 of
`docs/plans/2026-08-27-v0.16-s1-s2-measurement-notes.md` from the archived
`e1-rows-parity-001` epoch, and carries W4's per-turn tool-class table. The
numbers pinned here are what the script DERIVES:

  * pooled mean difference p is EXACT over every within-pair relabelling
    (C(10,5)^4 = 4 032 758 016): 0.013289 / 0.083758 / 0.439102 — the
    reviewers' 0.012 / 0.08 / 0.44 were a 20 000-draw Monte-Carlo estimate
    of it.
  * median-over-pairs p is Monte Carlo (100 000 label-vector shuffles, seed
    4537); its exact relabelling values, enumerated by review #2 of PR #1117,
    are 0.004132 / 0.026204 / 0.439069. The MC value is pinned WITHIN THREE
    STANDARD ERRORS of those — the fourth decimal is noise, not a pin. The
    notes' 0.004 / 0.025 / 0.449 and the roadmap's 0.0037 / 0.0249 / 0.4375
    are two 20 000-draw estimates of the same statistic under different
    shuffle mechanics (the roadmap's is reproduced exactly by this script's
    scheme at 20 000 draws — `test_roadmap_triple_is_this_scheme_at_20k`).
  * "correcting only N1-001/run-3 gives 1.3390" (notes S1.2, Draft v2) was a
    mis-attribution: N1-001/run-3 alone gives 1.1934; N1-003/run-5 alone
    gives 1.3390.

Run:  python3 -m unittest discover -s bench/phase0-agent-native/tests
"""

import importlib.util
import json
import math
import os
import shutil
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
EPOCHS = os.path.join(BENCH, "epochs")
COMMITTED = os.path.join(BENCH, "ppe1-turn-attribution.json")
TRANSCRIPT_FIXTURES = os.path.join(HERE, "fixtures", "transcripts")
E1 = "e1-rows-parity-001"

EXACT_MEDIAN_OVER_PAIRS = {"turns": 0.004132, "tokens": 0.026204, "wall": 0.439069}   # review #2 enumeration
EXACT_POOLED = {"turns": 0.013289, "tokens": 0.083758, "wall": 0.439102}


def _load():
    spec = importlib.util.spec_from_file_location(
        "ppe1_turn_attribution", os.path.join(BENCH, "ppe1-turn-attribution.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _scratch_root(tmp, copy=(E1,)):
    """An epochs root identical to the archive: symlinks, except real copies of `copy`."""
    root = os.path.join(tmp, "epochs")
    os.mkdir(root)
    for name in os.listdir(EPOCHS):
        if name.startswith("."):
            continue
        src = os.path.join(EPOCHS, name)
        if name in copy:
            shutil.copytree(src, os.path.join(root, name))
        else:
            os.symlink(src, os.path.join(root, name))
    return root


class ArchiveAttribution(unittest.TestCase):
    """The whole-archive report, computed once (the permutation loops take ~15 s)."""

    @classmethod
    def setUpClass(cls):
        cls.mod = _load()
        cls.report = cls.mod.attribute(EPOCHS)
        cls.e1 = next(e for e in cls.report["epochs"] if e["epoch"] == E1)

    # ---- gate 12 denominator -------------------------------------------------

    def test_every_entry_under_epochs_is_analyzed_or_skipped_by_name(self):
        entries = sorted(n for n in os.listdir(EPOCHS) if not n.startswith("."))
        listed = sorted(self.report["analyzedEpochs"] + [s["epoch"] for s in self.report["skipped"]])
        self.assertEqual(entries, listed)
        self.assertEqual(self.report["entries"], len(entries))
        for skipped in self.report["skipped"]:
            self.assertTrue(skipped["reason"], skipped)

    def test_the_three_ppe1_w5_shaped_epochs_are_analyzed(self):
        self.assertEqual(self.report["analyzedEpochs"], [E1, "w5-parity-001", "w5-parity-002"])
        self.assertEqual(self.report["analyzedRuns"], 120)
        self.assertEqual(self.report["skippedRunsWithinAnalyzedEpochs"], 0)
        for epoch in self.report["epochs"]:
            self.assertEqual(epoch["runs"], 40, epoch["epoch"])
            self.assertEqual(epoch["skippedRuns"], [], epoch["epoch"])
            self.assertEqual(epoch["unattributedRuns"], 0, epoch["epoch"])
            self.assertEqual(epoch["runsWithoutNumTurns"], [], epoch["epoch"])

    def test_skip_reasons_name_the_shape_failure_and_count_eligible_runs(self):
        by_name = {s["epoch"]: s for s in self.report["skipped"]}
        self.assertIn("kind='m5-comparison'", by_name["m5-compare-001"]["reason"])
        self.assertIn("kind='guarantees-probe'", by_name["guarantees-probe-001"]["reason"])
        self.assertIn("no pins.json", by_name["w4-dryrun-001"]["reason"])
        self.assertIn("not a directory", by_name["m5-compare-001.driver.log"]["reason"])
        # shape-eligible runs of other kinds are counted, never silently attributed
        self.assertEqual(by_name["m5-compare-001"]["shapeEligibleRuns"], 130)
        self.assertEqual(by_name["e1a-attribution"]["shapeEligibleRuns"], 180)
        self.assertEqual(by_name["w4-dryrun-001"]["shapeEligibleRuns"], 0)   # bundle-runner shape
        self.assertEqual(by_name["feasibility-dry-001"]["shapeEligibleRuns"], 0)
        self.assertEqual(self.report["shapeEligibleRunsNotAttributed"], 553)

    def test_committed_json_equals_a_fresh_recomputation(self):
        with open(COMMITTED, encoding="utf-8") as fh:
            committed = fh.read()
        self.assertEqual(json.dumps(self.report, indent=2) + "\n", committed,
                         "regenerate with: python3 bench/phase0-agent-native/ppe1-turn-attribution.py")

    def test_dotfiles_in_epochs_root_are_not_entries(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp, copy=())
            with open(os.path.join(root, ".DS_Store"), "wb") as fh:
                fh.write(b"\x00Bud1")
            os.mkdir(os.path.join(root, ".hidden-epoch"))
            with open(os.path.join(root, ".hidden-epoch", "pins.json"), "w", encoding="utf-8") as fh:
                fh.write('{"kind": "pp-e1-rows-parity"}')
            report = self.mod.attribute(root)
        self.assertEqual(json.dumps(report, indent=2), json.dumps(self.report, indent=2))

    # ---- N:S1.2 — agent behaviour per pair ------------------------------------

    def test_per_pair_median_turns_and_deltas(self):
        rows = {p["pair"]: p["turns"] for p in self.e1["perPair"]}
        self.assertEqual([(rows[p]["medianTreatment"], rows[p]["medianControl"], rows[p]["medianDelta"])
                          for p in ("N1-001", "N1-002", "N1-003", "N1-005")],
                         [(19, 16, 3), (23, 17, 6), (23, 19, 4), (19, 20, -1)])

    def test_per_pair_median_wall_clock(self):
        rows = {p["pair"]: p["wallClock"] for p in self.e1["perPair"]}
        self.assertEqual([(rows[p]["medianTreatmentSeconds"], rows[p]["medianControlSeconds"])
                          for p in ("N1-001", "N1-002", "N1-003", "N1-005")],
                         [(136, 127), (135, 91), (255, 187), (109, 126)])

    def test_per_pair_sorted_corrected_tokens(self):
        rows = {p["pair"]: p["tokens"] for p in self.e1["perPair"]}
        self.assertEqual(rows["N1-001"]["treatmentSorted"], [4864, 8027, 8203, 12426, 13150])
        self.assertEqual(rows["N1-001"]["controlSorted"], [4221, 5817, 7387, 9431, 12821])
        self.assertEqual(rows["N1-002"]["treatmentSorted"], [5642, 7437, 8048, 10298, 10621])
        self.assertEqual(rows["N1-002"]["controlSorted"], [3361, 4348, 4461, 5548, 10093])
        self.assertEqual(rows["N1-003"]["treatmentSorted"], [5004, 11552, 16362, 18034, 23938])
        self.assertEqual(rows["N1-003"]["controlSorted"], [10597, 11788, 12196, 13585, 14732])
        self.assertEqual(rows["N1-005"]["treatmentSorted"], [5736, 6045, 6882, 7284, 7900])
        self.assertEqual(rows["N1-005"]["controlSorted"], [6128, 7066, 7748, 8366, 8368])
        self.assertEqual([rows[p]["ratio"] for p in ("N1-001", "N1-002", "N1-003", "N1-005")],
                         [1.1762, 1.5118, 1.1907, 0.8984])

    def test_pooled_mean_difference_is_exact(self):
        pooled = self.e1["permutation"]["pooledMeanDifference"]
        self.assertIn("exact", pooled["method"])
        by = pooled["byMetric"]
        self.assertEqual({m: by[m]["p"] for m in ("turns", "tokens", "wall")}, EXACT_POOLED)
        self.assertEqual(by["turns"]["relabellings"], 252 ** 4)
        self.assertEqual(by["turns"]["pExactFraction"], "53590270/4032758016")
        self.assertEqual(by["turns"]["observed"], 4.35)
        for m in ("turns", "tokens", "wall"):
            self.assertEqual(by[m]["n"], {"treatment": 20, "control": 20})
            self.assertEqual(by[m]["pairs"], 4)

    def test_median_over_pairs_is_monte_carlo_within_three_se_of_exact(self):
        """The 3-s.e. band checks the VALUE, not the shuffle scheme.

        Other permutation schemes (e.g. permuting labels across pairs rather
        than within) can land inside this band on some metrics, so the band
        alone does not pin the mechanism. The scheme is pinned by the exact
        relabelling triple asserted below, by
        `test_roadmap_triple_is_this_scheme_at_20k`, and by the byte-equality
        of the committed artifact."""
        mc = self.e1["permutation"]["medianOverPairsOfPairedMeanDelta"]
        self.assertEqual((mc["seed"], mc["draws"]), (4537, 100000))
        by = mc["byMetric"]
        self.assertEqual(by["turns"]["observed"], 5.5)   # four pairs: mean of the two middle deltas
        for m in ("turns", "tokens", "wall"):
            p, se, hits = by[m]["p"], by[m]["standardError"], by[m]["hits"]
            self.assertEqual(p, round(hits / 100000, 4))
            self.assertAlmostEqual(se, math.sqrt((hits / 100000) * (1 - hits / 100000) / 100000), places=6)
            self.assertLessEqual(abs(p - EXACT_MEDIAN_OVER_PAIRS[m]), 3 * se,
                                 f"{m}: MC p {p} is more than 3 s.e. ({se}) from the exact {EXACT_MEDIAN_OVER_PAIRS[m]}")
            self.assertEqual(by[m]["n"], {"treatment": 20, "control": 20})
        # the committed draw, for the record (noise beyond the s.e. shown)
        self.assertEqual([by[m]["p"] for m in ("turns", "tokens", "wall")], [0.0041, 0.0251, 0.4385])

    def test_roadmap_triple_is_this_scheme_at_20k(self):
        ok, reason, pins, _ = self.mod.classify_epoch(os.path.join(EPOCHS, E1))
        runs, _ = self.mod.collect_runs(os.path.join(EPOCHS, E1), pins)
        triple = [self.mod.monte_carlo_median_over_pairs_p(self.mod._cells(runs, m), draws=20000)["p"]
                  for m in ("turns", "tokens", "wall")]
        self.assertEqual(triple, [0.0037, 0.0249, 0.4375])   # roadmap §0.2, reproduced

    def test_exact_pooled_p_agrees_with_brute_force_on_a_small_case(self):
        cells = {"A": {"treatment": [5, 7], "control": [1, 2, 3]},
                 "B": {"treatment": [10], "control": [4, 6]}}
        import itertools
        import statistics
        observed = self.mod.stat_pooled_mean_difference(cells)
        pool_a, pool_b = [5, 7, 1, 2, 3], [10, 4, 6]
        hits = total = 0
        # Split by INDEX, not by value: a value-membership split would drop
        # relabellings whenever a pool holds duplicates.
        for ia in itertools.combinations(range(len(pool_a)), 2):
            ta = [pool_a[i] for i in ia]
            ca = [pool_a[i] for i in range(len(pool_a)) if i not in ia]
            for ib in itertools.combinations(range(len(pool_b)), 1):
                tb = [pool_b[i] for i in ib]
                cb = [pool_b[i] for i in range(len(pool_b)) if i not in ib]
                total += 1
                stat = statistics.mean(ta + tb) - statistics.mean(ca + cb)
                hits += stat >= observed
        exact = self.mod.exact_pooled_p(cells)
        self.assertEqual(exact["relabellings"], total)
        self.assertEqual(exact["pExactFraction"], f"{hits}/{total}")

    def test_token_sensitivity_line(self):
        sens = self.e1["tokenSensitivity"]
        self.assertEqual(sens["allNaive"], 1.349)
        self.assertEqual(sens["registeredAllCorrected"], 1.1835)
        singles = {s["run"]: s for s in sens["singleRunCorrections"]}
        self.assertEqual(set(singles), {"N1-001/calor+v0.14.3/run-3", "N1-003/calor+v0.14.3/run-5"})
        self.assertEqual((singles["N1-001/calor+v0.14.3/run-3"]["naive"],
                          singles["N1-001/calor+v0.14.3/run-3"]["corrected"]), (4522, 12821))
        self.assertEqual((singles["N1-003/calor+v0.14.3/run-5"]["naive"],
                          singles["N1-003/calor+v0.14.3/run-5"]["corrected"]), (12547, 13585))
        self.assertEqual(singles["N1-003/calor+v0.14.3/run-5"]["pointEstimateCorrectingOnlyThisRun"], 1.339)
        self.assertEqual(singles["N1-001/calor+v0.14.3/run-3"]["pointEstimateCorrectingOnlyThisRun"], 1.1934)
        self.assertEqual(sens["meanTotalCostUsdPerRun"], 1.0048)
        self.assertEqual(sens["costRuns"], 40)

    def test_result_json_tokens_agree_with_the_token_usage_rule(self):
        self.assertEqual(self.e1["resultJsonTokenDisagreements"], [])
        self.assertEqual(self.e1["tokenSources"], {"modelUsage": 40})
        self.assertEqual(self.e1["validRuns"], {"control": 20, "treatment": 20})

    def test_w5_result_json_token_disagreements_are_listed(self):
        by_epoch = {e["epoch"]: e["resultJsonTokenDisagreements"] for e in self.report["epochs"]}
        self.assertEqual(by_epoch["w5-parity-001"],
                         ["N1-002-inventory/calor+control/run-2", "N1-005-order-pipeline/calor+treatment/run-3"])
        self.assertEqual(by_epoch["w5-parity-002"],
                         ["N1-001-string-utils/calor+treatment/run-4", "N1-002-inventory/calor+control/run-3"])

    # ---- N:S1.1 — the harness compile census ----------------------------------

    def test_compile_census(self):
        census = self.e1["compileCensus"]
        self.assertEqual(census["totalBuilds"], 49)
        treatment = census["byArm"]["calor+0.15.0"]
        control = census["byArm"]["calor+v0.14.3"]
        self.assertEqual((treatment["role"], treatment["builds"], treatment["editedBuilds"],
                          treatment["uneditedObservationBuilds"]), ("treatment", 26, 22, 4))
        self.assertEqual((control["role"], control["builds"], control["editedBuilds"],
                          control["uneditedObservationBuilds"]), ("control", 23, 21, 2))
        self.assertEqual(treatment["codes"], {"Calor0100": 2, "Calor0101": 2, "Calor0830": 1})
        self.assertEqual(control["codes"], {"Calor0830": 1})
        for arm in (treatment, control):
            self.assertEqual(arm["effectFamilyDiagnostics"], 0)
            self.assertEqual(set(arm["namedEffectCodes"].values()), {0})
            self.assertEqual((arm["missingJournals"], arm["unparsableJournalLines"]), (0, 0))

    # ---- W4 per-turn table over the archive: nothing archived yet -------------

    def test_archived_runs_have_no_transcript_and_are_listed_not_dropped(self):
        for epoch in self.report["epochs"]:
            table = epoch["perTurn"]
            self.assertEqual(table["runs"], 40, epoch["epoch"])
            self.assertEqual(table["withTranscript"], 0, epoch["epoch"])
            self.assertEqual(len(table["noTranscript"]), 40, epoch["epoch"])
            self.assertEqual(sum(a["noTranscript"] for a in table["byArm"].values()), 40)
            # perTurn and perRun agree on the short pair id and the int run number
            self.assertEqual([(r["pair"], r["arm"], r["run"]) for r in table["byRun"]],
                             [(r["pair"], r["arm"], r["run"]) for r in epoch["perRun"]])
            self.assertTrue(all(isinstance(r["run"], int) for r in epoch["perRun"]))

    # ---- gate 12 discriminating pin: delete one run -> the output changes -----

    def test_deleting_one_archived_run_changes_the_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp)
            shutil.rmtree(os.path.join(root, E1, "N1-002-inventory", "calor+0.15.0", "run-3"))
            mutated = self.mod.attribute(root)
        e1 = next(e for e in mutated["epochs"] if e["epoch"] == E1)
        self.assertEqual(e1["runs"], 39)
        self.assertEqual(e1["compileCensus"]["totalBuilds"], 47)   # that run carried two builds
        self.assertEqual(e1["perTurn"]["runs"], 39)
        self.assertNotEqual(json.dumps(mutated, indent=2) + "\n", json.dumps(self.report, indent=2) + "\n")
        with open(COMMITTED, encoding="utf-8") as fh:
            self.assertNotEqual(json.dumps(mutated, indent=2) + "\n", fh.read())

    def test_a_damaged_run_is_skipped_per_run_not_per_epoch(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp)
            e1_dir = os.path.join(root, E1)
            os.remove(os.path.join(e1_dir, "N1-001-string-utils", "calor+0.15.0", "run-2", "agent.json"))
            with open(os.path.join(e1_dir, "N1-003-csv-row", "calor+v0.14.3", "run-4", "result.json"), "w",
                      encoding="utf-8") as fh:
                fh.write("{not json")
            os.remove(os.path.join(e1_dir, "N1-005-order-pipeline", "calor+0.15.0", "run-1", "result.json"))
            with open(os.path.join(e1_dir, "N1-005-order-pipeline", "calor+v0.14.3", "run-5", "result.json"),
                      "w", encoding="utf-8") as fh:
                fh.write('{"pair": "N1-005-order-pipeline", "arm": "calor+v0.14.3"}')
            mutated = self.mod.attribute(root)
        self.assertIn(E1, mutated["analyzedEpochs"])
        e1 = next(e for e in mutated["epochs"] if e["epoch"] == E1)
        self.assertEqual(e1["runs"], 36)
        self.assertEqual([(s["directory"], s["reason"].split(":")[0].split(" (")[0]) for s in e1["skippedRuns"]], [
            ("N1-001-string-utils/calor+0.15.0/run-2", "no agent.json"),
            ("N1-003-csv-row/calor+v0.14.3/run-4", "result.json is not valid JSON"),
            ("N1-005-order-pipeline/calor+0.15.0/run-1", "no result.json"),
            ("N1-005-order-pipeline/calor+v0.14.3/run-5", "result.json lacks ['run', 'tokens', 'censored', 'invalid']"),
        ])
        self.assertEqual(mutated["skippedRunsWithinAnalyzedEpochs"], 4)
        self.assertEqual(e1["perTurn"]["runs"], 36)

    def test_a_malformed_pins_json_skips_the_epoch_with_a_reason(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp)
            with open(os.path.join(root, E1, "pins.json"), "w", encoding="utf-8") as fh:
                fh.write("{broken")
            mutated = self.mod.attribute(root)
        self.assertNotIn(E1, mutated["analyzedEpochs"])
        entry = next(s for s in mutated["skipped"] if s["epoch"] == E1)
        self.assertTrue(entry["reason"].startswith("pins.json is not valid JSON"), entry)
        self.assertEqual(entry["shapeEligibleRuns"], 40)

    def test_a_run_without_num_turns_is_visible_in_n(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp)
            agent = os.path.join(root, E1, "N1-002-inventory", "calor+v0.14.3", "run-1", "agent.json")
            with open(agent, encoding="utf-8") as fh:
                envelope = json.load(fh)
            del envelope["num_turns"]
            with open(agent, "w", encoding="utf-8") as fh:
                json.dump(envelope, fh)
            mutated = self.mod.attribute(root)
        e1 = next(e for e in mutated["epochs"] if e["epoch"] == E1)
        self.assertEqual(e1["runsWithoutNumTurns"], ["N1-002-inventory/calor+v0.14.3/run-1"])
        self.assertEqual(e1["runs"], 40)
        turns = e1["permutation"]["pooledMeanDifference"]["byMetric"]["turns"]
        self.assertEqual(turns["n"], {"treatment": 20, "control": 19})
        tokens = e1["permutation"]["pooledMeanDifference"]["byMetric"]["tokens"]
        self.assertEqual(tokens["n"], {"treatment": 20, "control": 20})

    def test_a_diagnostic_without_a_code_is_counted_not_crashed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = _scratch_root(tmp)
            journal = os.path.join(root, E1, "N1-001-string-utils", "calor+0.15.0", "run-1", "journal.jsonl")
            with open(journal, "a", encoding="utf-8") as fh:
                fh.write('{"cmd": "build", "edited": true, "diagnostics": [{"declarationId": "f9"}, "junk"]}\n')
                fh.write("not json\n")
            mutated = self.mod.attribute(root)
        arm = next(e for e in mutated["epochs"] if e["epoch"] == E1)["compileCensus"]["byArm"]["calor+0.15.0"]
        self.assertEqual(arm["builds"], 27)
        self.assertEqual(arm["codes"]["<none>"], 2)
        self.assertEqual(arm["unparsableJournalLines"], 1)


class TranscriptTabulation(unittest.TestCase):
    """W4's per-turn tool-class table over synthetic stream-json fixtures."""

    @classmethod
    def setUpClass(cls):
        cls.mod = _load()
        cls.run_dirs = cls.mod.find_run_dirs([TRANSCRIPT_FIXTURES])
        cls.table = cls.mod.tabulate_runs(cls.run_dirs, base=TRANSCRIPT_FIXTURES)
        cls.by_dir = {r["directory"]: r for r in cls.table["byRun"]}

    def test_fixture_layout(self):
        self.assertEqual(sorted(self.by_dir), [
            "W-001-demo/calor+control/run-1", "W-001-demo/calor+control/run-2",
            "W-001-demo/calor+treatment/run-1", "W-001-demo/calor+treatment/run-2"])
        self.assertEqual(self.table["runs"], 4)
        self.assertEqual(self.table["withTranscript"], 3)
        run = self.by_dir["W-001-demo/calor+treatment/run-1"]
        self.assertEqual((run["pair"], run["pairDirectory"], run["arm"], run["run"]),
                         ("W-001", "W-001-demo", "calor+treatment", 1))

    def test_multi_block_messages_count_once_and_tools_are_classified(self):
        run = self.by_dir["W-001-demo/calor+treatment/run-1"]
        self.assertEqual(run["transcript"], "present")
        self.assertEqual(run["turns"]["assistantMessages"], 7)   # msg_01..msg_07, three events share msg_01
        self.assertEqual(run["turns"]["subagentMessages"], 0)
        self.assertEqual(run["turns"]["resultNumTurns"], 14)
        self.assertEqual(run["toolCalls"], {"Read": 1, "Grep": 2, "Bash-build": 3,
                                            "Bash-other": 1, "Edit": 2, "other": 1})
        self.assertEqual(run["toolCallsTotal"], 10)
        self.assertEqual(run["toolNames"], {"Bash": 4, "Edit": 1, "Glob": 1, "Grep": 1,
                                            "Read": 1, "TodoWrite": 1, "Write": 1})
        self.assertEqual(run["unparsableLines"], 1)
        self.assertEqual(run["events"], 19)
        self.assertFalse(run["empty"])

    def test_bash_build_pattern_has_word_boundaries(self):
        classify = self.mod.classify_tool
        build = [
            "dotnet build -c Release", "cd x && dotnet test tests/Held", "dotnet  test",
            "dotnet run --project src/Calor.Compiler -- --input a.calr",
            "calor --input a.calr", "calor", "cd /ws && calor",
            "dotnet ./calor.dll --input a.calr", "ls\ndotnet build", "(dotnet test) 2>&1 | tail",
        ]
        other = [
            "escalor foo", "echo 'dotnet test' > notes", "echo 'calor '", "grep calor foo",
            "dotnet build-server shutdown", "which calor", "dotnet run-x", "ls", "dotnet",
            "cat calor.dll", "",
        ]
        for command in build:
            self.assertEqual(classify("Bash", {"command": command}), "Bash-build", command)
        for command in other:
            self.assertEqual(classify("Bash", {"command": command}), "Bash-other", command)
        self.assertEqual(classify("Bash", {}), "Bash-other")
        self.assertEqual(classify("Bash", None), "Bash-other")
        self.assertEqual(classify("Glob", {}), "Grep")
        self.assertEqual(classify("NotebookEdit", {}), "Edit")
        self.assertEqual(classify("Agent", {}), "other")
        self.assertEqual(classify("", {}), "other")

    def test_mcp_compiles_land_in_other_with_the_raw_name_kept(self):
        self.assertEqual(self.mod.classify_tool("mcp__calor__compile", {"source": "§M{m:X}"}), "other")
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = os.path.join(tmp, "P-1-x", "arm", "run-1")
            os.makedirs(run_dir)
            with open(os.path.join(run_dir, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                fh.write('{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"t1",'
                         '"name":"mcp__calor__compile","input":{"source":"x"}},{"type":"tool_use","id":"t2",'
                         '"name":"mcp__calor__query","input":{}}]},"parent_tool_use_id":null}\n')
            run = self.mod.tabulate_runs([run_dir], base=tmp)["byRun"][0]
        self.assertEqual(run["toolCalls"]["other"], 2)
        self.assertEqual(run["toolNames"], {"mcp__calor__compile": 1, "mcp__calor__query": 1})

    def test_subagent_messages_are_counted_separately_from_top_level(self):
        run = self.by_dir["W-001-demo/calor+treatment/run-2"]
        self.assertEqual(run["turns"]["assistantMessages"], 3)   # msg_01mainA, msg_05mainB, msg_06mainC
        self.assertEqual(run["turns"]["subagentMessages"], 3)
        self.assertEqual(run["turns"]["resultNumTurns"], 1)   # the delegation under-count num_turns carries
        # the re-emitted MultiEdit block (same tool_use id) counts once
        self.assertEqual(run["toolCalls"], {"Read": 1, "Grep": 1, "Bash-build": 1,
                                            "Bash-other": 0, "Edit": 1, "other": 1})
        self.assertEqual(run["toolCallsTotal"], 5)

    def test_tool_use_without_id_is_deduplicated_per_event_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = os.path.join(tmp, "P-1-x", "arm", "run-1")
            os.makedirs(run_dir)
            same = ('{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","name":"Read",'
                    '"input":{"file_path":"a"}}]},"parent_tool_use_id":null}\n')
            other = ('{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","name":"Read",'
                     '"input":{"file_path":"b"}}]},"parent_tool_use_id":null}\n')
            not_dict = '{"type":"assistant","message":"oops"}\n{"type":"assistant"}\n'
            with open(os.path.join(run_dir, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                fh.write(same + same + other + not_dict)
            run = self.mod.tabulate_runs([run_dir], base=tmp)["byRun"][0]
        self.assertEqual(run["toolCalls"]["Read"], 2)   # the identical re-emission counts once, the other block counts
        self.assertEqual(run["turns"]["assistantMessages"], 1)
        self.assertEqual(run["events"], 5)
        self.assertEqual(run["unparsableLines"], 0)

    def test_empty_transcript_is_present_and_zero(self):
        run = self.by_dir["W-001-demo/calor+control/run-1"]
        self.assertEqual(run["transcript"], "present")
        self.assertTrue(run["empty"])
        self.assertEqual(run["events"], 0)
        self.assertEqual(run["turns"], {"assistantMessages": 0, "subagentMessages": 0, "resultNumTurns": None})
        self.assertEqual(run["toolCallsTotal"], 0)

    def test_missing_transcript_is_listed_as_noTranscript(self):
        run = self.by_dir["W-001-demo/calor+control/run-2"]
        self.assertEqual(run["transcript"], "noTranscript")
        self.assertNotIn("turns", run)
        self.assertEqual(self.table["noTranscript"], ["W-001-demo/calor+control/run-2"])

    def test_per_arm_totals(self):
        arms = self.table["byArm"]
        self.assertEqual(sorted(arms), ["calor+control", "calor+treatment"])
        self.assertEqual(arms["calor+treatment"]["runs"], 2)
        self.assertEqual(arms["calor+treatment"]["withTranscript"], 2)
        self.assertEqual(arms["calor+treatment"]["assistantMessages"], 10)
        self.assertEqual(arms["calor+treatment"]["subagentMessages"], 3)
        self.assertEqual(arms["calor+treatment"]["toolCalls"], {"Read": 2, "Grep": 3, "Bash-build": 4,
                                                                "Bash-other": 1, "Edit": 3, "other": 2})
        self.assertEqual(arms["calor+treatment"]["toolCallsTotal"], 15)
        self.assertEqual(arms["calor+control"], {"runs": 2, "withTranscript": 1, "noTranscript": 1,
                                                 "assistantMessages": 0, "subagentMessages": 0,
                                                 "toolCalls": {"Read": 0, "Grep": 0, "Bash-build": 0,
                                                               "Bash-other": 0, "Edit": 0, "other": 0},
                                                 "toolCallsTotal": 0})

    def test_markdown_rendering_lists_every_run(self):
        text = self.mod.render_markdown(self.table)
        self.assertIn("| calor+treatment | 2 | 2 | 10 | 3 | 2 | 3 | 4 | 1 | 3 | 2 | 15 |", text)
        self.assertIn("| W-001-demo/calor+control/run-2 | noTranscript |", text)
        self.assertIn("1 run(s) without transcript.jsonl", text)

    def test_cli_transcripts_mode_writes_json_and_markdown(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = os.path.join(tmp, "table.json")
            md = os.path.join(tmp, "table.md")
            rc = self.mod.main(["--transcripts", TRANSCRIPT_FIXTURES, "--out", out, "--markdown", md])
            self.assertEqual(rc, 0)
            with open(out, encoding="utf-8") as fh:
                written = json.load(fh)
            with open(md, encoding="utf-8") as fh:
                self.assertEqual(fh.read(), self.mod.render_markdown(self.table))
        self.assertEqual(written["byRun"], json.loads(json.dumps(self.table["byRun"])))

    def test_a_single_run_directory_is_accepted(self):
        run_dir = os.path.join(TRANSCRIPT_FIXTURES, "W-001-demo", "calor+treatment", "run-1")
        self.assertEqual(self.mod.find_run_dirs([run_dir]), [run_dir])
        table = self.mod.tabulate_runs([run_dir], base=run_dir)
        self.assertEqual(table["byRun"][0]["turns"]["assistantMessages"], 7)

    def test_nested_runs_below_a_result_json_are_not_pruned(self):
        with tempfile.TemporaryDirectory() as tmp:
            outer = os.path.join(tmp, "P-2-y", "arm", "run-1")
            inner = os.path.join(outer, "nested", "P-3-z", "arm", "run-2")
            os.makedirs(inner)
            with open(os.path.join(outer, "result.json"), "w", encoding="utf-8") as fh:
                fh.write('{"pair": "P-2-y", "arm": "arm", "run": "run-1"}')
            with open(os.path.join(inner, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                fh.write('{"type":"assistant","message":{"id":"m1","content":[]},"parent_tool_use_id":null}\n')
            found = self.mod.find_run_dirs([tmp])
            self.assertEqual(found, [outer, inner])
            table = self.mod.tabulate_runs(found, base=tmp)
        self.assertEqual([(r["pair"], r["run"], r["transcript"]) for r in table["byRun"]],
                         [("P-2", 1, "noTranscript"), ("P-3", 2, "present")])

    def test_run_identity_is_normalised_to_an_int(self):
        mod = self.mod
        self.assertEqual([mod._run_number(v) for v in (7, "7", "run-7", "run-12", None, "x", True)],
                         [7, 7, 7, 12, None, None, None])
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = os.path.join(tmp, "P-9", "arm-x", "run-7")
            os.makedirs(run_dir)
            with open(os.path.join(run_dir, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                fh.write('{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","name":"Read","input":{}}]}}\n')
            table = self.mod.tabulate_runs(self.mod.find_run_dirs([tmp]), base=tmp)
            run = table["byRun"][0]
            self.assertEqual((run["pair"], run["arm"], run["run"]), ("P-9", "arm-x", 7))
            self.assertEqual(run["turns"]["assistantMessages"], 1)
            self.assertEqual(run["toolCalls"]["Read"], 1)
            with open(os.path.join(run_dir, "result.json"), "w", encoding="utf-8") as fh:
                fh.write('{"pair": "P-9-long-name", "arm": "arm-y", "run": "run-3"}')
            run = self.mod.tabulate_runs([run_dir], base=tmp)["byRun"][0]
            self.assertEqual((run["pair"], run["pairDirectory"], run["arm"], run["run"]),
                             ("P-9", "P-9-long-name", "arm-y", 3))
            with open(os.path.join(run_dir, "result.json"), "w", encoding="utf-8") as fh:
                fh.write("{broken")
            run = self.mod.tabulate_runs([run_dir], base=tmp)["byRun"][0]
            self.assertEqual((run["pair"], run["arm"], run["run"]), ("P-9", "arm-x", 7))


if __name__ == "__main__":
    unittest.main()
