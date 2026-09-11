#!/usr/bin/env python3
"""Resolve the additive gateway profile and audit archived request accounting without execution."""
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import re

BENCH = Path(__file__).resolve().parent
ROOT = BENCH / "registrations/ppw-rows-stage1"
PROFILE = ROOT / "gateway-execution-profile.json"
PRE_TERMINAL_PROFILE = ROOT / "gateway-execution-profile-1432.json"
PRE_TERMINAL_EVIDENCE = ROOT / "gateway-recovery-evidence-1432.json"
RECOVERY_PROFILE = ROOT / "gateway-execution-profile-1434.json"
RECOVERY_EVIDENCE = ROOT / "gateway-recovery-evidence-1434.json"
DISPOSITION_PROFILE = ROOT / "gateway-execution-profile-1436.json"
BASELINE = "epochs/w-rows-pilot-001/registration.json"
BASELINE_SHA256 = "b506c9ec65469640b6135708c7f1e9adbce7dcd318f6ca51e1b305156041484c"
PRE_TERMINAL_PROFILE_PROOF = {
    "path": PRE_TERMINAL_PROFILE.name,
    "sha256": "8066b86aeecf292a8e86002011fee7415c91c9c528ea25727ab4541f3e05ec2f",
}
PRE_TERMINAL_EVIDENCE_PROOF = {
    "path": PRE_TERMINAL_EVIDENCE.name,
    "sha256": "9c3552b34bd1730f6c028a495a18a1db8918e735309b4b21eb0e81e52864643f",
}
PRE_TERMINAL_AMENDMENT_PROOF = {
    "path": "gateway-instrument-amendment-1432.json",
    "sha256": "3d45adad658c569ab84b34b3a9a91a35f4ca65a09e18ee77c327222b9f1a3f59",
}
PRE_TERMINAL_AUTHORIZATION_PROOF = {
    "path": "gateway-authorization-1432.json",
    "sha256": "c4aa8608d478870922867f2fb892e14ca3efe442ea6fddca64643927f72e2b43",
}
PRE_TERMINAL_PLAN_PROOF = {
    "path": "gateway-spending-plan-1432.json",
    "sha256": "ebea4e7db9b6ee1fa67a01b343dd182a9ebb49c673ab967b73750ffefa4b55c4",
}
PR_1433 = "https://github.com/juanmicrosoft/calor/pull/1433"
MERGE_1433 = "ac4ad3a016feb283e5ea7b3d90e6505ecaf6e49b"
ACTUAL_SOURCE_COMMIT = "d705c0c015954cf8bfe4d39040db727bfb2d2309"
WIRE_SOURCE_HASHES = {
    "probe-ppw-gateway.py", "ppw-gateway-client.py", "run-pair.sh",
    "ppw-gateway-budget.py", "ppw-budget-gateway.py",
}
HISTORICAL_WIRE_SOURCE_HASHES = {
    "probe-ppw-gateway.py": "ff0f6e72c753572e95daad891c87974c393eaa8fc65096a1596dfc5f2faba4fe",
    "ppw-gateway-client.py": "12fe9fbc9005130f5e936b91f128bcf96037e258710ea8b86e0bef9ec8d57b41",
    "run-pair.sh": "77b1b99c763ce14a341a62d0adc895a2b76b3f7d3a1062faa3cf485f5154212f",
    "ppw-gateway-budget.py": "999af1d9b55a6bcc1dec1a4f8a92c0108830d4473582f1c01122bf48e4628ce1",
}
STARTUP_SOURCE_FILES = {
    "run-pair.sh", "ppw-budget-gateway.py", "ppw-gateway-budget.py",
    "ppw-gateway-client.py", "gateway-tools/bash-env.sh", "ppw-run-observer.py",
    "ppw-source-assembly.py", "ppw-source-inspection.py", "ppw-test-host.py",
}


def helper(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W gateway registration: " + message)


def validate_native_startup(value):
    spending = helper("ppw-spending.py")
    isolation = helper("ppw-gateway-client.py")
    require(value.get("kind") == "SYNTHETIC/native-first-job-gateway-startup-v1"
            and value.get("outcome") == "passed"
            and value.get("empirical") is False and value.get("modelInvoked") is False
            and value.get("provider", {}).get("realUpstreamGuardInstalled") is True
            and value.get("accounting", {}).get("state") == "complete"
            and value.get("accounting", {}).get("sourceBoundAttemptStart") is True
            and value.get("accounting", {}).get("accountedSlots") == 1
            and value.get("accounting", {}).get("validCompletedSlots") == 1
            and value.get("accounting", {}).get("invalidTerminalSlots") == 0
            and value.get("pins", {}).get("clientSha256") == isolation.CLIENT_SHA256
            and value.get("invocation", {}).get("clientFlags") == isolation.registered_client_flags()
            and value.get("nativeToolExecution", {}).get("toolResultErrors") == 0
            and [item.get("command") for item in value.get("observer", {}).get("journal", [])]
            == ["build", "test"]
            and value.get("final", {}).get("heldoutPassed") == 4
            and value.get("final", {}).get("taskSuccess") is True,
            "actual scripted native build/test startup evidence did not pass")
    require(set(value.get("sourceHashes", {})) == STARTUP_SOURCE_FILES
            and all(spending.digest(BENCH / name) == sha
                    for name, sha in value["sourceHashes"].items())
            and value.get("testSha256")
            == spending.digest(BENCH / "tests/test_ppw_gateway_native_startup.py"),
            "native startup evidence binds different execution or test source")


def _resolve_profile(path, require_wire_contract):
    spending = helper("ppw-spending.py")
    profile = json.loads(Path(path).read_text())
    require(profile.get("schemaVersion") == 1
            and profile.get("kind") == "pp-w-request-gateway-execution-profile"
            and profile.get("stage") == "pilot", "unknown operational execution profile")
    require(profile.get("baselineRegistration") == {"path": BASELINE, "sha256": BASELINE_SHA256},
            "the preserved pilot registration must be the exact baseline")
    _, registration = spending.pinned_document(BENCH, profile["baselineRegistration"], "baselineRegistration")
    require(profile.get("allowedDataKinds") == ["synthetic", "empirical"],
            "the reviewed gateway profile must admit actual collection, not only fixtures")
    _, transport = spending.pinned_document(Path(path).parent, profile.get("transportEvidence"),
                                           "transportEvidence")
    isolation = helper("ppw-gateway-client.py")
    require(transport.get("kind") == "pp-w-engineering-no-forward-native-probe"
            and transport.get("success") is True and transport.get("upstreamRequests") == 0
            and transport.get("experimentalObservations") == 0
            and transport.get("clientSha256") == isolation.CLIENT_SHA256
            and transport.get("clientFlags") == isolation.registered_client_flags()
            and transport.get("kernelEvidence", {}).get("kernelProbe") == isolation.PROBE_EXPECTATIONS,
            "native transport evidence differs from this instrument")
    require(transport.get("observations") and all(
        value.get("messagesPath") is True and value.get("credentialHeaderPresent") is True
        and value.get("priceContractAccepted") is True and value.get("unclassifiedBetaCount") == 0
        and (not require_wire_contract or value.get("wireContractAccepted") is True)
        for value in transport["observations"]), "native requests were not within the priced schema")
    expected_sources = WIRE_SOURCE_HASHES if require_wire_contract else WIRE_SOURCE_HASHES - {
        "ppw-budget-gateway.py",
    }
    require(set(transport.get("sourceHashes", {})) == expected_sources
            and (all(spending.digest(BENCH / name) == sha
                     for name, sha in transport["sourceHashes"].items())
                 if require_wire_contract
                 else transport["sourceHashes"] == HISTORICAL_WIRE_SOURCE_HASHES),
            "native compatibility evidence binds different source")
    selected = registration["stages"]["pilot"]
    selected["epochId"] = profile["epochId"]
    for name in ("spendAuthorization", "spendingPlan", "instrumentAmendment",
                 "stageRegistration", "modelRegistration", "sourceInspectionEvidence"):
        spending.pinned_document(Path(path).parent, profile[name], name)
        selected[name] = profile[name]
    selected["executionProfile"] = {"path": Path(path).name, "sha256": spending.digest(path)}
    _, plan = spending.pinned_document(Path(path).parent, profile["spendingPlan"], "spendingPlan")
    if require_wire_contract:
        require(transport.get("toolShellSha256") == plan["clientControl"]["shellSha256"],
                "native transport used a different tool shell")
        _, startup = spending.pinned_document(
            Path(path).parent, profile.get("nativeStartupEvidence"), "nativeStartupEvidence")
        validate_native_startup(startup)
    spending.validate_forecast(plan, spending.units(plan["ceilingUsd"]),
                               len(registration["tasks"]) * selected["runsPerArm"] * 2,
                               Path(path).parent)
    source_inspector = plan.get("clientControl", {}).get("sourceInspector")
    require(isinstance(source_inspector, dict), "source-inspector runtime binding is required")
    selected["sourceInspector"] = source_inspector
    _, certificates = spending.pinned_document(
        Path(path).parent, profile["sourceInspectionEvidence"], "sourceInspectionEvidence")
    require(certificates.get("schemaVersion") == 1
            and certificates.get("kind") == "pp-w-gateway-source-inspection-supersession"
            and certificates.get("baselineRegistration") == profile["baselineRegistration"]
            and certificates.get("runtimeSha256") == source_inspector.get("runtimeSha256")
            and set(certificates.get("tasks", {})) == set(registration["tasks"]),
            "source-inspection supersession differs from the preserved task set")
    runtime_fields = {"inspectorSha256", "inspectorRuntimeSha256"}
    for task, previous in registration["sourceInspections"].items():
        current = certificates["tasks"][task]
        require({key: value for key, value in current.items() if key not in runtime_fields}
                == {key: value for key, value in previous.items() if key not in runtime_fields}
                and current.get("inspectorSha256")
                == source_inspector.get("files", {}).get("ppw-source-inspector.dll")
                and current.get("inspectorRuntimeSha256") == source_inspector["runtimeSha256"],
                "source-inspection supersession changed frozen observations: " + task)
    registration["sourceInspections"] = certificates["tasks"]
    registration.update(collectionAuthorized=True, fundingStatus="approved")
    return registration


def resolve_profile(path):
    """Resolve the immutable #1406 profile for historical archive validation."""
    require(Path(path).resolve() == PROFILE.resolve(),
            "historical profile resolution is restricted to the preserved #1406 bytes")
    return _resolve_profile(path, False)


def load_recovery_evidence(path=RECOVERY_EVIDENCE):
    """Load the canonical reviewed #1434 authority chain; no caller-selected approvals."""
    spending = helper("ppw-spending.py")
    path = Path(path)
    require(path.resolve() == RECOVERY_EVIDENCE.resolve(),
            "recovery evidence must be the canonical reviewed #1434 registration")
    value = json.loads(path.read_text(encoding="utf-8"))
    require(value.get("schemaVersion") == 1
            and value.get("kind") == "pp-w-terminal-semantics-recovery-evidence"
            and value.get("issue") == "https://github.com/juanmicrosoft/calor/issues/1434",
            "unrecognized recovery evidence")
    expected_names = {
        "recoveryAuthorization", "executionProfile", "spendingPlan", "inspectionProof",
        "failedArchiveInventory", "failedOperationalSnapshot", "terminalSemanticsAmendment",
    }
    require(expected_names <= set(value), "recovery evidence chain is incomplete")
    documents = {}
    for name in expected_names:
        _, documents[name] = spending.pinned_document(ROOT, value[name], name)
    _, predecessor_authorization = spending.pinned_document(
        ROOT, PRE_TERMINAL_AUTHORIZATION_PROOF, "pre-terminal authorization")
    _, predecessor_plan = spending.pinned_document(
        ROOT, PRE_TERMINAL_PLAN_PROOF, "pre-terminal spending plan")
    _, predecessor_evidence = spending.pinned_document(
        ROOT, PRE_TERMINAL_EVIDENCE_PROOF, "pre-terminal recovery evidence")
    require(value["executionProfile"] == {
        "path": RECOVERY_PROFILE.name, "sha256": spending.digest(RECOVERY_PROFILE),
    }, "recovery evidence selects another execution profile")
    authorization = documents["recoveryAuthorization"]
    profile = documents["executionProfile"]
    plan = documents["spendingPlan"]
    amendment = documents["terminalSemanticsAmendment"]
    proof = documents["inspectionProof"]
    inventory = documents["failedArchiveInventory"]
    failed = documents["failedOperationalSnapshot"]
    require(profile.get("spendAuthorization") == value["recoveryAuthorization"]
            and profile.get("spendingPlan") == value["spendingPlan"]
            and plan.get("authorizationSha256") == value["recoveryAuthorization"]["sha256"],
            "mixed profile, plan, or authorization sources")
    require(profile.get("supersedes") == PRE_TERMINAL_PROFILE_PROOF
            and plan.get("supersedes") == PRE_TERMINAL_PLAN_PROOF
            and authorization.get("supersedes") == PRE_TERMINAL_AUTHORIZATION_PROOF
            and amendment.get("supersedes") == PRE_TERMINAL_AMENDMENT_PROOF,
            "the #1433 proposal is not explicitly preserved and superseded")
    require(profile.get("instrumentAmendment") == value["terminalSemanticsAmendment"]
            and profile.get("terminalSemanticsAmendment") == value["terminalSemanticsAmendment"]
            and authorization.get("terminalSemanticsAmendment")
            == value["terminalSemanticsAmendment"]
            and plan.get("recovery", {}).get("terminalSemanticsAmendment")
            == value["terminalSemanticsAmendment"],
            "terminal semantics amendment is not bound across the authority chain")
    spending.validate_terminal_supersession({
        "instrumentAmendment": value["terminalSemanticsAmendment"],
        "terminalSemanticsAmendment": value["terminalSemanticsAmendment"],
    }, authorization, plan, amendment)
    require(amendment.get("replacementHarnessArtifacts")
            == spending.artifact_manifest(spending.GATEWAY),
            "terminal registration binds stale or mixed execution sources")
    require(authorization.get("kind") == "pp-w-terminal-semantics-recovery-authorization"
            and authorization.get("spendingCeilingUsd") == 1000
            and authorization.get("additionalAllowance") is False
            and authorization.get("ledgerReset") is False
            and authorization.get("ledgerBinding") == plan.get("ledgerBinding")
            and authorization.get("ledgerBinding")
            == predecessor_authorization.get("ledgerBinding")
            and authorization.get("oldBinding") == predecessor_authorization.get("oldBinding")
            and authorization.get("failedLedgerSha256")
            == predecessor_authorization.get("failedLedgerSha256")
            and authorization.get("failedArchiveInventorySha256")
            == predecessor_authorization.get("failedArchiveInventorySha256")
            and authorization.get("failedLedgerSha256") == proof.get("oldLedgerSha256")
            and authorization.get("failedArchiveInventorySha256")
            == proof.get("failedArchiveInventorySha256") == inventory.get("sha256"),
            "recovery authorization, proof, and inventory differ")
    require(profile.get("epochId") == plan.get("epochId") == proof.get("targetEpochId")
            == authorization.get("targetEpochId") == "w-rows-pilot-gateway-002",
            "recovery target epoch differs")
    require(authorization.get("oldEpochId") == proof.get("oldEpochId")
            == failed.get("binding", {}).get("epochId") == "w-rows-pilot-gateway-001",
            "failed epoch identity differs")
    preserved = authorization.get("preservedAttemptedSlots")
    require(isinstance(preserved, list) and len(preserved) == 1
            and preserved == proof.get("preservedAttemptedSlots"),
            "the first attempted slot is not preserved")
    require(proof.get("requestCount") == 0 and proof.get("completedSlots") == 0
            and proof.get("remainingSlotCount") == 443
            and failed.get("state") == "INCOMPLETE_POLICY"
            and failed.get("requests") == [] and failed.get("exposureMicroUsd") == 0,
            "recovery evidence is not the zero-request failed scope")
    require(plan.get("forecast") == predecessor_plan.get("forecast")
            and plan.get("plannedInvocations") == 444
            and plan.get("ceilingUsd") == predecessor_plan.get("ceilingUsd") == 1000,
            "the full-444 forecast or canonical total ceiling changed")
    history = value.get("sourceHistory")
    require(isinstance(history, dict)
            and history == authorization.get("sourceHistory")
            and history == profile.get("sourceHistory")
            and history == amendment.get("sourceHistory")
            and history.get("actualFailedCollection", {}).get("epochId")
            == "w-rows-pilot-gateway-001"
            and history["actualFailedCollection"].get("sourceCommit") == ACTUAL_SOURCE_COMMIT
            and history["actualFailedCollection"].get("bindingSha256")
            == proof.get("oldBindingSha256")
            and history["actualFailedCollection"].get("ledgerSha256")
            == authorization["failedLedgerSha256"]
            and history["actualFailedCollection"].get("archiveInventorySha256")
            == authorization["failedArchiveInventorySha256"]
            and history.get("supersededUnexecutedProposal", {}).get("pullRequest") == PR_1433
            and history["supersededUnexecutedProposal"].get("mergeCommit") == MERGE_1433
            and history["supersededUnexecutedProposal"].get("executed") is False
            and history["supersededUnexecutedProposal"].get("profile")
            == PRE_TERMINAL_PROFILE_PROOF
            and history["supersededUnexecutedProposal"].get("recoveryEvidence")
            == PRE_TERMINAL_EVIDENCE_PROOF
            and history.get("prospectiveCollection", {}).get("epochId")
            == "w-rows-pilot-gateway-002"
            and history["prospectiveCollection"].get("issue")
            == "https://github.com/juanmicrosoft/calor/issues/1434",
            "actual, superseded, or prospective source lineage differs")
    lineage = value.get("historicalLineage", {})
    require(lineage.get("predecessorRecoveryEvidence") == PRE_TERMINAL_EVIDENCE_PROOF
            and lineage.get("predecessorProfile") == PRE_TERMINAL_PROFILE_PROOF
            and lineage.get("predecessorPlan") == PRE_TERMINAL_PLAN_PROOF
            and lineage.get("predecessorAuthorization") == PRE_TERMINAL_AUTHORIZATION_PROOF,
            "pre-terminal recovery lineage differs")
    require(lineage.get("originalActual") == predecessor_evidence.get("historicalLineage"),
            "original financial and source lineage changed")
    target = value.get("targetBinding")
    invariants = authorization.get("targetInvariants", {})
    require(isinstance(target, dict)
            and {key: target.get(key) for key in invariants} == invariants
            and target.get("authorizationSha256") == value["recoveryAuthorization"]["sha256"]
            and target.get("planSha256") == value["spendingPlan"]["sha256"]
            and target.get("protocolSha256") == plan.get("protocolSha256")
            and target.get("harnessArtifacts") == amendment["replacementHarnessArtifacts"]
            and target.get("plannedSlots") == authorization.get("plannedSlots"),
            "recovery target binding is not fully pinned")
    return value, documents


def resolve_collection_profile(path):
    """Resolve only the active reviewed terminal/disposition collection profile."""
    path = Path(path)
    if path.resolve() == DISPOSITION_PROFILE.resolve():
        return helper("ppw-gateway-disposition-registration.py").resolve_collection_profile(path)
    require(path.resolve() == RECOVERY_PROFILE.resolve(),
            "collection requires the canonical reviewed #1434 terminal profile")
    require(RECOVERY_PROFILE.is_file() and RECOVERY_EVIDENCE.is_file(),
            "fresh #1434 terminal profile and recovery evidence are not registered")
    registration = _resolve_profile(path, True)
    evidence, documents = load_recovery_evidence()
    selected = registration["stages"]["pilot"]
    selected["recoveryEvidence"] = {
        "path": RECOVERY_EVIDENCE.name, "sha256": helper("ppw-spending.py").digest(RECOVERY_EVIDENCE),
    }
    selected["terminalSemanticsAmendment"] = evidence["terminalSemanticsAmendment"]
    for name in ("recoveryAuthorization", "inspectionProof", "failedArchiveInventory",
                 "failedOperationalSnapshot"):
        selected[name] = evidence[name]
    selected["preservedAttemptedSlots"] = documents["recoveryAuthorization"][
        "preservedAttemptedSlots"]
    return registration


def archive_proof(epoch, proof, expected):
    """Operational archives must carry their evidence; aliases cannot substitute for files."""
    require(isinstance(proof, dict) and set(proof) == {"path", "sha256"}
            and proof.get("sha256") == expected["sha256"], "unregistered operational evidence")
    relative = proof["path"]
    require(isinstance(relative, str) and relative and all(
        not path.is_absolute() and not path.drive and not path.root and ".." not in path.parts
        for path in (PurePosixPath(relative), PureWindowsPath(relative))),
        "operational evidence must have a contained relative path")
    spending = helper("ppw-spending.py")
    path, value = spending.pinned_document(epoch, proof, "archived operational evidence")
    return value


def validate_archive(epoch, pins, selected):
    if "dispositionEvidence" in selected:
        return helper("ppw-gateway-disposition-registration.py").validate_archive(
            epoch, pins, selected)
    spending = helper("ppw-spending.py")
    budget = helper("ppw-gateway-budget.py")
    isolation = helper("ppw-gateway-client.py")
    recovery_mode = "recoveryEvidence" in selected
    profile_path = RECOVERY_PROFILE if recovery_mode else PROFILE
    profile = json.loads(profile_path.read_text())
    profile_proof = {
        "path": profile_path.relative_to(BENCH).as_posix(),
        "sha256": spending.digest(profile_path),
    }
    require(epoch is not None, "an operational archive is required")
    epoch = Path(epoch)
    require(archive_proof(epoch, selected.get("executionProfile"), profile_proof) == profile,
            "execution profile contents differ")
    proof_names = [
        "spendAuthorization", "spendingPlan", "instrumentAmendment",
        "stageRegistration", "modelRegistration", "sourceInspectionEvidence",
    ]
    recovery_documents = None
    if recovery_mode:
        proof_names.extend([
            "recoveryEvidence", "recoveryAuthorization", "inspectionProof",
            "failedArchiveInventory", "failedOperationalSnapshot",
        ])
    proofs = {name: archive_proof(
        epoch, selected.get(name),
        profile[name] if name in profile else selected[name])
        for name in proof_names}
    if recovery_mode:
        recovery_documents = {
            name: proofs[name] for name in (
                "recoveryEvidence", "recoveryAuthorization", "inspectionProof",
                "failedArchiveInventory", "failedOperationalSnapshot",
            )
        }
    registration = json.loads((epoch / "registration.json").read_text())
    plan, authorization, amendment = (proofs[name] for name in
                                     ("spendingPlan", "spendAuthorization", "instrumentAmendment"))
    require(pins["epochId"] == profile["epochId"] and pins["stage"] == "pilot"
            and pins["dataKind"] in profile["allowedDataKinds"], "wrong gateway epoch or stage")
    require(pins["lifecycle"] == "collected" and not (epoch / "collection-outcome.json").exists(),
            "incomplete collection cannot be adjudicated")
    require(pins["harnessArtifacts"] == amendment["replacementHarnessArtifacts"]
            and (not recovery_mode
                 or pins["harnessArtifacts"] == spending.artifact_manifest(spending.GATEWAY)),
            "mixed or changed gateway sources")
    require(plan["clientControl"]["kind"] == spending.GATEWAY, "unregistered gateway control")
    source_inspector = plan["clientControl"].get("sourceInspector")
    require(isinstance(source_inspector, dict)
            and re.fullmatch(r"[0-9a-f]{64}",
                             source_inspector.get("files", {}).get("ppw-source-inspector.dll", ""))
            and re.fullmatch(r"[0-9a-f]{64}", source_inspector.get("runtimeSha256", "")),
            "registered source-inspector runtime is missing")
    require(all(certificate.get("inspectorSha256")
                == source_inspector["files"]["ppw-source-inspector.dll"]
                and certificate.get("inspectorRuntimeSha256") == source_inspector["runtimeSha256"]
                for certificate in registration["sourceInspections"].values()),
            "gateway source-inspection certificates differ from the registered runtime")
    require(registration["sourceInspections"] == proofs["sourceInspectionEvidence"]["tasks"],
            "archived source-inspection certificates differ from their selected proof")
    _, prices = spending.pinned_document(ROOT, plan["clientControl"]["priceContract"], "priceContract")
    price_identity = hashlib.sha256(budget.canonical(prices).encode()).hexdigest()
    historical_prices = dict(budget.price_contract())
    historical_prices.pop("dataResidencyPolicy", None)
    historical_prices["sources"] = [
        source for source in historical_prices["sources"]
        if source != "https://platform.claude.com/docs/en/manage-claude/data-residency"]
    historical_price = price_identity == (
        "582a71eeb22ad7604759d7441f719615def5a04a5ca2cecece8a2c9e1f45eb3e")
    # The historical rates and request bounds are identical, but its response
    # policy did not admit the prospective unknown-geography sentinel.
    require(prices == budget.price_contract()
            or (historical_price and prices == historical_prices),
            "gateway prices differ from implemented accounting")
    require(plan["protocolSha256"] == spending.protocol_identity(
        registration, selected, "pilot", pins["epochId"]), "gateway protocol binding differs")
    require(plan["authorizationSha256"] == selected["spendAuthorization"]["sha256"]
            and plan["ledgerBinding"] == authorization["ledgerBinding"]
            and plan["ceilingUsd"] == authorization["spendingCeilingUsd"],
            "authorization/plan/ledger binding differs")
    require(plan["epochId"] == authorization["epochId"] == pins["epochId"]
            and plan["stage"] == authorization["stage"] == "pilot",
            "cross-epoch or wrong-stage funding")
    slots = ["%s/%s/%d" % (task, arm, run)
             for run in range(1, pins["runsPerArm"] + 1) for task in pins["suite"]
             for arm in ("calor-permissive", "calor-strict")]
    require(len(slots) == plan["plannedInvocations"] == 444, "the full pilot inventory is required")
    expected_binding = {
        "stage": "pilot", "epochId": pins["epochId"], "priceSha256": price_identity,
        "authorizationSha256": selected["spendAuthorization"]["sha256"],
        "protocolSha256": plan["protocolSha256"], "planSha256": selected["spendingPlan"]["sha256"],
        "harnessArtifacts": pins["harnessArtifacts"], "plannedSlots": slots,
    }
    strict_terminal_lifecycle = budget.strict_terminal_lifecycle(expected_binding)
    initial = budget.decode((epoch / "spending-initial.json").read_bytes())
    final = budget.decode((epoch / "spending-final.json").read_bytes())
    ceiling = spending.units(authorization["spendingCeilingUsd"])
    spending.validate_forecast(plan, ceiling, len(slots), epoch)
    for snapshot in (initial, final):
        require(snapshot.get("kind") == budget.KIND and snapshot.get("binding") == expected_binding
                and type(snapshot.get("ceilingMicroUsd")) is int
                and snapshot["ceilingMicroUsd"] == ceiling and snapshot.get("verdict") is None,
                "spending snapshot scope differs")
    preserved_attempted = []
    prefix_length = 2
    if recovery_mode:
        recovery = helper("ppw-gateway-recovery.py")
        authority = recovery_documents["recoveryAuthorization"]
        proof = recovery_documents["inspectionProof"]
        failed = recovery_documents["failedOperationalSnapshot"]
        recovery_evidence = recovery_documents["recoveryEvidence"]
        preserved_attempted = authority["preservedAttemptedSlots"]
        require(recovery_evidence.get("targetBinding") == expected_binding
                and authority["oldBinding"]["plannedSlots"] == slots
                and authority["failedLedgerSha256"] == proof["oldLedgerSha256"]
                and authority["failedArchiveInventorySha256"]
                == proof["failedArchiveInventorySha256"]
                == recovery_documents["failedArchiveInventory"]["sha256"],
                "archived recovery authority differs from accounting")
        require(all(recovery_evidence.get(name) == selected[name] for name in (
                    "recoveryAuthorization", "inspectionProof",
                    "failedArchiveInventory", "failedOperationalSnapshot"))
                and recovery_evidence.get("executionProfile") == selected["executionProfile"]
                and recovery_evidence.get("spendingPlan") == selected["spendingPlan"],
                "archived recovery evidence chain differs")
        recovery.validate_recovered_initial_archive(
            failed, initial, expected_old_binding=authority["oldBinding"],
            target_binding=expected_binding,
            expected_ledger_sha256=authority["failedLedgerSha256"],
            expected_archive_inventory_sha256=authority["failedArchiveInventorySha256"],
            recovery_registration_sha256=selected["recoveryAuthorization"]["sha256"],
            preserved_attempted_slots=preserved_attempted)
        prefix_length = 5
        slot = preserved_attempted[0]
        task, arm, run = slot.split("/")
        directory = epoch / "runs" / task / arm / ("run-" + run)
        recovered_record = json.loads((directory / "result.json").read_text())
        client_invocation = json.loads((directory / "client-invocation.json").read_text())
        origin = recovered_record.get("recoveredAttempt", {})
        original_profile = origin.get("originalProfile") if isinstance(origin, dict) else None
        original_run = origin.get("originalRun") if isinstance(origin, dict) else None
        inventory_entries = recovery_documents["failedArchiveInventory"].get("files")
        require(isinstance(original_profile, dict) and isinstance(original_run, dict)
                and isinstance(inventory_entries, list)
                and all(isinstance(item, dict) and set(item) >= {"path", "sha256"}
                        for item in inventory_entries),
                "preserved failed attempt provenance is malformed")
        failed_files = {
            item["path"]: item["sha256"]
            for item in inventory_entries
        }
        failed_run_root = "runs/%s/%s/run-%s/" % (task, arm, run)
        require(recovered_record.get("recordKind") == "pp-w-recovered-invalid-wrapper-v1"
                and recovered_record.get("invalid") is True
                and recovered_record.get("censored") is True
                and origin.get("kind") == "pp-w-opaque-original-invalid-attempt-v1"
                and origin.get("failedEpochId") == authority["oldEpochId"]
                and origin.get("wrapperEpochId") == pins["epochId"]
                and origin.get("slot") == slot
                and origin.get("providerRequests") == 0
                and origin.get("replacementPermitted") is False
                and origin.get("provenanceResolution")
                == ("verified-failed-archive" if pins["dataKind"] == "empirical"
                    else "synthetic-registered-proof-double")
                and origin.get("failedArchiveInventorySha256")
                == authority["failedArchiveInventorySha256"]
                and origin.get("failedLedgerSha256") == authority["failedLedgerSha256"]
                and original_profile.get("sourceHashes")
                == authority["oldBinding"]["harnessArtifacts"]
                and original_profile.get("pinsIdentitySha256")
                == recovery_documents["inspectionProof"]["failedPinsIdentitySha256"]
                and original_profile.get("pinsSha256") == failed_files.get("pins.json")
                and original_profile.get("registrationSha256")
                == recovery_documents["inspectionProof"]["failedRegistrationSha256"],
                "preserved failed attempt origin differs from the recovery authority")
        require(original_run.get("rawRecordRelativePath")
                == failed_run_root + "result.json"
                and original_run.get("rawRecordSha256")
                == failed_files.get(failed_run_root + "result.json")
                and original_run.get("clientInvocationSha256")
                == failed_files.get(failed_run_root + "client-invocation.json")
                and (pins["dataKind"] == "synthetic"
                     or original_run["clientInvocationSha256"]
                     == spending.digest(directory / "client-invocation.json"))
                and original_run.get("runPairSha256")
                == authority["oldBinding"]["harnessArtifacts"]["run-pair.sh"]
                and origin.get("wrapperSourceHashes") == {
                    name: pins["harnessArtifacts"][name]
                    for name in ("run-pair.sh", "ppw-gateway-budget.py", "ppw-instrument.py")
                }
                and (directory / "invalid.txt").is_file()
                and set(client_invocation) == {"exitCode"}
                and type(client_invocation["exitCode"]) is int
                and 0 < client_invocation["exitCode"] < 128
                and client_invocation["exitCode"] != 124,
                "preserved failed attempt is missing from the recovered archive")
    else:
        require(initial["state"] == "collecting" and initial["requests"] == []
                and initial["exposureMicroUsd"] == 0 and len(initial["events"]) == 2,
                "initial accounting must precede every request")
    if strict_terminal_lifecycle:
        require({
            key: initial.get(key) for key in (
                "accountedSlots", "validCompletedSlots",
                "invalidTerminalSlots", "requestBearingSlots")
        } == {
            "accountedSlots": len(preserved_attempted),
            "validCompletedSlots": 0,
            "invalidTerminalSlots": len(preserved_attempted),
            "requestBearingSlots": 0,
        }, "initial terminal-attempt counters differ")
    require(final["state"] == "complete"
            and final["events"][:len(initial["events"])] == initial["events"],
            "incomplete accounting or broken event lineage")
    rows = {}
    for row in final["requests"]:
        require(isinstance(row, dict) and set(row) ==
                {"id", "slot", "request", "reserved", "charge", "state", "reason", "usage"},
                "request ledger fields differ")
        identity = row["id"]
        require(isinstance(identity, str) and re.fullmatch(r"[0-9a-f]{48}", identity)
                and identity not in rows and row["slot"] in slots, "foreign or duplicate request")
        request = budget.decode(row["request"])
        expected_request = budget.admit_request(budget.canonical({
            "model": budget.MODEL, "max_tokens": request["maxTokens"],
            "messages": [{"role": "user", "content": "ARCHIVE_SCHEMA_ONLY_NO_INFERENCE"}],
            "stream": request["stream"], "service_tier": request["serviceTier"],
        }), ",".join(request["betaCapabilities"]) or None)
        expected_request["priceSha256"] = price_identity
        require(request == expected_request and row["reserved"] == request["maximumMicroUsd"],
                "request reservation differs from implemented liability")
        receipt = budget.decode(row["usage"])
        require(not historical_price
                or receipt["usage"].get("inference_geo") in (None, "global", "us"),
                "usage geography was not admitted by the historical price contract")
        cost, verified = budget.reconciled_cost(
            request, receipt["model"], receipt["usage"], receipt["stopReason"])
        require(receipt == verified and type(row["charge"]) is int and row["charge"] == cost
                and row["state"] == "reconciled" and row["reason"] is None,
                "unknown or incorrectly reconciled charge")
        rows[identity] = row
    outstanding, settled, accounted, valid_completed = (
        {}, set(), list(preserved_attempted), [])
    invalid_terminal = list(preserved_attempted)
    exposure = 0
    require(all(type(event.get("id")) is int for event in final["events"])
            and [event["id"] for event in final["events"]] == list(range(1, len(final["events"]) + 1)),
            "missing or reordered accounting events")
    for index, event in enumerate(final["events"]):
        kind, identity, detail = event["kind"], event["request_id"], budget.decode(event["detail"])
        if recovery_mode and index < prefix_length:
            continue
        if index == 0:
            require(kind == "initialized" and identity is None and detail == {"ceilingMicroUsd": ceiling},
                    "incorrect initialization event")
        elif index == 1:
            require(kind == "started" and identity is None and detail == {}, "incorrect start event")
        elif kind == "reserved-before-upstream":
            require(identity in rows and identity not in outstanding and identity not in settled,
                    "missing or duplicate reservation event")
            row = rows[identity]
            require(len(accounted) < len(slots) and row["slot"] == slots[len(accounted)]
                    and detail == {"maximumMicroUsd": row["reserved"]},
                    "request after slot completion or wrong reservation")
            outstanding[identity] = row["reserved"]
            exposure += row["reserved"]
            require(exposure <= ceiling, "historical concurrent liabilities exceeded the ceiling")
        elif kind == "complete-provider-usage":
            require(identity in outstanding, "settlement without a preceding reservation")
            row = rows[identity]
            require(detail == {"conservativeChargeMicroUsd": row["charge"],
                               "releasedMicroUsd": row["reserved"] - row["charge"]},
                    "reconciliation event differs")
            exposure -= outstanding.pop(identity) - row["charge"]
            settled.add(identity)
        elif kind == "slot-complete":
            expected_fields = {"slot", "clientExitCode", "isolation"}
            if strict_terminal_lifecycle:
                expected_fields.add("attempt")
            require(identity is None and set(detail) == expected_fields,
                    "malformed slot completion")
            slot = detail["slot"]
            require(len(accounted) < len(slots) and slot == slots[len(accounted)],
                    "changed order, duplicate or replaced slot")
            requests = {name for name, row in rows.items() if row["slot"] == slot}
            require(requests and requests <= settled, "slot completed without reconciled traffic")
            proof = detail["isolation"]
            require(set(proof) == {"kind", "kernelProbe", "modelInvoked", "clientSha256", "policySha256",
                                   "workspaceRoot", "authoritativeRoot"}
                    and proof["kind"] == isolation.ISOLATION
                    and proof["kernelProbe"] == isolation.PROBE_EXPECTATIONS
                    and proof["modelInvoked"] is False and proof["clientSha256"] == isolation.CLIENT_SHA256
                    and re.fullmatch(r"[0-9a-f]{64}", proof["policySha256"]),
                    "missing registered isolation evidence")
            task, arm, run = slot.split("/")
            directory = epoch / "runs" / task / arm / ("run-" + run)
            if strict_terminal_lifecycle:
                require(detail["attempt"] == budget.validate_attempt_start(
                    directory, epoch, slot, {
                        name: pins["harnessArtifacts"][name]
                        for name in budget.TERMINAL_SOURCE_FILES
                    }, recorded_authoritative_root=proof["authoritativeRoot"]),
                    "slot attempt-start evidence differs")
            require(json.loads((directory / "result.json").read_text()).get("invalid") is False,
                    "valid terminal event has an invalid result")
            source_report = json.loads((directory / "source-inspection.json").read_text())
            require(source_report.get("inspectorSha256")
                    == source_inspector["files"]["ppw-source-inspector.dll"]
                    and source_report.get("inspectorRuntimeSha256")
                    == source_inspector["runtimeSha256"],
                    "archived source inspection differs from the registered runtime")
            require(json.loads((directory / "gateway-isolation.json").read_text()) == proof,
                    "public isolation copy differs from protected accounting evidence")
            code = detail["clientExitCode"]
            require(type(code) is int and 0 <= code < 128 and code != 124
                    and json.loads((directory / "client-invocation.json").read_text())["exitCode"] == code,
                    "interrupted or mismatched client invocation")
            accounted.append(slot)
            valid_completed.append(slot)
        elif kind == budget.TERMINAL_INVALID_EVENT:
            require(strict_terminal_lifecycle and identity is None
                    and set(detail) == {"slot", "clientExitCode", "isolation", "terminal"},
                    "malformed or unregistered terminal-invalid event")
            slot = detail["slot"]
            require(len(accounted) < len(slots) and slot == slots[len(accounted)],
                    "changed order, duplicate or replaced terminal-invalid slot")
            requests = {name for name, row in rows.items() if row["slot"] == slot}
            require(requests <= settled,
                    "terminal-invalid slot has unreconciled request traffic")
            proof = detail["isolation"]
            require(set(proof) == {"kind", "kernelProbe", "modelInvoked", "clientSha256", "policySha256",
                                   "workspaceRoot", "authoritativeRoot"}
                    and proof["kind"] == isolation.ISOLATION
                    and proof["kernelProbe"] == isolation.PROBE_EXPECTATIONS
                    and proof["modelInvoked"] is False and proof["clientSha256"] == isolation.CLIENT_SHA256
                    and re.fullmatch(r"[0-9a-f]{64}", proof["policySha256"]),
                    "missing registered isolation evidence")
            task, arm, run = slot.split("/")
            directory = epoch / "runs" / task / arm / ("run-" + run)
            terminal = budget.validate_terminal_attempt(
                directory, epoch, slot, {
                    name: pins["harnessArtifacts"][name]
                    for name in budget.TERMINAL_SOURCE_FILES
                }, recorded_authoritative_root=proof["authoritativeRoot"])
            require(detail["terminal"] == terminal
                    and detail["clientExitCode"] == terminal["clientExitCode"]
                    and budget.valid_client_exit(detail["clientExitCode"])
                    and json.loads((directory / "gateway-isolation.json").read_text()) == proof,
                    "terminal-invalid archive evidence differs")
            accounted.append(slot)
            invalid_terminal.append(slot)
        elif kind == "collection-complete":
            if strict_terminal_lifecycle:
                expected_detail = {
                    "accountedSlots": len(slots),
                    "validCompletedSlots": len(valid_completed),
                    "invalidTerminalSlots": len(invalid_terminal),
                }
                if recovery_mode:
                    expected_detail["preservedAttemptedSlots"] = preserved_attempted
            else:
                expected_detail = ({"preservedAttemptedSlots": preserved_attempted}
                                   if recovery_mode else {})
            require(index == len(final["events"]) - 1 and identity is None and detail == expected_detail
                    and accounted == slots and not outstanding and settled == set(rows),
                    "incomplete final request/slot inventory")
        else:
            raise ValueError("PP-W gateway registration: stopped, unknown or unregistered accounting event")
    require(final["events"][-1]["kind"] == "collection-complete"
            and type(final["exposureMicroUsd"]) is int
            and exposure == final["exposureMicroUsd"] == sum(row["charge"] for row in rows.values()),
            "final liability total differs")
    if strict_terminal_lifecycle:
        require({
            key: final.get(key) for key in (
                "accountedSlots", "validCompletedSlots",
                "invalidTerminalSlots", "requestBearingSlots")
        } == {
            "accountedSlots": len(accounted),
            "validCompletedSlots": len(valid_completed),
            "invalidTerminalSlots": len(invalid_terminal),
            "requestBearingSlots": len({row["slot"] for row in rows.values()}),
        }, "final terminal-attempt counters differ")
    return {"id": profile["id"], "authority": profile_proof,
            "instrumentAmendment": profile["instrumentAmendment"],
            "evidenceResolution": "verified-archive-local-files",
            "requestCount": len(rows),
            "requestBearingSlots": len({row["slot"] for row in rows.values()}),
            "completedSlots": len(valid_completed),
            "invalidTerminalSlots": len(invalid_terminal),
            "accountedSlots": len(accounted),
            "preservedAttemptedSlots": preserved_attempted,
            "accountedMicroUsd": exposure, "ceilingMicroUsd": ceiling}
