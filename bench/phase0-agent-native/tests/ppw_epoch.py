#!/usr/bin/env python3
"""Materialize a synthetic PP-W-rows epoch from a compact case spec.

No epoch exists yet — `w-rows-001` is a paid run the 0.16.0 release PR's author
makes. `ppw-analyze.py` adjudicates a ~$135 pre-registered experiment, so every
registered rule needs a fixture that exercises it BEFORE the money is spent.
The committed fixtures are the case specs under `tests/fixtures/ppw/cases/`;
this module expands one into a real epoch directory (pins.json, per-run
result.json / transcript.jsonl / agent.json / final-src, and a starter-compile
record) in a temporary tree.

A case spec is JSON:

    {
      "name": "hit",
      "why": "what this case pins",
      "expect": {"verdict": "HIT", "route": null},
      "runsPerArm": 4,
      "pins": {...},                # merged into the default pins.json
      "starterCompiles": "frozen"   # or {"break": [["<pair-dir>", "A"|"B"], ...]}
                                    # or "absent"
      "defaultCell": {...},         # cell defaults for every pair x arm
      "cells": {"<pair-dir>": {"A": {...}, "B": {...}}}
    }

Cell fields (all optional):
    escapes        how many runs escape (>= 1 effectObservingTest failing with
                   the SILENCE signature on a workspace that BUILT)
    namedOnlyFailures
                   how many runs fail a named effect-observing test WITHOUT the
                   silence signature — a silent-but-wrong solution, published
                   and never scored
    notBuilt       how many runs whose declared-done state does NOT build; they
                   contribute no escape and stay in the denominator
    invalid        how many runs are marked invalid
    noTranscript   how many runs archive no transcript.jsonl
    censored       how many runs are marked censored
    tokens         a list of per-run corrected output tokens (cycled), or a
                   single number
    escapeSource   a source to archive as the escaping runs' final-src, resolved
                   against tests/fixtures/ppw/sources/ (`*.calr.txt`) or the
                   pair's own seeded/ directory; drives the escape classifier
    logsOnly       omit `finalBuild` / `heldoutFinal` from result.json, leaving
                   only the archived `.ho_final.txt` / `.src_final.txt` — the
                   shape of a run collected before those fields existed
    canary         the armCanary verdict to archive (default "permissive-ok" on
                   arm A, "strict-ok" on arm B — `run-pair.sh`'s two verdicts)
    compilerHash   override the arm's compilerHash (validity condition (3))
    optionsHash    override the arm's buildState.optionsHash — what witnesses the
                   permissive policy, since a control arm run STRICT leaves
                   compilerHash unchanged and moves only this
    turns          top-level assistant messages written into the transcript AND
                   recorded (they must agree: the analyzer recomputes)
    subagentTurns  forwarded subagent messages, counted separately
    shapeRealized  force the shape-realized indicator on/off (default: on)

Python 3.9 compatible; standard library only.
"""
import json
import os
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
PAIRS = os.path.join(BENCH, "pairs")
FIXTURES = os.path.join(HERE, "fixtures", "ppw")
CASES = os.path.join(FIXTURES, "cases")
SOURCES = os.path.join(FIXTURES, "sources")
SEEDED_COMPILES = os.path.join(PAIRS, "ppw-seeded-compiles.json")

PAIR_DIRS = ["W-001-middleware-stage", "W-002-map-and-report", "W-003-match-fallback",
             "W-004-counter-peek", "W-005-pipeline-trace", "W-006-map-doubler"]
ARM_A_LABEL = "calor+v0.14.3-pre-rows"
ARM_B_LABEL = "calor+v0.15.0"
ARM_A_COMMIT = "283ec9f9964ddd5b21da15b646a0dd77d53de99e"
ARM_B_COMMIT = "3bb2601e0cbd93fc25fdaaf2a0ea5183b8a2dd6a"


def load_case(name):
    with open(os.path.join(CASES, name + ".json"), encoding="utf-8") as fh:
        return json.load(fh)


def case_names():
    return sorted(f[:-5] for f in os.listdir(CASES) if f.endswith(".json"))


def _pair(pair_dir):
    with open(os.path.join(PAIRS, pair_dir, "pair.json"), encoding="utf-8") as fh:
        return json.load(fh)


def _clean_source(pair_dir, arm_letter):
    """The pair's frozen `clean` seed — the shape the indicator must match."""
    pair = _pair(pair_dir)
    rel = ((pair.get("seeded") or {}).get("clean") or {}).get(arm_letter)
    base = os.path.join(PAIRS, pair_dir, rel)
    for name in sorted(os.listdir(base)):
        if name.endswith(".calr"):
            with open(os.path.join(base, name), encoding="utf-8") as fh:
                return name, fh.read()
    raise AssertionError("no .calr under " + base)


def _starter_source(pair_dir, arm_key):
    pair = _pair(pair_dir)
    rel = pair["arms"][arm_key]["fixture"]
    base = os.path.join(PAIRS, pair_dir, rel)
    for name in sorted(os.listdir(base)):
        if name.endswith(".calr"):
            with open(os.path.join(base, name), encoding="utf-8") as fh:
                return name, fh.read()
    raise AssertionError("no .calr under " + base)


def _named_source(pair_dir, spec):
    """`escapeSource`: a file under tests/fixtures/ppw/sources/, or a seeded
    role directory of the pair itself (so the committed arm-B escape seeds are
    the fixtures, not a hand-written imitation of them)."""
    direct = os.path.join(SOURCES, spec)
    if os.path.isfile(direct):
        # `<name>.calr.txt` on disk (a committed *.calr under bench/ would enter the
        # whole-corpus counts the effect-rows ledgers pin); `<name>.calr` in the epoch,
        # which is what run-pair.sh's archive_final_src writes.
        with open(direct, encoding="utf-8") as fh:
            name = os.path.basename(direct)
            return (name[:-4] if name.endswith(".calr.txt") else name), fh.read()
    base = os.path.join(PAIRS, pair_dir, "seeded", spec)
    for name in sorted(os.listdir(base)):
        if name.endswith(".calr"):
            with open(os.path.join(base, name), encoding="utf-8") as fh:
                return name, fh.read()
    raise AssertionError("no escape source %r for %s" % (spec, pair_dir))


def _starter_compiles(spec, out_path):
    with open(SEEDED_COMPILES, encoding="utf-8") as fh:
        data = json.load(fh)
    broken = set()
    if isinstance(spec, dict):
        broken = {(p, a) for p, a in spec.get("break", [])}
    compiles = []
    for cell in data["compiles"]:
        if cell.get("role") != "starter":
            continue
        cell = json.loads(json.dumps(cell))
        if (cell["pair"], cell["arm"]) in broken:
            # A route-(a) failure with the starter BYTES intact: the frozen
            # multiset is not reproduced, but nothing was edited, so it is NOT
            # auto-attributed to this workstream (gates §0.3 forbids guessing).
            cell["diagnostics"] = cell.get("diagnostics", []) + [
                {"line": 99, "column": 1, "code": "Calor0410", "severity": "error",
                 "text": "injected route-(a) divergence"}]
            cell["exitCode"] = 1
        compiles.append(cell)
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump({"schemaVersion": 1, "compiles": compiles}, fh, indent=1)


def build(case, root):
    """Materialize `case` under `root`; returns the epoch directory."""
    epoch_id = (case.get("pins") or {}).get("epochId", "w-rows-001")
    epoch = os.path.join(root, epoch_id)
    os.makedirs(epoch, exist_ok=True)
    arm_a_root = os.path.join(root, "armA")
    arm_b_root = os.path.join(root, "armB")

    pins = {
        "epochId": epoch_id,
        "kind": "pp-w-rows",
        "mode": "live",
        "modelPin": "claude-opus-4-8",
        "harnessCommit": "a1230e2aa1230e2aa1230e2aa1230e2aa1230e2a",
        "runsPerArm": case.get("runsPerArm", 4),
        "suite": list(PAIR_DIRS),
        "armA": {"label": ARM_A_LABEL, "role": "control", "commit": ARM_A_COMMIT,
                 "repoRoot": arm_a_root, "calorTasksSha": "aaaa1111", "editMechanism": "raw",
                 "armConfig": "calor-pre-rows"},
        "armB": {"label": ARM_B_LABEL, "role": "treatment", "commit": ARM_B_COMMIT,
                 "repoRoot": arm_b_root, "calorTasksSha": "bbbb2222", "editMechanism": "raw",
                 "armConfig": "calor"},
        "ppW": {
            "gate": "PP-W-rows (roadmap v0.16 §4.1; annex A-1.12)",
            "pairs": list(PAIR_DIRS),
            "legBPairs": ["W-001-middleware-stage", "W-002-map-and-report",
                          "W-003-match-fallback", "W-004-counter-peek", "W-006-map-doubler"],
            "blindPairs": ["W-001-middleware-stage", "W-004-counter-peek", "W-006-map-doubler"],
        },
    }
    for key, value in (case.get("pins") or {}).items():
        if key in ("armA", "armB", "ppW") and isinstance(value, dict):
            if value is None:
                pins[key] = None
            else:
                pins[key] = dict(pins[key] or {}, **value)
        else:
            pins[key] = value
    if (case.get("pins") or {}).get("ppW", "unset") is None:
        pins["ppW"] = None
    with open(os.path.join(epoch, "pins.json"), "w", encoding="utf-8") as fh:
        json.dump(pins, fh, indent=1)

    sc = case.get("starterCompiles", "frozen")
    if sc != "absent":
        _starter_compiles(sc, os.path.join(epoch, "ppw-starter-compiles.json"))

    runs_per_arm = case.get("runsPerArm", 4)
    default_cell = case.get("defaultCell") or {}
    for pair_dir in PAIR_DIRS:
        pair = _pair(pair_dir)
        observing = pair["tests"]["effectObservingTests"]
        for arm_key, arm_letter, label, repo_root, compiler_hash in (
                ("calor-pre-rows", "a", ARM_A_LABEL, arm_a_root, "hash-arm-a"),
                ("calor", "b", ARM_B_LABEL, arm_b_root, "hash-arm-b")):
            spec = dict(default_cell)
            spec.update(((case.get("cells") or {}).get(pair_dir) or {})
                        .get(arm_letter.upper(), {}))
            _write_cell(epoch, pair_dir, label, repo_root, compiler_hash, arm_key,
                        arm_letter, observing, spec, runs_per_arm)
    return epoch


def _write_cell(epoch, pair_dir, label, repo_root, compiler_hash, arm_key, arm_letter,
                observing, spec, runs_per_arm):
    escapes = spec.get("escapes", 0)
    not_built = spec.get("notBuilt", 0)
    invalid = spec.get("invalid", 0)
    no_transcript = spec.get("noTranscript", 0)
    censored = spec.get("censored", 0)
    tokens = spec.get("tokens", 1000)
    if not isinstance(tokens, list):
        tokens = [tokens]
    clean_name, clean_text = _clean_source(pair_dir, arm_letter)
    escape_name, escape_text = (clean_name, clean_text)
    if spec.get("escapeSource"):
        escape_name, escape_text = _named_source(pair_dir, spec["escapeSource"])
    starter_name, starter_text = _starter_source(pair_dir, arm_key)

    for index in range(runs_per_arm):
        run = index + 1
        run_dir = os.path.join(epoch, pair_dir, label, "run-%d" % run)
        os.makedirs(run_dir, exist_ok=True)
        is_invalid = index < invalid
        is_no_transcript = invalid <= index < invalid + no_transcript
        rest = index - invalid - no_transcript
        is_not_built = 0 <= rest < not_built
        is_escape = (not is_invalid and not is_not_built
                     and 0 <= rest - not_built < escapes)

        # The transcript must agree with the recorded turn count: ppw-analyze.py
        # RECOMPUTES turns.assistantMessages from it (validity condition (2)).
        turns = spec.get("turns", 7)
        subagent = spec.get("subagentTurns", 0)
        if not is_no_transcript:
            with open(os.path.join(run_dir, "transcript.jsonl"), "w", encoding="utf-8") as fh:
                for turn in range(turns):
                    fh.write(json.dumps({"type": "assistant",
                                         "message": {"id": "msg_%d_%d" % (run, turn),
                                                     "content": []}}) + "\n")
                for turn in range(subagent):
                    fh.write(json.dumps({"type": "assistant",
                                         "parent_tool_use_id": "toolu_%d" % turn,
                                         "message": {"id": "sub_%d_%d" % (run, turn),
                                                     "content": []}}) + "\n")
        with open(os.path.join(run_dir, "agent.json"), "w", encoding="utf-8") as fh:
            json.dump({"type": "result", "num_turns": 3, "total_cost_usd": 1.0,
                       "usage": {"input_tokens": 10, "output_tokens": 10},
                       "modelUsage": {"claude-opus-4-8": {
                           "inputTokens": 100,
                           "outputTokens": tokens[index % len(tokens)]}}}, fh)

        src = os.path.join(run_dir, "final-src")
        os.makedirs(src, exist_ok=True)
        if spec.get("shapeRealized") is False:
            name, text = starter_name, starter_text
        elif is_escape:
            name, text = escape_name, escape_text
        else:
            name, text = clean_name, clean_text
        with open(os.path.join(src, name), "w", encoding="utf-8") as fh:
            fh.write(text)

        # An escape fails a named effect-observing test WITH the silence signature.
        # `namedOnly` fails one WITHOUT it — a silent-but-wrong solution, which
        # laundered nothing and must not be scored as an escape.
        failed, silence = [], []
        if is_escape:
            failed, silence = list(observing[:1]), list(observing[:1])
        elif spec.get("namedOnlyFailures", 0) > rest - not_built - escapes >= 0:
            failed = list(observing[:1])
        if is_not_built:
            # gates §2 would score a non-compiling final state as ALL tests
            # failing; A-1.12 replaces that, and the harness archives no
            # .ho_final.txt at all because the build came first.
            failed, silence = [], []

        # The archived declared-done logs run-pair.sh writes. A run whose
        # result.json predates the `heldoutFinal` field is read from these, so
        # the fallback path has a fixture too.
        if spec.get("logsOnly"):
            record_fields = False
        else:
            record_fields = True
        if not is_invalid:
            with open(os.path.join(run_dir, ".src_final.txt"), "w", encoding="utf-8") as fh:
                fh.write("  Determining projects to restore...\n"
                         + ("  Src.calr(9,5): error Calor0410: laundered\n" if is_not_built
                            else "  Src -> /tmp/src/bin/Debug/net10.0/Src.dll\n"))
            if not is_not_built:
                with open(os.path.join(run_dir, ".ho_final.txt"), "w", encoding="utf-8") as fh:
                    for test in observing:
                        if test in silence:
                            fh.write("  Failed HeldOut.Tests.%s [3 ms]\n" % test)
                            fh.write("  Error Message:\n")
                            fh.write("   Assert.Equal() Failure: Strings differ\n")
                            fh.write('   Expected: ""\n')
                            fh.write('   Actual:   "beat\\n"\n')
                        elif test in failed:
                            fh.write("  Failed HeldOut.Tests.%s [2 ms]\n" % test)
                            fh.write("  Error Message:\n")
                            fh.write("   Assert.Equal() Failure: Values differ\n")
                            fh.write("   Expected: 2\n   Actual:   1\n")
                        else:
                            fh.write("  Passed HeldOut.Tests.%s [1 ms]\n" % test)

        record = {
            "pair": pair_dir, "arm": label, "run": run,
            "taskSuccess": not failed,
            "escapedBugs": len(observing) if is_not_built else len(failed),
            "heldoutPassed": 0 if is_not_built else len(observing) - len(failed),
            "iterations": 3, "iterationsToGreen": 2,
            "censored": index < censored or is_invalid,
            "invalid": is_invalid,
            "finalBuild": ({"ok": None if is_invalid else not is_not_built,
                            "log": ".src_final.txt"} if record_fields else None),
            "heldoutFinal": ({"failedTests": failed, "silenceFailures": silence,
                              "valueOnlyFailures": sorted(set(failed) - set(silence)),
                              "source": "missing" if is_not_built else "log"}
                             if record_fields else None),
            # `run-pair.sh`'s two verdicts, one per arm and deliberately different
            # strings: the control arm proves it honours <CalorPermissiveEffects>,
            # the treatment arm proves it rejects the same laundering program.
            "armCanary": spec.get("canary",
                                  "permissive-ok" if arm_letter == "a" else "strict-ok"),
            "armRepoRoot": repo_root,
            "editMechanism": "raw",
            "compilerHash": None if is_invalid else spec.get("compilerHash", compiler_hash),
            # #1094 + A-1.12 validity (3): compilerHash witnesses WHICH compiler built the
            # agent's code; optionsHash witnesses WHICH POLICY it ran under. A control arm
            # built from the registered commit but run STRICT moves only the latter.
            "buildState": {
                "compilerHash": None if is_invalid else spec.get("compilerHash", compiler_hash),
                "optionsHash": None if is_invalid else spec.get(
                    "optionsHash",
                    "options-permissive" if arm_letter == "a" else "options-strict"),
                "source": "invalid" if is_invalid else "workspace"},
            "armConfigKey": arm_key,
            "controlArmKind": "pre-rows" if arm_letter == "a" else None,
            "permissiveEffects": arm_letter == "a",
            "turns": {"assistantMessages": turns, "subagentMessages": subagent,
                      "assistantMessagesIncludingSubagents": turns + subagent,
                      "numTurns": turns + subagent, "source": "transcript.jsonl"},
            "tokens": {"input": 100, "output": tokens[index % len(tokens)]},
            "nullAgent": False,
        }
        record.update(spec.get("resultOverrides") or {})
        with open(os.path.join(run_dir, "result.json"), "w", encoding="utf-8") as fh:
            json.dump(record, fh, indent=1)


def scratch_pairs(root, mutate=None):
    """A copy of the six pair directories a mutation lane can edit."""
    target = os.path.join(root, "pairs")
    if os.path.exists(target):
        shutil.rmtree(target)
    os.makedirs(target)
    shutil.copy2(SEEDED_COMPILES, os.path.join(target, "ppw-seeded-compiles.json"))
    for pair_dir in PAIR_DIRS:
        shutil.copytree(os.path.join(PAIRS, pair_dir), os.path.join(target, pair_dir))
    if mutate:
        mutate(target)
    return target
