#!/usr/bin/env python3
"""Real contract-001..005 controls; only strings and disposable native inputs."""

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
TASKS = {
    "contract001": ("01_precondition", "SquareRoot", "i32:x", "§Q (>= x 0)", "§R x"),
    "contract002": ("02_postcondition", "Abs", "i32:x", "§S (>= result 0)",
                    "§R (? (< x 0) (- 0 x) x)"),
    "contract003": ("03_fix_postcondition", "Abs", "i32:x", "§S (>= result 0)",
                    "§R (? (< x 0) (- 0 x) x)"),
    "contract004": ("04_pre_and_post", "SafeDivide", "i32:a, i32:b",
                    "§Q (!= b 0)\n§Q (== (% a b) 0)\n§S (== (* result b) a)", "§R (/ a b)"),
    "contract005": ("05_loop_invariant", "SumToN", "i32:n",
                    "§Q (>= n 0)\n§S (>= result 0)", "§R (cast i32 (/ (* (cast i64 n) (+ n 1)) 2))"),
}
ALTERNATIVES = {
    "contract001": "§B{identity:int} x\n§R identity",
    "contract002": "§IF (< x 0)\n  §R (- 0 x)\n§R x",
    "contract003": "§IF (< x 0)\n  §R (- 0 x)\n§R x",
    "contract004": "§B{quotient:int} (/ a b)\n§R quotient",
    "contract005": "§B{wide:i64} n\n§B{sum:i64} (/ (+ (* wide wide) wide) 2)\n§R (cast i32 sum)",
}
FACTORED_SUM = ("§IF (== (% n 2) 0)\n  §R (* (/ n 2) (+ n 1))\n"
                "§R (* n (/ (+ n 1) 2))")
NAIVE_SUM = "§R (/ (* n (+ n 1)) 2)"
STRONG_SUM_POST = "§Q (>= n 0)\n§S (>= result 0)\n§S (>= result n)"
EQUIVALENT = {
    "contract001": "§Q (> x -1)\n§S (== result x)",
    "contract002": "§S (! (< result 0))",
    "contract003": "§S (> result -1)",
    "contract004": "§Q (&& (!= 0 b) (== 0 (% a b)))\n§S (== a (* b result))",
    "contract005": "§Q (> n -1)\n§S (! (< result 0))",
}
WEAK = {
    "contract001": "§Q (>= x -1)",
    "contract002": "§S (>= result -1)",
    "contract003": "§S (>= result -1)",
    "contract004": "§Q (!= b 0)\n§S (== (* result b) a)",
    "contract005": "§Q (>= n 0)\n§S (>= result -1)",
}
INPUTS = [-2147483648, -2147483647, -1073741824, -46340, -100, -7, -3, -2, -1,
          0, 1, 2, 3, 7, 100, 46340, 1073741823, 2147483646, 2147483647]
COUNTS = {
    "contract001": sum(x >= 0 for x in INPUTS),
    "contract002": len(INPUTS) - 1,
    "contract003": len(INPUTS) - 1,
    "contract004": sum(b != 0 and a % b == 0 and -2147483648 <= a // b <= 2147483647
                       for a in INPUTS for b in INPUTS),
    "contract005": 65536,
}


def source(profile, *, body=None, contracts=None, signature=None, overflow=None):
    _, name, parameters, required, original = TASKS[profile]
    module = "BuggyContracts" if profile == "contract003" else "Calculator"
    if overflow:
        module += f":overflow={overflow}"
    lines = (required if contracts is None else contracts).splitlines()
    lines += (original if body is None else body).splitlines()
    declaration = signature or f"§F{{{name}:pub}} ({parameters}) -> i32"
    return f"§M{{{module}}}\n  {declaration}\n" + "".join("    " + line + "\n" for line in lines)


def historical_syntax(text):
    """Preserve predicates; use actual v0.29 parameter syntax only in old inputs."""
    lines = []
    serial = 0
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(("§M{", "§F{", "§L{")):
            serial += 1
            line = line.replace("{", f"{{id{serial}:", 1)
            if stripped.startswith("§F{") and " | " in line:
                header, parameters = line.split("} (", 1)
                parameters, output = parameters.rsplit(") -> ", 1)
                lines.append(header + "}")
                for parameter in parameters.split(", "):
                    binding, separator, predicate = parameter.partition(" | ")
                    lines.append("    §I{" + binding + "}" + (separator + predicate if separator else ""))
                lines.append("    §O{" + output + "}")
                continue
        elif stripped.startswith("§IF "):
            serial += 1
            line = line.replace("§IF ", f"§IF{{if{serial}}} ", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


def inline_source(profile, *, narrowed=False):
    _, name, parameters, _, _ = TASKS[profile]
    if profile == "contract004":
        parameters = "i32:a, i32:b | (!= # 0)"
        contracts = "§Q (&& (!= b 0) (== (% a b) 0))\n§S (== (* result b) a)"
    else:
        predicate = "(> # 0)" if narrowed else (
            "(>= # 0)" if profile in ("contract001", "contract005") else "(>= # INT:-2147483648)")
        parameters += " | " + predicate
        contracts = "" if profile == "contract001" else "§S (>= result 0)"
    if narrowed and profile == "contract004":
        contracts += "\n§Q (> a 0)"
    return source(profile, signature=f"§F{{{name}:pub}} ({parameters}) -> i32", contracts=contracts)


def sum_decoy(*, runtime_failure=False):
    gate = "§B{check:i32} (/ 1 unused)" if runtime_failure else "§C{RequirePositive} §A unused §/C"
    text = source("contract005", body="§B{unused:i32} (cast i32 (/ (* (cast i64 n) (+ n 1)) 2))\n"
                  + "§IF (> n 0)\n  " + gate + "\n§R §C{Compute} §A n §/C")
    text += "  §F{Compute:priv} (i32:n) -> i32\n    " + TASKS["contract005"][4] + "\n"
    text += "  §F{RequirePositive:priv} (i32:value) -> i32\n    §Q (> value 0)\n    §R 0\n"
    return text


def sum_loop_decoy(*, widened=True):
    formula = "(cast i32 (/ (* (cast i64 n) (+ n 1)) 2))" if widened else "(/ (* n (+ n 1)) 2)"
    text = source("contract005", body=f"§B{{unused:i32}} {formula}\n"
                  "§IF (> n 0)\n  §B{checkedValue:i32} §C{CheckPositive} §A unused §/C\n"
                  "§B{~sum:i32} 0\n§L{i:0:n:1}\n  §ASSIGN sum (+ sum i)\n§R sum")
    return text + "  §F{CheckPositive:pub} (i32:value) -> i32\n    §Q (> value 0)\n    §R value\n"


class ContractWritingVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real contract-writing verify.sh controls.")

    def verify(self, profile, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-contract-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                filename = "BuggyContracts.calr" if profile == "contract003" else "Calculator.calr"
                (workspace / filename).write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/contract-writing" / TASKS[profile][0] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {TASKS[profile][1]}; contract-writing predicates", result.stdout)
                self.assertIn(f"{COUNTS[profile]} runtime cases;", result.stdout)
                self.assertIn("Limits:", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_positive_native_alternatives_equivalence_and_aliases(self):
        for profile in TASKS:
            self.verify(profile, source(profile))
            _, name, parameters, _, _ = TASKS[profile]
            self.verify(profile, source(profile, body=ALTERNATIVES[profile], contracts=EQUIVALENT[profile],
                        signature=f"§F{{{name}:pub}} ({parameters.replace('i32', 'int')}) -> int"))
            self.verify(profile, source(profile, contracts=TASKS[profile][3] + "\n§Q true\n§S true")
                        + "  §F{Other:pub} () -> i32\n    §S (== result 7)\n    §R 7\n")
            self.verify(profile, inline_source(profile))
        original = (HERE / "fixtures/buggy-contracts/BuggyContracts.calr").read_text(encoding="utf-8")
        self.verify("contract003", original, False, detail="Writing postconditions exclude a required output")
        self.verify("contract003", original.replace("(> result 0)", "(>= result 0)"))

    def test_missing_weak_foreign_and_vacuous_contracts(self):
        for profile in TASKS:
            self.verify(profile, source(profile, contracts=""), False, detail="writing")
            self.verify(profile, source(profile, contracts=WEAK[profile]), False, detail="writing")
            self.verify(profile, source(profile, contracts="§Q false\n§S false"), False,
                        detail="Missing/wrong writing preconditions")
            unrelated = "  §F{Decoy:pub} (" + TASKS[profile][2] + ") -> i32\n"
            unrelated += "".join("    " + line + "\n" for line in
                                 (TASKS[profile][3] + "\n" + TASKS[profile][4]).splitlines())
            self.verify(profile, source(profile, contracts="") + unrelated, False, detail="writing")
            self.verify(profile, source(profile, contracts="// " + TASKS[profile][3].replace("\n", " ")),
                        False, detail="writing")
            strengthened = TASKS[profile][3] + "\n§S (> result 0)"
            self.verify(profile, source(profile, contracts=strengthened), False,
                        detail="Writing postconditions exclude a required output")
        self.verify("contract004", source("contract004", contracts="§Q (!= b 0)\n§Q (== (% a b) 0)"),
                    False, detail="Missing/weakened writing postconditions")
        self.verify("contract004", source("contract004",
                    contracts="§Q (== (% a b) 0)\n§S (== (* result b) a)"), False)
        self.verify("contract004", inline_source("contract004").replace(
                    "§Q (&& (!= b 0) (== (% a b) 0))", "§Q (== (% a b) 0)"),
                    False, detail="DivideByZeroException")

    def test_no_optional_contract_domain_laundering(self):
        for profile in TASKS:
            param = "a" if profile == "contract004" else "n" if profile == "contract005" else "x"
            for predicate in (f"(!= {param} 17)", f"(> {param} 0)"):
                self.verify(profile, source(profile, contracts=TASKS[profile][3] + f"\n§Q {predicate}"),
                            False, detail="Missing/wrong writing preconditions")
            self.verify(profile, inline_source(profile, narrowed=True), False,
                        detail="Missing/wrong writing preconditions")
        self.verify("contract002", source("contract002", contracts="§S (>= result 0)\n§S (!= result 17)"),
                    False, detail="Writing postconditions exclude a required output")
        for profile in TASKS:
            self.verify(profile, source(profile, contracts=TASKS[profile][3] + "\n§S (== result (% result 0))"),
                        False, detail="arithmetic")
            self.verify(profile, source(profile, contracts=TASKS[profile][3] + "\n§S (== result §C{Math.Abs} §A result §/C)"),
                        False)

    def test_signatures_missing_files_stubs_and_decoys(self):
        for profile in TASKS:
            self.verify(profile, None, False, detail="Input missing")
            self.verify(profile, source(profile).replace(f"§F{{{TASKS[profile][1]}:", "§F{Other:"),
                        False, detail="Expected exactly one")
            for declaration in (
                f"§F{{{TASKS[profile][1]}:priv}} ({TASKS[profile][2]}) -> i32",
                f"§F{{{TASKS[profile][1]}:pub}} ({TASKS[profile][2].replace('i32:', 'i64:', 1)}) -> i32",
                f"§F{{{TASKS[profile][1]}:pub}} ({TASKS[profile][2]}) -> i64",
            ):
                self.verify(profile, source(profile, signature=declaration), False)
            self.verify(profile, source(profile, body=""), False)
            self.verify(profile, source(profile, body="§R 1"), False)
        self.verify("contract003", source("contract003", contracts="§S (> result 0)\n// (>= result 0)"),
                    False, detail="Writing postconditions exclude a required output")
        self.verify("contract001", source("contract001", body='§R §C{Math.Sqrt} §A x §/C'), False)

    def test_signed_boundary_behavior_and_overflow_limits(self):
        for profile in TASKS:
            for overflow in ("checked", "unchecked"):
                self.verify(profile, source(profile, overflow=overflow))
        for profile in ("contract002", "contract003"):
            for body in ("§R x", "§R (- 0 x)",
                         "§IF (== x INT:2147483647)\n  §R 1\n§R (? (< x 0) (- 0 x) x)"):
                self.verify(profile, source(profile, body=body), False)
        self.verify("contract004", source("contract004", body="§R (/ b a)"), False)
        self.verify("contract004", source("contract004",
                    body="§IF (< b 0)\n  §R 0\n§R (/ a b)"), False)
        self.verify("contract005", source("contract005", body="§R (/ (* n n) 2)"), False)
        self.verify("contract005", source("contract005",
                    body="§IF (== n 46340)\n  §R 1\n§R (/ (* n (+ n 1)) 2)"), False)
        self.verify("contract005", source("contract005", contracts=TASKS["contract005"][3] + "\n§Q (<= n 46340)"),
                    False, detail="Missing/wrong writing preconditions")
        self.verify("contract005", source("contract005", contracts=TASKS["contract005"][3] + "\n§Q (<= n 65535)"),
                    False, detail="Missing/wrong writing preconditions")
        loop = "§B{~sum:i32} 0\n§L{i:0:n:1}\n  §ASSIGN sum (+ sum i)\n§R sum"
        self.verify("contract005", source("contract005", body=loop), False, detail="Required native Gauss formula")
        self.verify("contract005", source("contract005",
                    body="§B{unused:i32} (cast i32 (/ (* (cast i64 n) (+ n 1)) 2))\n"
                         "§R §C{Compute} §A n §/C")
                    + "  §F{Compute:priv} (i32:n) -> i32\n    " + TASKS["contract005"][4] + "\n",
                    False, detail="Gauss coupling failed")

    def test_all_representable_sums_widening_factoring_and_overflow_rejection(self):
        for body in (TASKS["contract005"][4], ALTERNATIVES["contract005"], FACTORED_SUM,
                     "§R (cast i32 (/ (* n (+ (cast i64 n) 1)) LONG:2))",
                     "§R (cast i32 (/ (+ (* (cast i64 n) n) n) 2))"):
            for overflow in ("checked", "unchecked"):
                self.verify("contract005", source("contract005", body=body, overflow=overflow))
        for overflow in ("checked", "unchecked"):
            self.verify("contract005", source("contract005", body=NAIVE_SUM, overflow=overflow), False)
        # Corrupt only the formerly omitted interval: all lower inputs stay correct.
        for boundary, expected in ((46341, 1073767311), (50000, 1250025000), (65535, 2147450880)):
            self.assertEqual(sum(range(boundary + 1)), expected)
            self.verify("contract005", source("contract005",
                        body=f"§IF (== n {boundary})\n  §R 1\n" + TASKS["contract005"][4]),
                        False, detail=f"SumToN({boundary}) returned 1; expected {expected}")

    def test_gauss_output_coupling_preserves_strong_contracts_and_rejects_errors(self):
        for post in (STRONG_SUM_POST,
                     TASKS["contract005"][3] + "\n§S (|| (== n 0) (> result 0))",
                     TASKS["contract005"][3] + "\n§S (<= result INT:2147450880)"):
            self.verify("contract005", source("contract005", contracts=post))
        self.verify("contract005", sum_decoy(), False, detail="Gauss coupling failed")
        self.verify("contract005", sum_decoy(runtime_failure=True), False, detail="Gauss counterfactual runtime failed")
        self.verify("contract005", sum_loop_decoy(), False, detail="Gauss coupling failed")
        self.verify("contract005", sum_loop_decoy(widened=False), False)
        self.verify("contract005", source("contract005", body="§IF (== n 46341)\n  §R 0\n" + NAIVE_SUM),
                    False, detail="SumToN(46341) returned 0; expected 1073767311")

    def test_failure_deadline_selection_and_original_profiles(self):
        self.verify("contract001", "invalid source", False, detail="Calor compilation failed")
        self.verify("contract001", source("contract001", body='§E{throw}\n§TH STR:"contract runtime failure"'),
                    False, detail="contract runtime failure")
        self.verify("contract001", source("contract001", body="§WH true\n  §CN\n§R x"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("contract001", source("contract001"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        with tempfile.TemporaryDirectory(prefix="calor-contract-missing-arm-") as arm:
            self.verify("contract001", source("contract001"), False,
                        env={"CALOR_COMPILER_ROOT": arm}, detail="Selected compiler dependency missing")
        specification = importlib.util.spec_from_file_location("logic_controls", HERE / "test-logic-verifiers.py")
        logic = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(logic)
        test = logic.LogicVerifierTests()
        for name in logic.TASK_NAMES:
            test.verify(name, logic.source(name))
            test.verify(name, logic.source(name, contracts=""), False)
        print(f"Executed {logic.LogicVerifierTests.entry_points_executed} preserved mandatory logic controls.")


def run_compatibility(old, current, deadline):
    test = ContractWritingVerifierTests()
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for profile in TASKS:
            _, name, parameters, _, _ = TASKS[profile]
            cases = [
                (source(profile), True, None),
                (source(profile, body=ALTERNATIVES[profile], contracts=EQUIVALENT[profile],
                        signature=f"§F{{{name}:pub}} ({parameters.replace('i32', 'int')}) -> int"), True, None),
                (inline_source(profile), True, None),
                (inline_source(profile, narrowed=True), False, "Missing/wrong writing preconditions"),
                (source(profile, contracts=WEAK[profile]), False, "writing"),
                (source(profile, contracts="§Q false\n§S false"), False, "Missing/wrong writing preconditions"),
                (source(profile, body="§R 1"), False, None),
                (source(profile, contracts=TASKS[profile][3] + "\n§S (> result 0)"),
                 False, "Writing postconditions exclude a required output"),
            ]
            if profile == "contract005":
                cases += [(source(profile, body=FACTORED_SUM), True, None),
                          (source(profile, contracts=STRONG_SUM_POST), True, None),
                          (source(profile, body=NAIVE_SUM, overflow="checked"), False, None),
                          (source(profile, body=NAIVE_SUM, overflow="unchecked"), False, None),
                          (sum_decoy(), False, "Gauss coupling failed"),
                          (sum_decoy(runtime_failure=True), False, "Gauss counterfactual runtime failed"),
                          (sum_loop_decoy(), False, "Gauss coupling failed"),
                          (source(profile, body="§IF (== n 46341)\n  §R 0\n" + NAIVE_SUM),
                           False, "SumToN(46341) returned 0; expected 1073767311")]
            for text, expected, detail in cases:
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Contract-writing compatibility exceeded its overall deadline.")
                output = test.verify(profile, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root),
                                          "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)}, detail=detail)
                print(f"{arm} {profile}: {'passed' if expected else 'rejected'}")
                if expected and str(root / "src/Calor.Compiler/bin") not in output:
                    raise RuntimeError("Contract compatibility did not report its selected compiler arm.")
    test.verify("contract001", historical_syntax(source("contract001")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    if ContractWritingVerifierTests.entry_points_executed != 97:
        raise RuntimeError("Expected exactly 97 actual old/current contract-writing controls.")
    print("Compatibility passed: 97 actual old/current contract-writing entry-point controls.")


if __name__ == "__main__":
    unittest.main()
