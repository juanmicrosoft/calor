#!/usr/bin/env python3
"""Unregistered, zero-provider init-arm plumbing controls."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

BENCH = Path(__file__).resolve().parents[1]
REPO = BENCH.parents[1]
RUNNER = BENCH / "run-pair.sh"
DLL = REPO / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
TASKS = REPO / "src/Calor.Tasks/bin/Release/net10.0/calor.dll"
PAIR = BENCH / "pairs/N1-001-string-utils"
spec = importlib.util.spec_from_file_location("init_capture", BENCH / "harness-capture.py")
hc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hc)
HAVE_PRODUCT = shutil.which("dotnet") and shutil.which("jq") and DLL.is_file() and TASKS.is_file()
BASE = "89f298e99"


def control_pair(destination):
    """Only disposable copies: historical pair bytes are never rewritten."""
    shutil.copytree(PAIR, destination)
    for path in destination.rglob("*.calr"):
        text = path.read_text()
        text = re.sub(r"\{(?:m|f)\d+:", "{", text)
        text = re.sub(r"§IF\{if\d+\}", "§IF", text)
        text = re.sub(r"§EACH\{e\d+:", "§EACH{", text)
        path.write_text(text)


class AdmissionTests(unittest.TestCase):
    def test_ordinary_pair_is_admitted(self):
        hc.initialized_arm_admission(PAIR / "pair.json", "calor", "calor")

    def test_frozen_inputs_and_other_arms_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "pair.json"
            original = json.loads((PAIR / "pair.json").read_text())
            for key, arm in (("calor", "csharp"), ("calor-pre-rows", "calor"),
                             ("calor-strict", "calor")):
                with self.subTest(key=key, arm=arm), self.assertRaises(ValueError):
                    hc.initialized_arm_admission(PAIR / "pair.json", key, arm)
            for field in ("seeded", "sourceAssembly", "registration", "frozenAt"):
                path.write_text(json.dumps(dict(original, **{field: {}})))
                with self.subTest(field=field), self.assertRaises(ValueError):
                    hc.initialized_arm_admission(path, "calor", "calor")

    def test_bad_duplicate_and_incompatible_flags_fail_before_provider(self):
        for extra in (["--initialized-workspace", "--initialized-workspace"],
                      ["--initialized-workspace", "yes"],
                      ["--initialized-workspac"],
                      ["--initialized-workspace", "--arm", "csharp"],
                      ["--initialized-workspace", "--canary-only"],
                      ["--initialized-workspace", "--edit-mechanism", "mcp-file"],
                      ["--initialized-workspace", "--arm-label", "calor"],
                      ["--initialized-workspace", "--runs", "0"],
                      ["--initialized-workspace", "--run-offset", "-1"],
                      ["--initialized-workspace", "--out", "/tmp/new-init-control",
                       "--out", "/tmp/another-init-control"],
                      ["--initialized-workspace", "--calor-dll", str(DLL),
                       "--calor-dll", str(DLL)],
                      ["--initialized-workspace", "--arm-config", "calor-strict"],
                      ["--initialized-workspace", "--arm-repo-root", "/missing-init-arm"]):
            with self.subTest(extra=extra):
                result = subprocess.run(
                    ["bash", str(RUNNER), "--pair", str(PAIR), "--arm", "calor"] + extra,
                    capture_output=True, text=True, timeout=10)
                self.assertNotEqual(result.returncode, 0, result.stdout)

    def test_subprocess_timeout_is_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(subprocess.TimeoutExpired):
                hc._initialized_command(
                    [sys.executable, "-c", "import time; time.sleep(60)"],
                    tmp, os.environ, 0.1)

    def test_inherited_pipe_timeout_and_cancellation_are_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(subprocess.TimeoutExpired):
                hc._initialized_command(
                    [sys.executable, "-c",
                     "import subprocess,sys; subprocess.Popen("
                     "[sys.executable,'-c','import time; time.sleep(60)'])"],
                    tmp, os.environ, 0.1)
            communicate = subprocess.Popen.communicate
            calls = []
            def interrupted(process, *args, **kwargs):
                calls.append(True)
                if len(calls) == 1:
                    raise KeyboardInterrupt()
                return communicate(process, *args, **kwargs)
            with patch.object(subprocess.Popen, "communicate", interrupted):
                with self.assertRaises(KeyboardInterrupt):
                    hc._initialized_command(
                        [sys.executable, "-c", "import time; time.sleep(60)"],
                        tmp, os.environ, 5)

    def test_existing_and_pinned_output_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "registered"
            output.mkdir()
            (output / "pins.json").write_text('{"keep":"original"}')
            result = subprocess.run(
                ["bash", str(RUNNER), "--pair", str(PAIR), "--arm", "calor",
                 "--initialized-workspace", "--out", str(output)],
                capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 3, result.stderr)
            self.assertEqual((output / "pins.json").read_text(), '{"keep":"original"}')
            with self.assertRaisesRegex(ValueError, "registered"):
                hc.initialized_arm_admission(PAIR / "pair.json", "calor", "calor",
                                             output / "new-child")

    def test_escaped_pipe_timeout_and_real_cancellation_are_bounded(self):
        for mode in ("timeout-exited-root", "timeout-live-root", "cancel-live-root"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                child_pid = root / "escaped.pid"
                command_pid = root / "command.pid"
                child = (
                    "import os,time; from pathlib import Path; "
                    "Path(%r).write_text(str(os.getpid())); "
                    "print('escaped pipe held open', flush=True); time.sleep(60)"
                ) % str(child_pid)
                command = (
                    "import os,subprocess,sys,time; from pathlib import Path; "
                    "Path(%r).write_text(str(os.getpid())); "
                    "subprocess.Popen([sys.executable,'-c',%r], start_new_session=True)\n"
                    "while not Path(%r).exists(): time.sleep(.01)\n"
                ) % (str(command_pid), child, str(child_pid))
                if mode != "timeout-exited-root":
                    command += "time.sleep(60)\n"
                driver = (
                    "import importlib.util,json,os,subprocess,sys,time\n"
                    "spec=importlib.util.spec_from_file_location('hc',%r)\n"
                    "hc=importlib.util.module_from_spec(spec); spec.loader.exec_module(hc)\n"
                    "start=time.monotonic()\n"
                    "try:\n"
                    " hc._initialized_command([sys.executable,'-c',%r],%r,os.environ,%r)\n"
                    "except (subprocess.TimeoutExpired,KeyboardInterrupt) as error:\n"
                    " print(json.dumps({'exception':type(error).__name__,"
                    "'elapsed':time.monotonic()-start}),flush=True)\n"
                    "else: raise AssertionError('escaped pipe was accepted')\n"
                ) % (str(BENCH / "harness-capture.py"), command, tmp,
                     60 if mode.startswith("cancel") else 0.2)
                process = subprocess.Popen(
                    [sys.executable, "-c", driver], stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE, text=True, start_new_session=True)
                try:
                    deadline = time.monotonic() + 5
                    while not child_pid.exists() and time.monotonic() < deadline:
                        time.sleep(0.01)
                    self.assertTrue(child_pid.exists(), "escaped child did not become ready")
                    escaped = int(child_pid.read_text())
                    self.assertEqual(os.getpgid(escaped), escaped, "child did not escape the group")
                    if mode.startswith("cancel"):
                        process.send_signal(signal.SIGINT)
                    stdout, stderr = process.communicate(timeout=5)
                    self.assertEqual(process.returncode, 0, stdout + stderr)
                    evidence = json.loads(stdout)
                    self.assertEqual(evidence["exception"],
                                     "KeyboardInterrupt" if mode.startswith("cancel") else "TimeoutExpired")
                    self.assertLess(evidence["elapsed"], 3, evidence)
                    os.kill(escaped, 0)  # EOF was abandoned while the escaped pipe holder was live.
                    print("CLEANUP %s: %s returned in %.3fs with escaped pipe holder live" %
                          (mode, evidence["exception"], evidence["elapsed"]), flush=True)
                finally:
                    for marker in (child_pid, command_pid):
                        if marker.exists():
                            try:
                                os.kill(int(marker.read_text()), signal.SIGKILL)
                            except ProcessLookupError:
                                pass
                    if process.poll() is None:
                        process.kill()
                    process.communicate(timeout=2)


@unittest.skipUnless(HAVE_PRODUCT, "build selected Release Compiler and Tasks, install jq")
class CompilerBackedTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="p0-initialized-test-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()
        self.ws = self.root / "workspace"
        self.out = self.root / "capture"
        self.ws.mkdir()
        self.out.mkdir()
        subprocess.run(["git", "-c", "init.templateDir=", "init", "-q", str(self.ws)],
                       check=True)
        (self.ws / "src").mkdir()
        (self.ws / "src/Src.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        self.template = self.root / "arm.csproj"
        text = (BENCH / "templates/calor-arm/CalorArm.csproj.template").read_text()
        self.template.write_text(text.replace("__REPO_ROOT__", str(REPO)).replace(
            "__CALOR_PERMISSIVE_EFFECTS__", "false"))

    def initialize(self, **kw):
        args = dict(workspace=self.ws, output=self.out, repo_root=REPO,
                    compiler=DLL, template=self.template, timeout=30)
        args.update(kw)
        return hc.initialize_workspace(**args)

    def test_real_init_and_primer_are_archived_verbatim(self):
        exemplar = self.root / "exemplar.txt"
        exemplar.write_text("control exemplar, not a measured input")
        user_home = self.root / "user-home"
        user_home.mkdir()
        user_config = user_home / ".claude.json"
        user_config.write_text('{"keep":"user-settings"}')
        with patch.dict(os.environ, HOME=str(user_home), USERPROFILE=str(user_home)):
            identity = self.initialize(exemplar=exemplar)
        self.assertEqual(user_config.read_text(), '{"keep":"user-settings"}')
        archive = self.out / "initialized-inputs"
        self.assertEqual(identity["compilerSha256"], hashlib.sha256(DLL.read_bytes()).hexdigest())
        self.assertEqual(identity["repoRoot"], str(REPO))
        self.assertTrue(identity["version"])
        for relative, digest in identity["inputs"].items():
            self.assertEqual(digest, hashlib.sha256((archive / relative).read_bytes()).hexdigest())
        for relative in ("CLAUDE.md", ".claude/settings.json", ".calor/config.json"):
            self.assertEqual((self.ws / relative).read_bytes(), (archive / relative).read_bytes())
        self.assertIn("calor://primer", (archive / "CLAUDE.md").read_text())
        self.assertIn("calor://primer", (archive / "primer.jsonl").read_text())
        self.assertEqual(exemplar.read_bytes(), (archive / "exemplar.txt").read_bytes())
        mcp = json.loads((self.out / "mcp-config.json").read_text())["mcpServers"]["calor"]
        self.assertEqual(mcp["command"], "dotnet")
        self.assertEqual(mcp["args"], [str(DLL), "mcp", "--stdio"])
        self.assertNotEqual((archive / "src/Src.csproj").read_bytes(),
                            (archive / "bound-Src.csproj").read_bytes())

    def test_existing_agent_template_is_not_overwritten(self):
        (self.ws / "CLAUDE.md").write_text("keep user template")
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.initialize()
        self.assertEqual((self.ws / "CLAUDE.md").read_text(), "keep user template")
        self.assertFalse((self.out / "initialized-inputs").exists())

    def test_existing_settings_and_missing_project_are_rejected(self):
        (self.ws / ".claude").mkdir()
        settings = self.ws / ".claude/settings.json"
        settings.write_text('{"keep":true}')
        with self.assertRaisesRegex(ValueError, "overwrite"):
            self.initialize()
        self.assertEqual(settings.read_text(), '{"keep":true}')
        settings.unlink()
        settings.parent.rmdir()
        (self.ws / "src/Src.csproj").unlink()
        with self.assertRaisesRegex(ValueError, "project"):
            self.initialize()

    def test_nested_template_or_symlink_is_rejected_before_init(self):
        nested = self.ws / "src/CLAUDE.md"
        nested.write_text("keep nested instructions")
        with self.assertRaisesRegex(ValueError, "conflicting"):
            self.initialize()
        self.assertEqual(nested.read_text(), "keep nested instructions")
        nested.unlink()
        nested.symlink_to(self.root / "outside")
        with self.assertRaisesRegex(ValueError, "conflicting"):
            self.initialize()

    def test_actual_init_exit_error_and_timeout_are_fail_closed(self):
        guard = self.root / "dotnet-guard"
        guard.mkdir()
        executable = guard / "dotnet"
        version = hc.ET.parse(REPO / "Directory.Build.props").findtext(".//Version")
        executable.write_text(
            "#!/bin/sh\ncase \"$2\" in\n"
            "--version) echo " + version + "; exit 0;;\n"
            "init) echo 'init unsupported by selected old arm' >&2; exit 23;;\n"
            "*) exit 99;;\nesac\n")
        executable.chmod(0o755)
        with patch.dict(os.environ, PATH=str(guard) + os.pathsep + os.environ["PATH"]):
            with self.assertRaisesRegex(ValueError, "23.*|unsupported"):
                self.initialize()
        shutil.rmtree(self.out / "initialized-inputs")
        executable.write_text("#!/bin/sh\nexec sleep 60\n")
        with patch.dict(os.environ, PATH=str(guard) + os.pathsep + os.environ["PATH"]):
            with self.assertRaises(subprocess.TimeoutExpired):
                self.initialize(timeout=0.1)

    def test_selected_root_binary_is_used_for_real_init_not_parent_fallback(self):
        arm = self.root / "selected-arm"
        compiler_dir = arm / "src/Calor.Compiler/bin/Release/net10.0"
        shutil.copytree(DLL.parent, compiler_dir)
        selected = compiler_dir / "calor.dll"
        tasks = arm / "src/Calor.Tasks/bin/Release/net10.0"
        tasks.mkdir(parents=True)
        shutil.copyfile(selected, tasks / "calor.dll")
        shutil.copyfile(REPO / "Directory.Build.props", arm / "Directory.Build.props")
        subprocess.run(["git", "-c", "init.templateDir=", "init", "-q", str(arm)], check=True)
        subprocess.run(["git", "-C", str(arm), "-c", "commit.gpgsign=false",
                        "-c", "user.name=Control", "-c", "user.email=control@example.invalid",
                        "commit", "--allow-empty", "-q", "-m",
                        "Disposable compiler control\n\nCo-authored-by: Copilot App "
                        "<223556219+Copilot@users.noreply.github.com>"], check=True)
        self.template.write_text(self.template.read_text().replace(str(REPO), str(arm)))
        identity = self.initialize(repo_root=arm, compiler=selected)
        self.assertEqual(identity["repoRoot"], str(arm))
        self.assertEqual(identity["compiler"], str(selected))
        self.assertEqual(identity["command"][1], str(selected))
        self.assertEqual((self.ws / "CLAUDE.md").read_bytes(),
                         (self.out / "initialized-inputs/CLAUDE.md").read_bytes())
        print("PLUMBING selected-root: real init compiler=%s version=%s" %
              (identity["compiler"], identity["version"]), flush=True)

    def test_selected_version_mismatch_is_rejected(self):
        with patch.object(hc, "_initialized_command", return_value=("0.0.0-stale", "")):
            with self.assertRaisesRegex(ValueError, "version"):
                self.initialize()

    def test_missing_primer_support_is_rejected_after_real_init(self):
        execute = hc._initialized_command
        def no_primer(command, *args, **kwargs):
            if len(command) > 2 and command[2] == "mcp":
                return '{"jsonrpc":"2.0","id":2,"error":{"code":-32601}}\n', ""
            return execute(command, *args, **kwargs)
        with patch.object(hc, "_initialized_command", no_primer):
            with self.assertRaisesRegex(ValueError, "calor://primer"):
                self.initialize()
        self.assertTrue((self.out / "initialized-inputs/CLAUDE.md").is_file())
        self.assertFalse((self.out / "initialization.json").exists())

    def test_real_raw_agent_invocation_admits_generated_mcp_and_hooks(self):
        pair = self.root / "invocation-pair"
        control_pair(pair)
        guard = self.root / "local-agent"
        guard.mkdir()
        capture = self.root / "invocation.json"
        executable = guard / "claude"
        executable.write_text(
            "#!" + sys.executable + "\n"
            "import hashlib,json,os,pathlib,shutil,subprocess,sys\n"
            "cwd=pathlib.Path.cwd(); root=cwd.parent; argv=sys.argv[1:]\n"
            "mcp_path=pathlib.Path(argv[argv.index('--mcp-config')+1])\n"
            "settings=(root/'.claude/settings.json').read_text()\n"
            "instructions=(root/'CLAUDE.md').read_text()\n"
            "shim=pathlib.Path(shutil.which('calor'))\n"
            "version=subprocess.run(['calor','--version'],capture_output=True,text=True,check=True).stdout.strip()\n"
            "hook=subprocess.run(['calor','hook','validate-write',"
            "json.dumps({'file_path':str(cwd/'Unwritten.cs'),'content':'class Unwritten {}'})],"
            "capture_output=True,text=True,timeout=10)\n"
            "record={'argv':argv,'cwd':str(cwd),'gitRoot':str(root),"
            "'hasGitRoot':(root/'.git').is_dir(),'mcp':json.loads(mcp_path.read_text()),"
            "'settings':settings,'instructions':instructions,'calorShim':str(shim),"
            "'calorShimText':shim.read_text(),'calorVersion':version,"
            "'hookExitCode':hook.returncode,'hookStderr':hook.stderr}\n"
            "pathlib.Path(" + repr(str(capture)) + ").write_text(json.dumps(record))\n"
            "reference=pathlib.Path(" + repr(str(pair / "reference/calor")) + ")\n"
            "for source in reference.iterdir(): shutil.copyfile(source,cwd/source.name)\n"
            "build=subprocess.run(['dotnet','build','--nologo','-v','q'],capture_output=True,text=True)\n"
            "if build.returncode: print(build.stdout+build.stderr,file=sys.stderr); sys.exit(build.returncode)\n"
            "print(json.dumps({'type':'result','subtype':'success','localControl':True,"
            "'result':'local invocation plumbing only; no provider',"
            "'usage':{'input_tokens':0,'output_tokens':0}}))\n")
        executable.chmod(0o755)
        output = self.root / "local-invocation"
        env = dict(os.environ, PATH=str(guard) + os.pathsep + os.environ["PATH"],
                   CALOR_TELEMETRY_OPTOUT="1", CALOR_P0_SKIP_ARM_CANARY="0")
        self.assertEqual(shutil.which("claude", path=env["PATH"]), str(executable))
        command = ["bash", str(RUNNER), "--pair", str(pair), "--arm", "calor",
                   "--initialized-workspace", "--out", str(output),
                   "--arm-repo-root", str(REPO), "--calor-dll", str(DLL)]
        run = subprocess.run(command, capture_output=True, text=True, env=env, timeout=180)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        boundary = json.loads(capture.read_text())
        run_dir = output / "N1-001-string-utils/calor+initialized/run-1"
        self.assertIn("--strict-mcp-config", boundary["argv"])
        self.assertEqual(boundary["mcp"]["mcpServers"]["calor"]["args"],
                         [str(DLL), "mcp", "--stdio"])
        self.assertTrue(boundary["hasGitRoot"])
        self.assertEqual(Path(boundary["cwd"]), Path(boundary["gitRoot"]) / "src")
        self.assertEqual(boundary["instructions"],
                         (run_dir / "initialized-inputs/CLAUDE.md").read_text())
        self.assertEqual(boundary["settings"],
                         (run_dir / "initialized-inputs/.claude/settings.json").read_text())
        self.assertIn("calor hook", boundary["settings"])
        self.assertIn(str(DLL), boundary["calorShimText"])
        self.assertEqual(Path(boundary["calorShim"]), run_dir / ".shim/calor")
        self.assertEqual(boundary["calorVersion"],
                         hc.ET.parse(REPO / "Directory.Build.props").findtext(".//Version"))
        self.assertNotEqual(boundary["hookExitCode"], 0)
        self.assertIn("BLOCKED", boundary["hookStderr"])
        self.assertIn("Calor-first", boundary["hookStderr"])
        result = json.loads((run_dir / "result.json").read_text())
        self.assertTrue(result["finalBuild"]["ok"])
        self.assertEqual(result["heldoutPassed"], 11)
        self.assertEqual(result["escapedBugs"], 0)
        self.assertTrue(json.loads((run_dir / "agent.json").read_text())["localControl"])
        print("INVOCATION calor/raw initialized: real run_agent admitted strict MCP, "
              "root instructions/settings and selected calor shim; local stub only; heldout=11",
              flush=True)

    def test_mismatched_root_compiler_and_missing_compiler_are_rejected(self):
        foreign = self.root / "calor.dll"
        shutil.copyfile(DLL, foreign)
        for compiler in (foreign, self.root / "missing.dll"):
            with self.subTest(compiler=compiler), self.assertRaisesRegex(ValueError, "selected arm"):
                self.initialize(compiler=compiler)
        with self.assertRaisesRegex(ValueError, "selected arm"):
            self.initialize(repo_root=self.root)

    def test_task_compiler_mismatch_is_rejected(self):
        with patch.object(Path, "read_bytes", autospec=True) as read:
            read.side_effect = lambda p: b"cli" if p == DLL else b"task"
            with self.assertRaisesRegex(ValueError, "Calor.Tasks"):
                self.initialize()

    def test_no_init_support_or_missing_generated_inputs_fails_closed(self):
        version = hc.ET.parse(REPO / "Directory.Build.props").findtext(".//Version")
        for response in (ValueError("old compiler has no init"), ("", "")):
            with self.subTest(response=response):
                with patch.object(hc, "_initialized_command") as command:
                    command.side_effect = [(version, ""), response]
                    with self.assertRaises(ValueError):
                        self.initialize()
                archive = self.out / "initialized-inputs"
                if archive.exists():
                    shutil.rmtree(archive)

    def test_null_runner_heldout_and_absent_flag_control(self):
        pair = self.root / "pair"
        control_pair(pair)
        guard = self.root / "guard"
        guard.mkdir()
        provider_marker = self.root / "provider-called"
        (guard / "claude").write_text(
            "#!/bin/sh\nprintf attempted > '" + str(provider_marker) + "'\nexit 99\n")
        (guard / "claude").chmod(0o755)
        env = dict(os.environ, PATH=str(guard) + os.pathsep + os.environ["PATH"],
                   CALOR_TELEMETRY_OPTOUT="1")
        old_bench = self.root / "base/bench/phase0-agent-native"
        old_bench.mkdir(parents=True)
        for path in BENCH.iterdir():
            if path.is_file() and path.suffix in (".py", ".sh"):
                shutil.copyfile(path, old_bench / path.name)
        shutil.copytree(BENCH / "templates", old_bench / "templates")
        baseline_script = subprocess.run(
            ["git", "show", BASE + ":bench/phase0-agent-native/run-pair.sh"],
            capture_output=True, text=True, check=True).stdout
        (old_bench / "run-pair.sh").write_text(baseline_script)
        self.assertEqual(
            re.search(r"^run_agent\(\) \{.*?^\}", baseline_script, re.S | re.M).group(),
            re.search(r"^run_agent\(\) \{.*?^\}", RUNNER.read_text(), re.S | re.M).group())
        results = {}
        sources = {}
        for mode in ("base", "default", "initialized"):
            initialized = mode == "initialized"
            output = self.root / (mode + "-null")
            script = old_bench / "run-pair.sh" if mode == "base" else RUNNER
            command = ["bash", str(script), "--pair", str(pair), "--arm", "calor",
                       "--null-agent", "--out", str(output), "--arm-repo-root", str(REPO),
                       "--calor-dll", str(DLL)]
            if initialized:
                command.append("--initialized-workspace")
            run_env = dict(env, CALOR_P0_SKIP_ARM_CANARY="0" if initialized else "1")
            run = subprocess.run(command, capture_output=True, text=True, env=run_env, timeout=180)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            label = "calor+initialized" if initialized else "calor"
            run_dir = output / "N1-001-string-utils" / label / "run-1"
            result = json.loads((run_dir / "result.json").read_text())
            self.assertTrue(result["nullAgent"])
            self.assertTrue(result["finalBuild"]["ok"], (run_dir / ".src_final.txt").read_text())
            self.assertGreater(result["heldoutPassed"], 0)
            self.assertEqual(result["escapedBugs"], 0, (run_dir / ".ho_final.txt").read_text())
            self.assertTrue(result["compilerHash"])
            self.assertEqual(result["armCanary"], "strict-ok" if initialized else "skipped")
            self.assertEqual(result["turns"]["assistantMessages"], 0)
            self.assertEqual(result["tokens"], {"input": 0, "output": 0})
            self.assertEqual("initialization" in result, initialized)
            print("PLUMBING %s: build=%s heldoutPassed=%s escapedBugs=%s compilerHash=%s" %
                  (label, result["finalBuild"]["ok"], result["heldoutPassed"],
                   result["escapedBugs"], result["compilerHash"]), flush=True)
            results[mode] = result
            sources[mode] = {str(p.relative_to(run_dir / "final-src")): p.read_bytes()
                             for p in (run_dir / "final-src").rglob("*") if p.is_file()}
        self.assertFalse(provider_marker.exists(), "a paid agent was invoked")
        self.assertEqual(results["default"]["compilerHash"], results["initialized"]["compilerHash"])
        for field in ("taskSuccess", "escapedBugs", "heldoutPassed", "nullAgent", "turns", "tokens"):
            self.assertEqual(results["base"][field], results["default"][field])
            self.assertEqual(results["default"][field], results["initialized"][field])
        for field in ("arm", "iterations", "iterationsToGreen", "censored", "invalid",
                      "calorDll", "armRepoRoot", "editMechanism", "compilerHash",
                      "productCompilerHash", "armConfigKey", "controlArmKind",
                      "permissiveEffects", "fixture", "templateSource", "armCanary",
                      "reference", "referenceSource", "finalBuild", "heldoutFinal",
                      "agentBuilds", "tokenUsage"):
            self.assertEqual(results["base"].get(field), results["default"].get(field), field)
        self.assertEqual(sources["base"], sources["default"])
        self.assertEqual(sources["default"], sources["initialized"])
        # The five legacy compiler-backed harness failures are driven by this
        # unchanged historical canary, not by the new arm's separate control.
        failures = []
        for script in (old_bench / "run-pair.sh", RUNNER):
            command = ["bash", str(script), "--pair", str(PAIR), "--arm", "calor",
                       "--null-agent", "--out", str(self.root / "historical-null"),
                       "--arm-repo-root", str(REPO), "--calor-dll", str(DLL)]
            run = subprocess.run(command, capture_output=True, text=True,
                                 env=dict(env, CALOR_P0_SKIP_ARM_CANARY="0"), timeout=30)
            self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
            self.assertIn("error Calor0100", run.stderr)
            failures.append(run.stderr)
        self.assertEqual(failures[0], failures[1])
        print("HISTORICAL canary: integration-base and absent-flag runner both exit 3; "
              "identical Calor0100/Calor0122 source-ID rejection", flush=True)


if __name__ == "__main__":
    unittest.main()
