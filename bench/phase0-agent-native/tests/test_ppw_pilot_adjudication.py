"""Registered math and read-only plumbing, using explicitly SYNTHETIC fixtures."""
import copy
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid

from ppw_pilot_epoch import BENCH, analysis, build


class PilotAdjudicationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = BENCH / "tests" / (".pilot-analysis-" + uuid.uuid4().hex)
        cls.root.mkdir()
        cls.addClassCleanup(shutil.rmtree, cls.root)
        cls.epochs = cls.root / "epochs"
        cls.epoch = build(cls.epochs)
        cls.pins = analysis.load(cls.epoch / "pins.json")
        cls.registration = analysis.load(cls.epoch / "registration.json")
        cls.method = analysis.load(BENCH / analysis.METHOD)
        cls.instrument = analysis.module("pilot_test_instrument", "ppw-instrument.py")
        cls.baseline = cls.instrument.analyze(cls.epochs, cls.epoch.name, "pilot")

    def report(self):
        return copy.deepcopy(self.baseline)

    def aggregate(self, report=None):
        return analysis.aggregate(report or self.report(), self.pins, self.method)

    def change_json(self, path, change):
        previous = path.read_bytes()
        self.addCleanup(path.write_bytes, previous)
        value = json.loads(previous)
        change(value)
        path.write_text(json.dumps(value) + "\n")

    def raw_result(self):
        return (self.epoch / "runs" / self.pins["suite"][0]
                / "calor-permissive/run-1/result.json")

    def guarded_projection(self):
        amendment = analysis.load(BENCH / analysis.SPENDING_AMENDMENT)
        self.change_json(self.epoch / "registration.json", lambda registration:
                         registration["stages"]["pilot"].update(instrumentAmendment={
                             "path": "admission/spending-instrument-amendment.json",
                             "sha256": analysis.digest(BENCH / analysis.SPENDING_AMENDMENT)}))
        self.change_json(self.epoch / "pins.json", lambda pins: pins.update(
            harnessArtifacts=amendment["replacementHarnessArtifacts"],
            harnessCommit="80e2d77579750e48d58088d4474ba5d3c35014c6",
            registrationSha256=analysis.digest(self.epoch / "registration.json")))

    @staticmethod
    def eligible(cell, count):
        cell.update(validRuns=count, invalidRuns=74 - count, censoredRuns=74 - count,
                    invalidReasons=[{"run": run, "reason": "SYNTHETIC invalid attempt"}
                                    for run in range(count + 1, 75)],
                    didNotBuildAtDeclaredDone=0, shapeRealized=0, escapes=0,
                    outputTokens=[1] * count, optionsHashes=["synthetic"] * count)

    def test_full_synthetic_archive_uses_registered_two_estimands_and_no_commands(self):
        before = analysis.inventory(self.epoch, self.instrument)
        with patch("subprocess.run", side_effect=AssertionError("analysis cannot invoke commands")):
            report = analysis.adjudicate(self.epochs, self.epoch.name)
        self.assertEqual(before, analysis.inventory(self.epoch, self.instrument))
        self.assertEqual({"shapeRealizationRate", "armAEscapeRate"}, set(report["estimands"]))
        for result in report["estimands"].values():
            self.assertEqual({"numerator": 1, "denominator": 2}, result["exactEstimate"])
        self.assertAlmostEqual(math.sqrt(math.log(80) / 888),
                               report["estimands"]["shapeRealizationRate"]["halfWidth"])
        self.assertAlmostEqual(math.sqrt(math.log(80) / 444),
                               report["estimands"]["armAEscapeRate"]["halfWidth"])
        self.assertEqual("synthetic", report["dataKind"])
        self.assertFalse(report["empirical"])
        self.assertFalse(report["decision"]["appliedToEmpiricalData"])
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertEqual("NO_REGISTERED_POINT_STOP", report["decision"]["evaluatedStatus"])
        self.assertFalse(report["decision"]["stage2Authorized"])
        self.assertFalse(report["collectionAuthorized"])
        self.assertFalse(report["pilotPoolingAllowed"])
        self.assertIsNone(report["stage2N"])
        self.assertIsNone(report["stage2Delta"])
        self.assertIsNone(report["decision"]["confirmatoryVerdict"])
        self.assertEqual(before, report["provenance"]["epochInventory"])
        self.assertFalse(report["provenance"]["savedDescriptiveLedgerUsedAsAuthority"])
        self.assertEqual(analysis.digest(analysis.MANIFEST),
                         report["provenance"]["analysisRegistrationSha256"])

    def test_exact_half_does_not_trigger_shape_stop(self):
        report = self.aggregate()
        self.assertIs(report["stoppingRules"]["shapeRealizationBelowHalf"], False)
        self.assertEqual({"numerator": 1, "denominator": 2},
                         report["estimands"]["shapeRealizationRate"]["exactEstimate"])

    def test_below_half_that_rounds_to_half_still_stops(self):
        report = self.report()
        first, second = report["perCell"][:2]
        first["shapeRealized"] = 1
        self.eligible(second, 73)
        second["shapeRealized"] = 72
        result = self.aggregate(report)
        estimate = result["estimands"]["shapeRealizationRate"]["estimate"]
        self.assertEqual(0.5, round(estimate, 4))
        self.assertLess(estimate, 0.5)
        self.assertTrue(result["stoppingRules"]["shapeRealizationBelowHalf"])
        self.assertIn("STOP_STAGE2_PROTOCOL_DEFECT_R6", result["decision"]["prescribedConsequences"])

    def test_zero_escape_stops_despite_positive_upper_bound(self):
        report = self.report()
        for cell in report["perCell"]:
            if cell["arm"] == "A":
                cell["escapes"] = 0
        result = self.aggregate(report)
        self.assertTrue(result["stoppingRules"]["armAEscapeExactlyZero"])
        self.assertGreater(result["estimands"]["armAEscapeRate"]["upper"], 0)
        self.assertEqual("STOP_STAGE2", result["decision"]["evaluatedStatus"])
        self.assertEqual("SYNTHETIC_ONLY", result["decision"]["status"])
        self.assertIn("STOP_STAGE2_REDESIGN_UNSUCCESSFUL_R1_R2",
                      result["decision"]["prescribedConsequences"])

    def test_both_stops_are_reported_without_inventing_precedence(self):
        report = self.report()
        for cell in report["perCell"]:
            cell.update(shapeRealized=0, escapes=0)
        result = self.aggregate(report)
        self.assertEqual([True, True], list(result["stoppingRules"].values()))
        self.assertEqual(2, len(result["decision"]["prescribedConsequences"]))

    def test_fixed_weights_survive_unequal_attrition_without_replacement(self):
        report = self.report()
        for cell in report["perCell"]:
            cell.update(shapeRealized=0, escapes=0)
        cell = report["perCell"][0]
        self.eligible(cell, 1)
        cell.update(shapeRealized=1, escapes=1)
        result = self.aggregate(report)
        self.assertEqual({"numerator": 1, "denominator": 6},
                         result["estimands"]["shapeRealizationRate"]["exactEstimate"])
        self.assertEqual({"numerator": 1, "denominator": 3},
                         result["estimands"]["armAEscapeRate"]["exactEstimate"])
        self.assertGreater(result["estimands"]["armAEscapeRate"]["halfWidth"], 0.1)
        self.assertEqual(73, result["accounting"][0]["invalid"])
        self.assertEqual(74, result["accounting"][0]["scheduled"])

    def test_nonbuilds_and_changed_contracts_stay_in_escape_denominator(self):
        report = self.report()
        for cell in report["perCell"]:
            if cell["arm"] == "A":
                cell.update(escapes=1, didNotBuildAtDeclaredDone=73, shapeRealized=0)
        result = self.aggregate(report)
        self.assertEqual({"numerator": 1, "denominator": 74},
                         result["estimands"]["armAEscapeRate"]["exactEstimate"])
        for cell in report["perCell"]:
            if cell["arm"] == "A":
                cell.update(escapes=0, didNotBuildAtDeclaredDone=0,
                            changedPublicApiRuns=list(range(1, 75)))
        result = self.aggregate(report)
        self.assertTrue(result["stoppingRules"]["armAEscapeExactlyZero"])
        self.assertEqual(74, result["accounting"][0]["eligible"])
        self.assertEqual(74, result["accounting"][0]["changedPublicApi"])

    def test_overlapping_unknowns_are_unioned_not_zeroed_or_double_counted(self):
        report = self.report()
        cell = report["perCell"][0]
        cell.update(unscorableShapeRuns=[1], unscorableHeldoutRuns=[1],
                    unscorablePublicApiRuns=[1])
        result = self.aggregate(report)
        self.assertEqual(1, result["accounting"][0]["unscorableEscape"])
        for estimate in result["estimands"].values():
            self.assertFalse(estimate["identified"])
            self.assertIsNone(estimate["exactEstimate"])
            self.assertEqual((0, 1), (estimate["lower"], estimate["upper"]))
        self.assertEqual([None, None], list(result["stoppingRules"].values()))
        self.assertEqual("UNIDENTIFIED", result["decision"]["evaluatedStatus"])
        self.assertFalse(result["decision"]["stage2Authorized"])

    def test_b_arm_unknown_escape_does_not_change_a_estimand(self):
        report = self.report()
        report["perCell"][1]["unscorableHeldoutRuns"] = [74]
        result = self.aggregate(report)
        self.assertTrue(result["estimands"]["armAEscapeRate"]["identified"])
        self.assertTrue(result["estimands"]["shapeRealizationRate"]["identified"])

    def test_zero_eligible_cell_is_unidentified_not_imputed(self):
        report = self.report()
        self.eligible(report["perCell"][0], 0)
        result = self.aggregate(report)
        self.assertIsNone(result["estimands"]["shapeRealizationRate"]["estimate"])
        self.assertIsNone(result["estimands"]["armAEscapeRate"]["estimate"])
        self.assertFalse(result["decision"]["stage2Authorized"])

    def test_known_stop_is_preserved_when_the_other_estimand_is_unidentified(self):
        report = self.report()
        for cell in report["perCell"]:
            cell["escapes"] = 0
        report["perCell"][1]["unscorableShapeRuns"] = [74]
        result = self.aggregate(report)
        self.assertIsNone(result["stoppingRules"]["shapeRealizationBelowHalf"])
        self.assertTrue(result["stoppingRules"]["armAEscapeExactlyZero"])
        self.assertEqual("STOP_STAGE2", result["decision"]["evaluatedStatus"])
        self.assertFalse(result["decision"]["stage2Authorized"])

    def test_rounded_rates_and_numeric_failures_are_not_escape_evidence(self):
        report = self.report()
        for cell in report["perCell"]:
            cell.update(escapeRate=1.0, shapeRealizedRate=0.0,
                        namedTestFailuresWithoutEffect=[74], escapes=0)
        result = self.aggregate(report)
        self.assertEqual(0.5, result["estimands"]["shapeRealizationRate"]["estimate"])
        self.assertEqual(0, result["estimands"]["armAEscapeRate"]["estimate"])

    def test_missing_duplicate_foreign_and_cross_stage_cells_are_rejected(self):
        changes = [
            lambda r: r["perCell"].pop(),
            lambda r: r["perCell"].append(copy.deepcopy(r["perCell"][0])),
            lambda r: r["perCell"].__setitem__(1, copy.deepcopy(r["perCell"][0])),
            lambda r: r["perCell"][0].update(pair="foreign-task"),
            lambda r: r["perCell"][0].update(stage="confirmatory"),
            lambda r: r["perCell"][0].update(epoch="different-epoch"),
            lambda r: r.update(stage="confirmatory"),
            lambda r: r.update(epoch="different-epoch"),
            lambda r: r.update(empirical=True),
            lambda r: r.update(registrationSha256="0" * 64),
        ]
        for index, change in enumerate(changes):
            report = self.report()
            change(report)
            with self.subTest(index=index), self.assertRaises(ValueError):
                self.aggregate(report)

    def test_invalid_counts_unknown_ids_and_lost_attempts_are_rejected(self):
        for change in (
            {"plannedRuns": 73}, {"validRuns": 73}, {"invalidRuns": True},
            {"shapeRealized": 75}, {"escapes": -1},
            {"unscorableShapeRuns": [1, 1]}, {"unscorableHeldoutRuns": [75]},
            {"unscorablePublicApiRuns": [True]},
            {"changedPublicApiRuns": [1], "unscorableShapeRuns": [1]},
        ):
            report = self.report()
            report["perCell"][0].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.aggregate(report)

    def test_scope_rejects_repinning_foreign_method_tasks_model_and_code(self):
        for target, field, value in (
            ("pins", "runsPerArm", 73), ("pins", "modelPin", "foreign"),
            ("pins", "harnessArtifacts", {}), ("pins", "harnessCommit", "missing"),
            ("registration", "sourceInspections", {}),
        ):
            pins, registration = copy.deepcopy(self.pins), copy.deepcopy(self.registration)
            (pins if target == "pins" else registration)[field] = value
            with self.subTest(target=target, field=field), self.assertRaises(ValueError):
                analysis.validate_scope(pins, registration, self.method, self.epoch.name, "pilot")
        registration = copy.deepcopy(self.registration)
        registration["stages"]["pilot"]["stageRegistration"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "registered method"):
            analysis.validate_scope(self.pins, registration, self.method, self.epoch.name, "pilot")

    def test_raw_wrong_stage_or_epoch_and_missing_run_refuse_before_aggregation(self):
        path = self.raw_result()
        for field, value in (("stage", "confirmatory"), ("epochId", "foreign-epoch")):
            previous = path.read_bytes()
            record = json.loads(previous)
            record[field] = value
            path.write_text(json.dumps(record))
            try:
                with self.subTest(field=field), self.assertRaisesRegex(ValueError, "cross-epoch"):
                    analysis.adjudicate(self.epochs, self.epoch.name)
            finally:
                path.write_bytes(previous)
        moved = path.with_suffix(".preserved")
        path.rename(moved)
        try:
            with self.assertRaisesRegex(ValueError, "run inventory"):
                analysis.adjudicate(self.epochs, self.epoch.name)
        finally:
            moved.rename(path)

    def test_empirical_header_cannot_promote_explicit_synthetic_fixtures(self):
        self.change_json(self.epoch / "pins.json", lambda pins: pins.update(dataKind="empirical"))
        with self.assertRaisesRegex(ValueError, "declared synthetic fixture"):
            analysis.adjudicate(self.epochs, self.epoch.name)

    def test_saved_ledger_is_not_trusted_and_cli_output_never_changes_epoch(self):
        ledger = self.epoch / "ppw-stage-ledger.json"
        ledger.write_text('{"synthetic": true, "perCell": [], "verdict": "FAKE"}\n')
        self.addCleanup(ledger.unlink)
        before = analysis.inventory(self.epoch, self.instrument)
        output = self.root / "synthetic-adjudication.json"
        self.addCleanup(lambda: output.unlink(missing_ok=True))
        command = [sys.executable, str(BENCH / "ppw-pilot-adjudicate.py"),
                   "--epoch-id", self.epoch.name, "--stage", "pilot",
                   "--epochs-root", str(self.epochs), "--out", str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(output.read_text())
        self.assertEqual(0.5, report["estimands"]["shapeRealizationRate"]["estimate"])
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertEqual(before, analysis.inventory(self.epoch, self.instrument))
        again = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(2, again.returncode)
        self.assertIn("never overwrite", again.stderr)

    def test_cli_rejects_confirmation_multiple_ids_unrun_inputs_and_epoch_writes(self):
        command = [sys.executable, str(BENCH / "ppw-pilot-adjudicate.py")]
        for arguments, reason in (
            (["--epoch-id", self.epoch.name, "--stage", "confirmatory"], "invalid choice"),
            (["--epoch-id", self.epoch.name, "--stage", "pilot", "--epoch-id", "other"], "exactly once"),
            (["--epoch-id", "w-rows-pilot-001", "--stage", "pilot"], "has not completed collection"),
            (["--epoch-id", "w-rows-001", "--stage", "pilot"], "schemaVersion 2"),
            (["--epoch-id", self.epoch.name, "--stage", "pilot",
              "--epochs-root", str(self.epochs), "--out", str(self.epoch / "forbidden.json")],
             "outside immutable epoch roots"),
            (["--epoch-id", self.epoch.name + ",other", "--stage", "pilot"], "one epoch"),
        ):
            result = subprocess.run(command + arguments, capture_output=True, text=True)
            with self.subTest(arguments=arguments):
                self.assertEqual(2, result.returncode, result.stderr)
                self.assertIn(reason, result.stderr)
        self.assertFalse((self.epoch / "forbidden.json").exists())

    def test_epoch_changes_during_analysis_fail_closed(self):
        original = analysis.inventory
        calls = 0

        def changed(epoch, instrument):
            nonlocal calls
            calls += 1
            result = original(epoch, instrument)
            if calls > 1:
                result["sha256"] = "0" * 64
            return result

        with patch.object(analysis, "inventory", side_effect=changed):
            with self.assertRaisesRegex(ValueError, "epoch changed"):
                analysis.adjudicate(self.epochs, self.epoch.name)

    def test_analysis_manifest_rejects_unreviewed_code_drift(self):
        original = analysis.digest

        def changed(path):
            return "0" * 64 if Path(path).name == "ppw-instrument.py" else original(path)

        with patch.object(analysis, "digest", side_effect=changed):
            with self.assertRaisesRegex(ValueError, "analysis artifact changed"):
                analysis.validate_analysis_registration()

    def test_guarded_synthetic_projection_preserves_math_and_never_invokes_commands(self):
        before = analysis.adjudicate(self.epochs, self.epoch.name)
        self.guarded_projection()
        inventory = analysis.inventory(self.epoch, self.instrument)
        with patch("subprocess.run", side_effect=AssertionError("analysis cannot invoke commands")):
            report = analysis.adjudicate(self.epochs, self.epoch.name)
        for field in ("estimands", "stoppingRules", "decision", "accounting"):
            self.assertEqual(before[field], report[field], field)
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertFalse(report["empirical"])
        self.assertFalse(report["collectionAuthorized"])
        self.assertEqual(15, len(report["provenance"]["collectionHarnessArtifacts"]))
        self.assertEqual("guarded-fifteen-artifact-unactivated",
                         report["provenance"]["collectionExecutionProjection"]["id"])
        self.assertEqual(inventory, analysis.inventory(self.epoch, self.instrument))

    def test_guarded_projection_refuses_unactivated_empirical_before_raw_analysis(self):
        self.guarded_projection()
        self.change_json(self.epoch / "pins.json", lambda pins: pins.update(dataKind="empirical"))
        with self.assertRaisesRegex(ValueError, "guarded empirical analysis is not activated"):
            analysis.adjudicate(self.epochs, self.epoch.name)

    def test_guarded_projection_rejects_missing_altered_and_malformed_amendment(self):
        self.guarded_projection()
        pins = analysis.load(self.epoch / "pins.json")
        original = analysis.load(self.epoch / "registration.json")["stages"]["pilot"]
        changes = [
            lambda selected: selected.pop("instrumentAmendment"),
            lambda selected: selected["instrumentAmendment"].update(sha256="0" * 64),
            lambda selected: selected["instrumentAmendment"].update(path="../foreign.json"),
            lambda selected: selected["instrumentAmendment"].update(path="/foreign.json"),
            lambda selected: selected.update(instrumentAmendment=[]),
            lambda selected: selected.update(instrumentAmendment={}),
            lambda selected: selected["instrumentAmendment"].update(unbound=True),
        ]
        for index, change in enumerate(changes):
            selected = copy.deepcopy(original)
            change(selected)
            with self.subTest(index=index), self.assertRaises(ValueError):
                analysis.execution_projection(pins, selected)
        selected = {"spendingPlan": {"path": "not-authorization.json", "sha256": "0" * 64}}
        with self.assertRaisesRegex(ValueError, "pinned instrument amendment"):
            analysis.execution_projection(pins, selected)

    def test_guarded_projection_rejects_downgraded_partial_and_mixed_inventories(self):
        self.guarded_projection()
        pins = analysis.load(self.epoch / "pins.json")
        selected = analysis.load(self.epoch / "registration.json")["stages"]["pilot"]
        variants = [
            self.pins["harnessArtifacts"],
            {key: value for key, value in pins["harnessArtifacts"].items()
             if key != "ppw-spending.py"},
            {**pins["harnessArtifacts"], "ppw-instrument.py": "0" * 64},
            {**pins["harnessArtifacts"], "unregistered.py": "0" * 64},
        ]
        for index, artifacts in enumerate(variants):
            with self.subTest(index=index), self.assertRaisesRegex(ValueError, "inventory differs"):
                analysis.execution_projection({**pins, "harnessArtifacts": artifacts}, selected)

    def test_guarded_projection_rejects_wrong_stage_and_missing_raw_run(self):
        self.guarded_projection()
        pins = analysis.load(self.epoch / "pins.json")
        registration = analysis.load(self.epoch / "registration.json")
        with self.assertRaisesRegex(ValueError, "confirmatory"):
            analysis.validate_scope(pins, registration, self.method, self.epoch.name, "confirmatory")
        path = self.raw_result()
        moved = path.with_suffix(".preserved")
        path.rename(moved)
        try:
            with self.assertRaisesRegex(ValueError, "run inventory"):
                analysis.adjudicate(self.epochs, self.epoch.name)
        finally:
            moved.rename(path)

    def test_projection_and_historical_manifest_drift_fail_before_loading_analysis(self):
        original = analysis.digest
        for filename in (analysis.GUARDED_PROJECTION, analysis.SPENDING_AMENDMENT,
                         analysis.PRE_PROJECTION_MANIFEST):
            with self.subTest(filename=filename), patch.object(
                    analysis, "digest", side_effect=lambda path:
                    "0" * 64 if Path(path) == BENCH / filename else original(path)):
                with self.assertRaisesRegex(ValueError, "analysis artifact changed"):
                    analysis.validate_analysis_registration()

    def test_projection_semantics_cannot_promote_scope_or_authority(self):
        original = analysis.load
        changes = [
            {"stage": "confirmatory"}, {"collectionAuthorized": True},
            {"allowedDataKinds": ["empirical"]}, {"empiricalAdmissionRegistered": True},
            {"kind": "ordinary-epoch-pins"},
            {"baselinePins": {"path": "unregistered.json", "sha256": "0" * 64}},
            {"instrumentAmendment": {"path": analysis.SPENDING_AMENDMENT, "sha256": "0" * 64}},
        ]
        for change in changes:
            def changed(path):
                result = original(path)
                if Path(path) == BENCH / analysis.GUARDED_PROJECTION:
                    result.update(change)
                return result

            with self.subTest(change=change), patch.object(analysis, "load", side_effect=changed):
                with self.assertRaises(ValueError):
                    analysis.validate_analysis_registration()


if __name__ == "__main__":
    unittest.main()
