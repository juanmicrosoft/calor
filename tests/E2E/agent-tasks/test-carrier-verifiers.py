#!/usr/bin/env python3
"""Bounded original type-001..004 controls; no agent or fixture rewriting."""

import importlib.util
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, patch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {
    "TryDouble": "01_option_some_return", "GetNothing": "02_option_none_return",
    "SafeDivide": "03_result_ok_return", "AlwaysFail": "04_result_err_return",
}
COUNTS = {"TryDouble": 137, "GetNothing": 8, "SafeDivide": 399, "AlwaysFail": 8}


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def source(name, api=False):
    option = name in ("TryDouble", "GetNothing")
    carrier = "Calculator.Option<i32>" if option else "Calculator.Result<i32,str>"
    signature = {"TryDouble": "(i32:x)", "GetNothing": "()",
                 "SafeDivide": "(i32:a, i32:b)", "AlwaysFail": "()"}[name]
    tag = "IsSome" if option else "IsOk"
    fields = f"""  §CL{{{'Option' if option else 'Result'}:pub}}<{'T' if option else 'T,E'}>
    §FLD{{bool:{tag}:pub}}
    §FLD{{?T:Value:pub}}
"""
    if not option:
        fields += "    §FLD{?E:Error:pub}\n"
    if api:
        fields += f"    §PROP{{{'IsNone' if option else 'IsErr'}:bool:pub}}\n" \
                  f"      §GET\n        §R (! {tag})\n"
        fields += """    §MT{Unwrap:pub} () -> ?T
      §R Value
"""
        if not option:
            fields += """    §MT{UnwrapErr:pub} () -> ?E
      §R Error
"""
    body = f"    §B{{result}} §NEW{{{carrier.removeprefix('Calculator.')}}}\n"
    if name == "TryDouble":
        body += """    §ASSIGN result.IsSome (> x 0)
    §IF (> x 0)
      §ASSIGN result.Value (* x 2)
"""
    elif name == "SafeDivide":
        body += """    §ASSIGN result.IsOk (!= b 0)
    §IF (!= b 0)
      §ASSIGN result.Value (/ a b)
    §EL
      §ASSIGN result.Error "Division by zero"
"""
    elif name == "AlwaysFail":
        body += '    §ASSIGN result.Error "Operation not supported"\n'
    return f"§M{{Calculator}}\n{fields}  §F{{{name}:pub}} {signature} -> {carrier}\n" \
           f"    §E{{alloc,mut}}\n{body}    §R result\n"


def historical_syntax(text):
    result = []
    for serial, line in enumerate(text.splitlines()):
        if line.lstrip().startswith(("§M{", "§F{", "§CL{", "§MT{", "§PROP{")):
            line = line.replace("{", f"{{id{serial}:", 1)
        if line.lstrip().startswith("§IF "):
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        result.append(line)
    return "\n".join(result) + "\n"


def constructors(name):
    signature = {"TryDouble": "(i32:x)", "GetNothing": "()",
                 "SafeDivide": "(i32:a, i32:b)", "AlwaysFail": "()"}[name]
    carrier = "Option<i32>" if name in ("TryDouble", "GetNothing") else "Result<i32,str>"
    body = {
        "TryDouble": "    §IF (> x 0)\n      §R §SM (* x 2)\n    §R §NN{i32}\n",
        "GetNothing": "    §R §NN{i32}\n",
        "SafeDivide": '    §IF (!= b 0)\n      §R §OK (/ a b)\n    §R §ERR "Division by zero"\n',
        "AlwaysFail": '    §R §ERR "Operation not supported"\n',
    }[name]
    return f"§M{{Calculator}}\n  §F{{{name}:pub}} {signature} -> {carrier}\n{body}"


def cases(name):
    text = source(name)
    option = name in ("TryDouble", "GetNothing")
    tag = "IsSome" if option else "IsOk"
    opposite = "IsNone" if option else "IsErr"
    result = [
        (text, True, None),
        (source(name, api=True), True, None),
        (text.replace("i32", "int").replace("str", "string"), True, None),
        (text.replace(name + ":pub", name + ":priv"), False, "signature"),
        (text.replace(name + ":pub", "Other:pub"), False, "Expected exactly one native"),
        ("// " + text.replace("\n", " ") + "\n§M{Calculator}\n", False, "Expected exactly one native"),
        (text.replace(f"bool:{tag}", "bool:IsSuccess").replace(f"result.{tag}", "result.IsSuccess"),
         False, "Missing authoritative"),
        (text.replace(f"bool:{tag}", f"i32:{tag}").replace(f"result.{tag} (> x 0)", f"result.{tag} 1")
             .replace(f"result.{tag} (!= b 0)", f"result.{tag} 1"), False, "shape/type"),
        (text.replace("?T:Value", "?i64:Value"), False, "shape/type"),
        (text.replace(f"bool:{tag}:pub", f"bool:{tag}:priv"), False, None),
        (text.replace("    §E{alloc,mut}", '    §E{alloc,mut}\n    §Q (== (len "x") 0)'),
         False, "Precondition failed"),
        (text.replace("    §E{alloc,mut}", '    §E{alloc,mut}\n    §S (== (len "x") 0)'),
         False, "Postcondition failed"),
        (text.replace(f"    §FLD{{bool:{tag}:pub}}",
                      f"    §FLD{{bool:{tag}:pub}}\n    §FLD{{bool:{opposite}:pub}}"),
         False, "contradictory"),
        (text.replace(f"    §FLD{{bool:{tag}:pub}}",
                      f"    §FLD{{bool:{tag}:pub}}\n    §FLD{{bool:IsSuccess:pub}} = true"),
         True, None),
        (text.replace("    §R result", "    §R null"), False, "CS8603"),
        (text.replace("§E{alloc,mut}", "§E{alloc,mut,throw}")
             .replace("    §R result", "    §TH STR:\"carrier failure\""), False, "carrier failure"),
        (text.replace(f"    §FLD{{bool:{tag}:pub}}", f"    §FLD{{bool:caseTag:pub}}\n"
                      f"    §PROP{{{tag}:bool:pub}}\n      §GET\n        §R caseTag")
             .replace(f"result.{tag}", "result.caseTag"), True, None),
        (source(name, api=True).replace("    §MT{Unwrap:pub} () -> ?T\n      §R Value",
                                       "    §MT{Unwrap:pub} () -> ?T\n      §R default"),
         name != "TryDouble" and name != "SafeDivide", None if option and name == "GetNothing"
         or name == "AlwaysFail" else "payload"),
    ]
    if name == "TryDouble":
        result += [
            (text.replace("(* x 2)", "(+ x 1)"), False, "payload"),
            (text.replace("(> x 0)", "(>= x 0)"), False, "case"),
            (text.replace("result.IsSome (> x 0)", "result.IsSome (< x 0)"), False, "case"),
            (text.replace("    §E{alloc,mut}", "    §E{alloc,mut}\n    §Q (> x 0)"), False, "Precondition failed"),
            (text.replace("(* x 2)", "(* x 3)"), False, "payload"),
            (text.replace("i32:x", "i64:x").replace("(* x 2)", "0"), False, "signature"),
            (text.replace("(* x 2)", "§IF (== x 1073741823) -> 0 §EL -> (* x 2)"), False, "payload"),
        ]
    elif name == "SafeDivide":
        result += [
            (text.replace("(/ a b)", "(% a b)"), False, "payload"),
            (text.replace('"Division by zero"', '"division by zero"'), False, "payload"),
            (text.replace("(!= b 0)", "(> b 0)"), False, "case"),
            (text.replace("    §E{alloc,mut}", "    §E{alloc,mut}\n    §Q (!= b 0)"), False, "Precondition failed"),
            (text.replace("?E:Error", "?str:Error"), False, "shape/type"),
            (text.replace("i32:b", "i64:b").replace("(/ a b)", "0"), False, "signature"),
            (text.replace("(/ a b)", "0"), False, "payload"),
        ]
    elif name == "GetNothing":
        result.append((text.replace("    §R result", "    §ASSIGN result.IsSome true\n    §R result"),
                       False, "case"))
    else:
        result += [
            (text.replace('"Operation not supported"', '"unsupported"'), False, "payload"),
            (text.replace("    §R result", "    §ASSIGN result.IsOk true\n    §R result"), False, "case"),
            (text.replace("?E:Error", "?str:Error"), False, "shape/type"),
        ]
    return result


class CarrierVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        if cls.entry_points_executed != 137:
            raise RuntimeError(f"Expected exactly 137 current carrier entrypoints, observed {cls.entry_points_executed}.")
        print(f"Executed {cls.entry_points_executed} actual carrier verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None, deadline=None, expect_timeout=False):
        if expect_timeout and expected:
            raise ValueError("A bounded-timeout control must expect rejection.")
        helper = load("carrier_process", HERE / "lib/verify-logic.py")
        with tempfile.TemporaryDirectory(prefix="calor-carrier-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            entry_deadline = min(time.monotonic() + 120, deadline or float("inf"))
            process_deadline = min(time.monotonic() + 120, entry_deadline)
            environment["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(
                float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120")),
                process_deadline - time.monotonic()))
            try:
                output = helper.run([str(HERE / "tasks/type-system" / TASKS[name] / "verify.sh"), str(workspace)],
                                    HERE, entry_deadline, env=environment)
                passed = True
            except RuntimeError as error:
                output = str(error)
                timed_out = "Verifier timeout" in output
                if expected or "Verifier cleanup timeout" in output or "Cannot terminate verifier process group" in output:
                    raise
                if expect_timeout:
                    if not timed_out:
                        raise
                elif timed_out or not output.startswith("Verifier command exited"):
                    raise
                passed = False
            type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if expected:
                selected = Path(environment["CALOR_COMPILER_ROOT"]).resolve() / "src/Calor.Compiler/bin" \
                    / environment.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0/calor.dll"
                self.assertEqual(
                    [line for line in output.splitlines() if line.startswith("Selected compiler:")],
                    [f"Selected compiler: {selected}"], output)
                self.assertIn(f"Verification passed: {name}; native/reflected closed CLR", output)
                self.assertIn(f"{COUNTS[name]} runtime cases.", output)
                self.assertIn("optional contracts execute", output)
            if detail:
                self.assertIn(detail, output)
            return output

    def test_native_carrier_cases(self):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                with self.subTest(name=name, index=index):
                    self.verify(name, text, expected, detail=detail)

    def test_missing_raw_duplicates_and_retired_constructors(self):
        for name in TASKS:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "invalid", False, detail="Calor compilation failed")
            self.verify(name, source(name) + source(name).split("\n", 1)[1], False)
            self.verify(name, source(name) + "  §CSHARP{public class Decoy { }}§/CSHARP\n",
                        False, detail="Raw interop")
            self.verify(name, constructors(name), False, detail="Calor0006")

    def test_equivalent_observations_and_wrong_return_identity(self):
        for name in TASKS:
            text = source(name)
            tag, opposite = ("IsSome", "IsNone") if name in ("TryDouble", "GetNothing") else ("IsOk", "IsErr")
            self.verify(name, text.replace(tag, opposite).replace(
                "    §R result", f"    §ASSIGN result.{opposite} (! result.{opposite})\n    §R result"))
            self.verify(name, source(name, api=True).replace("Value", "Payload").replace("Error", "Failure"))
            carrier = "Calculator.Option<i32>" if name in ("TryDouble", "GetNothing") else "Calculator.Result<i32,str>"
            self.verify(name, text.replace("-> " + carrier, "-> i32").replace("§R result", "§R 1"),
                        False, detail="signature")

    def test_bounds_and_selected_image(self):
        self.verify("TryDouble", source("TryDouble").replace("    §R result",
                    "    §WH (>= x -2147483648)\n      §CN\n    §R result"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout", expect_timeout=True)
        self.verify("GetNothing", source("GetNothing"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout", expect_timeout=True)
        self.verify("GetNothing", source("GetNothing").replace("§E{alloc,mut}", "§E{alloc,mut,cw}")
                    .replace("    §R result", "    §L{i:1:5000:1}\n      §P i\n    §R result"),
                    False, detail="Excessive stdout")
        self.verify("GetNothing", source("GetNothing"), False,
                    env={"CALOR_COMPILER_ROOT": ""}, detail="CALOR_COMPILER_ROOT")
        for configuration in ("", "..", "../Release"):
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_CONFIGURATION": configuration},
                        detail="CALOR_COMPILER_CONFIGURATION")
        with tempfile.TemporaryDirectory(prefix="calor-carrier-selected-") as temporary:
            arm = Path(temporary)
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            (arm / ".github").mkdir()
            shutil.copyfile(COMPILER_ROOT / ".github/z3-binaries-4.15.7.sha256",
                            arm / ".github/z3-binaries-4.15.7.sha256")
            publish = arm / relative / "publish"
            publish.mkdir(exist_ok=True)
            (publish / "unreferenced.dll").write_bytes(b"unreferenced publish artifact")
            self.verify("GetNothing", source("GetNothing"), env={"CALOR_COMPILER_ROOT": str(arm)})
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"corrupt native asset")
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected native manifest mismatch")
            foreign = COMPILER_ROOT / "src/Calor.Compiler/runtimes" / (
                "linux-arm64/native/libz3.so" if sys.platform == "win32" else "win-x64/native/libz3.dll")
            shutil.copyfile(foreign, arm / relative / native)
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected native manifest mismatch")
            (arm / relative / native).unlink()
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            shutil.copyfile(COMPILER_ROOT / relative / native, arm / relative / native)
            managed = arm / relative / "Microsoft.Z3.dll"
            managed.write_bytes(b"corrupt managed asset")
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected native manifest mismatch")
            shutil.copyfile(COMPILER_ROOT / relative / "Microsoft.Z3.dll", managed)
            (arm / ".github/z3-binaries-4.15.7.sha256").unlink()
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="z3-binaries-4.15.7.sha256")
            shutil.copyfile(COMPILER_ROOT / ".github/z3-binaries-4.15.7.sha256",
                            arm / ".github/z3-binaries-4.15.7.sha256")
            shutil.copyfile(arm / relative / "Calor.Runtime.dll", arm / relative / "calor.dll")
            self.verify("GetNothing", source("GetNothing"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Verifier command exited")

    def test_immutable_selected_images(self):
        helper = load("carrier_snapshot", HERE / "lib/verify-logic.py")
        native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
        for filename in ("calor.dll", native):
            with self.subTest(image=filename), tempfile.TemporaryDirectory(prefix="calor-carrier-image-") as temporary:
                arm = Path(temporary)
                relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
                shutil.copytree(COMPILER_ROOT / relative, arm / relative)
                (arm / ".github").mkdir()
                shutil.copyfile(COMPILER_ROOT / ".github/z3-binaries-4.15.7.sha256",
                                arm / ".github/z3-binaries-4.15.7.sha256")
                (arm / "Calculator.calr").write_text(source("GetNothing"), encoding="utf-8")
                run = helper.run

                def substitute(command, cwd, deadline, env=None):
                    output = run(command, cwd, deadline, env=env)
                    image = arm / relative / filename
                    image.write_bytes(image.read_bytes() + b"substitution")
                    return output

                with patch.dict(os.environ, {"CALOR_COMPILER_ROOT": str(arm)}), \
                        patch.object(sys, "argv", ["verify-logic.py", "type002", str(arm)]), \
                        patch.object(helper, "run", side_effect=substitute), \
                        self.assertRaisesRegex(RuntimeError, "image changed or was substituted"):
                    helper.main()
        print("Executed 2 selected-image substitution controls with actual helper builds.")


class CarrierControlSupervisionTests(unittest.TestCase):
    def test_finite_controls_never_accept_supervision_failures(self):
        errors = [
            "Verifier timeout: overall deadline expired.",
            "Verifier command exited 1:\nVerification failed: Verifier timeout after 120s: dotnet",
            "Verifier cleanup timeout: child did not exit by the deadline.",
            "Verifier command exited 1:\nVerification failed: Cannot terminate verifier process group 123",
            "Unexpected process/EOF supervision failure",
        ]
        helper = Mock()
        with patch.dict(CarrierVerifierTests.verify.__globals__, {"load": lambda *_: helper}), \
                patch.object(CarrierVerifierTests, "entry_points_executed", 0):
            for expected in (True, False):
                for message in errors:
                    with self.subTest(expected=expected, error=message):
                        error = RuntimeError(message)
                        helper.run.side_effect = error
                        with self.assertRaises(RuntimeError) as raised:
                            CarrierVerifierTests().verify("GetNothing", source("GetNothing"), expected)
                        self.assertIs(raised.exception, error)
            helper.run.side_effect = RuntimeError("Verifier command exited 1:\nwrong carrier case")
            CarrierVerifierTests().verify("GetNothing", source("GetNothing"), False)
            for message in errors[:2]:
                helper.run.side_effect = RuntimeError(message)
                CarrierVerifierTests().verify("GetNothing", source("GetNothing"), False, expect_timeout=True)
            for message in errors[2:] + ["Verifier command exited 1:\nwrong carrier case"]:
                helper.run.side_effect = RuntimeError(message)
                with self.assertRaises(RuntimeError):
                    CarrierVerifierTests().verify("GetNothing", source("GetNothing"), False, expect_timeout=True)

    def test_every_success_requires_exact_selected_root_and_configuration(self):
        helper = Mock()
        root = Path(tempfile.gettempdir()) / "carrier selected root"
        selected = root.resolve() / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
        success = "Verification passed: GetNothing; native/reflected closed CLR Option identity; " \
                  "8 runtime cases. Limits: optional contracts execute.\n"
        environment = {"CALOR_COMPILER_ROOT": str(root / ".." / root.name),
                       "CALOR_COMPILER_CONFIGURATION": "Release"}
        with patch.dict(CarrierVerifierTests.verify.__globals__, {"load": lambda *_: helper}), \
                patch.object(CarrierVerifierTests, "entry_points_executed", 0):
            helper.run.return_value = f"Selected compiler: {selected}\n" + success
            CarrierVerifierTests().verify("GetNothing", source("GetNothing"), env=environment)
            for identity in ("", f"Selected compiler: {selected.parents[2] / 'Debug/net10.0/calor.dll'}\n",
                             f"Selected compiler: {root.resolve() / 'foreign/src/Calor.Compiler/bin/Release/net10.0/calor.dll'}\n",
                             f"Selected compiler: {selected}\nSelected compiler: {selected}\n"):
                helper.run.return_value = identity + success
                with self.subTest(identity=identity), self.assertRaises(AssertionError):
                    CarrierVerifierTests().verify("GetNothing", source("GetNothing"), env=environment)


def run_compatibility(old, current, deadline):
    test = CarrierVerifierTests()
    selected = {}
    for name in TASKS:
        controls = cases(name)
        selected[name] = [controls[index] for index in (0, 1, 2, 3, 6, 8, 10, 11, 12, 16, 17, 18)]
        selected[name] += controls[19:21]
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(selected[name]):
                output = test.verify(name, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root)}, detail=detail, deadline=deadline)
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Carrier compatibility selected the wrong compiler.")
                print(f"{arm} {name} {index}: {'passed' if expected else 'rejected'}")
            text = constructors(name)
            test.verify(name, historical_syntax(text) if historical else text, historical,
                        env={"CALOR_COMPILER_ROOT": str(root)},
                        detail=None if historical else "Calor0006", deadline=deadline)
            print(f"{arm} {name}: original constructor {'passed' if historical else 'rejected'}")
    expected_count = 2 * sum(len(selected[name]) + 1 for name in TASKS)
    if expected_count != 116 or CarrierVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected exactly {expected_count} actual old/current carrier controls.")
    print(f"Compatibility passed: {expected_count} actual old/current carrier entry-point controls.")


if __name__ == "__main__":
    unittest.main()
