#!/usr/bin/env python3
"""Real basic-001..004 entrypoint controls; no provider calls or active fixtures."""

import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASK_NAMES = {
    "GetZero": "01_simple_function", "Multiply": "02_params_function",
    "IsPositive": "03_return_type", "PrintNumber": "04_effect_declaration",
}
SIGNATURES = {
    "GetZero": "§F{GetZero:pub} () -> i32",
    "Multiply": "§F{Multiply:pub} (i32:a, i32:b) -> i32",
    "IsPositive": "§F{IsPositive:pub} (i32:x) -> bool",
    "PrintNumber": "§F{PrintNumber:pub} (i32:x) -> void",
}
BODIES = {
    "GetZero": "§R 0", "Multiply": "§R (* a b)",
    "IsPositive": "§R (> x 0)", "PrintNumber": "§P x",
}
ALTERNATIVES = {
    "GetZero": "§B{zero:int} 0\n§R zero",
    "Multiply": "§B{product:int} (* b a)\n§R product",
    "IsPositive": "§IF (<= x 0)\n  §R false\n§R true",
    "PrintNumber": "§C{Console.WriteLine} §A x §/C",
}
WRONG = {"GetZero": "§R 1", "Multiply": "§R (+ a b)",
         "IsPositive": "§R (>= x 0)", "PrintNumber": "§P 0"}
COUNTS = {"GetZero": 1, "Multiply": 205, "IsPositive": 11, "PrintNumber": 11}


def source(name, *, body=None, signature=None, contracts="", effect="cw", overflow=None):
    lines = ([] if name != "PrintNumber" or effect is None else [f"§E{{{effect}}}"])
    lines += contracts.splitlines() + (BODIES[name] if body is None else body).splitlines()
    module = "Calculator" + (f":overflow={overflow}" if overflow else "")
    return f"§M{{{module}}}\n  " + (signature or SIGNATURES[name]) + "\n" + "".join(
        "    " + line + "\n" for line in lines)


def historical_syntax(text):
    """IDs are only emitted into disposable actual-old-parser input strings."""
    lines = []
    serial = 0
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(("§M{", "§F{")):
            serial += 1
            line = line.replace("{", f"{{id{serial}:", 1)
        elif stripped.startswith("§IF "):
            serial += 1
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


class BasicSyntaxVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real basic-syntax verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-basic-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/basic-syntax" / TASK_NAMES[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name};", result.stdout)
                self.assertIn(f"{COUNTS[name]} " + ("exact stdout cases." if name == "PrintNumber"
                                                   else "runtime cases."), result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_alternatives_aliases_and_optional_contracts(self):
        for name in TASK_NAMES:
            for body in (BODIES[name], ALTERNATIVES[name]):
                self.verify(name, source(name, body=body))
                self.verify(name, source(name, body=body, signature=SIGNATURES[name].replace("i32", "int"),
                                         contracts="§Q true\n§S true"))
            self.verify(name, source(name, contracts="§Q (|| true false)\n§S true")
                        + "  §F{Unrelated:pub} () -> i32\n    §S (== result 7)\n    §R 7\n")
        self.verify("Multiply", source("Multiply", signature="§F{Multiply:pub} (int:a, i32:b) -> int"))
        self.verify("Multiply", source("Multiply", contracts="§S (== result (* a b))"))
        self.verify("GetZero", source("GetZero", contracts="§S (== result 0)"))
        self.verify("IsPositive", source("IsPositive", contracts="§S (== result (> x 0))"))
        self.verify("PrintNumber", source("PrintNumber", effect="cw,cr"))

    def test_checked_and_unchecked_representable_products(self):
        for overflow in ("checked", "unchecked"):
            self.verify("Multiply", source("Multiply", overflow=overflow))

    def test_missing_private_comment_only_and_stubs(self):
        for name in TASK_NAMES:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "// " + " ".join((SIGNATURES[name], BODIES[name]))
                        + "\n§M{Calculator}\n", False)
            self.verify(name, source(name).replace(f"§F{{{name}:", "§F{Other:"), False,
                        detail="Expected exactly one")
            self.verify(name, source(name, signature=SIGNATURES[name].replace(":pub", ":priv")), False,
                        detail="Wrong public signature")
            self.verify(name, source(name, body=""), False)
            self.verify(name, source(name, body=WRONG[name]), False)

    def test_exact_signatures(self):
        for name in TASK_NAMES:
            self.verify(name, source(name, signature=SIGNATURES[name].split("->")[0] + "-> i64"), False)
            self.verify(name, source(name, signature=SIGNATURES[name].replace("} ", "}<T> ", 1)), False)
            self.verify(name, source(name, signature=SIGNATURES[name].replace("§F{", "§AF{")), False)
        self.verify("GetZero", source("GetZero", signature="§F{GetZero:pub} (i32:x) -> i32"), False,
                    detail="Wrong public signature")
        self.verify("GetZero", source("GetZero", signature="§F{GetZero:pub} () -> void", body=""), False)
        self.verify("Multiply", source("Multiply", signature="§F{Multiply:pub} (i32:b, i32:a) -> i32"), False,
                    detail="Wrong public signature")
        for name in ("Multiply", "IsPositive", "PrintNumber"):
            self.verify(name, source(name, signature=SIGNATURES[name].replace("i32:", "i64:", 1)), False)
            self.verify(name, source(name, signature=SIGNATURES[name].replace(":x", ":other").replace(":a", ":other")),
                        False)

    def test_optional_guards_do_not_narrow_prompt_domain(self):
        for name in TASK_NAMES:
            guards = ("false",) if name == "GetZero" else (
                f"(>= {'a' if name == 'Multiply' else 'x'} 0)",
                f"(!= {'a' if name == 'Multiply' else 'x'} 17)", "false")
            for guard in guards:
                self.verify(name, source(name, contracts=f"§Q {guard}\n§S true"), False,
                            detail="Basic preconditions exclude prompt inputs")
            self.verify(name, source(name, contracts="§S false"), False)
        self.verify("IsPositive", source("IsPositive", contracts="§Q (>= x INT:-2147483648)"))
        self.verify("Multiply", source("Multiply", contracts="§Q (|| (<= a 0) (> a 0))"))
        self.verify("IsPositive", source("IsPositive",
                    signature="§F{IsPositive:pub} (i32:x | (>= # INT:-2147483648)) -> bool"))
        self.verify("IsPositive", source("IsPositive",
                    signature="§F{IsPositive:pub} (i32:x | (> # 0)) -> bool"), False,
                    detail="Basic preconditions exclude prompt inputs")

    def test_independent_product_boundary_oracles(self):
        for body in ("§R a", "§R b", "§R 0", "§R (- a b)",
                     "§IF (== a b)\n  §R 0\n§R (* a b)",
                     "§IF (== a -46340)\n  §R 0\n§R (* a b)",
                     "§IF (== a INT:-2147483648)\n  §R 0\n§R (* a b)"):
            self.verify("Multiply", source("Multiply", body=body), False, detail="expected")
        self.verify("IsPositive", source("IsPositive", body="§R (!= x 0)"), False)
        self.verify("IsPositive", source("IsPositive",
                    body="§IF (== x INT:2147483647)\n  §R false\n§R (> x 0)"), False)

    def test_original_effect_and_exact_output(self):
        for effect in (None, "", "cr"):
            self.verify("PrintNumber", source("PrintNumber", effect=effect), False)
        unrelated = "  §F{Decoy:pub} (i32:x) -> void\n    §E{cw}\n    §P x\n"
        self.verify("PrintNumber", source("PrintNumber", effect=None, body="") + unrelated, False,
                    detail="Declared console-write effect")
        for body in ('// §P x\n§P "x"', '§P x\n§P "extra"', '§P "PrintNumber §E{cw}"',
                     "§IF (== x INT:-2147483648)\n  §P 0\n§EL\n  §P x",
                     "§C{Console.Write} §A x §/C"):
            self.verify("PrintNumber", source("PrintNumber", body=body), False, detail="Wrong stdout")
        self.verify("PrintNumber", source("PrintNumber").replace("PrintNumber", "PrintResult"), False)

    def test_selector_collision_preserves_required_logic_proofs(self):
        specification = importlib.util.spec_from_file_location("logic_controls", HERE / "test-logic-verifiers.py")
        logic = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(logic)
        test = logic.LogicVerifierTests()
        for contracts in ("", "§S true", "§S (|| (! result) (> x 0))"):
            text = source("IsPositive", contracts=contracts)
            self.verify("IsPositive", text)
            test.verify("IsPositive", text, False, detail="Missing/weakened postconditions")
            unrelated = "  §F{Unrelated:pub} (i32:x) -> bool\n    §S (== result (> x 0))\n    §R (> x 0)\n"
            self.verify("IsPositive", text + unrelated)
            test.verify("IsPositive", text + unrelated, False, detail="Missing/weakened postconditions")
        print(f"Executed {logic.LogicVerifierTests.entry_points_executed} additional logic selector-collision controls.")

    def test_compile_runtime_and_deadline_failures(self):
        self.verify("GetZero", "invalid source", False, detail="Calor compilation failed")
        self.verify("GetZero", source("GetZero", body='§E{throw}\n§TH STR:"basic runtime failure"'),
                    False, detail="basic runtime failure")
        self.verify("GetZero", source("GetZero", body="§WH true\n  §CN\n§R 0"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("PrintNumber", source("PrintNumber", body='§WH true\n  §P "many"'), False,
                    detail="Excessive stdout")
        self.verify("GetZero", source("GetZero"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        with tempfile.TemporaryDirectory(prefix="calor-basic-missing-arm-") as arm:
            self.verify("GetZero", source("GetZero"), False,
                        env={"CALOR_COMPILER_ROOT": arm}, detail="Selected compiler dependency missing")


def run_compatibility(old, current, deadline):
    test = BasicSyntaxVerifierTests()
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASK_NAMES:
            cases = [
                (source(name), True, None),
                (source(name, body=ALTERNATIVES[name], signature=SIGNATURES[name].replace("i32", "int"),
                        contracts="§Q true\n§S true"), True, None),
                (source(name, body=WRONG[name]), False, None),
                (source(name, signature=SIGNATURES[name].replace(":pub", ":priv")), False, "Wrong public signature"),
                (source(name, contracts="§Q false"), False, "Basic preconditions exclude prompt inputs"),
            ]
            if name == "PrintNumber":
                cases += [(source(name, effect=None, body=""), False, "Declared console-write effect"),
                          (source(name, body='§P x\n§P "extra"'), False, "Wrong stdout")]
            if name == "Multiply":
                cases += [(source(name, overflow=mode), True, None) for mode in ("checked", "unchecked")]
            for text, expected, detail in cases:
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Basic compatibility exceeded its overall deadline.")
                output = test.verify(name, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root),
                                          "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                print(f"{arm} {name}: {'passed' if expected else 'rejected'}")
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Basic compatibility did not report its selected compiler arm.")
    test.verify("GetZero", historical_syntax(source("GetZero")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    if BasicSyntaxVerifierTests.entry_points_executed != 49:
        raise RuntimeError("Expected exactly 49 actual old/current basic compatibility controls.")
    print("Compatibility passed: 49 actual old/current basic entry-point controls.")


if __name__ == "__main__":
    unittest.main()
