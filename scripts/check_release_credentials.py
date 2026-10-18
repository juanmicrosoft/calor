#!/usr/bin/env python3
"""Read-only check: can any workflow but the gated publish job reach the NuGet key? (#1545)

Older tags hold an ungated publish-nuget.yml that reads the repository secret NUGET_API_KEY, and
`workflow_dispatch` or `gh run rerun` can run those files. The fix is a repository setting, which
only the maintainer may change: the key moves into the `nuget-release` environment, whose
deployment policy admits `main` only, under a name no older workflow uses (NUGET_RELEASE_API_KEY).
publish-nuget.yml on main names that environment and that secret.

This script reports whether the gap is closed. It changes nothing: every GitHub API call it makes
is an explicit `gh api --method GET`.

  * Workflow check (always, offline): no workflow names NUGET_API_KEY; only the `publish` job of
    publish-nuget.yml names NUGET_RELEASE_API_KEY, that job declares `environment: nuget-release`,
    and no other job declares that environment.
  * Settings check (`--live`, or `--from-dir` with saved API responses for tests):
      - no repository-level Actions secret whose name contains NUGET;
      - the environment exists, admins cannot bypass it, and its deployment policy is custom
        branch policies (not "protected branches") holding exactly one rule: branch `main`;
      - the environment holds the secret NUGET_RELEASE_API_KEY.
    Listing secrets needs a token with admin rights on the repository (the maintainer's own
    `gh` login). A response the script cannot read is UNKNOWN, never CLOSED.

Exit 0 only when every check passes (CLOSED). Exit 1 when a check fails (OPEN) or cannot be
completed (UNKNOWN). Maintainer steps: docs/plans/evidence/release-gaps-0.25/README.md.
Tests: scripts/test_check_release_credentials.py.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Callable

REPO = "juanmicrosoft/calor"
ENVIRONMENT = "nuget-release"
SECRET = "NUGET_RELEASE_API_KEY"
OLD_SECRET = "NUGET_API_KEY"
PUBLISH_WORKFLOW = "publish-nuget.yml"
PUBLISH_JOB = "publish"

# Relative API paths (after repos/<owner>/<repo>/) and the file names used by --from-dir.
ENDPOINTS = {
    "repo-secrets": "actions/secrets",
    "environment": f"environments/{ENVIRONMENT}",
    "branch-policies": f"environments/{ENVIRONMENT}/deployment-branch-policies",
    "environment-secrets": f"environments/{ENVIRONMENT}/secrets",
}


class Unreadable(Exception):
    """The API answered with something other than the data (403, network error, bad JSON)."""


class NotFound(Exception):
    """The API answered 404."""


def split_jobs(lines: list[str]) -> tuple[dict[str, list[str]], list[str]]:
    """Block-style jobs under the top-level `jobs:` key (two-space job headers), and every other
    line of the file (top-level keys, and anything in `jobs:` that is not a block-style job)."""
    found: dict[str, list[str]] = {}
    outside: list[str] = []
    current = None
    inside = False
    for line in lines:
        if not inside:
            if line.rstrip() == "jobs:":
                inside = True
            else:
                outside.append(line)
            continue
        if line and not line.startswith((" ", "#")):
            inside, current = False, None
            outside.append(line)
            continue
        header = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", line)
        if header:
            current = header.group(1)
            found[current] = []
        elif current is not None and (not line.strip() or line.startswith("   ")):
            found[current].append(line)
        else:
            outside.append(line)
    return found, outside


def jobs(lines: list[str]) -> dict[str, list[str]]:
    return split_jobs(lines)[0]


GUARD_STEP = "Require the environment-scoped NuGet key (#1545)"
PUBLISH_ENVIRONMENT = f"    environment: {ENVIRONMENT}"
# Every other environment declaration on main, verbatim (whitespace-normalized). The scan does not
# parse YAML; it accepts only declarations it has seen, so case changes, escapes, quoting, flow
# mappings, anchors, and expressions all fail instead of being interpreted.
OTHER_ENVIRONMENTS = {
    "environment: name: github-pages url: ${{ steps.deployment.outputs.page_url }}",
}
ESCAPE = re.compile(r"\\(?:x([0-9A-Fa-f]{2})|u([0-9A-Fa-f]{4})|U([0-9A-Fa-f]{8}))")
# A possible `environment` key after escapes are decoded and case is folded: the word, an optional
# quote, and a colon; or a complex key `? environment` alone on its line (its colon is on the
# next line). Prose such as "the nuget-release environment (#1545)" does not match.
ENV_WORD = re.compile(r"""(?<![a-z0-9_-])environment["']?[ \t]*:|^\s*\?[ \t]*["']?environment["']?\s*$""")
# A possible `uses` key (decoded, case-folded), and the only job-level form accepted.
USES_KEY = re.compile(r"""(?<![a-z0-9_-])uses["']?[ \t]*:|^\s*\?[ \t]*["']?uses["']?\s*$""")
LOCAL_USES = re.compile(r"^\s+uses: \./\.github/workflows/[A-Za-z0-9._/-]+\.ya?ml\s*$")
# The publish job's first step, verbatim. Only this exact script counts as the guard.
GUARD_LINES = [
    "      - name: Require the environment-scoped NuGet key (#1545)",
    "        env:",
    "          NUGET_RELEASE_API_KEY: ${{ secrets.NUGET_RELEASE_API_KEY }}",
    "        run: |",
    '          if [ -z "$NUGET_RELEASE_API_KEY" ]; then',
    '            echo "::error::NUGET_RELEASE_API_KEY is not available; create it in the nuget-release environment (#1545)"',
    "            exit 1",
    "          fi",
]
ANCHOR = re.compile(r"(?:^|[\s:\-\[{,])[&*][A-Za-z0-9_]|^\s*<<\s*:")


def code_lines(lines: list[str]) -> list[str]:
    return [line for line in lines if not line.lstrip().startswith("#")]


def decoded(text: str) -> str:
    """YAML escapes decoded, escaped line breaks joined, case folded."""
    text = re.sub(r"\\\n\s*", "", text)
    return ESCAPE.sub(lambda m: chr(int(next(g for g in m.groups() if g), 16)), text).casefold()


def folded(text: str) -> str:
    """For name searches: decoded, with all whitespace, quotes, and backslashes removed."""
    return re.sub(r"""[\s"'\\]""", "", decoded(text))


def environment_keys(lines: list[str]) -> int:
    return sum(1 for line in lines if ENV_WORD.search(decoded(line)))


def environment_declarations(body: list[str]) -> list[str]:
    """Every possible `environment` key in a job (any indentation, quoting, escape, or case), with
    its continuation lines, whitespace-normalized."""
    found = []
    for i, line in enumerate(body):
        if ENV_WORD.search(decoded(line)):
            indent = len(line) - len(line.lstrip())
            decl = [line.strip()]
            for more in body[i + 1:]:
                if more.strip() and len(more) - len(more.lstrip()) <= indent:
                    break
                decl.append(more.strip())
            found.append(" ".join(part for part in decl if part))
    return found


def first_step(body: list[str]) -> list[str]:
    starts = [i for i, line in enumerate(body) if re.match(r"^      - ", line)]
    if not starts:
        return []
    end = starts[1] if len(starts) > 1 else len(body)
    return [line for line in body[starts[0]:end] if line.strip()]


def guard_is_sound(step: list[str]) -> bool:
    """The first step is exactly GUARD_LINES: no added keys (`if`, `continue-on-error`, quoted or
    not) and no edited script (an uncalled function or `exit 0` changes the text)."""
    return [line.rstrip() for line in step] == GUARD_LINES


def check_workflows(workflow_dir: Path) -> list[str]:
    """Conservative text checks. Entering an environment is the only way to read an environment
    secret, so every environment declaration must be the publish job's exact line or one of
    OTHER_ENVIRONMENTS; anything else is reported, not interpreted. Mentions of a NuGet secret
    outside the publish job are reported in any case, quoting, or escape (defense in depth)."""
    problems: list[str] = []
    files = sorted(workflow_dir.glob("*.yml")) + sorted(workflow_dir.glob("*.yaml"))
    if not any(f.name == PUBLISH_WORKFLOW for f in files):
        return [f"W001: {PUBLISH_WORKFLOW} is missing from {workflow_dir}"]
    publish_ok = False
    for path in files:
        lines = code_lines(path.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n"))
        if folded(OLD_SECRET) in folded("\n".join(lines)):
            problems.append(f"W002: {path.name} names {OLD_SECRET}; older workflow versions read that secret")
        for line in lines:
            if ANCHOR.search(line.split(" #", 1)[0]) and not re.search(r"""["'][^"']*[&*][^"']*["']""", line):
                problems.append(f"W009: {path.name} uses a YAML anchor, alias, or merge key: '{line.strip()}'")
        found, outside = split_jobs(lines)
        # Backstop: the whole file, with escaped line breaks joined, may hold no more possible
        # environment keys than the per-line scan below sees.
        whole = len(re.findall(r"""(?<![a-z0-9_-])environment["'\s]*:""", decoded("\n".join(lines))))
        if whole > environment_keys(lines):
            problems.append(f"W010: {path.name} has an environment key split across lines")
        # EXC-RELEASE-GAPS-A-CHECKER: a job-level `uses` key (a reusable-workflow call) is
        # accepted only as `uses: ./.github/workflows/<file>.yml` on one line; any other form
        # (another repository, a folded `>-` value, a quoted, escaped, or flow-style key) fails.
        for job, body in found.items():
            content = [line for line in body if line.strip()]
            indent = len(content[0]) - len(content[0].lstrip()) if content else 0
            for line in content:
                if len(line) - len(line.lstrip()) == indent and USES_KEY.search(decoded(line)) \
                        and not LOCAL_USES.match(line):
                    problems.append(f"W011: {path.name} job '{job}' calls a workflow other than a local "
                                    f"./.github/workflows/ file on one line: '{line.strip()}'")
        for line in outside:
            if USES_KEY.search(decoded(line)):
                problems.append(f"W011: {path.name} has a `uses` key outside a parsed job: '{line.strip()}'")
        for job, body in found.items():
            is_publish = path.name == PUBLISH_WORKFLOW and job == PUBLISH_JOB
            decls = environment_declarations(body)
            if is_publish:
                publish_ok = decls == [PUBLISH_ENVIRONMENT.strip()] and PUBLISH_ENVIRONMENT in body
                if not guard_is_sound(first_step(body)):
                    problems.append(f"W007: the first step of {PUBLISH_WORKFLOW} job '{PUBLISH_JOB}' must be the "
                                    f"unconditional '{GUARD_STEP}' check (keys name, env, run only)")
                continue
            if folded(SECRET) in folded("\n".join(body)):
                problems.append(f"W003: {path.name} job '{job}' names {SECRET}; only the gated publish job may")
            for decl in decls:
                if decl not in OTHER_ENVIRONMENTS:
                    problems.append(f"W004: {path.name} job '{job}' declares an environment the check does not "
                                    f"recognize (it may be {ENVIRONMENT}): '{decl}'")
        # Outside parsed jobs (flow-style jobs, top-level keys) nothing may declare an environment
        # or mention the key.
        for line in outside:
            if "environment" in folded(line) or folded(SECRET) in folded(line):
                problems.append(f"W008: {path.name} declares an environment or names {SECRET} outside a parsed job: "
                                f"'{line.strip()}'")
    if not publish_ok:
        problems.append(f"W006: {PUBLISH_WORKFLOW} job '{PUBLISH_JOB}' must declare exactly `{PUBLISH_ENVIRONMENT.strip()}`")
    return problems


LISTS = {"repo-secrets": "secrets", "environment-secrets": "secrets", "branch-policies": "branch_policies"}


def gh_fetch(repo: str) -> Callable[[str], object]:
    def get(path: str) -> object:
        result = subprocess.run(
            ["gh", "api", "--method", "GET", "-H", "Accept: application/vnd.github+json", path],
            capture_output=True, text=True,
        )
        if result.returncode != 0:
            text = result.stdout + result.stderr
            if "HTTP 404" in text:
                raise NotFound(path)
            raise Unreadable(f"{path}: {text.strip()[:300]}")
        try:
            return json.loads(result.stdout)
        except ValueError as error:
            raise Unreadable(f"{path}: {error}") from error

    def fetch(key: str) -> object:
        path = f"repos/{repo}/{ENDPOINTS[key]}"
        if key not in LISTS:
            return get(path)
        # Every page, 100 at a time, until total_count entries are collected; listed() then
        # refuses any response whose entries do not add up to total_count.
        field, items, total = LISTS[key], [], None
        for page in range(1, 101):
            body = get(f"{path}?per_page=100&page={page}")
            if not isinstance(body, dict) or not isinstance(body.get(field), list):
                raise Unreadable(f"{path}: no {field} list")
            total = body.get("total_count")
            items += body[field]
            if not body[field] or not isinstance(total, int) or len(items) >= total:
                break
        return {"total_count": total, field: items}
    return fetch


def dir_fetch(root: Path) -> Callable[[str], object]:
    """Saved responses: <key>.json holds the body; a missing file means 404; <key>.error is unreadable."""
    def fetch(key: str) -> object:
        if (root / f"{key}.error").exists():
            raise Unreadable(f"{key}: {(root / f'{key}.error').read_text(encoding='utf-8').strip()}")
        path = root / f"{key}.json"
        if not path.exists():
            raise NotFound(key)
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except ValueError as error:
            raise Unreadable(f"{key}: {error}") from error
    return fetch


def listed(body: object, field: str, what: str) -> list[dict]:
    """The complete list, or Unreadable: a page short of total_count is never read as complete."""
    if not isinstance(body, dict) or not isinstance(body.get(field), list):
        raise Unreadable(f"{what}: no {field} list")
    items = body[field]
    if body.get("total_count") != len(items) or not all(isinstance(i, dict) for i in items):
        raise Unreadable(f"{what}: {len(items)} entries listed, total_count is {body.get('total_count')!r}")
    return items


def names(body: object, what: str) -> list[str]:
    return [str(s.get("name")) for s in listed(body, "secrets", what)]


def check_settings(fetch: Callable[[str], object]) -> tuple[list[str], list[str]]:
    """(problems, unknowns)"""
    problems: list[str] = []
    unknowns: list[str] = []
    try:
        repo_level = [n for n in names(fetch("repo-secrets"), "repository secrets") if "NUGET" in n.upper()]
        if repo_level:
            problems.append(f"S001: repository-level secret(s) {sorted(repo_level)} are readable by every ref, "
                            "including older tags; delete them")
    except (Unreadable, NotFound) as error:
        unknowns.append(f"U001: cannot list repository secrets ({error}); run with the maintainer's admin login")

    try:
        env = fetch("environment")
    except NotFound:
        problems.append(f"S002: environment {ENVIRONMENT} does not exist")
        return problems, unknowns
    except Unreadable as error:
        unknowns.append(f"U002: cannot read environment {ENVIRONMENT} ({error})")
        return problems, unknowns
    if not isinstance(env, dict):
        unknowns.append(f"U002: environment {ENVIRONMENT} response is not an object")
        return problems, unknowns
    if env.get("can_admins_bypass") is not False:
        problems.append(f"S003: administrators can bypass {ENVIRONMENT}'s protection rules; turn that off")
    policy = env.get("deployment_branch_policy")
    if not isinstance(policy, dict) or policy.get("custom_branch_policies") is not True \
            or policy.get("protected_branches") is not False:
        problems.append(f"S004: {ENVIRONMENT} must use 'Selected branches and tags' (custom branch policies), "
                        f"got {json.dumps(policy)}")
    else:
        try:
            rules = listed(fetch("branch-policies"), "branch_policies", "deployment branch policies")
            found = sorted((str(r.get("name")), str(r.get("type", "branch"))) for r in rules)
            if found != [("main", "branch")]:
                problems.append(f"S005: {ENVIRONMENT} must admit exactly the branch 'main', got {found}")
        except (Unreadable, NotFound) as error:
            unknowns.append(f"U003: cannot read {ENVIRONMENT}'s deployment branch policies ({error})")
    try:
        if SECRET not in names(fetch("environment-secrets"), "environment secrets"):
            problems.append(f"S006: environment {ENVIRONMENT} has no secret {SECRET}")
    except (Unreadable, NotFound) as error:
        unknowns.append(f"U004: cannot list {ENVIRONMENT}'s secrets ({error})")
    return problems, unknowns


def run(args: argparse.Namespace) -> int:
    problems = check_workflows(Path(args.repo) / ".github" / "workflows")
    unknowns: list[str] = []
    if args.from_dir or args.live:
        fetch = dir_fetch(Path(args.from_dir)) if args.from_dir else gh_fetch(args.github_repo)
        found, unknowns = check_settings(fetch)
        problems += found
    for line in problems:
        print(f"::error::release credentials (#1545) {line}")
    for line in unknowns:
        print(f"::warning::release credentials (#1545) {line}")
    if problems:
        print(f"RELEASE CREDENTIALS: OPEN ({len(problems)} problem(s)); older tags may still reach the NuGet key.")
        return 1
    if unknowns:
        print(f"RELEASE CREDENTIALS: UNKNOWN ({len(unknowns)} setting(s) unreadable).")
        return 1
    scope = "workflows and repository settings" if (args.from_dir or args.live) else "workflows only (pass --live for settings)"
    print(f"RELEASE CREDENTIALS: CLOSED ({scope}).")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent), help="repository checkout")
    parser.add_argument("--github-repo", default=REPO, help="owner/name for --live")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--live", action="store_true", help="also read the settings through `gh api --method GET`")
    source.add_argument("--from-dir", help="also read the settings from saved API responses (tests)")
    return run(parser.parse_args(argv))


if __name__ == "__main__":
    sys.exit(main())
