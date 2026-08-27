#!/usr/bin/env python3
"""Turn-gap attribution over the archived PP-E1 / PP-W5 epochs (v0.16 S1 step 3, W4, gate 12).

Reproduces N:S1.1 and N:S1.2 of `docs/plans/2026-08-27-v0.16-s1-s2-measurement-notes.md`
from committed artifacts only, and carries the per-turn tool-class table W4 will
populate once W1 archives `transcript.jsonl` per run. Nothing here is tuned to
the notes: every number is derived by the method stated below, and the tests
pin the derived numbers (a discrepancy against a published figure is reported
there, never fitted here).

Denominator (gate 12): EVERY entry under `bench/phase0-agent-native/epochs/`
(dot-entries such as `.DS_Store` excluded). An entry is ANALYZED when it is a
directory with a `pins.json` whose `kind` is a PP-E1 / PP-W5 kind and at
least one `<pair>/<arm>/run-*` directory; everything else is listed under
`skipped` by name with the reason, together with the number of runs there
that would have been shape-eligible (so the "other kinds" residue is
counted, not hidden). Within an analyzed epoch, a damaged run (no or invalid
`result.json`, missing keys, no `agent.json`) is skipped PER RUN under the
epoch's `skippedRuns`, never demoting the epoch. Deleting one archived run
changes the output (the discriminating pin).

Per run: `num_turns` and `duration_ms` from `agent.json`; `tokens.output`
naive (`usage.output_tokens`) and CORRECTED by token-usage.py's rule
(A-1.9.1: sum of modelUsage[*].outputTokens, side calls excluded); the
`result.json` figure alongside so the archive's own number is checked
against the derivation.

Per pair: median turns / wall-clock per arm, the per-pair median delta
(median treatment - median control), the sorted corrected token lists, and
the paired mean delta per metric.

Two permutation statistics, both stated (roadmap §0.2), labels permuted
WITHIN each pair, one-sided (P[permuted statistic >= observed]):
  * `pooledMeanDifference` - the reviewers' statistic: mean over all
    treatment runs minus mean over all control runs. Computed EXACTLY: the
    statistic is a strictly increasing function of the total treatment sum,
    so the distribution of that sum over every relabelling (the product of
    per-pair C(n, nT) choices) is built by convolving per-pair sum
    distributions and thresholded. No randomness, no seed.
  * `medianOverPairsOfPairedMeanDelta` - the notes' statistic: the median
    over pairs of (mean treatment - mean control) per pair; with an even
    number of pairs the median is the mean of the two middle deltas. Monte
    Carlo: a fresh `random.Random(SEED)` per metric, the label vector
    `[1]*nT + [0]*nC` shuffled per pair per draw (the scheme that produced
    the roadmap §0.2 figures at 20 000 draws), MC_DRAWS draws, with the
    binomial `standardError` emitted beside every p. The 4th decimal of a
    Monte-Carlo p is noise: cite the exact relabelling values as reference
    and read the MC ones within a few standard errors of them.

Census (N:S1.1): every `journal.jsonl` record with `cmd == "build"` is one
harness build; `edited` splits edited from unedited observation builds; the
`diagnostics[]` codes are the harness's own strict CLI compile of that source
state. Effect-family = any `Calor04xx` code; the named rows-era codes
(0410/0411/0419/0424/0425) are counted explicitly.

Sensitivity: the point estimate (median over pairs of mean treatment /
mean control corrected output tokens) under all-naive tokens, under a
single-run correction for every run whose corrected figure differs from the
naive one (each run alone), and registered (all corrected).

Per-turn table (W4): every analyzed run is tabulated from its
`transcript.jsonl` (Claude Code `--output-format stream-json`; one event per
content block; `type: "assistant"` events carry `message.id`), giving
`turns.assistantMessages` = distinct TOP-LEVEL assistant `message.id`
(`parent_tool_use_id` null - the A-1.12 field, independent of
`--forward-subagent-text`), `turns.subagentMessages` separately, and tool
calls by class Read / Grep / Bash-build / Bash-other / Edit / other. Runs
without a transcript are listed as `noTranscript` - never dropped.

Usage:
    ppe1-turn-attribution.py [--epochs-root DIR] [--out PATH]
    ppe1-turn-attribution.py --transcripts DIR [DIR ...] [--out PATH] [--markdown PATH]

Python 3.9 compatible; standard library only.
"""
import argparse
import bisect
import glob
import importlib.util
import itertools
import json
import math
import os
import random
import re
import statistics
import sys
from collections import Counter, OrderedDict

BENCH = os.path.dirname(os.path.abspath(__file__))
EPOCHS_ROOT = os.path.join(BENCH, "epochs")
DEFAULT_OUT = os.path.join(BENCH, "ppe1-turn-attribution.json")

REGISTERED_EPOCH = "e1-rows-parity-001"
ANALYZED_KINDS = ("pp-e1-rows-parity", "pp-w5-parity")
RESULT_KEYS = ("pair", "arm", "run", "tokens", "censored", "invalid")
SEED = 4537
MC_DRAWS = 100000   # 200 000 would take ~30 s per archive pass; 100 000 keeps a pass under ~20 s
METRICS = ("turns", "tokens", "wall")
EFFECT_FAMILY_PATTERN = re.compile(r"^Calor04\d\d$")
NAMED_EFFECT_CODES = ("Calor0410", "Calor0411", "Calor0419", "Calor0424", "Calor0425")

# W4 tool classes. Bash is split on the command text: a build is a `dotnet
# build|test|run` or a `calor` (or `dotnet .../calor.dll`) invocation at the
# start of the command or of a pipeline/list segment (after ; & | ( or a
# newline). `dotnet build-server shutdown`, `grep calor foo`, `echo 'calor '`
# and `escalor foo` are Bash-other; `calor` bare, `cd x && dotnet  test`,
# `dotnet run --project src/Calor.Compiler -- ...` and `dotnet ./calor.dll ...`
# are Bash-build. MCP compiles (`mcp__calor__*`) are not Bash: they land in
# `other` with the raw tool name preserved in `toolNames`.
BASH_BUILD_PATTERN = re.compile(
    r"(^|[;&|(]\s*)(dotnet\s+(build|test|run)(?![\w-])|dotnet\s+\S*calor\.dll\b|calor(?![\w-]))",
    re.MULTILINE)
TOOL_CLASSES = ("Read", "Grep", "Bash-build", "Bash-other", "Edit", "other")
TOOL_CLASS_OF = {
    "Read": "Read",
    "Grep": "Grep",
    "Glob": "Grep",
    "Edit": "Edit",
    "Write": "Edit",
    "MultiEdit": "Edit",
    "NotebookEdit": "Edit",
}
RUN_NUMBER_PATTERN = re.compile(r"(?:^|\D)(\d+)$")


def _load_token_usage():
    spec = importlib.util.spec_from_file_location(
        "token_usage", os.path.join(BENCH, "token-usage.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TOKEN_USAGE = _load_token_usage()


def _try_read_json(path):
    """(object, None) or (None, reason) - never raises on a damaged file."""
    try:
        with open(path, encoding="utf-8") as fh:
            return json.load(fh), None
    except OSError as exc:
        return None, f"{os.path.basename(path)} unreadable: {exc.strerror}"
    except ValueError as exc:
        return None, f"{os.path.basename(path)} is not valid JSON: {exc}"


def _short_pair(name):
    """`N1-001-string-utils` -> `N1-001` (the registered pair id)."""
    return "-".join(str(name).split("-")[:2])


def _run_number(value):
    """Normalise a run designator (7, "7", "run-7") to an int; None if it has no digits."""
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    match = RUN_NUMBER_PATTERN.search(str(value)) if value is not None else None
    return int(match.group(1)) if match else None


# ---------------------------------------------------------------------------
# Epoch enumeration (the gate-12 denominator)
# ---------------------------------------------------------------------------

def _run_dirs(epoch_dir):
    return sorted(p for p in glob.glob(os.path.join(epoch_dir, "*", "*", "run-*")) if os.path.isdir(p))


def _run_shape(run_dir):
    """(record, None) when the run has the PP-E1/W5 shape, else (None, reason)."""
    result_path = os.path.join(run_dir, "result.json")
    if not os.path.exists(result_path):
        return None, "no result.json"
    record, error = _try_read_json(result_path)
    if error:
        return None, error
    if not isinstance(record, dict):
        return None, "result.json is not an object"
    missing = [k for k in RESULT_KEYS if k not in record]
    if missing:
        return None, f"result.json lacks {missing} (not the PP-E1/W5 result shape)"
    if not os.path.exists(os.path.join(run_dir, "agent.json")):
        return None, "no agent.json"
    return record, None


def _shape_eligible_runs(epoch_dir):
    return sum(1 for run_dir in _run_dirs(epoch_dir) if _run_shape(run_dir)[1] is None)


def classify_epoch(epoch_dir):
    """Return (analyzable: bool, reason: str|None, pins: dict|None, shapeEligibleRuns: int)."""
    if not os.path.isdir(epoch_dir):
        return False, "not a directory (driver log or stray file)", None, 0
    pins_path = os.path.join(epoch_dir, "pins.json")
    if not os.path.exists(pins_path):
        return False, "no pins.json (not a harness epoch)", None, _shape_eligible_runs(epoch_dir)
    pins, error = _try_read_json(pins_path)
    if error or not isinstance(pins, dict):
        return False, error or "pins.json is not an object", None, _shape_eligible_runs(epoch_dir)
    kind = pins.get("kind")
    if kind not in ANALYZED_KINDS:
        return (False, f"pins.json kind={kind!r} is not a PP-E1/W5 epoch kind {list(ANALYZED_KINDS)}",
                pins, _shape_eligible_runs(epoch_dir))
    if not _run_dirs(epoch_dir):
        return False, "no <pair>/<arm>/run-* directory under the epoch", pins, 0
    return True, None, pins, 0


# ---------------------------------------------------------------------------
# Per-run collection
# ---------------------------------------------------------------------------

def collect_runs(epoch_dir, pins):
    """(runs, skippedRuns) - a damaged run is listed with its reason, never dropped."""
    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    roles = {arm_a.get("label"): "control", arm_b.get("label"): "treatment"}
    runs, skipped = [], []
    for run_dir in _run_dirs(epoch_dir):
        rel = os.path.relpath(run_dir, epoch_dir).replace(os.sep, "/")
        record, reason = _run_shape(run_dir)
        if reason:
            skipped.append(OrderedDict([("directory", rel), ("reason", reason)]))
            continue
        envelope = TOKEN_USAGE.load_envelope(os.path.join(run_dir, "agent.json"))
        usage = TOKEN_USAGE.compute(envelope)
        env = envelope if isinstance(envelope, dict) else {}
        duration = env.get("duration_ms")
        if not isinstance(duration, (int, float)) or isinstance(duration, bool):
            duration = None
        result_tokens = (record.get("tokens") or {}).get("output") if isinstance(record.get("tokens"), dict) else None
        runs.append(OrderedDict([
            ("pair", _short_pair(record["pair"])),
            ("pairDirectory", str(record["pair"])),
            ("arm", str(record["arm"])),
            ("role", roles.get(record["arm"], "unattributed")),
            ("run", _run_number(record["run"]) if _run_number(record["run"]) is not None
             else _run_number(os.path.basename(run_dir))),
            ("invalid", bool(record.get("invalid", False))),
            ("censored", bool(record.get("censored", False))),
            ("numTurns", usage["num_turns"]),
            ("durationMs", duration),
            ("wallClockSeconds", None if duration is None else round(duration / 1000, 1)),
            ("tokens", OrderedDict([
                ("outputNaive", usage["output_tokens_naive"]),
                ("outputCorrected", usage["output_tokens_corrected"]),
                ("source", usage["source"]),
                ("resultJsonOutput", result_tokens),
                ("resultJsonAgreesWithCorrected",
                 result_tokens == usage["output_tokens_corrected"]),
                ("undercountFlagged", usage["undercount_flagged"]),
            ])),
            ("totalCostUsd", env.get("total_cost_usd")),
            ("iterationsToGreen", record.get("iterationsToGreen")),
            ("transcript", "present" if os.path.exists(os.path.join(run_dir, "transcript.jsonl"))
             else "noTranscript"),
            ("directory", rel),
        ]))
    return runs, skipped


def _metric_value(run, metric):
    if metric == "turns":
        return run["numTurns"]
    if metric == "tokens":
        return None if run["tokens"]["source"] == "missing" else run["tokens"]["outputCorrected"]
    if metric == "wall":
        return run["durationMs"]
    raise ValueError(metric)


def _cells(runs, metric):
    """{pair: {"treatment": [...], "control": [...]}} over valid, attributed runs with a value."""
    cells = OrderedDict()
    for run in runs:
        if run["invalid"] or run["role"] == "unattributed":
            continue
        value = _metric_value(run, metric)
        if value is None:
            continue
        cells.setdefault(run["pair"], {"treatment": [], "control": []})[run["role"]].append(value)
    return OrderedDict((p, c) for p, c in sorted(cells.items())
                       if c["treatment"] and c["control"])


def _cell_sizes(cells):
    return OrderedDict([
        ("treatment", sum(len(c["treatment"]) for c in cells.values())),
        ("control", sum(len(c["control"]) for c in cells.values())),
    ])


# ---------------------------------------------------------------------------
# Statistics
# ---------------------------------------------------------------------------

def stat_pooled_mean_difference(cells):
    treatment = [v for c in cells.values() for v in c["treatment"]]
    control = [v for c in cells.values() for v in c["control"]]
    return statistics.mean(treatment) - statistics.mean(control)


def stat_median_over_pairs(cells):
    return statistics.median([statistics.mean(c["treatment"]) - statistics.mean(c["control"])
                              for c in cells.values()])


def _convolve(counters):
    total = Counter({0: 1})
    for counter in counters:
        merged = Counter()
        for a, ca in total.items():
            for b, cb in counter.items():
                merged[a + b] += ca * cb
        total = merged
    return total


def exact_pooled_p(cells):
    """Exact one-sided p of the pooled mean difference over every within-pair relabelling.

    pooled = S/NT - (TOTAL - S)/NC is strictly increasing in S, the total
    treatment sum, so P[stat >= observed] = P[S >= S_observed]. S is the sum of
    independent per-pair treatment sums, each uniform over the C(n, nT)
    subsets; their distributions are convolved in two halves and matched with
    a bisect so nothing near C(n, nT)^pairs is ever enumerated."""
    pairs = list(cells)
    per_pair = []
    s_obs = 0
    relabellings = 1
    for p in pairs:
        pool = cells[p]["treatment"] + cells[p]["control"]
        n_t = len(cells[p]["treatment"])
        sums = Counter(sum(pool[i] for i in combo) for combo in itertools.combinations(range(len(pool)), n_t))
        per_pair.append(sums)
        s_obs += sum(cells[p]["treatment"])
        relabellings *= math.comb(len(pool), n_t)
    # `bisect_left` + suffix sum counts S >= s_obs EXACTLY only because every
    # metric here is integer-valued (num_turns, output tokens, duration_ms), so
    # the per-pair sums and s_obs are ints and equality is exact. A future
    # float-valued metric would lose ties to representation error at the
    # boundary; add a `- 1e-9` slack to the search key (deliberately counting
    # near-ties as hits, the conservative direction for a one-sided p) if one
    # is ever added.
    half = len(per_pair) // 2
    left = _convolve(per_pair[:half]) if half else Counter({0: 1})
    right = _convolve(per_pair[half:])
    right_keys = sorted(right)
    suffix = [0] * (len(right_keys) + 1)
    for i in range(len(right_keys) - 1, -1, -1):
        suffix[i] = suffix[i + 1] + right[right_keys[i]]
    hits = 0
    for a, ca in left.items():
        idx = bisect.bisect_left(right_keys, s_obs - a)
        hits += ca * suffix[idx]
    return OrderedDict([
        ("observed", round(stat_pooled_mean_difference(cells), 4)),
        ("p", round(hits / relabellings, 6)),
        ("pExactFraction", f"{hits}/{relabellings}"),
        ("relabellings", relabellings),
        ("pairs", len(pairs)),
        ("n", _cell_sizes(cells)),
    ])


def monte_carlo_median_over_pairs_p(cells, draws=MC_DRAWS, seed=SEED):
    """One-sided MC p of the median-over-pairs statistic, label-vector shuffle per pair per draw."""
    rng = random.Random(seed)
    observed = stat_median_over_pairs(cells)
    pairs = list(cells)
    pools = [cells[p]["treatment"] + cells[p]["control"] for p in pairs]
    labels = [[1] * len(cells[p]["treatment"]) + [0] * len(cells[p]["control"]) for p in pairs]
    # Verified: at 20 000 draws this scheme (pool = treatment + control, label
    # vector rebuilt per draw, fresh rng per metric, >=) reproduces roadmap
    # §0.2's 0.0037 / 0.0249 / 0.4375 for e1-rows-parity-001.
    n_t = [len(cells[p]["treatment"]) for p in pairs]
    n_c = [len(cells[p]["control"]) for p in pairs]
    totals = [sum(pool) for pool in pools]
    hits = 0
    for _ in range(draws):
        deltas = []
        for k, pool in enumerate(pools):
            lab = labels[k][:]          # rebuilt [1]*nT + [0]*nC every draw (the roadmap's scheme)
            rng.shuffle(lab)
            s = sum(itertools.compress(pool, lab))
            deltas.append(s / n_t[k] - (totals[k] - s) / n_c[k])
        if statistics.median(deltas) >= observed:
            hits += 1
    p = hits / draws
    return OrderedDict([
        ("observed", round(observed, 4)),
        ("p", round(p, 4)),
        ("hits", hits),
        ("draws", draws),
        ("standardError", round(math.sqrt(p * (1 - p) / draws), 6)),
        ("pairs", len(pairs)),
        ("n", _cell_sizes(cells)),
    ])


def permutation_tests(runs):
    pooled = OrderedDict()
    median = OrderedDict()
    for metric in METRICS:
        cells = _cells(runs, metric)
        pooled[metric] = exact_pooled_p(cells) if cells else None
        median[metric] = monte_carlo_median_over_pairs_p(cells) if cells else None
    return OrderedDict([
        ("pooledMeanDifference", OrderedDict([
            ("method", "exact: every within-pair relabelling enumerated through the total "
                       "treatment sum (strictly monotone in the statistic); one-sided "
                       "P[permuted >= observed]"),
            ("byMetric", pooled),
        ])),
        ("medianOverPairsOfPairedMeanDelta", OrderedDict([
            ("method", "Monte Carlo: fresh random.Random(seed) per metric; per draw, per pair, "
                       "random.shuffle of the label vector [1]*nT + [0]*nC; one-sided "
                       "P[permuted >= observed]; the 4th decimal is noise - read p with "
                       "standardError"),
            ("seed", SEED),
            ("draws", MC_DRAWS),
            ("byMetric", median),
        ])),
    ])


def per_pair_summary(runs):
    summary = []
    turns_cells = _cells(runs, "turns")
    tokens_cells = _cells(runs, "tokens")
    wall_cells = _cells(runs, "wall")
    for pair in sorted(set(turns_cells) | set(tokens_cells) | set(wall_cells)):
        entry = OrderedDict([("pair", pair)])
        if pair in turns_cells:
            t, c = turns_cells[pair]["treatment"], turns_cells[pair]["control"]
            mt, mc = statistics.median(t), statistics.median(c)
            entry["turns"] = OrderedDict([
                ("medianTreatment", mt), ("medianControl", mc),
                ("medianDelta", mt - mc),
                ("pairedMeanDelta", round(statistics.mean(t) - statistics.mean(c), 4)),
                ("treatmentSorted", sorted(t)), ("controlSorted", sorted(c)),
            ])
        if pair in wall_cells:
            t, c = wall_cells[pair]["treatment"], wall_cells[pair]["control"]
            entry["wallClock"] = OrderedDict([
                ("medianTreatmentSeconds", round(statistics.median(t) / 1000)),
                ("medianControlSeconds", round(statistics.median(c) / 1000)),
                ("pairedMeanDeltaMs", round(statistics.mean(t) - statistics.mean(c), 4)),
                ("treatmentSortedMs", sorted(t)), ("controlSortedMs", sorted(c)),
            ])
        if pair in tokens_cells:
            t, c = tokens_cells[pair]["treatment"], tokens_cells[pair]["control"]
            entry["tokens"] = OrderedDict([
                ("meanTreatment", round(statistics.mean(t), 2)),
                ("meanControl", round(statistics.mean(c), 2)),
                ("ratio", round(statistics.mean(t) / statistics.mean(c), 4)
                 if statistics.mean(c) else None),
                ("pairedMeanDelta", round(statistics.mean(t) - statistics.mean(c), 4)),
                ("treatmentSorted", sorted(t)), ("controlSorted", sorted(c)),
            ])
        summary.append(entry)
    return summary


# ---------------------------------------------------------------------------
# Census (N:S1.1) and sensitivity (N:S1.2)
# ---------------------------------------------------------------------------

def compile_census(epoch_dir, runs):
    per_arm = OrderedDict()
    for run in runs:
        arm = per_arm.setdefault(run["arm"], OrderedDict([
            ("role", run["role"]), ("builds", 0), ("editedBuilds", 0),
            ("uneditedObservationBuilds", 0), ("codes", Counter()),
            ("effectFamilyDiagnostics", 0), ("journals", 0), ("missingJournals", 0),
            ("unparsableJournalLines", 0),
        ]))
        journal = os.path.join(epoch_dir, run["directory"], "journal.jsonl")
        if not os.path.exists(journal):
            arm["missingJournals"] += 1
            continue
        arm["journals"] += 1
        with open(journal, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    record = json.loads(line)
                except ValueError:
                    arm["unparsableJournalLines"] += 1
                    continue
                if not isinstance(record, dict) or record.get("cmd") != "build":
                    continue
                arm["builds"] += 1
                if record.get("edited"):
                    arm["editedBuilds"] += 1
                else:
                    arm["uneditedObservationBuilds"] += 1
                diagnostics = record.get("diagnostics")
                for diag in diagnostics if isinstance(diagnostics, list) else []:
                    code = str((diag.get("code") if isinstance(diag, dict) else None) or "<none>")
                    arm["codes"][code] += 1
                    if EFFECT_FAMILY_PATTERN.match(code):
                        arm["effectFamilyDiagnostics"] += 1
    for arm in per_arm.values():
        codes = arm["codes"]
        arm["codes"] = OrderedDict(sorted(codes.items()))
        arm["namedEffectCodes"] = OrderedDict((c, codes.get(c, 0)) for c in NAMED_EFFECT_CODES)
    return OrderedDict([
        ("source", "journal.jsonl records with cmd == \"build\"; diagnostics[] = the harness's "
                   "strict CLI compile of that source state (run-pair.sh:677-701)"),
        ("effectFamilyRule", "any Calor04xx code"),
        ("totalBuilds", sum(a["builds"] for a in per_arm.values())),
        ("byArm", per_arm),
    ])


def _point_estimate(tokens_by_pair):
    ratios = []
    for cell in tokens_by_pair.values():
        c = statistics.mean(cell["control"])
        if c > 0:
            ratios.append(statistics.mean(cell["treatment"]) / c)
    return round(statistics.median(ratios), 4) if ratios else None


def token_sensitivity(runs):
    valid = [r for r in runs if not r["invalid"] and r["role"] != "unattributed"
             and r["tokens"]["source"] != "missing"]

    def cells(pick):
        out = OrderedDict()
        for r in valid:
            out.setdefault(r["pair"], {"treatment": [], "control": []})[r["role"]].append(pick(r))
        return OrderedDict((p, c) for p, c in sorted(out.items()) if c["treatment"] and c["control"])

    undercounted = [r for r in valid if r["tokens"]["outputCorrected"] != r["tokens"]["outputNaive"]]
    single = []
    for target in undercounted:
        key = (target["pair"], target["arm"], target["run"])
        single.append(OrderedDict([
            ("run", f"{target['pair']}/{target['arm']}/run-{target['run']}"),
            ("naive", target["tokens"]["outputNaive"]),
            ("corrected", target["tokens"]["outputCorrected"]),
            ("pointEstimateCorrectingOnlyThisRun", _point_estimate(cells(
                lambda r, key=key: r["tokens"]["outputCorrected"]
                if (r["pair"], r["arm"], r["run"]) == key else r["tokens"]["outputNaive"]))),
        ]))
    costs = [r["totalCostUsd"] for r in valid
             if isinstance(r["totalCostUsd"], (int, float)) and not isinstance(r["totalCostUsd"], bool)]
    return OrderedDict([
        ("rule", "point = median over pairs of mean(treatment) / mean(control) output tokens"),
        ("allNaive", _point_estimate(cells(lambda r: r["tokens"]["outputNaive"]))),
        ("singleRunCorrections", single),
        ("registeredAllCorrected", _point_estimate(cells(lambda r: r["tokens"]["outputCorrected"]))),
        ("undercountedRuns", len(undercounted)),
        ("meanTotalCostUsdPerRun", round(statistics.mean(costs), 4) if costs else None),
        ("costRuns", len(costs)),
    ])


# ---------------------------------------------------------------------------
# W4: per-turn tool-class table from stream-json transcripts
# ---------------------------------------------------------------------------

def classify_tool(name, tool_input):
    if name == "Bash":
        command = ""
        if isinstance(tool_input, dict):
            command = str(tool_input.get("command") or "")
        return "Bash-build" if BASH_BUILD_PATTERN.search(command) else "Bash-other"
    return TOOL_CLASS_OF.get(name, "other")


def _empty_tool_counts():
    return OrderedDict((c, 0) for c in TOOL_CLASSES)


def tabulate_transcript(path):
    """Tabulate one stream-json transcript. Returns an OrderedDict (never raises on bad lines)."""
    top_level_ids = OrderedDict()        # message.id -> True (insertion-ordered set)
    subagent_ids = OrderedDict()
    seen_tool_keys = set()
    tools = _empty_tool_counts()
    tool_names = Counter()
    events = 0
    unparsable = 0
    result_num_turns = None
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            events += 1
            try:
                event = json.loads(line)
            except ValueError:
                unparsable += 1
                continue
            if not isinstance(event, dict):
                unparsable += 1
                continue
            if event.get("type") == "result":
                num_turns = event.get("num_turns")
                if isinstance(num_turns, int) and not isinstance(num_turns, bool):
                    result_num_turns = num_turns
                continue
            if event.get("type") != "assistant":
                continue
            message = event.get("message")
            if not isinstance(message, dict):
                continue
            message_id = message.get("id")
            if message_id is None:
                message_id = event.get("uuid")
            subagent = bool(event.get("parent_tool_use_id"))
            if message_id is not None:
                (subagent_ids if subagent else top_level_ids)[message_id] = True
            content = message.get("content")
            for position, block in enumerate(content if isinstance(content, list) else []):
                if not isinstance(block, dict) or block.get("type") != "tool_use":
                    continue
                name = str(block.get("name") or "")
                tool_id = block.get("id")
                key = (("id", tool_id) if tool_id is not None
                       else ("event", message_id, position, name, json.dumps(block.get("input"), sort_keys=True)))
                if key in seen_tool_keys:
                    continue
                seen_tool_keys.add(key)
                tools[classify_tool(name, block.get("input"))] += 1
                tool_names[name] += 1
    return OrderedDict([
        ("transcript", "present"),
        ("events", events),
        ("unparsableLines", unparsable),
        ("empty", events == 0),
        ("turns", OrderedDict([
            ("assistantMessages", len(top_level_ids)),
            ("subagentMessages", len(subagent_ids)),
            ("resultNumTurns", result_num_turns),
        ])),
        ("toolCalls", tools),
        ("toolCallsTotal", sum(tools.values())),
        ("toolNames", OrderedDict(sorted(tool_names.items()))),
    ])


def _run_identity(run_dir):
    """(pairDirectory, arm, run:int|None) from result.json when readable, else from the path."""
    pair = os.path.basename(os.path.dirname(os.path.dirname(run_dir)))
    arm = os.path.basename(os.path.dirname(run_dir))
    run = _run_number(os.path.basename(run_dir))
    record, error = _try_read_json(os.path.join(run_dir, "result.json"))
    if error is None and isinstance(record, dict):
        pair = str(record.get("pair") or pair)
        arm = str(record.get("arm") or arm)
        run = _run_number(record.get("run")) if _run_number(record.get("run")) is not None else run
    return pair, arm, run


def tabulate_runs(run_dirs, base=None):
    """Per-run and per-arm tool-class table over run directories."""
    per_run = []
    per_arm = OrderedDict()
    for run_dir in run_dirs:
        pair_directory, arm, run = _run_identity(run_dir)
        transcript = os.path.join(run_dir, "transcript.jsonl")
        rel = os.path.relpath(run_dir, base).replace(os.sep, "/") if base else run_dir
        entry = OrderedDict([("pair", _short_pair(pair_directory)), ("pairDirectory", pair_directory),
                             ("arm", arm), ("run", run), ("directory", rel)])
        if os.path.exists(transcript):
            entry.update(tabulate_transcript(transcript))
        else:
            entry["transcript"] = "noTranscript"
        per_run.append(entry)
        arm_row = per_arm.setdefault(arm, OrderedDict([
            ("runs", 0), ("withTranscript", 0), ("noTranscript", 0),
            ("assistantMessages", 0), ("subagentMessages", 0),
            ("toolCalls", _empty_tool_counts()), ("toolCallsTotal", 0),
        ]))
        arm_row["runs"] += 1
        if entry["transcript"] == "present":
            arm_row["withTranscript"] += 1
            arm_row["assistantMessages"] += entry["turns"]["assistantMessages"]
            arm_row["subagentMessages"] += entry["turns"]["subagentMessages"]
            for cls in TOOL_CLASSES:
                arm_row["toolCalls"][cls] += entry["toolCalls"][cls]
            arm_row["toolCallsTotal"] += entry["toolCallsTotal"]
        else:
            arm_row["noTranscript"] += 1
    return OrderedDict([
        ("field", "turns.assistantMessages = distinct TOP-LEVEL assistant message.id "
                  "(parent_tool_use_id null; A-1.12); subagentMessages counted separately"),
        ("toolClasses", OrderedDict([
            ("Read", ["Read"]), ("Grep", ["Grep", "Glob"]),
            ("Bash-build", ["Bash whose input.command matches /" + BASH_BUILD_PATTERN.pattern
                            + "/ (MULTILINE)"]),
            ("Bash-other", ["every other Bash"]),
            ("Edit", ["Edit", "Write", "MultiEdit", "NotebookEdit"]),
            ("other", ["any other tool name (Agent/Task, mcp__calor__*, WebFetch, TodoWrite, ...); "
                       "the raw name is kept in toolNames"]),
        ])),
        ("runs", len(per_run)),
        ("withTranscript", sum(1 for r in per_run if r["transcript"] == "present")),
        ("noTranscript", [r["directory"] for r in per_run if r["transcript"] != "present"]),
        ("byArm", per_arm),
        ("byRun", per_run),
    ])


def render_markdown(table):
    lines = ["| arm | runs | with transcript | assistant messages | subagent messages | "
             + " | ".join(TOOL_CLASSES) + " | total tool calls |",
             "|---|---|---|---|---|" + "---|" * len(TOOL_CLASSES) + "---|"]
    for arm, row in table["byArm"].items():
        lines.append(f"| {arm} | {row['runs']} | {row['withTranscript']} | {row['assistantMessages']} | "
                     f"{row['subagentMessages']} | "
                     + " | ".join(str(row["toolCalls"][c]) for c in TOOL_CLASSES)
                     + f" | {row['toolCallsTotal']} |")
    lines.append("")
    lines.append("| run | assistant messages | " + " | ".join(TOOL_CLASSES) + " |")
    lines.append("|---|---|" + "---|" * len(TOOL_CLASSES))
    for run in table["byRun"]:
        if run["transcript"] != "present":
            lines.append(f"| {run['directory']} | noTranscript |" + " |" * len(TOOL_CLASSES))
        else:
            lines.append(f"| {run['directory']} | {run['turns']['assistantMessages']} | "
                         + " | ".join(str(run["toolCalls"][c]) for c in TOOL_CLASSES) + " |")
    if table["noTranscript"]:
        lines.append("")
        lines.append(f"{len(table['noTranscript'])} run(s) without transcript.jsonl (listed, not dropped).")
    return "\n".join(lines) + "\n"


def find_run_dirs(paths):
    """Every directory under the given paths holding result.json or transcript.jsonl.

    Nothing is pruned: a run nested below another directory that happens to
    carry a result.json is still found."""
    found = []
    for path in paths:
        path = os.path.abspath(path)
        if os.path.isfile(path):
            path = os.path.dirname(path)
        for root, dirs, files in os.walk(path):
            dirs.sort()
            if "result.json" in files or "transcript.jsonl" in files:
                found.append(root)
    return sorted(dict.fromkeys(found))


# ---------------------------------------------------------------------------
# Whole-archive attribution
# ---------------------------------------------------------------------------

def analyze_epoch(epoch_dir, pins):
    runs, skipped_runs = collect_runs(epoch_dir, pins)
    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    return OrderedDict([
        ("epoch", os.path.basename(epoch_dir)),
        ("kind", pins.get("kind")),
        ("mode", pins.get("mode")),
        ("modelPin", pins.get("modelPin")),
        ("control", OrderedDict([("label", arm_a.get("label")), ("commit", arm_a.get("commit"))])),
        ("treatment", OrderedDict([("label", arm_b.get("label")), ("commit", arm_b.get("commit"))])),
        ("runs", len(runs)),
        ("skippedRuns", skipped_runs),
        ("validRuns", OrderedDict([
            (role, sum(1 for r in runs if r["role"] == role and not r["invalid"]))
            for role in ("control", "treatment")])),
        ("invalidRuns", sum(1 for r in runs if r["invalid"])),
        ("unattributedRuns", sum(1 for r in runs if r["role"] == "unattributed")),
        ("runsWithoutNumTurns", [r["directory"] for r in runs if r["numTurns"] is None]),
        ("tokenSources", OrderedDict(sorted(Counter(r["tokens"]["source"] for r in runs).items()))),
        ("resultJsonTokenDisagreements",
         [r["directory"] for r in runs if not r["tokens"]["resultJsonAgreesWithCorrected"]]),
        ("perPair", per_pair_summary(runs)),
        ("permutation", permutation_tests(runs)),
        ("compileCensus", compile_census(epoch_dir, runs)),
        ("tokenSensitivity", token_sensitivity(runs)),
        ("perTurn", tabulate_runs([os.path.join(epoch_dir, r["directory"]) for r in runs],
                                  base=epoch_dir)),
        ("perRun", runs),
    ])


def attribute(epochs_root):
    analyzed = []
    skipped = []
    for name in sorted(os.listdir(epochs_root)):
        if name.startswith("."):
            continue   # .DS_Store and friends are not archive entries
        epoch_dir = os.path.join(epochs_root, name)
        ok, reason, pins, eligible = classify_epoch(epoch_dir)
        if not ok:
            skipped.append(OrderedDict([("epoch", name), ("reason", reason),
                                        ("shapeEligibleRuns", eligible)]))
            continue
        analyzed.append(analyze_epoch(epoch_dir, pins))
    return OrderedDict([
        ("instrument", "bench/phase0-agent-native/ppe1-turn-attribution.py (v0.16 S1 step 3 / W4; gate 12)"),
        ("registeredEpoch", REGISTERED_EPOCH),
        ("denominator", "every non-dot entry under bench/phase0-agent-native/epochs/, attributed by "
                        "pins.json kind; shape-eligible runs of other kinds are counted, not attributed"),
        ("analyzedKinds", list(ANALYZED_KINDS)),
        ("tokenRule", "token-usage.py output_tokens_corrected (A-1.9.1)"),
        ("statistics", OrderedDict([
            ("pooledMeanDifference", "mean(all treatment runs) - mean(all control runs); exact over "
                                     "every within-pair relabelling"),
            ("medianOverPairsOfPairedMeanDelta",
             "median over pairs of (mean treatment - mean control); even pair count -> mean of "
             f"the two middle deltas; Monte Carlo, {MC_DRAWS} label-vector shuffles per metric, seed {SEED}"),
        ])),
        ("entries", len(analyzed) + len(skipped)),
        ("analyzedEpochs", [e["epoch"] for e in analyzed]),
        ("analyzedRuns", sum(e["runs"] for e in analyzed)),
        ("skippedRunsWithinAnalyzedEpochs", sum(len(e["skippedRuns"]) for e in analyzed)),
        ("shapeEligibleRunsNotAttributed", sum(s["shapeEligibleRuns"] for s in skipped)),
        ("skipped", skipped),
        ("epochs", analyzed),
    ])


def _print_summary(report):
    def fmt(value, spec=""):
        return "n/a" if value is None else format(value, spec)
    for epoch in report["epochs"]:
        print(f"=== {epoch['epoch']} ({epoch['kind']}) — control {epoch['control']['label']}, "
              f"treatment {epoch['treatment']['label']}; {epoch['runs']} runs, "
              f"{len(epoch['skippedRuns'])} skipped ===")
        for pair in epoch["perPair"]:
            t = pair.get("turns") or {}
            w = pair.get("wallClock") or {}
            print(f"  {pair['pair']}: median turns T/C {fmt(t.get('medianTreatment'))}/"
                  f"{fmt(t.get('medianControl'))} (Δ {fmt(t.get('medianDelta'), '+')}), median wall T/C "
                  f"{fmt(w.get('medianTreatmentSeconds'))}/{fmt(w.get('medianControlSeconds'))} s")
        for name, stat in epoch["permutation"].items():
            by = stat["byMetric"]
            print(f"  {name}: " + ", ".join(
                f"{m} p={by[m]['p']}" + (f" ±{by[m]['standardError']}" if by[m] and 'standardError' in by[m] else "")
                if by[m] else f"{m} n/a" for m in METRICS))
        census = epoch["compileCensus"]
        for arm, row in census["byArm"].items():
            print(f"  builds {arm}: {row['builds']} ({row['editedBuilds']} edited / "
                  f"{row['uneditedObservationBuilds']} unedited), codes {dict(row['codes'])}, "
                  f"effect-family {row['effectFamilyDiagnostics']}")
        sens = epoch["tokenSensitivity"]
        print(f"  sensitivity: all-naive {sens['allNaive']}, single-run "
              f"{[(s['run'], s['pointEstimateCorrectingOnlyThisRun']) for s in sens['singleRunCorrections']]}, "
              f"registered {sens['registeredAllCorrected']}; mean cost/run ${sens['meanTotalCostUsdPerRun']}")
        print(f"  per-turn: {epoch['perTurn']['withTranscript']} with transcript, "
              f"{len(epoch['perTurn']['noTranscript'])} noTranscript")
    for s in report["skipped"]:
        print(f"skipped {s['epoch']}: {s['reason']} (shape-eligible runs: {s['shapeEligibleRuns']})")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--epochs-root", default=EPOCHS_ROOT)
    parser.add_argument("--out", default=None)
    parser.add_argument("--transcripts", nargs="+", metavar="DIR",
                        help="tabulate the per-turn tool-class table over these epoch/run dirs")
    parser.add_argument("--markdown", default=None,
                        help="with --transcripts: also write the markdown rendering here")
    args = parser.parse_args(argv)

    if args.transcripts:
        run_dirs = find_run_dirs(args.transcripts)
        base = os.path.commonpath([os.path.abspath(p) for p in args.transcripts]) if run_dirs else None
        if base and os.path.isfile(base):
            base = os.path.dirname(base)
        table = tabulate_runs(run_dirs, base=base)
        text = json.dumps(table, indent=2) + "\n"
        if args.out:
            with open(args.out, "w", encoding="utf-8") as fh:
                fh.write(text)
        else:
            sys.stdout.write(text)
        markdown = render_markdown(table)
        if args.markdown:
            with open(args.markdown, "w", encoding="utf-8") as fh:
                fh.write(markdown)
        else:
            sys.stdout.write(markdown)
        return 0

    report = attribute(args.epochs_root)
    out = args.out or DEFAULT_OUT
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
        fh.write("\n")
    _print_summary(report)
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
