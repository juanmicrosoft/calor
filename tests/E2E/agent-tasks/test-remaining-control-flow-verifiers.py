#!/usr/bin/env python3
"""Real cf-005..008 controls. Finite behavior checks are not universal proofs."""

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
TASK_NAMES = {
    "Max": "05_if_arrow", "GradeToLetter": "06_nested_conditionals",
    "PrintEvenNumbers": "07_loop_with_conditional", "PrintDictionary": "08_foreach_dictionary",
}
FILES = {"Max": "Calculator.calr", "GradeToLetter": "Calculator.calr",
         "PrintEvenNumbers": "Effects.calr", "PrintDictionary": "Collections.calr"}
SIGNATURES = {
    "Max": "§F{Max:pub} (i32:a, i32:b) -> i32",
    "GradeToLetter": "§F{GradeToLetter:pub} (i32:grade) -> i32",
    "PrintEvenNumbers": "§F{PrintEvenNumbers:pub} () -> void",
    "PrintDictionary": "§F{PrintDictionary:pub} (Dictionary<str,i32>:items) -> void",
}
GRADE_EXPRESSION = "(? (>= grade 90) 4 (? (>= grade 80) 3 (? (>= grade 70) 2 (? (>= grade 60) 1 0))))"
GRADE_INDEPENDENT = "§R §C{Math.Clamp} §A (- (/ grade 10) 5) §A 0 §A 4 §/C"
BODIES = {
    "Max": "§IF (>= a b)\n  §R a\n§EL\n  §R b",
    "GradeToLetter": (
        "§IF (>= grade 90)\n  §R 4\n§EL\n"
        "  §IF (>= grade 80)\n    §R 3\n  §EL\n"
        "    §IF (>= grade 70)\n      §R 2\n    §EL\n"
        "      §IF (>= grade 60)\n        §R 1\n      §EL\n        §R 0"
    ),
    "PrintEvenNumbers": "§L{number:1:10:1}\n  §IF (== (% number 2) 0)\n    §P number",
    "PrintDictionary": "§EACHKV{key:value} items\n  §P key\n  §P value",
}
CONTRACTS = "§Q (>= grade 0)\n§Q (<= grade 100)"


def source(name, body=None, signature=None, contracts=None, effect="cw"):
    lines = []
    if name == "GradeToLetter":
        lines.extend((CONTRACTS if contracts is None else contracts).splitlines())
    elif contracts:
        lines.extend(contracts.splitlines())
    if name.startswith("Print") and effect is not None:
        lines.append(f"§E{{{effect}}}")
    lines.extend((BODIES[name] if body is None else body).splitlines())
    module = FILES[name].removesuffix(".calr")
    return f"§M{{{module}}}\n  {signature or SIGNATURES[name]}\n" + "".join(
        "    " + line + "\n" for line in lines
    )


def historical_syntax(text):
    specification = importlib.util.spec_from_file_location("cf_controls", HERE / "test-control-flow-verifiers.py")
    previous = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(previous)
    text = previous.historical_syntax(text)
    # Old foreach parsers require an ID position, including deconstructing foreach.
    serial = 0
    lines = []
    for line in text.splitlines():
        if line.lstrip().startswith(("§EACHKV{", "§EACH{")):
            serial += 1
            line = line.replace("{", f"{{e{serial:03d}:", 1)
        lines.append(line)
    return "\n".join(lines) + "\n"


class RemainingControlFlowTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real cf-005..008 verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None):
        with tempfile.TemporaryDirectory(prefix="calor-remaining-cf-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / FILES[name]).write_text(text, encoding="utf-8")
            configuration = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            configuration.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            configuration.update(env or {})
            result = subprocess.run(
                [str(HERE / "tasks/control-flow" / TASK_NAMES[name] / "verify.sh"), str(workspace)],
                env=configuration, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135,
            )
            type(self).entry_points_executed += 1
            self.assertEqual(result.returncode == 0, expected, result.stdout)
            if expected:
                self.assertIn(f"Verification passed: {name};", result.stdout)
                self.assertIn({"Max": "121 runtime cases.", "GradeToLetter": "101 runtime cases.",
                               "PrintEvenNumbers": "exact stdout.", "PrintDictionary": "5 dictionary cases."}[name],
                              result.stdout)
            if detail:
                self.assertIn(detail, result.stdout)
            return result.stdout

    def test_equivalent_native_implementations_and_signatures(self):
        for name in TASK_NAMES:
            self.verify(name, source(name))
            self.verify(name, "// harmless formatting\n" + source(name).replace("i32", "int").replace("str,", "string,"))
        self.verify("Max", source("Max", body="§R (? (> a b) a b)"))
        self.verify("Max", source("Max", body="§R §C{Math.Max} §A a §A b §/C"))
        self.verify("Max", source("Max", contracts="§S (>= result a)\n§S (>= result b)"))
        self.verify("GradeToLetter", source("GradeToLetter", contracts="§Q (&& (<= 0 grade) (>= 100 grade))"))
        self.verify("GradeToLetter", source("GradeToLetter", body=f"§R {GRADE_EXPRESSION}"))
        self.verify("GradeToLetter", source("GradeToLetter", body=f"§B{{category:i32}} {GRADE_EXPRESSION}\n§R category"))
        self.verify("GradeToLetter", source("GradeToLetter", body=(
            "§B{~category:int} 0\n§IF (>= grade 60)\n  §IF (>= grade 90)\n    §ASSIGN category 4\n"
            "  §EI (>= grade 80)\n    §ASSIGN category 3\n  §EI (>= grade 70)\n    §ASSIGN category 2\n"
            "  §EL\n    §ASSIGN category 1\n§R category")))
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", effect="cw,cr").replace(
            "§P number", "§C{Console.WriteLine} §A number §/C"))
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=(
            "§L{number:1:10:1}\n  §IF (! (== (% number 2) 0))\n    §CN\n  §P number")))
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=(
            "§L{number:1:10:1}\n  §IF (== (% number 2) 1)\n    §B{unused:i32} number\n  §EL\n    §P number")))
        self.verify("PrintDictionary", source("PrintDictionary").replace("items", "lookup"))
        self.verify("PrintDictionary", source("PrintDictionary", signature=SIGNATURES["PrintDictionary"].replace(
            "Dictionary<str,i32>", "System.Collections.Generic.Dictionary<string,int>")), False,
                    detail="Calor compilation failed")
        self.verify("PrintDictionary", source("PrintDictionary", body="§EACH{pair} items\n  §P pair.Key\n  §P pair.Value"))
        self.verify("PrintDictionary", source("PrintDictionary", body='§EACHKV{k:v} items\n  §P (+ (+ k ": ") v)'))
        self.verify("PrintDictionary", source("PrintDictionary", body='§EACHKV{k:v} items\n  §P (+ (+ v " = ") k)'))
        self.verify("PrintDictionary", source("PrintDictionary", body=(
            '§EACHKV{k:v} items\n  §P (+ (+ (+ "Key: " k) ", Value: ") v)')))
        self.verify("PrintDictionary", source("PrintDictionary", body=(
            '§EACHKV{k:v} §C{Enumerable.Reverse} §A items §/C\n  §P (+ (+ k ", ") v)')))

    def test_missing_private_comment_only_wrong_signatures(self):
        for name in TASK_NAMES:
            self.verify(name, source(name).replace(f"§F{{{name}:", "§F{Unrelated:"), False)
            self.verify(name, "// " + " ".join(source(name).splitlines()) + "\n§M{Empty}\n", False)
            self.verify(name, source(name, signature=SIGNATURES[name].replace(":pub", ":priv")), False,
                        detail="Wrong public signature")
            self.verify(name, source(name, signature=SIGNATURES[name].replace("-> i32", "-> i64")
                                     if name in ("Max", "GradeToLetter") else SIGNATURES[name].replace("-> void", "-> i32"),
                                     body="§R 0"), False)
        self.verify("Max", source("Max", signature=SIGNATURES["Max"].replace("i32:a", "i64:a")), False)
        self.verify("Max", source("Max").replace(":a", ":x").replace(" a ", " x ").replace("§R a", "§R x"), False)
        self.verify("GradeToLetter", source("GradeToLetter").replace("grade", "mark"), False)
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", signature="§F{PrintEvenNumbers:pub} (i32:x) -> void"), False)
        for generic in ("Dictionary<str,i64>", "Dictionary<i32,i32>", "List<i32>"):
            self.verify("PrintDictionary", source("PrintDictionary", signature=SIGNATURES["PrintDictionary"].replace(
                "Dictionary<str,i32>", generic)), False)

    def test_grade_domain_contracts_and_all_boundaries(self):
        for contracts in ("", "§Q (>= grade 0)", "§Q (<= grade 100)", "§Q true", "§Q false",
                          "§Q (&& (> grade 0) (<= grade 100))", "§Q (&& (>= grade 0) (< grade 100))",
                          "§Q (&& (>= grade -1) (<= grade 101))", CONTRACTS + "\n§Q (! (== grade 37))",
                          CONTRACTS + "\n§S false"):
            self.verify("GradeToLetter", source("GradeToLetter", contracts=contracts), False)
        for threshold in (60, 70, 80, 90):
            self.verify("GradeToLetter", source("GradeToLetter", body=BODIES["GradeToLetter"].replace(
                f"(>= grade {threshold})", f"(> grade {threshold})")), False, detail="expected")
        for grade in (0, 59, 60, 69, 70, 79, 80, 89, 90, 100):
            self.verify("GradeToLetter", source("GradeToLetter", body=(
                f"§IF (== grade {grade})\n  §R 99\n" + BODIES["GradeToLetter"])), False, detail="expected")

    def test_max_signed_boundaries_and_distinct_logic_profile(self):
        for body in ("§R a", "§R b", "§R (- 0 a)", "§R 0"):
            self.verify("Max", source("Max", body=body), False)
        for contracts in ("§Q (> a b)", "§Q false", "§S false"):
            self.verify("Max", source("Max", contracts=contracts), False)
        for value in (-2147483648, -1, 0, 1, 2147483647):
            self.verify("Max", source("Max", body=f"§IF (== a INT:{value})\n  §R 42\n" + BODIES["Max"]),
                        False, detail="expected")
        with tempfile.TemporaryDirectory(prefix="calor-distinct-max-") as temporary:
            (Path(temporary) / "Calculator.calr").write_text(source("Max"), encoding="utf-8")
            result = subprocess.run([str(HERE / "tasks/logic-implementation/04_max/verify.sh"), temporary],
                                    env=dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT)),
                                    text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=135)
            self.assertNotEqual(result.returncode, 0, result.stdout)
            self.assertIn("Missing/weakened postconditions", result.stdout)

    def test_equivalent_inline_grade_domains(self):
        signature = "§F{GradeToLetter:pub} (i32:grade | (&& (>= # 0) (<= # 100))) -> i32"
        self.verify("GradeToLetter", source("GradeToLetter", signature=signature, contracts=""))
        lower_bound = "§F{GradeToLetter:pub} (i32:grade | (>= # 0)) -> i32"
        self.verify("GradeToLetter", source("GradeToLetter", signature=lower_bound, contracts="§Q (<= grade 100)"))
        for predicate in ("(&& (> # 0) (<= # 100))", "(&& (>= # -1) (<= # 101))", "false"):
            invalid = f"§F{{GradeToLetter:pub}} (i32:grade | {predicate}) -> i32"
            self.verify("GradeToLetter", source("GradeToLetter", signature=invalid, contracts=""), False,
                        detail="Missing/wrong preconditions")

    def test_native_constructs_effects_and_coupling(self):
        for name in ("GradeToLetter", "PrintEvenNumbers", "PrintDictionary"):
            independent = GRADE_INDEPENDENT if name == "GradeToLetter" else (
                "§P 2\n§P 4\n§P 6\n§P 8\n§P 10" if name == "PrintEvenNumbers"
                else "§C{Console.Write} §A STR:\"\" §/C")
            unrelated = source(name).splitlines()[1:]
            unrelated[0] = unrelated[0].replace(f"§F{{{name}:", "§F{Other:")
            self.verify(name, source(name, body=independent) + "\n".join(unrelated) + "\n", False,
                        detail="Required native control flow")
            self.verify(name, source(name, body="// " + " ".join(BODIES[name].splitlines()) + "\n" + independent),
                        False, detail="Required native control flow")
            if name.startswith("Print"):
                self.verify(name, source(name, effect=None), False)
                self.verify(name, source(name, effect="cr"), False)
        self.verify("GradeToLetter", source("GradeToLetter", body=GRADE_INDEPENDENT), False)
        for body in (
            "§IF false\n  " + BODIES["GradeToLetter"].replace("\n", "\n  ") + "\n" + GRADE_INDEPENDENT,
            "§IF true\n  §IF (>= grade 60)\n    §B{unused:i32} 1\n" + GRADE_INDEPENDENT,
            f"§B{{unused:i32}} {GRADE_EXPRESSION}\n" + GRADE_INDEPENDENT,
            GRADE_INDEPENDENT.replace("§R ", "§B{actual:i32} ", 1) + f"\n§R (? true actual {GRADE_EXPRESSION})",
        ):
            self.verify("GradeToLetter", source("GradeToLetter", body=body), False,
                        detail="Control-flow coupling failed")
        for body in (
            "§IF false\n  " + BODIES["PrintEvenNumbers"].replace("\n", "\n  ") + "\n§P 2\n§P 4\n§P 6\n§P 8\n§P 10",
            BODIES["PrintEvenNumbers"].replace("§P number", "§B{unused:i32} number") + "\n§P 2\n§P 4\n§P 6\n§P 8\n§P 10",
            "§L{number:1:10:1}\n  §IF false\n    §P number\n  §IF (== (% number 2) 0)\n    §B{unused:i32} number\n"
            "  §C{Console.Write} §A (? (== (% number 2) 0) (+ number \"\\n\") \"\") §/C",
            "§L{number:2:10:2}\n  §IF true\n    §P number",
            "§L{number:1:10:1}\n  §IF (== number 2)\n    §P number\n"
            '  §C{Console.Write} §A (? (&& (== (% number 2) 0) (! (== number 2))) (+ number "\\n") "") §/C',
        ):
            self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=body), False,
                        detail="Control-flow coupling failed")
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=BODIES["PrintEvenNumbers"].replace(
            "§L{number:1:10:1}", "§B{~number:i32} 1\n§WH (<= number 10)") + "\n  §ASSIGN number (+ number 1)"),
                    False, detail="Required native control flow")
        self.verify("PrintDictionary", source("PrintDictionary", body="§IF false\n  " + BODIES["PrintDictionary"].replace(
            "\n", "\n  ")), False)
        for body in (
            "§EACHKV{k:v} items\n  §B{unused:i32} v\n§P items",
            '§EACHKV{k:v} items\n  §IF false\n    §P k\n    §P v',
        ):
            self.verify("PrintDictionary", source("PrintDictionary", body=body), False)
        helper = (
            "  §F{PrintActual:pub} (Dictionary<str,i32>:dictionary) -> void\n"
            "    §E{cw}\n    §EACHKV{k:v} dictionary\n      §P k\n      §P v\n"
        )
        self.verify("PrintDictionary", source("PrintDictionary", body=(
            "§EACHKV{k:v} items\n  §B{unused:i32} v\n"
            "§C{PrintActual} §A items §/C")) + helper, False, detail="Control-flow coupling failed")
        self.verify("PrintDictionary", source("PrintDictionary", body=(
            "§EACHKV{k:v} §C{Enumerable.Take} §A items §A 1 §/C\n"
            "  §C{PrintActual} §A items §/C")) + helper, False,
                    detail="Control-flow coupling failed")
        self.verify("PrintDictionary", source("PrintDictionary", body=(
            "§C{items.Clear} §/C\n" + BODIES["PrintDictionary"])), False)

    def test_exact_even_output_and_dictionary_pairing(self):
        for body in (
            BODIES["PrintEvenNumbers"].replace(":1:10:1", ":1:8:1"),
            BODIES["PrintEvenNumbers"].replace(":1:10:1", ":0:10:1"),
            BODIES["PrintEvenNumbers"].replace("(== (% number 2) 0)", "(== (% number 2) 1)"),
            BODIES["PrintEvenNumbers"].replace("§P number", "§P 2"),
            BODIES["PrintEvenNumbers"] + '\n§P "extra"',
        ):
            self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=body), False, detail="Wrong stdout")
        for body in (
            "§EACHKV{k:v} items\n  §P k",
            "§EACHKV{k:v} items\n  §P v",
            "§EACHKV{k:v} items\n  §P k\n  §P 0",
            "§EACHKV{k:v} items\n  §P k\n  §P v\n  §P k\n  §P v",
            '§EACHKV{k:v} items\n  §P k\n  §P v\n§P "extra"',
            "§EACHKV{k:v} items\n  §IF (! (== v INT:-2147483648))\n    §P k\n    §P v",
            "§EACHKV{k:v} items\n  §P k\n  §P (- 0 v)",
            '§EACHKV{k:v} items\n  §P (+ k "solo")\n  §P v',
        ):
            self.verify("PrintDictionary", source("PrintDictionary", body=body), False, detail="Wrong dictionary output")

    def test_invalid_inputs_compilation_runtime_and_timeout(self):
        for name in TASK_NAMES:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, "", False)
            self.verify(name, "invalid source", False, detail="Calor compilation failed")
            self.verify(name, source(name, body="§WH true\n  §CN\n" + BODIES[name]), False,
                        env={"CALOR_LOGIC_TIMEOUT_SECONDS": "8"}, detail="timeout")
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", body=BODIES["PrintEvenNumbers"]
                                              + "\n§C{Console.WriteLine} §A 1 §A 2 §/C"), False,
                    detail="Generated C# failed compilation")
        self.verify("PrintEvenNumbers", source("PrintEvenNumbers", effect="cw,throw",
                                              body=BODIES["PrintEvenNumbers"] + '\n§TH STR:"runtime failure"'),
                    False, detail="runtime failure")
        self.verify("PrintDictionary", source("PrintDictionary", body="§EACHKV{k:v} items\n  §WH true\n    §P k"),
                    False, detail="Excessive stdout")

    def test_selected_root_configuration_no_fallback(self):
        with tempfile.TemporaryDirectory(prefix="calor-remaining-selected-") as temporary:
            root = Path(temporary)
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            for name in TASK_NAMES:
                self.verify(name, source(name), False, env={"CALOR_COMPILER_ROOT": str(root)},
                            detail="Selected compiler dependency missing")
            shutil.copytree(COMPILER_ROOT / relative, root / relative)
            self.verify("PrintDictionary", source("PrintDictionary"), env={"CALOR_COMPILER_ROOT": str(root)})
            self.verify("Max", source("Max"), False, env={"CALOR_COMPILER_ROOT": ""},
                        detail="not an empty path")
            for configuration in ("", "../Debug", "Debug/../../other"):
                self.verify("PrintDictionary", source("PrintDictionary"), False, env={
                    "CALOR_COMPILER_ROOT": str(root), "CALOR_COMPILER_CONFIGURATION": configuration,
                }, detail="within the selected compiler root")
            self.verify("Max", source("Max"), False, env={"CALOR_COMPILER_ROOT": str(root),
                                                        "CALOR_COMPILER_CONFIGURATION": "Absent"})
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (root / relative / native).unlink()
            self.verify("PrintDictionary", source("PrintDictionary"), False, env={"CALOR_COMPILER_ROOT": str(root)},
                        detail="Selected compiler dependency missing")
            (root / relative / native).write_bytes(b"invalid native asset")
            self.verify("PrintDictionary", source("PrintDictionary"), False, env={"CALOR_COMPILER_ROOT": str(root)})
            (root / relative / "calor.dll").write_bytes(b"invalid assembly")
            self.verify("GradeToLetter", source("GradeToLetter"), False, env={"CALOR_COMPILER_ROOT": str(root)})


def run_compatibility(old, current, deadline):
    test = RemainingControlFlowTests()
    before = RemainingControlFlowTests.entry_points_executed
    for root, historical in ((old, True), (current, False)):
        for name in TASK_NAMES:
            cases = [(source(name), True), (source(name).replace("i32", "int").replace("str,", "string,"), True),
                     (source(name, signature=SIGNATURES[name].replace(":pub", ":priv")), False),
                     (source(name, body="§R 0" if name in ("Max", "GradeToLetter") else ""), False)]
            wrong_signature = (SIGNATURES[name].replace("i32", "i64", 1) if name != "PrintEvenNumbers"
                               else "§F{PrintEvenNumbers:pub} (i32:extra) -> void")
            cases += [(None, False), ("", False), (source(name, signature=wrong_signature), False)]
            if name == "Max":
                cases += [(source(name, body="§R (? (> a b) a b)"), True),
                          (source(name, contracts="§Q (> a b)"), False)]
            elif name == "GradeToLetter":
                inline = "§F{GradeToLetter:pub} (i32:grade | (&& (>= # 0) (<= # 100))) -> i32"
                cases += [(source(name, contracts="§Q (&& (<= 0 grade) (>= 100 grade))"), True),
                          (source(name, contracts="§Q (> grade 0)\n§Q (<= grade 100)"), False),
                          (source(name, body="§IF true\n  §IF false\n    §R 0\n" + GRADE_INDEPENDENT), False),
                          (source(name, body="§R " + GRADE_EXPRESSION), True),
                          (source(name, body=BODIES[name].replace("(>= grade 90)", "(> grade 90)")), False)]
                cases += [(source(name, signature=inline, contracts=""), True),
                          (source(name, signature=inline.replace("(>= # 0)", "(> # 0)"), contracts=""), False),
                          (source(name, signature=inline.replace("(<= # 100)", "(<= # 101)"), contracts=""), False)]
                cases += [(source(name, contracts=contracts), False) for contracts in (
                    "", "§Q false", "§Q (>= grade 0)", CONTRACTS + "\n§Q (! (== grade 37))")]
            elif name == "PrintEvenNumbers":
                cases += [(source(name, effect=None), False),
                          (source(name, body="§L{number:2:10:2}\n  §IF true\n    §P number"), False),
                          (source(name, body=BODIES[name] + "\n§P 12"), False),
                          (source(name, body="§L{number:1:10:1}\n  §IF (== (% number 2) 1)\n    §CN\n  §P number"), True),
                          (source(name, body=BODIES[name].replace("§P number", "§B{unused:i32} number")
                                  + "\n§P 2\n§P 4\n§P 6\n§P 8\n§P 10"), False),
                          (source(name, body='§L{number:1:10:1}\n  §IF (== number 2)\n    §P number\n'
                                  '  §C{Console.Write} §A (? (&& (== (% number 2) 0) (! (== number 2))) (+ number "\\n") "") §/C'), False)]
            else:
                cases += [(source(name).replace("items", "lookup"), True),
                          (source(name, body='§EACHKV{k:v} items\n  §P (+ (+ k ": ") v)'), True),
                          (source(name, body='§EACHKV{k:v} items\n  §P (+ (+ (+ "Key: " k) ", Value: ") v)'), True),
                          (source(name, effect=None), False),
                          (source(name, body="§EACHKV{k:v} items\n  §P k\n  §P 0"), False),
                          (source(name, body="§C{items.Clear} §/C\n" + BODIES[name]), False)]
                helper = (
                    "  §F{PrintActual:pub} (Dictionary<str,i32>:dictionary) -> void\n"
                    "    §E{cw}\n    §EACHKV{k:v} dictionary\n      §P k\n      §P v\n"
                )
                cases += [(source(name, body="§EACHKV{k:v} items\n  §B{unused:i32} v\n"
                                  "§C{PrintActual} §A items §/C") + helper, False)]
            for text, expected in cases:
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Remaining control-flow compatibility deadline expired.")
                test.verify(name, historical_syntax(text) if historical and text is not None else text, expected, env={
                    "CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining),
                })
                print(f"{'v0.29' if historical else 'current'} {name}: {'accepted' if expected else 'rejected'}")
        for body, effect, timeout, detail in (
            ("§WH true\n  §CN\n" + BODIES["PrintEvenNumbers"], "cw", "8", "timeout"),
            (BODIES["PrintEvenNumbers"] + '\n§TH STR:"runtime failure"', "cw,throw", "120", "runtime failure"),
            (BODIES["PrintEvenNumbers"] + "\n§C{Console.WriteLine} §A 1 §A 2 §/C", "cw", "120",
             "Generated C# failed compilation"),
        ):
            text = source("PrintEvenNumbers", body=body, effect=effect)
            remaining = min(float(timeout), deadline - time.monotonic())
            if remaining <= 0:
                raise RuntimeError("Remaining control-flow compatibility deadline expired.")
            test.verify("PrintEvenNumbers", historical_syntax(text) if historical else text, False, env={
                "CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining),
            }, detail=detail)
    test.verify("PrintDictionary", historical_syntax(source("PrintDictionary")), False,
                env={"CALOR_COMPILER_ROOT": str(current)}, detail="Calor compilation failed")
    count = RemainingControlFlowTests.entry_points_executed - before
    if count != 117:
        raise RuntimeError(f"Expected 117 remaining control-flow compatibility entries, executed {count}.")
    print(f"Compatibility passed: {count} actual old/current cf-005..008 entry-point controls.")


if __name__ == "__main__":
    unittest.main()
