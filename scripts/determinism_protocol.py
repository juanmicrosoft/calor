#!/usr/bin/env python3
"""0.24 G2 (#1421): the verifier determinism protocol — registration validator and decider.

The protocol is docs/plans/evidence/g2-1421/protocol.json; its case registry is cases.json.
This module holds what the registration freezes: the validator (codes D001-D016), the value
rules that turn one test invocation into registered case values, and the agreement decider.
The execution machinery (workflow, environment check, attempt runner, budget and ledger
guards) is the second G2 PR; #1135 executes after both merge.

  validate [--baseline-ref REF]   fail closed on registration defects; with a baseline,
                                  also on unrecorded or weakening changes to the packet
  decide --records DIR --execution-id ID --run-id ID --commit SHA --mode M --out FILE

Nothing here retries, normalizes a compared value, or converts a missing value to agreement.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKET = "docs/plans/evidence/g2-1421"
PROTOCOL, CASES, HASHES, README = (f"{PACKET}/{n}" for n in ("protocol.json", "cases.json", "sha256.json", "README.md"))
CONTRACT = "docs/plans/evidence/evidence-contract-1407/contract.json"
INVENTORY = "docs/plans/evidence/evidence-contract-1407/artifact-inventory.json"
Z3_CONSUMERS = "eng/z3-consumers.json"
ORACLE_REPORT = "bench/phase0-agent-native/verifier-runtime-differential.json"
HARNESS = "scripts/determinism_protocol.py"
FROZEN_FILES = [PROTOCOL, CASES, README, HARNESS, "scripts/test_determinism_protocol.py"]
ATTEMPT_STATUSES = ["completed", "timeout", "crash", "infrastructure-failure", "environment-violation", "invalid"]
VALUE_STATUSES = {"completed", "timeout", "crash"}
CASE_CLASSES = ["AGREE-PASS", "AGREE-FAIL", "DISAGREE", "INCOMPLETE"]
VERDICT_ORDER = ["INVALID", "NON-DETERMINISTIC", "FAILING", "INCOMPLETE", "DETERMINISTIC"]
MIN_ATTEMPTS_PER_ENVIRONMENT = 30
ORACLE_PROFILES = {"verification-full", "oracle-isolated"}
POSITIONS, POLARITIES, MAX_DEPTH = ["precondition", "postcondition", "obligation"], ["provable", "refutable"], 3
PROFILE_KEYS = {"profile", "status", "exitCode", "seconds", "calorCacheExisted", "invocation", "tests", "cells",
                "artifacts", "unregistered"}
FILL_VALUES = {"Missing", "Timeout", "Crash"}
CELL_VALUE = re.compile(r"^[0-9a-f]{32}\|[01]$")
SHA = re.compile(r"^[0-9a-f]{64}$")


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


def selection(profile, cases) -> dict[str, int]:
    """Registered test names (with multiplicity) a profile must report."""
    names = {t["name"]: t["multiplicity"] for t in cases["groups"][profile["group"]]["tests"]}
    return names if profile["select"] == "all" else {n: names.get(n, 0) for n in profile["select"]}


def profile_artifacts(profile, cases) -> list[dict]:
    return [a for a in cases["artifacts"] if profile["id"] in a["profiles"]]


def derive_cells(report: dict) -> list[dict]:
    """The F-4 generator's case order: form, then position, then depth, then polarity."""
    cells = []
    for form in (f for f in report["forms"] if f["applicable"]):
        for position in POSITIONS:
            for depth in range(1, MAX_DEPTH + 1):
                for polarity in POLARITIES:
                    cells.append({"id": f"case-{len(cells) + 1:06d}", "formId": form["id"], "position": position,
                                  "nestingDepth": depth, "polarity": polarity})
    return cells


def profile_keys(profile, cases) -> list[str]:
    """Every case key one invocation of a profile contributes to."""
    keys = [f"invocation:{profile['id']}"]
    if profile["select"] == "observed":
        return keys
    keys += [f"test:{profile['group']}:{n}" for n in selection(profile, cases)]
    if profile["oracle"]:
        keys += [f"cell:{c['id']}" for c in cases["cells"]["ids"]]
    return keys + [f"artifact:{a['name']}" for a in profile_artifacts(profile, cases)]


def case_universe(protocol, cases) -> set[str]:
    profiles = profile_by_id(protocol)
    return {k for e in protocol["environments"] for pid in e["profiles"] if pid in profiles
            for k in profile_keys(profiles[pid], cases)}


def worst_case(protocol) -> tuple[int, int, int]:
    b, control = protocol["budget"], protocol["control"]
    overhead = b["planJobTimeoutMinutes"] + b["decideJobTimeoutMinutes"]
    execution = overhead + sum(protocol["runPlan"]["jobsPerEnvironment"] * e["jobTimeoutMinutes"] for e in protocol["environments"])
    control_run = overhead + len(protocol["environments"]) * control["jobsPerEnvironment"] * control["jobTimeoutMinutes"]
    return execution, control_run, b["maxExecutions"] * execution + b["maxDispatchedControlRuns"] * control_run


def vkey(version: str) -> tuple:
    return tuple(int(x) for x in version.split("."))


def validate(root: Path, protocol=None, cases=None, contract=None, texts=None, baseline=None) -> list[tuple[str, str]]:
    """texts maps a repository-relative path to replacement text (negative controls only).
    baseline is (protocol, cases, hashes) as registered on protected main, when one exists."""
    def text(rel):
        return texts[rel] if texts and rel in texts else (root / rel).read_text(encoding="utf-8")

    protocol = protocol if protocol is not None else load(root, PROTOCOL)
    cases = cases if cases is not None else load(root, CASES)
    contract = contract if contract is not None else load(root, CONTRACT)
    v: list[tuple[str, str]] = []

    def add(code, message):
        v.append((code, message))

    semver = re.compile(r"^\d+\.\d+\.\d+$")
    # D001 lifecycle and protocol amendments.
    if protocol.get("status") != "FROZEN-AT-MERGE" or protocol.get("freeze", {}).get("decisionBearingExecutionBeforeFreeze") is not False:
        add("D001", "status must be FROZEN-AT-MERGE and decisionBearingExecutionBeforeFreeze explicitly false")
    version, log = protocol.get("protocolVersion", ""), protocol.get("amendments", {}).get("amendmentLog")
    if not semver.match(version) or not isinstance(log, list):
        add("D001", "protocolVersion must be MAJOR.MINOR.PATCH and amendments.amendmentLog a list")
        log = []
    previous = "1.0.0"
    for entry in log:
        ok = (isinstance(entry.get("pr"), int) and entry.get("justification") and isinstance(entry.get("afterDecisionBearingExecution"), bool)
              and re.match(r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", str(entry.get("timestampUtc", "")))
              and semver.match(str(entry.get("version", ""))) and vkey(entry["version"]) > vkey(previous))
        if not ok:
            add("D001", f"amendment {entry.get('version')} lacks a field or does not increase the version")
        else:
            previous = entry["version"]
    if version != previous:
        add("D001", "protocolVersion must equal the latest amendment (1.0.0 with none)")

    # D002 contract linkage.
    known = {"1.0.0"} | {a.get("version") for a in contract.get("amendmentLog", [])}
    cv = protocol.get("contract", {}).get("contractVersion")
    child = next((c for c in contract.get("children", []) if c.get("issue") == 1421), {})
    if cv not in known or vkey(cv) > vkey(contract["contractVersion"]) or protocol["contract"].get("consumes") != child.get("consumes") \
            or not re.match(r"^[0-9a-f]{40}$", protocol.get("registrationBase", {}).get("commit", "")):
        add("D002", "contract version, consumed sections, or registration base differ from the contract")

    envs, profiles = protocol.get("environments", []), profile_by_id(protocol)
    env_ids, by_env = [e.get("id") for e in envs], env_by_id(protocol)
    universe = case_universe(protocol, cases)
    # D003 determinism rows: every contract row registered on its platforms with its own test.
    rows = {r["id"]: r for r in protocol.get("determinismRows", [])}
    for crow in contract.get("determinismRows", {}).get("rows", []):
        row = rows.get(crow["id"], {})
        if not set(crow["platforms"]) <= set(row.get("environments", [])) or f"test:verification:{crow['test']}" not in row.get("cases", []) \
                or crow.get("releaseBlocking") is not True:
            add("D003", f"contract row {crow['id']} is not registered with its test on every platform it names")
    for row in rows.values():
        if not row.get("cases") or not set(row.get("environments", [])) <= set(env_ids):
            add("D003", f"row {row['id']} has no cases or names an unregistered environment")
        for key in row.get("cases", []):
            if key != "cell:*" and key not in universe:
                add("D003", f"row {row['id']} maps to unregistered case {key}")
            for eid in row.get("environments", []):
                if eid in by_env and not any((key == "cell:*" and profiles[p]["oracle"]) or key in profile_keys(profiles[p], cases)
                                             for p in by_env[eid]["profiles"] if p in profiles):
                    add("D003", f"row {row['id']} case {key} is not run on {eid}")

    # D004 environments: every supported RID, pinned labels, resources, both oracle profiles.
    z3c = load(root, Z3_CONSUMERS)
    if len(set(env_ids)) != len(env_ids) or {e.get("rid") for e in envs} != {r["rid"] for r in z3c["supportedRids"]}:
        add("D004", "environments must be unique and cover exactly the supported RIDs in eng/z3-consumers.json")
    for e in envs:
        required = ["id", "rid", "runner", "runnerOs", "runnerArch", "logicalProcessors", "memoryGiB", "profiles", "jobTimeoutMinutes"]
        if any(e.get(k) in (None, "", []) for k in required) or "latest" in e["runner"] \
                or not ORACLE_PROFILES <= set(e["profiles"]) or not set(e["profiles"]) <= set(profiles):
            add("D004", f"environment {e.get('id')} lacks a field, floats its runner label, or misses an oracle profile")
    if any(not any(pid in e.get("profiles", []) for e in envs) for pid in profiles):
        add("D004", "a profile runs in no environment")

    # D005 toolchain.
    tc, gj = protocol.get("toolchain", {}), json.loads(text("global.json"))["sdk"]
    sdk, runtime = str(tc.get("sdk", "")), str(tc.get("runtime", ""))
    if not semver.match(sdk) or not semver.match(runtime) or tc.get("globalJson") != {"version": gj["version"], "rollForward": gj["rollForward"]} \
            or sdk.split(".")[:2] != gj["version"].split(".")[:2] or vkey(sdk) < vkey(gj["version"]) or runtime.split(".")[:2] != sdk.split(".")[:2]:
        add("D005", "toolchain must pin exact SDK and runtime versions within global.json's band")

    # D006 Z3 pins, seed, and per-case timeout match the tree.
    z3 = protocol.get("z3", {})
    if z3.get("version") != z3c["z3Version"] or any(not (root / p["path"]).exists() or sha256_file(root / p["path"]) != p["sha256"]
                                                     for p in z3.get("pins", [])):
        add("D006", "Z3 version or a pin file differs from its registered value")
    for key in ("randomSeed", "perCaseTimeoutMs"):
        item = z3.get(key, {})
        if item.get("pattern", "\0") not in text(item.get("source", HARNESS)) or str(item.get("value")) not in item.get("pattern", ""):
            add("D006", f"z3.{key} does not match {item.get('source')}")

    # D007 run plan, agreement rate, retries, timeouts.
    rp, rt = protocol.get("runPlan", {}), protocol.get("retryPolicy", {})
    jobs, attempts = rp.get("jobsPerEnvironment", 0), rp.get("attemptsPerJob", 0)
    if not (isinstance(jobs, int) and isinstance(attempts, int) and jobs >= 2 and jobs * attempts >= MIN_ATTEMPTS_PER_ENVIRONMENT) \
            or rp.get("earlyStop") is not False or protocol.get("agreement", {}).get("requiredAgreementRate") != 1.0 \
            or rt.get("retriesPerAttempt") != 0 or rt.get("rerunJobs") is not False or rt.get("reexecuteCommit") is not False \
            or any(not isinstance(p.get("processTimeoutMinutes"), int) or p["processTimeoutMinutes"] <= 0 for p in profiles.values()):
        add("D007", f"run plan needs >= 2 jobs and >= {MIN_ATTEMPTS_PER_ENVIRONMENT} attempts per environment, rate 1.0, "
                    "no early stop, no retry or re-execution, and positive process timeouts")

    # D008 budget: recomputed worst case within the accepted ceiling.
    b = protocol.get("budget", {})
    ceiling = next((c for c in contract["authorityCapacity"]["capacity"]["ceilings"] if c["id"] == "determinism-compute"), {})
    try:
        computed = worst_case(protocol)
    except (KeyError, TypeError):
        computed = None
    if ceiling.get("status") != "ACCEPTED" or b.get("ceilingId") != "determinism-compute" or b.get("ceilingRunnerMinutes") != ceiling.get("value") \
            or computed is None or (b.get("worstCasePerExecution"), b.get("worstCasePerControlRun"), b.get("worstCaseTotal")) != computed \
            or computed[2] > ceiling["value"]:
        add("D008", f"budget must match the accepted determinism-compute ceiling and the recomputed worst case {computed}")

    # D009 no normalization of compared values.
    ag = protocol.get("agreement", {})
    if ag.get("normalizationRules") != []:
        add("D009", "normalizationRules must be empty")
    for proj in ag.get("projections", []):
        dropped = " ".join(proj.get("dropped", [])).lower()
        if any(w in dropped for w in ("outcome", "testname", "crlf", "line ending", "whitespace", "newline")) \
                or set(proj.get("dropped", [])) & set(proj.get("kept", [])) or (proj.get("source") == "cells.json" and proj.get("dropped")):
            add("D009", f"projection of {proj.get('source')} drops a verdict-bearing field or normalizes text")

    # D010 the case registry.
    for p in profiles.values():
        g = cases.get("groups", {}).get(p.get("group"), {})
        names = [t["name"] for t in g.get("tests", [])]
        classes = tuple(c + "." for c in g.get("classes", [])) or ("",)
        if g.get("project") != p.get("project") or len(names) != len(set(names)) or not names \
                or sum(t["multiplicity"] for t in g["tests"]) != g.get("count") or any(t["multiplicity"] < 1 for t in g["tests"]) \
                or (p["select"] != "all" and (not p["select"] or not set(p["select"]) <= set(names))) \
                or any(not n.startswith(classes) for n in names) or (p.get("filter") == "FROM-CASES") != ("classes" in g):
            add("D010", f"profile {p['id']} or its group does not match the registry")
    derived = derive_cells(json.loads(text(ORACLE_REPORT)))
    if cases.get("cells", {}).get("ids") != derived or cases["cells"].get("count") != len(derived):
        add("D010", "registered cells differ from the cells derived from the committed oracle report")
    inv = next(a for a in load(root, INVENTORY)["artifacts"] if a["id"] == "verifier-runtime-differential")
    reports = [a for a in cases.get("artifacts", []) if "committedPath" in a]
    if sorted((a["name"], a["committedPath"], tuple(a["profiles"])) for a in reports) != \
            sorted((Path(p).name, p, tuple(sorted(ORACLE_PROFILES))) for p in inv["paths"]):
        add("D010", "registered report artifacts differ from the inventory's verifier-runtime-differential paths")
    for a in (a for a in cases.get("artifacts", []) if "expected" in a):
        if any(part not in text(a["source"]) for part in a["expected"].split("|")) or not set(a["profiles"]) <= set(profiles):
            add("D010", f"artifact {a['name']} expected value does not match {a['source']}")

    # D011 timing-sensitive set: only by a recorded amendment and a #1407 amendment.
    ts, amended = protocol.get("cases", {}).get("timingSensitiveSet"), {a.get("version") for a in log}
    if not isinstance(ts, list) or any(e.get("case") not in universe or not e.get("reason") or e.get("amendment") not in amended
                                       or not e.get("contractAmendment") for e in ts):
        add("D011", "a timing-sensitive entry lacks a registered case, a reason, or its amendments")

    # D012 frozen vocabularies.
    if sorted(protocol.get("attemptStatuses", {})) != sorted(ATTEMPT_STATUSES) or sorted(protocol.get("caseClasses", {})) != sorted(CASE_CLASSES) \
            or protocol.get("executionVerdicts", {}).get("order") != VERDICT_ORDER:
        add("D012", "attempt statuses, case classes, or verdict order differ from the frozen sets")

    # D013 execution machinery: none may exist until a recorded amendment registers it.
    wf = protocol.get("workflow", {})
    if wf.get("status") == "pending-second-g2-pr":
        if (root / wf.get("path", "missing")).exists() or (root / wf.get("runner", "missing")).exists():
            add("D013", "execution machinery exists while the protocol records it as pending")
    elif wf.get("status") != "registered" or wf.get("amendment") not in amended or not all((root / wf.get(k, "missing")).exists() for k in ("path", "runner")):
        add("D013", "execution machinery must be registered by a recorded amendment")

    # D014 every declared gate site still runs the registered command; no runner configuration appeared.
    test_yml, publish = text(".github/workflows/test.yml"), text(".github/workflows/publish-nuget.yml")
    iso = profiles.get("oracle-isolated", {})
    if f"--filter \"{iso.get('filter')}\"" not in test_yml or any(f"project: {p['project']}" not in y for p in profiles.values()
                                                                   for y in (test_yml, publish)):
        add("D014", "a registered gate site no longer runs the registered oracle command or project")
    if any(any((root / p["project"]).parent.glob(g)) for p in profiles.values() for g in ("xunit.runner.json", "*.runsettings")):
        add("D014", "a registered project gained a runner configuration file")

    # D015 the packet and harness are frozen by hash.
    hashes = json.loads(text(HASHES)).get("files", {}) if (root / HASHES).exists() else {}
    for rel in FROZEN_FILES + wf.get("frozenFiles", []):
        if rel not in hashes or not (root / rel).exists() or (sha256_lf(root / rel) != hashes[rel] and not (texts and rel in texts)):
            add("D015", f"sha256.json does not cover {rel} or its hash differs")

    # D016 against the registration on protected main: any change is a recorded amendment, never a weakening.
    if baseline is not None:
        old_p, old_c, old_h = baseline
        if old_h != hashes:
            new = [a for a in log if a.get("version") not in {x.get("version") for x in old_p["amendments"]["amendmentLog"]}]
            if not new or vkey(version) <= vkey(old_p["protocolVersion"]) or log[:len(old_p["amendments"]["amendmentLog"])] != old_p["amendments"]["amendmentLog"]:
                add("D016", "the packet changed without a new protocol version and amendment entry (earlier entries are immutable)")
        weaker = [name for name, bad in (
            ("cases removed", not case_universe(old_p, old_c) <= universe),
            ("environments removed", not set(env_by_id(old_p)) <= set(env_ids)),
            ("fewer attempts", jobs * attempts < old_p["runPlan"]["jobsPerEnvironment"] * old_p["runPlan"]["attemptsPerJob"]),
            ("determinism rows removed", not set(r["id"] for r in old_p["determinismRows"]) <= set(rows)),
            ("timing-sensitive set grew", len(ts or []) > len(old_p["cases"]["timingSensitiveSet"]) and not all(e.get("contractAmendment") for e in ts)),
        ) if bad]
        if weaker:
            add("D016", f"the amendment weakens the registration: {', '.join(weaker)}")
    return v


def parse_trx(path: Path) -> tuple[dict[str, list[str]], str]:
    root = ET.parse(path).getroot()
    outcomes: dict[str, list[str]] = {}
    for result in root.findall(".//{*}UnitTestResult"):
        outcomes.setdefault(result.attrib["testName"], []).append(result.attrib.get("outcome", "Missing"))
    summary = root.find("./{*}ResultSummary")
    return outcomes, summary.attrib.get("outcome", "Missing") if summary is not None else "Missing"


def profile_values(profile, cases, outcomes, summary: str, exit_code, timed_out: bool, record_dir: Path | None) -> dict:
    """Turn one invocation's raw output into registered case values. Every observed value is kept;
    only a value that was never observed is filled (Timeout, Crash when no TRX exists, else Missing)."""
    fill = "Timeout" if timed_out else ("Crash" if outcomes is None else "Missing")
    outcomes = outcomes or {}
    result = {"invocation": f"{'timeout' if timed_out else exit_code}|{summary}", "tests": {}, "cells": None, "artifacts": {}, "unregistered": []}
    expected = selection(profile, cases) if profile["select"] != "observed" else {n: len(o) for n, o in outcomes.items()}
    for name, multiplicity in expected.items():
        got = sorted(outcomes.get(name, []))
        if len(got) > multiplicity:
            result["unregistered"].append(f"test:{name} (extra results)")
        result["tests"][name] = ",".join(got + [fill] * (multiplicity - len(got)))
    result["unregistered"] += [f"test:{n}" for n in outcomes if n not in expected]
    if profile["oracle"]:
        registered = {c["id"]: c for c in cases["cells"]["ids"]}
        cells = {cid: fill for cid in registered}
        cell_file = record_dir / "cells.json" if record_dir else None
        seen: set = set()
        for cell in json.loads(cell_file.read_text(encoding="utf-8")) if cell_file and cell_file.exists() else []:
            cid = cell.get("id") if isinstance(cell, dict) else None
            identity = {k: cell.get(k) for k in ("id", "formId", "position", "nestingDepth", "polarity")} if cid else None
            if cid not in registered or identity != registered[cid] or cid in seen or not isinstance(cell.get("mismatch"), bool):
                result["unregistered"].append(f"cell:{cid} (unregistered, duplicate, or malformed)")
                if cid in cells:
                    cells[cid] = "Malformed"
            else:
                cells[cid] = f"{cell_digest(cell)}|{1 if cell['mismatch'] else 0}"
            seen.add(cid)
        result["cells"] = cells
    for artifact in profile_artifacts(profile, cases):
        generated = record_dir / "generated" / artifact["name"] if record_dir else None
        result["artifacts"][artifact["name"]] = sha256_file(generated) if generated and generated.exists() else fill
    return result


def _is_pass(key: str, value: str, expected_artifacts: dict[str, str]) -> bool:
    kind, _, rest = key.partition(":")
    if kind == "invocation":
        return value == "0|Completed"
    if kind == "test":
        return all(part == "Passed" for part in value.split(","))
    if kind == "cell":
        return bool(CELL_VALUE.match(value)) and value.endswith("|0")
    return value == expected_artifacts.get(rest)


def _record_problem(r, protocol, cases, mode, envs, shape, expected_commit, run_ids, hashes) -> str | None:
    profiles_all, jobs, attempts = shape
    keys = set(protocol["recordFormats"]["attempt"])
    if set(r) - {"reason"} != keys:
        return "fields differ from recordFormats.attempt"
    if r["schemaVersion"] != 1 or (r["protocolSha256"], r["harnessSha256"]) != hashes or r["protocolVersion"] != protocol["protocolVersion"] \
            or r["mode"] != mode:
        return "made under other protocol or harness bytes, schema, or mode"
    if r["executionId"] not in run_ids or str(r["runId"]) != str(run_ids[r["executionId"]]) or r["commit"] != expected_commit:
        return "not from the registered run or commit"
    if r["environment"] not in envs or r["job"] not in range(1, jobs + 1) or r["attempt"] not in range(1, attempts + 1):
        return "from an unregistered environment, job, or attempt"
    if str(r["runAttempt"]) != "1":
        return "from a re-run job"
    if r["status"] not in ATTEMPT_STATUSES or (r["status"] == "environment-violation") != bool((r["environmentCheck"] or {}).get("violations")):
        return "status inconsistent with its environment check"
    expected = [p["id"] for p in profiles_all if mode == "control" or p["id"] in envs[r["environment"]]["profiles"]]
    got = [p.get("profile") for p in r["profiles"]]
    if len(got) != len(set(got)) or got != expected[:len(got)] or (r["status"] in VALUE_STATUSES and got != expected):
        return "profiles duplicated, out of order, or missing"
    for res in r["profiles"]:
        p = next(x for x in profiles_all if x["id"] == res["profile"])
        if set(res) != PROFILE_KEYS:
            return "a profile result has unexpected fields"
        if p["select"] != "observed":
            sel = selection(p, cases)
            if set(res["tests"]) != set(sel) or any(len(res["tests"][n].split(",")) != m for n, m in sel.items()):
                return f"profile {p['id']} test names or multiplicities differ from the registry"
        if any(not re.match(r"^[A-Za-z]+$", part) for val in res["tests"].values() for part in val.split(",")):
            return f"profile {p['id']} has a malformed outcome"
        cells_ok = res["cells"] is None if not p["oracle"] else (
            isinstance(res["cells"], dict) and set(res["cells"]) == {c["id"] for c in cases["cells"]["ids"]}
            and all(CELL_VALUE.match(x) or x in FILL_VALUES | {"Malformed"} for x in res["cells"].values()))
        arts = {a["name"] for a in profile_artifacts(p, cases)}
        if not cells_ok or set(res["artifacts"]) != arts or any(not (SHA.match(x) or x in FILL_VALUES) for x in res["artifacts"].values()):
            return f"profile {p['id']} cells or artifacts differ from the registry"
        if res["unregistered"]:
            return f"profile {p['id']} reported unregistered {res['unregistered'][:3]}"
    return None


def decide(root: Path, records: list[dict], run_ids: dict[str, str], expected_commit: str, mode: str, protocol=None, cases=None) -> dict:
    protocol = protocol if protocol is not None else load(root, PROTOCOL)
    cases = cases if cases is not None else load(root, CASES)
    envs = env_by_id(protocol)
    if mode == "control":
        shape = ([protocol["control"]["profile"]], protocol["control"]["jobsPerEnvironment"], protocol["control"]["attemptsPerJob"])
    else:
        shape = (protocol["profiles"], protocol["runPlan"]["jobsPerEnvironment"], protocol["runPlan"]["attemptsPerJob"])
    profiles, jobs, attempts = shape
    hashes = (sha256_file(root / PROTOCOL), sha256_file(root / HARNESS))
    expected_artifacts = {a["name"]: sha256_file(root / a["committedPath"]) if "committedPath" in a else sha256_bytes(a["expected"].encode())
                          for a in cases["artifacts"]}
    invalid, seen = [], {}
    if not re.match(r"^[0-9a-f]{40}$", expected_commit or ""):
        invalid.append("the expected commit is not a full SHA")
    for r in records:
        key = tuple(r.get(k) for k in ("executionId", "environment", "job", "attempt"))
        problem = "duplicated" if key in seen else _record_problem(r, protocol, cases, mode, envs, shape, expected_commit, run_ids, hashes)
        if problem:
            invalid.append(f"record {key}: {problem}")
        else:
            seen[key] = r

    expected: dict[str, int] = {}
    values: dict[str, list[tuple[str, str]]] = {}
    status_counts = {s: 0 for s in ATTEMPT_STATUSES + ["missing"]}
    for eid in run_ids:
        for env_id, env in envs.items():
            for job in range(1, jobs + 1):
                for attempt in range(1, attempts + 1):
                    r = seen.get((eid, env_id, job, attempt))
                    status_counts[r["status"] if r else "missing"] += 1
                    results = {x["profile"]: x for x in (r["profiles"] if r and r["status"] not in ("environment-violation", "infrastructure-failure") else [])}
                    for p in (p for p in profiles if mode == "control" or p["id"] in env["profiles"]):
                        res = results.get(p["id"]) if results.get(p["id"], {}).get("status") in VALUE_STATUSES else None
                        names = list(res["tests"]) if p["select"] == "observed" and res else []
                        for k in profile_keys(p, cases) + [f"test:control:{n}" for n in names]:
                            expected[k] = expected.get(k, 0) + (0 if k.startswith("test:control:") else 1)
                            if res is not None:
                                kind, _, rest = k.partition(":")
                                source = {"invocation": {rest: res["invocation"]}, "test": res["tests"], "cell": res["cells"],
                                          "artifact": res["artifacts"]}[kind]
                                values.setdefault(k, []).append((env_id, source[rest.split(":", 1)[1] if kind == "test" else rest]))
    if mode == "control":
        per = len(envs) * jobs * attempts * len(run_ids)
        expected.update({k: per for k in values if k.startswith("test:control:")})

    rows_out, counts = [], {c: 0 for c in CASE_CLASSES}
    for key in sorted(expected):
        present = values.get(key, [])
        distinct = sorted({x for _, x in present})
        klass = "DISAGREE" if len(distinct) > 1 else "INCOMPLETE" if len(present) < expected[key] or not present else \
            "AGREE-PASS" if _is_pass(key, distinct[0], expected_artifacts) else "AGREE-FAIL"
        counts[klass] += 1
        row = {"case": key, "class": klass, "contributions": len(present), "expected": expected[key]}
        if klass != "AGREE-PASS":
            by_env: dict[str, dict[str, int]] = {}
            for env_id, x in present:
                by_env.setdefault(env_id, {})[x] = by_env.get(env_id, {}).get(x, 0) + 1
            row["valuesByEnvironment"] = by_env
            row["platformDependent"] = klass == "DISAGREE" and all(len(x) == 1 for x in by_env.values())
        rows_out.append(row)
    verdict = next(name for name, hit in (("INVALID", invalid), ("NON-DETERMINISTIC", counts["DISAGREE"]), ("FAILING", counts["AGREE-FAIL"]),
                                          ("INCOMPLETE", counts["INCOMPLETE"] or not rows_out), ("DETERMINISTIC", True)) if hit)
    classes = {r["case"]: r["class"] for r in rows_out}
    det_rows = []
    for row in protocol["determinismRows"] if mode == "execution" else []:
        mapped = [k for pat in row["cases"] for k in ([c for c in classes if c.startswith("cell:")] if pat == "cell:*" else [pat])]
        ran = all(any(e == x[0] for k in mapped for x in values.get(k, [])) for e in row["environments"])
        det_rows.append({"id": row["id"], "status": "RESOLVED" if not invalid and ran and all(classes.get(k) == "AGREE-PASS" for k in mapped) else "OPEN",
                         "classes": {c: sum(classes.get(k) == c for k in mapped) for c in CASE_CLASSES}})
    return {"schemaVersion": 1, "protocolVersion": protocol["protocolVersion"], "protocolSha256": hashes[0], "harnessSha256": hashes[1],
            "mode": mode, "executionIds": sorted(run_ids), "runIds": run_ids, "commit": expected_commit,
            "verdict": ("CONTROL-" + verdict) if mode == "control" else verdict,
            "complete": not invalid and status_counts["missing"] == 0 and not counts["INCOMPLETE"],
            "attempts": status_counts, "invalidReasons": invalid, "classCounts": counts, "cases": rows_out,
            "determinismRows": det_rows, "environments": sorted(envs), "notCovered": protocol["scope"]["notCovered"],
            "limitations": protocol["limitations"] + (["CONTROL RUN: no registered case; never decision-bearing."] if mode == "control" else [])}


def git_show(ref: str, rel: str):
    out = subprocess.run(["git", "show", f"{ref}:{rel}"], cwd=ROOT, capture_output=True, check=False)
    return json.loads(out.stdout) if out.returncode == 0 else None


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("validate").add_argument("--baseline-ref")
    d = sub.add_parser("decide")
    for name in ("--records", "--execution-id", "--run-id"):
        d.add_argument(name, action="append", required=True)
    for name in ("--commit", "--mode", "--out"):
        d.add_argument(name, required=True)
    args = parser.parse_args(argv)
    if args.command == "validate":
        baseline = None
        if args.baseline_ref:
            old = [git_show(args.baseline_ref, rel) for rel in (PROTOCOL, CASES, HASHES)]
            baseline = (old[0], old[1], old[2]["files"]) if all(old) else None
            print(f"baseline {args.baseline_ref}: {'registered packet found' if baseline else 'no registered packet yet'}")
        violations = validate(ROOT, baseline=baseline)
        for code, message in violations:
            print(f"{code}: {message}")
        print("protocol valid" if not violations else f"{len(violations)} violation(s)")
        return 1 if violations else 0
    if len(args.execution_id) != len(args.run_id):
        parser.error("give one --run-id per --execution-id")
    records = [json.loads(p.read_text(encoding="utf-8")) for d in args.records for p in sorted(Path(d).rglob("attempt-*.json"))]
    result = decide(ROOT, records, dict(zip(args.execution_id, args.run_id)), args.commit, args.mode)
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_bytes((json.dumps(result, indent=1, sort_keys=True) + "\n").encode("utf-8"))
    print(json.dumps({k: result[k] for k in ("verdict", "complete", "attempts", "classCounts", "determinismRows", "invalidReasons")}, indent=1))
    return 0 if result["verdict"] in ("DETERMINISTIC", "CONTROL-DETERMINISTIC") else 1


if __name__ == "__main__":
    sys.exit(main())
