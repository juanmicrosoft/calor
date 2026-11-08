#!/usr/bin/env python3
"""Real native scalar pattern controls; no agent/provider invocation."""

import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest

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


def execute(profile, text, expected=True, root=ROOT, deadline=None):
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
        COUNT += 1
        try:
            output = PROCESS.run(command, HERE, deadline, environment)
        except RuntimeError as error:
            if expected:
                raise AssertionError(f"{profile}: {error}") from error
            if "Verifier timeout" in str(error):
                raise AssertionError(f"{profile}: infrastructure timeout is not a behavioral rejection") from error
            if "Verifier command exited 1:" not in str(error):
                raise
            return str(error)
        if not expected:
            raise AssertionError(f"{profile}: unexpected acceptance:\n{output}")
        if "actual native arms and successful input/output coupling." not in output:
            raise AssertionError(f"{profile}: missing native behavioral evidence:\n{output}")
        configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
        selected = root.resolve() / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
        if f"Selected compiler: {selected}" not in output.splitlines():
            raise AssertionError(f"{profile}: wrong selected compiler identity:\n{output}")
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


if __name__ == "__main__":
    program = unittest.main(verbosity=2, exit=False)
    print(f"Executed {COUNT} real scalar pattern verify.sh controls.")
    sys.exit(0 if program.result.wasSuccessful() else 1)
