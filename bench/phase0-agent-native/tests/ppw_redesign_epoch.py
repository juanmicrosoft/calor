"""Deterministic SYNTHETIC data only; no agents, real registration, or epochs.

All files are materialized inside the caller's project-local scratch directory.
Neither the arbitrary test counts nor the fabricated hashes are scientific pins.
"""
import importlib.util
import json
from pathlib import Path

BENCH = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("ppw_instrument", BENCH / "ppw-instrument.py")
instrument = importlib.util.module_from_spec(spec)
spec.loader.exec_module(instrument)


def save(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def build(root, stage="pilot", epoch_id=None):
    epoch_id = epoch_id or "synthetic-" + stage
    epoch = Path(root) / epoch_id
    task = "SYNTHETIC-task"
    directory = epoch / "tasks" / task
    directory.mkdir(parents=True)
    strict = {"enforceEffects": True, "permissiveEffects": False,
              "contractMode": "debug", "z3Required": True}
    pair = {
        "id": task, "class": "blind", "legB": True, "arms": {
            "calor-permissive": {"armId": "a", "fixture": "starter-a",
                                "config": dict(strict, permissiveEffects=True, controlArmKind="permissive")},
            "calor-strict": {"armId": "b", "fixture": "starter-b", "config": strict}},
        "tests": {"path": "tests", "count": 1, "effectObservingTests": ["PreservesState"],
                  "effectFailureSignature": "HELDOUT_EFFECT:state-change"},
        "shapeRealizedIndicator": {"sourceRegex": "this.lookup"},
        "seeded": {"clean": {"a": "seeded/clean-a", "b": "seeded/clean-b"}},
    }
    save(directory / "pair.json", pair)
    (directory / "spec.md").write_text("SYNTHETIC TEST INPUT — not an empirical task.\n")
    (directory / "tests" / "shims").mkdir(parents=True)
    (directory / "tests" / "shims" / "TestShim.calor.cs").write_text("// synthetic shim\n")
    (directory / "tests" / "Tests.cs").write_text(
        "// synthetic infrastructure test, not an effect measurement\n"
        "using Xunit;\npublic class Tests {\n[Fact]\npublic void PreservesState() { Assert.True(true); }\n}\n")
    for arm in ("a", "b"):
        for subdir, source in (("starter-" + arm, "return 0"),
                               ("seeded/clean-" + arm, "this.lookup")):
            source_dir = directory / subdir
            source_dir.mkdir(parents=True)
            (source_dir / "Source.calr").write_text(source + "\n")
    artifacts = {str(p.relative_to(epoch / "tasks")): instrument.digest(p)
                 for p in (epoch / "tasks").rglob("*") if p.is_file()}
    registration = {
        "schemaVersion": 2, "id": "SYNTHETIC-NOT-A-REGISTRATION", "status": "frozen",
        "supersedes": "A-1.12", "cause": "Synthetic test of explicit supersession only",
        "reviews": ["synthetic-review-fixture-not-an-approval"], "compilerCommit": "a" * 40,
        "stages": {"pilot": {"epochId": "synthetic-pilot", "runsPerArm": 2},
                   "confirmatory": {"epochId": "synthetic-confirmatory", "runsPerArm": 2}},
        "tasks": [task], "artifacts": artifacts,
    }
    registration["stages"][stage]["epochId"] = epoch_id
    pins_helper = instrument.helper("ppw-registration.py")
    registration["supersededPins"] = pins_helper.historical_pins()
    registration["replacementPins"] = {
        "compilerCommit": registration["compilerCommit"],
        "policies": {"A": ["--permissive-effects"], "B": []},
        "pairCounts": {"tasks": 1, "blind": 1, "warningVsError": 0, "legB": 1},
        "starterBlobs": [
            {"task": task, "arm": arm, "path": task + "/starter-" + arm.lower() + "/Source.calr",
             "blobSha": pins_helper.blob_sha(directory / ("starter-" + arm.lower()) / "Source.calr")}
            for arm in ("A", "B")
        ],
    }
    save(epoch / "registration.json", registration)
    compiler = {"commit": "a" * 40, "release": "v0.18.0",
                "repoRoot": "/synthetic/product", "calorDll": "/synthetic/product/calor.dll",
                "calorSha256": "b" * 64, "calorTasksSha256": "c" * 64, "compilerHash": "d" * 64}
    pins = {"schemaVersion": 2, "kind": instrument.KIND, "epochId": epoch_id, "stage": stage,
            "mode": "live", "dataKind": "synthetic", "lifecycle": "collected",
            "compiler": compiler, "arms": instrument.ARMS, "harnessCommit": "e" * 40,
            "modelPin": "SYNTHETIC", "agentVersion": "SYNTHETIC",
            "runsPerArm": 2, "suite": [task],
            "registrationSha256": instrument.digest(epoch / "registration.json")}
    save(epoch / "pins.json", pins)
    capture = instrument.helper("harness-capture.py")
    for arm, definition in instrument.ARMS.items():
        for run in (1, 2):
            path = epoch / "runs" / task / definition["label"] / ("run-%d" % run)
            path.mkdir(parents=True)
            transcript = path / "transcript.jsonl"
            transcript.write_text(json.dumps({"type": "assistant", "message": {"id": "test", "content": []}}) + "\n")
            save(path / "agent.json", {"usage": {"output_tokens": 1},
                                      "modelUsage": {"SYNTHETIC": {"outputTokens": 100}}})
            (path / "final-src").mkdir()
            (path / "final-src" / "Source.calr").write_text("this.lookup\n")
            snapshot = {"policy": {"CalorEnforceEffects": True, "CalorPermissiveEffects": arm == "A"},
                        "configurationSha256": {"src/Src.csproj": ("a" if arm == "A" else "b") * 64}}
            save(path / "policy-before.json", snapshot)
            save(path / "policy-after.json", snapshot)
            (path / ".ho_final.txt").write_text(
                "  Failed Synthetic.Tests.PreservesState [1 ms]\n"
                "  Error Message:\n   HELDOUT_EFFECT:state-change\n" if arm == "A" else
                "  Passed Synthetic.Tests.PreservesState [1 ms]\n")
            result = {
                "epochId": epoch_id, "stage": stage, "dataKind": "synthetic",
                "pair": task, "arm": definition["label"], "run": run,
                "compilerCommit": compiler["commit"], "compilerHash": compiler["compilerHash"],
                "productCompilerHash": compiler["compilerHash"],
                "armRepoRoot": compiler["repoRoot"], "armConfigKey": definition["label"],
                "permissiveEffects": arm == "A", "controlArmKind": "permissive" if arm == "A" else None,
                "editMechanism": "raw", "nullAgent": False, "invalid": False, "censored": False,
                "buildState": {"compilerHash": compiler["compilerHash"], "optionsHash": "policy-" + arm},
                "armCanary": "permissive-ok" if arm == "A" else "strict-ok",
                "finalBuild": {"ok": True}, "heldoutFinal": capture.read_heldout_final(str(path / ".ho_final.txt")),
                "turns": capture.count_turns(str(transcript)),
            }
            save(path / "result.json", result)
    return epoch
