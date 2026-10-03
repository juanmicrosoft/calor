#!/usr/bin/env python3
"""
fixture_compile_check.py — compile every tracked `.calr` fixture once and
compare the outcome with its registered expectation (#1241).

It compiles each fixture (or registered multi-file group) once with the
compiler built from this checkout (scripts/checkout_compiler.py). It does
not perform an AST round trip: it never re-parses emitted output and
compares no ASTs. Its predecessor, `ast_roundtrip_check.py`, claimed one
without doing that work; the claim is removed.

Expectations live in eng/tier2-fixture-expectations.json. Every tracked
fixture under the selected roots must appear there exactly once, with:

  expect = compile        must compile; the outcome is `passed`.
  expect = reject         a negative: must fail with exactly the registered
                          error signature (`expected-negative`). negativeKind
                          says whether the fixture is designed to fail or is
                          incidentally invalid Calor that nothing compiles.
  expect = known-failure  a real compiler failure, retained as a failure:
                          must fail with exactly the registered signature.

Row statuses. Only `passed` and `expected-negative` are successes:

  passed, expected-negative           success
  known-failure                       a registered real failure; not a pass
  failed                              wrong outcome or wrong error signature
  unexpected-pass                     a reject/known-failure row compiled
  unclassified                        tracked fixture with no expectation
  invalid                             malformed or stale expectation entry
  crashed                             no parseable compiler result
  TimeoutOrUnavailable                the compile hit the time limit

There is no skip path. The verdict is PASS only when there is at least one
row and every row is a success.

Exit codes:
    0  PASS
    1  FAIL (see the report)
    2  bad arguments, or the checkout-built compiler cannot be used

Usage:
    python3 scripts/fixture_compile_check.py --root samples --root tests \\
        [--report report.json] [--jobs N]
    python3 scripts/fixture_compile_check.py --self-test
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import checkout_compiler  # noqa: E402

REPO_ROOT = checkout_compiler.REPO_ROOT
DEFAULT_MANIFEST = REPO_ROOT / "eng" / "tier2-fixture-expectations.json"
SCHEMA_VERSION = 1

EXPECTATIONS = ("compile", "reject", "known-failure")
MODES = ("each", "together")
# designed: the fixture exists to be rejected (its consumer says so).
# incidental: nothing compiles the fixture; it is invalid Calor and the
# compiler rejects it correctly. Recorded so the two are never conflated.
NEGATIVE_KINDS = ("designed", "incidental")
# Only options a fixture's own consumer uses. Anything else (for example
# --help, which exits 0 without compiling) makes the entry invalid.
ALLOWED_OPTIONS = frozenset({
    "--no-enforce-effects",
    "--permissive-effects",
    "--transpile-only",
    "--require-docs",
})
SUCCESS = ("passed", "expected-negative")
STATUSES = SUCCESS + ("known-failure", "failed", "unexpected-pass",
                      "unclassified", "invalid", "crashed",
                      "TimeoutOrUnavailable")
CS_CODE = re.compile(r"\((CS\d{4})\)")


def signature(diagnostics: list[dict]) -> list[str]:
    """Sorted unique error codes; Calor1002 carries its Roslyn code."""
    sig = set()
    for d in diagnostics:
        if str(d.get("severity", "")).lower() != "error":
            continue
        code = str(d.get("code", ""))
        if code == "Calor1002":
            m = CS_CODE.search(str(d.get("message", "")))
            code = f"Calor1002:{m.group(1)}" if m else "Calor1002"
        sig.add(code)
    return sorted(sig)


# ---------------------------------------------------------------- manifest

def validate_manifest(manifest: dict) -> list[str]:
    """Structural problems in the expectations file (empty when valid)."""
    errors: list[str] = []
    if manifest.get("schemaVersion") != SCHEMA_VERSION:
        errors.append(f"schemaVersion must be {SCHEMA_VERSION}")
    groups = manifest.get("groups")
    if not isinstance(groups, list) or not groups:
        return errors + ["groups must be a non-empty list"]
    seen_ids: set[str] = set()
    for i, g in enumerate(groups):
        gid = g.get("id") if isinstance(g, dict) else None
        where = f"group {gid or i}"
        if not isinstance(g, dict) or not isinstance(gid, str) or not gid:
            errors.append(f"{where}: missing id")
            continue
        if gid in seen_ids:
            errors.append(f"{where}: duplicate id")
        seen_ids.add(gid)
        files = g.get("files")
        if (not isinstance(files, list) or not files
                or not all(isinstance(f, str) and f for f in files)):
            errors.append(f"{where}: files must be a non-empty list of paths")
        elif len(set(files)) != len(files):
            errors.append(f"{where}: a file is listed twice")
        if g.get("mode") not in MODES:
            errors.append(f"{where}: mode must be one of {MODES}")
        elif g["mode"] == "together" and isinstance(files, list) \
                and len(files) < 2:
            errors.append(f"{where}: a together group needs 2+ files")
        expect = g.get("expect")
        if expect not in EXPECTATIONS:
            errors.append(f"{where}: expect must be one of {EXPECTATIONS}")
        opts = g.get("options", [])
        if not isinstance(opts, list) or any(o not in ALLOWED_OPTIONS
                                             for o in opts):
            errors.append(f"{where}: options must be a subset of "
                          f"{sorted(ALLOWED_OPTIONS)}")
        errs = g.get("errors")
        if expect == "compile":
            if errs not in (None, []):
                errors.append(f"{where}: a compile group lists no errors")
        elif (not isinstance(errs, list) or not errs
              or not all(isinstance(e, str) and e for e in errs)
              or errs != sorted(set(errs))):
            errors.append(f"{where}: {expect} needs a sorted, unique, "
                          "non-empty error signature")
        kind = g.get("negativeKind")
        if expect == "reject" and kind not in NEGATIVE_KINDS:
            errors.append(f"{where}: reject needs negativeKind in "
                          f"{NEGATIVE_KINDS}")
        elif expect != "reject" and kind is not None:
            errors.append(f"{where}: only a reject group has a negativeKind")
        if not isinstance(g.get("evidence"), str) or not g["evidence"].strip():
            errors.append(f"{where}: evidence is required")
        if expect == "known-failure" and (
                not isinstance(g.get("defect"), str) or not g["defect"].strip()):
            errors.append(f"{where}: known-failure needs a defect description")
    return errors


# ------------------------------------------------------------- execution

def _parse_result(rc: int, stdout: str) -> tuple[str | None, list[str], str]:
    """Return (outcome, signature, detail); outcome None means crashed."""
    start = stdout.find("{")
    try:
        doc = json.loads(stdout[start:]) if start >= 0 else None
    except json.JSONDecodeError:
        doc = None
    if not isinstance(doc, dict) or not isinstance(doc.get("diagnostics"), list):
        return None, [], f"exit {rc}; no JSON diagnostics document"
    sig = signature(doc["diagnostics"])
    if rc == 0 and not sig:
        return "ok", [], ""
    if rc == 1 and sig:
        return "errors", sig, ""
    return None, sig, f"exit {rc} with error signature {sig}"


def run_unit(compiler_cmd: list[str], repo_root: Path, files: list[str],
             options: list[str], timeout: float) -> tuple[str | None, list[str], str]:
    """Compile one unit: one file, or one multi-file group, in a scratch dir.

    Inputs are copied so that nothing is written into the checkout (the CLI
    writes multi-file outputs next to each input).
    """
    with tempfile.TemporaryDirectory(prefix="fixture-check-") as td:
        work = Path(td)
        common = Path(os.path.commonpath([str(Path(f).parent) for f in files]))
        inputs = []
        for f in files:
            dst = work / "in" / Path(f).relative_to(common)
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(repo_root / f, dst)
            inputs.append(dst)
        cmd = list(compiler_cmd)
        for p in inputs:
            cmd += ["--input", str(p)]
        if len(inputs) == 1:
            cmd += ["--output", str(work / "out.g.cs")]
        cmd += ["--format", "json", "--no-cache", *options]
        try:
            cp = subprocess.run(cmd, cwd=work, capture_output=True, text=True,
                                timeout=timeout)
        except subprocess.TimeoutExpired:
            return "timeout", [], f"exceeded {timeout:.0f}s"
        outcome, sig, detail = _parse_result(cp.returncode, cp.stdout)
        if outcome == "ok":
            outputs = ([work / "out.g.cs"] if len(inputs) == 1
                       else [p.with_suffix(".g.cs") for p in inputs])
            missing = [o.name for o in outputs
                       if not o.is_file() or o.stat().st_size == 0]
            if missing:
                return None, [], f"exit 0 but no generated C#: {missing}"
        if outcome is None and not detail:
            detail = (cp.stderr or cp.stdout)[-400:]
        return outcome, sig, detail


def classify(expect: str, expected_sig: list[str], outcome: str | None,
             sig: list[str]) -> str:
    if outcome == "timeout":
        return "TimeoutOrUnavailable"
    if outcome is None:
        return "crashed"
    if expect == "compile":
        return "passed" if outcome == "ok" else "failed"
    if outcome == "ok":
        return "unexpected-pass"
    if sig != expected_sig:
        return "failed"
    return "expected-negative" if expect == "reject" else "known-failure"


def check(repo_root: Path, manifest: dict, roots: list[str], discovered: list[str],
          compiler_cmd: list[str], jobs: int, timeout: float) -> dict:
    """Run the registered expectations for `discovered` and build a report."""
    rows: list[dict] = []
    problems = validate_manifest(manifest)
    if problems:
        rows += [{"group": None, "files": [], "status": "invalid", "detail": p}
                 for p in problems]
        return _report(manifest, roots, discovered, rows)

    in_roots = set(discovered)
    owner: dict[str, str] = {}
    units = []
    for g in manifest["groups"]:
        files = g["files"]
        selected = [f for f in files if _under(f, roots)]
        if not selected:
            continue
        if len(selected) != len(files):
            rows.append({"group": g["id"], "files": files, "status": "invalid",
                         "detail": "group spans selected and unselected roots"})
            continue
        bad = False
        for f in files:
            if f in owner:
                rows.append({"group": g["id"], "files": [f],
                             "status": "invalid",
                             "detail": f"also registered in {owner[f]}"})
                bad = True
            elif f not in in_roots:
                rows.append({"group": g["id"], "files": [f],
                             "status": "invalid",
                             "detail": "registered but not a tracked fixture "
                                       "(stale or build output)"})
                bad = True
            owner.setdefault(f, g["id"])
        if bad:
            continue
        batches = ([[f] for f in files] if g["mode"] == "each" else [files])
        for batch in batches:
            units.append((g, batch))

    for f in discovered:
        if f not in owner:
            rows.append({"group": None, "files": [f], "status": "unclassified",
                         "detail": "tracked fixture with no registered "
                                   "expectation"})

    def work(unit):
        g, batch = unit
        outcome, sig, detail = run_unit(compiler_cmd, repo_root, batch,
                                        g.get("options", []), timeout)
        expected_sig = g.get("errors") or []
        status = classify(g["expect"], expected_sig, outcome, sig)
        row = {"group": g["id"], "files": batch, "expect": g["expect"],
               "options": g.get("options", []), "status": status,
               "signature": sig}
        if g["expect"] != "compile":
            row["expectedSignature"] = expected_sig
        if g.get("negativeKind"):
            row["negativeKind"] = g["negativeKind"]
        if detail:
            row["detail"] = detail
        return row

    with concurrent.futures.ThreadPoolExecutor(max(1, jobs)) as ex:
        rows += list(ex.map(work, units))
    return _report(manifest, roots, discovered, rows)


def _under(path: str, roots: list[str]) -> bool:
    return any(path == r or path.startswith(r.rstrip("/") + "/") for r in roots)


def _report(manifest: dict, roots: list[str], discovered: list[str],
            rows: list[dict]) -> dict:
    counts = {s: 0 for s in STATUSES}
    for r in rows:
        counts[r["status"]] += len(r["files"]) or 1
    reasons = []
    if not discovered:
        reasons.append("empty selection: no tracked fixtures under "
                       f"{roots}")
    bad = [s for s in STATUSES if s not in SUCCESS and counts[s]]
    if bad:
        reasons.append("non-success rows: " +
                       ", ".join(f"{s}={counts[s]}" for s in bad))
    return {
        "schemaVersion": 1,
        "check": "fixture-compile-check",
        "astRoundTrip": False,
        "roots": roots,
        "manifestSha256": hashlib.sha256(
            json.dumps(manifest, sort_keys=True).encode()).hexdigest(),
        "trackedFixtures": len(discovered),
        "counts": counts,
        "verdict": "FAIL" if reasons else "PASS",
        "failReasons": reasons,
        "rows": sorted(rows, key=lambda r: (r["status"] in SUCCESS,
                                            r["files"][:1], r["group"] or "")),
    }


def print_summary(report: dict, out=sys.stdout) -> None:
    print(f"fixture_compile_check: {report['trackedFixtures']} tracked "
          f"fixture(s) under {report['roots']} (compiled once each; no AST "
          "round trip)", file=out)
    for s, n in report["counts"].items():
        if n:
            print(f"  {s:<22} {n}", file=out)
    for r in report["rows"]:
        if r["status"] in SUCCESS:
            continue
        files = ", ".join(r["files"]) or "-"
        extra = r.get("detail") or ""
        if r.get("signature") or r.get("expectedSignature"):
            extra = (f"got {r.get('signature')} expected "
                     f"{r.get('expectedSignature', '[]')} {extra}").strip()
        print(f"  [{r['status']}] {files} {extra}", file=out)
    print(f"fixture_compile_check: {report['verdict']}"
          + (f" ({'; '.join(report['failReasons'])})"
             if report["failReasons"] else ""), file=out)


# -------------------------------------------------------------- self-test

def self_test() -> int:
    """Real compiler, synthetic fixtures: every expectation fails closed."""
    try:
        cmd = checkout_compiler.resolve().command
    except checkout_compiler.CompilerResolutionError as e:
        print(f"fixture_compile_check self-test: {e}", file=sys.stderr)
        return 2
    good = "§M{m001:Pos}\n  §F{f001:Echo:pub} (i32:x) -> i32\n    §R x\n"
    broken = "§M{m001:Neg}\n  §Q (unterminated\n"
    effect = "§M{m001:Eff}\n  §F{f001:Hi:pub} () -> void\n    §P \"hi\"\n"
    cases = [  # (source, expect, errors, wanted status)
        (good, "compile", [], "passed"),
        (broken, "compile", [], "failed"),
        (good, "reject", ["Calor0410"], "unexpected-pass"),
        (effect, "reject", ["Calor0410"], "expected-negative"),
        (effect, "reject", ["Calor0411"], "failed"),
        (effect, "known-failure", ["Calor0410"], "known-failure"),
    ]
    failures = 0
    with tempfile.TemporaryDirectory() as td:
        root, groups = Path(td), []
        (root / "fx").mkdir()
        for i, (src, expect, errs, _want) in enumerate(cases):
            (root / f"fx/c{i}.calr").write_text(src, encoding="utf-8")
            g = {"id": f"c{i}", "files": [f"fx/c{i}.calr"], "mode": "each",
                 "expect": expect, "evidence": "self-test"}
            g.update({"errors": errs} if errs else {})
            g.update({"defect": "self-test"} if expect == "known-failure"
                     else {"negativeKind": "designed"} if expect == "reject"
                     else {})
            groups.append(g)
        manifest = {"schemaVersion": SCHEMA_VERSION, "groups": groups}
        files = [g["files"][0] for g in groups]
        report = check(root, manifest, ["fx"], files, cmd, 4, 300)
        got = {r["group"]: r["status"] for r in report["rows"]}
        empty = check(root, manifest, ["none"], [], cmd, 1, 60)
        results = [(f"case {i} ({c[1]})", got.get(f"c{i}"), c[3])
                   for i, c in enumerate(cases)]
        results += [("verdict with failing rows", report["verdict"], "FAIL"),
                    ("empty selection", empty["verdict"], "FAIL")]
        for name, actual, want in results:
            failures += actual != want
            print(f"fixture_compile_check self-test: {name}: "
                  f"{'PASS' if actual == want else 'FAIL'} "
                  f"(got {actual}, want {want})")
    return 0 if failures == 0 else 1


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--root", action="append", default=[],
                   help="Repo-relative fixture root (repeatable).")
    p.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    p.add_argument("--report", help="Write the JSON report here.")
    p.add_argument("--jobs", type=int, default=os.cpu_count() or 2)
    p.add_argument("--timeout", type=float, default=300.0,
                   help="Per-compile time limit in seconds.")
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.root:
        p.error("at least one --root is required (or use --self-test)")
    try:
        compiler = checkout_compiler.resolve()
        discovered = checkout_compiler.tracked_calr(REPO_ROOT, args.root)
        ignored = checkout_compiler.untracked_calr_count(REPO_ROOT, args.root)
    except checkout_compiler.CompilerResolutionError as e:
        print(f"fixture_compile_check: {e}", file=sys.stderr)
        return 2
    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    report = check(REPO_ROOT, manifest, args.root, discovered,
                   compiler.command, args.jobs, args.timeout)
    report["provenance"] = compiler.provenance()
    report["ignoredUntrackedCalr"] = ignored
    print_summary(report)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=2) + "\n",
                                     encoding="utf-8")
    return 0 if report["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
