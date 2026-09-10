"""Engineering-only fragment composition; no real task registration or agents."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import unittest
import xml.etree.ElementTree as ET

import test_ppw_instrument as instrument_tests
from ppw_redesign_epoch import BENCH, instrument, save, synthetic_inspection

assembly = instrument.helper("ppw-source-assembly.py")
capture = instrument.helper("harness-capture.py")
registration_helper = instrument.helper("ppw-registration.py")


class SourceAssemblyTests(unittest.TestCase):
    def setUp(self):
        self.fixture = instrument_tests.InstrumentTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root, self.epoch = self.fixture.root, self.fixture.epoch
        self.task = self.epoch / "tasks" / "SYNTHETIC-task"
        self.pair = json.loads((self.task / "pair.json").read_text())
        self.pair["sourceAssembly"] = {
            "parts": ["dependency.calr.inc", "task.calr.inc"],
            "editableParts": ["task.calr.inc"],
        }
        for arm in ("a", "b"):
            for directory, editable in (
                (self.task / ("starter-" + arm), "return 0\n"),
                (self.task / "seeded" / ("clean-" + arm), "return 1\n"),
                (self.task / "seeded" / ("laundering-" + arm), "this.lookup\n"),
            ):
                (directory / "Source.calr").unlink()
                (directory / "dependency.calr.inc").write_text("this.lookup\n")
                (directory / "task.calr.inc").write_text(editable)
        for directory in (self.epoch / "runs").rglob("final-src"):
            (directory / "Source.calr").unlink()
            (directory / "dependency.calr.inc").write_text("this.lookup\n")
            (directory / "task.calr.inc").write_text("this.lookup\n")
        save(self.task / "pair.json", self.pair)
        self.refresh_synthetic_inventory()

    def refresh_synthetic_inventory(self):
        registration = json.loads((self.epoch / "registration.json").read_text())
        registration["artifacts"] = {
            p.relative_to(self.epoch / "tasks").as_posix(): instrument.digest(p)
            for p in (self.epoch / "tasks").rglob("*") if p.is_file()
        }
        registration["replacementPins"]["starterBlobs"] = [
            {"task": "SYNTHETIC-task", "arm": arm,
             "path": path.relative_to(self.epoch / "tasks").as_posix(),
             "blobSha": registration_helper.blob_sha(path)}
            for arm in ("A", "B")
            for path in sorted((self.task / ("starter-" + arm.lower())).glob("*.calr.inc"))
        ]
        pair = json.loads((self.task / "pair.json").read_text())
        inspection = instrument.helper("ppw-source-inspection.py")
        try:
            controls = inspection.control_directories(pair, self.task)
        except ValueError:
            controls = None
        if controls is not None:
            registration["sourceInspections"] = {"SYNTHETIC-task": synthetic_inspection(pair, controls)}
        for arm, definition in instrument.ARMS.items():
            for directory in (self.epoch / "runs" / "SYNTHETIC-task" / definition["label"]).glob("run-*"):
                save(directory / "source-inspection.json", synthetic_inspection(
                    pair, {"baseline": self.task / ("starter-" + arm.lower()),
                           "final": directory / "final-src"}))
        save(self.epoch / "registration.json", registration)
        pins = json.loads((self.epoch / "pins.json").read_text())
        pins["registrationSha256"] = instrument.digest(self.epoch / "registration.json")
        save(self.epoch / "pins.json", pins)

    def workspace(self):
        workspace = self.root / "workspace"
        source = workspace / "src"
        source.mkdir(parents=True)
        for path in (self.task / "starter-b").iterdir():
            shutil.copyfile(path, source / path.name)
        capture.isolate_workspace(workspace)
        (source / "Src.csproj").write_text(
            '<Project><PropertyGroup><CalorEnforceEffects>true</CalorEnforceEffects>'
            '<CalorPermissiveEffects>false</CalorPermissiveEffects></PropertyGroup>'
            '<ItemGroup><CalorCompile Include="**/*.calr" /></ItemGroup></Project>')
        assembly.setup(self.pair, source)
        return workspace, source

    def test_composes_exact_order_only_under_obj_without_editing_fragments(self):
        _, source = self.workspace()
        self.assertFalse((source / assembly.OUTPUT).exists())
        original = (source / "task.calr.inc").read_bytes()
        output = assembly.compose(source / assembly.MANIFEST)
        self.assertEqual(source / "obj/ppw-source/Program.calr", output)
        self.assertEqual(b"this.lookup\n\nreturn 0\n", output.read_bytes())
        self.assertEqual(original, (source / "task.calr.inc").read_bytes())
        self.assertFalse((source / "Program.calr").exists())

    def test_editable_changes_keep_policy_constant_and_recompose(self):
        workspace, source = self.workspace()
        before = capture.policy_snapshot(workspace)
        assembly.compose(source / assembly.MANIFEST)
        (source / "task.calr.inc").write_text("this.lookup\n")
        output = assembly.compose(source / assembly.MANIFEST)
        self.assertEqual(b"this.lookup\n\nthis.lookup\n", output.read_bytes())
        self.assertEqual(before, capture.policy_snapshot(workspace))

    def test_immutable_edits_are_neither_compiled_nor_treated_as_task_failure(self):
        workspace, source = self.workspace()
        (source / "dependency.calr.inc").write_text("changed dependency\n")
        with self.assertRaisesRegex(ValueError, "immutable dependency"):
            assembly.compose(source / assembly.MANIFEST)
        with self.assertRaisesRegex(ValueError, "immutable dependency"):
            capture.policy_snapshot(workspace)

    def test_noncanonical_parts_require_safe_explicit_editable_inputs(self):
        for config in (
            None,
            {"parts": ["library.calr.inc", "task.calr.inc"]},
            {"parts": ["../dependency.calr.inc", "task.calr.inc"], "editableParts": ["task.calr.inc"]},
            {"parts": ["task.calr.inc", "task.calr.inc"], "editableParts": ["task.calr.inc"]},
            {"parts": ["dependency.calr.inc", "task.calr.inc"], "editableParts": ["foreign.calr.inc"]},
            {"parts": ["dependency.calr.inc", "task.calr.inc"],
             "editableParts": ["dependency.calr.inc", "task.calr.inc"]},
        ):
            with self.subTest(config=config), self.assertRaises(ValueError):
                assembly.definition({"sourceAssembly": config})

    def test_canonical_compact_parts_name_only_the_task_as_editable(self):
        compact = {"parts": ["dependency.calr.inc", "task.calr.inc"]}
        self.pair["sourceAssembly"] = compact
        _, source = self.workspace()
        manifest = json.loads((source / assembly.MANIFEST).read_text())
        self.assertEqual(["task.calr.inc"], manifest["sourceAssembly"]["editableParts"])
        self.assertEqual({"parts"}, set(compact))
        self.assertEqual({"dependency.calr.inc"}, set(assembly.workspace_snapshot(source)))

    def test_explicit_honest_alias_is_negative_and_resolves_the_reference(self):
        self.pair["seeded"]["honest"] = self.pair["seeded"].pop("clean")
        save(self.task / "pair.json", self.pair)
        self.refresh_synthetic_inventory()
        self.fixture.analyze()
        config = capture.resolve_pair_config(str(self.task / "pair.json"), "calor-strict", "calor")
        self.assertTrue(config["admitted"])
        self.assertEqual("seeded/clean-b", config["reference"])
        self.assertEqual("seeded-honest-declared", config["referenceSource"])
        self.pair["seeded"]["clean"] = dict(self.pair["seeded"]["honest"])
        save(self.task / "pair.json", self.pair)
        self.refresh_synthetic_inventory()
        self.fixture.analyze()

    def test_conflicting_honest_and_clean_roles_are_rejected(self):
        self.pair["seeded"]["honest"] = dict(self.pair["seeded"]["laundering"])
        save(self.task / "pair.json", self.pair)
        self.refresh_synthetic_inventory()
        config = capture.resolve_pair_config(str(self.task / "pair.json"), "calor-strict", "calor")
        self.assertFalse(config["admitted"])
        self.assertIn("conflicting seeded", config["reason"])
        with self.assertRaisesRegex(ValueError, "conflicting seeded"):
            self.fixture.analyze()

    def test_unchanged_candidate_layout_matches_observed_compilation_bytes(self):
        candidates = BENCH / "task-candidates/1256"
        report = json.loads((candidates / "evidence/results.json").read_text())
        for candidate in report["candidates"]:
            task = candidates / candidate["id"]
            pair = json.loads((task / "pair.json").read_text())
            self.assertEqual("unregistered-candidate", pair["authoringStatus"])
            self.assertEqual(["task.calr.inc"], assembly.definition(pair)["editableParts"])
            _, negative = capture.honest_reference_cells(pair)
            pattern = re.compile(pair["shapeRealizedIndicator"]["sourceRegex"])
            for arm, label in (("a", "calor-permissive"), ("b", "calor-strict")):
                self.assertTrue(capture.resolve_pair_config(str(task / "pair.json"), label, "calor")["reference"])
                immutable = assembly.check_fragments(pair, task / pair["arms"][label]["fixture"])
                for role, relative, positive in (
                    ("starter", pair["arms"][label]["fixture"], False),
                    ("honest", negative[arm], False),
                    ("laundering", pair["seeded"]["laundering"][arm], True),
                ):
                    with self.subTest(task=candidate["id"], role=role, arm=arm):
                        origin = task / relative
                        assembly.check_fragments(pair, origin, immutable)
                        self.assertEqual(positive, any(pattern.search(p.read_text())
                                                       for p in assembly.source_paths(pair, origin, True)))
                        source = self.root / "concrete" / candidate["id"] / role / arm
                        shutil.copytree(origin, source)
                        (source / "Src.csproj").write_text(
                            '<Project><ItemGroup><CalorCompile Include="**/*.calr" /></ItemGroup></Project>')
                        assembly.setup(pair, source)
                        output = assembly.compose(source / assembly.MANIFEST)
                        self.assertEqual(candidate["variants"][role][arm.upper()]["sourceSha256"],
                                         instrument.digest(output))

    def test_extra_compiled_source_is_not_an_alternate_edit_surface(self):
        workspace, source = self.workspace()
        (source / "Bypass.cs").write_text("// unregistered\n")
        with self.assertRaisesRegex(ValueError, "inventory"):
            capture.policy_snapshot(workspace)

    def test_generated_source_tampering_is_overwritten_before_compilation(self):
        _, source = self.workspace()
        output = assembly.compose(source / assembly.MANIFEST)
        output.write_text("tampered generated source\n")
        assembly.compose(source / assembly.MANIFEST)
        self.assertEqual("this.lookup\n\nreturn 0\n", output.read_text())

    def test_generated_output_cannot_link_outside_workspace(self):
        _, source = self.workspace()
        elsewhere = self.root / "elsewhere"
        elsewhere.mkdir()
        (source / "obj").symlink_to(elsewhere, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "linked generated"):
            assembly.compose(source / assembly.MANIFEST)
        self.assertEqual([], list(elsewhere.iterdir()))

    def test_fragment_hardlink_and_missing_newline_are_rejected(self):
        _, source = self.workspace()
        fragment = source / "task.calr.inc"
        os.link(fragment, self.root / "linked-fragment")
        with self.assertRaisesRegex(ValueError, "linked fragment"):
            assembly.compose(source / assembly.MANIFEST)
        (self.root / "linked-fragment").unlink()
        fragment.write_text("missing newline")
        with self.assertRaisesRegex(ValueError, "end with a newline"):
            assembly.compose(source / assembly.MANIFEST)

    def test_dependency_marker_does_not_count_as_agent_realizing_shape(self):
        for directory in (self.epoch / "runs").rglob("final-src"):
            (directory / "task.calr.inc").write_text("return 0\n")
        self.refresh_synthetic_inventory()
        result = self.fixture.analyze()
        self.assertEqual([0, 0], [cell["shapeRealized"] for cell in result["perCell"]])

    def test_clean_seed_cannot_quietly_change_dependency(self):
        (self.task / "seeded/clean-b/dependency.calr.inc").write_text("changed dependency\n")
        self.refresh_synthetic_inventory()
        with self.assertRaisesRegex(ValueError, "immutable dependency"):
            self.fixture.analyze()

    def test_honest_is_negative_and_laundering_is_positive_in_each_arm(self):
        self.fixture.analyze()
        for arm in ("a", "b"):
            for role, replacement, message in (
                ("clean", "this.lookup\n", "honest negative"),
                ("laundering", "return 1\n", "laundering positive"),
            ):
                path = self.task / "seeded" / (role + "-" + arm) / "task.calr.inc"
                original = path.read_text()
                with self.subTest(arm=arm, role=role):
                    path.write_text(replacement)
                    self.refresh_synthetic_inventory()
                    with self.assertRaisesRegex(ValueError, message):
                        self.fixture.analyze()
                path.write_text(original)
        self.refresh_synthetic_inventory()

    def test_control_roles_cannot_silently_default_back_to_clean_positive(self):
        for role in ("clean", "laundering"):
            original = self.pair["seeded"].pop(role)
            save(self.task / "pair.json", self.pair)
            self.refresh_synthetic_inventory()
            with self.subTest(role=role), self.assertRaisesRegex(ValueError, "explicit seeded." + role):
                self.fixture.analyze()
            self.pair["seeded"][role] = original
        save(self.task / "pair.json", self.pair)

    def test_laundering_positive_must_preserve_immutable_dependency(self):
        (self.task / "seeded/laundering-a/dependency.calr.inc").write_text("changed dependency\n")
        self.refresh_synthetic_inventory()
        with self.assertRaisesRegex(ValueError, "immutable dependency"):
            self.fixture.analyze()

    def test_preserved_quota_template_has_honest_negative_and_laundering_positive(self):
        fixture = BENCH / "buildability/1255/quota-adapter"
        pattern = re.compile(r"§C\{this\.lookup\}")
        for name, expected in (("starter", False), ("honest", False), ("laundering", True)):
            with self.subTest(control=name):
                text = (fixture / (name + ".calr.inc")).read_text()
                self.assertEqual(expected, bool(pattern.search(text)))

    def test_final_dependency_is_checked_against_frozen_original(self):
        final = self.fixture.result().parent / "final-src/dependency.calr.inc"
        final.write_text("changed dependency\n")
        with self.assertRaisesRegex(ValueError, "immutable dependency"):
            self.fixture.analyze()

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "shell capture tools required")
    def test_actual_shell_captures_fragments_and_rejects_dependency_tampering(self):
        captured = self.fixture.fake_capture("dependency-edit")
        self.assertTrue(json.loads((captured / "result.json").read_text())["invalid"])
        self.assertIn("immutable dependency", (captured / "invalid.txt").read_text())
        self.assertEqual({"dependency.calr.inc", "task.calr.inc"},
                         {p.name for p in (captured / "final-src").iterdir()})

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "shell capture tools required")
    def test_actual_shell_rejects_added_compiled_source(self):
        captured = self.fixture.fake_capture("extra-source")
        self.assertTrue(json.loads((captured / "result.json").read_text())["invalid"])
        self.assertIn("inventory", (captured / "invalid.txt").read_text())

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "shell capture tools required")
    def test_fragment_edit_is_journaled_and_captured_into_pilot_ledger(self):
        pins = json.loads((self.epoch / "pins.json").read_text())
        pins["compiler"].update(repoRoot=str(self.root / "product"),
                                calorDll=str(self.root / "product/calor.dll"))
        save(self.epoch / "pins.json", pins)
        for arm in ("A", "B"):
            for run in (1, 2):
                captured = self.fixture.fake_capture("fragment-edit", arm, run)
                entries = [json.loads(line) for line in (captured / "journal.jsonl").read_text().splitlines()]
                self.assertEqual(1, sum(entry["edited"] for entry in entries))
                self.assertTrue(all(entry["envelope_valid"] for entry in entries))
                envelope = json.loads((captured / ".envelope.json").read_text())
                self.assertEqual(arm == "A", envelope["syntheticPermissive"])
                self.assertEqual(["Program.calr"], envelope["syntheticInputs"])
                self.assertFalse((captured / "final-src/Program.calr").exists())
                instrument.stamp_run(captured / "result.json", pins)
                target = self.fixture.result(instrument.ARMS[arm]["label"], run).parent
                shutil.rmtree(target)
                shutil.copytree(captured, target)
        result = json.loads(instrument.record_stage(self.root, "synthetic-pilot", "pilot").read_text())
        self.assertFalse(result["empirical"])
        self.assertEqual([2, 2], [cell["shapeRealized"] for cell in result["perCell"]])

    @unittest.skipUnless(shutil.which("dotnet"), "real MSBuild required")
    def test_actual_generated_msbuild_target_composes_only_for_compilation(self):
        _, source = self.workspace()
        project = source / "Src.csproj"
        project.write_text(project.read_text().replace(
            "</Project>", '<Target Name="CompileCalorFiles" /></Project>'))
        environment = dict(os.environ, TMPDIR=str(self.root))
        result = subprocess.run(["dotnet", "msbuild", str(project), "-t:CompileCalorFiles", "-v:q"],
                                env=environment, capture_output=True, text=True, timeout=60)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("this.lookup\n\nreturn 0\n", (source / assembly.OUTPUT).read_text())

    @unittest.skipUnless(shutil.which("dotnet"), "real MSBuild required")
    def test_generated_compile_target_rejects_non_authoritative_inputs(self):
        _, source = self.workspace()
        project = source / "Src.csproj"
        xml = ET.parse(project)
        ET.SubElement(ET.SubElement(xml.getroot(), "ItemGroup"), "Compile", Include="obj/rogue.cs")
        ET.SubElement(xml.getroot(), "Target", Name="CompileCalorFiles")
        xml.write(project, encoding="unicode")
        result = subprocess.run(
            ["dotnet", "msbuild", str(project), "-t:AddCalorGeneratedFilesToCompile", "-v:q"],
            env=dict(os.environ, TMPDIR=str(self.root)), capture_output=True, text=True, timeout=60)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Unregistered C# input", result.stdout + result.stderr)

    def test_sdk_output_reset_cannot_delete_source_or_external_inputs(self):
        _, source = self.workspace()
        for path in (source / "task.calr.inc", self.root / "outside.cs"):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, "source obj"):
                assembly.reset_sdk_outputs(source, [str(path)])
        self.assertTrue((source / "task.calr.inc").exists())

    @unittest.skipUnless(shutil.which("dotnet") and
                         (BENCH.parent.parent / "src/Calor.Tasks/bin/Debug/net10.0/Calor.Tasks.dll").exists(),
                         "real built Calor.Tasks Debug product required")
    def test_real_calor_build_consumes_generated_file_without_inlining_dependency_into_edit_surface(self):
        repo = BENCH.parent.parent
        workspace = self.root / "real-workspace"
        source = workspace / "src"
        source.mkdir(parents=True)
        capture.isolate_workspace(workspace)
        fixture = BENCH / "buildability/1255/quota-adapter"
        dependency = (fixture / "dependency.calr.inc").read_bytes()
        starter = (fixture / "starter.calr.inc").read_bytes()
        (source / "dependency.calr.inc").write_bytes(dependency)
        (source / "task.calr.inc").write_bytes(starter)
        template = (BENCH / "templates/calor-arm/CalorArm.csproj.template").read_text()
        (source / "Src.csproj").write_text(template.replace("__REPO_ROOT__", str(repo))
                                          .replace("__CALOR_PERMISSIVE_EFFECTS__", "false"))
        assembly.setup(self.pair, source)
        before = capture.policy_snapshot(workspace)
        environment = dict(os.environ, TMPDIR=str(self.root))
        command = ["dotnet", "build", str(source / "Src.csproj"), "--nologo", "-v:q",
                   "-p:BuildProjectReferences=false",
                   "-p:CalorTasksAssembly=" + str(repo / "src/Calor.Tasks/bin/Debug/net10.0/Calor.Tasks.dll")]
        result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=180)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(dependency + b"\n" + starter, (source / assembly.OUTPUT).read_bytes())
        self.assertTrue((source / "bin/Debug/net10.0/Src.dll").exists())
        self.assertEqual(starter, (source / "task.calr.inc").read_bytes())
        self.assertEqual(before, capture.policy_snapshot(workspace))
        extra = source / "obj/calor/Unregistered.g.cs"
        extra.write_text("#error UNREGISTERED_GENERATED_INPUT\n")
        generated = list((source / "obj").rglob("*.cs"))
        self.assertGreaterEqual(len(generated), 4)
        for path in generated:
            path.write_text("#error UNREGISTERED_GENERATED_INPUT\n")
        result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=180)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse(extra.exists())
        self.assertEqual(before, capture.policy_snapshot(workspace))
        honest = (fixture / "honest.calr.inc").read_bytes()
        (source / "task.calr.inc").write_bytes(honest)
        result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=180)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(dependency + b"\n" + honest, (source / assembly.OUTPUT).read_bytes())
        self.assertEqual(honest, (source / "task.calr.inc").read_bytes())
        self.assertEqual(before, capture.policy_snapshot(workspace))


if __name__ == "__main__":
    unittest.main()
