#!/usr/bin/env python3
"""Supplemental scalar admission regressions, never the 103-control matrix."""

import importlib.util
import contextlib
import io
import hashlib
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import time
import sys
import unittest
from unittest.mock import patch


HERE = Path(__file__).resolve().parent


def load(name, relative):
    specification = importlib.util.spec_from_file_location(name, HERE / relative)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


DRIVER = load("logic_policy_driver", "test-logic-verifiers.py")
ENGINE = load("logic_policy_engine", "lib/verify-logic.py")


class LogicAdmissionPolicyTests(unittest.TestCase):
    def control(self):
        return type("SupplementalLogicControls", (DRIVER.LogicVerifierTests,), {
            "entry_points_executed": 0, "require_full_totals": False,
            "counts": None,
        })()

    def fake(self, output, status):
        if "Selected compiler:" not in output:
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            output += (f"Selected compiler: {DRIVER.COMPILER_ROOT}/src/Calor.Compiler/bin/"
                       f"{configuration}/net10.0/calor.dll\n")
        legacy = patch.object(DRIVER.subprocess, "run", return_value=subprocess.CompletedProcess(
            [], status, stdout=output))
        engine = patch.object(DRIVER, "ENGINE", ENGINE, create=True)
        execution = patch.object(ENGINE, "run", **(
            {"return_value": output} if status == 0 else
            {"side_effect": RuntimeError(f"Verifier command exited {status}:\n{output}")}))
        return legacy, engine, execution

    def test_unknown_is_not_an_expected_negative(self):
        output = ("Verification failed: System.InvalidOperationException: "
                  "Missing/wrong preconditions: UNKNOWN; incomplete theory\n")
        legacy, engine, execution = self.fake(output, 1)
        with legacy, engine, execution, self.assertRaises(RuntimeError):
            self.control().verify("Max", DRIVER.source("Max"), False, real=False)

    def test_abnormal_exit_is_not_an_expected_negative(self):
        output = "Verification failed: System.InvalidOperationException: Wrong public signature for Max.\n"
        legacy, engine, execution = self.fake(output, 2)
        with legacy, engine, execution, self.assertRaises(RuntimeError):
            self.control().verify("Max", DRIVER.source("Max"), False, real=False)

    def test_build_failure_is_not_an_expected_negative(self):
        legacy, engine, execution = self.fake("Build FAILED.\nerror MSB9999: defective build\n", 1)
        with legacy, engine, execution, self.assertRaises(RuntimeError):
            self.control().verify("Max", DRIVER.source("Max"), False, real=False)

    def test_duplicate_identity_is_not_success(self):
        identity = f"Selected compiler: {DRIVER.COMPILER_ROOT}/src/Calor.Compiler/bin/Debug/net10.0/calor.dll\n"
        output = identity + identity + "Verification passed: Max; semantic contracts and 121 runtime cases.\n"
        legacy, engine, execution = self.fake(output, 0)
        with legacy, engine, execution, self.assertRaises(RuntimeError):
            self.control().verify("Max", DRIVER.source("Max"), real=False)

    def test_cleanup_and_noncanonical_nested_exits_are_not_finite(self):
        prefix = "Verification failed: "
        for output in (
            prefix + "Verifier command exited 2:\n" + prefix
            + "System.InvalidOperationException: Wrong public signature for Max.\n",
            prefix + "System.InvalidOperationException: Wrong public signature for Max.\n"
            + "Verifier cleanup EOF timeout\n",
            prefix + "System.InvalidOperationException: Calor compilation failed:\nCalor1002 wrong return type\n",
        ):
            legacy, engine, execution = self.fake(output, 1)
            with self.subTest(output=output), legacy, engine, execution, self.assertRaises(RuntimeError):
                self.control().verify("Max", DRIVER.source("Max"), False,
                                      rejection=DRIVER.native_rejection("Max", "signature"), real=False)

    def test_expected_native_rejection_must_match_its_own_oracle(self):
        output = "Verification failed: System.InvalidOperationException: Wrong public signature for Max.\n"
        legacy, engine, execution = self.fake(output, 1)
        with legacy, engine, execution, self.assertRaises(RuntimeError):
            self.control().verify("Max", DRIVER.source("Max"), False,
                                  rejection=DRIVER.native_rejection("Max", "post"), real=False)

    def test_native_negative_requires_exactly_one_compiler_identity(self):
        canonical = (f"Selected compiler: {DRIVER.COMPILER_ROOT}/src/Calor.Compiler/bin/"
                     f"{os.environ.get('CALOR_COMPILER_CONFIGURATION', 'Debug')}/net10.0/calor.dll\n")
        for kind in ("finite", "compile"):
            for identity in ("", "Selected compiler: /wrong/calor.dll\n", canonical + canonical):
                def execute(command, cwd, deadline, env=None):
                    source = Path(command[1]).resolve() / "Calculator.calr"
                    payload = ("System.InvalidOperationException: Wrong public signature for Max.\n"
                               if kind == "finite" else
                               "System.InvalidOperationException: Calor compilation failed:\n"
                               f"{source}:1:1 Calor0100 Expected Module but found Identifier\n")
                    raise RuntimeError("Verifier command exited 1:\nVerification failed: " + payload + identity)
                with self.subTest(kind=kind, identity=identity), \
                        patch.object(DRIVER.ENGINE, "run", side_effect=execute), \
                        self.assertRaisesRegex(RuntimeError, "compiler identity"):
                    self.control().verify(
                        "Max", DRIVER.source("Max") if kind == "finite" else "invalid source",
                        False, kind=kind, rejection=DRIVER.native_rejection("Max", "signature"), real=False)

    def test_copied_image_pre_and_post_checks_and_locked_local_commands(self):
        compiler = DRIVER.COMPILER_ROOT / "src/Calor.Compiler/bin" / os.environ.get(
            "CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        for when in ("before", "after"):
            launched = []
            def execute(command, cwd, deadline, env=None):
                output = Path(cwd) / "bin/Debug/net10.0"
                if command[1] == "restore":
                    self.assertIn("--locked-mode", command)
                    self.assertEqual(command[command.index("--source") + 1], str(Path.home() / ".nuget/packages"))
                elif command[1] == "build":
                    self.assertIn("--no-restore", command)
                    shutil.copytree(compiler, output)
                    if when == "before":
                        (output / "calor.dll").write_bytes(b"substituted helper copy")
                else:
                    launched.append(command)
                    (output / "calor.dll").write_bytes(b"substituted helper copy")
                    return "Verification passed: Max; semantic contracts and 121 runtime cases.\n"
                return ""
            with self.subTest(when=when), tempfile.TemporaryDirectory(prefix="calor-logic-image-check-") as directory:
                workspace = Path(directory)
                (workspace / "Calculator.calr").write_text(DRIVER.source("Max"), encoding="utf-8")
                with patch.object(sys, "argv", ["verify-logic.py", "Max", str(workspace)]), \
                        patch.dict(os.environ, CALOR_COMPILER_ROOT=str(DRIVER.COMPILER_ROOT)), \
                        patch.object(ENGINE, "run", side_effect=execute), \
                        contextlib.redirect_stdout(io.StringIO()), \
                        self.assertRaisesRegex(RuntimeError, "image changed or was substituted"):
                    ENGINE.main()
                self.assertEqual(len(launched), int(when == "after"))

    def test_negative_exit_image_mutation_is_not_finite_evidence(self):
        compiler = DRIVER.COMPILER_ROOT / "src/Calor.Compiler/bin" / os.environ.get(
            "CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        for target in ("selected", "copied"):
            changed = False
            read_bytes = Path.read_bytes
            def read_image(path):
                if target == "selected" and changed and path == compiler / "calor.dll":
                    return b"negative-exit substitution"
                return read_bytes(path)
            def execute(command, cwd, deadline, env=None):
                nonlocal changed
                output = Path(cwd) / "bin/Debug/net10.0"
                if command[1] == "build":
                    shutil.copytree(compiler, output)
                elif command[1] != "restore":
                    changed = True
                    if target == "copied":
                        (output / "calor.dll").write_bytes(b"negative-exit substitution")
                    raise RuntimeError("Verifier command exited 1:\nVerification failed: "
                                       "System.InvalidOperationException: Wrong public signature for Max.")
                return ""
            with self.subTest(target=target), \
                    tempfile.TemporaryDirectory(prefix="calor-logic-negative-image-") as directory:
                (Path(directory) / "Calculator.calr").write_text(DRIVER.source("Max"), encoding="utf-8")
                errors, stdout = io.StringIO(), io.StringIO()
                with patch.object(sys, "argv", ["verify-logic.py", "Max", directory]), \
                        patch.dict(os.environ, CALOR_COMPILER_ROOT=str(DRIVER.COMPILER_ROOT)), \
                        patch.object(ENGINE, "run", side_effect=execute), \
                        patch.object(Path, "read_bytes", read_image), \
                        contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(errors), \
                        self.assertRaises(RuntimeError) as raised:
                    ENGINE.main()
                self.assertIn("image changed or was substituted", "\n".join(raised.exception.__notes__))
                self.assertIn("post-failure image validation failure", errors.getvalue())
                captured = ("Verifier command exited 1:\n" + errors.getvalue()
                            + "Verification failed: " + str(raised.exception) + "\n" + stdout.getvalue())
                self.assertIn(f"Selected compiler: {compiler / 'calor.dll'}", captured)
                with patch.object(DRIVER.ENGINE, "run", side_effect=RuntimeError(captured)), \
                        self.assertRaisesRegex(RuntimeError, "image changed or was substituted"):
                    self.control().verify(
                        "Max", DRIVER.source("Max"), False,
                        rejection=DRIVER.native_rejection("Max", "signature"), real=False)

    def test_expired_helper_timeout_remains_primary_after_image_check(self):
        compiler = DRIVER.COMPILER_ROOT / "src/Calor.Compiler/bin" / os.environ.get(
            "CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        clock = [0.0]
        def execute(command, cwd, deadline, env=None):
            if command[1] == "build":
                shutil.copytree(compiler, Path(cwd) / "bin/Debug/net10.0")
            elif command[1] != "restore":
                clock[0] = 1000.0
                raise RuntimeError("Verifier timeout after 10.0s: dotnet")
            return ""
        with tempfile.TemporaryDirectory(prefix="calor-logic-timeout-image-") as directory:
            (Path(directory) / "Calculator.calr").write_text(DRIVER.source("Max"), encoding="utf-8")
            errors = io.StringIO()
            with patch.object(sys, "argv", ["verify-logic.py", "Max", directory]), \
                    patch.dict(os.environ, CALOR_COMPILER_ROOT=str(DRIVER.COMPILER_ROOT)), \
                    patch.object(ENGINE, "run", side_effect=execute), \
                    patch.object(ENGINE.time, "monotonic", side_effect=lambda: clock[0]), \
                    contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(errors), \
                    self.assertRaisesRegex(RuntimeError, "^Verifier timeout after 10.0s: dotnet$"):
                ENGINE.main()
            self.assertEqual(errors.getvalue(), "")

    def test_native_mixed_signature_positive(self):
        self.control().verify("Max", DRIVER.source(
            "Max", signature="func Max (int:a, System.Int32:b) → int"))

    def test_native_boolean_positive(self):
        self.control().verify("IsPositive", DRIVER.source("IsPositive", expression=True))

    def test_native_factorial_positive(self):
        self.control().verify("Factorial", DRIVER.source("Factorial"))

    def test_native_clamp_positive(self):
        self.control().verify("Clamp", DRIVER.source("Clamp"))

    def test_native_abi_guard_precedes_body_type_error(self):
        output = self.control().verify("Clamp", DRIVER.source(
            "Clamp", signature="func Clamp (long:value, int:min, int:max) → int"),
            False, rejection=DRIVER.native_rejection("Clamp", "signature"))
        self.assertNotIn("Calor compilation failed", output)

    def test_native_empty_body_is_a_structural_guard(self):
        self.control().verify("Factorial", DRIVER.source("Factorial", body=""), False,
                              rejection=DRIVER.native_rejection("Factorial", "body"))

    def test_native_contract_negative(self):
        self.control().verify("Factorial", DRIVER.source(
            "Factorial", contracts="requires n >= 0\nrequires n <= 5"),
            False, rejection=DRIVER.native_rejection("Factorial", "post"))

    def test_native_entry_and_required_output_oracles(self):
        for contracts, category in (
            (DRIVER.DECLARATIONS["Factorial"][1] + "\nrequires false", "entry"),
            (DRIVER.DECLARATIONS["Factorial"][1] + "\n=> false", "output"),
        ):
            with self.subTest(category=category):
                self.control().verify("Factorial", DRIVER.source("Factorial", contracts=contracts),
                                      False, rejection=DRIVER.native_rejection("Factorial", category))

    def test_original_wrong_factorial_boundary_uses_independent_oracle(self):
        self.control().verify("Factorial", DRIVER.source(
            "Factorial", body="if n == 0\n  return 2\n" + DRIVER.DECLARATIONS["Factorial"][2]),
            False, detail="expected", rejection=DRIVER.native_rejection("Factorial", "behavior"))

    def test_native_ensures_failure_is_specific_behavior_evidence(self):
        self.control().verify("IsPositive", DRIVER.source("IsPositive", body="return x >= 0"),
                              False, rejection=DRIVER.native_rejection("IsPositive", "behavior"))

    def test_original_wrong_sum_overflows_on_a_required_input(self):
        self.control().verify("Max", DRIVER.source("Max", body="return a + b"),
                              False, rejection=DRIVER.native_rejection("Max", "overflow"))

    def test_original_compile_invalid_is_not_behavior_evidence(self):
        control = self.control()
        control.verify("Factorial", "invalid source", False, detail="Calor compilation failed", kind="compile")
        self.assertEqual(control.counts["compile"], 1)
        self.assertEqual(control.counts["finite"], 0)

    def test_original_malformed_comment_inputs_are_compile_only_guards(self):
        control = self.control()
        for name in DRIVER.TASK_NAMES:
            with self.subTest(name=name):
                text = "// " + " ".join(DRIVER.DECLARATIONS[name]) + "\nmodule Calculator\n"
                control.verify(name, text, False, kind="compile")
        self.assertEqual(control.counts["compile"], 4)
        self.assertEqual(control.counts["finite"], 0)

    def test_compile_guards_refuse_other_sources_and_diagnostics(self):
        for text, diagnostic in (
            (DRIVER.source("Max"), "1:1 Calor0100 Expected Module but found Identifier"),
            ("// " + " ".join(DRIVER.DECLARATIONS["Max"]) + "\nmodule Calculator\n",
             "2:1 Calor0100 Expected Module but found Identifier"),
            ("invalid source", "1:1 Calor1002 Wrong return type"),
        ):
            def execute(command, cwd, deadline, env=None):
                source = Path(command[1]).resolve() / "Calculator.calr"
                raise RuntimeError("Verifier command exited 1:\nVerification failed: "
                                   "System.InvalidOperationException: Calor compilation failed:\n"
                                   f"{source}:{diagnostic}\n"
                                   f"Selected compiler: {DRIVER.COMPILER_ROOT}/src/Calor.Compiler/bin/"
                                   f"{os.environ.get('CALOR_COMPILER_CONFIGURATION', 'Debug')}/net10.0/calor.dll\n")
            with self.subTest(diagnostic=diagnostic), patch.object(DRIVER.ENGINE, "run", side_effect=execute), \
                    self.assertRaises(RuntimeError):
                self.control().verify("Max", text, False, kind="compile", real=False)

    def test_parser_eof_diagnostic_cannot_hide_actual_infrastructure(self):
        source = Path("/observed/Calculator.calr")
        text = "// " + " ".join(DRIVER.DECLARATIONS["Max"]) + "\nmodule Calculator\n"
        packet = (
            "Verifier command exited 1:\nVerification failed: "
            "System.InvalidOperationException: Calor compilation failed:\n"
            + "".join(f"{source}:{diagnostic}\n"
                      for diagnostic in DRIVER.parse_guard_diagnostics("Max", text))
        )
        DRIVER.compile_guard("Max", text, packet, source)
        for failure in ("Verifier cleanup EOF timeout", "UNKNOWN", "Build FAILED.", "Verifier timeout"):
            with self.subTest(failure=failure), self.assertRaises(RuntimeError):
                DRIVER.compile_guard("Max", text, packet + failure + "\n", source)
        for changed in (
            packet.replace("Expected Eof but found Return", "Expected Eof but found If"),
            packet.replace("Calor0102 Missing required", "Calor0100 Missing required"),
            packet.replace("Calor0100 Expected Identifier", "Calor0100 Unexpected Identifier"),
            packet + f"{source}:9:1 Calor0100 Extra parser error\n",
        ):
            with self.subTest(changed=changed), self.assertRaises(RuntimeError):
                DRIVER.compile_guard("Max", text, changed, source)

    def commented_native_control(self, name):
        text = "module Calculator\n" + "".join(
            "// " + line + "\n" for line in DRIVER.source(name).splitlines())
        print(f"Supplemental comment-only {name} input SHA256: "
              f"{hashlib.sha256(text.encode('utf-8')).hexdigest()}", flush=True)
        output = self.control().verify(
            name, text, False, rejection=DRIVER.native_rejection(name, "function"))
        self.assertNotIn("Calor compilation failed", output)

    def test_supplemental_commented_factorial_missing_function(self):
        self.commented_native_control("Factorial")

    def test_supplemental_commented_clamp_missing_function(self):
        self.commented_native_control("Clamp")

    def test_supplemental_commented_is_positive_missing_function(self):
        self.commented_native_control("IsPositive")

    def test_supplemental_commented_max_missing_function(self):
        self.commented_native_control("Max")

    def test_original_deliberate_execution_error(self):
        self.control().verify("Max", DRIVER.source(
            "Max", body='! throw\nthrow "deliberate runtime failure"'),
            False, detail="deliberate runtime failure", rejection=DRIVER.native_rejection("Max", "execution"))

    def test_original_selected_arm_contrasts(self):
        control = self.control()
        control.test_selected_arm_has_no_parent_fallback()
        self.assertEqual(control.counts["accepted"], 1)
        self.assertEqual(control.counts["prerequisite"], 2)
        self.assertEqual(control.counts["finite"], 0)

    def test_original_short_budget_is_a_separate_timeout(self):
        control = self.control()
        control.verify("Max", DRIVER.source("Max"), False,
                       env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        self.assertEqual(control.counts["timeout"], 1)
        self.assertEqual(control.counts["finite"], 0)

    def test_original_execution_budget_is_a_separate_timeout(self):
        control = self.control()
        control.verify("Max", DRIVER.source("Max", body="while true\n  continue\nreturn a"), False,
                       env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.assertEqual(control.counts["timeout"], 1)
        self.assertEqual(control.counts["finite"], 0)

    def test_original_missing_input_is_a_separate_guard(self):
        control = self.control()
        control.verify("Factorial", None, False, detail="Input missing", kind="input")
        self.assertEqual(control.counts["input"], 1)
        self.assertEqual(control.counts["finite"], 0)

    def test_manifest_refusal_for_every_scalar_profile(self):
        relative = Path("src/Calor.Compiler/bin") / os.environ.get(
            "CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        with tempfile.TemporaryDirectory(prefix="calor-logic-bad-native-") as temporary:
            root = Path(temporary)
            shutil.copytree(DRIVER.COMPILER_ROOT / relative, root / relative)
            shutil.copytree(DRIVER.COMPILER_ROOT / ".github", root / ".github")
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[os.sys.platform]
            (root / relative / native).write_bytes(b"invalid selected native image")
            for name in DRIVER.TASK_NAMES:
                with tempfile.TemporaryDirectory(prefix="calor-logic-native-input-") as workspace:
                    (Path(workspace) / "Calculator.calr").write_text(DRIVER.source(name), encoding="utf-8")
                    with self.assertRaisesRegex(RuntimeError, "Selected native manifest mismatch"):
                        DRIVER.ENGINE.run(
                            [str(DRIVER.TASKS / DRIVER.TASK_NAMES[name] / "verify.sh"), workspace],
                            HERE, time.monotonic() + 120,
                            env=dict(os.environ, CALOR_COMPILER_ROOT=str(root)))


if __name__ == "__main__":
    unittest.main(verbosity=2)
