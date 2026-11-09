#!/usr/bin/env python3
"""Original lam-001..004 disposable selected-native admission controls."""

import importlib.util
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(HERE.parents[2]))).resolve()
TASKS = {
    "lam001": ("01_inline_arrow_lambda", "ApplyDouble"),
    "lam002": ("02_block_lambda", "ApplyComplex"),
    "lam003": ("03_delegate_definition", "MathOperation"),
    "lam004": ("04_event_subscription", "EventPublisher"),
}
EXECUTED = {"accepted": 0, "rejected": 0}


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


SHARED = load("lambda_failure_controls", HERE / "test-carrier-pattern-verifiers.py")
PROCESS = SHARED.PROCESS
REJECTIONS = {
    "missing": ("Input missing:",),
    "invalid-source": ("Calor compilation failed:",),
    "wrong-name": ("Expected exactly one native",),
    "raw-interop": ("Raw interop cannot establish native lambda/delegate/event admission.",),
    "private-function": ("Wrong native public lambda function signature",),
    "wrong-function-type": ("Wrong native public lambda function signature",),
    "wrong-function-parameter": ("Wrong native public lambda function signature",),
    "wrong-output": ("Wrong native lambda output",),
    "no-lambda": ("Required native bound/called supplying lambda missing",),
    "unused-lambda": ("Required native bound/called supplying lambda missing",),
    "wrong-argument": ("Required native bound/called supplying lambda missing",),
    "capture-not-argument": ("Required native bound/called supplying lambda missing",),
    "wrong-lambda-type": ("Calor compilation failed:", "Ambiguous native/bound/emitted lambda symbol"),
    "single-block-statement": ("Required native bound/called supplying lambda missing",),
    "expression-not-block": ("Required native bound/called supplying lambda missing",),
    "false-precondition": ("Precondition failed",),
    "false-postcondition": ("Postcondition failed",),
    "delegate-class-decoy": ("Expected exactly one native MathOperation delegate.",),
    "wrong-delegate-type": ("Wrong native MathOperation delegate signature.",),
    "wrong-delegate-parameter": ("Wrong native MathOperation delegate signature.",),
    "wrong-delegate-arity": ("Wrong native MathOperation delegate signature.",),
    "private-class": ("Expected exactly one native public EventPublisher class.",),
    "private-event": ("Wrong native public EventPublisher event/Subscribe signature.",),
    "private-subscribe": ("Wrong native public EventPublisher event/Subscribe signature.",),
    "wrong-event-type": ("Wrong native/mapped CLR EventHandler event/Subscribe signature:",),
    "wrong-handler-type": ("Wrong native/mapped CLR EventHandler event/Subscribe signature:",),
    "ignored-handler": ("Wrong actual event subscriber state/order/duplicates/replacement",),
    "duplicate-subscription": ("Wrong actual event subscriber state/order/duplicates/replacement",),
    "replacement-not-add": ("Wrong actual event subscriber state/order/duplicates/replacement",),
    "immediate-callback": ("Event Subscribe immediately invoked a handler.",),
    "historical-ids": ("Source IDs are removed",),
}


def source(profile):
    fixture = "basic-calor-project" if profile in ("lam001", "lam002") else "oop-project"
    filename = "Calculator.calr" if profile in ("lam001", "lam002") else "Domain.calr"
    base = (HERE / "fixtures" / fixture / filename).read_text(encoding="utf-8").rstrip() + "\n"
    if profile in ("lam001", "lam002"):
        text = f"  §F{{{TASKS[profile][1]}:pub}} (i32:x) -> i32\n"
        text += "    §B{double:Func<i32,i32>} §LAM{n:i32}"
        if profile == "lam001":
            text += " (* n 2)\n"
        else:
            text += "\n      §IF (> n 0)\n        §R (* n 2)\n      §R 0\n    §/LAM\n"
        return base + text + "    §R §C{double} §A x §/C\n"
    if profile == "lam003":
        return base + "  §DEL{MathOperation}\n    §I{i32:a}\n    §I{i32:b}\n    §O{i32}\n"
    return (base + "  §CL{EventPublisher:pub}\n    §EVT{OnDataReceived:pub:EventHandler}\n"
            "    §MT{Subscribe:pub} (EventHandler:handler) -> void\n"
            "      §E{mut}\n"
            "      §SUB OnDataReceived handler\n")


def historical(text):
    if text is None:
        return None
    lines = []
    lambda_id = None
    delegate_id = None
    for index, line in enumerate(text.splitlines()):
        stripped = line.lstrip()
        if stripped.startswith(("§M{", "§F{", "§CL{", "§MT{", "§DEL{", "§EVT{", "§LAM{")):
            line = line.replace("{", f"{{ld{index}:", 1)
        elif "§LAM{" in line:
            line = line.replace("§LAM{", f"§LAM{{lambda{index}:", 1)
        if "§LAM{" in line:
            lambda_id = line.split("§LAM{", 1)[1].split(":", 1)[0]
        if stripped.startswith("§/LAM"):
            line = line.replace("§/LAM", f"§/LAM{{{lambda_id}}}", 1)
        if stripped.startswith("§DEL{"):
            delegate_id = f"ld{index}"
        if stripped.startswith("§IF "):
            line = line.replace("§IF ", f"§IF{{if{index}}} ", 1)
        lines.append(line)
        if "§DEL{" in text and stripped.startswith("§O{"):
            lines.append(f"  §/DEL{{{delegate_id}}}")
    return "\n".join(lines) + "\n"


def controls(profile):
    text = source(profile)
    name = TASKS[profile][1]
    yield "original-fixture", text, True
    yield "scalar-aliases", text.replace("i32", "int"), True
    if profile in ("lam001", "lam002"):
        yield "qualified-func", text.replace("Func<i32,i32>", "System.Func<i32,i32>"), True
        yield "bound-answer", text.replace("    §R §C{double}", "    §B{answer:i32} §C{double}") + "    §R answer\n", True
        yield "captured-factor", text.replace("    §B{double", "    §B{factor:i32} 2\n    §B{double").replace("(* n 2)", "(* n factor)"), True
        yield "static-lambda", text.replace("§LAM{n:i32}", "§LAM{static:n:i32}"), True
        yield "optional-contracts", text.replace("    §B{double", "    §Q true\n    §S true\n    §B{double"), True
        yield "exact-postcondition", text.replace("    §B{double", "    §S (== result " +
            ("(* x 2)" if profile == "lam001" else "(? (> x 0) (* x 2) 0)") + ")\n    §B{double"), True
        yield "implicit-lambda-parameter", text.replace("§LAM{n:i32}", "§LAM{n}"), True
        if profile == "lam002":
            yield "conditional-block-binding", text.replace(
                "      §IF (> n 0)\n        §R (* n 2)\n      §R 0",
                "      §B{answer:i32} (? (> n 0) (* n 2) 0)\n      §R answer"), True
        yield "private-function", text.replace(f"{name}:pub", f"{name}:priv"), False
        yield "wrong-function-type", text.replace("(i32:x)", "(i64:x)").replace("§A x", "§A (cast i32 x)"), False
        yield "wrong-function-parameter", text.replace("(i32:x)", "(i32:y)").replace("§A x", "§A y"), False
        yield "wrong-output", text.replace("(* n 2)", "(* n 1)"), False
        prefix = text[:text.index(f"  §F{{{name}")]
        yield "no-lambda", prefix + f"  §F{{{name}:pub}} (i32:x) -> i32\n    §R " + ("(* x 2)\n" if profile == "lam001" else "(? (> x 0) (* x 2) 0)\n"), False
        yield "unused-lambda", text.replace("§R §C{double} §A x §/C", "§R " + ("(* x 2)" if profile == "lam001" else "(? (> x 0) (* x 2) 0)")), False
        yield "wrong-argument", text.replace("§A x", "§A 0").replace("    §R §C{double}", "    §B{ignored:i32} §C{double}") + ("    §R (* x 2)\n" if profile == "lam001" else "    §R (? (> x 0) (* x 2) 0)\n"), False
        yield "capture-not-argument", text.replace("(* n 2)", "(* x 2)").replace("(> n 0)", "(> x 0)"), False
        yield "wrong-lambda-type", text.replace("Func<i32,i32>", "Func<i64,i32>").replace("n:i32", "n:i64").replace("(* n 2)", "(cast i32 (* n 2))"), False
        yield "false-precondition", text.replace("    §B{double", "    §Q false\n    §B{double"), False
        yield "false-postcondition", text.replace("    §B{double", "    §S false\n    §B{double"), False
        if profile == "lam002":
            yield "single-block-statement", text.replace("      §IF (> n 0)\n        §R (* n 2)\n      §R 0", "      §R (? (> n 0) (* n 2) 0)"), False
            yield "expression-not-block", text.replace("§LAM{n:i32}\n      §IF (> n 0)\n        §R (* n 2)\n      §R 0\n    §/LAM", "§LAM{n:i32} (? (> n 0) (* n 2) 0)"), False
    elif profile == "lam003":
        yield "wrong-delegate-type", text.replace("§I{i32:a}", "§I{i64:a}"), False
        yield "wrong-delegate-parameter", text.replace("§I{i32:a}", "§I{i32:x}"), False
        yield "wrong-delegate-arity", text.replace("    §I{i32:b}\n", ""), False
        yield "delegate-class-decoy", text[:text.index("  §DEL")] + "  §CL{MathOperation:pub}\n", False
    else:
        yield "qualified-eventhandler", text.replace("EventHandler", "System.EventHandler"), True
        yield "nullable-eventhandler", text.replace(":EventHandler}", ":?EventHandler}").replace("(EventHandler:handler)", "(?EventHandler:handler)"), True
        yield "different-parameter-name", text.replace("handler", "listener"), True
        yield "extra-return-value", text.replace("-> void", "-> i32") + "      §R 0\n", True
        yield "private-class", text.replace("EventPublisher:pub", "EventPublisher:int"), False
        yield "private-event", text.replace("OnDataReceived:pub", "OnDataReceived:priv"), False
        yield "private-subscribe", text.replace("Subscribe:pub", "Subscribe:priv"), False
        yield "wrong-event-type", text.replace("EventHandler", "EventHandler<EventArgs>"), False
        yield "wrong-handler-type", text.replace("(EventHandler:handler)", "(EventHandler<EventArgs>:handler)").replace("      §SUB OnDataReceived handler", ""), False
        yield "ignored-handler", text.replace("      §SUB OnDataReceived handler", ""), False
        yield "duplicate-subscription", text + "      §SUB OnDataReceived handler\n", False
        yield "replacement-not-add", text.replace("§SUB OnDataReceived handler", "§ASSIGN OnDataReceived handler"), False
        yield "immediate-callback", text + "      §C{handler.Invoke} §A this §A EventArgs.Empty §/C\n", False
    yield "missing", None, False
    yield "invalid-source", "invalid", False
    yield "wrong-name", text.replace(name, "WrongTarget"), False
    yield "raw-interop", text + "  §CSHARP{public class Decoy { }}§/CSHARP\n", False


def expected_lines(profile, text, root, configuration):
    identity = f"Selected compiler: {Path(root).resolve() / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
    if profile in ("lam001", "lam002"):
        count = 13 if profile == "lam001" else 14
        captures = count * 3 if "factor" in text else 0
        verdict = (f"Verification passed: {TASKS[profile][1]}; {count} independent representable signed/boundary cases; "
                   f"{count * (count + 1)} successful complete-case argument/output interventions; "
                   f"{captures} capture interventions; native/CLR mapping.")
    elif profile == "lam003":
        verdict = "Verification passed: MathOperation; 432 independent CLR delegate binding/invocation cases; native/CLR mapping."
    else:
        verdict = ("Verification passed: EventPublisher; 16 actual event state/invocation cases; "
                   "32 successful complete-case handler/neutralization interventions; native/CLR mapping.")
    return [identity, verdict]


def execute(profile, text, expected, root=ROOT, deadline=None, environment=None, category=None):
    with tempfile.TemporaryDirectory(prefix="calor-lambda-control-") as temporary:
        workspace = Path(temporary)
        fixture = "basic-calor-project" if profile in ("lam001", "lam002") else "oop-project"
        shutil.copytree(HERE / "fixtures" / fixture, workspace, dirs_exist_ok=True)
        filename = "Calculator.calr" if profile in ("lam001", "lam002") else "Domain.calr"
        if text is None:
            (workspace / filename).unlink()
        else:
            (workspace / filename).write_text(text, encoding="utf-8")
        env = dict(os.environ, CALOR_COMPILER_ROOT=str(root))
        env.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
        env.update(environment or {})
        deadline = min(time.monotonic() + 120, deadline or float("inf"))
        env["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(120, deadline - time.monotonic()))
        try:
            output = PROCESS.run(
                ["bash", str(HERE / "tasks/lambdas-delegates" / TASKS[profile][0] / "verify.sh"), str(workspace)],
                HERE, deadline, env)
        except RuntimeError as error:
            if expected:
                raise
            output = SHARED.control_failure(error)
            if re.search(r"\bfatal\b|\babnormal\b|\bsolver\b|process (?:error|failure|died)|success-shaped", output, re.IGNORECASE):
                raise
            if category not in REJECTIONS or not any(marker in output for marker in REJECTIONS[category]):
                raise RuntimeError(f"Unknown/unmodeled {profile}/{category} rejection:\n{output}") from error
            EXECUTED["rejected"] += 1
            return output
        if not expected:
            raise AssertionError(f"{profile}/{category}: unexpected acceptance:\n{output}")
        SHARED.assert_selected_compiler(output, env)
        if output.splitlines() != expected_lines(profile, text, root, env.get("CALOR_COMPILER_CONFIGURATION", "Debug")):
            raise AssertionError(f"{profile}/{category}: incomplete/contaminated native identity/case evidence:\n{output}")
        EXECUTED["accepted"] += 1
        return output


def run_compatibility(old, current, deadline):
    before = dict(EXECUTED)
    representatives = [(profile, "original-fixture", source(profile), True) for profile in TASKS]
    # Compound positive sources exercise interacting forms in one real launch;
    # the current matrix still isolates every form separately.
    combined = source("lam001").replace("i32", "int").replace("Func<int,int>", "System.Func<int,int>")
    combined = combined.replace("    §B{double", "    §Q true\n    §S true\n    §S (== result (* x 2))\n    §B{factor:int} 2\n    §B{double")
    combined = combined.replace("(* n 2)", "(* n factor)")
    combined = combined.replace("    §R §C{double}", "    §B{answer:int} §C{double}") + "    §R answer\n"
    representatives.append(("lam001", "combined-capture-alias-result-contracts", combined, True))
    representatives.append(("lam002", "combined-static-implicit",
                            source("lam002").replace("§LAM{n:i32}", "§LAM{static:n}"), True))
    representatives.append(("lam002", "conditional-block-binding", next(
        text for category, text, expected in controls("lam002")
        if category == "conditional-block-binding" and expected), True))
    combined_event = source("lam004").replace("EventHandler", "System.EventHandler")
    combined_event = combined_event.replace(":System.EventHandler}", ":?System.EventHandler}")
    combined_event = combined_event.replace("(System.EventHandler:handler)", "(?System.EventHandler:listener)")
    combined_event = combined_event.replace("OnDataReceived handler", "OnDataReceived listener")
    combined_event = combined_event.replace("-> void", "-> i32") + "      §R 0\n"
    representatives.append(("lam004", "combined-nullable-qualified-renamed-return", combined_event, True))
    coverage = {
        "original-fixture": {"original-fixture"},
        "combined-capture-alias-result-contracts": {"scalar-aliases", "qualified-func", "bound-answer",
            "captured-factor", "optional-contracts", "exact-postcondition"},
        "combined-static-implicit": {"static-lambda", "implicit-lambda-parameter"},
        "conditional-block-binding": {"conditional-block-binding"},
        "combined-nullable-qualified-renamed-return": {"qualified-eventhandler", "nullable-eventhandler",
            "different-parameter-name", "extra-return-value"},
    }
    covered = set().union(*(coverage[category] for _, category, _, _ in representatives))
    positive_forms = {category for profile in TASKS for category, _, expected in controls(profile) if expected}
    if covered != positive_forms or len(representatives) != 8:
        raise AssertionError("Incomplete representative native positive forms.")
    represented = set()
    for profile in TASKS:
        for category, text, expected in controls(profile):
            if not expected and category not in represented:
                representatives.append((profile, category, text, expected))
            if not expected:
                represented.add(category)
    if represented != set(REJECTIONS) - {"historical-ids"}:
        raise AssertionError("Incomplete representative native lambda/delegate/event categories.")
    for label, root in (("v0.29", old), ("current", current)):
        arm_before = dict(EXECUTED)
        for profile, category, text, expected in representatives:
            try:
                execute(profile, historical(text) if label == "v0.29" else text,
                        expected, root, deadline, category=category)
            except Exception as error:
                raise RuntimeError(f"{label}/{profile}/{category}: {error}\n"
                    f"Completed actual counters before refusal: {EXECUTED}.") from error
        print(f"{label}: all original lambda/delegate/event positive/adversarial categories executed "
              f"({EXECUTED['accepted'] - arm_before['accepted']} accepted, "
              f"{EXECUTED['rejected'] - arm_before['rejected']} rejected).", flush=True)
    execute("lam003", historical(source("lam003")), False, current, deadline, category="historical-ids")
    accepts = 2 * sum(expected for _, _, _, expected in representatives)
    rejects = 2 * sum(not expected for _, _, _, expected in representatives) + 1
    if EXECUTED != {"accepted": before["accepted"] + accepts, "rejected": before["rejected"] + rejects}:
        raise AssertionError("Incomplete actual old/current lambda matrix.")
    print(f"Compatibility passed: {accepts + rejects} actual old/current lambda/delegate/event entrypoints "
          f"({accepts} accepted, {rejects} rejected).")


class LambdaVerifierTests(unittest.TestCase):
    def test_current_native_qualified_scalar_blocker(self):
        text = source("lam001")
        boundary = text.index("  §F{ApplyDouble:")
        text = text[:boundary] + text[boundary:].replace("i32", "System.Int32")
        before = dict(EXECUTED)
        with self.assertRaises(RuntimeError) as refusal:
            execute("lam001", text, True)
        output = SHARED.failure_structure(refusal.exception)
        self.assertIn("Calor compilation failed:", output)
        self.assertIn("Calor0202 Arithmetic operators require numeric operands, got System.Int32 and i32", output)
        self.assertEqual(EXECUTED, before)
        print("Separate actual Program.Compile limitation: qualified System.Int32 lambda arithmetic rejects with Calor0202; not finite negative credit.")

    def test_preserved_profiles(self):
        preserved = load("lambda_preserved_effects", HERE / "test-effects-verifiers.py")
        preserved.EffectsVerifierTests().test_preserved_profiles()
        arithmetic = load("lambda_preserved_arithmetic", HERE / "test-type-arithmetic-verifiers.py")
        arithmetic.TypeArithmeticVerifierTests().verify("ComputeWithTemp", arithmetic.source("ComputeWithTemp"))
        SHARED.execute("pm004", SHARED.source("pm004"), True)
        print("Preserved 34 existing profiles, 1 native prerequisite, and 2 arithmetic/carrier entrypoints; separate counters.")

    def test_current_controls(self):
        before = dict(EXECUTED)
        entries = [(profile, category, text, expected) for profile in TASKS
                   for category, text, expected in controls(profile)]
        for profile, category, text, expected in entries:
            with self.subTest(profile=profile, category=category):
                execute(profile, text, expected, category=category)
        self.assertEqual(EXECUTED, {"accepted": before["accepted"] + sum(entry[3] for entry in entries),
                                    "rejected": before["rejected"] + sum(not entry[3] for entry in entries)})
        print(f"Current actual lambda controls: {EXECUTED}.")

    def test_synthetic_failure_and_identity_accounting(self):
        before = dict(EXECUTED)
        for message in ("UNKNOWN", "solver unknown", "OSError: OS error", "EOFError: inherited pipe",
                        "Verifier cleanup timeout", "Verifier timeout after 120s: dotnet", "launch failure",
                        "Build FAILED", "System.DllNotFoundException: native", "success-shaped failure"):
            with self.subTest(message=message):
                with patch.object(PROCESS, "run", side_effect=RuntimeError(
                    "Verifier command exited 1:\nVerification failed: " + message)):
                    with self.assertRaises(RuntimeError):
                        execute("lam001", source("lam001"), False, category="wrong-output")
        for nested in ("Verifier command exited 2:", "Verifier command exited -9:", "Verifier command exited 0:",
                       "Verification command exited 1:", "Verifier command exited 1: suffix"):
            with patch.object(PROCESS, "run", side_effect=RuntimeError(
                "Verifier command exited 1:\nVerification failed: " + nested + "\nWrong native lambda output")):
                with self.assertRaises(RuntimeError):
                    execute("lam001", source("lam001"), False, category="wrong-output")
        good = expected_lines("lam001", source("lam001"), ROOT, os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug"))
        for identity in (" " + good[0], good[0].lower(), good[0] + " suffix",
                         good[0].replace("net10.0", "net9.0"), good[0].replace("calor.dll", "alternate.dll"),
                         good[0] + "\n" + good[0], good[0] + "\nUNKNOWN"):
            with patch.object(PROCESS, "run", return_value=identity + "\n" + good[1] + "\n"):
                with self.assertRaises(AssertionError):
                    execute("lam001", source("lam001"), True)
        self.assertEqual(EXECUTED, before)

    def test_actual_prerequisites_and_timeout(self):
        with tempfile.TemporaryDirectory(prefix="calor-lambda-prerequisite-") as temporary:
            arm = Path(temporary).resolve()
            (arm / "Calculator.calr").write_text(source("lam001"), encoding="utf-8")
            command = ["bash", str(HERE / "tasks/lambdas-delegates/01_inline_arrow_lambda/verify.sh"), str(arm)]

            def reject(changes, refusal=None, timeout=False):
                environment = dict(os.environ, CALOR_COMPILER_ROOT=str(ROOT))
                environment.update(changes)
                SHARED.reject_prerequisite(command, environment, refusal, explicit_timeout=timeout)

            reject({"CALOR_COMPILER_ROOT": ""},
                   "Verification failed: CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path.")
            reject({"CALOR_COMPILER_CONFIGURATION": "../Release"},
                   "Verification failed: CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root.")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            reject({"CALOR_COMPILER_ROOT": str(arm)},
                   f"Verification failed: Selected compiler dependency missing: {arm / relative / 'calor.dll'}; build the selected root first.")
            shutil.copytree(ROOT / relative, arm / relative)
            (arm / ".github").mkdir()
            shutil.copyfile(ROOT / ".github/z3-binaries-4.15.7.sha256", arm / ".github/z3-binaries-4.15.7.sha256")
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            for filename in (native, "Microsoft.Z3.dll"):
                image = arm / relative / filename
                original = image.read_bytes()
                image.write_bytes(b"corrupt native prerequisite")
                reject({"CALOR_COMPILER_ROOT": str(arm)},
                       f"Verification failed: Selected native manifest mismatch: {image}")
                image.write_bytes(original)
            reject({"CALOR_LOGIC_TIMEOUT_SECONDS": "0.25"}, timeout=True)
        print("Executed 5 actual selected/native prerequisite refusals and 1 explicit timeout, separate from finite controls.")


if __name__ == "__main__":
    unittest.main(verbosity=2)
