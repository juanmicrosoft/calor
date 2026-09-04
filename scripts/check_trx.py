#!/usr/bin/env python3
import argparse
import json
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


def read_counts(trx: Path) -> tuple[int, int, int]:
    root = ET.parse(trx).getroot()
    if root.tag.rsplit("}", 1)[-1] != "TestRun":
        raise ValueError("TRX root element is not TestRun")
    results_element = root.find("./{*}Results")
    definitions_element = root.find("./{*}TestDefinitions")
    counters = root.find("./{*}ResultSummary/{*}Counters")
    if results_element is None:
        raise ValueError("TRX has no Results element")
    if definitions_element is None:
        raise ValueError("TRX has no TestDefinitions element")
    if counters is None:
        raise ValueError("TRX has no Counters element")

    required_counters = ("total", "executed", "passed")
    if any(name not in counters.attrib for name in required_counters):
        raise ValueError("TRX is missing required counter attributes")
    total, executed, passed = (
        int(counters.attrib[name]) for name in required_counters
    )

    definitions = {
        definition.attrib.get("id")
        for definition in definitions_element.findall("./{*}UnitTest")
        if definition.attrib.get("id")
    }
    results = results_element.findall("./{*}UnitTestResult")
    if len(results) != total:
        raise ValueError(
            f"counter total {total} does not match {len(results)} result records"
        )
    actual_passed = 0
    actual_executed = 0
    for result in results:
        test_id = result.attrib.get("testId")
        if not test_id or test_id not in definitions:
            raise ValueError("TRX result has no matching test definition")
        outcome = result.attrib.get("outcome")
        if outcome not in {"Passed", "Failed", "NotExecuted", "Skipped"}:
            raise ValueError(f"TRX result has invalid outcome: {outcome}")
        if outcome not in {"NotExecuted", "Skipped"}:
            actual_executed += 1
        if outcome == "Passed":
            actual_passed += 1
    if (actual_executed, actual_passed) != (executed, passed):
        raise ValueError("TRX counters do not match concrete result outcomes")
    return total, executed, passed


def validate(trx, project: str, manifest_path: Path,
             submodules: bool = False) -> list[str]:
    """`submodules` selects the skip expectation for the CALLER'S CHECKOUT.

    A corpus-gated test skips when bench/corpus/ is absent and runs when it is
    present, so `expectedSkipped` is a property of (project x checkout) and not
    of the project. `.github/workflows/test.yml:375` gives the enforcement shard
    `submodules: false`; `publish-nuget.yml:72` gives EVERY shard
    `submodules: recursive`. Applying one number to both is what skipped the
    v0.16.0 publish (run 33568423848: executed 640, expected 639, with all 640
    PASSING) — a release-path-only failure no PR could surface, because no PR
    runs that checkout.
    """
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entry = next((item for item in manifest["projects"] if item["path"] == project), None)
    if entry is None:
        return [f"project is not in test manifest: {project}"]
    # #1150: a project may be run as SEVERAL invocations in one job — the
    # tests (compiler) shard is split so each `dotnet test` gets a fresh test
    # host, bounding how much memory any one host accumulates. Counts are summed
    # across the parts and checked against the SAME expectedTotal, which makes
    # the split self-verifying: if the filters overlap, miss a test, or one part
    # silently fails to run, the sum stops matching and this gate goes red.
    reports = [trx] if isinstance(trx, Path) else list(trx)
    if not reports:
        return ["no TRX report given"]
    total = executed = passed = 0
    for report in reports:
        if not report.is_file():
            return [f"TRX report does not exist: {report}"]
        try:
            part_total, part_executed, part_passed = read_counts(report)
        except (ET.ParseError, ValueError) as error:
            return [f"invalid TRX report {report}: {error}"]
        total += part_total
        executed += part_executed
        passed += part_passed
    expected_total = int(entry["expectedTotal"])
    # A project that declares `corpusDependent` MUST declare both skip counts.
    # Falling back silently is how this bug class reappears: add a corpus-gated
    # Skip.IfNot to some project, watch test.yml (submodules: false) fail on the
    # PR, bump `expectedSkipped` 0 -> 1, go green — and then publish-nuget runs
    # the same project WITH the corpus, sees 0 skips against an expectation of 1,
    # and blocks the release. Invisible until release day, which is exactly the
    # failure #1149 fixed for Calor.Enforcement.Tests. So the manifest declares
    # the dependency and the checker refuses to guess.
    corpus_dependent = bool(entry.get("corpusDependent", False))
    if corpus_dependent and "expectedSkippedWithSubmodules" not in entry:
        return [f"{project}: corpusDependent is true but expectedSkippedWithSubmodules is "
                f"missing. A corpus-gated test skips without submodules and RUNS with them, so "
                f"the two checkouts need two numbers (test.yml:375 vs publish-nuget.yml:72)."]
    if not corpus_dependent and "expectedSkippedWithSubmodules" in entry:
        return [f"{project}: declares expectedSkippedWithSubmodules but not corpusDependent. "
                f"Set corpusDependent: true, or drop the key."]
    key = ("expectedSkippedWithSubmodules"
           if submodules and corpus_dependent
           else "expectedSkipped")
    expected_skipped = int(entry[key])
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
            '<TestRun xmlns="urn:test"><ResultSummary><Counters total="2" '
            'executed="2" passed="2" /></ResultSummary></TestRun>',
            encoding="utf-8",
        )
        if not validate(trx, "tests/Example.Tests/Example.Tests.csproj", manifest):
            raise AssertionError("counter-only TRX was accepted")

        # The (project x checkout) skip expectation. A corpus-gated test skips
        # without submodules and RUNS with them, so the same TRX must be valid
        # under exactly one of the two flags — and INVALID under the other, or
        # the flag is decorative. This is the case that skipped the v0.16.0
        # publish; it is pinned here so it cannot recur silently.
        project = "tests/Corpus.Tests/Corpus.Tests.csproj"
        manifest.write_text(
            json.dumps(
                {
                    "projects": [
                        {
                            "path": project,
                            "corpusDependent": True,
                            "expectedTotal": 640,
                            "expectedSkipped": 1,
                            "expectedSkippedWithSubmodules": 0,
                        }
                    ]
                }
            ),
            encoding="utf-8",
        )

        def write(total: int, executed: int) -> Path:
            """A TRX with CONCRETE results, because read_counts cross-checks the
            counters against them — the summary alone is not trusted."""
            path = root / f"r{executed}.trx"
            defs = "".join(f'<UnitTest id="t{i}" />' for i in range(total))
            rows = "".join(
                f'<UnitTestResult testId="t{i}" '
                f'outcome="{"Passed" if i < executed else "Skipped"}" />'
                for i in range(total))
            path.write_text(
                f'<TestRun xmlns="urn:test"><Results>{rows}</Results>'
                f'<TestDefinitions>{defs}</TestDefinitions>'
                f'<ResultSummary><Counters total="{total}" executed="{executed}" '
                f'passed="{executed}" /></ResultSummary></TestRun>',
                encoding="utf-8",
            )
            return path

        with_corpus = write(640, 640)     # nothing skipped: submodules present
        without_corpus = write(640, 639)  # one corpus-gated skip

        if validate(with_corpus, project, manifest, submodules=True):
            raise AssertionError("submodules checkout rejected its own skip expectation")
        if not validate(with_corpus, project, manifest, submodules=False):
            raise AssertionError(
                "a no-submodules checkout accepted a run with no corpus skip — the flag is "
                "decorative, which is the defect that skipped the v0.16.0 publish")
        if validate(without_corpus, project, manifest, submodules=False):
            raise AssertionError("no-submodules checkout rejected its own skip expectation")
        if not validate(without_corpus, project, manifest, submodules=True):
            raise AssertionError("submodules checkout accepted a run that skipped the corpus test")

        # Absent the declaration, one number governs both checkouts.
        manifest.write_text(
            json.dumps({"projects": [{"path": project, "expectedTotal": 640,
                                      "expectedSkipped": 1}]}),
            encoding="utf-8",
        )
        if validate(without_corpus, project, manifest, submodules=True):
            raise AssertionError("a project with no corpus-gated skips must ignore --submodules")

        # THE FALLBACK MUST NOT BE SILENT. A project declared corpus-dependent
        # without the second count is the shape that reappears as a release-only
        # failure, so it is refused rather than guessed at — and the converse
        # (the key without the declaration) is refused too, so the two cannot
        # drift apart.
        manifest.write_text(
            json.dumps({"projects": [{"path": project, "corpusDependent": True,
                                      "expectedTotal": 640, "expectedSkipped": 1}]}),
            encoding="utf-8",
        )
        errors = validate(without_corpus, project, manifest, submodules=True)
        if not any("expectedSkippedWithSubmodules is missing" in e for e in errors):
            raise AssertionError(
                "corpusDependent without the second count was accepted — the silent fallback is "
                "back, and with it the release-only failure #1149 fixed")
        manifest.write_text(
            json.dumps({"projects": [{"path": project, "expectedTotal": 640,
                                      "expectedSkipped": 1,
                                      "expectedSkippedWithSubmodules": 0}]}),
            encoding="utf-8",
        )
        if not any("not corpusDependent" in e
                   for e in validate(without_corpus, project, manifest, submodules=False)):
            raise AssertionError("the key without the declaration was accepted")
    print("TRX inventory negative self-test passed.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--trx", type=Path, action="append",
        help="TRX report. Repeatable: a project run as several invocations (see the "
             "split tests (compiler) shard, #1150) passes one --trx per part and the "
             "counts are summed against the single expectedTotal.")
    parser.add_argument("--project")
    parser.add_argument("--manifest", type=Path, default=Path("eng/test-manifest.json"))
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument(
        "--submodules", action="store_true",
        help="the caller checked out bench/corpus/ (submodules: recursive), so corpus-gated "
             "tests RUN instead of skipping; use expectedSkippedWithSubmodules where the "
             "manifest declares one")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if not args.trx or args.project is None:
        parser.error("--trx and --project are required")
    errors = validate(args.trx, args.project, args.manifest, submodules=args.submodules)
    for error in errors:
        print(f"ERROR: {error}")
    if not errors:
        print(f"TRX inventory matches manifest for {args.project}.")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
