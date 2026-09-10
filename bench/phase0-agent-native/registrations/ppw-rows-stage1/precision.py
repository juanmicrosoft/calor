"""Prospective PP-W pilot sizing; no agent, epoch, ledger, or spending interface."""

import json
import math
from fractions import Fraction
from pathlib import Path


HERE = Path(__file__).resolve().parent


def minimum_runs_per_arm(task_count, half_width=0.10, family_error=0.05):
    if type(task_count) is not int or not 3 <= task_count <= 12:
        raise ValueError("Task count must be an integer from three through twelve")
    for value in (half_width, family_error):
        if type(value) not in (int, float) or not math.isfinite(value) or not 0 < value < 1:
            raise ValueError("Precision and error probabilities must lie strictly inside (0, 1)")
    per_estimand_error = family_error / 2
    return math.ceil(math.log(2 / per_estimand_error) / (2 * half_width**2 * task_count))


def fixed_mixture_band(cells, expected_cells, family_error=0.05):
    """Cells are (successes, eligible, unscorable); all cell weights stay equal."""
    if type(expected_cells) is not int or expected_cells < 1 or len(cells) != expected_cells:
        raise ValueError("A complete declared cell inventory is required")
    if (type(family_error) not in (int, float) or not math.isfinite(family_error)
            or not 0 < family_error < 1):
        raise ValueError("Family error probability must lie strictly inside (0, 1)")
    for successes, eligible, unscorable in cells:
        if any(type(value) is not int or value < 0 for value in (successes, eligible, unscorable)):
            raise ValueError("Counts must be nonnegative integers, not Boolean values")
        if unscorable > eligible or successes > eligible - unscorable:
            raise ValueError("Successes and unknown outcomes cannot exceed eligibility")
    if any(eligible == 0 or unscorable for _, eligible, unscorable in cells):
        return {"identified": False, "estimate": None, "exactEstimate": None, "halfWidth": None,
                "lower": 0.0, "upper": 1.0}
    weight = 1 / expected_cells
    exact = sum(Fraction(successes, eligible) for successes, eligible, _ in cells) / expected_cells
    estimate = float(exact)
    squared_ranges = math.fsum(weight**2 / eligible for _, eligible, _ in cells)
    half_width = math.sqrt(math.log(4 / family_error) * squared_ranges / 2)
    return {"identified": True, "estimate": estimate,
            "exactEstimate": {"numerator": exact.numerator, "denominator": exact.denominator},
            "halfWidth": half_width,
            "lower": max(0.0, estimate - half_width), "upper": min(1.0, estimate + half_width)}


def planned_precision():
    registration = json.loads((HERE / "registration.json").read_text())
    count = len(registration["tasks"])
    target = registration["precision"]["absoluteHalfWidthTarget"]
    error = registration["precision"]["familyErrorProbability"]
    runs = minimum_runs_per_arm(count, target, error)
    return {
        "kind": "prospective-sizing-calculation-not-observations",
        "tasks": count,
        "representedShapes": registration["representedShapes"],
        "runsPerTaskPerArm": runs,
        "armASlots": count * runs,
        "totalSlots": 2 * count * runs,
        "armAEscapeHalfWidth": math.sqrt(math.log(4 / error) / (2 * count * runs)),
        "shapeRealizationHalfWidth": math.sqrt(math.log(4 / error) / (4 * count * runs)),
        "experimentalModelInvoked": False,
        "spendAuthorized": False,
    }


if __name__ == "__main__":
    print(json.dumps(planned_precision(), indent=2))
