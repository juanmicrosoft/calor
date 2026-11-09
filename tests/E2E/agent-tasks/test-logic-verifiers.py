#!/usr/bin/env python3
"""Executable controls for the real logic-task verify.sh entry points."""

import os
import importlib.util
import re
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
SPECIFICATION = importlib.util.spec_from_file_location(
    "scalar_shared_admission", Path(__file__).with_name("test-advanced-contract-verifiers.py"))
SHARED = importlib.util.module_from_spec(SPECIFICATION)
SPECIFICATION.loader.exec_module(SHARED)
ENGINE = SHARED.ENGINE
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


def native_rejection(name, category):
    prefix = r"System\.InvalidOperationException: "
    messages = {
        "function": f"Expected exactly one module function named {name}.",
        "signature": f"Wrong public signature for {name}.",
        "body": f"Logic native return body missing for {name}.",
        "entry": "Missing/wrong preconditions: SATISFIABLE;",
        "post": "Missing/weakened postconditions: SATISFIABLE;",
        "output": "Postconditions exclude a required output: SATISFIABLE;",
        "overflow": "Logic executable overflow for Max(-2147483648, -2147483648).",
    }
    if category == "behavior":
        return prefix + rf"(?:Logic native Ensures rejected for {name}\.|{name}\([^\n]* returned [^\n]*; expected )"
    if category == "execution":
        return (r"System\.Reflection\.TargetInvocationException: Exception has been thrown by the target of an invocation\."
                r"\s+---> System\.Exception: deliberate runtime failure\n")
    return prefix + re.escape(messages[category])


def finite_payload(output, *, build=False):
    prefix = "Verifier command exited 1:\n"
    if not output.startswith(prefix):
        raise RuntimeError("Missing canonical exit-1 logic wrapper.")
    payload = output.removeprefix(prefix)
    nested = 0
    while payload.startswith("Verification failed: Verifier command exited "):
        prefix = "Verification failed: Verifier command exited 1:\n"
        if not payload.startswith(prefix):
            raise RuntimeError("Abnormal nested logic exit.")
        payload = payload.removeprefix(prefix)
        nested += 1
    if not payload.startswith("Verification failed: ") and not (build and nested):
        raise RuntimeError("Missing canonical logic failure.")
    payload = payload.removeprefix("Verification failed: ")
    if re.search(r"Verifier\s+command\s+exited|Verification\s+(?:passed|failed)\b", payload, re.IGNORECASE):
        raise RuntimeError("Displaced or forged logic failure wrapper.")
    return payload


def prerequisite_refusal(error, compiler, detail):
    payload = finite_payload(str(error), build=detail == "Verifier command exited")
    if detail == "Selected compiler dependency missing":
        if compiler.exists() or payload != f"Selected compiler dependency missing: {compiler}; build the selected root first.\n":
            raise error
    elif detail == "Verifier command exited":
        if compiler.read_bytes() != b"not a compiler":
            raise error
        diagnostic = (
            r"[^\n]*Program\.cs\(\d+,\d+\): error (?:"
            r"CS0234: The type or namespace name 'Compiler' does not exist in the namespace 'Calor' "
            r"\(are you missing an assembly reference\?\)|"
            r"CS0246: The type or namespace name '(?:ExpressionNode|ReferenceNode|BindStatementNode|"
            r"StatementNode|AstNode|FunctionNode|ModuleNode)' could not be found "
            r"\(are you missing a using directive or an assembly reference\?\))"
            r" \[[^\n]*LogicVerifier\.csproj\]"
        )
        lines = [line.strip() for line in payload.splitlines() if line.strip()]
        if not any(re.fullmatch(diagnostic, line) for line in lines) or any(
            not (re.fullmatch(diagnostic, line) or re.fullmatch(
                r"Build FAILED\.|0 Warning\(s\)|22 Error\(s\)|Time Elapsed \d{2}:\d{2}:\d{2}\.\d+", line))
            for line in lines
        ):
            raise error
    else:
        raise error
    return str(error)


class LogicVerifierTests(unittest.TestCase):
    entry_points_executed = 0
    counts = None
    require_full_totals = False

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real verify.sh controls.")
        print("Logic accepted/finite/compile/input/prerequisite/timeout census:", cls.counts)
        if cls.require_full_totals:
            if cls.entry_points_executed != 103 or cls.counts != {
                "accepted": 33, "finite": 64, "compile": 1,
                "input": 1, "prerequisite": 2, "timeout": 2,
            }:
                raise AssertionError("Original nonempty 103-control logic census did not complete.")

    def verify(self, name, text, expected=True, env=None, detail=None, *,
               rejection=None, kind="finite", real=True):
        if kind not in ("finite", "compile", "input", "prerequisite"):
            raise ValueError("Unknown logic admission category.")
        with tempfile.TemporaryDirectory(prefix="calor-logic-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            root = Path(configuration["CALOR_COMPILER_ROOT"]).resolve()
            compiler = root / "src/Calor.Compiler/bin" / configuration.get(
                "CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0/calor.dll"
            command = [str(TASKS / TASK_NAMES[name] / "verify.sh"), str(workspace)]
            declared = float(configuration["CALOR_LOGIC_TIMEOUT_SECONDS"]) if detail == "timeout" else None
            if declared is not None and (expected or declared not in (10, 0.001)):
                raise ValueError("Only the two original bounded logic timeout controls are declared.")
            started = time.monotonic()
            if real:
                type(self).entry_points_executed += 1
            try:
                output = ENGINE.run(command, TASKS, started + 135, env=configuration)
                passed = True
            except RuntimeError as error:
                passed = False
                if declared == 0.001 and str(error) == (
                    "Verifier command exited 1:\nVerification failed: Verifier timeout: overall deadline expired.\n"
                ) and time.monotonic() - started >= declared:
                    output = str(error)
                    kind = "timeout"
                elif kind == "prerequisite":
                    output = prerequisite_refusal(error, compiler, detail)
                else:
                    output = SHARED.classify(error, timeout_seconds=declared, commands=("dotnet",))
                    kind = "timeout" if declared is not None else kind
                    if kind != "timeout":
                        payload = finite_payload(output)
                        if kind == "input":
                            if text is not None or payload != f"Input missing: {workspace.resolve() / 'Calculator.calr'}\n":
                                raise RuntimeError("Not the original missing-input guard.") from error
                        elif kind == "compile":
                            if text != "invalid source" or not re.match(
                                r"System\.InvalidOperationException: Calor compilation failed:\n"
                                + re.escape(str(workspace.resolve() / "Calculator.calr"))
                                + r":1:1 Calor0100 Expected Module but found Identifier\n", payload
                            ):
                                raise RuntimeError("Not the original native compile-invalid guard.") from error
                        elif rejection is None or not re.match(rejection, payload):
                            raise RuntimeError("Logic rejection did not match its specific native oracle:\n" + output) from error
            identities = [line for line in output.splitlines()
                          if re.search(r"Selected\s+compiler\b", line, re.IGNORECASE)
                          and not (kind == "prerequisite" and line ==
                                   f"Verification failed: Selected compiler dependency missing: {compiler}; build the selected root first.")]
            identity = f"Selected compiler: {compiler}"
            if identities and identities != [identity]:
                raise RuntimeError("Noncanonical or duplicated logic compiler identity.")
            self.assertEqual(passed, expected, output)
            if expected:
                ENGINE.check_advanced_success(output, identity)
                counts = {"Factorial": 6, "Clamp": 726, "IsPositive": 11, "Max": 121}
                verdict = f"Verification passed: {name}; semantic contracts and {counts[name]} runtime cases."
                self.assertEqual([line for line in output.splitlines() if line.startswith("Verification passed: ")],
                                 [verdict], output)
            if detail:
                self.assertIn(detail, output)
            if real:
                cls = type(self)
                if cls.counts is None:
                    cls.counts = dict.fromkeys(("accepted", "finite", "compile", "input", "prerequisite", "timeout"), 0)
                cls.counts["accepted" if expected else kind] += 1
            return output

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
                            detail="Expected exactly one", rejection=native_rejection(name, "function"))
                self.verify(name, "// " + " ".join(DECLARATIONS[name]) + "\nmodule Calculator\n", False,
                            rejection=native_rejection(name, "function"))
                self.verify(name, source(name, signature=DECLARATIONS[name][0].replace(f"func {name} ", f"func {name} private ")),
                            False, detail="Wrong public signature", rejection=native_rejection(name, "signature"))
                self.verify(name, source(name, body="return false" if name == "IsPositive" else "return 1"), False,
                            rejection=native_rejection(name, "behavior"))
                self.verify(name, source(name, body=""), False, rejection=native_rejection(name, "body"))

    def test_wrong_signatures(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                signature = DECLARATIONS[name][0].replace("int:", "long:", 1)
                self.verify(name, source(name, signature=signature), False, rejection=native_rejection(name, "signature"))
        self.verify("IsPositive", source("IsPositive", signature="func IsPositive (int:x) → int",
                                        body="return 1", contracts="=> result >= 1"), False,
                    rejection=native_rejection("IsPositive", "signature"))
        self.verify("Max", source("Max", signature="func Max (int:b, int:a) → int"), False,
                    detail="Wrong public signature", rejection=native_rejection("Max", "signature"))

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
                    self.verify(name, text + unrelated, False, rejection=native_rejection(
                        name, "entry" if required[index].startswith("requires ") else "post"))

    def test_weakened_and_vacuous_contracts(self):
        weak = {
            "Factorial": "requires n >= 0\nrequires n <= 5\n=> result >= 0",
            "Clamp": "requires min <= max\n=> result >= min || result <= max",
            "IsPositive": "=> !result || x > 0",
            "Max": "=> result >= a\n=> result >= b\n=> result >= a || result >= b",
        }
        for name, contracts in weak.items():
            with self.subTest(name=name):
                self.verify(name, source(name, contracts=contracts), False, detail="Missing/weakened postconditions",
                            rejection=native_rejection(name, "post"))
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\n=> false"),
                            False, detail="Postconditions exclude", rejection=native_rejection(name, "output"))
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\nrequires false"),
                            False, detail="Missing/wrong preconditions", rejection=native_rejection(name, "entry"))
        self.verify("Factorial", source("Factorial", contracts="requires n >= 0\nrequires n <= 4\n=> result >= 1"),
                    False, detail="Missing/wrong preconditions", rejection=native_rejection("Factorial", "entry"))

    def test_wrong_boundary_results(self):
        for n, wrong in ((0, 2), (1, 2), (2, 3), (3, 7), (4, 25), (5, 121)):
            with self.subTest(n=n):
                self.verify("Factorial", source("Factorial", body=f"if n == {n}\n  return {wrong}\n"
                                               + DECLARATIONS["Factorial"][2]), False, detail="expected",
                            rejection=native_rejection("Factorial", "behavior"))
        self.verify("Clamp", source("Clamp", body="return min"), False, detail="expected",
                    rejection=native_rejection("Clamp", "behavior"))
        self.verify("Clamp", source("Clamp", body="return max"), False, detail="expected",
                    rejection=native_rejection("Clamp", "behavior"))
        self.verify("IsPositive", source("IsPositive", body="return x >= 0"), False,
                    rejection=native_rejection("IsPositive", "behavior"))
        self.verify("IsPositive", source("IsPositive", body="return x != 0"), False,
                    rejection=native_rejection("IsPositive", "behavior"))
        self.verify("Max", source("Max", body="return a + b"), False, rejection=native_rejection("Max", "overflow"))
        self.verify("Max", source("Max", body="if a == b\n  return 0\n" + DECLARATIONS["Max"][2]), False,
                    rejection=native_rejection("Max", "behavior"))
        self.verify("Max", source("Max", body="if a == 2147483647\n  return b\n"
                                + DECLARATIONS["Max"][2]), False, rejection=native_rejection("Max", "behavior"))
        self.verify("Clamp", source("Clamp", body="if min == -2147483648\n  return max\n"
                                  + DECLARATIONS["Clamp"][2]), False, rejection=native_rejection("Clamp", "behavior"))

    def test_input_compile_execution_and_timeout_failures(self):
        self.verify("Factorial", None, False, detail="Input missing", kind="input")
        self.verify("Factorial", "invalid source", False, detail="Calor compilation failed", kind="compile")
        self.verify("Max", source("Max", body='! throw\nthrow "deliberate runtime failure"'),
                    False, detail="deliberate runtime failure", rejection=native_rejection("Max", "execution"))
        self.verify("Max", source("Max", body="while true\n  continue\nreturn a"),
                    False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("Max", source("Max"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")

    def test_selected_arm_has_no_parent_fallback(self):
        with tempfile.TemporaryDirectory(prefix="calor-selected-arm-") as temporary:
            arm = Path(temporary)
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            self.verify("Max", source("Max"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing",
                        kind="prerequisite")
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            shutil.copytree(COMPILER_ROOT / ".github", arm / ".github")
            for assets in ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes"):
                shutil.copytree(COMPILER_ROOT / assets, arm / assets)
            mixed = source("Max", signature="func Max (int:a, System.Int32:b) → int")
            self.verify("Max", mixed, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail=str(arm / relative / "calor.dll"))
            (arm / relative / "calor.dll").write_bytes(b"not a compiler")
            self.verify("Max", source("Max"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Verifier command exited", kind="prerequisite")

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
    deadline = time.monotonic() + 600
    print(ENGINE.run([sys.executable, "scripts/verify-z3-assets.py"], COMPILER_ROOT, deadline), end="")
    print(ENGINE.run([
        "dotnet", "restore", "src/Calor.Compiler/Calor.Compiler.csproj", "--locked-mode",
        "--source", str(Path.home() / ".nuget/packages"), "--disable-build-servers",
        "-p:UseSharedCompilation=false", "-nodeReuse:false", "-v", "quiet",
    ], COMPILER_ROOT, deadline), end="")
    print(ENGINE.run([
        "dotnet", "build", "src/Calor.Compiler/Calor.Compiler.csproj", "--no-restore", "-t:Rebuild",
        "-c", os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug"), "--disable-build-servers",
        "-p:UseSharedCompilation=false", "-nodeReuse:false", "--nologo", "-v", "quiet",
    ], COMPILER_ROOT, deadline), end="")
    LogicVerifierTests.require_full_totals = not any(not argument.startswith("-") for argument in sys.argv[1:])
    unittest.main(verbosity=2)
