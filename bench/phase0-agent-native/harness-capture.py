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
        {assistantMessages, assistantMessagesTopLevel, assistantMessagesSubagent,
         assistantEvents, assistantEventsWithoutId, resultEvents, events,
         eventTypes, source}
        `assistantMessages` is the field A-1.12 registers: the number of DISTINCT
        assistant `message.id` values in the transcript. stream-json emits one
        `assistant` event per content block, so events are NOT turns; the
        message id is. Subagent messages (present only because the runner
        passes --forward-subagent-text) carry `parent_tool_use_id` and are
        counted in the total AND reported separately, so the total matches the
        corrected-token rule (A-1.9.1 sums subagent tokens too).
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
        `after/` for the strict arm, §4.1).

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

BUILD_COMMAND_RE = re.compile(r"\bdotnet\s+(build|test)\b|\bcalor\b")
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
            "assistantMessagesTopLevel": 0,
            "assistantMessagesSubagent": 0,
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
        "assistantMessages": len(top) + len(sub),
        "assistantMessagesTopLevel": len(top),
        "assistantMessagesSubagent": len(sub),
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


def classify_command(command):
    if re.search(r"\bdotnet\s+test\b", command):
        return "dotnet-test"
    if re.search(r"\bdotnet\s+build\b", command):
        return "dotnet-build"
    if re.search(r"\bcalor\b", command):
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
            exit_code = None
            if result:
                m = EXIT_CODE_RE.search(result["text"])
                if m:
                    exit_code = int(m.group(1))
                elif not result["is_error"]:
                    exit_code = 0
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
                "permissiveEffects": None, "fixture": None}
    arms = pair.get("arms") if isinstance(pair, dict) else None
    entry = arms.get(key) if isinstance(arms, dict) else None
    if not isinstance(entry, dict):
        return {"admitted": False,
                "reason": "pair.json has no arms[%r] entry" % key,
                "armConfigKey": key, "arm": arm, "controlArmKind": None,
                "permissiveEffects": None, "fixture": None}
    fixture = entry.get("fixture")
    if not isinstance(fixture, str) or not fixture:
        fixture = arm
    base = {"armConfigKey": key, "arm": arm, "fixture": fixture,
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
        check(t["assistantMessages"] == 3, "turns total %r" % t)
        check(t["assistantMessagesTopLevel"] == 2, "turns top %r" % t)
        check(t["assistantMessagesSubagent"] == 1, "turns sub %r" % t)
        check(t["assistantEvents"] == 4, "assistant events %r" % t)
        b = extract_builds(transcript)
        check(len(b) == 1 and b[0]["kind"] == "dotnet-build" and b[0]["exitCode"] == 0, "builds %r" % b)
        check(count_turns(os.path.join(tmp, "nope.jsonl"))["source"] == "missing", "missing transcript")
        state = os.path.join(tmp, ".calor-build-state.json")
        with open(state, "w") as fh:
            json.dump({"formatVersion": "3.0", "compilerHash": "abc"}, fh)
        check(read_build_state(state)["compilerHash"] == "abc", "build state")
        check(read_build_state(os.path.join(tmp, "none"))["source"] == "missing", "build state missing")
    check(admit_config(STRICT_CONFIG) == (True, None, "strict calor arm (gates §1 pin)"), "strict admitted")
    pre = dict(STRICT_CONFIG, permissiveEffects=True, controlArmKind="pre-rows")
    check(admit_config(pre)[0] and admit_config(pre)[1] == "pre-rows", "pre-rows admitted")
    check(not admit_config(dict(STRICT_CONFIG, permissiveEffects=True))[0], "permissive alone rejected")
    check(not admit_config(dict(STRICT_CONFIG, controlArmKind="pre-rows"))[0], "kind without permissive rejected")
    check(not admit_config(dict(STRICT_CONFIG, contractMode="off"))[0], "other config rejected")
    check(not admit_config(dict(pre, controlArmKind="post-rows"))[0], "unknown kind rejected")
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
