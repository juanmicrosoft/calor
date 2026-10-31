#!/usr/bin/env python3
"""Measure default migrate on isolated pinned corpus source copies (#1604).

This is the directory-mode measurement, not the project-context round-trip
harness. Build/test status is explicitly not-run: writing a .calr file is not
evidence that a subject builds or that its tests pass. Existing harness gates
remain authoritative for those independent dimensions.
"""

from __future__ import annotations

import argparse
from collections import Counter
import fnmatch
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


PROJECTS = {
    "MediatR": "MediatR/src/MediatR",
    "Serilog": "serilog/src/Serilog",
    "FluentValidation": "FluentValidation/src/FluentValidation",
}
PRESERVATION_PATHS = {"interop", "rescue", "passthrough"}
BLOCKING_FEATURES = {"project-validation", "roundtrip-csharp-validation"}


def summarize(envelope: dict, source_root: Path) -> dict:
    if envelope.get("command") != "migrate":
        raise ValueError("Expected a migrate diagnostic envelope")
    data = envelope["data"]
    files = data["fileResults"]
    if not files or len(files) != data["summary"]["totalFiles"]:
        raise ValueError("Missing or incomplete per-file migration results")
    source_root = source_root.resolve()
    seen = set()
    counts = Counter()
    reasons = Counter()
    for file in files:
        source = Path(file["sourcePath"]).resolve()
        if not source.is_relative_to(source_root) or source in seen:
            raise ValueError("Duplicate or out-of-scope migration source")
        seen.add(source)
        status = file["status"]
        if type(status) is not int or status not in range(5):
            raise ValueError(f"Unknown migration status: {status!r}")
        output = Path(file["outputPath"]).resolve() if file["outputPath"] else None
        if output is not None and not output.is_relative_to(source_root):
            raise ValueError("Out-of-scope migration output")
        written = output is not None and output.is_file()
        losses = file["losses"]
        paths = file.get("conversionPaths")
        if status in (0, 1):
            if not written:
                raise ValueError("Successful migration result has no written output")
            counts["written"] += 1
            if (not losses and paths and paths["outcome"] == "native"
                    and paths["roundTrip"] in ("verified", "deferred")):
                counts["fully_native_written"] += 1
            for loss in losses:
                if loss["path"] in PRESERVATION_PATHS:
                    reasons[loss["feature"]] += 1
        elif written:
            raise ValueError("Unsuccessful migration result claims a written output")
        elif status == 3:
            counts["skipped"] += 1
        elif any(issue.get("feature") in BLOCKING_FEATURES for issue in file["issues"]):
            counts["converted_but_blocked"] += 1
        else:
            counts["genuinely_failed"] += 1
    all_sources = {path.resolve() for path in source_root.rglob("*.cs")}
    # ProjectDiscovery's default generated/global source exclusions are visible,
    # not mistaken for converter failures or removed from the inventory silently.
    excluded_sources = {
        path for path in all_sources
        if any(fnmatch.fnmatchcase(path.name, pattern) for pattern in (
            "*.g.cs", "*.Designer.cs", "GlobalUsings.cs", "AssemblyInfo.cs"))
    }
    expected_sources = all_sources - excluded_sources
    if seen != expected_sources:
        raise ValueError(
            "Migration report does not cover the copied source inventory: "
            f"missing={sorted(str(path) for path in expected_sources - seen)}, "
            f"unexpected={sorted(str(path) for path in seen - expected_sources)}")
    if set(source_root.rglob("*.calr")) != {
        Path(file["outputPath"]).resolve()
        for file in files if file["status"] in (0, 1)
    }:
        raise ValueError("Written output inventory disagrees with migration results")
    total = len(files)
    return {
        "total": total,
        "discovery_excluded_sources": sorted(
            str(path.relative_to(source_root)) for path in excluded_sources),
        **{key: counts[key] for key in (
            "written", "fully_native_written", "genuinely_failed",
            "converted_but_blocked", "skipped")},
        "native_file_fraction": counts["fully_native_written"] / total,
        "passthrough_units_by_reason": dict(sorted(reasons.items())),
        "build": {"status": "not-run"},
        "tests": {"status": "not-run"},
    }


def measure(root: Path, compiler: Path, output: Path, dotnet: str) -> list[dict]:
    output.mkdir(parents=True, exist_ok=True)
    results = []
    for project, relative in PROJECTS.items():
        upstream = root / "bench/corpus" / relative
        corpus = root / "bench/corpus" / relative.split("/")[0]
        pin = subprocess.check_output(
            ["git", "-C", str(root), "rev-parse", f"HEAD:bench/corpus/{corpus.name}"],
            text=True).strip()
        actual = subprocess.check_output(
            ["git", "-C", str(corpus), "rev-parse", "HEAD"], text=True).strip()
        dirty = subprocess.check_output(
            ["git", "-C", str(corpus), "status", "--porcelain", "--untracked-files=no"],
            text=True)
        if actual != pin or dirty:
            raise ValueError(f"{project}: corpus must be clean at its committed pin")
        with tempfile.TemporaryDirectory(prefix=f"calor-default-migrate-{project}-") as temp:
            sources = Path(temp) / "sources"
            for source in sorted(upstream.rglob("*.cs")):
                destination = sources / source.relative_to(upstream)
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, destination)
            if not sources.exists():
                raise ValueError(f"{project}: corpus sources missing; initialize submodules")
            raw_report = output / f"{project}-migrate.json"
            # Do not let a previous invocation's report stand in for a crashed CLI.
            raw_report.unlink(missing_ok=True)
            run = subprocess.run(
                [dotnet, str(compiler), "migrate", str(sources), "--report", str(raw_report)],
                capture_output=True, text=True, timeout=600, cwd=sources)
            (output / f"{project}-migrate.log").write_text(
                run.stdout + run.stderr, encoding="utf-8")
            if run.returncode not in (0, 1):
                raise ValueError(f"{project}: migrate crashed with exit {run.returncode}")
            result = summarize(json.loads(raw_report.read_text(encoding="utf-8")), sources)
            result.update(project=project, source_commit=pin, migrate_exit_code=run.returncode)
            results.append(result)
    return results


def markdown(results: list[dict]) -> str:
    lines = [
        "## Default directory-mode migrate (#1604)",
        "",
        "| Project | Failed | Converted but blocked | Written | Fully native files | Native file fraction | Build | Tests |",
        "|---|---:|---:|---:|---:|---:|---|---|",
    ]
    for result in results:
        lines.append(
            f"| {result['project']} | {result['genuinely_failed']} | "
            f"{result['converted_but_blocked']} | {result['written']}/{result['total']} | "
            f"{result['fully_native_written']} | {result['native_file_fraction']:.1%} | "
            f"{result['build']['status']} | {result['tests']['status']} |")
    lines += [
        "",
        "Native file fraction = fully native **written** files / discovered input files. "
        "Passthrough and unwritten conversions never count as native output. "
        "Per-project JSON reports list passthrough units by reason and sources "
        "excluded by default discovery (generated/global files).",
        "",
        "These isolated source-directory copies measure the default `calor migrate` "
        "path without lossy/passthrough flags. They do not form buildable projects. "
        "Build/test status is **not-run**, not passed; the separate round-trip "
        "harness still enforces its build, test, native and coverage floors.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--compiler", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    results = measure(args.root.resolve(), args.compiler.resolve(), args.output.resolve(), args.dotnet)
    (args.output / "default-migrate.json").write_text(
        json.dumps({"schema_version": 1, "projects": results}, indent=2) + "\n", encoding="utf-8")
    (args.output / "default-migrate.md").write_text(markdown(results), encoding="utf-8")
    incomplete = [
        result for result in results
        if result["written"] != result["total"]
        or any(result[key] for key in (
            "genuinely_failed", "converted_but_blocked", "skipped"))
        or result["migrate_exit_code"] != 0
    ]
    if incomplete:
        raise SystemExit(
            "Default migrate did not write every discovered input: "
            + ", ".join(
                f"{result['project']} {result['written']}/{result['total']} written"
                for result in incomplete))


if __name__ == "__main__":
    main()
