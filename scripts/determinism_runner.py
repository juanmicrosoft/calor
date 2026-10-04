#!/usr/bin/env python3
"""0.24 G2 (#1421): execution machinery for the registered verifier determinism protocol.

Run only by .github/workflows/determinism-protocol.yml (workflow_dispatch only). The protocol is
docs/plans/evidence/g2-1421/protocol.json; the value rules, the workflow structure check, the
environment judgment, and the decider are frozen in scripts/determinism_protocol.py and imported.

  plan          validate the packet (this tree and main's validator, D016); refuse a non-dispatch or
                foreign run, a re-run, a reused execution id, an executed commit, a docs-only repair,
                a run missing from the API inventory or ledger, and a run over the budget or run limits
  install-sdk   install exactly toolchain.sdk into a private root with a SHA-256-pinned installer
  env-check     check and record one job's environment
  run-job       run every attempt of one job once, writing the attempt record after every invocation
  fill-missing  record every attempt of a job that has no record (never runs one)
  decide        the frozen decider, bound to the dispatched commit and this run
  ledger-check  the ledger and the API inventory list the same dispatched runs and minutes

Nothing here retries an attempt, re-runs a job or a commit, normalizes a compared value, or looks
up, reads, writes, or deletes the real home directory: the workflow moves every job's home under
the runner temp directory, and every test invocation gets its own fresh, empty one.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import signal
import subprocess
import sys
import time
import urllib.request
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import determinism_protocol as dp  # noqa: E402

ROOT = dp.ROOT
EXECUTION_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9-]{0,39}$")
TITLE = re.compile(r"^determinism (control|execution) (\S+)$")
ISOLATED = ("HOME", "USERPROFILE", "DOTNET_CLI_HOME")
# Per-user configuration and cache roots that .NET, NuGet, and Windows read besides the home variables.
UNDER_HOME = {"APPDATA": "AppData/Roaming", "LOCALAPPDATA": "AppData/Local", "XDG_CONFIG_HOME": ".config", "XDG_DATA_HOME": ".local/share",
              "XDG_CACHE_HOME": ".cache", "NUGET_HTTP_CACHE_PATH": "nuget-http", "NUGET_PLUGINS_CACHE_PATH": "nuget-plugins"}
# The dotnet-install scripts bundled with actions/setup-dotnet at v4 (commit below), pinned by SHA-256.
# setup-dotnet itself first installs a floating LTS runtime, so it is not used (amendment 1.1.0).
INSTALLER = {"base": "https://raw.githubusercontent.com/actions/setup-dotnet/67a3573c9a986a3f9c594539f4ab511d57bb3ce9/externals/",
             "install-dotnet.sh": "19b0a7890c371201b944bf0f8cdbb6460d053d63ddbea18cfed3e4199769ce17",
             "install-dotnet.ps1": "7e9969069558023daf52bbf6fc55eb37032eb23c7ff55a7d6afc659d54d6c23b"}
PROBE = "System.Console.WriteLine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));\n"
PROBE_PROJECT = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>'
                 '<UseAppHost>false</UseAppHost></PropertyGroup></Project>\n')


class Refusal(Exception):
    """A guard refused to proceed. The workflow step fails; nothing is retried."""


def refuse_foreign(env) -> None:
    """Only the first attempt of a run of the registered workflow may act (retryPolicy; no copied workflow)."""
    if str(env.get("GITHUB_RUN_ATTEMPT", "")) != "1":
        raise Refusal(f"GITHUB_RUN_ATTEMPT={env.get('GITHUB_RUN_ATTEMPT')}: no job or workflow run is run again (retryPolicy)")
    if not str(env.get("GITHUB_WORKFLOW_REF", "")).startswith(f"{env.get('GITHUB_REPOSITORY')}/{dp.WORKFLOW}@"):
        raise Refusal(f"GITHUB_WORKFLOW_REF={env.get('GITHUB_WORKFLOW_REF')}: only {dp.WORKFLOW} may run the machinery")


def shape(protocol, mode) -> tuple[list[dict], int, int]:
    if mode == "control":
        c = protocol["control"]
        return [c["profile"]], c["jobsPerEnvironment"], c["attemptsPerJob"]
    if mode == "execution":
        return protocol["profiles"], protocol["runPlan"]["jobsPerEnvironment"], protocol["runPlan"]["attemptsPerJob"]
    raise Refusal(f"unknown mode {mode!r}")


def env_profiles(protocol, mode, env) -> list[dict]:
    profiles, _, _ = shape(protocol, mode)
    return profiles if mode == "control" else [p for p in profiles if p["id"] in env["profiles"]]


# ---------------------------------------------------------------- plan: history, budget, repairs


def classify(run) -> str:
    match = TITLE.match(run.get("title") or "")
    return match.group(1) if match else "unknown"  # an unreadable title is charged and counted as an execution


def plan_problems(protocol, *, mode, execution_id, env, inventory, ledger, changed_paths) -> list[str]:
    """Guards before any attempt runs. inventory is every run of the workflow from the GitHub API
    (fetch_inventory); ledger is #1135's ledger (entries with runId, commit, mode, runnerMinutes);
    changed_paths(old, new) lists changed paths, or None if git cannot tell."""
    problems = []
    if env.get("GITHUB_EVENT_NAME") != "workflow_dispatch":
        problems.append(f"event {env.get('GITHUB_EVENT_NAME')}: only a workflow_dispatch run may execute the protocol")
    try:
        refuse_foreign(env)
    except Refusal as error:
        problems.append(str(error))
    commit, here = env.get("GITHUB_SHA") or "", str(env.get("GITHUB_RUN_ID"))
    if mode not in dp.MODES or not EXECUTION_ID.match(execution_id or "") or not re.match(r"^[0-9a-f]{40}$", commit):
        return problems + ["mode must be control or execution, the execution id match [A-Za-z0-9][A-Za-z0-9-]{0,39}, and GITHUB_SHA be a full SHA"]
    current = [r for r in inventory if str(r["id"]) == here]
    if len(current) != 1 or current[0]["event"] != "workflow_dispatch" or TITLE.match(current[0]["title"] or "") is None \
            or TITLE.match(current[0]["title"]).groups() != (mode, execution_id):
        problems.append(f"run {here} is not listed exactly once in the workflow's inventory as 'determinism {mode} {execution_id}'")
    prior = [dict(r) for r in inventory if r["event"] == "workflow_dispatch" and str(r["id"]) != here]
    listed = {str(r["id"]): r for r in prior}
    for entry in ledger.get("entries", []):
        run = listed.get(str(entry.get("runId")))
        if run is None:
            problems.append(f"ledger run {entry.get('runId')} is missing from the API inventory (deleted?); history cannot be trusted")
        elif entry.get("commit", run["headSha"]) != run["headSha"] or entry.get("mode", classify(run)) != classify(run):
            problems.append(f"ledger run {entry.get('runId')} names another commit or mode than the API inventory")
        else:  # the larger of the two records counts, and a ledgered execution's commit stays executed
            run["minutes"] = max(run["minutes"], int(entry.get("runnerMinutes") or 0))
            run["attemptsStarted"] = run["attemptsStarted"] or entry.get("mode") == "execution"
    b = protocol["budget"]
    execution_worst, control_worst, _ = dp.worst_case(protocol)
    worst = {"execution": execution_worst, "control": control_worst, "unknown": execution_worst}
    recorded = b["dryRunSpent"] + sum(r["minutes"] for r in prior)
    reserved = sum(worst[classify(r)] for r in prior if r["status"] != "completed")
    if recorded + reserved + worst[mode] > b["ceilingRunnerMinutes"]:
        problems.append(f"budget: {recorded} recorded + {reserved} reserved for unfinished runs + {worst[mode]} for this run "
                        f"exceeds the {b['ceilingRunnerMinutes']} runner-minute ceiling (contract stopping rule 1)")
    executions = [r for r in prior if classify(r) != "control" and (r["status"] != "completed" or r["attemptsStarted"])]
    controls = [r for r in prior if classify(r) == "control"]  # every dispatched control run counts, started or not
    limit, count = (b["maxExecutions"], len(executions)) if mode == "execution" else (b["maxDispatchedControlRuns"], len(controls))
    if count >= limit:
        problems.append(f"the limit of {limit} {mode} runs is reached ({count} recorded)")
    if any((TITLE.match(r.get("title") or "") or [None, None, None])[2] == execution_id for r in prior):
        problems.append(f"execution id {execution_id} was already dispatched")
    if mode == "execution":
        if any(r["status"] != "completed" for r in executions):
            problems.append("another execution is unfinished")
        if any(r["headSha"] == commit for r in executions):
            problems.append(f"commit {commit} was already executed; a commit is executed at most once (retryPolicy)")
        previous = max((r for r in executions if r["status"] == "completed"), key=lambda r: r["runNumber"], default=None)
        # The API does not expose a prior verdict, so every execution run that did not conclude success is
        # treated as possibly NON-DETERMINISTIC or FAILING (fail closed).
        if previous is not None and previous["conclusion"] != "success":
            paths = changed_paths(previous["headSha"], commit)
            if paths is None or not any(not p.startswith("docs/") for p in paths):
                problems.append(f"after execution run {previous['id']} ({previous['conclusion']}) the next commit must change a "
                                f"path outside docs/ (changed: {paths})")
    return problems


def matrix(protocol, mode) -> dict:
    _, jobs, _ = shape(protocol, mode)
    include = []
    for env in protocol["environments"]:
        timeout = protocol["control"]["jobTimeoutMinutes"] if mode == "control" else env["jobTimeoutMinutes"]
        projects = sorted({p["project"] for p in env_profiles(protocol, mode, env)})
        for job in range(1, jobs + 1):
            include.append({"env": env["id"], "job": job, "runner": env["runner"], "timeout": timeout,
                            "projects": " ".join(projects)})
    return {"include": include}


def api(path: str, token: str):
    request = urllib.request.Request(f"https://api.github.com{path}", headers={
        "Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read())


def paged(path: str, key: str, token: str, get=api) -> list[dict]:
    """Every item of a paginated listing; fails closed on a missing count, a short read, or a duplicate."""
    items, page = [], 1
    while True:
        data = get(f"{path}{'&' if '?' in path else '?'}per_page=100&page={page}", token)
        if not isinstance(data.get("total_count"), int) or not isinstance(data.get(key), list):
            raise Refusal(f"{path}: the response has no total_count or {key}")
        items += data[key]
        if len(data[key]) < 100:
            ids = [item["id"] for item in items]
            if len(items) != data["total_count"] or len(set(ids)) != len(ids):
                raise Refusal(f"{path}: listed {len(items)} items ({len(set(ids))} distinct) of {data['total_count']}")
            return items
        page += 1


def job_minutes(jobs: list[dict], finished: bool) -> int:
    """budget.accounting: each job's completed_at - started_at, rounded up to a minute, no OS multiplier.
    A job of a finished run that started without finishing, or ends before it starts, fails closed."""
    total = 0
    for job in jobs:
        if not job.get("started_at") and job.get("completed_at") and job.get("conclusion") != "skipped":
            raise Refusal(f"job {job.get('id')} completed without a start time")
        if not job.get("started_at") or (not job.get("completed_at") and not finished):
            continue
        if not job.get("completed_at"):
            raise Refusal(f"job {job.get('id')} started but has no completed_at in a finished run")
        start, end = (datetime.fromisoformat(job[k].replace("Z", "+00:00")) for k in ("started_at", "completed_at"))
        if end < start:
            raise Refusal(f"job {job.get('id')} ends before it starts")
        total += math.ceil((end - start).total_seconds() / 60)
    return total


def fetch_inventory(repo: str, token: str, get=api) -> list[dict]:
    """Every run of the workflow (all branches, events, and run attempts) with its measured minutes."""
    out = []
    for run in paged(f"/repos/{repo}/actions/workflows/determinism-protocol.yml/runs", "workflow_runs", token, get):
        jobs = paged(f"/repos/{repo}/actions/runs/{run['id']}/jobs?filter=all", "jobs", token, get)
        finished = run["status"] == "completed"
        out.append({"id": run["id"], "runNumber": run["run_number"], "title": run.get("display_title"), "event": run["event"],
                    "status": run["status"], "conclusion": run.get("conclusion"), "headSha": run["head_sha"],
                    "minutes": job_minutes(jobs, finished),
                    "attemptsStarted": any(j["name"].startswith("attempts") and j.get("started_at") and j.get("conclusion") != "skipped"
                                           for j in jobs)})
    return out


def git(*args) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)


def changed_paths(old: str, new: str):
    result = subprocess.run(["git", "diff", "--name-only", "-z", f"{old}..{new}"], cwd=ROOT, capture_output=True, check=False)
    return [p for p in result.stdout.decode("utf-8", "surrogateescape").split("\0") if p] if result.returncode == 0 else None


def load_ledger(protocol) -> dict:
    path = ROOT / protocol["executions"]["ledger"]
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {"entries": []}


def cmd_plan(args, env=os.environ) -> int:
    protocol = dp.load(ROOT, dp.PROTOCOL)
    problems = [f"{c}: {m}" for c, m in dp.validate(ROOT, baseline=dp.load_baseline(ROOT, "origin/main"))]
    trusted = subprocess.run([sys.executable, args.main_validator, "validate", "--root", str(ROOT), "--baseline-ref", "origin/main"],
                             capture_output=True, text=True, check=False)
    if trusted.returncode != 0:
        problems.append(f"main's validator rejects this tree:\n{trusted.stdout}{trusted.stderr}")
    inventory = fetch_inventory(env["GITHUB_REPOSITORY"], env["GH_TOKEN"])
    problems += plan_problems(protocol, mode=args.mode, execution_id=args.execution_id, env=env, inventory=inventory,
                              ledger=load_ledger(protocol), changed_paths=changed_paths)
    print(json.dumps({"inventory": inventory}, indent=1))
    if problems:
        print("\n".join(f"REFUSED: {p}" for p in problems))
        return 1
    lines = [f"matrix={json.dumps(matrix(protocol, args.mode), separators=(',', ':'))}", f"mode={args.mode}",
             f"execution={args.execution_id}"]
    with open(args.github_output, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0


# ---------------------------------------------------------------- toolchain and environment check


def cmd_install_sdk(args, env=os.environ) -> int:
    refuse_foreign(env)
    sdk = dp.load(ROOT, dp.PROTOCOL)["toolchain"]["sdk"]
    root, work = Path(args.root).resolve(), Path(args.work).resolve()
    if root.exists():
        raise Refusal(f"{root} already exists; the private root must start empty")
    work.mkdir(parents=True, exist_ok=True)
    name = "install-dotnet.ps1" if os.name == "nt" else "install-dotnet.sh"
    with urllib.request.urlopen(INSTALLER["base"] + name, timeout=120) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != INSTALLER[name]:
        raise Refusal(f"{name} does not match its pinned SHA-256")
    script = work / name
    script.write_bytes(data)
    cmd = ["pwsh", "-NoProfile", "-NonInteractive", "-File", str(script), "-Version", sdk, "-InstallDir", str(root), "-NoPath"] \
        if os.name == "nt" else ["bash", str(script), "--version", sdk, "--install-dir", str(root), "--no-path"]
    subprocess.run(cmd, check=True)
    with open(env["GITHUB_PATH"], "a", encoding="utf-8") as handle:
        handle.write(f"{root}\n")
    with open(env["GITHUB_ENV"], "a", encoding="utf-8") as handle:
        handle.write(f"DOTNET_ROOT={root}\nDOTNET_MULTILEVEL_LOOKUP=0\n")
    return 0


def physical_memory_bytes():
    if os.name == "nt":
        import ctypes

        class Status(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + \
                [(n, ctypes.c_ulonglong) for n in ("total", "avail", "pageTotal", "pageAvail", "virtTotal", "virtAvail", "extAvail")]
        status = Status()
        status.dwLength = ctypes.sizeof(Status)
        return int(status.total) if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)) else None
    try:
        return os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    except (ValueError, OSError, AttributeError):
        return None


def home_probe(env_vars, probe: Path, nuget: str) -> bool:
    """Does the pinned runtime resolve SpecialFolder.UserProfile (the root of the verifier's default
    user-level cache, VerificationCacheOptions) to an isolated home? A probe project outside the
    repository, built with every output under the probe directory (no file-based-app cache), prints it;
    any failure is False. Only the comparison is recorded."""
    home = probe / "home"
    home.mkdir(parents=True, exist_ok=False)
    (probe / "Program.cs").write_text(PROBE, encoding="utf-8")
    (probe / "probe.csproj").write_text(PROBE_PROJECT, encoding="utf-8")
    child = invocation_env(env_vars, probe, "", nuget)
    try:
        built = subprocess.run(["dotnet", "build", str(probe / "probe.csproj"), "-c", "Release", "-o", str(probe / "bin")], cwd=probe,
                               env=child, capture_output=True, text=True, check=False, timeout=600)
        result = subprocess.run(["dotnet", str(probe / "bin" / "probe.dll")], cwd=probe, env=child, capture_output=True, text=True,
                                check=False, timeout=120) if built.returncode == 0 else built
    except (OSError, subprocess.TimeoutExpired):
        return False
    lines = result.stdout.strip().splitlines()
    return result.returncode == 0 and bool(lines) and os.path.normcase(os.path.abspath(lines[-1])) == os.path.normcase(str(home))


def observe(env_vars, dotnet_root: str, rid_entry: dict, out: Path) -> dict:
    def stdout(*cmd):
        try:
            result = subprocess.run(list(cmd), capture_output=True, text=True, check=False, env=dict(env_vars))
        except OSError as error:
            return f"<{error}>"
        return result.stdout if result.returncode == 0 else f"<exit {result.returncode}>"
    z3 = {asset: dp.sha256_file(ROOT / rel) if (ROOT / rel).exists() else None
          for asset, rel in ((rid_entry["asset"], rid_entry["path"]), ("Microsoft.Z3.dll", "src/Calor.Compiler/z3/Microsoft.Z3.dll"))}
    which = shutil.which("dotnet", path=env_vars.get("PATH"))
    status = git("status", "--porcelain", "--untracked-files=no")
    return {"sdks": [ln.strip() for ln in stdout("dotnet", "--list-sdks").splitlines() if ln.strip()],
            "runtimes": [ln.strip() for ln in stdout("dotnet", "--list-runtimes").splitlines() if ln.strip()],
            "dotnetVersion": stdout("dotnet", "--version").strip(), "dotnetPath": str(Path(which).resolve()) if which else None,
            "dotnetRoots": sorted({str(Path(dotnet_root).absolute()), str(Path(dotnet_root).resolve())}),
            "runnerOs": env_vars.get("RUNNER_OS"), "runnerArch": env_vars.get("RUNNER_ARCH"),
            "logicalProcessors": os.cpu_count(), "memoryBytes": physical_memory_bytes(), "imageOs": env_vars.get("ImageOS"),
            "imageVersion": env_vars.get("ImageVersion"), "runnerName": env_vars.get("RUNNER_NAME"), "z3": z3,
            "commit": stdout("git", "-C", str(ROOT), "rev-parse", "HEAD").strip(),
            "autocrlf": stdout("git", "-C", str(ROOT), "config", "--get", "core.autocrlf").strip(),
            "dirty": status.stdout.strip() if status.returncode == 0 else f"<git status exit {status.returncode}>",
            "runAttempt": env_vars.get("GITHUB_RUN_ATTEMPT"),
            "userProfileFollowsIsolatedHome": home_probe(env_vars, out / "home-probe", env_vars.get("NUGET_PACKAGES", ""))}


def cmd_env_check(args, env_vars=os.environ) -> int:
    refuse_foreign(env_vars)
    protocol = dp.load(ROOT, dp.PROTOCOL)
    env = dp.env_by_id(protocol)[args.env]
    rid = next(r for r in dp.load(ROOT, dp.Z3_CONSUMERS)["supportedRids"] if r["rid"] == env["rid"])
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    observed = observe(env_vars, args.dotnet_root, rid, out)
    check = {"violations": dp.judge(protocol, env, observed, env_vars["GITHUB_SHA"]), "observed": observed}
    write_json(out / "env.json", check)
    print(json.dumps(check, indent=1))
    return 0  # a violation is recorded per attempt by run-job, which then fails the job


# ---------------------------------------------------------------- attempts


def invocation_env(base, invocation_dir: Path, rid: str, nuget: str) -> dict:
    """Environment of one test invocation: a fresh home under the job's output directory, no
    CALOR_UPDATE_* variable, the expected RID, and an absolute record directory."""
    home, record = (invocation_dir / "home").resolve(), (invocation_dir / "record").resolve()
    child = {k: v for k, v in base.items() if not k.upper().startswith("CALOR_UPDATE_")}
    child.update({k: str(home) for k in ISOLATED})
    child.update({k: str(home / sub) for k, sub in UNDER_HOME.items()})
    child.update(NUGET_PACKAGES=nuget, CALOR_Z3_EXPECTED_RID=rid, CALOR_DETERMINISM_RECORD_DIR=str(record), MSBUILDDISABLENODEREUSE="1")
    return child


def check_isolation(child: dict, invocation_dir: Path) -> None:
    """Fail closed unless every home variable names the invocation's own fresh, empty directory."""
    base = invocation_dir.resolve()
    for key in ISOLATED:
        value = child.get(key)
        if not value or not Path(value).is_absolute() or Path(value).resolve().parent != base:
            raise Refusal(f"{key}={value!r} is not an isolated home under {base}")
        if Path(value).exists() and any(Path(value).iterdir()):
            raise Refusal(f"{key}={value} is not empty before the invocation")
    home = Path(child[ISOLATED[0]]).resolve()
    if any(Path(child.get(k) or ".").resolve() != home / sub for k, sub in UNDER_HOME.items()):
        raise Refusal(f"a per-user configuration or cache root ({', '.join(UNDER_HOME)}) is not under the invocation's home")
    if len({child[k] for k in ISOLATED}) != 1 or not Path(child.get("CALOR_DETERMINISM_RECORD_DIR", "")).is_absolute() \
            or not Path(child.get("NUGET_PACKAGES", "")).is_absolute() or any(k.upper().startswith("CALOR_UPDATE_") for k in child):
        raise Refusal("home variables differ, a record or package path is relative, or a CALOR_UPDATE_* variable remains")


def test_command(profile, cases, trx_dir: Path) -> list[str]:
    cmd = ["dotnet", "test", profile["project"], "-c", "Release", "--no-build",
           "--logger", f"trx;LogFileName={profile['id']}.trx", "--results-directory", str(trx_dir.resolve())]
    flt = "|".join(f"FullyQualifiedName~{c}." for c in cases["groups"][profile["group"]]["classes"]) \
        if profile.get("filter") == "FROM-CASES" else profile.get("filter")
    return cmd + (["--filter", flt] if flt else [])


def run_with_timeout(cmd, env, seconds: float, log: Path) -> tuple[int | None, bool]:
    kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with open(log, "wb") as handle:
        process = subprocess.Popen(cmd, cwd=ROOT, env=env, stdout=handle, stderr=subprocess.STDOUT, **kwargs)
        try:
            return process.wait(timeout=max(seconds, 1)), False
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            return process.returncode, True


def run_profile(protocol, cases, profile, env, invocation_dir: Path, base, nuget, deadline: float) -> dict:
    invocation_dir.mkdir(parents=True, exist_ok=False)  # never reuses an earlier invocation's directory
    child = invocation_env(base, invocation_dir, env["rid"], nuget)
    trx_dir = invocation_dir / "trx"
    for d in (Path(child["HOME"]), trx_dir):
        d.mkdir()
    check_isolation(child, invocation_dir)
    existed = (Path(child["HOME"]) / ".calor").exists()
    started = time.monotonic()
    budget = profile["processTimeoutMinutes"] * 60
    code, timed_out = run_with_timeout(test_command(profile, cases, trx_dir), child, min(budget, deadline - time.time()),
                                       invocation_dir / "console.log")
    cut = timed_out and time.monotonic() - started < budget - 1  # killed by the job deadline, not the process timeout
    trx, outcomes, summary, broken = trx_dir / f"{profile['id']}.trx", None, "Missing", None
    if trx.exists():  # read even after a timeout: every observed value is kept
        try:
            outcomes, summary = dp.parse_trx(trx)
        except Exception as error:  # noqa: BLE001 - a malformed TRX is a crash value, never a harness stop
            outcomes, summary, broken = None, "Malformed", error
    try:
        values = dp.profile_values(profile, cases, outcomes, summary, code, timed_out, Path(child["CALOR_DETERMINISM_RECORD_DIR"]))
    except Exception as error:  # noqa: BLE001 - keep every value that can still be read, file by file
        record, broken = Path(child["CALOR_DETERMINISM_RECORD_DIR"]), error
        values = dp.profile_values(profile, cases, outcomes, summary, code, timed_out, None)
        if values["cells"] is not None:
            try:
                values["cells"] = dp.profile_values(profile, dict(cases, artifacts=[]), outcomes, summary, code, timed_out, record)["cells"]
            except Exception:  # noqa: BLE001 - cells.json itself is unreadable
                values["cells"] = {k: "Malformed" for k in values["cells"]}
        for name in values["artifacts"]:
            try:
                if (record / "generated" / name).exists():
                    values["artifacts"][name] = dp.sha256_file(record / "generated" / name)
            except OSError:
                pass
    fills = any(part in dp.FILL_VALUES for val in values["tests"].values() for part in val.split(","))
    status = "invalid" if cut or (broken and outcomes is not None) else "timeout" if timed_out else "crash" if fills else \
        "invalid" if outcomes is None else "completed"
    result = {"profile": profile["id"], "status": status, "exitCode": code, "seconds": round(time.monotonic() - started, 1),
              "calorCacheExisted": existed, **values}
    return result


def base_record(protocol, mode, execution_id, env_id, job, attempt, base) -> dict:
    return {"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"], "protocolSha256": dp.sha256_file(ROOT / dp.PROTOCOL),
            "harnessSha256": dp.harness_digest(ROOT), "mode": mode, "executionId": execution_id, "environment": env_id,
            "job": job, "attempt": attempt, "runId": base.get("GITHUB_RUN_ID"), "runAttempt": base.get("GITHUB_RUN_ATTEMPT"),
            "commit": git("rev-parse", "HEAD").stdout.strip(), "status": None, "environmentCheck": None, "profiles": []}


def write_json(path: Path, data) -> None:
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes((json.dumps(data, indent=1, sort_keys=True) + "\n").encode("utf-8"))
    os.replace(tmp, path)  # a kill mid-write never leaves a truncated record


def record_path(out: Path, env_id, job, attempt) -> Path:
    return out / f"attempt-{env_id}-j{job}-a{attempt:02d}.json"


def tree_dirty() -> str:
    result = git("status", "--porcelain", "--untracked-files=no")
    return result.stdout.strip() if result.returncode == 0 else f"<git status exit {result.returncode}>"


def run_job(protocol, cases, *, env_id, job, mode, execution_id, out: Path, base, job_timeout=None, dirty=tree_dirty) -> int:
    """Run every attempt of one job exactly once, in order. Test failures are data; the return code is
    non-zero only when the environment was violated, the tree was modified, or the job deadline cut it."""
    refuse_foreign(base)
    env = dp.env_by_id(protocol)[env_id]
    _, jobs, attempts = shape(protocol, mode)
    out = out.resolve()
    nuget = base.get("NUGET_PACKAGES") or ""
    if job not in range(1, jobs + 1) or not EXECUTION_ID.match(execution_id):
        raise Refusal(f"job {job} or execution id {execution_id!r} is not registered")
    if (out / "started").exists() or any(out.glob("attempt-*.json")):
        raise Refusal(f"{out} already holds attempts of this job; no attempt is run twice")
    if not Path(nuget).is_absolute():
        raise Refusal("NUGET_PACKAGES must be the absolute package path the build step used")
    try:  # stop 5 minutes before the job timeout, so fill-missing and the upload always run
        deadline = float(base["JOB_STARTED"]) + (int(job_timeout) - 5) * 60
    except (KeyError, ValueError, TypeError):
        raise Refusal("JOB_STARTED (epoch seconds) and the job timeout are required") from None
    check_file = out / "env.json"
    check = json.loads(check_file.read_text(encoding="utf-8")) if check_file.exists() else \
        {"violations": ["the environment check recorded nothing"], "observed": {}}
    before = dirty()
    if before and not check["violations"]:  # the build must not modify tracked files either
        check = dict(check, violations=[f"tracked files differ after the build: {before}"])
    (out / "started").write_text("1\n", encoding="utf-8")
    stop = None
    for attempt in range(1, attempts + 1):
        record = base_record(protocol, mode, execution_id, env_id, job, attempt, base)
        record["environmentCheck"] = check
        if check["violations"]:
            record["status"] = "environment-violation"
        elif stop:
            record.update(status="invalid", reason=stop)
        else:
            for profile in env_profiles(protocol, mode, env):
                if time.time() >= deadline:
                    stop = "the job deadline was reached before this invocation"
                    break
                result = run_profile(protocol, cases, profile, env, out / "raw" / f"a{attempt:02d}" / profile["id"], base, nuget, deadline)
                record["profiles"].append(result)
                modified = dirty()
                if modified:
                    result["status"], stop = "invalid", f"an invocation modified tracked files: {modified}"
                elif result["status"] == "invalid" and time.time() >= deadline - 1:
                    stop = "the job deadline cut an invocation"
                if stop:
                    break
                record.update(status="invalid", reason="attempt in progress")  # replaced when the attempt ends
                write_json(record_path(out, env_id, job, attempt), record)
            if stop:
                record.update(status="invalid", reason=stop)
            else:
                statuses = [p["status"] for p in record["profiles"]]
                record.pop("reason", None)
                record["status"] = next(s for s in ("invalid", "timeout", "crash", "completed") if s in statuses + ["completed"])
        write_json(record_path(out, env_id, job, attempt), record)
    return 1 if check["violations"] or stop else 0


def fill_missing(protocol, *, env_id, job, mode, execution_id, out: Path, base) -> int:
    refuse_foreign(base)
    _, _, attempts = shape(protocol, mode)
    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    started = (out / "started").exists()
    check = json.loads((out / "env.json").read_text(encoding="utf-8")) if (out / "env.json").exists() else None
    for attempt in range(1, attempts + 1):
        if record_path(out, env_id, job, attempt).exists():
            continue  # an existing record is never replaced
        record = base_record(protocol, mode, execution_id, env_id, job, attempt, base)
        if check is not None and check.get("violations"):
            record.update(environmentCheck=check, status="environment-violation")
        elif started and check is not None:
            record.update(environmentCheck=check, status="invalid", reason="the harness stopped before this attempt")
        else:
            record.update(status="infrastructure-failure", reason="a setup step failed before any test ran")
        write_json(record_path(out, env_id, job, attempt), record)
    return 0


# ---------------------------------------------------------------- decide and ledger


def cmd_decide(args, env=os.environ) -> int:
    refuse_foreign(env)
    return dp.main(["decide", "--records", args.records, "--execution-id", args.execution_id, "--run-id", env["GITHUB_RUN_ID"],
                    "--commit", env["GITHUB_SHA"], "--mode", args.mode, "--out", args.out])


def ledger_problems(ledger: dict, inventory: list[dict]) -> list[str]:
    """executions.ledgerCheck, both ways: every dispatched run is in the ledger with its measured minutes,
    and every ledger run is still in the inventory."""
    entries = {str(e.get("runId")): e for e in ledger.get("entries", [])}
    dispatched = {str(r["id"]): r for r in inventory if r["event"] == "workflow_dispatch"}
    problems = [f"ledger run {i} is not in the API inventory (deleted?)" for i in entries if i not in dispatched]
    for i, run in dispatched.items():
        entry = entries.get(i)
        if entry is None:
            problems.append(f"run {i} ({run['title']}) is not in the ledger")
        elif entry.get("commit") != run["headSha"] or entry.get("mode") != classify(run):
            problems.append(f"run {i}: ledger commit or mode differs from the API inventory")
        elif run["status"] == "completed" and entry.get("runnerMinutes") != run["minutes"]:
            problems.append(f"run {i}: ledger says {entry.get('runnerMinutes')} runner-minutes, measured {run['minutes']}")
    return problems


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("plan")
    for name in ("--mode", "--execution-id", "--main-validator", "--github-output"):
        p.add_argument(name, required=True)
    p = sub.add_parser("install-sdk")
    p.add_argument("--root", required=True)
    p.add_argument("--work", required=True)
    for name in ("env-check", "run-job", "fill-missing"):
        p = sub.add_parser(name)
        p.add_argument("--env", required=True)
        p.add_argument("--out", required=True)
        if name == "env-check":
            p.add_argument("--dotnet-root", required=True)
        else:
            p.add_argument("--job", type=int, required=True)
            p.add_argument("--mode", required=True, choices=sorted(dp.MODES))
            p.add_argument("--execution-id", required=True)
            if name == "run-job":
                p.add_argument("--job-timeout", type=int, required=True)
    p = sub.add_parser("decide")
    for name in ("--records", "--execution-id", "--out"):
        p.add_argument(name, required=True)
    p.add_argument("--mode", required=True, choices=sorted(dp.MODES))
    p = sub.add_parser("ledger-check")
    p.add_argument("--ledger", required=True)
    args = parser.parse_args(argv)
    try:
        if args.command in ("run-job", "fill-missing"):
            protocol = dp.load(ROOT, dp.PROTOCOL)
            kwargs = dict(env_id=args.env, job=args.job, mode=args.mode, execution_id=args.execution_id, out=Path(args.out), base=os.environ)
            if args.command == "run-job":
                return run_job(protocol, dp.load(ROOT, dp.CASES), job_timeout=args.job_timeout, **kwargs)
            return fill_missing(protocol, **kwargs)
        if args.command == "ledger-check":
            problems = ledger_problems(json.loads(Path(args.ledger).read_text(encoding="utf-8")),
                                       fetch_inventory(os.environ["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"]))
            print("\n".join(problems) or "ledger and inventory list the same dispatched runs")
            return 1 if problems else 0
        return {"plan": cmd_plan, "install-sdk": cmd_install_sdk, "env-check": cmd_env_check, "decide": cmd_decide}[args.command](args)
    except Refusal as error:
        print(f"REFUSED: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
