#!/usr/bin/env python3
"""Original str-001..005 prompts: real entrypoints, disposable source strings."""

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
TASKS = {
    "str001": ("01_length_upper_lower", "StringLength", "str:s", "i32", "§R (len s)"),
    "str002": ("02_contains_starts_ends", "StringContains", "str:s, str:sub", "bool", "§R (contains s sub)"),
    "str003": ("03_char_operations", "GetFirstChar", "str:s", "char", "§R (char-at s 0)"),
    "str004": ("04_regex_operations", "IsValidEmail", "str:email", "bool", '§R (regex-test email "@")'),
    "str005": ("05_stringbuilder_operations", "BuildGreeting", "str:name", "str",
               '§R (sb-tostring (sb-append (sb-append (sb-append (sb-new) "Hello, ") name) "!"))'),
}
COUNTS = {"str001": 23, "str002": 529, "str003": 22, "str004": 23, "str005": 23}
ALTERNATIVES = {
    "str001": "§B{size:int} (len s)\n§R size",
    "str002": "§B{found:bool} (contains s sub)\n§R found",
    "str003": "§B{first:char} (char-at s INT:0)\n§R first",
    "str004": '§B{pattern:str} "@"\n§R (regex-test email pattern)',
    "str005": '§B{builder} (sb-new)\n§B{first} (sb-append builder "Hello, ")\n'
              '§B{second} (sb-append first name)\n§B{last} (sb-append second "!")\n§R (sb-tostring last)',
}
WRONG = {"str001": "§R 0", "str002": "§R false", "str003": '§R (char-lit "x")',
         "str004": "§R false", "str005": '§R "Hello, !"'}
DECOYS = {
    "str001": '§B{unused:int} (len s)\n§R §C{System.Linq.Enumerable.Count} §A s §/C',
    "str002": "§B{unused:bool} (contains s sub)\n§R (>= §C{s.IndexOf} §A sub §A System.StringComparison.Ordinal §/C 0)",
    "str003": '§B{unused:char} (char-at s 0)\n§R §C{System.Linq.Enumerable.First} §A s §/C',
    "str004": '§B{unused:bool} (regex-test email "@")\n§R (contains email "@")',
    "str005": '§B{unused} (sb-append (sb-append (sb-append (sb-new) "Hello, ") name) "!")\n'
              '§R (+ (+ "Hello, " name) "!")',
}


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def historical_syntax(text):
    return load("string_old_syntax", "test-contract-writing-verifiers.py").historical_syntax(text)


def source(profile, *, body=None, contracts=None, signature=None):
    _, name, parameters, output, original = TASKS[profile]
    required = "§Q (> (len s) 0)" if profile == "str003" else ""
    lines = (required if contracts is None else contracts).splitlines()
    if profile == "str005":
        lines.insert(0, "§E{mut}")
    lines += (original if body is None else body).splitlines()
    declaration = signature or f"§F{{{name}:pub}} ({parameters}) -> {output}"
    return "§M{Calculator}\n  " + declaration + "\n" + "".join("    " + line + "\n" for line in lines)


def cases(profile):
    _, name, parameters, output, _ = TASKS[profile]
    signature = f"§F{{{name}:pub}} ({parameters}) -> {output}"
    equivalent = "§Q (! (<= (len s) 0))" if profile == "str003" else "§Q true"
    unrelated = source(profile).replace("§M{Calculator}\n", "").replace(f"§F{{{name}:", "§F{Decoy:")
    controls = [
        (source(profile), True, None),
        (source(profile, body=ALTERNATIVES[profile], contracts=equivalent), True, None),
        (source(profile, signature=signature.replace("str:", "string:").replace("-> i32", "-> int")), True, None),
        (source(profile, signature=signature.replace(":pub", ":priv")), False, "Wrong public string signature"),
        (source(profile, signature=signature.replace("str:", "i32:", 1), body=WRONG[profile],
                contracts=""), False, "Wrong public string signature"),
        (source(profile, body=WRONG[profile]), False, None),
        (source(profile, contracts="§Q false"), False, "Missing/wrong string preconditions"),
        (source(profile, body=WRONG[profile]) + unrelated, False, None),
        ("// " + source(profile).replace("\n", " ") + "\n§M{Calculator}\n", False, "Expected exactly one"),
        (source(profile) + unrelated.replace("§F{Decoy:", f"§F{{{name}:"), False, None),
        (source(profile, body=DECOYS[profile]), False, "Required native string operation"),
        (source(profile, contracts="§Q (>= (len s) 0)" if profile == "str003" else "§Q true\n§S false"),
         False, "Missing/wrong string preconditions" if profile == "str003" else "String postconditions"),
    ]
    if profile == "str003":
        controls.append((source(profile, signature="§F{GetFirstChar:pub} (str:s | (> (len #) 0)) -> char",
                                contracts=""), True, None))
    return controls


class StringVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real string verify.sh controls.")

    def verify(self, profile, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-string-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/string-operations" / TASKS[profile][0] / "verify.sh"), str(workspace)],
                env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {TASKS[profile][1]}; exact native/CLR signatures, "
                              f"{COUNTS[profile]} runtime cases and native return coupling.", result.stdout)
                self.assertIn("finite non-null CLR UTF-16 cases, not a universal body proof", result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_shared_original_prompt_controls(self):
        for profile in TASKS:
            for text, expected, detail in cases(profile):
                with self.subTest(profile=profile, expected=expected, detail=detail):
                    self.verify(profile, text, expected, detail=detail)

    def test_native_coupling_and_decoys(self):
        for profile in TASKS:
            self.verify(profile, None, False, detail="Input missing")
            self.verify(profile, source(profile, body=""), False)
        self.verify("str002", source("str002", body=DECOYS["str002"]), False, detail="Required native string operation")
        self.verify("str004", source("str004", body=DECOYS["str004"]), False, detail="Required native string operation")
        self.verify("str005", source("str005", body=DECOYS["str005"]), False)
        self.verify("str001", source("str001", body=DECOYS["str001"]),
                    False, detail="Required native string operation")
        self.verify("str003", source("str003", body=DECOYS["str003"]), False, detail="Required native string operation")
        self.verify("str003", source("str003", body='§B{unused:char} (char-at s 0)\n§R (char-lit "x")'), False)
        self.verify("str004", source("str004", body='§R (regex-test email "^.+@.+\\\\..+$")'), False)
        self.verify("str004", source("str004", body='§R (regex-test email "[@]")'),
                    False, detail="Required native string operation")
        self.verify("str003", source("str003", body='§R (char-at s 1)'), False)
        self.verify("str005", source("str005", body=TASKS["str005"][4].replace(' "Hello, "', ' "Hi, "')), False)
        self.verify("str005", source("str005", body=TASKS["str005"][4].replace(' name) "!")', ' "!") name)')), False)
        self.verify("str005", source("str005", body='§B{builder} (sb-new "Hello, ")\n'
                    '§R (sb-tostring (sb-append (sb-append builder name) "!"))'), False)
        self.verify("str005", source("str005", body='§B{dead} (sb-append (sb-new) "Hello, ")\n'
                    '§B{builder} (sb-new "Hello, ")\n§B{a} (sb-append builder name)\n'
                    '§B{b} (sb-append builder "!")\n§R (sb-tostring builder)'), False)
        self.verify("str001", source("str001", body="§R (+ (len s) (- (len s) (len s)))"),
                    False, detail="Ambiguous native string expression mapping")
        self.verify("str001", source("str001", body="§B{l:i32} s.Length\n§B{square:i32} (* l l)\n"
                    "§IF (== l 0)\n  §R 0\n§R (/ square (len s))"), False, detail="counterfactual runtime failed")
        self.verify("str001", source("str001", body="§IF (>= (len s) 0)\n"
                    "  §R §C{System.Linq.Enumerable.Count} §A s §/C\n§R 0"),
                    False, detail="Required native string operation")

    def test_equivalent_native_and_bcl_forms(self):
        self.verify("str001", source("str001", body="§R s.Length"))
        self.verify("str002", source("str002", body="§R §C{s.Contains} §A sub §/C"))
        self.verify("str004", source("str004", body='§R §C{System.Text.RegularExpressions.Regex.IsMatch} §A email §A "@" §/C'))
        self.verify("str005", source("str005", body='§B{b} (sb-new INT:64)\n'
                    '§C{b.Append} §A "Hello, " §/C\n§C{b.Append} §A name §/C\n'
                    '§C{b.Append} §A "!" §/C\n§R §C{b.ToString} §/C'))
        self.verify("str003", source("str003", contracts="§Q (>= (len s) 1)\n§Q true\n§S true"))
        self.verify("str001", source("str001", body="§IF (== (len s) 0)\n  §R 0\n§R (len s)"))
        self.verify("str001", source("str001", body="§B{value:str} s\n§B{alias:str} value\n§R (len alias)"))
        self.verify("str005", source("str005", body='§B{scratch} (sb-append (sb-new) "unrelated")\n'
                    + TASKS["str005"][4]))

    def test_exact_native_and_clr_types(self):
        for profile, (_, name, parameters, _, _) in TASKS.items():
            self.verify(profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters}) -> object"),
                        False, detail="Wrong public string signature")
            self.verify(profile, source(profile, signature=f"§F{{{name}:pub}} ({parameters.replace('str:', '?str:', 1)}) -> "
                        + TASKS[profile][3]), False)

    def test_contract_domain_and_fail_closed_unmodeled_predicates(self):
        for contract in ("", "§Q (>= (len s) 0)", "§Q (> (len s) 1)",
                         "§Q (&& (> (len s) 0) (< (len s) 100))", "§Q false",
                         "§Q (> (len s) 0)\n§S false", "§Q (> (len s) 0)\n§S (== result (char-at s 0))",
                         "§Q (> (+ (len s) 1) 1)"):
            self.verify("str003", source("str003", contracts=contract), False)
        self.verify("str003", source("str003", signature="§F{GetFirstChar:pub} (str:s | (> (len #) 0)) -> char",
                    contracts=""))
        for profile in ("str001", "str002", "str004", "str005"):
            parameter = "s" if profile in ("str001", "str002") else "email" if profile == "str004" else "name"
            self.verify(profile, source(profile, contracts=f"§Q (> (len {parameter}) 0)"), False)
        self.verify("str001", source("str001", contracts="§Q (!= s null)"), False, detail="Calor compilation failed")

    def test_native_selection_and_deadlines(self):
        self.verify("str001", source("str001"), False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout")
        self.verify("str001", source("str001", body="§WH true\n  §CN\n§R (len s)"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout")
        self.verify("str002", source("str002"), False, env={"CALOR_COMPILER_CONFIGURATION": "../Debug"},
                    detail="CALOR_COMPILER_CONFIGURATION")
        with tempfile.TemporaryDirectory(prefix="calor-string-native-arm-") as temporary:
            arm = Path(temporary)
            self.verify("str001", source("str001"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid native asset")
            for profile in TASKS:
                self.verify(profile, source(profile), False, env={"CALOR_COMPILER_ROOT": str(arm)})


def run_compatibility(old, current, deadline):
    test = StringVerifierTests()
    controls = [(profile, text, expected, detail) for profile in TASKS
                for text, expected, detail in cases(profile)]
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for profile, text, expected, detail in controls:
            remaining = min(120, deadline - time.monotonic())
            if remaining <= 0:
                raise RuntimeError("String compatibility exceeded its 600-second deadline.")
            output = test.verify(profile, historical_syntax(text) if historical else text, expected,
                                 env={"CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                                 detail=detail)
            if expected and str(root / "src/Calor.Compiler/bin") not in output:
                raise RuntimeError("String compatibility selected-arm identity mismatch.")
            print(f"{arm} {profile}: {'passed' if expected else 'rejected'}")
    remaining = min(120, deadline - time.monotonic())
    if remaining <= 0:
        raise RuntimeError("String compatibility exceeded its 600-second deadline.")
    test.verify("str003", historical_syntax(source("str003")), False,
                env={"CALOR_COMPILER_ROOT": str(current), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                detail="Calor compilation failed")
    if StringVerifierTests.entry_points_executed != 123:
        raise RuntimeError("Expected exactly 123 actual old/current string entrypoint controls.")
    print("Compatibility passed: 123 actual old/current string entry-point controls.")


if __name__ == "__main__":
    unittest.main()
