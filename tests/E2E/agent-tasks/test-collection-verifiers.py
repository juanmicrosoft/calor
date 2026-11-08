#!/usr/bin/env python3
"""Six collection prerequisites: real entrypoints, disposable inputs, no agents."""

import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {
    "SumList": "01_list_creation", "CreateScores": "02_dictionary_creation",
    "UniqueCount": "03_hashset_creation", "BuildCollection": "04_push_put_operations",
    "HasValue": "05_contains_check", "GetListLength": "06_collection_count",
}
BODIES = {
    "SumList": "§B{items:List<i32>} §NEW{List<i32>}\n§PUSH{items} 10\n§PUSH{items} 20\n§PUSH{items} 30\n"
               "§B{~sum:i32} 0\n§EACH{item} items\n  §ASSIGN sum (+ sum item)\n§R sum",
    "CreateScores": '§B{scores:Dictionary<str,i32>} §NEW{Dictionary<str,i32>}\n§PUT{scores} "Alice" 95\n§PUT{scores} "Bob" 87\n'
                    '§PUT{scores} "Carol" 92\n§R §CNT{scores}',
    "UniqueCount": "§B{items:HashSet<i32>} §NEW{HashSet<i32>}\n" + "".join(f"§PUSH{{items}} {value}\n" for value in [1, 2, 2, 3, 3, 3])
                   + "§R §CNT{items}",
    "BuildCollection": '§B{items:List<i32>} §NEW{List<i32>}\n§PUSH{items} 1\n§PUSH{items} 2\n'
                       '§B{words:Dictionary<i32,str>} §NEW{Dictionary<i32,str>}\n'
                       '§PUT{words} 1 "one"\n§PUT{words} 2 "two"\n§PUT{words} 3 "three"\n'
                       '§R (+ §CNT{items} §CNT{words})',
    "HasValue": "§B{items:List<i32>} §NEW{List<i32>} §A §C{System.Linq.Enumerable.Select} "
                "§A §C{System.Linq.Enumerable.Range} §A 1 §A 3 §/C "
                "§A §LAM{x:i32} (* x 10) §/LAM §/C\n§R §HAS{items} value",
    "GetListLength": "§B{items:List<i32>} §NEW{List<i32>}\n§L{i:1:n:1}\n  §PUSH{items} i\n§R §CNT{items}",
}
COUNTS = {"HasValue": (21, 21), "GetListLength": (11, 11), "BuildCollection": (1, 2)}
PARAMETERS = {"HasValue": "i32:value", "GetListLength": "i32:n"}
CONTRACTS = {"GetListLength": "§Q (>= n 0)"}
RETURNS = {"HasValue": "bool"}
EXPECTED = {"SumList": "60", "CreateScores": "3", "UniqueCount": "3", "BuildCollection": "5",
            "HasValue": "(|| (== value 10) (|| (== value 20) (== value 30)))", "GetListLength": "n"}


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def source(name, *, body=None, contracts=None, signature=None, extra=""):
    signature = signature or f"§F{{{name}:pub}} ({PARAMETERS.get(name, '')}) -> {RETURNS.get(name, 'i32')}"
    lines = ["§E{alloc,mut}"] + (CONTRACTS.get(name, "") if contracts is None else contracts).splitlines()
    lines += (BODIES[name] if body is None else body).splitlines()
    return "§M{Collections}\n  " + signature + "\n" + "".join("    " + line + "\n" for line in lines) + extra


def alternative(name):
    """BCL constructors/Add/Count, helper returns and same-object aliases."""
    body = BODIES[name]
    if name == "HasValue":
        body = body.replace("§R §HAS{items} value", "§B{alias:List<i32>} items\n§R §C{Has} §A alias §A value §/C")
        return source(name, body=body, extra="  §F{Has:priv} (List<i32>:xs, i32:x) -> bool\n"
                      "    §R §C{xs.Contains} §A x §/C\n")
    lines = []
    for line in body.splitlines():
        stripped = line.lstrip()
        indent = line[:len(line) - len(stripped)]
        if stripped.startswith(("§PUSH{", "§PUT{")):
            target, values = stripped.split("}", 1)
            target = target.split("{")[1]
            arguments = values.strip().split()
            lines.append(indent + f"§C{{{target}.Add}} " + " ".join("§A " + value for value in arguments) + " §/C")
        else:
            lines.append(line)
    body = "\n".join(lines)
    for target in ("items", "scores", "words"):
        body = body.replace(f"§CNT{{{target}}}", f"{target}.Count")
    if name == "GetListLength":
        body = body.replace("§L{i:1:n:1}\n  §C{items.Add} §A i §/C",
                            "§B{~i:i32} 1\n§WH (<= i n)\n  §C{items.Add} §A i §/C\n  §ASSIGN i (+ i 1)")
    if name == "SumList":
        body = body.replace("§EACH{item} items\n  §ASSIGN sum (+ sum item)",
                            "§B{~i:i32} 0\n§WH (< i items.Count)\n"
                            "  §ASSIGN sum (+ sum §IDX{items} i)\n  §ASSIGN i (+ i 1)")
    return source(name, body=body)


def shared_cases(name):
    original = BODIES[name]
    prefix = original.rsplit("§R ", 1)[0]
    dead = prefix + ("" if name == "SumList" else
                     f"§B{{unused:{RETURNS.get(name, 'i32')}}} " + original.rsplit("§R ", 1)[1] + "\n")
    wrong = {
        "SumList": original.replace("§PUSH{items} 30", "§PUSH{items} 31").replace("§R sum", "§R (- sum 1)"),
        "CreateScores": original.replace('"Bob" 87', '"Bob" 88'),
        "UniqueCount": original.replace("§PUSH{items} 2\n", "", 1),
        "BuildCollection": original.replace('3 "three"', '3 "wrong"'),
        "HasValue": original.replace("(* x 10)", "(+ (* x 10) 1)").replace("§R §HAS{items} value", "§R " + EXPECTED[name]),
        "GetListLength": original.replace("§PUSH{items} i", "§PUSH{items} n"),
    }
    result = [
        (source(name), True, None),
        (alternative(name), True, None),
        (source(name).replace("i32", "int"), True, None),
        (source(name, body=dead + "§R " + EXPECTED[name]), False, "coupling"),
        (source(name, body=wrong[name]), False, "provenance"),
        (source(name, signature=f"§F{{{name}:priv}} ({PARAMETERS.get(name, '')}) -> {RETURNS.get(name, 'i32')}"),
         False, "Wrong public signature"),
        (source(name, body="§R " + EXPECTED[name]), False, "provenance"),
    ]
    if name == "GetListLength":
        result += [(source(name, contracts=predicate), False, "writing preconditions")
                   for predicate in ("", "§Q (>= n 0)\n§Q (<= n 257)", "§Q (> n 0)")]
        result.append((source(name, signature="§F{GetListLength:pub} (i32:n | (>= # 0)) -> i32", contracts=""), True, None))
    elif name == "HasValue":
        result.append((source(name, contracts="§Q (>= value 0)"), False, "writing preconditions"))
        result.append((source(name, body=BODIES["SumList"].split("§B{~sum")[0] + "§R §HAS{items} value"), True, None))
        for count in (1, 2):
            body = original.replace("§A 1 §A 3 §/C", f"§A 1 §A {count} §/C").replace(
                "§R §HAS{items} value",
                "".join(f"§PUSH{{items}} {value}\n" for value in (10, 20, 30)[count:]) + "§R §HAS{items} value")
            result.append((source(name, body=body), True, None))
    else:
        result.append((source(name, contracts="§Q false"), False, "writing preconditions"))
    return result


class CollectionVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real collection verify.sh controls.")

    def verify(self, name, text, expected=True, *, env=None, detail=None, overall_deadline=None):
        verifier = load("collection_process", "lib/verify-logic.py")
        with tempfile.TemporaryDirectory(prefix="calor-collection-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Collections.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            deadline = time.monotonic() + min(120, float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))) + 2
            if overall_deadline is not None:
                deadline = min(deadline, overall_deadline)
            command = [str(HERE / "tasks/collections" / TASKS[name] / "verify.sh"), str(workspace)]
            try:
                output = verifier.run(command, HERE, deadline, env=environment)
                passed = True
            except RuntimeError as error:
                if "Verifier command exited" not in str(error):
                    raise
                output = str(error)
                passed = False
            type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if expected:
                runtime, coupling = COUNTS.get(name, (1, 1))
                self.assertIn(f"Verification passed: {name}; native/CLR signature; {runtime} runtime cases; "
                              f"{coupling} successful identity-specific output counterfactuals.", output)
                self.assertIn("not a universal body proof", output)
            if detail:
                self.assertIn(detail, output)
            return output

    def test_shared_actual_old_current_controls(self):
        for name in TASKS:
            for text, expected, detail in shared_cases(name):
                with self.subTest(name=name, expected=expected, detail=detail):
                    self.verify(name, text, expected, detail=detail)

    def test_exact_states_sequences_and_dead_decoys(self):
        negatives = {
            "SumList": [
                BODIES["SumList"].replace("§NEW{List<i32>}", "§NEW{List<i32>} §A §C{System.Linq.Enumerable.Repeat} §A 10 §A 1 §/C")
                    .replace("§PUSH{items} 10\n", ""),
                BODIES["SumList"].replace("§ASSIGN sum (+ sum item)", "§ASSIGN sum (+ sum 20)"),
                BODIES["SumList"].replace("§EACH{item} items", "§L{item:10:30:10}"),
                BODIES["SumList"].replace("§PUSH{items} 20", "§PUSH{items} 10").replace("§R sum", "§R (+ sum 10)"),
            ],
            "CreateScores": [
                BODIES["CreateScores"].replace('"Carol" 92', '"Charlie" 92'),
                BODIES["CreateScores"].replace('"Alice" 95', '"Alice" 0'),
                BODIES["CreateScores"].replace('§R §CNT{scores}', '§R (+ 1 2)'),
                BODIES["CreateScores"].replace('§R §CNT{scores}',
                    '§B{other:Dictionary<str,i32>} §NEW{Dictionary<str,i32>} §A scores\n§R §CNT{other}'),
            ],
            "UniqueCount": [
                BODIES["UniqueCount"].replace("§PUSH{items} 3\n", "", 1),
                BODIES["UniqueCount"].replace("§PUSH{items} 1\n§PUSH{items} 2\n",
                                             "§PUSH{items} 2\n§PUSH{items} 1\n", 1),
                "§B{items:HashSet<i32>} §NEW{HashSet<i32>} §A §C{System.Linq.Enumerable.Range} §A 1 §A 3 §/C\n§R §CNT{items}",
                BODIES["UniqueCount"].replace("§R §CNT{items}", "§R (+ 1 2)"),
            ],
            "BuildCollection": [
                BODIES["BuildCollection"].replace('§R (+ §CNT{items} §CNT{words})', "§R (+ §CNT{items} 3)"),
                BODIES["BuildCollection"].replace('§R (+ §CNT{items} §CNT{words})', "§R (+ 2 §CNT{words})"),
                BODIES["BuildCollection"].replace('§PUSH{items} 2', '§PUSH{items} 1'),
                BODIES["BuildCollection"].replace('2 "two"', '2 "one"'),
            ],
            "HasValue": [
                (BODIES["SumList"].split("§B{~sum")[0] + "§R §HAS{items} value").replace(
                    "§PUSH{items} 20", "§SETIDX{items} 0 11\n§PUSH{items} 20\n§SETIDX{items} 0 10"),
                BODIES["HasValue"].replace("§R §HAS{items} value", "§B{unused:bool} §HAS{items} value\n§R " + EXPECTED["HasValue"]),
                BODIES["HasValue"].replace("§R §HAS{items} value",
                    "§B{other:List<i32>} §NEW{List<i32>} §A items\n§R " + EXPECTED["HasValue"]),
                BODIES["HasValue"].replace("§R §HAS{items} value", "§IF (== value INT:-2147483648)\n"
                    "  §R true\n§R §HAS{items} value"),
            ],
            "GetListLength": [
                BODIES["GetListLength"].replace("§PUSH{items} i", "§PUSH{items} 1"),
                "§B{items:List<i32>} §NEW{List<i32>}\n§L{i:1:n:1}\n  §B{unused:i32} i\n§R n",
                BODIES["GetListLength"].replace("§PUSH{items} i", "§PUSH{items} (+ i 1)"),
                BODIES["GetListLength"].replace("§R §CNT{items}", "§R n"),
            ],
        }
        for name, bodies in negatives.items():
            for body in bodies:
                with self.subTest(name=name, body=body):
                    self.verify(name, source(name, body=body), False)

    def test_signatures_optional_guards_failures_and_mapping(self):
        for name in TASKS:
            self.verify(name, None, False, detail="Input missing")
            self.verify(name, source(name).replace(f"§F{{{name}:", "§F{Decoy:"), False, detail="Expected exactly one")
            self.verify(name, source(name, contracts=CONTRACTS.get(name, "") + "\n§S false"), False,
                        detail="postconditions exclude")
        self.verify("HasValue", source("HasValue", signature="§F{HasValue:pub} (i64:value) -> bool", body="§R false"), False,
                    detail="Wrong public signature")
        self.verify("GetListLength", source("GetListLength", signature="§F{GetListLength:pub} (i32:n) -> i64"), False,
                    detail="Wrong public signature")
        self.verify("GetListLength", source("GetListLength", contracts="§Q (>= n 0)\n§Q (<= n INT:2147483646)"),
                    False, detail="writing preconditions")
        self.verify("HasValue", source("HasValue", contracts="§Q (!= value INT:2147483647)"), False,
                    detail="writing preconditions")
        self.verify("GetListLength", source("GetListLength", contracts="§Q (>= n 0)\n§S (== result n)"))
        self.verify("HasValue", source("HasValue", contracts="§S (== result " + EXPECTED["HasValue"] + ")"))
        self.verify("SumList", source("SumList", contracts="§S (== result 60)"))
        self.verify("HasValue", source("HasValue", contracts="§Q (== (+ value 1) (+ value 1))"), False, detail="undefined")
        self.verify("HasValue", source("HasValue", contracts="§S (== (/ 0 (- value value)) 0)"), False, detail="undefined")
        self.verify("SumList", source("SumList", body="§R unknown"), False, detail="Calor compilation failed")
        self.verify("GetListLength", source("GetListLength", body="§WH true\n  §B{unused:i32} 1\n§R 0"),
                    False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "4"}, detail="timeout")

    def test_native_helpers_aliases_and_successful_counterfactual_oracles(self):
        empty = "§B{items:List<i32>} §NEW{List<i32>}"
        extra = "  §F{Empty:priv} () -> List<i32>\n    §E{alloc}\n    §R §NEW{List<i32>}\n"
        self.verify("SumList", source("SumList", body=BODIES["SumList"].replace(
            empty, "§B{items:List<i32>} §C{Empty} §/C"), extra=extra))
        for name in ("SumList", "GetListLength"):
            body = BODIES[name].replace(empty, empty + "\n§B{alias:List<i32>} items").replace(
                "§PUSH{items}", "§PUSH{alias}").replace("§CNT{items}", "§CNT{alias}")
            self.verify(name, source(name, body=body))
        factory = "§B{items:List<i32>} §C{System.Linq.Enumerable.ToList} §A §C{System.Linq.Enumerable.Range} §A 0 §A 0 §/C §/C"
        self.verify("SumList", source("SumList", body=BODIES["SumList"].replace(empty, factory)))
        has_factory = ("§B{items:List<i32>} §C{System.Linq.Enumerable.ToList} §A §C{System.Linq.Enumerable.Select} "
                       "§A §C{System.Linq.Enumerable.Range} §A 1 §A 3 §/C §A §LAM{x:i32} (* x 10) §/LAM §/C §/C\n"
                       "§R §HAS{items} value")
        self.verify("HasValue", source("HasValue", body=has_factory), False, detail="Unknown effects for call")
        # These return the right original result and change under a mutation,
        # but still do not compute the independent mutated sum/count.
        self.verify("SumList", source("SumList", body=BODIES["SumList"].replace(
            "§ASSIGN sum (+ sum item)", "§ASSIGN sum (+ sum (? (== item 10) 10 (? (== item 20) 20 (? (== item 30) 30 1))))")),
            False, detail="coupling")
        self.verify("UniqueCount", source("UniqueCount", body=BODIES["UniqueCount"].replace(
            "§R §CNT{items}", "§R (? (== §CNT{items} 3) 3 99)")), False, detail="coupling")
        self.verify("GetListLength", source("GetListLength", body=BODIES["GetListLength"].replace(
            "§R §CNT{items}", "§R (? (== §CNT{items} n) n 99)")), False, detail="coupling")
        # A mutant-only division failure must reject, not count as dependence.
        self.verify("SumList", source("SumList", body=BODIES["SumList"].replace(
            "§R sum", "§IF (!= sum 60)\n  §R (/ 1 (- sum sum))\n§R sum")), False, detail="counterfactual runtime failed")
        # Copy constructors independently expose nonempty initial dictionary and
        # list states; the otherwise valid source collection is not the counted one.
        self.verify("CreateScores", source("CreateScores", body=BODIES["CreateScores"].replace(
            "§R §CNT{scores}", "§B{copy:Dictionary<str,i32>} §NEW{Dictionary<str,i32>} §A scores\n§R §CNT{copy}")),
            False, detail="provenance")
        self.verify("BuildCollection", source("BuildCollection", body=BODIES["BuildCollection"].replace(
            "§R (+ §CNT{items} §CNT{words})",
            "§B{copy:List<i32>} §NEW{List<i32>} §A items\n§R (+ §CNT{copy} §CNT{words})")), False, detail="provenance")
        fixture = (HERE / "fixtures/collections-project/Collections.calr").read_text(encoding="utf-8")
        self.verify("SumList", fixture, False, detail="provenance")
        # Copy the unchanged fixture's native initialized-list block into a
        # disposable HasValue control; no new active source or closer spelling.
        list_block = "\n".join(line[4:] for line in fixture.splitlines()[5:10])
        self.verify("HasValue", source("HasValue", body=list_block + "\n§R §HAS{numbers} value"))

    def test_preserved_profiles_and_native_selection(self):
        suites = [
            ("logic", "test-logic-verifiers.py", "LogicVerifierTests", ["Factorial", "Clamp", "IsPositive", "Max"]),
            ("basic", "test-basic-syntax-verifiers.py", "BasicSyntaxVerifierTests", ["GetZero", "Multiply", "IsPositive", "PrintNumber"]),
            ("flow", "test-control-flow-verifiers.py", "ControlFlowVerifierTests", ["PrintNumbers", "Countdown", "DoWhileDemo", "Classify"]),
            ("remaining", "test-remaining-control-flow-verifiers.py", "RemainingControlFlowTests",
             ["Max", "GradeToLetter", "PrintEvenNumbers", "PrintDictionary"]),
            ("enum", "test-enum-verifiers.py", "EnumVerifierTests", ["Status", "Color", "Priority"]),
            ("contract", "test-contract-writing-verifiers.py", "ContractWritingVerifierTests",
             ["contract001", "contract002", "contract003", "contract004", "contract005"]),
            ("idiom", "test-idiom-verifiers.py", "IdiomVerifierTests", ["Clamp", "IsEven"]),
        ]
        preserved = 0
        for label, filename, class_name, names in suites:
            suite = load("collection_preserved_" + label, filename)
            test = getattr(suite, class_name)()
            for name in names:
                test.verify(name, suite.source(name))
                preserved += 1
        self.assertEqual(preserved, 26)
        print("Executed 26 preserved profile controls.")
        verifier = load("collection_native", "lib/verify-logic.py")
        output = verifier.run([sys.executable, str(HERE / "lib/verify-logic.py"), "--check-native"],
                              HERE, time.monotonic() + 120,
                              env=dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT)))
        self.assertIn("Selected native Z3 prerequisite passed: SATISFIABLE.", output)
        print("Executed 1 selected native-only control.")
        self.verify("SumList", source("SumList"), False, env={"CALOR_COMPILER_CONFIGURATION": "../Debug"},
                    detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("SumList", source("SumList"), False, env={"CALOR_COMPILER_ROOT": ""}, detail="CALOR_COMPILER_ROOT")


def run_compatibility(old, current, deadline):
    test = CollectionVerifierTests()
    adapter = load("collection_historical", "test-contract-writing-verifiers.py").historical_syntax
    def old_syntax(text):
        lines = adapter(text).splitlines()
        for index, line in enumerate(lines):
            if line.lstrip().startswith("§WH "):
                lines[index] = line.replace("§WH ", f"§WH{{collectionWhile{index}}} ", 1)
        return ("\n".join(lines) + "\n").replace("§LAM{", "§LAM{collectionLambda:").replace(
            "§/LAM", "§/LAM{collectionLambda}")
    initial = CollectionVerifierTests.entry_points_executed
    for label, root, historical in (("v0.29", old, True), ("current", current, False)):
        for name in TASKS:
            for text, expected, detail in shared_cases(name):
                if time.monotonic() >= deadline:
                    raise RuntimeError("Collection compatibility exceeded its 600-second total deadline.")
                test.verify(name, old_syntax(text) if historical else text, expected, detail=detail,
                            overall_deadline=deadline,
                            env={"CALOR_COMPILER_ROOT": str(root),
                                 "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))})
                print(f"{label} {name}: {'accepted' if expected else 'rejected'} actual entrypoint.")
    test.verify("GetListLength", old_syntax(source("GetListLength")), False, detail="Calor compilation failed",
                overall_deadline=deadline,
                env={"CALOR_COMPILER_ROOT": str(current),
                     "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))})
    expected_count = 2 * sum(len(shared_cases(name)) for name in TASKS) + 1
    observed = CollectionVerifierTests.entry_points_executed - initial
    if observed != expected_count or observed != 109:
        raise RuntimeError(f"Expected exactly 109 nonempty actual old/current collection controls; observed {observed}.")
    print(f"Compatibility passed: {observed} actual old/current collection entry-point controls.")


if __name__ == "__main__":
    result = unittest.main(exit=False).result
    if not result.wasSuccessful():
        sys.exit(1)
    if len(sys.argv) == 1 and CollectionVerifierTests.entry_points_executed != 122:
        raise RuntimeError(f"Expected exactly 122 current collection entrypoints; observed {CollectionVerifierTests.entry_points_executed}.")
