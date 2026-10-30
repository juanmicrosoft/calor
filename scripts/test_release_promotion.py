#!/usr/bin/env python3
"""Exercise the actual promotion entry point with authenticated API responses replaced offline."""

import contextlib
import copy
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import release_checks as rc
import release_promotion as rp


class PromotionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        (self.root / "Directory.Build.props").write_text("<Version>0.30.0</Version>")
        (self.root / "CHANGELOG.md").write_text("## [0.30.0]\n\nRelease.\n")
        for args in (["init", "-q"], ["add", "."],
                     ["-c", "user.name=Test", "-c", "user.email=test@example.invalid",
                      "-c", "commit.gpgsign=false", "commit", "-qm", "candidate"]):
            subprocess.run(["git", "-C", str(self.root), *args], check=True, capture_output=True)
        self.candidate = rc.git(self.root, "rev-parse", "HEAD")
        subprocess.run(["git", "-C", str(self.root), "update-ref", "refs/remotes/origin/main",
                        self.candidate], check=True)
        self.files = {
            "nupkg/calor.0.30.0.nupkg": b"cli",
            "nupkg/Calor.Sdk.0.30.0.nupkg": b"sdk",
            "release-notes/notes.md": b"Release.",
            "site/site.tar": b"validated site",
            **{f"release-metadata/calor-nuget-{self.candidate}.{s}": b"{}"
               for s in ("sbom.spdx.json", "provenance.json")},
        }
        self.sums = "".join(f"{rc.sha256(data)}  {name}\n" for name, data in self.files.items()).encode()
        self.files["SHA256SUMS"] = self.sums
        self.archive = self.make_archive()
        self.digest = rc.sha256(self.archive)
        self.responses = {
            "actions/runs/42": {
                "id": 42, "repository": {"full_name": rp.REPOSITORY}, "event": "workflow_dispatch",
                "head_branch": "main", "head_sha": self.candidate, "workflow_id": 7,
                "path": rp.WORKFLOW, "status": "in_progress", "conclusion": None},
            "actions/workflows/publish-nuget.yml": {"id": 7, "path": rp.WORKFLOW, "state": "active"},
            "actions/artifacts/123": {
                "id": 123, "name": "release-bundle", "workflow_run": {"id": 42, "head_sha": self.candidate},
                "digest": f"sha256:{self.digest}", "expired": False, "expires_at": "2099-01-01T00:00:00Z"},
            "environments/nuget-release": {
                "can_admins_bypass": False,
                "protection_rules": [{"type": "required_reviewers", "prevent_self_review": True,
                                      "reviewers": [{"type": "User", "reviewer": {"login": "maintainer"}}]}],
                "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True}},
            "environments/nuget-release/deployment-branch-policies": {
                "total_count": 1, "branch_policies": [{"type": "branch", "name": "main"}]},
            "branches/main": {"protected": True},
        }
        self.needs = {g: {"result": "success"} for g in rp.GATES}
        self.env = {
            "GITHUB_REPOSITORY": rp.REPOSITORY, "GITHUB_REF": "refs/heads/main",
            "GITHUB_SHA": self.candidate, "GITHUB_RUN_ID": "42",
            "GITHUB_WORKFLOW_REF": f"{rp.REPOSITORY}/{rp.WORKFLOW}@refs/heads/main",
        }
        self.downloads = []
        self.api_error = None

    def make_archive(self):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as archive:
            for name, data in self.files.items():
                archive.writestr(name, data)
        return output.getvalue()

    def invoke(self, *, version="0.30.0", destination="download"):
        def get(path):
            if self.api_error is not None:
                raise self.api_error
            return copy.deepcopy(self.responses[path])

        def download(path):
            self.downloads.append(path)
            self.assertEqual(f"repos/{rp.REPOSITORY}/actions/artifacts/123/zip", path)
            return self.archive

        with (contextlib.chdir(self.root),
              patch.dict(os.environ, {**self.env, "RELEASE_NEEDS": json.dumps(self.needs)}),
              patch.object(rp, "get", side_effect=get),
              patch.object(rp, "api", side_effect=download),
              patch.object(sys, "argv", ["release_promotion.py", "--candidate", self.candidate,
                                        "--version", version, "--artifact-id", "123", "--digest", self.digest,
                                        "--sums", rc.sha256(self.sums),
                                        "--destination", str(self.root / destination)]),
              contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO())):
            return rp.main()

    def test_exact_bytes_and_retry_after_main_advances(self):
        self.assertEqual(0, self.invoke())
        # A failed-job rerun keeps GITHUB_SHA, successful gate outputs and artifact ID even if
        # the branch tip advances; no API read uses the current tip as the candidate.
        (self.root / "later").write_text("unrelated main change")
        for args in (["add", "later"],
                     ["-c", "user.name=Test", "-c", "user.email=test@example.invalid",
                      "-c", "commit.gpgsign=false", "commit", "-qm", "later"],
                     ["update-ref", "refs/remotes/origin/main", "HEAD"],
                     ["checkout", "--detach", self.candidate]):
            subprocess.run(["git", "-C", str(self.root), *args], check=True, capture_output=True)
        self.assertEqual(0, self.invoke(destination="retry"))
        for name, data in self.files.items():
            self.assertEqual(data, (self.root / "retry" / name).read_bytes())

    def test_identity_failures_never_download(self):
        mutations = [
            ("actions/runs/42", "id", 99),
            ("actions/runs/42", "repository", {"full_name": "attacker/calor"}),
            ("actions/runs/42", "event", "pull_request"),
            ("actions/runs/42", "head_branch", "feature"),
            ("actions/runs/42", "head_sha", "f" * 40),
            ("actions/runs/42", "workflow_id", 9),
            ("actions/runs/42", "path", ".github/workflows/other.yml"),
            ("actions/runs/42", "status", "completed"),
            ("actions/runs/42", "conclusion", "failure"),
            ("actions/workflows/publish-nuget.yml", "state", "disabled_manually"),
            ("actions/workflows/publish-nuget.yml", "path", "other.yml"),
            ("actions/artifacts/123", "id", 456),
            ("actions/artifacts/123", "name", "replacement"),
            ("actions/artifacts/123", "workflow_run", {"id": 99, "head_sha": self.candidate}),
            ("actions/artifacts/123", "workflow_run", {"id": 42, "head_sha": "f" * 40}),
            ("actions/artifacts/123", "digest", "sha256:" + "f" * 64),
            ("actions/artifacts/123", "expired", True),
            ("actions/artifacts/123", "expires_at", "2020-01-01T00:00:00Z"),
            ("branches/main", "protected", False),
            ("environments/nuget-release", "can_admins_bypass", True),
            ("environments/nuget-release", "protection_rules", []),
            ("environments/nuget-release", "protection_rules",
             [{"type": "required_reviewers", "prevent_self_review": False, "reviewers": [{}]}]),
            ("environments/nuget-release", "protection_rules",
             [{"type": "required_reviewers", "prevent_self_review": True, "reviewers": []}]),
            ("environments/nuget-release", "deployment_branch_policy", None),
            ("environments/nuget-release/deployment-branch-policies", "total_count", 2),
            ("environments/nuget-release/deployment-branch-policies", "branch_policies",
             [{"type": "tag", "name": "main"}]),
        ]
        original = copy.deepcopy(self.responses)
        for endpoint, key, value in mutations:
            with self.subTest(endpoint=endpoint, key=key, value=value):
                self.responses = copy.deepcopy(original)
                self.responses[endpoint][key] = value
                self.assertEqual(1, self.invoke())
                self.assertEqual([], self.downloads)

    def test_every_gate_must_succeed(self):
        for gate in rp.GATES:
            for state in ("failure", "cancelled", "skipped", "in_progress", None):
                with self.subTest(gate=gate, state=state):
                    self.needs[gate] = {"result": state}
                    self.assertEqual(1, self.invoke())
                    self.assertEqual([], self.downloads)
                    self.needs[gate] = {"result": "success"}
        del self.needs["test"]
        self.assertEqual(1, self.invoke())

    def test_wrong_version_and_untrusted_context(self):
        self.assertEqual(1, self.invoke(version="0.31.0"))
        for key in self.env:
            original = self.env[key]
            self.env[key] = "untrusted"
            self.assertEqual(1, self.invoke())
            self.env[key] = original
        self.assertEqual([], self.downloads)

    def test_missing_artifact_or_unreadable_policy(self):
        for error in (subprocess.CalledProcessError(1, "gh"), ValueError("invalid JSON"),
                      KeyError("artifact missing")):
            self.api_error = error
            self.assertEqual(1, self.invoke())
            self.assertEqual([], self.downloads)

    def test_tampered_download_fails_before_extraction(self):
        self.archive += b"tampered"
        self.assertEqual(1, self.invoke())
        self.assertFalse((self.root / "download").exists())

    def test_missing_required_bundle_content(self):
        original = dict(self.files)
        for number, name in enumerate(original):
            if name == "SHA256SUMS":
                continue
            with self.subTest(name=name):
                self.files = {k: v for k, v in original.items() if k not in (name, "SHA256SUMS")}
                self.sums = "".join(f"{rc.sha256(v)}  {k}\n" for k, v in self.files.items()).encode()
                self.files["SHA256SUMS"] = self.sums
                self.archive = self.make_archive()
                self.digest = rc.sha256(self.archive)
                self.responses["actions/artifacts/123"]["digest"] = f"sha256:{self.digest}"
                self.assertEqual(1, self.invoke(destination=f"missing-{number}"))

    def test_archive_and_inventory_failures(self):
        original = dict(self.files)
        for name in ("../escape", "/absolute", "scripts/release_checks.py", "extra.txt"):
            with self.subTest(name=name):
                self.files = {**original, name: b"bad"}
                self.archive = self.make_archive()
                self.digest = rc.sha256(self.archive)
                self.responses["actions/artifacts/123"]["digest"] = f"sha256:{self.digest}"
                self.assertEqual(1, self.invoke())
                self.assertFalse((self.root / "download").exists())
        self.files = {**original, "nupkg/calor.0.30.0.nupkg": b"changed"}
        self.archive = self.make_archive()
        self.digest = rc.sha256(self.archive)
        self.responses["actions/artifacts/123"]["digest"] = f"sha256:{self.digest}"
        self.assertEqual(1, self.invoke())

    def test_zip_duplicates_directories_and_symlinks_are_refused(self):
        for kind in ("duplicate", "directory", "symlink"):
            with self.subTest(kind=kind):
                output = io.BytesIO()
                with zipfile.ZipFile(output, "w") as archive:
                    if kind == "duplicate":
                        archive.writestr("SHA256SUMS", b"first")
                        with self.assertWarns(UserWarning):
                            archive.writestr("SHA256SUMS", b"second")
                    elif kind == "directory":
                        archive.writestr("nupkg/", b"")
                    else:
                        entry = zipfile.ZipInfo("site/site.tar")
                        entry.create_system = 3
                        entry.external_attr = 0o120777 << 16
                        archive.writestr(entry, b"/etc/passwd")
                with self.assertRaises(rc.CheckError):
                    rp.unpack(output.getvalue(), self.root / f"bad-{kind}")
                self.assertFalse((self.root / f"bad-{kind}").exists())


if __name__ == "__main__":
    unittest.main()
