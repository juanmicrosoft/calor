"""Real source/profile/ledger/adjudicator handoff using only declared SYNTHETIC observations."""
import copy
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

from ppw_pilot_epoch import BENCH, analysis, build, save
from test_ppw_gateway import body, budget, usage


class GatewayArchiveTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = BENCH / "tests" / (".gateway-archive-SYNTHETIC-" + uuid.uuid4().hex)
        cls.root.mkdir()
        cls.addClassCleanup(shutil.rmtree, cls.root)
        cls.gateway = analysis.module("gateway_archive_test", "ppw-gateway-registration.py")
        cls.spending = analysis.module("gateway_archive_spending", "ppw-spending.py")
        cls.isolation = analysis.module("gateway_archive_isolation", "ppw-gateway-client.py")
        cls.epochs = cls.root / "epochs"
        seed = build(cls.epochs)
        cls.registration = cls.gateway.resolve_profile(cls.gateway.PROFILE)
        cls.selected = cls.registration["stages"]["pilot"]
        cls.epoch = seed.with_name(cls.selected["epochId"])
        seed.rename(cls.epoch)
        cls.pins = analysis.load(cls.epoch / "pins.json")
        save(cls.epoch / "registration.json", cls.registration)
        cls.pins.update(epochId=cls.epoch.name, harnessArtifacts=cls.spending.artifact_manifest(cls.spending.GATEWAY),
                        harnessCommit="f" * 40, registrationSha256=analysis.digest(cls.epoch / "registration.json"))
        save(cls.epoch / "pins.json", cls.pins)
        for name in ("spendAuthorization", "spendingPlan", "stageRegistration", "modelRegistration",
                     "instrumentAmendment", "executionProfile"):
            proof = cls.selected[name]
            destination = cls.epoch / proof["path"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(cls.gateway.ROOT / proof["path"], destination)
        cls.plan = analysis.load(cls.epoch / cls.selected["spendingPlan"]["path"])
        slots = ["%s/%s/%d" % (task, arm, run)
                 for run in range(1, 75) for task in cls.pins["suite"]
                 for arm in ("calor-permissive", "calor-strict")]
        ledger = budget.RequestLedger(cls.root / "SYNTHETIC-accounting.sqlite3")
        ledger.initialize({
            "stage": "pilot", "epochId": cls.epoch.name, "priceSha256": budget.price_identity(),
            "authorizationSha256": cls.selected["spendAuthorization"]["sha256"],
            "protocolSha256": cls.plan["protocolSha256"],
            "planSha256": cls.selected["spendingPlan"]["sha256"],
            "harnessArtifacts": cls.pins["harnessArtifacts"], "plannedSlots": slots,
        }, cls.spending.units(cls.plan["ceilingUsd"]))
        owner = ledger.start()
        save(cls.epoch / "spending-initial.json", ledger.snapshot())
        request = budget.admit_request(body())
        proof = {
            "kind": cls.isolation.ISOLATION, "kernelProbe": dict(cls.isolation.PROBE_EXPECTATIONS),
            "modelInvoked": False, "clientSha256": cls.isolation.CLIENT_SHA256,
            "policySha256": "e" * 64,
        }
        for slot in slots:
            task, arm, run = slot.split("/")
            directory = cls.epoch / "runs" / task / arm / ("run-" + run)
            record = analysis.load(directory / "result.json")
            record["epochId"] = cls.epoch.name
            save(directory / "result.json", record)
            identity = ledger.reserve(owner, slot, request)
            ledger.settle(owner, identity, *budget.reconciled_cost(request, budget.MODEL, usage(), "end_turn"))
            ledger.complete_slot(owner, slot, proof, 0)
            save(directory / "gateway-isolation.json", proof)
            save(directory / "client-invocation.json", {"exitCode": 0, "synthetic": True})
        ledger.complete(owner)
        save(cls.epoch / "spending-final.json", ledger.snapshot())

    def mutate(self, relative, change):
        path = self.epoch / relative
        original = path.read_bytes()
        self.addCleanup(path.write_bytes, original)
        value = json.loads(original)
        change(value)
        save(path, value)

    def validate(self, pins=None):
        return self.gateway.validate_archive(self.epoch, pins or self.pins, self.selected)

    def test_complete_actual_artifact_map_and_accounting_reach_registered_analyzer_readonly(self):
        with patch("subprocess.run", side_effect=AssertionError("analysis must not execute commands")):
            projection = self.validate()
            result = analysis.adjudicate(self.epochs, self.epoch.name)
        self.assertEqual(444, projection["requestCount"])
        self.assertEqual(444, projection["completedSlots"])
        self.assertEqual(1_000_000_000, projection["ceilingMicroUsd"])
        self.assertEqual(24, len(self.pins["harnessArtifacts"]))
        self.assertEqual("request-reserving-gateway-1406", projection["id"])
        self.assertEqual(projection, result["provenance"]["collectionExecutionProjection"])
        self.assertEqual("SYNTHETIC_ONLY", result["decision"]["status"])
        self.assertFalse(result["empirical"])
        self.assertFalse(result["decision"]["stage2Authorized"])
        for estimate in result["estimands"].values():
            self.assertEqual({"numerator": 1, "denominator": 2}, estimate["exactEstimate"])

    def test_incomplete_collection_never_selects_gateway_adjudication(self):
        self.mutate("spending-final.json", lambda value: value.update(state="INCOMPLETE_BUDGET"))
        with self.assertRaisesRegex(ValueError, "incomplete accounting"):
            self.validate()

    def test_wrong_stage_or_epoch_and_mixed_source_maps_are_refused(self):
        for change in ({"stage": "confirmatory"}, {"epochId": "another"},
                       {"harnessArtifacts": {}}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.validate(dict(self.pins, **change))

    def test_rewritten_charge_does_not_reconcile(self):
        self.mutate("spending-final.json", lambda value: value["requests"][0].update(charge=0))
        with self.assertRaisesRegex(ValueError, "incorrectly reconciled"):
            self.validate()

    def test_missing_usage_and_duplicate_request_fail_closed(self):
        for change in (lambda value: value["requests"][0].update(usage="null"),
                       lambda value: value["requests"].append(copy.deepcopy(value["requests"][0]))):
            path = self.epoch / "spending-final.json"
            original = path.read_bytes()
            try:
                value = json.loads(original)
                change(value)
                save(path, value)
                with self.assertRaises((ValueError, TypeError)):
                    self.validate()
            finally:
                path.write_bytes(original)

    def test_pre_reservation_and_slot_order_are_not_optional(self):
        def swap(value):
            events = value["events"]
            events[2]["kind"], events[3]["kind"] = events[3]["kind"], events[2]["kind"]
        self.mutate("spending-final.json", swap)
        with self.assertRaisesRegex(ValueError, "preceding reservation"):
            self.validate()

    def test_child_visible_isolation_copy_is_not_authority(self):
        task = self.pins["suite"][0]
        relative = "runs/%s/calor-permissive/run-1/gateway-isolation.json" % task
        self.mutate(relative, lambda value: value["kernelProbe"].update(samePortIpv6=True))
        with self.assertRaisesRegex(ValueError, "public isolation copy differs"):
            self.validate()

    def test_proof_paths_cannot_alias_reserved_epoch_registration(self):
        selected = copy.deepcopy(self.selected)
        selected["stageRegistration"]["path"] = "registration.json"
        with self.assertRaisesRegex(ValueError, "missing, linked, or changed"):
            self.gateway.validate_archive(self.epoch, self.pins, selected)

    def test_pending_real_forecast_does_not_admit_empirical_input(self):
        if self.plan["forecast"]["status"] == "registered":
            self.skipTest("the coordinating parent's real forecast has been registered")
        with self.assertRaisesRegex(ValueError, "cost forecast is not registered"):
            self.validate(dict(self.pins, dataKind="empirical"))


if __name__ == "__main__":
    unittest.main()
