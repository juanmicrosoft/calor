#!/usr/bin/env python3
"""Turn-gap attribution over the archived PP-E1 / PP-W5 epochs (v0.16 S1 step 3, W4, gate 12).

Reproduces N:S1.1 and N:S1.2 of `docs/plans/2026-08-27-v0.16-s1-s2-measurement-notes.md`
from committed artifacts only, and carries the per-turn tool-class table W4 will
populate once W1 archives `transcript.jsonl` per run. Nothing here is tuned to
the notes: every number is derived by the method stated below, and the tests
pin the derived numbers (a discrepancy against the notes is reported there,
never fitted here).

Denominator (gate 12): EVERY entry under `bench/phase0-agent-native/epochs/`.
An entry is ANALYZED when it is a directory with `pins.json` whose `kind` is
one of the PP-E1 / PP-W5 kinds and whose run directories carry the
`result.json` shape (`pair`, `arm`, `run`, `tokens`, `censored`, `invalid`)
with a sibling `agent.json`; everything else is listed under `skipped` by name
with the reason. Deleting one archived run changes the output (the
discriminating pin).

Per run: `num_turns` and `duration_ms` from `agent.json`; `tokens.output`
naive (`usage.output_tokens`) and CORRECTED by token-usage.py's rule
(A-1.9.1: sum of modelUsage[*].outputTokens, side calls excluded); the
`result.json` figure alongside so the archive's own number is checked
against the derivation.

Per pair: median turns / wall-clock per arm, the per-pair median delta
(median treatment - median control), the sorted corrected token lists, and
the paired mean delta per metric.

Two permutation statistics, both stated (roadmap §0.2):
  * `pooledMeanDifference` - the reviewers' statistic: mean over all
    treatment runs minus mean over all control runs.
  * `medianOverPairsOfPairedMeanDelta` - this document's statistic: the
    median over pairs of (mean treatment - mean control) per pair; with an
    even number of pairs the median is the mean of the two middle deltas.
Both are one-sided (P[permuted statistic >= observed]), with labels
permuted WITHIN each pair (`random.shuffle` of the pooled cell, the first
|treatment| values relabelled treatment), 20 000 permutations, seed 4537.
One `random.Random(SEED)` per statistic, consumed in metric order turns ->
tokens -> wall, so each p-value is exactly reproducible.

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
`turns.assistantMessages` = distinct assistant `message.id`, and tool calls
by class Read / Grep / Bash-build / Bash-other / Edit / other. Runs without a
transcript are listed as `noTranscript` - never dropped.

Usage:
    ppe1-turn-attribution.py [--epochs-root DIR] [--out PATH]
    ppe1-turn-attribution.py --transcripts DIR [DIR ...] [--out PATH] [--markdown PATH]

Python 3.9 compatible; standard library only.
"""
import argparse
import glob
import importlib.util
import json
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
PERMUTATIONS = 20000
METRICS = ("turns", "tokens", "wall")
EFFECT_FAMILY_PATTERN = re.compile(r"^Calor04\d\d$")
NAMED_EFFECT_CODES = ("Calor0410", "Calor0411", "Calor0419", "Calor0424", "Calor0425")

# W4 tool classes. Bash is split on the command text; the pattern is the
# roadmap's, verbatim ("dotnet build|dotnet test|calor ").
BASH_BUILD_PATTERN = re.compile(r"dotnet build|dotnet test|calor ")
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


def _load_token_usage():
    spec = importlib.util.spec_from_file_location(
        "token_usage", os.path.join(BENCH, "token-usage.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TOKEN_USAGE = _load_token_usage()


def _read_json(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _short_pair(name):
    """`N1-001-string-utils` -> `N1-001` (the registered pair id)."""
    return "-".join(name.split("-")[:2])


# ---------------------------------------------------------------------------
# Epoch enumeration (the gate-12 denominator)
# ---------------------------------------------------------------------------

def _run_dirs(epoch_dir):
    return sorted(os.path.dirname(p) for p in
                  glob.glob(os.path.join(epoch_dir, "*", "*", "run-*", "result.json")))


def classify_epoch(epoch_dir):
    """Return (analyzable: bool, reason: str|None, pins: dict|None)."""
    if not os.path.isdir(epoch_dir):
        return False, "not a directory (driver log or stray file)", None
    pins_path = os.path.join(epoch_dir, "pins.json")
    if not os.path.exists(pins_path):
        return False, "no pins.json (not a harness epoch)", None
    pins = _read_json(pins_path)
    kind = pins.get("kind")
    if kind not in ANALYZED_KINDS:
        return False, f"pins.json kind={kind!r} is not a PP-E1/W5 epoch kind {list(ANALYZED_KINDS)}", pins
    runs = _run_dirs(epoch_dir)
    if not runs:
        return False, "no <pair>/<arm>/run-*/result.json under the epoch", pins
    for run_dir in runs:
        record = _read_json(os.path.join(run_dir, "result.json"))
        missing = [k for k in RESULT_KEYS if k not in record]
        if missing:
            return False, (f"{os.path.relpath(run_dir, epoch_dir)}/result.json lacks "
                           f"{missing} (not the PP-E1/W5 result shape)"), pins
        if not os.path.exists(os.path.join(run_dir, "agent.json")):
            return False, f"{os.path.relpath(run_dir, epoch_dir)} has no agent.json", pins
    return True, None, pins


# ---------------------------------------------------------------------------
# Per-run collection
# ---------------------------------------------------------------------------

def collect_runs(epoch_dir, pins):
    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    roles = {arm_a.get("label"): "control", arm_b.get("label"): "treatment"}
    runs = []
    for run_dir in _run_dirs(epoch_dir):
        record = _read_json(os.path.join(run_dir, "result.json"))
        agent_path = os.path.join(run_dir, "agent.json")
        envelope = TOKEN_USAGE.load_envelope(agent_path)
        usage = TOKEN_USAGE.compute(envelope)
        env = envelope if isinstance(envelope, dict) else {}
        duration = env.get("duration_ms")
        result_tokens = (record.get("tokens") or {}).get("output")
        runs.append(OrderedDict([
            ("pair", _short_pair(record["pair"])),
            ("pairDirectory", record["pair"]),
            ("arm", record["arm"]),
            ("role", roles.get(record["arm"], "unattributed")),
            ("run", record["run"]),
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
            ("directory", os.path.relpath(run_dir, epoch_dir).replace(os.sep, "/")),
        ]))
    return runs


def _metric_value(run, metric):
    if metric == "turns":
        return run["numTurns"]
    if metric == "tokens":
        return None if run["tokens"]["source"] == "missing" else run["tokens"]["outputCorrected"]
    if metric == "wall":
        return run["durationMs"]
    raise ValueError(metric)


def _cells(runs, metric):
    """{pair: {"treatment": [...], "control": [...]}} over valid, attributed runs."""
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


STATISTICS = OrderedDict([
    ("pooledMeanDifference", stat_pooled_mean_difference),
    ("medianOverPairsOfPairedMeanDelta", stat_median_over_pairs),
])


def permutation_p(cells, statistic, rng, permutations=PERMUTATIONS):
    """One-sided within-pair label permutation: P[stat(perm) >= stat(observed)]."""
    observed = statistic(cells)
    pairs = list(cells)
    pools = {p: cells[p]["treatment"] + cells[p]["control"] for p in pairs}
    sizes = {p: len(cells[p]["treatment"]) for p in pairs}
    hits = 0
    for _ in range(permutations):
        permuted = OrderedDict()
        for p in pairs:
            pool = list(pools[p])
            rng.shuffle(pool)
            permuted[p] = {"treatment": pool[:sizes[p]], "control": pool[sizes[p]:]}
        if statistic(permuted) >= observed:
            hits += 1
    return observed, hits / permutations


def permutation_tests(runs):
    out = OrderedDict()
    for name, statistic in STATISTICS.items():
        rng = random.Random(SEED)
        per_metric = OrderedDict()
        for metric in METRICS:
            cells = _cells(runs, metric)
            if len(cells) < 1:
                per_metric[metric] = None
                continue
            observed, p = permutation_p(cells, statistic, rng)
            per_metric[metric] = OrderedDict([
                ("observed", round(observed, 4)),
                ("p", round(p, 4)),
                ("pairs", len(cells)),
            ])
        out[name] = OrderedDict([
            ("seed", SEED),
            ("permutations", PERMUTATIONS),
            ("sided", "one-sided, P[permuted >= observed], labels permuted within pair"),
            ("rngConsumption", "one random.Random(seed) per statistic, metrics in order "
                               + " -> ".join(METRICS)),
            ("byMetric", per_metric),
        ])
    return out


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
                record = json.loads(line)
                if record.get("cmd") != "build":
                    continue
                arm["builds"] += 1
                if record.get("edited"):
                    arm["editedBuilds"] += 1
                else:
                    arm["uneditedObservationBuilds"] += 1
                for diag in record.get("diagnostics") or []:
                    code = diag.get("code")
                    arm["codes"][code] += 1
                    if code and EFFECT_FAMILY_PATTERN.match(code):
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
    costs = [r["totalCostUsd"] for r in valid if r["totalCostUsd"] is not None]
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
    message_ids = OrderedDict()          # message.id -> True (insertion-ordered set)
    subagent_ids = OrderedDict()
    seen_tool_ids = set()
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
                if isinstance(num_turns, int):
                    result_num_turns = num_turns
                continue
            if event.get("type") != "assistant":
                continue
            message = event.get("message") or {}
            message_id = message.get("id") if isinstance(message, dict) else None
            if message_id is None:
                message_id = event.get("uuid")
            if message_id is not None:
                message_ids[message_id] = True
                if event.get("parent_tool_use_id"):
                    subagent_ids[message_id] = True
            content = message.get("content") if isinstance(message, dict) else None
            for block in content if isinstance(content, list) else []:
                if not isinstance(block, dict) or block.get("type") != "tool_use":
                    continue
                tool_id = block.get("id")
                if tool_id is not None:
                    if tool_id in seen_tool_ids:
                        continue
                    seen_tool_ids.add(tool_id)
                name = str(block.get("name") or "")
                tools[classify_tool(name, block.get("input"))] += 1
                tool_names[name] += 1
    return OrderedDict([
        ("transcript", "present"),
        ("events", events),
        ("unparsableLines", unparsable),
        ("empty", events == 0),
        ("turns", OrderedDict([
            ("assistantMessages", len(message_ids)),
            ("subagentMessages", len(subagent_ids)),
            ("resultNumTurns", result_num_turns),
        ])),
        ("toolCalls", tools),
        ("toolCallsTotal", sum(tools.values())),
        ("toolNames", OrderedDict(sorted(tool_names.items()))),
    ])


def _run_identity(run_dir):
    """(pair, arm, run) from result.json when present, else from the path."""
    result_path = os.path.join(run_dir, "result.json")
    if os.path.exists(result_path):
        try:
            record = _read_json(result_path)
            return (str(record.get("pair") or os.path.basename(os.path.dirname(os.path.dirname(run_dir)))),
                    str(record.get("arm") or os.path.basename(os.path.dirname(run_dir))),
                    record.get("run"))
        except (ValueError, OSError):
            pass
    return (os.path.basename(os.path.dirname(os.path.dirname(run_dir))),
            os.path.basename(os.path.dirname(run_dir)),
            os.path.basename(run_dir))


def tabulate_runs(run_dirs, base=None):
    """Per-run and per-arm tool-class table over run directories."""
    per_run = []
    per_arm = OrderedDict()
    for run_dir in run_dirs:
        pair, arm, run = _run_identity(run_dir)
        transcript = os.path.join(run_dir, "transcript.jsonl")
        rel = os.path.relpath(run_dir, base).replace(os.sep, "/") if base else run_dir
        entry = OrderedDict([("pair", pair), ("arm", arm), ("run", run), ("directory", rel)])
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
        ("field", "turns.assistantMessages = distinct assistant message.id (A-1.12)"),
        ("toolClasses", OrderedDict([
            ("Read", ["Read"]), ("Grep", ["Grep", "Glob"]),
            ("Bash-build", ["Bash whose input.command matches /" + BASH_BUILD_PATTERN.pattern + "/"]),
            ("Bash-other", ["every other Bash"]),
            ("Edit", ["Edit", "Write", "MultiEdit", "NotebookEdit"]),
            ("other", ["any other tool name (Agent/Task, WebFetch, TodoWrite, ...)"]),
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
    """Run dirs under the given paths: any dir holding result.json or transcript.jsonl."""
    found = []
    for path in paths:
        path = os.path.abspath(path)
        if os.path.exists(os.path.join(path, "result.json")) or \
                os.path.exists(os.path.join(path, "transcript.jsonl")):
            found.append(path)
            continue
        for root, dirs, files in os.walk(path):
            dirs.sort()
            if "result.json" in files or "transcript.jsonl" in files:
                found.append(root)
                dirs[:] = []
    return sorted(dict.fromkeys(found))


# ---------------------------------------------------------------------------
# Whole-archive attribution
# ---------------------------------------------------------------------------

def analyze_epoch(epoch_dir, pins):
    runs = collect_runs(epoch_dir, pins)
    arm_a, arm_b = pins.get("armA") or {}, pins.get("armB") or {}
    return OrderedDict([
        ("epoch", os.path.basename(epoch_dir)),
        ("kind", pins.get("kind")),
        ("mode", pins.get("mode")),
        ("modelPin", pins.get("modelPin")),
        ("control", OrderedDict([("label", arm_a.get("label")), ("commit", arm_a.get("commit"))])),
        ("treatment", OrderedDict([("label", arm_b.get("label")), ("commit", arm_b.get("commit"))])),
        ("runs", len(runs)),
        ("validRuns", OrderedDict([
            (role, sum(1 for r in runs if r["role"] == role and not r["invalid"]))
            for role in ("control", "treatment")])),
        ("invalidRuns", sum(1 for r in runs if r["invalid"])),
        ("unattributedRuns", sum(1 for r in runs if r["role"] == "unattributed")),
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
        epoch_dir = os.path.join(epochs_root, name)
        ok, reason, pins = classify_epoch(epoch_dir)
        if not ok:
            skipped.append(OrderedDict([("epoch", name), ("reason", reason)]))
            continue
        analyzed.append(analyze_epoch(epoch_dir, pins))
    return OrderedDict([
        ("instrument", "bench/phase0-agent-native/ppe1-turn-attribution.py (v0.16 S1 step 3 / W4; gate 12)"),
        ("registeredEpoch", REGISTERED_EPOCH),
        ("denominator", "every entry under bench/phase0-agent-native/epochs/"),
        ("analyzedKinds", list(ANALYZED_KINDS)),
        ("tokenRule", "token-usage.py output_tokens_corrected (A-1.9.1)"),
        ("statistics", OrderedDict([
            ("pooledMeanDifference", "mean(all treatment runs) - mean(all control runs)"),
            ("medianOverPairsOfPairedMeanDelta",
             "median over pairs of (mean treatment - mean control); even pair count -> mean of "
             "the two middle deltas"),
            ("permutation", f"one-sided, within-pair label shuffle, {PERMUTATIONS} permutations, seed {SEED}"),
        ])),
        ("entries", len(analyzed) + len(skipped)),
        ("analyzedEpochs", [e["epoch"] for e in analyzed]),
        ("skipped", skipped),
        ("epochs", analyzed),
    ])


def _print_summary(report):
    for epoch in report["epochs"]:
        print(f"=== {epoch['epoch']} ({epoch['kind']}) — control {epoch['control']['label']}, "
              f"treatment {epoch['treatment']['label']}; {epoch['runs']} runs ===")
        for pair in epoch["perPair"]:
            t = pair.get("turns") or {}
            w = pair.get("wallClock") or {}
            print(f"  {pair['pair']}: median turns T/C {t.get('medianTreatment')}/{t.get('medianControl')} "
                  f"(Δ {t.get('medianDelta'):+}), median wall T/C {w.get('medianTreatmentSeconds')}/"
                  f"{w.get('medianControlSeconds')} s")
        for name, stat in epoch["permutation"].items():
            by = stat["byMetric"]
            print(f"  {name}: " + ", ".join(
                f"{m} p={by[m]['p']:.4f}" if by[m] else f"{m} n/a" for m in METRICS))
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
        print(f"skipped {s['epoch']}: {s['reason']}")


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
