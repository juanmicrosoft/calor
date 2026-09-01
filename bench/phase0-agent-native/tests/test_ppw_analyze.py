#!/usr/bin/env python3
"""PP-W-rows adjudication, observed (annex A-1.12, §A.2 row PP-W6; roadmap
v0.16 §4.1, §5 gate 10).

`ppw-analyze.py` adjudicates a ~$135 pre-registered experiment that has not run
yet. Every registered rule therefore needs a fixture that exercises it BEFORE
the money is spent, and — the harder half — a MUTATION that proves the fixture
would go red if the rule were implemented wrongly. An analyzer that cannot fail
on wrong input is worthless.

The committed fixtures are `tests/fixtures/ppw/cases/*.json` (compact epoch
specs, expanded by `ppw_epoch.py`) and `tests/fixtures/ppw/sources/*.calr.txt` (the
two arm-B escape spellings the pairs do not already commit as seeds — carried
with a `.txt` suffix on purpose: they are classifier INPUTS, never compiled,
and a committed `*.calr` under `bench/` would enter the whole-corpus counts the
effect-rows ledgers and the design doc's transcripts pin). Every
other classifier fixture is a REAL committed seed under `pairs/W-00x/seeded/`,
so the classifier is pinned against the artifact #1123 froze rather than an
imitation of it.

Run:  python3 -m unittest discover -s bench/phase0-agent-native/tests
"""

import copy
import importlib.util
import json
import os
import shutil
import statistics
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import ppw_epoch  # noqa: E402


def _load():
    spec = importlib.util.spec_from_file_location(
        "ppw_analyze", os.path.join(BENCH, "ppw-analyze.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PPW = _load()
PAIRS = os.path.join(BENCH, "pairs")


def seed_source(pair_dir, role):
    base = os.path.join(PAIRS, pair_dir, "seeded", role)
    return [open(os.path.join(base, n), encoding="utf-8").read()
            for n in sorted(os.listdir(base)) if n.endswith(".calr")]


def fixture_source(name):
    path = os.path.join(HERE, "fixtures", "ppw", "sources", name)
    return [open(path, encoding="utf-8").read()]


class FrozenConstantsAreTheRows(unittest.TestCase):
    """Every load-bearing number A-1.12 registers, transcribed once and pinned
    here so a drift in the script is observed in the test lane, not at
    adjudication."""

    def test_constants(self):
        self.assertEqual(PPW.EPOCH_ID, "w-rows-001")
        self.assertEqual(PPW.KIND, "pp-w-rows")
        self.assertEqual(PPW.LEG_A_BAR, 0.0)
        self.assertEqual(PPW.LEG_A_EFFECT_SIZE, 0.5)
        self.assertEqual(PPW.BLIND_FLOOR, 2)
        self.assertEqual(PPW.MARGIN, 1.20)
        self.assertEqual(PPW.SENSITIVITY_MARGINS, [1.20, 1.30, 1.35])
        self.assertEqual(PPW.LOWER_BOUND_GATE, 1.0)
        self.assertEqual(PPW.CV_CAP, 0.41)
        self.assertEqual(PPW.MIN_RUNS_PER_CELL, 2)
        self.assertEqual(PPW.CENSOR_CAP, 0.40)
        self.assertEqual(PPW.MIN_POWER, 0.80)
        self.assertEqual(PPW.SPEND_CEILING_USD, 150.0)
        self.assertEqual((PPW.BOOT, PPW.SEED), (2000, 4537))

    def test_registered_sets(self):
        self.assertEqual(PPW.REGISTERED_BLIND,
                         ["W-001-middleware-stage", "W-004-counter-peek", "W-006-map-doubler"])
        self.assertEqual(PPW.REGISTERED_LEG_B,
                         ["W-001-middleware-stage", "W-002-map-and-report",
                          "W-003-match-fallback", "W-004-counter-peek", "W-006-map-doubler"])
        self.assertEqual(PPW.REGISTERED_WARNING_VS_ERROR,
                         ["W-002-map-and-report", "W-003-match-fallback",
                          "W-005-pipeline-trace"])
        # W-005 is leg A only: its arm-B starter does not build, the agent must
        # repair Handle first, and that repair would confound the ratio.
        self.assertNotIn("W-005-pipeline-trace", PPW.REGISTERED_LEG_B)
        self.assertEqual(PPW.ESCAPE_CATEGORIES,
                         ["this-qualified", "property", "inherited", "alias-of-this",
                          "other-instance", "instance-method-group", "other"])

    def test_median_convention_is_the_frozen_one(self):
        """Odd -> the middle value; even -> the mean of the two middle values,
        so k = 3 blind pairs is no more powerful than k = 2."""
        self.assertEqual(PPW.median([0.1, 0.5, 0.9]), 0.5)
        self.assertEqual(PPW.median([0.1, 0.5, 0.7, 0.9]), 0.6)
        self.assertEqual(PPW.median([0.5]), 0.5)
        self.assertIsNone(PPW.median([]))

    def test_pair_id_to_directory_map_cannot_drift(self):
        self.assertEqual(list(PPW.PAIR_DIRS.items()), [
            ("W-001", "W-001-middleware-stage"), ("W-002", "W-002-map-and-report"),
            ("W-003", "W-003-match-fallback"), ("W-004", "W-004-counter-peek"),
            ("W-005", "W-005-pipeline-trace"), ("W-006", "W-006-map-doubler")])
        for short, directory in PPW.PAIR_DIRS.items():
            self.assertTrue(os.path.isdir(os.path.join(PAIRS, directory)), directory)


class OutcomeMap(unittest.TestCase):
    """Every case under tests/fixtures/ppw/cases/ adjudicates to the verdict and
    route its own spec names — the four-valued outcome in its frozen precedence
    NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT."""

    def analyze(self, name, **kwargs):
        case = ppw_epoch.load_case(name)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        analysis, _ = PPW.analyze(epoch, **kwargs)
        return case, epoch, analysis

    def test_every_case_adjudicates_as_registered(self):
        names = ppw_epoch.case_names()
        self.assertGreaterEqual(len(names), 16)
        for name in names:
            with self.subTest(case=name):
                case, _, analysis = self.analyze(name)
                expect = case["expect"]
                self.assertEqual(analysis["verdict"], expect["verdict"], analysis["reason"])
                if "route" in expect:
                    self.assertEqual(analysis["route"], expect["route"])
                if "ownGoal" in expect:
                    self.assertIs(analysis["ownGoal"], expect["ownGoal"])
                if "legAMeetsBar" in expect:
                    self.assertIs(analysis["legA"]["meetsBar"], expect["legAMeetsBar"])
                if "legBFails" in expect:
                    self.assertIs(analysis["legB"]["fails"], expect["legBFails"])
                if expect.get("aPrimeFires"):
                    self.assertTrue(analysis["routes"]["aPrime"]["fires"])
                self.assertEqual(analysis["precedence"],
                                 "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT")

    def test_hit_is_the_only_case_that_hits(self):
        hits = []
        for name in ppw_epoch.case_names():
            _, _, analysis = self.analyze(name)
            if analysis["verdict"] == "HIT":
                hits.append(name)
        self.assertEqual(sorted(hits), ["hit", "leg-a-read-from-the-archived-logs",
                                        "non-building-read-from-the-archived-logs",
                                        "non-building-runs-contribute-no-escape"])

    def test_route_d_is_inert_by_construction(self):
        """A-1.12: cut line 2's antecedent is "if A-1.12 has not registered by
        the branch cut", which merging A-1.12 made permanently false. A W2 slip
        for any reason is a MISS, and route (d) can never fire."""
        for name in ppw_epoch.case_names():
            _, _, analysis = self.analyze(name)
            self.assertFalse(analysis["routes"]["d"]["fires"], name)
            self.assertTrue(analysis["routes"]["d"]["inert"])

    def test_not_adjudicated_names_its_route_with_an_artifact(self):
        """"in every not-adjudicated case the cause must be published WITH THE
        ARTIFACT that shows it, never asserted in prose"."""
        for name in ("not-adjudicated-a", "not-adjudicated-a-prime", "not-adjudicated-b",
                     "not-adjudicated-c"):
            with self.subTest(case=name):
                _, _, analysis = self.analyze(name)
                self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")
                self.assertIsNotNone(analysis["route"])
                self.assertIsNotNone(analysis["routes"][analysis["route"]]["artifact"])

    def test_the_disclosed_limitations_are_published_with_their_direction(self):
        """A limitation whose SIGN is unstated is not a disclosure. The stderr
        channel removes escapes from arm A only, so it biases AGAINST this
        workstream's own hypothesis and cannot manufacture a HIT."""
        _, _, analysis = self.analyze("hit")
        ids = [d["id"] for d in analysis["disclosedLimitations"]]
        self.assertEqual(sorted(ids), ["W-004-arm-B-zero-is-compiler-behaviour",
                                       "silence-signature-required",
                                       "stderr-laundering-invisible-on-arm-A"])
        for entry in analysis["disclosedLimitations"]:
            self.assertTrue(entry["detail"])
            self.assertTrue(entry["direction"])
        stderr = next(d for d in analysis["disclosedLimitations"]
                      if d["id"] == "stderr-laundering-invisible-on-arm-A")
        self.assertIn("ARM A ONLY", stderr["direction"])
        self.assertIn("cannot manufacture a HIT", stderr["direction"])
        w004 = next(d for d in analysis["disclosedLimitations"]
                    if d["id"] == "W-004-arm-B-zero-is-compiler-behaviour")
        self.assertIn("CHARGED CONTROL", w004["direction"])

    def test_own_goal_causes_carry_an_artifact(self):
        _, _, analysis = self.analyze("not-adjudicated-c-no-ppw-block")
        self.assertTrue(analysis["ownGoal"])
        self.assertTrue(analysis["ownGoalCauses"])
        for cause in analysis["ownGoalCauses"]:
            self.assertTrue(cause["cause"])
            self.assertTrue(cause["artifact"])

    def test_no_artifact_is_an_absolute_path(self):
        """The ledger must be byte stable across checkouts, so every published
        artifact is repo-relative and no scratch path may leak into it."""
        for name in ppw_epoch.case_names():
            with self.subTest(case=name):
                _, _, analysis = self.analyze(name)
                published = [c["artifact"] for c in analysis["ownGoalCauses"]]
                published += [r.get("artifact") for r in analysis["routes"].values()]
                for artifact in published:
                    if artifact:
                        self.assertFalse(os.path.isabs(artifact), artifact)
                        self.assertNotIn(tempfile.gettempdir(), artifact)


class LegAEscapeSemantics(unittest.TestCase):
    """THE SINGLE MOST IMPORTANT RULE IN THE SCRIPT. An escape is >= 1 named
    effectObservingTest failing on a workspace that BUILT. Reading `escapedBugs`
    instead — gates §2's "a non-compiling final state counts as all tests
    failing" — inverts leg A's sign, because arm B is precisely the arm on which
    the laundering shortcut does not compile."""

    def setUp(self):
        case = ppw_epoch.load_case("non-building-runs-contribute-no-escape")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        self.epoch = ppw_epoch.build(case, tmp)
        self.analysis, _ = PPW.analyze(self.epoch)

    def _cell(self, pair, arm):
        return next(c for c in self.analysis["perCell"]
                    if c["pair"] == pair and c["arm"] == arm)

    def test_the_fixture_really_does_score_escapedBugs_against_arm_b(self):
        """The mutation this rule exists to prevent, made visible: every arm-B
        run in this epoch records the FULL held-out count as `escapedBugs`."""
        path = os.path.join(self.epoch, "W-001-middleware-stage", ppw_epoch.ARM_B_LABEL,
                            "run-1", "result.json")
        record = json.load(open(path, encoding="utf-8"))
        self.assertEqual(record["escapedBugs"], 2)
        self.assertEqual(record["heldoutPassed"], 0)
        self.assertIs(record["finalBuild"]["ok"], False)

    def test_non_building_runs_contribute_no_escape(self):
        cell = self._cell("W-001-middleware-stage", "B")
        self.assertEqual(cell["escapeRate"], 0.0)
        self.assertEqual(cell["escapes"], 0)

    def test_non_building_runs_are_published_in_their_own_category(self):
        cell = self._cell("W-001-middleware-stage", "B")
        self.assertEqual(cell["didNotBuildAtDeclaredDone"], 4)
        self.assertEqual(len(cell["didNotBuildRuns"]), 4)

    def test_non_building_runs_stay_in_the_denominator(self):
        """"it is counted and published in a separate category" — counted."""
        cell = self._cell("W-001-middleware-stage", "B")
        self.assertEqual(cell["validRuns"], 4)
        self.assertEqual(cell["readableRuns"], 4)

    def test_the_sign_is_not_inverted(self):
        """Under the discarded rule arm B would score 2 escapes per run against
        arm A's 1 and leg A would read backwards. It reads forwards."""
        self.assertEqual(self._cell("W-001-middleware-stage", "A")["escapeRate"], 1.0)
        self.assertEqual(self.analysis["legA"]["blind"]["medianDelta"], 1.0)
        self.assertIs(self.analysis["legA"]["meetsBar"], True)

    def test_escaped_bugs_is_never_read(self):
        """`escapedBugs` is the AGGREGATE Failed: count for the whole held-out
        suite, so it counts failures of tests that are not effect-observing at
        all. Measured on W-006: an unfixed `Map` off-by-one gives Failed: 8
        where the two SURVIVORS are precisely the effect-observing pair — a
        maximal `escapedBugs` on a run that laundered nothing."""
        source = open(os.path.join(BENCH, "ppw-analyze.py"), encoding="utf-8").read()
        body = source[source.index("def read_declared_done"):source.index("def analyze(")]
        self.assertNotIn('record.get("escapedBugs")', body)
        self.assertNotIn('["escapedBugs"]', body)

        case = copy.deepcopy(ppw_epoch.load_case("hit"))
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        for run in range(1, 5):
            path = os.path.join(epoch, "W-001-middleware-stage", ppw_epoch.ARM_B_LABEL,
                                "run-%d" % run, "result.json")
            record = json.load(open(path, encoding="utf-8"))
            record["escapedBugs"] = 8       # eight IndexOutOfRangeExceptions
            record["heldoutPassed"] = 2
            json.dump(record, open(path, "w", encoding="utf-8"))
        analysis, _ = PPW.analyze(epoch)
        cell = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "B")
        self.assertEqual(cell["escapeRate"], 0.0)
        self.assertEqual(analysis["verdict"], "HIT")

    def test_a_silent_but_wrong_solution_is_not_an_escape(self):
        """W-001's Twice_IsSilent_AfterProbe and W-003's Sum2_* assert the RETURN
        VALUE before the silence assertion, so a solution that returns 1 instead
        of 2 and prints nothing fails a NAMED effect-observing test having
        laundered nothing. Only the SILENCE failure signature counts."""
        case = ppw_epoch.load_case("silent-but-wrong-is-not-an-escape")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        cell = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "A")
        self.assertEqual(cell["escapeRate"], 0.0)
        # …and the excluded runs are PUBLISHED, not silently dropped.
        self.assertEqual(len(cell["namedTestFailuresWithoutSilence"]), 4)
        self.assertIs(analysis["legA"]["meetsBar"], False)
        self.assertEqual(analysis["verdict"], "MISS")

    def test_the_refinement_is_arm_symmetric(self):
        """It removes noise from both arms and therefore cannot move the sign:
        the same value-only failure on arm B scores zero too."""
        case = copy.deepcopy(ppw_epoch.load_case("silent-but-wrong-is-not-an-escape"))
        for pair in PPW.REGISTERED_BLIND:
            case["cells"][pair]["B"] = {"escapes": 0, "namedOnlyFailures": 4}
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        for arm in ("A", "B"):
            cell = next(c for c in analysis["perCell"]
                        if c["pair"] == "W-004-counter-peek" and c["arm"] == arm)
            self.assertEqual(cell["escapeRate"], 0.0, arm)
            self.assertEqual(len(cell["namedTestFailuresWithoutSilence"]), 4, arm)
        self.assertEqual(analysis["legA"]["blind"]["medianDelta"], 0.0)

    def test_leg_a_is_recoverable_from_the_archived_logs_alone(self):
        """The fallback the live dry epoch needs: `result.json` predates the
        `finalBuild` / `heldoutFinal` fields, but `.ho_final.txt` and
        `.src_final.txt` have always been archived, so no data is lost and
        nothing needs re-running."""
        case = ppw_epoch.load_case("leg-a-read-from-the-archived-logs")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        run = os.path.join(epoch, "W-001-middleware-stage", ppw_epoch.ARM_A_LABEL, "run-1")
        record = json.load(open(os.path.join(run, "result.json"), encoding="utf-8"))
        self.assertIsNone(record["finalBuild"])
        self.assertIsNone(record["heldoutFinal"])
        self.assertTrue(os.path.exists(os.path.join(run, ".ho_final.txt")))
        self.assertTrue(os.path.exists(os.path.join(run, ".src_final.txt")))

        built, failed, silence = PPW.read_declared_done(run, record)
        self.assertIs(built, True)
        self.assertEqual(silence, ["Twice_IsSilent_OnFreshBehavior"])
        self.assertEqual(failed, ["Twice_IsSilent_OnFreshBehavior"])

        analysis, _ = PPW.analyze(epoch)
        self.assertEqual(analysis["verdict"], "HIT")
        cell = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "A")
        self.assertEqual(cell["escapeRate"], 1.0)

    def test_a_run_with_neither_the_fields_nor_the_logs_is_invalid(self):
        case = copy.deepcopy(ppw_epoch.load_case("leg-a-read-from-the-archived-logs"))
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        for root, _, files in os.walk(epoch):
            for name in (".ho_final.txt", ".src_final.txt"):
                if name in files:
                    os.remove(os.path.join(root, name))
        analysis, _ = PPW.analyze(epoch)
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("escapedBugs may not stand in for it" in b
                            for b in analysis["blockers"]))

    def test_only_the_named_effect_observing_tests_count(self):
        """A functional miss elsewhere in a 6-to-11-test suite is not an escape."""
        case = ppw_epoch.load_case("hit")
        case = copy.deepcopy(case)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        for run in range(1, 5):
            run_dir = os.path.join(epoch, "W-001-middleware-stage", ppw_epoch.ARM_A_LABEL,
                                   "run-%d" % run)
            path = os.path.join(run_dir, "result.json")
            record = json.load(open(path, encoding="utf-8"))
            # A functional test failing its own string assertion: a silence-shaped
            # failure on a test that is NOT effect-observing.
            record["heldoutFinal"]["failedTests"] = ["Twice_ReturnsSum"]
            record["heldoutFinal"]["silenceFailures"] = ["Twice_ReturnsSum"]
            json.dump(record, open(path, "w", encoding="utf-8"))
            os.remove(os.path.join(run_dir, ".ho_final.txt"))   # the fields are the source here
        analysis, _ = PPW.analyze(epoch)
        cell = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "A")
        self.assertEqual(cell["escapeRate"], 0.0)


class LegAStatistic(unittest.TestCase):
    def test_the_bar_is_strictly_above_zero(self):
        """"the one-sided 95 % lower bound of that delta EXCEEDS 0" — a bound of
        exactly 0 does not meet the bar."""
        case = ppw_epoch.load_case("shape-not-realized")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        self.assertEqual(analysis["legA"]["blind"]["lowerBound95"], 0.0)
        self.assertIs(analysis["legA"]["meetsBar"], False)
        self.assertEqual(analysis["verdict"], "MISS")

    def test_the_verdict_is_read_on_the_blind_cells_only(self):
        """The warning-vs-error class is published beside the verdict with the
        same statistic and NEVER pooled into it."""
        case = ppw_epoch.load_case("hit")
        case = copy.deepcopy(case)
        # Make the warning-vs-error class point the other way. If it were pooled
        # the blind statistic would move; it must not.
        for pair in PPW.REGISTERED_WARNING_VS_ERROR:
            case["cells"][pair] = {"A": {"escapes": 0}, "B": {"escapes": 4}}
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        self.assertEqual(analysis["legA"]["blind"]["pairs"], PPW.REGISTERED_BLIND)
        self.assertEqual(analysis["legA"]["blind"]["medianDelta"], 1.0)
        self.assertEqual(analysis["legA"]["warningVsError"]["medianDelta"], -1.0)
        self.assertEqual(analysis["verdict"], "HIT")

    def test_w005_carries_a_leg_a_rate_and_no_leg_b_ratio(self):
        case = ppw_epoch.load_case("hit")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        cells = [c["pair"] for c in analysis["perCell"]]
        self.assertIn("W-005-pipeline-trace", cells)
        self.assertNotIn("W-005-pipeline-trace", analysis["legB"]["pairs"])
        self.assertIn("W-005-pipeline-trace", analysis["legA"]["warningVsError"]["pairs"])


class LegBRule(unittest.TestCase):
    def analyze(self, name):
        case = ppw_epoch.load_case(name)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        return analysis

    def test_fails_needs_both_conditions(self):
        fails = self.analyze("miss-leg-b")["legB"]
        self.assertGreater(fails["pointEstimate"], PPW.MARGIN)
        self.assertGreater(fails["lowerBound95"], PPW.LOWER_BOUND_GATE)
        self.assertIs(fails["fails"], True)
        cv = self.analyze("underpowered-cv")["legB"]
        self.assertIs(cv["fails"], False)

    def test_the_point_alone_does_not_fail_leg_b(self):
        """THE CONJUNCTION, and the mutation that would drop it: a point over
        the 1.20 margin with the bound NOT firing is UNDERPOWERED, never a
        clean miss. The UNDERPOWERED branch exists for exactly this shape."""
        analysis = self.analyze("underpowered-point-over-margin-bound-not-firing")
        leg_b = analysis["legB"]
        self.assertGreater(leg_b["pointEstimate"], PPW.MARGIN)
        self.assertLessEqual(leg_b["lowerBound95"], PPW.LOWER_BOUND_GATE)
        self.assertIs(leg_b["boundFires"], False)
        self.assertIs(leg_b["fails"], False)
        self.assertLessEqual(leg_b["realizedMedianWithinCellCv"], PPW.CV_CAP)
        self.assertIs(analysis["legA"]["meetsBar"], True)
        self.assertEqual(analysis["verdict"], "UNDERPOWERED")

    def test_sensitivity_is_published_at_all_three_candidate_margins(self):
        """A-1.12 makes this an OBLIGATION, not a courtesy."""
        leg_b = self.analyze("miss-leg-b-only-at-the-registered-margin")["legB"]
        self.assertEqual([s["margin"] for s in leg_b["sensitivity"]], [1.20, 1.30, 1.35])
        self.assertEqual([s["population"] for s in leg_b["sensitivity"]],
                         ["e1-rows-parity-001", "pooled", "w5-parity-002"])
        self.assertEqual([s["registered"] for s in leg_b["sensitivity"]], [True, False, False])
        for entry in leg_b["sensitivity"]:
            self.assertIsNotNone(entry["pointEstimate"])
            self.assertIsNotNone(entry["lowerBound95"])

    def test_the_verdict_is_read_at_1_20_and_only_at_1_20(self):
        analysis = self.analyze("miss-leg-b-only-at-the-registered-margin")
        by_margin = {s["margin"]: s for s in analysis["legB"]["sensitivity"]}
        self.assertIs(by_margin[1.20]["fails"], True)
        self.assertIs(by_margin[1.30]["fails"], False)
        self.assertIs(by_margin[1.35]["fails"], False)
        self.assertIs(analysis["legB"]["fails"], True)
        self.assertEqual(analysis["verdict"], "MISS")

    def test_tokens_are_the_corrected_figure(self):
        """A-1.9.1: the per-run figure is token-usage.py's corrected sum over
        modelUsage[*], never the naive usage.output_tokens."""
        case = ppw_epoch.load_case("hit")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        run = os.path.join(epoch, "W-001-middleware-stage", ppw_epoch.ARM_A_LABEL, "run-1")
        envelope = json.load(open(os.path.join(run, "agent.json"), encoding="utf-8"))
        self.assertEqual(envelope["usage"]["output_tokens"], 10)      # the naive figure
        tokens, source = PPW._corrected_tokens(run)
        self.assertEqual((tokens, source), (1000, "modelUsage"))
        analysis, _ = PPW.analyze(epoch)
        self.assertEqual(analysis["tokenSources"], {"modelUsage": 48})

    def test_cv_cap_reads_underpowered_not_miss(self):
        analysis = self.analyze("underpowered-cv")
        self.assertGreater(analysis["legB"]["realizedMedianWithinCellCv"], PPW.CV_CAP)
        self.assertEqual(analysis["verdict"], "UNDERPOWERED")


class DenominatorsComeFromPins(unittest.TestCase):
    """A-1.12's explicit requirement, and the defect `ppe1-analyze.py:66` has:
    legBPairs and blindPairs are READ FROM `pins.json`, never from a script
    default — and then CHECKED against the registration, because an epoch
    naming anything else is invalid under validity condition (4)."""

    def build(self, mutate_pins=None, case_name="hit"):
        case = ppw_epoch.load_case(case_name)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        if mutate_pins:
            path = os.path.join(epoch, "pins.json")
            pins = json.load(open(path, encoding="utf-8"))
            mutate_pins(pins)
            json.dump(pins, open(path, "w", encoding="utf-8"))
        return epoch

    def test_the_analyzer_uses_the_pinned_sets(self):
        """A dry run whose pins name a SHORTER leg-B denominator computes over
        exactly that denominator — proof the value is read, not defaulted."""
        epoch = self.build(lambda p: p["ppW"].update(
            legBPairs=["W-001-middleware-stage", "W-004-counter-peek"]))
        analysis, _ = PPW.analyze(epoch, dry_run=True)
        self.assertEqual(analysis["legBPairs"],
                         ["W-001-middleware-stage", "W-004-counter-peek"])
        self.assertEqual(analysis["legB"]["pairs"],
                         ["W-001-middleware-stage", "W-004-counter-peek"])

    def test_a_live_epoch_naming_other_sets_is_invalid(self):
        epoch = self.build(lambda p: p["ppW"]["legBPairs"].append("W-005-pipeline-trace"))
        analysis, _ = PPW.analyze(epoch)
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("legBPairs" in b for b in analysis["blockers"]))

    def test_a_live_epoch_with_a_short_blind_set_is_invalid(self):
        epoch = self.build(lambda p: p["ppW"].update(blindPairs=["W-001-middleware-stage"]))
        analysis, _ = PPW.analyze(epoch)
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")
        # Both (a') — one blind cell is below the floor of two — and (c) fire; the
        # registered order names (a') first. The (4) blocker is published either way.
        self.assertIn(analysis["route"], ("aPrime", "c"))
        self.assertTrue(analysis["routes"]["c"]["fires"])
        self.assertTrue(any("blindPairs" in b for b in analysis["blockers"]))

    def test_no_hardcoded_pair_list_drives_the_denominator(self):
        """The script's own text: the registered sets exist to VALIDATE the
        pinned ones, and the computation reads `pins.json`."""
        source = open(os.path.join(BENCH, "ppw-analyze.py"), encoding="utf-8").read()
        self.assertIn("read_leg_b_pairs(pins_path)", source)
        self.assertIn("NEVER FROM A SCRIPT DEFAULT", source)
        # The registered sets are compared, never substituted for the pinned ones.
        self.assertIn("sorted(leg_b_pairs) != sorted(REGISTERED_LEG_B)", source)


class ValidityConditions(unittest.TestCase):
    def build(self, case_name="hit", mutate_pins=None, mutate_runs=None):
        case = ppw_epoch.load_case(case_name)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        if mutate_pins:
            path = os.path.join(epoch, "pins.json")
            pins = json.load(open(path, encoding="utf-8"))
            mutate_pins(pins)
            json.dump(pins, open(path, "w", encoding="utf-8"))
        if mutate_runs:
            for root, _, files in os.walk(epoch):
                if "result.json" in files:
                    path = os.path.join(root, "result.json")
                    record = json.load(open(path, encoding="utf-8"))
                    if mutate_runs(record, root) is not False:
                        json.dump(record, open(path, "w", encoding="utf-8"))
        return epoch

    def test_1_a_run_without_a_transcript_is_invalid_and_reads_route_b(self):
        analysis, _ = PPW.analyze(self.build("not-adjudicated-b"))
        self.assertTrue(analysis["routes"]["b"]["fires"])
        self.assertTrue(analysis["missingTranscript"])
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")

    def test_2_turns_assistant_messages_is_recomputed_from_the_transcript(self):
        """A-1.12 (v): the field must carry the TOP-LEVEL count — distinct
        assistant message.id values whose parent_tool_use_id is null — with
        forwarded subagent messages counted separately and never folded in. The
        analyzer does not take the recorded number on trust: it RECOMPUTES it
        from the archived transcript, which is what makes the condition a check
        rather than a restatement. Here the run records the total (7 top-level
        + 3 subagent = 10) under the registered name."""
        def fold(record, root):
            record["turns"] = {"assistantMessages": 10, "subagentMessages": 3,
                               "assistantMessagesIncludingSubagents": 10,
                               "numTurns": 10, "source": "transcript.jsonl"}
        analysis, _ = PPW.analyze(self.build(mutate_runs=fold))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("TOP-LEVEL assistant message.id" in b
                            for b in analysis["blockers"]), analysis["blockers"][:3])

    def test_2_a_missing_turn_count_is_invalid(self):
        def blank(record, root):
            record["turns"] = {"assistantMessages": None, "source": "helper-error"}
        analysis, _ = PPW.analyze(self.build(mutate_runs=blank))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("not an integer count" in b for b in analysis["blockers"]))

    def test_2_the_registered_shape_is_accepted(self):
        """The control: a run whose transcript holds 7 top-level assistant
        messages and 3 forwarded subagent ones, recording 7 — the shape
        harness-capture.py writes — is VALID."""
        case = copy.deepcopy(ppw_epoch.load_case("hit"))
        case["defaultCell"] = dict(case.get("defaultCell") or {}, turns=7, subagentTurns=3)
        for cell in case["cells"].values():
            for arm in cell.values():
                arm.setdefault("turns", 7)
                arm.setdefault("subagentTurns", 3)
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        record = json.load(open(os.path.join(epoch, "W-001-middleware-stage",
                                             ppw_epoch.ARM_A_LABEL, "run-1", "result.json"),
                                encoding="utf-8"))
        self.assertEqual(record["turns"]["assistantMessages"], 7)
        self.assertEqual(record["turns"]["assistantMessagesIncludingSubagents"], 10)
        analysis, _ = PPW.analyze(epoch)
        self.assertTrue(analysis["harnessValid"], analysis["blockers"])
        self.assertEqual(analysis["verdict"], "HIT")

    def test_a_recorded_own_goal_needs_an_artifact_that_exists(self):
        """A-1.12's own-goal clause covers causes the instrument cannot see (a
        Calor0410 demotion changed before the epoch). The epoch may RECORD one,
        but only WITH the artifact that shows it."""
        honoured = self.build(mutate_pins=lambda p: p["ppW"].update(ownGoal={
            "cause": "the Calor0410 demotion moved before the epoch",
            "artifact": "bench/phase0-agent-native/pairs/ppw-seeded-compiles.json"}))
        analysis, _ = PPW.analyze(honoured)
        self.assertTrue(analysis["ownGoal"])
        self.assertEqual(analysis["verdict"], "MISS")
        self.assertTrue(any("recorded by the epoch" in c["cause"]
                            for c in analysis["ownGoalCauses"]))

        prose = self.build(mutate_pins=lambda p: p["ppW"].update(ownGoal={
            "cause": "trust me", "artifact": "bench/phase0-agent-native/nope.json"}))
        analysis, _ = PPW.analyze(prose)
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("never asserted in prose" in b for b in analysis["blockers"]))

    def test_3_each_arm_must_record_exactly_one_compiler_hash(self):
        def drift(record, root):
            if ppw_epoch.ARM_B_LABEL in root and root.endswith("run-1"):
                record["compilerHash"] = "some-other-hash"
        analysis, _ = PPW.analyze(self.build(mutate_runs=drift))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("distinct compilerHash" in b for b in analysis["blockers"]))

    def test_3_the_two_arms_compiler_hashes_must_be_disjoint(self):
        def same(record, root):
            record["compilerHash"] = "identical-compiler"
        analysis, _ = PPW.analyze(self.build(mutate_runs=same))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("share a compilerHash" in b for b in analysis["blockers"]))

    def test_3_the_two_arms_calor_tasks_hashes_must_differ(self):
        analysis, _ = PPW.analyze(self.build(
            mutate_pins=lambda p: p["armB"].update(calorTasksSha=p["armA"]["calorTasksSha"])))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("Calor.Tasks.dll hash to the same value" in b
                            for b in analysis["blockers"]))

    def test_3_the_permissive_policy_is_witnessed_by_optionsHash(self):
        """A control arm built from the registered commit but run STRICT leaves
        compilerHash unchanged and moves only buildState.optionsHash, so the
        compilerHash leg alone cannot see it. The runner checks only that leg;
        the analyzer checks both."""
        analysis, _ = PPW.analyze(self.build("hit"))
        self.assertTrue(analysis["harnessValid"], analysis["blockers"])
        self.assertEqual(analysis["armA"]["optionsHashes"], ["options-permissive"])
        self.assertEqual(analysis["armB"]["optionsHashes"], ["options-strict"])

        def strict_control(record, root):
            record["buildState"]["optionsHash"] = "options-strict"
        shared = PPW.analyze(self.build(mutate_runs=strict_control))[0]
        self.assertEqual(shared["route"], "c")
        self.assertTrue(any("share a buildState.optionsHash" in b for b in shared["blockers"]))

        def drop(record, root):
            record["buildState"]["optionsHash"] = None
        missing = PPW.analyze(self.build(mutate_runs=drop))[0]
        self.assertEqual(missing["route"], "c")
        self.assertTrue(any("optionsHash is not recorded" in b for b in missing["blockers"]))

    def test_the_inherited_ppl5_ppl6_pins_are_ignored_and_named(self):
        """`run-m5-epoch.sh`'s base pins carry ppL5.pairs (the W2-/W3- loop suite)
        and ppL6.pairs (the N1- neutral set). They are NOT this experiment's
        pairs; reading one would be a silent wrong-denominator bug."""
        epoch = self.build(mutate_pins=lambda p: p.update(
            ppL5={"pairs": ["W2-001", "W3-004"]},
            ppL6={"pairs": ["N1-001", "N1-002"]},
            ppW5=None))
        analysis, lines = PPW.analyze(epoch)
        self.assertEqual(analysis["inheritedPinsIgnored"], ["ppL5", "ppL6", "ppW5"])
        self.assertEqual(analysis["legBPairs"], PPW.REGISTERED_LEG_B)
        self.assertEqual(analysis["blindPairs"], PPW.REGISTERED_BLIND)
        self.assertEqual(analysis["verdict"], "HIT")
        self.assertTrue(any("inherited pins IGNORED" in line for line in lines))

    def test_arm_provenance_names_the_plus_one_commit(self):
        analysis, _ = PPW.analyze(self.build("hit"))
        self.assertIn("283ec9f9964ddd5b21da15b646a0dd77d53de99e", analysis["armA"]["provenance"])
        self.assertIn("arm/v0.14.3-pre-rows", analysis["armA"]["provenance"])
        self.assertIn("not drift", analysis["armA"]["provenance"])

    def test_4_the_harness_commit_must_be_recorded(self):
        analysis, _ = PPW.analyze(self.build(mutate_pins=lambda p: p.pop("harnessCommit")))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("harness commit" in b for b in analysis["blockers"]))

    def test_5_the_censoring_cap_is_forty_percent_per_arm(self):
        analysis, _ = PPW.analyze(self.build("hit"))
        self.assertEqual(analysis["censored"], {"control": 0.0, "treatment": 0.0})

        def censor(record, root):
            if ppw_epoch.ARM_A_LABEL in root:
                record["censored"] = True
        over = PPW.analyze(self.build(mutate_runs=censor))[0]
        self.assertEqual(over["censored"]["control"], 1.0)
        self.assertTrue(any("censored fraction" in b for b in over["blockers"]))
        self.assertEqual(over["route"], "c")

    def test_6_the_pp_w5_validity_floor_drops_a_pair(self):
        analysis, _ = PPW.analyze(self.build("not-adjudicated-c"))
        self.assertEqual([d["pair"] for d in analysis["pairsDropped"]], ["W-004-counter-peek"])
        self.assertNotIn("W-004-counter-peek", analysis["pairsSurviving"])
        self.assertEqual(analysis["route"], "c")

    def test_the_pre_rows_arm_canary_verdict_is_honoured(self):
        def break_canary(record, root):
            if record.get("controlArmKind") == "pre-rows":
                record["armCanary"] = "failed"
        analysis, _ = PPW.analyze(self.build(mutate_runs=break_canary))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("canary verdict" in b for b in analysis["blockers"]))

    def test_a_null_agent_run_cannot_adjudicate(self):
        def null(record, root):
            record["nullAgent"] = True
        analysis, _ = PPW.analyze(self.build(mutate_runs=null))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("null-agent" in b for b in analysis["blockers"]))

    def test_a_non_live_epoch_cannot_adjudicate(self):
        analysis, _ = PPW.analyze(self.build(mutate_pins=lambda p: p.update(mode="--null-agent")))
        self.assertEqual(analysis["route"], "c")
        self.assertTrue(any("only a live epoch" in b for b in analysis["blockers"]))

    def test_an_unregistered_epoch_id_is_refused_without_dry_run(self):
        epoch = self.build(mutate_pins=lambda p: p.update(epochId="w-rows-dryrun"))
        with self.assertRaises(SystemExit):
            PPW.analyze(epoch)
        analysis, _ = PPW.analyze(epoch, dry_run=True)
        self.assertTrue(analysis["dryRun"])
        self.assertIsNotNone(analysis["dryRunNote"])

    def test_stripping_the_result_json_fields_falls_back_to_the_archived_logs(self):
        """The fields are a convenience; the LOGS are the evidence. Removing
        `finalBuild` / `heldoutFinal` must change nothing, because
        `.ho_final.txt` and `.src_final.txt` carry both facts."""
        def strip(record, root):
            record.pop("finalBuild", None)
            record.pop("heldoutFinal", None)
        analysis, _ = PPW.analyze(self.build(mutate_runs=strip))
        self.assertTrue(analysis["harnessValid"], analysis["blockers"])
        self.assertEqual(analysis["verdict"], "HIT")

    def test_a_run_with_neither_the_fields_nor_the_logs_is_invalid_here_too(self):
        """Fail closed. `escapedBugs` may NOT stand in for the registered escape:
        that substitution is the sign inversion A-1.12 replaces."""
        def strip(record, root):
            record.pop("finalBuild", None)
            record.pop("heldoutFinal", None)
            for name in (".ho_final.txt", ".src_final.txt"):
                path = os.path.join(root, name)
                if os.path.exists(path):
                    os.remove(path)
        analysis, _ = PPW.analyze(self.build(mutate_runs=strip))
        self.assertEqual(analysis["route"], "c")
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")
        self.assertTrue(any("escapedBugs may not stand in for it" in b
                            for b in analysis["blockers"]))

    def test_route_a_cannot_be_skipped(self):
        """An adjudication that never re-checked the unmutated starters against
        their frozen multisets is not an adjudication."""
        case = ppw_epoch.load_case("hit")
        case = copy.deepcopy(case)
        case["starterCompiles"] = "absent"
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        self.assertIsNone(analysis["routes"]["a"]["fires"] or None)
        self.assertFalse(analysis["routes"]["a"]["evaluated"])
        self.assertEqual(analysis["route"], "c")
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")


class DryRunSizesNAndNeverMovesTheBar(unittest.TestCase):
    """A:81 / A-1.12. The dry run's job is to SIZE N; emitting HIT or MISS from a
    dry epoch is the worst failure mode this script has, so a dry run emits no
    verdict at all and can never reach the ledger."""

    def dry(self, **kwargs):
        case = copy.deepcopy(ppw_epoch.load_case("hit"))
        case.setdefault("pins", {})["epochId"] = "w-rows-dry-001"
        case["runsPerArm"] = 3
        for pair, escapes in (("W-001-middleware-stage", 3), ("W-004-counter-peek", 3),
                              ("W-006-map-doubler", 2)):
            case["cells"][pair] = {"A": {"escapes": escapes}, "B": {"escapes": 0}}
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        epoch = ppw_epoch.build(case, tmp)
        return PPW.analyze(epoch, dry_run=True, sizing_sims=40, sizing_boot=100, **kwargs)

    def test_a_dry_run_emits_no_verdict(self):
        analysis, lines = self.dry()
        self.assertTrue(analysis["dryRun"])
        self.assertIsNone(analysis["verdict"])
        self.assertIsNone(analysis["route"])
        self.assertIn("sizes N", analysis["reason"])
        self.assertTrue(any("verdict: (none" in line for line in lines))

    def test_a_dry_run_can_never_reach_the_ledger(self):
        analysis, _ = self.dry()
        with self.assertRaises(SystemExit):
            PPW.build_ledger(analysis)

    def test_a_dry_run_still_reports_the_leg_statistics(self):
        """Sizing needs the variance, so the legs are computed — they are just
        never read as a verdict."""
        analysis, _ = self.dry()
        self.assertIsNotNone(analysis["legA"]["blind"]["medianDelta"])
        self.assertIsNotNone(analysis["legB"]["pointEstimate"])

    def test_the_sizing_output_is_first_class(self):
        analysis, _ = self.dry()
        sizing = analysis["sizing"]
        self.assertEqual(sizing["blindPairsObserved"], 3)
        self.assertEqual(sizing["blindFloor"], 2)
        self.assertEqual(sizing["observedDeltas"], [1.0, 1.0, 0.6667])
        self.assertIsNotNone(sizing["observedVariance"])
        self.assertGreater(sizing["varianceUpperBound95"], sizing["observedVariance"])
        self.assertEqual([e["n"] for e in sizing["powerCurve"]], list(range(2, 13)))
        for entry in sizing["powerCurve"]:
            self.assertEqual(entry["runs"], 2 + 36 + 6 * entry["n"] * 2)
            self.assertEqual(entry["fitsCeiling"], entry["estimatedUsd"] <= 150.0)
        self.assertIn("recommendedN", sizing)
        self.assertIn("atPointVarianceNotRegistered", sizing)
        self.assertIn("NOT the registered basis",
                      sizing["atPointVarianceNotRegistered"]["note"])

    def test_the_spend_arithmetic_is_a_1_12s(self):
        """N = 8 -> 134 runs ~ $135; N = 9 -> 146 ~ $147; N = 10 -> 158 ~ $159,
        already over the $150 ceiling."""
        self.assertEqual((PPW._epoch_runs(8), PPW._epoch_runs(9), PPW._epoch_runs(10)),
                         (134, 146, 158))
        self.assertEqual(round(PPW._epoch_cost(8)), 135)
        self.assertEqual(round(PPW._epoch_cost(9)), 147)
        self.assertEqual(round(PPW._epoch_cost(10)), 159)
        self.assertGreater(PPW._epoch_cost(10), PPW.SPEND_CEILING_USD)
        self.assertLessEqual(PPW._epoch_cost(9), PPW.SPEND_CEILING_USD)

    def test_the_variance_bound_is_the_upper_one(self):
        """A-1.12 sizes N "under the UPPER confidence bound of its variance";
        sizing on the point estimate would under-size by design. df = 2 gives
        2 s^2 / 0.102587."""
        values = [0.2, 0.5, 0.8]
        self.assertAlmostEqual(PPW.variance_upper_bound(values),
                               2 * statistics.variance(values) / 0.102587, places=6)
        self.assertGreater(PPW.variance_upper_bound(values), statistics.variance(values))
        self.assertIsNone(PPW.variance_upper_bound([0.5]))

    def test_the_recommendation_is_not_carried_by_a_single_noisy_point(self):
        curve = [{"n": 2, "power": 0.1}, {"n": 3, "power": 0.85},
                 {"n": 4, "power": 0.2}, {"n": 5, "power": 0.3}]
        self.assertIsNone(PPW._smallest_sufficient_n(curve))
        curve = [{"n": 2, "power": 0.1}, {"n": 3, "power": 0.85},
                 {"n": 4, "power": 0.9}, {"n": 5, "power": 0.95}]
        self.assertEqual(PPW._smallest_sufficient_n(curve), 3)
        self.assertIsNone(PPW._smallest_sufficient_n([]))

    def test_the_registered_procedure_is_what_is_simulated(self):
        """The sizing loop runs the SAME two-level cluster bootstrap the
        adjudicator runs. Sanity anchor: at k = 2 blind pairs, 8 runs/cell and no
        between-pair spread, power at Delta = 0.5 is high — the shape roadmap
        §4.1's round-3 simulation reports (power 0.87, MDD ~ 0.45)."""
        power = PPW._simulate_power(2, 8, 0.5, 0.0, sims=200, boot=400)
        self.assertGreater(power, 0.7)
        # and it is not vacuously high: at Delta = 0 the bar must almost never clear
        null_power = PPW._simulate_power(2, 8, 0.0, 0.0, sims=200, boot=400)
        self.assertLess(null_power, 0.2)

    def test_sizing_arms_underpowered_when_nothing_affordable_reaches_the_bar(self):
        sizing = PPW.size_n([1.0, 0.0, -0.5], 3, sims=40, boot=100)
        self.assertIsNone(sizing["recommendedN"])
        self.assertTrue(sizing["armsUnderpowered"])
        self.assertIn("registers its achievable power", sizing["note"])

    def test_sizing_refuses_below_the_blind_floor(self):
        sizing = PPW.size_n([0.5], 1, sims=10, boot=50)
        self.assertIsNone(sizing["recommendedN"])
        self.assertIn("floor of two", sizing["note"])


class ShapeRealizedIndicator(unittest.TestCase):
    """A-1.12 (#1123 M2): without it a null leg A is uninterpretable — "rows did
    not help" cannot be told from "the shape under test was never written". And
    the indicator MUST NOT be satisfiable by the starter itself; #1123's review
    found W-004's vacuously true, reporting "realized" on a workspace where the
    agent had written nothing at all."""

    def test_every_registered_pair_satisfies_the_obligation_today(self):
        for pair_dir in PPW.PAIR_DIRS.values():
            with self.subTest(pair=pair_dir):
                check = PPW.indicator_self_check(pair_dir, PPW.load_pair(pair_dir))
                self.assertTrue(check["hasIndicator"], pair_dir)
                self.assertIs(check["falseOnStarter"], True, pair_dir)
                self.assertIs(check["trueOnClean"], True, pair_dir)
                self.assertIs(check["ok"], True, pair_dir)

    def test_it_is_published_per_cell_beside_the_escape_rate(self):
        case = ppw_epoch.load_case("hit")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        for cell in analysis["perCell"]:
            self.assertIn("shapeRealized", cell)
            self.assertIn("shapeRealizedRate", cell)
            self.assertIn("escapeRate", cell)
        blind = next(c for c in analysis["perCell"]
                     if c["pair"] == "W-001-middleware-stage" and c["arm"] == "B")
        self.assertEqual(blind["shapeRealizedRate"], 1.0)

    def test_a_cell_that_never_wrote_the_shape_says_so(self):
        case = ppw_epoch.load_case("shape-not-realized")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        for pair in PPW.REGISTERED_BLIND:
            for arm in ("A", "B"):
                cell = next(c for c in analysis["perCell"]
                            if c["pair"] == pair and c["arm"] == arm)
                self.assertEqual(cell["shapeRealizedRate"], 0.0, "%s/%s" % (pair, arm))

    def test_an_indicator_satisfiable_by_its_own_starter_is_an_own_goal(self):
        """THE MUTATION: make W-001's indicator match its frozen starter. The
        obligation must go red, the epoch must not adjudicate, and — because
        the defect is this workstream's own artifact — the verdict must be MISS
        with the artifact published."""
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)

        def vacuous(root):
            path = os.path.join(root, "W-001-middleware-stage", "pair.json")
            pair = json.load(open(path, encoding="utf-8"))
            pair["shapeRealizedIndicator"]["sourceRegex"] = "§M\\{m001:MiddlewareAfter\\}"
            json.dump(pair, open(path, "w", encoding="utf-8"))

        pairs_root = ppw_epoch.scratch_pairs(tmp, vacuous)
        epoch = ppw_epoch.build(ppw_epoch.load_case("hit"), tmp)
        analysis, _ = PPW.analyze(epoch, pairs_root=pairs_root)
        check = next(c for c in analysis["indicatorSelfCheck"]
                     if c["pair"] == "W-001-middleware-stage")
        self.assertIs(check["falseOnStarter"], False)
        self.assertIs(check["ok"], False)
        self.assertTrue(analysis["ownGoal"])
        self.assertEqual(analysis["verdict"], "MISS")
        self.assertTrue(any("shape-realized indicator is satisfiable" in c["cause"]
                            for c in analysis["ownGoalCauses"]))

    def test_the_unmutated_pairs_root_does_not_fire_that_own_goal(self):
        """The control for the mutation above: without the edit, no own goal."""
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        pairs_root = ppw_epoch.scratch_pairs(tmp)
        analysis, _ = PPW.analyze(ppw_epoch.build(ppw_epoch.load_case("hit"), tmp),
                                  pairs_root=pairs_root)
        self.assertFalse(analysis["ownGoal"])
        self.assertEqual(analysis["verdict"], "HIT")


class StarterBlobDrift(unittest.TestCase):
    """The own-goal clause's first named example: "a starter broken by a
    W-series edit". A starter whose BYTES no longer hash to the blob A-1.12
    froze was edited here; a starter whose bytes are intact but whose multiset
    differs is route (a) and is NOT auto-attributed, because the cause could be
    the compiler and gates §0.3 forbids a results-dependent guess."""

    def test_editing_a_frozen_starter_is_an_own_goal(self):
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)

        def edit(root):
            path = os.path.join(root, "W-001-middleware-stage", "starter-b",
                                "A3-middleware.calr")
            with open(path, "a", encoding="utf-8") as fh:
                fh.write("\n  §F{f099:Extra:pub} () -> i32\n    §E{}\n    §R INT:1\n")

        pairs_root = ppw_epoch.scratch_pairs(tmp, edit)
        analysis, _ = PPW.analyze(ppw_epoch.build(ppw_epoch.load_case("hit"), tmp),
                                  pairs_root=pairs_root)
        self.assertTrue(analysis["starterBlobDrift"])
        self.assertTrue(analysis["ownGoal"])
        self.assertEqual(analysis["verdict"], "MISS")
        self.assertTrue(any("no longer hash to the frozen blob" in c["cause"]
                            for c in analysis["ownGoalCauses"]))

    def test_route_a_alone_is_not_an_own_goal(self):
        case = ppw_epoch.load_case("not-adjudicated-a")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        self.assertTrue(analysis["routes"]["a"]["fires"])
        self.assertEqual(analysis["starterBlobDrift"], [])
        self.assertFalse(analysis["ownGoal"])
        self.assertEqual(analysis["verdict"], "NOT-ADJUDICATED")

    def test_every_frozen_starter_hashes_to_its_registered_blob_today(self):
        for pair_dir in PPW.PAIR_DIRS.values():
            with self.subTest(pair=pair_dir):
                self.assertEqual(
                    PPW.starter_blob_drift(pair_dir, PPW.load_pair(pair_dir)), [])


class EscapeClassification(unittest.TestCase):
    """A-1.12: arm-B escapes must be classified by WHICH INSTANCE of the
    argument-resolution escape they used. A two-way "this-route or other" split
    is insufficient — it cannot tell the forms apart, and the property form is
    the one an agent is most likely to write.

    Five of the seven instances are pinned against the seeds #1123 committed at
    `a1230e2a`; the two the artifact does not seed are committed under
    tests/fixtures/ppw/sources/."""

    def assert_class(self, sources, expected):
        label, categories = PPW.classify_escape(sources)
        self.assertEqual(label, expected)
        self.assertIn(expected, categories)

    def test_this_qualified(self):
        self.assert_class(
            seed_source("W-001-middleware-stage", "unregistered-this-qualified-escape-b"),
            "this-qualified")
        self.assert_class(
            seed_source("W-006-map-doubler", "unregistered-this-qualified-escape-b"),
            "this-qualified")

    def test_property(self):
        self.assert_class(
            seed_source("W-001-middleware-stage", "unregistered-property-backed-escape-b"),
            "property")

    def test_property_through_the_local_that_does_not_preserve_the_row(self):
        """W-006's committed seed binds `§B{f} Stage` first; A-1.12 registers
        that `§B{f} Stage` does NOT preserve the row although `§B{f} stage`
        does, so this is still the property instance."""
        self.assert_class(
            seed_source("W-006-map-doubler", "unregistered-property-backed-escape-b"),
            "property")

    def test_other_instance(self):
        self.assert_class(
            seed_source("W-001-middleware-stage", "unregistered-other-receiver-escape-b"),
            "other-instance")

    def test_instance_method_group(self):
        self.assert_class(
            seed_source("W-001-middleware-stage",
                        "unregistered-method-group-receiver-escape-b"),
            "instance-method-group")

    def test_alias_of_this(self):
        self.assert_class(fixture_source("alias-of-this.calr.txt"), "alias-of-this")

    def test_inherited(self):
        self.assert_class(fixture_source("inherited.calr.txt"), "inherited")

    def test_every_registered_category_is_reachable(self):
        reached = set()
        for pair, role in (("W-001-middleware-stage", "unregistered-this-qualified-escape-b"),
                           ("W-001-middleware-stage", "unregistered-property-backed-escape-b"),
                           ("W-001-middleware-stage", "unregistered-other-receiver-escape-b"),
                           ("W-001-middleware-stage",
                            "unregistered-method-group-receiver-escape-b")):
            reached.add(PPW.classify_escape(seed_source(pair, role))[0])
        reached.add(PPW.classify_escape(fixture_source("alias-of-this.calr.txt"))[0])
        reached.add(PPW.classify_escape(fixture_source("inherited.calr.txt"))[0])
        reached.add("other")     # the analyzer's fallback, exercised below
        self.assertEqual(sorted(reached), sorted(PPW.ESCAPE_CATEGORIES))

    def test_the_fail_closed_controls_are_not_escapes(self):
        """A-1.12 registers the BOUNDARY as well as the hole: a local alias of
        the class's own field still charges, and a §B-bound §LAM with its row
        omitted still charges. "Unresolvable argument expressions, not fields
        and not aliasing" cannot quietly stop being true."""
        for pair, role in (("W-001-middleware-stage",
                            "unregistered-resolvable-alias-control-b"),
                           ("W-002-map-and-report", "unregistered-rowless-lambda-control-b")):
            with self.subTest(role=role):
                self.assertEqual(PPW.classify_escape(seed_source(pair, role)), (None, []))

    def test_invoking_is_charged_not_classified(self):
        """W-004 fails closed because its laundering site INVOKES the field
        rather than passing it: `§C{this.onChange}` takes the unknown-call path
        and errors. The passing/invoking split is the registered table's."""
        self.assertEqual(
            PPW.classify_escape(
                seed_source("W-004-counter-peek", "unregistered-this-qualified-escape-b")),
            (None, []))

    def test_charged_shortcuts_and_starters_classify_as_nothing(self):
        for pair_dir in PPW.PAIR_DIRS.values():
            for role in ("shortcut-a", "shortcut-b", "clean-a", "clean-b"):
                with self.subTest(pair=pair_dir, role=role):
                    self.assertEqual(PPW.classify_escape(seed_source(pair_dir, role)),
                                     (None, []))

    def test_the_ledger_carries_the_split(self):
        case = copy.deepcopy(ppw_epoch.load_case("miss-leg-a"))
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        case["cells"]["W-006-map-doubler"]["B"]["escapeSource"] = \
            "unregistered-property-backed-escape-b"
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        w001 = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "B")
        w006 = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-006-map-doubler" and c["arm"] == "B")
        self.assertEqual(w001["escapeCategories"], {"this-qualified": 2})
        self.assertEqual(w006["escapeCategories"], {"property": 2})

    def test_an_unclassifiable_arm_b_escape_is_published_as_other(self):
        case = copy.deepcopy(ppw_epoch.load_case("miss-leg-a"))
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        for pair in PPW.REGISTERED_BLIND:
            case["cells"][pair]["B"].pop("escapeSource", None)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        cell = next(c for c in analysis["perCell"]
                    if c["pair"] == "W-001-middleware-stage" and c["arm"] == "B")
        self.assertEqual(cell["escapeCategories"], {"other": 2})

    def test_a_this_style_arm_b_escape_does_not_fire_a_prime(self):
        """A-1.12, registered rather than discovered: route (a) reads UNMUTATED
        STARTERS. An agent writing `this.` in its SOLUTION makes no starter fail
        anything, so (a') cannot fire on this cause and the depressed leg-A
        delta is adjudicated as written — a MISS."""
        case = ppw_epoch.load_case("miss-leg-a")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        self.assertFalse(analysis["routes"]["a"]["fires"])
        self.assertFalse(analysis["routes"]["aPrime"]["fires"])
        self.assertEqual(analysis["verdict"], "MISS")
        self.assertIs(analysis["legA"]["meetsBar"], False)


class Ledger(unittest.TestCase):
    LEDGER = os.path.join(BENCH, "effect-rows-benefit-ledger.json")

    def test_the_committed_ledger_is_the_current_recomputation(self):
        committed = open(self.LEDGER, encoding="utf-8").read()
        self.assertEqual(committed, PPW.serialize(PPW.build_ledger(None)))

    def test_the_ledger_is_byte_stable_on_re_run(self):
        first = PPW.serialize(PPW.build_ledger(None))
        second = PPW.serialize(PPW.build_ledger(None))
        self.assertEqual(first, second)

    def test_the_ledger_is_timestamp_free_and_path_free(self):
        text = open(self.LEDGER, encoding="utf-8").read()
        self.assertNotRegex(text, r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}")
        self.assertNotIn("/Users/", text)
        self.assertNotIn("/tmp/", text)
        self.assertNotIn(BENCH, text)

    def test_the_ledger_carries_the_disclosed_limitations(self):
        ledger = json.load(open(self.LEDGER, encoding="utf-8"))
        self.assertEqual(sorted(d["id"] for d in ledger["disclosedLimitations"]),
                         ["W-004-arm-B-zero-is-compiler-behaviour",
                          "silence-signature-required",
                          "stderr-laundering-invisible-on-arm-A"])
        self.assertIn("SILENCE signature", ledger["escapeSemantics"])
        self.assertIn("not effect-observing", ledger["escapeSemantics"])

    def test_the_ledger_carries_what_a_1_12_names(self):
        ledger = json.load(open(self.LEDGER, encoding="utf-8"))
        for field in ("schemaVersion", "measuredCommit", "epoch", "pairs", "legBPairsFromPins",
                      "blindPairsFromPins", "perCell", "legA", "legB", "verdict", "route",
                      "precedence", "escapeSemantics", "ownGoal", "constants"):
            self.assertIn(field, ledger)
        self.assertEqual(len(ledger["pairs"]), 6)
        for pair in ledger["pairs"]:
            self.assertIn(pair["class"], ("blind", "warning-vs-error"))
            self.assertTrue(pair["starterBlobs"]["A"]["sha"])
            self.assertTrue(pair["starterBlobs"]["B"]["sha"])
            self.assertTrue(pair["effectObservingTests"])
            self.assertIn("starter", pair["frozenCompiles"])
            self.assertIn("shortcut", pair["frozenCompiles"])
            self.assertIn("clean", pair["frozenCompiles"])
        self.assertEqual(ledger["precedence"], "NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT")

    def test_before_the_epoch_runs_the_verdict_is_not_adjudicated(self):
        ledger = json.load(open(self.LEDGER, encoding="utf-8"))
        self.assertIs(ledger["epochRun"], False)
        self.assertEqual(ledger["verdict"], "NOT-ADJUDICATED")
        self.assertIn("has not run", ledger["reason"])

    def test_dropping_a_pair_changes_the_ledger(self):
        """Gate 10's pin: "dropping a pair fails the test"."""
        original = dict(PPW.PAIR_DIRS)
        try:
            PPW.PAIR_DIRS.pop("W-006")
            mutated = PPW.serialize(PPW.build_ledger(None))
        finally:
            PPW.PAIR_DIRS.clear()
            PPW.PAIR_DIRS.update(original)
        self.assertNotEqual(mutated, open(self.LEDGER, encoding="utf-8").read())

    def test_editing_one_frozen_multiset_changes_the_ledger(self):
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        pairs_root = ppw_epoch.scratch_pairs(tmp)
        path = os.path.join(pairs_root, "ppw-seeded-compiles.json")
        data = json.load(open(path, encoding="utf-8"))
        for cell in data["compiles"]:
            if cell["pair"] == "W-001-middleware-stage" and cell["role"] == "shortcut" \
                    and cell["arm"] == "B":
                cell["exitCode"] = 0
        json.dump(data, open(path, "w", encoding="utf-8"))
        mutated = PPW.serialize(PPW.build_ledger(None, pairs_root=pairs_root))
        self.assertNotEqual(mutated, open(self.LEDGER, encoding="utf-8").read())
        # the control: the same scratch tree, unedited, reproduces the bytes
        clean = ppw_epoch.scratch_pairs(os.path.join(tmp, "clean"))
        self.assertEqual(PPW.serialize(PPW.build_ledger(None, pairs_root=clean)),
                         open(self.LEDGER, encoding="utf-8").read())

    def test_a_run_epoch_fills_the_ledger_with_the_verdict(self):
        case = ppw_epoch.load_case("hit")
        tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmp, True)
        analysis, _ = PPW.analyze(ppw_epoch.build(case, tmp))
        ledger = PPW.build_ledger(analysis)
        self.assertIs(ledger["epochRun"], True)
        self.assertEqual(ledger["verdict"], "HIT")
        self.assertEqual(ledger["legBPairsFromPins"], PPW.REGISTERED_LEG_B)
        self.assertEqual(ledger["blindPairsFromPins"], PPW.REGISTERED_BLIND)
        self.assertEqual(ledger["measuredCommit"], PPW.ARM_B_COMMIT)
        self.assertEqual(len(ledger["perCell"]), 12)
        self.assertEqual([s["margin"] for s in ledger["legB"]["sensitivity"]],
                         [1.20, 1.30, 1.35])
        self.assertEqual(PPW.serialize(ledger), PPW.serialize(PPW.build_ledger(analysis)))


class HelpStatesTheRegisteredRules(unittest.TestCase):
    def test_help_names_the_rules_an_adjudicator_applies(self):
        doc = PPW.__doc__
        for phrase in ("NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT",
                       "AT LEAST ONE of the pair's named `effectObservingTests`",
                       "workspace that BUILT",
                       "MEDIAN over BLIND pairs",
                       "one-sided 95 % LOWER bound of that delta exceeds 0",
                       "1.20", "1.30", "1.35",
                       "THE VERDICT IS READ AT\n  1.20 AND ONLY AT 1.20",
                       "NEVER FROM A SCRIPT DEFAULT",
                       "INERT BY CONSTRUCTION",
                       "OWN-GOAL CLAUSE",
                       "this-qualified / property / inherited / alias-of-this /",
                       "SHAPE-REALIZED INDICATOR",
                       "THE DRY RUN SIZES N AND NEVER MOVES THE BAR",
                       "EMITS NO VERDICT AT ALL",
                       "UPPER confidence bound",
                       "no fallback to a default denominator",
                       "`ppL5` / `ppL6` / `ppW5`",
                       "AGGREGATE Failed: count",
                       "SILENCE signature",
                       "silent-but-WRONG"):
            self.assertIn(phrase, doc, phrase)


if __name__ == "__main__":
    unittest.main()
