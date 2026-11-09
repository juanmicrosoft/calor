#!/usr/bin/env python3
"""Compiler-backed logic/control-flow/basic oracles; never invokes an agent."""

import os
import hashlib
import platform
import re
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time


ADVANCED_INFRASTRUCTURE = re.compile(
    r"timeout|timed out|deadline|\bEOF\b|cleanup|Cannot terminate|infrastructure|(?-i:\bUNKNOWN\b)|"
    r"OutOfMemory|DllNotFound|BadImageFormat|FileNotFoundException|AccessViolation|"
    r"Aborted|abnormal|segmentation|killed|\b(?:MSB|NU)\d+\b|Build FAILED|cannot launch|permission denied|"
    r"Selected native manifest|Selected compiler dependency|native manifest changed|image changed or was substituted|"
    r"\[Errno\b|OSError|WinError|Win32Exception|UnauthorizedAccessException|IOException|"
    r"FileNotFoundError|PermissionError|ProcessLookupError|EOFError|"
    r"\bOS\b|Traceback|ConnectionError|TimeoutError|success-shaped failure",
    re.IGNORECASE,
)


def check_advanced_success(output, identity=None):
    identities = [line for line in output.splitlines()
                  if re.search(r"Selected\s+compiler\b", line, re.IGNORECASE)]
    verdicts = [line for line in output.splitlines()
                if re.search(r"Verification\s+passed\b", line, re.IGNORECASE)]
    if (identities != ([] if identity is None else [identity])
            or len(verdicts) != 1 or not verdicts[0].startswith("Verification passed: ")
            or ADVANCED_INFRASTRUCTURE.search(output)
            or re.search(r"Verifier\s+command\s+exited|\b(?:failure|failed|error)\b|Exception\b",
                         output, re.IGNORECASE)):
        raise RuntimeError("Advanced verifier returned success-shaped failure or extra identity/verdict output.")


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
        raise RuntimeError("Verifier cleanup EOF timeout: inherited output pipe did not close by the deadline.") from None
    if termination_error is not None:
        raise RuntimeError(f"Cannot terminate verifier process group {process.pid}: {termination_error}") from termination_error


def run(command, cwd, deadline, env=None):
    timeout = deadline - time.monotonic()
    if timeout <= 0:
        raise RuntimeError("Verifier timeout: overall deadline expired.")
    process = subprocess.Popen(
        command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, start_new_session=True, env=env,
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
    native_only = sys.argv[1:] == ["--check-native"]
    if not native_only and len(sys.argv) != 3:
        raise RuntimeError("Usage: verify-logic.py FUNCTION WORKSPACE | --check-native")
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
    filename = ("Domain.calr" if sys.argv[1] in ("oop001", "oop002", "oop003", "oop004", "oop005", "oop006", "async004", "lam003", "lam004") else
                "AsyncOps.calr" if sys.argv[1] in ("async001", "async002", "async003") else
                "Generic.calr" if sys.argv[1] in ("gen001", "gen002", "gen003") else
                "Enums.calr" if sys.argv[1] in ("enum001", "enum002", "enum003") else
                "Impure.calr" if sys.argv[1] in ("ref001", "ref003", "ref007") else
                "BuggyContracts.calr" if sys.argv[1] in ("contract003", "ref006") else
                "Contracts.calr" if sys.argv[1] in ("ac001", "ac002", "ac003", "ac004", "ac005") else
                "Collections.calr" if sys.argv[1] in ("PrintDictionary", "coll001", "coll002", "coll003", "coll004", "coll005", "coll006") else "Effects.calr"
                if sys.argv[1] in ("PrintNumbers", "Countdown", "DoWhileDemo", "PrintEvenNumbers",
                                    "eff001", "eff002", "eff003", "eff004", "eff005", "eff006", "ref002") else "Calculator.calr")
    source = None if native_only else Path(sys.argv[2]).resolve() / filename
    if source is not None and not source.is_file():
        raise RuntimeError(f"Input missing: {source}")
    timeout = float(os.environ.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))
    if not 0 < timeout <= 120:
        raise RuntimeError("CALOR_LOGIC_TIMEOUT_SECONDS must be in (0, 120].")
    deadline = time.monotonic() + timeout
    advanced = not native_only and sys.argv[1] in ("ac001", "ac002", "ac003", "ac004", "ac005")
    carrier = advanced or not native_only and sys.argv[1] in (
        "type001", "type002", "type003", "type004", "pm004", "pm005",
        "ref001", "ref002", "ref003", "ref004", "ref005", "ref006", "ref007", "ref008",
        "lam001", "lam002", "lam003", "lam004")
    images = {}
    manifest_digest = None
    if carrier:
        manifest = root / ".github/z3-binaries-4.15.7.sha256"
        manifest_digest = hashlib.sha256(manifest.read_bytes()).digest()
        pins = {}
        for line in manifest.read_text(encoding="utf-8").splitlines():
            if line.strip() and not line.lstrip().startswith("#"):
                digest, name, size = line.split()
                if name in pins or len(digest) != 64:
                    raise RuntimeError(f"Invalid selected native manifest: {manifest}")
                pins[name] = (digest, int(size))
        architecture = {"aarch64": "arm64", "arm64": "arm64", "amd64": "x64", "x86_64": "x64"}.get(
            platform.machine().lower())
        platform_name = {"darwin": "osx", "linux": "linux", "win32": "win"}[sys.platform]
        extension = {"darwin": "dylib", "linux": "so", "win32": "dll"}[sys.platform]
        for file, name in (("Microsoft.Z3.dll", "Microsoft.Z3.dll"),
                           (native, f"libz3-{platform_name}-{architecture}.{extension}")):
            image = compiler / file
            if name not in pins or (hashlib.sha256(image.read_bytes()).hexdigest(), image.stat().st_size) != pins[name]:
                raise RuntimeError(f"Selected native manifest mismatch: {image}")
        images = {
            image.relative_to(compiler): hashlib.sha256(image.read_bytes()).digest()
            for image in (*compiler.iterdir(), *(compiler / "runtimes").rglob("*"))
            if image.is_file() and (image.suffix == ".dll" or image.name.startswith("libz3."))
        }

    def check_images(output):
        if hashlib.sha256(manifest.read_bytes()).digest() != manifest_digest:
            raise RuntimeError(f"Selected native manifest changed: {manifest}")
        for relative, expected in images.items():
            for image in (compiler / relative, output / relative):
                if not image.is_file() or hashlib.sha256(image.read_bytes()).digest() != expected:
                    raise RuntimeError(f"Selected compiler/native image changed or was substituted: {image}")
        if time.monotonic() >= deadline:
            raise RuntimeError("Verifier timeout: selected image validation exceeded the overall deadline.")

    with tempfile.TemporaryDirectory(prefix="calor-logic-verifier-") as temporary:
        project = Path(temporary)
        shutil.copytree(helper / "logic-verifier", project, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        run([
            "dotnet", "build", "LogicVerifier.csproj", "--nologo", "-v", "quiet",
            f"-p:CompilerDirectory={compiler}", "-p:UseSharedCompilation=false",
            "-nodeReuse:false", "--disable-build-servers",
        ], project, deadline)
        if carrier:
            check_images(project / "bin/Debug/net10.0")
        print(f"Selected compiler: {compiler / 'calor.dll'}")
        output = run([
            "dotnet", str(project / "bin/Debug/net10.0/LogicVerifier.dll"),
            *(["--check-native"] if native_only else [sys.argv[1], str(source)]),
        ], project, deadline)
        if carrier:
            check_images(project / "bin/Debug/net10.0")
        if advanced:
            check_advanced_success(output)
        print(output, end="")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Verification failed: {error}", file=sys.stderr)
        sys.exit(1)
