"""Opt-in native Claude startup regression with a SYNTHETIC in-process provider."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import threading
from datetime import datetime, timezone
import unittest
from unittest.mock import patch
import uuid


BENCH = Path(__file__).resolve().parent.parent
REPO = BENCH.parent.parent
TASK = BENCH / "tasks/ppw-redesign/C-001-quota-adapter"
RUN_PAIR = BENCH / "run-pair.sh"
FROZEN = REPO.parent / "ppw-v018"
FROZEN_COMMIT = "514f538024df990af86054af25975b756ba42ab1"
COMPILER = FROZEN / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
COMPILER_SHA256 = "8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0"
CLIENT = Path("/Users/juanrivera/.local/share/claude/versions/2.1.266")
CLIENT_SHA256 = "553d1b9e9e7068b275c0a783c7e139ff6503096f286e674c8c919379fb0eca62"
BASH = Path("/opt/homebrew/Cellar/bash/5.3.15/bin/bash")
DOTNET = Path("/opt/homebrew/Cellar/dotnet/10.0.400/bin/dotnet")
PYTHON = Path(
    "/Applications/Xcode.app/Contents/Developer/Library/Frameworks/"
    "Python3.framework/Versions/3.9/bin/python3.9"
)
REGISTERED_FLAGS = [
    "--print",
    "--verbose",
    "--output-format",
    "stream-json",
    "--forward-subagent-text",
    "--dangerously-skip-permissions",
]
SLOT = "C-001-quota-adapter/calor-permissive/1"
EXPECTED_SOURCE = (
    "    §MT{mt001:Preview:pub} (i32:requested) -> i32\n"
    "      §E{}\n"
    "      §R §C{Calculate} §A requested §/C\n"
)
TOOL_COMMAND = (
    "set -euo pipefail\n"
    "# SYNTHETIC scripted native startup regression; no model inference.\n"
    "/bin/cat > task.calr.inc <<'SYNTHETIC_CALOR_EOF'\n"
    + EXPECTED_SOURCE
    + "SYNTHETIC_CALOR_EOF\n"
    "dotnet build Src.csproj --nologo -v q\n"
    'dotnet test ../smoke/Smoke.csproj --nologo -v q '
    '--logger "console;verbosity=normal"\n'
)
SOURCE_HASH_FILES = (
    "run-pair.sh",
    "ppw-budget-gateway.py",
    "ppw-gateway-budget.py",
    "ppw-gateway-client.py",
    "gateway-tools/bash-env.sh",
    "ppw-run-observer.py",
    "ppw-source-assembly.py",
    "ppw-source-inspection.py",
    "ppw-test-host.py",
)


def load(filename):
    name = "native_startup_" + filename.replace("-", "_").replace(".", "_")
    spec = importlib.util.spec_from_file_location(name, BENCH / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gateway_module = load("ppw-budget-gateway.py")
budget = gateway_module.budget
isolation = load("ppw-gateway-client.py")
observer_module = load("ppw-run-observer.py")
inspection = load("ppw-source-inspection.py")
test_host = load("ppw-test-host.py")


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def command(argv, **kwargs):
    return subprocess.run(
        [str(value) for value in argv],
        capture_output=True,
        text=True,
        **kwargs
    )


def tree_manifest(root):
    root = Path(root)
    return {
        path.relative_to(root).as_posix(): sha256(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def write_private_json(path, value):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        json.dump(value, stream, sort_keys=True, allow_nan=False)
        stream.write("\n")


def event(value):
    return ("event: %s\ndata: %s\n\n" % (
        value["type"],
        json.dumps(value, separators=(",", ":"), allow_nan=False),
    )).encode()


def usage(input_tokens, output_tokens):
    return {
        "input_tokens": input_tokens,
        "output_tokens": output_tokens,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0,
        "service_tier": "standard",
        "speed": "standard",
        "inference_geo": "global",
    }


def stream(message_id, content, stop_reason, counters):
    initial = dict(counters, output_tokens=0)
    parts = [
        event({
            "type": "message_start",
            "message": {
                "id": message_id,
                "type": "message",
                "role": "assistant",
                "content": [],
                "model": budget.MODEL,
                "stop_reason": None,
                "stop_sequence": None,
                "usage": initial,
            },
        }),
    ]
    for index, block in enumerate(content):
        if block["type"] == "tool_use":
            start = {
                "type": "tool_use",
                "id": block["id"],
                "name": block["name"],
                "input": {},
            }
            delta = {
                "type": "input_json_delta",
                "partial_json": json.dumps(
                    block["input"], separators=(",", ":"), ensure_ascii=False
                ),
            }
        else:
            start = {"type": "text", "text": ""}
            delta = {"type": "text_delta", "text": block["text"]}
        parts.extend([
            event({
                "type": "content_block_start",
                "index": index,
                "content_block": start,
            }),
            event({
                "type": "content_block_delta",
                "index": index,
                "delta": delta,
            }),
            event({"type": "content_block_stop", "index": index}),
        ])
    parts.extend([
        event({
            "type": "message_delta",
            "delta": {"stop_reason": stop_reason, "stop_sequence": None},
            "usage": {"output_tokens": counters["output_tokens"]},
        }),
        event({"type": "message_stop"}),
    ])
    return b"".join(parts)


class SyntheticResponse:
    status = 200

    def __init__(self, payload):
        self.payload = payload
        self.offset = 0

    def getheaders(self):
        return [("Content-Type", "text/event-stream")]

    def getheader(self, name, default=None):
        if name.lower() == "content-type":
            return "text/event-stream"
        return default

    def read1(self, size):
        if self.offset >= len(self.payload):
            return b""
        result = self.payload[self.offset:self.offset + size]
        self.offset += len(result)
        return result


class SyntheticConnection:
    def __init__(self, provider):
        self.provider = provider
        self.response = None

    def request(self, method, path, body=None, headers=None):
        self.response = SyntheticResponse(
            self.provider.respond(method, path, body, headers or {})
        )

    def getresponse(self):
        if self.response is None:
            raise OSError("SYNTHETIC provider request was not initialized")
        return self.response

    def close(self):
        pass


class SyntheticProvider:
    """Scripted provider transport. It retains no request body or credential value."""

    TOOL_ID = "toolu_SYNTHETIC_NATIVE_STARTUP_1"
    BOOTSTRAP_TOOL_ID = "toolu_SYNTHETIC_NATIVE_BOOTSTRAP_0"

    def __init__(self):
        self.lock = threading.Lock()
        self.requests = []
        self.failure = None

    def connection(self):
        return SyntheticConnection(self)

    @staticmethod
    def has_tool_result(messages):
        return any(
            isinstance(message, dict)
            and isinstance(message.get("content"), list)
            and any(
                isinstance(block, dict) and block.get("type") == "tool_result"
                for block in message["content"]
            )
            for message in messages
        )

    @staticmethod
    def tool_result_ids(messages):
        return [
            block.get("tool_use_id")
            for message in messages
            if isinstance(message, dict) and isinstance(message.get("content"), list)
            for block in message["content"]
            if isinstance(block, dict) and block.get("type") == "tool_result"
        ]

    def refuse(self, code):
        self.failure = code
        raise OSError("SYNTHETIC provider contract refused")

    def respond(self, method, path, raw, headers):
        with self.lock:
            ordinal = len(self.requests) + 1
            lowered = {name.lower(): value for name, value in headers.items()}
            try:
                request = json.loads(raw)
            except (TypeError, UnicodeError, json.JSONDecodeError):
                self.refuse("invalid-json")
            tools = request.get("tools", [])
            bash_tools = [
                tool for tool in tools
                if isinstance(tool, dict) and tool.get("name") == "Bash"
            ]
            tool_result = self.has_tool_result(request.get("messages", []))
            summary = {
                "ordinal": ordinal,
                "method": method,
                "path": path,
                "model": request.get("model"),
                "stream": request.get("stream"),
                "maxTokens": request.get("max_tokens"),
                "bashToolAdvertised": len(bash_tools) == 1,
                "toolResultPresent": tool_result,
                "toolResultIds": self.tool_result_ids(request.get("messages", [])),
                "browserAccessHeaderAccepted": (
                    lowered.get("anthropic-dangerous-direct-browser-access") == "true"
                ),
            }
            self.requests.append(summary)
            if (
                method != "POST"
                or path not in ("/v1/messages", "/v1/messages?beta=true")
                or request.get("model") != budget.MODEL
                or request.get("stream") is not True
                or lowered.get("anthropic-dangerous-direct-browser-access") != "true"
            ):
                self.refuse("native-request-contract")
            if ordinal == 1:
                if tool_result:
                    self.refuse("unexpected-first-tool-result")
                return stream(
                    "msg_SYNTHETIC_NATIVE_BOOTSTRAP_0",
                    [{
                        "type": "tool_use",
                        "id": self.BOOTSTRAP_TOOL_ID,
                        "name": "Bash",
                        "input": {
                            "command": "printf SYNTHETIC-native-bootstrap",
                            "description": "SYNTHETIC native bootstrap transition",
                        },
                    }],
                    "tool_use",
                    usage(800, 16),
                )
            if ordinal == 2:
                if tool_result or len(bash_tools) != 1:
                    self.refuse("native-tool-turn-contract")
                return stream(
                    "msg_SYNTHETIC_NATIVE_STARTUP_1",
                    [{
                        "type": "tool_use",
                        "id": self.TOOL_ID,
                        "name": "Bash",
                        "input": {
                            "command": TOOL_COMMAND,
                            "description": "Run SYNTHETIC native startup build and visible tests",
                        },
                    }],
                    "tool_use",
                    usage(1200, 80),
                )
            if ordinal == 3:
                if not tool_result:
                    self.refuse("missing-tool-result")
                return stream(
                    "msg_SYNTHETIC_NATIVE_STARTUP_2",
                    [{
                        "type": "text",
                        "text": (
                            "SYNTHETIC scripted provider completion; "
                            "no model inference occurred."
                        ),
                    }],
                    "end_turn",
                    usage(1600, 32),
                )
            self.refuse("unexpected-extra-request")


class RecordingObserver(observer_module.RunObserver):
    def __init__(self, **kwargs):
        super().__init__(**kwargs)
        self.route_calls = []

    def register(self, value):
        self.route_calls.append("register")
        return super().register(value)

    def observe(self, value):
        self.route_calls.append("observe")
        return super().observe(value)

    def seal(self):
        self.route_calls.append("seal")
        return super().seal()

    def final(self):
        self.route_calls.append("final")
        return super().final()

    def source_inspection(self, pair, baseline, final, runtime):
        self.route_calls.append("source-inspection")
        return super().source_inspection(pair, baseline, final, runtime)


def transcript_summary(path):
    summary = {
        "events": 0,
        "bashToolUses": 0,
        "matchingToolCommands": 0,
        "toolResults": 0,
        "toolResultErrors": 0,
        "matchingToolResultIds": 0,
        "toolResultSignals": {
            "missingPath": False,
            "permissionDenied": False,
            "toolUnavailable": False,
        },
        "visiblePassed": None,
        "visibleFailed": None,
        "visibleTotal": None,
        "resultEvents": 0,
    }
    if not path.is_file():
        return summary
    texts = []
    for line in path.read_text(encoding="utf-8").splitlines():
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        summary["events"] += 1
        if value.get("type") == "result":
            summary["resultEvents"] += 1
        message = value.get("message")
        content = message.get("content") if isinstance(message, dict) else None
        if not isinstance(content, list):
            continue
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "tool_use" and block.get("name") == "Bash":
                summary["bashToolUses"] += 1
                tool_input = block.get("input")
                if isinstance(tool_input, dict) and tool_input.get("command") == TOOL_COMMAND:
                    summary["matchingToolCommands"] += 1
            if block.get("type") == "tool_result":
                summary["toolResults"] += 1
                if block.get("is_error") is True:
                    summary["toolResultErrors"] += 1
                if block.get("tool_use_id") == SyntheticProvider.TOOL_ID:
                    summary["matchingToolResultIds"] += 1
                content_value = block.get("content", "")
                if isinstance(content_value, str):
                    texts.append(content_value)
                elif isinstance(content_value, list):
                    texts.extend(
                        item.get("text", "")
                        for item in content_value
                        if isinstance(item, dict) and isinstance(item.get("text"), str)
                    )
    combined = "\n".join(texts).lower()
    summary["toolResultSignals"] = {
        "missingPath": "no such file" in combined or "does not exist" in combined,
        "permissionDenied": "permission denied" in combined or "operation not permitted" in combined,
        "toolUnavailable": "tool is not available" in combined or "unknown tool" in combined,
    }
    summary["syntheticToolErrorLines"] = [
        re.sub(r"/[^\s:'\"]+", "<PATH>", line)
        for text in texts for line in text.splitlines()
        if any(marker in line.lower() for marker in
               ("no such file", "does not exist", "permission denied", "operation not permitted"))
    ]
    matches = [
        line
        for text in texts
        for line in text.splitlines()
        if all(label in line for label in ("Passed:", "Failed:", "Total:"))
    ]
    if matches:
        last = matches[-1]
        for name in ("Passed", "Failed", "Total"):
            match = re.search(name + r":\s*(\d+)", last)
            if match:
                summary["visible" + name] = int(match.group(1))
    return summary


@unittest.skipUnless(
    os.environ.get("PPW_RUN_GATEWAY_NATIVE_STARTUP") == "1",
    "set PPW_RUN_GATEWAY_NATIVE_STARTUP=1 for the native startup integration",
)
class NativeGatewayStartupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        required = (CLIENT, BASH, DOTNET, PYTHON, COMPILER, RUN_PAIR)
        if sys.platform != "darwin" or not Path("/usr/bin/sandbox-exec").is_file():
            raise unittest.SkipTest("actual macOS Seatbelt is required")
        if any(not path.is_file() for path in required):
            raise unittest.SkipTest("a registered native executable or frozen compiler is missing")
        if Path(sys.executable).resolve(strict=True) != PYTHON.resolve(strict=True):
            raise unittest.SkipTest("run this integration with the registered Python 3.9 executable")
        if sha256(CLIENT) != CLIENT_SHA256 or sha256(COMPILER) != COMPILER_SHA256:
            raise AssertionError("registered client or compiler bytes differ")
        if isolation.registered_client_flags() != REGISTERED_FLAGS:
            raise AssertionError("registered native client flags differ")
        head = command(["git", "-C", FROZEN, "rev-parse", "HEAD"], timeout=30)
        if head.returncode or head.stdout.strip() != FROZEN_COMMIT:
            raise unittest.SkipTest("frozen v0.18 checkout is not at the registered commit")
        if command([CLIENT, "--version"], env=dict(
                os.environ, DISABLE_AUTOUPDATER="1"), timeout=30).stdout.strip() != isolation.CLIENT_VERSION:
            raise AssertionError("registered native client version differs")

        cls.shell_sha256 = sha256(BASH)
        shell_version = command([BASH, "--version"], timeout=10)
        if shell_version.returncode or not shell_version.stdout.startswith("GNU bash, version 5."):
            raise unittest.SkipTest("registered Bash 5 executable is unavailable")

        packages = test_host.package_root()
        dependency_hashes = test_host.dependencies(packages)
        source_hashes = {
            name: sha256(BENCH / "test-host" / name)
            for name in ("Program.cs", "PpwXunitHost.csproj")
        }
        fingerprint = hashlib.sha256(json.dumps(
            {"sources": source_hashes, "dependencies": dependency_hashes},
            sort_keys=True,
        ).encode()).hexdigest()
        runtime_path = BENCH / "test-host/cache" / fingerprint / "runtime.json"
        if not runtime_path.is_file():
            raise unittest.SkipTest("prebuilt registered xUnit host is unavailable")
        cls.test_runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
        test_host.validate_runtime(cls.test_runtime)

        previous_readonly = os.environ.get("PPW_INSPECTOR_READONLY")
        os.environ["PPW_INSPECTOR_READONLY"] = "1"
        try:
            cls.inspector_runtime = inspection.prepare(COMPILER)
        finally:
            if previous_readonly is None:
                os.environ.pop("PPW_INSPECTOR_READONLY", None)
            else:
                os.environ["PPW_INSPECTOR_READONLY"] = previous_readonly
        inspection.validate_runtime(cls.inspector_runtime, COMPILER)

        cls.execution_runtime = isolation.runtime_identity()
        if Path(cls.execution_runtime["dotnetExecutable"]) != DOTNET.resolve(strict=True):
            raise unittest.SkipTest("registered dotnet executable is not first on PATH")
        cls.repository_denials = isolation.discover_sensitive_roots((REPO, FROZEN))
        cls.task_before = tree_manifest(TASK)
        frozen_status = command(
            ["git", "-C", FROZEN, "status", "--porcelain"], timeout=30)
        if frozen_status.returncode:
            raise AssertionError("cannot inspect frozen checkout")
        cls.frozen_before = frozen_status.stdout

    def environment(self, root, python_bin, probe_root):
        blocked = set(isolation.BLOCKED_ENV)
        environment = {
            name: value for name, value in os.environ.items()
            if name not in blocked
            and not name.startswith(("DYLD_", "CLAUDE_CODE_USE_", "PPW_TRUSTED_"))
            and not name.startswith(("CALOR_P0_", "CALOR_LOOP_"))
            and name not in {
                "ANTHROPIC_BASE_URL",
                "PPW_GATEWAY_ACTIVE",
                "PPW_OBSERVER_URL",
                "PPW_REAL_DOTNET",
                "PPW_NATIVE_STARTUP_EVIDENCE",
            }
        }
        dotnet_home = root / ".trusted-dotnet-home"
        dotnet_home.mkdir()
        path = [
            str(python_bin),
            str(BASH.parent),
            str(DOTNET.parent),
            "/opt/homebrew/bin",
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
        ]
        environment.update(
            CLAUDE_MODEL=budget.MODEL,
            PATH=os.pathsep.join(path),
            TMPDIR=str(root),
            DOTNET_CLI_HOME=str(dotnet_home),
            DOTNET_CLI_TELEMETRY_OPTOUT="1",
            DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1",
            MSBUILDDISABLENODEREUSE="1",
            NUGET_PACKAGES=self.test_runtime["packages"],
            PPW_INSPECTOR_READONLY="1",
            PPW_SYNTHETIC_TEMP_ROOT=str(probe_root),
            PYTHONDONTWRITEBYTECODE="1",
        )
        return environment

    def python_wrapper(self, root):
        wrapper_root = root / "SYNTHETIC-python-bin"
        wrapper_root.mkdir()
        patcher = wrapper_root / "SYNTHETIC-probe-temp-root.py"
        patcher.write_text(
            "import importlib.util,json,os,sys,tempfile\n"
            "original=tempfile.TemporaryDirectory\n"
            "def redirected(*args,**kwargs):\n"
            "    if kwargs.get('dir') == '/private/tmp':\n"
            "        kwargs['dir']=os.environ['PPW_SYNTHETIC_TEMP_ROOT']\n"
            "    return original(*args,**kwargs)\n"
            "tempfile.TemporaryDirectory=redirected\n"
            "target=sys.argv[1]\n"
            "sys.argv=sys.argv[1:]\n"
            "spec=importlib.util.spec_from_file_location('synthetic_probe_client',target)\n"
            "module=importlib.util.module_from_spec(spec)\n"
            "spec.loader.exec_module(module)\n"
            "try:\n"
            "    module.main()\n"
            "except Exception as error:\n"
            "    path=os.path.join(os.environ['PPW_SYNTHETIC_TEMP_ROOT'],"
            "'SYNTHETIC-probe-failure.json')\n"
            "    with open(path,'w',encoding='utf-8') as stream:\n"
            "        json.dump({'type':type(error).__name__,'message':str(error)},stream)\n"
            "    raise\n",
            encoding="utf-8",
        )
        wrapper = wrapper_root / "python3"
        wrapper.write_text(
            "#!/opt/homebrew/Cellar/bash/5.3.15/bin/bash\n"
            "set -euo pipefail\n"
            f"if [[ \"${{1:-}}\" == {json.dumps(str(BENCH / 'ppw-gateway-client.py'))} "
            "&& \"${2:-}\" == \"--probe\" ]]; then\n"
            f"  exec {json.dumps(str(PYTHON))} {json.dumps(str(patcher))} \"$@\"\n"
            "fi\n"
            f"exec {json.dumps(str(PYTHON))} \"$@\"\n",
            encoding="utf-8",
        )
        wrapper.chmod(0o700)
        return wrapper_root

    def test_first_paid_eligible_native_startup_uses_gateway_and_tools(self):
        evidence_value = os.environ.get("PPW_NATIVE_STARTUP_EVIDENCE")
        evidence_path = (
            Path(evidence_value).expanduser()
            if evidence_value
            else BENCH / "tests/.SYNTHETIC-native-gateway-startup-evidence-v1.json"
        ).resolve()
        evidence_path.parent.mkdir(parents=True, exist_ok=True)
        root = (
            BENCH / "tests" / (
                ".gateway-native-startup-SYNTHETIC-" + uuid.uuid4().hex
            )
        ).resolve()
        root.mkdir()
        probe_root = (
            Path.home() / ".copilot" / (
                "SYNTHETIC-ppw1432-native-probe-" + uuid.uuid4().hex[:8]
            )
        ).resolve()
        probe_root.mkdir()
        provider = SyntheticProvider()
        completed = None
        isolation_evidence = None
        routes = []
        snapshot = None
        infrastructure_failure = None
        infrastructure_detail = None
        result = None
        journal = []
        transcript = {}
        source_inspection_ok = False
        try:
            work = root / "work"
            archive = root / "archive"
            protected = root / "protected"
            for path in (work, archive, protected):
                path.mkdir()
            out = archive / "runs"
            run_directory = out / TASK.name / "calor-permissive" / "run-1"
            python_bin = self.python_wrapper(root)

            hidden_roots = [
                TASK.parent.resolve(strict=True),
                (TASK / "tests").resolve(strict=True),
                (TASK / "seeded").resolve(strict=True),
                *map(Path, self.repository_denials),
            ]
            hidden_roots = list(dict.fromkeys(hidden_roots))
            observer = RecordingObserver(
                work_root=work,
                output=run_directory,
                hidden_roots=hidden_roots,
                protected_root=protected,
                arm="calor",
                fragment_glob="*.calr.inc",
                heldout_count=4,
                compiler=COMPILER,
                permissive=True,
                test_runtime=self.test_runtime,
                execution_runtime=self.execution_runtime,
            )
            ledger = budget.RequestLedger(
                protected / "SYNTHETIC-native-startup-ledger.sqlite3"
            )
            ledger.initialize({
                "kind": "SYNTHETIC/native-first-job-startup-v1",
                "stage": "pilot",
                "epochId": "SYNTHETIC-native-startup",
                "priceSha256": budget.price_identity(),
                "plannedSlots": [SLOT],
            }, 500_000_000)
            owner = ledger.start()

            context_path = protected / "SYNTHETIC-native-startup-context.json"
            with patch.object(
                gateway_module.http.client,
                "HTTPSConnection",
                side_effect=AssertionError(
                    "SYNTHETIC native startup test forbids real upstream HTTP"
                ),
            ):
                with gateway_module.Gateway(
                    ledger,
                    owner,
                    SLOT,
                    connection_factory=provider.connection,
                    observer=observer,
                ) as gateway:
                    hidden_test = next((TASK / "tests").glob("*.cs"))
                    seeded_file = next(
                        path for path in (TASK / "seeded").rglob("*")
                        if path.is_file()
                    )
                    write_private_json(context_path, {
                        "kind": budget.KIND,
                        "protectedRoot": str(protected),
                        "gatewayPid": os.getpid(),
                        "baseUrl": gateway.base_url,
                        "clientExecutable": str(CLIENT),
                        "shellExecutable": str(BASH),
                        "shellSha256": self.shell_sha256,
                        "testHost": self.test_runtime,
                        "observerUrl": gateway.observer_url,
                        "observerControlUrl": gateway.observer_control_url,
                        "hiddenRoots": [str(path) for path in observer.model_hidden_roots],
                        "repositoryReadDenyRoots": self.repository_denials,
                        "probeHiddenFiles": [str(hidden_test), str(seeded_file)],
                        "executionRuntime": self.execution_runtime,
                    })
                    runner = [
                        PYTHON,
                        BENCH / "ppw-gateway-client.py",
                        "--context",
                        context_path,
                        "--workspace",
                        work,
                        "--output",
                        archive,
                        "--",
                        RUN_PAIR,
                        "--pair",
                        TASK,
                        "--arm",
                        "calor",
                        "--arm-config",
                        "calor-permissive",
                        "--arm-label",
                        "calor-permissive",
                        "--arm-repo-root",
                        FROZEN,
                        "--calor-dll",
                        COMPILER,
                        "--edit-mechanism",
                        "raw",
                        "--runs",
                        "1",
                        "--run-offset",
                        "0",
                        "--out",
                        out,
                        "--ppw-gateway-client",
                        CLIENT,
                        "--ppw-source-inspector-runtime",
                        self.inspector_runtime["manifest"],
                    ]
                    completed = command(
                        runner,
                        cwd=REPO,
                        env=self.environment(work, python_bin, probe_root),
                        timeout=750,
                    )
                    routes = list(observer.route_calls)
                    isolation_evidence = isolation.read_isolation_evidence(
                        context_path, work, archive
                    )
                    snapshot = ledger.snapshot()
                    if (
                        completed.returncode == 0
                        and snapshot["state"] == "collecting"
                        and snapshot["requests"]
                        and all(
                            request["state"] == "reconciled"
                            for request in snapshot["requests"]
                        )
                    ):
                        ledger.complete_slot(
                            owner, SLOT, isolation_evidence, completed.returncode
                        )
                        ledger.complete(owner)
                        snapshot = ledger.snapshot()

            result_path = run_directory / "result.json"
            if result_path.is_file():
                result = json.loads(result_path.read_text(encoding="utf-8"))
            journal_path = run_directory / "journal.jsonl"
            if journal_path.is_file():
                journal = [
                    json.loads(line)
                    for line in journal_path.read_text(encoding="utf-8").splitlines()
                ]
            transcript = transcript_summary(run_directory / "transcript.jsonl")
            report_path = run_directory / "source-inspection.json"
            final_source = run_directory / "final-src"
            if report_path.is_file() and final_source.is_dir():
                report = json.loads(report_path.read_text(encoding="utf-8"))
                pair = json.loads((TASK / "pair.json").read_text(encoding="utf-8"))
                inspection.validate(
                    report,
                    pair,
                    {"baseline": TASK / "starter-a", "final": final_source},
                    COMPILER_SHA256,
                    self.inspector_runtime,
                )
                source_inspection_ok = True
        except Exception as error:
            infrastructure_failure = type(error).__name__
            infrastructure_detail = str(error)
        finally:
            context_path = root / "protected/SYNTHETIC-native-startup-context.json"
            context_path.unlink(missing_ok=True)
            isolation.isolation_evidence_path(context_path).unlink(missing_ok=True)

            task_after = tree_manifest(TASK)
            frozen_after_result = command(
                ["git", "-C", FROZEN, "status", "--porcelain"], timeout=30)
            frozen_after = (
                frozen_after_result.stdout
                if frozen_after_result.returncode == 0
                else "status-error"
            )
            requests = snapshot["requests"] if snapshot else []
            accounting = []
            for request in requests:
                receipt = (
                    json.loads(request["usage"])
                    if isinstance(request.get("usage"), str)
                    else None
                )
                accounting.append({
                    "state": request.get("state"),
                    "chargeMicroUsd": request.get("charge"),
                    "receipt": receipt,
                })
            run_directory = (
                root / "archive/runs/C-001-quota-adapter/calor-permissive/run-1"
            )
            final_task = run_directory / "final-src/task.calr.inc"
            evidence = {
                "schemaVersion": 1,
                "kind": "SYNTHETIC/native-first-job-gateway-startup-v1",
                "generatedAt": datetime.now(timezone.utc).isoformat(),
                "empirical": False,
                "research": False,
                "modelInvoked": False,
                "provider": {
                    "kind": "SYNTHETIC/in-process-scripted-provider-v1",
                    "realUpstreamGuardInstalled": True,
                    "requestCount": len(provider.requests),
                    "requests": provider.requests,
                    "failure": provider.failure,
                    "scriptSha256": hashlib.sha256(
                        TOOL_COMMAND.encode("utf-8")
                    ).hexdigest(),
                },
                "invocation": {
                    "test": (
                        "test_ppw_gateway_native_startup."
                        "NativeGatewayStartupTests."
                        "test_first_paid_eligible_native_startup_uses_gateway_and_tools"
                    ),
                    "slot": SLOT,
                    "model": budget.MODEL,
                    "clientFlags": REGISTERED_FLAGS,
                    "runnerExitCode": (
                        completed.returncode if completed is not None else None
                    ),
                    "infrastructureFailure": infrastructure_failure,
                    "infrastructureDetail": (
                        infrastructure_detail
                        .replace(str(root), "<SYNTHETIC_ROOT>")
                        .replace(str(REPO), "<REPOSITORY>")
                        .replace(str(Path.home()), "<HOME>")
                        if infrastructure_detail else None
                    ),
                    "runnerSignals": ({
                        "isolationRefused": (
                            "PP-W gateway isolation refused invocation"
                            in completed.stderr
                        ),
                        "invalidRun": "INVALID run detected" in completed.stderr,
                        "sourceInspectionFailed": (
                            "Source inspection failed" in completed.stderr
                        ),
                    } if completed is not None else None),
                },
                "pins": {
                    "frozenCommit": FROZEN_COMMIT,
                    "compilerSha256": COMPILER_SHA256,
                    "clientVersion": isolation.CLIENT_VERSION,
                    "clientSha256": CLIENT_SHA256,
                    "bashSha256": self.shell_sha256,
                    "dotnetSha256": self.execution_runtime["dotnetSha256"],
                    "pythonSha256": self.execution_runtime["pythonSha256"],
                },
                "sourceHashes": {
                    name: sha256(BENCH / name) for name in SOURCE_HASH_FILES
                },
                "testSha256": sha256(Path(__file__)),
                "accounting": {
                    "state": snapshot.get("state") if snapshot else None,
                    "exposureMicroUsd": (
                        snapshot.get("exposureMicroUsd") if snapshot else None
                    ),
                    "requests": accounting,
                },
                "nativeToolExecution": transcript,
                "observer": {
                    "routes": routes,
                    "journal": [{
                        "command": record.get("cmd"),
                        "edited": record.get("edited"),
                        "exitCode": record.get("exit"),
                        "heldoutPassed": record.get("heldout_pass"),
                        "heldoutFailed": record.get("heldout_fail"),
                    } for record in journal],
                    "sourceInspectionOk": source_inspection_ok,
                },
                "final": {
                    "invalid": result.get("invalid") if result else None,
                    "nullAgent": result.get("nullAgent") if result else None,
                    "buildOk": (
                        result.get("finalBuild", {}).get("ok")
                        if result else None
                    ),
                    "heldoutPassed": (
                        result.get("heldoutPassed") if result else None
                    ),
                    "taskSuccess": result.get("taskSuccess") if result else None,
                    "sourceMatchesScript": (
                        final_task.is_file()
                        and final_task.read_text(encoding="utf-8") == EXPECTED_SOURCE
                    ),
                },
                "containment": {
                    "isolation": (
                        dict(
                            isolation_evidence,
                            workspaceRoot="<SYNTHETIC_WORKSPACE>",
                            authoritativeRoot="<SYNTHETIC_ARCHIVE>",
                        )
                        if isolation_evidence else None
                    ),
                    "probeScratchRedirectedToOwnedSyntheticRoot": True,
                    "taskAssetsUnchanged": task_after == self.task_before,
                    "frozenCheckoutUnchanged": frozen_after == self.frozen_before,
                },
                "blockingIssue": (
                    {
                        "code": "NATIVE_BASH_TEMP_ROOT_DENIED",
                        "requiredSourceFix": (
                            "Set CLAUDE_CODE_TMPDIR to the existing "
                            "workspace/.ppw-client-tmp directory in "
                            "ppw-gateway-client.py client_environment()."
                        ),
                    }
                    if transcript.get("toolResultSignals", {}).get(
                        "permissionDenied"
                    ) else None
                ),
            }
            evidence["outcome"] = (
                "passed"
                if (
                    infrastructure_failure is None
                    and provider.failure is None
                    and completed is not None
                    and completed.returncode == 0
                    and transcript.get("toolResultErrors") == 0
                    and result is not None
                    and result.get("invalid") is False
                    and result.get("finalBuild", {}).get("ok") is True
                    and result.get("heldoutPassed") == 4
                    and result.get("taskSuccess") is True
                    and source_inspection_ok
                )
                else "failed"
            )
            evidence_path.write_text(
                json.dumps(
                    evidence,
                    sort_keys=True,
                    separators=(",", ":"),
                    allow_nan=False,
                ) + "\n",
                encoding="utf-8",
            )
            shutil.rmtree(root, ignore_errors=True)
            shutil.rmtree(probe_root, ignore_errors=True)

        self.assertIsNone(
            infrastructure_failure,
            "native startup integration infrastructure failed; inspect sanitized evidence",
        )
        self.assertIsNotNone(completed, "native runner did not start")
        self.assertEqual(
            0,
            completed.returncode,
            "native runner failed; verbose output was intentionally suppressed",
        )
        self.assertIsNone(provider.failure, "synthetic provider contract failed")
        self.assertEqual(3, len(provider.requests), "native request count differs")
        self.assertTrue(all(
            request["browserAccessHeaderAccepted"] for request in provider.requests
        ), "native browser-access header was not admitted and forwarded")
        self.assertEqual(
            [False, False, True],
            [request["toolResultPresent"] for request in provider.requests],
            "native Bash tool result did not produce the second provider turn",
        )
        self.assertEqual("complete", snapshot["state"], "synthetic ledger did not complete")
        self.assertEqual(
            ["reconciled", "reconciled", "reconciled"],
            [request["state"] for request in snapshot["requests"]],
            "provider usage was not fully reconciled",
        )
        self.assertEqual(1, transcript["bashToolUses"], "native Bash tool did not execute")
        self.assertEqual(
            1, transcript["matchingToolCommands"], "native Bash tool command changed"
        )
        self.assertGreaterEqual(
            transcript["toolResults"], 1, "native Bash tool result is missing"
        )
        self.assertEqual(
            0,
            transcript["toolResultErrors"],
            "scripted native Bash build/test failed; inspect sanitized evidence",
        )
        self.assertEqual(
            ["build", "test"],
            [record.get("cmd") for record in journal],
            "registered dotnet shim did not observe build then visible test",
        )
        self.assertEqual(
            [True, False],
            [record.get("edited") for record in journal],
            "observer edit attribution differs",
        )
        self.assertTrue(all(
            record.get("exit") == 0
            and record.get("heldout_pass") == 4
            and record.get("heldout_fail") == 0
            for record in journal
        ), "observer did not authoritatively pass held-out tests after each tool command")
        self.assertTrue(
            {"register", "observe", "seal", "source-inspection", "final"}
            <= set(routes),
            "trusted observer routes were not all exercised",
        )
        self.assertTrue(source_inspection_ok, "final source inspection failed")
        self.assertIsNotNone(result, "result.json is missing")
        self.assertIs(result.get("invalid"), False, "run-pair marked the native run invalid")
        self.assertIs(result.get("nullAgent"), False, "native run was marked null-agent")
        self.assertIs(
            result.get("finalBuild", {}).get("ok"),
            True,
            "authoritative final build failed",
        )
        self.assertEqual(4, result.get("heldoutPassed"), "final held-out grading differs")
        self.assertIs(result.get("taskSuccess"), True, "native task did not succeed")
        self.assertEqual(self.task_before, tree_manifest(TASK), "frozen task assets changed")
        self.assertEqual(
            self.frozen_before,
            command(["git", "-C", FROZEN, "status", "--porcelain"], timeout=30).stdout,
            "frozen product checkout changed",
        )


if __name__ == "__main__":
    unittest.main()
