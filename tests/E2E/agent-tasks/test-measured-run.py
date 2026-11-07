#!/usr/bin/env python3
"""No-provider controls. Only the guard executable can occupy Claude's PATH slot."""

import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch


TASKS = Path(__file__).resolve().parent
ROOT = TASKS.parents[2]
RUNNER = TASKS / "run-agent-tests.sh"
MODEL = "claude-sonnet-4-20250514"
spec = importlib.util.spec_from_file_location("measured_runner", TASKS / "lib/measured-run.py")
measured = importlib.util.module_from_spec(spec)
spec.loader.exec_module(measured)

SOURCE = """§M{Calculator}
  §F{Factorial:pub} (i32:n) -> i32
    §Q (>= n 0)
    §Q (<= n 5)
    §S (>= result 1)
    §IF (<= n 1)
      §R 1
    §EI (== n 2)
      §R 2
    §EI (== n 3)
      §R 6
    §EI (== n 4)
      §R 24
    §R 120
"""


def events():
    return [
        {"type": "system", "subtype": "init", "model": MODEL, "session_id": "guard-session"},
        {"type": "assistant", "message": {"id": "m1", "model": MODEL,
         "content": [{"type": "tool_use", "id": "t1", "name": "Bash", "input": {"command": "true"}}]}},
        {"type": "assistant", "message": {"id": "m1", "model": MODEL,
         "content": [{"type": "text", "text": "done"}]}},
        {"type": "result", "subtype": "success", "is_error": False,
         "num_turns": 2, "session_id": "guard-session",
         "modelUsage": {MODEL: {"inputTokens": 10, "outputTokens": 20,
                               "cacheCreationInputTokens": 30, "cacheReadInputTokens": 40}},
         "usage": {"input_tokens": 1, "output_tokens": 2,
                   "cache_creation_input_tokens": 3, "cache_read_input_tokens": 4}},
    ]


class SchemaTests(unittest.TestCase):
    def test_explicit_model_identity_shapes(self):
        for model in (MODEL, "claude-3-5-sonnet-20241022", "claude-opus-5-5"):
            measured.validate_model_id(model)
        for model in ("", "sonnet", "opus", "default", "claude-opus",
                      "claude-sonnet-latest", "claude-opus-5-5-latest",
                      "claude-opus-5-5-default", "claude-opus-5.5"):
            with self.subTest(model=model), self.assertRaises(measured.EvidenceError):
                measured.validate_model_id(model)

    def parse(self, value):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "stdout"
            path.write_text("\n".join(json.dumps(item) for item in value))
            return measured.usage(path, MODEL)

    def test_authoritative_usage_and_distinct_turns(self):
        result = self.parse(events())
        self.assertEqual(result["total_tokens"], 100)
        self.assertEqual(result["total_input_tokens"], 80)
        self.assertEqual(result["assistant_messages"], 1)
        self.assertEqual(result["tool_calls"], 1)
        self.assertEqual(result["cli_num_turns"], 2)
        self.assertEqual(result["main_loop_usage"]["input_tokens"], 1)
        self.assertIsNone(result["iterations_to_green"])

    def test_malformed_empty_errors_and_mismatched_schema(self):
        variants = []
        for field, value in (("usage", {}), ("subtype", "error_max_turns"),
                             ("is_error", True), ("num_turns", 0),
                             ("modelUsage", {"other-model": {}})):
            value_events = events()
            value_events[-1][field] = value
            variants.append(value_events)
        for value in (True, -1, None, "10"):
            value_events = events()
            value_events[-1]["usage"]["input_tokens"] = value
            variants.append(value_events)
            value_events = events()
            value_events[-1]["modelUsage"][MODEL]["inputTokens"] = value
            variants.append(value_events)
        variants.append(events() + [{"type": "system", "subtype": "compact_boundary"}])
        value_events = events()
        value_events[0]["model"] = "alias"
        variants += [value_events, [], events() + [events()[-1]], events()[:-1]]
        for variant in variants:
            with self.subTest(variant=variant), self.assertRaises((measured.EvidenceError, ValueError)):
                self.parse(variant)

    def test_duplicate_json_and_partial_usage_are_rejected(self):
        with self.assertRaises(measured.EvidenceError):
            measured.parse_json('{"usage": {}, "usage": {"input_tokens": 5}}')
        with self.assertRaises(measured.EvidenceError):
            measured.parse_json('{"input_tokens": NaN}')
        value = events()
        value[-1]["modelUsage"][MODEL] = {}
        with self.assertRaises(measured.EvidenceError):
            self.parse(value)

    def test_inputs_symlinks_and_mutations(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            file = root / "input.calr.txt"
            file.write_text("input bytes")
            original = measured.inventory(root)
            file.write_text("changed bytes")
            self.assertNotEqual(measured.inventory(root), original)
            (root / "link").symlink_to(file)
            with self.assertRaises(measured.EvidenceError):
                measured.inventory(root)

    def test_bad_selection_and_fixture_fail_before_admission(self):
        with tempfile.TemporaryDirectory() as temporary:
            tasks = Path(temporary).resolve()
            task = tasks / "task"
            task.mkdir()
            path = task / "task.json"
            args = type("Args", (), {"task": None, "filter": None, "category": None})()
            with patch.object(measured, "TASKS", tasks):
                for value in ([], {}, {"id": "bad", "prompt": "test", "fixture": "missing"},
                              {"id": "bad", "prompt": "test", "fixture": "../unsafe"}):
                    path.write_text(json.dumps(value))
                    with self.assertRaises(measured.EvidenceError):
                        measured.select_tasks(args)


class CaptureTests(unittest.TestCase):
    def test_slow_identity_scan_cannot_accept_late_eof(self):
        original = measured.supervision.ProcessTracker.scan
        calls = 0

        def delayed_scan(tracker):
            nonlocal calls
            calls += 1
            if calls == 2:
                time.sleep(0.4)
            return original(tracker)

        with tempfile.TemporaryDirectory() as temporary, patch.object(
            measured.supervision.ProcessTracker, "scan", delayed_scan
        ):
            path = Path(temporary).resolve()
            with self.assertRaisesRegex(measured.EvidenceError, "deadline expired"):
                measured.capture([sys.executable, "-c", "pass"], path, path, "late-eof", 0.2)
            self.assertEqual(json.loads((path / "late-eof.process.json").read_text())["status"], "invalid")

    def test_exact_raw_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary).resolve()
            self.assertEqual(measured.capture(
                [sys.executable, "-c", "import os; os.write(1,b'\\xff\\x00'); os.write(2,b'err\\xfe')"],
                path, path, "raw", 5), 0)
            self.assertEqual((path / "raw.stdout").read_bytes(), b"\xff\x00")
            self.assertEqual((path / "raw.stderr").read_bytes(), b"err\xfe")

    def test_timeout_and_escaped_inherited_pipe(self):
        for code in (
            "import time; print('started',flush=True); time.sleep(60)",
            "import subprocess,sys,time; subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)'],start_new_session=True); time.sleep(1)",
        ):
            with self.subTest(code=code), tempfile.TemporaryDirectory() as temporary:
                path = Path(temporary).resolve()
                start = time.monotonic()
                with self.assertRaises(measured.EvidenceError):
                    measured.capture([sys.executable, "-c", code], path, path, "timeout", 1.5)
                self.assertLess(time.monotonic() - start, 8)
                self.assertEqual(json.loads((path / "timeout.process.json").read_text())["status"], "invalid")


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="e2e-measured-control-")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name).resolve()
        self.bin = self.base / "bin"
        self.bin.mkdir()
        self.calls = self.base / "calls"
        self.provider = self.bin / "claude"
        self.provider.write_text(
            "#!/usr/bin/env python3\n"
            "import json,os,sys,uuid\nfrom pathlib import Path\n"
            "with Path(os.environ['GUARD_CALLS']).open('a') as output: output.write(json.dumps(sys.argv[1:])+'\\n')\n"
            "Path('Calculator.calr').write_text(os.environ['GUARD_SOURCE'])\n"
            "if os.environ.get('GUARD_FAIL_SECOND') and len(Path(os.environ['GUARD_CALLS']).read_text().splitlines())==2:\n"
            "    Path('Calculator.calr').write_text(os.environ['GUARD_SOURCE'].replace('§R 120','§R 119'))\n"
            "session=os.environ.get('GUARD_SESSION',str(uuid.uuid4()))\n"
            "for event in json.loads(os.environ['GUARD_EVENTS']):\n"
            "    if 'session_id' in event: event['session_id']=session\n"
            "    print(json.dumps(event),flush=True)\n"
            "os.write(2,b'guard stderr\\xff')\n"
            "exec(os.environ.get('GUARD_ACTION',''))\n"
            "sys.exit(int(os.environ.get('GUARD_EXIT','0')))\n"
        )
        self.provider.chmod(0o755)
        self.env = dict(os.environ, PATH=f"{self.bin}:{os.environ['PATH']}",
                        GUARD_CALLS=str(self.calls), GUARD_SOURCE=SOURCE,
                        GUARD_EVENTS=json.dumps(events()), CALOR_COMPILER_ROOT=str(ROOT))
        self.env.pop("CALOR_COMPILER_CONFIGURATION", None)
        self.output = self.base / "evidence"

    def run_control(self, *arguments, env=None):
        return subprocess.run(["bash", str(RUNNER), *arguments], env=env or self.env,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=240)

    def options(self, single=True):
        return ["--measured-output", str(self.output), "--model", MODEL,
                "--arm", "control", "--task", "logic-001"] + (["--single-run"] if single else [])

    def test_invalid_admission_never_reaches_provider(self):
        cases = [
            ["--model", MODEL], self.options() + ["--clean"],
            self.options() + ["--category", "basic-syntax"],
            self.options() + ["--task", "missing-task"],
            self.options() + ["--model", "sonnet"],
            self.options() + ["--measured-output", str(ROOT / "nested-evidence")],
        ]
        for arguments in cases:
            with self.subTest(arguments=arguments):
                result = self.run_control(*arguments)
                self.assertNotEqual(result.returncode, 0, result.stdout)
                self.assertFalse(self.calls.exists())
        self.output.mkdir()
        self.assertNotEqual(self.run_control(*self.options()).returncode, 0)
        self.assertFalse(self.calls.exists())
        link = self.base / "link"
        link.symlink_to(self.output, target_is_directory=True)
        self.assertNotEqual(self.run_control(*self.options(), "--measured-output", str(link / "new")).returncode, 0)
        self.assertFalse(self.calls.exists())
        bad_env = dict(self.env, CALOR_COMPILER_ROOT=str(self.base / "missing"))
        self.assertNotEqual(self.run_control(*self.options(), env=bad_env).returncode, 0)
        self.assertFalse(self.calls.exists())

    def test_real_build_setup_compilation_custom_verifier_and_voting(self):
        result = self.run_control(*self.options(single=False), env=dict(self.env, GUARD_FAIL_SECOND="1"))
        self.assertEqual(result.returncode, 0, result.stdout.decode() + result.stderr.decode())
        record = json.loads((self.output / "run.json").read_text())
        self.assertEqual(record["status"], "accepted")
        self.assertEqual(record["compiler"]["root"], str(ROOT))
        self.assertEqual(record["compiler"]["configuration"], "Debug")
        self.assertEqual(record["compiler"]["commit"], measured.source_state(ROOT)["commit"])
        self.assertIn("calor.dll", record["compiler"]["image"])
        self.assertEqual(record["task_vote_passes"], 1)
        self.assertEqual(record["attempt_passes"], 2)
        self.assertEqual(record["attempt_denominator"], 3)
        self.assertEqual(record["task_vote_pass_rate"], 1)
        self.assertEqual(record["attempt_pass_rate"], 2 / 3)
        self.assertEqual(record["tasks"][0]["tokens_total"], 300)
        self.assertEqual(record["tasks"][0]["tokens_per_attempt"], 100)
        self.assertEqual(record["tasks"][0]["input_tokens_total"], 240)
        self.assertEqual(record["tasks"][0]["input_tokens_per_attempt"], 80)
        self.assertEqual(record["input_tokens_total"], 240)
        self.assertEqual(record["input_tokens_per_task_all_attempts"], 240)
        self.assertEqual(record["tasks"][0]["passes"], 2)
        self.assertFalse(record["tasks"][0]["attempts"][1]["passed"])
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertEqual(len(calls), 3)
        self.assertEqual(calls[0], ["--print", "--dangerously-skip-permissions", "--model", MODEL,
                                   "--verbose", "--output-format", "stream-json",
                                   json.loads((TASKS / "tasks/logic-implementation/01_factorial/task.json").read_text())["prompt"]])
        attempt = self.output / "logic-001-attempt-1"
        self.assertEqual((attempt / "agent.stderr").read_bytes(), b"guard stderr\xff")
        self.assertIn("6 runtime cases", (attempt / "verify.stdout").read_text())
        self.assertTrue((attempt / "initial-workspace/Calculator.calr").is_file())
        self.assertNotEqual((attempt / "initial-workspace/Calculator.calr").read_text(),
                            (attempt / "workspace/Calculator.calr").read_text())
        self.assertFalse((self.output / "run.json").is_symlink())
        self.assertIn("logic-001-attempt-3/verify.stdout", record["artifact_hashes"])
        self.assertIn("logic-001-attempt-3/attempt.json", record["artifact_hashes"])
        self.assertEqual((attempt / "agent-output-workspace/Calculator.calr").read_text(),
                         (attempt / "workspace/Calculator.calr").read_text())

    def test_explicit_versioned_model_without_date(self):
        model = "claude-opus-5-5"
        value = events()
        value[0]["model"] = model
        for event in value:
            if event["type"] == "assistant":
                event["message"]["model"] = model
        value[-1]["modelUsage"][model] = value[-1]["modelUsage"].pop(MODEL)
        arguments = self.options()
        arguments[arguments.index("--model") + 1] = model
        result = self.run_control(*arguments, env=dict(self.env, GUARD_EVENTS=json.dumps(value)))
        self.assertEqual(result.returncode, 0, result.stderr)
        record = json.loads((self.output / "run.json").read_text())
        self.assertEqual(record["model"], model)
        self.assertEqual(record["status"], "accepted")
        self.assertEqual(json.loads(self.calls.read_text().splitlines()[0])[3], model)

    def test_resolved_model_mismatch_cannot_rescue_correct_implementation(self):
        arguments = self.options()
        arguments[arguments.index("--model") + 1] = "claude-sonnet-4"
        result = self.run_control(*arguments)
        self.assertEqual(result.returncode, 2, result.stderr)
        record = json.loads((self.output / "run.json").read_text())
        self.assertEqual(record["status"], "invalid")
        self.assertIn("model mismatch", record["error"])
        self.assertFalse((self.output / "logic-001-attempt-1/verify.stdout").exists())
        self.assertNotIn("task_vote_passes", record)

    def test_correct_file_cannot_rescue_invocation_or_usage_error(self):
        for failure in ("exit", "usage"):
            with self.subTest(failure=failure):
                self.output = self.base / failure
                env = dict(self.env)
                if failure == "exit":
                    env["GUARD_EXIT"] = "23"
                else:
                    value = events()
                    value[-1]["usage"] = {}
                    env["GUARD_EVENTS"] = json.dumps(value)
                result = self.run_control(*self.options(), env=env)
                self.assertEqual(result.returncode, 2, result.stderr)
                record = json.loads((self.output / "run.json").read_text())
                self.assertEqual(record["status"], "invalid")
                self.assertNotIn("task_vote_passes", record)
                self.assertFalse((self.output / "logic-001-attempt-1/verify.stdout").exists())

    def test_retained_input_mutation_and_workspace_symlink_invalidate_run(self):
        for label, action in (
            ("mutation", "Path('../initial-workspace/Calculator.calr').write_text('tampered')"),
            ("symlink", "Path('external').symlink_to('/etc/passwd')"),
            ("raw", "import time; time.sleep(0.3); Path('../agent.stdout').write_text('tampered')"),
        ):
            with self.subTest(label=label):
                self.output = self.base / label
                result = self.run_control(*self.options(), env=dict(self.env, GUARD_ACTION=action))
                self.assertEqual(result.returncode, 2, result.stderr)
                record = json.loads((self.output / "run.json").read_text())
                self.assertEqual(record["status"], "invalid")
                self.assertNotIn("task_vote_passes", record)

    def test_cancellation_retains_invalid_raw_evidence_and_stops_guard(self):
        env = dict(self.env, GUARD_ACTION="import time; time.sleep(60)")
        start = time.monotonic()
        process = subprocess.Popen(["bash", str(RUNNER), *self.options()], env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            deadline = start + 120
            while not self.calls.exists():
                if process.poll() is not None or time.monotonic() > deadline:
                    self.fail("Guard never reached admission")
                time.sleep(0.1)
            process.send_signal(signal.SIGTERM)
            _, stderr = process.communicate(timeout=10)
            self.assertEqual(process.returncode, 2, stderr)
            record = json.loads((self.output / "run.json").read_text())
            self.assertEqual(record["status"], "invalid")
            self.assertIn("Cancelled", record["error"])
            self.assertTrue((self.output / "logic-001-attempt-1/agent.stdout").exists())
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)

    def test_actual_selected_root_build_failure_never_calls_provider(self):
        root = self.base / "compiler"
        project = root / "src/Calor.Compiler/Calor.Compiler.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project><Invalid /></Project>")
        (root / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>0.29.0</Version></PropertyGroup></Project>")
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        subprocess.run(["git", "-C", str(root), "add", "."], check=True)
        subprocess.run(["git", "-C", str(root), "-c", "commit.gpgsign=false",
                        "-c", "user.name=Guard", "-c", "user.email=guard@example.invalid",
                        "commit", "-qm", "guard inputs\n\nCo-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"], check=True)
        result = self.run_control(*self.options(), env=dict(self.env, CALOR_COMPILER_ROOT=str(root),
                                                          CALOR_COMPILER_CONFIGURATION="Release"))
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertFalse(self.calls.exists())
        record = json.loads((self.output / "run.json").read_text())
        self.assertEqual(record["compiler"]["root"], str(root))
        self.assertEqual(record["compiler"]["configuration"], "Release")
        self.assertIn(str(project),
                      (self.output / "build.stderr").read_text() + (self.output / "build.stdout").read_text())

    def compiler_control_root(self, name, version="0.29.0"):
        root = self.base / name
        project = root / "src/Calor.Compiler/Calor.Compiler.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project />")
        (root / "Directory.Build.props").write_text(
            f"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>")
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        subprocess.run(["git", "-C", str(root), "add", "."], check=True)
        subprocess.run(["git", "-C", str(root), "-c", "commit.gpgsign=false",
                        "-c", "user.name=Guard", "-c", "user.email=guard@example.invalid",
                        "commit", "-qm", "guard inputs\n\nCo-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"],
                       check=True)
        image = root / "src/Calor.Compiler/bin/Release/net10.0"
        shutil.copytree(ROOT / "src/Calor.Compiler/bin/Debug/net10.0", image)
        # Build outputs are not source inputs, just as in the real repository.
        (root / ".git/info/exclude").write_text("bin/\n")
        return root, image

    def test_missing_corrupt_and_version_mismatched_images_never_admit_provider(self):
        dotnet = self.bin / "dotnet"
        dotnet.write_text("#!/usr/bin/env bash\nexit 0\n")
        dotnet.chmod(0o755)
        for failure in ("missing", "corrupt", "version"):
            with self.subTest(failure=failure):
                root, image = self.compiler_control_root(
                    f"compiler-{failure}", version="9.99.0" if failure == "version" else "0.29.0")
                self.output = self.base / f"evidence-{failure}"
                if failure == "missing":
                    (image / "calor.dll").unlink()
                elif failure == "corrupt":
                    (image / "calor").write_text("#!/usr/bin/env bash\nexit 77\n")
                result = self.run_control(*self.options(), env=dict(
                    self.env, CALOR_COMPILER_ROOT=str(root), CALOR_COMPILER_CONFIGURATION="Release"))
                self.assertEqual(result.returncode, 2, result.stderr)
                self.assertFalse(self.calls.exists())
                record = json.loads((self.output / "run.json").read_text())
                self.assertEqual(record["status"], "invalid")
                self.assertNotIn("task_vote_passes", record)
                expected = {"missing": "missing", "corrupt": "cannot execute", "version": "mismatch"}
                self.assertIn(expected[failure], record["error"].lower())

    def test_selected_source_and_image_mutation_after_admission_invalidates_run(self):
        # Mutate only isolated control roots, never the real worktree/compiler.
        dotnet = self.bin / "dotnet"
        dotnet.write_text("#!/usr/bin/env bash\nexit 0\n")
        dotnet.chmod(0o755)
        for label, relative in (("source", "src/Calor.Compiler/Calor.Compiler.csproj"),
                                ("image", "src/Calor.Compiler/bin/Release/net10.0/calor.dll")):
            with self.subTest(label=label):
                root, _ = self.compiler_control_root(f"mutation-{label}")
                self.output = self.base / f"evidence-{label}"
                action = f"Path({str(root / relative)!r}).write_bytes(b'mutated')"
                result = self.run_control(*self.options(), env=dict(
                    self.env, CALOR_COMPILER_ROOT=str(root), CALOR_COMPILER_CONFIGURATION="Release",
                    GUARD_ACTION=action))
                self.assertEqual(result.returncode, 2, result.stderr)
                record = json.loads((self.output / "run.json").read_text())
                self.assertEqual(record["status"], "invalid")
                self.assertIn("changed", record["error"])
                self.assertNotIn("task_vote_passes", record)

    def test_default_invocation_is_byte_equivalent(self):
        # Exact old helper invocation, guarded; it still emits plain CLI bytes.
        workspace = self.base / "workspace"
        workspace.mkdir()
        result = subprocess.run(
            ["bash", "-c", 'source "$1"; invoke_agent original "$2" 10 "$2/output"',
             "legacy-control", str(TASKS / "lib/helpers.sh"), str(workspace)],
            env=dict(self.env, GUARD_SESSION="legacy-session"), capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(self.calls.read_text().splitlines()[0]),
                         ["--print", "--dangerously-skip-permissions", "original"])
        self.assertNotIn("--model", self.calls.read_text())
        # The legacy helper merges stdout/stderr without JSON transformation.
        raw = (workspace / "output").read_bytes()
        expected = events()
        for event in expected:
            if "session_id" in event:
                event["session_id"] = "legacy-session"
        expected_bytes = "".join(json.dumps(event) + "\n" for event in expected).encode() + b"guard stderr\xff"
        self.assertEqual(raw, expected_bytes)

    def test_default_runner_preserves_task_behavior_and_plain_capture(self):
        result = self.run_control("--task", "logic-001", "--single-run", "--verbose",
                                  env=dict(self.env, GUARD_SESSION="legacy-session",
                                           TMPDIR=str(self.base), KEEP_WORKSPACE="1"))
        self.assertEqual(result.returncode, 0, result.stdout.decode() + result.stderr.decode())
        calls = [json.loads(line) for line in self.calls.read_text().splitlines()]
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][:2], ["--print", "--dangerously-skip-permissions"])
        self.assertEqual(len(calls[0]), 3)
        workspaces = list(self.base.glob("calor-agent-logic-001-run1-*"))
        self.assertEqual(len(workspaces), 1)
        self.assertTrue((workspaces[0] / ".agent-output.json").read_bytes().endswith(b"guard stderr\xff"))
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
