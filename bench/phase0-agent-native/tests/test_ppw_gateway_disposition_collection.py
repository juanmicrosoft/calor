"""SYNTHETIC #1436 collector integration; no operational authority or native probes.

All financial/profile/archive validators execute against explicitly substituted,
test-owned SYNTHETIC trust roots. Production defaults must reject that authority;
no validator is mocked to approve it and no operational registration is changed.
"""
from contextlib import ExitStack, contextmanager
import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from unittest.mock import Mock, patch

from ppw_pilot_epoch import analysis, synthetic_current_analysis_manifest
import test_ppw_gateway_collection as collection
import test_ppw_gateway_disposition as historical
import test_ppw_gateway_registered_collection as registered


BENCH = collection.BENCH
instrument = collection.instrument
budget = collection.budget
gateway = collection.gateway


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


class SyntheticDispositionFixture:
    """Own all financial state; reuse only existing model/compiler/OS doubles."""

    connect = historical.DispositionFixture.connect
    historical_request = historical.DispositionFixture.historical_request
    create_ledger = historical.DispositionFixture.create_ledger
    old_snapshot = historical.DispositionFixture.old_snapshot
    attempt = historical.DispositionFixture.attempt
    make_authorization = historical.DispositionFixture.make_authorization
    arguments = historical.DispositionFixture.arguments

    def __init__(self, case):
        self.case = case
        self.collector = registered.RegisteredCollectionTests(
            "test_synthetic_source_bound_collector_output_reaches_registered_adjudicator")
        self.collector.setUp()
        case.addCleanup(self.collector.doCleanups)
        self.root = self.collector.root
        self.inputs = self.collector.inputs
        self.core = instrument.helper("ppw-gateway-disposition.py")
        self.registration = instrument.helper("ppw-gateway-disposition-registration.py")
        self.profile = instrument.helper("ppw-gateway-registration.py")
        self.adjudication = instrument.helper("ppw-pilot-adjudicate.py")
        self.spending = self.collector.spending
        self.epoch_id = "SYNTHETIC-disposition-pilot"
        self.collector.epoch_id = self.epoch_id
        self.collector.epoch = self.collector.epochs / self.epoch_id
        self.collector.historical_manifest = False
        self.slots = [value["id"] for value in self.collector.admission["slots"]]
        case.assertEqual(444, len(self.slots))
        case.assertEqual([
            "C-001-quota-adapter/calor-permissive/1",
            "C-001-quota-adapter/calor-strict/1",
            "C-002-shipping-quote/calor-permissive/1",
        ], self.slots[:3])
        self.common = self.root / "SYNTHETIC-git-common"
        self.protected = self.common / "ppw-budget"
        self.protected.mkdir(parents=True, mode=0o700)
        self.ledger_path = self.protected / "epic1254-pilot.sqlite3"
        self.old_backup = self.protected / "SYNTHETIC-old-backup.sqlite3"
        self.new_backup = self.protected / "SYNTHETIC-pre-disposition.sqlite3"
        self.original_archive = self.root / "SYNTHETIC-epoch-001"
        self.failed_archive = self.root / "SYNTHETIC-epoch-002"
        self.request_ids = ("a" * 48, "b" * 48)
        self.create_archives()
        self.old_binding = {
            "stage": "pilot", "epochId": self.failed_archive.name,
            "priceSha256": self.core.HISTORICAL_PRICE_SHA256,
            "authorizationSha256": "1" * 64, "protocolSha256": "2" * 64,
            "planSha256": "3" * 64,
            "harnessArtifacts": analysis.load(
                self.failed_archive / "pins.json")["harnessArtifacts"],
            "plannedSlots": self.slots,
        }
        self.create_ledger()
        with self.connect() as db:
            db.execute("UPDATE events SET created='2026-09-11T00:00:00Z'")
        self.old_backup.write_bytes(b"SYNTHETIC immutable predecessor backup\n")
        self.old_backup.chmod(0o600)
        self.authorization = self.make_authorization()
        self.authorization.update(
            targetEpochId=self.epoch_id,
            targetHarnessArtifacts=self.spending.artifact_manifest(self.spending.GATEWAY))
        self.core.validate_authorization(self.authorization)
        self.build_authority()

    def reference(self, name, value):
        path = self.inputs / ("SYNTHETIC-" + name + ".json")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.core.authorization_bytes(value))
        return {"path": path.name, "sha256": instrument.digest(path)}

    def create_archives(self):
        self.attempt_paths = []
        for index, archive in enumerate((self.original_archive, self.failed_archive)):
            task, arm, run = self.slots[index].split("/")
            prefix = "runs/%s/%s/run-%s/" % (task, arm, run)
            paths = {
                "rawRecordPath": prefix + "result.json",
                "clientInvocationPath": prefix + "client-invocation.json",
                "invalidReasonPath": prefix + "invalid.txt",
                "attemptStartPath": prefix + "attempt-start.json" if index else None,
                "sourcePath": "sources/SYNTHETIC-historical-source.py",
            }
            for name, relative in paths.items():
                if relative is None or name == "sourcePath":
                    continue
                path = archive / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("SYNTHETIC historical evidence\n", encoding="utf-8")
            save(archive / paths["rawRecordPath"], {
                "synthetic": True, "dataKind": "synthetic",
                "invalid": True, "censored": True,
                "reason": "raw API invalid" if index else "accounted launch invalid",
            })
            save(archive / paths["clientInvocationPath"], {"exitCode": 1})
            (archive / paths["invalidReasonPath"]).write_text(
                'SYNTHETIC attempt=0 agent_rc=1: agent output matches error marker: "api error"\n'
                if index else "SYNTHETIC accounted launch invalid\n",
                encoding="utf-8")
            save(archive / "pins.json", {
                "epochId": archive.name, "stage": "pilot", "dataKind": "synthetic",
                "harnessCommit": str(index + 1) * 40,
                "harnessArtifacts": {
                    "SYNTHETIC-historical-source.py": "4" * 64,
                },
            })
            self.attempt_paths.append(paths)

    def build_authority(self):
        selected = self.collector.selected
        selected["epochId"] = self.epoch_id
        funding = analysis.load(self.registration.OLD_AUTHORIZATION)
        self.operational_funding_binding = copy.deepcopy(funding["ledgerBinding"])
        funding.update(
            fixtureKind="SYNTHETIC-no-operation-authority",
            approvalReference="SYNTHETIC://test-owned-funding-anchor",
            approvedBy="SYNTHETIC fixture; no actual financial approval")
        funding["ledgerBinding"]["anchorSha256"] = hashlib.sha256(
            str(self.common.resolve()).encode()).hexdigest()
        self.funding_reference = self.reference("historical-funding", funding)
        self.funding_path = self.inputs / self.funding_reference["path"]
        self.active_analysis = self.inputs / "SYNTHETIC-active-analysis.json"
        self.pre_analysis = self.inputs / self.registration.PRE_DISPOSITION_ANALYSIS.name
        self.pre_analysis.write_bytes(self.registration.OLD_ANALYSIS.read_bytes())
        self.active_analysis.write_bytes(self.pre_analysis.read_bytes())
        self.record_set_path = self.inputs / self.registration.RECORD_SET.name
        self.activation_path = self.inputs / self.registration.ACTIVATION.name
        for path in (self.registration.OLD_PROFILE, self.registration.OLD_PLAN):
            shutil.copy2(path, self.inputs / path.name)
        auth = self.reference("disposition-authorization", self.authorization)
        self.authorization_sha256 = auth["sha256"]
        amendment = {
            "schemaVersion": 1,
            "kind": "pp-w-prospective-spending-instrument-amendment",
            "stage": "pilot", "fixtureKind": "SYNTHETIC-test-only",
            "replacementHarnessArtifacts": self.authorization["targetHarnessArtifacts"],
        }
        selected["spendAuthorization"] = auth
        selected["instrumentAmendment"] = self.reference("amendment", amendment)
        plan = analysis.load(self.registration.OLD_PLAN)
        plan.pop("recovery", None)
        plan.update(
            epochId=self.epoch_id, authorizationSha256=auth["sha256"],
            fixtureKind="SYNTHETIC-no-operation-authority",
            ledgerBinding=copy.deepcopy(funding["ledgerBinding"]),
            protocolSha256=self.spending.protocol_identity(
                self.collector.registration, selected, "pilot", self.epoch_id))
        price = self.reference("prices", budget.price_contract())
        control = plan["clientControl"]
        control["priceContract"] = price
        control["sourceInspector"] = self.collector.admission["sourceInspector"]
        for name in ("clientExecutable", "shellExecutable", "shellSha256", "runtimeSha256",
                     "testHost", "executionRuntime"):
            control[name] = self.collector.admission[name]
        plan["forecastUse"] = "immutable-historical-reference-not-current-headroom"
        plan["currentBudgetPlanning"] = self.registration.planning_projection(plan)
        selected["spendingPlan"] = self.reference("plan", plan)
        self.plan = plan
        self.target_binding = {
            **self.old_binding, "epochId": self.epoch_id,
            "priceSha256": budget.price_identity(),
            "authorizationSha256": auth["sha256"],
            "protocolSha256": plan["protocolSha256"],
            "planSha256": selected["spendingPlan"]["sha256"],
            "harnessArtifacts": self.authorization["targetHarnessArtifacts"],
        }
        # Exercise the real process-evidence parser against an explicit OS double.
        with patch("subprocess.run", side_effect=self.os_command):
            self.proof = self.core.inspect_disposition(**self.arguments())
            self.core.apply_disposition(
                **self.arguments(), confirmed_proof_sha256=self.proof["proofSha256"])

        self.case.assertEqual(plan, analysis.load(
            self.inputs / selected["spendingPlan"]["path"]))
        wire = {
            "kind": "pp-w-engineering-no-forward-native-probe",
            "fixtureKind": "SYNTHETIC-OS-client-boundary-double",
            "success": True, "upstreamRequests": 0, "experimentalObservations": 0,
            "clientSha256": self.collector.isolation.CLIENT_SHA256,
            "clientFlags": self.collector.isolation.registered_client_flags(),
            "kernelEvidence": {"kernelProbe": self.collector.isolation.PROBE_EXPECTATIONS},
            "observations": [{
                "messagesPath": True, "credentialHeaderPresent": True,
                "priceContractAccepted": True, "wireContractAccepted": True,
                "unclassifiedBetaCount": 0,
            }],
            "sourceHashes": {name: instrument.digest(BENCH / name)
                             for name in self.profile.WIRE_SOURCE_HASHES},
            "toolShellSha256": control["shellSha256"],
            "priceContract": price,
        }
        startup = {
            "kind": "SYNTHETIC/native-first-job-gateway-startup-v1",
            "fixtureKind": "SYNTHETIC-OS-client-boundary-double",
            "outcome": "passed", "empirical": False, "modelInvoked": False,
            "provider": {"realUpstreamGuardInstalled": True},
            "accounting": {"state": "complete", "sourceBoundAttemptStart": True,
                           "accountedSlots": 1, "validCompletedSlots": 1,
                           "invalidTerminalSlots": 0},
            "pins": {"clientSha256": self.collector.isolation.CLIENT_SHA256},
            "invocation": {"clientFlags": self.collector.isolation.registered_client_flags()},
            "nativeToolExecution": {"toolResultErrors": 0},
            "observer": {"journal": [{"command": "build"}, {"command": "test"}]},
            "final": {"heldoutPassed": 4, "taskSuccess": True},
            "sourceHashes": {name: instrument.digest(BENCH / name)
                             for name in self.profile.STARTUP_SOURCE_FILES},
            "testSha256": instrument.digest(BENCH / "tests/test_ppw_gateway_native_startup.py"),
        }
        self.profile.validate_native_startup(startup)
        self.registration._validate_wire(wire, price)
        self.profile_path = self.inputs / "SYNTHETIC-execution-profile.json"
        profile = {
            "schemaVersion": 1, "kind": "pp-w-request-gateway-execution-profile",
            "fixtureKind": "SYNTHETIC-no-operation-authority",
            "id": "request-reserving-gateway-disposition-1436",
            "stage": "pilot", "epochId": self.epoch_id,
            "activationRecord": self.activation_path.name,
            "baselineRegistration": {"path": self.profile.BASELINE,
                                     "sha256": self.profile.BASELINE_SHA256},
            "allowedDataKinds": ["synthetic", "empirical"],
            "transportEvidence": self.reference("wire", wire),
            "nativeStartupEvidence": self.reference("startup", startup),
            **{name: selected[name] for name in (
                "spendAuthorization", "spendingPlan", "instrumentAmendment",
                "stageRegistration", "modelRegistration", "sourceInspectionEvidence")},
        }
        save(self.profile_path, profile)
        self.profile_value = profile
        self.profile_reference = {
            "path": self.profile_path.name, "sha256": instrument.digest(self.profile_path)}
        documents = {
            "authorization": auth, "executionProfile": self.profile_reference,
            "spendingPlan": selected["spendingPlan"],
            "instrumentAmendment": selected["instrumentAmendment"],
            "priceContract": price,
            "inspectionProof": self.reference("proof", self.proof),
            "originalArchiveInventory": self.reference(
                "original-inventory", self.core.archive_inventory(self.original_archive)),
            "failedArchiveInventory": self.reference(
                "failed-inventory", self.core.archive_inventory(self.failed_archive)),
            "stoppedSnapshot": self.reference("stopped", self.authorization["oldSnapshot"]),
            "transportEvidence": profile["transportEvidence"],
            "nativeStartupEvidence": profile["nativeStartupEvidence"],
        }
        self.history = {}
        for name, subject in (("financialBoundReview", "historical-bound"),
                              ("methodsReview", "methods-draft-description")):
            expected = self.registration.HISTORY[subject]
            raw = {
                "schemaVersion": 1, "kind": expected["kind"], "verdict": expected["verdict"],
                "fixtureKind": "SYNTHETIC fictional fixture; no operation authority",
                "reviewer": {"humanReview": False},
                "verdictScope": {"historicalBoundOnly": subject == "historical-bound",
                                 "newImplementationApproved": False,
                                 "generatedOperationalRecordsApproved": False,
                                 "operatorExecutionApproved": False},
            }
            ref = self.reference(name + "-raw", raw)
            self.history[subject] = {**ref, "kind": raw["kind"], "verdict": raw["verdict"]}
            documents[name] = self.reference(
                name, self.registration.review_index(subject, ref, raw["verdict"]))
        self.evidence_path = self.inputs / "SYNTHETIC-disposition-evidence.json"
        save(self.evidence_path, {
            "schemaVersion": 1,
            "kind": "pp-w-historical-liability-disposition-evidence-v1",
            "fixtureKind": "SYNTHETIC-no-operation-authority",
            "issue": self.registration.ISSUE, **documents,
            "targetBinding": {"authorization": auth, "binding": self.target_binding},
            "financialProjection": {
                "ceilingMicroUsd": 1_000_000_000, "permanentUnknownMicroUsd": 51_040_000,
                "actualCost": None, "historicalUnknownRequestCount": 2,
                "historicalUnknownRequestIds": list(self.request_ids),
                "liveUnknownRequestCountIfComplete": 0, "futureUnknownPolicy": "halt",
            },
            "historicalLineage": {
                "preDispositionProfile": {
                    "path": self.registration.OLD_PROFILE.name,
                    "sha256": self.registration.EXPECTED_OLD_PROFILE_SHA256},
                "preDispositionPlan": {
                    "path": self.registration.OLD_PLAN.name,
                    "sha256": self.registration.EXPECTED_OLD_PLAN_SHA256},
                "preDispositionAuthorization": {
                    "path": self.funding_path.name,
                    "sha256": self.funding_reference["sha256"]},
                "preDispositionAnalysis": {
                    "path": self.registration.PRE_DISPOSITION_ANALYSIS.name,
                    "sha256": self.registration.EXPECTED_OLD_ANALYSIS_SHA256},
            },
        })
        self.documents = documents
        with self.authority_paths():
            evidence, values = self.registration._load_evidence(require_activation=False)
            self.record_set = self.registration.make_record_set(evidence, values)
            save(self.record_set_path, self.record_set)
            for subject in self.registration.FINAL_SUBJECTS:
                index_path, raw_path = self.registration.final_review_paths(subject)
                raw = {
                    "schemaVersion": 1, "kind": "pp-w-independent-disposition-final-review",
                    "subject": subject, "verdict": "APPROVE",
                    "reviewer": {
                        "name": "SYNTHETIC fictional fixture; no operation authority",
                        "kind": "AI", "independentOfImplementation": True,
                        "humanReview": False, "operator": False,
                    },
                    "binding": self.registration.final_binding(self.record_set, self.authorization),
                    "scope": self.registration.final_scope(subject),
                    "findings": ["SYNTHETIC fictional fixture only"],
                    "limitations": ["SYNTHETIC fictional fixture; no real independent approval"],
                }
                save(self.inputs / raw_path, raw)
                save(self.inputs / index_path, self.registration.review_index(
                    subject, {"path": raw_path, "sha256": instrument.digest(self.inputs / raw_path)},
                    "APPROVE"))
            activation = dict(
                self.registration._activation_value(self.inputs, self.record_set, self.authorization),
                activatedAt="2026-09-11T00:00:00+00:00")
            save(self.activation_path, activation)
            save(self.active_analysis, {
                "recoveryStatus": "historical-liability-registered", "collectionAuthorized": False,
                "supersedes": {"path": self.pre_analysis.relative_to(BENCH).as_posix(),
                               "sha256": instrument.digest(self.pre_analysis)},
                "gatewayExecutionProfile": {
                    "path": self.profile_path.relative_to(BENCH).as_posix(),
                    "sha256": instrument.digest(self.profile_path)},
                "gatewayDispositionEvidence": {
                    "path": self.evidence_path.relative_to(BENCH).as_posix(),
                    "sha256": instrument.digest(self.evidence_path)},
                "gatewayDispositionActivation": {
                    "path": self.activation_path.relative_to(BENCH).as_posix(),
                    "sha256": instrument.digest(self.activation_path)},
            })

    def os_command(self, argv, **kwargs):
        if argv == ["/bin/ps", "-axww", "-o", "pid=,ppid=,pgid=,command="]:
            return subprocess.CompletedProcess(argv, 0, "1 0 1 SYNTHETIC-idle\n", "")
        if argv[0] == "git" and argv[-2:] == ["rev-parse", "--git-common-dir"]:
            return subprocess.CompletedProcess(argv, 0, str(self.common) + "\n", "")
        raise AssertionError("No real subprocess is permitted: %r" % argv)

    @contextmanager
    def authority_paths(self, synthetic_trust_roots=True):
        """Scope private paths and explicit SYNTHETIC identities, not validator results."""
        modules = {
            "ppw-gateway-disposition-registration.py": self.registration,
            "ppw-gateway-registration.py": self.profile,
            "ppw-gateway-disposition.py": self.core,
            "ppw-spending.py": self.spending,
            "ppw-budget-gateway.py": gateway,
            "ppw-gateway-budget.py": budget,
            "ppw-gateway-client.py": self.collector.isolation,
            "ppw-source-inspection.py": self.collector.inspection,
            "ppw-test-host.py": self.collector.test_host,
            "ppw-pilot-adjudicate.py": self.adjudication,
            "ppw-run-observer.py": Mock(RunObserver=lambda **values: Mock(
                model_hidden_roots=tuple(values["hidden_roots"]))),
        }
        with ExitStack() as stack:
            for module, name in ((instrument, "helper"), (self.profile, "helper"),
                                 (self.registration, "module"), (self.spending, "module")):
                original = getattr(module, name)
                stack.enter_context(patch.object(
                    module, name, side_effect=lambda filename, original=original:
                    modules[filename] if filename in modules else original(filename)))
            for name, value in (
                    ("ROOT", self.inputs), ("PROFILE", self.profile_path),
                    ("EVIDENCE", self.evidence_path), ("EPOCH", self.epoch_id),
                    ("RECORD_SET", self.record_set_path), ("ACTIVATION", self.activation_path),
                    ("OLD_ANALYSIS", self.active_analysis),
                    ("PRE_DISPOSITION_ANALYSIS", self.pre_analysis),
                    ("ORIGINAL_ARCHIVE", self.original_archive),
                    ("FAILED_ARCHIVE", self.failed_archive)):
                stack.enter_context(patch.object(self.registration, name, value))
            stack.enter_context(patch.object(
                self.profile, "DISPOSITION_PROFILE", self.profile_path))
            if synthetic_trust_roots:
                stack.enter_context(patch.object(
                    self.registration, "REQUIRED_EVIDENCE_MODE", "SYNTHETIC_TEST_ONLY"))
                stack.enter_context(patch.object(self.registration, "HISTORY", self.history))
                stack.enter_context(patch.object(
                    self.registration, "OLD_AUTHORIZATION", self.funding_path))
                stack.enter_context(patch.object(
                    self.registration, "EXPECTED_OLD_AUTHORIZATION_SHA256",
                    self.funding_reference["sha256"]))
                stack.enter_context(patch.object(
                    self.registration, "GRANT", self.authorization["grant"]["reference"]))
                stack.enter_context(patch.object(
                    self.core, "EXPECTED_OLD_LEDGER_SHA256",
                    self.authorization["oldLedgerSha256"]))
            yield

    def collect(self):
        value = self.collector
        factory, value.observed, _ = collection.gateway_tests.TransportTests.provider(
            self.case, status=500 if value.failure == "unknown" else 200)
        constructor = gateway.Gateway
        serve_forever = gateway.Server.serve_forever
        validate_pins = instrument.validate_pins
        admit = self.spending.admit

        def synthetic_pins(pins, *args):
            pins["dataKind"] = "synthetic"
            return validate_pins(pins, *args)

        def discover(_):
            value.discovery_complete = True
            return [str(self.root / "SYNTHETIC-hidden-repository")]

        def record_admission(*args):
            value.admission = admit(*args)
            return value.admission

        with self.authority_paths(), ExitStack() as stack:
            for module, name, replacement in (
                    (instrument, "product", lambda *_: dict(value.product)),
                    (instrument, "command", value.command),
                    (instrument, "validate_pins", synthetic_pins),
                    (self.spending, "admit", record_admission),
                    (value.inspection, "prepare", lambda *_: value.admission["sourceInspector"]),
                    (value.inspection, "validate_runtime", lambda *_: None),
                    (value.isolation, "validate_platform", lambda: None),
                    (value.isolation, "validate_client", lambda path: Path(path)),
                    (value.isolation, "validate_shell", lambda path, sha: Path(path)),
                    (value.isolation, "validate_runtime", lambda *_: None),
                    (value.test_host, "validate_runtime", lambda *_: None),
                    (value.isolation, "discover_sensitive_roots", discover)):
                stack.enter_context(patch.object(module, name, side_effect=replacement))
            stack.enter_context(patch.object(instrument, "REPO", self.root))
            stack.enter_context(patch("subprocess.run", side_effect=self.os_command))
            stack.enter_context(patch.object(gateway, "Gateway", side_effect=(
                lambda ledger, owner, slot, observer=None:
                constructor(ledger, owner, slot, factory, observer))))
            stack.enter_context(patch.object(
                gateway.Server, "serve_forever",
                lambda server: serve_forever(server, poll_interval=0.001)))
            stack.enter_context(patch.dict(os.environ, {"CLAUDE_MODEL": budget.MODEL}))
            instrument.run_epoch(
                self.profile_path, self.inputs / "tasks", value.compiler_root,
                value.epochs, self.epoch_id, "pilot", True)

    def adjudicate(self, epochs=None):
        epochs = epochs or self.collector.epochs
        epoch = epochs / self.epoch_id
        manifest = synthetic_current_analysis_manifest(analysis, self.root)
        original_module = analysis.module
        with self.authority_paths(), \
                patch.object(analysis, "MANIFEST", manifest), \
                patch.object(analysis, "module", side_effect=lambda name, relative:
                             self.profile if relative == "ppw-gateway-registration.py"
                             else self.registration
                             if relative == "ppw-gateway-disposition-registration.py"
                             else original_module(name, relative)), \
                patch("subprocess.run", side_effect=AssertionError("analysis is read-only")):
            before = analysis.inventory(epoch, instrument)
            try:
                return analysis.adjudicate(epochs, self.epoch_id)
            finally:
                self.case.assertEqual(before, analysis.inventory(epoch, instrument))


class DispositionCollectionTests(unittest.TestCase):
    def setUp(self):
        self.fixture = SyntheticDispositionFixture(self)

    def test_complete_original_442_suffix_reaches_portable_registered_analysis(self):
        fixture = self.fixture
        fixture.collect()
        collector = fixture.collector
        self.assertEqual(fixture.slots[2:], collector.launched)
        self.assertEqual(442, len(collector.observed))
        self.assertEqual(442, len(set(collector.launched)))
        self.assertTrue(set(fixture.slots[:2]).isdisjoint(collector.launched))
        self.assertEqual(444, len(list((collector.epoch / "runs").rglob("result.json"))))
        wrappers = []
        for slot in fixture.slots[:2]:
            task, arm, run = slot.split("/")
            wrapper = analysis.load(
                collector.epoch / "runs" / task / arm / ("run-" + run) / "result.json")
            self.assertEqual("pp-w-historical-disposition-wrapper-v1", wrapper["recordKind"])
            self.assertTrue(wrapper["invalid"])
            self.assertTrue(wrapper["censored"])
            self.assertFalse(wrapper["historicalDisposition"]["replacementPermitted"])
            wrappers.append(wrapper["historicalDisposition"])
        self.assertEqual(list(fixture.core.ATTEMPT_CLASSIFICATIONS),
                         [wrapper["classification"] for wrapper in wrappers])
        self.assertNotEqual(wrappers[0]["sourceEpochId"], wrappers[1]["sourceEpochId"])
        self.assertIsNone(wrappers[0]["originalRun"]["attemptStartPath"])
        self.assertTrue(all(item["originalProfile"]["sourceEvidenceKind"] == "original-pins-only"
                            for item in wrappers))
        final = analysis.load(collector.epoch / "spending-final.json")
        replay = fixture.core.validate_history(
            final, authorization=fixture.authorization,
            authorization_sha256=fixture.authorization_sha256,
            target_binding=fixture.target_binding)
        self.assertEqual(("complete", 0, 2, 51_040_000, None), (
            replay["state"], replay["liveUnknownRequestCount"],
            replay["historicalUnknownRequestCount"], replay["permanentUnknownMicroUsd"],
            replay["actualCost"]))
        self.assertEqual(1_000_000_000, final["ceilingMicroUsd"])
        self.assertEqual(51_040_000 + 442 * collection.expected_charge(),
                         final["exposureMicroUsd"])
        self.assertEqual(fixture.authorization["oldSnapshot"]["requests"],
                         final["requests"][:2])
        completions = [
            budget.decode(event["detail"])["slot"] for event in final["events"]
            if event["kind"] == "slot-complete"
        ]
        self.assertEqual(fixture.slots[2:], completions)
        self.assertEqual(fixture.slots[2:], [request["slot"] for request in final["requests"][2:]])
        cells = instrument.analyze(collector.epochs, fixture.epoch_id, "pilot")["perCell"]
        self.assertEqual([74] * 6, [cell["plannedRuns"] for cell in cells])
        self.assertEqual(2, sum(cell["invalidRuns"] for cell in cells))
        self.assertEqual(442, sum(cell["validRuns"] for cell in cells))
        moved_root = fixture.root / "SYNTHETIC-relocated"
        moved_root.mkdir()
        moved = moved_root / fixture.epoch_id
        shutil.copytree(collector.epoch, moved)
        shutil.rmtree(collector.epoch)
        shutil.rmtree(fixture.original_archive)
        shutil.rmtree(fixture.failed_archive)
        shutil.rmtree(fixture.protected)
        shutil.rmtree(collector.seed)
        report = fixture.adjudicate(moved_root)
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertFalse(report["empirical"])
        self.assertFalse(report["decision"]["stage2Authorized"])
        projection = report["provenance"]["collectionExecutionProjection"]
        self.assertEqual("verified-portable-archive-local-files", projection["evidenceResolution"])
        self.assertEqual((444, 442, 444, 2, 0, 51_040_000, 1_000_000_000, None), (
            projection["accountedSlots"], projection["completedSlots"],
            projection["requestCount"], projection["historicalUnknownRequestCount"],
            projection["liveUnknownRequestCount"], projection["permanentUnknownMicroUsd"],
            projection["ceilingMicroUsd"], projection["actualCost"]))
        # Canonical originals still exist. None may substitute for missing portable proofs.
        archive_only_paths = [
            moved / fixture.activation_path.name,
            moved / fixture.record_set_path.name,
            moved / fixture.documents["spendingPlan"]["path"],
            moved / "admission/disposition-sources/ppw-gateway-disposition-registration.py",
        ]
        archive_only_paths += [
            moved / name for subject in fixture.registration.FINAL_SUBJECTS
            for name in fixture.registration.final_review_paths(subject)]
        archive_only_paths += [
            moved / entry["path"] for entry in fixture.history.values()]
        for path in archive_only_paths:
            before = path.read_bytes()
            with self.subTest(missing_archive_proof=path.relative_to(moved).as_posix()):
                path.unlink()
                try:
                    with self.assertRaises((ValueError, OSError)):
                        fixture.adjudicate(moved_root)
                finally:
                    path.write_bytes(before)
        # The registered consumer still resolves its active registration in
        # validate_scope; archive portability does not remove that prerequisite.
        shutil.rmtree(fixture.inputs)
        with self.assertRaisesRegex(ValueError, "profile and evidence are not registered"):
            fixture.adjudicate(moved_root)

    def test_fixture_profile_validates_without_overwriting_actual_authority(self):
        fixture = self.fixture
        before = (
            fixture.core.EXPECTED_OLD_LEDGER_SHA256,
            fixture.core.EXPECTED_OLD_BACKUP_SHA256,
            fixture.registration.EXPECTED_OLD_AUTHORIZATION_SHA256,
            fixture.registration.OLD_AUTHORIZATION,
            fixture.registration.REQUIRED_EVIDENCE_MODE,
            fixture.registration.GRANT,
            fixture.profile.BASELINE_SHA256,
        )
        with fixture.authority_paths():
            resolved = fixture.registration.resolve_collection_profile(fixture.profile_path)
            instrument.validate_registration(resolved, "pilot", fixture.epoch_id)
            instrument.validate_tasks(fixture.inputs / "tasks", resolved)
            funding = instrument.validate_collection_authorization(
                resolved, resolved["stages"]["pilot"], fixture.inputs, fixture.epoch_id,
                "pilot", True)
            self.assertEqual("SYNTHETIC://test-owned-funding-anchor",
                             funding["approvalReference"])
            self.assertEqual(fixture.plan["ledgerBinding"], funding["ledgerBinding"])
            self.assertEqual(fixture.slots[:2], fixture.core.validate_authorization(
                fixture.authorization)["preservedSlots"])
            self.assertEqual(3, len(resolved["tasks"]))
            self.assertEqual(74, resolved["stages"]["pilot"]["runsPerArm"])
        self.assertEqual(before, (
            fixture.core.EXPECTED_OLD_LEDGER_SHA256,
            fixture.core.EXPECTED_OLD_BACKUP_SHA256,
            fixture.registration.EXPECTED_OLD_AUTHORIZATION_SHA256,
            fixture.registration.OLD_AUTHORIZATION,
            fixture.registration.REQUIRED_EVIDENCE_MODE,
            fixture.registration.GRANT,
            fixture.profile.BASELINE_SHA256,
        ))
        self.assertNotEqual(before[0], fixture.authorization["oldLedgerSha256"])

    def test_authentic_synthetic_disposition_is_not_promoted_to_operator(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        with fixture.authority_paths(synthetic_trust_roots=False):
            with self.assertRaisesRegex(
                    ValueError, "disposition authorization bytes or target binding differ"):
                fixture.registration.resolve_collection_profile(fixture.profile_path)
        self.assertEqual(before, fixture.ledger_path.read_bytes())
        self.assertFalse(fixture.collector.epoch.exists())

    def test_synthetic_review_cannot_impersonate_hard_pinned_actual_approval(self):
        fixture = self.fixture
        review = analysis.load(fixture.inputs / fixture.documents["financialBoundReview"]["path"])
        with self.assertRaisesRegex(ValueError, "unchanged pinned audit"):
            fixture.registration._history_review(fixture.inputs, review, "historical-bound")

    def test_synthetic_trust_roots_still_reject_wrong_review_hashes_and_identities(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        with fixture.authority_paths():
            for kind in fixture.registration.FINAL_SUBJECTS:
                _, raw_path = fixture.registration.final_review_paths(kind)
                review = analysis.load(fixture.inputs / raw_path)
                fixture.registration.validate_final_review(
                    review, kind, fixture.record_set, fixture.authorization)
                for field, value in (
                        ("historicalLedgerSha256", "0" * 64),
                        ("authorizationReference", "SYNTHETIC://wrong-grant"),
                        ("recordSetSha256", "0" * 64),
                        ("sourceArtifactsSha256", "0" * 64),
                        ("permanentLiabilityMicroUsd", 0),
                        ("ceilingMicroUsd", 1_000_000_001)):
                    with self.subTest(review=kind, field=field):
                        altered = copy.deepcopy(review)
                        altered["binding"][field] = value
                        with self.assertRaisesRegex(ValueError, "final review has wrong subject"):
                            fixture.registration.validate_final_review(
                                altered, kind, fixture.record_set, fixture.authorization)
        self.assertEqual(before, fixture.ledger_path.read_bytes())

    def test_synthetic_trust_roots_still_reject_evidence_and_funding_tampering(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        original_evidence = fixture.evidence_path.read_bytes()
        evidence = analysis.load(fixture.evidence_path)
        with fixture.authority_paths():
            fixture.registration.resolve_collection_profile(fixture.profile_path)
            for key in ("authorization", "inspectionProof", "originalArchiveInventory",
                        "failedArchiveInventory", "financialBoundReview", "methodsReview"):
                with self.subTest(reference=key):
                    altered = copy.deepcopy(evidence)
                    altered[key]["sha256"] = "0" * 64
                    try:
                        save(fixture.evidence_path, altered)
                        with self.assertRaisesRegex(ValueError, "missing, linked, or changed"):
                            fixture.registration.resolve_collection_profile(fixture.profile_path)
                    finally:
                        fixture.evidence_path.write_bytes(original_evidence)
            for path, message in (
                    (("targetBinding", "binding", "authorizationSha256"),
                     "authorization bytes or target binding differ"),
                    (("historicalLineage", "preDispositionAuthorization", "sha256"),
                     "lineage differs")):
                with self.subTest(identity=path):
                    altered = copy.deepcopy(evidence)
                    altered[path[0]][path[1]][path[2]] = "0" * 64
                    try:
                        save(fixture.evidence_path, altered)
                        with self.assertRaisesRegex(ValueError, message):
                            fixture.registration.resolve_collection_profile(fixture.profile_path)
                    finally:
                        fixture.evidence_path.write_bytes(original_evidence)
            resolved = fixture.registration.resolve_collection_profile(fixture.profile_path)
            original_funding = fixture.funding_path.read_bytes()
            try:
                fixture.funding_path.write_bytes(original_funding + b"\n")
                with self.assertRaisesRegex(ValueError, "missing, linked, or changed"):
                    instrument.validate_collection_authorization(
                        resolved, resolved["stages"]["pilot"], fixture.inputs,
                        fixture.epoch_id, "pilot", True)
            finally:
                fixture.funding_path.write_bytes(original_funding)
        self.assertEqual(before, fixture.ledger_path.read_bytes())
        self.assertFalse(fixture.collector.epoch.exists())

    def test_proposal_without_activation_never_admits_or_changes_active_analysis(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        with fixture.authority_paths():
            resolved = fixture.registration.resolve_collection_profile(fixture.profile_path)
            activation = fixture.activation_path.read_bytes()
            fixture.activation_path.unlink()
            try:
                evidence, documents = fixture.registration._load_evidence(require_activation=False)
                self.assertEqual(fixture.record_set,
                                 fixture.registration.make_record_set(evidence, documents))
                for action in (
                        lambda: fixture.registration.resolve_collection_profile(fixture.profile_path),
                        lambda: fixture.registration.canonical_inputs(),
                        lambda: instrument.validate_collection_authorization(
                            resolved, resolved["stages"]["pilot"], fixture.inputs,
                            fixture.epoch_id, "pilot", True)):
                    with self.assertRaises((ValueError, OSError)):
                        action()
            finally:
                fixture.activation_path.write_bytes(activation)
            selected = dict(resolved["stages"]["pilot"])
            selected.pop("dispositionEvidence")
            with self.assertRaisesRegex(ValueError, "cannot omit prospective activation"):
                fixture.spending.admit(
                    resolved, selected, {"kind": fixture.core.DISPOSITION_KIND},
                    fixture.inputs, fixture.epoch_id, "pilot")
            active = fixture.active_analysis.read_bytes()
            fixture.active_analysis.write_bytes(fixture.pre_analysis.read_bytes())
            try:
                with self.assertRaisesRegex(ValueError, "active analysis registration differ"):
                    fixture.registration.resolve_collection_profile(fixture.profile_path)
            finally:
                fixture.active_analysis.write_bytes(active)
        self.assertEqual(before, fixture.ledger_path.read_bytes())
        self.assertFalse(fixture.collector.epoch.exists())

    def test_tampered_raw_history_and_record_set_fail_even_with_synthetic_trust_roots(self):
        fixture = self.fixture
        with fixture.authority_paths():
            for path in ([fixture.inputs / item["path"] for item in fixture.history.values()]
                         + [fixture.record_set_path, fixture.activation_path]):
                before = path.read_bytes()
                with self.subTest(path=path.name):
                    try:
                        value = analysis.load(path)
                        value["SYNTHETIC-tampered"] = True
                        save(path, value)
                        with self.assertRaises(ValueError):
                            fixture.registration.resolve_collection_profile(fixture.profile_path)
                    finally:
                        path.write_bytes(before)

    def test_missing_tampered_or_wrong_subject_final_reviews_fail_closed(self):
        fixture = self.fixture
        with fixture.authority_paths():
            for subject in fixture.registration.FINAL_SUBJECTS:
                index_path, raw_path = fixture.registration.final_review_paths(subject)
                index_path, raw_path = fixture.inputs / index_path, fixture.inputs / raw_path
                index_bytes, raw_bytes = index_path.read_bytes(), raw_path.read_bytes()
                original = analysis.load(raw_path)
                mutations = [
                    {"subject": "historical-bound"},
                    {"subject": "methods-draft-description"},
                    {"verdict": "REQUEST_CHANGES"},
                    {"scope": dict(original["scope"], descriptionOnly=True)},
                    {"binding": dict(original["binding"], recordSetSha256="0" * 64)},
                    {"binding": dict(original["binding"], sourceArtifactsSha256="0" * 64)},
                ]
                for changes in mutations:
                    with self.subTest(subject=subject, changes=changes):
                        try:
                            raw = dict(original, **changes)
                            save(raw_path, raw)
                            save(index_path, fixture.registration.review_index(
                                subject, {"path": raw_path.relative_to(fixture.inputs).as_posix(),
                                          "sha256": instrument.digest(raw_path)}, raw["verdict"]))
                            with self.assertRaises(ValueError):
                                fixture.registration.resolve_collection_profile(fixture.profile_path)
                        finally:
                            index_path.write_bytes(index_bytes)
                            raw_path.write_bytes(raw_bytes)
                for path in (index_path, raw_path):
                    before = path.read_bytes()
                    for tamper in ("missing", "changed"):
                        with self.subTest(subject=subject, artifact=path.name, tamper=tamper):
                            try:
                                if tamper == "missing":
                                    path.unlink()
                                else:
                                    path.write_bytes(before + b"\n")
                                with self.assertRaises((ValueError, OSError)):
                                    fixture.registration.resolve_collection_profile(fixture.profile_path)
                            finally:
                                path.write_bytes(before)

    def test_activation_is_exact_subject_metadata_only_and_preserves_proposal_bytes(self):
        fixture = self.fixture
        helper = fixture.registration
        with fixture.authority_paths(), ExitStack() as stack:
            evidence, documents = helper._load_evidence()
            refs, sources = helper.archival_references(evidence, documents)
            artifact_names = tuple(sorted(
                {(fixture.inputs / ref["path"]).relative_to(BENCH).as_posix() for ref in refs}
                | set(sources)))
            for name in ("ARTIFACTS", "RECOVERY_ARTIFACTS", "TERMINAL_BASE_ARTIFACTS",
                         "TERMINAL_ARTIFACTS"):
                stack.enter_context(patch.object(fixture.adjudication, name, ()))
            for name, value in (
                    ("DISPOSITION_ARTIFACTS", artifact_names),
                    ("PRE_DISPOSITION_MANIFEST", fixture.pre_analysis.relative_to(BENCH).as_posix()),
                    ("DISPOSITION_PROFILE", fixture.profile_path.relative_to(BENCH).as_posix()),
                    ("DISPOSITION_EVIDENCE", fixture.evidence_path.relative_to(BENCH).as_posix())):
                stack.enter_context(patch.object(fixture.adjudication, name, value))
            fixture.activation_path.unlink()
            fixture.active_analysis.write_bytes(fixture.pre_analysis.read_bytes())
            before = {path: path.read_bytes() for path in (
                fixture.ledger_path, fixture.old_backup, fixture.new_backup,
                fixture.profile_path, fixture.evidence_path, fixture.record_set_path,
                fixture.pre_analysis)}
            with self.assertRaisesRegex(ValueError, "confirmation must name"):
                helper.activate("0" * 64)
            self.assertFalse(fixture.activation_path.exists())
            activation = helper.activate(fixture.record_set["recordSetSha256"])
            self.assertFalse(activation["ledgerApplied"])
            self.assertFalse(activation["collectionStarted"])
            self.assertFalse(fixture.collector.epoch.exists())
            self.assertEqual(before, {path: path.read_bytes() for path in before})
            helper.resolve_collection_profile(fixture.profile_path)
            self.assertEqual("historical-liability-registered",
                             analysis.load(fixture.active_analysis)["recoveryStatus"])
            with self.assertRaisesRegex(ValueError, "unexecuted proposal"):
                helper.activate(fixture.record_set["recordSetSha256"])

    def test_real_proposal_builder_and_refresh_do_not_activate_or_require_final_approval(self):
        fixture = self.fixture
        helper = fixture.registration
        fixture.ledger_path.write_bytes(fixture.new_backup.read_bytes())
        fixture.new_backup.unlink()
        fixture.activation_path.unlink()
        fixture.active_analysis.write_bytes(fixture.pre_analysis.read_bytes())
        old_profile_path = fixture.inputs / helper.OLD_PROFILE.name
        old_plan_path = fixture.inputs / helper.OLD_PLAN.name
        shutil.copy2(helper.ROOT / "gateway-instrument-amendment-1434.json",
                     fixture.inputs / "gateway-instrument-amendment-1434.json")
        old_plan = analysis.load(old_plan_path)
        old_plan["ledgerBinding"] = fixture.plan["ledgerBinding"]
        save(old_plan_path, old_plan)
        old_profile = analysis.load(old_profile_path)
        old_profile.update(
            spendAuthorization=fixture.funding_reference,
            spendingPlan={"path": old_plan_path.name, "sha256": instrument.digest(old_plan_path)})
        for name in ("stageRegistration", "modelRegistration", "sourceInspectionEvidence"):
            old_profile[name] = fixture.collector.selected[name]
        save(old_profile_path, old_profile)
        output_fields = {
            "authorization": "authorization", "instrument": "instrumentAmendment",
            "plan": "spendingPlan", "profile": "executionProfile", "proof": "inspectionProof",
            "price": "priceContract", "originalInventory": "originalArchiveInventory",
            "failedInventory": "failedArchiveInventory", "stoppedSnapshot": "stoppedSnapshot",
        }
        outputs = {name: fixture.inputs / fixture.documents[field]["path"]
                   for name, field in output_fields.items()}
        outputs.update(evidence=fixture.evidence_path, preAnalysis=fixture.pre_analysis,
                       recordSet=fixture.record_set_path)
        before = {path: path.read_bytes() for path in (
            fixture.ledger_path, fixture.old_backup, fixture.active_analysis, fixture.pre_analysis)}
        with fixture.authority_paths(), ExitStack() as stack:
            for name, value in (
                    ("OLD_PROFILE", old_profile_path), ("OLD_PLAN", old_plan_path),
                    ("EXPECTED_OLD_PROFILE_SHA256", instrument.digest(old_profile_path)),
                    ("EXPECTED_OLD_PLAN_SHA256", instrument.digest(old_plan_path)),
                    ("BACKUP_NAME", fixture.old_backup.name),
                    ("DISPOSITION_BACKUP_NAME", fixture.new_backup.name),
                    ("BOUND_REVIEW", fixture.inputs / fixture.documents["financialBoundReview"]["path"]),
                    ("METHODS_REVIEW", fixture.inputs / fixture.documents["methodsReview"]["path"]),
                    ("WIRE", fixture.inputs / fixture.documents["transportEvidence"]["path"]),
                    ("STARTUP", fixture.inputs / fixture.documents["nativeStartupEvidence"]["path"]),
                    ("OUTPUTS", outputs)):
                stack.enter_context(patch.object(helper, name, value))
            for name, value in (
                    ("EXPECTED_OLD_BACKUP_SHA256", instrument.digest(fixture.old_backup)),
                    ("GRANT_REFERENCE", fixture.authorization["grant"]["reference"]),
                    ("OLD_EPOCHS", (fixture.original_archive.name, fixture.failed_archive.name))):
                stack.enter_context(patch.object(fixture.core, name, value))
            stack.enter_context(patch("subprocess.run", side_effect=fixture.os_command))
            # No final reviews at all: proposal construction remains possible.
            for subject in helper.FINAL_SUBJECTS:
                for path in helper.final_review_paths(subject):
                    (fixture.inputs / path).unlink()
            proposed = helper.build_documents()
            self.assertNotIn(fixture.active_analysis, proposed)
            self.assertNotIn(fixture.activation_path, proposed)
            self.assertEqual("REQUEST_CHANGES", proposed[fixture.evidence_path][
                "reviewHistoryOnly"]["draftMethodsDescription"]["verdict"])
            for path in outputs.values():
                path.unlink()
            helper.write_documents(proposed)
            self.assertEqual(before, {path: path.read_bytes() for path in before})
            helper.write_documents(proposed, refresh_unexecuted=True)
            self.assertEqual(before, {path: path.read_bytes() for path in before})
            self.assertFalse(fixture.activation_path.exists())
            self.assertFalse(fixture.collector.epoch.exists())
            self.assertFalse(fixture.new_backup.exists())
            evidence, documents = helper._load_evidence(require_activation=False)
            helper._validate_record_set(
                fixture.inputs, evidence, documents, proposed[fixture.record_set_path], live=True)
            with self.assertRaises((ValueError, OSError)):
                helper.resolve_collection_profile(fixture.profile_path)
            with self.assertRaises((ValueError, OSError)):
                helper.activate(proposed[fixture.record_set_path]["recordSetSha256"])
            self.assertEqual(before, {path: path.read_bytes() for path in before})

    def test_actual_funding_anchor_cannot_be_reset_to_private_synthetic_ledger(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        with fixture.authority_paths(), \
                patch("subprocess.run", side_effect=fixture.os_command):
            with self.assertRaisesRegex(ValueError, "another clone/state root cannot reset"):
                fixture.spending.gateway_ledger_location(fixture.operational_funding_binding)
        self.assertEqual(before, fixture.ledger_path.read_bytes())
        self.assertFalse(fixture.collector.epoch.exists())

    def test_future_unknown_halts_real_gateway_and_refuses_scope_completion(self):
        fixture = self.fixture
        ledger = budget.RequestLedger(fixture.ledger_path)
        ledger.initialize(fixture.target_binding, 1_000_000_000)
        owner = ledger.start()
        factory, observed, _ = collection.gateway_tests.TransportTests.provider(self, status=500)
        with gateway.Gateway(ledger, owner, fixture.slots[2], factory) as proxy:
            client, response = collection.gateway_tests.TransportTests.send(
                self, proxy, collection.body())
            self.assertEqual(500, response.status)
            response.read()
            client.close()
        self.assertEqual(1, len(observed))
        snapshot = ledger.snapshot()
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", snapshot["state"])
        self.assertEqual(fixture.authorization["oldSnapshot"]["requests"],
                         snapshot["requests"][:2])
        self.assertEqual(fixture.slots[2], snapshot["requests"][2]["slot"])
        self.assertEqual(51_040_000 + budget.admit_request(
            collection.body())["maximumMicroUsd"], snapshot["exposureMicroUsd"])
        replay = fixture.core.validate_history(
            snapshot, authorization=fixture.authorization,
            authorization_sha256=fixture.authorization_sha256,
            target_binding=fixture.target_binding)
        self.assertEqual(1, replay["liveUnknownRequestCount"])
        self.assertEqual(2, replay["historicalUnknownRequestCount"])
        self.assertEqual(51_040_000, replay["permanentUnknownMicroUsd"])
        self.assertIsNone(replay["actualCost"])
        with self.assertRaises((ValueError, budget.Refusal)):
            ledger.complete(owner)
        with self.assertRaises((ValueError, budget.Refusal)):
            ledger.reserve(owner, fixture.slots[3], budget.admit_request(collection.body()))
        self.assertEqual(1, len(observed))

    def test_collector_future_unknown_stops_before_next_slot_and_refuses_analysis(self):
        fixture = self.fixture
        collector = fixture.collector
        collector.failure = "unknown"
        with self.assertRaises(subprocess.CalledProcessError):
            fixture.collect()
        self.assertEqual(fixture.slots[2:3], collector.launched)
        self.assertEqual(1, len(collector.observed))
        outcome = analysis.load(collector.epoch / "collection-outcome.json")
        self.assertFalse(outcome["complete"])
        self.assertIsNone(outcome["verdict"])
        self.assertEqual("collecting", analysis.load(collector.epoch / "pins.json")["lifecycle"])
        final = outcome["spending"]
        self.assertEqual("INCOMPLETE_UNKNOWN_CHARGE", final["state"])
        self.assertEqual(fixture.authorization["oldSnapshot"]["requests"], final["requests"][:2])
        replay = fixture.core.validate_history(
            final, authorization=fixture.authorization,
            authorization_sha256=fixture.authorization_sha256,
            target_binding=fixture.target_binding)
        self.assertEqual((1, 2, 51_040_000, None), (
            replay["liveUnknownRequestCount"], replay["historicalUnknownRequestCount"],
            replay["permanentUnknownMicroUsd"], replay["actualCost"]))
        self.assertEqual(51_040_000 + budget.admit_request(
            collection.body())["maximumMicroUsd"], final["exposureMicroUsd"])
        with self.assertRaisesRegex(ValueError, "collect|lifecycle|incomplete"):
            fixture.adjudicate()

    def test_all_invalid_suffix_is_accounted_without_inventing_eligible_results(self):
        fixture = self.fixture
        collector = fixture.collector
        collector.invalid_slots = {
            slot: "zero-requests" if index % 2 == 0 else "reconciled"
            for index, slot in enumerate(fixture.slots[2:])
        }
        fixture.collect()
        self.assertEqual(fixture.slots[2:], collector.launched)
        self.assertEqual(442, len(set(collector.launched)))
        self.assertEqual(221, len(collector.observed))
        final = analysis.load(collector.epoch / "spending-final.json")
        replay = fixture.core.validate_history(
            final, authorization=fixture.authorization,
            authorization_sha256=fixture.authorization_sha256,
            target_binding=fixture.target_binding)
        self.assertEqual(("complete", 0, 442, 0), (
            replay["state"], replay["validCompletedSlots"], replay["invalidTerminalSlots"],
            replay["remainingNonterminalSlots"]))
        self.assertEqual(51_040_000 + 221 * collection.expected_charge(),
                         final["exposureMicroUsd"])
        self.assertEqual(1_000_000_000, final["ceilingMicroUsd"])
        self.assertEqual(fixture.authorization["oldSnapshot"]["requests"], final["requests"][:2])
        report = fixture.adjudicate()
        self.assertEqual("SYNTHETIC_ONLY", report["decision"]["status"])
        self.assertEqual("UNIDENTIFIED", report["decision"]["evaluatedStatus"])
        self.assertFalse(report["empirical"])
        self.assertFalse(report["decision"]["stage2Authorized"])
        self.assertTrue(all(value is None for value in report["stoppingRules"].values()))
        self.assertTrue(all(value["exactEstimate"] is None for value in report["estimands"].values()))
        self.assertEqual([74] * 6, [cell["invalid"] for cell in report["accounting"]])
        self.assertEqual([0] * 6, [cell["eligible"] for cell in report["accounting"]])
        projection = report["provenance"]["collectionExecutionProjection"]
        # The ledger projection includes one historical terminal-invalid slot;
        # the replay's 442 count above covers only the new suffix.
        self.assertEqual((444, 0, 443, 444, 51_040_000, None), (
            projection["accountedSlots"], projection["completedSlots"],
            projection["invalidTerminalSlots"], projection["scientificInvalidRuns"],
            projection["permanentUnknownMicroUsd"], projection["actualCost"]))

    def test_portable_historical_wrappers_reject_tampering_and_missing_wrapper(self):
        fixture = self.fixture
        epoch = fixture.root / "SYNTHETIC-wrapper-only-NOT-complete"
        epoch.mkdir()
        pins = analysis.load(fixture.collector.seed / "pins.json")
        pins.update(epochId=fixture.epoch_id,
                    harnessArtifacts=fixture.authorization["targetHarnessArtifacts"])
        evidence = {
            "path": fixture.evidence_path.name, "sha256": instrument.digest(fixture.evidence_path)}
        instrument.preserve_disposition_attempts(epoch, pins, {
            "disposition": {
                "authorizationValue": fixture.authorization, "evidence": evidence,
                "originalArchive": str(fixture.original_archive),
                "failedArchive": str(fixture.failed_archive),
            },
        })
        shutil.rmtree(fixture.original_archive)
        shutil.rmtree(fixture.failed_archive)
        for index, inventory_name in enumerate(
                ("originalArchiveInventory", "failedArchiveInventory")):
            inventory = analysis.load(fixture.inputs / fixture.documents[inventory_name]["path"])
            fixture.registration._wrapper(
                epoch, pins, fixture.authorization, evidence, index, inventory)
        second = fixture.authorization["preservedAttempts"][1]
        copied_result = (
            epoch / "admission/historical" / second["epochId"] / second["rawRecordPath"])
        original = copied_result.read_bytes()
        copied_result.write_bytes(original + b"SYNTHETIC altered raw evidence\n")
        inventory = analysis.load(
            fixture.inputs / fixture.documents["failedArchiveInventory"]["path"])
        with self.assertRaisesRegex(ValueError, "copied historical attempt artifact differs"):
            fixture.registration._wrapper(
                epoch, pins, fixture.authorization, evidence, 1, inventory)
        copied_result.write_bytes(original)
        task, arm, run = second["slot"].split("/")
        (epoch / "runs" / task / arm / ("run-" + run) / "result.json").unlink()
        with self.assertRaises(FileNotFoundError):
            fixture.registration._wrapper(
                epoch, pins, fixture.authorization, evidence, 1, inventory)


if __name__ == "__main__":
    unittest.main()
