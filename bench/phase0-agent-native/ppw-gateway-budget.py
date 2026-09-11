#!/usr/bin/env python3
"""Request-level financial admission. Never reads credentials or client cost estimates."""
import argparse
from contextlib import contextmanager
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import sqlite3
import sys

MICRO = 1_000_000
KIND = "pp-w-request-gateway-v1"
MODEL = "claude-opus-4-8"
# Exact limits confirmed by the independent, metadata-only Models API preflight.
CONTEXT = 1_000_000
OUTPUT = 128_000
REQUEST_FIELDS = {
    "model", "max_tokens", "messages", "system", "stream", "tools", "tool_choice",
    "thinking", "output_config", "output_format", "temperature", "top_p", "top_k",
    "stop_sequences", "metadata", "cache_control", "service_tier", "inference_geo",
    "speed", "context_management",
}
CLIENT_TOOL_TYPES = {
    "custom", "bash_20250124", "text_editor_20250124", "text_editor_20250429",
    "text_editor_20250728", "memory_20250818",
}
BETA_CAPABILITIES = {
    "prompt-caching-2024-07-31", "extended-cache-ttl-2025-04-11",
    "interleaved-thinking-2025-05-14", "context-management-2025-06-27",
    "context-1m-2025-08-07", "fast-mode-2026-02-01",
    "claude-code-20250219", "oauth-2025-04-20", "effort-2025-11-24",
    "prompt-caching-scope-2026-01-05", "structured-outputs-2025-12-15",
    "thinking-token-count-2026-05-13", "mid-conversation-system-2026-04-07",
    "mid-conversation-tool-changes-2026-07-01",
    # Native clients advertise this even without an advisor. The actual
    # advisor_20260301 tool definition remains unconditionally unadmitted.
    "advisor-tool-2026-03-01",
}
RESPONSE_CONTENT_TYPES = {"text", "thinking", "redacted_thinking", "tool_use"}
REQUEST_CONTENT_FIELDS = {
    "text": {"text", "citations"},
    "thinking": {"thinking", "signature"},
    "redacted_thinking": {"data"},
    "tool_use": {"id", "name", "input"},
    "tool_result": {"tool_use_id", "content", "is_error"},
    "tool_reference": {"tool_name"},
    "image": {"source"},
    "document": {"source", "title", "context", "citations"},
}
COUNTERS = ("input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens")
ATTEMPT_START_KIND = "pp-w-gateway-attempt-start-v1"
TERMINAL_INVALID_KIND = "pp-w-gateway-terminal-invalid-v1"
TERMINAL_INVALID_EVENT = "slot-terminal-invalid"
TERMINAL_SOURCE_FILES = ("run-pair.sh", "ppw-gateway-budget.py")
TERMINAL_REASON_CODES = {
    "agent.json missing or empty": "MISSING_AGENT_RESULT",
    "agent.json is not valid JSON": "MALFORMED_AGENT_RESULT",
    "transcript.jsonl missing or empty (W1: a run without a per-turn transcript is invalid)":
        "MISSING_TRANSCRIPT",
}
TERMINAL_CLASSIFICATIONS = sorted(
    set(TERMINAL_REASON_CODES.values()) | {"CLIENT_EXIT_WITHOUT_OBSERVED_WORK"})
STAMP_FIELDS = {"epochId", "stage", "dataKind", "compilerCommit"}
ISOLATION_KIND = "macos-seatbelt-request-gateway-v1"
ISOLATION_PROBE = {
    "gateway": True, "otherPort": False, "workspaceWrite": True,
    "authoritativeWrite": False, "authoritativeRead": False,
    "hiddenTestRead": False, "seededSolutionRead": False,
    "stateWrite": False, "stateRead": False,
    "outsideSignal": False, "hardlinkWrite": False, "symlinkWrite": False,
    "samePortIpv6": False,
    "unixSocket": False, "delegatedPreferencesWrite": False,
    "readableSourceRead": True, "readableSourceHardlinkWrite": False,
}


class Refusal(ValueError):
    """Messages are fixed control descriptions, never provider/user payloads."""


def require(condition, reason):
    if not condition:
        raise Refusal(reason)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def sha256_bytes(value):
    return hashlib.sha256(value).hexdigest()


def digest(path):
    return sha256_bytes(Path(path).read_bytes())


def sha256_hex(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def decode(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate JSON key")
            result[key] = value
        return result
    try:
        return json.loads(raw, object_pairs_hook=unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(Refusal("nonfinite JSON")))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise Refusal("invalid JSON") from error


_DISPOSITION = None


def disposition_module():
    global _DISPOSITION
    if _DISPOSITION is None:
        path = Path(__file__).with_name("ppw-gateway-disposition.py")
        spec = importlib.util.spec_from_file_location("ppw_gateway_disposition_budget", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        _DISPOSITION = module
    return _DISPOSITION


PROVIDER_FAILURE_KIND = "pp-w-provider-failure-v1"
PROVIDER_REFUSALS = {
    "invalid JSON": "PROVIDER_JSON_INVALID",
    "duplicate JSON key": "PROVIDER_JSON_DUPLICATE",
    "nonfinite JSON": "PROVIDER_JSON_NONFINITE",
    "invalid provider message": "PROVIDER_MESSAGE_SCHEMA",
    "unexpected server-side response content": "PROVIDER_CONTENT_SCHEMA",
    "oversized provider receipt": "PROVIDER_RECEIPT_OVERSIZED",
    "response model differs": "USAGE_MODEL",
    "missing or unadmitted terminal reason": "USAGE_TERMINAL_REASON",
    "missing provider usage": "USAGE_MISSING",
    "invalid token count or limit": "USAGE_TOKEN_COUNT",
    "output exceeds requested bound": "USAGE_OUTPUT_BOUND",
    "provider input exceeds context bound": "USAGE_INPUT_BOUND",
    "unknown provider service tier": "USAGE_SERVICE_TIER",
    "provider service tier differs from the admitted request": "USAGE_SERVICE_TIER_MISMATCH",
    "unknown provider geography": "USAGE_GEOGRAPHY",
    "unknown provider speed": "USAGE_SPEED",
    "unknown provider usage component": "USAGE_COMPONENT",
    "unexpected fallback credit": "USAGE_FALLBACK_CREDIT",
    "multiple or ambiguous server iterations": "USAGE_ITERATIONS",
    "unknown server iteration fields": "USAGE_ITERATION_SCHEMA",
    "server iteration differs from the single admitted model turn": "USAGE_ITERATION_MISMATCH",
    "unknown output token category": "USAGE_OUTPUT_CATEGORY",
    "thinking tokens exceed the inclusive output total": "USAGE_THINKING_BOUND",
    "unknown server operation counter": "USAGE_SERVER_COUNTER",
    "unexpected server operation": "USAGE_SERVER_OPERATION",
    "unknown cache categories": "USAGE_CACHE_SCHEMA",
    "cache counters disagree": "USAGE_CACHE_MISMATCH",
    "request liability bound contradicted": "USAGE_LIABILITY_BOUND",
    "oversized provider event": "SSE_EVENT_OVERSIZED",
    "invalid provider event": "SSE_EVENT_SCHEMA",
    "event after message stop": "SSE_AFTER_STOP",
    "duplicate provider message": "SSE_DUPLICATE_MESSAGE",
    "nonempty initial provider content": "SSE_INITIAL_CONTENT",
    "invalid terminal delta order": "SSE_DELTA_ORDER",
    "unadmitted message delta": "SSE_DELTA_SCHEMA",
    "non-null provider message delta container": "SSE_DELTA_CONTAINER",
    "non-null provider stop details": "SSE_DELTA_STOP_DETAILS",
    "missing final output usage": "SSE_FINAL_OUTPUT_MISSING",
    "conflicting terminal reasons": "SSE_TERMINAL_CONFLICT",
    "message stop without complete usage": "SSE_STOP_WITHOUT_USAGE",
    "content outside message": "SSE_CONTENT_ORDER",
    "unknown or error provider event": "SSE_UNADMITTED_EVENT",
    "missing usage event": "SSE_USAGE_MISSING",
    "decreasing provider usage": "SSE_USAGE_DECREASED",
    "truncated or ambiguous provider stream": "SSE_INCOMPLETE",
}
PROVIDER_FAILURE_CODES = frozenset(PROVIDER_REFUSALS.values()) | {
    "PROVIDER_SCHEMA", "PROVIDER_TLS_CERTIFICATE", "PROVIDER_TLS", "PROVIDER_TIMEOUT",
    "PROVIDER_DNS", "PROVIDER_CONNECTION", "PROVIDER_HTTP_TRUNCATED", "PROVIDER_HTTP_PROTOCOL",
    "PROVIDER_IO", "PROVIDER_DATA_SHAPE", "PROVIDER_UNEXPECTED_FAILURE",
    "PROVIDER_HTTP_STATUS", "PROVIDER_CONTENT_TYPE", "PROVIDER_CONTENT_ENCODING",
}
PROVIDER_PHASES = {"connect", "send-request", "response-headers", "response-body",
                   "receipt-validation", "upstream-close"}


class ProviderFailure(Refusal):
    def __init__(self, code):
        require(code in PROVIDER_FAILURE_CODES, "unregistered provider diagnostic")
        super().__init__(code)
        self.code = code


def provider_refusal_code(error):
    if isinstance(error, ProviderFailure):
        return error.code
    # Never serialize arbitrary exception text, even if a new refusal is added.
    return PROVIDER_REFUSALS.get(str(error), "PROVIDER_SCHEMA")


def validate_provider_diagnostic(value):
    require(isinstance(value, dict) and set(value) == {
        "kind", "code", "phase", "httpStatus", "responseType", "streamState",
    }, "invalid provider diagnostic fields")
    require(value["kind"] == PROVIDER_FAILURE_KIND
            and isinstance(value["code"], str) and value["code"] in PROVIDER_FAILURE_CODES
            and isinstance(value["phase"], str) and value["phase"] in PROVIDER_PHASES,
            "unregistered provider diagnostic")
    status = value["httpStatus"]
    require(status is None or type(status) is int and 100 <= status <= 999,
            "invalid provider diagnostic status")
    require(isinstance(value["responseType"], str)
            and value["responseType"] in ("unobserved", "absent", "sse", "json", "other"),
            "invalid provider diagnostic content type")
    state = value["streamState"]
    require(state is None or isinstance(state, dict) and set(state) == {
        "started", "terminalDeltaSeen", "stopped",
    } and all(type(item) is bool for item in state.values()),
            "invalid provider diagnostic stream state")
    return value


def source_identities():
    root = Path(__file__).resolve().parent
    return {name: digest(root / name) for name in TERMINAL_SOURCE_FILES}


def strict_terminal_lifecycle(binding):
    artifacts = binding.get("harnessArtifacts", {}) if isinstance(binding, dict) else {}
    return all(artifacts.get(name) == sha for name, sha in source_identities().items())


def contained(path, root):
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def authoritative_run_directory(run_directory, authoritative_root):
    root = Path(authoritative_root)
    directory = Path(run_directory)
    require(root.is_absolute() and directory.is_absolute(), "attempt paths must be absolute")
    require(".." not in root.parts and ".." not in directory.parts, "attempt paths must be canonical")
    require(root.is_dir() and directory.is_dir(), "attempt output directory is missing")
    require(not any(path.is_symlink() for path in (root, directory, *directory.parents)),
            "attempt output path is linked")
    root = root.resolve()
    directory = directory.resolve()
    require(contained(directory, root) and directory != root,
            "attempt output is outside the authoritative archive")
    return directory, root


def read_regular(path, reason):
    path = Path(path)
    require(path.is_file() and not path.is_symlink() and path.stat().st_nlink == 1, reason)
    return path.read_bytes()


def tree_inventory(path):
    root = Path(path)
    require(root.is_dir() and not root.is_symlink(), "sealed source archive is missing")
    entries = []
    for item in sorted(root.rglob("*")):
        require(not item.is_symlink(), "sealed source archive contains a link")
        if item.is_dir():
            continue
        raw = read_regular(item, "sealed source archive contains a nonregular file")
        entries.append({
            "path": item.relative_to(root).as_posix(),
            "sha256": sha256_bytes(raw),
            "size": len(raw),
        })
    require(entries, "sealed source archive is empty")
    return {
        "inventorySha256": sha256_bytes(canonical(entries).encode()),
        "fileCount": len(entries),
        "byteCount": sum(item["size"] for item in entries),
    }


def result_projection(value):
    require(isinstance(value, dict), "invalid result record")
    return {key: item for key, item in value.items() if key not in STAMP_FIELDS}


def attempt_start_path(run_directory):
    return Path(run_directory) / "attempt-start.json"


def terminal_attempt_path(run_directory):
    return Path(run_directory) / "invalid-terminal.json"


def write_json_exclusive(path, value):
    with Path(path).open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n")


def write_attempt_start(run_directory, authoritative_root, slot, attempt_number):
    directory, root = authoritative_run_directory(run_directory, authoritative_root)
    require(isinstance(slot, str) and slot and slot.count("/") == 2, "invalid attempt slot")
    require(type(attempt_number) is int and attempt_number == 1,
            "registered slots permit exactly one numbered attempt")
    value = {
        "schemaVersion": 1,
        "kind": ATTEMPT_START_KIND,
        "slot": slot,
        "attemptNumber": attempt_number,
        "maximumAttempts": 1,
        "actualClientInvocationPending": True,
        "authoritativeRootSha256": sha256_bytes(str(root).encode()),
        "producerSourceSha256": source_identities(),
        "nonce": secrets.token_hex(32),
    }
    write_json_exclusive(attempt_start_path(directory), value)
    return value


def _read_attempt_start(run_directory, authoritative_root, expected_slot, expected_sources=None,
                        recorded_authoritative_root=None):
    directory, root = authoritative_run_directory(run_directory, authoritative_root)
    raw = read_regular(attempt_start_path(directory), "trusted attempt-start record is missing")
    value = decode(raw)
    expected = source_identities() if expected_sources is None else expected_sources
    identity_root = root if recorded_authoritative_root is None else recorded_authoritative_root
    validate_attempt_identity(value, identity_root, expected_slot, expected)
    return value, sha256_bytes(raw)


def validate_attempt_identity(value, root, expected_slot, expected):
    require(isinstance(expected, dict) and set(expected) == set(TERMINAL_SOURCE_FILES)
            and all(sha256_hex(item) for item in expected.values()),
            "registered attempt source identities are missing")
    require(isinstance(value, dict) and set(value) == {
        "schemaVersion", "kind", "slot", "attemptNumber", "maximumAttempts",
        "actualClientInvocationPending", "authoritativeRootSha256",
        "producerSourceSha256", "nonce",
    }, "attempt-start fields differ")
    require(value["schemaVersion"] == 1 and value["kind"] == ATTEMPT_START_KIND
            and value["slot"] == expected_slot and value["attemptNumber"] == 1
            and value["maximumAttempts"] == 1
            and value["actualClientInvocationPending"] is True
            and value["authoritativeRootSha256"] == sha256_bytes(str(root).encode())
            and value["producerSourceSha256"] == expected
            and sha256_hex(value["nonce"]),
            "attempt-start evidence differs from the registered slot or source")


def validate_attempt_start(run_directory, authoritative_root, expected_slot, expected_sources=None,
                           recorded_authoritative_root=None):
    value, _ = _read_attempt_start(
        run_directory, authoritative_root, expected_slot, expected_sources,
        recorded_authoritative_root)
    return value


def terminal_reason_code(reason):
    require(isinstance(reason, str), "invalid terminal reason")
    if reason in TERMINAL_REASON_CODES:
        return TERMINAL_REASON_CODES[reason]
    match = re.fullmatch(r"agent exit code ([1-9][0-9]*) with empty journal\.jsonl", reason)
    if match and valid_client_exit(int(match.group(1))):
        return "CLIENT_EXIT_WITHOUT_OBSERVED_WORK"
    raise Refusal("invalid reason is not an admitted terminal attrition reason")


def valid_client_exit(value):
    return type(value) is int and 0 <= value < 128 and value != 124


def reason_evidence(directory, reason, exit_code):
    contents = {}
    for name in ("agent.json", "transcript.jsonl", "journal.jsonl"):
        path = directory / name
        require(not path.is_symlink(), "invalid-reason evidence is linked")
        contents[name] = read_regular(path, "invalid-reason evidence is not regular") \
            if path.exists() else None
    code = terminal_reason_code(reason)
    if code == "MISSING_AGENT_RESULT":
        require(not contents["agent.json"], "agent result is not missing or empty")
    elif code == "MISSING_TRANSCRIPT":
        require(not contents["transcript.jsonl"], "transcript is not missing or empty")
    elif code == "MALFORMED_AGENT_RESULT":
        require(contents["agent.json"], "malformed agent result is missing")
        try:
            value = decode(contents["agent.json"])
        except Refusal:
            pass
        else:
            require(value is None or value is False, "agent result is valid JSON")
    else:
        require(exit_code > 0 and reason == "agent exit code %d with empty journal.jsonl" % exit_code
                and not contents["journal.jsonl"], "client crash has observed work or a different exit")
    return {name: sha256_bytes(raw) if raw is not None else None for name, raw in contents.items()}


def validate_isolation_evidence(evidence):
    require(isinstance(evidence, dict) and set(evidence) == {
        "kind", "kernelProbe", "modelInvoked", "clientSha256", "policySha256",
        "workspaceRoot", "authoritativeRoot",
    }, "trusted isolation evidence fields differ")
    require(evidence["kind"] == ISOLATION_KIND
            and evidence["kernelProbe"] == ISOLATION_PROBE
            and evidence["modelInvoked"] is False
            and sha256_hex(evidence["clientSha256"])
            and sha256_hex(evidence["policySha256"])
            and all(isinstance(evidence[name], str) and evidence[name]
                    for name in ("workspaceRoot", "authoritativeRoot")),
            "trusted isolation evidence differs")
    return evidence


def _invalid_reason_from_log(raw, exit_code):
    try:
        lines = raw.decode("utf-8").splitlines()
    except UnicodeError as error:
        raise Refusal("invalid reason log is not UTF-8") from error
    require(lines, "invalid reason log is empty")
    match = re.fullmatch(
        r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z attempt=0 agent_rc=(\d+): (.+)",
        lines[-1])
    require(match is not None and int(match.group(1)) == exit_code,
            "invalid reason log lacks the trusted final attempt")
    return match.group(2)


def write_terminal_attempt(run_directory, authoritative_root, slot, reason):
    directory, root = authoritative_run_directory(run_directory, authoritative_root)
    start, start_sha = _read_attempt_start(directory, root, slot)
    reason_code = terminal_reason_code(reason)
    invocation_raw = read_regular(
        directory / "client-invocation.json", "actual client invocation record is missing")
    invocation = decode(invocation_raw)
    require(isinstance(invocation, dict) and set(invocation) == {"exitCode"}
            and valid_client_exit(invocation["exitCode"]),
            "terminal invalid requires a noninterrupted client invocation")
    reason_raw = read_regular(directory / "invalid.txt", "invalid reason provenance is missing")
    require(_invalid_reason_from_log(reason_raw, invocation["exitCode"]) == reason,
            "invalid reason differs from the trusted runner log")
    result_raw = read_regular(directory / "result.json", "invalid result record is missing")
    result = decode(result_raw)
    task, arm, run = slot.split("/")
    require(result.get("pair") == task and result.get("arm") == arm
            and result.get("run") == int(run)
            and result.get("invalid") is True and result.get("censored") is True,
            "invalid result does not identify the terminal slot")
    source = tree_inventory(directory / "final-src")
    record = {
        "schemaVersion": 1,
        "kind": TERMINAL_INVALID_KIND,
        "slot": slot,
        "attemptNumber": start["attemptNumber"],
        "classification": reason_code,
        "invalidReason": reason,
        "attemptStartSha256": start_sha,
        "actualClientInvocation": True,
        "clientExitCode": invocation["exitCode"],
        "clientInvocationSha256": sha256_bytes(invocation_raw),
        "invalidReasonSha256": sha256_bytes(reason_raw),
        "reasonEvidence": reason_evidence(directory, reason, invocation["exitCode"]),
        "resultProjectionSha256": sha256_bytes(
            canonical(result_projection(result)).encode()),
        "sealedSource": source,
        "producerSourceSha256": start["producerSourceSha256"],
    }
    write_json_exclusive(terminal_attempt_path(directory), record)
    return record


def validate_terminal_attempt(
        run_directory, authoritative_root, expected_slot, expected_sources=None,
        recorded_authoritative_root=None):
    directory, root = authoritative_run_directory(run_directory, authoritative_root)
    start, start_sha = _read_attempt_start(
        directory, root, expected_slot, expected_sources, recorded_authoritative_root)
    raw = read_regular(terminal_attempt_path(directory), "terminal-invalid record is missing")
    record = decode(raw)
    require(isinstance(record, dict) and set(record) == {
        "schemaVersion", "kind", "slot", "attemptNumber", "classification",
        "invalidReason", "attemptStartSha256", "actualClientInvocation",
        "clientExitCode", "clientInvocationSha256", "invalidReasonSha256",
        "resultProjectionSha256", "sealedSource", "producerSourceSha256", "reasonEvidence",
    }, "terminal-invalid record fields differ")
    require(record["schemaVersion"] == 1 and record["kind"] == TERMINAL_INVALID_KIND
            and record["slot"] == expected_slot
            and record["attemptNumber"] == start["attemptNumber"] == 1
            and record["classification"] == terminal_reason_code(record["invalidReason"])
            and record["attemptStartSha256"] == start_sha
            and record["actualClientInvocation"] is True
            and valid_client_exit(record["clientExitCode"])
            and record["producerSourceSha256"] == start["producerSourceSha256"],
            "terminal-invalid evidence differs from the trusted attempt")
    invocation_raw = read_regular(
        directory / "client-invocation.json", "actual client invocation record is missing")
    invocation = decode(invocation_raw)
    require(isinstance(invocation, dict) and set(invocation) == {"exitCode"}
            and valid_client_exit(invocation["exitCode"])
            and invocation["exitCode"] == record["clientExitCode"]
            and record["clientInvocationSha256"] == sha256_bytes(invocation_raw),
            "terminal-invalid client invocation evidence differs")
    reason_raw = read_regular(directory / "invalid.txt", "invalid reason provenance is missing")
    require(_invalid_reason_from_log(reason_raw, record["clientExitCode"]) == record["invalidReason"]
            and record["invalidReasonSha256"] == sha256_bytes(reason_raw),
            "terminal-invalid reason provenance differs")
    require(record["reasonEvidence"] == reason_evidence(
        directory, record["invalidReason"], record["clientExitCode"]),
        "terminal-invalid reason evidence changed")
    result_raw = read_regular(directory / "result.json", "invalid result record is missing")
    result = decode(result_raw)
    task, arm, run = expected_slot.split("/")
    require(result.get("pair") == task and result.get("arm") == arm
            and result.get("run") == int(run)
            and result.get("invalid") is True and result.get("censored") is True
            and record["resultProjectionSha256"] == sha256_bytes(
                canonical(result_projection(result)).encode()),
            "terminal-invalid result projection differs")
    require(record["sealedSource"] == tree_inventory(directory / "final-src"),
            "terminal-invalid sealed source differs")
    return record


def integer(value, upper):
    require(type(value) is int and 0 <= value <= upper, "invalid token count or limit")
    return value


def upper_cost(input_tokens, output_tokens):
    # Opus4.8 fast mode: $10 input/$50 output per MTok; 1h cache writes 2x;
    # first-party US residency 1.1x. Applies conservatively even in standard mode.
    return ((input_tokens * 20 + output_tokens * 50) * 11 + 9) // 10


def price_contract():
    return {
        "schemaVersion": 1, "model": MODEL, "upstream": "https://api.anthropic.com",
        "contextUpperTokens": CONTEXT, "outputUpperTokens": OUTPUT,
        "modelLimitsSource": {
            "method": "GET", "path": "/v1/models/" + MODEL,
            "responseFields": {"id": MODEL, "max_input_tokens": CONTEXT, "max_tokens": OUTPUT},
            "verification": "independent parent-reported metadata-only preflight, 2026-09-10",
            "status": 200, "upstreamInferenceRequests": 0,
        },
        "ratesUsdPerMillion": {"input": "5", "output": "25", "cacheWrite5m": "6.25",
                              "cacheWrite1h": "10", "cacheRead": "0.50",
                              "fastInput": "10", "fastOutput": "50"},
        "residencyMaximumMultiplier": "1.1",
        "dataResidencyPolicy": {
            "requestValues": ["global", "us"],
            "responseValues": ["global", "us", "not_available"],
            "omittedResponsePricing": "US 1.1x conservative maximum",
            "notAvailableResponsePricing": "US 1.1x conservative maximum",
            "notAvailableSemantics":
                "observed partial native receipt sentinel; no global residency or cheaper rate inferred",
            "source": "https://platform.claude.com/docs/en/manage-claude/data-residency",
        },
        "serviceTierPolicy": {
            "requests": ["auto", "standard_only"],
            "responses": ["standard", "priority"],
            "pricing": "published token-category rates; priority draws existing capacity",
            "capacityPurchases": "not performed by this Messages request adapter",
        },
        "reservation": "full context at fast 1h-write rate plus requested output at fast rate; 1.1x",
        "reconciliation": "complete provider category counts; higher priced speed/residency/cache TTL if absent",
        "serverOperations": "not admitted; request fails before forwarding, never stripped",
        "requestMultiplicity": "one model iteration only; no server-side compaction or fallback",
        "allowedBetaCapabilities": sorted(BETA_CAPABILITIES),
        "unknownBetaPolicy": "reject the entire request before reservation or upstream connection",
        "advertisedAdvisorPolicy": "header may pass; advisor tool/type/model/server operations are refused",
        "sources": [
            "https://platform.claude.com/docs/en/models/opus-4-8/overview",
            "https://platform.claude.com/docs/en/about-claude/pricing",
            "https://platform.claude.com/docs/en/api/messages/create",
            "https://platform.claude.com/docs/en/api/models/retrieve",
            "https://platform.claude.com/docs/en/api/service-tiers",
            "https://platform.claude.com/docs/en/manage-claude/data-residency",
            "https://platform.claude.com/docs/en/build-with-claude/compaction",
            "https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback",
            "https://platform.claude.com/docs/en/build-with-claude/context-editing",
            "https://platform.claude.com/docs/en/agents-and-tools/tool-use/advisor-tool",
            "https://platform.claude.com/docs/en/build-with-claude/thinking",
            "https://platform.claude.com/docs/en/build-with-claude/structured-outputs",
            "https://code.claude.com/docs/en/llm-gateway-protocol",
        ],
    }


def price_identity():
    return hashlib.sha256(canonical(price_contract()).encode()).hexdigest()


def fields(value, allowed, reason):
    require(isinstance(value, dict) and set(value) <= allowed, reason)


def admitted_betas(header):
    if header is None:
        return []
    require(isinstance(header, str) and not any(ord(c) < 32 and c != "\t" for c in header),
            "invalid beta capability header")
    capabilities = [part.strip(" \t") for part in header.split(",")]
    require(len(set(capabilities)) == len(capabilities)
            and all(value in BETA_CAPABILITIES for value in capabilities),
            "unpriced or ambiguous beta capability")
    return capabilities


def validate_context_management(management):
    fields(management, {"edits"}, "unpriced context management")
    edits = management.get("edits", [])
    require(isinstance(edits, list), "invalid context edits")
    for edit in edits:
        require(isinstance(edit, dict), "invalid context edit")
        if edit.get("type") == "clear_thinking_20251015":
            fields(edit, {"type", "keep"}, "unpriced thinking edit")
        elif edit.get("type") == "clear_tool_uses_20250919":
            fields(edit, {"type", "keep", "trigger", "clear_at_least",
                          "clear_tool_inputs", "exclude_tools"}, "unpriced tool edit")
        else:
            raise Refusal("unpriced context-management operation")
        for name in ("keep", "trigger", "clear_at_least"):
            if isinstance(edit.get(name), dict):
                fields(edit[name], {"type", "value"}, "unpriced context-edit control")


def validate_cache_control(value):
    fields(value, {"type", "ttl"}, "unpriced cache control")
    require(value.get("type") == "ephemeral" and value.get("ttl", "5m") in ("5m", "1h"),
            "unpriced cache lifetime")


def validate_request_content(content):
    if isinstance(content, str):
        return
    require(isinstance(content, list), "invalid request content")
    for block in content:
        require(isinstance(block, dict) and isinstance(block.get("type"), str)
                and block["type"] in REQUEST_CONTENT_FIELDS, "unpriced request content type")
        fields(block, REQUEST_CONTENT_FIELDS[block["type"]] | {"type", "cache_control"},
               "unpriced request content field")
        if "cache_control" in block:
            validate_cache_control(block["cache_control"])
        if block["type"] == "tool_result" and "content" in block:
            validate_request_content(block["content"])
        if block["type"] in ("image", "document"):
            source = block.get("source")
            fields(source, {"type", "data", "media_type", "content"}, "unadmitted remote content source")
            require(source.get("type") in ("base64", "text", "content"),
                    "unpriced content source type")
            if source.get("type") == "content":
                validate_request_content(source.get("content"))


def admit_request(raw, beta_header=None):
    capabilities = admitted_betas(beta_header)
    body = decode(raw)
    require(isinstance(body, dict) and set(body) <= REQUEST_FIELDS, "unpriced request field")
    require(body.get("model") == MODEL, "unregistered or unpriced model")
    maximum = integer(body.get("max_tokens"), OUTPUT)
    require(isinstance(body.get("messages"), list) and body["messages"], "missing messages")
    for message in body["messages"]:
        fields(message, {"role", "content", "cache_control"}, "unpriced message field")
        require(message.get("role") in ("user", "assistant", "system"), "unpriced message role")
        validate_request_content(message.get("content"))
        if "cache_control" in message:
            validate_cache_control(message["cache_control"])
    if "system" in body:
        validate_request_content(body["system"])
    require(type(body.get("stream", False)) is bool, "invalid stream mode")
    require(body.get("service_tier", "auto") in ("auto", "standard_only"), "unpriced service tier")
    require(body.get("speed", "standard") in ("standard", "fast"), "unpriced speed")
    require(body.get("inference_geo", "global") in ("global", "us"), "unpriced inference geography")
    tools = body.get("tools", [])
    require(isinstance(tools, list), "invalid tools")
    for tool in tools:
        require(isinstance(tool, dict) and isinstance(tool.get("type", "custom"), str)
                and tool.get("type", "custom") in CLIENT_TOOL_TYPES,
                "unadmitted server operation or tool")
        fields(tool, {"type", "name", "description", "input_schema", "cache_control",
                      "allowed_callers", "strict", "defer_loading", "max_characters"},
               "unpriced tool field")
        require(tool.get("allowed_callers", ["direct"]) == ["direct"],
                "unadmitted server tool caller")
        if "cache_control" in tool:
            validate_cache_control(tool["cache_control"])
    validate_context_management(body.get("context_management", {}))
    if "thinking" in body:
        fields(body["thinking"], {"type", "budget_tokens", "display"}, "unpriced thinking field")
        require(body["thinking"].get("type") in ("enabled", "disabled", "adaptive"),
                "unpriced thinking mode")
    if "output_config" in body:
        fields(body["output_config"], {"effort", "format"}, "unpriced output configuration")
    for output_format in (body.get("output_format"), body.get("output_config", {}).get("format")):
        if output_format is not None:
            fields(output_format, {"type", "schema"}, "unpriced output format")
            require(output_format.get("type") == "json_schema", "unpriced output format type")
    if "cache_control" in body:
        validate_cache_control(body["cache_control"])
    return {"model": MODEL, "maxTokens": maximum, "stream": body.get("stream", False),
            "maximumMicroUsd": upper_cost(CONTEXT, maximum), "priceSha256": price_identity(),
            "betaCapabilities": capabilities, "serviceTier": body.get("service_tier", "auto")}


def validate_response_content(content):
    require(isinstance(content, list) and all(
        isinstance(block, dict) and block.get("type") in RESPONSE_CONTENT_TYPES for block in content),
        "unexpected server-side response content")


def reconciled_cost(request, model, usage, stop_reason):
    require(model == request["model"], "response model differs")
    require(stop_reason in ("end_turn", "max_tokens", "stop_sequence", "tool_use", "refusal"),
            "missing or unadmitted terminal reason")
    require(isinstance(usage, dict), "missing provider usage")
    counts = {name: integer(usage.get(name), OUTPUT if name == "output_tokens" else CONTEXT)
              for name in COUNTERS}
    require(counts["output_tokens"] <= request["maxTokens"], "output exceeds requested bound")
    total_input = sum(counts[name] for name in COUNTERS if name != "output_tokens")
    require(total_input <= CONTEXT, "provider input exceeds context bound")
    require(usage.get("service_tier") in ("standard", "priority"), "unknown provider service tier")
    require(request.get("serviceTier") in ("auto", "standard_only")
            and (request["serviceTier"] != "standard_only" or usage["service_tier"] == "standard"),
            "provider service tier differs from the admitted request")
    require(usage.get("inference_geo") in (None, "global", "us", "not_available"),
            "unknown provider geography")
    require(usage.get("speed") in (None, "standard", "fast"), "unknown provider speed")
    allowed = set(COUNTERS) | {"service_tier", "inference_geo", "speed", "cache_creation", "server_tool_use",
                              "output_tokens_details", "iterations", "fallback_credit"}
    require(set(usage) <= allowed, "unknown provider usage component")
    require(usage.get("fallback_credit") is None, "unexpected fallback credit")
    iterations = usage.get("iterations")
    if iterations is not None:
        require(isinstance(iterations, list) and len(iterations) == 1,
                "multiple or ambiguous server iterations")
        iteration = iterations[0]
        fields(iteration, set(COUNTERS) | {"type", "model", "cache_creation"},
               "unknown server iteration fields")
        require(iteration.get("type") == "message" and iteration.get("model") == model
                and all(type(iteration.get(name)) is int and iteration[name] == counts[name]
                        for name in COUNTERS)
                and iteration.get("cache_creation") == usage.get("cache_creation"),
                "server iteration differs from the single admitted model turn")
    details = usage.get("output_tokens_details")
    if details is not None:
        fields(details, {"thinking_tokens"}, "unknown output token category")
        require(integer(details.get("thinking_tokens"), OUTPUT) <= counts["output_tokens"],
                "thinking tokens exceed the inclusive output total")
    server = usage.get("server_tool_use")
    if server is not None:
        fields(server, {"web_search_requests", "web_fetch_requests"}, "unknown server operation counter")
    require(server is None or isinstance(server, dict) and all(
        type(value) is int and value == 0 for value in server.values()), "unexpected server operation")
    creation = usage.get("cache_creation")
    if creation is not None:
        require(isinstance(creation, dict) and set(creation) ==
                {"ephemeral_5m_input_tokens", "ephemeral_1h_input_tokens"}, "unknown cache categories")
        require(sum(integer(value, CONTEXT) for value in creation.values()) ==
                counts["cache_creation_input_tokens"], "cache counters disagree")
    # Quarter microdollars avoid floating-point money. Missing billing modifiers
    # select their higher documented rates, never an inferred entitlement.
    quarters = counts["input_tokens"] * 20 + counts["output_tokens"] * 100
    quarters += counts["cache_read_input_tokens"] * 2
    if creation is None:
        quarters += counts["cache_creation_input_tokens"] * 40
    else:
        quarters += creation["ephemeral_5m_input_tokens"] * 25
        quarters += creation["ephemeral_1h_input_tokens"] * 40
    speed = 1 if usage.get("speed") == "standard" else 2
    numerator, denominator = (1, 1) if usage.get("inference_geo") == "global" else (11, 10)
    cost = (quarters * speed * numerator + 4 * denominator - 1) // (4 * denominator)
    require(cost <= request["maximumMicroUsd"], "request liability bound contradicted")
    receipt = {"model": model, "stopReason": stop_reason,
               "usage": {name: usage[name] for name in allowed if name in usage}}
    return cost, receipt


class UsageStream:
    """Observe SSE incrementally; callers relay the original bytes independently."""
    def __init__(self, request):
        self.request = request
        self.buffer = b""
        self.started = False
        self.stopped = False
        self.delta_seen = False
        self.model = None
        self.usage = {}
        self.stop_reason = None
        self.failure = None

    def feed(self, data):
        if self.failure:
            return
        self.buffer += data
        if len(self.buffer) > 8 * 1024 * 1024:
            self.failure = PROVIDER_REFUSALS["oversized provider event"]
            return
        try:
            while b"\n\n" in self.buffer or b"\r\n\r\n" in self.buffer:
                lf, crlf = self.buffer.find(b"\n\n"), self.buffer.find(b"\r\n\r\n")
                width = 4 if crlf >= 0 and (lf < 0 or crlf < lf) else 2
                end = crlf if width == 4 else lf
                block, self.buffer = self.buffer[:end], self.buffer[end + width:]
                lines = block.splitlines()
                payload = b"\n".join(line[5:].lstrip(b" ") for line in lines if line.startswith(b"data:"))
                if not payload:
                    continue
                event = decode(payload)
                require(isinstance(event, dict), "invalid provider event")
                kind = event.get("type")
                if kind == "ping":
                    continue
                require(not self.stopped, "event after message stop")
                if kind == "message_start":
                    require(not self.started, "duplicate provider message")
                    message = event.get("message", {})
                    require(message.get("type") == "message" and message.get("role") == "assistant",
                            "invalid provider message")
                    require(message.get("content") == [], "nonempty initial provider content")
                    self.started = True
                    self.model = message.get("model")
                    self.update_usage(message.get("usage"))
                elif kind == "message_delta":
                    require(self.started, "invalid terminal delta order")
                    self.update_usage(event.get("usage"))
                    delta = event.get("delta")
                    fields(delta, {"stop_reason", "stop_sequence", "container", "stop_details"},
                           "unadmitted message delta")
                    require(delta.get("container") is None,
                            "non-null provider message delta container")
                    require(delta.get("stop_details") is None,
                            "non-null provider stop details")
                    reason = delta.get("stop_reason")
                    if reason is not None:
                        require("output_tokens" in event["usage"], "missing final output usage")
                        require(self.stop_reason in (None, reason), "conflicting terminal reasons")
                        self.stop_reason = reason
                        self.delta_seen = True
                elif kind == "message_stop":
                    require(self.started and self.delta_seen, "message stop without complete usage")
                    self.stopped = True
                elif kind in ("content_block_start", "content_block_delta", "content_block_stop"):
                    require(self.started and not self.delta_seen, "content outside message")
                    if kind == "content_block_start":
                        validate_response_content([event.get("content_block")])
                else:
                    raise Refusal("unknown or error provider event")
        except Refusal as error:
            self.failure = provider_refusal_code(error)
        except (TypeError, KeyError, AttributeError):
            self.failure = "PROVIDER_DATA_SHAPE"

    def update_usage(self, usage):
        require(isinstance(usage, dict), "missing usage event")
        for name, value in usage.items():
            if name in COUNTERS and name in self.usage:
                require(type(value) is int and value >= self.usage[name], "decreasing provider usage")
            self.usage[name] = value

    def finish(self):
        if self.failure:
            raise ProviderFailure(self.failure)
        require(self.started and self.stopped and not self.buffer.strip(),
                "truncated or ambiguous provider stream")
        return reconciled_cost(self.request, self.model, self.usage, self.stop_reason)


class RequestLedger:
    """One immutable pilot scope; every upstream attempt gets a fresh reservation."""
    def __init__(self, path):
        self.path = Path(path)
        require(self.path.is_absolute() and ".." not in self.path.parts, "noncanonical ledger path")
        require(not any(p.is_symlink() for p in (self.path, *self.path.parents)), "linked ledger path")
        require(not self.path.exists() or self.path.stat().st_nlink == 1, "hardlinked ledger")

    @contextmanager
    def transaction(self):
        connection = sqlite3.connect(str(self.path), timeout=30, isolation_level=None)
        connection.row_factory = sqlite3.Row
        try:
            connection.execute("PRAGMA synchronous=FULL")
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.execute("COMMIT")
        except BaseException:
            if connection.in_transaction:
                connection.execute("ROLLBACK")
            raise
        finally:
            connection.close()

    def initialize(self, binding, ceiling_micro):
        require(type(ceiling_micro) is int and 0 < ceiling_micro <= 2 ** 63 - 1, "invalid ceiling")
        require(binding.get("stage") == "pilot" and binding.get("priceSha256") == price_identity(),
                "wrong pilot or price binding")
        planned = binding.get("plannedSlots")
        if planned is not None:
            require(isinstance(planned, list) and planned
                    and all(isinstance(slot, str) and slot for slot in planned)
                    and len(set(planned)) == len(planned), "invalid registered slot inventory")
        self.path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        with self.transaction() as db:
            db.execute("CREATE TABLE IF NOT EXISTS scope "
                       "(id INTEGER PRIMARY KEY CHECK(id=1),binding TEXT,ceiling INTEGER,state TEXT,owner TEXT)")
            db.execute("CREATE TABLE IF NOT EXISTS requests "
                       "(id TEXT PRIMARY KEY,slot TEXT,request TEXT,reserved INTEGER,charge INTEGER,"
                       "state TEXT,reason TEXT,usage TEXT)")
            db.execute("CREATE TABLE IF NOT EXISTS events "
                       "(id INTEGER PRIMARY KEY AUTOINCREMENT,kind TEXT,request_id TEXT,detail TEXT,"
                       "created TEXT DEFAULT(strftime('%Y-%m-%dT%H:%M:%fZ','now')))")
            old = db.execute("SELECT * FROM scope").fetchone()
            if old:
                require(old["binding"] == canonical(binding) and old["ceiling"] == ceiling_micro,
                        "budget scope changed; no reset or implicit additional authorization")
            else:
                db.execute("INSERT INTO scope VALUES(1,?,?,'ready',NULL)",
                           (canonical(binding), ceiling_micro))
                self.event(db, "initialized", None, {"ceilingMicroUsd": ceiling_micro})
        self.path.chmod(0o600)

    @staticmethod
    def event(db, kind, request_id, detail):
        db.execute("INSERT INTO events(kind,request_id,detail) VALUES(?,?,?)",
                   (kind, request_id, canonical(detail)))

    @staticmethod
    def terminal_slots(db):
        return [
            decode(row["detail"])["slot"] for row in db.execute(
                "SELECT detail FROM events WHERE kind IN ('slot-complete',?) ORDER BY id",
                (TERMINAL_INVALID_EVENT,))
        ]

    @staticmethod
    def preserved_slots(db):
        disposition_rows = db.execute(
            "SELECT detail FROM events WHERE kind=? ORDER BY id",
            (disposition_module().DISPOSITION_EVENT,)).fetchall()
        if disposition_rows:
            require(len(disposition_rows) == 1, "duplicate historical liability disposition")
            detail = decode(disposition_rows[0]["detail"])
            require(isinstance(detail, dict) and isinstance(detail.get("authorization"), dict),
                    "malformed historical liability disposition")
            return disposition_module().validate_authorization(
                detail["authorization"])["preservedSlots"]
        rows = db.execute(
            "SELECT detail FROM events WHERE kind='zero-request-recovery' ORDER BY id").fetchall()
        if not rows:
            return []
        require(len(rows) == 1, "duplicate recovery authority")
        detail = decode(rows[0]["detail"])
        preserved = detail.get("preservedAttemptedSlots")
        require(isinstance(preserved, list)
                and all(isinstance(slot, str) and slot for slot in preserved),
                "malformed preserved attempt inventory")
        return preserved

    @staticmethod
    def _base_snapshot(db, scope=None):
        if scope is None:
            scope = db.execute("SELECT * FROM scope").fetchone()
        scope = dict(scope)
        requests = [dict(row) for row in db.execute(
            "SELECT id,slot,request,reserved,charge,state,reason,usage FROM requests ORDER BY rowid")]
        events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        binding = decode(scope["binding"])
        result = {
            "kind": KIND, "state": scope["state"], "binding": binding,
            "ceilingMicroUsd": scope["ceiling"],
            "exposureMicroUsd": sum(row["charge"] for row in requests),
            "requests": requests, "events": events, "verdict": None,
            "basis": "request reservations; only validated complete provider usage permits release",
        }
        if strict_terminal_lifecycle(binding):
            preserved = []
            valid = 0
            invalid = 0
            historical_disposition = False
            for event in events:
                if event["kind"] == "zero-request-recovery":
                    preserved = decode(event["detail"])["preservedAttemptedSlots"]
                elif event["kind"] == disposition_module().DISPOSITION_EVENT:
                    detail = decode(event["detail"])
                    preserved = disposition_module().validate_authorization(
                        detail["authorization"])["preservedSlots"]
                    historical_disposition = True
                elif event["kind"] == "slot-complete":
                    valid += 1
                elif event["kind"] == TERMINAL_INVALID_EVENT:
                    invalid += 1
            result.update(
                accountedSlots=len(preserved) + valid + invalid,
                validCompletedSlots=valid,
                invalidTerminalSlots=(1 if historical_disposition else len(preserved)) + invalid,
                requestBearingSlots=len({row["slot"] for row in requests}),
            )
        return result

    @staticmethod
    def _disposition_context(db):
        rows = db.execute(
            "SELECT id,detail FROM events WHERE kind=? ORDER BY id",
            (disposition_module().DISPOSITION_EVENT,)).fetchall()
        if not rows:
            prefix = db.execute(
                "SELECT kind FROM events WHERE id<=10 ORDER BY id").fetchall()
            historical = db.execute(
                "SELECT COUNT(*) FROM requests WHERE state='unknown' "
                "AND reason='unreconciled-provider-charge' AND usage IS NULL").fetchone()[0]
            kinds = [row["kind"] for row in prefix]
            if len(kinds) >= 10 and historical == 2 and kinds[:9] == [
                    "initialized", "started", "stopped", "zero-request-recovery", "started",
                    "reserved-before-upstream", "reserved-before-upstream",
                    "unknown-charge-retained", "unknown-charge-retained"]:
                raise Refusal("missing or malformed historical liability disposition event 10")
            return None
        require(len(rows) == 1 and rows[0]["id"] == 10,
                "duplicate or misplaced historical liability disposition")
        detail = decode(rows[0]["detail"])
        require(isinstance(detail, dict)
                and set(detail) == {
                    "schemaVersion", "kind", "authorization", "authorizationSha256", "proof"},
                "malformed historical liability disposition")
        return detail["authorization"], detail["authorizationSha256"]

    @classmethod
    def _validate_disposition(cls, db, scope):
        context = cls._disposition_context(db)
        if context is None:
            return None, None
        authorization, authorization_sha256 = context
        replay = disposition_module().validate_history(
            cls._base_snapshot(db, scope),
            authorization=authorization,
            authorization_sha256=authorization_sha256,
            target_binding=decode(scope["binding"]))
        if scope["owner"] is not None:
            disposition_module().validate_owner_binding(
                scope["owner"], decode(scope["binding"]), replay["proofSha256"])
        return context, replay

    @staticmethod
    def _halt_disposition_integrity(db, scope):
        if scope["state"] in ("ready", "collecting"):
            db.execute("UPDATE scope SET state='INCOMPLETE_POLICY' WHERE id=1")
            RequestLedger.event(db, "stopped", None, {
                "reason": "INCOMPLETE_POLICY",
                "diagnostic": "DISPOSITION_INTEGRITY",
            })

    @classmethod
    def require_next_slot(cls, db, scope, slot):
        require(slot not in cls.terminal_slots(db), "duplicate terminal slot")
        planned = decode(scope["binding"]).get("plannedSlots")
        if planned is None:
            return
        require(slot in planned, "unregistered gateway slot")
        preserved = cls.preserved_slots(db)
        remaining = [item for item in planned if item not in preserved]
        terminals = cls.terminal_slots(db)
        require(terminals == remaining[:len(terminals)], "terminal slot history is out of order")
        require(len(terminals) < len(remaining) and slot == remaining[len(terminals)],
                "gateway slot is duplicate, replaced, or out of registered order")

    def start(self, expected_disposition_proof=None):
        owner = secrets.token_hex(32)
        failure = None
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            try:
                context, replay = self._validate_disposition(db, row)
                if context is not None:
                    proof = decode(db.execute(
                        "SELECT detail FROM events WHERE id=10").fetchone()["detail"])["proof"]
                    require(expected_disposition_proof == proof
                            or expected_disposition_proof is None
                            and context[0]["evidenceMode"] == "SYNTHETIC_TEST_ONLY",
                            "collector start does not match the registered disposition proof")
                    owner += "." + disposition_module().owner_binding_identity(
                        decode(row["binding"]), replay["proofSha256"])
                else:
                    require(expected_disposition_proof is None,
                            "disposition proof supplied to a different scope")
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, row)
                failure = error
            if failure is None:
                require(row and row["state"] == "ready",
                        "scope already started; no concurrent collector or reset")
                db.execute("UPDATE scope SET state='collecting',owner=?", (owner,))
                self.event(db, "started", None, {})
        if failure is not None:
            raise Refusal(str(failure))
        return owner

    def reserve(self, owner, slot, request):
        request_id = secrets.token_hex(24)
        refused = False
        failure = None
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            try:
                self._validate_disposition(db, row)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, row)
                failure = error
            if failure is None:
                require(row and row["owner"] == owner and row["state"] == "collecting",
                        "budget scope is not collecting")
                self.require_next_slot(db, row, slot)
                exposure = db.execute(
                    "SELECT COALESCE(SUM(charge),0) AS total FROM requests").fetchone()["total"]
                maximum = request["maximumMicroUsd"]
                require(request["priceSha256"] == price_identity() and
                        maximum == upper_cost(CONTEXT, integer(request["maxTokens"], OUTPUT)),
                        "request reservation does not match implemented price bound")
                if exposure + maximum > row["ceiling"]:
                    db.execute("UPDATE scope SET state='INCOMPLETE_BUDGET'")
                    self.event(db, "budget-refusal", None, {"requiredMicroUsd": maximum})
                    refused = True
                else:
                    db.execute("INSERT INTO requests VALUES(?,?,?,?,?,'reserved',NULL,NULL)",
                               (request_id, slot, canonical(request), maximum, maximum))
                    self.event(db, "reserved-before-upstream", request_id,
                               {"maximumMicroUsd": maximum})
        if failure is not None:
            raise Refusal(str(failure))
        require(not refused, "INCOMPLETE_BUDGET")
        return request_id

    def settle(self, owner, request_id, cost=None, usage=None, diagnostic=None):
        require(diagnostic is None or cost is None, "failure diagnostic cannot reconcile a charge")
        if diagnostic is not None:
            validate_provider_diagnostic(diagnostic)
        failure = None
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            try:
                self._validate_disposition(db, scope)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, scope)
                failure = error
            if failure is None:
                require(scope and scope["owner"] == owner, "settlement owner differs")
                row = db.execute("SELECT * FROM requests WHERE id=?", (request_id,)).fetchone()
                require(row and row["state"] == "reserved", "unknown or duplicate settlement")
                if cost is None:
                    db.execute(
                        "UPDATE requests SET state='unknown',"
                        "reason='unreconciled-provider-charge' WHERE id=?", (request_id,))
                    db.execute("UPDATE scope SET state=CASE WHEN state='INCOMPLETE_BUDGET' THEN state "
                               "ELSE 'INCOMPLETE_UNKNOWN_CHARGE' END")
                    self.event(db, "unknown-charge-retained", request_id,
                               {} if diagnostic is None else {"diagnostic": diagnostic})
                else:
                    require(type(cost) is int and 0 <= cost <= row["reserved"],
                            "invalid reconciliation")
                    require(isinstance(usage, dict), "provider usage evidence required")
                    verified_cost, verified_receipt = reconciled_cost(
                        decode(row["request"]), usage.get("model"), usage.get("usage"),
                        usage.get("stopReason"))
                    require(cost == verified_cost and usage == verified_receipt,
                            "settlement differs from complete provider usage")
                    db.execute("UPDATE requests SET state='reconciled',charge=?,usage=? WHERE id=?",
                               (cost, canonical(usage), request_id))
                    self.event(db, "complete-provider-usage", request_id, {
                        "conservativeChargeMicroUsd": cost,
                        "releasedMicroUsd": row["reserved"] - cost,
                    })
        if failure is not None:
            raise Refusal(str(failure))

    def stop(self, owner, reason, diagnostic=None):
        require(reason in ("INCOMPLETE_POLICY", "INCOMPLETE_INTERRUPTED", "INCOMPLETE_UNKNOWN_CHARGE"),
                "unregistered incomplete reason")
        require(diagnostic is None or reason == "INCOMPLETE_POLICY" and diagnostic in {
            "WIRE_DUPLICATE_HEADER", "WIRE_TRANSFER_ENCODING", "WIRE_CONTENT_ENCODING",
            "WIRE_CONTENT_TYPE", "WIRE_API_VERSION", "WIRE_UNKNOWN_PROVIDER_HEADER",
            "WIRE_BROWSER_ACCESS_VALUE", "WIRE_CONTENT_LENGTH", "WIRE_HOP_CAPABILITY",
            "WIRE_ENDPOINT", "OBSERVER_OPERATION", "PRICE_REQUEST", "REQUEST_RESERVATION",
        } or reason == "INCOMPLETE_INTERRUPTED" and diagnostic == "CLIENT_DISCONNECTED",
                "unregistered nonsecret policy diagnostic")
        with self.transaction() as db:
            row = db.execute("SELECT owner,state FROM scope").fetchone()
            require(row and row["owner"] == owner, "stop owner differs")
            if row["state"] == "collecting":
                db.execute("UPDATE scope SET state=?", (reason,))
                detail = {"reason": reason}
                if diagnostic is not None:
                    detail["diagnostic"] = diagnostic
                self.event(db, "stopped", None, detail)

    def complete_slot(self, owner, slot, evidence, exit_code, attempt=None):
        require(valid_client_exit(exit_code), "missing or interrupted client invocation")
        require(isinstance(evidence, dict), "trusted isolation evidence required")
        failure = None
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            try:
                self._validate_disposition(db, scope)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, scope)
                failure = error
            if failure is None:
                require(scope and scope["owner"] == owner and scope["state"] == "collecting",
                        "incomplete scope cannot complete a slot")
                if strict_terminal_lifecycle(decode(scope["binding"])):
                    validate_isolation_evidence(evidence)
                    validate_attempt_identity(
                        attempt, evidence["authoritativeRoot"], slot, source_identities())
                self.require_next_slot(db, scope, slot)
                requests = db.execute("SELECT state FROM requests WHERE slot=?", (slot,)).fetchall()
                require(requests and all(row["state"] == "reconciled" for row in requests),
                        "slot lacks complete gateway-accounted requests")
                detail = {"slot": slot, "clientExitCode": exit_code, "isolation": evidence}
                if attempt is not None:
                    require(isinstance(attempt, dict) and attempt.get("kind") == ATTEMPT_START_KIND
                            and attempt.get("slot") == slot and attempt.get("attemptNumber") == 1,
                            "trusted attempt-start evidence required")
                    detail["attempt"] = attempt
                self.event(db, "slot-complete", None, detail)
        if failure is not None:
            raise Refusal(str(failure))

    def complete_invalid_slot(
            self, owner, slot, run_directory, authoritative_root, evidence, expected_sources):
        validate_isolation_evidence(evidence)
        require(expected_sources == source_identities(),
                "terminal-invalid completion requires the current registered producer sources")
        terminal = validate_terminal_attempt(
            run_directory, authoritative_root, slot, expected_sources)
        failure = None
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            try:
                self._validate_disposition(db, scope)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, scope)
                failure = error
            if failure is None:
                require(scope and scope["owner"] == owner and scope["state"] == "collecting",
                        "incomplete scope cannot terminally account an invalid slot")
                require(strict_terminal_lifecycle(decode(scope["binding"])),
                        "terminal-invalid lifecycle is not source-bound by this scope")
                self.require_next_slot(db, scope, slot)
                requests = db.execute(
                    "SELECT state,reason FROM requests WHERE slot=?", (slot,)).fetchall()
                require(all(row["state"] == "reconciled" and row["reason"] is None
                            for row in requests),
                        "terminal-invalid slot has in-flight or unknown charge")
                self.event(db, TERMINAL_INVALID_EVENT, None, {
                    "slot": slot,
                    "clientExitCode": terminal["clientExitCode"],
                    "isolation": evidence,
                    "terminal": terminal,
                })
        if failure is not None:
            raise Refusal(str(failure))
        return terminal

    def complete(self, owner):
        context = None
        failure = None
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            try:
                context, _ = self._validate_disposition(db, row)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, row)
                failure = error
        if failure is not None:
            raise Refusal(str(failure))
        if context is not None:
            authorization, authorization_sha256 = context
            return disposition_module().complete_scope(
                self.path, owner,
                authorization=authorization,
                authorization_sha256=authorization_sha256,
                target_binding=decode(row["binding"]))
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            require(row and row["owner"] == owner and row["state"] == "collecting",
                    "incomplete scope cannot complete")
            require(not db.execute("SELECT 1 FROM requests WHERE state!='reconciled'").fetchone(),
                    "in-flight or unknown charge remains")
            planned = decode(row["binding"]).get("plannedSlots")
            if planned is not None:
                completed = self.terminal_slots(db)
                require(completed == planned,
                        "complete registered slot inventory is required")
                if strict_terminal_lifecycle(decode(row["binding"])):
                    valid = db.execute(
                        "SELECT COUNT(*) FROM events WHERE kind='slot-complete'").fetchone()[0]
                    invalid = db.execute(
                        "SELECT COUNT(*) FROM events WHERE kind=?",
                        (TERMINAL_INVALID_EVENT,)).fetchone()[0]
                    detail = {
                        "accountedSlots": len(completed),
                        "validCompletedSlots": valid,
                        "invalidTerminalSlots": invalid,
                    }
                else:
                    detail = {}
            else:
                detail = {}
            db.execute("UPDATE scope SET state='complete'")
            self.event(db, "collection-complete", None, detail)

    def snapshot(self):
        failure = None
        replay = None
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            try:
                _, replay = self._validate_disposition(db, scope)
            except (Refusal, ValueError, KeyError, TypeError) as error:
                self._halt_disposition_integrity(db, scope)
                failure = error
            result = self._base_snapshot(db)
        if failure is not None:
            raise Refusal(str(failure))
        if replay is not None:
            result.update(
                permanentUnknownMicroUsd=replay["permanentUnknownMicroUsd"],
                actualCost=replay["actualCost"],
                historicalUnknownRequestCount=replay["historicalUnknownRequestCount"],
                historicalUnknownRequestIds=replay["historicalUnknownRequestIds"],
                historicalSourceEpochIds=replay["historicalSourceEpochIds"],
                historicalPreservedSlots=replay["historicalPreservedSlots"],
                liveUnknownRequestCount=replay["liveUnknownRequestCount"],
                liveUnknownRequestIds=replay["liveUnknownRequestIds"],
                liveReservedRequestCount=replay["liveReservedRequestCount"],
                remainingNonterminalSlots=replay["remainingNonterminalSlots"],
            )
        return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    subparsers = parser.add_subparsers(dest="operation", required=True)
    for operation in ("attempt-start", "terminal-invalid"):
        command = subparsers.add_parser(operation, allow_abbrev=False)
        command.add_argument("--run-directory", required=True)
        command.add_argument("--authoritative-root", required=True)
        command.add_argument("--slot", required=True)
        if operation == "attempt-start":
            command.add_argument("--attempt-number", required=True, type=int)
        else:
            command.add_argument("--reason", required=True)
    args = parser.parse_args(argv)
    try:
        if args.operation == "attempt-start":
            value = write_attempt_start(
                args.run_directory, args.authoritative_root, args.slot, args.attempt_number)
        else:
            value = write_terminal_attempt(
                args.run_directory, args.authoritative_root, args.slot, args.reason)
        print(canonical(value))
    except (Refusal, OSError, ValueError, TypeError, KeyError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    sys.exit(main())
