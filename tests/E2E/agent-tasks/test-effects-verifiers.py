#!/usr/bin/env python3
"""Six original effects prompts, actual entrypoints, no agent or host IO."""

import importlib.util
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import re
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

# Same finite-admission boundaries as test-async-verifiers.py (8cc800185).
INFRASTRUCTURE = re.compile(
    r"\b(?:EOF|cleanup|OSError|FileNotFoundError|PermissionError|TimeoutError|Traceback|errno"
    r"|DllNotFoundException|BadImageFormatException|Win32Exception|Z3Exception)\b"
    r"|inherited (?:pipe|stdout)|broken pipe|cannot terminate|process group"
    r"|permission denied|no such file|(?:failed|unable) to (?:launch|start)|could not (?:launch|start)"
    r"|launch failure|OS error|operating.system error"
    r"|\b(?-i:UNKNOWN)\b|solver[^\n]*(?:unknown|timeout|timed out)|(?:unknown|timeout)[^\n]*solver",
    re.IGNORECASE,
)
TIMEOUT = re.compile(r"timeout|timed out|deadline", re.IGNORECASE)
EFFECT_LIMITS = ("Limits: finite non-null strings and owned IO, deterministic restored console; "
                 "process supervision, not a sandbox or universal body proof. "
                 "No real network, EOF/null, missing-file or encoding policy.")


def control_failure(error, *, allow_timeout=False):
    output = str(error)
    wrappers = list(re.finditer(r"Verifier command exited\b", output))
    explicit_timeout = re.search(r"Verifier timeout after (?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?s: \S+", output)
    if (INFRASTRUCTURE.search(output)
        or re.search(r"\bdeadline\b", output, re.IGNORECASE)
        or any(not re.match(r"Verifier command exited 1:\n", output[wrapper.start():]) for wrapper in wrappers)
        or "Verification passed:" in output):
        raise error
    if TIMEOUT.search(output) and not (allow_timeout and explicit_timeout):
        raise error
    if not re.match(r"Verifier command exited 1:\n", output) and not (allow_timeout and explicit_timeout):
        raise error
    return output


def assert_selected_compiler(test, output, environment):
    # The unchanged engine's exact success disclaimer names EOF as untested.
    # Do not exempt arbitrary Limits-prefixed diagnostics or appended failures.
    diagnostics = "\n".join(line for line in output.splitlines() if line != EFFECT_LIMITS)
    if (INFRASTRUCTURE.search(diagnostics) or TIMEOUT.search(diagnostics)
        or "Verifier command exited" in output or "Verification failed:" in output):
        raise AssertionError(f"Infrastructure-shaped positive output:\n{output}")
    root = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
    configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    expected = f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
    test.assertEqual([line for line in output.splitlines() if re.search(r"selected\s+compiler\s*:", line, re.IGNORECASE)],
                     [expected], output)


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
        if allow_timeout and (expected or detail != "timeout"):
            raise ValueError("Only explicit negative bounded-timeout controls may opt in.")
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
                output = control_failure(error, allow_timeout=allow_timeout)
                passed = False
            with type(self).count_lock:
                type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if expected:
                assert_selected_compiler(self, output, environment)
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
        before = (type(self).entry_points_executed, type(self).positives, type(self).negatives)
        outer = "Verifier command exited 1:\n"
        infrastructure = [
            "Verifier cleanup timeout: child did not exit by the deadline.",
            "EOF supervision failure", "inherited stdout pipe", "broken pipe",
            "Cannot terminate verifier process group 123",
            "UNKNOWN", "solver returned unknown", "solver timed out", "solver timeout",
            "OSError: launch failure", "FileNotFoundError: missing executable",
            "PermissionError: permission denied", "TimeoutError: process start",
            "errno 8: OS error", "no such file or directory", "unable to launch dotnet",
            "could not start subprocess", "operating-system error", "Traceback (most recent call last):",
            "System.DllNotFoundException: Microsoft.Z3", "System.BadImageFormatException",
        ]
        errors = infrastructure + [outer + "Verification failed: " + error for error in infrastructure]
        errors += [
            "unclassified supervision failure",
            "Verifier command exited 1\nmissing colon",
            outer + "Verification failed: Verifier command exited 1\nmissing nested colon",
            outer + "Verification failed: Verification passed: FetchUrl",
            outer + "global deadline expired", "Verifier timeout: overall deadline expired.",
            outer + "Verification failed: Verifier timeout after 2s: dotnet",
        ]
        for status in ("0", "2", "-6", "137", "139", "1garbage"):
            errors += [f"Verifier command exited {status}:\nwrong result",
                       outer + f"Verification failed: Verifier command exited {status}:\nwrong result"]
        try:
            for error in errors:
                for opted_in in (False, True):
                    # The explicit ordinary verifier timeout is the only allowed
                    # opt-in; UNKNOWN, solver/OS and cleanup/EOF still raise.
                    if opted_in and error == outer + "Verification failed: Verifier timeout after 2s: dotnet":
                        continue
                    with self.subTest(error=error, opted_in=opted_in), patch.object(
                        ENGINE, "run", side_effect=RuntimeError(error)
                    ):
                        with self.assertRaises(RuntimeError):
                            self.verify("eff005", source("eff005"), False,
                                        detail="timeout" if opted_in else None, allow_timeout=opted_in)
            for output in (outer + "Verification failed: Wrong output",
                           outer + "Verification failed: " + outer + "Verification failed: Wrong output"):
                with patch.object(ENGINE, "run", side_effect=RuntimeError(output)):
                    self.verify("eff005", source("eff005"), False, detail="Wrong output")
            for output in ("Verifier timeout after 2s: dotnet",
                           outer + "Verification failed: Verifier timeout after 2s: dotnet"):
                with patch.object(ENGINE, "run", side_effect=RuntimeError(output)):
                    self.verify("eff005", source("eff005"), False, detail="timeout", allow_timeout=True)
            with self.assertRaises(ValueError):
                self.verify("eff005", source("eff005"), False, allow_timeout=True)
            with patch.object(ENGINE, "run", side_effect=OSError("launch failed")):
                with self.assertRaises(OSError):
                    self.verify("eff005", source("eff005"), False)

            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            selected = COMPILER_ROOT / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll"
            success = (f"Selected compiler: {selected}\nVerification passed: FetchUrl; original declared effects, "
                       "native/CLR signatures, 16 runtime cases and BCL output/state coupling.\n"
                       f"{EFFECT_LIMITS}\n")
            invalid = [
                success.replace(str(selected), str(selected) + ".other"),
                success.replace(str(selected), str(selected.parent.parent.parent / "wrong-configuration/net10.0/calor.dll")),
                success.replace(f"Selected compiler: {selected}\n", ""),
                f"Selected compiler: {selected}\n" + success,
                f"  Selected compiler: {selected}\n" + success,
                success + "  Selected compiler: wrong-root\n",
                success + "warning: Selected compiler: mixed-extra\n",
                success.replace("Selected compiler:", "  Selected compiler:"),
                success.replace(EFFECT_LIMITS, EFFECT_LIMITS + " EOF supervision failure"),
                success + "Limits: solver UNKNOWN\n",
            ]
            invalid += [success + error + "\n" for error in infrastructure]
            invalid += [success + error + "\n" for error in (
                "Verification failed: Wrong output", "Verifier command exited 1:\n",
                "Verifier timeout after 2s: dotnet", "global deadline expired",
            )]
            for output in invalid:
                with self.subTest(output=output), patch.object(ENGINE, "run", return_value=output):
                    with self.assertRaises(AssertionError):
                        self.verify("eff005", source("eff005"))
            with patch.object(ENGINE, "run", return_value=success):
                self.verify("eff005", source("eff005"))
        finally:
            # Synthetic calls exercise actual verify(), but are not entrypoints.
            with type(self).count_lock:
                type(self).entry_points_executed, type(self).positives, type(self).negatives = before

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
        assert_selected_compiler(self, output, dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT)))
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
        with tempfile.TemporaryDirectory(prefix="calor-effects-missing-arm-") as temporary:
            arm = Path(temporary)
            self.verify("eff005", source("eff005"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
        self.assert_corrupted_native_refusals()

    def assert_corrupted_native_refusals(self):
        with tempfile.TemporaryDirectory(prefix="calor-effects-native-arm-") as temporary:
            arm = Path(temporary)
            relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid native asset")
            for profile in TASKS:
                before = type(self).entry_points_executed
                with self.assertRaises(RuntimeError) as refused:
                    self.verify(profile, source(profile), False, env={"CALOR_COMPILER_ROOT": str(arm)})
                output = str(refused.exception)
                self.assertEqual(re.findall(r"Verifier command exited ([^\s:]+)", output), ["1", "1"], output)
                self.assertTrue(output.startswith("Verifier command exited 1:\n"), output)
                for wrapper in re.finditer(r"Verifier command exited\b", output):
                    self.assertRegex(output[wrapper.start():], r"\AVerifier command exited 1:\n")
                self.assertIn("System.DllNotFoundException", output)
                self.assertIn("Microsoft.Z3", output)
                self.assertRegex(output, r"slice is not valid mach-o|invalid ELF header|file too short|not a valid Win32")
                self.assertNotRegex(output,
                    r"(?i)timeout|timed out|deadline|EOF|cleanup|Verification passed:|\bUNKNOWN\b"
                    r"|solver[^\n]*unknown|OSError|FileNotFoundError|PermissionError|Win32Exception|Z3Exception|Traceback"
                    r"|permission denied|cannot terminate|process group|inherited (?:pipe|stdout)|broken pipe"
                    r"|(?:failed|unable) to (?:launch|start)|could not (?:launch|start)|launch failure|OS error|operating.system error")
                self.assertEqual([line for line in output.splitlines() if re.search(r"selected\s+compiler\s*:", line, re.IGNORECASE)],
                                 [f"Selected compiler: {arm.resolve() / relative / 'calor.dll'}"], output)
                self.assertEqual((arm / relative / native).read_bytes(), b"invalid native asset")
                # Count the one real, validated prerequisite refusal separately
                # from finite behavior admission; preserve the existing totals.
                with type(self).count_lock:
                    self.assertEqual(type(self).entry_points_executed, before)
                    type(self).entry_points_executed += 1
                    type(self).negatives += 1
        print("Validated 6 real corrupted-native prerequisite refusals, not behavioral negatives.")


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
