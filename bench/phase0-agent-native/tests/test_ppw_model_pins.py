"""Prospective pin agreement and funding refusal; no model invocations."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch
import uuid

BENCH = Path(__file__).resolve().parents[1]
EPOCH = BENCH / "epochs/w-rows-pilot-001"
METHOD = BENCH / "registrations/ppw-rows-stage1"


def load(path):
    return json.loads(path.read_text())


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ProspectiveModelPinsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("model_pin_instrument", BENCH / "ppw-instrument.py")
        cls.instrument = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.instrument)
        cls.pins = load(EPOCH / "pins.json")
        cls.registration = load(EPOCH / "registration.json")

    def test_actual_pins_and_task_certificates_validate_without_collection(self):
        self.instrument.validate_pins(self.pins, self.registration, "pilot", "w-rows-pilot-001")
        self.instrument.validate_tasks(BENCH / "tasks/ppw-redesign", self.registration)
        self.assertEqual("scaffolded", self.pins["lifecycle"])
        self.assertEqual(digest(EPOCH / "registration.json"), self.pins["registrationSha256"])
        self.assertFalse((EPOCH / "runs").exists())
        self.assertFalse((EPOCH / "ppw-stage-ledger.json").exists())
        self.assertFalse(self.registration["collectionAuthorized"])
        self.assertEqual("unfunded", self.registration["fundingStatus"])

    def test_model_client_and_count_match_the_reviewed_method_and_observation(self):
        method = load(METHOD / "registration.json")
        model = load(METHOD / "model-registration.json")
        observed = load(EPOCH / "admission/agent-provenance.json")
        self.assertEqual(model, load(EPOCH / "admission/model-registration.json"))
        self.assertEqual(self.pins["modelPin"], method["prospectiveModelPin"])
        self.assertEqual(self.pins["modelPin"], model["modelPin"])
        self.assertEqual(self.pins["agentVersion"], method["prospectiveAgentVersion"])
        self.assertEqual(self.pins["agentVersion"], observed["stdout"].strip())
        self.assertEqual(model["clientObservation"]["executableSha256"], observed["executableSha256"])
        self.assertFalse(observed["modelInvoked"])
        self.assertEqual(74, self.pins["runsPerArm"])
        self.assertEqual(self.pins["runsPerArm"], method["runsPerArm"])
        self.assertEqual(model["methodRegistration"]["sha256"], digest(METHOD / "registration.json"))

    def test_admission_proofs_and_distinct_product_hashes_are_bound(self):
        stage = self.registration["stages"]["pilot"]
        for name in ("stageRegistration", "modelRegistration"):
            proof = stage[name]
            self.assertEqual(digest(EPOCH / proof["path"]), proof["sha256"])
        task_proof = self.registration["taskSupersession"]
        self.assertEqual(digest(EPOCH / task_proof["path"]), task_proof["sha256"])
        self.assertEqual((EPOCH / task_proof["path"]).read_bytes(),
                         (BENCH / "registrations/ppw-redesign-task-supersession.json").read_bytes())
        canaries = load(EPOCH / "admission/product-policy-canaries.json")
        self.assertFalse(canaries["wrapperExecuted"])
        self.assertEqual(0, canaries["observations"]["A"]["exitCode"])
        self.assertEqual(1, canaries["observations"]["B"]["exitCode"])
        for observation in canaries["observations"].values():
            self.assertEqual(self.pins["compiler"]["compilerHash"], observation["compilerHash"])
        self.assertNotEqual(self.pins["compiler"]["compilerHash"], self.pins["compiler"]["calorSha256"])
        amendment = load(METHOD / "spending-instrument-amendment.json")
        self.assertEqual(self.pins["harnessArtifacts"], amendment["supersededHarnessArtifacts"])
        gateway = load(METHOD / "gateway-instrument-amendment.json")
        self.assertEqual(amendment["replacementHarnessArtifacts"], gateway["supersededHarnessArtifacts"])
        self.assertEqual(digest(METHOD / "spending-instrument-amendment.json"), gateway["supersedes"]["sha256"])
        for filename, sha in gateway["replacementHarnessArtifacts"].items():
            self.assertEqual(digest(BENCH / filename), sha)

    def test_spending_amendment_preserves_historical_inputs_without_activating_collection(self):
        amendment = load(METHOD / "spending-instrument-amendment.json")
        self.assertEqual("pp-w-prospective-spending-instrument-amendment", amendment["kind"])
        self.assertFalse(amendment["collectionAuthorized"])
        self.assertFalse(amendment["analysisArithmeticChanged"])
        self.assertTrue(amendment["frozenProtocolUnchanged"])
        for relative, sha in amendment["preservedArtifacts"].items():
            self.assertEqual(digest(BENCH / relative), sha, relative)
        spending = self.instrument.helper("ppw-spending.py")
        gateway = load(METHOD / "gateway-instrument-amendment.json")
        self.assertEqual(amendment["replacementHarnessArtifacts"], gateway["supersededHarnessArtifacts"])
        self.assertEqual(spending.artifact_manifest(spending.GATEWAY), gateway["replacementHarnessArtifacts"])
        self.assertEqual(15, len(amendment["replacementHarnessArtifacts"]))
        self.assertEqual(25, len(gateway["replacementHarnessArtifacts"]))
        analysis = load(METHOD / "analysis-registration.json")
        for reference in ("supersedes", "instrumentAmendment"):
            proof = analysis[reference]
            self.assertEqual(digest(BENCH / proof["path"]), proof["sha256"])
        target = amendment["preservedArtifacts"][
            "registrations/ppw-rows-stage1/analysis-registration.pre-spending-1378.json"]
        proof, seen = analysis["supersedes"], set()
        while proof["sha256"] != target:
            self.assertNotIn(proof["sha256"], seen, "analysis lineage cycle")
            seen.add(proof["sha256"])
            self.assertEqual(digest(BENCH / proof["path"]), proof["sha256"])
            proof = load(BENCH / proof["path"])["supersedes"]
        self.assertEqual(digest(BENCH / proof["path"]), target)
        old_analysis = load(BENCH / proof["path"])
        self.assertEqual(old_analysis["artifacts"]["ppw-instrument.py"],
                         self.pins["harnessArtifacts"]["ppw-instrument.py"])
        self.assertFalse(self.registration["collectionAuthorized"])
        self.assertNotEqual(self.pins["harnessArtifacts"], amendment["replacementHarnessArtifacts"])

    def test_actual_stage_rejects_model_or_client_drift_and_missing_identity(self):
        for field in ("modelPin", "agentVersion"):
            for value in ("different-identity", "", None, True):
                pins = copy.deepcopy(self.pins)
                pins[field] = value
                with self.subTest(field=field, value=value), self.assertRaisesRegex(
                        ValueError, field + " differs"):
                    self.instrument.validate_pins(pins, self.registration, "pilot", "w-rows-pilot-001")
            registration = copy.deepcopy(self.registration)
            del registration["stages"]["pilot"][field]
            with self.subTest(field=field, missing=True), self.assertRaisesRegex(
                    ValueError, field + " differs"):
                self.instrument.validate_pins(self.pins, registration, "pilot", "w-rows-pilot-001")

    def test_missing_authorization_refuses_before_any_product_or_agent_command(self):
        self.assertNotIn("spendAuthorization", self.registration["stages"]["pilot"])
        output = BENCH / ("never-created-pin-admission-" + uuid.uuid4().hex)
        env = {k: v for k, v in os.environ.items()
               if not k.startswith(("CALOR_P0_", "CALOR_LOOP_"))}
        for confirm in (False, True):
            with patch.dict(os.environ, env, clear=True), patch.object(
                    self.instrument, "command", side_effect=AssertionError("subprocess must not be reached")):
                with self.assertRaisesRegex(ValueError, "registration does not authorize collection"):
                    self.instrument.run_epoch(
                        EPOCH / "registration.json", BENCH / "tasks/ppw-redesign",
                        "unused-product", output, "w-rows-pilot-001", "pilot", confirm)
        self.assertFalse(output.exists())

    def test_confirmatory_size_and_stage_are_not_invented(self):
        model = load(METHOD / "model-registration.json")
        self.assertIsNone(model["stage2SampleSize"])
        self.assertIsNone(model["stage2EffectSize"])
        self.assertEqual({"pilot"}, set(self.registration["stages"]))
        with self.assertRaisesRegex(ValueError, "stage not registered"):
            self.instrument.validate_registration(self.registration, "confirmatory", "w-rows-001")

    def test_opaque_evidence_cannot_activate_an_explicitly_unfunded_registration(self):
        original_load = self.instrument.load
        env = {k: v for k, v in os.environ.items()
               if not k.startswith(("CALOR_P0_", "CALOR_LOOP_"))}
        for authorized, funding, reason in (
            (False, "unfunded", "does not authorize"),
            (True, "unfunded", "funding is not approved"),
            (False, "approved", "does not authorize"),
        ):
            registration = copy.deepcopy(self.registration)
            registration["collectionAuthorized"] = authorized
            registration["fundingStatus"] = funding
            stage = registration["stages"]["pilot"]
            stage["spendAuthorization"] = dict(stage["stageRegistration"])
            proof = stage["spendAuthorization"]
            self.assertEqual(digest(EPOCH / proof["path"]), proof["sha256"])

            def load_with_copy(path):
                return registration if Path(path) == EPOCH / "registration.json" else original_load(path)

            with patch.dict(os.environ, env, clear=True), \
                    patch.object(self.instrument, "load", side_effect=load_with_copy), \
                    patch.object(self.instrument, "command", side_effect=AssertionError("no subprocess allowed")):
                with self.assertRaisesRegex(ValueError, reason):
                    self.instrument.run_epoch(
                        EPOCH / "registration.json", BENCH / "tasks/ppw-redesign",
                        "unused-product", BENCH / "never-created-opaque-admission",
                        "w-rows-pilot-001", "pilot", True)


if __name__ == "__main__":
    unittest.main()
