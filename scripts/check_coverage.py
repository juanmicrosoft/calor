#!/usr/bin/env python3
import argparse
import json
import re
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


def evaluate(reports: list[Path], baseline_path: Path) -> tuple[dict, list[str]]:
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))["components"]
    lines: dict[str, dict[tuple[str, int], int]] = {name: {} for name in baseline}
    branches: dict[str, dict[tuple[str, int], tuple[int, int]]] = {
        name: {} for name in baseline
    }

    for report in reports:
        for cls in ET.parse(report).findall(".//class"):
            raw_path = cls.attrib["filename"].replace("\\", "/")
            marker = "Calor.Compiler/"
            if marker not in raw_path:
                continue
            source_path = raw_path.split(marker, 1)[1]
            if source_path.startswith("obj/"):
                continue

            for name, limits in baseline.items():
                if not source_path.startswith(limits["path"]):
                    continue
                for line in cls.findall("./lines/line"):
                    key = (source_path, int(line.attrib["number"]))
                    lines[name][key] = max(
                        lines[name].get(key, 0), int(line.attrib.get("hits", "0"))
                    )
                    condition = line.attrib.get("condition-coverage")
                    if condition:
                        match = re.search(r"\((\d+)/(\d+)\)", condition)
                        if match:
                            covered, total = map(int, match.groups())
                            previous = branches[name].get(key, (0, total))
                            branches[name][key] = (
                                max(previous[0], covered),
                                max(previous[1], total),
                            )

    results = {"schemaVersion": 1, "components": {}}
    failures: list[str] = []
    for name, limits in baseline.items():
        line_total = len(lines[name])
        line_covered = sum(hits > 0 for hits in lines[name].values())
        branch_total = sum(total for _, total in branches[name].values())
        branch_covered = sum(covered for covered, _ in branches[name].values())
        line_rate = 100 * line_covered / line_total if line_total else 0
        branch_rate = 100 * branch_covered / branch_total if branch_total else 0
        results["components"][name] = {
            "line": round(line_rate, 2),
            "branch": round(branch_rate, 2),
            "lineCovered": line_covered,
            "lineTotal": line_total,
            "branchCovered": branch_covered,
            "branchTotal": branch_total,
        }
        for metric, actual in (("line", line_rate), ("branch", branch_rate)):
            required = float(limits[metric])
            if actual + 1e-9 < required:
                failures.append(
                    f"{name} {metric} coverage {actual:.2f}% is below {required:.2f}%"
                )
    return results, failures


def self_test() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        baseline = root / "baseline.json"
        baseline.write_text(
            json.dumps(
                {
                    "components": {
                        "binder": {"path": "Binding/", "line": 100, "branch": 100}
                    }
                }
            ),
            encoding="utf-8",
        )
        report = root / "coverage.xml"
        report.write_text(
            """<coverage><packages><package><classes>
<class filename="Calor.Compiler/Binding/Binder.cs"><lines>
<line number="1" hits="1" branch="true" condition-coverage="50% (1/2)" />
<line number="2" hits="0" />
</lines></class>
</classes></package></packages></coverage>""",
            encoding="utf-8",
        )
        _, failures = evaluate([report], baseline)
        if len(failures) != 2:
            raise AssertionError(f"expected line and branch regressions, got {failures}")
    print("Coverage ratchet negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reports", default="artifacts/coverage/raw")
    parser.add_argument("--baseline", default="eng/coverage-baselines.json")
    parser.add_argument("--output", default="artifacts/coverage/coverage-summary.json")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0

    reports = sorted(Path(args.reports).rglob("coverage.cobertura.xml"))
    if not reports:
        print(f"ERROR: no coverage reports found under {args.reports}")
        return 1
    results, failures = evaluate(reports, Path(args.baseline))
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    for name, metrics in results["components"].items():
        print(f"{name}: line {metrics['line']:.2f}%, branch {metrics['branch']:.2f}%")
    for failure in failures:
        print(f"ERROR: {failure}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
