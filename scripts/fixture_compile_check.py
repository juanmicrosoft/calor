#!/usr/bin/env python3
"""
fixture_compile_check.py — compile every tracked `.calr` fixture once (or
each registered multi-file group) with the checkout-built compiler and
compare the outcome with its expectation in
eng/tier2-fixture-expectations.json (#1241). It does not perform an AST round
trip. Its predecessor, `ast_roundtrip_check.py`, claimed one but did not.

Every tracked fixture under the selected roots needs exactly one entry:
`compile` (must compile), `reject` (must fail with exactly the registered
error signature; negativeKind says whether it is designed to fail or is
incidentally invalid Calor nothing compiles), or `known-failure` (a real
compiler failure, kept as a failure). Only `passed` and `expected-negative`
are successes; `known-failure`, `failed`, `unexpected-pass`, `unclassified`,
`invalid`, `crashed`, and `TimeoutOrUnavailable` all fail. There is no skip
path; an empty selection fails. Exit 0 = PASS, 1 = FAIL, 2 = unusable
compiler or bad arguments.

Usage: fixture_compile_check.py --root samples [--root tests]
       [--report report.json] [--jobs N] | --self-test
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


def errors_of(diagnostics: list[dict]) -> list[dict]:
    return [d for d in diagnostics
            if str(d.get("severity", "")).lower() == "error"]


def signature(diagnostics: list[dict]) -> list[str]:
    """One `file|code|declaration|message` entry per error, sorted, with
    duplicates kept, so an error with another location, declaration, or
    cause, or one more error, changes the signature. `file` is the base
    name; Calor1002 carries its Roslyn code; whitespace is collapsed.
    """
    sig = []
    for d in errors_of(diagnostics):
        code = str(d.get("code", ""))
        if code == "Calor1002":
            m = CS_CODE.search(str(d.get("message", "")))
            code = f"Calor1002:{m.group(1)}" if m else "Calor1002"
        where = Path(str((d.get("location") or {}).get("file") or "-")).name
        msg = " ".join(str(d.get("message", "")).split())
        sig.append(f"{where}|{code}|{d.get('declarationId') or '-'}|{msg}")
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
    ids = [g.get("id") if isinstance(g, dict) else None for g in groups]
    for i, g in enumerate(groups):
        def bad(why):
            errors.append(f"group {ids[i] or i}: {why}")
        if not isinstance(ids[i], str) or not ids[i] or ids.count(ids[i]) > 1:
            bad("missing or duplicate id")
            continue
        files, expect, errs = g.get("files"), g.get("expect"), g.get("errors")
        if (not isinstance(files, list) or not files or len(set(files))
                != len(files) or not all(isinstance(f, str) and f
                                         for f in files)):
            bad("files must be a non-empty list of distinct paths")
        if g.get("mode") not in MODES or (
                g["mode"] == "together" and len(files or []) < 2):
            bad(f"mode must be one of {MODES}; together needs 2+ files")
        if expect not in EXPECTATIONS:
            bad(f"expect must be one of {EXPECTATIONS}")
        opts = g.get("options", [])
        if not isinstance(opts, list) or not set(opts) <= ALLOWED_OPTIONS:
            bad(f"options must be a subset of {sorted(ALLOWED_OPTIONS)}")
        if expect == "compile" and errs not in (None, []):
            bad("a compile group lists no errors")
        if expect != "compile" and (
                not isinstance(errs, list) or not errs or errs != sorted(errs)
                or not all(isinstance(e, str) and e for e in errs)):
            bad(f"{expect} needs a sorted, non-empty error signature")
        if (expect == "reject") != (g.get("negativeKind") in NEGATIVE_KINDS) \
                or (expect != "reject" and "negativeKind" in g):
            bad(f"negativeKind in {NEGATIVE_KINDS} is required for reject "
                "and only there")
        if not (isinstance(g.get("evidence"), str) and g["evidence"].strip()):
            bad("evidence is required")
        if expect == "known-failure" and not (
                isinstance(g.get("defect"), str) and g["defect"].strip()):
            bad("known-failure needs a defect description")
    return errors


# ------------------------------------------------------------- execution

def _parse_result(rc: int, stdout: str) -> tuple:
    """Return (outcome, signature, detail, errors); None outcome = crashed."""
    start = stdout.find("{")
    try:
        doc = json.loads(stdout[start:]) if start >= 0 else None
    except json.JSONDecodeError:
        doc = None
    if not isinstance(doc, dict) or not isinstance(doc.get("diagnostics"), list):
        return None, [], f"exit {rc}; no JSON diagnostics document", []
    sig, errs = signature(doc["diagnostics"]), errors_of(doc["diagnostics"])
    if rc == 0 and not sig:
        return "ok", [], "", []
    if rc == 1 and sig:
        return "errors", sig, "", errs
    return None, sig, f"exit {rc} with error signature {sig}", errs


def run_unit(compiler_cmd: list[str], repo_root: Path, files: list[str],
             options: list[str], timeout: float) -> tuple:
    """Compile one file or multi-file group from a scratch copy (the CLI
    writes multi-file outputs next to each input; never into the checkout)."""
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
            return "timeout", [], f"exceeded {timeout:.0f}s", []
        outcome, sig, detail, errs = _parse_result(cp.returncode, cp.stdout)
        if outcome == "ok":
            outputs = ([work / "out.g.cs"] if len(inputs) == 1
                       else [p.with_suffix(".g.cs") for p in inputs])
            missing = [o.name for o in outputs
                       if not o.is_file() or o.stat().st_size == 0]
            if missing:
                return None, [], f"exit 0 but no generated C#: {missing}", []
        if outcome is None:
            detail = f"{detail} {(cp.stderr or cp.stdout)[-400:]}".strip()
        return outcome, sig, detail, errs


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
        outcome, sig, detail, errs = run_unit(compiler_cmd, repo_root, batch,
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
        if status not in SUCCESS:  # keep what is needed to investigate
            row["errorDiagnostics"] = [{"file": e.get("location", {}).get("file"),
                              "line": e.get("location", {}).get("line"),
                              "code": e.get("code"),
                              "message": str(e.get("message"))[:300]}
                             for e in errs]
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


def print_summary(report: dict) -> None:
    print(f"fixture_compile_check: {report['trackedFixtures']} tracked "
          f"fixture(s) under {report['roots']} (compiled once; no AST round "
          "trip)")
    for s, n in report["counts"].items():
        print(f"  {s:<22} {n}") if n else None
    for r in (r for r in report["rows"] if r["status"] not in SUCCESS):
        print(f"  [{r['status']}] {', '.join(r['files']) or '-'} "
              f"{r.get('detail', '')} got {r.get('signature')} expected "
              f"{r.get('expectedSignature', [])}")
    print(f"fixture_compile_check: {report['verdict']} "
          f"{'; '.join(report['failReasons'])}")


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
        (good, "reject", ["Calor0410|f001|Function 'Hi' uses effect 'cw' but does not declare it"], "unexpected-pass"),
        (effect, "reject", ["Calor0410|f001|Function 'Hi' uses effect 'cw' but does not declare it"], "expected-negative"),
        (effect, "reject", ["Calor0410|f001|Function 'Hi' uses effect 'db' but does not declare it"], "failed"),
        (effect, "known-failure", ["Calor0410|f001|Function 'Hi' uses effect 'cw' but does not declare it"], "known-failure"),
    ]
    failures = 0
    with tempfile.TemporaryDirectory() as td:
        root, groups = Path(td), []
        (root / "fx").mkdir()
        for i, (src, expect, errs, _want) in enumerate(cases):
            (root / f"fx/c{i}.calr").write_text(src, encoding="utf-8")
            g = {"id": f"c{i}", "files": [f"fx/c{i}.calr"], "mode": "each",
                 "expect": expect, "evidence": "self-test"}
            g.update({"errors": [f"c{i}.calr|{e}" for e in errs]} if errs
                     else {})
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
