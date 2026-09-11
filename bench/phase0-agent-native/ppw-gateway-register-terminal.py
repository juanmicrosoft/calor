#!/usr/bin/env python3
"""Generate the #1434 terminal-attempt registration after fresh evidence exists."""
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
ISSUE = "https://github.com/juanmicrosoft/calor/issues/1434"
PR_1433 = "https://github.com/juanmicrosoft/calor/pull/1433"
MERGE_1433 = "ac4ad3a016feb283e5ea7b3d90e6505ecaf6e49b"
ACTUAL_SOURCE_COMMIT = "d705c0c015954cf8bfe4d39040db727bfb2d2309"
WIRE = ROOT / "gateway-evidence/gateway-native-wire-1434-evidence.json"
STARTUP = ROOT / "gateway-evidence/gateway-native-startup-1434-evidence.json"
PRE_TERMINAL_MANIFEST = ROOT / "analysis-registration.pre-terminal-1434.json"
PRIOR = {
    "analysis": (
        PRE_TERMINAL_MANIFEST,
        "d0c1f4735294bfb393768ae09273110da3743601d320a731e3b9cba6c6046c6b",
    ),
    "profile": (
        ROOT / "gateway-execution-profile-1432.json",
        "8066b86aeecf292a8e86002011fee7415c91c9c528ea25727ab4541f3e05ec2f",
    ),
    "instrument": (
        ROOT / "gateway-instrument-amendment-1432.json",
        "3d45adad658c569ab84b34b3a9a91a35f4ca65a09e18ee77c327222b9f1a3f59",
    ),
    "authorization": (
        ROOT / "gateway-authorization-1432.json",
        "c4aa8608d478870922867f2fb892e14ca3efe442ea6fddca64643927f72e2b43",
    ),
    "plan": (
        ROOT / "gateway-spending-plan-1432.json",
        "ebea4e7db9b6ee1fa67a01b343dd182a9ebb49c673ab967b73750ffefa4b55c4",
    ),
    "inventory": (
        ROOT / "gateway-recovery-archive-inventory-1432.json",
        "4f00fdbedcb026eca62386025a826e0e793af1528bb1692b76266cdea74d2c77",
    ),
    "snapshot": (
        ROOT / "gateway-recovery-original-snapshot-1432.json",
        "2a280f67270053f17532bf93a9c51f81969f0f5b6796d0a4aab0ab42cdb5a75e",
    ),
    "proof": (
        ROOT / "gateway-recovery-proof-1432.json",
        "a910b6efb731bc397dbde0aed1ed5a6a42bc85877f7f79ad9f7dafda722eb2f2",
    ),
    "evidence": (
        ROOT / "gateway-recovery-evidence-1432.json",
        "9c3552b34bd1730f6c028a495a18a1db8918e735309b4b21eb0e81e52864643f",
    ),
    "wire": (
        ROOT / "gateway-evidence/gateway-native-wire-1432-evidence.json",
        "f1e95d6d7bda35f1e25c92352189fdc009c92fbf6c98db5619f72e2c270b9d48",
    ),
    "startup": (
        ROOT / "gateway-evidence/gateway-native-startup-1432-evidence.json",
        "00138025791bca2cbc47afd4e22adf19b673851c129fd372e27b2657fc3feed1",
    ),
}
OUTPUTS = {
    "instrument": ROOT / "gateway-instrument-amendment-1434.json",
    "authorization": ROOT / "gateway-authorization-1434.json",
    "plan": ROOT / "gateway-spending-plan-1434.json",
    "profile": ROOT / "gateway-execution-profile-1434.json",
    "inventory": ROOT / "gateway-recovery-archive-inventory-1434.json",
    "snapshot": ROOT / "gateway-recovery-original-snapshot-1434.json",
    "proof": ROOT / "gateway-recovery-proof-1434.json",
    "evidence": ROOT / "gateway-recovery-evidence-1434.json",
    "analysis": ROOT / "analysis-registration.json",
}
INPUT_CONTRACT = {
    "freshEvidence": {
        "wire": {
            "path": WIRE.relative_to(ROOT).as_posix(),
            "kind": "pp-w-engineering-no-forward-native-probe",
            "requirements": [
                "success=true",
                "upstreamRequests=0",
                "experimentalObservations=0",
                "current five-file wire source hashes",
                "registered client identity, flags, shell, price, and wire contracts",
            ],
        },
        "startup": {
            "path": STARTUP.relative_to(ROOT).as_posix(),
            "kind": "SYNTHETIC/native-first-job-gateway-startup-v1",
            "requirements": [
                "outcome=passed",
                "empirical=false",
                "modelInvoked=false",
                "realUpstreamGuardInstalled=true",
                "current startup/test source hashes",
                "native build and test observer journal",
            ],
        },
    },
    "immutablePredecessors": {
        name: {"path": path.relative_to(ROOT).as_posix(), "sha256": sha}
        for name, (path, sha) in PRIOR.items()
    },
    "readOnlyActualEvidence": {
        "epoch": FAILED_EPOCH,
        "ledger": "the canonical git-common-dir ppw-budget/epic1254-pilot.sqlite3",
        "requiredLedgerState": "INCOMPLETE_POLICY",
        "requiredLedgerSha256": "0f32e092fbd760f7273405b4feda7e107b1a5630c9d98668b90d84a323048295",
        "requiredArchiveInventorySha256":
            "1554f1d841f4e455c36c677b2d9d778d7903c2153bd210efadb1ecc5351973e2",
    },
}
OUTPUT_SCHEMA = {
    "schemaVersion": 1,
    "targetEpoch": EPOCH,
    "documents": {
        "instrument": {
            "path": OUTPUTS["instrument"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-prospective-spending-instrument-amendment",
        },
        "authorization": {
            "path": OUTPUTS["authorization"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-terminal-semantics-recovery-authorization",
        },
        "plan": {
            "path": OUTPUTS["plan"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-pilot-spending-plan",
        },
        "profile": {
            "path": OUTPUTS["profile"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-request-gateway-execution-profile",
        },
        "inventory": {
            "path": OUTPUTS["inventory"].relative_to(ROOT).as_posix(),
            "requiredFields": ["byteCount", "fileCount", "files", "sha256"],
        },
        "snapshot": {
            "path": OUTPUTS["snapshot"].relative_to(ROOT).as_posix(),
            "kind": "pp-w-request-gateway-v1",
            "requiredState": "INCOMPLETE_POLICY",
        },
        "proof": {
            "path": OUTPUTS["proof"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-zero-request-gateway-recovery-v1",
        },
        "evidence": {
            "path": OUTPUTS["evidence"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-terminal-semantics-recovery-evidence",
        },
        "analysis": {
            "path": OUTPUTS["analysis"].relative_to(ROOT).as_posix(),
            "schemaVersion": 1,
            "kind": "pp-w-rows-stage1-analysis-registration",
            "recoveryStatus": "terminal-semantics-registered",
        },
    },
    "writesOperationalState": False,
}


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W terminal registration: " + message)


def load_predecessors(spending):
    documents = {}
    for name, (path, expected) in PRIOR.items():
        require(path.is_file() and spending.digest(path) == expected,
                "immutable #1433 predecessor changed: " + path.name)
        documents[name] = json.loads(path.read_text(encoding="utf-8"))
    return documents


def terminal_semantics(budget):
    return {
        "schemaVersion": 1,
        "kind": "pp-w-source-bound-terminal-attempt-semantics-v1",
        "attemptDefinition": "scheduled-client-launch",
        "attemptStart": {
            "kind": budget.ATTEMPT_START_KIND,
            "attemptsPerSlot": 1,
            "producerSources": list(budget.TERMINAL_SOURCE_FILES),
            "requiredBeforeClientInvocation": True,
        },
        "terminalInvalid": {
            "kind": budget.TERMINAL_INVALID_KIND,
            "ledgerEvent": budget.TERMINAL_INVALID_EVENT,
            "admittedClassifications": budget.TERMINAL_CLASSIFICATIONS,
            "clientExitCodeDomain": "integer-0-through-127-excluding-124",
            "requiresActualClientInvocation": True,
            "requiresTrustedAttemptStart": True,
            "requiresReconciledRequests": True,
            "requiresSourceInspection": False,
            "countsAsAccountedSlot": True,
            "countsAsValidCompletion": False,
            "replacementPermitted": False,
        },
        "validTerminal": {
            "clientExitCodeDomain": "integer-0-through-127-excluding-124",
            "nonzeroWithObservedWorkRemainsEligible": True,
            "requiresReconciledRequestTraffic": True,
            "requiresSourceInspection": True,
        },
        "haltConditions": [
            "pricing-or-financial-failure",
            "unknown-or-unreconciled-liability",
            "isolation-failure",
            "interrupted-client-invocation",
            "missing-or-untrusted-terminal-proof",
        ],
        "scientificMethodChange": False,
        "zeroImputationPermitted": False,
        "poolingChangePermitted": False,
    }


def source_history(predecessors, old_binding):
    return {
        "actualFailedCollection": {
            "epochId": FAILED_EPOCH,
            "sourceCommit": ACTUAL_SOURCE_COMMIT,
            "bindingSha256": hashlib.sha256(json.dumps(
                old_binding, sort_keys=True, separators=(",", ":"), allow_nan=False
            ).encode()).hexdigest(),
            "ledgerSha256": predecessors["authorization"]["failedLedgerSha256"],
            "archiveInventorySha256":
                predecessors["authorization"]["failedArchiveInventorySha256"],
        },
        "supersededUnexecutedProposal": {
            "pullRequest": PR_1433,
            "mergeCommit": MERGE_1433,
            "executed": False,
            "profile": {
                "path": PRIOR["profile"][0].name,
                "sha256": PRIOR["profile"][1],
            },
            "recoveryEvidence": {
                "path": PRIOR["evidence"][0].name,
                "sha256": PRIOR["evidence"][1],
            },
        },
        "prospectiveCollection": {
            "issue": ISSUE,
            "epochId": EPOCH,
            "sourceIdentity": "replacementHarnessArtifacts",
        },
    }


def validate_actual_predecessor(
        spending, recovery, legacy, predecessors, ledger, archive, old_binding):
    authorization = predecessors["authorization"]
    require(old_binding == authorization["oldBinding"],
            "actual failed ledger binding differs from the registered original")
    ledger_sha256 = spending.digest(ledger)
    require(ledger_sha256 == authorization["failedLedgerSha256"],
            "actual failed ledger bytes differ from the registered original")
    inventory = recovery.archive_inventory(archive)
    require(inventory == predecessors["inventory"]
            and inventory["sha256"] == authorization["failedArchiveInventorySha256"],
            "actual failed archive differs from the preserved #1433 inventory")
    snapshot = json.loads((archive / "collection-outcome.json").read_text(encoding="utf-8"))[
        "spending"]
    require(snapshot == predecessors["snapshot"],
            "actual failed operational snapshot differs from the preserved #1433 record")
    require(legacy.sha_bytes(legacy.encoded(snapshot))
            == PRIOR["snapshot"][1], "preserved failed snapshot encoding differs")
    return ledger_sha256, inventory, snapshot


def build_documents():
    spending = module("ppw-spending.py")
    budget = module("ppw-gateway-budget.py")
    recovery = module("ppw-gateway-recovery.py")
    gateway_registration = module("ppw-gateway-registration.py")
    legacy = module("ppw-gateway-register-recovery.py")
    require(WIRE.is_file(), "missing root-supplied fresh wire evidence: " + str(WIRE))
    require(STARTUP.is_file(), "missing root-supplied fresh native startup evidence: " + str(STARTUP))
    wire = json.loads(WIRE.read_text(encoding="utf-8"))
    legacy.validate_wire_evidence(wire)
    startup = json.loads(STARTUP.read_text(encoding="utf-8"))
    gateway_registration.validate_native_startup(startup)
    predecessors = load_predecessors(spending)
    old_profile = predecessors["profile"]
    old_plan = predecessors["plan"]
    old_authorization = predecessors["authorization"]
    require(wire.get("toolShellSha256") == old_plan["clientControl"]["shellSha256"],
            "fresh native wire proof used a different registered shell")
    registration = gateway_registration.resolve_profile(gateway_registration.PROFILE)
    selected = registration["stages"]["pilot"]
    slots = [
        "%s/%s/%d" % (task, arm, run)
        for run in range(1, selected["runsPerArm"] + 1)
        for task in registration["tasks"]
        for arm in ("calor-permissive", "calor-strict")
    ]
    require(len(slots) == 444, "frozen slot schedule changed")
    preserved = old_authorization["preservedAttemptedSlots"]
    require(preserved == [slots[0]]
            and old_plan["recovery"]["continuationInvocations"] == 443,
            "the original first attempt or 443-slot continuation changed")

    ledger = spending.gateway_ledger_location(old_authorization["ledgerBinding"])
    archive = BENCH / "epochs" / FAILED_EPOCH
    old_binding = legacy.read_old_binding(ledger)
    failed_ledger_sha256, inventory, failed_snapshot = validate_actual_predecessor(
        spending, recovery, legacy, predecessors, ledger, archive, old_binding)

    semantics = terminal_semantics(budget)
    source_lineage = source_history(predecessors, old_binding)
    amendment = {
        "schemaVersion": 1,
        "kind": "pp-w-prospective-spending-instrument-amendment",
        "stage": "pilot",
        "supersedes": {
            "path": PRIOR["instrument"][0].name,
            "sha256": PRIOR["instrument"][1],
        },
        "replacementHarnessArtifacts": spending.artifact_manifest(spending.GATEWAY),
        "terminalSemantics": semantics,
        "sourceHistory": source_lineage,
        "reason": "Reviewed #1434 source-bound attempt starts and terminal-invalid proofs "
                  "without changing the frozen estimands, weights, stopping rules, or allowance.",
    }
    amendment_ref = legacy.proof(OUTPUTS["instrument"], amendment)
    selected = dict(
        selected,
        epochId=EPOCH,
        instrumentAmendment=amendment_ref,
        terminalSemanticsAmendment=amendment_ref,
    )
    protocol = spending.protocol_identity(registration, selected, "pilot", EPOCH)

    authorization = dict(old_authorization)
    authorization.update({
        "kind": "pp-w-terminal-semantics-recovery-authorization",
        "epochId": EPOCH,
        "targetEpochId": EPOCH,
        "supersedes": {
            "path": PRIOR["authorization"][0].name,
            "sha256": PRIOR["authorization"][1],
        },
        "failedLedgerSha256": failed_ledger_sha256,
        "failedArchiveInventorySha256": inventory["sha256"],
        "terminalSemanticsAmendment": amendment_ref,
        "sourceHistory": source_lineage,
        "targetInvariants": {
            "stage": "pilot",
            "epochId": EPOCH,
            "priceSha256": budget.price_identity(),
            "protocolSha256": protocol,
            "harnessArtifacts": amendment["replacementHarnessArtifacts"],
        },
        "methodClarification":
            "Trusted terminal-invalid attempts consume their original slots and are excluded "
            "from eligible completions without replacement or zero imputation.",
        "methodClarificationReference": ISSUE,
    })
    authorization_ref = legacy.proof(OUTPUTS["authorization"], authorization)

    plan = dict(old_plan)
    plan.update({
        "epochId": EPOCH,
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol,
        "supersedes": {
            "path": PRIOR["plan"][0].name,
            "sha256": PRIOR["plan"][1],
        },
        "recovery": {
            **old_plan["recovery"],
            "terminalSemanticsAmendment": amendment_ref,
            "accountedTerminalSlots": 444,
            "validCompletionPopulation": "registered-terminal-valid-attempts-only",
        },
    })
    plan_ref = legacy.proof(OUTPUTS["plan"], plan)

    profile = dict(old_profile)
    profile.update({
        "id": "request-reserving-gateway-terminal-1434",
        "epochId": EPOCH,
        "effectiveOn": "independently reviewed merge of the #1434 implementation PR",
        "supersedes": {
            "path": PRIOR["profile"][0].name,
            "sha256": PRIOR["profile"][1],
        },
        "spendAuthorization": authorization_ref,
        "spendingPlan": plan_ref,
        "instrumentAmendment": amendment_ref,
        "terminalSemanticsAmendment": amendment_ref,
        "transportEvidence": {
            "path": WIRE.relative_to(ROOT).as_posix(),
            "sha256": spending.digest(WIRE),
        },
        "nativeStartupEvidence": {
            "path": STARTUP.relative_to(ROOT).as_posix(),
            "sha256": spending.digest(STARTUP),
        },
        "sourceHistory": source_lineage,
        "recovery": {
            **old_profile["recovery"],
            "terminalSemantics": semantics["kind"],
        },
    })
    profile_ref = legacy.proof(OUTPUTS["profile"], profile)
    target_binding = {
        "stage": "pilot",
        "epochId": EPOCH,
        "priceSha256": budget.price_identity(),
        "authorizationSha256": authorization_ref["sha256"],
        "protocolSha256": protocol,
        "planSha256": plan_ref["sha256"],
        "harnessArtifacts": amendment["replacementHarnessArtifacts"],
        "plannedSlots": slots,
    }
    backup = ledger.parent / authorization["backupName"]
    inspection = recovery.inspect_zero_request_recovery(
        ledger,
        archive,
        ledger.parent,
        backup,
        expected_old_binding=old_binding,
        target_binding=target_binding,
        expected_ledger_sha256=failed_ledger_sha256,
        expected_archive_inventory_sha256=inventory["sha256"],
        recovery_registration_sha256=authorization_ref["sha256"],
    )
    inventory_ref = legacy.proof(OUTPUTS["inventory"], inventory)
    snapshot_ref = legacy.proof(OUTPUTS["snapshot"], failed_snapshot)
    inspection_ref = legacy.proof(OUTPUTS["proof"], inspection)
    evidence = {
        "schemaVersion": 1,
        "kind": "pp-w-terminal-semantics-recovery-evidence",
        "issue": ISSUE,
        "recoveryAuthorization": authorization_ref,
        "executionProfile": profile_ref,
        "spendingPlan": plan_ref,
        "terminalSemanticsAmendment": amendment_ref,
        "inspectionProof": inspection_ref,
        "failedArchiveInventory": inventory_ref,
        "failedOperationalSnapshot": snapshot_ref,
        "targetBinding": target_binding,
        "sourceHistory": source_lineage,
        "historicalLineage": {
            "predecessorRecoveryEvidence": {
                "path": PRIOR["evidence"][0].name,
                "sha256": PRIOR["evidence"][1],
            },
            "predecessorProfile": {
                "path": PRIOR["profile"][0].name,
                "sha256": PRIOR["profile"][1],
            },
            "predecessorPlan": {
                "path": PRIOR["plan"][0].name,
                "sha256": PRIOR["plan"][1],
            },
            "predecessorAuthorization": {
                "path": PRIOR["authorization"][0].name,
                "sha256": PRIOR["authorization"][1],
            },
            "originalActual": predecessors["evidence"]["historicalLineage"],
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
    analysis = dict(predecessors["analysis"])
    artifacts = {}
    for relative in adjudication.analysis_artifacts("terminal-semantics-registered"):
        path = BENCH / relative
        generated = documents.get(path)
        artifacts[relative] = (
            legacy.sha_bytes(legacy.encoded(generated))
            if generated is not None
            else hashlib.sha256(path.read_bytes()).hexdigest()
        )
    analysis.update({
        "artifacts": artifacts,
        "supersedes": {
            "path": adjudication.PRE_TERMINAL_MANIFEST,
            "sha256": artifacts[adjudication.PRE_TERMINAL_MANIFEST],
        },
        "scope": "Reviewed #1434 terminal-attempt accounting for the unchanged 444-slot pilot; "
                 "invalid terminals consume slots without replacement or zero imputation.",
        "effectiveOn": "independently reviewed merge of the #1434 implementation PR",
        "recoveryStatus": "terminal-semantics-registered",
        "collectionAuthorized": False,
        "gatewayExecutionProfile": {
            "path": adjudication.TERMINAL_PROFILE,
            "sha256": artifacts[adjudication.TERMINAL_PROFILE],
        },
        "gatewayRecoveryEvidence": {
            "path": adjudication.TERMINAL_EVIDENCE,
            "sha256": artifacts[adjudication.TERMINAL_EVIDENCE],
        },
        "terminalSemanticsAmendment": {
            "path": adjudication.TERMINAL_AMENDMENT,
            "sha256": artifacts[adjudication.TERMINAL_AMENDMENT],
        },
    })
    documents[OUTPUTS["analysis"]] = analysis
    return documents


def write_documents(documents, legacy):
    adjudication = module("ppw-pilot-adjudicate.py")
    current = adjudication.validate_analysis_registration()
    require(current.get("recoveryStatus") == "pending-reviewed-terminal-evidence"
            and current.get("supersedes") == {
                "path": adjudication.PRE_TERMINAL_MANIFEST,
                "sha256": PRIOR["analysis"][1],
            }, "active analysis registration is not the exact pending #1434 manifest")
    for path, value in documents.items():
        if path == OUTPUTS["analysis"]:
            continue
        encoded = legacy.encoded(value)
        require(not path.exists() or path.read_bytes() == encoded,
                "refusing to overwrite a different terminal registration: " + path.name)
    for path, value in documents.items():
        path.write_bytes(legacy.encoded(value))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--write", action="store_true",
                        help="write only the new #1434 registration files and active manifest")
    parser.add_argument("--contract", action="store_true",
                        help="print the fixed input/output contract without reading operational state")
    args = parser.parse_args(argv)
    if args.contract:
        print(json.dumps(
            {"inputs": INPUT_CONTRACT, "outputs": OUTPUT_SCHEMA},
            indent=2,
            sort_keys=True,
        ))
        return 0
    try:
        documents = build_documents()
        legacy = module("ppw-gateway-register-recovery.py")
        if not args.write:
            print(json.dumps({
                "ready": True,
                "outputs": {
                    path.relative_to(ROOT).as_posix(): legacy.sha_bytes(legacy.encoded(value))
                    for path, value in documents.items()
                },
            }, indent=2, sort_keys=True))
            return 0
        write_documents(documents, legacy)
        print(json.dumps({
            "written": [str(path) for path in documents],
            "operationalStateChanged": False,
        }, indent=2))
    except (OSError, ValueError, KeyError, TypeError, sqlite3.Error) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    sys.exit(main())
