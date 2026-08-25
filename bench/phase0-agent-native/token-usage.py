#!/usr/bin/env python3
"""Corrected token accounting for a harness run's `agent.json` (#881).

`run-pair.sh` and `run-bundle.sh` capture the `claude --print --output-format
json` result envelope as `<run>/agent.json` and used to read the cost-leg
figure from `usage.output_tokens`. That field covers ONLY the final turn
of the top-level conversation: when the agent delegated to a subagent
(`origin.kind == "task-notification"`, `num_turns` 1, `duration_api_ms`
far above `duration_ms`) or resumed after compaction, everything before the
resumption is missing. The archived `w5-parity-002` N1-001 treatment run-4
recorded 543 tokens against 30,084 actually generated — a 55x under-count.

`modelUsage` in the same envelope is the per-model aggregate over the whole
run — main conversation, every subagent turn, and every compaction segment
— and its `costUSD` entries sum to `total_cost_usd`, the figure the harness
already trusts for cost. This helper is the ONE place the cost-leg token
figure is derived, so both shell runners agree.

Usage:
    token-usage.py <agent.json> [--exclude-model REGEX] [--flag-ratio R]

Prints one JSON object to stdout and always exits 0 (a missing or
unparseable envelope yields zeros with "source": "missing" — the runners
must never lose a run over accounting). Fields:

    output_tokens_naive       usage.output_tokens as recorded (the old metric)
    output_tokens_corrected   sum of modelUsage[*].outputTokens over counted models
    input_tokens_naive        usage.input_tokens as recorded
    input_tokens_corrected    sum of modelUsage[*].inputTokens over counted models
    output_tokens_all_models  unfiltered sum over every modelUsage entry
    models_counted            sorted model keys that contribute to *_corrected
    models_excluded           {model key: outputTokens} dropped by --exclude-model
    source                    "modelUsage" | "usage" (no modelUsage: naive is
                              the best available) | "missing"
    origin_kind               envelope origin.kind (e.g. "task-notification"
                              marks a subagent-delegated resumption) or null
    num_turns                 envelope num_turns or null
    undercount_ratio          corrected / naive (null when naive is 0)
    undercount_flagged        true when naive under-counts by more than
                              --flag-ratio (default 1.05) — the collection-time
                              guard the issue asked for

--exclude-model (default "haiku") drops the ~15-token topic-detector call
that Claude Code makes on a side model. An entry is dropped only when at
least one NON-matching model exists, so an epoch pinned to a matching
model still counts its own output.

Python 3.9 compatible; standard library only.
"""

import argparse
import json
import re
import sys

DEFAULT_EXCLUDE = "haiku"
DEFAULT_FLAG_RATIO = 1.05


def _int(value):
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def _empty(source):
    return {
        "output_tokens_naive": 0,
        "output_tokens_corrected": 0,
        "input_tokens_naive": 0,
        "input_tokens_corrected": 0,
        "output_tokens_all_models": 0,
        "models_counted": [],
        "models_excluded": {},
        "source": source,
        "origin_kind": None,
        "num_turns": None,
        "undercount_ratio": None,
        "undercount_flagged": False,
    }


def load_envelope(path):
    """Return the parsed envelope dict, or None when absent/empty/invalid.

    `run-bundle.sh` historically read the file with `jq -s`, so a file that
    holds several concatenated JSON objects is tolerated: the LAST object
    that carries `usage` or `modelUsage` wins (it is the final result
    envelope of the run).
    """
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except OSError:
        return None
    text = text.strip()
    if not text:
        return None
    decoder = json.JSONDecoder()
    idx = 0
    chosen = None
    while idx < len(text):
        try:
            obj, end = decoder.raw_decode(text, idx)
        except ValueError:
            return chosen
        if isinstance(obj, dict) and ("usage" in obj or "modelUsage" in obj):
            chosen = obj
        idx = end
        while idx < len(text) and text[idx].isspace():
            idx += 1
    return chosen


def compute(envelope, exclude_pattern=DEFAULT_EXCLUDE, flag_ratio=DEFAULT_FLAG_RATIO):
    """Pure computation over a parsed envelope (None = missing file)."""
    if not isinstance(envelope, dict):
        return _empty("missing")

    usage = envelope.get("usage") or {}
    if not isinstance(usage, dict):
        usage = {}
    naive_out = _int(usage.get("output_tokens"))
    naive_in = _int(usage.get("input_tokens"))

    origin = envelope.get("origin")
    origin_kind = origin.get("kind") if isinstance(origin, dict) else None
    num_turns = envelope.get("num_turns")
    num_turns = _int(num_turns) if num_turns is not None else None

    model_usage = envelope.get("modelUsage")
    entries = []
    if isinstance(model_usage, dict):
        for key, val in model_usage.items():
            if isinstance(val, dict):
                entries.append((str(key), _int(val.get("outputTokens")), _int(val.get("inputTokens"))))

    result = _empty("modelUsage" if entries else "usage")
    result["output_tokens_naive"] = naive_out
    result["input_tokens_naive"] = naive_in
    result["origin_kind"] = origin_kind
    result["num_turns"] = num_turns

    if not entries:
        result["output_tokens_corrected"] = naive_out
        result["input_tokens_corrected"] = naive_in
        result["output_tokens_all_models"] = naive_out
        return result

    pattern = re.compile(exclude_pattern) if exclude_pattern else None
    kept = [e for e in entries if not (pattern and pattern.search(e[0]))]
    if not kept:
        kept = entries  # every model matched: count everything rather than nothing
    kept_keys = {e[0] for e in kept}

    result["output_tokens_corrected"] = sum(e[1] for e in kept)
    result["input_tokens_corrected"] = sum(e[2] for e in kept)
    result["output_tokens_all_models"] = sum(e[1] for e in entries)
    result["models_counted"] = sorted(kept_keys)
    result["models_excluded"] = {e[0]: e[1] for e in sorted(entries) if e[0] not in kept_keys}

    corrected = result["output_tokens_corrected"]
    if naive_out > 0:
        ratio = corrected / naive_out
        result["undercount_ratio"] = round(ratio, 4)
        result["undercount_flagged"] = ratio > flag_ratio
    else:
        result["undercount_flagged"] = corrected > 0
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("agent_json", help="path to the run's agent.json envelope")
    parser.add_argument("--exclude-model", default=DEFAULT_EXCLUDE,
                        help="regex; matching modelUsage keys are dropped when a "
                             "non-matching model exists (default: %(default)s)")
    parser.add_argument("--flag-ratio", type=float, default=DEFAULT_FLAG_RATIO,
                        help="set undercount_flagged when corrected/naive exceeds "
                             "this (default: %(default)s)")
    args = parser.parse_args(argv)
    result = compute(load_envelope(args.agent_json), args.exclude_model, args.flag_ratio)
    json.dump(result, sys.stdout, sort_keys=True)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
