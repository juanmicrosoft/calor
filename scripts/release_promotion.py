#!/usr/bin/env python3
"""Verify and download a same-run immutable release artifact. No publication credentials."""

import argparse
import datetime
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import zipfile

import release_checks as rc

REPOSITORY = "juanmicrosoft/calor"
WORKFLOW = ".github/workflows/publish-nuget.yml"
GATES = ("preflight", "test", "sdk-consumer", "release-quality", "site", "package")


def api(path: str) -> bytes:
    return subprocess.check_output(["gh", "api", "--method", "GET", path])


def get(path: str) -> dict:
    return json.loads(api(f"repos/{REPOSITORY}/{path}"))


def require(condition: object, message: str) -> None:
    if not condition:
        raise rc.CheckError(message)


def identity(run: dict, workflow: dict, artifact: dict, environment: dict, policies: dict,
             branch: dict, *, run_id: int, candidate: str, artifact_id: int, digest: str,
             needs: dict, now: datetime.datetime | None = None) -> None:
    """All inputs except expected identities come from authenticated GitHub API reads."""
    require(re.fullmatch(r"[0-9a-f]{40}", candidate), "invalid candidate")
    require(run["id"] == run_id and run["repository"]["full_name"] == REPOSITORY,
            "untrusted repository/run")
    require(run["event"] == "workflow_dispatch" and run["head_branch"] == "main"
            and run["head_sha"] == candidate, "wrong candidate or dispatch ref")
    require(workflow["path"] == WORKFLOW and workflow["state"] == "active"
            and run["workflow_id"] == workflow["id"] and run["path"] == WORKFLOW,
            "untrusted workflow")
    # This workflow is deliberately still running while its protected publish job waits.
    require(run["status"] == "in_progress" and run["conclusion"] is None,
            "run is not an active validation/promotion run")
    require(all(needs.get(g, {}).get("result") == "success" for g in GATES),
            "required validation gate failed, missing, or incomplete")
    require(branch["protected"] is True, "main is not protected")
    require(environment["can_admins_bypass"] is False, "environment allows admin bypass")
    reviewers = [r for r in environment["protection_rules"] if r["type"] == "required_reviewers"]
    require(len(reviewers) == 1 and reviewers[0]["prevent_self_review"] is True
            and len(reviewers[0]["reviewers"]) > 0, "independent maintainer approval is not required")
    require(environment["deployment_branch_policy"] == {
        "protected_branches": False, "custom_branch_policies": True},
        "environment does not use a main-only policy")
    require(policies["total_count"] == 1 and len(policies["branch_policies"]) == 1
            and policies["branch_policies"][0]["name"] == "main"
            and policies["branch_policies"][0]["type"] == "branch",
            "environment admits refs other than main")
    require(artifact["id"] == artifact_id and artifact["name"] == "release-bundle"
            and artifact["workflow_run"]["id"] == run_id
            and artifact["workflow_run"]["head_sha"] == candidate,
            "wrong artifact identity or candidate")
    require(re.fullmatch(r"[0-9a-f]{64}", digest)
            and artifact["digest"] == f"sha256:{digest}", "artifact digest differs from upload record")
    expires = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
    require(artifact["expired"] is False and expires > (now or datetime.datetime.now(datetime.timezone.utc)),
            "artifact is expired")


def unpack(data: bytes, destination: Path) -> None:
    require(not destination.exists(), "bundle destination must not exist")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "duplicate archive entries")
        for item in archive.infolist():
            require(item.filename == rc.BUNDLE_SUMS or rc.BUNDLE_PATH_RE.fullmatch(item.filename),
                    f"unexpected archive path: {item.filename}")
            require(not item.is_dir() and (item.external_attr >> 16) & 0o170000 != 0o120000,
                    "archive contains a directory or symlink")
        destination.mkdir(parents=True)
        archive.extractall(destination)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--artifact-id", type=int, required=True)
    parser.add_argument("--digest", required=True)
    parser.add_argument("--sums", required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    try:
        require(os.environ["GITHUB_REPOSITORY"] == REPOSITORY
                and os.environ["GITHUB_REF"] == "refs/heads/main"
                and os.environ["GITHUB_SHA"] == args.candidate
                and os.environ["GITHUB_WORKFLOW_REF"] == f"{REPOSITORY}/{WORKFLOW}@refs/heads/main",
                "untrusted execution context")
        problems, _ = rc.preflight(Path("."), args.version)
        require(not problems, "; ".join(problems))
        require(rc.git(Path("."), "rev-parse", "HEAD") == args.candidate, "wrong checkout")
        require(rc.git(Path("."), "merge-base", "--is-ancestor", args.candidate,
                       "refs/remotes/origin/main") is not None, "candidate is not on protected main")
        run_id = int(os.environ["GITHUB_RUN_ID"])
        identity(get(f"actions/runs/{run_id}"), get("actions/workflows/publish-nuget.yml"),
                 get(f"actions/artifacts/{args.artifact_id}"), get("environments/nuget-release"),
                 get("environments/nuget-release/deployment-branch-policies"), get("branches/main"),
                 run_id=run_id, candidate=args.candidate, artifact_id=args.artifact_id,
                 digest=args.digest, needs=json.loads(os.environ["RELEASE_NEEDS"]))
        data = api(f"repos/{REPOSITORY}/actions/artifacts/{args.artifact_id}/zip")
        require(rc.sha256(data) == args.digest, "downloaded artifact bytes differ from upload digest")
        unpack(data, args.destination)
        problems = rc.bundle_inventory_problems(args.destination, args.version, args.sums)
        require(not problems, "; ".join(problems))
        require((args.destination / "site/site.tar").is_file(), "bundle lacks the validated site")
        for suffix in ("sbom.spdx.json", "provenance.json"):
            require((args.destination / f"release-metadata/calor-nuget-{args.candidate}.{suffix}").is_file(),
                    f"bundle lacks candidate {suffix}")
        print(f"Verified run {run_id}, candidate {args.candidate}, artifact {args.artifact_id}, sha256:{args.digest}")
        return 0
    except (rc.CheckError, KeyError, TypeError, ValueError, OSError, subprocess.CalledProcessError,
            zipfile.BadZipFile) as error:
        print(f"::error::release promotion refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
