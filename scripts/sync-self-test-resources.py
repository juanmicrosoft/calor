#!/usr/bin/env python3
"""Explicitly synchronize or verify compiler self-test resources."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys


def expected_resources(repo_root: Path) -> dict[str, bytes]:
    scenarios = repo_root / "tests/E2E/scenarios"
    expected: dict[str, bytes] = {}
    for scenario in sorted(path for path in scenarios.iterdir() if path.is_dir()):
        input_path = scenario / "input.calr"
        output_path = scenario / "output.g.cs"
        if input_path.is_file() and output_path.is_file():
            expected[f"{scenario.name}.calr"] = input_path.read_bytes()
            expected[f"{scenario.name}.g.cs"] = output_path.read_bytes()
    return expected


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
    )
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    destination = repo_root / "src/Calor.Compiler/Resources/SelfTest"
    expected = expected_resources(repo_root)
    existing = {
        path.name: path.read_bytes()
        for path in destination.iterdir()
        if path.is_file() and path.suffix in {".calr", ".cs"}
    }

    stale = sorted(
        name for name, content in expected.items() if existing.get(name) != content
    )
    extra = sorted(set(existing) - set(expected))
    if args.check:
        if stale or extra:
            print(
                f"ERROR: self-test resources are out of sync; "
                f"stale/missing={stale}, extra={extra}",
                file=sys.stderr,
            )
            print(
                "Run: python3 scripts/sync-self-test-resources.py",
                file=sys.stderr,
            )
            return 1
        print("Self-test resources match canonical E2E scenarios.")
        return 0

    destination.mkdir(parents=True, exist_ok=True)
    for name in extra:
        (destination / name).unlink()
    for name, content in expected.items():
        (destination / name).write_bytes(content)
    print(f"Synchronized {len(expected) // 2} self-test scenarios.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
