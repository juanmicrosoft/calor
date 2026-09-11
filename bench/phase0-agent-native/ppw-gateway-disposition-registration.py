#!/usr/bin/env python3
"""Resolve, validate, and prospectively register the fixed #1436 disposition."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil


BENCH = Path(__file__).resolve().parent
ROOT = BENCH / "registrations/ppw-rows-stage1"
EPOCH = "w-rows-pilot-gateway-003"
ISSUE = "https://github.com/juanmicrosoft/calor/issues/1436"
GRANT = "https://github.com/juanmicrosoft/calor/issues/1436#issuecomment-5633881736"
OPERATOR_BENCH = Path(
    "/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/"
    "files/worktrees/instrument/bench/phase0-agent-native"
)
ORIGINAL_ARCHIVE = OPERATOR_BENCH / "epochs/w-rows-pilot-gateway-001"
FAILED_ARCHIVE = OPERATOR_BENCH / "epochs/w-rows-pilot-gateway-002"
OLD_PROFILE = ROOT / "gateway-execution-profile-1434.json"
OLD_PLAN = ROOT / "gateway-spending-plan-1434.json"
OLD_AUTHORIZATION = ROOT / "gateway-authorization-1434.json"
OLD_ANALYSIS = ROOT / "analysis-registration.json"
PRE_DISPOSITION_ANALYSIS = ROOT / "analysis-registration.pre-disposition-1436.json"
PROFILE = ROOT / "gateway-execution-profile-1436.json"
EVIDENCE = ROOT / "gateway-disposition-evidence-1436.json"
WIRE = ROOT / "gateway-evidence/gateway-native-wire-1436-evidence.json"
STARTUP = ROOT / "gateway-evidence/gateway-native-startup-1436-evidence.json"
BOUND_REVIEW = ROOT / "gateway-evidence/gateway-liability-bound-review-1436.json"
METHODS_REVIEW = ROOT / "gateway-evidence/gateway-liability-methods-review-1436.json"
REQUIRED_EVIDENCE_MODE = "registered-operator"
EXPECTED_OLD_PROFILE_SHA256 = (
    "3869f2d2b21743f917fe010fbf9b6fdac0f9b51eab55a11d4162a4a1d0a766ee"
)
EXPECTED_OLD_PLAN_SHA256 = (
    "f4bfa29abc4cab698fd2d612177b506a1dcc6d7a7c55f16fdee293ec4a58efbd"
)
EXPECTED_OLD_AUTHORIZATION_SHA256 = (
    "756ab420af2c8e9e9038ccfa68568d0223e0895575cd0bef3f02639b82c5a3e7"
)
EXPECTED_OLD_ANALYSIS_SHA256 = (
    "2595da7445eb347e6d60dd3325d19964f4f0d9e6a7929f4033ecd1a8336a22e4"
)
BACKUP_NAME = "epic1254-pilot.failed-1432.sqlite3"
DISPOSITION_BACKUP_NAME = "epic1254-pilot.pre-disposition-1436.sqlite3"
OUTPUTS = {
    "authorization": ROOT / "gateway-liability-authorization-1436.json",
    "instrument": ROOT / "gateway-instrument-amendment-1436.json",
    "plan": ROOT / "gateway-spending-plan-1436.json",
    "profile": PROFILE,
    "proof": ROOT / "gateway-disposition-proof-1436.json",
    "evidence": EVIDENCE,
    "price": ROOT / "gateway-prices-1436.json",
    "originalInventory": ROOT / "gateway-disposition-original-inventory-1436.json",
    "failedInventory": ROOT / "gateway-disposition-failed-inventory-1436.json",
    "stoppedSnapshot": ROOT / "gateway-disposition-stopped-snapshot-1436.json",
    "preAnalysis": PRE_DISPOSITION_ANALYSIS,
    "analysis": OLD_ANALYSIS,
}
REVIEW_FIELDS = {
    "schemaVersion", "kind", "reviewType", "verdict", "authorizationReference",
    "historicalLedgerSha256", "permanentLiabilityMicroUsd", "ceilingMicroUsd",
    "reference", "scope", "findings",
}


def module(filename):
    spec = importlib.util.spec_from_file_location(
        filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W disposition registration: " + message)


def load(path):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, "duplicate JSON key: " + key)
            value[key] = item
        return value
    return json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=unique)


def encoded(value):
    return module("ppw-gateway-disposition.py").authorization_bytes(value)


def sha_bytes(value):
    return hashlib.sha256(value).hexdigest()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def reference(path, value):
    return {"path": Path(path).relative_to(ROOT).as_posix(),
            "sha256": sha_bytes(encoded(value))}


def registration_contract():
    """Describe fixed inputs and outputs without reading the ledger or archives."""
    return {
        "schemaVersion": 1,
        "kind": "pp-w-prospective-disposition-registration-contract",
        "issue": ISSUE,
        "targetEpochId": EPOCH,
        "evidenceMode": "registered-operator",
        "permanentUnknownMicroUsd": 51_040_000,
        "actualCost": None,
        "plannedInvocations": 444,
        "retainedHistoricalAttempts": 2,
        "continuationInvocations": 442,
        "futureUnknownPolicy": "halt",
        "operatorPaths": {
            "originalArchive": str(ORIGINAL_ARCHIVE),
            "failedArchive": str(FAILED_ARCHIVE),
            "ledger": "derived only from the registered git-common-dir ledger binding",
            "oldBackup": BACKUP_NAME,
            "newBackup": DISPOSITION_BACKUP_NAME,
        },
        "requiredReviews": [
            BOUND_REVIEW.relative_to(ROOT).as_posix(),
            METHODS_REVIEW.relative_to(ROOT).as_posix(),
        ],
        "requiredReadinessEvidence": [
            WIRE.relative_to(ROOT).as_posix(),
            STARTUP.relative_to(ROOT).as_posix(),
        ],
        "outputs": sorted(path.relative_to(ROOT).as_posix()
                          for path in OUTPUTS.values()),
        "generatorCreatesEpoch": False,
        "generatorAppliesDisposition": False,
        "generatorStartsCollection": False,
    }


def _review_value(value, review_type):
    core = module("ppw-gateway-disposition.py")
    require(set(value) == REVIEW_FIELDS
            and value["schemaVersion"] == 1
            and value["kind"] == "pp-w-retained-liability-review"
            and value["reviewType"] == review_type
            and value["verdict"] == "APPROVE"
            and value["authorizationReference"] == GRANT
            and value["historicalLedgerSha256"] == core.EXPECTED_OLD_LEDGER_SHA256
            and value["permanentLiabilityMicroUsd"] == core.PERMANENTLY_RETAINED_MICRO_USD
            and value["ceilingMicroUsd"] == core.PILOT_CEILING_MICRO_USD
            and isinstance(value["reference"], str) and value["reference"]
            and isinstance(value["scope"], str) and value["scope"]
            and isinstance(value["findings"], list) and value["findings"],
            "independent review does not approve the exact registered bound/method")
    return value


def _review(path, review_type):
    require(path.is_file(), "missing independent review: " + str(path))
    return _review_value(load(path), review_type)


def _validate_wire(value, price_reference):
    registration = module("ppw-gateway-registration.py")
    spending = module("ppw-spending.py")
    isolation = module("ppw-gateway-client.py")
    require(value.get("kind") == "pp-w-engineering-no-forward-native-probe"
            and value.get("success") is True
            and value.get("upstreamRequests") == 0
            and value.get("experimentalObservations") == 0
            and value.get("clientSha256") == isolation.CLIENT_SHA256
            and value.get("clientFlags") == isolation.registered_client_flags(),
            "fresh wire evidence identity or no-forward result differs")
    observations = value.get("observations")
    require(isinstance(observations, list) and observations
            and all(item.get("messagesPath") is True
                    and item.get("credentialHeaderPresent") is True
                    and item.get("priceContractAccepted") is True
                    and item.get("wireContractAccepted") is True
                    and item.get("unclassifiedBetaCount") == 0
                    for item in observations),
            "fresh wire evidence did not pass transport and price contracts")
    require(value.get("priceContract") == price_reference,
            "fresh wire evidence does not bind the prospective price contract")
    require(set(value.get("sourceHashes", {})) == registration.WIRE_SOURCE_HASHES
            and all(spending.digest(BENCH / name) == value["sourceHashes"][name]
                    for name in registration.WIRE_SOURCE_HASHES),
            "fresh wire evidence binds different launcher sources")


def _archive_metadata(core, path, role, epoch_id):
    inventory = core.archive_inventory(path)
    pins = path / "pins.json"
    return inventory, {
        "archiveRole": role,
        "epochId": epoch_id,
        "pathSha256": sha_bytes(str(path.resolve()).encode("utf-8")),
        "inventorySha256": inventory["sha256"],
        "fileCount": inventory["fileCount"],
        "byteCount": inventory["byteCount"],
        "pinsPath": "pins.json",
        "pinsSha256": digest(pins),
    }


def _source_map(archive, inventory, pins):
    pinned = set(pins.get("harnessArtifacts", {}).values())
    values = {
        item["path"]: item["sha256"] for item in inventory["files"]
        if item["sha256"] in pinned
    }
    require(values, "predecessor archive contains no source pinned by its profile")
    return values


def _registered_method_from_profile(profile, spending):
    _, registration = spending.pinned_document(
        BENCH, profile["baselineRegistration"], "baseline registration")
    registration = copy.deepcopy(registration)
    selected = registration["stages"]["pilot"]
    selected["epochId"] = profile["epochId"]
    for name in (
            "spendAuthorization", "spendingPlan", "instrumentAmendment",
            "stageRegistration", "modelRegistration", "sourceInspectionEvidence"):
        spending.pinned_document(ROOT, profile[name], name)
        selected[name] = profile[name]
    _, certificates = spending.pinned_document(
        ROOT, profile["sourceInspectionEvidence"], "source inspection evidence")
    registration["sourceInspections"] = certificates["tasks"]
    registration.update(collectionAuthorized=True, fundingStatus="approved")
    return registration


def _attempt(core, archive, inventory, archive_record, index):
    pins = load(archive / "pins.json")
    run_root = (
        "runs/C-001-quota-adapter/calor-permissive/run-1"
        if index == 0 else "runs/C-001-quota-adapter/calor-strict/run-1"
    )
    paths = {
        "rawRecordPath": run_root + "/result.json",
        "clientInvocationPath": run_root + "/client-invocation.json",
        "invalidReasonPath": run_root + "/invalid.txt",
        "attemptStartPath": None if index == 0 else run_root + "/attempt-start.json",
    }
    entries = {item["path"]: item["sha256"] for item in inventory["files"]}
    for name, relative in paths.items():
        require(relative is None or relative in entries,
                "predecessor attempt artifact is missing: " + str(relative))
    return {
        "slot": (
            "C-001-quota-adapter/calor-permissive/1"
            if index == 0 else "C-001-quota-adapter/calor-strict/1"
        ),
        "epochId": archive_record["epochId"],
        "classification": core.ATTEMPT_CLASSIFICATIONS[index],
        "archiveRole": archive_record["archiveRole"],
        "rawRecordPath": paths["rawRecordPath"],
        "rawRecordSha256": entries[paths["rawRecordPath"]],
        "clientInvocationPath": paths["clientInvocationPath"],
        "clientInvocationSha256": entries[paths["clientInvocationPath"]],
        "invalidReasonPath": paths["invalidReasonPath"],
        "invalidReasonSha256": entries[paths["invalidReasonPath"]],
        "attemptStartPath": paths["attemptStartPath"],
        "attemptStartSha256": (
            None if paths["attemptStartPath"] is None
            else entries[paths["attemptStartPath"]]
        ),
        "sourceHashes": _source_map(archive, inventory, pins),
    }


def _load_evidence(path=EVIDENCE):
    spending = module("ppw-spending.py")
    path = Path(path)
    require(path.resolve() == EVIDENCE.resolve(),
            "disposition evidence must be the canonical #1436 registration")
    value = load(path)
    require(value.get("schemaVersion") == 1
            and value.get("kind") == "pp-w-historical-liability-disposition-evidence-v1"
            and value.get("issue") == ISSUE,
            "unrecognized disposition evidence")
    names = {
        "authorization", "executionProfile", "spendingPlan", "instrumentAmendment",
        "priceContract", "inspectionProof", "originalArchiveInventory",
        "failedArchiveInventory", "stoppedSnapshot", "financialBoundReview",
        "methodsReview", "transportEvidence", "nativeStartupEvidence",
    }
    require(names <= set(value), "disposition evidence chain is incomplete")
    documents = {}
    for name in names:
        _, documents[name] = spending.pinned_document(ROOT, value[name], name)
    core = module("ppw-gateway-disposition.py")
    authorization = documents["authorization"]
    core.validate_authorization(authorization)
    require(authorization["evidenceMode"] == REQUIRED_EVIDENCE_MODE
            and value["authorization"] == value["targetBinding"]["authorization"]
            and value["targetBinding"]["authorization"]["sha256"]
            == sha_bytes(core.authorization_bytes(authorization))
            and value["targetBinding"]["binding"]["authorizationSha256"]
            == value["authorization"]["sha256"],
            "disposition authorization bytes or target binding differ")
    require(value["executionProfile"]["sha256"] == digest(PROFILE),
            "disposition evidence selects another execution profile")
    require(value.get("historicalLineage") == {
        "preDispositionProfile": {
            "path": OLD_PROFILE.name, "sha256": EXPECTED_OLD_PROFILE_SHA256,
        },
        "preDispositionPlan": {
            "path": OLD_PLAN.name, "sha256": EXPECTED_OLD_PLAN_SHA256,
        },
        "preDispositionAuthorization": {
            "path": OLD_AUTHORIZATION.name,
            "sha256": EXPECTED_OLD_AUTHORIZATION_SHA256,
        },
        "preDispositionAnalysis": {
            "path": PRE_DISPOSITION_ANALYSIS.name,
            "sha256": EXPECTED_OLD_ANALYSIS_SHA256,
        },
    }, "pre-disposition profile/plan/authorization/analysis lineage differs")
    return value, documents


def resolve_collection_profile(path):
    """Resolve only the active, reviewed #1436 profile for collection."""
    path = Path(path)
    require(path.resolve() == PROFILE.resolve(),
            "collection requires the canonical reviewed #1436 profile")
    require(PROFILE.is_file() and EVIDENCE.is_file(),
            "the #1436 disposition profile and evidence are not registered")
    gateway = module("ppw-gateway-registration.py")
    registration = gateway._resolve_profile(path, True)
    evidence, documents = _load_evidence()
    profile = documents["executionProfile"]
    selected = registration["stages"]["pilot"]
    require("dispositionEvidence" not in profile
            and profile.get("spendAuthorization") == evidence["authorization"]
            and profile.get("spendingPlan") == evidence["spendingPlan"],
            "profile must remain acyclic and select the registered disposition authority")
    for name, field in (
            ("dispositionAuthorization", "authorization"),
            ("dispositionProof", "inspectionProof"),
            ("originalArchiveInventory", "originalArchiveInventory"),
            ("failedArchiveInventory", "failedArchiveInventory"),
            ("stoppedSnapshot", "stoppedSnapshot"),
            ("priceContract", "priceContract"),
            ("financialBoundReview", "financialBoundReview"),
            ("methodsReview", "methodsReview"),
            ("transportEvidence", "transportEvidence"),
            ("nativeStartupEvidence", "nativeStartupEvidence")):
        selected[name] = evidence[field]
    selected["dispositionEvidence"] = {
        "path": EVIDENCE.name, "sha256": digest(EVIDENCE),
    }
    selected["preservedAttemptedSlots"] = [
        item["slot"] for item in documents["authorization"]["preservedAttempts"]
    ]
    selected["historicalSourceEpochIds"] = [
        item["epochId"] for item in documents["authorization"]["preservedAttempts"]
    ]
    selected["disposition"] = {
        "kind": documents["authorization"]["kind"],
        "permanentUnknownMicroUsd":
            documents["authorization"]["permanentlyRetainedMicroUsd"],
        "actualCost": None,
        "futureUnknownPolicy": documents["authorization"]["futureUnknownPolicy"],
    }
    return registration


def canonical_inputs():
    """Return fixed read-only/apply inputs only after the active registration validates."""
    adjudication = module("ppw-pilot-adjudicate.py")
    manifest = adjudication.validate_analysis_registration()
    require(manifest.get("recoveryStatus") == "historical-liability-registered"
            and manifest.get("gatewayDispositionEvidence") == {
                "path": EVIDENCE.relative_to(BENCH).as_posix(),
                "sha256": digest(EVIDENCE),
            }, "the reviewed #1436 disposition registration is not active")
    evidence, documents = _load_evidence()
    authorization = documents["authorization"]
    spending = module("ppw-spending.py")
    ledger = spending.gateway_ledger_location(
        documents["spendingPlan"]["ledgerBinding"])
    protected = ledger.parent
    values = {
        "ledger_path": ledger,
        "authorization": authorization,
        "authorization_sha256": evidence["authorization"]["sha256"],
        "target_binding": evidence["targetBinding"]["binding"],
        "original_archive": ORIGINAL_ARCHIVE,
        "failed_archive": FAILED_ARCHIVE,
        "old_backup_path": protected / BACKUP_NAME,
        "new_backup_path": protected / DISPOSITION_BACKUP_NAME,
        "protected_root": protected,
    }
    require(evidence.get("operatorPaths") == {
        "ledgerPathSha256": sha_bytes(str(ledger.resolve()).encode()),
        "originalArchivePathSha256": sha_bytes(str(ORIGINAL_ARCHIVE.resolve()).encode()),
        "failedArchivePathSha256": sha_bytes(str(FAILED_ARCHIVE.resolve()).encode()),
        "oldBackupPathSha256":
            sha_bytes(str((protected / BACKUP_NAME).resolve()).encode()),
        "newBackupPathSha256":
            sha_bytes(str((protected / DISPOSITION_BACKUP_NAME).resolve()).encode()),
        "protectedRootPathSha256": sha_bytes(str(protected.resolve()).encode()),
    }, "registered operator paths differ")
    return values, documents["inspectionProof"]


def build_documents():
    """Build prospective documents after read-only proof; never write or apply anything."""
    spending = module("ppw-spending.py")
    budget = module("ppw-gateway-budget.py")
    core = module("ppw-gateway-disposition.py")
    gateway = module("ppw-gateway-registration.py")
    active_analysis_sha256 = digest(OLD_ANALYSIS)
    refreshing = active_analysis_sha256 != EXPECTED_OLD_ANALYSIS_SHA256
    if refreshing:
        active = load(OLD_ANALYSIS)
        require(active.get("recoveryStatus") == "historical-liability-registered"
                and active.get("collectionAuthorized") is False
                and not (BENCH / "epochs" / EPOCH).exists()
                and digest(PRE_DISPOSITION_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "only an unexecuted #1436 proposal may be rebuilt")
    require(digest(OLD_PROFILE) == EXPECTED_OLD_PROFILE_SHA256
            and digest(OLD_PLAN) == EXPECTED_OLD_PLAN_SHA256
            and digest(OLD_AUTHORIZATION) == EXPECTED_OLD_AUTHORIZATION_SHA256
            and (not refreshing or PRE_DISPOSITION_ANALYSIS.is_file()),
            "the registered #1434 profile/plan/authorization/analysis lineage changed")
    bound = _review(BOUND_REVIEW, "financial-bound")
    methods = _review(METHODS_REVIEW, "registered-methods")
    require(WIRE.is_file(), "missing fresh no-forward wire evidence")
    require(STARTUP.is_file(), "missing fresh native startup evidence")

    price = budget.price_contract()
    price_ref = reference(OUTPUTS["price"], price)
    wire = load(WIRE)
    _validate_wire(wire, price_ref)
    startup = load(STARTUP)
    gateway.validate_native_startup(startup)

    old_profile = load(OLD_PROFILE)
    old_plan = load(OLD_PLAN)
    old_authorization = load(OLD_AUTHORIZATION)
    registration = _registered_method_from_profile(old_profile, spending)
    selected = registration["stages"]["pilot"]
    slots = [
        "%s/%s/%d" % (task, arm, run)
        for run in range(1, selected["runsPerArm"] + 1)
        for task in registration["tasks"]
        for arm in ("calor-permissive", "calor-strict")
    ]
    require(len(slots) == 444 and slots[:3] == [
        "C-001-quota-adapter/calor-permissive/1",
        "C-001-quota-adapter/calor-strict/1",
        "C-002-shipping-quote/calor-permissive/1",
    ], "the exact ordered 444-slot population changed")

    ledger = spending.gateway_ledger_location(old_authorization["ledgerBinding"])
    protected = ledger.parent
    old_backup = protected / BACKUP_NAME
    new_backup = protected / DISPOSITION_BACKUP_NAME
    require(digest(ledger) == core.EXPECTED_OLD_LEDGER_SHA256
            and digest(old_backup) == core.EXPECTED_OLD_BACKUP_SHA256,
            "canonical ledger or immutable predecessor backup changed")
    old_snapshot = budget.RequestLedger(ledger).snapshot()
    original_inventory, original_record = _archive_metadata(
        core, ORIGINAL_ARCHIVE, core.ARCHIVE_ROLES[0], core.OLD_EPOCHS[0])
    failed_inventory, failed_record = _archive_metadata(
        core, FAILED_ARCHIVE, core.ARCHIVE_ROLES[1], core.OLD_EPOCHS[1])
    attempts = [
        _attempt(core, ORIGINAL_ARCHIVE, original_inventory, original_record, 0),
        _attempt(core, FAILED_ARCHIVE, failed_inventory, failed_record, 1),
    ]
    harness = spending.artifact_manifest(spending.GATEWAY)
    authorization = {
        "schemaVersion": 1,
        "kind": core.DISPOSITION_KIND,
        "evidenceMode": "registered-operator",
        "grant": {
            "quote": core.GRANT_QUOTE,
            "authorizedAt": core.GRANT_TIME,
            "reference": core.GRANT_REFERENCE,
        },
        "oldSnapshot": old_snapshot,
        "oldLedgerSha256": core.EXPECTED_OLD_LEDGER_SHA256,
        "oldBackupSha256": core.EXPECTED_OLD_BACKUP_SHA256,
        "predecessorArchives": [original_record, failed_record],
        "preservedAttempts": attempts,
        "targetEpochId": EPOCH,
        "targetPriceSha256": budget.price_identity(),
        "targetHarnessArtifacts": harness,
        "permanentlyRetainedMicroUsd": core.PERMANENTLY_RETAINED_MICRO_USD,
        "actualCost": None,
        "futureUnknownPolicy": "halt",
        "quiescenceProcessMarkers": [
            str(ledger), str(ORIGINAL_ARCHIVE), str(FAILED_ARCHIVE),
            "w-rows-pilot-gateway-003",
            str(OPERATOR_BENCH.parents[1] / ".ppw-instrument-work"),
            old_plan["clientControl"]["clientExecutable"],
        ],
    }
    core.validate_authorization(authorization)
    authorization_ref = reference(OUTPUTS["authorization"], authorization)

    amendment = {
        "schemaVersion": 1,
        "kind": "pp-w-prospective-spending-instrument-amendment",
        "stage": "pilot",
        "supersedes": {
            "path": OLD_PROFILE.name.replace("execution-profile", "instrument-amendment"),
            "sha256": digest(ROOT / "gateway-instrument-amendment-1434.json"),
        },
        "replacementHarnessArtifacts": harness,
        "historicalDisposition": {
            "kind": core.DISPOSITION_KIND,
            "authorization": authorization_ref,
            "retainedAttempts": 2,
            "continuationInvocations": 442,
            "replacementAttempts": 0,
            "permanentUnknownMicroUsd": core.PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "futureUnknownPolicy": "halt",
            "scientificRulesChanged": False,
        },
        "reason": "Register the one authorized permanent historical $51.04 liability, preserve "
                  "both consumed attempts, and continue only the 442 untouched slots.",
    }
    amendment_ref = reference(OUTPUTS["instrument"], amendment)
    selected = dict(
        selected, epochId=EPOCH, spendAuthorization=authorization_ref,
        instrumentAmendment=amendment_ref)
    protocol = spending.protocol_identity(registration, selected, "pilot", EPOCH)

    plan = copy.deepcopy(old_plan)
    plan.pop("recovery", None)
    plan.update({
        "epochId": EPOCH,
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol,
        "supersedes": {"path": OLD_PLAN.name, "sha256": EXPECTED_OLD_PLAN_SHA256},
        "plannedInvocations": 444,
        "disposition": {
            "kind": core.DISPOSITION_KIND,
            "authorization": authorization_ref,
            "plannedInvocations": 444,
            "retainedHistoricalAttempts": 2,
            "continuationInvocations": 442,
            "replacementAttempts": 0,
            "permanentUnknownMicroUsd": core.PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "sameExperimentCeiling": True,
            "futureUnknownPolicy": "halt",
            "validCompletionPopulation": "registered-terminal-valid-attempts-only",
        },
    })
    plan["clientControl"]["priceContract"] = price_ref
    plan_ref = reference(OUTPUTS["plan"], plan)

    target_binding = {
        "stage": "pilot",
        "epochId": EPOCH,
        "priceSha256": budget.price_identity(),
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol,
        "planSha256": plan_ref["sha256"],
        "harnessArtifacts": harness,
        "plannedSlots": slots,
    }
    proof = core.inspect_disposition(
        ledger, authorization=authorization,
        authorization_sha256=authorization_ref["sha256"],
        target_binding=target_binding,
        original_archive=ORIGINAL_ARCHIVE,
        failed_archive=FAILED_ARCHIVE,
        old_backup_path=old_backup,
        new_backup_path=new_backup,
        protected_root=protected)
    proof_ref = reference(OUTPUTS["proof"], proof)
    original_inventory_ref = reference(OUTPUTS["originalInventory"], original_inventory)
    failed_inventory_ref = reference(OUTPUTS["failedInventory"], failed_inventory)
    stopped_ref = reference(OUTPUTS["stoppedSnapshot"], old_snapshot)

    profile = copy.deepcopy(old_profile)
    profile.pop("recovery", None)
    profile.pop("terminalSemanticsAmendment", None)
    profile.update({
        "id": "request-reserving-gateway-disposition-1436",
        "epochId": EPOCH,
        "effectiveOn": "independently reviewed activation of the #1436 registration",
        "supersedes": {"path": OLD_PROFILE.name, "sha256": EXPECTED_OLD_PROFILE_SHA256},
        "spendAuthorization": authorization_ref,
        "spendingPlan": plan_ref,
        "instrumentAmendment": amendment_ref,
        "transportEvidence": {
            "path": WIRE.relative_to(ROOT).as_posix(), "sha256": digest(WIRE),
        },
        "nativeStartupEvidence": {
            "path": STARTUP.relative_to(ROOT).as_posix(), "sha256": digest(STARTUP),
        },
        "disposition": {
            "kind": core.DISPOSITION_KIND,
            "retainedAttempts": 2,
            "continuationInvocations": 442,
            "replacementAttempts": 0,
            "permanentUnknownMicroUsd": core.PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
        },
        "sourceHistory": {
            "preDisposition": old_profile["sourceHistory"],
            "historicalDisposedArchives": [
                {
                    "epochId": original_record["epochId"],
                    "archiveRole": original_record["archiveRole"],
                    "pathIdentitySha256": original_record["pathSha256"],
                    "inventorySha256": original_record["inventorySha256"],
                },
                {
                    "epochId": failed_record["epochId"],
                    "archiveRole": failed_record["archiveRole"],
                    "pathIdentitySha256": failed_record["pathSha256"],
                    "inventorySha256": failed_record["inventorySha256"],
                },
            ],
            "prospectiveCollection": {
                "epochId": EPOCH,
                "issue": ISSUE,
                "sourceIdentity": "replacementHarnessArtifacts",
            },
        },
    })
    profile_ref = reference(OUTPUTS["profile"], profile)

    operator_paths = {
        "ledgerPathSha256": sha_bytes(str(ledger.resolve()).encode()),
        "originalArchivePathSha256": sha_bytes(str(ORIGINAL_ARCHIVE.resolve()).encode()),
        "failedArchivePathSha256": sha_bytes(str(FAILED_ARCHIVE.resolve()).encode()),
        "oldBackupPathSha256": sha_bytes(str(old_backup.resolve()).encode()),
        "newBackupPathSha256": sha_bytes(str(new_backup.resolve()).encode()),
        "protectedRootPathSha256": sha_bytes(str(protected.resolve()).encode()),
    }
    evidence = {
        "schemaVersion": 1,
        "kind": "pp-w-historical-liability-disposition-evidence-v1",
        "issue": ISSUE,
        "grantReference": GRANT,
        "authorization": authorization_ref,
        "executionProfile": profile_ref,
        "spendingPlan": plan_ref,
        "instrumentAmendment": amendment_ref,
        "priceContract": price_ref,
        "inspectionProof": proof_ref,
        "originalArchiveInventory": original_inventory_ref,
        "failedArchiveInventory": failed_inventory_ref,
        "stoppedSnapshot": stopped_ref,
        "financialBoundReview": {
            "path": BOUND_REVIEW.relative_to(ROOT).as_posix(),
            "sha256": digest(BOUND_REVIEW),
        },
        "methodsReview": {
            "path": METHODS_REVIEW.relative_to(ROOT).as_posix(),
            "sha256": digest(METHODS_REVIEW),
        },
        "transportEvidence": profile["transportEvidence"],
        "nativeStartupEvidence": profile["nativeStartupEvidence"],
        "targetBinding": {
            "authorization": authorization_ref,
            "binding": target_binding,
        },
        "operatorPaths": operator_paths,
        "historicalArchives": [
            {
                "epochId": core.OLD_EPOCHS[0],
                "copiedArchivePath":
                    "admission/historical/" + core.OLD_EPOCHS[0],
                "originalPathIdentitySha256":
                    original_record["pathSha256"],
                "inventory": original_inventory_ref,
            },
            {
                "epochId": core.OLD_EPOCHS[1],
                "copiedArchivePath":
                    "admission/historical/" + core.OLD_EPOCHS[1],
                "originalPathIdentitySha256":
                    failed_record["pathSha256"],
                "inventory": failed_inventory_ref,
            },
        ],
        "historicalLineage": {
            "preDispositionProfile": {
                "path": OLD_PROFILE.name, "sha256": EXPECTED_OLD_PROFILE_SHA256,
            },
            "preDispositionPlan": {
                "path": OLD_PLAN.name, "sha256": EXPECTED_OLD_PLAN_SHA256,
            },
            "preDispositionAuthorization": {
                "path": OLD_AUTHORIZATION.name,
                "sha256": EXPECTED_OLD_AUTHORIZATION_SHA256,
            },
            "preDispositionAnalysis": {
                "path": PRE_DISPOSITION_ANALYSIS.name,
                "sha256": EXPECTED_OLD_ANALYSIS_SHA256,
            },
        },
        "financialProjection": {
            "ceilingMicroUsd": core.PILOT_CEILING_MICRO_USD,
            "permanentUnknownMicroUsd": core.PERMANENTLY_RETAINED_MICRO_USD,
            "actualCost": None,
            "historicalUnknownRequestCount": 2,
            "historicalUnknownRequestIds": list(core.HISTORICAL_REQUEST_IDS),
            "liveUnknownRequestCountIfComplete": 0,
            "futureUnknownPolicy": "halt",
        },
        "reviews": {
            "financialBound": bound,
            "registeredMethods": methods,
        },
    }
    pre_analysis = load(
        PRE_DISPOSITION_ANALYSIS if refreshing else OLD_ANALYSIS)
    documents = {
        OUTPUTS["authorization"]: authorization,
        OUTPUTS["instrument"]: amendment,
        OUTPUTS["plan"]: plan,
        OUTPUTS["profile"]: profile,
        OUTPUTS["proof"]: proof,
        OUTPUTS["price"]: price,
        OUTPUTS["originalInventory"]: original_inventory,
        OUTPUTS["failedInventory"]: failed_inventory,
        OUTPUTS["stoppedSnapshot"]: old_snapshot,
        OUTPUTS["preAnalysis"]: pre_analysis,
        OUTPUTS["evidence"]: evidence,
    }
    adjudication = module("ppw-pilot-adjudicate.py")
    artifacts = {}
    for relative in adjudication.ARTIFACTS + adjudication.RECOVERY_ARTIFACTS \
            + adjudication.TERMINAL_BASE_ARTIFACTS + adjudication.TERMINAL_ARTIFACTS \
            + adjudication.DISPOSITION_ARTIFACTS:
        path = BENCH / relative
        generated = documents.get(path)
        artifacts[relative] = (
            sha_bytes(encoded(generated)) if generated is not None else digest(path)
        )
    analysis = copy.deepcopy(pre_analysis)
    analysis.update({
        "artifacts": artifacts,
        "supersedes": {
            "path": adjudication.PRE_DISPOSITION_MANIFEST,
            "sha256": artifacts[adjudication.PRE_DISPOSITION_MANIFEST],
        },
        "scope": "Reviewed #1436 permanent $51.04 historical liability disposition; "
                 "two consumed attempts are retained and only 442 untouched slots continue.",
        "effectiveOn": "independently reviewed activation of the #1436 registration",
        "recoveryStatus": "historical-liability-registered",
        "collectionAuthorized": False,
        "gatewayExecutionProfile": {
            "path": adjudication.DISPOSITION_PROFILE,
            "sha256": artifacts[adjudication.DISPOSITION_PROFILE],
        },
        "gatewayDispositionEvidence": {
            "path": adjudication.DISPOSITION_EVIDENCE,
            "sha256": artifacts[adjudication.DISPOSITION_EVIDENCE],
        },
        "financialProjection": evidence["financialProjection"],
    })
    if refreshing:
        analysis["refreshesUnexecutedProposal"] = {
            "path": OLD_ANALYSIS.name,
            "sha256": active_analysis_sha256,
        }
    documents[OUTPUTS["analysis"]] = analysis
    return documents


def document_identities(documents):
    require(isinstance(documents, dict), "documents must be a path/value mapping")
    return {
        Path(path).relative_to(ROOT).as_posix(): sha_bytes(encoded(value))
        for path, value in sorted(documents.items(), key=lambda item: str(item[0]))
    }


def write_documents(documents, refresh_unexecuted=False):
    """Write only the reviewed prospective registration; never touch operational state."""
    require(set(map(Path, documents)) == set(OUTPUTS.values()),
            "prospective document set differs")
    target = BENCH / "epochs" / EPOCH
    require(not target.exists() and not target.is_symlink(),
            "target epoch already exists; registration cannot overwrite or refresh it")
    authorization = documents[OUTPUTS["authorization"]]
    core = module("ppw-gateway-disposition.py")
    core.validate_authorization(authorization)
    require(authorization["oldLedgerSha256"] == core.EXPECTED_OLD_LEDGER_SHA256
            and authorization["oldBackupSha256"] == core.EXPECTED_OLD_BACKUP_SHA256,
            "prospective documents do not bind the immutable old state")
    current = OLD_ANALYSIS.read_bytes()
    if not refresh_unexecuted:
        require(digest(OLD_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "active analysis manifest changed before registration")
        for path in OUTPUTS.values():
            if path != OLD_ANALYSIS:
                require(not path.exists() and not path.is_symlink(),
                        "prospective output already exists: " + str(path))
    else:
        active = load(OLD_ANALYSIS)
        require(active.get("recoveryStatus") == "historical-liability-registered"
                and active.get("collectionAuthorized") is False
                and active.get("gatewayDispositionEvidence", {}).get("sha256")
                == digest(EVIDENCE),
                "only the current unexecuted #1436 proposal may be refreshed")
        require(documents[OLD_ANALYSIS].get("refreshesUnexecutedProposal") == {
            "path": OLD_ANALYSIS.name,
            "sha256": sha_bytes(current),
        }, "refreshed proposal does not bind the previous active manifest")
        require(digest(PRE_DISPOSITION_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "preserved pre-disposition manifest changed")
        for path in OUTPUTS.values():
            require(path == OLD_ANALYSIS or path.is_file(),
                    "refresh cannot introduce a missing reviewed output")
    staging = []
    try:
        for path, value in documents.items():
            if path == OLD_ANALYSIS:
                continue
            stage = path.with_name(path.name + ".unexecuted-1436-new")
            require(not stage.exists(), "stale registration staging file: " + str(stage))
            stage.write_bytes(encoded(value))
            staging.append((stage, path))
        active_stage = OLD_ANALYSIS.with_name(
            OLD_ANALYSIS.name + ".unexecuted-1436-new")
        require(not active_stage.exists(), "stale active-manifest staging file")
        active_stage.write_bytes(encoded(documents[OLD_ANALYSIS]))
        staging.append((active_stage, OLD_ANALYSIS))
        for stage, path in staging:
            if refresh_unexecuted:
                os.replace(stage, path)
            else:
                require(not path.exists() or path == OLD_ANALYSIS,
                        "prospective output appeared during write: " + str(path))
                os.replace(stage, path)
        require(PRE_DISPOSITION_ANALYSIS.read_bytes() == current,
                "pre-disposition manifest bytes were not preserved")
    finally:
        for stage, _ in staging:
            stage.unlink(missing_ok=True)


def _archive_proof(epoch, proof, name):
    spending = module("ppw-spending.py")
    require(isinstance(proof, dict) and set(proof) == {"path", "sha256"},
            name + " reference differs")
    return spending.pinned_document(epoch, proof, name)[1]


def _wrapper(epoch, pins, authorization, evidence_reference, index, inventory):
    attempt = authorization["preservedAttempts"][index]
    task, arm, run = attempt["slot"].split("/")
    directory = epoch / "runs" / task / arm / ("run-" + run)
    wrapper = load(directory / "result.json")
    historical = wrapper.get("historicalDisposition")
    copied_root = "admission/historical/" + attempt["epochId"]
    entries = {item["path"]: item["sha256"] for item in inventory["files"]}
    require(wrapper.get("recordKind") == "pp-w-historical-disposition-wrapper-v1"
            and wrapper.get("epochId") == pins["epochId"]
            and wrapper.get("stage") == "pilot"
            and wrapper.get("dataKind") == pins["dataKind"]
            and wrapper.get("pair") == task and wrapper.get("arm") == arm
            and wrapper.get("run") == int(run)
            and wrapper.get("invalid") is True and wrapper.get("censored") is True
            and isinstance(historical, dict)
            and historical.get("kind") == authorization["kind"]
            and historical.get("sourceEpochId") == attempt["epochId"]
            and historical.get("slot") == attempt["slot"]
            and historical.get("classification") == attempt["classification"]
            and historical.get("replacementPermitted") is False
            and historical.get("dispositionReference") == evidence_reference
            and historical.get("originalArchive", {}).get("relativeRoot") == copied_root
            and historical.get("originalArchive", {}).get("inventorySha256")
            == inventory["sha256"]
            and historical.get("originalArchive", {}).get("originalPathIdentitySha256")
            == authorization["predecessorArchives"][index]["pathSha256"]
            and historical.get("originalArchive", {}).get("fileCount")
            == inventory["fileCount"]
            and historical.get("originalArchive", {}).get("byteCount")
            == inventory["byteCount"]
            and historical.get("originalRun") == {
                "rawRecordPath": attempt["rawRecordPath"],
                "rawRecordSha256": attempt["rawRecordSha256"],
                "clientInvocationPath": attempt["clientInvocationPath"],
                "clientInvocationSha256": attempt["clientInvocationSha256"],
                "invalidReasonPath": attempt["invalidReasonPath"],
                "invalidReasonSha256": attempt["invalidReasonSha256"],
                "attemptStartPath": attempt["attemptStartPath"],
                "attemptStartSha256": attempt["attemptStartSha256"],
            }
            and historical.get("originalProfile") == {
                "pinsPath": authorization["predecessorArchives"][index]["pinsPath"],
                "pinsSha256": authorization["predecessorArchives"][index]["pinsSha256"],
                "sourceHashes": attempt["sourceHashes"],
            }
            and historical.get("wrapperSourceHashes") == {
                name: pins["harnessArtifacts"][name]
                for name in ("run-pair.sh", "ppw-gateway-budget.py", "ppw-instrument.py")
            }, "historical wrapper provenance differs")
    for path_name, hash_name in (
            ("rawRecordPath", "rawRecordSha256"),
            ("clientInvocationPath", "clientInvocationSha256"),
            ("invalidReasonPath", "invalidReasonSha256"),
            ("attemptStartPath", "attemptStartSha256")):
        relative = attempt[path_name]
        if relative is not None:
            require(entries.get(relative) == attempt[hash_name]
                    and digest(epoch / copied_root / relative) == attempt[hash_name],
                    "copied historical attempt artifact differs")
    for relative, expected in attempt["sourceHashes"].items():
        require(entries.get(relative) == expected
                and digest(epoch / copied_root / relative) == expected,
                "copied historical source differs")
    require((directory / "invalid.txt").is_file()
            and "historical" in (directory / "invalid.txt").read_text().lower(),
            "historical wrapper lacks an explicit invalid reason")


def validate_archive(epoch, pins, selected):
    """Validate a portable 003 archive, including raw runs and permanent liability."""
    core = module("ppw-gateway-disposition.py")
    budget = module("ppw-gateway-budget.py")
    instrument = module("ppw-instrument.py")
    spending = module("ppw-spending.py")
    epoch = Path(epoch)
    require(epoch.is_dir() and not epoch.is_symlink(), "operational archive is required")
    profile = _archive_proof(epoch, selected.get("executionProfile"), "execution profile")
    evidence = _archive_proof(
        epoch, selected.get("dispositionEvidence"), "disposition evidence")
    authorization = _archive_proof(
        epoch, selected.get("dispositionAuthorization"), "disposition authorization")
    proof = _archive_proof(epoch, selected.get("dispositionProof"), "disposition proof")
    original_inventory = _archive_proof(
        epoch, selected.get("originalArchiveInventory"), "original archive inventory")
    failed_inventory = _archive_proof(
        epoch, selected.get("failedArchiveInventory"), "failed archive inventory")
    plan = _archive_proof(epoch, selected.get("spendingPlan"), "spending plan")
    amendment = _archive_proof(epoch, selected.get("instrumentAmendment"), "instrument amendment")
    price = _archive_proof(epoch, selected.get("priceContract"), "price contract")
    bound_review = _archive_proof(
        epoch, selected.get("financialBoundReview"), "financial bound review")
    methods_review = _archive_proof(
        epoch, selected.get("methodsReview"), "registered methods review")
    source_inspections = _archive_proof(
        epoch, selected.get("sourceInspectionEvidence"), "source inspection evidence")
    registration_value = load(epoch / "registration.json")
    require(profile.get("id") == "request-reserving-gateway-disposition-1436"
            and profile.get("epochId") == pins["epochId"] == EPOCH
            and evidence.get("executionProfile") == selected["executionProfile"]
            and evidence.get("authorization") == selected["dispositionAuthorization"]
            and evidence.get("inspectionProof") == selected["dispositionProof"]
            and evidence.get("targetBinding", {}).get("binding")
            == authorization["oldSnapshot"]["binding"] | {
                "epochId": EPOCH,
                "priceSha256": authorization["targetPriceSha256"],
                "authorizationSha256": selected["dispositionAuthorization"]["sha256"],
                "protocolSha256": plan["protocolSha256"],
                "planSha256": selected["spendingPlan"]["sha256"],
                "harnessArtifacts": pins["harnessArtifacts"],
            },
            "portable disposition authority chain differs")
    target_binding = evidence["targetBinding"]["binding"]
    core.validate_authorization(authorization)
    _review_value(bound_review, "financial-bound")
    _review_value(methods_review, "registered-methods")
    require(target_binding["plannedSlots"] == authorization["oldSnapshot"]["binding"]["plannedSlots"]
            and pins["lifecycle"] == "collected"
            and not (epoch / "collection-outcome.json").exists()
            and pins["harnessArtifacts"] == amendment["replacementHarnessArtifacts"]
            and plan["clientControl"]["priceContract"] == selected["priceContract"]
            and price == budget.price_contract()
            and plan["protocolSha256"] == spending.protocol_identity(
                registration_value, selected, "pilot", pins["epochId"])
            and registration_value["sourceInspections"] == source_inspections["tasks"],
            "disposition archive scope, lifecycle, or source inventory differs")
    spending.validate_forecast(
        plan, spending.units(plan["ceilingUsd"]), 444, epoch)
    initial = load(epoch / "spending-initial.json")
    final = load(epoch / "spending-final.json")
    initial_replay = core.validate_history(
        initial, authorization=authorization,
        authorization_sha256=selected["dispositionAuthorization"]["sha256"],
        target_binding=target_binding)
    final_replay = core.validate_history(
        final, authorization=authorization,
        authorization_sha256=selected["dispositionAuthorization"]["sha256"],
        target_binding=target_binding)
    require(initial_replay["state"] == "collecting"
            and initial_replay["remainingNonterminalSlots"] == 442
            and final_replay["state"] == "complete"
            and final_replay["remainingNonterminalSlots"] == 0
            and final_replay["permanentUnknownMicroUsd"] == 51_040_000
            and final_replay["actualCost"] is None
            and final_replay["historicalUnknownRequestCount"] == 2
            and final_replay["liveUnknownRequestCount"] == 0
            and final_replay["liveReservedRequestCount"] == 0,
            "final financial projection is incomplete or understates unknown liability")
    for index, inventory in enumerate((original_inventory, failed_inventory)):
        copied = epoch / "admission/historical" / authorization[
            "predecessorArchives"][index]["epochId"]
        observed = core.archive_inventory(copied)
        require(observed == inventory
                and observed["sha256"]
                == authorization["predecessorArchives"][index]["inventorySha256"],
                "portable predecessor archive copy differs")
        _wrapper(
            epoch, pins, authorization, selected["dispositionEvidence"], index, inventory)
    report = instrument.analyze(epoch.parent, epoch.name, "pilot")
    require(sum(cell["plannedRuns"] for cell in report["perCell"]) == 444
            and sum(cell["invalidRuns"] for cell in report["perCell"])
            == 2 + final_replay["invalidTerminalSlots"],
            "logical 444-run population or invalid censoring differs")
    events = {
        budget.decode(event["detail"])["slot"]: event
        for event in final["events"][11:]
        if event["kind"] in ("slot-complete", budget.TERMINAL_INVALID_EVENT)
    }
    source_names = {
        name: pins["harnessArtifacts"][name]
        for name in budget.TERMINAL_SOURCE_FILES
    }
    for slot in target_binding["plannedSlots"][2:]:
        event = events.get(slot)
        require(event is not None, "continued slot lacks a terminal accounting event")
        detail = budget.decode(event["detail"])
        task, arm, run = slot.split("/")
        directory = epoch / "runs" / task / arm / ("run-" + run)
        isolation = load(directory / "gateway-isolation.json")
        invocation = load(directory / "client-invocation.json")
        require(detail.get("isolation") == isolation
                and detail.get("clientExitCode") == invocation.get("exitCode"),
                "continued run isolation or invocation evidence differs")
        if event["kind"] == "slot-complete":
            require(detail.get("attempt") == budget.validate_attempt_start(
                directory, epoch, slot, source_names,
                recorded_authoritative_root=isolation["authoritativeRoot"]),
                "continued valid attempt-start evidence differs")
        else:
            require(detail.get("terminal") == budget.validate_terminal_attempt(
                directory, epoch, slot, source_names,
                recorded_authoritative_root=isolation["authoritativeRoot"]),
                "continued terminal-invalid evidence differs")
    require(proof.get("proofSha256") == final_replay["proofSha256"],
            "archived disposition proof differs from event 10")
    return {
        "id": profile["id"],
        "authority": selected["executionProfile"],
        "instrumentAmendment": selected["instrumentAmendment"],
        "evidenceResolution": "verified-portable-archive-local-files",
        "requestCount": len(final["requests"]),
        "requestBearingSlots": final["requestBearingSlots"],
        "completedSlots": final_replay["validCompletedSlots"],
        "invalidTerminalSlots": final["invalidTerminalSlots"],
        "historicalDisposedAttempts": 2,
        "scientificInvalidRuns": 2 + final_replay["invalidTerminalSlots"],
        "accountedSlots": 444,
        "preservedAttemptedSlots": final_replay["historicalPreservedSlots"],
        "continuedTerminalSlots": 442,
        "accountedMicroUsd": final_replay["exposureMicroUsd"],
        "ceilingMicroUsd": final["ceilingMicroUsd"],
        "permanentUnknownMicroUsd": 51_040_000,
        "actualCost": None,
        "historicalUnknownRequestCount": 2,
        "historicalUnknownRequestIds": final_replay["historicalUnknownRequestIds"],
        "liveUnknownRequestCount": 0,
        "futureUnknownPolicy": "halt",
    }
