#!/usr/bin/env python3
"""Per-turn capture helpers for the agent harness (roadmap v0.16 §3.1 W1; #1094).

`run-pair.sh` / `run-bundle.sh` run the agent as

    claude --print --verbose --output-format stream-json --forward-subagent-text ... \\
        | tee transcript.jsonl | jq -c 'select(.type=="result")' > agent.json

so every run archives a per-turn `transcript.jsonl` next to the `agent.json`
result envelope (whose content is unchanged, so `detect_invalid_run` and
`token-usage.py` keep their input). This module is the ONE place the
transcript is read; the shell runners call it and the tests import it.

Subcommands (each prints one JSON document to stdout; exit codes below):

    turns <transcript.jsonl>
        {assistantMessages, subagentMessages, assistantMessagesIncludingSubagents,
         assistantEvents, assistantEventsWithoutId, resultEvents, events,
         eventTypes, source}
        `assistantMessages` is the field A-1.12 registers, defined as #1117
        defines it: the number of distinct **top-level** assistant `message.id`
        values — events with `parent_tool_use_id` null. stream-json emits one
        `assistant` event per content block, so events are NOT turns; the
        message id is. Subagent messages (visible only because the runner passes
        --forward-subagent-text) are counted SEPARATELY as `subagentMessages`,
        so the registered field cannot be inflated by whether that flag was
        passed; `assistantMessagesIncludingSubagents` is their sum, reported for
        audit beside the corrected-token rule (A-1.9.1 sums subagent tokens).
        A missing / empty file yields zeros with source "missing"; exit 0.

    builds <transcript.jsonl> [--max-output N]
        One JSON object per line (JSONL) for every Bash tool call whose command
        mentions `dotnet build`, `dotnet test` or `calor` — the agent's own
        build stdout that §0.2 says was never archived — joined to its
        tool_result by tool_use_id: {index, toolCallOrdinal, toolUseId,
        messageId, parentToolUseId, command, kind, exitCode, isError, output,
        outputTruncated}. Exit 0 even when nothing matches.

    build-state <.calor-build-state.json>
        {compilerHash, optionsHash, manifestHash, formatVersion,
         compilerSemanticsVersion, source}; source "missing" (all null) when the
        file is absent or unreadable. Exit 0. (#1094: the compiler's own
        attestation of which product built the agent's code.)

    pair-config <pair.json> <arm-config-key> [--arm calor|csharp]
        Admission of the calor-arm config pin (gates doc §1) plus the ADDITIVE
        pre-rows control arm (roadmap §4.1). Exactly two configs are admitted:
            strict:   enforceEffects true, permissiveEffects false,
                      contractMode "debug", z3Required true, no controlArmKind
            pre-rows: the same with permissiveEffects true AND
                      controlArmKind "pre-rows"
        Anything else — permissive without controlArmKind, controlArmKind
        without permissive, an unknown controlArmKind, any other value of
        the four pins, a missing arm entry — is rejected: reason on stdout in
        the JSON (`admitted:false, reason`), exit 3. The C# arm has no config
        pin and is always admitted with controlArmKind null.
        Also resolves the FIXTURE directory for the arm entry
        (`arms[key].fixture`, defaulting to the arm language), so a pair can
        carry per-arm starters (PP-W-rows: `before/` for the pre-rows arm and
        `after/` for the strict arm, §4.1), and the REFERENCE solution the
        --null-agent path applies (`reference` / `referenceSource`): pre-0.16
        pairs ship `reference/<fixture>/`, the PP-W-rows pairs declare their
        per-arm clean cell as `seeded.clean.<armId>`. `reference` is null when
        the pair has none; the runner decides that is fatal for --null-agent.

    heldout-final <.ho_final.txt>
        {failedTests, silenceFailures, valueOnlyFailures, source}. The SIMPLE
        method names of the held-out tests that failed in the declared-done run
        — the spelling `pair.json` uses for `effectObservingTests`. A-1.12's
        leg-A escape is "at least one named effectObservingTest failing on a
        workspace that BUILT", a per-TEST fact `result.json`'s aggregate
        `escapedBugs` count cannot carry: that count includes failures of tests
        that are not effect-observing (W-006 with the Map off-by-one unfixed
        gives Failed: 8 where the two SURVIVORS are the effect-observing pair)
        and it does not condition on the build.
        `silenceFailures` is the subset whose assertion text carries the
        SILENCE signature ("Strings differ" / `Expected: ""`). Only those may be
        read as an escape: W-001's and W-003's effect-observing tests assert the
        return value BEFORE the silence assertion, so a silent-but-wrong
        implementation fails a named test having laundered nothing.
        source "missing" when the log is absent, which is also what a
        declared-done state that did not build looks like (run-pair.sh runs the
        held-out suite only after a successful build). Exit 0.

    leg-b-pairs <pins.json>
        {legBPairs, blindPairs, suite, excludedFromLegB}. `legBPairs` is the
        registered leg-B denominator (roadmap §3.1 W2 / §4.1) read from the
        epoch's pins.json, never a script default. Exit 3 when the field is
        missing, empty, non-unique, or names a pair outside `suite`.

    self-test
        Runs the module's own examples; exit 0 on success.

Python 3.9 compatible; standard library only.
"""

import argparse
import json
import os
import re
import sys

# `calor` must be an INVOCATION, not any occurrence of the word. The calor arm's workspace
# is `mktemp -d .../p0-<pair>-calor-XXXXXX`, so a bare \bcalor\b matched the WORKSPACE PATH
# in nearly every Bash command the agent ran ("cd .../p0-N1-001-calor-ab12/src && ls") while
# the C# arm's `-csharp-` path did not — an arm-asymmetric inflation of agentBuilds in the
# very artifact that is supposed to answer "why more turns".
BUILD_COMMAND_RE = re.compile(
    r"\bdotnet\s+(build|test)\b"
    r"|(?:^|[;&|(]\s*|\s)(?:dotnet\s+\S*calor\.dll|calor)(?=\s|$)")
EXIT_CODE_RE = re.compile(r"(?:Exit code|exit code|exited with code|Exit Code):?\s*(-?\d+)")
DEFAULT_MAX_OUTPUT = 4000

STRICT_CONFIG = {
    "enforceEffects": True,
    "permissiveEffects": False,
    "contractMode": "debug",
    "z3Required": True,
}
PRE_ROWS_KIND = "pre-rows"
ADMITTED_CONTROL_ARM_KINDS = (PRE_ROWS_KIND,)
PAIR_CONFIG_REJECT_EXIT = 3


# ---------------------------------------------------------------------------
# transcript reading
# ---------------------------------------------------------------------------
def read_events(path):
    """Yield parsed JSON objects from a JSONL transcript; non-JSON lines and
    non-object lines are skipped (counted by the caller via `events`)."""
    events = []
    if not path or not os.path.isfile(path):
        return events
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except ValueError:
                continue
            if isinstance(obj, dict):
                events.append(obj)
    return events


def _message_id(event):
    message = event.get("message")
    if isinstance(message, dict):
        mid = message.get("id")
        if isinstance(mid, str) and mid:
            return mid
    return None


def count_turns(path):
    events = read_events(path)
    if not events:
        return {
            "assistantMessages": 0,
            "subagentMessages": 0,
            "assistantMessagesIncludingSubagents": 0,
            "assistantEvents": 0,
            "assistantEventsWithoutId": 0,
            "resultEvents": 0,
            "events": 0,
            "eventTypes": {},
            "source": "missing",
        }
    top, sub = set(), set()
    assistant_events = 0
    without_id = 0
    result_events = 0
    types = {}
    for event in events:
        kind = event.get("type")
        types[str(kind)] = types.get(str(kind), 0) + 1
        if kind == "result":
            result_events += 1
        if kind != "assistant":
            continue
        assistant_events += 1
        mid = _message_id(event)
        if mid is None:
            without_id += 1
            continue
        if event.get("parent_tool_use_id"):
            sub.add(mid)
        else:
            top.add(mid)
    # An id seen both top-level and under a subagent (should not happen) is
    # counted once, on the top-level side.
    sub -= top
    return {
        # #1117's definition, registered by A-1.12: top-level ids only.
        "assistantMessages": len(top),
        "subagentMessages": len(sub),
        "assistantMessagesIncludingSubagents": len(top) + len(sub),
        "assistantEvents": assistant_events,
        "assistantEventsWithoutId": without_id,
        "resultEvents": result_events,
        "events": len(events),
        "eventTypes": types,
        "source": "transcript",
    }


def _tool_result_text(block):
    """Flatten a tool_result content payload (string or list of blocks)."""
    content = block.get("content")
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        parts = []
        for item in content:
            if isinstance(item, dict):
                if item.get("type") == "text" and isinstance(item.get("text"), str):
                    parts.append(item["text"])
                elif isinstance(item.get("text"), str):
                    parts.append(item["text"])
            elif isinstance(item, str):
                parts.append(item)
        return "\n".join(parts)
    if content is None:
        return ""
    return json.dumps(content)


CALOR_INVOCATION_RE = re.compile(
    r"(?:^|[;&|(]\s*|\s)(?:dotnet\s+\S*calor\.dll|calor)(?=\s|$)")


def classify_command(command):
    if re.search(r"\bdotnet\s+test\b", command):
        return "dotnet-test"
    if re.search(r"\bdotnet\s+build\b", command):
        return "dotnet-build"
    # Same invocation shape as BUILD_COMMAND_RE: a workspace path containing "-calor-"
    # is not a calor call.
    if CALOR_INVOCATION_RE.search(command):
        return "calor"
    return "other"


def extract_builds(path, max_output=DEFAULT_MAX_OUTPUT):
    events = read_events(path)
    results = {}
    for event in events:
        if event.get("type") != "user":
            continue
        message = event.get("message")
        if not isinstance(message, dict):
            continue
        content = message.get("content")
        if not isinstance(content, list):
            continue
        for block in content:
            if isinstance(block, dict) and block.get("type") == "tool_result":
                tid = block.get("tool_use_id")
                if isinstance(tid, str) and tid not in results:
                    results[tid] = {
                        "text": _tool_result_text(block),
                        "is_error": bool(block.get("is_error", False)),
                    }
    records = []
    ordinal = 0
    for event in events:
        if event.get("type") != "assistant":
            continue
        message = event.get("message")
        if not isinstance(message, dict):
            continue
        content = message.get("content")
        if not isinstance(content, list):
            continue
        for block in content:
            if not isinstance(block, dict) or block.get("type") != "tool_use":
                continue
            ordinal += 1
            if block.get("name") != "Bash":
                continue
            inp = block.get("input") if isinstance(block.get("input"), dict) else {}
            command = inp.get("command")
            if not isinstance(command, str) or not BUILD_COMMAND_RE.search(command):
                continue
            result = results.get(block.get("id"))
            text = result["text"] if result else ""
            truncated = False
            if max_output is not None and max_output >= 0 and len(text) > max_output:
                text = text[:max_output]
                truncated = True
            # exitCode: read from the tool_result when it names one ("Exit code: N"),
            # else 0 for a successful call and 1 for a failed one — most real failing
            # builds carry only `Build FAILED` plus is_error, and null there would read
            # as "no failure" to an analyzer. null now means only "no result recorded".
            exit_code = None
            if result:
                m = EXIT_CODE_RE.search(result["text"])
                if m:
                    exit_code = int(m.group(1))
                else:
                    exit_code = 1 if result["is_error"] else 0
            records.append({
                "index": len(records) + 1,
                "toolCallOrdinal": ordinal,
                "toolUseId": block.get("id"),
                "messageId": message.get("id"),
                "parentToolUseId": event.get("parent_tool_use_id"),
                "command": command,
                "kind": classify_command(command),
                "exitCode": exit_code,
                "isError": (result["is_error"] if result else None),
                "hasResult": result is not None,
                "output": text,
                "outputTruncated": truncated,
            })
    return records


# ---------------------------------------------------------------------------
# build state (#1094)
# ---------------------------------------------------------------------------
BUILD_STATE_FIELDS = ("compilerHash", "optionsHash", "manifestHash",
                      "formatVersion", "compilerSemanticsVersion")


def read_build_state(path):
    out = {field: None for field in BUILD_STATE_FIELDS}
    out["source"] = "missing"
    if not path or not os.path.isfile(path):
        return out
    try:
        with open(path, "r", encoding="utf-8") as fh:
            state = json.load(fh)
    except (OSError, ValueError):
        return out
    if not isinstance(state, dict):
        return out
    for field in BUILD_STATE_FIELDS:
        value = state.get(field)
        out[field] = value if isinstance(value, str) else None
    out["source"] = "file" if out["compilerHash"] else "no-compiler-hash"
    return out


# ---------------------------------------------------------------------------
# pair config admission (gates §1 pin + roadmap §4.1 pre-rows control arm)
# ---------------------------------------------------------------------------
def admit_config(config):
    """Return (admitted: bool, control_arm_kind: str|None, reason: str)."""
    if not isinstance(config, dict):
        return False, None, "arm config is missing or not an object"
    kind = config.get("controlArmKind")
    pins = {k: config.get(k) for k in STRICT_CONFIG}
    # Type check BEFORE the dict comparison: in Python 1 == True and 0 == False, so
    # {"enforceEffects": 1, "permissiveEffects": 0, ...} would otherwise be admitted —
    # a loosening against the jq check this replaced, and `permissiveEffects: 1` would
    # slip a permissive arm past the pre-rows requirement.
    for key, expected in STRICT_CONFIG.items():
        value = pins[key]
        if isinstance(expected, bool) and not isinstance(value, bool):
            return False, None, (
                "%s must be a JSON boolean, got %r (%s)" % (key, value, type(value).__name__))
        if isinstance(expected, str) and not isinstance(value, str):
            return False, None, (
                "%s must be a JSON string, got %r (%s)" % (key, value, type(value).__name__))
    permissive = pins["permissiveEffects"]
    if kind is not None and kind not in ADMITTED_CONTROL_ARM_KINDS:
        return False, None, "unknown controlArmKind %r (admitted: %s)" % (
            kind, ", ".join(ADMITTED_CONTROL_ARM_KINDS))
    if kind == PRE_ROWS_KIND:
        expected = dict(STRICT_CONFIG, permissiveEffects=True)
        if pins != expected:
            return False, None, (
                "controlArmKind 'pre-rows' requires exactly %s; got %s"
                % (_fmt(expected), _fmt(pins)))
        return True, PRE_ROWS_KIND, "pre-rows control arm (roadmap §4.1, additive to gates §1)"
    if permissive is True:
        return False, None, (
            "permissiveEffects true is admitted only together with "
            "controlArmKind \"pre-rows\" (roadmap §4.1); got %s" % _fmt(pins))
    if pins != STRICT_CONFIG:
        return False, None, "calor config violates gates-doc pin: got %s, need %s" % (
            _fmt(pins), _fmt(STRICT_CONFIG))
    return True, None, "strict calor arm (gates §1 pin)"


def _fmt(pins):
    return json.dumps(pins, sort_keys=True)


def resolve_reference(pair_json, pair, entry, fixture):
    """Locate the null-agent reference solution for one arm entry.

    The null path applies a *correct* solution to the workspace and checks that the
    shim, the held-out suite and the metrics all fire — zero API spend, full plumbing.
    Pairs authored before v0.16 ship `reference/<fixture>/`. The PP-W-rows pairs
    (roadmap §4.1, S3 (c)) do not: their per-arm clean solutions are the seeded
    `clean` cells, declared in pair.json as `seeded.clean.<armId>` — the same programs
    the epoch uses as its non-laundering control, which is exactly what the null path
    wants. Resolution order, most explicit first; returns (path-relative-to-pair, source):

      1. `reference/<fixture>/`                      -> "reference"
      2. pair.json `seeded.clean.<arms[key].armId>`  -> "seeded-clean-declared"
      3. `seeded/clean-<suffix of fixture>`          -> "seeded-clean-derived"
      4. nothing                                     -> (None, "none")

    (4) is not an error here: the C# arm and any pair without a reference simply have
    none, and it is the RUNNER that decides a missing reference is fatal for a
    --null-agent run. Returning it as data keeps that decision in one place and lets
    the tests observe every branch.

    Two refusals rather than guesses: a `seeded.clean.<armId>` that pair.json DECLARES
    but that does not exist stops here as
    `seeded-clean-declared-missing:<path>` instead of falling through to (3) — silently
    applying a different program than the pair names is worse than failing — and any
    candidate that is absolute or climbs out of the pair with `..` is rejected outright.
    """
    pair_dir = os.path.dirname(os.path.abspath(pair_json))

    def inside(rel):
        """A pair-relative directory that really is inside the pair. pair.json is data;
        an absolute path or one climbing out with `..` would let it point the null path
        at anything on the machine."""
        if not isinstance(rel, str) or not rel or os.path.isabs(rel):
            return None
        full = os.path.normpath(os.path.join(pair_dir, rel))
        if not full.startswith(pair_dir + os.sep):
            return None
        return full if os.path.isdir(full) else None

    candidate = os.path.join("reference", fixture)
    if inside(candidate):
        return candidate, "reference"

    arm_id = entry.get("armId")
    seeded = pair.get("seeded") if isinstance(pair.get("seeded"), dict) else {}
    clean = seeded.get("clean") if isinstance(seeded.get("clean"), dict) else {}
    declared = clean.get(arm_id) if isinstance(arm_id, str) else None
    if declared is not None:
        # DECLARED but not usable is an error, not a cue to guess: falling through to the
        # derived form would silently apply a different program than pair.json names.
        if inside(declared):
            return declared, "seeded-clean-declared"
        return None, "seeded-clean-declared-missing:%s" % declared

    suffix = fixture.rsplit("-", 1)[-1] if "-" in fixture else None
    if suffix:
        derived = os.path.join("seeded", "clean-" + suffix)
        if inside(derived):
            return derived, "seeded-clean-derived"

    return None, "none"


def resolve_pair_config(pair_json, key, arm=None):
    """Read arms[key] from pair.json and admit it. Returns the JSON dict the
    runner consumes; `admitted` false carries `reason`."""
    arm = arm or ("csharp" if key == "csharp" else "calor")
    try:
        with open(pair_json, "r", encoding="utf-8") as fh:
            pair = json.load(fh)
    except (OSError, ValueError) as exc:
        return {"admitted": False, "reason": "cannot read pair.json: %s" % exc,
                "armConfigKey": key, "arm": arm, "controlArmKind": None,
                "permissiveEffects": None, "fixture": None,
                "reference": None, "referenceSource": "none"}
    arms = pair.get("arms") if isinstance(pair, dict) else None
    entry = arms.get(key) if isinstance(arms, dict) else None
    if not isinstance(entry, dict):
        return {"admitted": False,
                "reason": "pair.json has no arms[%r] entry" % key,
                "armConfigKey": key, "arm": arm, "controlArmKind": None,
                "permissiveEffects": None, "fixture": None,
                "reference": None, "referenceSource": "none"}
    fixture = entry.get("fixture")
    if not isinstance(fixture, str) or not fixture:
        fixture = arm
    reference, reference_source = resolve_reference(pair_json, pair, entry, fixture)
    base = {"armConfigKey": key, "arm": arm, "fixture": fixture,
            "reference": reference, "referenceSource": reference_source,
            "pairId": pair.get("id")}
    if arm != "calor":
        base.update({"admitted": True, "controlArmKind": None,
                     "permissiveEffects": None,
                     "reason": "non-calor arm has no config pin"})
        return base
    admitted, kind, reason = admit_config(entry.get("config"))
    base.update({"admitted": admitted, "controlArmKind": kind,
                 "permissiveEffects": (bool(entry.get("config", {}).get("permissiveEffects"))
                                       if isinstance(entry.get("config"), dict) else None),
                 "reason": reason})
    return base


# ---------------------------------------------------------------------------
# The declared-done held-out run, per TEST (annex A-1.12 leg A)
#
# A-1.12 registers leg A's escape as "at least one of the pair's named
# `effectObservingTests` failing on a workspace that BUILT" — a per-TEST fact
# that `result.json`'s `escapedBugs` count cannot carry. This reads the names
# out of the final `dotnet test` log so ppw-analyze.py never has to guess which
# of a 6-to-11-test suite failed.
# ---------------------------------------------------------------------------
# xUnit's own stream: "[xUnit.net 00:00:00.62]     Class.Test [FAIL]"
_XUNIT_RESULT = re.compile(
    r"^\[xUnit\.net[^\]]*\]\s+(?P<name>[A-Za-z_][A-Za-z0-9_.<>,+]*)\s+\[(?P<state>FAIL|SKIP)\]")
_XUNIT_LINE = re.compile(r"^\[xUnit\.net[^\]]*\]\s?(?P<rest>.*)$")
# VSTest's summary block: "  Failed Class.Test [1 ms]" / "  Passed Class.Test [1 ms]"
_VSTEST_RESULT = re.compile(
    r"^\s*(?:X\s+)?(?P<state>Failed|Passed|Skipped)\s+(?P<name>[A-Za-z_][A-Za-z0-9_.<>,+]*)")
# Microsoft.Testing.Platform: "failed Class.Test (1ms)"
_MTP_RESULT = re.compile(
    r"^\s*(?P<state>failed|passed|skipped)\s+(?P<name>[A-Za-z_][A-Za-z0-9_.<>,+]*)")

# The SILENCE assertion the effect-observing tests make is
# `Assert.Equal(string.Empty, output)`, which xUnit renders as "Strings differ"
# with `Expected: ""`. Some of those tests assert the RETURN VALUE first (W-001's
# Twice_IsSilent_AfterProbe, W-003's Sum2_*), so a silent-but-WRONG implementation
# fails a named effect-observing test having laundered nothing. Requiring the
# silence signature closes that false-escape channel; it is arm-symmetric, so it
# adds noise rather than bias, but noise on a 6-fixture probe is worth removing.
_SILENCE_SIGNATURES = ("Strings differ", 'Expected: ""', "Expected: <empty>")
_VALUE_SIGNATURE = "Values differ"


def _result_lines(text):
    """Yield (state, simple_name, detail_line) — detail_line is None on a result
    line and carries the assertion text on the lines that follow one."""
    current = None
    for line in text.splitlines():
        match = _XUNIT_RESULT.match(line)
        if match:
            current = ("FAIL" if match.group("state") == "FAIL" else "SKIP",
                       match.group("name").split(".")[-1])
            yield current[0], current[1], None
            continue
        match = _VSTEST_RESULT.match(line) or _MTP_RESULT.match(line)
        if match:
            state = match.group("state").lower()
            current = ("FAIL" if state == "failed" else state.upper(),
                       match.group("name").split(".")[-1])
            yield current[0], current[1], None
            continue
        if current is None:
            continue
        inner = _XUNIT_LINE.match(line)
        yield current[0], current[1], (inner.group("rest") if inner else line)


def read_heldout_final(path):
    """{failedTests, silenceFailures, valueOnlyFailures, source}.

    Names are the SIMPLE method names, the spelling `pair.json` uses for
    `effectObservingTests`. `silenceFailures` is the subset whose assertion text
    carries the SILENCE signature — the only failures PP-W-rows' leg A may read
    as an escape. source is "log" when the log was read and "missing" when it is
    absent, which is also what a declared-done state that did not BUILD looks
    like, since run-pair.sh runs the held-out suite only after a successful
    build."""
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except OSError:
        return {"failedTests": [], "silenceFailures": [], "valueOnlyFailures": [],
                "source": "missing"}
    failed, detail = set(), {}
    for state, name, line in _result_lines(text):
        if state != "FAIL":
            continue
        failed.add(name)
        if line:
            detail.setdefault(name, []).append(line)
    silence, value_only = set(), set()
    for name in failed:
        blob = "\n".join(detail.get(name, []))
        if any(sig in blob for sig in _SILENCE_SIGNATURES):
            silence.add(name)
        elif _VALUE_SIGNATURE in blob:
            value_only.add(name)
    return {"failedTests": sorted(failed), "silenceFailures": sorted(silence),
            "valueOnlyFailures": sorted(value_only), "source": "log"}


# ---------------------------------------------------------------------------
# legBPairs (roadmap §3.1 W2 / §4.1)
# ---------------------------------------------------------------------------
def read_leg_b_pairs(pins_json):
    with open(pins_json, "r", encoding="utf-8") as fh:
        pins = json.load(fh)
    if not isinstance(pins, dict):
        raise ValueError("pins.json is not an object")
    block = pins.get("ppW") if isinstance(pins.get("ppW"), dict) else pins
    leg_b = block.get("legBPairs")
    blind = block.get("blindPairs")
    suite = pins.get("suite")
    if not isinstance(leg_b, list) or not leg_b:
        raise ValueError("legBPairs missing or empty in pins.json (it is registered, never defaulted)")
    if any(not isinstance(p, str) or not p for p in leg_b):
        raise ValueError("legBPairs must be a list of non-empty pair ids")
    if len(set(leg_b)) != len(leg_b):
        raise ValueError("legBPairs contains duplicates: %s" % leg_b)
    if isinstance(suite, list):
        outside = [p for p in leg_b if p not in suite]
        if outside:
            raise ValueError("legBPairs names pairs outside the epoch suite: %s" % outside)
    if blind is None:
        blind = []
    if not isinstance(blind, list) or any(not isinstance(p, str) for p in blind):
        raise ValueError("blindPairs must be a list of pair ids")
    excluded = [p for p in (suite or []) if p not in leg_b] if isinstance(suite, list) else []
    return {"legBPairs": list(leg_b), "blindPairs": list(blind),
            "suite": list(suite) if isinstance(suite, list) else None,
            "excludedFromLegB": excluded}


# ---------------------------------------------------------------------------
# self-test
# ---------------------------------------------------------------------------
def _self_test():
    import tempfile
    ok = True

    def check(cond, what):
        nonlocal ok
        if not cond:
            ok = False
            sys.stderr.write("self-test FAIL: %s\n" % what)

    with tempfile.TemporaryDirectory() as tmp:
        transcript = os.path.join(tmp, "transcript.jsonl")
        lines = [
            {"type": "system", "subtype": "init"},
            {"type": "assistant", "message": {"id": "msg_1", "content": [{"type": "text", "text": "hi"}]}},
            {"type": "assistant", "message": {"id": "msg_1", "content": [
                {"type": "tool_use", "id": "toolu_1", "name": "Bash", "input": {"command": "dotnet build"}}]}},
            {"type": "user", "message": {"content": [
                {"type": "tool_result", "tool_use_id": "toolu_1", "content": "Build succeeded.\n"}]}},
            {"type": "assistant", "parent_tool_use_id": "toolu_9",
             "message": {"id": "msg_sub", "content": [{"type": "text", "text": "sub"}]}},
            {"type": "assistant", "message": {"id": "msg_2", "content": [{"type": "text", "text": "done"}]}},
            {"type": "result", "subtype": "success", "num_turns": 7},
        ]
        with open(transcript, "w") as fh:
            for obj in lines:
                fh.write(json.dumps(obj) + "\n")
        t = count_turns(transcript)
        check(t["assistantMessages"] == 2, "turns top-level %r" % t)
        check(t["subagentMessages"] == 1, "turns sub %r" % t)
        check(t["assistantMessagesIncludingSubagents"] == 3, "turns total %r" % t)
        check(t["assistantEvents"] == 4, "assistant events %r" % t)
        b = extract_builds(transcript)
        check(len(b) == 1 and b[0]["kind"] == "dotnet-build" and b[0]["exitCode"] == 0, "builds %r" % b)
        check(count_turns(os.path.join(tmp, "nope.jsonl"))["source"] == "missing", "missing transcript")
        state = os.path.join(tmp, ".calor-build-state.json")
        with open(state, "w") as fh:
            json.dump({"formatVersion": "3.0", "compilerHash": "abc"}, fh)
        check(read_build_state(state)["compilerHash"] == "abc", "build state")
        check(read_build_state(os.path.join(tmp, "none"))["source"] == "missing", "build state missing")
        ho = os.path.join(tmp, ".ho_final.txt")
        with open(ho, "w") as fh:
            fh.write(
                "  Failed HeldOut.EffectTests.Twice_IsSilent_OnFreshBehavior [3 ms]\n"
                "  Error Message:\n"
                "   Assert.Equal() Failure: Strings differ\n"
                '   Expected: ""\n'
                '   Actual:   "beat\\nbeat\\n"\n'
                "  Failed HeldOut.EffectTests.Twice_IsSilent_AfterProbe [1 ms]\n"
                "  Error Message:\n"
                "   Assert.Equal() Failure: Values differ\n"
                "   Expected: 2\n"
                "   Actual:   1\n"
                "  Passed HeldOut.EffectTests.Twice_ReturnsSum [1 ms]\n"
                "Failed! - Failed: 2, Passed: 5, Skipped: 0, Total: 7\n")
        h = read_heldout_final(ho)
        check(h["failedTests"] == ["Twice_IsSilent_AfterProbe", "Twice_IsSilent_OnFreshBehavior"],
              "heldout-final names %r" % h)
        # Only the SILENCE failure is an escape: the value-differ one is a
        # silent-but-wrong implementation, which laundered nothing.
        check(h["silenceFailures"] == ["Twice_IsSilent_OnFreshBehavior"],
              "heldout-final silence %r" % h)
        check(h["valueOnlyFailures"] == ["Twice_IsSilent_AfterProbe"],
              "heldout-final value-only %r" % h)
        check(h["source"] == "log", "heldout-final source %r" % h)
        # xUnit's own stream form, which is what `dotnet test -v q` prints.
        xunit = os.path.join(tmp, ".ho_xunit.txt")
        with open(xunit, "w") as fh:
            fh.write(
                "[xUnit.net 00:00:00.62]     MapDoubler.HeldOut.Tests.Twice_IsSilent [FAIL]\n"
                "[xUnit.net 00:00:00.62]       Assert.Equal() Failure: Strings differ\n"
                '[xUnit.net 00:00:00.62]       Expected: ""\n'
                '[xUnit.net 00:00:00.62]       Actual:   "10\\n"\n'
                "[xUnit.net 00:00:00.63]     MapDoubler.HeldOut.Tests.Map_Doubles [FAIL]\n"
                "[xUnit.net 00:00:00.63]       System.IndexOutOfRangeException\n")
        x = read_heldout_final(xunit)
        check(x["failedTests"] == ["Map_Doubles", "Twice_IsSilent"], "xunit names %r" % x)
        check(x["silenceFailures"] == ["Twice_IsSilent"], "xunit silence %r" % x)
        # A declared-done state that did not BUILD leaves no .ho_final.txt at all.
        check(read_heldout_final(os.path.join(tmp, "nope.txt"))
              == {"failedTests": [], "silenceFailures": [], "valueOnlyFailures": [],
                  "source": "missing"}, "heldout-final missing")
    check(admit_config(STRICT_CONFIG) == (True, None, "strict calor arm (gates §1 pin)"), "strict admitted")
    pre = dict(STRICT_CONFIG, permissiveEffects=True, controlArmKind="pre-rows")
    check(admit_config(pre)[0] and admit_config(pre)[1] == "pre-rows", "pre-rows admitted")
    check(not admit_config(dict(STRICT_CONFIG, permissiveEffects=True))[0], "permissive alone rejected")
    check(not admit_config(dict(STRICT_CONFIG, controlArmKind="pre-rows"))[0], "kind without permissive rejected")
    check(not admit_config(dict(STRICT_CONFIG, contractMode="off"))[0], "other config rejected")
    check(not admit_config(dict(pre, controlArmKind="post-rows"))[0], "unknown kind rejected")
    check(not admit_config(dict(STRICT_CONFIG, enforceEffects=1, permissiveEffects=0))[0],
          "1/0 in place of true/false rejected")
    check(not admit_config(dict(pre, permissiveEffects=1))[0], "permissiveEffects: 1 rejected")
    if ok:
        print("harness-capture.py self-test OK")
        return 0
    return 1


# ---------------------------------------------------------------------------
def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="cmd")
    p = sub.add_parser("turns"); p.add_argument("transcript")
    p = sub.add_parser("builds"); p.add_argument("transcript")
    p.add_argument("--max-output", type=int, default=DEFAULT_MAX_OUTPUT)
    p = sub.add_parser("build-state"); p.add_argument("path")
    p = sub.add_parser("pair-config"); p.add_argument("pair_json"); p.add_argument("key")
    p.add_argument("--arm", default=None, choices=(None, "calor", "csharp"))
    p = sub.add_parser("leg-b-pairs"); p.add_argument("pins_json")
    p = sub.add_parser("heldout-final"); p.add_argument("path")
    sub.add_parser("self-test")
    args = parser.parse_args(argv)
    if args.cmd == "turns":
        json.dump(count_turns(args.transcript), sys.stdout, sort_keys=True)
        sys.stdout.write("\n")
        return 0
    if args.cmd == "builds":
        for record in extract_builds(args.transcript, args.max_output):
            sys.stdout.write(json.dumps(record, sort_keys=True) + "\n")
        return 0
    if args.cmd == "build-state":
        json.dump(read_build_state(args.path), sys.stdout, sort_keys=True)
        sys.stdout.write("\n")
        return 0
    if args.cmd == "heldout-final":
        json.dump(read_heldout_final(args.path), sys.stdout, sort_keys=True)
        sys.stdout.write("\n")
        return 0
    if args.cmd == "pair-config":
        result = resolve_pair_config(args.pair_json, args.key, args.arm)
        json.dump(result, sys.stdout, sort_keys=True)
        sys.stdout.write("\n")
        return 0 if result["admitted"] else PAIR_CONFIG_REJECT_EXIT
    if args.cmd == "leg-b-pairs":
        try:
            result = read_leg_b_pairs(args.pins_json)
        except (OSError, ValueError) as exc:
            json.dump({"error": str(exc)}, sys.stdout)
            sys.stdout.write("\n")
            return PAIR_CONFIG_REJECT_EXIT
        json.dump(result, sys.stdout, sort_keys=True)
        sys.stdout.write("\n")
        return 0
    if args.cmd == "self-test":
        return _self_test()
    parser.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
