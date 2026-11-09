#!/usr/bin/env python3
"""Six existing OOP entry points; native disposable controls, never agents."""

import importlib.util
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {"Person": "01_class_definition", "IRepository": "02_interface_definition",
         "Calculator": "03_method_in_class", "Product": "04_property",
         "Counter": "05_field_declaration", "Point": "06_constructor"}
specification = importlib.util.spec_from_file_location("oop_process", HERE / "lib/verify-logic.py")
PROCESS = importlib.util.module_from_spec(specification)
specification.loader.exec_module(PROCESS)

# Keep the effects/async finite-admission boundary local to this test unit.
INFRASTRUCTURE = re.compile(
    r"\b(?:EOF|EOFError|cleanup|OSError|FileNotFoundError|PermissionError|TimeoutError|Traceback|errno"
    r"|DllNotFoundException|BadImageFormatException|Win32Exception|Z3Exception)\b"
    r"|inherited (?:pipe|stdout|stderr)|broken pipe|cannot terminate|process[ -]group"
    r"|permission denied|no such file|(?:failed|unable) to (?:launch|start)|could not (?:launch|start)"
    r"|launch failure|OS error|operating.system error|supervision (?:failure|failed)"
    r"|process (?:failure|failed|exited|terminated)"
    r"|segmentation fault|core dumped|\baborted\b|\bsignal\s+-?\d+"
    r"|\b(?-i:UNKNOWN)\b|solver[^\n]*(?:unknown|timeout|timed out)|(?:unknown|timeout)[^\n]*solver",
    re.IGNORECASE,
)
TIMEOUT = re.compile(r"timeout|timed out|deadline", re.IGNORECASE)
ORDINARY_TIMEOUT = re.compile(
    r"Verifier timeout after ((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)s: \S+(?=\n|\Z)"
)


def control_failure(error, *, allow_timeout=False):
    output = str(error)
    wrappers = list(re.finditer(r"verifier\s+command\s+exited", output, re.IGNORECASE))
    timeouts = list(ORDINARY_TIMEOUT.finditer(output))
    if (INFRASTRUCTURE.search(output)
        or re.search(r"Selected compiler dependency missing:|CALOR_COMPILER_(?:ROOT|CONFIGURATION) must", output)
        or any(not output[wrapper.start():].startswith("Verifier command exited 1:\n") for wrapper in wrappers)
        or re.search(r"verification\s+passed\s*:", output, re.IGNORECASE)):
        raise error
    if TIMEOUT.search(output) and not (
        allow_timeout and timeouts and all(0 < float(timeout[1]) <= 120 for timeout in timeouts)
        and not TIMEOUT.search(ORDINARY_TIMEOUT.sub("", output))
    ):
        raise error
    if not output.startswith("Verifier command exited 1:\n"):
        if not (allow_timeout and ORDINARY_TIMEOUT.fullmatch(output)):
            raise error
    elif "Verification failed:" not in output:
        raise error
    return output


def assert_compiler_identity(output, environment):
    root = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
    configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    expected = f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
    identities = [line for line in output.splitlines()
                  if re.search(r"selected\s+compiler\s*:", line, re.IGNORECASE)]
    if identities != [expected]:
        raise AssertionError(f"Wrong selected compiler identity:\n{output}")


def assert_positive_output(output, environment):
    if (INFRASTRUCTURE.search(output) or TIMEOUT.search(output)
        or re.search(r"verifier\s+command\s+exited|verification\s+failed\s*:", output, re.IGNORECASE)
        or len(re.findall(r"verification\s+passed\s*:", output, re.IGNORECASE)) != 1):
        raise AssertionError(f"Infrastructure-shaped positive output:\n{output}")
    assert_compiler_identity(output, environment)


def source(name):
    bodies = {
        "Person": "  §CL{Person:pub}\n",
        "IRepository": """  §CL{Entity:pub}
  §CL{Option:pub}<T>
  §IFACE{IRepository}
    §MT{GetById} (i32:id) -> Option<Entity>
""",
        "Calculator": """  §CL{Calculator:pub}
    §MT{Add:pub} (i32:a, i32:b) -> i32
      §R (+ a b)
""",
        "Product": """  §CL{Product:pub}
    §PROP{Price:i32:pub:get,set}
""",
        "Counter": """  §CL{Counter:pub}
    §FLD{i32:_count:priv}
""",
        "Point": """  §CL{Point:pub}
    §FLD{i32:_x:priv}
    §FLD{i32:_y:priv}
    §CTOR{pub} (i32:x, i32:y)
      §ASSIGN _x x
      §ASSIGN _y y
"""
    }
    return "§M{Domain}\n" + bodies[name]


def historical_syntax(text):
    lines = []
    for serial, line in enumerate(text.splitlines()):
        extra = []
        if " | " in line and line.lstrip().startswith(("§MT{", "§CTOR{")):
            header, signature = line.split(" (", 1)
            parameters, _, output = signature.rpartition(")")
            indent = " " * (len(line) - len(line.lstrip()) + 2)
            for parameter in parameters.split(", "):
                kind, _, predicate = parameter.partition(" | ")
                extra.append(indent + f"§I{{{kind}}}" + (" | " + predicate if predicate else ""))
            if output.startswith(" -> "):
                extra.append(indent + "§O{" + output[4:] + "}")
            line = header
        if line.lstrip().startswith(("§M{", "§CL{", "§IFACE{", "§MT{", "§PROP{", "§CTOR{")):
            line = line.replace("{", f"{{id{serial}:", 1)
        if line.lstrip().startswith("§IF "):
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        lines.append(line)
        lines.extend(extra)
    return "\n".join(lines) + "\n"


def cases(name):
    text = source(name)
    result = [(text, True, None)]
    if name != "IRepository":
        result += [
            (text.replace(name + ":pub", name + ":int", 1), False, None),
            (text.replace(name + ":pub", "Other:pub", 1), False, None),
            (text.replace(name + ":pub", name + ":pub}<T", 1), False, None),
        ]
    else:
        result += [
            (text.replace("IRepository}", "Other}"), False, None),
            (text.replace("IRepository}", "IRepository}<T>"), False, None),
            (text.replace("i32:id", "int:id"), True, None),
            (text.replace("i32:id", "i32:key"), False, "native GetById signature"),
            (text.replace("i32:id", "i64:id"), False, None),
            (text.replace("Option<Entity>", "?Entity"), False, "exact Option<Entity>"),
            (text.replace("Option<Entity>", "Entity"), False, "exact Option<Entity>"),
            (text.replace("Option<Entity>", "Option<i32>"), False, "exact Option<Entity>"),
            (text.replace("GetById}", "Other}"), False, None),
            (text + "      §R null\n", False, None),
            (text + "      §Q (> id 0)\n", False, "exclude prompt inputs"),
            (text + "      §Q (== id id)\n", True, None),
            (text.replace("  §CL{Entity:pub}\n", ""), False, None),
            (text.replace("i32:id", "i32:id | (> # 0)"), False, "exclude prompt inputs"),
            (text.replace("i32:id", "i32:id | (>= # INT:-2147483648)"), True, None),
            (text + "    §MT{GetById} (str:key) -> Option<Entity>\n", True, None),
        ]
    if name == "Person":
        result += [
            (text.replace("Person:pub", "Person:pub:sealed"), True, None),
            (text + "    §FLD{i32:unrelated:priv}\n", False, "no source members"),
            (text + "    §CTOR{pub} ()\n", False, "no source members"),
            (text + "    §MT{Other:pub} () -> i32\n      §R 0\n", False, "no source members"),
            (text + "    §PROP{Other:i32:pub:get}\n", False, "no source members"),
            (text + "    §CL{Nested:pub}\n", False, "no source members"),
        ]
    elif name == "Calculator":
        result += [
            (text.replace("i32", "int"), True, None),
            (text.replace("      §R (+ a b)", "      §B{sum:i32} (+ a b)\n      §R sum"), True, None),
            (text.replace("Add:pub", "Add:pub:static"), True, None),
            (text.replace("Add:pub", "Add:priv"), False, None),
            (text.replace("Add:pub", "Other:pub"), False, None),
            (text.replace("i32:a", "i32:x").replace("(+ a b)", "(+ x b)"), False, None),
            (text.replace("i32:b", "i64:b").replace("§R (+ a b)", "§R 0"), False, None),
            (text.replace("-> i32", "-> i64"), False, None),
            (text.replace("§R (+ a b)", "§R 0"), False, "expected"),
            (text.replace("§R (+ a b)", "§R (- a b)"), False, None),
            (text.replace("      §R", "      §Q (>= a 0)\n      §R"), False, "exclude prompt inputs"),
            (text.replace("      §R", "      §Q (== a a)\n      §R"), True, None),
            (text.replace("      §R", "      §S false\n      §R"), False, None),
            (text.replace("    §MT", "    §CTOR{priv} (i32:seed)\n    §MT"), True, None),
            (text.replace("    §MT", "    §CTOR{pub} (str:seed)\n    §MT"), False, "Unsupported finite constructor"),
            (text.replace("§M{Domain}", "§M{Domain:overflow=checked}"), True, None),
            (text.replace("§M{Domain}", "§M{Domain:overflow=unchecked}"), True, None),
            (text.replace("i32:a", "i32:a | (> # 0)"), False, "exclude prompt inputs"),
            (text.replace("i32:a", "i32:a | (>= # INT:-2147483648)"), True, None),
            (text.replace("      §R", "      §Q (== §C{Math.Abs} §A a §/C a)\n      §R"), False, None),
            (text + "    §MT{Add:pub} (i32:a) -> i32\n      §R a\n", True, None),
        ]
    elif name == "Product":
        result += [
            (text.replace("i32", "int"), True, None),
            (text.replace("get,set", "get"), True, None),
            (text.replace("get,set", "get,init"), True, None),
            (text.replace("get,set", "get,priset"), True, None),
            (text.replace("Price:i32:pub", "Price:i32:pub:static"), True, None),
            (text.rstrip() + " = -7\n", True, None),
            (text.replace("Price:i32:pub", "Price:i32:priv"), False, None),
            (text.replace("Price:i32", "Price:i64"), False, None),
            (text.replace("Price:", "Other:"), False, None),
            (text.replace("    §PROP{Price:i32:pub:get,set}",
                "    §FLD{i32:stored:priv}\n    §PROP{Price:i32:pub}\n      §GET\n        §R stored\n"
                "      §SET\n        §ASSIGN stored value"), False, "auto-property"),
            (text.replace("    §PROP{Price:i32:pub:get,set}",
                "    §PROP{Price:i32:pub}\n      §GET\n        §R 0"), False, "auto-property"),
            (text.replace("    §PROP", "    §CTOR{priv} (i32:seed)\n    §PROP"), True, None),
            (text.replace("    §PROP", "    §CTOR{pub} (str:seed)\n    §PROP"), False, "Unsupported finite constructor"),
        ]
    elif name == "Counter":
        result += [
            (text.replace("i32", "int"), True, None),
            (text.replace("_count:priv", "_count:priv:static"), True, None),
            (text.replace("_count:priv", "_count:priv:readonly"), True, None),
            (text.replace("_count:priv", "_count:priv:const").rstrip() + " = -7\n", True, None),
            (text.rstrip() + " = -7\n", True, None),
            (text.replace("_count:priv", "_count:pub"), False, None),
            (text.replace("_count", "_other"), False, None),
            (text.replace("i32:", "i64:"), False, None),
            (text.replace("    §FLD{i32:_count:priv}", "    §PROP{_count:i32:pub:get,set}"), False, None),
            (text.replace("    §FLD", "    §CTOR{priv} (str:seed)\n    §FLD"), True, None),
        ]
    elif name == "Point":
        result += [
            (text.replace("i32", "int"), True, None),
            (text.replace("_x:priv", "_x:priv:readonly").replace("_y:priv", "_y:priv:readonly"), True, None),
            (text.replace("_x:priv", "_x:priv:static").replace("_y:priv", "_y:priv:static"), True, None),
            (text.replace("_x:priv", "_x:pub"), False, None),
            (text.replace("_y:priv", "_y:pub"), False, None),
            (text.replace("_x", "_other"), False, None),
            (text.replace("i32:_y", "i64:_y"), False, None),
            (text.replace("§CTOR{pub}", "§CTOR{priv}"), False, None),
            (text.replace("i32:x", "i32:a").replace("§ASSIGN _x x", "§ASSIGN _x a"), False, None),
            (text.replace("i32:y", "i64:y").replace("§ASSIGN _y y", "§ASSIGN _y 0"), False, None),
            (text.replace("§ASSIGN _x x", "§ASSIGN _x y").replace("§ASSIGN _y y", "§ASSIGN _y x"), False, "expected"),
            (text.replace("§ASSIGN _x x", "§ASSIGN _x 0").replace("§ASSIGN _y y", "§ASSIGN _y 0"), False, "expected"),
            (text.replace("      §ASSIGN _y y\n", ""), False, "expected"),
            (text.replace("      §ASSIGN _x", "      §Q (>= x 0)\n      §ASSIGN _x"), False, "exclude prompt inputs"),
            (text.replace("      §ASSIGN _x", "      §Q (== x x)\n      §ASSIGN _x"), True, None),
            (text.replace("    §CTOR", "    §FLD{i32:decoy:priv}\n    §CTOR")
                 .replace("§ASSIGN _x x", "§ASSIGN decoy x"), False, "expected"),
            (text.replace("i32:x", "i32:x | (> # 0)"), False, "exclude prompt inputs"),
            (text.replace("i32:x", "i32:x | (>= # INT:-2147483648)"), True, None),
        ]
    return result


class OopVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        if cls.entry_points_executed != 160:
            raise RuntimeError(f"Expected exactly 160 actual OOP controls, observed {cls.entry_points_executed}.")
        print(f"Executed {cls.entry_points_executed} actual OOP verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None, allow_timeout=False):
        if allow_timeout and (expected or detail != "timeout"):
            raise ValueError("Only an explicitly negative timeout control may opt in.")
        with tempfile.TemporaryDirectory(prefix="calor-oop-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Domain.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            deadline = time.monotonic() + min(120, float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120")))
            try:
                output = PROCESS.run(
                    [str(HERE / "tasks/oop-features" / TASKS[name] / "verify.sh"), str(workspace)],
                    HERE, deadline, env=environment)
                success = True
            except RuntimeError as error:
                output = control_failure(error, allow_timeout=allow_timeout)
                success = False
            if success:
                assert_positive_output(output, environment)
            self.assertEqual(success, expected, output)
            if success:
                self.assertIn(f"Verification passed: {name}; native/reflected OOP identity;", output)
                self.assertRegex(output, r"; [0-9]+ finite runtime cases\.")
                if text == source(name):
                    count = {"Calculator": 133, "Product": 13, "Point": 338}.get(name, 0)
                    self.assertIn(f"; {count} finite runtime cases.", output)
            if detail:
                self.assertIn(detail, output)
            type(self).entry_points_executed += 1
            return output

    def test_native_identity_state_and_signed_boundaries(self):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                with self.subTest(name=name, index=index):
                    self.verify(name, text, expected, detail=detail)

    def test_missing_comments_interop_and_unrelated_members(self):
        for name in TASKS:
            text = source(name)
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "invalid", False, detail="Calor compilation failed")
            self.verify(name, "// " + text.replace("\n", " ") + "\n§M{Domain}\n", False)
            self.verify(name, text + text.split("\n", 1)[1], False)
            self.verify(name, f"§M{{Domain}}\n  §CSHARP{{public class {name} {{ }}}}§/CSHARP\n", False)
            self.verify(name, text + "  §CSHARP{public class Unrelated { }}§/CSHARP\n", False,
                        detail="Raw interop")
            if name != "IRepository":
                self.verify(name, text.replace("§CL{" + name + ":pub}", "§M{" + name + ":pub}")
                            .replace("§MT{", "§F{"), False)
            if name not in ("Person", "IRepository"):
                self.verify(name, text.replace("  §CL{" + name + ":pub}", "  §CL{" + name
                            + ":pub}\n  §CL{Unrelated:pub}"), False)

    def test_selected_compiler_and_bounded_processes(self):
        for configuration in ("", "..", "../Debug"):
            self.verify_prerequisite_refusal(
                {"CALOR_COMPILER_CONFIGURATION": configuration},
                "CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root.")
        self.verify_prerequisite_refusal(
            {"CALOR_COMPILER_ROOT": ""},
            "CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path.")
        with tempfile.TemporaryDirectory(prefix="calor-oop-selected-") as temporary:
            arm = Path(temporary).resolve()
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            missing = arm / relative / "calor.dll"
            self.verify_prerequisite_refusal(
                {"CALOR_COMPILER_ROOT": str(arm)},
                f"Selected compiler dependency missing: {missing}; build the selected root first.", missing=missing)
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            self.verify("Person", source("Person"), env={"CALOR_COMPILER_ROOT": str(arm)})
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            image = arm / relative / native
            image.write_bytes(b"not a native Z3 library")
            self.verify_corrupt_native_refusal(arm, image)
            (arm / relative / native).unlink()
            self.verify_prerequisite_refusal(
                {"CALOR_COMPILER_ROOT": str(arm)},
                f"Selected compiler dependency missing: {image}; build the selected root first.", missing=image)
        self.verify("Calculator", source("Calculator").replace("§R (+ a b)",
                    '§E{throw}\n      §TH STR:"oop failure"'), False, detail="oop failure")
        self.verify("Calculator", source("Calculator").replace("§R (+ a b)",
                    "§WH true\n        §CN\n      §R (+ a b)"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout", allow_timeout=True)

    def verify_prerequisite_refusal(self, environment, diagnostic, *, missing=None):
        before = type(self).entry_points_executed
        if missing is not None:
            self.assertFalse(missing.exists())
        with self.assertRaises(RuntimeError) as refused:
            self.verify("Person", source("Person"), False, env=environment)
        self.assertEqual(str(refused.exception), f"Verifier command exited 1:\nVerification failed: {diagnostic}\n")
        if missing is not None:
            self.assertFalse(missing.exists())
        self.assertEqual(type(self).entry_points_executed, before)
        type(self).entry_points_executed += 1

    def verify_corrupt_native_refusal(self, arm, image):
        before = type(self).entry_points_executed
        self.assertEqual(image.read_bytes(), b"not a native Z3 library")
        with self.assertRaises(RuntimeError) as refused:
            self.verify("Person", source("Person"), False, env={"CALOR_COMPILER_ROOT": str(arm)})
        output = str(refused.exception)
        self.assertEqual(re.findall(r"Verifier command exited ([^\s:]+)", output), ["1", "1"], output)
        self.assertTrue(output.startswith("Verifier command exited 1:\n"), output)
        for wrapper in re.finditer(r"verifier\s+command\s+exited", output, re.IGNORECASE):
            self.assertTrue(output[wrapper.start():].startswith("Verifier command exited 1:\n"), output)
        self.assertIn("System.DllNotFoundException", output)
        self.assertIn("Microsoft.Z3", output)
        self.assertRegex(output, r"slice is not valid mach-o|invalid ELF header|file too short|not a valid Win32")
        contamination = output.replace("System.DllNotFoundException", "")
        # Native loaders enumerate missing alternative probes even when the
        # owned library's actual refusal is the invalid-image diagnostic above.
        loader_probe = re.compile(
            r"dlopen\([^,\n]*(?:liblibz3|libz3)(?:\.dylib)?, 0x[0-9a-f]+\): tried: "
            r"'[^'\n]*(?:liblibz3|libz3)(?:\.dylib)?' "
            r"\((?:no such file(?:, not in dyld cache)?|slice is not valid mach-o file)\)"
            r"(?:, '[^'\n]*(?:liblibz3|libz3)(?:\.dylib)?' "
            r"\((?:no such file(?:, not in dyld cache)?|slice is not valid mach-o file)\))*"
            r"|[^:\n]*(?:liblibz3|libz3)(?:\.so)?: cannot open shared object file: No such file or directory"
        )
        contamination = "\n".join(
            re.sub(r"(?i)no such file(?: or directory)?", "", line) if loader_probe.fullmatch(line) else line
            for line in contamination.splitlines()
        )
        self.assertIsNone(INFRASTRUCTURE.search(contamination), output)
        self.assertIsNone(TIMEOUT.search(output), output)
        self.assertNotRegex(output, r"(?i)verification\s+passed\s*:")
        assert_compiler_identity(output, dict(os.environ, CALOR_COMPILER_ROOT=str(arm)))
        self.assertEqual(image.read_bytes(), b"not a native Z3 library")
        self.assertEqual(type(self).entry_points_executed, before)
        # This validated native prerequisite refusal is not finite behavior.
        type(self).entry_points_executed += 1


class OopAdmissionTests(unittest.TestCase):
    def test_actual_prerequisite_refusals_are_separate_and_uncontaminated(self):
        test = OopVerifierTests()
        before = OopVerifierTests.entry_points_executed
        diagnostic = "CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path."
        output = f"Verifier command exited 1:\nVerification failed: {diagnostic}\n"
        try:
            for error in (output + "UNKNOWN\n", output.replace("exited 1:", "exited 139:"),
                          output + "Verifier command exited 2:\n",
                          output + "Verification passed: Person\n"):
                with self.subTest(root_refusal=error), patch.object(PROCESS, "run", side_effect=RuntimeError(error)):
                    with self.assertRaises(AssertionError):
                        test.verify_prerequisite_refusal({"CALOR_COMPILER_ROOT": ""}, diagnostic)
                    self.assertEqual(OopVerifierTests.entry_points_executed, before)
            with patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                test.verify_prerequisite_refusal({"CALOR_COMPILER_ROOT": ""}, diagnostic)
            with tempfile.TemporaryDirectory(prefix="calor-oop-mock-native-") as temporary:
                arm = Path(temporary).resolve()
                configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
                selected = arm / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
                image = arm / "libz3.dylib"
                image.write_bytes(b"not a native Z3 library")
                native = ("Verifier command exited 1:\n"
                          f"Selected compiler: {selected}\n"
                          "Verification failed: Verifier command exited 1:\n"
                          "Verification failed: System.DllNotFoundException: Microsoft.Z3; "
                          "slice is not valid mach-o\n"
                          "dlopen(libz3.dylib, 0x0001): tried: 'libz3.dylib' (no such file)\n")
                for error in (native + "UNKNOWN\n", native + "cleanup failure\n",
                              native + "no such file or directory\n",
                              native + "dlopen(libz3.dylib, 0x0001): no such file\n",
                              native.replace("(no such file)", "(no such file) EOF failure"),
                              native + "Verification passed: Person\n",
                              native.replace("exited 1:", "exited 137:", 1),
                              native.replace("Verification failed: Verifier command exited 1:",
                                             "Verification failed: Verifier command exited 2:"),
                              native.replace("slice is not valid mach-o", "unrelated loader failure"),
                              native.replace(str(selected), str(selected) + ".other")):
                    with self.subTest(native_refusal=error), patch.object(
                        PROCESS, "run", side_effect=RuntimeError(error)
                    ):
                        with self.assertRaises(AssertionError):
                            test.verify_corrupt_native_refusal(arm, image)
                        self.assertEqual(OopVerifierTests.entry_points_executed, before + 1)
                with patch.object(PROCESS, "run", side_effect=RuntimeError(native)):
                    test.verify_corrupt_native_refusal(arm, image)
            self.assertEqual(OopVerifierTests.entry_points_executed, before + 2)
        finally:
            OopVerifierTests.entry_points_executed = before

    def test_actual_verify_finite_failure_and_identity_guards(self):
        test = OopVerifierTests()
        before = OopVerifierTests.entry_points_executed
        outer = "Verifier command exited 1:\n"
        diagnostic = "Verification failed: Wrong result: expected 7"
        infrastructure = [
            "UNKNOWN", "solver returned unknown", "solver timed out", "solver timeout",
            "EOF failure", "EOFError: pipe closed", "cleanup failure", "inherited stdout pipe",
            "inherited stderr pipe", "broken pipe", "Cannot terminate verifier process group 123",
            "OSError: launch failure", "FileNotFoundError: missing executable",
            "PermissionError: denied", "TimeoutError: process start", "errno 8",
            "permission denied", "no such file", "unable to launch dotnet", "could not start process",
            "OS error: denied", "operating-system error", "process failure", "Traceback",
            "supervision failure",
            "process exited 139", "process terminated by signal 6", "Segmentation fault", "Aborted (core dumped)",
            "System.DllNotFoundException", "System.BadImageFormatException",
            "System.Win32Exception", "Microsoft.Z3.Z3Exception",
            "global deadline expired", "Verifier timeout: overall deadline expired.",
            "Verifier timeout after 2s: dotnet", "Verification passed: Person",
        ]
        errors = [error + "\n" + diagnostic for error in infrastructure]
        errors += [outer + diagnostic + "\n" + error for error in infrastructure]
        for status in ("0", "2", "-6", "137", "139", "1garbage", "+1", "01", ""):
            errors += [f"Verifier command exited {status}:\n" + diagnostic,
                       outer + f"Verification failed: Verifier command exited {status}:\n" + diagnostic]
        errors += [
            "unclassified failure\n" + diagnostic,
            "Verifier command exited 1\n" + diagnostic,
            outer + "Verifier command exited 1: " + diagnostic,
            outer + "Verifier command exited 1:\r\n" + diagnostic,
            outer + "verifier command exited 1:\n" + diagnostic,
            outer + "Verifier command exited1:\n" + diagnostic,
            outer + diagnostic + "\nVerifier timeout after 2s: dotnet trailing garbage",
            outer + diagnostic + "\nVerifier timeout after 1e999s: dotnet",
            outer + diagnostic + "\nVerifier timeout after 0s: dotnet",
            outer + diagnostic + "\nVerifier timeout after 121s: dotnet",
        ]
        try:
            for output in errors:
                with self.subTest(output=output), patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    with self.assertRaises(RuntimeError):
                        test.verify("Calculator", source("Calculator"), False, detail="expected")
                    self.assertEqual(OopVerifierTests.entry_points_executed, before)
            for error in infrastructure:
                output = outer + diagnostic + "\nVerifier timeout after 2s: dotnet\n" + error
                if error == "Verifier timeout after 2s: dotnet":
                    continue
                with self.subTest(timeout_contamination=error), patch.object(
                    PROCESS, "run", side_effect=RuntimeError(output)
                ):
                    with self.assertRaises(RuntimeError):
                        test.verify("Calculator", source("Calculator"), False, detail="timeout", allow_timeout=True)
                    self.assertEqual(OopVerifierTests.entry_points_executed, before)
            for output in errors:
                with self.subTest(timeout_opt_in=output), patch.object(
                    PROCESS, "run", side_effect=RuntimeError(output)
                ):
                    if output.endswith("\nVerifier timeout after 2s: dotnet"):
                        continue
                    with self.assertRaises(RuntimeError):
                        test.verify("Calculator", source("Calculator"), False, detail="timeout", allow_timeout=True)
                    self.assertEqual(OopVerifierTests.entry_points_executed, before)
            for error in (OSError("launch failed"), EOFError("pipe closed"), TimeoutError("expired")):
                with patch.object(PROCESS, "run", side_effect=error), self.assertRaises(type(error)):
                    test.verify("Calculator", source("Calculator"), False, detail="expected")
                self.assertEqual(OopVerifierTests.entry_points_executed, before)

            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            selected = COMPILER_ROOT / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
            identity = f"Selected compiler: {selected}\n"
            success = identity + "Verification passed: Calculator; native/reflected OOP identity; 133 finite runtime cases.\n"
            invalid = [
                success.replace(str(selected), str(selected) + ".other"),
                success.replace(str(selected), str(selected.parent.parent / "wrong/net10.0/calor.dll")),
                success.replace(str(selected), str(selected.parent / "other.dll")),
                success.replace(str(COMPILER_ROOT), str(COMPILER_ROOT) + "-other"),
                success.replace("/net10.0/", "/net9.0/"),
                success.replace(identity, ""), identity + success, "  " + success,
                success + "  Selected compiler: wrong\n", success + "warning: Selected compiler: wrong\n",
                success.replace("Selected compiler:", "selected compiler:"),
            ] + [success + error + "\n" for error in infrastructure + [diagnostic, outer]]
            for output in invalid:
                with self.subTest(positive=output), patch.object(PROCESS, "run", return_value=output):
                    with self.assertRaises(AssertionError):
                        test.verify("Calculator", source("Calculator"))
                    self.assertEqual(OopVerifierTests.entry_points_executed, before)
            for output in (outer + diagnostic, outer + "Verification failed: " + outer + diagnostic):
                with patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    test.verify("Calculator", source("Calculator"), False, detail="expected")
            for output in ("Verifier timeout after 2s: dotnet",
                           outer + "Verification failed: Verifier timeout after 2s: dotnet"):
                with patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    test.verify("Calculator", source("Calculator"), False, detail="timeout", allow_timeout=True)
            with self.assertRaises(ValueError):
                test.verify("Calculator", source("Calculator"), False, allow_timeout=True)
            with patch.object(PROCESS, "run", return_value=success):
                test.verify("Calculator", source("Calculator"))
            self.assertEqual(OopVerifierTests.entry_points_executed, before + 5)
        finally:
            OopVerifierTests.entry_points_executed = before


def run_compatibility(old, current, deadline):
    test = OopVerifierTests()
    # Named representatives keep both real arms well within the unchanged total
    # deadline. All omitted permutations still run in the full current suite.
    selected = {
        "Person": {
            "empty-sealed": 4, "private-class": 1,
            "source-field": 5, "source-constructor": 6,
        },
        "IRepository": {
            "integer-alias": 3, "parameter-name": 4, "parameter-width": 5,
            "nullable-not-option": 6, "closed-generic-argument": 8,
            "implementation": 10, "excluding-refinement": 14,
            "admitting-refinement": 15,
        },
        "Calculator": {
            "checked-signed-sums": 19, "unchecked-signed-sums": 20,
            "private-method": 7, "parameter-name": 9, "parameter-width": 10,
            "constant-stub": 12, "excluding-precondition": 14,
            "failing-output-contract": 16, "real-nonpublic-constructor": 17,
            "unmodeled-precondition": 23,
        },
        "Product": {
            "get-only": 5, "init": 6, "private-setter": 7, "static-get-set": 8,
            "private-property": 10, "property-width": 11,
            "behaviorally-equivalent-manual-property": 13,
            "unsupported-constructor": 16,
        },
        "Counter": {
            "integer-alias": 4, "readonly": 6, "constant": 7,
            "public-field": 9, "field-name": 10,
        },
        "Point": {
            "integer-alias-and-instance-state": 4, "readonly-state": 5, "static-state": 6,
            "public-field": 7, "private-constructor": 11, "parameter-width": 13,
            "swapped-state": 14, "missing-assignment": 16,
        },
    }
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            controls = [(category, cases(name)[index]) for category, index in selected[name].items()]
            controls.append(("recursive-interop", (
                source(name) + "  §CSHARP{public class Unrelated { }}§/CSHARP\n", False, "Raw interop")))
            for category, (text, expected, detail) in controls:
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("OOP compatibility exceeded its 600-second deadline.")
                test.verify(name, historical_syntax(text) if historical else text, expected,
                            env={"CALOR_COMPILER_ROOT": str(root),
                                 "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                print(f"{arm} {name} {category}: {'passed' if expected else 'rejected'}", flush=True)
        text = source("Calculator")
        adversarial = {
            "missing-input": None,
            "comment-only-native-decoy": "// " + text.replace("\n", " ") + "\n§M{Domain}\n",
            "named-module-not-class": text.replace("§CL{Calculator:pub}", "§M{Calculator:pub}")
                .replace("§MT{", "§F{"),
            "unrelated-owner": text.replace("  §CL{Calculator:pub}",
                "  §CL{Calculator:pub}\n  §CL{Unrelated:pub}"),
        }
        for category, text in adversarial.items():
            remaining = min(120, deadline - time.monotonic())
            if remaining <= 0:
                raise RuntimeError("OOP compatibility exceeded its 600-second deadline.")
            test.verify("Calculator", historical_syntax(text) if historical and text is not None else text, False,
                        env={"CALOR_COMPILER_ROOT": str(root),
                             "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)})
            print(f"{arm} Calculator {category}: rejected", flush=True)
    test.verify("Person", historical_syntax(source("Person")), False,
                env={"CALOR_COMPILER_ROOT": str(current),
                     "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))},
                detail="Calor compilation failed")
    expected_count = 2 * (sum(len(indices) + 1 for indices in selected.values()) + 4) + 1
    if expected_count != 107 or OopVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected {expected_count} actual OOP compatibility controls.")
    print(f"Compatibility passed: {expected_count} actual old/current OOP entry-point controls.")


if __name__ == "__main__":
    unittest.main()
