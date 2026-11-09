#!/usr/bin/env python3
"""Strict native/CLR acceptance controls for the unchanged ac-001..005 prompts."""

from concurrent.futures import ThreadPoolExecutor
import importlib.util
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


ENGINE = load("advanced_engine", "lib/verify-logic.py")
ADAPTER = load("advanced_old_syntax", "test-contract-writing-verifiers.py")
TASKS = {
    "ac001": ("01_forall_quantifier", "AllPositive", "i32[]:arr", "bool",
              "§S (-> result (forall ((i i32)) (-> (&& (>= i 0) (< i arr.Length)) (> arr{i} 0))))",
              "§B{~index:i32} 0\n§WH (< index arr.Length)\n"
              "  §IF (<= arr{index} 0)\n    §R false\n  §ASSIGN index (+ index 1)\n§R true"),
    "ac002": ("02_exists_quantifier", "HasNegative", "i32[]:arr", "bool",
              "§S (-> result (exists ((i i32)) (&& (>= i 0) (< i arr.Length) (< arr{i} 0))))",
              "§B{~index:i32} 0\n§WH (< index arr.Length)\n"
              "  §IF (< arr{index} 0)\n    §R true\n  §ASSIGN index (+ index 1)\n§R false"),
    "ac003": ("03_implication", "SafeIncrement", "i32:x", "i32",
              "§S (-> (> x 0) (> result x))", "§R (+ x 1)"),
    "ac004": ("04_custom_error_message", "Divide", "i32:a, i32:b", "i32",
              '§Q{"Divisor must not be zero"} (!= b 0)', "§R (/ a b)"),
    "ac005": ("05_multiple_contracts", "ClampValue", "i32:min, i32:max, i32:value", "i32",
              "§Q (<= min max)\n§Q (>= max min)\n§S (>= result min)\n§S (<= result max)\n"
              "§S (-> (&& (>= value min) (<= value max)) (== result value))",
              "§IF (< value min)\n  §R min\n§IF (> value max)\n  §R max\n§R value"),
}
COUNTS = {"ac001": (225, 0, 13), "ac002": (225, 0, 13), "ac003": (12, 0, 0),
          "ac004": (155, 5, 0), "ac005": (1183, 3, 0)}
ALTERNATIVES = {
    "ac001": "§B{end:i32} (- arr.Length 1)\n§B{~ok:bool} true\n§L{index:0:end:1}\n"
             "  §IF (<= arr{index} 0)\n    §ASSIGN ok false\n§R ok",
    "ac002": "§B{end:i32} (- arr.Length 1)\n§B{~found:bool} false\n§L{index:0:end:1}\n"
             "  §IF (< arr{index} 0)\n    §ASSIGN found true\n§R found",
    "ac003": "§B{next:int} (+ 1 x)\n§R next",
    "ac004": "§B{quotient:int} (/ a b)\n§R quotient",
    "ac005": "§R (? (< value min) min (? (> value max) max value))",
}
EQUIVALENT = {
    "ac001": TASKS["ac001"][4].replace("(>= i 0)", "(> i -1)").replace("(> arr{i} 0)", "(! (<= arr{i} 0))"),
    "ac002": TASKS["ac002"][4].replace("(>= i 0)", "(> i -1)").replace("(< arr{i} 0)", "(! (>= arr{i} 0))"),
    "ac003": "§S (-> (! (<= x 0)) (! (<= result x)))",
    "ac004": '§Q{"Divisor must not be zero"} (!= 0 b)',
    "ac005": "§Q (! (> min max))\n§Q (! (< max min))\n§S (! (< result min))\n§S (! (> result max))\n"
             "§S (-> (&& (! (< value min)) (! (> value max))) (== value result))",
}


def source(profile, *, body=None, contracts=None, signature=None):
    _, name, parameters, output, required, original = TASKS[profile]
    declaration = signature or f"§F{{{name}:pub}} ({parameters}) -> {output}"
    # A spaced dot produces a native member node in both lexers, rather than an
    # old-parser qualified-name token. This changes no predicate or input type.
    lines = (required if contracts is None else contracts).replace("arr.Length", "arr . Length").splitlines()
    # The native array-length expression is shared by both compiler arms. Keep
    # .Length in contracts, where the certified quantifier lowerer requires it.
    lines += (original if body is None else body).replace("arr.Length", "§LEN arr").splitlines()
    return "§M{Contracts}\n  " + declaration + "\n" + "".join("    " + line + "\n" for line in lines)


def historical_syntax(text):
    lines = []
    for index, line in enumerate(ADAPTER.historical_syntax(text).splitlines()):
        stripped = line.lstrip()
        if stripped.startswith("§WH "):
            line = line.replace("§WH ", f"§WH{{advancedWhile{index}}} ", 1)
        elif stripped.startswith("§EACH{"):
            line = line.replace("§EACH{", f"§EACH{{advancedEach{index}:", 1)
        lines.append(line)
    return ("\n".join(lines) + "\n").replace("§LAM{", "§LAM{advancedLambda:")


def matrix():
    """Each label is a distinct admission obligation, not a text-match proxy."""
    controls = []
    def add(category, profile, text, expected=False, detail=None):
        controls.append((category, profile, text, expected, detail))
    for profile, (_, name, parameters, output, required, original) in TASKS.items():
        add("native-positive", profile, source(profile), True)
        signature = f"§F{{{name}:pub}} ({parameters.replace('i32', 'int')}) -> {output.replace('i32', 'int')}"
        add("equivalent-alias-loop", profile, source(profile, body=ALTERNATIVES[profile],
            contracts=EQUIVALENT[profile], signature=signature), True)
        add("scoped-refinement", profile, source(profile,
            signature=f"§F{{{name}:pub}} ({parameters} | true) -> {output}"), True)
        add("scoped-unrelated", profile, source(profile, contracts=required + "\n§Q true\n§S true")
            + "  §F{Other:pub} () -> i32\n    §S (== result 7)\n    §R 7\n", True)
        add("missing-file", profile, None, detail="Input missing")
        add("missing-native-function", profile, "§M{Contracts}\n", detail="Expected exactly one module function")
        add("comment-only", profile, "§M{Contracts}\n// " + source(profile).replace("\n", "\n// "),
            detail="Expected exactly one module function")
        add("duplicate", profile, source(profile) + source(profile).split("\n", 1)[1])
        add("private-signature", profile, source(profile, signature=f"§F{{{name}:priv}} ({parameters}) -> {output}"),
            detail="Wrong public signature")
        add("wide-signature", profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters.replace('i32', 'i64')}) -> {output}"))
        add("wrong-output", profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters}) -> str"))
        add("missing-contracts", profile, source(profile, contracts=""), detail="Advanced")
        add("vacuous-contracts", profile, source(profile, contracts="§Q true\n§S true"), detail="Advanced")
        add("foreign-contracts", profile, source(profile, contracts="") +
            f"  §F{{Foreign:pub}} ({parameters}) -> {output}\n"
            + "".join("    " + line + "\n" for line in (required + "\n" + original).splitlines()), detail="Advanced")
        add("narrowed-entry", profile, source(profile, contracts=required + "\n§Q false"),
            detail="Advanced missing/wrong entry predicates")
        add("excluded-output", profile, source(profile, contracts=required + "\n§S false"),
            detail="Advanced output predicates exclude")
        add("baseline-error", profile, source(profile, body="§R false" if output == "bool" else "§R 0"))
        add("raw-body", profile, source(profile, body="§RAW return " + ("true;" if output == "bool" else "0;") + " §/RAW"))
        add("wrong-owner", profile, "§M{Contracts}\n  §CL{Other:pub}\n"
            + "".join("  " + line + "\n" for line in source(profile).splitlines()[1:]))
    for profile in ("ac001", "ac002"):
        required = TASKS[profile][4]
        original = TASKS[profile][5]
        each_body = ("§EACH{item:i32} arr\n  §IF " + ("(<= item 0)\n    §R false\n§R true"
            if profile == "ac001" else "(< item 0)\n    §R true\n§R false"))
        add("native-foreach-alias", profile, source(profile, body=each_body).replace("arr", "values"), True)
        add("wrong-quantifier", profile, source(profile, contracts=required.replace(
            "forall" if profile == "ac001" else "exists", "exists" if profile == "ac001" else "forall")))
        add("unbounded-quantifier", profile, source(profile, contracts=required.replace("(< i arr.Length)", "true")))
        add("wrong-bound", profile, source(profile, contracts=required.replace("(< i arr.Length)", "(< i 1)")))
        add("wrong-sign", profile, source(profile, contracts=required.replace(
            "(> arr{i} 0)" if profile == "ac001" else "(< arr{i} 0)", "(>= arr{i} 0)")))
        add("vacuous-quantifier", profile, source(profile, contracts=required.replace("arr{i}", "i")))
        add("wrong-result-implication", profile, source(profile, contracts=required.replace("(-> result ", "(-> false ")))
        add("empty-array-error", profile, source(profile, body="§IF (== arr.Length 0)\n  §R "
            + ("false" if profile == "ac001" else "true") + "\n" + original))
        add("state-mutation", profile, source(profile, body="§IF (> arr.Length 0)\n  §ASSIGN arr{0} 1\n" + original))
        add("foreign-payload", profile, source(profile, body=original.replace("arr{index}", "foreign{index}")
            .replace("§B{~index", "§B{foreign:i32[]} §C{System.Linq.Enumerable.ToArray} §A arr §/C\n§B{~index")),
            detail="Advanced foreign array provenance")
        add("native-contract-runtime-error", profile, source(profile, body="§R true"),
            detail="Contract: Ensures")
        # Baseline is correct; a read used only by a tautology or a private
        # precondition cannot supply the successful independently changed output.
        independent = "§R §C{Independent} §A arr §/C"
        decoy = "§B{end:i32} (- arr.Length 1)\n§L{index:0:end:1}\n  §B{unused:i32} arr{index}\n" + independent
        # Independent computes through BCL, leaving no native read to mutate.
        bcl = "§R §C{System.Linq.Enumerable." + ("All" if profile == "ac001" else "Any") + \
            "} §A arr §A §LAM{item:i32} " + ("(> item 0)" if profile == "ac001" else "(< item 0)") + " §/LAM §/C"
        add("loop-payload-decoy", profile, source(profile, body=decoy)
            + "  §F{Independent:priv} (i32[]:arr) -> bool\n    " + bcl + "\n",
            detail="Advanced successful array output coupling failed")
        add("tautological-read-decoy", profile, source(profile, body=decoy.replace(
            "§B{unused:i32} arr{index}", "§IF (== arr{index} arr{index})\n    §B{unused:i32} 0"))
            + "  §F{Independent:priv} (i32[]:arr) -> bool\n    " + bcl + "\n",
            detail="Advanced successful array output coupling failed")
        add("mutant-only-exception", profile, source(profile, body="§E{throw}\n§B{end:i32} (- arr.Length 1)\n§L{index:0:end:1}\n"
            "  §IF (== arr.Length 1)\n    §C{RequireValue} §A arr{index} §A §C{System.Linq.Enumerable.First} §A arr §/C §/C\n"
            + independent)
            + '  §F{RequireValue:priv} (i32:seen, i32:again) -> void\n    §E{throw}\n'
              '    §IF (!= seen again)\n      §TH "mutant failed"\n'
            + "  §F{Independent:priv} (i32[]:arr) -> bool\n    " + bcl + "\n",
            detail="Advanced counterfactual runtime failed; exceptions are not coupling evidence")
    add("vacuous-implication", "ac003", source("ac003", contracts="§S (-> false (> result x))"))
    add("unconditional-implication", "ac003", source("ac003", contracts="§S (> result x)"))
    add("wrong-increment", "ac003", source("ac003", body="§R (+ x 2)"))
    add("missing-message", "ac004", source("ac004", contracts="§Q (!= b 0)"))
    add("wrong-message", "ac004", source("ac004", contracts='§Q{"Different"} (!= b 0)'))
    add("vacuous-message", "ac004", source("ac004", contracts='§Q (!= b 0)\n§Q{"Divisor must not be zero"} true'))
    add("divisibility-narrowing", "ac004", source("ac004", contracts=TASKS["ac004"][4] + "\n§Q (== (% a b) 0)"))
    add("wrong-division", "ac004", source("ac004", body="§R (* a b)"))
    add("missing-multiple-entry", "ac005", source("ac005", contracts=TASKS["ac005"][4].replace("§Q (>= max min)\n", "")))
    add("vacuous-multiple-entry", "ac005", source("ac005", contracts=TASKS["ac005"][4].replace("§Q (>= max min)", "§Q true")),
        detail="Advanced multiple native entry/output predicates missing")
    add("missing-preservation", "ac005", source("ac005", contracts="\n".join(TASKS["ac005"][4].splitlines()[:4])))
    add("wrong-preservation", "ac005", source("ac005", contracts=TASKS["ac005"][4].replace("(== result value)", "(>= result value)")))
    add("wrong-clamp", "ac005", source("ac005", body="§R min"))
    return controls


INFRASTRUCTURE = re.compile(
    r"timeout|timed out|deadline|(?-i:\bEOF\b)|cleanup|Cannot terminate|solver infrastructure|(?-i:UNKNOWN)|"
    r"OutOfMemory|DllNotFound|BadImageFormat|FileNotFoundException|AccessViolation|"
    r"Aborted|segmentation|killed|MSB\d+|Build FAILED|NU\d+|cannot launch|permission denied|"
    r"Selected native manifest|Selected compiler dependency|native manifest changed|image changed or was substituted|"
    r"\[Errno \d+\]|OSError|WinError|UnauthorizedAccessException|IOException|success-shaped failure",
    re.IGNORECASE)


def classify(error, allow_timeout=False):
    output = str(error)
    if re.search(r"EOF supervision|cleanup|Cannot terminate", output, re.IGNORECASE):
        raise error
    if INFRASTRUCTURE.search(output):
        if not (allow_timeout and re.search(r"Verifier timeout", output)
                and not re.search(r"UNKNOWN|solver|EOF|cleanup", output, re.IGNORECASE)):
            raise error
        return output
    match = re.match(r"Verifier command exited (\d+):\n", output)
    if match is None or int(match[1]) != 1 or "Verification failed:" not in output:
        raise error
    if any(int(code) != 1 for code in re.findall(r"Verifier command exited (-?\d+):", output)):
        raise error
    if re.search(r"^\s*Verification passed:", output, re.MULTILINE):
        raise error
    return output


class AdvancedContractTests(unittest.TestCase):
    entry_points_executed = 0
    positives = 0
    negatives = 0
    bounded_timeouts = 0
    lock = threading.Lock()

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real advanced controls: {cls.positives} positive, "
              f"{cls.negatives} finite rejected, {cls.bounded_timeouts} separately declared bounded timeouts.")

    def verify(self, profile, text, expected=True, *, env=None, detail=None, deadline=None,
               allow_timeout=False, real=True):
        with tempfile.TemporaryDirectory(prefix="calor-advanced-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Contracts.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            end = min(deadline or float("inf"), time.monotonic() + 120)
            remaining = end - time.monotonic()
            if remaining <= 0:
                raise RuntimeError("Advanced supervision deadline expired.")
            environment["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(
                float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120")), remaining))
            command = [str(HERE / "tasks/advanced-contracts" / TASKS[profile][0] / "verify.sh"), str(workspace)]
            try:
                output = ENGINE.run(command, HERE, end, env=environment)
                passed = True
            except RuntimeError as error:
                output = classify(error, allow_timeout)
                passed = False
            self.assertEqual(passed, expected, output)
            if expected:
                root = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
                configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
                identity = f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
                identities = [line for line in output.splitlines() if re.search(r"Selected\s+compiler:", line, re.IGNORECASE)]
                self.assertEqual(identities, [identity], output)
                self.assertNotRegex(output, r"(?im)Verification failed:|UNKNOWN|infrastructure failure")
                count, rejections, coupling = COUNTS[profile]
                self.assertIn(f"Verification passed: {TASKS[profile][1]}; advanced native/CLR predicates; "
                              f"{count} runtime cases; {rejections} entry rejections; "
                              f"{coupling} successful array output counterfactuals.", output)
                self.assertIn("Limits:", output)
            if detail:
                self.assertIn(detail, output)
            if real:
                with type(self).lock:
                    type(self).entry_points_executed += 1
                    if expected:
                        type(self).positives += 1
                    elif allow_timeout:
                        type(self).bounded_timeouts += 1
                    else:
                        type(self).negatives += 1
            return output

    def test_native_positives(self):
        for category, profile, text, expected, detail in matrix():
            if expected:
                with self.subTest(profile=profile, category=category):
                    self.verify(profile, text, expected, detail=detail)

    def test_finite_rejections(self):
        for category, profile, text, expected, detail in matrix():
            if not expected:
                with self.subTest(profile=profile, category=category):
                    self.verify(profile, text, expected, detail=detail)

    def test_supervision_and_identity_guards(self):
        before = type(self).entry_points_executed
        errors = [
            "Verifier timeout: overall deadline expired.",
            "Verifier cleanup timeout: inherited pipe did not close.",
            "EOF supervision failure", "Cannot terminate verifier process group 123",
            "Verifier command exited 1:\nVerification failed: Verifier timeout after 2s: dotnet",
            "Verifier command exited 1:\nVerification failed: Advanced solver infrastructure failure: UNKNOWN",
            "Verifier command exited -9:\nVerification failed: killed",
            "Verifier command exited 137:\nVerification failed: abnormal exit",
            "Verifier command exited 1:\nVerification failed: Verifier command exited -9:\nVerification failed: abnormal",
            "Verifier command exited 1:\nVerification failed: Verifier cleanup timeout",
            "Verifier command exited 1:\nVerification failed: System.DllNotFoundException",
            "Verifier command exited 1:\nVerification passed: SafeIncrement",
            "Verifier command exited 1:\nVerification failed: process failed\nVerification passed: SafeIncrement",
        ]
        for error in errors:
            with patch.object(ENGINE, "run", side_effect=RuntimeError(error)):
                with self.assertRaises(RuntimeError):
                    self.verify("ac003", source("ac003"), False, real=False)
        selected = COMPILER_ROOT / "src/Calor.Compiler/bin" / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0/calor.dll"
        identity = f"Selected compiler: {selected}\n"
        success = identity + ("Verification passed: SafeIncrement; advanced native/CLR predicates; "
            "12 runtime cases; 0 entry rejections; 0 successful array output counterfactuals.\nLimits: no universal body proof.\n")
        invalid = [
            success.replace(identity, ""), identity + success, "  " + identity + success,
            "Selected compiler:\t" + str(selected) + "\n" + success,
            success.replace(str(selected), str(selected) + ".other"),
            success.replace("/net10.0/", "/net9.0/"),
            success + "Verification failed: disguised success\n",
        ]
        for output in invalid:
            with patch.object(ENGINE, "run", return_value=output):
                with self.assertRaises(AssertionError):
                    self.verify("ac003", source("ac003"), real=False)
        with patch.object(ENGINE, "run", side_effect=OSError("launch failure")):
            with self.assertRaises(OSError):
                self.verify("ac003", source("ac003"), False, real=False)
        self.assertEqual(before, type(self).entry_points_executed)

    def test_declared_bounded_timeouts(self):
        self.verify("ac003", source("ac003"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout", allow_timeout=True)
        self.verify("ac003", source("ac003", body="§WH true\n  §CN\n§R 0"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "5"}, detail="timeout", allow_timeout=True)

    def test_real_success_shaped_output_is_infrastructure(self):
        for message in ("  Selected compiler: forged", "Verification failed: disguised"):
            text = source("ac003", body='§E{cw}\n§P "' + message + '"\n§R (+ x 1)')
            with self.assertRaisesRegex(RuntimeError, "success-shaped failure"):
                self.verify("ac003", text, False)

    def test_selected_images(self):
        relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        with tempfile.TemporaryDirectory(prefix="calor-advanced-images-") as temporary:
            arm = Path(temporary)
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            shutil.copytree(COMPILER_ROOT / ".github", arm / ".github")
            # An unrelated publish subtree is not selected dependency evidence.
            unrelated = arm / relative / "publish/unused.dll"
            unrelated.parent.mkdir(exist_ok=True)
            unrelated.write_bytes(b"not a selected image")
            self.verify("ac003", source("ac003"), env={"CALOR_COMPILER_ROOT": str(arm)})
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid selected native image")
            # Explicit infrastructure controls are not finite admission negatives.
            for profile in TASKS:
                with self.assertRaisesRegex(RuntimeError, "manifest mismatch"):
                    self.verify(profile, source(profile), False, env={"CALOR_COMPILER_ROOT": str(arm)})

    def test_preserved_profiles(self):
        profiles = [
            ("test-logic-verifiers.py", "LogicVerifierTests", "TASK_NAMES"),
            ("test-basic-syntax-verifiers.py", "BasicSyntaxVerifierTests", "TASK_NAMES"),
            ("test-control-flow-verifiers.py", "ControlFlowVerifierTests", "TASK_NAMES"),
            ("test-remaining-control-flow-verifiers.py", "RemainingControlFlowTests", "TASK_NAMES"),
            ("test-contract-writing-verifiers.py", "ContractWritingVerifierTests", "TASKS"),
            ("test-enum-verifiers.py", "EnumVerifierTests", "TASKS"),
            ("test-idiom-verifiers.py", "IdiomVerifierTests", "TASKS"),
            ("test-generic-verifiers.py", "GenericVerifierTests", "TASKS"),
            ("test-string-verifiers.py", "StringVerifierTests", "TASKS"),
            ("test-collection-verifiers.py", "CollectionVerifierTests", "TASKS"),
            ("test-scalar-pattern-verifiers.py", "ScalarPatternVerifierTests", "TASKS"),
            ("test-oop-verifiers.py", "OopVerifierTests", "TASKS"),
            ("test-carrier-verifiers.py", "CarrierVerifierTests", "TASKS"),
            ("test-type-arithmetic-verifiers.py", "TypeArithmeticVerifierTests", "TASKS"),
            ("test-async-verifiers.py", "AsyncVerifierTests", "TASKS"),
            ("test-effects-verifiers.py", "EffectsVerifierTests", "TASKS"),
        ]
        executed = 0
        for filename, classname, table in profiles:
            module = load("advanced_preserved_" + classname, filename)
            test = getattr(module, classname)()
            for profile in getattr(module, table):
                with self.subTest(preserved=profile):
                    if classname == "ScalarPatternVerifierTests":
                        module.execute(profile, module.source(profile))
                    else:
                        test.verify(profile, module.source(profile))
                    executed += 1
        self.assertEqual(executed, 66)
        print(f"Executed {executed} genuine preserved profile entrypoints (separate from advanced counters).")


def run_compatibility(old, current, deadline):
    # Representative strict matrix keeps every distinct category. Only duplicate
    # per-profile permutations of shared signature/scoping/supervision categories
    # are omitted; every profile retains both bodies/aliases and substantive proof
    # failure categories. Arms remain sequential; helpers have no shared cache.
    controls = matrix()
    shared = {"missing-file", "missing-native-function", "comment-only", "duplicate", "private-signature",
              "wide-signature", "wrong-output", "foreign-contracts", "narrowed-entry", "excluded-output",
              "baseline-error", "raw-body", "wrong-owner", "scoped-refinement", "scoped-unrelated"}
    strict = [item for item in controls if item[0] not in shared or item[1] == "ac001"
              or item[0] in ("scoped-refinement", "scoped-unrelated") and item[1] == "ac004"]
    if {item[0] for item in strict} != {item[0] for item in controls}:
        raise RuntimeError("Advanced strict compatibility omitted a distinct category.")
    if len(strict) != 78 or sum(item[3] for item in strict) != 16:
        raise RuntimeError("Advanced representative strict matrix/count changed.")
    strict.sort(key=lambda item: (item[0] != "native-positive", item[1], item[0]))
    test = AdvancedContractTests()
    before = test.entry_points_executed
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        def execute(item):
            category, profile, text, expected, detail = item
            end = min(deadline, time.monotonic() + 120)
            if time.monotonic() >= end:
                raise RuntimeError("Advanced compatibility exceeded its 600-second total deadline.")
            try:
                test.verify(profile, historical_syntax(text) if historical and text is not None else text,
                            expected, detail=detail, env={"CALOR_COMPILER_ROOT": str(root)}, deadline=end)
            except AssertionError as error:
                if historical and expected and "Calor0200" in str(error) and "Unknown identifier: 'arr.Length'" in str(error):
                    print(f"{arm} {profile} {category}: original array task BLOCKED by native Calor0200.")
                    raise RuntimeError("Original advanced-contract task is blocked in the source-built v0.29 compiler: "
                                       "native array-length postconditions are rejected; no contract-disabled or "
                                       f"retyped positive is admitted.\n{error}") from error
                raise
            return f"{arm} {profile} {category}: {'passed' if expected else 'rejected'}"
        with ThreadPoolExecutor(max_workers=2) as workers:
            # Do not leave a whole queued matrix running after the first blocker.
            for offset in range(0, len(strict), 2):
                for verdict in workers.map(execute, strict[offset:offset + 2]):
                    print(verdict)
    count = test.entry_points_executed - before
    if count != 2 * len(strict):
        raise RuntimeError("Advanced strict real compatibility count mismatch.")
    print(f"Compatibility passed: {count} actual old/current advanced controls; "
          f"{test.positives} positive, {test.negatives} rejected; {len(strict)} per arm; every distinct category retained.")


if __name__ == "__main__":
    unittest.main(verbosity=2)
