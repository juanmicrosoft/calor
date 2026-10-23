#!/usr/bin/env python3
"""Self-tests for scripts/soundness_sweep_gate.py (#1589). Pure Python; no build or sweep run.

Each test writes a synthetic sweep output (the files `run` and `report` produce) and checks that
the gate passes or fails it for the right reason. The last tests check the real fixture.
"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import soundness_sweep_gate as gate  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[1]
CASES = ["R1-A-001", "R1-A-002", "R1-CTRL-RETRO-845-001", "R1-B-001"]
EXPECTED = {
    "R1-A-001": {"class": "validated-proof", "token": "Proven"},
    "R1-A-002": {"class": "no-claim", "token": "Failed"},
    "R1-CTRL-RETRO-845-001": {"class": "validated-proof", "token": "Proven"},
    "R1-B-001": {"class": "timed-out", "token": "TimeoutOrUnavailable"},
}


def write(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data), encoding="utf-8")


class Sweep:
    """A synthetic repo (case index + fixture) and a clean sweep output for it."""

    def __init__(self, root: Path):
        self.repo, self.out = root / "repo", root / "out"
        write(self.repo / gate.PACKET / "cases-index.json", {"cases": [{"id": c} for c in CASES]})
        self.fixture = {"schemaVersion": 1, "issue": 1589, "cases": dict(EXPECTED)}
        # Extra retained attempts per (slot, case): raw attempt records written next to attempt 1.
        self.extra_attempts: dict[tuple[str, str], list[dict]] = {}
        self.results = {slot: {c: dict(v) for c, v in EXPECTED.items()} for slot in ("B1", "N1")}
        self.results["P845"] = {"R1-CTRL-RETRO-845-001": dict(EXPECTED["R1-CTRL-RETRO-845-001"])}
        self.summary = {b: {"rowStatusCounts": {"CLEAN-WITHIN-BUDGET": 2}, "findings": 0, "runValidity": "pinned"} for b in ("B1", "N1")}
        self.rows = {b: [{"row": "A", "status": "CLEAN-WITHIN-BUDGET", "findingClasses": []}] for b in ("B1", "N1")}
        self.oracle = {"pass": True, "controls": [{"id": "OC-1", "ok": True}]}
        self.controls = {
            "solverAvailability": {s: {"pass": True, "cases": []} for s in ("P845", "B1", "N1")},
            "mutationPairs": {b: [{"pair": "M1", "oracleEqual": True}] for b in ("B1", "N1")},
            "controlMismatches": {"B1": [], "N1": []},
            "retro845": {"B1": {"cases": [{"caseId": "R1-CTRL-RETRO-845-001", "expected": "Proven", "token": "Proven"}]}},
        }
        self.invalid: list[str] = []

    def judge(self, run_exit: int = 0, report_exit: int = 0) -> dict:
        write(self.repo / gate.FIXTURE, self.fixture)
        for slot, cases in self.results.items():
            lines = []
            for c, v in cases.items():
                raws = [{"status": "executed", "class": v["class"], "addedFindings": v.get("addedFindings", [])}, *self.extra_attempts.get((slot, c), [])]
                attempts = []
                for n, raw in enumerate(raws, 1):
                    path = f"attempts/{c}/sweep-attempt-{n}.json"
                    write(self.out / slot / path, raw)
                    attempts.append({"attempt": n, "class": raw.get("class"), "path": path})
                lines.append(json.dumps({"caseId": c, "class": v["class"], "token": v["token"], "addedFindings": v.get("addedFindings", []),
                                         "tokens": v.get("tokens", [v["token"]]), "attempts": attempts}))
            (self.out / slot).mkdir(parents=True, exist_ok=True)
            (self.out / slot / "case-results.jsonl").write_text("".join(line + "\n" for line in lines), encoding="utf-8")
        for b in ("B1", "N1"):
            write(self.out / b / "row-status.json", {"counts": self.summary[b]["rowStatusCounts"], "rows": self.rows[b]})
        write(self.out / "summary.json", self.summary)
        write(self.out / "oracle-controls.json", self.oracle)
        write(self.out / "controls.json", self.controls)
        (self.out / "ledger.jsonl").write_text("{}\n" * 9, encoding="utf-8")
        if self.invalid:
            (self.out / "invalid-run.jsonl").write_text("".join(json.dumps({"reason": r}) + "\n" for r in self.invalid), encoding="utf-8")
        return gate.judge(self.repo, self.out, run_exit, report_exit)


class GateTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.sweep = Sweep(Path(self._tmp.name))

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def assertFailsWith(self, result: dict, fragment: str) -> None:
        self.assertTrue(any(fragment in f for f in result["failures"]), result["failures"])

    def test_clean_run_passes(self) -> None:
        result = self.sweep.judge()
        self.assertEqual([], result["failures"])
        self.assertIn("PASS", gate.markdown(result, None))

    def test_false_proof_fails_even_when_the_fixture_expects_it_not_to(self) -> None:
        self.sweep.results["N1"]["R1-A-002"] = {"class": "false-unconditional-proof", "token": "Proven"}
        self.assertFailsWith(self.sweep.judge(), "N1: R1-A-002 false-unconditional-proof")

    def test_added_finding_fails(self) -> None:
        self.sweep.results["B1"]["R1-A-001"]["addedFindings"] = ["stale-cache-proof"]
        self.assertFailsWith(self.sweep.judge(), "stale-cache-proof")

    def test_false_proof_row_fails(self) -> None:
        self.sweep.rows["B1"][0] = {"row": "A", "status": "FALSE-PROOF", "findingClasses": ["false-unconditional-proof"]}
        self.assertFailsWith(self.sweep.judge(), "row A is FALSE-PROOF")

    def test_changed_verdict_fails(self) -> None:
        self.sweep.results["B1"]["R1-A-001"] = {"class": "no-claim", "token": "Unsupported"}
        self.assertFailsWith(self.sweep.judge(), "verdict changed: B1: R1-A-001")

    def test_retro_845_control_mismatch_fails(self) -> None:
        self.sweep.controls["controlMismatches"]["B1"] = [
            {"caseId": "R1-CTRL-RETRO-845-001", "expected": "Proven", "token": "Unsupported", "class": "no-claim"}]
        self.assertFailsWith(self.sweep.judge(), "control R1-CTRL-RETRO-845-001 expected Proven")

    def test_oracle_availability_and_mutation_controls_fail(self) -> None:
        self.sweep.oracle = {"pass": False, "controls": [{"id": "OC-1", "ok": False}]}
        self.sweep.controls["solverAvailability"]["P845"] = {"pass": False, "cases": [{"caseId": "R1-CTRL-POSITIVE-001", "ok": False}]}
        self.sweep.controls["mutationPairs"]["N1"] = [{"pair": "M2", "oracleEqual": False}]
        result = self.sweep.judge()
        self.assertFailsWith(result, "oracle control mismatch")
        self.assertFailsWith(result, "P845: solver-availability")
        self.assertFailsWith(result, "mutation pair M2")

    def test_invalid_run_fails(self) -> None:
        self.sweep.invalid = ["INVALID RUN: B1 pin missing or changed before the first case"]
        self.sweep.summary["B1"]["runValidity"] = "INVALID: see invalid-run.jsonl"
        result = self.sweep.judge(run_exit=4)
        self.assertFailsWith(result, "sweep `run` exited 4")
        self.assertFailsWith(result, "invalid run: INVALID RUN")
        self.assertFailsWith(result, "run validity")

    def test_missing_report_fails(self) -> None:
        result = self.sweep.judge(report_exit=1)
        self.assertFailsWith(result, "`report` exited 1")
        (self.sweep.out / "summary.json").unlink()
        self.assertFailsWith(gate.judge(self.sweep.repo, self.sweep.out, 0, 0), "summary.json is missing")

    def test_unexecuted_case_fails(self) -> None:
        del self.sweep.results["N1"]["R1-A-002"]
        self.assertFailsWith(self.sweep.judge(), "N1: 1 case(s) not executed")

    def test_new_timeout_fails_and_is_marked_timeout_related(self) -> None:
        self.sweep.results["B1"]["R1-A-001"] = {"class": "timed-out", "token": "TimeoutOrUnavailable"}
        result = self.sweep.judge()
        self.assertFailsWith(result, "verdict changed: B1: R1-A-001")
        self.assertEqual(1, len(result["timeoutRelated"]))
        self.assertIn("involve a timeout", gate.markdown(result, None, "abc"))

    def test_expected_timeout_that_now_completes_or_crashes_fails(self) -> None:
        self.sweep.results["N1"]["R1-B-001"] = {"class": "validated-proof", "token": "Proven"}
        self.sweep.results["B1"]["R1-B-001"] = {"class": "crashed", "token": None}
        result = self.sweep.judge()
        self.assertFailsWith(result, "verdict changed: N1: R1-B-001")
        self.assertFailsWith(result, "verdict changed: B1: R1-B-001 expected timed-out/TimeoutOrUnavailable, got crashed/None")

    def test_finding_in_a_retry_attempt_fails_even_when_combined_as_flaky(self) -> None:
        self.sweep.results["B1"]["R1-A-001"] = {"class": "flaky", "token": "Failed", "tokens": ["TimeoutOrUnavailable", "Failed"]}
        self.sweep.extra_attempts[("B1", "R1-A-001")] = [{"status": "executed", "class": "spurious-refutation", "addedFindings": []}]
        self.assertFailsWith(self.sweep.judge(), "B1: R1-A-001 spurious-refutation (final flaky/Failed)")

    def test_finding_in_a_late_observation_fails(self) -> None:
        self.sweep.extra_attempts[("N1", "R1-A-002")] = [
            {"status": "over-time", "lateObservation": {"status": "executed", "class": "no-claim", "addedFindings": ["required-demotion-absent"]}}]
        self.assertFailsWith(self.sweep.judge(), "N1: R1-A-002 required-demotion-absent")

    def test_missing_attempt_file_fails(self) -> None:
        self.sweep.judge()
        (self.sweep.out / "B1" / "attempts" / "R1-A-001" / "sweep-attempt-1.json").unlink()
        self.assertFailsWith(gate.judge(self.sweep.repo, self.sweep.out, 0, 0), "B1: R1-A-001 attempt file(s) missing")

    def test_freeze_refuses_findings_on_any_slot_and_slot_disagreement(self) -> None:
        self.sweep.results["P845"]["R1-CTRL-RETRO-845-001"] = {"class": "validated-proof", "token": "Failed"}
        self.sweep.results["N1"]["R1-A-002"]["addedFindings"] = ["spurious-model"]
        self.sweep.judge()
        args = type("Args", (), {"repo": str(self.sweep.repo), "out": str(self.sweep.out), "commit": "c", "runner": "r"})()
        before = (self.sweep.repo / gate.FIXTURE).read_text(encoding="utf-8")
        self.assertEqual(1, gate.cmd_freeze(args))
        self.assertEqual(before, (self.sweep.repo / gate.FIXTURE).read_text(encoding="utf-8"))

    def test_freeze_writes_the_fixture_from_a_clean_run(self) -> None:
        self.sweep.judge()
        args = type("Args", (), {"repo": str(self.sweep.repo), "out": str(self.sweep.out), "commit": "c", "runner": "r"})()
        self.assertEqual(0, gate.cmd_freeze(args))
        self.assertEqual(EXPECTED, gate.load_json(self.sweep.repo / gate.FIXTURE)["cases"])

    def test_fixture_cannot_expect_a_finding(self) -> None:
        self.sweep.fixture["cases"]["R1-A-001"] = {"class": "false-unconditional-proof", "token": "Proven"}
        self.assertFailsWith(self.sweep.judge(), "is a finding class")

    def test_fixture_must_cover_the_case_index(self) -> None:
        del self.sweep.fixture["cases"]["R1-B-001"]
        self.sweep.fixture["cases"]["R1-Z-999"] = {"class": "no-claim", "token": None}
        result = self.sweep.judge()
        self.assertFailsWith(result, "have no expected verdict")
        self.assertFailsWith(result, "name no case in cases-index.json")


class RealFixtureTests(unittest.TestCase):
    def test_checked_in_fixture_is_valid(self) -> None:
        self.assertEqual([], gate.fixture_errors(REPO_ROOT, gate.load_json(REPO_ROOT / gate.FIXTURE)))

    def test_workflow_runs_the_gate_and_notifies_with_issues_write_only(self) -> None:
        text = (REPO_ROOT / ".github/workflows/soundness-sweep.yml").read_text(encoding="utf-8")
        self.assertIn("bash scripts/run-soundness-sweep.sh", text)
        # Scheduled and dispatched runs sweep main, whatever ref dispatched them.
        self.assertIn("ref: ${{ github.event_name == 'pull_request' && github.sha || 'refs/heads/main' }}", text)
        self.assertIn("python3 scripts/soundness_sweep_gate.py check-fixture", text)
        notify = text.split("\n  notify:\n", 1)[1]
        self.assertIn("    permissions:\n      issues: write\n\n", notify)
        self.assertIn("GH_TOKEN: ${{ github.token }}", notify)
        self.assertNotIn("secrets.", text)
        for line in text.splitlines():
            if "uses: " in line and "./.github/actions/" not in line:
                self.assertRegex(line, r"@[0-9a-f]{40} # v\d+$", line)


if __name__ == "__main__":
    unittest.main()
