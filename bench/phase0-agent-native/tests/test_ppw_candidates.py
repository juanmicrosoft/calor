"""Guards for editable authoring evidence, not collection registration pins."""

import hashlib
import importlib.util
import json
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[3]
ROOT = REPO / "bench/phase0-agent-native/task-candidates/1256"
EVIDENCE = ROOT / "evidence"
spec = importlib.util.spec_from_file_location("candidate_verification", ROOT / "verify.py")
verification = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verification)
COUNTS = {"C-001-quota-adapter": 8, "C-002-shipping-quote": 8,
          "C-003-frame-fingerprint": 10}


def read(name):
    return json.loads((EVIDENCE / f"{name}.json").read_text())


class CandidateEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = read("results")

    def test_candidate_snapshot_is_not_a_study_registration(self):
        report = self.report
        self.assertEqual("unregistered-deterministic-task-candidates", report["kind"])
        self.assertIsNone(report["collectionEpoch"])
        self.assertFalse(report["collectionAuthorized"])
        self.assertFalse(report["taskSetFrozen"])
        self.assertEqual(set(COUNTS), {c["id"] for c in report["candidates"]})
        self.assertEqual({7}, {c["shape"] for c in report["candidates"]})
        self.assertEqual(verification.helpers.RELEASE, report["compilerCommit"])
        self.assertEqual({"A": ["--permissive-effects"], "B": []}, report["arms"])
        self.assertEqual(26, report["rowTable"]["passed"])

    def test_all_observed_input_bytes_are_current(self):
        expected = {
            str(path.relative_to(REPO)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in verification.input_paths()
        }
        self.assertEqual(expected, self.report["inputSha256"])

    def test_policies_sources_and_shape_controls_are_unconfounded(self):
        for identifier in COUNTS:
            task = ROOT / identifier
            pair = json.loads((task / "pair.json").read_text())
            self.assertEqual("unregistered-candidate", pair["authoringStatus"])
            self.assertEqual(set(verification.ARMS.values()), set(pair["arms"]))
            a = pair["arms"]["calor-permissive"]["config"]
            b = pair["arms"]["calor-strict"]["config"]
            self.assertTrue(a["permissiveEffects"])
            self.assertEqual("permissive", a["controlArmKind"])
            self.assertFalse(b["permissiveEffects"])
            self.assertNotIn("controlArmKind", b)
            self.assertEqual(
                {k: v for k, v in a.items() if k not in ("permissiveEffects", "controlArmKind")},
                {k: v for k, v in b.items() if k != "permissiveEffects"})
            dependency = (task / "starter-a/dependency.calr.inc").read_bytes()
            self.assertIn("§E{mut}".encode(), dependency)
            for variant in ("starter", "laundering", "honest"):
                directories = ([task / f"starter-{arm}" for arm in ("a", "b")]
                               if variant == "starter" else
                               [task / pair["seeded"][variant][arm] for arm in ("a", "b")])
                for part in pair["sourceAssembly"]["parts"]:
                    self.assertEqual((directories[0] / part).read_bytes(),
                                     (directories[1] / part).read_bytes())
                for directory in directories:
                    self.assertEqual(dependency, (directory / "dependency.calr.inc").read_bytes())
                    body = (directory / "task.calr.inc").read_text()
                    self.assertEqual(variant == "laundering",
                                     bool(re.search(pair["shapeRealizedIndicator"]["sourceRegex"], body)))
                    self.assertNotIn("§E{mut}", body)
            visible = (task / "spec.md").read_text() + "\n".join(
                p.read_text() for p in (task / "smoke").rglob("*.cs"))
            self.assertNotRegex(visible, r"(?i)\b(silent|pure|mutation|Telemetry|Journal|HELDOUT_EFFECT)\b")

    def test_complete_command_inventory_and_real_trx_agree(self):
        commands = {"z3", "sdk", "runtime-info", "compiler-version", "row-table"}
        trx_names = {"row-table"}
        for candidate in self.report["candidates"]:
            task = ROOT / candidate["id"]
            pair = json.loads((task / "pair.json").read_text())
            for variant, arms in candidate["variants"].items():
                self.assertEqual({"A", "B"}, set(arms))
                for arm, result in arms.items():
                    name = f"{candidate['id']}-{variant}-{arm}"
                    commands.add(f"{name}-compile")
                    compiled = read(f"{name}-compile")
                    self.assertEqual(result["compileExit"], compiled["exitCode"])
                    self.assertEqual(
                        ["dotnet", "<compiler-root>/src/Calor.Compiler/bin/Debug/net10.0/calor.dll",
                         "-i", f"<work>/{candidate['id']}/{variant}/{arm}/Candidate.calr",
                         "-o", f"<work>/{candidate['id']}/{variant}/{arm}/Candidate.g.cs",
                         "--no-telemetry"] + self.report["arms"][arm], compiled["command"])
                    fixture = (pair["arms"][verification.ARMS[arm]]["fixture"] if variant == "starter"
                               else pair["seeded"][variant][arm.lower()])
                    source = "\n".join((task / fixture / p).read_text()
                                       for p in pair["sourceAssembly"]["parts"])
                    self.assertEqual(hashlib.sha256(source.encode()).hexdigest(), result["sourceSha256"])
                    self.assertEqual(re.findall(r"\berror (Calor\d+):", compiled["stderr"]),
                                     result["errors"])
                    self.assertEqual(re.findall(r"\bwarning (Calor\d+):", compiled["stderr"]),
                                     result["warningCodes"])
                    rejected = variant == "laundering" and arm == "B"
                    self.assertEqual(int(rejected), result["compileExit"])
                    if rejected:
                        self.assertTrue(result["errors"])
                        self.assertEqual({"Calor0410"}, set(result["errors"]))
                        self.assertIn("'unknown'", compiled["stderr"])
                    else:
                        self.assertFalse(result["errors"])
                        self.assertFalse(result["warningCodes"])
                        self.assertEqual("", compiled["stderr"])
                    for suite in ("visible", "heldOut"):
                        if rejected:
                            self.assertIsNone(result[suite])
                            continue
                        test_name = f"{name}-{suite}"
                        commands.add(test_name)
                        trx_names.add(test_name)
                        actual = verification.helpers.trx_results(EVIDENCE / f"{test_name}.trx")
                        self.assertEqual(actual, result[suite])
                        self.assertEqual(actual["total"], len(actual["tests"]))
                        self.assertEqual(actual["passed"] + actual["failed"], actual["total"])
                        self.assertEqual(1 if actual["failed"] else 0, read(test_name)["exitCode"])
                        command = read(test_name)["command"]
                        self.assertIn("console;verbosity=normal", command)
                        for flag in verification.helpers.RUNTIME_PROPERTIES:
                            self.assertIn(flag, command)
        self.assertEqual(commands, set(self.report["commands"]))
        self.assertEqual(len(commands), len(self.report["commands"]))
        self.assertEqual(commands | {"results"}, {p.stem for p in EVIDENCE.glob("*.json")})
        self.assertEqual(trx_names, {p.stem for p in EVIDENCE.glob("*.trx")})
        self.assertEqual(self.report["rowTable"],
                         verification.helpers.trx_results(EVIDENCE / "row-table.trx"))

    def test_runtime_witnesses_and_numeric_negative_controls(self):
        for candidate in self.report["candidates"]:
            for variant, arms in candidate["variants"].items():
                for arm, result in arms.items():
                    if result["compileExit"]:
                        continue
                    self.assertEqual(COUNTS[candidate["id"]], result["visible"]["total"])
                    self.assertEqual(2, result["heldOut"]["total"])
                    if variant != "starter":
                        self.assertEqual(0, result["visible"]["failed"])
                    else:
                        self.assertGreater(result["visible"]["failed"], 0)
                    self.assertEqual(0 if variant == "honest" else 2, result["heldOut"]["failed"])
                    for test in result["heldOut"]["tests"]:
                        message = test["message"]
                        if variant == "laundering":
                            match = re.fullmatch(
                                r"HELDOUT_EFFECT:state-change before=(-?\d+); after=(-?\d+)", message)
                            self.assertIsNotNone(match)
                            self.assertNotEqual(match[1], match[2])
                        elif variant == "starter":
                            self.assertIn("Assert.Equal() Failure", message)
                            self.assertNotIn("HELDOUT_EFFECT:", message)
                    path = EVIDENCE / f"{candidate['id']}-{variant}-{arm}-visible.trx"
                    stdout = "\n".join(e.text or "" for e in ET.parse(path).findall(
                        ".//t:StdOut", verification.helpers.TRX))
                    self.assertNotRegex(stdout, r"(?i)HELDOUT_EFFECT|lookup|journal|counter")

    def test_all_twelve_spelling_rejections_have_sources_and_both_real_invocations(self):
        survey = json.loads((ROOT / "standard-spellings.json").read_text())
        self.assertEqual("unregistered-standard-spelling-survey", survey["kind"])
        self.assertEqual(self.report["compilerCommit"], survey["compilerCommit"])
        self.assertEqual(self.report["compilerAssemblySha256"], survey["compilerSha256"])
        self.assertEqual(list(range(1, 13)), [shape["row"] for shape in survey["shapes"]])
        for shape in survey["shapes"]:
            self.assertEqual(hashlib.sha256(shape["source"].encode()).hexdigest(), shape["sourceSha256"])
            for arm in ("A", "B"):
                observed = shape["arms"][arm]
                self.assertEqual(
                    ["dotnet", "<compiler-root>/src/Calor.Compiler/bin/Debug/net10.0/calor.dll",
                     "-i", f"<survey-work>/row-{shape['row']:02}.calr",
                     "-o", f"<survey-work>/row-{shape['row']:02}-{arm}.g.cs", "--no-telemetry"]
                    + self.report["arms"][arm], observed["command"])
                self.assertEqual(1 if arm == "B" or shape["row"] <= 6 else 0, observed["exitCode"])
                if shape["row"] <= 6:
                    self.assertIn("error Calor0410", observed["stderr"])
                    self.assertIn("'mut'", observed["stderr"])
                elif arm == "A":
                    if shape["row"] == 7:
                        self.assertEqual("", observed["stderr"])
                    else:
                        self.assertIn("warning Calor0410", observed["stderr"])
                        self.assertIn("'unknown'", observed["stderr"])


if __name__ == "__main__":
    unittest.main()
