"""Prescribed SYNTHETIC archive plumbing; never experimental/model observations.

Uses frozen source and cached native control facts without executing a compiler.
All outcome, usage, transcript and build records below are synthetic fixtures.
"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil


BENCH = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("pilot_fixture_analysis", BENCH / "ppw-pilot-adjudicate.py")
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n")


def build(root):
    epoch = Path(root) / "synthetic-pilot"
    shutil.copytree(BENCH / "epochs/w-rows-pilot-001", epoch)
    pins, registration = analysis.load(epoch / "pins.json"), analysis.load(epoch / "registration.json")
    pins.update(epochId=epoch.name, lifecycle="collected", dataKind="synthetic")
    registration["stages"]["pilot"]["epochId"] = epoch.name
    save(epoch / "registration.json", registration)
    pins["registrationSha256"] = hashlib.sha256((epoch / "registration.json").read_bytes()).hexdigest()
    save(epoch / "pins.json", pins)
    instrument = analysis.module("pilot_fixture_instrument", "ppw-instrument.py")
    inspection = instrument.helper("ppw-source-inspection.py")
    capture = instrument.helper("harness-capture.py")
    for task in pins["suite"]:
        directory = epoch / "tasks" / task
        pair = analysis.load(directory / "pair.json")
        controls = registration["sourceInspections"][task]
        for arm, definition in pins["arms"].items():
            lower = arm.lower()
            for run in range(1, pins["runsPerArm"] + 1):
                laundering = run <= pins["runsPerArm"] // 2
                role = "laundering" if laundering else "honest"
                built = not (arm == "B" and laundering)
                run_dir = epoch / "runs" / task / definition["label"] / ("run-%d" % run)
                run_dir.mkdir(parents=True)
                relative = pair["seeded"]["laundering" if laundering else "clean"][lower]
                shutil.copytree(directory / relative, run_dir / "final-src")
                source_report = {key: controls[key] for key in (
                    "schemaVersion", "compilerSha256", "inspectorSha256")}
                source_report["sources"] = {
                    "baseline:0": copy.deepcopy(controls["sources"]["starter-" + lower + ":0"]),
                    "final:0": copy.deepcopy(controls["sources"][role + "-" + lower + ":0"]),
                }
                source_report["inputSha256"] = {
                    "baseline": inspection.inputs(pair, directory / ("starter-" + lower), "baseline")[1],
                    "final": inspection.inputs(pair, run_dir / "final-src", "final")[1],
                }
                save(run_dir / "source-inspection.json", source_report)
                transcript = run_dir / "transcript.jsonl"
                transcript.write_text(json.dumps({
                    "type": "assistant", "synthetic": True,
                    "message": {"id": "synthetic", "content": []},
                }) + "\n")
                save(run_dir / "agent.json", {
                    "synthetic": True, "usage": {"output_tokens": 1},
                    "modelUsage": {pins["modelPin"]: {"outputTokens": 1}},
                })
                snapshot = {"policy": {"CalorEnforceEffects": True, "CalorPermissiveEffects": arm == "A"},
                            "configurationSha256": {"src/Src.csproj": "synthetic-" + arm}}
                save(run_dir / "policy-before.json", snapshot)
                save(run_dir / "policy-after.json", snapshot)
                effect = built and laundering and arm == "A"
                log = []
                for observer in pair["tests"]["effectObservingTests"]:
                    log.append("  %s %s [1 ms]\n" % ("Failed" if effect else "Passed", observer))
                    if effect:
                        log.append("  Error Message:\n   " + pair["tests"]["effectFailureSignature"] + "\n")
                heldout = run_dir / ".ho_final.txt"
                heldout.write_text("".join(log))
                compiler = pins["compiler"]
                save(run_dir / "result.json", {
                    "synthetic": True, "epochId": epoch.name, "stage": "pilot", "dataKind": "synthetic",
                    "pair": task, "arm": definition["label"], "run": run,
                    "compilerCommit": compiler["commit"], "compilerHash": compiler["compilerHash"],
                    "productCompilerHash": compiler["compilerHash"], "armRepoRoot": compiler["repoRoot"],
                    "armConfigKey": definition["label"], "permissiveEffects": arm == "A",
                    "controlArmKind": "permissive" if arm == "A" else None, "editMechanism": "raw",
                    "nullAgent": False, "invalid": False, "censored": False,
                    "buildState": {"compilerHash": compiler["compilerHash"], "optionsHash": "synthetic-" + arm},
                    "armCanary": "permissive-ok" if arm == "A" else "strict-ok",
                    "finalBuild": {"ok": built}, "heldoutFinal": capture.read_heldout_final(str(heldout)),
                    "turns": capture.count_turns(str(transcript)),
                })
    return epoch
