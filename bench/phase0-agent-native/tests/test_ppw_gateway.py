"""Deterministic loopback provider doubles only. Never invokes a model or real provider."""
from concurrent.futures import ThreadPoolExecutor
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time
import unittest
from unittest.mock import Mock, patch
from urllib.parse import urlsplit
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

    def ledger(self, ceiling=500_000_000, planned=None, terminal=False):
        ledger = budget.RequestLedger(self.root / ("SYNTHETIC-" + uuid.uuid4().hex + ".sqlite3"))
        binding = {"stage": "pilot", "epochId": "SYNTHETIC", "priceSha256": budget.price_identity(),
                   "authorization": "SYNTHETIC"}
        if planned is not None:
            binding["plannedSlots"] = planned
        if terminal:
            binding["harnessArtifacts"] = budget.source_identities()
        ledger.initialize(binding, ceiling)
        return ledger, ledger.start()

    def terminal_attempt(self, slot="task/calor-permissive/1", exit_code=0,
                         reason=None, agent_result='{"result":"SYNTHETIC_DONE"}\n'):
        directory = self.root / "archive/runs/task/calor-permissive/run-1"
        directory.mkdir(parents=True)
        (directory / "final-src").mkdir()
        (directory / "final-src/Source.calr").write_text("§M{m:Synthetic}\n", encoding="utf-8")
        budget.write_attempt_start(directory, self.root / "archive", slot, 1)
        (directory / "client-invocation.json").write_text(
            json.dumps({"exitCode": exit_code}) + "\n", encoding="utf-8")
        if agent_result is not None:
            (directory / "agent.json").write_text(agent_result, encoding="utf-8")
        if reason is None:
            reason = next(value for value, code in budget.TERMINAL_REASON_CODES.items()
                          if code == "MISSING_TRANSCRIPT")
        (directory / "invalid.txt").write_text(
            "2026-09-11T00:00:00Z attempt=0 agent_rc=%d: %s\n" % (exit_code, reason),
            encoding="utf-8")
        (directory / "result.json").write_text(json.dumps({
            "pair": "task", "arm": "calor-permissive", "run": 1,
            "invalid": True, "censored": True,
        }) + "\n", encoding="utf-8")
        if budget.valid_client_exit(exit_code):
            budget.write_terminal_attempt(directory, self.root / "archive", slot, reason)
        return directory

    def isolation_evidence(self):
        return {
            "kind": budget.ISOLATION_KIND,
            "kernelProbe": dict(budget.ISOLATION_PROBE),
            "modelInvoked": False,
            "clientSha256": "1" * 64,
            "policySha256": "2" * 64,
            "workspaceRoot": str(self.root / "workspace"),
            "authoritativeRoot": str(self.root / "archive"),
        }


class PriceTests(Fixture):
    def test_native_advertised_advisor_beta_does_not_admit_server_advisor_or_url_fetch(self):
        native = "oauth-2025-04-20,advisor-tool-2026-03-01,thinking-token-count-2026-05-13"
        self.assertGreater(budget.admit_request(body(), native)["maximumMicroUsd"], 0)
        with self.assertRaises(budget.Refusal):
            budget.admit_request(body(tools=[{
                "type": "advisor_20260301", "name": "advisor", "model": budget.MODEL,
                "max_tokens": 1024, "max_uses": 1,
            }]), native)
        for kind in ("image", "document"):
            with self.subTest(kind=kind), self.assertRaises(budget.Refusal):
                budget.admit_request(body(messages=[{"role": "user", "content": [{
                    "type": kind, "source": {"type": "url", "url": "https://example.invalid/SYNTHETIC"},
                }]}]), native)

    def test_provider_thinking_detail_and_single_iteration_are_not_double_billed(self):
        request = budget.admit_request(body())
        counters = usage(output_tokens_details={"thinking_tokens": 6},
                         iterations=None, fallback_credit=None, speed=None, inference_geo=None)
        expected = budget.reconciled_cost(request, budget.MODEL, counters, "end_turn")[0]
        counters["iterations"] = [{
            "type": "message", "model": budget.MODEL,
            **{name: counters[name] for name in budget.COUNTERS}, "cache_creation": None,
        }]
        cost, receipt = budget.reconciled_cost(request, budget.MODEL, counters, "end_turn")
        self.assertEqual(expected, cost)
        self.assertEqual(counters, receipt["usage"])
        self.assertEqual(budget.MODEL, receipt["model"])
        self.assertEqual("end_turn", receipt["stopReason"])
        counters["iterations"].append(dict(counters["iterations"][0]))
        with self.assertRaises(budget.Refusal):
            budget.reconciled_cost(request, budget.MODEL, counters, "end_turn")

    def test_exact_metadata_limits_and_native_output_request_are_source_bound(self):
        self.assertEqual(1_000_000, budget.CONTEXT)
        self.assertEqual(128_000, budget.OUTPUT)
        source = budget.price_contract()["modelLimitsSource"]
        self.assertEqual({"id": budget.MODEL, "max_input_tokens": 1_000_000, "max_tokens": 128_000},
                         source["responseFields"])
        self.assertEqual("GET", source["method"])
        self.assertEqual(0, source["upstreamInferenceRequests"])
        self.assertEqual(25_520_000, budget.admit_request(body(max_tokens=64_000))["maximumMicroUsd"])
        self.assertEqual(29_040_000, budget.admit_request(body(max_tokens=128_000))["maximumMicroUsd"])
        for overlimit in (128_001, 131_072):
            with self.subTest(overlimit=overlimit), self.assertRaises(budget.Refusal):
                budget.admit_request(body(max_tokens=overlimit))

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

    def test_service_tier_uses_published_categories_and_honors_standard_only(self):
        automatic = budget.admit_request(body())
        standard = budget.admit_request(body(service_tier="standard_only"))
        self.assertEqual("auto", automatic["serviceTier"])
        self.assertEqual("standard_only", standard["serviceTier"])
        counters = usage(speed="standard", inference_geo="global")
        amount = budget.reconciled_cost(automatic, budget.MODEL, counters, "end_turn")[0]
        self.assertEqual(amount, budget.reconciled_cost(standard, budget.MODEL, counters, "end_turn")[0])
        counters["service_tier"] = "priority"
        self.assertEqual(amount, budget.reconciled_cost(automatic, budget.MODEL, counters, "end_turn")[0])
        with self.assertRaisesRegex(budget.Refusal, "service tier differs"):
            budget.reconciled_cost(standard, budget.MODEL, counters, "end_turn")

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
        authorization = {"spendingCeilingUsd": 1000, "ledgerBinding": binding}
        for proof in spending.FORECAST_EVIDENCE.values():
            destination = self.root / proof["path"]
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(BENCH / "registrations/ppw-rows-stage1" / proof["path"], destination)
        proposal = json.loads((self.root / spending.FORECAST_EVIDENCE["proposal"]["path"]).read_text())
        plan = {
            "schemaVersion": 1, "kind": "pp-w-pilot-spending-plan", "epochId": "SYNTHETIC-pilot",
            "stage": "pilot", "authorizationSha256": selected["spendAuthorization"]["sha256"],
            "protocolSha256": spending.protocol_identity(registration, selected, "pilot", "SYNTHETIC-pilot"),
            "ceilingUsd": 1000, "costBasis": "both-list-price-study-cost-and-actual-spend",
            "plannedInvocations": 444, "ledgerBinding": binding,
            "forecast": dict(proposal["forecast"], status="registered"),
            "forecastRegistration": {"from": "proposed", "to": "registered",
                                     "reviewReference": spending.FORECAST_REVIEW,
                                     "artifacts": spending.FORECAST_EVIDENCE},
            "clientControl": {
                "kind": spending.GATEWAY, "isolation": isolation.ISOLATION,
                "priceContract": {"path": prices.name, "sha256": spending.digest(prices)},
                "clientExecutable": str(self.root / "SYNTHETIC-client"),
                "shellExecutable": str(self.root / "SYNTHETIC-shell"), "shellSha256": "1" * 64,
                "runtimeSha256": "2" * 64,
                "sourceInspector": {"kind": "SYNTHETIC-source-inspector-runtime"},
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
        inspector = Mock()
        modules = {"ppw-gateway-budget.py": budget, "ppw-gateway-client.py": isolated,
                   "ppw-source-inspection.py": inspector}
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
        self.assertEqual(1_000_000_000, admission["ceilingUnits"])
        self.assertEqual(values[0].FORECAST_EVIDENCE, admission["forecastEvidence"])
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
    def test_slot_completion_requires_accounted_traffic_exit_and_entire_inventory(self):
        ledger, owner = self.ledger(planned=["SYNTHETIC-1", "SYNTHETIC-2"])
        request = budget.admit_request(body())
        evidence = {"kind": "SYNTHETIC-isolation-not-real-evidence"}
        with self.assertRaisesRegex(budget.Refusal, "unregistered gateway slot"):
            ledger.reserve(owner, "foreign", request)
        with self.assertRaisesRegex(budget.Refusal, "lacks complete"):
            ledger.complete_slot(owner, "SYNTHETIC-1", evidence, 0)
        first = ledger.reserve(owner, "SYNTHETIC-1", request)
        with self.assertRaisesRegex(budget.Refusal, "lacks complete"):
            ledger.complete_slot(owner, "SYNTHETIC-1", evidence, 0)
        ledger.settle(owner, first, *budget.reconciled_cost(request, budget.MODEL, usage(), "end_turn"))
        for code in (None, True, -9, -15, 124, 137, 143):
            with self.subTest(code=code), self.assertRaisesRegex(budget.Refusal, "interrupted"):
                ledger.complete_slot(owner, "SYNTHETIC-1", evidence, code)
        ledger.complete_slot(owner, "SYNTHETIC-1", evidence, 0)
        with self.assertRaisesRegex(budget.Refusal, "duplicate"):
            ledger.complete_slot(owner, "SYNTHETIC-1", evidence, 0)
        with self.assertRaisesRegex(budget.Refusal, "registered slot inventory"):
            ledger.complete(owner)
        second = ledger.reserve(owner, "SYNTHETIC-2", request)
        ledger.settle(owner, second, *budget.reconciled_cost(request, budget.MODEL, usage(), "end_turn"))
        ledger.complete_slot(owner, "SYNTHETIC-2", evidence, 0)
        ledger.complete(owner)
        events = [budget.decode(row["detail"]) for row in ledger.snapshot()["events"]
                  if row["kind"] == "slot-complete"]
        self.assertEqual(["SYNTHETIC-1", "SYNTHETIC-2"], [value["slot"] for value in events])
        self.assertEqual([evidence, evidence], [value["isolation"] for value in events])

    def test_terminal_invalid_accepts_zero_or_reconciled_requests_and_consumes_slots(self):
        slots = ["task/calor-permissive/1", "task/calor-strict/1"]
        ledger, owner = self.ledger(planned=slots, terminal=True)
        evidence = self.isolation_evidence()
        first = self.terminal_attempt(slots[0])
        terminal = ledger.complete_invalid_slot(
            owner, slots[0], first, self.root / "archive", evidence,
            budget.source_identities())
        self.assertEqual("MISSING_TRANSCRIPT", terminal["classification"])

        request = budget.admit_request(body())
        request_id = ledger.reserve(owner, slots[1], request)
        ledger.settle(
            owner, request_id,
            *budget.reconciled_cost(request, budget.MODEL, usage(), "end_turn"))
        second = self.root / "archive/runs/task/calor-strict/run-1"
        shutil.copytree(first, second)
        start = json.loads((second / "attempt-start.json").read_text())
        start["slot"] = slots[1]
        (second / "attempt-start.json").write_text(
            budget.canonical(start) + "\n", encoding="utf-8")
        (second / "invalid-terminal.json").unlink()
        result = json.loads((second / "result.json").read_text())
        result["arm"] = "calor-strict"
        (second / "result.json").write_text(json.dumps(result) + "\n", encoding="utf-8")
        reason = next(value for value, code in budget.TERMINAL_REASON_CODES.items()
                      if code == "MISSING_TRANSCRIPT")
        budget.write_terminal_attempt(second, self.root / "archive", slots[1], reason)
        ledger.complete_invalid_slot(
            owner, slots[1], second, self.root / "archive", evidence,
            budget.source_identities())
        ledger.complete(owner)
        snapshot = ledger.snapshot()
        self.assertEqual("complete", snapshot["state"])
        self.assertEqual(
            slots,
            [budget.decode(event["detail"])["slot"] for event in snapshot["events"]
             if event["kind"] == budget.TERMINAL_INVALID_EVENT])

    def test_terminal_invalid_refuses_forgery_order_unknown_charge_and_stopped_scope(self):
        slots = ["task/calor-permissive/1", "task/calor-strict/1"]
        evidence = self.isolation_evidence()
        mutations = (
            (lambda directory: (directory / "invalid-terminal.json").unlink(), "record is missing"),
            (lambda directory: (directory / "client-invocation.json").write_text(
                '{"exitCode":1}\n', encoding="utf-8"), "invocation evidence differs"),
            (lambda directory: (directory / "final-src/Source.calr").write_text(
                "changed", encoding="utf-8"), "sealed source differs"),
            (lambda directory: (directory / "attempt-start.json").write_text(
                (directory / "attempt-start.json").read_text().replace(
                    budget.source_identities()["run-pair.sh"], "0" * 64),
                encoding="utf-8"), "source"),
        )
        for mutation, message in mutations:
            with self.subTest(message=message):
                ledger, owner = self.ledger(planned=slots, terminal=True)
                directory = self.terminal_attempt(slots[0])
                mutation(directory)
                with self.assertRaisesRegex(budget.Refusal, message):
                    ledger.complete_invalid_slot(
                        owner, slots[0], directory, self.root / "archive", evidence,
                        budget.source_identities())
                shutil.rmtree(self.root / "archive")

        ledger, owner = self.ledger(planned=slots, terminal=True)
        directory = self.terminal_attempt(slots[0])
        forged_isolation = self.isolation_evidence()
        forged_isolation["kernelProbe"]["authoritativeRead"] = True
        with self.assertRaisesRegex(budget.Refusal, "isolation"):
            ledger.complete_invalid_slot(
                owner, slots[0], directory, self.root / "archive", forged_isolation,
                budget.source_identities())
        request = budget.admit_request(body())
        with self.assertRaisesRegex(budget.Refusal, "out of registered order"):
            ledger.reserve(owner, slots[1], request)
        with self.assertRaisesRegex(budget.Refusal, "unregistered"):
            ledger.reserve(owner, "foreign/arm/1", request)
        request_id = ledger.reserve(owner, slots[0], request)
        with self.assertRaises(budget.Refusal):
            ledger.settle(owner, request_id, 0, {
                "model": budget.MODEL, "stopReason": "end_turn", "usage": {},
            })
        with self.assertRaisesRegex(budget.Refusal, "in-flight or unknown"):
            ledger.complete_invalid_slot(
                owner, slots[0], directory, self.root / "archive", evidence,
                budget.source_identities())
        ledger.settle(owner, request_id)
        with self.assertRaisesRegex(budget.Refusal, "incomplete scope"):
            ledger.complete_invalid_slot(
                owner, slots[0], directory, self.root / "archive", evidence,
                budget.source_identities())

        for reason in ("INCOMPLETE_POLICY", "INCOMPLETE_INTERRUPTED"):
            shutil.rmtree(self.root / "archive")
            ledger, owner = self.ledger(planned=slots, terminal=True)
            directory = self.terminal_attempt(slots[0])
            ledger.stop(owner, reason)
            with self.subTest(reason=reason), self.assertRaisesRegex(
                    budget.Refusal, "incomplete scope"):
                ledger.complete_invalid_slot(
                    owner, slots[0], directory, self.root / "archive", evidence,
                    budget.source_identities())

    def test_terminal_invalid_rejects_api_integrity_and_interrupted_failures(self):
        for reason in (
                'agent output matches error marker: "api error"',
                "generated workspace policy/configuration changed during the run",
                "trusted observer did not archive declared-done source"):
            with self.subTest(reason=reason), self.assertRaisesRegex(
                    budget.Refusal, "not an admitted terminal"):
                budget.terminal_reason_code(reason)
        directory = self.terminal_attempt(exit_code=124)
        reason = next(value for value, code in budget.TERMINAL_REASON_CODES.items()
                      if code == "MISSING_TRANSCRIPT")
        with self.assertRaisesRegex(budget.Refusal, "noninterrupted"):
            budget.write_terminal_attempt(
                directory, self.root / "archive", "task/calor-permissive/1", reason)

    def test_registered_nonzero_no_work_attempt_is_invalid_not_replaced(self):
        slot = "task/calor-permissive/1"
        ledger, owner = self.ledger(planned=[slot], terminal=True)
        directory = self.terminal_attempt(
            slot, exit_code=1, reason="agent exit code 1 with empty journal.jsonl")
        terminal = ledger.complete_invalid_slot(
            owner, slot, directory, self.root / "archive",
            self.isolation_evidence(), budget.source_identities())
        self.assertEqual(1, terminal["clientExitCode"])
        self.assertEqual("CLIENT_EXIT_WITHOUT_OBSERVED_WORK", terminal["classification"])
        ledger.complete(owner)
        self.assertEqual(1, ledger.snapshot()["invalidTerminalSlots"])
        self.assertEqual([], ledger.snapshot()["requests"])

    def test_invalid_reason_predicates_and_hashes_are_required(self):
        slot = "task/calor-permissive/1"
        for reason, raw, code in (
                ("agent.json missing or empty", None, "MISSING_AGENT_RESULT"),
                ("agent.json is not valid JSON", "SYNTHETIC-not-json", "MALFORMED_AGENT_RESULT")):
            with self.subTest(code=code):
                directory = self.terminal_attempt(slot, reason=reason, agent_result=raw)
                terminal = budget.validate_terminal_attempt(
                    directory, self.root / "archive", slot)
                self.assertEqual(code, terminal["classification"])
                (directory / "agent.json").write_text('{"result":"SYNTHETIC"}\n')
                with self.assertRaises(budget.Refusal):
                    budget.validate_terminal_attempt(directory, self.root / "archive", slot)
                shutil.rmtree(self.root / "archive")
        directory = self.terminal_attempt(
            slot, exit_code=1, reason="agent exit code 1 with empty journal.jsonl")
        (directory / "journal.jsonl").write_text('{"cmd":"build"}\n')
        with self.assertRaisesRegex(budget.Refusal, "observed work"):
            budget.validate_terminal_attempt(directory, self.root / "archive", slot)
        (directory / "journal.jsonl").unlink()
        (directory / "agent.json").write_text('{"result":"SYNTHETIC-altered"}\n')
        with self.assertRaisesRegex(budget.Refusal, "reason evidence changed"):
            budget.validate_terminal_attempt(directory, self.root / "archive", slot)

    def test_source_bound_valid_completion_requires_exact_attempt_and_isolation(self):
        slot = "task/calor-permissive/1"
        ledger, owner = self.ledger(planned=[slot], terminal=True)
        directory = self.terminal_attempt(slot)
        attempt = budget.validate_attempt_start(directory, self.root / "archive", slot)
        evidence = self.isolation_evidence()
        request = budget.admit_request(body())
        identity = ledger.reserve(owner, slot, request)
        ledger.settle(owner, identity, *budget.reconciled_cost(
            request, budget.MODEL, usage(), "end_turn"))
        for altered in (None, dict(attempt, slot="foreign"), dict(attempt, nonce="missing")):
            with self.subTest(attempt=altered), self.assertRaises(budget.Refusal):
                ledger.complete_slot(owner, slot, evidence, 1, attempt=altered)
        with self.assertRaises(budget.Refusal):
            ledger.complete_slot(owner, slot, {"kind": "untrusted"}, 1, attempt=attempt)
        ledger.complete_slot(owner, slot, evidence, 1, attempt=attempt)
        ledger.complete(owner)
        self.assertEqual(1, ledger.snapshot()["validCompletedSlots"])

    def test_archived_terminal_origin_survives_relocation_not_origin_changes(self):
        slot = "task/calor-permissive/1"
        directory = self.terminal_attempt(slot)
        old_root = self.root / "archive"
        relocated = self.root / "relocated-archive"
        old_root.rename(relocated)
        moved = relocated / directory.relative_to(old_root)
        record = budget.validate_terminal_attempt(
            moved, relocated, slot, recorded_authoritative_root=str(old_root))
        self.assertEqual("MISSING_TRANSCRIPT", record["classification"])
        with self.assertRaisesRegex(budget.Refusal, "attempt-start evidence differs"):
            budget.validate_terminal_attempt(
                moved, relocated, slot, recorded_authoritative_root=str(self.root / "foreign"))
        with self.assertRaisesRegex(budget.Refusal, "attempt-start evidence differs"):
            budget.validate_terminal_attempt(moved, relocated, slot)

    def test_nonzero_exit_with_observed_work_keeps_legacy_valid_completion_guard(self):
        ledger, owner = self.ledger(planned=["SYNTHETIC-slot"])
        request = budget.admit_request(body())
        identity = ledger.reserve(owner, "SYNTHETIC-slot", request)
        ledger.settle(owner, identity, *budget.reconciled_cost(
            request, budget.MODEL, usage(), "end_turn"))
        ledger.complete_slot(owner, "SYNTHETIC-slot", self.isolation_evidence(), 1)
        ledger.complete(owner)
        self.assertEqual("complete", ledger.snapshot()["state"])

    def test_completion_without_planned_inventory_still_refuses_duplicates(self):
        ledger, owner = self.ledger()
        request = budget.admit_request(body())
        identity = ledger.reserve(owner, "SYNTHETIC-slot", request)
        ledger.settle(owner, identity, *budget.reconciled_cost(
            request, budget.MODEL, usage(), "end_turn"))
        ledger.complete_slot(owner, "SYNTHETIC-slot", self.isolation_evidence(), 0)
        with self.assertRaisesRegex(budget.Refusal, "duplicate"):
            ledger.complete_slot(owner, "SYNTHETIC-slot", self.isolation_evidence(), 0)
        with self.assertRaisesRegex(budget.Refusal, "duplicate"):
            ledger.reserve(owner, "SYNTHETIC-slot", request)

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
        self.assertEqual(request, budget.decode(snapshot["requests"][0]["request"]))
        self.assertEqual("auto", budget.decode(snapshot["requests"][0]["request"])["serviceTier"])
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

    def test_native_sdk_browser_header_passes_exactly_without_admitting_other_headers(self):
        factory, observed, _ = self.provider()
        ledger, owner = self.ledger()
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", factory) as proxy:
            client, response = self.send(
                proxy, body(), headers={"anthropic-dangerous-direct-browser-access": "true"})
            self.assertEqual(200, response.status)
            response.read()
            client.close()
        self.assertEqual("true", observed[0][1]["anthropic-dangerous-direct-browser-access"])
        for extra, code in (
            ({"anthropic-dangerous-direct-browser-access": "false"}, "WIRE_BROWSER_ACCESS_VALUE"),
            ({"anthropic-dangerous-direct-browser-access": "true",
              "anthropic-unpriced-feature": "SYNTHETIC_PRIVATE"}, "WIRE_UNKNOWN_PROVIDER_HEADER"),
            ({"anthropic-dangerous-direct-browser-access": "true",
              "Connection": "anthropic-dangerous-direct-browser-access"}, "WIRE_HOP_CAPABILITY"),
        ):
            rejected, rejected_owner = self.ledger()
            calls = []
            def never_connect():
                calls.append(True)
                raise AssertionError("wire refusal must precede upstream")
            with gateway.Gateway(rejected, rejected_owner, "SYNTHETIC-slot", never_connect) as proxy:
                client, response = self.send(proxy, body(), headers=extra)
                self.assertEqual(400, response.status)
                self.assertIn(code, response.read().decode())
                client.close()
            self.assertEqual([], calls)
            snapshot = rejected.snapshot()
            self.assertEqual([], snapshot["requests"])
            self.assertEqual({"reason": "INCOMPLETE_POLICY", "diagnostic": code},
                             budget.decode(snapshot["events"][-1]["detail"]))
            self.assertNotIn(b"SYNTHETIC_PRIVATE", rejected.path.read_bytes())

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
        self.assertEqual("INCOMPLETE_BUDGET", ledger.snapshot()["state"])

    def test_reservation_refusal_halts_before_zero_request_invalid_can_advance(self):
        slot = "task/calor-permissive/1"
        for failure in ("unregistered-slot", "invalid-price-bound"):
            with self.subTest(failure=failure):
                ledger, owner = self.ledger(planned=[slot], terminal=True)
                factory = Mock(side_effect=AssertionError("refusal must precede upstream"))
                admission = budget.admit_request(body())
                if failure == "invalid-price-bound":
                    admission = dict(admission, maximumMicroUsd=1)
                selected_slot = "foreign/arm/1" if failure == "unregistered-slot" else slot
                with patch.object(gateway.budget, "admit_request", return_value=admission), \
                        gateway.Gateway(ledger, owner, selected_slot, factory) as proxy:
                    client, response = self.send(proxy, body())
                    self.assertEqual(402, response.status)
                    response.read()
                    client.close()
                factory.assert_not_called()
                snapshot = ledger.snapshot()
                self.assertEqual("INCOMPLETE_POLICY", snapshot["state"])
                self.assertEqual([], snapshot["requests"])
                self.assertEqual({
                    "reason": "INCOMPLETE_POLICY", "diagnostic": "REQUEST_RESERVATION",
                }, budget.decode(snapshot["events"][-1]["detail"]))
                directory = self.terminal_attempt(slot)
                with self.assertRaisesRegex(budget.Refusal, "incomplete scope"):
                    ledger.complete_invalid_slot(
                        owner, slot, directory, self.root / "archive",
                        self.isolation_evidence(), budget.source_identities())
                shutil.rmtree(self.root / "archive")

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
    def test_observer_endpoint_exposes_only_typed_local_operations(self):
        ledger, owner = self.ledger(planned=["SYNTHETIC-slot"])
        observer = Mock()
        observer.observe.return_value = {"ok": True}
        observer.register.return_value = {"ok": True}
        observer.seal.return_value = {"ok": True, "archivedFiles": 1}
        observer.final.return_value = {"buildOk": True}
        with gateway.Gateway(ledger, owner, "SYNTHETIC-slot", observer=observer) as proxy:
            for url, operation, value, expected in (
                (proxy.observer_url, "observe",
                 {"kind": "ppw-dotnet-observation-v1", "command": "build",
                  "exitCode": 0, "feedbackLatencyMs": 1}, {"ok": True}),
                (proxy.observer_control_url, "register",
                 {"kind": "ppw-observer-registration-v1", "workspace": "/SYNTHETIC",
                  "output": "/SYNTHETIC"}, {"ok": True}),
                (proxy.observer_control_url, "seal", {},
                 {"ok": True, "archivedFiles": 1}),
                (proxy.observer_control_url, "final", {}, {"buildOk": True}),
            ):
                endpoint = urlsplit(url)
                client = http.client.HTTPConnection(endpoint.hostname, endpoint.port, timeout=10)
                client.request("POST", endpoint.path + "/" + operation, json.dumps(value), {
                    "Content-Type": "application/json",
                })
                response = client.getresponse()
                self.assertEqual(200, response.status)
                self.assertEqual(expected, json.loads(response.read()))
                client.close()
            endpoint = urlsplit(proxy.observer_url)
            client = http.client.HTTPConnection(endpoint.hostname, endpoint.port, timeout=10)
            client.request("POST", endpoint.path + "/final", "{}", {"Content-Type": "application/json"})
            response = client.getresponse()
            self.assertEqual(400, response.status)
            response.read()
            client.close()
        observer.observe.assert_called_once()
        observer.register.assert_called_once()
        observer.seal.assert_called_once()
        observer.final.assert_called_once()
        observer.cancel.assert_called_once()
        observer.close.assert_called_once()

    @unittest.skipIf(os.name == "nt", "private evidence uses Unix permission bits")
    def test_evidence_is_private_append_only_and_bound_to_the_exact_policy(self):
        spec = importlib.util.spec_from_file_location("isolation_test", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected = (self.root / name for name in ("work", "output", "protected"))
        for path in (work, output, protected):
            path.mkdir()
        hidden = self.root / "hidden"
        hidden.mkdir()
        hidden_test = hidden / "HeldOutTests.cs"
        seeded = hidden / "seeded.calr"
        hidden_test.write_text("hidden")
        seeded.write_text("seeded")
        context_path = protected / "SYNTHETIC-invocation.json"
        context_path.write_text(json.dumps({"protectedRoot": str(protected),
                                           "baseUrl": "http://127.0.0.1:12345/SYNTHETIC",
                                           "hiddenRoots": [],
                                           "repositoryReadDenyRoots": []}))
        evidence = {
            "kind": isolation.ISOLATION, "kernelProbe": dict(isolation.PROBE_EXPECTATIONS),
            "modelInvoked": False, "clientSha256": isolation.CLIENT_SHA256,
            "policySha256": isolation.hashlib.sha256(
                isolation.sandbox_policy(work, output, protected, 12345, ()).encode()).hexdigest(),
            "workspaceRoot": str(work), "authoritativeRoot": str(output),
        }
        isolation.write_isolation_evidence(context_path, evidence)
        path = isolation.isolation_evidence_path(context_path)
        self.assertEqual(protected, path.parent)
        self.assertEqual(0, path.stat().st_mode & 0o077)
        with self.assertRaises(FileExistsError):
            isolation.write_isolation_evidence(context_path, evidence)
        (output / "gateway-isolation.json").write_text('{"SYNTHETIC":"untrusted child output"}')
        self.assertEqual(evidence, isolation.read_isolation_evidence(context_path, work, output))
        with self.assertRaisesRegex(ValueError, "paths differ"):
            isolation.read_isolation_evidence(context_path, work, work / "different-output")
        path.chmod(0o644)
        with self.assertRaisesRegex(ValueError, "private"):
            isolation.read_isolation_evidence(context_path, work, output)

    def test_git_inventory_parsers_are_nul_safe_portable_and_fail_closed(self):
        spec = importlib.util.spec_from_file_location(
            "git_inventory_parser", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        raw = (
            b"worktree /tmp/SYNTHETIC main\0HEAD " + b"a" * 40
            + b"\0branch refs/heads/main\0\0"
            + b"worktree /tmp/SYNTHETIC linked\0HEAD " + b"b" * 40
            + b"\0detached\0locked test-only\0\0"
        )
        self.assertEqual(
            ["/tmp/SYNTHETIC main", "/tmp/SYNTHETIC linked"],
            isolation.parse_worktree_porcelain(raw))
        objects = self.root / "SYNTHETIC-objects"
        relative = self.root / "SYNTHETIC-relative-objects"
        absolute = self.root / "SYNTHETIC-absolute-objects"
        for path in (objects, relative, absolute):
            path.mkdir()
        self.assertEqual(
            [relative, absolute],
            isolation.parse_alternate_object_directories(
                ("../SYNTHETIC-relative-objects\n%s\n" % absolute).encode(), objects))
        for malformed in (
            raw[:-1],
            raw.replace(b"branch refs/heads/main", b"unknown value"),
            raw.replace(b"HEAD " + b"a" * 40 + b"\0", b""),
        ):
            with self.subTest(malformed=malformed), self.assertRaises(ValueError):
                isolation.parse_worktree_porcelain(malformed)
        with self.assertRaises(ValueError):
            isolation.parse_alternate_object_directories(b"\n", objects)

    def test_current_repository_discovery_is_explicit_and_does_not_hide_public_source(self):
        spec = importlib.util.spec_from_file_location(
            "current_repository_discovery", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        repository = BENCH.parents[1]
        roots = set(map(Path, isolation.discover_sensitive_roots((repository,))))
        common = Path(subprocess.check_output(
            ["git", "-C", str(repository), "rev-parse", "--path-format=absolute",
             "--git-common-dir"], text=True).strip())
        self.assertIn(common, roots)
        worktrees = isolation.parse_worktree_porcelain(subprocess.check_output(
            ["git", "-C", str(repository), "worktree", "list", "--porcelain", "-z"]))
        for worktree in map(Path, worktrees):
            self.assertIn(worktree / "bench/phase0-agent-native/tasks", roots)
        self.assertNotIn(BENCH / "ppw-gateway-client.py", roots)

    @unittest.skipUnless(sys.platform == "darwin", "actual macOS git storage policy")
    def test_registered_worktree_loose_packed_and_alternate_objects_are_hidden(self):
        spec = importlib.util.spec_from_file_location(
            "git_storage_policy", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        repository = self.root / "SYNTHETIC-repository"
        other = self.root / "SYNTHETIC-other-worktree"
        workspace = self.root / "SYNTHETIC-visible-workspace"
        output = self.root / "SYNTHETIC-output"
        for path in (repository, workspace, output):
            path.mkdir()

        def git(*arguments):
            result = subprocess.run(
                ["git", "-C", str(repository), *arguments],
                capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            return result.stdout.strip()

        git("init", "-b", "main")
        git("config", "user.name", "SYNTHETIC")
        git("config", "user.email", "synthetic@example.invalid")
        hidden_relative = "bench/phase0-agent-native/tasks/SYNTHETIC/tests/HeldOutTests.cs"
        public_relative = "bench/phase0-agent-native/helpers/public-helper.txt"
        hidden = repository / hidden_relative
        public = repository / public_relative
        hidden.parent.mkdir(parents=True)
        public.parent.mkdir(parents=True)
        hidden.write_text("SYNTHETIC HIDDEN")
        public.write_text("SYNTHETIC PUBLIC")
        git("add", ".")
        git("commit", "-m", "SYNTHETIC fixture")
        git("worktree", "add", "-b", "synthetic-linked", str(other), "HEAD")
        common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir"))
        objects = common / "objects"
        alternate = self.root / "SYNTHETIC-alternate-objects"
        (alternate / "info").mkdir(parents=True)
        alternate_sentinel = alternate / "SYNTHETIC-object"
        alternate_sentinel.write_text("SYNTHETIC ALTERNATE")
        (objects / "info/alternates").write_text(str(alternate) + "\n")
        protected = common / "ppw-budget"
        protected.mkdir()
        roots = tuple(map(Path, isolation.discover_sensitive_roots((repository,))))
        self.assertIn(common, roots)
        self.assertIn(alternate, roots)
        self.assertIn(other / "bench/phase0-agent-native/tasks", roots)
        object_id = git("rev-parse", "HEAD:" + hidden_relative)
        loose = objects / object_id[:2] / object_id[2:]
        self.assertTrue(loose.is_file())
        visible = workspace / "visible.txt"
        visible.write_text("SYNTHETIC VISIBLE")
        script = """
import json,os,subprocess,sys
from pathlib import Path
def read(path):
    try: return bool(Path(path).read_bytes())
    except OSError: return False
result={
    "mainHidden":read(sys.argv[1]),"otherHidden":read(sys.argv[2]),
    "object":read(sys.argv[5]),"alternate":read(sys.argv[6]),
    "public":read(sys.argv[7]),"visible":read(sys.argv[8]),
}
for kind in ("hardlink","symlink"):
    alias=Path(sys.argv[9]+"-"+kind)
    try:
        os.link(sys.argv[2],alias) if kind=="hardlink" else alias.symlink_to(sys.argv[2])
        result[kind]=read(alias)
    except OSError: result[kind]=False
shown=subprocess.run(["git","--git-dir",sys.argv[3],"show","HEAD:"+sys.argv[4]],
                     capture_output=True,timeout=10)
result["gitShow"]=shown.returncode==0 and bool(shown.stdout)
print(json.dumps(result))
"""
        arguments = [
            str(hidden), str(other / hidden_relative), str(common), hidden_relative,
            str(loose), str(alternate_sentinel), str(other / public_relative), str(visible),
            str(workspace / "alias"),
        ]
        expected = {
            "mainHidden": False, "otherHidden": False, "object": False,
            "alternate": False, "public": True, "visible": True,
            "hardlink": False, "symlink": False, "gitShow": False,
        }
        policy = isolation.sandbox_policy(workspace, output, protected, 1, roots, network=False)
        model = subprocess.run(
            ["/usr/bin/sandbox-exec", "-p", policy, sys.executable, "-c", script, *arguments],
            cwd=workspace, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, model.returncode, model.stdout + model.stderr)
        self.assertEqual(expected, json.loads(model.stdout))

        git("gc", "--prune=now")
        pack = next((objects / "pack").glob("*.pack"))
        arguments[4] = str(pack)
        generated = isolation.execute_generated(
            workspace, output, protected, roots,
            [sys.executable, "-c", script, *arguments], cwd=workspace,
            environment=os.environ.copy(), timeout=30)
        self.assertEqual(0, generated.returncode, generated.stdout + generated.stderr)
        self.assertEqual(expected, json.loads(generated.stdout))

    @unittest.skipUnless(sys.platform == "darwin", "actual macOS kernel policy")
    def test_kernel_blocks_other_network_state_access_and_outside_signals(self):
        spec = importlib.util.spec_from_file_location("kernel_test", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected = (self.root / name for name in ("work", "output", "protected"))
        for path in (work, output, protected):
            path.mkdir()
        hidden = self.root / "hidden"
        hidden.mkdir()
        hidden_test = hidden / "HeldOutTests.cs"
        seeded = hidden / "seeded.calr"
        hidden_test.write_text("hidden")
        seeded.write_text("seeded")
        listener = socket.socket()
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        self.addCleanup(listener.close)
        port = listener.getsockname()[1]
        result = isolation.kernel_probe(
            isolation.sandbox_policy(work, output, protected, port, (hidden,)),
            work, output, protected, (hidden_test, seeded), port, os.getpid())
        self.assertTrue(result["kernelProbe"]["gateway"])
        self.assertFalse(result["kernelProbe"]["outsideSignal"])
        self.assertFalse(result["kernelProbe"]["samePortIpv6"])
        self.assertFalse(result["kernelProbe"]["authoritativeRead"])
        self.assertFalse(result["kernelProbe"]["hiddenTestRead"])
        self.assertFalse(result["kernelProbe"]["seededSolutionRead"])
        self.assertTrue(result["kernelProbe"]["workspaceWrite"])
        self.assertFalse(result["modelInvoked"])

    @unittest.skipUnless(sys.platform == "darwin" and shutil.which("dotnet"),
                         "actual macOS generated-code policy")
    def test_generated_dotnet_code_cannot_read_or_write_authoritative_or_hidden_files(self):
        spec = importlib.util.spec_from_file_location("generated_policy_test",
                                                     BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected, hidden, build = (
            self.root / name for name in ("work", "output", "protected", "hidden", "build"))
        for path in (work, output, protected, hidden, build):
            path.mkdir()
        surfaces = []
        for relative in ("heldout/HeldOut.csproj", "policy-before.json",
                         "source-inspection.json", ".ho_final.txt"):
            path = output / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("UNCHANGED")
            surfaces.append(path)
        hidden_test = hidden / "HeldOutTests.cs"
        seeded = hidden / "seeded-solution.calr"
        hidden_test.write_text("HIDDEN")
        seeded.write_text("SEEDED")
        project = build / "Probe.csproj"
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        (build / "Program.cs").write_text("""
using System.Text.Json;
static bool Read(string path) { try { File.ReadAllText(path); return true; } catch { return false; } }
static bool Write(string path) { try { File.WriteAllText(path, "CHANGED"); return true; } catch { return false; } }
Console.WriteLine(JsonSerializer.Serialize(new {
    workspaceWrite = Write(args[0]),
    authoritativeReads = args.Skip(1).Take(4).Select(Read).ToArray(),
    authoritativeWrites = args.Skip(1).Take(4).Select(Write).ToArray(),
    hiddenReads = args.Skip(5).Take(2).Select(Read).ToArray()
}));
""")
        compiled = subprocess.run(["dotnet", "build", str(project), "--nologo", "-v", "q"],
                                  capture_output=True, text=True, timeout=120)
        self.assertEqual(0, compiled.returncode, compiled.stdout + compiled.stderr)
        command = ["dotnet", str(build / "bin/Debug/net10.0/Probe.dll"),
                   str(work / "allowed.txt"), *map(str, surfaces), str(hidden_test), str(seeded)]
        result = isolation.execute_generated(
            work, output, protected, (hidden,), command, cwd=work,
            environment=dict(os.environ, DOTNET_EnableDiagnostics="0"), timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        observed = json.loads(result.stdout)
        self.assertTrue(observed["workspaceWrite"])
        self.assertEqual([False] * 4, observed["authoritativeReads"])
        self.assertEqual([False] * 4, observed["authoritativeWrites"])
        self.assertEqual([False, False], observed["hiddenReads"])
        self.assertTrue(all(path.read_text() == "UNCHANGED" for path in surfaces))

    @unittest.skipUnless(sys.platform == "darwin", "actual narrow readable-child policy")
    def test_readable_child_does_not_expose_execution_root_sibling(self):
        spec = importlib.util.spec_from_file_location(
            "generated_readable_child", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        execution, sandbox, output, protected = (
            self.root / name for name in ("execution", "execution/sandbox", "output", "protected"))
        for path in (sandbox, output, protected):
            path.mkdir(parents=True)
        own = sandbox / "own.txt"
        sibling = execution / "unrelated-secret.txt"
        own.write_text("OWN")
        sibling.write_text("SECRET")
        script = (
            "import json,sys;from pathlib import Path\n"
            "def read(path):\n"
            " try:return Path(path).read_text()\n"
            " except OSError:return None\n"
            "print(json.dumps([read(sys.argv[1]),read(sys.argv[2])]))\n")
        result = isolation.execute_generated(
            sandbox, output, protected, (execution,),
            [sys.executable, "-c", script, str(own), str(sibling)],
            cwd=sandbox, environment=os.environ.copy(), timeout=30, readable=(sandbox,))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(["OWN", None], json.loads(result.stdout))

    @unittest.skipUnless(sys.platform == "darwin", "actual generated process-group timeout")
    def test_generated_timeout_terminates_nested_sandbox_descendants(self):
        spec = importlib.util.spec_from_file_location("generated_timeout",
                                                     BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected, hidden = (
            self.root / name for name in ("work", "output", "protected", "hidden"))
        for path in (work, output, protected, hidden):
            path.mkdir()
        child_file = work / "child.pid"
        with self.assertRaises(subprocess.TimeoutExpired):
            isolation.execute_generated(
                work, output, protected, (hidden,),
                ["/bin/bash", "-c", "sleep 60 & echo $! > child.pid; wait"],
                cwd=work, environment=os.environ.copy(), timeout=0.5)
        child = int(child_file.read_text())
        for _ in range(50):
            if subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode != 0:
                break
            time.sleep(0.02)
        self.assertNotEqual(
            0, subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode)

    @unittest.skipUnless(sys.platform == "darwin", "actual generated process-group sealing")
    def test_generated_normal_exit_terminates_detached_stdio_descendant(self):
        spec = importlib.util.spec_from_file_location(
            "generated_normal_exit", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected, hidden = (
            self.root / name for name in ("work", "output", "protected", "hidden"))
        for path in (work, output, protected, hidden):
            path.mkdir()
        result = isolation.execute_generated(
            work, output, protected, (hidden,),
            ["/bin/bash", "-c",
             "sleep 60 </dev/null >/dev/null 2>&1 & echo $! > child.pid"],
            cwd=work, environment=os.environ.copy(), timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        child = int((work / "child.pid").read_text())
        for _ in range(50):
            if subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode != 0:
                break
            time.sleep(0.02)
        self.assertNotEqual(
            0, subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode)

    @unittest.skipUnless(sys.platform == "darwin", "actual run-pair process-group sealing")
    def test_run_pair_gateway_group_seals_after_leader_exit(self):
        runner = (BENCH / "run-pair.sh").read_text()
        start = runner.index("gateway_group_live() {")
        run_agent = runner.index("run_agent() {", start)
        script = runner[start:run_agent] + r"""
set -m
/bin/bash -c 'sleep 60 </dev/null >/dev/null 2>&1 & echo $! > child.pid' &
leader=$!
wait "$leader"
terminate_gateway_group "$leader"
child=$(cat child.pid)
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    /bin/ps -p "$child" -o stat= 2>/dev/null | grep -qv '^[[:space:]]*Z' || exit 0
    sleep 0.1
done
exit 9
"""
        result = subprocess.run(
            ["/bin/bash", "--noprofile", "--norc", "-c", script],
            cwd=self.root, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    @unittest.skipUnless(sys.platform == "darwin", "actual nested sandbox process group")
    def test_runner_timeout_can_terminate_nested_sandbox_descendants(self):
        spec = importlib.util.spec_from_file_location("termination_test", BENCH / "ppw-gateway-client.py")
        isolation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(isolation)
        work, output, protected, hidden = (
            self.root / name for name in ("work", "output", "protected", "hidden"))
        for path in (work, output, protected, hidden):
            path.mkdir()
        policy = isolation.sandbox_policy(work, output, protected, 1, (hidden,), network=False)
        process = subprocess.Popen(
            ["/usr/bin/sandbox-exec", "-p", policy, "/bin/bash", "-c",
             "sleep 60 & child=$!; echo $child; wait $child"],
            cwd=work, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            start_new_session=True)
        child = int(process.stdout.readline().strip())
        os.killpg(process.pid, signal.SIGTERM)
        process.wait(timeout=10)
        process.stdout.close()
        process.stderr.close()
        for _ in range(50):
            if subprocess.run(["ps", "-p", str(child)], capture_output=True).returncode != 0:
                break
            time.sleep(0.02)
        self.assertNotEqual(0, subprocess.run(
            ["ps", "-p", str(child)], capture_output=True).returncode)


if __name__ == "__main__":
    unittest.main()
