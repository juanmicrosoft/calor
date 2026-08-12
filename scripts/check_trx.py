#!/usr/bin/env python3
import argparse
import json
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


def read_counts(trx: Path) -> tuple[int, int, int]:
    counters = ET.parse(trx).find(".//{*}Counters")
    if counters is None:
        raise ValueError("TRX has no Counters element")
    total = int(counters.attrib.get("total", "0"))
    executed = int(counters.attrib.get("executed", "0"))
    passed = int(counters.attrib.get("passed", "0"))
    return total, executed, passed


def validate(trx: Path, project: str, manifest_path: Path) -> list[str]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entry = next((item for item in manifest["projects"] if item["path"] == project), None)
    if entry is None:
        return [f"project is not in test manifest: {project}"]
    if not trx.is_file():
        return [f"TRX report does not exist: {trx}"]
    try:
        total, executed, passed = read_counts(trx)
    except (ET.ParseError, ValueError) as error:
        return [f"invalid TRX report {trx}: {error}"]
    expected_total = int(entry["expectedTotal"])
    expected_skipped = int(entry["expectedSkipped"])
    expected_executed = expected_total - expected_skipped
    errors = []
    if total != expected_total:
        errors.append(f"{project}: total {total}, expected {expected_total}")
    if executed != expected_executed:
        errors.append(f"{project}: executed {executed}, expected {expected_executed}")
    if passed != expected_executed:
        errors.append(f"{project}: passed {passed}, expected {expected_executed}")
    return errors


def self_test() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        manifest = root / "manifest.json"
        manifest.write_text(
            json.dumps(
                {
                    "projects": [
                        {
                            "path": "tests/Example.Tests/Example.Tests.csproj",
                            "expectedTotal": 2,
                            "expectedSkipped": 0,
                        }
                    ]
                }
            ),
            encoding="utf-8",
        )
        trx = root / "results.trx"
        trx.write_text(
            '<TestRun xmlns="urn:test"><ResultSummary><Counters total="0" '
            'executed="0" passed="0" /></ResultSummary></TestRun>',
            encoding="utf-8",
        )
        if not validate(trx, "tests/Example.Tests/Example.Tests.csproj", manifest):
            raise AssertionError("zero-test TRX was accepted")
    print("TRX inventory negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--trx", type=Path)
    parser.add_argument("--project")
    parser.add_argument("--manifest", type=Path, default=Path("eng/test-manifest.json"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.trx is None or args.project is None:
        parser.error("--trx and --project are required")
    errors = validate(args.trx, args.project, args.manifest)
    for error in errors:
        print(f"ERROR: {error}")
    if not errors:
        print(f"TRX inventory matches manifest for {args.project}.")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
