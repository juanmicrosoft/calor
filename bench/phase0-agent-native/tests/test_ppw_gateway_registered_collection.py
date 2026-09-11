"""Actual collector output enters the registered adjudicator; every observation remains SYNTHETIC."""
import shutil
import sqlite3
from pathlib import Path
import unittest
from unittest.mock import patch

from ppw_pilot_epoch import analysis, build
import test_ppw_gateway_collection as collection_tests


class RegisteredCollectionTests(collection_tests.CollectionTests):
    def prepare_inventory(self, tasks, runs):
        self.assertEqual((3, 74), (tasks, runs))
        self.seed = build(self.root / "registered-SYNTHETIC-seed")
        registration_helper = analysis.module("registered_collector_fixture", "ppw-gateway-registration.py")
        recovered = self._testMethodName == (
            "test_recovery_keeps_first_launch_invalid_and_collects_only_unstarted_slots")
        profile_path = registration_helper.RECOVERY_PROFILE if recovered else registration_helper.PROFILE
        self.registration = (
            registration_helper.resolve_collection_profile(profile_path) if recovered
            else registration_helper.resolve_profile(profile_path))
        self.historical_manifest = not recovered
        self.selected = self.registration["stages"]["pilot"]
        self.epoch_id = self.selected["epochId"]
        self.epoch = self.epochs / self.epoch_id
        frozen_product = analysis.load(self.seed / "pins.json")["compiler"]
        self.compiler_hash = frozen_product["compilerHash"]
        self.product = {key: value for key, value in frozen_product.items() if key != "compilerHash"}
        # The compiler/OS boundary remains a double, including its local Runtime
        # fixture. The exact registered source/product metadata goes through the
        # real collector and the real read-only consumer, without running a model.
        self.registration_file = self.inputs / "resolved-historical-registration.json"
        collection_tests.save(self.registration_file, self.registration)
        shutil.copy2(profile_path, self.inputs / profile_path.name)
        transport = analysis.load(profile_path)["transportEvidence"]
        destination = self.inputs / transport["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(registration_helper.ROOT / transport["path"], destination)
        shutil.copytree(self.seed / "tasks", self.inputs / "tasks")
        for name in ("spendAuthorization", "spendingPlan", "stageRegistration",
                     "modelRegistration", "instrumentAmendment", "sourceInspectionEvidence",
                     "recoveryEvidence", "recoveryAuthorization", "inspectionProof",
                     "failedArchiveInventory", "failedOperationalSnapshot"):
            if name not in self.selected:
                continue
            proof = self.selected[name]
            path = self.inputs / proof["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(registration_helper.ROOT / proof["path"], path)
        plan = analysis.load(self.inputs / self.selected["spendingPlan"]["path"])
        for proof in self.spending.FORECAST_EVIDENCE.values():
            destination = self.inputs / proof["path"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(registration_helper.ROOT / proof["path"], destination)
        self.admission.update(
            forecastEvidence=self.spending.FORECAST_EVIDENCE,
            sourceInspector=plan["clientControl"]["sourceInspector"],
            epochId=self.epoch_id, ceilingUnits=self.spending.units(plan["ceilingUsd"]),
            authorizationSha256=self.selected["spendAuthorization"]["sha256"],
            planSha256=self.selected["spendingPlan"]["sha256"], protocolSha256=plan["protocolSha256"],
            slots=[{"id": "%s/%s/%d" % (task, arm, run), "task": task, "arm": arm, "run": run}
                   for run in range(1, runs + 1) for task in self.registration["tasks"]
                   for arm in ("calor-permissive", "calor-strict")],
            harnessArtifacts=analysis.load(
                registration_helper.ROOT / self.selected["instrumentAmendment"]["path"]
            )["replacementHarnessArtifacts"],
        )

    def seed_run(self, task, arm, run):
        directory = self.seed / "runs" / task / arm / ("run-%d" % run)
        report = analysis.load(directory / "source-inspection.json")
        runtime = self.admission["sourceInspector"]
        report.update(inspectorSha256=runtime["files"]["ppw-source-inspector.dll"],
                      inspectorRuntimeSha256=runtime["runtimeSha256"])
        collection_tests.save(directory / "source-inspection.json", report)
        return directory

    def test_exact_source_bound_collector_output_reaches_registered_adjudicator(self):
        self.collect()
        before = analysis.inventory(self.epoch, analysis.module("readonly_collector", "ppw-instrument.py"))
        with patch("subprocess.run", side_effect=AssertionError("adjudication must remain read-only")):
            report = analysis.adjudicate(self.epochs, self.epoch_id)
        projection = report["provenance"]["collectionExecutionProjection"]
        self.assertEqual(444, len(self.observed))
        self.assertEqual(444, projection["completedSlots"])
        self.assertEqual(444, projection["requestCount"])
        self.assertEqual("request-reserving-gateway-1406", projection["id"])
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertFalse(report["empirical"])
        self.spending.validate_forecast(
            analysis.load(self.epoch / self.selected["spendingPlan"]["path"]),
            self.admission["ceilingUnits"], 444, self.epoch)
        self.assertEqual(before, report["provenance"]["epochInventory"])
        for estimate in report["estimands"].values():
            self.assertEqual({"numerator": 1, "denominator": 2}, estimate["exactEstimate"])

    def test_recovery_keeps_first_launch_invalid_and_collects_only_unstarted_slots(self):
        recovery = collection_tests.instrument.helper("ppw-gateway-recovery.py")
        evidence = analysis.load(self.inputs / self.selected["recoveryEvidence"]["path"])
        authority = analysis.load(self.inputs / self.selected["recoveryAuthorization"]["path"])
        failed = analysis.load(self.inputs / self.selected["failedOperationalSnapshot"]["path"])
        target = evidence["targetBinding"]
        full_slots = list(self.admission["slots"])
        preserved = authority["preservedAttemptedSlots"]
        failed_archive = self.root / "SYNTHETIC-original-launch"
        first = full_slots[0]
        collection_tests.save(
            failed_archive / "runs" / first["task"] / first["arm"] / "run-1/client-invocation.json",
            {"exitCode": 1})
        ledger_path = Path(self.admission["ledgerPath"])
        ledger_path.parent.mkdir()
        ledger = collection_tests.budget.RequestLedger(ledger_path)
        ledger.initialize(target, recovery.PILOT_CEILING_MICRO_USD)
        # Model the reviewed post-apply state in a temporary ledger. Atomic apply
        # itself is exercised against synthetic raw ledgers in the recovery suite.
        detail = recovery._recovery_detail(
            authority["oldBinding"], target, authority["failedLedgerSha256"],
            authority["failedArchiveInventorySha256"],
            self.selected["recoveryAuthorization"]["sha256"], preserved)
        with sqlite3.connect(ledger_path) as db:
            db.execute("DELETE FROM events")
            for event in failed["events"]:
                db.execute("INSERT INTO events(id,kind,request_id,detail,created) VALUES(?,?,?,?,?)",
                           tuple(event[name] for name in ("id", "kind", "request_id", "detail", "created")))
            db.execute("INSERT INTO events(id,kind,request_id,detail) VALUES(4,?,NULL,?)",
                       (recovery.RECOVERY_EVENT, collection_tests.budget.canonical(detail)))
        self.admission.update(
            plannedSlots=full_slots, slots=full_slots[1:],
            recovery={
                "oldBinding": authority["oldBinding"], "targetBinding": target,
                "failedLedgerSha256": authority["failedLedgerSha256"],
                "failedArchiveInventorySha256": authority["failedArchiveInventorySha256"],
                "recoveryRegistrationSha256": self.selected["recoveryAuthorization"]["sha256"],
                "preservedAttemptedSlots": preserved, "failedArchive": str(failed_archive),
                "backupName": authority["backupName"],
            })
        self.collect()
        self.assertEqual([slot["id"] for slot in full_slots[1:]], self.launched)
        self.assertEqual(443, len(self.observed))
        before = analysis.inventory(self.epoch, collection_tests.instrument)
        with patch("subprocess.run", side_effect=AssertionError("adjudication must remain read-only")):
            report = analysis.adjudicate(self.epochs, self.epoch_id)
        self.assertEqual(before, report["provenance"]["epochInventory"])
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertFalse(report["empirical"])
        projection = report["provenance"]["collectionExecutionProjection"]
        self.assertEqual(443, projection["completedSlots"])
        self.assertEqual(444, projection["accountedSlots"])
        self.assertEqual(443, projection["requestCount"])
        self.assertEqual(preserved, projection["preservedAttemptedSlots"])
