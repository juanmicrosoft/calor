#!/usr/bin/env python3
"""PP-E1 leg B arithmetic, observed (annex A-1.11; roadmap §4.4 gate 4).

`ppe1-analyze.py` is the recorded half of PP-E1's instrument: at the 0.15.0
release commit it turns the archived `e1-rows-parity-001` per-run result.json
files into the leg-B inputs the ledger test derives the verdict from. That
epoch has not run, so this test pins the arithmetic on the one archived epoch
of the same shape — `w5-parity-002` (four N1 pairs, 5 runs/arm, both arms
Calor, two-toolchain contrast v0.10.0 vs v0.11 main), read as a DRY RUN. The
numbers below were produced by the script on that epoch and are pinned so a
change to the statistic, the token derivation or the validity floor is
observed here rather than at adjudication.

Two of them are cross-checked against the margin derivation the row froze:
the realized within-cell CV median 0.4392 / max 0.8038 are exactly the
figures `ppe1-margin-derivation.txt` reports for w5-parity-002 with corrected
tokens (the population the 1.35 margin was derived on).

Run:  python3 -m unittest discover -s bench/phase0-agent-native/tests
"""

import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
EPOCH = os.path.join(BENCH, "epochs", "w5-parity-002")


def _load():
    spec = importlib.util.spec_from_file_location("ppe1_analyze", os.path.join(BENCH, "ppe1-analyze.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PpE1AnalyzeDryRunOnW5Parity002(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.mod = _load()
        cls.analysis, cls.lines = cls.mod.analyze(EPOCH, dry_run=True)

    def test_frozen_constants_are_the_rows(self):
        m = self.mod
        self.assertEqual(m.POINT_GATE, 1.35)
        self.assertEqual(m.LOWER_BOUND_GATE, 1.0)
        self.assertEqual(m.CV_CAP, 0.66)
        self.assertEqual((m.MIN_RUNS_PER_CELL, m.MIN_PAIRS, m.MIN_VALID_PER_ARM), (2, 3, 12))
        self.assertEqual(m.CENSOR_CAP, 0.40)
        self.assertEqual(m.PAIRS, ["N1-001", "N1-002", "N1-003", "N1-005"])
        self.assertEqual(m.EPOCH_ID, "e1-rows-parity-001")

    def test_dry_run_is_labelled_and_harness_valid(self):
        a = self.analysis
        self.assertTrue(a["dryRun"])
        self.assertEqual(a["epoch"], "w5-parity-002")
        self.assertTrue(a["harnessValid"], a["blockers"])
        self.assertEqual(a["pairsSurviving"], ["N1-001", "N1-002", "N1-003", "N1-005"])
        self.assertEqual(a["pairsDropped"], [])
        self.assertEqual(a["validRuns"], {"control": 20, "treatment": 20})
        self.assertEqual(a["censored"], {"control": 0.0, "treatment": 0.0})
        # Every run's figure came from modelUsage — the corrected sum, never usage.output_tokens.
        self.assertEqual(a["tokenSources"], {"modelUsage": 40})
        self.assertNotEqual(a["armA"]["calorTasksSha"], a["armB"]["calorTasksSha"])
        self.assertNotEqual(a["armA"]["repoRoot"], a["armB"]["repoRoot"])

    def test_pinned_numbers(self):
        a = self.analysis
        self.assertEqual([(p["pair"], p["ratio"]) for p in a["perPair"]],
                         [("N1-001", 0.9528), ("N1-002", 0.9380), ("N1-003", 1.6222), ("N1-005", 1.0505)])
        self.assertEqual(a["pointEstimate"], 1.0016)
        self.assertEqual(a["lowerBound95"], 0.8270)
        # The margin derivation's population figures, reproduced exactly.
        self.assertEqual(a["realizedMedianWithinCellCv"], 0.4392)
        self.assertEqual(a["maxWithinCellCv"], 0.8038)
        self.assertIs(a["legBFails"], False)
        self.assertIs(a["underpowered"], False)
        self.assertEqual(a["legBInput"], "PASS")
        self.assertEqual(a["iterationsToGreen"],
                         {"control": {"1": 17, "2": 2, "3": 1}, "treatment": {"1": 18, "2": 1, "4": 1}})

    def test_corrected_not_naive(self):
        """The 55x under-count run (N1-001 treatment run-4, A-1.9.1) must be read corrected."""
        run_dir = os.path.join(EPOCH, "N1-001-string-utils", "calor+treatment", "run-4")
        tokens, source = self.mod.corrected_output_tokens(run_dir)
        naive = json.load(open(os.path.join(run_dir, "result.json")))["tokens"]["output"]
        self.assertEqual(source, "modelUsage")
        self.assertEqual(naive, 543)
        self.assertEqual(tokens, 30084)

    def test_refuses_a_non_registered_epoch_without_dry_run(self):
        with self.assertRaises(SystemExit):
            self.mod.analyze(EPOCH, dry_run=False)

    def test_validity_floor_drops_a_pair_and_invalidates(self):
        """Synthetic: copy the epoch, invalidate 4 of 5 control runs of two pairs -> both pairs
        drop (disclosed); the control arm is left with exactly 12 valid runs (the floor, not
        below it) and 8/20 = 40% censored (the cap, not above it), so the ONE blocker is
        < 3 surviving pairs -> harness invalid, nothing adjudicated."""
        with tempfile.TemporaryDirectory() as tmp:
            import shutil
            copy = os.path.join(tmp, "w5-copy")
            shutil.copytree(EPOCH, copy)
            for pair in ("N1-001-string-utils", "N1-002-inventory"):
                for run in (1, 2, 3, 4):
                    path = os.path.join(copy, pair, "calor+control", f"run-{run}", "result.json")
                    record = json.load(open(path))
                    record["invalid"] = True
                    json.dump(record, open(path, "w"))
            analysis, _ = self.mod.analyze(copy, dry_run=True)
            self.assertEqual([d["pair"] for d in analysis["pairsDropped"]], ["N1-001", "N1-002"])
            self.assertEqual(analysis["pairsSurviving"], ["N1-003", "N1-005"])
            self.assertFalse(analysis["harnessValid"])
            self.assertEqual(analysis["legBInput"], "INVALID")
            self.assertIsNone(analysis["legBFails"])
            self.assertEqual(analysis["validRuns"], {"control": 12, "treatment": 20})
            self.assertEqual(analysis["censored"], {"control": 0.4, "treatment": 0.0})
            self.assertEqual(analysis["blockers"], ["only 2 pair(s) survived (need >= 3)"])

    def test_fail_rule_is_the_conjunction(self):
        """Scale the treatment arm's envelopes 2x: point > 1.35 AND lower bound > 1.0 -> FAIL."""
        with tempfile.TemporaryDirectory() as tmp:
            import shutil
            copy = os.path.join(tmp, "w5-copy")
            shutil.copytree(EPOCH, copy)
            for pair in os.listdir(copy):
                arm = os.path.join(copy, pair, "calor+treatment")
                if not os.path.isdir(arm):
                    continue
                for run in os.listdir(arm):
                    envelope_path = os.path.join(arm, run, "agent.json")
                    envelope = self.mod.TOKEN_USAGE.load_envelope(envelope_path)
                    for entry in envelope["modelUsage"].values():
                        entry["outputTokens"] = int(entry.get("outputTokens", 0)) * 2
                    json.dump(envelope, open(envelope_path, "w"))
            analysis, _ = self.mod.analyze(copy, dry_run=True)
            self.assertEqual(analysis["pointEstimate"], 2.0032)
            self.assertGreater(analysis["lowerBound95"], 1.0)
            self.assertIs(analysis["legBFails"], True)
            self.assertEqual(analysis["legBInput"], "FAIL")


class PpE1AnalyzeRecomputesTheRegisteredEpoch(unittest.TestCase):
    """Once `epochs/e1-rows-parity-001/` exists, "leg B's arithmetic is recomputed" is a
    CI-observed claim, not a release-author-observed one: analyze() is re-run on the
    archived result.json files and compared to the committed ppe1-analysis.json. Skips
    (and says so) until the epoch has run."""

    REGISTERED = os.path.join(BENCH, "epochs", "e1-rows-parity-001")

    def test_committed_analysis_matches_recomputation(self):
        committed_path = os.path.join(self.REGISTERED, "ppe1-analysis.json")
        if not os.path.isdir(self.REGISTERED):
            self.skipTest("epochs/e1-rows-parity-001 has not run (PP-E1 leg B not adjudicated)")
        self.assertTrue(os.path.exists(committed_path),
                        "the registered epoch directory exists but ppe1-analysis.json was not committed "
                        "with it — run bench/phase0-agent-native/ppe1-analyze.py on it")
        with open(committed_path, encoding="utf-8") as fh:
            committed = json.load(fh)
        recomputed, _ = _load().analyze(self.REGISTERED, dry_run=False)
        self.assertFalse(committed.get("dryRun"), "a dry run cannot be the recorded leg B")
        self.assertEqual(recomputed, committed,
                         "ppe1-analysis.json no longer matches its recomputation from the archived "
                         "result.json files — regenerate in a PR that names what moved")


if __name__ == "__main__":
    unittest.main()
