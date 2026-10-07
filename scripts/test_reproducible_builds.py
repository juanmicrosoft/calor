#!/usr/bin/env python3
"""Guards for reproducible publication builds (packages and website).

The release gate (#1410) rebuilds packages, release metadata and the website and
compares SHA-256 hashes with the adjudicated ones, so every source of build
nondeterminism found by 0.24 C2 (#1424) and by this fix's own proof must stay fixed:

  * .nupkg zip entries carried the wall-clock pack time  -> DeterministicTimestamp
  * DLLs/PDBs embedded the absolute checkout path          -> DeterministicSourcePaths
  * Next.js picked a random build id per build             -> generateBuildId
  * webpack module ids hashed absolute loader paths        -> PathIndependentModuleIdsPlugin
  * app entry chunks named by [chunkhash] of path inputs   -> [contenthash] + realContentHash
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

STAMP = (2000, 1, 1, 0, 0, 0)


def props() -> ET.Element:
    return ET.parse(REPO_ROOT / "Directory.Build.props").getroot()


def node() -> str:
    found = shutil.which("node")
    if found is None:
        raise AssertionError("node is required to evaluate the website configuration")
    return found


def run_node(script: str) -> object:
    return json.loads(subprocess.check_output([node(), "-e", script], cwd=REPO_ROOT / "website", text=True))


class DotnetConfigTests(unittest.TestCase):
    def group(self, label: str) -> ET.Element:
        found = [g for g in props().iter("PropertyGroup") if g.get("Label") == label]
        self.assertEqual(1, len(found), f"one PropertyGroup labelled {label!r}")
        return found[0]

    def test_compiler_and_pack_are_deterministic(self) -> None:
        group = self.group("Reproducible builds")
        self.assertIsNone(group.get("Condition"))
        self.assertEqual("true", group.findtext("Deterministic"))
        stamp = group.find("DeterministicTimestamp")
        self.assertIsNotNone(stamp)
        # A constant with no $(...) expansion and no condition: MSBuild imports environment
        # variables as properties, so a condition on emptiness would let an ambient
        # DeterministicTimestamp through. Only a -p: global property can override it.
        self.assertRegex(stamp.text or "", r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        self.assertIsNone(stamp.get("Condition"))

    def test_shipped_release_builds_map_source_paths(self) -> None:
        mapped = [g for g in props().iter("PropertyGroup") if g.findtext("DeterministicSourcePaths") == "true"]
        self.assertEqual(1, len(mapped))
        condition = mapped[0].get("Condition") or ""
        self.assertIn("'$(Configuration)' == 'Release'", condition)
        self.assertIn("src/", condition)
        roots = list(props().iter("SourceRoot"))
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
    def test_build_id_is_fixed_per_commit(self) -> None:
        config = (REPO_ROOT / "website/next.config.js").read_text(encoding="utf-8")
        self.assertIn("generateBuildId", config)
        for forbidden in ("Math.random", "Date.now", "new Date", "randomUUID", "nanoid", "execSync("):
            self.assertNotIn(forbidden, config)
        out = run_node("require('./next.config.js').generateBuildId()"
                       ".then(id => process.stdout.write(JSON.stringify(id)))")
        version = json.loads((REPO_ROOT / "website/package.json").read_text(encoding="utf-8"))["version"]
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, text=True).strip()
        # Fixed for one commit; different for every deployed commit.
        self.assertEqual(f"calor-{version}-{head[:12]}", out)

    def test_client_bundles_do_not_depend_on_the_checkout_path(self) -> None:
        config = (REPO_ROOT / "website/next.config.js").read_text(encoding="utf-8")
        self.assertIn("new PathIndependentModuleIdsPlugin(path.resolve(__dirname, '..'))", config)
        self.assertIn(".replace('[chunkhash]', '[contenthash]')", config)
        self.assertIn("config.optimization.realContentHash = true", config)

    def test_module_ids_are_the_same_from_any_checkout(self) -> None:
        """Run the plugin's moduleIds hook on modules named the way Next 14 names them:
        loader options JSON-stringified, then URL-encoded into the loader query."""
        script = r"""
const { PathIndependentModuleIdsPlugin } = require('./reproducible-module-ids');
function ids(root, sep) {
  const p = rel => root + rel.split('/').join(sep);
  const q = rel => 'next-flight-client-entry-loader.js?modules=' +
    encodeURIComponent(JSON.stringify({ request: p(rel), ids: [] })) + '&server=false!';
  const identifiers = [q('/website/src/app/layout.tsx'), q('/website/src/app/page.tsx'),
    p('/website/node_modules/next/dist/client/app-index.js'), 'external "react"'];
  const modules = identifiers.map(id => ({ needId: true, identifier: () => id }));
  const assigned = new Map();
  const chunkGraph = {
    getModuleId: m => (assigned.has(m) ? assigned.get(m) : null),
    setModuleId: (m, id) => assigned.set(m, id),
    getNumberOfModuleChunks: () => 1,
  };
  let hook;
  const compilation = { chunkGraph, modules: new Set(modules),
    hooks: { moduleIds: { tap: (_, fn) => { hook = fn; } } } };
  const compiler = { hooks: { compilation: { tap: (_, fn) => fn(compilation) } } };
  new PathIndependentModuleIdsPlugin(root).apply(compiler);
  hook(modules);
  return modules.map(m => assigned.get(m));
}
process.stdout.write(JSON.stringify([ids('/tmp/check"out/calor', '/'),
  ids('/tmp/other/deeper/calor', '/'), ids('/app', '/'), ids('/website', '/'), ids('/src', '/'),
  ids('C:\\work\\calor', '\\'), ids('D:\\a\\b c\\calor', '\\')]));
"""
        a, b, app, website, src, win_a, win_b = run_node(script)
        self.assertEqual(a, b)
        # Roots that also occur inside repository-relative paths (src/app, /website/...).
        self.assertEqual(a, app)
        self.assertEqual(a, website)
        self.assertEqual(a, src)
        # Windows roots: backslashes, JSON-escaped as \\ inside the URL-encoded loader query.
        self.assertEqual(win_a, win_b)
        self.assertEqual(4, len(set(a)), "distinct modules get distinct ids")

    def test_only_a_leading_root_is_replaced(self) -> None:
        script = r"""
const m = require('./reproducible-module-ids');
process.stdout.write(JSON.stringify([
  m.normalize('/app/website/src/app/layout.tsx', m.rootForms('/app')),
  m.normalize('loader.js?x=' + encodeURIComponent(JSON.stringify({ request: '/app/src/app' })) + '!/app/x',
              m.rootForms('/app')),
  m.normalize('/tmp/calor-other/x.js', m.rootForms('/tmp/calor')),
]));
"""
        plain, query, sibling = run_node(script)
        self.assertEqual("<root>/website/src/app/layout.tsx", plain)
        self.assertEqual('loader.js?x={"request":"<root>/src/app"}!<root>/x', query)
        self.assertEqual("/tmp/calor-other/x.js", sibling, "a sibling directory is not the root")

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
        self.assertIn("--targets packages", workflow)
        self.assertIn("--targets website", workflow)
        self.assertIn("python3 scripts/reproducible_builds.py clone", workflow)
        # Runs on every PR: publication inputs live all over the tree.
        trigger = workflow.split("\non:", 1)[1].split("\njobs:", 1)[0]
        self.assertNotIn("paths", trigger)
        # The second build varies the environment, including ambient timestamp variables.
        for needle in ('HOME="$home"', "NUGET_PACKAGES=", "npm_config_cache=", 'TZ="$SECOND_TZ"',
                       'LC_ALL="$SECOND_LOCALE"', "SOURCE_DATE_EPOCH=", "DeterministicTimestamp="):
            self.assertIn(needle, workflow)

    def test_build_script_runs_the_publish_job_pack_commands(self) -> None:
        publish = (REPO_ROOT / ".github/workflows/publish-nuget.yml").read_text(encoding="utf-8")
        script = (REPO_ROOT / "scripts/reproducible-build.sh").read_text(encoding="utf-8")
        for command in (
            "dotnet restore src/Calor.Compiler/Calor.Compiler.csproj --locked-mode",
            "dotnet build src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-restore",
            "dotnet pack src/Calor.Compiler/Calor.Compiler.csproj -c Release --no-build -o",
            "dotnet pack src/Calor.Sdk/Calor.Sdk.csproj -c Release -o",
            "/p:CalorSdkRequireAllRids=true",
            "bash .github/scripts/inspect-sdk-nupkg.sh",
            "python3 scripts/check-packaged-z3.py",
        ):
            with self.subTest(command=command):
                self.assertIn(command, publish)
                self.assertIn(command, script)


class CompareToolTests(unittest.TestCase):
    SCRIPT = "_next/static/chunks/app.js"

    def build(self, root: Path, stamp: tuple = STAMP, entry: bytes = b"dll", site: str = "<html>",
              drop: tuple = ()) -> Path:
        """A complete output: both packages with their essential entries, both metadata files,
        the required site files and the script index.html loads. `drop` removes entries."""
        (root / "nupkg").mkdir(parents=True)
        for prefix, required in rb.EXPECTED["packages"]["packages"].items():
            with zipfile.ZipFile(root / "nupkg" / f"{prefix}1.0.0.nupkg", "w") as archive:
                for name in ("lib/demo.dll", *required):
                    if name not in drop:
                        archive.writestr(zipfile.ZipInfo(name, stamp), entry)
        (root / "release-metadata").mkdir()
        for name in ("n.provenance.json", "n.sbom.spdx.json"):
            (root / "release-metadata" / name).write_text("{}", encoding="utf-8")
        (root / "website/_next/static/chunks").mkdir(parents=True)
        (root / "website/index.html").write_text(f'<script src="/calor/{self.SCRIPT}"></script>{site}', encoding="utf-8")
        for name in ("404.html", "sitemap.xml", "search-index.json", self.SCRIPT):
            if name not in drop:
                (root / "website" / name).write_text(name, encoding="utf-8")
        return root

    def hashes(self, **kwargs) -> dict:
        with tempfile.TemporaryDirectory() as tmp:
            return rb.hash_build(self.build(Path(tmp), **kwargs))

    def test_identical_complete_builds_compare_equal(self) -> None:
        a, b = self.hashes(), self.hashes()
        for target in ("all", "packages", "website"):
            with self.subTest(target=target):
                self.assertEqual([], rb.compare(a, b, target))
        self.assertEqual(5, a["website"]["fileCount"])

    def test_zip_timestamp_alone_is_a_difference(self) -> None:
        self.assertEqual([self.SCRIPT], self.hashes()["website"]["indexAssets"])
        problems = rb.compare(self.hashes(), self.hashes(stamp=(2026, 10, 7, 15, 19, 42)), "packages")
        self.assertTrue(any("nupkg/calor.1.0.0.nupkg!lib/demo.dll" in p for p in problems), problems)
        self.assertTrue(any(p.startswith("differs: nupkg/calor.1.0.0.nupkg:") for p in problems), problems)

    def test_content_and_website_differences_are_reported(self) -> None:
        problems = rb.compare(self.hashes(), self.hashes(entry=b"DLL", site="<html id=2>"), "all")
        self.assertTrue(any("website/<tree>" in p for p in problems), problems)
        self.assertTrue(any("website/index.html" in p for p in problems), problems)
        self.assertTrue(any("!lib/demo.dll" in p for p in problems), problems)

    def test_a_file_missing_on_one_side_fails(self) -> None:
        a = self.hashes()
        b = json.loads(json.dumps(a))
        del b["website"]["files"][self.SCRIPT]
        self.assertIn(f"only in A: website/{self.SCRIPT}", rb.compare(a, b, "website"))

    def test_identical_but_empty_or_partial_outputs_fail(self) -> None:
        empty = {"packages": {}, "releaseMetadata": {}, "website": None}
        for target in ("all", "packages", "website"):
            with self.subTest(target=target):
                self.assertTrue(rb.compare(empty, empty, target))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "website").mkdir()
            empty_site = rb.hash_build(root)
        self.assertEqual(0, empty_site["website"]["fileCount"])
        self.assertIn("A: the website output is missing or empty", rb.compare(empty_site, empty_site, "website"))
        hollow = self.hashes(drop=("Sdk/Sdk.props", "tools/net10.0/any/calor.dll", self.SCRIPT))
        problems = rb.compare(hollow, hollow, "all")
        for needle in ("Calor.Sdk.1.0.0.nupkg has no Sdk/Sdk.props", "calor.1.0.0.nupkg has no tools/net10.0/any/calor.dll",
                       f"index.html loads {self.SCRIPT}, which the output lacks"):
            self.assertTrue(any(needle in p for p in problems), (needle, problems))
        partial = self.hashes()
        del partial["packages"]["Calor.Sdk.1.0.0.nupkg"]
        del partial["releaseMetadata"]["n.provenance.json"]
        del partial["website"]["files"]["sitemap.xml"]
        problems = rb.compare(partial, partial, "all")
        for needle in ("Calor.Sdk.*.nupkg", "*.provenance.json", "no sitemap.xml"):
            self.assertTrue(any(needle in p for p in problems), (needle, problems))

    def test_a_target_compares_only_its_own_surface(self) -> None:
        both = self.hashes()
        packages_only = {k: v for k, v in both.items() if k != "website"}
        packages_only["website"] = None
        site_only = {"packages": {}, "releaseMetadata": {}, "website": both["website"]}
        self.assertEqual([], rb.compare(both, packages_only, "packages"))
        self.assertEqual([], rb.compare(both, site_only, "website"))
        self.assertTrue(rb.compare(both, packages_only, "all"))

    def test_toolchain_differences_are_reported_not_hidden(self) -> None:
        a = {"toolchain": {"dotnet": "10.0.401", "node": "v20.20.2"}}
        b = {"toolchain": {"dotnet": "10.0.402", "node": "v20.20.2"}}
        self.assertEqual(["toolchain differs: dotnet: 10.0.401 != 10.0.402"], rb.toolchain_notes(a, b))

    def test_tree_digest_is_the_release_gate_digest(self) -> None:
        import verify_release_adjudication as gate
        self.assertIs(gate.tree_digest, rb.tree_digest)


class Z3CopyTests(unittest.TestCase):
    def test_copy_takes_only_the_registered_pinned_assets(self) -> None:
        registry = json.loads((REPO_ROOT / "eng/z3-consumers.json").read_text(encoding="utf-8"))
        expected = [registry["managed"]["path"], *(e["path"] for e in registry["supportedRids"])]
        self.assertEqual(expected, rb.z3_asset_paths())
        verifier = (REPO_ROOT / "scripts/verify-z3-assets.py").read_text(encoding="utf-8")
        for path in expected:
            self.assertIn(f'"{path}"', verifier, "every copied asset is one the verifier pins")
        source = (REPO_ROOT / "scripts/reproducible_builds.py").read_text(encoding="utf-8")
        self.assertNotIn("copytree", source, "never copy whole ignored directories into a clone")

    def test_a_stray_file_next_to_the_assets_is_not_copied(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            fake_repo = Path(tmp) / "repo"
            for rel in rb.z3_asset_paths():
                (fake_repo / rel).parent.mkdir(parents=True, exist_ok=True)
                (fake_repo / rel).write_bytes(b"asset")
            (fake_repo / "src/Calor.Compiler/z3/Injected.cs").write_text("class X {}", encoding="utf-8")
            dest = Path(tmp) / "clone"
            rb.copy_z3_assets(fake_repo, dest)
            copied = sorted(p.relative_to(dest).as_posix() for p in dest.rglob("*") if p.is_file())
        self.assertEqual(sorted(rb.z3_asset_paths()), copied)


if __name__ == "__main__":
    unittest.main()
