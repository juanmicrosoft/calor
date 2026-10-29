#!/usr/bin/env python3
"""Fixture-based tests of the blocking gate, updater CLI, and publication boundary."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

import roundtrip_baselines as gate


ROOT = Path(__file__).resolve().parents[1]
SOURCE_RUN = "https://github.com/juanmicrosoft/calor/actions/runs/12345"
SOURCE_COMMIT = "a" * 40
UPDATED_AT = "2026-10-29T12:00:00Z"


class BaselineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.path = self.root / "baseline.json"
        # Synthetic bootstrap state must not change when committed floors ratchet.
        fixture = {
            "schema_version": 1,
            "blocking_floors": {
                mode: {
                    project: {
                        "round_trip_passed": 155 if mode == "lossy" else 0,
                        "round_trip_total": 157 if mode == "lossy" else 0,
                        "native_fraction": 0.4 if mode == "lossy" else 0.15625,
                        "coverage_fraction": 0.75 if mode == "lossy" else 0.25,
                    }
                    for project in gate.PROJECTS
                }
                for mode in gate.MODES
            },
            "projects": {
                project: {
                    "round_trip_passed": 155,
                    "round_trip_total": 157,
                    "native_fraction": 0.5,
                    "coverage_fraction": 0.8,
                }
                for project in gate.PROJECTS
            },
        }
        self.path.write_text(json.dumps(fixture), encoding="utf-8")
        self.baseline = gate.load_baseline(self.path)
        self.reports = self.root / "reports"
        for mode in gate.MODES:
            for project in gate.PROJECTS:
                metrics = self.baseline["blocking_floors"][mode][project]
                passed = max(10, metrics["round_trip_passed"])
                total = max(passed, metrics["round_trip_total"])
                report = {
                    "report_schema_version": 2,
                    "project": project,
                    "conversion_mode": mode,
                    "verdict": "pass",
                    "gate_failures": [],
                    "inconclusive": False,
                    "build_succeeded": True,
                    "round_trip": {"passed": passed, "total": total,
                                   "failed": 0, "skipped": total - passed},
                    "fidelity": {"coverage": {key: metrics[key] for key in gate.FRACTIONS}},
                }
                self.write(mode, project, report)

    def report_path(self, mode="lossy", project="MediatR"):
        return self.reports / mode / f"{project}-roundtrip.json"

    def write(self, mode, project, report):
        path = self.report_path(mode, project)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report), encoding="utf-8")

    def mutate(self, change, mode="lossy", project="MediatR"):
        report = json.loads(self.report_path(mode, project).read_text())
        change(report)
        self.write(mode, project, report)

    def update(self, baseline=None):
        return gate.updated_baseline(baseline or self.baseline, self.reports,
                                     SOURCE_RUN, SOURCE_COMMIT, UPDATED_AT)

    def cli(self, command, *args):
        return subprocess.run(
            [sys.executable, str(ROOT / "scripts/roundtrip_baselines.py"), command,
             "--baseline", str(self.path), "--reports", str(self.reports), *args],
            capture_output=True, text=True, check=False)

    def test_exact_floors_pass_both_modes(self):
        for mode in gate.MODES:
            reports, _ = gate.load_reports(self.reports / mode, mode)
            gate.check_floors(self.baseline, reports, mode)

    def test_each_metric_regression_blocks_both_modes(self):
        for mode in gate.MODES:
            reports, _ = gate.load_reports(self.reports / mode, mode)
            for metric in gate.METRICS:
                with self.subTest(mode=mode, metric=metric):
                    baseline = copy.deepcopy(self.baseline)
                    baseline["blocking_floors"][mode]["MediatR"][metric] = (
                        reports["MediatR"][metric] + (1 if metric in gate.COUNTS else 0.000001))
                    with self.assertRaisesRegex(ValueError, metric):
                        gate.check_floors(baseline, reports, mode)

    def test_raises_floors_and_preserves_every_previous_metric(self):
        self.mutate(lambda r: r["fidelity"]["coverage"].update(native_fraction=0.5))
        result = self.update()
        self.assertEqual(0.5, result["blocking_floors"]["lossy"]["MediatR"]["native_fraction"])
        for mode in gate.MODES:
            for project in gate.PROJECTS:
                for metric in gate.METRICS:
                    self.assertGreaterEqual(result["blocking_floors"][mode][project][metric],
                                            self.baseline["blocking_floors"][mode][project][metric])
        for project in gate.PROJECTS:
            for metric in gate.METRICS:
                self.assertGreaterEqual(result["projects"][project][metric],
                                        self.baseline["projects"][project][metric])
        self.assertEqual(SOURCE_COMMIT, result["_writeback"]["source_commit"])
        self.assertEqual(SOURCE_RUN, result["_source_run"])
        self.assertEqual(UPDATED_AT, result["_updated_at"])
        self.assertEqual(10, len(result["_writeback"]["report_sha256"]))
        self.assertEqual(hashlib.sha256(self.report_path().read_bytes()).hexdigest(),
                         result["_writeback"]["report_sha256"]["lossy/MediatR-roundtrip.json"])
        self.assertEqual(self.baseline, gate.load_baseline(self.path))

    def test_default_counts_bootstrap_and_modes_never_mix(self):
        result = self.update()
        self.assertEqual(10, result["blocking_floors"]["default"]["MediatR"]["round_trip_passed"])
        self.assertEqual(155, result["blocking_floors"]["lossy"]["MediatR"]["round_trip_passed"])
        self.assertEqual(0.15625, result["blocking_floors"]["default"]["MediatR"]["native_fraction"])

    def test_established_higher_floors_reject_each_regression_atomically(self):
        for mode in gate.MODES:
            self.mutate(lambda r: r["round_trip"].update(passed=160, total=162, skipped=2),
                        mode=mode)
            self.mutate(lambda r: r["fidelity"]["coverage"].update(
                native_fraction=0.6, coverage_fraction=0.8), mode=mode)
        established = self.update()
        for mode in gate.MODES:
            self.assertEqual(
                dict(round_trip_passed=160, round_trip_total=162,
                     native_fraction=0.6, coverage_fraction=0.8),
                established["blocking_floors"][mode]["MediatR"])
        self.path.write_text(json.dumps(established), encoding="utf-8")
        self.assertEqual(established, self.update(established))
        before = self.path.read_bytes()
        for mode in gate.MODES:
            original = self.report_path(mode).read_bytes()
            for metric in gate.METRICS:
                with self.subTest(mode=mode, metric=metric):
                    self.report_path(mode).write_bytes(original)
                    if metric == "round_trip_passed":
                        self.mutate(lambda r: r["round_trip"].update(passed=159, skipped=3),
                                    mode=mode)
                    elif metric == "round_trip_total":
                        self.mutate(lambda r: r["round_trip"].update(total=161, skipped=1),
                                    mode=mode)
                    else:
                        self.mutate(lambda r: r["fidelity"]["coverage"].update(
                            {metric: established["blocking_floors"][mode]["MediatR"][metric] - 0.01}),
                            mode=mode)
                    process = self.cli("update", "--source-run", SOURCE_RUN,
                                       "--source-commit", SOURCE_COMMIT, "--updated-at", UPDATED_AT)
                    self.assertNotEqual(0, process.returncode)
                    self.assertIn(f"{mode}/MediatR: {metric}", process.stderr)
                    self.assertIn("below floor", process.stderr)
                    self.assertEqual(before, self.path.read_bytes())
            self.report_path(mode).write_bytes(original)

    def test_flat_run_preserves_metadata_and_bytes(self):
        result = self.update()
        self.path.write_text(json.dumps(result, indent=4) + "\n")
        before = self.path.read_bytes()
        process = self.cli("update", "--source-run", SOURCE_RUN, "--source-commit",
                           "b" * 40, "--updated-at", "2026-10-30T12:00:00Z")
        self.assertEqual(0, process.returncode, process.stderr)
        self.assertIn("unchanged", process.stdout)
        self.assertEqual(before, self.path.read_bytes())

    def test_cli_writes_an_update_then_check_enforces_it(self):
        process = self.cli("update", "--source-run", SOURCE_RUN,
                           "--source-commit", SOURCE_COMMIT, "--updated-at", UPDATED_AT)
        self.assertEqual(0, process.returncode, process.stderr)
        updated = gate.load_baseline(self.path)
        self.assertEqual(10, updated["blocking_floors"]["default"]["MediatR"]["round_trip_passed"])
        self.reports = self.reports / "default"
        process = self.cli("check", "--mode", "default")
        self.assertEqual(0, process.returncode, process.stderr)
        path = self.reports / "MediatR-roundtrip.json"
        report = json.loads(path.read_text())
        report["round_trip"].update(passed=9, skipped=1)
        path.write_text(json.dumps(report))
        process = self.cli("check", "--mode", "default")
        self.assertNotEqual(0, process.returncode)
        self.assertIn("round_trip_passed 9 is below floor 10", process.stderr)

    def test_late_failure_leaves_entire_baseline_untouched(self):
        self.mutate(lambda r: r["round_trip"].update(passed=156, total=158), mode="lossy")
        self.mutate(lambda r: r.update(verdict="fail"), mode="default", project="Synthetic2")
        before = self.path.read_bytes()
        process = self.cli("update", "--source-run", SOURCE_RUN,
                           "--source-commit", SOURCE_COMMIT, "--updated-at", UPDATED_AT)
        self.assertNotEqual(0, process.returncode)
        self.assertEqual(before, self.path.read_bytes())
        self.assertEqual(["baseline.json", "reports"], sorted(p.name for p in self.root.iterdir()))

    def test_update_refuses_regression_instead_of_taking_max(self):
        self.mutate(lambda r: r["fidelity"]["coverage"].update(native_fraction=0.399999))
        with self.assertRaisesRegex(ValueError, "below floor"):
            self.update()

    def test_rejects_bad_report_states(self):
        path = self.report_path()
        original = path.read_bytes()
        changes = [
            {"verdict": "fail"}, {"verdict": "inconclusive"}, {"inconclusive": True},
            {"inconclusive": None}, {"build_succeeded": False}, {"gate_failures": ["regression"]},
            {"gate_failures": None}, {"conversion_mode": "default"}, {"conversion_mode": None},
            {"report_schema_version": 1}, {"project": "Unknown"}, {"fidelity": None},
            {"round_trip": None},
        ]
        for change in changes:
            with self.subTest(change=change):
                path.write_bytes(original)
                self.mutate(lambda r: r.update(change))
                with self.assertRaises(ValueError):
                    self.update()

    def test_rejects_missing_required_report_fields(self):
        path = self.report_path()
        original = path.read_bytes()
        for key in ("project", "conversion_mode", "verdict", "inconclusive",
                    "gate_failures", "build_succeeded", "report_schema_version"):
            with self.subTest(key=key):
                path.write_bytes(original)
                self.mutate(lambda r: r.pop(key))
                with self.assertRaises(ValueError):
                    self.update()

    def test_rejects_invalid_fractions(self):
        original = self.report_path().read_bytes()
        for value in (None, True, "0.5", -0.1, 1.01, 10**400, float("nan"), float("inf")):
            with self.subTest(value=value):
                self.report_path().write_bytes(original)
                self.mutate(lambda r: r["fidelity"]["coverage"].update(native_fraction=value))
                with self.assertRaises(ValueError):
                    self.update()

    def test_rejects_invalid_counts(self):
        original = self.report_path().read_bytes()
        for value in (None, True, "155", 155.0, -1):
            with self.subTest(value=value):
                self.report_path().write_bytes(original)
                self.mutate(lambda r: r["round_trip"].update(passed=value))
                with self.assertRaises(ValueError):
                    self.update()

    def test_rejects_inconsistent_or_empty_inventory(self):
        original = self.report_path().read_bytes()
        for counts in ({"passed": 0, "total": 0, "failed": 0, "skipped": 0},
                       {"passed": 155, "total": 157, "failed": 0, "skipped": 0}):
            self.report_path().write_bytes(original)
            self.mutate(lambda r: r.update(round_trip=counts))
            with self.assertRaisesRegex(ValueError, "inventory"):
                self.update()

    def test_rejects_native_greater_than_coverage(self):
        self.mutate(lambda r: r["fidelity"]["coverage"].update(native_fraction=0.9))
        with self.assertRaisesRegex(ValueError, "native exceeds coverage"):
            self.update()

    def test_rejects_missing_project_or_directory(self):
        self.report_path().unlink()
        with self.assertRaisesRegex(ValueError, "incomplete project inventory"):
            self.update()
        with self.assertRaisesRegex(ValueError, "directory is missing"):
            gate.load_reports(self.root / "absent", "lossy")

    def test_rejects_duplicate_project_even_with_another_filename(self):
        duplicate = self.report_path().with_name("zz-duplicate-roundtrip.json")
        duplicate.write_bytes(self.report_path().read_bytes())
        with self.assertRaisesRegex(ValueError, "duplicate project"):
            self.update()

    def test_rejects_malformed_and_duplicate_key_json(self):
        for text in ('{"broken":', '{"project":"MediatR","project":"Serilog"}', "[]"):
            self.report_path().write_text(text)
            with self.assertRaises(ValueError):
                self.update()

    def test_rejects_incomplete_or_invalid_baseline(self):
        for change in (lambda b: b["blocking_floors"].pop("default"),
                       lambda b: b["blocking_floors"]["default"].pop("Synthetic2"),
                       lambda b: b["projects"].pop("Synthetic"),
                       lambda b: b.update(schema_version=2),
                       lambda b: b.update(schema_version=True),
                       lambda b: b["blocking_floors"]["lossy"]["MediatR"].update(native_fraction=True)):
            baseline = copy.deepcopy(self.baseline)
            change(baseline)
            self.path.write_text(json.dumps(baseline))
            with self.assertRaises(ValueError):
                gate.load_baseline(self.path)

    def test_rejects_missing_provenance(self):
        process = self.cli("update")
        self.assertNotEqual(0, process.returncode)
        self.assertIn("requires --source-run", process.stderr)
        for run, commit, timestamp in (("bad", SOURCE_COMMIT, UPDATED_AT),
                                       (SOURCE_RUN, "main", UPDATED_AT),
                                       (SOURCE_RUN, SOURCE_COMMIT, "today"),
                                       (SOURCE_RUN, SOURCE_COMMIT, "2026-99-29T12:00:00Z")):
            with self.assertRaises(ValueError):
                gate.updated_baseline(self.baseline, self.reports, run, commit, timestamp)

    def test_missing_mode_cannot_skip_gate(self):
        process = self.cli("check")
        self.assertNotEqual(0, process.returncode)
        self.assertIn("requires --mode", process.stderr)


class WorkflowTests(unittest.TestCase):
    def test_publication_is_separate_and_main_only(self):
        text = (ROOT / ".github/workflows/roundtrip-baselines.yml").read_text()
        self.assertNotIn("pull_request_target", text)
        self.assertNotIn("\n  pull_request:", text)
        measure, propose = text.split("\n  propose:\n")
        for job in (measure, propose):
            self.assertIn("github.ref == 'refs/heads/main'", job)
            self.assertIn("github.repository == 'juanmicrosoft/calor'", job)
            self.assertIn("github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'", job)
            self.assertIn("ref: ${{ github.sha }}", job)
            self.assertIn("persist-credentials: false", job)
        self.assertNotIn("contents: write", measure)
        self.assertNotIn("GH_TOKEN", measure)
        self.assertIn("needs.measure.result == 'success'", propose)
        self.assertIn("contents: write", propose)
        self.assertNotIn("dotnet ", propose)
        self.assertNotIn("submodules:", propose)
        self.assertNotIn("workflow_run:", text)
        self.assertNotIn("run-id:", text)
        self.assertIn('if [ "$current_main" != "$SOURCE_COMMIT" ]; then', propose)
        self.assertIn('git push origin "HEAD:refs/heads/$UPDATE_BRANCH"', propose)
        self.assertIn('gh pr create --base main --head "$UPDATE_BRANCH"', propose)
        self.assertNotIn("gh pr merge", propose)
        self.assertNotIn("--force", propose)
        self.assertLess(propose.index('if [ "$current_main"'), propose.index("git push"))

    def test_required_ci_gate_and_tests_are_not_masked(self):
        text = (ROOT / ".github/workflows/test.yml").read_text()
        self.assertIn("python3 scripts/test_roundtrip_baselines.py", text)
        step = text.split("      - name: Enforce committed round-trip floors (#1030)\n", 1)[1]
        step = step.split("\n      - name:", 1)[0]
        self.assertNotIn("continue-on-error", step)
        self.assertNotIn("if:", step)
        self.assertNotIn("||", step)
        self.assertIn("python3 scripts/roundtrip_baselines.py check", step)
        self.assertIn("--mode '${{ matrix.mode }}'", step)

    def test_committed_fraction_floors_meet_product_configs(self):
        baseline = gate.load_baseline(ROOT / "eng/roundtrip-baselines.json")
        configs = (ROOT / "tools/Calor.RoundTrip.Harness/ProjectConfigs.cs").read_text()
        for project in gate.PROJECTS:
            # Each config has one ProjectName followed by its four floor assignments.
            block = configs.split(f'ProjectName = "{project}"', 1)[1]
            for mode, prefix in (("lossy", ""), ("default", "DefaultMode")):
                for metric, property_name in (("coverage_fraction", "Coverage"), ("native_fraction", "Native")):
                    expression = re.search(
                        rf"\b{prefix}Minimum{property_name}Fraction = ([0-9. /]+),", block)[1]
                    parts = expression.split("/")
                    configured = float(parts[0]) / (float(parts[1]) if len(parts) == 2 else 1)
                    self.assertGreaterEqual(baseline["blocking_floors"][mode][project][metric],
                                            configured, f"{mode}/{project}/{metric}")


if __name__ == "__main__":
    unittest.main()
