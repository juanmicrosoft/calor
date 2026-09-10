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
import os
from pathlib import Path
import re
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
        regex = re.compile(indicator)
        for arm in ("a", "b"):
            starter = list((directory / ("starter-" + arm)).glob("*.calr"))
            clean = local(directory, pair["seeded"]["clean"][arm])
            clean_sources = list(clean.glob("*.calr"))
            require(starter and clean_sources, "starter and clean seed sources required")
            require(not any(regex.search(p.read_text()) for p in starter),
                    "shape indicator matches its starter")
            require(any(regex.search(p.read_text()) for p in clean_sources),
                    "shape indicator misses its clean seed")
    helper("ppw-registration.py").check_supersession(registration, root)


def analyze(epochs_root, epoch_id, stage):
    """One selected epoch, no sibling enumeration and no pooled input API."""
    identifier(epoch_id)
    epoch = local(epochs_root, epoch_id)
    isolated_tree(epoch)
    pins = load(epoch / "pins.json")
    registration = load(epoch / "registration.json")
    validate_pins(pins, registration, stage, epoch_id)
    require(digest(epoch / "registration.json") == pins.get("registrationSha256"),
            "registration hash differs from pins")
    validate_tasks(epoch / "tasks", registration)
    require(pins["lifecycle"] in ("collected", "archived"),
            "epoch has not completed collection: %s" % pins["lifecycle"])
    require(not [p for p in epoch.rglob("pins.json") if p != epoch / "pins.json"],
            "nested epoch/pooling is forbidden")
    expected = {
        "%s/%s/run-%d/result.json" % (task, definition["label"], run)
        for task in pins["suite"] for definition in ARMS.values()
        for run in range(1, pins["runsPerArm"] + 1)
    }
    actual = {str(path.relative_to(epoch / "runs"))
              for path in (epoch / "runs").rglob("result.json")}
    require(actual == expected, "run inventory differs from registered cells; pooling or missing runs")
    capture = helper("harness-capture.py")
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
                    "unscorableHeldoutRuns": [], "invalidReasons": [], "optionsHashes": []}
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
                sources = list((run_dir / "final-src").rglob("*.calr"))
                require(sources, "missing final sources for shape indicator")
                cell["shapeRealized"] += int(any(regex.search(p.read_text()) for p in sources))
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
                    cell["escapes"] += int(readable and bool(set(observing) & effect))
                    if (set(observing) & failed) - effect:
                        cell["namedTestFailuresWithoutEffect"].append(run)
                usage = token_usage.compute(token_usage.load_envelope(str(run_dir / "agent.json")))
                require(usage["source"] != "missing", "missing agent token envelope")
                cell["outputTokens"].append(usage["output_tokens_corrected"])
            valid = cell["validRuns"]
            cell["escapeRate"] = (round(cell["escapes"] / valid, 4)
                                  if valid and not cell["unscorableHeldoutRuns"] else None)
            cell["shapeRealizedRate"] = round(cell["shapeRealized"] / valid, 4) if valid else None
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


def run_epoch(registration_path, tasks_root, compiler_root, epochs_root, epoch_id, stage,
              confirm_paid=False):
    """Collection driver. There is deliberately no compiler-per-arm argument."""
    validate_collection_environment()
    identifier(epoch_id)
    registration_path = Path(registration_path).resolve()
    registration = load(registration_path)
    selected = validate_registration(registration, stage, epoch_id)
    validate_tasks(tasks_root, registration)
    # A written, reviewed task supersession is not spending or stage-registration approval.
    for name in ("spendAuthorization", "stageRegistration", "modelRegistration"):
        artifact = selected.get(name, {})
        path = local(registration_path.parent, artifact.get("path"))
        require(path.is_file() and digest(path) == artifact.get("sha256"),
                "%s evidence is missing or changed" % name)
    require(confirm_paid, "paid collection requires --confirm-paid-epoch and written authorization")
    require(selected.get("modelPin") and selected.get("agentVersion"),
            "model/agent pin must be registered before collection")
    require(not command(["git", "-C", str(REPO), "status", "--porcelain",
                         "--", str(BENCH)]), "harness checkout is dirty")
    require(os.environ.get("CLAUDE_MODEL") == selected["modelPin"], "CLAUDE_MODEL differs from registration")
    require(command(["claude", "--version"]) == selected["agentVersion"], "agent version differs from registration")
    shared = product(compiler_root, registration["compilerCommit"])
    harness_files = ("run-pair.sh", "harness-capture.py", "ppw-instrument.py", "ppw-registration.py",
                     "token-usage.py", "token-usage.sh", "telemetry-helpers.py", "ppw-pins.schema.json",
                     "templates/calor-arm/CalorArm.csproj.template",
                     "templates/calor-arm/policy-canary.calr.txt")
    harness_hashes = {name: digest(BENCH / name) for name in harness_files}
    epoch = local(epochs_root, epoch_id)
    require(not epoch.exists(), "epoch already exists; use a reviewed new epoch id, never overwrite")
    # Canaries run outside the epoch and before any agent invocation.
    work = REPO / ".ppw-instrument-work" / epoch_id
    work.mkdir(parents=True, exist_ok=False)
    try:
        env = dict(os.environ, TMPDIR=str(work))
        hashes = []
        for definition in ARMS.values():
            argv = pair_command(Path(tasks_root) / registration["tasks"][0], definition,
                                shared, work, 0) + ["--canary-only"]
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
        epoch.mkdir(parents=True)
        shutil.copy2(registration_path, epoch / "registration.json")
        shutil.copytree(tasks_root, epoch / "tasks")
        pins = {"schemaVersion": 2, "kind": KIND, "epochId": epoch_id, "stage": stage,
                "dataKind": "empirical", "mode": "live", "lifecycle": "collecting",
                "harnessCommit": command(["git", "-C", str(REPO), "rev-parse", "HEAD"]),
                "modelPin": selected["modelPin"], "agentVersion": selected["agentVersion"],
                "runsPerArm": selected["runsPerArm"], "suite": registration["tasks"],
                "compiler": shared, "arms": ARMS, "registrationSha256": digest(epoch / "registration.json")}
        pins["harnessArtifacts"] = harness_hashes
        validate_pins(pins, registration, stage, epoch_id)
        write_new(epoch / "pins.json", pins)
        for run in range(1, selected["runsPerArm"] + 1):
            for task in registration["tasks"]:
                for definition in ARMS.values():
                    require({name: digest(BENCH / name) for name in harness_files} == harness_hashes,
                            "harness changed during collection")
                    require(product(compiler_root, registration["compilerCommit"]) ==
                            {k: v for k, v in shared.items() if k != "compilerHash"},
                            "shared compiler drift before run")
                    command(pair_command(epoch / "tasks" / task, definition, shared,
                                         epoch / "runs", run - 1), env=env)
                    result_path = epoch / "runs" / task / definition["label"] / ("run-%d" % run) / "result.json"
                    stamp_run(result_path, pins)
        require(product(compiler_root, registration["compilerCommit"]) ==
                {k: v for k, v in shared.items() if k != "compilerHash"}, "compiler drift after collection")
        pins["lifecycle"] = "collected"
        (epoch / "pins.json").write_text(json.dumps(pins, indent=2) + "\n")
        record_stage(epochs_root, epoch_id, stage)
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
