#!/usr/bin/env python3
"""0.24 G2 (#1421): positive and negative controls for the verifier determinism protocol.

Each validator control changes one fact and asserts its specific code and no other code (or a
named co-fire). Each decider control builds synthetic attempt records (no test runs) and asserts
the frozen class and verdict, so a rule cannot pass because an unrelated rule fired.
"""
from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import determinism_protocol as dp  # noqa: E402

ROOT = dp.ROOT
PROTOCOL, CASES, CONTRACT = dp.load(ROOT, dp.PROTOCOL), dp.load(ROOT, dp.CASES), dp.load(ROOT, dp.CONTRACT)
HASHES = dp.load(ROOT, dp.HASHES)["files"]
ORACLE = "Calor.Verification.Tests.VerifierRuntimeDifferential.VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle"
STRING_ROW = "Calor.Verification.Tests.IntegrationTests.StringInBodyOnly_StillNeverElides"
THEORY = next(t["name"] for t in CASES["groups"]["verification"]["tests"] if t["multiplicity"] > 1)
COMMIT, RUN = "c" * 40, "4242"


def codes(protocol=None, cases=None, texts=None, baseline=None) -> set[str]:
    return {c for c, _ in dp.validate(ROOT, protocol or copy.deepcopy(PROTOCOL), cases or copy.deepcopy(CASES),
                                      copy.deepcopy(CONTRACT), texts, baseline)}


def mutate(edit, source=PROTOCOL) -> dict:
    copied = copy.deepcopy(source)
    edit(copied)
    return copied


def small_protocol() -> dict:
    """The committed protocol with 1 job x 2 attempts, so decider controls stay fast."""
    return mutate(lambda p: p["runPlan"].update(jobsPerEnvironment=1, attemptsPerJob=2))


def passing_records(protocol, execution="E1") -> list[dict]:
    expected = {a["name"]: dp.sha256_file(ROOT / a["committedPath"]) if "committedPath" in a else dp.sha256_bytes(a["expected"].encode())
                for a in CASES["artifacts"]}
    profiles, records = dp.profile_by_id(protocol), []
    for env in protocol["environments"]:
        for attempt in range(1, protocol["runPlan"]["attemptsPerJob"] + 1):
            results = []
            for pid in env["profiles"]:
                p = profiles[pid]
                results.append({"profile": pid, "status": "completed", "exitCode": 0, "seconds": 1.0, "calorCacheExisted": False,
                                "invocation": "0|Completed", "unregistered": [],
                                "tests": {n: ",".join(["Passed"] * m) for n, m in dp.selection(p, CASES).items()},
                                "cells": {c["id"]: dp.cell_digest(c) + "|0" for c in CASES["cells"]["ids"]} if p["oracle"] else None,
                                "artifacts": {a["name"]: expected[a["name"]] for a in dp.profile_artifacts(p, CASES)}})
            records.append({"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"], "protocolSha256": dp.sha256_file(ROOT / dp.PROTOCOL),
                            "harnessSha256": dp.sha256_file(ROOT / dp.HARNESS), "mode": "execution", "executionId": execution,
                            "environment": env["id"], "job": 1, "attempt": attempt, "runId": RUN, "runAttempt": "1", "commit": COMMIT,
                            "status": "completed", "environmentCheck": {"violations": [], "observed": {}}, "profiles": results})
    return records


def find(records, env, attempt=1) -> dict:
    return next(r for r in records if r["environment"] == env and r["attempt"] == attempt)


def prof(record, pid) -> dict:
    return next(p for p in record["profiles"] if p["profile"] == pid)


class RegistrationTests(unittest.TestCase):
    def test_committed_protocol_is_valid(self) -> None:
        self.assertEqual([], dp.validate(ROOT))

    def test_unchanged_packet_is_valid_against_itself_as_baseline(self) -> None:
        self.assertEqual(set(), codes(baseline=(PROTOCOL, CASES, HASHES)))

    def test_case_registry_shape(self) -> None:
        self.assertEqual((411, 554, 1170), (CASES["groups"]["verification"]["count"], CASES["groups"]["compiler-verifier"]["count"],
                                            CASES["cells"]["count"]))
        names = {t["name"] for t in CASES["groups"]["verification"]["tests"]}
        self.assertTrue({ORACLE, STRING_ROW} <= names)

    def test_worst_case_fits_the_accepted_ceiling(self) -> None:
        self.assertEqual((555, 115, 1895), dp.worst_case(PROTOCOL))


class NegativeRegistrationControls(unittest.TestCase):
    def only(self, code: str, **kwargs) -> None:
        self.assertEqual({code}, codes(**kwargs))

    def test_d001_inspected_before_freeze(self) -> None:
        self.only("D001", protocol=mutate(lambda p: p["freeze"].update(decisionBearingExecutionBeforeFreeze=True)))

    def test_d002_unrecorded_contract_version(self) -> None:
        self.only("D002", protocol=mutate(lambda p: p["contract"].update(contractVersion="1.0.5")))

    def test_d003_contract_row_missing_remapped_or_off_platform(self) -> None:
        self.only("D003", protocol=mutate(lambda p: p["determinismRows"].pop(0)))
        self.only("D003", protocol=mutate(lambda p: p["determinismRows"][0].update(environments=["linux-x64"])))
        self.only("D003", protocol=mutate(lambda p: p["determinismRows"][0].update(cases=[f"test:verification:{ORACLE}"])))

    def test_d004_floating_label_or_missing_platform(self) -> None:
        self.only("D004", protocol=mutate(lambda p: p["environments"][3].update(runner="windows-latest")))
        self.only("D004", protocol=mutate(lambda p: p["environments"][3].pop("memoryGiB")))
        # Named co-fires: the rows that name win-arm64 (D003) and the recomputed budget (D008).
        self.assertEqual({"D004", "D003", "D008"}, codes(protocol=mutate(lambda p: p["environments"].pop(4))))

    def test_d005_floating_sdk(self) -> None:
        self.only("D005", protocol=mutate(lambda p: p["toolchain"].update(sdk="10.0.x")))

    def test_d006_seed_or_timeout_drift(self) -> None:
        self.only("D006", texts={"tests/Shared/Z3TestSeeding.cs": "public const string RandomSeed = \"7\";"})
        self.only("D006", texts={"src/Calor.Compiler/Verification/VerificationOptions.cs": "DefaultTimeoutMs = 10000;"})

    def test_d007_fewer_attempts_retry_reexecution_or_lower_rate(self) -> None:
        for edit in (lambda p: p["runPlan"].update(attemptsPerJob=10), lambda p: p["retryPolicy"].update(retriesPerAttempt=1),
                     lambda p: p["retryPolicy"].update(reexecuteCommit=True), lambda p: p["agreement"].update(requiredAgreementRate=0.99),
                     lambda p: p["runPlan"].update(earlyStop=True)):
            self.only("D007", protocol=mutate(edit))

    def test_d008_budget_over_ceiling(self) -> None:
        self.only("D008", protocol=mutate(lambda p: p["budget"].update(maxExecutions=4)))

    def test_d009_line_ending_normalization(self) -> None:
        self.only("D009", protocol=mutate(lambda p: p["agreement"].update(normalizationRules=["CRLF -> LF"])))
        self.only("D009", protocol=mutate(lambda p: p["agreement"]["projections"][0]["dropped"].append("outcome")))

    def test_d010_registry_drift(self) -> None:
        self.only("D010", cases=mutate(lambda c: c["cells"]["ids"].pop(), CASES))
        self.only("D010", cases=mutate(lambda c: c["groups"]["verification"]["tests"][0].update(multiplicity=5), CASES))
        # Named co-fire: emptying the guard test also removes its DeterminismRecord call site (D014).
        self.assertEqual({"D010", "D014"}, codes(texts={"tests/Calor.Verification.Tests/ContractTranslatorSemanticsVersionGuardTests.cs": "x"}))

    def test_d011_timing_sensitive_set_must_stay_empty(self) -> None:
        entry = {"case": f"test:verification:{ORACLE}", "reason": "flaky", "amendment": "1.0.0", "contractAmendment": "1.1.1"}
        self.only("D011", protocol=mutate(lambda p: p["cases"]["timingSensitiveSet"].append(entry)))

    def test_d012_vocabulary_changed(self) -> None:
        self.only("D012", protocol=mutate(lambda p: p["attemptStatuses"].pop("timeout")))

    def test_d013_unregistered_machinery(self) -> None:
        self.only("D013", protocol=mutate(lambda p: p["workflow"].update(status="registered")))

    def test_d014_gate_site_drift(self) -> None:
        test_yml = (ROOT / ".github/workflows/test.yml").read_text(encoding="utf-8")
        self.only("D014", texts={".github/workflows/test.yml": test_yml.replace(
            "VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle\"", "VerifierRuntimeDifferentialTests\"")})
        self.only("D014", texts={".github/workflows/publish-nuget.yml": ""})
        self.only("D014", texts={".github/workflows/test.yml": test_yml.replace("git show origin/main:scripts", "git show HEAD:scripts")})
        gate = "tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs"
        self.only("D014", texts={gate: (ROOT / gate).read_text(encoding="utf-8").replace("DeterminismRecord.WriteCells(results);", "")})

    def test_d014_structural_bypasses(self) -> None:
        """The two bypasses open after round 3, plus continue-on-error, || true, and a reorder."""
        test_yml = (ROOT / ".github/workflows/test.yml").read_text(encoding="utf-8")
        main = '          python3 "$RUNNER_TEMP/determinism_protocol_main.py" validate --root . --baseline-ref origin/main\n'
        oracle_step = "      - name: Run verifier-runtime differential gate\n"
        own_step = "      - name: Validate the determinism protocol and its controls (#1421)\n"
        main_step = "      - name: Validate the determinism protocol with main's validator (#1421)\n"
        main_block = test_yml[test_yml.index(main_step):test_yml.index(own_step)]
        publish = (ROOT / ".github/workflows/publish-nuget.yml").read_text(encoding="utf-8")
        for path, mutated in (
            (".github/workflows/test.yml", test_yml.replace(main, "          if false; then\n  " + main + "          fi\n")),
            (".github/workflows/publish-nuget.yml", publish.replace(
                "      - name: Run project tests\n", "      - name: Run project tests\n        if: ${{ 1 == 2 }}\n", 1)),
            (".github/workflows/test.yml", test_yml.replace(oracle_step, oracle_step + "        if: ${{ 1 == 2 }}\n")),
            (".github/workflows/test.yml", test_yml.replace(main_step, main_step + "        continue-on-error: true\n")),
            (".github/workflows/test.yml", test_yml.replace(main, main.rstrip("\n") + " || true\n")),
            (".github/workflows/test.yml", test_yml.replace(main_block, "").replace(  # reorder: main's validator after this tree's
                "      # #881 pinned reproduction", main_block + "      # #881 pinned reproduction", 1)),
            (".github/workflows/publish-nuget.yml", publish.replace(
                "          set -euo pipefail\n          mkdir -p artifacts/test\n", "          set -euo pipefail\n          mkdir -p artifacts/test\n          set +e\n", 1)),
        ):
            self.assertIn(path, (".github/workflows/test.yml", ".github/workflows/publish-nuget.yml"))
            self.assertNotEqual(mutated, (ROOT / path).read_text(encoding="utf-8"))
            self.only("D014", texts={path: mutated})

    def test_harness_never_touches_the_real_home(self) -> None:
        self.assertTrue(dp.home_safe((ROOT / dp.HARNESS).read_text(encoding="utf-8")))
        for bad in ("shutil.rmtree(Path.home() / '.calor')", "os.path.expanduser('~/.calor')", "os.environ.get('HOME')", "os.getenv(\"USERPROFILE\")"):
            self.assertFalse(dp.home_safe(bad), bad)
        self.only("D013", texts={dp.HARNESS: (ROOT / dp.HARNESS).read_text(encoding="utf-8") + "\nshutil.rmtree(Path.home() / '.calor')\n"})
        gate = "tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs"
        self.only("D014", texts={gate: (ROOT / gate).read_text(encoding="utf-8").replace("DeterminismRecord.WriteCells(results);", "")})

    def test_d016_unrecorded_or_weakening_change_against_main(self) -> None:
        changed = dict(HASHES, **{dp.PROTOCOL: "0" * 64})
        self.only("D016", baseline=(PROTOCOL, CASES, changed))
        bigger = mutate(lambda p: p["runPlan"].update(attemptsPerJob=20))
        self.assertIn("D016", codes(baseline=(bigger, CASES, HASHES)))
        more = mutate(lambda c: c["groups"]["verification"]["tests"][0].update(multiplicity=9), CASES)
        self.assertIn("D016", codes(baseline=(PROTOCOL, more, HASHES)))
        renamed = mutate(lambda c: c["cells"]["ids"][0].update(formId="replacement:scalar-type:i8"), CASES)
        self.assertIn("D016", codes(baseline=(PROTOCOL, renamed, HASHES)))
        no_cells = mutate(lambda p: p["profiles"][1].update(oracle=False))
        self.assertIn("D016", codes(protocol=no_cells, baseline=(PROTOCOL, CASES, HASHES)))

    def test_baseline_that_cannot_be_read_fails_closed(self) -> None:
        with self.assertRaises(SystemExit):
            dp.load_baseline(ROOT, "no-such-ref-1421")


class DecisionControls(unittest.TestCase):
    protocol = small_protocol()

    def decide(self, records, runs=None) -> dict:
        return dp.decide(ROOT, records, runs or {"E1": RUN}, COMMIT, "execution", self.protocol, CASES)

    def klass(self, result, key) -> dict:
        return next(r for r in result["cases"] if r["case"] == key)

    def test_all_agreeing_is_deterministic_and_resolves_every_row(self) -> None:
        result = self.decide(passing_records(self.protocol))
        self.assertEqual(("DETERMINISTIC", True), (result["verdict"], result["complete"]))
        self.assertEqual({"RESOLVED"}, {r["status"] for r in result["determinismRows"]})

    def test_windows_only_verdict_is_platform_dependent_disagreement(self) -> None:
        records = passing_records(self.protocol)
        for r in records:
            if r["environment"].startswith("win-"):
                prof(r, "verification-full")["tests"][STRING_ROW] = "Failed"
        result = self.decide(records)
        row = self.klass(result, f"test:verification:{STRING_ROW}")
        self.assertEqual(("NON-DETERMINISTIC", "DISAGREE", True), (result["verdict"], row["class"], row["platformDependent"]))
        self.assertIn("OPEN", {r["status"] for r in result["determinismRows"] if r["id"] == "platform-StringInBodyOnly_StillNeverElides"})

    def test_two_different_failing_translator_outputs_disagree(self) -> None:
        records = passing_records(self.protocol)
        for r in records:
            prof(r, "verification-full")["artifacts"]["translator-fixture"] = ("a" if r["environment"].startswith("win-") else "b") * 64
        self.assertEqual("DISAGREE", self.klass(self.decide(records), "artifact:translator-fixture")["class"])

    def test_single_cell_flip_and_crlf_report_bytes_disagree(self) -> None:
        records = passing_records(self.protocol)
        prof(find(records, "linux-x64", 2), "verification-full")["cells"]["case-000007"] = "0" * 32 + "|1"
        crlf = dp.sha256_bytes((ROOT / CASES["artifacts"][1]["committedPath"]).read_bytes().replace(b"\n", b"\r\n"))
        prof(find(records, "win-x64"), "oracle-isolated")["artifacts"]["verifier-runtime-differential.md"] = crlf
        result = self.decide(records)
        self.assertEqual(("DISAGREE", False), tuple(self.klass(result, "cell:case-000007")[k] for k in ("class", "platformDependent")))
        self.assertEqual("DISAGREE", self.klass(result, "artifact:verifier-runtime-differential.md")["class"])

    def test_timeout_failed_invocation_and_skip_are_never_agreement(self) -> None:
        records = passing_records(self.protocol)
        p = prof(find(records, "osx-arm64"), "oracle-isolated")
        p["status"], p["tests"][ORACLE], p["invocation"] = "timeout", "Timeout", "timeout|Missing"
        find(records, "osx-arm64")["status"] = "timeout"
        prof(find(records, "linux-arm64"), "verification-full").update(invocation="1|Completed", exitCode=1)
        result = self.decide(records)
        self.assertEqual("DISAGREE", self.klass(result, f"test:verification:{ORACLE}")["class"])
        self.assertEqual("DISAGREE", self.klass(result, "invocation:verification-full")["class"])
        records = passing_records(self.protocol)
        for r in records:
            prof(r, "verification-full")["tests"][STRING_ROW] = "Skipped"
        self.assertEqual("FAILING", self.decide(records)["verdict"])

    def test_infrastructure_failure_violation_and_missing_records_are_incomplete(self) -> None:
        records = passing_records(self.protocol)
        find(records, "win-arm64").update(status="infrastructure-failure", profiles=[])
        self.assertEqual("INCOMPLETE", self.decide(records)["verdict"])
        records = passing_records(self.protocol)
        find(records, "win-x64").update(status="environment-violation", environmentCheck={"violations": ["SDK"], "observed": {}})
        self.assertEqual("INCOMPLETE", self.decide(records)["verdict"])
        records = passing_records(self.protocol)
        records.remove(find(records, "linux-arm64", 2))
        self.assertEqual(("INCOMPLETE", 1), (lambda r: (r["verdict"], r["attempts"]["missing"]))(self.decide(records)))

    def test_invalid_attempt_reveals_disagreement_but_never_establishes(self) -> None:
        records = passing_records(self.protocol)
        r = find(records, "linux-x64")
        r["status"], r["profiles"] = "invalid", r["profiles"][:1]
        r["profiles"][0]["tests"][ORACLE] = "Failed"
        self.assertEqual("NON-DETERMINISTIC", self.decide(records)["verdict"])
        records = passing_records(self.protocol)
        r = find(records, "linux-x64")
        r["status"] = prof(r, "verification-full")["status"] = "invalid"
        prof(r, "verification-full").update(exitCode=1, invocation="1|Completed")
        prof(r, "verification-full")["tests"][ORACLE] = "Failed"
        self.assertEqual("NON-DETERMINISTIC", self.decide(records)["verdict"])
        records = passing_records(self.protocol)
        find(records, "linux-x64")["status"] = "invalid"
        result = self.decide(records)
        self.assertEqual(("INCOMPLETE", False), (result["verdict"], result["complete"]))
        self.assertEqual({"OPEN"}, {r["status"] for r in result["determinismRows"] if r["id"] == "identical-tree-CommittedReportsMatchGeneratedOracle"})

    def test_malformed_foreign_or_rerun_records_are_invalid(self) -> None:
        def theory_short(rs):
            prof(find(rs, "linux-x64"), "verification-full")["tests"][THEORY] = "Passed"
        for edit in (
            lambda rs: find(rs, "linux-x64").update(runAttempt="2"),
            lambda rs: find(rs, "linux-x64").update(protocolSha256="0" * 64),
            lambda rs: find(rs, "linux-x64").update(harnessSha256="0" * 64),
            lambda rs: find(rs, "linux-x64").update(commit="d" * 40),
            lambda rs: find(rs, "linux-x64").update(runId="1"),
            lambda rs: find(rs, "linux-x64").update(schemaVersion=999),
            lambda rs: find(rs, "linux-x64").update(environmentCheck={"violations": ["SDK"]}),
            lambda rs: find(rs, "linux-x64").update(environmentCheck=None),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full").update(exitCode=19),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full").update(status="timeout"),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full")["tests"].update({ORACLE: "Missing"}),
            lambda rs: find(rs, "linux-x64")["profiles"].append(copy.deepcopy(find(rs, "linux-x64")["profiles"][0])),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full")["tests"].update({"Extra.Test": "Failed"}),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full")["cells"].update({"case-999999": "0" * 32 + "|1"}),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full")["unregistered"].append("test:New"),
            theory_short,
            lambda rs: rs.append(copy.deepcopy(find(rs, "osx-arm64"))),
        ):
            records = passing_records(self.protocol)
            edit(records)
            self.assertEqual("INVALID", self.decide(records)["verdict"])
        records = passing_records(self.protocol) + passing_records(self.protocol, "E2")
        self.assertEqual("INVALID", self.decide(records, {"E1": RUN, "E2": RUN})["verdict"])
        records = passing_records(self.protocol)
        for r in records:
            r["mode"] = "not-a-registered-mode"
        self.assertEqual("INVALID", dp.decide(ROOT, records, {"E1": RUN}, COMMIT, "not-a-registered-mode", self.protocol, CASES)["verdict"])


class ValueControls(unittest.TestCase):
    def test_values_are_exact_and_flag_duplicates_and_unregistered(self) -> None:
        tmp = Path(tempfile.mkdtemp())
        (tmp / "generated").mkdir()
        committed = ROOT / CASES["artifacts"][1]["committedPath"]
        (tmp / "generated" / "verifier-runtime-differential.md").write_bytes(committed.read_bytes().replace(b"\n", b"\r\n"))
        cell = dict(CASES["cells"]["ids"][0], category="x", solverStatus="proven", runtimeVerdict="completed",
                    guardForced=True, elidedWhenEnabled=False, solverHandled=True, mismatch=False, detail=None)
        (tmp / "cells.json").write_text(json.dumps([cell, dict(cell, mismatch=True), dict(cell, id="case-999999")]), encoding="utf-8")
        oracle = dp.profile_by_id(PROTOCOL)["oracle-isolated"]
        values = dp.profile_values(oracle, CASES, {ORACLE: ["Passed"], "Some.New.Test": ["Passed"]}, "Completed", 0, False, tmp)
        self.assertEqual(("Passed", "0|Completed"), (values["tests"][ORACLE], values["invocation"]))
        self.assertNotEqual(dp.sha256_file(committed), values["artifacts"]["verifier-runtime-differential.md"])
        self.assertEqual(("Missing", "Malformed", "Missing"), (values["artifacts"]["verifier-runtime-differential.json"],
                                                              values["cells"]["case-000001"], values["cells"]["case-000002"]))
        self.assertEqual(3, len(values["unregistered"]))
        bare = {k: cell[k] for k in ("id", "formId", "position", "nestingDepth", "polarity", "mismatch")}
        (tmp / "cells.json").write_text(json.dumps([bare]), encoding="utf-8")
        values = dp.profile_values(oracle, CASES, {ORACLE: ["Passed"]}, "Completed", 0, False, tmp)
        self.assertEqual("Malformed", values["cells"]["case-000001"])

    def test_timeout_keeps_observed_values_and_fills_only_the_rest(self) -> None:
        full = dp.profile_by_id(PROTOCOL)["verification-full"]
        values = dp.profile_values(full, CASES, {ORACLE: ["Failed"]}, "Missing", None, True, None)
        self.assertEqual(("Failed", "Timeout", "timeout|Missing"), (values["tests"][ORACLE], values["tests"][STRING_ROW], values["invocation"]))

    def test_trx_projection_keeps_name_outcome_and_summary(self) -> None:
        trx = Path(tempfile.mkdtemp()) / "t.trx"
        trx.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                       '<UnitTestResult testName="A(x: 1)" outcome="Passed" duration="00:00:01" />'
                       '<UnitTestResult testName="A(x: 1)" outcome="Failed" duration="00:00:09" />'
                       '</Results><ResultSummary outcome="Failed" /></TestRun>', encoding="utf-8")
        self.assertEqual(({"A(x: 1)": ["Passed", "Failed"]}, "Failed"), dp.parse_trx(trx))


if __name__ == "__main__":
    unittest.main()
