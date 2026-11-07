#!/usr/bin/env python3
"""Opt-in, fail-closed E2E capture; no experiment or baseline selection."""

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import selectors
import shutil
import signal
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parent
TASKS = HERE.parent / "tasks"
REPO = HERE.parents[3]
HELPERS = HERE / "helpers.sh"
spec = importlib.util.spec_from_file_location("e2e_process_supervision", REPO / "scripts/run_flake_gate.py")
supervision = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = supervision
spec.loader.exec_module(supervision)


class EvidenceError(RuntimeError):
    pass


def write_json(path, value):
    # Never follow an artifact symlink or replace an already published file.
    safe_path(path, directory=False)
    with path.open("x", encoding="utf-8") as output:
        json.dump(value, output, indent=2, sort_keys=True)
        output.write("\n")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_json(text):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise EvidenceError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result

    def invalid_constant(value):
        raise EvidenceError(f"Non-JSON numeric constant: {value}")

    return json.loads(text, object_pairs_hook=unique, parse_constant=invalid_constant)


def safe_path(path, directory=True):
    if not path.is_absolute() or ".." in path.parts:
        raise EvidenceError(f"Expected absolute, non-traversing path: {path}")
    for parent in (path, *path.parents):
        if parent.is_symlink():
            raise EvidenceError(f"Symlink path rejected: {parent}")
    if directory and (not path.is_dir() or path == Path("/") or path == Path.home()):
        raise EvidenceError(f"Unsafe or missing input root: {path}")
    return path


def nested(left, right):
    return left == right or left in right.parents or right in left.parents


def inventory(root, exclude_build=False):
    safe_path(root)
    files = {}
    for current, directories, names in os.walk(root, followlinks=False):
        base = Path(current)
        directories[:] = sorted(
            name for name in directories
            if name not in (".git", "__pycache__")
            and not (exclude_build and name in ("bin", "obj"))
        )
        for name in directories + sorted(names):
            path = base / name
            if path.is_symlink():
                raise EvidenceError(f"Symlink input/output rejected: {path}")
            if path.is_file():
                files[str(path.relative_to(root))] = digest(path)
            elif not path.is_dir():
                raise EvidenceError(f"Nonregular file rejected: {path}")
    if not files:
        raise EvidenceError(f"Empty input/image: {root}")
    return files


def bounded_cleanup(tracker):
    # Reuse identity-checked signaling, but cap the number of slow process-table
    # queries by elapsed time rather than draining an arbitrarily large tree.
    deadline = time.monotonic() + 3
    for sig in (signal.SIGTERM, signal.SIGKILL):
        for group in tracker.known_groups:
            if time.monotonic() >= deadline:
                return False
            supervision.safe_signal_group(tracker, group, sig)
        for identity in tracker.known_processes.values():
            if time.monotonic() >= deadline:
                return False
            supervision.safe_signal_pid(tracker, identity, sig)
        if sig == signal.SIGTERM:
            time.sleep(0.05)
    live, _ = tracker.scan()
    return not live


def capture(command, cwd, output, name, seconds, env=None):
    """Byte capture with the repository's identity-checked descendant tracker."""
    safe_path(output)
    safe_path(cwd)
    admitted, reason = supervision.preflight_supervision()
    if not admitted:
        raise EvidenceError(reason)
    started = time.monotonic()
    deadline = started + seconds
    tracker = None
    process = None
    status = {"argv": command, "cwd": str(cwd), "timeout_seconds": seconds,
              "returncode": None, "status": "invalid",
              "started_at": datetime.now(timezone.utc).isoformat()}
    selector = selectors.DefaultSelector()
    hashes = {}
    try:
        with (output / f"{name}.stdout").open("xb", buffering=0) as stdout, (output / f"{name}.stderr").open("xb", buffering=0) as stderr:
            process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                                       stderr=subprocess.PIPE, start_new_session=True)
            tracker = supervision.ProcessTracker(process.pid)
            tracker.scan()
            for pipe, destination in ((process.stdout, stdout), (process.stderr, stderr)):
                os.set_blocking(pipe.fileno(), False)
                selector.register(pipe, selectors.EVENT_READ, destination)
                hashes[destination] = hashlib.sha256()
            while selector.get_map() or process.poll() is None:
                if time.monotonic() >= deadline:
                    raise EvidenceError(f"{name}: deadline expired (including output EOF)")
                tracker.scan()
                for key, _ in selector.select(min(0.05, max(0, deadline - time.monotonic()))):
                    chunk = os.read(key.fileobj.fileno(), 65536)
                    if chunk:
                        if key.data.tell() + len(chunk) > 64 * 1024 * 1024:
                            raise EvidenceError(f"{name}: output exceeds 64 MiB per stream")
                        key.data.write(chunk)
                        hashes[key.data].update(chunk)
                    else:
                        selector.unregister(key.fileobj)
                if process.poll() is not None and not selector.get_map():
                    break
            live, _ = tracker.scan()
            if any(identity.pid != process.pid for identity in live):
                raise EvidenceError(f"{name}: descendants remain after command completion")
            for stream in (stdout, stderr):
                stream.flush()
                if digest(Path(stream.name)) != hashes[stream].hexdigest():
                    raise EvidenceError(f"{name}: raw capture was modified")
            if time.monotonic() >= deadline:
                raise EvidenceError(f"{name}: deadline expired (including output EOF and capture validation)")
            status["raw_sha256"] = {Path(stream.name).name: hashes[stream].hexdigest() for stream in (stdout, stderr)}
            status.update(returncode=process.returncode, status="completed")
    except BaseException as error:
        status["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        selector.close()
        try:
            if process is not None:
                if process.poll() is None:
                    process.kill()
                process.wait(timeout=1)
                if tracker is not None and not bounded_cleanup(tracker):
                    status["status"] = "invalid"
                    status["cleanup_error"] = "Known descendants could not be terminated within the cleanup budget"
        except (OSError, subprocess.SubprocessError, supervision.SupervisionError) as error:
            status.update(status="invalid", cleanup_error=f"{type(error).__name__}: {error}")
        finally:
            if process is not None:
                for pipe in (process.stdout, process.stderr):
                    if pipe is not None:
                        pipe.close()
            status.update(finished_at=datetime.now(timezone.utc).isoformat(),
                          elapsed_seconds=time.monotonic() - started)
            write_json(output / f"{name}.process.json", status)
    if status["status"] != "completed":
        raise EvidenceError(f"{name}: cleanup was not verified")
    return process.returncode


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments], timeout=10)


def source_state(root, scope="compiler"):
    if Path(os.fsdecode(git(root, "rev-parse", "--show-toplevel").strip())) != root:
        raise EvidenceError(f"Compiler root is not the exact Git worktree root: {root}")
    files = {}
    for raw in git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard").split(b"\0"):
        if not raw:
            continue
        relative = os.fsdecode(raw)
        relevant = (
            relative.startswith(("src/", "scripts/")) or "/" not in relative
            if scope == "compiler"
            else relative.startswith(("tests/E2E/agent-tasks/", "src/Calor.Compiler/Resources/Teaching/"))
            or relative == "scripts/run_flake_gate.py"
        )
        if not relevant:
            continue
        path = root / relative
        # Submodules are pinned by Git status/commit; do not read external stores.
        if path.is_dir():
            continue
        safe_path(path, directory=False)
        files[relative] = digest(path) if path.is_file() else None
    return {"root": str(root), "commit": git(root, "rev-parse", "HEAD").decode().strip(),
            "status": git(root, "status", "--porcelain=v1", "--untracked-files=all").decode(),
            "files": files}


def retain(root, files, destination):
    destination.mkdir()
    for relative, expected in files.items():
        if expected is None:
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / relative, target)
        if digest(target) != expected:
            raise EvidenceError(f"Input changed during retention: {root / relative}")


def usage(transcript, model):
    events = [parse_json(line) for line in transcript.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not events or any(not isinstance(event, dict) for event in events):
        raise EvidenceError("Empty or unsupported stream-json output")
    initial = [event for event in events if event.get("type") == "system" and event.get("subtype") == "init"]
    results = [event for event in events if event.get("type") == "result"]
    if len(initial) != 1 or initial[0].get("model") != model or len(results) != 1 or events[-1] != results[0]:
        raise EvidenceError("Missing/duplicate init/result or model mismatch")
    result = results[0]
    if not isinstance(initial[0].get("session_id"), str) or not initial[0]["session_id"]:
        raise EvidenceError("Missing CLI session identity")
    if result.get("session_id") != initial[0]["session_id"]:
        raise EvidenceError("Result session identity mismatch")
    if result.get("subtype") != "success" or result.get("is_error") is not False or result.get("errors"):
        raise EvidenceError("CLI result reports error or unsupported subtype")
    if not isinstance(result.get("modelUsage"), dict) or set(result["modelUsage"]) != {model}:
        raise EvidenceError("Unsupported/mismatched modelUsage (exact single model required)")
    for event in events:
        if event.get("type") not in ("system", "assistant", "user", "result"):
            raise EvidenceError(f"Unsupported event type: {event.get('type')}")
        if event.get("parent_tool_use_id") is not None:
            raise EvidenceError("Subagent transcripts are not supported by measured schema v1")
        if event.get("error") or event.get("aborted"):
            raise EvidenceError("CLI stream reports an error/aborted message")
        if event.get("type") == "system" and event.get("subtype") != "init":
            raise EvidenceError("Unsupported system event subtype")
    main_counts = result.get("usage")
    fields = ("input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens")
    if not isinstance(main_counts, dict) or any(type(main_counts.get(field)) is not int or main_counts[field] < 0 for field in fields):
        raise EvidenceError("Missing/malformed real token usage")
    model_counts = result["modelUsage"][model]
    model_fields = ("inputTokens", "outputTokens", "cacheCreationInputTokens", "cacheReadInputTokens")
    if not isinstance(model_counts, dict) or any(type(model_counts.get(field)) is not int or model_counts[field] < 0 for field in model_fields):
        raise EvidenceError("Missing/malformed real modelUsage token fields")
    counts = dict(zip(fields, (model_counts[field] for field in model_fields)))
    if counts["output_tokens"] == 0 or sum(counts[field] for field in fields) == 0:
        raise EvidenceError("Empty usage is not measured evidence")
    if main_counts["output_tokens"] == 0:
        raise EvidenceError("Empty main-loop output usage is not measured evidence")
    if type(result.get("num_turns")) is not int or result["num_turns"] <= 0:
        raise EvidenceError("Missing/malformed CLI num_turns")
    assistants, tools = set(), {}
    for event in events:
        if event.get("type") != "assistant":
            continue
        message = event.get("message", {})
        if message.get("model") != model or not isinstance(message.get("id"), str) or not message["id"]:
            raise EvidenceError("Assistant identity/model missing or mismatched")
        assistants.add(message["id"])
        if not isinstance(message.get("content"), list):
            raise EvidenceError("Malformed assistant content")
        for block in message["content"]:
            if not isinstance(block, dict):
                raise EvidenceError("Malformed assistant content block")
            if block.get("type") == "tool_use":
                identity = block.get("id")
                if not isinstance(identity, str) or not identity or (identity in tools and tools[identity] != block):
                    raise EvidenceError("Missing/conflicting tool identity")
                tools[identity] = block
    if not assistants:
        raise EvidenceError("No observed assistant messages")
    return {**{field: counts[field] for field in fields},
            "total_input_tokens": sum(counts[field] for field in fields if field != "output_tokens"),
            "total_tokens": sum(counts[field] for field in fields),
            "source": "sole final result.modelUsage[exact_model]; never sum result.usage or assistant counters",
            "main_loop_usage": {field: main_counts[field] for field in fields},
            "assistant_messages": len(assistants), "tool_calls": len(tools),
            "session_id": initial[0]["session_id"],
            "cli_num_turns": result["num_turns"], "iterations_to_green": None,
            "agent_compiler_build_iterations": None}


def stage(name, output, root, configuration, task=None, workspace=None):
    commands = {
        "build": "build_compiler",
        "setup": 'setup_workspace "$(cat "$1/task.json")" "$2"',
        "verify": 'verify_task "$(cat "$1/task.json")" "$1" "$2"',
    }
    script = 'source "$1"; shift; trap - SIGINT SIGTERM; ' + commands[name]
    env = os.environ.copy()
    env.update(CALOR_COMPILER_ROOT=str(root), CALOR_COMPILER_CONFIGURATION=configuration,
               KEEP_WORKSPACE="1", VERBOSE="true", DOTNET_CLI_USE_MSBUILD_SERVER="0",
               MSBUILDDISABLENODEREUSE="1", UseSharedCompilation="false")
    return capture(["bash", "-c", script, "measured-stage", str(HELPERS),
                    str(task or ""), str(workspace or "")], REPO, output, name, 300, env)


def select_tasks(args):
    if args.task and (args.category or args.filter):
        raise EvidenceError("--task cannot be combined with --category/--filter")
    category = TASKS
    if args.category:
        if not re.fullmatch(r"[a-zA-Z0-9_-]+", args.category):
            raise EvidenceError("Invalid category")
        category = TASKS / args.category
    selected = []
    for path in sorted(category.glob("**/task.json")):
        safe_path(path, directory=False)
        data = parse_json(path.read_text())
        if not isinstance(data, dict):
            raise EvidenceError(f"Task must be a JSON object: {path}")
        identity = data.get("id")
        if not isinstance(identity, str) or not re.fullmatch(r"[a-zA-Z0-9_-]+", identity):
            raise EvidenceError(f"Invalid task identity: {path}")
        if args.task and identity != args.task:
            continue
        if args.filter and re.search(args.filter, identity, re.I) is None:
            continue
        selected.append((path.parent, data))
    if not selected or len({data["id"] for _, data in selected}) != len(selected):
        raise EvidenceError("No tasks selected or duplicate task IDs")
    for task, data in selected:
        fixture = data.get("fixture")
        if not isinstance(data.get("prompt"), str) or not data["prompt"]:
            raise EvidenceError(f"Missing prompt: {task}")
        if not isinstance(fixture, str) or not re.fullmatch(r"[a-zA-Z0-9_-]+", fixture):
            raise EvidenceError("Measured mode requires an existing local fixture; GitHub inputs unsupported")
        inventory(HERE.parent / "fixtures" / fixture)
        inventory(task)
        if type(data.get("timeout", 120)) is not int or not 0 < data.get("timeout", 120) <= 3600:
            raise EvidenceError("Task timeout must be integer seconds in [1, 3600]")
    return selected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--measured-output", type=Path, required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--arm", required=True)
    parser.add_argument("--task")
    parser.add_argument("--category")
    parser.add_argument("--filter")
    parser.add_argument("--single-run", action="store_true")
    parser.add_argument("--verbose", "-v", action="store_true")
    seen = set()
    for argument in sys.argv[1:]:
        if argument.startswith("--"):
            name = argument.split("=", 1)[0]
            if name in seen:
                raise EvidenceError(f"Duplicate option: {name}")
            seen.add(name)
    args = parser.parse_args()
    if not re.fullmatch(r"claude-[a-z0-9.-]+-\d{8}", args.model):
        raise EvidenceError("--model must be an explicit dated Claude ID, not an alias/default")
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", args.arm):
        raise EvidenceError("Invalid arm label")
    output = safe_path(args.measured_output, directory=False)
    safe_path(output.parent)
    root = safe_path(Path(os.environ.get("CALOR_COMPILER_ROOT", str(REPO))))
    configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", configuration):
        raise EvidenceError("Invalid compiler configuration")
    if output.exists() or nested(output, REPO) or nested(output, root):
        raise EvidenceError("Output must be new, outside input/compiler roots, and nonoverwriting")
    selected = select_tasks(args)
    if not shutil.which("jq") or not shutil.which("dotnet") or not shutil.which("claude"):
        raise EvidenceError("Measured mode requires jq, dotnet, and claude on PATH")
    source = source_state(root)
    runner = source_state(REPO, "runner")
    provider = Path(shutil.which("claude"))
    provider_identity = {"path": str(provider), "sha256": digest(provider)}
    if not (root / "src/Calor.Compiler/Calor.Compiler.csproj").is_file():
        raise EvidenceError("Selected compiler project missing")
    version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
    if not version:
        raise EvidenceError("Compiler version missing")
    inputs = {str(task): inventory(task) for task, _ in selected}
    for _, data in selected:
        fixture = HERE.parent / "fixtures" / data["fixture"]
        inputs[str(fixture)] = inventory(fixture)
    # This is the existing helper's integration teaching selection, not an arm primer policy.
    teaching = REPO / "src/Calor.Compiler/Resources/Teaching/Generated"
    inputs[str(teaching)] = inventory(teaching)
    inputs[str(HERE)] = inventory(HERE, exclude_build=True)
    output.mkdir(mode=0o700)
    record = {"schema": 1, "run_id": str(uuid.uuid4()), "status": "invalid",
              "model": args.model, "arm": args.arm, "commandline": sys.argv,
              "provider_executable": provider_identity,
              "compiler": {**source, "configuration": configuration, "version": version},
              "runner": runner, "inputs": inputs, "tasks": [], "skips": [],
              "selected_tasks": [data["id"] for _, data in selected],
              "token_definitions": "modelUsage query-pipeline totals, including internal/subagent calls on this model; excludes helper calls outside that pipeline. Input excludes cache; total = input + output + cache creation + cache read. Main-loop result.usage is retained separately, never added."}
    try:
        retain(root, source["files"], output / "compiler-source")
        retain(REPO, runner["files"], output / "runner-source")
        if stage("build", output, root, configuration):
            raise EvidenceError("Selected compiler build failed")
        image = root / f"src/Calor.Compiler/bin/{configuration}/net10.0"
        images = inventory(image)
        for required in ("calor", "calor.dll", "Calor.Runtime.dll", "Microsoft.Z3.dll"):
            if required not in images:
                raise EvidenceError(f"Selected compiler image missing {required}")
        if not os.access(image / "calor", os.X_OK):
            raise EvidenceError("Selected compiler is not executable")
        retain(image, images, output / "compiler-image")
        record["compiler"]["image"] = images
        if capture([str(image / "calor"), "--version"], REPO, output, "compiler-version", 30):
            raise EvidenceError("Selected compiler image cannot execute")
        binary_version = (output / "compiler-version.stdout").read_text().strip()
        if not re.search(rf"(?<![\d.]){re.escape(version)}(?![\d.])", binary_version):
            raise EvidenceError("Selected compiler image/version mismatch")
        record["compiler"]["reported_version"] = binary_version
        retained_attempts = {}

        def unchanged():
            if source_state(root) != source or source_state(REPO, "runner") != runner:
                raise EvidenceError("Compiler/runner source or Git worktree changed")
            if inventory(image) != images:
                raise EvidenceError("Selected compiler image changed")
            if shutil.which("claude") != str(provider) or digest(provider) != provider_identity["sha256"]:
                raise EvidenceError("Provider executable changed")
            for original, retained in (
                (source["files"], output / "compiler-source"),
                (runner["files"], output / "runner-source"),
                (images, output / "compiler-image"),
            ):
                expected = {path: value for path, value in original.items() if value is not None}
                if inventory(retained) != expected:
                    raise EvidenceError(f"Retained provenance changed: {retained}")
            for path, expected in inputs.items():
                if inventory(Path(path), exclude_build=Path(path) == HERE) != expected:
                    raise EvidenceError(f"Task/fixture/helper/teaching input changed: {path}")
            for path, expected in retained_attempts.items():
                if inventory(path) != expected:
                    raise EvidenceError(f"Earlier attempt artifacts changed: {path}")

        unchanged()
        runs = 1 if args.single_run else 3
        required = 1 if args.single_run else 2
        sessions = set()
        for task, data in selected:
            result = {"id": data["id"], "arm": args.arm, "attempts": [],
                      "required_passes": required, "attempt_denominator": runs}
            record["tasks"].append(result)
            for number in range(1, runs + 1):
                attempt_dir = output / f"{data['id']}-attempt-{number}"
                attempt_dir.mkdir()
                workspace = attempt_dir / "workspace"
                workspace.mkdir()
                attempt = {"id": f"{record['run_id']}:{data['id']}:{number}", "status": "invalid"}
                result["attempts"].append(attempt)
                unchanged()
                if stage("setup", attempt_dir, root, configuration, task, workspace):
                    raise EvidenceError("Workspace setup failed")
                start = inventory(workspace)
                retain(workspace, start, attempt_dir / "initial-workspace")
                attempt["initial_inputs"] = start
                attempt["teaching_sha256"] = digest(workspace / "CLAUDE.md")
                unchanged()
                command = [str(provider), "--print", "--dangerously-skip-permissions",
                           "--model", args.model, "--verbose", "--output-format", "stream-json", data["prompt"]]
                if capture(command, workspace, attempt_dir, "agent", data.get("timeout", 120)):
                    raise EvidenceError("Agent invocation failed; task cannot rescue evidence")
                unchanged()
                if inventory(attempt_dir / "initial-workspace") != start:
                    raise EvidenceError("Retained initial inputs changed")
                attempt["usage"] = usage(attempt_dir / "agent.stdout", args.model)
                session = attempt["usage"]["session_id"]
                if session in sessions:
                    raise EvidenceError("Reused CLI session identity; cumulative usage cannot be attributed")
                sessions.add(session)
                attempt["agent_outputs"] = inventory(workspace)
                retain(workspace, attempt["agent_outputs"], attempt_dir / "agent-output-workspace")
                before_verify = {
                    path: value for path, value in inventory(attempt_dir).items()
                    if not path.startswith("workspace/")
                }
                verify_exit = stage("verify", attempt_dir, root, configuration, task, workspace)
                unchanged()
                current_artifacts = inventory(attempt_dir)
                if any(current_artifacts.get(path) != expected for path, expected in before_verify.items()):
                    raise EvidenceError("Verification changed retained inputs or capture artifacts")
                agent_process = parse_json((attempt_dir / "agent.process.json").read_text())
                if any(digest(attempt_dir / filename) != expected
                       for filename, expected in agent_process["raw_sha256"].items()):
                    raise EvidenceError("Raw agent capture changed during verification")
                attempt.update(status="accepted", passed=verify_exit == 0,
                               verification_returncode=verify_exit, final_outputs=inventory(workspace))
                write_json(attempt_dir / "attempt.json", attempt)
                retained_attempts[attempt_dir] = inventory(attempt_dir)
            result["passes"] = sum(attempt["passed"] for attempt in result["attempts"])
            result["vote_passed"] = result["passes"] >= required
            result["tokens_total"] = sum(attempt["usage"]["total_tokens"] for attempt in result["attempts"])
            result["tokens_per_attempt"] = result["tokens_total"] / runs
            result["input_tokens_total"] = sum(attempt["usage"]["total_input_tokens"] for attempt in result["attempts"])
            result["input_tokens_per_attempt"] = result["input_tokens_total"] / runs
        unchanged()
        record["artifact_hashes"] = inventory(output)
        attempts = [attempt for task in record["tasks"] for attempt in task["attempts"]]
        record.update(status="accepted", task_denominator=len(record["tasks"]),
                      task_vote_passes=sum(task["vote_passed"] for task in record["tasks"]),
                      attempt_denominator=len(attempts), attempt_passes=sum(attempt["passed"] for attempt in attempts),
                      task_vote_pass_rate=sum(task["vote_passed"] for task in record["tasks"]) / len(record["tasks"]),
                      attempt_pass_rate=sum(attempt["passed"] for attempt in attempts) / len(attempts),
                      pass_rate_units="fraction in [0, 1]; task votes and attempts are distinct",
                      tokens_total=sum(attempt["usage"]["total_tokens"] for attempt in attempts),
                      tokens_per_task_all_attempts=sum(attempt["usage"]["total_tokens"] for attempt in attempts) / len(record["tasks"]),
                      input_tokens_total=sum(attempt["usage"]["total_input_tokens"] for attempt in attempts),
                      input_tokens_per_task_all_attempts=sum(attempt["usage"]["total_input_tokens"] for attempt in attempts) / len(record["tasks"]),
                      skipped_tasks=0)
    except BaseException as error:
        record["error"] = f"{type(error).__name__}: {error}"
        raise
    finally:
        write_json(output / "run.json", record)
    print(f"Measured evidence retained: {output}; task votes {record['task_vote_passes']}/{record['task_denominator']}")
    return 0 if record["task_vote_passes"] == record["task_denominator"] else 1


def cancelled(signum, _frame):
    signal.signal(signal.SIGINT, signal.SIG_IGN)
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
    raise EvidenceError(f"Cancelled by signal {signum}")


if __name__ == "__main__":
    signal.signal(signal.SIGINT, cancelled)
    signal.signal(signal.SIGTERM, cancelled)
    try:
        sys.exit(main())
    except (EvidenceError, OSError, ValueError, re.error, subprocess.SubprocessError,
            supervision.SupervisionError, ET.ParseError) as error:
        print(f"Measured evidence rejected: {error}", file=sys.stderr)
        sys.exit(2)
