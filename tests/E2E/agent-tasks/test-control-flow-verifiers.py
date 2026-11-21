#!/usr/bin/env python3
"""Real cf-001..cf-004 entry-point controls; no agents or active corpus fixtures."""

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
TASK_NAMES = {
    "PrintNumbers": "01_for_loop", "Countdown": "02_while_loop",
    "DoWhileDemo": "03_do_while_loop", "Classify": "04_if_elseif_else",
}
SIGNATURES = {
    name: f"§F{{{name}:pub}} " + ("(i32:x) -> i32" if name == "Classify" else "() -> void")
    for name in TASK_NAMES
}
BODIES = {
    "PrintNumbers": "§L{index:1:5:1}\n  §P index",
    "Countdown": "§B{~counter:i32} 5\n§WH (> counter 0)\n  §P counter\n  §ASSIGN counter (- counter 1)",
    "DoWhileDemo": "§B{~counter:i32} 0\n§DO\n  §P counter\n  §ASSIGN counter (+ counter 1)\n(< counter 3)",
    "Classify": "§IF (< x 0)\n  §R -1\n§EI (== x 0)\n  §R 0\n§EL\n  §R 1",
}
EXPECTED_STDOUT = {
    "PrintNumbers": range(1, 6), "Countdown": range(5, 0, -1), "DoWhileDemo": range(3),
}
COUPLING_CASES = {
    name: [
        ("dormant", "§IF false\n" + "\n".join("  " + line for line in BODIES[name].splitlines())
         + "\n" + ("§R (? (< x 0) -1 (? (== x 0) 0 1))" if name == "Classify"
                    else "\n".join(f"§P {value}" for value in EXPECTED_STDOUT[name])))
    ] for name in TASK_NAMES
}
for name in EXPECTED_STDOUT:
    COUPLING_CASES[name].append((
        "executed nonprinting",
        BODIES[name].replace("§P index", "§B{discard:i32} index")
        .replace("§P counter", "§B{discard:i32} counter")
        + "\n" + "\n".join(f"§P {value}" for value in EXPECTED_STDOUT[name]),
    ))
COUPLING_CASES["Classify"].append((
    "executed unused",
    "§IF (< x 0)\n  §B{unused:i32} 0\n§EI (== x 0)\n  §B{unused:i32} 0\n§EL\n  §B{unused:i32} 0\n"
    "§R (? (< x 0) -1 (? (== x 0) 0 1))",
))
ASSIGNING_CLASSIFY = (
    "§B{~classification:int} 0\n"
    "§IF (< x 0)\n  §ASSIGN classification -1\n§EI (== x 0)\n  §ASSIGN classification 0\n"
    "§EL\n  §ASSIGN classification 1\n§R classification"
)


def source(name, *, body=None, signature=None, effect="cw", nested=False):
    module = "Calculator" if name == "Classify" else "Effects"
    lines = ([] if name == "Classify" or effect is None else [f"§E{{{effect}}}"])
    statements = (BODIES[name] if body is None else body).splitlines()
    if nested:
        statements = ["§IF true"] + ["  " + line for line in statements]
        if name == "Classify":
            statements += ["§R 99"]
    lines += statements
    return f"§M{{{module}}}\n  " + (signature or SIGNATURES[name]) + "\n" + "".join(
        "    " + line + "\n" for line in lines
    )


def historical_syntax(text):
    """Only temporary old-parser inputs receive IDs and old do-while closers."""
    lines = []
    serial = 0
    do_blocks = []
    for line in text.splitlines():
        stripped = line.lstrip()
        indent = len(line) - len(stripped)
        if do_blocks and indent == do_blocks[-1][0] and stripped.startswith("("):
            _, identity = do_blocks.pop()
            line = " " * indent + f"§/DO{{{identity}}} " + stripped
        elif stripped.startswith(("§M{", "§F{", "§L{")):
            serial += 1
            prefix = {"§M": "m", "§F": "f", "§L": "l"}[stripped[:2]]
            line = line.replace("{", f"{{{prefix}{serial:03d}:", 1)
        elif stripped.startswith(("§WH ", "§IF ", "§DO")):
            serial += 1
            marker = stripped.split()[0]
            identity = f"b{serial:03d}"
            line = " " * indent + stripped.replace(marker, f"{marker}{{{identity}}}", 1)
            if marker == "§DO":
                do_blocks.append((indent, identity))
        lines.append(line)
    if do_blocks:
        raise RuntimeError("Unclosed do-while compatibility input.")
    return "\n".join(lines) + "\n"


class ControlFlowVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real control-flow verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-control-flow-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                filename = "Calculator.calr" if name == "Classify" else "Effects.calr"
                (workspace / filename).write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/control-flow" / TASK_NAMES[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                timeout=135,
            )
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name}; native control flow", result.stdout)
                self.assertIn("11 runtime cases." if name == "Classify" else "exact stdout.", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_native_implementations_aliases_and_nesting(self):
        for name in TASK_NAMES:
            for nested in (False, True):
                with self.subTest(name=name, nested=nested):
                    self.verify(name, source(name, nested=nested))
                    text = source(name, nested=nested).replace("i32", "int").replace("counter", "remaining")
                    text = text.replace("§P remaining", "§C{Console.WriteLine} §A remaining §/C")
                    self.verify(name, "// harmless formatting and names\n" + text.replace("    ", "      "))
        self.verify("Classify", source("Classify", signature="§F{Classify:pub} (int:x) -> i32"))
        self.verify("Classify", source("Classify", body="§IF (> x 0)\n  §R 1\n§EI (< x 0)\n  §R -1\n§EL\n  §R 0"))
        self.verify("PrintNumbers", source("PrintNumbers", effect="cw,cr"))
        self.verify("Classify", source("Classify", body=ASSIGNING_CLASSIFY))

    def test_construct_witnesses_must_produce_behavior(self):
        for name, cases in COUPLING_CASES.items():
            for label, body in cases:
                with self.subTest(name=name, label=label):
                    self.verify(name, source(name, body=body), False, detail="Control-flow coupling failed")

    def test_missing_private_comment_only_and_stub_functions(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                self.verify(name, source(name).replace(f"§F{{{name}:", "§F{Unrelated:"), False,
                            detail="Expected exactly one")
                self.verify(name, "// " + " ".join(source(name).splitlines()) + "\n§M{Empty}\n", False)
                self.verify(name, source(name, signature=SIGNATURES[name].replace(":pub", ":priv")),
                            False, detail="Wrong public signature")
                self.verify(name, source(name, body="§R 1" if name == "Classify" else ""), False)

    def test_wrong_scalar_signatures(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                if name == "Classify":
                    for signature in ("§F{Classify:pub} (i64:x) -> i32",
                                      "§F{Classify:pub} (i32:y) -> i32",
                                      "§F{Classify:pub} (i32:x) -> i64"):
                        self.verify(name, source(name, signature=signature).replace(" x ", " y ")
                                    if ":y" in signature else source(name, signature=signature), False)
                else:
                    self.verify(name, source(name, signature=f"§F{{{name}:pub}} (i32:unused) -> void"), False,
                                detail="Wrong public signature")
                    self.verify(name, source(name, signature=f"§F{{{name}:pub}} () -> i32",
                                             body=BODIES[name] + "\n§R 0"), False,
                                detail="Wrong public signature")

    def test_constructs_and_effects_must_belong_to_requested_function(self):
        for name in TASK_NAMES:
            with self.subTest(name=name):
                wrong = "§R (? (< x 0) -1 (? (== x 0) 0 1))" if name == "Classify" else ""
                unrelated = source(name).splitlines()[1:]
                unrelated[0] = unrelated[0].replace(f"§F{{{name}:", "§F{Unrelated:")
                self.verify(name, source(name, body=wrong, effect=None) + "\n".join(unrelated) + "\n",
                            False, detail="Required native control flow")
                self.verify(name, source(name, body="// " + " ".join(BODIES[name].splitlines())
                                         + "\n" + wrong), False)
                if name != "Classify":
                    self.verify(name, source(name, effect=None) + "\n".join(unrelated) + "\n", False)
                    self.verify(name, source(name, effect="cr"), False)
        self.verify("PrintNumbers", source("PrintNumbers", body=BODIES["Countdown"]), False,
                    detail="Required native control flow")
        self.verify("Countdown", source("Countdown", body=BODIES["DoWhileDemo"]), False,
                    detail="Required native control flow")
        self.verify("DoWhileDemo", source("DoWhileDemo", body=BODIES["Countdown"]), False,
                    detail="Required native control flow")
        self.verify("Classify", source("Classify", body="§IF (< x 0)\n  §R -1\n§EL\n  §R (? (== x 0) 0 1)"),
                    False, detail="Required native control flow")
        self.verify("Classify", source("Classify", body="§IF (< x 0)\n  §R -1\n§EI (== x 0)\n  §R 0\n§R 1"),
                    False, detail="Required native control flow")

    def test_exact_stdout_order_extra_missing_and_off_by_one(self):
        variants = {
            "PrintNumbers": [
                BODIES["PrintNumbers"].replace(":1:5:", ":0:5:"),
                BODIES["PrintNumbers"].replace(":1:5:", ":1:4:"),
                BODIES["PrintNumbers"].replace(":1:5:1", ":5:1:-1"),
                BODIES["PrintNumbers"].replace("§P index", "§P 1"),
            ],
            "Countdown": [
                BODIES["Countdown"].replace(" 5\n", " 4\n"),
                BODIES["Countdown"].replace("(> counter 0)", "(>= counter 0)"),
                BODIES["Countdown"].replace("§P counter", "§P (- 6 counter)"),
            ],
            "DoWhileDemo": [
                BODIES["DoWhileDemo"].replace(" 0\n", " 1\n"),
                BODIES["DoWhileDemo"].replace("(< counter 3)", "(<= counter 3)"),
                BODIES["DoWhileDemo"].replace("(< counter 3)", "false"),
            ],
        }
        for name, bodies in variants.items():
            bodies += [BODIES[name] + '\n§P "extra"', BODIES[name].replace("§P", "// §P")]
            for body in bodies:
                with self.subTest(name=name, body=body):
                    self.verify(name, source(name, body=body), False, detail="Wrong stdout")

    def test_signed_classification_boundaries(self):
        for value in (-2147483648, -2147483647, -100, -2, -1, 0, 1, 2, 100, 2147483646, 2147483647):
            with self.subTest(value=value):
                body = f"§IF (== x INT:{value})\n  §R 42\n" + BODIES["Classify"]
                self.verify("Classify", source("Classify", body=body), False, detail="expected")

    def test_input_generated_compilation_runtime_and_timeouts(self):
        for name in TASK_NAMES:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "invalid source", False, detail="Calor compilation failed")
        self.verify("PrintNumbers", source("PrintNumbers", body=BODIES["PrintNumbers"] + "\n§R 1"), False)
        self.verify("PrintNumbers", source("PrintNumbers", body=BODIES["PrintNumbers"]
                                          + "\n§C{Console.WriteLine} §A 1 §A 2 §/C"),
                    False, detail="Generated C# failed compilation")
        self.verify("DoWhileDemo", source("DoWhileDemo", effect="cw,throw",
                                        body=BODIES["DoWhileDemo"] + '\n§TH STR:"deliberate runtime failure"'),
                    False, detail="deliberate runtime failure")
        for name in TASK_NAMES:
            loop = BODIES[name] + "\n§WH true\n  §CN" if name != "Classify" else (
                "§WH true\n  §CN\n" + BODIES[name])
            self.verify(name, source(name, body=loop), False,
                        env={"CALOR_LOGIC_TIMEOUT_SECONDS": "8"}, detail="timeout")
        self.verify("PrintNumbers", source("PrintNumbers", body="§L{index:1:5:1}\n  §WH true\n    §P index"),
                    False, detail="Excessive stdout")
        self.verify("Classify", source("Classify"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")

    def test_selected_root_configuration_and_no_fallback(self):
        with tempfile.TemporaryDirectory(prefix="calor-cf-selected-arm-") as temporary:
            arm = Path(temporary)
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            self.verify("PrintNumbers", source("PrintNumbers"), False,
                        env={"CALOR_COMPILER_ROOT": str(arm)}, detail="Selected compiler dependency missing")
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            self.verify("DoWhileDemo", source("DoWhileDemo"), env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail=str(arm / relative / "calor.dll"))
            self.verify("Classify", source("Classify"), False, env={
                "CALOR_COMPILER_ROOT": str(arm), "CALOR_COMPILER_CONFIGURATION": "MissingConfiguration",
            }, detail="Selected compiler dependency missing")
            (arm / relative / "calor.dll").write_bytes(b"not a compiler")
            self.verify("Countdown", source("Countdown"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Verifier command exited")


def run_compatibility(old, current, deadline):
    test = ControlFlowVerifierTests()
    before = ControlFlowVerifierTests.entry_points_executed

    def execute(root, historical, name, text, expected=True, detail=None):
        remaining = min(120, deadline - time.monotonic())
        if remaining <= 0:
            raise RuntimeError("Control-flow compatibility exceeded its overall deadline.")
        output = test.verify(name, historical_syntax(text) if historical else text, expected, env={
            "CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining),
        }, detail=detail)
        print(f"{'v0.29' if historical else 'current'} {name}: {'accepted' if expected else 'rejected'}")
        return output

    for root, historical in ((old, True), (current, False)):
        for name in TASK_NAMES:
            execute(root, historical, name, source(name))
            execute(root, historical, name, source(name, nested=True).replace("i32", "int"))
            execute(root, historical, name, source(name, signature=SIGNATURES[name].replace(":pub", ":priv")),
                    False, "Wrong public signature")
            wrong = "§R 1" if name == "Classify" else ""
            execute(root, historical, name, source(name, body=wrong), False, "Required native control flow")
            body = BODIES[name].replace("§R -1", "§R 1") if name == "Classify" else BODIES[name] + '\n§P "extra"'
            execute(root, historical, name, source(name, body=body), False,
                    "expected" if name == "Classify" else "Wrong stdout")
            if name != "Classify":
                execute(root, historical, name, source(name, effect=None), False)
            else:
                execute(root, historical, name, source(name, body="§IF (== x INT:-2147483648)\n  §R 0\n"
                                                      + BODIES[name]), False, "expected")
        for name, cases in COUPLING_CASES.items():
            for label, body in cases:
                execute(root, historical, name, source(name, body=body), False, "Control-flow coupling failed")
                print(f"Coupling control: {label} {name} rejected.")
        execute(root, historical, "Classify", source("Classify", body=ASSIGNING_CLASSIFY))
    test.verify("Classify", historical_syntax(source("Classify")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    count = ControlFlowVerifierTests.entry_points_executed - before
    if count != 67:
        raise RuntimeError(f"Expected 67 actual old/current control-flow controls, executed {count}.")
    print(f"Compatibility passed: {count} actual old/current control-flow entry-point controls.")


if __name__ == "__main__":
    unittest.main()
