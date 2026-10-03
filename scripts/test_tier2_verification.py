#!/usr/bin/env python3
"""0.24 G4 (#1241): Tier 1/Tier 2 verification reports truthfully.

Runs without a build (test.yml guard job):

  * Repository guards: eng/tier2-fixture-expectations.json lists every
    tracked fixture under samples/ and tests/ exactly once and agrees with
    BulkBenchmarkCompilationTests; no live script or workflow claims an AST
    round trip, selects an empty test category, or runs an installed `calor`.
  * Negative controls: the fixture check, the pinned-compiler resolver, the
    migrator checks, and the tier drivers fail closed, driven by a fake
    compiler in throwaway git repositories.
"""

from __future__ import annotations

import contextlib
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import checkout_compiler  # noqa: E402
import fixture_compile_check as fcc  # noqa: E402
import migrator_corpus_dryrun as dryrun  # noqa: E402
import migrator_revert_roundtrip as revert  # noqa: E402
import verify_phase1  # noqa: E402

REPO_ROOT = HERE.parent
MANIFEST = REPO_ROOT / "eng" / "tier2-fixture-expectations.json"

# Markers in a fixture's text drive the fake compiler's behaviour.
FAKE_COMPILER = '''import json, os, sys, time
args = sys.argv[1:]
if args[:2] == ["fix", "--help"]:
    h = sys.argv[0] + ".help"
    print(open(h).read() if os.path.exists(h) else "")
    sys.exit(0)
inputs = [args[i + 1] for i, a in enumerate(args) if a == "--input"]
out = args[args.index("--output") + 1] if "--output" in args else None
blob = "\\n".join(open(p, encoding="utf-8").read() for p in inputs)
if "HANG" in blob: time.sleep(30)
if "CRASH" in blob: sys.exit(134)
err = lambda c, m="x": {"code": c, "severity": "error", "message": m}
if "ZEROERR" in blob:
    print(json.dumps({"diagnostics": [err("Calor0410")]})); sys.exit(0)
codes = [l.split("FAIL:", 1)[1].strip() for l in blob.splitlines() if "FAIL:" in l]
if "NEEDS2" in blob and len(inputs) < 2: codes.append("Calor0200")
diags = [err(c, "Generated C# failed compilation (CS0266): x" if c == "Calor1002" else "x") for c in codes]
diags.append({"code": "Calor0411", "severity": "warning", "message": "w"})
print(json.dumps({"diagnostics": diags}))
if codes: sys.exit(1)
if "NOOUT" not in blob:
    for p in ([out] if out else [p[:-5] + ".g.cs" for p in inputs]):
        open(p, "w").write("// generated")
'''


def git(cwd: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True)


class FakeRepo:
    """A throwaway git repository with fixtures and a fake compiler."""

    def __init__(self, test: unittest.TestCase, files: dict[str, str]):
        td = tempfile.TemporaryDirectory()
        test.addCleanup(td.cleanup)
        self.root = Path(td.name)
        git(self.root, "init", "-q")
        for rel, text in files.items():
            (self.root / rel).parent.mkdir(parents=True, exist_ok=True)
            (self.root / rel).write_text(text, encoding="utf-8")
        git(self.root, "add", "-A")
        self.fake = self.root / "fake_calor.py"
        self.fake.write_text(FAKE_COMPILER, encoding="utf-8")
        self.cmd = [sys.executable, str(self.fake)]

    def check(self, groups, roots=("fx",), only=None, timeout=20.0) -> dict:
        found = checkout_compiler.tracked_calr(self.root, list(roots))
        if only is not None:
            found = sorted(set(found) & set(only))
        return fcc.check(self.root, {"schemaVersion": 1, "groups": groups},
                         list(roots), found, self.cmd, jobs=4, timeout=timeout)

    def run_one(self, *groups, timeout=20.0) -> dict:
        """Select only the files the groups name, so nothing else runs."""
        named = [f for g in groups for f in g["files"]]
        return self.check(list(groups), only=named, timeout=timeout)


def group(gid, files, expect="compile", mode="each", **extra):
    g = {"id": gid, "files": files, "mode": mode, "expect": expect,
         "evidence": "test"}
    if expect == "reject":
        g["negativeKind"] = "designed"
    if expect == "known-failure":
        g["defect"] = "test"
    g.update(extra)
    return {k: v for k, v in g.items() if v is not None}


def statuses(report: dict) -> dict:
    return {f: r["status"] for r in report["rows"] for f in r["files"]}


class FixtureCheckControls(unittest.TestCase):
    def setUp(self):
        self.repo = FakeRepo(self, {
            "fx/ok.calr": "fine", "fx/broken.calr": "FAIL:Calor0100",
            "fx/neg.calr": "FAIL:Calor0410", "fx/codegen.calr": "FAIL:Calor1002",
            "fx/crash.calr": "CRASH", "fx/noout.calr": "NOOUT",
            "fx/zeroerr.calr": "ZEROERR", "fx/hang.calr": "HANG",
            "fx/pair/a.calr": "NEEDS2", "fx/pair/b.calr": "fine",
        })
        for copy in ("fx/obj/copy.calr", "fx/bin/Release/ok.calr"):
            (self.repo.root / copy).parent.mkdir(parents=True)
            (self.repo.root / copy).write_text("FAIL:Calor0100")

    def test_single_fixture_outcomes(self):
        neg = {"errors": ["Calor0410"]}
        kf = {"errors": ["Calor1002:CS0266"]}
        cases = [
            ("fx/ok.calr", "compile", {}, "passed"),
            ("fx/broken.calr", "compile", {}, "failed"),
            ("fx/ok.calr", "reject", neg, "unexpected-pass"),
            ("fx/neg.calr", "reject", neg, "expected-negative"),
            ("fx/neg.calr", "reject", {"errors": ["Calor0411"]}, "failed"),
            ("fx/codegen.calr", "known-failure", kf, "known-failure"),
            ("fx/ok.calr", "known-failure", kf, "unexpected-pass"),
            ("fx/crash.calr", "compile", {}, "crashed"),
            ("fx/noout.calr", "compile", {}, "crashed"),
            ("fx/zeroerr.calr", "compile", {}, "crashed"),
            ("fx/gone.calr", "compile", {}, "invalid"),
            ("fx/obj/copy.calr", "compile", {}, "invalid"),
        ]
        for f, expect, extra, want in cases:
            with self.subTest(f=f, expect=expect, want=want):
                r = self.repo.run_one(group("g", [f], expect, **extra))
                self.assertEqual(statuses(r)[f], want)
                self.assertEqual(r["verdict"],
                                 "PASS" if want in fcc.SUCCESS else "FAIL")
                self.assertFalse(r["astRoundTrip"])

    def test_timeout_is_not_a_pass(self):
        r = self.repo.run_one(group("h", ["fx/hang.calr"], "reject",
                                    errors=["Calor0410"]), timeout=2)
        self.assertEqual(statuses(r)["fx/hang.calr"], "TimeoutOrUnavailable")
        self.assertEqual(r["verdict"], "FAIL")

    def test_unclassified_fixture_fails(self):
        r = self.repo.check([group("ok", ["fx/ok.calr"])])
        self.assertEqual(statuses(r)["fx/broken.calr"], "unclassified")
        self.assertEqual(r["verdict"], "FAIL")

    def test_build_output_is_never_discovered(self):
        found = checkout_compiler.tracked_calr(self.repo.root, ["fx"])
        self.assertFalse([f for f in found if "/obj/" in f or "/bin/" in f])
        counts = checkout_compiler.untracked_calr_count(self.repo.root, ["fx"])
        self.assertEqual(counts["buildOutput"], 2)
        git(self.repo.root, "add", "-f", "fx/bin/Release/ok.calr")
        with self.assertRaises(checkout_compiler.CompilerResolutionError):
            checkout_compiler.tracked_calr(self.repo.root, ["fx"])

    def test_double_registration_is_invalid(self):
        r = self.repo.run_one(group("a", ["fx/ok.calr"]),
                              group("b", ["fx/ok.calr"]))
        self.assertIn("invalid", [x["status"] for x in r["rows"]])

    def test_malformed_entries_are_invalid(self):
        bad = [
            group("o", ["fx/ok.calr"], options=["--help"]),
            group("r", ["fx/neg.calr"], "reject", errors=["Calor0410"],
                  negativeKind="maybe"),
            group("c", ["fx/ok.calr"], errors=["Calor0410"]),
            group("u", ["fx/neg.calr"], "reject",
                  errors=["Calor0410", "Calor0100"]),
            group("k", ["fx/neg.calr"], "known-failure", errors=["Calor0410"],
                  defect=" "),
            group("t", ["fx/ok.calr"], mode="together"),
            group("e", ["fx/ok.calr"], evidence=" "),
        ]
        for g in bad:
            with self.subTest(g["id"]):
                self.assertTrue(fcc.validate_manifest(
                    {"schemaVersion": 1, "groups": [g]}))
                r = self.repo.run_one(g)
                self.assertEqual(r["verdict"], "FAIL")
                self.assertTrue(all(x["status"] == "invalid"
                                    for x in r["rows"]))

    def test_multifile_group_is_compiled_together_outside_the_checkout(self):
        pair = ["fx/pair/a.calr", "fx/pair/b.calr"]
        r = self.repo.check([group("p", pair, mode="together")], ["fx/pair"])
        self.assertEqual(r["verdict"], "PASS")
        self.assertEqual(list((self.repo.root / "fx/pair").glob("*.g.cs")), [])
        r = self.repo.check([group("p", pair)], ["fx/pair"])
        self.assertEqual(statuses(r)["fx/pair/a.calr"], "failed")

    def test_empty_selection_fails(self):
        r = self.repo.check([group("ok", ["fx/ok.calr"])], roots=["nothing"])
        self.assertEqual((r["trackedFixtures"], r["verdict"]), (0, "FAIL"))


class PinnedCompilerControls(unittest.TestCase):
    def setUp(self):
        self.repo = FakeRepo(self, {
            "Directory.Build.props": "<Project><PropertyGroup><TargetFramework>"
                                     "net10.0</TargetFramework></PropertyGroup>"
                                     "</Project>",
            "src/Calor.Compiler/A.cs": "class A {}",
        })
        git(self.repo.root, "-c", "user.name=t", "-c", "user.email=t@t", "-c",
            "commit.gpgsign=false", "commit", "-q", "-m", "init")
        self.dll = (self.repo.root
                    / "src/Calor.Compiler/bin/Release/net10.0/calor.dll")

    def test_missing_build_never_falls_back_to_installed_tool(self):
        tool = self.repo.root / "pathbin" / "calor"
        tool.parent.mkdir()
        tool.write_text("#!/bin/sh\nexit 0\n")
        tool.chmod(0o755)
        path = f"{tool.parent}{os.pathsep}{os.environ.get('PATH', '')}"
        with mock.patch.dict(os.environ, {"PATH": path}):
            with self.assertRaises(checkout_compiler.CompilerResolutionError):
                checkout_compiler.resolve(self.repo.root)

    def test_stale_build_is_refused_and_fresh_build_is_pinned(self):
        self.dll.parent.mkdir(parents=True)
        self.dll.write_bytes(b"dll")
        os.utime(self.dll, (1_000_000, 1_000_000))
        with self.assertRaises(checkout_compiler.CompilerResolutionError):
            checkout_compiler.resolve(self.repo.root)
        os.utime(self.dll)
        pinned = checkout_compiler.resolve(self.repo.root)
        self.assertEqual(pinned.command, ["dotnet", str(self.dll.resolve())])


class MigratorAndDriverControls(unittest.TestCase):
    def run_migrator(self, module, help_text, files):
        repo = FakeRepo(self, files)
        (repo.root / "fx").mkdir(exist_ok=True)
        Path(str(repo.fake) + ".help").write_text(help_text)
        fake = mock.Mock(command=repo.cmd)
        err = io.StringIO()
        with mock.patch.object(checkout_compiler, "resolve",
                               return_value=fake), \
                mock.patch.object(dryrun, "REPO_ROOT", repo.root), \
                mock.patch.object(revert, "REPO_ROOT", repo.root), \
                contextlib.redirect_stderr(err), \
                contextlib.redirect_stdout(io.StringIO()):
            return module.main([str(repo.root / "fx")]), err.getvalue()

    def test_migrators_fail_closed(self):
        flags = "--drop-structural-ids --dry-run --revert --log"
        cases = [
            ("no flags", {"fx/a.calr": "x"}, dryrun.UNAVAILABLE,
             "TimeoutOrUnavailable"),
            (flags, {"other/a.calr": "x"}, 1, "empty selection"),
            # The fake `fix` rewrites nothing, so the control is untouched.
            (flags, {"fx/a.calr": "x"}, 1, "exercised nothing"),
        ]
        for module in (dryrun, revert):
            for help_text, files, want, message in cases:
                with self.subTest(module=module.__name__, case=message):
                    rc, err = self.run_migrator(module, help_text, files)
                    self.assertEqual(rc, want)
                    self.assertIn(message, err)

    def test_unavailable_is_never_ok(self):
        self.assertEqual(verify_phase1.status_of(3), "TimeoutOrUnavailable")
        self.assertEqual(verify_phase1.status_of(2), "FAIL")
        self.assertEqual(verify_phase1.status_of(0), "OK")


class RepositoryGuards(unittest.TestCase):
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_every_tracked_fixture_is_registered_exactly_once(self):
        self.assertEqual(fcc.validate_manifest(self.manifest), [])
        self.assertEqual(self.manifest["roots"], ["samples", "tests"])
        listed = [f for g in self.manifest["groups"] for f in g["files"]]
        self.assertEqual(len(listed), len(set(listed)))
        self.assertEqual(sorted(listed), checkout_compiler.tracked_calr(
            REPO_ROOT, self.manifest["roots"]))

    def test_benchmark_known_failures_match_the_bulk_test(self):
        src = (REPO_ROOT / "tests/Calor.Compiler.Tests/"
               "BulkBenchmarkCompilationTests.cs").read_text(encoding="utf-8")
        block = src[src.index("KnownGeneratedCSharpFailures ="):]
        bench = "tests/TestData/Benchmarks/"
        bulk = {bench + m for m in re.findall(r'"([^"]+\.calr)"',
                                             block[:block.index("];")])}
        listed = json.loads((REPO_ROOT / bench / "manifest.json").read_text())
        listed = {bench + b["calorFile"] for b in listed["benchmarks"]}
        known = {f for g in self.manifest["groups"]
                 if g["expect"] == "known-failure" for f in g["files"]}
        self.assertEqual(known & listed, bulk)

    def test_codegen_only_failures_are_not_incidental_negatives(self):
        for g in self.manifest["groups"]:
            if g.get("negativeKind") == "incidental":
                self.assertTrue(any(not e.startswith(("Calor1002", "Calor1006"))
                                    for e in g["errors"]), g["id"])

    def live_files(self):
        globs = ("scripts/*.py", "scripts/*.sh", "scripts/*.ps1",
                 ".github/workflows/*.yml")
        return [f for pattern in globs for f in REPO_ROOT.glob(pattern)
                if f.name != Path(__file__).name]

    def test_no_live_claims_or_empty_selections(self):
        claim = re.compile(r"ast[ _-]?round[ _-]?trip", re.IGNORECASE)
        negation = re.compile(r"\b(no|not|never|false)\b", re.IGNORECASE)
        for f in self.live_files():
            text = f.read_text(encoding="utf-8", errors="replace")
            for n, line in enumerate(text.splitlines(), 1):
                if claim.search(line) and not negation.search(line):
                    self.fail(f"{f.relative_to(REPO_ROOT)}:{n}: {line.strip()}")
            for cat in ("Category=Unit", "Category=DiagnosticSnapshot"):
                self.assertFalse(cat in text, f"{f.name} selects {cat}")
        self.assertFalse((REPO_ROOT / "scripts/ast_roundtrip_check.py").exists())

    def test_tier_scripts_never_resolve_an_installed_tool(self):
        for name in ("fixture_compile_check.py", "checkout_compiler.py",
                     "migrator_corpus_dryrun.py", "migrator_revert_roundtrip.py",
                     "verify_phase1.py", "verify_corpus.py",
                     "token_delta_corpus.py"):
            text = (REPO_ROOT / "scripts" / name).read_text(encoding="utf-8")
            for banned in ('which("calor")', '["calor"]', "rglob("):
                self.assertFalse(banned in text, f"{name} uses {banned}")

    def test_workflows_run_the_repaired_drivers(self):
        tier2 = (REPO_ROOT / ".github/workflows/tier2.yml").read_text()
        for needle in ("set -o pipefail", "scripts/verify_corpus.py --report",
                       "tier2-fixtures.json"):
            self.assertIn(needle, tier2)
        test = (REPO_ROOT / ".github/workflows/test.yml").read_text()
        self.assertIn("scripts/verify_phase1.py --self-test", test)
        self.assertIn("python3 scripts/test_tier2_verification.py", test)


if __name__ == "__main__":
    unittest.main(verbosity=1)
