#!/usr/bin/env python3
"""No-forward native compatibility probe. No provider client, ledger, or epoch is created."""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import tempfile
import threading
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent
MARKER = "NO_FORWARD_TRANSPORT_PROBE"
CAPABILITY = re.compile(
    r"(?:oauth|claude-code|context|interleaved-thinking|fine-grained-tool-streaming|"
    r"adaptive-thinking|thinking|effort|tool|advanced-tool|output|prompt-caching|"
    r"extended-cache|fast-mode|model-context|structured-outputs|task-budgets)"
    r"[-a-z0-9]*-20\d{2}-?\d{2}-?\d{2}\Z")
PUBLIC_MODEL = re.compile(r"claude-(?:opus|sonnet|haiku|fable|mythos)-[0-9][a-z0-9.-]*\Z")
PUBLIC_BETAS = {
    "token-efficient-tools-2025-02-19", "cache-diagnosis-2026-04-07",
    "mid-conversation-tool-changes-2026-07-01", "mid-conversation-output-config-2026-07-01",
    "mid-conversation-system-clear-at-2026-08-21", "dev-full-thinking-2025-05-14",
    "files-api-2025-04-14", "pdfs-2024-09-25", "mcp-client-2025-04-04",
    "mcp-client-2025-11-20", "server-side-fallback-2026-06-01", "server-side-fallback-2026-07-01",
    "fallback-credit-2026-06-01", "fallback-credit-2026-07-01", "compact-2026-01-12",
    "advisor-tool-2026-03-01", "mid-conversation-system-2026-04-07",
}


def public_beta(value):
    return value in PUBLIC_BETAS or bool(CAPABILITY.fullmatch(value))


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


isolation = module("ppw-gateway-client")
budget = module("ppw-gateway-budget")


def keys(value):
    if not isinstance(value, dict):
        return None
    return sorted(key for key in value if re.fullmatch(r"[a-z_]{1,64}", key))


def footprint(raw, beta_header):
    body = budget.decode(raw)
    budget.require(isinstance(body, dict), "invalid request object")
    betas = [part.strip() for part in (beta_header or "").split(",") if part.strip()]
    tools = body.get("tools", [])
    management = body.get("context_management", {})
    edits = management.get("edits", []) if isinstance(management, dict) else []
    result = {
        "bodyKeys": keys(body), "model": body.get("model") if isinstance(body.get("model"), str)
        and PUBLIC_MODEL.fullmatch(body["model"]) else "unrecognized",
        "maxTokens": body.get("max_tokens") if type(body.get("max_tokens")) is int else None,
        "stream": body.get("stream") if type(body.get("stream")) is bool else None,
        "betaCapabilities": sorted(value for value in betas if public_beta(value)),
        "unclassifiedBetaCount": sum(not public_beta(value) for value in betas),
        "thinkingKeys": keys(body.get("thinking")), "outputConfigKeys": keys(body.get("output_config")),
        "contextManagementKeys": keys(management),
        "contextEditKeys": [keys(edit) for edit in edits] if isinstance(edits, list) else None,
        "contextEditTypes": [edit.get("type") if edit.get("type") in
                             ("clear_thinking_20251015", "clear_tool_uses_20250919", "compact_20260112")
                             else "unrecognized" for edit in edits if isinstance(edit, dict)]
        if isinstance(edits, list) else None,
        "toolFieldSets": sorted({tuple(keys(tool) or []) for tool in tools})
        if isinstance(tools, list) else None,
        "toolTypes": sorted({tool.get("type", "custom") if isinstance(tool, dict)
                             and tool.get("type", "custom") in budget.CLIENT_TOOL_TYPES
                             else "unrecognized" for tool in tools}) if isinstance(tools, list) else None,
    }
    try:
        budget.admit_request(raw, beta_header)
        result["priceContractAccepted"] = True
    except budget.Refusal as error:
        result["priceContractAccepted"] = False
        result["admissionFailure"] = str(error)
    return result


class RejectingHandler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_HEAD(self):
        self.send_response(200)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True

    def do_GET(self):
        self.reject()

    def do_POST(self):
        self.connection.settimeout(10)
        try:
            length = int(self.headers.get("Content-Length", "-1"))
            budget.require(0 < length <= 32 * 1024 * 1024, "invalid probe request length")
            observation = footprint(self.rfile.read(length), self.headers.get("anthropic-beta"))
            path = urlsplit(self.path).path
            observation.update(
                messagesPath=path == "/" + self.server.capability + "/v1/messages",
                credentialHeaderPresent=bool(self.headers.get("Authorization") or self.headers.get("x-api-key")),
            )
            self.server.observations.append(observation)
        except (ValueError, TypeError, OSError):
            self.server.observations.append({"invalidRequest": True})
        self.reject()

    def reject(self):
        payload = json.dumps({"type": "error", "error": {
            "type": "invalid_request_error", "message": MARKER + ": no provider request was sent.",
        }}).encode()
        self.send_response(400)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(payload)
        self.close_connection = True


class RejectingServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), RejectingHandler)
        self.capability = secrets.token_hex(32)
        self.observations = []

    def handle_error(self, *_):
        self.observations.append({"requestTransportError": True})


def run(client, scratch_root):
    isolation.validate_platform()
    client = isolation.validate_client(client)
    scratch_root = isolation.canonical_path(scratch_root)
    isolation.require(scratch_root.is_dir(), "existing scratch root required")
    server = RejectingServer()
    worker = threading.Thread(target=server.serve_forever)
    worker.start()
    try:
        with tempfile.TemporaryDirectory(prefix="no-forward-", dir=scratch_root) as temporary:
            base = Path(temporary)
            work, output, protected = (base / name for name in ("work", "output", "protected"))
            for path in (work, output, protected):
                path.mkdir()
            source = work / "src"
            source.mkdir()
            policy = isolation.sandbox_policy(work, output, protected, server.server_port)
            evidence = isolation.kernel_probe(policy, work, protected, server.server_port, os.getpid())
            endpoint = "http://127.0.0.1:%d/%s" % (server.server_port, server.capability)
            environment = isolation.client_environment(work, endpoint)
            environment["CLAUDE_MODEL"] = budget.MODEL
            argv = ["/usr/bin/sandbox-exec", "-p", policy, str(client)]
            argv += isolation.registered_client_flags() + ["--model", budget.MODEL,
                "Engineering no-forward compatibility probe. The local gateway rejects requests. Do not use tools."]
            process = subprocess.Popen(argv, env=environment, cwd=source, close_fds=True,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
            timed_out = False
            try:
                stdout, stderr = process.communicate(timeout=90)
            except subprocess.TimeoutExpired:
                timed_out = True
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    stdout, stderr = process.communicate(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    stdout, stderr = process.communicate()
            finally:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            recognized = MARKER.encode() in stdout + stderr
            return {
                "kind": "pp-w-engineering-no-forward-native-probe",
                "clientSha256": isolation.CLIENT_SHA256, "clientVersion": isolation.CLIENT_VERSION,
                "clientFlags": isolation.registered_client_flags(), "model": budget.MODEL,
                "clientExitCode": process.returncode, "timedOut": timed_out,
                "recognizedProbeError": recognized, "observations": server.observations,
                "kernelEvidence": evidence, "upstreamRequests": 0, "experimentalObservations": 0,
                "sourceHashes": {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
                                 for name in ("probe-ppw-gateway.py", "ppw-gateway-client.py", "run-pair.sh",
                                              "ppw-gateway-budget.py")},
                "completeIsolationProof": False,
                "success": not timed_out and recognized and any(
                    value.get("messagesPath") and value.get("credentialHeaderPresent")
                    and value.get("model") == budget.MODEL for value in server.observations),
            }
    finally:
        server.shutdown()
        worker.join()
        server.server_close()


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--client", required=True)
    parser.add_argument("--scratch-root", required=True)
    args = parser.parse_args()
    report = run(args.client, args.scratch_root)
    print(json.dumps(report, indent=2))
    return 0 if report["success"] else 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, OSError, subprocess.SubprocessError):
        print(json.dumps({"kind": "pp-w-engineering-no-forward-native-probe", "success": False,
                          "error": "local probe setup or execution failed; no provider forwarding exists"}))
        raise SystemExit(2)
