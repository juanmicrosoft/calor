#!/usr/bin/env python3
"""Real idiom-001..002 controls; source strings and disposable native inputs only."""

import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {"Clamp": "01_option_return", "IsEven": "02_match_option"}
SIGNATURES = {
    "Clamp": "§F{Clamp:pub} (i32:min, i32:max, i32:value) -> i32",
    "IsEven": "§F{IsEven:pub} (i32:n) -> bool",
}
CONTRACTS = {
    "Clamp": "§Q (<= min max)\n§S (>= result min)\n§S (<= result max)",
    "IsEven": "§S (== result (== (% n 2) 0))",
}
BODIES = {
    "Clamp": "§IF (< value min)\n  §R min\n§IF (> value max)\n  §R max\n§R value",
    "IsEven": "§R (== (% n 2) 0)",
}
ALTERNATIVES = {
    "Clamp": "§R (? (> value max) max (? (< value min) min value))",
    "IsEven": "§IF (!= (% n 2) 0)\n  §R false\n§R true",
}
EQUIVALENT = {
    "Clamp": "§Q (! (> min max))\n§S (&& (<= min result) (>= max result))",
    "IsEven": "§S (&& (|| (! result) (== (% n 2) 0)) (|| result (!= (% n 2) 0)))",
}
WEAK = {
    "Clamp": "§Q (<= min max)\n§S (|| (>= result min) (<= result max))",
    "IsEven": "§S (|| (! result) (== (% n 2) 0))",
}
BOUNDARIES = [-2147483648, -2147483647, -1073741824, -46340, -100, -7, -3, -2, -1,
              0, 1, 2, 3, 7, 100, 46340, 1073741823, 2147483646, 2147483647]
COUNTS = {"Clamp": 3610, "IsEven": len(set(BOUNDARIES) | set(range(-256, 257)))}


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def historical_syntax(text):
    # Reuse the existing actual-old IDs/refinement adapter, not a copied parser.
    return load("idiom_old_syntax", "test-contract-writing-verifiers.py").historical_syntax(text)


def source(name, *, body=None, contracts=None, signature=None, overflow=None):
    module = "Calculator" + (f":overflow={overflow}" if overflow else "")
    lines = (CONTRACTS[name] if contracts is None else contracts).splitlines()
    lines += (BODIES[name] if body is None else body).splitlines()
    return f"§M{{{module}}}\n  " + (signature or SIGNATURES[name]) + "\n" + "".join(
        "    " + line + "\n" for line in lines)


def inline_source(name, *, narrowed=False):
    if name == "Clamp":
        predicate = "(< # max)" if narrowed else "(<= # max)"
        signature = f"§F{{Clamp:pub}} (i32:min | {predicate}, i32:max, i32:value) -> i32"
        contracts = "\n".join(CONTRACTS[name].splitlines()[1:])
    else:
        predicate = "(>= # 0)" if narrowed else "(>= # INT:-2147483648)"
        signature = f"§F{{IsEven:pub}} (i32:n | {predicate}) -> bool"
        contracts = CONTRACTS[name]
    return source(name, signature=signature, contracts=contracts)


def cases(name):
    """Shared real entrypoint cases for the actual old/current compiler arms."""
    parameter = "value" if name == "Clamp" else "n"
    optional = "§S (|| (== result min) (|| (== result max) (== result value)))" if name == "Clamp" else "§S (== (== (% n 2) 0) result)"
    wrong_body = "§R min" if name == "Clamp" else "§R false"
    wrong_boundary = ("§IF (== min INT:-2147483648)\n  §R max\n" if name == "Clamp"
                      else "§IF (== n INT:-2147483648)\n  §R false\n")
    return [
        (source(name), True, None),
        (source(name, body=ALTERNATIVES[name], contracts=EQUIVALENT[name],
                signature=SIGNATURES[name].replace("i32", "int")), True, None),
        (inline_source(name), True, None),
        (source(name, contracts=CONTRACTS[name] + "\n§Q true\n§S true\n" + optional), True, None),
        (source(name, signature=SIGNATURES[name].replace("i32:", "int:", 1)), True, None),
        (source(name, contracts=""), False, "writing"),
        (source(name, contracts=WEAK[name]), False, "Missing/weakened writing postconditions"),
        (source(name, contracts="§Q false\n§S false"), False, "Missing/wrong writing preconditions"),
        (inline_source(name, narrowed=True), False, "Missing/wrong writing preconditions"),
        (source(name, contracts=CONTRACTS[name] + f"\n§Q (!= {parameter} 17)"),
         False, "Missing/wrong writing preconditions"),
        (source(name, contracts=CONTRACTS[name] + "\n§S false"),
         False, "Writing postconditions exclude a required output"),
        (source(name, contracts=CONTRACTS[name] + f"\n§S (== (/ 0 (- {parameter} {parameter})) 0)"),
         False, "undefined"),
        (source(name, contracts=CONTRACTS[name] + f"\n§S (== (+ {parameter} 1) (+ {parameter} 1))"),
         False, "undefined"),
        (source(name, signature=SIGNATURES[name].replace(":pub", ":priv")),
         False, "Wrong public signature"),
        (source(name, signature=SIGNATURES[name].replace("i32:", "i64:", 1),
                body="§R 0" if name == "Clamp" else "§R false"),
         False, "Wrong public signature"),
        (source(name).replace(f"§F{{{name}:", "§F{Other:"), False, "Expected exactly one"),
        ("// " + source(name).replace("\n", " ") + "\n§M{Calculator}\n", False, "Expected exactly one"),
        (source(name, contracts="") + source(name).replace("§M{Calculator}\n", "").replace(
            f"§F{{{name}:", "§F{Decoy:"), False, "writing"),
        (source(name, body=wrong_body), False, None),
        (source(name, body=wrong_boundary + BODIES[name]), False, None),
    ]


class IdiomVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real idiom verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-idiom-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/calor-idioms" / TASKS[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name}; idiom predicates and {COUNTS[name]} runtime cases;", result.stdout)
                self.assertIn(f"{3 if name == 'Clamp' else 0} domain rejection cases.", result.stdout)
                self.assertIn("Limits: full signed Int32 prompt domain", result.stdout)
                self.assertIn("not a universal body proof", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_actual_entrypoints_shared_with_old_arm(self):
        for name in TASKS:
            for text, expected, detail in cases(name):
                with self.subTest(name=name, detail=detail, expected=expected):
                    self.verify(name, text, expected, detail=detail)

    def test_mandatory_exact_contracts_and_optional_definedness(self):
        for index in range(3):
            required = CONTRACTS["Clamp"].splitlines()
            self.verify("Clamp", source("Clamp", contracts="\n".join(required[:index] + required[index + 1:])), False)
        self.verify("Clamp", source("Clamp", contracts="§Q (< min max)\n§S (&& (>= result min) (<= result max))"),
                    False, detail="Missing/wrong writing preconditions")
        for post in ("§S (|| result (!= (% n 2) 0))",
                     "§S (== result (== (% n 2) 1))",
                     "§S (== result (== (% n 2) -1))",
                     "§S (== result (== (% n 2) 0))\n§Q (>= n 0)"):
            self.verify("IsEven", source("IsEven", contracts=post), False)
        for name in TASKS:
            parameter = "value" if name == "Clamp" else "n"
            for post in (f"§S (== (% {parameter} -1) 0)", f"§S (== (cast i64 {parameter}) (cast i64 {parameter}))",
                         "§S (== §C{Identity} §A 1 §/C 1)"):
                extra = "  §F{Identity:priv} (i32:x) -> i32\n    §R x\n"
                self.verify(name, source(name, contracts=CONTRACTS[name] + "\n" + post) + extra, False)
            for contracts in (CONTRACTS[name] + f"\n§Q (== (% {parameter} 2) (% {parameter} 2))",
                              CONTRACTS[name] + f"\n§S (|| true (== (/ 1 (- {parameter} {parameter})) 0))"):
                self.verify(name, source(name, contracts=contracts))
            self.verify(name, source(name, contracts=CONTRACTS[name] + "\n§S (== local 0)",
                        body="§B{local:i32} 0\n" + BODIES[name]), False)
            self.verify(name, source(name, contracts=CONTRACTS[name] + "\n§S (== # 0)"), False)
        self.verify("IsEven", source("IsEven", contracts="§S (? (== (% n 2) 0) result (! result))"))

    def test_exact_signatures_and_runtime_decoys(self):
        for name in TASKS:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, source(name, body=""), False)
            self.verify(name, source(name) + source(name).replace("§M{Calculator}\n", ""), False)
            for overflow in ("checked", "unchecked"):
                self.verify(name, source(name, overflow=overflow))
        for signature in (
            "§F{Clamp:pub} (i32:value, i32:min, i32:max) -> i32",
            "§F{Clamp:pub} (i32:min, i32:max, i32:value, i32:extra) -> i32",
            "§F{Clamp:pub} (i32:min, i32:max, i32:value) -> i64",
        ):
            self.verify("Clamp", source("Clamp", signature=signature), False, detail="Wrong public signature")
        self.verify("IsEven", source("IsEven", signature="§F{IsEven:pub} (i32:n) -> i32",
                    contracts="§S (== result 0)", body="§R 0"), False, detail="Wrong public signature")
        for body in ("§R max", "§R value", "§R (? (< value min) min max)"):
            self.verify("Clamp", source("Clamp", body=body), False)
        for body in ("§R true", "§R (== (% n 2) 1)", "§R (>= n 0)",
                     "§B{unused:bool} (== (% n 2) 0)\n§R false",
                     "§IF (== n INT:2147483647)\n  §R true\n" + BODIES["IsEven"],
                     "§IF (== n -255)\n  §R true\n" + BODIES["IsEven"]):
            self.verify("IsEven", source("IsEven", body=body), False)

    def test_preserved_selector_collisions(self):
        logic = load("idiom_logic_controls", "test-logic-verifiers.py")
        basic = load("idiom_basic_controls", "test-basic-syntax-verifiers.py")
        logic_test = logic.LogicVerifierTests()
        basic_test = basic.BasicSyntaxVerifierTests()
        for name in ("Clamp", "IsPositive", "Max"):
            logic_test.verify(name, logic.source(name))
            logic_test.verify(name, logic.source(name, contracts=""), False)
        basic_test.verify("IsPositive", basic.source("IsPositive"))
        basic_test.verify("IsPositive", basic.source("IsPositive", body="§R (>= x 0)"), False)
        logic_test.verify("Clamp", source("Clamp"), False, detail="Wrong public signature")
        self.verify("Clamp", logic.source("Clamp"), False, detail="Wrong public signature")
        print("Executed 7 preserved logic and 2 basic selector-collision controls.")

    def test_preserved_enum_and_flow_guards(self):
        enums = load("idiom_enum_controls", "test-enum-verifiers.py")
        flow = load("idiom_flow_controls", "test-control-flow-verifiers.py")
        enum_test = enums.EnumVerifierTests()
        flow_test = flow.ControlFlowVerifierTests()
        for name in ("Status", "Priority"):
            enum_test.verify(name, enums.source(name))
        enum_test.verify("Priority", enums.source("Priority", body="§R true"), False)
        flow_test.verify("PrintNumbers", flow.source("PrintNumbers"))
        flow_test.verify("PrintNumbers", flow.source("PrintNumbers", body="§P 1\n§P 2\n§P 3\n§P 4\n§P 5"),
                         False, detail="Required native control flow")
        print("Executed 3 preserved enum and 2 control-flow guards.")

    def test_selected_arm_native_only_and_timeout_guards(self):
        for name in TASKS:
            self.verify(name, source(name), False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        self.verify("IsEven", source("IsEven", body="§WH true\n  §CN\n§R true"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        for configuration in ("", "..", "../Debug"):
            self.verify("IsEven", source("IsEven"), False, env={"CALOR_COMPILER_CONFIGURATION": configuration},
                        detail="CALOR_COMPILER_CONFIGURATION")
        with tempfile.TemporaryDirectory(prefix="calor-idiom-missing-arm-") as temporary:
            arm = Path(temporary)
            self.verify("Clamp", source("Clamp"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"not a native Z3 library")
            for name in TASKS:
                self.verify(name, source(name), False, env={"CALOR_COMPILER_ROOT": str(arm)})
            (arm / relative / native).unlink()
            self.verify("IsEven", source("IsEven"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
        result = subprocess.run(
            [sys.executable, str(HERE / "lib/verify-logic.py"), "--check-native"],
            env=dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT)), text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("Selected native Z3 prerequisite passed: SATISFIABLE.", result.stdout)
        print("Executed 1 preserved native-only control.")


def run_compatibility(old, current, deadline):
    test = IdiomVerifierTests()
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for text, expected, detail in cases(name):
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Idiom compatibility exceeded its 600-second deadline.")
                output = test.verify(name, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root),
                                          "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Idiom compatibility did not report its actual selected compiler arm.")
                print(f"{arm} {name}: {'passed' if expected else 'rejected'}")
    remaining = min(120, deadline - time.monotonic())
    if remaining <= 0:
        raise RuntimeError("Idiom compatibility exceeded its 600-second deadline.")
    test.verify("IsEven", historical_syntax(source("IsEven")), False,
                env={"CALOR_COMPILER_ROOT": str(current), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                detail="Calor compilation failed")
    expected_count = 2 * sum(len(cases(name)) for name in TASKS) + 1
    if expected_count != 81 or IdiomVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected exactly {expected_count} actual old/current idiom controls.")
    print(f"Compatibility passed: {expected_count} actual old/current idiom entry-point controls.")


if __name__ == "__main__":
    unittest.main()
