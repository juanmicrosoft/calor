"""Correctness tests for #1264. All observations are deterministic synthetic data."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

from ppw_redesign_epoch import BENCH, build, instrument, save


class InstrumentTests(unittest.TestCase):
    def setUp(self):
        self.root = BENCH / "tests" / (".instrument-test-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.epoch = build(self.root)

    def analyze(self, stage="pilot", epoch="synthetic-pilot"):
        return instrument.analyze(self.root, epoch, stage)

    def mutate(self, path, change):
        data = json.loads(path.read_text())
        change(data)
        save(path, data)

    def result(self, arm="calor-permissive", run=1):
        return self.epoch / "runs" / "SYNTHETIC-task" / arm / ("run-%d" % run) / "result.json"

    def test_pilot_records_without_dry_run_and_remains_nonconfirmatory(self):
        path = instrument.record_stage(self.root, "synthetic-pilot", "pilot")
        report = json.loads(path.read_text())
        self.assertFalse(report["dryRun"])
        self.assertFalse(report["empirical"])
        self.assertEqual("synthetic", report["dataKind"])
        self.assertEqual("pilot", report["stage"])
        self.assertTrue(report["epochRun"])
        self.assertIsNone(report["verdict"])
        self.assertNotIn("sizing", report)
        self.assertNotIn("constants", report)
        self.assertEqual([1, 0], [cell["escapeRate"] for cell in report["perCell"]])
        self.assertEqual([100, 100], report["perCell"][0]["outputTokens"])

    def test_cli_capture_integration_records_explicit_stage(self):
        result = subprocess.run(
            [sys.executable, str(BENCH / "ppw-analyze.py"), "--epoch-id", "synthetic-pilot",
             "--stage", "pilot", "--epochs-root", str(self.root)],
            text=True, capture_output=True)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        report = json.loads((self.epoch / "ppw-stage-ledger.json").read_text())
        self.assertEqual("pilot", report["stage"])
        self.assertEqual(2, report["perCell"][0]["escapes"])

    def test_wrong_stage_is_rejected_with_reason(self):
        with self.assertRaisesRegex(ValueError, "wrong-stage"):
            self.analyze(stage="confirmatory")

    def test_model_and_client_must_match_registered_stage(self):
        original = (self.epoch / "pins.json").read_text()
        for field in ("modelPin", "agentVersion"):
            with self.subTest(field=field):
                self.mutate(self.epoch / "pins.json", lambda pins: pins.update({field: "DIFFERENT"}))
                with self.assertRaisesRegex(ValueError, field + " differs"):
                    self.analyze()
            (self.epoch / "pins.json").write_text(original)

    def test_confirmatory_reads_only_its_own_epoch(self):
        build(self.root, "confirmatory")
        before = self.analyze("confirmatory", "synthetic-confirmatory")
        self.mutate(self.result(), lambda record: record.update(compilerHash="broken"))
        self.assertEqual(before, self.analyze("confirmatory", "synthetic-confirmatory"))

    def test_copied_pilot_run_cannot_enter_confirmatory(self):
        confirmation = build(self.root, "confirmatory")
        target = confirmation / self.result().relative_to(self.epoch)
        shutil.copyfile(self.result(), target)
        with self.assertRaisesRegex(ValueError, "cross-epoch"):
            self.analyze("confirmatory", "synthetic-confirmatory")

    def test_multiple_epoch_inputs_are_not_an_api(self):
        for epoch in ("synthetic-pilot,synthetic-confirmatory", "../synthetic-pilot", "*",
                      ["synthetic-pilot", "synthetic-confirmatory"]):
            with self.subTest(epoch=epoch), self.assertRaisesRegex(ValueError, "one epoch"):
                self.analyze(epoch=epoch)

    def test_unexpected_result_cannot_silently_change_denominator(self):
        target = self.epoch / "runs" / "foreign-task" / "calor-permissive" / "run-1"
        target.mkdir(parents=True)
        shutil.copyfile(self.result(), target / "result.json")
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.analyze()

    def test_symlinked_pilot_data_is_refused(self):
        path = self.result()
        original = self.root / "elsewhere.json"
        path.rename(original)
        path.symlink_to(original)
        with self.assertRaisesRegex(ValueError, "symlink"):
            self.analyze()

    def test_mixed_compiler_and_policy_pins_fail_closed(self):
        for change, message in (
            (lambda p: p["compiler"].update(commit="f" * 40), "compiler commit"),
            (lambda p: p.update(armA={"commit": "f" * 40}), "per-arm compiler"),
            (lambda p: p["arms"]["A"].update(flags=["--permissive-effects", "--transpile-only"]), "solely"),
        ):
            original = (self.epoch / "pins.json").read_text()
            with self.subTest(message=message):
                self.mutate(self.epoch / "pins.json", change)
                with self.assertRaisesRegex(ValueError, message):
                    self.analyze()
            (self.epoch / "pins.json").write_text(original)

    def test_every_valid_run_requires_shared_compiler(self):
        for field, value in (("compilerHash", "f" * 64), ("compilerCommit", "f" * 40),
                             ("armRepoRoot", "/different/product")):
            original = self.result().read_text()
            with self.subTest(field=field):
                self.mutate(self.result(), lambda r: r.update({field: value}))
                with self.assertRaisesRegex(ValueError, "compiler"):
                    self.analyze()
            self.result().write_text(original)

    def test_options_hashes_are_observational_not_a_policy_proof(self):
        for run in (1, 2):
            self.mutate(self.result("calor-strict", run),
                        lambda r: r["buildState"].update(optionsHash="policy-A"))
        self.assertEqual(2, len(self.analyze()["perCell"]))

    def test_changed_public_contract_is_not_an_escape_and_does_not_remove_a_slot(self):
        path = self.result().parent / "source-inspection.json"
        self.mutate(path, lambda r: r["sources"]["final:0"]["publicApi"].update(effects=["mut"]))
        cell = self.analyze()["perCell"][0]
        self.assertEqual((2, 0, 1, 0.5),
                         (cell["validRuns"], cell["invalidRuns"], cell["escapes"], cell["escapeRate"]))
        self.assertEqual([1], cell["changedPublicApiRuns"])

    def test_unparseable_nonbuild_retains_denominator_without_claiming_a_negative_shape(self):
        path = self.result().parent / "source-inspection.json"
        self.mutate(path, lambda r: r["sources"]["final:0"].update(
            parseOk=False, publicApi=None, calls=[]))
        self.mutate(self.result(), lambda r: r.update(finalBuild={"ok": False}))
        cell = self.analyze()["perCell"][0]
        self.assertEqual((2, 0, 1, 0.5),
                         (cell["validRuns"], cell["invalidRuns"], cell["escapes"], cell["escapeRate"]))
        self.assertEqual([1], cell["unscorableShapeRuns"])
        self.assertIsNone(cell["shapeRealizedRate"])
        self.assertEqual([], cell["unscorablePublicApiRuns"])

    def test_built_but_uninspectable_output_cannot_produce_an_escape_rate(self):
        self.mutate(self.result().parent / "source-inspection.json",
                    lambda r: r["sources"]["final:0"].update(parseOk=False, publicApi=None, calls=[]))
        cell = self.analyze()["perCell"][0]
        self.assertEqual((2, 0), (cell["validRuns"], cell["invalidRuns"]))
        self.assertEqual([1], cell["unscorablePublicApiRuns"])
        self.assertIsNone(cell["escapeRate"])

    def test_native_inspection_cannot_be_reused_for_changed_source_or_another_compiler(self):
        path = self.result().parent / "source-inspection.json"
        original = path.read_text()
        for change, message in (
            (lambda r: r.update(compilerSha256="f" * 64), "different compiler"),
            (lambda r: r["inputSha256"]["final"].update({"Source.calr": "f" * 64}), "input hashes"),
            (lambda r: r["sources"]["baseline:0"]["publicApi"].update(changed=True), "baseline differs"),
            (lambda r: r["sources"].update({"foreign:0": {}}), "inventory"),
        ):
            with self.subTest(message=message):
                self.mutate(path, change)
                with self.assertRaisesRegex(ValueError, message):
                    self.analyze()
            path.write_text(original)

    def test_native_control_certificate_cannot_pin_another_compiler(self):
        path = self.epoch / "registration.json"
        self.mutate(path, lambda r: r["sourceInspections"]["SYNTHETIC-task"].update(compilerSha256="f" * 64))
        self.mutate(self.epoch / "pins.json",
                    lambda p: p.update(registrationSha256=instrument.digest(path)))
        with self.assertRaisesRegex(ValueError, "source-inspection compiler"):
            self.analyze()

    def test_wrong_effective_policy_is_rejected_despite_disjoint_hashes(self):
        for name in ("policy-before.json", "policy-after.json"):
            self.mutate(self.result("calor-strict").parent / name,
                        lambda p: p["policy"].update(CalorPermissiveEffects=True))
        with self.assertRaisesRegex(ValueError, "effective workspace policy"):
            self.analyze()

    def test_missing_or_changed_policy_capture_is_rejected(self):
        self.mutate(self.result().parent / "policy-after.json",
                    lambda p: p["configurationSha256"].update({"src/Src.csproj": "f" * 64}))
        with self.assertRaisesRegex(ValueError, "configuration changed"):
            self.analyze()

    def test_actual_invalid_record_with_missing_transcript_is_disclosed(self):
        path = self.result()
        self.mutate(path, lambda r: r.update(invalid=True, compilerHash=None, buildState={},
                                           turns={"assistantMessages": None}, finalBuild={"ok": None}))
        (path.parent / "transcript.jsonl").unlink()
        (path.parent / "invalid.txt").write_text("synthetic API error\n")
        cell = self.analyze()["perCell"][0]
        self.assertEqual((1, 1, 1), (cell["invalidRuns"], cell["validRuns"], cell["censoredRuns"]))

    def test_entirely_invalid_arm_is_recordable_without_false_rate(self):
        for run in (1, 2):
            path = self.result(run=run)
            self.mutate(path, lambda r: r.update(invalid=True, turns={"assistantMessages": None}))
            (path.parent / "invalid.txt").write_text("synthetic API error\n")
        cell = self.analyze()["perCell"][0]
        self.assertEqual(2, cell["invalidRuns"])
        self.assertIsNone(cell["escapeRate"])

    def test_heldout_compile_failure_is_unscorable_not_a_pass_or_dropped_run(self):
        (self.result().parent / ".ho_final.txt").write_text("error CS0117: missing member\n")
        cell = self.analyze()["perCell"][0]
        self.assertEqual(2, cell["validRuns"])
        self.assertEqual([1], cell["unscorableHeldoutRuns"])
        self.assertIsNone(cell["escapeRate"])

    def test_skipped_observer_is_not_scored_as_no_escape(self):
        (self.result().parent / ".ho_final.txt").write_text("  Skipped Synthetic.Tests.PreservesState [1 ms]\n")
        self.assertIsNone(self.analyze()["perCell"][0]["escapeRate"])

    def test_scaffolded_and_collecting_are_not_run_even_if_directory_exists(self):
        for lifecycle in ("scaffolded", "collecting"):
            self.mutate(self.epoch / "pins.json", lambda p: p.update(lifecycle=lifecycle))
            with self.assertRaisesRegex(ValueError, "not completed collection"):
                self.analyze()
        self.mutate(self.epoch / "pins.json", lambda p: p.update(lifecycle="archived"))
        self.assertTrue(self.analyze()["epochRun"])

    def test_value_failure_is_not_an_escape(self):
        log = self.result().parent / ".ho_final.txt"
        log.write_text("  Failed Synthetic.Tests.PreservesState [1 ms]\n"
                       "  Error Message:\n   Assert.Equal() Failure: Values differ\n")
        cell = self.analyze()["perCell"][0]
        self.assertEqual(1, cell["escapes"])
        self.assertEqual([1], cell["namedTestFailuresWithoutEffect"])

    def test_nonbuilding_runs_stay_in_denominator_without_escape(self):
        self.mutate(self.result(), lambda r: r.update(finalBuild={"ok": False}))
        cell = self.analyze()["perCell"][0]
        self.assertEqual(2, cell["validRuns"])
        self.assertEqual(1, cell["didNotBuildAtDeclaredDone"])
        self.assertEqual(0.5, cell["escapeRate"])

    def test_generated_validation_failure_without_cache_retains_nonbuilding_slot(self):
        self.mutate(self.result(), lambda r: r.update(finalBuild={"ok": False},
                                                    compilerHash=None, buildState={}))
        cell = self.analyze()["perCell"][0]
        self.assertEqual((2, 1, 0.5), (cell["validRuns"], cell["didNotBuildAtDeclaredDone"],
                                      cell["escapeRate"]))
        self.mutate(self.result(), lambda r: r.update(productCompilerHash="f" * 64))
        with self.assertRaisesRegex(ValueError, "product canary"):
            self.analyze()

    @unittest.skipUnless(shutil.which("dotnet"), "dotnet required for real workspace build regression")
    def test_real_restore_build_does_not_mutate_isolated_workspace_policy(self):
        workspace = self.root / "real-build"
        capture = instrument.helper("harness-capture.py")
        capture.isolate_workspace(workspace)
        (workspace / "src").mkdir()
        (workspace / "src" / "Src.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<CalorEnforceEffects>true</CalorEnforceEffects>'
            '<CalorPermissiveEffects>false</CalorPermissiveEffects>'
            '</PropertyGroup></Project>')
        before = capture.policy_snapshot(workspace)
        result = subprocess.run(["dotnet", "build", str(workspace / "src" / "Src.csproj"), "--nologo", "-v", "q"],
                                text=True, capture_output=True, timeout=120,
                                env=dict(os.environ, TMPDIR=str(self.root)))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertFalse((workspace / "src" / "packages.lock.json").exists())
        self.assertEqual(before, capture.policy_snapshot(workspace))

    def test_registration_changes_and_unreviewed_supersession_are_rejected(self):
        self.mutate(self.epoch / "registration.json", lambda r: r.update(reviews=[]))
        with self.assertRaisesRegex(ValueError, "review references"):
            self.analyze()

    def test_task_mutation_is_not_a_new_registration(self):
        source = self.epoch / "tasks" / "SYNTHETIC-task" / "starter-a" / "Source.calr"
        source.write_text("changed")
        with self.assertRaisesRegex(ValueError, "frozen artifact changed"):
            self.analyze()

    def test_analysis_record_is_append_only(self):
        instrument.record_stage(self.root, "synthetic-pilot", "pilot")
        with self.assertRaises(FileExistsError):
            instrument.record_stage(self.root, "synthetic-pilot", "pilot")

    def test_one_shared_product_is_passed_to_both_capture_arms(self):
        compiler = json.loads((self.epoch / "pins.json").read_text())["compiler"]
        calls = [instrument.pair_command(self.epoch, arm, compiler, self.root, 0)
                 for arm in instrument.ARMS.values()]
        for option in ("--arm-repo-root", "--calor-dll", "--edit-mechanism", "--runs"):
            self.assertEqual(calls[0][calls[0].index(option) + 1], calls[1][calls[1].index(option) + 1])
        self.assertEqual("calor-permissive", calls[0][calls[0].index("--arm-config") + 1])
        self.assertEqual("calor-strict", calls[1][calls[1].index("--arm-config") + 1])

    def test_historical_analyzer_cannot_consume_modern_data(self):
        legacy = instrument.helper("ppw-analyze.py")
        with self.assertRaisesRegex(SystemExit, "historical A-1.12"):
            legacy.analyze(str(self.epoch), dry_run=True)
        with self.assertRaisesRegex(SystemExit, "historical A-1.12"):
            legacy.build_ledger(self.analyze())

    def test_runner_refuses_missing_authorization_before_creating_epoch(self):
        with self.assertRaisesRegex(ValueError, "registration does not authorize collection"):
            instrument.run_epoch(self.epoch / "registration.json", self.epoch / "tasks",
                                 self.root / "compiler", self.root, "synthetic-pilot", "pilot")

    def test_collection_rejects_all_ambient_harness_test_overrides_before_io(self):
        for name in ("CALOR_P0_TIMEOUT_OVERRIDE", "CALOR_P0_SHIM_OFF",
                     "CALOR_P0_SKIP_ARM_CANARY", "CALOR_P0_VERIFY_GATE", "CALOR_LOOP_SNAPSHOTS"):
            with self.subTest(name=name), patch.dict(os.environ, {name: "1"}):
                with self.assertRaisesRegex(ValueError, name):
                    instrument.run_epoch("missing-registration", "missing-tasks", "missing-compiler",
                                         self.root, "synthetic-pilot", "pilot")

    def test_dirty_check_allows_only_exact_registered_failed_archive_inventory(self):
        repo = self.root / "SYNTHETIC-repo"
        bench = repo / "bench/phase0-agent-native"
        archive = bench / "epochs/w-rows-pilot-gateway-001"
        archive.mkdir(parents=True)
        (archive / "pins.json").write_text("{}\n")
        # Git status omits ignored archive contents; the inventory still authenticates them.
        (archive / "ignored-operational-record.json").write_text("{}\n")
        recovery = instrument.helper("ppw-gateway-recovery.py")
        inventory = recovery.archive_inventory(archive)
        relative = (archive / "pins.json").relative_to(repo).as_posix()
        admission = {"recovery": {
            "failedArchive": str(archive),
            "failedArchiveInventorySha256": inventory["sha256"],
        }}

        def clean_status(argv, **_):
            return "" if "--untracked-files=no" in argv else "?? " + relative

        with patch.object(instrument, "REPO", repo), patch.object(instrument, "BENCH", bench), \
                patch.object(instrument, "helper", return_value=recovery), \
                patch.object(instrument, "command", side_effect=clean_status):
            instrument.validate_harness_checkout(admission)

        def extra_status(argv, **_):
            if "--untracked-files=no" in argv:
                return ""
            return "?? %s\n?? bench/phase0-agent-native/epochs/unregistered/data.json" % relative

        with patch.object(instrument, "REPO", repo), patch.object(instrument, "BENCH", bench), \
                patch.object(instrument, "helper", return_value=recovery), \
                patch.object(instrument, "command", side_effect=extra_status):
            with self.assertRaisesRegex(ValueError, "exact registered failed archive"):
                instrument.validate_harness_checkout(admission)

        def tracked_status(argv, **_):
            return " M bench/phase0-agent-native/epochs/historical/pins.json"

        with patch.object(instrument, "REPO", repo), patch.object(instrument, "BENCH", bench), \
                patch.object(instrument, "helper", return_value=recovery), \
                patch.object(instrument, "command", side_effect=tracked_status):
            with self.assertRaisesRegex(ValueError, "tracked changes"):
                instrument.validate_harness_checkout(admission)

    def fake_capture(self, behavior, arm="B", run=1, spending_ticket=None,
                     budget_support=True, expect_refusal=None, gateway=False):
        """Execute the actual shell runner with deterministic local stand-ins.

        PATH resolves claude and dotnet to these files, never real agents or
        compilers. These results are explicitly stamped synthetic afterwards.
        """
        executable = self.root / "fake-bin"
        executable.mkdir(exist_ok=True)
        calls = self.root / "synthetic-agent-calls"
        prior_calls = len(calls.read_text().splitlines()) if calls.exists() else 0
        fake_dotnet = executable / "dotnet"
        fake_dotnet.write_text("""#!/usr/bin/env python3
import hashlib, json, pathlib, shlex, shutil, subprocess, sys, xml.etree.ElementTree as ET
if "--help" in sys.argv:
    sys.exit(0)
if any(a.endswith("PpwSourceInspector.csproj") for a in sys.argv):
    output = pathlib.Path(sys.argv[sys.argv.index("--output") + 1])
    output.mkdir(parents=True, exist_ok=True)
    (output / "ppw-source-inspector.dll").write_text("synthetic inspector, not executable")
    compiler = next(a.split("=", 1)[1] for a in sys.argv if a.startswith("--property:CalorCompilerDll="))
    shutil.copyfile(compiler, output / "calor.dll")
    sys.exit(0)
if len(sys.argv) > 1 and sys.argv[1].endswith("ppw-source-inspector.dll"):
    request = json.load(sys.stdin)
    sources = {}
    for item in request["sources"]:
        text = item["text"].encode("utf-16-le")
        edited = "\\n".join(text[a*2:b*2].decode("utf-16-le") for a,b in item["editableRanges"])
        sources[item["name"]] = {"parseOk":True, "publicApi":{"syntheticFixture":True},
                                "calls":["§C{this.lookup}"] if "this.lookup" in edited else []}
    dll = pathlib.Path(sys.argv[1]).parent / "calor.dll"
    print(json.dumps({"schemaVersion":1, "compilerSha256":hashlib.sha256(dll.read_bytes()).hexdigest(),
                      "sources":sources}))
    sys.exit(0)
if "--input" in sys.argv and "--format" in sys.argv:
    inputs = [sys.argv[i+1] for i,a in enumerate(sys.argv) if a == "--input"]
    print(json.dumps({"version":"SYNTHETIC", "diagnostics":[],
                      "syntheticPermissive":"--permissive-effects" in sys.argv,
                      "syntheticInputs":[pathlib.Path(p).name for p in inputs]}))
    sys.exit(0)
project = pathlib.Path.cwd() / "Src.csproj"
if "build" in sys.argv:
    if not project.exists():
        project = pathlib.Path(next(a for a in sys.argv if a.endswith(".csproj")))
    assembly = ET.parse(project).find(".//Target[@Name='_PpwAssembleSources']/Exec")
    if assembly is not None:
        command = assembly.attrib["Command"].replace("$(MSBuildThisFileDirectory)", str(project.parent) + "/")
        result = subprocess.run(shlex.split(command), capture_output=True, text=True)
        if result.returncode:
            print(result.stderr)
            sys.exit(result.returncode)
    state = project.parent / "obj/calor/.calor-build-state.json"
    state.parent.mkdir(parents=True, exist_ok=True)
    state.write_text(json.dumps({"compilerHash": "d"*64, "optionsHash": str(project.parent)}))
    if (project.parent / "canary.calr").exists() and "<CalorPermissiveEffects>false" in project.read_text():
        print("error Calor0410: synthetic unknown canary")
        sys.exit(1)
    print("Build succeeded.")
if "test" in sys.argv:
    print("  Passed Synthetic.Tests.PreservesState [1 ms]")
    print("Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1")
""")
        fake_agent = executable / "claude"
        fake_agent.write_text("""#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys
if "--help" in sys.argv:
    print("--max-budget-usd" if os.environ["SYNTHETIC_BUDGET_SUPPORT"] == "1" else "no budget control")
    sys.exit(0)
with open(os.environ["SYNTHETIC_CALLS"], "a") as f: f.write("attempt\\n")
pathlib.Path(os.environ["SYNTHETIC_CALLS"]+".arguments.json").write_text(json.dumps(sys.argv[1:]))
if os.environ["SYNTHETIC_BEHAVIOR"] == "policy-change":
    p=pathlib.Path("Src.csproj")
    p.write_text(p.read_text().replace("<CalorPermissiveEffects>false", "<CalorPermissiveEffects>true"))
if os.environ["SYNTHETIC_BEHAVIOR"] == "fragment-edit":
    pathlib.Path("task.calr.inc").write_text("this.lookup\\n")
    subprocess.run(["dotnet", "build"], capture_output=True, text=True, check=True)
if os.environ["SYNTHETIC_BEHAVIOR"] == "dependency-edit":
    pathlib.Path("dependency.calr.inc").write_text("changed dependency\\n")
if os.environ["SYNTHETIC_BEHAVIOR"] == "extra-source":
    pathlib.Path("Bypass.cs").write_text("// unregistered source\\n")
print(json.dumps({"type":"assistant","message":{"id":"synthetic","content":[]}}))
print(json.dumps({"type":"result","result":"API error" if os.environ["SYNTHETIC_BEHAVIOR"] == "api-error" else "synthetic done",
                  "total_cost_usd":0.05,
                  "usage":{"output_tokens":1},"modelUsage":{"SYNTHETIC":{"outputTokens":100}}}))
""")
        fake_dotnet.chmod(0o755)
        fake_agent.chmod(0o755)
        compiler_root = self.root / "product"
        (compiler_root / "src/Calor.Tasks").mkdir(parents=True, exist_ok=True)
        (compiler_root / "bench/phase0-agent-native/templates").mkdir(parents=True, exist_ok=True)
        dll = compiler_root / "calor.dll"
        dll.write_text("synthetic product, not executable")
        task = self.epoch / "tasks" / "SYNTHETIC-task"
        compiler = {"repoRoot": str(compiler_root), "calorDll": str(dll)}
        output = self.root / "captured"
        env = dict(os.environ, PATH=str(executable) + os.pathsep + os.environ["PATH"],
                   TMPDIR=str(self.root), SYNTHETIC_CALLS=str(calls), SYNTHETIC_BEHAVIOR=behavior,
                   SYNTHETIC_BUDGET_SUPPORT="1" if budget_support else "0",
                   CALOR_P0_SKIP_ARM_CANARY="0")
        argv = instrument.pair_command(task, instrument.ARMS[arm], compiler, output, run - 1)
        if spending_ticket is not None:
            argv += ["--ppw-spend-ticket", str(spending_ticket)]
        if gateway:
            argv += ["--ppw-gateway-client", str(fake_agent)]
            env["PPW_GATEWAY_ACTIVE"] = "1"
        result = subprocess.run(["bash"] + argv,
                                env=env, text=True, capture_output=True, timeout=30)
        if expect_refusal:
            self.assertNotEqual(0, result.returncode)
            self.assertIn(expect_refusal, result.stderr)
            self.assertEqual(prior_calls, len(calls.read_text().splitlines()) if calls.exists() else 0)
            return None
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual(prior_calls + 1, len(calls.read_text().splitlines()))
        captured = output / "SYNTHETIC-task" / instrument.ARMS[arm]["label"] / ("run-%d" % run)
        self.assertTrue((captured / "transcript.jsonl").is_file())
        self.assertTrue((captured / "agent.json").is_file())
        return captured

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "jq and bash required for shell capture")
    def test_actual_capture_does_not_retry_or_discard_api_failure(self):
        captured = self.fake_capture("api-error")
        self.assertTrue(json.loads((captured / "result.json").read_text())["invalid"])
        self.assertIn("API error", (captured / "agent.json").read_text())

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "jq and bash required for shell capture")
    def test_actual_capture_rejects_strict_workspace_changed_to_permissive(self):
        captured = self.fake_capture("policy-change")
        self.assertTrue(json.loads((captured / "result.json").read_text())["invalid"])
        self.assertIn("configuration changed", (captured / "invalid.txt").read_text())

    @unittest.skipUnless(shutil.which("jq") and shutil.which("bash"), "jq and bash required for shell capture")
    def test_actual_shell_capture_to_stage_ledger_uses_one_product(self):
        pins = json.loads((self.epoch / "pins.json").read_text())
        pins["compiler"].update(repoRoot=str(self.root / "product"),
                                calorDll=str(self.root / "product" / "calor.dll"))
        save(self.epoch / "pins.json", pins)
        for arm in ("A", "B"):
            for run in (1, 2):
                captured = self.fake_capture("success", arm, run)
                instrument.stamp_run(captured / "result.json", pins)
                target = self.result(instrument.ARMS[arm]["label"], run).parent
                shutil.rmtree(target)
                shutil.copytree(captured, target)
        report = json.loads(instrument.record_stage(self.root, "synthetic-pilot", "pilot").read_text())
        self.assertFalse(report["empirical"])
        self.assertEqual("pilot", report["stage"])
        self.assertEqual([2, 2], [cell["validRuns"] for cell in report["perCell"]])
        self.assertEqual([0, 0], [cell["escapeRate"] for cell in report["perCell"]])

    @unittest.skipUnless(shutil.which("dotnet") and shutil.which("jq") and shutil.which("bash"),
                         "dotnet, jq and bash required for real held-out infrastructure regression")
    def test_real_shell_generated_heldout_project_restores_and_executes(self):
        captured = self.fake_capture("success")
        heldout = captured / "heldout" / "HeldOut.csproj"
        # Supply the source assembly at the exact HintPath emitted by the shell;
        # do not rewrite the generated held-out project or its package settings.
        hint = Path(ET.parse(heldout).find(".//Reference[@Include='Src']/HintPath").text)
        self.assertTrue(hint.is_relative_to(self.root))
        source = self.root / "real-source"
        instrument.helper("harness-capture.py").isolate_workspace(source)
        (source / "Src.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        environment = dict(os.environ, TMPDIR=str(self.root))
        result = subprocess.run(["dotnet", "build", str(source / "Src.csproj"), "-o", str(hint.parent),
                                 "--nologo", "-v", "q"], env=environment,
                                text=True, capture_output=True, timeout=120)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        result = subprocess.run(["dotnet", "test", str(heldout), "--nologo", "-v", "q"],
                                env=environment, text=True, capture_output=True, timeout=180)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("Passed:", result.stdout)
        self.assertFalse((heldout.parent / "packages.lock.json").exists())


if __name__ == "__main__":
    unittest.main()
