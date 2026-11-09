#!/usr/bin/env python3
"""Original type-005/type-006 prerequisites; real entrypoints, no agent runs."""

import importlib.util
import os
from pathlib import Path
import re
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {"ComputeWithTemp": "05_variable_binding", "Accumulate": "06_variable_assignment"}
BODIES = {
    "ComputeWithTemp": "§B{sum:i32} (+ a b)\n§B{difference:i32} (- a b)\n§R (* sum difference)",
    "Accumulate": "§B{~acc:i32} 0\n" + "§ASSIGN acc (+ acc n)\n" * 3 + "§R acc",
}
PARAMETERS = {"ComputeWithTemp": "i32:a, i32:b", "Accumulate": "i32:n"}
CURRENT_ENTRYPOINTS = 59
COMPATIBILITY_ENTRYPOINTS = 71


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def source(name, *, body=None, contracts="", signature=None, extra="", overflow=None):
    header = "Calculator" + (":overflow=" + overflow if overflow else "")
    declaration = signature or f"§F{{{name}:pub}} ({PARAMETERS[name]}) -> i32"
    lines = contracts.splitlines() + (BODIES[name] if body is None else body).splitlines()
    return f"§M{{{header}}}\n  {declaration}\n" + "".join("    " + line + "\n" for line in lines) + extra


def shared_cases(name):
    original = BODIES[name]
    cases = [(source(name), True, None), (source(name).replace("i32", "int"), True, None)]
    if name == "ComputeWithTemp":
        widened = "§B{plus:i64} (/ (+ (* (cast i64 a) 2) (* (cast i64 b) 2)) 2)\n"
        widened += "§B{minus:i64} (+ (cast i64 a) (- 0 b))\n"
        # Negating Int32.MinValue is not representable: widen before negation.
        widened = widened.replace("(- 0 b)", "(- (cast i64 0) b)")
        cases += [
            (source(name, body=widened + "§R (cast i32 (* plus minus))"), True, None),
            (source(name, body=original.replace("sum:i32", "sum:i64").replace("difference:i32", "difference:i64")
                    .replace("§R (* sum difference)", "§R (cast i32 (* sum difference))")), True, None),
            (source(name, overflow="unchecked"), True, None),
            (source(name, body="§B{x:i32} (+ b a)\n§B{y:i32} (+ a (- 0 b))\n"
                    "§B{alias:i32} x\n§R (* alias y)"), True, None),
            (source(name, body="§B{x:i32} (+ b a)\n§B{y:i32} (- a b)\n"
                    "§B{~alias:i32} x\n§R (* alias y)"), True, None),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§B{~answer:i32} 0\n§ASSIGN answer (* sum difference)\n§R answer")), True, None),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§B{~answer:i32} (* sum difference)\n§ASSIGN answer "
                    "(? (&& (== sum (+ a b)) (== difference (- a b))) "
                    "(cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b))) 0)\n§R answer")),
             False, "provenance/coupling"),
            (source(name, body=original, contracts="§Q (|| (< a 0) (>= a 0))\n§S (== result result)"), True, None),
            (source(name, body=original.replace("§R (* sum difference)", "§R (cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b)))")),
             False, "provenance/coupling"),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§R (+ (* sum 0) (cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b))))")),
             False, "provenance/coupling"),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§R (? (&& (== sum (+ a b)) (== difference (- a b))) "
                    "(cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b))) 0)")),
             False, "provenance/coupling"),
            (source(name, body=original.replace("(- a b)", "(+ a b)")), False, "Arithmetic original baseline"),
            (source(name, body="§R (cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b)))"),
             False, "native provenance"),
            (source(name, body="§IF false\n  §B{sum:i32} (+ a b)\n  §B{difference:i32} (- a b)\n"
                    "§R (cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b)))"), False, "provenance"),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§C{Guard} §A sum §A difference §/C\n§R (cast i32 (- (* (cast i64 a) a) (* (cast i64 b) b)))"),
                    extra="  §F{Guard:priv} (i32:s, i32:d) -> void\n    §Q (== s s)\n    §Q (== d d)\n"),
             False, "provenance/coupling"),
            (source(name, body=original.replace("§R (* sum difference)",
                    "§IF (!= (+ a b) 0)\n  §B{guard:i32} (/ 1 sum)\n§R (* sum difference)")),
             False, "counterfactual runtime failed"),
            (source(name, body=original.replace("§R (* sum difference)", "§R (+ (* sum difference) (/ 1 (- a a)))")),
             False, "runtime failed"),
        ]
    else:
        cases += [
            (source(name, body="§B{~total:i32} 0\n§L{i:1:3:1}\n  §ASSIGN total (+ n total)\n§R total"), True, None),
            (source(name, body="§B{increment:i32} n\n§B{~value:i64} 0\n"
                    + "§ASSIGN value (+ value increment)\n" * 3 + "§R (cast i32 value)"), True, None),
            (source(name, overflow="unchecked"), True, None),
            (source(name, contracts="§Q (|| (< n 0) (>= n 0))\n§S (== result result)"), True, None),
            (source(name, body=original.replace("§R acc", "§R (* 3 n)")), False, "provenance/coupling"),
            (source(name, body=original.replace("§R acc", "§R (+ (* acc 0) (* 3 n))")), False, "provenance/coupling"),
            (source(name, body=original.replace("§ASSIGN acc (+ acc n)\n", "", 1).replace("§R acc", "§R (* 3 n)")),
             False, "provenance/coupling"),
            (source(name, body=original.replace("§R acc", "§ASSIGN acc (+ acc n)\n§R (* 3 n)")),
             False, "Arithmetic original baseline"),
            (source(name, body=original.replace("§B{~acc:i32} 0", "§B{~acc:i32} 1").replace("§R acc", "§R (- acc 1)")),
             False, "native provenance"),
            (source(name, body=original.replace("(+ acc n)", "(+ acc 1)").replace("§R acc", "§R (* 3 n)")),
             False, "provenance/coupling"),
            (source(name, body="§B{~acc:i32} 0\n§B{~other:i64} n\n"
                    + "§ASSIGN other (+ other n)\n" * 3 + "§R (cast i32 (- other n))"), False, "provenance/coupling"),
            (source(name, body="§IF false\n  §B{~acc:i32} 0\n  §ASSIGN acc (+ acc n)\n§R (* 3 n)"),
             False, "provenance"),
            (source(name, body="§R (* 3 n)"), False, "native provenance"),
            (source(name, body=original.replace("§R acc", "§IF (== n 1)\n  §B{guard:i32} (/ 1 (- acc 4))\n§R acc")),
             False, "counterfactual runtime failed"),
            (source(name, body=original.replace("§R acc", "§R (+ acc (/ 1 (- n n)))")), False, "runtime failed"),
        ]
    first = "a" if name == "ComputeWithTemp" else "n"
    cases += [
        (source(name, signature=f"§F{{{name}:pub}} ({PARAMETERS[name].replace('i32:' + first, 'i32:' + first + ' | (>= # INT:-2147483648)')}) -> i32"),
         True, None),
        (source(name, signature=f"§F{{{name}:pub}} ({PARAMETERS[name].replace('i32:' + first, 'i32:' + first + ' | (>= # 0)')}) -> i32"),
         False, "writing preconditions"),
        (source(name, contracts=f"§Q (>= {first} 0)"), False, "writing preconditions"),
        (source(name, contracts="§S false"), False, "Writing postconditions"),
        (source(name, contracts="§Q (== result result)"), False, None),
        (source(name, signature=f"§F{{{name}:priv}} ({PARAMETERS[name]}) -> i32"), False, "Wrong public signature"),
        (source(name, body="§R 0", signature=f"§F{{{name}:pub}} ({PARAMETERS[name].replace('i32:', 'i64:', 1)}) -> i32"),
         False, "Wrong public signature"),
        (source(name, signature=f"§F{{{name}:pub}} ({PARAMETERS[name]}) -> i64"), False, "Wrong public signature"),
    ]
    return cases


def independent_cases(name):
    if name == "Accumulate":
        cases = [(n,) for n in (-715827882, -715827881, -1000000, -46341, -31, -3, -2, -1,
                                0, 1, 2, 3, 31, 46341, 1000000, 715827881, 715827882)]
        expected = [3 * n for (n,) in cases]
    else:
        values = (-46340, -1024, -31, -2, -1, 0, 1, 2, 31, 1024, 46340)
        cases = [(a, b) for a in values for b in values] + [
            (1073741824, 1073741823), (1073741823, 1073741824),
            (-1073741824, -1073741823), (-1073741823, -1073741824),
            (1073741824, -1073741823), (-1073741824, 1073741823),
            (-1073741823, 1073741824), (1073741823, -1073741824),
            (46341, 46340), (46340, 46341), (-46341, 46340), (46340, -46341)]
        expected = [(a + b) * (a - b) for a, b in cases]
        assert all(-2**31 <= a + b < 2**31 and -2**31 <= a - b < 2**31 for a, b in cases)
    assert len(cases) == len(set(cases))
    assert all(-2**31 <= value < 2**31 for value in expected)
    return cases, expected


def compatibility_cases(name):
    """Representative strict categories; the full current matrix stays unchanged."""
    selections = {
        "ComputeWithTemp": (
            (1, "int-alias-checked-unrolled"),
            (2, "genuinely-widened-algebra"),
            (3, "long-declaration-narrow-evaluation"),
            (4, "unchecked-narrow"),
            (6, "mutable-binding-alias"),
            (7, "assigned-product-alias"),
            (8, "overwritten-gated-alias"),
            (9, "admissible-entry-output-contracts"),
            (10, "unused-bindings-input-recomputed-return"),
            (11, "zero-weight-decoy"),
            (13, "wrong-binding-value"),
            (15, "unreachable-bindings"),
            (16, "helper-contract-decoy"),
            (17, "mutant-only-runtime-error"),
            (18, "baseline-runtime-error"),
            (19, "admissible-scoped-refinement"),
            (20, "narrowed-scoped-refinement"),
            (21, "narrowed-precondition"),
            (22, "excluded-correct-output"),
            (23, "foreign-contract-reference"),
            (24, "private-signature"),
            (25, "wide-parameter-signature"),
            (26, "wide-return-signature"),
        ),
        "Accumulate": (
            (0, "checked-unrolled-state-updates"),
            (2, "three-iteration-loop"),
            (3, "widened-increment-alias"),
            (6, "unused-accumulator-independent-return"),
            (8, "two-updates"),
            (9, "four-updates"),
            (10, "nonzero-initialization"),
            (11, "constant-increments"),
            (12, "other-variable-updates"),
            (13, "unreachable-accumulator"),
            (15, "mutant-only-runtime-error"),
            (16, "baseline-runtime-error"),
        ),
    }
    full = shared_cases(name)
    expected_full, expected_selected, accepted = (
        (27, 23, 8) if name == "ComputeWithTemp" else (25, 12, 3))
    selected = selections[name]
    if (len(full) != expected_full or len(selected) != expected_selected
            or len({index for index, _ in selected}) != expected_selected
            or len({category for _, category in selected}) != expected_selected
            or sum(full[index][1] for index, _ in selected) != accepted):
        raise RuntimeError(f"Arithmetic representative matrix/count mismatch for {name}.")
    return [(category, *full[index]) for index, category in selected]


def require_finite_process_result(output):
    if (re.search(r"timeout|timed\s*out|deadline|\beof\b|cleanup|"
                  r"cannot terminate verifier process group|"
                  r"\b(?:OSError|FileNotFoundError|PermissionError|BrokenPipeError|ChildProcessError)\b|"
                  r"failed to (?:start|launch)|process infrastructure|"
                  r"segmentation fault|access violation|killed by signal", output, re.IGNORECASE)
            or any(int(code) != 1 for code in re.findall(r"Verifier command exited (-?\d+)", output))):
        raise RuntimeError(f"Arithmetic control infrastructure failure; no finite verdict:\n{output}")


class TypeArithmeticVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    def verify(self, name, text, expected=True, *, env=None, detail=None, overall_deadline=None):
        process = load("arithmetic_process", "lib/verify-logic.py")
        with tempfile.TemporaryDirectory(prefix="calor-type-arithmetic-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            deadline = time.monotonic() + min(120, float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120")))
            if overall_deadline is not None:
                deadline = min(deadline, overall_deadline)
            command = [str(HERE / "tasks/type-system" / TASKS[name] / "verify.sh"), str(workspace)]
            try:
                output = process.run(command, HERE, deadline, env=environment)
                passed = True
            except RuntimeError as error:
                require_finite_process_result(str(error))
                if not re.match(r"Verifier command exited 1:\n", str(error)):
                    raise
                output, passed = str(error), False
            require_finite_process_result(output)
            type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if passed:
                selected = Path(environment["CALOR_COMPILER_ROOT"]).resolve() / "src/Calor.Compiler/bin"
                selected /= environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
                identities = [line for line in output.splitlines() if line.strip().startswith("Selected compiler:")]
                self.assertEqual(identities, [f"Selected compiler: {selected / 'net10.0/calor.dll'}"], output)
                count = len(independent_cases(name)[0])
                coupling = count * (2 if name == "ComputeWithTemp" else 3)
                self.assertIn(f"{count} representable runtime/state cases; {coupling} complete-case binding counterfactuals;", output)
                self.assertIn("not a universal body proof", output)
                self.assertIn("no unknown-proof fallback", output)
                wider = 6 if name == "ComputeWithTemp" and (
                    ":overflow=unchecked}" in text or "§B{plus:i64}" in text) else 0
                self.assertIn(f"{wider} wider-intermediate cases.", output)
            if detail:
                self.assertIn(detail, output)
                if not passed and detail == "writing preconditions":
                    self.assertIn("Concrete representable entry counterexample", output)
            return output

    def test_shared_controls(self):
        for name in TASKS:
            for text, expected, detail in shared_cases(name):
                with self.subTest(name=name, expected=expected, detail=detail):
                    self.verify(name, text, expected, detail=detail)

    def test_fixture_and_selection(self):
        fixture = (HERE / "fixtures/basic-calor-project/Calculator.calr").read_text(encoding="utf-8")
        for name in TASKS:
            self.verify(name, fixture + "\n" + source(name).split("\n", 1)[1])
            self.verify(name, fixture, False, detail="Expected exactly one module function")
        self.verify("ComputeWithTemp", None, False, detail="Input missing")
        self.verify("Accumulate", source("Accumulate"), False,
                    env={"CALOR_COMPILER_CONFIGURATION": "../Debug"}, detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("Accumulate", source("Accumulate"), False,
                    env={"CALOR_COMPILER_ROOT": ""}, detail="CALOR_COMPILER_ROOT")

    def test_independent_oracle_counts(self):
        self.assertEqual(len(independent_cases("ComputeWithTemp")[0]), 133)
        self.assertEqual(len(independent_cases("Accumulate")[0]), 17)
        self.assertEqual(independent_cases("Accumulate")[1][0], -2147483646)
        self.assertEqual(independent_cases("ComputeWithTemp")[1][121], 2147483647)


class TypeArithmeticGuardTests(unittest.TestCase):
    def setUp(self):
        class MockedArithmeticVerifier(TypeArithmeticVerifierTests):
            entry_points_executed = 0
        self.verifier = MockedArithmeticVerifier()
        self.environment = {"CALOR_COMPILER_ROOT": str(ROOT / "mock selected root"),
                            "CALOR_COMPILER_CONFIGURATION": "Release"}
        selected = Path(self.environment["CALOR_COMPILER_ROOT"]).resolve()
        self.identity = f"Selected compiler: {selected / 'src/Calor.Compiler/bin/Release/net10.0/calor.dll'}"
        self.success = (self.identity + "\nVerification passed: Accumulate; native/CLR signature; "
                        "17 representable runtime/state cases; 51 complete-case binding counterfactuals; "
                        "0 wider-intermediate cases.\nLimits: not a universal body proof; no unknown-proof fallback.\n")

    def execute(self, *, output=None, error=None, expected=True):
        run = Mock(return_value=output, side_effect=error)
        try:
            with patch(__name__ + ".load", return_value=SimpleNamespace(run=run)):
                return self.verifier.verify("Accumulate", source("Accumulate"), expected, env=self.environment)
        finally:
            run.assert_called_once()
            command = run.call_args.args[0]
            self.assertEqual(Path(command[0]), HERE / "tasks/type-system/06_variable_assignment/verify.sh")
            self.assertEqual(run.call_args.kwargs["env"]["CALOR_COMPILER_CONFIGURATION"], "Release")

    def test_infrastructure_errors_never_supply_finite_verdicts(self):
        failures = [
            "Verifier timeout after 120s: dotnet",
            "Verifier cleanup timeout: child did not exit by the deadline.",
            "Cannot terminate verifier process group 123: permission denied",
            "Unexpected EOF while capturing process output",
            "OSError: cannot launch dotnet",
            "Verifier command exited -9:\nprocess terminated",
            "Verifier command exited 137:\nprocess terminated",
            "Verifier command exited 1:\nprocess infrastructure failure",
            "Missing/wrong writing preconditions: UNKNOWN; timeout",
        ]
        for failure in failures:
            for wrapped in (failure, "Verifier command exited 1:\nVerification failed: " + failure):
                for expected in (True, False):
                    with self.subTest(failure=wrapped, expected=expected):
                        with self.assertRaises(RuntimeError):
                            self.execute(error=RuntimeError(wrapped), expected=expected)
                        self.assertEqual(self.verifier.entry_points_executed, 0)
            for error in (OSError("cannot launch dotnet"), EOFError("captured pipe closed"),
                                  TimeoutError("capture timed out")):
                        with self.subTest(error=type(error).__name__, expected=expected):
                            with self.assertRaises(type(error)):
                                self.execute(error=error, expected=expected)
                            self.assertEqual(self.verifier.entry_points_executed, 0)
        for expected in (True, False):
            with self.subTest(success_shaped=expected):
                with self.assertRaises(RuntimeError):
                    self.execute(output=self.success + "Verification failed: Verifier timeout after 120s\n",
                                 expected=expected)
                self.assertEqual(self.verifier.entry_points_executed, 0)

    def test_positive_requires_one_exact_selected_identity(self):
        body = self.success.split("\n", 1)[1]
        invalid = [
            body,
            "Selected compiler: /wrong/root/calor.dll\n" + body,
            self.identity + ".other\n" + body,
            self.identity + "\n" + self.identity + "\n" + body,
            self.identity + "\nSelected compiler: /wrong/root/calor.dll\n" + body,
            " " + self.identity + "\n" + body,
        ]
        for output in invalid:
            with self.subTest(output=output):
                with self.assertRaises(AssertionError):
                    self.execute(output=output)
        self.assertEqual(self.execute(output=self.success), self.success)

    def test_finite_candidate_failure_remains_a_negative(self):
        error = RuntimeError("Verifier command exited 1:\nVerification failed: Verifier command exited 1:\n"
                             "Calor compilation failed: foreign contract reference")
        self.assertEqual(self.execute(error=error, expected=False), str(error))
        self.assertEqual(self.verifier.entry_points_executed, 1)


def run_compatibility(old, current, deadline):
    test = TypeArithmeticVerifierTests()
    adapter = load("arithmetic_historical", "test-contract-writing-verifiers.py").historical_syntax
    initial = TypeArithmeticVerifierTests.entry_points_executed
    for label, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for category, text, expected, detail in compatibility_cases(name):
                if time.monotonic() >= deadline:
                    raise RuntimeError("Arithmetic compatibility exceeded its 600-second total deadline.")
                test.verify(name, adapter(text) if historical else text, expected, detail=detail,
                            overall_deadline=deadline, env={"CALOR_COMPILER_ROOT": str(root),
                            "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))})
                print(f"{label} {name} [{category}]: {'accepted' if expected else 'rejected'} actual entrypoint.")
    test.verify("Accumulate", adapter(source("Accumulate")), False, detail="Calor compilation failed",
                overall_deadline=deadline, env={"CALOR_COMPILER_ROOT": str(current),
                "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))})
    observed = TypeArithmeticVerifierTests.entry_points_executed - initial
    expected_count = 2 * sum(len(compatibility_cases(name)) for name in TASKS) + 1
    if observed != expected_count or observed != COMPATIBILITY_ENTRYPOINTS:
        raise RuntimeError(f"Expected exactly {COMPATIBILITY_ENTRYPOINTS} arithmetic old/current controls; observed {observed}.")
    print(f"Compatibility passed: {observed} actual old/current arithmetic entry-point controls.")


if __name__ == "__main__":
    result = unittest.main(exit=False).result
    if not result.wasSuccessful():
        sys.exit(1)
    if len(sys.argv) == 1:
        expected_count = sum(len(shared_cases(name)) for name in TASKS) + 7
        if expected_count != CURRENT_ENTRYPOINTS or TypeArithmeticVerifierTests.entry_points_executed != CURRENT_ENTRYPOINTS:
            raise RuntimeError(f"Expected {CURRENT_ENTRYPOINTS} actual current arithmetic entrypoints; "
                               f"observed {TypeArithmeticVerifierTests.entry_points_executed}.")
        print(f"Passed {expected_count} actual current arithmetic entry-point controls.")
