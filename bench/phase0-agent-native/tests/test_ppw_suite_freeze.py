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
        actual = {
            path.relative_to(TASKS).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in TASKS.rglob("*") if path.is_file()
        }
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
            self.assertEqual("prepared-not-frozen", pair["freezeStatus"])

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
