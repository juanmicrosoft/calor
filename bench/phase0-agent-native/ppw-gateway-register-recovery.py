#!/usr/bin/env python3
"""Generate the fixed #1432 registration after reviewed native wire evidence exists."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import sys


BENCH = Path(__file__).resolve().parent
ROOT = BENCH / "registrations/ppw-rows-stage1"
EPOCH = "w-rows-pilot-gateway-002"
FAILED_EPOCH = "w-rows-pilot-gateway-001"
WIRE = ROOT / "gateway-evidence/gateway-native-wire-1432-evidence.json"
STARTUP = ROOT / "gateway-evidence/gateway-native-startup-1432-evidence.json"
OLD_PROFILE = ROOT / "gateway-execution-profile.json"
OLD_PLAN = ROOT / "gateway-spending-plan.json"
OLD_AUTHORIZATION = ROOT / "gateway-authorization.json"
OLD_PROFILE_SHA256 = "8b6e1d2243cdb807a082e3da39b3b5333dc8062d2118395285924aa10ab38d2b"
OLD_PLAN_SHA256 = "8847c46ec5e07fd1e34c26261bb5ca0ef30372e63ecba111dbadf64834c7e882"
OLD_AUTHORIZATION_SHA256 = "9eb64e3b11bbfad69a932c4075ba10cea39d1d21de2e860d3b2eefaacb9a3050"
OUTPUTS = {
    "instrument": ROOT / "gateway-instrument-amendment-1432.json",
    "authorization": ROOT / "gateway-authorization-1432.json",
    "plan": ROOT / "gateway-spending-plan-1432.json",
    "profile": ROOT / "gateway-execution-profile-1432.json",
    "inventory": ROOT / "gateway-recovery-archive-inventory-1432.json",
    "snapshot": ROOT / "gateway-recovery-original-snapshot-1432.json",
    "proof": ROOT / "gateway-recovery-proof-1432.json",
    "evidence": ROOT / "gateway-recovery-evidence-1432.json",
    "analysis": ROOT / "analysis-registration.json",
}


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W recovery registration: " + message)


def encoded(value):
    return (json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n").encode()


def sha_bytes(value):
    return hashlib.sha256(value).hexdigest()


def proof(path, value):
    return {"path": path.relative_to(ROOT).as_posix(), "sha256": sha_bytes(encoded(value))}


def validate_wire_evidence(value):
    registration = module("ppw-gateway-registration.py")
    spending = module("ppw-spending.py")
    isolation = module("ppw-gateway-client.py")
    require(value.get("kind") == "pp-w-engineering-no-forward-native-probe"
            and value.get("success") is True and value.get("upstreamRequests") == 0
            and value.get("experimentalObservations") == 0
            and value.get("clientSha256") == isolation.CLIENT_SHA256
            and value.get("clientFlags") == isolation.registered_client_flags(),
            "wire evidence identity or no-forward result differs")
    observations = value.get("observations")
    require(isinstance(observations, list) and observations
            and all(item.get("messagesPath") is True
                    and item.get("credentialHeaderPresent") is True
                    and item.get("priceContractAccepted") is True
                    and item.get("wireContractAccepted") is True
                    and item.get("unclassifiedBetaCount") == 0
                    for item in observations),
            "wire evidence did not pass both transport and price contracts")
    require(set(value.get("sourceHashes", {})) == registration.WIRE_SOURCE_HASHES
            and all(spending.digest(BENCH / name) == digest
                    for name, digest in value["sourceHashes"].items()),
            "wire evidence does not bind the exact five reviewed sources")


def read_old_binding(ledger):
    db = sqlite3.connect("file:" + str(ledger) + "?mode=ro", uri=True)
    try:
        db.execute("PRAGMA query_only=ON")
        row = db.execute("SELECT binding FROM scope WHERE id=1").fetchone()
        require(row is not None, "failed ledger scope is missing")
        return json.loads(row[0])
    finally:
        db.close()


def build_documents():
    spending = module("ppw-spending.py")
    budget = module("ppw-gateway-budget.py")
    recovery = module("ppw-gateway-recovery.py")
    gateway_registration = module("ppw-gateway-registration.py")
    require(WIRE.is_file(), "missing reviewed wire evidence: " + str(WIRE))
    wire = json.loads(WIRE.read_text(encoding="utf-8"))
    validate_wire_evidence(wire)
    require(STARTUP.is_file(), "missing actual native startup evidence")
    gateway_registration.validate_native_startup(json.loads(STARTUP.read_text(encoding="utf-8")))
    require(spending.digest(OLD_PROFILE) == OLD_PROFILE_SHA256
            and spending.digest(OLD_PLAN) == OLD_PLAN_SHA256
            and spending.digest(OLD_AUTHORIZATION) == OLD_AUTHORIZATION_SHA256,
            "the #1431 profile, plan, or authorization bytes changed")

    old_profile = json.loads(OLD_PROFILE.read_text(encoding="utf-8"))
    old_plan = json.loads(OLD_PLAN.read_text(encoding="utf-8"))
    require(wire.get("toolShellSha256") == old_plan["clientControl"]["shellSha256"],
            "native wire probe did not use the registered shell")
    old_authorization = json.loads(OLD_AUTHORIZATION.read_text(encoding="utf-8"))
    registration = gateway_registration.resolve_profile(OLD_PROFILE)
    selected = registration["stages"]["pilot"]
    slots = ["%s/%s/%d" % (task, arm, run)
             for run in range(1, selected["runsPerArm"] + 1)
             for task in registration["tasks"]
             for arm in ("calor-permissive", "calor-strict")]
    require(len(slots) == 444, "frozen slot schedule changed")

    ledger = spending.gateway_ledger_location(old_authorization["ledgerBinding"])
    archive = BENCH / "epochs" / FAILED_EPOCH
    old_binding = read_old_binding(ledger)
    inventory = recovery.archive_inventory(archive)
    failed_snapshot = json.loads(
        (archive / "collection-outcome.json").read_text(encoding="utf-8"))["spending"]
    failed_ledger_sha256 = spending.digest(ledger)
    preserved = [slots[0]]

    amendment = {
        "schemaVersion": 1,
        "kind": "pp-w-prospective-spending-instrument-amendment",
        "stage": "pilot",
        "supersedes": {"path": OLD_PROFILE.name.replace(
            "execution-profile", "instrument-amendment"), "sha256":
            spending.digest(ROOT / "gateway-instrument-amendment.json")},
        "replacementHarnessArtifacts": spending.artifact_manifest(spending.GATEWAY),
        "reason": "Reviewed #1432 exact SDK header admission, workspace-local native temporaries, "
                  "registered Bash/tool PATH preservation, and zero-request recovery integration.",
    }
    amendment_ref = proof(OUTPUTS["instrument"], amendment)
    selected = dict(selected, epochId=EPOCH, instrumentAmendment=amendment_ref)
    protocol = spending.protocol_identity(registration, selected, "pilot", EPOCH)
    authorization = dict(old_authorization)
    authorization.update({
        "kind": "pp-w-zero-request-recovery-authorization",
        "epochId": EPOCH,
        "targetEpochId": EPOCH,
        "oldEpochId": FAILED_EPOCH,
        "additionalAllowance": False,
        "ledgerReset": False,
        "supersedes": {"path": OLD_AUTHORIZATION.name, "sha256": OLD_AUTHORIZATION_SHA256},
        "failedLedgerSha256": failed_ledger_sha256,
        "failedArchivePath": "epochs/" + FAILED_EPOCH,
        "failedArchiveInventorySha256": inventory["sha256"],
        "backupName": "epic1254-pilot.failed-1432.sqlite3",
        "preservedAttemptedSlots": preserved,
        "plannedSlots": slots,
        "targetInvariants": {
            "stage": "pilot", "epochId": EPOCH, "priceSha256": budget.price_identity(),
            "protocolSha256": protocol,
            "harnessArtifacts": amendment["replacementHarnessArtifacts"],
        },
        "oldBinding": old_binding,
        "recoveryConditions": {
            "requestCount": 0, "completedSlots": 0, "exposureMicroUsd": 0,
            "ledgerState": "INCOMPLETE_POLICY", "replacementAttempts": 0,
        },
        "methodClarification":
            "The first scheduled launch remains an invalid censored attempt; only 443 slots continue.",
        "methodClarificationReference":
            "https://github.com/juanmicrosoft/calor/issues/1432#issuecomment-5627948180",
    })
    authorization_ref = proof(OUTPUTS["authorization"], authorization)

    plan = dict(old_plan)
    plan.update({
        "epochId": EPOCH,
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol,
        "supersedes": {"path": OLD_PLAN.name, "sha256": OLD_PLAN_SHA256},
        "recovery": {
            "failedEpochId": FAILED_EPOCH,
            "plannedInvocations": 444,
            "preservedAttemptedSlots": preserved,
            "continuationInvocations": 443,
            "replacementAttempts": 0,
            "sameExperimentCeiling": True,
        },
    })
    plan_ref = proof(OUTPUTS["plan"], plan)

    profile = dict(old_profile)
    profile.update({
        "id": "request-reserving-gateway-recovery-1432",
        "epochId": EPOCH,
        "effectiveOn": "independently reviewed merge of the #1432 implementation PR",
        "supersedes": {"path": OLD_PROFILE.name, "sha256": OLD_PROFILE_SHA256},
        "spendAuthorization": authorization_ref,
        "spendingPlan": plan_ref,
        "instrumentAmendment": amendment_ref,
        "transportEvidence": {
            "path": WIRE.relative_to(ROOT).as_posix(), "sha256": spending.digest(WIRE),
        },
        "nativeStartupEvidence": {
            "path": STARTUP.relative_to(ROOT).as_posix(), "sha256": spending.digest(STARTUP),
        },
        "recovery": {
            "failedEpochId": FAILED_EPOCH,
            "preservedAttemptedSlots": preserved,
            "continuationInvocations": 443,
            "automaticRecovery": False,
        },
    })
    profile_ref = proof(OUTPUTS["profile"], profile)
    target_binding = {
        "stage": "pilot", "epochId": EPOCH, "priceSha256": budget.price_identity(),
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol, "planSha256": plan_ref["sha256"],
        "harnessArtifacts": amendment["replacementHarnessArtifacts"], "plannedSlots": slots,
    }
    backup = ledger.parent / authorization["backupName"]
    inspection = recovery.inspect_zero_request_recovery(
        ledger, archive, ledger.parent, backup,
        expected_old_binding=old_binding, target_binding=target_binding,
        expected_ledger_sha256=failed_ledger_sha256,
        expected_archive_inventory_sha256=inventory["sha256"],
        recovery_registration_sha256=authorization_ref["sha256"])
    inventory_ref = proof(OUTPUTS["inventory"], inventory)
    snapshot_ref = proof(OUTPUTS["snapshot"], failed_snapshot)
    inspection_ref = proof(OUTPUTS["proof"], inspection)
    evidence = {
        "schemaVersion": 1,
        "kind": "pp-w-zero-request-recovery-evidence",
        "issue": "https://github.com/juanmicrosoft/calor/issues/1432",
        "rootCauseReference":
            "https://github.com/juanmicrosoft/calor/issues/1432#issuecomment-5627884715",
        "recoveryAuthorization": authorization_ref,
        "executionProfile": profile_ref,
        "spendingPlan": plan_ref,
        "inspectionProof": inspection_ref,
        "failedArchiveInventory": inventory_ref,
        "failedOperationalSnapshot": snapshot_ref,
        "targetBinding": target_binding,
        "historicalLineage": {
            "profile": {"path": OLD_PROFILE.name, "sha256": OLD_PROFILE_SHA256},
            "plan": {"path": OLD_PLAN.name, "sha256": OLD_PLAN_SHA256},
            "authorization": {
                "path": OLD_AUTHORIZATION.name, "sha256": OLD_AUTHORIZATION_SHA256,
            },
        },
    }
    documents = {
        OUTPUTS["instrument"]: amendment,
        OUTPUTS["authorization"]: authorization,
        OUTPUTS["plan"]: plan,
        OUTPUTS["profile"]: profile,
        OUTPUTS["inventory"]: inventory,
        OUTPUTS["snapshot"]: failed_snapshot,
        OUTPUTS["proof"]: inspection,
        OUTPUTS["evidence"]: evidence,
    }
    adjudication = module("ppw-pilot-adjudicate.py")
    analysis = json.loads(
        (ROOT / "analysis-registration.pre-recovery-1432.json").read_text(encoding="utf-8"))
    artifacts = {}
    for relative in adjudication.ARTIFACTS + adjudication.RECOVERY_ARTIFACTS:
        path = BENCH / relative
        generated = documents.get(path)
        artifacts[relative] = (
            sha_bytes(encoded(generated)) if generated is not None
            else hashlib.sha256(path.read_bytes()).hexdigest()
        )
    analysis.update({
        "artifacts": artifacts,
        "supersedes": {
            "path": adjudication.PRE_RECOVERY_MANIFEST,
            "sha256": artifacts[adjudication.PRE_RECOVERY_MANIFEST],
        },
        "scope": "Reviewed #1432 zero-request recovery and 443-slot continuation; "
                 "the original failed attempt remains invalid/censored in the fixed 444-slot pilot.",
        "effectiveOn": "independently reviewed merge of the #1432 implementation PR",
        "recoveryStatus": "registered",
        "collectionAuthorized": False,
        "gatewayExecutionProfile": {
            "path": adjudication.RECOVERY_PROFILE,
            "sha256": artifacts[adjudication.RECOVERY_PROFILE],
        },
        "gatewayRecoveryEvidence": {
            "path": adjudication.RECOVERY_EVIDENCE,
            "sha256": artifacts[adjudication.RECOVERY_EVIDENCE],
        },
    })
    documents[OUTPUTS["analysis"]] = analysis
    return documents


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--write", action="store_true",
                        help="write the fixed canonical registration files")
    args = parser.parse_args(argv)
    try:
        documents = build_documents()
        if not args.write:
            print(json.dumps({
                "ready": True,
                "outputs": {path.name: sha_bytes(encoded(value))
                            for path, value in documents.items()},
            }, indent=2, sort_keys=True))
            return 0
        for path, value in documents.items():
            path.write_bytes(encoded(value))
        print(json.dumps({"written": [str(path) for path in documents]}, indent=2))
    except (OSError, ValueError, KeyError, TypeError, sqlite3.Error) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    sys.exit(main())
