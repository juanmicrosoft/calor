"""SYNTHETIC consumer/registration tests for the #1436 disposition."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

from ppw_pilot_epoch import BENCH, analysis, build


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, BENCH / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


registration = load(
    "ppw_disposition_registration_test", "ppw-gateway-disposition-registration.py")
instrument = load("ppw_disposition_instrument_test", "ppw-instrument.py")
core = load("ppw_disposition_core_consumer_test", "ppw-gateway-disposition.py")


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class DispositionRegistrationTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (
            ".disposition-registration-SYNTHETIC-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)

    def test_contract_is_read_only_fixed_and_prominent_about_unknown_cost(self):
        with patch.object(Path, "read_text", side_effect=AssertionError("contract cannot read")), \
                patch.object(Path, "read_bytes", side_effect=AssertionError("contract cannot read")):
            contract = registration.registration_contract()
        self.assertEqual("w-rows-pilot-gateway-003", contract["targetEpochId"])
        self.assertEqual((444, 2, 442), (
            contract["plannedInvocations"], contract["retainedHistoricalAttempts"],
            contract["continuationInvocations"]))
        self.assertEqual(51_040_000, contract["permanentUnknownMicroUsd"])
        self.assertIsNone(contract["actualCost"])
        self.assertEqual("halt", contract["futureUnknownPolicy"])
        self.assertFalse(contract["generatorCreatesEpoch"])
        self.assertFalse(contract["generatorAppliesDisposition"])
        self.assertFalse(contract["generatorStartsCollection"])

    def test_builder_holds_before_any_actual_read_without_independent_reviews(self):
        with patch.object(registration, "_review",
                          side_effect=ValueError("SYNTHETIC missing independent review")), \
                patch.object(registration, "digest", wraps=registration.digest) as digests:
            with self.assertRaisesRegex(ValueError, "missing independent review"):
                registration.build_documents()
        self.assertEqual(
            {registration.OLD_PROFILE, registration.OLD_PLAN,
             registration.OLD_AUTHORIZATION, registration.OLD_ANALYSIS},
            {call.args[0] for call in digests.call_args_list})

    def test_review_validator_never_fabricates_or_loosens_approval(self):
        review = self.root / "review.json"
        base = registration.review_index("historical-bound", {
            key: registration.HISTORY["historical-bound"][key] for key in ("path", "sha256")
        }, "APPROVE")
        for field, value in (
                ("verdict", "HOLD"),
                ("subject", "source-implementation"),
                ("artifact", {"path": "SYNTHETIC.json", "sha256": "0" * 64}),
                ("findings", ["SYNTHETIC invented scope"])):
            changed = dict(base)
            changed[field] = value
            save(review, changed)
            with self.subTest(field=field), self.assertRaises(ValueError):
                registration._review(review, "historical-bound")

    def test_genuine_history_is_resolved_verbatim_without_promotion(self):
        for path, subject in ((registration.BOUND_REVIEW, "historical-bound"),
                              (registration.METHODS_REVIEW, "methods-draft-description")):
            index = registration._review(path, subject)
            raw_path = registration.ROOT / index["artifact"]["path"]
            before = raw_path.read_bytes()
            raw = registration._history_review(registration.ROOT, index, subject)
            self.assertEqual(registration.HISTORY[subject]["sha256"], digest(raw_path))
            self.assertFalse(raw["reviewer"]["humanReview"])
            self.assertFalse(raw["verdictScope"]["newImplementationApproved"])
            self.assertFalse(raw["verdictScope"]["operatorExecutionApproved"])
            self.assertEqual(before, raw_path.read_bytes())
        self.assertEqual("REQUEST_CHANGES", registration.load(registration.METHODS_REVIEW)["verdict"])

    def test_current_planning_keeps_old_forecast_and_uses_4860_headroom(self):
        plan = registration.load(registration.OLD_PLAN)
        original = copy.deepcopy(plan)
        plan["forecastUse"] = "immutable-historical-reference-not-current-headroom"
        plan["currentBudgetPlanning"] = registration.planning_projection(plan)
        registration.validate_planning(plan)
        self.assertEqual(original["forecast"], plan["forecast"])
        self.assertEqual(original["forecastRegistration"], plan["forecastRegistration"])
        projection = plan["currentBudgetPlanning"]
        self.assertEqual((948_960_000, 951_400_000, 48_600_000, 1_041_440_000, 1_176_490_000), (
            projection["initialAvailableMicroUsd"], projection["conservativeCombinedMicroUsd"],
            projection["planningHeadroomMicroUsd"],
            projection["pricingSensitivityPlusEncumbranceMicroUsd"],
            projection["trafficStressPlusEncumbranceMicroUsd"]))
        for field, value in (("planningHeadroomMicroUsd", 99_640_000),
                             ("unchangedHistoricalReferenceMicroUsd", 898_000_000),
                             ("newApprovedForecast", True), ("completionGuarantee", True)):
            with self.subTest(field=field):
                changed = copy.deepcopy(plan)
                changed["currentBudgetPlanning"][field] = value
                with self.assertRaisesRegex(ValueError, "USD 48.60"):
                    registration.validate_planning(changed)

    def historical_archive(self, epoch_id, source_name, source_sha, second=False):
        archive = self.root / epoch_id
        run = archive / (
            "runs/C-001-quota-adapter/"
            + ("calor-strict" if second else "calor-permissive")
            + "/run-1"
        )
        run.mkdir(parents=True)
        (run / "result.json").write_text(
            '{"SYNTHETIC":"historical record","invalid":true,"censored":true}\n', encoding="utf-8")
        (run / "client-invocation.json").write_text(
            '{"SYNTHETIC":"opaque invocation"}\n', encoding="utf-8")
        (run / "invalid.txt").write_text(
            'SYNTHETIC attempt=0 agent_rc=1: agent output matches error marker: "api error"\n'
            if second else "SYNTHETIC historical terminal invalid\n",
            encoding="utf-8")
        if second:
            (run / "attempt-start.json").write_text(
                '{"SYNTHETIC":"opaque attempt start"}\n', encoding="utf-8")
        source = archive / source_name
        source.parent.mkdir(parents=True)
        source.write_text("SYNTHETIC source\n", encoding="utf-8")
        source_sha = digest(source)
        save(archive / "pins.json", {
            "epochId": epoch_id,
            "stage": "pilot",
            "harnessCommit": str(int(second) + 1) * 40,
            "harnessArtifacts": {source_name: source_sha},
        })
        inventory = core.archive_inventory(archive)
        entries = {item["path"]: item["sha256"] for item in inventory["files"]}
        prefix = run.relative_to(archive).as_posix()
        return archive, inventory, {
            "slot": (
                "C-001-quota-adapter/calor-strict/1"
                if second else "C-001-quota-adapter/calor-permissive/1"
            ),
            "epochId": epoch_id,
            "classification": core.ATTEMPT_CLASSIFICATIONS[int(second)],
            "archiveRole": core.ARCHIVE_ROLES[int(second)],
            "rawRecordPath": prefix + "/result.json",
            "rawRecordSha256": entries[prefix + "/result.json"],
            "clientInvocationPath": prefix + "/client-invocation.json",
            "clientInvocationSha256": entries[prefix + "/client-invocation.json"],
            "invalidReasonPath": prefix + "/invalid.txt",
            "invalidReasonSha256": entries[prefix + "/invalid.txt"],
            "attemptStartPath": prefix + "/attempt-start.json" if second else None,
            "attemptStartSha256":
                entries[prefix + "/attempt-start.json"] if second else None,
            "sourceHashes": {source_name: source_sha},
            "sourceEvidenceKind": "archived-files",
            "sourceCommit": str(int(second) + 1) * 40,
        }

    def test_two_historical_wrappers_form_one_portable_444_population(self):
        epochs = self.root / "epochs"
        epoch = build(epochs)
        target = epochs / "w-rows-pilot-gateway-003"
        epoch.rename(target)
        pins = analysis.load(target / "pins.json")
        registration_value = analysis.load(target / "registration.json")
        pins["epochId"] = target.name
        pins["harnessArtifacts"].update({
            name: digest(BENCH / name)
            for name in ("run-pair.sh", "ppw-gateway-budget.py", "ppw-instrument.py")
        })
        registration_value["stages"]["pilot"].update(
            epochId=target.name,
            historicalSourceEpochIds=["SYNTHETIC-epoch-001", "SYNTHETIC-epoch-002"],
        )
        for result in (target / "runs").rglob("result.json"):
            value = analysis.load(result)
            value["epochId"] = target.name
            save(result, value)
        save(target / "registration.json", registration_value)
        pins["registrationSha256"] = digest(target / "registration.json")
        save(target / "pins.json", pins)
        for arm in ("calor-permissive", "calor-strict"):
            shutil.rmtree(
                target / "runs/C-001-quota-adapter" / arm / "run-1")

        original, original_inventory, attempt_a = self.historical_archive(
            "SYNTHETIC-epoch-001", "sources/original.py", "unused")
        failed, failed_inventory, attempt_b = self.historical_archive(
            "SYNTHETIC-epoch-002", "sources/failed.py", "unused", second=True)
        evidence = {"path": "SYNTHETIC-disposition.json", "sha256": "e" * 64}
        authorization = {
            "kind": core.DISPOSITION_KIND,
            "oldSnapshot": {"binding": {
                "harnessArtifacts": analysis.load(failed / "pins.json")["harnessArtifacts"]}},
            "preservedAttempts": [attempt_a, attempt_b],
            "predecessorArchives": [
                {
                    "epochId": attempt_a["epochId"],
                    "archiveRole": attempt_a["archiveRole"],
                    "pathSha256": hashlib.sha256(str(original.resolve()).encode()).hexdigest(),
                    "inventorySha256": original_inventory["sha256"],
                    "fileCount": original_inventory["fileCount"],
                    "byteCount": original_inventory["byteCount"],
                    "pinsPath": "pins.json",
                    "pinsSha256": digest(original / "pins.json"),
                },
                {
                    "epochId": attempt_b["epochId"],
                    "archiveRole": attempt_b["archiveRole"],
                    "pathSha256": hashlib.sha256(str(failed.resolve()).encode()).hexdigest(),
                    "inventorySha256": failed_inventory["sha256"],
                    "fileCount": failed_inventory["fileCount"],
                    "byteCount": failed_inventory["byteCount"],
                    "pinsPath": "pins.json",
                    "pinsSha256": digest(failed / "pins.json"),
                },
            ],
        }
        admission = {
            "disposition": {
                "authorizationValue": authorization,
                "evidence": evidence,
                "originalArchive": str(original),
                "failedArchive": str(failed),
            },
        }
        instrument.preserve_disposition_attempts(target, pins, admission)
        report = instrument.analyze(epochs, target.name, "pilot")
        self.assertEqual(444, sum(cell["plannedRuns"] for cell in report["perCell"]))
        self.assertEqual(2, sum(cell["invalidRuns"] for cell in report["perCell"]))
        self.assertEqual(442, sum(cell["validRuns"] for cell in report["perCell"]))
        for cell in report["perCell"]:
            self.assertEqual(74, cell["plannedRuns"])
        adjudication = analysis.aggregate(
            report, pins, analysis.load(BENCH / analysis.METHOD))
        self.assertEqual(2, sum(row["invalid"] for row in adjudication["accounting"]))
        self.assertTrue(all(row["scheduled"] == 74
                            for row in adjudication["accounting"]))
        self.assertFalse(adjudication["decision"]["stage2Authorized"])
        for index, attempt in enumerate((attempt_a, attempt_b)):
            task, arm, run = attempt["slot"].split("/")
            wrapper = analysis.load(
                target / "runs" / task / arm / ("run-" + run) / "result.json")
            self.assertEqual(evidence, wrapper["historicalDisposition"]["dispositionReference"])
            self.assertEqual(attempt["rawRecordSha256"],
                             wrapper["historicalDisposition"]["originalRun"]["rawRecordSha256"])

        relocated_root = self.root / "relocated"
        relocated_root.mkdir()
        relocated = relocated_root / target.name
        target.rename(relocated)
        moved = instrument.analyze(relocated_root, relocated.name, "pilot")
        self.assertEqual(report["perCell"], moved["perCell"])
        for epoch_id, inventory in (
                (attempt_a["epochId"], original_inventory),
                (attempt_b["epochId"], failed_inventory)):
            self.assertEqual(
                inventory,
                core.archive_inventory(relocated / "admission/historical" / epoch_id))

    def test_write_refuses_partial_documents_and_existing_target_epoch(self):
        with self.assertRaisesRegex(ValueError, "document set differs"):
            registration.write_documents({})
        target = self.root / "epochs" / registration.EPOCH
        target.mkdir(parents=True)
        documents = {path: {} for path in registration.OUTPUTS.values()}
        with patch.object(registration, "BENCH", self.root):
            with self.assertRaisesRegex(ValueError, "target epoch already exists"):
                registration.write_documents(documents)


if __name__ == "__main__":
    unittest.main()
