#!/usr/bin/env python3
"""0.24 G2 (#1421): the verifier determinism protocol — registration validator and decider.

The protocol is docs/plans/evidence/g2-1421/protocol.json; its case registry is cases.json.
This module holds what the registration freezes: the validator (codes D001-D016), the value
rules that turn one test invocation into registered case values, and the agreement decider.
The execution machinery (workflow, environment check, attempt runner, budget and history
guards) is scripts/determinism_runner.py and .github/workflows/determinism-protocol.yml,
registered by amendment 1.1.0; #1135 executes it.

  validate [--baseline-ref REF]   fail closed on registration defects; with a baseline,
                                  also on unrecorded or weakening changes to the packet
  decide --records DIR --execution-id ID --run-id ID --commit SHA --mode M --out FILE

Nothing here retries, normalizes a compared value, or converts a missing value to agreement.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import ntpath
import posixpath
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
RECORDER = "tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DeterminismRecord.cs"
FROZEN_FILES = [PROTOCOL, CASES, README, HARNESS, "scripts/test_determinism_protocol.py", RECORDER,
                "tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialModels.cs"]
# Instrumentation call sites that must stay in the oracle's test code (D014).
CALL_SITES = {"tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs": ["DeterminismRecord.WriteCells(results);"],
              "tests/Calor.Verification.Tests/VerifierRuntimeDifferential/VerifierRuntimeDifferentialTests.cs":
                  ['DeterminismRecord.WriteGenerated("verifier-runtime-differential.json"', 'DeterminismRecord.WriteGenerated("verifier-runtime-differential.md"'],
              "tests/Calor.Verification.Tests/ContractTranslatorSemanticsVersionGuardTests.cs": ['DeterminismRecord.WriteGenerated(\n            "translator-fixture"']}
CELL_FIELDS = {"id": str, "formId": str, "category": str, "position": str, "nestingDepth": int, "polarity": str, "solverStatus": str,
               "runtimeVerdict": str, "guardForced": bool, "elidedWhenEnabled": bool, "solverHandled": bool, "mismatch": bool,
               "detail": (str, type(None))}
MODES = {"execution", "control"}


def parse_workflow(text: str) -> dict:
    """Minimal parser for the GitHub workflow subset the gates use (no YAML library is available):
    jobs at indent 2, job keys at 4, steps as '- ' items at 6 with keys at 8, block scalars kept.
    Returns {job: {"keys": {key: value}, "steps": [{key: value}]}}."""
    lines, jobs, i = text.splitlines(), {}, 0
    while i < len(lines) and lines[i] != "jobs:":
        i += 1
    job = step = None
    while i + 1 < len(lines):
        i += 1
        line = lines[i]
        indent, body = len(line) - len(line.lstrip(" ")), line.strip()
        if not body or body.startswith("#") or indent == 0:
            continue
        if indent == 2 and body.endswith(":"):
            job, step = jobs.setdefault(body[:-1], {"keys": {}, "steps": []}), None
            continue
        if job is None:
            continue
        if indent == 6 and body.startswith("- "):
            step = {}
            job["steps"].append(step)
            body, indent = body[2:], 8
        target = step if indent == 8 and step is not None else job["keys"] if indent == 4 else None
        if target is None:
            continue
        match = re.match(r"^([A-Za-z0-9_-]+):(?: (.*))?$", body)
        if match is None:  # quoted keys, flow mappings, anchors, merge keys: never silently ignored
            target.setdefault("__unparsed__", []).append(body)
            continue
        key, value = match.group(1), (match.group(2) or "").strip()
        if key == "steps" and target is job["keys"]:
            continue
        if value in ("|", ">-", ">", "|-") or value == "":
            block = []
            while i + 1 < len(lines) and (not lines[i + 1].strip() or len(lines[i + 1]) - len(lines[i + 1].lstrip(" ")) > indent):
                i += 1
                block.append(lines[i])
            width = min((len(b) - len(b.lstrip(" ")) for b in block if b.strip()), default=0)
            block = [b[width:].rstrip() for b in block]
            value = (" ".join(b for b in block if b) if value.startswith(">") else "\n".join(block).strip("\n")) if value else "\n".join(block)
        target[key] = value.strip("'\"") if value[:1] in "'\"" and value[-1:] == value[:1] else value
    return jobs


def shell_commands(script: str) -> list[tuple[int, str]]:
    """Logical commands of a bash script with their nesting depth (if/for/while/case/{/( blocks);
    heredoc bodies and comments are skipped."""
    out, depth, heredoc, pending = [], 0, None, ""
    for raw in script.splitlines():
        if heredoc is not None:
            heredoc = None if raw.strip() == heredoc else heredoc
            continue
        line = pending + raw.strip()
        if line.endswith("\\"):
            pending = line[:-1] + " "
            continue
        pending = ""
        if not line or line.startswith("#"):
            continue
        tag = re.search(r"<<-?\s*'?\"?([A-Za-z_]+)'?\"?", line)
        heredoc = tag.group(1) if tag else None
        first = re.split(r"[\s;]", line, 1)[0]
        if first in ("fi", "done", "esac", "}", ")") or line.startswith(")"):
            depth -= 1
        out.append((depth, line))
        opens = first in ("if", "for", "while", "until", "case", "{", "(") or line.startswith("(") or line.endswith(("{", "("))
        closes = bool(re.search(r"(;|\s)(fi|done|esac)$", line)) or (line.endswith("}") and "{" in line and first != "}")
        depth += 1 if opens and not closes else 0
    return out


def top_level_ok(script: str, command: str) -> bool:
    """The command is an unconditional top-level line after an effective 'set -euo pipefail',
    with no operator that can swallow its status and nothing before it that disables errexit or exits."""
    cmds = shell_commands(script)
    if not cmds or cmds[0] != (0, "set -euo pipefail"):
        return False
    for depth, line in cmds[1:]:
        if line == command:
            return depth == 0
        if depth == 0 and re.match(r"^(exit|return|set \+[a-z]*e|trap\b.*\bERR\b)", line):
            return False
    return False


def home_safe(source: str) -> bool:
    """The harness never touches the real home directory (it isolates HOME under the job temp dir):
    no home-directory lookup, user expansion, or read of the HOME or USERPROFILE variables."""
    return not re.search(r"Path\.home\(|expanduser\(|getenv\(\s*['\"](HOME|USERPROFILE)|environ(\.get\(|\[)\s*['\"](HOME|USERPROFILE)", source)


def check_gates(protocol, text) -> list[str]:
    """D014 structural check of every registered gate step (protocol.json gates)."""
    problems, parsed = [], {}
    gates = protocol.get("gates", {})
    always = re.compile(r"always\(\)|failure\(\)|cancelled\(\)")
    for gate in gates.get("steps", []) + gates.get("shardSteps", []):
        wf = parsed.setdefault(gate["workflow"], parse_workflow(text(gate["workflow"])))
        job = wf.get(gate["job"])
        where = f"{gate['workflow']} {gate['job']} '{gate['step']}'"
        if job is None or {"if", "continue-on-error", "__unparsed__"} & set(job["keys"]):
            problems.append(f"{where}: job missing, conditional, or allowed to fail")
            continue
        names = [s.get("name") for s in job["steps"]]
        if names.count(gate["step"]) != 1:
            problems.append(f"{where}: not exactly one step with this name")
            continue
        index = names.index(gate["step"])
        step, script = job["steps"][index], job["steps"][index].get("run", "")
        if set(step) - {"name", "run", "env"} or step.get("shell", "bash") != "bash":
            problems.append(f"{where}: step has a condition, continue-on-error, a shell override, or is not a run step")
        if any(a not in names[:index] for a in gate.get("after", [])):
            problems.append(f"{where}: a required earlier step is missing or after it")
        for later in job["steps"][index + 1:]:
            if always.search(later.get("if", "")) and not later.get("uses", "").startswith("actions/upload-artifact@") \
                    and later.get("name") not in gates.get("alwaysAllowed", []):
                problems.append(f"{where}: later step '{later.get('name')}' runs after a failure and is not registered")
        if "script" in gate:
            lines = [ln for ln in script.splitlines() if ln.strip()]
            if lines != gate["script"] or any(not top_level_ok(script, c) for c in gate.get("commands", [])):
                problems.append(f"{where}: script differs from the frozen script or a gate command is not top-level")
        else:
            cmds = shell_commands(script)
            if gate.get("scriptSha256") != sha256_bytes("\n".join(ln for ln in script.splitlines() if ln.strip()).encode("utf-8")):
                problems.append(f"{where}: shard script differs from its frozen hash")
            masked = any(re.search(r"\|\|\s*(true|:|exit 0)\b|^set \+[a-z]*e", c) and not c.startswith("trap ") for _, c in cmds)
            if not cmds or cmds[0] != (0, "set -euo pipefail") or masked or not any("dotnet test" in c for _, c in cmds):
                problems.append(f"{where}: shard script lacks set -euo pipefail, runs no dotnet test, or masks a status")
    return problems


WORKFLOW, RUNNER = ".github/workflows/determinism-protocol.yml", "scripts/determinism_runner.py"
DECIDE_IF = "always() && needs.plan.result == 'success'"
ISOLATE = "Isolate the home directory and refuse a re-run"
JOB_SHAPE = {  # job -> (job keys compared exactly, [(step name, required runner command or action, if)])
    "plan": ({"runs-on": "ubuntu-24.04", "timeout-minutes": "planJobTimeoutMinutes"},
             [(ISOLATE, None, None), ("Checkout code", "actions/checkout@v4", None),
              ("Validate, check history and budget, and plan the run", "plan", None)]),
    "attempts": ({"needs": "plan", "name": "attempts (${{ matrix.env }} job ${{ matrix.job }})", "runs-on": "${{ matrix.runner }}",
                  "timeout-minutes": "${{ matrix.timeout }}"},
                 [(ISOLATE, None, None), ("Checkout code", "actions/checkout@v4", None),
                  ("Install the pinned SDK into a private root", "install-sdk", None),
                  ("Bootstrap verified Z3 assets", "./.github/actions/bootstrap-z3", None),
                  ("Check and record the environment", "env-check", None), ("Build the registered test hosts", None, None),
                  ("Run every attempt of this job", "run-job", None), ("Record attempts that did not run", "fill-missing", "always()"),
                  ("Upload attempt records", "actions/upload-artifact@v4", "always()")]),
    "decide": ({"needs": "[plan, attempts]", "if": DECIDE_IF, "runs-on": "ubuntu-24.04", "timeout-minutes": "decideJobTimeoutMinutes"},
               [(ISOLATE, None, None), ("Checkout code", "actions/checkout@v4", None),
                ("Download every attempt record", "actions/download-artifact@v4", None),
                ("Decide agreement under the frozen rule", "decide", None),
                ("Upload the result record", "actions/upload-artifact@v4", "always()")]),
}


def _block(lines, key, indent) -> list[str]:
    if key not in lines:
        return []
    out = []
    for ln in lines[lines.index(key) + 1:]:
        if ln.strip() and len(ln) - len(ln.lstrip()) <= indent:
            break
        if ln.strip():
            out.append(ln)
    return out


def workflow_problems(text: str, protocol) -> list[str]:
    """D013 structure of the registered workflow: dispatch only with exactly the registered inputs; the
    three jobs with their registered keys, steps, order, runner commands, and timeouts; fail-fast false;
    nothing that can mask a status; no YAML form the minimal parser does not read."""
    problems, lines = [], [ln for ln in text.splitlines() if not ln.lstrip().startswith("#")]
    top = [ln for ln in lines if ln and not ln.startswith(" ")]
    if sorted(t.split(":")[0] for t in top) != sorted(["name", "run-name", "on", "permissions", "concurrency", "jobs"]) or "on:" not in top:
        problems.append(f"top-level keys differ from the registered set: {top}")
    triggers = [ln.strip() for ln in _block(lines, "on:", 0) if len(ln) - len(ln.lstrip()) == 2]
    if triggers != ["workflow_dispatch:"]:
        problems.append(f"triggers must be exactly workflow_dispatch (no automatic trigger): {triggers}")
    if [ln.strip() for ln in _block(lines, "    inputs:", 4)] != ["mode:", "type: choice", "options: [control, execution]", "required: true",
                                                               "execution_id:", "type: string", "required: true"]:
        problems.append("inputs must be exactly mode (choice of control, execution) and execution_id")
    if sorted(ln.strip() for ln in _block(lines, "permissions:", 0)) != ["actions: read", "contents: read"]:
        problems.append("permissions must be exactly contents: read and actions: read")
    if [ln.strip() for ln in _block(lines, "concurrency:", 0)] != ["group: determinism-protocol", "cancel-in-progress: false"]:
        problems.append("concurrency must be one global group that never cancels a run in progress")
    if re.search(r"continue-on-error|\|\||set \+[a-z]*e\b|<<:|:\s+[&*!]\w|^\s*-\s+[&*!]\w|:\s+\{(?!\{)|\s#|^---|^\.\.\.", re.sub(r"\$\{\{.*?\}\}", "", "\n".join(lines)), re.M):
        problems.append("a status can be masked (continue-on-error, ||, set +e) or the file uses YAML the checker does not read")
    jobs, budget = parse_workflow(text), protocol["budget"]
    if sorted(jobs) != sorted(JOB_SHAPE):
        return problems + [f"jobs must be exactly plan, attempts, decide: {sorted(jobs)}"]
    for name, (keys, steps) in JOB_SHAPE.items():
        job = jobs[name]
        want = {k: str(budget[v]) if v in budget else v for k, v in keys.items()}
        got = {k: v for k, v in job["keys"].items() if k not in ("outputs", "strategy", "defaults", "env")}
        if got != want:
            problems.append(f"job {name}: keys {got} differ from {want}")
        if [s.get("name") for s in job["steps"]] != [s[0] for s in steps]:
            problems.append(f"job {name}: steps differ from the registered steps")
            continue
        for step, (sname, command, condition) in zip(job["steps"], steps):
            run, uses = step.get("run", ""), step.get("uses", "")
            used = uses if uses else next(iter(re.findall(r"scripts/determinism_runner\.py (\S+)", run)), None)
            if "__unparsed__" in step or step.get("if") != condition or (command and used != command) or (uses and run) \
                    or (run and not run.startswith("set -euo pipefail\n")) or (not command and (uses or "determinism_runner" in run)) \
                    or len(re.findall(r"determinism_runner\.py", run)) > 1 or "timeout-minutes" in step:
                problems.append(f"job {name} step {sname!r}: command, condition, or form differs from the registration")
    strategy = [ln.strip() for ln in jobs["attempts"]["keys"].get("strategy", "").splitlines() if ln.strip()]
    if strategy != ["fail-fast: false", "matrix: ${{ fromJSON(needs.plan.outputs.matrix) }}"]:
        problems.append(f"attempts strategy must be exactly fail-fast false with the plan matrix: {strategy}")
    if 'test "$GITHUB_RUN_ATTEMPT" = 1' not in jobs["plan"]["steps"][0].get("run", "") or \
            len({j["steps"][0].get("run") for j in jobs.values()}) != 1:
        problems.append(f"every job must start with the same '{ISOLATE}' step")
    return problems


def _inside(path, roots) -> bool:
    if not isinstance(path, str) or not path or not roots:
        return False
    pm = ntpath if re.match(r"^[A-Za-z]:[\\/]", roots[0]) else posixpath
    norm = pm.normcase(pm.normpath(path))
    return any(norm == r or norm.startswith(r.rstrip(pm.sep) + pm.sep) for r in (pm.normcase(pm.normpath(x)) for x in roots))


def judge(protocol, env: dict, observed, commit: str) -> list[str]:
    """Environment violations of one job's observations (run by env-check and again by the decider)."""
    o, tc, v = observed if isinstance(observed, dict) else {}, protocol["toolchain"], []
    roots = [r for r in o.get("dotnetRoots") or [] if isinstance(r, str)]
    sdks = [re.match(r"^(\S+) \[(.+)\]$", s) if isinstance(s, str) else None for s in o.get("sdks") or []]
    runtimes = [re.match(r"^(\S+) (\S+) \[(.+)\]$", s) if isinstance(s, str) else None for s in o.get("runtimes") or []]
    if not sdks or not runtimes or None in sdks + runtimes:
        return ["SDK or runtime listing missing or unreadable"]
    netcore = [m.group(2) for m in runtimes if m.group(1) == "Microsoft.NETCore.App"]
    if [m.group(1) for m in sdks] != [tc["sdk"]] or o.get("dotnetVersion") != tc["sdk"]:
        v.append(f"SDKs {[m.group(1) for m in sdks]} (dotnet --version {o.get('dotnetVersion')}) are not exactly {tc['sdk']}")
    if netcore != [tc["runtime"]]:
        v.append(f"Microsoft.NETCore.App runtimes {netcore} are not exactly {tc['runtime']}")
    if not _inside(o.get("dotnetPath"), roots) or not all(_inside(m.group(m.lastindex), roots) for m in sdks + runtimes):
        v.append(f"dotnet {o.get('dotnetPath')} or an SDK or runtime is outside the private root {roots}")
    if (o.get("runnerOs"), o.get("runnerArch")) != (env["runnerOs"], env["runnerArch"]):
        v.append(f"runner {o.get('runnerOs')}/{o.get('runnerArch')} is not {env['runnerOs']}/{env['runnerArch']}")
    if o.get("logicalProcessors") != env["logicalProcessors"]:
        v.append(f"{o.get('logicalProcessors')} logical processors, registered {env['logicalProcessors']}")
    memory = o.get("memoryBytes")
    if not isinstance(memory, int) or memory < 0.9 * env["memoryGiB"] * 2 ** 30:
        v.append(f"physical memory {memory} bytes is below 90% of {env['memoryGiB']} GiB")
    rid = next(r for r in load(ROOT, Z3_CONSUMERS)["supportedRids"] if r["rid"] == env["rid"])
    pins = {p[1]: p[0] for p in (ln.split() for ln in (ROOT / ".github/z3-binaries-4.15.7.sha256").read_text(encoding="utf-8").splitlines()
                                 if ln.strip() and not ln.startswith("#")) if len(p) == 3}
    if o.get("z3") != {a: pins[a] for a in (rid["asset"], "Microsoft.Z3.dll")}:
        v.append(f"Z3 assets {o.get('z3')} differ from their pins")
    for key, want in (("commit", commit), ("autocrlf", "false"), ("dirty", ""), ("runAttempt", "1"), ("userProfileFollowsIsolatedHome", True)):
        if o.get(key) != want:
            v.append(f"{key} is {o.get(key)!r}, required {want!r}")
    if not all(isinstance(o.get(k), str) and o.get(k) for k in ("imageOs", "imageVersion")):
        v.append("the runner image (ImageOS, ImageVersion) is not recorded")
    return v


def harness_digest(root: Path) -> str:
    """harnessSha256 of a record (amendment 1.1.0): SHA-256 over the validator/decider, runner, and workflow bytes."""
    return sha256_bytes("\n".join(f"{rel} {sha256_file(root / rel)}" for rel in (HARNESS, RUNNER, WORKFLOW)).encode("utf-8"))


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


def contribution_matrix(protocol, cases) -> dict[str, int]:
    """Expected contributions per case per execution, with test multiplicity folded in."""
    profiles, per = profile_by_id(protocol), protocol["runPlan"]["jobsPerEnvironment"] * protocol["runPlan"]["attemptsPerJob"]
    matrix: dict[str, int] = {}
    for env in protocol["environments"]:
        for p in (profiles[pid] for pid in env["profiles"] if pid in profiles):
            for k in profile_keys(p, cases):
                mult = selection(p, cases).get(k.split(":", 2)[2], 1) if k.startswith("test:") else 1
                matrix[f"{env['id']}|{k}"] = matrix.get(f"{env['id']}|{k}", 0) + per * mult
    return matrix


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
    if ts != []:
        add("D011", "timingSensitiveSet must stay empty: the decider gives it no semantics until an amendment (with a #1407 amendment) adds them")

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
    else:
        for problem in workflow_problems(text(wf["path"]), protocol):
            add("D013", f"{wf['path']}: {problem}")
        for other in sorted((root / ".github/workflows").glob("*.y*ml")):
            rel = other.relative_to(root).as_posix()
            if rel != wf["path"] and "determinism_runner" in text(rel):
                add("D013", f"{rel} runs the execution machinery outside the registered workflow")

    if any(not home_safe(text(rel)) for rel in [HARNESS] + [f for f in wf.get("frozenFiles", []) if f.endswith(".py")]):
        add("D013", "a harness file references the real home directory")

    # D014 every declared gate site still runs the registered command; no runner configuration appeared.
    test_yml, publish = text(".github/workflows/test.yml"), text(".github/workflows/publish-nuget.yml")
    iso = profiles.get("oracle-isolated", {})
    if f"--filter \"{iso.get('filter')}\"" not in test_yml or any(f"project: {p['project']}" not in y for p in profiles.values()
                                                                   for y in (test_yml, publish)):
        add("D014", "a registered gate site no longer runs the registered oracle command or project")
    for problem in check_gates(protocol, text):
        add("D014", problem)
    if any(any((root / p["project"]).parent.glob(g)) for p in profiles.values() for g in ("xunit.runner.json", "*.runsettings")):
        add("D014", "a registered project gained a runner configuration file")
    if any(site not in text(rel) for rel, sites in CALL_SITES.items() for site in sites):
        add("D014", "a DeterminismRecord call site was removed from the oracle's test code")

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
        old_m, new_m = contribution_matrix(old_p, old_c), contribution_matrix(protocol, cases)
        new_cells, new_arts = cases.get("cells", {}).get("ids", []), cases.get("artifacts", [])
        old_rows = {r["id"]: r for r in old_p["determinismRows"]}
        weaker = [name for name, bad in (
            ("cases or contributions removed", any(new_m.get(k, 0) < n for k, n in old_m.items())),
            ("a gate was removed or changed", any(g not in protocol.get("gates", {}).get(k, []) for k in ("steps", "shardSteps")
                                                  for g in old_p.get("gates", {}).get(k, []))),
            ("a registered cell or artifact was repurposed", new_cells[:len(old_c["cells"]["ids"])] != old_c["cells"]["ids"]
             or any(a not in new_arts for a in old_c["artifacts"])),
            ("determinism row mapping narrowed", any(not (set(r["cases"]) <= set(rows.get(i, {}).get("cases", []))
                                                         and set(r["environments"]) <= set(rows.get(i, {}).get("environments", [])))
                                                     for i, r in old_rows.items())),
            ("environments removed", not set(env_by_id(old_p)) <= set(env_ids)),
            ("fewer attempts", jobs * attempts < old_p["runPlan"]["jobsPerEnvironment"] * old_p["runPlan"]["attemptsPerJob"]),
            ("determinism rows removed", not set(r["id"] for r in old_p["determinismRows"]) <= set(rows)),
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
            typed = isinstance(cell, dict) and set(cell) == set(CELL_FIELDS) and all(
                isinstance(cell[f], t) and not (t is int and isinstance(cell[f], bool)) for f, t in CELL_FIELDS.items())
            if cid not in registered or identity != registered[cid] or cid in seen or not typed:
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
    check = r["environmentCheck"]
    if r["status"] not in ATTEMPT_STATUSES or (r["status"] != "infrastructure-failure" and not (
            isinstance(check, dict) and isinstance(check.get("violations"), list) and isinstance(check.get("observed"), dict))) \
            or (r["status"] == "environment-violation") != bool((check or {}).get("violations")):
        return "status inconsistent with its environment check, or no environment attestation"
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
        fills = any(part in FILL_VALUES for val in res["tests"].values() for part in val.split(","))
        timed = str(res["invocation"]).startswith("timeout|")
        if res["status"] not in VALUE_STATUSES | {"invalid"} or (not timed and not str(res["invocation"]).startswith(f"{res['exitCode']}|")) \
                or (res["status"] in VALUE_STATUSES and timed != (res["status"] == "timeout")) \
                or (res["status"] == "completed" and fills) or (res["status"] == "crash" and not fills):
            return f"profile {p['id']} status, exit code, invocation, and values are inconsistent"
    statuses = [x["status"] for x in r["profiles"]]
    if r["status"] in VALUE_STATUSES and r["status"] != next(s for s in ("invalid", "timeout", "crash", "completed") if s in statuses + ["completed"]):
        return "attempt status inconsistent with its profiles"
    if r["status"] not in ("infrastructure-failure", "environment-violation"):
        # Amendment 1.1.0: the attestation is re-judged here, so an empty violations list cannot hide observations.
        bad = [] if check["violations"] else judge(protocol, envs[r["environment"]], check["observed"], expected_commit)
        if bad or any(p.get("calorCacheExisted") is not False for p in r["profiles"]):
            return f"environment observations contradict the attestation or a home was not fresh: {bad[:3]}"
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
    hashes = (sha256_file(root / PROTOCOL), harness_digest(root))
    expected_artifacts = {a["name"]: sha256_file(root / a["committedPath"]) if "committedPath" in a else sha256_bytes(a["expected"].encode())
                          for a in cases["artifacts"]}
    invalid, seen = [], {}
    if not re.match(r"^[0-9a-f]{40}$", expected_commit or "") or mode not in MODES or (mode == "execution" and len(run_ids) != 1):
        invalid.append("unknown mode, an expected commit that is not a full SHA, or more than one execution pooled")
    for r in records:
        key = tuple(r.get(k) for k in ("executionId", "environment", "job", "attempt"))
        problem = "duplicated" if key in seen else _record_problem(r, protocol, cases, mode, envs, shape, expected_commit, run_ids, hashes)
        if problem:
            invalid.append(f"record {key}: {problem}")
        else:
            seen[key] = r

    expected: dict[str, int] = {}
    values: dict[str, list[tuple[str, str]]] = {}
    establishing: dict[str, int] = {}
    status_counts = {s: 0 for s in ATTEMPT_STATUSES + ["missing"]}
    for eid in run_ids:
        for env_id, env in envs.items():
            for job in range(1, jobs + 1):
                for attempt in range(1, attempts + 1):
                    r = seen.get((eid, env_id, job, attempt))
                    status_counts[r["status"] if r else "missing"] += 1
                    results = {x["profile"]: x for x in (r["profiles"] if r and r["status"] not in ("environment-violation", "infrastructure-failure") else [])}
                    for p in (p for p in profiles if mode == "control" or p["id"] in env["profiles"]):
                        res = results.get(p["id"]) if results.get(p["id"], {}).get("status") in VALUE_STATUSES | {"invalid"} else None
                        names = list(res["tests"]) if p["select"] == "observed" and res else []
                        for k in profile_keys(p, cases) + [f"test:control:{n}" for n in names]:
                            expected[k] = expected.get(k, 0) + (0 if k.startswith("test:control:") else 1)
                            if res is not None:
                                kind, _, rest = k.partition(":")
                                source = {"invocation": {rest: res["invocation"]}, "test": res["tests"], "cell": res["cells"],
                                          "artifact": res["artifacts"]}[kind]
                                values.setdefault(k, []).append((env_id, source[rest.split(":", 1)[1] if kind == "test" else rest]))
                                establishing[k] = establishing.get(k, 0) + (r["status"] != "invalid" and res["status"] != "invalid")
    if mode == "control":  # an observed control name is expected from every attempt
        per = len(envs) * jobs * attempts * len(run_ids)
        expected.update({k: per for k in values if k.startswith("test:control:")})

    rows_out, counts = [], {c: 0 for c in CASE_CLASSES}
    for key in sorted(expected):
        present = values.get(key, [])
        distinct = sorted({x for _, x in present})
        # Values from invalid attempts can reveal a disagreement but never count toward agreement.
        klass = "DISAGREE" if len(distinct) > 1 else "INCOMPLETE" if establishing.get(key, 0) < expected[key] or not present else \
            "AGREE-PASS" if _is_pass(key, distinct[0], expected_artifacts) else "AGREE-FAIL"
        counts[klass] += 1
        row = {"case": key, "class": klass, "contributions": establishing.get(key, 0), "expected": expected[key]}
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
            "complete": not invalid and not counts["INCOMPLETE"] and all(status_counts[s] == 0 for s in
                                                                          ("missing", "invalid", "infrastructure-failure", "environment-violation")),
            "attempts": status_counts, "invalidReasons": invalid, "classCounts": counts, "cases": rows_out,
            "determinismRows": det_rows, "environments": sorted(envs), "notCovered": protocol["scope"]["notCovered"],
            "limitations": protocol["limitations"] + (["CONTROL RUN: no registered case; never decision-bearing."] if mode == "control" else [])}


def load_baseline(root: Path, ref: str):
    """(protocol, cases, hashes) registered at ref; None only when ref verifiably has no packet."""
    def git(*args):
        return subprocess.run(["git", *args], cwd=root, capture_output=True, check=False)
    if git("rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}").returncode != 0:
        raise SystemExit(f"baseline ref {ref} does not resolve to a commit")
    present = [git("cat-file", "-e", f"{ref}:{rel}").returncode == 0 for rel in (PROTOCOL, CASES, HASHES)]
    if not any(present) and git("ls-tree", "-d", ref, PACKET).stdout == b"":
        return None
    if not all(present):
        raise SystemExit(f"baseline {ref} has a partial packet")
    old = [json.loads(git("show", f"{ref}:{rel}").stdout) for rel in (PROTOCOL, CASES, HASHES)]
    return old[0], old[1], old[2]["files"]


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    v = sub.add_parser("validate")
    v.add_argument("--baseline-ref")
    v.add_argument("--root", default=str(ROOT), help="tree to validate (the trusted entry point runs main's validator on a PR tree)")
    d = sub.add_parser("decide")
    d.add_argument("--records", action="append", required=True)
    d.add_argument("--execution-id", required=True)
    d.add_argument("--run-id", required=True)
    d.add_argument("--mode", required=True, choices=sorted(MODES))
    for name in ("--commit", "--out"):
        d.add_argument(name, required=True)
    args = parser.parse_args(argv)
    if args.command == "validate":
        root = Path(args.root).resolve()
        baseline = load_baseline(root, args.baseline_ref) if args.baseline_ref else None
        if args.baseline_ref:
            print(f"baseline {args.baseline_ref}: {'registered packet found' if baseline else 'no registered packet yet'}")
        violations = validate(root, baseline=baseline)
        for code, message in violations:
            print(f"{code}: {message}")
        print("protocol valid" if not violations else f"{len(violations)} violation(s)")
        return 1 if violations else 0
    records = [json.loads(p.read_text(encoding="utf-8")) for d in args.records for p in sorted(Path(d).rglob("attempt-*.json"))]
    result = decide(ROOT, records, {args.execution_id: args.run_id}, args.commit, args.mode)
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    Path(args.out).write_bytes((json.dumps(result, indent=1, sort_keys=True) + "\n").encode("utf-8"))
    print(json.dumps({k: result[k] for k in ("verdict", "complete", "attempts", "classCounts", "determinismRows", "invalidReasons")}, indent=1))
    return 0 if result["verdict"] in ("DETERMINISTIC", "CONTROL-DETERMINISTIC") else 1


if __name__ == "__main__":
    sys.exit(main())
