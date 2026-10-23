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
  * any finding: a FALSE-PROOF or FINDING row, a finding record, or a case whose class is a
    finding class (false proof, stale-cache proof, spurious refutation, ...);
  * a missing case: a fixture case that a slot never executed;
  * a changed verdict: a case whose (class, token) differs from
    tests/TestData/SoundnessSweep/expected-verdicts.json.

Timeouts are the one exception to the last rule. A timed-out case carries no proof claim (the
harness classifies any attempt that proves something unsound as a false proof, even a late
one), so a timeout cannot hide a false proof; it only loses coverage. A verdict change that
only moves a case into or out of a timeout is reported, and the job fails only when more
cases newly time out on one slot than the fixture's `timeoutTolerance`.

Subcommands:
  gate          judge a finished run (writes a Markdown job summary; exit 1 on failure)
  check-fixture validate the fixture against the case index (PR-time check; no run needed)
  freeze        rewrite the fixture from a clean run (for a reviewed fixture update)
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
TIMEOUT_CLASSES = {"timed-out"}

# Report.cs FindingClasses: any of these on any case fails the job, whatever the fixture says.
FINDING_CLASSES = {
    "false-unconditional-proof", "false-proof-unconfirmed-vacuity", "required-demotion-absent",
    "guard-elided-without-proof", "vacuity-mislabel", "spurious-refutation", "spurious-model",
    "stale-cache-proof", "semantic-divergence", "control-mismatch", "candidate-unconfirmed",
}
FAILING_ROW_STATUSES = {"FALSE-PROOF", "FINDING"}


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
    tolerance = fixture.get("timeoutTolerance")
    if not isinstance(tolerance, int) or isinstance(tolerance, bool) or tolerance < 0:
        errors.append("timeoutTolerance must be a non-negative integer")
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


def is_timeout(verdict: dict | None) -> bool:
    return verdict is not None and (verdict.get("class") in TIMEOUT_CLASSES or verdict.get("token") == TIMEOUT_TOKEN)


def final_results(out: Path, slot: str) -> dict[str, dict]:
    finals: dict[str, dict] = {}
    for record in read_lines(out / slot / "case-results.jsonl"):
        finals[record["caseId"]] = record  # the last line for a case is its final result (Report.cs Final)
    return finals


def judge(repo: Path, out: Path, run_exit: int, report_exit: int) -> dict:
    """Everything the summary shows; `failures` decides the job."""
    failures: list[str] = []
    drift: list[str] = []
    fixture = load_json(repo / FIXTURE)
    problems = fixture_errors(repo, fixture)
    failures += [f"fixture: {p}" for p in problems]
    expected: dict[str, dict] = fixture.get("cases", {}) if isinstance(fixture.get("cases"), dict) else {}

    # 1. Run validity.
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

    # 2. Controls.
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
        for slot, availability in controls["solverAvailability"].items():
            if not availability["pass"]:
                bad = [c["caseId"] for c in availability["cases"] if not c["ok"]]
                failures.append(f"{slot}: solver-availability control mismatch {bad}")
        for slot, pairs in controls["mutationPairs"].items():
            for pair in pairs:
                if not pair["oracleEqual"]:
                    failures.append(f"{slot}: mutation pair {pair['pair']} oracle verdicts differ")
        for slot, mismatches in controls["controlMismatches"].items():
            for m in mismatches:
                failures.append(f"{slot}: control {m['caseId']} expected {m['expected']}, got {m['token']} (class {m['class']})")

    # 3. Findings and row status.
    row_counts: dict[str, dict] = {}
    for slot in ("B1", "N1"):
        status_path = out / slot / "row-status.json"
        if not status_path.is_file():
            continue
        status = load_json(status_path)
        row_counts[slot] = status["counts"]
        for row in status["rows"]:
            if row["status"] in FAILING_ROW_STATUSES:
                failures.append(f"{slot}: row {row['row']} is {row['status']} ({', '.join(row['findingClasses'])})")
        findings = summary.get(slot, {}).get("findings", 0)
        if findings:
            failures.append(f"{slot}: {findings} finding record(s) in {slot}/findings/")

    # 4. Verdicts, slot by slot, against the fixture.
    by_class: dict[str, collections.Counter] = {}
    executed: dict[str, int] = {}
    new_timeouts: dict[str, list[str]] = {}
    for slot in SLOTS:
        finals = final_results(out, slot)
        executed[slot] = len(finals)
        by_class[slot] = collections.Counter(r["class"] for r in finals.values())
        new_timeouts[slot] = []
        for cid, record in finals.items():
            actual = {"class": record["class"], "token": record.get("token")}
            findings = sorted({record["class"], *record.get("addedFindings", [])} & FINDING_CLASSES)
            if findings:
                failures.append(f"{slot}: {cid} {', '.join(findings)} (token {actual['token']})")
                continue
            want = expected.get(cid)
            if want is None:
                failures.append(f"{slot}: {cid} has no expected verdict in the fixture")
            elif actual != want:
                change = f"{slot}: {cid} expected {want['class']}/{want['token']}, got {actual['class']}/{actual['token']}"
                if is_timeout(actual) or is_timeout(want) or (record["class"] == "flaky" and TIMEOUT_TOKEN in record.get("tokens", [])):
                    drift.append(change)
                    if (is_timeout(actual) or record["class"] == "flaky") and not is_timeout(want):
                        new_timeouts[slot].append(cid)
                else:
                    failures.append(f"verdict changed: {change}")
        # B1 and N1 execute every case; P845 executes only its availability and #845 controls.
        if slot != "P845":
            missing = [cid for cid in expected if cid not in finals]
            if missing:
                failures.append(f"{slot}: {len(missing)} case(s) not executed, e.g. {missing[:3]}")
    tolerance = fixture.get("timeoutTolerance", 0) if isinstance(fixture.get("timeoutTolerance"), int) else 0
    for slot, cids in new_timeouts.items():
        if len(cids) > tolerance:
            failures.append(f"{slot}: {len(cids)} case(s) newly timed out, more than timeoutTolerance {tolerance}: {cids[:10]}")

    retro = []
    for case in controls.get("retro845", {}).get("B1", {}).get("cases", []):
        retro.append(case)
    ledger = read_lines(out / "ledger.jsonl")
    return {
        "failures": failures, "drift": drift, "byClass": by_class, "executed": executed,
        "executions": len(ledger), "rowCounts": row_counts, "retro845": retro,
        "newTimeouts": new_timeouts, "tolerance": tolerance,
    }


def markdown(result: dict, run_url: str | None) -> str:
    ok = not result["failures"]
    lines = [f"## Soundness sweep (#1589): {'PASS' if ok else 'FAIL'}", ""]
    if run_url:
        lines += [f"Run: {run_url}", ""]
    lines += [
        f"{result['executions']} case executions. Final results per slot: "
        + ", ".join(f"{slot} {n}" for slot, n in result["executed"].items()) + ".",
        "All three slots run the same build of main (#1589 single baseline).",
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
    lines += ["", "### Regressions against expected-verdicts.json", ""]
    lines += [f"- {f}" for f in result["failures"]] or ["None."]
    lines += ["", f"### Timeout drift (reported only; tolerance {result['tolerance']} new timeouts per slot)", ""]
    lines += [f"- {d}" for d in result["drift"]] or ["None."]
    return "\n".join(lines) + "\n"


def headline(result: dict) -> str:
    if not result["failures"]:
        return "pass"
    return f"{len(result['failures'])} failure(s); first: {result['failures'][0]}"


def cmd_gate(args) -> int:
    repo, out = Path(args.repo).resolve(), Path(args.out).resolve()
    result = judge(repo, out, args.run_exit, args.report_exit)
    text = markdown(result, args.run_url)
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
    old = load_json(repo / FIXTURE) if (repo / FIXTURE).is_file() else {}
    b1 = final_results(out, "B1")
    if (out / "invalid-run.jsonl").is_file():
        print("refusing to freeze an invalid run", file=sys.stderr)
        return 1
    bad = [cid for cid, r in b1.items() if ({r["class"], *r.get("addedFindings", [])} & FINDING_CLASSES)]
    if bad:
        print(f"refusing to freeze a run with findings: {bad[:10]}", file=sys.stderr)
        return 1
    index = case_index_ids(repo)
    absent = [cid for cid in index if cid not in b1]
    if absent:
        print(f"refusing to freeze: {len(absent)} case(s) not executed on B1", file=sys.stderr)
        return 1
    fixture = {
        "schemaVersion": 1,
        "issue": 1589,
        "note": old.get("note", "Expected (class, token) per sweep case at main, checked by scripts/soundness_sweep_gate.py. Update only in a reviewed PR, with the reason in the PR body."),
        "recordedFrom": {"commit": args.commit, "runner": args.runner},
        "timeoutTolerance": old.get("timeoutTolerance", args.timeout_tolerance),
        "cases": {cid: {"class": b1[cid]["class"], "token": b1[cid].get("token")} for cid in index},
    }
    (repo / FIXTURE).write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {FIXTURE}: {len(index)} cases")
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
    gate.set_defaults(func=cmd_gate)
    check = sub.add_parser("check-fixture")
    check.add_argument("--repo", default=".")
    check.set_defaults(func=cmd_check_fixture)
    freeze = sub.add_parser("freeze")
    freeze.add_argument("--repo", default=".")
    freeze.add_argument("--out", required=True)
    freeze.add_argument("--commit", required=True)
    freeze.add_argument("--runner", required=True)
    freeze.add_argument("--timeout-tolerance", type=int, default=8)
    freeze.set_defaults(func=cmd_freeze)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
