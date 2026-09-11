"""Read-only progress on private SYNTHETIC financial histories, never model outcomes."""
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

from test_ppw_gateway_disposition import DispositionFixture, budget, digest, disposition, load


progress = load("ppw_progress_test", "ppw-gateway-progress.py")


class ProgressTests(DispositionFixture):
    def test_unresolved_old_attempt_is_not_reported_as_untouched_or_complete(self):
        before = digest(self.ledger_path)
        report = progress.read_progress(self.ledger_path)
        self.assertEqual(before, digest(self.ledger_path))
        self.assertEqual(1, report["accountedSlots"])
        self.assertEqual(443, report["remainingNonterminalSlots"])
        self.assertEqual(1, report["requestBearingNonterminalSlots"])
        self.assertEqual(2, report["liveUnknownRequestCount"])
        self.assertEqual(51_040_000, report["retainedUnreconciledExposureMicroUsd"])
        self.assertFalse(report["accountingComplete"])
        self.assertNotIn("untouchedSlots", report)

    def test_disposition_is_distinct_from_success_and_permanently_retains_unknowns(self):
        self.apply()
        before = digest(self.ledger_path)
        report = progress.read_progress(self.ledger_path)
        self.assertEqual(before, digest(self.ledger_path))
        self.assertEqual(2, report["accountedSlots"])
        self.assertEqual(442, report["remainingNonterminalSlots"])
        self.assertEqual(0, report["validTerminalSlots"])
        self.assertEqual(1, report["ordinaryInvalidTerminalSlots"])
        self.assertEqual(1, report["historicalRawInvalidDispositionSlots"])
        self.assertEqual(2, report["unknownRequestCount"])
        self.assertEqual(0, report["liveUnknownRequestCount"])
        self.assertEqual(51_040_000, report["permanentUnknownMicroUsd"])
        self.assertIsNone(report["historicalActualCost"])
        self.assertFalse(report["stage2Authorized"])
        self.assertNotIn("owner", json.dumps(report))

    def test_halt_cannot_be_hidden_by_a_collecting_scope_state(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        ledger.stop(owner, "INCOMPLETE_POLICY")
        with self.connect() as db:
            db.execute("UPDATE scope SET state='collecting'")
        before = digest(self.ledger_path)
        with self.assertRaisesRegex(ValueError, "halting scope state"):
            progress.read_progress(self.ledger_path)
        self.assertEqual(before, digest(self.ledger_path))

    def test_start_requires_the_exact_supplied_reviewed_proof(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        with self.assertRaisesRegex(ValueError, "registered disposition proof"):
            ledger.start(expected_disposition_proof={})
        with self.connect() as db:
            self.assertEqual("INCOMPLETE_POLICY", db.execute("SELECT state FROM scope").fetchone()[0])
            self.assertEqual(0, db.execute("SELECT COUNT(*) FROM events WHERE id>10 AND kind='started'").fetchone()[0])

    def test_active_owner_rejects_rehashed_opaque_proof_changes(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        with self.connect() as db:
            detail = json.loads(db.execute("SELECT detail FROM events WHERE id=10").fetchone()[0])
            proof = detail["proof"]
            proof["quiescence"]["criteriaSha256"] = "7" * 64
            proof.pop("proofSha256")
            proof["proofSha256"] = disposition._sha256_json(proof)
            db.execute("UPDATE events SET detail=? WHERE id=10", (json.dumps(detail),))
        request = json.loads(self.future_row()["request"])
        with self.assertRaisesRegex(ValueError, "active owner"):
            ledger.reserve(owner, self.slots[2], request)
        with self.connect() as db:
            self.assertEqual(2, db.execute("SELECT COUNT(*) FROM requests").fetchone()[0])
            self.assertEqual("INCOMPLETE_POLICY", db.execute("SELECT state FROM scope").fetchone()[0])

    def test_inflight_unknown_preserves_an_existing_budget_halt(self):
        self.apply()
        ledger = budget.RequestLedger(self.ledger_path)
        owner = ledger.start()
        request = json.loads(self.future_row()["request"])
        reserved = [ledger.reserve(owner, self.slots[2], request) for _ in range(43)]
        with self.assertRaisesRegex(ValueError, "INCOMPLETE_BUDGET"):
            ledger.reserve(owner, self.slots[2], request)
        ledger.settle(owner, reserved[0], None, None)
        report = progress.read_progress(self.ledger_path)
        self.assertEqual("INCOMPLETE_BUDGET", report["state"])
        self.assertEqual(1, report["liveUnknownRequestCount"])
        self.assertEqual(42, report["inflightReservedRequestCount"])
        self.assertFalse(report["accountingComplete"])
        self.assertEqual(51_040_000, report["permanentUnknownMicroUsd"])


class QuiescenceTests(unittest.TestCase):
    def probe(self, descriptors, stderr=b""):
        def run(argv, **kwargs):
            if argv[0] == "/bin/ps":
                self.assertIn("-axww", argv)
                return subprocess.CompletedProcess(argv, 0, "1 0 1 SYNTHETIC-idle\n", "")
            return subprocess.CompletedProcess(argv, 0, descriptors, stderr)
        with patch.object(disposition.subprocess, "run", side_effect=run):
            return disposition._quiescence_probe(
                [Path("/SYNTHETIC/historical-work")], ["/SYNTHETIC/pinned-native"],
                inspect_descriptors=True)

    def test_descriptor_metadata_is_separate_and_does_not_publish_paths(self):
        evidence = self.probe(b"p1\0fcwd\0n/SYNTHETIC/unrelated\0ftxt\0n/bin/sleep\0\n")
        self.assertEqual([], evidence["descriptorInspection"]["matchedProcesses"])
        self.assertNotIn("unrelated", json.dumps(evidence))

    def test_hidden_workspace_native_unavailable_coverage_and_warnings_refuse(self):
        for data, warning in (
                (b"p1\0fcwd\0n/SYNTHETIC/historical-work/src\0", b""),
                (b"p1\0fcwd\0n/\0ftxt\0n/SYNTHETIC/pinned-native\0", b""),
                (b"p1\0ftxt\0n/bin/sleep\0", b""),
                (b"p1\0fcwd\0n/\0", b"incomplete enumeration"),
                (b"p1\0fcwd\0n(unavailable)\0", b""),
                (b"p1\0fcwd\0n/\xff\0", b"")):
            with self.subTest(data=data), self.assertRaises(ValueError):
                self.probe(data, warning)

    def test_only_a_preexisting_native_outside_protected_workspaces_can_be_excluded(self):
        for birth, allowed in (
                ("Wed Sep  9 16:23:55 2026", True),
                ("Sat Sep 12 16:23:55 2026", False),
                ("unavailable", False)):
            def run(argv, **kwargs):
                if argv[0] == "/bin/ps":
                    return subprocess.CompletedProcess(argv, 0, birth + "\n", "")
                return subprocess.CompletedProcess(
                    argv, 0, b"p1\0fcwd\0n/SYNTHETIC/unrelated\0ftxt\0n/SYNTHETIC/native\0", b"")
            with self.subTest(birth=birth), patch.object(
                    disposition.subprocess, "run", side_effect=run):
                if allowed:
                    value = disposition._descriptor_quiescence(
                        ["/SYNTHETIC/work", "/SYNTHETIC/native"],
                        "2026-09-11T01:00:14.806Z", "/SYNTHETIC/native")
                    self.assertEqual([], value["matchedProcesses"])
                else:
                    with self.assertRaises(ValueError):
                        disposition._descriptor_quiescence(
                            ["/SYNTHETIC/work", "/SYNTHETIC/native"],
                            "2026-09-11T01:00:14.806Z", "/SYNTHETIC/native")


if __name__ == "__main__":
    unittest.main()
