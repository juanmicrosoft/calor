#!/usr/bin/env python3
"""Four original async prompts; real entrypoints and disposable native controls."""

import importlib.util
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time
import unittest


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
COMPILER_ROOT = Path(os.environ.get("CALOR_COMPILER_ROOT", str(ROOT))).resolve()
TASKS = {
    "async001": ("01_async_function", "GetDataAsync", "AsyncOps.calr", 3),
    "async002": ("02_await_expression", "ProcessAsync", "AsyncOps.calr", 20),
    "async003": ("03_configure_await", "BackgroundProcessAsync", "AsyncOps.calr", 24),
    "async004": ("04_async_method_in_class", "LoadAsync", "Domain.calr", 6),
}


def load_verifier():
    spec = importlib.util.spec_from_file_location("async_shared_verifier", HERE / "lib/verify-logic.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def source(profile, *, body=None, signature=None, effect=None):
    name = TASKS[profile][1]
    helper = "FetchDataAsync" if profile == "async002" else "SlowOperationAsync"
    default = {
        "async001": "§R 42",
        "async002": "§B{value:i32} §AWAIT §C{FetchDataAsync} §/C\n§R (* value 2)",
        "async003": "§R §AWAIT{false} §C{SlowOperationAsync} §/C",
        "async004": '§B{client} §NEW{System.Net.Http.HttpClient}\n'
                    '§B{response} §AWAIT §C{client.GetAsync} §A "https://invalid.example/test" §/C\n§R 100',
    }[profile]
    body = default if body is None else body
    effect = ("" if profile == "async001" else "§E{net,alloc}" if profile == "async004" else "§E{net:r}") if effect is None else effect
    header = signature or f"§{'AMT' if profile == 'async004' else 'AF'}{{{name}:pub}} () -> i32"
    prefix = "§M{Domain}\n  §CL{DataService:pub}\n    " if profile == "async004" else "§M{AsyncOps}\n  "
    indent = "      " if profile == "async004" else "    "
    text = prefix + header + "\n" + "".join(indent + line + "\n" for line in
                                            ((effect + "\n") if effect else "").splitlines() + body.splitlines())
    if profile in ("async002", "async003"):
        text += f"  §AF{{{helper}:priv}} () -> i32\n    §R 17\n"
    return text


def historical_syntax(text):
    """IDs and explicit I/O only in disposable old inputs; preserve every body."""
    lines = []
    serial = 0
    for line in text.splitlines():
        stripped = line.lstrip()
        if stripped.startswith(("§M{", "§AF{", "§F{", "§CL{", "§AMT{", "§MT{", "§CTOR{")):
            serial += 1
            line = line.replace("{", f"{{id{serial}:", 1)
            if stripped.startswith("§CTOR{") and line.endswith(" ()"):
                line = line[:-3]
            if "} (" in line and ") -> " in line:
                header, tail = line.split("} (", 1)
                parameters, output = tail.rsplit(") -> ", 1)
                lines.append(header + "}")
                indent = " " * (len(line) - len(line.lstrip()) + 2)
                if parameters:
                    lines += [indent + "§I{" + parameter + "}" for parameter in parameters.split(", ")]
                lines.append(indent + "§O{" + output + "}")
                continue
        lines.append(line)
    return "\n".join(lines) + "\n"


def http_field_source(*, aliases=False, constructor=False):
    body = ('§B{client} _client\n§B{address:str} "https://invalid.example/field"\n'
            '§B{task} §C{client.GetAsync} §A address §/C\n§B{response} §AWAIT task\n§R 100') if aliases else (
            '§B{response} §AWAIT §C{_client.GetAsync} §A "https://invalid.example/field" §/C\n§R 100')
    text = source("async004", body=body, effect="§E{net}")
    fields = "    §FLD{System.Net.Http.HttpClient:_client:priv:readonly} = §NEW{System.Net.Http.HttpClient}\n"
    if constructor:
        fields += "    §CTOR{pub} ()\n"
    return text.replace("  §CL{DataService:pub}\n", "  §CL{DataService:pub}\n" + fields)


def http_instance_cases():
    return [
        (http_field_source(), True, None),
        (http_field_source(aliases=True), True, None),
        (http_field_source(constructor=True), True, None),
        (http_field_source().replace(":priv:readonly", ":priv"), False, "Unmodeled HTTP field"),
        (http_field_source().replace("= §NEW{System.Net.Http.HttpClient}", "= null"), False, "Unmodeled HTTP field"),
        (http_field_source(constructor=True).replace("§CTOR{pub} ()", "§CTOR{priv} ()"),
         False, "HTTP instance construction"),
        (http_field_source(constructor=True).replace("§CTOR{pub} ()\n", "§CTOR{pub} ()\n      §B{unused:i32} 1\n"),
         False, "HTTP instance construction"),
        (http_field_source().replace("§R 100", "§B{denominator:i32} (? (== response null) 0 1)\n"
                                    "      §R (/ 100 denominator)"),
         False, "Await-neutralized HTTP control"),
    ]


def cases(profile):
    name = TASKS[profile][1]
    equivalent = {
        "async001": "§B{answer:int} (+ 40 2)\n§R answer",
        "async002": "§B{task} §C{FetchDataAsync} §/C\n§B{alias} task\n"
                    "§B{value:int} §AWAIT alias\n§R (+ value value)",
        "async003": "§B{task} §C{SlowOperationAsync} §/C\n§B{alias} task\n"
                    "§B{value:int} §AWAIT{false} alias\n§R value",
        "async004": '§B{client} §NEW{System.Net.Http.HttpClient}\n'
                    '§B{task} §C{client.GetAsync} §A "https://invalid.example/other" §/C\n'
                    '§B{alias} task\n§B{response} §AWAIT alias\n§B{value:i32} (+ 90 10)\n§R value',
    }[profile]
    controls = [
        (source(profile), True, None),
        (source(profile, body=equivalent), True, None),
        (source(profile, signature=f"§{'AMT' if profile == 'async004' else 'AF'}{{{name}:pub}} () -> int"),
         True, None),
        (source(profile).replace(f"{name}:pub", f"{name}:priv"), False, "Wrong native public async signature"),
        (source(profile).replace("-> i32", "-> i64", 1), False, "Wrong native public async signature"),
        (source(profile).replace(f"{name}:pub}} ()", f"{name}:pub}} (i32:x)"), False,
         "Wrong native public async signature"),
        (source(profile, body="§R 0"), False, None),
        ("// " + source(profile).replace("\n", " ") + "\n§M{AsyncOps}\n", False, "Expected exactly one"),
        (source(profile).replace(f"{name}:pub", "Unrelated:pub"), False, "Expected exactly one"),
        (source(profile).replace("§AMT{" if profile == "async004" else "§AF{",
                                 "§MT{" if profile == "async004" else "§F{", 1),
         False, "Wrong native public async signature"),
    ]
    if profile != "async001":
        controls += [
            (source(profile, effect=""), False, "Declared native"),
            (source(profile, effect="§E{cw}"), False, "Declared native"),
        ]
    controls.append((source(profile) + "  §CSHARP{public class Unrelated { }}§/CSHARP\n",
                     False, "requires native Calor"))
    if profile == "async002":
        controls += [
            (source(profile, body="§B{unused} §C{FetchDataAsync} §/C\n§R 34"), False, "same-task await"),
            (source(profile, body="§B{unused} §AWAIT §C{FetchDataAsync} §/C\n§R 34"),
             False, "Wrong controlled async result"),
            (source(profile, body="§B{value} §AWAIT §C{FetchDataAsync} §/C\n§R (+ value 2)"),
             False, "Wrong controlled async result"),
            (source(profile, body="§B{value} §AWAIT §C{FetchDataAsync} §/C\n§R (* value 3)"),
             False, "Wrong controlled async result"),
        ]
    if profile == "async003":
        controls += [
            (source(profile).replace("§AWAIT{false}", "§AWAIT"), False, "same-task await"),
            (source(profile).replace("§AWAIT{false}", "§AWAIT{true}"), False, "same-task await"),
            (source(profile, body="§B{unused} §AWAIT{false} §C{SlowOperationAsync} §/C\n§R 17"),
             False, "Wrong controlled async result"),
            (source(profile, body="§B{unused} §AWAIT{false} §C{System.Threading.Tasks.Task.FromResult} §A 0 §/C\n"
                                 "§R §AWAIT §C{SlowOperationAsync} §/C"), False, "same-task await"),
            (source(profile, body="§B{value} §AWAIT{false} §C{SlowOperationAsync} §/C\n§R (* value 2)"),
             False, "Wrong controlled async result"),
        ]
    if profile == "async004":
        controls += [
            (source(profile).replace("DataService:pub", "DataService:priv"), False, "native public DataService"),
            (source(profile).replace("LoadAsync:pub", "LoadAsync:pub:stat"), False, "Wrong native public async signature"),
            (source(profile, body='§B{client} §NEW{System.Net.Http.HttpClient}\n'
                                 '§B{unused} §C{client.GetAsync} §A "https://invalid.example/test" §/C\n§R 100'),
             False, "same-task await"),
            (source(profile).replace("§R 100", "§R 101"), False, "Wrong controlled async result"),
        ]
    return controls


def coupling_cases(profile):
    if profile in ("async002", "async003"):
        helper = "FetchDataAsync" if profile == "async002" else "SlowOperationAsync"
        controls = [
            (source(profile).replace("§R 17", "§R -999"), True, None),
            (source(profile).replace(f"§AF{{{helper}:priv}} () -> i32\n    §R 17",
                                     f"§F{{{helper}:priv}} () -> Task<int>\n"
                                     "    §R §C{System.Threading.Tasks.Task.FromResult} §A 31 §/C"), True, None),
            (source(profile).replace(f"§C{{{helper}}}", "§C{Unrelated}")
             + "  §AF{Unrelated:priv} () -> i32\n    §R 17\n", False, "mapped required native async call"),
            (source(profile).replace(f"§AF{{{helper}:priv}}", "§AF{Unrelated:priv}"), False, None),
        ]
        if profile == "async002":
            controls += [
                (source(profile, body="§B{task} §C{FetchDataAsync} §/C\n§B{value} task.Result\n§R (* value 2)"),
                 False, "blocking Result"),
                (source(profile, body="§B{a} §AWAIT §C{FetchDataAsync} §/C\n"
                                      "§B{b} §AWAIT §C{FetchDataAsync} §/C\n§R (+ a b)"),
                 False, "mapped required native async call"),
                (source(profile, body="§B{v} §AWAIT §C{FetchDataAsync} §/C\n§R (/ (* v v) (/ v 2))"),
                 False, None),
            ]
        else:
            controls.append((source(profile, body="§B{task} §C{SlowOperationAsync} §/C\n"
                                                 "§B{unused} §C{task.ConfigureAwait} §A false §/C\n§R §AWAIT task"),
                             False, "same-task await"))
        return controls
    if profile == "async004":
        fake = source(profile).replace("System.Net.Http.HttpClient", "HttpClient")
        fake += "  §CL{HttpClient:pub}\n    §AMT{GetAsync:pub} (str:url) -> i32\n      §R 12\n"
        return [
            (fake, False, "mapped required native async call"),
            (source(profile).replace("§M{Domain}", "§M{System.Net.Http}")
             + "  §CL{HttpClient:pub}\n    §AMT{GetAsync:pub} (str:url) -> i32\n      §R 12\n",
             False, "mapped required native async call"),
            (source(profile, body='§B{client} §NEW{System.Net.Http.HttpClient}\n'
                                  '§B{unused} §AWAIT §C{System.Threading.Tasks.Task.FromResult} §A 0 §/C\n'
                                  '§B{task} §C{client.GetAsync} §A "https://invalid.example/test" §/C\n§R 100'),
             False, "same-task await"),
            (source(profile).replace('§B{client} §NEW{System.Net.Http.HttpClient}',
                                     '§B{client:System.Net.Http.HttpClient} null'),
             False, "HTTP receiver/input"),
            (source(profile).replace("https://invalid.example/test", "not-an-absolute-uri"),
             False, "HTTP receiver/input"),
            (source(profile, body='§B{client} §NEW{System.Net.Http.HttpClient}\n§B{aliasClient} client\n'
                                  '§B{url:str} "https://invalid.example/alias"\n'
                                  '§B{response} §AWAIT §C{aliasClient.GetAsync} §A url §/C\n§R 100'),
             True, None),
        ]
    return [(source(profile).replace("-> i32", "-> Task<int>"), True, None)]


def control_failure(error, *, allow_timeout=False):
    output = str(error)
    timeout = "Verifier timeout:" in output or "Verifier timeout after " in output
    cleanup = "Verifier cleanup timeout:" in output or "Cannot terminate verifier process group" in output
    if cleanup or timeout and not allow_timeout:
        raise error
    if not output.startswith("Verifier command exited ") and not (allow_timeout and timeout):
        raise error
    return output


def assert_selected_compiler(test, output, environment):
    root = Path(environment["CALOR_COMPILER_ROOT"]).resolve()
    configuration = environment.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    expected = f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}"
    test.assertEqual([line for line in output.splitlines() if line.startswith("Selected compiler:")],
                     [expected], output)


class AsyncVerifierTests(unittest.TestCase):
    entry_points_executed = 0

    @classmethod
    def tearDownClass(cls):
        print(f"Executed {cls.entry_points_executed} real async verify.sh controls.")

    def verify(self, profile, text, expected=True, *, env=None, detail=None, deadline=None, allow_timeout=False):
        if allow_timeout and (expected or detail != "timeout"):
            raise ValueError("Only explicit negative bounded-timeout controls may opt in.")
        with tempfile.TemporaryDirectory(prefix="calor-async-control-") as temporary:
            workspace = Path(temporary)
            if text is not None:
                (workspace / TASKS[profile][2]).write_text(text, encoding="utf-8")
            environment = dict(os.environ, CALOR_COMPILER_ROOT=str(COMPILER_ROOT))
            environment.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
            environment.update(env or {})
            command = [str(HERE / "tasks/async-functions" / TASKS[profile][0] / "verify.sh"), str(workspace)]
            try:
                entry_deadline = time.monotonic() + 120
                if deadline is not None:
                    entry_deadline = min(entry_deadline, deadline)
                remaining = entry_deadline - time.monotonic() - 2
                if remaining <= 0:
                    raise RuntimeError("Verifier timeout: no process/EOF cleanup budget remains.")
                requested = float(environment.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))
                if 0 < requested <= 120:
                    # Let the nested supervisor clean its own process groups before
                    # the outer entry/global deadline can terminate that supervisor.
                    environment["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(requested, remaining))
                output = load_verifier().run(command, HERE, entry_deadline, env=environment)
                passed = True
            except RuntimeError as error:
                output = control_failure(error, allow_timeout=allow_timeout)
                passed = False
            type(self).entry_points_executed += 1
            self.assertEqual(passed, expected, output)
            if expected:
                assert_selected_compiler(self, output, environment)
                self.assertIn(f"Verification passed: {TASKS[profile][1]}; exact native/CLR async signatures, "
                              f"{TASKS[profile][3]} controlled cases and same-task await coupling.", output)
                self.assertIn("No real network", output)
                if profile == "async004":
                    self.assertIn("Actual HTTP evidence: real zero-argument instance/field construction, BCL GetAsync", output)
            if detail:
                self.assertIn(detail, output)
            return output

    def test_original_prompt_controls(self):
        for profile in TASKS:
            for text, expected, detail in cases(profile):
                with self.subTest(profile=profile, expected=expected, detail=detail):
                    self.verify(profile, text, expected, detail=detail)

    def test_missing_input_and_native_selection(self):
        for profile in TASKS:
            self.verify(profile, None, False, detail="Input missing")
        self.verify("async001", source("async001"), False,
                    env={"CALOR_COMPILER_CONFIGURATION": "../Debug"}, detail="CALOR_COMPILER_CONFIGURATION")
        self.verify("async001", source("async001"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, detail="timeout", allow_timeout=True)
        with tempfile.TemporaryDirectory(prefix="calor-async-native-arm-") as temporary:
            arm = Path(temporary)
            self.verify("async001", source("async001"), False, env={"CALOR_COMPILER_ROOT": str(arm)},
                        detail="Selected compiler dependency missing")
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
            shutil.copytree(COMPILER_ROOT / relative, arm / relative)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            (arm / relative / native).write_bytes(b"invalid native asset")
            self.verify("async001", source("async001"), False, env={"CALOR_COMPILER_ROOT": str(arm)})

    def test_native_call_identity_and_counterfactuals(self):
        for profile in TASKS:
            for text, expected, detail in coupling_cases(profile):
                with self.subTest(profile=profile, expected=expected, detail=detail):
                    self.verify(profile, text, expected, detail=detail)
        self.verify("async001", source("async001", body="§WH true\n  §CN\n§R 42"), False,
                    env={"CALOR_LOGIC_TIMEOUT_SECONDS": "10"}, detail="timeout", allow_timeout=True)

    def test_current_native_task_binding_blocker(self):
        self.verify("async002", source("async002", body="§B{task:Task<int>} §C{FetchDataAsync} §/C\n"
                    "§B{value} §AWAIT task\n§R (* value 2)"), False, detail="Calor0202")

    def test_actual_http_default_instances(self):
        for text, expected, detail in http_instance_cases():
            with self.subTest(expected=expected, detail=detail):
                self.verify("async004", text, expected, detail=detail)

    def test_failure_admission_and_selected_identity(self):
        for message in ("Verifier timeout: overall deadline expired.",
                        "Verifier timeout after 120s: dotnet",
                        "Verifier cleanup timeout: child did not exit by the deadline.",
                        "Cannot terminate verifier process group 123: denied",
                        "unexpected process/EOF failure",
                        "Verifier command exited 1:\nVerification failed: Verifier timeout after 118s: dotnet"):
            with self.subTest(message=message), self.assertRaises(RuntimeError):
                control_failure(RuntimeError(message))
        for message in ("Verifier cleanup timeout: child did not exit by the deadline.",
                        "unexpected process/EOF failure"):
            with self.subTest(message=message), self.assertRaises(RuntimeError):
                control_failure(RuntimeError(message), allow_timeout=True)
        self.assertIn("Verifier timeout", control_failure(
            RuntimeError("Verifier command exited 1:\nVerifier timeout after 10s: dotnet"), allow_timeout=True))
        self.assertEqual(control_failure(RuntimeError("Verifier command exited 1:\nWrong result")),
                         "Verifier command exited 1:\nWrong result")
        environment = {"CALOR_COMPILER_ROOT": str(COMPILER_ROOT), "CALOR_COMPILER_CONFIGURATION": "Debug"}
        identity = f"Selected compiler: {COMPILER_ROOT / 'src/Calor.Compiler/bin/Debug/net10.0/calor.dll'}"
        assert_selected_compiler(self, identity, environment)
        for output in ("", identity.replace("/Debug/", "/Release/"), identity.replace("calor.dll", "other.dll"),
                       identity + "\n" + identity):
            with self.subTest(output=output), self.assertRaises(AssertionError):
                assert_selected_compiler(self, output, environment)


def compatibility_cases(profile):
    # Keep the old matrix within its existing 600-second source-build/run bound.
    # The full current suite retains all alias/signature and supervision controls.
    base_indices = {
        "async001": (0, 1, 2, 3, 4, 7, 9, 10),
        "async002": (0, 1, 2, 3, 4, 7, 9, 10, 12, 13, 14, 15),
        "async003": (0, 1, 2, 3, 4, 7, 9, 10, 12, 13, 14, 15, 16, 17),
        "async004": (0, 1, 2, 3, 4, 7, 9, 10, 12, 13, 14, 15, 16),
    }[profile]
    coupling_indices = {"async001": (0,), "async002": (0, 1, 4, 5),
                        "async003": (0, 1, 4), "async004": (0, 1, 2, 3, 4, 5)}[profile]
    base = cases(profile)
    coupling = coupling_cases(profile)
    selected = [base[index] for index in base_indices] + [coupling[index] for index in coupling_indices]
    return selected + http_instance_cases() if profile == "async004" else selected


def run_compatibility(old, current, deadline):
    test = AsyncVerifierTests()
    count = 0
    for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
        for profile in TASKS:
            for text, expected, detail in compatibility_cases(profile):
                remaining = min(120, deadline - time.monotonic())
                if remaining <= 0:
                    raise RuntimeError("Async compatibility exceeded its 600-second deadline.")
                old_result_blocker = historical and detail == "blocking Result"
                output = test.verify(profile, historical_syntax(text) if historical else text, expected,
                                     env={"CALOR_COMPILER_ROOT": str(root), "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining)},
                                     detail="Calor0410" if old_result_blocker else detail, deadline=deadline)
                if old_result_blocker:
                    test.assertIn("?.get_Result", output)
                    test.assertIn("effect 'unknown'", output)
                    print("v0.29 native/API blocker: inferred task.Result has unknown getter effect (Calor0410); "
                          "this negative is not CLR behavioral evidence.")
                count += 1
                print(f"{arm} {profile}: {'accepted' if expected else 'rejected'}")
    test.verify("async001", historical_syntax(source("async001")), False,
                env={"CALOR_COMPILER_ROOT": str(current),
                     "CALOR_LOGIC_TIMEOUT_SECONDS": str(min(120, deadline - time.monotonic()))},
                detail="Calor parsing failed", deadline=deadline)
    count += 1
    expected_count = 139
    if count != expected_count or AsyncVerifierTests.entry_points_executed != expected_count:
        raise RuntimeError("Async compatibility skipped/empty/missing controls.")
    print(f"Compatibility passed: {count} actual old/current async entry-point controls.")


if __name__ == "__main__":
    unittest.main()
