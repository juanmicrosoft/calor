#!/usr/bin/env python3
"""Fail-closed checks for CI test inventory, skips, and vacuous assertions."""

from __future__ import annotations

import argparse
import json
import re
import tempfile
from pathlib import Path


TEST_PROJECT_MARKERS = ("Microsoft.NET.Test.Sdk", "<IsTestProject>true</IsTestProject>")
SKIP_PATTERN = re.compile(
    r"\[(?:Fact|Theory)\s*\(\s*Skip\s*=\s*\"([^\"]+)\"\s*\)\]"
)
VACUOUS_PATTERNS = (
    (
        re.compile(r"Assert\.True\s*\((?:(?!;).)*\|\|\s*true\b", re.DOTALL),
        "assertion contains '|| true'",
    ),
    (
        re.compile(r"Assert\.True\s*\((?:(?!;).)*\.Count\s*>=\s*0\b", re.DOTALL),
        "count is always non-negative",
    ),
)


def discover_test_projects(root: Path) -> set[str]:
    discovered: set[str] = set()
    for project in (root / "tests").rglob("*.csproj"):
        text = project.read_text(encoding="utf-8")
        if any(marker in text for marker in TEST_PROJECT_MARKERS):
            discovered.add(project.relative_to(root).as_posix())
    return discovered


def check_manifest(root: Path, manifest: dict) -> list[str]:
    errors: list[str] = []
    projects = {item["path"] for item in manifest["projects"]}
    excluded = {item["path"] for item in manifest["excludedProjects"]}
    duplicates = len(projects) != len(manifest["projects"])
    if duplicates:
        errors.append("test manifest contains duplicate project paths")

    discovered = discover_test_projects(root)
    missing = sorted(discovered - projects - excluded)
    stale = sorted(projects - discovered)
    missing_exclusions = sorted(path for path in excluded if not (root / path).is_file())
    if missing:
        errors.append(f"test projects omitted from manifest: {', '.join(missing)}")
    if stale:
        errors.append(f"manifest paths are not test projects: {', '.join(stale)}")
    if missing_exclusions:
        errors.append(f"excluded project paths do not exist: {', '.join(missing_exclusions)}")

    for item in manifest["projects"]:
        workflow = root / item["workflow"]
        if not workflow.is_file():
            errors.append(f"workflow does not exist for {item['path']}: {item['workflow']}")
            continue
        if item["path"] not in workflow.read_text(encoding="utf-8"):
            errors.append(f"workflow does not reference declared test project: {item['path']}")

    release_workflow = root / ".github/workflows/publish-nuget.yml"
    if not release_workflow.is_file():
        errors.append("release workflow does not exist")
    else:
        release_text = release_workflow.read_text(encoding="utf-8")
        for gate in manifest.get("releaseGates", []):
            if not re.search(rf"(?m)^  {re.escape(gate)}:", release_text):
                errors.append(f"release workflow is missing declared gate job: {gate}")
        needs_match = re.search(r"(?m)^    needs:\s*\[([^\]]+)\]", release_text)
        needs = (
            {item.strip() for item in needs_match.group(1).split(",")}
            if needs_match
            else set()
        )
        missing_needs = set(manifest.get("releaseGates", [])) - needs
        if missing_needs:
            errors.append(
                "publish job does not depend on release gates: "
                + ", ".join(sorted(missing_needs))
            )
    return errors


def check_skips(root: Path, manifest: dict) -> list[str]:
    actual: dict[str, list[str]] = {}
    for source in (root / "tests").rglob("*.cs"):
        text = source.read_text(encoding="utf-8")
        for match in SKIP_PATTERN.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            actual.setdefault(match.group(1), []).append(
                f"{source.relative_to(root).as_posix()}:{line}"
            )

    allowed = {item["reason"]: item["count"] for item in manifest["allowedSkippedTests"]}
    errors: list[str] = []
    for reason, locations in actual.items():
        if reason not in allowed:
            errors.append(f"unapproved skipped tests ({reason}): {', '.join(locations)}")
        elif len(locations) != allowed[reason]:
            errors.append(
                f"skip count changed for '{reason}': expected {allowed[reason]}, got {len(locations)}"
            )
    for reason, count in allowed.items():
        if len(actual.get(reason, [])) != count:
            errors.append(
                f"allowed skip inventory missing '{reason}': expected {count}, got {len(actual.get(reason, []))}"
            )
    return errors


def check_vacuous_assertions(root: Path) -> list[str]:
    errors: list[str] = []
    for source in (root / "tests").rglob("*.cs"):
        text = source.read_text(encoding="utf-8")
        for pattern, description in VACUOUS_PATTERNS:
            for match in pattern.finditer(text):
                line = text.count("\n", 0, match.start()) + 1
                errors.append(
                    f"{source.relative_to(root).as_posix()}:{line}: {description}"
                )
    return errors


def run_checks(root: Path, manifest_path: Path) -> list[str]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    return (
        check_manifest(root, manifest)
        + check_skips(root, manifest)
        + check_vacuous_assertions(root)
    )


def self_test(root: Path, manifest_path: Path) -> None:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    with tempfile.TemporaryDirectory() as temp_dir:
        fixture = Path(temp_dir)
        (fixture / "tests" / "Missing.Tests").mkdir(parents=True)
        (fixture / "tests" / "Missing.Tests" / "Missing.Tests.csproj").write_text(
            '<Project><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /></ItemGroup></Project>',
            encoding="utf-8",
        )
        (fixture / "tests" / "Missing.Tests" / "BadTests.cs").write_text(
            '[Fact(Skip = "hidden skip")] void Skipped() { }\n'
            "void Vacuous() { Assert.True(items.Count >= 0); }\n",
            encoding="utf-8",
        )
        fixture_manifest = fixture / "manifest.json"
        fixture_manifest.write_text(json.dumps(manifest), encoding="utf-8")
        errors = run_checks(fixture, fixture_manifest)
        expected = ("omitted from manifest", "unapproved skipped tests", "always non-negative")
        for fragment in expected:
            if not any(fragment in error for error in errors):
                raise SystemExit(f"self-test failed to detect: {fragment}")
    print("Test-quality negative self-tests passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    root = args.root.resolve()
    manifest = (args.manifest or root / "eng" / "test-manifest.json").resolve()
    if args.self_test:
        self_test(root, manifest)
        return 0

    errors = run_checks(root, manifest)
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1
    print("Test manifest, skip inventory, and assertion-quality checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
