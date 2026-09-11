"""Actual collector output enters the registered adjudicator; every observation remains SYNTHETIC."""
import shutil
import unittest
from unittest.mock import patch

from ppw_pilot_epoch import analysis, build
import test_ppw_gateway_collection as collection_tests


class RegisteredCollectionTests(collection_tests.CollectionTests):
    def prepare_inventory(self, tasks, runs):
        self.assertEqual((3, 74), (tasks, runs))
        self.seed = build(self.root / "registered-SYNTHETIC-seed")
        registration_helper = analysis.module("registered_collector_fixture", "ppw-gateway-registration.py")
        self.registration = registration_helper.resolve_profile(registration_helper.PROFILE)
        self.historical_manifest = True
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
        shutil.copy2(registration_helper.PROFILE,
                     self.inputs / registration_helper.PROFILE.name)
        transport = analysis.load(registration_helper.PROFILE)["transportEvidence"]
        destination = self.inputs / transport["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(registration_helper.ROOT / transport["path"], destination)
        shutil.copytree(self.seed / "tasks", self.inputs / "tasks")
        for name in ("spendAuthorization", "spendingPlan", "stageRegistration",
                     "modelRegistration", "instrumentAmendment", "sourceInspectionEvidence"):
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
                registration_helper.ROOT / "gateway-instrument-amendment.json"
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

    @unittest.skip("final #1432 registered profile awaits reviewed native wire evidence")
    def test_recovery_keeps_first_launch_invalid_and_collects_only_unstarted_slots(self):
        pass
