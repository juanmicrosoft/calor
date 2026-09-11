#!/usr/bin/env python3
"""Append-only disposition of the fixed PP-W historical unknown liability."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import stat
import subprocess
from urllib.parse import quote


DISPOSITION_KIND = "pp-w-historical-liability-disposition-v1"
DISPOSITION_EVENT = "historical-liability-disposition"
PERMANENTLY_RETAINED_MICRO_USD = 51_040_000
PILOT_CEILING_MICRO_USD = 1_000_000_000
HISTORICAL_REQUEST_RESERVATION = 25_520_000
HISTORICAL_PRICE_SHA256 = "582a71eeb22ad7604759d7441f719615def5a04a5ca2cecece8a2c9e1f45eb3e"
EXPECTED_OLD_LEDGER_SHA256 = "464acefd37b96620aeab69c025cbb11703ca038d91eaa0d3d514af3d3c8a8990"
EXPECTED_OLD_BACKUP_SHA256 = "0f32e092fbd760f7273405b4feda7e107b1a5630c9d98668b90d84a323048295"
GRANT_QUOTE = (
    "I authorize permanently retaining the full $51.04 against the existing $1,000 ceiling, "
    "while leaving the actual cost explicitly unknown. Preserve both consumed attempts. "
    "Implement and independently review this accounting amendment, and resume only the 442 "
    "untouched slots if the review confirms the financial bound is sound. Keep the scientific "
    "rules unchanged; future accounting failures must still halt collection."
)
GRANT_TIME = "2026-09-11T07:39:10.192-04:00"
GRANT_REFERENCE = "https://github.com/juanmicrosoft/calor/issues/1436#issuecomment-5633881736"
OLD_EPOCHS = ("w-rows-pilot-gateway-001", "w-rows-pilot-gateway-002")
TARGET_EPOCH = "w-rows-pilot-gateway-003"
HISTORICAL_REQUEST_IDS = (
    "8d7fa7bf4a31f28ba7f7e8f6802781e8b6d8238fb38d3219",
    "a24d9933f2d62c0c904eefdb81d9b592b4e36d30a7e577b3",
)
BINDING_FIELDS = {
    "stage", "epochId", "priceSha256", "authorizationSha256", "protocolSha256",
    "planSha256", "harnessArtifacts", "plannedSlots",
}
SNAPSHOT_FIELDS = {
    "kind", "state", "binding", "ceilingMicroUsd", "exposureMicroUsd", "requests",
    "events", "verdict", "basis", "accountedSlots", "validCompletedSlots",
    "invalidTerminalSlots", "requestBearingSlots",
}
REPORT_FIELDS = {
    "permanentUnknownMicroUsd", "actualCost", "historicalUnknownRequestCount",
    "historicalUnknownRequestIds", "historicalSourceEpochIds", "historicalPreservedSlots",
    "liveUnknownRequestCount", "liveUnknownRequestIds", "liveReservedRequestCount",
    "remainingNonterminalSlots",
}
REQUEST_FIELDS = {"id", "slot", "request", "reserved", "charge", "state", "reason", "usage"}
EVENT_FIELDS = {"id", "kind", "request_id", "detail", "created"}
AUTHORIZATION_FIELDS = {
    "schemaVersion", "kind", "evidenceMode", "grant", "oldSnapshot", "oldLedgerSha256",
    "oldBackupSha256", "predecessorArchives", "preservedAttempts", "targetEpochId",
    "targetPriceSha256", "targetHarnessArtifacts", "permanentlyRetainedMicroUsd",
    "actualCost", "futureUnknownPolicy", "quiescenceProcessMarkers",
}
GRANT_FIELDS = {"quote", "authorizedAt", "reference"}
ARCHIVE_FIELDS = {
    "archiveRole", "epochId", "pathSha256", "inventorySha256", "fileCount", "byteCount",
    "pinsPath", "pinsSha256",
}
ATTEMPT_FIELDS = {
    "slot", "epochId", "classification", "archiveRole", "rawRecordPath",
    "rawRecordSha256", "clientInvocationPath", "clientInvocationSha256",
    "invalidReasonPath", "invalidReasonSha256", "attemptStartPath",
    "attemptStartSha256", "sourceHashes",
}
ATTEMPT_CLASSIFICATIONS = (
    "HISTORICAL_TERMINAL_INVALID",
    "HISTORICAL_RAW_API_INVALID_CENSORED",
)
ARCHIVE_ROLES = ("original-epoch-001", "failed-epoch-002")
TRUSTED_LAUNCHER_SOURCES = (
    "run-pair.sh", "ppw-gateway-client.py", "ppw-budget-gateway.py", "ppw-gateway-budget.py",
)
EXPECTED_TABLE_COLUMNS = {
    "scope": ["id", "binding", "ceiling", "state", "owner"],
    "requests": ["id", "slot", "request", "reserved", "charge", "state", "reason", "usage"],
    "events": ["id", "kind", "request_id", "detail", "created"],
}


class DispositionRefusal(ValueError):
    """A fixed disposition invariant was not proven."""


def _require(condition, message):
    if not condition:
        raise DispositionRefusal("PP-W historical liability disposition: " + message)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def authorization_bytes(value):
    return (json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n").encode("utf-8")


def _decode(raw, description):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            _require(key not in result, description + " has a duplicate JSON key")
            result[key] = value
        return result
    _require(isinstance(raw, (str, bytes, bytearray)), description + " is not JSON")
    try:
        return json.loads(
            raw, object_pairs_hook=unique,
            parse_constant=lambda _: (_ for _ in ()).throw(
                DispositionRefusal(
                    "PP-W historical liability disposition: nonfinite JSON")))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise DispositionRefusal(
            "PP-W historical liability disposition: invalid " + description + " JSON") from error


def _sha256_bytes(value):
    return hashlib.sha256(value).hexdigest()


def _sha256_json(value):
    return _sha256_bytes(canonical(value).encode("utf-8"))


def _sha256(value, description):
    _require(isinstance(value, str) and len(value) == 64
             and all(character in "0123456789abcdef" for character in value),
             description + " must be a lowercase SHA-256")
    return value


def _relative_path(value, description):
    _require(isinstance(value, str) and value and not Path(value).is_absolute()
             and ".." not in Path(value).parts, description + " must be a relative canonical path")
    return value


def _binding(value, description):
    _require(isinstance(value, dict) and set(value) == BINDING_FIELDS,
             description + " fields differ")
    _require(value["stage"] == "pilot" and isinstance(value["epochId"], str)
             and value["epochId"], description + " is not a pilot binding")
    for name in ("priceSha256", "authorizationSha256", "protocolSha256", "planSha256"):
        _sha256(value[name], description + " " + name)
    artifacts = value["harnessArtifacts"]
    _require(isinstance(artifacts, dict) and artifacts
             and all(isinstance(name, str) and name and _valid_sha(digest)
                     for name, digest in artifacts.items()),
             description + " harness artifacts differ")
    slots = value["plannedSlots"]
    _require(isinstance(slots, list) and len(slots) == 444
             and all(isinstance(slot, str) and slot for slot in slots)
             and len(set(slots)) == len(slots),
             description + " must bind the exact unique 444-slot inventory")
    _validate_slot_order(slots)
    return value


def _valid_sha(value):
    return isinstance(value, str) and len(value) == 64 \
        and all(character in "0123456789abcdef" for character in value)


def _validate_slot_order(slots):
    parsed = []
    for slot in slots:
        parts = slot.split("/")
        _require(len(parts) == 3
                 and parts[1] in ("calor-permissive", "calor-strict"),
                 "registered slot syntax differs")
        try:
            run = int(parts[2])
        except ValueError as error:
            raise DispositionRefusal(
                "PP-W historical liability disposition: registered slot run differs") from error
        parsed.append((parts[0], parts[1], run))
    tasks = [parsed[index][0] for index in range(0, 6, 2)]
    _require(len(tasks) == 3 and len(set(tasks)) == 3,
             "registered inventory must contain three ordered tasks")
    expected = [
        (task, arm, run)
        for run in range(1, 75)
        for task in tasks
        for arm in ("calor-permissive", "calor-strict")
    ]
    _require(parsed == expected,
             "registered inventory order must be run ordinal, task, permissive/strict")


def _event_detail(event):
    _require(isinstance(event, dict) and set(event) == EVENT_FIELDS,
             "accounting event fields differ")
    _require(type(event["id"]) is int and event["id"] > 0
             and isinstance(event["created"], str) and event["created"],
             "accounting event identity differs")
    return _decode(event["detail"], "accounting event detail")


def _validate_old_snapshot(snapshot, attempts, evidence_mode):
    _require(isinstance(snapshot, dict) and set(snapshot) == SNAPSHOT_FIELDS,
             "old snapshot fields differ")
    _require(snapshot["kind"] == "pp-w-request-gateway-v1"
             and snapshot["state"] == "INCOMPLETE_UNKNOWN_CHARGE"
             and snapshot["ceilingMicroUsd"] == PILOT_CEILING_MICRO_USD
             and snapshot["exposureMicroUsd"] == PERMANENTLY_RETAINED_MICRO_USD
             and snapshot["verdict"] is None
             and snapshot["basis"]
             == "request reservations; only validated complete provider usage permits release",
             "old snapshot financial state differs")
    binding = _binding(snapshot["binding"], "old binding")
    _require(binding["priceSha256"] == HISTORICAL_PRICE_SHA256,
             "old binding price identity differs")
    _require(snapshot["accountedSlots"] == 1
             and snapshot["validCompletedSlots"] == 0
             and snapshot["invalidTerminalSlots"] == 1
             and snapshot["requestBearingSlots"] == 1,
             "old snapshot historical counters differ")
    _require([item["slot"] for item in attempts] == binding["plannedSlots"][:2],
             "preserved attempts are not the first two registered slots")
    rows = snapshot["requests"]
    _require(isinstance(rows, list) and len(rows) == 2
             and all(isinstance(row, dict) and set(row) == REQUEST_FIELDS for row in rows),
             "old snapshot must contain exactly two request rows")
    request_ids = []
    for row in rows:
        _require(row["slot"] == attempts[1]["slot"]
                 and row["reserved"] == HISTORICAL_REQUEST_RESERVATION
                 and row["charge"] == HISTORICAL_REQUEST_RESERVATION
                 and row["state"] == "unknown"
                 and row["reason"] == "unreconciled-provider-charge"
                 and row["usage"] is None,
                 "historical request row liability differs")
        request = _decode(row["request"], "historical request")
        _require(isinstance(request, dict)
                 and request.get("maximumMicroUsd") == HISTORICAL_REQUEST_RESERVATION
                 and request.get("priceSha256") == HISTORICAL_PRICE_SHA256,
                 "historical request reservation identity differs")
        _require(isinstance(row["id"], str) and row["id"], "historical request id differs")
        request_ids.append(row["id"])
    _require(len(set(request_ids)) == 2, "historical request ids are duplicated")
    if evidence_mode == "registered-operator":
        _require(tuple(request_ids) == HISTORICAL_REQUEST_IDS,
                 "registered historical request ids differ")
        _require(binding["epochId"] == OLD_EPOCHS[1],
                 "registered old scope epoch differs")
        _require(binding["plannedSlots"][0]
                 == "C-001-quota-adapter/calor-permissive/1"
                 and binding["plannedSlots"][1]
                 == "C-001-quota-adapter/calor-strict/1"
                 and binding["plannedSlots"][2]
                 == "C-002-shipping-quote/calor-permissive/1",
                 "registered first-slot order differs")
    events = snapshot["events"]
    _require(isinstance(events, list) and len(events) == 9,
             "old ledger must contain exactly nine events")
    _require([event.get("id") for event in events] == list(range(1, 10)),
             "old event sequence differs")
    expected_kinds = [
        "initialized", "started", "stopped", "zero-request-recovery", "started",
        "reserved-before-upstream", "reserved-before-upstream",
        "unknown-charge-retained", "unknown-charge-retained",
    ]
    _require([event.get("kind") for event in events] == expected_kinds,
             "old event kinds differ")
    details = [_event_detail(event) for event in events]
    _require(events[0]["request_id"] is None
             and details[0] == {"ceilingMicroUsd": PILOT_CEILING_MICRO_USD}
             and events[1]["request_id"] is None and details[1] == {}
             and events[2]["request_id"] is None
             and details[2] == {"reason": "INCOMPLETE_POLICY"}
             and events[3]["request_id"] is None
             and details[3].get("preservedAttemptedSlots") == [attempts[0]["slot"]]
             and events[4]["request_id"] is None and details[4] == {},
             "old recovery/start prefix differs")
    _require([events[index]["request_id"] for index in (5, 6)] == request_ids
             and all(details[index] == {
                 "maximumMicroUsd": HISTORICAL_REQUEST_RESERVATION
             } for index in (5, 6))
             and [events[index]["request_id"] for index in (7, 8)] == request_ids
             and all(details[index] == {} for index in (7, 8)),
             "old reservation/unknown event linkage differs")
    return binding, request_ids


def validate_authorization(value):
    """Validate the complete registered grant/evidence value without file-system access."""
    _require(isinstance(value, dict) and set(value) == AUTHORIZATION_FIELDS,
             "authorization fields differ")
    _require(value["schemaVersion"] == 1 and value["kind"] == DISPOSITION_KIND,
             "authorization kind or version differs")
    mode = value["evidenceMode"]
    _require(mode in ("registered-operator", "SYNTHETIC_TEST_ONLY"),
             "authorization evidence mode differs")
    grant = value["grant"]
    _require(isinstance(grant, dict) and set(grant) == GRANT_FIELDS
             and grant["quote"] == GRANT_QUOTE
             and grant["authorizedAt"] == GRANT_TIME,
             "authorization grant differs")
    if mode == "registered-operator":
        _require(grant["reference"] == GRANT_REFERENCE,
                 "registered authorization reference differs")
        _require(value["oldLedgerSha256"] == EXPECTED_OLD_LEDGER_SHA256
                 and value["oldBackupSha256"] == EXPECTED_OLD_BACKUP_SHA256,
                 "registered immutable ledger identities differ")
        _require(value["targetEpochId"] == TARGET_EPOCH,
                 "registered target epoch differs")
    else:
        _require(grant["reference"].startswith("SYNTHETIC://"),
                 "synthetic authorization is not visibly test-only")
    _sha256(value["oldLedgerSha256"], "old ledger hash")
    _sha256(value["oldBackupSha256"], "old backup hash")
    _sha256(value["targetPriceSha256"], "target price hash")
    _require(value["permanentlyRetainedMicroUsd"] == PERMANENTLY_RETAINED_MICRO_USD
             and value["actualCost"] is None and value["futureUnknownPolicy"] == "halt",
             "permanent unknown-liability policy differs")
    archives = value["predecessorArchives"]
    _require(isinstance(archives, list) and len(archives) == 2,
             "exactly two predecessor archives are required")
    for index, archive in enumerate(archives):
        _require(isinstance(archive, dict) and set(archive) == ARCHIVE_FIELDS
                 and archive["archiveRole"] == ARCHIVE_ROLES[index]
                 and isinstance(archive["epochId"], str) and archive["epochId"],
                 "predecessor archive identity differs")
        for name in ("pathSha256", "inventorySha256"):
            _sha256(archive[name], "predecessor archive " + name)
        _relative_path(archive["pinsPath"], "predecessor archive pinsPath")
        _sha256(archive["pinsSha256"], "predecessor archive pinsSha256")
        _require(type(archive["fileCount"]) is int and archive["fileCount"] > 0
                 and type(archive["byteCount"]) is int and archive["byteCount"] >= 0,
                 "predecessor archive size identity differs")
    if mode == "registered-operator":
        _require(tuple(item["epochId"] for item in archives) == OLD_EPOCHS,
                 "registered predecessor epochs differ")
    attempts = value["preservedAttempts"]
    _require(isinstance(attempts, list) and len(attempts) == 2,
             "exactly two preserved attempts are required")
    for index, attempt in enumerate(attempts):
        _require(isinstance(attempt, dict) and set(attempt) == ATTEMPT_FIELDS
                 and attempt["classification"] == ATTEMPT_CLASSIFICATIONS[index]
                 and attempt["archiveRole"] == ARCHIVE_ROLES[index]
                 and attempt["epochId"] == archives[index]["epochId"]
                 and isinstance(attempt["slot"], str) and attempt["slot"],
                 "preserved attempt identity differs")
        for name in ("rawRecordPath", "clientInvocationPath", "invalidReasonPath"):
            _relative_path(attempt[name], "preserved attempt " + name)
        for name in ("rawRecordSha256", "clientInvocationSha256", "invalidReasonSha256"):
            _sha256(attempt[name], "preserved attempt " + name)
        _require((attempt["attemptStartPath"] is None)
                 == (attempt["attemptStartSha256"] is None),
                 "attempt-start path/hash nullability differs")
        if attempt["attemptStartPath"] is not None:
            _relative_path(attempt["attemptStartPath"], "preserved attempt attemptStartPath")
            _sha256(attempt["attemptStartSha256"], "preserved attempt attemptStartSha256")
        sources = attempt["sourceHashes"]
        _require(isinstance(sources, dict) and sources
                 and all(_relative_path(path, "preserved source path") and _valid_sha(digest)
                         for path, digest in sources.items()),
                 "preserved attempt source hashes differ")
    binding, request_ids = _validate_old_snapshot(value["oldSnapshot"], attempts, mode)
    _require(value["targetEpochId"] != binding["epochId"],
             "target epoch must differ from the old scope")
    artifacts = value["targetHarnessArtifacts"]
    _require(isinstance(artifacts, dict) and artifacts
             and all(isinstance(name, str) and name and _valid_sha(digest)
                     for name, digest in artifacts.items()),
             "target harness artifact identities differ")
    markers = value["quiescenceProcessMarkers"]
    _require(isinstance(markers, list) and markers
             and all(isinstance(marker, str) and marker for marker in markers)
             and len(set(markers)) == len(markers),
             "quiescence process markers differ")
    return {
        "oldBinding": binding,
        "historicalRequestIds": request_ids,
        "preservedSlots": [attempt["slot"] for attempt in attempts],
    }


def _validate_target_binding(target, authorization, authorization_sha256):
    values = validate_authorization(authorization)
    target = _binding(target, "target binding")
    old = values["oldBinding"]
    _sha256(authorization_sha256, "authorization file hash")
    _require(_sha256_bytes(authorization_bytes(authorization)) == authorization_sha256,
             "authorization bytes differ from their registered identity")
    _require(target["epochId"] == authorization["targetEpochId"]
             and target["priceSha256"] == authorization["targetPriceSha256"]
             and target["authorizationSha256"] == authorization_sha256
             and target["harnessArtifacts"] == authorization["targetHarnessArtifacts"],
             "target binding differs from authorization")
    _require(target["priceSha256"] == _budget().price_identity(),
             "target price identity differs from the implemented prospective contract")
    _require(target["plannedSlots"] == old["plannedSlots"]
             and target["stage"] == old["stage"],
             "target changed the registered 444-slot inventory")
    _require(target["protocolSha256"] != old["protocolSha256"]
             and target["planSha256"] != old["planSha256"],
             "target must bind distinct reviewed protocol and plan identities")
    return values


def _path_parts_are_unlinked(path):
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        if current.is_symlink():
            return False
    return True


def _absolute(value, description, must_exist):
    path = Path(value)
    _require(path.is_absolute() and ".." not in path.parts,
             description + " must be absolute and canonical")
    _require(_path_parts_are_unlinked(path), description + " contains a symlink")
    if must_exist:
        _require(path.exists(), description + " does not exist")
    else:
        _require(not path.is_symlink(), description + " is a symlink")
    return path


def _regular_single_link(path, description):
    info = os.stat(path, follow_symlinks=False)
    _require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1,
             description + " must be a single-link regular file")
    return info


def _contained(path, root):
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _sidecars(path):
    return [Path(str(path) + suffix) for suffix in ("-journal", "-wal", "-shm")]


def _require_no_sidecars(path):
    _require(not any(item.exists() or item.is_symlink() for item in _sidecars(path)),
             "SQLite journal or WAL sidecar is present")


def _hash_regular_file(path, description):
    before = _regular_single_link(path, description)
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
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
            item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns, item.st_ctime_ns)
        _require(identity(opened) == identity(after) == identity(current),
                 description + " changed while hashing")
        return digest.hexdigest(), size
    finally:
        os.close(descriptor)


def archive_inventory(path):
    """Hash an archive without interpreting candidate, transcript, or source contents."""
    root = _absolute(path, "archive", True)
    _require(root.is_dir(), "archive is not a directory")
    entries = []
    directory_states = {}
    for directory, names, files in os.walk(root, topdown=True, followlinks=False):
        current = Path(directory)
        info = os.stat(current, follow_symlinks=False)
        _require(stat.S_ISDIR(info.st_mode), "archive contains a non-directory")
        directory_states[current] = (
            info.st_dev, info.st_ino, info.st_mtime_ns, info.st_ctime_ns)
        for name in names:
            _require(not (current / name).is_symlink(), "archive contains a linked directory")
        for name in files:
            child = current / name
            _require(not child.is_symlink(), "archive contains a linked file")
            relative = child.relative_to(root).as_posix()
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


def _validate_paths(ledger_path, original_archive, failed_archive, old_backup_path,
                    new_backup_path, protected_root, evidence_mode):
    ledger = _absolute(ledger_path, "ledger path", True)
    original = _absolute(original_archive, "original archive", True)
    failed = _absolute(failed_archive, "failed archive", True)
    old_backup = _absolute(old_backup_path, "old backup", True)
    new_backup = _absolute(new_backup_path, "new backup", False)
    protected = _absolute(protected_root, "protected root", True)
    _require(original.is_dir() and failed.is_dir() and original != failed,
             "predecessor archives must be distinct directories")
    _require(protected.is_dir() and _contained(ledger, protected)
             and _contained(old_backup, protected)
             and new_backup.parent == protected,
             "ledger and backups must be direct protected state")
    _regular_single_link(ledger, "ledger")
    _regular_single_link(old_backup, "old backup")
    _require(new_backup != ledger and new_backup != old_backup and not new_backup.exists(),
             "new backup path is reused or aliases immutable state")
    if evidence_mode == "SYNTHETIC_TEST_ONLY":
        _require("SYNTHETIC" in str(protected),
                 "synthetic authorization may operate only on visibly synthetic test state")
    return ledger, original, failed, old_backup, new_backup, protected


def _open_database(path, mode):
    uri = "file:" + quote(str(path), safe="/") + "?mode=" + mode
    db = sqlite3.connect(uri, uri=True, timeout=0, isolation_level=None)
    db.row_factory = sqlite3.Row
    db.execute("PRAGMA query_only=" + ("ON" if mode == "ro" else "OFF"))
    db.execute("PRAGMA busy_timeout=0")
    return db


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
    scopes = db.execute("SELECT * FROM scope ORDER BY id").fetchall()
    _require(len(scopes) == 1 and scopes[0]["id"] == 1, "ledger scope row differs")
    return (
        dict(scopes[0]),
        [dict(row) for row in db.execute(
            "SELECT id,slot,request,reserved,charge,state,reason,usage FROM requests ORDER BY rowid")],
        [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")],
    )


def _snapshot(scope, requests, events):
    binding = _decode(scope["binding"], "scope binding")
    result = {
        "kind": "pp-w-request-gateway-v1",
        "state": scope["state"],
        "binding": binding,
        "ceilingMicroUsd": scope["ceiling"],
        "exposureMicroUsd": sum(row["charge"] for row in requests),
        "requests": requests,
        "events": events,
        "verdict": None,
        "basis": "request reservations; only validated complete provider usage permits release",
    }
    preserved = []
    valid = 0
    invalid = 0
    historical_disposition = False
    for event in events:
        if event["kind"] == "zero-request-recovery":
            preserved = _event_detail(event).get("preservedAttemptedSlots", [])
        elif event["kind"] == DISPOSITION_EVENT:
            preserved = validate_authorization(
                _event_detail(event)["authorization"])["preservedSlots"]
            historical_disposition = True
        elif event["kind"] == "slot-complete":
            valid += 1
        elif event["kind"] == "slot-terminal-invalid":
            invalid += 1
    result.update(
        accountedSlots=len(preserved) + valid + invalid,
        validCompletedSlots=valid,
        invalidTerminalSlots=(1 if historical_disposition else len(preserved)) + invalid,
        requestBearingSlots=len({row["slot"] for row in requests}),
    )
    return result


def _archive_entry(inventory, relative):
    return next((item for item in inventory["files"] if item["path"] == relative), None)


def _validate_attempt_files(root, inventory, attempt):
    identities = {}
    for path_name, hash_name in (
            ("rawRecordPath", "rawRecordSha256"),
            ("clientInvocationPath", "clientInvocationSha256"),
            ("invalidReasonPath", "invalidReasonSha256"),
            ("attemptStartPath", "attemptStartSha256")):
        relative = attempt[path_name]
        expected = attempt[hash_name]
        if relative is None:
            continue
        entry = _archive_entry(inventory, relative)
        _require(entry is not None and entry["sha256"] == expected,
                 "preserved attempt artifact changed: " + relative)
        current, _ = _hash_regular_file(root / relative, "preserved attempt " + relative)
        _require(current == expected, "preserved attempt artifact changed after inventory")
        identities[path_name] = expected
    for relative, expected in attempt["sourceHashes"].items():
        entry = _archive_entry(inventory, relative)
        _require(entry is not None and entry["sha256"] == expected,
                 "preserved source changed: " + relative)
    reason = (root / attempt["invalidReasonPath"]).read_text(encoding="utf-8").lower()
    if attempt["classification"] == "HISTORICAL_RAW_API_INVALID_CENSORED":
        _require("actual api" in reason and "invalid" in reason and "censor" in reason,
                 "epoch 002 invalid reason is not the pinned actual-API raw invalid/censored reason")
    return identities


def _validate_archive_pins(root, inventory, expected, attempt):
    entry = _archive_entry(inventory, expected["pinsPath"])
    _require(entry is not None and entry["sha256"] == expected["pinsSha256"],
             "predecessor archive pins changed")
    raw = (root / expected["pinsPath"]).read_bytes()
    _require(_sha256_bytes(raw) == expected["pinsSha256"],
             "predecessor archive pins changed after inventory")
    pins = _decode(raw, "predecessor archive pins")
    _require(isinstance(pins, dict)
             and pins.get("epochId") == expected["epochId"]
             and pins.get("stage") == "pilot"
             and isinstance(pins.get("harnessArtifacts"), dict),
             "predecessor archive pins do not identify the preserved epoch")
    pinned_hashes = set(pins["harnessArtifacts"].values())
    _require(set(attempt["sourceHashes"].values()) <= pinned_hashes,
             "preserved attempt sources are not pinned by the predecessor archive")
    return expected["pinsSha256"]


def _trusted_launcher_sources(target_binding):
    root = Path(__file__).resolve().parent
    result = {}
    for name in TRUSTED_LAUNCHER_SOURCES:
        digest, _ = _hash_regular_file(root / name, "trusted launcher source " + name)
        _require(target_binding["harnessArtifacts"].get(name) == digest,
                 "target binding does not pin trusted launcher source " + name)
        result[name] = digest
    return result


def _descriptor_quiescence(criteria, first_collector_start=None, native_executable=None):
    uid = os.getuid()
    command = ["/usr/sbin/lsof", "-nP", "-a", "-u", str(uid), "-d", "cwd,txt", "-F0pfn"]
    try:
        result = subprocess.run(command, capture_output=True, timeout=20, check=True)
    except (OSError, subprocess.SubprocessError) as error:
        raise DispositionRefusal("current process descriptor inspection failed") from error
    _require(not result.stderr, "current process descriptor inspection was incomplete")
    pid = None
    descriptor = None
    seen = set()
    directories = set()
    native_processes = set()
    for raw in result.stdout.split(b"\0"):
        raw = raw.lstrip(b"\n")
        if not raw:
            continue
        try:
            tag, value = raw[:1], raw[1:].decode("utf-8", errors="strict")
        except UnicodeDecodeError as error:
            raise DispositionRefusal("process descriptor metadata is not valid UTF-8") from error
        if tag == b"p":
            _require(value.isdecimal(), "malformed descriptor process identity")
            pid, descriptor = int(value), None
            seen.add(pid)
        elif tag == b"f":
            _require(pid is not None and value in ("cwd", "txt"),
                     "unexpected process descriptor category")
            descriptor = value
        elif tag == b"n":
            _require(pid is not None and descriptor is not None,
                     "process descriptor path lacks an identity")
            _require(value.startswith("/"), "process descriptor path is unavailable")
            if descriptor == "cwd":
                directories.add(pid)
            matching = [marker for marker in criteria if marker in value]
            if matching and value == native_executable and matching == [native_executable]:
                native_processes.add(pid)
            else:
                _require(not matching,
                         "active historical workspace or pinned native executable remains")
        else:
            raise DispositionRefusal("unexpected process descriptor metadata")
    _require(seen and seen == directories,
             "process descriptor inspection lacks working-directory coverage")
    if native_processes:
        from datetime import datetime, timezone
        _require(isinstance(first_collector_start, str), "historical process cutoff is missing")
        cutoff = datetime.fromisoformat(first_collector_start.replace("Z", "+00:00"))
        _require(cutoff.tzinfo is not None, "historical process cutoff lacks a timezone")
        for native_pid in sorted(native_processes):
            try:
                started = subprocess.run(
                    ["/bin/ps", "-p", str(native_pid), "-o", "lstart="],
                    capture_output=True, text=True, check=True, timeout=10,
                    env=dict(os.environ, LC_ALL="C"))
                birth = datetime.strptime(
                    started.stdout.strip(), "%a %b %d %H:%M:%S %Y").astimezone(timezone.utc)
            except (OSError, subprocess.SubprocessError, ValueError) as error:
                raise DispositionRefusal("native process start metadata is unavailable") from error
            _require(not started.stderr and (cutoff - birth).total_seconds() > 2,
                     "a pinned native process may belong to the historical collection")
    return {
        "kind": "pp-w-current-user-cwd-and-text-descriptors-v1",
        "uid": uid,
        "method": "/usr/sbin/lsof -nP -a -u <uid> -d cwd,txt -F0pfn",
        "matchedProcesses": [],
        "preexistingNativePolicy": "kernel start before first collector start; no protected cwd/text match",
        "firstCollectorStart": first_collector_start,
    }


def _quiescence_probe(paths, markers, inspect_descriptors=False, first_collector_start=None,
                      native_executable=None):
    criteria = sorted(set(
        [str(path) for path in paths]
        + list(markers)
        + ([native_executable] if inspect_descriptors and native_executable else [])
        + ["ppw-instrument.py", "ppw-budget-gateway.py", "ppw-gateway-client.py", "run-pair.sh"]))
    try:
        completed = subprocess.run(
            ["/bin/ps", "-axww", "-o", "pid=,ppid=,pgid=,command="],
            check=True, capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.SubprocessError) as error:
        raise DispositionRefusal(
            "PP-W historical liability disposition: OS process metadata probe failed") from error
    rows = []
    for line in completed.stdout.splitlines():
        if not line.strip():
            continue
        parts = line.strip().split(None, 3)
        _require(len(parts) == 4, "OS process metadata is incomplete")
        try:
            pid, ppid, pgid = (int(parts[0]), int(parts[1]), int(parts[2]))
        except ValueError as error:
            raise DispositionRefusal(
                "PP-W historical liability disposition: malformed OS process identity") from error
        _require(pid >= 0 and ppid >= 0 and pgid >= 0, "invalid OS process identity")
        rows.append((pid, ppid, pgid, parts[3]))
    matches = []
    for pid, ppid, pgid, command in rows:
        matched = [item for item in criteria if item in command]
        if matched:
            matches.append({
                "pid": pid, "ppid": ppid, "pgid": pgid,
                "matchedCriteriaSha256": _sha256_json(matched),
                "commandSha256": _sha256_bytes(command.encode("utf-8")),
            })
    _require(not matches,
             "active collector/gateway/native/workspace process metadata remains")
    return {
        "kind": "pp-w-current-process-quiescence-v1",
        "method": "/bin/ps -axww -o pid=,ppid=,pgid=,command=",
        "criteriaSha256": _sha256_json(criteria),
        "matchedProcesses": [],
        "descriptorInspection": _descriptor_quiescence(
            criteria, first_collector_start, native_executable) if inspect_descriptors else {
            "kind": "SYNTHETIC-not-operational-descriptor-evidence",
        },
    }


def _proof_payload(authorization, authorization_sha256, target_binding, ledger, original,
                   failed, old_backup, new_backup, protected, inventories,
                   attempt_evidence, quiescence, launcher_sources):
    values = validate_authorization(authorization)
    return {
        "schemaVersion": 1,
        "kind": DISPOSITION_KIND,
        "authorizationSha256": authorization_sha256,
        "authorizationCanonicalSha256": _sha256_json(authorization),
        "oldLedgerSha256": authorization["oldLedgerSha256"],
        "oldBackupSha256": authorization["oldBackupSha256"],
        "oldBindingSha256": _sha256_json(values["oldBinding"]),
        "targetBindingSha256": _sha256_json(target_binding),
        "predecessorArchiveInventories": inventories,
        "preservedAttemptEvidence": attempt_evidence,
        "trustedLauncherSources": launcher_sources,
        "quiescence": quiescence,
        "ledgerPathSha256": _sha256_bytes(str(ledger).encode("utf-8")),
        "originalArchivePathSha256": _sha256_bytes(str(original).encode("utf-8")),
        "failedArchivePathSha256": _sha256_bytes(str(failed).encode("utf-8")),
        "oldBackupPathSha256": _sha256_bytes(str(old_backup).encode("utf-8")),
        "newBackupPathSha256": _sha256_bytes(str(new_backup).encode("utf-8")),
        "protectedRootPathSha256": _sha256_bytes(str(protected).encode("utf-8")),
        "oldEventCount": 9,
        "historicalRequestIds": values["historicalRequestIds"],
        "sourceEpochIds": [attempt["epochId"] for attempt in authorization["preservedAttempts"]],
        "preservedSlots": values["preservedSlots"],
        "remainingSlotCount": 442,
        "permanentlyRetainedMicroUsd": PERMANENTLY_RETAINED_MICRO_USD,
        "actualCost": None,
        "futureUnknownPolicy": "halt",
    }


def _inspect_locked(db, authorization, authorization_sha256, target_binding, ledger,
                    original, failed, old_backup, new_backup, protected):
    if authorization["evidenceMode"] == "SYNTHETIC_TEST_ONLY":
        _require(all("SYNTHETIC" in str(path) for path in (
            ledger, original, failed, old_backup, new_backup, protected)),
            "synthetic disposition requires explicitly synthetic paths")
    _require(db.execute("PRAGMA journal_mode").fetchone()[0].lower() == "delete",
             "ledger is not in rollback-journal DELETE mode")
    _require_no_sidecars(ledger)
    scope, requests, events = _database_snapshot(db)
    _require(scope["state"] == "INCOMPLETE_UNKNOWN_CHARGE"
             and isinstance(scope["owner"], str) and scope["owner"],
             "old scope state/owner differs or disposition was already applied")
    observed = _snapshot(scope, requests, events)
    _require(observed == authorization["oldSnapshot"],
             "old normalized spending snapshot changed")
    ledger_sha256, _ = _hash_regular_file(ledger, "ledger")
    _require(ledger_sha256 == authorization["oldLedgerSha256"],
             "old ledger source hash changed")
    old_backup_sha256, _ = _hash_regular_file(old_backup, "old backup")
    _require(old_backup_sha256 == authorization["oldBackupSha256"],
             "immutable old backup changed")
    paths = (original, failed)
    inventories = []
    attempt_evidence = []
    for index, path in enumerate(paths):
        expected = authorization["predecessorArchives"][index]
        _require(_sha256_bytes(str(path).encode("utf-8")) == expected["pathSha256"],
                 "predecessor archive path identity differs")
        inventory = archive_inventory(path)
        _require(inventory["sha256"] == expected["inventorySha256"]
                 and inventory["fileCount"] == expected["fileCount"]
                 and inventory["byteCount"] == expected["byteCount"],
                 "predecessor archive inventory changed")
        inventories.append({
            "archiveRole": expected["archiveRole"],
            "epochId": expected["epochId"],
            "inventorySha256": inventory["sha256"],
            "fileCount": inventory["fileCount"],
            "byteCount": inventory["byteCount"],
            "pinsSha256": _validate_archive_pins(
                path, inventory, expected, authorization["preservedAttempts"][index]),
        })
        attempt_evidence.append({
            "slot": authorization["preservedAttempts"][index]["slot"],
            "epochId": authorization["preservedAttempts"][index]["epochId"],
            "classification": authorization["preservedAttempts"][index]["classification"],
            "artifactHashes": _validate_attempt_files(
                path, inventory, authorization["preservedAttempts"][index]),
        })
    launcher_sources = _trusted_launcher_sources(target_binding)
    quiescence = _quiescence_probe(
        (ledger, original, failed, protected), authorization["quiescenceProcessMarkers"],
        inspect_descriptors=authorization["evidenceMode"] == "registered-operator",
        first_collector_start=authorization["oldSnapshot"]["events"][1]["created"],
        native_executable="/Users/juanrivera/.local/share/claude/versions/2.1.266")
    return _proof_payload(
        authorization, authorization_sha256, target_binding, ledger, original, failed,
        old_backup, new_backup, protected, inventories, attempt_evidence, quiescence,
        launcher_sources)


def inspect_disposition(
        ledger_path, *, authorization, authorization_sha256, target_binding,
        original_archive, failed_archive, old_backup_path, new_backup_path, protected_root):
    """Read-only proof of the exact old liability, evidence, paths, and current quiescence."""
    _validate_target_binding(target_binding, authorization, authorization_sha256)
    paths = _validate_paths(
        ledger_path, original_archive, failed_archive, old_backup_path,
        new_backup_path, protected_root, authorization["evidenceMode"])
    ledger, original, failed, old_backup, new_backup, protected = paths
    _require_no_sidecars(ledger)
    db = _open_database(ledger, "ro")
    try:
        db.execute("BEGIN")
        proof = _inspect_locked(
            db, authorization, authorization_sha256, target_binding, ledger, original,
            failed, old_backup, new_backup, protected)
        db.execute("ROLLBACK")
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    _require_no_sidecars(ledger)
    proof["proofSha256"] = _sha256_json(proof)
    return proof


def _copy_backup(ledger, protected, backup, expected_sha256):
    _require(not backup.exists(), "new backup path was already used")
    root_descriptor = os.open(protected, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    source_descriptor = os.open(ledger, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    destination_descriptor = None
    try:
        source_info = os.fstat(source_descriptor)
        _require(source_info.st_nlink == 1 and stat.S_ISREG(source_info.st_mode),
                 "ledger changed before backup")
        destination_descriptor = os.open(
            backup.name,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            0o600, dir_fd=root_descriptor)
        os.fchmod(destination_descriptor, 0o600)
        digest = hashlib.sha256()
        while True:
            block = os.read(source_descriptor, 1024 * 1024)
            if not block:
                break
            digest.update(block)
            remaining = memoryview(block)
            while remaining:
                remaining = remaining[os.write(destination_descriptor, remaining):]
        os.fsync(destination_descriptor)
        _require(digest.hexdigest() == expected_sha256,
                 "ledger changed while creating new backup")
        os.fsync(root_descriptor)
    finally:
        if destination_descriptor is not None:
            os.close(destination_descriptor)
        os.close(source_descriptor)
        os.close(root_descriptor)
    observed, _ = _hash_regular_file(backup, "new backup")
    _require(observed == expected_sha256
             and stat.S_IMODE(os.stat(backup, follow_symlinks=False).st_mode) == 0o600,
             "new backup verification failed")


def _disposition_detail(authorization, authorization_sha256, proof):
    return {
        "schemaVersion": 1,
        "kind": DISPOSITION_KIND,
        "authorization": authorization,
        "authorizationSha256": authorization_sha256,
        "proof": proof,
    }


def apply_disposition(
        ledger_path, *, authorization, authorization_sha256, target_binding,
        original_archive, failed_archive, old_backup_path, new_backup_path, protected_root,
        confirmed_proof_sha256):
    """Apply one reviewed proof; never creates an epoch or starts a collector."""
    _validate_target_binding(target_binding, authorization, authorization_sha256)
    _sha256(confirmed_proof_sha256, "confirmed proof hash")
    paths = _validate_paths(
        ledger_path, original_archive, failed_archive, old_backup_path,
        new_backup_path, protected_root, authorization["evidenceMode"])
    ledger, original, failed, old_backup, new_backup, protected = paths
    _require_no_sidecars(ledger)
    db = _open_database(ledger, "rw")
    committed = False
    try:
        try:
            db.execute("PRAGMA synchronous=FULL")
            db.execute("BEGIN IMMEDIATE")
        except sqlite3.OperationalError as error:
            raise DispositionRefusal(
                "PP-W historical liability disposition: concurrent ledger writer "
                "or unavailable lock") from error
        proof = _inspect_locked(
            db, authorization, authorization_sha256, target_binding, ledger, original,
            failed, old_backup, new_backup, protected)
        proof["proofSha256"] = _sha256_json(proof)
        _require(proof["proofSha256"] == confirmed_proof_sha256,
                 "reviewed proof does not match fresh apply evidence")
        _copy_backup(ledger, protected, new_backup, authorization["oldLedgerSha256"])
        _require(_hash_regular_file(old_backup, "old backup")[0]
                 == authorization["oldBackupSha256"], "immutable old backup changed during apply")
        _require(archive_inventory(original)["sha256"]
                 == authorization["predecessorArchives"][0]["inventorySha256"]
                 and archive_inventory(failed)["sha256"]
                 == authorization["predecessorArchives"][1]["inventorySha256"],
                 "predecessor evidence changed during apply")
        cursor = db.execute(
            "UPDATE scope SET binding=?,state='ready',owner=NULL "
            "WHERE id=1 AND binding=? AND ceiling=? AND state='INCOMPLETE_UNKNOWN_CHARGE'",
            (canonical(target_binding), canonical(authorization["oldSnapshot"]["binding"]),
             PILOT_CEILING_MICRO_USD))
        _require(cursor.rowcount == 1, "old scope changed before disposition")
        db.execute(
            "INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
            (DISPOSITION_EVENT, None, canonical(
                _disposition_detail(authorization, authorization_sha256, proof))))
        _require(db.execute("SELECT last_insert_rowid()").fetchone()[0] == 10,
                 "disposition did not append exact event 10")
        db.execute("COMMIT")
        committed = True
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    _require(committed, "disposition transaction did not commit")
    return {
        **proof,
        "applied": True,
        "dispositionEventId": 10,
        "newBackupSha256": authorization["oldLedgerSha256"],
    }


_BUDGET = None


def _budget():
    global _BUDGET
    if _BUDGET is None:
        path = Path(__file__).with_name("ppw-gateway-budget.py")
        spec = importlib.util.spec_from_file_location("ppw_disposition_budget", path)
        _BUDGET = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_BUDGET)
    return _BUDGET


def _validate_disposition_event(event, authorization, authorization_sha256, target_binding):
    detail = _event_detail(event)
    _require(event["kind"] == DISPOSITION_EVENT and event["request_id"] is None
             and isinstance(detail, dict) and set(detail) == {
                 "schemaVersion", "kind", "authorization", "authorizationSha256", "proof"}
             and type(detail["schemaVersion"]) is int and detail["schemaVersion"] == 1
             and detail["kind"] == DISPOSITION_KIND
             and canonical(detail["authorization"]) == canonical(authorization)
             and detail["authorizationSha256"] == authorization_sha256,
             "disposition event evidence differs")
    proof = detail["proof"]
    _require(isinstance(proof, dict), "disposition proof is not an object")
    values = validate_authorization(authorization)
    archives = authorization["predecessorArchives"]
    attempts = authorization["preservedAttempts"]
    path_fields = (
        "ledgerPathSha256", "originalArchivePathSha256", "failedArchivePathSha256",
        "oldBackupPathSha256", "newBackupPathSha256", "protectedRootPathSha256",
    )
    paths = {name: _sha256(proof.get(name), "disposition proof " + name)
             for name in path_fields}
    _require(paths["originalArchivePathSha256"] == archives[0]["pathSha256"]
             and paths["failedArchivePathSha256"] == archives[1]["pathSha256"],
             "disposition proof predecessor archive paths differ")
    quiescence = proof.get("quiescence")
    _require(isinstance(quiescence, dict), "disposition quiescence is not an object")
    criteria_sha256 = _sha256(
        quiescence.get("criteriaSha256"), "disposition quiescence criteria")
    descriptor_evidence = {"kind": "SYNTHETIC-not-operational-descriptor-evidence"}
    if authorization["evidenceMode"] == "registered-operator":
        descriptor = quiescence.get("descriptorInspection")
        _require(isinstance(descriptor, dict) and type(descriptor.get("uid")) is int
                 and descriptor["uid"] >= 0, "descriptor inspection user identity differs")
        descriptor_evidence = {
            "kind": "pp-w-current-user-cwd-and-text-descriptors-v1",
            "uid": descriptor["uid"],
            "method": "/usr/sbin/lsof -nP -a -u <uid> -d cwd,txt -F0pfn",
            "matchedProcesses": [],
            "preexistingNativePolicy":
                "kernel start before first collector start; no protected cwd/text match",
            "firstCollectorStart": authorization["oldSnapshot"]["events"][1]["created"],
        }
    sources = {
        name: target_binding["harnessArtifacts"].get(name)
        for name in TRUSTED_LAUNCHER_SOURCES
    }
    _require(all(_valid_sha(digest) for digest in sources.values()),
             "target binding lacks trusted launcher sources")
    expected = {
        "schemaVersion": 1,
        "kind": DISPOSITION_KIND,
        "authorizationSha256": authorization_sha256,
        "authorizationCanonicalSha256": _sha256_json(authorization),
        "oldLedgerSha256": authorization["oldLedgerSha256"],
        "oldBackupSha256": authorization["oldBackupSha256"],
        "oldBindingSha256": _sha256_json(values["oldBinding"]),
        "targetBindingSha256": _sha256_json(target_binding),
        "predecessorArchiveInventories": [
            {name: archive[name] for name in (
                "archiveRole", "epochId", "inventorySha256", "fileCount", "byteCount",
                "pinsSha256")}
            for archive in archives
        ],
        "preservedAttemptEvidence": [
            {
                "slot": attempt["slot"],
                "epochId": attempt["epochId"],
                "classification": attempt["classification"],
                "artifactHashes": {
                    path_name: attempt[hash_name]
                    for path_name, hash_name in (
                        ("rawRecordPath", "rawRecordSha256"),
                        ("clientInvocationPath", "clientInvocationSha256"),
                        ("invalidReasonPath", "invalidReasonSha256"),
                        ("attemptStartPath", "attemptStartSha256"))
                    if attempt[path_name] is not None
                },
            }
            for attempt in attempts
        ],
        "trustedLauncherSources": sources,
        "quiescence": {
            "kind": "pp-w-current-process-quiescence-v1",
            "method": "/bin/ps -axww -o pid=,ppid=,pgid=,command=",
            "criteriaSha256": criteria_sha256,
            "matchedProcesses": [],
            "descriptorInspection": descriptor_evidence,
        },
        **paths,
        "oldEventCount": 9,
        "historicalRequestIds": values["historicalRequestIds"],
        "sourceEpochIds": [attempt["epochId"] for attempt in attempts],
        "preservedSlots": values["preservedSlots"],
        "remainingSlotCount": 442,
        "permanentlyRetainedMicroUsd": PERMANENTLY_RETAINED_MICRO_USD,
        "actualCost": None,
        "futureUnknownPolicy": "halt",
    }
    proof_without_hash = dict(proof)
    proof_sha = proof_without_hash.pop("proofSha256", None)
    # Canonical JSON equality distinguishes integers from booleans/floats at every depth.
    _require(canonical(proof_without_hash) == canonical(expected),
             "disposition proof fields differ")
    _require(_valid_sha(proof_sha) and proof_sha == _sha256_json(proof_without_hash),
             "disposition proof hash differs")
    return proof


def _reconciled_row(row, target_binding):
    _require(isinstance(row, dict) and set(row) == REQUEST_FIELDS
             and type(row["reserved"]) is int and type(row["charge"]) is int
             and 0 <= row["charge"] <= row["reserved"]
             and row["state"] == "reconciled" and row["reason"] is None
             and isinstance(row["usage"], str),
             "future request lacks complete reconciled usage")
    request = _decode(row["request"], "future request")
    usage = _decode(row["usage"], "future usage")
    _require(isinstance(request, dict) and set(request) == {
        "model", "maxTokens", "stream", "maximumMicroUsd", "priceSha256",
        "betaCapabilities", "serviceTier",
    } and isinstance(request["betaCapabilities"], list)
        and all(isinstance(value, str) for value in request["betaCapabilities"]),
        "future request descriptor fields or capabilities differ")
    _require(isinstance(usage, dict), "future usage is not an object")
    budget = _budget()
    try:
        admitted = budget.admit_request(canonical({
            "model": request["model"],
            "max_tokens": request["maxTokens"],
            "messages": [{"role": "user", "content": "ARCHIVE_SCHEMA_ONLY_NO_INFERENCE"}],
            "stream": request["stream"],
            "service_tier": request["serviceTier"],
        }), ",".join(request["betaCapabilities"]) or None)
        _require(canonical(request) == canonical(admitted)
                 and request["priceSha256"] == target_binding["priceSha256"]
                 and row["reserved"] == admitted["maximumMicroUsd"],
                 "future request reservation differs from admitted target liability")
        cost, receipt = budget.reconciled_cost(
            admitted, usage.get("model"), usage.get("usage"), usage.get("stopReason"))
    except budget.Refusal as error:
        raise DispositionRefusal(
            "PP-W historical liability disposition: future request is not admitted: "
            + str(error)) from error
    _require(row["charge"] == cost and canonical(usage) == canonical(receipt),
             "future reconciled request differs from complete provider usage")


def validate_history(snapshot, *, authorization, authorization_sha256, target_binding):
    """Portable replay of the immutable old prefix, disposition, and all later accounting."""
    values = _validate_target_binding(target_binding, authorization, authorization_sha256)
    _require(isinstance(snapshot, dict)
             and set(snapshot) in (SNAPSHOT_FIELDS, SNAPSHOT_FIELDS | REPORT_FIELDS),
             "live snapshot fields differ")
    _require(snapshot["kind"] == "pp-w-request-gateway-v1"
             and snapshot["binding"] == target_binding
             and snapshot["ceilingMicroUsd"] == PILOT_CEILING_MICRO_USD
             and snapshot["verdict"] is None,
             "live scope identity differs")
    old = authorization["oldSnapshot"]
    events = snapshot["events"]
    requests = snapshot["requests"]
    _require(isinstance(events, list) and len(events) >= 10
             and events[:9] == old["events"]
             and [event.get("id") for event in events] == list(range(1, len(events) + 1)),
             "immutable old event prefix changed or event sequence is not contiguous")
    _require(isinstance(requests, list) and len(requests) >= 2
             and requests[:2] == old["requests"],
             "immutable historical request rows changed")
    _require(sum(1 for event in events if event.get("kind") == DISPOSITION_EVENT) == 1
             and events[9]["kind"] == DISPOSITION_EVENT,
             "disposition must occur exactly once as event 10")
    proof = _validate_disposition_event(
        events[9], authorization, authorization_sha256, target_binding)
    preserved = values["preservedSlots"]
    remaining = [slot for slot in target_binding["plannedSlots"] if slot not in preserved]
    _require(len(remaining) == 442 and target_binding["plannedSlots"][:2] == preserved,
             "target inventory does not preserve exactly the first two attempts")
    future_rows = requests[2:]
    _require(all(isinstance(row, dict) and set(row) == REQUEST_FIELDS for row in future_rows)
             and len({row["id"] for row in requests}) == len(requests)
             and not any(row["slot"] in preserved for row in future_rows)
             and all(row["slot"] in remaining for row in future_rows),
             "future request rows retry or escape the 442 untouched slots")
    row_by_id = {row["id"]: row for row in future_rows}
    started = len(events) > 10
    if started:
        _require(events[10]["kind"] == "started" and events[10]["request_id"] is None
                 and _event_detail(events[10]) == {},
                 "new start must be exact event 11")
    _require(not any(event["kind"] == "started" for event in events[11:]),
             "duplicate new collection start")
    outstanding = set()
    settled = set()
    accounted = []
    valid_slots = []
    invalid_slots = []
    live_unknown_ids = []
    failure_seen = False
    expected_halt_state = None
    completion = None
    for event in events[11:]:
        kind = event["kind"]
        detail = _event_detail(event)
        request_id = event["request_id"]
        if completion is not None:
            _require(False, "event appears after collection completion")
        if kind == "reserved-before-upstream":
            _require(not failure_seen and request_id in row_by_id
                     and request_id not in outstanding and request_id not in settled
                     and detail == {"maximumMicroUsd": row_by_id[request_id]["reserved"]},
                     "future reservation event differs")
            outstanding.add(request_id)
        elif kind == "complete-provider-usage":
            _require(request_id in outstanding and request_id not in settled,
                     "future settlement is missing or duplicated")
            _reconciled_row(row_by_id[request_id], target_binding)
            _require(detail == {
                "conservativeChargeMicroUsd": row_by_id[request_id]["charge"],
                "releasedMicroUsd":
                    row_by_id[request_id]["reserved"] - row_by_id[request_id]["charge"],
            }, "future complete-usage event differs")
            outstanding.remove(request_id)
            settled.add(request_id)
        elif kind == "unknown-charge-retained":
            _require(request_id in outstanding and row_by_id[request_id]["state"] == "unknown"
                     and row_by_id[request_id]["charge"] == row_by_id[request_id]["reserved"]
                     and row_by_id[request_id]["usage"] is None
                     and row_by_id[request_id]["reason"] == "unreconciled-provider-charge",
                     "future unknown charge evidence differs")
            outstanding.remove(request_id)
            live_unknown_ids.append(request_id)
            failure_seen = True
            if expected_halt_state != "INCOMPLETE_BUDGET":
                expected_halt_state = "INCOMPLETE_UNKNOWN_CHARGE"
        elif kind in ("slot-complete", "slot-terminal-invalid"):
            _require(not failure_seen and request_id is None and isinstance(detail.get("slot"), str),
                     "terminal event follows an accounting failure")
            slot = detail["slot"]
            _require(slot not in accounted and slot == remaining[len(accounted)],
                     "future terminal slot is duplicated, replaced, or out of order")
            slot_requests = {
                row["id"] for row in future_rows if row["slot"] == slot
            }
            _require(slot_requests <= settled and (kind == "slot-terminal-invalid"
                                                   or slot_requests),
                     "future terminal slot lacks reconciled request accounting")
            accounted.append(slot)
            (valid_slots if kind == "slot-complete" else invalid_slots).append(slot)
        elif kind in ("stopped", "budget-refusal"):
            _require(request_id is None, "scope halt cannot identify a settled request")
            if kind == "budget-refusal":
                expected_halt_state = "INCOMPLETE_BUDGET"
            else:
                _require(not failure_seen and detail.get("reason") in {
                    "INCOMPLETE_POLICY", "INCOMPLETE_INTERRUPTED", "INCOMPLETE_UNKNOWN_CHARGE"},
                    "scope stop reason or precedence differs")
                expected_halt_state = detail["reason"]
            failure_seen = True
        elif kind == "collection-complete":
            completion = detail
        else:
            _require(False, "unknown post-disposition accounting event")
    _require(settled | set(live_unknown_ids) | outstanding == set(row_by_id),
             "future request row lacks an accounting event")
    live_reserved_ids = [
        row["id"] for row in future_rows if row["state"] == "reserved"
    ]
    _require(set(live_reserved_ids) == outstanding,
             "future reserved row/event state differs")
    if live_unknown_ids or live_reserved_ids or failure_seen:
        _require(snapshot["state"] != "complete",
                 "accounting failure or in-flight request cannot count complete")
    if not started:
        _require(snapshot["state"] == "ready",
                 "disposition without event 11 must remain ready")
    elif completion is None:
        _require(snapshot["state"] != "complete",
                 "complete state requires an exact collection-complete event")
        if expected_halt_state is not None:
            _require(snapshot["state"] == expected_halt_state,
                     "protected failure did not retain its halting scope state")
        elif not live_reserved_ids:
            _require(snapshot["state"] == "collecting",
                     "active reconciled continuation must remain collecting")
    if completion is not None:
        _require(snapshot["state"] == "complete" and not failure_seen
                 and not outstanding and not live_unknown_ids
                 and accounted == remaining and len(valid_slots) + len(invalid_slots) == 442,
                 "collection completed without exact reconciled 442-slot continuation")
        expected_completion = {
            "accountedSlots": 444,
            "continuedSlots": 442,
            "validCompletedSlots": len(valid_slots),
            "invalidTerminalSlots": len(invalid_slots),
            "historicalPreservedSlots": 2,
            "permanentUnknownMicroUsd": PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "historicalRequestIds": values["historicalRequestIds"],
            "sourceEpochIds": [
                attempt["epochId"] for attempt in authorization["preservedAttempts"]],
        }
        _require(completion == expected_completion,
                 "collection-complete disposition counters differ")
    exposure = sum(row["charge"] for row in requests)
    _require(snapshot["exposureMicroUsd"] == exposure
             and exposure >= PERMANENTLY_RETAINED_MICRO_USD
             and exposure <= PILOT_CEILING_MICRO_USD,
             "live exposure does not permanently retain the historical liability")
    _require(snapshot["accountedSlots"] == 2 + len(accounted)
             and snapshot["validCompletedSlots"] == len(valid_slots)
             and snapshot["invalidTerminalSlots"] == 1 + len(invalid_slots),
             "live snapshot terminal counters differ")
    return {
        "kind": DISPOSITION_KIND,
        "state": snapshot["state"],
        "permanentUnknownMicroUsd": PERMANENTLY_RETAINED_MICRO_USD,
        "actualCost": None,
        "historicalUnknownRequestCount": 2,
        "historicalUnknownRequestIds": values["historicalRequestIds"],
        "historicalSourceEpochIds": [
            attempt["epochId"] for attempt in authorization["preservedAttempts"]],
        "historicalPreservedSlots": preserved,
        "liveUnknownRequestCount": len(live_unknown_ids),
        "liveUnknownRequestIds": live_unknown_ids,
        "liveReservedRequestCount": len(live_reserved_ids),
        "continuedTerminalSlots": len(accounted),
        "remainingNonterminalSlots": 442 - len(accounted),
        "validCompletedSlots": len(valid_slots),
        "invalidTerminalSlots": len(invalid_slots),
        "accountedSlots": 2 + len(accounted),
        "exposureMicroUsd": exposure,
        "proofSha256": proof["proofSha256"],
    }


def owner_binding_identity(binding, proof_sha256):
    _sha256(proof_sha256, "owner-bound disposition proof")
    return _sha256_json({"binding": binding, "proofSha256": proof_sha256})


def validate_owner_binding(owner, binding, proof_sha256):
    _require(isinstance(owner, str), "disposition owner is missing")
    parts = owner.split(".")
    _require(len(parts) == 2 and _valid_sha(parts[0])
             and parts[1] == owner_binding_identity(binding, proof_sha256),
             "active owner no longer binds the reviewed disposition and scope")


def complete_scope(
        ledger_path, owner, *, authorization, authorization_sha256, target_binding):
    """Complete only after 442 new terminals and fully reconciled future traffic."""
    _require(isinstance(owner, str) and owner, "active owner is required")
    ledger = _absolute(ledger_path, "ledger path", True)
    _regular_single_link(ledger, "ledger")
    _require_no_sidecars(ledger)
    db = _open_database(ledger, "rw")
    try:
        db.execute("PRAGMA synchronous=FULL")
        db.execute("BEGIN IMMEDIATE")
        scope, requests, events = _database_snapshot(db)
        _require(scope["binding"] == canonical(target_binding)
                 and scope["ceiling"] == PILOT_CEILING_MICRO_USD
                 and scope["state"] == "collecting" and scope["owner"] == owner,
                 "disposition scope is not collecting under the supplied owner")
        current = _snapshot(scope, requests, events)
        replay = validate_history(
            current, authorization=authorization,
            authorization_sha256=authorization_sha256, target_binding=target_binding)
        validate_owner_binding(owner, target_binding, replay["proofSha256"])
        _require(replay["continuedTerminalSlots"] == 442
                 and replay["liveUnknownRequestCount"] == 0
                 and replay["liveReservedRequestCount"] == 0,
                 "exact fully reconciled 442-slot continuation is required")
        detail = {
            "accountedSlots": 444,
            "continuedSlots": 442,
            "validCompletedSlots": replay["validCompletedSlots"],
            "invalidTerminalSlots": replay["invalidTerminalSlots"],
            "historicalPreservedSlots": 2,
            "permanentUnknownMicroUsd": PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "historicalRequestIds": replay["historicalUnknownRequestIds"],
            "sourceEpochIds": replay["historicalSourceEpochIds"],
        }
        db.execute("UPDATE scope SET state='complete' WHERE id=1")
        db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                   ("collection-complete", None, canonical(detail)))
        db.execute("COMMIT")
    except BaseException:
        if db.in_transaction:
            db.execute("ROLLBACK")
        raise
    finally:
        db.close()
    return {
        **replay,
        "state": "complete",
        "accountedSlots": 444,
        "remainingNonterminalSlots": 0,
    }
