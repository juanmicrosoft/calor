#!/usr/bin/env python3
"""Launch the trusted runner and isolate its model/generated-code children with Seatbelt."""
import argparse
import _sqlite3
import _ssl
from contextlib import ExitStack
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import plistlib
import secrets
import signal
import shutil
import socket
import sqlite3
import ssl
import subprocess
import sys
import tempfile
import time
from urllib.parse import urlsplit

CLIENT_VERSION = "2.1.266 (Claude Code)"
CLIENT_SHA256 = "553d1b9e9e7068b275c0a783c7e139ff6503096f286e674c8c919379fb0eca62"
ISOLATION = "macos-seatbelt-request-gateway-v1"
PROBE_EXPECTATIONS = {
    "gateway": True, "otherPort": False, "workspaceWrite": True,
    "authoritativeWrite": False, "authoritativeRead": False,
    "hiddenTestRead": False, "seededSolutionRead": False,
    "stateWrite": False, "stateRead": False,
    "outsideSignal": False, "hardlinkWrite": False, "symlinkWrite": False,
    "samePortIpv6": False,
    "unixSocket": False, "delegatedPreferencesWrite": False,
    "readableSourceRead": True, "readableSourceHardlinkWrite": False,
}
BLOCKED_ENV = {
    "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_OAUTH_TOKEN",
    "ANTHROPIC_CUSTOM_HEADERS", "CLAUDE_CONFIG_DIR", "CLAUDE_CODE_USE_BEDROCK",
    "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY", "CLAUDE_CODE_USE_AWS",
    "NODE_OPTIONS", "BUN_OPTIONS", "BASH_ENV", "ENV", "SSL_CERT_FILE", "SSL_CERT_DIR",
    "SSLKEYLOGFILE", "NODE_DEBUG", "NODE_DEBUG_NATIVE",
}


def require(condition, message):
    if not condition:
        raise ValueError("PP-W gateway isolation: " + message)


def canonical_path(value):
    path = Path(value)
    require(path.is_absolute() and ".." not in path.parts and not any(ord(c) < 32 for c in str(path)),
            "canonical absolute path required")
    require(not any(p.is_symlink() for p in (path, *path.parents)), "linked path refused")
    return path


def contained(path, root):
    return path == root or root in path.parents


def narrow_read_denials(root, readable):
    allowed = tuple(path for path in readable if contained(path, root))
    if not allowed:
        return (root,)
    denials = []
    frontier = [root]
    while frontier:
        parent = frontier.pop()
        require(parent.is_dir(), "readable child exception requires an existing directory ancestry")
        for child in parent.iterdir():
            descendants = tuple(path for path in allowed if contained(path, child))
            if not descendants:
                denials.append(child)
            elif child not in allowed:
                frontier.append(child)
    return tuple(denials)


def validate_client(path):
    validate_environment()
    path = canonical_path(path)
    require(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == CLIENT_SHA256,
            "client bytes differ from the registered 2.1.266 binary")
    result = subprocess.run([str(path), "--version"], text=True, capture_output=True, timeout=30,
                            env=dict(os.environ, DISABLE_AUTOUPDATER="1"))
    require(result.returncode == 0 and result.stdout.strip() == CLIENT_VERSION,
            "registered client version differs")
    return path


def validate_environment():
    require(not any(os.environ.get(name) for name in BLOCKED_ENV)
            and not any(name.startswith(("DYLD_", "CLAUDE_CODE_USE_")) and value
                        for name, value in os.environ.items()),
            "unregistered credential/provider/injection environment override")


def validate_platform():
    require(platform.system() == "Darwin" and Path("/usr/bin/sandbox-exec").is_file(),
            "the implemented isolation platform is macOS sandbox-exec")


def validate_shell(path, sha256):
    path = canonical_path(path)
    require(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == sha256,
            "registered shell bytes differ")
    validate_environment()
    result = subprocess.run([str(path), "--version"], capture_output=True, text=True, timeout=10)
    require(result.returncode == 0 and result.stdout.startswith("GNU bash, version 5."),
            "the isolated runner requires an existing registered Bash 5 shell")
    return path


def runtime_identity():
    python = Path(sys.executable).resolve(strict=True)
    dotnet_command = shutil.which("dotnet")
    require(dotnet_command is not None, "the registered .NET runtime is required")
    dotnet = Path(dotnet_command).resolve(strict=True)
    versions = []
    for option in ("--version", "--list-runtimes"):
        result = subprocess.run([str(dotnet), option], capture_output=True, text=True, timeout=30)
        require(result.returncode == 0 and result.stdout.strip(), "cannot identify the .NET runtime")
        versions.append(result.stdout.strip())
    runtimes = [line.split()[:2] for line in versions[1].splitlines()]
    require(all(len(value) == 2 for value in runtimes), "unrecognized .NET runtime inventory")
    return {
        "pythonExecutable": str(python), "pythonSha256": hashlib.sha256(python.read_bytes()).hexdigest(),
        "pythonVersion": list(sys.version_info[:3]),
        "dotnetExecutable": str(dotnet), "dotnetSha256": hashlib.sha256(dotnet.read_bytes()).hexdigest(),
        "dotnetSdkVersion": versions[0], "dotnetRuntimes": runtimes,
        "opensslVersion": ssl.OPENSSL_VERSION, "sqliteVersion": sqlite3.sqlite_version,
        "nativeModules": {name: hashlib.sha256(Path(value.__file__).read_bytes()).hexdigest()
                          for name, value in (("_ssl", _ssl), ("_sqlite3", _sqlite3))},
        "os": platform.system(), "osRelease": platform.release(), "architecture": platform.machine(),
    }


def validate_runtime(expected):
    require(isinstance(expected, dict) and runtime_identity() == expected,
            "Python/TLS/SQLite/.NET/OS runtime identity differs from the registered execution profile")


def sandbox_policy(workspace, authoritative, protected, port, hidden_roots=(), *,
                   network=True, readable=(), writable=()):
    workspace, authoritative, protected = map(
        canonical_path, (workspace, authoritative, protected))
    hidden_roots = tuple(canonical_path(path) for path in hidden_roots)
    readable = tuple(canonical_path(path) for path in readable)
    writable = tuple(canonical_path(path) for path in writable)
    require(type(port) is int and 1 <= port <= 65535, "invalid gateway port")
    require(not contained(protected, workspace) and not contained(workspace, protected)
            and not contained(authoritative, workspace) and not contained(workspace, authoritative),
            "writable workspace overlaps authoritative or protected state")
    require(all(not contained(path, authoritative) and not contained(authoritative, path)
                and not contained(path, protected) and not contained(protected, path)
                and not any(contained(path, hidden) or contained(hidden, path)
                            for hidden in hidden_roots)
                for path in writable),
            "additional writable path overlaps protected, hidden, or authoritative state")
    require(all(not contained(hidden, path) for path in readable for hidden in hidden_roots)
            and all(not contained(authoritative, path) and not contained(protected, path)
                    for path in readable),
            "readable path must be a narrow child exception, not a protected ancestor")
    quote = lambda path: json.dumps(str(path))
    rules = [
        "(version 1)",
        "(allow default)",
        "(deny network*)",
        "(deny file-write*)",
        '(allow file-write* (literal "/dev/null"))',
        "(allow file-write* (subpath %s))" % quote(workspace),
    ]
    denied_roots = (authoritative, protected, *hidden_roots)
    rules.extend("(deny file-read* (subpath %s))" % quote(path)
                 for root in denied_roots for path in narrow_read_denials(root, readable))
    rules.extend("(allow file-read* (subpath %s))" % quote(path) for path in readable)
    rules.extend("(allow file-write* (subpath %s))" % quote(path) for path in writable)
    if network:
        rules.append('(allow network-outbound (remote tcp4 "localhost:%d"))' % port)
    rules.extend([
        "(deny signal)",
        "(allow signal (target same-sandbox))",
        "(deny process-info*)",
        "(allow process-info* (target same-sandbox))",
        "(deny mach-priv-task-port)",
        "(allow mach-priv-task-port (target same-sandbox))",
        "(deny appleevent-send)",
        "(deny lsopen)",
        "(deny mach-lookup)",
        '(allow mach-lookup (global-name "com.apple.securityd.xpc")'
        ' (global-name "com.apple.SecurityServer")'
        ' (global-name "com.apple.cfprefsd.agent")'
        ' (global-name "com.apple.cfprefsd.daemon")'
        ' (global-name "com.apple.logd"))',
        "(deny mach-register)",
        "(deny ipc-posix-shm*)",
        "(deny ipc-posix-sem*)",
    ])
    return "\n".join(rules) + "\n"


def kernel_probe(policy, workspace, authoritative, protected, hidden_files, gateway_port, gateway_pid):
    """Cost-free live enforcement checks, not a caller's asserted JSON capability."""
    validate_platform()
    with ExitStack() as resources:
        listener = resources.enter_context(socket.socket())
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        ipv6_listener = resources.enter_context(socket.socket(socket.AF_INET6))
        ipv6_listener.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 1)
        ipv6_listener.bind(("::1", gateway_port))
        ipv6_listener.listen()
        # AF_UNIX has a short pathname limit; deep worktrees cannot hold this
        # sentinel. This private, disposable directory never holds user state.
        ipc_root = Path(resources.enter_context(tempfile.TemporaryDirectory(prefix="ppw-ipc-", dir="/private/tmp")))
        unix_listener = resources.enter_context(socket.socket(socket.AF_UNIX))
        unix_path = ipc_root / "probe.sock"
        unix_listener.bind(str(unix_path))
        unix_listener.listen()
        readable_source = ipc_root / "readable-source-sentinel"
        readable_source.write_text("UNCHANGED")
        require(readable_source.stat().st_dev == workspace.stat().st_dev,
                "source-alias probe requires the workspace filesystem")
        sentinel = protected / "kernel-probe-sentinel"
        sentinel.write_text("UNCHANGED")
        resources.callback(sentinel.unlink, missing_ok=True)
        preferences = protected / "kernel-preferences-sentinel.plist"
        preferences_bytes = plistlib.dumps({"value": "UNCHANGED"})
        preferences.write_bytes(preferences_bytes)
        resources.callback(preferences.unlink, missing_ok=True)
        alias_prefix = workspace / ("state-alias-probe-" + secrets.token_hex(8))
        workspace_sentinel = workspace / ("workspace-write-probe-" + secrets.token_hex(8))
        resources.callback(workspace_sentinel.unlink, missing_ok=True)
        authoritative_sentinel = authoritative / ("authoritative-probe-" + secrets.token_hex(8))
        authoritative_sentinel.write_text("UNCHANGED")
        resources.callback(authoritative_sentinel.unlink, missing_ok=True)
        hidden_test, seeded_solution = map(canonical_path, hidden_files)
        for suffix in ("hardlink", "symlink", "source"):
            resources.callback(Path(str(alias_prefix) + "-" + suffix).unlink, missing_ok=True)
        script = """
import json,os,socket,subprocess,sys
from pathlib import Path
result={}
for label,address,port in (("gateway","127.0.0.1",int(sys.argv[1])),
                           ("otherPort","127.0.0.1",int(sys.argv[2])),
                           ("samePortIpv6","::1",int(sys.argv[1]))):
    try:
        with socket.create_connection((address,port),timeout=2): pass
        result[label]=True
    except OSError: result[label]=False
try: Path(sys.argv[3]).write_text("CHANGED");result["stateWrite"]=True
except OSError: result["stateWrite"]=False
try: Path(sys.argv[3]).read_text();result["stateRead"]=True
except OSError: result["stateRead"]=False
try: Path(sys.argv[9]).write_text("WORKS");result["workspaceWrite"]=True
except OSError: result["workspaceWrite"]=False
try: Path(sys.argv[10]).write_text("CHANGED");result["authoritativeWrite"]=True
except OSError: result["authoritativeWrite"]=False
try: Path(sys.argv[10]).read_text();result["authoritativeRead"]=True
except OSError: result["authoritativeRead"]=False
try: Path(sys.argv[11]).read_text();result["hiddenTestRead"]=True
except OSError: result["hiddenTestRead"]=False
try: Path(sys.argv[12]).read_text();result["seededSolutionRead"]=True
except OSError: result["seededSolutionRead"]=False
try: os.kill(int(sys.argv[4]),0);result["outsideSignal"]=True
except OSError: result["outsideSignal"]=False
for kind in ("hardlink","symlink"):
    target=Path(sys.argv[5]+"-"+kind)
    try:
        if kind=="hardlink": os.link(sys.argv[3],target)
        else: target.symlink_to(sys.argv[3])
        target.write_text("CHANGED")
        result[kind+"Write"]=True
    except OSError: result[kind+"Write"]=False
with socket.socket(socket.AF_UNIX) as client:
    try:
        client.connect(sys.argv[6])
        result["unixSocket"]=True
    except OSError: result["unixSocket"]=False
preferences=subprocess.run(["/usr/bin/defaults","write",sys.argv[7],"value","CHANGED"],
                           capture_output=True,timeout=10)
result["delegatedPreferencesWrite"]=preferences.returncode==0
try: result["readableSourceRead"]=Path(sys.argv[8]).read_text()=="UNCHANGED"
except OSError: result["readableSourceRead"]=False
try:
    source_alias=Path(sys.argv[5]+"-source")
    os.link(sys.argv[8],source_alias)
    source_alias.write_text("CHANGED")
    result["readableSourceHardlinkWrite"]=True
except OSError: result["readableSourceHardlinkWrite"]=False
print(json.dumps(result))
"""
        result = subprocess.run(
            ["/usr/bin/sandbox-exec", "-p", policy, sys.executable, "-c", script,
             str(gateway_port), str(listener.getsockname()[1]), str(sentinel), str(gateway_pid),
             str(alias_prefix), str(unix_path), str(preferences.with_suffix("")), str(readable_source),
             str(workspace_sentinel), str(authoritative_sentinel),
             str(hidden_test), str(seeded_solution)],
            cwd=workspace, text=True, capture_output=True, timeout=15)
        require(result.returncode == 0, "kernel isolation probe could not execute")
        observed = json.loads(result.stdout)
        require(observed == PROBE_EXPECTATIONS
                and sentinel.read_text() == "UNCHANGED"
                and authoritative_sentinel.read_text() == "UNCHANGED"
                and preferences.read_bytes() == preferences_bytes,
                "kernel isolation controls did not hold")
        workspace_sentinel.unlink()
        require(readable_source.read_text() == "UNCHANGED", "readable source was modified through an alias")
        return {"kind": ISOLATION, "kernelProbe": observed, "modelInvoked": False}


def isolation_evidence_path(context_path):
    return canonical_path(context_path).with_suffix(".isolation.json")


def write_isolation_evidence(context_path, evidence):
    path = isolation_evidence_path(context_path)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        json.dump(evidence, stream, sort_keys=True, allow_nan=False)
        stream.write("\n")


def read_isolation_evidence(context_path, workspace, output):
    path = canonical_path(isolation_evidence_path(context_path))
    require(path.is_file() and path.stat().st_mode & 0o077 == 0 and path.stat().st_nlink == 1,
            "private, unlinked isolation evidence required")
    context = json.loads(Path(context_path).read_text())
    evidence = json.loads(path.read_text())
    require(isinstance(evidence, dict) and set(evidence) ==
            {"kind", "kernelProbe", "modelInvoked", "clientSha256", "policySha256",
             "workspaceRoot", "authoritativeRoot"},
            "isolation evidence fields differ")
    workspace_root = canonical_path(evidence["workspaceRoot"])
    authoritative = canonical_path(evidence["authoritativeRoot"])
    require(contained(workspace_root, canonical_path(workspace))
            and authoritative == canonical_path(output), "isolation evidence paths differ")
    policy = sandbox_policy(workspace_root, authoritative, context["protectedRoot"],
                            urlsplit(context["baseUrl"]).port, context["hiddenRoots"])
    require(evidence["kind"] == ISOLATION and evidence["kernelProbe"] == PROBE_EXPECTATIONS
            and evidence["modelInvoked"] is False and evidence["clientSha256"] == CLIENT_SHA256
            and evidence["policySha256"] == hashlib.sha256(policy.encode()).hexdigest(),
            "isolation evidence does not bind this invocation's enforced policy")
    return evidence


def registered_client_flags():
    return ["--print", "--verbose", "--output-format", "stream-json",
            "--forward-subagent-text", "--dangerously-skip-permissions"]


def client_environment(workspace, base_url):
    workspace = canonical_path(workspace)
    temporary = workspace / ".ppw-client-tmp"
    temporary.mkdir(exist_ok=True)
    dotnet_home = workspace / ".ppw-dotnet-home"
    dotnet_home.mkdir(exist_ok=True)
    inherited = {name: value for name, value in os.environ.items()
                 if not name.startswith("PPW_TRUSTED_")}
    return dict(inherited, ANTHROPIC_BASE_URL=base_url, DISABLE_AUTOUPDATER="1",
                TMPDIR=str(temporary), DOTNET_CLI_HOME=str(dotnet_home),
                DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1", MSBUILDDISABLENODEREUSE="1",
                GIT_OPTIONAL_LOCKS="0", PPW_INSPECTOR_READONLY="1", PPW_GATEWAY_ACTIVE="1",
                PPW_PYTHON_EXECUTABLE=sys.executable,
                PATH=str(Path(__file__).resolve().parent / "gateway-tools") + os.pathsep + os.environ["PATH"])


def load_context(context_path):
    context_path = canonical_path(context_path)
    require(context_path.is_file() and context_path.stat().st_mode & 0o077 == 0,
            "private gateway context required")
    context = json.loads(context_path.read_text())
    require(context.get("kind") == "pp-w-request-gateway-v1", "wrong gateway context")
    protected = canonical_path(context["protectedRoot"])
    require(contained(context_path, protected), "context is outside protected state")
    return context_path, context, protected


def terminate_process_group(process, grace=2):
    def members():
        result = subprocess.run(
            ["/bin/ps", "-axo", "pid=,pgid=,stat="],
            capture_output=True, text=True, timeout=10)
        require(result.returncode == 0, "cannot inspect generated descendant process group")
        return [int(line.split()[0]) for line in result.stdout.splitlines()
                if len(line.split()) == 3 and int(line.split()[1]) == process.pid
                and not line.split()[2].startswith("Z")]

    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        process.wait(timeout=grace)
        return
    deadline = time.monotonic() + grace
    while members() and time.monotonic() < deadline:
        time.sleep(0.02)
    if members():
        os.killpg(process.pid, signal.SIGKILL)
        deadline = time.monotonic() + grace
        while members() and time.monotonic() < deadline:
            time.sleep(0.02)
    require(not members(), "generated descendant process group survived termination")
    process.wait(timeout=grace)


def execute_generated(workspace, authoritative, protected, hidden_roots, command, *,
                      cwd, environment, timeout, readable=(), writable=(), input_text=None,
                      started=None, finished=None):
    workspace, authoritative, protected = map(
        canonical_path, (workspace, authoritative, protected))
    port = 1
    policy = sandbox_policy(workspace, authoritative, protected, port, hidden_roots,
                            network=False, readable=readable, writable=writable)
    argv = ["/usr/bin/sandbox-exec", "-p", policy] + [str(value) for value in command]
    process = subprocess.Popen(
        argv, cwd=cwd, env=environment, stdin=subprocess.PIPE if input_text is not None else None,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
    try:
        if started is not None:
            started(process)
        stdout, stderr = process.communicate(input=input_text, timeout=timeout)
        returncode = process.returncode
        terminate_process_group(process)
        return subprocess.CompletedProcess(argv, returncode, stdout, stderr)
    except subprocess.TimeoutExpired:
        terminate_process_group(process)
        process.communicate()
        raise
    except Exception:
        terminate_process_group(process)
        process.communicate()
        raise
    finally:
        if finished is not None:
            finished(process)


def probe(context_path, workspace, authoritative):
    context_path, context, protected = load_context(context_path)
    endpoint = urlsplit(context["baseUrl"])
    policy = sandbox_policy(workspace, authoritative, protected, endpoint.port, context["hiddenRoots"])
    evidence = kernel_probe(policy, canonical_path(workspace), canonical_path(authoritative), protected,
                            context["probeHiddenFiles"], endpoint.port, context["gatewayPid"])
    evidence.update(clientSha256=CLIENT_SHA256, policySha256=hashlib.sha256(policy.encode()).hexdigest(),
                    workspaceRoot=str(canonical_path(workspace)),
                    authoritativeRoot=str(canonical_path(authoritative)))
    write_isolation_evidence(context_path, evidence)
    return evidence


def execute_client(context_path, workspace, authoritative, arguments):
    _, context, protected = load_context(context_path)
    client = validate_client(context["clientExecutable"])
    require(arguments and canonical_path(arguments[0]) == client,
            "only the registered client may enter the model sandbox")
    endpoint = urlsplit(context["baseUrl"])
    policy = sandbox_policy(workspace, authoritative, protected, endpoint.port, context["hiddenRoots"])
    environment = client_environment(workspace, context["baseUrl"])
    environment.update(NUGET_PACKAGES=context["testHost"]["packages"],
                       PPW_XUNIT_RUNTIME=context["testHost"]["manifest"],
                       PPW_OBSERVER_URL=context["observerUrl"])
    os.execve("/usr/bin/sandbox-exec",
              ["/usr/bin/sandbox-exec", "-p", policy] + arguments, environment)


def launch(context_path, workspace, output, arguments):
    context_path, context, protected = load_context(context_path)
    client = validate_client(context["clientExecutable"])
    shell = validate_shell(context["shellExecutable"], context["shellSha256"])
    validate_runtime(context["executionRuntime"])
    validate_environment()
    endpoint = urlsplit(context["baseUrl"])
    require(endpoint.scheme == "http" and endpoint.hostname == "127.0.0.1"
            and endpoint.port and not endpoint.username and not endpoint.password
            and not endpoint.query and not endpoint.fragment, "gateway must be the private loopback endpoint")
    runner = Path(__file__).resolve().parent / "run-pair.sh"
    require(arguments and Path(arguments[0]) == runner and "--ppw-gateway-client" in arguments,
            "registered runner/client binding is required")
    require(arguments[arguments.index("--ppw-gateway-client") + 1] == str(client)
            and os.environ.get("CLAUDE_MODEL") == "claude-opus-4-8",
            "registered model/client must be explicit")
    workspace, output = canonical_path(workspace), canonical_path(output)
    environment = dict(os.environ, PPW_GATEWAY_ACTIVE="1",
                       PPW_TRUSTED_CONTEXT=str(context_path),
                       PPW_TRUSTED_OBSERVER_URL=context["observerControlUrl"],
                       PPW_OBSERVER_URL=context["observerUrl"],
                       PPW_TRUSTED_ARCHIVE_ROOT=str(output))
    spec = importlib.util.spec_from_file_location("ppw_xunit_runtime",
                                                 runner.parent / "ppw-test-host.py")
    test_host = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(test_host)
    test_host.validate_runtime(context["testHost"])
    environment.update(NUGET_PACKAGES=context["testHost"]["packages"],
                       PPW_XUNIT_RUNTIME=context["testHost"]["manifest"])
    # The runner is trusted and owns authoritative output. It launches only
    # the native client and generated code through the child policies above.
    os.execve(str(shell), [str(shell), "--noprofile", "--norc"] + arguments, environment)


def main():
    if sys.argv[1:] == ["--client-flags"]:
        print(json.dumps(registered_client_flags()))
        return
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--probe", action="store_true")
    parser.add_argument("--exec-client", action="store_true")
    parser.add_argument("--context", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    arguments = args.arguments[1:] if args.arguments[:1] == ["--"] else args.arguments
    if args.probe:
        print(json.dumps(probe(args.context, args.workspace, args.output), sort_keys=True))
    elif args.exec_client:
        execute_client(args.context, args.workspace, args.output, arguments)
    else:
        launch(args.context, args.workspace, args.output, arguments)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError):
        # Do not echo configuration or authentication material on a failure path.
        print("PP-W gateway isolation refused invocation; inspect nonsecret registration/probe evidence.",
              file=sys.stderr)
        sys.exit(2)
