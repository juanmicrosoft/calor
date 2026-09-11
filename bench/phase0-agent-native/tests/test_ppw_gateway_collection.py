"""SYNTHETIC collector/HTTP/ledger/analysis integration; no client or compiler execution."""
import copy
import hashlib
import http.client
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import Mock, patch
from urllib.parse import urlsplit
import uuid

from ppw_redesign_epoch import BENCH, build, instrument, save
import test_ppw_gateway as gateway_tests
from test_ppw_gateway import body, budget, expected_charge, gateway


@unittest.skipIf(os.name == "nt", "the isolated collector uses Unix permission bits")
class CollectionTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".gateway-collection-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.seed = build(self.root / "seed")
        self.inputs = self.root / "SYNTHETIC-inputs"
        self.inputs.mkdir()
        self.epochs = self.root / "SYNTHETIC-epochs"
        self.epoch_id = "synthetic-pilot"
        self.epoch = self.epochs / self.epoch_id
        self.spending = instrument.helper("ppw-spending.py")
        self.isolation = instrument.helper("ppw-gateway-client.py")
        self.inspection = instrument.helper("ppw-source-inspection.py")
        self.test_host = instrument.helper("ppw-test-host.py")
        self.registration = instrument.load(self.seed / "registration.json")
        self.selected = self.registration["stages"]["pilot"]
        self.selected.update(modelPin=budget.MODEL, agentVersion=self.isolation.CLIENT_VERSION)
        for name in ("spendAuthorization", "stageRegistration", "modelRegistration",
                     "instrumentAmendment", "spendingPlan"):
            path = self.inputs / ("SYNTHETIC-" + name + ".json")
            save(path, {"kind": "SYNTHETIC-TEST-DOUBLE-NOT-AN-AUTHORIZATION", "role": name})
            self.selected[name] = {"path": path.name, "sha256": instrument.digest(path)}
        self.product = copy.deepcopy(instrument.load(self.seed / "pins.json")["compiler"])
        del self.product["compilerHash"]
        self.product["repoRoot"] = str(self.root / "SYNTHETIC-product")
        self.compiler_root = self.product["repoRoot"]
        self.compiler_hash = "d" * 64
        self.product["calorDll"] = str(self.root / "SYNTHETIC-product/calor.dll")
        runtime = Path(self.product["repoRoot"]) / "src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll"
        runtime.parent.mkdir(parents=True)
        runtime.write_bytes(b"SYNTHETIC nonexecutable runtime fixture")
        inspector_manifest = self.root / "SYNTHETIC-source-inspector-runtime.json"
        inspector = {
            "kind": "SYNTHETIC-nonexecutable-inspector-runtime",
            "manifest": str(inspector_manifest),
            "files": {"ppw-source-inspector.dll": "9" * 64},
            "runtimeSha256": "8" * 64,
        }
        save(inspector_manifest, inspector)
        self.admission = {
            "mechanism": self.spending.GATEWAY, "epochId": self.epoch_id, "stage": "pilot",
            "ceilingUnits": 500_000_000, "authorizationSha256": "a" * 64,
            "protocolSha256": "b" * 64, "planSha256": "c" * 64,
            "ledgerPath": str(self.root / "SYNTHETIC-protected" / "ledger.sqlite3"),
            "costBasis": "SYNTHETIC-test-only",
            "harnessArtifacts": self.spending.artifact_manifest(self.spending.GATEWAY),
            "clientExecutable": str(self.root / "SYNTHETIC-client-not-executable"),
            "shellExecutable": str(self.root / "SYNTHETIC-shell-not-executable"),
            "shellSha256": "e" * 64, "priceSha256": budget.price_identity(),
            "runtimeSha256": instrument.digest(runtime),
            "testHost": {"kind": "SYNTHETIC-nonexecutable-test-runtime"},
            "sourceInspector": inspector,
            "executionRuntime": {"kind": "SYNTHETIC-local-runtime-double"},
            "forecastEvidence": {},
        }
        self.launched = []
        self.failure = None
        self.invalid_slots = {}
        self.discovery_complete = False
        self.registration_file = self.inputs / "registration.json"
        self.prepare_inventory(3, 74)

    def prepare_inventory(self, tasks, runs):
        root = self.inputs / "tasks"
        root.mkdir()
        original = self.seed / "tasks" / "SYNTHETIC-task"
        self.registration["tasks"] = ["SYNTHETIC-task-%d" % number for number in range(1, tasks + 1)]
        self.registration["sourceInspections"] = {
            task: copy.deepcopy(self.registration["sourceInspections"]["SYNTHETIC-task"])
            for task in self.registration["tasks"]
        }
        for certificate in self.registration["sourceInspections"].values():
            certificate.update(inspectorSha256="9" * 64, inspectorRuntimeSha256="8" * 64)
        for task in self.registration["tasks"]:
            target = root / task
            shutil.copytree(original, target)
            pair = instrument.load(target / "pair.json")
            pair["id"] = task
            save(target / "pair.json", pair)
        self.registration["artifacts"] = {
            str(path.relative_to(root)): instrument.digest(path)
            for path in root.rglob("*") if path.is_file()
        }
        registration_helper = instrument.helper("ppw-registration.py")
        self.registration["replacementPins"]["pairCounts"] = {
            "tasks": tasks, "blind": tasks, "warningVsError": 0, "legB": tasks,
        }
        self.registration["replacementPins"]["starterBlobs"] = [
            {"task": task, "arm": arm, "path": "%s/starter-%s/Source.calr" % (task, arm.lower()),
             "blobSha": registration_helper.blob_sha(root / task / ("starter-" + arm.lower()) / "Source.calr")}
            for task in self.registration["tasks"] for arm in ("A", "B")
        ]
        self.selected["runsPerArm"] = runs
        self.admission["slots"] = [
            {"id": "%s/%s/%d" % (task, arm["label"], run), "task": task, "arm": arm["label"], "run": run}
            for run in range(1, runs + 1) for task in self.registration["tasks"]
            for arm in instrument.ARMS.values()
        ]
        save(self.inputs / "registration.json", self.registration)

    def command(self, argv, **kwargs):
        if argv[0] == "git":
            if "status" in argv:
                return ""
            if argv[-2:] == ["rev-parse", "HEAD"]:
                return "f" * 40
        if argv == [self.admission["clientExecutable"], "--version"]:
            self.assertTrue(self.discovery_complete)
            return self.isolation.CLIENT_VERSION
        if argv[0] == str(BENCH / "run-pair.sh") and "--canary-only" in argv:
            arm = argv[argv.index("--arm-label") + 1]
            return json.dumps({"armCanary": "permissive-ok" if arm == "calor-permissive" else "strict-ok",
                               "compilerHash": self.compiler_hash})
        self.assertEqual([sys.executable, str(BENCH / "ppw-gateway-client.py")], argv[:2])
        self.assertIn("--ppw-gateway-client", argv)
        self.assertEqual(self.admission["clientExecutable"],
                         argv[argv.index("--ppw-gateway-client") + 1])
        context_path = Path(argv[argv.index("--context") + 1])
        workspace = Path(argv[argv.index("--workspace") + 1])
        authoritative = Path(argv[argv.index("--output") + 1])
        context = instrument.load(context_path)
        endpoint = urlsplit(context["baseUrl"])
        task = Path(argv[argv.index("--pair") + 1]).name
        arm = argv[argv.index("--arm-label") + 1]
        run = int(argv[argv.index("--run-offset") + 1]) + 1
        output = authoritative / "runs" / task / arm / ("run-%d" % run)
        slot = "%s/%s/%d" % (task, arm, run)
        self.launched.append(slot)
        strict_terminal = budget.strict_terminal_lifecycle({
            "harnessArtifacts": self.admission["harnessArtifacts"],
        })
        if strict_terminal:
            budget.write_attempt_start(output, authoritative, slot, 1)
        evidence = {
            "kind": self.isolation.ISOLATION, "kernelProbe": dict(self.isolation.PROBE_EXPECTATIONS),
            "modelInvoked": False, "clientSha256": self.isolation.CLIENT_SHA256,
            "policySha256": hashlib.sha256(self.isolation.sandbox_policy(
                workspace, authoritative, context["protectedRoot"], endpoint.port,
                context["hiddenRoots"]).encode()).hexdigest(),
            "workspaceRoot": str(workspace), "authoritativeRoot": str(authoritative),
        }
        if self.failure == "forged-isolation":
            evidence["kernelProbe"]["authoritativeRead"] = True
        # Only the OS/client boundary is a double. The real HTTP gateway and
        # request ledger execute unchanged against a deterministic local provider.
        self.isolation.write_isolation_evidence(context_path, evidence)
        invalid_mode = self.invalid_slots.get(slot)
        if self.failure == "forged-terminal":
            invalid_mode = "zero-requests"
        if self.failure != "no-requests" and invalid_mode != "zero-requests":
            client = http.client.HTTPConnection(endpoint.hostname, endpoint.port, timeout=10)
            client.request("POST", endpoint.path + "/v1/messages?beta=true", body(), {
                "Content-Type": "application/json", "anthropic-version": "2023-06-01",
            })
            response = client.getresponse()
            response.read()
            client.close()
            if response.status != 200:
                raise subprocess.CalledProcessError(1, argv)
        seed = self.seed_run(task, arm, run)
        shutil.copytree(seed, output, dirs_exist_ok=True)
        source_report = instrument.load(output / "source-inspection.json")
        source_report.update(
            inspectorSha256=self.admission["sourceInspector"]["files"][
                "ppw-source-inspector.dll"],
            inspectorRuntimeSha256=self.admission["sourceInspector"]["runtimeSha256"])
        save(output / "source-inspection.json", source_report)
        result = instrument.load(output / "result.json")
        result.update(pair=task, run=run, armRepoRoot=self.product["repoRoot"])
        if invalid_mode or self.failure == "untrusted-invalid":
            result.update(invalid=True, censored=True)
        save(output / "result.json", result)
        save(output / "client-invocation.json", {"exitCode": 124 if self.failure == "interrupted" else 0})
        if invalid_mode:
            (output / "transcript.jsonl").unlink(missing_ok=True)
            (output / "source-inspection.json").unlink(missing_ok=True)
            reason = next(value for value, code in budget.TERMINAL_REASON_CODES.items()
                          if code == "MISSING_TRANSCRIPT")
            (output / "invalid.txt").write_text(
                "2026-09-11T00:00:00Z attempt=0 agent_rc=0: " + reason + "\n",
                encoding="utf-8")
            budget.write_terminal_attempt(output, authoritative, slot, reason)
            if self.failure == "forged-terminal":
                terminal = instrument.load(output / "invalid-terminal.json")
                terminal["classification"] = "FORGED"
                save(output / "invalid-terminal.json", terminal)
        elif self.failure == "missing-source":
            (output / "source-inspection.json").unlink()
        return ""

    def seed_run(self, task, arm, run):
        return self.seed / "runs" / "SYNTHETIC-task" / arm / "run-1"

    def collect(self):
        factory, self.observed, _ = gateway_tests.TransportTests.provider(
            self, status=500 if self.failure == "unknown" else 200)
        constructor = gateway.Gateway
        serve_forever = gateway.Server.serve_forever
        helper = instrument.helper
        validate_pins = instrument.validate_pins
        modules = {
            "ppw-spending.py": self.spending, "ppw-budget-gateway.py": gateway,
            "ppw-gateway-client.py": self.isolation, "ppw-source-inspection.py": self.inspection,
            "ppw-test-host.py": self.test_host,
            "ppw-run-observer.py": Mock(RunObserver=lambda **values: Mock(
                model_hidden_roots=tuple(values["hidden_roots"]))),
        }
        repository_denials = [
            str(self.root / "SYNTHETIC-other-worktree/bench/phase0-agent-native/tasks"),
            str(self.root / "SYNTHETIC-common-git"),
        ]
        def discover(_):
            self.discovery_complete = True
            return repository_denials
        def synthetic_pins(pins, registration, stage, epoch_id):
            pins["dataKind"] = "synthetic"
            return validate_pins(pins, registration, stage, epoch_id)
        artifact_manifest = self.spending.artifact_manifest
        with patch.object(instrument, "REPO", self.root), \
                patch.object(instrument, "helper", side_effect=lambda name:
                             modules[name] if name in modules else helper(name)), \
                patch.object(instrument, "validate_collection_authorization",
                             return_value={"kind": "SYNTHETIC-not-an-approval"}), \
                patch.object(self.spending, "admit", return_value=self.admission), \
                patch.object(self.spending, "artifact_manifest", side_effect=lambda mechanism:
                             self.admission["harnessArtifacts"]
                             if getattr(self, "historical_manifest", False)
                             else artifact_manifest(mechanism)), \
                patch.object(instrument, "current_harness_artifacts", side_effect=lambda names:
                             self.admission["harnessArtifacts"]
                             if getattr(self, "historical_manifest", False)
                             else {name: instrument.digest(BENCH / name) for name in names}), \
                patch.object(self.inspection, "prepare", return_value=self.admission["sourceInspector"]), \
                patch.object(self.test_host, "validate_runtime"), \
                patch.object(self.isolation, "validate_runtime"), \
                patch.object(self.isolation, "discover_sensitive_roots", side_effect=discover), \
                patch.object(instrument, "product", side_effect=lambda *_: dict(self.product)), \
                patch.object(instrument, "command", side_effect=self.command), \
                patch.object(instrument, "validate_pins", side_effect=synthetic_pins), \
                patch.object(gateway, "Gateway", side_effect=lambda ledger, owner, slot, observer=None:
                             constructor(ledger, owner, slot, factory, observer)), \
                patch.object(gateway.Server, "serve_forever", lambda server:
                             serve_forever(server, poll_interval=0.001)), \
                patch.dict(os.environ, {"CLAUDE_MODEL": budget.MODEL}):
            return instrument.run_epoch(
                self.registration_file, self.inputs / "tasks",
                self.compiler_root, self.epochs, self.epoch_id, "pilot", True)

    def prepare_recovered_scope(self):
        recovery = instrument.helper("ppw-gateway-recovery.py")
        full_slots = [slot["id"] for slot in self.admission["slots"]]
        old = {
            "stage": "pilot", "epochId": "SYNTHETIC-failed",
            "priceSha256": budget.price_identity(), "authorizationSha256": "a" * 64,
            "protocolSha256": "1" * 64, "planSha256": "2" * 64,
            "harnessArtifacts": self.admission["harnessArtifacts"],
            "plannedSlots": full_slots,
        }
        target = {
            "stage": "pilot", "epochId": self.epoch_id,
            "priceSha256": budget.price_identity(),
            "authorizationSha256": self.admission["authorizationSha256"],
            "protocolSha256": self.admission["protocolSha256"],
            "planSha256": self.admission["planSha256"],
            "harnessArtifacts": self.admission["harnessArtifacts"],
            "plannedSlots": full_slots,
        }
        protected = Path(self.admission["ledgerPath"]).parent
        protected.mkdir()
        ledger = budget.RequestLedger(self.admission["ledgerPath"])
        ledger.initialize(old, recovery.PILOT_CEILING_MICRO_USD)
        owner = ledger.start()
        initial = ledger.snapshot()
        ledger.stop(owner, recovery.FAILED_STATE)
        failed = ledger.snapshot()
        failed_archive = self.root / "SYNTHETIC-failed-archive"
        failed_archive.mkdir()
        save(failed_archive / "registration.json", {
            "stages": {"pilot": {
                "spendAuthorization": {"path": "authorization", "sha256": old["authorizationSha256"]},
                "spendingPlan": {"path": "plan", "sha256": old["planSha256"]},
            }},
        })
        save(failed_archive / "pins.json", {
            "epochId": old["epochId"], "stage": "pilot", "lifecycle": "collecting",
            "mode": "live", "harnessArtifacts": old["harnessArtifacts"],
            "registrationSha256": instrument.digest(failed_archive / "registration.json"),
            "suite": self.registration["tasks"], "runsPerArm": self.selected["runsPerArm"],
            "modelPin": budget.MODEL, "agentVersion": self.isolation.CLIENT_VERSION,
            "compiler": self.product, "arms": instrument.ARMS,
        })
        save(failed_archive / "spending-initial.json", initial)
        save(failed_archive / "collection-outcome.json", {
            "kind": "pp-w-incomplete-collection", "epochId": old["epochId"],
            "stage": "pilot", "complete": False, "verdict": None,
            "reason": "SYNTHETIC pre-forward startup failure", "spending": failed,
        })
        first = self.admission["slots"][0]
        failed_run = failed_archive / "runs" / first["task"] / first["arm"] / (
            "run-%d" % first["run"])
        failed_run.mkdir(parents=True)
        save(failed_run / "client-invocation.json", {"exitCode": 1})
        (failed_run / "result.json").write_bytes(
            b"SYNTHETIC OPAQUE INVALID PLACEHOLDER; NOT AN OUTCOME")
        inventory = recovery.archive_inventory(failed_archive)
        ledger_sha = instrument.digest(self.admission["ledgerPath"])
        recovery_authority = {
            "kind": "SYNTHETIC-test-owned-recovery-authorization",
            "oldEpochId": old["epochId"],
            "oldBinding": old,
            "targetEpochId": target["epochId"],
            "targetBinding": target,
            "failedLedgerSha256": ledger_sha,
            "failedArchiveInventorySha256": inventory["sha256"],
            "preservedAttemptedSlots": [full_slots[0]],
            "backupName": "SYNTHETIC-failed.sqlite3",
        }
        recovery_authority_path = self.inputs / "SYNTHETIC-recoveryAuthorization.json"
        save(recovery_authority_path, recovery_authority)
        recovery_sha = instrument.digest(recovery_authority_path)
        proof = recovery.prove_zero_request_recovery(
            self.admission["ledgerPath"], failed_archive, protected,
            protected / "SYNTHETIC-failed.sqlite3",
            expected_old_binding=old, target_binding=target,
            expected_ledger_sha256=ledger_sha,
            expected_archive_inventory_sha256=inventory["sha256"],
            recovery_registration_sha256=recovery_sha)
        recovery.apply_zero_request_recovery(
            self.admission["ledgerPath"], failed_archive, protected,
            protected / "SYNTHETIC-failed.sqlite3",
            expected_old_binding=old, target_binding=target,
            expected_ledger_sha256=ledger_sha,
            expected_archive_inventory_sha256=inventory["sha256"],
            recovery_registration_sha256=recovery_sha,
            confirmed_proof_sha256=proof["proofSha256"])
        documents = {
            "recoveryEvidence": {"kind": "SYNTHETIC recovery evidence"},
            "recoveryAuthorization": recovery_authority,
            "inspectionProof": proof,
            "failedArchiveInventory": inventory,
            "failedOperationalSnapshot": failed,
        }
        for name, value in documents.items():
            path = self.inputs / ("SYNTHETIC-" + name + ".json")
            save(path, value)
            self.selected[name] = {"path": path.name, "sha256": instrument.digest(path)}
        save(self.registration_file, self.registration)
        self.admission.update(
            ceilingUnits=recovery.PILOT_CEILING_MICRO_USD,
            plannedSlots=list(self.admission["slots"]),
            slots=list(self.admission["slots"][1:]),
            recovery={
                "oldBinding": old, "targetBinding": target,
                "failedLedgerSha256": ledger_sha,
                "failedArchiveInventorySha256": inventory["sha256"],
                "recoveryRegistrationSha256": recovery_sha,
                "preservedAttemptedSlots": [full_slots[0]],
                "failedArchive": str(failed_archive),
                "backupName": "SYNTHETIC-failed.sqlite3",
            })
        return full_slots

    def test_real_gateway_collector_hands_complete_synthetic_data_to_stage_analysis(self):
        self.collect()
        spending = instrument.load(self.epoch / "spending-final.json")
        pins = instrument.load(self.epoch / "pins.json")
        report = instrument.load(self.epoch / "ppw-stage-ledger.json")
        self.assertEqual([slot["id"] for slot in self.admission["slots"]], self.launched)
        self.assertEqual(444, len(self.observed))
        self.assertEqual(444 * expected_charge(), spending["exposureMicroUsd"])
        self.assertEqual("complete", spending["state"])
        self.assertEqual("collected", pins["lifecycle"])
        self.assertEqual("synthetic", pins["dataKind"])
        self.assertFalse(report["empirical"])
        self.assertIsNone(report["verdict"])
        self.assertEqual("pilot", report["stage"])
        self.assertEqual(6, len(report["perCell"]))
        self.assertEqual([74] * 6, [cell["validRuns"] for cell in report["perCell"]])
        completed = [budget.decode(event["detail"]) for event in spending["events"]
                     if event["kind"] == "slot-complete"]
        self.assertEqual(self.launched, [value["slot"] for value in completed])
        for value in completed:
            task, arm, run = value["slot"].split("/")
            path = self.epoch / "runs" / task / arm / ("run-" + run) / "gateway-isolation.json"
            self.assertEqual(value["isolation"], instrument.load(path))
        self.assertEqual([], list(Path(self.admission["ledgerPath"]).parent.glob("invocation-*")))

    def test_budget_failure_never_advances_collection_or_calls_stage_analysis(self):
        self.admission["ceilingUnits"] = 1
        self.failure = "budget"
        with patch.object(instrument, "record_stage") as analyze:
            with self.assertRaises(subprocess.CalledProcessError):
                self.collect()
        analyze.assert_not_called()
        outcome = instrument.load(self.epoch / "collection-outcome.json")
        self.assertFalse(outcome["complete"])
        self.assertIsNone(outcome["verdict"])
        self.assertEqual("INCOMPLETE_BUDGET", outcome["spending"]["state"])
        self.assertEqual([], self.observed)
        self.assertEqual("collecting", instrument.load(self.epoch / "pins.json")["lifecycle"])

    def test_missing_or_interrupted_activity_is_not_a_completed_slot(self):
        for failure in ("no-requests", "interrupted", "unknown"):
            with self.subTest(failure=failure):
                other = CollectionTests()
                other.setUp()
                self.addCleanup(other.doCleanups)
                other.failure = failure
                with patch.object(instrument, "record_stage") as analyze:
                    with self.assertRaises((ValueError, subprocess.CalledProcessError)):
                        other.collect()
                analyze.assert_not_called()
                outcome = instrument.load(other.epoch / "collection-outcome.json")
                self.assertFalse(outcome["complete"])
                self.assertIsNone(outcome["verdict"])
                expected = "INCOMPLETE_UNKNOWN_CHARGE" if failure == "unknown" else "INCOMPLETE_INTERRUPTED"
                self.assertEqual(expected, outcome["spending"]["state"])
                if failure == "unknown":
                    self.assertEqual(budget.admit_request(body())["maximumMicroUsd"],
                                     outcome["spending"]["exposureMicroUsd"])

    def test_policy_stop_diagnostic_is_reported_before_slot_completion_secondary_error(self):
        ledger = Mock()
        ledger.snapshot.return_value = {
            "state": "INCOMPLETE_POLICY",
            "events": [{
                "kind": "stopped",
                "detail": budget.canonical({
                    "reason": "INCOMPLETE_POLICY",
                    "diagnostic": "WIRE_UNKNOWN_PROVIDER_HEADER",
                }),
            }],
        }
        with self.assertRaisesRegex(
                ValueError, r"INCOMPLETE_POLICY \(WIRE_UNKNOWN_PROVIDER_HEADER\)"):
            instrument.require_gateway_collecting(ledger, budget)
            ledger.complete_slot("owner", "slot", {}, 1)
        ledger.complete_slot.assert_not_called()

    def test_untrusted_invalid_missing_source_forged_terminal_and_isolation_halt(self):
        for failure in (
                "untrusted-invalid", "missing-source", "forged-terminal", "forged-isolation"):
            with self.subTest(failure=failure):
                other = CollectionTests()
                other.setUp()
                self.addCleanup(other.doCleanups)
                other.failure = failure
                with patch.object(instrument, "record_stage") as analyze:
                    with self.assertRaises((ValueError, OSError)):
                        other.collect()
                analyze.assert_not_called()
                outcome = instrument.load(other.epoch / "collection-outcome.json")
                self.assertEqual("INCOMPLETE_INTERRUPTED", outcome["spending"]["state"])

    def test_recovery_keeps_first_launch_invalid_and_collects_only_unstarted_slots(self):
        full_slots = self.prepare_recovered_scope()
        self.collect()
        self.assertEqual(full_slots[1:], self.launched)
        report = instrument.load(self.epoch / "ppw-stage-ledger.json")
        first_cell = next(cell for cell in report["perCell"]
                          if cell["pair"] == self.registration["tasks"][0] and cell["arm"] == "A")
        self.assertEqual(1, first_cell["invalidRuns"])
        self.assertEqual(73, first_cell["validRuns"])
        spending = instrument.load(self.epoch / "spending-final.json")
        self.assertEqual("complete", spending["state"])
        self.assertEqual(full_slots[:1], budget.decode(spending["events"][-1]["detail"])[
            "preservedAttemptedSlots"])
        task, arm, run = full_slots[0].split("/")
        preserved_directory = self.epoch / "runs" / task / arm / ("run-" + run)
        wrapper = instrument.load(preserved_directory / "result.json")
        self.assertEqual("pp-w-recovered-invalid-wrapper-v1", wrapper["recordKind"])
        self.assertEqual(
            self.admission["recovery"]["oldBinding"]["harnessArtifacts"],
            wrapper["recoveredAttempt"]["originalProfile"]["sourceHashes"])
        self.assertFalse((preserved_directory / "invalid-terminal.json").exists())
        self.assertFalse((preserved_directory / "original-result.json").exists())
        self.assertEqual(443, len(self.observed))

    def test_recovery_mixes_zero_and_reconciled_terminal_invalids_without_replacement(self):
        full_slots = self.prepare_recovered_scope()
        first_task = self.registration["tasks"][0]
        continued_first_cell = [
            slot["id"] for slot in self.admission["slots"]
            if slot["task"] == first_task and slot["arm"] == "calor-permissive"
        ]
        self.invalid_slots = {
            slot: ("zero-requests" if index % 2 == 0 else "reconciled-requests")
            for index, slot in enumerate(continued_first_cell)
        }
        self.collect()
        self.assertEqual(full_slots[1:], self.launched)
        report = instrument.load(self.epoch / "ppw-stage-ledger.json")
        first_cell = next(cell for cell in report["perCell"]
                          if cell["pair"] == first_task and cell["arm"] == "A")
        self.assertEqual((0, 74, None, None), (
            first_cell["validRuns"], first_cell["invalidRuns"],
            first_cell["shapeRealizedRate"], first_cell["escapeRate"]))
        spending = instrument.load(self.epoch / "spending-final.json")
        invalid_events = [
            budget.decode(event["detail"]) for event in spending["events"]
            if event["kind"] == budget.TERMINAL_INVALID_EVENT
        ]
        self.assertEqual(73, len(invalid_events))
        self.assertEqual(36, sum(
            any(row["slot"] == event["slot"] for row in spending["requests"])
            for event in invalid_events))
        self.assertEqual({
            "preservedAttemptedSlots": full_slots[:1],
            "accountedSlots": 444,
            "validCompletedSlots": 370,
            "invalidTerminalSlots": 74,
        }, budget.decode(spending["events"][-1]["detail"]))

    def test_conflicting_operational_proof_alias_refuses_before_client_launch(self):
        self.prepare_recovered_scope()
        self.selected["recoveryAuthorization"] = dict(
            self.selected["spendAuthorization"], sha256="0" * 64)
        save(self.registration_file, self.registration)
        with self.assertRaisesRegex(ValueError, "operational proof aliases disagree"):
            self.collect()
        self.assertEqual([], self.observed)


if __name__ == "__main__":
    unittest.main()
