#!/usr/bin/env python3
"""Disposable original pm-004/005 native carrier matching controls."""

import importlib.util
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
TASKS = {"pm004": ("04_option_some_matching", "UnwrapOrDefault"),
         "pm005": ("05_result_ok_err_matching", "HandleResult")}
COUNTS = {"pm004": (210, 1050), "pm005": (90, 360)}
EXECUTED = {"accepted": 0, "rejected": 0}
REJECTIONS = {
    "missing": ("Input missing:",),
    "invalid-source": ("Calor compilation failed:",),
    "wrong-name": ("Expected exactly one native",),
    "private-function": ("Wrong native public carrier matching signature",),
    "wrong-payload-type": ("Wrong native public carrier matching signature",),
    "wrong-result-type": ("Wrong native public carrier matching signature",),
    "duplicate-function": ("Expected exactly one native", "Duplicate signature"),
    "raw-interop": ("Raw interop cannot establish",),
    "wrong-case-api": ("Missing authoritative typed carrier",),
    "private-case-api": ("Wrong carrier member shape/type:", "CS0122"),
    "contradictory-case": ("Contradictory carrier case observation:",),
    "contradictory-payload": ("Contradictory active carrier payload observation:",),
    "contradictory-error": ("Contradictory active carrier payload observation:",),
    "false-precondition": ("Precondition failed",),
    "false-postcondition": ("Postcondition failed",),
    "no-switch": ("Required native carrier-input switch expression missing",),
    "dead-switch": ("Required native carrier case arms missing or do not supply the output",
                    "Carrier supplying-arm output counterfactual failed"),
    "original-payload-not-capture": ("Required native carrier case arms missing or do not supply the output",),
    "boolean-only-switch": ("Required native carrier-input switch expression missing",),
    "wrong-payload": ("Wrong carrier matching output",),
    "wrong-fallback": ("Wrong carrier matching output",),
    "wrong-tag-polarity": ("Wrong carrier matching output",),
    "single-arm-decoy": ("Required native carrier case arms missing or do not supply the output",),
    "comment-only": ("Expected exactly one native",),
    "historical-ids": ("Source IDs are removed",),
}


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PROCESS = load("carrier_pattern_process", HERE / "lib/verify-logic.py")


def source(profile, form="property", api=False):
    option = profile == "pm004"
    carrier = "Option" if option else "Result"
    generic = "T" if option else "T,E"
    closed = f"Calculator.{carrier}<i32{'' if option else ',str'}>"
    tag = "IsSome" if option else "IsOk"
    opposite = "IsNone" if option else "IsErr"
    parameter = "opt" if option else "res"
    signature = f"({closed}:{parameter}{', i32:defaultVal' if option else ''})"
    fallback = "defaultVal" if option else "-1"
    fields = f"  §CL{{{carrier}:pub}}<{generic}>\n    §FLD{{bool:{tag}:pub}}\n    §FLD{{?T:Value:pub}}\n"
    if not option:
        fields += "    §FLD{?E:Error:pub}\n"
    if api:
        fields += f"    §PROP{{{opposite}:bool:pub}}\n      §GET\n        §R (! {tag})\n"
        fields += "    §MT{Unwrap:pub} () -> ?T\n      §R Value\n"
        if not option:
            fields += "    §MT{UnwrapErr:pub} () -> ?E\n      §R Error\n"
    if form == "property":
        arms = f"      §K (and §PTYPE{{{closed}:v}} §PPROP §PMATCH{{{tag}}} true) → v.Value\n"
    else:
        pattern = "§VAR{v}" if form == "guard" else f"§PTYPE{{{closed}:v}}"
        arms = f"      §K {pattern} §WHEN v.{tag} → v.Value\n"
    arms += f"      §K _ → {fallback}\n"
    return (f"§M{{Calculator}}\n{fields}  §F{{{TASKS[profile][1]}:pub}} {signature} -> i32\n"
            f"    §R §W {parameter}\n{arms}")


def historical(text):
    if text is None:
        return None
    lines = []
    for index, line in enumerate(text.splitlines()):
        if line.lstrip().startswith(("§M{", "§F{", "§CL{", "§MT{", "§PROP{")):
            line = line.replace("{", f"{{cp{index}:", 1)
        line = line.replace("§W ", f"§W{{w{index}}} ")
        lines.append(line)
    return "\n".join(lines) + "\n"


def controls(profile):
    text = source(profile)
    option = profile == "pm004"
    parameter = "opt" if option else "res"
    tag, opposite = ("IsSome", "IsNone") if option else ("IsOk", "IsErr")
    name = TASKS[profile][1]
    yield "property", text, True
    yield "guard", source(profile, "guard"), True
    yield "type-guard", source(profile, "type"), True
    yield "all-observations", source(profile, api=True), True
    yield "scalar-aliases", text.replace("i32", "int").replace("str", "string"), True
    yield "carrier-alias", text.replace(f"    §R §W {parameter}",
                                      f"    §B{{alias}} {parameter}\n    §R §W alias"), True
    yield "bound-result", text.replace(f"    §R §W {parameter}",
                                      f"    §B{{answer:i32}} §W {parameter}") + "    §R answer\n", True
    yield "opposite-tag", text.replace(tag, opposite).replace(f"§PMATCH{{{opposite}}} true",
                                                               f"§PMATCH{{{opposite}}} false"), True
    yield "unrelated-flag", text.replace(f"    §FLD{{bool:{tag}:pub}}",
                                       f"    §FLD{{bool:{tag}:pub}}\n    §FLD{{bool:IsSuccess:pub}} = true"), True
    yield "optional-contracts", text.replace("    §R §W", "    §Q true\n    §S true\n    §R §W"), True
    closed = f"Calculator.{'Option<i32>' if option else 'Result<i32,str>'}"
    yield "qualified-factory-inferred-allocation", (text
        + f"  §F{{MakeCarrier:pub}} () -> {closed}\n    §E{{alloc}}\n"
          f"    §B{{created}} §NEW{{{closed.removeprefix('Calculator.')}}}\n    §R created\n"), True
    yield "missing", None, False
    yield "invalid-source", "invalid", False
    yield "wrong-name", text.replace(name, "WrongTarget"), False
    yield "private-function", text.replace(f"{name}:pub", f"{name}:priv"), False
    yield "wrong-payload-type", text.replace("<i32", "<i64").replace("→ v.Value", "→ (cast i32 v.Value)"), False
    yield "wrong-result-type", text.replace("-> i32", "-> i64"), False
    yield "duplicate-function", text + text[text.index("\n  §F{") + 1:], False
    yield "raw-interop", text + "  §CSHARP{public class Decoy { }}§/CSHARP\n", False
    yield "wrong-case-api", text.replace(tag, "IsSuccess"), False
    yield "private-case-api", text.replace(f"bool:{tag}:pub", f"bool:{tag}:priv"), False
    yield "contradictory-case", source(profile, api=True).replace(f"§R (! {tag})", f"§R {tag}"), False
    yield "contradictory-payload", source(profile, api=True).replace("§R Value", "§R default"), False
    if not option:
        yield "contradictory-error", source(profile, api=True).replace("§R Error", "§R default"), False
    yield "false-precondition", text.replace("    §R §W", '    §Q (== (len "x") 0)\n    §R §W'), False
    yield "false-postcondition", text.replace("    §R §W", '    §S (== (len "x") 0)\n    §R §W'), False
    fallback = "defaultVal" if option else "-1"
    independent = f"(? {parameter}.{tag} {parameter}.Value {fallback})"
    dead = text.replace("    §R §W", "    §B{ignored:i32} §W")
    yield "dead-and-supplying-switch", dead + text[text.index("    §R"):], True
    yield "no-switch", text[:text.index("    §R")] + f"    §R {independent}\n", False
    yield "dead-switch", dead + f"    §R {independent}\n", False
    yield "original-payload-not-capture", source(profile, "guard").replace("→ v.Value", f"→ {parameter}.Value"), False
    yield "boolean-only-switch", (text[:text.index("    §R")] + f"    §B{{caseFlag:bool}} {parameter}.{tag}\n"
                                  "    §R §W caseFlag\n"
                                  f"      §K true → {parameter}.Value\n      §K _ → {fallback}\n"), False
    yield "wrong-payload", text.replace("→ v.Value\n", "→ 0\n"), False
    yield "wrong-fallback", text.replace(f"→ {fallback}\n", "→ 0\n"), False
    yield "wrong-tag-polarity", text.replace(f"§PMATCH{{{tag}}} true", f"§PMATCH{{{tag}}} false"), False
    yield "single-arm-decoy", (text[:text.index("    §R")] + f"    §R §W {parameter}\n"
                               f"      §K §VAR{{v}} → {independent}\n"), False
    yield "comment-only", "// " + text.replace("\n", " ") + "\n§M{Calculator}\n", False


INFRASTRUCTURE = re.compile(
    r"\b(?:EOF|cleanup|OSError|EOFError|FileNotFoundError|PermissionError|TimeoutError|Traceback|errno"
    r"|DllNotFoundException|TypeInitializationException|FileNotFoundException|BadImageFormatException"
    r"|OutOfMemoryException|Win32Exception|Z3Exception|unknown|unmodeled|unclassified|supervision|infrastructure)\b"
    r"|inherited (?:pipe|stdout|stderr)|broken pipe|cannot terminate|process group"
    r"|permission denied|no such file|(?:failed|unable) to (?:launch|start)|could not (?:launch|start)"
    r"|launch failure|OS error|operating.system error"
    r"|selected compiler dependency|selected native manifest|selected compiler/native image"
    r"|image changed or was substituted|build failed",
    re.IGNORECASE,
)
TIMEOUT = re.compile(r"timeout|timed out|deadline", re.IGNORECASE)
SUCCESS = re.compile(r"verification\s+passed\s*:", re.IGNORECASE)
FAILURE = re.compile(
    r"verification\s+failed\s*:|compilation failed|\b(?:[\w.]*Exception|[\w.]*Error|failure|failed)\s*:",
    re.IGNORECASE,
)
WRAPPER = re.compile(r"Verifi(?:er|cation)\s+command\s+exited", re.IGNORECASE)
EXPLICIT_TIMEOUT = re.compile(
    r"Verification failed: Verifier timeout after (?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?s: \S+"
)


def failure_structure(error):
    output = str(error)
    if (not output.startswith("Verifier command exited 1:\n") or SUCCESS.search(output)
        or any(not re.match(r"Verifier command exited 1:\n", output[wrapper.start():])
               for wrapper in WRAPPER.finditer(output))):
        raise error
    return output


def control_failure(error):
    output = failure_structure(error)
    if INFRASTRUCTURE.search(output) or TIMEOUT.search(output):
        raise error
    return output


def reject_prerequisite(command, environment, refusal=None, *, explicit_timeout=False):
    if (refusal is None) != explicit_timeout:
        raise ValueError("Declare exactly one prerequisite refusal or ordinary timeout.")
    try:
        PROCESS.run(command, HERE, time.monotonic() + 120, environment)
    except RuntimeError as error:
        output = failure_structure(error)
        lines = output.splitlines()
        refusals = [line for line in lines if (
            EXPLICIT_TIMEOUT.fullmatch(line) if explicit_timeout else line == refusal)]
        if len(refusals) != 1:
            raise
        diagnostics = "\n".join(line for line in lines if line != refusals[0])
        if INFRASTRUCTURE.search(diagnostics) or TIMEOUT.search(diagnostics):
            raise
        if any(not re.fullmatch(r"(?:Verification failed: )?Verifier command exited 1:", line)
               for line in lines if line != refusals[0]):
            raise
        return output
    raise AssertionError("Prerequisite control unexpectedly succeeded.")


def assert_selected_compiler(output, environment):
    if (INFRASTRUCTURE.search(output) or TIMEOUT.search(output) or WRAPPER.search(output)
        or FAILURE.search(output)):
        raise AssertionError(f"Infrastructure/failure-shaped positive output:\n{output}")
    selected = Path(environment["CALOR_COMPILER_ROOT"]).resolve() / "src/Calor.Compiler/bin" \
        / environment.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0/calor.dll"
    identities = [line for line in output.splitlines()
                  if re.search(r"selected\s+compiler\s*:", line, re.IGNORECASE)]
    if identities != [f"Selected compiler: {selected}"]:
        raise AssertionError(f"Wrong selected compiler identity:\n{output}")


def execute(profile, text, expected, root=ROOT, deadline=None, environment=None, category=None):
    with tempfile.TemporaryDirectory(prefix="calor-carrier-pattern-control-") as temporary:
        workspace = Path(temporary)
        if text is not None:
            (workspace / "Calculator.calr").write_text(text, encoding="utf-8")
        env = dict(os.environ, CALOR_COMPILER_ROOT=str(root))
        env.pop("CALOR_LOGIC_TIMEOUT_SECONDS", None)
        env.update(environment or {})
        deadline = min(time.monotonic() + 120, deadline or float("inf"))
        env["CALOR_LOGIC_TIMEOUT_SECONDS"] = str(min(120, deadline - time.monotonic()))
        try:
            output = PROCESS.run(
                ["bash", str(HERE / "tasks/pattern-matching" / TASKS[profile][0] / "verify.sh"), str(workspace)],
                HERE, deadline, env)
        except RuntimeError as error:
            if expected:
                raise
            output = control_failure(error)
            # Only a real helper's exited, explicit behavioral failure is finite
            # rejection evidence; build/native/selection failures never count.
            behavioral = ("Input missing:", "Calor compilation failed:", "Verification failed: System.",
                          "Verification failed: Microsoft.")
            if not any(marker in output for marker in behavioral) or any(marker in output for marker in (
                "Selected compiler dependency", "Selected native manifest", "Selected compiler/native image",
                "Build FAILED", "DllNotFoundException", "TypeInitializationException", "FileNotFoundException",
                "BadImageFormatException", "OutOfMemoryException")):
                raise
            if category not in REJECTIONS or not any(marker in output for marker in REJECTIONS[category]):
                raise RuntimeError(f"Unknown/unmodeled {profile}/{category} rejection:\n{output}") from error
            EXECUTED["rejected"] += 1
            return output
        if not expected:
            raise AssertionError(f"{profile}: unexpected acceptance:\n{output}")
        assert_selected_compiler(output, env)
        cases, counterfactuals = COUNTS[profile]
        required = (f"Verification passed: {TASKS[profile][1]}; native/reflected exact closed CLR input carrier; "
                    f"{cases} independent runtime cases; {counterfactuals} successful complete-case")
        if required not in output or "optional contracts execute." not in output:
            raise AssertionError(f"{profile}: missing complete native behavioral evidence:\n{output}")
        EXECUTED["accepted"] += 1
        return output


def run_compatibility(old, current, deadline):
    before = dict(EXECUTED)
    representatives = []
    shared_rejections = set()
    # All positive forms run for both exact signatures. Each distinct shared
    # adversarial category runs once per arm, alternating the target profile.
    rejected = {profile: {category: (text, expected) for category, text, expected in controls(profile)
                          if not expected} for profile in TASKS}
    for profile in TASKS:
        representatives.extend((profile, category, text, expected)
                               for category, text, expected in controls(profile) if expected)
    for profile in TASKS:
        for category in rejected[profile]:
            if category in shared_rejections:
                continue
            selected = list(TASKS)[len(shared_rejections) % len(TASKS)]
            if category not in rejected[selected]:
                selected = profile
            text, expected = rejected[selected][category]
            representatives.append((selected, category, text, expected))
            shared_rejections.add(category)
    if len(representatives) != 48 or shared_rejections != set(REJECTIONS) - {"historical-ids"}:
        raise AssertionError("Incomplete representative carrier pattern categories.")
    representatives.sort(key=lambda control: control[3])
    for label, root in (("v0.29", old), ("current", current)):
        for profile, category, text, expected in representatives:
            try:
                execute(profile, historical(text) if label == "v0.29" else text, expected, root, deadline,
                        category=category)
            except Exception as error:
                raise RuntimeError(f"{label}/{profile}/{category}: {error}") from error
        print(f"{label}: all distinct native carrier pattern positive/adversarial categories executed.")
    execute("pm004", historical(source("pm004")), False, current, deadline, category="historical-ids")
    expected_accepts = 2 * sum(expected for _, _, _, expected in representatives)
    expected_rejects = 2 * sum(not expected for _, _, _, expected in representatives) + 1
    if EXECUTED != {"accepted": before["accepted"] + expected_accepts,
                    "rejected": before["rejected"] + expected_rejects}:
        raise AssertionError("Incomplete actual old/current carrier pattern matrix.")
    print(f"Compatibility passed: {expected_accepts + expected_rejects} actual old/current carrier pattern entrypoints "
          f"({expected_accepts} accepted, {expected_rejects} rejected).")


class CarrierPatternVerifierTests(unittest.TestCase):
    def test_current_controls(self):
        before = dict(EXECUTED)
        for profile in TASKS:
            for category, text, expected in controls(profile):
                with self.subTest(profile=profile, category=category):
                    execute(profile, text, expected, category=category)
        self.assertEqual(EXECUTED, {"accepted": before["accepted"] + 24, "rejected": before["rejected"] + 47})

    def test_finite_rejections_reraise_supervision_and_infrastructure(self):
        failures = (
            "Verifier timeout after 120s: bash",
            "Verifier command exited 1:\nVerification failed: Verifier timeout after 120s: dotnet",
            "Verifier command exited 1:\nVerification failed: Verifier cleanup timeout: child did not exit",
            "Verifier command exited 1:\nCannot terminate verifier process group 1",
            "Verifier command exited 1:\nBuild FAILED",
            "Verifier command exited 1:\nVerification failed: System.DllNotFoundException: native",
            "Verifier command exited 1:\nUnknown failure",
        )
        for failure in failures:
            with self.subTest(failure=failure), patch.object(PROCESS, "run", side_effect=RuntimeError(failure)):
                with self.assertRaises(RuntimeError):
                    execute("pm004", source("pm004"), False)

    def test_actual_selected_guards_and_declared_timeout(self):
        with tempfile.TemporaryDirectory(prefix="calor-carrier-pattern-selection-") as temporary:
            arm = Path(temporary).resolve()
            workspace = arm / "input"
            workspace.mkdir()
            (workspace / "Calculator.calr").write_text(source("pm004"), encoding="utf-8")
            command = ["bash", str(HERE / "tasks/pattern-matching" / TASKS["pm004"][0] / "verify.sh"), str(workspace)]

            def reject(changes, refusal=None, *, explicit_timeout=False):
                environment = dict(os.environ, CALOR_COMPILER_ROOT=str(ROOT))
                environment.update(changes)
                reject_prerequisite(command, environment, refusal, explicit_timeout=explicit_timeout)

            reject({"CALOR_COMPILER_ROOT": ""},
                   "Verification failed: CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path.")
            reject({"CALOR_COMPILER_CONFIGURATION": "../Release"},
                   "Verification failed: CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root.")
            configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
            relative = Path("src/Calor.Compiler/bin") / configuration / "net10.0"
            reject({"CALOR_COMPILER_ROOT": str(arm)},
                   f"Verification failed: Selected compiler dependency missing: {arm / relative / 'calor.dll'}; build the selected root first.")
            shutil.copytree(ROOT / relative, arm / relative)
            (arm / ".github").mkdir()
            manifest = ".github/z3-binaries-4.15.7.sha256"
            shutil.copyfile(ROOT / manifest, arm / manifest)
            native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
            for filename in (native, "Microsoft.Z3.dll"):
                image = arm / relative / filename
                original = image.read_bytes()
                image.write_bytes(b"corrupt native prerequisite")
                reject({"CALOR_COMPILER_ROOT": str(arm)},
                       f"Verification failed: Selected native manifest mismatch: {image}")
                image.write_bytes(original)
            (arm / manifest).unlink()
            reject({"CALOR_COMPILER_ROOT": str(arm)},
                   f"Verification failed: [Errno 2] No such file or directory: {str(arm / manifest)!r}")
            reject({"CALOR_LOGIC_TIMEOUT_SECONDS": "0.001"}, explicit_timeout=True)
        print("Executed 6 actual selected-root/configuration/native infrastructure guards and 1 declared timeout.")

    def test_actual_helper_image_substitution(self):
        native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}[sys.platform]
        for filename in ("calor.dll", native):
            with self.subTest(image=filename), tempfile.TemporaryDirectory(prefix="calor-carrier-pattern-image-") as temporary:
                arm = Path(temporary)
                relative = Path("src/Calor.Compiler/bin") / os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug") / "net10.0"
                shutil.copytree(ROOT / relative, arm / relative)
                (arm / ".github").mkdir()
                shutil.copyfile(ROOT / ".github/z3-binaries-4.15.7.sha256", arm / ".github/z3-binaries-4.15.7.sha256")
                (arm / "Calculator.calr").write_text(source("pm004"), encoding="utf-8")
                run = PROCESS.run

                def substitute(command, cwd, deadline, env=None):
                    output = run(command, cwd, deadline, env)
                    image = arm / relative / filename
                    image.write_bytes(image.read_bytes() + b"substitution")
                    return output

                with patch.dict(os.environ, {"CALOR_COMPILER_ROOT": str(arm)}), \
                        patch.object(sys, "argv", ["verify-logic.py", "pm004", str(arm)]), \
                        patch.object(PROCESS, "run", side_effect=substitute), \
                        self.assertRaisesRegex(RuntimeError, "image changed or was substituted"):
                    PROCESS.main()
        print("Executed 2 actual carrier-pattern helper-build image substitution controls.")


class CarrierPatternAdmissionTests(unittest.TestCase):
    infrastructure = (
        "UNKNOWN", "solver returned unknown", "solver timed out", "solver timeout",
        "EOF supervision failure", "inherited stdout pipe", "broken pipe",
        "Verifier cleanup timeout", "Cannot terminate verifier process group 123",
        "OSError: launch failure", "EOFError: missing EOF", "TimeoutError: process start",
        "FileNotFoundError: missing executable", "PermissionError: permission denied",
        "errno 8: OS error", "no such file or directory", "unable to launch dotnet",
        "could not start subprocess", "operating-system error", "Traceback (most recent call last):",
        "System.DllNotFoundException", "System.TypeInitializationException",
        "System.FileNotFoundException", "System.BadImageFormatException",
        "System.OutOfMemoryException", "System.Win32Exception", "Microsoft.Z3.Z3Exception",
        "Selected compiler dependency missing", "Selected native manifest mismatch",
        "Selected compiler/native image changed", "Build FAILED",
        "global deadline expired", "Verifier timeout after 2s: dotnet",
        "unclassified supervision failure",
    )

    def test_execute_finite_declared_diagnostics_never_mask_failures(self):
        outer = "Verifier command exited 1:\n"
        declared = "Verification failed: Calor compilation failed:\ninvalid source diagnostic\n"
        failures = [outer + declared + failure + "\n" for failure in self.infrastructure]
        failures += [outer + "Verification failed: " + outer + declared + failure + "\n"
                     for failure in self.infrastructure]
        for status in ("0", "2", "-6", "137", "139", "1garbage", "+1"):
            failures += [f"Verifier command exited {status}:\n" + declared,
                         outer + f"Verification failed: Verifier command exited {status}:\n" + declared]
        failures += [
            declared, "Verifier command exited 1\n" + declared,
            outer + "Verification failed: Verifier command exited 1\n" + declared,
            outer + "Verifier command exited1:\n" + declared,
            outer + "Verifier command exited 1: suffixed\n" + declared,
            outer + "verification command exited 1:\n" + declared,
            outer + "Verification passed: UnwrapOrDefault\n" + declared,
        ]
        with patch.dict(EXECUTED, {"accepted": 0, "rejected": 0}, clear=True):
            for output in failures:
                error = RuntimeError(output)
                with self.subTest(output=output), patch.object(PROCESS, "run", side_effect=error):
                    with self.assertRaises(RuntimeError) as raised:
                        execute("pm004", "invalid", False, category="invalid-source")
                    self.assertIs(raised.exception, error)
            for output in (outer + declared, outer + "Verification failed: " + outer + declared):
                with patch.object(PROCESS, "run", side_effect=RuntimeError(output)):
                    execute("pm004", "invalid", False, category="invalid-source")
            self.assertEqual(EXECUTED, {"accepted": 0, "rejected": 2})
        print(f"Checked {len(failures)} contaminated finite failures and 2 exact exited-1 controls; synthetic counters isolated.")

    def test_execute_propagates_actual_os_eof_timeout_exceptions(self):
        with patch.dict(EXECUTED, {"accepted": 0, "rejected": 0}, clear=True):
            for kind in (OSError, EOFError, TimeoutError):
                for expected in (False, True):
                    error = kind("Calor compilation failed: declared diagnostic with infrastructure failure")
                    with self.subTest(kind=kind, expected=expected), patch.object(PROCESS, "run", side_effect=error):
                        with self.assertRaises(kind) as raised:
                            execute("pm004", "invalid", expected, category="invalid-source")
                        self.assertIs(raised.exception, error)
            self.assertEqual(EXECUTED, {"accepted": 0, "rejected": 0})
        print("Checked 6 actual OSError/EOFError/TimeoutError propagation paths; synthetic counters isolated.")

    def test_execute_positive_unique_complete_identity_and_clean_evidence(self):
        environment = {"CALOR_COMPILER_ROOT": str(ROOT) + "/../" + ROOT.name,
                       "CALOR_COMPILER_CONFIGURATION": "Release"}
        selected = ROOT / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
        identity = f"Selected compiler: {selected}\n"
        rejected = 0
        with patch.dict(EXECUTED, {"accepted": 0, "rejected": 0}, clear=True):
            for profile in TASKS:
                cases, counterfactuals = COUNTS[profile]
                evidence = (f"Verification passed: {TASKS[profile][1]}; native/reflected exact closed CLR input carrier; "
                            f"{cases} independent runtime cases; {counterfactuals} successful complete-case "
                            "arm/case/payload/default/input-output counterfactuals.\n"
                            "Limits: finite evidence; optional contracts execute.\n")
                success = identity + evidence
                failures = [
                    evidence, success.replace(str(selected), str(selected) + ".other"),
                    success.replace("/Release/net10.0/", "/Other/net10.0/"),
                    success.replace(str(ROOT), str(ROOT.parent / "other-root")),
                    identity + success, "  " + success,
                    success + "  Selected compiler: wrong-root\n",
                    success + "warning: Selected compiler: mixed-extra\n",
                    success + "  sElEcTeD CoMpIlEr : alternate\n",
                    success + identity.rstrip() + " suffixed\n",
                ]
                failures += [success + failure + "\n" for failure in self.infrastructure]
                failures += [success + failure + "\n" for failure in (
                    "Verification failed: Wrong output", "Calor compilation failed:",
                    "Verifier command exited 0:", "Verifier command exited 1:",
                    "System.InvalidOperationException: wrong output",
                    "System.NullReferenceException: unexpected failure", "error: wrong output",
                )]
                for output in failures:
                    with self.subTest(profile=profile, output=output), patch.object(PROCESS, "run", return_value=output):
                        with self.assertRaises(AssertionError):
                            execute(profile, source(profile), True, environment=environment)
                    rejected += 1
                with patch.object(PROCESS, "run", return_value=success):
                    execute(profile, source(profile), True, environment=environment)
            self.assertEqual(EXECUTED, {"accepted": 2, "rejected": 0})
        print(f"Checked {rejected} positive identity/failure adversaries and 2 clean controls; synthetic counters isolated.")

    def test_explicit_prerequisite_and_timeout_guards_do_not_mask_contamination(self):
        outer = "Verifier command exited 1:\n"
        refusal = "Verification failed: Selected native manifest mismatch: /selected/libz3.dylib"
        ordinary_timeout = "Verification failed: Verifier timeout after 0.001s: dotnet"
        missing_manifest = "Verification failed: [Errno 2] No such file or directory: '/selected/.github/z3-binaries-4.15.7.sha256'"
        rejected = 0
        for declared, options in ((refusal, {"refusal": refusal}),
                                  (missing_manifest, {"refusal": missing_manifest}),
                                  (ordinary_timeout, {"explicit_timeout": True})):
            valid = outer + declared + "\n"
            failures = [valid + failure + "\n" for failure in self.infrastructure]
            failures += [outer + f"Verification failed: Verifier command exited {status}:\n" + declared + "\n"
                         for status in ("0", "2", "-6", "137", "139", "1garbage")]
            failures += [valid + "Verification passed: UnwrapOrDefault\n", valid + declared + "\n",
                         valid + "unexpected unmodeled diagnostic\n"]
            for output in failures:
                error = RuntimeError(output)
                with self.subTest(options=options, output=output), patch.object(PROCESS, "run", side_effect=error):
                    with self.assertRaises(RuntimeError) as raised:
                        reject_prerequisite(["unused"], {}, **options)
                    self.assertIs(raised.exception, error)
                rejected += 1
            with patch.object(PROCESS, "run", side_effect=RuntimeError(valid)):
                self.assertEqual(reject_prerequisite(["unused"], {}, **options), valid)
        for kind in (OSError, EOFError, TimeoutError):
            error = kind(ordinary_timeout)
            with patch.object(PROCESS, "run", side_effect=error):
                with self.assertRaises(kind) as raised:
                    reject_prerequisite(["unused"], {}, explicit_timeout=True)
                self.assertIs(raised.exception, error)
        with patch.object(PROCESS, "run", return_value=refusal):
            with self.assertRaises(AssertionError):
                reject_prerequisite(["unused"], {}, refusal)
        print(f"Checked {rejected} contaminated prerequisite/timeout refusals, 3 exact refusals, "
              "3 direct exception paths and 1 success-shaped refusal.")


if __name__ == "__main__":
    result = unittest.main(verbosity=2, exit=False).result
    print(f"Executed {sum(EXECUTED.values())} actual carrier pattern entrypoints "
          f"({EXECUTED['accepted']} accepted, {EXECUTED['rejected']} rejected).")
    sys.exit(0 if result.wasSuccessful() else 1)
