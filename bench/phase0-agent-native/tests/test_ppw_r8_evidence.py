"""Corroborate committed R8 observations without treating them as agent data."""
import hashlib
import json
from pathlib import Path
import re
import unittest

BENCH = Path(__file__).resolve().parents[1]
TASKS = BENCH / "tasks/ppw-redesign"
SUITES = BENCH / "task-validation/1257/evidence"
R8 = BENCH / "task-validation/1258"
EXPECTED = ("C-001-quota-adapter", "C-002-shipping-quote", "C-003-frame-fingerprint")


class R8EvidenceTests(unittest.TestCase):
    def test_actual_verdicts_use_one_release_and_one_policy_difference(self):
        matrix = json.loads((SUITES / "results.json").read_text())
        compiler_hashes = {v for k, v in matrix["inputSha256"].items()
                           if k.endswith("/Calor.Compiler/bin/Release/net10.0/calor.dll")}
        self.assertEqual(1, len(compiler_hashes))
        for task in EXPECTED:
            root = TASKS / task / "evidence/r8"
            report = json.loads((root / "observation.json").read_text())
            self.assertEqual(task, report["task"])
            self.assertEqual(7, report["registeredShape"])
            self.assertEqual(matrix["compilerCommit"], report["compilerCommit"])
            self.assertIn(report["compilerSha256"], compiler_hashes)
            self.assertEqual("Release", report["compilerConfiguration"])
            self.assertEqual("A accepts without diagnostics; B rejects unknown with Calor0410.",
                             report["verdictDifference"])
            self.assertFalse(report["agentInvoked"])
            self.assertFalse(report["suiteReexecuted"])
            a, b = [json.loads((root / f"{arm}.json").read_text()) for arm in ("A", "B")]
            self.assertEqual(a, report["arms"]["A"])
            self.assertEqual(b, report["arms"]["B"])
            self.assertEqual(["--permissive-effects"], a["policyFlags"])
            self.assertEqual([], b["policyFlags"])
            self.assertEqual(0, a["exitCode"])
            self.assertIsNone(re.search(r"(?:warning|error) Calor\d+", a["stdout"] + a["stderr"]))
            self.assertEqual(1, b["exitCode"])
            self.assertIn("unknown", b["stderr"])
            self.assertEqual({"0410"}, set(re.findall(r"error Calor(\d+)", b["stderr"])))

            def comparable(command):
                result = list(command)
                if "--permissive-effects" in result:
                    result.remove("--permissive-effects")
                result[result.index("-o") + 1] = "<separate-output>"
                return result

            self.assertEqual(comparable(a["command"]), comparable(b["command"]))
            self.assertEqual(["dotnet"], a["command"][:1])
            self.assertTrue(a["command"][1].endswith("/Calor.Compiler/bin/Release/net10.0/calor.dll"))
            self.assertTrue((root / "A.generated.cs.txt").read_text().strip())
            self.assertFalse((root / "B.generated.cs.txt").exists())

    def test_cli_input_is_exactly_the_sdk_observed_input(self):
        matrix = json.loads((SUITES / "results.json").read_text())
        for task in matrix["tasks"]:
            self.assertIn(task["id"], EXPECTED)
            source = (TASKS / task["id"] / "evidence/r8/source.calr.txt").read_bytes()
            for arm in ("a", "b"):
                self.assertEqual(source, (SUITES / task["id"] / "laundering" / arm / "source.calr.txt").read_bytes())
                self.assertEqual(hashlib.sha256(source).hexdigest(),
                                 task["variants"]["laundering"][arm]["sourceSha256"])
            report = json.loads((TASKS / task["id"] / "evidence/r8/observation.json").read_text())
            self.assertEqual(hashlib.sha256(source).hexdigest(), report["sourceSha256"])

    def test_index_is_three_tasks_and_one_shape_not_six_study_runs(self):
        index = json.loads((R8 / "index.json").read_text())
        self.assertEqual("R8-observations-not-agent-data", index["kind"])
        self.assertEqual(3, index["tasks"])
        self.assertEqual([7], index["distinctShapes"])
        self.assertEqual(set(EXPECTED), {r["task"] for r in index["observations"]})
        for report in index["observations"]:
            actual = json.loads((TASKS / report["task"] / "evidence/r8/observation.json").read_text())
            self.assertEqual(actual, report)


if __name__ == "__main__":
    unittest.main()
