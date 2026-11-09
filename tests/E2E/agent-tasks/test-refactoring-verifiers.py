#!/usr/bin/env python3
"""Finite actual original ref-001..008 controls; no protected fixture edits."""

import importlib.util
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(HERE.parents[2]))).resolve()


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ADMISSION = load("refactoring_admission", HERE / "test-carrier-pattern-verifiers.py")
EFFECTS = load("refactoring_effects", HERE / "test-effects-verifiers.py")
PROCESS = ADMISSION.PROCESS
TASKS = {
    "ref001": ("01_extract_pure_function", "DoubleValue", "Impure.calr", "refactoring-impure"),
    "ref002": ("02_add_missing_effects", "ProcessAndLog", "Effects.calr", "effects-project"),
    "ref003": ("03_remove_unnecessary_effects", "JustCompute", "Impure.calr", "refactoring-impure"),
    "ref004": ("04_strengthen_postcondition", "AbsoluteValue", "Calculator.calr", "basic-calor-project"),
    "ref005": ("05_weaken_precondition", "SafeMultiply", "Calculator.calr", "basic-calor-project"),
    "ref006": ("06_fix_contract_violation", "Abs", "BuggyContracts.calr", "buggy-contracts"),
    "ref007": ("07_add_contracts_to_uncontracted", "NoContractAdd", "Impure.calr", "refactoring-impure"),
    "ref008": ("08_csharp_to_calor", "TryParseDigit", "Calculator.calr", "basic-calor-project"),
}
CONTRACTS = {
    "ref004": "§S (>= result 0)\n§S (-> (>= x 0) (== result x))\n§S (-> (< x 0) (== result (- 0 x)))",
    "ref005": "§S (-> (&& (> a 0) (> b 0)) (> result 0))\n§S (-> (&& (< a 0) (< b 0)) (> result 0))",
    "ref006": "§S (>= result 0)",
    "ref007": "§Q (>= x -1000000)\n§Q (<= x 1000000)\n§Q (>= y -1000000)\n§Q (<= y 1000000)\n§S (== result (+ x y))",
}
COUNTS = {"ref001": 21, "ref002": 128, "ref003": 541, "ref004": 24,
          "ref005": 313, "ref006": 24, "ref007": 289, "ref008": 65536}
EXECUTED = {"accepted": 0, "rejected": 0, "prerequisite": 0}


def declaration(name, parameters, body, contracts="", effects=""):
    lines = ([effects] if effects else []) + contracts.splitlines() + body.splitlines()
    return f"  §F{{{name}:pub}} ({parameters}) -> i32\n" + "".join("    " + line + "\n" for line in lines)


def source(profile, *, body=None, contracts=None, alternative=False):
    _, name, filename, fixture = TASKS[profile]
    text = (HERE / "fixtures" / fixture / filename).read_text(encoding="utf-8")
    if profile == "ref001":
        text = text.replace("§R (* x 2)", "§R §C{DoubleValue} §A x §/C", 1)
        return text + declaration(name, "i32:x", body or ("§B{twice:i32} (+ x x)\n§R twice" if alternative else "§R (* x 2)"))
    if profile == "ref002":
        return text + ("  §F{ProcessAndLog:pub} (str:path) -> void\n    §E{fs:r,cw}\n"
                       "    §B{content:str} §C{File.ReadAllText} §A path §/C\n"
                       "    §C{Console.WriteLine} §A content §/C\n")
    if profile == "ref003":
        return text + declaration(name, "i32:a, i32:b",
                                  body or ("§B{sum:int} (+ b a)\n§R sum" if alternative else "§R (+ a b)"), effects="§E{}")
    if profile in ("ref004", "ref005", "ref006", "ref007"):
        required = CONTRACTS[profile] if contracts is None else contracts
        parameters = "i32:a, i32:b" if profile == "ref005" else "i32:x, i32:y" if profile == "ref007" else "i32:x"
        computation = ("§IF (< x 0)\n  §R (- 0 x)\n§R x" if alternative else "§R (? (< x 0) (- 0 x) x)") \
            if profile in ("ref004", "ref006") else "§R (* a b)" if profile == "ref005" else "§R (+ x y)"
        if alternative and profile in ("ref005", "ref007"):
            computation = "§B{answer:int} " + ("(* b a)" if profile == "ref005" else "(+ y x)") + "\n§R answer"
        replacement = declaration(name, parameters, body or computation, required)
        if profile in ("ref006", "ref007"):
            start = text.index("  §F{" + name + ":")
            return text[:start] + replacement
        return text + replacement
    return text + """  §CL{Option:pub}<T>
    §FLD{bool:IsSome:pub}
    §FLD{?T:Value:pub}
    §PROP{IsNone:bool:pub}
      §GET
        §R (! IsSome)
  §F{Some:priv} (i32:value) -> Calculator.Option<i32>
    §E{alloc,mut}
    §B{answer} §NEW{Option<i32>}
    §ASSIGN answer.IsSome true
    §ASSIGN answer.Value value
    §R answer
  §F{None:priv} () -> Calculator.Option<i32>
    §E{alloc}
    §R §NEW{Option<i32>}
  §F{TryParseDigit:pub} (char:c) -> Calculator.Option<i32>
    §E{alloc,mut}
    §IF (&& (>= c '0') (<= c '9'))
      §R §C{Some} §A (- (cast i32 c) (cast i32 '0')) §/C
    §R §C{None} §/C
"""


def historical(text):
    if text is None:
        return None
    lines = []
    for index, line in enumerate(text.splitlines()):
        if line.lstrip().startswith(("§M{", "§F{", "§CL{", "§MT{", "§PROP{")):
            line = line.replace("{", f"{{rf{index}:", 1)
        if line.lstrip().startswith("§IF "):
            line = line.replace("§IF ", f"§IF{{rfif{index}}} ", 1)
        if line.lstrip().startswith("§WH "):
            line = line.replace("§WH ", f"§WH{{rfwh{index}}} ", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


def builtin_digit():
    text = (HERE / "fixtures/basic-calor-project/Calculator.calr").read_text(encoding="utf-8")
    return text + """  §F{TryParseDigit:pub} (char:c) -> Option<i32>
    §IF (&& (>= c '0') (<= c '9'))
      §R §SM (- (cast i32 c) (cast i32 '0'))
    §R §NN{i32}
"""


REJECTIONS = {
    "missing": ("Input missing:",),
    "wrong-name": ("Expected exactly one native refactoring function", "Expected exactly one native module function"),
    "wrong-signature": ("Wrong native refactoring signature", "Wrong public effects signature",
                        "Wrong native Option<int> digit-conversion signature"),
    "wrong-body": ("Wrong refactoring baseline behavior", "Refactoring runtime failed",
                   "Wrong instrumented effects baseline", "Effects BCL provenance",
                   "Wrong digit baseline", "Digit runtime failed"),
    "missing-contract": ("Missing/weakened writing postconditions", "Missing/wrong writing preconditions"),
    "weak-contract": ("Missing/weakened writing postconditions", "Missing/wrong writing preconditions"),
    "restrictive-contract": ("Writing postconditions exclude a required output", "Missing/wrong writing preconditions",
                             "SafeMultiply must not declare preconditions"),
    "pure-declaration": ("must be pure",),
    "pure-execution": ("Wrong refactoring baseline behavior", "Calor0410"),
    "missing-effects": ("Original declared effects missing", "Original LogAndDouble console-write declaration missing"),
    "dead-helper": ("DoubleValue call coupling failed",),
    "dead-read": ("Effects coupling failed: ReadAllText",),
    "missing-write": ("Required resolved BCL effects operation missing",),
    "foreign-contract": ("Missing/weakened writing postconditions", "Missing/wrong writing preconditions"),
    "wrong-zero": ("Writing postconditions exclude a required output",),
    "wrong-negative-bound": ("Missing/wrong writing preconditions",),
    "wrong-positive-bound": ("Missing/wrong writing preconditions",),
    "not-implication": ("SafeMultiply requires native implication",),
    "not-multiple": ("AbsoluteValue requires multiple native postconditions",),
    "wrong-carrier": ("Wrong native Option<int> digit-conversion signature", "Calor0210", "CS0029"),
    "wrong-tag": ("Wrong digit baseline", "Contradictory Option case"),
    "dead-factory": ("Option Some factory coupling failed", "Option None factory coupling failed"),
    "wrong-factory": ("Wrong typed Option Some/None factory API", "Missing native Option"),
    "contradictory-api": ("Contradictory Option case observations",),
    "raw-interop": ("Raw interop cannot establish",),
    "invalid-source": ("Calor compilation failed",),
    "historical-ids": ("Calor0100 Invalid header field 'BuggyContracts' in '§M'; expected overflow=checked or overflow=unchecked.",),
}


def controls(profile):
    text = source(profile)
    name = TASKS[profile][1]
    yield "original-requirement", text, True
    if profile not in ("ref002", "ref008"):
        yield "alternative", source(profile, alternative=True), True
    yield "aliases", text.replace("i32", "int").replace("str:", "string:"), True
    yield "missing", None, False
    yield "wrong-name", text.replace(f"§F{{{name}:pub}}", f"§F{{OtherTarget:pub}}"), False
    yield "wrong-signature", text.replace("(char:c)", "(i32:c)") if profile == "ref008" \
        else text.replace("(str:path)", "(i32:path)") if profile == "ref002" \
        else text.replace(f"§F{{{name}:pub}} (i32:", f"§F{{{name}:pub}} (i64:"), False
    if profile in ("ref001", "ref003", "ref004", "ref005", "ref006", "ref007"):
        yield "wrong-body", source(profile, body="§R 0"), False
    if profile in CONTRACTS:
        yield "missing-contract", source(profile, contracts=""), False
        yield "weak-contract", source(profile, contracts=CONTRACTS[profile].replace(
            "(>= result 0)", "(>= result -1)").replace("(== result x)", "(>= result 0)").replace(
            "(== result (- 0 x))", "(>= result 0)").replace("(< b 0)", "(< b -1)").replace(
            "(<= y 1000000)", "(<= y 1000001)")), False
        yield "restrictive-contract", source(profile, contracts=CONTRACTS[profile] + "\n§Q (!= " +
              ("a" if profile == "ref005" else "x") + " 17)"), False
        unrelated = declaration("Unrelated", "i32:a, i32:b" if profile == "ref005"
                                else "i32:x, i32:y" if profile == "ref007" else "i32:x",
                                "§R 0", CONTRACTS[profile])
        yield "foreign-contract", source(profile, contracts="") + unrelated, False
    if profile in ("ref001", "ref003"):
        yield "pure-declaration", text.replace(f"§F{{{name}:pub}} ", f"§F{{{name}:pub}} ").replace(
            "    §R (* x 2)" if profile == "ref001" else "    §E{}",
            "    §E{cw}\n    §R (* x 2)" if profile == "ref001" else "    §E{cw}"), False
        yield "pure-execution", source(profile, body='§C{Console.WriteLine} §A "impure" §/C\n'
                                      + ("§R (* x 2)" if profile == "ref001" else "§R (+ a b)")), False
    if profile == "ref001":
        yield "missing-effects", text.replace("    §E{cw}\n", "", 1), False
        yield "dead-helper", text.replace("§R §C{DoubleValue} §A x §/C",
              "§B{unused:i32} §C{DoubleValue} §A x §/C\n    §R (* x 2)"), False
    if profile == "ref002":
        yield "missing-effects", text.replace("§E{fs:r,cw}", "§E{cw}"), False
        yield "wrong-body", text.replace("§A content", '§A "content"'), False
        yield "dead-read", text.replace("    §C{Console.WriteLine} §A content §/C",
              "    §B{bytes:byte[]} §C{System.IO.File.ReadAllBytes} §A path §/C\n"
              "    §B{decoded:str} §C{Decode} §A bytes §/C\n"
              "    §C{Console.WriteLine} §A decoded §/C") + EFFECTS.DECODE, False
        yield "missing-write", text.replace("    §C{Console.WriteLine} §A content §/C\n", ""), False
    if profile == "ref004":
        yield "not-multiple", source(profile, contracts="§S (&& (>= result 0) (&& "
              "(-> (>= x 0) (== result x)) (-> (< x 0) (== result (- 0 x)))))"), False
    if profile == "ref005":
        yield "not-implication", source(profile, contracts="§S (|| (! (&& (> a 0) (> b 0))) (> result 0))\n"
              "§S (|| (! (&& (< a 0) (< b 0))) (> result 0))"), False
    if profile == "ref006":
        yield "wrong-zero", source(profile, contracts="§S (> result 0)"), False
    if profile == "ref007":
        yield "wrong-negative-bound", source(profile, contracts=CONTRACTS[profile].replace("(>= x -1000000)", "(>= x -999999)")), False
        yield "wrong-positive-bound", source(profile, contracts=CONTRACTS[profile].replace("(<= y 1000000)", "(<= y 999999)")), False
    if profile == "ref008":
        yield "wrong-body", text.replace("(- (cast i32 c) (cast i32 '0'))", "7"), False
        yield "wrong-tag", text.replace("(>= c '0')", "(> c '0')"), False
        yield "wrong-carrier", text.replace("-> Calculator.Option<i32>", "-> ?i32"), False
        yield "dead-factory", text.replace("    §R §C{None} §/C",
              "    §B{ignored} §C{None} §/C\n    §R §NEW{Option<i32>}"), False
        yield "wrong-factory", text.replace("§F{Some:priv}", "§F{SomeOther:priv}").replace("§C{Some}", "§C{SomeOther}"), False
        yield "contradictory-api", text.replace("§R (! IsSome)", "§R IsSome"), False
    yield "raw-interop", text + "  §CSHARP{public class Decoy { }}§/CSHARP\n", False


def execute(profile, category, text, expected, root=ROOT, deadline=None):
    deadline = min(time.monotonic() + 120, deadline or float("inf"))
    with tempfile.TemporaryDirectory(prefix="calor-refactoring-control-") as temporary:
        workspace = Path(temporary)
        if text is not None:
            (workspace / TASKS[profile][2]).write_text(text, encoding="utf-8")
        environment = dict(os.environ, CALOR_COMPILER_ROOT=str(root),
                           CALOR_LOGIC_TIMEOUT_SECONDS=str(min(120, deadline - time.monotonic())))
        command = ["bash", str(HERE / "tasks/refactoring" / TASKS[profile][0] / "verify.sh"), str(workspace)]
        try:
            output = PROCESS.run(command, HERE, deadline, environment)
        except RuntimeError as error:
            if expected:
                raise
            output = ADMISSION.control_failure(error)
            if category not in REJECTIONS or not any(marker in output for marker in REJECTIONS[category]):
                raise RuntimeError(f"Unclassified {profile}/{category} rejection:\n{output}") from error
            EXECUTED["rejected"] += 1
            print(f"{profile}/{category}: rejected")
            return output
        if not expected:
            raise AssertionError(f"{profile}/{category}: unexpected acceptance:\n{output}")
        ADMISSION.assert_selected_compiler(output, environment)
        verdicts = [line for line in output.splitlines() if ADMISSION.SUCCESS.search(line)]
        if len(verdicts) != 1 or not verdicts[0].startswith(f"Verification passed: {TASKS[profile][1]};"):
            raise AssertionError(f"Missing unique target verdict:\n{output}")
        if f"{COUNTS[profile]} independent" not in output and f"{COUNTS[profile]} runtime cases" not in output:
            raise AssertionError(f"Wrong complete case count:\n{output}")
        if "Limits:" not in output or ("coupling" not in output if profile == "ref002" else "counterfactuals" not in output):
            raise AssertionError(f"Incomplete behavioral/coupling evidence:\n{output}")
        EXECUTED["accepted"] += 1
        print(f"{profile}/{category}: {verdicts[0]}")
        return output


def run_compatibility(old, current, deadline):
    before = dict(EXECUTED)
    # Exercise the archived compiler's distinct inferred generic factory
    # emission before the representative matrix, not after its budget is spent.
    execute("ref008", "builtin-native", historical(builtin_digit()), True, old, deadline)
    representatives = []
    seen = set()
    for profile in TASKS:
        for category, text, expected in controls(profile):
            if expected and category == "aliases" and profile not in ("ref002", "ref008"):
                continue
            if expected and category == "alternative":
                category = "alternative-aliases"
                text = text.replace("i32", "int")
            if expected or category not in seen:
                representatives.append((profile, category, text, expected))
                if not expected:
                    seen.add(category)
    for label, root in (("v0.29", old), ("current", current)):
        for profile, category, text, expected in representatives:
            try:
                execute(profile, category, historical(text) if label == "v0.29" else text, expected, root, deadline)
            except Exception as error:
                raise RuntimeError(f"{label}/{profile}/{category}: {error}") from error
        print(f"{label}: all eight original requirements and distinct adversarial categories executed.")
    # A legacy module header is a deliberate syntax rejection. Adding old
    # function headers too produces secondary "Unknown visibility" diagnostics,
    # which correctly cannot be admitted as finite rejection evidence.
    legacy_module = source("ref006").replace("§M{BuggyContracts}", "§M{rf0:BuggyContracts}", 1)
    execute("ref006", "historical-ids", legacy_module, False, current, deadline)
    accepted = 2 * sum(control[3] for control in representatives) + 1
    rejected = 2 * sum(not control[3] for control in representatives) + 1
    if EXECUTED != dict(accepted=before["accepted"] + accepted,
                        rejected=before["rejected"] + rejected, prerequisite=before["prerequisite"]):
        raise AssertionError("Incomplete actual refactoring compatibility census.")
    if (accepted, rejected) != (33, 51):
        raise AssertionError("Changed strict refactoring representative census.")
    print(f"Compatibility passed: {accepted + rejected} actual old/current refactoring entrypoints "
          f"({accepted} accepted, {rejected} rejected; mocks contribute 0 actual entries).")


class RefactoringAdmissionTests(unittest.TestCase):
    def test_mock_failures_contribute_zero_actual_entries(self):
        before = dict(EXECUTED)
        failures = [
            "Verifier command exited 2:\nVerification failed: Wrong refactoring baseline behavior",
            "Verifier command exited 1:\nVerifier command exited 2:\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nVerifier command exited 11:\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nVerification passed: forged\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nUNKNOWN\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nOSError\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nEOFError\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nZ3Exception\nWrong refactoring baseline behavior",
            "Verifier command exited 1:\nVerifier timeout after 120s: dotnet",
            "Verifier command exited 1:\nUnclassified body failure",
        ]
        for failure in failures:
            with self.subTest(failure=failure), patch.object(PROCESS, "run", side_effect=RuntimeError(failure)):
                with self.assertRaises(RuntimeError):
                    execute("ref003", "wrong-body", source("ref003", body="§R 0"), False)
        self.assertEqual(EXECUTED, before)
        ADMISSION.failure_structure(RuntimeError(
            "Verifier command exited 1:\nVerifier command exited 1:\nVerification failed: Wrong body"))

    def test_positive_identity_and_failure_contamination(self):
        environment = dict(CALOR_COMPILER_ROOT=str(ROOT), CALOR_COMPILER_CONFIGURATION="Debug")
        identity = f"Selected compiler: {ROOT / 'src/Calor.Compiler/bin/Debug/net10.0/calor.dll'}"
        ADMISSION.assert_selected_compiler(identity, environment)
        for output in (identity + "\n" + identity, identity + ".backup", identity.lower(),
                       identity + "\nUNKNOWN", identity + "\nEOF error", identity + "\nZ3Exception",
                       identity + "\nVerifier command exited 1:", identity + "\nVerification failed: bad"):
            with self.subTest(output=output), self.assertRaises(AssertionError):
                ADMISSION.assert_selected_compiler(output, environment)


def prerequisite_controls(deadline):
    with tempfile.TemporaryDirectory(prefix="calor-refactoring-prerequisite-") as temporary:
        arm = Path(temporary).resolve()
        workspace = arm / "input"
        workspace.mkdir()
        (workspace / "Impure.calr").write_text(source("ref003"), encoding="utf-8")
        command = ["bash", str(HERE / "tasks/refactoring" / TASKS["ref003"][0] / "verify.sh"), str(workspace)]
        configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
        relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"

        def reject(changes, refusal=None, *, explicit_timeout=False):
            if time.monotonic() >= deadline:
                raise RuntimeError("Refactoring prerequisite total deadline expired.")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(ROOT),
                               CALOR_LOGIC_TIMEOUT_SECONDS=str(min(120, deadline - time.monotonic())))
            environment.update(changes)
            # This check admits only the exact declared refusal; infrastructure
            # accompanying it still invalidates the control, never a body result.
            ADMISSION.reject_prerequisite(command, environment, refusal, explicit_timeout=explicit_timeout)
            EXECUTED["prerequisite"] += 1

        reject({"CALOR_COMPILER_ROOT": ""},
               "Verification failed: CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path.")
        reject({"CALOR_COMPILER_CONFIGURATION": "../Release"},
               "Verification failed: CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root.")
        reject({"CALOR_COMPILER_ROOT": str(arm)},
               f"Verification failed: Selected compiler dependency missing: {arm / relative / 'calor.dll'}; build the selected root first.")
        reject({"CALOR_LOGIC_TIMEOUT_SECONDS": "121"},
               "Verification failed: CALOR_LOGIC_TIMEOUT_SECONDS must be in (0, 120].")
        reject({"CALOR_LOGIC_TIMEOUT_SECONDS": "0.5"}, explicit_timeout=True)
        shutil.copytree(ROOT / relative, arm / relative)
        (arm / ".github").mkdir()
        manifest = ".github/z3-binaries-4.15.7.sha256"
        shutil.copyfile(ROOT / manifest, arm / manifest)
        native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
        for filename in ("Calor.Runtime.dll", "Microsoft.Z3.dll", native):
            image = arm / relative / filename
            original = image.read_bytes()
            image.unlink()
            reject({"CALOR_COMPILER_ROOT": str(arm)},
                   f"Verification failed: Selected compiler dependency missing: {image}; build the selected root first.")
            image.write_bytes(original)
        for filename in ("Microsoft.Z3.dll", native):
            image = arm / relative / filename
            original = image.read_bytes()
            image.write_bytes(b"corrupt prerequisite")
            reject({"CALOR_COMPILER_ROOT": str(arm)},
                   f"Verification failed: Selected native manifest mismatch: {image}")
            image.write_bytes(original)
    print("Prerequisites passed: 10 actual explicit selection/native/deadline refusals; 0 behavioral entries.")


def main():
    if sys.argv[1:] == ["--admission"]:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(RefactoringAdmissionTests)
        if not unittest.TextTestRunner().run(suite).wasSuccessful():
            raise RuntimeError("Refactoring admission unit controls failed.")
        print("Admission passed: mocks contribute 0 actual entries.")
        return
    deadline = time.monotonic() + 600
    if sys.argv[1:] == ["--prerequisites"]:
        prerequisite_controls(deadline)
        if EXECUTED != {"accepted": 0, "rejected": 0, "prerequisite": 10}:
            raise AssertionError("Incomplete actual prerequisite census.")
        return
    if sys.argv[1:] == ["--smoke"]:
        for profile in TASKS:
            execute(profile, "original-requirement", source(profile), True, deadline=deadline)
        return
    if sys.argv[1:]:
        raise RuntimeError("Usage: test-refactoring-verifiers.py [--admission|--prerequisites|--smoke]")
    for profile in TASKS:
        for category, text, expected in controls(profile):
            execute(profile, category, text, expected, deadline=deadline)
    if EXECUTED != {"accepted": 22, "rejected": 75, "prerequisite": 0}:
        raise AssertionError(f"Incomplete actual current refactoring census: {EXECUTED}")
    print(f"Current controls passed: {EXECUTED}; mocks contribute 0 actual entries.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, AssertionError) as error:
        print(f"Refactoring controls failed: {error}", file=sys.stderr)
        sys.exit(1)
