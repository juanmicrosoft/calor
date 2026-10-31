#!/usr/bin/env python3
"""0.24 G4 (#1241): Tier 1/Tier 2 verification reports truthfully.

Repository guards plus fail-closed negative controls (a fake compiler in
throwaway git repositories). No build needed; run by the test.yml guard job.
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
if args[:1] == ["fix"]:  # rewrites "§/F{" <-> "§/F[" unless fake.noop exists
    fixes, dry, rev = [], "--dry-run" in args, "--revert" in args
    for d, _, names in os.walk(args[1]):
        for p in (os.path.join(d, x) for x in names if x.endswith(".calr")):
            t = open(p, encoding="utf-8").read()
            new = t.replace("§/F[", "§/F{") if rev else t.replace("§/F{", "§/F[")
            new += "!" if rev and "CORRUPT" in t else ""
            if new == t or os.path.exists(sys.argv[0] + ".noop"): continue
            fixes.append({"file": os.path.relpath(p, args[1]), "count": 1})
            if not dry or "DRYWRITE" in t:
                open(p, "w", encoding="utf-8").write(new)
    if os.path.exists(sys.argv[0] + ".skipcontrol"):
        fixes = [{"file": "fx/a.calr", "count": 1}]
    print(json.dumps({"data": {"fixes": fixes}})); sys.exit(0)
inputs = [args[i + 1] for i, a in enumerate(args) if a == "--input"]
out = args[args.index("--output") + 1] if "--output" in args else None
texts = {os.path.basename(p): open(p, encoding="utf-8").read() for p in inputs}
blob = "\\n".join(texts.values())
if "HANG" in blob: time.sleep(30)
if "CRASH" in blob: sys.exit(134)
err = lambda f, c, m="x": {"code": c, "severity": "error", "location": {"file": f},
    "message": "Generated C# failed compilation (CS0266): x" if c == "Calor1002" else m}
if "ZEROERR" in blob:
    print(json.dumps({"diagnostics": [err("z.calr", "Calor0410")]})); sys.exit(0)
codes = [(f, *l.split("FAIL:", 1)[1].strip().split(":", 1)) for f, t in texts.items()
         for l in t.splitlines() if "FAIL:" in l]
if "NEEDS2" in blob and len(inputs) < 2: codes.append(("a.calr", "Calor0200"))
diags = [err(*c) for c in codes]
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


class ScratchDeclarationControls(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / "in" / "fixture.calr"
        self.source.parent.mkdir()
        self.source.write_text("controlled fixture", encoding="utf-8")
        self.sidecar = self.source.parent / ".calor" / "ids.json"
        self.sidecar.parent.mkdir()
        self.declaration = {
            "Id": "f_000000000001", "File": "fixture.calr", "Kind": 1,
            "Container": "Demo", "Name": "Run",
            "Signature": '{"arity":0,"parameters":[],"output":"void"}',
        }

    def keys(self, *declarations):
        self.sidecar.write_text(json.dumps({
            "Version": 1, "Files": ["fixture.calr"],
            "Declarations": list(declarations),
        }), encoding="utf-8")
        return fcc.scratch_declaration_keys(self.root, [self.source])

    def test_fresh_ids_keep_exact_semantic_identity(self):
        first = self.keys(self.declaration)["f_000000000001"]
        second = self.keys(dict(self.declaration, Id="f_000000000002"))
        self.assertEqual(first, second["f_000000000002"])
        for field, value in (("Kind", 2), ("Container", "Other"),
                             ("Name", "Other"), ("Signature", "different")):
            changed = self.keys(dict(self.declaration, **{field: value}))
            self.assertNotEqual(first, changed["f_000000000001"])

    def test_duplicate_or_invalid_storage_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.keys(self.declaration, self.declaration)
        self.sidecar.write_text("{ malformed", encoding="utf-8")
        with self.assertRaises(ValueError):
            fcc.scratch_declaration_keys(self.root, [self.source])
        with self.assertRaisesRegex(ValueError, "invalid"):
            self.keys(dict(self.declaration, Kind=True))

    def test_missing_diagnostic_identity_is_not_normalized_away(self):
        diagnostic = {"code": "Calor0410", "severity": "error",
                      "message": "controlled", "declarationId": "f_missing"}
        with self.assertRaisesRegex(ValueError, "unresolved"):
            fcc.signature([diagnostic], self.keys(self.declaration))
        result = subprocess.CompletedProcess(
            [], 1, json.dumps({"diagnostics": [diagnostic]}), "")
        with mock.patch.object(fcc.subprocess, "run", return_value=result):
            outcome, _, detail, _ = fcc.run_unit(
                ["unused"], self.root, ["in/fixture.calr"], [], 1)
        self.assertIsNone(outcome)
        self.assertIn("invalid scratch declaration identity", detail)

    def test_distinct_input_paths_keep_distinct_identity(self):
        first = self.keys(self.declaration)["f_000000000001"]
        nested = self.source.parent / "nested" / self.source.name
        nested.parent.mkdir()
        nested.write_text("controlled fixture", encoding="utf-8")
        other = self.keys(dict(self.declaration, File="nested/fixture.calr"))
        self.assertNotEqual(first, other["f_000000000001"])

    def test_unavailable_parser_identity_preserves_exact_error_causes(self):
        diagnostic = {"code": "Calor0830", "severity": "error",
                      "location": {"file": "broken.calr"},
                      "message": "retired closer", "declarationId": None}
        expected = ["broken.calr|Calor0830|-|retired closer"]
        self.assertEqual(fcc.signature([diagnostic], {}), expected)
        for field, value in (("code", "Calor0100"), ("message", "another cause"),
                             ("location", {"file": "other.calr"})):
            self.assertNotEqual(fcc.signature([dict(diagnostic, **{field: value})], {}),
                                expected)
        self.assertNotEqual(fcc.signature([diagnostic, diagnostic], {}), expected)
        with self.assertRaisesRegex(ValueError, "unresolved"):
            fcc.signature([dict(diagnostic, declarationId="f_parser_local")], {})


class CurrentInputControls(unittest.TestCase):
    def setUp(self):
        self.registry = "current/inputs.json"
        self.original = "fx/original.calr"
        self.mirror = "current/" + self.original
        self.repo = FakeRepo(self, {
            self.original: "FAIL:Calor0100:historical source",
            self.mirror: "current source",
            "fx/direct.calr": "fine",
            self.registry: json.dumps({"originalPaths": [self.original]}),
        })

    def check(self, groups):
        return fcc.check(self.repo.root, {
            "schemaVersion": 1, "currentInputs": self.registry, "groups": groups,
        }, ["fx", "current"], checkout_compiler.tracked_calr(
            self.repo.root, ["fx", "current"]), self.repo.cmd, 2, 20)

    def test_explicit_resolution_keeps_subjects_paths_and_frozen_bytes(self):
        before = (self.repo.root / self.original).read_bytes()
        report = self.check([group("original", [self.original]),
                             group("mirror", [self.mirror]),
                             group("direct", ["fx/direct.calr"])])
        self.assertEqual(report["verdict"], "PASS")
        self.assertEqual(report["trackedFixtures"], 3)
        self.assertEqual(report["counts"]["passed"], 3)
        rows = {r["group"]: r for r in report["rows"]}
        self.assertEqual(rows["original"]["files"], [self.original])
        self.assertEqual(rows["original"]["currentFiles"], [self.mirror])
        self.assertEqual(rows["mirror"]["currentFiles"], [self.mirror])
        self.assertEqual(rows["direct"]["currentFiles"], ["fx/direct.calr"])
        self.assertEqual((self.repo.root / self.original).read_bytes(), before)

    def test_mirror_registration_and_original_expectation_are_not_waived(self):
        groups = [group("original", [self.original], "known-failure",
                        errors=["original.calr|Calor0100|-|historical source"]),
                  group("direct", ["fx/direct.calr"])]
        report = self.check(groups)
        self.assertEqual(report["verdict"], "FAIL")
        self.assertEqual(statuses(report)[self.original], "unexpected-pass")
        self.assertEqual(statuses(report)[self.mirror], "unclassified")

    def test_resolution_preserves_multifile_group_and_options(self):
        report = self.check([group("pair", [self.original, "fx/direct.calr"],
                                   mode="together", options=["--no-enforce-effects"]),
                             group("mirror", [self.mirror])])
        self.assertEqual(report["verdict"], "PASS")
        row = next(r for r in report["rows"] if r["group"] == "pair")
        self.assertEqual(row["files"], [self.original, "fx/direct.calr"])
        self.assertEqual(row["currentFiles"], [self.mirror, "fx/direct.calr"])
        self.assertEqual(row["options"], ["--no-enforce-effects"])

    def test_missing_registry_or_mirror_fails_without_historical_fallback(self):
        for missing in (self.registry, self.mirror):
            with self.subTest(missing=missing):
                path = self.repo.root / missing
                saved = path.read_bytes()
                path.unlink()
                try:
                    with mock.patch.object(fcc, "run_unit") as compile_input:
                        report = self.check([group("original", [self.original])])
                    compile_input.assert_not_called()
                    self.assertEqual(report["counts"]["invalid"], 1)
                    self.assertEqual(report["counts"]["passed"], 0)
                    self.assertIn("invalid current input registry", report["rows"][0]["detail"])
                finally:
                    path.write_bytes(saved)

    def test_duplicate_malformed_chained_and_escaping_mappings_fail_closed(self):
        documents = [
            {"originalPaths": [self.original, self.original]},
            {"originalPaths": []},
            {"originalPaths": "not a list"},
            {"originalPaths": ["../outside.calr"]},
            {"originalPaths": ["/outside.calr"]},
            {"originalPaths": ["./" + self.original]},
            {"originalPaths": [True]},
            {"originalPaths": [self.original, self.mirror]},
        ]
        nested = self.repo.root / "current" / self.mirror
        nested.parent.mkdir(parents=True)
        nested.write_text("must not follow a chain", encoding="utf-8")
        for document in documents:
            with self.subTest(document=document):
                (self.repo.root / self.registry).write_text(json.dumps(document))
                report = self.check([group("original", [self.original])])
                self.assertEqual(report["verdict"], "FAIL")
                self.assertEqual(report["counts"]["invalid"], 1)
        (self.repo.root / self.registry).write_text("{ malformed")
        self.assertEqual(self.check([group("original", [self.original])])[
            "counts"]["invalid"], 1)

    def test_explicit_invalid_registry_path_does_not_disable_resolution(self):
        manifest = {"schemaVersion": 1, "groups": [group("original", [self.original])]}
        for value in (None, False, "", "../outside.json", "/outside.json"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                fcc.current_input_paths(self.repo.root, dict(manifest, currentInputs=value))

    def test_distinct_registrations_cannot_alias_one_physical_mirror(self):
        alias = "fx/alias.calr"
        (self.repo.root / alias).write_text("historical alias", encoding="utf-8")
        (self.repo.root / ("current/" + alias)).symlink_to(
            Path(self.mirror).name)
        (self.repo.root / self.registry).write_text(json.dumps({
            "originalPaths": [self.original, alias],
        }))
        report = self.check([group("original", [self.original])])
        self.assertEqual(report["counts"]["invalid"], 1)
        self.assertIn("duplicate", report["rows"][0]["detail"])


class FixtureCheckControls(unittest.TestCase):
    def setUp(self):
        self.repo = FakeRepo(self, {
            "fx/ok.calr": "fine", "fx/broken.calr": "FAIL:Calor0100",
            "fx/two.calr": "FAIL:Calor0410\nFAIL:Calor0410",
            "fx/sub.calr": "FAIL:Calor0410:effect db",
            "fx/ws/a.calr": "FAIL:Calor0410", "fx/ws/b.calr": "fine",
            "fx/neg.calr": "FAIL:Calor0410", "fx/codegen.calr": "FAIL:Calor1002",
            "fx/crash.calr": "CRASH", "fx/noout.calr": "NOOUT",
            "fx/zeroerr.calr": "ZEROERR", "fx/hang.calr": "HANG",
            "fx/pair/a.calr": "NEEDS2", "fx/pair/b.calr": "fine",
        })
        for copy in ("fx/obj/copy.calr", "fx/bin/Release/ok.calr"):
            (self.repo.root / copy).parent.mkdir(parents=True)
            (self.repo.root / copy).write_text("FAIL:Calor0100")

    def test_single_fixture_outcomes(self):
        neg = {"errors": ["neg.calr|Calor0410|-|x"]}
        kf = {"errors": ["codegen.calr|Calor1002:CS0266|-|Generated C# failed "
                         "compilation (CS0266): x"]}
        cases = [
            ("fx/ok.calr", "compile", {}, "passed"),
            ("fx/broken.calr", "compile", {}, "failed"),
            ("fx/ok.calr", "reject", neg, "unexpected-pass"),
            ("fx/neg.calr", "reject", neg, "expected-negative"),
            ("fx/neg.calr", "reject", {"errors": ["neg.calr|Calor0411|-|x"]},
             "failed"),
            ("fx/two.calr", "reject", {"errors": ["two.calr|Calor0410|-|x"]},
             "failed"),  # one more error of the same code
            ("fx/sub.calr", "reject", {"errors": ["sub.calr|Calor0410|-|x"]},
             "failed"),  # same file and code, another cause
            ("fx/codegen.calr", "known-failure", kf, "known-failure"),
            ("fx/ok.calr", "known-failure", kf, "unexpected-pass"),
            ("fx/crash.calr", "compile", {}, "crashed"),
            ("fx/noout.calr", "compile", {}, "crashed"),
            ("fx/zeroerr.calr", "compile", {}, "crashed"),
            ("fx/gone.calr", "compile", {}, "invalid"),
            ("fx/obj/copy.calr", "compile", {}, "invalid"),
            ("fx/hang.calr", "compile", {}, "TimeoutOrUnavailable"),
        ]
        for f, expect, extra, want in cases:
            with self.subTest(f=f, expect=expect, want=want):
                r = self.repo.run_one(group("g", [f], expect, **extra),
                                      timeout=2)
                self.assertEqual(statuses(r)[f], want)
                self.assertEqual(r["verdict"],
                                 "PASS" if want in fcc.SUCCESS else "FAIL")
                self.assertFalse(r["astRoundTrip"])

    def test_negative_error_must_come_from_the_registered_file(self):
        ws = ["fx/ws/a.calr", "fx/ws/b.calr"]
        for errs, want in ((["a.calr|Calor0410|-|x"], "expected-negative"),
                           (["b.calr|Calor0410|-|x"], "failed")):
            r = self.repo.run_one(group("w", ws, "reject", "together",
                                        errors=errs))
            self.assertEqual(statuses(r)["fx/ws/a.calr"], want)
            if want == "failed":
                self.assertEqual(r["rows"][0]["errorDiagnostics"][0]["code"],
                                 "Calor0410")

    def test_unclassified_fixture_and_empty_selection_fail(self):
        r = self.repo.check([group("ok", ["fx/ok.calr"])])
        self.assertEqual(statuses(r)["fx/broken.calr"], "unclassified")
        self.assertEqual(r["verdict"], "FAIL")
        r = self.repo.check([group("ok", ["fx/ok.calr"])], roots=["nothing"])
        self.assertEqual((r["trackedFixtures"], r["verdict"]), (0, "FAIL"))

    def test_build_output_is_never_discovered(self):
        found = checkout_compiler.tracked_calr(self.repo.root, ["fx"])
        self.assertFalse([f for f in found if "/obj/" in f or "/bin/" in f])
        counts = checkout_compiler.untracked_calr_count(self.repo.root, ["fx"])
        self.assertEqual(counts["buildOutput"], 2)
        git(self.repo.root, "add", "-f", "fx/bin/Release/ok.calr")
        with self.assertRaises(checkout_compiler.CompilerResolutionError):
            checkout_compiler.tracked_calr(self.repo.root, ["fx"])

    def test_malformed_entries_are_invalid(self):
        r = self.repo.run_one(group("a", ["fx/ok.calr"]),
                              group("b", ["fx/ok.calr"]))  # double registration
        self.assertIn("invalid", [x["status"] for x in r["rows"]])
        bad = [
            group("o", ["fx/ok.calr"], options=["--help"]),
            group("r", ["fx/neg.calr"], "reject", errors=["n|Calor0410"],
                  negativeKind="maybe"),
            group("c", ["fx/ok.calr"], errors=["n|Calor0410"]),
            group("u", ["fx/neg.calr"], "reject",
                  errors=["n|Calor0410", "n|Calor0100"]),
            group("k", ["fx/neg.calr"], "known-failure", errors=["n|Calor0410"],
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


class PinnedCompilerControls(unittest.TestCase):
    def setUp(self):
        self.repo = FakeRepo(self, {
            "Directory.Build.props": "<Project><PropertyGroup><TargetFramework>"
                                     "net10.0</TargetFramework></PropertyGroup>"
                                     "</Project>",
            "src/Calor.Compiler/Sub/A.cs": "class A {}",
            "src/Calor.Compiler/C.csproj":
                '<EmbeddedResource Include="..\\..\\bench\\m.json" />',
            "bench/m.json": "{}",
        })
        self.git = lambda *a: git(self.repo.root, "-c", "user.name=t", "-c",
                                  "user.email=t@t", "-c", "commit.gpgsign=false",
                                  *a)
        self.git("commit", "-q", "-m", "init")
        (self.repo.root / "src/Calor.Compiler/Sub/U.json").write_text("{}")
        self.dll = (self.repo.root
                    / "src/Calor.Compiler/bin/Release/net10.0/calor.dll")
        self.dll.parent.mkdir(parents=True)
        self.dll.write_bytes(b"dll")
        for d, _, names in os.walk(self.repo.root):  # inputs older than dll
            for x in [d, *(os.path.join(d, n) for n in names)]:
                os.utime(x, (1_000_000_000,) * 2)
        os.utime(self.dll, (1_500_000_000,) * 2)

    def test_missing_build_never_falls_back_to_installed_tool(self):
        self.dll.unlink()  # `calor` on PATH is never consulted (see guards)
        with mock.patch.dict(os.environ, {"PATH": str(self.repo.root)}):
            (self.repo.root / "calor").write_text("#!/bin/sh\n")
            with self.assertRaises(checkout_compiler.CompilerResolutionError):
                checkout_compiler.resolve(self.repo.root)

    def test_fresh_build_is_pinned_and_any_input_change_is_stale(self):
        pinned = checkout_compiler.resolve(self.repo.root)
        self.assertEqual(pinned.command, ["dotnet", str(self.dll.resolve())])
        f = lambda rel: self.repo.root / rel  # noqa: E731 (fresh repo per case)
        a = "src/Calor.Compiler/Sub/A.cs"
        changes = {
            "edited source": lambda: f(a).write_text("class B {}"),
            "edited untracked": lambda: f("src/Calor.Compiler/Sub/U.json")
            .write_text("[]"),
            "linked resource": lambda: f("bench/m.json").write_text("[]"),
            "unstaged deletion": lambda: f(a).unlink(),
            "committed linked deletion": lambda: (self.git("rm", "-q", "bench/m.json"),
                                                  self.git("commit", "-qm", "d")),
            "committed deletion": lambda: (self.git("rm", "-q", a),
                                           self.git("commit", "-q", "-m", "d")),
        }
        for name, change in changes.items():
            with self.subTest(name):
                self.setUp()
                change()
                with self.assertRaises(checkout_compiler.CompilerResolutionError):
                    checkout_compiler.resolve(self.repo.root)


class MigratorAndDriverControls(unittest.TestCase):
    def run_migrator(self, module, help_text, files, flag=None):
        repo = FakeRepo(self, files)
        (repo.root / "fx").mkdir(exist_ok=True)
        Path(str(repo.fake) + ".help").write_text(help_text)
        if flag:  # fake.noop: rewrite nothing; fake.skipcontrol: omit it
            Path(f"{repo.fake}.{flag}").write_text("")
        fake = mock.Mock(command=repo.cmd)
        err = io.StringIO()
        with mock.patch.object(checkout_compiler, "resolve",
                               return_value=fake), \
                mock.patch.object(dryrun, "REPO_ROOT", repo.root), \
                mock.patch.object(revert, "REPO_ROOT", repo.root), \
                contextlib.redirect_stderr(err), \
                contextlib.redirect_stdout(io.StringIO()):
            rc = module.main([str(repo.root / "fx")])
        self.assertEqual(files, {f: (repo.root / f).read_text()
                                 for f in files}, "checkout was modified")
        return rc, err.getvalue()

    def test_migrators_fail_closed(self):
        flags = "--drop-structural-ids --dry-run --revert --log"
        ok = {"fx/a.calr": "§/F{x}"}
        cases = [  # (modules, help, files, fake flag, exit, stderr text)
            ((dryrun, revert), "no flags", ok, None, dryrun.UNAVAILABLE,
             "TimeoutOrUnavailable"),
            ((dryrun, revert), flags, {"other/a.calr": "x"}, None, 1,
             "empty selection"),
            ((dryrun, revert), flags, ok, "noop", 1, "exercised nothing"),
            ((dryrun,), flags, ok, "skipcontrol", 1, "exercised nothing"),
            ((dryrun, revert), flags, ok, None, 0, ""),
            ((dryrun,), flags, {"fx/a.calr": "§/F{x} DRYWRITE"}, None, 1,
             "changed despite --dry-run"),
            ((revert,), flags, {"fx/a.calr": "§/F{x} CORRUPT"}, None, 1,
             "byte-mismatch post-revert"),
        ]
        for modules, help_text, files, flag, want, message in cases:
            for module in modules:
                with self.subTest(module=module.__name__, case=message):
                    rc, err = self.run_migrator(module, help_text, files, flag)
                    self.assertEqual(rc, want, err)
                    self.assertIn(message, err)

    def test_each_failed_or_unavailable_step_fails_its_driver(self):
        import verify_corpus
        self.assertEqual(verify_phase1.status_of(3), "TimeoutOrUnavailable")
        required = {  # (driver, argv): commands that must run, independently
            (verify_phase1, ()): ["fixture_compile_check.py --root samples"],
            (verify_phase1, ("--self-test",)): [
                "byte_preservation_check.py --self-test",
                "fixture_compile_check.py --self-test"],
            (verify_corpus, ()): ["verify_phase1.py --corpus all",
                                  "migrator_corpus_dryrun.py", "migrator_revert"]}
        for (module, argv), needed in required.items():
            names, cmds, argv = [], [], list(argv)
            with mock.patch.object(module, "run_step", side_effect=lambda n, c: (
                    names.append(n), cmds.append(" ".join(c)), 0)[2]), \
                    contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(module.main(argv), 0)
            for need in needed:
                self.assertTrue(any(need in c for c in cmds), need)
            for bad in names:
                for rc in (1, 2, 3):
                    want = 0 if "informational" in bad else 1
                    with self.subTest(module=module.__name__, argv=argv,
                                      step=bad, rc=rc), \
                            mock.patch.object(module, "run_step", side_effect=(
                                lambda n, c: rc if n == bad else 0)), \
                            contextlib.redirect_stdout(io.StringIO()), \
                            contextlib.redirect_stderr(io.StringIO()):
                        self.assertEqual(module.main(argv), want)


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

    def test_all_current_copies_have_explicit_consumer_expectations(self):
        mapping = fcc.current_input_paths(REPO_ROOT, self.manifest)
        self.assertTrue(mapping)
        groups = {f: g for g in self.manifest["groups"] for f in g["files"]}
        self.assertTrue(set(mapping.values()) <= groups.keys())
        for original, current in mapping.items():
            if original not in groups:
                continue
            with self.subTest(original=original):
                self.assertEqual(groups[original]["expect"], groups[current]["expect"])
                self.assertEqual(groups[original].get("options", []),
                                 groups[current].get("options", []))
        ds15 = [g for g in self.manifest["groups"]
                if g["id"] == "active:ds15-conversion"]
        self.assertEqual(len(ds15), 1)
        self.assertEqual(set(ds15[0]["options"]),
                         {"--no-enforce-effects", "--permissive-effects"})
        self.assertEqual(ds15[0]["expect"], "compile")

    def test_proven_current_defect_resolutions_require_compilation(self):
        groups = {g["id"]: g for g in self.manifest["groups"]}
        for identifier, files, options in [
            ("conversion-snapshots:08-03.approved",
             ["tests/Calor.Conversion.Tests/Snapshots/08-03.approved.calr"],
             ["--permissive-effects"]),
            ("benchmarks:unregistered:NullDeref_buggy",
             ["tests/TestData/Benchmarks/ErrorDetection/NullDeref_buggy.calr"],
             ["--no-enforce-effects"]),
        ]:
            with self.subTest(group=identifier):
                current = groups[identifier]
                self.assertEqual(current["expect"], "compile")
                self.assertEqual(current["mode"], "each")
                self.assertEqual(current["files"], files)
                self.assertEqual(current["options"], options)
                self.assertNotIn("errors", current)
                self.assertNotIn("defect", current)
        malformed = groups["lint-scenarios:mismatched_ids"]
        self.assertEqual(malformed["expect"], "reject")
        self.assertEqual(len(malformed["errors"]), 2)
        self.assertTrue(all(error.split("|", 3)[1:3] == ["Calor0830", "-"]
                            for error in malformed["errors"]))

    def test_current_gold_references_require_default_compilation(self):
        mapping = fcc.current_input_paths(REPO_ROOT, self.manifest)
        groups = {f: g for g in self.manifest["groups"] for f in g["files"]}
        gold = [current for original, current in mapping.items()
                if original.startswith("bench/phase0-agent-native/pairs/")
                and "/reference/calor/" in original]
        self.assertTrue(gold)
        for current in gold:
            with self.subTest(current=current):
                group = groups[current]
                self.assertEqual(group["expect"], "compile")
                self.assertEqual(group["mode"], "each")
                self.assertEqual(group.get("options", []), [])
                self.assertNotIn("errors", group)

    def test_codegen_only_failures_are_not_incidental_negatives(self):
        for g in self.manifest["groups"]:
            if g.get("negativeKind") == "incidental":
                codes = [e.split("|", 1)[1] for e in g["errors"]]
                self.assertTrue(any(not c.startswith(("Calor1002", "Calor1006"))
                                    for c in codes), g["id"])

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
        for name in ("fixture_compile_check", "checkout_compiler", "verify_phase1",
                     "migrator_corpus_dryrun", "migrator_revert_roundtrip",
                     "verify_corpus", "token_delta_corpus"):
            text = (REPO_ROOT / f"scripts/{name}.py").read_text(encoding="utf-8")
            for banned in ('which("calor")', '["calor"]', "rglob("):
                self.assertFalse(banned in text, f"{name} uses {banned}")

    def test_workflows_run_the_repaired_drivers(self):
        wf = REPO_ROOT / ".github/workflows"
        for name, needle in (("tier2", "set -o pipefail"),
                             ("tier2", "scripts/verify_corpus.py --report"),
                             ("test", "scripts/verify_phase1.py --self-test"),
                             ("test", "scripts/test_tier2_verification.py")):
            self.assertIn(needle, (wf / f"{name}.yml").read_text())


if __name__ == "__main__":
    unittest.main(verbosity=1)
