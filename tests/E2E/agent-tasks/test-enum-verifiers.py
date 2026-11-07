#!/usr/bin/env python3
"""Bounded real enum verify.sh controls, with disposable selected-parser inputs."""

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
TASKS = {"Status": "01_enum_definition", "Color": "02_enum_explicit_values",
         "Priority": "03_enum_extension"}
MEMBERS = {"Status": ["Pending", "InProgress", "Completed"],
           "Color": ["Red = 1", "Green = 2", "Blue = 4"],
           "Priority": ["Low", "Medium", "High", "Critical"]}
BODY = "§R (|| (== self Priority.High) (== self Priority.Critical))"
ALTERNATIVE = "§IF (== self Priority.High)\n  §R true\n§IF (== self Priority.Critical)\n  §R true\n§R false"


def source(name, *, members=None, visibility="pub", underlying=None, body=None,
           signature=None, contracts="", extension="Priority", extra=""):
    header = f"§EN{{{name}:{visibility}" + (f":{underlying}" if underlying else "") + "}"
    text = "§M{Enums}\n  " + header + "\n" + "".join(
        "    " + member + "\n" for member in (MEMBERS[name] if members is None else members))
    if name == "Priority" and body is not False:
        text += f"  §EEXT{{{extension}}}\n    " + (signature or "§F{IsUrgent:pub} (Priority:self) -> bool") + "\n"
        text += "".join("      " + line + "\n" for line in
                        (contracts.splitlines() + (BODY if body is None else body).splitlines()))
    return text + extra


def historical_syntax(text):
    """Only temporary inputs gain old IDs and native old parameter refinements."""
    text = text.replace("§F{IsUrgent:pub} (Priority:self | ", "§F{IsUrgent:pub}\n      §I{Priority:self} | ")
    if "§I{Priority:self} | " in text:
        text = text.replace(") -> bool", "\n      §O{bool}", 1)
    lines = []
    serial = 0
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(("§M{", "§EN{", "§EEXT{", "§F{", "§CL{")):
            serial += 1
            line = line.replace("{", f"{{id{serial}:", 1)
        elif stripped.startswith("§IF "):
            serial += 1
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


def cases(name):
    """Both actual selected arms must execute this same acceptance/rejection set."""
    result = [(source(name), True, None),
              (source(name, visibility="priv"), False, None),
              (source(name).replace(f"§EN{{{name}:", "§EN{Other:"), False, None),
              ("// " + source(name).replace("\n", " ") + "\n§M{Enums}\n", False,
               "Expected exactly one native module enum"),
              (source(name, members=[]), False, None)]
    for index in range(len(MEMBERS[name])):
        result.append((source(name, members=MEMBERS[name][:index] + MEMBERS[name][index + 1:]), False, None))
    if name == "Status":
        result += [
            (source(name, members=["Completed = 19", "Pending = -2", "InProgress = 7", "Other = 80"]), True, None),
            (source(name, underlying="u8"), True, None),
        ]
    elif name == "Color":
        result += [(source(name, members=["Red = 0x1", "Green = 2", "Blue = 1 << 2"],
                          underlying="u8"), True, None)]
        for index, member in enumerate(MEMBERS[name]):
            omitted = MEMBERS[name].copy()
            omitted[index] = member.split(" = ")[0]
            result.append((source(name, members=omitted), False, "requires an explicit integer assignment"))
            wrong = MEMBERS[name].copy()
            wrong[index] = member.split(" = ")[0] + " = 3"
            result.append((source(name, members=wrong), False, "Wrong explicit CLR value"))
        result += [(source(name, members=["Red", "Green", "Blue"],
                           extra="  §EN{Decoy:pub}\n    Red = 1\n    Green = 2\n    Blue = 4\n"),
                    False, "requires an explicit integer assignment"),
                   (source(name, members=["Red", "Green", "Blue"]) + "// Red = 1 Green = 2 Blue = 4\n",
                    False, "requires an explicit integer assignment")]
    else:
        result += [
            (source(name, body=ALTERNATIVE, contracts="§Q true\n§S true"), True, None),
            (source(name, members=["Critical = 12", "Low = 9", "Medium = 18", "High = 2", "Other = 90"],
                    underlying="u8"), True, None),
            (source(name, signature="§F{IsUrgent:pub} (Priority:value) -> bool",
                    body=BODY.replace("self", "value")), True, None),
            (source(name, contracts="§Q (|| (== self Priority.Low) (|| (== self Priority.Medium) "
                    "(|| (== self Priority.High) (== self Priority.Critical))))"), True, None),
            (source(name, signature="§F{IsUrgent:pub} (Priority:self | (== # #)) -> bool"), True, None),
            (source(name, body=False), False, "Expected exactly one native Priority enum extension"),
            (source(name, body=False) + "  §EEXT{Priority}\n", False, "Expected exactly one native IsUrgent"),
            (source(name).replace("§F{IsUrgent:pub}", "§F{Other:pub}"), False, "Expected exactly one native IsUrgent"),
            (source(name, signature="§F{IsUrgent:priv} (Priority:self) -> bool"), False, "Wrong native public bool"),
            (source(name, signature="§F{IsUrgent:pub} (Priority:self) -> i32", body="§R 1"), False, "Wrong native public bool"),
            (source(name, signature="§F{IsUrgent:pub} (Priority:self, i32:x) -> bool"), False, "Wrong native public bool"),
            (source(name, signature="§F{IsUrgent:pub} (i32:self) -> bool", body="§R true"), False, None),
            (source(name, extension="Other", body="§R true",
                    extra="  §EN{Other:pub}\n    One\n"), False, None),
            (source(name, body=False) + "  §F{IsUrgent:pub} (Priority:self) -> bool\n    " + BODY + "\n",
             False, "Expected exactly one native Priority enum extension"),
            (source(name, body=""), False, None),
            (source(name, body="§R true"), False, "expected"),
            (source(name, body="§R false"), False, "expected"),
            (source(name, body="§R (== self Priority.High)"), False, "expected"),
            (source(name, body="§R (== self Priority.Critical)"), False, "expected"),
            (source(name, body="§R (|| (== self Priority.Medium) (== self Priority.High))"), False, "expected"),
            (source(name, contracts="§Q false"), False, None),
            (source(name, contracts="§S false"), False, None),
            (source(name, signature="§F{IsUrgent:pub} (Priority:self | (!= # Priority.Low)) -> bool"),
             False, None),
        ]
        for member in ("Low", "Medium", "High", "Critical"):
            result.append((source(name, contracts=f"§Q (!= self Priority.{member})"), False, None))
    return result


class EnumVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real enum verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-enum-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Enums.calr").write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/enums" / TASKS[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name}; native public enum and {len(MEMBERS[name])} reflected CLR members", result.stdout)
                if name == "Priority":
                    self.assertIn("native/reflected extension and 4 named-value runtime cases.", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_native_members_values_and_extension_behavior(self):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                with self.subTest(name=name, index=index):
                    self.verify(name, text, expected, detail=detail)

    def test_interop_decoys_and_ambiguity(self):
        for name in TASKS:
            members = ", ".join(MEMBERS[name])
            self.verify(name, f"§M{{Enums}}\n  §CSHARP{{public enum {name} {{ {members} }} }}§/CSHARP\n",
                        False, detail="Expected exactly one native module enum")
            self.verify(name, source(name) + source(name).split("\n", 1)[1], False)
        self.verify("Priority", source("Priority", body=False)
                    + "  §CSHARP{public static class PriorityExtensions { public static bool IsUrgent(this Priority self) => true; }}§/CSHARP\n",
                    False, detail="Expected exactly one native Priority enum extension")
        self.verify("Priority", source("Priority", body=False) + "  §EEXT{Priority}\n"
                    + "  §CSHARP{public static class ForeignExtensions { public static bool IsUrgent(this Priority self) => true; }}§/CSHARP\n",
                    False, detail="Expected exactly one native IsUrgent")
        self.verify("Priority", source("Priority") + "  §EEXT{Priority}\n", False)

    def test_input_runtime_and_selected_prerequisite_failures(self):
        for name in TASKS:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "invalid source", False, detail="Calor compilation failed")
        self.verify("Priority", source("Priority", body='§E{throw}\n§TH STR:"enum runtime failure"'),
                    False, detail="enum runtime failure")
        self.verify("Priority", source("Priority", body="§WH true\n  §CN\n§R true"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("Status", source("Status"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        for configuration in ("", "..", "../Debug"):
            self.verify("Status", source("Status"), False,
                        env={"CALOR_COMPILER_CONFIGURATION": configuration}, detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("Status", source("Status"), False,
                    env={"CALOR_COMPILER_ROOT": ""}, detail="CALOR_COMPILER_ROOT")
        with tempfile.TemporaryDirectory(prefix="calor-enum-selected-") as temporary:
            arm = Path(temporary)
            self.verify("Status", source("Status"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            output = self.verify("Status", source("Status"), env={"CALOR_COMPILER_ROOT": str(arm)})
            self.assertIn(str(arm / relative / "calor.dll"), output)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[os.sys.platform]
            (arm / relative / native).write_bytes(b"not a native Z3 library")
            self.verify("Status", source("Status"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Verifier command exited")
            (arm / relative / native).unlink()
            self.verify("Status", source("Status"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")


def run_compatibility(old, current, deadline):
    test = EnumVerifierTests()
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for index, (text, expected, detail) in enumerate(cases(name)):
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Enum compatibility exceeded its overall deadline.")
                output = test.verify(name, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root),
                                          "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                print(f"{arm} {name} {index}: {'passed' if expected else 'rejected'}")
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Enum compatibility did not report its selected compiler arm.")
    test.verify("Status", historical_syntax(source("Status")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    expected_count = 2 * sum(len(cases(name)) for name in TASKS) + 1
    if expected_count != 127 or EnumVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected exactly 127 actual old/current enum controls, got {EnumVerifierTests.entry_points_executed}.")
    print("Compatibility passed: 127 actual old/current enum entry-point controls.")


if __name__ == "__main__":
    unittest.main()
