#!/usr/bin/env python3
"""Request-level financial admission. Never reads credentials or client cost estimates."""
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import secrets
import sqlite3

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


class Refusal(ValueError):
    """Messages are fixed control descriptions, never provider/user payloads."""


def require(condition, reason):
    if not condition:
        raise Refusal(reason)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


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
    require(usage.get("inference_geo") in (None, "global", "us"), "unknown provider geography")
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
            self.failure = "oversized provider event"
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
                    fields(delta, {"stop_reason", "stop_sequence"}, "unadmitted message delta")
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
        except (Refusal, TypeError, KeyError, AttributeError):
            self.failure = "incomplete or ambiguous provider stream"

    def update_usage(self, usage):
        require(isinstance(usage, dict), "missing usage event")
        for name, value in usage.items():
            if name in COUNTERS and name in self.usage:
                require(type(value) is int and value >= self.usage[name], "decreasing provider usage")
            self.usage[name] = value

    def finish(self):
        require(not self.failure and self.started and self.stopped and not self.buffer.strip(),
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

    def start(self):
        owner = secrets.token_hex(32)
        with self.transaction() as db:
            row = db.execute("SELECT state FROM scope").fetchone()
            require(row and row["state"] == "ready", "scope already started; no concurrent collector or reset")
            db.execute("UPDATE scope SET state='collecting',owner=?", (owner,))
            self.event(db, "started", None, {})
        return owner

    def reserve(self, owner, slot, request):
        request_id = secrets.token_hex(24)
        refused = False
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            require(row and row["owner"] == owner and row["state"] == "collecting",
                    "budget scope is not collecting")
            planned = decode(row["binding"]).get("plannedSlots")
            require(planned is None or slot in planned, "unregistered gateway slot")
            exposure = db.execute("SELECT COALESCE(SUM(charge),0) AS total FROM requests").fetchone()["total"]
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
                self.event(db, "reserved-before-upstream", request_id, {"maximumMicroUsd": maximum})
        require(not refused, "INCOMPLETE_BUDGET")
        return request_id

    def settle(self, owner, request_id, cost=None, usage=None):
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            require(scope and scope["owner"] == owner, "settlement owner differs")
            row = db.execute("SELECT * FROM requests WHERE id=?", (request_id,)).fetchone()
            require(row and row["state"] == "reserved", "unknown or duplicate settlement")
            if cost is None:
                db.execute("UPDATE requests SET state='unknown',reason='unreconciled-provider-charge' WHERE id=?",
                           (request_id,))
                db.execute("UPDATE scope SET state=CASE WHEN state='INCOMPLETE_BUDGET' THEN state "
                           "ELSE 'INCOMPLETE_UNKNOWN_CHARGE' END")
                self.event(db, "unknown-charge-retained", request_id, {})
            else:
                require(type(cost) is int and 0 <= cost <= row["reserved"], "invalid reconciliation")
                require(isinstance(usage, dict), "provider usage evidence required")
                verified_cost, verified_receipt = reconciled_cost(
                    decode(row["request"]), usage.get("model"), usage.get("usage"), usage.get("stopReason"))
                require(cost == verified_cost and usage == verified_receipt,
                        "settlement differs from complete provider usage")
                db.execute("UPDATE requests SET state='reconciled',charge=?,usage=? WHERE id=?",
                           (cost, canonical(usage), request_id))
                self.event(db, "complete-provider-usage", request_id,
                           {"conservativeChargeMicroUsd": cost, "releasedMicroUsd": row["reserved"] - cost})

    def stop(self, owner, reason):
        require(reason in ("INCOMPLETE_POLICY", "INCOMPLETE_INTERRUPTED", "INCOMPLETE_UNKNOWN_CHARGE"),
                "unregistered incomplete reason")
        with self.transaction() as db:
            row = db.execute("SELECT owner,state FROM scope").fetchone()
            require(row and row["owner"] == owner, "stop owner differs")
            if row["state"] == "collecting":
                db.execute("UPDATE scope SET state=?", (reason,))
                self.event(db, "stopped", None, {"reason": reason})

    def complete_slot(self, owner, slot, evidence, exit_code):
        require(type(exit_code) is int and 0 <= exit_code < 128 and exit_code != 124,
                "missing or interrupted client invocation")
        require(isinstance(evidence, dict), "trusted isolation evidence required")
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope").fetchone()
            require(scope and scope["owner"] == owner and scope["state"] == "collecting",
                    "incomplete scope cannot complete a slot")
            requests = db.execute("SELECT state FROM requests WHERE slot=?", (slot,)).fetchall()
            require(requests and all(row["state"] == "reconciled" for row in requests),
                    "slot lacks complete gateway-accounted requests")
            completed = db.execute("SELECT detail FROM events WHERE kind='slot-complete'").fetchall()
            require(all(decode(row["detail"])["slot"] != slot for row in completed),
                    "duplicate slot completion")
            self.event(db, "slot-complete", None,
                       {"slot": slot, "clientExitCode": exit_code, "isolation": evidence})

    def complete(self, owner):
        with self.transaction() as db:
            row = db.execute("SELECT * FROM scope").fetchone()
            require(row and row["owner"] == owner and row["state"] == "collecting",
                    "incomplete scope cannot complete")
            require(not db.execute("SELECT 1 FROM requests WHERE state!='reconciled'").fetchone(),
                    "in-flight or unknown charge remains")
            planned = decode(row["binding"]).get("plannedSlots")
            if planned is not None:
                completed = [decode(event["detail"])["slot"] for event in db.execute(
                    "SELECT detail FROM events WHERE kind='slot-complete'")]
                require(len(completed) == len(planned) and set(completed) == set(planned),
                        "complete registered slot inventory is required")
            db.execute("UPDATE scope SET state='complete'")
            self.event(db, "collection-complete", None, {})

    def snapshot(self):
        with self.transaction() as db:
            scope = dict(db.execute("SELECT * FROM scope").fetchone())
            requests = [dict(row) for row in db.execute(
                "SELECT id,slot,request,reserved,charge,state,reason,usage FROM requests ORDER BY rowid")]
            events = [dict(row) for row in db.execute("SELECT * FROM events ORDER BY id")]
        return {"kind": KIND, "state": scope["state"], "binding": decode(scope["binding"]),
                "ceilingMicroUsd": scope["ceiling"], "exposureMicroUsd": sum(r["charge"] for r in requests),
                "requests": requests, "events": events, "verdict": None,
                "basis": "request reservations; only validated complete provider usage permits release"}
