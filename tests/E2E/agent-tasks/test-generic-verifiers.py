#!/usr/bin/env python3
"""Real bounded gen-001..003 entry points; disposable native inputs only."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {"Identity": "01_generic_function", "Container": "02_generic_class",
         "Repository": "03_type_constraints"}


def source(name):
    if name == "Identity":
        return "§M{Generic}\n  §F{Identity:pub}<T> (T:value) -> T\n    §R value\n"
    if name == "Container":
        return """§M{Generic}
  §CL{Container:pub}<T>
    §FLD{T:stored:priv}
    §MT{Get:pub} () -> T
      §R stored
    §MT{Set:pub} (T:value) -> void
      §E{mut}
      §ASSIGN stored value
"""
    return """§M{Generic}
  §CL{Repository:pub}<T>
    §WHERE T : class
    §FLD{List<T>:items:priv} = §NEW{List<T>}
    §MT{Add:pub} (T:value) -> void
      §E{mut}
      §C{items.Add}
        §A value
      §/C
"""


def historical_syntax(text):
    lines = []
    for serial, line in enumerate(text.splitlines()):
        if line.lstrip().startswith(("§M{", "§F{", "§CL{", "§MT{", "§CTOR{")):
            line = line.replace("{", f"{{id{serial}:", 1)
        if line.lstrip().startswith("§IF "):
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


def cases(name):
    text = source(name)
    result = [
        (text, True, None),
        (text.replace(name + ":pub", name + ":int", 1), False, "native"),
        (text.replace(name + ":pub", "Other:pub", 1), False, "Expected exactly one native"),
        ("// " + text.replace("\n", " ") + "\n§M{Generic}\n", False, "Expected exactly one native"),
        (text.replace("<T>", "<U>", 1).replace("T:", "U:").replace("-> T", "-> U")
             .replace(" T :", " U :").replace("List<T>", "List<U>"), False, "native"),
        (text.replace("<T>", "<T, U>", 1), False, "native"),
    ]
    if name == "Identity":
        result += [
            (text.replace("value", "input"), True, None),
            (text.replace("    §R value", "    §B{copy:T} value\n    §R copy"), True, None),
            (text.replace("    §R value", "    §Q false\n    §R value"), False, None),
            (text.replace("    §R value", "    §S false\n    §R value"), False, None),
            (text.replace("<T> (T:value) -> T", "(i32:value) -> i32"), False, "native"),
            (text.replace("-> T", "-> i32").replace("§R value", "§R 0"), False, "native"),
            (text.replace("T:value", "i32:value"), False, None),
            (text.replace("§R value", "§R default"), False, "expected"),
            (text.replace("§R value", ""), False, None),
            (text.replace("    §R value", "    §WHERE T : struct\n    §R value"), True, None),
            (text.replace("    §R value", "    §WHERE T : class\n    §Q (!= value null)\n    §R value"), False, None),
            (text.replace("    §R value", "    §WHERE T : class, new\n    §R value"), True, None),
        ]
    elif name == "Container":
        result += [
            (text.replace("stored", "_other").replace("value", "input"), True, None),
            (text.replace("Get:pub", "Get:priv").replace("Set:pub", "Set:priv"), True, None),
            (text.replace("    §MT{Get", "    §CTOR{priv} (T:initial)\n      §ASSIGN stored initial\n    §MT{Get"), True, None),
            (text.replace("    §MT{Get", "    §WHERE T : class\n    §MT{Get"), True, None),
            (text.replace("T:stored:priv", "i32:stored:priv").replace("§R stored", "§R default")
                 .replace("§ASSIGN stored value", "§ASSIGN stored 0"), False, "storage"),
            (text.replace("T:stored:priv", "T:stored:pub"), False, "storage"),
            (text.replace("§R stored", "§R default"), False, "expected"),
            (text.replace("§ASSIGN stored value", "§ASSIGN stored default"), False, "expected"),
            (text.replace("§ASSIGN stored value", "§B{ignored:T} value"), False, "expected"),
            (text.replace("      §ASSIGN stored value", "      §Q false\n      §ASSIGN stored value"), False, None),
            (text.replace("      §R stored", "      §S false\n      §R stored"), False, None),
            (text.replace("Set:pub", "Other:pub"), False, "Expected exactly one native"),
            (text.replace("    §MT{Get", "    §CTOR{pub} (i32:x, i32:y)\n    §MT{Get"), False, "Unsupported finite constructor"),
            (text.replace("-> void", "-> bool")
                 .replace("§ASSIGN stored value", "§ASSIGN stored value\n      §R true"), True, None),
            (text.replace("    §MT{Get", "    §FLD{T:decoy:pub}\n    §MT{Get")
                 .replace("§R stored", "§R decoy"), False, "expected"),
            (text.replace("    §MT{Get", "    §WHERE T : class\n    §MT{Get")
                 .replace("      §ASSIGN stored value", "      §Q (!= value null)\n      §ASSIGN stored value"), False, None),
        ]
    else:
        result += [
            (text.replace("items", "_storage").replace("value", "input"), True, None),
            (text.replace("List<T>", "System.Collections.Generic.List<T>"), True, None),
            (text.replace(" = §NEW{List<T>}", "")
                 .replace("    §MT{Add", "    §CTOR{priv} (List<T>:initial)\n      §ASSIGN items initial\n    §MT{Add"), True, None),
            (text.replace("    §WHERE T : class\n", ""), False, "constraint"),
            (text.replace("T : class", "T : struct"), False, "constraint"),
            (text.replace("T : class", "T : new"), False, "constraint"),
            (text.replace("List<T>", "List<str>").replace("§A value", '§A "wrong"'), False, "storage"),
            (text.replace("items:priv", "items:pub"), False, "storage"),
            (text.replace("Add:pub", "Add:priv"), False, "signature"),
            (text.replace("§A value", "§A null"), False, "expected"),
            (text.replace("      §C{items.Add}", "      §Q false\n      §C{items.Add}"), False, None),
            (text.replace("      §C{items.Add}\n        §A value\n      §/C", "      §B{ignored:T} value"), False, "expected"),
            (text.replace("    §MT{Add", "    §FLD{List<T>:decoy:pub} = §NEW{List<T>}\n    §MT{Add")
                 .replace("items.Add", "decoy.Add"), False, "expected"),
            (text.replace("      §C{items.Add}", "      §C{items.Clear} §/C\n      §C{items.Add}"), False, "expected"),
            (text.replace("      §/C", "      §/C\n      §C{items.Add} §A value §/C"), False, "expected"),
            (text.replace(" = §NEW{List<T>}", ""), False, "NullReferenceException"),
            (text.replace("-> void", "-> i32") + "      §R 1\n", True, None),
            (text.replace("      §C{items.Add}", "      §IF (== value null)\n        §R\n      §C{items.Add}"),
             False, "expected"),
            (text.replace("T : class", "T : class, new"), True, None),
            (text.replace("T : class", "T : class?"), True, None),
            (text.replace(" = §NEW{List<T>}", "").replace("§E{mut}", "§E{mut,alloc}").replace("      §C{items.Add}",
                "      §IF (== items null)\n        §ASSIGN items §NEW{List<T>}\n      §C{items.Add}"), True, None),
        ]
    return result


class GenericVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        if cls.entry_points_executed != 93:
            raise RuntimeError(f"Expected exactly 93 actual generic controls, observed {cls.entry_points_executed}.")
        print(f"Executed {cls.entry_points_executed} actual generic verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-generic-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Generic.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            result = subprocess.run([str(HERE / "tasks/generics" / TASKS[name] / "verify.sh"), str(workspace)],
                                    env=environment, text=True, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name}; native/reflected generic identity", result.stdout)
                self.assertRegex(result.stdout, r"; [2-6] closed types, [1-9][0-9]* runtime cases\.")
                if text == source(name):
                    count = {"Identity": "6 closed types, 21", "Container": "6 closed types, 42",
                             "Repository": "3 closed types, 24"}[name]
                    self.assertIn(count + " runtime cases.", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_actual_native_signatures_and_behavior(self):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                with self.subTest(name=name, index=index):
                    self.verify(name, text, expected, detail=detail)

    def test_missing_invalid_interop_and_ambiguity(self):
        for name in TASKS:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "invalid", False, detail="Calor compilation failed")
            self.verify(name, source(name) + source(name).split("\n", 1)[1], False)
            declaration = ("public static class GenericModule { public static T Identity<T>(T value) => value; }"
                           if name == "Identity" else f"public class {name}<T> {{ }}")
            self.verify(name, f"§M{{Generic}}\n  §CSHARP{{{declaration}}}§/CSHARP\n", False)
            self.verify(name, source(name) + "  §CSHARP{public class Unrelated { }}§/CSHARP\n",
                        False, detail="Raw interop")

    def test_runtime_and_profile_bounds(self):
        self.verify("Identity", source("Identity").replace("§R value", "§E{throw}\n    §TH STR:\"generic failure\""),
                    False, detail="generic failure")
        self.verify("Identity", source("Identity").replace("§R value", "§WH true\n      §CN\n    §R value"),
                    False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("Container", source("Container"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")

    def test_actual_selected_native_prerequisite(self):
        for configuration in ("", "..", "../Debug"):
            self.verify("Identity", source("Identity"), False,
                        env={"CALOR_COMPILER_CONFIGURATION": configuration},
                        detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("Identity", source("Identity"), False,
                    env={"CALOR_COMPILER_ROOT": ""}, detail="CALOR_COMPILER_ROOT")
        with tempfile.TemporaryDirectory(prefix="calor-generic-selected-") as temporary:
            arm = Path(temporary)
            self.verify("Identity", source("Identity"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            output = self.verify("Identity", source("Identity"), env={"CALOR_COMPILER_ROOT": str(arm)})
            self.assertIn(str(arm / relative / "calor.dll"), output)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[os.sys.platform]
            (arm / relative / native).write_bytes(b"not a native Z3 library")
            self.verify("Identity", source("Identity"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Verifier command exited")
            (arm / relative / native).unlink()
            self.verify("Identity", source("Identity"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")


def run_compatibility(old, current, deadline):
    test = GenericVerifierTests()
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Generic compatibility exceeded its 600-second deadline.")
                output = test.verify(name, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root),
                                          "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Generic compatibility used the wrong selected compiler.")
                print(f"{arm} {name} {index}: {'passed' if expected else 'rejected'}")
    test.verify("Identity", historical_syntax(source("Identity")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    expected_count = 2 * sum(len(cases(name)) for name in TASKS) + 1
    if expected_count != 135 or GenericVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected {expected_count} actual generic compatibility controls.")
    print(f"Compatibility passed: {expected_count} actual old/current generic entry-point controls.")


if __name__ == "__main__":
    unittest.main()
