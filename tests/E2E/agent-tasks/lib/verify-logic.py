#!/usr/bin/env python3
"""Compiler-backed logic/control-flow/basic oracles; never invokes an agent."""

import os
import hashlib
import platform
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
    filename = ("Domain.calr" if sys.argv[1] in ("oop001", "oop002", "oop003", "oop004", "oop005", "oop006") else
                "Generic.calr" if sys.argv[1] in ("gen001", "gen002", "gen003") else
                "Enums.calr" if sys.argv[1] in ("enum001", "enum002", "enum003") else
                "BuggyContracts.calr" if sys.argv[1] == "contract003" else
                "Collections.calr" if sys.argv[1] in ("PrintDictionary", "coll001", "coll002", "coll003", "coll004", "coll005", "coll006") else "Effects.calr"
                if sys.argv[1] in ("PrintNumbers", "Countdown", "DoWhileDemo", "PrintEvenNumbers") else "Calculator.calr")
    source = None if native_only else Path(sys.argv[2]).resolve() / filename
    if source is not None and not source.is_file():
        raise RuntimeError(f"Input missing: {source}")
    timeout = float(os.environ.get("CALOR_LOGIC_TIMEOUT_SECONDS", "120"))
    if not 0 < timeout <= 120:
        raise RuntimeError("CALOR_LOGIC_TIMEOUT_SECONDS must be in (0, 120].")
    deadline = time.monotonic() + timeout
    carrier = not native_only and sys.argv[1] in ("type001", "type002", "type003", "type004")
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
            for image in compiler.rglob("*")
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
        print(output, end="")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Verification failed: {error}", file=sys.stderr)
        sys.exit(1)
