"""Guards for actual deterministic suite observations, not agent outcomes."""
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

BENCH = Path(__file__).resolve().parents[1]
VALIDATION = BENCH / "task-validation/1257"
EVIDENCE = VALIDATION / "evidence"
TASKS = BENCH / "tasks/ppw-redesign"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


class SuiteFreezeEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads((EVIDENCE / "results.json").read_text())
        spec = importlib.util.spec_from_file_location("suite_validation", VALIDATION / "run.py")
        cls.runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.runner)

    def test_exact_observed_task_inputs_are_preserved(self):
        actual = self.runner.task_input_hashes(TASKS)
        self.assertEqual(self.report["taskArtifactSha256"], actual)
        self.assertFalse(self.report["agentInvoked"])
        self.assertFalse(self.report["epochCreated"])
        self.assertEqual(self.runner.RELEASE, self.report["compilerCommit"])
        self.assertEqual("Release", self.report["configuration"]["compiler"])
        self.assertEqual("Release", self.report["configuration"]["tasks"])
        self.assertEqual(3, len(self.report["tasks"]))
        for task in self.report["tasks"]:
            pair = json.loads((TASKS / task["id"] / "pair.json").read_text())
            self.assertEqual(7, pair["registeredShape"])
            self.assertEqual("source-suites-frozen-on-merge-of-1364", pair["freezeStatus"])
            self.assertIn("not full task", pair["freezeScope"])

    def test_derived_evidence_does_not_change_frozen_execution_inputs(self):
        self.assertFalse(self.runner.is_task_input(Path("C-001/evidence/r8/a.json")))
        for path in ("C-001/pair.json", "C-001/spec.md", "C-001/starter-a/task.calr.inc",
                     "C-001/tests/HeldOutTests.cs", "C-001/starter-a/evidence/extra.calr"):
            self.assertTrue(self.runner.is_task_input(Path(path)))

    def test_scoped_freeze_locks_source_suites_and_observed_report(self):
        freeze = json.loads((VALIDATION / "artifact-freeze.json").read_text())
        self.assertEqual("pp-w-1257-source-suite-artifact-freeze", freeze["kind"])
        self.assertEqual("merge of PR #1364", freeze["effectiveOn"])
        self.assertIn("epoch registration or pins", freeze["notClaimed"])
        self.assertIn("spending or collection authorization", freeze["notClaimed"])
        self.assertEqual(hashlib.sha256((EVIDENCE / "results.json").read_bytes()).hexdigest(),
                         freeze["observationReportSha256"])
        expected = {k: v for k, v in self.report["taskArtifactSha256"].items()
                    if k.endswith((".calr.inc", ".cs"))}
        self.assertEqual(64, len(expected))
        self.assertEqual(expected, freeze["sourceAndSuiteSha256"])
        old = json.loads((VALIDATION / "evidence-preparation/results.json").read_text())
        for path, sha in expected.items():
            self.assertEqual(sha, old["taskArtifactSha256"][path])

    def test_preparation_observations_are_preserved_byte_for_byte(self):
        preservation = json.loads((VALIDATION / "preparation-preservation.json").read_text())
        root = VALIDATION / "evidence-preparation"
        actual = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                  for path in root.rglob("*") if path.is_file()}
        self.assertEqual(266, len(actual))
        self.assertEqual(preservation["files"], actual)
        freeze = json.loads((VALIDATION / "artifact-freeze.json").read_text())
        self.assertEqual(hashlib.sha256((root / "results.json").read_bytes()).hexdigest(),
                         freeze["preservedPreparationReportSha256"])

    def test_actual_control_outcomes_and_complete_case_counts(self):
        self.runner.validate_observations(self.report, EVIDENCE)
        builds = [arm for task in self.report["tasks"]
                  for arms in task["variants"].values() for arm in arms.values()]
        self.assertEqual(26, len(builds))
        self.assertEqual(19, sum(result["buildExit"] == 0 for result in builds))
        suites = [result[name] for result in builds for name in ("visible", "heldOut") if result[name]]
        self.assertEqual(240, sum(suite["total"] for suite in suites))
        self.assertEqual(135, sum(suite["passed"] for suite in suites))
        self.assertEqual(105, sum(suite["failed"] for suite in suites))

    def test_summary_matches_raw_build_commands_sources_and_trx(self):
        for task in self.report["tasks"]:
            pair = json.loads((TASKS / task["id"] / "pair.json").read_text())
            for variant, arms in task["variants"].items():
                for arm, outcome in arms.items():
                    directory = EVIDENCE / task["id"] / variant / arm
                    build = json.loads((directory / "build.json").read_text())
                    self.assertEqual(outcome["buildExit"], build["exitCode"])
                    self.assertEqual(["dotnet", "build"], build["command"][:2])
                    before = json.loads((directory / "policy-before.json").read_text())
                    after = json.loads((directory / "policy-after.json").read_text())
                    self.assertEqual(before, after)
                    fixture = f"starter-{arm}" if variant == "starter" else pair["seeded"][variant][arm]
                    source = b"".join((TASKS / task["id"] / fixture / part).read_bytes()
                                      for part in pair["sourceAssembly"]["parts"])
                    self.assertEqual(source, (directory / "source.calr.txt").read_bytes())
                    self.assertEqual(hashlib.sha256(source).hexdigest(), outcome["sourceSha256"])
                    project = (directory / "Src.csproj.txt").read_text()
                    policy = "true" if arm == "a" else "false"
                    self.assertIn(f"<CalorPermissiveEffects>{policy}</CalorPermissiveEffects>", project)
                    self.assertIn("/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll", project)
                    for suite in ("visible", "heldOut"):
                        if outcome[suite] is None:
                            self.assertFalse((directory / f"{suite}.trx").exists())
                            continue
                        raw = ET.parse(directory / f"{suite}.trx")
                        counts = raw.find(".//t:Counters", NS).attrib
                        for count in ("total", "passed", "failed"):
                            self.assertEqual(int(counts[count]), outcome[suite][count])
                        tests = [
                            {"name": test.attrib["testName"], "outcome": test.attrib["outcome"],
                             "message": test.findtext("t:Output/t:ErrorInfo/t:Message", "", NS)}
                            for test in raw.findall(".//t:UnitTestResult", NS)
                        ]
                        self.assertEqual(tests, outcome[suite]["tests"])


if __name__ == "__main__":
    unittest.main()
