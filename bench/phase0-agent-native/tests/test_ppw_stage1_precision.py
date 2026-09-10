"""Mathematical checks with synthetic in-memory counts; no pilot data or epoch."""

import importlib.util
import json
import math
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1] / "registrations/ppw-rows-stage1"
spec = importlib.util.spec_from_file_location("ppw_stage1_precision", ROOT / "precision.py")
precision = importlib.util.module_from_spec(spec)
spec.loader.exec_module(precision)


class Stage1PrecisionTests(unittest.TestCase):
    def test_registered_allocation_is_minimal_and_not_a_power_calculation(self):
        registration = json.loads((ROOT / "registration.json").read_text())
        calculated = precision.planned_precision()
        self.assertEqual(74, calculated["runsPerTaskPerArm"])
        self.assertEqual(74, registration["runsPerArm"])
        self.assertEqual(222, calculated["armASlots"])
        self.assertEqual(444, calculated["totalSlots"])
        self.assertGreater(math.sqrt(math.log(80) / (2 * 3 * 73)), 0.10)
        self.assertLessEqual(calculated["armAEscapeHalfWidth"], 0.10)
        self.assertAlmostEqual(0.09934500167282502, calculated["armAEscapeHalfWidth"])
        self.assertAlmostEqual(0.07024752435984348, calculated["shapeRealizationHalfWidth"])
        self.assertEqual([7], calculated["representedShapes"])
        self.assertFalse(calculated["experimentalModelInvoked"])
        self.assertFalse(calculated["spendAuthorized"])
        self.assertIsNone(registration["stage2Delta"])
        self.assertIsNone(registration["stage2N"])
        self.assertIsNone(registration["spendAuthorization"])
        self.assertFalse(registration["collectionAuthorized"])

    def test_weighted_heterogeneous_cells_match_the_analytic_bound(self):
        result = precision.fixed_mixture_band([(0, 74, 0), (37, 74, 0), (74, 74, 0)], 3)
        self.assertAlmostEqual(0.5, result["estimate"])
        self.assertEqual({"numerator": 1, "denominator": 2}, result["exactEstimate"])
        self.assertAlmostEqual(math.sqrt(math.log(80) / 444), result["halfWidth"])
        self.assertAlmostEqual(0.025, 2 * math.exp(-2 * result["halfWidth"]**2 * 222))

    def test_attrition_does_not_change_task_weights(self):
        result = precision.fixed_mixture_band([(1, 1, 0), (0, 74, 0), (0, 74, 0)], 3)
        self.assertAlmostEqual(1 / 3, result["estimate"])
        self.assertGreater(result["halfWidth"], 0.10)
        self.assertNotAlmostEqual(1 / 149, result["estimate"])

    def test_unknown_outcomes_are_not_zero_or_dropped(self):
        for cells in ([(0, 0, 0), (0, 74, 0), (0, 74, 0)],
                      [(0, 74, 1), (0, 74, 0), (0, 74, 0)]):
            result = precision.fixed_mixture_band(cells, 3)
            self.assertFalse(result["identified"])
            self.assertIsNone(result["estimate"])
            self.assertIsNone(result["exactEstimate"])
            self.assertIsNone(result["halfWidth"])
            self.assertEqual((0.0, 1.0), (result["lower"], result["upper"]))

    def test_probability_bands_are_clipped_without_changing_the_estimate(self):
        for successes in (0, 74):
            result = precision.fixed_mixture_band([(successes, 74, 0)] * 3, 3)
            self.assertEqual(successes / 74, result["estimate"])
            self.assertGreaterEqual(result["lower"], 0)
            self.assertLessEqual(result["upper"], 1)

    def test_half_threshold_is_exact_with_unequal_cell_denominators(self):
        result = precision.fixed_mixture_band([(1, 7, 0), (5, 7, 0), (9, 14, 0)], 3)
        self.assertEqual(0.5, result["estimate"])
        self.assertEqual({"numerator": 1, "denominator": 2}, result["exactEstimate"])

    def test_malformed_inventory_and_counts_are_rejected(self):
        for cells, expected in (([(0, 74, 0)], 3), ([], 0), ([(True, 74, 0)], 1),
                                ([(75, 74, 0)], 1), ([(74, 74, 1)], 1),
                                ([(0, -1, 0)], 1), ([(0, 74, 75)], 1)):
            with self.assertRaises(ValueError):
                precision.fixed_mixture_band(cells, expected)
        for count in (True, 0, 2, 13, 3.0):
            with self.assertRaises(ValueError):
                precision.minimum_runs_per_arm(count)
        for value in (0, 1, -1, math.nan, math.inf, True):
            with self.assertRaises(ValueError):
                precision.minimum_runs_per_arm(3, half_width=value)


if __name__ == "__main__":
    unittest.main()
