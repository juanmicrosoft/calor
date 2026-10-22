#!/usr/bin/env python3
"""Tests for scripts/release_checks.py (#1582): each check passes the good case and fails each bad
one. No network: the registry checks take a fake fetch function."""

from __future__ import annotations

import io
import subprocess
import sys
import tempfile
import unittest
import zipfile
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import release_checks as rc  # noqa: E402

VERSION = "0.26.0"


def nupkg(path: Path, entries: dict[str, bytes]) -> Path:
    with zipfile.ZipFile(path, "w") as archive:
        for name, data in entries.items():
            archive.writestr(name, data)
    return path


def packages(root: Path, extra: dict[str, bytes] | None = None) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    for pid in ("calor", "Calor.Sdk"):
        nupkg(root / f"{pid}.{VERSION}.nupkg", {f"{pid}.nuspec": f"<id>{pid}</id>".encode(), "lib/a.dll": pid.encode(), **(extra or {})})
    return root


class FakeRegistry:
    """Serves the given files by package id; missing ids answer `missing` (404 by default)."""

    def __init__(self, served: dict[str, Path], missing: int = 404, fail_first: int = 0):
        self.served, self.missing, self.fail_first, self.calls = served, missing, fail_first, 0

    def __call__(self, url: str, dest: Path) -> int:
        self.calls += 1
        if self.calls <= self.fail_first:
            raise OSError("connection reset")
        package_id = url.rsplit("/", 3)[1]
        if package_id not in self.served:
            return self.missing
        dest.write_bytes(self.served[package_id].read_bytes())
        return 200


def signed(source: Path, dest: Path, tamper: bool = False) -> Path:
    """What nuget.org serves: the package plus a repository signature (optionally altered)."""
    with zipfile.ZipFile(source) as archive:
        entries = {i.filename: archive.read(i) for i in archive.infolist()}
    entries[".signature.p7s"] = b"repository signature"
    if tamper:
        entries["lib/a.dll"] = b"something else"
    return nupkg(dest, entries)


class RegistryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.local = packages(self.root / "nupkg")

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def served(self, tamper: str | None = None) -> dict[str, Path]:
        out = {}
        for pid, name in (("calor", "calor"), ("calor.sdk", "Calor.Sdk")):
            out[pid] = signed(self.local / f"{name}.{VERSION}.nupkg", self.root / f"served-{pid}.nupkg", tamper == pid)
        return out

    def check(self, mode: str, fetch, **kw) -> list[str]:
        with redirect_stdout(io.StringIO()):
            return rc.registry_problems(VERSION, self.local, mode, self.root / f"work-{mode}", fetch=fetch,
                                        sleep=lambda _: None, **kw)

    def test_absent_version_passes_the_existing_check(self) -> None:
        self.assertEqual([], self.check("existing", FakeRegistry({})))

    def test_same_package_with_a_repository_signature_passes(self) -> None:
        self.assertEqual([], self.check("existing", FakeRegistry(self.served())))
        self.assertEqual([], self.check("served", FakeRegistry(self.served())))

    def test_a_different_package_already_served_fails(self) -> None:
        for pid in ("calor", "calor.sdk"):
            problems = self.check("existing", FakeRegistry(self.served(tamper=pid)))
            self.assertEqual(1, len(problems), pid)
            self.assertTrue(problems[0].startswith(rc.ALREADY_RELEASED))
            self.assertEqual(1, len(self.check("served", FakeRegistry(self.served(tamper=pid)))), pid)

    def test_an_entry_differing_only_in_another_signature_name_fails(self) -> None:
        served = self.served()
        with zipfile.ZipFile(served["calor"], "a") as archive:
            archive.writestr("extra.p7s", b"x")
        self.assertEqual(1, len(self.check("existing", FakeRegistry(served))))

    def test_an_unknown_answer_fails_the_existing_check(self) -> None:
        self.assertEqual(2, len(self.check("existing", FakeRegistry({}, missing=500))))
        self.assertEqual(2, len(self.check("existing", FakeRegistry({}, fail_first=99))))

    def test_served_check_polls_then_requires_both_packages(self) -> None:
        self.assertEqual([], self.check("served", FakeRegistry(self.served(), fail_first=3), attempts=5))
        served = self.served()
        del served["calor.sdk"]
        problems = self.check("served", FakeRegistry(served), attempts=3)
        self.assertEqual(1, len(problems))
        self.assertIn("does not serve calor.sdk", problems[0])
        self.assertFalse(problems[0].startswith(rc.ALREADY_RELEASED))

    def test_dry_run_downgrades_only_already_released_findings(self) -> None:
        out = io.StringIO()
        with redirect_stdout(out):
            self.assertEqual(0, rc.report([rc.ALREADY_RELEASED + "x"], "t", dry_run=True))
            self.assertEqual(1, rc.report([rc.ALREADY_RELEASED + "x"], "t", dry_run=False))
            self.assertEqual(1, rc.report([rc.ALREADY_RELEASED + "x", "other"], "t", dry_run=True))
        self.assertIn("::warning::", out.getvalue())

    def test_bundle_must_be_exactly_the_two_packages(self) -> None:
        self.assertEqual([], rc.bundle_problems(self.local, VERSION))
        (self.local / "stray.nupkg").write_bytes(b"x")
        self.assertEqual(1, len(rc.bundle_problems(self.local, VERSION)))
        (self.local / "stray.nupkg").unlink()
        (self.local / f"calor.{VERSION}.nupkg").unlink()
        self.assertEqual(1, len(rc.bundle_problems(self.local, VERSION)))
        self.assertEqual(1, len(rc.bundle_problems(self.root / "missing", VERSION)))


class WordingTests(unittest.TestCase):
    def test_claims_fail_and_negations_pass(self) -> None:
        self.assertEqual(["independently verified"], rc.wording_hits("a.md", "The results were independently verified."))
        self.assertEqual([], rc.wording_hits("a.md", "The results were not independently verified."))
        self.assertEqual(["independent adjudication"], rc.wording_hits("a.md", "independent adjudication"))
        self.assertTrue(rc.wording_hits("a.md", "*independently* *verified*"))
        self.assertTrue(rc.wording_hits("a.html", "independently&#32;verified"))
        # A negation that only appears once markup is removed does not count.
        self.assertIn("negation supplied by markup", rc.wording_hits("a.html", "n<span>ot</span> independently verified"))
        self.assertTrue(rc.wording_hits("a.json", '{"k": "independently\\u0020verified"}'))

    def test_scan_paths_reads_files_trees_and_packages(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "notes.md").write_text("Clean notes.\n", encoding="utf-8")
            site = root / "out"
            (site / "docs").mkdir(parents=True)
            (site / "docs" / "index.html").write_text("<p>ok</p>", encoding="utf-8")
            (site / "app.js").write_text("independently verified", encoding="utf-8")  # not a text suffix
            pkg = nupkg(root / "p.nupkg", {"p.nuspec": b"<description>fine</description>", "README.md": b"fine"})
            self.assertEqual([], rc.scan_paths([root / "notes.md", site, pkg]))
            (site / "docs" / "index.html").write_text("<p>independently adjudicated</p>", encoding="utf-8")
            bad = nupkg(root / "q.nupkg", {"q.nuspec": b"<description>independently verified</description>"})
            problems = rc.scan_paths([root / "notes.md", site, bad, root / "missing.md"])
            self.assertEqual(3, len(problems), problems)

    def test_cli_exit_codes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            good, bad = Path(tmp) / "good.md", Path(tmp) / "bad.md"
            good.write_text("fine\n", encoding="utf-8")
            bad.write_text("independently verified\n", encoding="utf-8")
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, rc.main(["wording", str(good)]))
                self.assertEqual(1, rc.main(["wording", str(good), str(bad)]))


class ChangelogTests(unittest.TestCase):
    def test_section_is_matched_literally(self) -> None:
        text = "# Changelog\r\n\r\n## [Unreleased]\n\n## [0.26.0] - x\n\nA.\n\n## [0.25.0]\n\nB.\n## [0x26y0]\n"
        self.assertEqual(1, rc.version_headers(text, "0.26.0"))
        self.assertEqual("## [0.26.0] - x\n\nA.\n", rc.changelog_section(text, "0.26.0"))
        self.assertEqual(0, rc.version_headers(text, "0.27.0"))

    def test_the_renderer_and_the_gate_share_these_functions(self) -> None:
        import render_release_notes
        import verify_release_adjudication as gate
        self.assertIs(render_release_notes.changelog_section, rc.changelog_section)
        self.assertIs(gate.changelog_section, rc.changelog_section)
        self.assertIs(gate.version_headers, rc.version_headers)


class PreflightTests(unittest.TestCase):
    def repo(self, version: str = VERSION, changelog: str | None = None) -> Path:
        root = Path(self.tmp.name) / "repo"
        root.mkdir()
        (root / "Directory.Build.props").write_text(f"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>\n")
        (root / "CHANGELOG.md").write_text(changelog if changelog is not None else f"# Changelog\n\n## [{VERSION}] - d\n\nNotes.\n")
        for args in (["init", "-q"], ["add", "-A"], ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "c"]):
            subprocess.run(["git", "-C", str(root), *args], check=True, capture_output=True)
        return root

    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def test_matching_version_passes_with_outputs(self) -> None:
        root = self.repo()
        problems, outputs = rc.preflight(root, VERSION)
        self.assertEqual([], problems)
        self.assertEqual({"version": VERSION, "tag": f"v{VERSION}", "prerelease": "true"}, {k: outputs[k] for k in ("version", "tag", "prerelease")})
        self.assertEqual(rc.git(root, "rev-parse", "HEAD"), outputs["candidate"])
        self.assertEqual("false", rc.preflight(self.repo_at("1.0.0"), "1.0.0")[1]["prerelease"])

    def repo_at(self, version: str) -> Path:
        self.tmp.cleanup()
        self.tmp = tempfile.TemporaryDirectory()
        return self.repo(version, f"## [{version}]\n\nx\n")

    def test_each_mismatch_fails(self) -> None:
        root = self.repo()
        self.assertEqual(2, len(rc.preflight(root, "0.26.1")[0]))  # props differ, and no section
        self.assertTrue(rc.preflight(root, "v0.26.0")[0])
        self.assertTrue(rc.preflight(root, "0.26.0 ")[0])
        for changelog in ("# Changelog\n", f"## [{VERSION}]\n\n## [{VERSION}]\nx\n", f"## [{VERSION}]\n\n\n## [0.25.0]\nx\n"):
            self.tmp.cleanup()
            self.tmp = tempfile.TemporaryDirectory()
            self.assertEqual(1, len(rc.preflight(self.repo(changelog=changelog), VERSION)[0]), changelog)

    def test_a_tag_elsewhere_fails_and_a_tag_here_passes(self) -> None:
        root = self.repo()
        subprocess.run(["git", "-C", str(root), "tag", f"v{VERSION}"], check=True)
        self.assertEqual([], rc.preflight(root, VERSION)[0])
        (root / "x").write_text("x")
        for args in (["add", "-A"], ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "d"]):
            subprocess.run(["git", "-C", str(root), *args], check=True, capture_output=True)
        problems = rc.preflight(root, VERSION)[0]
        self.assertEqual(1, len(problems))
        self.assertTrue(problems[0].startswith(rc.ALREADY_RELEASED))
        with redirect_stdout(io.StringIO()):
            self.assertEqual(1, rc.main(["preflight", "--repo", str(root), "--version", VERSION, "--github-output", ""]))
            self.assertEqual(0, rc.main(["preflight", "--repo", str(root), "--version", VERSION, "--github-output", "", "--dry-run", "true"]))


if __name__ == "__main__":
    unittest.main()
