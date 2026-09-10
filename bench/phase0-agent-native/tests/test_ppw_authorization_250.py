"""Actual financial receipt and arithmetic; never invokes an experimental model."""
from decimal import Decimal
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import unittest


BENCH = Path(__file__).resolve().parents[1]
ROOT = BENCH / "registrations/ppw-rows-stage1/authorization-250"
QUOTE = ("I authorize the redesigned PP-W-rows [pilot only / entire two-stage study], "
         "with a total spending ceiling of $250. I accept the registered stopping rules "
         "and publication of negative or null results. Do not exceed this ceiling or "
         "weaken the protocol to finish within it.")


def load(path):
    return json.loads(path.read_text())


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class PilotAuthorization250Tests(unittest.TestCase):
    def test_exact_quote_and_parent_relay_are_preserved_without_selecting_the_placeholder(self):
        approval = load(ROOT / "authorization.json")
        self.assertEqual(QUOTE + "\n", (ROOT / "source-quote.txt").read_text())
        self.assertEqual(QUOTE, approval["source"]["exactQuote"])
        self.assertEqual(digest(ROOT / "source-quote.txt"), approval["source"]["quoteFileSha256"])
        self.assertFalse(approval["source"]["placeholderSelectedInOriginalQuote"])
        self.assertEqual("pilot-only", approval["scope"])
        self.assertIn("narrower", approval["scopeInterpretation"])
        self.assertIn("parent", approval["source"]["provenance"].lower())

    def test_real_approval_is_pilot_bound_total250_and_not_stage2_or_execution_admission(self):
        approval = load(ROOT / "authorization.json")
        self.assertEqual("pp-w-rows-spending-authorization", approval["kind"])
        self.assertEqual(("w-rows-pilot-001", "pilot"), (approval["epochId"], approval["stage"]))
        self.assertEqual("USD", approval["currency"])
        self.assertIs(type(approval["spendingCeilingUsd"]), int)
        self.assertEqual(250, approval["spendingCeilingUsd"])
        self.assertTrue(approval["financialAuthorizationGranted"])
        self.assertTrue(approval["nullResultAccepted"])
        self.assertTrue(approval["registeredStoppingRulesAccepted"])
        self.assertFalse(approval["collectionOperationallyAdmitted"])
        self.assertFalse(approval["stage2Authorized"])
        self.assertIsNone(approval["stage2BudgetUsd"])
        self.assertTrue(approval["approvedBy"])
        self.assertTrue(approval["approvalReference"].endswith("5618103570"))

    def test_receipt_binds_unchanged_registered_method_not_a_smaller_allocation(self):
        approval = load(ROOT / "authorization.json")
        method_path = BENCH / approval["methodRegistration"]["path"]
        method = load(method_path)
        self.assertEqual(digest(method_path), approval["methodRegistration"]["sha256"])
        self.assertEqual(74, method["runsPerArm"])
        self.assertEqual(3, len(method["tasks"]))
        self.assertEqual(444, method["totalScheduledRuns"])
        self.assertEqual("claude-opus-4-8", method["prospectiveModelPin"])
        self.assertEqual("2.1.266 (Claude Code)", method["prospectiveAgentVersion"])
        self.assertEqual(0.5, method["offRamps"]["shapeRealizationBelow"])
        self.assertEqual(0, method["offRamps"]["armAEscapeExactly"])
        self.assertFalse(method["offRamps"]["confidenceBoundsReplacePointRules"])
        protocol = approval["preservedProtocol"]
        current = (BENCH.parents[1] / protocol["path"]).read_bytes()
        self.assertEqual(protocol["sha256"],
                         hashlib.sha256(current[:protocol["prefixByteCount"]]).hexdigest())
        self.assertTrue(current[protocol["prefixByteCount"]:].startswith(b"\n## 11."))

    def test_budget_arithmetic_is_exact_but_not_a_cost_lower_bound_or_per_run_policy(self):
        assessment = load(ROOT / "feasibility.json")
        self.assertEqual(444, assessment["scheduledSlots"])
        historical = Decimal(assessment["historicalPerRunReferenceUsd"])
        self.assertEqual(Decimal("2.0278"), historical)
        self.assertEqual(Decimal("900.3432"), historical * assessment["scheduledSlots"])
        self.assertEqual(Decimal(assessment["historicalPlanningTotalUsd"]), historical * 444)
        ratio = assessment["allInAverageBudgetPerScheduledSlotUsd"]
        self.assertEqual(Fraction(250, 444), Fraction(ratio["numerator"], ratio["denominator"]))
        self.assertEqual(Decimal(250) / Decimal(444), Decimal(ratio["decimalApproximation"]))
        self.assertFalse(assessment["historicalEstimateIsCostLowerBound"])
        self.assertFalse(assessment["averageIsPerRunAllocationOrTruncationPolicy"])
        self.assertFalse(assessment["costImpossibilityProven"])
        self.assertFalse(assessment["fullPilotWithinCeilingEstablished"])

    def test_budget_hold_is_not_a_scientific_off_ramp_or_observation(self):
        assessment = load(ROOT / "feasibility.json")
        self.assertEqual("BUDGET_NOT_RUN", assessment["status"])
        self.assertEqual("NO_DEFENSIBLE_FULL_PILOT_PLAN_ESTABLISHED", assessment["reasonCode"])
        self.assertIsNone(assessment["scientificVerdict"])
        self.assertIsNone(assessment["stage1Estimands"])
        self.assertIsNone(assessment["stage2N"])
        self.assertIsNone(assessment["stage2Delta"])
        self.assertFalse(assessment["stage2UnderpoweredCarriedApplied"])
        self.assertFalse(assessment["notBuildableClaimed"])
        self.assertFalse(assessment["experimentalModelInvoked"])
        self.assertEqual(0, assessment["newExperimentalCalls"])
        self.assertEqual(digest(ROOT / "authorization.json"), assessment["authorizationSha256"])
        self.assertEqual(digest(ROOT / "billing-evidence.json"), assessment["billingEvidenceSha256"])

    def test_billing_report_does_not_claim_a_zero_subscription_cost_or_hard_bound(self):
        evidence = load(ROOT / "billing-evidence.json")
        self.assertEqual("claude.ai Max / firstParty", evidence["accountContext"]["reportedMode"])
        self.assertIn("not a new authentication observation", evidence["accountContext"]["source"])
        self.assertFalse(evidence["accountContext"]["credentialsOrPersonalBillingDetailsCollected"])
        self.assertFalse(evidence["experimentalModelInvoked"])
        self.assertFalse(evidence["billingGuaranteeEstablished"])
        self.assertTrue(all(item["url"] == "https://code.claude.com/docs/en/costs"
                            for item in evidence["officialDocumentation"]))

    def test_existing_epoch_inputs_stay_unarmed_and_have_no_collection_results(self):
        pilot = BENCH / "epochs/w-rows-pilot-001"
        registration = load(pilot / "registration.json")
        pins = load(pilot / "pins.json")
        self.assertFalse(registration["collectionAuthorized"])
        self.assertEqual("scaffolded", pins["lifecycle"])
        self.assertFalse((pilot / "runs").exists())
        self.assertFalse((pilot / "ppw-stage-ledger.json").exists())
        reservation = load(BENCH / "epochs/w-rows-001/pins.json")
        self.assertEqual("reserved-unregistered", reservation["lifecycle"])
        self.assertIsNone(reservation["runsPerArm"])


if __name__ == "__main__":
    unittest.main()
