#!/usr/bin/env python3
"""Resolve, validate, and prospectively register the fixed #1436 disposition."""
import copy
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3


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
RECORD_SET = ROOT / "gateway-disposition-record-set-1436.json"
ACTIVATION = ROOT / "gateway-disposition-activation-1436.json"
HISTORY = {
    "historical-bound": {
        "path": "gateway-evidence/historical-bound-review-1436.json",
        "sha256": "a387cc8f7376eb48295f63ac87d3c38d85985ac2fc565c96c9982a4f86f4d1a5",
        "kind": "pp-w-historical-two-request-financial-bound-ai-review",
        "verdict": "APPROVE",
    },
    "methods-draft-description": {
        "path": "gateway-evidence/methods-description-review-1436-d1396511.json",
        "sha256": "6d7f03c266ec74f37f4b44aefe5d4ab511774d10e7d0b771169dabc290948c04",
        "kind": "pp-w-independent-methods-registration-draft-review",
        "verdict": "REQUEST_CHANGES",
    },
}
FINAL_SUBJECTS = (
    "registered-methods-final-records", "new-financial-implementation",
    "source-implementation",
)
EXTRA_REVIEW_SOURCES = (
    "README-ppw-gateway.md", "ppw-pilot-adjudicate.py",
    "tests/test_ppw_gateway_disposition.py",
    "tests/test_ppw_gateway_disposition_registration.py",
    "tests/test_ppw_gateway_disposition_collection.py",
)
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
    "recordSet": RECORD_SET,
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
    path = Path(path)
    require(not any(item.is_symlink() for item in (path, *path.parents)),
            "metadata must not resolve through symbolic links")
    def unique(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, "duplicate JSON key: " + key)
            value[key] = item
        return value
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)


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
        "phases": {
            "proposal": {
                "command": "ppw-gateway-register-disposition.py --write",
                "inputs": "immutable predecessor metadata, stopped ledger/backup/archive read-only "
                          "proof, typed historical/draft indexes and unchanged raw reviews, "
                          "fresh source-bound wire/startup evidence",
                "finalApprovalRequired": False,
                "activeAnalysisChanged": False,
                "refresh": "--write --refresh-unexecuted-proposal; exact old state only, "
                           "no activation or target epoch; preserved analysis bytes never rewritten",
            },
            "review": {
                "subjects": list(FINAL_SUBJECTS),
                "paths": {subject: dict(zip(("index", "raw"), final_review_paths(subject)))
                          for subject in FINAL_SUBJECTS},
                "rawSchema": {
                    "schemaVersion": 1, "kind": "pp-w-independent-disposition-final-review",
                    "subject": "one exact required subject",
                    "verdict": "APPROVE or REQUEST_CHANGES",
                    "reviewer": {"name": "nonblank real reviewer identity", "kind": "AI or human",
                                 "independentOfImplementation": True,
                                 "humanReview": "true iff kind is human", "operator": False},
                    "binding": {
                        "recordSetSha256": "proposal recordSetSha256",
                        "sourceArtifactsSha256": "proposal sourceArtifactsSha256",
                        "historicalLedgerSha256": "exact authorization oldLedgerSha256",
                        "authorizationReference": GRANT,
                        "permanentLiabilityMicroUsd": 51_040_000,
                        "ceilingMicroUsd": 1_000_000_000, "targetEpochId": EPOCH,
                        "actualHistoricalCost": None, "futureUnknownPolicy": "halt",
                    },
                    "scope": final_scope("<exact subject>"),
                    "findings": ["nonempty reviewer findings; no fabricated approvals"],
                    "limitations": ["nonempty scope/assumption/missing-evidence limitations"],
                },
                "indexSchema": review_index(
                    "<exact subject>", {"path": "<fixed raw path>", "sha256": "<raw byte hash>"},
                    "<verbatim raw verdict>"),
                "additionalFieldsAllowed": False,
                "historicalOnlyAndDraftReviewsAreFinalApprovals": False,
            },
            "activation": {
                "command": "ppw-gateway-register-disposition.py --activate "
                           "--confirmed-record-set-sha256 <recordSetSha256>",
                "inputs": "unchanged proposal and source map plus all three genuine final APPROVE "
                          "raw artifacts and typed indexes; unchanged pre-disposition analysis",
                "record": ACTIVATION.name,
                "metadataOnly": True,
                "supersession": "preserve original analysis bytes; publish activation then "
                               "atomically replace active analysis wrapper",
            },
            "operation": {
                "commands": ["ppw-gateway-dispose.py inspect",
                             "ppw-gateway-dispose.py apply --confirmed-proof-sha256 <proofSha256>",
                             "ppw-instrument.py run --registration <activated profile> "
                             "--epoch-id w-rows-pilot-gateway-003 --stage pilot --confirm-paid-epoch"],
                "allRequireExactActivation": True,
                "externalManualGate": "final-head GitHub source review, CI, merge and separate "
                                      "parent/operator readiness/action; not machine-attested here",
            },
        },
        "hashSubjects": {
            "encoding": "UTF-8 JSON sorted keys, indent=2, trailing newline (authorization_bytes)",
            "rawReviews": "SHA-256 of exact immutable reviewer bytes; never reserialized",
            "sourceArtifactsSha256": "SHA-256 of encoded exact path-to-source-byte-hash map",
            "recordSetSha256": "SHA-256 of encoded record-set object excluding recordSetSha256",
            "criticalRecords": "authorization, plan, profile, proof, price, inventories, stopped "
                               "snapshot, evidence, immutable lineage, forecast artifacts, "
                               "stage/model/source-inspection records and history indexes/raw reviews",
            "excludedToAvoidCycles": "record set itself, activation, final-review indexes/raw files, "
                                    "active analysis wrapper and final Git commit identity",
            "profile": "names fixed activation basename only, never its future hash",
            "portableAnalysis": "archive-local critical records, activation, every index AND raw "
                                "review, and source map files required; no canonical file fallback",
        },
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


def review_index(subject, artifact, verdict):
    """An index is a typed pointer, never a replacement for the reviewer's bytes."""
    return {
        "schemaVersion": 1, "kind": "pp-w-disposition-review-index",
        "subject": subject, "artifact": artifact, "verdict": verdict,
    }


def _indexed_review(root, index, subject):
    require(isinstance(index, dict) and set(index) == {
        "schemaVersion", "kind", "subject", "artifact", "verdict",
    } and type(index["schemaVersion"]) is int and index["schemaVersion"] == 1
        and index["kind"] == "pp-w-disposition-review-index"
        and index["subject"] == subject
        and index["verdict"] in ("APPROVE", "REQUEST_CHANGES"),
        "review index has a wrong subject or schema")
    raw = _archive_proof(root, index["artifact"], subject + " raw review")
    require(raw.get("verdict") == index["verdict"], "review index broadens the raw verdict")
    return raw


def _history_review(root, index, subject):
    expected = HISTORY[subject]
    require(index == review_index(subject, {
        "path": expected["path"], "sha256": expected["sha256"],
    }, expected["verdict"]), "historical review index must resolve the unchanged pinned audit")
    raw = _indexed_review(root, index, subject)
    require(raw.get("kind") == expected["kind"]
            and raw.get("reviewer", {}).get("humanReview") is False
            and raw.get("verdictScope", {}).get("newImplementationApproved") is False
            and raw["verdictScope"].get("operatorExecutionApproved") is False,
            "historical/draft review scope was broadened")
    if subject == "historical-bound":
        require(raw["verdictScope"].get("historicalBoundOnly") is True,
                "historical approval is historical-bound ONLY")
    else:
        require(raw["verdictScope"].get("generatedOperationalRecordsApproved") is False,
                "draft description cannot approve final records")
    return raw


def _review(path, subject):
    require(Path(path).is_file(), "missing independent review index: " + str(path))
    value = load(path)
    _history_review(ROOT, value, subject)
    return value


def planning_projection(plan):
    """Integer-only illustration using the unchanged full-444 historical reference."""
    spending = module("ppw-spending.py")
    forecast = plan["forecast"]
    ceiling = spending.units(plan["ceilingUsd"])
    reference_cost = spending.units(forecast["estimatedFullPilotUsd"])
    sensitivities = [spending.units(item["fullPilotUsd"]) for item in forecast["sensitivities"]]
    permanent = 51_040_000
    require(ceiling == 1_000_000_000
            and forecast["plannedInvocations"] == 444
            and reference_cost == 900_360_000
            and sensitivities == [990_400_000, 1_125_450_000],
            "current planning requires the unchanged full-444 historical reference")
    return {
        "schemaVersion": 1, "kind": "pp-w-conditional-current-budget-planning",
        "historicalForecast": {
            "plan": {"path": OLD_PLAN.name, "sha256": EXPECTED_OLD_PLAN_SHA256},
            "registration": copy.deepcopy(plan["forecastRegistration"]),
            "plannedInvocations": 444,
        },
        "ceilingMicroUsd": ceiling,
        "permanentEncumbranceMicroUsd": permanent,
        "initialAvailableMicroUsd": ceiling - permanent,
        "unchangedHistoricalReferenceMicroUsd": reference_cost,
        "conservativeCombinedMicroUsd": reference_cost + permanent,
        "planningHeadroomMicroUsd": ceiling - permanent - reference_cost,
        "pricingSensitivityPlusEncumbranceMicroUsd": sensitivities[0] + permanent,
        "trafficStressPlusEncumbranceMicroUsd": sensitivities[1] + permanent,
        "conditionalIllustration": True, "observations": False,
        "completionGuarantee": False, "newTaskMeanEstablished": False,
        "newApprovedForecast": False, "remaining442CheapnessAssumed": False,
        "actualHistoricalCost": None,
    }


def validate_planning(plan):
    require(plan.get("forecastUse") == "immutable-historical-reference-not-current-headroom"
            and encoded(plan.get("currentBudgetPlanning")) == encoded(planning_projection(plan)),
            "current-budget planning projection differs (headroom must be USD 48.60)")


def final_review_paths(subject):
    require(subject in FINAL_SUBJECTS, "unknown final review subject")
    base = "gateway-evidence/gateway-" + subject + "-1436"
    return base + "-index.json", base + "-review.json"


def final_binding(record_set, authorization):
    return {
        "recordSetSha256": record_set["recordSetSha256"],
        "sourceArtifactsSha256": record_set["sourceArtifactsSha256"],
        "historicalLedgerSha256": authorization["oldLedgerSha256"],
        "authorizationReference": GRANT,
        "permanentLiabilityMicroUsd": 51_040_000, "ceilingMicroUsd": 1_000_000_000,
        "targetEpochId": EPOCH, "actualHistoricalCost": None,
        "futureUnknownPolicy": "halt",
    }


def final_scope(subject):
    return {
        "approvedSubject": subject, "historicalBoundOnly": False,
        "descriptionOnly": False, "operatorExecutionApproved": False,
        "additionalFundsApproved": False, "scientificChangesApproved": False,
        "stage2Approved": False, "releaseApproved": False,
    }


def validate_final_review(raw, subject, record_set, authorization, approved=True):
    require(isinstance(raw, dict) and set(raw) == {
        "schemaVersion", "kind", "subject", "verdict", "reviewer",
        "binding", "scope", "findings", "limitations",
    } and type(raw["schemaVersion"]) is int and raw["schemaVersion"] == 1
        and raw["kind"] == "pp-w-independent-disposition-final-review"
        and raw["subject"] == subject and subject in FINAL_SUBJECTS
        and raw["verdict"] in ("APPROVE", "REQUEST_CHANGES")
        and encoded(raw["binding"]) == encoded(final_binding(record_set, authorization))
        and encoded(raw["scope"]) == encoded(final_scope(subject)),
        "final review has wrong subject, record set, source map, or financial scope")
    reviewer = raw["reviewer"]
    require(isinstance(reviewer, dict) and set(reviewer) == {
        "name", "kind", "independentOfImplementation", "humanReview", "operator",
    } and isinstance(reviewer["name"], str) and reviewer["name"].strip()
        and reviewer["kind"] in ("AI", "human")
        and reviewer["independentOfImplementation"] is True
        and reviewer["humanReview"] is (reviewer["kind"] == "human")
        and reviewer["operator"] is False
        and isinstance(raw["findings"], list) and raw["findings"]
        and all(isinstance(item, str) and item.strip() for item in raw["findings"])
        and isinstance(raw["limitations"], list) and raw["limitations"]
        and all(isinstance(item, str) and item.strip() for item in raw["limitations"]),
        "final review must retain independent reviewer identity, findings and limitations")
    require(not approved or raw["verdict"] == "APPROVE",
            "REQUEST_CHANGES is not final approval")


def _critical_references(evidence, documents):
    refs = [value for value in evidence.values()
            if isinstance(value, dict) and set(value) == {"path", "sha256"}]
    refs += list(evidence["historicalLineage"].values())
    profile = documents["executionProfile"]
    refs += [profile[name] for name in (
        "stageRegistration", "modelRegistration", "sourceInspectionEvidence")]
    refs += list(documents["spendingPlan"]["forecastRegistration"]["artifacts"].values())
    for name in ("financialBoundReview", "methodsReview"):
        refs.append(documents[name]["artifact"])
    refs.append({"path": EVIDENCE.name, "sha256": sha_bytes(encoded(evidence))})
    result = {}
    for ref in refs:
        require(ref["path"] not in result or result[ref["path"]] == ref["sha256"],
                "critical record aliases disagree")
        result[ref["path"]] = ref["sha256"]
    return result


def make_record_set(evidence, documents):
    sources = dict(documents["authorization"]["targetHarnessArtifacts"])
    sources.update({name: digest(BENCH / name) for name in EXTRA_REVIEW_SOURCES})
    body = {
        "schemaVersion": 1, "kind": "pp-w-immutable-disposition-proposal",
        "targetEpochId": EPOCH,
        "criticalRecords": _critical_references(evidence, documents),
        "sourceArtifacts": sources, "sourceArtifactsSha256": sha_bytes(encoded(sources)),
    }
    return dict(body, recordSetSha256=sha_bytes(encoded(body)))


def _validate_record_set(root, evidence, documents, record_set, live):
    require(isinstance(record_set, dict) and set(record_set) == {
        "schemaVersion", "kind", "targetEpochId", "criticalRecords",
        "sourceArtifacts", "sourceArtifactsSha256", "recordSetSha256",
    }, "proposal record set schema differs")
    body = {key: value for key, value in record_set.items() if key != "recordSetSha256"}
    sources = record_set["sourceArtifacts"]
    require(type(record_set["schemaVersion"]) is int and record_set["schemaVersion"] == 1
            and record_set["kind"] == "pp-w-immutable-disposition-proposal"
            and record_set["targetEpochId"] == EPOCH
            and record_set["recordSetSha256"] == sha_bytes(encoded(body))
            and record_set["sourceArtifactsSha256"] == sha_bytes(encoded(sources))
            and record_set["criticalRecords"] == _critical_references(evidence, documents)
            and set(sources) == set(documents["authorization"]["targetHarnessArtifacts"])
            | set(EXTRA_REVIEW_SOURCES)
            and all(sources[name] == sha for name, sha
                    in documents["authorization"]["targetHarnessArtifacts"].items()),
            "proposal record set or source map identity differs")
    spending = module("ppw-spending.py")
    for path, sha in record_set["criticalRecords"].items():
        spending.pinned_file(root, {"path": path, "sha256": sha},
                             "critical proposal record " + path)
    source_root = BENCH if live else Path(root) / "admission/disposition-sources"
    for path, sha in sources.items():
        spending.pinned_file(source_root, {"path": path, "sha256": sha}, "reviewed source")
    validate_planning(documents["spendingPlan"])
    _history_review(root, documents["financialBoundReview"], "historical-bound")
    _history_review(root, documents["methodsReview"], "methods-draft-description")


def _activation_value(root, record_set, authorization):
    reviews = {}
    for subject in FINAL_SUBJECTS:
        index_path, raw_path = final_review_paths(subject)
        index = load(Path(root) / index_path)
        raw = _indexed_review(root, index, subject)
        require(index["artifact"]["path"] == raw_path, "final review raw path differs")
        validate_final_review(raw, subject, record_set, authorization)
        reviews[subject] = {"path": index_path, "sha256": digest(Path(root) / index_path)}
    return {
        "schemaVersion": 1, "kind": "pp-w-prospective-disposition-activation",
        "targetEpochId": EPOCH,
        "recordSet": {"path": RECORD_SET.name, "sha256": digest(Path(root) / RECORD_SET.name)},
        "recordSetSha256": record_set["recordSetSha256"],
        "sourceArtifactsSha256": record_set["sourceArtifactsSha256"],
        "finalReviews": reviews,
        "externalManualGate": "final-head-github-review-and-separate-parent-operation",
        "externalManualGateMachineVerified": False,
        "ledgerApplied": False, "collectionStarted": False,
    }


def validate_activation(root, evidence, documents, live=True, active=True):
    root = Path(root)
    profile = documents["executionProfile"]
    require(profile.get("activationRecord") == ACTIVATION.name,
            "profile must name the fixed prospective activation record")
    record_set = load(root / RECORD_SET.name)
    _validate_record_set(root, evidence, documents, record_set, live)
    activation = load(root / ACTIVATION.name)
    timestamp = activation.get("activatedAt")
    require(isinstance(timestamp, str)
            and datetime.fromisoformat(timestamp).utcoffset() is not None,
            "activation timestamp must have an explicit timezone")
    require(encoded(activation) == encoded(dict(
        _activation_value(root, record_set, documents["authorization"]), activatedAt=timestamp)),
        "activation does not bind exact final reviews and proposal/source identities")
    if live and active:
        manifest = load(OLD_ANALYSIS)
        require(manifest.get("recoveryStatus") == "historical-liability-registered"
                and manifest.get("collectionAuthorized") is False
                and manifest.get("supersedes") == {
                    "path": PRE_DISPOSITION_ANALYSIS.relative_to(BENCH).as_posix(),
                    "sha256": EXPECTED_OLD_ANALYSIS_SHA256}
                and manifest.get("gatewayExecutionProfile") == {
                    "path": PROFILE.relative_to(BENCH).as_posix(), "sha256": digest(PROFILE)}
                and manifest.get("gatewayDispositionEvidence") == {
                    "path": EVIDENCE.relative_to(BENCH).as_posix(), "sha256": digest(EVIDENCE)}
                and manifest.get("gatewayDispositionActivation") == {
                    "path": ACTIVATION.relative_to(BENCH).as_posix(), "sha256": digest(ACTIVATION)}
                and digest(PRE_DISPOSITION_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "prospective activation and active analysis registration differ")
    return record_set, activation


def archival_references(evidence, documents):
    record_set, activation = validate_activation(ROOT, evidence, documents)
    refs = [{"path": path, "sha256": sha}
            for path, sha in record_set["criticalRecords"].items()]
    refs += [activation["recordSet"],
             {"path": ACTIVATION.name, "sha256": digest(ACTIVATION)}]
    for index_ref in activation["finalReviews"].values():
        refs += [index_ref, load(ROOT / index_ref["path"])["artifact"]]
    return refs, record_set["sourceArtifacts"]


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
    if values:
        return "archived-files", values
    return "original-pins-only", dict(pins["harnessArtifacts"])


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
    source_kind, source_hashes = _source_map(archive, inventory, pins)
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
        "sourceHashes": source_hashes,
        "sourceEvidenceKind": source_kind,
        "sourceCommit": pins["harnessCommit"],
    }


def _load_evidence(path=None, require_activation=True):
    spending = module("ppw-spending.py")
    path = EVIDENCE if path is None else Path(path)
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
    if require_activation:
        validate_activation(ROOT, value, documents)
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
    evidence, documents = _load_evidence()
    adjudication = module("ppw-pilot-adjudicate.py")
    manifest = adjudication.validate_analysis_registration()
    require(manifest.get("recoveryStatus") == "historical-liability-registered"
            and manifest.get("gatewayDispositionEvidence") == {
                "path": EVIDENCE.relative_to(BENCH).as_posix(),
                "sha256": digest(EVIDENCE),
            }, "the reviewed #1436 disposition registration is not active")
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
    require(digest(OLD_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256
            and not ACTIVATION.exists() and not ACTIVATION.is_symlink()
            and not (BENCH / "epochs" / EPOCH).exists(),
            "proposal generation cannot supersede active analysis or refresh an activation")
    require(digest(OLD_PROFILE) == EXPECTED_OLD_PROFILE_SHA256
            and digest(OLD_PLAN) == EXPECTED_OLD_PLAN_SHA256
            and digest(OLD_AUTHORIZATION) == EXPECTED_OLD_AUTHORIZATION_SHA256,
            "the registered #1434 profile/plan/authorization/analysis lineage changed")
    bound = _review(BOUND_REVIEW, "historical-bound")
    methods = _review(METHODS_REVIEW, "methods-draft-description")
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
    # Current-source terminal detection cannot describe a frozen predecessor.
    # Use the financial core's historical normalization, on an immutable RO connection.
    with sqlite3.connect(ledger.resolve().as_uri() + "?mode=ro&immutable=1", uri=True) as db:
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA query_only=ON")
        old_snapshot = core._snapshot(*core._database_snapshot(db))
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
        "evidenceMode": REQUIRED_EVIDENCE_MODE,
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
    plan["forecastUse"] = "immutable-historical-reference-not-current-headroom"
    plan["currentBudgetPlanning"] = planning_projection(plan)
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
        "activationRecord": ACTIVATION.name,
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
        "reviewHistoryOnly": {
            "historicalBound": bound,
            "draftMethodsDescription": methods,
        },
    }
    pre_analysis = load(OLD_ANALYSIS)
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
    documents[RECORD_SET] = make_record_set(evidence, {
        **{name: documents[ROOT / evidence[name]["path"]] for name in (
            "authorization", "executionProfile", "spendingPlan")},
        "financialBoundReview": bound, "methodsReview": methods,
    })
    return documents


def _active_analysis(activation):
    adjudication = module("ppw-pilot-adjudicate.py")
    artifacts = {
        relative: (sha_bytes(encoded(activation)) if BENCH / relative == ACTIVATION
                   else digest(BENCH / relative))
        for relative in adjudication.analysis_artifacts("historical-liability-registered")
    }
    evidence = load(EVIDENCE)
    pre_analysis = load(PRE_DISPOSITION_ANALYSIS)
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
        "gatewayDispositionActivation": {
            "path": ACTIVATION.relative_to(BENCH).as_posix(),
            "sha256": sha_bytes(encoded(activation)),
        },
        "financialProjection": evidence["financialProjection"],
    })
    return analysis


def document_identities(documents):
    require(isinstance(documents, dict), "documents must be a path/value mapping")
    return {
        Path(path).relative_to(ROOT).as_posix(): (
            EXPECTED_OLD_ANALYSIS_SHA256 if Path(path) == PRE_DISPOSITION_ANALYSIS
            else sha_bytes(encoded(value)))
        for path, value in sorted(documents.items(), key=lambda item: str(item[0]))
    }


def write_documents(documents, refresh_unexecuted=False):
    """Write an unapproved proposal, preserving the active analysis bytes exactly."""
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
    evidence = documents[OUTPUTS["evidence"]]
    require(documents[OUTPUTS["recordSet"]] == make_record_set(evidence, {
        "authorization": authorization, "executionProfile": documents[OUTPUTS["profile"]],
        "spendingPlan": documents[OUTPUTS["plan"]],
        "financialBoundReview": load(ROOT / evidence["financialBoundReview"]["path"]),
        "methodsReview": load(ROOT / evidence["methodsReview"]["path"]),
    }), "proposed record set must bind the exact documents and sources being written")
    critical = documents[OUTPUTS["recordSet"]]["criticalRecords"]
    require(all(critical.get(path.relative_to(ROOT).as_posix()) == sha_bytes(encoded(value))
                for path, value in documents.items()
                if path not in (RECORD_SET, PRE_DISPOSITION_ANALYSIS)),
            "proposed document bytes differ from the critical record hashes")
    validate_planning(documents[OUTPUTS["plan"]])
    current = OLD_ANALYSIS.read_bytes()
    require(sha_bytes(current) == EXPECTED_OLD_ANALYSIS_SHA256
            and not ACTIVATION.exists() and not ACTIVATION.is_symlink(),
            "active analysis changed or proposal is already activated")
    spending = module("ppw-spending.py")
    ledger = spending.gateway_ledger_location(documents[OUTPUTS["plan"]]["ledgerBinding"])
    require(digest(ledger) == core.EXPECTED_OLD_LEDGER_SHA256
            and digest(ledger.parent / BACKUP_NAME) == core.EXPECTED_OLD_BACKUP_SHA256
            and not (ledger.parent / DISPOSITION_BACKUP_NAME).exists()
            and core.archive_inventory(ORIGINAL_ARCHIVE) == documents[OUTPUTS["originalInventory"]]
            and core.archive_inventory(FAILED_ARCHIVE) == documents[OUTPUTS["failedInventory"]],
            "only exact unexecuted old ledger/backup/archive state may be proposed or refreshed")
    if not refresh_unexecuted:
        require(digest(OLD_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "active analysis manifest changed before registration")
        for path in OUTPUTS.values():
            require(not path.exists() and not path.is_symlink(),
                    "prospective output already exists: " + str(path))
    else:
        require(digest(PRE_DISPOSITION_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "preserved pre-disposition manifest changed")
        for path in OUTPUTS.values():
            require(path.is_file() and not path.is_symlink(),
                    "refresh cannot introduce a missing reviewed output")
    staging = []
    try:
        for path, value in documents.items():
            if path == PRE_DISPOSITION_ANALYSIS and refresh_unexecuted:
                continue
            stage = path.with_name(path.name + ".unexecuted-1436-new")
            require(not stage.exists(), "stale registration staging file: " + str(stage))
            stage.write_bytes(current if path == PRE_DISPOSITION_ANALYSIS else encoded(value))
            staging.append((stage, path))
        for stage, path in staging:
            if refresh_unexecuted:
                os.replace(stage, path)
            else:
                require(not path.exists(),
                        "prospective output appeared during write: " + str(path))
                os.replace(stage, path)
        require(PRE_DISPOSITION_ANALYSIS.read_bytes() == current,
                "pre-disposition manifest bytes were not preserved")
        require(OLD_ANALYSIS.read_bytes() == current, "proposal changed active analysis")
    finally:
        for stage, _ in staging:
            stage.unlink(missing_ok=True)


def activate(confirmed_record_set_sha256):
    """Prospective metadata only. Never inspect/apply a ledger or start collection."""
    require(digest(OLD_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256
            and digest(PRE_DISPOSITION_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256
            and not ACTIVATION.exists() and not ACTIVATION.is_symlink()
            and not (BENCH / "epochs" / EPOCH).exists(),
            "activation requires the unchanged old analysis and an unexecuted proposal")
    evidence, documents = _load_evidence(require_activation=False)
    record_set = load(RECORD_SET)
    _validate_record_set(ROOT, evidence, documents, record_set, live=True)
    require(confirmed_record_set_sha256 == record_set["recordSetSha256"],
            "confirmation must name the exact proposed record set SHA-256")
    activation = dict(
        _activation_value(ROOT, record_set, documents["authorization"]),
        activatedAt=datetime.now(timezone.utc).isoformat())
    analysis = _active_analysis(activation)
    stage = OLD_ANALYSIS.with_name(OLD_ANALYSIS.name + ".activation-1436-new")
    try:
        with stage.open("xb") as stream:
            stream.write(encoded(analysis))
        # Publish activation first; a crash before the wrapper swap is fail-closed.
        with ACTIVATION.open("xb") as stream:
            stream.write(encoded(activation))
        require(digest(OLD_ANALYSIS) == EXPECTED_OLD_ANALYSIS_SHA256,
                "active analysis changed during activation")
        os.replace(stage, OLD_ANALYSIS)
        validate_activation(ROOT, evidence, documents)
    finally:
        stage.unlink(missing_ok=True)
    return activation


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
                "sourceEvidenceKind": attempt["sourceEvidenceKind"],
                "sourceCommit": attempt["sourceCommit"],
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
    core = module("ppw-gateway-disposition.py")
    core._validate_archive_pins(
        epoch / copied_root, inventory, authorization["predecessorArchives"][index], attempt,
        authorization["oldSnapshot"]["binding"]["harnessArtifacts"] if index == 1 else None)
    core._validate_attempt_files(epoch / copied_root, inventory, attempt)
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
    require(authorization["evidenceMode"] == REQUIRED_EVIDENCE_MODE,
            "portable archive does not have the registered disposition authority")
    validate_activation(epoch, evidence, {
        "authorization": authorization, "executionProfile": profile,
        "spendingPlan": plan, "financialBoundReview": bound_review,
        "methodsReview": methods_review,
    }, live=False, active=False)
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
