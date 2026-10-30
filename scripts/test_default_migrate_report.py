#!/usr/bin/env python3
"""Truth checks for the default-path migration report."""

import copy
from pathlib import Path
import tempfile
import unittest

from default_migrate_report import markdown, summarize


class DefaultMigrateReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.files = []
        for name in ("Native", "Preserved", "Failed", "Blocked"):
            source = self.root / f"{name}.cs"
            source.write_text(f"class {name} {{}}")
            written = name in ("Native", "Preserved")
            output = source.with_suffix(".calr") if written else None
            if output:
                output.write_text("converted")
            self.files.append({
                "sourcePath": str(source),
                "outputPath": str(output) if output else None,
                "status": 0 if name == "Native" else 1 if written else 2,
                "losses": [] if name != "Preserved" else [
                    {"path": "rescue", "feature": "roundtrip-validation"}],
                "issues": [] if name != "Blocked" else [
                    {"feature": "project-validation", "message": "Another file failed"}],
                "conversionPaths": {
                    "outcome": "preserved" if name == "Preserved" else "native",
                    "roundTrip": "deferred",
                },
            })
        self.envelope = {
            "command": "migrate",
            "data": {"summary": {"totalFiles": 4}, "fileResults": self.files},
        }

    def test_separates_written_native_preserved_failed_and_blocked(self):
        result = summarize(self.envelope, self.root)
        self.assertEqual(result["written"], 2)
        self.assertEqual(result["fully_native_written"], 1)
        self.assertEqual(result["native_file_fraction"], 0.25)
        self.assertEqual(result["genuinely_failed"], 1)
        self.assertEqual(result["converted_but_blocked"], 1)
        self.assertEqual(result["passthrough_units_by_reason"], {"roundtrip-validation": 1})
        self.assertEqual(result["build"]["status"], "not-run")
        self.assertEqual(result["tests"]["status"], "not-run")
        self.assertIn("not-run", markdown([{**result, "project": "Fixture"}]))

    def test_refused_native_path_does_not_inflate_fraction(self):
        self.files[0]["status"] = 2
        Path(self.files[0]["outputPath"]).unlink()
        self.files[0]["outputPath"] = None
        self.assertEqual(summarize(self.envelope, self.root)["native_file_fraction"], 0)

    def test_losses_prevent_native_claim_even_with_native_path(self):
        self.files[1]["conversionPaths"]["outcome"] = "native"
        self.assertEqual(summarize(self.envelope, self.root)["fully_native_written"], 1)

    def test_failed_validation_never_counts_as_native(self):
        self.files[0]["conversionPaths"]["roundTrip"] = "failed"
        self.assertEqual(summarize(self.envelope, self.root)["fully_native_written"], 0)

    def test_missing_output_fails_closed(self):
        Path(self.files[0]["outputPath"]).unlink()
        with self.assertRaisesRegex(ValueError, "no written output"):
            summarize(self.envelope, self.root)

    def test_missing_report_entries_fail_closed(self):
        self.files.pop()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            summarize(self.envelope, self.root)

    def test_duplicate_sources_fail_closed(self):
        self.files[3]["sourcePath"] = self.files[2]["sourcePath"]
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            summarize(self.envelope, self.root)

    def test_unknown_status_fails_closed(self):
        self.files[0]["status"] = "success"
        with self.assertRaisesRegex(ValueError, "Unknown"):
            summarize(self.envelope, self.root)

    def test_failed_output_claim_fails_closed(self):
        self.files[0]["status"] = 2
        with self.assertRaisesRegex(ValueError, "claims a written"):
            summarize(self.envelope, self.root)

    def test_extra_source_fails_closed(self):
        (self.root / "Missing.cs").write_text("class Missing {}")
        with self.assertRaisesRegex(ValueError, "source inventory"):
            summarize(self.envelope, self.root)

    def test_extra_output_fails_closed(self):
        (self.root / "Unexpected.calr").write_text("stale output")
        with self.assertRaisesRegex(ValueError, "output inventory"):
            summarize(self.envelope, self.root)

    def test_default_discovery_exclusions_are_reported(self):
        (self.root / "GlobalUsings.cs").write_text("global using System;")
        result = summarize(self.envelope, self.root)
        self.assertEqual(result["discovery_excluded_sources"], ["GlobalUsings.cs"])
        self.assertEqual(result["total"], 4)

    def test_does_not_mutate_input(self):
        before = copy.deepcopy(self.envelope)
        summarize(self.envelope, self.root)
        self.assertEqual(before, self.envelope)


if __name__ == "__main__":
    unittest.main()
