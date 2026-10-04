#!/usr/bin/env python3
"""0.24 G1 (#1420): bootstrapped Z3 assets are hermetic for every consumer.

Run without arguments for the repository guards (test.yml runs this next to
test_supply_chain.py). They check eng/z3-consumers.json against:

  * the pins and scripts/verify-z3-assets.py (one RID set, one asset set);
  * Calor.Compiler.csproj (Z3 output items exist only at execution time, after
    ValidateZ3Assets; no evaluation-time glob, no AfterTargets="Build" copy);
  * every workflow (Z3 is seeded only through .github/actions/bootstrap-z3,
    before the first dotnet command of every job that builds or probes);
  * the registered RID consumer matrix and negative-control steps;
  * every test project in eng/test-manifest.json (each links the guard).

`--check-trx <file> --min-passed N` checks a test run that must include every
Z3ConsumerGuardTests fact, with nothing skipped and nothing failed.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


REPO_ROOT = Path(__file__).resolve().parent.parent
REGISTRY_PATH = REPO_ROOT / "eng/z3-consumers.json"
ACTION_USE = "uses: ./.github/actions/bootstrap-z3"
GUARD_FACTS = (
    "HostRidIsASupportedZ3Rid",
    "Z3LoadsWithThePinnedVersion",
    "HostNativeInOutputMatchesItsPin",
    "ManagedWrapperInOutputMatchesItsPin",
)
# GitHub-hosted runner label -> the RID its default .NET SDK reports.
RUNNER_RIDS = {
    "ubuntu-latest": "linux-x64",
    "ubuntu-24.04-arm": "linux-arm64",
    "macos-14": "osx-arm64",
    "windows-latest": "win-x64",
    "windows-11-arm": "win-arm64",
}
DOTNET_COMMAND = re.compile(r"\bdotnet (?:build|test|run|pack|restore|publish|msbuild)\b")
CONSUMER_PROBE = re.compile(r"test-sdk-package\.sh|test-cli-tool-consumer\.sh")
Z3_SOURCE_DIRS = ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes")


def load_registry() -> dict:
    return json.loads(REGISTRY_PATH.read_text(encoding="utf-8"))


def read_pins(path: Path) -> dict[str, tuple[str, int]]:
    pins: dict[str, tuple[str, int]] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#"):
            digest, name, size = line.split()
            pins[name] = (digest.lower(), int(size))
    return pins


def uncommented(text: str) -> str:
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))


def workflow_jobs(text: str) -> dict[str, str]:
    body = text.split("\njobs:\n", 1)[1]
    parts = re.split(r"(?m)^  ([A-Za-z0-9_-]+):[ \t]*$", body)
    return {parts[i]: parts[i + 1] for i in range(1, len(parts) - 1, 2)}


def job_steps(job: str) -> list[str]:
    return re.split(r"(?m)^      - ", job)[1:]


def step_name(step: str) -> str:
    match = re.match(r"name: (.+)", step)
    return match.group(1).strip() if match else ""


def matrix_pairs(job: str) -> list[tuple[str, str]]:
    return re.findall(r"(?m)^ +- runner: (\S+)\n +rid: (\S+)$", job)


def divergences(rid: str, project: str) -> list[str]:
    entry = load_registry()["testHosts"]["platformDivergences"]
    if rid not in entry["rids"] or project != entry["project"]:
        return []
    return [test["name"] for test in entry["tests"]]


def divergence_filter(rid: str, project: str) -> str:
    return "&".join(f"FullyQualifiedName!={name}" for name in divergences(rid, project))


def check_guard_trx(
    trx: Path, min_passed: int, exact: bool = False, guard: bool = True, required: tuple[str, ...] = ()
) -> list[str]:
    root = ET.parse(trx).getroot()
    results = root.findall("./{*}Results/{*}UnitTestResult")
    errors: list[str] = []
    passed = 0
    for result in results:
        outcome = result.attrib.get("outcome")
        name = result.attrib.get("testName", "?")
        if outcome != "Passed":
            errors.append(f"{name}: {outcome} (Z3 consumer runs allow no skip or failure)")
        else:
            passed += 1
    if passed < min_passed:
        errors.append(f"only {passed} passed results; expected at least {min_passed}")
    if exact and len(results) != min_passed:
        errors.append(f"{len(results)} results; expected exactly {min_passed}")
    names = [result.attrib.get("testName", "") for result in results]
    for fact in GUARD_FACTS if guard else ():
        if not any(f"Z3ConsumerGuardTests.{fact}" in name for name in names):
            errors.append(f"Z3ConsumerGuardTests.{fact} did not run")
    for name in required:
        if name not in names:
            errors.append(f"{name} did not run")
    return errors


class Z3HermeticTests(unittest.TestCase):
    registry = load_registry()

    def supported(self) -> dict[str, dict]:
        return {entry["rid"]: entry for entry in self.registry["supportedRids"]}

    def workflows(self) -> dict[str, str]:
        return {
            str(path.relative_to(REPO_ROOT)): path.read_text(encoding="utf-8")
            for path in sorted((REPO_ROOT / ".github/workflows").glob("*.yml"))
        }

    def test_registry_matches_pins_and_asset_verifier(self) -> None:
        version = self.registry["z3Version"]
        assets = read_pins(REPO_ROOT / self.registry["pins"]["assets"])
        archives = read_pins(REPO_ROOT / self.registry["pins"]["archives"])
        self.assertIn(version, self.registry["pins"]["assets"])
        self.assertIn(version, self.registry["pins"]["archives"])

        expected_paths = {self.registry["managed"]["asset"]: self.registry["managed"]["path"]}
        for rid, entry in self.supported().items():
            expected_paths[entry["asset"]] = entry["path"]
            self.assertEqual(f"src/Calor.Compiler/runtimes/{rid}/native/{entry['native']}", entry["path"])
            self.assertIn(entry["archive"], archives, rid)
        self.assertIn(self.registry["managed"]["archive"], archives)
        self.assertTrue(set(expected_paths) <= set(assets), "every consumed asset is pinned")

        spec = importlib.util.spec_from_file_location("verify_z3_assets", REPO_ROOT / "scripts/verify-z3-assets.py")
        module = importlib.util.module_from_spec(spec)
        assert spec.loader is not None
        spec.loader.exec_module(module)
        self.assertEqual(expected_paths, module.ASSET_PATHS)

        unsupported = {entry["rid"] for entry in self.registry["unsupportedRids"]}
        self.assertFalse(unsupported & set(self.supported()))
        self.assertTrue(all(entry["reason"] for entry in self.registry["unsupportedRids"]))

    def test_compiler_project_creates_z3_items_only_at_execution_time(self) -> None:
        project = ET.parse(REPO_ROOT / "src/Calor.Compiler/Calor.Compiler.csproj").getroot()
        for group in project.findall("ItemGroup"):
            for item in group:
                include = item.attrib.get("Include", "")
                if item.tag == "Reference" and include == "Microsoft.Z3":
                    continue
                self.assertNotRegex(
                    include, r"(?i)runtimes|libz3|z3",
                    f"evaluation-time Z3 item <{item.tag} Include={include!r}>",
                )

        targets = {target.attrib["Name"]: target for target in project.findall("Target")}
        validate = targets["ValidateZ3Assets"]
        self.assertEqual("ResolveReferences", validate.attrib.get("BeforeTargets"))
        add = targets["AddZ3AssetsToOutput"]
        self.assertEqual("AssignTargetPaths", add.attrib.get("BeforeTargets"))
        self.assertIn("ValidateZ3Assets", add.attrib.get("DependsOnTargets", ""))

        links = {}
        for item in add.iter("None"):
            links[item.attrib["Link"].replace("\\", "/")] = item.attrib
            self.assertEqual("Always", item.attrib.get("CopyToOutputDirectory"))
        expected = {
            f"runtimes/{rid}/native/{entry['native']}" for rid, entry in self.supported().items()
        }
        self.assertEqual(expected, {link for link in links if link.startswith("runtimes/")})
        root_copies = {link: attrs for link, attrs in links.items() if "/" not in link}
        self.assertEqual({"$(_CalorZ3HostLib)"}, set(root_copies))
        self.assertEqual("Never", root_copies["$(_CalorZ3HostLib)"].get("CopyToPublishDirectory"))
        host_rids = {element.text for element in add.iter("_CalorZ3HostRid")}
        self.assertEqual(set(self.supported()), host_rids)

        for name, target in targets.items():
            text = ET.tostring(target, encoding="unicode")
            for forbidden in ("DownloadZ3", "curl", "Invoke-WebRequest", "http://", "https://"):
                self.assertNotIn(forbidden, text, name)
            if "Build" in target.attrib.get("AfterTargets", "").split(";"):
                self.assertNotRegex(text, r"(?i)z3", f"{name} is a post-build Z3 side copy")

    def test_tasks_project_has_no_z3_side_copy(self) -> None:
        text = (REPO_ROOT / "src/Calor.Tasks/Calor.Tasks.csproj").read_text(encoding="utf-8")
        code = re.sub(r"<!--.*?-->", "", text, flags=re.S)
        self.assertNotRegex(code, r"(?i)libz3|runtimes|z3")

    def test_every_registered_test_project_links_the_guard(self) -> None:
        manifest = json.loads((REPO_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(13, len(manifest["projects"]))
        link = '<Compile Include="..\\Shared\\Z3ConsumerGuardTests.cs" Link="Shared\\Z3ConsumerGuardTests.cs" />'
        for project in manifest["projects"]:
            with self.subTest(project=project["path"]):
                self.assertIn(link, (REPO_ROOT / project["path"]).read_text(encoding="utf-8"))

        guard = (REPO_ROOT / self.registry["testHosts"]["guard"]).read_text(encoding="utf-8")
        code = "\n".join(line for line in guard.splitlines() if not line.lstrip().startswith("//"))
        self.assertNotRegex(code, r"Skip\.|Skippable")
        self.assertEqual(len(GUARD_FACTS), guard.count("[Fact]"))
        for fact in GUARD_FACTS:
            self.assertIn(f"public void {fact}()", guard)
        self.assertIn("eng\", \"z3-consumers.json", guard)

    def test_every_z3_consuming_project_is_registered(self) -> None:
        excluded = tuple(self.registry["projects"]["excludedTrees"])
        projects = {
            path.relative_to(REPO_ROOT).as_posix(): path
            for path in REPO_ROOT.rglob("*.csproj")
            if not path.relative_to(REPO_ROOT).as_posix().startswith(excluded)
            and "/obj/" not in path.as_posix() and "/bin/" not in path.as_posix()
        }
        references: dict[str, set[str]] = {}
        for relative, path in projects.items():
            refs = re.findall(r'<ProjectReference\s+Include="([^"]+)"', path.read_text(encoding="utf-8"))
            references[relative] = {
                (path.parent / ref.replace("\\", "/")).resolve().relative_to(REPO_ROOT).as_posix()
                for ref in refs
            }
        compiler = "src/Calor.Compiler/Calor.Compiler.csproj"
        consumers = {compiler}
        changed = True
        while changed:
            changed = False
            for project, refs in references.items():
                if project not in consumers and refs & consumers:
                    consumers.add(project)
                    changed = True
        manifest = json.loads((REPO_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        registered = {entry["path"] for entry in self.registry["projects"]["entries"]}
        registered |= {project["path"] for project in manifest["projects"]}
        self.assertEqual(consumers, registered)

        performance = next(p for p in manifest["projects"] if "Calor.Performance.Tests" in p["path"])
        baseline = json.loads((REPO_ROOT / "eng/performance-baselines.json").read_text(encoding="utf-8"))
        self.assertEqual(performance["expectedTotal"], baseline["expectedTestCount"])

    def test_workflows_seed_z3_only_through_the_owned_action(self) -> None:
        action = (REPO_ROOT / self.registry["bootstrap"]["action"]).read_text(encoding="utf-8")
        for script in self.registry["bootstrap"]["scripts"]:
            self.assertIn(script, action)

        monitors = {entry["workflow"] for entry in self.registry["producersAndMonitors"]}
        controls = {
            (entry["workflow"], entry["job"], entry["step"]) for entry in self.registry["negativeControls"]
        }
        for workflow, text in self.workflows().items():
            if workflow in monitors:
                code = uncommented(text)
                self.assertIsNone(DOTNET_COMMAND.search(code), workflow)
                for source in Z3_SOURCE_DIRS:
                    self.assertNotIn(source, code, workflow)
                continue
            for job_name, job in workflow_jobs(text).items():
                with self.subTest(workflow=workflow, job=job_name):
                    for step in job_steps(job):
                        if (workflow, job_name, step_name(step)) in controls:
                            continue
                        for forbidden in ("download-z3", "z3-binaries", "Z3Prover/z3", "libz3", *Z3_SOURCE_DIRS):
                            self.assertNotIn(forbidden, uncommented(step), step_name(step))
                    steps = [uncommented(step) for step in job_steps(job)]
                    uses = [i for i, step in enumerate(steps) if ACTION_USE in step]
                    first_dotnet = next(
                        (i for i, step in enumerate(steps)
                         if DOTNET_COMMAND.search(step) or CONSUMER_PROBE.search(step)),
                        None,
                    )
                    if first_dotnet is None:
                        self.assertEqual([], uses, "bootstrap in a job that builds nothing")
                        continue
                    self.assertEqual(1, len(uses), "exactly one owned bootstrap per building job")
                    self.assertLess(uses[0], first_dotnet, "bootstrap must precede the first dotnet command")

    def test_registered_matrix_covers_every_supported_rid(self) -> None:
        workflows = self.workflows()
        supported = set(self.supported())
        test_host_rids = set()
        for cell in self.registry["matrix"]:
            with self.subTest(cell=cell):
                job = workflow_jobs(workflows[cell["workflow"]])[cell["job"]]
                self.assertIn(ACTION_USE, job)
                pairs = matrix_pairs(job)
                for runner, rid in pairs:
                    self.assertEqual(RUNNER_RIDS[runner], rid, f"runner {runner} is not {rid}")
                rids = {rid for _, rid in pairs}
                if cell["rid"] == "*":
                    self.assertEqual(supported, rids)
                elif pairs:
                    self.assertIn(cell["rid"], rids)
                else:
                    self.assertIn("runs-on: ubuntu-latest", job)
                    self.assertEqual("linux-x64", cell["rid"])
                if cell["consumer"] == "test-hosts":
                    test_host_rids.add(cell["rid"])
                    if pairs:
                        variable = self.registry["testHosts"]["expectedRidVariable"]
                        self.assertIn(f"{variable}: ${{{{ matrix.rid }}}}", job)
                        self.assertIn("Z3ConsumerGuardTests", job)
                if cell["consumer"] in ("sdk-package", "cli-tool-package"):
                    self.assertRegex(job, r"(?:test-sdk-package|test-cli-tool-consumer)\.sh")
        self.assertEqual(supported, test_host_rids)
        consumers = {cell["consumer"] for cell in self.registry["matrix"]}
        self.assertEqual({"test-hosts", "sdk-package", "cli-tool-package"}, consumers)

    def test_platform_divergences_are_narrow_and_enforced(self) -> None:
        entry = self.registry["testHosts"]["platformDivergences"]
        self.assertTrue(set(entry["rids"]) <= set(self.supported()))
        self.assertLessEqual(len(entry["tests"]), 3)
        self.assertTrue(all(test["observed"] for test in entry["tests"]))
        for test in entry["tests"]:
            self.assertNotIn("Z3ConsumerGuardTests", test["name"])
        source = "\n".join(p.read_text(encoding="utf-8") for p in (REPO_ROOT / "tests/Calor.Verification.Tests").rglob("*.cs"))
        for test in entry["tests"]:
            self.assertIn(f"void {test['name'].rsplit('.', 1)[1]}(", source)
        job = workflow_jobs((REPO_ROOT / ".github/workflows/test.yml").read_text(encoding="utf-8"))["z3-consumer-matrix"]
        self.assertIn("--divergence-filter", job)
        self.assertEqual(3, job.count("--list-test-projects"), "build, guard run, and guard check cover every manifest project")
        self.assertIn('--exact 4', job)
        self.assertIn("--require Calor.Tasks.Tests.CompileCalorIntegrationTests.VerifyGate_NativeZ3_DeployedToTasksOutputRoot", job)
        self.assertIn(f"--check-trx artifacts/z3/verification.trx --project {entry['project']}", job)
        self.assertEqual("", divergence_filter("linux-arm64", entry["project"]))
        # #1135 fixed the three G1 divergences; none is deselected, and each stays recorded.
        self.assertEqual("", divergence_filter("win-x64", entry["project"]))
        self.assertEqual(3, len(entry["resolved"]))
        for test in entry["resolved"]:
            self.assertIn(f"void {test['name'].rsplit('.', 1)[1]}(", source)
            self.assertTrue(test["observed"] and test["cause"])

    def test_negative_controls_are_registered_steps(self) -> None:
        workflows = self.workflows()
        for control in self.registry["negativeControls"]:
            job = workflow_jobs(workflows[control["workflow"]])[control["job"]]
            self.assertIn(control["step"], [step_name(step) for step in job_steps(job)])

    def test_publish_packs_the_bootstrapped_assets(self) -> None:
        job = workflow_jobs((REPO_ROOT / ".github/workflows/publish-nuget.yml").read_text(encoding="utf-8"))["publish"]
        self.assertIn(ACTION_USE, job)
        self.assertIn("check-packaged-z3.py ./nupkg/calor.*.nupkg --prefix tools/net10.0/any --all-rids", job)
        self.assertIn("check-packaged-z3.py ./nupkg/Calor.Sdk.*.nupkg --prefix tasks/net10.0 --all-rids", job)
        for script, prefix in (
            (".github/scripts/test-sdk-package.sh", "tasks/net10.0"),
            (".github/scripts/test-cli-tool-consumer.sh", "tools/net10.0/any"),
        ):
            text = (REPO_ROOT / script).read_text(encoding="utf-8")
            self.assertIn("scripts/check-packaged-z3.py", text)
            self.assertIn(prefix, text)

    def test_packaged_z3_checker_fails_closed(self) -> None:
        checker = REPO_ROOT / "scripts/check-packaged-z3.py"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "eng").mkdir()
            shutil.copy(REGISTRY_PATH, root / "eng/z3-consumers.json")
            contents = {self.registry["managed"]["asset"]: b"managed"}
            for rid, entry in self.supported().items():
                contents[entry["asset"]] = f"native-{rid}".encode()
            pin_path = root / self.registry["pins"]["assets"]
            pin_path.parent.mkdir(parents=True)
            pin_path.write_text(
                "".join(f"{hashlib.sha256(data).hexdigest()}  {name}  {len(data)}\n" for name, data in contents.items()),
                encoding="utf-8",
            )

            def package(mutate=None) -> int:
                entries = {"pkg/Microsoft.Z3.dll": contents[self.registry["managed"]["asset"]]}
                for rid, entry in self.supported().items():
                    entries[f"pkg/runtimes/{rid}/native/{entry['native']}"] = contents[entry["asset"]]
                if mutate:
                    mutate(entries)
                nupkg = root / "test.nupkg"
                with zipfile.ZipFile(nupkg, "w") as archive:
                    for name, data in entries.items():
                        archive.writestr(name, data)
                return subprocess.run(
                    [sys.executable, str(checker), str(nupkg), "--prefix", "pkg", "--all-rids",
                     "--repo-root", str(root)],
                    capture_output=True, text=True,
                ).returncode

            self.assertEqual(0, package())
            mutations = {
                "wrong bytes": lambda e: e.__setitem__("pkg/runtimes/linux-arm64/native/libz3.so", b"x"),
                "missing rid": lambda e: e.pop("pkg/runtimes/win-arm64/native/libz3.dll"),
                "missing wrapper": lambda e: e.pop("pkg/Microsoft.Z3.dll"),
                "root copy": lambda e: e.__setitem__("pkg/libz3.so", contents["libz3-linux-x64.so"]),
                "unsupported rid": lambda e: e.__setitem__("pkg/runtimes/osx-x64/native/libz3.dylib", b"x"),
            }
            for label, mutate in mutations.items():
                with self.subTest(mutation=label):
                    self.assertNotEqual(0, package(mutate))

    def test_guard_trx_check_rejects_skips_and_missing_facts(self) -> None:
        def trx(results: list[tuple[str, str]]) -> Path:
            rows = "".join(
                f'<UnitTestResult testName="Calor.Tests.Shared.Z3ConsumerGuardTests.{name}" outcome="{outcome}" />'
                for name, outcome in results
            )
            path = Path(tempfile.mkstemp(suffix=".trx")[1])
            path.write_text(
                f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{rows}</Results></TestRun>',
                encoding="utf-8",
            )
            return path

        good = [(fact, "Passed") for fact in GUARD_FACTS]
        self.assertEqual([], check_guard_trx(trx(good), 4))
        self.assertTrue(check_guard_trx(trx(good[:-1]), 3))
        self.assertTrue(check_guard_trx(trx(good[:-1] + [(GUARD_FACTS[-1], "NotExecuted")]), 3))
        self.assertTrue(check_guard_trx(trx(good[:-1] + [(GUARD_FACTS[-1], "Failed")]), 3))
        self.assertTrue(check_guard_trx(trx(good), 5))
        self.assertTrue(check_guard_trx(trx(good + [("Other", "Passed")]), 4, exact=True))
        root_test = "Calor.Tasks.Tests.CompileCalorIntegrationTests.VerifyGate_NativeZ3_DeployedToTasksOutputRoot"
        substitute = trx([("Unrelated", "Passed")])
        self.assertTrue(check_guard_trx(substitute, 1, exact=True, guard=False, required=(root_test,)))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-trx", type=Path)
    parser.add_argument("--min-passed", type=int, default=len(GUARD_FACTS))
    parser.add_argument("--project", help="with --check-trx: require the manifest total minus registered divergences")
    parser.add_argument("--rid")
    parser.add_argument("--divergence-filter", action="store_true", help="print the dotnet test filter for --rid/--project")
    parser.add_argument("--list-test-projects", action="store_true", help="print every eng/test-manifest.json project")
    parser.add_argument("--exact", type=int, help="with --check-trx: require exactly this many results, all passed")
    parser.add_argument("--no-guard", action="store_true", help="with --check-trx: the run need not include the guard")
    parser.add_argument("--require", action="append", default=[], help="with --check-trx: a test name that must pass")
    args, rest = parser.parse_known_args()
    if args.list_test_projects:
        manifest = json.loads((REPO_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
        print("\n".join(project["path"] for project in manifest["projects"]))
        return 0
    if args.divergence_filter:
        print(divergence_filter(args.rid, args.project))
        return 0
    if args.check_trx:
        min_passed = args.min_passed
        if args.project:
            manifest = json.loads((REPO_ROOT / "eng/test-manifest.json").read_text(encoding="utf-8"))
            total = next(p["expectedTotal"] for p in manifest["projects"] if p["path"] == args.project)
            min_passed = total - len(divergences(args.rid or "", args.project))
        if args.exact is not None:
            min_passed = args.exact
        errors = check_guard_trx(
            args.check_trx, min_passed, exact=bool(args.project) or args.exact is not None,
            guard=not args.no_guard, required=tuple(args.require),
        )
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        if not errors:
            print(f"OK: {args.check_trx}: every required result passed, nothing skipped or failed.")
        return 1 if errors else 0
    result = unittest.main(argv=[sys.argv[0], *rest], exit=False).result
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
