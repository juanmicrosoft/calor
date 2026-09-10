#!/usr/bin/env python3
"""Loopback-only, fixed-upstream Messages gateway. No credentials or bodies are logged."""
import hmac
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
from pathlib import Path
import secrets
import socket
import ssl
import threading
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("ppw_gateway_budget", ROOT / "ppw-gateway-budget.py")
budget = importlib.util.module_from_spec(spec)
spec.loader.exec_module(budget)

MAX_BODY = 32 * 1024 * 1024
MAX_RECEIPT = 8 * 1024 * 1024
HOP_HEADERS = {"connection", "proxy-connection", "proxy-authorization", "proxy-authenticate",
               "transfer-encoding", "te", "trailer", "upgrade", "keep-alive", "host", "content-length"}


def provider_connection():
    # Deliberately no environment proxy, alternate destination, redirect or retry.
    return http.client.HTTPSConnection("api.anthropic.com", 443, timeout=300,
                                      context=ssl.create_default_context())


class Server(ThreadingHTTPServer):
    daemon_threads = False
    allow_reuse_address = False

    def handle_error(self, request, client_address):
        # The HTTP library's default prints tracebacks, which can contain request data.
        self.gateway.fail("INCOMPLETE_UNKNOWN_CHARGE")


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "PPW"
    sys_version = ""

    def setup(self):
        self.request.settimeout(30)
        super().setup()

    def log_message(self, format, *args):
        pass

    def reply(self, status, reason):
        raw = json.dumps({"type": "error", "error": {"type": "invalid_request_error",
                                                   "message": reason}}).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Connection", "close")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(raw)
        self.close_connection = True

    def authorized_path(self):
        parsed = urlsplit(self.path)
        if parsed.scheme or parsed.netloc or parsed.fragment:
            return None
        prefix, separator, path = parsed.path[1:].partition("/")
        if not separator or not hmac.compare_digest(prefix, self.server.gateway.capability):
            return None
        return "/" + path, parsed.query

    def do_HEAD(self):
        if self.authorized_path() == ("/api/hello", ""):
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.send_header("Connection", "close")
            self.end_headers()
            self.close_connection = True
        else:
            self.reply(404, "unsupported gateway endpoint")

    def do_GET(self):
        self.reply(404, "unsupported gateway endpoint")

    def do_CONNECT(self):
        self.reply(405, "tunneling is not supported")

    def do_POST(self):
        gateway = self.server.gateway
        route = self.authorized_path()
        if route is None:
            self.reply(403, "invalid gateway capability")
            return
        path, query = route
        if path == "/v1/messages/count_tokens":
            self.reply(404, "optional token counting is unavailable; never a financial bound")
            return
        if path != "/v1/messages" or query not in ("", "beta=true"):
            gateway.fail("INCOMPLETE_POLICY")
            self.reply(400, "unpriced gateway endpoint")
            return
        try:
            budget.require(gateway.active, "gateway slot is closed")
            for name in ("content-length", "content-type", "anthropic-version", "anthropic-beta",
                         "authorization", "x-api-key"):
                budget.require(len(self.headers.get_all(name, [])) <= 1, "ambiguous request header")
            budget.require(self.headers.get("Transfer-Encoding") is None, "chunked requests unsupported")
            budget.require(self.headers.get("Content-Encoding") is None, "encoded requests unsupported")
            budget.require(self.headers.get_content_type() == "application/json", "JSON body required")
            budget.require(self.headers.get("anthropic-version") == "2023-06-01",
                           "unregistered API version")
            budget.require(all(name.lower() in ("anthropic-version", "anthropic-beta")
                               for name in self.headers
                               if name.lower().startswith(("anthropic-", "x-anthropic-"))),
                           "unregistered provider capability header")
            length = int(self.headers.get("Content-Length", "-1"))
            budget.require(0 < length <= MAX_BODY, "invalid request body length")
            self.connection.settimeout(30)
            raw = self.rfile.read(length)
            budget.require(len(raw) == length, "truncated request body")
            request = budget.admit_request(raw, self.headers.get("anthropic-beta"))
            connection_tokens = {p.strip().lower() for p in self.headers.get("Connection", "").split(",")}
            budget.require(not connection_tokens.intersection(
                {"authorization", "x-api-key", "anthropic-version", "anthropic-beta"}),
                "capability headers cannot be hop-by-hop")
        except (budget.Refusal, ValueError, TimeoutError, OSError):
            gateway.fail("INCOMPLETE_POLICY")
            self.reply(400, "request is outside the registered price contract")
            return

        request_id = None
        try:
            request_id = gateway.ledger.reserve(gateway.owner, gateway.slot, request)
        except budget.Refusal:
            self.reply(402, "PP-W budget scope stopped; collection is incomplete")
            return
        upstream = None
        cost = None
        usage = None
        try:
            # Reservation is durable before the first upstream byte or connection.
            upstream = gateway.connection_factory()
            headers = {}
            for name, value in self.headers.items():
                if name.lower() not in HOP_HEADERS | connection_tokens | {"accept-encoding"}:
                    headers[name] = value
            headers["Content-Length"] = str(len(raw))
            headers["Accept-Encoding"] = "identity"
            upstream.request("POST", path + ("?" + query if query else ""), body=raw, headers=headers)
            response = upstream.getresponse()
            self.send_response(response.status)
            for name, value in response.getheaders():
                if name.lower() not in HOP_HEADERS:
                    self.send_header(name, value)
            self.send_header("Connection", "close")
            self.end_headers()
            self.close_connection = True
            stream = budget.UsageStream(request) if request["stream"] else None
            content_type = response.getheader("Content-Type", "").split(";")[0].strip().lower()
            correct_type = content_type == ("text/event-stream" if stream else "application/json")
            receipt = bytearray()
            disconnected = False
            while True:
                data = response.read1(16 * 1024)
                if not data:
                    break
                if not disconnected:
                    try:
                        self.wfile.write(data)
                        self.wfile.flush()
                    except (BrokenPipeError, ConnectionResetError, OSError):
                        disconnected = True
                        gateway.fail("INCOMPLETE_INTERRUPTED")
                if stream:
                    stream.feed(data)
                elif len(receipt) <= MAX_RECEIPT:
                    receipt.extend(data)
            if response.status == 200 and correct_type:
                if stream:
                    cost, usage = stream.finish()
                else:
                    budget.require(len(receipt) <= MAX_RECEIPT, "oversized provider receipt")
                    message = budget.decode(receipt)
                    budget.require(isinstance(message, dict) and message.get("type") == "message"
                                   and message.get("role") == "assistant", "invalid provider message")
                    budget.validate_response_content(message.get("content"))
                    cost, usage = budget.reconciled_cost(
                        request, message.get("model"), message.get("usage"), message.get("stop_reason"))
        except (budget.Refusal, OSError, http.client.HTTPException, ValueError, TypeError, AttributeError):
            # Never release an error/truncated/ambiguous charge, even for 4xx/5xx.
            cost, usage = None, None
            self.close_connection = True
        finally:
            if upstream is not None:
                upstream.close()
            gateway.ledger.settle(gateway.owner, request_id, cost, usage)


class Gateway:
    """One slot's endpoint; the request ledger remains shared across the entire pilot."""
    def __init__(self, ledger, owner, slot, connection_factory=provider_connection):
        self.ledger, self.owner, self.slot = ledger, owner, slot
        self.connection_factory = connection_factory
        self.capability = secrets.token_hex(32)
        self.active = True
        self.server = Server(("127.0.0.1", 0), Handler)
        self.server.gateway = self
        self.thread = threading.Thread(target=self.server.serve_forever, name="ppw-budget-gateway")

    @property
    def port(self):
        return self.server.server_address[1]

    @property
    def base_url(self):
        return "http://127.0.0.1:%d/%s" % (self.port, self.capability)

    def start(self):
        self.thread.start()
        return self

    def fail(self, reason):
        self.ledger.stop(self.owner, reason)

    def close(self):
        self.active = False
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()

    def __enter__(self):
        return self.start()

    def __exit__(self, exc_type, exc, tb):
        self.close()
