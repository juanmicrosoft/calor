#!/usr/bin/env python3
"""Pinned reproduction for #881: the epoch cost metric must count subagent and
compaction-segment output tokens, not just the final top-level turn.

Runs `token-usage.py` over synthetic `agent.json` fixtures shaped like the
archived runs that exhibited the defect, and asserts:

  * output_tokens_naive     == the top-level usage.output_tokens (old metric)
  * output_tokens_corrected == the known per-model sum (what the cost leg reads)

so the 55x class of under-count is observable, and a regression to reading
`usage.output_tokens` fails here rather than in a frozen annex.

Run:  python3 bench/phase0-agent-native/tests/test_token_usage.py
"""

import importlib.util
import json
import os
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
HELPER = os.path.join(BENCH, "token-usage.py")
FIXTURES = os.path.join(HERE, "fixtures", "token-usage")

# The committed artifact behind the issue's 55x row (w5-parity-002 epoch).
ARCHIVED_55X = os.path.join(
    BENCH, "epochs", "w5-parity-002", "N1-001-string-utils",
    "calor+treatment", "run-4", "agent.json")


def _load_helper():
    spec = importlib.util.spec_from_file_location("token_usage", HELPER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


helper = _load_helper()


def run_cli(path, *extra):
    out = subprocess.run(
        [sys.executable, HELPER, path] + list(extra),
        check=True, capture_output=True, text=True)
    return json.loads(out.stdout)


def fixture(name):
    return os.path.join(FIXTURES, name)


class SubagentUnderCount(unittest.TestCase):
    """The defect: a run that delegated to a subagent and resumed on a
    task-notification recorded 543 tokens; the model actually produced 30,084."""

    def test_naive_equals_top_level_and_corrected_equals_model_sum(self):
        r = run_cli(fixture("subagent-run.agent.json"))
        self.assertEqual(r["output_tokens_naive"], 543)
        self.assertEqual(r["output_tokens_corrected"], 30084)
        self.assertEqual(r["input_tokens_naive"], 2)
        self.assertEqual(r["input_tokens_corrected"], 73)
        self.assertEqual(r["source"], "modelUsage")

    def test_undercount_is_flagged_and_ratio_is_the_issues_55x(self):
        r = run_cli(fixture("subagent-run.agent.json"))
        self.assertTrue(r["undercount_flagged"])
        self.assertGreater(r["undercount_ratio"], 55.0)
        self.assertLess(r["undercount_ratio"], 56.0)
        self.assertEqual(r["origin_kind"], "task-notification")
        self.assertEqual(r["num_turns"], 1)

    def test_topic_detector_side_model_is_excluded_but_auditable(self):
        r = run_cli(fixture("subagent-run.agent.json"))
        self.assertEqual(r["models_counted"], ["claude-opus-4-8"])
        self.assertEqual(r["models_excluded"], {"claude-haiku-4-5-20251001": 14})
        self.assertEqual(r["output_tokens_all_models"], 30084 + 14)


class MultiModelDelegation(unittest.TestCase):
    """Subagent turns on a second model: every non-side model is summed."""

    def test_sums_every_counted_model(self):
        r = run_cli(fixture("multi-model-run.agent.json"))
        self.assertEqual(r["output_tokens_naive"], 6331)
        self.assertEqual(r["output_tokens_corrected"], 6331 + 17981)
        self.assertEqual(r["models_counted"], ["claude-fable-5", "claude-opus-4-8[1m]"])
        self.assertTrue(r["undercount_flagged"])


class NormalRun(unittest.TestCase):
    """No delegation, no compaction: naive and corrected agree and no warning."""

    def test_naive_and_corrected_agree(self):
        r = run_cli(fixture("normal-run.agent.json"))
        self.assertEqual(r["output_tokens_naive"], 8311)
        self.assertEqual(r["output_tokens_corrected"], 8311)
        self.assertEqual(r["undercount_ratio"], 1.0)
        self.assertFalse(r["undercount_flagged"])


class EdgeShapes(unittest.TestCase):
    def test_epoch_pinned_to_the_excluded_model_still_counts_its_output(self):
        r = run_cli(fixture("haiku-only-run.agent.json"))
        self.assertEqual(r["output_tokens_corrected"], 1214)
        self.assertEqual(r["models_counted"], ["claude-haiku-4-5-20251001"])
        self.assertEqual(r["models_excluded"], {})

    def test_large_haiku_entry_is_a_subagent_and_counts(self):
        # feasibility-dry-001/N1-001 run-3 shape: haiku 10,142 vs main 3,893.
        r = run_cli(fixture("haiku-subagent-run.agent.json"))
        self.assertEqual(r["output_tokens_naive"], 3893)
        self.assertEqual(r["output_tokens_corrected"], 3893 + 10142)
        self.assertEqual(r["models_counted"], ["claude-fable-5", "claude-haiku-4-5-20251001"])
        self.assertEqual(r["models_excluded"], {})
        self.assertTrue(r["undercount_flagged"])

    def test_side_call_size_gate_is_configurable(self):
        r = run_cli(fixture("haiku-subagent-run.agent.json"), "--side-call-max", "20000")
        self.assertEqual(r["output_tokens_corrected"], 3893)
        self.assertEqual(r["models_excluded"], {"claude-haiku-4-5-20251001": 10142})

    def test_cache_counters_are_reported_for_audit_only(self):
        r = run_cli(fixture("haiku-subagent-run.agent.json"))
        self.assertEqual(r["cache_read_input_tokens_naive"], 50000)
        self.assertEqual(r["cache_read_input_tokens_corrected"], 170000)
        self.assertEqual(r["cache_creation_input_tokens_naive"], 3000)
        self.assertEqual(r["cache_creation_input_tokens_corrected"], 12000)
        self.assertEqual(r["input_tokens_corrected"], 2030)  # tokens.input semantics unchanged

    def test_truncated_envelope_is_missing_not_a_crash(self):
        # Non-empty but cut off mid-object: the helper must still exit 0 and
        # report source "missing"; the runner's fallback path handles it.
        r = run_cli(fixture("truncated-run.agent.json"))
        self.assertEqual(r["source"], "missing")
        self.assertEqual(r["output_tokens_naive"], 0)
        self.assertEqual(r["output_tokens_corrected"], 0)

    def test_envelope_without_modelusage_falls_back_to_naive_and_says_so(self):
        r = run_cli(fixture("no-modelusage-run.agent.json"))
        self.assertEqual(r["source"], "usage")
        self.assertEqual(r["output_tokens_naive"], 2500)
        self.assertEqual(r["output_tokens_corrected"], 2500)
        self.assertFalse(r["undercount_flagged"])

    def test_empty_or_missing_envelope_yields_zeros_and_exit_0(self):
        for path in (fixture("empty-run.agent.json"), fixture("does-not-exist.json")):
            r = run_cli(path)
            self.assertEqual(r["source"], "missing")
            self.assertEqual(r["output_tokens_corrected"], 0)
            self.assertEqual(r["output_tokens_naive"], 0)

    def test_exclude_pattern_is_configurable(self):
        r = run_cli(fixture("multi-model-run.agent.json"), "--exclude-model", "^$")
        self.assertEqual(r["output_tokens_corrected"], 6331 + 17981 + 17)
        self.assertEqual(r["models_excluded"], {})

    def test_concatenated_objects_take_the_last_result_envelope(self):
        env = {"usage": {"output_tokens": 5}, "modelUsage": {"m": {"outputTokens": 9}}}
        text = json.dumps({"type": "system"}) + "\n" + json.dumps(env) + "\n"
        path = os.path.join(HERE, ".concat.tmp.json")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text)
        try:
            r = helper.compute(helper.load_envelope(path))
        finally:
            os.remove(path)
        self.assertEqual(r["output_tokens_naive"], 5)
        self.assertEqual(r["output_tokens_corrected"], 9)


class ArchivedArtifact(unittest.TestCase):
    """The real run behind the issue, when the epoch archive is present."""

    @unittest.skipUnless(os.path.isfile(ARCHIVED_55X), "archived epoch not present")
    def test_w5_parity_002_n1_001_treatment_run_4(self):
        r = run_cli(ARCHIVED_55X)
        self.assertEqual(r["output_tokens_naive"], 543)
        self.assertEqual(r["output_tokens_corrected"], 30084)
        self.assertEqual(r["origin_kind"], "task-notification")


if __name__ == "__main__":
    unittest.main(verbosity=2)
