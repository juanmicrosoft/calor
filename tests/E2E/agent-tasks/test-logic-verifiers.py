#!/usr/bin/env python3
"""Executable controls for the real logic-task verify.sh entry points."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
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
        "§F{Factorial:pub} (i32:n) -> i32",
        "§Q (>= n 0)\n§Q (<= n 5)\n§S (>= result 1)",
        "§IF (<= n 1)\n  §R 1\n§EI (== n 2)\n  §R 2\n§EI (== n 3)\n  §R 6\n§EI (== n 4)\n  §R 24\n§R 120",
        "§R (? (<= n 1) 1 (? (== n 2) 2 (? (== n 3) 6 (? (== n 4) 24 120))))",
    ),
    "Clamp": (
        "§F{Clamp:pub} (i32:value, i32:min, i32:max) -> i32",
        "§Q (<= min max)\n§S (>= result min)\n§S (<= result max)",
        "§IF (< value min)\n  §R min\n§IF (> value max)\n  §R max\n§R value",
        "§R (? (< value min) min (? (> value max) max value))",
    ),
    "IsPositive": (
        "§F{IsPositive:pub} (i32:x) -> bool",
        "§S (== result (> x 0))",
        "§IF (> x 0)\n  §R true\n§R false",
        "§R (> x 0)",
    ),
    "Max": (
        "§F{Max:pub} (i32:a, i32:b) -> i32",
        "§S (>= result a)\n§S (>= result b)\n§S (|| (== result a) (== result b))",
        "§IF (>= a b)\n  §R a\n§R b",
        "§R (? (>= a b) a b)",
    ),
}


def source(name, *, contracts=None, body=None, signature=None, expression=False):
    declaration, required, statement, alternative = DECLARATIONS[name]
    lines = (required if contracts is None else contracts).splitlines()
    lines += (body if body is not None else alternative if expression else statement).splitlines()
    return "§M{Calculator}\n  " + (signature or declaration) + "\n" + "".join(
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

    def test_equivalent_contract_forms(self):
        alternatives = {
            "Factorial": "§Q (&& (<= 0 n) (>= 5 n))\n§S (<= 1 result)",
            "Clamp": "§Q (! (> min max))\n§S (&& (<= min result) (>= max result))",
            "IsPositive": "§S (&& (|| (! result) (> x 0)) (|| result (<= x 0)))",
            "Max": "§S (&& (<= a result) (&& (<= b result) (|| (== a result) (== b result))))",
        }
        for name, contracts in alternatives.items():
            with self.subTest(name=name):
                self.verify(name, source(name, contracts=contracts, expression=True))

    def test_missing_wrong_and_comment_only_functions(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                self.verify(name, source(name).replace(f"§F{{{name}:", f"§F{{Other:"), False,
                            detail="Expected exactly one")
                self.verify(name, "// " + " ".join(DECLARATIONS[name]) + "\n§M{Calculator}\n", False)
                self.verify(name, source(name, signature=DECLARATIONS[name][0].replace(":pub", ":priv")),
                            False, detail="Wrong public signature")
                self.verify(name, source(name, body="§R false" if name == "IsPositive" else "§R 1"), False)
                self.verify(name, source(name, body=""), False)

    def test_wrong_signatures(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                signature = DECLARATIONS[name][0].replace("i32:", "i64:", 1)
                self.verify(name, source(name, signature=signature), False)
        self.verify("IsPositive", source("IsPositive", signature="§F{IsPositive:pub} (i32:x) -> i32",
                                        body="§R 1", contracts="§S (>= result 1)"), False)
        self.verify("Max", source("Max", signature="§F{Max:pub} (i32:b, i32:a) -> i32"), False,
                    detail="Wrong public signature")

    def test_missing_each_contract_and_unrelated_contracts(self):
        for name in TASK_NAMES:
            required = DECLARATIONS[name][1].splitlines()
            for index in range(len(required)):
                with self.subTest(name=name, index=index):
                    text = source(name, contracts="\n".join(required[:index] + required[index + 1:]))
                    unrelated = (
                        "  §F{Unrelated:pub} (i32:n, i32:value, i32:min, i32:max, i32:x, i32:a, i32:b) -> "
                        + ("bool" if name == "IsPositive" else "i32") + "\n"
                        + "".join("    " + line + "\n" for line in required)
                        + "    §R " + ("(> x 0)" if name == "IsPositive" else "1") + "\n"
                    )
                    self.verify(name, text + unrelated, False)

    def test_weakened_and_vacuous_contracts(self):
        weak = {
            "Factorial": "§Q (>= n 0)\n§Q (<= n 5)\n§S (>= result 0)",
            "Clamp": "§Q (<= min max)\n§S (|| (>= result min) (<= result max))",
            "IsPositive": "§S (|| (! result) (> x 0))",
            "Max": "§S (>= result a)\n§S (>= result b)\n§S (|| (>= result a) (>= result b))",
        }
        for name, contracts in weak.items():
            with self.subTest(name=name):
                self.verify(name, source(name, contracts=contracts), False, detail="Missing/weakened postconditions")
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\n§S false"),
                            False, detail="Postconditions exclude")
                self.verify(name, source(name, contracts=DECLARATIONS[name][1] + "\n§Q false"),
                            False, detail="Missing/wrong preconditions")
        self.verify("Factorial", source("Factorial", contracts="§Q (>= n 0)\n§Q (<= n 4)\n§S (>= result 1)"),
                    False, detail="Missing/wrong preconditions")

    def test_wrong_boundary_results(self):
        for n, wrong in ((0, 2), (1, 2), (2, 3), (3, 7), (4, 25), (5, 121)):
            with self.subTest(n=n):
                self.verify("Factorial", source("Factorial", body=f"§IF (== n {n})\n  §R {wrong}\n"
                                               + DECLARATIONS["Factorial"][2]), False, detail="expected")
        self.verify("Clamp", source("Clamp", body="§R min"), False, detail="expected")
        self.verify("Clamp", source("Clamp", body="§R max"), False, detail="expected")
        self.verify("IsPositive", source("IsPositive", body="§R (>= x 0)"), False)
        self.verify("IsPositive", source("IsPositive", body="§R (!= x 0)"), False)
        self.verify("Max", source("Max", body="§R (+ a b)"), False)
        self.verify("Max", source("Max", body="§IF (== a b)\n  §R 0\n" + DECLARATIONS["Max"][2]), False)
        self.verify("Max", source("Max", body="§IF (== a INT:2147483647)\n  §R b\n"
                                + DECLARATIONS["Max"][2]), False)
        self.verify("Clamp", source("Clamp", body="§IF (== min INT:-2147483648)\n  §R max\n"
                                  + DECLARATIONS["Clamp"][2]), False)

    def test_input_compile_execution_and_timeout_failures(self):
        self.verify("Factorial", None, False, detail="Input missing")
        self.verify("Factorial", "invalid source", False, detail="Calor compilation failed")
        self.verify("Max", source("Max", body='§E{throw}\n§TH STR:"deliberate runtime failure"'),
                    False, detail="deliberate runtime failure")
        self.verify("Max", source("Max", body="§WH true\n  §CN\n§R a"),
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
            self.verify("Max", source("Max"), env={"CALOR_COMPILER_ROOT": str(arm)},
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


if __name__ == "__main__":
    unittest.main(verbosity=2)
