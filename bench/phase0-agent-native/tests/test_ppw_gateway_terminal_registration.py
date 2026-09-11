"""SYNTHETIC/read-only checks for the prospective #1434 registration chain."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from ppw_pilot_epoch import synthetic_current_analysis_manifest

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

    def test_historical_terminal_manifest_refuses_current_source_and_synthetic_analysis_passes(self):
        historical = analysis.load(analysis.MANIFEST)
        self.assertFalse(historical["collectionAuthorized"])
        self.assertEqual("terminal-semantics-registered", historical["recoveryStatus"])
        self.assertEqual(
            analysis.TERMINAL_PROFILE,
            historical["gatewayExecutionProfile"]["path"],
        )
        self.assertEqual(
            analysis.TERMINAL_EVIDENCE,
            historical["gatewayRecoveryEvidence"]["path"],
        )
        with self.assertRaisesRegex(ValueError, "analysis artifact changed: ppw-budget-gateway.py"):
            analysis.validate_analysis_registration()
        with tempfile.TemporaryDirectory(
                prefix="SYNTHETIC-current-analysis-", dir=BENCH / "tests") as raw:
            fixture = synthetic_current_analysis_manifest(analysis, Path(raw))
            with patch.object(analysis, "MANIFEST", fixture):
                manifest = analysis.validate_analysis_registration()
        self.assertFalse(manifest["collectionAuthorized"])
        self.assertEqual("pending-reviewed-wire-evidence", manifest["recoveryStatus"])
        self.assertEqual(
            analysis.GATEWAY_PROFILE,
            manifest["gatewayExecutionProfile"]["path"],
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

    def test_proposal_refresh_is_explicit_and_refuses_an_existing_target(self):
        with self.assertRaises(SystemExit):
            terminal_generator.main(["--refresh-unexecuted-proposal"])
        with tempfile.TemporaryDirectory(prefix="SYNTHETIC-terminal-generator-", dir=BENCH / "tests") as raw:
            root = Path(raw)
            manifest = root / "analysis.json"
            manifest.write_text(json.dumps({
                "recoveryStatus": "terminal-semantics-registered",
                "collectionAuthorized": False,
                "supersedes": {
                    "path": analysis.PRE_TERMINAL_MANIFEST,
                    "sha256": terminal_generator.PRIOR["analysis"][1],
                },
            }))
            (root / "epochs" / terminal_generator.EPOCH).mkdir(parents=True)
            before = manifest.read_bytes()
            with patch.object(terminal_generator, "BENCH", root), \
                    patch.object(terminal_generator, "OUTPUTS", {"analysis": manifest}), \
                    patch.object(terminal_generator, "module", return_value=SimpleNamespace(
                        PRE_TERMINAL_MANIFEST=analysis.PRE_TERMINAL_MANIFEST)):
                with self.assertRaisesRegex(ValueError, "target epoch already exists"):
                    terminal_generator.write_documents({}, legacy_generator, refresh_unexecuted=True)
            self.assertEqual(before, manifest.read_bytes())

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

    def test_historical_authority_refuses_current_source_and_synthetic_admission_passes(self):
        instrument = load_module("terminal_collector_admission_test", "ppw-instrument.py")
        with self.assertRaisesRegex(
                ValueError, "native compatibility evidence binds different source"):
            gateway.resolve_collection_profile(gateway.RECOVERY_PROFILE)
        with tempfile.TemporaryDirectory(
                prefix="SYNTHETIC-terminal-authority-", dir=BENCH / "tests") as raw:
            root = Path(raw)
            registration = gateway.resolve_profile(gateway.PROFILE)
            selected = registration["stages"]["pilot"]
            selected["epochId"] = "SYNTHETIC-terminal-pilot"
            registration.update(collectionAuthorized=True, fundingStatus="approved")
            for name in ("stageRegistration", "modelRegistration"):
                source = gateway.ROOT / selected[name]["path"]
                destination = root / selected[name]["path"]
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(source.read_bytes())
            authority_path = root / "SYNTHETIC-terminal-authorization.json"
            authority_path.write_text(json.dumps({
                "kind": "pp-w-terminal-semantics-recovery-authorization",
                "epochId": selected["epochId"],
                "stage": "pilot",
                "spendingCeilingUsd": 1000,
                "nullResultAccepted": True,
                "approvedBy": "SYNTHETIC fixture owner",
                "approvalReference": "SYNTHETIC test-only authority",
            }, indent=2) + "\n", encoding="utf-8")
            selected["spendAuthorization"] = {
                "path": authority_path.name,
                "sha256": hashlib.sha256(authority_path.read_bytes()).hexdigest(),
            }
            authority = instrument.validate_collection_authorization(
                registration, selected, root, selected["epochId"], "pilot", True)
            self.assertEqual("pp-w-terminal-semantics-recovery-authorization", authority["kind"])
            self.assertEqual(1000, authority["spendingCeilingUsd"])
            with self.assertRaisesRegex(ValueError, "paid collection requires"):
                instrument.validate_collection_authorization(
                    registration, selected, root, selected["epochId"], "pilot", False)

    def test_isolation_expectations_remain_identical_across_lifecycle_consumers(self):
        budget = load_module("terminal_budget_isolation_test", "ppw-gateway-budget.py")
        recovery = load_module("terminal_recovery_isolation_test", "ppw-gateway-recovery.py")
        client = load_module("terminal_client_isolation_test", "ppw-gateway-client.py")
        self.assertEqual(client.ISOLATION, budget.ISOLATION_KIND)
        self.assertEqual(client.ISOLATION, recovery.ISOLATION_KIND)
        self.assertEqual(client.PROBE_EXPECTATIONS, budget.ISOLATION_PROBE)
        self.assertEqual(client.PROBE_EXPECTATIONS, recovery.ISOLATION_PROBE)


if __name__ == "__main__":
    unittest.main()
