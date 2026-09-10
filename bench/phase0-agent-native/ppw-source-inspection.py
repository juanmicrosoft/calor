#!/usr/bin/env python3
"""Parse source with the shared pinned compiler, without running task code."""
import argparse
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
TOOL = ROOT / "source-inspection"
RUNTIME_KIND = "ppw-source-inspector-runtime-v1"
SOURCE_FILES = ("Program.cs", "PpwSourceInspector.csproj")


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def assembly():
    spec = importlib.util.spec_from_file_location("ppw_assembly", ROOT / "ppw-source-assembly.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def source_hashes():
    return {name: sha(TOOL / name) for name in SOURCE_FILES}


def runtime_files(directory):
    directory = Path(directory)
    return {path.relative_to(directory).as_posix(): sha(path)
            for path in sorted(directory.rglob("*"))
            if path.is_file() and path.name != "runtime.json"
            and path.suffix in (".dll", ".json")}


def runtime_identity(cache, compiler):
    binary = cache / "bin/ppw-source-inspector.dll"
    files = runtime_files(binary.parent)
    if binary.name not in files or "calor.dll" not in files:
        raise ValueError("source inspector runtime is incomplete")
    runtime = {
        "schemaVersion": 1,
        "kind": RUNTIME_KIND,
        "binary": str(binary),
        "manifest": str(cache / "runtime.json"),
        "compilerSha256": sha(compiler),
        "sources": source_hashes(),
        "files": files,
    }
    runtime["runtimeSha256"] = hashlib.sha256(json.dumps(
        runtime, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    return runtime


def validate_runtime(runtime, compiler=None):
    fields = {"schemaVersion", "kind", "binary", "manifest", "compilerSha256",
              "sources", "files", "runtimeSha256"}
    if (not isinstance(runtime, dict) or set(runtime) != fields
            or runtime.get("schemaVersion") != 1 or runtime.get("kind") != RUNTIME_KIND
            or runtime.get("sources") != source_hashes()):
        raise ValueError("source inspector runtime identity differs")
    unsigned = {name: value for name, value in runtime.items() if name != "runtimeSha256"}
    if runtime["runtimeSha256"] != hashlib.sha256(json.dumps(
            unsigned, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()).hexdigest():
        raise ValueError("source inspector runtime identity hash differs")
    manifest = Path(runtime["manifest"])
    binary = Path(runtime["binary"])
    if (not manifest.is_absolute() or not binary.is_absolute() or not manifest.is_file()
            or not binary.is_file() or binary.parent.parent != manifest.parent
            or binary.name != "ppw-source-inspector.dll"
            or manifest.is_symlink() or binary.is_symlink()
            or manifest.stat().st_nlink != 1 or binary.stat().st_nlink != 1
            or any(path.is_symlink() for path in binary.parents)):
        raise ValueError("source inspector runtime paths are missing, linked, or misplaced")
    if json.loads(manifest.read_text(encoding="utf-8")) != runtime:
        raise ValueError("source inspector runtime manifest changed")
    runtime_paths = [path for path in binary.parent.rglob("*")
                     if path.is_file() and path.name != "runtime.json"
                     and path.suffix in (".dll", ".json")]
    if (any(path.is_symlink() or path.stat().st_nlink != 1
            or any(parent.is_symlink() for parent in path.parents
                   if parent != binary.parent.parent)
            for path in runtime_paths)
            or runtime_files(binary.parent) != runtime["files"]):
        raise ValueError("source inspector runtime dependency bytes changed")
    if runtime["files"].get("ppw-source-inspector.dll") != sha(binary):
        raise ValueError("source inspector executable bytes changed")
    if runtime["files"].get("calor.dll") != runtime["compilerSha256"]:
        raise ValueError("source inspector adjacent compiler differs")
    if compiler is not None and sha(Path(compiler).resolve(strict=True)) != runtime["compilerSha256"]:
        raise ValueError("source inspector compiler differs from the registered runtime")
    return binary


def build_runtime(compiler, directory):
    result = subprocess.run(
        ["dotnet", "build", str(TOOL / "PpwSourceInspector.csproj"),
         "--configuration", "Release", "--output", str(directory / "bin"),
         "--property:BaseIntermediateOutputPath=" + str(directory / "obj") + "/",
         "--property:CalorCompilerDll=" + str(compiler), "--verbosity", "quiet"],
        capture_output=True, text=True, timeout=180)
    (directory / "build.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    binary = directory / "bin/ppw-source-inspector.dll"
    if result.returncode or not binary.is_file():
        raise ValueError("pinned source inspector failed to build: " + str(directory / "build.log"))
    if sha(binary.parent / "calor.dll") != sha(compiler):
        raise ValueError("source inspector does not load the shared pinned compiler")


def prepare(compiler):
    compiler = Path(compiler).resolve(strict=True)
    fingerprint = hashlib.sha256(
        json.dumps({"builder": 4, "compiler": sha(compiler), "sources": source_hashes()},
                   sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    cache = TOOL / "cache" / fingerprint
    if os.environ.get("PPW_INSPECTOR_READONLY") == "1":
        manifest = cache / "runtime.json"
        if not manifest.is_file():
            raise ValueError("isolated source inspection requires the prebuilt pinned inspector")
        runtime = json.loads(manifest.read_text(encoding="utf-8"))
        validate_runtime(runtime, compiler)
        return runtime
    TOOL.joinpath("cache").mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)
    with (cache / "build.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        staging = TOOL / "cache/.prospective" / fingerprint
        if staging.exists():
            shutil.rmtree(staging)
        staging.mkdir(parents=True)
        try:
            build_runtime(compiler, staging)
            candidate = runtime_identity(staging, compiler)
            binary = cache / "bin/ppw-source-inspector.dll"
            if binary.exists():
                existing = runtime_identity(cache, compiler)
                if existing["files"] != candidate["files"]:
                    raise ValueError("prepopulated source inspector cache differs from a trusted rebuild")
            else:
                shutil.copytree(staging / "bin", cache / "bin")
            runtime = runtime_identity(cache, compiler)
            manifest = cache / "runtime.json"
            if manifest.exists():
                if json.loads(manifest.read_text(encoding="utf-8")) != runtime:
                    raise ValueError("existing source inspector runtime manifest differs")
            else:
                with manifest.open("x", encoding="utf-8") as stream:
                    json.dump(runtime, stream, sort_keys=True, allow_nan=False)
                    stream.write("\n")
        finally:
            shutil.rmtree(staging, ignore_errors=True)
        validate_runtime(runtime, compiler)
        return runtime


def inputs(pair, directory, name):
    module = assembly()
    paths = module.source_paths(pair, directory)
    editable = set(module.source_paths(pair, directory, editable_only=True))
    if not paths:
        raise ValueError("source inspection requires archived source")
    groups = [paths] if module.definition(pair) else [[path] for path in paths]
    requests = []
    for index, group in enumerate(groups):
        chunks, ranges, offset = [], [], 0
        for path in group:
            text = path.read_bytes().decode("utf-8")
            if not chunks:
                text = text.removeprefix("\ufeff")
            if chunks:
                offset += 1
            length = len(text.encode("utf-16-le")) // 2
            if path in editable:
                ranges.append([offset, offset + length])
            chunks.append(text)
            offset += length
        requests.append({"name": name + ":" + str(index),
                         "text": "\n".join(chunks), "editableRanges": ranges})
    hashes = {path.relative_to(directory).as_posix(): sha(path) for path in paths}
    return requests, hashes


def inspect(pair, directories, compiler, runtime=None):
    runtime = prepare(compiler) if runtime is None else runtime
    binary = validate_runtime(runtime, compiler)
    requests, hashes = [], {}
    for name, directory in directories.items():
        source_requests, hashes[name] = inputs(pair, Path(directory), name)
        requests.extend(source_requests)
    result = subprocess.run(["dotnet", str(binary)], input=json.dumps({"sources": requests}),
                            capture_output=True, text=True, timeout=60)
    if result.returncode:
        raise ValueError("pinned source inspector failed: " + result.stderr[:2000])
    report = json.loads(result.stdout)
    if report.get("compilerSha256") != sha(compiler):
        raise ValueError("source inspector compiler identity differs from the pinned DLL")
    report["inputSha256"] = hashes
    report["inspectorSha256"] = sha(binary)
    report["inspectorRuntimeSha256"] = runtime["runtimeSha256"]
    return report


def control_directories(pair, directory):
    spec = importlib.util.spec_from_file_location("ppw_capture", ROOT / "harness-capture.py")
    capture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(capture)
    _, honest = capture.honest_reference_cells(pair)
    if not isinstance(honest, dict) or set(honest) != {"a", "b"}:
        raise ValueError("explicit seeded.clean or seeded.honest controls required")
    laundering = pair.get("seeded", {}).get("laundering")
    if not isinstance(laundering, dict) or set(laundering) != {"a", "b"}:
        raise ValueError("explicit seeded.laundering controls required")
    root = Path(directory).resolve()
    directories = {}
    for arm in ("a", "b"):
        for role, relative in (("starter", "starter-" + arm),
                               ("honest", honest[arm]),
                               ("laundering", laundering[arm])):
            if not isinstance(relative, str):
                raise ValueError("source inspection control path must be a string")
            path = root / relative
            if path.is_symlink() or not path.resolve().is_relative_to(root):
                raise ValueError("source inspection control escapes its task")
            directories[role + "-" + arm] = path
    return directories


def validate(report, pair, directories, compiler_sha, inspector_runtime=None):
    if (not isinstance(compiler_sha, str) or not re.fullmatch(r"[0-9a-f]{64}", compiler_sha)
            or report.get("schemaVersion") != 1 or report.get("compilerSha256") != compiler_sha):
        raise ValueError("source inspection uses a different compiler or schema")
    if not re.fullmatch(r"[0-9a-f]{64}", report.get("inspectorSha256", "")):
        raise ValueError("source inspection executable identity is missing")
    if inspector_runtime is not None:
        validate_runtime(inspector_runtime)
        if (report.get("inspectorSha256")
                != inspector_runtime["files"].get("ppw-source-inspector.dll")
                or report.get("inspectorRuntimeSha256") != inspector_runtime["runtimeSha256"]):
            raise ValueError("source inspection report differs from the registered inspector runtime")
    requests, hashes = [], {}
    for name, directory in directories.items():
        source_requests, hashes[name] = inputs(pair, Path(directory), name)
        requests.extend(source_requests)
    if report.get("inputSha256") != hashes:
        raise ValueError("source inspection input hashes differ from archived source")
    sources = report.get("sources", {})
    if not isinstance(sources, dict) or set(sources) != {item["name"] for item in requests}:
        raise ValueError("source inspection inventory differs from archived source")
    for source in sources.values():
        if (not isinstance(source, dict) or type(source.get("parseOk")) is not bool
                or not isinstance(source.get("calls"), list)
                or any(not isinstance(call, str) or not re.fullmatch(r"§C\{[^{}\r\n]+\}", call)
                       for call in source["calls"])
                or (source["parseOk"] and not isinstance(source.get("publicApi"), dict))):
            raise ValueError("malformed native source inspection")


def observation(report, name):
    sources = [value for key, value in sorted(report["sources"].items())
               if key.startswith(name + ":")]
    if not sources or not all(value.get("parseOk") is True for value in sources):
        return {"parseOk": False, "calls": [], "publicApi": None}
    return {"parseOk": True, "calls": sorted(set(
        call for value in sources for call in value["calls"])),
        "publicApi": [value["publicApi"] for value in sources]}


def validate_controls(report, pair, directory, compiler_sha, inspector_runtime=None):
    directories = control_directories(pair, directory)
    validate(report, pair, directories, compiler_sha, inspector_runtime)
    regex = re.compile(pair["shapeRealizedIndicator"]["sourceRegex"])
    baseline = observation(report, "starter-a")["publicApi"]
    for name in directories:
        value = observation(report, name)
        if not value["parseOk"]:
            raise ValueError("source inspection cannot parse control: " + name)
        if value["publicApi"] != baseline:
            raise ValueError("public API changed in control: " + name)
        realized = any(regex.search(call) for call in value["calls"])
        if realized != name.startswith("laundering-"):
            role = ("misses its laundering positive" if name.startswith("laundering-")
                    else "matches its honest negative" if name.startswith("honest-")
                    else "matches its starter")
            raise ValueError("shape indicator " + role + " control: " + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler", required=True)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--validate-runtime", action="store_true")
    parser.add_argument("--runtime-manifest")
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--pair")
    parser.add_argument("--baseline")
    parser.add_argument("--final")
    args = parser.parse_args()
    if args.prepare:
        print(json.dumps(prepare(args.compiler), sort_keys=True))
    elif args.validate_runtime:
        if not args.runtime_manifest:
            raise ValueError("--validate-runtime requires --runtime-manifest")
        runtime = json.loads(Path(args.runtime_manifest).read_text(encoding="utf-8"))
        print(validate_runtime(runtime, args.compiler))
    else:
        pair = json.loads(Path(args.pair).read_text(encoding="utf-8"))
        directories = (control_directories(pair, Path(args.pair).parent) if args.controls
                       else {"baseline": args.baseline, "final": args.final})
        runtime = (json.loads(Path(args.runtime_manifest).read_text(encoding="utf-8"))
                   if args.runtime_manifest else None)
        report = inspect(pair, directories, args.compiler, runtime)
        if args.controls:
            validate_controls(report, pair, Path(args.pair).parent, sha(args.compiler), runtime)
        print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print("source inspection: " + str(error), file=sys.stderr)
        sys.exit(2)
