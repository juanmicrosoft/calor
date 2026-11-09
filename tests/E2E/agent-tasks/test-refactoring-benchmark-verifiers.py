#!/usr/bin/env python3
"""Unchanged paired benchmark inputs; disposable independent compiler controls."""

import importlib.util
import hashlib
from contextlib import contextmanager
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import tarfile
import time
from threading import Lock
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(HERE.parents[2]))).resolve()
OLD = "d82f3ae95175ecd09581c6922701706be72b0ca9"
INTERIM_NATIVE_COMMIT = "260f086936198c112b1cf0f4d4b053771809513a"
INTERIM_NATIVE_AUTHORING = False


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
NATIVE_COUNTS = dict(COUNTS, **{"contract-pre": 10, "contract-bounds": 45, "extract-contracts": 60,
                              "rename-contract": 55, "move-contracts": 271})
EXECUTED = {"accepted": 0, "rejected": 0, "prerequisite": 0}
CENSUS_LOCK = Lock()
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
    "Wrong native benchmark module identity.", "Wrong native benchmark original declaration identity:",
    "Wrong native benchmark requested extraction identity:", "Wrong native benchmark module namespace topology.",
    "Wrong native benchmark ambiguous cross-module function identity.",
    "Wrong native benchmark effect declaration:", "Wrong native benchmark pure helper:",
)
NATIVE_BLOCKERS = (
    "Native benchmark literal-ID prerequisite refused: selected DeclarationIdentityStore.Sync requires canonical identities and rejects proven original m001/f001-style IDs. This is not a behavior rejection.",
    "Native benchmark variable identity prerequisite refused:",
    "Native benchmark signature prerequisite refused:",
    "Native benchmark identity anchor prerequisite refused:",
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


def native_sidecar(task, files, deadline=None):
    category = task.split("-")[0]
    fixture = f"refactor-{'signature' if category == 'sig' else category}-calor"
    declarations = []
    for file in sorted(files):
        original = PROCESS.run(["git", "show", f"{OLD}:tests/E2E/agent-tasks/fixtures/{fixture}/{file}"],
                               ROOT, min(time.monotonic() + 30, deadline if deadline is not None else float("inf")))
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


def current_native_source(task, *, equivalent=False):
    """Disposable native projections; original protected fixtures stay untouched."""
    category = task.split("-")[0]
    declarations = {}
    def add(name, parameters, result, expression, pre=(), post=(), effects=None, statements=()):
        declarations[name] = [parameters, result, expression, list(pre), list(post), effects, list(statements)]
    if category == "contract":
        module, filename = "ContractExamples", "Contract.calr"
        add("Sqrt", "int:x", "int", "x")
        add("Abs", "int:x", "int", "x < 0 ? -x : x")
        add("GetElement", "int:index, int:length", "int", "index >= 0 && index < length ? index : -1")
        add("PrintValue", "int:x", "void", None, effects="cw", statements=["Console.WriteLine(x)"])
        if task == "contract-pre":
            declarations["Sqrt"][3] = ["x >= 0"]
        elif task == "contract-post":
            declarations["Abs"][4] = ["result >= 0"]
        elif task == "contract-bounds":
            declarations["GetElement"][3] = ["index >= 0", "index < length", "length > 0"]
    elif category == "extract":
        module, filename = "ExtractExamples", "Extract.calr"
        add("Distance", "int:x, int:y", "int", "x * x + y * y")
        add("ProcessArray", "int:index, int:length", "int", "index >= 0 && index < length ? index : -1",
            ["index >= 0", "index < length"], ["result >= 0"])
        add("LogAndCompute", "int:a, int:b", "int", "a + b", effects="cw",
            statements=['Console.WriteLine("Computing sum")'])
        add("ComputeNested", "int:x, int:y, int:z", "int", "x * x + y * y + z * z")
        if task == "extract-simple":
            add("SumOfSquares", "int:a, int:b", "int", "a * a + b * b")
            declarations["Distance"][2] = "SumOfSquares(x, y)"
        elif task == "extract-contracts":
            add("ValidateIndex", "int:index, int:length", "bool", "index >= 0 && index < length",
                ["length >= 0"], ["result == (index >= 0 && index < length)"])
            declarations["ProcessArray"][2] = "ValidateIndex(index, length) ? index : -1"
        elif task == "extract-effects":
            add("ComputeSum", "int:a, int:b", "int", "a + b")
            declarations["LogAndCompute"][2] = "ComputeSum(a, b)"
        else:
            add("Square", "int:n", "int", "n * n", post=["result >= 0"])
            declarations["ComputeNested"][2] = "Square(x) + Square(y) + Square(z)"
    elif category == "inline":
        module, filename = "InlineExamples", "Inline.calr"
        add("Double", "int:x", "int", "x * 2")
        add("Calculate", "int:a, int:b", "int", "Double(a) + Double(b)")
        add("Clamp", "int:value, int:min, int:max", "int", "value < min ? min : (value > max ? max : value)",
            ["min <= max"], ["result >= min", "result <= max"])
        add("NormalizeScore", "int:score", "int", "Clamp(score, 0, 100)", post=["result >= 0", "result <= 100"])
        add("Square", "int:n", "int", "n * n", post=["result >= 0"])
        add("SumOfSquares", "int:x, int:y", "int", "Square(x) + Square(y)", post=["result >= 0"])
        if task == "inline-simple":
            declarations["Calculate"][2] = "a * 2 + b * 2"
        elif task == "inline-multi":
            declarations["SumOfSquares"][2] = "x * x + y * y"
        else:
            declarations["NormalizeScore"][2] = "score < 0 ? 0 : (score > 100 ? 100 : score)"
    elif category == "rename":
        module, filename = "RenameExamples", "Rename.calr"
        add("Calculate", "int:val", "int", "val * 2", post=["result == val * 2"])
        add("ValidatedOp", "int:num, int:max", "int", "num", ["num >= 0", "num <= max"], ["result <= max"])
        add("OuterCalc", "int:x", "int", "x + inner", statements=["let int:inner = x * 2"])
        if task == "rename-param":
            declarations["Calculate"] = ["int:value", "int", "value * 2", [], ["result == value * 2"], None, []]
        elif task == "rename-contract":
            declarations["ValidatedOp"] = ["int:value, int:max", "int", "value", ["value >= 0", "value <= max"], ["result <= max"], None, []]
        else:
            declarations["OuterCalc"] = ["int:input", "int", "input + inner", [], [], None, ["let int:inner = input * 2"]]
    elif category == "sig":
        module, filename = "SignatureExamples", "Signature.calr"
        add("Greet", "string:name", "string", "name")
        add("TryParse", "string:input", "int", "0")
        add("CreateRange", "int:start, int:end, int:step", "int", "(end - start) / step", ["start <= end", "step > 0"])
        add("CountSteps", "int:max", "int", "CreateRange(0, max, 1)", ["max > 0"])
        if task == "sig-addparam":
            declarations["Greet"][0:3] = ["string:greeting, string:name", "string", "greeting + name"]
        elif task == "sig-return":
            declarations["TryParse"][1:3] = ["Option<int>", "§NN"]
        else:
            declarations["CreateRange"][0] = "int:step, int:start, int:end"
            declarations["CountSteps"][2] = "CreateRange(1, 0, max)"
    else:
        module, filename = "MathModule", "src/Math.calr"
        add("Abs", "int:x", "int", "x < 0 ? -x : x", post=["result >= 0"])
        add("SafeDivide", "int:a, int:b", "int", "a / b", ["b != 0"], ["result == a / b"])
        add("Distance", "int:x, int:y", "int", "Abs(x * x + y * y)", post=["result >= 0"])

    def emit(module, members):
        output = [f"module {module}" + ("" if INTERIM_NATIVE_AUTHORING else f" {module}")]
        for name, (parameters, result, expression, pre, post, effects, statements) in members.items():
            def condition(source):
                comparison = re.fullmatch(r"(\w+) (>=|<=|>|<) (-?\w+)", source)
                if equivalent and comparison:
                    left, operator, right = comparison.groups()
                    return f"{right} " + {">=": "<=", "<=": ">=", ">": "<", "<": ">"}[operator] + f" {left}"
                return source
            output.append(f"  func {name} ({parameters}) → {result}")
            output += [f"    ! {effects}"] if effects else []
            output += [f"    requires {condition(predicate)}" for predicate in pre]
            output += [f"    => {condition(predicate)}" for predicate in post]
            if equivalent and task == "contract-effect" and name == "PrintValue":
                statements = ["let int:printed = x + 0", "Console.WriteLine(printed)"]
            output += ["    " + statement for statement in statements]
            if expression is not None:
                if equivalent and name == TASKS[task][1]:
                    output += [f"    let {result}:answer = ({expression})", "    return answer"]
                else:
                    output.append(f"    return ({expression})")
        return re.sub(r"\bstep\b", "@step", "\n".join(output) + "\n")
    if category == "move":
        moved = "SafeDivide" if task == "move-contracts" else "Abs"
        utility = {"Log": ["string:message", "void", None, [], [], "cw", ["Console.WriteLine(message)"]],
                   moved: declarations.pop(moved)}
        return {filename: emit(module, declarations), "src/Utils.calr": emit("UtilsModule", utility)}
    return {filename: emit(module, declarations)}


def current_native_sidecar(task, files, deadline=None):
    category = task.split("-")[0]
    originals = native_source(task)
    document = json.loads(native_sidecar(task, originals, deadline))
    if category == "move":
        name = "SafeDivide" if task == "move-contracts" else "Abs"
        moved = [entry for entry in document["Declarations"] if entry["Kind"] == 1 and entry["Name"] == name]
        if len(moved) != 1 or moved[0]["File"] != "src/Math.calr" or moved[0]["Container"] != "MathModule":
            raise AssertionError("Ambiguous archived move lineage")
        moved[0].update(File="src/Utils.calr", Container="UtilsModule")
    extra = {"extract-simple": ("SumOfSquares", "f005"), "extract-contracts": ("ValidateIndex", "f006"),
             "extract-effects": ("ComputeSum", "f007"), "extract-nested": ("Square", "f008")}.get(task)
    if extra:
        name, identity = extra
        filename = next(iter(files))
        module = re.search(r"^module (\w+)", files[filename]).group(1)
        parameters, result = re.search(rf"  func {name} \(([^)]*)\) → ([^\n]+)", files[filename]).groups()
        signature = json.dumps(dict(arity=0, parameters=[
            dict(type=parameter.strip().split(":")[0], modifier="None") for parameter in parameters.split(",")],
            output=result), separators=(",", ":"))
        document["Declarations"].append(dict(Id=identity, File=filename, Container=module, Kind=1,
                                             Name=name, Signature=signature))
    return json.dumps(document, indent=2)


@contextmanager
def source_built_native(source_commit, deadline=None):
    outer_deadline = deadline
    deadline = min(deadline if deadline is not None else float("inf"), time.monotonic() + 600)
    with tempfile.TemporaryDirectory(prefix="benchmark-native-current-") as temporary:
        directory = Path(temporary)
        archive, selected = directory / "source.tar", directory / "source"
        selected.mkdir()
        PROCESS.run(["git", "archive", "--format=tar", f"--output={archive}", source_commit], ROOT, deadline)
        with tarfile.open(archive) as source:
            source.extractall(selected, filter="data")
        for relative in ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes"):
            shutil.copytree(ROOT / relative, selected / relative, dirs_exist_ok=True)
        print(PROCESS.run([sys.executable, "scripts/verify-z3-assets.py"], selected, deadline), end="")
        print(PROCESS.run(["dotnet", "restore", "src/Calor.Compiler/Calor.Compiler.csproj", "--locked-mode",
                          "--source", str(Path.home() / ".nuget/packages"), "--disable-build-servers",
                          "-p:UseSharedCompilation=false", "-nodeReuse:false", "-v", "quiet"], selected, deadline), end="")
        print(PROCESS.run(["dotnet", "build", "src/Calor.Compiler/Calor.Compiler.csproj", "--no-restore",
                          "-c", os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug"),
                          "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false",
                          "-v", "quiet"], selected, deadline), end="")
        print(f"Actual native compiler source: {source_commit}")
        image = selected / "src/Calor.Compiler/bin" / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
        hashes = {name: hashlib.sha256((image / name).read_bytes()).hexdigest()
                  for name in ("calor.dll", "Calor.Runtime.dll", "Microsoft.Z3.dll")}
        print("Native selected source archive SHA256:", hashlib.sha256(archive.read_bytes()).hexdigest())
        print("Native selected image SHA256:", json.dumps(hashes, sort_keys=True))
        try:
            yield selected
        finally:
            if hashes != {name: hashlib.sha256((image / name).read_bytes()).hexdigest() for name in hashes}:
                raise AssertionError("Native selected compiler images changed during controls")
            final_deadline = min(outer_deadline if outer_deadline is not None else float("inf"), time.monotonic() + 120)
            print(PROCESS.run([sys.executable, "scripts/verify-z3-assets.py"], selected, final_deadline), end="")


NATIVE_UNADMITTED = {"rename-shadow", "sig-return"}


def native_span(text, name):
    match = re.search(rf"(?m)^  func {re.escape(name)}(?: priv)? \([^\n]*\n", text)
    if not match:
        raise AssertionError(f"Missing native control declaration {name}")
    following = re.search(r"(?m)^  func ", text[match.end():])
    return match.start(), match.end() + following.start() if following else len(text)


def native_replace(files, name, transform):
    changed = dict(files)
    matches = [filename for filename, text in files.items() if re.search(rf"(?m)^  func {re.escape(name)} \(", text)]
    if len(matches) != 1:
        raise AssertionError(f"Ambiguous native control edit {name}: {matches}")
    filename = matches[0]
    start, end = native_span(changed[filename], name)
    before = changed[filename][start:end]
    after = transform(before)
    if after == before:
        raise AssertionError(f"Empty native control edit {name}")
    changed[filename] = changed[filename][:start] + after + changed[filename][end:]
    return changed


def native_controls(task):
    if task in NATIVE_UNADMITTED:
        raise AssertionError("Unadmitted task cannot supply genuine positive controls")
    files = current_native_source(task)
    name = TASKS[task][1]
    yield "canonical", files, True, None
    yield "equivalent", current_native_source(task, equivalent=True), True, None
    yield "missing", {}, False, None
    yield "wrong-selected-name", {file: re.sub(rf"\b{name}\b", "Other" + name, text)
                                  for file, text in files.items()}, False, None
    if task not in ("move-module", "move-deps"):
        yield "wrong-selected-access", native_replace(files, name, lambda text: text.replace(f"func {name} ", f"func {name} priv ", 1)), False, None
    header = next(re.search(rf"(?m)^  func {name} \(([^)]*)\)", text).group(1)
                  for text in files.values() if re.search(rf"(?m)^  func {name} \(", text))
    parameter = header.split(",")[0].split(":")[-1].lstrip("@")
    yield "wrong-parameter-identity", native_replace(files, name, lambda text: re.sub(
        rf"\b{re.escape(parameter)}\b", "wrongParameter", text)), False, None
    yield "wrong-selected-identity", files, False, name
    yield "wrong-module-identity", files, False, "module"
    body = '    Console.WriteLine(0)\n' if task == "contract-effect" else '    return ("")\n' if task == "sig-addparam" else "    return (0)\n"
    yield "wrong-independent-behavior", native_replace(files, name, lambda text: re.sub(r"(?m)^    (?:let |return |Console\.WriteLine)[\s\S]*", body, text)), False, None
    selected_file = next(text for text in files.values() if re.search(rf"(?m)^  func {name} \(", text))
    start, end = native_span(selected_file, name)
    selected = selected_file[start:end]
    if re.search(r"(?m)^    (?:requires |=> )", selected):
        yield "missing-selected-contract", native_replace(files, name, lambda text: re.sub(r"(?m)^    (?:requires |=> )[^\n]*\n", "", text)), False, None
        weaker = selected.replace(">= 0", ">= -1").replace("> 0", ">= 0").replace("!= 0", ">= 0")
        if weaker != selected:
            yield "weakened-selected-contract", native_replace(files, name, lambda _: weaker), False, None
    if task.startswith("extract-"):
        helper = {"extract-simple": "SumOfSquares", "extract-contracts": "ValidateIndex",
                  "extract-effects": "ComputeSum", "extract-nested": "Square"}[task]
        dead = {
            "extract-simple": "    SumOfSquares(x, y)\n    return (x * x + y * y)\n",
            "extract-contracts": "    ValidateIndex(index, length)\n    return (index >= 0 && index < length ? index : -1)\n",
            "extract-effects": '    ComputeSum(a, b)\n    Console.WriteLine("Computing sum")\n    return (a + b)\n',
            "extract-nested": "    Square(x)\n    Square(y)\n    Square(z)\n    return (x * x + y * y + z * z)\n",
        }[task]
        yield "dead-supplying-helper", native_replace(files, name, lambda text: re.sub(r"(?m)^    (?:return |Console\.WriteLine)[\s\S]*", dead, text)), False, None
        yield "wrong-helper-behavior", native_replace(files, helper, lambda text: re.sub(
            r"(?m)^    return [^\n]*", "    return (true)" if task == "extract-contracts" else "    return (0)", text)), False, None
        if task == "extract-effects":
            yield "impure-helper-row", native_replace(files, helper, lambda text: text.replace("\n", "\n    ! cw\n", 1)), False, None
    if task.startswith("inline-"):
        old = {"inline-simple": "Double(a) + Double(b)", "inline-multi": "Square(x) + Square(y)",
               "inline-contracts": "Clamp(score, 0, 100)"}[task]
        yield "not-inlined", native_replace(files, name, lambda text: re.sub(r"(?m)^    return [^\n]*", f"    return ({old})", text)), False, None
        if task == "inline-simple":
            yield "decoy-inlined-multiplications", native_replace(files, name, lambda text: re.sub(
                r"(?m)^    return [^\n]*", "    let int:discardedA = a * 2\n    let int:discardedB = b * 2\n    return (a + a + b + b)", text)), False, None
    if task == "sig-reorder":
        yield "stale-callsite-order", native_replace(files, "CountSteps", lambda text: text.replace("CreateRange(1, 0, max)", "CreateRange(0, max, 1)")), False, None
    if task.startswith("move-"):
        filename = "src/Utils.calr"
        start, end = native_span(files[filename], name)
        duplicate = dict(files)
        duplicate["src/Math.calr"] += files[filename][start:end]
        yield "duplicated-moved-declaration", duplicate, False, None


def archived_native_source(task, deadline=None):
    category = task.split("-")[0]
    fixture = f"refactor-{'signature' if category == 'sig' else category}-calor"
    files = {file: PROCESS.run(["git", "show", f"{OLD}:tests/E2E/agent-tasks/fixtures/{fixture}/{file}"],
                              ROOT, min(time.monotonic() + 30, deadline if deadline is not None else float("inf")))
             for file in native_source(task)}
    name = TASKS[task][1]
    filename = next(file for file, text in files.items() if re.search(rf"§F\{{f\d+:{name}:pub\}}", text))
    text = files[filename]

    def span(source, target):
        match = re.search(rf"(?m)^  §F\{{f\d+:{target}:pub\}}[^\n]*\n", source)
        if not match:
            raise AssertionError(f"Missing archived native declaration {target}")
        following = re.search(r"(?m)^  §F", source[match.end():])
        return match.start(), match.end() + following.start() if following else len(source)

    start, end = span(text, name)
    selected = text[start:end]
    helper = ""
    if task == "contract-pre":
        selected = selected.replace("    §R", "    §Q (>= x 0)\n    §R", 1)
    elif task == "contract-post":
        selected = selected.replace("    §R", "    §S (>= result 0)\n    §R", 1)
    elif task == "contract-bounds":
        selected = selected.replace("    §R", "    §Q (>= index 0)\n    §Q (< index length)\n    §Q (> length 0)\n    §R", 1)
    elif task == "contract-effect":
        selected = selected.replace("    §P", "    §E{cw}\n    §P", 1)
    elif task.startswith("extract-"):
        calls = {
            "extract-simple": "§C{SumOfSquares} §A x §A y §/C",
            "extract-contracts": "(? §C{ValidateIndex} §A index §A length §/C index -1)",
            "extract-effects": "§C{ComputeSum} §A a §A b §/C",
            "extract-nested": "(+ (+ §C{Square} §A x §/C §C{Square} §A y §/C) §C{Square} §A z §/C)",
        }
        selected = re.sub(r"(?m)^    §R[^\n]*", "    §R " + calls[task], selected)
        helper = {
            "extract-simple": "  §F{f005:SumOfSquares:pub} (i32:a, i32:b) -> i32\n    §R (+ (* a a) (* b b))\n",
            "extract-contracts": "  §F{f006:ValidateIndex:pub} (i32:index, i32:length) -> bool\n    §Q (>= length 0)\n    §S (== result (and (>= index 0) (< index length)))\n    §R (and (>= index 0) (< index length))\n",
            "extract-effects": "  §F{f007:ComputeSum:pub} (i32:a, i32:b) -> i32\n    §R (+ a b)\n",
            "extract-nested": "  §F{f008:Square:pub} (i32:n) -> i32\n    §S (>= result 0)\n    §R (* n n)\n",
        }[task]
    elif task.startswith("inline-"):
        expression = {"inline-simple": "(+ (* a 2) (* b 2))",
                      "inline-multi": "(+ (* x x) (* y y))",
                      "inline-contracts": "(? (< score 0) 0 (? (> score 100) 100 score))"}[task]
        selected = re.sub(r"(?m)^    §B[^\n]*\n", "", selected)
        selected = re.sub(r"(?m)^    §R[^\n]*", "    §R " + expression, selected)
    elif category == "rename":
        old, new = ("val", "value") if task == "rename-param" else ("num", "value") if task == "rename-contract" else ("x", "input")
        selected = re.sub(rf"\b{old}\b", new, selected)
    elif task == "sig-addparam":
        selected = selected.replace("(str:name)", "(str:greeting, str:name)").replace("§R name", "§R (+ greeting name)")
    elif task == "sig-return":
        selected = selected.replace("-> i32", "-> Option<i32>").replace("§R 0", "§R §NN")
    elif task == "sig-reorder":
        selected = selected.replace("(i32:start, i32:end, i32:step)", "(i32:step, i32:start, i32:end)")
        text = text.replace("§C{CreateRange} §A 0 §A max §A 1", "§C{CreateRange} §A 1 §A 0 §A max")
    else:
        files[filename] = text[:start] + text[end:]
        files["src/Utils.calr"] += "\n" + selected
        return files
    files[filename] = text[:start] + selected + text[end:] + helper
    return files


def archived_native_wrong_body(task, files):
    name = TASKS[task][1]
    changed = dict(files)
    filename = next(file for file, text in files.items() if re.search(rf"§F\{{f\d+:{name}:pub\}}", text))
    source = files[filename]
    start = re.search(rf"(?m)^  §F\{{f\d+:{name}:pub\}}", source).start()
    following = re.search(r"(?m)^  §F", source[start + 3:])
    end = start + 3 + following.start() if following else len(source)
    selected = source[start:end]
    if task == "contract-effect":
        selected = selected.replace("§A x §/C", "§A 0 §/C")
    else:
        value = '""' if task == "sig-addparam" else "§SM 1" if task == "sig-return" else "0"
        selected = re.sub(r"(?m)^    §R[^\n]*", "    §R " + value, selected)
    if selected == source[start:end]:
        raise AssertionError("Empty archived negative edit")
    changed[filename] = source[:start] + selected + source[end:]
    return changed


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


def execute(task, category, files, expected, root=ROOT, deadline=None, language="csharp", environment=None, blocker=False,
            current_native=False, sidecar_change=None):
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
            content = current_native_sidecar(task, files, deadline) if current_native else native_sidecar(task, files, deadline)
            if sidecar_change:
                document = json.loads(content)
                candidates = [entry for entry in document["Declarations"] if
                              entry["Kind"] == 0] if sidecar_change == "module" else [
                                  entry for entry in document["Declarations"] if entry["Name"] == sidecar_change]
                if not candidates or sidecar_change != "module" and len(candidates) != 1:
                    raise AssertionError("Ambiguous native identity control")
                candidates[0]["Id"] = "m099" if sidecar_change == "module" else "f099"
                content = json.dumps(document)
            sidecar.write_text(content, encoding="utf-8")
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
                with CENSUS_LOCK:
                    EXECUTED["prerequisite"] += 1
                print(f"{task}/{category}: native positive BLOCKED\n{output}")
                return output
            if not any(marker in output for marker in REJECTIONS):
                raise
            with CENSUS_LOCK:
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
        counts = NATIVE_COUNTS if language == "calor" else COUNTS
        if f"; {counts[task]} runtime cases;" not in output or not re.search(r"; [1-9]\d* successful source counterfactuals\.", output):
            raise AssertionError(f"Wrong exact benchmark runtime census or empty intervention evidence:\n{output}")
        with CENSUS_LOCK:
            EXECUTED["accepted"] += 1
        if language == "calor":
            for line in output.splitlines():
                if line.startswith(("Actual native AST identities:", "Actual native namespace topology:", "Native benchmark selected proof floor passed:",
                                    "Actual selected native proof coverage", "Actual native cross-module diagnostic:")):
                    print(f"{task}/{category}: {line}")
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


def run_native_compatibility(old, current, deadline):
    before = dict(EXECUTED)
    for arm, root in (("v0.29", old), ("selected-current", current)):
        def verify(task):
            if task == "rename-shadow" or arm == "selected-current" and task == "sig-return":
                execute(task, f"{arm}-unadmitted-original-requirement",
                        archived_native_source(task, deadline) if arm == "v0.29" else current_native_source(task),
                        False, root, deadline, language="calor", current_native=arm != "v0.29", blocker=True)
                return
            files = archived_native_source(task, deadline) if arm == "v0.29" else current_native_source(task)
            execute(task, f"{arm}-native-canonical", files, True, root, deadline,
                    language="calor", current_native=arm != "v0.29")
            wrong = archived_native_wrong_body(task, files) if arm == "v0.29" else next(
                files for category, files, _, _ in native_controls(task) if category == "wrong-independent-behavior")
            execute(task, f"{arm}-native-wrong-behavior", wrong, False, root, deadline,
                    language="calor", current_native=arm != "v0.29")
        # Arms stay sequential; each worker builds its own helper and uses the same absolute deadline.
        with ThreadPoolExecutor(max_workers=2) as workers:
            for _ in workers.map(verify, TASKS):
                pass
    if EXECUTED != dict(accepted=before["accepted"] + 37, rejected=before["rejected"] + 37,
                        prerequisite=before["prerequisite"] + 3):
        raise AssertionError("Incomplete genuine native benchmark old/current compatibility census.")
    print("Native compatibility passed: 77 actual entries (37 accepted, 37 substantive rejections, 3 prerequisite refusals); "
          "archived exact Option/None admitted, current exact signature unadmitted; v001 unadmitted in both arms.")


class BenchmarkTests(unittest.TestCase):
    native_control_root = ROOT

    def test_actual_current_native_controls(self):
        before = dict(EXECUTED)
        expected = {"accepted": 0, "rejected": 0}
        planned = [accepted for task in TASKS if task not in NATIVE_UNADMITTED
                   for _, _, accepted, _ in native_controls(task)]
        self.assertEqual((planned.count(True), planned.count(False)), (36, 164))
        root = self.native_control_root
        for task in TASKS:
            if task in NATIVE_UNADMITTED:
                execute(task, "unadmitted-original-requirement", current_native_source(task), False,
                        root=root, language="calor", current_native=True, blocker=True)
                continue
            for category, files, accepted, identity in native_controls(task):
                if category == "canonical":
                    execute(task, "native-" + category, files, accepted, root=root, language="calor",
                            current_native=True, sidecar_change=identity)
                else:
                    with self.subTest(task=task, category=category):
                        execute(task, "native-" + category, files, accepted, root=root, language="calor",
                                current_native=True, sidecar_change=identity)
                expected["accepted" if accepted else "rejected"] += 1
        self.assertEqual(EXECUTED, dict(accepted=before["accepted"] + expected["accepted"],
                                       rejected=before["rejected"] + expected["rejected"],
                                       prerequisite=before["prerequisite"] + 2))
        self.assertEqual(sum(expected.values()), len(planned))
        print(f"Selected native root {root} census: {expected}; 18 profiles, 2 original requirements unadmitted.")

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
        print("Baseline native census: 19 literal-ID refusals and 1 absent v001 lineage; 0 admitted behavioral profiles.")

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
            "Verifier command exited 1:\nNative benchmark identity anchor prerequisite refused: Ambiguous declaration identity",
        ):
            with self.subTest(failure=failure), patch.object(PROCESS, "run", side_effect=RuntimeError(failure)):
                with self.assertRaises(RuntimeError):
                    execute("contract-post", "wrong-behavior", source("contract-post"), False)
        self.assertEqual(EXECUTED, before)

    def test_mock_native_asset_recheck_preserves_outer_deadline(self):
        before = dict(EXECUTED)
        for outer, expected in ((None, 1120), (2000, 1120), (1050, 1050)):
            calls = []

            def run(command, root, deadline):
                calls.append((command, deadline))
                if command[:2] == ["git", "archive"]:
                    Path(next(value[9:] for value in command if value.startswith("--output="))).write_bytes(b"mock archive")
                if command[:2] == ["dotnet", "build"]:
                    image = root / "src/Calor.Compiler/bin" / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
                    image.mkdir(parents=True)
                    for name in ("calor.dll", "Calor.Runtime.dll", "Microsoft.Z3.dll"):
                        (image / name).write_bytes(b"mock image")
                return ""

            with self.subTest(outer=outer), patch.object(PROCESS, "run", side_effect=run), \
                    patch.object(tarfile, "open"), patch.object(shutil, "copytree"), \
                    patch("builtins.print"), patch.object(time, "monotonic", return_value=100) as clock:
                with source_built_native(INTERIM_NATIVE_COMMIT, outer):
                    clock.return_value = 1000
                self.assertEqual(calls[0][1], 700)
                self.assertEqual(calls[-1][1], expected)
                self.assertEqual(calls[-1][0], [sys.executable, "scripts/verify-z3-assets.py"])
        self.assertEqual(EXECUTED, before)

    def test_mock_native_lane_uses_selected_root_and_stops_before_adversaries(self):
        before = dict(EXECUTED)
        with patch(__name__ + ".execute", side_effect=RuntimeError("selected-root prerequisite refused")) as verify, \
                patch(__name__ + ".source_built_native", side_effect=AssertionError("No implicit source substitution")):
            with self.assertRaisesRegex(RuntimeError, "^selected-root prerequisite refused$"):
                BenchmarkTests("test_actual_current_native_controls").test_actual_current_native_controls()
        verify.assert_called_once()
        self.assertEqual(verify.call_args.kwargs["root"], ROOT)
        self.assertEqual(verify.call_args.args[0:2], ("contract-pre", "native-canonical"))
        self.assertEqual(EXECUTED, before)

    def test_native_authoring_retains_original_namespace_and_interim_body(self):
        before = dict(EXECUTED)
        for task in TASKS:
            for equivalent in (False, True):
                current = current_native_source(task, equivalent=equivalent)
                with patch(__name__ + ".INTERIM_NATIVE_AUTHORING", True):
                    interim = current_native_source(task, equivalent=equivalent)
                self.assertEqual(current.keys(), interim.keys())
                for file, text in current.items():
                    header, body = text.split("\n", 1)
                    keyword, name, namespace = header.split()
                    self.assertEqual((keyword, namespace), ("module", name))
                    self.assertEqual(interim[file], f"module {name}\n{body}")
        self.assertEqual(EXECUTED, before)


if __name__ == "__main__":
    if sys.argv[1:] == ["--interim-native"]:
        print(f"Explicit interim lane only: {INTERIM_NATIVE_COMMIT}; not selected-root or final-parent evidence.")
        INTERIM_NATIVE_AUTHORING = True
        with source_built_native(INTERIM_NATIVE_COMMIT) as selected:
            BenchmarkTests.native_control_root = selected
            result = unittest.main(argv=[sys.argv[0], "BenchmarkTests.test_actual_current_native_controls"], exit=False).result
        sys.exit(not result.wasSuccessful())
    unittest.main()
