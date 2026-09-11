"""Reviewed historical planning evidence; no provider, client or collection execution."""
import copy
import json
import os
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

from ppw_redesign_epoch import BENCH, instrument, save


spending = instrument.helper("ppw-spending.py")
REGISTRATION = BENCH / "registrations/ppw-rows-stage1"


class ForecastTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".forecast-SYNTHETIC-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        for proof in spending.FORECAST_EVIDENCE.values():
            destination = self.root / proof["path"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REGISTRATION / proof["path"], destination)
        self.proposal = json.loads(
            (self.root / spending.FORECAST_EVIDENCE["proposal"]["path"]).read_text())
        self.plan = {
            "forecast": dict(self.proposal["forecast"], status="registered"),
            "forecastRegistration": {
                "from": "proposed", "to": "registered",
                "reviewReference": spending.FORECAST_REVIEW,
                "artifacts": copy.deepcopy(spending.FORECAST_EVIDENCE),
            },
        }

    def validate(self, ceiling=1_000_000_000, slots=444):
        return spending.validate_forecast(self.plan, ceiling, slots, self.root)

    def test_exact_reviewed_projection_is_readonly_and_keeps_proposal_unchanged(self):
        before = {proof["path"]: (self.root / proof["path"]).read_bytes()
                  for proof in spending.FORECAST_EVIDENCE.values()}
        with patch("subprocess.run", side_effect=AssertionError("forecast validation cannot execute")):
            self.assertEqual(spending.FORECAST_EVIDENCE, self.validate())
        self.assertEqual("proposed", self.proposal["forecast"]["status"])
        self.assertEqual("registered", self.plan["forecast"]["status"])
        self.assertEqual("900.36", self.plan["forecast"]["estimatedFullPilotUsd"])
        self.assertEqual("99.64", self.plan["forecast"]["baselineHeadroomUsd"])
        self.assertFalse(self.plan["forecast"]["completionGuaranteed"])
        self.assertIsNone(self.plan["forecast"]["numericalInterruptionProbability"])
        for relative, raw in before.items():
            self.assertEqual(raw, (self.root / relative).read_bytes())

    def test_small_number_nonblank_method_or_unreviewed_status_cannot_admit(self):
        original = copy.deepcopy(self.plan)
        for change in (
            lambda value: value.pop("forecastRegistration"),
            lambda value: value["forecast"].update(status="proposed"),
            lambda value: value["forecast"].update(estimatedFullPilotUsd="0.01"),
            lambda value: value["forecast"].update(method="self-asserted cheap forecast"),
            lambda value: value["forecast"].update(limitations=[]),
            lambda value: value["forecast"].update(completionGuaranteed=True),
            lambda value: value["forecast"].update(numericalInterruptionProbability=0),
            lambda value: value["forecast"].update(plannedInvocations=443),
            lambda value: value["forecast"].update(experimentalObservations=1),
        ):
            self.plan = copy.deepcopy(original)
            change(self.plan)
            with self.subTest(plan=self.plan), self.assertRaises(ValueError):
                self.validate()

    def test_forged_review_and_rehashed_proposal_or_script_are_not_approved(self):
        for name, proof in spending.FORECAST_EVIDENCE.items():
            path = self.root / proof["path"]
            original = path.read_bytes()
            try:
                if name == "script":
                    path.write_bytes(original + b"\n# SYNTHETIC substituted source\n")
                elif name == "review":
                    review = json.loads(original)
                    review["decision"] = "self-approved"
                    save(path, review)
                else:
                    proposal = json.loads(original)
                    proposal["forecast"]["estimatedFullPilotUsd"] = "0.01"
                    save(path, proposal)
                self.plan["forecastRegistration"]["artifacts"][name]["sha256"] = spending.digest(path)
                with self.subTest(name=name), self.assertRaisesRegex(
                        ValueError, "exact independently reviewed evidence"):
                    self.validate()
            finally:
                path.write_bytes(original)
                self.plan["forecastRegistration"]["artifacts"] = copy.deepcopy(spending.FORECAST_EVIDENCE)

    def test_missing_changed_or_aliased_proof_is_not_a_canonical_fallback(self):
        for name, proof in spending.FORECAST_EVIDENCE.items():
            path = self.root / proof["path"]
            original = path.read_bytes()
            try:
                path.unlink()
                with self.subTest(name=name, kind="missing"), self.assertRaisesRegex(ValueError, "missing"):
                    self.validate()
                path.write_bytes(original + b"\n")
                with self.subTest(name=name, kind="changed"), self.assertRaisesRegex(ValueError, "changed"):
                    self.validate()
                self.plan["forecastRegistration"]["artifacts"][name]["path"] = "../" + proof["path"]
                with self.subTest(name=name, kind="alias"), self.assertRaises(ValueError):
                    self.validate()
            finally:
                path.write_bytes(original)
                self.plan["forecastRegistration"]["artifacts"] = copy.deepcopy(spending.FORECAST_EVIDENCE)

    @unittest.skipIf(os.name == "nt", "symlink creation requires optional Windows privileges")
    def test_linked_evidence_is_refused(self):
        proof = spending.FORECAST_EVIDENCE["review"]
        path = self.root / proof["path"]
        path.unlink()
        path.symlink_to(REGISTRATION / proof["path"])
        with self.assertRaisesRegex(ValueError, "linked"):
            self.validate()

    def test_other_ceiling_inventory_and_review_transition_are_refused(self):
        for ceiling, slots in ((500_000_000, 444), (2_000_000_000, 444), (1_000_000_000, 443)):
            with self.subTest(ceiling=ceiling, slots=slots), self.assertRaises(ValueError):
                self.validate(ceiling, slots)
        for field, value in (("from", "registered"), ("to", "approved"),
                             ("reviewReference", "SYNTHETIC-forged-review")):
            original = self.plan["forecastRegistration"][field]
            self.plan["forecastRegistration"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.validate()
            self.plan["forecastRegistration"][field] = original

    def test_current_plan_has_explicit_pending_plan_supersession(self):
        plan = json.loads((REGISTRATION / "gateway-spending-plan.json").read_text())
        proof = plan["supersedes"]
        previous, value = spending.pinned_document(REGISTRATION, proof, "previous spending plan")
        self.assertEqual("gateway-spending-plan.pre-forecast-1406.json", previous.name)
        self.assertEqual("awaiting-independent-parent-forecast", value["forecast"]["status"])
        self.assertEqual(value["ledgerBinding"], plan["ledgerBinding"])
        self.assertEqual(value["ceilingUsd"], plan["ceilingUsd"])
        self.assertEqual(value["authorizationSha256"], plan["authorizationSha256"])
        self.assertEqual(spending.FORECAST_EVIDENCE,
                         spending.validate_forecast(plan, 1_000_000_000, 444, REGISTRATION))


if __name__ == "__main__":
    unittest.main()
