#!/usr/bin/env python3
"""Parse source with the shared pinned compiler, without running task code."""
import argparse
import fcntl
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
TOOL = ROOT / "source-inspection"


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def assembly():
    spec = importlib.util.spec_from_file_location("ppw_assembly", ROOT / "ppw-source-assembly.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def prepare(compiler):
    compiler = Path(compiler).resolve(strict=True)
    fingerprint = hashlib.sha256(
        compiler.read_bytes() + (TOOL / "Program.cs").read_bytes()
        + (TOOL / "PpwSourceInspector.csproj").read_bytes()).hexdigest()
    cache = TOOL / "cache" / fingerprint
    cache.mkdir(parents=True, exist_ok=True)
    binary = cache / "bin/ppw-source-inspector.dll"
    with (cache / "build.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if not binary.is_file():
            result = subprocess.run(
                ["dotnet", "build", str(TOOL / "PpwSourceInspector.csproj"),
                 "--configuration", "Release", "--output", str(cache / "bin"),
                 "--property:BaseIntermediateOutputPath=" + str(cache / "obj") + "/",
                 "--property:CalorCompilerDll=" + str(compiler), "--verbosity", "quiet"],
                capture_output=True, text=True, timeout=180)
            (cache / "build.log").write_text(result.stdout + result.stderr, encoding="utf-8")
            if result.returncode or not binary.is_file():
                raise ValueError("pinned source inspector failed to build: " + str(cache / "build.log"))
        if sha(binary.parent / "calor.dll") != sha(compiler):
            raise ValueError("source inspector does not load the shared pinned compiler")
    return binary


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


def inspect(pair, directories, compiler):
    binary = prepare(compiler)
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


def validate(report, pair, directories, compiler_sha):
    if (not isinstance(compiler_sha, str) or not re.fullmatch(r"[0-9a-f]{64}", compiler_sha)
            or report.get("schemaVersion") != 1 or report.get("compilerSha256") != compiler_sha):
        raise ValueError("source inspection uses a different compiler or schema")
    if not re.fullmatch(r"[0-9a-f]{64}", report.get("inspectorSha256", "")):
        raise ValueError("source inspection executable identity is missing")
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


def validate_controls(report, pair, directory, compiler_sha):
    directories = control_directories(pair, directory)
    validate(report, pair, directories, compiler_sha)
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
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--pair")
    parser.add_argument("--baseline")
    parser.add_argument("--final")
    args = parser.parse_args()
    if args.prepare:
        print(prepare(args.compiler))
    else:
        pair = json.loads(Path(args.pair).read_text(encoding="utf-8"))
        directories = (control_directories(pair, Path(args.pair).parent) if args.controls
                       else {"baseline": args.baseline, "final": args.final})
        report = inspect(pair, directories, args.compiler)
        if args.controls:
            validate_controls(report, pair, Path(args.pair).parent, sha(args.compiler))
        print(json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print("source inspection: " + str(error), file=sys.stderr)
        sys.exit(2)
