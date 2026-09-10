"""Actual unrun input scaffolds, not synthetic or empirical collection data."""
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid

BENCH = Path(__file__).resolve().parents[1]
EPOCHS = BENCH / "epochs"
PILOT = EPOCHS / "w-rows-pilot-001"
CONFIRMATION = EPOCHS / "w-rows-001"


def load(path):
    return json.loads(path.read_text())


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class EpochScaffoldTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("scaffold_instrument", BENCH / "ppw-instrument.py")
        cls.instrument = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.instrument)
        spec = importlib.util.spec_from_file_location("scaffold_legacy", BENCH / "ppw-analyze.py")
        cls.legacy = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.legacy)
        cls.pins = load(PILOT / "pins.json")
        cls.registration = load(PILOT / "registration.json")
        cls.task_registration = load(BENCH / "registrations/ppw-redesign-task-supersession.json")

    def setUp(self):
        self.work = BENCH / "tests" / (".scaffold-test-" + uuid.uuid4().hex)
        self.work.mkdir()
        self.addCleanup(shutil.rmtree, self.work)

    def environment(self):
        return {key: value for key, value in os.environ.items()
                if not key.startswith(("CALOR_P0_", "CALOR_LOOP_"))}

    def test_both_task_snapshots_match_every_frozen_byte_and_native_certificate(self):
        self.assertEqual(85, len(self.registration["artifacts"]))
        self.assertEqual(self.task_registration["artifacts"], self.registration["artifacts"])
        for epoch in (PILOT, CONFIRMATION):
            with self.subTest(epoch=epoch.name):
                self.instrument.validate_tasks(epoch / "tasks", self.registration)
                for relative, sha in self.registration["artifacts"].items():
                    self.assertEqual(sha, digest(epoch / "tasks" / relative))
                    self.assertEqual((BENCH / "tasks/ppw-redesign" / relative).read_bytes(),
                                     (epoch / "tasks" / relative).read_bytes())

    def test_actual_pilot_pins_validate_with_their_own_complete_task_tree(self):
        self.instrument.validate_pins(self.pins, self.registration, "pilot", PILOT.name)
        self.instrument.validate_tasks(PILOT / "tasks", self.registration)
        self.assertEqual("scaffolded", self.pins["lifecycle"])
        self.assertEqual(digest(PILOT / "registration.json"), self.pins["registrationSha256"])
        self.assertEqual(74, self.pins["runsPerArm"])
        self.assertFalse(self.registration["collectionAuthorized"])
        self.assertEqual("unfunded", self.registration["fundingStatus"])

    def test_confirmation_is_a_bound_reservation_without_stage_two_sizing(self):
        pins = load(CONFIRMATION / "pins.json")
        registration = load(CONFIRMATION / "registration.json")
        self.assertEqual(1, pins["schemaVersion"])
        self.assertEqual("pp-w-rows-confirmatory-reservation", pins["kind"])
        self.assertEqual("reserved-unregistered", pins["lifecycle"])
        self.assertEqual("reserved-unregistered", registration["status"])
        self.assertEqual("pp-w-rows-stage-reservation", registration["kind"])
        self.assertEqual("unrun", pins["dataKind"])
        self.assertEqual(digest(CONFIRMATION / "registration.json"), pins["registrationSha256"])
        for value in (pins, registration):
            self.assertEqual(CONFIRMATION.name, value["epochId"])
            self.assertEqual("confirmatory", value["stage"])
            self.assertEqual(PILOT.name, value["sourcePilotEpochId"])
            self.assertFalse(value["collectionAuthorized"])
            self.assertEqual("unfunded", value["fundingStatus"])
            self.assertNotIn("stages", value)
            self.assertNotIn("spendAuthorization", value)
        for field in ("runsPerArm", "effectSizeDelta", "noninferiorityMargin"):
            self.assertIsNone(pins[field])
        for field in ("compiler", "arms", "modelPin", "agentVersion", "suite"):
            self.assertEqual(self.pins[field], pins[field])
        for field, original in (
            ("taskSupersession", BENCH / "registrations/ppw-redesign-task-supersession.json"),
            ("pilotInputPins", PILOT / "pins.json"),
        ):
            proof = registration[field]
            self.assertEqual(proof["sha256"], digest(CONFIRMATION / proof["path"]))
            self.assertEqual(original.read_bytes(), (CONFIRMATION / proof["path"]).read_bytes())

    def test_neither_input_tree_has_run_or_ledger_artifacts(self):
        for epoch in (PILOT, CONFIRMATION):
            for name in ("runs", "ppw-stage-ledger.json", "ppw-analysis.json", "result.json"):
                self.assertFalse(list(epoch.rglob(name)), (epoch.name, name))

    def test_pilot_analysis_refuses_unrun_after_validating_complete_inputs(self):
        with self.assertRaisesRegex(ValueError, "has not completed collection: scaffolded"):
            self.instrument.analyze(EPOCHS, PILOT.name, "pilot")
        self.assertFalse((PILOT / "ppw-stage-ledger.json").exists())

    def test_confirmation_refuses_analysis_and_stage_registration(self):
        with self.assertRaisesRegex(ValueError, "not a redesigned schemaVersion 2 epoch"):
            self.instrument.analyze(EPOCHS, CONFIRMATION.name, "confirmatory")
        with self.assertRaisesRegex(ValueError, "registration schemaVersion must be 2"):
            self.instrument.validate_registration(
                load(CONFIRMATION / "registration.json"), "confirmatory", CONFIRMATION.name)
        self.assertFalse((CONFIRMATION / "ppw-stage-ledger.json").exists())

    def test_confirmation_analysis_reads_no_pilot_or_sibling_inputs(self):
        original = self.instrument.load
        with patch.object(self.instrument, "load", wraps=original) as reader:
            with self.assertRaisesRegex(ValueError, "not a redesigned schemaVersion 2 epoch"):
                self.instrument.analyze(EPOCHS, CONFIRMATION.name, "confirmatory")
        self.assertEqual([CONFIRMATION / "pins.json", CONFIRMATION / "registration.json"],
                         [Path(call.args[0]) for call in reader.call_args_list])

    def test_wrong_stage_and_combined_epoch_ids_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "wrong-stage"):
            self.instrument.analyze(EPOCHS, PILOT.name, "confirmatory")
        for epoch_id in (PILOT.name + "," + CONFIRMATION.name,
                         [PILOT.name, CONFIRMATION.name], "../" + PILOT.name):
            with self.subTest(epoch_id=epoch_id), self.assertRaisesRegex(ValueError, "one epoch"):
                self.instrument.analyze(EPOCHS, epoch_id, "pilot")

    def test_runner_resolves_both_templates_but_never_reaches_a_product_or_agent(self):
        for epoch, stage, reason in (
            (PILOT, "pilot", "does not authorize collection"),
            (CONFIRMATION, "confirmatory", "registration schemaVersion must be 2"),
        ):
            for confirm in (False, True):
                with self.subTest(epoch=epoch.name, confirm=confirm), \
                        patch.dict(os.environ, self.environment(), clear=True), \
                        patch.object(self.instrument, "command", side_effect=AssertionError("no commands")):
                    with self.assertRaisesRegex(ValueError, reason):
                        self.instrument.run_epoch(
                            epoch / "registration.json", epoch / "tasks", "unused-product",
                            self.work / "no-output", epoch.name, stage, confirm)
        self.assertFalse((self.work / "no-output").exists())

    def test_actual_runner_cli_refuses_before_product_agent_or_output_creation(self):
        commands = self.work / "commands"
        commands.mkdir()
        tripwire = self.work / "unexpected-command"
        for name in ("git", "claude", "dotnet"):
            executable = commands / name
            executable.write_text(
                "#!/usr/bin/env python3\nimport os, pathlib, sys\n"
                "pathlib.Path(os.environ['PPW_COMMAND_TRIPWIRE']).write_text(sys.argv[0])\n"
                "raise SystemExit(97)\n")
            executable.chmod(0o755)
        env = self.environment()
        env.update(PATH=str(commands) + os.pathsep + env.get("PATH", ""),
                   PPW_COMMAND_TRIPWIRE=str(tripwire))
        for epoch, stage, reason in (
            (PILOT, "pilot", "does not authorize collection"),
            (CONFIRMATION, "confirmatory", "registration schemaVersion must be 2"),
        ):
            result = subprocess.run([
                "bash", str(BENCH / "run-ppw-epoch.sh"), "--epoch-id", epoch.name, "--stage", stage,
                "--registration", str(epoch / "registration.json"),
                "--tasks-root", str(epoch / "tasks"), "--compiler-root", "unused-product",
                "--epochs-root", str(self.work / "no-output"), "--confirm-paid-epoch",
            ], text=True, capture_output=True, env=env)
            with self.subTest(epoch=epoch.name):
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                self.assertIn(reason, result.stderr)
                self.assertFalse(tripwire.exists())
                self.assertFalse((self.work / "no-output").exists())

    def test_analysis_cli_accepts_one_unrun_input_and_rejects_multiple_ids(self):
        command = [sys.executable, str(BENCH / "ppw-analyze.py"),
                   "--epochs-root", str(EPOCHS), "--stage", "pilot",
                   "--epoch-id", PILOT.name]
        for extra, reason in (
            ([], "has not completed collection: scaffolded"),
            (["--epoch-id", CONFIRMATION.name], "must occur exactly once"),
        ):
            result = subprocess.run(command + extra, text=True, capture_output=True,
                                    env=self.environment())
            with self.subTest(extra=extra):
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                self.assertIn(reason, result.stderr)
        self.assertFalse((PILOT / "ppw-stage-ledger.json").exists())

    def test_reservation_cannot_enter_historical_analysis_even_as_a_dry_run(self):
        for dry_run in (False, True):
            with self.subTest(dry_run=dry_run), self.assertRaisesRegex(
                    SystemExit, "confirmatory reservation is unregistered"):
                self.legacy.analyze(str(CONFIRMATION), dry_run=dry_run)

    def test_historical_ledger_recomputation_does_not_treat_reservation_as_a_run(self):
        self.assertTrue(self.legacy._unrun_confirmatory_reservation(str(CONFIRMATION)))
        output = self.work / "historical-ledger.json"
        with redirect_stdout(io.StringIO()):
            result = self.legacy.main([
                "--ledger", "--epochs-root", str(EPOCHS),
                "--underpowered-from", str(EPOCHS / "w-rows-dry-002"), "--out", str(output)])
        self.assertEqual(0, result)
        self.assertEqual((BENCH / "effect-rows-benefit-ledger.json").read_bytes(), output.read_bytes())
        self.assertFalse(load(output)["epochRun"])

    def test_malformed_reservations_or_run_artifacts_are_never_silently_ignored(self):
        # These mutations live only in owned test copies, never in an epoch input.
        for case in ("lifecycle", "authorization", "boolean-schema", "registration-hash",
                     "runs-directory", "run-result", "transcript", "symlink"):
            directory = self.work / case / CONFIRMATION.name
            shutil.copytree(CONFIRMATION, directory)
            pins = load(directory / "pins.json")
            if case == "lifecycle":
                pins["lifecycle"] = "collected"
            elif case == "authorization":
                pins["collectionAuthorized"] = 0
            elif case == "boolean-schema":
                pins["schemaVersion"] = True
            elif case == "registration-hash":
                pins["registrationSha256"] = "0" * 64
            elif case == "runs-directory":
                (directory / "runs").mkdir()
            elif case == "run-result":
                (directory / "result.json").write_text('{"synthetic": true}\n')
            elif case == "transcript":
                (directory / "transcript.jsonl").write_text('{"synthetic": true}\n')
            elif case == "symlink":
                (directory / "linked-input").symlink_to(directory / "README.md")
            (directory / "pins.json").write_text(json.dumps(pins) + "\n")
            output = self.work / (case + "-must-not-exist.json")
            with self.subTest(case=case):
                self.assertFalse(self.legacy._unrun_confirmatory_reservation(str(directory)))
                with self.assertRaisesRegex(SystemExit, "confirmatory reservation is unregistered"):
                    self.legacy.main([
                        "--ledger", "--epochs-root", str(directory.parent),
                        "--underpowered-from", str(EPOCHS / "w-rows-dry-002"), "--out", str(output)])
                self.assertFalse(output.exists())

    def test_active_or_unknown_reservation_fields_fail_closed(self):
        mutations = [
            (descriptor, field) for descriptor in ("pins", "registration")
            for field in ("stages", "spendAuthorization", "stageRegistration",
                          "modelRegistration", "epochRun", "verdict", "unknownField")
        ] + [("pins", "effectSizeDelta"), ("pins", "noninferiorityMargin")]
        for index, (descriptor, field) in enumerate(mutations):
            directory = self.work / str(index) / CONFIRMATION.name
            shutil.copytree(CONFIRMATION, directory)
            path = directory / (descriptor + ".json")
            value = load(path)
            value[field] = 0.5 if field in ("effectSizeDelta", "noninferiorityMargin") else {"synthetic": True}
            path.write_text(json.dumps(value) + "\n")
            pins = load(directory / "pins.json")
            pins["registrationSha256"] = digest(directory / "registration.json")
            (directory / "pins.json").write_text(json.dumps(pins) + "\n")
            output = self.work / (str(index) + "-must-not-exist.json")
            with self.subTest(descriptor=descriptor, field=field):
                self.assertFalse(self.legacy._unrun_confirmatory_reservation(str(directory)))
                with self.assertRaisesRegex(SystemExit, "confirmatory reservation is unregistered"):
                    self.legacy.main([
                        "--ledger", "--epochs-root", str(directory.parent),
                        "--underpowered-from", str(EPOCHS / "w-rows-dry-002"), "--out", str(output)])
                self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
