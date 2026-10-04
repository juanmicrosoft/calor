#!/usr/bin/env python3
"""0.24 G2 (#1421): positive and negative controls for the verifier determinism protocol.

Each validator control changes one fact and asserts its specific code and no other code (or a
named co-fire). Each decider control builds synthetic attempt records (no test runs) and asserts
the frozen class and verdict, so a rule cannot pass because an unrelated rule fired.
"""
from __future__ import annotations

import argparse
import copy
import json
import os
import sys
import tempfile
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import determinism_protocol as dp  # noqa: E402
import determinism_runner as dr  # noqa: E402

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


def good_observed(env: dict, commit=COMMIT) -> dict:
    """Observations that dp.judge accepts for a registered environment (no runner involved)."""
    root = "D:\\a\\_temp\\dotnet-1421" if env["runnerOs"] == "Windows" else "/home/runner/work/_temp/dotnet-1421"
    sep = "\\" if env["runnerOs"] == "Windows" else "/"
    rid = next(r for r in dp.load(ROOT, dp.Z3_CONSUMERS)["supportedRids"] if r["rid"] == env["rid"])
    pins = {p[1]: p[0] for p in (ln.split() for ln in (ROOT / ".github/z3-binaries-4.15.7.sha256").read_text(encoding="utf-8").splitlines()
                                 if ln.strip() and not ln.startswith("#")) if len(p) == 3}
    return {"sdks": [f"10.0.401 [{root}{sep}sdk]"], "dotnetVersion": "10.0.401", "dotnetPath": f"{root}{sep}dotnet",
            "runtimes": [f"Microsoft.AspNetCore.App 10.0.12 [{root}{sep}shared{sep}Microsoft.AspNetCore.App]",
                         f"Microsoft.NETCore.App 10.0.12 [{root}{sep}shared{sep}Microsoft.NETCore.App]"],
            "dotnetRoots": [root], "runnerOs": env["runnerOs"], "runnerArch": env["runnerArch"], "logicalProcessors": env["logicalProcessors"],
            "memoryBytes": env["memoryGiB"] * 2 ** 30, "imageOs": "image", "imageVersion": "20261001.1", "runnerName": "r",
            "z3": {a: pins[a] for a in (rid["asset"], "Microsoft.Z3.dll")}, "commit": commit, "autocrlf": "false", "dirty": "",
            "runAttempt": "1", "userProfileFollowsIsolatedHome": True}


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
                            "harnessSha256": dp.harness_digest(ROOT), "mode": "execution", "executionId": execution,
                            "environment": env["id"], "job": 1, "attempt": attempt, "runId": RUN, "runAttempt": "1", "commit": COMMIT,
                            "status": "completed", "environmentCheck": {"violations": [], "observed": good_observed(env)}, "profiles": results})
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
        self.only("D013", protocol=mutate(lambda p: p["workflow"].update(amendment="9.9.9")))
        self.only("D013", protocol=mutate(lambda p: p["workflow"].update(status="pending-second-g2-pr")))

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
            (".github/workflows/test.yml", test_yml.replace(main_step, main_step + "        'if': ${{ 1 == 2 }}\n")),
            (".github/workflows/test.yml", test_yml.replace(main_step, main_step + "        \"continue-on-error\": true\n")),
            (".github/workflows/publish-nuget.yml", publish.replace(
                "          set -euo pipefail\n          mkdir -p artifacts/test\n", "          set -euo pipefail\n          exit 0\n          mkdir -p artifacts/test\n", 1)),
            (".github/workflows/publish-nuget.yml", publish.replace(
                "          set -euo pipefail\n          mkdir -p artifacts/test\n", "          set -euo pipefail\n          if false; then\n          mkdir -p artifacts/test\n", 1)
             .replace("              --verbosity normal 2>&1 | tee test-output.log\n          fi\n",
                      "              --verbosity normal 2>&1 | tee test-output.log\n          fi\n          fi\n", 1)),
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
            # amendment 1.1.0: the attestation is re-judged, so an empty violations list cannot hide an observation
            lambda rs: find(rs, "linux-x64")["environmentCheck"]["observed"].update(logicalProcessors=1),
            lambda rs: find(rs, "linux-x64")["environmentCheck"]["observed"].update(commit="d" * 40),
            lambda rs: find(rs, "linux-x64")["environmentCheck"]["observed"].update(userProfileFollowsIsolatedHome=False),
            lambda rs: find(rs, "linux-x64")["environmentCheck"]["observed"].pop("imageVersion"),
            lambda rs: find(rs, "win-x64")["environmentCheck"]["observed"].update(dotnetPath="D:\\a\\_temp\\dotnet-1421-evil\\dotnet"),
            lambda rs: find(rs, "linux-x64")["environmentCheck"].update(observed={}),
            lambda rs: prof(find(rs, "linux-x64"), "verification-full").update(calorCacheExisted=True),
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


# ---------------------------------------------------------------- execution machinery (amendment 1.1.0)

WORKFLOW_TEXT = (ROOT / dp.WORKFLOW).read_text(encoding="utf-8")
OLD, NEW, REPO = "a" * 40, "b" * 40, "owner/calor"
WORKFLOW_REF = f"{REPO}/{dp.WORKFLOW}@refs/heads/main"


def dispatch(**extra) -> dict:
    env = {"GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_SHA": NEW, "GITHUB_RUN_ID": "900",
           "GITHUB_REPOSITORY": REPO, "GITHUB_WORKFLOW_REF": WORKFLOW_REF}
    env.update(extra)
    return env


def past(i, title, sha=OLD, status="completed", conclusion="success", minutes=100, started=True, event="workflow_dispatch") -> dict:
    return {"id": i, "runNumber": i, "title": title, "event": event, "status": status, "conclusion": conclusion, "headSha": sha,
            "minutes": minutes, "attemptsStarted": started}


def this_run(mode="execution", execution_id="E9") -> dict:
    return past(900, f"determinism {mode} {execution_id}", sha=NEW, status="in_progress", minutes=0, started=False)


class PlanGuards(unittest.TestCase):
    """Each guard the plan job applies before any attempt runs, from a synthetic API inventory."""

    def plan(self, inventory=(), mode="execution", execution_id="E9", env=None, paths=("src/Calor.Compiler/X.cs",), ledger=None,
             current=True) -> list[str]:
        runs = list(inventory) + ([this_run(mode, execution_id)] if current else [])
        return dr.plan_problems(PROTOCOL, mode=mode, execution_id=execution_id, env=env or dispatch(), inventory=runs,
                                ledger=ledger or {"entries": []}, changed_paths=lambda old, new: None if paths is None else list(paths))

    def refused(self, needle: str, **kwargs) -> None:
        problems = self.plan(**kwargs)
        self.assertTrue(any(needle in p for p in problems), problems)

    def test_first_execution_is_planned_from_the_protocol(self) -> None:
        self.assertEqual([], self.plan())
        self.assertEqual([], self.plan(mode="control", execution_id="C1"))
        include = dr.matrix(PROTOCOL, "execution")["include"]
        envs = dp.env_by_id(PROTOCOL)
        self.assertEqual(10, len(include))
        for cell in include:
            env = envs[cell["env"]]
            self.assertEqual((env["runner"], env["jobTimeoutMinutes"]), (cell["runner"], cell["timeout"]))
        self.assertEqual({20}, {c["timeout"] for c in dr.matrix(PROTOCOL, "control")["include"]})

    def test_second_execution_of_a_commit_is_refused(self) -> None:
        self.refused("already executed", inventory=[past(1, "determinism execution E1", sha=NEW)])
        self.refused("already executed", inventory=[past(1, "determinism execution E1", sha=NEW, conclusion="failure")])
        self.refused("already executed", inventory=[past(1, "unreadable title", sha=NEW)])  # fail closed: counted as an execution
        self.refused("unfinished", inventory=[past(1, "determinism execution E1", status="in_progress", started=False)])
        self.assertEqual([], self.plan(inventory=[past(1, "determinism control C1", sha=NEW)]))  # a control run is not an execution
        ledgered = {"entries": [{"runId": 1, "mode": "execution", "commit": NEW, "runnerMinutes": 1, "attemptsStarted": True}]}
        self.refused("already executed", inventory=[past(1, "determinism execution E1", sha=NEW, started=False)], ledger=ledgered)
        refused_run = {"entries": [dict(ledgered["entries"][0], attemptsStarted=False)]}  # a ledgered plan refusal is not an execution
        self.assertEqual([], self.plan(inventory=[past(1, "determinism execution E1", sha=NEW, started=False)], ledger=refused_run))

    def test_deleted_or_foreign_runs_are_refused(self) -> None:
        self.refused("missing from the API inventory", ledger={"entries": [{"runId": 7, "mode": "execution", "runnerMinutes": 300}]})
        self.refused("not listed exactly once", current=False)  # a copied workflow's run is not in this workflow's inventory
        self.refused("only .github/workflows/determinism-protocol.yml", env=dispatch(GITHUB_WORKFLOW_REF=f"{REPO}/.github/workflows/copy.yml@x"))
        copy_text = "on:\n  workflow_dispatch:\njobs:\n  x:\n    steps:\n      - run: python3 scripts/determinism_runner.py plan\n"
        original = dp.Path.glob  # list a copied workflow without writing one into the tree
        try:
            dp.Path.glob = lambda self, pattern: list(original(self, pattern)) + (
                [ROOT / ".github/workflows/copy.yml"] if self == ROOT / ".github/workflows" else [])
            self.assertEqual({"D013"}, codes(texts={".github/workflows/copy.yml": copy_text}))
        finally:
            dp.Path.glob = original

    def test_docs_only_repair_is_refused(self) -> None:
        failed = [past(1, "determinism execution E1", conclusion="failure")]
        self.refused("outside docs/", inventory=failed, paths=["docs/plans/evidence/g3-1135/ledger.json", "docs/fix notes.md"])
        self.refused("outside docs/", inventory=failed, paths=None)  # git cannot tell: fail closed
        self.assertEqual([], self.plan(inventory=failed, paths=["docs/x.md", "tests/Calor.Verification.Tests/X.cs"]))
        self.assertEqual([], self.plan(inventory=[past(1, "determinism execution E1")], paths=["docs/x.md"]))

    def test_budget_exceeded_is_refused(self) -> None:
        spent = [past(i, f"determinism control C{i}", minutes=720, started=False) for i in (1, 2)]  # 13 + 1,440 + 555 > 2,000
        self.refused("exceeds the 2000", inventory=spent)
        self.refused("exceeds the 2000", inventory=[past(i, "unreadable", status="in_progress", minutes=0) for i in (1, 2, 3)])
        busy = [past(1, "determinism execution E1", status="in_progress", minutes=1800)]  # measured minutes plus the worst case
        self.refused("exceeds the 2000", inventory=busy, mode="control", execution_id="C1")
        self.refused("missing from the API inventory", ledger={"entries": [{"runId": 3, "runnerMinutes": 1}]})
        bigger = {"entries": [{"runId": 1, "mode": "control", "runnerMinutes": 1500}]}
        self.refused("exceeds the 2000", inventory=[past(1, "determinism control C1", minutes=1)], ledger=bigger)
        executions = [past(i, f"determinism execution E{i}", sha=str(i) * 40, minutes=1) for i in (1, 2, 3)]
        self.refused("limit of 3 execution", inventory=executions)
        cancelled = [past(i, f"determinism control C{i}", minutes=0, started=False, conclusion="cancelled") for i in (1, 2)]
        self.refused("limit of 2 control", inventory=cancelled, mode="control", execution_id="C3")  # every dispatch counts
        refusals = [past(i, f"determinism execution E{i}", minutes=1, started=False, conclusion="failure") for i in (1, 2, 3)]
        self.assertEqual([], self.plan(inventory=refusals))  # refused runs are charged but are not executions
        self.assertEqual([], self.plan(inventory=[past(1, "determinism execution E1", minutes=1900, event="pull_request")]))

    def test_retry_rerun_or_reused_id_is_refused(self) -> None:
        self.refused("GITHUB_RUN_ATTEMPT=2", env=dispatch(GITHUB_RUN_ATTEMPT="2"))
        self.refused("already dispatched", inventory=[past(1, "determinism control E9")])
        for env in (dispatch(GITHUB_RUN_ATTEMPT="2"), {k: v for k, v in dispatch().items() if k != "GITHUB_RUN_ATTEMPT"}):
            with self.assertRaises(dr.Refusal):
                dr.refuse_foreign(env)
        dr.refuse_foreign(dispatch())
        out = Path(tempfile.mkdtemp())
        base = dict(dispatch(GITHUB_RUN_ATTEMPT="2"), NUGET_PACKAGES=str(out / "nuget"), JOB_STARTED=str(time.time()))
        kwargs = dict(env_id="linux-x64", job=1, mode="control", execution_id="C1", out=out, base=base)
        for call in (lambda: dr.run_job(PROTOCOL, CASES, job_timeout=20, **kwargs), lambda: dr.fill_missing(PROTOCOL, **kwargs)):
            with self.assertRaisesRegex(dr.Refusal, "GITHUB_RUN_ATTEMPT"):
                call()
        self.assertEqual([], list(out.iterdir()))

    def test_automatic_trigger_is_rejected(self) -> None:
        self.assertEqual([], dp.workflow_problems(WORKFLOW_TEXT, PROTOCOL))
        self.refused("only a workflow_dispatch", env=dispatch(GITHUB_EVENT_NAME="push"))
        self.refused("only a workflow_dispatch", env=dispatch(GITHUB_EVENT_NAME="pull_request"))
        for trigger in ("  push:\n", "  pull_request:\n", "  schedule:\n    - cron: '0 0 * * *'\n", "  workflow_run:\n"):
            mutated = WORKFLOW_TEXT.replace("on:\n", "on:\n" + trigger, 1)
            self.assertTrue(any("triggers" in p for p in dp.workflow_problems(mutated, PROTOCOL)), trigger)
            self.assertEqual({"D013"}, codes(texts={dp.WORKFLOW: mutated}))
        self.assertTrue(dp.workflow_problems(WORKFLOW_TEXT.replace("on:\n", "on: [push]\n", 1), PROTOCOL))

    def test_normalization_flag_or_masked_status_is_rejected(self) -> None:
        flag = WORKFLOW_TEXT.replace("      execution_id:\n", "      normalize_line_endings:\n        type: boolean\n      execution_id:\n", 1)
        self.assertTrue(any("inputs" in p for p in dp.workflow_problems(flag, PROTOCOL)))
        with self.assertRaises(SystemExit):
            dr.main(["run-job", "--env", "linux-x64", "--out", "x", "--job", "1", "--mode", "execution", "--execution-id", "E1",
                     "--job-timeout", "70", "--normalize-line-endings"])
        self.assertIn("D009", codes(protocol=mutate(lambda p: p["agreement"]["normalizationRules"].append("CRLF to LF"))))
        for old, new in (("fail-fast: false", "fail-fast: true"), ("fail-fast: false", "fail-fast: true # fail-fast: false"),
                         ("    timeout-minutes: 5\n", "    timeout-minutes: 6\n"), ("cancel-in-progress: false", "cancel-in-progress: true"),
                         ('--github-output "$GITHUB_OUTPUT"\n', '--github-output "$GITHUB_OUTPUT" || echo ignored\n'),
                         ("scripts/determinism_runner.py run-job", "scripts/determinism_runner.py fill-missing"),
                         ("    needs: plan\n", ""), ("      - name: Build the registered test hosts\n",
                                                     "      - name: Build the registered test hosts\n        continue-on-error: true\n"),
                         ("      - name: Run every attempt of this job\n", "      - name: Run every attempt of this job\n        if: always()\n"),
                         ("  actions: read\n", "  actions: write\n"), ("    strategy:\n", "    strategy: &s\n"),
                         ('          "$PY" scripts/determinism_runner.py decide', '          ! "$PY" scripts/determinism_runner.py decide'),
                         ('          "$PY" scripts/determinism_runner.py run-job', '          exit 0\n          "$PY" scripts/determinism_runner.py run-job'),
                         ('            dotnet build "$project" -c Release\n', '            until dotnet build "$project" -c Release; do :; done\n')):
            self.assertIn(old, WORKFLOW_TEXT)
            self.assertTrue(dp.workflow_problems(WORKFLOW_TEXT.replace(old, new, 1), PROTOCOL), new)

    def test_missing_isolated_home_is_refused(self) -> None:
        tmp = Path(tempfile.mkdtemp()).resolve()
        inv = tmp / "inv"
        base = {"PATH": "/usr/bin", "HOME": "/real-home-never-used", "CALOR_UPDATE_BASELINES": "1", "calor_update_x": "1"}
        child = dr.invocation_env(base, inv, "linux-x64", str(tmp / "nuget"))
        (inv / "home").mkdir(parents=True)
        dr.check_isolation(child, inv)
        self.assertEqual({str(inv / "home")}, {child[k] for k in dr.ISOLATED})
        self.assertFalse([k for k in child if k.upper().startswith("CALOR_UPDATE_")])
        self.assertEqual(str(inv / "record"), child["CALOR_DETERMINISM_RECORD_DIR"])
        broken = [{k: v for k, v in child.items() if k != "USERPROFILE"}, dict(child, HOME="/real-home-never-used"),
                  dict(child, DOTNET_CLI_HOME="home"), dict(child, CALOR_UPDATE_BASELINES="1"), dict(child, NUGET_PACKAGES="pkgs"),
                  dict(child, USERPROFILE=str(tmp / "elsewhere" / "home"))]
        for env in broken:
            with self.assertRaises(dr.Refusal):
                dr.check_isolation(env, inv)
        (inv / "home" / ".calor").mkdir()
        with self.assertRaises(dr.Refusal):
            dr.check_isolation(child, inv)  # a home that is not empty is not fresh
        runner = (ROOT / dp.RUNNER).read_text(encoding="utf-8")
        self.assertTrue(dp.home_safe(runner))
        self.assertEqual({"D013"}, codes(texts={dp.RUNNER: runner + "\nshutil.rmtree(Path.home() / '.calor')\n"}))
        self.assertIn('test "$GITHUB_RUN_ATTEMPT" = 1\n', WORKFLOW_TEXT)
        for old in ('          mkdir -p "$home"\n', "          printf 'APPDATA=%s"):  # removed from every job at once
            self.assertTrue(dp.workflow_problems("\n".join(ln for ln in WORKFLOW_TEXT.splitlines() if not ln.startswith(old.rstrip("\n"))), PROTOCOL))
        for k in dr.UNDER_HOME:
            with self.assertRaises(dr.Refusal):
                dr.check_isolation(dict(child, **{k: "/real-home-never-used/AppData"}), inv)

    def test_environment_check_rejects_any_other_toolchain_or_runner(self) -> None:
        env = dp.env_by_id(PROTOCOL)["linux-x64"]
        good = good_observed(env, NEW)
        root = good["dotnetRoots"][0]
        self.assertEqual([], dp.judge(PROTOCOL, env, good, NEW))
        for change in ({"sdks": good["sdks"] + ["10.0.100 [/usr/share/dotnet/sdk]"]},
                       {"runtimes": good["runtimes"] + [f"Microsoft.NETCore.App 10.0.0 [{root}/shared/Microsoft.NETCore.App]"]},
                       {"dotnetPath": "/usr/bin/dotnet"}, {"dotnetPath": f"{root}-evil/dotnet"}, {"sdks": [f"10.0.401 [{root}-evil/sdk]"]},
                       {"runnerArch": "ARM64"}, {"logicalProcessors": 2}, {"memoryBytes": 14 * 2 ** 30},
                       {"z3": dict(good["z3"], **{"Microsoft.Z3.dll": "0" * 64})}, {"commit": OLD}, {"autocrlf": "true"},
                       {"dirty": " M src/x.cs"}, {"dirty": "<git status exit 128>"}, {"userProfileFollowsIsolatedHome": False},
                       {"imageOs": None}, {"runAttempt": "2"}, {"sdks": ["garbage"]}):
            self.assertTrue(dp.judge(PROTOCOL, env, dict(good, **change), NEW), change)
        windows = dp.env_by_id(PROTOCOL)["win-x64"]
        self.assertEqual([], dp.judge(PROTOCOL, windows, good_observed(windows, NEW), NEW))
        upper = dict(good_observed(windows, NEW), dotnetPath="d:\\A\\_TEMP\\DOTNET-1421\\dotnet.exe")
        self.assertEqual([], dp.judge(PROTOCOL, windows, upper, NEW))  # Windows paths compare case-insensitively

    def test_inventory_and_ledger_come_from_the_api(self) -> None:
        jobs = [{"id": 1, "started_at": "2026-10-03T10:00:00Z", "completed_at": "2026-10-03T10:00:01Z"},
                {"id": 2, "started_at": "2026-10-03T10:00:00Z", "completed_at": "2026-10-03T10:02:00Z"}, {"id": 3, "started_at": None}]
        self.assertEqual(3, dr.job_minutes(jobs, True))
        for bad in ({"id": 4, "started_at": "2026-10-03T10:05:00Z", "completed_at": "2026-10-03T10:04:00Z"},
                    {"id": 5, "started_at": "2026-10-03T10:05:00Z", "completed_at": None}):
            with self.assertRaises(dr.Refusal):
                dr.job_minutes(jobs + [bad], True)
        self.assertEqual(3, dr.job_minutes(jobs + [{"id": 5, "started_at": "2026-10-03T10:05:00Z"}], False))
        run = {"id": 11, "run_number": 1, "display_title": "determinism control C1", "event": "workflow_dispatch", "status": "completed",
               "conclusion": "success", "head_sha": OLD}
        pages = {"runs": {"total_count": 1, "workflow_runs": [run]},
                 "jobs": {"total_count": 1, "jobs": [{"id": 1, "name": "attempts (linux-x64 job 1)", "conclusion": "success",
                                                      "started_at": "2026-10-03T10:00:00Z", "completed_at": "2026-10-03T10:10:30Z"}]}}
        get = lambda path, token: pages["jobs" if "/jobs" in path else "runs"]  # noqa: E731
        self.assertEqual([{"id": 11, "runNumber": 1, "title": "determinism control C1", "event": "workflow_dispatch",
                           "status": "completed", "conclusion": "success", "headSha": OLD, "minutes": 11, "attemptsStarted": True}],
                         dr.fetch_inventory(REPO, "t", get))
        for broken in ({"workflow_runs": [run]}, {"total_count": 2, "workflow_runs": [run]}, {"total_count": 2, "workflow_runs": [run, run]}):
            with self.assertRaises(dr.Refusal):
                dr.paged("/runs", "workflow_runs", "t", lambda path, token, b=broken: b)
        inventory = [past(1, "determinism control C1", minutes=13), past(2, "determinism execution E1", minutes=300)]
        one, two = {"runId": 1, "mode": "control", "commit": OLD, "runnerMinutes": 13}, {"runId": "2", "mode": "execution", "commit": OLD, "runnerMinutes": 300}
        self.assertEqual([], dr.ledger_problems({"entries": [one, two]}, inventory))
        self.assertEqual(2, len(dr.ledger_problems({"entries": [dict(one, runnerMinutes=12)]}, inventory)))
        self.assertEqual(1, len(dr.ledger_problems({"entries": [one, two, dict(one, runId=9)]}, inventory)))
        for wrong in (dict(two, commit=NEW), dict(two, mode="control")):
            self.assertEqual(1, len(dr.ledger_problems({"entries": [one, wrong]}, inventory)))
            self.refused("another commit or mode", inventory=inventory, ledger={"entries": [wrong]})
        with self.assertRaises(dr.Refusal):
            dr.job_minutes([{"id": 6, "started_at": None, "completed_at": "2026-10-03T10:04:00Z", "conclusion": "success"}], True)


FAKE_DOTNET = """#!/bin/sh
# Fake dotnet for the machinery controls: answers the environment check, prints the probe's home,
# and for 'test' logs its environment and writes a TRX unless FAKE_FAIL is set.
case "$1" in
  --list-sdks) echo "10.0.401 [$FAKE_ROOT/sdk]"; exit 0;;
  --list-runtimes) echo "Microsoft.NETCore.App 10.0.12 [$FAKE_ROOT/shared/Microsoft.NETCore.App]"; exit 0;;
  --version) echo "10.0.401"; exit 0;;
  build) echo build >> "$FAKE_LOG.probe"; [ "$FAKE_PROBE" != buildfail ] && [ "$4" = Release ] && [ -n "$APPDATA" ] && exit 0; exit 1;;
  *.dll) echo run >> "$FAKE_LOG.probe"
         case "$FAKE_PROBE" in runfail) echo "$USERPROFILE"; exit 1;; empty) exit 0;; foreign) echo /real-home; exit 0;; esac
         echo "$USERPROFILE"; exit 0;;
esac
echo "$HOME|$USERPROFILE|$DOTNET_CLI_HOME|$CALOR_DETERMINISM_RECORD_DIR|${CALOR_UPDATE_X:-unset}|$NUGET_PACKAGES" >> "$FAKE_LOG"
prev=""; dir=""; name=""
for a in "$@"; do
  [ "$prev" = "--results-directory" ] && dir="$a"
  case "$a" in trx\\;LogFileName=*) name="${a#trx;LogFileName=}";; esac
  prev="$a"
done
[ -n "$FAKE_FAIL" ] && exit 3
printf '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="T.A" outcome="Passed" /></Results><ResultSummary outcome="Completed" /></TestRun>' > "$dir/$name"
"""


@unittest.skipIf(os.name == "nt", "the fake dotnet is a POSIX shell script")
class AttemptRunnerControls(unittest.TestCase):
    """env-check and run-job end to end with a fake dotnet: one invocation per profile per attempt, never retried."""

    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp()).resolve()
        (self.tmp / "bin").mkdir()
        fake = self.tmp / "bin" / "dotnet"
        fake.write_text(FAKE_DOTNET, encoding="utf-8")
        fake.chmod(0o755)
        self.log, self.out = self.tmp / "log.txt", self.tmp / "out"
        self.out.mkdir()
        self.head = dr.git("rev-parse", "HEAD").stdout.strip()
        self.base = dict(dispatch(GITHUB_RUN_ID="77", GITHUB_SHA=self.head), PATH=f"{self.tmp / 'bin'}{os.pathsep}{os.environ.get('PATH', '')}",
                         NUGET_PACKAGES=str(self.tmp / "nuget"), FAKE_LOG=str(self.log), FAKE_ROOT=str(self.tmp / "bin"),
                         CALOR_UPDATE_X="1", JOB_STARTED=str(time.time()))

    def run_job(self, violations=(), dirty=lambda: "", protocol=None, mode="control", **extra) -> int:
        observed = good_observed(dp.env_by_id(PROTOCOL)["linux-x64"], self.head)
        (self.out / "env.json").write_text(json.dumps({"violations": list(violations), "observed": observed}), encoding="utf-8")
        return dr.run_job(protocol or PROTOCOL, CASES, env_id="linux-x64", job=1, mode=mode, execution_id="C1", out=self.out,
                          base=dict(self.base, **extra), job_timeout=20, dirty=dirty)

    def records(self) -> list[dict]:
        return [json.loads(p.read_text(encoding="utf-8")) for p in sorted(self.out.glob("attempt-*.json"))]

    def lines(self) -> list[str]:
        return self.log.read_text(encoding="utf-8").splitlines() if self.log.exists() else []

    def problem(self, record):
        shape = ([PROTOCOL["control"]["profile"]], PROTOCOL["control"]["jobsPerEnvironment"], PROTOCOL["control"]["attemptsPerJob"])
        hashes = (dp.sha256_file(ROOT / dp.PROTOCOL), dp.harness_digest(ROOT))
        return dp._record_problem(record, PROTOCOL, CASES, "control", dp.env_by_id(PROTOCOL), shape, self.head, {"C1": "77"}, hashes)

    def test_environment_check_records_its_observations(self) -> None:
        args = argparse.Namespace(env="linux-x64", dotnet_root=str(self.tmp / "bin"), out=str(self.out))
        self.assertEqual(0, dr.cmd_env_check(args, self.base))
        check = json.loads((self.out / "env.json").read_text(encoding="utf-8"))
        observed = check["observed"]
        self.assertEqual((["10.0.401"], True), ([s.split()[0] for s in observed["sdks"]], observed["userProfileFollowsIsolatedHome"]))
        self.assertEqual(len(dp.judge(PROTOCOL, dp.env_by_id(PROTOCOL)["linux-x64"], observed, self.head)), len(check["violations"]))
        self.assertTrue(observed["dotnetPath"].startswith(str(self.tmp / "bin")))
        self.assertEqual([], self.lines())  # the environment check runs no test

    def test_the_home_probe_fails_closed(self) -> None:
        self.assertTrue(dr.home_probe(self.base, self.out / "ok", self.base["NUGET_PACKAGES"]))
        # Amendment 1.2.0: the probe compiles the verifier's own resolver from this tree.
        resolver = ROOT / dr.USER_HOME_SOURCE
        self.assertTrue(resolver.is_file())
        self.assertIn("public static string Resolve()", resolver.read_text(encoding="utf-8"))
        self.assertIn(f'<Compile Include="{resolver}" />', (self.out / "ok" / "probe.csproj").read_text(encoding="utf-8"))
        self.assertIn("UserHome.Resolve()", (self.out / "ok" / "Program.cs").read_text(encoding="utf-8"))
        for mode, runs in (("buildfail", 0), ("runfail", 1), ("empty", 1), ("foreign", 1)):
            Path(f"{self.log}.probe").unlink()
            self.assertFalse(dr.home_probe(dict(self.base, FAKE_PROBE=mode), self.out / mode, self.base["NUGET_PACKAGES"]), mode)
            self.assertEqual(runs, Path(f"{self.log}.probe").read_text(encoding="utf-8").count("run"), mode)  # a failed build never runs

    def test_every_attempt_runs_once_in_its_own_isolated_home(self) -> None:
        self.assertEqual(0, self.run_job())
        records = self.records()
        self.assertEqual([1, 2], [r["attempt"] for r in records])
        self.assertEqual(["completed", "completed"], [r["status"] for r in records])
        self.assertEqual([None, None], [self.problem(r) for r in records])
        homes = [line.split("|") for line in self.lines()]
        self.assertEqual(2, len(homes))
        self.assertEqual(2, len({h[0] for h in homes}))
        for home, profile, cli, record, update, nuget in homes:
            self.assertTrue(home == profile == cli and home.startswith(str(self.out)), home)
            self.assertTrue(Path(record).is_absolute() and update == "unset" and nuget == self.base["NUGET_PACKAGES"])
        before = [p.read_bytes() for p in sorted(self.out.glob("attempt-*.json"))]
        with self.assertRaisesRegex(dr.Refusal, "no attempt is run twice"):
            self.run_job()
        dr.fill_missing(PROTOCOL, env_id="linux-x64", job=1, mode="control", execution_id="C1", out=self.out, base=self.base)
        self.assertEqual(before, [p.read_bytes() for p in sorted(self.out.glob("attempt-*.json"))])  # never replaced
        self.assertEqual(2, len(self.lines()))

    def test_a_record_is_written_after_every_invocation(self) -> None:
        two = mutate(lambda p: (p.update(profiles=[dict(p["control"]["profile"], id="p1"), dict(p["control"]["profile"], id="p2")]),
                                p["runPlan"].update(jobsPerEnvironment=1, attemptsPerJob=1),
                                p["environments"][0].update(profiles=["p1", "p2"])))
        seen = []

        def dirty() -> str:
            path = dr.record_path(self.out.resolve(), "linux-x64", 1, 1)
            seen.append(json.loads(path.read_text(encoding="utf-8")) if path.exists() else None)
            return ""
        self.assertEqual(0, self.run_job(protocol=two, mode="execution", dirty=dirty))
        self.assertEqual(None, seen[1])  # after the first invocation, before its record
        self.assertEqual((["p1"], "invalid", "attempt in progress"), ([p["profile"] for p in seen[2]["profiles"]], seen[2]["status"], seen[2]["reason"]))
        self.assertEqual(["p1", "p2"], [p["profile"] for p in self.records()[0]["profiles"]])

    def test_a_failing_invocation_is_recorded_and_never_retried(self) -> None:
        self.assertEqual(0, self.run_job(FAKE_FAIL="1"))
        records = self.records()
        self.assertEqual(["invalid", "invalid"], [r["status"] for r in records])  # control: no TRX and no registered names to fill
        self.assertEqual(["3|Missing", "3|Missing"], [r["profiles"][0]["invocation"] for r in records])
        self.assertEqual([None, None], [self.problem(r) for r in records])
        self.assertEqual(2, len(self.lines()))

    def test_a_modified_tree_invalidates_this_and_every_later_attempt(self) -> None:
        calls = []
        self.assertEqual(1, self.run_job(dirty=lambda: (calls.append(1), "" if len(calls) == 1 else " M src/x.cs")[1]))
        records = self.records()
        self.assertEqual(["invalid", "invalid"], [r["status"] for r in records])
        self.assertEqual((1, 0), (len(records[0]["profiles"]), len(records[1]["profiles"])))
        self.assertEqual([None, None], [self.problem(r) for r in records])
        self.assertEqual(1, len(self.lines()))

    def test_a_violation_or_a_build_that_modifies_the_tree_runs_nothing(self) -> None:
        self.assertEqual(1, self.run_job(violations=["2 logical processors"]))
        self.assertEqual(["environment-violation"] * 2, [r["status"] for r in self.records()])
        self.setUp()
        self.assertEqual(1, self.run_job(dirty=lambda: " M src/x.cs"))
        self.assertEqual(["environment-violation"] * 2, [r["status"] for r in self.records()])
        self.assertEqual([None, None], [self.problem(r) for r in self.records()])
        self.assertEqual([], self.lines())

    def test_the_job_deadline_stops_starting_invocations(self) -> None:
        self.assertEqual(1, self.run_job(JOB_STARTED=str(time.time() - 16 * 60)))
        self.assertEqual(["invalid", "invalid"], [r["status"] for r in self.records()])
        self.assertEqual([], self.lines())

    def test_fill_missing_records_unrun_attempts_without_running_them(self) -> None:
        dr.fill_missing(PROTOCOL, env_id="linux-x64", job=1, mode="control", execution_id="C1", out=self.out, base=self.base)
        self.assertEqual(["infrastructure-failure"] * 2, [r["status"] for r in self.records()])
        self.assertEqual([None, None], [self.problem(r) for r in self.records()])
        for p in self.out.glob("attempt-*.json"):
            p.unlink()
        (self.out / "env.json").write_text(json.dumps({"violations": ["SDK"], "observed": {}}), encoding="utf-8")
        dr.fill_missing(PROTOCOL, env_id="linux-x64", job=1, mode="control", execution_id="C1", out=self.out, base=self.base)
        self.assertEqual([None, None], [self.problem(r) for r in self.records()])
        self.assertEqual([], self.lines())

if __name__ == "__main__":
    unittest.main()
