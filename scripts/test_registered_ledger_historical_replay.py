#!/usr/bin/env python3
"""Fail-closed controls for registered effect/demand ledger replay."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import registered_ledger_historical_replay as replay


class TranslatorReplayTests(unittest.TestCase):
    def test_requires_exact_summary_as_well_as_one_passing_result(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "result.trx"
            for outcome, total, executed, passed, failed, skipped in (
                ("Completed", 1, 1, 1, 0, 0), ("Failed", 1, 1, 1, 0, 0),
                ("Completed", 0, 0, 0, 0, 0), ("Completed", 2, 1, 1, 0, 1),
                ("Completed", 1, 1, 1, 1, 0),
            ):
                path.write_text(
                    '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                    f'<Results><UnitTestResult testName="{replay.TRANSLATOR_TEST}" outcome="Passed" /></Results>'
                    f'<ResultSummary outcome="{outcome}"><Counters total="{total}" executed="{executed}" '
                    f'passed="{passed}" failed="{failed}" notExecuted="{skipped}" error="0" timeout="0" />'
                    '</ResultSummary></TestRun>', encoding="utf-8",
                )
                if (outcome, total, executed, passed, failed, skipped) == ("Completed", 1, 1, 1, 0, 0):
                    replay.verify_result(path, replay.TRANSLATOR_TEST, strict=True)
                else:
                    with self.assertRaisesRegex(ValueError, "exact 1 pass"):
                        replay.verify_result(path, replay.TRANSLATOR_TEST, strict=True)

    def test_regeneration_and_missing_producer_fail_before_build(self):
        for flag in ("CALOR_UPDATE_ANYTHING", "CALOR_REGENERATE_ANYTHING"):
            with patch.dict(os.environ, {flag: "1"}, clear=True):
                with patch.object(replay, "require_commit") as require:
                    with self.assertRaisesRegex(ValueError, "forbids regeneration"):
                        replay.replay_translator(Path("unused"))
                    require.assert_not_called()
        with patch.dict(os.environ, {}, clear=True):
            with patch.object(replay, "git", side_effect=subprocess.CalledProcessError(128, "git")):
                with self.assertRaisesRegex(ValueError, "fetch full history"):
                    replay.replay_translator(Path("unused"))

    def test_acceptance_requires_actual_output_pass_assets_and_immutable_inputs(self):
        cases = json.loads((replay.ROOT / replay.DETERMINISM_CASES).read_bytes())
        artifact = next(a for a in cases["artifacts"] if a["name"] == "translator-fixture")
        for fault in (None, "missing-output", "empty-output", "tampered-output", "skipped",
                      "empty-run", "tampered-source", "missing-assets", "build-failure",
                      "changed-registration", "reused-output"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                registry = root / replay.DETERMINISM_CASES
                registry.parent.mkdir(parents=True)
                registry.write_text(json.dumps(cases), encoding="utf-8")
                manifests = (
                    ".github/z3-binaries-4.15.7.sha256",
                    "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256",
                )
                for relative in manifests:
                    path = root / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(b"pinned assets")
                def git(*args):
                    if args[0] == "show":
                        return json.dumps(cases).encode() if args[1].endswith("cases.json") \
                            else artifact["expected"].encode()
                    return b""
                def extract(_, source):
                    for relative in (*manifests, "original.cs"):
                        path = source / relative
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_bytes(b"pinned assets" if relative in manifests else b"original")
                def run(arguments, source, environment, timeout):
                    self.assertGreater(timeout, 0)
                    self.assertEqual(root / "output", output)
                    self.assertNotEqual(os.environ.get("HOME"), environment["HOME"])
                    if fault == "build-failure":
                        raise subprocess.CalledProcessError(1, "dotnet")
                    if arguments[0] != "test":
                        self.assertIn("--locked-mode", arguments)
                        return
                    self.assertIn(f"FullyQualifiedName={replay.TRANSLATOR_TEST}", arguments)
                    self.assertNotIn("--no-build", arguments)
                    results = Path(arguments[arguments.index("--results-directory") + 1])
                    results.mkdir()
                    outcome = "NotExecuted" if fault == "skipped" else "Passed"
                    rows = "" if fault == "empty-run" else (
                        f'<UnitTestResult testName="{replay.TRANSLATOR_TEST}" outcome="{outcome}" />')
                    (results / "historical.trx").write_text(
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                        f'<Results>{rows}</Results><ResultSummary outcome="Completed">'
                        '<Counters total="1" executed="1" passed="1" failed="0" error="0" '
                        'timeout="0" notExecuted="0" /></ResultSummary></TestRun>', encoding="utf-8",
                    )
                    generated = Path(environment["CALOR_DETERMINISM_RECORD_DIR"]) / "generated"
                    generated.mkdir(parents=True)
                    if fault != "missing-output":
                        (generated / "translator-fixture").write_bytes(
                            b"" if fault == "empty-output" else b"changed" if fault == "tampered-output"
                            else artifact["expected"].encode())
                    if fault == "tampered-source":
                        (source / "original.cs").write_bytes(b"changed")
                    if fault == "changed-registration":
                        registry.write_bytes(b"changed")
                output = root / "output"
                if fault == "reused-output":
                    output.mkdir()
                with patch.dict(os.environ, {}, clear=True), patch.object(replay, "ROOT", root), \
                        patch.object(replay, "git", side_effect=git), \
                        patch.object(replay, "require_commit"), \
                        patch.object(replay, "extract_archive", side_effect=extract), \
                        patch.object(replay.runpy, "run_path", return_value={"ASSET_PATHS": {}}), \
                        patch.object(replay.subprocess, "run", side_effect=(
                            subprocess.CalledProcessError(1, "verify assets") if fault == "missing-assets" else None)), \
                        patch.object(replay, "run_dotnet", side_effect=run):
                    if fault is None:
                        replay.replay_translator(output)
                        self.assertEqual(artifact["expected"].encode(),
                                         (output / "generated/translator-fixture").read_bytes())
                        replay.verify_result(output / "historical.trx", replay.TRANSLATOR_TEST, strict=True)
                    else:
                        with self.assertRaises((ValueError, FileNotFoundError, subprocess.CalledProcessError)):
                            replay.replay_translator(output)
                        observed = output / "generated/translator-fixture"
                        if fault in ("empty-output", "tampered-output"):
                            self.assertEqual(b"" if fault == "empty-output" else b"changed", observed.read_bytes())
                        elif fault in ("skipped", "empty-run", "tampered-source", "changed-registration"):
                            self.assertEqual(artifact["expected"].encode(), observed.read_bytes())
                        else:
                            self.assertFalse(observed.exists())


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
            for repoint in ((False,) if name in ("formatter", "binder") else (False, True)):
                with self.subTest(name=name, repoint=repoint), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    field = "MeasuredCommit" if name == "corpus" else "measuredCommit"
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

    def test_changed_binder_aggregate_baseline_fails_before_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ledger = root / replay.REPLAYS["binder"][2]
            ledger.parent.mkdir(parents=True)
            ledger.write_bytes(b"{}")
            (ledger.parent / "binder-incomplete-baseline.json").write_bytes(b"changed")
            with patch.dict(os.environ, {}, clear=True):
                with patch.object(replay, "ROOT", root), patch.object(replay, "require_commit"):
                    with patch.object(replay, "git", return_value=b"{}"):
                        with patch.object(replay, "run_dotnet") as run:
                            with self.assertRaisesRegex(ValueError, "immutable conversion baseline"):
                                replay.replay("binder")
                            run.assert_not_called()

    def test_changed_binder_carrier_evidence_fails_before_build(self):
        for changed in (b"changed", b"{}\n", b'{"Cases": []}'):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                ledger = root / replay.REPLAYS["binder"][2]
                ledger.parent.mkdir(parents=True)
                ledger.write_bytes(b"{}")
                (ledger.parent / "binder-incomplete-baseline.json").write_bytes(b"{}")
                (ledger.parent / "binder-source-carrier-evidence.json").write_bytes(changed)
                with patch.dict(os.environ, {}, clear=True), \
                        patch.object(replay, "ROOT", root), patch.object(replay, "require_commit"), \
                        patch.object(replay, "git", return_value=b"{}"), \
                        patch.object(replay, "run_dotnet") as run, \
                        patch.object(replay, "archive_submodules") as archive:
                    with self.assertRaisesRegex(ValueError, "immutable source-carrier evidence"):
                        replay.replay("binder")
                    run.assert_not_called()
                    archive.assert_not_called()

    def test_binder_carrier_evidence_is_checked_in_archive_and_after_execution(self):
        carrier = "bench/phase0-agent-native/binder-source-carrier-evidence.json"
        baseline = "bench/phase0-agent-native/binder-incomplete-baseline.json"
        manifests = (
            ".github/z3-binaries-4.15.7.sha256",
            "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256",
        )
        for fault in (None, "archive", "missing-archive", "replayed-input", "current-input"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                artifacts = {replay.REPLAYS["binder"][2]: b"{}",
                             baseline: b"original baseline", carrier: b"original carriers"}
                inputs = artifacts | {relative: b"pinned assets" for relative in manifests}
                for relative, content in inputs.items():
                    path = root / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(content)
                def git(*args):
                    return artifacts[args[1].split(":", 1)[1]] if args[0] == "show" else b""
                def extract(_, source):
                    for relative, content in inputs.items():
                        if relative == carrier and fault == "missing-archive":
                            continue
                        path = source / relative
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_bytes(content + b"\n" if relative == carrier and fault == "archive" else content)
                def run(arguments, source, environment, timeout):
                    self.assertEqual(artifacts[carrier], (source / carrier).read_bytes())
                    self.assertGreater(timeout, 0)
                    if arguments[0] != "test":
                        self.assertIn("--locked-mode", arguments)
                        return
                    self.assertNotIn("--no-build", arguments)
                    results = Path(arguments[arguments.index("--results-directory") + 1])
                    results.mkdir()
                    (results / "historical.trx").write_text(
                        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                        f'<Results><UnitTestResult testName="{replay.REPLAYS["binder"][3]}" outcome="Passed" />'
                        '</Results></TestRun>', encoding="utf-8",
                    )
                    if fault == "replayed-input":
                        (source / carrier).write_bytes(artifacts[carrier] + b"\n")
                    elif fault == "current-input":
                        (root / carrier).write_bytes(artifacts[carrier] + b"\n")
                with patch.dict(os.environ, {}, clear=True), patch.object(replay, "ROOT", root), \
                        patch.object(replay, "git", side_effect=git), patch.object(replay, "require_commit"), \
                        patch.object(replay, "extract_archive", side_effect=extract), \
                        patch.object(replay, "archive_submodules"), \
                        patch.object(replay.runpy, "run_path", return_value={"ASSET_PATHS": {}}), \
                        patch.object(replay.subprocess, "run"), \
                        patch.object(replay, "run_dotnet", side_effect=run) as launch:
                    if fault is None:
                        replay.replay("binder")
                        self.assertEqual(2, launch.call_count)
                    else:
                        with self.assertRaises((ValueError, FileNotFoundError)):
                            replay.replay("binder")
                        self.assertEqual(0 if fault in ("archive", "missing-archive") else 2, launch.call_count)

    def test_isolated_index_uses_original_paths_not_current_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            original = root / "original"
            original.mkdir()
            subprocess.run(["git", "init", "--quiet", str(original)], check=True)
            (original / "frozen.calr").write_text("original\n")
            subprocess.run(["git", "-C", str(original), "add", "frozen.calr"], check=True)
            subprocess.run(
                ["git", "-C", str(original), "-c", "user.name=Replay fixture",
                 "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false",
                 "commit", "--quiet", "-m", "Original test fixture"], check=True,
            )
            commit = subprocess.check_output(["git", "-C", str(original), "rev-parse", "HEAD"]).decode().strip()
            source = root / "source"
            source.mkdir()
            (source / "untracked-current.calr").write_text("not an original subject\n")
            def git(*args):
                return subprocess.check_output(["git", "-C", str(original), *args])
            with patch.object(replay, "git", side_effect=git):
                replay.index_original_tree(commit, source)
            tracked = subprocess.check_output(["git", "-C", str(source), "ls-files"]).decode()
            self.assertEqual("frozen.calr\n", tracked)
            self.assertFalse((source / "frozen.calr").exists())

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
