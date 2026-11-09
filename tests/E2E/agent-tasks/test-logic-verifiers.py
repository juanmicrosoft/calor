#!/usr/bin/env python3
"""Executable controls for the real logic-task verify.sh entry points."""

import os
import importlib.util
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest


ROOT = Path(__file__).resolve().parents[3]
TASKS = Path(__file__).resolve().parent / "tasks/logic-implementation"
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASK_NAMES = {
    "Factorial": "01_factorial", "Clamp": "02_clamp",
    "IsPositive": "03_is_positive", "Max": "04_max",
}
DECLARATIONS = {
    "Factorial": (
        "func Factorial (int:n) → int",
        "requires n >= 0\nrequires n <= 5\n=> result >= 1",
        "if n <= 1\n  return 1\nelse if n == 2\n  return 2\nelse if n == 3\n  return 6\nelse if n == 4\n  return 24\nreturn 120",
        "return n <= 1 ? 1 : (n == 2 ? 2 : (n == 3 ? 6 : (n == 4 ? 24 : 120)))",
    ),
    "Clamp": (
        "func Clamp (int:value, int:min, int:max) → int",
        "requires min <= max\n=> result >= min\n=> result <= max",
        "if value < min\n  return min\nif value > max\n  return max\nreturn value",
        "return value < min ? min : (value > max ? max : value)",
    ),
    "IsPositive": (
        "func IsPositive (int:x) → bool",
        "=> result == (x > 0)",
        "if x > 0\n  return true\nreturn false",
        "return x > 0",
    ),
    "Max": (
        "func Max (int:a, int:b) → int",
        "=> result >= a\n=> result >= b\n=> result == a || result == b",
        "if a >= b\n  return a\nreturn b",
        "return a >= b ? a : b",
    ),
}


def source(name, *, contracts=None, body=None, signature=None, expression=False):
    declaration, required, statement, alternative = DECLARATIONS[name]
    lines = (required if contracts is None else contracts).splitlines()
    lines += (body if body is not None else alternative if expression else statement).splitlines()
    return "module Calculator\n  " + (signature or declaration) + "\n" + "".join(
        "    " + line + "\n" for line in lines
    )


class LogicVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real verify.sh controls.")

    def verify(self, name, text, expected=True, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-logic-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(TASKS / TASK_NAMES[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                timeout=135,
            )
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                counts = {"Factorial": 6, "Clamp": 726, "IsPositive": 11, "Max": 121}
                self.assertIn(f"semantic contracts and {counts[name]} runtime cases.", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_statement_and_expression_implementations(self):
        for name in TASK_NAMES:
            for expression in (False, True):
                with self.subTest(name=name, expression=expression):
                    self.verify(name, source(name, expression=expression))

    def test_int_and_mixed_alias_signatures(self):
        for name in TASK_NAMES:
            declaration = DECLARATIONS[name][0]
            signatures = (
                declaration.replace("int", "System.Int32"),
                declaration.replace("int:", "System.Int32:", 1),
                declaration.replace("→ int", "→ System.Int32") if name != "IsPositive"
                else declaration.replace("int:", "System.Int32:"),
            )
            for signature in dict.fromkeys(signatures):
                for expression in (False, True):
                    with self.subTest(name=name, signature=signature, expression=expression):
                        self.verify(name, source(name, signature=signature, expression=expression))

    def test_equivalent_contract_forms(self):
        alternatives = {
            "Factorial": "requires 0 <= n && 5 >= n\n=> 1 <= result",
            "Clamp": "requires !(min > max)\n=> min <= result && max >= result",
            "IsPositive": "=> (!result || x > 0) && (result || x <= 0)",
            "Max": "=> a <= result && b <= result && (a == result || b == result)",
        }
        for name, contracts in alternatives.items():
            with self.subTest(name=name):
                self.verify(name, source(name, contracts=contracts, expression=True))

    def test_missing_wrong_and_comment_only_functions(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                self.verify(name, source(name).replace(f"func {name} ", "func Other "), False,
                            detail="Expected exactly one")
                self.verify(name, "// " + " ".join(DECLARATIONS[name]) + "\nmodule Calculator\n", False)
                self.verify(name, source(name, signature=DECLARATIONS[name][0].replace(f"func {name} ", f"func {name} private ")),
                            False, detail="Wrong public signature")
                self.verify(name, source(name, body="return false" if name == "IsPositive" else "return 1"), False)
                self.verify(name, source(name, body=""), False)

    def test_wrong_signatures(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                signature = DECLARATIONS[name][0].replace("int:", "long:", 1)
                self.verify(name, source(name, signature=signature), False)
        self.verify("IsPositive", source("IsPositive", signature="func IsPositive (int:x) → int",
                                        body="return 1", contracts="=> result >= 1"), False)
        self.verify("Max", source("Max", signature="func Max (int:b, int:a) → int"), False,
                    detail="Wrong public signature")

    def test_missing_each_contract_and_unrelated_contracts(self):
        for name in TASK_NAMES:
            required = DECLARATIONS[name][1].splitlines()
            for index in range(len(required)):
                with self.subTest(name=name, index=index):
                    text = source(name, contracts="\n".join(required[:index] + required[index + 1:]))
                    unrelated = (
                        "  func Unrelated (int:n, int:value, int:min, int:max, int:x, int:a, int:b) → "
                        + ("bool" if name == "IsPositive" else "int") + "\n"
                        + "".join("    " + line + "\n" for line in required)
                        + "    return " + ("x > 0" if name == "IsPositive" else "1") + "\n"
                    )
                    self.verify(name, text + unrelated, False)

    def test_weakened_and_vacuous_contracts(self):
        weak = {
            "Factorial": "requires n >= 0\nrequires n <= 5\n=> result >= 0",
            "Clamp": "requires min <= max\n=> result >= min || result <= max",
            "IsPositive": "=> !result || x > 0",
            "Max": "=> result >= a\n=> result >= b\n=> result >= a || result >= b",
        }
        for name, contracts in weak.items():
            with self.subTest(name=name):
                self.verify(name, source(name, contracts=contracts), False, detail="Missing/weakened postconditions")
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\n=> false"),
                            False, detail="Postconditions exclude")
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\nrequires false"),
                            False, detail="Missing/wrong preconditions")
        self.verify("Factorial", source("Factorial", contracts="requires n >= 0\nrequires n <= 4\n=> result >= 1"),
                    False, detail="Missing/wrong preconditions")

    def test_wrong_boundary_results(self):
        for n, wrong in ((0, 2), (1, 2), (2, 3), (3, 7), (4, 25), (5, 121)):
            with self.subTest(n=n):
                self.verify("Factorial", source("Factorial", body=f"if n == {n}\n  return {wrong}\n"
                                               + DECLARATIONS["Factorial"][2]), False, detail="expected")
        self.verify("Clamp", source("Clamp", body="return min"), False, detail="expected")
        self.verify("Clamp", source("Clamp", body="return max"), False, detail="expected")
        self.verify("IsPositive", source("IsPositive", body="return x >= 0"), False)
        self.verify("IsPositive", source("IsPositive", body="return x != 0"), False)
        self.verify("Max", source("Max", body="return a + b"), False)
        self.verify("Max", source("Max", body="if a == b\n  return 0\n" + DECLARATIONS["Max"][2]), False)
        self.verify("Max", source("Max", body="if a == 2147483647\n  return b\n"
                                + DECLARATIONS["Max"][2]), False)
        self.verify("Clamp", source("Clamp", body="if min == -2147483648\n  return max\n"
                                  + DECLARATIONS["Clamp"][2]), False)

    def test_input_compile_execution_and_timeout_failures(self):
        self.verify("Factorial", None, False, detail="Input missing")
        self.verify("Factorial", "invalid source", False, detail="Calor compilation failed")
        self.verify("Max", source("Max", body='! throw\nthrow "deliberate runtime failure"'),
                    False, detail="deliberate runtime failure")
        self.verify("Max", source("Max", body="while true\n  continue\nreturn a"),
                    False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("Max", source("Max"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")

    def test_selected_arm_has_no_parent_fallback(self):
        with tempfile.TemporaryDirectory(prefix="calor-selected-arm-") as temporary:
            arm = Path(temporary)
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            self.verify("Max", source("Max"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            mixed = source("Max", signature="func Max (int:a, System.Int32:b) → int")
            self.verify("Max", mixed, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail=str(arm / relative / "calor.dll"))
            (arm / relative / "calor.dll").write_bytes(b"not a compiler")
            self.verify("Max", source("Max"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Verifier command exited")

    def test_shared_compilation_selects_same_arm(self):
        helper = TASKS.parents[1] / "lib/helpers.sh"
        with tempfile.TemporaryDirectory(prefix="calor-runner-arm-") as temporary:
            result = subprocess.run(
                ["bash", "-c", 'source "$1"; printf "%s\\n" "$COMPILER"', "bash", str(helper)],
                env=dict(os.environ, CALOR_COMPILER_ROOT=temporary, CALOR_COMPILER_CONFIGURATION="Release"),
                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=10,
            )
            self.assertEqual(result.returncode, 0, result.stdout)
            self.assertEqual(result.stdout.strip(), f"{temporary}/src/Calor.Compiler/bin/Release/net10.0/calor")


class TimeoutWrapperTests(unittest.TestCase):
    def test_escaped_inherited_stdout_is_bounded(self):
        specification = importlib.util.spec_from_file_location("verify_logic", TASKS.parents[1] / "lib/verify-logic.py")
        verifier = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(verifier)
        with tempfile.TemporaryDirectory(prefix="calor-inherited-pipe-") as temporary:
            pid_file = Path(temporary) / "escaped.pid"
            code = (
                "import pathlib, subprocess, sys; "
                "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], "
                "start_new_session=True); "
                "pathlib.Path(sys.argv[1]).write_text(str(child.pid))"
            )
            started = time.monotonic()
            try:
                with self.assertRaisesRegex(RuntimeError, "Verifier timeout") as raised:
                    verifier.run([sys.executable, "-c", code, str(pid_file)], temporary, started + 1.5)
                self.assertIn("Verifier cleanup EOF timeout", "\n".join(raised.exception.__notes__))
                self.assertLess(time.monotonic() - started, 2.5)
                self.assertTrue(pid_file.is_file(), "Escaped inherited-pipe child did not start.")
            finally:
                if pid_file.is_file():
                    try:
                        os.kill(int(pid_file.read_text()), signal.SIGKILL)
                    except ProcessLookupError:
                        pass

    def test_cancellation_terminates_child_without_waiting_for_escaped_eof(self):
        with tempfile.TemporaryDirectory(prefix="calor-cancel-control-") as temporary:
            child_file = Path(temporary) / "child.pid"
            escaped_file = Path(temporary) / "escaped.pid"
            child_code = (
                "import os, pathlib, subprocess, sys, time; "
                "pathlib.Path(sys.argv[1]).write_text(str(os.getpid())); "
                "escaped = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], "
                "start_new_session=True); "
                "pathlib.Path(sys.argv[2]).write_text(str(escaped.pid)); time.sleep(60)"
            )
            wrapper_code = """
import importlib.util, sys, time
spec = importlib.util.spec_from_file_location("verify_logic", sys.argv[1])
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)
try:
    verifier.run([sys.executable, "-c", sys.argv[2], sys.argv[3], sys.argv[4]], ".", time.monotonic() + 2)
except KeyboardInterrupt:
    sys.exit(130)
"""
            wrapper = subprocess.Popen([
                sys.executable, "-c", wrapper_code, str(TASKS.parents[1] / "lib/verify-logic.py"),
                child_code, str(child_file), str(escaped_file),
            ], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, start_new_session=True)
            started = time.monotonic()
            try:
                while not escaped_file.is_file() and time.monotonic() - started < 1:
                    time.sleep(0.01)
                self.assertTrue(escaped_file.is_file(), "Cancellation child did not start.")
                wrapper.send_signal(signal.SIGINT)
                _, error = wrapper.communicate(timeout=3)
                self.assertEqual(wrapper.returncode, 130, error)
                self.assertIn("Verifier cleanup EOF timeout", error)
                self.assertLess(time.monotonic() - started, 3)
                with self.assertRaises(ProcessLookupError):
                    os.kill(int(child_file.read_text()), 0)
            finally:
                if wrapper.poll() is None:
                    wrapper.kill()
                    wrapper.communicate(timeout=1)
                for path in (child_file, escaped_file):
                    if path.is_file():
                        try:
                            os.kill(int(path.read_text()), signal.SIGKILL)
                        except ProcessLookupError:
                            pass


if __name__ == "__main__":
    unittest.main(verbosity=2)
