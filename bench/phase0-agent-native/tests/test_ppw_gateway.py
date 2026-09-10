"""Deterministic loopback provider doubles only. Never invokes a model or real provider."""
from concurrent.futures import ThreadPoolExecutor
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import shutil
import socket
import sys
import threading
import time
import unittest
from unittest.mock import Mock, patch
import uuid

BENCH = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("gateway_test", BENCH / "ppw-budget-gateway.py")
gateway = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gateway)
budget = gateway.budget


def body(**changes):
    value = {"model": budget.MODEL, "max_tokens": 128, "messages": [
        {"role": "user", "content": "SYNTHETIC ENGINEERING FIXTURE"}]}
    value.update(changes)
    return json.dumps(value).encode()


def usage(**changes):
    result = {"input_tokens": 100, "output_tokens": 7, "cache_creation_input_tokens": 20,
              "cache_read_input_tokens": 50, "service_tier": "standard"}
    result.update(changes)
    return result


def expected_charge():
    return budget.reconciled_cost(budget.admit_request(body()), budget.MODEL, usage(), "end_turn")[0]


def message(**changes):
    result = {"id": "SYNTHETIC-response", "type": "message", "role": "assistant",
              "model": budget.MODEL, "content": [{"type": "text", "text": "SYNTHETIC"}],
              "stop_reason": "end_turn", "usage": usage()}
    result.update(changes)
    return result


def event(value):
    return ("event: %s\ndata: %s\n\n" % (value["type"], json.dumps(value))).encode()


def stream_bytes():
    return [
        b": SYNTHETIC keepalive\n\n", event({"type": "ping"}),
        event({"type": "message_start", "message": message(
            content=[], usage=usage(output_tokens=0), stop_reason=None)}),
        event({"type": "content_block_start", "index": 0,
               "content_block": {"type": "text", "text": ""}}),
        event({"type": "content_block_delta", "index": 0,
               "delta": {"type": "text_delta", "text": "SYNTHETIC"}}),
        event({"type": "content_block_stop", "index": 0}),
        event({"type": "message_delta", "delta": {"stop_reason": "end_turn"},
               "usage": {"output_tokens": 7}}),
        event({"type": "message_stop"}),
    ]


class Fixture(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".gateway-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)

    def ledger(self, ceiling=500_000_000):
        ledger = budget.RequestLedger(self.root / ("SYNTHETIC-" + uuid.uuid4().hex + ".sqlite3"))
        ledger.initialize({"stage": "pilot", "epochId": "SYNTHETIC", "priceSha256": budget.price_identity(),
                           "authorization": "SYNTHETIC"}, ceiling)
        return ledger, ledger.start()


class PriceTests(Fixture):
    def test_real_supported_model_has_positive_implemented_request_bound(self):
        request = budget.admit_request(body(max_tokens=budget.OUTPUT))
        self.assertGreater(request["maximumMicroUsd"], 0)
        self.assertEqual(budget.upper_cost(budget.CONTEXT, budget.OUTPUT), request["maximumMicroUsd"])
        self.assertGreater(request["maximumMicroUsd"] * 444, 500_000_000)
        ledger, owner = self.ledger()
        self.assertTrue(ledger.reserve(owner, "SYNTHETIC-slot", request))

    def test_cache_prewarming_zero_output_is_not_a_free_request(self):
        self.assertGreater(budget.admit_request(body(max_tokens=0))["maximumMicroUsd"], 0)

    def test_verified_usage_categories_and_fast_residency_multipliers_round_up(self):
        request = budget.admit_request(body())
        counters = usage(speed="standard", inference_geo="global", cache_creation={
            "ephemeral_5m_input_tokens": 10, "ephemeral_1h_input_tokens": 10})
        self.assertEqual(863, budget.reconciled_cost(request, budget.MODEL, counters, "end_turn")[0])
        counters.update(speed="fast", inference_geo="us")
        self.assertEqual(1898, budget.reconciled_cost(request, budget.MODEL, counters, "end_turn")[0])

    def test_unpriced_models_fields_server_tools_and_overlimits_refuse(self):
        for changes in (
            {"model": "unregistered"}, {"max_tokens": True}, {"max_tokens": -1},
            {"max_tokens": budget.OUTPUT + 1}, {"container": "server-container"},
            {"mcp_servers": []}, {"new_billable_feature": True}, {"speed": "unknown"},
            {"inference_geo": "unknown"}, {"service_tier": "batch"},
            {"tools": [{"type": "code_execution_20260120"}]},
            {"tools": [{"type": "web_search_20250305", "max_uses": 1}]},
            {"tools": [{"name": "x", "allowed_callers": ["code_execution_20260120"]}]},
            {"context_management": {"edits": [{"type": "compact_20260112"}]}},
            {"context_management": {"edits": None}},
            {"context_management": {"edits": {"type": "compact_20260112"}}},
            {"context_management": {"edits": [
                {"type": "clear_thinking_20251015", "fallbacks": "default"}]}},
            {"fallbacks": "default"}, {"fallbacks": ["claude-opus-4-8"]},
            {"thinking": {"type": "adaptive", "fallbacks": "default"}},
            {"output_config": {"effort": "high", "fallbacks": "default"}},
            {"tools": [{"name": "local", "fallbacks": "default"}]},
        ):
            with self.subTest(changes=changes), self.assertRaises(budget.Refusal):
                budget.admit_request(body(**changes))

    def test_beta_capabilities_are_closed_without_rewriting_approved_requests(self):
        for header in ("compact-2026-01-12", "server-side-fallback-2026-07-01",
                       "server-side-fallback-2026-06-01", "output-300k-2026-03-24",
                       "context-1m-2025-08-07,unknown", "", "context-1m-2025-08-07,",
                       "context-1m-2025-08-07,context-1m-2025-08-07"):
            with self.subTest(header=header), self.assertRaises(budget.Refusal):
                budget.admit_request(body(), header)
        header = "context-management-2025-06-27, \tcontext-1m-2025-08-07"
        request = budget.admit_request(body(context_management={
            "edits": [{"type": "clear_thinking_20251015", "keep": "all"}]}), header)
        self.assertEqual(["context-management-2025-06-27", "context-1m-2025-08-07"],
                         request["betaCapabilities"])

    def test_duplicate_keys_and_nonfinite_numbers_refuse(self):
        for value in (b'{"model":"x","model":"y"}', b'{"temperature":NaN}'):
            with self.assertRaises(budget.Refusal):
                budget.admit_request(value)

    def test_nested_billing_controls_refuse_but_tool_payloads_remain_opaque(self):
        for changes in (
            {"tools": [{"type": []}]},
            {"tools": [{"name": "local", "cache_control": {"type": "ephemeral", "ttl": "24h"}}]},
            {"system": [{"type": "text", "text": "SYNTHETIC",
                         "cache_control": {"type": "ephemeral", "ttl": "24h"}}]},
            {"messages": [{"role": "user", "content": "SYNTHETIC",
                           "cache_control": {"type": "ephemeral", "ttl": "24h"}}]},
            {"messages": [{"role": "assistant", "content": [{"type": "compaction"}]}]},
            {"messages": [{"role": "user", "content": [{"type": "tool_result", "tool_use_id": "x",
                           "content": [{"type": "text", "text": "SYNTHETIC",
                                        "cache_control": {"type": "ephemeral", "ttl": "24h"}}]}]}]},
        ):
            with self.subTest(changes=changes), self.assertRaises(budget.Refusal):
                budget.admit_request(body(**changes))
        raw = body(messages=[{"role": "assistant", "content": [{
            "type": "tool_use", "id": "SYNTHETIC", "name": "local",
            "input": {"fallbacks": "user data, not an API control",
                      "cache_control": {"ttl": "user data"}},
            "cache_control": {"type": "ephemeral", "ttl": "1h"},
        }]}])
        self.assertGreater(budget.admit_request(raw)["maximumMicroUsd"], 0)

    def test_usage_requires_all_counters_model_tier_and_consistent_cache_breakdown(self):
        request = budget.admit_request(body())
        bad = [usage(output_tokens=129), usage(input_tokens=-1), usage(service_tier=None),
               usage(server_tool_use={"web_search_requests": 1}),
               usage(new_cost=1), usage(input_tokens=budget.CONTEXT),
               usage(iterations=[{"type": "compaction", "output_tokens": 1}]),
               usage(iterations=[{"type": "fallback_message", "model": budget.MODEL}]),
               usage(cache_creation={"ephemeral_5m_input_tokens": 2, "ephemeral_1h_input_tokens": 3})]
        missing = usage()
        del missing["cache_read_input_tokens"]
        bad.append(missing)
        for values in bad:
            with self.subTest(values=values), self.assertRaises(budget.Refusal):
                budget.reconciled_cost(request, budget.MODEL, values, "end_turn")
        with self.assertRaises(budget.Refusal):
            budget.reconciled_cost(request, "different", usage(), "end_turn")

    def test_sse_receipt_is_incremental_and_requires_final_usage_and_stop(self):
        request = budget.admit_request(body(stream=True))
        tracker = budget.UsageStream(request)
        raw = b"".join(stream_bytes())
        for byte in raw:
            tracker.feed(bytes([byte]))
        self.assertEqual(expected_charge(), tracker.finish()[0])
        for malformed in (b"".join(stream_bytes()[:-1]), raw + event({"type": "error"}),
                          raw.replace(b'"output_tokens": 7', b'"output_tokens": -7')):
            tracker = budget.UsageStream(request)
            tracker.feed(malformed)
            with self.assertRaises(budget.Refusal):
                tracker.finish()


class AdmissionTests(Fixture):
    def fixture(self):
        from ppw_redesign_epoch import instrument, save
        spending = instrument.helper("ppw-spending.py")
        isolation = spending.module("ppw-gateway-client.py")
        registration = {
            "compilerCommit": "a" * 40, "tasks": ["SYNTHETIC-1", "SYNTHETIC-2", "SYNTHETIC-3"],
            "artifacts": {"SYNTHETIC/pair.json": "b" * 64},
        }
        selected = {
            "runsPerArm": 74, "modelPin": budget.MODEL, "agentVersion": isolation.CLIENT_VERSION,
            "stageRegistration": {"path": "SYNTHETIC-method.json", "sha256": "c" * 64},
            "modelRegistration": {"path": "SYNTHETIC-model.json", "sha256": "d" * 64},
            "spendAuthorization": {"path": "SYNTHETIC-approval.json", "sha256": "e" * 64},
        }
        amendment = self.root / "SYNTHETIC-amendment.json"
        save(amendment, {"schemaVersion": 1, "kind": "pp-w-prospective-spending-instrument-amendment",
                         "stage": "pilot", "replacementHarnessArtifacts":
                             spending.artifact_manifest(spending.GATEWAY)})
        selected["instrumentAmendment"] = {"path": amendment.name, "sha256": spending.digest(amendment)}
        prices = self.root / "SYNTHETIC-prices.json"
        save(prices, budget.price_contract())
        binding = {"anchor": "git-common-dir", "relativePath": "ppw-budget/epic1254-pilot.sqlite3",
                   "anchorSha256": "f" * 64}
        authorization = {"spendingCeilingUsd": 500, "ledgerBinding": binding}
        plan = {
            "schemaVersion": 1, "kind": "pp-w-pilot-spending-plan", "epochId": "SYNTHETIC-pilot",
            "stage": "pilot", "authorizationSha256": selected["spendAuthorization"]["sha256"],
            "protocolSha256": spending.protocol_identity(registration, selected, "pilot", "SYNTHETIC-pilot"),
            "ceilingUsd": 500, "costBasis": "both-list-price-study-cost-and-actual-spend",
            "plannedInvocations": 444, "ledgerBinding": binding,
            "clientControl": {
                "kind": spending.GATEWAY, "isolation": isolation.ISOLATION,
                "priceContract": {"path": prices.name, "sha256": spending.digest(prices)},
                "clientExecutable": str(self.root / "SYNTHETIC-client"),
                "shellExecutable": str(self.root / "SYNTHETIC-shell"), "shellSha256": "1" * 64,
                "runtimeSha256": "2" * 64,
            },
        }
        return spending, isolation, registration, selected, authorization, plan

    def check(self, values):
        from ppw_redesign_epoch import save
        spending, isolation, registration, selected, authorization, plan = values
        path = self.root / "SYNTHETIC-plan.json"
        save(path, plan)
        selected["spendingPlan"] = {"path": path.name, "sha256": spending.digest(path)}
        isolated = Mock(ISOLATION=isolation.ISOLATION, CLIENT_VERSION=isolation.CLIENT_VERSION)
        isolated.validate_client.side_effect = Path
        isolated.validate_shell.side_effect = lambda path, sha: Path(path)
        modules = {"ppw-gateway-budget.py": budget, "ppw-gateway-client.py": isolated}
        with patch.object(spending, "module", side_effect=modules.__getitem__), \
                patch.object(spending, "gateway_ledger_location",
                             return_value=self.root / "SYNTHETIC-never-created.sqlite3"):
            admission = spending.admit(registration, selected, authorization, self.root,
                                       "SYNTHETIC-pilot", "pilot")
        isolated.validate_platform.assert_called_once_with()
        isolated.validate_client.assert_called_once_with(plan["clientControl"]["clientExecutable"])
        isolated.validate_shell.assert_called_once_with(plan["clientControl"]["shellExecutable"], "1" * 64)
        return admission

    def test_source_bound_gateway_admits_all_444_slots_without_per_run_budget_sizing(self):
        values = self.fixture()
        admission = self.check(values)
        self.assertEqual(444, len(admission["slots"]))
        self.assertEqual(444, len({slot["id"] for slot in admission["slots"]}))
        self.assertEqual(500_000_000, admission["ceilingUnits"])
        self.assertEqual(budget.KIND, admission["mechanism"])
        self.assertGreater(budget.admit_request(body())["maximumMicroUsd"] * 444,
                           admission["ceilingUnits"])
        self.assertFalse(Path(admission["ledgerPath"]).exists())

    def test_price_manifest_stage_and_shared_budget_cannot_be_asserted_away(self):
        from ppw_redesign_epoch import save
        for change in ("price", "source", "stage", "ledger", "model"):
            values = self.fixture()
            spending, _, _, selected, _, plan = values
            if change == "price":
                prices = self.root / plan["clientControl"]["priceContract"]["path"]
                save(prices, dict(budget.price_contract(), contextUpperTokens=1))
                plan["clientControl"]["priceContract"]["sha256"] = spending.digest(prices)
            elif change == "source":
                amendment = self.root / selected["instrumentAmendment"]["path"]
                document = json.loads(amendment.read_text())
                document["replacementHarnessArtifacts"]["ppw-gateway-budget.py"] = "0" * 64
                save(amendment, document)
                selected["instrumentAmendment"]["sha256"] = spending.digest(amendment)
            elif change == "stage":
                plan["stage"] = "confirmatory"
            elif change == "ledger":
                plan["ledgerBinding"] = dict(plan["ledgerBinding"], anchorSha256="0" * 64)
            else:
                selected["modelPin"] = "SYNTHETIC-unregistered-model"
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.check(values)
        self.assertFalse((self.root / "SYNTHETIC-never-created.sqlite3").exists())


class LedgerTests(Fixture):
    def test_complete_provider_usage_releases_unused_request_reservation(self):
        ledger, owner = self.ledger()
        request = budget.admit_request(body())
        for _ in range(100):
            request_id = ledger.reserve(owner, "SYNTHETIC-slot", request)
            cost, counts = budget.reconciled_cost(request, budget.MODEL, usage(), "end_turn")
            ledger.settle(owner, request_id, cost, counts)
        snapshot = ledger.snapshot()
        self.assertEqual(100, len(snapshot["requests"]))
        self.assertEqual(100 * expected_charge(), snapshot["exposureMicroUsd"])
        ledger.complete(owner)
        self.assertIsNone(ledger.snapshot()["verdict"])

    def test_exhaustion_commits_refusal_before_throwing(self):
        request = budget.admit_request(body())
        ledger, owner = self.ledger(request["maximumMicroUsd"] - 1)
        with self.assertRaisesRegex(budget.Refusal, "INCOMPLETE_BUDGET"):
            ledger.reserve(owner, "SYNTHETIC-slot", request)
        self.assertEqual("INCOMPLETE_BUDGET", ledger.snapshot()["state"])
        self.assertEqual([], ledger.snapshot()["requests"])
        with self.assertRaises(budget.Refusal):
            ledger.complete(owner)

    def test_concurrent_requests_cannot_overreserve_the_total(self):
        ledger, owner = self.ledger()
        request = budget.admit_request(body())
        def attempt(_):
            try:
                return ledger.reserve(owner, "SYNTHETIC-slot", request)
            except budget.Refusal:
                return None
        with ThreadPoolExecutor(max_workers=25) as pool:
            ids = [x for x in pool.map(attempt, range(40)) if x]
        snapshot = ledger.snapshot()
        self.assertEqual(len(ids), len(set(ids)))
        self.assertLessEqual(snapshot["exposureMicroUsd"], 500_000_000)
        self.assertEqual("INCOMPLETE_BUDGET", snapshot["state"])

    def test_unknown_interrupted_charge_stays_reserved_and_no_retry_or_reset(self):
        ledger, owner = self.ledger()
        request = budget.admit_request(body())
        request_id = ledger.reserve(owner, "SYNTHETIC-slot", request)
        self.assertEqual(request["maximumMicroUsd"], budget.RequestLedger(ledger.path).snapshot()["exposureMicroUsd"])
        ledger.settle(owner, request_id)
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", ledger.snapshot()["state"])
        self.assertEqual(request["maximumMicroUsd"], ledger.snapshot()["exposureMicroUsd"])
        with self.assertRaises(budget.Refusal):
            ledger.reserve(owner, "SYNTHETIC-slot", request)
        with self.assertRaises(budget.Refusal):
            ledger.start()
        with self.assertRaises(budget.Refusal):
            ledger.settle(owner, request_id, 0, usage())
        with self.assertRaises(budget.Refusal):
            ledger.initialize({"stage": "pilot", "priceSha256": budget.price_identity(),
                               "authorization": "another receipt"}, 500_000_000)


class TransportTests(Fixture):
    def provider(self, payload=None, status=200, streaming=False, pause=False, chunks=None):
        observed = []
        received_first = threading.Event()
        release = threading.Event()
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass
            def do_POST(self):
                raw = self.rfile.read(int(self.headers["Content-Length"]))
                observed.append((self.path, dict(self.headers), raw))
                self.send_response(status)
                self.send_header("Content-Type", "text/event-stream" if streaming else "application/json")
                self.end_headers()
                parts = chunks if chunks is not None else (
                    stream_bytes() if streaming else [json.dumps(payload or message()).encode()])
                for index, chunk in enumerate(parts):
                    self.wfile.write(chunk)
                    self.wfile.flush()
                    if index == 0 and pause:
                        received_first.set()
                        release.wait(10)
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        worker = threading.Thread(target=server.serve_forever)
        worker.start()
        self.addCleanup(worker.join)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        self.addCleanup(release.set)
        return lambda: http.client.HTTPConnection("127.0.0.1", server.server_port), observed, release

    def send(self, proxy, raw, suffix="/v1/messages?beta=true", headers=None):
        client = http.client.HTTPConnection("127.0.0.1", proxy.port, timeout=10)
        request_headers = {
            "Content-Type": "application/json", "anthropic-version": "2023-06-01",
            "anthropic-beta": "context-management-2025-06-27, context-1m-2025-08-07",
            "Authorization": "Bearer SYNTHETIC-credential-never-persist",
        }
        request_headers.update(headers or {})
        client.request("POST", "/" + proxy.capability + suffix, raw, request_headers)
        response = client.getresponse()
        return client, response

    def test_positive_proxy_forwards_exact_body_query_and_capability_headers(self):
        factory, observed, _ = self.provider()
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            raw = body()
            client, response = self.send(proxy, raw)
            self.assertEqual(200, response.status)
            self.assertEqual(message(), json.loads(response.read()))
            client.close()
        self.assertEqual("/v1/messages?beta=true", observed[0][0])
        self.assertEqual(raw, observed[0][2])
        self.assertEqual("context-management-2025-06-27, context-1m-2025-08-07",
                         observed[0][1]["anthropic-beta"])
        self.assertEqual("Bearer SYNTHETIC-credential-never-persist", observed[0][1]["Authorization"])
        self.assertEqual("reconciled", ledger.snapshot()["requests"][0]["state"])
        self.assertNotIn(b"SYNTHETIC-credential", ledger.path.read_bytes())
        self.assertNotIn(b"SYNTHETIC ENGINEERING", ledger.path.read_bytes())

    def test_stream_relay_delivers_ping_before_completion_and_preserves_all_bytes(self):
        factory, observed, release = self.provider(streaming=True, pause=True)
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(proxy, body(stream=True))
            first = response.read(len(stream_bytes()[0]))
            self.assertEqual(stream_bytes()[0], first)
            self.assertFalse(release.is_set())
            release.set()
            self.assertEqual(b"".join(stream_bytes()), first + response.read())
            client.close()
        self.assertEqual("reconciled", ledger.snapshot()["requests"][0]["state"])

    def test_unpriced_and_budget_denied_requests_never_reach_upstream(self):
        factory, observed, _ = self.provider()
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(proxy, body(container="unpriced"))
            self.assertEqual(400, response.status)
            response.read()
            client.close()
        self.assertEqual([], observed)
        self.assertEqual("INCOMPLETE_POLICY", ledger.snapshot()["state"])
        ledger, owner = self.ledger(1)
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(proxy, body())
            self.assertEqual(402, response.status)
            response.read()
            client.close()
        self.assertEqual([], observed)

    def test_provider_error_retains_entire_reservation_and_never_follows_redirects(self):
        for status in (302, 400, 429, 500):
            with self.subTest(status=status):
                factory, observed, _ = self.provider(status=status, payload={"type": "error"})
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                    client, response = self.send(proxy, body())
                    response.read()
                    client.close()
                self.assertEqual(1, len(observed))
                self.assertEqual("unknown", ledger.snapshot()["requests"][0]["state"])
                self.assertEqual(budget.admit_request(body())["maximumMicroUsd"],
                                 ledger.snapshot()["exposureMicroUsd"])


    def test_compaction_fallback_and_unknown_capabilities_refuse_before_connection(self):
        factory, observed, _ = self.provider()
        connections = []
        def counted_connection():
            connections.append(True)
            return factory()
        cases = [
            (body(context_management={"edits": [{"type": "compact_20260112",
                                                "pause_after_compaction": True}]}), {}),
            (body(fallbacks="default"), {}),
            (body(fallbacks=[budget.MODEL]), {}),
            (body(), {"anthropic-beta": "compact-2026-01-12"}),
            (body(), {"anthropic-beta": "server-side-fallback-2026-07-01"}),
            (body(), {"anthropic-beta": "context-1m-2025-08-07, unknown"}),
            (body(), {"anthropic-unpriced-feature": "enabled"}),
            (body(), {"Connection": "anthropic-beta"}),
        ]
        for raw, headers in cases:
            with self.subTest(raw=raw, headers=headers):
                ledger, owner = self.ledger()
                with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", counted_connection) as proxy:
                    client, response = self.send(proxy, raw, headers=headers)
                    self.assertEqual(400, response.status)
                    response.read()
                    client.close()
                self.assertEqual([], connections)
                self.assertEqual([], observed)
                self.assertEqual([], ledger.snapshot()["requests"])
                self.assertEqual("INCOMPLETE_POLICY", ledger.snapshot()["state"])

    def test_unexpected_server_iterations_never_release_the_reservation(self):
        for kind in ("compaction", "fallback"):
            for streaming in (False, True):
                with self.subTest(kind=kind, streaming=streaming):
                    chunks = stream_bytes()
                    chunks[3] = event({"type": "content_block_start", "index": 0,
                                       "content_block": {"type": kind}})
                    factory, observed, _ = self.provider(
                        payload=message(content=[{"type": kind}]), streaming=streaming,
                        chunks=chunks if streaming else None)
                    ledger, owner = self.ledger()
                    with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
                        client, response = self.send(proxy, body(stream=streaming))
                        response.read()
                        client.close()
                    self.assertEqual(1, len(observed))
                    self.assertEqual("unknown", ledger.snapshot()["requests"][0]["state"])
                    self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", ledger.snapshot()["state"])
                    self.assertEqual(budget.admit_request(body())["maximumMicroUsd"],
                                     ledger.snapshot()["exposureMicroUsd"])


class IsolationTests(Fixture):
    @unittest.skipUnless(sys.platform == "darwin", "actual macOS kernel policy")
    def test_kernel_blocks_other_network_state_access_and_outside_signals(self):
        spec = importlib.util.spec_from_file_location("kernel_test", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected = (self.root / name for name in ("work", "output", "protected"))
        for path in (work, output, protected):
            path.mkdir()
        listener = socket.socket()
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        self.addCleanup(listener.close)
        port = listener.getsockname()[1]
        result = isolation.kernel_probe(isolation.sandbox_policy(work, output, protected, port),
                                        work, protected, port, os.getpid())
        self.assertTrue(result["kernelProbe"]["gateway"])
        self.assertFalse(result["kernelProbe"]["outsideSignal"])
        self.assertFalse(result["modelInvoked"])


if __name__ == "__main__":
    unittest.main()
