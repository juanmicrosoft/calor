"""Synthetic financial controls only: no real authorization, provider call or epoch."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid

from ppw_redesign_epoch import BENCH, instrument, save

spending = instrument.helper("ppw-spending.py")


class SpendingFixture(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".spending-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)

    def admission(self, count=3, maximum=1_000_000, ceiling=3_000_000):
        return {
            "epochId": "SYNTHETIC-pilot", "stage": "pilot", "ceilingUnits": ceiling,
            "authorizationSha256": "a" * 64, "protocolSha256": "b" * 64, "planSha256": "c" * 64,
            "ledgerPath": str(self.root / "ledger.sqlite3"), "costBasis": "synthetic-test-only",
            "harnessArtifacts": spending.artifact_manifest(),
            "clientLimitUnits": 100_000, "maximumUnits": maximum,
            "slots": [{"id": "SYNTHETIC/calor-permissive/%d" % run, "task": "SYNTHETIC",
                       "arm": "calor-permissive", "run": run} for run in range(1, count + 1)],
        }

    def ledger(self, **kwargs):
        admission = self.admission(**kwargs)
        ledger = spending.Ledger(admission["ledgerPath"])
        ledger.initialize(admission)
        return ledger, ledger.start()

    def claim(self, ledger, owner, run=1):
        ticket = ledger.reserve(owner, "SYNTHETIC/calor-permissive/%d" % run)
        self.assertEqual("0.1", ledger.claim(ticket, "SYNTHETIC", "calor-permissive", run))
        return ticket


class SpendingTests(SpendingFixture):
    def test_exact_money_and_conservative_observation_rounding(self):
        self.assertEqual(250_000_000, spending.units(250))
        self.assertEqual(100_000, spending.units("0.10"))
        self.assertEqual(1, spending.units("0.0000001", observed=True))
        for value in (True, None, -1, float("nan"), float("inf"), "garbage", "0.0000001", "1e99"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                spending.units(value)

    def test_full_444_slot_readiness_is_not_reduced_to_fit_250(self):
        admission = self.admission(count=444, maximum=1_000_000, ceiling=250_000_000)
        ledger = spending.Ledger(admission["ledgerPath"])
        with self.assertRaisesRegex(ValueError, "full planned liabilities"):
            ledger.initialize(admission)
        self.assertFalse(ledger.path.exists())
        self.assertEqual(444, len(admission["slots"]))

    def test_estimated_cost_never_refunds_a_worst_case_reservation(self):
        ledger, owner = self.ledger()
        for run in (1, 2, 3):
            ticket = self.claim(ledger, owner, run)
            self.assertIsNone(ledger.settle(owner, ticket, {"total_cost_usd": 0}, 0))
        snapshot = ledger.snapshot()
        self.assertEqual("3", snapshot["reservedExposureUsd"])
        self.assertFalse(snapshot["reservationsRefunded"])
        self.assertTrue(snapshot["reportedCostsAreEstimates"])
        self.assertIsNone(snapshot["verdict"])
        ledger.complete(owner)
        with self.assertRaisesRegex(ValueError, "already active/completed"):
            spending.Ledger(ledger.path).start()

    def test_budget_exhaustion_stops_remaining_slots_without_a_study_verdict(self):
        ledger, owner = self.ledger()
        ticket = self.claim(ledger, owner)
        reason = ledger.settle(owner, ticket,
                               {"subtype": "error_max_budget_usd", "total_cost_usd": 0.12}, 0)
        self.assertEqual("client-budget-exhausted", reason)
        snapshot = ledger.snapshot()
        self.assertEqual("incomplete", snapshot["state"])
        self.assertEqual("1", snapshot["reservedExposureUsd"])
        self.assertEqual(2, sum(a["state"] == "unattempted" for a in snapshot["attempts"]))
        self.assertIsNone(snapshot["verdict"])
        with self.assertRaises(ValueError):
            ledger.reserve(owner, "SYNTHETIC/calor-permissive/2")
        with self.assertRaises(ValueError):
            ledger.complete(owner)

    def test_missing_invalid_and_interrupted_costs_keep_the_entire_reservation(self):
        for report, code in ((None, 0), ({}, 1), ({"total_cost_usd": "NaN"}, 0),
                             ({"total_cost_usd": -1}, 0), ({"total_cost_usd": 0.1}, -9),
                             ({"total_cost_usd": 0.1}, 124), ({"total_cost_usd": 0.1}, 137)):
            with self.subTest(report=report, code=code):
                directory = self.root / uuid.uuid4().hex
                directory.mkdir()
                admission = self.admission()
                admission["ledgerPath"] = str(directory / "ledger.sqlite3")
                ledger = spending.Ledger(admission["ledgerPath"])
                ledger.initialize(admission)
                owner = ledger.start()
                ticket = self.claim(ledger, owner)
                self.assertEqual("unknown-cost-or-interruption", ledger.settle(owner, ticket, report, code))
                self.assertEqual("1", ledger.snapshot()["reservedExposureUsd"])
                with self.assertRaises(ValueError):
                    ledger.reserve(owner, "SYNTHETIC/calor-permissive/2")

    def test_failed_calls_are_accounted_and_no_replacement_attempt_is_bought(self):
        ledger, owner = self.ledger()
        ticket = self.claim(ledger, owner)
        self.assertIsNone(ledger.settle(owner, ticket, {"total_cost_usd": 0.1}, 1))
        self.assertEqual("failed", ledger.snapshot()["attempts"][0]["state"])
        self.assertEqual("1", ledger.snapshot()["reservedExposureUsd"])
        with self.assertRaisesRegex(ValueError, "duplicate or retry"):
            ledger.reserve(owner, ticket["slot"])

    def test_unclaimed_invocation_cannot_masquerade_as_controlled(self):
        ledger, owner = self.ledger()
        ticket = ledger.reserve(owner, "SYNTHETIC/calor-permissive/1")
        self.assertEqual("invocation-not-claimed",
                         ledger.settle(owner, ticket, {"total_cost_usd": 0.1}, 0))
        self.assertEqual("incomplete", ledger.snapshot()["state"])

    def test_contradicted_bound_is_preserved_not_clipped_to_the_ceiling(self):
        ledger, owner = self.ledger()
        ticket = self.claim(ledger, owner)
        self.assertEqual("upper-bound-contradicted",
                         ledger.settle(owner, ticket, {"total_cost_usd": 4}, 0))
        snapshot = ledger.snapshot()
        self.assertEqual("4", snapshot["reservedExposureUsd"])
        self.assertEqual("incomplete", snapshot["state"])
        with self.assertRaises(ValueError):
            ledger.reserve(owner, "SYNTHETIC/calor-permissive/2")

    def test_simultaneous_duplicate_reservations_and_claims_are_atomic(self):
        ledger, owner = self.ledger()
        def reserve(_):
            try:
                return spending.Ledger(ledger.path).reserve(owner, "SYNTHETIC/calor-permissive/1")
            except ValueError:
                return None
        with ThreadPoolExecutor(max_workers=2) as pool:
            tickets = [t for t in pool.map(reserve, (1, 2)) if t]
        self.assertEqual(1, len(tickets))
        def claim(_):
            try:
                return spending.Ledger(ledger.path).claim(tickets[0], "SYNTHETIC", "calor-permissive", 1)
            except ValueError:
                return None
        with ThreadPoolExecutor(max_workers=2) as pool:
            self.assertEqual(1, sum(value is not None for value in pool.map(claim, (1, 2))))
        self.assertEqual("1", ledger.snapshot()["reservedExposureUsd"])

    def test_different_output_roots_cannot_start_two_collectors_on_the_shared_scope(self):
        ledger, _ = self.ledger()
        second = spending.Ledger(ledger.path)
        second.initialize(self.admission())
        with self.assertRaisesRegex(ValueError, "duplicate collection"):
            second.start()
        altered = self.admission()
        altered["epochId"] = "different-epoch"
        with self.assertRaisesRegex(ValueError, "scope/pins/ceiling changed"):
            second.initialize(altered)

    def test_process_loss_retains_in_flight_liability_and_never_automatically_resumes(self):
        admission = self.admission()
        ledger = spending.Ledger(admission["ledgerPath"])
        ledger.initialize(admission)
        script = (
            "import importlib.util,os,sys;"
            "s=importlib.util.spec_from_file_location('spend',sys.argv[1]);"
            "m=importlib.util.module_from_spec(s);s.loader.exec_module(m);"
            "l=m.Ledger(sys.argv[2]);o=l.start();t=l.reserve(o,'SYNTHETIC/calor-permissive/1');"
            "l.claim(t,'SYNTHETIC','calor-permissive',1);os._exit(17)"
        )
        result = subprocess.run([sys.executable, "-c", script, str(BENCH / "ppw-spending.py"),
                                 str(ledger.path)], env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"))
        self.assertEqual(17, result.returncode)
        snapshot = ledger.snapshot()
        self.assertEqual("in-flight", snapshot["attempts"][0]["state"])
        self.assertEqual("1", snapshot["reservedExposureUsd"])
        with self.assertRaises(ValueError):
            ledger.start()

    def test_ticket_scope_and_single_use_are_checked_before_returning_client_limit(self):
        ledger, owner = self.ledger()
        ticket = ledger.reserve(owner, "SYNTHETIC/calor-permissive/1")
        for task, arm, run in (("foreign", "calor-permissive", 1),
                               ("SYNTHETIC", "calor-strict", 1), ("SYNTHETIC", "calor-permissive", 2)):
            with self.assertRaisesRegex(ValueError, "another task/arm/run"):
                ledger.claim(ticket, task, arm, run)
        self.assertEqual("0.1", ledger.claim(ticket, "SYNTHETIC", "calor-permissive", 1))
        with self.assertRaisesRegex(ValueError, "consumed"):
            ledger.claim(ticket, "SYNTHETIC", "calor-permissive", 1)

    def test_harness_drift_after_reservation_refuses_before_ticket_claim(self):
        ledger, owner = self.ledger()
        ticket = ledger.reserve(owner, "SYNTHETIC/calor-permissive/1")
        with patch.object(spending, "artifact_manifest", return_value={"changed": "f" * 64}):
            with self.assertRaisesRegex(ValueError, "source changed before invocation"):
                ledger.claim(ticket, "SYNTHETIC", "calor-permissive", 1)
        self.assertEqual("reserved", ledger.snapshot()["attempts"][0]["state"])
        self.assertEqual("1", ledger.snapshot()["reservedExposureUsd"])


class SpendingAdmissionTests(SpendingFixture):
    def fixture(self):
        registration = {"compilerCommit": "a" * 40, "tasks": ["SYNTHETIC"],
                        "artifacts": {"SYNTHETIC/pair.json": "b" * 64}}
        selected = {"runsPerArm": 2, "modelPin": "SYNTHETIC", "agentVersion": "SYNTHETIC",
                    "stageRegistration": {"path": "method", "sha256": "c" * 64},
                    "modelRegistration": {"path": "model", "sha256": "d" * 64},
                    "spendAuthorization": {"path": "authorization", "sha256": "e" * 64}}
        amendment = self.root / "synthetic-amendment.json"
        save(amendment, {"schemaVersion": 1, "kind": "pp-w-prospective-spending-instrument-amendment",
                         "stage": "pilot", "replacementHarnessArtifacts": spending.artifact_manifest()})
        selected["instrumentAmendment"] = {"path": amendment.name, "sha256": spending.digest(amendment)}
        authorization = {"spendingCeilingUsd": 250,
                         "spendingLedgerPath": str(self.root / "never-created.sqlite3")}
        plan = {
            "schemaVersion": 1, "kind": "pp-w-pilot-spending-plan",
            "epochId": "SYNTHETIC-pilot", "stage": "pilot",
            "authorizationSha256": selected["spendAuthorization"]["sha256"],
            "protocolSha256": spending.protocol_identity(registration, selected, "pilot", "SYNTHETIC-pilot"),
            "ceilingUsd": 250, "costBasis": "both-list-price-study-cost-and-actual-spend",
            "ledgerPath": str(self.root / "never-created.sqlite3"), "plannedInvocations": 4,
            "clientControl": {"kind": spending.CONTROL, "limitUsd": "0.50"},
        }
        return registration, selected, authorization, plan

    def check(self, values):
        registration, selected, authorization, plan = values
        path = self.root / "SYNTHETIC-spending-plan.json"
        save(path, plan)
        selected["spendingPlan"] = {"path": path.name, "sha256": spending.digest(path)}
        return spending.admit(registration, selected, authorization, self.root, "SYNTHETIC-pilot", "pilot")

    def test_real_cli_estimate_is_not_promoted_to_a_hard_cap_by_json_assertions(self):
        values = self.fixture()
        values[-1]["clientControl"].update(
            hardCap=True, verified=True, maximumLiabilityUsd=0.5, invoiceBoundUsd=0.5)
        with self.assertRaisesRegex(ValueError, "no trustworthy upper bound"):
            self.check(values)
        self.assertFalse((self.root / "never-created.sqlite3").exists())

    def test_frozen_protocol_slot_count_cost_basis_ceiling_and_epoch_are_bound(self):
        for key, value in (("plannedInvocations", 3), ("plannedInvocations", True),
                           ("costBasis", "subscription-is-free"), ("ceilingUsd", 251),
                           ("epochId", "another"), ("stage", "confirmatory"),
                           ("protocolSha256", "f" * 64), ("authorizationSha256", "f" * 64)):
            values = self.fixture()
            values[-1][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check(values)

    def test_synthetic_verified_adapter_exercises_full_inventory_fit_without_real_provider_claim(self):
        values = self.fixture()
        with patch.object(spending, "verified_upper_bound", return_value=100_000_000):
            with self.assertRaisesRegex(ValueError, "full unchanged pilot"):
                self.check(values)
        with patch.object(spending, "verified_upper_bound", return_value=1_000_000):
            admission = self.check(values)
        self.assertEqual(4, len(admission["slots"]))
        self.assertEqual(250_000_000, admission["ceilingUnits"])
        self.assertFalse((self.root / "never-created.sqlite3").exists())

    def test_alternate_plan_cannot_reset_the_authorizations_shared_ledger(self):
        values = self.fixture()
        values[-1]["ledgerPath"] = str(self.root / "another-ledger.sqlite3")
        with patch.object(spending, "verified_upper_bound", return_value=1_000_000):
            with self.assertRaisesRegex(ValueError, "authorization must bind the same shared ledger"):
                self.check(values)

    def test_changed_instrument_manifest_refuses_even_with_a_synthetic_bounded_adapter(self):
        values = self.fixture()
        with patch.object(spending, "verified_upper_bound", return_value=1_000_000), \
                patch.object(spending, "artifact_manifest", return_value={"different": "f" * 64}):
            with self.assertRaisesRegex(ValueError, "instrument source differs"):
                self.check(values)

    def test_real_driver_refuses_unknown_bounds_before_any_product_agent_or_output(self):
        values = self.fixture()
        registration, selected, authorization, _ = values
        try:
            self.check(values)
        except ValueError:
            pass
        path = self.root / "registration.json"
        save(path, registration)
        with patch.object(instrument, "validate_collection_environment"), \
                patch.object(instrument, "validate_registration", return_value=selected), \
                patch.object(instrument, "validate_tasks"), \
                patch.object(instrument, "validate_collection_authorization", return_value=authorization), \
                patch.object(instrument, "command", side_effect=AssertionError("no subprocess allowed")):
            with self.assertRaisesRegex(ValueError, "no trustworthy upper bound"):
                instrument.run_epoch(path, self.root / "unused-tasks", self.root / "unused-product",
                                     self.root / "no-output", "SYNTHETIC-pilot", "pilot", True)
        self.assertFalse((self.root / "no-output").exists())


class SpendingShellTests(SpendingFixture):
    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "existing shell capture tools required")
    def test_unsupported_client_wrong_scope_and_replayed_ticket_never_invoke_the_client(self):
        from test_ppw_instrument import InstrumentTests
        for case, reason in (("unsupported", "lacks --max-budget-usd"),
                             ("wrong-arm", "another task/arm/run"), ("replay", "consumed")):
            with self.subTest(case=case):
                fixture = InstrumentTests()
                fixture.setUp()
                self.addCleanup(fixture.doCleanups)
                admission = self.admission(count=1)
                admission["ledgerPath"] = str(self.root / (case + ".sqlite3"))
                admission["slots"] = [{"id": "SYNTHETIC-task/calor-strict/1", "task": "SYNTHETIC-task",
                                       "arm": "calor-strict", "run": 1}]
                ledger = spending.Ledger(admission["ledgerPath"])
                ledger.initialize(admission)
                ticket = ledger.reserve(ledger.start(), admission["slots"][0]["id"])
                if case == "replay":
                    ledger.claim(ticket, "SYNTHETIC-task", "calor-strict", 1)
                path = self.root / (case + ".json")
                save(path, ticket)
                fixture.fake_capture("success", spending_ticket=path,
                                     arm="A" if case == "wrong-arm" else "B",
                                     budget_support=case != "unsupported", expect_refusal=reason)

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "existing shell capture tools required")
    def test_supported_budget_flag_is_passed_to_the_actual_scripted_client_invocation(self):
        from test_ppw_instrument import InstrumentTests
        fixture = InstrumentTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        admission = self.admission(count=1)
        admission["slots"] = [{"id": "SYNTHETIC-task/calor-strict/1", "task": "SYNTHETIC-task",
                               "arm": "calor-strict", "run": 1}]
        ledger = spending.Ledger(admission["ledgerPath"])
        ledger.initialize(admission)
        owner = ledger.start()
        ticket = ledger.reserve(owner, admission["slots"][0]["id"])
        path = self.root / "ticket.json"
        save(path, ticket)
        captured = fixture.fake_capture("success", spending_ticket=path)
        arguments = json.loads((fixture.root / "synthetic-agent-calls.arguments.json").read_text())
        self.assertEqual(1, arguments.count("--max-budget-usd"))
        self.assertEqual("0.1", arguments[arguments.index("--max-budget-usd") + 1])
        self.assertIn("--print", arguments)
        control = json.loads((captured / "client-spending-control.json").read_text())
        self.assertFalse(control["invoiceHardCap"])
        self.assertEqual("0.1", control["limitUsd"])
        self.assertEqual(0, json.loads((captured / "client-invocation.json").read_text())["exitCode"])
        self.assertEqual("in-flight", ledger.snapshot()["attempts"][0]["state"])
        report = json.loads((captured / "agent.json").read_text())
        self.assertIsNone(ledger.settle(owner, ticket, report, 0))
        self.assertEqual("1", ledger.snapshot()["reservedExposureUsd"])


class SpendingCollectorTests(SpendingFixture):
    def collect(self, report, client_exit=0):
        epoch_id = "SYNTHETIC-budget-" + uuid.uuid4().hex
        directory = self.root / epoch_id
        directory.mkdir()
        tasks = directory / "tasks"
        (tasks / "SYNTHETIC").mkdir(parents=True)
        (tasks / "SYNTHETIC/fixture.txt").write_text("Synthetic collector fixture, not task data.")
        registration = {"compilerCommit": "a" * 40, "tasks": ["SYNTHETIC"]}
        path = directory / "registration.json"
        save(path, registration)
        authorization_path = directory / "authorization.json"
        authorization_path.write_text('{ "synthetic": true }\n')
        save(directory / "plan.json", {"synthetic": True})
        selected = {"modelPin": "SYNTHETIC", "agentVersion": "SYNTHETIC", "runsPerArm": 1,
                    "spendingPlan": {"path": "plan.json"},
                    "spendAuthorization": {"path": "authorization.json"}}
        admission = self.admission(count=2)
        admission["epochId"] = epoch_id
        admission["ledgerPath"] = str(directory / "ledger.sqlite3")
        admission["slots"] = [
            {"id": "SYNTHETIC/%s/1" % arm, "task": "SYNTHETIC", "arm": arm, "run": 1}
            for arm in ("calor-permissive", "calor-strict")]
        product = {"repoRoot": str(directory), "calorDll": str(directory / "synthetic.dll"),
                   "commit": "a" * 40}
        invocations = []

        def command(argv, **kwargs):
            if argv[0] == "git":
                return "f" * 40 if "rev-parse" in argv else ""
            if argv == ["claude", "--version"]:
                return "SYNTHETIC"
            arm = argv[argv.index("--arm-label") + 1]
            if "--canary-only" in argv:
                return json.dumps({"compilerHash": "d" * 64, "armCanary":
                                   "permissive-ok" if arm == "calor-permissive" else "strict-ok"})
            ticket = instrument.load(argv[argv.index("--ppw-spend-ticket") + 1])
            ledger = spending.Ledger(ticket["ledgerPath"])
            self.assertEqual("reserved", next(a["state"] for a in ledger.snapshot()["attempts"]
                                              if a["slot"] == ticket["slot"]))
            ledger.claim(ticket, "SYNTHETIC", arm, 1)
            invocations.append(ticket["slot"])
            output = Path(argv[argv.index("--out") + 1]) / "SYNTHETIC" / arm / "run-1"
            output.mkdir(parents=True)
            if report is not None:
                save(output / "agent.json", report)
            save(output / "client-invocation.json", {"exitCode": client_exit})
            save(output / "result.json", {"synthetic": True})
            return ""

        original_helper = instrument.helper
        with patch.object(instrument, "validate_collection_environment"), \
                patch.object(instrument, "validate_registration", return_value=selected), \
                patch.object(instrument, "validate_tasks"), \
                patch.object(instrument, "validate_pins"), \
                patch.object(instrument, "validate_collection_authorization", return_value={"synthetic": True}), \
                patch.object(instrument, "helper", side_effect=lambda name:
                             spending if name == "ppw-spending.py" else original_helper(name)), \
                patch.object(spending, "admit", return_value=admission), \
                patch.object(instrument, "product", side_effect=lambda *_: dict(product)), \
                patch.object(instrument, "command", side_effect=command), \
                patch.object(instrument, "record_stage") as record_stage, \
                patch.dict(os.environ, CLAUDE_MODEL="SYNTHETIC"):
            failure = None
            try:
                instrument.run_epoch(path, tasks, directory, directory / "output", epoch_id, "pilot", True)
            except ValueError as error:
                failure = str(error)
        return directory / "output" / epoch_id, invocations, failure, record_stage.call_count

    def test_budget_unknown_and_timeout_stops_preserve_outputs_without_adjudication(self):
        for report, code, reason in (
            ({"subtype": "error_max_budget_usd", "total_cost_usd": 0.12}, 0, "client-budget-exhausted"),
            (None, 0, "unknown-cost-or-interruption"),
            ({"total_cost_usd": 0.05}, 124, "unknown-cost-or-interruption"),
            ({"total_cost_usd": 0.2}, 0, "client-limit-exceeded"),
        ):
            with self.subTest(reason=reason):
                epoch, calls, error, analysis_calls = self.collect(report, code)
                self.assertIn(reason, error)
                self.assertEqual(1, len(calls))
                self.assertEqual(0, analysis_calls)
                self.assertTrue((epoch / "runs" / calls[0].replace("/1", "/run-1") / "result.json").is_file())
                self.assertEqual("collecting", instrument.load(epoch / "pins.json")["lifecycle"])
                outcome = instrument.load(epoch / "collection-outcome.json")
                self.assertFalse(outcome["complete"])
                self.assertIsNone(outcome["verdict"])
                self.assertEqual("incomplete", outcome["spending"]["state"])
                self.assertEqual("1", outcome["spending"]["reservedExposureUsd"])
                self.assertEqual(b'{ "synthetic": true }\n',
                                 (epoch / "spending-authorization.json").read_bytes())

    def test_all_original_synthetic_slots_complete_before_stage_record(self):
        epoch, calls, error, analysis_calls = self.collect({"total_cost_usd": 0.05})
        self.assertIsNone(error)
        self.assertEqual(2, len(calls))
        self.assertEqual(1, analysis_calls)
        self.assertEqual("collected", instrument.load(epoch / "pins.json")["lifecycle"])
        self.assertEqual("2", instrument.load(epoch / "spending-final.json")["reservedExposureUsd"])
        self.assertFalse((epoch / "collection-outcome.json").exists())


if __name__ == "__main__":
    unittest.main()
