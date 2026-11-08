#!/usr/bin/env python3
"""Mandatory-failure v0.29 compatibility control, not an experiment baseline."""

import importlib.util
import os
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
import time


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OLD_COMMIT = "d82f3ae95175ecd09581c6922701706be72b0ca9"


def load(name, path):
    specification = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def main():
    if sys.argv[1:] not in ([], ["--control-flow"], ["--remaining-control-flow"], ["--basic-syntax"], ["--contract-writing"], ["--native-prerequisite"], ["--enums"], ["--idioms"], ["--generics"], ["--strings"], ["--collections"], ["--scalar-patterns"], ["--oop"], ["--type-arithmetic"], ["--carriers"], ["--async-functions"], ["--effects-system"]):
        raise RuntimeError("Usage: test-logic-old-release.py [--control-flow|--remaining-control-flow|--basic-syntax|--contract-writing|--native-prerequisite|--enums|--idioms|--generics|--strings|--collections|--scalar-patterns|--oop|--type-arithmetic|--carriers|--async-functions|--effects-system]")
    verifier = load("verify_logic", HERE / "lib/verify-logic.py")
    controls = load("logic_controls", HERE / "test-logic-verifiers.py")
    current = controls.COMPILER_ROOT
    configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    if not (current / "src/Calor.Compiler/bin" / configuration / "net10.0/calor.dll").is_file():
        raise RuntimeError(f"Build the selected current compiler first: {current}")
    deadline = time.monotonic() + 600
    def bootstrap_deadline():
        return min(deadline, time.monotonic() + 120) if sys.argv[1:] == ["--carriers"] else deadline

    with tempfile.TemporaryDirectory(prefix="calor-v029-compatibility-") as temporary:
        directory = Path(temporary)
        archive = directory / "v029.tar"
        old = directory / "v029"
        old.mkdir()
        verifier.run(["git", "cat-file", "-e", OLD_COMMIT + "^{commit}"], ROOT, bootstrap_deadline())
        verifier.run(["git", "archive", "--format=tar", f"--output={archive}", OLD_COMMIT], ROOT, bootstrap_deadline())
        with tarfile.open(archive) as archived:
            archived.extractall(old, filter="data")
        # These are gitignored build prerequisites, verified against the archived
        # release's own manifest before its unchanged compiler source is built.
        for relative in ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes"):
            source = current / relative
            if not source.is_dir():
                raise RuntimeError(f"Verified Z3 build prerequisite missing: {source}")
            shutil.copytree(source, old / relative, dirs_exist_ok=True)
        print(verifier.run([sys.executable, "scripts/verify-z3-assets.py"], old, bootstrap_deadline()), end="")
        print(verifier.run([
            "dotnet", "restore", "src/Calor.Compiler/Calor.Compiler.csproj", "--locked-mode",
            "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false", "-v", "quiet",
        ], old, bootstrap_deadline()), end="")
        print(verifier.run([
            "dotnet", "build", "src/Calor.Compiler/Calor.Compiler.csproj", "--no-restore",
            "-c", configuration, "--disable-build-servers", "-p:UseSharedCompilation=false",
            "-nodeReuse:false", "--nologo", "-v", "quiet",
        ], old, bootstrap_deadline()), end="")
        print(f"Built actual v0.29.0 source from Git commit {OLD_COMMIT}.")
        if sys.argv[1:] == ["--native-prerequisite"]:
            for arm, root in (("v0.29", old), ("current", current)):
                root = root.resolve()
                environment = dict(os.environ, CALOR_COMPILER_ROOT=str(root),
                                   CALOR_COMPILER_CONFIGURATION=configuration,
                                   CALOR_LOGIC_TIMEOUT_SECONDS=str(min(120, deadline - time.monotonic())))
                output = verifier.run(
                    [sys.executable, str(HERE / "lib/verify-logic.py"), "--check-native"],
                    HERE, deadline, env=environment)
                expected = [
                    f"Selected compiler: {root / 'src/Calor.Compiler/bin' / configuration / 'net10.0/calor.dll'}",
                    "Selected native Z3 prerequisite passed: SATISFIABLE.",
                ]
                if output.splitlines() != expected:
                    raise RuntimeError(f"{arm}: native prerequisite identity/result mismatch: "
                                       f"expected {expected!r}, observed {output.splitlines()!r}.")
                print(f"{arm}: actual selected native prerequisite passed.")
            print("Compatibility passed: 2 actual old/current native prerequisite controls.")
            return
        if sys.argv[1:] == ["--contract-writing"]:
            contracts = load("contract_controls", HERE / "test-contract-writing-verifiers.py")
            contracts.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--idioms"]:
            idioms = load("idiom_controls", HERE / "test-idiom-verifiers.py")
            idioms.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--strings"]:
            strings = load("string_controls", HERE / "test-string-verifiers.py")
            strings.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--collections"]:
            collections = load("collection_controls", HERE / "test-collection-verifiers.py")
            collections.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--scalar-patterns"]:
            patterns = load("scalar_pattern_controls", HERE / "test-scalar-pattern-verifiers.py")
            patterns.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--type-arithmetic"]:
            arithmetic = load("arithmetic_controls", HERE / "test-type-arithmetic-verifiers.py")
            arithmetic.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--carriers"]:
            carriers = load("carrier_controls", HERE / "test-carrier-verifiers.py")
            carriers.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--async-functions"]:
            asynchronous = load("async_controls", HERE / "test-async-verifiers.py")
            asynchronous.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--effects-system"]:
            effects = load("effects_controls", HERE / "test-effects-verifiers.py")
            effects.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--enums"]:
            enums = load("enum_controls", HERE / "test-enum-verifiers.py")
            enums.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--generics"]:
            generics = load("generic_controls", HERE / "test-generic-verifiers.py")
            generics.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--oop"]:
            oop = load("oop_controls", HERE / "test-oop-verifiers.py")
            oop.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--basic-syntax"]:
            basic = load("basic_controls", HERE / "test-basic-syntax-verifiers.py")
            basic.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--control-flow"]:
            control_flow = load("control_flow_controls", HERE / "test-control-flow-verifiers.py")
            control_flow.run_compatibility(old, current, deadline)
            return
        if sys.argv[1:] == ["--remaining-control-flow"]:
            remaining = load("remaining_cf_controls", HERE / "test-remaining-control-flow-verifiers.py")
            remaining.run_compatibility(old, current, deadline)
            return
        test = controls.LogicVerifierTests()

        def syntax(text, historical):
            if not historical:
                return text
            text = text.replace("§M{Calculator}", "§M{m001:Calculator}")
            for name in controls.TASK_NAMES:
                text = text.replace(f"§F{{{name}:", f"§F{{f001:{name}:")
            lines = []
            conditional = 0
            for line in text.splitlines():
                if "§IF " in line:
                    conditional += 1
                    line = line.replace("§IF ", f"§IF{{if{conditional}}} ", 1)
                lines.append(line)
            return "\n".join(lines) + "\n"

        def execute(root, arm, name, text, expected=True, detail=None):
            if time.monotonic() >= deadline:
                raise RuntimeError("Old/new compatibility control exceeded its 600-second deadline.")
            remaining = min(120, deadline - time.monotonic())
            output = test.verify(name, text, expected, env={
                "CALOR_COMPILER_ROOT": str(root),
                "CALOR_LOGIC_TIMEOUT_SECONDS": str(remaining),
            }, detail=detail)
            verdict = next((line for line in output.splitlines() if " ---> " in line), None)
            verdict = verdict or next((line for line in output.splitlines() if "Verification passed:" in line
                            or (detail and detail in line)
                            or (not detail and ("System.InvalidOperationException:" in line
                                                or "ContractViolationException:" in line))), output.splitlines()[0])
            print(f"{arm} {name}: {verdict}")

        weak = {
            "Factorial": "§Q (>= n 0)\n§Q (<= n 5)\n§S (>= result 0)",
            "Clamp": "§Q (<= min max)\n§S (|| (>= result min) (<= result max))",
            "IsPositive": "§S (|| (! result) (> x 0))",
            "Max": "§S (>= result a)\n§S (>= result b)",
        }
        for arm, root, historical in (("v0.29", old, True), ("current", current, False)):
            for name in controls.TASK_NAMES:
                declaration = controls.DECLARATIONS[name][0]
                for expression, signature in (
                    (False, declaration.replace("i32", "int")),
                    (True, declaration.replace("i32:", "int:", 1)),
                ):
                    execute(root, arm, name, syntax(controls.source(
                        name, signature=signature, expression=expression), historical))
                execute(root, arm, name, syntax(controls.source(
                    name, signature=declaration.replace("i32:", "i64:", 1),
                    body="§R false" if name == "IsPositive" else "§R 1"), historical),
                    False, "Wrong public signature")
                required = controls.DECLARATIONS[name][1].splitlines()
                execute(root, arm, name, syntax(controls.source(
                    name, contracts="\n".join(line for line in required if not line.startswith("§S"))), historical),
                    False, "Missing/weakened postconditions")
                execute(root, arm, name, syntax(controls.source(name, contracts=weak[name]), historical),
                    False, "Missing/weakened postconditions")
                execute(root, arm, name, syntax(controls.source(
                    name, body="§R false" if name == "IsPositive" else "§R 1"), historical), False)
        execute(current, "current rejects old syntax", "Max",
                syntax(controls.source("Max"), True), False, "Calor compilation failed")
        if controls.LogicVerifierTests.entry_points_executed != 49:
            raise RuntimeError("Expected exactly 49 executed compatibility controls.")
        print(f"Compatibility passed: {controls.LogicVerifierTests.entry_points_executed} actual old/current entry-point controls.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Compatibility failed: {error}", file=sys.stderr)
        sys.exit(1)
