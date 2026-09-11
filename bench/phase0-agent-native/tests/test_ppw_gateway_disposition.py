"""SYNTHETIC-only tests for the retained historical-liability disposition core."""
import copy
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import stat
import unittest
import uuid


BENCH = Path(__file__).resolve().parents[1]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, BENCH / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


budget = load("ppw_disposition_budget_test", "ppw-gateway-budget.py")
disposition = load("ppw_disposition_test", "ppw-gateway-disposition.py")


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class DispositionFixture(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parent / (
            ".gateway-disposition-SYNTHETIC-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.protected = self.root / "protected"
        self.protected.mkdir(mode=0o700)
        self.ledger_path = self.protected / "current.sqlite3"
        self.old_backup = self.protected / "immutable-old-backup.sqlite3"
        self.new_backup = self.protected / "disposition-source.sqlite3"
        self.original_archive = self.root / "original-epoch-001"
        self.failed_archive = self.root / "failed-epoch-002"
        self.original_archive.mkdir()
        self.failed_archive.mkdir()
        self.slots = [
            "task-%03d/%s/%d" % (task, arm, run)
            for run in range(1, 75)
            for task in range(1, 4)
            for arm in ("calor-permissive", "calor-strict")
        ]
        self.old_binding = {
            "stage": "pilot",
            "epochId": "SYNTHETIC-epoch-002",
            "priceSha256": disposition.HISTORICAL_PRICE_SHA256,
            "authorizationSha256": "1" * 64,
            "protocolSha256": "2" * 64,
            "planSha256": "3" * 64,
            "harnessArtifacts": {"ppw-gateway-budget.py": "4" * 64},
            "plannedSlots": self.slots,
        }
        self.request_ids = ("a" * 48, "b" * 48)
        self.create_archives()
        self.create_ledger()
        self.old_backup.write_bytes(b"SYNTHETIC immutable predecessor backup\n")
        self.old_backup.chmod(0o600)
        self.authorization = self.make_authorization()
        self.authorization_sha256 = hashlib.sha256(
            disposition.authorization_bytes(self.authorization)).hexdigest()
        self.target_binding = {
            "stage": "pilot",
            "epochId": self.authorization["targetEpochId"],
            "priceSha256": self.authorization["targetPriceSha256"],
            "authorizationSha256": self.authorization_sha256,
            "protocolSha256": "e" * 64,
            "planSha256": "f" * 64,
            "harnessArtifacts": self.authorization["targetHarnessArtifacts"],
            "plannedSlots": self.slots,
        }

    def connect(self, path=None, timeout=5):
        connection = sqlite3.connect(path or self.ledger_path, timeout=timeout)
        connection.row_factory = sqlite3.Row
        return connection

    def historical_request(self):
        return canonical({
            "model": budget.MODEL,
            "maxTokens": 64_000,
            "maximumMicroUsd": disposition.HISTORICAL_REQUEST_RESERVATION,
            "priceSha256": disposition.HISTORICAL_PRICE_SHA256,
            "serviceTier": "auto",
        })

    def create_ledger(self):
        with self.connect() as db:
            db.execute("CREATE TABLE scope "
                       "(id INTEGER PRIMARY KEY CHECK(id=1),binding TEXT,ceiling INTEGER,state TEXT,"
                       "owner TEXT)")
            db.execute("CREATE TABLE requests "
                       "(id TEXT PRIMARY KEY,slot TEXT,request TEXT,reserved INTEGER,charge INTEGER,"
                       "state TEXT,reason TEXT,usage TEXT)")
            db.execute("CREATE TABLE events "
                       "(id INTEGER PRIMARY KEY AUTOINCREMENT,kind TEXT,request_id TEXT,detail TEXT,"
                       "created TEXT DEFAULT(strftime('%Y-%m-%dT%H:%M:%fZ','now')))")
            db.execute("INSERT INTO scope VALUES(1,?,?,?,?)", (
                canonical(self.old_binding), disposition.PILOT_CEILING_MICRO_USD,
                "INCOMPLETE_UNKNOWN_CHARGE", "SYNTHETIC-old-owner"))
            for request_id in self.request_ids:
                db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                    request_id, self.slots[1], self.historical_request(),
                    disposition.HISTORICAL_REQUEST_RESERVATION,
                    disposition.HISTORICAL_REQUEST_RESERVATION, "unknown",
                    "unreconciled-provider-charge", None))
            old_events = (
                ("initialized", None, {"ceilingMicroUsd": disposition.PILOT_CEILING_MICRO_USD}),
                ("started", None, {}),
                ("stopped", None, {"reason": "INCOMPLETE_POLICY"}),
                ("zero-request-recovery", None, {
                    "preservedAttemptedSlots": [self.slots[0]],
                    "SYNTHETIC": True,
                }),
                ("started", None, {}),
                ("reserved-before-upstream", self.request_ids[0], {
                    "maximumMicroUsd": disposition.HISTORICAL_REQUEST_RESERVATION}),
                ("reserved-before-upstream", self.request_ids[1], {
                    "maximumMicroUsd": disposition.HISTORICAL_REQUEST_RESERVATION}),
                ("unknown-charge-retained", self.request_ids[0], {}),
                ("unknown-charge-retained", self.request_ids[1], {}),
            )
            for kind, request_id, detail in old_events:
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                           (kind, request_id, canonical(detail)))
        self.ledger_path.chmod(0o600)

    def create_archives(self):
        attempts = (
            (self.original_archive, "first"),
            (self.failed_archive, "second"),
        )
        self.attempt_paths = []
        for archive, prefix in attempts:
            paths = {
                "rawRecordPath": "evidence/%s-result.json" % prefix,
                "clientInvocationPath": "evidence/%s-client.json" % prefix,
                "invalidReasonPath": "evidence/%s-invalid.txt" % prefix,
                "attemptStartPath": "evidence/%s-attempt.json" % prefix,
                "sourcePath": "sources/%s-source.py" % prefix,
            }
            for name, relative in paths.items():
                path = archive / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                content = "SYNTHETIC opaque %s %s\n" % (prefix, name)
                if prefix == "second" and name == "invalidReasonPath":
                    content = "SYNTHETIC actual API marker is raw invalid and censored\n"
                path.write_bytes(content.encode())
            (archive / "pins.json").write_text(json.dumps({
                "epochId": "SYNTHETIC-epoch-%03d" % (len(self.attempt_paths) + 1),
                "stage": "pilot",
                "harnessArtifacts": {
                    Path(paths["sourcePath"]).name: digest(archive / paths["sourcePath"]),
                },
            }) + "\n", encoding="utf-8")
            self.attempt_paths.append(paths)

    def old_snapshot(self):
        with self.connect() as db:
            requests = [dict(row) for row in db.execute(
                "SELECT id,slot,request,reserved,charge,state,reason,usage FROM requests ORDER BY rowid")]
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        return {
            "kind": budget.KIND,
            "state": "INCOMPLETE_UNKNOWN_CHARGE",
            "binding": self.old_binding,
            "ceilingMicroUsd": disposition.PILOT_CEILING_MICRO_USD,
            "exposureMicroUsd": disposition.PERMANENTLY_RETAINED_MICRO_USD,
            "requests": requests,
            "events": events,
            "verdict": None,
            "basis": "request reservations; only validated complete provider usage permits release",
            "accountedSlots": 1,
            "validCompletedSlots": 0,
            "invalidTerminalSlots": 1,
            "requestBearingSlots": 1,
        }

    def attempt(self, index, inventory):
        archive = (self.original_archive, self.failed_archive)[index]
        paths = self.attempt_paths[index]
        return {
            "slot": self.slots[index],
            "epochId": "SYNTHETIC-epoch-%03d" % (index + 1),
            "classification": disposition.ATTEMPT_CLASSIFICATIONS[index],
            "archiveRole": disposition.ARCHIVE_ROLES[index],
            "rawRecordPath": paths["rawRecordPath"],
            "rawRecordSha256": digest(archive / paths["rawRecordPath"]),
            "clientInvocationPath": paths["clientInvocationPath"],
            "clientInvocationSha256": digest(archive / paths["clientInvocationPath"]),
            "invalidReasonPath": paths["invalidReasonPath"],
            "invalidReasonSha256": digest(archive / paths["invalidReasonPath"]),
            "attemptStartPath": paths["attemptStartPath"],
            "attemptStartSha256": digest(archive / paths["attemptStartPath"]),
            "sourceHashes": {
                paths["sourcePath"]: digest(archive / paths["sourcePath"]),
            },
        }

    def make_authorization(self):
        archives = []
        attempts = []
        for index, path in enumerate((self.original_archive, self.failed_archive)):
            inventory = disposition.archive_inventory(path)
            archives.append({
                "archiveRole": disposition.ARCHIVE_ROLES[index],
                "epochId": "SYNTHETIC-epoch-%03d" % (index + 1),
                "pathSha256": hashlib.sha256(str(path).encode()).hexdigest(),
                "inventorySha256": inventory["sha256"],
                "fileCount": inventory["fileCount"],
                "byteCount": inventory["byteCount"],
                "pinsPath": "pins.json",
                "pinsSha256": digest(path / "pins.json"),
            })
            attempts.append(self.attempt(index, inventory))
        target_sources = {
            name: digest(BENCH / name) for name in disposition.TRUSTED_LAUNCHER_SOURCES
        }
        return {
            "schemaVersion": 1,
            "kind": disposition.DISPOSITION_KIND,
            "evidenceMode": "SYNTHETIC_TEST_ONLY",
            "grant": {
                "quote": disposition.GRANT_QUOTE,
                "authorizedAt": disposition.GRANT_TIME,
                "reference": "SYNTHETIC://issue-1436-low-level-fixture",
            },
            "oldSnapshot": self.old_snapshot(),
            "oldLedgerSha256": digest(self.ledger_path),
            "oldBackupSha256": digest(self.old_backup),
            "predecessorArchives": archives,
            "preservedAttempts": attempts,
            "targetEpochId": "SYNTHETIC-epoch-003",
            "targetPriceSha256": budget.price_identity(),
            "targetHarnessArtifacts": target_sources,
            "permanentlyRetainedMicroUsd":
                disposition.PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "futureUnknownPolicy": "halt",
            "quiescenceProcessMarkers": ["SYNTHETIC_NO_ACTIVE_NATIVE_PROCESS_MARKER"],
        }

    def arguments(self, **changes):
        values = {
            "ledger_path": self.ledger_path,
            "authorization": self.authorization,
            "authorization_sha256": self.authorization_sha256,
            "target_binding": self.target_binding,
            "original_archive": self.original_archive,
            "failed_archive": self.failed_archive,
            "old_backup_path": self.old_backup,
            "new_backup_path": self.new_backup,
            "protected_root": self.protected,
        }
        values.update(changes)
        return values

    def inspect(self, **changes):
        return disposition.inspect_disposition(**self.arguments(**changes))

    def apply(self, proof=None, **changes):
        proof = proof or self.inspect(**changes)
        return disposition.apply_disposition(
            **self.arguments(**changes), confirmed_proof_sha256=proof["proofSha256"])

    def disposition_snapshot(self):
        return budget.RequestLedger(self.ledger_path).snapshot()

    def replay(self, snapshot):
        return disposition.validate_history(
            snapshot, authorization=self.authorization,
            authorization_sha256=self.authorization_sha256,
            target_binding=self.target_binding)

    def assert_rehashed_proof_refused(self, snapshot, mutate):
        changed = copy.deepcopy(snapshot)
        detail = json.loads(changed["events"][9]["detail"])
        mutate(detail["proof"])
        detail["proof"].pop("proofSha256", None)
        detail["proof"]["proofSha256"] = disposition._sha256_json(detail["proof"])
        changed["events"][9]["detail"] = canonical(detail)
        with self.assertRaises(disposition.DispositionRefusal):
            self.replay(changed)

    def future_row(self, *, max_tokens=0, stream=False, service_tier="auto", betas=()):
        request = budget.admit_request(canonical({
            "model": budget.MODEL,
            "max_tokens": max_tokens,
            "stream": stream,
            "service_tier": service_tier,
            "messages": [{"role": "user", "content": "SYNTHETIC_NO_INFERENCE"}],
        }), ",".join(betas) or None)
        cost, receipt = budget.reconciled_cost(request, budget.MODEL, {
            "input_tokens": 1,
            "output_tokens": 0,
            "cache_creation_input_tokens": 0,
            "cache_read_input_tokens": 0,
            "service_tier": "standard",
            "inference_geo": "not_available",
        }, "end_turn")
        return {
            "id": "c" * 48, "slot": self.slots[2], "request": canonical(request),
            "reserved": request["maximumMicroUsd"], "charge": cost,
            "state": "reconciled", "reason": None, "usage": canonical(receipt),
        }

    def append_all_future_slots(self):
        request = budget.admit_request(json.dumps({
            "model": budget.MODEL,
            "max_tokens": 0,
            "messages": [{"role": "user", "content": "SYNTHETIC"}],
        }).encode())
        usage = {
            "input_tokens": 1,
            "output_tokens": 0,
            "cache_creation_input_tokens": 0,
            "cache_read_input_tokens": 0,
            "service_tier": "standard",
            "inference_geo": "not_available",
        }
        cost, receipt = budget.reconciled_cost(
            request, budget.MODEL, usage, "end_turn")
        with self.connect() as db:
            for index, slot in enumerate(self.slots[2:]):
                request_id = "%048x" % (index + 1)
                db.execute("INSERT INTO requests VALUES(?,?,?,?,?,?,?,?)", (
                    request_id, slot, canonical(request), request["maximumMicroUsd"],
                    cost, "reconciled", None, canonical(receipt)))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)", (
                    "reserved-before-upstream", request_id,
                    canonical({"maximumMicroUsd": request["maximumMicroUsd"]})))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)", (
                    "complete-provider-usage", request_id, canonical({
                        "conservativeChargeMicroUsd": cost,
                        "releasedMicroUsd": request["maximumMicroUsd"] - cost,
                    })))
                db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)", (
                    "slot-complete", None, canonical({"slot": slot})))


class DispositionCoreTests(DispositionFixture):
    def test_contract_inspect_apply_preserves_old_bytes_and_appends_only_event_10(self):
        before = self.ledger_path.read_bytes()
        old_snapshot = copy.deepcopy(self.authorization["oldSnapshot"])
        proof = self.inspect()
        self.assertEqual(before, self.ledger_path.read_bytes())
        self.assertEqual(442, proof["remainingSlotCount"])
        self.assertEqual(disposition.PERMANENTLY_RETAINED_MICRO_USD,
                         proof["permanentlyRetainedMicroUsd"])
        self.assertIsNone(proof["actualCost"])
        self.assertEqual([], proof["quiescence"]["matchedProcesses"])
        applied = self.apply(proof)
        self.assertTrue(applied["applied"])
        self.assertEqual(before, self.new_backup.read_bytes())
        self.assertEqual(0o600, stat.S_IMODE(self.new_backup.stat().st_mode))
        self.assertEqual(self.authorization["oldBackupSha256"], digest(self.old_backup))
        with self.connect() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            requests = [dict(row) for row in db.execute("SELECT * FROM requests ORDER BY rowid")]
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        self.assertEqual(canonical(self.target_binding), scope["binding"])
        self.assertEqual("ready", scope["state"])
        self.assertIsNone(scope["owner"])
        self.assertEqual(old_snapshot["requests"], requests)
        self.assertEqual(old_snapshot["events"], events[:9])
        self.assertEqual(disposition.DISPOSITION_EVENT, events[9]["kind"])

    def test_full_accounting_keeps_51_04_unknown_and_completes_only_442_new_slots(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        self.append_all_future_slots()
        result = ledger.complete(owner)
        self.assertEqual("complete", result["state"])
        snapshot = ledger.snapshot()
        self.assertEqual(444, snapshot["accountedSlots"])
        self.assertEqual(442, snapshot["validCompletedSlots"])
        self.assertEqual(1, snapshot["invalidTerminalSlots"])
        self.assertEqual(disposition.PERMANENTLY_RETAINED_MICRO_USD,
                         snapshot["permanentUnknownMicroUsd"])
        self.assertIsNone(snapshot["actualCost"])
        self.assertEqual(2, snapshot["historicalUnknownRequestCount"])
        self.assertEqual(0, snapshot["liveUnknownRequestCount"])
        self.assertEqual(0, snapshot["remainingNonterminalSlots"])
        replay = disposition.validate_history(
            snapshot, authorization=self.authorization,
            authorization_sha256=self.authorization_sha256,
            target_binding=self.target_binding)
        self.assertEqual(444, replay["accountedSlots"])
        self.assertGreaterEqual(snapshot["exposureMicroUsd"],
                                disposition.PERMANENTLY_RETAINED_MICRO_USD)
        with self.connect() as db:
            detail = json.loads(db.execute(
                "SELECT detail FROM events WHERE kind='collection-complete'").fetchone()[0])
        self.assertEqual(2, detail["historicalPreservedSlots"])
        self.assertEqual(442, detail["continuedSlots"])
        self.assertEqual(disposition.PERMANENTLY_RETAINED_MICRO_USD,
                         detail["permanentUnknownMicroUsd"])
        self.assertIsNone(detail["actualCost"])

    def test_historical_rows_cannot_be_reconciled_refunded_or_retried(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        with self.assertRaises(budget.Refusal):
            ledger.settle(owner, self.request_ids[0], cost=0, usage={})
        request = budget.admit_request(json.dumps({
            "model": budget.MODEL, "max_tokens": 0,
            "messages": [{"role": "user", "content": "SYNTHETIC"}],
        }).encode())
        with self.assertRaisesRegex(budget.Refusal, "duplicate|replaced|order"):
            ledger.reserve(owner, self.slots[0], request)
        snapshot = ledger.snapshot()
        self.assertEqual(self.authorization["oldSnapshot"]["requests"], snapshot["requests"][:2])
        self.assertEqual(disposition.PERMANENTLY_RETAINED_MICRO_USD,
                         sum(row["charge"] for row in snapshot["requests"][:2]))

    def test_future_unknown_halts_even_with_verified_historical_exception(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        request = budget.admit_request(json.dumps({
            "model": budget.MODEL, "max_tokens": 0,
            "messages": [{"role": "user", "content": "SYNTHETIC"}],
        }).encode())
        request_id = ledger.reserve(owner, self.slots[2], request)
        ledger.settle(owner, request_id)
        snapshot = ledger.snapshot()
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", snapshot["state"])
        self.assertEqual(2, snapshot["historicalUnknownRequestCount"])
        self.assertEqual(1, snapshot["liveUnknownRequestCount"])
        self.assertEqual(disposition.PERMANENTLY_RETAINED_MICRO_USD,
                         snapshot["permanentUnknownMicroUsd"])
        with self.assertRaises(budget.Refusal):
            ledger.reserve(owner, self.slots[2], request)
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.complete_scope(
                self.ledger_path, owner, authorization=self.authorization,
                authorization_sha256=self.authorization_sha256,
                target_binding=self.target_binding)

    def test_inflight_completion_and_concurrent_start_fail_closed(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: self._try_start(ledger), range(2)))
        self.assertEqual(1, sum(result is not None for result in results))
        owner = next(result for result in results if result is not None)
        request = budget.admit_request(json.dumps({
            "model": budget.MODEL, "max_tokens": 0,
            "messages": [{"role": "user", "content": "SYNTHETIC"}],
        }).encode())
        ledger.reserve(owner, self.slots[2], request)
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.complete_scope(
                self.ledger_path, owner, authorization=self.authorization,
                authorization_sha256=self.authorization_sha256,
                target_binding=self.target_binding)
        self.assertEqual("collecting", ledger.snapshot()["state"])

    @staticmethod
    def _try_start(ledger):
        try:
            return ledger.start()
        except (budget.Refusal, sqlite3.OperationalError):
            return None

    def test_wrong_identity_evidence_paths_extra_attempt_and_backup_change_refuse(self):
        mutations = (
            ("request id", lambda value: value["oldSnapshot"]["requests"][0].update(id="x" * 48)),
            ("price", lambda value: value["oldSnapshot"]["binding"].update(priceSha256="0" * 64)),
            ("source", lambda value: value["preservedAttempts"][0]["sourceHashes"].update(
                {self.attempt_paths[0]["sourcePath"]: "0" * 64})),
            ("archive", lambda value: value["predecessorArchives"][0].update(
                inventorySha256="0" * 64)),
        )
        for name, mutate in mutations:
            with self.subTest(name=name):
                changed = copy.deepcopy(self.authorization)
                mutate(changed)
                with self.assertRaises(disposition.DispositionRefusal):
                    self.inspect(authorization=changed)
        extra = copy.deepcopy(self.authorization)
        extra["preservedAttempts"].append(copy.deepcopy(extra["preservedAttempts"][1]))
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.validate_authorization(extra)
        alias = self.root / "alias"
        alias.symlink_to(self.ledger_path)
        with self.assertRaises(disposition.DispositionRefusal):
            self.inspect(ledger_path=alias)
        self.old_backup.write_bytes(b"changed")
        with self.assertRaises(disposition.DispositionRefusal):
            self.inspect()

    def test_malformed_proof_reuse_and_malformed_live_event_halt(self):
        proof = self.inspect()
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.apply_disposition(
                **self.arguments(), confirmed_proof_sha256="0" * 64)
        self.assertFalse(self.new_backup.exists())
        self.apply(proof)
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.apply_disposition(
                **self.arguments(), confirmed_proof_sha256=proof["proofSha256"])
        with self.connect() as db:
            event = db.execute(
                "SELECT detail FROM events WHERE kind=?", (disposition.DISPOSITION_EVENT,)
            ).fetchone()
            detail = json.loads(event["detail"])
            detail["proof"]["remainingSlotCount"] = 441
            db.execute("UPDATE events SET detail=? WHERE kind=?",
                       (canonical(detail), disposition.DISPOSITION_EVENT))
        ledger = budget.RequestLedger(self.ledger_path)
        with self.assertRaises(budget.Refusal):
            ledger.snapshot()
        with self.connect() as db:
            scope = db.execute("SELECT state FROM scope").fetchone()
            stopped = db.execute(
                "SELECT detail FROM events ORDER BY id DESC LIMIT 1").fetchone()
        self.assertEqual("INCOMPLETE_POLICY", scope["state"])
        self.assertEqual("DISPOSITION_INTEGRITY", json.loads(stopped["detail"])["diagnostic"])

    def test_apply_refuses_concurrent_writer_before_backup_or_mutation(self):
        proof = self.inspect()
        before = self.ledger_path.read_bytes()
        blocker = self.connect(timeout=0)
        blocker.execute("BEGIN IMMEDIATE")
        try:
            with self.assertRaises(disposition.DispositionRefusal):
                disposition.apply_disposition(
                    **self.arguments(), confirmed_proof_sha256=proof["proofSha256"])
            self.assertFalse(self.new_backup.exists())
        finally:
            blocker.execute("ROLLBACK")
            blocker.close()
        self.assertEqual(before, self.ledger_path.read_bytes())

    def test_apply_rechecks_backup_and_archive_after_reviewed_proof(self):
        proof = self.inspect()
        self.old_backup.write_bytes(b"SYNTHETIC changed after review\n")
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.apply_disposition(
                **self.arguments(), confirmed_proof_sha256=proof["proofSha256"])
        self.assertFalse(self.new_backup.exists())
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE",
                         budget.RequestLedger(self.ledger_path).snapshot()["state"])

        self.old_backup.write_bytes(b"SYNTHETIC immutable predecessor backup\n")
        self.authorization["oldBackupSha256"] = digest(self.old_backup)
        proof = self.inspect()
        source = self.original_archive / self.attempt_paths[0]["sourcePath"]
        source.write_bytes(b"SYNTHETIC changed archive after review\n")
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.apply_disposition(
                **self.arguments(), confirmed_proof_sha256=proof["proofSha256"])
        self.assertFalse(self.new_backup.exists())

    def test_authorization_schema_is_strict_and_registered_mode_pins_real_ids(self):
        validated = disposition.validate_authorization(self.authorization)
        self.assertEqual(self.slots[:2], validated["preservedSlots"])
        changed = copy.deepcopy(self.authorization)
        changed["unexpected"] = True
        with self.assertRaises(disposition.DispositionRefusal):
            disposition.validate_authorization(changed)
        registered = copy.deepcopy(self.authorization)
        registered["evidenceMode"] = "registered-operator"
        registered["grant"]["reference"] = disposition.GRANT_REFERENCE
        with self.assertRaisesRegex(disposition.DispositionRefusal, "immutable ledger"):
            disposition.validate_authorization(registered)

    def test_missing_or_renamed_event_10_durably_halts(self):
        self.apply()
        with self.connect() as db:
            db.execute("UPDATE events SET kind='SYNTHETIC-malformed' WHERE id=10")
        ledger = budget.RequestLedger(self.ledger_path)
        with self.assertRaisesRegex(budget.Refusal, "event 10"):
            ledger.start()
        with self.connect() as db:
            state = db.execute("SELECT state FROM scope").fetchone()[0]
            last = json.loads(db.execute(
                "SELECT detail FROM events ORDER BY id DESC LIMIT 1").fetchone()[0])
        self.assertEqual("INCOMPLETE_POLICY", state)
        self.assertEqual("DISPOSITION_INTEGRITY", last["diagnostic"])

    def test_rehashed_proof_cannot_replace_authorized_evidence(self):
        self.apply()
        snapshot = self.disposition_snapshot()
        proof = json.loads(snapshot["events"][9]["detail"])["proof"]
        paths = [(name,) for name in (
            "schemaVersion", "kind", "authorizationSha256", "authorizationCanonicalSha256",
            "oldLedgerSha256", "oldBackupSha256", "oldBindingSha256", "targetBindingSha256",
            "originalArchivePathSha256", "failedArchivePathSha256", "oldEventCount",
            "remainingSlotCount", "permanentlyRetainedMicroUsd", "actualCost",
            "futureUnknownPolicy",
        )]
        for name in ("historicalRequestIds", "sourceEpochIds", "preservedSlots"):
            paths.extend((name, index) for index in range(2))
        for index in range(2):
            paths.extend(("predecessorArchiveInventories", index, name)
                         for name in proof["predecessorArchiveInventories"][index])
            attempt = proof["preservedAttemptEvidence"][index]
            paths.extend(("preservedAttemptEvidence", index, name)
                         for name in ("slot", "epochId", "classification"))
            paths.extend(("preservedAttemptEvidence", index, "artifactHashes", name)
                         for name in attempt["artifactHashes"])
        paths.extend(("trustedLauncherSources", name) for name in proof["trustedLauncherSources"])
        for path in paths:
            with self.subTest(path=path):
                def mutate(value):
                    for key in path[:-1]:
                        value = value[key]
                    old = value[path[-1]]
                    value[path[-1]] = (
                        "0" * 64 if disposition._valid_sha(old)
                        else old + "-changed" if isinstance(old, str)
                        else old + 1 if type(old) is int else 0)
                self.assert_rehashed_proof_refused(snapshot, mutate)
        for name in ("schemaVersion", "oldEventCount", "remainingSlotCount",
                     "permanentlyRetainedMicroUsd"):
            with self.subTest(float_field=name):
                self.assert_rehashed_proof_refused(
                    snapshot, lambda value: value.update({name: float(value[name])}))
        self.assert_rehashed_proof_refused(
            snapshot, lambda value: value.update(schemaVersion=True))
        for name in ("historicalRequestIds", "sourceEpochIds", "preservedSlots",
                     "predecessorArchiveInventories", "preservedAttemptEvidence"):
            for operation in ("reverse", "truncate", "duplicate"):
                with self.subTest(array=name, operation=operation):
                    def mutate(value):
                        items = value[name]
                        value[name] = (items[::-1] if operation == "reverse"
                                       else items[:1] if operation == "truncate"
                                       else items + items[:1])
                    self.assert_rehashed_proof_refused(snapshot, mutate)

    def test_rehashed_proof_schema_rejects_extra_and_missing_fields_at_every_depth(self):
        self.apply()
        snapshot = self.disposition_snapshot()
        proof = json.loads(snapshot["events"][9]["detail"])["proof"]

        def objects(value, path=()):
            if isinstance(value, dict):
                yield path, value
                for key, child in value.items():
                    yield from objects(child, path + (key,))
            elif isinstance(value, list):
                for index, child in enumerate(value):
                    yield from objects(child, path + (index,))

        for path, value in objects(proof):
            for field in [None] + [key for key in value if key != "proofSha256"]:
                with self.subTest(path=path, field=field):
                    def mutate(changed):
                        for key in path:
                            changed = changed[key]
                        if field is None:
                            changed["unexpected"] = None
                        else:
                            del changed[field]
                    self.assert_rehashed_proof_refused(snapshot, mutate)
        for value in (None, [], False, "proof"):
            with self.subTest(proof_type=value):
                changed = copy.deepcopy(snapshot)
                detail = json.loads(changed["events"][9]["detail"])
                detail["proof"] = value
                changed["events"][9]["detail"] = canonical(detail)
                with self.assertRaises(disposition.DispositionRefusal):
                    self.replay(changed)
        for version in (True, 1.0, "1", None):
            with self.subTest(event_schema_version=version):
                changed = copy.deepcopy(snapshot)
                detail = json.loads(changed["events"][9]["detail"])
                detail["schemaVersion"] = version
                changed["events"][9]["detail"] = canonical(detail)
                with self.assertRaises(disposition.DispositionRefusal):
                    self.replay(changed)

    def test_rehashed_proof_requires_exact_empty_current_ps_quiescence(self):
        self.apply()
        snapshot = self.disposition_snapshot()
        for field, invalid in (
                ("kind", "historical-descendants-proven"),
                ("method", "synthetic-no-probe"),
                ("matchedProcesses", None), ("matchedProcesses", {}),
                ("matchedProcesses", False), ("matchedProcesses", 0),
                ("matchedProcesses", ""), ("matchedProcesses", [{"pid": 1}]),
                ("criteriaSha256", "g" * 64), ("criteriaSha256", "A" * 64),
                ("criteriaSha256", None)):
            with self.subTest(field=field, invalid=invalid):
                self.assert_rehashed_proof_refused(
                    snapshot, lambda value: value["quiescence"].update({field: invalid}))

    def test_rehashed_proof_requires_all_path_hash_forms(self):
        self.apply()
        snapshot = self.disposition_snapshot()
        proof = json.loads(snapshot["events"][9]["detail"])["proof"]
        for field in (name for name in proof if name.endswith("PathSha256")):
            for invalid in (None, [], {}, 0, True, "a" * 63, "A" * 64, "g" * 64):
                with self.subTest(field=field, invalid=invalid):
                    self.assert_rehashed_proof_refused(
                        snapshot, lambda value: value.update({field: invalid}))

    def test_rehashed_malformed_proof_durably_halts_ledger_start(self):
        self.apply()
        with self.connect() as db:
            detail = json.loads(db.execute("SELECT detail FROM events WHERE id=10").fetchone()[0])
            detail["proof"]["oldBindingSha256"] = "0" * 64
            detail["proof"].pop("proofSha256")
            detail["proof"]["proofSha256"] = disposition._sha256_json(detail["proof"])
            db.execute("UPDATE events SET detail=? WHERE id=10", (canonical(detail),))
        with self.assertRaisesRegex(budget.Refusal, "proof fields"):
            budget.RequestLedger(self.ledger_path).start()
        with self.connect() as db:
            self.assertEqual("INCOMPLETE_POLICY", db.execute(
                "SELECT state FROM scope").fetchone()[0])
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        self.assertEqual(self.authorization["oldSnapshot"]["events"], events[:9])
        self.assertEqual("DISPOSITION_INTEGRITY", json.loads(events[-1]["detail"])["diagnostic"])

    def test_proof_self_hash_is_still_required_after_semantic_validation(self):
        self.apply()
        snapshot = self.disposition_snapshot()
        for invalid in (None, False, [], "0" * 64, "A" * 64, "a" * 63):
            with self.subTest(hash=invalid):
                changed = copy.deepcopy(snapshot)
                detail = json.loads(changed["events"][9]["detail"])
                if invalid is None:
                    del detail["proof"]["proofSha256"]
                else:
                    detail["proof"]["proofSha256"] = invalid
                changed["events"][9]["detail"] = canonical(detail)
                with self.assertRaisesRegex(disposition.DispositionRefusal, "proof hash"):
                    self.replay(changed)

    def test_absent_historical_attempt_start_is_not_fabricated_in_proof(self):
        self.authorization["preservedAttempts"][0].update(
            attemptStartPath=None, attemptStartSha256=None)
        self.authorization_sha256 = hashlib.sha256(
            disposition.authorization_bytes(self.authorization)).hexdigest()
        self.target_binding["authorizationSha256"] = self.authorization_sha256
        self.apply()
        snapshot = self.disposition_snapshot()
        proof = json.loads(snapshot["events"][9]["detail"])["proof"]
        self.assertNotIn("attemptStartPath",
                         proof["preservedAttemptEvidence"][0]["artifactHashes"])
        self.assertEqual(442, self.replay(snapshot)["remainingNonterminalSlots"])
        for fabricated in (None, "0" * 64):
            with self.subTest(attempt_start=fabricated):
                self.assert_rehashed_proof_refused(
                    snapshot, lambda value: value["preservedAttemptEvidence"][0][
                        "artifactHashes"].update(attemptStartPath=fabricated))

    def test_authorization_hash_rebinding_cannot_hide_changed_authorization(self):
        changed = copy.deepcopy(self.authorization)
        changed["quiescenceProcessMarkers"].append("SYNTHETIC_CHANGED_MARKER")
        disposition.validate_authorization(changed)
        with self.assertRaisesRegex(disposition.DispositionRefusal, "authorization bytes"):
            self.inspect(authorization=changed)
        rebound_hash = hashlib.sha256(disposition.authorization_bytes(changed)).hexdigest()
        with self.assertRaisesRegex(disposition.DispositionRefusal, "target binding"):
            self.inspect(authorization=changed, authorization_sha256=rebound_hash)
        target = copy.deepcopy(self.target_binding)
        target["authorizationSha256"] = "0" * 64
        with self.assertRaisesRegex(disposition.DispositionRefusal, "authorization bytes"):
            self.inspect(target_binding=target, authorization_sha256="0" * 64)
        self.apply()
        with self.connect() as db:
            detail = json.loads(db.execute("SELECT detail FROM events WHERE id=10").fetchone()[0])
            detail["authorization"] = changed
            detail["proof"]["authorizationCanonicalSha256"] = disposition._sha256_json(changed)
            detail["proof"].pop("proofSha256")
            detail["proof"]["proofSha256"] = disposition._sha256_json(detail["proof"])
            db.execute("UPDATE events SET detail=? WHERE id=10", (canonical(detail),))
        with self.assertRaisesRegex(budget.Refusal, "authorization bytes"):
            budget.RequestLedger(self.ledger_path).start()

    def test_444_slots_does_not_authorize_74_tasks_by_three_runs(self):
        wrong = [
            "task-%03d/%s/%d" % (task, arm, run)
            for run in range(1, 4)
            for task in range(1, 75)
            for arm in ("calor-permissive", "calor-strict")
        ]
        self.assertEqual(444, len(wrong))
        self.assertEqual(self.slots[:2], wrong[:2])
        changed = copy.deepcopy(self.authorization)
        changed["oldSnapshot"]["binding"]["plannedSlots"] = wrong
        with self.assertRaisesRegex(disposition.DispositionRefusal, "inventory order"):
            disposition.validate_authorization(changed)
        for slots in (
                sorted(self.slots),
                [slot for task in range(1, 4) for slot in self.slots
                 if slot.startswith("task-%03d/" % task)],
                self.slots[1::-1] + self.slots[2:]):
            with self.subTest(order=slots[:6]):
                with self.assertRaises(disposition.DispositionRefusal):
                    disposition._validate_slot_order(slots)

    def test_reconciled_descriptors_admit_exact_bounds_and_known_capabilities(self):
        for max_tokens in (0, budget.OUTPUT):
            for stream in (False, True):
                for service_tier in ("auto", "standard_only"):
                    with self.subTest(max_tokens=max_tokens, stream=stream, tier=service_tier):
                        disposition._reconciled_row(self.future_row(
                            max_tokens=max_tokens, stream=stream, service_tier=service_tier,
                            betas=sorted(budget.BETA_CAPABILITIES)), self.target_binding)

    def test_reconciled_descriptor_schema_and_capabilities_are_strict(self):
        row = self.future_row()
        descriptor = json.loads(row["request"])
        mutations = [(field, None) for field in descriptor]
        mutations += [("unexpected", True)]
        mutations += [("betaCapabilities", value) for value in (
            None, {}, False, 0, "", "prompt-caching-2024-07-31", [None], [1],
            ["unknown-beta"], [""], ["prompt-caching-2024-07-31"] * 2,
            [" prompt-caching-2024-07-31"],
        )]
        for field, value in mutations:
            with self.subTest(field=field, value=value):
                changed = copy.deepcopy(row)
                request = copy.deepcopy(descriptor)
                if value is None and field != "betaCapabilities":
                    del request[field]
                else:
                    request[field] = value
                changed["request"] = canonical(request)
                with self.assertRaises(disposition.DispositionRefusal):
                    disposition._reconciled_row(changed, self.target_binding)
        for column in ("request", "usage"):
            for value in (None, [], False, "not-an-object"):
                with self.subTest(column=column, value=value):
                    changed = copy.deepcopy(row)
                    changed[column] = canonical(value)
                    with self.assertRaises(disposition.DispositionRefusal):
                        disposition._reconciled_row(changed, self.target_binding)

    def test_consistent_rehashed_request_receipts_cannot_bypass_admission(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        row = self.future_row()
        request_id = ledger.reserve(owner, row["slot"], json.loads(row["request"]))
        ledger.settle(owner, request_id, cost=row["charge"], usage=json.loads(row["usage"]))
        snapshot = ledger.snapshot()
        descriptor = json.loads(row["request"])
        for field, value in (
                ("model", "SYNTHETIC_UNREGISTERED_MODEL"),
                ("maxTokens", budget.OUTPUT + 1), ("maxTokens", False),
                ("maxTokens", 0.0), ("stream", 0), ("stream", "false"),
                ("maximumMicroUsd", row["charge"]),
                ("maximumMicroUsd", descriptor["maximumMicroUsd"] + 1),
                ("maximumMicroUsd", float(descriptor["maximumMicroUsd"])),
                ("betaCapabilities", ["SYNTHETIC_UNPRICED_CAPABILITY"]),
                ("priceSha256", "0" * 64)):
            with self.subTest(field=field, value=value):
                changed = copy.deepcopy(snapshot)
                request = copy.deepcopy(descriptor)
                request[field] = value
                usage = json.loads(row["usage"])
                cost, receipt = budget.reconciled_cost(
                    request, request["model"], usage["usage"], usage["stopReason"])
                changed["requests"][2].update(
                    request=canonical(request), reserved=request["maximumMicroUsd"],
                    charge=cost, usage=canonical(receipt))
                changed["events"][11]["detail"] = canonical({
                    "maximumMicroUsd": request["maximumMicroUsd"]})
                changed["events"][12]["detail"] = canonical({
                    "conservativeChargeMicroUsd": cost,
                    "releasedMicroUsd": request["maximumMicroUsd"] - cost})
                changed["exposureMicroUsd"] = disposition.PERMANENTLY_RETAINED_MICRO_USD + cost
                with self.assertRaises(disposition.DispositionRefusal):
                    self.replay(changed)

    def test_reconciled_row_money_and_request_number_types_are_strict(self):
        row = self.future_row()
        for field in ("reserved", "charge"):
            for value in (float(row[field]), True, False, str(row[field]), None, -1):
                with self.subTest(field=field, value=value):
                    changed = copy.deepcopy(row)
                    changed[field] = value
                    with self.assertRaises(disposition.DispositionRefusal):
                        disposition._reconciled_row(changed, self.target_binding)
        for field, value in (("maxTokens", -1), ("serviceTier", "priority"),
                             ("serviceTier", None)):
            with self.subTest(field=field, value=value):
                changed = copy.deepcopy(row)
                request = json.loads(changed["request"])
                request[field] = value
                changed["request"] = canonical(request)
                with self.assertRaises(disposition.DispositionRefusal):
                    disposition._reconciled_row(changed, self.target_binding)


if __name__ == "__main__":
    unittest.main()
