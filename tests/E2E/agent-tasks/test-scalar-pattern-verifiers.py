#!/usr/bin/env python3
"""Real native scalar pattern controls; no agent/provider invocation."""

import importlib.util
import os
from pathlib import Path
import re
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(HERE.parents[2]))).resolve()
spec = importlib.util.spec_from_file_location("pattern_process", HERE / "lib/verify-logic.py")
PROCESS = importlib.util.module_from_spec(spec)
spec.loader.exec_module(PROCESS)
TASKS = {
    "pm001": ("01_switch_literals", "DayName", "day", "i32"),
    "pm002": ("02_relational_patterns", "ScoreCategory", "score", "i32"),
    "pm003": ("03_variable_with_guard", "DescribeNumber", "n", "i32"),
    "pm006": ("06_wildcard_default", "IsSingleDigit", "n", "bool"),
}
COUNT = 0

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


def control_failure(error):
    output = str(error)
    wrappers = list(re.finditer(r"verifier\s+command\s+exited", output, re.IGNORECASE))
    if (INFRASTRUCTURE.search(output) or TIMEOUT.search(output)
        or any(not output[wrapper.start():].startswith("Verifier command exited 1:\n") for wrapper in wrappers)
        or re.search(r"verification\s+passed\s*:", output, re.IGNORECASE)
        or not output.startswith("Verifier command exited 1:\n")
        or "Verification failed:" not in output):
        raise error
    return output


def assert_positive_output(output, environment):
    if (INFRASTRUCTURE.search(output) or TIMEOUT.search(output)
        or re.search(r"verifier\s+command\s+exited|verification\s+failed\s*:", output, re.IGNORECASE)
        or len(re.findall(r"verification\s+passed\s*:", output, re.IGNORECASE)) != 1):
        raise AssertionError(f"Infrastructure-shaped positive output:\n{output}")
    root = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
    configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    expected = f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
    identities = [line for line in output.splitlines()
                  if re.search(r"selected\s+compiler\s*:", line, re.IGNORECASE)]
    if identities != [expected]:
        raise AssertionError(f"Wrong selected compiler identity:\n{output}")


def source(profile):
    _, name, parameter, result = TASKS[profile]
    if profile == "pm001":
        arms = [f"§K {day} → {100 if day <= 5 else 200}" for day in range(1, 8)] + ["§K _ → 0"]
    elif profile == "pm002":
        arms = ["§K §PREL{gte} 90 → 3", "§K §PREL{gte} 70 → 2", "§K §PREL{gte} 50 → 1", "§K _ → 0"]
    elif profile == "pm003":
        arms = ["§K §VAR{v} §WHEN (> v 0) → 1", "§K §VAR{v} §WHEN (< v 0) → -1", "§K _ → 0"]
    else:
        arms = [f"§K {digit} → true" for digit in range(10)] + ["§K _ → false"]
    return (f"§M{{Calculator}}\n  §F{{{name}:pub}} (i32:{parameter}) -> {result}\n"
            f"    §R §W {parameter}\n" + "".join(f"      {arm}\n" for arm in arms))


def historical(text):
    lines = []
    for index, line in enumerate(text.splitlines()):
        if line.lstrip().startswith(("§M{", "§F{", "§CL{", "§MT{")):
            line = line.replace("{", f"{{p{index}:", 1)
        line = line.replace("§W ", f"§W{{w{index}}} ")
        lines.append(line)
    return "\n".join(lines) + "\n"


def execute(profile, text, expected=True, root=ROOT, deadline=None, *, detail=None):
    global COUNT
    with tempfile.TemporaryDirectory(prefix="calor-pattern-control-") as temporary:
        workspace = Path(temporary)
        if text is not None:
            (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
        environment = dict(os.environ, CALOR_COMPILER_ROOT=str(root))
        environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
        deadline = deadline if deadline is not None else time.monotonic() + 130
        environment["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(120, deadline - time.monotonic()))
        command = ["bash", str(HERE / "tasks/pattern-matching" / TASKS[profile][0] / "verify.sh"), str(workspace)]
        try:
            output = PROCESS.run(command, HERE, deadline, environment)
        except RuntimeError as error:
            output = control_failure(error)
            if expected:
                raise AssertionError(f"{profile}: {error}") from error
            if detail is not None and detail not in output:
                raise AssertionError(f"{profile}: missing behavioral diagnostic {detail!r}:\n{output}")
            COUNT += 1
            return output
        assert_positive_output(output, environment)
        if not expected:
            raise AssertionError(f"{profile}: unexpected acceptance:\n{output}")
        if "actual native arms and successful input/output coupling." not in output:
            raise AssertionError(f"{profile}: missing native behavioral evidence:\n{output}")
        COUNT += 1
        return output


def controls():
    for profile, (_, name, parameter, result) in TASKS.items():
        text = source(profile)
        yield profile, text, True
        yield profile, text.replace("i32:", "int:").replace("-> i32", "-> int"), True
        yield profile, text.replace(f"i32:{parameter}", "i32:inputValue").replace(f"§W {parameter}", "§W inputValue"), True
        yield profile, text.replace(f"    §R §W {parameter}", f"    §B{{alias:i32}} {parameter}\n    §R §W alias"), True
        yield profile, text.replace(f"    §R §W {parameter}", "    §Q true\n    §S true\n" + f"    §R §W {parameter}"), True
        yield profile, text.replace("§K _ →", "§K _ §WHEN true →"), True
        binding = text.replace(f"    §R §W {parameter}", f"    §B{{answer:{result}}} §W {parameter}")
        yield profile, binding + "    §R answer\n", True
        dead = text.replace(f"    §R §W {parameter}", f"    §B{{ignored:{result}}} §W {parameter}")
        yield profile, dead + text[text.index("    §R"):], True
        independent = {
            "pm001": "(? (&& (>= day 1) (<= day 5)) 100 (? (|| (== day 6) (== day 7)) 200 0))",
            "pm002": "(? (>= score 90) 3 (? (>= score 70) 2 (? (>= score 50) 1 0)))",
            "pm003": "(? (> n 0) 1 (? (< n 0) -1 0))",
            "pm006": "(&& (>= n 0) (<= n 9))",
        }[profile]
        yield profile, dead + f"    §R {independent}\n", False
        yield profile, None, False
        yield profile, text.replace(name, "WrongTarget"), False
        yield profile, text.replace(":pub}", ":priv}"), False
        yield profile, text + text[text.index("  §F"):], False
        yield profile, text.replace(f"    §R §W {parameter}", f"    §Q (>= {parameter} 0)\n    §R §W {parameter}"), False
        yield profile, text.replace(f"    §R §W {parameter}", "    §S false\n" + f"    §R §W {parameter}"), False
        yield profile, text.replace(f"    §R §W {parameter}", "    §Q false\n" + f"    §R §W {parameter}"), False
        yield profile, f"§M{{Calculator}}\n  §F{{{name}:pub}} (i32:{parameter}) -> {result}\n    §R {'false' if result == 'bool' else '0'}\n", False
        yield profile, text.replace(f"§W {parameter}", "§W 1"), False
        if profile == "pm001":
            yield profile, text.replace("§K 6 → 200", "§K 6 → 100"), False
        elif profile == "pm002":
            yield profile, text.replace("§PREL{gte} 90", "§PREL{gt} 90"), False
            yield profile, (text.replace("§PREL{gte} 90 → 3", "§PREL{lt} 50 → 0")
                            .replace("§PREL{gte} 70 → 2", "§PREL{lt} 70 → 1")
                            .replace("§PREL{gte} 50 → 1", "§PREL{lt} 90 → 2")
                            .replace("§K _ → 0", "§K _ → 3")), True
        elif profile == "pm003":
            yield profile, ("§M{Calculator}\n  §F{DescribeNumber:pub} (i32:n) -> i32\n"
                            "    §W n\n      §K §VAR{v} §WHEN (> v 0)\n        §R 1\n"
                            "      §K §VAR{v} §WHEN (< v 0)\n        §R -1\n"
                            "      §K _\n        §R 0\n"), True
            yield profile, text.replace("> v", "> n").replace("< v", "< n"), False
            yield profile, (text.replace("(> v 0)", "(&& (== v v) (> n 0))")
                            .replace("(< v 0)", "(&& (== v v) (< n 0))")), False
        else:
            yield profile, text.replace("§K 9 → true", "§K 9 → false"), False


def run_compatibility(old, current, deadline):
    before = COUNT
    selected = {
        "pm001": [0, 1, 2, 3, 5, 6, 7, 8, 11, 13, 17, 18],
        "pm002": [0, 1, 2, 3, 5, 6, 7, 8, 13, 17, 18, 19],
        "pm003": [0, 2, 3, 5, 6, 7, 8, 13, 17, 18, 19, 20],
        "pm006": [0, 1, 2, 3, 5, 6, 7, 8, 11, 13, 17, 18],
    }
    by_profile = {profile: [control for control in controls() if control[0] == profile] for profile in TASKS}
    for label, root in (("v0.29", old), ("current", current)):
        for profile, indices in selected.items():
            for index in indices:
                _, text, expected = by_profile[profile][index]
                execute(profile, historical(text) if label == "v0.29" and text is not None else text,
                        expected, root, deadline)
        print(f"{label}: real scalar pattern controls passed.")
    execute("pm001", historical(source("pm001")), False, current, deadline)
    if COUNT - before != 2 * sum(len(indices) for indices in selected.values()) + 1:
        raise AssertionError("Incomplete scalar old/current control execution.")
    print(f"Compatibility passed: {COUNT - before} actual old/current scalar pattern controls.")


class ScalarPatternVerifierTests(unittest.TestCase):
    def test_real_entrypoint_controls(self):
        for profile, text, expected in controls():
            with self.subTest(profile=profile, expected=expected, text=text):
                execute(profile, text, expected)


class ScalarAdmissionTests(unittest.TestCase):
    def test_actual_execute_finite_failure_and_identity_guards(self):
        global COUNT
        before = COUNT
        outer = "Verifier command exited 1:\n"
        diagnostic = "Verification failed: Wrong output: expected 100"
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
            "Verifier timeout after 2s: dotnet", "Verification passed: DayName",
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
        ]
        try:
            for output in errors:
                with self.subTest(output=output), patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    with self.assertRaises(RuntimeError):
                        execute("pm001", source("pm001"), False, detail="expected")
                    self.assertEqual(COUNT, before)
            for error in (OSError("launch failed"), EOFError("pipe closed"), TimeoutError("expired")):
                with patch.object(PROCESS, "run", side_effect=error), self.assertRaises(type(error)):
                    execute("pm001", source("pm001"), False, detail="expected")
                self.assertEqual(COUNT, before)

            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            selected = ROOT / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
            identity = f"Selected compiler: {selected}\n"
            success = identity + ("Verification passed: DayName; 1031 independent runtime cases, "
                                  "actual native arms and successful input/output coupling.\n")
            invalid = [
                success.replace(str(selected), str(selected) + ".other"),
                success.replace(str(selected), str(selected.parent.parent / "wrong/net10.0/calor.dll")),
                success.replace(str(selected), str(selected.parent / "other.dll")),
                success.replace(str(ROOT), str(ROOT) + "-other"),
                success.replace("/net10.0/", "/net9.0/"),
                success.replace(identity, ""), identity + success, "  " + success,
                success + "  Selected compiler: wrong\n", success + "warning: Selected compiler: wrong\n",
                success.replace("Selected compiler:", "selected compiler:"),
            ] + [success + error + "\n" for error in infrastructure + [diagnostic, outer]]
            for output in invalid:
                with self.subTest(positive=output), patch.object(PROCESS, "run", return_value=output):
                    with self.assertRaises(AssertionError):
                        execute("pm001", source("pm001"))
                    self.assertEqual(COUNT, before)
            for output in (outer + diagnostic, outer + "Verification failed: " + outer + diagnostic):
                with patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    execute("pm001", source("pm001"), False, detail="expected")
            with patch.object(PROCESS, "run", return_value=success):
                execute("pm001", source("pm001"))
            self.assertEqual(COUNT, before + 3)
        finally:
            COUNT = before


if __name__ == "__main__":
    program = unittest.main(verbosity=2, exit=False)
    print(f"Executed {COUNT} real scalar pattern verify.sh controls.")
    sys.exit(0 if program.result.wasSuccessful() else 1)
