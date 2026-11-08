#!/usr/bin/env python3
"""Six existing OOP entry points; native disposable controls, never agents."""

import importlib.util
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {"Person": "01_class_definition", "IRepository": "02_interface_definition",
         "Calculator": "03_method_in_class", "Product": "04_property",
         "Counter": "05_field_declaration", "Point": "06_constructor"}
specification = importlib.util.spec_from_file_location("oop_process", HERE / "lib/verify-logic.py")
PROCESS = importlib.util.module_from_spec(specification)
specification.loader.exec_module(PROCESS)


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

    def verify(self, name, text, expected=True, *, env=None, detail=None):
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
                output = str(error)
                success = False
            type(self).entry_points_executed += 1
            self.assertEqual(success, expected, output)
            if not expected and "timeout" in output.lower() and detail != "timeout":
                self.fail("A timeout cannot satisfy a finite negative OOP control:\n" + output)
            if expected:
                self.assertIn(f"Verification passed: {name}; native/reflected OOP identity;", output)
                self.assertIn(str(Path(environment["CALOR_COMPILER_ROOT"]).resolve()
                                  / "src/Calor.Compiler/bin"), output)
                self.assertRegex(output, r"; [0-9]+ finite runtime cases\.")
                if text == source(name):
                    count = {"Calculator": 133, "Product": 13, "Point": 338}.get(name, 0)
                    self.assertIn(f"; {count} finite runtime cases.", output)
            if detail:
                self.assertIn(detail, output)
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
            self.verify("Person", source("Person"), False,
                        env={"CALOR_COMPILER_CONFIGURATION": configuration}, detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("Person", source("Person"), False, env={"CALOR_COMPILER_ROOT": ""},
                    detail="CALOR_COMPILER_ROOT")
        with tempfile.TemporaryDirectory(prefix="calor-oop-selected-") as temporary:
            arm = Path(temporary)
            self.verify("Person", source("Person"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            self.verify("Person", source("Person"), env={"CALOR_COMPILER_ROOT": str(arm)})
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"not a native Z3 library")
            self.verify("Person", source("Person"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Verifier command exited")
            (arm / relative / native).unlink()
            self.verify("Person", source("Person"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
        self.verify("Calculator", source("Calculator").replace("§R (+ a b)",
                    '§E{throw}\n      §TH STR:"oop failure"'), False, detail="oop failure")
        self.verify("Calculator", source("Calculator").replace("§R (+ a b)",
                    "§WH true\n        §CN\n      §R (+ a b)"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")


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
