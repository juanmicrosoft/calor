"""Expanded #1271 controls plus the explicit frozen-task pin supersession."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
import uuid

from ppw_redesign_epoch import BENCH, build, instrument, save

registration_helper = instrument.helper("ppw-registration.py")


class FrozenTaskSupersessionTests(unittest.TestCase):
    def setUp(self):
        self.path = BENCH / "registrations/ppw-redesign-task-supersession.json"
        self.registration = json.loads(self.path.read_text())
        self.root = BENCH.parent.parent / self.registration["taskRoot"]

    def test_actual_pins_preserve_history_and_replace_all_five_registration_groups(self):
        result = registration_helper.check_supersession(self.registration, self.root)
        self.assertEqual("514f538024df990af86054af25975b756ba42ab1", result["compilerCommit"])
        self.assertEqual({"tasks": 3, "blind": 3, "warningVsError": 0, "legB": 3}, result["pairCounts"])
        self.assertEqual(12, result["starterSlots"])
        self.assertEqual({"A": ["--permissive-effects"], "B": []},
                         self.registration["replacementPins"]["policies"])
        self.assertEqual(registration_helper.historical_pins(), self.registration["supersededPins"])
        self.assertEqual(12, sum(len(value) for value in
                                 self.registration["supersededPins"]["starterBlobs"].values()))
        self.assertEqual({"taskCount": 3, "distinctShapes": [7], "distinctShapeCount": 1},
                         self.registration["shapeInventory"])

    def test_source_suite_and_r8_evidence_remain_bound_to_the_actual_merged_freezes(self):
        repo = BENCH.parent.parent
        for key, commit in (
            ("sourceSuiteFreeze", "92d4517e89623c3450bc635ba1db9fc61c48acee"),
            ("armDiscrimination", "92b906be25bde814d7a772bae4c8f554c081d46e"),
        ):
            evidence = self.registration[key]
            self.assertEqual(commit, evidence["mergeCommit"])
            self.assertEqual(evidence["sha256"], instrument.digest(repo / evidence["manifest"]))
        freeze = json.loads((repo / self.registration["sourceSuiteFreeze"]["manifest"]).read_text())
        for path, digest in freeze["sourceAndSuiteSha256"].items():
            self.assertEqual(digest, instrument.digest(self.root / path), path)

    def test_every_actual_fragment_slot_is_a_git_blob_not_a_synthetic_hash(self):
        slots = self.registration["replacementPins"]["starterBlobs"]
        self.assertEqual(6, len({(slot["task"], slot["arm"]) for slot in slots}))
        for slot in slots:
            actual = subprocess.check_output(
                ["git", "hash-object", str(self.root / slot["path"])], text=True).strip()
            self.assertEqual(actual, slot["blobSha"])

    def test_native_control_certificates_and_complete_inventory_validate_offline(self):
        sha = self.registration["inspectionWitness"]["calorSha256"]
        self.assertEqual("8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0", sha)
        self.assertEqual({sha}, {value["compilerSha256"]
                                for value in self.registration["sourceInspections"].values()})
        instrument.validate_tasks(self.root, self.registration)
        self.assertEqual(85, len(self.registration["artifacts"]))

    def test_task_supersession_is_not_an_epoch_or_collection_authorization(self):
        self.assertFalse(self.registration["collectionAuthorized"])
        self.assertFalse(self.registration["epochCreated"])
        self.assertNotIn("stages", self.registration)
        self.assertNotIn("spendAuthorization", self.registration)
        with self.assertRaisesRegex(ValueError, "schemaVersion must be 2"):
            instrument.validate_registration(self.registration, "pilot", "not-an-epoch")


class RegistrationTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".registration-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.epoch = build(self.root)
        self.registration = json.loads((self.epoch / "registration.json").read_text())

    def check(self):
        return registration_helper.check_supersession(self.registration, self.epoch / "tasks")

    def test_synthetic_supersession_keeps_old_and_new_values_separate(self):
        old = self.registration["supersededPins"]
        self.assertEqual({"tasks": 6, "blind": 3, "warningVsError": 3, "legB": 5}, old["pairCounts"])
        self.assertEqual(6, len(old["starterBlobs"]))
        self.assertEqual(12, sum(len(arms) for arms in old["starterBlobs"].values()))
        self.assertEqual(2, self.check()["starterSlots"])
        self.assertEqual(1, self.check()["pairCounts"]["tasks"])

    def test_old_arm_and_count_pins_must_not_be_quietly_rewritten(self):
        for group in ("arms", "pairCounts", "starterBlobs", "starterFreezeCommit", "registration"):
            original = self.registration["supersededPins"][group]
            with self.subTest(group=group):
                self.registration["supersededPins"][group] = None
                with self.assertRaisesRegex(ValueError, "omitted/rewritten"):
                    self.check()
            self.registration["supersededPins"][group] = original

    def test_missing_actual_replacement_table_is_not_ready(self):
        del self.registration["replacementPins"]
        with self.assertRaisesRegex(ValueError, "after actual task freeze"):
            self.check()

    def test_replacement_counts_come_from_task_bytes_not_old_defaults(self):
        self.registration["replacementPins"]["pairCounts"] = self.registration["supersededPins"]["pairCounts"]
        with self.assertRaisesRegex(ValueError, "new pair counts"):
            self.check()

    def test_boolean_is_not_an_integer_pair_count(self):
        self.registration["replacementPins"]["pairCounts"]["tasks"] = True
        with self.assertRaisesRegex(ValueError, "new pair counts"):
            self.check()

    def test_replacement_policy_and_compiler_cannot_reintroduce_old_confound(self):
        replacement = self.registration["replacementPins"]
        replacement["compilerCommit"] = self.registration["supersededPins"]["arms"]["A"]["commit"]
        with self.assertRaisesRegex(ValueError, "compiler differs"):
            self.check()
        replacement["compilerCommit"] = self.registration["compilerCommit"]
        replacement["policies"]["B"] = ["--permissive-effects"]
        with self.assertRaisesRegex(ValueError, "only the policy flag"):
            self.check()

    def test_every_starter_slot_is_git_blob_pinned(self):
        for slot in self.registration["replacementPins"]["starterBlobs"]:
            source = self.epoch / "tasks" / slot["path"]
            actual = subprocess.check_output(["git", "hash-object", str(source)], text=True).strip()
            self.assertEqual(actual, slot["blobSha"])

    def test_edited_duplicate_or_missing_starter_slot_is_rejected(self):
        slots = self.registration["replacementPins"]["starterBlobs"]
        for changed in (slots[:1], slots + [slots[0]], [dict(slots[0], blobSha="f" * 40), slots[1]]):
            with self.subTest(changed=changed):
                self.registration["replacementPins"]["starterBlobs"] = changed
                with self.assertRaisesRegex(ValueError, "starter git-blob pins"):
                    self.check()
        self.registration["replacementPins"]["starterBlobs"] = slots

    def test_written_supersession_cause_is_mandatory(self):
        self.registration["cause"] = ""
        with self.assertRaisesRegex(ValueError, "written cause"):
            self.check()

    def test_task_class_cannot_be_implicitly_defaulted(self):
        pair_path = self.epoch / "tasks" / "SYNTHETIC-task" / "pair.json"
        pair = json.loads(pair_path.read_text())
        del pair["class"]
        save(pair_path, pair)
        with self.assertRaisesRegex(ValueError, "cell class"):
            self.check()

    def test_pilot_copy_nested_in_confirmatory_runs_is_refused(self):
        confirmation = build(self.root, "confirmatory")
        shutil.copytree(self.epoch, confirmation / "runs" / "pooled-pilot")
        with self.assertRaisesRegex(ValueError, "nested epoch/pooling"):
            instrument.analyze(self.root, "synthetic-confirmatory", "confirmatory")

    def test_cross_epoch_hardlink_is_refused(self):
        confirmation = build(self.root, "confirmatory")
        source = self.epoch / "runs/SYNTHETIC-task/calor-permissive/run-1/result.json"
        target = confirmation / "runs/SYNTHETIC-task/calor-permissive/run-1/result.json"
        target.unlink()
        os.link(source, target)
        with self.assertRaisesRegex(ValueError, "hard-linked epoch"):
            instrument.analyze(self.root, "synthetic-confirmatory", "confirmatory")

    def test_same_id_cannot_register_both_stages(self):
        self.registration["stages"]["confirmatory"]["epochId"] = "synthetic-pilot"
        with self.assertRaisesRegex(ValueError, "separate epoch ids"):
            instrument.validate_registration(self.registration, "pilot", "synthetic-pilot")

    def test_unrecognized_stage_role_is_rejected(self):
        self.registration["stages"]["pooled"] = {"epochId": "pooled", "runsPerArm": 2}
        with self.assertRaisesRegex(ValueError, "unrecognized registered stage"):
            instrument.validate_registration(self.registration, "pilot", "synthetic-pilot")

    def test_renaming_pilot_directory_does_not_promote_its_data(self):
        self.epoch.rename(self.root / "synthetic-confirmatory")
        with self.assertRaisesRegex(ValueError, "pins epochId"):
            instrument.analyze(self.root, "synthetic-confirmatory", "confirmatory")

    def test_wrong_stage_cli_emits_no_record_or_legacy_ledger(self):
        result = subprocess.run(
            [sys.executable, str(BENCH / "ppw-analyze.py"), "--epoch-id", "synthetic-pilot",
             "--stage", "confirmatory", "--epochs-root", str(self.root)],
            capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("wrong-stage", result.stderr)
        self.assertFalse((self.epoch / "ppw-stage-ledger.json").exists())
        self.assertFalse((self.root / "effect-rows-benefit-ledger.json").exists())

    def test_second_epoch_argument_is_not_silently_ignored(self):
        result = subprocess.run(
            [sys.executable, str(BENCH / "ppw-analyze.py"), "--epoch-id", "synthetic-confirmatory",
             "--epoch-id", "synthetic-pilot", "--stage", "pilot", "--epochs-root", str(self.root)],
            capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("--epoch-id must occur exactly once", result.stderr)
        self.assertFalse((self.epoch / "ppw-stage-ledger.json").exists())

    def test_repeated_stage_is_not_silently_overridden(self):
        result = subprocess.run(
            [sys.executable, str(BENCH / "ppw-analyze.py"), "--epoch-id", "synthetic-pilot",
             "--stage", "confirmatory", "--stage=pilot", "--epochs-root", str(self.root)],
            capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("--stage must occur exactly once", result.stderr)
        self.assertFalse((self.epoch / "ppw-stage-ledger.json").exists())

    def test_epoch_conditional_pair_reads_one_ledger_flag_and_keeps_skip_counts(self):
        repo = BENCH.parent.parent
        source = (repo / "tests/Calor.Compiler.Tests/Effects/EffectRowsBenefitLedgerTests.cs").read_text()
        for method, trigger in (
            ("BeforeTheEpochRunsTheSizingOffRampRecordsUnderpowered", 'Skip.If(root.GetProperty("epochRun").GetBoolean()'),
            ("WhenTheEpochIsArchivedTheLedgerCarriesItsPerCellReporting", 'Skip.If(!root.GetProperty("epochRun").GetBoolean()'),
        ):
            body = source.split("public void " + method + "()", 1)[1].split("\n    }", 1)[0]
            self.assertIn(trigger, body)
            self.assertNotIn("Directory.Exists", body)
        manifest = json.loads((repo / "eng/test-manifest.json").read_text())
        # The manifest may reorganize its nesting; the exact reason/count pair
        # remains the interface with the executed skip audit.
        def entries(value):
            if isinstance(value, dict):
                yield value
                for item in value.values():
                    yield from entries(item)
            elif isinstance(value, list):
                for item in value:
                    yield from entries(item)
        reasons = {entry.get("reason"): entry.get("count") for entry in entries(manifest) if "reason" in entry}
        self.assertEqual(1, reasons["ledger records epochRun true; the run assertions below cover it"])
        self.assertEqual(1, reasons["ledger records epochRun false"])


if __name__ == "__main__":
    unittest.main()
