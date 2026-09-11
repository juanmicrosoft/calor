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
import subprocess
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
PROVIDER_HEADERS = {"anthropic-version", "anthropic-beta", "anthropic-dangerous-direct-browser-access"}


class WireRefusal(budget.Refusal):
    def __init__(self, code):
        super().__init__(code)
        self.code = code


def request_headers(headers):
    """The same nonsecret wire contract is used by the gateway and native probe."""
    def check(condition, code):
        if not condition:
            raise WireRefusal(code)

    for name in PROVIDER_HEADERS | {"content-length", "content-type", "authorization", "x-api-key"}:
        check(len(headers.get_all(name, [])) <= 1, "WIRE_DUPLICATE_HEADER")
    check(headers.get("Transfer-Encoding") is None, "WIRE_TRANSFER_ENCODING")
    check(headers.get("Content-Encoding") is None, "WIRE_CONTENT_ENCODING")
    check(headers.get_content_type() == "application/json", "WIRE_CONTENT_TYPE")
    check(headers.get("anthropic-version") == "2023-06-01", "WIRE_API_VERSION")
    check(all(name.lower() in PROVIDER_HEADERS for name in headers
              if name.lower().startswith(("anthropic-", "x-anthropic-"))),
          "WIRE_UNKNOWN_PROVIDER_HEADER")
    # SDK browser-access opt-in, not a model, billing or server-operation control.
    check(headers.get("anthropic-dangerous-direct-browser-access") in (None, "true"),
          "WIRE_BROWSER_ACCESS_VALUE")
    try:
        length = int(headers.get("Content-Length", "-1"))
    except ValueError as error:
        raise WireRefusal("WIRE_CONTENT_LENGTH") from error
    check(0 < length <= MAX_BODY, "WIRE_CONTENT_LENGTH")
    connection_tokens = {part.strip().lower() for part in headers.get("Connection", "").split(",")
                         if part.strip()}
    check(not connection_tokens.intersection(PROVIDER_HEADERS | {"authorization", "x-api-key"}),
          "WIRE_HOP_CAPABILITY")
    return length, connection_tokens


def priced_endpoint(path, query):
    return path == "/v1/messages" and query in ("", "beta=true")


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
        if not separator:
            return None
        gateway = self.server.gateway
        if hmac.compare_digest(prefix, gateway.capability):
            role = "client"
        elif hmac.compare_digest(prefix, gateway.observer_capability):
            role = "observer"
        else:
            return None
        return role, "/" + path, parsed.query

    def do_HEAD(self):
        if self.authorized_path() == ("client", "/api/hello", ""):
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
        role, path, query = route
        if ((role == "client" and path == "/ppw/observe")
                or (role == "observer" and path in
                    ("/ppw/register", "/ppw/seal", "/ppw/final", "/ppw/source-inspection"))):
            if gateway.observer is None or query:
                gateway.fail("INCOMPLETE_POLICY", "OBSERVER_OPERATION")
                self.reply(400, "observer operation is unavailable")
                return
            try:
                length = int(self.headers.get("Content-Length", "-1"))
                budget.require(self.headers.get_content_type() == "application/json"
                               and 0 <= length <= 16 * 1024, "invalid observer request")
                value = json.loads(self.rfile.read(length))
                operation = path.rsplit("/", 1)[1]
                if operation == "seal":
                    budget.require(value == {}, "seal request must be empty")
                    gateway.active = False
                    result = gateway.observer.seal()
                elif operation == "final":
                    budget.require(value == {}, "final request must be empty")
                    result = gateway.observer.final()
                elif operation == "source-inspection":
                    result = gateway.observer.source_inspection(
                        value["pair"], value["baseline"], value["final"], value["runtime"])
                else:
                    result = getattr(gateway.observer, operation)(value)
                raw = json.dumps(result, sort_keys=True, allow_nan=False).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(raw)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(raw)
                self.close_connection = True
            except (budget.Refusal, ValueError, OSError, KeyError, TypeError,
                    subprocess.SubprocessError):
                gateway.fail("INCOMPLETE_POLICY", "OBSERVER_OPERATION")
                self.reply(400, "observer operation failed")
            return
        if role != "client":
            gateway.fail("INCOMPLETE_POLICY", "OBSERVER_OPERATION")
            self.reply(400, "unsupported observer operation")
            return
        if path == "/v1/messages/count_tokens":
            self.reply(404, "optional token counting is unavailable; never a financial bound")
            return
        if not priced_endpoint(path, query):
            gateway.fail("INCOMPLETE_POLICY", "WIRE_ENDPOINT")
            self.reply(400, "unpriced gateway endpoint")
            return
        try:
            budget.require(gateway.active, "gateway slot is closed")
            length, connection_tokens = request_headers(self.headers)
            self.connection.settimeout(30)
            raw = self.rfile.read(length)
            budget.require(len(raw) == length, "truncated request body")
            request = budget.admit_request(raw, self.headers.get("anthropic-beta"))
        except WireRefusal as error:
            gateway.fail("INCOMPLETE_POLICY", error.code)
            self.reply(400, "request wire contract refused: " + error.code)
            return
        except (budget.Refusal, ValueError, TimeoutError, OSError):
            gateway.fail("INCOMPLETE_POLICY", "PRICE_REQUEST")
            self.reply(400, "request price contract refused: PRICE_REQUEST")
            return

        request_id = None
        try:
            request_id = gateway.ledger.reserve(gateway.owner, gateway.slot, request)
        except budget.Refusal:
            gateway.fail("INCOMPLETE_POLICY", "REQUEST_RESERVATION")
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
    def __init__(self, ledger, owner, slot, connection_factory=provider_connection, observer=None):
        self.ledger, self.owner, self.slot = ledger, owner, slot
        self.connection_factory = connection_factory
        self.capability = secrets.token_hex(32)
        self.observer_capability = secrets.token_hex(32)
        self.observer = observer
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

    @property
    def observer_url(self):
        return self.base_url + "/ppw"

    @property
    def observer_control_url(self):
        return "http://127.0.0.1:%d/%s/ppw" % (self.port, self.observer_capability)

    def start(self):
        self.thread.start()
        return self

    def fail(self, reason, diagnostic=None):
        self.ledger.stop(self.owner, reason, diagnostic=diagnostic)

    def close(self):
        self.active = False
        if self.observer is not None:
            self.observer.cancel()
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()
        if self.observer is not None:
            self.observer.close()

    def __enter__(self):
        return self.start()

    def __exit__(self, exc_type, exc, tb):
        self.close()
