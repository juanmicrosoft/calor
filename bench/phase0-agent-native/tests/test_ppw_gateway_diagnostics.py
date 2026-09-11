"""Synthetic local transport only; never loads actual epochs or financial databases."""
from concurrent.futures import ThreadPoolExecutor
from contextlib import redirect_stderr
import http.client
import io
import json
import socket
import ssl
import threading
from unittest.mock import Mock

import test_ppw_gateway as fixtures

budget = fixtures.budget
gateway = fixtures.gateway
PRIVATE = "SYNTHETIC_PRIVATE_CREDENTIAL_PROMPT_OR_PROVIDER_TEXT"


class DiagnosticTests(fixtures.Fixture):
    provider = fixtures.TransportTests.provider
    send = fixtures.TransportTests.send

    def failure(self, ledger, code, phase, status=None, response_type="unobserved"):
        snapshot = ledger.snapshot()
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", snapshot["state"])
        self.assertEqual(sum(row["reserved"] for row in snapshot["requests"]),
                         snapshot["exposureMicroUsd"])
        self.assertTrue(all(row["state"] == "unknown" and row["usage"] is None
                            for row in snapshot["requests"]))
        events = [row for row in snapshot["events"] if row["kind"] == "unknown-charge-retained"]
        self.assertEqual(1, len(events))
        self.assertEqual(snapshot["requests"][0]["id"], events[0]["request_id"])
        diagnostic = budget.decode(events[0]["detail"])["diagnostic"]
        self.assertEqual(budget.PROVIDER_FAILURE_KIND, diagnostic["kind"])
        self.assertEqual(code, diagnostic["code"])
        self.assertEqual(phase, diagnostic["phase"])
        self.assertEqual(status, diagnostic["httpStatus"])
        self.assertEqual(response_type, diagnostic["responseType"])
        self.assertNotIn(PRIVATE.encode(), ledger.path.read_bytes())
        return diagnostic

    def synthetic_connection(self, payload=None, status=200, content_type="application/json",
                             encoding="identity"):
        response = Mock()
        response.status = status
        headers = {"Content-Type": content_type, "Content-Encoding": encoding}
        response.getheader.side_effect = lambda name, default=None: headers.get(name, default)
        response.getheaders.return_value = list(headers.items())
        response.read1.side_effect = [payload or json.dumps(fixtures.message()).encode(), b""]
        connection = Mock()
        connection.getresponse.return_value = response
        return connection

    def test_observed_geography_category_is_a_control_not_a_historical_receipt(self):
        for streaming in (False, True):
            with self.subTest(streaming=streaming):
                # Only this enum is drawn from the technical observation; every count,
                # content block and terminal event below is explicitly synthetic.
                values = fixtures.usage(inference_geo="not_available")
                chunks = fixtures.stream_bytes()
                chunks[2] = fixtures.event({"type": "message_start", "message": fixtures.message(
                    content=[], usage=values, stop_reason=None)})
                factory, observed, _ = self.provider(
                    payload=fixtures.message(usage=values), streaming=streaming,
                    chunks=chunks if streaming else None)
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                    client, response = self.send(proxy, fixtures.body(stream=streaming))
                    self.assertEqual(200, response.status)
                    response.read()
                    client.close()
                diagnostic = self.failure(ledger, "USAGE_GEOGRAPHY", "receipt-validation",
                                          200, "sse" if streaming else "json")
                self.assertEqual({"started": True, "terminalDeltaSeen": True, "stopped": True}
                                 if streaming else None, diagnostic["streamState"])
                self.assertEqual(1, len(observed))

    def test_tls_timeout_connection_and_protocol_errors_never_persist_exception_text(self):
        cases = (
            (ssl.SSLCertVerificationError(PRIVATE), "PROVIDER_TLS_CERTIFICATE"),
            (ssl.SSLError(PRIVATE), "PROVIDER_TLS"),
            (TimeoutError(PRIVATE), "PROVIDER_TIMEOUT"),
            (socket.gaierror(PRIVATE), "PROVIDER_DNS"),
            (ConnectionResetError(PRIVATE), "PROVIDER_CONNECTION"),
            (http.client.IncompleteRead(PRIVATE.encode()), "PROVIDER_HTTP_TRUNCATED"),
            (http.client.HTTPException(PRIVATE), "PROVIDER_HTTP_PROTOCOL"),
            (OSError(PRIVATE), "PROVIDER_IO"),
            (ValueError(PRIVATE), "PROVIDER_DATA_SHAPE"),
            (budget.Refusal(PRIVATE), "PROVIDER_SCHEMA"),
        )
        for error, code in cases:
            with self.subTest(code=code):
                ledger, owner = self.ledger()
                factory = Mock(side_effect=error)
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                    client = http.client.HTTPConnection("127.0.0.1", proxy.port, timeout=10)
                    try:
                        client.request("POST", "/" + proxy.capability + "/v1/messages",
                                       fixtures.body(), {"Content-Type": "application/json",
                                                         "anthropic-version": "2023-06-01"})
                        with self.assertRaises(http.client.RemoteDisconnected):
                            client.getresponse()
                    finally:
                        client.close()
                factory.assert_called_once()
                self.failure(ledger, code, "connect")

    def test_status_type_and_encoding_refusals_are_categorical(self):
        cases = (
            (429, "application/json", "identity", "PROVIDER_HTTP_STATUS", "json"),
            (302, PRIVATE, "identity", "PROVIDER_HTTP_STATUS", "other"),
            (200, PRIVATE, "identity", "PROVIDER_CONTENT_TYPE", "other"),
            (200, "", "identity", "PROVIDER_CONTENT_TYPE", "absent"),
            (200, "application/json", PRIVATE, "PROVIDER_CONTENT_ENCODING", "json"),
        )
        for status, content_type, encoding, code, category in cases:
            with self.subTest(code=code, status=status):
                connection = self.synthetic_connection(
                    payload=PRIVATE.encode(), status=status,
                    content_type=content_type, encoding=encoding)
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", lambda: connection) as proxy:
                    client, response = self.send(proxy, fixtures.body())
                    response.read()
                    client.close()
                self.failure(ledger, code, "response-headers", status, category)

    def test_transport_phase_is_not_a_claim_of_successful_forwarding(self):
        for operation, phase in (("request", "send-request"), ("getresponse", "response-headers"),
                                 ("read1", "response-body")):
            with self.subTest(operation=operation):
                connection = self.synthetic_connection()
                target = (connection.getresponse.return_value if operation == "read1" else connection)
                getattr(target, operation).side_effect = TimeoutError(PRIVATE)
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", lambda: connection) as proxy:
                    client = http.client.HTTPConnection("127.0.0.1", proxy.port, timeout=10)
                    try:
                        client.request("POST", "/" + proxy.capability + "/v1/messages",
                                       fixtures.body(), {"Content-Type": "application/json",
                                                         "anthropic-version": "2023-06-01"})
                        if operation == "read1":
                            client.getresponse().read()
                        else:
                            with self.assertRaises(http.client.RemoteDisconnected):
                                client.getresponse()
                    finally:
                        client.close()
                self.failure(ledger, "PROVIDER_TIMEOUT", phase,
                             200 if operation == "read1" else None,
                             "json" if operation == "read1" else "unobserved")
                connection.close.assert_called_once()

    def test_unexpected_exception_is_accounted_without_a_traceback_or_phase_rewrite(self):
        for operation, phase in (("getresponse", "response-headers"), ("close", "upstream-close")):
            with self.subTest(operation=operation):
                connection = self.synthetic_connection()
                getattr(connection, operation).side_effect = RuntimeError(PRIVATE)
                ledger, owner = self.ledger()
                errors = io.StringIO()
                with redirect_stderr(errors), \
                        gateway.Gateway(ledger, owner, "SYNTHETIC-slot", lambda: connection) as proxy:
                    client = http.client.HTTPConnection("127.0.0.1", proxy.port, timeout=10)
                    try:
                        client.request("POST", "/" + proxy.capability + "/v1/messages",
                                       fixtures.body(), {"Content-Type": "application/json",
                                                         "anthropic-version": "2023-06-01"})
                        if operation == "close":
                            client.getresponse().read()
                        else:
                            with self.assertRaises(http.client.RemoteDisconnected):
                                client.getresponse()
                    finally:
                        client.close()
                self.failure(ledger, "PROVIDER_UNEXPECTED_FAILURE", phase,
                             200 if operation == "close" else None,
                             "json" if operation == "close" else "unobserved")
                self.assertEqual("", errors.getvalue())
                connection.close.assert_called_once()

    def test_known_header_failure_halts_before_error_body_finishes(self):
        factory, observed, release = self.provider(
            status=429, streaming=True, pause=True, chunks=[b": SYNTHETIC\n\n"])
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(proxy, fixtures.body(stream=True))
            try:
                self.assertFalse(release.is_set())
                self.failure(ledger, "PROVIDER_HTTP_STATUS", "response-headers", 429, "sse")
                later, refusal = self.send(proxy, fixtures.body())
                self.assertEqual(402, refusal.status)
                refusal.read()
                later.close()
                self.assertEqual(1, len(observed))
                self.assertEqual(1, len(ledger.snapshot()["requests"]))
            finally:
                release.set()
                response.read()
                client.close()

    def test_first_stream_parser_cause_is_retained_before_remaining_bytes(self):
        raw = b"data: " + PRIVATE.encode() + b"\n\n"
        factory, _, release = self.provider(streaming=True, pause=True, chunks=[raw])
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(proxy, fixtures.body(stream=True))
            try:
                self.assertEqual(raw, response.read(len(raw)))
                # Receiving the relayed bytes does not imply the observer has run yet.
                deadline = fixtures.time.monotonic() + 5
                while ledger.snapshot()["state"] == "collecting" and fixtures.time.monotonic() < deadline:
                    fixtures.time.sleep(0.01)
                self.failure(ledger, "PROVIDER_JSON_INVALID", "response-body", 200, "sse")
                self.assertFalse(release.is_set())
            finally:
                release.set()
                response.read()
                client.close()

    def test_stream_refusals_keep_the_specific_first_category(self):
        mutations = (
            ({"type": "message_delta", "delta": {"stop_reason": "end_turn", PRIVATE: PRIVATE},
              "usage": {"output_tokens": 7}}, "SSE_DELTA_SCHEMA"),
            ({"type": "message_delta", "delta": {"stop_reason": "end_turn"},
              "usage": {"output_tokens": -1}}, "SSE_USAGE_DECREASED"),
            ({"type": PRIVATE, "secret": PRIVATE}, "SSE_UNADMITTED_EVENT"),
        )
        for replacement, code in mutations:
            with self.subTest(code=code):
                chunks = fixtures.stream_bytes()
                chunks[-2] = fixtures.event(replacement)
                factory, _, _ = self.provider(streaming=True, chunks=chunks)
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                    client, response = self.send(proxy, fixtures.body(stream=True))
                    response.read()
                    client.close()
                diagnostic = self.failure(ledger, code, "response-body", 200, "sse")
                self.assertTrue(diagnostic["streamState"]["started"])
                self.assertFalse(diagnostic["streamState"]["stopped"])

    def test_truncated_stream_and_incomplete_usage_remain_unknown(self):
        for streaming in (False, True):
            with self.subTest(streaming=streaming):
                factory, _, _ = self.provider(
                    streaming=streaming, chunks=fixtures.stream_bytes()[:-1] if streaming else None,
                    payload=fixtures.message(usage=fixtures.usage(cache_read_input_tokens=None)))
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                    client, response = self.send(proxy, fixtures.body(stream=streaming))
                    response.read()
                    client.close()
                self.failure(ledger, "SSE_INCOMPLETE" if streaming else "USAGE_TOKEN_COUNT",
                             "receipt-validation", 200, "sse" if streaming else "json")

    def test_cleanup_failure_cannot_skip_retention_or_replace_the_first_cause(self):
        for earlier in (False, True):
            with self.subTest(earlier=earlier):
                connection = self.synthetic_connection(
                    payload=PRIVATE.encode() if earlier else None)
                connection.close.side_effect = OSError(PRIVATE)
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", lambda: connection) as proxy:
                    client, response = self.send(proxy, fixtures.body())
                    response.read()
                    client.close()
                self.failure(ledger, "PROVIDER_JSON_INVALID" if earlier else "PROVIDER_IO",
                             "receipt-validation" if earlier else "upstream-close", 200, "json")

    def test_two_synthetic_inflight_failures_remain_distinct_then_402_never_reserves(self):
        barrier = threading.Barrier(2)
        factory_calls = []

        def factory():
            factory_calls.append(True)
            connection = self.synthetic_connection(payload=PRIVATE.encode())
            connection.request.side_effect = lambda *args, **kwargs: barrier.wait(timeout=5)
            return connection

        ledger, owner = self.ledger(1_000_000_000)
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            def invoke(_):
                client, response = self.send(proxy, fixtures.body(max_tokens=64_000))
                response.read()
                client.close()
            with ThreadPoolExecutor(max_workers=2) as pool:
                list(pool.map(invoke, range(2)))
            # Gateway.close joins its request handlers; wait for both settlements
            # before asking for the later refusal while the listener is still open.
            deadline = fixtures.time.monotonic() + 5
            while (any(row["state"] == "reserved" for row in ledger.snapshot()["requests"])
                   and fixtures.time.monotonic() < deadline):
                fixtures.time.sleep(0.01)
            before = ledger.snapshot()
            later, refusal = self.send(proxy, fixtures.body())
            self.assertEqual(402, refusal.status)
            self.assertIn(b"PP-W budget scope stopped", refusal.read())
            later.close()
        after = ledger.snapshot()
        self.assertEqual(2, len(factory_calls))
        self.assertEqual(2, len(after["requests"]))
        self.assertEqual(51_040_000, after["exposureMicroUsd"])
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", after["state"])
        self.assertEqual(before["events"], after["events"])
        failures = [row for row in after["events"] if row["kind"] == "unknown-charge-retained"]
        self.assertEqual({row["id"] for row in after["requests"]},
                         {row["request_id"] for row in failures})
        self.assertEqual(2, len(failures))
        self.assertTrue(all(budget.decode(row["detail"])["diagnostic"]["code"] ==
                            "PROVIDER_JSON_INVALID" for row in failures))
        self.assertNotIn(PRIVATE.encode(), ledger.path.read_bytes())
        with self.assertRaises(budget.Refusal):
            ledger.start()

    def test_diagnostic_schema_refuses_arbitrary_strings_and_reconciliation(self):
        valid = {"kind": budget.PROVIDER_FAILURE_KIND, "code": "PROVIDER_TIMEOUT",
                 "phase": "response-body", "httpStatus": 200, "responseType": "sse",
                 "streamState": {"started": True, "terminalDeltaSeen": False, "stopped": False}}
        for changes in (
            {"code": PRIVATE}, {"phase": PRIVATE}, {"responseType": PRIVATE},
            {"httpStatus": PRIVATE}, {"httpStatus": True}, {"exception": PRIVATE},
            {"streamState": {"started": PRIVATE}}, {"code": [PRIVATE]},
        ):
            with self.subTest(fields=list(changes)):
                ledger, owner = self.ledger()
                request = budget.admit_request(fixtures.body())
                identity = ledger.reserve(owner, "SYNTHETIC-slot", request)
                before = ledger.snapshot()
                with self.assertRaises(budget.Refusal):
                    ledger.settle(owner, identity, diagnostic=dict(valid, **changes))
                self.assertEqual(before, ledger.snapshot())
                self.assertNotIn(PRIVATE.encode(), ledger.path.read_bytes())
        ledger, owner = self.ledger()
        request = budget.admit_request(fixtures.body())
        identity = ledger.reserve(owner, "SYNTHETIC-slot", request)
        cost, receipt = budget.reconciled_cost(request, budget.MODEL, fixtures.usage(), "end_turn")
        with self.assertRaisesRegex(budget.Refusal, "cannot reconcile"):
            ledger.settle(owner, identity, cost, receipt, diagnostic=valid)
        ledger.settle(owner, identity)
        event = ledger.snapshot()["events"][-1]
        self.assertEqual({}, budget.decode(event["detail"]))

    def test_successful_receipt_keeps_existing_prices_and_has_no_failure_record(self):
        connection = self.synthetic_connection()
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", lambda: connection) as proxy:
            client, response = self.send(proxy, fixtures.body())
            response.read()
            client.close()
        snapshot = ledger.snapshot()
        self.assertEqual("collecting", snapshot["state"])
        self.assertEqual(fixtures.expected_charge(), snapshot["exposureMicroUsd"])
        self.assertEqual("reconciled", snapshot["requests"][0]["state"])
        self.assertFalse(any(row["kind"] == "unknown-charge-retained" for row in snapshot["events"]))
