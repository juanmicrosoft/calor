"""Reproduce the prospective planning baseline without inference or outcome selection."""

import argparse
from collections import defaultdict
from decimal import Decimal, ROUND_CEILING
import hashlib
import json
from pathlib import Path
import subprocess


def money(value):
    return str(value.quantize(Decimal("0.01"), rounding=ROUND_CEILING))


def project(repo):
    base = Path("bench/phase0-agent-native/epochs/w-rows-dry-002")
    files = sorted((repo / base).glob("*/*/run-*/agent.json"))
    if len(files) != 18:
        raise ValueError("The historical calibration requires exactly 18 envelopes")
    cells = defaultdict(list)
    models = defaultdict(lambda: defaultdict(Decimal))
    inputs = []
    durations = []
    costs = []
    for path in files:
        raw = path.read_bytes()
        data = json.loads(raw, parse_float=Decimal)
        cost = Decimal(data["total_cost_usd"])
        if not cost.is_finite() or cost <= 0 or data["duration_ms"] <= 0:
            raise ValueError("A historical calibration record is missing usable cost/duration")
        relative = path.relative_to(repo)
        cell = str(relative.parent.parent.relative_to(base))
        cells[cell].append(cost)
        costs.append(cost)
        durations.append(Decimal(data["duration_ms"]))
        inputs.append({"path": relative.as_posix(), "sha256": hashlib.sha256(raw).hexdigest()})
        for model, usage in data["modelUsage"].items():
            if model not in {"claude-opus-4-8", "claude-haiku-4-5-20251001"}:
                raise ValueError("An unexpected historical model requires explicit review")
            for field in ("inputTokens", "cacheCreationInputTokens", "cacheReadInputTokens", "outputTokens"):
                if type(usage[field]) is not int or usage[field] < 0:
                    raise ValueError("Historical model token categories must be nonnegative integers")
                models[model][field] += usage[field]
            models[model]["reportedCostUsd"] += Decimal(usage["costUSD"])
    if len(cells) != 6 or any(len(values) != 3 for values in cells.values()):
        raise ValueError("The historical calibration must have six equally sampled cells")
    total = sum(costs)
    model_total = sum(values["reportedCostUsd"] for values in models.values())
    if abs(total - model_total) > Decimal("0.000001"):
        raise ValueError("Model and envelope cost totals do not reconcile")
    baseline = Decimal(74) * sum(sum(values) / len(values) for values in cells.values())
    ceiling = Decimal(1000)
    commit = subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True
    ).strip()
    return {
        "schemaVersion": 1,
        "kind": "pp-w-prospective-cost-forecast",
        "reviewStatus": "pending-independent-method-review",
        "authorRole": "coordinating agent; independent of gateway implementation; not human signoff",
        "forecast": {
            "status": "proposed",
            "plannedInvocations": 444,
            "experimentalObservations": 0,
            "estimatedFullPilotUsd": money(baseline),
            "method": (
                "Historical-traffic planning baseline: 74 times the sum of six historical "
                "task/arm cell means, each based on three cost-bearing dry-002 runs. "
                "The historical sample is not pooled with the redesigned pilot. No cost "
                "reduction is credited for smaller redesigned tasks. This is a conditional "
                "planning estimate, not an established new-task mean, provider quote, "
                "predictive interval, or completion guarantee."
            ),
            "completionGuaranteed": False,
            "numericalInterruptionProbability": None,
            "ceilingUsd": "1000.00",
            "baselineHeadroomUsd": money(ceiling - Decimal(money(baseline))),
            "scope": "The 444-slot pilot draws from one total experiment ceiling; no per-stage reset.",
            "sensitivities": [
                {
                    "name": "same historical traffic with 1.1 pricing multiplier",
                    "fullPilotUsd": money(baseline * Decimal("1.1")),
                    "interpretation": "Pricing sensitivity only; not an assertion of actual residency settings.",
                },
                {
                    "name": "25 percent more dollar-weighted traffic",
                    "fullPilotUsd": money(baseline * Decimal("1.25")),
                    "interpretation": "Illustrative stress scenario, not a predicted frequency or upper bound.",
                },
            ],
            "limitations": [
                "The historical client is 2.1.252; the redesigned client remains pinned to 2.1.266.",
                "The old tasks and two older compiler products differ from the three redesigned tasks and shared v0.18.0 compiler.",
                "The redesigned tasks have smaller specs and source, but continuation, reasoning and retry costs are unmeasured.",
                "A prepared first-request token count cannot establish the cost of all later requests.",
                "The calibration includes observed auxiliary-model costs, but does not bound future retries, helpers or retained unknown liabilities.",
                "Outstanding worst-case reservations and retained unknown liabilities can stop collection before charged usage reaches the ceiling.",
                "Provider pricing and supported request classes must match the independently reviewed gateway price contract.",
                "The old dry-001 zero-cost invalid/censored starts are not treated as cheap completed runs.",
            ],
            "budgetExhaustion": (
                "Stop before an unaffordable request. Preserve all attempted, interrupted "
                "and unstarted slots. Record INCOMPLETE_BUDGET, not a pilot null, completed "
                "fixed-allocation estimate or scientific stopping verdict. No replacements "
                "or sample-size reduction."
            ),
            "stage2": (
                "No automatic collection. Use actual pilot evidence for prospective effect-size, "
                "sample-size and affordability decisions. Any eligible stage must use only the "
                "remaining balance of the same $1,000 ceiling; do not pool pilot observations."
            ),
        },
        "historicalEvidence": {
            "sourceGitCommit": commit,
            "envelopes": 18,
            "cells": 6,
            "runsPerCell": 3,
            "reportedTotalUsd": str(total),
            "reportedMeanUsd": str(total / 18),
            "unrounded444ProjectionUsd": str(baseline),
            "historicalMinRunUsd": str(min(costs)),
            "historicalMaxRunUsd": str(max(costs)),
            "meanAgentMinutes": str(sum(durations) / 18 / 60000),
            "serial444AgentHours": str(sum(durations) / 18 * 444 / 3600000),
            "models": {
                model: {
                    key: str(value) if key == "reportedCostUsd" else int(value)
                    for key, value in sorted(values.items())
                }
                for model, values in sorted(models.items())
            },
            "inputs": inputs,
        },
        "pricingReferences": [
            "https://platform.claude.com/docs/en/about-claude/pricing",
            "https://platform.claude.com/docs/en/models/opus-4-8/overview",
        ],
        "moneyRounding": "Planning amounts round upward to whole US cents; source totals retain parsed decimal precision.",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("repo", type=Path)
    args = parser.parse_args()
    print(json.dumps(project(args.repo.resolve()), indent=2, sort_keys=True))
