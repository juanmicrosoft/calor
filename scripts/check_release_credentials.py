#!/usr/bin/env python3
"""Read-only check: can any workflow but the gated publish job reach the NuGet key? (#1545)

Older tags hold an ungated publish-nuget.yml that reads the repository secret NUGET_API_KEY, and
`workflow_dispatch` or `gh run rerun` can run those files. The fix is a repository setting, which
only the maintainer may change: the key moves into the `nuget-release` environment, whose
deployment policy admits `main` only, under a name no older workflow uses (NUGET_RELEASE_API_KEY).
publish-nuget.yml on main names that environment and that secret.

This script reports whether the gap is closed. It changes nothing: every GitHub API call it makes
is an explicit `gh api --method GET`.

  * Workflow check (always, offline), on the parsed YAML (PyYAML, pinned in
    eng/release-credentials-requirements.txt; duplicate keys and parse failures fail closed): no
    workflow names NUGET_API_KEY; only the `publish` job of publish-nuget.yml names
    NUGET_RELEASE_API_KEY, that job declares `environment: nuget-release` and starts with the
    exact empty-key guard, no other job declares an unrecognized environment, and every job-level
    `uses` is a local workflow file (codes: check_workflows).
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

try:  # pinned in eng/release-credentials-requirements.txt; missing means W012 (fail closed)
    import yaml
except ImportError:  # pragma: no cover - exercised by the tests through monkeypatching
    yaml = None

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


GUARD_STEP = "Require the environment-scoped NuGet key (#1545)"
SECRET_REF = "${{ secrets.NUGET_RELEASE_API_KEY }}"
# The publish job's first step, as parsed. Only this exact mapping counts as the guard.
GUARD = {
    "name": GUARD_STEP,
    "env": {SECRET: SECRET_REF},
    "run": ('if [ -z "$NUGET_RELEASE_API_KEY" ]; then\n'
            '  echo "::error::NUGET_RELEASE_API_KEY is not available; create it in the nuget-release environment (#1545)"\n'
            "  exit 1\n"
            "fi\n"),
}
# Every other job-level environment on main, as parsed. Anything else is reported.
OTHER_ENVIRONMENTS = [
    {"name": "github-pages", "url": "${{ steps.deployment.outputs.page_url }}"},
]
LOCAL_WORKFLOW = re.compile(r"\./\.github/workflows/[A-Za-z0-9._-]+\.ya?ml")


class ParseFailure(Exception):
    pass



def strict_loader():
    """yaml.SafeLoader that rejects duplicate keys (also keys equal under case folding)."""
    if yaml is None:
        raise ParseFailure("PyYAML is not installed; pip install --require-hashes -r eng/release-credentials-requirements.txt")

    class StrictLoader(yaml.SafeLoader):
        pass

    def mapping(loader, node):
        seen = set()
        for key_node, _ in node.value:
            key = loader.construct_object(key_node, deep=True)
            folded_key = key.casefold() if isinstance(key, str) else key
            try:
                duplicate = folded_key in seen
            except TypeError as error:
                raise ParseFailure(f"unhashable mapping key at {key_node.start_mark}") from error
            if duplicate:
                raise ParseFailure(f"duplicate key {key!r} at {key_node.start_mark}")
            seen.add(folded_key)
        return yaml.SafeLoader.construct_mapping(loader, node, deep=True)

    StrictLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, mapping)
    return StrictLoader


def load_workflow(text: str) -> tuple[object, bool]:
    """(document, uses anchors/aliases/merge keys). Any parse problem raises ParseFailure."""
    loader = strict_loader()
    try:
        events = list(yaml.parse(text, Loader=loader))
        # #1567: an explicit tag changes what a node is built as. A tagged collection (`!!set`,
        # `!!omap`, `!!pairs`) skips the strict mapping constructor's duplicate-key check, and a
        # tagged scalar (`!!null`, `!!binary`) can erase the text the checks look for. Workflows
        # need no tags, so any explicit tag, on any node, fails closed.
        for event in events:
            if isinstance(event, (yaml.CollectionStartEvent, yaml.ScalarEvent)) and event.tag is not None:
                raise ParseFailure(f"explicit YAML tag {event.tag!r} at {event.start_mark}")
        anchored = any(isinstance(e, yaml.AliasEvent) or getattr(e, "anchor", None) for e in events) \
            or any(isinstance(e, yaml.ScalarEvent) and e.value == "<<" and e.style is None for e in events)
        documents = list(yaml.load_all(text, Loader=loader))
    except ParseFailure:
        raise
    except yaml.YAMLError as error:
        raise ParseFailure(str(error).replace("\n", " ")) from error
    if len(documents) != 1 or not isinstance(documents[0], dict):
        raise ParseFailure("a workflow must be exactly one YAML mapping")
    return documents[0], anchored


def strings(value: object):
    """Every string in a parsed value, keys included."""
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for key, item in value.items():
            yield from strings(key)
            yield from strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from strings(item)


def mentions(value: object, needle: str) -> bool:
    return any(needle.casefold() in text.casefold() for text in strings(value))


def keyed(mapping: dict, name: str) -> list[object]:
    """Values under keys equal to `name` ignoring case (duplicates were already rejected)."""
    return [v for k, v in mapping.items() if isinstance(k, str) and k.casefold() == name]


def check_workflows(workflow_dir: Path) -> list[str]:
    """Decide on the parsed structure (PyYAML, duplicate keys rejected), never on text. Entering
    an environment is the only way to read an environment secret, so every job-level
    environment must be the publish job's exact `nuget-release` or one of OTHER_ENVIRONMENTS, and
    every job-level `uses` must be a local ./.github/workflows/ file with no @ref (its own jobs
    are checked here too). Codes: W001 publish workflow missing; W002 a workflow names
    NUGET_API_KEY; W003 another job names the key; W004 another job declares an unrecognized
    environment; W005 the publish job's push step does not read the key; W006 the publish job's
    environment is not exactly nuget-release; W007 the publish job's first step is not the guard;
    W008 the key or environment appears outside a job, or jobs are not mappings; W009 anchors,
    aliases, or merge keys; W011 a job-level `uses` other than a local workflow file; W012 a
    workflow cannot be parsed, has a duplicate key, or carries an explicit YAML tag on any node
    (`!!set` would skip the duplicate-key check, `!!null` would erase a value). (W010, a key split across lines, is retired:
    the parser decodes those.)"""
    problems: list[str] = []
    files = sorted(workflow_dir.glob("*.yml")) + sorted(workflow_dir.glob("*.yaml"))
    if not any(f.name == PUBLISH_WORKFLOW for f in files):
        return [f"W001: {PUBLISH_WORKFLOW} is missing from {workflow_dir}"]
    local_files = {f.name for f in files}
    publish_seen = False
    for path in files:
        try:
            doc, anchored = load_workflow(path.read_text(encoding="utf-8"))
        except ParseFailure as error:
            problems.append(f"W012: {path.name} cannot be checked: {error}")
            continue
        if anchored:
            problems.append(f"W009: {path.name} uses a YAML anchor, alias, or merge key")
        if mentions(doc, OLD_SECRET):
            problems.append(f"W002: {path.name} names {OLD_SECRET}; older workflow versions read that secret")
        jobs_values = keyed(doc, "jobs")
        outside = {k: v for k, v in doc.items() if not (isinstance(k, str) and k.casefold() == "jobs")}
        if mentions(outside, SECRET) or mentions(outside, ENVIRONMENT):
            problems.append(f"W008: {path.name} names {SECRET} or {ENVIRONMENT} outside a job")
        jobs = jobs_values[0] if jobs_values else {}
        if not isinstance(jobs, dict) or not all(isinstance(j, dict) for j in jobs.values()):
            problems.append(f"W008: {path.name} has jobs that are not mappings")
            continue
        for job, body in jobs.items():
            is_publish = path.name == PUBLISH_WORKFLOW and job == PUBLISH_JOB
            for uses in keyed(body, "uses"):
                if not (isinstance(uses, str) and LOCAL_WORKFLOW.fullmatch(uses)
                        and uses.rsplit("/", 1)[1] in local_files):
                    problems.append(f"W011: {path.name} job '{job}' calls {uses!r}; only a local "
                                    "./.github/workflows/<file>.yml with no @ref is allowed")
            environments = keyed(body, "environment")
            if is_publish:
                publish_seen = True
                if environments != [ENVIRONMENT]:
                    problems.append(f"W006: {PUBLISH_WORKFLOW} job '{PUBLISH_JOB}' must declare exactly "
                                    f"`environment: {ENVIRONMENT}`")
                steps = (keyed(body, "steps") or [None])[0]
                if not isinstance(steps, list) or not steps or steps[0] != GUARD:
                    problems.append(f"W007: the first step of {PUBLISH_WORKFLOW} job '{PUBLISH_JOB}' must be "
                                    f"exactly the '{GUARD_STEP}' check")
                pushes = [step for step in (steps or [])[1:] if isinstance(step, dict)
                          and "dotnet nuget push" in str(step.get("run", ""))
                          and isinstance(step.get("env"), dict) and SECRET_REF in step["env"].values()]
                if not pushes:
                    problems.append(f"W005: {PUBLISH_WORKFLOW} job '{PUBLISH_JOB}' has no `dotnet nuget push` "
                                    f"step that reads {SECRET} through env")
                continue
            if mentions(body, SECRET):
                problems.append(f"W003: {path.name} job '{job}' names {SECRET}; only the gated publish job may")
            for environment in environments:
                if environment not in OTHER_ENVIRONMENTS or mentions(environment, ENVIRONMENT):
                    problems.append(f"W004: {path.name} job '{job}' declares an environment the check does not "
                                    f"recognize (it may be {ENVIRONMENT}): {environment!r}")
    if not publish_seen and not any(p.startswith("W012: " + PUBLISH_WORKFLOW) for p in problems):
        problems.append(f"W006: {PUBLISH_WORKFLOW} has no job '{PUBLISH_JOB}'")
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
