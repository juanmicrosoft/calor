#!/usr/bin/env python3
"""Resolve the additive gateway profile and audit archived request accounting without execution."""
import importlib.util
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import re

BENCH = Path(__file__).resolve().parent
ROOT = BENCH / "registrations/ppw-rows-stage1"
PROFILE = ROOT / "gateway-execution-profile.json"
BASELINE = "epochs/w-rows-pilot-001/registration.json"
BASELINE_SHA256 = "b506c9ec65469640b6135708c7f1e9adbce7dcd318f6ca51e1b305156041484c"


def helper(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W gateway registration: " + message)


def resolve_profile(path):
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
        for value in transport["observations"]), "native requests were not within the priced schema")
    require(set(transport.get("sourceHashes", {})) ==
            {"probe-ppw-gateway.py", "ppw-gateway-client.py", "run-pair.sh", "ppw-gateway-budget.py"}
            and all(spending.digest(BENCH / name) == sha for name, sha in transport["sourceHashes"].items()),
            "native compatibility evidence binds different source")
    selected = registration["stages"]["pilot"]
    selected["epochId"] = profile["epochId"]
    for name in ("spendAuthorization", "spendingPlan", "instrumentAmendment",
                 "stageRegistration", "modelRegistration"):
        spending.pinned_document(Path(path).parent, profile[name], name)
        selected[name] = profile[name]
    selected["executionProfile"] = {"path": Path(path).name, "sha256": spending.digest(path)}
    registration.update(collectionAuthorized=True, fundingStatus="approved")
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
    profile = json.loads(PROFILE.read_text())
    profile_proof = {"path": PROFILE.relative_to(BENCH).as_posix(), "sha256": spending.digest(PROFILE)}
    require(epoch is not None, "an operational archive is required")
    epoch = Path(epoch)
    require(archive_proof(epoch, selected.get("executionProfile"), profile_proof) == profile,
            "execution profile contents differ")
    proofs = {name: archive_proof(epoch, selected.get(name), profile[name])
              for name in ("spendAuthorization", "spendingPlan", "instrumentAmendment",
                           "stageRegistration", "modelRegistration")}
    registration = json.loads((epoch / "registration.json").read_text())
    plan, authorization, amendment = (proofs[name] for name in
                                     ("spendingPlan", "spendAuthorization", "instrumentAmendment"))
    require(pins["epochId"] == profile["epochId"] and pins["stage"] == "pilot"
            and pins["dataKind"] in profile["allowedDataKinds"], "wrong gateway epoch or stage")
    require(pins["lifecycle"] == "collected" and not (epoch / "collection-outcome.json").exists(),
            "incomplete collection cannot be adjudicated")
    require(pins["harnessArtifacts"] == amendment["replacementHarnessArtifacts"]
            == spending.artifact_manifest(spending.GATEWAY), "mixed or changed gateway sources")
    require(plan["clientControl"]["kind"] == spending.GATEWAY, "unregistered gateway control")
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
    if pins["dataKind"] == "empirical":
        spending.validate_forecast(plan, ceiling, len(slots))
    for snapshot in (initial, final):
        require(snapshot.get("kind") == budget.KIND and snapshot.get("binding") == expected_binding
                and type(snapshot.get("ceilingMicroUsd")) is int
                and snapshot["ceilingMicroUsd"] == ceiling and snapshot.get("verdict") is None,
                "spending snapshot scope differs")
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
    outstanding, settled, completed, exposure = {}, set(), [], 0
    require(all(type(event.get("id")) is int for event in final["events"])
            and [event["id"] for event in final["events"]] == list(range(1, len(final["events"]) + 1)),
            "missing or reordered accounting events")
    for index, event in enumerate(final["events"]):
        kind, identity, detail = event["kind"], event["request_id"], budget.decode(event["detail"])
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
            require(set(proof) == {"kind", "kernelProbe", "modelInvoked", "clientSha256", "policySha256"}
                    and proof["kind"] == isolation.ISOLATION
                    and proof["kernelProbe"] == isolation.PROBE_EXPECTATIONS
                    and proof["modelInvoked"] is False and proof["clientSha256"] == isolation.CLIENT_SHA256
                    and re.fullmatch(r"[0-9a-f]{64}", proof["policySha256"]),
                    "missing registered isolation evidence")
            task, arm, run = slot.split("/")
            directory = epoch / "runs" / task / arm / ("run-" + run)
            require(json.loads((directory / "gateway-isolation.json").read_text()) == proof,
                    "public isolation copy differs from protected accounting evidence")
            code = detail["clientExitCode"]
            require(type(code) is int and 0 <= code < 128 and code != 124
                    and json.loads((directory / "client-invocation.json").read_text())["exitCode"] == code,
                    "interrupted or mismatched client invocation")
            completed.append(slot)
        elif kind == "collection-complete":
            require(index == len(final["events"]) - 1 and identity is None and detail == {}
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
            "requestCount": len(rows), "completedSlots": len(completed),
            "accountedMicroUsd": exposure, "ceilingMicroUsd": ceiling}
