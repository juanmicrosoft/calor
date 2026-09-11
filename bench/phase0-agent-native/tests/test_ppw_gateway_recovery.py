"""SYNTHETIC-only tests for the bounded PP-W zero-request recovery core."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import stat
import unittest
from unittest.mock import patch
import uuid


BENCH = Path(__file__).resolve().parents[1]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


recovery = load("ppw_gateway_recovery_test", BENCH / "ppw-gateway-recovery.py")
budget = load("ppw_gateway_budget_recovery_test", BENCH / "ppw-gateway-budget.py")
operator = load("ppw_gateway_recover_test", BENCH / "ppw-gateway-recover.py")
registration_generator = load(
    "ppw_gateway_recovery_registration_test", BENCH / "ppw-gateway-register-recovery.py")


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def isolation():
    return {
        "kind": recovery.ISOLATION_KIND,
        "kernelProbe": dict(recovery.ISOLATION_PROBE),
        "modelInvoked": False,
        "clientSha256": "6" * 64,
        "policySha256": "7" * 64,
        "workspaceRoot": "/SYNTHETIC/workspace",
        "authoritativeRoot": "/SYNTHETIC/archive",
    }


def attempt(slot, binding):
    return {
        "schemaVersion": 1,
        "kind": recovery.ATTEMPT_START_KIND,
        "slot": slot,
        "attemptNumber": 1,
        "maximumAttempts": 1,
        "actualClientInvocationPending": True,
        "authoritativeRootSha256": "8" * 64,
        "producerSourceSha256": {
            name: binding["harnessArtifacts"][name]
            for name in recovery.TERMINAL_SOURCE_FILES
        },
        "nonce": "9" * 64,
    }


class RecoveryFixture(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parent / (
            ".gateway-recovery-SYNTHETIC-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.protected = self.root / "protected"
        self.protected.mkdir(mode=0o700)
        self.ledger = self.protected / "current.sqlite3"
        self.backup = self.protected / "failed-ledger.sqlite3"
        self.archive = self.root / "failed-archive"
        self.archive.mkdir()
        self.old = self.binding("w-rows-pilot-gateway-001", "1", "2")
        self.target = self.binding("w-rows-pilot-gateway-002", "3", "4")
        self.target["harnessArtifacts"].update(budget.source_identities())
        self.recovery_registration_sha256 = "5" * 64
        self.create_ledger()
        self.create_archive()
        self.refresh_evidence()

    def binding(self, epoch, protocol, plan):
        tasks = ["task-a", "task-b", "task-c"]
        slots = ["%s/%s/%d" % (task, arm, run)
                 for run in range(1, 75) for task in tasks
                 for arm in ("calor-permissive", "calor-strict")]
        return {
            "stage": "pilot",
            "epochId": epoch,
            "priceSha256": budget.price_identity(),
            "authorizationSha256": "b" * 64,
            "protocolSha256": protocol * 64,
            "planSha256": plan * 64,
            "harnessArtifacts": {"ppw-gateway-budget.py": "c" * 64},
            "plannedSlots": slots,
        }

    def connect(self):
        connection = sqlite3.connect(self.ledger)
        connection.row_factory = sqlite3.Row
        return connection

    def create_ledger(self):
        with self.connect() as db:
            db.execute("CREATE TABLE scope "
                       "(id INTEGER PRIMARY KEY CHECK(id=1),binding TEXT,ceiling INTEGER,state TEXT,owner TEXT)")
            db.execute("CREATE TABLE requests "
                       "(id TEXT PRIMARY KEY,slot TEXT,request TEXT,reserved INTEGER,charge INTEGER,"
                       "state TEXT,reason TEXT,usage TEXT)")
            db.execute("CREATE TABLE events "
                       "(id INTEGER PRIMARY KEY AUTOINCREMENT,kind TEXT,request_id TEXT,detail TEXT,"
                       "created TEXT DEFAULT(strftime('%Y-%m-%dT%H:%M:%fZ','now')))")
            db.execute("INSERT INTO scope VALUES(1,?,?,?,?)", (
                canonical(self.old), recovery.PILOT_CEILING_MICRO_USD,
                recovery.FAILED_STATE, "SYNTHETIC-owner-secret"))
            for kind, detail in (
                    ("initialized", {"ceilingMicroUsd": recovery.PILOT_CEILING_MICRO_USD}),
                    ("started", {}),
                    ("stopped", {"reason": recovery.FAILED_STATE})):
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                           (kind, None, canonical(detail)))
        self.ledger.chmod(0o600)

    def ledger_snapshot(self, state=None, binding=None, event_count=None):
        with self.connect() as db:
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        if event_count is not None:
            events = events[:event_count]
        return {
            "kind": recovery.KIND,
            "state": state or recovery.FAILED_STATE,
            "binding": binding or self.old,
            "ceilingMicroUsd": recovery.PILOT_CEILING_MICRO_USD,
            "exposureMicroUsd": 0,
            "requests": [],
            "events": events,
            "verdict": None,
            "basis": "request reservations; only validated complete provider usage permits release",
        }

    def create_archive(self):
        registration = {
            "stages": {"pilot": {
                "spendAuthorization": {"path": "authorization.json",
                                       "sha256": self.old["authorizationSha256"]},
                "spendingPlan": {"path": "plan.json", "sha256": self.old["planSha256"]},
            }}
        }
        save(self.archive / "registration.json", registration)
        pins = {
            "epochId": self.old["epochId"],
            "stage": "pilot",
            "lifecycle": "collecting",
            "mode": "live",
            "harnessArtifacts": self.old["harnessArtifacts"],
            "registrationSha256": digest(self.archive / "registration.json"),
            "suite": ["task-a", "task-b", "task-c"],
            "runsPerArm": 74,
            "modelPin": "SYNTHETIC-model",
            "agentVersion": "SYNTHETIC-client",
            "compiler": {"commit": "d" * 40},
            "arms": {"SYNTHETIC": True},
        }
        save(self.archive / "pins.json", pins)
        save(self.archive / "spending-initial.json",
             self.ledger_snapshot(state="collecting", event_count=2))
        save(self.archive / "collection-outcome.json", {
            "kind": "pp-w-incomplete-collection",
            "epochId": self.old["epochId"],
            "stage": "pilot",
            "complete": False,
            "verdict": None,
            "reason": "SYNTHETIC local startup refusal; no scientific outcome",
            "spending": self.ledger_snapshot(),
        })
        opaque = self.archive / "runs" / "task-a" / "calor-permissive" / "run-1" / "result.json"
        opaque.parent.mkdir(parents=True)
        opaque.write_bytes(b"SYNTHETIC OPAQUE OUTCOME BYTES ARE HASHED, NOT PARSED")
        save(opaque.parent / "client-invocation.json", {"exitCode": 1})

    def refresh_evidence(self):
        self.ledger_sha256 = digest(self.ledger)
        self.archive_sha256 = recovery.archive_inventory(self.archive)["sha256"]

    def arguments(self, **changes):
        values = {
            "ledger_path": self.ledger,
            "failed_archive": self.archive,
            "protected_root": self.protected,
            "backup_path": self.backup,
            "expected_old_binding": self.old,
            "target_binding": self.target,
            "expected_ledger_sha256": self.ledger_sha256,
            "expected_archive_inventory_sha256": self.archive_sha256,
            "recovery_registration_sha256": self.recovery_registration_sha256,
        }
        values.update(changes)
        return values

    def prove(self, **changes):
        return recovery.prove_zero_request_recovery(**self.arguments(**changes))

    def apply(self, proof=None, **changes):
        proof = proof or self.prove(**changes)
        return recovery.apply_zero_request_recovery(
            **self.arguments(**changes), confirmed_proof_sha256=proof["proofSha256"])


class ZeroRequestRecoveryTests(RecoveryFixture):
    def test_new_wire_evidence_requires_both_contracts_and_all_five_sources(self):
        isolation = load("ppw_gateway_recovery_isolation_test", BENCH / "ppw-gateway-client.py")
        source_names = {
            "probe-ppw-gateway.py", "ppw-gateway-client.py", "run-pair.sh",
            "ppw-gateway-budget.py", "ppw-budget-gateway.py",
        }
        evidence = {
            "kind": "pp-w-engineering-no-forward-native-probe",
            "success": True,
            "upstreamRequests": 0,
            "experimentalObservations": 0,
            "clientSha256": isolation.CLIENT_SHA256,
            "clientFlags": isolation.registered_client_flags(),
            "observations": [{
                "messagesPath": True,
                "credentialHeaderPresent": True,
                "priceContractAccepted": True,
                "wireContractAccepted": True,
                "unclassifiedBetaCount": 0,
            }],
            "sourceHashes": {name: digest(BENCH / name) for name in source_names},
        }
        registration_generator.validate_wire_evidence(evidence)
        for mutation, message in (
                (lambda value: value["observations"][0].update(wireContractAccepted=False),
                 "both transport and price"),
                (lambda value: value["observations"][0].update(priceContractAccepted=False),
                 "both transport and price"),
                (lambda value: value["sourceHashes"].pop("ppw-budget-gateway.py"),
                 "exact five reviewed sources")):
            changed = copy.deepcopy(evidence)
            mutation(changed)
            with self.assertRaisesRegex(ValueError, message):
                registration_generator.validate_wire_evidence(changed)

    def test_operator_cli_uses_only_canonical_inputs_and_separate_hash_confirmation(self):
        proof = self.prove()
        with patch.object(operator, "canonical_inputs",
                          return_value=(self.arguments(), proof)):
            self.assertEqual(proof, operator.inspect())
            with self.assertRaisesRegex(ValueError, "explicit confirmation"):
                operator.apply("0" * 64)
            result = operator.apply(proof["proofSha256"])
        self.assertTrue(result["applied"])

    def test_operator_cli_has_no_caller_selected_ledger_archive_or_profile_options(self):
        with self.assertRaises(SystemExit):
            operator.main(["inspect", "--ledger", str(self.ledger)])

    def test_operator_cli_refuses_until_reviewed_recovery_registration_is_active(self):
        with patch.object(operator, "module") as modules:
            modules.return_value.validate_analysis_registration.return_value = {
                "recoveryStatus": "pending-wire-evidence",
            }
            with self.assertRaisesRegex(
                    ValueError, "reviewed #1434 terminal recovery registration is not active"):
                operator.canonical_inputs()
            modules.assert_called_once_with("ppw-pilot-adjudicate.py")

    def test_historical_startup_refuses_changed_source_and_synthetic_shape_stays_checked(self):
        registration = load("startup_evidence_validation", BENCH / "ppw-gateway-registration.py")
        historical = json.loads((BENCH / "registrations/ppw-rows-stage1/gateway-evidence/"
                                 "gateway-native-startup-1434-evidence.json").read_text())
        with self.assertRaisesRegex(ValueError, "binds different execution or test source"):
            registration.validate_native_startup(historical)
        # SYNTHETIC in-memory validator control, not a new native observation.
        evidence = copy.deepcopy(historical)
        evidence["sourceHashes"] = {
            name: digest(BENCH / name) for name in registration.STARTUP_SOURCE_FILES
        }
        evidence["testSha256"] = digest(BENCH / "tests/test_ppw_gateway_native_startup.py")
        registration.validate_native_startup(evidence)
        for mutate in (
            lambda value: value.update(outcome="failed"),
            lambda value: value.update(empirical=True),
            lambda value: value["nativeToolExecution"].update(toolResultErrors=1),
            lambda value: value["observer"].update(journal=[]),
            lambda value: value["accounting"].update(sourceBoundAttemptStart=False),
            lambda value: value["accounting"].update(invalidTerminalSlots=1),
            lambda value: value["sourceHashes"].pop("gateway-tools/bash-env.sh"),
            lambda value: value.update(testSha256="0" * 64),
        ):
            changed = copy.deepcopy(evidence)
            mutate(changed)
            with self.assertRaises(ValueError):
                registration.validate_native_startup(changed)

    def test_zero_request_proof_and_apply_preserve_history_then_allow_exact_new_start(self):
        before = self.ledger.read_bytes()
        proof = self.prove()
        self.assertEqual(before, self.ledger.read_bytes())
        self.assertFalse(self.backup.exists())
        self.assertEqual(0, proof["requestCount"])
        self.assertEqual(1, proof["clientInvocations"])
        self.assertEqual([self.old["plannedSlots"][0]], proof["preservedAttemptedSlots"])
        self.assertEqual(443, proof["remainingSlotCount"])
        self.assertEqual(["initialized", "started", "stopped"], proof["eventKinds"])
        self.assertNotIn("SYNTHETIC-owner-secret", canonical(proof))
        self.assertEqual(proof, recovery.inspect_zero_request_recovery(**self.arguments()))
        applied = self.apply(proof)
        self.assertTrue(applied["applied"])
        self.assertEqual(before, self.backup.read_bytes())
        self.assertEqual(0o600, stat.S_IMODE(self.backup.stat().st_mode))
        with self.connect() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
            self.assertEqual(canonical(self.target), scope["binding"])
            self.assertEqual("ready", scope["state"])
            self.assertIsNone(scope["owner"])
            self.assertEqual(["initialized", "started", "stopped",
                              recovery.RECOVERY_EVENT], [event["kind"] for event in events])
            self.assertNotIn("SYNTHETIC-owner-secret", events[-1]["detail"])

        ledger = budget.RequestLedger(self.ledger)
        ledger.initialize(self.target, recovery.PILOT_CEILING_MICRO_USD)
        ledger.start()
        recovered_initial = ledger.snapshot()
        failed = json.loads((self.archive / "collection-outcome.json").read_text())["spending"]
        result = recovery.validate_recovered_initial_archive(
            failed, recovered_initial, expected_old_binding=self.old,
            target_binding=self.target, expected_ledger_sha256=self.ledger_sha256,
            expected_archive_inventory_sha256=self.archive_sha256,
            recovery_registration_sha256=self.recovery_registration_sha256,
            preserved_attempted_slots=[self.old["plannedSlots"][0]])
        self.assertEqual(3, result["preservedEventCount"])
        self.assertEqual(5, result["newStartEventId"])
        self.assertEqual(443, result["remainingSlotCount"])

    def test_recovered_completion_preserves_first_attempt_and_finishes_only_remaining_slots(self):
        proof = self.prove()
        self.apply(proof)
        ledger = budget.RequestLedger(self.ledger)
        owner = ledger.start()
        failed = json.loads((self.archive / "collection-outcome.json").read_text())["spending"]
        remaining = self.target["plannedSlots"][1:]
        with self.connect() as db:
            for index, slot in enumerate(remaining):
                request_id = "%048x" % (index + 1)
                db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                    request_id, slot, "{}", 1, 0, "reconciled", None, "{}"))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                           ("reserved-before-upstream", request_id, canonical({
                               "maximumMicroUsd": 1,
                           })))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                           ("complete-provider-usage", request_id, canonical({
                               "conservativeChargeMicroUsd": 0,
                               "releasedMicroUsd": 1,
                           })))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                           ("slot-complete", None, canonical({
                               "slot": slot, "clientExitCode": 0, "isolation": isolation(),
                               "attempt": attempt(slot, self.target),
                           })))
        result = recovery.complete_recovered_scope(
            self.ledger, owner, failed, expected_old_binding=self.old,
            target_binding=self.target, expected_ledger_sha256=self.ledger_sha256,
            expected_archive_inventory_sha256=self.archive_sha256,
            recovery_registration_sha256=self.recovery_registration_sha256,
            preserved_attempted_slots=[self.old["plannedSlots"][0]])
        self.assertEqual(443, result["continuedSlots"])
        with self.connect() as db:
            scope = db.execute("SELECT state FROM scope").fetchone()
            final = db.execute("SELECT kind,detail FROM events ORDER BY id DESC LIMIT 1").fetchone()
        self.assertEqual("complete", scope["state"])
        self.assertEqual("collection-complete", final["kind"])
        self.assertEqual({
            "preservedAttemptedSlots": [self.old["plannedSlots"][0]],
            "accountedSlots": 444,
            "validCompletedSlots": 443,
            "invalidTerminalSlots": 1,
        }, json.loads(final["detail"]))

    def test_recovered_completion_refuses_retry_of_preserved_attempt(self):
        proof = self.prove()
        self.apply(proof)
        ledger = budget.RequestLedger(self.ledger)
        owner = ledger.start()
        failed = json.loads((self.archive / "collection-outcome.json").read_text())["spending"]
        with self.connect() as db:
            db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                "1" * 48, self.target["plannedSlots"][0], "{}", 1, 0, "reconciled", None, "{}"))
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "retried|slot sequence"):
            recovery.complete_recovered_scope(
                self.ledger, owner, failed, expected_old_binding=self.old,
                target_binding=self.target, expected_ledger_sha256=self.ledger_sha256,
                expected_archive_inventory_sha256=self.archive_sha256,
                recovery_registration_sha256=self.recovery_registration_sha256,
                preserved_attempted_slots=[self.old["plannedSlots"][0]])

    def test_recovered_completion_counts_zero_and_reconciled_terminal_invalids(self):
        proof = self.prove()
        self.apply(proof)
        ledger = budget.RequestLedger(self.ledger)
        owner = ledger.start()
        failed = json.loads((self.archive / "collection-outcome.json").read_text())["spending"]
        remaining = self.target["plannedSlots"][1:]
        with self.connect() as db:
            for index, slot in enumerate(remaining):
                request_id = "%048x" % (index + 1)
                request_bearing = index != 0
                if request_bearing:
                    db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                        request_id, slot, "{}", 1, 0, "reconciled", None, "{}"))
                    db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                               ("reserved-before-upstream", request_id, canonical({
                                   "maximumMicroUsd": 1,
                               })))
                    db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                               ("complete-provider-usage", request_id, canonical({
                                   "conservativeChargeMicroUsd": 0,
                                   "releasedMicroUsd": 1,
                               })))
                if index < 2:
                    terminal = {
                        "schemaVersion": 1,
                        "kind": recovery.TERMINAL_INVALID_KIND,
                        "slot": slot,
                        "attemptNumber": 1,
                        "actualClientInvocation": True,
                        "clientExitCode": 0,
                        "classification": "MISSING_TRANSCRIPT",
                        "invalidReason": next(
                            reason for reason, code in budget.TERMINAL_REASON_CODES.items()
                            if code == "MISSING_TRANSCRIPT"),
                        "attemptStartSha256": "1" * 64,
                        "clientInvocationSha256": "2" * 64,
                        "invalidReasonSha256": "3" * 64,
                        "reasonEvidence": {
                            "agent.json": "6" * 64,
                            "transcript.jsonl": None,
                            "journal.jsonl": None,
                        },
                        "resultProjectionSha256": "4" * 64,
                        "sealedSource": {
                            "inventorySha256": "5" * 64,
                            "fileCount": 1,
                            "byteCount": 1,
                        },
                        "producerSourceSha256": {
                            name: self.target["harnessArtifacts"][name]
                            for name in recovery.TERMINAL_SOURCE_FILES
                        },
                    }
                    db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                               (recovery.TERMINAL_INVALID_EVENT, None, canonical({
                                   "slot": slot, "clientExitCode": 0,
                                   "isolation": isolation(), "terminal": terminal,
                               })))
                else:
                    db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                               ("slot-complete", None, canonical({
                                   "slot": slot, "clientExitCode": 0, "isolation": isolation(),
                                   "attempt": attempt(slot, self.target),
                               })))
        result = recovery.complete_recovered_scope(
            self.ledger, owner, failed, expected_old_binding=self.old,
            target_binding=self.target, expected_ledger_sha256=self.ledger_sha256,
            expected_archive_inventory_sha256=self.archive_sha256,
            recovery_registration_sha256=self.recovery_registration_sha256,
            preserved_attempted_slots=[self.old["plannedSlots"][0]])
        self.assertEqual((441, 3), (
            result["validCompletedSlots"], result["invalidTerminalSlots"]))

    def test_every_request_row_refuses_even_zero_cost_reconciled_rows(self):
        rows = (
            ("reserved", 17, 17, None),
            ("unknown", 17, 17, "unreconciled-provider-charge"),
            ("reconciled", 17, 0, None),
        )
        for index, (state, reserved, charge, reason) in enumerate(rows):
            with self.subTest(state=state):
                if index:
                    with self.connect() as db:
                        db.execute("DELETE FROM requests")
                with self.connect() as db:
                    db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                        "request-" + state, "task-a/calor-permissive/1", "{}",
                        reserved, charge, state, reason, "{}"))
                self.refresh_evidence()
                with self.assertRaisesRegex(recovery.RecoveryRefusal, "any request row"):
                    self.prove()

    def test_reserved_liability_unknown_and_completed_event_evidence_refuse(self):
        for kind, detail in (
                ("reserved-before-upstream", {"maximumMicroUsd": 1}),
                ("unknown-charge-retained", {}),
                ("slot-complete", {"slot": self.old["plannedSlots"][0]})):
            with self.subTest(kind=kind):
                with self.connect() as db:
                    db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                               (kind, "SYNTHETIC-request", canonical(detail)))
                self.refresh_evidence()
                with self.assertRaisesRegex(recovery.RecoveryRefusal, "exactly 3 events"):
                    self.prove()
                with self.connect() as db:
                    db.execute("DELETE FROM events WHERE id=4")
                    db.execute("DELETE FROM sqlite_sequence WHERE name='events'")
                    db.execute("UPDATE sqlite_sequence SET seq=3 WHERE name='events'")

    def test_client_invocation_or_final_accounting_refuses_as_completed_evidence(self):
        invocation = self.archive / "runs/task-a/calor-permissive/run-1/client-invocation.json"
        for value in ({"exitCode": 0}, {"exitCode": 124}, {"exitCode": 1, "extra": True}):
            with self.subTest(value=value):
                save(invocation, value)
                self.refresh_evidence()
                with self.assertRaisesRegex(recovery.RecoveryRefusal, "client invocation"):
                    self.prove()
        save(invocation, {"exitCode": 1})
        final = self.archive / "spending-final.json"
        save(final, {"state": "complete"})
        self.refresh_evidence()
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "final accounting"):
            self.prove()

    def test_changed_binding_ceiling_ledger_hash_and_archive_refuse(self):
        changed = copy.deepcopy(self.old)
        changed["epochId"] = "changed-old"
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "old ledger binding changed"):
            self.prove(expected_old_binding=changed)
        with self.connect() as db:
            db.execute("UPDATE scope SET ceiling=999999999")
        self.refresh_evidence()
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "ceiling changed"):
            self.prove()

        with self.connect() as db:
            db.execute("UPDATE scope SET ceiling=?", (recovery.PILOT_CEILING_MICRO_USD,))
        self.refresh_evidence()
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "source hash changed"):
            self.prove(expected_ledger_sha256="0" * 64)
        (self.archive / "opaque.bin").write_bytes(b"SYNTHETIC archive drift")
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "inventory changed"):
            self.prove()

    def test_changed_archived_source_map_refuses_even_when_rehashed(self):
        pins_path = self.archive / "pins.json"
        pins = json.loads(pins_path.read_text())
        pins["harnessArtifacts"]["ppw-gateway-budget.py"] = "0" * 64
        save(pins_path, pins)
        self.refresh_evidence()
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "harness binding differs"):
            self.prove()

    def test_journal_symlink_hardlink_alias_and_traversal_paths_refuse(self):
        for suffix in ("-journal", "-wal", "-shm"):
            with self.subTest(suffix=suffix):
                sidecar = Path(str(self.ledger) + suffix)
                sidecar.write_bytes(b"SYNTHETIC")
                with self.assertRaisesRegex(recovery.RecoveryRefusal, "journal or WAL"):
                    self.prove()
                sidecar.unlink()

        linked_ledger = self.protected / "linked.sqlite3"
        linked_ledger.symlink_to(self.ledger)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "symlink"):
            self.prove(ledger_path=linked_ledger)
        linked_ledger.unlink()

        hardlink = self.protected / "hardlink.sqlite3"
        os.link(self.ledger, hardlink)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "hard-linked"):
            self.prove(ledger_path=hardlink)
        hardlink.unlink()

        with self.assertRaisesRegex(recovery.RecoveryRefusal, "aliases"):
            self.prove(backup_path=self.ledger)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "direct child"):
            self.prove(backup_path=self.root / "outside.sqlite3")
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "canonical"):
            self.prove(backup_path=self.protected / ".." / "escape.sqlite3")

    def test_archive_links_and_backup_links_refuse(self):
        opaque = self.archive / "opaque-link"
        opaque.symlink_to(self.archive / "pins.json")
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "linked file"):
            recovery.archive_inventory(self.archive)
        opaque.unlink()

        hardlink = self.archive / "pins-hardlink.json"
        os.link(self.archive / "pins.json", hardlink)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "hard-linked"):
            recovery.archive_inventory(self.archive)
        hardlink.unlink()

        self.backup.symlink_to(self.ledger)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "symlink"):
            self.prove()

    def test_repeated_apply_refuses_and_does_not_add_another_event(self):
        proof = self.prove()
        self.apply(proof)
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "state changed|binding changed"):
            self.apply(proof)
        with self.connect() as db:
            self.assertEqual(4, db.execute("SELECT COUNT(*) FROM events").fetchone()[0])

    def test_concurrent_writer_refuses_without_creating_backup(self):
        proof = self.prove()
        blocker = sqlite3.connect(self.ledger, isolation_level=None)
        try:
            blocker.execute("BEGIN IMMEDIATE")
            with self.assertRaisesRegex(recovery.RecoveryRefusal, "concurrent ledger writer"):
                self.apply(proof)
        finally:
            blocker.execute("ROLLBACK")
            blocker.close()
        self.assertFalse(self.backup.exists())

    def test_matching_backup_resumes_after_process_loss(self):
        shutil.copyfile(self.ledger, self.backup)
        self.backup.chmod(0o600)
        proof = self.prove()
        self.apply(proof)
        self.assertEqual(self.ledger_sha256, digest(self.backup))

    def test_failure_after_backup_rolls_back_and_preserves_backup(self):
        proof = self.prove()
        original_inventory = recovery.archive_inventory
        calls = 0

        def changed_second_inventory(path):
            nonlocal calls
            calls += 1
            value = original_inventory(path)
            if calls == 2:
                value = dict(value, sha256="0" * 64)
            return value

        with patch.object(recovery, "archive_inventory", side_effect=changed_second_inventory):
            with self.assertRaisesRegex(recovery.RecoveryRefusal, "changed before recovery"):
                self.apply(proof)
        self.assertTrue(self.backup.exists())
        self.assertEqual(self.ledger_sha256, digest(self.backup))
        with self.connect() as db:
            scope = db.execute("SELECT state,binding,owner FROM scope").fetchone()
            self.assertEqual(recovery.FAILED_STATE, scope["state"])
            self.assertEqual(canonical(self.old), scope["binding"])
            self.assertEqual("SYNTHETIC-owner-secret", scope["owner"])
            self.assertEqual(3, db.execute("SELECT COUNT(*) FROM events").fetchone()[0])

    def test_archive_validator_rejects_any_old_prefix_rewrite(self):
        proof = self.prove()
        self.apply(proof)
        ledger = budget.RequestLedger(self.ledger)
        ledger.start()
        recovered = ledger.snapshot()
        failed = json.loads((self.archive / "collection-outcome.json").read_text())["spending"]
        recovered["events"][0]["created"] = "changed"
        with self.assertRaisesRegex(recovery.RecoveryRefusal, "audit prefix"):
            recovery.validate_recovered_initial_archive(
                failed, recovered, expected_old_binding=self.old,
                target_binding=self.target, expected_ledger_sha256=self.ledger_sha256,
                expected_archive_inventory_sha256=self.archive_sha256,
                recovery_registration_sha256=self.recovery_registration_sha256,
                preserved_attempted_slots=[self.old["plannedSlots"][0]])


if __name__ == "__main__":
    unittest.main()
