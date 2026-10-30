#!/usr/bin/env python3
"""Fail-closed controls for registered key and corpus replay."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import registered_ledger_historical_replay as replay


class RegisteredReplayTests(unittest.TestCase):
    def test_every_regeneration_flag_fails_before_git_or_build(self):
        for name in replay.REPLAYS:
            for flag in replay.REGENERATION_FLAGS:
                with self.subTest(name=name, flag=flag):
                    with patch.dict(os.environ, {flag: "1"}, clear=True):
                        with patch.object(replay, "git") as git:
                            with self.assertRaisesRegex(ValueError, "forbids"):
                                replay.replay(name)
                            git.assert_not_called()

    def test_missing_producer_or_landing_fails(self):
        for name, (producer, landing, _, _) in replay.REPLAYS.items():
            for unavailable in (producer, landing):
                def git(*args):
                    commit = args[-1].removesuffix("^{commit}")
                    if commit == unavailable:
                        raise subprocess.CalledProcessError(128, "git")
                    return (commit + "\n").encode()
                with self.subTest(name=name, missing=unavailable):
                    with patch.dict(os.environ, {}, clear=True):
                        with patch.object(replay, "git", side_effect=git):
                            with self.assertRaisesRegex(ValueError, "fetch full history"):
                                replay.replay(name)

    def test_changed_final_ledger_or_repointed_producer_fails_before_build(self):
        for name, (producer, _, ledger, _) in replay.REPLAYS.items():
            for repoint in (False, True):
                with self.subTest(name=name, repoint=repoint), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    field = "measuredCommit" if name == "key" else "MeasuredCommit"
                    original = json.dumps({field: "0" * 40 if repoint else producer}).encode()
                    path = root / ledger
                    path.parent.mkdir(parents=True)
                    path.write_bytes(original if repoint else original + b"\nchanged")
                    with patch.dict(os.environ, {}, clear=True):
                        with patch.object(replay, "ROOT", root), patch.object(replay, "require_commit"):
                            with patch.object(replay, "git", return_value=original):
                                with patch.object(replay, "run_dotnet") as run:
                                    with self.assertRaisesRegex(ValueError, "producer differs|immutable final ledger"):
                                        replay.replay(name)
                                    run.assert_not_called()

    def test_only_one_executed_pass_is_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "result.trx"
            for _, _, _, test in replay.REPLAYS.values():
                def result(name=test, outcome="Passed"):
                    return f'<UnitTestResult testName="{name}" outcome="{outcome}" />'
                def write(results):
                    path.write_text(
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                        f"<Results>{results}</Results></TestRun>", encoding="utf-8",
                    )
                write(result())
                replay.verify_result(path, test)
                for rows in ("", result(outcome="Failed"), result(outcome="NotExecuted"),
                             result(name="Unrelated"), result() + result()):
                    write(rows)
                    with self.assertRaisesRegex(ValueError, "no skips/empty runs"):
                        replay.verify_result(path, test)
            with self.assertRaises(FileNotFoundError):
                replay.verify_result(path.with_name("missing.trx"), test)

    def test_uninitialized_submodule_cannot_resolve_to_parent_repository(self):
        entries = "\n".join(
            f"160000 commit {'1' * 40}\tbench/corpus/{subject}"
            for subject in ("MediatR", "serilog", "FluentValidation")
        ).encode()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with patch.object(replay, "ROOT", root), patch.object(replay, "git", return_value=entries):
                with patch.object(replay.subprocess, "check_output", return_value=str(root).encode()):
                    with patch.object(replay.subprocess, "run") as run:
                        with self.assertRaisesRegex(ValueError, "submodule is not initialized"):
                            replay.archive_submodules("producer", root / "source", root)
                        run.assert_not_called()

    def test_missing_registered_submodule_pin_fails(self):
        with patch.object(replay, "git", return_value=b""):
            with self.assertRaisesRegex(ValueError, "exactly the three registered submodule pins"):
                replay.archive_submodules("producer", Path("unused"), Path("unused"))

    def test_missing_original_submodule_object_fails_without_network(self):
        entries = "\n".join(
            f"160000 commit {'1' * 40}\tbench/corpus/{subject}"
            for subject in ("MediatR", "serilog", "FluentValidation")
        ).encode()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            module = root / "bench/corpus/MediatR"
            with patch.object(replay, "ROOT", root), patch.object(replay, "git", return_value=entries):
                with patch.object(replay.subprocess, "check_output", return_value=str(module).encode()):
                    with patch.object(replay.subprocess, "run",
                                      side_effect=subprocess.CalledProcessError(128, "git")) as run:
                        with self.assertRaisesRegex(ValueError, "original submodule commit.*unavailable"):
                            replay.archive_submodules("producer", root / "source", root)
                        self.assertEqual("cat-file", run.call_args.args[0][3])

    def test_submodule_archives_use_pinned_objects_not_worktree_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            entries = []
            for subject in ("MediatR", "serilog", "FluentValidation"):
                module = root / "bench/corpus" / subject
                module.mkdir(parents=True)
                subprocess.run(["git", "init", "--quiet", str(module)], check=True)
                fixture = module / "original.cs"
                fixture.write_text("original source\n", encoding="utf-8")
                subprocess.run(["git", "-C", str(module), "add", "original.cs"], check=True)
                subprocess.run(
                    ["git", "-C", str(module), "-c", "user.name=Replay fixture",
                     "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false",
                     "commit", "--quiet", "-m", "Original test fixture"],
                    check=True,
                )
                commit = subprocess.check_output(["git", "-C", str(module), "rev-parse", "HEAD"]).decode().strip()
                entries.append(f"160000 commit {commit}\tbench/corpus/{subject}")
                fixture.write_text("changed current worktree\n", encoding="utf-8")
                (module / "active-mirror.cs").write_text("untracked active copy\n", encoding="utf-8")
            source = root / "source"
            with patch.object(replay, "ROOT", root):
                with patch.object(replay, "git", return_value=("\n".join(entries)).encode()):
                    replay.archive_submodules("producer", source, root)
            for _, _, _, relative in (entry.split() for entry in entries):
                self.assertEqual("original source\n", (source / relative / "original.cs").read_text())
                self.assertFalse((source / relative / "active-mirror.cs").exists())


if __name__ == "__main__":
    unittest.main()
