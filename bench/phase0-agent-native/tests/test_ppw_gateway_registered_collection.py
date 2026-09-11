"""Actual collector output enters the registered adjudicator; every observation remains SYNTHETIC."""
import copy
from contextlib import contextmanager
import shutil
from pathlib import Path
import unittest
from unittest.mock import patch

from ppw_pilot_epoch import analysis, build, synthetic_current_analysis_manifest
import test_ppw_gateway_collection as collection_tests


class RegisteredCollectionTests(collection_tests.CollectionTests):
    def prepare_inventory(self, tasks, runs):
        self.assertEqual((3, 74), (tasks, runs))
        self.seed = build(self.root / "registered-SYNTHETIC-seed")
        registration_helper = analysis.module("registered_collector_fixture", "ppw-gateway-registration.py")
        recovered = self._testMethodName.startswith("test_recovery_")
        self.recovered_fixture = recovered
        profile_path = registration_helper.PROFILE
        self.registration = registration_helper.resolve_profile(profile_path)
        self.historical_manifest = not recovered
        self.selected = self.registration["stages"]["pilot"]
        self.epoch_id = (
            "SYNTHETIC-terminal-pilot" if recovered else self.selected["epochId"])
        self.selected["epochId"] = self.epoch_id
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
        _, self.historical_prices = self.spending.pinned_document(
            registration_helper.ROOT, plan["clientControl"]["priceContract"], "historical prices")
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
            harnessArtifacts=(
                self.spending.artifact_manifest(self.spending.GATEWAY)
                if recovered else analysis.load(
                    registration_helper.ROOT / self.selected["instrumentAmendment"]["path"]
                )["replacementHarnessArtifacts"]
            ),
        )

    @contextmanager
    def historical_pricing(self):
        # Reproduce the preserved profile's pricing, not the prospective #1436 policy.
        with patch.object(collection_tests.budget, "price_contract", return_value=self.historical_prices):
            self.admission["priceSha256"] = collection_tests.budget.price_identity()
            yield

    def collect(self):
        with self.historical_pricing():
            return super().collect()

    def seed_run(self, task, arm, run):
        directory = self.seed / "runs" / task / arm / ("run-%d" % run)
        report = analysis.load(directory / "source-inspection.json")
        runtime = self.admission["sourceInspector"]
        report.update(inspectorSha256=runtime["files"]["ppw-source-inspector.dll"],
                      inspectorRuntimeSha256=runtime["runtimeSha256"])
        collection_tests.save(directory / "source-inspection.json", report)
        return directory

    def test_synthetic_source_bound_collector_output_reaches_registered_adjudicator(self):
        self.collect()
        before = analysis.inventory(self.epoch, analysis.module("readonly_collector", "ppw-instrument.py"))
        with self.assertRaisesRegex(ValueError, "analysis artifact changed: ppw-budget-gateway.py"):
            analysis.validate_analysis_registration()
        with patch("subprocess.run", side_effect=AssertionError("adjudication must remain read-only")):
            report = self.adjudicate(self.epochs)
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

    def prepare_recovered_scope(self):
        with self.historical_pricing():
            return self._prepare_recovered_scope()

    def _prepare_recovered_scope(self):
        if not self.recovered_fixture:
            return super().prepare_recovered_scope()
        self.prepare_synthetic_terminal_documents()
        full_slots = super().prepare_recovered_scope()
        recovery_evidence = {
            "kind": "SYNTHETIC-test-owned-recovery-evidence",
            "targetBinding": self.admission["recovery"]["targetBinding"],
            **{
                name: self.selected[name]
                for name in (
                    "recoveryAuthorization", "inspectionProof",
                    "failedArchiveInventory", "failedOperationalSnapshot",
                )
            },
            "executionProfile": self.selected["executionProfile"],
            "spendingPlan": self.selected["spendingPlan"],
        }
        path = self.inputs / "SYNTHETIC-recoveryEvidence.json"
        collection_tests.save(path, recovery_evidence)
        self.selected["recoveryEvidence"] = {
            "path": path.name, "sha256": collection_tests.instrument.digest(path),
        }
        collection_tests.save(self.registration_file, self.registration)
        return full_slots

    def prepare_synthetic_terminal_documents(self):
        harness = self.spending.artifact_manifest(self.spending.GATEWAY)
        amendment = analysis.load(self.inputs / self.selected["instrumentAmendment"]["path"])
        amendment = copy.deepcopy(amendment)
        amendment["fixtureKind"] = "SYNTHETIC-current-terminal-amendment"
        amendment["replacementHarnessArtifacts"] = harness
        amendment_path = self.inputs / "SYNTHETIC-terminal-amendment.json"
        collection_tests.save(amendment_path, amendment)
        self.selected["instrumentAmendment"] = {
            "path": amendment_path.name,
            "sha256": collection_tests.instrument.digest(amendment_path),
        }

        authorization = analysis.load(self.inputs / self.selected["spendAuthorization"]["path"])
        authorization = copy.deepcopy(authorization)
        authorization.update(
            fixtureKind="SYNTHETIC-current-terminal-authorization",
            epochId=self.epoch_id,
        )
        authorization_path = self.inputs / "SYNTHETIC-terminal-authorization.json"
        collection_tests.save(authorization_path, authorization)
        self.selected["spendAuthorization"] = {
            "path": authorization_path.name,
            "sha256": collection_tests.instrument.digest(authorization_path),
        }

        plan = analysis.load(self.inputs / self.selected["spendingPlan"]["path"])
        plan = copy.deepcopy(plan)
        plan.update(
            fixtureKind="SYNTHETIC-current-terminal-plan",
            epochId=self.epoch_id,
            authorizationSha256=self.selected["spendAuthorization"]["sha256"],
        )
        plan["protocolSha256"] = self.spending.protocol_identity(
            self.registration, self.selected, "pilot", self.epoch_id)
        plan_path = self.inputs / "SYNTHETIC-terminal-plan.json"
        collection_tests.save(plan_path, plan)
        self.selected["spendingPlan"] = {
            "path": plan_path.name,
            "sha256": collection_tests.instrument.digest(plan_path),
        }
        self.admission.update(
            authorizationSha256=self.selected["spendAuthorization"]["sha256"],
            planSha256=self.selected["spendingPlan"]["sha256"],
            protocolSha256=plan["protocolSha256"],
            harnessArtifacts=harness,
        )

        profile = {
            "schemaVersion": 1,
            "kind": "pp-w-request-gateway-execution-profile",
            "id": "SYNTHETIC-current-terminal-profile",
            "fixtureKind": "SYNTHETIC-current-terminal-profile",
            "stage": "pilot",
            "epochId": self.epoch_id,
            "allowedDataKinds": ["synthetic"],
            **{
                name: self.selected[name]
                for name in (
                    "spendAuthorization", "spendingPlan", "instrumentAmendment",
                    "stageRegistration", "modelRegistration", "sourceInspectionEvidence",
                )
            },
        }
        self.synthetic_profile = self.inputs / "SYNTHETIC-terminal-profile.json"
        collection_tests.save(self.synthetic_profile, profile)
        self.selected["executionProfile"] = {
            "path": self.synthetic_profile.name,
            "sha256": collection_tests.instrument.digest(self.synthetic_profile),
        }
        collection_tests.save(self.registration_file, self.registration)

    def adjudicate(self, epochs):
        manifest = synthetic_current_analysis_manifest(analysis, self.root)
        if not self.recovered_fixture:
            with patch.object(analysis, "MANIFEST", manifest):
                return analysis.adjudicate(epochs, self.epoch_id)
        archive_gateway = analysis.module(
            "synthetic_recovery_archive_profile", "ppw-gateway-registration.py")
        archive_gateway.RECOVERY_PROFILE = self.synthetic_profile
        original_module = analysis.module
        with patch.object(analysis, "MANIFEST", manifest), \
                patch.object(analysis, "module", side_effect=lambda name, relative:
                             archive_gateway if name == "archived_gateway_profile"
                             else original_module(name, relative)):
            return analysis.adjudicate(epochs, self.epoch_id)

    def test_recovery_keeps_first_launch_invalid_and_collects_only_unstarted_slots(self):
        full_slots = self.prepare_recovered_scope()
        preserved = full_slots[:1]
        self.collect()
        self.assertEqual(full_slots[1:], self.launched)
        self.assertEqual(443, len(self.observed))
        before = analysis.inventory(self.epoch, collection_tests.instrument)
        with patch("subprocess.run", side_effect=AssertionError("adjudication must remain read-only")):
            report = self.adjudicate(self.epochs)
        self.assertEqual(before, report["provenance"]["epochInventory"])
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertFalse(report["empirical"])
        projection = report["provenance"]["collectionExecutionProjection"]
        self.assertEqual(443, projection["completedSlots"])
        self.assertEqual(444, projection["accountedSlots"])
        self.assertEqual(443, projection["requestCount"])
        self.assertEqual(preserved, projection["preservedAttemptedSlots"])

    def test_recovery_mixes_zero_and_reconciled_terminal_invalids_without_replacement(self):
        super().test_recovery_mixes_zero_and_reconciled_terminal_invalids_without_replacement()
        before = analysis.inventory(self.epoch, collection_tests.instrument)
        with patch("subprocess.run", side_effect=AssertionError("adjudication must remain read-only")):
            report = self.adjudicate(self.epochs)
        projection = report["provenance"]["collectionExecutionProjection"]
        self.assertEqual(before, report["provenance"]["epochInventory"])
        self.assertEqual((370, 74, 444), (
            projection["completedSlots"], projection["invalidTerminalSlots"],
            projection["accountedSlots"]))
        self.assertEqual(406, projection["requestBearingSlots"])
        self.assertEqual(406, projection["requestCount"])
        self.assertIsNone(report["estimands"]["shapeRealizationRate"]["exactEstimate"])
        self.assertIsNone(report["estimands"]["armAEscapeRate"]["exactEstimate"])
        self.assertEqual("UNIDENTIFIED", report["decision"]["evaluatedStatus"])
        moved_root = self.root / "relocated-SYNTHETIC-epochs"
        moved_root.mkdir()
        self.epoch.rename(moved_root / self.epoch_id)
        with patch("subprocess.run", side_effect=AssertionError("archive analysis is read-only")):
            moved = self.adjudicate(moved_root)
        self.assertEqual(report["estimands"], moved["estimands"])
        self.assertEqual(report["provenance"]["epochInventory"],
                         moved["provenance"]["epochInventory"])
