#!/usr/bin/env python3
"""Per-turn capture in the agent harness — roadmap v0.16 §3.1 W1, gate 8, #1094.

Pins, each named after the roadmap claim it observes:

  * `turns.assistantMessages` = DISTINCT assistant `message.id` (stream-json
    emits one event per content block; subagent turns only with
    --forward-subagent-text) — never "equals num_turns"          (§2.1 S1)
  * a run without `transcript.jsonl` is INVALID, in both runners  (W1 pin, gate 8)
  * the agent's own `dotnet build` / `dotnet test` / `calor` calls and their
    output are archived per run as agent-builds.jsonl              (§0.2 / W1)
  * `compilerHash` from obj/calor/.calor-build-state.json is surfaced  (#1094)
  * `pair.json` admits `permissiveEffects: true` ONLY with
    `controlArmKind: "pre-rows"`; every other config still exits 3 (§4.1 / W1)
  * the template carries <CalorPermissiveEffects> following the arm config,
    and the pre-rows arm is refused before spend when the arm's Calor.Tasks
    build does not honour it                                        (§4.1)
  * `legBPairs` is read from the epoch's pins.json, never defaulted (§3.1 W2)
  * `run-ppw-epoch.sh` fails loudly listing every missing W-00x pair (§3.1 W1)
  * `ppe1-margin-derivation.py` defaults reproduce the committed A-1.11 output
    byte for byte; the extended grid / population flags never touch the RNG
    sequence                                                         (§2.3(b))

Run:  python3 -m unittest discover -s bench/phase0-agent-native/tests
"""

import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(BENCH))
HELPER = os.path.join(BENCH, "harness-capture.py")
RUN_PAIR = os.path.join(BENCH, "run-pair.sh")
RUN_BUNDLE = os.path.join(BENCH, "run-bundle.sh")
RUN_PPW = os.path.join(BENCH, "run-ppw-epoch.sh")
RUN_M5 = os.path.join(BENCH, "run-m5-epoch.sh")
MARGIN = os.path.join(BENCH, "ppe1-margin-derivation.py")
MARGIN_TXT = os.path.join(BENCH, "ppe1-margin-derivation.txt")
PPW_TXT = os.path.join(BENCH, "ppw-margin-derivation.txt")
TEMPLATE = os.path.join(BENCH, "templates", "calor-arm", "CalorArm.csproj.template")
CANARY = os.path.join(BENCH, "templates", "calor-arm", "permissive-canary.calr")
FIX = os.path.join(HERE, "fixtures", "harness-capture")
TOKEN_FIX = os.path.join(HERE, "fixtures", "token-usage")
PAIRS = os.path.join(BENCH, "pairs")
N1_001 = os.path.join(PAIRS, "N1-001-string-utils")

RELEASE_CLI = os.path.join(REPO, "src", "Calor.Compiler", "bin", "Release", "net10.0", "calor.dll")
RELEASE_TASKS = os.path.join(REPO, "src", "Calor.Tasks", "bin", "Release", "net10.0", "Calor.Tasks.dll")
HAVE_DOTNET = shutil.which("dotnet") is not None
HAVE_JQ = shutil.which("jq") is not None
HAVE_PRODUCT = HAVE_DOTNET and os.path.isfile(RELEASE_CLI) and os.path.isfile(RELEASE_TASKS)


def _load_helper():
    spec = importlib.util.spec_from_file_location("harness_capture", HELPER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


hc = _load_helper()

STRICT = {"enforceEffects": True, "permissiveEffects": False, "contractMode": "debug", "z3Required": True}
PRE_ROWS = dict(STRICT, permissiveEffects=True, controlArmKind="pre-rows")


def fixture(name):
    return os.path.join(FIX, name)


def _read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def _write(path, data):
    with open(path, "wb") as fh:
        fh.write(data)


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def write_pair_json(path, calor_config=None, extra_arms=None, fixture_name=None):
    arms = {"csharp": {"fixture": "csharp", "toolkit": "full"}}
    if calor_config is not None:
        entry = {"config": calor_config}
        if fixture_name:
            entry["fixture"] = fixture_name
        arms["calor"] = entry
    if extra_arms:
        arms.update(extra_arms)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump({"id": "T-000-synthetic", "category": "N1", "arms": arms,
                   "iterationBudget": 10, "timeoutSeconds": 600}, fh)


# ---------------------------------------------------------------------------
class AssistantMessagesAreDistinctMessageIds(unittest.TestCase):
    """§2.1 S1 as A-1.12 registers it, following #1117: turns.assistantMessages =
    distinct TOP-LEVEL assistant message.id (parent_tool_use_id null), with the
    subagent count reported separately so the registered field cannot be inflated by
    whether --forward-subagent-text was passed."""

    def test_multi_block_messages_count_once(self):
        t = hc.count_turns(fixture("multi-block-run.transcript.jsonl"))
        # msg_A spans three events (thinking, text, tool_use); msg_D two.
        self.assertEqual(t["assistantEvents"], 10)
        self.assertEqual(t["assistantMessages"], 7)
        self.assertEqual(t["subagentMessages"], 0)
        self.assertEqual(t["assistantMessagesIncludingSubagents"], 7)
        self.assertEqual(t["resultEvents"], 1)
        self.assertEqual(t["source"], "transcript")

    def test_the_field_is_not_num_turns(self):
        # The result envelope says num_turns 16 for a transcript with 7
        # assistant messages: the two are different quantities, archived
        # side by side (turns.numTurns) and never conflated.
        t = hc.count_turns(fixture("multi-block-run.transcript.jsonl"))
        events = hc.read_events(fixture("multi-block-run.transcript.jsonl"))
        num_turns = [e for e in events if e.get("type") == "result"][0]["num_turns"]
        self.assertEqual(num_turns, 16)
        self.assertNotEqual(t["assistantMessages"], num_turns)

    def test_subagent_messages_are_reported_separately_and_excluded_from_the_field(self):
        # #1117's definition, registered by A-1.12: the field counts top-level ids only.
        # Passing or not passing --forward-subagent-text must not move it.
        t = hc.count_turns(fixture("subagent-run.transcript.jsonl"))
        self.assertEqual(t["assistantMessages"], 3)            # top-level only
        self.assertEqual(t["subagentMessages"], 2)
        self.assertEqual(t["assistantMessagesIncludingSubagents"], 5)
        self.assertEqual(t["assistantEvents"], 7)

    def test_the_field_is_invariant_to_forward_subagent_text(self):
        # The same run without the flag: the subagent events simply are not there.
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "no-forward.jsonl")
            kept = [e for e in hc.read_events(fixture("subagent-run.transcript.jsonl"))
                    if not e.get("parent_tool_use_id")]
            with open(path, "w") as fh:
                for event in kept:
                    fh.write(json.dumps(event) + "\n")
            without = hc.count_turns(path)
        with_flag = hc.count_turns(fixture("subagent-run.transcript.jsonl"))
        self.assertEqual(without["assistantMessages"], with_flag["assistantMessages"])
        self.assertEqual(without["subagentMessages"], 0)

    def test_empty_and_missing_transcripts_read_as_missing(self):
        for path in (fixture("empty.transcript.jsonl"), fixture("does-not-exist.jsonl")):
            t = hc.count_turns(path)
            self.assertEqual(t["source"], "missing")
            self.assertEqual(t["assistantMessages"], 0)

    def test_malformed_lines_and_idless_events_do_not_crash(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "t.jsonl")
            with open(path, "w") as fh:
                fh.write("not json\n")
                fh.write(json.dumps(["a", "list"]) + "\n")
                fh.write(json.dumps({"type": "assistant", "message": {"content": []}}) + "\n")
                fh.write(json.dumps({"type": "assistant", "message": {"id": "m1", "content": []}}) + "\n")
                fh.write(json.dumps({"type": "assistant", "parent_tool_use_id": "x",
                                     "message": {"id": "m1", "content": []}}) + "\n")
            t = hc.count_turns(path)
            self.assertEqual(t["assistantMessages"], 1)      # m1 once, not twice
            self.assertEqual(t["subagentMessages"], 0)
            self.assertEqual(t["assistantEventsWithoutId"], 1)
            self.assertEqual(t["events"], 3)

    def test_cli_turns_matches_module(self):
        out = run([sys.executable, HELPER, "turns", fixture("subagent-run.transcript.jsonl")])
        self.assertEqual(out.returncode, 0, out.stderr)
        self.assertEqual(json.loads(out.stdout)["assistantMessages"], 3)
        self.assertEqual(json.loads(out.stdout)["subagentMessages"], 2)


# ---------------------------------------------------------------------------
class RunWithoutTranscriptIsInvalid(unittest.TestCase):
    """W1 pin / gate 8, in both runners' detect_invalid_run."""

    def _ws_out(self, tmp, transcript=None, agent="normal-run.agent.json"):
        shutil.copy(os.path.join(TOKEN_FIX, agent), os.path.join(tmp, "agent.json"))
        with open(os.path.join(tmp, "journal.jsonl"), "w") as fh:
            fh.write('{"schema":"loop-telemetry/2","edited":true}\n')
        if transcript is not None:
            with open(os.path.join(tmp, "transcript.jsonl"), "w") as fh:
                fh.write(transcript)
        return tmp

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_pair_missing_transcript_is_invalid(self):
        with tempfile.TemporaryDirectory() as tmp:
            self._ws_out(tmp)
            out = run(["bash", RUN_PAIR, "--detect-invalid", tmp, "0"])
            self.assertEqual(out.returncode, 0, out.stdout + out.stderr)
            self.assertIn("transcript.jsonl missing or empty", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_pair_empty_transcript_is_invalid(self):
        with tempfile.TemporaryDirectory() as tmp:
            self._ws_out(tmp, transcript="")
            out = run(["bash", RUN_PAIR, "--detect-invalid", tmp, "0"])
            self.assertEqual(out.returncode, 0)
            self.assertIn("transcript.jsonl missing or empty", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_pair_with_transcript_is_valid(self):
        with tempfile.TemporaryDirectory() as tmp:
            self._ws_out(tmp, transcript='{"type":"result"}\n')
            out = run(["bash", RUN_PAIR, "--detect-invalid", tmp, "0"])
            self.assertEqual(out.returncode, 1, out.stdout + out.stderr)
            self.assertIn("VALID", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_marker_reason_keeps_precedence_over_the_transcript_pin(self):
        with tempfile.TemporaryDirectory() as tmp:
            with open(os.path.join(tmp, "agent.json"), "w") as fh:
                json.dump({"type": "result", "result": "You've hit your rate limit", "usage": {}}, fh)
            out = run(["bash", RUN_PAIR, "--detect-invalid", tmp, "0"])
            self.assertEqual(out.returncode, 0)
            self.assertIn("error marker", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_bundle_detect_invalid_carries_the_same_pin(self):
        # run-bundle.sh has no test entrypoint; lift its detect_invalid_run
        # verbatim and call it.
        src = _read(RUN_BUNDLE)
        m = re.search(r"^detect_invalid_run\(\) \{.*?^\}", src, re.S | re.M)
        self.assertIsNotNone(m, "detect_invalid_run not found in run-bundle.sh")
        fn = 'INVALID_MARKERS=("hit your session limit" "rate limit" "overloaded" "api error")\n' + m.group(0)
        with tempfile.TemporaryDirectory() as tmp:
            self._ws_out(tmp)
            script = fn + '\nif reason="$(detect_invalid_run "$1" 0)"; then echo "INVALID: $reason"; exit 0; fi; echo VALID; exit 1\n'
            out = run(["bash", "-c", script, "_", tmp])
            self.assertEqual(out.returncode, 0, out.stdout + out.stderr)
            self.assertIn("transcript.jsonl missing or empty", out.stdout)
            with open(os.path.join(tmp, "transcript.jsonl"), "w") as fh:
                fh.write('{"type":"result"}\n')
            out = run(["bash", "-c", script, "_", tmp])
            self.assertEqual(out.returncode, 1, out.stdout + out.stderr)

    def test_runners_stream_json_into_transcript_and_filter_the_result(self):
        # The capture shape §2.1 names, present in both live invocations of
        # both runners; agent.json is the filtered result event, not the stream.
        for runner in (RUN_PAIR, RUN_BUNDLE):
            src = _read(runner)
            self.assertNotIn("--output-format json ", src, runner)
            self.assertIn("--output-format stream-json", src, runner)
            self.assertIn("--verbose", src, runner)
            self.assertIn("--forward-subagent-text", src, runner)
            self.assertEqual(src.count('tee "$ws_out/transcript.jsonl" | jq -c \'select(.type? == "result")\' > "$ws_out/agent.json"'), 2, runner)
        # null-agent runs write the synthetic transcript so the pin holds uniformly
        self.assertIn('"null_agent":true}\' > "$ws_out/transcript.jsonl"', _read(RUN_PAIR))


# ---------------------------------------------------------------------------
class AgentBuildsAreArchived(unittest.TestCase):
    """§0.2 / W1: the agent's own dotnet build stdout is archived."""

    def test_extracts_build_test_and_calor_calls_only(self):
        b = hc.extract_builds(fixture("multi-block-run.transcript.jsonl"))
        self.assertEqual([r["toolUseId"] for r in b], ["toolu_01", "toolu_04", "toolu_05", "toolu_06"])
        self.assertEqual([r["kind"] for r in b], ["dotnet-build", "dotnet-build", "calor", "dotnet-test"])
        self.assertEqual([r["index"] for r in b], [1, 2, 3, 4])
        self.assertEqual([r["toolCallOrdinal"] for r in b], [1, 4, 5, 6])   # Read/Edit/ls skipped, still ordinal
        self.assertEqual([r["exitCode"] for r in b], [0, 1, 0, 0])
        # m4: a failing Bash tool_result usually carries only `Build FAILED` + is_error.
        # exitCode must then be 1, never null — null would read as "no failure".
        self.assertTrue(all(r["exitCode"] is not None for r in b))
        self.assertEqual([r["isError"] for r in b], [False, True, False, False])
        self.assertIn("error Calor0410", b[1]["output"])
        self.assertIn("Build succeeded", b[0]["output"])
        self.assertEqual(b[1]["messageId"], "msg_D")
        self.assertTrue(all(r["hasResult"] for r in b))

    def test_workspace_path_is_not_a_calor_invocation(self):
        """M1 — the calor arm's workspace is `.../p0-<pair>-calor-XXXXXX`, so matching a
        bare `calor` recorded nearly every Bash call that named the workspace as a build,
        on the calor arm only (`-csharp-` never matched). That inflates agentBuilds
        arm-asymmetrically in the very artifact meant to answer "why more turns"."""
        commands = [
            "cd /tmp/p0-N1-001-calor-ab12/src && ls -la",
            "grep -rn 'Slugify' /tmp/p0-N1-001-calor-ab12/src",
            "cat /tmp/p0-N1-001-calor-ab12/spec.md",
            "cd /tmp/p0-N1-001-calor-ab12/src && dotnet build",
        ]
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "t.jsonl")
            with open(path, "w") as fh:
                for i, command in enumerate(commands, start=1):
                    fh.write(json.dumps({"type": "assistant", "message": {"id": "m%d" % i, "content": [
                        {"type": "tool_use", "id": "t%d" % i, "name": "Bash",
                         "input": {"command": command}}]}}) + "\n")
                    fh.write(json.dumps({"type": "user", "message": {"content": [
                        {"type": "tool_result", "tool_use_id": "t%d" % i, "content": "ok"}]}}) + "\n")
            records = hc.extract_builds(path)
        self.assertEqual([r["command"] for r in records], [commands[-1]])
        self.assertEqual([r["kind"] for r in records], ["dotnet-build"])

    def test_calor_invocations_still_register(self):
        for command, kind in [("calor --version", "calor"),
                              ("dotnet /opt/calor/calor.dll ids index x.calr", "calor"),
                              ("dotnet test --nologo", "dotnet-test"),
                              ("echo calorimeter", None),
                              ("ls /tmp/p0-N1-001-csharp-ab12/src", None)]:
            self.assertEqual(bool(hc.BUILD_COMMAND_RE.search(command)), kind is not None, command)
            if kind:
                self.assertEqual(hc.classify_command(command), kind, command)

    def test_output_is_capped_and_marked_truncated(self):
        b = hc.extract_builds(fixture("multi-block-run.transcript.jsonl"), max_output=20)
        self.assertTrue(all(len(r["output"]) <= 20 for r in b))
        self.assertTrue(b[0]["outputTruncated"])

    def test_subagent_builds_carry_parent_tool_use_id(self):
        b = hc.extract_builds(fixture("subagent-run.transcript.jsonl"))
        self.assertEqual(len(b), 2)
        self.assertEqual(b[0]["parentToolUseId"], "toolu_agent")
        self.assertIsNone(b[1]["parentToolUseId"])

    def test_cli_emits_jsonl(self):
        out = run([sys.executable, HELPER, "builds", fixture("multi-block-run.transcript.jsonl")])
        self.assertEqual(out.returncode, 0, out.stderr)
        lines = [json.loads(l) for l in out.stdout.splitlines() if l.strip()]
        self.assertEqual(len(lines), 4)
        self.assertEqual(lines[0]["command"], "cd /tmp/p0-N1-001-calor-abc/src && dotnet build")
        empty = run([sys.executable, HELPER, "builds", fixture("empty.transcript.jsonl")])
        self.assertEqual(empty.returncode, 0)
        self.assertEqual(empty.stdout, "")

    def test_runners_write_agent_builds_jsonl(self):
        for runner in (RUN_PAIR, RUN_BUNDLE):
            src = _read(runner)
            self.assertIn('builds "$ws_out/transcript.jsonl" > "$ws_out/agent-builds.jsonl"', src, runner)
            self.assertIn("agentBuilds:$agent_builds", src, runner)
            self.assertIn("turns:$turns", src, runner)


# ---------------------------------------------------------------------------
class CompilerHashIsSurfaced(unittest.TestCase):
    """#1094: obj/calor/.calor-build-state.json archived; compilerHash in result.json."""

    def test_reads_the_compiler_hash(self):
        s = hc.read_build_state(fixture("calor-build-state.json"))
        self.assertEqual(s["compilerHash"], "6474337f4e0657b11d936bd6e45547b4f1de8bc70805002147688263ee06c9ce")
        self.assertEqual(s["formatVersion"], "4.0")
        self.assertEqual(s["source"], "file")
        self.assertTrue(s["optionsHash"])

    def test_missing_or_malformed_state_is_null_not_a_crash(self):
        self.assertEqual(hc.read_build_state(fixture("nope.json"))["source"], "missing")
        self.assertIsNone(hc.read_build_state(fixture("nope.json"))["compilerHash"])
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "s.json")
            _write(p, b"{not json")
            self.assertEqual(hc.read_build_state(p)["source"], "missing")
            _write(p, json.dumps({"formatVersion": "4.0"}).encode())
            self.assertEqual(hc.read_build_state(p)["source"], "no-compiler-hash")
        out = run([sys.executable, HELPER, "build-state", fixture("calor-build-state.json")])
        self.assertEqual(out.returncode, 0)
        self.assertEqual(json.loads(out.stdout)["compilerHash"][:8], "6474337f")

    def test_run_pair_archives_before_deleting_the_workspace(self):
        src = _read(RUN_PAIR)
        agent_stop = src.index('run_agent "$WS" "$WS_OUT" "$SHIM_DIR"\n\n')
        archive = src.index('archive_build_state "$WS" "$WS_OUT" "agent-workspace"')
        final_src = src.index('reason="$(archive_final_src "$WS" "$WS_OUT")"')
        delete = src.index('extract_metrics "$WS" "$WS_OUT" "$run"\n        rm -rf "$WS"')
        self.assertTrue(agent_stop < archive < final_src < delete)
        self.assertIn("compilerHash:$compiler_hash", src)
        self.assertIn("harness-final-build", src)   # fallback when the agent never built, labelled
        self.assertIn("compilerHash:$compiler_hash", _read(RUN_BUNDLE))


# ---------------------------------------------------------------------------
class PairConfigAdmission(unittest.TestCase):
    """§4.1 / W1: pre-rows accepted; permissive without controlArmKind rejected;
    any other config rejected (exit 3, run-pair.sh's pin-violation code)."""

    def test_strict_and_pre_rows_are_the_only_admitted_configs(self):
        self.assertTrue(hc.admit_config(STRICT)[0])
        self.assertIsNone(hc.admit_config(STRICT)[1])
        ok, kind, _ = hc.admit_config(PRE_ROWS)
        self.assertTrue(ok)
        self.assertEqual(kind, "pre-rows")

    def test_permissive_without_control_arm_kind_is_rejected(self):
        ok, kind, reason = hc.admit_config(dict(STRICT, permissiveEffects=True))
        self.assertFalse(ok)
        self.assertIn("controlArmKind", reason)

    def test_control_arm_kind_without_permissive_is_rejected(self):
        ok, _, reason = hc.admit_config(dict(STRICT, controlArmKind="pre-rows"))
        self.assertFalse(ok)
        self.assertIn("pre-rows", reason)

    def test_every_other_config_is_rejected(self):
        for bad in (dict(STRICT, contractMode="off"), dict(STRICT, z3Required=False),
                    dict(STRICT, enforceEffects=False), dict(PRE_ROWS, contractMode="release"),
                    dict(PRE_ROWS, z3Required=False), dict(PRE_ROWS, controlArmKind="post-rows"),
                    dict(STRICT, permissiveEffects="true"),
                    # m1: Python's 1 == True and 0 == False would admit these through a
                    # plain dict comparison — a loosening against the jq check this
                    # replaced, and the second one would slip a permissive arm through.
                    dict(STRICT, enforceEffects=1, permissiveEffects=0),
                    dict(PRE_ROWS, permissiveEffects=1),
                    dict(STRICT, contractMode=None), {}, None):
            self.assertFalse(hc.admit_config(bad)[0], repr(bad))

    def _check(self, pair_json, key, arm=None):
        args = ["bash", RUN_PAIR, "--check-pair-config", pair_json, key]
        if arm:
            args.append(arm)
        out = run(args)
        return out.returncode, (json.loads(out.stdout) if out.stdout.strip() else None), out.stderr

    def test_run_pair_entrypoint_admits_strict_and_pre_rows_and_exits_3_otherwise(self):
        with tempfile.TemporaryDirectory() as tmp:
            pj = os.path.join(tmp, "pair.json")
            write_pair_json(pj, STRICT)
            rc, res, err = self._check(pj, "calor")
            self.assertEqual(rc, 0, err)
            self.assertEqual((res["controlArmKind"], res["fixture"], res["permissiveEffects"]), (None, "calor", False))

            write_pair_json(pj, STRICT, extra_arms={"calor-pre-rows": {"fixture": "calor-pre-rows", "config": PRE_ROWS}})
            rc, res, err = self._check(pj, "calor-pre-rows")
            self.assertEqual(rc, 0, err)
            self.assertEqual((res["controlArmKind"], res["fixture"], res["permissiveEffects"]), ("pre-rows", "calor-pre-rows", True))
            rc, res, _ = self._check(pj, "calor")            # the strict entry still admitted
            self.assertEqual(rc, 0)

            write_pair_json(pj, dict(STRICT, permissiveEffects=True))
            rc, res, _ = self._check(pj, "calor")
            self.assertEqual(rc, 3)
            self.assertFalse(res["admitted"])

            write_pair_json(pj, dict(STRICT, contractMode="off"))
            self.assertEqual(self._check(pj, "calor")[0], 3)
            self.assertEqual(self._check(pj, "missing-key")[0], 3)
            rc, res, _ = self._check(pj, "csharp", "csharp")
            self.assertEqual(rc, 0)
            self.assertEqual(res["fixture"], "csharp")

    def test_fixture_defaults_to_the_arm_language(self):
        with tempfile.TemporaryDirectory() as tmp:
            pj = os.path.join(tmp, "pair.json")
            write_pair_json(pj, STRICT)
            self.assertEqual(hc.resolve_pair_config(pj, "calor")["fixture"], "calor")
            write_pair_json(pj, STRICT, fixture_name="after")
            self.assertEqual(hc.resolve_pair_config(pj, "calor")["fixture"], "after")

    def test_existing_pairs_all_admit_under_the_strict_pin(self):
        pairs_dir = os.path.join(BENCH, "pairs")
        seen = 0
        for name in sorted(os.listdir(pairs_dir)):
            pj = os.path.join(pairs_dir, name, "pair.json")
            if not os.path.isfile(pj):
                continue
            res = hc.resolve_pair_config(pj, "calor")
            self.assertTrue(res["admitted"], name + ": " + str(res["reason"]))
            self.assertIsNone(res["controlArmKind"], name)
            seen += 1
        self.assertGreater(seen, 20)

    def _synthetic_pair(self, tmp, calor_config, extra_arms=None):
        pair = os.path.join(tmp, "T-000-synthetic")
        shutil.copytree(os.path.join(N1_001, "tests"), os.path.join(pair, "tests"))
        shutil.copytree(os.path.join(N1_001, "calor"), os.path.join(pair, "calor"))
        shutil.copy(os.path.join(N1_001, "spec.md"), os.path.join(pair, "spec.md"))
        write_pair_json(os.path.join(pair, "pair.json"), calor_config, extra_arms=extra_arms)
        return pair

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_pair_exits_3_on_a_rejected_config_before_any_build(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._synthetic_pair(tmp, dict(STRICT, permissiveEffects=True))
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
                       "--out", os.path.join(tmp, "out")])
            self.assertEqual(out.returncode, 3, out.stderr)
            self.assertIn("violates gates-doc pin", out.stderr)
            self.assertIn("controlArmKind", out.stderr)
            self.assertFalse(os.path.exists(os.path.join(tmp, "out", "T-000-synthetic")))

    @unittest.skipUnless(HAVE_PRODUCT and HAVE_JQ,
                         "Release product not built (dotnet build src/Calor.{Compiler,Tasks,Runtime} -c Release)")
    def test_pre_rows_arm_canary_passes_now_that_the_product_honours_the_property(self):
        """Before #1124 no product threaded <CalorPermissiveEffects> through Calor.Tasks,
        so this pinned the REFUSAL. #1124 shipped the property, so the same run now has to
        pass the canary and proceed — which is the end-to-end proof, against a REAL product,
        that the paid control arm can actually run permissive. The refusal paths are pinned
        executably by `CanaryRefusals` (fake `dotnet` on PATH, no product needed); this test
        is the real-product counterpart."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._synthetic_pair(tmp, STRICT, extra_arms={
                "calor-pre-rows": {"fixture": "calor", "config": PRE_ROWS}})
            shutil.copytree(os.path.join(N1_001, "reference"), os.path.join(pair, "reference"))
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor",
                       "--arm-config", "calor-pre-rows", "--null-agent",
                       "--out", os.path.join(tmp, "out")], timeout=900)
            if "MSB4064" in out.stderr or "MSB4063" in out.stderr:
                self.skipTest("this checkout's Calor.Tasks assembly is stale or held by an "
                              "MSBuild node (dotnet build-server shutdown)")
            self.assertEqual(out.returncode, 0, out.stderr[-3000:])
            self.assertIn("arm canary OK (pre-rows)", out.stderr)
            self.assertIn("Calor0410 is a warning", out.stderr)
            rd = os.path.join(tmp, "out", "T-000-synthetic", "calor", "run-1")
            r = json.loads(_read(os.path.join(rd, "result.json")))
            self.assertEqual(r["controlArmKind"], "pre-rows")
            self.assertTrue(r["permissiveEffects"])
            self.assertEqual(r["templateSource"], "harness")

    @unittest.skipUnless(HAVE_PRODUCT and HAVE_JQ,
                         "Release product not built (dotnet build src/Calor.{Compiler,Tasks,Runtime} -c Release)")
    def test_strict_arm_is_canaried_too(self):
        """The canary runs on BOTH arms: a task-assembly mismatch fails a strict arm
        identically, and nothing downstream would distinguish it from a failing agent."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._synthetic_pair(tmp, STRICT)
            shutil.copytree(os.path.join(N1_001, "reference"), os.path.join(pair, "reference"))
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
                       "--out", os.path.join(tmp, "out")], timeout=900)
            if "MSB4064" in out.stderr or "MSB4063" in out.stderr:
                self.skipTest("this checkout's Calor.Tasks assembly is stale or held by an "
                              "MSBuild node (dotnet build-server shutdown)")
            self.assertEqual(out.returncode, 0, out.stderr[-3000:])
            self.assertIn("arm canary OK (strict)", out.stderr)
            self.assertIn("Calor0410 is an error", out.stderr)

    def test_arm_canary_diagnoses_a_task_assembly_mismatch_before_the_semantic_branches(self):
        """Source-shape only — the EXECUTABLE coverage is CanaryRefusals below. Kept because
        it localizes an ordering regression, and the ordering itself was corrected after
        review: a stale/mis-built product is the cause, MSBuild worker reuse is not (probed:
        workers reload the assembly from disk every build, same pids)."""
        src = _read(RUN_PAIR)
        canary = src[src.index("arm_canary() {"):src.index("# Config pin check")]
        self.assertLess(canary.index("MSB4064|MSB4063|MSB4062"), canary.index('case "$expect" in'),
                        "the task-assembly mismatch branch must be checked first")
        # the corrected cause order: stale product first, build-server shutdown second
        self.assertLess(canary.index("product is stale"), canary.index("build-server shutdown"),
                        "a stale/mis-built product is the cause; worker reuse is the fallback")
        self.assertIn("arm_canary permissive", src)
        self.assertIn("arm_canary strict", src)
        self.assertIn("MSBUILDDISABLENODEREUSE", src)      # named, and deliberately not set
        self.assertNotIn("export MSBUILDDISABLENODEREUSE", src)


class CanaryRefusals(unittest.TestCase):
    """M1 — the canary's REFUSAL paths, adopted verbatim from the review probe.

    Mutation that motivated them: replacing both semantic branches in `arm_canary` with
    `if false; then` — the canary can no longer refuse an arm running the wrong policy,
    which is the w5-parity-001 failure it exists to prevent — left the whole suite green,
    because the only test that observed a refusal had been deleted by this PR.

    Method: a fake `dotnet` first on PATH emits canned canary output. `arm_canary` is the
    first thing `check_pins` does for a calor arm, so every branch is observable in about
    a second with no product build at all.
    """

    @staticmethod
    def _pair(tmp, extra_arms=None):
        pair = os.path.join(tmp, "T-900-canary")
        shutil.copytree(os.path.join(N1_001, "tests"), os.path.join(pair, "tests"))
        shutil.copytree(os.path.join(N1_001, "calor"), os.path.join(pair, "calor"))
        shutil.copytree(os.path.join(N1_001, "reference"), os.path.join(pair, "reference"))
        shutil.copy(os.path.join(N1_001, "spec.md"), os.path.join(pair, "spec.md"))
        arms = {"calor": {"fixture": "calor", "config": STRICT}}
        arms.update(extra_arms or {})
        _write_json(os.path.join(pair, "pair.json"),
                    {"id": "T-900-canary", "category": "N1", "arms": arms,
                     "iterationBudget": 10, "timeoutSeconds": 600})
        return pair

    @staticmethod
    def _fake_dotnet(tmp, stdout_text, rc):
        d = os.path.join(tmp, "fakebin")
        os.makedirs(d, exist_ok=True)
        path = os.path.join(d, "dotnet")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("#!/usr/bin/env bash\ncat <<'CANARY'\n%s\nCANARY\nexit %d\n" % (stdout_text, rc))
        os.chmod(path, 0o755)
        return d

    def _run(self, pair, tmp, bindir, arm_config=None):
        cmd = ["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
               "--out", os.path.join(tmp, "out")]
        if arm_config:
            cmd += ["--arm-config", arm_config]
        return run(cmd, timeout=300,
                   env=dict(os.environ, PATH=bindir + os.pathsep + os.environ["PATH"]))

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_pre_rows_arm_running_strict_is_refused(self):
        """The w5-parity-001 failure: the control arm silently runs STRICT."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp, {"calor-pre-rows": {"fixture": "calor", "config": PRE_ROWS}})
            b = self._fake_dotnet(tmp, "canary.calr(4,5): error Calor0410: 'Quiet' uses effect 'cw'", 1)
            out = self._run(pair, tmp, b, "calor-pre-rows")
            self.assertEqual(out.returncode, 3, out.stderr[-2000:])
            self.assertIn("does not honor <CalorPermissiveEffects>", out.stderr)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_strict_arm_running_permissive_is_refused(self):
        """The mirror: a 'strict' arm whose product demotes Calor0410 to a warning."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp)
            b = self._fake_dotnet(tmp, "canary.calr(4,5): warning Calor0410: 'Quiet' uses effect 'cw'", 0)
            out = self._run(pair, tmp, b)
            self.assertEqual(out.returncode, 3, out.stderr[-2000:])
            self.assertIn("did not reject a laundered effect", out.stderr)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_task_assembly_mismatch_is_refused_and_names_the_fix(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp)
            b = self._fake_dotnet(tmp, 'Src.csproj(1,1): error MSB4064: The "PermissiveEffects" '
                                       'parameter is not supported by the "CompileCalor" task.', 1)
            out = self._run(pair, tmp, b)
            self.assertEqual(out.returncode, 3, out.stderr[-2000:])
            self.assertIn("MSB4064", out.stderr)
            self.assertIn("product is stale", out.stderr)          # the cause, first
            self.assertIn("dotnet build-server shutdown", out.stderr)   # the fallback

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_a_missing_task_assembly_is_diagnosed_as_MSB4062_not_as_a_policy_story(self):
        """m10: MSB4062 is "the task could not be loaded". Before it was in the pattern the
        operator was told the arm "did not reject a laundered effect" — a config story for
        what is really a missing Calor.Tasks.dll."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp)
            b = self._fake_dotnet(tmp, 'Src.csproj(1,1): error MSB4062: The "Calor.Tasks.CompileCalor" '
                                       'task could not be loaded from the assembly ... '
                                       'Confirm that the declaration is correct.', 1)
            out = self._run(pair, tmp, b)
            self.assertEqual(out.returncode, 3, out.stderr[-2000:])
            self.assertIn("MSB4062", out.stderr)
            self.assertNotIn("did not reject a laundered effect", out.stderr)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_a_correct_arm_passes_and_the_verdict_is_archived(self):
        """M3: the verdict is provenance, not just a console line."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp)
            b = self._fake_dotnet(tmp, "canary.calr(4,5): error Calor0410: 'Quiet' uses effect 'cw'", 1)
            out = self._run(pair, tmp, b)
            self.assertEqual(out.returncode, 0, out.stderr[-2000:])
            self.assertIn("arm canary OK (strict)", out.stderr)
            r = json.loads(_read(os.path.join(tmp, "out", "T-900-canary", "calor", "run-1", "result.json")))
            self.assertEqual(r["armCanary"], "strict-ok")
            self.assertEqual(r["reference"], os.path.join("reference", "calor"))   # m11
            self.assertEqual(r["referenceSource"], "reference")

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_the_skip_is_bounded_and_recorded(self):
        """M3: `CALOR_P0_SKIP_ARM_CANARY=1` set once and forgotten must not be able to
        produce an archive indistinguishable from one where every arm was proven."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = self._pair(tmp)
            b = self._fake_dotnet(tmp, "", 0)
            # refused: the output directory is not a '-null' one
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
                       "--out", os.path.join(tmp, "out")], timeout=300,
                      env=dict(os.environ, PATH=b + os.pathsep + os.environ["PATH"],
                               CALOR_P0_SKIP_ARM_CANARY="1"))
            self.assertEqual(out.returncode, 3, out.stderr[-1500:])
            self.assertIn("only honoured for a --null-agent run", out.stderr)
            # honoured, and recorded, when it is
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
                       "--out", os.path.join(tmp, "out-null")], timeout=300,
                      env=dict(os.environ, PATH=b + os.pathsep + os.environ["PATH"],
                               CALOR_P0_SKIP_ARM_CANARY="1"))
            self.assertEqual(out.returncode, 0, out.stderr[-1500:])
            r = json.loads(_read(os.path.join(tmp, "out-null", "T-900-canary", "calor", "run-1", "result.json")))
            self.assertEqual(r["armCanary"], "skipped")


class PreflightArmProof(unittest.TestCase):
    """The compilerHash disjointness assert runs BEFORE the first paid run, not only after
    collection. The canary is the only build the harness can make before an agent exists,
    and it writes the same obj/calor/.calor-build-state.json #1094 archives per run — so
    a mis-pointed arm is a five-second refusal instead of a $135 loss."""

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_canary_only_reports_the_verdict_and_the_hash_without_running_anything(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = CanaryRefusals._pair(tmp)
            b = CanaryRefusals._fake_dotnet(
                tmp, "canary.calr(4,5): error Calor0410: 'Quiet' uses effect 'cw'", 1)
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--canary-only",
                       "--out", os.path.join(tmp, "out")], timeout=300,
                      env=dict(os.environ, PATH=b + os.pathsep + os.environ["PATH"]))
            self.assertEqual(out.returncode, 0, out.stderr[-2000:])
            doc = json.loads(out.stdout)
            self.assertEqual(doc["armCanary"], "strict-ok")
            self.assertIn("compilerHash", doc)          # null here: the fake dotnet builds nothing
            # nothing was run: no per-run archive exists
            self.assertFalse(os.path.isdir(os.path.join(tmp, "out", "T-900-canary")))

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_canary_only_propagates_a_refusal(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = CanaryRefusals._pair(tmp)
            b = CanaryRefusals._fake_dotnet(
                tmp, "canary.calr(4,5): warning Calor0410: 'Quiet' uses effect 'cw'", 0)
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--canary-only",
                       "--out", os.path.join(tmp, "out")], timeout=300,
                      env=dict(os.environ, PATH=b + os.pathsep + os.environ["PATH"]))
            self.assertEqual(out.returncode, 3, out.stderr[-2000:])
            self.assertIn("did not reject a laundered effect", out.stderr)

    @unittest.skipUnless(HAVE_PRODUCT and HAVE_JQ,
                         "Release product not built (dotnet build src/Calor.{Compiler,Tasks,Runtime} -c Release)")
    def test_canary_only_reads_a_real_compiler_hash_from_the_canary_build(self):
        """Against a REAL product: the hash the pre-flight reports is the same field the
        per-run archive carries, which is what makes comparing them meaningful."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = CanaryRefusals._pair(tmp)
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--canary-only",
                       "--out", os.path.join(tmp, "out")], timeout=900)
            if "MSB4064" in out.stderr or "MSB4063" in out.stderr or "MSB4062" in out.stderr:
                self.skipTest("this checkout's Calor.Tasks assembly is stale or unloadable")
            self.assertEqual(out.returncode, 0, out.stderr[-3000:])
            doc = json.loads(out.stdout)
            self.assertRegex(doc["compilerHash"] or "", r"^[0-9a-f]{64}$")
            self.assertEqual(doc["armCanary"], "strict-ok")

    def test_the_epoch_runner_refuses_two_arms_that_share_a_compiler_before_spending(self):
        src = _read(RUN_PPW)
        preflight = src[src.index("PRE-FLIGHT ARM PROOF"):src.index("cat <<PLAN")]
        # runs before the plan, and therefore before --confirm-paid-epoch is even read
        self.assertLess(src.index("PRE-FLIGHT ARM PROOF"), src.index("REFUSING TO RUN"))
        self.assertIn("--canary-only", preflight)
        self.assertIn('if [[ "$PREFLIGHT_A_HASH" == "$PREFLIGHT_B_HASH" ]]', preflight)
        self.assertIn("Refusing BEFORE any spend", preflight)
        self.assertIn("exit 5", preflight)
        # an unreadable build state is refused too, rather than passing an empty compare
        self.assertIn('-z "$PREFLIGHT_A_HASH" || -z "$PREFLIGHT_B_HASH"', preflight)
        # and the post-collection assert stays as the mid-epoch guard
        self.assertIn("compiler_hashes()", src)

    def test_the_plan_block_shows_both_pre_flight_hashes(self):
        """The plan block is what a reader checks before typing --confirm-paid-epoch."""
        src = _read(RUN_PPW)
        # the heredoc BODY: from the opening marker to the terminator on its own line
        # (searching for "PLAN\n" alone finds the opening `cat <<PLAN` itself)
        start = src.index("cat <<PLAN")
        plan = src[start:src.index("\nPLAN\n", start)]
        self.assertIn("${PREFLIGHT_A_HASH:0:12}", plan)
        self.assertIn("${PREFLIGHT_B_HASH:0:12}", plan)
        # beside the material they qualify
        self.assertIn("Calor.Tasks ${a_tasks:0:12}   compilerHash", plan)
        self.assertIn("Calor.Tasks ${b_tasks:0:12}   compilerHash", plan)


class EpochGuards(unittest.TestCase):
    """M4 and M6 in run-ppw-epoch.sh."""

    def test_arms_must_record_one_distinct_compiler_hash_each(self):
        src = _read(RUN_PPW)
        self.assertIn("compiler_hashes()", src)
        self.assertIn('if [[ "$a_hashes" == "$b_hashes" ]]', src)
        self.assertIn("exit 5", src)
        # and the reason the canary cannot cover it is recorded
        self.assertIn("superset of parameters", src)

    def test_null_epochs_are_dot_prefixed_so_the_archive_walk_skips_them(self):
        src = _read(RUN_PPW)
        self.assertIn('EPOCH=".null-${EPOCH#.}"', src)
        # the instrument this protects really does skip dot entries
        attribution = _read(os.path.join(BENCH, "ppe1-turn-attribution.py"))
        self.assertIn('if name.startswith("."):', attribution)

    def test_one_build_server_shutdown_before_the_first_run(self):
        src = _read(RUN_PPW)
        self.assertIn("dotnet build-server shutdown", src)
        self.assertLess(src.index("dotnet build-server shutdown"), src.index('OUT="$SCRIPT_DIR/epochs/$EPOCH"'))


class TemplateCarriesPermissiveEffects(unittest.TestCase):
    def test_template_placeholder_follows_the_arm_config(self):
        tpl = _read(TEMPLATE)
        self.assertIn("<CalorEnforceEffects>true</CalorEnforceEffects>", tpl)
        self.assertIn("<CalorPermissiveEffects>__CALOR_PERMISSIVE_EFFECTS__</CalorPermissiveEffects>", tpl)
        rendered = tpl.replace("__REPO_ROOT__", "/arm").replace("__CALOR_PERMISSIVE_EFFECTS__", "true")
        self.assertIn("<CalorPermissiveEffects>true</CalorPermissiveEffects>", rendered)
        self.assertNotIn("__CALOR_PERMISSIVE_EFFECTS__", rendered)
        src = _read(RUN_PAIR)
        self.assertIn('s|__CALOR_PERMISSIVE_EFFECTS__|$PERMISSIVE_EFFECTS|g', src)
        self.assertIn('TEMPLATE_SOURCE="harness"', src)   # pre-rows arm uses the harness template

    @unittest.skipUnless(HAVE_PRODUCT, "Release calor.dll not built")
    def test_canary_is_an_error_strict_and_a_warning_permissive(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = ["dotnet", RELEASE_CLI, "-i", CANARY, "--enforce-effects", "--no-telemetry"]
            strict = run(base + ["-o", os.path.join(tmp, "s.g.cs")])
            self.assertNotEqual(strict.returncode, 0)
            self.assertIn("error Calor0410", strict.stdout + strict.stderr)
            perm = run(base + ["--permissive-effects", "-o", os.path.join(tmp, "p.g.cs")])
            self.assertEqual(perm.returncode, 0, perm.stdout + perm.stderr)
            self.assertIn("warning Calor0410", perm.stdout + perm.stderr)


# ---------------------------------------------------------------------------
class LegBPairsAreRegisteredInPins(unittest.TestCase):
    """§3.1 W2 / §4.1: legBPairs read from pins.json, W-005 excluded, never a default."""

    def _pins(self, tmp, **ppw):
        p = os.path.join(tmp, "pins.json")
        with open(p, "w") as fh:
            json.dump({"epochId": "w-rows-001", "suite": ["W-001", "W-002", "W-003", "W-004", "W-005", "W-006"],
                       "ppW": ppw}, fh)
        return p

    def test_reads_leg_b_and_blind_pairs_and_names_the_excluded(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = self._pins(tmp, legBPairs=["W-001", "W-002", "W-003", "W-004", "W-006"],
                           blindPairs=["W-001", "W-004", "W-006"])
            r = hc.read_leg_b_pairs(p)
            self.assertEqual(r["legBPairs"], ["W-001", "W-002", "W-003", "W-004", "W-006"])
            self.assertEqual(r["blindPairs"], ["W-001", "W-004", "W-006"])
            self.assertEqual(r["excludedFromLegB"], ["W-005"])
            out = run([sys.executable, HELPER, "leg-b-pairs", p])
            self.assertEqual(out.returncode, 0)
            self.assertEqual(json.loads(out.stdout)["excludedFromLegB"], ["W-005"])

    def test_missing_duplicate_or_foreign_pairs_are_errors(self):
        with tempfile.TemporaryDirectory() as tmp:
            for bad in ({}, {"legBPairs": []}, {"legBPairs": ["W-001", "W-001"]},
                        {"legBPairs": ["W-001", "N1-001"]}, {"legBPairs": "W-001"}):
                p = self._pins(tmp, **bad)
                with self.assertRaises(ValueError, msg=repr(bad)):
                    hc.read_leg_b_pairs(p)
                out = run([sys.executable, HELPER, "leg-b-pairs", p])
                self.assertEqual(out.returncode, 3, repr(bad))

    def test_run_ppw_epoch_stamps_the_registered_defaults(self):
        src = _read(RUN_PPW)
        self.assertIn('LEG_B_PAIRS="W-001 W-002 W-003 W-004 W-006"', src)
        self.assertIn('BLIND_PAIRS="W-001 W-004 W-006"', src)
        self.assertIn("PAIRS=(W-001 W-002 W-003 W-004 W-005 W-006)", src)
        self.assertIn("legBPairs: $legb, blindPairs: $blind", src)
        self.assertIn("--confirm-paid-epoch", src)
        self.assertIn('--arm-a-config "$ARM_A_CONFIG" --arm-b-config "$ARM_B_CONFIG"', src)
        self.assertIn('ARM_A_CONFIG="calor-pre-rows"; ARM_B_CONFIG="calor"', src)
        self.assertIn('ARM_A_TAG="v0.14.3"; ARM_B_TAG="v0.15.0"', src)
        # Arm A is tag v0.14.3 + the one Tasks-passthrough commit (branch arm/v0.14.3-pre-rows);
        # the runner refuses any other commit and re-verifies the diff confinement.
        self.assertIn('ARM_A_EXPECTED_COMMIT="283ec9f9964ddd5b21da15b646a0dd77d53de99e"', src)
        self.assertIn('ARM_A_BRANCH="arm/v0.14.3-pre-rows"', src)
        self.assertIn('[[ "$ARM_A_COMMIT" != "$ARM_A_EXPECTED_COMMIT" ]]', src)
        self.assertIn('== "src/Calor.Sdk/Sdk/Sdk.targets src/Calor.Tasks/CompileCalor.cs "', src)
        # M2 — the product is built from the WORKING TREE, so a commit pin alone would
        # certify a build that does not match the commit.
        self.assertIn('git -C "$ARM_A_ROOT" status --porcelain', src)
        self.assertIn('git -C "$ARM_B_ROOT" status --porcelain', src)
        self.assertIn("armDirtyFiles", src)
        # M3 — compilerHash cannot witness the POLICY (both arms built from one product
        # share it); optionsHash is what moves.
        self.assertIn("optionsHash", src)
        # m2 — the blind floor is checked before spend, not discovered after $135.
        self.assertIn("the registered floor is 2", src)
        # m3 + M6 — a null-agent run never lands on a live epoch id, whatever id was
        # passed, and the directory is dot-prefixed so the archive instruments skip it.
        self.assertIn('[[ -n "$NULL_FLAG" && "$EPOCH" != .* ]]', src)
        self.assertIn('EPOCH=".null-${EPOCH#.}"', src)
        # m5 — every registered pair must actually have produced runs.
        self.assertIn("missing_results", src)
        m5 = _read(RUN_M5)
        self.assertIn("--arm-a-config) ARM_A_CONFIG=", m5)
        self.assertIn('--arm-config "$cfg"', m5)


# ---------------------------------------------------------------------------
class RunPpwEpochFailsLoudOnMissingPairs(unittest.TestCase):
    """§3.1 W1: the runner must list every unrunnable W-00x pair and exit before any spend.

    The six pair directories landed with #1123 (S3 (c)), so the missing-pair path is now
    exercised against a COPY of the runner beside an empty `pairs/` — the behaviour is the
    point, not the accident of the tree not holding the pairs yet. `test_the_real_pair_set_passes_preflight`
    covers the other side: with the committed pairs present, no pair problem is raised."""

    def _fake_harness(self, tmp):
        """A harness dir holding just what run-ppw-epoch.sh reads from SCRIPT_DIR, and no pairs."""
        harness = os.path.join(tmp, "bench", "phase0-agent-native")
        os.makedirs(os.path.join(harness, "pairs"))
        for name in ("run-ppw-epoch.sh", "harness-capture.py"):
            shutil.copy(os.path.join(BENCH, name), os.path.join(harness, name))
        return os.path.join(harness, "run-ppw-epoch.sh")

    def _fake_root(self, tmp, name, tasks_bytes):
        root = os.path.join(tmp, name)
        for rel in ("src/Calor.Compiler/bin/Release/net10.0", "src/Calor.Tasks/bin/Release/net10.0",
                    "src/Calor.Runtime/bin/Release/net10.0"):
            os.makedirs(os.path.join(root, rel))
        _write(os.path.join(root, "src/Calor.Compiler/bin/Release/net10.0/calor.dll"), b"cli" + tasks_bytes)
        _write(os.path.join(root, "src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll"), tasks_bytes)
        _write(os.path.join(root, "src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll"), b"rt")
        return root

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_the_real_pair_set_passes_preflight(self):
        # With the committed pairs present the pair preflight must raise NO problem —
        # every id resolves to exactly one directory and both arm entries are admitted.
        # The run still refuses before spend, on the arm-A commit pin, which is the next
        # gate; what must not appear is a pair problem.
        with tempfile.TemporaryDirectory() as tmp:
            a = self._fake_root(tmp, "armA", b"A")
            b = self._fake_root(tmp, "armB", b"B")
            out = run(["bash", RUN_PPW, "--arm-a-repo-root", a, "--arm-b-repo-root", b, "--null-agent"])
            self.assertEqual(out.returncode, 2, out.stdout + out.stderr)
            self.assertNotIn("refusing before any spend", out.stderr)
            self.assertNotIn("no directory pairs/", out.stderr)
            self.assertNotIn("rejected —", out.stderr)
            self.assertNotIn("fixture directory missing", out.stderr)
            # The plan is echoed first and the refusal follows it; what matters is that the
            # refusal is the arm-A pin (the next gate) and that no run was dispatched.
            self.assertIn("arm A must be checked out at", out.stderr)
            self.assertNotIn("run-pair.sh", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_lists_every_missing_pair_and_exits_2(self):
        with tempfile.TemporaryDirectory() as tmp:
            a = self._fake_root(tmp, "armA", b"A")
            b = self._fake_root(tmp, "armB", b"B")
            runner = self._fake_harness(tmp)
            out = run(["bash", runner, "--arm-a-repo-root", a, "--arm-b-repo-root", b, "--null-agent"])
            self.assertEqual(out.returncode, 2, out.stdout + out.stderr)
            self.assertIn("refusing before any spend", out.stderr)
            for pid in ("W-001", "W-002", "W-003", "W-004", "W-005", "W-006"):
                self.assertIn("%s: no directory pairs/%s-*" % (pid, pid), out.stderr)
            # exact-id matching: the existing W1-/W2-/W3-/W5A- directories never satisfy a W-00x id
            self.assertNotIn("W1-001", out.stderr)
            self.assertNotIn("=== PP-W-rows — epoch plan ===", out.stdout)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_a_foreign_leg_b_pair_is_a_problem_too(self):
        with tempfile.TemporaryDirectory() as tmp:
            a = self._fake_root(tmp, "armA", b"A")
            b = self._fake_root(tmp, "armB", b"B")
            out = run(["bash", RUN_PPW, "--arm-a-repo-root", a, "--arm-b-repo-root", b, "--null-agent",
                       "--leg-b-pairs", "W-001 N1-001"])
            self.assertEqual(out.returncode, 2)
            self.assertIn("'N1-001' (in --leg-b-pairs/--blind-pairs) is not one of", out.stderr)


# ---------------------------------------------------------------------------
class NullAgentReferenceResolution(unittest.TestCase):
    """The --null-agent path applies a correct solution and checks the whole loop at zero
    spend. It assumed every pair ships `reference/<fixture>/`; the PP-W-rows pairs
    (§4.1, S3 (c)) ship per-arm `seeded/clean-<armId>` instead, so a free null epoch over
    the six W-00x pairs died at `cp` on the first run of the first pair."""

    def test_pre_v016_pairs_still_resolve_reference_slash_fixture(self):
        r = hc.resolve_pair_config(os.path.join(N1_001, "pair.json"), "calor")
        self.assertEqual(r["reference"], os.path.join("reference", "calor"))
        self.assertEqual(r["referenceSource"], "reference")

    def test_ppw_pairs_resolve_their_declared_clean_cell(self):
        for pair in sorted(p for p in os.listdir(PAIRS) if p.startswith("W-00")):
            pj = os.path.join(PAIRS, pair, "pair.json")
            if not os.path.isfile(pj):
                continue
            for key, arm_id in (("calor-pre-rows", "a"), ("calor", "b")):
                r = hc.resolve_pair_config(pj, key)
                self.assertTrue(r["admitted"], (pair, key, r["reason"]))
                self.assertEqual(r["reference"], "seeded/clean-" + arm_id, (pair, key))
                self.assertEqual(r["referenceSource"], "seeded-clean-declared", (pair, key))
                self.assertTrue(os.path.isdir(os.path.join(PAIRS, pair, r["reference"])),
                                (pair, key, "declared reference does not exist"))

    def test_every_pair_in_the_tree_resolves_a_reference_that_exists(self):
        seen = 0
        for pair in sorted(os.listdir(PAIRS)):
            pj = os.path.join(PAIRS, pair, "pair.json")
            if not os.path.isfile(pj):
                continue
            with open(pj, encoding="utf-8") as fh:
                keys = list((json.load(fh).get("arms") or {}).keys())
            for key in keys:
                r = hc.resolve_pair_config(pj, key)
                self.assertIsNotNone(r["reference"], (pair, key, "no reference resolved"))
                self.assertTrue(os.path.isdir(os.path.join(PAIRS, pair, r["reference"])),
                                (pair, key, r["reference"]))
                seen += 1
        # 31 pairs x 2 arm entries = 62 today; asserted as a floor so a pair family that
        # silently stops being walked is caught (the old `> 50` had no derivation).
        pair_count = sum(1 for d in os.listdir(PAIRS)
                         if os.path.isfile(os.path.join(PAIRS, d, "pair.json")))
        self.assertGreaterEqual(seen, 2 * pair_count)
        self.assertGreaterEqual(seen, 62)

    def test_resolution_order_is_reference_then_declared_then_derived(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = os.path.join(tmp, "T-000")
            os.makedirs(os.path.join(pair, "seeded", "clean-a"))
            pj = os.path.join(pair, "pair.json")
            entry = {"armId": "a", "fixture": "starter-a", "config": PRE_ROWS}
            doc = {"id": "T-000", "arms": {"calor-pre-rows": entry},
                   "seeded": {"clean": {"a": "seeded/clean-a"}}}
            _write_json(pj, doc)
            # (2) declared wins when reference/<fixture> is absent
            r = hc.resolve_pair_config(pj, "calor-pre-rows")
            self.assertEqual((r["reference"], r["referenceSource"]),
                             ("seeded/clean-a", "seeded-clean-declared"))
            # (3) derived from the fixture suffix when nothing is declared
            _write_json(pj, {"id": "T-000", "arms": {"calor-pre-rows": entry}})
            r = hc.resolve_pair_config(pj, "calor-pre-rows")
            self.assertEqual((r["reference"], r["referenceSource"]),
                             (os.path.join("seeded", "clean-a"), "seeded-clean-derived"))
            # (1) an explicit reference/<fixture> outranks both
            os.makedirs(os.path.join(pair, "reference", "starter-a"))
            _write_json(pj, doc)
            r = hc.resolve_pair_config(pj, "calor-pre-rows")
            self.assertEqual((r["reference"], r["referenceSource"]),
                             (os.path.join("reference", "starter-a"), "reference"))

    def test_no_reference_at_all_is_reported_as_none_not_guessed(self):
        with tempfile.TemporaryDirectory() as tmp:
            pair = os.path.join(tmp, "T-001")
            os.makedirs(pair)
            pj = os.path.join(pair, "pair.json")
            _write_json(pj, {"id": "T-001", "arms": {
                "calor-pre-rows": {"armId": "a", "fixture": "starter-a", "config": PRE_ROWS}}})
            r = hc.resolve_pair_config(pj, "calor-pre-rows")
            self.assertIsNone(r["reference"])
            self.assertEqual(r["referenceSource"], "none")
            # a declared cell that does not exist on disk is an ERROR, and must not fall
            # through to the derived form: applying a different program than pair.json
            # names is worse than failing (m2).
            os.makedirs(os.path.join(pair, "seeded", "clean-a"))
            _write_json(pj, {"id": "T-001", "arms": {
                "calor-pre-rows": {"armId": "a", "fixture": "starter-a", "config": PRE_ROWS}},
                "seeded": {"clean": {"a": "seeded/clean-typo"}}})
            r = hc.resolve_pair_config(pj, "calor-pre-rows")
            self.assertIsNone(r["reference"])
            self.assertEqual(r["referenceSource"], "seeded-clean-declared-missing:seeded/clean-typo")

    def test_a_reference_that_escapes_the_pair_directory_is_rejected(self):
        """m4: pair.json is data. An absolute path or one climbing out with `..` would
        point the null path at anything on the machine."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = os.path.join(tmp, "T-003")
            os.makedirs(os.path.join(pair, "seeded", "clean-a"))
            outside = os.path.join(tmp, "outside")
            os.makedirs(outside)
            pj = os.path.join(pair, "pair.json")
            for escape in ("/etc", outside, "../outside", "seeded/../../outside"):
                _write_json(pj, {"id": "T-003", "arms": {
                    "calor-pre-rows": {"armId": "a", "fixture": "starter-a", "config": PRE_ROWS}},
                    "seeded": {"clean": {"a": escape}}})
                r = hc.resolve_pair_config(pj, "calor-pre-rows")
                self.assertIsNone(r["reference"], escape)
                self.assertTrue(r["referenceSource"].startswith("seeded-clean-declared-missing"), escape)

    @unittest.skipUnless(HAVE_JQ, "jq not on PATH")
    def test_run_pair_null_agent_exits_3_when_no_reference_exists(self):
        """Defect 2: the guard printed and fell through into `cp`, which then failed with
        a raw shell error and a zero-ish exit path. It must exit non-zero, before the copy."""
        with tempfile.TemporaryDirectory() as tmp:
            pair = os.path.join(tmp, "T-002-no-reference")
            shutil.copytree(os.path.join(N1_001, "tests"), os.path.join(pair, "tests"))
            shutil.copytree(os.path.join(N1_001, "calor"), os.path.join(pair, "calor"))
            shutil.copy(os.path.join(N1_001, "spec.md"), os.path.join(pair, "spec.md"))
            _write_json(os.path.join(pair, "pair.json"), {
                "id": "T-002-no-reference", "category": "N1",
                "arms": {"calor": {"fixture": "calor", "config": STRICT},
                         "csharp": {"fixture": "csharp", "toolkit": "full"}},
                "iterationBudget": 10, "timeoutSeconds": 600})
            # The arm canary runs before this guard and needs a built product; this test is
            # about the reference guard itself, so the canary is switched off explicitly.
            # the '-null' out dir is what M3's bounded skip requires
            out = run(["bash", RUN_PAIR, "--pair", pair, "--arm", "calor", "--null-agent",
                       "--out", os.path.join(tmp, "out-null")], timeout=900,
                      env=dict(os.environ, CALOR_P0_SKIP_ARM_CANARY="1"))
            self.assertEqual(out.returncode, 3, out.stdout[-800:] + out.stderr[-2000:])
            self.assertIn("null-agent needs a reference solution", out.stderr)
            self.assertIn("seeded.clean.<armId>", out.stderr)
            # the raw shell error the old shape produced must be gone
            self.assertNotIn("No such file or directory", out.stderr)

    def test_runner_reads_the_resolved_reference_and_the_guard_exits(self):
        src = _read(RUN_PAIR)
        self.assertIn('REFERENCE_DIR="$(jq -r \'.reference // empty\'', src)
        # m1: the copy is structurally safe. With REFERENCE_DIR empty and the guard above
        # deleted, `cp -R "$PAIR_DIR//."` is a VALID copy of the whole pair directory —
        # tests/ (the held-out suite) and pair.json included — into the agent's workspace.
        self.assertIn('cp -R "$PAIR_DIR/${REFERENCE_DIR:?null-agent reference unresolved}/." "$ws/src/"', src)
        self.assertNotIn('cp -R "$PAIR_DIR/reference/$FIXTURE_DIR/." "$ws/src/"', src)
        self.assertNotIn('cp -R "$PAIR_DIR/$REFERENCE_DIR/." "$ws/src/"', src)
        # Defect 2 was a `|| { echo ...; }` guard with no exit, which printed and then
        # fell through. Audit EVERY diagnostic guard in the file by brace depth (a regex
        # stops at the first `}` and truncates guards containing ${VAR:-default}).
        no_exit = [g for g in _diagnostic_guards(src) if "exit " not in g]
        self.assertEqual(
            len(no_exit), 1,
            "every `|| { echo ... }` guard must exit, except the deliberately non-fatal "
            "starter build; found: %r" % no_exit)
        self.assertIn("starter fixture failed to build", no_exit[0])
        self.assertIn("Deliberately NOT fatal", src)   # and it says why


def _diagnostic_guards(src):
    """Every `|| { ... }` block in a shell source that reports something (contains echo),
    extracted by brace depth so multi-line guards and ${VAR:-default} survive intact."""
    guards = []
    i = 0
    while True:
        i = src.find("|| {", i)
        if i < 0:
            return [g for g in guards if "echo" in g]
        j = src.index("{", i)
        depth, k = 0, j
        while k < len(src):
            if src[k] == "{":
                depth += 1
            elif src[k] == "}":
                depth -= 1
                if depth == 0:
                    break
            k += 1
        guards.append(src[i:k + 1])
        i = k + 1


def _write_json(path, doc):
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(doc, fh)


class CensusExclusionIsNarrow(unittest.TestCase):
    """M4 — harness scratch is excluded from the committed-.calr census, and nothing else is.

    **Dispositioned when the PP-W-rows pairs landed (#1123, S3 (c)).** W1 shipped the
    seeded-only form and recorded that the per-arm STARTERS needed a decision, because 9
    of the `starter-b` files write inline parameter rows and same-line `§FLD … §E{…}` —
    exactly the forms `EffectRowCorpusShapeTests` asserts no committed `.calr` writes. Of
    the three options W1 listed — exclude the starters, retire the §3.2 line-rule claim,
    or rewrite the fixtures — only the first was available: the starters are frozen by
    blob SHA at `7d621c0d` (§4.1 route (a) reads their multisets and `test_ppw_pairs.py`
    asserts their bytes), and §3.2's claim is about programs written before the line rule,
    which these are not. Including them would also have moved the census 927 → 939 and
    forced a formatter-baseline regeneration from the workstream that runs the probe.

    So the rule is now one shared helper, `tests/Calor.Compiler.Tests/Effects/PpwFixture.cs`
    — `^bench/phase0-agent-native/(pairs|epochs/[^/]+)/W-\\d{3}-` — covering the whole PP-W
    pair family AND the per-run archives a PP-W epoch writes (every arm-B run archives
    `final-src/*.calr` carrying the starter's rows verbatim; without this the first epoch
    would red the shape pin and drift the count, which §4.1 adjudicates as an own-goal MISS).

    Narrowness is still the property under test: no other pair family, and no other epoch,
    is exempt."""

    WALKERS = [
        os.path.join(REPO, "tests", "Calor.Compiler.Tests", "LosslessFormattingTests.cs"),
        os.path.join(REPO, "tests", "Calor.Compiler.Tests", "Effects", "EffectRowCorpusShapeTests.cs"),
        os.path.join(REPO, "tests", "Calor.Compiler.Tests", "Effects", "EffectResolverKeyLedgerTests.cs"),
        os.path.join(REPO, "tests", "Calor.Compiler.Tests", "Effects", "HigherOrderDemandLedgerTests.cs"),
    ]

    HELPER = os.path.join(REPO, "tests", "Calor.Compiler.Tests", "Effects", "PpwFixture.cs")
    PATTERN = r"^bench/phase0-agent-native/(pairs|epochs/[^/]+)/W-\d{3}-"

    def test_every_walker_excludes_templates_and_the_ppw_fixtures(self):
        for walker in self.WALKERS:
            src = _read(walker)
            self.assertIn("bench/phase0-agent-native/templates/", src, walker)
            # one shared rule, not four copies of a regex that can drift apart
            self.assertIn("PpwFixture.IsMatch", src, walker)
            # the un-anchored prefix form must be gone: it stops covering W-010+
            self.assertNotIn('"bench/phase0-agent-native/pairs/W-00"', src, walker)

    def test_the_helper_carries_the_anchored_rule_and_says_why(self):
        src = _read(self.HELPER)
        self.assertIn(self.PATTERN, src)
        for word in ("frozen", "epoch", "own-goal", "starters"):
            self.assertIn(word, src, f"PpwFixture must record the disposition ({word})")

    def test_the_regex_matches_the_ppw_family_and_its_epoch_archives_only(self):
        import re as _re
        pattern = _re.compile(self.PATTERN)
        matches = [
            "bench/phase0-agent-native/pairs/W-001-middleware/seeded/shortcut.calr",
            "bench/phase0-agent-native/pairs/W-001-middleware/starter-a/Prog.calr",
            "bench/phase0-agent-native/pairs/W-001-middleware/starter-b/Prog.calr",
            "bench/phase0-agent-native/pairs/W-010-later/seeded/x.calr",   # W-010+, not just W-00x
            # what a PP-W epoch archives, run by run — the reason the rule covers epochs/
            "bench/phase0-agent-native/epochs/w-rows-001/W-001-middleware/calor/run-1/final-src/Prog.calr",
        ]
        non_matches = [
            "bench/phase0-agent-native/pairs/W1-001-temperature-converter/calor/Temp.calr",
            "bench/phase0-agent-native/pairs/N1-001-string-utils/calor/TextUtils.calr",
            # other epochs stay in the census: 210 of their archived .calr are inside the
            # committed counts today, and excluding them would regenerate every ledger
            "bench/phase0-agent-native/epochs/e1-rows-parity-001/N1-003-csv-row/calor/run-5/final-src/CsvRow.calr",
            "bench/phase0-agent-native/epochs/w5-parity-002/N1-001-string-utils/calor/run-1/final-src/T.calr",
        ]
        for path in matches:
            self.assertIsNotNone(pattern.match(path), path)
        for path in non_matches:
            self.assertIsNone(pattern.match(path), path)


class MarginDerivation(unittest.TestCase):
    """§2.3(b): population flag, SIMS >= 3000, grid extended to 1.15/1.20 —
    with the A-1.11 defaults frozen byte for byte."""

    def test_defaults_reproduce_the_committed_a111_output_byte_for_byte(self):
        out = run([sys.executable, MARGIN], cwd=REPO)
        self.assertEqual(out.returncode, 0, out.stderr)
        self.assertEqual(out.stdout, _read(MARGIN_TXT))

    def test_grid_choice_does_not_touch_the_rng_sequence(self):
        frozen = run([sys.executable, MARGIN, "--sims", "8", "--boot", "16", "--seed", "4537", "--grid", "frozen"], cwd=REPO)
        extended = run([sys.executable, MARGIN, "--sims", "8", "--boot", "16", "--seed", "4537", "--grid", "extended"], cwd=REPO)
        self.assertEqual(frozen.returncode, 0, frozen.stderr)
        self.assertEqual(extended.returncode, 0, extended.stderr)
        null_f = [l for l in frozen.stdout.splitlines() if "null point:" in l]
        null_e = [l for l in extended.stdout.splitlines() if "null point:" in l]
        self.assertEqual(len(null_f), 4)
        self.assertEqual(null_f, null_e)
        self.assertNotIn("margin 1.15:", frozen.stdout)
        self.assertNotIn("margin 1.20:", frozen.stdout)
        self.assertIn("margin 1.15:", extended.stdout)
        self.assertIn("margin 1.20:", extended.stdout)
        self.assertIn("A-1.12 rule", extended.stdout)
        self.assertNotIn("A-1.12 rule", frozen.stdout)

    def test_e1_and_pooled_populations_run(self):
        e1 = run([sys.executable, MARGIN, "--population", "e1-rows-parity-001", "--sims", "6", "--boot", "12",
                  "--seed", "1", "--grid", "extended"], cwd=REPO)
        self.assertEqual(e1.returncode, 0, e1.stderr)
        self.assertIn("population e1-rows-parity-001", e1.stdout)
        self.assertIn("e1-rows-parity-001 — PP-W-rows leg-B derivation population", e1.stdout)
        self.assertNotIn("m5-compare-001", e1.stdout)
        self.assertEqual(e1.stdout.count("control    n=5"), 4)
        self.assertEqual(e1.stdout.count("treatment  n=5"), 4)
        pooled = run([sys.executable, MARGIN, "--population", "pooled", "--sims", "6", "--boot", "12",
                      "--seed", "1"], cwd=REPO)
        self.assertEqual(pooled.returncode, 0, pooled.stderr)
        self.assertEqual(pooled.stdout.count("control    n=10"), 4)

    def test_a112_rule_rounds_p95_plus_half_width_up_to_the_grid(self):
        m = _load_margin_module()
        self.assertEqual(m.a112_margin(1.1766, 0.005), 1.20)
        self.assertEqual(m.a112_margin(1.1864, 0.005), 1.20)
        self.assertEqual(m.a112_margin(1.1951, 0.005), 1.25)
        self.assertEqual(m.a112_margin(1.3302, 0.005), 1.35)
        self.assertEqual(m.grid_round_up(1.3302), 1.35)
        self.assertEqual(m.grid_round_up(1.247), 1.25)
        self.assertEqual(m.EXTENDED_MARGINS[:2], (1.15, 1.20))
        self.assertEqual(m.FROZEN_MARGINS, (1.25, 1.30, 1.35, 1.40, 1.45, 1.50))
        self.assertEqual((m.SEED, m.SIMS if "PPE1_SIMS" not in os.environ else 300), (4537, 300))

    def test_committed_ppw_derivation_is_the_registered_run_and_self_consistent(self):
        txt = _read(PPW_TXT)
        self.assertIn("3000 null simulations x 400-resample two-level cluster bootstrap, seed 4537", txt)
        self.assertIn("population e1-rows-parity-001; grid extended (1.15, 1.20, 1.25", txt)
        self.assertIn("resample-with-replacement", txt)
        rule = re.search(r"\[corrected\] A-1\.12 rule: grid line above \(p95 ([0-9.]+) \+ half-width ([0-9.]+) = ([0-9.]+)\) -> margin ([0-9.]+)", txt)
        self.assertIsNotNone(rule, "corrected A-1.12 rule line missing")
        p95, hw, total, margin = (float(x) for x in rule.groups())
        m = _load_margin_module()
        self.assertAlmostEqual(p95 + hw, total, places=4)
        self.assertEqual(m.a112_margin(p95, hw), margin)
        null = re.search(r"\[corrected\] null point: median ([0-9.]+)  p95 ([0-9.]+)", txt)
        self.assertEqual(float(null.group(2)), p95)


def _load_margin_module():
    spec = importlib.util.spec_from_file_location("ppe1_margin", MARGIN)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ---------------------------------------------------------------------------
class SelfTest(unittest.TestCase):
    def test_helper_self_test_passes(self):
        out = run([sys.executable, HELPER, "self-test"])
        self.assertEqual(out.returncode, 0, out.stderr)


@unittest.skipUnless(HAVE_PRODUCT and HAVE_JQ, "Release product not built (dotnet build src/Calor.{Compiler,Tasks,Runtime} -c Release)")
class NullAgentEndToEnd(unittest.TestCase):
    """Zero-spend integration: a null-agent run of N1-001 on the calor arm
    archives transcript.jsonl, agent-builds.jsonl, calor-build-state.json and
    surfaces compilerHash / turns / controlArmKind in result.json."""

    def test_calor_arm_null_agent_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = run(["bash", RUN_PAIR, "--pair", N1_001, "--arm", "calor", "--null-agent", "--out", tmp], timeout=900)
            self.assertEqual(out.returncode, 0, out.stderr[-3000:])
            rd = os.path.join(tmp, "N1-001-string-utils", "calor", "run-1")
            for f in ("transcript.jsonl", "agent-builds.jsonl", "calor-build-state.json", "result.json", "agent.json"):
                self.assertTrue(os.path.isfile(os.path.join(rd, f)), f)
            r = json.loads(_read(os.path.join(rd, "result.json")))
            self.assertFalse(r["invalid"])
            self.assertRegex(r["compilerHash"], r"^[0-9a-f]{64}$")
            self.assertEqual(r["buildState"]["archivedFrom"], "agent-workspace")
            self.assertEqual(r["turns"]["assistantMessages"], 0)
            self.assertEqual(r["turns"]["transcript"], "transcript.jsonl")
            self.assertIsNone(r["controlArmKind"])
            self.assertEqual((r["armConfigKey"], r["fixture"], r["permissiveEffects"], r["templateSource"]),
                             ("calor", "calor", False, "arm-repo-root"))
            self.assertEqual(r["agentBuilds"], {"count": 0, "file": "agent-builds.jsonl"})


if __name__ == "__main__":
    unittest.main(verbosity=2)
