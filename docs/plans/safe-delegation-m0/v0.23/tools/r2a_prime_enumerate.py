#!/usr/bin/env python3
"""R2A' public-proxy repository enumeration (#1373).

Applies ONLY the frozen selection criteria in ../r2a-prime-public-proxy.md
(section "Frozen selection rule") to GitHub repository *metadata*. It never
reads issue/PR titles, bodies, comments, labels, diffs, or source files.
Every issue/PR/commit query below requests a count only.

Usage:
  python3 r2a_prime_enumerate.py --freeze-commit <sha> --out ../r2a-prime-repos.json

Requires an authenticated `gh` CLI. Deterministic given the same GitHub
snapshot; GitHub search results drift over time, so the committed JSON
snapshot (not a re-run) is the authoritative record.
"""
import argparse
import datetime as dt
import hashlib
import json
import re
import subprocess
import sys
import time

# ---- Frozen parameters (must match r2a-prime-public-proxy.md) -------------
SNAPSHOT_DATE = "2026-10-01"
WINDOW_SINCE = "2025-10-01T00:00:00Z"   # preceding 12 months, inclusive
WINDOW_UNTIL = "2026-10-01T00:00:00Z"   # exclusive
WINDOW_SEARCH = "2025-10-01..2026-09-30"
CREATED_ON_OR_BEFORE = "2023-10-01"
PUSHED_ON_OR_AFTER = "2026-07-03"       # 90 days before snapshot
MIN_STARS = 500
MAX_DISK_KB = 1_000_000
MIN_COMMITS_WINDOW = 50
MIN_MERGED_PRS_WINDOW = 24
MIN_ISSUES_OPENED_WINDOW = 12
LICENSE_ALLOWLIST = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause"}
MAINTAINER_LOGINS = ["juanmicrosoft", "juanatjcx"]
MAINTAINER_OWNERS = {"juanmicrosoft", "calor-lang"}
SEED_BASE = "calor-1373-r2a-prime-v1:72a0a855d8cd7f1e85c5bcd99474b83d1556d4e1"
N_PRIMARY = 12
N_RESERVE = 12
STAR_PARTITIONS = [(500, 999), (1000, 1999), (2000, 4999), (5000, 9999),
                   (10000, 10_000_000)]
EXPOSURE_PATHSPEC_EXCLUDE = ":(exclude)docs/plans/safe-delegation-m0/v0.23"
# --------------------------------------------------------------------------


def gh(args, retries=6):
    for attempt in range(retries):
        p = subprocess.run(["gh", "api", *args], capture_output=True, text=True)
        if p.returncode == 0:
            return json.loads(p.stdout) if p.stdout.strip() else None
        msg = p.stderr + p.stdout
        if "rate limit" in msg.lower() or "abuse" in msg.lower() or "502" in msg or "503" in msg:
            time.sleep(20 * (attempt + 1))
            continue
        raise RuntimeError(f"gh api {' '.join(args)} failed: {msg.strip()}")
    raise RuntimeError(f"gh api {' '.join(args)} failed after retries")


def order_key(full_name):
    return hashlib.sha256(f"{SEED_BASE}\n{full_name.lower()}".encode()).hexdigest()


def calor_exposed(freeze_commit, repo_root):
    """Owner/repo strings appearing in tracked Calor files at the freeze commit."""
    p = subprocess.run(
        ["git", "-C", repo_root, "grep", "-hoiE",
         r"github\.com[/:][A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", freeze_commit, "--",
         ".", EXPOSURE_PATHSPEC_EXCLUDE],
        capture_output=True, text=True)
    names = set()
    for line in p.stdout.splitlines():
        m = re.search(r"github\.com[/:]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)", line, re.I)
        if m:
            repo = re.sub(r"\.git$", "", m.group(2)).rstrip(".")
            names.add(f"{m.group(1)}/{repo}".lower())
    return sorted(names)


def search_partition(lo, hi):
    q = (f"language:csharp stars:{lo}..{hi} pushed:>={PUSHED_ON_OR_AFTER} "
         f"created:<={CREATED_ON_OR_BEFORE} fork:false archived:false is:public")
    first = gh(["-X", "GET", "search/repositories", "-f", f"q={q}",
                "-f", "per_page=100", "-f", "page=1", "-f", "sort=stars"])
    total = first["total_count"]
    if total > 1000:
        if lo == hi:
            raise RuntimeError(f"cannot partition stars={lo}")
        mid = (lo + hi) // 2
        return search_partition(lo, mid) + search_partition(mid + 1, hi)
    items = list(first["items"])
    incomplete = first.get("incomplete_results", False)
    page = 2
    while len(items) < total:
        time.sleep(2.5)  # search API: 30 requests/minute
        r = gh(["-X", "GET", "search/repositories", "-f", f"q={q}",
                "-f", "per_page=100", "-f", f"page={page}", "-f", "sort=stars"])
        incomplete = incomplete or r.get("incomplete_results", False)
        if not r["items"]:
            break
        items.extend(r["items"])
        page += 1
    time.sleep(2.5)
    return [{"query": q, "total_count": total, "retrieved": len(items),
             "incomplete_results": incomplete,
             "names": sorted({i["full_name"] for i in items})}]


GQL = """
query($owner:String!,$name:String!,$since:GitTimestamp!,$until:GitTimestamp!,
      $qpr:String!,$qis:String!){
  repository(owner:$owner,name:$name){
    nameWithOwner url isFork isArchived isDisabled isTemplate isPrivate isMirror
    hasIssuesEnabled createdAt pushedAt stargazerCount diskUsage
    owner{login}
    primaryLanguage{name}
    licenseInfo{spdxId}
    defaultBranchRef{ name target{ ... on Commit {
      oid committedDate history(since:$since, until:$until){ totalCount } } } }
  }
  prs: search(query:$qpr, type:ISSUE, first:1){ issueCount }
  issues: search(query:$qis, type:ISSUE, first:1){ issueCount }
}
"""
GQL_COUNT = "query($q:String!){ search(query:$q, type:ISSUE, first:1){ issueCount } }"
# NOTE: `first:1` is the GraphQL minimum; only `issueCount` is selected, so no
# node fields (titles, bodies, authors) are requested or returned.


def evaluate(full_name, exposed):
    owner, name = full_name.split("/", 1)
    r = gh(["graphql", "-f", f"query={GQL}", "-f", f"owner={owner}", "-f", f"name={name}",
            "-f", f"since={WINDOW_SINCE}", "-f", f"until={WINDOW_UNTIL}",
            "-f", f"qpr=repo:{full_name} is:pr is:merged merged:{WINDOW_SEARCH}",
            "-f", f"qis=repo:{full_name} is:issue created:{WINDOW_SEARCH}"])["data"]
    repo = r["repository"]
    target = (repo.get("defaultBranchRef") or {}).get("target") or {}
    sha = target.get("oid")
    lic_at_sha = None
    if sha:
        try:
            lic_at_sha = gh(["-X", "GET", f"repos/{full_name}/license", "-f", f"ref={sha}",
                             "--jq", ".license.spdx_id // null"])
        except RuntimeError:
            lic_at_sha = None
    # Maintainer-association counts only (--jq / issueCount strip all items).
    maintainer_issue_pr, maintainer_commits = 0, 0
    for login in MAINTAINER_LOGINS:
        maintainer_issue_pr += gh(["graphql", "-f", f"query={GQL_COUNT}",
                                   "-f", f"q=repo:{full_name} involves:{login}",
                                   "--jq", ".data.search.issueCount"])
        time.sleep(2.5)
        maintainer_commits += gh(["-X", "GET", "search/commits", "-f",
                                  f"q=repo:{full_name} author:{login}", "-f", "per_page=1",
                                  "--jq", ".total_count"])
    ev = {
        "repo": repo["nameWithOwner"],
        "url": repo["url"],
        "primary_language": (repo.get("primaryLanguage") or {}).get("name"),
        "license_spdx_repo": (repo.get("licenseInfo") or {}).get("spdxId"),
        "license_spdx_at_pinned_sha": lic_at_sha,
        "created_at": repo["createdAt"],
        "pushed_at": repo["pushedAt"],
        "stars": repo["stargazerCount"],
        "disk_usage_kb": repo["diskUsage"],
        "is_fork": repo["isFork"], "is_archived": repo["isArchived"],
        "is_disabled": repo["isDisabled"], "is_template": repo["isTemplate"],
        "is_mirror": repo["isMirror"], "is_private": repo["isPrivate"],
        "has_issues_enabled": repo["hasIssuesEnabled"],
        "default_branch": (repo.get("defaultBranchRef") or {}).get("name"),
        "pinned_sha": sha,
        "pinned_sha_committed_date": target.get("committedDate"),
        "commits_default_branch_window": (target.get("history") or {}).get("totalCount"),
        "merged_prs_window": r["prs"]["issueCount"],
        "issues_opened_window": r["issues"]["issueCount"],
        "maintainer_involved_issue_pr_count": maintainer_issue_pr,
        "maintainer_authored_commit_count": maintainer_commits,
    }
    fails = []
    if ev["primary_language"] != "C#": fails.append("C1-language")
    if any(ev[k] for k in ("is_fork", "is_archived", "is_disabled", "is_template",
                           "is_mirror", "is_private")): fails.append("C2-repo-state")
    if ev["created_at"][:10] > CREATED_ON_OR_BEFORE: fails.append("C3-age")
    if ev["pushed_at"][:10] < PUSHED_ON_OR_AFTER: fails.append("C4-recent-push")
    if ev["stars"] < MIN_STARS: fails.append("C5-stars")
    if not (ev["license_spdx_repo"] in LICENSE_ALLOWLIST
            and ev["license_spdx_at_pinned_sha"] == ev["license_spdx_repo"]):
        fails.append("C6-license")
    if (ev["commits_default_branch_window"] or 0) < MIN_COMMITS_WINDOW:
        fails.append("C7-commits")
    if not ev["has_issues_enabled"] or ev["merged_prs_window"] < MIN_MERGED_PRS_WINDOW \
            or ev["issues_opened_window"] < MIN_ISSUES_OPENED_WINDOW:
        fails.append("C8-public-history")
    if (owner.lower() in MAINTAINER_OWNERS or ev["maintainer_involved_issue_pr_count"] > 0
            or ev["maintainer_authored_commit_count"] > 0):
        fails.append("C9-maintainer")
    if full_name.lower() in exposed: fails.append("C10-calor-exposed")
    if ev["disk_usage_kb"] > MAX_DISK_KB: fails.append("C11-size")
    ev["failed_criteria"] = fails
    ev["eligible"] = not fails
    return ev


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--freeze-commit", required=True)
    ap.add_argument("--repo-root", default=".")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    exposed = calor_exposed(a.freeze_commit, a.repo_root)
    partitions = []
    for lo, hi in STAR_PARTITIONS:
        partitions.extend(search_partition(lo, hi))
    candidates = sorted({n for p in partitions for n in p["names"]})
    ordered = sorted(candidates, key=order_key)

    walked, primary, reserve = [], [], []
    for full_name in ordered:
        if len(primary) >= N_PRIMARY and len(reserve) >= N_RESERVE:
            break
        ev = evaluate(full_name, exposed)
        ev["order_rank"] = ordered.index(full_name) + 1
        ev["order_key"] = order_key(full_name)
        walked.append(ev)
        if ev["eligible"]:
            (primary if len(primary) < N_PRIMARY else reserve).append(ev["repo"])
        print(f"{ev['order_rank']:5d} {full_name:55s} {'OK' if ev['eligible'] else ','.join(ev['failed_criteria'])}",
              file=sys.stderr)

    out = {
        "schema": "calor/r2a-prime-repos/v1",
        "issue": 1373,
        "gate": "R2A-prime (public-proxy authority)",
        "label": "AI-adjudicated, public-proxy domain",
        "criteria_document": "docs/plans/safe-delegation-m0/v0.23/r2a-prime-public-proxy.md",
        "criteria_freeze_commit": a.freeze_commit,
        "retrieval_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "snapshot_date": SNAPSHOT_DATE,
        "parameters": {
            "window": [WINDOW_SINCE, WINDOW_UNTIL], "created_on_or_before": CREATED_ON_OR_BEFORE,
            "pushed_on_or_after": PUSHED_ON_OR_AFTER, "min_stars": MIN_STARS,
            "max_disk_kb": MAX_DISK_KB, "min_commits_window": MIN_COMMITS_WINDOW,
            "min_merged_prs_window": MIN_MERGED_PRS_WINDOW,
            "min_issues_opened_window": MIN_ISSUES_OPENED_WINDOW,
            "license_allowlist": sorted(LICENSE_ALLOWLIST), "seed": SEED_BASE,
            "order_key": "sha256(seed + '\\n' + lower(full_name)) ascending",
            "n_primary": N_PRIMARY, "n_reserve": N_RESERVE,
        },
        "calor_exposed_repos": exposed,
        "search_partitions": [{k: v for k, v in p.items() if k != "names"} for p in partitions],
        "candidate_count": len(candidates),
        "candidate_walk_order": ordered,
        "primary": primary,
        "reserve_ordered": reserve,
        "walked": walked,
        "content_inspected": "none: metadata and counts only; no issue/PR/commit titles, bodies, comments, labels, diffs, or source files were requested",
    }
    with open(a.out, "w") as f:
        json.dump(out, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
