#!/usr/bin/env python3
"""Read-only check: can anything but the gated publish job get a NuGet key? (#1545)

Older tags hold an ungated publish-nuget.yml that read the repository secret NUGET_API_KEY, and
`workflow_dispatch` or `gh run rerun` can run those files. The fix (maintainer decision
2026-10-19) is nuget.org Trusted Publishing: no NuGet key is stored in GitHub. The `publish` job
of publish-nuget.yml on main enters the `nuget-release` environment, and the pinned NuGet/login
action exchanges that job's GitHub OIDC token for a one-hour key.

Why older refs cannot get a key. nuget.org's policy check (NuGetGallery
GitHubTokenPolicyValidator.EvaluatePolicyAsync) requires the token's `repository_owner` and
`repository` to match the policy (and, once the policy has been used, `repository_owner_id` and
`repository_id`). It takes the workflow FILE NAME from the `job_workflow_ref` claim and ignores
the ref after `@`. When the policy names an environment, the `environment` claim must equal it
(compared without case); a token with no environment claim does not match. The policy
"calor-release" names publish-nuget.yml and nuget-release. So:
  * an older tag's publish-nuget.yml names no environment; its token has no environment claim and
    is refused, even though the file name matches;
  * a ref other than `main` that names the environment is stopped by GitHub before the job starts,
    because the environment admits branch `main` alone and administrators cannot bypass it;
  * a workflow with another file name never matches, on any ref.
The environment and its branch rule are therefore load-bearing; nuget.org does not check the ref.

This script reports whether that holds. It changes nothing: every GitHub API call it makes is an
explicit `gh api --method GET`. It cannot see nuget.org, so it cannot confirm the policy or that
the old key was revoked.

  * Workflow check (always, offline), on the parsed YAML (PyYAML, pinned in
    eng/release-credentials-requirements.txt; duplicate keys, explicit tags, and parse failures
    fail closed). Codes: check_workflows.
  * Settings check (`--live`, or `--from-dir` with saved API responses for tests):
      - S001: a repository-level Actions secret whose name contains NUGET exists;
      - S002: the environment is missing; S003: admins can bypass it; S004: its deployment policy
        is not custom branch policies; S005: its rules are not exactly branch `main`;
      - S007: an environment, nuget-release or any other, holds a secret whose name contains
        NUGET;
      - S008: Actions' default workflow token is not read-only; S009: Actions may create or
        approve pull requests.
    (S006, "the environment lacks NUGET_RELEASE_API_KEY", is retired: no key is stored now.)
    Listing secrets needs a token with admin rights on the repository (the maintainer's own
    `gh` login). A response the script cannot read is UNKNOWN (U001-U005), never CLOSED.
    Reading the workflow permissions also needs the "Actions policies" permission; a plain login
    gets HTTP 403. A 403 there is UNVERIFIABLE (V001), never CLOSED, unless
    --attest-workflow-permissions names a valid maintainer attestation (exact schema:
    load_attestation). Then the output says ATTESTED, not read (A001), and names who confirmed it
    and when. An invalid attestation is UNKNOWN (U006). An attestation never overrides an answer
    the API gave.

Exit 0 only when every check passes (CLOSED, possibly with A001). Exit 1 when a check fails
(OPEN), cannot be completed (UNKNOWN), or was refused with no attestation (UNVERIFIABLE). Maintainer steps: docs/plans/evidence/release-gaps-0.25/README.md.
Tests: scripts/test_check_release_credentials.py.
"""

from __future__ import annotations

import argparse
import datetime
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Callable
from urllib.parse import quote

try:  # pinned in eng/release-credentials-requirements.txt; missing means W012 (fail closed)
    import yaml
except ImportError:  # pragma: no cover - exercised by the tests through monkeypatching
    yaml = None

REPO = "juanmicrosoft/calor"
ENVIRONMENT = "nuget-release"
# NUGET_API_KEY: the repository secret older workflows read. NUGET_RELEASE_API_KEY: the
# environment secret of the interim design. A workflow may name neither, except KEY_REF below.
OLD_SECRET = "NUGET_API_KEY"
RETIRED_SECRET = "NUGET_RELEASE_API_KEY"
KEY_NAMES = (OLD_SECRET, RETIRED_SECRET)
PUBLISH_WORKFLOW = "publish-nuget.yml"
PUBLISH_JOB = "publish"

# The official action, pinned to the commit of release v1.2.0 (tags v1.2.0 and v1 on 2026-10-19).
LOGIN_ACTION = "NuGet/login@8d196754b4036150537f80ac539e15c2f1028841"
LOGIN_ID = "nuget-login"
# The nuget.org account that owns calor and Calor.Sdk and the "calor-release" policy. Not a secret.
NUGET_USER = "juanmicrosoft"
# NuGet/login names its output NUGET_API_KEY. This exact expression, in the env of the guard and of
# the push step, is the only mention of that name any workflow may contain.
KEY_REF = "${{ steps.nuget-login.outputs.NUGET_API_KEY }}"
KEY_ENV = "NUGET_OIDC_KEY"

# Relative API paths (after repos/<owner>/<repo>/); --from-dir reads <key with / as -->.json.
ENDPOINTS = {
    "repo-secrets": "actions/secrets",
    "environment": f"environments/{ENVIRONMENT}",
    "branch-policies": f"environments/{ENVIRONMENT}/deployment-branch-policies",
    "environments": "environments",
    "workflow-permissions": "actions/permissions/workflow",
}
ENV_SECRETS = "environment-secrets/"  # + environment name


class Unreadable(Exception):
    """The API answered with something other than the data (403, network error, bad JSON)."""


class Forbidden(Unreadable):
    """The API answered 403: the token may not read this setting."""


class NotFound(Exception):
    """The API answered 404."""


# A maintainer's confirmation of the Actions workflow permissions, for when the API refuses to show
# them (HTTP 403: reading them needs the "Actions policies" permission, which a plain `gh` login may
# lack). Used only after a 403, never instead of an answer the API gave. Exact keys and values.
ATTESTATION_SCHEMA = "calor-release-credentials-attestation/v1"
ATTESTATION_SETTING = "actions/permissions/workflow"
ATTESTATION_KEYS = {"schema", "repository", "setting", "default_workflow_permissions",
                    "can_approve_pull_request_reviews", "confirmed_by", "confirmed_on", "why_not_read"}


# The publish job's first two steps, as parsed. Only these exact mappings count.
LOGIN_STEP = "Exchange the GitHub OIDC token for a short-lived NuGet key (#1545)"
LOGIN = {
    "name": LOGIN_STEP,
    "id": LOGIN_ID,
    "uses": LOGIN_ACTION,
    "with": {"user": NUGET_USER},
}
GUARD_STEP = "Require the Trusted Publishing NuGet key (#1545)"
GUARD = {
    "name": GUARD_STEP,
    "env": {KEY_ENV: KEY_REF},
    "run": (f'if [ -z "${KEY_ENV}" ]; then\n'
            '  echo "::error::nuget.org Trusted Publishing returned no key; check the calor-release '
            'policy and the nuget-release environment (#1545)"\n'
            "  exit 1\n"
            "fi\n"),
}
# Text that marks the login action or its output, matched without case.
LOGIN_MARKERS = ("nuget/login", LOGIN_ID)
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


def without(value: object, drop: str) -> object:
    """A copy of a parsed value in which every string equal to `drop` is replaced by ''."""
    if isinstance(value, str):
        return "" if value == drop else value
    if isinstance(value, dict):
        return {k: without(v, drop) for k, v in value.items()}
    if isinstance(value, list):
        return [without(v, drop) for v in value]
    return value


def mentions_login(value: object) -> bool:
    return any(mentions(value, marker) for marker in LOGIN_MARKERS)


def check_workflows(workflow_dir: Path) -> list[str]:
    """Decide on the parsed structure (PyYAML, duplicate keys rejected), never on text.

    nuget.org issues a key only to a job of publish-nuget.yml that runs in nuget-release (module
    docstring), so every job-level environment must be the publish job's exact `nuget-release`
    or one of OTHER_ENVIRONMENTS, and every job-level `uses` must be a local
    ./.github/workflows/ file with no @ref (its own jobs are checked here too). Codes:
      W001 publish-nuget.yml is missing.
      W002 a workflow names NUGET_API_KEY or NUGET_RELEASE_API_KEY anywhere other than the exact
           login-output expression inside the publish job (old workflows read secrets by those
           names; no secret may be referenced by them again).
      W003 another job uses NuGet/login or names its output.
      W004 another job declares an environment the check does not recognize.
      W005 the publish job has no `dotnet nuget push` step that reads the login output through
           env (and only that).
      W006 the publish job's environment is not exactly nuget-release (or the job is missing).
      W007 the publish job's second step is not exactly the empty-key guard.
      W008 the environment or the login appears outside a job, or jobs are not mappings.
      W009 anchors, aliases, or merge keys.
      W011 a job-level `uses` other than a local workflow file.
      W012 a workflow cannot be parsed, has a duplicate key, or carries an explicit YAML tag on
           any node (`!!set` would skip the duplicate-key check, `!!null` would erase a value).
      W013 the publish job does not grant `id-token: write` (the OIDC exchange needs it).
      W014 the publish job's first step is not exactly the pinned NuGet/login step.
      W015 another part of the publish job (job-level keys, or a step other than the login, the
           guard, and the push) uses NuGet/login or names its output.
    (W010, a key split across lines, is retired: the parser decodes those.)"""
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
        jobs_values = keyed(doc, "jobs")
        jobs = jobs_values[0] if jobs_values else {}
        # The login output's exact expression is allowed inside the publish job only; W015 and
        # W005 decide where in that job.
        scrubbed = doc
        if path.name == PUBLISH_WORKFLOW and isinstance(jobs, dict):
            scrubbed = {k: ({j: (without(b, KEY_REF) if j == PUBLISH_JOB else b) for j, b in v.items()}
                            if isinstance(k, str) and k.casefold() == "jobs" else v)
                        for k, v in doc.items()}
        for name in KEY_NAMES:
            if mentions(scrubbed, name):
                problems.append(f"W002: {path.name} names {name}; older workflow versions read a secret by "
                                "that name, and no workflow may name it again")
        outside = {k: v for k, v in doc.items() if not (isinstance(k, str) and k.casefold() == "jobs")}
        if mentions(outside, ENVIRONMENT) or mentions_login(outside):
            problems.append(f"W008: {path.name} names {ENVIRONMENT} or the NuGet login outside a job")
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
                problems += check_publish_job(body, environments)
                continue
            if mentions_login(body):
                problems.append(f"W003: {path.name} job '{job}' uses NuGet/login or names its output; only "
                                "the gated publish job may")
            for environment in environments:
                if environment not in OTHER_ENVIRONMENTS or mentions(environment, ENVIRONMENT):
                    problems.append(f"W004: {path.name} job '{job}' declares an environment the check does not "
                                    f"recognize (it may be {ENVIRONMENT}): {environment!r}")
    if not publish_seen and not any(p.startswith("W012: " + PUBLISH_WORKFLOW) for p in problems):
        problems.append(f"W006: {PUBLISH_WORKFLOW} has no job '{PUBLISH_JOB}'")
    return problems


def check_publish_job(body: dict, environments: list[object]) -> list[str]:
    where = f"{PUBLISH_WORKFLOW} job '{PUBLISH_JOB}'"
    problems: list[str] = []
    if environments != [ENVIRONMENT]:
        problems.append(f"W006: {where} must declare exactly `environment: {ENVIRONMENT}`")
    permissions = keyed(body, "permissions")
    if len(permissions) != 1 or not isinstance(permissions[0], dict) \
            or keyed(permissions[0], "id-token") != ["write"]:
        problems.append(f"W013: {where} must grant `id-token: write` in its own permissions")
    steps = (keyed(body, "steps") or [None])[0]
    steps = steps if isinstance(steps, list) else []
    if not steps or steps[0] != LOGIN:
        problems.append(f"W014: the first step of {where} must be exactly the '{LOGIN_STEP}' step "
                        f"(uses: {LOGIN_ACTION}, user: {NUGET_USER})")
    if len(steps) < 2 or steps[1] != GUARD:
        problems.append(f"W007: the second step of {where} must be exactly the '{GUARD_STEP}' check")
    pushes = [i for i, step in enumerate(steps) if i >= 2 and isinstance(step, dict)
              and isinstance(step.get("run"), str) and "dotnet nuget push" in step["run"]
              and step.get("env") == {KEY_ENV: KEY_REF} and f'--api-key "${KEY_ENV}"' in step["run"]]
    if not pushes:
        problems.append(f"W005: {where} has no `dotnet nuget push` step that reads the login output through "
                        f"env {KEY_ENV} (and nothing else)")
    rest = [step for i, step in enumerate(steps) if i >= 2 and i not in pushes]
    job_level = {k: v for k, v in body.items() if not (isinstance(k, str) and k.casefold() == "steps")}
    if mentions_login(rest) or mentions_login(job_level) or any(mentions(rest + [job_level], n) for n in KEY_NAMES):
        problems.append(f"W015: {where} uses NuGet/login or its output outside the login, guard, and push steps")
    return problems


LISTS = {"repo-secrets": "secrets", "branch-policies": "branch_policies", "environments": "environments"}


def endpoint(key: str) -> str:
    if key.startswith(ENV_SECRETS):
        return f"environments/{quote(key[len(ENV_SECRETS):], safe='')}/secrets"
    return ENDPOINTS[key]


def list_field(key: str) -> str | None:
    return "secrets" if key.startswith(ENV_SECRETS) else LISTS.get(key)


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
            if "HTTP 403" in text or '"status":"403"' in text:
                raise Forbidden(f"{path}: {text.strip()[:300]}")
            raise Unreadable(f"{path}: {text.strip()[:300]}")
        try:
            return json.loads(result.stdout, object_pairs_hook=unique_members)
        except ValueError as error:
            raise Unreadable(f"{path}: {error}") from error

    def fetch(key: str) -> object:
        path = f"repos/{repo}/{endpoint(key)}"
        field = list_field(key)
        if field is None:
            return get(path)
        # Every page, 100 at a time, until total_count entries are collected; listed() then
        # refuses any response whose entries do not add up to total_count.
        items, total = [], None
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
    """Saved responses: <key>.json holds the body (a / in the key is written --); a missing file
    means 404; <key>.error is unreadable (Forbidden when its text says HTTP 403)."""
    def fetch(key: str) -> object:
        stem = key.replace("/", "--")
        if (root / f"{stem}.error").exists():
            text = (root / f"{stem}.error").read_text(encoding="utf-8").strip()
            raise (Forbidden if "HTTP 403" in text else Unreadable)(f"{key}: {text}")
        path = root / f"{stem}.json"
        if not path.exists():
            raise NotFound(key)
        try:
            return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_members)
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


def names(body: object, what: str, field: str = "secrets") -> list[str]:
    """Every entry's name. An entry without a non-empty string name makes the list unreadable."""
    items = listed(body, field, what)
    if not all(isinstance(i.get("name"), str) and i["name"] for i in items):
        raise Unreadable(f"{what}: an entry has no name")
    return [i["name"] for i in items]


def check_environment(fetch: Callable[[str], object], problems: list[str], unknowns: list[str]) -> bool | None:
    """True when the environment exists (whatever its rules), False when it does not, None when unreadable."""
    try:
        env = fetch("environment")
    except NotFound:
        problems.append(f"S002: environment {ENVIRONMENT} does not exist")
        return False
    except Unreadable as error:
        unknowns.append(f"U002: cannot read environment {ENVIRONMENT} ({error})")
        return None
    if not isinstance(env, dict):
        unknowns.append(f"U002: environment {ENVIRONMENT} response is not an object")
        return None
    if env.get("can_admins_bypass") is not False:
        problems.append(f"S003: administrators can bypass {ENVIRONMENT}'s protection rules; turn that off")
    policy = env.get("deployment_branch_policy")
    if not isinstance(policy, dict) or policy.get("custom_branch_policies") is not True \
            or policy.get("protected_branches") is not False:
        problems.append(f"S004: {ENVIRONMENT} must use 'Selected branches and tags' (custom branch policies), "
                        f"got {json.dumps(policy)}")
        return True
    try:
        rules = listed(fetch("branch-policies"), "branch_policies", "deployment branch policies")
        found = sorted((str(r.get("name")), str(r.get("type", "branch"))) for r in rules)
        if found != [("main", "branch")]:
            problems.append(f"S005: {ENVIRONMENT} must admit exactly the branch 'main', got {found}")
    except (Unreadable, NotFound) as error:
        unknowns.append(f"U003: cannot read {ENVIRONMENT}'s deployment branch policies ({error})")
    return True


def unique_members(pairs: list[tuple[str, object]]) -> dict:
    """json object_pairs_hook for every API response and the attestation: a repeated member (a later
    one would silently win) is a ValueError, which the callers turn into Unreadable."""
    body: dict = {}
    for key, value in pairs:
        if key in body:
            raise ValueError(f"duplicate member {key!r}")
        body[key] = value
    return body


def load_attestation(path: Path, repo: str) -> dict:
    """The maintainer's attestation of the workflow permissions, or Unreadable. Every key and value
    is fixed except who confirmed it and when; anything else is refused."""
    try:
        body = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_members)
    except (OSError, ValueError) as error:
        raise Unreadable(f"{path}: {error}") from error
    if not isinstance(body, dict) or set(body) != ATTESTATION_KEYS:
        raise Unreadable(f"{path}: keys must be exactly {sorted(ATTESTATION_KEYS)}")
    expected = {"schema": ATTESTATION_SCHEMA, "repository": repo, "setting": ATTESTATION_SETTING,
                "default_workflow_permissions": "read"}
    for key, value in expected.items():
        if body[key] != value:
            raise Unreadable(f"{path}: {key} must be {value!r}, got {body[key]!r}")
    if body["can_approve_pull_request_reviews"] is not False:
        raise Unreadable(f"{path}: can_approve_pull_request_reviews must be false")
    if not (isinstance(body["confirmed_by"], str) and re.fullmatch(r"@[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})", body["confirmed_by"])):
        raise Unreadable(f"{path}: confirmed_by must be a GitHub @login")
    try:
        if not (isinstance(body["confirmed_on"], str) and re.fullmatch(r"\d{4}-\d{2}-\d{2}", body["confirmed_on"])):
            raise ValueError("not YYYY-MM-DD")
        datetime.date.fromisoformat(body["confirmed_on"])
    except ValueError as error:
        raise Unreadable(f"{path}: confirmed_on must be a real YYYY-MM-DD date ({error})") from error
    if not (isinstance(body["why_not_read"], str) and body["why_not_read"].strip()):
        raise Unreadable(f"{path}: why_not_read must say why the API could not be read")
    return body


def check_workflow_permissions(fetch: Callable[[str], object], attestation: Path | None, repo: str,
                               result: dict[str, list[str]]) -> None:
    """S008/S009 from the API. On HTTP 403 only, the setting is UNVERIFIABLE (V001) unless a valid
    maintainer attestation is passed; then it is reported as ATTESTED (A001), never as read."""
    try:
        workflow = fetch("workflow-permissions")
        if not isinstance(workflow, dict):
            raise Unreadable("workflow permissions response is not an object")
    except Forbidden as error:
        if attestation is None:
            result["unverifiable"].append(
                f"V001: the API refused to show the Actions workflow permissions ({error}); pass "
                "--attest-workflow-permissions <file> with the maintainer's confirmation, or rerun with a token "
                "that has the Actions policies permission")
            return
        try:
            body = load_attestation(attestation, repo)
        except Unreadable as bad:
            result["unknowns"].append(f"U006: the workflow-permissions attestation is not valid ({bad})")
            return
        result["attested"].append(
            f"A001: Actions workflow permissions ATTESTED, not read by API: default token "
            f"{body['default_workflow_permissions']}, create/approve pull requests off; confirmed by "
            f"{body['confirmed_by']} on {body['confirmed_on']} ({attestation}). Why not read: {body['why_not_read']}")
        return
    except (Unreadable, NotFound) as error:
        result["unknowns"].append(f"U005: cannot read the Actions workflow permissions ({error})")
        return
    if attestation is not None:
        result["attested"].append(f"A000: the API answered, so the attestation {attestation} was not used")
    if workflow.get("default_workflow_permissions") != "read":
        result["problems"].append("S008: Actions' default GITHUB_TOKEN is not read-only (Settings -> Actions -> "
                                  f"General -> Workflow permissions), got {workflow.get('default_workflow_permissions')!r}")
    if workflow.get("can_approve_pull_request_reviews") is not False:
        result["problems"].append("S009: GitHub Actions may create and approve pull requests; turn that off, got "
                                  f"{workflow.get('can_approve_pull_request_reviews')!r}")


def check_settings(fetch: Callable[[str], object], attestation: Path | None = None,
                   repo: str = REPO) -> dict[str, list[str]]:
    """{problems, unknowns, unverifiable, attested}"""
    result: dict[str, list[str]] = {"problems": [], "unknowns": [], "unverifiable": [], "attested": []}
    problems = result["problems"]
    unknowns = result["unknowns"]
    try:
        repo_level = [n for n in names(fetch("repo-secrets"), "repository secrets") if "NUGET" in n.upper()]
        if repo_level:
            problems.append(f"S001: repository-level secret(s) {sorted(repo_level)} are readable by every ref, "
                            "including older tags; delete them")
    except (Unreadable, NotFound) as error:
        unknowns.append(f"U001: cannot list repository secrets ({error}); run with the maintainer's admin login")

    release_environment = check_environment(fetch, problems, unknowns)

    # Trusted Publishing stores no key: a NuGet secret left in any environment is a stale key.
    try:
        environments = sorted(set(names(fetch("environments"), "environments", "environments")))
        inventory_read = True
    except (Unreadable, NotFound) as error:
        unknowns.append(f"U004: cannot list the repository's environments ({error})")
        environments, inventory_read = [], False
    if release_environment is not False and ENVIRONMENT not in environments:
        if release_environment and inventory_read:
            unknowns.append(f"U004: the environment list omits {ENVIRONMENT}, which exists; the inventory is "
                            "inconsistent")
        # Read its secrets whatever the list says (unless it is known not to exist).
        environments = sorted(set(environments) | {ENVIRONMENT})
    for name in environments:
        try:
            leftover = [n for n in names(fetch(ENV_SECRETS + name), f"{name} secrets") if "NUGET" in n.upper()]
            if leftover:
                problems.append(f"S007: environment {name} holds secret(s) {sorted(leftover)}; Trusted Publishing "
                                "stores no key, so delete them")
        except (Unreadable, NotFound) as error:
            unknowns.append(f"U004: cannot list environment {name}'s secrets ({error})")

    check_workflow_permissions(fetch, attestation, repo, result)
    return result


def run(args: argparse.Namespace) -> int:
    problems = check_workflows(Path(args.repo) / ".github" / "workflows")
    unknowns: list[str] = []
    unverifiable: list[str] = []
    attested: list[str] = []
    settings = bool(args.from_dir or args.live)
    if args.attest_workflow_permissions and not settings:
        print("::error::release credentials (#1545) --attest-workflow-permissions needs --live or --from-dir")
        return 2
    if settings:
        fetch = dir_fetch(Path(args.from_dir)) if args.from_dir else gh_fetch(args.github_repo)
        attestation = Path(args.attest_workflow_permissions) if args.attest_workflow_permissions else None
        result = check_settings(fetch, attestation, args.github_repo)
        problems += result["problems"]
        unknowns, unverifiable, attested = result["unknowns"], result["unverifiable"], result["attested"]
    for line in problems:
        print(f"::error::release credentials (#1545) {line}")
    for line in unknowns + unverifiable:
        print(f"::warning::release credentials (#1545) {line}")
    for line in attested:
        print(f"::notice::release credentials (#1545) {line}")
    if problems:
        print(f"RELEASE CREDENTIALS: OPEN ({len(problems)} problem(s)); a NuGet key may still be reachable "
              "outside the gated publish job.")
        return 1
    if unknowns:
        print(f"RELEASE CREDENTIALS: UNKNOWN ({len(unknowns)} setting(s) unreadable).")
        return 1
    if unverifiable:
        print(f"RELEASE CREDENTIALS: UNVERIFIABLE ({len(unverifiable)} setting(s) the API refused to show, "
              "with no attestation); every other check passed.")
        return 1
    if not settings:
        scope = "workflows only (pass --live for settings)"
    elif any(line.startswith("A001") for line in attested):
        scope = ("workflows and repository settings; Actions workflow permissions ATTESTED by the maintainer, "
                 "not read by API (A001 above)")
    else:
        scope = "workflows and repository settings"
    print(f"RELEASE CREDENTIALS: CLOSED ({scope}). nuget.org itself (the calor-release policy, the old key's "
          "revocation) is not visible to this check.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--repo", default=str(Path(__file__).resolve().parent.parent), help="repository checkout")
    parser.add_argument("--github-repo", default=REPO, help="owner/name for --live")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--live", action="store_true", help="also read the settings through `gh api --method GET`")
    source.add_argument("--from-dir", help="also read the settings from saved API responses (tests)")
    parser.add_argument("--attest-workflow-permissions", metavar="FILE",
                        help="maintainer attestation of the Actions workflow permissions, used only when the API "
                             "answers HTTP 403 (docs/plans/evidence/release-gaps-0.25/"
                             "actions-workflow-permissions-attestation.json)")
    return run(parser.parse_args(argv))


if __name__ == "__main__":
    sys.exit(main())
