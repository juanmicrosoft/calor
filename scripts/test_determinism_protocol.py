#!/usr/bin/env python3
"""0.24 G2 (#1421): positive and negative controls for the verifier determinism protocol.

Each validator control changes one fact and asserts its specific code and no other code. Each
decider control builds synthetic attempt records (no test runs) and asserts the frozen class
and verdict, so a rule cannot silently pass because an unrelated rule fired.
"""
from __future__ import annotations

import contextlib
import copy
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import determinism_protocol as dp  # noqa: E402

ROOT = dp.ROOT
PROTOCOL = dp.load(ROOT, dp.PROTOCOL)
CASES = dp.load(ROOT, dp.CASES)
CONTRACT = dp.load(ROOT, dp.CONTRACT)
ORACLE = "Calor.Verification.Tests.VerifierRuntimeDifferential.VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle"
STRING_ROW = "Calor.Verification.Tests.IntegrationTests.StringInBodyOnly_StillNeverElides"

def codes(protocol=None, cases=None, contract=None, texts=None) -> set[str]:
    return {c for c, _ in dp.validate(ROOT, protocol or copy.deepcopy(PROTOCOL), cases or copy.deepcopy(CASES),
                                      contract or copy.deepcopy(CONTRACT), texts)}

def small_protocol(attempts: int = 2) -> dict:
    """The committed protocol with fewer attempts, so decider controls stay fast."""
    p = copy.deepcopy(PROTOCOL)
    p["runPlan"].update(jobsPerEnvironment=1, attemptsPerJob=attempts)
    return p

def passing_records(protocol, execution="E1", commit="c" * 40) -> list[dict]:
    committed = {a["name"]: dp.sha256_file(ROOT / a["committedPath"]) for a in CASES["artifacts"]}
    profiles = dp.profile_by_id(protocol)
    records = []
    for env in protocol["environments"]:
        for job in range(1, protocol["runPlan"]["jobsPerEnvironment"] + 1):
            for attempt in range(1, protocol["runPlan"]["attemptsPerJob"] + 1):
                results = []
                for pid in env["profiles"]:
                    p = profiles[pid]
                    tests = {n: ",".join(["Passed"] * m) for n, m in dp.selection(p, CASES).items()}
                    result = {"profile": pid, "status": "completed", "exitCode": 0, "seconds": 1.0,
                              "calorCacheExisted": False, "tests": tests, "cells": None, "artifacts": None, "unregistered": []}
                    if p["oracle"]:
                        result["cells"] = {c["id"]: dp.cell_digest(c) + "|0" for c in CASES["cells"]["ids"]}
                        result["artifacts"] = dict(committed)
                    results.append(result)
                records.append({"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"],
                                "protocolSha256": dp.sha256_file(ROOT / dp.PROTOCOL),
                                "harnessSha256": dp.sha256_file(ROOT / dp.HARNESS), "mode": "execution",
                                "executionId": execution, "environment": env["id"], "job": job, "attempt": attempt,
                                "runId": "1", "runAttempt": "1", "commit": commit, "status": "completed",
                                "environmentCheck": {"violations": []}, "profiles": results})
    return records

def find(records, env, attempt=1, job=1) -> dict:
    return next(r for r in records if r["environment"] == env and r["attempt"] == attempt and r["job"] == job)

def profile(record, pid) -> dict:
    return next(p for p in record["profiles"] if p["profile"] == pid)

class RegistrationTests(unittest.TestCase):
    def test_committed_protocol_is_valid(self) -> None:
        self.assertEqual([], dp.validate(ROOT))

    def test_every_contract_determinism_row_is_registered_on_its_platforms(self) -> None:
        rows = {r["id"]: r for r in PROTOCOL["determinismRows"]}
        for row in CONTRACT["determinismRows"]["rows"]:
            self.assertTrue(set(row["platforms"]) <= set(rows[row["id"]]["environments"]), row["id"])

    def test_case_registry_shape(self) -> None:
        self.assertEqual(411, CASES["groups"]["verification"]["count"])
        self.assertEqual(554, CASES["groups"]["compiler-verifier"]["count"])
        self.assertEqual(1170, CASES["cells"]["count"])
        names = {t["name"] for t in CASES["groups"]["verification"]["tests"]}
        self.assertIn(ORACLE, names)
        self.assertIn(STRING_ROW, names)

    def test_worst_case_fits_the_accepted_ceiling(self) -> None:
        execution, control, total = dp.worst_case(PROTOCOL)
        self.assertEqual((555, 115, 1895), (execution, control, total))
        self.assertLessEqual(total, 2000)

    def test_plan_forces_control_mode_on_pull_requests(self) -> None:
        out = Path(tempfile.mkdtemp()) / "out"
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, dp.main(["plan", "--mode", "execution", "--execution-id", "pr-1", "--event", "pull_request",
                                         "--github-output", str(out)]))
        text = out.read_text(encoding="utf-8")
        self.assertIn("mode=control", text)
        matrix = json.loads(text.split("matrix=", 1)[1].splitlines()[0])["include"]
        self.assertEqual(5, len(matrix))
        self.assertTrue(all(m["timeout"] == 20 and "Semantics" in m["projects"] for m in matrix))

    def test_execution_plan_takes_every_pin_from_the_protocol(self) -> None:
        out = Path(tempfile.mkdtemp()) / "out"
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, dp.main(["plan", "--mode", "execution", "--execution-id", "G3-E1", "--github-output", str(out)]))
        matrix = json.loads(out.read_text(encoding="utf-8").split("matrix=", 1)[1].splitlines()[0])["include"]
        self.assertEqual(10, len(matrix))
        self.assertEqual({"10.0.401"}, {m["sdk"] for m in matrix})
        self.assertEqual({70, 50}, {m["timeout"] for m in matrix})
        self.assertTrue(all(("Compiler.Tests" in m["projects"]) == (m["env"] == "linux-x64") for m in matrix))

class NegativeRegistrationControls(unittest.TestCase):
    def assert_only(self, code: str, protocol=None, cases=None, contract=None, texts=None) -> None:
        self.assertEqual({code}, codes(protocol, cases, contract, texts))

    def mutate(self, edit) -> dict:
        p = copy.deepcopy(PROTOCOL)
        edit(p)
        return p

    def test_d001_unfrozen_or_inspected_before_freeze(self) -> None:
        self.assert_only("D001", self.mutate(lambda p: p["freeze"].update(decisionBearingExecutionBeforeFreeze=True)))

    def test_d002_unrecorded_contract_version(self) -> None:
        self.assert_only("D002", self.mutate(lambda p: p["contract"].update(contractVersion="1.0.5")))

    def test_d003_contract_row_missing(self) -> None:
        self.assert_only("D003", self.mutate(lambda p: p["determinismRows"].pop(0)))

    def test_d003_row_without_its_platform(self) -> None:
        self.assert_only("D003", self.mutate(lambda p: p["determinismRows"][0].update(environments=["linux-x64"])))

    def test_d004_floating_runner_label(self) -> None:
        self.assert_only("D004", self.mutate(lambda p: p["environments"][3].update(runner="windows-latest")))

    def test_d004_platform_dropped(self) -> None:
        # Named co-fires: the rows that name win-arm64 (D003) and the recomputed budget (D008).
        self.assertEqual({"D004", "D003", "D008"}, codes(self.mutate(lambda p: p["environments"].pop(4))))

    def test_d005_floating_sdk(self) -> None:
        self.assert_only("D005", self.mutate(lambda p: p["toolchain"].update(sdk="10.0.x")))

    def test_d006_seed_drift(self) -> None:
        self.assert_only("D006", texts={"tests/Shared/Z3TestSeeding.cs": "public const string RandomSeed = \"7\";"})

    def test_d006_timeout_drift(self) -> None:
        self.assert_only("D006", texts={"src/Calor.Compiler/Verification/VerificationOptions.cs": "DefaultTimeoutMs = 10000;"})

    def test_d007_too_few_attempts_or_a_retry(self) -> None:
        self.assert_only("D007", self.mutate(lambda p: p["runPlan"].update(attemptsPerJob=10)))
        self.assert_only("D007", self.mutate(lambda p: p["retryPolicy"].update(retriesPerAttempt=1)))
        self.assert_only("D007", self.mutate(lambda p: p["agreement"].update(requiredAgreementRate=0.99)))
        self.assert_only("D007", self.mutate(lambda p: p["runPlan"].update(earlyStop=True)))

    def test_d008_budget_over_ceiling(self) -> None:
        self.assert_only("D008", self.mutate(lambda p: p["budget"].update(maxExecutions=4)))

    def test_d009_line_ending_normalization(self) -> None:
        self.assert_only("D009", self.mutate(lambda p: p["agreement"].update(normalizationRules=["CRLF -> LF"])))
        self.assert_only("D009", self.mutate(lambda p: p["agreement"]["projections"][0]["dropped"].append("outcome")))

    def test_d010_cell_dropped(self) -> None:
        cases = copy.deepcopy(CASES)
        cases["cells"]["ids"].pop()
        self.assert_only("D010", cases=cases)

    def test_d011_timing_sensitive_without_amendment(self) -> None:
        entry = {"case": f"test:verification:{ORACLE}", "reason": "flaky"}
        self.assert_only("D011", self.mutate(lambda p: p["cases"]["timingSensitiveSet"].append(entry)))

    def test_d012_vocabulary_changed(self) -> None:
        self.assert_only("D012", self.mutate(lambda p: p["attemptStatuses"].pop("timeout")))

    def test_d013_masked_workflow(self) -> None:
        workflow = (ROOT / PROTOCOL["workflow"]["path"]).read_text(encoding="utf-8")
        self.assert_only("D013", texts={PROTOCOL["workflow"]["path"]: workflow.replace("fail-fast: false", "fail-fast: true")})
        self.assert_only("D013", texts={PROTOCOL["workflow"]["path"]: workflow + "\n        continue-on-error: true\n"})

    def test_d014_gate_command_drift(self) -> None:
        test_yml = (ROOT / ".github/workflows/test.yml").read_text(encoding="utf-8")
        self.assert_only("D014", texts={".github/workflows/test.yml": test_yml.replace(
            "VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle\"", "VerifierRuntimeDifferentialTests\"")})

class DecisionControls(unittest.TestCase):
    protocol = small_protocol()

    def decide(self, records, executions=("E1",)) -> dict:
        return dp.decide(ROOT, records, list(executions), "execution", self.protocol, CASES)

    def classes(self, result) -> dict:
        return {r["case"]: r for r in result["cases"]}

    def test_all_agreeing_is_deterministic_and_resolves_every_row(self) -> None:
        result = self.decide(passing_records(self.protocol))
        self.assertEqual("DETERMINISTIC", result["verdict"])
        self.assertTrue(result["complete"])
        self.assertEqual({"RESOLVED"}, {r["status"] for r in result["determinismRows"]})

    def test_windows_only_verdict_is_platform_dependent_disagreement(self) -> None:
        records = passing_records(self.protocol)
        for env in ("win-x64", "win-arm64"):
            for r in records:
                if r["environment"] == env:
                    profile(r, "verification-full")["tests"][STRING_ROW] = "Failed"
        result = self.decide(records)
        row = self.classes(result)[f"test:verification:{STRING_ROW}"]
        self.assertEqual("NON-DETERMINISTIC", result["verdict"])
        self.assertEqual(("DISAGREE", True), (row["class"], row["platformDependent"]))
        rows = {r["id"]: r["status"] for r in result["determinismRows"]}
        self.assertEqual("OPEN", rows["platform-StringInBodyOnly_StillNeverElides"])

    def test_single_cell_flip_on_one_attempt_is_disagreement(self) -> None:
        records = passing_records(self.protocol)
        profile(find(records, "linux-x64", 2), "verification-full")["cells"]["case-000007"] = "0" * 32 + "|1"
        row = self.classes(self.decide(records))["cell:case-000007"]
        self.assertEqual(("DISAGREE", False), (row["class"], row["platformDependent"]))

    def test_crlf_report_bytes_are_never_normalized(self) -> None:
        records = passing_records(self.protocol)
        crlf = dp.sha256_bytes((ROOT / CASES["artifacts"][1]["committedPath"]).read_bytes().replace(b"\n", b"\r\n"))
        for r in records:
            if r["environment"].startswith("win-"):
                for pid in ("verification-full", "oracle-isolated"):
                    profile(r, pid)["artifacts"]["verifier-runtime-differential.md"] = crlf
        result = self.decide(records)
        self.assertEqual("DISAGREE", self.classes(result)["artifact:verifier-runtime-differential.md"]["class"])
        self.assertIn("OPEN", {r["status"] for r in result["determinismRows"]})

    def test_timeout_is_a_value_never_agreement(self) -> None:
        records = passing_records(self.protocol)
        p = profile(find(records, "osx-arm64"), "oracle-isolated")
        p["status"], p["tests"][ORACLE] = "timeout", "Timeout"
        find(records, "osx-arm64")["status"] = "timeout"
        self.assertEqual("DISAGREE", self.classes(self.decide(records))[f"test:verification:{ORACLE}"]["class"])

    def test_consistent_skip_is_failing_not_passing(self) -> None:
        records = passing_records(self.protocol)
        for r in records:
            profile(r, "verification-full")["tests"][STRING_ROW] = "Skipped"
        result = self.decide(records)
        self.assertEqual("FAILING", result["verdict"])
        self.assertEqual("AGREE-FAIL", self.classes(result)[f"test:verification:{STRING_ROW}"]["class"])

    def test_infrastructure_failure_and_missing_records_are_incomplete(self) -> None:
        records = passing_records(self.protocol)
        r = find(records, "win-arm64")
        r["status"], r["profiles"] = "infrastructure-failure", []
        self.assertEqual("INCOMPLETE", self.decide(records)["verdict"])
        records = passing_records(self.protocol)
        records.remove(find(records, "linux-arm64", 2))
        result = self.decide(records)
        self.assertEqual(("INCOMPLETE", 1), (result["verdict"], result["attempts"]["missing"]))

    def test_environment_violation_contributes_nothing(self) -> None:
        records = passing_records(self.protocol)
        find(records, "win-x64")["status"] = "environment-violation"
        self.assertEqual("INCOMPLETE", self.decide(records)["verdict"])

    def test_missing_attempts_cannot_hide_a_disagreement(self) -> None:
        records = passing_records(self.protocol)
        records.remove(find(records, "linux-x64", 1))
        profile(find(records, "linux-arm64"), "verification-full")["tests"][ORACLE] = "Failed"
        self.assertEqual("NON-DETERMINISTIC", self.decide(records)["verdict"])

    def test_rerun_job_foreign_protocol_unregistered_case_and_duplicate_are_invalid(self) -> None:
        mutations = [
            lambda rs: find(rs, "linux-x64").update(runAttempt="2"),
            lambda rs: find(rs, "linux-x64").update(protocolSha256="0" * 64),
            lambda rs: find(rs, "linux-x64").update(harnessSha256="0" * 64),
            lambda rs: profile(find(rs, "linux-x64"), "verification-full")["unregistered"].append("test:New"),
            lambda rs: rs.append(copy.deepcopy(find(rs, "osx-arm64"))),
            lambda rs: find(rs, "osx-arm64").update(commit="d" * 40),
        ]
        for mutate in mutations:
            records = passing_records(self.protocol)
            mutate(records)
            self.assertEqual("INVALID", self.decide(records)["verdict"])

    def test_same_commit_executions_are_pooled(self) -> None:
        first = passing_records(self.protocol, "E1")
        profile(find(first, "win-x64"), "verification-full")["tests"][ORACLE] = "Failed"
        second = passing_records(self.protocol, "E2")
        self.assertEqual("NON-DETERMINISTIC", self.decide(first + second, ("E1", "E2"))["verdict"])

class RecordControls(unittest.TestCase):
    def test_profile_values_compare_exact_bytes_and_flag_unregistered(self) -> None:
        tmp = Path(tempfile.mkdtemp())
        (tmp / "generated").mkdir()
        committed = ROOT / CASES["artifacts"][1]["committedPath"]
        (tmp / "generated" / "verifier-runtime-differential.md").write_bytes(committed.read_bytes().replace(b"\n", b"\r\n"))
        cell = dict(CASES["cells"]["ids"][0], category="x", solverStatus="proven", runtimeVerdict="completed",
                    guardForced=True, elidedWhenEnabled=False, solverHandled=True, mismatch=False, detail=None)
        stray = dict(cell, id="case-999999")
        (tmp / "cells.json").write_text(json.dumps([cell, stray]), encoding="utf-8")
        oracle = dp.profile_by_id(PROTOCOL)["oracle-isolated"]
        values = dp.profile_values(oracle, CASES, {ORACLE: ["Passed"], "Some.New.Test": ["Passed"]}, tmp, None)
        self.assertEqual("Passed", values["tests"][ORACLE])
        self.assertNotEqual(dp.sha256_file(committed), values["artifacts"]["verifier-runtime-differential.md"])
        self.assertEqual("Missing", values["artifacts"]["verifier-runtime-differential.json"])
        self.assertTrue(values["cells"]["case-000001"].endswith("|0"))
        self.assertEqual("Missing", values["cells"]["case-000002"])
        self.assertEqual({"test:Some.New.Test", "cell:case-999999"}, set(values["unregistered"]))

    def test_trx_projection_keeps_name_and_outcome_only(self) -> None:
        trx = Path(tempfile.mkdtemp()) / "t.trx"
        trx.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                       '<UnitTestResult testName="A(x: 1)" outcome="Passed" duration="00:00:01" />'
                       '<UnitTestResult testName="A(x: 1)" outcome="Failed" duration="00:00:09" />'
                       '</Results></TestRun>', encoding="utf-8")
        self.assertEqual({"A(x: 1)": ["Passed", "Failed"]}, dp.parse_trx(trx))

    def test_runner_minutes_round_each_job_up(self) -> None:
        jobs = {"jobs": [{"started_at": "2026-10-03T00:00:00Z", "completed_at": "2026-10-03T00:01:01Z"},
                         {"started_at": "2026-10-03T00:00:00Z", "completed_at": "2026-10-03T00:00:10Z"}]}
        self.assertEqual(3, dp.runner_minutes(jobs))

class LedgerControls(unittest.TestCase):
    def entry(self, eid, commit, verdict, minutes=300, **extra) -> dict:
        return {"executionId": eid, "mode": "execution", "charge": "determinism-compute", "runId": eid,
                "commit": commit, "verdict": verdict, "runnerMinutes": minutes, **extra}

    def check(self, entries, runs=None) -> set[str]:
        return {c for c, _ in dp.check_ledger(PROTOCOL, {"entries": entries}, runs)}

    def test_repair_then_deterministic_is_clean(self) -> None:
        repair = {"from": "a" * 40, "changedPaths": ["tests/Calor.Verification.Tests/X.cs"], "addresses": ["cell:case-000001"]}
        entries = [self.entry("E1", "a" * 40, "NON-DETERMINISTIC"), self.entry("E2", "b" * 40, "DETERMINISTIC", repair=repair)]
        self.assertEqual(set(), self.check(entries))

    def test_rerunning_a_non_deterministic_commit_is_rejected(self) -> None:
        entries = [self.entry("E1", "a" * 40, "NON-DETERMINISTIC"), self.entry("E2", "a" * 40, "DETERMINISTIC")]
        self.assertIn("L004", self.check(entries))

    def test_docs_only_repair_is_rejected(self) -> None:
        repair = {"from": "a" * 40, "changedPaths": ["docs/plans/x.md"], "addresses": ["cell:case-000001"]}
        entries = [self.entry("E1", "a" * 40, "FAILING"), self.entry("E2", "b" * 40, "DETERMINISTIC", repair=repair)]
        self.assertEqual({"L005"}, self.check(entries))

    def test_limits_and_hidden_runs(self) -> None:
        entries = [self.entry(f"E{i}", str(i) * 40, "INCOMPLETE", 600) for i in range(1, 5)]
        self.assertTrue({"L002", "L003"} <= self.check(entries))
        hidden = [{"id": 99, "event": "workflow_dispatch", "display_title": "determinism execution E9"}]
        self.assertEqual({"L006"}, self.check([], hidden))

if __name__ == "__main__":
    unittest.main()
