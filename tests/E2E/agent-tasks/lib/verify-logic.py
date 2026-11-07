#!/usr/bin/env python3
"""Compiler-backed logic/control-flow oracles; never invokes an agent."""

import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time


def stop(process, deadline):
    deadline = min(deadline, time.monotonic() + 0.5)
    process.poll()
    termination_error = None
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except OSError as error:
        termination_error = error
    try:
        process.communicate(timeout=max(0, deadline - time.monotonic()))
    except subprocess.TimeoutExpired:
        # Escaped descendants may retain stdout after the original process group
        # dies. Do not wait for their EOF beyond the overall deadline.
        if process.stdout is not None:
            process.stdout.close()
        try:
            process.wait(timeout=max(0, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            raise RuntimeError("Verifier cleanup timeout: child did not exit by the deadline.") from None
    if termination_error is not None:
        raise RuntimeError(f"Cannot terminate verifier process group {process.pid}: {termination_error}") from termination_error


def run(command, cwd, deadline):
    timeout = deadline - time.monotonic()
    if timeout <= 0:
        raise RuntimeError("Verifier timeout: overall deadline expired.")
    process = subprocess.Popen(
        command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, start_new_session=True,
    )
    try:
        # Reserve part of this same deadline for bounded termination and EOF cleanup.
        output, _ = process.communicate(timeout=max(0, deadline - time.monotonic() - min(0.5, timeout / 2)))
    except subprocess.TimeoutExpired:
        stop(process, deadline)
        raise RuntimeError(f"Verifier timeout after {timeout}s: {command[0]}") from None
    except BaseException:
        stop(process, deadline)
        raise
    if process.returncode:
        raise RuntimeError(f"Verifier command exited {process.returncode}:\n{output}")
    return output


def main():
    if len(sys.argv) != 3:
        raise RuntimeError("Usage: verify-logic.py FUNCTION WORKSPACE")
    helper = Path(__file__).resolve().parent
    selected_root = os.environ.get("CALOR_COMPILER_ROOT", str(helper.parents[3]))
    if not selected_root.strip():
        raise RuntimeError("CALOR_COMPILER_ROOT must name a compiler checkout, not an empty path.")
    root = Path(selected_root).resolve()
    configuration = os.environ.get("CALOR_COMPILER_CONFIGURATION", "Debug")
    if not configuration.strip() or configuration in (".", "..") or "/" in configuration or "\\" in configuration:
        raise RuntimeError("CALOR_COMPILER_CONFIGURATION must be a nonempty directory name within the selected compiler root.")
    compiler = root / "src/Calor.Compiler/bin" / configuration / "net10.0"
    native = {"darwin": "libz3.dylib", "linux": "libz3.so", "win32": "libz3.dll"}.get(sys.platform)
    if native is None:
        raise RuntimeError(f"Unsupported verifier native platform: {sys.platform}")
    for dependency in ("calor.dll", "Calor.Runtime.dll", "Microsoft.Z3.dll", native):
        if not (compiler / dependency).is_file():
            raise RuntimeError(f"Selected compiler dependency missing: {compiler / dependency}; build the selected root first.")
    filename = ("Collections.calr" if sys.argv[1] == "PrintDictionary" else "Effects.calr"
                if sys.argv[1] in ("PrintNumbers", "Countdown", "DoWhileDemo", "PrintEvenNumbers") else "Calculator.calr")
    source = Path(sys.argv[2]).resolve() / filename
    if not source.is_file():
        raise RuntimeError(f"Input missing: {source}")
    timeout = float(os.environ.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))
    if not 0 < timeout <= 120:
        raise RuntimeError("CALOR_LOGIC_TIMEOUT_SECONDS must be in (0, 120].")
    deadline = time.monotonic() + timeout
    with tempfile.TemporaryDirectory(prefix="calor-logic-verifier-") as temporary:
        project = Path(temporary)
        shutil.copytree(helper / "logic-verifier", project, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        run([
            "dotnet", "build", "LogicVerifier.csproj", "--nologo", "-v", "quiet",
            f"-p:CompilerDirectory={compiler}", "-p:UseSharedCompilation=false",
            "-nodeReuse:false", "--disable-build-servers",
        ], project, deadline)
        print(f"Selected compiler: {compiler / 'calor.dll'}")
        print(run([
            "dotnet", str(project / "bin/Debug/net10.0/LogicVerifier.dll"),
            sys.argv[1], str(source),
        ], project, deadline), end="")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Verification failed: {error}", file=sys.stderr)
        sys.exit(1)
