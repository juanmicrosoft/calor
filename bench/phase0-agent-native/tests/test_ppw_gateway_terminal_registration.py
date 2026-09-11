"""SYNTHETIC/read-only checks for the prospective #1434 registration chain."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

BENCH = Path(__file__).resolve().parents[1]

def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


analysis = load_module("terminal_analysis_test", "ppw-pilot-adjudicate.py")
gateway = load_module("terminal_gateway_registration_test", "ppw-gateway-registration.py")
legacy_generator = load_module(
    "terminal_legacy_registration_test", "ppw-gateway-register-recovery.py")
spending = load_module("terminal_spending_test", "ppw-spending.py")
terminal_generator = load_module(
    "terminal_registration_generator_test", "ppw-gateway-register-terminal.py")


class TerminalRegistrationTests(unittest.TestCase):
    def test_pre_terminal_manifest_is_the_exact_1433_registration(self):
        preserved = BENCH / analysis.PRE_TERMINAL_MANIFEST
        self.assertEqual(
            "d0c1f4735294bfb393768ae09273110da3743601d320a731e3b9cba6c6046c6b",
            hashlib.sha256(preserved.read_bytes()).hexdigest(),
        )
        value = json.loads(preserved.read_text(encoding="utf-8"))
        self.assertEqual("registered", value["recoveryStatus"])
        self.assertEqual(analysis.RECOVERY_PROFILE, value["gatewayExecutionProfile"]["path"])
        self.assertEqual(analysis.RECOVERY_EVIDENCE, value["gatewayRecoveryEvidence"]["path"])

    def test_manifest_is_pending_or_selects_only_the_complete_terminal_chain(self):
        manifest = analysis.validate_analysis_registration()
        self.assertFalse(manifest["collectionAuthorized"])
        if manifest["recoveryStatus"] == "pending-reviewed-terminal-evidence":
            self.assertEqual(
                analysis.RECOVERY_PROFILE,
                manifest["gatewayExecutionProfile"]["path"],
            )
            self.assertEqual([
                "registrations/ppw-rows-stage1/gateway-evidence/"
                "gateway-native-wire-1434-evidence.json",
                "registrations/ppw-rows-stage1/gateway-evidence/"
                "gateway-native-startup-1434-evidence.json",
            ], manifest["requiredTerminalEvidence"])
        else:
            self.assertEqual("terminal-semantics-registered", manifest["recoveryStatus"])
            self.assertEqual(
                analysis.TERMINAL_PROFILE,
                manifest["gatewayExecutionProfile"]["path"],
            )
            self.assertEqual(
                analysis.TERMINAL_EVIDENCE,
                manifest["gatewayRecoveryEvidence"]["path"],
            )

    def test_generator_contract_has_one_existing_epoch_and_no_operational_writes(self):
        self.assertEqual("w-rows-pilot-gateway-002", terminal_generator.OUTPUT_SCHEMA["targetEpoch"])
        self.assertFalse(terminal_generator.OUTPUT_SCHEMA["writesOperationalState"])
        self.assertEqual({
            "gateway-evidence/gateway-native-wire-1434-evidence.json",
            "gateway-evidence/gateway-native-startup-1434-evidence.json",
        }, {
            value["path"]
            for value in terminal_generator.INPUT_CONTRACT["freshEvidence"].values()
        })
        self.assertEqual(30, len(spending.GATEWAY_ARTIFACTS))
        self.assertIn("ppw-gateway-register-terminal.py", spending.GATEWAY_ARTIFACTS)
        self.assertNotIn("w-rows-pilot-gateway-003",
                         json.dumps(terminal_generator.OUTPUT_SCHEMA, sort_keys=True))

    def test_generator_refuses_missing_fresh_proofs_before_operational_lookup(self):
        missing = BENCH / "tests/SYNTHETIC-missing-terminal-proof.json"
        with patch.object(terminal_generator, "WIRE", missing), \
                patch.object(terminal_generator, "STARTUP", missing), \
                patch.object(terminal_generator, "load_predecessors",
                             side_effect=AssertionError("must refuse before predecessor or ledger reads")):
            with self.assertRaisesRegex(ValueError, "missing root-supplied fresh wire evidence"):
                terminal_generator.build_documents()

    def test_1432_native_proofs_are_stale_for_changed_terminal_sources(self):
        old_startup = json.loads(
            (gateway.ROOT / "gateway-evidence/gateway-native-startup-1432-evidence.json")
            .read_text(encoding="utf-8"))
        with self.assertRaises(ValueError):
            gateway.validate_native_startup(old_startup)
        old_wire = json.loads(
            (gateway.ROOT / "gateway-evidence/gateway-native-wire-1432-evidence.json")
            .read_text(encoding="utf-8"))
        with self.assertRaisesRegex(ValueError, "exact five reviewed sources"):
            legacy_generator.validate_wire_evidence(old_wire)

    def test_only_original_history_or_new_terminal_profile_can_be_selected(self):
        self.assertEqual("gateway-execution-profile-1434.json",
                         gateway.RECOVERY_PROFILE.name)
        registration = gateway.resolve_profile(gateway.PROFILE)
        self.assertEqual("w-rows-pilot-gateway-001",
                         registration["stages"]["pilot"]["epochId"])
        for path in (gateway.PRE_TERMINAL_PROFILE, gateway.ROOT / "unapproved-third-profile.json"):
            with self.subTest(path=path), self.assertRaisesRegex(
                    ValueError, "canonical reviewed #1434 terminal profile"):
                gateway.resolve_collection_profile(path)

    def test_terminal_semantics_preserve_population_budget_and_valid_nonzero_exits(self):
        budget = load_module("terminal_budget_test", "ppw-gateway-budget.py")
        proof = {"path": "gateway-instrument-amendment-1434.json", "sha256": "a" * 64}
        amendment = {
            "supersedes": spending.PRE_TERMINAL_AMENDMENT,
            "terminalSemantics": terminal_generator.terminal_semantics(budget),
        }
        authorization = {
            "supersedes": spending.PRE_TERMINAL_AUTHORIZATION,
            "terminalSemanticsAmendment": proof,
            "additionalAllowance": False,
            "ledgerReset": False,
        }
        plan = {
            "supersedes": spending.PRE_TERMINAL_PLAN,
            "recovery": {
                "terminalSemanticsAmendment": proof,
                "plannedInvocations": 444,
                "continuationInvocations": 443,
                "replacementAttempts": 0,
                "sameExperimentCeiling": True,
                "accountedTerminalSlots": 444,
                "validCompletionPopulation": "registered-terminal-valid-attempts-only",
            },
        }
        selected = {
            "instrumentAmendment": proof,
            "terminalSemanticsAmendment": proof,
        }
        semantics = spending.validate_terminal_supersession(
            selected, authorization, plan, amendment)
        self.assertTrue(semantics["validTerminal"]["nonzeroWithObservedWorkRemainsEligible"])
        self.assertFalse(semantics["terminalInvalid"]["requiresSourceInspection"])
        self.assertFalse(semantics["scientificMethodChange"])
        self.assertFalse(semantics["zeroImputationPermitted"])
        self.assertFalse(semantics["poolingChangePermitted"])
        for mutation in (
            lambda value: value["terminalSemantics"]["terminalInvalid"].update(
                replacementPermitted=True),
            lambda value: value["terminalSemantics"]["validTerminal"].update(
                nonzeroWithObservedWorkRemainsEligible=False),
            lambda value: value["terminalSemantics"].update(scientificMethodChange=True),
            lambda value: value.update(supersedes={"path": "foreign.json", "sha256": "b" * 64}),
        ):
            changed = copy.deepcopy(amendment)
            mutation(changed)
            with self.assertRaisesRegex(ValueError, "unregistered terminal-attempt semantics"):
                spending.validate_terminal_supersession(
                    selected, authorization, plan, changed)

    def test_predecessor_keeps_full_forecast_and_canonical_total_ceiling(self):
        plan = json.loads((gateway.ROOT / "gateway-spending-plan-1432.json").read_text())
        authorization = json.loads((gateway.ROOT / "gateway-authorization-1432.json").read_text())
        self.assertEqual(444, plan["forecast"]["plannedInvocations"])
        self.assertEqual(444, plan["recovery"]["plannedInvocations"])
        self.assertEqual(443, plan["recovery"]["continuationInvocations"])
        self.assertEqual(1000, authorization["spendingCeilingUsd"])
        self.assertFalse(authorization["additionalAllowance"])
        self.assertFalse(authorization["ledgerReset"])
        self.assertEqual(
            "ppw-budget/epic1254-pilot.sqlite3",
            authorization["ledgerBinding"]["relativePath"],
        )


if __name__ == "__main__":
    unittest.main()
