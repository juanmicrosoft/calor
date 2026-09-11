"""SYNTHETIC #1436 collector integration; no operational authority or native probes.

All financial/profile/archive validators execute against explicitly substituted,
test-owned SYNTHETIC trust roots. Production defaults must reject that authority;
no validator is mocked to approve it and no operational registration is changed.
"""
from contextlib import ExitStack, contextmanager
import copy
import functools
import hashlib
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
save = collection.save


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
            "harnessArtifacts": {"ppw-gateway-budget.py": "4" * 64},
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
                "attemptStartPath": prefix + "attempt-start.json",
                "sourcePath": "sources/SYNTHETIC-historical-source.py",
            }
            for relative in paths.values():
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
                "SYNTHETIC actual API raw invalid and censored\n"
                if index else "SYNTHETIC accounted launch invalid\n",
                encoding="utf-8")
            save(archive / "pins.json", {
                "epochId": archive.name, "stage": "pilot", "dataKind": "synthetic",
                "harnessArtifacts": {
                    "SYNTHETIC-historical-source.py":
                        instrument.digest(archive / paths["sourcePath"]),
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
        for name, review_type in (("financialBoundReview", "financial-bound"),
                                  ("methodsReview", "registered-methods")):
            documents[name] = self.reference(name, {
                "schemaVersion": 1, "kind": "pp-w-retained-liability-review",
                "reviewType": review_type, "verdict": "APPROVE",
                "authorizationReference": self.authorization["grant"]["reference"],
                "historicalLedgerSha256": self.authorization["oldLedgerSha256"],
                "permanentLiabilityMicroUsd": 51_040_000,
                "ceilingMicroUsd": 1_000_000_000,
                "reference": "SYNTHETIC://test-owned-review",
                "scope": "SYNTHETIC fixture only; not an independent review",
                "findings": ["SYNTHETIC no operation authority"],
            })
        self.evidence_path = self.inputs / "SYNTHETIC-disposition-evidence.json"
        save(self.evidence_path, {
            "schemaVersion": 1,
            "kind": "pp-w-historical-liability-disposition-evidence-v1",
            "fixtureKind": "SYNTHETIC-no-operation-authority",
            "issue": self.registration.ISSUE, **documents,
            "targetBinding": {"authorization": auth, "binding": self.target_binding},
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
                    ("ORIGINAL_ARCHIVE", self.original_archive),
                    ("FAILED_ARCHIVE", self.failed_archive)):
                stack.enter_context(patch.object(self.registration, name, value))
            stack.enter_context(patch.object(
                self.profile, "DISPOSITION_PROFILE", self.profile_path))
            if synthetic_trust_roots:
                stack.enter_context(patch.object(
                    self.registration, "REQUIRED_EVIDENCE_MODE", "SYNTHETIC_TEST_ONLY"))
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
            original_load = self.registration._load_evidence
            stack.enter_context(patch.object(
                self.registration, "_load_evidence",
                new=functools.partial(original_load, self.evidence_path)))
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
        with self.assertRaisesRegex(ValueError, "independent review does not approve"):
            fixture.registration._review_value(review, "financial-bound")

    def test_synthetic_trust_roots_still_reject_wrong_review_hashes_and_identities(self):
        fixture = self.fixture
        before = fixture.ledger_path.read_bytes()
        with fixture.authority_paths():
            for name, kind in (("financialBoundReview", "financial-bound"),
                               ("methodsReview", "registered-methods")):
                review = analysis.load(fixture.inputs / fixture.documents[name]["path"])
                self.assertEqual(review, fixture.registration._review_value(review, kind))
                for field, value in (
                        ("historicalLedgerSha256", "0" * 64),
                        ("authorizationReference", "SYNTHETIC://wrong-grant"),
                        ("reviewType", "SYNTHETIC-wrong-review-type"),
                        ("permanentLiabilityMicroUsd", 0),
                        ("ceilingMicroUsd", 1_000_000_001)):
                    with self.subTest(review=name, field=field):
                        altered = dict(review, **{field: value})
                        with self.assertRaisesRegex(ValueError, "independent review does not approve"):
                            fixture.registration._review_value(altered, kind)
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
                with self.assertRaisesRegex(ValueError, "historical funding/null-result authorization changed"):
                    instrument.validate_collection_authorization(
                        resolved, resolved["stages"]["pilot"], fixture.inputs,
                        fixture.epoch_id, "pilot", True)
            finally:
                fixture.funding_path.write_bytes(original_funding)
        self.assertEqual(before, fixture.ledger_path.read_bytes())
        self.assertFalse(fixture.collector.epoch.exists())

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
