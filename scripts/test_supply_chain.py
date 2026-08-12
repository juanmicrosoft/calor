#!/usr/bin/env python3
"""Regression tests for hermetic asset and release metadata tooling."""

from __future__ import annotations

import json
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parent.parent


class SupplyChainTests(unittest.TestCase):
    def test_build_project_has_no_network_or_tracked_resource_mutation_targets(self) -> None:
        project = (REPO_ROOT / "src/Calor.Compiler/Calor.Compiler.csproj").read_text()
        for forbidden in (
            "DownloadZ3",
            "curl ",
            "Invoke-WebRequest",
            ".NETCoreApp,Version=v8.0",
            "SyncSelfTestFiles",
            "Resources\\SelfTest</SelfTestDir>",
        ):
            self.assertNotIn(forbidden, project)
        self.assertIn("ValidateZ3Assets", project)

    def test_maintained_package_references_are_centrally_versioned_and_locked(self) -> None:
        isolated = (
            "bench/corpus/",
            "bench/phase0-agent-native/",
            "tests/E2E/",
            "tests/SdkConsumer/",
            "tests/DebugRT/",
        )
        for project in REPO_ROOT.rglob("*.csproj"):
            relative = project.relative_to(REPO_ROOT).as_posix()
            if any(relative.startswith(prefix) for prefix in isolated):
                continue
            text = project.read_text(encoding="utf-8")
            package_lines = [
                line for line in text.splitlines() if "<PackageReference" in line
            ]
            for line in package_lines:
                self.assertNotRegex(line, r'\sVersion="', relative)
            if package_lines:
                self.assertTrue(
                    (project.parent / "packages.lock.json").is_file(),
                    f"{relative} has PackageReference items but no packages.lock.json",
                )

    def test_z3_verifier_rejects_corrupt_truncated_and_error_page_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            shutil.copytree(REPO_ROOT / ".github", root / ".github")
            shutil.copytree(
                REPO_ROOT / "src/Calor.Compiler/scripts",
                root / "src/Calor.Compiler/scripts",
            )
            asset_paths = {
                "Microsoft.Z3.dll": "src/Calor.Compiler/z3/Microsoft.Z3.dll",
                "libz3-linux-arm64.so": "src/Calor.Compiler/runtimes/linux-arm64/native/libz3.so",
                "libz3-linux-x64.so": "src/Calor.Compiler/runtimes/linux-x64/native/libz3.so",
                "libz3-osx-arm64.dylib": "src/Calor.Compiler/runtimes/osx-arm64/native/libz3.dylib",
                "libz3-win-arm64.dll": "src/Calor.Compiler/runtimes/win-arm64/native/libz3.dll",
                "libz3-win-x64.dll": "src/Calor.Compiler/runtimes/win-x64/native/libz3.dll",
            }
            manifest_lines = []
            for index, (name, relative) in enumerate(asset_paths.items()):
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                content = f"synthetic-z3-asset-{index}".encode()
                target.write_bytes(content)
                manifest_lines.append(
                    f"{hashlib.sha256(content).hexdigest()}  {name}  {len(content)}"
                )
            (root / ".github/z3-binaries-4.15.7.sha256").write_text(
                "\n".join(manifest_lines) + "\n",
                encoding="utf-8",
            )

            verifier = REPO_ROOT / "scripts/verify-z3-assets.py"
            valid = subprocess.run(
                ["python3", str(verifier), "--repo-root", str(root)],
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, valid.returncode, valid.stderr)

            target = root / "src/Calor.Compiler/z3/Microsoft.Z3.dll"
            original = target.read_bytes()
            for bad_content in (original[:-1], b"<html>404 Not Found</html>", b"X" * len(original)):
                target.write_bytes(bad_content)
                rejected = subprocess.run(
                    ["python3", str(verifier), "--repo-root", str(root)],
                    capture_output=True,
                    text=True,
                )
                self.assertNotEqual(0, rejected.returncode)
                target.write_bytes(original)

    def test_release_metadata_is_deterministic_and_hashes_subjects(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact = root / "calor.nupkg"
            artifact.write_bytes(b"package")
            first = root / "first"
            second = root / "second"
            command = [
                "python3",
                str(REPO_ROOT / "scripts/generate-release-metadata.py"),
                "--name",
                "test-release",
                "--artifacts",
                str(artifact),
                "--repo-root",
                str(REPO_ROOT),
                "--commit",
                "a" * 40,
                "--repository",
                "https://github.com/juanmicrosoft/calor",
            ]
            subprocess.run([*command, "--output", str(first)], check=True)
            subprocess.run([*command, "--output", str(second)], check=True)
            sbom_name = "test-release.sbom.spdx.json"
            provenance_name = "test-release.provenance.json"
            self.assertEqual(
                (first / sbom_name).read_bytes(),
                (second / sbom_name).read_bytes(),
            )
            self.assertEqual(
                (first / provenance_name).read_bytes(),
                (second / provenance_name).read_bytes(),
            )
            provenance = json.loads((first / provenance_name).read_text())
            self.assertEqual("calor.nupkg", provenance["subject"][0]["name"])
            self.assertEqual(64, len(provenance["subject"][0]["digest"]["sha256"]))
            dependencies = provenance["predicate"]["buildDefinition"]["resolvedDependencies"]
            self.assertTrue(
                any(
                    dependency["uri"].endswith("/Microsoft.Z3.dll")
                    and len(dependency["digest"]["sha256"]) == 64
                    for dependency in dependencies
                )
            )
            self.assertTrue(
                any(
                    dependency["uri"].startswith("pkg:npm/%40types/")
                    and "%2F" not in dependency["uri"]
                    for dependency in dependencies
                )
            )


if __name__ == "__main__":
    unittest.main()
