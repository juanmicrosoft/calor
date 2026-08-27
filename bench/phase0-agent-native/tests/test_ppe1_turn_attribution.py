#!/usr/bin/env python3
"""Turn-gap attribution, observed (v0.16 S1 step 3 / W4; roadmap §0.2, §2.1, §5 gate 12).

`ppe1-turn-attribution.py` reproduces N:S1.1 and N:S1.2 of
`docs/plans/2026-08-27-v0.16-s1-s2-measurement-notes.md` from the archived
`e1-rows-parity-001` epoch, and carries W4's per-turn tool-class table. The
numbers pinned here are what the script DERIVES; where they differ from a
published figure the difference is stated in the test, never fitted away:

  * median-over-pairs p (turns / tokens / wall) = 0.0043 / 0.0249 / 0.4494 —
    the notes' 0.004 / 0.025 / 0.449 at their precision; the roadmap §0.2
    line quotes 0.0037 / 0.0249 / 0.4375 for the same statistic, which no
    stdlib draw order reproduces (0.0037 and 0.4375 are within permutation
    noise of the pinned values).
  * pooled-mean-difference p = 0.0124 / 0.0851 / 0.4451 — the reviewers'
    0.012 / 0.08 / 0.44 at two figures except tokens (0.085 vs "0.08"; the
    reviewers' draw order is not recorded).
  * "correcting only N1-001/run-3 gives 1.3390" (notes S1.2) is a
    mis-attribution: correcting only N1-001/run-3 gives 1.1934; correcting
    only N1-003/run-5 gives 1.3390. The roadmap's unnamed "one-run-corrected
    1.3390" is the N1-003/run-5 figure.

Run:  python3 -m unittest discover -s bench/phase0-agent-native/tests
"""

import importlib.util
import json
import os
import shutil
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
EPOCHS = os.path.join(BENCH, "epochs")
COMMITTED = os.path.join(BENCH, "ppe1-turn-attribution.json")
TRANSCRIPT_FIXTURES = os.path.join(HERE, "fixtures", "transcripts")


def _load():
    spec = importlib.util.spec_from_file_location(
        "ppe1_turn_attribution", os.path.join(BENCH, "ppe1-turn-attribution.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ArchiveAttribution(unittest.TestCase):
    """The whole-archive report, computed once (the permutation loops take a few seconds)."""

    @classmethod
    def setUpClass(cls):
        cls.mod = _load()
        cls.report = cls.mod.attribute(EPOCHS)
        cls.e1 = next(e for e in cls.report["epochs"] if e["epoch"] == "e1-rows-parity-001")

    # ---- gate 12 denominator -------------------------------------------------

    def test_every_entry_under_epochs_is_analyzed_or_skipped_by_name(self):
        entries = sorted(os.listdir(EPOCHS))
        listed = sorted(self.report["analyzedEpochs"] + [s["epoch"] for s in self.report["skipped"]])
        self.assertEqual(entries, listed)
        self.assertEqual(self.report["entries"], len(entries))
        for skipped in self.report["skipped"]:
            self.assertTrue(skipped["reason"], skipped)

    def test_the_three_ppe1_w5_shaped_epochs_are_analyzed(self):
        self.assertEqual(self.report["analyzedEpochs"],
                         ["e1-rows-parity-001", "w5-parity-001", "w5-parity-002"])
        for epoch in self.report["epochs"]:
            self.assertEqual(epoch["runs"], 40, epoch["epoch"])
            self.assertEqual(epoch["unattributedRuns"], 0, epoch["epoch"])

    def test_skip_reasons_name_the_shape_failure(self):
        reasons = {s["epoch"]: s["reason"] for s in self.report["skipped"]}
        self.assertIn("kind='m5-comparison'", reasons["m5-compare-001"])
        self.assertIn("kind='guarantees-probe'", reasons["guarantees-probe-001"])
        self.assertIn("no pins.json", reasons["w4-dryrun-001"])
        self.assertIn("not a directory", reasons["m5-compare-001.driver.log"])

    def test_committed_json_equals_a_fresh_recomputation(self):
        with open(COMMITTED, encoding="utf-8") as fh:
            committed = fh.read()
        self.assertEqual(json.dumps(self.report, indent=2) + "\n", committed,
                         "regenerate with: python3 bench/phase0-agent-native/ppe1-turn-attribution.py")

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

    def test_both_permutation_statistics_are_stated_and_pinned(self):
        perm = self.e1["permutation"]
        self.assertEqual(list(perm), ["pooledMeanDifference", "medianOverPairsOfPairedMeanDelta"])
        for stat in perm.values():
            self.assertEqual((stat["seed"], stat["permutations"]), (4537, 20000))
        pooled = perm["pooledMeanDifference"]["byMetric"]
        self.assertEqual([pooled[m]["p"] for m in ("turns", "tokens", "wall")], [0.0124, 0.0851, 0.4451])
        median = perm["medianOverPairsOfPairedMeanDelta"]["byMetric"]
        self.assertEqual([median[m]["p"] for m in ("turns", "tokens", "wall")], [0.0043, 0.0249, 0.4494])
        # observed statistics: the median of four paired mean deltas is the mean of the two middle ones
        self.assertEqual(median["turns"]["observed"], 5.5)
        self.assertEqual(pooled["turns"]["observed"], 4.35)

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
        # the notes attribute 1.3390 to N1-001/run-3; the arithmetic puts it on N1-003/run-5
        self.assertEqual(singles["N1-003/calor+v0.14.3/run-5"]["pointEstimateCorrectingOnlyThisRun"], 1.339)
        self.assertEqual(singles["N1-001/calor+v0.14.3/run-3"]["pointEstimateCorrectingOnlyThisRun"], 1.1934)
        self.assertEqual(sens["meanTotalCostUsdPerRun"], 1.0048)
        self.assertEqual(sens["costRuns"], 40)

    def test_result_json_tokens_agree_with_the_token_usage_rule(self):
        self.assertEqual(self.e1["resultJsonTokenDisagreements"], [])
        self.assertEqual(self.e1["tokenSources"], {"modelUsage": 40})
        self.assertEqual(self.e1["validRuns"], {"control": 20, "treatment": 20})

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
            self.assertEqual(arm["missingJournals"], 0)

    # ---- W4 per-turn table over the archive: nothing archived yet -------------

    def test_archived_runs_have_no_transcript_and_are_listed_not_dropped(self):
        for epoch in self.report["epochs"]:
            table = epoch["perTurn"]
            self.assertEqual(table["runs"], 40, epoch["epoch"])
            self.assertEqual(table["withTranscript"], 0, epoch["epoch"])
            self.assertEqual(len(table["noTranscript"]), 40, epoch["epoch"])
            self.assertEqual(sum(a["noTranscript"] for a in table["byArm"].values()), 40)

    # ---- gate 12 discriminating pin: delete one run -> the output changes -----

    def test_deleting_one_archived_run_changes_the_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = os.path.join(tmp, "epochs")
            os.mkdir(root)
            for name in os.listdir(EPOCHS):
                src = os.path.join(EPOCHS, name)
                if name == "e1-rows-parity-001":
                    shutil.copytree(src, os.path.join(root, name))
                else:
                    os.symlink(src, os.path.join(root, name))
            shutil.rmtree(os.path.join(root, "e1-rows-parity-001", "N1-002-inventory",
                                       "calor+0.15.0", "run-3"))
            mutated = self.mod.attribute(root)
        e1 = next(e for e in mutated["epochs"] if e["epoch"] == "e1-rows-parity-001")
        self.assertEqual(e1["runs"], 39)
        self.assertEqual(e1["compileCensus"]["totalBuilds"], 47)   # that run carried two builds
        self.assertEqual(e1["perTurn"]["runs"], 39)
        self.assertNotEqual(json.dumps(mutated, indent=2) + "\n", json.dumps(self.report, indent=2) + "\n")
        with open(COMMITTED, encoding="utf-8") as fh:
            self.assertNotEqual(json.dumps(mutated, indent=2) + "\n", fh.read())


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

    def test_bash_build_classification_is_the_roadmap_pattern(self):
        classify = self.mod.classify_tool
        self.assertEqual(classify("Bash", {"command": "dotnet build -c Release"}), "Bash-build")
        self.assertEqual(classify("Bash", {"command": "cd x && dotnet test"}), "Bash-build")
        self.assertEqual(classify("Bash", {"command": "calor --input a.calr"}), "Bash-build")
        self.assertEqual(classify("Bash", {"command": "dotnet run"}), "Bash-other")
        self.assertEqual(classify("Bash", {"command": "ls"}), "Bash-other")
        self.assertEqual(classify("Bash", {}), "Bash-other")
        self.assertEqual(classify("Bash", None), "Bash-other")
        self.assertEqual(classify("Glob", {}), "Grep")
        self.assertEqual(classify("NotebookEdit", {}), "Edit")
        self.assertEqual(classify("Agent", {}), "other")
        self.assertEqual(classify("", {}), "other")

    def test_subagent_messages_are_counted_and_flagged(self):
        run = self.by_dir["W-001-demo/calor+treatment/run-2"]
        self.assertEqual(run["turns"]["assistantMessages"], 6)
        self.assertEqual(run["turns"]["subagentMessages"], 3)
        self.assertEqual(run["turns"]["resultNumTurns"], 1)   # the delegation under-count num_turns carries
        # the re-emitted MultiEdit block (same tool_use id) counts once
        self.assertEqual(run["toolCalls"], {"Read": 1, "Grep": 1, "Bash-build": 1,
                                            "Bash-other": 0, "Edit": 1, "other": 1})
        self.assertEqual(run["toolCallsTotal"], 5)

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
        self.assertEqual(arms["calor+treatment"]["assistantMessages"], 13)
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
        self.assertIn("| calor+treatment | 2 | 2 | 13 | 3 | 2 | 3 | 4 | 1 | 3 | 2 | 15 |", text)
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

    def test_run_identity_falls_back_to_the_path_without_result_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = os.path.join(tmp, "P-9", "arm-x", "run-7")
            os.makedirs(run_dir)
            with open(os.path.join(run_dir, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                fh.write('{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","name":"Read","input":{}}]}}\n')
            table = self.mod.tabulate_runs(self.mod.find_run_dirs([tmp]), base=tmp)
        run = table["byRun"][0]
        self.assertEqual((run["pair"], run["arm"], run["run"]), ("P-9", "arm-x", "run-7"))
        self.assertEqual(run["turns"]["assistantMessages"], 1)
        self.assertEqual(run["toolCalls"]["Read"], 1)


if __name__ == "__main__":
    unittest.main()
