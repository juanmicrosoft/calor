#!/usr/bin/env python3
"""Fail-closed controls for the historical PP-E1 runner."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import ppe1_historical_replay as replay


class HistoricalReplayTests(unittest.TestCase):
    def test_both_dotnet_launches_disable_persistent_servers(self):
        project = "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj"
        environment = {
            "LC_ALL": "C",
            "CALOR_HOME": "/isolated/empty-calor-home",
            "MSBUILDDISABLENODEREUSE": "0",
            "DOTNET_CLI_USE_MSBUILD_SERVER": "1",
        }
        launches = (
            (["restore", project, "--locked-mode"], 300),
            ([
                "test", project, "-c", "Release", "--no-restore",
                "--filter", f"FullyQualifiedName={replay.TEST}",
                "--logger", "trx;LogFileName=historical.trx",
                "--results-directory", "/isolated/results",
            ], 600),
        )
        for arguments, timeout in launches:
            with self.subTest(command=arguments[0]):
                with patch.object(replay.subprocess, "run") as run:
                    replay.run_dotnet(arguments, Path("/isolated/source"), environment, timeout)
                    run.assert_called_once_with(
                        [
                            "dotnet", *arguments, "--disable-build-servers",
                            "-nr:false", "-p:UseSharedCompilation=false",
                        ],
                        cwd=Path("/isolated/source"),
                        env={
                            **environment,
                            "MSBUILDDISABLENODEREUSE": "1",
                            "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
                        },
                        check=True, timeout=timeout,
                    )
        self.assertEqual("0", environment["MSBUILDDISABLENODEREUSE"])
        self.assertEqual("1", environment["DOTNET_CLI_USE_MSBUILD_SERVER"])

    def test_frozen_bytes_must_match(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "fixture.calr"
            path.write_bytes(b"frozen original\n")
            replay.require_bytes(path, b"frozen original\n")
            with self.assertRaisesRegex(ValueError, "frozen replay input differs"):
                replay.require_bytes(path, b"active translation\n")
            with self.assertRaises(FileNotFoundError):
                replay.require_bytes(path.with_name("missing.calr"), b"frozen original\n")

    def test_regeneration_is_rejected_before_any_git_or_build(self):
        with patch.dict(os.environ, {"CALOR_REGENERATE_PPE1_LEDGER": "1"}):
            with patch.object(replay, "git") as git:
                with self.assertRaisesRegex(ValueError, "forbids"):
                    replay.replay()
                git.assert_not_called()

    def test_missing_original_compiler_fails_with_prerequisite(self):
        with patch.dict(os.environ, {}, clear=True):
            with patch.object(replay, "git", side_effect=subprocess.CalledProcessError(128, "git")):
                with self.assertRaisesRegex(ValueError, "fetch full repository history"):
                    replay.replay()

    def test_changed_ledger_fails_before_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ledger = root / replay.LEDGER
            ledger.parent.mkdir(parents=True)
            ledger.write_bytes(b'{"measuredCommit":"invented"}')
            with patch.dict(os.environ, {}, clear=True):
                with patch.object(replay, "ROOT", root):
                    with patch.object(replay, "git", return_value=(replay.COMMIT + "\n").encode()):
                        with self.assertRaisesRegex(ValueError, "historical ledger bytes changed"):
                            replay.replay()

    def test_only_one_executed_pass_is_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "result.trx"

            def result(name=replay.TEST, outcome="Passed"):
                return f'<UnitTestResult testName="{name}" outcome="{outcome}" />'

            def write(results):
                path.write_text(
                    '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                    f"<Results>{results}</Results></TestRun>", encoding="utf-8"
                )

            write(result())
            replay.verify_result(path)
            for results in (
                "", result(outcome="Failed"), result(outcome="NotExecuted"),
                result(name="UnrelatedTest"), result() + result(),
            ):
                with self.subTest(results=results):
                    write(results)
                    with self.assertRaisesRegex(ValueError, "exactly the named test once"):
                        replay.verify_result(path)
            with self.assertRaises(FileNotFoundError):
                replay.verify_result(path.with_name("missing.trx"))


if __name__ == "__main__":
    unittest.main()
