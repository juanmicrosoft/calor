#!/usr/bin/env python3
import argparse
import json
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Report:
    """What one test host wrote to one TRX file (#1666): its counters, and for every result the
    test id, outcome and the test assembly it came from."""
    total: int
    executed: int
    passed: int
    test_ids: list[str] = field(default_factory=list)
    not_passed: list[str] = field(default_factory=list)  # executed results that did not pass
    assemblies: set[str] = field(default_factory=set)


def read_report(trx: Path) -> Report:
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

    # #1666: each definition names the assembly the test host loaded it from.
    definitions = {}
    for definition in definitions_element.findall("./{*}UnitTest"):
        test_id = definition.attrib.get("id")
        if not test_id:
            continue
        method = definition.find("./{*}TestMethod")
        code_base = method.attrib.get("codeBase", "") if method is not None else ""
        if not code_base:
            raise ValueError(f"TRX test definition {test_id} names no test assembly (TestMethod codeBase)")
        definitions[test_id] = code_base.replace("\\", "/").rsplit("/", 1)[-1]
    results = results_element.findall("./{*}UnitTestResult")
    if len(results) != total:
        raise ValueError(
            f"counter total {total} does not match {len(results)} result records"
        )
    report = Report(total, executed, passed)
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
        elif outcome not in {"NotExecuted", "Skipped"}:
            report.not_passed.append(result.attrib.get("testName") or test_id)
        report.test_ids.append(test_id)
        report.assemblies.add(definitions[test_id])
    if (actual_executed, actual_passed) != (executed, passed):
        raise ValueError("TRX counters do not match concrete result outcomes")
    return report


def read_counts(trx: Path) -> tuple[int, int, int]:
    report = read_report(trx)
    return report.total, report.executed, report.passed


def expected_assembly(project: str) -> str:
    """The test assembly a project builds: its <AssemblyName>, else the project file's name."""
    path = Path(project)
    if path.is_file():
        try:
            name = ET.parse(path).getroot().find(".//AssemblyName")
        except ET.ParseError:
            name = None
        if name is not None and (name.text or "").strip():
            return name.text.strip() + ".dll"
    return path.stem + ".dll"


def is_tracked(path: Path) -> bool:
    """True when git tracks the file: a committed TRX is not a report this run's test host wrote."""
    resolved = path.resolve()
    probe = subprocess.run(["git", "ls-files", "--error-unmatch", "--", resolved.name],
                           cwd=resolved.parent, capture_output=True, check=False)
    return probe.returncode == 0


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
    # #1666: each part is checked on its own, from what its test host wrote, not only in sum.
    # A part must have run tests, every executed test in it must have passed, and every result
    # must come from this project's test assembly. No test may run in two parts, so the parts are
    # disjoint and none can stand in for another.
    total = executed = passed = 0
    assembly = expected_assembly(project)
    seen: dict[str, Path] = {}
    part_errors = []
    for report in reports:
        if not report.is_file():
            return [f"TRX report does not exist: {report}"]
        if is_tracked(report):
            return [f"TRX report {report} is tracked by git; it must be written by this run's test host"]
        try:
            part = read_report(report)
        except (ET.ParseError, ValueError) as error:
            return [f"invalid TRX report {report}: {error}"]
        if part.executed == 0:
            part_errors.append(f"{project}: {report} executed no tests")
        if part.not_passed:
            part_errors.append(f"{project}: {report}: {len(part.not_passed)} executed test(s) did not pass, "
                               f"first {part.not_passed[0]}")
        if part.assemblies != {assembly}:
            part_errors.append(f"{project}: {report} holds results from {sorted(part.assemblies)}, "
                               f"expected only {assembly}")
        # A theory's rows can share one test id within a host, so ids are compared between parts.
        for test_id in dict.fromkeys(part.test_ids):
            if test_id in seen:
                part_errors.append(f"{project}: test {test_id} ran in {seen[test_id]} and again in {report}")
                break
        for test_id in part.test_ids:
            seen.setdefault(test_id, report)
        total += part.total
        executed += part.executed
        passed += part.passed
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
    errors = list(part_errors)
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
            defs = "".join(f'<UnitTest id="t{i}"><TestMethod codeBase="/w/bin/Corpus.Tests.dll" /></UnitTest>'
                           for i in range(total))
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
        # #1666: per-part checks, from what the test host wrote, not only the summed counts.
        def part(name: str, ids: range, outcome: str = "Passed", dll: str = "Split.Tests.dll") -> Path:
            path = root / name
            defs = "".join(f'<UnitTest id="s{i}"><TestMethod codeBase="/w/bin/{dll}" /></UnitTest>' for i in ids)
            rows = "".join(f'<UnitTestResult testId="s{i}" testName="T{i}" outcome="{outcome}" />' for i in ids)
            ok = len(ids) if outcome == "Passed" else 0
            ran = 0 if outcome == "Skipped" else len(ids)
            path.write_text(
                f'<TestRun xmlns="urn:test"><Results>{rows}</Results><TestDefinitions>{defs}</TestDefinitions>'
                f'<ResultSummary><Counters total="{len(ids)}" executed="{ran}" passed="{ok}" /></ResultSummary></TestRun>',
                encoding="utf-8")
            return path
        split = "tests/Split.Tests/Split.Tests.csproj"
        manifest.write_text(json.dumps({"projects": [{"path": split, "expectedTotal": 4, "expectedSkipped": 0}]}),
                            encoding="utf-8")
        first, second = part("p1.trx", range(0, 2)), part("p2.trx", range(2, 4))
        if validate([first, second], split, manifest):
            raise AssertionError("two disjoint passing parts were rejected")
        # The same part twice sums to the expected total but runs half the suite.
        if not any("ran in" in e for e in validate([first, first], split, manifest)):
            raise AssertionError("a part counted twice was accepted")
        # A part that ran nothing (its body skipped) fails on its own.
        empty = part("p0.trx", range(4, 4))
        whole = part("pall.trx", range(0, 4))
        if not any("executed no tests" in e for e in validate([whole, empty], split, manifest)):
            raise AssertionError("a part that executed no tests was accepted")
        # A part whose tests failed fails even when the step that ran it reported success.
        failed = part("pf.trx", range(2, 4), outcome="Failed")
        if not any("did not pass" in e for e in validate([first, failed], split, manifest)):
            raise AssertionError("a part with failing tests was accepted")
        # Results from another project's assembly cannot stand in for this one's.
        foreign = part("px.trx", range(2, 4), dll="Other.Tests.dll")
        if not any("expected only Split.Tests.dll" in e for e in validate([first, foreign], split, manifest)):
            raise AssertionError("a part from another test assembly was accepted")
        # A definition that names no assembly is not a test host's report.
        bare = root / "pb.trx"
        bare.write_text(first.read_text(encoding="utf-8").replace(' codeBase="/w/bin/Split.Tests.dll"', ""), encoding="utf-8")
        if not any("names no test assembly" in e for e in validate([bare, second], split, manifest)):
            raise AssertionError("a TRX without test assemblies was accepted")
        # A committed TRX is not a report this run wrote.
        if subprocess.run(["git", "init", "-q", str(root)], capture_output=True, check=False).returncode == 0:
            subprocess.run(["git", "-C", str(root), "add", "p1.trx"], capture_output=True, check=True)
            if not any("tracked by git" in e for e in validate([first, second], split, manifest)):
                raise AssertionError("a git-tracked TRX was accepted")
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
