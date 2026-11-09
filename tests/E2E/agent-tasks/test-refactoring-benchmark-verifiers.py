#!/usr/bin/env python3
"""Unchanged paired benchmark inputs; disposable independent compiler controls."""

import importlib.util
import json
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
OLD = "d82f3ae95175ecd09581c6922701706be72b0ca9"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ADMISSION = load("benchmark_admission", HERE / "test-carrier-pattern-verifiers.py")
PROCESS = ADMISSION.PROCESS
TASKS = {
    "contract-pre": ("Contract.cs", "Sqrt"),
    "contract-post": ("Contract.cs", "Abs"),
    "contract-bounds": ("Contract.cs", "GetElement"),
    "contract-effect": ("Contract.cs", "PrintValue"),
    "extract-simple": ("Extract.cs", "Distance"),
    "extract-contracts": ("Extract.cs", "ProcessArray"),
    "extract-effects": ("Extract.cs", "LogAndCompute"),
    "extract-nested": ("Extract.cs", "ComputeNested"),
    "inline-simple": ("Inline.cs", "Calculate"),
    "inline-multi": ("Inline.cs", "SumOfSquares"),
    "inline-contracts": ("Inline.cs", "NormalizeScore"),
    "rename-param": ("Rename.cs", "Calculate"),
    "rename-contract": ("Rename.cs", "ValidatedOp"),
    "rename-shadow": ("Rename.cs", "OuterCalc"),
    "sig-addparam": ("Signature.cs", "Greet"),
    "sig-return": ("Signature.cs", "TryParse"),
    "sig-reorder": ("Signature.cs", "CreateRange"),
    "move-module": ("src/Math.cs", "Abs"),
    "move-deps": ("src/Math.cs", "Abs"),
    "move-contracts": ("src/Math.cs", "SafeDivide"),
}
COUNTS = {
    "contract-pre": 17, "contract-post": 16, "contract-bounds": 289, "contract-effect": 17,
    "extract-simple": 190, "extract-contracts": 185, "extract-effects": 284, "extract-nested": 356,
    "inline-simple": 227, "inline-multi": 165, "inline-contracts": 17,
    "rename-param": 15, "rename-contract": 289, "rename-shadow": 15,
    "sig-addparam": 36, "sig-return": 7, "sig-reorder": 188,
    "move-module": 41, "move-deps": 41, "move-contracts": 288,
}
EXECUTED = {"accepted": 0, "rejected": 0, "prerequisite": 0}
REJECTIONS = (
    "Benchmark input missing:", "Wrong benchmark selected method signature/owner:",
    "Benchmark requires exactly one selected method:", "Wrong benchmark contract documentation",
    "Wrong benchmark effect documentation:", "Wrong benchmark nullable-result documentation.",
    "Wrong independent benchmark runtime behavior", "Required benchmark supplying helper call missing.",
    "Benchmark supplying helper calls do not all affect successful observed output.",
    "Benchmark inline target still calls the original helper.",
    "Required benchmark inlined multiplication missing.",
    "Required benchmark inlined multiplications do not both supply successful output.",
    "Benchmark renamed parameter must preserve the independent inner local.",
    "Benchmark renamed parameter discarded the supplying inner local.",
    "Unsupported finite benchmark executable surface.",
)
NATIVE_BLOCKERS = (
    "Native benchmark literal-ID prerequisite refused: selected DeclarationIdentityStore.Sync requires canonical identities and rejects proven original m001/f001-style IDs. This is not a behavior rejection.",
)


def method_span(text, name):
    match = re.search(r"(?m)^    (?:public|private) (?:static )?\S+ " + re.escape(name) + r"\(", text)
    if not match:
        raise AssertionError(f"Missing original method {name}")
    start = match.start()
    while start > 0:
        preceding = text.rfind("\n", 0, start - 1) + 1
        if not text[preceding:start].strip().startswith("//"):
            break
        start = preceding
    opening = text.index("{", match.end())
    depth = 1
    end = opening + 1
    while depth:
        if text[end] == "{":
            depth += 1
        elif text[end] == "}":
            depth -= 1
        end += 1
    return start, end


def replace_method(text, name, replacement):
    start, end = method_span(text, name)
    return text[:start] + replacement.rstrip() + text[end:]


def declaration(name, parameters, result, body, docs="", private=False):
    return "".join("    // " + line + "\n" for line in docs.splitlines()) + (
        f"    {'private' if private else 'public'} {result} {name}({parameters})\n    {{\n"
        + "".join("        " + line + "\n" for line in body.splitlines()) + "    }"
    )


def source(task, equivalent=False):
    category = task.split("-")[0]
    fixture = f"refactor-{'signature' if category == 'sig' else category}-csharp"
    files = {str(path.relative_to(HERE / "fixtures" / fixture)): path.read_text(encoding="utf-8")
             for path in (HERE / "fixtures" / fixture).rglob("*.cs") if "obj" not in path.parts}
    filename, name = TASKS[task]
    text = files[filename]
    additions = ""
    if task == "contract-pre":
        replacement = declaration(name, "int x", "int",
                                  'if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));\nreturn x;',
                                  "Precondition: x >= 0")
    elif task == "contract-post":
        replacement = declaration(name, "int x", "int", "return x < 0 ? -x : x;", "Postcondition: result >= 0")
    elif task == "contract-bounds":
        replacement = declaration(name, "int index, int length", "int",
                                  'if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));\n'
                                  'if (index < 0 || index >= length) throw new ArgumentOutOfRangeException(nameof(index));\nreturn index;',
                                  "Precondition: index >= 0\nPrecondition: index < length\nPrecondition: length > 0")
    elif task == "contract-effect":
        replacement = declaration(name, "int x", "void", "Console.WriteLine(x);", "Effect: Writes to console (cw)")
    elif task == "extract-simple":
        replacement = declaration(name, "int x, int y", "int", "return SumOfSquares(x, y);")
        additions = declaration("SumOfSquares", "int a, int b", "int", "return a * a + b * b;", private=True)
    elif task == "extract-contracts":
        replacement = declaration(name, "int index, int length", "int",
                                  "if (!ValidateIndex(index, length)) return -1;\nreturn index;",
                                  "Precondition: index >= 0\nPrecondition: index < length\nPostcondition: result >= 0 (when valid)")
        additions = declaration("ValidateIndex", "int index, int length", "bool", "return index >= 0 && index < length;",
                                "Precondition: length >= 0\nPostcondition: result == (index >= 0 && index < length)", private=True)
    elif task == "extract-effects":
        replacement = declaration(name, "int a, int b", "int",
                                  'Console.WriteLine("Computing sum");\nreturn ComputeSum(a, b);', "Effect: Writes to console")
        additions = declaration("ComputeSum", "int a, int b", "int", "return a + b;", "Effect: None (pure function)", private=True)
    elif task == "extract-nested":
        replacement = declaration(name, "int x, int y, int z", "int", "return Square(x) + Square(y) + Square(z);")
        additions = declaration("Square", "int n", "int", "return n * n;", "Postcondition: result >= 0", private=True)
    elif task == "inline-simple":
        replacement = declaration(name, "int a, int b", "int", "return a * 2 + b * 2;")
    elif task == "inline-multi":
        replacement = declaration(name, "int x, int y", "int", "return x * x + y * y;", "Postcondition: result >= 0")
    elif task == "inline-contracts":
        replacement = declaration(name, "int score", "int", "if (score < 0) return 0;\nif (score > 100) return 100;\nreturn score;",
                                  "Postcondition: result >= 0\nPostcondition: result <= 100")
    elif category == "rename":
        start, end = method_span(text, name)
        old, new = ("val", "value") if task == "rename-param" else ("num", "value") if task == "rename-contract" else ("x", "input")
        replacement = re.sub(r"\b" + old + r"\b", new, text[start:end])
    elif task == "sig-addparam":
        replacement = declaration(name, "string greeting, string name", "string", "return greeting + name;")
    elif task == "sig-return":
        replacement = declaration(name, "string input", "int?", "return null;",
                                  "Postcondition: result is null for invalid input, or the parsed value")
    elif task == "sig-reorder":
        replacement = declaration(name, "int step, int start, int end", "int", "return (end - start) / step;",
                                  "Precondition: step > 0\nPrecondition: start <= end")
        text = text.replace("CreateRange(0, max, 1)", "CreateRange(1, 0, max)")
    else:
        start, end = method_span(text, name)
        moved = text[start:end]
        files["src/Utils.cs"] = files["src/Utils.cs"].rstrip()[:-1] + moved + "\n}\n"
        files[filename] = text[:start] + text[end:]
        if name == "Abs":
            files[filename] = files[filename].replace("return Abs(", "return new UtilsModule().Abs(")
        if equivalent and name == "Abs":
            files["src/Utils.cs"] = files["src/Utils.cs"].replace("public int Abs", "public static int Abs")
            files[filename] = files[filename].replace("new UtilsModule().Abs(", "UtilsModule.Abs(")
        elif equivalent:
            files["src/Utils.cs"] = files["src/Utils.cs"].replace("return a / b;", "var quotient = a / b;\n        return quotient;")
        return files
    files[filename] = replace_method(text, name, replacement)
    if additions:
        files[filename] = files[filename].rstrip()[:-1] + "\n" + additions + "\n}\n"
    if equivalent:
        files = {path: text.replace("int ", "System.Int32 ").replace("string ", "System.String ")
                 .replace("return a + b;", "var sum = b + a;\n        return sum;")
                 .replace("return a * a + b * b;", "var sum = b * b + a * a;\n        return sum;")
                 .replace("return x < 0 ? -x : x;", "if (x >= 0) return x;\n        return -x;")
                 for path, text in files.items()}
    return files


def native_source(task):
    category = task.split("-")[0]
    fixture = f"refactor-{'signature' if category == 'sig' else category}-calor"
    base = HERE / "fixtures" / fixture
    return {str(path.relative_to(base)): path.read_text(encoding="utf-8") for path in base.rglob("*.calr")}


def native_sidecar(task, files):
    category = task.split("-")[0]
    fixture = f"refactor-{'signature' if category == 'sig' else category}-calor"
    declarations = []
    for file in sorted(files):
        original = PROCESS.run(["git", "show", f"{OLD}:tests/E2E/agent-tasks/fixtures/{fixture}/{file}"],
                               ROOT, time.monotonic() + 30)
        module = re.search(r"(?m)^§M\{([^:}]+):([^}]+)\}", original)
        if not module:
            raise AssertionError("Missing authoritative archived module identity")
        module_id, container = module.groups()
        declarations.append(dict(Id=module_id, File=file, Container="", Kind=0, Name=container, Signature=""))
        original_headers = re.findall(r"(?m)^  §F\{(f\d+):([^:}]+):pub\} \(([^)]*)\) -> ([^\n\r]+)", original)
        if not original_headers:
            raise AssertionError("Empty original native declaration lineage")
        for identity, name, parameters, output in original_headers:
            signature = json.dumps(dict(arity=0, parameters=[
                dict(type=parameter.strip().split(":")[0], modifier="None")
                for parameter in parameters.split(",") if parameter.strip()], output=output), separators=(",", ":"))
            declarations.append(dict(Id=identity, File=file, Container=container, Kind=1, Name=name, Signature=signature))
    return json.dumps(dict(Version=1, Files=sorted(files), Declarations=declarations), indent=2)


def controls(task):
    text = source(task)
    filename, name = TASKS[task]
    if task.startswith("move-"):
        filename = "src/Utils.cs"
    yield "canonical", text, True
    yield "equivalent", source(task, equivalent=True), True
    yield "missing", {}, False
    wrong = {file: re.sub(r"\b" + re.escape(name) + r"\b", "Other" + name, source)
             for file, source in text.items()}
    yield "wrong-selected-name", wrong, False
    wrong = dict(text)
    start, end = method_span(wrong[filename], name)
    selected = wrong[filename][start:end]
    wrong[filename] = wrong[filename][:start] + selected.replace("public ", "private ", 1) + wrong[filename][end:]
    if not task.startswith("move-"):
        yield "wrong-selected-access", wrong, False
    wrong = dict(text)
    start, end = method_span(wrong[filename], name)
    selected = wrong[filename][start:end]
    opening = selected.index("{")
    body = "Console.WriteLine(0);" if task == "contract-effect" else 'return "";' if task == "sig-addparam" else "return 1;" if task == "sig-return" else "return 0;"
    wrong[filename] = wrong[filename][:start] + selected[:opening + 1] + "\n        " + body + "\n    }" + wrong[filename][end:]
    yield "wrong-independent-behavior", wrong, False
    if task not in ("inline-simple", "rename-shadow", "sig-addparam"):
        wrong = dict(text)
        start, end = method_span(wrong[filename], name)
        selected = wrong[filename][start:end]
        stripped = re.sub(r"(?m)^    // (?:Precondition|Postcondition|Effect):.*\n", "", selected)
        if stripped != selected:
            wrong[filename] = wrong[filename][:start] + stripped + wrong[filename][end:]
            yield "missing-selected-documentation", wrong, False
        weaker = selected.replace("result >= 0", "result >= -1").replace("value >= 0", "value >= -1").replace(
            "x >= 0", "x >= -1").replace("index >= 0", "index >= -1").replace(
            "result == value * 2", "result >= value * 2").replace("step > 0", "step >= 0").replace(
            "b != 0", "b >= 0").replace("Effect: Writes to console", "Effect: None")
        if weaker != selected:
            wrong = dict(text)
            wrong[filename] = wrong[filename][:start] + weaker + wrong[filename][end:]
            yield "weakened-selected-documentation", wrong, False
    if task.startswith("extract-"):
        helper = {"extract-simple": "SumOfSquares", "extract-contracts": "ValidateIndex",
                  "extract-effects": "ComputeSum", "extract-nested": "Square"}[task]
        wrong = dict(text)
        start, end = method_span(wrong[filename], name)
        selected = wrong[filename][start:end]
        opening = selected.index("{")
        dead = {
            "extract-simple": "_ = SumOfSquares(x, y);\nreturn x * x + y * y;",
            "extract-contracts": "_ = ValidateIndex(index, length);\nreturn index >= 0 && index < length ? index : -1;",
            "extract-effects": '_ = ComputeSum(a, b);\nConsole.WriteLine("Computing sum");\nreturn a + b;',
            "extract-nested": "_ = Square(x); _ = Square(y); _ = Square(z);\nreturn x * x + y * y + z * z;",
        }[task]
        wrong[filename] = wrong[filename][:start] + selected[:opening + 1] + "\n        " + dead + "\n    }" + wrong[filename][end:]
        yield "dead-supplying-helper", wrong, False
        wrong = dict(text)
        wrong[filename] = replace_method(wrong[filename], helper, declaration(
            helper, "int n" if task == "extract-nested" else "int index, int length" if task == "extract-contracts" else "int a, int b",
            "bool" if task == "extract-contracts" else "int", "return true;" if task == "extract-contracts" else "return 0;",
            "Postcondition: result >= 0" if task == "extract-nested" else
            "Precondition: length >= 0\nPostcondition: result == (index >= 0 && index < length)" if task == "extract-contracts" else
            "Effect: None (pure function)" if task == "extract-effects" else "", private=True))
        yield "wrong-helper-behavior", wrong, False
    if task.startswith("inline-"):
        wrong = dict(text)
        original = {"inline-simple": "return Double(a) + Double(b);",
                    "inline-multi": "return Square(x) + Square(y);",
                    "inline-contracts": "return Clamp(score, 0, 100);"}[task]
        start, end = method_span(wrong[filename], name)
        selected = wrong[filename][start:end]
        wrong[filename] = wrong[filename][:start] + selected[:selected.index("{") + 1] + "\n        " + original + "\n    }" + wrong[filename][end:]
        yield "not-inlined", wrong, False
        if task != "inline-contracts":
            wrong = dict(text)
            decoy = "var discardedA = a * 2; var discardedB = b * 2;\nreturn a + a + b + b;" if task == "inline-simple" else (
                "var discardedX = x * x; var discardedY = y * y;\nvar answer = 0;\n"
                "for (var count = 0; count < Math.Abs(x); count++) answer += Math.Abs(x);\n"
                "for (var count = 0; count < Math.Abs(y); count++) answer += Math.Abs(y);\nreturn answer;")
            start, end = method_span(wrong[filename], name)
            selected = wrong[filename][start:end]
            wrong[filename] = wrong[filename][:start] + selected[:selected.index("{") + 1] + "\n        " + decoy + "\n    }" + wrong[filename][end:]
            yield "decoy-inlined-multiplications", wrong, False
    if task == "rename-shadow":
        wrong = dict(text)
        wrong[filename] = wrong[filename].replace("var inner =", "var renamedInner =").replace("input + inner", "input + renamedInner")
        yield "discarded-local-identity", wrong, False
        wrong = dict(text)
        wrong[filename] = wrong[filename].replace("return input + inner;", "return input * 3;")
        yield "dead-supplying-local", wrong, False
    if task == "sig-reorder":
        wrong = dict(text)
        wrong[filename] = wrong[filename].replace("CreateRange(1, 0, max)", "CreateRange(0, max, 1)")
        yield "stale-callsite-order", wrong, False
    if task.startswith("move-"):
        wrong = source("move-contracts" if task != "move-contracts" else "move-module")
        yield "wrong-move", wrong, False
    if task == "contract-post":
        yield "selected-name-in-additional-source", dict(
            text, **{"Additional.cs": "namespace Other; public class Decoy { public int Abs(int x) => x; }\n"}), False
        wrong = dict(text)
        wrong[filename] = wrong[filename].rstrip()[:-1] + """
    [System.Runtime.InteropServices.DllImport("deliberately-unused-native")]
    private static extern int Escaped();
}
"""
        yield "foreign-native-executable-surface", wrong, False


def execute(task, category, files, expected, root=ROOT, deadline=None, language="csharp", environment=None, blocker=False):
    definition = json.loads((HERE / "tasks/refactoring-benchmark" / f"refactor-{task}-{language}/task.json").read_text())
    maximum = definition["timeout"]
    deadline = min(time.monotonic() + maximum, deadline or float("inf"))
    with tempfile.TemporaryDirectory(prefix="calor-benchmark-controls-") as temporary:
        workspace = Path(temporary).resolve()
        if language == "calor":
            workspace /= "tests/benchmark"
            workspace.mkdir(parents=True)
        for filename, text in files.items():
            path = workspace / filename
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        if language == "calor" and files:
            sidecar = workspace / ".calor/ids.json"
            sidecar.parent.mkdir()
            sidecar.write_text(native_sidecar(task, files), encoding="utf-8")
        env = dict(os.environ, CALOR_COMPILER_ROOT=str(root))
        env.update(environment or {})
        env["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(maximum, deadline - time.monotonic()))
        try:
            output = PROCESS.run(
                ["bash", str(HERE / "tasks/refactoring-benchmark" / f"refactor-{task}-{language}/verify.sh"), str(workspace)],
                HERE, deadline, env)
        except RuntimeError as error:
            if expected:
                raise
            output = ADMISSION.control_failure(error)
            if blocker:
                if not any(marker in output for marker in NATIVE_BLOCKERS):
                    raise
                EXECUTED["prerequisite"] += 1
                print(f"{task}/{category}: native positive BLOCKED\n{output}")
                return output
            if not any(marker in output for marker in REJECTIONS):
                raise
            EXECUTED["rejected"] += 1
            print(f"{task}/{category}: rejected")
            return output
        if not expected:
            raise AssertionError(f"Unexpected benchmark acceptance {task}/{category}:\n{output}")
        ADMISSION.assert_selected_compiler(output, env)
        verdicts = [line for line in output.splitlines() if line.startswith("Verification passed:")]
        if len(verdicts) != 1 or not verdicts[0].startswith(f"Verification passed: bench-refactor-{task}-{language};"):
            raise AssertionError(f"Wrong unique benchmark verdict:\n{output}")
        if "runtime cases;" not in output or "successful source counterfactuals." not in output or "Limits:" not in output:
            raise AssertionError(f"Incomplete benchmark behavioral evidence:\n{output}")
        if f"; {COUNTS[task]} runtime cases;" not in output or not re.search(r"; [1-9]\d* successful source counterfactuals\.", output):
            raise AssertionError(f"Wrong exact benchmark runtime census or empty intervention evidence:\n{output}")
        EXECUTED["accepted"] += 1
        print(f"{task}/{category}: {verdicts[0]}")
        return output


def run_compatibility(old, current, deadline):
    before = dict(EXECUTED)
    for arm, root in (("v0.29", old), ("current", current)):
        for task in TASKS:
            execute(task, f"{arm}-canonical", source(task), True, root, deadline)
            execute(task, f"{arm}-wrong-behavior", next(files for category, files, _ in controls(task)
                    if category == "wrong-independent-behavior"), False, root, deadline)
        execute("contract-post", f"{arm}-foreign-native-surface", next(files for category, files, _ in controls("contract-post")
                if category == "foreign-native-executable-surface"), False, root, deadline)
    if EXECUTED != dict(accepted=before["accepted"] + 40, rejected=before["rejected"] + 42,
                        prerequisite=before["prerequisite"]):
        raise AssertionError("Incomplete actual independent C# benchmark compatibility census.")
    print("Compatibility passed: 82 actual independent C# old/current benchmark entrypoints (40 accepted, 42 rejected). Calor identity admission is separate.")


class BenchmarkTests(unittest.TestCase):
    def test_actual_selected_prerequisite_refusals(self):
        before = dict(EXECUTED)
        with tempfile.TemporaryDirectory(prefix="benchmark-selection-controls-") as temporary:
            directory = Path(temporary).resolve()
            workspace = directory / "workspace"
            workspace.mkdir()
            (workspace / "Contract.cs").write_text(source("contract-post")["Contract.cs"], encoding="utf-8")
            command = ["bash", str(HERE / "tasks/refactoring-benchmark/refactor-contract-post-csharp/verify.sh"), str(workspace)]
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            missing = directory / "missing"
            missing_image = missing / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
            for changes, refusal in (
                ({"CALOR_COMPILER_ROOT": str(missing)}, f"Verification failed: Selected compiler dependency missing: {missing_image}; build the selected root first."),
                ({"CALOR_COMPILER_ROOT": ""}, "Verification failed: CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path."),
                ({"CALOR_COMPILER_CONFIGURATION": "../other"}, "Verification failed: CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root."),
                ({"CALOR_LOGIC_TIMEOUT_SECONDS": "121"}, "Verification failed: CALOR_LOGIC_TIMEOUT_SECONDS must be in (0, 120]."),
            ):
                environment = dict(os.environ, CALOR_COMPILER_ROOT=str(ROOT), CALOR_COMPILER_CONFIGURATION=configuration)
                environment.update(changes)
                ADMISSION.reject_prerequisite(command, environment, refusal)
                EXECUTED["prerequisite"] += 1
            corrupt = directory / "corrupt"
            bin_path = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
            target = corrupt / bin_path
            target.mkdir(parents=True)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            for image in ("calor.dll", "Calor.Runtime.dll", "Microsoft.Z3.dll", native):
                shutil.copyfile(ROOT / bin_path / image, target / image)
            (corrupt / ".github").mkdir()
            manifest = ".github/z3-binaries-4.15.7.sha256"
            shutil.copyfile(ROOT / manifest, corrupt / manifest)
            (target / native).write_bytes(b"deliberate selected native prerequisite corruption")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(corrupt), CALOR_COMPILER_CONFIGURATION=configuration)
            ADMISSION.reject_prerequisite(command, environment,
                f"Verification failed: Selected native manifest mismatch: {target / native}")
            EXECUTED["prerequisite"] += 1
            shutil.copyfile(ROOT / bin_path / native, target / native)
            (target / "Microsoft.Z3.dll").write_bytes(b"deliberate selected managed prerequisite corruption")
            ADMISSION.reject_prerequisite(command, environment,
                f"Verification failed: Selected native manifest mismatch: {target / 'Microsoft.Z3.dll'}")
            EXECUTED["prerequisite"] += 1
        self.assertEqual(EXECUTED, dict(accepted=before["accepted"], rejected=before["rejected"],
                                       prerequisite=before["prerequisite"] + 6))

    def test_actual_native_literal_identity_prerequisites(self):
        before = dict(EXECUTED)
        for task in TASKS:
            with self.subTest(task=task):
                execute(task, "literal-original-identity", native_source(task), False, language="calor", blocker=True)
        self.assertEqual(EXECUTED, dict(accepted=before["accepted"], rejected=before["rejected"],
                                       prerequisite=before["prerequisite"] + 20))
        print("Native census: 20 actual literal-ID prerequisite refusals; 0 admitted behavioral profiles.")

    def test_actual_independent_csharp_controls(self):
        before = dict(EXECUTED)
        self.assertEqual(len(TASKS), 20)
        self.assertEqual(
            {path.parent.name for path in (HERE / "tasks/refactoring-benchmark").glob("*/task.json")},
            {f"refactor-{task}-{language}" for task in TASKS for language in ("calor", "csharp")},
        )
        planned = [accepted for task in TASKS for _, _, accepted in controls(task)]
        self.assertEqual((planned.count(True), planned.count(False)), (40, 127))
        expected = {"accepted": 0, "rejected": 0}
        for task in TASKS:
            for category, files, accepted in controls(task):
                with self.subTest(task=task, category=category):
                    execute(task, category, files, accepted)
                expected["accepted" if accepted else "rejected"] += 1
        self.assertEqual(EXECUTED, dict(accepted=before["accepted"] + expected["accepted"],
                                       rejected=before["rejected"] + expected["rejected"],
                                       prerequisite=before["prerequisite"]))
        print(f"Current independent C# census: {expected}; {sum(expected.values())} actual controls.")

    def test_mock_infrastructure_failures_never_count(self):
        before = dict(EXECUTED)
        for failure in (
            "Verifier command exited 2:\nWrong independent benchmark runtime behavior",
            "Verifier command exited 1:\nUNKNOWN\nWrong independent benchmark runtime behavior",
            "Verifier command exited 1:\nBuild FAILED\nWrong independent benchmark runtime behavior",
            "Verifier command exited 1:\nZ3Exception\nWrong independent benchmark runtime behavior",
            "Verifier timeout after 120s: dotnet",
            "Verifier command exited 1:\nVerification passed: forged\nWrong independent benchmark runtime behavior",
        ):
            with self.subTest(failure=failure), patch.object(PROCESS, "run", side_effect=RuntimeError(failure)):
                with self.assertRaises(RuntimeError):
                    execute("contract-post", "wrong-behavior", source("contract-post"), False)
        self.assertEqual(EXECUTED, before)


if __name__ == "__main__":
    unittest.main()
