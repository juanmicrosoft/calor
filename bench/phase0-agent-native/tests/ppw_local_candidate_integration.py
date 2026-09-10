#!/usr/bin/env python3
"""Opt-in real-product integration with scripted controls, NEVER an agent study.

Uses an owned, clean v0.18 Release checkout beneath .instrument-validation.
No model API client is invoked. The three slots are starter/honest/laundering test
coverage, NOT a proposed sample size. Registration-shaped files are generated
only as explicitly synthetic test fixtures, never as actual registration pins.
"""
import argparse
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys

from ppw_redesign_epoch import BENCH, build, instrument, save

REPO = BENCH.parent.parent
ROLES = ("starter", "honest", "laundering")
INTEGRITY_ROLES = ROLES + ("effectful-wrong-value", "effectful-throwing",
                          "comment-marker", "literal-marker", "whitespace", "changed-contract")
RELEASE = "514f538024df990af86054af25975b756ba42ab1"


def prescribed_source(task, pair, role, arm):
    _, honest = instrument.helper("harness-capture.py").honest_reference_cells(pair)
    if role in ("starter", "honest", "laundering", "effectful-wrong-value", "effectful-throwing"):
        relative = ("starter-" + arm if role == "starter" else
                    honest[arm] if role == "honest" else pair["seeded"][role][arm])
        return (task / relative / "task.calr.inc").read_bytes()
    text = (task / honest[arm] / "task.calr.inc").read_text()
    if role == "comment-marker":
        text = text.replace("      §R", "      // §C{this.lookup}\n      §R", 1)
    elif role == "literal-marker":
        text = text.replace("      §R",
                            '      §B{marker:str} "§C{this.lookup}"\n'
                            '      §IF{literalCheck} (== marker "")\n        §R 0\n      §R', 1)
    elif role == "whitespace":
        text = prescribed_source(task, pair, "laundering", arm).decode("utf-8").replace(
            "§C{this.lookup}", "§C{ this.lookup }")
    elif role == "changed-contract":
        text = text.replace("§E{}", "§E{mut}").replace("§C{Calculate}", "§C{Lookup}")
    else:
        raise ValueError("Unknown prescribed engineering control")
    return text.encode("utf-8")


def stand_in():
    if "--version" in sys.argv:
        print("SYNTHETIC-LOCAL-NO-AGENT")
        return
    if "--help" in sys.argv:
        print("--output-format stream-json --verbose --include-partial-messages --forward-subagent-text")
        return
    if os.environ.get("PPW_ENGINEERING_ONLY") != "1":
        raise ValueError("The deterministic stand-in requires explicit engineering mode")
    task = Path(os.environ["PPW_ENGINEERING_TASK"])
    counter = Path(os.environ["PPW_ENGINEERING_COUNTER"])
    arm = os.environ["PPW_ENGINEERING_ARM"]
    roles = tuple(json.loads(os.environ.get("PPW_ENGINEERING_ROLES", json.dumps(ROLES))))
    if roles not in (ROLES, INTEGRITY_ROLES):
        raise ValueError("Unrecognized engineering control inventory")
    ordinal = int(counter.read_text()) if counter.exists() else 0
    if ordinal >= len(roles):
        raise ValueError("Unexpected replacement attempt")
    role = roles[ordinal]
    counter.write_text(str(ordinal + 1))
    pair = json.loads((task / "pair.json").read_text())
    (Path.cwd() / "task.calr.inc").write_bytes(prescribed_source(task, pair, role, arm))

    def emit(value):
        print(json.dumps(value), flush=True)

    def observe(label, args):
        identity = "synthetic-" + label
        emit({"type": "assistant", "message": {"id": identity, "content": [
            {"type": "tool_use", "id": identity, "name": "Bash", "input": {"command": shlex.join(args)}}
        ]}})
        result = subprocess.run(args, text=True, capture_output=True, timeout=180)
        emit({"type": "user", "message": {"content": [{
            "type": "tool_result", "tool_use_id": identity,
            "content": result.stdout + result.stderr, "is_error": result.returncode != 0
        }]}})
        return result.returncode

    if observe("build", ["dotnet", "build", "--nologo", "-v", "q"]) == 0:
        observe("smoke", ["dotnet", "test", "../smoke/Smoke.csproj", "--nologo", "-v", "q"])
    emit({"type": "assistant", "message": {"id": "synthetic-done", "content": [
        {"type": "text", "text": "SYNTHETIC LOCAL test finished; not an agent observation."}
    ]}})
    emit({"type": "result", "result": "SYNTHETIC LOCAL test finished",
          "usage": {"output_tokens": 0}, "modelUsage": {"SYNTHETIC-LOCAL": {"outputTokens": 0}},
          "total_cost_usd": 0})


def inventory(root):
    return {p.relative_to(root).as_posix(): instrument.digest(p)
            for p in root.rglob("*") if p.is_file()}


def run(tasks_root, product_root, output, roles=ROLES):
    scratch = REPO / ".instrument-validation"
    for path in (product_root, output):
        if not path.resolve().is_relative_to(scratch.resolve()) or path.is_symlink():
            raise ValueError("Product and output must be owned paths beneath .instrument-validation")
    if output.exists():
        raise ValueError("Use a new output directory; never replace an earlier attempt")
    output.mkdir(parents=True)
    instrument.isolated_tree(tasks_root)
    source_inventory = inventory(tasks_root)
    compiler = instrument.product(product_root, RELEASE)
    tasks = sorted(p.parent for p in tasks_root.glob("*/pair.json"))
    if not tasks:
        raise ValueError("No candidate manifests found")
    if roles == INTEGRITY_ROLES and [task.name for task in tasks] != ["C-001-quota-adapter"]:
        raise ValueError("The additional prescribed integrity transformations target quota-adapter only")
    epoch_id = "synthetic-integrity-controls" if roles == INTEGRITY_ROLES else "synthetic-local-candidates"
    fake = output / "stand-in" / "claude"
    fake.parent.mkdir()
    fake.write_text("#!/usr/bin/env bash\nexec " + shlex.join(
        [sys.executable, str(Path(__file__).resolve()), "--stand-in"]) + ' "$@"\n')
    fake.chmod(0o755)
    env = dict(os.environ, TMPDIR=str(output),
               PATH=str(fake.parent) + os.pathsep + os.environ["PATH"],
               PPW_ENGINEERING_ONLY="1", CLAUDE_MODEL="SYNTHETIC-LOCAL",
               PPW_ENGINEERING_ROLES=json.dumps(roles))
    for name in list(env):
        if name.startswith(("CALOR_P0_", "CALOR_LOOP_")):
            del env[name]
    if Path(shutil.which("claude", path=env["PATH"])).resolve() != fake.resolve():
        raise ValueError("Stand-in executable resolution failed")
    capture = instrument.helper("harness-capture.py")
    assembly = instrument.helper("ppw-source-assembly.py")
    raw = output / "scripted-runs"
    observations = []
    for task in tasks:
        pair = json.loads((task / "pair.json").read_text())
        if pair.get("compilerCommit") != RELEASE:
            raise ValueError("Candidate compiler differs from the shared release")
        assembly.definition(pair)
        _, honest = capture.honest_reference_cells(pair)
        for arm, definition in instrument.ARMS.items():
            config = capture.resolve_pair_config(str(task / "pair.json"), definition["label"], "calor")
            if not config["admitted"] or not config["reference"]:
                raise ValueError("Candidate policy/reference is incompatible")
            env.update(PPW_ENGINEERING_TASK=str(task),
                       PPW_ENGINEERING_COUNTER=str(output / (task.name + "-" + arm + ".counter")),
                       PPW_ENGINEERING_ARM=arm.lower())
            argv = instrument.pair_command(task, definition, compiler, raw, 0)
            argv[argv.index("--runs") + 1] = str(len(roles))
            process = subprocess.run(["bash"] + argv, env=env, capture_output=True, text=True, timeout=1200)
            (output / (task.name + "-" + arm + ".log")).write_text(process.stdout + process.stderr)
            if process.returncode:
                raise ValueError("Actual runner failed: %s/%s (see log)" % (task.name, arm))
            for index, role in enumerate(roles, 1):
                directory = raw / task.name / definition["label"] / ("run-%d" % index)
                result = json.loads((directory / "result.json").read_text())
                shape_roles = {"laundering", "effectful-wrong-value", "effectful-throwing", "whitespace"}
                expected_build = not (role in shape_roles and arm == "B")
                if result["invalid"] or result["finalBuild"]["ok"] != expected_build:
                    raise ValueError("Unexpected validity/build: %s/%s/%s" % (task.name, arm, role))
                if result["tokenUsage"]["output_tokens_corrected"] != 0:
                    raise ValueError("Unexpected non-synthetic token usage")
                for part in assembly.definition(pair)["parts"]:
                    expected_source = (prescribed_source(task, pair, role, arm.lower())
                                       if part == "task.calr.inc" else
                                       (task / ("starter-" + arm.lower()) / part).read_bytes())
                    if (directory / "final-src" / part).read_bytes() != expected_source:
                        raise ValueError("Captured source differs from the prescribed test control")
                journal = [json.loads(line) for line in (directory / "journal.jsonl").read_text().splitlines()]
                if not journal or not all(entry["envelope_valid"] for entry in journal):
                    raise ValueError("Missing valid diagnostic capture")
                diagnostics = {d["code"] for entry in journal for d in entry["diagnostics"]}
                if expected_build and diagnostics:
                    raise ValueError("Unexpected diagnostics in a successful control")
                if not expected_build and "Calor0410" not in diagnostics:
                    raise ValueError("Strict laundering did not capture its actual diagnostic")
                observations.append({"task": task.name, "arm": arm, "control": role,
                                     "built": expected_build, "compilerHash": result["compilerHash"],
                                     "diagnosticCodes": sorted(diagnostics)})
            if instrument.product(product_root, RELEASE) != compiler:
                raise ValueError("Shared product changed during local validation")
            print("Completed scripted controls: %s/%s" % (task.name, arm), flush=True)
    hashes = {item["compilerHash"] for item in observations}
    if len(hashes) != 1 or not next(iter(hashes)):
        raise ValueError("Mixed or missing compiler provenance")
    compiler["compilerHash"] = next(iter(hashes))

    # Unit-test-only adapter: identifiers and records cannot be mistaken for a
    # frozen task set. Original candidate bytes, limits and classifications stay unchanged.
    fixtures = output / "synthetic-test-fixtures"
    epoch = build(fixtures, epoch_id=epoch_id)
    shutil.rmtree(epoch / "tasks")
    shutil.rmtree(epoch / "runs")
    registration = json.loads((epoch / "registration.json").read_text())
    registration.update(id="SYNTHETIC-LOCAL-NOT-A-REGISTRATION", compilerCommit=RELEASE)
    registration["stages"]["pilot"]["runsPerArm"] = len(roles)
    registration["sourceInspections"] = {}
    identifiers = []
    blobs = []
    blob_helper = instrument.helper("ppw-registration.py")
    for task in tasks:
        identifier = "SYNTHETIC-" + task.name
        identifiers.append(identifier)
        destination = epoch / "tasks" / identifier
        shutil.copytree(task, destination)
        pair = json.loads((destination / "pair.json").read_text())
        pair.update(id=identifier, authoringStatus="synthetic-test-fixture", **{"class": "blind", "legB": True})
        save(destination / "pair.json", pair)
        inspection = instrument.helper("ppw-source-inspection.py")
        registration["sourceInspections"][identifier] = inspection.inspect(
            pair, inspection.control_directories(pair, destination), compiler["calorDll"])
        for arm, definition in instrument.ARMS.items():
            for path in sorted((destination / ("starter-" + arm.lower())).glob("*.calr.inc")):
                blobs.append({"task": identifier, "arm": arm,
                              "path": path.relative_to(epoch / "tasks").as_posix(),
                              "blobSha": blob_helper.blob_sha(path)})
            for index in range(1, len(roles) + 1):
                target = epoch / "runs" / identifier / definition["label"] / ("run-%d" % index)
                shutil.copytree(raw / task.name / definition["label"] / ("run-%d" % index), target)
                record = json.loads((target / "result.json").read_text())
                record.update(pair=identifier, epochId=epoch_id,
                              stage="pilot", dataKind="synthetic", compilerCommit=RELEASE)
                save(target / "result.json", record)
    registration["tasks"] = identifiers
    registration["artifacts"] = inventory(epoch / "tasks")
    registration["replacementPins"] = {
        "compilerCommit": RELEASE, "policies": {"A": ["--permissive-effects"], "B": []},
        "pairCounts": {"tasks": len(tasks), "blind": len(tasks), "warningVsError": 0, "legB": len(tasks)},
        "starterBlobs": blobs,
    }
    save(epoch / "registration.json", registration)
    pins = json.loads((epoch / "pins.json").read_text())
    pins.update(compiler=compiler, runsPerArm=len(roles), suite=identifiers,
                modelPin="SYNTHETIC-LOCAL", agentVersion="SYNTHETIC-LOCAL-NO-AGENT",
                registrationSha256=instrument.digest(epoch / "registration.json"))
    save(epoch / "pins.json", pins)
    ledger = json.loads(instrument.record_stage(fixtures, epoch_id, "pilot").read_text())
    if ledger["empirical"] or ledger["verdict"] is not None:
        raise ValueError("Synthetic checks were promoted to empirical conclusions")
    for cell in ledger["perCell"]:
        positive = len(set(roles) & {"laundering", "effectful-wrong-value", "effectful-throwing", "whitespace"})
        expected = {"validRuns": len(roles), "invalidRuns": 0, "shapeRealized": positive,
                    "escapes": positive if cell["arm"] == "A" else 0,
                    "didNotBuildAtDeclaredDone": positive if cell["arm"] == "B" else 0}
        if any(cell[key] != value for key, value in expected.items()) or cell["unscorableHeldoutRuns"]:
            raise ValueError("Synthetic stage accounting failed: " + str(cell))
        if not cell["namedTestFailuresWithoutEffect"]:
            raise ValueError("Numeric-only starter failure was not disclosed separately")
        if cell["unscorablePublicApiRuns"] or cell["unscorableShapeRuns"]:
            raise ValueError("Native inspection failed for a prescribed control")
        expected_changes = [roles.index("changed-contract") + 1] if "changed-contract" in roles else []
        if cell["changedPublicApiRuns"] != expected_changes:
            raise ValueError("Changed public contract was not disclosed separately")
    if inventory(tasks_root) != source_inventory:
        raise ValueError("Original authoring bytes changed")
    save(output / "engineering-report.json", {
        "engineeringOnly": True, "notARegistration": True, "paidAgentCalls": 0,
        "controlSlotsNotSampleSize": list(roles), "originalCandidateBytesUnchanged": True,
        "runnerDefaultsAreNotRegisteredMeasurementLimits": True,
        "compiler": compiler, "scriptedObservations": observations,
        "syntheticStageLedger": str((epoch / "ppw-stage-ledger.json").relative_to(output)),
        "sourceInputSha256": source_inventory,
    })
    print("PASS: real runner/compiler/visible+held-out suites/capture -> synthetic stage ledger; no paid calls.")


def main():
    if "--stand-in" in sys.argv:
        stand_in()
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tasks-root", type=Path, default=BENCH / "task-candidates/1256")
    parser.add_argument("--product-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--integrity-controls", action="store_true")
    args = parser.parse_args()
    run(args.tasks_root.resolve(), args.product_root.resolve(), args.output.resolve(),
        INTEGRITY_ROLES if args.integrity_controls else ROLES)


if __name__ == "__main__":
    main()
