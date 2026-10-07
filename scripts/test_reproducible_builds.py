#!/usr/bin/env python3
"""Guards for reproducible publication builds (packages and website).

The release gate (#1410) rebuilds packages, release metadata and the website and
compares SHA-256 hashes with the adjudicated ones, so every source of build
nondeterminism found by 0.24 C2 (#1424) must stay fixed:

  * .nupkg zip entries carried the wall-clock pack time  -> DeterministicTimestamp
  * DLLs/PDBs embedded the absolute checkout path          -> DeterministicSourcePaths
  * Next.js picked a random build id per build             -> generateBuildId
  * readdir order decided sitemap/search-index order       -> sorted directory walk

This file checks the configuration and the comparison tool without building
anything. The real two-build proof is .github/workflows/reproducible-builds.yml
(scripts/reproducible_builds.py); the recorded run is in
docs/plans/evidence/deterministic-builds/.
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
import reproducible_builds as rb  # noqa: E402


def props() -> ET.Element:
    return ET.parse(REPO_ROOT / "Directory.Build.props").getroot()


class DotnetConfigTests(unittest.TestCase):
    def group(self, label: str) -> ET.Element:
        found = [g for g in props().iter("PropertyGroup") if g.get("Label") == label]
        self.assertEqual(1, len(found), f"one PropertyGroup labelled {label!r}")
        return found[0]

    def test_compiler_and_pack_are_deterministic(self) -> None:
        group = self.group("Reproducible builds")
        self.assertEqual("true", group.findtext("Deterministic"))
        stamp = group.find("DeterministicTimestamp")
        self.assertIsNotNone(stamp)
        # A constant: no $(...) expansion, so neither the environment
        # (SOURCE_DATE_EPOCH) nor git can change it.
        self.assertRegex(stamp.text or "", r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        self.assertEqual("'$(DeterministicTimestamp)' == ''", stamp.get("Condition"))

    def test_shipped_release_builds_map_source_paths(self) -> None:
        mapped = [
            g for g in props().iter("PropertyGroup")
            if g.findtext("DeterministicSourcePaths") == "true"
        ]
        self.assertEqual(1, len(mapped))
        condition = mapped[0].get("Condition") or ""
        self.assertIn("'$(Configuration)' == 'Release'", condition)
        self.assertIn("src/", condition)
        roots = [i for i in props().iter("SourceRoot")]
        self.assertEqual(["$(CalorRepoRoot)"], [i.get("Include") for i in roots])

    def test_nothing_reenables_nondeterminism(self) -> None:
        offenders = []
        for path in [*REPO_ROOT.glob("src/**/*.csproj"), *REPO_ROOT.glob("src/**/*.props"),
                     *REPO_ROOT.glob("src/**/*.targets"), REPO_ROOT / "Directory.Build.targets"]:
            text = path.read_text(encoding="utf-8")
            for pattern in (r"<Deterministic>\s*false", r"<DeterministicSourcePaths>\s*false",
                            r"<DeterministicTimestamp>", r"<PathMap>"):
                if re.search(pattern, text, re.IGNORECASE):
                    offenders.append(f"{path.relative_to(REPO_ROOT)}: {pattern}")
        self.assertEqual([], offenders)


class WebsiteConfigTests(unittest.TestCase):
    def test_build_id_is_fixed_per_version(self) -> None:
        config = (REPO_ROOT / "website/next.config.js").read_text(encoding="utf-8")
        self.assertIn("generateBuildId", config)
        for forbidden in ("Math.random", "Date.now", "new Date", "randomUUID", "nanoid", "execSync"):
            self.assertNotIn(forbidden, config)
        node = shutil.which("node")
        if node is None:
            self.fail("node is required to evaluate website/next.config.js")
        out = subprocess.check_output(
            [node, "-e", "require('./next.config.js').generateBuildId().then(id => process.stdout.write(id))"],
            cwd=REPO_ROOT / "website", text=True,
        )
        version = json.loads((REPO_ROOT / "website/package.json").read_text(encoding="utf-8"))["version"]
        self.assertEqual(f"calor-{version}", out)

    def test_client_bundles_do_not_depend_on_the_checkout_path(self) -> None:
        config = (REPO_ROOT / "website/next.config.js").read_text(encoding="utf-8")
        self.assertIn("new PathIndependentModuleIdsPlugin(path.resolve(__dirname, '..'))", config)
        self.assertIn(".replace('[chunkhash]', '[contenthash]')", config)
        self.assertIn("config.optimization.realContentHash = true", config)
        node = shutil.which("node")
        if node is None:
            self.fail("node is required to evaluate website/reproducible-module-ids.js")
        script = (
            "const m = require('./reproducible-module-ids');"
            "const forms = m.rootForms('/work/a/calor');"
            "const id = 'next-flight-client-entry-loader.js?modules=%7B%22request%22%3A%22'"
            " + encodeURIComponent('/work/a/calor/website/src/app/x.tsx') + '%22%7D!';"
            "const other = id.split(encodeURIComponent('/work/a/calor')).join(encodeURIComponent('/elsewhere/b'));"
            "const forms2 = m.rootForms('/elsewhere/b');"
            "process.stdout.write(JSON.stringify([m.normalize(id, forms), m.normalize(other, forms2),"
            " m.normalize('/work/a/calor/website/node_modules/x.js', forms)]));"
        )
        a, b, plain = json.loads(subprocess.check_output([node, "-e", script], cwd=REPO_ROOT / "website", text=True))
        self.assertEqual(a, b)
        self.assertNotIn("work", a)
        self.assertEqual("<root>/website/node_modules/x.js", plain)

    def test_docs_walk_is_sorted(self) -> None:
        source = (REPO_ROOT / "website/src/lib/docs.ts").read_text(encoding="utf-8")
        walk = source.split("function getAllDocFiles", 1)[1].split("\n}\n", 1)[0]
        self.assertRegex(walk, r"readdirSync\(dir, \{ withFileTypes: true \}\)\s*\.sort\(")
        self.assertNotIn("localeCompare", walk)


class WorkflowTests(unittest.TestCase):
    def test_ci_builds_twice_and_compares(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/reproducible-builds.yml").read_text(encoding="utf-8")
        self.assertEqual(4, workflow.count("bash scripts/reproducible-build.sh"))
        self.assertEqual(2, workflow.count("python3 scripts/reproducible_builds.py compare"))
        self.assertIn("python3 scripts/reproducible_builds.py clone", workflow)

    def test_build_script_runs_the_publish_job_pack_commands(self) -> None:
        publish = (REPO_ROOT / ".github/workflows/publish-nuget.yml").read_text(encoding="utf-8")
        script = (REPO_ROOT / "scripts/reproducible-build.sh").read_text(encoding="utf-8")
        for command in (
            "dotnet restore src/Calor.Compiler/Calor.Compiler.csproj --locked-mode",
            "dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-restore",
            "dotnet pack src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-build -o",
            "dotnet pack src/Calor.Sdk/Calor.Sdk.csproj -c Release -o",
            "/p:CalorSdkRequireAllRids=true",
        ):
            with self.subTest(command=command):
                self.assertIn(command, publish)
                self.assertIn(command, script)


class CompareToolTests(unittest.TestCase):
    def build(self, root: Path, stamp: tuple, entry: bytes, site: str) -> Path:
        (root / "nupkg").mkdir(parents=True)
        with zipfile.ZipFile(root / "nupkg/demo.1.0.0.nupkg", "w") as archive:
            archive.writestr(zipfile.ZipInfo("lib/demo.dll", stamp), entry)
        (root / "release-metadata").mkdir()
        (root / "release-metadata/demo.sbom.spdx.json").write_text("{}", encoding="utf-8")
        (root / "website/_next").mkdir(parents=True)
        (root / "website/index.html").write_text(site, encoding="utf-8")
        (root / "website/_next/app.js").write_text("x", encoding="utf-8")
        return root

    def hashes(self, *args) -> dict:
        with tempfile.TemporaryDirectory() as tmp:
            return rb.hash_build(self.build(Path(tmp), *args))

    def test_identical_builds_compare_equal(self) -> None:
        a = self.hashes((2000, 1, 1, 0, 0, 0), b"dll", "<html>")
        b = self.hashes((2000, 1, 1, 0, 0, 0), b"dll", "<html>")
        self.assertEqual([], rb.compare(a, b))
        self.assertEqual(2, a["website"]["fileCount"])

    def test_zip_timestamp_alone_is_a_difference(self) -> None:
        a = self.hashes((2000, 1, 1, 0, 0, 0), b"dll", "<html>")
        b = self.hashes((2026, 10, 7, 15, 19, 42), b"dll", "<html>")
        problems = rb.compare(a, b)
        self.assertTrue(any("nupkg/demo.1.0.0.nupkg!lib/demo.dll" in p for p in problems), problems)
        self.assertTrue(any(p.startswith("differs: nupkg/demo.1.0.0.nupkg:") for p in problems), problems)

    def test_content_and_website_differences_are_reported(self) -> None:
        a = self.hashes((2000, 1, 1, 0, 0, 0), b"dll", "<html>")
        b = self.hashes((2000, 1, 1, 0, 0, 0), b"DLL", "<html id=2>")
        problems = rb.compare(a, b)
        self.assertTrue(any("website/<tree>" in p for p in problems), problems)
        self.assertTrue(any("website/index.html" in p for p in problems), problems)
        self.assertTrue(any("!lib/demo.dll" in p for p in problems), problems)

    def test_missing_files_and_empty_builds_fail(self) -> None:
        a = self.hashes((2000, 1, 1, 0, 0, 0), b"dll", "<html>")
        b = json.loads(json.dumps(a))
        del b["website"]["files"]["_next/app.js"]
        self.assertIn("only in A: website/_next/app.js", rb.compare(a, b))
        empty = {"packages": {}, "releaseMetadata": {}, "website": None}
        self.assertEqual(["nothing was hashed in either build"], rb.compare(empty, empty))

    def test_tree_digest_is_the_release_gate_digest(self) -> None:
        import verify_release_adjudication as gate
        self.assertIs(gate.tree_digest, rb.tree_digest)


if __name__ == "__main__":
    unittest.main()
