#!/usr/bin/env python3
"""Single-compiler, single-stage PP-W instrument; not a scientific registration.

Version 2 deliberately has no inherited A-1.12 effect size, spending ceiling,
sample size, or verdict rule. The historical adjudicator remains in
ppw-analyze.py. This module records descriptive stage data, never funds a stage
or promotes a pilot to a confirmatory verdict.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys

BENCH = Path(__file__).resolve().parent
REPO = BENCH.parent.parent
KIND = "pp-w-rows-redesign"
ARMS = {
    "A": {"label": "calor-permissive", "policy": "permissive",
          "flags": ["--permissive-effects"], "editMechanism": "raw"},
    "B": {"label": "calor-strict", "policy": "strict",
          "flags": [], "editMechanism": "raw"},
}


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def load(path):
    with open(path, encoding="utf-8") as stream:
        value = json.load(stream)
    require(isinstance(value, dict), "%s must contain an object" % path)
    return value


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_new(path, value):
    """Append-only: even an analysis rerun cannot silently replace evidence."""
    with open(path, "x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, sort_keys=True, allow_nan=False)
        stream.write("\n")


def identifier(value):
    require(isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", value),
            "expected one epoch/task id, not a path, list, or wildcard: %r" % value)
    return value


def local(root, relative):
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute(),
            "artifact path must be relative")
    root = Path(root).resolve()
    path = root / relative
    require(".." not in Path(relative).parts and path.resolve().is_relative_to(root),
            "artifact escapes its containing directory: %s" % relative)
    return path


def isolated_tree(root):
    """Do not follow links into another epoch (including hard-linked records)."""
    root = Path(root)
    require(not root.is_symlink(), "epoch must not be a symlink")
    for path in root.rglob("*"):
        require(not path.is_symlink(), "symlink in epoch: %s" % path)
        if path.is_file():
            require(path.stat().st_nlink == 1, "hard-linked epoch artifact: %s" % path)


def helper(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_registration(registration, stage, epoch_id):
    require(stage in ("pilot", "confirmatory"), "stage must be pilot or confirmatory")
    require(registration.get("schemaVersion") == 2, "registration schemaVersion must be 2")
    require(registration.get("status") == "frozen", "task registration is not frozen")
    require(registration.get("supersedes") == "A-1.12" and registration.get("cause"),
            "explicit A-1.12 supersession and written cause required; never rewrite old pins")
    require(registration.get("reviews") and isinstance(registration["reviews"], list),
            "supersession requires independent review references")
    require(re.fullmatch(r"[0-9a-f]{40}", registration.get("compilerCommit", "")),
            "registration must pin the exact compiler commit")
    stages = registration.get("stages", {})
    require(isinstance(stages, dict) and stage in stages, "stage not registered: %s" % stage)
    require(set(stages) <= {"pilot", "confirmatory"}, "unrecognized registered stage role")
    ids = [identifier(item.get("epochId")) for item in stages.values()]
    require(len(ids) == len(set(ids)), "pilot and confirmatory must have separate epoch ids")
    selected = stages[stage]
    require(selected.get("epochId") == epoch_id, "wrong-stage epoch id for %s" % stage)
    n = selected.get("runsPerArm")
    require(type(n) is int and n > 0, "stage runsPerArm must be registered, not defaulted")
    tasks = registration.get("tasks")
    require(isinstance(tasks, list) and tasks and len(tasks) == len(set(tasks)),
            "registration needs a nonempty unique task denominator")
    for task in tasks:
        identifier(task)
    require(isinstance(registration.get("sourceInspections"), dict)
            and set(registration["sourceInspections"]) == set(tasks)
            and all(isinstance(value, dict) for value in registration["sourceInspections"].values()),
            "frozen native source-inspection certificates required for every task")
    artifacts = registration.get("artifacts")
    require(isinstance(artifacts, dict) and artifacts, "frozen task artifact hashes required")
    for path, sha in artifacts.items():
        local(Path("."), path)
        require(re.fullmatch(r"[0-9a-f]{64}", sha or ""), "invalid artifact SHA-256: %s" % path)
    for task in tasks:
        require(task + "/pair.json" in artifacts, "unfrozen pair manifest: %s" % task)
    return selected


def validate_pins(pins, registration, stage, epoch_id):
    require(pins.get("schemaVersion") == 2 and pins.get("kind") == KIND,
            "not a redesigned schemaVersion 2 epoch")
    require(pins.get("epochId") == epoch_id, "pins epochId does not match the single input id")
    require(pins.get("stage") == stage, "wrong-stage epoch: expected %s, got %s"
            % (stage, pins.get("stage")))
    selected = validate_registration(registration, stage, epoch_id)
    for key in ("modelPin", "agentVersion"):
        require(isinstance(selected.get(key), str) and selected[key].strip()
                and pins.get(key) == selected[key],
                "%s differs from the registered stage identity" % key)
    require(pins.get("runsPerArm") == selected["runsPerArm"], "run denominator differs from registration")
    require(pins.get("suite") == registration["tasks"], "task denominator differs from registration")
    require(pins.get("arms") == ARMS, "arms must differ solely by permissive-effects policy")
    require(not any(key in pins for key in ("armA", "armB")),
            "per-arm compiler pins are forbidden: use the shared compiler")
    schema = load(BENCH / "ppw-pins.schema.json")
    require(set(schema["required"]) <= set(pins) <= set(schema["properties"]),
            "pins fields differ from schemaVersion 2")
    compiler = pins.get("compiler", {})
    require(set(compiler) == set(schema["properties"]["compiler"]["required"]),
            "shared compiler fields differ from schemaVersion 2")
    require(type(pins["runsPerArm"]) is int, "run denominator must be an integer")
    require(compiler.get("commit") == registration["compilerCommit"],
            "shared compiler commit differs from registration")
    require(compiler.get("release") == "v0.18.0", "frozen redesign requires the v0.18.0 release")
    for key in ("calorSha256", "calorTasksSha256", "compilerHash"):
        require(re.fullmatch(r"[0-9a-f]{64}", compiler.get(key, "")),
                "shared compiler needs %s SHA-256" % key)
    require(compiler.get("repoRoot") and compiler.get("calorDll"),
            "shared compiler root and DLL required")
    require(all(value.get("compilerSha256") == compiler["calorSha256"]
                for value in registration["sourceInspections"].values()),
            "source-inspection compiler differs from the shared compiler")
    source_inspector = selected.get("sourceInspector")
    if source_inspector is not None:
        require(source_inspector.get("compilerSha256") == compiler["calorSha256"],
                "registered source-inspector runtime uses another compiler")
    require(pins.get("dataKind") in ("empirical", "synthetic"), "dataKind must be explicit")
    require(pins.get("mode") == "live", "null-agent plumbing is not an analysable collection")
    require(pins.get("harnessCommit") and pins.get("modelPin") and pins.get("agentVersion"),
            "harness, model and agent identities must be recorded")
    require(pins.get("lifecycle") in ("scaffolded", "collecting", "collected", "archived"),
            "unknown epoch lifecycle")


def validate_tasks(root, registration):
    root = Path(root)
    isolated_tree(root)
    expected = registration["artifacts"]
    actual = {str(path.relative_to(root)) for path in root.rglob("*") if path.is_file()}
    require(actual == set(expected), "task artifact inventory differs from frozen registration")
    for path, sha in expected.items():
        require(digest(local(root, path)) == sha, "frozen artifact changed: %s" % path)
    capture = helper("harness-capture.py")
    assembly = helper("ppw-source-assembly.py")
    inspection = helper("ppw-source-inspection.py")
    for task in registration["tasks"]:
        directory = root / task
        pair = load(directory / "pair.json")
        require(pair.get("id") == task, "pair id differs from task directory")
        require(set(pair.get("arms", {})) == {a["label"] for a in ARMS.values()},
                "task must use neutral calor-permissive/calor-strict arm keys")
        fixtures = []
        for arm, definition in ARMS.items():
            entry = pair["arms"][definition["label"]]
            config = capture.resolve_pair_config(str(directory / "pair.json"), definition["label"])
            require(config["admitted"], config["reason"])
            require(config["permissiveEffects"] is (arm == "A"), "wrong arm policy")
            require(config["controlArmKind"] == ("permissive" if arm == "A" else None),
                    "retired pre-rows policy kind is forbidden")
            require(entry.get("fixture") == "starter-" + arm.lower(), "use neutral starter-a/starter-b")
            fixture = local(directory, entry["fixture"])
            fixtures.append({str(p.relative_to(fixture)): p.read_bytes()
                             for p in fixture.rglob("*") if p.is_file()})
        require(fixtures[0] and fixtures[0] == fixtures[1],
                "arm starters must be byte-identical; only the policy differs")
        tests = pair.get("tests", {})
        observing = tests.get("effectObservingTests")
        require(isinstance(observing, list) and observing and len(observing) == len(set(observing)),
                "named effect-observing tests required")
        signature = tests.get("effectFailureSignature")
        require(isinstance(signature, str) and signature.startswith("HELDOUT_EFFECT:"),
                "explicit held-out effect-failure signature required (not aggregate failures)")
        indicator = pair.get("shapeRealizedIndicator", {}).get("sourceRegex")
        require(isinstance(indicator, str) and indicator, "shape indicator required")
        re.compile(indicator)
        seeded = pair.get("seeded", {})
        require(isinstance(seeded, dict), "explicit seeded control roles required")
        _, honest = capture.honest_reference_cells(pair)
        require(isinstance(honest, dict) and set(honest) == {"a", "b"},
                "explicit seeded.clean or seeded.honest controls for both arms required")
        require(isinstance(seeded.get("laundering"), dict) and set(seeded["laundering"]) == {"a", "b"},
                "explicit seeded.laundering controls for both arms required")
        for arm in ("a", "b"):
            fixture = directory / ("starter-" + arm)
            starter = assembly.source_paths(pair, fixture, editable_only=True)
            clean = local(directory, honest[arm])
            laundering = local(directory, seeded["laundering"][arm])
            clean_sources = assembly.source_paths(pair, clean, editable_only=True)
            laundering_sources = assembly.source_paths(pair, laundering, editable_only=True)
            if assembly.definition(pair):
                immutable = assembly.check_fragments(pair, fixture)
                assembly.check_fragments(pair, clean, immutable)
                assembly.check_fragments(pair, laundering, immutable)
            require(starter and clean_sources and laundering_sources,
                    "starter, honest negative, and laundering positive sources required")
        certificate = registration.get("sourceInspections", {}).get(task)
        require(isinstance(certificate, dict), "missing frozen source inspection: " + task)
        inspection.validate_controls(certificate, pair, directory, certificate.get("compilerSha256"))
    helper("ppw-registration.py").check_supersession(registration, root)


def analyze(epochs_root, epoch_id, stage):
    """One selected epoch, no sibling enumeration and no pooled input API."""
    identifier(epoch_id)
    epoch = local(epochs_root, epoch_id)
    isolated_tree(epoch)
    pins = load(epoch / "pins.json")
    registration = load(epoch / "registration.json")
    validate_pins(pins, registration, stage, epoch_id)
    selected = registration["stages"][stage]
    require(digest(epoch / "registration.json") == pins.get("registrationSha256"),
            "registration hash differs from pins")
    validate_tasks(epoch / "tasks", registration)
    require(pins["lifecycle"] in ("collected", "archived"),
            "epoch has not completed collection: %s" % pins["lifecycle"])
    nested_pins = {p for p in epoch.rglob("pins.json") if p != epoch / "pins.json"}
    historical_epochs = selected.get("historicalSourceEpochIds", [])
    allowed_historical_pins = {
        epoch / "admission/historical" / source_epoch / "pins.json"
        for source_epoch in historical_epochs
    }
    require((not nested_pins and not historical_epochs)
            or (len(historical_epochs) == 2
                and len(set(historical_epochs)) == 2
                and nested_pins == allowed_historical_pins),
            "nested epoch/pooling is forbidden outside the two registered historical copies")
    expected = {
        "%s/%s/run-%d/result.json" % (task, definition["label"], run)
        for task in pins["suite"] for definition in ARMS.values()
        for run in range(1, pins["runsPerArm"] + 1)
    }
    actual = {str(path.relative_to(epoch / "runs"))
              for path in (epoch / "runs").rglob("result.json")}
    require(actual == expected, "run inventory differs from registered cells; pooling or missing runs")
    capture = helper("harness-capture.py")
    assembly = helper("ppw-source-assembly.py")
    inspection = helper("ppw-source-inspection.py")
    token_usage = helper("token-usage.py")
    cells = []
    for task in pins["suite"]:
        pair = load(epoch / "tasks" / task / "pair.json")
        regex = re.compile(pair["shapeRealizedIndicator"]["sourceRegex"])
        for arm, definition in ARMS.items():
            cell = {"pair": task, "arm": arm, "plannedRuns": pins["runsPerArm"],
                    "validRuns": 0, "invalidRuns": 0, "censoredRuns": 0,
                    "escapes": 0, "shapeRealized": 0, "didNotBuildAtDeclaredDone": 0,
                    "namedTestFailuresWithoutEffect": [], "outputTokens": [],
                    "unscorableHeldoutRuns": [], "invalidReasons": [], "optionsHashes": [],
                    "changedPublicApiRuns": [], "unscorablePublicApiRuns": [],
                    "unscorableShapeRuns": []}
            for run in range(1, pins["runsPerArm"] + 1):
                run_dir = epoch / "runs" / task / definition["label"] / ("run-%d" % run)
                record = load(run_dir / "result.json")
                identity = (record.get("epochId"), record.get("stage"), record.get("pair"),
                            record.get("arm"), record.get("run"), record.get("dataKind"))
                require(identity == (epoch_id, stage, task, definition["label"], run, pins["dataKind"]),
                        "cross-epoch, wrong-stage or misplaced run: %s" % run_dir)
                require(record.get("nullAgent") is False, "null-agent run is not a measurement")
                require(record.get("compilerCommit") == pins["compiler"]["commit"],
                        "run compiler commit differs from shared compiler")
                require(record.get("productCompilerHash") == pins["compiler"]["compilerHash"],
                        "run product canary differs from shared compiler")
                require(record.get("armRepoRoot") == pins["compiler"]["repoRoot"],
                        "run product root differs from shared compiler")
                require(record.get("armConfigKey") == definition["label"]
                        and record.get("permissiveEffects") is (arm == "A")
                        and record.get("controlArmKind") == ("permissive" if arm == "A" else None)
                        and record.get("editMechanism") == "raw",
                        "run policy differs from pinned arm")
                require(type(record.get("invalid")) is bool and type(record.get("censored")) is bool,
                        "run validity and censoring must be booleans")
                if record["invalid"]:
                    cell["invalidRuns"] += 1
                    cell["censoredRuns"] += 1
                    reason_file = run_dir / "invalid.txt"
                    require(reason_file.is_file() and reason_file.read_text().strip(),
                            "invalid run requires its archived reason")
                    cell["invalidReasons"].append({"run": run, "reason": reason_file.read_text().strip()})
                    continue
                require((run_dir / "transcript.jsonl").is_file(), "missing transcript")
                require(record.get("turns", {}).get("assistantMessages")
                        == capture.count_turns(str(run_dir / "transcript.jsonl"))["assistantMessages"],
                        "top-level turn count differs from transcript")
                before, after = load(run_dir / "policy-before.json"), load(run_dir / "policy-after.json")
                require(before == after and before.get("configurationSha256"),
                        "workspace policy/configuration changed during run")
                require(before.get("policy") == {"CalorEnforceEffects": True,
                                                "CalorPermissiveEffects": arm == "A"},
                        "effective workspace policy differs from pinned arm")
                state = record.get("buildState", {})
                built = record.get("finalBuild", {}).get("ok")
                require(type(built) is bool, "missing declared-done build outcome")
                for value in (record.get("compilerHash"), state.get("compilerHash")):
                    require(value == pins["compiler"]["compilerHash"] or (not built and value is None),
                            "mixed compiler hashes within epoch")
                require(record.get("armCanary") == ("permissive-ok" if arm == "A" else "strict-ok"),
                        "wrong policy canary verdict")
                require((isinstance(state.get("optionsHash"), str) and state["optionsHash"]) or not built,
                        "missing policy optionsHash")
                cell["optionsHashes"].append(state.get("optionsHash"))
                cell["validRuns"] += 1
                cell["censoredRuns"] += int(record["censored"])
                cell["didNotBuildAtDeclaredDone"] += int(not built)
                final_source = run_dir / "final-src"
                if assembly.definition(pair):
                    frozen_source = epoch / "tasks" / task / ("starter-" + arm.lower())
                    immutable = assembly.check_fragments(pair, frozen_source)
                    assembly.check_fragments(pair, final_source, immutable)
                sources = assembly.source_paths(pair, final_source, editable_only=True)
                require(sources, "missing final sources for shape indicator")
                source_report = load(run_dir / "source-inspection.json")
                starter = epoch / "tasks" / task / ("starter-" + arm.lower())
                inspection.validate(source_report, pair, {"baseline": starter, "final": final_source},
                                    pins["compiler"]["calorSha256"], selected.get("sourceInspector"))
                baseline = inspection.observation(source_report, "baseline")
                frozen = inspection.observation(registration["sourceInspections"][task],
                                                "starter-" + arm.lower())
                require(baseline == frozen, "run baseline differs from frozen source inspection")
                final = inspection.observation(source_report, "final")
                api_preserved = None
                if final["parseOk"]:
                    api_preserved = final["publicApi"] == baseline["publicApi"]
                    cell["shapeRealized"] += int(any(regex.search(call) for call in final["calls"]))
                    if not api_preserved:
                        cell["changedPublicApiRuns"].append(run)
                else:
                    cell["unscorableShapeRuns"].append(run)
                    if built:
                        cell["unscorablePublicApiRuns"].append(run)
                if built:
                    log = run_dir / ".ho_final.txt"
                    require(log.is_file(), "built run missing held-out log")
                    observing = pair["tests"]["effectObservingTests"]
                    signature = pair["tests"]["effectFailureSignature"]
                    outcomes = list(capture._result_lines(log.read_text()))
                    observed = {name for _, name, _ in outcomes}
                    readable = (set(observing) <= observed
                                and not any(name in observing and state not in ("PASSED", "FAIL")
                                            for state, name, _ in outcomes))
                    if not readable:
                        cell["unscorableHeldoutRuns"].append(run)
                    failed = {name for state, name, _ in outcomes if state == "FAIL"}
                    effect = {name for state, name, text in outcomes
                              if state == "FAIL" and text and signature in text}
                    cell["escapes"] += int(readable and api_preserved is True
                                           and bool(set(observing) & effect))
                    if failed - (set(observing) & effect):
                        cell["namedTestFailuresWithoutEffect"].append(run)
                usage = token_usage.compute(token_usage.load_envelope(str(run_dir / "agent.json")))
                require(usage["source"] != "missing", "missing agent token envelope")
                cell["outputTokens"].append(usage["output_tokens_corrected"])
            valid = cell["validRuns"]
            cell["escapeRate"] = (round(cell["escapes"] / valid, 4)
                                  if valid and not cell["unscorableHeldoutRuns"]
                                  and not cell["unscorablePublicApiRuns"] else None)
            cell["shapeRealizedRate"] = (round(cell["shapeRealized"] / valid, 4)
                                         if valid and not cell["unscorableShapeRuns"] else None)
            cells.append(cell)
    return {
        "schemaVersion": 2, "kind": KIND, "epoch": epoch_id, "stage": stage,
        "dataKind": pins["dataKind"], "empirical": pins["dataKind"] == "empirical",
        "dryRun": False, "epochRun": True, "lifecycle": pins["lifecycle"],
        "registrationSha256": pins["registrationSha256"], "compiler": pins["compiler"],
        "arms": pins["arms"], "perCell": cells, "verdict": None,
        "reason": "Descriptive %s record only; no confirmatory verdict, sizing or funding decision."
                  % stage,
        "pilotPoolingAllowed": False,
    }


def record_stage(epochs_root, epoch_id, stage):
    report = analyze(epochs_root, epoch_id, stage)
    path = local(epochs_root, epoch_id) / "ppw-stage-ledger.json"
    write_new(path, report)
    return path


def command(argv, **kwargs):
    return subprocess.run(argv, check=True, text=True, capture_output=True, **kwargs).stdout.strip()


def validate_harness_checkout(admission):
    """Allow only the exact registered failed archive as untracked evidence."""
    tracked = command([
        "git", "-C", str(REPO), "status", "--porcelain", "--untracked-files=no", "--", str(BENCH),
    ])
    require(not tracked, "harness checkout has tracked changes")
    status = command([
        "git", "-C", str(REPO), "status", "--porcelain", "--untracked-files=all", "--", str(BENCH),
    ])
    if not status:
        return
    recovery = admission.get("recovery")
    require(isinstance(recovery, dict), "harness checkout has unregistered untracked data")
    archive = Path(recovery["failedArchive"]).resolve()
    require(archive == (BENCH / "epochs/w-rows-pilot-gateway-001").resolve(),
            "recovery names another failed archive")
    recovery_module = helper("ppw-gateway-recovery.py")
    inventory = recovery_module.archive_inventory(archive)
    require(inventory["sha256"] == recovery["failedArchiveInventorySha256"],
            "registered failed archive inventory changed")
    allowed = {
        (archive / item["path"]).relative_to(REPO).as_posix()
        for item in inventory["files"]
    }
    observed = set()
    for line in status.splitlines():
        require(line.startswith("?? "), "harness checkout has non-untracked changes")
        observed.add(line[3:])
    require(observed and observed <= allowed,
            "only the exact registered failed archive may remain untracked")


def preserve_recovered_attempt(epoch, pins, admission, failed_inventory, inspection_proof):
    recovery = admission["recovery"]
    preserved = recovery["preservedAttemptedSlots"]
    require(preserved == [admission["plannedSlots"][0]["id"]],
            "recovery does not preserve the first scheduled attempt")
    slot = preserved[0]
    task, arm, run_text = slot.split("/")
    run = int(run_text)
    source = Path(recovery["failedArchive"]) / "runs" / task / arm / ("run-%d" % run)
    invocation = load(source / "client-invocation.json")
    require(set(invocation) == {"exitCode"} and type(invocation["exitCode"]) is int
            and 0 < invocation["exitCode"] < 128 and invocation["exitCode"] != 124,
            "preserved attempt lacks the fixed failed client launch")
    raw_result = source / "result.json"
    require(raw_result.is_file() and not raw_result.is_symlink(),
            "preserved attempt lacks its opaque original result record")
    original_root = Path(recovery["failedArchive"])
    original_pins = original_root / "pins.json"
    original_registration = original_root / "registration.json"
    require(original_pins.is_file() and original_registration.is_file(),
            "preserved attempt lacks its original profile provenance")
    original_sources = recovery["oldBinding"]["harnessArtifacts"]
    require(isinstance(original_sources, dict)
            and original_sources.get("run-pair.sh")
            and original_sources.get("ppw-gateway-budget.py"),
            "preserved attempt lacks original producer source identities")
    require(failed_inventory.get("sha256") == recovery["failedArchiveInventorySha256"],
            "preserved failed inventory differs from the recovery authority")
    inventory_files = {
        item["path"]: item["sha256"] for item in failed_inventory.get("files", [])
        if isinstance(item, dict) and set(item) >= {"path", "sha256"}
    }
    failed_run_root = "runs/%s/%s/run-%d/" % (task, arm, run)
    provenance_resolution = (
        "verified-failed-archive" if pins["dataKind"] == "empirical"
        else "synthetic-registered-proof-double")
    if pins["dataKind"] == "empirical":
        require(digest(raw_result) == inventory_files.get(failed_run_root + "result.json")
                and digest(source / "client-invocation.json")
                == inventory_files.get(failed_run_root + "client-invocation.json")
                and digest(original_pins) == inventory_files.get("pins.json")
                and digest(original_registration)
                == inspection_proof["failedRegistrationSha256"],
                "preserved original files differ from the registered failed inventory")
    destination = epoch / "runs" / task / arm / ("run-%d" % run)
    destination.mkdir(parents=True)
    shutil.copy2(source / "client-invocation.json", destination / "client-invocation.json")
    (destination / "invalid.txt").write_text(
        "Preserved pre-forward client launch from w-rows-pilot-gateway-001; "
        "the registered attempt is invalid and cannot be replaced.\n",
        encoding="utf-8")
    write_new(destination / "result.json", {
        "recordKind": "pp-w-recovered-invalid-wrapper-v1",
        "epochId": pins["epochId"], "stage": pins["stage"], "dataKind": pins["dataKind"],
        "pair": task, "arm": arm, "run": run, "nullAgent": False,
        "compilerCommit": pins["compiler"]["commit"],
        "productCompilerHash": pins["compiler"]["compilerHash"],
        "armRepoRoot": pins["compiler"]["repoRoot"], "armConfigKey": arm,
        "permissiveEffects": arm == "calor-permissive",
        "controlArmKind": "permissive" if arm == "calor-permissive" else None,
        "editMechanism": "raw", "invalid": True, "censored": True,
        "recoveredAttempt": {
            "kind": "pp-w-opaque-original-invalid-attempt-v1",
            "failedEpochId": recovery["oldBinding"]["epochId"],
            "wrapperEpochId": pins["epochId"],
            "slot": slot,
            "failedArchiveInventorySha256": recovery["failedArchiveInventorySha256"],
            "failedLedgerSha256": recovery["failedLedgerSha256"],
            "provenanceResolution": provenance_resolution,
            "originalProfile": {
                "pinsSha256": inventory_files["pins.json"],
                "pinsIdentitySha256": inspection_proof["failedPinsIdentitySha256"],
                "registrationSha256": inspection_proof["failedRegistrationSha256"],
                "sourceHashes": original_sources,
            },
            "originalRun": {
                "rawRecordRelativePath": failed_run_root + "result.json",
                "rawRecordSha256": inventory_files[failed_run_root + "result.json"],
                "clientInvocationSha256":
                    inventory_files[failed_run_root + "client-invocation.json"],
                "runPairSha256": original_sources["run-pair.sh"],
            },
            "wrapperSourceHashes": {
                name: pins["harnessArtifacts"][name]
                for name in ("run-pair.sh", "ppw-gateway-budget.py", "ppw-instrument.py")
            },
            "clientExitCode": invocation["exitCode"],
            "providerRequests": 0,
            "replacementPermitted": False,
        },
    })


def preserve_disposition_attempts(epoch, pins, admission):
    """Copy both immutable predecessor archives and write two explicit wrappers."""
    disposition = admission["disposition"]
    authorization = disposition["authorizationValue"]
    evidence_reference = disposition["evidence"]
    archives = (
        Path(disposition["originalArchive"]),
        Path(disposition["failedArchive"]),
    )
    historical_root = epoch / "admission/historical"
    for index, source in enumerate(archives):
        attempt = authorization["preservedAttempts"][index]
        archive = authorization["predecessorArchives"][index]
        require(source.resolve().name == attempt["epochId"]
                and source.resolve().name == archive["epochId"],
                "historical archive path names another epoch")
        destination = historical_root / attempt["epochId"]
        require(not destination.exists(), "historical archive copy would overwrite evidence")
        shutil.copytree(source, destination, copy_function=shutil.copy2)
        copied = helper("ppw-gateway-disposition.py").archive_inventory(destination)
        require(copied["sha256"] == archive["inventorySha256"]
                and copied["fileCount"] == archive["fileCount"]
                and copied["byteCount"] == archive["byteCount"],
                "historical archive changed while creating the portable copy")
        task, arm, run_text = attempt["slot"].split("/")
        run = int(run_text)
        wrapper = epoch / "runs" / task / arm / ("run-%d" % run)
        wrapper.mkdir(parents=True)
        (wrapper / "invalid.txt").write_text(
            "Historical consumed attempt retained by the registered #1436 disposition; "
            "invalid, censored, and never replaceable.\n",
            encoding="utf-8")
        write_new(wrapper / "result.json", {
            "recordKind": "pp-w-historical-disposition-wrapper-v1",
            "epochId": pins["epochId"], "stage": pins["stage"], "dataKind": pins["dataKind"],
            "pair": task, "arm": arm, "run": run, "nullAgent": False,
            "compilerCommit": pins["compiler"]["commit"],
            "productCompilerHash": pins["compiler"]["compilerHash"],
            "armRepoRoot": pins["compiler"]["repoRoot"], "armConfigKey": arm,
            "permissiveEffects": arm == "calor-permissive",
            "controlArmKind": "permissive" if arm == "calor-permissive" else None,
            "editMechanism": "raw", "invalid": True, "censored": True,
            "historicalDisposition": {
                "kind": authorization["kind"],
                "sourceEpochId": attempt["epochId"],
                "slot": attempt["slot"],
                "classification": attempt["classification"],
                "replacementPermitted": False,
                "dispositionReference": evidence_reference,
                "originalArchive": {
                    "relativeRoot": "admission/historical/" + attempt["epochId"],
                    "originalPathIdentitySha256": archive["pathSha256"],
                    "inventorySha256": archive["inventorySha256"],
                    "fileCount": archive["fileCount"],
                    "byteCount": archive["byteCount"],
                },
                "originalRun": {
                    "rawRecordPath": attempt["rawRecordPath"],
                    "rawRecordSha256": attempt["rawRecordSha256"],
                    "clientInvocationPath": attempt["clientInvocationPath"],
                    "clientInvocationSha256": attempt["clientInvocationSha256"],
                    "invalidReasonPath": attempt["invalidReasonPath"],
                    "invalidReasonSha256": attempt["invalidReasonSha256"],
                    "attemptStartPath": attempt["attemptStartPath"],
                    "attemptStartSha256": attempt["attemptStartSha256"],
                },
                "originalProfile": {
                    "pinsPath": archive["pinsPath"],
                    "pinsSha256": archive["pinsSha256"],
                    "sourceHashes": attempt["sourceHashes"],
                    "sourceEvidenceKind": attempt["sourceEvidenceKind"],
                    "sourceCommit": attempt["sourceCommit"],
                },
                "wrapperSourceHashes": {
                    name: pins["harnessArtifacts"][name]
                    for name in ("run-pair.sh", "ppw-gateway-budget.py", "ppw-instrument.py")
                },
            },
        })


def require_gateway_collecting(ledger, budget_module):
    snapshot = ledger.snapshot()
    if snapshot["state"] != "collecting":
        diagnostics = [
            budget_module.decode(event["detail"])["diagnostic"]
            for event in snapshot["events"] if event["kind"] == "stopped"
            and "diagnostic" in budget_module.decode(event["detail"])
        ]
        raise ValueError("pilot incomplete: " + snapshot["state"]
                         + (" (" + diagnostics[-1] + ")" if diagnostics else ""))
    return snapshot


def current_harness_artifacts(names):
    return {name: digest(BENCH / name) for name in names}


def product(root, commit):
    root = Path(root).resolve()
    require(command(["git", "-C", str(root), "rev-parse", "HEAD"]) == commit,
            "compiler checkout does not match registered commit")
    require(command(["git", "-C", str(root), "rev-parse", "v0.18.0^{commit}"]) == commit,
            "compiler commit is not the frozen v0.18.0 release")
    require(not command(["git", "-C", str(root), "status", "--porcelain"]),
            "compiler checkout is dirty (including untracked sources)")
    dll = root / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
    tasks = root / "src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll"
    require(dll.is_file() and tasks.is_file(), "build the shared compiler and Tasks product first")
    return {"commit": commit, "release": "v0.18.0", "repoRoot": str(root),
            "calorDll": str(dll), "calorSha256": digest(dll), "calorTasksSha256": digest(tasks)}


def validate_collection_authorization(registration, selected, directory, epoch_id, stage, confirm_paid):
    require(registration.get("collectionAuthorized") is True,
            "registration does not authorize collection")
    require(registration.get("fundingStatus") == "approved",
            "registration funding is not approved")
    proofs = {}
    for name in ("spendAuthorization", "stageRegistration", "modelRegistration"):
        artifact = selected.get(name, {})
        path = local(directory, artifact.get("path"))
        require(path.is_file() and digest(path) == artifact.get("sha256"),
                "%s evidence is missing or changed" % name)
        proofs[name] = path
    authorization = load(proofs["spendAuthorization"])
    if authorization.get("kind") == "pp-w-historical-liability-disposition-v1":
        disposition = helper("ppw-gateway-disposition.py")
        disposition.validate_authorization(authorization)
        registration = helper("ppw-gateway-disposition-registration.py")
        evidence, documents = registration._load_evidence()
        require(selected.get("dispositionEvidence") == {
            "path": registration.EVIDENCE.name, "sha256": digest(registration.EVIDENCE)}
            and selected["spendAuthorization"] == evidence["authorization"]
            and authorization == documents["authorization"],
            "collection requires the exact activated disposition authority")
        require(digest(registration.OLD_AUTHORIZATION)
                == registration.EXPECTED_OLD_AUTHORIZATION_SHA256,
                "historical funding/null-result authorization changed")
        funding = load(registration.OLD_AUTHORIZATION)
        authorization = dict(funding)
        authorization.update(
            kind=disposition.DISPOSITION_KIND,
            epochId=epoch_id,
            stage=stage,
            dispositionAuthorization=load(proofs["spendAuthorization"]),
        )
    require(authorization.get("kind") in {
        "pp-w-rows-spending-authorization", "pp-w-zero-request-recovery-authorization",
        "pp-w-terminal-semantics-recovery-authorization",
        "pp-w-historical-liability-disposition-v1",
    },
            "structured spending authorization required")
    require(authorization.get("epochId") == epoch_id and authorization.get("stage") == stage,
            "spending authorization is for another epoch or stage")
    ceiling = authorization.get("spendingCeilingUsd")
    require((type(ceiling) is int and ceiling > 0)
            or (type(ceiling) is float and math.isfinite(ceiling) and ceiling > 0),
            "spending authorization requires an explicit positive finite ceiling")
    require(authorization.get("nullResultAccepted") is True,
            "separate null-result acceptance is required")
    for name in ("approvedBy", "approvalReference"):
        require(isinstance(authorization.get(name), str) and authorization[name].strip(),
                "spending authorization requires %s" % name)
    require(confirm_paid, "paid collection requires --confirm-paid-epoch and written authorization")
    return authorization


def run_epoch(registration_path, tasks_root, compiler_root, epochs_root, epoch_id, stage,
              confirm_paid=False):
    """Collection driver. There is deliberately no compiler-per-arm argument."""
    validate_collection_environment()
    identifier(epoch_id)
    registration_path = Path(registration_path).resolve()
    registration = load(registration_path)
    operational_profile = registration.get("kind") == "pp-w-request-gateway-execution-profile"
    if operational_profile:
        registration = helper("ppw-gateway-registration.py").resolve_collection_profile(
            registration_path)
    selected = validate_registration(registration, stage, epoch_id)
    validate_tasks(tasks_root, registration)
    authorization = validate_collection_authorization(
        registration, selected, registration_path.parent, epoch_id, stage, confirm_paid)
    require(selected.get("modelPin") and selected.get("agentVersion"),
            "model/agent pin must be registered before collection")
    spending = helper("ppw-spending.py")
    admission = spending.admit(
        registration, selected, authorization, registration_path.parent, epoch_id, stage)
    gateway_mode = admission.get("mechanism") == spending.GATEWAY
    gateway_module = helper("ppw-budget-gateway.py") if gateway_mode else None
    isolation = helper("ppw-gateway-client.py") if gateway_mode else None
    inspection = helper("ppw-source-inspection.py") if gateway_mode else None
    validate_harness_checkout(admission)
    require(os.environ.get("CLAUDE_MODEL") == selected["modelPin"], "CLAUDE_MODEL differs from registration")
    harness_hashes = admission["harnessArtifacts"]
    harness_files = tuple(harness_hashes)
    require(spending.artifact_manifest(admission.get("mechanism", spending.CONTROL)) == harness_hashes,
            "registered harness changed after admission")
    repository_read_denials = []
    if gateway_mode:
        repository_read_denials = isolation.discover_sensitive_roots(
            (REPO, Path(compiler_root).resolve()))
    client_command = admission["clientExecutable"] if gateway_mode else "claude"
    require(command([client_command, "--version"],
                    env=dict(os.environ, DISABLE_AUTOUPDATER="1")) == selected["agentVersion"],
            "agent version differs from registration")
    shared = product(compiler_root, registration["compilerCommit"])
    runtime = Path(compiler_root) / "src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll"
    if gateway_mode:
        require(runtime.is_file() and digest(runtime) == admission["runtimeSha256"],
                "gateway requires the frozen prebuilt runtime, never a rebuilt product")
        if "disposition" in admission:
            inspection_runtime = admission["sourceInspector"]
            inspection.validate_runtime(inspection_runtime, shared["calorDll"])
        else:
            inspection_runtime = inspection.prepare(shared["calorDll"])
        require(inspection_runtime == admission["sourceInspector"],
                "prospective source-inspector rebuild differs from the registered runtime")
        for certificate in registration["sourceInspections"].values():
            require(certificate.get("inspectorSha256")
                    == inspection_runtime["files"]["ppw-source-inspector.dll"]
                    and certificate.get("inspectorRuntimeSha256")
                    == inspection_runtime["runtimeSha256"],
                    "gateway source-inspection certificate differs from the registered runtime")
        helper("ppw-test-host.py").validate_runtime(admission["testHost"])
    epoch = local(epochs_root, epoch_id)
    require(not epoch.exists(), "epoch already exists; use a reviewed new epoch id, never overwrite")
    # Canaries run outside the epoch and before any agent invocation.
    work = REPO / ".ppw-instrument-work" / epoch_id
    work.mkdir(parents=True, exist_ok=False)
    ledger = None
    owner = None
    try:
        env = dict(os.environ, TMPDIR=str(work))
        hashes = []
        for definition in ARMS.values():
            argv = pair_command(Path(tasks_root) / registration["tasks"][0], definition,
                                shared, work, 0) + ["--canary-only"]
            if gateway_mode:
                argv += ["--ppw-gateway-client", admission["clientExecutable"]]
            evidence = json.loads(command(argv, env=env))
            require(evidence.get("armCanary") ==
                    ("permissive-ok" if definition["policy"] == "permissive" else "strict-ok"),
                    "preflight policy canary failed")
            hashes.append(evidence.get("compilerHash"))
        require(hashes[0] and hashes[0] == hashes[1], "canaries used different compilers")
        shared["compilerHash"] = hashes[0]
        require(product(compiler_root, registration["compilerCommit"]) ==
                {k: v for k, v in shared.items() if k != "compilerHash"},
                "compiler product changed during preflight")
        if gateway_mode:
            ledger = gateway_module.budget.RequestLedger(admission["ledgerPath"])
            ledger.initialize({
                "stage": stage, "epochId": epoch_id, "priceSha256": admission["priceSha256"],
                "authorizationSha256": admission["authorizationSha256"],
                "protocolSha256": admission["protocolSha256"], "planSha256": admission["planSha256"],
                "harnessArtifacts": harness_hashes,
                "plannedSlots": [
                    slot["id"] for slot in admission.get("plannedSlots", admission["slots"])
                ],
            }, admission["ceilingUnits"])
        else:
            ledger = spending.Ledger(admission["ledgerPath"])
            ledger.initialize(admission)
        if gateway_mode and "disposition" in admission:
            owner = ledger.start(expected_disposition_proof=load(local(
                registration_path.parent, selected["dispositionProof"]["path"])))
        else:
            owner = ledger.start()
        epoch.mkdir(parents=True)
        if operational_profile:
            write_new(epoch / "registration.json", registration)
        else:
            shutil.copy2(registration_path, epoch / "registration.json")
        shutil.copytree(tasks_root, epoch / "tasks")
        write_new(epoch / "spending-initial.json", ledger.snapshot())
        shutil.copy2(local(registration_path.parent, selected["spendAuthorization"]["path"]),
                     epoch / "spending-authorization.json")
        shutil.copy2(local(registration_path.parent, selected["spendingPlan"]["path"]),
                     epoch / "spending-plan.json")
        if gateway_mode:
            proofs = [selected[name] for name in
                      ("spendAuthorization", "stageRegistration", "modelRegistration",
                       "instrumentAmendment", "spendingPlan")]
            if "executionProfile" in selected:
                proofs.append(selected["executionProfile"])
                proofs.append(selected["sourceInspectionEvidence"])
            if "recoveryEvidence" in selected:
                proofs.extend(selected[name] for name in (
                    "recoveryEvidence", "recoveryAuthorization", "inspectionProof",
                    "failedArchiveInventory", "failedOperationalSnapshot",
                ))
            if "dispositionEvidence" in selected:
                disposition_registration = helper("ppw-gateway-disposition-registration.py")
                evidence, documents = disposition_registration._load_evidence()
                packet_proofs, reviewed_sources = disposition_registration.archival_references(
                    evidence, documents)
                proofs.extend(packet_proofs)
                for name, sha in reviewed_sources.items():
                    source = local(BENCH, name)
                    destination = local(epoch / "admission/disposition-sources", name)
                    require(digest(source) == sha, "reviewed source changed before archival")
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(source, destination)
                    require(digest(destination) == sha, "reviewed source changed during archival")
                proofs.extend(selected[name] for name in (
                    "dispositionEvidence", "dispositionAuthorization", "dispositionProof",
                    "originalArchiveInventory", "failedArchiveInventory", "stoppedSnapshot",
                    "priceContract", "financialBoundReview", "methodsReview",
                    "transportEvidence", "nativeStartupEvidence",
                ))
            proofs.extend(admission["forecastEvidence"].values())
            copied_proofs = {}
            for proof in proofs:
                destination = local(epoch, proof["path"])
                if destination in copied_proofs:
                    require(copied_proofs[destination] == proof["sha256"]
                            and digest(destination) == proof["sha256"],
                            "operational proof aliases disagree")
                    continue
                require(not destination.exists(), "operational proof would overwrite an epoch artifact")
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(local(registration_path.parent, proof["path"]), destination)
                require(digest(destination) == proof["sha256"],
                        "operational proof changed during archival")
                copied_proofs[destination] = proof["sha256"]
        pins = {"schemaVersion": 2, "kind": KIND, "epochId": epoch_id, "stage": stage,
                "dataKind": "empirical", "mode": "live", "lifecycle": "collecting",
                "harnessCommit": command(["git", "-C", str(REPO), "rev-parse", "HEAD"]),
                "modelPin": selected["modelPin"], "agentVersion": selected["agentVersion"],
                "runsPerArm": selected["runsPerArm"], "suite": registration["tasks"],
                "compiler": shared, "arms": ARMS, "registrationSha256": digest(epoch / "registration.json")}
        pins["harnessArtifacts"] = harness_hashes
        validate_pins(pins, registration, stage, epoch_id)
        write_new(epoch / "pins.json", pins)
        if gateway_mode and "recovery" in admission:
            preserve_recovered_attempt(
                epoch, pins, admission,
                load(local(epoch, selected["failedArchiveInventory"]["path"])),
                load(local(epoch, selected["inspectionProof"]["path"])))
        if gateway_mode and "disposition" in admission:
            preserve_disposition_attempts(epoch, pins, admission)
        for run in range(1, selected["runsPerArm"] + 1):
            for task in registration["tasks"]:
                for definition in ARMS.values():
                    require(current_harness_artifacts(harness_files) == harness_hashes,
                            "harness changed during collection")
                    require(product(compiler_root, registration["compilerCommit"]) ==
                            {k: v for k, v in shared.items() if k != "compilerHash"},
                            "shared compiler drift before run")
                    slot = "%s/%s/%d" % (task, definition["label"], run)
                    preserved = (
                        admission.get("disposition", admission.get("recovery", {}))
                        .get("preservedAttemptedSlots", ())
                    )
                    if slot in preserved:
                        continue
                    run_directory = epoch / "runs" / task / definition["label"] / ("run-%d" % run)
                    if gateway_mode:
                        require(digest(runtime) == admission["runtimeSha256"], "runtime drift before run")
                        run_directory.mkdir(parents=True)
                        protected = Path(admission["ledgerPath"]).parent
                        context_path = protected / ("invocation-" + secrets.token_hex(16) + ".json")
                        try:
                            pair = load(epoch / "tasks" / task / "pair.json")
                            original_task = Path(tasks_root).resolve() / task
                            hidden_test = next((original_task / "tests").glob("*.cs"))
                            seeded_paths = [value for group in pair.get("seeded", {}).values()
                                            if isinstance(group, dict) for value in group.values()
                                            if isinstance(value, str)]
                            require(seeded_paths, "a seeded solution is required for isolation probing")
                            seeded_root = original_task / seeded_paths[0]
                            seeded_file = next(path for path in seeded_root.rglob("*") if path.is_file())
                            hidden_roots = [
                                Path(tasks_root).resolve(), epoch,
                                *(Path(path) for path in repository_read_denials),
                            ]
                            hidden_roots = list(dict.fromkeys(path.resolve() for path in hidden_roots))
                            observer_module = helper("ppw-run-observer.py")
                            observer = observer_module.RunObserver(
                                work_root=work, output=run_directory, hidden_roots=hidden_roots,
                                protected_root=protected, arm="calor",
                                fragment_glob="*.calr.inc" if "sourceAssembly" in pair else "*.calr",
                                heldout_count=pair["tests"]["count"], compiler=shared["calorDll"],
                                permissive=definition["policy"] == "permissive",
                                test_runtime=admission["testHost"],
                                execution_runtime=admission["executionRuntime"])
                            hidden_roots = list(observer.model_hidden_roots)
                            with gateway_module.Gateway(ledger, owner, slot, observer=observer) as gateway:
                                descriptor = os.open(context_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                                with os.fdopen(descriptor, "w") as stream:
                                    json.dump({
                                        "kind": spending.GATEWAY, "protectedRoot": str(protected),
                                        "gatewayPid": os.getpid(), "baseUrl": gateway.base_url,
                                        "clientExecutable": admission["clientExecutable"],
                                        "shellExecutable": admission["shellExecutable"],
                                        "shellSha256": admission["shellSha256"],
                                        "testHost": admission["testHost"],
                                        "observerUrl": gateway.observer_url,
                                        "observerControlUrl": gateway.observer_control_url,
                                        "hiddenRoots": [str(path) for path in hidden_roots],
                                        "repositoryReadDenyRoots": repository_read_denials,
                                        "probeHiddenFiles": [str(hidden_test), str(seeded_file)],
                                        "executionRuntime": admission["executionRuntime"],
                                    }, stream)
                                argv = [sys.executable, str(BENCH / "ppw-gateway-client.py"),
                                        "--context", str(context_path), "--workspace", str(work),
                                        "--output", str(epoch), "--"]
                                argv += pair_command(epoch / "tasks" / task, definition, shared,
                                                     epoch / "runs", run - 1)
                                argv += ["--ppw-gateway-client", admission["clientExecutable"]]
                                argv += ["--ppw-source-inspector-runtime",
                                         admission["sourceInspector"]["manifest"]]
                                command(argv, env=env)
                            evidence = isolation.read_isolation_evidence(context_path, work, epoch)
                            client_exit = load(run_directory / "client-invocation.json").get("exitCode")
                            require_gateway_collecting(ledger, gateway_module.budget)
                            terminal_lifecycle = gateway_module.budget.strict_terminal_lifecycle({
                                "harnessArtifacts": harness_hashes,
                            })
                            terminal_sources = None
                            attempt = None
                            if terminal_lifecycle:
                                terminal_sources = {
                                    name: harness_hashes[name]
                                    for name in gateway_module.budget.TERMINAL_SOURCE_FILES
                                }
                                attempt = gateway_module.budget.validate_attempt_start(
                                    run_directory, epoch, slot, terminal_sources)
                            result_path = run_directory / "result.json"
                            result = load(result_path)
                            if (run_directory / "invalid-terminal.json").exists():
                                require(terminal_lifecycle,
                                        "terminal-invalid evidence uses an unregistered source lifecycle")
                                terminal = ledger.complete_invalid_slot(
                                    owner, slot, run_directory, epoch, evidence, terminal_sources)
                                require(result.get("invalid") is True
                                        and result.get("censored") is True
                                        and terminal["clientExitCode"] == client_exit,
                                        "terminal-invalid result or invocation differs")
                            else:
                                require(result.get("invalid") is False,
                                        "invalid result lacks trusted terminal-invalid evidence")
                                source_report = load(run_directory / "source-inspection.json")
                                require(
                                    source_report.get("inspectorSha256")
                                    == admission["sourceInspector"]["files"][
                                        "ppw-source-inspector.dll"]
                                    and source_report.get("inspectorRuntimeSha256")
                                    == admission["sourceInspector"]["runtimeSha256"],
                                    "valid run lacks registered source-inspection evidence")
                                ledger.complete_slot(
                                    owner, slot, evidence, client_exit, attempt=attempt)
                            write_new(run_directory / "gateway-isolation.json", evidence)
                        finally:
                            context_path.unlink(missing_ok=True)
                            isolation.isolation_evidence_path(context_path).unlink(missing_ok=True)
                        require(ledger.snapshot()["state"] == "collecting",
                                "pilot incomplete: " + ledger.snapshot()["state"])
                        require(digest(runtime) == admission["runtimeSha256"], "runtime drift after run")
                        isolation.validate_runtime(admission["executionRuntime"])
                        stamp_run(run_directory / "result.json", pins)
                        continue
                    ticket = ledger.reserve(owner, slot)
                    ticket_path = epoch / "spend-tickets" / task / definition["label"] / ("run-%d.json" % run)
                    ticket_path.parent.mkdir(parents=True, exist_ok=True)
                    write_new(ticket_path, ticket)
                    exit_code = -1
                    try:
                        command(pair_command(epoch / "tasks" / task, definition, shared,
                                             epoch / "runs", run - 1)
                                + ["--ppw-spend-ticket", str(ticket_path)], env=env)
                        exit_code = 0
                    except subprocess.CalledProcessError as error:
                        exit_code = error.returncode
                        raise
                    finally:
                        try:
                            cost_report = load(run_directory / "agent.json")
                        except (ValueError, OSError):
                            cost_report = None
                        try:
                            client_exit = load(run_directory / "client-invocation.json")["exitCode"]
                            require(type(client_exit) is int, "client exit code is missing")
                        except (ValueError, OSError, KeyError, TypeError):
                            client_exit = -1
                        stop_reason = ledger.settle(
                            owner, ticket, cost_report, client_exit if exit_code == 0 else exit_code)
                    require(stop_reason is None, "pilot incomplete: " + str(stop_reason))
                    result_path = run_directory / "result.json"
                    stamp_run(result_path, pins)
        require(product(compiler_root, registration["compilerCommit"]) ==
                {k: v for k, v in shared.items() if k != "compilerHash"}, "compiler drift after collection")
        if gateway_mode and "recovery" in admission:
            recovery = admission["recovery"]
            recovery_module = helper("ppw-gateway-recovery.py")
            failed_snapshot = load(local(
                epoch, selected["failedOperationalSnapshot"]["path"]))
            recovery_module.complete_recovered_scope(
                admission["ledgerPath"], owner, failed_snapshot,
                expected_old_binding=recovery["oldBinding"],
                target_binding=recovery["targetBinding"],
                expected_ledger_sha256=recovery["failedLedgerSha256"],
                expected_archive_inventory_sha256=recovery["failedArchiveInventorySha256"],
                recovery_registration_sha256=recovery["recoveryRegistrationSha256"],
                preserved_attempted_slots=recovery["preservedAttemptedSlots"])
        else:
            ledger.complete(owner)
        write_new(epoch / "spending-final.json", ledger.snapshot())
        pins["lifecycle"] = "collected"
        (epoch / "pins.json").write_text(json.dumps(pins, indent=2) + "\n")
        record_stage(epochs_root, epoch_id, stage)
    except BaseException:
        if ledger is not None and owner is not None:
            ledger.stop(owner, "INCOMPLETE_INTERRUPTED" if gateway_mode
                        else "interrupted-or-incomplete-collection")
            if epoch.is_dir() and not (epoch / "collection-outcome.json").exists():
                write_new(epoch / "collection-outcome.json", {
                    "kind": "pp-w-incomplete-collection", "epochId": epoch_id, "stage": stage,
                    "complete": False, "verdict": None,
                    "reason": "Collection did not complete the unchanged registered inventory; "
                              "this is not a null, negative, positive, or underpowered result.",
                    "spending": ledger.snapshot(),
                })
        raise
    finally:
        shutil.rmtree(work)


def pair_command(task, arm, compiler, output, offset):
    return [str(BENCH / "run-pair.sh"), "--pair", str(task), "--arm", "calor",
            "--arm-config", arm["label"], "--arm-label", arm["label"],
            "--arm-repo-root", compiler["repoRoot"], "--calor-dll", compiler["calorDll"],
            "--edit-mechanism", "raw", "--runs", "1", "--run-offset", str(offset),
            "--out", str(output)]


def validate_collection_environment():
    overrides = sorted(name for name, value in os.environ.items()
                       if value and name.startswith(("CALOR_P0_", "CALOR_LOOP_")))
    require(not overrides, "unregistered harness environment overrides forbidden: %s" % overrides)


def stamp_run(result_path, pins):
    result = load(result_path)
    result.update(epochId=pins["epochId"], stage=pins["stage"], dataKind=pins["dataKind"],
                  compilerCommit=pins["compiler"]["commit"])
    Path(result_path).write_text(json.dumps(result, indent=2) + "\n")


class SingleOption(argparse.Action):
    def __call__(self, parser, namespace, values, option_string=None):
        if getattr(namespace, self.dest, None) is not None:
            parser.error("%s must occur exactly once" % option_string)
        setattr(namespace, self.dest, values)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("operation", choices=("run", "analyze"))
    parser.add_argument("--epoch-id", required=True, action=SingleOption)
    parser.add_argument("--stage", choices=("pilot", "confirmatory"), required=True, action=SingleOption)
    parser.add_argument("--epochs-root", default=str(BENCH / "epochs"))
    parser.add_argument("--registration")
    parser.add_argument("--tasks-root")
    parser.add_argument("--compiler-root")
    parser.add_argument("--confirm-paid-epoch", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.operation == "analyze":
            require(not any((args.registration, args.tasks_root, args.compiler_root, args.confirm_paid_epoch)),
                    "analysis accepts one epoch only, not collection inputs")
            print(record_stage(args.epochs_root, args.epoch_id, args.stage))
        else:
            require(args.registration and args.tasks_root and args.compiler_root,
                    "--registration, --tasks-root and --compiler-root are required")
            run_epoch(args.registration, args.tasks_root, args.compiler_root, args.epochs_root,
                      args.epoch_id, args.stage, args.confirm_paid_epoch)
    except (ValueError, OSError, KeyError, TypeError, subprocess.CalledProcessError) as exc:
        parser.exit(2, "ERROR: %s\n" % exc)
    return 0


if __name__ == "__main__":
    sys.exit(main())
