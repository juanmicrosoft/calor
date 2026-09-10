"""Slow SYNTHETIC/null-agent coverage for frozen PP-W gateway controls."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


BENCH = Path(__file__).resolve().parent.parent
REPO = BENCH.parent.parent
TASKS = BENCH / "tasks/ppw-redesign"
RUN_PAIR = BENCH / "run-pair.sh"
CLIENT_DEFAULT = Path("/Users/juanrivera/.local/share/claude/versions/2.1.266")
FROZEN_COMMIT = "514f538024df990af86054af25975b756ba42ab1"
FROZEN_COMPILER_SHA256 = "8adf683d36296f92ddd6bdd414980f4ef3cca9bd16c78826621e866f1e8405d0"
TASK_NAMES = (
    "C-001-quota-adapter",
    "C-002-shipping-quote",
    "C-003-frame-fingerprint",
)
VARIANTS = ("clean", "laundering", "effectful-wrong-value")
ARMS = ("calor-permissive", "calor-strict")
SOURCE_HASH_FILES = (
    "run-pair.sh",
    "ppw-budget-gateway.py",
    "ppw-gateway-client.py",
    "ppw-run-observer.py",
    "ppw-source-assembly.py",
    "ppw-source-inspection.py",
    "ppw-test-host.py",
    "templates/calor-arm/CalorArm.Gateway.csproj.template",
    "source-inspection/Program.cs",
    "source-inspection/PpwSourceInspector.csproj",
    "test-host/Program.cs",
    "test-host/PpwXunitHost.csproj",
)


def load(filename):
    name = "frozen_controls_" + filename.replace("-", "_").replace(".", "_")
    spec = importlib.util.spec_from_file_location(name, BENCH / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


budget = load("ppw-gateway-budget.py")
gateway_module = load("ppw-budget-gateway.py")
instrument = load("ppw-instrument.py")
isolation = load("ppw-gateway-client.py")
observer_module = load("ppw-run-observer.py")
inspection = load("ppw-source-inspection.py")
source_assembly = load("ppw-source-assembly.py")
spending = load("ppw-spending.py")
test_host = load("ppw-test-host.py")


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def command(argv, **kwargs):
    return subprocess.run(
        [str(value) for value in argv], capture_output=True, text=True, **kwargs)


def tree_manifest(root):
    root = Path(root)
    return {
        path.relative_to(root).as_posix(): sha256(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def assert_private_copy(root):
    root = Path(root)
    if root.is_symlink():
        raise AssertionError("task copy root is linked")
    for path in root.rglob("*"):
        if path.is_symlink():
            raise AssertionError("task copy contains a symlink: " + str(path))
        if path.is_file() and path.stat().st_nlink != 1:
            raise AssertionError("task copy contains a hard link: " + str(path))


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


@unittest.skipUnless(
    os.environ.get("PPW_RUN_FROZEN_GATEWAY_CONTROLS") == "1",
    "set PPW_RUN_FROZEN_GATEWAY_CONTROLS=1 for the slow frozen-product integration",
)
class FrozenGatewayControlTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if sys.platform != "darwin" or not Path("/usr/bin/sandbox-exec").is_file():
            raise unittest.SkipTest("actual macOS Seatbelt is required")
        missing = [name for name in ("dotnet", "jq") if shutil.which(name) is None]
        if missing:
            raise unittest.SkipTest("required installed tools are missing: " + ", ".join(missing))

        configured_frozen = os.environ.get("PPW_FROZEN_PRODUCT_ROOT")
        cls.frozen = Path(configured_frozen) if configured_frozen else REPO.parent / "ppw-v018"
        if not cls.frozen.is_dir():
            raise unittest.SkipTest("frozen v0.18 product checkout is unavailable")
        cls.frozen = cls.frozen.resolve(strict=True)
        head = command(["git", "-C", cls.frozen, "rev-parse", "HEAD"], timeout=30)
        if head.returncode or head.stdout.strip() != FROZEN_COMMIT:
            raise unittest.SkipTest("frozen v0.18 product checkout is not at the registered commit")
        try:
            cls.product = instrument.product(cls.frozen, FROZEN_COMMIT)
        except (OSError, ValueError, subprocess.SubprocessError) as error:
            raise unittest.SkipTest("frozen v0.18 product checkout is unavailable: " + str(error))
        if cls.product["calorSha256"] != FROZEN_COMPILER_SHA256:
            raise AssertionError("frozen compiler DLL differs from the registered SHA-256")

        configured_client = os.environ.get("PPW_CLAUDE_2_1_266")
        cls.client = Path(configured_client) if configured_client else CLIENT_DEFAULT
        if not cls.client.is_file():
            raise unittest.SkipTest("retained Claude Code 2.1.266 client is unavailable")
        cls.client = cls.client.resolve(strict=True)
        if sha256(cls.client) != isolation.CLIENT_SHA256:
            raise AssertionError("retained Claude Code client bytes differ")
        version = command(
            [cls.client, "--version"],
            env=dict(os.environ, DISABLE_AUTOUPDATER="1"),
            timeout=30,
        )
        if version.returncode or version.stdout.strip() != isolation.CLIENT_VERSION:
            raise AssertionError("retained Claude Code client version differs")

        shell_value = os.environ.get("PPW_BASH5")
        cls.shell = Path(shell_value) if shell_value else Path("/opt/homebrew/bin/bash")
        if not cls.shell.is_file():
            raise unittest.SkipTest("registered Bash 5 executable is unavailable")
        cls.shell = cls.shell.resolve(strict=True)
        shell_version = command([cls.shell, "--version"], timeout=10)
        if shell_version.returncode or not shell_version.stdout.startswith("GNU bash, version 5."):
            raise unittest.SkipTest("registered Bash 5 executable is unavailable")
        cls.shell_sha256 = sha256(cls.shell)

        packages = test_host.package_root()
        try:
            test_host.dependencies(packages)
        except (OSError, ValueError) as error:
            raise unittest.SkipTest("registered xUnit packages are unavailable: " + str(error))
        cls.test_runtime = test_host.prepare(packages)
        cls.execution_runtime = isolation.runtime_identity()
        cls.inspector_runtime = inspection.prepare(cls.product["calorDll"])
        inspection.validate_runtime(cls.inspector_runtime, cls.product["calorDll"])

        cls.original_tasks_manifest = {
            name: tree_manifest(TASKS / name) for name in TASK_NAMES
        }
        cls.frozen_before = cls._frozen_identity()
        cls.source_hashes = {
            relative: sha256(BENCH / relative) for relative in SOURCE_HASH_FILES
        }

        evidence_value = os.environ.get("PPW_FROZEN_GATEWAY_EVIDENCE")
        cls.evidence_path = (
            Path(evidence_value).expanduser()
            if evidence_value
            else Path(tempfile.gettempdir()) / "ppw-frozen-gateway-controls.json"
        )
        cls.evidence_path = cls.evidence_path.resolve()
        cls.evidence_path.parent.mkdir(parents=True, exist_ok=True)
        cls.raw_root = cls.evidence_path.parent / "frozen-gateway-controls-raw"
        if cls.raw_root.exists():
            shutil.rmtree(cls.raw_root)
        cls.raw_root.mkdir()

    @classmethod
    def _frozen_identity(cls):
        files = (
            "src/Calor.Compiler/bin/Release/net10.0/calor.dll",
            "src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll",
            "src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll",
        )
        status = command(
            ["git", "-C", cls.frozen, "status", "--porcelain"], timeout=30)
        if status.returncode:
            raise AssertionError("cannot inspect frozen product checkout")
        return {
            "commit": command(
                ["git", "-C", cls.frozen, "rev-parse", "HEAD"], timeout=30
            ).stdout.strip(),
            "status": status.stdout,
            "files": {relative: sha256(cls.frozen / relative) for relative in files},
        }

    def clean_environment(self, work):
        blocked = set(isolation.BLOCKED_ENV)
        environment = {
            name: value for name, value in os.environ.items()
            if name not in blocked
            and not name.startswith(("DYLD_", "CLAUDE_CODE_USE_", "PPW_TRUSTED_"))
            and not name.startswith(("CALOR_P0_", "CALOR_LOOP_"))
            and name not in {
                "ANTHROPIC_BASE_URL", "PPW_GATEWAY_ACTIVE", "PPW_INSPECTOR_READONLY",
                "PPW_OBSERVER_URL", "PPW_XUNIT_RUNTIME", "PPW_REAL_DOTNET",
            }
        }
        dotnet_home = work / ".trusted-dotnet-home"
        dotnet_home.mkdir()
        environment.update(
            CLAUDE_MODEL="claude-opus-4-8",
            TMPDIR=str(work),
            DOTNET_CLI_HOME=str(dotnet_home),
            DOTNET_CLI_TELEMETRY_OPTOUT="1",
            DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1",
            MSBUILDDISABLENODEREUSE="1",
            NUGET_PACKAGES=self.test_runtime["packages"],
            PYTHONDONTWRITEBYTECODE="1",
        )
        return environment

    def write_context(self, path, value):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True, allow_nan=False)
            stream.write("\n")

    def copy_raw_logs(self, case_id, completed, run_directory):
        destination = self.raw_root / case_id
        destination.mkdir()
        (destination / "runner.stdout").write_text(
            completed.stdout, encoding="utf-8")
        (destination / "runner.stderr").write_text(
            completed.stderr, encoding="utf-8")
        for name in (
            "invalid.txt",
            "journal.jsonl",
            ".src_build.txt",
            ".src_final.txt",
            ".ho_last.txt",
            ".ho_final.txt",
            "agent.err",
            "policy-after.err",
            "source-inspection.err",
            "source-inspection.json",
            "result.json",
        ):
            source = run_directory / name
            if source.is_file():
                shutil.copy2(source, destination / name)

    def run_case(self, temporary, task_name, variant, arm):
        case_id = "-".join((task_name, variant, arm))
        case_root = temporary / "cases" / case_id
        copied_task = case_root / "hidden-task-copy" / task_name
        copied_task.parent.mkdir(parents=True)
        shutil.copytree(TASKS / task_name, copied_task)
        assert_private_copy(copied_task)
        pair_path = copied_task / "pair.json"
        pair = json.loads(pair_path.read_text(encoding="utf-8"))
        pair["seeded"]["clean"] = dict(pair["seeded"][variant])
        pair_path.write_text(
            json.dumps(pair, indent=2, sort_keys=True, allow_nan=False) + "\n",
            encoding="utf-8",
        )

        work = (case_root / "work").resolve()
        archive = (case_root / "archive").resolve()
        protected = (case_root / "protected").resolve()
        for path in (work, archive, protected):
            path.mkdir()
        out = archive / "runs"
        run_directory = out / task_name / arm / "run-1"
        run_directory.mkdir(parents=True)

        hidden_roots = [
            TASKS.resolve(strict=True),
            (copied_task / "tests").resolve(strict=True),
            (copied_task / "seeded").resolve(strict=True),
        ]
        copied_evidence = copied_task / "evidence"
        if copied_evidence.is_dir():
            hidden_roots.append(copied_evidence.resolve(strict=True))
        observer = RecordingObserver(
            work_root=work,
            output=run_directory,
            hidden_roots=hidden_roots,
            protected_root=protected,
            arm="calor",
            fragment_glob="*.calr.inc",
            heldout_count=pair["tests"]["count"],
            compiler=self.product["calorDll"],
            permissive=arm == "calor-permissive",
            test_runtime=self.test_runtime,
            execution_runtime=self.execution_runtime,
        )

        ledger = budget.RequestLedger(protected / "SYNTHETIC-request-ledger.sqlite3")
        ledger.initialize(
            {
                "kind": "SYNTHETIC/null-agent-control-only",
                "stage": "pilot",
                "priceSha256": budget.price_identity(),
            },
            1,
        )
        owner = ledger.start()
        upstream_attempts = []

        def refuse_upstream():
            upstream_attempts.append("refused-before-upstream")
            raise OSError("SYNTHETIC/null-agent test forbids provider connections")

        context_path = protected / "SYNTHETIC-invocation.json"
        completed = None
        isolation_evidence = None
        try:
            with gateway_module.Gateway(
                ledger,
                owner,
                case_id,
                connection_factory=refuse_upstream,
                observer=observer,
            ) as gateway:
                hidden_test = next((copied_task / "tests").glob("*.cs"))
                seeded_file = next(
                    path for path in (copied_task / pair["seeded"]["clean"]["a"]).rglob("*")
                    if path.is_file()
                )
                self.write_context(context_path, {
                    "kind": spending.GATEWAY,
                    "protectedRoot": str(protected),
                    "gatewayPid": os.getpid(),
                    "baseUrl": gateway.base_url,
                    "clientExecutable": str(self.client),
                    "shellExecutable": str(self.shell),
                    "shellSha256": self.shell_sha256,
                    "testHost": self.test_runtime,
                    "observerUrl": gateway.observer_url,
                    "observerControlUrl": gateway.observer_control_url,
                    "hiddenRoots": [str(path) for path in observer.model_hidden_roots],
                    "probeHiddenFiles": [str(hidden_test), str(seeded_file)],
                    "executionRuntime": self.execution_runtime,
                })
                runner = [
                    sys.executable,
                    BENCH / "ppw-gateway-client.py",
                    "--context", context_path,
                    "--workspace", work,
                    "--output", archive,
                    "--",
                    RUN_PAIR,
                    "--pair", copied_task,
                    "--arm", "calor",
                    "--arm-config", arm,
                    "--arm-label", arm,
                    "--arm-repo-root", self.frozen,
                    "--calor-dll", self.product["calorDll"],
                    "--edit-mechanism", "raw",
                    "--runs", "1",
                    "--run-offset", "0",
                    "--out", out,
                    "--ppw-gateway-client", self.client,
                    "--ppw-source-inspector-runtime", self.inspector_runtime["manifest"],
                    "--null-agent",
                ]
                completed = command(
                    runner,
                    cwd=REPO,
                    env=self.clean_environment(work),
                    timeout=420,
                )
                isolation_evidence = isolation.read_isolation_evidence(
                    context_path, work, archive)
        finally:
            route_calls = list(observer.route_calls)
            ledger_snapshot = ledger.snapshot()
            if completed is None:
                completed = subprocess.CompletedProcess([], -1, "", "runner did not start")
            self.copy_raw_logs(case_id, completed, run_directory)
            context_path.unlink(missing_ok=True)
            isolation.isolation_evidence_path(context_path).unlink(missing_ok=True)

        result_path = run_directory / "result.json"
        result = (
            json.loads(result_path.read_text(encoding="utf-8"))
            if result_path.is_file() else None
        )
        journal_path = run_directory / "journal.jsonl"
        journal = (
            [json.loads(line) for line in journal_path.read_text(encoding="utf-8").splitlines()]
            if journal_path.is_file() else []
        )
        source_report_path = run_directory / "source-inspection.json"
        source_inspection_ok = False
        source_inspection_error = None
        if source_report_path.is_file() and (run_directory / "final-src").is_dir():
            try:
                report = json.loads(source_report_path.read_text(encoding="utf-8"))
                inspection.validate(
                    report,
                    pair,
                    {
                        "baseline": copied_task / pair["arms"][arm]["fixture"],
                        "final": run_directory / "final-src",
                    },
                    self.product["calorSha256"],
                    self.inspector_runtime,
                )
                source_inspection_ok = True
            except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as error:
                source_inspection_error = str(error)

        return {
            "task": task_name,
            "variant": variant,
            "arm": arm,
            "runnerExitCode": completed.returncode,
            "result": result,
            "routeCalls": route_calls,
            "journal": journal,
            "sourceInspectionOk": source_inspection_ok,
            "sourceInspectionError": source_inspection_error,
            "isolation": isolation_evidence,
            "providerRequests": len(ledger_snapshot["requests"]),
            "upstreamAttempts": len(upstream_attempts),
            "ledgerState": ledger_snapshot["state"],
        }

    def materialize_parity(self, task, pair, workspace, output):
        source = workspace / "src"
        shutil.copytree(task / "starter-a", source)
        project = (BENCH / "templates/calor-arm/CalorArm.Gateway.csproj.template").read_text(
            encoding="utf-8")
        project = project.replace("__REPO_ROOT__", str(self.frozen))
        project = project.replace("__CALOR_PERMISSIVE_EFFECTS__", "true")
        (source / "Src.csproj").write_text(project, encoding="utf-8")
        source_assembly.setup(pair, source)

        heldout = output / "heldout"
        heldout.mkdir(parents=True)
        for test in (task / "tests").glob("*.cs"):
            shutil.copy2(test, heldout / test.name)
        shutil.copy2(task / "tests/shims/TestShim.calor.cs", heldout / "TestShim.cs")
        (heldout / "HeldOut.csproj").write_text(f"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Src">
      <HintPath>{source / "bin/Debug/net10.0/Src.dll"}</HintPath>
    </Reference>
    <Reference Include="Calor.Runtime">
      <HintPath>{source / "bin/Debug/net10.0/Calor.Runtime.dll"}</HintPath>
    </Reference>
  </ItemGroup>
</Project>
""", encoding="utf-8")

    def run_visible_parity(self, temporary):
        case_root = temporary / "visible-shim-parity"
        task = TASKS / TASK_NAMES[0]
        pair = json.loads((task / "pair.json").read_text(encoding="utf-8"))
        work = (case_root / "work").resolve()
        workspace = (work / "workspace").resolve()
        output = (
            case_root / "archive" / TASK_NAMES[0] / "calor-permissive" / "run-1"
        ).resolve()
        protected = (case_root / "protected").resolve()
        baseline = (case_root / "baseline-snapshot").resolve()
        work.mkdir(parents=True)
        workspace.mkdir()
        output.mkdir(parents=True)
        protected.mkdir()
        shutil.copytree(task / "starter-a", baseline)
        assert_private_copy(baseline)
        self.materialize_parity(task, pair, workspace, output)

        observer = RecordingObserver(
            work_root=work,
            output=output,
            hidden_roots=(
                TASKS.resolve(strict=True),
                (task / "tests").resolve(strict=True),
                (task / "seeded").resolve(strict=True),
            ),
            protected_root=protected,
            arm="calor",
            fragment_glob="*.calr.inc",
            heldout_count=4,
            compiler=self.product["calorDll"],
            permissive=True,
            test_runtime=self.test_runtime,
            execution_runtime=self.execution_runtime,
        )
        ledger = budget.RequestLedger(protected / "SYNTHETIC-request-ledger.sqlite3")
        ledger.initialize(
            {
                "kind": "SYNTHETIC/null-agent-visible-shim-parity",
                "stage": "pilot",
                "priceSha256": budget.price_identity(),
            },
            1,
        )
        owner = ledger.start()
        upstream_attempts = []

        def refuse_upstream():
            upstream_attempts.append("refused-before-upstream")
            raise OSError("SYNTHETIC/null-agent test forbids provider connections")

        visible_result = None
        seal_result = None
        final_result = None
        source_report = None
        source_error = None
        try:
            with gateway_module.Gateway(
                ledger,
                owner,
                "SYNTHETIC-visible-shim-parity",
                connection_factory=refuse_upstream,
                observer=observer,
            ) as gateway:
                observer_module.request(
                    gateway.observer_control_url,
                    "register",
                    {
                        "kind": observer_module.REGISTER_KIND,
                        "workspace": str(workspace),
                        "output": str(output),
                    },
                )
                for source_file in (task / "seeded/honest-a").glob("*.calr.inc"):
                    shutil.copy2(source_file, workspace / "src" / source_file.name)

                shim = workspace / ".ppw-visible-shim/dotnet"
                shim.parent.mkdir()
                real_dotnet = Path(self.execution_runtime["dotnetExecutable"])
                shim.write_text(
                    "#!/usr/bin/env bash\n"
                    "set -uo pipefail\n"
                    f"{json.dumps(str(real_dotnet))} \"$@\"\n"
                    "rc=$?\n"
                    f"{json.dumps(sys.executable)} "
                    f"{json.dumps(str(BENCH / 'ppw-run-observer.py'))} observe "
                    "--url \"$PPW_OBSERVER_URL\" --command \"${1:-build}\" "
                    "--exit-code \"$rc\" --feedback-latency-ms 0 >/dev/null || exit 2\n"
                    "exit \"$rc\"\n",
                    encoding="utf-8",
                )
                shim.chmod(0o700)
                generated_home = workspace / ".ppw-visible-home"
                generated_tmp = workspace / ".ppw-visible-tmp"
                generated_home.mkdir()
                generated_tmp.mkdir()
                environment = self.clean_environment(work)
                environment.update(
                    PPW_OBSERVER_URL=gateway.observer_url,
                    DOTNET_CLI_HOME=str(generated_home),
                    TMPDIR=str(generated_tmp),
                    PATH=str(shim.parent) + os.pathsep + environment["PATH"],
                )
                policy = isolation.sandbox_policy(
                    workspace,
                    output,
                    protected,
                    gateway.port,
                    (*observer.model_hidden_roots, baseline),
                )
                visible_result = command(
                    [
                        "/usr/bin/sandbox-exec", "-p", policy,
                        shim, "build", "--nologo", "-v", "q",
                    ],
                    cwd=workspace / "src",
                    env=environment,
                    timeout=240,
                )
                seal_result = observer_module.request(
                    gateway.observer_control_url, "seal", {})
                try:
                    source_report = observer_module.request(
                        gateway.observer_control_url,
                        "source-inspection",
                        {
                            "pair": pair,
                            "baseline": str(baseline),
                            "final": str(output / "final-src"),
                            "runtime": self.inspector_runtime,
                        },
                    )
                except (OSError, ValueError, KeyError, TypeError) as error:
                    source_error = str(error)
                final_result = observer_module.request(
                    gateway.observer_control_url, "final", {})
        finally:
            ledger_snapshot = ledger.snapshot()
            raw = self.raw_root / "visible-shim-parity"
            raw.mkdir()
            if visible_result is not None:
                (raw / "visible.stdout").write_text(
                    visible_result.stdout, encoding="utf-8")
                (raw / "visible.stderr").write_text(
                    visible_result.stderr, encoding="utf-8")
            for name in (
                "journal.jsonl", ".src_build.txt", ".ho_last.txt",
                ".src_final.txt", ".ho_final.txt",
            ):
                source = output / name
                if source.is_file():
                    shutil.copy2(source, raw / name)

        source_ok = False
        if source_report is not None:
            try:
                inspection.validate(
                    source_report,
                    pair,
                    {"baseline": baseline, "final": output / "final-src"},
                    self.product["calorSha256"],
                    self.inspector_runtime,
                )
                source_ok = True
            except (OSError, ValueError, KeyError, TypeError) as error:
                source_error = str(error)
        journal = [
            json.loads(line)
            for line in (output / "journal.jsonl").read_text(encoding="utf-8").splitlines()
        ] if (output / "journal.jsonl").is_file() else []
        return {
            "visibleExitCode": visible_result.returncode if visible_result else -1,
            "routeCalls": list(observer.route_calls),
            "seal": seal_result,
            "final": final_result,
            "sourceInspectionOk": source_ok,
            "sourceInspectionError": source_error,
            "journal": journal,
            "providerRequests": len(ledger_snapshot["requests"]),
            "upstreamAttempts": len(upstream_attempts),
            "policySha256": (
                hashlib.sha256(policy.encode()).hexdigest()
                if visible_result is not None else None
            ),
        }

    def validate_visible_parity(self, parity):
        failures = []
        if parity["visibleExitCode"] != 0:
            failures.append("visible-shim parity build failed")
        if parity["providerRequests"] != 0 or parity["upstreamAttempts"] != 0:
            failures.append("visible-shim parity attempted a provider request")
        if not {"register", "observe", "seal", "final", "source-inspection"} <= set(
                parity["routeCalls"]):
            failures.append("visible-shim parity did not exercise every typed observer route")
        if parity["seal"] != {"ok": True, "archivedFiles": 2}:
            failures.append("visible-shim parity did not seal both Calor fragments")
        final = parity["final"] or {}
        if not (
            final.get("buildOk") is True
            and final.get("heldoutPassed") == 4
            and final.get("heldoutFailed") == 0
        ):
            failures.append("visible-shim parity final grading did not pass 4/4")
        if not parity["sourceInspectionOk"]:
            failures.append(
                "visible-shim parity source inspection failed: "
                + (parity["sourceInspectionError"] or "report missing"))
        if not (
            len(parity["journal"]) == 1
            and parity["journal"][0].get("edited") is True
            and parity["journal"][0].get("heldout_pass") == 4
            and parity["journal"][0].get("heldout_fail") == 0
        ):
            failures.append("visible-shim parity observation did not record edited 4/4 state")
        return failures

    def validate_case(self, case):
        failures = []
        label = "/".join((case["task"], case["variant"], case["arm"]))
        result = case["result"]
        if case["runnerExitCode"] != 0:
            failures.append(label + ": trusted runner exited " + str(case["runnerExitCode"]))
        if case["providerRequests"] != 0 or case["upstreamAttempts"] != 0:
            failures.append(label + ": null-agent attempted a provider request")
        if not case["isolation"] or case["isolation"].get("modelInvoked") is not False:
            failures.append(label + ": trusted Seatbelt evidence is missing or claims model use")
        required_routes = {"register", "observe", "seal", "final", "source-inspection"}
        missing_routes = sorted(required_routes - set(case["routeCalls"]))
        if missing_routes:
            failures.append(label + ": missing typed observer routes " + ",".join(missing_routes))
        if not case["sourceInspectionOk"]:
            detail = case["sourceInspectionError"] or "report missing"
            failures.append(label + ": source inspection failed: " + detail)
        if result is None:
            failures.append(label + ": result.json is missing")
            return failures
        if result.get("invalid") is not False:
            failures.append(label + ": run-pair recorded an invalid control")
        if result.get("nullAgent") is not True:
            failures.append(label + ": result is not explicitly null-agent")

        edited = [record for record in case["journal"] if record.get("edited") is True]
        unedited = [record for record in case["journal"] if record.get("edited") is False]
        if not edited or not unedited or any(record.get("cmd") != "build" for record in edited):
            failures.append(
                label + ": visible shim did not observe both starter and deterministic edited builds")

        build_ok = result.get("finalBuild", {}).get("ok")
        passed = result.get("heldoutPassed")
        success = result.get("taskSuccess")
        if case["variant"] == "clean":
            if not (build_ok is True and passed == 4 and success is True):
                failures.append(label + ": clean control did not build and pass 4/4 held-out tests")
        elif case["arm"] == "calor-permissive":
            if not (build_ok is True and passed < 4 and success is False):
                failures.append(
                    label + ": permissive effect control did not build and fail held-out")
        elif not (build_ok is False and success is False):
            failures.append(label + ": strict effect control did not fail compilation")
        return failures

    def compact_case(self, case):
        result = case["result"] or {}
        edited = [record for record in case["journal"] if record.get("edited") is True]
        return {
            "task": case["task"],
            "variant": case["variant"],
            "arm": case["arm"],
            "runnerExitCode": case["runnerExitCode"],
            "invalid": result.get("invalid"),
            "nullAgent": result.get("nullAgent"),
            "buildOk": result.get("finalBuild", {}).get("ok"),
            "heldoutPassed": result.get("heldoutPassed"),
            "heldoutFailed": (
                None if result.get("heldoutPassed") is None
                else 4 - result["heldoutPassed"]
            ),
            "taskSuccess": result.get("taskSuccess"),
            "routeCalls": case["routeCalls"],
            "observations": len(case["journal"]),
            "editedObservations": len(edited),
            "sourceInspectionOk": case["sourceInspectionOk"],
            "seatbeltProbe": (
                case["isolation"].get("kernelProbe") if case["isolation"] else None
            ),
            "providerRequests": case["providerRequests"],
            "upstreamAttempts": case["upstreamAttempts"],
        }

    def test_frozen_null_agent_gateway_control_matrix(self):
        cases = []
        failures = []
        parity = None
        temporary_path = Path(tempfile.mkdtemp(
            prefix="ppw-frozen-gateway-controls-", dir="/private/tmp")).resolve()
        try:
            for task_name in TASK_NAMES:
                for variant in VARIANTS:
                    for arm in ARMS:
                        try:
                            case = self.run_case(
                                temporary_path, task_name, variant, arm)
                        except Exception as error:
                            case = {
                                "task": task_name,
                                "variant": variant,
                                "arm": arm,
                                "runnerExitCode": -1,
                                "result": None,
                                "routeCalls": [],
                                "journal": [],
                                "sourceInspectionOk": False,
                                "sourceInspectionError": (
                                    type(error).__name__ + ": " + str(error)
                                ),
                                "isolation": None,
                                "providerRequests": -1,
                                "upstreamAttempts": -1,
                                "ledgerState": "error",
                            }
                        cases.append(case)
                        failures.extend(self.validate_case(case))
            try:
                parity = self.run_visible_parity(temporary_path)
                failures.extend(self.validate_visible_parity(parity))
            except Exception as error:
                parity = {
                    "visibleExitCode": -1,
                    "routeCalls": [],
                    "seal": None,
                    "final": None,
                    "sourceInspectionOk": False,
                    "sourceInspectionError": type(error).__name__ + ": " + str(error),
                    "journal": [],
                    "providerRequests": -1,
                    "upstreamAttempts": -1,
                    "policySha256": None,
                }
                failures.append(
                    "visible-shim parity infrastructure failed: "
                    + parity["sourceInspectionError"])
        finally:
            shutil.rmtree(temporary_path, ignore_errors=True)

        original_after = {
            name: tree_manifest(TASKS / name) for name in TASK_NAMES
        }
        if original_after != self.original_tasks_manifest:
            failures.append("original frozen task bytes changed")
        frozen_after = self._frozen_identity()
        if frozen_after != self.frozen_before:
            failures.append("frozen product checkout or prebuilt binaries changed")

        compact = [self.compact_case(case) for case in cases]
        summary = {
            "cases": len(cases),
            "cleanPassed4Of4": sum(
                case["variant"] == "clean"
                and case["buildOk"] is True
                and case["heldoutPassed"] == 4
                and case["taskSuccess"] is True
                for case in compact
            ),
            "permissiveEffectBuiltFailedHeldout": sum(
                case["variant"] != "clean"
                and case["arm"] == "calor-permissive"
                and case["buildOk"] is True
                and case["heldoutPassed"] is not None
                and case["heldoutPassed"] < 4
                and case["taskSuccess"] is False
                for case in compact
            ),
            "strictEffectFailedCompilation": sum(
                case["variant"] != "clean"
                and case["arm"] == "calor-strict"
                and case["buildOk"] is False
                and case["taskSuccess"] is False
                for case in compact
            ),
            "providerRequests": (
                sum(max(0, case["providerRequests"]) for case in compact)
                + max(0, parity["providerRequests"])
            ),
            "upstreamAttempts": (
                sum(max(0, case["upstreamAttempts"]) for case in compact)
                + max(0, parity["upstreamAttempts"])
            ),
        }
        expected_summary = {
            "cases": 18,
            "providerRequests": 0,
            "upstreamAttempts": 0,
            "cleanPassed4Of4": 6,
            "permissiveEffectBuiltFailedHeldout": 6,
            "strictEffectFailedCompilation": 6,
        }
        for name, expected in expected_summary.items():
            if summary[name] != expected:
                failures.append(
                    "summary %s expected %s, got %s"
                    % (name, expected, summary[name]))
        summary["failures"] = len(failures)
        evidence = {
            "kind": "SYNTHETIC/null-agent-frozen-gateway-controls-v1",
            "empirical": False,
            "research": False,
            "modelInvoked": False,
            "compiler": {
                "commit": self.product["commit"],
                "calorSha256": self.product["calorSha256"],
                "calorTasksSha256": self.product["calorTasksSha256"],
                "prebuiltFiles": self.frozen_before["files"],
                "frozenUnchanged": frozen_after == self.frozen_before,
            },
            "client": {
                "version": isolation.CLIENT_VERSION,
                "sha256": isolation.CLIENT_SHA256,
                "inferenceInvoked": False,
            },
            "sourceHashes": self.source_hashes,
            "summary": summary,
            "visibleShimParity": {
                "visibleExitCode": parity["visibleExitCode"],
                "routeCalls": parity["routeCalls"],
                "seal": parity["seal"],
                "final": parity["final"],
                "sourceInspectionOk": parity["sourceInspectionOk"],
                "sourceInspectionError": parity["sourceInspectionError"],
                "observations": len(parity["journal"]),
                "providerRequests": parity["providerRequests"],
                "upstreamAttempts": parity["upstreamAttempts"],
                "policySha256": parity["policySha256"],
            },
            "cases": compact,
            "failures": failures,
        }
        self.evidence_path.write_text(
            json.dumps(evidence, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n",
            encoding="utf-8",
        )

        if failures:
            self.fail(
                "%d frozen gateway control failures; evidence: %s; first: %s"
                % (len(failures), self.evidence_path, failures[0])
            )


if __name__ == "__main__":
    unittest.main()
