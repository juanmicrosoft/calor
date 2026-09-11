#!/usr/bin/env python3
"""Resolve the additive gateway profile and audit archived request accounting without execution."""
import importlib.util
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import re

BENCH = Path(__file__).resolve().parent
ROOT = BENCH / "registrations/ppw-rows-stage1"
PROFILE = ROOT / "gateway-execution-profile.json"
RECOVERY_PROFILE = ROOT / "gateway-execution-profile-1432.json"
RECOVERY_EVIDENCE = ROOT / "gateway-recovery-evidence-1432.json"
BASELINE = "epochs/w-rows-pilot-001/registration.json"
BASELINE_SHA256 = "b506c9ec65469640b6135708c7f1e9adbce7dcd318f6ca51e1b305156041484c"
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
    """Load the canonical reviewed #1432 authority chain; no caller-selected approvals."""
    spending = helper("ppw-spending.py")
    path = Path(path)
    require(path.resolve() == RECOVERY_EVIDENCE.resolve(),
            "recovery evidence must be the canonical reviewed #1432 registration")
    value = json.loads(path.read_text(encoding="utf-8"))
    require(value.get("schemaVersion") == 1
            and value.get("kind") == "pp-w-zero-request-recovery-evidence"
            and value.get("issue") == "https://github.com/juanmicrosoft/calor/issues/1432"
            and value.get("rootCauseReference")
            == "https://github.com/juanmicrosoft/calor/issues/1432#issuecomment-5627884715",
            "unrecognized recovery evidence")
    expected_names = {
        "recoveryAuthorization", "executionProfile", "spendingPlan", "inspectionProof",
        "failedArchiveInventory", "failedOperationalSnapshot",
    }
    require(expected_names <= set(value), "recovery evidence chain is incomplete")
    documents = {}
    for name in expected_names:
        _, documents[name] = spending.pinned_document(ROOT, value[name], name)
    require(value["executionProfile"] == {
        "path": RECOVERY_PROFILE.name, "sha256": spending.digest(RECOVERY_PROFILE),
    }, "recovery evidence selects another execution profile")
    authorization = documents["recoveryAuthorization"]
    profile = documents["executionProfile"]
    plan = documents["spendingPlan"]
    proof = documents["inspectionProof"]
    inventory = documents["failedArchiveInventory"]
    failed = documents["failedOperationalSnapshot"]
    require(authorization.get("kind") == "pp-w-zero-request-recovery-authorization"
            and authorization.get("spendingCeilingUsd") == 1000
            and authorization.get("ledgerBinding") == plan.get("ledgerBinding")
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
    target = value.get("targetBinding")
    invariants = authorization.get("targetInvariants", {})
    require(isinstance(target, dict)
            and {key: target.get(key) for key in invariants} == invariants
            and target.get("authorizationSha256") == value["recoveryAuthorization"]["sha256"]
            and target.get("planSha256") == value["spendingPlan"]["sha256"]
            and target.get("protocolSha256") == plan.get("protocolSha256")
            and target.get("plannedSlots") == authorization.get("plannedSlots"),
            "recovery target binding is not fully pinned")
    return value, documents


def resolve_collection_profile(path):
    """Resolve only the reviewed recovery profile accepted for a new collector."""
    path = Path(path)
    require(path.resolve() == RECOVERY_PROFILE.resolve(),
            "collection requires the canonical reviewed #1432 recovery profile")
    registration = _resolve_profile(path, True)
    evidence, documents = load_recovery_evidence()
    selected = registration["stages"]["pilot"]
    selected["recoveryEvidence"] = {
        "path": RECOVERY_EVIDENCE.name, "sha256": helper("ppw-spending.py").digest(RECOVERY_EVIDENCE),
    }
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
    require(prices == budget.price_contract(), "gateway prices differ from implemented accounting")
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
        "stage": "pilot", "epochId": pins["epochId"], "priceSha256": budget.price_identity(),
        "authorizationSha256": selected["spendAuthorization"]["sha256"],
        "protocolSha256": plan["protocolSha256"], "planSha256": selected["spendingPlan"]["sha256"],
        "harnessArtifacts": pins["harnessArtifacts"], "plannedSlots": slots,
    }
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
        require(recovered_record.get("invalid") is True
                and recovered_record.get("censored") is True
                and recovered_record.get("recoveredAttempt", {}).get("slot") == slot
                and recovered_record["recoveredAttempt"].get("providerRequests") == 0
                and recovered_record["recoveredAttempt"].get("replacementPermitted") is False
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
        require(request == expected_request and row["reserved"] == request["maximumMicroUsd"],
                "request reservation differs from implemented liability")
        receipt = budget.decode(row["usage"])
        cost, verified = budget.reconciled_cost(
            request, receipt["model"], receipt["usage"], receipt["stopReason"])
        require(receipt == verified and type(row["charge"]) is int and row["charge"] == cost
                and row["state"] == "reconciled" and row["reason"] is None,
                "unknown or incorrectly reconciled charge")
        rows[identity] = row
    outstanding, settled, completed, exposure = {}, set(), list(preserved_attempted), 0
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
            require(len(completed) < len(slots) and row["slot"] == slots[len(completed)]
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
            require(identity is None and set(detail) == {"slot", "clientExitCode", "isolation"},
                    "malformed slot completion")
            slot = detail["slot"]
            require(len(completed) < len(slots) and slot == slots[len(completed)],
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
            completed.append(slot)
        elif kind == "collection-complete":
            expected_detail = ({"preservedAttemptedSlots": preserved_attempted}
                               if recovery_mode else {})
            require(index == len(final["events"]) - 1 and identity is None and detail == expected_detail
                    and completed == slots and not outstanding and settled == set(rows),
                    "incomplete final request/slot inventory")
        else:
            raise ValueError("PP-W gateway registration: stopped, unknown or unregistered accounting event")
    require(final["events"][-1]["kind"] == "collection-complete"
            and type(final["exposureMicroUsd"]) is int
            and exposure == final["exposureMicroUsd"] == sum(row["charge"] for row in rows.values()),
            "final liability total differs")
    return {"id": profile["id"], "authority": profile_proof,
            "instrumentAmendment": profile["instrumentAmendment"],
            "evidenceResolution": "verified-archive-local-files",
            "requestCount": len(rows),
            "completedSlots": len(completed) - len(preserved_attempted),
            "accountedSlots": len(completed),
            "preservedAttemptedSlots": preserved_attempted,
            "accountedMicroUsd": exposure, "ceilingMicroUsd": ceiling}
