#!/usr/bin/env python3
"""Focused property-policy regressions; no full math acceptance census."""

import importlib.util
import os
from pathlib import Path
import tempfile
import time
import unittest


HERE = Path(__file__).resolve().parent
specification = importlib.util.spec_from_file_location(
    "math_property_controls", HERE / "test-advanced-math-verifiers.py")
MATH = importlib.util.module_from_spec(specification)
specification.loader.exec_module(MATH)


class MathPropertyPolicyTests(unittest.TestCase):
    def test_original_bool_external_state_control(self):
        control = next(row for row in MATH.matrix()
                       if row[:2] == ("external-state", "advanced002"))
        self.assertEqual(control[2], MATH.source(
            "advanced002", body="return System.Environment.TickCount"))
        output = MATH.MathTests().verify(
            control[1], control[2], control[3], detail=control[4])
        self.assertNotIn("Calor compilation failed", output)

    def test_type_correct_property_read_is_also_forbidden(self):
        output = MATH.MathTests().verify(
            "advanced002",
            MATH.source("advanced002", body="return System.Environment.TickCount == 0"),
            False, detail="Unsupported Math executable property or external state.")
        self.assertNotIn("Calor compilation failed", output)

    def test_ordinary_native_positive_still_compiles_and_executes(self):
        MATH.MathTests().verify("advanced002", MATH.source("advanced002"))

    def test_property_spelling_in_literal_is_not_a_property_read(self):
        MATH.MathTests().verify("advanced002", MATH.source(
            "advanced002",
            body='let string:label = "System.Environment.TickCount"\n'
                 'return value >= min && value <= max'))

    def assert_native_compilation_failure(self, body, diagnostic=None):
        with tempfile.TemporaryDirectory(prefix="calor-math-policy-compile-") as temporary:
            workspace = Path(temporary)
            (workspace / "MathUtils.calr").write_text(
                MATH.source("advanced002", body=body), encoding="utf-8")
            command = [str(HERE / "tasks/github-projects/02_guard_clauses/verify.sh"),
                       str(workspace)]
            with self.assertRaises(RuntimeError) as raised:
                MATH.ENGINE.run(
                    command, HERE, time.monotonic() + 120,
                    env=dict(os.environ, CALOR_COMPILER_ROOT=str(MATH.ROOT),
                             CALOR_LOGIC_TIMEOUT_SECONDS="120"))
            output = MATH.SHARED.classify(raised.exception, commands=(command[0], "dotnet"))
            self.assertIn("Calor compilation failed", output)
            self.assertNotIn("Unsupported Math executable property", output)
            if diagnostic:
                self.assertIn(diagnostic, output)

    def test_plain_return_type_error_remains_a_compilation_failure(self):
        self.assert_native_compilation_failure("return 42", "Calor1002")

    def test_unresolved_property_lookalike_is_not_relabelled(self):
        self.assert_native_compilation_failure(
            "return DoesNotExist.Environment.TickCount == 0")


if __name__ == "__main__":
    unittest.main(verbosity=2)
