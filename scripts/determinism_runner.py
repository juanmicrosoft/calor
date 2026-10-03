#!/usr/bin/env python3
"""0.24 G2 (#1421): execution machinery for the registered verifier determinism protocol.

Run only by .github/workflows/determinism-protocol.yml (workflow_dispatch only). The protocol is
docs/plans/evidence/g2-1421/protocol.json; the value rules and the decider are frozen in
scripts/determinism_protocol.py and imported, never copied.

  plan          validate the packet (this tree and main's validator, D016); refuse a non-dispatch
                event, a re-run, a reused execution id, an executed commit, a docs-only repair, and a
                run over the budget or the run limits, all counted from the GitHub API run inventory
  env-check     check and record one job's environment (toolchain, OS, CPU, memory, Z3, tree)
  run-job       run every attempt of one job once, writing the attempt record after every invocation
  fill-missing  record every attempt of a job that has no record (never re-runs one)
  decide        the frozen decider, bound to the dispatched commit and this run
  ledger-check  every dispatched run is in #1135's ledger with its measured runner-minutes

Nothing here retries an attempt, re-runs a job or a commit, normalizes a compared value, or looks
up, reads, writes, or deletes the real home directory: every test invocation gets a fresh HOME,
USERPROFILE, and DOTNET_CLI_HOME under the job's output directory.
"""
from __future__ import annotations

import argparse
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
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import determinism_protocol as dp  # noqa: E402

ROOT = dp.ROOT
WORKFLOW = ".github/workflows/determinism-protocol.yml"
RUNNER = "scripts/determinism_runner.py"
EXECUTION_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9-]{0,39}$")
TITLE = re.compile(r"^determinism (control|execution) (\S+)$")
ALWAYS_STEPS = {"Record attempts that did not run", "Upload attempt records", "Upload the result record"}
DECIDE_IF = "always() && needs.plan.result == 'success'"
ISOLATED = ("HOME", "USERPROFILE", "DOTNET_CLI_HOME")


class Refusal(Exception):
    """A guard refused to proceed. The workflow step fails; nothing is retried."""


def refuse_rerun(env) -> None:
    if str(env.get("GITHUB_RUN_ATTEMPT", "")) != "1":
        raise Refusal(f"GITHUB_RUN_ATTEMPT={env.get('GITHUB_RUN_ATTEMPT')}: no job or workflow run is run again (retryPolicy)")


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


# ---------------------------------------------------------------- workflow structure


def workflow_problems(text: str, protocol) -> list[str]:
    """The workflow is dispatch-only with exactly the registered inputs, three jobs, the protocol's
    timeouts, fail-fast false, no continue-on-error, no status masking, and if: only on the steps
    that keep records after a failure."""
    problems, lines = [], text.splitlines()
    top = [ln for ln in lines if ln and not ln.startswith((" ", "#"))]
    if sorted(t.split(":")[0] for t in top) != sorted(["name", "run-name", "on", "permissions", "concurrency", "jobs"]) or "on:" not in top:
        problems.append(f"top-level keys differ from the registered set: {top}")
    block = lambda key, indent: [ln for ln in _block(lines, key, indent) if ln.strip() and not ln.strip().startswith("#")]  # noqa: E731
    triggers = [ln.strip() for ln in block("on:", 0) if len(ln) - len(ln.lstrip()) == 2]
    if triggers != ["workflow_dispatch:"]:
        problems.append(f"triggers must be exactly workflow_dispatch (no automatic trigger): {triggers}")
    inputs = [ln.strip() for ln in block("    inputs:", 4) if len(ln) - len(ln.lstrip()) == 6]
    if inputs != ["mode:", "execution_id:"] or "        options: [control, execution]" not in lines:
        problems.append(f"inputs must be exactly mode (control|execution) and execution_id: {inputs}")
    if sorted(ln.strip() for ln in block("permissions:", 0)) != ["actions: read", "contents: read"]:
        problems.append("permissions must be exactly contents: read and actions: read")
    if [ln.strip() for ln in block("concurrency:", 0)] != ["group: determinism-protocol", "cancel-in-progress: false"]:
        problems.append("concurrency must be one global group that never cancels a run in progress")
    code = "\n".join(ln for ln in lines if not ln.strip().startswith("#"))
    if re.search(r"continue-on-error|\|\|\s*(true\b|:(?!\S)|exit 0\b)|set \+[a-z]*e\b", code):
        problems.append("a step may fail silently (continue-on-error, || true, or set +e)")
    jobs = dp.parse_workflow(text)
    budget = protocol["budget"]
    expected = {"plan": str(budget["planJobTimeoutMinutes"]), "attempts": "${{ matrix.timeout }}",
                "decide": str(budget["decideJobTimeoutMinutes"])}
    if sorted(jobs) != sorted(expected):
        return problems + [f"jobs must be exactly plan, attempts, decide: {sorted(jobs)}"]
    for name, job in jobs.items():
        keys = job["keys"]
        if keys.get("timeout-minutes") != expected[name] or "__unparsed__" in keys \
                or keys.get("if") != (DECIDE_IF if name == "decide" else None):
            problems.append(f"job {name}: timeout differs from the protocol, a key is unparsed, or it has a condition")
        for step in job["steps"]:
            if "__unparsed__" in step or ("if" in step and (step.get("if") != "always()" or step.get("name") not in ALWAYS_STEPS)):
                problems.append(f"job {name} step {step.get('name')!r}: unparsed key or an unregistered condition")
            if "run" in step and "\n" in step["run"] and not step["run"].startswith("set -euo pipefail"):
                problems.append(f"job {name} step {step.get('name')!r}: a multi-line script must start with set -euo pipefail")
    if "fail-fast: false" not in jobs["attempts"]["keys"].get("strategy", "") or jobs["attempts"]["keys"].get("runs-on") != "${{ matrix.runner }}":
        problems.append("attempts must take runner labels from the plan matrix with fail-fast false")
    return problems


def _block(lines, key, indent) -> list[str]:
    try:
        start = lines.index(key)
    except ValueError:
        return []
    out = []
    for ln in lines[start + 1:]:
        if ln.strip() and len(ln) - len(ln.lstrip()) <= indent and not ln.strip().startswith("#"):
            break
        out.append(ln)
    return out


# ---------------------------------------------------------------- plan: history, budget, repairs


def classify(run) -> str:
    match = TITLE.match(run.get("title") or "")
    return match.group(1) if match else "unknown"  # an unreadable title is charged as an execution


def plan_problems(protocol, *, mode, execution_id, env, inventory, changed_paths) -> list[str]:
    """Guards before any attempt runs. inventory is every run of the workflow from the GitHub API
    (see fetch_inventory); changed_paths(old, new) lists changed paths, or None if git cannot tell."""
    problems = []
    if env.get("GITHUB_EVENT_NAME") != "workflow_dispatch":
        problems.append(f"event {env.get('GITHUB_EVENT_NAME')}: only a workflow_dispatch run may execute the protocol")
    try:
        refuse_rerun(env)
    except Refusal as error:
        problems.append(str(error))
    if mode not in dp.MODES or not EXECUTION_ID.match(execution_id or ""):
        problems.append("mode must be control or execution and the execution id match [A-Za-z0-9][A-Za-z0-9-]{0,39}")
        return problems
    if not re.match(r"^[0-9a-f]{40}$", env.get("GITHUB_SHA") or ""):
        problems.append("GITHUB_SHA is not a full commit SHA")
    commit, here = env.get("GITHUB_SHA"), str(env.get("GITHUB_RUN_ID"))
    prior = [r for r in inventory if r["event"] == "workflow_dispatch" and str(r["id"]) != here]
    b = protocol["budget"]
    execution_worst, control_worst, _ = dp.worst_case(protocol)
    worst = {"execution": execution_worst, "control": control_worst, "unknown": execution_worst}
    finished = [r for r in prior if r["status"] == "completed"]
    unfinished = [r for r in prior if r["status"] != "completed"]
    recorded = b["dryRunSpent"] + sum(r["minutes"] for r in finished)
    reserved = sum(worst[classify(r)] for r in unfinished)
    if recorded + reserved + worst[mode] > b["ceilingRunnerMinutes"]:
        problems.append(f"budget: {recorded} recorded + {reserved} reserved for unfinished runs + {worst[mode]} for this run "
                        f"exceeds the {b['ceilingRunnerMinutes']} runner-minute ceiling (contract stopping rule 1)")
    started = [r for r in prior if r["status"] != "completed" or r["attemptsStarted"]]
    executions = [r for r in started if classify(r) != "control"]
    limit, count = (b["maxExecutions"], len(executions)) if mode == "execution" else \
        (b["maxDispatchedControlRuns"], len(started) - len(executions))
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
        # The machinery cannot read a prior verdict from the API, so every execution run that did not
        # conclude success is treated as possibly NON-DETERMINISTIC or FAILING (fail closed).
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
            # The attempt step stops 5 minutes before the job timeout so the records are always uploaded.
            include.append({"env": env["id"], "job": job, "runner": env["runner"], "timeout": timeout, "stepTimeout": timeout - 5,
                            "sdk": protocol["toolchain"]["sdk"], "projects": " ".join(projects)})
    return {"include": include}


def api(path: str, token: str):
    request = urllib.request.Request(f"https://api.github.com{path}", headers={
        "Authorization": f"Bearer {token}", "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read())


def paged(path: str, key: str, token: str) -> list[dict]:
    items, page = [], 1
    while True:
        data = api(f"{path}{'&' if '?' in path else '?'}per_page=100&page={page}", token)
        items += data[key]
        if len(data[key]) < 100:
            if len(items) != data.get("total_count", len(items)):
                raise Refusal(f"{path}: listed {len(items)} of {data.get('total_count')} items")
            return items
        page += 1


def job_minutes(jobs: list[dict]) -> int:
    """budget.accounting: each job's completed_at - started_at, rounded up to a minute, no OS multiplier."""
    total = 0
    for job in jobs:
        if job.get("started_at") and job.get("completed_at"):
            start, end = (datetime.fromisoformat(job[k].replace("Z", "+00:00")) for k in ("started_at", "completed_at"))
            total += math.ceil(max(0.0, (end - start).total_seconds()) / 60)
    return total


def fetch_inventory(repo: str, token: str) -> list[dict]:
    """Every run of the workflow (all branches, events, and attempts) with its measured minutes."""
    out = []
    for run in paged(f"/repos/{repo}/actions/workflows/determinism-protocol.yml/runs", "workflow_runs", token):
        jobs = paged(f"/repos/{repo}/actions/runs/{run['id']}/jobs?filter=all", "jobs", token)
        out.append({"id": run["id"], "runNumber": run["run_number"], "title": run.get("display_title"), "event": run["event"],
                    "status": run["status"], "conclusion": run.get("conclusion"), "headSha": run["head_sha"],
                    "minutes": job_minutes(jobs),
                    "attemptsStarted": any(j["name"].startswith("attempts") and j.get("started_at") and j.get("conclusion") != "skipped"
                                           for j in jobs)})
    return out


def git(*args) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)


def changed_paths(old: str, new: str):
    result = git("diff", "--name-only", f"{old}..{new}")
    return result.stdout.split() if result.returncode == 0 else None


def cmd_plan(args, env=os.environ) -> int:
    protocol = dp.load(ROOT, dp.PROTOCOL)
    problems = [f"{c}: {m}" for c, m in dp.validate(ROOT, baseline=dp.load_baseline(ROOT, "origin/main"))]
    trusted = subprocess.run([sys.executable, args.main_validator, "validate", "--root", str(ROOT), "--baseline-ref", "origin/main"],
                             capture_output=True, text=True, check=False)
    if trusted.returncode != 0:
        problems.append(f"main's validator rejects this tree:\n{trusted.stdout}{trusted.stderr}")
    problems += workflow_problems((ROOT / WORKFLOW).read_text(encoding="utf-8"), protocol)
    inventory = fetch_inventory(env["GITHUB_REPOSITORY"], env["GH_TOKEN"])
    problems += plan_problems(protocol, mode=args.mode, execution_id=args.execution_id, env=env, inventory=inventory,
                              changed_paths=changed_paths)
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


# ---------------------------------------------------------------- environment check


def physical_memory_bytes():
    if os.name == "nt":
        import ctypes

        class Status(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + \
                [(n, ctypes.c_ulonglong) for n in ("total", "avail", "pageTotal", "pageAvail", "virtTotal", "virtAvail", "extAvail")]
        status = Status()
        status.dwLength = ctypes.sizeof(Status)
        return status.total if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)) else None
    try:
        return os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    except (ValueError, OSError, AttributeError):
        return None


def home_probe(env_vars, probe: Path):
    """Does .NET resolve SpecialFolder.UserProfile (the root of the verifier's user-level cache) to an
    overridden HOME/USERPROFILE? Only the comparison is recorded; None when no pwsh is installed."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        return None
    probe.mkdir(parents=True, exist_ok=True)
    child = dict(env_vars)
    child.update({k: str(probe.resolve()) for k in ISOLATED})
    got = subprocess.run([pwsh, "-NoProfile", "-NonInteractive", "-Command", "[Environment]::GetFolderPath('UserProfile')"],
                         env=child, capture_output=True, text=True, check=False).stdout.strip()
    return bool(got) and os.path.normcase(os.path.abspath(got)) == os.path.normcase(str(probe.resolve()))


def observe(env_vars, dotnet_root: str, rid_entry: dict, out: Path) -> dict:
    def out(*cmd):
        try:
            return subprocess.run(list(cmd), capture_output=True, text=True, check=False).stdout
        except OSError as error:
            return f"<{error}>"
    pins = {p[1]: p[0] for p in (ln.split() for ln in (ROOT / ".github/z3-binaries-4.15.7.sha256").read_text(encoding="utf-8").splitlines()
                                 if ln.strip() and not ln.startswith("#")) if len(p) == 3}
    z3 = {}
    for asset, rel in ((rid_entry["asset"], rid_entry["path"]), ("Microsoft.Z3.dll", "src/Calor.Compiler/z3/Microsoft.Z3.dll")):
        z3[asset] = {"sha256": dp.sha256_file(ROOT / rel) if (ROOT / rel).exists() else None, "pin": pins.get(asset)}
    which = shutil.which("dotnet")
    return {"sdks": [ln.strip() for ln in out("dotnet", "--list-sdks").splitlines() if ln.strip()],
            "runtimes": [ln.strip() for ln in out("dotnet", "--list-runtimes").splitlines() if ln.strip()],
            "dotnetVersion": out("dotnet", "--version").strip(), "dotnetPath": str(Path(which).resolve()) if which else None,
            "dotnetRoots": sorted({str(Path(dotnet_root).absolute()), str(Path(dotnet_root).resolve())}),
            "runnerOs": env_vars.get("RUNNER_OS"), "runnerArch": env_vars.get("RUNNER_ARCH"),
            "logicalProcessors": os.cpu_count(), "memoryBytes": physical_memory_bytes(), "imageOs": env_vars.get("ImageOS"),
            "imageVersion": env_vars.get("ImageVersion"), "runnerName": env_vars.get("RUNNER_NAME"), "z3": z3,
            "commit": git("rev-parse", "HEAD").stdout.strip(), "autocrlf": git("config", "--get", "core.autocrlf").stdout.strip(),
            "dirty": git("status", "--porcelain", "--untracked-files=no").stdout.strip(), "runAttempt": env_vars.get("GITHUB_RUN_ATTEMPT"),
            "userProfileFollowsIsolatedHome": home_probe(env_vars, out / "home-probe")}


def judge(protocol, env: dict, observed: dict, commit: str) -> list[str]:
    tc, v = protocol["toolchain"], []
    sdks = [s.split()[0] for s in observed["sdks"]]
    netcore = [r.split()[1] for r in observed["runtimes"] if r.startswith("Microsoft.NETCore.App ")]
    roots = {os.path.normcase(r) for r in observed["dotnetRoots"]}
    inside = lambda s: s and any(r in os.path.normcase(s) for r in roots)  # noqa: E731
    if sdks != [tc["sdk"]] or observed["dotnetVersion"] != tc["sdk"]:
        v.append(f"SDKs {sdks} (dotnet --version {observed['dotnetVersion']}) are not exactly {tc['sdk']}")
    if netcore != [tc["runtime"]]:
        v.append(f"Microsoft.NETCore.App runtimes {netcore} are not exactly {tc['runtime']}")
    if not inside(observed["dotnetPath"]) or not all(inside(s) for s in observed["sdks"] + observed["runtimes"]):
        v.append(f"dotnet {observed['dotnetPath']} or an SDK or runtime is outside the private root {sorted(roots)}")
    if (observed["runnerOs"], observed["runnerArch"]) != (env["runnerOs"], env["runnerArch"]):
        v.append(f"runner {observed['runnerOs']}/{observed['runnerArch']} is not {env['runnerOs']}/{env['runnerArch']}")
    if observed["logicalProcessors"] != env["logicalProcessors"]:
        v.append(f"{observed['logicalProcessors']} logical processors, registered {env['logicalProcessors']}")
    if not observed["memoryBytes"] or observed["memoryBytes"] < 0.9 * env["memoryGiB"] * 2 ** 30:
        v.append(f"physical memory {observed['memoryBytes']} bytes is below 90% of {env['memoryGiB']} GiB")
    v += [f"Z3 asset {a} {h['sha256']} does not match its pin {h['pin']}" for a, h in observed["z3"].items() if not h["pin"] or h["sha256"] != h["pin"]]
    if observed["userProfileFollowsIsolatedHome"] is False or (observed["userProfileFollowsIsolatedHome"] is None and env["runnerOs"] == "Windows"):
        v.append("SpecialFolder.UserProfile does not follow the isolated HOME/USERPROFILE, so the verifier's user-level cache "
                 "would not start empty in each invocation (isolation.perInvocation)")
    if observed["commit"] != commit:
        v.append(f"HEAD {observed['commit']} is not the dispatched commit {commit}")
    if observed["autocrlf"] != "false":
        v.append(f"core.autocrlf is {observed['autocrlf']!r}, not false")
    if observed["dirty"]:
        v.append("tracked files differ from the commit before the first attempt")
    return v


def cmd_env_check(args, env_vars=os.environ) -> int:
    refuse_rerun(env_vars)
    protocol = dp.load(ROOT, dp.PROTOCOL)
    env = dp.env_by_id(protocol)[args.env]
    rid = next(r for r in dp.load(ROOT, dp.Z3_CONSUMERS)["supportedRids"] if r["rid"] == env["rid"])
    out = Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    observed = observe(env_vars, args.dotnet_root, rid, out)
    check = {"violations": judge(protocol, env, observed, env_vars["GITHUB_SHA"]), "observed": observed}
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
            return process.wait(timeout=seconds), False
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            return process.returncode, True


def run_profile(protocol, cases, profile, env, invocation_dir: Path, base, nuget) -> dict:
    invocation_dir.mkdir(parents=True, exist_ok=False)  # never reuses an earlier invocation's directory
    child = invocation_env(base, invocation_dir, env["rid"], nuget)
    trx_dir = invocation_dir / "trx"
    for d in (Path(child["HOME"]), trx_dir):
        d.mkdir()
    check_isolation(child, invocation_dir)
    existed = (Path(child["HOME"]) / ".calor").exists()
    started = time.monotonic()
    code, timed_out = run_with_timeout(test_command(profile, cases, trx_dir), child, profile["processTimeoutMinutes"] * 60,
                                       invocation_dir / "console.log")
    trx, outcomes, summary = trx_dir / f"{profile['id']}.trx", None, "Missing"
    if trx.exists():  # read even after a timeout: every observed value is kept
        try:
            outcomes, summary = dp.parse_trx(trx)
        except (ET.ParseError, KeyError):
            outcomes, summary = None, "Malformed"
    values = dp.profile_values(profile, cases, outcomes, summary, code, timed_out, Path(child["CALOR_DETERMINISM_RECORD_DIR"]))
    fills = any(part in dp.FILL_VALUES for val in values["tests"].values() for part in val.split(","))
    status = "timeout" if timed_out else "crash" if outcomes is None or fills else "completed"
    return {"profile": profile["id"], "status": status, "exitCode": code, "seconds": round(time.monotonic() - started, 1),
            "calorCacheExisted": existed, **values}


def base_record(protocol, mode, execution_id, env_id, job, attempt, base) -> dict:
    return {"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"], "protocolSha256": dp.sha256_file(ROOT / dp.PROTOCOL),
            "harnessSha256": dp.sha256_file(ROOT / dp.HARNESS), "mode": mode, "executionId": execution_id, "environment": env_id,
            "job": job, "attempt": attempt, "runId": base.get("GITHUB_RUN_ID"), "runAttempt": base.get("GITHUB_RUN_ATTEMPT"),
            "commit": git("rev-parse", "HEAD").stdout.strip(), "status": None, "environmentCheck": None, "profiles": []}


def write_json(path: Path, data) -> None:
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes((json.dumps(data, indent=1, sort_keys=True) + "\n").encode("utf-8"))
    os.replace(tmp, path)  # a kill mid-write never leaves a truncated record


def record_path(out: Path, env_id, job, attempt) -> Path:
    return out / f"attempt-{env_id}-j{job}-a{attempt:02d}.json"


def tree_dirty() -> str:
    return git("status", "--porcelain", "--untracked-files=no").stdout.strip()


def run_job(protocol, cases, *, env_id, job, mode, execution_id, out: Path, base, dirty=tree_dirty) -> int:
    """Run every attempt of one job exactly once, in order. Test failures are data; the return code
    is non-zero only when the environment was violated or the tree was modified."""
    refuse_rerun(base)
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
    check_file = out / "env.json"
    check = json.loads(check_file.read_text(encoding="utf-8")) if check_file.exists() else \
        {"violations": ["the environment check recorded nothing"], "observed": {}}
    (out / "started").write_text("1\n", encoding="utf-8")
    modified = None
    for attempt in range(1, attempts + 1):
        record = base_record(protocol, mode, execution_id, env_id, job, attempt, base)
        record["environmentCheck"] = check
        if check["violations"]:
            record["status"] = "environment-violation"
        elif modified:
            record.update(status="invalid", reason=f"an earlier invocation modified tracked files: {modified}")
        else:
            for profile in env_profiles(protocol, mode, env):
                result = run_profile(protocol, cases, profile, env, out / "raw" / f"a{attempt:02d}" / profile["id"], base, nuget)
                record["profiles"].append(result)
                modified = dirty() or None
                if modified:
                    result["status"] = "invalid"
                    record.update(status="invalid", reason=f"this invocation modified tracked files: {modified}")
                    break
                record.update(status="invalid", reason="attempt in progress")  # replaced when the attempt ends
                write_json(record_path(out, env_id, job, attempt), record)
            if not modified:
                statuses = [p["status"] for p in record["profiles"]]
                record.pop("reason", None)
                record["status"] = next(s for s in ("invalid", "timeout", "crash", "completed") if s in statuses + ["completed"])
        write_json(record_path(out, env_id, job, attempt), record)
    return 1 if check["violations"] or modified else 0


def fill_missing(protocol, *, env_id, job, mode, execution_id, out: Path, base) -> int:
    refuse_rerun(base)
    _, _, attempts = shape(protocol, mode)
    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    started = (out / "started").exists()
    check = json.loads((out / "env.json").read_text(encoding="utf-8")) if (out / "env.json").exists() else None
    for attempt in range(1, attempts + 1):
        if record_path(out, env_id, job, attempt).exists():
            continue
        record = base_record(protocol, mode, execution_id, env_id, job, attempt, base)
        if started and check is not None:
            record.update(environmentCheck=check, status="environment-violation" if check["violations"] else "invalid",
                          reason="the harness stopped before this attempt")
        else:
            record.update(environmentCheck=check, status="infrastructure-failure", reason="a setup step failed before any test ran")
        write_json(record_path(out, env_id, job, attempt), record)
    return 0


# ---------------------------------------------------------------- decide and ledger


def cmd_decide(args, env=os.environ) -> int:
    refuse_rerun(env)
    return dp.main(["decide", "--records", args.records, "--execution-id", args.execution_id, "--run-id", env["GITHUB_RUN_ID"],
                    "--commit", env["GITHUB_SHA"], "--mode", args.mode, "--out", args.out])


def ledger_problems(ledger: dict, inventory: list[dict]) -> list[str]:
    """executions.ledgerCheck: every dispatched run is in the ledger with its measured minutes."""
    entries = {str(e.get("runId")): e for e in ledger.get("entries", [])}
    problems = []
    for run in (r for r in inventory if r["event"] == "workflow_dispatch"):
        entry = entries.get(str(run["id"]))
        if entry is None:
            problems.append(f"run {run['id']} ({run['title']}) is not in the ledger")
        elif run["status"] == "completed" and entry.get("runnerMinutes") != run["minutes"]:
            problems.append(f"run {run['id']}: ledger says {entry.get('runnerMinutes')} runner-minutes, measured {run['minutes']}")
    return problems


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("plan")
    p.add_argument("--mode", required=True)
    p.add_argument("--execution-id", required=True)
    p.add_argument("--main-validator", required=True)
    p.add_argument("--github-output", required=True)
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
            return run_job(protocol, dp.load(ROOT, dp.CASES), **kwargs) if args.command == "run-job" else fill_missing(protocol, **kwargs)
        if args.command == "ledger-check":
            problems = ledger_problems(json.loads(Path(args.ledger).read_text(encoding="utf-8")),
                                       fetch_inventory(os.environ["GITHUB_REPOSITORY"], os.environ["GH_TOKEN"]))
            print("\n".join(problems) or "ledger covers every dispatched run")
            return 1 if problems else 0
        return {"plan": cmd_plan, "env-check": cmd_env_check, "decide": cmd_decide}[args.command](args)
    except Refusal as error:
        print(f"REFUSED: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
