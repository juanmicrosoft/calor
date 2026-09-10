"""Pins the executed #1255 spike without interpreting a scientific verdict."""

import hashlib
import importlib.util
import json
from pathlib import Path
import unittest


SPIKE = Path(__file__).resolve().parents[1] / "buildability/1255"
spec = importlib.util.spec_from_file_location("buildability_spike", SPIKE / "run.py")
spike = importlib.util.module_from_spec(spec)
spec.loader.exec_module(spike)


class BuildabilityEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads((SPIKE / "evidence/results.json").read_text())

    def test_observations_pin_release_and_all_five_shapes(self):
        self.assertEqual(spike.RELEASE, self.report["compilerCommit"])
        self.assertEqual("deterministic-buildability-spike", self.report["kind"])
        self.assertIsNone(self.report["collectionEpoch"])
        self.assertEqual({"A": ["--permissive-effects"], "B": []}, self.report["arms"])
        self.assertEqual({1, 8, 9, 11, 12}, {c["shape"] for c in self.report["candidates"]})
        self.assertEqual(5, len(self.report["candidates"]))
        self.assertEqual(26, self.report["rowTable"]["passed"])
        self.assertEqual(0, self.report["rowTable"]["failed"])

    def test_observations_match_executed_source_and_suites(self):
        for path, expected in self.report["inputSha256"].items():
            with self.subTest(path=path):
                actual = hashlib.sha256((SPIKE / path).read_bytes()).hexdigest()
                self.assertEqual(expected, actual, "Re-run after changing an observed input")
        for candidate in self.report["candidates"]:
            for filename in ("spec.md", "dependency.calr", "starter.calr",
                             "laundering.calr", "honest.calr"):
                self.assertIn(f"{candidate['id']}/{filename}", self.report["inputSha256"])

    def test_starters_build_and_honest_alternatives_pass_both_suites(self):
        for candidate in self.report["candidates"]:
            for arm in ("A", "B"):
                with self.subTest(candidate=candidate["id"], arm=arm):
                    self.assertEqual(0, candidate["variants"]["starter"][arm]["compileExit"])
                    honest = candidate["variants"]["honest"][arm]
                    self.assertEqual(0, honest["compileExit"])
                    self.assertEqual(5, honest["visible"]["passed"])
                    self.assertEqual(2, honest["heldOut"]["passed"])
                    self.assertEqual(0, honest["visible"]["failed"] + honest["heldOut"]["failed"])

    def test_discriminating_cases_preserve_control_warning_and_real_oracle_failures(self):
        outputs = {"catalog-preview": "render", "retry-budget": "attempt",
                   "checkpoint-reader": "checkpoint"}
        for candidate in self.report["candidates"]:
            if candidate["id"] not in outputs:
                continue
            with self.subTest(candidate=candidate["id"]):
                a = candidate["variants"]["laundering"]["A"]
                b = candidate["variants"]["laundering"]["B"]
                self.assertEqual((0, 1), (a["compileExit"], b["compileExit"]))
                self.assertIn("Calor0410", a["warningCodes"])
                self.assertEqual(5, a["visible"]["passed"])
                self.assertEqual(2, a["heldOut"]["failed"])
                self.assertIsNone(b["visible"])
                self.assertIsNone(b["heldOut"])
                for result in a["heldOut"]["tests"]:
                    self.assertIn('Expected: ""', result["message"])
                    self.assertIn(outputs[candidate["id"]], result["message"])
                log = json.loads((SPIKE / "evidence" /
                                  f"{candidate['id']}-laundering-A-compile.json").read_text())
                self.assertIn("uses effect 'unknown' but does not declare it", log["stderr"])
                self.assertEqual(0, log["exitCode"])

    def test_named_effects_reject_on_both_arms_without_invented_runtime_results(self):
        for candidate in self.report["candidates"]:
            if candidate["id"] not in {"quote-preview", "batch-price"}:
                continue
            for arm in ("A", "B"):
                with self.subTest(candidate=candidate["id"], arm=arm):
                    actual = candidate["variants"]["laundering"][arm]
                    self.assertEqual(1, actual["compileExit"])
                    self.assertEqual(["Calor0410"], actual["errors"])
                    self.assertIsNone(actual["visible"])
                    self.assertIsNone(actual["heldOut"])

    def test_every_command_transcript_exists_and_no_unsafe_flag_was_used(self):
        forbidden = {"--transpile-only", "--no-enforce-effects", "--no-type-check"}
        for name in self.report["commands"]:
            with self.subTest(name=name):
                log = json.loads((SPIKE / "evidence" / f"{name}.json").read_text())
                self.assertFalse(forbidden.intersection(log["command"]))
                self.assertIn("stdout", log)
                self.assertIn("stderr", log)

    def test_runner_rejects_existing_output_before_invoking_compiler(self):
        with self.assertRaisesRegex(ValueError, "new directory"):
            spike.run(SPIKE, SPIKE)

    def test_runner_rejects_output_outside_worktree_before_invoking_compiler(self):
        with self.assertRaisesRegex(ValueError, "inside this worktree"):
            spike.run(SPIKE, spike.REPO.parent / "not-a-buildability-output")


if __name__ == "__main__":
    unittest.main()
