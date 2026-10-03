#!/usr/bin/env python3
"""0.24 G2 (#1421): the verifier determinism protocol harness.

The protocol is docs/plans/evidence/g2-1421/protocol.json and its case registry is cases.json.
#1135 (and #1424) execute it through .github/workflows/determinism-protocol.yml; G2 only registers.

Subcommands:
  validate      fail closed on registration defects (codes D001-D015)
  plan          validate, check the ledger budget, and emit the job matrix
  env-check     check and record the environment of one job (SDK, runtime, OS, CPU, Z3, tree)
  run-job       run every attempt of one job and write one record per attempt
  fill-missing  write a record for every attempt of a job that has none
  decide        classify every case over all attempt records and write the result record
  check-ledger  check #1135's execution ledger (limits, same-commit pooling, repairs, hidden runs)
  budget        runner-minutes of a run from its GitHub API jobs listing

cases.json is built once, at registration, by docs/plans/evidence/g2-1421/freeze_cases.py.

Nothing here retries, normalizes a compared value, or converts a missing value to agreement.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKET = "docs/plans/evidence/g2-1421"
PROTOCOL = f"{PACKET}/protocol.json"
CASES = f"{PACKET}/cases.json"
CONTRACT = "docs/plans/evidence/evidence-contract-1407/contract.json"
INVENTORY = "docs/plans/evidence/evidence-contract-1407/artifact-inventory.json"
Z3_CONSUMERS = "eng/z3-consumers.json"
ORACLE_REPORT = "bench/phase0-agent-native/verifier-runtime-differential.json"
HASHES = f"{PACKET}/sha256.json"
HARNESS = "scripts/determinism_protocol.py"
FROZEN_FILES = [PROTOCOL, CASES, f"{PACKET}/freeze_cases.py", f"{PACKET}/README.md", HARNESS,
                "scripts/test_determinism_protocol.py", ".github/workflows/determinism-protocol.yml"]

ATTEMPT_STATUSES = ["completed", "timeout", "crash", "infrastructure-failure", "environment-violation", "invalid"]
VALUE_STATUSES = {"completed", "timeout", "crash"}
CASE_CLASSES = ["AGREE-PASS", "AGREE-FAIL", "DISAGREE", "INCOMPLETE"]
VERDICT_ORDER = ["INVALID", "NON-DETERMINISTIC", "FAILING", "INCOMPLETE", "DETERMINISTIC"]
MIN_ATTEMPTS_PER_ENVIRONMENT = 30
ORACLE_PROFILES = {"verification-full", "oracle-isolated"}
POSITIONS = ["precondition", "postcondition", "obligation"]
POLARITIES = ["provable", "refutable"]
MAX_DEPTH = 3

def load(root: Path, rel: str):
    return json.loads((root / rel).read_text(encoding="utf-8"))

def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())

def sha256_lf(path: Path) -> str:
    return sha256_bytes(path.read_bytes().replace(b"\r\n", b"\n"))

def cell_digest(cell: dict) -> str:
    canonical = json.dumps(cell, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return sha256_bytes(canonical.encode("utf-8"))[:32]

def env_by_id(protocol) -> dict:
    return {e["id"]: e for e in protocol["environments"]}

def profile_by_id(protocol) -> dict:
    return {p["id"]: p for p in protocol["profiles"]}

def compiler_filter(cases) -> str:
    return "|".join(f"FullyQualifiedName~{c}." for c in cases["groups"]["compiler-verifier"]["classes"])

def selection(profile, cases) -> dict[str, int]:
    """Registered test names (with multiplicity) a profile must report."""
    group = cases["groups"][profile["group"]]["tests"]
    names = {t["name"]: t["multiplicity"] for t in group}
    if profile["select"] == "all":
        return names
    return {n: names.get(n, 0) for n in profile["select"]}

def derive_cells(report: dict) -> list[dict]:
    """The F-4 generator's case order: form, then position, then depth, then polarity."""
    cells, sequence = [], 0
    for form in report["forms"]:
        if not form["applicable"]:
            continue
        for position in POSITIONS:
            for depth in range(1, MAX_DEPTH + 1):
                for polarity in POLARITIES:
                    sequence += 1
                    cells.append({"id": f"case-{sequence:06d}", "formId": form["id"], "position": position,
                                  "nestingDepth": depth, "polarity": polarity})
    return cells

def case_universe(protocol, cases) -> set[str]:
    keys = set()
    profiles = profile_by_id(protocol)
    for env in protocol["environments"]:
        for pid in env["profiles"]:
            p = profiles.get(pid)
            if p is None:
                continue
            keys |= {f"test:{p['group']}:{n}" for n in selection(p, cases)}
            if p["oracle"]:
                keys |= {f"cell:{c['id']}" for c in cases["cells"]["ids"]}
                keys |= {f"artifact:{a['name']}" for a in cases["artifacts"]}
    return keys

def worst_case(protocol) -> tuple[int, int, int]:
    b = protocol["budget"]
    overhead = b["planJobTimeoutMinutes"] + b["decideJobTimeoutMinutes"]
    jobs = protocol["runPlan"]["jobsPerEnvironment"]
    execution = overhead + sum(jobs * e["jobTimeoutMinutes"] for e in protocol["environments"])
    control = protocol["control"]
    control_run = overhead + len(protocol["environments"]) * control["jobsPerEnvironment"] * control["jobTimeoutMinutes"]
    total = b["maxExecutions"] * execution + b["maxDispatchedControlRuns"] * control_run
    return execution, control_run, total

def validate(root: Path, protocol=None, cases=None, contract=None, texts=None) -> list[tuple[str, str]]:
    """texts maps a repository-relative path to replacement text (negative controls only)."""
    def text(rel):
        return texts[rel] if texts and rel in texts else (root / rel).read_text(encoding="utf-8")

    protocol = protocol if protocol is not None else load(root, PROTOCOL)
    cases = cases if cases is not None else load(root, CASES)
    contract = contract if contract is not None else load(root, CONTRACT)
    v: list[tuple[str, str]] = []

    def add(code, message):
        v.append((code, message))

    semver = re.compile(r"^\d+\.\d+\.\d+$")

    def vkey(s):
        return tuple(int(x) for x in s.split("."))

    # D001 lifecycle and protocol amendments.
    if protocol.get("status") != "FROZEN-AT-MERGE":
        add("D001", "status must be FROZEN-AT-MERGE")
    if protocol.get("freeze", {}).get("decisionBearingExecutionBeforeFreeze") is not False:
        add("D001", "freeze.decisionBearingExecutionBeforeFreeze must be explicitly false")
    version = protocol.get("protocolVersion", "")
    log = protocol.get("amendments", {}).get("amendmentLog")
    if not semver.match(version) or not isinstance(log, list):
        add("D001", "protocolVersion must be MAJOR.MINOR.PATCH and amendments.amendmentLog a list")
    else:
        previous = "1.0.0"
        for entry in log:
            fields_ok = (isinstance(entry.get("pr"), int) and entry.get("justification")
                         and re.match(r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", str(entry.get("timestampUtc", "")))
                         and isinstance(entry.get("afterDecisionBearingExecution"), bool))
            if not fields_ok or not semver.match(str(entry.get("version", ""))) or vkey(entry["version"]) <= vkey(previous):
                add("D001", f"amendment {entry.get('version')} lacks a field or does not increase the version")
            else:
                previous = entry["version"]
        if version != previous:
            add("D001", "protocolVersion must equal the latest amendment (1.0.0 with none)")

    # D002 contract linkage.
    known = {"1.0.0"} | {a.get("version") for a in contract.get("amendmentLog", [])}
    cv = protocol.get("contract", {}).get("contractVersion")
    if cv not in known or vkey(cv) > vkey(contract["contractVersion"]):
        add("D002", "contract.contractVersion is not a recorded contract version no newer than contract.json")
    child = next((c for c in contract.get("children", []) if c.get("issue") == 1421), None)
    if child is None or protocol["contract"].get("consumes") != child.get("consumes"):
        add("D002", "contract.consumes differs from contract.json children[#1421].consumes")
    if not re.match(r"^[0-9a-f]{40}$", protocol.get("registrationBase", {}).get("commit", "")):
        add("D002", "registrationBase.commit must be a full SHA")

    envs = protocol.get("environments", [])
    env_ids = [e.get("id") for e in envs]
    profiles = profile_by_id(protocol)

    # D003 determinism rows: every contract row registered on its platforms, mapped to real cases.
    universe = case_universe(protocol, cases)
    rows = {r["id"]: r for r in protocol.get("determinismRows", [])}
    for crow in contract.get("determinismRows", {}).get("rows", []):
        row = rows.get(crow["id"])
        if row is None:
            add("D003", f"contract determinism row {crow['id']} is not registered")
            continue
        if not set(crow["platforms"]) <= set(row.get("environments", [])):
            add("D003", f"row {crow['id']} does not register every platform the contract names")
        if crow.get("releaseBlocking") is not True:
            add("D003", f"contract row {crow['id']} is not release-blocking")
    for row in rows.values():
        if not row.get("cases") or not set(row.get("environments", [])) <= set(env_ids):
            add("D003", f"row {row['id']} has no cases or names an unregistered environment")
        for key in row.get("cases", []):
            if key != "cell:*" and key not in universe:
                add("D003", f"row {row['id']} maps to unregistered case {key}")
            for eid in row.get("environments", []):
                env = env_by_id(protocol).get(eid)
                if env and not any(_profile_covers(profiles.get(pid), key, cases) for pid in env["profiles"]):
                    add("D003", f"row {row['id']} case {key} is not run on {eid}")

    # D004 environments: every supported RID, pinned labels, known profiles.
    z3c = load(root, Z3_CONSUMERS)
    supported = {r["rid"] for r in z3c["supportedRids"]}
    if len(set(env_ids)) != len(env_ids) or {e.get("rid") for e in envs} != supported:
        add("D004", "environments must be unique and cover exactly the supported RIDs in eng/z3-consumers.json")
    for e in envs:
        required = ["id", "rid", "runner", "runnerOs", "runnerArch", "logicalProcessors", "profiles", "jobTimeoutMinutes"]
        if any(e.get(k) in (None, "", []) for k in required):
            add("D004", f"environment {e.get('id')} is missing a field")
            continue
        if "latest" in e["runner"]:
            add("D004", f"environment {e['id']} uses a floating runner label")
        if not ORACLE_PROFILES <= set(e["profiles"]) or not set(e["profiles"]) <= set(profiles):
            add("D004", f"environment {e['id']} must run both oracle profiles and only registered profiles")
    for pid in profiles:
        if not any(pid in e.get("profiles", []) for e in envs):
            add("D004", f"profile {pid} runs in no environment")

    # D005 toolchain.
    tc = protocol.get("toolchain", {})
    gj = json.loads((root / "global.json").read_text(encoding="utf-8"))["sdk"]
    sdk, runtime = str(tc.get("sdk", "")), str(tc.get("runtime", ""))
    if not semver.match(sdk) or not semver.match(runtime):
        add("D005", "toolchain.sdk and toolchain.runtime must be exact versions")
    elif (tc.get("globalJson") != {"version": gj["version"], "rollForward": gj["rollForward"]}
          or sdk.split(".")[:2] != gj["version"].split(".")[:2] or vkey(sdk) < vkey(gj["version"])
          or runtime.split(".")[:2] != sdk.split(".")[:2]):
        add("D005", "toolchain does not match global.json or its SDK/runtime band")

    # D006 Z3 pins, seed, and per-case timeout match the tree.
    z3 = protocol.get("z3", {})
    if z3.get("version") != z3c["z3Version"]:
        add("D006", "z3.version differs from eng/z3-consumers.json")
    for pin in z3.get("pins", []):
        path = root / pin["path"]
        if not path.exists() or sha256_file(path) != pin["sha256"]:
            add("D006", f"pin file {pin['path']} does not match its registered SHA-256")
    for key in ("randomSeed", "perCaseTimeoutMs"):
        item = z3.get(key, {})
        path = root / item.get("source", "missing")
        if not path.exists() or item.get("pattern", "\0") not in text(item["source"]) \
                or str(item.get("value")) not in item.get("pattern", ""):
            add("D006", f"z3.{key} does not match {item.get('source')}")

    # D007 run plan, agreement rate, retries.
    rp = protocol.get("runPlan", {})
    jobs, attempts = rp.get("jobsPerEnvironment", 0), rp.get("attemptsPerJob", 0)
    if not (isinstance(jobs, int) and jobs >= 2 and isinstance(attempts, int) and jobs * attempts >= MIN_ATTEMPTS_PER_ENVIRONMENT):
        add("D007", f"run plan needs >= 2 jobs and >= {MIN_ATTEMPTS_PER_ENVIRONMENT} attempts per environment")
    if rp.get("earlyStop") is not False:
        add("D007", "runPlan.earlyStop must be false")
    if protocol.get("agreement", {}).get("requiredAgreementRate") != 1.0:
        add("D007", "requiredAgreementRate must be 1.0")
    rt = protocol.get("retryPolicy", {})
    if rt.get("retriesPerAttempt") != 0 or rt.get("rerunJobs") is not False:
        add("D007", "retries must be 0 and job re-runs forbidden")
    if any(not isinstance(p.get("processTimeoutMinutes"), int) or p["processTimeoutMinutes"] <= 0 for p in profiles.values()):
        add("D007", "every profile needs a positive processTimeoutMinutes")

    # D008 budget: recomputed worst case within the accepted ceiling.
    b = protocol.get("budget", {})
    ceiling = next((c for c in contract["authorityCapacity"]["capacity"]["ceilings"] if c["id"] == b.get("ceilingId")), None)
    try:
        execution, control_run, total = worst_case(protocol)
    except (KeyError, TypeError):
        execution = control_run = total = None
    if (ceiling is None or ceiling.get("status") != "ACCEPTED" or b.get("ceilingRunnerMinutes") != ceiling.get("value")
            or b.get("ceilingId") != "determinism-compute"):
        add("D008", "budget must name the accepted determinism-compute ceiling and its value")
    elif (total is None or (b.get("worstCasePerExecution"), b.get("worstCasePerControlRun"), b.get("worstCaseTotal"))
          != (execution, control_run, total) or total > ceiling["value"]):
        add("D008", f"declared worst case differs from the recomputed {execution}/{control_run}/{total} or exceeds the ceiling")

    # D009 no normalization of compared values.
    ag = protocol.get("agreement", {})
    if ag.get("normalizationRules") != []:
        add("D009", "normalizationRules must be empty")
    for proj in ag.get("projections", []):
        dropped = " ".join(proj.get("dropped", [])).lower()
        if any(word in dropped for word in ("outcome", "testname", "crlf", "line ending", "whitespace", "newline")) \
                or set(proj.get("dropped", [])) & set(proj.get("kept", [])):
            add("D009", f"projection of {proj.get('source')} drops a verdict-bearing field or normalizes text")
        if proj.get("source") == "cells.json" and proj.get("dropped"):
            add("D009", "cells.json projection may not drop a field")

    # D010 the case registry.
    groups = cases.get("groups", {})
    for p in profiles.values():
        g = groups.get(p.get("group"))
        if g is None or g.get("project") != p.get("project"):
            add("D010", f"profile {p['id']} names a group or project not in cases.json")
            continue
        names = [t["name"] for t in g["tests"]]
        if len(names) != len(set(names)) or sum(t["multiplicity"] for t in g["tests"]) != g.get("count") \
                or any(t["multiplicity"] < 1 for t in g["tests"]):
            add("D010", f"group {p['group']} has duplicate names or a count that differs from its multiplicities")
        if p["select"] != "all" and (not p["select"] or not set(p["select"]) <= set(names)):
            add("D010", f"profile {p['id']} selects an unregistered test")
        if p["id"] == "compiler-verifier" and p.get("filter") != "FROM-CASES":
            add("D010", "compiler-verifier filter must be built from cases.json classes")
        if p["id"] == "compiler-verifier" and any(not n.startswith(tuple(c + "." for c in g.get("classes", []))) for n in names):
            add("D010", "a compiler-verifier test is outside its registered classes")
    derived = derive_cells(load(root, ORACLE_REPORT))
    if cases.get("cells", {}).get("ids") != derived or cases["cells"].get("count") != len(derived):
        add("D010", "registered cells differ from the cells derived from the committed oracle report")
    inv = next(a for a in load(root, INVENTORY)["artifacts"] if a["id"] == "verifier-runtime-differential")
    if sorted((a.get("name"), a.get("committedPath")) for a in cases.get("artifacts", [])) != \
            sorted((Path(p).name, p) for p in inv["paths"]):
        add("D010", "registered artifacts differ from the inventory's verifier-runtime-differential paths")

    # D011 timing-sensitive set: disjoint, and only by a recorded amendment.
    ts = protocol.get("cases", {}).get("timingSensitiveSet")
    amended = {a.get("version"): a for a in (log or [])}
    if not isinstance(ts, list):
        add("D011", "timingSensitiveSet must be a list")
    else:
        for entry in ts:
            a = amended.get(entry.get("amendment"))
            if (entry.get("case") not in universe or not entry.get("reason") or a is None
                    or not entry.get("contractAmendment") or entry.get("case") in rows):
                add("D011", f"timing-sensitive entry {entry.get('case')} lacks a registered case, reason, or amendment")

    # D012 frozen vocabularies.
    if sorted(protocol.get("attemptStatuses", {})) != sorted(ATTEMPT_STATUSES):
        add("D012", "attemptStatuses differ from the frozen set")
    if sorted(protocol.get("caseClasses", {})) != sorted(CASE_CLASSES):
        add("D012", "caseClasses differ from the frozen set")
    if protocol.get("executionVerdicts", {}).get("order") != VERDICT_ORDER:
        add("D012", "executionVerdicts.order differs from the frozen order")

    # D013 the workflow G3 runs: no masking, no retry, timeouts from the protocol.
    wf_path = root / protocol.get("workflow", {}).get("path", "missing")
    if not wf_path.exists():
        add("D013", "workflow file is missing")
    else:
        wf = text(protocol["workflow"]["path"])
        required = ["fail-fast: false", "core.autocrlf false", "DOTNET_INSTALL_DIR", "timeout-minutes: ${{ matrix.timeout }}",
                    f"timeout-minutes: {b.get('planJobTimeoutMinutes')}\n", f"timeout-minutes: {b.get('decideJobTimeoutMinutes')}\n",
                    "determinism_protocol.py\" plan", "determinism_protocol.py\" run-job", "determinism_protocol.py\" fill-missing",
                    "determinism_protocol.py\" decide", "./.github/actions/bootstrap-z3"]
        forbidden = ["continue-on-error", "|| true", "|| exit 0", "retry", "max-attempts", "-latest"]
        if any(r not in wf for r in required) or any(f in wf for f in forbidden):
            add("D013", "workflow lacks a required protocol element or contains masking, retry, or a floating label")

    # D014 gate sites still run the registered command; no runner configuration appeared.
    test_yml = text(".github/workflows/test.yml")
    iso = profiles.get("oracle-isolated", {})
    if f"--filter \"{iso.get('filter')}\"" not in test_yml or "project: tests/Calor.Verification.Tests/Calor.Verification.Tests.csproj" not in test_yml:
        add("D014", "test.yml no longer runs the registered oracle command or verification project")
    for p in profiles.values():
        project_dir = (root / p["project"]).parent
        if any(project_dir.glob("xunit.runner.json")) or any(project_dir.glob("*.runsettings")):
            add("D014", f"{p['project']} gained a runner configuration file")
    # D015 the packet, harness, and workflow are frozen by hash; any change is an amendment.
    hashes = json.loads(text(HASHES)).get("files", {}) if (root / HASHES).exists() else {}
    for rel in FROZEN_FILES:
        if rel not in hashes or not (root / rel).exists() or (sha256_lf(root / rel) != hashes[rel] and not (texts and rel in texts)):
            add("D015", f"sha256.json does not cover {rel} or its hash differs (re-hash only by amendment)")
    return v

def _profile_covers(profile, key: str, cases) -> bool:
    if profile is None:
        return False
    if key.startswith(("cell:", "artifact:")):
        return profile["oracle"]
    return key.startswith(f"test:{profile['group']}:") and key.split(":", 2)[2] in selection(profile, cases)

def git(*args) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False).stdout.strip()

def env_check(protocol, env: dict, expected_commit: str) -> dict:
    violations, observed = [], {}

    def run(*cmd):
        try:
            return subprocess.run(list(cmd), capture_output=True, text=True, check=False).stdout
        except OSError as error:
            return f"<{error}>"

    sdks = [line.split()[0] for line in run("dotnet", "--list-sdks").splitlines() if line.strip()]
    runtimes = [line.split()[1] for line in run("dotnet", "--list-runtimes").splitlines()
                if line.startswith("Microsoft.NETCore.App ")]
    observed.update(sdks=sdks, netcoreRuntimes=runtimes, dotnetVersion=run("dotnet", "--version").strip())
    if sdks != [protocol["toolchain"]["sdk"]] or observed["dotnetVersion"] != protocol["toolchain"]["sdk"]:
        violations.append(f"SDK {sdks} is not exactly {protocol['toolchain']['sdk']}")
    if runtimes != [protocol["toolchain"]["runtime"]]:
        violations.append(f"Microsoft.NETCore.App {runtimes} is not exactly {protocol['toolchain']['runtime']}")
    for key, want in (("RUNNER_OS", env["runnerOs"]), ("RUNNER_ARCH", env["runnerArch"])):
        observed[key] = os.environ.get(key)
        if observed[key] != want:
            violations.append(f"{key}={observed[key]} expected {want}")
    observed["logicalProcessors"] = os.cpu_count()
    if observed["logicalProcessors"] != env["logicalProcessors"]:
        violations.append(f"{observed['logicalProcessors']} logical processors, expected {env['logicalProcessors']}")
    for key in ("ImageOS", "ImageVersion", "RUNNER_NAME"):
        observed[key] = os.environ.get(key)
    z3c = load(ROOT, Z3_CONSUMERS)
    rid = next(r for r in z3c["supportedRids"] if r["rid"] == env["rid"])
    pins = {}
    for line in (ROOT / ".github/z3-binaries-4.15.7.sha256").read_text(encoding="utf-8").splitlines():
        parts = line.split()
        if len(parts) == 3 and not line.startswith("#"):
            pins[parts[1]] = parts[0]
    for asset, path in ((rid["asset"], ROOT / rid["path"]), ("Microsoft.Z3.dll", ROOT / "src/Calor.Compiler/z3/Microsoft.Z3.dll")):
        digest = sha256_file(path) if path.exists() else None
        observed[f"z3:{asset}"] = digest
        if digest != pins.get(asset):
            violations.append(f"Z3 asset {asset} does not match its pin")
    observed["calorHomeExisted"] = (Path.home() / ".calor").exists()
    if observed["calorHomeExisted"]:
        violations.append("~/.calor exists before the first attempt (user state; never deleted by the harness)")
    observed.update(commit=git("rev-parse", "HEAD"), autocrlf=git("config", "--get", "core.autocrlf"),
                    dirty=git("status", "--porcelain", "--untracked-files=no"))
    if observed["commit"] != expected_commit:
        violations.append(f"HEAD {observed['commit']} is not {expected_commit}")
    if observed["autocrlf"] != "false":
        violations.append("core.autocrlf is not false")
    if observed["dirty"]:
        violations.append("tracked files differ from the commit")
    return {"violations": violations, "observed": observed}

def run_with_timeout(cmd, env, timeout_seconds: float, log: Path) -> tuple[int, bool]:
    kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with open(log, "wb") as handle:
        process = subprocess.Popen(cmd, cwd=ROOT, env=env, stdout=handle, stderr=subprocess.STDOUT, **kwargs)
        try:
            return process.wait(timeout=timeout_seconds), False
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True, check=False)
            else:
                import signal
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            return process.returncode, True

def parse_trx(path: Path) -> dict[str, list[str]]:
    outcomes: dict[str, list[str]] = {}
    for result in ET.parse(path).getroot().findall(".//{*}UnitTestResult"):
        outcomes.setdefault(result.attrib["testName"], []).append(result.attrib.get("outcome", "Missing"))
    return outcomes

def profile_values(profile, cases, outcomes, record_dir: Path | None, fill: str | None) -> dict:
    """Turn one invocation's raw output into registered case values (no normalization)."""
    result = {"tests": {}, "cells": None, "artifacts": None, "unregistered": []}
    expected = selection(profile, cases) if profile["select"] != "observed" else \
        {n: len(o) for n, o in (outcomes or {}).items()}
    for name, multiplicity in expected.items():
        got = sorted((outcomes or {}).get(name, []))
        if len(got) > multiplicity:
            result["unregistered"].append(f"test:{name} (extra results)")
        got += [fill or "Missing"] * (multiplicity - len(got))
        result["tests"][name] = ",".join(got)
    result["unregistered"] += [f"test:{n}" for n in (outcomes or {}) if n not in expected]
    if profile["oracle"]:
        registered = {c["id"]: c for c in cases["cells"]["ids"]}
        cells = {cid: fill or "Missing" for cid in registered}
        cell_file = record_dir / "cells.json" if record_dir else None
        if fill is None and cell_file is not None and cell_file.exists():
            for cell in json.loads(cell_file.read_text(encoding="utf-8")):
                reg = registered.get(cell.get("id"))
                identity = {k: cell.get(k) for k in ("id", "formId", "position", "nestingDepth", "polarity")}
                if reg is None or identity != reg:
                    result["unregistered"].append(f"cell:{cell.get('id')}")
                    continue
                cells[cell["id"]] = f"{cell_digest(cell)}|{1 if cell.get('mismatch') is not False else 0}"
        result["cells"] = cells
        result["artifacts"] = {}
        for artifact in cases["artifacts"]:
            generated = record_dir / "generated" / artifact["name"] if record_dir else None
            result["artifacts"][artifact["name"]] = (fill or "Missing") if fill is not None or generated is None \
                or not generated.exists() else sha256_file(generated)
    return result

def run_profile(protocol, cases, profile, env, attempt_dir: Path, owns_calor_home: bool) -> dict:
    pdir = attempt_dir / profile["id"]
    trx_dir, record_dir = pdir / "trx", pdir / "record"
    trx_dir.mkdir(parents=True, exist_ok=True)
    # ~/.calor was verified absent at job start, so anything there now was created by an
    # earlier invocation of this job; it is removed so attempts do not share a cache. The
    # harness never removes a ~/.calor it did not verify absent (owns_calor_home).
    cache = Path.home() / ".calor"
    existed = cache.exists()
    if existed and owns_calor_home:
        shutil.rmtree(cache)
    child_env = {k: v for k, v in os.environ.items() if not k.startswith("CALOR_UPDATE_")}
    child_env.update(CALOR_Z3_EXPECTED_RID=env["rid"], CALOR_DETERMINISM_RECORD_DIR=str(record_dir))
    cmd = ["dotnet", "test", profile["project"], "-c", "Release", "--no-build",
           "--logger", f"trx;LogFileName={profile['id']}.trx", "--results-directory", str(trx_dir)]
    flt = compiler_filter(cases) if profile.get("filter") == "FROM-CASES" else profile.get("filter")
    if flt:
        cmd += ["--filter", flt]
    started = time.monotonic()
    code, timed_out = run_with_timeout(cmd, child_env, profile["processTimeoutMinutes"] * 60, pdir / "console.log")
    trx = trx_dir / f"{profile['id']}.trx"
    outcomes = parse_trx(trx) if trx.exists() and not timed_out else None
    fill = "Timeout" if timed_out else ("Crash" if outcomes is None else None)
    values = profile_values(profile, cases, outcomes, record_dir, fill)
    short = any(v.split(",").count("Missing") for v in values["tests"].values())
    status = "timeout" if timed_out else "crash" if outcomes is None or short else "completed"
    return {"profile": profile["id"], "status": status, "exitCode": code, "seconds": round(time.monotonic() - started, 1),
            "calorCacheExisted": existed, **values}

def base_record(protocol, mode, execution_id, env_id, job, attempt) -> dict:
    return {"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"],
            "protocolSha256": sha256_file(ROOT / PROTOCOL), "harnessSha256": sha256_file(ROOT / HARNESS), "mode": mode, "executionId": execution_id,
            "environment": env_id, "job": job, "attempt": attempt, "runId": os.environ.get("GITHUB_RUN_ID"),
            "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "commit": os.environ.get("GITHUB_SHA") or git("rev-parse", "HEAD"),
            "status": None, "environmentCheck": None, "profiles": []}

def write_record(out: Path, record: dict) -> None:
    out.mkdir(parents=True, exist_ok=True)
    path = out / f"attempt-{record['environment']}-j{record['job']}-a{record['attempt']:02d}.json"
    path.write_bytes((json.dumps(record, indent=1, sort_keys=True) + "\n").encode("utf-8"))

def plan_shape(protocol, mode) -> tuple[list[dict], int, int]:
    if mode == "control":
        c = protocol["control"]
        return [c["profile"]], c["jobsPerEnvironment"], c["attemptsPerJob"]
    return protocol["profiles"], protocol["runPlan"]["jobsPerEnvironment"], protocol["runPlan"]["attemptsPerJob"]

def cmd_run_job(args) -> int:
    protocol, cases = load(ROOT, PROTOCOL), load(ROOT, CASES)
    env = env_by_id(protocol)[args.env]
    profiles, _, attempts = plan_shape(protocol, args.mode)
    if args.mode == "execution":
        profiles = [p for p in profiles if p["id"] in env["profiles"]]
    out = Path(args.out)
    check_path = out / "env.json"
    check = json.loads(check_path.read_text(encoding="utf-8")) if check_path.exists() else {"violations": ["env-check missing"]}
    (out / "started").write_text("1\n", encoding="utf-8")
    tree_ok = True
    for attempt in range(1, attempts + 1):
        record = base_record(protocol, args.mode, args.execution_id, args.env, args.job, attempt)
        record["environmentCheck"] = check
        if check["violations"]:
            record["status"] = "environment-violation"
        elif not tree_ok:
            record["status"], record["reason"] = "invalid", "an earlier invocation modified tracked files"
        else:
            for profile in profiles:
                result = run_profile(protocol, cases, profile, env, out / "raw" / f"a{attempt:02d}",
                                     check.get("observed", {}).get("calorHomeExisted") is False)
                record["profiles"].append(result)
                if git("status", "--porcelain", "--untracked-files=no"):
                    tree_ok = False
                    result["status"] = "invalid"
                    break
            statuses = {p["status"] for p in record["profiles"]}
            record["status"] = next(s for s in ("invalid", "timeout", "crash", "completed") if s in statuses)
        write_record(out, record)
    return 0

def cmd_fill_missing(args) -> int:
    protocol = load(ROOT, PROTOCOL)
    _, _, attempts = plan_shape(protocol, args.mode)
    out = Path(args.out)
    started = (out / "started").exists()
    for attempt in range(1, attempts + 1):
        if not (out / f"attempt-{args.env}-j{args.job}-a{attempt:02d}.json").exists():
            record = base_record(protocol, args.mode, args.execution_id, args.env, args.job, attempt)
            record["status"] = "invalid" if started else "infrastructure-failure"
            record["reason"] = "harness stopped before this attempt" if started else "a setup step failed before any test ran"
            write_record(out, record)
    return 0

def cmd_env_check(args) -> int:
    protocol = load(ROOT, PROTOCOL)
    result = env_check(protocol, env_by_id(protocol)[args.env], args.commit)
    Path(args.out).mkdir(parents=True, exist_ok=True)
    (Path(args.out) / "env.json").write_text(json.dumps(result, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=1))
    return 0

def decide(root: Path, records: list[dict], execution_ids: list[str], mode: str, protocol=None, cases=None) -> dict:
    protocol = protocol if protocol is not None else load(root, PROTOCOL)
    cases = cases if cases is not None else load(root, CASES)
    envs = env_by_id(protocol)
    profiles, jobs, attempts = plan_shape(protocol, mode)
    protocol_sha, harness_sha = sha256_file(root / PROTOCOL), sha256_file(root / HARNESS)
    committed = {a["name"]: sha256_file(root / a["committedPath"]) for a in cases["artifacts"]}
    invalid: list[str] = []
    seen: dict[tuple, dict] = {}
    commits = {r.get("commit") for r in records}
    if len(commits) > 1:
        invalid.append(f"records come from more than one commit: {sorted(map(str, commits))}")
    required = protocol["recordFormats"]["attempt"]
    for r in records:
        key = (r.get("executionId"), r.get("environment"), r.get("job"), r.get("attempt"))
        problem = None
        if any(k not in r for k in required):
            problem = "missing a required field"
        elif r["protocolSha256"] != protocol_sha or r.get("harnessSha256") != harness_sha \
                or r["protocolVersion"] != protocol["protocolVersion"] or r["mode"] != mode:
            problem = "made under other protocol bytes or another mode"
        elif r["executionId"] not in execution_ids or r["environment"] not in envs \
                or not (1 <= int(r["job"]) <= jobs) or not (1 <= int(r["attempt"]) <= attempts):
            problem = "from an unregistered execution, environment, job, or attempt"
        elif str(r["runAttempt"]) != "1":
            problem = "from a re-run job"
        elif r["status"] not in ATTEMPT_STATUSES:
            problem = f"unknown status {r['status']}"
        elif key in seen:
            problem = "duplicated"
        if problem:
            invalid.append(f"record {key}: {problem}")
            continue
        seen[key] = r
        for p in r["profiles"]:
            if p.get("unregistered"):
                invalid.append(f"record {key} profile {p.get('profile')}: unregistered {p['unregistered'][:5]}")

    # Expected contributions and the values present.
    expected: dict[str, int] = {}
    values: dict[str, list[tuple[str, str]]] = {}
    attempt_status: dict[str, int] = {s: 0 for s in ATTEMPT_STATUSES + ["missing"]}
    control_names: dict[str, int] = {}
    for eid in execution_ids:
        for env_id, env in envs.items():
            env_profiles = [p for p in profiles if mode == "control" or p["id"] in env["profiles"]]
            for job in range(1, jobs + 1):
                for attempt in range(1, attempts + 1):
                    r = seen.get((eid, env_id, job, attempt))
                    attempt_status[r["status"] if r else "missing"] += 1
                    for p in env_profiles:
                        keys = _profile_keys(p, cases)
                        for k in keys:
                            expected[k] = expected.get(k, 0) + 1
                        if r is None or r["status"] not in VALUE_STATUSES:
                            continue
                        res = next((x for x in r["profiles"] if x.get("profile") == p["id"]), None)
                        if res is None or res.get("status") == "invalid":
                            continue
                        if p["select"] == "observed":
                            for name, value in res["tests"].items():
                                control_names[name] = control_names.get(name, 0) + 1
                                values.setdefault(f"test:control:{name}", []).append((env_id, value))
                            continue
                        for k in keys:
                            kind, _, rest = k.partition(":")
                            source = res.get({"test": "tests", "cell": "cells", "artifact": "artifacts"}[kind]) or {}
                            name = rest.split(":", 1)[1] if kind == "test" else rest
                            if name in source:
                                values.setdefault(k, []).append((env_id, source[name]))
    if mode == "control":
        per = sum(jobs * attempts for _ in envs) * len(execution_ids)
        expected = {f"test:control:{n}": per for n in control_names}

    case_rows, counts = [], {c: 0 for c in CASE_CLASSES}
    for key in sorted(expected):
        present = values.get(key, [])
        distinct = sorted({v for _, v in present})
        if len(distinct) > 1:
            klass = "DISAGREE"
        elif len(present) < expected[key] or not present:
            klass = "INCOMPLETE"
        else:
            klass = "AGREE-PASS" if _is_pass(key, distinct[0], committed) else "AGREE-FAIL"
        counts[klass] += 1
        row = {"case": key, "class": klass, "contributions": len(present), "expected": expected[key]}
        if klass != "AGREE-PASS":
            by_env: dict[str, dict[str, int]] = {}
            for env_id, value in present:
                by_env.setdefault(env_id, {}).setdefault(value, 0)
                by_env[env_id][value] += 1
            row["valuesByEnvironment"] = by_env
            row["platformDependent"] = klass == "DISAGREE" and all(len(x) == 1 for x in by_env.values())
        case_rows.append(row)

    if invalid:
        verdict = "INVALID"
    elif counts["DISAGREE"]:
        verdict = "NON-DETERMINISTIC"
    elif counts["AGREE-FAIL"]:
        verdict = "FAILING"
    elif counts["INCOMPLETE"] or not case_rows:
        verdict = "INCOMPLETE"
    else:
        verdict = "DETERMINISTIC"
    classes = {r["case"]: r["class"] for r in case_rows}
    rows = []
    if mode == "execution":
        for row in protocol["determinismRows"]:
            mapped = [k for pattern in row["cases"] for k in ([c for c in classes if c.startswith("cell:")]
                                                                 if pattern == "cell:*" else [pattern])]
            ran = all(any(v[0] == e for k in mapped for v in values.get(k, [])) for e in row["environments"])
            resolved = not invalid and ran and all(classes.get(k) == "AGREE-PASS" for k in mapped)
            rows.append({"id": row["id"], "status": "RESOLVED" if resolved else "OPEN",
                         "classes": {c: sum(classes.get(k) == c for k in mapped) for c in CASE_CLASSES}})
    return {"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"], "protocolSha256": protocol_sha,
            "mode": mode, "executionIds": execution_ids, "commit": sorted(map(str, commits))[0] if len(commits) == 1 else None,
            "verdict": ("CONTROL-" + verdict) if mode == "control" else verdict,
            "complete": not invalid and attempt_status["missing"] == 0 and not counts["INCOMPLETE"],
            "attempts": attempt_status, "invalidReasons": invalid, "classCounts": counts, "cases": case_rows,
            "determinismRows": rows, "environments": sorted(envs),
            "limitations": protocol["limitations"] + (["CONTROL RUN: not a registered case; never decision-bearing."] if mode == "control" else [])}

def _profile_keys(profile, cases) -> list[str]:
    if profile["select"] == "observed":
        return []
    keys = [f"test:{profile['group']}:{n}" for n in selection(profile, cases)]
    if profile["oracle"]:
        keys += [f"cell:{c['id']}" for c in cases["cells"]["ids"]] + [f"artifact:{a['name']}" for a in cases["artifacts"]]
    return keys

def _is_pass(key: str, value: str, committed: dict[str, str]) -> bool:
    kind, _, rest = key.partition(":")
    if kind == "test":
        return all(part == "Passed" for part in value.split(","))
    if kind == "cell":
        return value.endswith("|0") and len(value) == 34
    return value == committed.get(rest)

def cmd_decide(args) -> int:
    records = [json.loads(p.read_text(encoding="utf-8")) for d in args.records for p in sorted(Path(d).rglob("attempt-*.json"))]
    result = decide(ROOT, records, args.execution_id, args.mode)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes((json.dumps(result, indent=1, sort_keys=True) + "\n").encode("utf-8"))
    print(json.dumps({k: result[k] for k in ("verdict", "complete", "attempts", "classCounts", "determinismRows", "invalidReasons")}, indent=1))
    return 0 if result["verdict"] in ("DETERMINISTIC", "CONTROL-DETERMINISTIC") else 1

def check_ledger(protocol, ledger: dict, runs: list[dict] | None = None, changed_paths=None) -> list[tuple[str, str]]:
    v: list[tuple[str, str]] = []
    b = protocol["budget"]
    entries = ledger.get("entries", [])
    charged = [e for e in entries if e.get("charge") == "determinism-compute"]
    for e in entries:
        if any(e.get(k) in (None, "") for k in ("executionId", "mode", "charge", "runId", "commit", "verdict", "runnerMinutes")):
            v.append(("L001", f"ledger entry {e.get('executionId')} is missing a field"))
    if sum(e.get("runnerMinutes") or 0 for e in charged) > b["ceilingRunnerMinutes"]:
        v.append(("L002", "recorded runner-minutes exceed the determinism-compute ceiling"))
    if sum(e.get("mode") == "execution" for e in charged) > b["maxExecutions"] or \
            sum(e.get("mode") == "control" for e in charged) > b["maxDispatchedControlRuns"]:
        v.append(("L003", "more executions or dispatched control runs than registered"))
    history: dict[str, str] = {}
    previous = None
    for e in [x for x in entries if x.get("mode") == "execution"]:
        prior = history.get(e.get("commit"))
        if prior in ("NON-DETERMINISTIC", "FAILING", "INVALID"):
            v.append(("L004", f"{e['executionId']} re-executes commit {e['commit']} after {prior}"))
        if prior == "INCOMPLETE" and not e.get("pooledWith"):
            v.append(("L004", f"{e['executionId']} re-executes an INCOMPLETE commit without pooling"))
        if previous and previous["verdict"] in ("NON-DETERMINISTIC", "FAILING") and e["commit"] != previous["commit"]:
            repair = e.get("repair") or {}
            paths = (changed_paths or {}).get((previous["commit"], e["commit"]), repair.get("changedPaths", []))
            if repair.get("from") != previous["commit"] or not repair.get("addresses") \
                    or not any(not p.startswith("docs/") for p in paths):
                v.append(("L005", f"{e['executionId']} follows {previous['verdict']} without a recorded code repair"))
        history[e.get("commit")] = e.get("verdict")
        previous = e
    if runs is not None:
        recorded = {str(e.get("runId")) for e in entries}
        for run in runs:
            title = run.get("display_title") or run.get("name") or ""
            if run.get("event") == "workflow_dispatch" and title.startswith(("determinism execution", "determinism control")) \
                    and str(run.get("id")) not in recorded:
                v.append(("L006", f"run {run.get('id')} ({title}) is not in the ledger"))
    return v

def runner_minutes(jobs_listing: dict) -> int:
    total = 0
    for job in jobs_listing.get("jobs", []):
        if job.get("started_at") and job.get("completed_at"):
            start = datetime.fromisoformat(job["started_at"].replace("Z", "+00:00"))
            end = datetime.fromisoformat(job["completed_at"].replace("Z", "+00:00"))
            total += math.ceil(max(0.0, (end - start).total_seconds()) / 60)
    return total

def cmd_plan(args) -> int:
    violations = validate(ROOT)
    if violations:
        for code, message in violations:
            print(f"{code}: {message}")
        return 1
    protocol = load(ROOT, PROTOCOL)
    mode = "control" if args.event != "workflow_dispatch" else args.mode
    if mode not in ("control", "execution") or not re.match(r"^[A-Za-z0-9-]{1,40}$", args.execution_id):
        print("mode must be control or execution and the execution id [A-Za-z0-9-]{1,40}")
        return 1
    if args.event == "workflow_dispatch" and args.charge == "determinism-compute":
        ledger_path = ROOT / protocol["executions"]["ledger"]
        ledger = json.loads(ledger_path.read_text(encoding="utf-8")) if ledger_path.exists() else {"entries": []}
        problems = check_ledger(protocol, ledger)
        execution, control_run, _ = worst_case(protocol)
        charged = [e for e in ledger["entries"] if e.get("charge") == "determinism-compute"]
        spent = sum(e.get("runnerMinutes") or 0 for e in charged)
        limit = protocol["budget"]["maxExecutions"] if mode == "execution" else protocol["budget"]["maxDispatchedControlRuns"]
        if spent + (execution if mode == "execution" else control_run) > protocol["budget"]["ceilingRunnerMinutes"]:
            problems.append(("L002", f"{spent} recorded + worst case would exceed the ceiling"))
        if sum(e.get("mode") == mode for e in charged) >= limit:
            problems.append(("L003", f"the {mode} limit of {limit} is reached"))
        if any(e.get("executionId") == args.execution_id for e in ledger["entries"]):
            problems.append(("L001", f"execution id {args.execution_id} is already in the ledger"))
        if problems:
            for code, message in problems:
                print(f"{code}: {message}")
            return 1
    profiles, jobs, _ = plan_shape(protocol, mode)
    timeout = protocol["control"]["jobTimeoutMinutes"] if mode == "control" else None
    include = []
    for env in protocol["environments"]:
        projects = sorted({p["project"] for p in profiles if mode == "control" or p["id"] in env["profiles"]})
        for job in range(1, jobs + 1):
            include.append({"env": env["id"], "job": job, "runner": env["runner"], "rid": env["rid"],
                            "timeout": timeout or env["jobTimeoutMinutes"], "sdk": protocol["toolchain"]["sdk"],
                            "projects": " ".join(projects)})
    lines = [f"matrix={json.dumps({'include': include}, separators=(',', ':'))}", f"mode={mode}", f"execution={args.execution_id}"]
    print("\n".join(lines))
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("validate")
    p = sub.add_parser("plan")
    p.add_argument("--mode", default="control")
    p.add_argument("--execution-id", required=True)
    p.add_argument("--event", default="workflow_dispatch")
    p.add_argument("--charge", default="determinism-compute", choices=["determinism-compute", "regeneration-compute"])
    p.add_argument("--github-output")
    for name in ("env-check", "run-job", "fill-missing"):
        p = sub.add_parser(name)
        p.add_argument("--env", required=True)
        p.add_argument("--out", required=True)
        if name == "env-check":
            p.add_argument("--commit", required=True)
        else:
            p.add_argument("--job", type=int, required=True)
            p.add_argument("--mode", required=True)
            p.add_argument("--execution-id", required=True)
    p = sub.add_parser("decide")
    p.add_argument("--records", action="append", required=True)
    p.add_argument("--execution-id", action="append", required=True)
    p.add_argument("--mode", required=True)
    p.add_argument("--out", required=True)
    p = sub.add_parser("check-ledger")
    p.add_argument("--ledger", required=True)
    p.add_argument("--runs")
    p = sub.add_parser("budget")
    p.add_argument("--jobs", required=True)
    args = parser.parse_args(argv)

    if args.command == "validate":
        violations = validate(ROOT)
        for code, message in violations:
            print(f"{code}: {message}")
        print("protocol valid" if not violations else f"{len(violations)} violation(s)")
        return 1 if violations else 0
    if args.command == "check-ledger":
        runs = json.loads(Path(args.runs).read_text(encoding="utf-8")).get("workflow_runs") if args.runs else None
        violations = check_ledger(load(ROOT, PROTOCOL), json.loads(Path(args.ledger).read_text(encoding="utf-8")), runs)
        for code, message in violations:
            print(f"{code}: {message}")
        return 1 if violations else 0
    if args.command == "budget":
        print(runner_minutes(json.loads(Path(args.jobs).read_text(encoding="utf-8"))))
        return 0
    return {"plan": cmd_plan, "env-check": cmd_env_check, "run-job": cmd_run_job, "fill-missing": cmd_fill_missing,
            "decide": cmd_decide}[args.command](args)

if __name__ == "__main__":
    sys.exit(main())
