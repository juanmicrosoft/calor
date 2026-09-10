"""Synthetic authorization-shape controls; no epoch or model is invoked."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

BENCH = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("authorization_instrument", BENCH / "ppw-instrument.py")
instrument = importlib.util.module_from_spec(spec)
spec.loader.exec_module(instrument)


class CollectionAuthorizationTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".authorization-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.registration = {"collectionAuthorized": True, "fundingStatus": "approved"}
        self.authorization = {
            "kind": "pp-w-rows-spending-authorization",
            "epochId": "synthetic-authorization-control",
            "stage": "pilot",
            "spendingCeilingUsd": 1.0,
            "nullResultAccepted": True,
            "approvedBy": "synthetic test fixture, not a real approval",
            "approvalReference": "synthetic:test-only",
        }
        self.selected = {}
        for name in ("stageRegistration", "modelRegistration"):
            path = self.root / (name + ".json")
            path.write_text("{}\n")
            self.selected[name] = {"path": path.name, "sha256": instrument.digest(path)}
        self.save_authorization(self.authorization)

    def tearDown(self):
        shutil.rmtree(self.root)

    def save_authorization(self, value):
        path = self.root / "synthetic-authorization.json"
        path.write_text(json.dumps(value) + "\n")
        self.selected["spendAuthorization"] = {"path": path.name, "sha256": instrument.digest(path)}

    def validate(self, confirm=True):
        instrument.validate_collection_authorization(
            self.registration, self.selected, self.root,
            "synthetic-authorization-control", "pilot", confirm)

    def test_synthetic_complete_structure_passes_only_the_pure_shape_check(self):
        self.validate()
        with self.assertRaisesRegex(ValueError, "confirm-paid"):
            self.validate(False)

    def test_valid_evidence_cannot_override_explicit_unfunded_or_unauthorized_state(self):
        for value in (False, None, 1, "true"):
            with self.subTest(collectionAuthorized=value):
                self.registration["collectionAuthorized"] = value
                with self.assertRaisesRegex(ValueError, "does not authorize"):
                    self.validate()
        self.registration["collectionAuthorized"] = True
        for value in ("unfunded", "pending", None, "funded"):
            with self.subTest(fundingStatus=value):
                self.registration["fundingStatus"] = value
                with self.assertRaisesRegex(ValueError, "funding is not approved"):
                    self.validate()

    def test_empty_or_unstructured_evidence_is_not_an_authorization(self):
        self.save_authorization({})
        with self.assertRaisesRegex(ValueError, "structured spending"):
            self.validate()
        path = self.root / self.selected["spendAuthorization"]["path"]
        path.write_text("")
        self.selected["spendAuthorization"]["sha256"] = instrument.digest(path)
        with self.assertRaises(ValueError):
            self.validate()

    def test_epoch_stage_ceiling_acceptance_and_approval_reference_are_required(self):
        mutations = [
            ("kind", "unrelated"), ("epochId", "another-epoch"), ("stage", "confirmatory"),
            ("spendingCeilingUsd", 0), ("spendingCeilingUsd", -1),
            ("spendingCeilingUsd", float("inf")), ("spendingCeilingUsd", float("nan")),
            ("spendingCeilingUsd", True), ("spendingCeilingUsd", "1"),
            ("spendingCeilingUsd", None), ("nullResultAccepted", False),
            ("approvedBy", ""), ("approvalReference", " "),
        ]
        for name, value in mutations:
            with self.subTest(field=name, value=value):
                authorization = copy.deepcopy(self.authorization)
                authorization[name] = value
                self.save_authorization(authorization)
                with self.assertRaises(ValueError):
                    self.validate()

    def test_changed_evidence_does_not_match_its_pin(self):
        (self.root / self.selected["spendAuthorization"]["path"]).write_text('{"changed":true}\n')
        with self.assertRaisesRegex(ValueError, "spendAuthorization evidence is missing or changed"):
            self.validate()

    def test_run_refuses_opaque_evidence_before_any_product_or_agent_command(self):
        self.registration = {"collectionAuthorized": False, "fundingStatus": "unfunded"}
        path = self.root / "registration.json"
        path.write_text(json.dumps(self.registration))
        for confirm in (False, True):
            with patch.object(instrument, "validate_collection_environment"), \
                    patch.object(instrument, "validate_registration", return_value=self.selected), \
                    patch.object(instrument, "validate_tasks"), \
                    patch.object(instrument, "command", side_effect=AssertionError("no subprocess allowed")):
                with self.assertRaisesRegex(ValueError, "does not authorize"):
                    instrument.run_epoch(
                        path, self.root / "unused-tasks", self.root / "unused-product",
                        self.root / "never-created", "synthetic-authorization-control", "pilot", confirm)
        self.assertFalse((self.root / "never-created").exists())


if __name__ == "__main__":
    unittest.main()
