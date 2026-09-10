#!/usr/bin/env python3
"""Launch the pinned client under a process-local kernel policy, never global settings."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import secrets
import socket
import subprocess
import sys
from urllib.parse import urlsplit

CLIENT_VERSION = "2.1.266 (Claude Code)"
CLIENT_SHA256 = "553d1b9e9e7068b275c0a783c7e139ff6503096f286e674c8c919379fb0eca62"
ISOLATION = "macos-seatbelt-request-gateway-v1"
BLOCKED_ENV = {
    "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_OAUTH_TOKEN",
    "ANTHROPIC_CUSTOM_HEADERS", "CLAUDE_CONFIG_DIR", "CLAUDE_CODE_USE_BEDROCK",
    "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY", "CLAUDE_CODE_USE_AWS",
    "NODE_OPTIONS", "BUN_OPTIONS", "BASH_ENV", "ENV", "SSL_CERT_FILE", "SSL_CERT_DIR",
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
            and not any(name.startswith("DYLD_") and value for name, value in os.environ.items()),
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


def sandbox_policy(workspace, output, protected, port):
    workspace, output, protected = map(canonical_path, (workspace, output, protected))
    require(type(port) is int and 1 <= port <= 65535, "invalid gateway port")
    for writable in (workspace, output):
        require(not contained(protected, writable) and not contained(writable, protected),
                "writable workspace/output overlaps protected gateway state")
    quote = lambda path: json.dumps(str(path))
    return "\n".join([
        "(version 1)",
        "(allow default)",
        "(deny network*)",
        '(allow network-outbound (remote tcp "localhost:%d"))' % port,
        "(deny file-write*)",
        '(allow file-write* (literal "/dev/null"))',
        "(allow file-write* (subpath %s) (subpath %s))" % (quote(workspace), quote(output)),
        "(deny file-read* (subpath %s))" % quote(protected),
        "(deny signal)",
        "(allow signal (target same-sandbox))",
        "(deny process-info*)",
        "(allow process-info* (target same-sandbox))",
        "(deny mach-lookup)",
        '(allow mach-lookup (global-name "com.apple.securityd") (global-name "com.apple.trustd.agent"))',
        "(deny mach-register)",
        "(deny ipc-posix-shm*)",
        "(deny ipc-posix-sem*)",
    ]) + "\n"


def kernel_probe(policy, workspace, protected, gateway_port, gateway_pid):
    """Cost-free live enforcement checks, not a caller's asserted JSON capability."""
    validate_platform()
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen()
    sentinel = protected / "kernel-probe-sentinel"
    sentinel.write_text("UNCHANGED")
    alias_prefix = workspace / ("state-alias-probe-" + secrets.token_hex(8))
    try:
        script = """
import json,os,socket,sys
from pathlib import Path
result={}
for label,port in (("gateway",int(sys.argv[1])),("otherPort",int(sys.argv[2]))):
    try:
        with socket.create_connection(("127.0.0.1",port),timeout=2): pass
        result[label]=True
    except OSError: result[label]=False
try: Path(sys.argv[3]).write_text("CHANGED");result["stateWrite"]=True
except OSError: result["stateWrite"]=False
try: Path(sys.argv[3]).read_text();result["stateRead"]=True
except OSError: result["stateRead"]=False
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
print(json.dumps(result))
"""
        result = subprocess.run(
            ["/usr/bin/sandbox-exec", "-p", policy, sys.executable, "-c", script,
             str(gateway_port), str(listener.getsockname()[1]), str(sentinel), str(gateway_pid),
             str(alias_prefix)],
            cwd=workspace, text=True, capture_output=True, timeout=15)
        require(result.returncode == 0, "kernel isolation probe could not execute")
        observed = json.loads(result.stdout)
        require(observed == {"gateway": True, "otherPort": False, "stateWrite": False,
                             "stateRead": False, "outsideSignal": False, "hardlinkWrite": False,
                             "symlinkWrite": False}
                and sentinel.read_text() == "UNCHANGED", "kernel isolation controls did not hold")
        return {"kind": ISOLATION, "kernelProbe": observed, "modelInvoked": False}
    finally:
        listener.close()
        sentinel.unlink(missing_ok=True)
        for suffix in ("hardlink", "symlink"):
            Path(str(alias_prefix) + "-" + suffix).unlink(missing_ok=True)


def launch(context_path, workspace, output, arguments):
    context_path = canonical_path(context_path)
    require(context_path.is_file() and context_path.stat().st_mode & 0o077 == 0,
            "private gateway context required")
    context = json.loads(context_path.read_text())
    require(context.get("kind") == "pp-w-request-gateway-v1", "wrong gateway context")
    protected = canonical_path(context["protectedRoot"])
    require(contained(context_path, protected), "context is outside protected state")
    client = validate_client(context["clientExecutable"])
    shell = validate_shell(context["shellExecutable"], context["shellSha256"])
    validate_environment()
    endpoint = urlsplit(context["baseUrl"])
    require(endpoint.scheme == "http" and endpoint.hostname == "127.0.0.1"
            and endpoint.port and not endpoint.username and not endpoint.password
            and not endpoint.query and not endpoint.fragment, "gateway must be the private loopback endpoint")
    runner = Path(__file__).resolve().parent / "run-pair.sh"
    require(arguments and Path(arguments[0]) == runner and "--ppw-gateway-client" in arguments,
            "the entire registered runner must be isolated, including builds and held-out execution")
    require(arguments[arguments.index("--ppw-gateway-client") + 1] == str(client)
            and os.environ.get("CLAUDE_MODEL") == "claude-opus-4-8",
            "registered model/client must be explicit")
    workspace, output = canonical_path(workspace), canonical_path(output)
    policy = sandbox_policy(workspace, output, protected, endpoint.port)
    evidence = kernel_probe(policy, workspace, protected, endpoint.port, context["gatewayPid"])
    evidence.update(clientSha256=CLIENT_SHA256, policySha256=hashlib.sha256(policy.encode()).hexdigest())
    (output / "gateway-isolation.json").write_text(json.dumps(evidence, indent=2) + "\n")
    temporary = workspace / ".ppw-client-tmp"
    temporary.mkdir(exist_ok=True)
    dotnet_home = workspace / ".ppw-dotnet-home"
    dotnet_home.mkdir(exist_ok=True)
    environment = dict(os.environ, ANTHROPIC_BASE_URL=context["baseUrl"], DISABLE_AUTOUPDATER="1",
                       TMPDIR=str(temporary), DOTNET_CLI_HOME=str(dotnet_home),
                       DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1", MSBUILDDISABLENODEREUSE="1",
                       GIT_OPTIONAL_LOCKS="0", PPW_INSPECTOR_READONLY="1", PPW_GATEWAY_ACTIVE="1",
                       PPW_PYTHON_EXECUTABLE=sys.executable,
                       PATH=str(runner.parent / "gateway-tools") + os.pathsep + os.environ["PATH"])
    # exec keeps the existing runner's timeout/process-tree ownership intact.
    os.execve("/usr/bin/sandbox-exec",
              ["/usr/bin/sandbox-exec", "-p", policy, str(shell), "--noprofile", "--norc"] + arguments,
              environment)


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--context", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    arguments = args.arguments[1:] if args.arguments[:1] == ["--"] else args.arguments
    launch(args.context, args.workspace, args.output, arguments)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.SubprocessError):
        # Do not echo configuration or authentication material on a failure path.
        print("PP-W gateway isolation refused invocation; inspect nonsecret registration/probe evidence.",
              file=sys.stderr)
        sys.exit(2)
