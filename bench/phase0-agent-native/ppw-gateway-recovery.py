#!/usr/bin/env python3
"""Bounded one-shot recovery for a proven zero-request PP-W gateway ledger."""
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import stat
from urllib.parse import quote


KIND = "pp-w-request-gateway-v1"
RECOVERY_KIND = "pp-w-zero-request-gateway-recovery-v1"
RECOVERY_EVENT = "zero-request-recovery"
FAILED_STATE = "INCOMPLETE_POLICY"
PILOT_CEILING_MICRO_USD = 1_000_000_000
EXPECTED_BINDING_FIELDS = {
    "stage", "epochId", "priceSha256", "authorizationSha256", "protocolSha256",
    "planSha256", "harnessArtifacts", "plannedSlots",
}
EXPECTED_TABLE_COLUMNS = {
    "scope": ["id", "binding", "ceiling", "state", "owner"],
    "requests": ["id", "slot", "request", "reserved", "charge", "state", "reason", "usage"],
    "events": ["id", "kind", "request_id", "detail", "created"],
}
ARCHIVE_ROOT_FILES = {
    "pins.json", "registration.json", "spending-initial.json", "collection-outcome.json",
}


class RecoveryRefusal(ValueError):
    """A fixed recovery invariant was not proven."""


def _require(condition, message):
    if not condition:
        raise RecoveryRefusal("PP-W zero-request recovery: " + message)


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def _decode(raw, description):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            _require(key not in value, description + " has a duplicate JSON key")
            value[key] = item
        return value

    try:
        return json.loads(raw, object_pairs_hook=unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(
                              RecoveryRefusal("PP-W zero-request recovery: nonfinite JSON")))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise RecoveryRefusal(
            "PP-W zero-request recovery: invalid " + description + " JSON") from error


def _sha256_bytes(value):
    return hashlib.sha256(value).hexdigest()


def _sha256_json(value):
    return _sha256_bytes(_canonical(value).encode("utf-8"))


def _validate_sha256(value, name):
    _require(isinstance(value, str) and len(value) == 64
             and all(character in "0123456789abcdef" for character in value),
             name + " must be a lowercase SHA-256")


def _path_parts_are_unlinked(path):
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        if current.is_symlink():
            return False
    return True


def _absolute_path(value, name, must_exist):
    path = Path(value)
    _require(path.is_absolute() and ".." not in path.parts, name + " must be absolute and canonical")
    _require(_path_parts_are_unlinked(path), name + " contains a symlink")
    if must_exist:
        _require(path.exists(), name + " does not exist")
    else:
        _require(not path.is_symlink(), name + " is a symlink")
    return path


def _regular_single_link(path, name):
    info = os.stat(path, follow_symlinks=False)
    _require(stat.S_ISREG(info.st_mode), name + " is not a regular file")
    _require(info.st_nlink == 1, name + " is hard-linked")
    return info


def _contained(path, root):
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _validate_paths(ledger_path, failed_archive, protected_root, backup_path):
    ledger = _absolute_path(ledger_path, "ledger path", True)
    archive = _absolute_path(failed_archive, "failed archive", True)
    protected = _absolute_path(protected_root, "protected root", True)
    backup = _absolute_path(backup_path, "backup path", False)
    _require(archive.is_dir(), "failed archive is not a directory")
    _require(protected.is_dir(), "protected root is not a directory")
    _regular_single_link(ledger, "ledger")
    _require(_contained(ledger, protected), "ledger is outside the protected root")
    _require(backup.parent == protected, "backup must be a direct child of the protected root")
    _require(backup != ledger and not _contained(backup, archive),
             "backup aliases the live ledger or failed archive")
    if backup.exists():
        backup_info = _regular_single_link(backup, "backup")
        ledger_info = os.stat(ledger, follow_symlinks=False)
        _require((backup_info.st_dev, backup_info.st_ino) != (ledger_info.st_dev, ledger_info.st_ino),
                 "backup aliases the live ledger")
    return ledger, archive, protected, backup


def _sidecars(path):
    return [Path(str(path) + suffix) for suffix in ("-journal", "-wal", "-shm")]


def _require_no_sqlite_sidecars(path):
    _require(not any(item.exists() or item.is_symlink() for item in _sidecars(path)),
             "SQLite journal or WAL sidecar is present")


def _hash_regular_file(path, description):
    before = _regular_single_link(path, description)
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    try:
        opened = os.fstat(descriptor)
        _require((opened.st_dev, opened.st_ino) == (before.st_dev, before.st_ino),
                 description + " changed while opening")
        digest = hashlib.sha256()
        size = 0
        while True:
            block = os.read(descriptor, 1024 * 1024)
            if not block:
                break
            digest.update(block)
            size += len(block)
        after = os.fstat(descriptor)
        current = os.stat(path, follow_symlinks=False)
        identity = lambda item: (
            item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns, item.st_ctime_ns
        )
        _require(identity(opened) == identity(after) == identity(current),
                 description + " changed while hashing")
        return digest.hexdigest(), size
    finally:
        os.close(descriptor)


def archive_inventory(failed_archive):
    """Hash every archive file without interpreting candidate/result contents."""
    archive = _absolute_path(failed_archive, "failed archive", True)
    _require(archive.is_dir(), "failed archive is not a directory")
    entries = []
    directory_states = {}
    for directory, names, files in os.walk(archive, topdown=True, followlinks=False):
        current = Path(directory)
        info = os.stat(current, follow_symlinks=False)
        _require(stat.S_ISDIR(info.st_mode) and info.st_nlink >= 1,
                 "archive contains a non-directory")
        directory_states[current] = (info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns)
        for name in list(names):
            child = current / name
            _require(not child.is_symlink(), "archive contains a linked directory")
        for name in files:
            child = current / name
            _require(not child.is_symlink(), "archive contains a linked file")
            relative = child.relative_to(archive).as_posix()
            digest, size = _hash_regular_file(child, "archive file " + relative)
            entries.append({"path": relative, "sha256": digest, "size": size})
    for directory, expected in directory_states.items():
        info = os.stat(directory, follow_symlinks=False)
        _require((info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns) == expected,
                 "archive directory changed while hashing")
    entries.sort(key=lambda item: item["path"])
    return {
        "sha256": _sha256_json(entries),
        "fileCount": len(entries),
        "byteCount": sum(item["size"] for item in entries),
        "files": entries,
    }


def _read_archive_json(archive, relative, inventory):
    entry = next((item for item in inventory["files"] if item["path"] == relative), None)
    _require(entry is not None, "failed archive is missing " + relative)
    path = archive / relative
    raw = path.read_bytes()
    _require(_sha256_bytes(raw) == entry["sha256"], relative + " changed after inventory")
    return _decode(raw, relative)


def _binding(value, name):
    _require(isinstance(value, dict) and set(value) == EXPECTED_BINDING_FIELDS,
             name + " fields differ")
    _require(value["stage"] == "pilot" and isinstance(value["epochId"], str)
             and value["epochId"], name + " is not a pilot epoch")
    for field in ("priceSha256", "authorizationSha256", "protocolSha256", "planSha256"):
        _validate_sha256(value[field], name + " " + field)
    artifacts = value["harnessArtifacts"]
    _require(isinstance(artifacts, dict) and artifacts, name + " has no harness artifacts")
    _require(all(isinstance(key, str) and key and isinstance(digest, str)
                 and len(digest) == 64
                 and all(character in "0123456789abcdef" for character in digest)
                 for key, digest in artifacts.items()), name + " has invalid harness artifacts")
    slots = value["plannedSlots"]
    _require(isinstance(slots, list) and len(slots) == 444
             and all(isinstance(slot, str) and slot for slot in slots)
             and len(set(slots)) == len(slots), name + " must contain the exact 444-slot inventory")
    return value


def _validate_bindings(old, target):
    old = _binding(old, "old binding")
    target = _binding(target, "target binding")
    _require(old["epochId"] != target["epochId"], "target epoch must be new")
    for field in ("stage", "priceSha256", "plannedSlots"):
        _require(target[field] == old[field], "target changed preserved " + field)
    _require(set(old["harnessArtifacts"]) <= set(target["harnessArtifacts"]),
             "target removed registered execution artifacts")
    _require(target["protocolSha256"] != old["protocolSha256"]
             and target["planSha256"] != old["planSha256"],
             "target must carry the reviewed recovery protocol and plan identities")


def _event_detail(event):
    _require(isinstance(event, dict), "malformed accounting event")
    return _decode(event.get("detail"), "accounting event detail")


def _validate_failed_events(events, ceiling):
    _require(isinstance(events, list) and len(events) == 3, "failed ledger must have exactly 3 events")
    _require(all(set(event) == {"id", "kind", "request_id", "detail", "created"}
                 and isinstance(event["created"], str) and event["created"] for event in events),
             "failed ledger event fields differ")
    _require([event.get("id") for event in events] == [1, 2, 3],
             "failed ledger event sequence differs")
    _require(all(event.get("request_id") is None for event in events),
             "failed ledger has request-linked events")
    expected = [
        ("initialized", {"ceilingMicroUsd": ceiling}),
        ("started", {}),
        ("stopped", {"reason": FAILED_STATE}),
    ]
    for event, (kind, detail) in zip(events, expected):
        _require(event.get("kind") == kind and _event_detail(event) == detail,
                 "failed ledger event history differs")


def _database_snapshot(db):
    objects = db.execute(
        "SELECT type,name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name"
    ).fetchall()
    _require([(row["type"], row["name"]) for row in objects]
             == [("table", "events"), ("table", "requests"), ("table", "scope")],
             "ledger schema objects differ")
    for table, expected in EXPECTED_TABLE_COLUMNS.items():
        columns = [row["name"] for row in db.execute("PRAGMA table_info(" + table + ")")]
        _require(columns == expected, "ledger " + table + " columns differ")
    _require(db.execute("PRAGMA integrity_check").fetchone()[0] == "ok",
             "ledger integrity check failed")
    scope_rows = db.execute("SELECT * FROM scope ORDER BY id").fetchall()
    _require(len(scope_rows) == 1 and scope_rows[0]["id"] == 1, "ledger scope row differs")
    scope = dict(scope_rows[0])
    requests = [dict(row) for row in db.execute(
        "SELECT id,slot,request,reserved,charge,state,reason,usage FROM requests ORDER BY rowid")]
    events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
    return scope, requests, events


def _expected_snapshot(binding, ceiling, state, events):
    return {
        "kind": KIND,
        "state": state,
        "binding": binding,
        "ceilingMicroUsd": ceiling,
        "exposureMicroUsd": 0,
        "requests": [],
        "events": events,
        "verdict": None,
        "basis": "request reservations; only validated complete provider usage permits release",
    }


def _validate_archive(archive, inventory, old_binding, ceiling, events):
    paths = {entry["path"] for entry in inventory["files"]}
    _require(ARCHIVE_ROOT_FILES <= paths, "failed archive lacks required operational artifacts")
    _require("spending-final.json" not in paths, "failed archive contains final accounting")
    _require(not any(Path(path).name.endswith(("-journal", "-wal", "-shm")) for path in paths),
             "failed archive contains SQLite journal state")
    invocation_paths = sorted(path for path in paths if Path(path).name == "client-invocation.json")
    invocation_slots = []
    for path in invocation_paths:
        invocation = _read_archive_json(archive, path, inventory)
        _require(isinstance(invocation, dict) and set(invocation) == {"exitCode"}
                 and type(invocation["exitCode"]) is int
                 and 0 < invocation["exitCode"] < 128 and invocation["exitCode"] != 124,
                 "failed archive contains completed, interrupted, or malformed client invocation")
        parts = Path(path).parts
        _require(len(parts) == 5 and parts[0] == "runs"
                 and parts[3].startswith("run-") and parts[4] == "client-invocation.json",
                 "failed archive contains a misplaced client invocation")
        try:
            run = int(parts[3][4:])
        except ValueError as error:
            raise RecoveryRefusal(
                "PP-W zero-request recovery: failed archive invocation run is malformed") from error
        invocation_slots.append("%s/%s/%d" % (parts[1], parts[2], run))

    pins = _read_archive_json(archive, "pins.json", inventory)
    registration = _read_archive_json(archive, "registration.json", inventory)
    initial = _read_archive_json(archive, "spending-initial.json", inventory)
    outcome = _read_archive_json(archive, "collection-outcome.json", inventory)

    _require(pins.get("epochId") == old_binding["epochId"] and pins.get("stage") == "pilot"
             and pins.get("lifecycle") == "collecting" and pins.get("mode") == "live",
             "failed archive pins differ from the old scope")
    _require(pins.get("harnessArtifacts") == old_binding["harnessArtifacts"],
             "failed archive harness binding differs")
    registration_entry = next(
        item for item in inventory["files"] if item["path"] == "registration.json")
    _require(pins.get("registrationSha256") == registration_entry["sha256"],
             "failed archive registration hash differs")
    selected = registration.get("stages", {}).get("pilot", {})
    _require(selected.get("spendAuthorization", {}).get("sha256")
             == old_binding["authorizationSha256"]
             and selected.get("spendingPlan", {}).get("sha256") == old_binding["planSha256"],
             "failed archive registration differs from the old funding binding")
    suite, runs = pins.get("suite"), pins.get("runsPerArm")
    _require(isinstance(suite, list) and suite
             and all(isinstance(task, str) and task for task in suite)
             and len(set(suite)) == len(suite)
             and type(runs) is int and runs > 0,
             "failed archive schedule is malformed")
    slots = ["%s/%s/%d" % (task, arm, run)
             for run in range(1, runs + 1) for task in suite
             for arm in ("calor-permissive", "calor-strict")]
    _require(slots == old_binding["plannedSlots"], "failed archive schedule differs")
    _require(invocation_slots == slots[:1],
             "failed archive must preserve exactly the first scheduled client launch")

    initial_events = events[:2]
    _require(initial == _expected_snapshot(old_binding, ceiling, "collecting", initial_events),
             "failed archive initial accounting differs")
    _require(isinstance(outcome, dict)
             and set(outcome) == {"kind", "epochId", "stage", "complete", "verdict", "reason", "spending"}
             and outcome.get("kind") == "pp-w-incomplete-collection"
             and outcome.get("epochId") == old_binding["epochId"]
             and outcome.get("stage") == "pilot" and outcome.get("complete") is False
             and outcome.get("verdict") is None,
             "failed archive outcome is not the fixed incomplete record")
    _require(outcome["spending"] == _expected_snapshot(
        old_binding, ceiling, FAILED_STATE, events),
        "failed archive stopped accounting differs")
    return {
        "registrationSha256": registration_entry["sha256"],
        "clientInvocations": len(invocation_paths),
        "preservedAttemptedSlots": invocation_slots,
        "pinsIdentitySha256": _sha256_json({
            key: pins.get(key) for key in
            ("epochId", "stage", "modelPin", "agentVersion", "runsPerArm",
             "suite", "compiler", "arms", "harnessArtifacts")
        }),
    }


def _open_database(path, mode):
    uri = "file:" + quote(str(path), safe="/") + "?mode=" + mode
    db = sqlite3.connect(uri, uri=True, timeout=0, isolation_level=None)
    db.row_factory = sqlite3.Row
    db.execute("PRAGMA query_only=" + ("ON" if mode == "ro" else "OFF"))
    db.execute("PRAGMA busy_timeout=0")
    return db


def _proof_payload(ledger, archive, protected, backup, old_binding, target_binding,
                   expected_ledger_sha256, expected_archive_inventory_sha256,
                   recovery_registration_sha256, inventory, archive_details):
    return {
        "schemaVersion": 1,
        "kind": RECOVERY_KIND,
        "oldEpochId": old_binding["epochId"],
        "targetEpochId": target_binding["epochId"],
        "oldBindingSha256": _sha256_json(old_binding),
        "targetBindingSha256": _sha256_json(target_binding),
        "ceilingMicroUsd": PILOT_CEILING_MICRO_USD,
        "oldLedgerSha256": expected_ledger_sha256,
        "failedArchiveInventorySha256": expected_archive_inventory_sha256,
        "failedArchiveFileCount": inventory["fileCount"],
        "failedArchiveByteCount": inventory["byteCount"],
        "failedRegistrationSha256": archive_details["registrationSha256"],
        "failedPinsIdentitySha256": archive_details["pinsIdentitySha256"],
        "recoveryRegistrationSha256": recovery_registration_sha256,
        "ledgerPathSha256": _sha256_bytes(str(ledger).encode()),
        "failedArchivePathSha256": _sha256_bytes(str(archive).encode()),
        "protectedRootPathSha256": _sha256_bytes(str(protected).encode()),
        "backupPathSha256": _sha256_bytes(str(backup).encode()),
        "ledgerState": FAILED_STATE,
        "eventKinds": ["initialized", "started", "stopped"],
        "requestCount": 0,
        "completedSlots": 0,
        "clientInvocations": archive_details["clientInvocations"],
        "preservedAttemptedSlots": archive_details["preservedAttemptedSlots"],
        "remainingSlotCount": len(target_binding["plannedSlots"])
        - len(archive_details["preservedAttemptedSlots"]),
    }


def _inspect_locked(db, ledger, archive, protected, backup, old_binding, target_binding,
                    expected_ledger_sha256, expected_archive_inventory_sha256,
                    recovery_registration_sha256):
    _require(db.execute("PRAGMA journal_mode").fetchone()[0].lower() == "delete",
             "ledger is not in rollback-journal DELETE mode")
    _require_no_sqlite_sidecars(ledger)
    scope, requests, events = _database_snapshot(db)
    _require(scope["binding"] == _canonical(old_binding), "old ledger binding changed")
    _require(scope["ceiling"] == PILOT_CEILING_MICRO_USD, "old ledger ceiling changed")
    _require(scope["state"] == FAILED_STATE, "old ledger state changed or recovery already applied")
    _require(isinstance(scope["owner"], str) and scope["owner"],
             "failed ledger has no original owner marker")
    _require(not requests, "any request row forbids zero-request recovery")
    _validate_failed_events(events, PILOT_CEILING_MICRO_USD)
    ledger_sha256, _ = _hash_regular_file(ledger, "ledger")
    _require(ledger_sha256 == expected_ledger_sha256, "old ledger source hash changed")
    inventory = archive_inventory(archive)
    _require(inventory["sha256"] == expected_archive_inventory_sha256,
             "failed archive inventory changed")
    archive_details = _validate_archive(
        archive, inventory, old_binding, PILOT_CEILING_MICRO_USD, events)
    if backup.exists():
        _require(stat.S_IMODE(os.stat(backup, follow_symlinks=False).st_mode) == 0o600,
                 "existing backup mode differs from 0600")
        backup_sha256, _ = _hash_regular_file(backup, "backup")
        _require(backup_sha256 == expected_ledger_sha256,
                 "existing backup differs from the failed ledger")
    return _proof_payload(
        ledger, archive, protected, backup, old_binding, target_binding,
        expected_ledger_sha256, expected_archive_inventory_sha256,
        recovery_registration_sha256, inventory, archive_details)


def _validate_inputs(expected_old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256):
    _validate_bindings(expected_old_binding, target_binding)
    _validate_sha256(expected_ledger_sha256, "expected ledger hash")
    _validate_sha256(expected_archive_inventory_sha256, "expected archive inventory hash")
    _validate_sha256(recovery_registration_sha256, "recovery registration hash")


def inspect_zero_request_recovery(
        ledger_path, failed_archive, protected_root, backup_path, *,
        expected_old_binding, target_binding, expected_ledger_sha256,
        expected_archive_inventory_sha256, recovery_registration_sha256):
    """Read and prove the fixed failed scope without mutating the ledger or archive."""
    _validate_inputs(expected_old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256)
    ledger, archive, protected, backup = _validate_paths(
        ledger_path, failed_archive, protected_root, backup_path)
    _require_no_sqlite_sidecars(ledger)
    db = _open_database(ledger, "ro")
    try:
        db.execute("BEGIN")
        proof = _inspect_locked(
            db, ledger, archive, protected, backup, expected_old_binding, target_binding,
            expected_ledger_sha256, expected_archive_inventory_sha256,
            recovery_registration_sha256)
        db.execute("ROLLBACK")
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    _require_no_sqlite_sidecars(ledger)
    proof["proofSha256"] = _sha256_json(proof)
    return proof


def prove_zero_request_recovery(
        ledger_path, failed_archive, protected_root, backup_path, *,
        expected_old_binding, target_binding, expected_ledger_sha256,
        expected_archive_inventory_sha256, recovery_registration_sha256):
    """Produce the reviewable proof used to confirm a later apply operation."""
    return inspect_zero_request_recovery(
        ledger_path, failed_archive, protected_root, backup_path,
        expected_old_binding=expected_old_binding, target_binding=target_binding,
        expected_ledger_sha256=expected_ledger_sha256,
        expected_archive_inventory_sha256=expected_archive_inventory_sha256,
        recovery_registration_sha256=recovery_registration_sha256)


def _copy_backup(ledger, protected, backup, expected_sha256):
    if backup.exists():
        digest, _ = _hash_regular_file(backup, "backup")
        _require(digest == expected_sha256
                 and stat.S_IMODE(os.stat(backup, follow_symlinks=False).st_mode) == 0o600,
                 "existing backup is not the exact mode-0600 failed ledger")
        return
    root_descriptor = os.open(protected, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    source_descriptor = os.open(ledger, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    destination_descriptor = None
    try:
        source_info = os.fstat(source_descriptor)
        _require(source_info.st_nlink == 1 and stat.S_ISREG(source_info.st_mode),
                 "ledger changed before backup")
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
        destination_descriptor = os.open(backup.name, flags, 0o600, dir_fd=root_descriptor)
        os.fchmod(destination_descriptor, 0o600)
        digest = hashlib.sha256()
        while True:
            block = os.read(source_descriptor, 1024 * 1024)
            if not block:
                break
            digest.update(block)
            view = memoryview(block)
            while view:
                written = os.write(destination_descriptor, view)
                view = view[written:]
        os.fsync(destination_descriptor)
        _require(digest.hexdigest() == expected_sha256,
                 "ledger changed while creating the backup")
        os.fsync(root_descriptor)
    finally:
        if destination_descriptor is not None:
            os.close(destination_descriptor)
        os.close(source_descriptor)
        os.close(root_descriptor)
    backup_digest, _ = _hash_regular_file(backup, "backup")
    _require(backup_digest == expected_sha256
             and stat.S_IMODE(os.stat(backup, follow_symlinks=False).st_mode) == 0o600,
             "backup verification failed")


def _recovery_detail(old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256,
                     preserved_attempted_slots):
    return {
        "schemaVersion": 1,
        "oldEpochId": old_binding["epochId"],
        "newEpochId": target_binding["epochId"],
        "oldBindingSha256": _sha256_json(old_binding),
        "newBindingSha256": _sha256_json(target_binding),
        "oldLedgerSha256": expected_ledger_sha256,
        "failedArchiveInventorySha256": expected_archive_inventory_sha256,
        "recoveryRegistrationSha256": recovery_registration_sha256,
        "preservedAttemptedSlots": preserved_attempted_slots,
    }


def apply_zero_request_recovery(
        ledger_path, failed_archive, protected_root, backup_path, *,
        expected_old_binding, target_binding, expected_ledger_sha256,
        expected_archive_inventory_sha256, recovery_registration_sha256,
        confirmed_proof_sha256):
    """Apply one reviewed proof. This never starts a collector or a new epoch."""
    _validate_inputs(expected_old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256)
    _validate_sha256(confirmed_proof_sha256, "confirmed proof hash")
    ledger, archive, protected, backup = _validate_paths(
        ledger_path, failed_archive, protected_root, backup_path)
    _require_no_sqlite_sidecars(ledger)
    db = _open_database(ledger, "rw")
    committed = False
    try:
        try:
            db.execute("PRAGMA synchronous=FULL")
            db.execute("BEGIN IMMEDIATE")
        except sqlite3.OperationalError as error:
            raise RecoveryRefusal(
                "PP-W zero-request recovery: concurrent ledger writer or unavailable lock") from error
        proof = _inspect_locked(
            db, ledger, archive, protected, backup, expected_old_binding, target_binding,
            expected_ledger_sha256, expected_archive_inventory_sha256,
            recovery_registration_sha256)
        _require(_sha256_json(proof) == confirmed_proof_sha256,
                 "reviewed proof does not match current evidence")
        _copy_backup(ledger, protected, backup, expected_ledger_sha256)
        _require_no_sqlite_sidecars(ledger)
        current_sha256, _ = _hash_regular_file(ledger, "ledger")
        _require(current_sha256 == expected_ledger_sha256,
                 "ledger changed after backup and before recovery")
        second_inventory = archive_inventory(archive)
        _require(second_inventory["sha256"] == expected_archive_inventory_sha256,
                 "failed archive changed before recovery")
        cursor = db.execute(
            "UPDATE scope SET binding=?,state='ready',owner=NULL "
            "WHERE id=1 AND binding=? AND ceiling=? AND state=?",
            (_canonical(target_binding), _canonical(expected_old_binding),
             PILOT_CEILING_MICRO_USD, FAILED_STATE))
        _require(cursor.rowcount == 1, "old scope changed before recovery")
        db.execute(
            "INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
            (RECOVERY_EVENT, None, _canonical(_recovery_detail(
                expected_old_binding, target_binding, expected_ledger_sha256,
                expected_archive_inventory_sha256, recovery_registration_sha256,
                proof["preservedAttemptedSlots"]))))
        _require(db.execute("SELECT last_insert_rowid()").fetchone()[0] == 4,
                 "recovery event did not extend the exact old history")
        db.execute("COMMIT")
        committed = True
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    _require(committed, "recovery transaction did not commit")
    result = dict(proof)
    result.update(applied=True, backupSha256=expected_ledger_sha256, recoveryEventId=4)
    return result


def validate_recovered_initial_archive(
        failed_snapshot, recovered_initial, *, expected_old_binding, target_binding,
        expected_ledger_sha256, expected_archive_inventory_sha256,
        recovery_registration_sha256, preserved_attempted_slots):
    """Validate the old three-event prefix plus recovery and the new start event."""
    _validate_inputs(expected_old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256)
    _require(isinstance(failed_snapshot, dict) and isinstance(recovered_initial, dict),
             "accounting snapshots are required")
    _require(preserved_attempted_slots == expected_old_binding["plannedSlots"][:1],
             "preserved attempted slots differ from the proven first launch")
    failed_events = failed_snapshot.get("events")
    _validate_failed_events(failed_events, PILOT_CEILING_MICRO_USD)
    _require(failed_snapshot == _expected_snapshot(
        expected_old_binding, PILOT_CEILING_MICRO_USD, FAILED_STATE, failed_events),
        "failed snapshot differs from the proven stopped ledger")
    events = recovered_initial.get("events")
    _require(isinstance(events, list) and len(events) == 5,
             "recovered initial snapshot must contain exactly five events")
    _require(events[:3] == failed_events, "recovered initial snapshot changed the old audit prefix")
    _require([event.get("id") for event in events] == [1, 2, 3, 4, 5],
             "recovered event sequence differs")
    recovery, started = events[3], events[4]
    _require(set(recovery) == {"id", "kind", "request_id", "detail", "created"}
             and isinstance(recovery["created"], str) and recovery["created"]
             and recovery.get("kind") == RECOVERY_EVENT and recovery.get("request_id") is None
             and _event_detail(recovery) == _recovery_detail(
                 expected_old_binding, target_binding, expected_ledger_sha256,
                 expected_archive_inventory_sha256, recovery_registration_sha256,
                 preserved_attempted_slots),
             "recovery event evidence differs")
    _require(set(started) == {"id", "kind", "request_id", "detail", "created"}
             and isinstance(started["created"], str) and started["created"]
             and started.get("kind") == "started" and started.get("request_id") is None
             and _event_detail(started) == {}, "new collection start event differs")
    _require(recovered_initial == _expected_snapshot(
        target_binding, PILOT_CEILING_MICRO_USD, "collecting", events),
        "recovered initial accounting differs")
    return {
        "kind": RECOVERY_KIND,
        "oldEpochId": expected_old_binding["epochId"],
        "targetEpochId": target_binding["epochId"],
        "preservedEventCount": 3,
        "recoveryEventId": 4,
        "newStartEventId": 5,
        "requestCount": 0,
        "preservedAttemptedSlots": preserved_attempted_slots,
        "remainingSlotCount": len(target_binding["plannedSlots"]) - len(preserved_attempted_slots),
        "ceilingMicroUsd": PILOT_CEILING_MICRO_USD,
    }


def complete_recovered_scope(
        ledger_path, owner, failed_snapshot, *, expected_old_binding, target_binding,
        expected_ledger_sha256, expected_archive_inventory_sha256,
        recovery_registration_sha256, preserved_attempted_slots):
    """Complete a recovered scope after only the previously unstarted slots finish."""
    _validate_inputs(expected_old_binding, target_binding, expected_ledger_sha256,
                     expected_archive_inventory_sha256, recovery_registration_sha256)
    _require(isinstance(owner, str) and owner, "active owner is required")
    ledger = _absolute_path(ledger_path, "ledger path", True)
    _regular_single_link(ledger, "ledger")
    _require_no_sqlite_sidecars(ledger)
    db = _open_database(ledger, "rw")
    try:
        db.execute("PRAGMA synchronous=FULL")
        db.execute("BEGIN IMMEDIATE")
        scope, requests, events = _database_snapshot(db)
        _require(scope["binding"] == _canonical(target_binding)
                 and scope["ceiling"] == PILOT_CEILING_MICRO_USD
                 and scope["state"] == "collecting" and scope["owner"] == owner,
                 "recovered scope is not collecting under the supplied owner")
        initial = _expected_snapshot(
            target_binding, PILOT_CEILING_MICRO_USD, "collecting", events[:5])
        validate_recovered_initial_archive(
            failed_snapshot, initial, expected_old_binding=expected_old_binding,
            target_binding=target_binding, expected_ledger_sha256=expected_ledger_sha256,
            expected_archive_inventory_sha256=expected_archive_inventory_sha256,
            recovery_registration_sha256=recovery_registration_sha256,
            preserved_attempted_slots=preserved_attempted_slots)
        _require(all(row["state"] == "reconciled" and row["reason"] is None for row in requests),
                 "in-flight or unknown charge remains")
        remaining = [
            slot for slot in target_binding["plannedSlots"] if slot not in preserved_attempted_slots
        ]
        _require(not any(row["slot"] in preserved_attempted_slots for row in requests),
                 "preserved failed attempt was retried")
        completed = [
            _event_detail(event)["slot"] for event in events if event["kind"] == "slot-complete"
        ]
        _require(completed == remaining,
                 "only the exact previously unstarted slot sequence may complete")
        request_slots = {row["slot"] for row in requests}
        _require(request_slots == set(remaining),
                 "each continued slot requires gateway-accounted traffic")
        _require(not any(event["kind"] == "collection-complete" for event in events),
                 "recovered scope is already complete")
        db.execute("UPDATE scope SET state='complete' WHERE id=1")
        db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                   ("collection-complete", None, _canonical({
                       "preservedAttemptedSlots": preserved_attempted_slots,
                   })))
        db.execute("COMMIT")
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    return {
        "kind": RECOVERY_KIND,
        "state": "complete",
        "preservedAttemptedSlots": preserved_attempted_slots,
        "continuedSlots": len(remaining),
        "plannedSlots": len(target_binding["plannedSlots"]),
    }
