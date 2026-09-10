#!/usr/bin/env python3
"""Run the existing xUnit v2 framework without VSTest's out-of-process TCP transport."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
TOOL = ROOT / "test-host"
DEPENDENCIES = (
    "xunit.runner.visualstudio/2.8.2/build/net6.0/xunit.runner.utility.netcoreapp10.dll",
    "xunit.runner.visualstudio/2.8.2/build/net6.0/xunit.abstractions.dll",
    "xunit.extensibility.core/2.9.2/lib/netstandard1.1/xunit.core.dll",
    "xunit.extensibility.execution/2.9.2/lib/netstandard1.1/xunit.execution.dotnet.dll",
    "xunit.assert/2.9.2/lib/net6.0/xunit.assert.dll",
)


def require(condition, message):
    if not condition:
        raise ValueError("PP-W xUnit host: " + message)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def package_root():
    root = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
    require(root.is_absolute() and root.is_dir(), "an existing absolute NuGet package cache is required")
    return root.resolve(strict=True)


def dependencies(root):
    root = Path(root)
    require(all((root / name).is_file() for name in DEPENDENCIES),
            "the registered xUnit 2.9.2 / adapter 2.8.2 dependencies must already be restored")
    return {name: digest(root / name) for name in DEPENDENCIES}


def prepare(packages):
    packages = Path(packages).resolve(strict=True)
    manifest = dependencies(packages)
    sources = {name: digest(TOOL / name) for name in ("Program.cs", "PpwXunitHost.csproj")}
    fingerprint = hashlib.sha256(json.dumps(
        {"sources": sources, "dependencies": manifest}, sort_keys=True).encode()).hexdigest()
    cache = TOOL / "cache" / fingerprint
    binary = cache / "bin/ppw-xunit-host.dll"
    cache.mkdir(parents=True, exist_ok=True)
    with (cache / "build.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if not binary.is_file():
            adapter = packages / "xunit.runner.visualstudio/2.8.2/build/net6.0"
            result = subprocess.run([
                "dotnet", "build", str(TOOL / "PpwXunitHost.csproj"), "--configuration", "Release",
                "--output", str(cache / "bin"), "--property:BaseIntermediateOutputPath=" + str(cache / "obj") + "/",
                "--property:XunitAdapterRoot=" + str(adapter), "--verbosity", "quiet"],
                capture_output=True, text=True, timeout=180)
            (cache / "build.log").write_text(result.stdout + result.stderr)
            require(result.returncode == 0 and binary.is_file(), "host build failed: " + str(cache / "build.log"))
    runtime = {"binary": str(binary), "packages": str(packages), "dependencies": manifest,
               "manifest": str(cache / "runtime.json"),
               "binaries": {path.name: digest(path) for path in sorted(binary.parent.iterdir())
                            if path.suffix in (".dll", ".json")}}
    runtime_path = Path(runtime["manifest"])
    with (cache / "build.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if not runtime_path.exists():
            with runtime_path.open("x") as stream:
                json.dump(runtime, stream, sort_keys=True)
        require(json.loads(runtime_path.read_text()) == runtime,
                "existing runtime manifest differs")
    return runtime


def validate_runtime(runtime):
    require(isinstance(runtime, dict) and dependencies(runtime["packages"]) == runtime.get("dependencies"),
            "test framework dependency bytes changed")
    require(json.loads(Path(runtime["manifest"]).read_text()) == runtime, "runtime manifest changed")
    binary = Path(runtime["binary"])
    require(binary.is_absolute() and binary.is_file(), "prebuilt host is missing")
    actual = {path.name: digest(path) for path in sorted(binary.parent.iterdir())
              if path.suffix in (".dll", ".json")}
    require(actual == runtime.get("binaries"), "prebuilt host bytes changed")
    return binary


def run(arguments):
    require(arguments and arguments[0] == "test", "expected dotnet test arguments")
    parser = argparse.ArgumentParser(allow_abbrev=False)
    parser.add_argument("project", nargs="?")
    parser.add_argument("--nologo", action="store_true")
    parser.add_argument("-v", "--verbosity", default="minimal")
    parser.add_argument("-c", "--configuration", default="Debug")
    parser.add_argument("--no-restore", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--logger", action="append", default=[])
    options = parser.parse_args(arguments[1:])
    require(all(logger in ("console", "console;verbosity=normal", "console;verbosity=detailed",
                           "console;verbosity=minimal", "console;verbosity=quiet")
                for logger in options.logger), "unregistered test logger; only console presentation is supported")
    project = Path(options.project) if options.project else Path.cwd()
    if project.is_dir():
        projects = list(project.glob("*.csproj"))
        require(len(projects) == 1, "exactly one project is required")
        project = projects[0]
    project = project.resolve(strict=True)
    require(project.suffix == ".csproj", "a C# test project is required")
    runtime_path = os.environ.get("PPW_XUNIT_RUNTIME")
    require(runtime_path, "the protected prebuilt test-host runtime manifest is required")
    runtime = json.loads(Path(runtime_path).read_text())
    binary = validate_runtime(runtime)
    dotnet = os.environ.get("PPW_REAL_DOTNET") or shutil.which("dotnet")
    require(dotnet, "dotnet executable is required")
    environment = dict(os.environ, CALOR_P0_SHIM_OFF="1", NUGET_PACKAGES=runtime["packages"])
    if not options.no_build:
        command = [dotnet, "build", str(project), "--nologo", "-v", options.verbosity,
                   "-c", options.configuration]
        if options.no_restore:
            command.append("--no-restore")
        result = subprocess.run(command, env=environment)
        if result.returncode:
            return result.returncode
    result = subprocess.run([dotnet, "msbuild", str(project), "-nologo", "-getProperty:TargetPath",
                             "-property:Configuration=" + options.configuration],
                            capture_output=True, text=True, env=environment, check=True)
    target = Path(result.stdout.strip())
    require(target.is_absolute() and target.is_file(), "compiled test assembly is missing")
    return subprocess.run([dotnet, str(binary), str(target)], env=environment).returncode


def main():
    if sys.argv[1:] == ["--prepare"]:
        print(json.dumps(prepare(package_root()), sort_keys=True))
        return 0
    return run(sys.argv[1:])


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, KeyError, subprocess.SubprocessError) as error:
        print("PPW_XUNIT_INFRASTRUCTURE_ERROR: " + str(error), file=sys.stderr)
        sys.exit(2)
