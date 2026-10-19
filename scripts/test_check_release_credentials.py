#!/usr/bin/env python3
"""Tests for scripts/check_release_credentials.py (#1545).

The settings check runs against saved API responses in a temporary directory; each negative
control changes one fact. The workflow check runs against this repository's workflows and
against mutated copies. Run: python3 scripts/test_check_release_credentials.py
"""

from __future__ import annotations

import contextlib
import io
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import unittest.mock
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))
import check_release_credentials as cred  # noqa: E402

def secrets(*names: str) -> dict:
    return {"total_count": len(names), "secrets": [{"name": n} for n in names]}


CLOSED = {
    "repo-secrets": secrets("ANTHROPIC_API_KEY"),
    "environment": {
        "name": "nuget-release",
        "can_admins_bypass": False,
        "protection_rules": [{"type": "branch_policy"}, {"type": "required_reviewers"}],
        "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
    },
    "branch-policies": {"total_count": 1, "branch_policies": [{"name": "main", "type": "branch"}]},
    # Trusted Publishing: no environment holds a NuGet secret.
    "environments": {"total_count": 2, "environments": [{"name": "github-pages"}, {"name": "nuget-release"}]},
    "environment-secrets/nuget-release": secrets(),
    "environment-secrets/github-pages": secrets("PAGES_TOKEN"),
    "workflow-permissions": {"default_workflow_permissions": "read", "can_approve_pull_request_reviews": False},
}


class Fixture:
    def __init__(self) -> None:
        self.base = Path(tempfile.mkdtemp(prefix="calor-cred-"))
        self.responses = self.base / "api"
        self.responses.mkdir()
        self.repo = self.base / "repo"
        shutil.copytree(REPO_ROOT / ".github" / "workflows", self.repo / ".github" / "workflows")
        for key, body in CLOSED.items():
            self.write(key, body)

    def write(self, key: str, body: object) -> None:
        (self.responses / f"{key.replace('/', '--')}.error").unlink(missing_ok=True)
        (self.responses / f"{key.replace('/', '--')}.json").write_text(json.dumps(body), encoding="utf-8")

    def remove(self, key: str) -> None:
        (self.responses / f"{key.replace('/', '--')}.json").unlink(missing_ok=True)
        (self.responses / f"{key.replace('/', '--')}.error").unlink(missing_ok=True)

    def unreadable(self, key: str, text: str = "HTTP 403: Resource not accessible") -> None:
        self.remove(key)
        (self.responses / f"{key.replace('/', '--')}.error").write_text(text, encoding="utf-8")

    def workflow(self, name: str) -> Path:
        return self.repo / ".github" / "workflows" / name

    def run(self, live: bool = True, extra: list[str] | None = None) -> tuple[int, str]:
        out = io.StringIO()
        args = ["--repo", str(self.repo)] + (["--from-dir", str(self.responses)] if live else []) + (extra or [])
        with contextlib.redirect_stdout(out):
            code = cred.main(args)
        return code, out.getvalue()


class CredentialCheckTests(unittest.TestCase):
    def setUp(self) -> None:
        self.f = Fixture()

    def tearDown(self) -> None:
        shutil.rmtree(self.f.base, ignore_errors=True)

    def assert_codes(self, expected: set[str], verdict: str = "OPEN", extra: list[str] | None = None) -> None:
        code, out = self.f.run(extra=extra)
        found = set(re.findall(r"\(#1545\) ([WSUV]\d{3}):", out))
        self.assertEqual(expected, found, out)
        self.assertEqual(1, code, out)
        self.assertIn(f"RELEASE CREDENTIALS: {verdict}", out)

    # -- positive controls ------------------------------------------------------------------

    def test_committed_workflows_pass_the_offline_check(self) -> None:
        code, out = self.f.run(live=False)
        self.assertEqual(0, code, out)
        self.assertIn("CLOSED (workflows only", out)

    def test_closed_settings_pass(self) -> None:
        code, out = self.f.run()
        self.assertEqual(0, code, out)
        self.assertIn("CLOSED (workflows and repository settings)", out)

    # -- settings: one fact changed per control ---------------------------------------------

    def test_repository_level_nuget_secret_is_open(self) -> None:
        self.f.write("repo-secrets", secrets("NUGET_API_KEY"))
        self.assert_codes({"S001"})

    def test_new_secret_name_at_repository_level_is_open(self) -> None:
        self.f.write("repo-secrets", secrets("NUGET_RELEASE_API_KEY"))
        self.assert_codes({"S001"})

    def test_missing_environment_is_open(self) -> None:
        self.f.remove("environment")
        self.assert_codes({"S002"})

    def test_auto_created_environment_without_rules_is_open(self) -> None:
        # GitHub creates a referenced environment on first use, with no rules and admin bypass on.
        self.f.write("environment", {"name": "nuget-release", "can_admins_bypass": True,
                                     "protection_rules": [], "deployment_branch_policy": None})
        self.assert_codes({"S003", "S004"})

    def test_protected_branches_policy_is_open(self) -> None:
        env = json.loads(json.dumps(CLOSED["environment"]))
        env["deployment_branch_policy"] = {"protected_branches": True, "custom_branch_policies": False}
        self.f.write("environment", env)
        self.assert_codes({"S004"})

    def test_branch_policy_other_than_main_only_is_open(self) -> None:
        for rules in ([{"name": "main", "type": "branch"}, {"name": "v*", "type": "tag"}],
                      [{"name": "*", "type": "branch"}],
                      [{"name": "main", "type": "tag"}],
                      []):
            with self.subTest(rules=rules):
                self.f.write("branch-policies", {"total_count": len(rules), "branch_policies": rules})
                self.assert_codes({"S005"})

    def test_leftover_nuget_secret_in_any_environment_is_open(self) -> None:
        # Trusted Publishing stores no key: the interim design's environment secret, or a NuGet
        # secret in any other environment, is a stale key (S007). S006 (secret missing) is retired.
        for key, names in (("environment-secrets/nuget-release", ("NUGET_RELEASE_API_KEY",)),
                           ("environment-secrets/nuget-release", ("OTHER", "nuget_api_key")),
                           ("environment-secrets/github-pages", ("NUGET_API_KEY",))):
            with self.subTest(key=key, names=names):
                self.f.write(key, secrets(*names))
                self.assert_codes({"S007"})
                self.f.write(key, CLOSED[key])

    def test_every_listed_environment_is_read(self) -> None:
        # A third environment the fixture has no response for is UNKNOWN, never skipped.
        self.f.write("environments", {"total_count": 3, "environments": [
            {"name": "github-pages"}, {"name": "nuget-release"}, {"name": "staging"}]})
        self.assert_codes({"U004"}, verdict="UNKNOWN")
        self.f.write("environment-secrets/staging", secrets("NUGET_KEY"))
        self.assert_codes({"S007"})

    def test_actions_workflow_permissions_must_be_read_only_without_pr_creation(self) -> None:
        for body, codes in (
            ({"default_workflow_permissions": "write", "can_approve_pull_request_reviews": False}, {"S008"}),
            ({"default_workflow_permissions": "read", "can_approve_pull_request_reviews": True}, {"S009"}),
            ({"default_workflow_permissions": "write", "can_approve_pull_request_reviews": True}, {"S008", "S009"}),
            ({}, {"S008", "S009"}),
            ({"default_workflow_permissions": "READ", "can_approve_pull_request_reviews": "false"}, {"S008", "S009"}),
        ):
            with self.subTest(body=body):
                self.f.write("workflow-permissions", body)
                self.assert_codes(codes)

    def test_environment_secrets_are_read_from_each_environment(self) -> None:
        paths = []

        def fake_run(argv, capture_output, text):
            self.assertEqual(["gh", "api", "--method", "GET"], argv[:4])
            paths.append(argv[-1])
            return subprocess.CompletedProcess(argv, 0, json.dumps(secrets()), "")

        with unittest.mock.patch.object(cred.subprocess, "run", fake_run):
            cred.gh_fetch("o/r")("environment-secrets/nuget-release")
            cred.gh_fetch("o/r")("environment-secrets/a b/c")
        self.assertEqual(["repos/o/r/environments/nuget-release/secrets?per_page=100&page=1",
                          "repos/o/r/environments/a%20b%2Fc/secrets?per_page=100&page=1"], paths)

    def test_listing_short_of_its_total_count_is_unknown(self) -> None:
        # A first page of 30 harmless names with total_count 31: the 31st could be NUGET_API_KEY.
        page = secrets(*[f"OTHER_{i}" for i in range(30)])
        page["total_count"] = 31
        self.f.write("repo-secrets", page)
        self.assert_codes({"U001"}, verdict="UNKNOWN")
        policies = dict(CLOSED["branch-policies"], total_count=2)
        self.f.write("repo-secrets", CLOSED["repo-secrets"])
        self.f.write("branch-policies", policies)
        self.assert_codes({"U003"}, verdict="UNKNOWN")

    def test_live_fetch_reads_every_page(self) -> None:
        pages = {1: secrets(*[f"OTHER_{i}" for i in range(100)]), 2: secrets("NUGET_API_KEY")}
        for body in pages.values():
            body["total_count"] = 101
        calls = []

        def fake_run(argv, capture_output, text):
            calls.append(argv)
            self.assertEqual(["gh", "api", "--method", "GET"], argv[:4])
            page = int(re.search(r"[?&]page=(\d+)", argv[-1]).group(1))
            return subprocess.CompletedProcess(argv, 0, json.dumps(pages[page]), "")

        with unittest.mock.patch.object(cred.subprocess, "run", fake_run):
            body = cred.gh_fetch("o/r")("repo-secrets")
        self.assertEqual(2, len(calls))
        self.assertIn("NUGET_API_KEY", cred.names(body, "repository secrets"))

    def test_unreadable_settings_are_unknown_never_closed(self) -> None:
        for key, code in (("repo-secrets", "U001"), ("environment", "U002"),
                          ("branch-policies", "U003"), ("environment-secrets/nuget-release", "U004"),
                          ("environments", "U004")):
            with self.subTest(key=key):
                fixture = Fixture()
                try:
                    self.f, saved = fixture, self.f
                    self.f.unreadable(key)
                    self.assert_codes({code}, verdict="UNKNOWN")
                finally:
                    self.f = saved
                    shutil.rmtree(fixture.base, ignore_errors=True)

    # -- workflow permissions: 403, attestation ---------------------------------------------

    ATTESTATION = REPO_ROOT / "docs" / "plans" / "evidence" / "release-gaps-0.25" / \
        "actions-workflow-permissions-attestation.json"

    def attest(self, **changes: object) -> list[str]:
        body = json.loads(self.ATTESTATION.read_text(encoding="utf-8"))
        body.update(changes)
        for key in [k for k, v in changes.items() if v is ...]:
            del body[key]
        path = self.f.base / "attestation.json"
        path.write_text(json.dumps(body), encoding="utf-8")
        return ["--attest-workflow-permissions", str(path)]

    def test_forbidden_workflow_permissions_are_unverifiable_never_closed(self) -> None:
        # The maintainer's gh login gets HTTP 403 on this endpoint (2026-10-19). Never a silent pass.
        self.f.unreadable("workflow-permissions", 'gh: HTTP 403 {"status":"403"}')
        self.assert_codes({"V001"}, verdict="UNVERIFIABLE")
        # Any other failure stays UNKNOWN.
        self.f.unreadable("workflow-permissions", "HTTP 502: Bad Gateway")
        self.assert_codes({"U005"}, verdict="UNKNOWN")
        # UNKNOWN and OPEN outrank UNVERIFIABLE.
        self.f.unreadable("workflow-permissions", "HTTP 403")
        self.f.unreadable("repo-secrets", "HTTP 500")
        self.assert_codes({"U001", "V001"}, verdict="UNKNOWN")
        self.f.write("repo-secrets", secrets("NUGET_API_KEY"))
        self.assert_codes({"S001", "V001"}, verdict="OPEN")

    def test_live_403_is_forbidden(self) -> None:
        def fake_run(argv, capture_output, text):
            return subprocess.CompletedProcess(argv, 1, '{"message":"no","status":"403"}', "gh: no (HTTP 403)")

        with unittest.mock.patch.object(cred.subprocess, "run", fake_run):
            with self.assertRaises(cred.Forbidden):
                cred.gh_fetch("o/r")("workflow-permissions")

    def test_committed_attestation_closes_a_forbidden_read_and_is_shown(self) -> None:
        self.f.unreadable("workflow-permissions", "HTTP 403")
        code, out = self.f.run(extra=self.attest())
        self.assertEqual(0, code, out)
        self.assertIn("A001: Actions workflow permissions ATTESTED, not read by API", out)
        self.assertIn("confirmed by @juanmicrosoft on 2026-10-19", out)
        self.assertIn("RELEASE CREDENTIALS: CLOSED (workflows and repository settings; Actions workflow "
                      "permissions ATTESTED", out)

    def test_attestation_never_overrides_an_api_answer(self) -> None:
        self.f.write("workflow-permissions", {"default_workflow_permissions": "write",
                                              "can_approve_pull_request_reviews": True})
        self.assert_codes({"S008", "S009"}, extra=self.attest())
        self.f.write("workflow-permissions", CLOSED["workflow-permissions"])
        code, out = self.f.run(extra=self.attest())
        self.assertEqual(0, code, out)
        self.assertIn("A000", out)
        self.assertNotIn("ATTESTED", out)

    def test_invalid_attestation_is_unknown(self) -> None:
        self.f.unreadable("workflow-permissions", "HTTP 403")
        for changes in ({"default_workflow_permissions": "write"},
                        {"can_approve_pull_request_reviews": True},
                        {"can_approve_pull_request_reviews": "false"},
                        {"repository": "someone/else"},
                        {"schema": "v0"},
                        {"setting": "actions/permissions"},
                        {"confirmed_by": "juanmicrosoft"},
                        {"confirmed_on": "yesterday"},
                        {"why_not_read": " "},
                        {"why_not_read": ...},
                        {"extra": 1}):
            with self.subTest(changes=changes):
                self.assert_codes({"U006"}, verdict="UNKNOWN", extra=self.attest(**changes))
        missing = ["--attest-workflow-permissions", str(self.f.base / "nope.json")]
        self.assert_codes({"U006"}, verdict="UNKNOWN", extra=missing)

    def test_attestation_needs_a_settings_source(self) -> None:
        code, out = self.f.run(live=False, extra=self.attest())
        self.assertEqual(2, code, out)
        self.assertNotIn("CLOSED", out)

    # -- workflows --------------------------------------------------------------------------

    def test_workflow_naming_the_old_secret_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        text = path.read_text(encoding="utf-8")
        old = "steps.nuget-login.outputs.NUGET_API_KEY }}\n        run: dotnet nuget push"
        self.assertIn(old, text)
        for secret in ("NUGET_API_KEY", "NUGET_RELEASE_API_KEY"):
            with self.subTest(secret=secret):
                path.write_text(text.replace(old, f"secrets.{secret} }}}}\n        run: dotnet nuget push"),
                                encoding="utf-8")
                # W015: the step that names the secret is no longer the accepted push step.
                self.assert_codes({"W002", "W005", "W015"})

    def test_publish_job_without_the_environment_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        text = path.read_text(encoding="utf-8")
        self.assertIn("    environment: nuget-release\n", text)
        path.write_text(text.replace("    environment: nuget-release\n", ""), encoding="utf-8")
        self.assert_codes({"W006"})

    def test_another_job_using_the_key_or_environment_is_open(self) -> None:
        path = self.f.workflow("benchmark.yml")
        text = path.read_text(encoding="utf-8")
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        self.assertIn(marker, text)
        path.write_text(text.replace(marker, marker + "    environment: nuget-release\n"
                                     "    env:\n      K: ${{ secrets.NUGET_RELEASE_API_KEY }}\n"), encoding="utf-8")
        # Trusted Publishing: the retired secret name is W002 everywhere (formerly W003).
        self.assert_codes({"W002", "W004"})

    def test_other_syntaxes_for_the_key_or_environment_are_open(self) -> None:
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        for injected, codes in (
            ("    environment:\n      name: nuget-release\n", {"W004"}),
            ("    environment: { name: nuget-release }\n", {"W004"}),
            ("    environment: ${{ inputs.env }}\n", {"W004"}),
            ("    environment:\n      name: ${{ format('nuget-{0}', 'release') }}\n", {"W004"}),
            ("    environment:\n      url: https://example.invalid\n", {"W004"}),
            ("    env:\n      K: ${{ secrets['NUGET_RELEASE_API_KEY'] }}\n", {"W002"}),
            # Round 2: names are case-insensitive in Actions, and YAML escapes decode.
            ("    environment: NUGET-RELEASE\n", {"W004"}),
            ('    environment: "nuget-\\u0072elease"\n', {"W004"}),
            ("    'environment': nuget-release\n", {"W004"}),
            ("    env:\n      K: ${{ secrets.nuget_release_api_key }}\n", {"W002"}),
            ('    env:\n      K: "${{ secrets.NUGET_\\u0052ELEASE_API_KEY }}"\n', {"W002"}),
            # EXC-RELEASE-GAPS-A-YAML: the parser fails closed on an undefined alias (W012); a
            # defined anchor is still reported (W009).
            ("    <<: *defaults\n", {"W012"}),
            ("    timeout-minutes: &t 30\n", {"W009"}),
            # Formerly C# ReleaseWorkflowGateTests cases (the C# lane has no PyYAML).
            ("    env:\n      K: ${{ secrets.NUGET_API_KEY }}\n", {"W002"}),
            # Round 3: escaped keys, flow mappings under a block header, complex keys, escaped
            # line breaks, and workflows called from another repository.
            ('    "environm\\u0065nt": nuget-release\n', {"W004"}),
            ("    {environment: nuget-release}\n", {"W012"}),
            ("    ? environment\n    : nuget-release\n", {"W004"}),
            ('    "environ\\\n      ment": nuget-release\n', {"W012"}),
            ("    uses: someone/else/.github/workflows/publish.yml@main\n", {"W011"}),
            # EXC-RELEASE-GAPS-A-CHECKER (verification-pass finding): folded and quoted forms.
            ("    uses: >-\n      someone/else/.github/workflows/publish.yml@0123456789abcdef\n    secrets: inherit\n",
             {"W011"}),
            ('    "uses": someone/else/.github/workflows/publish.yml@main\n', {"W011"}),
            ("    uses: ./.github/workflows/no-such-file.yml\n", {"W011"}),
            ("    uses: ./.github/workflows/sub/test.yml\n", {"W011"}),
            ("    uses: someone/else/.github/workflows/publish.yml@main\n    secrets: inherit\n", {"W011"}),
            # Duplicate keys, also under case folding, fail closed.
            ("    runs-on: ubuntu-22.04\n", {"W012"}),
            ("    Environment: github-pages\n    environment: github-pages\n", {"W012"}),
            ("    uses: ./.github/workflows/test.yml@main\n", {"W011"}),
            # Trusted Publishing: only the publish job may use NuGet/login or its output. Another
            # job could not get a key (nuget.org also needs publish-nuget.yml and nuget-release),
            # but the check refuses it outright.
            ("    env:\n      K: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n", {"W002", "W003"}),
            ("    env:\n      K: ${{ steps.nuget-login.outputs.nuget_api_key }}\n", {"W002", "W003"}),
            ("    env:\n      K: ${{ steps.NUGET-LOGIN.outputs.key }}\n", {"W003"}),
            ("    env:\n      K: nuget/LOGIN@v1\n", {"W003"}),
        ):
            with self.subTest(injected=injected):
                fixture = Fixture()
                try:
                    self.f, saved = fixture, self.f
                    path = self.f.workflow("benchmark.yml")
                    text = path.read_text(encoding="utf-8")
                    self.assertIn(marker, text)
                    path.write_text(text.replace(marker, marker + injected), encoding="utf-8")
                    self.assert_codes(codes)
                finally:
                    self.f = saved
                    shutil.rmtree(fixture.base, ignore_errors=True)

    def test_local_reusable_workflow_call_is_accepted(self) -> None:
        for call in ("    uses: ./.github/workflows/test.yml\n", "    'uses': ./.github/workflows/test.yml\n",
                     '    "uses": "./.github/workflows/test.yml"\n    secrets: inherit\n'):
            with self.subTest(call=call):
                fixture = Fixture()
                try:
                    path = fixture.workflow("benchmark.yml")
                    marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
                    text = path.read_text(encoding="utf-8")
                    path.write_text(text.replace(marker, "  local-call:\n" + call + marker), encoding="utf-8")
                    code, out = fixture.run()
                    self.assertEqual(0, code, out)
                finally:
                    shutil.rmtree(fixture.base, ignore_errors=True)

    def test_multiline_flow_mapping_job_is_open(self) -> None:
        # The exception verification pass's finding: a job body written as a multiline flow mapping.
        path = self.f.workflow("benchmark.yml")
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        text = path.read_text(encoding="utf-8")
        path.write_text(text.replace(marker, "  remote:\n    {\n      uses: someone/else/.github/workflows/publish.yml@main\n"
                                             "    }\n" + marker), encoding="utf-8")
        self.assert_codes({"W011"})

    def test_unparseable_workflow_is_open(self) -> None:
        path = self.f.workflow("benchmark.yml")
        path.write_text(path.read_text(encoding="utf-8") + "\n  broken: [\n", encoding="utf-8")
        self.assert_codes({"W012"})

    def test_explicit_yaml_tags_are_open(self) -> None:
        # #1567 (the #1565 verification pass): `!!set` and other tagged collections are not built
        # by the strict mapping constructor, so a duplicate key under one would go unseen. Codex
        # round 1: a tagged scalar can erase the text the checks look for (`!!null` turns the
        # secret reference into None). Any explicit tag, on any node, is W012.
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        for injected in (
            "    environment: !!set\n      ? nuget-release\n      ? nuget-release\n",
            "    environment: !!set {github-pages}\n",
            "    steps2: !!omap\n      - a: 1\n      - a: 2\n",
            "    steps2: !!pairs\n      - a: 1\n",
            "    env: !local\n      K: v\n",
            "    env: !!python/dict\n      K: v\n",
            "    env2: !!map\n      K: v\n",
            "    outputs2: !!seq [a]\n",
            '    env: {K: !!null "${{ secrets.NUGET_RELEASE_API_KEY }}"}\n',
            '    env:\n      K: !!binary "JHt7IHNlY3JldHMuTlVHRVRfUkVMRUFTRV9BUElfS0VZIH19"\n',
            "    env:\n      !!str K: v\n",
            "    env:\n      K: ! v\n",
            "    timeout-minutes: !!int 30\n",
        ):
            with self.subTest(injected=injected):
                fixture = Fixture()
                try:
                    self.f, saved = fixture, self.f
                    path = self.f.workflow("benchmark.yml")
                    text = path.read_text(encoding="utf-8")
                    self.assertIn(marker, text)
                    path.write_text(text.replace(marker, marker + injected), encoding="utf-8")
                    self.assert_codes({"W012"})
                finally:
                    self.f = saved
                    shutil.rmtree(fixture.base, ignore_errors=True)
        # The same keys untagged pass, so the cases above fail on the tag alone.
        path = self.f.workflow("benchmark.yml")
        text = path.read_text(encoding="utf-8")
        path.write_text(text.replace(marker, marker + "    env2:\n      K: v\n    outputs2: [a]\n"), encoding="utf-8")
        code, out = self.f.run()
        self.assertEqual(0, code, out)

    def test_missing_yaml_parser_fails_closed(self) -> None:
        with unittest.mock.patch.object(cred, "yaml", None):
            code, out = self.f.run(live=False)
        self.assertEqual(1, code, out)
        self.assertIn("W012", out)
        self.assertNotIn("CLOSED", out)

    def test_push_step_without_the_key_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        text = path.read_text(encoding="utf-8")
        old = ("      - name: Push to NuGet\n        env:\n"
               "          NUGET_OIDC_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n")
        self.assertIn(old, text)
        path.write_text(text.replace(old, "      - name: Push to NuGet\n"), encoding="utf-8")
        self.assert_codes({"W005"})
        # The push must read exactly the login output through env, and nothing else.
        for env, codes in (
            ("          NUGET_OIDC_KEY: ${{ steps.other.outputs.NUGET_API_KEY }}\n", {"W002", "W005", "W015"}),
            ("          NUGET_OIDC_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n          X: y\n", {"W005", "W015"}),
            ("          OTHER: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n", {"W005", "W015"}),
        ):
            with self.subTest(env=env):
                path.write_text(text.replace(old, "      - name: Push to NuGet\n        env:\n" + env), encoding="utf-8")
                self.assert_codes(codes)

    def test_flow_style_reusable_call_is_open(self) -> None:
        path = self.f.workflow("benchmark.yml")
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        text = path.read_text(encoding="utf-8")
        path.write_text(text.replace(marker, "  sneaky: {uses: someone/else/.github/workflows/x.yml@main}\n" + marker),
                        encoding="utf-8")
        self.assert_codes({"W011"})

    def test_flow_style_job_is_open(self) -> None:
        path = self.f.workflow("benchmark.yml")
        text = path.read_text(encoding="utf-8")
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        path.write_text(text.replace(marker, "  sneaky: {runs-on: ubuntu-latest, environment: nuget-release}\n" + marker),
                        encoding="utf-8")
        self.assert_codes({"W004"})

    def test_other_environments_are_not_flagged(self) -> None:
        # nextjs-gh-pages deploys into github-pages with an expression url; that is not this key.
        text = self.f.workflow("nextjs-gh-pages.yml").read_text(encoding="utf-8")
        self.assertIn("name: github-pages", text)
        code, out = self.f.run(live=False)
        self.assertEqual(0, code, out)

    def test_suppressed_moved_or_conditional_guard_is_open(self) -> None:
        guard = "      - name: Require the Trusted Publishing NuGet key (#1545)\n"
        path = self.f.workflow("publish-nuget.yml")
        original = path.read_text(encoding="utf-8")
        self.assertIn(guard, original)
        checkout = "      - name: Checkout the adjudicated candidate with full history\n"
        start = original.index(guard)
        end = original.index(checkout)
        moved = original[:start] + original[end:].replace(
            "      - name: Setup .NET\n", original[start:end] + "      - name: Setup .NET\n", 1)
        for mutant in (original.replace(guard, guard + "        continue-on-error: true\n"),
                       original.replace(guard, guard + "        if: false\n"),
                       original.replace(guard, guard + '        "if": false\n'),
                       original.replace(guard, guard + "        'continue-on-error': true\n"),
                       original[:start] + original[start:end].replace("exit 1", "exit 0") + original[end:],
                       original[:start] + original[start:end].replace(
                           '          if [ -z "$NUGET_OIDC_KEY" ]; then',
                           '          check_key() {\n          if [ -z "$NUGET_OIDC_KEY" ]; then', 1).replace(
                           "            exit 1\n          fi\n", "            exit 1\n          fi\n          }\n", 1)
                       + original[end:],
                       moved):
            with self.subTest(mutant=mutant[start:start + 200]):
                path.write_text(mutant, encoding="utf-8")
                # A moved guard also reads the login output from a step that is not the guard.
                self.assert_codes({"W007", "W015"} if mutant is moved else {"W007"})
        path.write_text(original, encoding="utf-8")

    def test_publish_job_must_start_with_the_pinned_login(self) -> None:
        # Trusted Publishing: the first step is exactly the NuGet/login step, pinned to the v1.2.0
        # commit, for the package owner, unconditional.
        login = ("      - name: Exchange the GitHub OIDC token for a short-lived NuGet key (#1545)\n"
                 "        id: nuget-login\n"
                 "        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0\n"
                 "        with:\n"
                 "          user: juanmicrosoft\n")
        path = self.f.workflow("publish-nuget.yml")
        original = path.read_text(encoding="utf-8")
        self.assertIn(login, original)
        sha = "8d196754b4036150537f80ac539e15c2f1028841 # v1.2.0"
        for mutant, codes in (
            (original.replace(sha, "v1"), {"W014"}),
            (original.replace(sha, "v1.2.0"), {"W014"}),
            (original.replace(sha, "0000000000000000000000000000000000000000"), {"W014"}),
            (original.replace(login, login.replace("NuGet/login@", "someone/login@")), {"W014"}),
            (original.replace(login, login.replace("user: juanmicrosoft", "user: someone-else")), {"W014"}),
            (original.replace(login, login.replace("user: juanmicrosoft", "user: ${{ vars.NUGET_USER }}")), {"W014"}),
            (original.replace(login, login + "          audience: https://example.invalid\n"), {"W014"}),
            (original.replace(login, login + "        continue-on-error: true\n"), {"W014"}),
            (original.replace(login, login + "        if: false\n"), {"W014"}),
            (original.replace(login, login.replace("        id: nuget-login\n", "")), {"W014"}),
            # Removed: the guard becomes the first step.
            (original.replace(login + "\n", ""), {"W014", "W007"}),
        ):
            with self.subTest(mutant=mutant[mutant.index("    steps:\n"):][:420]):
                path.write_text(mutant, encoding="utf-8")
                self.assert_codes(codes)
        path.write_text(original, encoding="utf-8")

    def test_publish_job_without_id_token_write_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        original = path.read_text(encoding="utf-8")
        grant = "      id-token: write\n      attestations: write\n"
        self.assertIn(grant, original)
        for mutant in (original.replace(grant, "      attestations: write\n"),
                       original.replace(grant, "      id-token: read\n      attestations: write\n"),
                       original.replace(grant, "      id-token: none\n      attestations: write\n")):
            with self.subTest(mutant=mutant):
                path.write_text(mutant, encoding="utf-8")
                self.assert_codes({"W013"})
        path.write_text(original, encoding="utf-8")

    def test_login_output_used_elsewhere_in_the_publish_job_is_open(self) -> None:
        # Only the guard and the push may read the key; the build steps run candidate code.
        path = self.f.workflow("publish-nuget.yml")
        original = path.read_text(encoding="utf-8")
        build = "      - name: Build\n        run: dotnet build src/Calor.Compiler/Calor.Compiler.csproj"
        self.assertEqual(1, original.count(build))
        build = build.split("\n")[0] + "\n"
        original_split = original.split(build)
        self.assertEqual(3, len(original_split))  # the test job's Build, then the publish job's

        def at_publish_build(text: str) -> str:
            return build.join(original_split[:2]) + build + text + original_split[2]
        for injected, codes in (
            ("        env:\n          K: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n", {"W015"}),
            ("        env:\n          K: ${{ steps.nuget-login.outputs.nuget_api_key }}\n", {"W002", "W015"}),
            ("        env:\n          K: ${{ steps.nuget-login.outputs['NUGET_API_KEY'] }}\n", {"W002", "W015"}),
        ):
            with self.subTest(injected=injected):
                path.write_text(at_publish_build(injected), encoding="utf-8")
                self.assert_codes(codes)
        # A second login step, or the output at job level.
        second = ("      - name: Again\n        uses: NuGet/login@8d196754b4036150537f80ac539e15c2f1028841\n"
                  "        with:\n          user: juanmicrosoft\n")
        path.write_text(build.join(original_split[:2]) + second + build + original_split[2], encoding="utf-8")
        self.assert_codes({"W015"})
        job = "    environment: nuget-release\n"
        path.write_text(original.replace(job, job + "    outputs:\n      k: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n"),
                        encoding="utf-8")
        self.assert_codes({"W015"})
        top_env = "\nenv:\n  ADJUDICATION_IDENTITY:"
        self.assertEqual(1, original.count(top_env))
        path.write_text(original.replace(top_env, "\nenv:\n  K: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}\n"
                                                  "  ADJUDICATION_IDENTITY:"), encoding="utf-8")
        self.assert_codes({"W002", "W008"})
        path.write_text(original, encoding="utf-8")

    def test_commented_environment_does_not_count(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        path.write_text(path.read_text(encoding="utf-8").replace(
            "    environment: nuget-release\n", "    # environment: nuget-release\n"), encoding="utf-8")
        self.assert_codes({"W006"})

    def test_check_never_writes(self) -> None:
        # Every API call is an explicit GET.
        source = (REPO_ROOT / "scripts" / "check_release_credentials.py").read_text(encoding="utf-8")
        self.assertIn('"gh", "api", "--method", "GET"', source)
        for verb in ("POST", "PUT", "PATCH", "DELETE"):
            self.assertNotIn(f'"--method", "{verb}"', source)
            self.assertNotIn(f"-X {verb}", source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
