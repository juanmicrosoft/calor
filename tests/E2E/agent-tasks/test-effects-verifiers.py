#!/usr/bin/env python3
"""Six original effects prompts, actual entrypoints, no agent or host IO."""

import importlib.util
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
from threading import Lock
import unittest
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {
    "eff001": ("01_console_write", "SayHello", "str:name", "void", "cw",
               "§C{System.Console.WriteLine} §A name §/C"),
    "eff002": ("02_console_read", "ReadInput", "", "str", "cr,throw",
               "§B{line:?str} §C{System.Console.ReadLine} §/C\n"
               "§IF (!= line null)\n  §R line\n"
               '§TH "Outside supplied non-null line controls"'),
    "eff003": ("03_file_read", "ReadFile", "str:path", "str", "fs:r",
               "§R §C{System.IO.File.ReadAllText} §A path §/C"),
    "eff004": ("04_file_write", "WriteFile", "str:path, str:content", "void", "fs:w",
               "§C{System.IO.File.WriteAllText} §A path §A content §/C"),
    "eff005": ("05_network_effect", "FetchUrl", "str:url", "str", "net:r",
               '§R (+ "Response from: " url)'),
    "eff006": ("06_multiple_effects", "LogToFileAndConsole", "str:path, str:message", "void", "cw,fs:w",
               "§C{System.Console.WriteLine} §A message §/C\n"
               "§C{System.IO.File.AppendAllText} §A path §A message §/C"),
}
COUNTS = {"eff001": 16, "eff002": 12, "eff003": 128, "eff004": 128, "eff005": 16, "eff006": 128}
# Independent native codecs keep these deliberate decoys compilable without
# adding an Encoding effect manifest or changing compiler behavior.
DECODE = """  §F{Decode:priv} (byte[]:bytes) -> str
    §B{~text:str} ""
    §B{~i:i32} 0
    §WH (< i (len bytes))
      §B{first:i32} (cast i32 §IDX bytes i)
      §ASSIGN i (+ i 1)
      §B{~scalar:i32} first
      §B{~count:i32} 0
      §IF (>= first 240)
        §ASSIGN scalar (% first 8)
        §ASSIGN count 3
      §EI (>= first 224)
        §ASSIGN scalar (% first 16)
        §ASSIGN count 2
      §EI (>= first 192)
        §ASSIGN scalar (% first 32)
        §ASSIGN count 1
      §WH (> count 0)
        §ASSIGN scalar (+ (* scalar 64) (% (cast i32 §IDX bytes i) 64))
        §ASSIGN i (+ i 1)
        §ASSIGN count (- count 1)
      §IF (> scalar 65535)
        §ASSIGN scalar (- scalar 65536)
        §ASSIGN text (+ text (cast char (+ 55296 (/ scalar 1024))))
        §ASSIGN text (+ text (cast char (+ 56320 (% scalar 1024))))
      §EL
        §ASSIGN text (+ text (cast char scalar))
    §R text
"""
ENCODE = """  §F{Encode:priv} (str:text) -> byte[]
    §E{mut,alloc}
    §LIST{bytes:byte}
    §/LIST
    §B{~i:i32} 0
    §WH (< i (len text))
      §B{~scalar:i32} (cast i32 (char-at text i))
      §ASSIGN i (+ i 1)
      §IF (&& (>= scalar 55296) (<= scalar 56319))
        §ASSIGN scalar (+ (+ 65536 (* (- scalar 55296) 1024)) (- (cast i32 (char-at text i)) 56320))
        §ASSIGN i (+ i 1)
      §IF (>= scalar 65536)
        §PUSH{bytes} (cast byte (+ 240 (/ scalar 262144)))
        §PUSH{bytes} (cast byte (+ 128 (% (/ scalar 4096) 64)))
        §PUSH{bytes} (cast byte (+ 128 (% (/ scalar 64) 64)))
        §PUSH{bytes} (cast byte (+ 128 (% scalar 64)))
      §EI (>= scalar 2048)
        §PUSH{bytes} (cast byte (+ 224 (/ scalar 4096)))
        §PUSH{bytes} (cast byte (+ 128 (% (/ scalar 64) 64)))
        §PUSH{bytes} (cast byte (+ 128 (% scalar 64)))
      §EI (>= scalar 128)
        §PUSH{bytes} (cast byte (+ 192 (/ scalar 64)))
        §PUSH{bytes} (cast byte (+ 128 (% scalar 64)))
      §EL
        §PUSH{bytes} (cast byte scalar)
    §R §C{bytes.ToArray} §/C
"""


def load(name, filename):
    specification = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


ENGINE = load("effects_shared_engine", "lib/verify-logic.py")


def historical_syntax(text):
    translated = load("effects_old_syntax", "test-contract-writing-verifiers.py").historical_syntax(text)
    lines = []
    serial = 0
    for line in translated.splitlines():
        if line.lstrip().startswith("§WH "):
            serial += 1
            line = line.replace("§WH ", f"§WH{{effects_wh{serial}}} ", 1)
        lines.append(line)
    # Only this disposable byte-codec list needs the released matching closer.
    return (("\n".join(lines) + "\n").replace("§/LIST\n", "§/LIST{bytes}\n")
            .replace("§LAM{static:value:str}", "§LAM{effects_local:static:value:str}")
            .replace("§/LAM\n", "§/LAM{effects_local}\n"))


def local_cases(historical=False):
    raw = "Raw interop cannot establish native effects behavior."
    retired = "Local §F declarations are removed. Use a §B binding with §LAM instead."
    # The archived parser retains LocalFunctionStatementNode; the current parser
    # explicitly rejects nested §F before an AST can reach the native scan.
    for body in ('§RAW return "raw"; §/RAW', '§R §CS{"raw"}'):
        yield (source("eff005", body="§F{Local} (str:value) -> str\n  " + body
                      + '\n§R (+ "Response from: " url)'), False, raw if historical else retired)
        yield (source("eff005", body="§F{Local} (str:value) -> str\n"
                      "  §F{Nested} (str:item) -> str\n    " + body
                      + '\n  §R value\n§R (+ "Response from: " url)'),
               False, raw if historical else retired)
    native = source("eff005", body="§F{Local} (str:value) -> str\n  §R value\n"
                    '§R (+ "Response from: " §C{Local} §A url §/C)')
    yield (native, historical, None if historical else retired)
    lambda_body = "§B{Local:Func<str,str>} §LAM{static:value:str}\n  §R value\n§/LAM\n"
    yield (source("eff005", body=lambda_body + '§R (+ "Response from: " §C{Local} §A url §/C)'),
           True, None)
    for body in ('§RAW return "raw"; §/RAW', '§R §CS{"raw"}'):
        yield (source("eff005", body="§B{Local:Func<str,str>} §LAM{static:value:str}\n  "
                      + body + '\n§/LAM\n§R (+ "Response from: " url)'), False, raw)


def source(profile, *, body=None, effects=None, signature=None):
    _, name, parameters, output, required, original = TASKS[profile]
    declaration = signature or f"§F{{{name}:pub}} ({parameters}) -> {output}"
    lines = ["§E{" + (required if effects is None else effects) + "}"]
    lines += (original if body is None else body).splitlines()
    return "§M{Effects}\n  " + declaration + "\n" + "".join("    " + line + "\n" for line in lines)


def cases(profile):
    _, name, parameters, output, required, original = TASKS[profile]
    signature = f"§F{{{name}:pub}} ({parameters}) -> {output}"
    wrong = '§R "constant"' if output == "str" else ""
    unrelated = source(profile).replace("§M{Effects}\n", "").replace(f"§F{{{name}:", "§F{Unrelated:")
    equivalent = original
    if profile == "eff002":
        equivalent = original.replace("line", "supplied")
    elif output == "str":
        equivalent = original.replace("§R ", "§B{value:str} ", 1) + "\n§R value"
    else:
        parameter = "name" if profile == "eff001" else "content" if profile == "eff004" else "message"
        equivalent = "§B{value:str} " + parameter + "\n" + original.replace("§A " + parameter, "§A value")
    controls = [
        (source(profile), True, None),
        (source(profile, body=equivalent, effects=required + ",mut"), True, None),
        (source(profile, signature=signature.replace("str:", "string:").replace("-> str", "-> string")),
         True, None),
        (source(profile, effects=""), False, "Original declared effects missing"),
        (source(profile, body=wrong), False, None),
        (source(profile, signature=signature.replace(":pub", ":priv")), False, "Wrong public effects signature"),
        (source(profile, signature=signature.replace("-> " + output, "-> object")), False,
         "Wrong public effects signature"),
        (source(profile, effects="", body=wrong) + unrelated, False, "Original declared effects missing"),
        ("// " + source(profile).replace("\n", " ") + "\n§M{Effects}\n", False, "Expected exactly one"),
        (source(profile) + unrelated.replace("§F{Unrelated:", f"§F{{{name}:"), False, None),
    ]
    if parameters:
        controls.append((source(profile, signature=signature.replace("str:", "i32:", 1), body=wrong),
                         False, "Wrong public effects signature"))
    if profile == "eff002":
        controls += [
            (source(profile, body='§B{unused:?str} §C{System.Console.ReadLine} §/C\n§R "constant"'),
             False, "Wrong instrumented effects baseline"),
            (source(profile, body='§B{unused:?str} §C{System.Console.ReadLine} §/C\n'
                    '§R §C{ReadLine} §/C') + '  §F{ReadLine:priv} () -> str\n    §R "constant"\n',
             False, None),
        ]
    if profile == "eff003":
        controls.append((source(profile, body='§B{unused:str} §C{System.IO.File.ReadAllText} §A path §/C\n'
                               '§R "constant"'), False, "Wrong instrumented effects baseline"))
        controls += [
            (source(profile, body='§B{unused:str} §C{System.IO.File.ReadAllText} §A path §/C\n'
                    '§R §C{Decode} §A §C{System.IO.File.ReadAllBytes} §A path §/C §/C') + DECODE,
             False, "Effects coupling failed: ReadAllText"),
            (source(profile, body='§B{value:str} §C{System.IO.File.ReadAllText} §A path §/C\n'
                    '§IF (== value "counterfactual file content")\n  §TH "mutant failed"\n§R value', effects="fs:r,throw"),
             False, "Effects counterfactual runtime failed; exceptions are not dependence evidence"),
        ]
    if profile == "eff004":
        controls.append((source(profile, effects="fs:w,mut,alloc", body=original + "\n"
                         '§C{System.IO.File.WriteAllBytes} §A path §A §C{Encode} §A content §/C §/C') + ENCODE,
                         False, "Effects coupling failed: WriteAllText"))
    if profile == "eff005":
        controls += [
            (source(profile, body='§R (+ url "Response from: ")'), False, "Wrong instrumented effects baseline"),
            (source(profile, body='§R (+ "Response from: " "constant")'), False, "Wrong instrumented effects baseline"),
            (source(profile, body='§C{System.Console.WriteLine} §A url §/C\n' + original, effects="net:r,cw"),
             False, "FetchUrl is a simulation"),
        ]
    if profile == "eff006":
        controls += [
            (source(profile, effects="cw"), False, "Original declared effects missing"),
            (source(profile, effects="fs:w"), False, "Original declared effects missing"),
            (source(profile, body=original.replace("AppendAllText", "WriteAllText")), False,
             "Required resolved BCL effects operation missing"),
            (source(profile, body=original.replace("§A message", '§A "constant"')), False,
             "Effects BCL provenance/arguments"),
            (source(profile, effects="cw,fs:rw,mut,alloc", body=
                    '§B{initial:str} §C{System.IO.File.ReadAllText} §A path §/C\n' + original + "\n"
                    '§C{System.IO.File.WriteAllBytes} §A path §A §C{Encode} '
                    '§A (+ initial message) §/C §/C') + ENCODE,
             False, "Effects coupling failed: AppendAllText"),
        ]
    return controls


class EffectsVerifierTests(unittest.TestCase):
    count_lock = Lock()
    entry_points_executed = 0
    positives = 0
    negatives = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real effects verify.sh controls: "
              f"{cls.positives} positive, {cls.negatives} rejected.")

    def verify(self, profile, text, expected=True, *, env=None, detail=None, deadline=None,
               allow_timeout=False):
        if allow_timeout and expected:
            raise ValueError("Timeout rejection must be an explicitly negative bounded-timeout control.")
        with tempfile.TemporaryDirectory(prefix="calor-effects-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / "Effects.calr").write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            command = [str(HERE / "tasks/effects-system" / TASKS[profile][0] / "verify.sh"), str(workspace)]
            deadline = min(deadline or time.monotonic() + 120, time.monotonic() + 120)
            budget = float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))
            if 0 < budget <= 120:
                # Give the nested helper time to terminate its own process groups.
                remaining = deadline - time.monotonic() - 1
                if remaining <= 0:
                    raise RuntimeError("Effects entrypoint supervision deadline expired.")
                environment["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(budget, remaining))
            try:
                output = ENGINE.run(command, HERE, deadline, env=environment)
                passed = True
            except RuntimeError as error:
                output = str(error)
                if ("Verifier cleanup" in output or "Cannot terminate verifier process group" in output
                    or "EOF" in output):
                    raise
                timeout = "Verifier timeout" in output
                if timeout:
                    if not allow_timeout:
                        raise
                elif not output.startswith("Verifier command exited"):
                    raise
                passed = False
            with type(self).count_lock:
                type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if expected:
                selected = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
                configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
                identity = f"Selected compiler: {selected / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
                self.assertEqual([line for line in output.splitlines() if line.startswith("Selected compiler: ")],
                                 [identity], output)
                self.assertIn(f"Verification passed: {TASKS[profile][1]}; original declared effects, native/CLR signatures, "
                              f"{COUNTS[profile]} runtime cases and BCL output/state coupling.", output)
                self.assertIn("process supervision, not a sandbox", output)
                with type(self).count_lock:
                    type(self).positives += 1
            else:
                with type(self).count_lock:
                    type(self).negatives += 1
            if detail:
                self.assertIn(detail, output)
            return output

    def test_original_prompt_controls(self):
        for profile in TASKS:
            for text, expected, detail in cases(profile):
                with self.subTest(profile=profile, expected=expected, detail=detail):
                    self.verify(profile, text, expected, detail=detail)

    def test_native_local_helpers_and_nested_raw(self):
        for text, expected, detail in local_cases():
            with self.subTest(expected=expected, detail=detail):
                self.verify("eff005", text, expected, detail=detail)

    def test_supervision_failures_are_not_finite_rejections(self):
        before = type(self).entry_points_executed
        errors = [
            "Verifier timeout: overall deadline expired.",
            "Verifier cleanup timeout: child did not exit by the deadline.",
            "EOF supervision failure",
            "Verifier command exited 1:\nVerification failed: Verifier timeout after 2s: dotnet",
            "Verifier command exited 1:\nVerifier cleanup timeout: inherited pipe did not close.",
            "Verifier command exited 1:\nCannot terminate verifier process group 123",
        ]
        for error in errors:
            with self.subTest(error=error), patch.object(ENGINE, "run", side_effect=RuntimeError(error)):
                with self.assertRaisesRegex(RuntimeError, "timeout|EOF|Cannot terminate"):
                    self.verify("eff005", source("eff005"), False)
        configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
        selected = COMPILER_ROOT / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
        success = (f"Selected compiler: {selected}\nVerification passed: FetchUrl; original declared effects, "
                   "native/CLR signatures, 16 runtime cases and BCL output/state coupling.\n"
                   "Limits: process supervision, not a sandbox\n")
        invalid = [
            success.replace(str(selected), str(selected) + ".other"),
            success.replace(str(selected), str(selected.parent.parent.parent / "wrong-configuration/net10.0/calor.dll")),
            success.replace(f"Selected compiler: {selected}\n", ""),
            f"Selected compiler: {selected}\n" + success,
        ]
        for output in invalid:
            with patch.object(ENGINE, "run", return_value=output):
                with self.assertRaises(AssertionError):
                    self.verify("eff005", source("eff005"))
        # Synthetic supervisor checks are not real entrypoint controls.
        type(self).entry_points_executed = before

    def test_independent_provenance_and_coupling(self):
        self.verify("eff001", source("eff001", body="§C{WriteLine} §A name §/C") +
                    "  §F{WriteLine:priv} (str:value) -> void\n", False,
                    detail="Required resolved BCL effects operation missing")
        self.verify("eff003", source("eff003", body="§R §C{ReadAllText} §A path §/C") +
                    '  §F{ReadAllText:priv} (str:path) -> str\n    §R path\n', False,
                    detail="Required resolved BCL effects operation missing")
        self.verify("eff002", source("eff002", body='§B{unused:?str} §C{System.Console.ReadLine} §/C\n'
                    '§R "alpha"'), False)
        self.verify("eff004", source("eff004", body='§C{System.IO.File.WriteAllText} §A path §A "constant" §/C'),
                    False, detail="Effects BCL provenance/arguments")
        self.verify("eff006", source("eff006", body='§C{System.Console.WriteLine} §A message §/C\n'
                    '§C{System.IO.File.AppendAllText} §A path §A "" §/C'), False,
                    detail="Effects BCL provenance/arguments")
        self.verify("eff003", source("eff003", body='§R §C{System.IO.File.ReadAllText} §A (+ path ".other") §/C'),
                    False, detail="Effects probe refused non-owned path")
        self.verify("eff004", source("eff004", body='§C{System.IO.File.WriteAllText} §A (+ path ".other") §A content §/C'),
                    False, detail="Effects probe refused non-owned path")
        # Correct baseline, but the read only controls a tautological successful branch.
        self.verify("eff002", source("eff002", body='§B{read:?str} §C{System.Console.ReadLine} §/C\n'
                    '§IF (!= read null)\n  §IF (>= (len read) 0)\n    §R §C{Independent} §/C\n§R ""') +
                    '  §F{Independent:priv} () -> str\n    §R "constant"\n', False)
        self.verify("eff001", source("eff001", body='§IF false\n  ' + TASKS["eff001"][5] + "\n"
                    '§C{System.Console.Write} §A (+ name "\\n") §/C'),
                    False, detail="Effects BCL provenance/arguments")
        self.verify("eff004", source("eff004", body='§C{System.IO.File.WriteAllText} '
                    '§A §C{path.Replace} §A "first é" §A "second 中" §/C §A content §/C'),
                    False, detail="Effects BCL provenance/arguments")
        self.verify("eff006", source("eff006", body=TASKS["eff006"][5] + "\n"
                    '§C{System.IO.File.WriteAllText} §A path §A message §/C'), False,
                    detail="Wrong instrumented effects baseline")

    def test_helper_and_encoding_equivalents(self):
        self.verify("eff001", source("eff001", body="§C{WriteLine} §A name §/C") +
                    "  §F{WriteLine:priv} (str:value) -> void\n    §E{cw}\n"
                    "    §C{System.Console.WriteLine} §A value §/C\n")
        self.verify("eff003", source("eff003", body="§R §C{ReadAllText} §A path §/C") +
                    "  §F{ReadAllText:priv} (str:input) -> str\n    §E{fs:r}\n"
                    "    §R §C{System.IO.File.ReadAllText} §A input §/C\n")
        for profile in ("eff003", "eff004", "eff006"):
            body = TASKS[profile][5].splitlines()
            body[-1] = body[-1].replace(" §/C", " §A System.Text.Encoding.UTF8 §/C")
            self.verify(profile, source(profile, body="\n".join(body)))
        for profile in TASKS:
            self.verify(profile, source(profile, body="§Q true\n§S true\n" + TASKS[profile][5]))
        self.verify("eff005", source("eff005", body='§R (sb-tostring (sb-append '
                    '(sb-append (sb-new) "Response from: ") url))', effects="net:rw,mut"))
        for profile in ("eff003", "eff004", "eff006"):
            self.verify(profile, source(profile, effects="cw,fs:rw"))

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
        ]
        executed = 0
        for filename, classname, table in profiles:
            module = load("effects_preserved_" + classname, filename)
            test = getattr(module, classname)()
            for profile in getattr(module, table):
                test.verify(profile, module.source(profile))
                executed += 1
        self.assertEqual(executed, 34)
        output = ENGINE.run([sys.executable, str(HERE / "lib/verify-logic.py"), "--check-native"],
                            HERE, time.monotonic() + 120,
                            env=dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT),
                                     CALOR_LOGIC_TIMEOUT_SECONDS="119"))
        self.assertIn("Selected native Z3 prerequisite passed: SATISFIABLE.", output)
        configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
        self.assertEqual([line for line in output.splitlines() if line.startswith("Selected compiler: ")],
                         [f"Selected compiler: {COMPILER_ROOT / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"])
        print("Executed 34 preserved profile entrypoints and 1 selected native-only entrypoint.")

    def test_failure_guards(self):
        for profile in TASKS:
            self.verify(profile, None, False, detail="Input missing")
        self.verify("eff001", "invalid source", False, detail="Calor compilation failed")
        self.verify("eff005", source("eff005"), False, env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"},
                    detail="timeout", allow_timeout=True)
        self.verify("eff005", source("eff005", body='§WH true\n  §CN\n§R ""'), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "5"}, detail="timeout", allow_timeout=True)
        self.verify("eff005", source("eff005"), False, env={"CALOR_COMPILER_CONFIGURATION": "../Debug"},
                    detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("eff002", source("eff002", body="§R §C{System.Console.ReadLine} §/C"), False,
                    detail="Calor0273")
        with tempfile.TemporaryDirectory(prefix="calor-effects-native-arm-") as temporary:
            arm = Path(temporary)
            self.verify("eff005", source("eff005"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid native asset")
            for profile in TASKS:
                self.verify(profile, source(profile), False, env={"CALOR_COMPILER_ROOT": str(arm)})


def run_compatibility(old, current, deadline):
    test = EffectsVerifierTests()
    # Keep the genuine two-arm subset bounded; the current suite runs every case.
    controls = []
    for profile in TASKS:
        for index, (text, expected, detail) in enumerate(cases(profile)):
            if (index in (0, 1, 2, 3, 4, 5, 7, 8)
                or detail and ("Effects coupling" in detail or "counterfactual runtime" in detail
                               or detail == "Required resolved BCL effects operation missing"
                               or detail == "FetchUrl is a simulation")
                or profile == "eff006" and detail in ("Original declared effects missing", "Effects BCL provenance/arguments")
                or profile in ("eff002", "eff003") and detail == "Wrong instrumented effects baseline"
                or profile == "eff005" and '§R (+ url "Response from: ")' in text):
                controls.append((profile, text, expected, detail))
    if len(controls) != 60:
        raise RuntimeError(f"Expected exactly 60 bounded compatibility cases, observed {len(controls)}.")
    controls.sort(key=lambda case: not (case[3] and ("Effects coupling" in case[3] or "counterfactual runtime" in case[3])))
    def execute(item):
        arm, root, historical, profile, text, expected, detail, local = item
        remaining = min(120, deadline - time.monotonic())
        if remaining <= 0:
            raise RuntimeError("Effects compatibility exceeded its 600-second deadline.")
        test.verify(profile, historical_syntax(text) if historical else text, expected,
                    env={"CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                    detail=detail, deadline=min(deadline, time.monotonic() + 120))
        verdict = "passed" if expected else detail if local else "rejected"
        return f"{arm} {profile}{' local' if local else ''}: {verdict}"

    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        batch = [(arm, root, historical, "eff005", text, expected, detail, True)
                 for text, expected, detail in local_cases(historical)]
        batch += [(arm, root, historical, profile, text, expected, detail, False)
                  for profile, text, expected, detail in controls]
        # Every worker launches a real verify.sh with its own disposable workspace
        # and the same absolute total deadline; no compiler/helper cache is reused.
        with ThreadPoolExecutor(max_workers=2) as workers:
            for verdict in workers.map(execute, batch):
                print(verdict)
    test.verify("eff005", historical_syntax(source("eff005")), False,
                env={"CALOR_COMPILER_ROOT": str(current)},
                detail="Calor compilation failed", deadline=min(deadline, time.monotonic() + 120))
    # Both genuine native APIs reject the nullable BCL return without a guard.
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        text = source("eff002", body="§R §C{System.Console.ReadLine} §/C")
        remaining = min(120, deadline - time.monotonic())
        if remaining <= 0:
            raise RuntimeError("Effects compatibility exceeded its 600-second deadline.")
        test.verify("eff002", historical_syntax(text) if historical else text, False,
                    env={"CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                    detail="Calor0273", deadline=min(deadline, time.monotonic() + 120))
        print(f"{arm} eff002 direct nullable BCL return: Calor0273 rejection")
    expected_count = 139
    if EffectsVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError(f"Expected exactly {expected_count} actual old/current effects controls.")
    print(f"Compatibility passed: {expected_count} actual old/current effects controls: "
          f"{test.positives} positive, {test.negatives} rejected.")


if __name__ == "__main__":
    unittest.main(verbosity=2)
