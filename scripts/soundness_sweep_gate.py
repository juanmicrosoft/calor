#!/usr/bin/env python3
"""Pass/fail gate for the scheduled soundness sweep (#1589).

`bench/Calor.Soundness.Sweep` exits 0 from `run` even when it records findings, so the job
decides here, from the files `run` and `report` write. With every baseline slot (B1, N1,
P845) pointed at one build of main, the gate fails on:

  * an invalid run: a nonzero `run`/`report` exit, `invalid-run.jsonl`, or a run that is not
    "pinned" in summary.json;
  * a failed control: the 13 oracle controls, solver availability on any slot, a mutation
    pair whose oracle verdicts differ, or a CTRL-RETRO-845 / CTRL-RETRO-FIXED case whose token
    differs from its registered expected outcome;
  * any finding: a FALSE-PROOF or FINDING row, a finding record, or a finding class on a case's
    final result OR on any of its retained attempts (including an over-time attempt's late
    observation), so a retry cannot fold a finding into `flaky`;
  * a missing case: a fixture case that B1 or N1 never executed;
  * a changed verdict: a case whose final (class, token) differs from
    tests/TestData/SoundnessSweep/expected-verdicts.json. Every difference fails, timeouts
    included; the summary marks differences that involve a timeout so a slow runner is easy
    to tell from a compiler change.

Subcommands:
  gate          judge a finished run (writes a Markdown job summary; exit 1 on failure)
  check-fixture validate the fixture against the case index (PR-time check; no run needed)
  freeze        rewrite the fixture from a run that passes every other check
"""

from __future__ import annotations

import argparse
import collections
import json
import sys
from pathlib import Path

PACKET = Path("tests/TestData/SoundnessSweep")
FIXTURE = PACKET / "expected-verdicts.json"
SLOTS = ("B1", "N1", "P845")
TIMEOUT_TOKEN = "TimeoutOrUnavailable"

# Report.cs FindingClasses: any of these on any case fails the job, whatever the fixture says.
FINDING_CLASSES = {
    "false-unconditional-proof", "false-proof-unconfirmed-vacuity", "required-demotion-absent",
    "guard-elided-without-proof", "vacuity-mislabel", "spurious-refutation", "spurious-model",
    "stale-cache-proof", "semantic-divergence", "control-mismatch", "candidate-unconfirmed",
}
FAILING_ROW_STATUSES = {"FALSE-PROOF", "FINDING"}
VERDICT_PREFIX = "verdict changed: "


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def read_lines(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def case_index_ids(repo: Path) -> list[str]:
    return [case["id"] for case in load_json(repo / PACKET / "cases-index.json")["cases"]]


def fixture_errors(repo: Path, fixture: dict) -> list[str]:
    """Structural problems in the fixture; empty when it is usable."""
    errors: list[str] = []
    if fixture.get("schemaVersion") != 1:
        errors.append("schemaVersion must be 1")
    cases = fixture.get("cases")
    if not isinstance(cases, dict):
        return errors + ["cases must be an object keyed by case id"]
    index = case_index_ids(repo)
    missing = [cid for cid in index if cid not in cases]
    extra = [cid for cid in cases if cid not in set(index)]
    if missing:
        errors.append(f"{len(missing)} case(s) in cases-index.json have no expected verdict, e.g. {missing[:3]}")
    if extra:
        errors.append(f"{len(extra)} expected verdict(s) name no case in cases-index.json, e.g. {extra[:3]}")
    if list(cases) != [cid for cid in index if cid in cases]:
        errors.append("cases must follow cases-index.json order")
    for cid, verdict in cases.items():
        if not isinstance(verdict, dict) or set(verdict) != {"class", "token"}:
            errors.append(f"{cid}: a verdict is exactly {{class, token}}")
            continue
        if not isinstance(verdict["class"], str) or not verdict["class"]:
            errors.append(f"{cid}: class must be a non-empty string")
        elif verdict["class"] in FINDING_CLASSES:
            errors.append(f"{cid}: '{verdict['class']}' is a finding class; a finding can never be an expected verdict")
        if verdict["token"] is not None and not isinstance(verdict["token"], str):
            errors.append(f"{cid}: token must be a string or null")
    return errors


def final_results(out: Path, slot: str) -> dict[str, dict]:
    finals: dict[str, dict] = {}
    for record in read_lines(out / slot / "case-results.jsonl"):
        finals[record["caseId"]] = record  # the last line for a case is its final result (Report.cs Final)
    return finals


def finding_classes(record: dict) -> set[str]:
    return {record.get("class"), *(record.get("addedFindings") or [])} & FINDING_CLASSES


def attempt_findings(out: Path, slot: str, record: dict) -> tuple[set[str], list[str]]:
    """Finding classes on any retained attempt of a case, and attempt files that are missing."""
    found: set[str] = set()
    missing: list[str] = []
    for attempt in record.get("attempts") or []:
        found |= finding_classes(attempt)
        path = out / slot / attempt.get("path", "")
        if not attempt.get("path") or not path.is_file():
            missing.append(attempt.get("path") or "?")
            continue
        raw = load_json(path)
        found |= finding_classes(raw)
        if isinstance(raw.get("lateObservation"), dict):
            found |= finding_classes(raw["lateObservation"])
    return found, missing


def run_failures(repo: Path, out: Path, run_exit: int, report_exit: int) -> tuple[list[str], dict]:
    """Every check except the comparison with the fixture."""
    failures: list[str] = []
    if run_exit != 0:
        failures.append(f"sweep `run` exited {run_exit} (3 = preflight, 4 = pin/native image, 5 = solver availability, 6 = CLI contradiction)")
    if report_exit != 0:
        failures.append(f"sweep `report` exited {report_exit}")
    for line in read_lines(out / "invalid-run.jsonl"):
        failures.append(f"invalid run: {line.get('reason')}")
    summary_path = out / "summary.json"
    summary = load_json(summary_path) if summary_path.is_file() else {}
    if not summary:
        failures.append("summary.json is missing: `report` did not finish")
    for slot in ("B1", "N1"):
        validity = summary.get(slot, {}).get("runValidity")
        if summary and validity != "pinned":
            failures.append(f"{slot}: run validity is {validity!r}, not 'pinned'")

    oracle_path = out / "oracle-controls.json"
    if not oracle_path.is_file():
        failures.append("oracle-controls.json is missing: the preflight did not run")
    elif not load_json(oracle_path).get("pass"):
        bad = [c["id"] for c in load_json(oracle_path)["controls"] if not c.get("ok")]
        failures.append(f"oracle control mismatch: {bad}")
    controls_path = out / "controls.json"
    controls = load_json(controls_path) if controls_path.is_file() else {}
    if not controls:
        failures.append("controls.json is missing: `report` did not finish")
    else:
        for slot in SLOTS:
            availability = controls["solverAvailability"].get(slot)
            if not availability or not availability["pass"]:
                bad = [c["caseId"] for c in (availability or {}).get("cases", []) if not c["ok"]]
                failures.append(f"{slot}: solver-availability control mismatch {bad}")
        for slot in ("B1", "N1"):
            for pair in controls["mutationPairs"].get(slot, []):
                if not pair["oracleEqual"]:
                    failures.append(f"{slot}: mutation pair {pair['pair']} oracle verdicts differ")
            for m in controls["controlMismatches"].get(slot, []):
                failures.append(f"{slot}: control {m['caseId']} expected {m['expected']}, got {m['token']} (class {m['class']})")

    row_counts: dict[str, dict] = {}
    for slot in ("B1", "N1"):
        status_path = out / slot / "row-status.json"
        if not status_path.is_file():
            failures.append(f"{slot}/row-status.json is missing: `report` did not finish")
            continue
        status = load_json(status_path)
        row_counts[slot] = status["counts"]
        for row in status["rows"]:
            if row["status"] in FAILING_ROW_STATUSES:
                failures.append(f"{slot}: row {row['row']} is {row['status']} ({', '.join(row['findingClasses'])})")
        findings = summary.get(slot, {}).get("findings", 0)
        if findings:
            failures.append(f"{slot}: {findings} finding record(s) in {slot}/findings/")

    finals = {slot: final_results(out, slot) for slot in SLOTS}
    index = case_index_ids(repo)
    for slot in SLOTS:
        for cid, record in finals[slot].items():
            found = finding_classes(record)
            in_attempts, missing = attempt_findings(out, slot, record)
            if found | in_attempts:
                failures.append(f"{slot}: {cid} {', '.join(sorted(found | in_attempts))} (final {record['class']}/{record.get('token')})")
            if missing:
                failures.append(f"{slot}: {cid} attempt file(s) missing: {missing}")
        # B1 and N1 execute every case; P845 executes only its availability and #845 controls.
        if slot != "P845":
            absent = [cid for cid in index if cid not in finals[slot]]
            if absent:
                failures.append(f"{slot}: {len(absent)} case(s) not executed, e.g. {absent[:3]}")
    context = {
        "finals": finals, "rowCounts": row_counts,
        "retro845": controls.get("retro845", {}).get("B1", {}).get("cases", []),
        "executions": len(read_lines(out / "ledger.jsonl")),
    }
    return failures, context


def judge(repo: Path, out: Path, run_exit: int, report_exit: int) -> dict:
    """Everything the summary shows; `failures` decides the job."""
    fixture = load_json(repo / FIXTURE)
    failures = [f"fixture: {p}" for p in fixture_errors(repo, fixture)]
    expected: dict[str, dict] = fixture["cases"] if isinstance(fixture.get("cases"), dict) else {}
    checks, context = run_failures(repo, out, run_exit, report_exit)
    failures += checks
    timeout_related: list[str] = []
    for slot in SLOTS:
        for cid, record in context["finals"][slot].items():
            if finding_classes(record):
                continue  # already a failure above
            actual = {"class": record["class"], "token": record.get("token")}
            want = expected.get(cid)
            if want is None:
                failures.append(f"{slot}: {cid} has no expected verdict in the fixture")
            elif actual != want:
                change = f"{slot}: {cid} expected {want['class']}/{want['token']}, got {actual['class']}/{actual['token']}"
                failures.append(VERDICT_PREFIX + change)
                if TIMEOUT_TOKEN in (want["token"], actual["token"], *(record.get("tokens") or [])):
                    timeout_related.append(change)
    return {
        "failures": failures, "timeoutRelated": timeout_related,
        "byClass": {s: collections.Counter(r["class"] for r in context["finals"][s].values()) for s in SLOTS},
        "executed": {s: len(context["finals"][s]) for s in SLOTS},
        "executions": context["executions"], "rowCounts": context["rowCounts"], "retro845": context["retro845"],
    }


def markdown(result: dict, run_url: str | None, commit: str | None = None) -> str:
    ok = not result["failures"]
    lines = [f"## Soundness sweep (#1589): {'PASS' if ok else 'FAIL'}", ""]
    if commit:
        lines += [f"Commit under test: `{commit}`", ""]
    if run_url:
        lines += [f"Run: {run_url}", ""]
    lines += [
        f"{result['executions']} case executions. Final results per slot: "
        + ", ".join(f"{slot} {n}" for slot, n in result["executed"].items()) + ".",
        "All three slots run the same build (#1589 single baseline).",
        "",
        "### Verdicts by class",
        "",
        "| Class | " + " | ".join(SLOTS) + " |",
        "|---|" + "---|" * len(SLOTS),
    ]
    classes = sorted({c for counter in result["byClass"].values() for c in counter})
    for cls in classes:
        lines.append(f"| {cls} | " + " | ".join(str(result["byClass"][s].get(cls, 0)) for s in SLOTS) + " |")
    if result["rowCounts"]:
        lines += ["", "### Row status", ""]
        for slot, counts in result["rowCounts"].items():
            lines.append(f"- {slot}: " + ", ".join(f"{k} {v}" for k, v in counts.items()))
    if result["retro845"]:
        lines += ["", "### CTRL-RETRO-845 (B1)", "", "| Case | Expected | Token |", "|---|---|---|"]
        for case in result["retro845"]:
            lines.append(f"| {case['caseId']} | {case['expected']} | {case['token']} |")
    lines += ["", "### Failures and regressions against expected-verdicts.json", ""]
    lines += [f"- {f}" for f in result["failures"]] or ["None."]
    if result["timeoutRelated"]:
        lines += ["", "### Of those, verdict changes that involve a timeout (check runner speed first)", ""]
        lines += [f"- {d}" for d in result["timeoutRelated"]]
    return "\n".join(lines) + "\n"


def headline(result: dict) -> str:
    if not result["failures"]:
        return "pass"
    return f"{len(result['failures'])} failure(s); first: {result['failures'][0]}"


def cmd_gate(args) -> int:
    repo, out = Path(args.repo).resolve(), Path(args.out).resolve()
    result = judge(repo, out, args.run_exit, args.report_exit)
    text = markdown(result, args.run_url, args.commit)
    print(text)
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as handle:
            handle.write(text)
    if args.headline:
        Path(args.headline).write_text(headline(result) + "\n", encoding="utf-8")
    return 1 if result["failures"] else 0


def cmd_check_fixture(args) -> int:
    repo = Path(args.repo).resolve()
    errors = fixture_errors(repo, load_json(repo / FIXTURE))
    for error in errors:
        print(f"::error file={FIXTURE}::{error}")
    if not errors:
        cases = load_json(repo / FIXTURE)["cases"]
        print(f"{FIXTURE}: {len(cases)} expected verdicts, matching cases-index.json")
    return 1 if errors else 0


def cmd_freeze(args) -> int:
    repo, out = Path(args.repo).resolve(), Path(args.out).resolve()
    failures, context = run_failures(repo, out, 0, 0)
    finals = context["finals"]
    # The three slots ran one build, so they must agree before one verdict per case is recorded.
    for slot in ("N1", "P845"):
        for cid, record in finals[slot].items():
            b1 = finals["B1"].get(cid)
            if b1 is not None and (record["class"], record.get("token")) != (b1["class"], b1.get("token")):
                failures.append(f"{slot}: {cid} {record['class']}/{record.get('token')} differs from B1 {b1['class']}/{b1.get('token')}")
    if failures:
        print("refusing to freeze; the run fails these checks:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1
    old = load_json(repo / FIXTURE) if (repo / FIXTURE).is_file() else {}
    fixture = {
        "schemaVersion": 1,
        "issue": 1589,
        "note": old.get("note", "Expected (class, token) per sweep case at main, checked by scripts/soundness_sweep_gate.py. Update only in a reviewed PR, with the reason in the PR body."),
        "recordedFrom": {"commit": args.commit, "runner": args.runner},
        "cases": {cid: {"class": finals["B1"][cid]["class"], "token": finals["B1"][cid].get("token")} for cid in case_index_ids(repo)},
    }
    (repo / FIXTURE).write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {FIXTURE}: {len(fixture['cases'])} cases")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    gate = sub.add_parser("gate")
    gate.add_argument("--repo", default=".")
    gate.add_argument("--out", required=True)
    gate.add_argument("--run-exit", type=int, required=True)
    gate.add_argument("--report-exit", type=int, required=True)
    gate.add_argument("--summary")
    gate.add_argument("--headline")
    gate.add_argument("--run-url")
    gate.add_argument("--commit")
    gate.set_defaults(func=cmd_gate)
    check = sub.add_parser("check-fixture")
    check.add_argument("--repo", default=".")
    check.set_defaults(func=cmd_check_fixture)
    freeze = sub.add_parser("freeze")
    freeze.add_argument("--repo", default=".")
    freeze.add_argument("--out", required=True)
    freeze.add_argument("--commit", required=True)
    freeze.add_argument("--runner", required=True)
    freeze.set_defaults(func=cmd_freeze)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
