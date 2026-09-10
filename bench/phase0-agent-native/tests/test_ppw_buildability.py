"""Pins the executed #1255 spike without interpreting a scientific verdict."""

import hashlib
import importlib.util
import json
from pathlib import Path
import re
import unittest


SPIKE = Path(__file__).resolve().parents[1] / "buildability/1255"
spec = importlib.util.spec_from_file_location("buildability_spike", SPIKE / "run.py")
spike = importlib.util.module_from_spec(spec)
spec.loader.exec_module(spike)


class BuildabilityEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = json.loads((SPIKE / "evidence/results.json").read_text())

    def test_observations_pin_release_and_all_six_shapes(self):
        self.assertEqual(spike.RELEASE, self.report["compilerCommit"])
        self.assertEqual("deterministic-buildability-spike", self.report["kind"])
        self.assertIsNone(self.report["collectionEpoch"])
        self.assertEqual({"A": ["--permissive-effects"], "B": []}, self.report["arms"])
        self.assertEqual({1, 7, 8, 9, 11, 12}, {c["shape"] for c in self.report["candidates"]})
        self.assertEqual(7, len(self.report["candidates"]))
        self.assertEqual(26, self.report["rowTable"]["passed"])
        self.assertEqual(0, self.report["rowTable"]["failed"])

    def test_observations_match_executed_source_and_suites(self):
        required = {"run.py", "candidates.json", "runtime/RuntimeTests.csproj",
                    "runtime/VisibleTests.cs", "runtime/HeldOutTests.cs",
                    "runtime/StateHeldOutTests.cs", "runtime/NuGet.Config"}
        for candidate in self.report["candidates"]:
            required.update(f"{candidate['id']}/{name}" for name in
                            ("spec.md", "dependency.calr.inc", "starter.calr.inc",
                             "laundering.calr.inc", "honest.calr.inc"))
        self.assertEqual(required, set(self.report["inputSha256"]))
        for path, expected in self.report["inputSha256"].items():
            with self.subTest(path=path):
                actual = hashlib.sha256((SPIKE / path).read_bytes()).hexdigest()
                self.assertEqual(expected, actual, "Re-run after changing an observed input")
        for candidate in self.report["candidates"]:
            for filename in ("spec.md", "dependency.calr.inc", "starter.calr.inc",
                             "laundering.calr.inc", "honest.calr.inc"):
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
        expected = {"row-table", "z3", "sdk", "compiler-version", "runtime-info"}
        for candidate in self.report["candidates"]:
            for variant in ("starter", "laundering", "honest"):
                for arm in ("A", "B"):
                    prefix = f"{candidate['id']}-{variant}-{arm}"
                    expected.add(f"{prefix}-compile")
                    if variant == "honest" or (
                            variant == "laundering" and arm == "A"
                            and candidate["id"] not in {"quote-preview", "batch-price"}):
                        expected.update(f"{prefix}-{suite}" for suite in ("visible", "heldOut"))
        self.assertEqual(expected, set(self.report["commands"]))
        self.assertEqual(len(expected), len(self.report["commands"]))
        self.assertEqual(expected | {"results"},
                         {path.stem for path in (SPIKE / "evidence").glob("*.json")})
        for name in self.report["commands"]:
            with self.subTest(name=name):
                log = json.loads((SPIKE / "evidence" / f"{name}.json").read_text())
                self.assertFalse(forbidden.intersection(log["command"]))
                self.assertIn("stdout", log)
                self.assertIn("stderr", log)

    def test_all_summaries_reconcile_with_transcripts_and_retained_trx(self):
        for candidate in self.report["candidates"]:
            for variant, arms in candidate["variants"].items():
                for arm, result in arms.items():
                    name = f"{candidate['id']}-{variant}-{arm}"
                    log = json.loads((SPIKE / "evidence" / f"{name}-compile.json").read_text())
                    with self.subTest(command=name):
                        self.assertEqual(result["compileExit"], log["exitCode"])
                        self.assertEqual(result["errors"],
                                         re.findall(r"\berror (Calor\d+):", log["stderr"]))
                        self.assertEqual(result["warningCodes"],
                                         re.findall(r"\bwarning (Calor\d+):", log["stderr"]))
                        source = f"<work>/{candidate['id']}/{variant}/{arm}/Candidate"
                        expected = [
                            "dotnet", "<compiler-root>/src/Calor.Compiler/bin/Debug/net10.0/calor.dll",
                            "-i", f"{source}.calr", "-o", f"{source}.g.cs", "--no-telemetry",
                        ] + (["--permissive-effects"] if arm == "A" else [])
                        self.assertEqual(expected, log["command"])
                    for suite in ("visible", "heldOut"):
                        if result[suite] is None:
                            continue
                        prefix = f"{name}-{suite}"
                        runtime = json.loads((SPIKE / "evidence" / f"{prefix}.json").read_text())
                        trx = spike.trx_results(SPIKE / "evidence" / f"{prefix}.trx")
                        with self.subTest(command=prefix):
                            self.assertEqual(result[suite], trx)
                            self.assertEqual(1 if trx["failed"] else 0, runtime["exitCode"])
                            project = f"<work>/{candidate['id']}/{variant}/{arm}/{suite}"
                            self.assertEqual([
                                "dotnet", "test", f"{project}/RuntimeTests.csproj",
                                "--logger", "trx;LogFileName=tests.trx",
                                "--logger", "console;verbosity=normal",
                                "--results-directory", f"{project}/results",
                                "--verbosity", "quiet",
                                "-p:ImportDirectoryBuildProps=false",
                                "-p:ImportDirectoryBuildTargets=false",
                                "-p:ImportDirectoryPackagesProps=false",
                                "-p:ManagePackageVersionsCentrally=false",
                                "-p:RestoreConfigFile=<repo>/bench/phase0-agent-native/"
                                "buildability/1255/runtime/NuGet.Config",
                            ], runtime["command"])
        self.assertEqual(self.report["rowTable"],
                         spike.trx_results(SPIKE / "evidence/row-table.trx"))
        self.assertEqual(0, json.loads((SPIKE / "evidence/row-table.json").read_text())["exitCode"])

    def test_silent_control_candidate_is_distinct_from_warning_cases(self):
        candidate = next(c for c in self.report["candidates"] if c["id"] == "quota-lookup")
        a, b = (candidate["variants"]["laundering"][arm] for arm in ("A", "B"))
        self.assertEqual((0, 1), (a["compileExit"], b["compileExit"]))
        self.assertEqual([], a["warningCodes"])
        self.assertEqual(5, a["visible"]["passed"])
        self.assertEqual(2, a["heldOut"]["failed"])
        log = json.loads((SPIKE / "evidence/quota-lookup-laundering-A-compile.json").read_text())
        self.assertEqual("", log["stderr"])
        self.assertIn("Compilation successful:", log["stdout"])
        for test in a["heldOut"]["tests"]:
            self.assertIn('Expected: ""', test["message"])
            self.assertIn("lookup", test["message"])

    def test_runtime_has_explicit_package_versions_and_same_instance_adapters(self):
        self.assertRegex(self.report["originatingCheckout"], r"^[0-9a-f]{40}$")
        dependencies = self.report["runtimeDependencies"]
        for package in ("Microsoft.NET.Test.Sdk/17.8.0", "xunit/2.6.2",
                        "xunit.runner.visualstudio/2.5.4"):
            self.assertIn(package, dependencies)
        for package in dependencies.values():
            self.assertEqual("package", package["type"])
            self.assertTrue(package["sha512"])
        for candidate in json.loads((SPIKE / "candidates.json").read_text()):
            self.assertNotIn("new ", candidate["invoke"])
            if candidate["id"] != "batch-price":
                self.assertIn("new ", candidate["setup"])

    def test_mutation_candidate_has_no_visible_output_and_real_state_failures(self):
        candidate = next(c for c in self.report["candidates"] if c["id"] == "quota-adapter")
        a, b = (candidate["variants"]["laundering"][arm] for arm in ("A", "B"))
        self.assertEqual((0, 1), (a["compileExit"], b["compileExit"]))
        self.assertEqual([], a["warningCodes"])
        self.assertEqual(5, a["visible"]["passed"])
        self.assertEqual(2, a["heldOut"]["failed"])
        compile_log = json.loads(
            (SPIKE / "evidence/quota-adapter-laundering-A-compile.json").read_text())
        self.assertEqual("", compile_log["stderr"])
        tree = spike.ET.parse(SPIKE / "evidence/quota-adapter-laundering-A-visible.trx")
        for stdout in tree.findall(".//t:StdOut", spike.TRX):
            for line in (stdout.text or "").splitlines():
                if line.strip():
                    self.assertRegex(
                        line, r"^\[xUnit.net [0-9:.]+\]\s+(xUnit.net VSTest Adapter .*|"
                              r"(Discovering|Discovered|Starting|Finished):\s+RuntimeTests)$")
        for test in a["heldOut"]["tests"]:
            expected = int(re.search(r"Expected: (\d+)", test["message"]).group(1))
            actual = int(re.search(r"Actual:\s+(\d+)", test["message"]).group(1))
            self.assertEqual(expected + 1, actual)

    def test_runner_rejects_existing_output_before_invoking_compiler(self):
        with self.assertRaisesRegex(ValueError, "new directory"):
            spike.run(SPIKE, SPIKE)

    def test_runner_rejects_output_outside_worktree_before_invoking_compiler(self):
        with self.assertRaisesRegex(ValueError, "inside this worktree"):
            spike.run(SPIKE, spike.REPO.parent / "not-a-buildability-output")


if __name__ == "__main__":
    unittest.main()
