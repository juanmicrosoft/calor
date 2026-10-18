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
    "environment-secrets": secrets("NUGET_RELEASE_API_KEY"),
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
        (self.responses / f"{key}.json").write_text(json.dumps(body), encoding="utf-8")

    def remove(self, key: str) -> None:
        (self.responses / f"{key}.json").unlink()

    def unreadable(self, key: str) -> None:
        self.remove(key)
        (self.responses / f"{key}.error").write_text("HTTP 403: Resource not accessible", encoding="utf-8")

    def workflow(self, name: str) -> Path:
        return self.repo / ".github" / "workflows" / name

    def run(self, live: bool = True) -> tuple[int, str]:
        out = io.StringIO()
        args = ["--repo", str(self.repo)] + (["--from-dir", str(self.responses)] if live else [])
        with contextlib.redirect_stdout(out):
            code = cred.main(args)
        return code, out.getvalue()


class CredentialCheckTests(unittest.TestCase):
    def setUp(self) -> None:
        self.f = Fixture()

    def tearDown(self) -> None:
        shutil.rmtree(self.f.base, ignore_errors=True)

    def assert_codes(self, expected: set[str], verdict: str = "OPEN") -> None:
        code, out = self.f.run()
        found = set(re.findall(r"\(#1545\) ([WSU]\d{3}):", out))
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

    def test_environment_without_the_secret_is_open(self) -> None:
        self.f.write("environment-secrets", secrets("NUGET_API_KEY"))
        self.assert_codes({"S006"})

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
                          ("branch-policies", "U003"), ("environment-secrets", "U004")):
            with self.subTest(key=key):
                fixture = Fixture()
                try:
                    self.f, saved = fixture, self.f
                    self.f.unreadable(key)
                    self.assert_codes({code}, verdict="UNKNOWN")
                finally:
                    self.f = saved
                    shutil.rmtree(fixture.base, ignore_errors=True)

    # -- workflows --------------------------------------------------------------------------

    def test_workflow_naming_the_old_secret_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        path.write_text(path.read_text(encoding="utf-8").replace(
            "secrets.NUGET_RELEASE_API_KEY }}\n        run: dotnet nuget push",
            "secrets.NUGET_API_KEY }}\n        run: dotnet nuget push"), encoding="utf-8")
        self.assert_codes({"W002", "W005"})

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
        self.assert_codes({"W003", "W004"})

    def test_other_syntaxes_for_the_key_or_environment_are_open(self) -> None:
        marker = "  benchmark:\n    runs-on: ubuntu-latest\n"
        for injected, codes in (
            ("    environment:\n      name: nuget-release\n", {"W004"}),
            ("    environment: { name: nuget-release }\n", {"W004"}),
            ("    environment: ${{ inputs.env }}\n", {"W004"}),
            ("    environment:\n      name: ${{ format('nuget-{0}', 'release') }}\n", {"W004"}),
            ("    environment:\n      url: https://example.invalid\n", {"W004"}),
            ("    env:\n      K: ${{ secrets['NUGET_RELEASE_API_KEY'] }}\n", {"W003"}),
            # Round 2: names are case-insensitive in Actions, and YAML escapes decode.
            ("    environment: NUGET-RELEASE\n", {"W004"}),
            ('    environment: "nuget-\\u0072elease"\n', {"W004"}),
            ("    'environment': nuget-release\n", {"W004"}),
            ("    env:\n      K: ${{ secrets.nuget_release_api_key }}\n", {"W003"}),
            ('    env:\n      K: "${{ secrets.NUGET_\\u0052ELEASE_API_KEY }}"\n', {"W003"}),
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

    def test_missing_yaml_parser_fails_closed(self) -> None:
        with unittest.mock.patch.object(cred, "yaml", None):
            code, out = self.f.run(live=False)
        self.assertEqual(1, code, out)
        self.assertIn("W012", out)
        self.assertNotIn("CLOSED", out)

    def test_push_step_without_the_key_is_open(self) -> None:
        path = self.f.workflow("publish-nuget.yml")
        text = path.read_text(encoding="utf-8")
        old = ("      - name: Push to NuGet\n        env:\n          NUGET_RELEASE_API_KEY: ${{ secrets.NUGET_RELEASE_API_KEY }}\n")
        self.assertIn(old, text)
        path.write_text(text.replace(old, "      - name: Push to NuGet\n"), encoding="utf-8")
        self.assert_codes({"W005"})

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
        guard = "      - name: Require the environment-scoped NuGet key (#1545)\n"
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
                           '          if [ -z "$NUGET_RELEASE_API_KEY" ]; then',
                           '          check_key() {\n          if [ -z "$NUGET_RELEASE_API_KEY" ]; then', 1).replace(
                           "            exit 1\n          fi\n", "            exit 1\n          fi\n          }\n", 1)
                       + original[end:],
                       moved):
            with self.subTest(mutant=mutant[start:start + 200]):
                path.write_text(mutant, encoding="utf-8")
                self.assert_codes({"W007"})
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
