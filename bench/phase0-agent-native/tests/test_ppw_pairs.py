#!/usr/bin/env python3
"""PP-W-rows pair fixtures (roadmap v0.16 §4.1; §2.3 S3 (c)) — structural and
measurement pins for the six `bench/phase0-agent-native/pairs/W-00x-*` pairs
and the frozen artifact `pairs/ppw-seeded-compiles.json`.

Always-on checks (no compiler needed):

  * every pair directory has spec.md, pair.json, starter-a/, starter-b/,
    tests/ (with an xUnit test file and shims/TestShim.calor.cs — the layout
    run-pair.sh materializes, both arms being calor arms), and the seeded
    shortcut/clean directories for both arms;
  * every starter blob's `git hash-object` SHA equals the SHA pinned in
    pair.json (the 7d621c0d freeze);
  * arm A's config carries permissiveEffects: true + controlArmKind "pre-rows";
    arm B's config is the gates-doc §1 pin verbatim;
  * legBPairs = exactly the five pairs with legB: true (W-005 excluded);
  * the blind set is exactly {W-001, W-004, W-006};
  * the recorded JSON multisets match the registered class: blind => no
    Calor0410 on A and `error Calor0410` on B for the shortcut;
    warning-vs-error => `warning Calor0410` on A;
  * every unmutated starter's recorded multiset matches its frozen baseline
    (A-1.11.1 for A2; the row-less Calor0418 warnings for the before/ files);
  * every clean seed is recorded as exit 0 with no Calor0410 on either arm;
  * the JSON's blobSha for every recorded source equals the file on disk.

Recompute check (skipped with a reason unless BOTH arm builds are present):

  * PPW_ARM_A_DLL and PPW_ARM_B_DLL point at the v0.14.3 and v0.15.0
    calor.dll builds; `ppw-compile.py --check` recompiles everything under the
    pinned invocation and must reproduce the committed multisets exactly.

Run:  python3 bench/phase0-agent-native/tests/test_ppw_pairs.py
"""

import importlib.util
import json
import os
import re
import subprocess
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BENCH = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(BENCH))
PAIRS = os.path.join(BENCH, "pairs")
JSON_PATH = os.path.join(PAIRS, "ppw-seeded-compiles.json")
COMPILE_PY = os.path.join(BENCH, "ppw-compile.py")

PAIR_IDS = [
    "W-001-middleware-stage",
    "W-002-map-and-report",
    "W-003-match-fallback",
    "W-004-counter-peek",
    "W-005-pipeline-trace",
    "W-006-map-doubler",
]
BLIND = {"W-001-middleware-stage", "W-004-counter-peek", "W-006-map-doubler"}
LEG_B = {"W-001-middleware-stage", "W-002-map-and-report", "W-003-match-fallback",
         "W-004-counter-peek", "W-006-map-doubler"}

# Roadmap §4.1 table, verified with `git ls-tree 7d621c0d`.
FROZEN_BLOBS = {
    "W-001-middleware-stage": ("2d351d101f5972cf1f5c4cb5640be3bd2870974f", "e5ee81e24abcf38f9111407d8e5c635a482a7ed2"),
    "W-002-map-and-report": ("9f108655fcc376a721efd3e4b1be187aeb4da5e4", "0885b3dd40fcff28c51de72860d47a32db60bf8c"),
    "W-003-match-fallback": ("1f36ea6e36ac331679d4672b17294cd100a5c25e", "c1ce75179ff0ab0b80bd74e2e7f6709ffb542bfe"),
    "W-004-counter-peek": ("f2dca4a6a71e28266e27ccfd56e4d2a06bc5fd79", "05ddc23d342e8652ae59be242d29dd0b8a3ca5c4"),
    "W-005-pipeline-trace": ("d49d00178aff477288e5e0527e39834865820761", "93ecdf1605c4e220313c1dd76b3291d3a79bb705"),
    "W-006-map-doubler": ("9f108655fcc376a721efd3e4b1be187aeb4da5e4", "0885b3dd40fcff28c51de72860d47a32db60bf8c"),
}

# Frozen unmutated-starter multisets per arm: (exit, [(line, col, code, severity), ...]).
# Arm A: the row-less before/ programs under the waiver (A-1.11 threshold column: Calor0418 at
# these locations, demoted to warnings). Arm B: A-1.11.1's corrected post-E4 control —
# exit 0 / zero diagnostics for the four A3 fixtures; A2 exit 1 with 1x Calor0410 'unknown' at
# (23,9) + 2x Calor0411 at (26,24) and (28,19).
FROZEN_STARTERS = {
    ("W-001-middleware-stage", "A"): (0, [(4, 19, "Calor0418", "warning"), (5, 20, "Calor0418", "warning")]),
    ("W-001-middleware-stage", "B"): (0, []),
    ("W-002-map-and-report", "A"): (0, [(7, 22, "Calor0418", "warning")]),
    ("W-002-map-and-report", "B"): (0, []),
    ("W-003-match-fallback", "A"): (0, [(5, 10, "Calor0418", "warning"), (6, 8, "Calor0418", "warning")]),
    ("W-003-match-fallback", "B"): (0, []),
    ("W-004-counter-peek", "A"): (0, [(6, 7, "Calor0418", "warning")]),
    ("W-004-counter-peek", "B"): (0, []),
    ("W-005-pipeline-trace", "A"): (0, [(25, 27, "Calor0418", "warning")]),
    ("W-005-pipeline-trace", "B"): (1, [(23, 9, "Calor0410", "error"), (26, 24, "Calor0411", "warning"), (28, 19, "Calor0411", "warning")]),
    ("W-006-map-doubler", "A"): (0, [(7, 22, "Calor0418", "warning")]),
    ("W-006-map-doubler", "B"): (0, []),
}


def load_compile_module():
    spec = importlib.util.spec_from_file_location("ppw_compile", COMPILE_PY)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_pair(pid):
    with open(os.path.join(PAIRS, pid, "pair.json"), encoding="utf-8") as fh:
        return json.load(fh)


def git_blob_sha(path):
    return subprocess.run(["git", "hash-object", path], cwd=REPO, capture_output=True,
                          text=True, check=True).stdout.strip()


def load_json():
    with open(JSON_PATH, encoding="utf-8") as fh:
        return json.load(fh)


def rows(doc, pid, role, arm):
    return [c for c in doc["compiles"] if c["pair"] == pid and c["role"] == role and c["arm"] == arm]


def codes(row):
    return [d["code"] for d in row["diagnostics"]]


def sev_codes(row):
    return [(d["severity"], d["code"]) for d in row["diagnostics"]]


class PairLayout(unittest.TestCase):
    def test_every_pair_has_the_required_files(self):
        for pid in PAIR_IDS:
            pdir = os.path.join(PAIRS, pid)
            for rel in ("spec.md", "pair.json", "starter-a", "starter-b", "tests",
                        "tests/shims/TestShim.calor.cs",
                        "seeded/shortcut-a", "seeded/shortcut-b", "seeded/clean-a", "seeded/clean-b"):
                self.assertTrue(os.path.exists(os.path.join(pdir, rel)), f"{pid}: missing {rel}")
            tests = [f for f in os.listdir(os.path.join(pdir, "tests")) if f.endswith(".cs")]
            self.assertTrue(tests, f"{pid}: no held-out test file")
            for sub in ("starter-a", "starter-b", "seeded/shortcut-a", "seeded/shortcut-b",
                        "seeded/clean-a", "seeded/clean-b"):
                calrs = [f for f in os.listdir(os.path.join(pdir, sub)) if f.endswith(".calr")]
                self.assertEqual(1, len(calrs), f"{pid}/{sub}: expected exactly one .calr")

    def test_sibling_w001s_is_a_note_not_a_pair(self):
        self.assertTrue(os.path.isfile(os.path.join(PAIRS, "W-001-middleware-stage", "sibling-W-001s.md")))
        self.assertFalse(any(d.startswith("W-001s") for d in os.listdir(PAIRS)))
        # No PP-W pair directory beyond the six registered ones: the A3-match fourth
        # blind cell and the recorded confounds are seeds, never pairs (§4.1), and an
        # unregistered W-0xx pair would also slip past the census walkers' path rule.
        stray = sorted(d for d in os.listdir(PAIRS)
                       if re.match(r"^W-\d{3}-", d) and d not in PAIR_IDS)
        self.assertEqual([], stray, "unregistered PP-W pair directory")

    def test_spec_is_arm_neutral(self):
        # The task statement must not steer the agent by naming the mechanism under test.
        for pid in PAIR_IDS:
            with open(os.path.join(PAIRS, pid, "spec.md"), encoding="utf-8") as fh:
                spec = fh.read().lower()
            for banned in ("effect row", "<eff ", "permissive", "v0.14", "v0.15", "arm a", "arm b", "launder"):
                self.assertNotIn(banned, spec, f"{pid}/spec.md mentions '{banned}'")
            self.assertIn("escapedbugs", spec, f"{pid}/spec.md must record the escapedBugs semantics")
            self.assertIn("heldoutpassed", spec, f"{pid}/spec.md must record the heldoutPassed semantics")

    def test_effect_observing_tests_exist_in_the_heldout_suite(self):
        for pid in PAIR_IDS:
            manifest = load_pair(pid)
            hdir = os.path.join(PAIRS, pid, manifest["tests"]["path"])
            src = ""
            for f in os.listdir(hdir):
                if f.endswith(".cs"):
                    with open(os.path.join(hdir, f), encoding="utf-8") as fh:
                        src += fh.read()
            names = manifest["tests"]["effectObservingTests"]
            self.assertTrue(names, f"{pid}: no effectObservingTests listed")
            for name in names:
                self.assertIn(f"void {name}()", src, f"{pid}: effect-observing test {name} not found")
            self.assertIn("Console.SetOut", src, f"{pid}: held-out suite does not capture the console")


class PairManifests(unittest.TestCase):
    def test_starter_blobs_match_pair_json_and_the_freeze(self):
        for pid in PAIR_IDS:
            manifest = load_pair(pid)
            by_arm = {arm["armId"]: arm for arm in manifest["arms"].values()}
            for arm_id, frozen in zip(("a", "b"), FROZEN_BLOBS[pid]):
                arm = by_arm[arm_id]
                self.assertEqual(frozen, arm["starterBlob"]["sha"], f"{pid} arm {arm_id}: pair.json sha != §4.1 table")
                fixture = os.path.join(PAIRS, pid, arm["fixture"])
                calr = [f for f in os.listdir(fixture) if f.endswith(".calr")][0]
                self.assertEqual(frozen, git_blob_sha(os.path.join(fixture, calr)),
                                 f"{pid} arm {arm_id}: starter on disk is not the frozen blob")
                self.assertEqual("7d621c0d", arm["starterBlob"]["frozenAt"])

    def test_arm_configs(self):
        for pid in PAIR_IDS:
            manifest = load_pair(pid)
            self.assertEqual("calor", manifest["armKind"])
            a = manifest["arms"]["calor-pre-rows"]
            b = manifest["arms"]["calor"]
            self.assertEqual("a", a["armId"])
            self.assertEqual("b", b["armId"])
            self.assertEqual({"enforceEffects": True, "permissiveEffects": True, "controlArmKind": "pre-rows",
                              "contractMode": "debug", "z3Required": True}, a["config"], pid)
            self.assertEqual({"enforceEffects": True, "permissiveEffects": False,
                              "contractMode": "debug", "z3Required": True}, b["config"], pid)
            self.assertTrue(a["compilerCommit"].startswith("63316987"), pid)
            self.assertTrue(b["compilerCommit"].startswith("3bb2601e"), pid)
            self.assertEqual("starter-a", a["fixture"])
            self.assertEqual("starter-b", b["fixture"])
            self.assertEqual(10, manifest["iterationBudget"])

    def test_classes_and_leg_b_denominator(self):
        blind = {pid for pid in PAIR_IDS if load_pair(pid)["class"] == "blind"}
        wve = {pid for pid in PAIR_IDS if load_pair(pid)["class"] == "warning-vs-error"}
        self.assertEqual(BLIND, blind)
        self.assertEqual(set(PAIR_IDS) - BLIND, wve)
        self.assertEqual(3, len(blind))
        leg_b = {pid for pid in PAIR_IDS if load_pair(pid)["legB"]}
        self.assertEqual(LEG_B, leg_b)
        self.assertEqual(5, len(leg_b))
        self.assertFalse(load_pair("W-005-pipeline-trace")["legB"])
        self.assertIn("legBExclusionReason", load_pair("W-005-pipeline-trace"))
        self.assertFalse(load_pair("W-005-pipeline-trace")["arms"]["calor"]["starterBuilds"])


class FrozenCompiles(unittest.TestCase):
    """The committed multisets, read back — the numbers A-1.12 will cite."""

    @classmethod
    def setUpClass(cls):
        cls.doc = load_json()

    def test_header(self):
        self.assertEqual(1, self.doc["schemaVersion"])
        self.assertEqual("7d621c0d", self.doc["starterFreezeCommit"])
        self.assertEqual(["--permissive-effects"], self.doc["arms"]["A"]["flags"])
        self.assertEqual([], self.doc["arms"]["B"]["flags"])
        self.assertEqual("pre-rows", self.doc["arms"]["A"]["controlArmKind"])
        self.assertTrue(self.doc["arms"]["A"]["compilerCommit"].startswith("63316987"))
        self.assertTrue(self.doc["arms"]["B"]["compilerCommit"].startswith("3bb2601e"))
        self.assertEqual(sorted(LEG_B), sorted(self.doc["legBPairs"]))
        self.assertEqual(sorted(BLIND), sorted(self.doc["blindPairs"]))

    def test_the_json_covers_exactly_the_sources_on_disk(self):
        # Without this, a seed could enter the tree (a directory plus a pair.json
        # entry) and never be compiled: every other check here reads the JSON, so an
        # unrecorded source would be invisible until the DLL-gated recompute ran.
        # This runs everywhere, with no compiler.
        expected = {(pid, role, arm.upper(), rel)
                    for pid, role, arm, rel in load_compile_module().enumerate_sources()}
        got = {(c["pair"], c["role"], c["arm"], c["path"]) for c in self.doc["compiles"]}
        self.assertEqual(expected, got, "ppw-seeded-compiles.json does not cover exactly the "
                                        "sources ppw-compile.py enumerates — regenerate it")
        self.assertEqual(46, len(self.doc["compiles"]))
        self.assertEqual(len(expected), len(self.doc["compiles"]), "duplicate rows")

    def test_every_source_is_recorded_once_per_arm_and_blob_matches_disk(self):
        seen = set()
        for c in self.doc["compiles"]:
            key = (c["pair"], c["role"], c["arm"], c["path"])
            self.assertNotIn(key, seen, f"duplicate row {key}")
            seen.add(key)
            self.assertEqual(git_blob_sha(os.path.join(REPO, c["path"])), c["blobSha"], f"{key}: blob drifted")
            for d in c["diagnostics"]:
                self.assertEqual(["line", "column", "code", "severity", "text"], list(d.keys()))
            keys = [(d["line"], d["column"], d["code"], d["severity"], d["text"]) for d in c["diagnostics"]]
            self.assertEqual(sorted(keys), keys, f"{key}: diagnostics not in pinned sort order")
        for pid in PAIR_IDS:
            for role in ("starter", "shortcut", "clean"):
                for arm in ("A", "B"):
                    self.assertEqual(1, len(rows(self.doc, pid, role, arm)), f"{pid}/{role}/{arm} missing")

    def test_unmutated_starters_reproduce_their_frozen_multisets(self):
        # Route (a) of §4.1's NOT-ADJUDICATED map: any drift here drops the pair.
        for (pid, arm), (exit_code, diags) in FROZEN_STARTERS.items():
            row = rows(self.doc, pid, "starter", arm)[0]
            self.assertEqual(exit_code, row["exitCode"], f"{pid}/{arm} starter exit")
            got = [(d["line"], d["column"], d["code"], d["severity"]) for d in row["diagnostics"]]
            self.assertEqual(diags, got, f"{pid}/{arm} starter multiset")
            self.assertEqual(exit_code == 0, row["emitted"], f"{pid}/{arm} starter emitted")

    def test_shortcut_emissions_match_the_registered_class(self):
        for pid in PAIR_IDS:
            a = rows(self.doc, pid, "shortcut", "A")[0]
            b = rows(self.doc, pid, "shortcut", "B")[0]
            if pid in BLIND:
                self.assertNotIn("Calor0410", codes(a), f"{pid}: blind cell must draw no Calor0410 on arm A")
                self.assertEqual(0, a["exitCode"], f"{pid}: blind shortcut must build on arm A")
                self.assertTrue(all(c == "Calor0418" for c in codes(a)),
                                f"{pid}: arm A may show only the pre-existing Calor0418 warnings")
                self.assertIn(("error", "Calor0410"), sev_codes(b), f"{pid}: arm B must reject with error Calor0410")
                self.assertEqual(1, b["exitCode"])
            else:
                self.assertIn(("warning", "Calor0410"), sev_codes(a), f"{pid}: warning-vs-error cell must warn Calor0410 on arm A")
                self.assertEqual(0, a["exitCode"], f"{pid}: the warning is a warning — arm A still builds")
                self.assertEqual(1, b["exitCode"], f"{pid}: arm B must fail")
                if pid == "W-005-pipeline-trace":
                    # Arm B's starter does not build: the shortcut shows the starter's own multiset
                    # (Calor0410 'unknown'), and only the repaired variant shows the 'cw' error.
                    self.assertIn(("error", "Calor0410"), sev_codes(b))
                    rep = rows(self.doc, pid, "shortcut-b-repaired", "B")[0]
                    self.assertEqual(1, rep["exitCode"])
                    self.assertTrue(any(d["code"] == "Calor0410" and "'cw'" in d["text"] for d in rep["diagnostics"]))
                else:
                    self.assertIn(("error", "Calor0410"), sev_codes(b))

    def test_blind_shortcuts_name_the_laundering_function_on_arm_b(self):
        expected = {
            "W-001-middleware-stage": "Function 'Twice' uses effect 'cw'",
            "W-004-counter-peek": "Function 'Peek' uses effect 'cw'",
            "W-006-map-doubler": "Function 'Twice' uses effect 'cw'",
        }
        for pid, text in expected.items():
            b = rows(self.doc, pid, "shortcut", "B")[0]
            self.assertTrue(any(d["code"] == "Calor0410" and text in d["text"] for d in b["diagnostics"]),
                            f"{pid}: arm B message must read '{text}'")

    def test_clean_seeds_build_without_laundering_on_both_arms(self):
        for pid in PAIR_IDS:
            for arm in ("A", "B"):
                row = rows(self.doc, pid, "clean", arm)[0]
                self.assertEqual(0, row["exitCode"], f"{pid}/{arm} clean seed must build")
                self.assertTrue(row["emitted"])
                self.assertNotIn("Calor0410", codes(row), f"{pid}/{arm} clean seed must not launder")
                if arm == "B":
                    self.assertEqual([], row["diagnostics"], f"{pid}/B clean seed must be diagnostic-free")
                else:
                    self.assertTrue(all(c == "Calor0418" for c in codes(row)),
                                    f"{pid}/A clean seed may carry only the waiver's Calor0418 warnings")

    def test_sibling_w001s_is_warning_vs_error(self):
        a = rows(self.doc, "W-001-middleware-stage", "sibling-W-001s", "A")[0]
        b = rows(self.doc, "W-001-middleware-stage", "sibling-W-001s", "B")[0]
        self.assertIn(("warning", "Calor0410"), sev_codes(a))
        self.assertEqual(0, a["exitCode"])
        self.assertIn(("error", "Calor0410"), sev_codes(b))

    def test_this_qualified_escape_is_recorded_on_the_treatment_arm(self):
        # Issue #1136, recorded before any agent run: on arm B `§A this.field` makes the
        # row variable instantiate to Unknown — warning Calor0425, exit 0, nothing charged
        # — so two of the three blind cells have a laundering route through the TREATMENT
        # arm. A-1.12 registers it as a pre-registered confound on leg A's direction.
        for pid in ("W-001-middleware-stage", "W-006-map-doubler"):
            row = rows(self.doc, pid, "unregistered-this-qualified-escape", "B")[0]
            self.assertEqual(0, row["exitCode"], f"{pid}: the escape must BUILD on arm B")
            self.assertTrue(row["emitted"])
            self.assertNotIn("Calor0410", codes(row), f"{pid}: nothing is charged — that is the defect")
            self.assertTrue(all(c == "Calor0425" for c in codes(row)) and codes(row),
                            f"{pid}: expected Calor0425 warnings only")
            self.assertTrue(all(d["severity"] == "warning" for d in row["diagnostics"]))
        # The negative control: direct invocation of a this.-qualified field fails closed.
        control = rows(self.doc, "W-004-counter-peek", "unregistered-this-qualified-escape", "B")[0]
        self.assertEqual(1, control["exitCode"])
        self.assertIn(("error", "Calor0410"), sev_codes(control))

    def test_specs_pin_the_escape_indicator_and_the_shape_indicator(self):
        for pid in PAIR_IDS:
            manifest = load_pair(pid)
            with open(os.path.join(PAIRS, pid, "spec.md"), encoding="utf-8") as fh:
                spec = " ".join(fh.read().split())  # the spec is hard-wrapped; match on words
            self.assertIn("on a workspace that built", spec, f"{pid}: escape indicator not pinned in spec")
            self.assertIn("does not build is not an escape", spec, pid)
            self.assertIn("at least one of tests.effectObservingTests failed on a workspace that BUILT",
                          manifest["escapeSemantics"], pid)
            self.assertIn("non-building declared-done", manifest["escapeSemantics"], pid)
            indicator = manifest["shapeRealizedIndicator"]
            for key in ("name", "question", "sourceRegex", "measuredOn", "reportedBy", "why"):
                self.assertTrue(indicator.get(key), f"{pid}: shapeRealizedIndicator.{key} missing")
            self.assertIn(indicator["name"], spec, f"{pid}: shape indicator not disclosed in the spec")
            re.compile(indicator["sourceRegex"])
            # The indicator must actually separate the two reference solutions it exists to
            # tell apart: the seeded shortcut realizes the shape, and (for the blind cells)
            # the published lambda sibling does not.
            shortcut = os.path.join(PAIRS, pid, manifest["seeded"]["shortcut"]["b"])
            body = "".join(open(os.path.join(shortcut, f), encoding="utf-8").read()
                           for f in sorted(os.listdir(shortcut)) if f.endswith(".calr"))
            self.assertRegex(body, indicator["sourceRegex"],
                             f"{pid}: the seeded shortcut does not match its own shape indicator")

    def test_unregistered_extras_are_recorded_as_measured(self):
        # §4.1: the A3-match field shape with a field for onNone is blind on both arms.
        a = rows(self.doc, "W-003-match-fallback", "unregistered-fourth-blind-cell", "A")[0]
        b = rows(self.doc, "W-003-match-fallback", "unregistered-fourth-blind-cell", "B")[0]
        self.assertEqual(0, a["exitCode"])
        self.assertEqual(["Calor0418", "Calor0418"], codes(a))
        self.assertTrue(any(d["code"] == "Calor0410" and "'Twice'" in d["text"] for d in b["diagnostics"]))
        # §10 round 4 open question 2: the module-level method group from a class body draws
        # Calor1002 on arm A, and reproduces on arm B with the row declared.
        for arm in ("A", "B"):
            row = rows(self.doc, "W-003-match-fallback", "unregistered-calor1002-confound", arm)[0]
            self.assertEqual(1, row["exitCode"])
            self.assertTrue(all(c == "Calor1002" or c == "Calor0418" for c in codes(row)), arm)
            self.assertIn("Calor1002", codes(row), arm)


class Recompute(unittest.TestCase):
    """Recompiles everything under the pinned invocation when both arm builds are present."""

    @unittest.skipUnless(os.environ.get("PPW_ARM_A_DLL") and os.environ.get("PPW_ARM_B_DLL"),
                         "set PPW_ARM_A_DLL (v0.14.3 calor.dll) and PPW_ARM_B_DLL (v0.15.0 calor.dll) to recompute")
    def test_committed_multisets_match_a_recompute(self):
        p = subprocess.run([sys.executable, COMPILE_PY, "--check"], cwd=REPO, capture_output=True, text=True)
        self.assertEqual(0, p.returncode, p.stdout + p.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
