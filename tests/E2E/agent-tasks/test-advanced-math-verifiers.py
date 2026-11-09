#!/usr/bin/env python3
"""Actual native/CLR controls for the four unchanged MathUtils task prompts."""

import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tarfile
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor
import unittest
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OLD = "d82f3ae95175ecd09581c6922701706be72b0ca9"


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


SHARED = load("math_shared_admission", "test-advanced-contract-verifiers.py")
ENGINE = SHARED.ENGINE
FINITE = re.compile(
    r"^(?:Input missing:|System\.InvalidOperationException: (?:"
    r"Calor compilation failed|Expected exactly one native Math|Wrong (?:public signature for Math|native MathUtils|Math independent)|"
    r"Raw interop cannot establish native advanced MathUtils|Unsupported Math executable|"
    r"Math (?:missing/wrong entry|undefined entry|missing/weakened postconditions|output predicates exclude|"
    r"undefined postcondition|native contract translation unsupported|native arithmetic translation unsupported|"
    r"foreign/scoped/unsafe contract operand|successful supplying-path/output/contract coupling failed|"
    r"nested inner branches lack|counterfactual runtime failed|nested-condition counterfactual runtime failed|"
    r"baseline native postcondition rejected)))",
    re.DOTALL,
)
FIXTURE = (HERE / "fixtures/advanced-calor-project/MathUtils.calr").read_bytes().decode("utf-8")
TASKS = {
    "advanced001": ("01_fluent_results", "Sign", "i32:x", "i32",
                    "§S (|| (== result -1) (|| (== result 0) (== result 1)))",
                    "§R (? (< x 0) -1 (? (== x 0) 0 1))", (13, 0, 3)),
    "advanced002": ("02_guard_clauses", "InRange", "i32:value, i32:min, i32:max", "bool",
                    "§Q (<= min max)\n§S (== result (&& (>= value min) (<= value max)))",
                    "§R (&& (>= value min) (<= value max))", (1183, 2, 5)),
    "advanced003": ("03_strongly_typed_id", "Min", "i32:a, i32:b", "i32",
                    "§S (<= result a)\n§S (<= result b)\n§S (|| (== result a) (== result b))",
                    "§R (? (<= a b) a b)", (169, 0, 3)),
    "advanced004": ("04_humanizer", "SafeDiv+SafeMod", "i32:a, i32:b", "i32",
                    "§Q (!= b 0)\n§S (== result (/ a b))",
                    "§R (/ a b)", (310, 6, 20)),
}
ALTERNATIVES = {
    "advanced001": "§B{sign:int} (? (> x 0) 1 (? (< x 0) -1 0))\n§R sign",
    "advanced002": "§IF (< value min)\n  §R false\n§R (! (> value max))",
    "advanced003": "§B{left:int} a\n§B{right:int} b\n§IF (<= left right)\n  §R left\n§R right",
    "advanced004": "§B{quotient:int} (/ a b)\n§R quotient",
}
EQUIVALENT = {
    "advanced001": "§S (&& (>= result -1) (<= result 1))",
    "advanced002": "§Q (! (> min max))\n§S (&& (-> result (&& (>= value min) (<= value max)))\n"
                   "  (-> (&& (>= value min) (<= value max)) result))",
    "advanced003": "§S (! (> result a))\n§S (! (> result b))\n§S (|| (== a result) (== b result))",
    "advanced004": "§Q (!= 0 b)\n§S (== (/ a b) result)",
}


def legacy_source(profile, *, body=None, contracts=None, signature=None, mod_body=None, mod_contracts=None):
    _, name, parameters, output, required, original, _ = TASKS[profile]
    name = name.split("+")[0]
    declaration = signature or f"§F{{{name}:pub}} ({parameters}) -> {output}"
    lines = (required if contracts is None else contracts).splitlines()
    lines += (original if body is None else body).splitlines()
    text = FIXTURE + "  " + declaration + "\n" + "".join("    " + line + "\n" for line in lines)
    if profile == "advanced004":
        lines = ("§Q (!= b 0)\n§S (== result (% a b))" if mod_contracts is None else mod_contracts).splitlines()
        lines += ("§R (% a b)" if mod_body is None else mod_body).splitlines()
        text += "  §F{SafeMod:pub} (i32:a, i32:b) -> i32\n" + "".join("    " + line + "\n" for line in lines)
    return text


def legacy_matrix():
    source = legacy_source
    controls = []
    def add(category, profile, text, positive=False, detail=None):
        controls.append((category, profile, text, positive, detail))
    for profile, (_, name, parameters, output, required, _, _) in TASKS.items():
        name = name.split("+")[0]
        add("native-positive", profile, source(profile), True)
        add("equivalent-alias-body", profile, source(profile, body=ALTERNATIVES[profile],
            contracts=EQUIVALENT[profile], signature=f"§F{{{name}:pub}} ({parameters.replace('i32', 'int')}) -> {output}"), True)
        add("total-extra-contracts", profile, source(profile, contracts=required + "\n§Q true\n§S true",
            body="§E{throw}\n" + TASKS[profile][5]), True)
        add("scoped-inline-refinement", profile, source(profile,
            signature=f"§F{{{name}:pub}} ({parameters} | true) -> {output}"), True)
        add("missing-file", profile, None, detail="Input missing")
        add("missing-native-function", profile, FIXTURE, detail="Expected exactly one native Math")
        add("comment-only", profile, FIXTURE + "// " + source(profile).replace("\n", "\n// "))
        add("duplicate-native", profile, source(profile) + source(profile)[len(FIXTURE):])
        add("private-signature", profile, source(profile, signature=f"§F{{{name}:priv}} ({parameters}) -> {output}"))
        add("wide-signature", profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters.replace('i32', 'i64')}) -> {output}"))
        add("wrong-output", profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters}) -> str"))
        add("wrong-parameter-order", profile, source(profile).replace(
            "i32:a, i32:b", "i32:b, i32:a") if profile in ("advanced003", "advanced004") else
            source(profile).replace("i32:value, i32:min, i32:max", "i32:min, i32:value, i32:max")
            if profile == "advanced002" else source(profile).replace("i32:x", "i32:y"))
        add("wrong-owner", profile, source(profile).replace("§M{MathUtils}", "§M{Other}"))
        add("raw-opaque", profile, source(profile, body="§RAW return 0; §/RAW"))
        add("missing-contracts", profile, source(profile, contracts=""))
        add("vacuous-contracts", profile, source(profile, contracts="§Q true\n§S true"))
        add("foreign-contracts", profile, source(profile, contracts="") +
            "  §F{Foreign:pub} (" + parameters + ") -> " + output + "\n" +
            "".join("    " + line + "\n" for line in (required + "\n§R " + ("false" if output == "bool" else "0")).splitlines()))
        add("narrowed-entry", profile, source(profile, contracts=required + "\n§Q false"))
        add("excluded-required-output", profile, source(profile, contracts=required + "\n§S false"))
        add("wrong-behavior", profile, source(profile, body="§R false" if output == "bool" else "§R 0"))
        add("declared-effect", profile, source(profile, body='§E{cw}\n§P "unexpected"\n' + TASKS[profile][5]))
        add("external-state", profile, source(profile, body="§R §C{System.Environment.TickCount} §/C"))
    add("non-nested-sign", "advanced001", source("advanced001",
        body="§IF (< x 0)\n  §R -1\n§IF (== x 0)\n  §R 0\n§R 1"))
    add("dead-nested-helper", "advanced001", source("advanced001",
        body="§IF (< x 0)\n  §R -1\n§IF (== x 0)\n  §R 0\n§R 1") +
        "  §F{Unused:priv} (i32:x) -> i32\n    §R (? (< x 0) -1 (? (== x 0) 0 1))\n")
    add("tautological-nested", "advanced001", source("advanced001",
        body="§R (? (== x x) §C{Independent} §A x §/C (? true §C{Independent} §A x §/C 0))") +
        "  §F{Independent:priv} (i32:x) -> i32\n    §IF (< x 0)\n      §R -1\n    §IF (== x 0)\n      §R 0\n    §R 1\n")
    add("sign-missing-negative-bound", "advanced001", source("advanced001", contracts="§S (<= result 1)"))
    add("sign-missing-positive-bound", "advanced001", source("advanced001", contracts="§S (>= result -1)"))
    add("range-one-way-iff", "advanced002", source("advanced002",
        contracts="§Q (<= min max)\n§S (-> result (&& (>= value min) (<= value max)))"))
    add("range-other-way-iff", "advanced002", source("advanced002",
        contracts="§Q (<= min max)\n§S (-> (&& (>= value min) (<= value max)) result)"))
    add("range-exclusive-boundary", "advanced002", source("advanced002", body="§R (&& (> value min) (< value max))"))
    for index in range(3):
        add("min-missing-symmetric-" + str(index), "advanced003", source("advanced003",
            contracts="\n".join(line for serial, line in enumerate(TASKS["advanced003"][4].splitlines()) if serial != index)))
    add("min-wrong-tie-supplier", "advanced003", source("advanced003", body="§R (? (< a b) a b)"))
    add("min-call-input-is-not-chosen-value", "advanced003", source("advanced003",
        body="§R §C{System.Math.Min} §A a §A b §/C"), detail="Math successful supplying-path/output/contract coupling failed")
    add("division-divisibility-narrowing", "advanced004", source("advanced004",
        contracts=TASKS["advanced004"][4] + "\n§Q (== (% a b) 0)"))
    add("division-overflow-guard-narrowing", "advanced004", source("advanced004",
        contracts=TASKS["advanced004"][4] + "\n§Q (! (&& (== a INT:-2147483648) (== b -1)))"))
    add("mod-missing-function", "advanced004", source("advanced004").split("  §F{SafeMod:pub}")[0])
    add("mod-missing-precondition", "advanced004", source("advanced004", mod_contracts="§S (== result (% a b))"))
    add("mod-missing-postcondition", "advanced004", source("advanced004", mod_contracts="§Q (!= b 0)"))
    add("mod-wrong-sign", "advanced004", source("advanced004", mod_body="§R (? (< a 0) (- 0 (% a b)) (% a b))"))
    add("division-dead-operation", "advanced004", source("advanced004", body="§B{unused:i32} (/ a b)\n§R §C{Independent} §A a §A b §/C") +
        "  §F{Independent:priv} (i32:a, i32:b) -> i32\n    §R (cast i32 (/ (cast i64 a) (cast i64 b)))\n",
        detail="Math successful supplying-path/output/contract coupling failed")
    return controls


NATIVE_FIXTURE = """module MathUtils
  func Add (int:a, int:b) → int
    return a + b

  func Negate (int:x) → int
    return 0 - x

  func IsEven (int:x) → bool
    return x % 2 == 0

"""
NATIVE_TASKS = {
    "advanced001": ("int:x", "int",
                    "=> result == -1 || result == 0 || result == 1",
                    "return x < 0 ? -1 : (x == 0 ? 0 : 1)"),
    "advanced002": ("int:value, int:min, int:max", "bool",
                    "requires min <= max\n=> result == (value >= min && value <= max)",
                    "return value >= min && value <= max"),
    "advanced003": ("int:a, int:b", "int",
                    "=> result <= a\n=> result <= b\n=> result == a || result == b",
                    "return a <= b ? a : b"),
    "advanced004": ("int:a, int:b", "int",
                    "requires b != 0\n=> result == a / b",
                    "return a / b"),
}
NATIVE_ALTERNATIVES = {
    "advanced001": "let int:sign = x > 0 ? 1 : (x < 0 ? -1 : 0)\nreturn sign",
    "advanced002": "if value < min\n  return false\nreturn !(value > max)",
    "advanced003": "let int:left = a\nlet int:right = b\nif left <= right\n  return left\nreturn right",
    "advanced004": "let int:quotient = a / b\nreturn quotient",
}
NATIVE_EQUIVALENT = {
    "advanced001": "=> result >= -1 && result <= 1",
    "advanced002": "requires !(min > max)\n"
                   "=> (!result || (value >= min && value <= max)) && (!(value >= min && value <= max) || result)",
    "advanced003": "=> !(result > a)\n=> !(result > b)\n=> a == result || b == result",
    "advanced004": "requires 0 != b\n=> a / b == result",
}


def source(profile, *, body=None, contracts=None, signature=None, mod_body=None, mod_contracts=None):
    parameters, output, required, original = NATIVE_TASKS[profile]
    name = TASKS[profile][1].split("+")[0]
    declaration = signature or f"func {name} ({parameters}) → {output}"
    lines = (required if contracts is None else contracts).splitlines()
    lines += (original if body is None else body).splitlines()
    text = NATIVE_FIXTURE + "  " + declaration + "\n" + "".join("    " + line + "\n" for line in lines)
    if profile == "advanced004":
        lines = ("requires b != 0\n=> result == a % b" if mod_contracts is None else mod_contracts).splitlines()
        lines += ("return a % b" if mod_body is None else mod_body).splitlines()
        text += "  func SafeMod (int:a, int:b) → int\n" + "".join("    " + line + "\n" for line in lines)
    return text


def matrix():
    controls = []
    semantic_failures = {
        "raw-opaque": "Raw interop cannot establish native advanced MathUtils",
        "foreign-contracts": "Math missing/weakened postconditions",
        "narrowed-entry": "Math missing/wrong entry",
        "excluded-required-output": "Math output predicates exclude",
        "wrong-behavior": "Math baseline native postcondition rejected",
        "declared-effect": "Unsupported Math executable call surface",
        "external-state": "Unsupported Math executable property",
        "non-nested-sign": "Math successful supplying-path/output/contract coupling failed",
        "dead-nested-helper": "Math successful supplying-path/output/contract coupling failed",
        "tautological-nested": "Math successful supplying-path/output/contract coupling failed",
        "sign-missing-negative-bound": "Math missing/weakened postconditions",
        "sign-missing-positive-bound": "Math missing/weakened postconditions",
        "range-one-way-iff": "Math missing/weakened postconditions",
        "range-other-way-iff": "Math missing/weakened postconditions",
        "range-exclusive-boundary": "Math baseline native postcondition rejected",
        "min-wrong-tie-supplier": "Math successful supplying-path/output/contract coupling failed",
        "division-divisibility-narrowing": "Math missing/wrong entry",
        "division-overflow-guard-narrowing": "Math missing/wrong entry",
        "mod-missing-precondition": "Math missing/wrong entry",
        "mod-missing-postcondition": "Math missing/weakened postconditions",
        "mod-wrong-sign": "Math baseline native postcondition rejected",
    }
    def add(category, profile, text, positive=False, detail=None):
        if category == "foreign-contracts" and profile in ("advanced002", "advanced004"):
            detail = "Math missing/wrong entry"
        if category == "wrong-behavior" and profile == "advanced001":
            detail = "Wrong Math independent"
        controls.append((category, profile, text, positive,
                         detail or semantic_failures.get(category)
                         or ("Math missing/weakened postconditions" if category.startswith("min-missing-symmetric-") else None)))
    for profile, (parameters, output, required, original) in NATIVE_TASKS.items():
        name = TASKS[profile][1].split("+")[0]
        add("native-positive", profile, source(profile), True)
        add("equivalent-alias-body", profile, source(profile, body=NATIVE_ALTERNATIVES[profile],
            contracts=NATIVE_EQUIVALENT[profile],
            signature=f"func {name} ({parameters.replace('int', 'System.Int32')}) → {output}"), True)
        add("total-extra-contracts", profile, source(profile, contracts=required + "\nrequires true\n=> true",
            body="! throw\n" + original), True)
        add("scoped-inline-refinement", profile, source(profile,
            signature=f"func {name} ({parameters} | true) → {output}"), True)
        add("missing-file", profile, None, detail="Input missing")
        add("missing-native-function", profile, NATIVE_FIXTURE, detail="Expected exactly one native Math")
        add("comment-only", profile, NATIVE_FIXTURE + "// " + source(profile).replace("\n", "\n// "))
        add("duplicate-native", profile, source(profile) + source(profile)[len(NATIVE_FIXTURE):])
        add("private-signature", profile, source(profile, signature=f"func {name} private ({parameters}) → {output}"))
        add("wide-signature", profile, source(profile, signature=f"func {name} ({parameters.replace('int', 'long')}) → {output}"))
        add("wrong-output", profile, source(profile, signature=f"func {name} ({parameters}) → string"))
        add("wrong-parameter-order", profile, source(profile).replace(
            "int:a, int:b", "int:b, int:a") if profile in ("advanced003", "advanced004") else
            source(profile).replace("int:value, int:min, int:max", "int:min, int:value, int:max")
            if profile == "advanced002" else source(profile).replace("int:x", "int:y"))
        add("wrong-owner", profile, source(profile).replace("module MathUtils", "module Other"))
        add("raw-opaque", profile, source(profile, body="raw statement 13\nreturn 0;"))
        add("missing-contracts", profile, source(profile, contracts=""))
        add("vacuous-contracts", profile, source(profile, contracts="requires true\n=> true"))
        add("foreign-contracts", profile, source(profile, contracts="") +
            f"  func Foreign ({parameters}) → {output}\n" +
            "".join("    " + line + "\n" for line in (required + "\nreturn " + ("false" if output == "bool" else "0")).splitlines()))
        add("narrowed-entry", profile, source(profile, contracts=required + "\nrequires false"))
        add("excluded-required-output", profile, source(profile, contracts=required + "\n=> false"))
        add("wrong-behavior", profile, source(profile, body="return false" if output == "bool" else "return 0"))
        add("declared-effect", profile, source(profile, body='! cw\nSystem.Console.WriteLine("unexpected")\n' + original))
        add("external-state", profile, source(profile, body="return System.Environment.TickCount"))
    add("non-nested-sign", "advanced001", source("advanced001",
        body="if x < 0\n  return -1\nif x == 0\n  return 0\nreturn 1"))
    add("dead-nested-helper", "advanced001", source("advanced001",
        body="if x < 0\n  return -1\nif x == 0\n  return 0\nreturn 1") +
        "  func Unused private (int:x) → int\n    return x < 0 ? -1 : (x == 0 ? 0 : 1)\n")
    add("tautological-nested", "advanced001", source("advanced001",
        body="return x == x ? Independent(x) : (true ? Independent(x) : 0)") +
        "  func Independent private (int:x) → int\n    if x < 0\n      return -1\n    if x == 0\n      return 0\n    return 1\n")
    add("sign-missing-negative-bound", "advanced001", source("advanced001", contracts="=> result <= 1"))
    add("sign-missing-positive-bound", "advanced001", source("advanced001", contracts="=> result >= -1"))
    add("range-one-way-iff", "advanced002", source("advanced002",
        contracts="requires min <= max\n=> !result || (value >= min && value <= max)"))
    add("range-other-way-iff", "advanced002", source("advanced002",
        contracts="requires min <= max\n=> !(value >= min && value <= max) || result"))
    add("range-exclusive-boundary", "advanced002", source("advanced002", body="return value > min && value < max"))
    for index in range(3):
        add("min-missing-symmetric-" + str(index), "advanced003", source("advanced003",
            contracts="\n".join(line for serial, line in enumerate(NATIVE_TASKS["advanced003"][2].splitlines()) if serial != index)))
    add("min-wrong-tie-supplier", "advanced003", source("advanced003", body="return a < b ? a : b"))
    add("min-call-input-is-not-chosen-value", "advanced003", source("advanced003",
        body="return System.Math.Min(a, b)"), detail="Math successful supplying-path/output/contract coupling failed")
    add("division-divisibility-narrowing", "advanced004", source("advanced004",
        contracts=NATIVE_TASKS["advanced004"][2] + "\nrequires a % b == 0"))
    add("division-overflow-guard-narrowing", "advanced004", source("advanced004",
        contracts=NATIVE_TASKS["advanced004"][2] + "\nrequires !(a == -2147483648 && b == -1)"))
    add("mod-missing-function", "advanced004", source("advanced004").split("  func SafeMod ")[0])
    add("mod-missing-precondition", "advanced004", source("advanced004", mod_contracts="=> result == a % b"))
    add("mod-missing-postcondition", "advanced004", source("advanced004", mod_contracts="requires b != 0"))
    add("mod-wrong-sign", "advanced004", source("advanced004", mod_body="return a < 0 ? 0 - a % b : a % b"))
    add("division-dead-operation", "advanced004", source("advanced004", body="let int:unused = a / b\nreturn Independent(a, b)") +
        "  func Independent private (int:a, int:b) → int\n    return (int)((long)a / (long)b)\n",
        detail="Math successful supplying-path/output/contract coupling failed")
    if [(item[0], item[1], item[3]) for item in controls] != [
            (item[0], item[1], item[3]) for item in legacy_matrix()]:
        raise RuntimeError("Canonical Math controls changed the original category/profile/expectation census.")
    return controls


class MathTests(unittest.TestCase):
    totals = [0, 0, 0, 0, 0]  # actual, positive, finite, prerequisite refusal, declared timeout
    lock = threading.Lock()

    def verify(self, profile, text, positive=True, *, root=ROOT, deadline=None, detail=None, real=True, declared_timeout=None):
        if declared_timeout is not None and (positive or detail != "timeout" or deadline is not None
                                             or not 0 < declared_timeout <= 120):
            raise ValueError("Math declared timeout requires an explicit bounded negative, not a global deadline.")
        with tempfile.TemporaryDirectory(prefix="calor-math-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "MathUtils.calr").write_bytes(text.encode("utf-8"))
            end = min(deadline or float("inf"), time.monotonic() + (declared_timeout or 120))
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(root.resolve()),
                               CALOR_LOGIC_TIMEOUT_SECONDS=str(min(120, end - time.monotonic())))
            command = [str(HERE / "tasks/github-projects" / TASKS[profile][0] / "verify.sh"), str(workspace)]
            try:
                output = ENGINE.run(command, HERE, end, env=environment)
                passed = True
            except RuntimeError as error:
                output = SHARED.classify(error, timeout_seconds=declared_timeout, commands=(command[0], "dotnet"))
                if declared_timeout is None:
                    payload = output.removeprefix("Verifier command exited 1:\n")
                    while payload.startswith("Verification failed: Verifier command exited 1:\n"):
                        payload = payload.removeprefix("Verification failed: Verifier command exited 1:\n")
                    payload = payload.removeprefix("Verification failed: ")
                    if not FINITE.match(payload):
                        raise RuntimeError("Math unknown finite diagnostic; not admission evidence:\n" + output) from error
                passed = False
            identity = f"Selected compiler: {root.resolve() / 'src/Calor.Compiler/bin' / environment.get('CALOR_COMPILER_CONFIGURATION', 'Debug') / 'net10.0/calor.dll'}"
            identities = [line for line in output.splitlines() if re.search(r"Selected\s+compiler\b", line, re.IGNORECASE)]
            if identities and identities != [identity]:
                raise RuntimeError("Math noncanonical or duplicated selected compiler identity.")
            self.assertEqual(passed, positive, output)
            if positive:
                ENGINE.check_advanced_success(output, identity)
                cases, rejected, classes = TASKS[profile][6]
                verdict = (f"Verification passed: {TASKS[profile][1]}; Math native/CLR predicates; {cases} runtime cases; "
                           f"{rejected} entry rejections; {classes} supplying classes; {classes} output classes; {classes} contract classes.")
                self.assertEqual([line for line in output.splitlines() if line.startswith("Verification passed: ")], [verdict], output)
                self.assertIn("Limits: full signed Int32", output)
            if detail:
                self.assertIn(detail, output)
            if real:
                with self.lock:
                    if declared_timeout is not None:
                        self.totals[4] += 1
                    else:
                        self.totals[0] += 1
                        self.totals[1 if positive else 2] += 1
            return output

    def test_current_matrix(self):
        before = self.totals.copy()
        controls = matrix()
        with ThreadPoolExecutor(max_workers=2) as workers:
            for offset in range(0, len(controls), 2):
                futures = [(item, workers.submit(self.verify, item[1], item[2], item[3], detail=item[4]))
                           for item in controls[offset:offset + 2]]
                for (category, profile, _text, positive, _detail), future in futures:
                    with self.subTest(category=category, profile=profile):
                        future.result()
                        print(f"current {profile} {category}: {'passed' if positive else 'finite rejected'}", flush=True)
        wanted = [108, 16, 92, 0, 0]
        self.assertEqual([len(matrix()), sum(item[3] for item in matrix()), sum(not item[3] for item in matrix()), 0, 0], wanted)
        self.assertEqual([after - prior for after, prior in zip(self.totals, before)], wanted)
        self.assertGreater(wanted[1], 0)
        self.assertGreater(wanted[2], 0)
        print("Math exact current actual/positive/finite/refusal/timeout census:", wanted)

    def test_fail_closed_shared_guards(self):
        before = self.totals.copy()
        SHARED.AdvancedContractTests("test_supervision_and_identity_guards").test_supervision_and_identity_guards()
        for profile in TASKS:
            with patch.object(ENGINE, "run", side_effect=RuntimeError(
                    "Verifier command exited 1:\nVerification failed: Math solver infrastructure failure: UNKNOWN")):
                with self.assertRaises(RuntimeError):
                    self.verify(profile, source(profile), False, real=False)
            identity = f"Selected compiler: {ROOT / 'src/Calor.Compiler/bin/Debug/net10.0/calor.dll'}\n"
            with patch.object(ENGINE, "run", return_value=identity + identity + "Verification passed: forged\n"):
                with self.assertRaises(RuntimeError):
                    self.verify(profile, source(profile), real=False)
            for message in ("unexpected diagnostic", "System.NullReferenceException: defective verifier",
                            "Math solver infrastructure failure: UNKNOWN"):
                with patch.object(ENGINE, "run", side_effect=RuntimeError(
                        "Verifier command exited 1:\nVerification failed: " + message)):
                    with self.assertRaises(RuntimeError):
                        self.verify(profile, source(profile), False, real=False)
        self.assertEqual(self.totals, before)

    def test_declared_timeout(self):
        before = self.totals.copy()
        self.verify("advanced001", source("advanced001", body="while true\n  continue\nreturn 0"),
                    False, detail="timeout", declared_timeout=5)
        self.assertEqual([after - prior for after, prior in zip(self.totals, before)], [0, 0, 0, 0, 1])

    def test_native_image_refusal(self):
        before = self.totals.copy()
        configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
        relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
        with tempfile.TemporaryDirectory(prefix="calor-math-invalid-image-") as temporary:
            arm = Path(temporary)
            shutil.copytree(ROOT / relative, arm / relative)
            shutil.copytree(ROOT / ".github", arm / ".github")
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid selected image")
            for profile in TASKS:
                with self.assertRaisesRegex(RuntimeError, "manifest mismatch"):
                    self.verify(profile, source(profile), False, root=arm)
                self.totals[3] += 1
        self.assertEqual([after - prior for after, prior in zip(self.totals, before)], [0, 0, 0, 4, 0])

    def test_original_task_fixture_preserved(self):
        for index, (directory, *_rest) in enumerate(TASKS.values(), 1):
            task = json.loads((HERE / "tasks/github-projects" / directory / "task.json").read_text())
            self.assertEqual(task["id"], f"advanced-{index:03}")
            self.assertEqual(task["timeout"], 120)
            self.assertEqual(task["fixture"], "advanced-calor-project")


def build(root, deadline):
    print(ENGINE.run([sys.executable, "scripts/verify-z3-assets.py"], root, deadline), end="")
    print(ENGINE.run(["dotnet", "restore", "src/Calor.Compiler/Calor.Compiler.csproj", "--locked-mode",
                     "--source", str(Path.home() / ".nuget/packages"), "--disable-build-servers",
                     "-p:UseSharedCompilation=false", "-nodeReuse:false", "-v", "quiet"], root, deadline), end="")
    print(ENGINE.run(["dotnet", "build", "src/Calor.Compiler/Calor.Compiler.csproj", "--no-restore",
                     "-c", os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug"), "--disable-build-servers",
                     "-p:UseSharedCompilation=false", "-nodeReuse:false", "--nologo", "-v", "quiet"], root, deadline), end="")


def archived():
    deadline = time.monotonic() + 600
    build(ROOT, deadline)
    with tempfile.TemporaryDirectory(prefix="calor-math-v029-") as temporary:
        directory = Path(temporary)
        archive = directory / "source.tar"
        old = directory / "source"
        old.mkdir()
        ENGINE.run(["git", "archive", "--format=tar", f"--output={archive}", OLD], ROOT, deadline)
        with tarfile.open(archive) as stream:
            stream.extractall(old, filter="data")
        for relative in ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes"):
            shutil.copytree(ROOT / relative, old / relative)
        build(old, deadline)
        test = MathTests()
        before = test.totals.copy()
        shared = {"missing-file", "missing-native-function", "comment-only", "duplicate-native", "private-signature",
                  "wide-signature", "wrong-output", "wrong-parameter-order", "wrong-owner", "raw-opaque",
                  "foreign-contracts", "declared-effect", "external-state", "missing-contracts", "vacuous-contracts"}
        strict = [item for item in matrix() if item[0] not in shared or item[1] == "advanced001"]
        if (len(strict) != 63 or {item[0] for item in strict} != {item[0] for item in matrix()}
                or sum(item[3] for item in strict) != 16):
            raise RuntimeError("Math compatibility lost a distinct positive variant or adversarial category.")
        for arm, root, historical in (("v0.29", old, True), ("current", ROOT, False)):
            candidates = [item for item in (legacy_matrix() if historical else matrix())
                          if item[0] not in shared or item[1] == "advanced001"]
            def execute(item):
                category, profile, text, positive, detail = item
                adapted = SHARED.historical_syntax(text) if historical and text is not None else text
                test.verify(profile, adapted, positive, root=root, deadline=deadline, detail=detail)
                return f"{arm} {profile} {category}: {'passed' if positive else 'finite rejected'}"
            with ThreadPoolExecutor(max_workers=2) as workers:
                for offset in range(0, len(candidates), 2):
                    for verdict in workers.map(execute, candidates[offset:offset + 2]):
                        print(verdict, flush=True)
        wanted = [126, 32, 94, 0, 0]
        actual = [after - prior for after, prior in zip(test.totals, before)]
        if actual != wanted or not actual[1] or not actual[2]:
            raise RuntimeError(f"Math archived exact nonempty census {actual} != {wanted}.")
        print(f"Math actual unchanged {OLD}/current census: {actual}; all distinct categories retained.")


if __name__ == "__main__":
    if sys.argv[1:] == ["--archived"]:
        try:
            archived()
        except Exception as error:
            print(f"Math archived controls BLOCKED (not a finite rejection or timeout count): {error}", file=sys.stderr)
            sys.exit(1)
    else:
        build(ROOT, time.monotonic() + 600)
        unittest.main(verbosity=2)
