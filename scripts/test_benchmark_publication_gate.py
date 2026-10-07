#!/usr/bin/env python3
"""0.24 B2 (#1422) end-to-end tests: benchmark publication refuses incomparable results.

Each refusal is exercised on a throwaway git repository with an origin, built from a small
synthetic B1-shaped packet, and the workflow's own step scripts are executed against it. The real
B1 packet is checked by the same functions. Run: python3 scripts/test_benchmark_publication_gate.py
"""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT / "scripts"))
import benchmark_publication_gate as gate  # noqa: E402

WORKFLOW = REPO_ROOT / ".github/workflows/benchmark.yml"
GATE_STEP = "Refuse incomparable or non-equivalent benchmark results (#1422)"
RESTORE_STEP = "Keep historical benchmark files historical (#1422)"
METRIC = {
    "metric": "TokenEconomics/CompositeTokenEconomics",
    "population": "registered manifest.benchmarks pairs dispositioned EQUIVALENT by the reconciled oracle result",
    "samplingUnit": "program-pair",
    "intervalLabel": "corpus-resampling interval for this fixed corpus; not a confidence interval for any population",
    "overall": {"pairs": 2, "geometricMeanR": "1.2", "interval95": ["1.1", "1.3"], "medianR": "1.2"},
}
LABEL = ("Describes this fixed, author-built corpus only. r is a static source-size ratio of two committed files, "
         "not a coding-agent outcome or a language advantage. EQUIVALENT means no observed disagreement on the "
         "registered finite inputs; it is not a proof and not a correctness claim about either arm.")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def run(cwd: Path, *args: str, env: dict | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(list(args), cwd=cwd, capture_output=True, text=True, env=env)


def steps() -> dict:
    text = WORKFLOW.read_text(encoding="utf-8")
    return {block.splitlines()[0].strip(): block for block in text.split("\n      - name: ")[1:]}


def step_script(name: str) -> str:
    block = steps()[name]
    body = block.split("        run: |\n", 1)[1]
    lines = []
    for line in body.splitlines():
        if line and not line.startswith("          "):
            break
        lines.append(line)
    return textwrap.dedent("\n".join(lines)) + "\n"


class Fixture:
    """A git repository with an origin holding a synthetic, internally consistent B1 packet."""

    PAIRS = [("Cat/A", "EQUIVALENT", True, "manifest.benchmarks"), ("Cat/B", "EQUIVALENT", True, "manifest.benchmarks"),
             ("Cat/C", "NOT-EQUIVALENT", False, "manifest.benchmarks"), ("Cat/D", "UNCLASSIFIED", False, "manifest.benchmarks"),
             ("Cat/E", "EXCLUDED-PRE-REGISTERED", False, "manifest.bugScenarios")]
    CONTRACT_DOC = "docs/plans/contract.md"  # a sealed file outside every INPUT_PATHS entry

    def __init__(self, tmp: Path):
        self.tmp, self.root, self.origin = tmp, tmp / "work", tmp / "origin.git"
        self.regen = tmp / "regen"
        run(tmp, "git", "init", "-q", "--bare", "-b", "main", str(self.origin))
        run(tmp, "git", "init", "-q", "-b", "main", str(self.root))
        for key, value in (("user.email", "t@example.com"), ("user.name", "t"), ("commit.gpgsign", "false")):
            self.git("config", key, value)
        self.git("remote", "add", "origin", str(self.origin))
        for path in ("src/a.txt", "tests/Calor.Evaluation/a.txt", "website/public/data/benchmark-results.json",
                     "scripts/benchmark_publication_gate.py", *[p for p in gate.INPUT_PATHS if p.endswith((".cs", ".csproj"))],
                     f"{gate.VALIDATOR}/CandidateManifestTests.cs"):
            self.write(path, "{}\n")
        shutil.copy(REPO_ROOT / "scripts/benchmark_publication_gate.py", self.root / "scripts/benchmark_publication_gate.py")
        self.write_json(gate.STAMP_INDEX, {"publicationStamps": []})
        self.contract = {"amendmentLog": [{"version": "1.0.1", "change": "acceptance"}]}
        self.write_contract()
        self.registered = []
        for pair_id, *_ in self.PAIRS:
            name = pair_id.split("/")[1]
            row = {"pairId": pair_id, "source": dict((p[0], p[3]) for p in self.PAIRS)[pair_id],
                   "taskStatement": f"category: Cat\nname: {name}\n", "taskStatementSha256": "0" * 64,
                   "inputSet": {}, "expectedOutputs": {}, "failureBehavior": "oracle-v1-default"}
            for side, ext in (("calor", "calr"), ("csharp", "cs")):
                path = f"tests/TestData/Benchmarks/Cat/{name}.{ext}"
                self.write(path, f"{name} {side}\n")
                row[f"{side}Path"], row[f"{side}Sha256"] = path, sha((self.root / path).read_bytes())
            self.registered.append(row)
        self.key = {"pairManifestSha256": "", "metricSetSha256": "1" * 64, "metricImplementationVersion": "m@1",
                    "aggregationMethod": "agg-v1", "samplingUnit": "program-pair", "runCount": 2,
                    "generatorVersion": gate.GENERATOR, "exclusionsSha256": "2" * 64}
        self.write_json(f"{gate.REGISTRATION}/pairs.json", {"pairs": self.registered})
        self.write_json(f"{gate.REGISTRATION}/registration.json", {"comparability": self.key})
        self.seal(gate.REGISTRATION, lf=True)
        self.registration_commit = self.commit_and_push("registration")
        copy = self.root / "scripts/benchmark_publication_gate.py"
        copy.write_text(copy.read_text().replace(gate.ACCEPTED_REGISTRATION_COMMITS[0], self.registration_commit))
        self.manifest = {"generatorVersion": gate.GENERATOR, "registrationCommit": self.registration_commit,
                         "pairCount": len(self.PAIRS),
                         "registrationPairManifestSha256": self.file_sha(f"{gate.REGISTRATION}/pairs.json"),
                         "pairs": [dict(row, disposition=d, included=inc) for row, (_, d, inc, _) in zip(self.registered, self.PAIRS)]}
        self.results = {"generatorVersion": gate.GENERATOR, "comparability": dict(self.key),
                        "registrationPairManifestSha256": self.manifest["registrationPairManifestSha256"],
                        "includedPerCategory": {"Cat": 2}, "metric": json.loads(json.dumps(METRIC)), "label": LABEL}
        self.publish_results("results")

    def git(self, *args: str) -> str:
        result = run(self.root, "git", *args)
        if result.returncode != 0:
            raise AssertionError(f"git {args}: {result.stderr}")
        return result.stdout.strip()

    def write(self, relative: str, text: str) -> None:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def write_json(self, relative: str, doc: dict) -> None:
        self.write(relative, json.dumps(doc, indent=2) + "\n")

    def file_sha(self, relative: str) -> str:
        return sha((self.root / relative).read_bytes())

    def seal(self, directory: str, lf: bool) -> None:
        files = {f"{directory}/{p.name}": sha(p.read_bytes()) for p in sorted((self.root / directory).iterdir())
                 if p.name != "sha256.json"}
        self.write_json(f"{directory}/sha256.json", {"files": files})

    def write_contract(self) -> None:
        self.write_json(gate.CONTRACT, self.contract)
        if not (self.root / self.CONTRACT_DOC).exists():
            self.write(self.CONTRACT_DOC, "# contract\n")
        self.write_json(gate.CONTRACT_SEAL, {"files": {gate.CONTRACT: self.file_sha(gate.CONTRACT),
                                                       self.CONTRACT_DOC: self.file_sha(self.CONTRACT_DOC)}})

    def publish_results(self, message: str, reseal: bool = True, push: bool = True) -> str:
        """Write the manifest and results (deriving counts from the manifest) and commit them."""
        for i, field in ((1, "firstOracleResultSha256"), (2, "oracleResultSha256")):
            self.write_json(f"{gate.RESULTS}/oracle-run-{i}.json", {"valid": i == 2})
            self.manifest[field] = self.file_sha(f"{gate.RESULTS}/oracle-run-{i}.json")
        self.write_json(f"{gate.RESULTS}/pair-manifest.json", self.manifest)
        counts: dict = {}
        for row in self.manifest["pairs"]:
            counts[row["disposition"]] = counts.get(row["disposition"], 0) + 1
        self.results.setdefault("byDisposition", counts)
        self.results.setdefault("denominator", len(self.manifest["pairs"]))
        self.results["comparability"].setdefault("pairManifestSha256", "")
        if not self.results["comparability"]["pairManifestSha256"] or reseal:
            self.results["comparability"]["pairManifestSha256"] = self.file_sha(f"{gate.RESULTS}/pair-manifest.json")
        self.write_json(f"{gate.RESULTS}/results.json", self.results)
        for i in (1, 2):
            self.write_json(f"{gate.RESULTS}/metrics-run-{i}.json", {"pairs": ["Cat/A", "Cat/B"]})
        self.seal(gate.RESULTS, lf=False)
        commit = self.commit_and_push(message, push=push)
        self.copy_regenerated()
        return commit

    def copy_regenerated(self) -> None:
        shutil.rmtree(self.regen, ignore_errors=True)
        self.regen.mkdir()
        for name in gate.REGENERATED_FILES:
            shutil.copy(self.root / gate.RESULTS / name, self.regen / name)

    def commit_and_push(self, message: str, push: bool = True, allow_empty: bool = False) -> str:
        self.git("add", "-A")
        self.git("commit", "-q", "-m", message, *(["--allow-empty"] if allow_empty else []))
        if push:
            self.git("push", "-q", "origin", "HEAD:refs/heads/main")
            self.git("fetch", "-q", "origin", "+refs/heads/main:refs/remotes/origin/main")
        return self.git("rev-parse", "HEAD")

    def check(self, commit: str | None = None) -> tuple:
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = gate.main(["check", "--repo", str(self.root), "--commit", commit or self.git("rev-parse", "HEAD"),
                              "--regenerated", str(self.regen)])
        return code, out.getvalue() + err.getvalue()

    def changed(self) -> list:
        status = run(self.root, "git", "status", "--porcelain", "--untracked-files=all").stdout
        return sorted(line[3:] for line in status.splitlines())

    def headline(self) -> dict | None:
        path = self.root / gate.HEADLINE
        return json.loads(path.read_text()) if path.exists() else None


class GateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="b2-gate-"))
        self.f = Fixture(self.tmp)
        self.accepted = gate.ACCEPTED_REGISTRATION_COMMITS
        gate.ACCEPTED_REGISTRATION_COMMITS = (self.f.registration_commit,)

    def tearDown(self) -> None:
        gate.ACCEPTED_REGISTRATION_COMMITS = self.accepted
        shutil.rmtree(self.tmp, ignore_errors=True)

    def assertRefused(self, code_name: str) -> str:
        code, text = self.f.check()
        self.assertEqual(1, code, text)
        self.assertIn(f" {code_name} ", text)
        self.assertIn("REFUSED", text)
        self.assertEqual([], self.f.changed(), "a refusal must write nothing (no headline, no index entry)")
        return text

    def test_consistent_packet_writes_one_headline_with_limits_and_provenance(self) -> None:
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        headline = self.f.headline()
        self.assertEqual(METRIC, headline["headline"])
        self.assertIsNone(headline["comparableWithPublished"])
        self.assertEqual({"registered": 5, "excludedPreRegistered": 1, "sentToOracle": 4, "included": 2},
                         {k: v for k, v in headline["denominator"].items() if k != "byDisposition"})
        self.assertIn("2 pairs are included of the 4 pairs sent to the oracle", " ".join(headline["limitations"]))
        self.assertIn("not a claim that either arm is correct", " ".join(headline["limitations"]))
        commit = self.f.git("rev-parse", "HEAD")
        self.assertEqual(commit, headline["provenance"]["commit"])
        self.assertEqual(self.f.git("rev-parse", "HEAD^{tree}"), headline["provenance"]["treeHashes"]["/"])
        entry = json.loads((self.f.root / gate.STAMP_INDEX).read_text())["publicationStamps"][-1]
        self.assertEqual((gate.HEADLINE, "/provenance/commit", commit), (entry["path"], entry["stampPointer"], entry["measuredCommit"]))
        self.assertEqual("complete", entry["durableIdentity"]["status"])
        self.assertEqual(sorted([gate.HEADLINE, gate.STAMP_INDEX]), self.f.changed())

    def test_included_unclassified_pair_blocks_the_headline(self) -> None:
        self.f.manifest["pairs"][3]["included"] = True
        self.f.publish_results("include an unclassified pair")
        self.assertIn("Cat/D: included with disposition UNCLASSIFIED", self.assertRefused("B2-05"))

    def test_equivalent_pair_left_out_of_the_population_is_refused(self) -> None:
        self.f.manifest["pairs"][1]["included"] = False
        self.f.results["metric"]["overall"]["pairs"] = 1
        self.f.results["includedPerCategory"] = {"Cat": 1}
        self.f.publish_results("drop an equivalent pair")
        self.assertIn("Cat/B: EQUIVALENT benchmarks pair left out", self.assertRefused("B2-05"))

    def test_registration_commit_not_on_main_is_refused(self) -> None:
        off_main = self.f.git("commit-tree", "HEAD^{tree}", "-m", "same registration, never merged")
        self.f.manifest["registrationCommit"] = off_main
        self.f.publish_results("point at an unmerged registration")
        self.assertIn(f"registration commit {off_main} is not an ancestor", self.assertRefused("B2-02"))

    def test_counts_that_do_not_follow_from_the_manifest_are_refused(self) -> None:
        self.f.results["byDisposition"] = {"EQUIVALENT": 5}
        self.f.publish_results("inflate counts")
        self.assertRefused("B2-05")

    def test_changed_pair_content_blocks_the_headline(self) -> None:
        self.f.write("tests/TestData/Benchmarks/Cat/C.cs", "edited after registration\n")
        self.f.commit_and_push("edit a registered pair")
        self.assertIn("Cat/C: tests/TestData/Benchmarks/Cat/C.cs", self.assertRefused("B2-03"))

    def test_changed_pair_manifest_row_is_refused(self) -> None:
        self.f.manifest["pairs"][0]["taskStatement"] = "category: Cat\nname: other\n"
        self.f.publish_results("rewrite a task statement")
        self.assertIn("Cat/A: registered fields changed: taskStatement", self.assertRefused("B2-04"))

    def test_registration_changed_after_its_merge_is_refused(self) -> None:
        self.f.write_json(f"{gate.REGISTRATION}/registration.json", {"comparability": self.f.key, "edited": True})
        self.f.seal(gate.REGISTRATION, lf=True)
        self.f.commit_and_push("edit the registration")
        self.assertRefused("B2-02")

    def test_reduced_run_count_is_a_method_change(self) -> None:
        self.f.results["comparability"]["runCount"] = 1
        self.f.publish_results("one run")
        self.assertIn("runCount", self.assertRefused("B2-06"))

    def test_changed_metric_set_is_a_method_change(self) -> None:
        self.f.results["comparability"]["metricSetSha256"] = "3" * 64
        self.f.publish_results("other metrics")
        self.assertIn("metricSetSha256", self.assertRefused("B2-06"))

    def test_changed_aggregation_without_an_amendment_is_refused(self) -> None:
        self.f.results["comparability"]["aggregationMethod"] = "agg-v2"
        self.f.publish_results("new aggregation")
        self.assertIn("never a workflow input", self.assertRefused("B2-06"))

    def _new_key_hash(self) -> str:
        self.f.results["comparability"]["aggregationMethod"] = "agg-v2"
        self.f.results["comparability"]["pairManifestSha256"] = self.f.file_sha(f"{gate.RESULTS}/pair-manifest.json")
        return gate.key_sha256(self.f.results["comparability"])

    def _amend(self, authorization: dict | None, prose: str = "", commit: bool = True) -> None:
        entry = {"version": "1.2.0", "change": prose}
        if authorization is not None:
            entry[gate.AUTHORIZATION] = authorization
        self.f.contract["amendmentLog"].append(entry)
        self.f.write_contract()
        if commit:
            self.f.commit_and_push("amendment")

    def test_amendment_for_another_key_does_not_authorize(self) -> None:
        self._new_key_hash()
        self._amend({"comparabilityKeySha256": "4" * 64})
        self.f.publish_results("new aggregation, unrelated amendment")
        self.assertRefused("B2-06")

    def test_a_hash_mentioned_in_prose_does_not_authorize(self) -> None:
        new_hash = self._new_key_hash()
        self._amend(None, prose=f"Rejected: do NOT authorize comparabilityKeySha256 {new_hash}.")
        self.f.publish_results("new aggregation, prose only")
        self.assertRefused("B2-06")

    def test_an_amendment_in_the_same_commit_as_the_results_is_not_prior(self) -> None:
        new_hash = self._new_key_hash()
        self._amend({"comparabilityKeySha256": new_hash}, commit=False)
        self.f.publish_results("amendment and results together")
        self.assertIn("merged before these results", self.assertRefused("B2-06"))

    def test_amendment_and_results_merged_together_from_one_branch_are_not_prior(self) -> None:
        new_hash = self._new_key_hash()
        self.f.git("checkout", "-q", "-b", "feature")
        self._amend({"comparabilityKeySha256": new_hash}, commit=False)
        self.f.commit_and_push("amendment on the branch", push=False)
        self.f.publish_results("results on the branch", push=False)
        self.f.git("checkout", "-q", "main")
        self.f.git("merge", "-q", "--no-ff", "-m", "merge feature", "feature")
        self.f.commit_and_push("noop", push=True, allow_empty=True)
        self.assertRefused("B2-06")

    def test_an_amendment_withdrawn_after_the_results_no_longer_authorizes(self) -> None:
        new_hash = self._new_key_hash()
        self._amend({"comparabilityKeySha256": new_hash})
        self.f.publish_results("new aggregation")
        self.assertEqual(0, self.f.check()[0])
        self.f.git("checkout", "-q", "--", ".")
        self.f.git("clean", "-fdq")
        self.f.contract["amendmentLog"][-1]["withdrawn"] = True
        self.f.write_contract()
        self.f.commit_and_push("withdraw later")
        self.assertRefused("B2-06")

    def test_supersession_must_be_an_exact_list_member(self) -> None:
        published = self._publish_headline()
        new_hash = self._new_key_hash()
        self._amend({"comparabilityKeySha256": new_hash,
                     "supersedesComparabilityKeySha256": f"DO NOT supersede {published['comparabilityKeySha256']}"})
        self.f.publish_results("string, not list")
        self.assertRefused("B2-07")

    def test_an_older_checkout_with_different_inputs_is_refused(self) -> None:
        self.f.write("tests/TestData/Benchmarks/Cat/new.txt", "added on main\n")
        self.f.commit_and_push("main moves on")
        self.f.git("checkout", "-q", "--detach", "HEAD~1")
        self.assertIn("differs from refs/remotes/origin/main", self.assertRefused("B2-08"))

    def test_a_withdrawn_amendment_does_not_authorize(self) -> None:
        new_hash = self._new_key_hash()
        self._amend({"comparabilityKeySha256": new_hash})
        self.f.contract["amendmentLog"][-1]["withdrawn"] = True
        self.f.write_contract()
        self.f.commit_and_push("withdraw")
        self.f.publish_results("new aggregation, withdrawn amendment")
        self.assertRefused("B2-06")

    def test_a_reregistration_needs_a_prior_amendment(self) -> None:
        self.f.write_json(f"{gate.REGISTRATION}/registration.json", {"comparability": self.f.key, "v": 2})
        self.f.seal(gate.REGISTRATION, lf=True)
        self.f.manifest["registrationCommit"] = self.f.commit_and_push("re-register")
        self.f.publish_results("results for the new registration")
        self.assertIn("is not the accepted B1 registration", self.assertRefused("B2-02"))

    def _publish_headline(self) -> dict:
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.f.commit_and_push("publish headline")
        return self.f.headline()

    def _amend_method(self, published_hash: str | None) -> str:
        new_hash = self._new_key_hash()
        self._amend({"comparabilityKeySha256": new_hash,
                     "supersedesComparabilityKeySha256": [published_hash] if published_hash else []})
        self.f.publish_results("amended aggregation")
        return new_hash

    def test_method_change_by_prior_amendment_is_published_as_incomparable(self) -> None:
        published = self._publish_headline()
        self._amend_method(published["comparabilityKeySha256"])
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertIn("INCOMPARABLE with the published headline", text)
        self.assertIn("no delta, ratio change, or advantage movement", text)
        self.assertFalse(self.f.headline()["comparableWithPublished"])
        self.assertNotRegex(text, r"(?i)\bdelta\b[^,]*[-+]?\d+\.\d+")

    def test_amended_key_that_does_not_name_the_published_key_is_refused(self) -> None:
        self._publish_headline()
        self._amend_method(None)
        self.assertIn("no prior #1407 amendment authorizes this key superseding", self.assertRefused("B2-07"))

    def test_same_key_different_numbers_is_refused(self) -> None:
        published = self._publish_headline()
        published["headline"]["overall"]["geometricMeanR"] = "9.9"
        self.f.write_json(gate.HEADLINE, published)
        self.f.commit_and_push("tamper with the published headline")
        self.assertRefused("B2-07")

    def test_published_headline_without_a_key_is_refused(self) -> None:
        self.f.write_json(gate.HEADLINE, {"headline": METRIC})
        self.f.commit_and_push("keyless headline")
        self.assertIn("carries no valid comparability key", self.assertRefused("B2-07"))

    def test_same_key_regeneration_is_comparable(self) -> None:
        self._publish_headline()
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertTrue(self.f.headline()["comparableWithPublished"])

    def test_a_candidate_whose_headline_already_reached_main_rewrites_the_same_bytes(self) -> None:
        """#1422 PR 2 (replaces 'an older checkout is compared with the headline on main'): the comparison
        is with the headline published at the candidate, so a re-run after this candidate's own headline
        merged writes exactly the bytes main already has, not a 'comparable with main' variant."""
        self._publish_headline()
        on_main = {p: (self.f.root / p).read_bytes() for p in (gate.HEADLINE, gate.STAMP_INDEX)}
        self.f.git("checkout", "-q", "--detach", "HEAD~1")
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertIsNone(self.f.headline()["comparableWithPublished"], "compared with HEAD, not origin/main")
        self.assertEqual(on_main, {p: (self.f.root / p).read_bytes() for p in on_main})

    # ---- #1422 PR 2: freshness is scoped to headline inputs; the bytes depend on the candidate only ----

    def _candidate_then_main_moves(self, changes: dict) -> str:
        """Fix the candidate at HEAD, then land `changes` on main and check the candidate out again."""
        candidate = self.f.git("rev-parse", "HEAD")
        for path, text in changes.items():
            self.f.write(path, text)
        self.f.commit_and_push("main moves on after the candidate")
        self.f.git("checkout", "-q", "--detach", candidate)
        return candidate

    def _outputs(self) -> dict:
        return {p: (self.f.root / p).read_bytes() for p in (gate.HEADLINE, gate.STAMP_INDEX)}

    def _clean(self) -> None:
        self.f.git("checkout", "-q", "--", ".")
        self.f.git("clean", "-fdq")

    def test_unrelated_test_files_on_main_after_the_candidate_do_not_block(self) -> None:
        """The C2 (#1424) case: C1 added candidate tests next to B1's validator after the candidate."""
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        at_candidate = self._outputs()
        self._clean()
        self._candidate_then_main_moves({f"{gate.VALIDATOR}/CandidateInvalidationTests.cs": "new C1 test\n",
                                         f"{gate.VALIDATOR}/CandidateManifestTests.cs": "edited C1 test\n",
                                         "src/b.txt": "compiler change\n", "docs/other.md": "notes\n"})
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertEqual(at_candidate, self._outputs(), "main's unrelated changes must not change the bytes")

    def test_every_headline_input_changed_on_main_is_refused(self) -> None:
        # (file changed on main, the input the refusal names)
        cases = [(f"{gate.REGISTRATION}/registration.json", gate.REGISTRATION),
                 (f"{gate.RESULTS}/results.json", gate.RESULTS), (gate.CONTRACT, gate.CONTRACT),
                 ("tests/TestData/Benchmarks/Cat/new.txt", "tests/TestData/Benchmarks"),
                 ("tests/TestData/Benchmarks/Cat/A.calr", "tests/TestData/Benchmarks/Cat/A.calr"),
                 ("tests/Calor.Evaluation/a.txt", "tests/Calor.Evaluation"),
                 (gate.CONTRACT_SEAL, gate.CONTRACT_SEAL),  # review round 1, finding 1
                 (Fixture.CONTRACT_DOC, Fixture.CONTRACT_DOC),  # named only by the candidate's seal
                 *[(p, p) for p in gate.INPUT_PATHS if p.endswith((".py", ".cs", ".csproj"))]]
        self.assertEqual(16, len(cases))
        base = self.f.git("rev-parse", "HEAD")
        for path, named in cases:
            with self.subTest(input=path):
                # Each case starts from the same candidate, with main reset to it.
                self.f.git("checkout", "-q", "-f", "main")
                self.f.git("reset", "-q", "--hard", base)
                self.f.git("push", "-q", "-f", "origin", "HEAD:refs/heads/main")
                self._candidate_then_main_moves({path: f"changed on main after the candidate: {path}\n"})
                text = self.assertRefused("B2-08")
                self.assertIn(f"  B2-08 {named} at HEAD differs from", text)
                self.assertEqual({"B2-08"}, set(re.findall(r"(?m)^  (B2-\d\d) ", text)),
                                 "the candidate itself is sound; only freshness refuses")

    def test_a_registered_pair_file_outside_the_corpus_directory_is_an_input(self) -> None:
        self.f.write("samples/Z.calr", "z calor\n")
        row = dict(self.f.registered[0], calorPath="samples/Z.calr", calorSha256=self.f.file_sha("samples/Z.calr"))
        self.f.write_json(f"{gate.REGISTRATION}/pairs.json", {"pairs": [row] + self.f.registered[1:]})
        self.f.commit_and_push("a registration naming a file elsewhere")
        self.assertIn("samples/Z.calr", gate.input_paths(self.f.root))
        self.f.write_json(f"{gate.REGISTRATION}/pairs.json", {"pairs": self.f.registered})
        self.f.commit_and_push("restore")
        self.assertNotIn("samples/Z.calr", gate.input_paths(self.f.root))

    def test_a_generator_change_on_main_is_refused(self) -> None:
        self._candidate_then_main_moves({"tests/Calor.Evaluation/Equivalence/PairResultsCommand.cs": "changed\n"})
        self.assertIn("tests/Calor.Evaluation at HEAD differs", self.assertRefused("B2-08"))

    def test_a_different_headline_published_on_main_after_the_candidate_is_refused(self) -> None:
        self._candidate_then_main_moves({gate.HEADLINE: json.dumps({"headline": "newer"}) + "\n"})
        self.assertIn("changed since the candidate and is not this run's headline", self.assertRefused("B2-07"))

    def test_a_changed_headline_stamp_entry_on_main_is_refused(self) -> None:
        index = {"publicationStamps": [{"path": gate.HEADLINE, "stampPointer": "/provenance/commit",
                                        "measuredCommit": "0" * 40}]}
        self._candidate_then_main_moves({gate.STAMP_INDEX: json.dumps(index) + "\n"})
        self.assertIn("entries of", self.assertRefused("B2-07"))

    def test_an_input_replaced_by_a_symlink_with_the_same_bytes_is_refused(self) -> None:
        """Review round 2: the blob id is unchanged, only the tree-entry mode (100644 -> 120000) differs."""
        candidate = self.f.git("rev-parse", "HEAD")
        seal = self.f.root / gate.CONTRACT_SEAL
        content = seal.read_text(encoding="utf-8")
        blob_id = self.f.git("rev-parse", f"HEAD:{gate.CONTRACT_SEAL}")
        seal.unlink()
        os.symlink(content, seal)
        self.f.commit_and_push("same bytes, now a symlink")
        self.assertEqual(blob_id, self.f.git("rev-parse", f"origin/main:{gate.CONTRACT_SEAL}"))
        self.f.git("checkout", "-q", "--detach", candidate)
        self.assertIn(f"  B2-08 {gate.CONTRACT_SEAL} at HEAD differs", self.assertRefused("B2-08"))

    def test_a_duplicate_headline_stamp_entry_on_main_is_refused(self) -> None:
        """Review round 1, finding 2: a second headline entry appended on main is a change."""
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        ours = json.loads((self.f.root / gate.STAMP_INDEX).read_text())["publicationStamps"][-1]
        self._clean()
        other = dict(ours, measuredCommit="0" * 40)
        self._candidate_then_main_moves({gate.STAMP_INDEX: json.dumps({"publicationStamps": [ours, other]}) + "\n"})
        self.assertIn("entries of", self.assertRefused("B2-07"))

    def test_an_unreadable_stamp_index_on_main_is_refused(self) -> None:
        for text in ("not json\n", "[]\n", '{"publicationStamps": {}}\n'):
            with self.subTest(index=text):
                self.f.git("checkout", "-q", "-f", "main")
                self.f.git("reset", "-q", "--hard", "origin/main")
                self._candidate_then_main_moves({gate.STAMP_INDEX: text})
                self.assertIn("unreadable", self.assertRefused("B2-07"))
                self.f.git("checkout", "-q", "-f", "main")
                self.f.git("reset", "-q", "--hard", "HEAD~1")
                self.f.git("push", "-q", "-f", "origin", "HEAD:refs/heads/main")
                self.f.git("fetch", "-q", "origin", "+refs/heads/main:refs/remotes/origin/main")

    def test_other_stamp_index_entries_on_main_do_not_block_or_change_the_bytes(self) -> None:
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        at_candidate = self._outputs()
        self._clean()
        index = {"publicationStamps": [{"path": "other.json", "stampPointer": "/commit", "measuredCommit": "1" * 40}]}
        self._candidate_then_main_moves({gate.STAMP_INDEX: json.dumps(index) + "\n"})
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertEqual(at_candidate, self._outputs())

    def test_two_runs_write_identical_bytes(self) -> None:
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        first = self._outputs()
        code, text = self.f.check()  # second run in the same work tree, over the first run's outputs
        self.assertEqual(0, code, text)
        self.assertEqual(first, self._outputs())
        self._clean()
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertEqual(first, self._outputs())

    def test_the_bytes_depend_on_the_committed_stamp_index_not_the_work_tree(self) -> None:
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        first = self._outputs()
        self.f.write_json(gate.STAMP_INDEX, {"publicationStamps": [{"path": "x", "stampPointer": "/y"}]})
        code, text = self.f.check()
        self.assertEqual(0, code, text)
        self.assertEqual(first, self._outputs())

    def test_short_sha_provenance_is_refused(self) -> None:
        code, text = self.f.check(self.f.git("rev-parse", "--short", "HEAD"))
        self.assertEqual(1, code)
        self.assertIn("is not a full lowercase 40-hex SHA", text)
        self.assertIsNone(self.f.headline())

    def test_commit_not_on_main_is_refused(self) -> None:
        self.f.write("src/b.txt", "branch only\n")
        self.f.commit_and_push("unpushed", push=False)
        self.f.copy_regenerated()
        self.assertIn("not an ancestor", self.assertRefused("B2-08"))

    def test_shallow_clone_is_refused(self) -> None:
        shallow = self.tmp / "shallow"
        run(self.tmp, "git", "clone", "-q", "--depth", "1", f"file://{self.f.origin}", str(shallow))
        self.f.root = shallow
        self.assertIn("shallow", self.assertRefused("B2-08"))

    def test_any_other_changed_file_is_refused(self) -> None:
        self.f.write("website/public/data/benchmark-results.json", '{"overallAdvantage": 1.5}\n')
        self.assertIn("website/public/data/benchmark-results.json differs", self.f.check()[1])
        self.assertEqual(1, self.f.check()[0])

    def test_regeneration_that_differs_is_refused(self) -> None:
        (self.f.regen / "results.json").write_text("{}\n")
        (self.f.regen / "metrics-run-2.json").unlink()
        text = self.assertRefused("B2-10")
        self.assertIn("metrics-run-2.json is missing", text)
        self.assertIn("regenerated results.json differs", text)

    def test_seal_mismatch_is_refused(self) -> None:
        self.f.write_json(f"{gate.RESULTS}/environment.json", {"unsealed": True})
        sealed = json.loads((self.f.root / gate.RESULTS / "sha256.json").read_text())
        sealed["files"][f"{gate.RESULTS}/environment.json"] = "5" * 64
        self.f.write_json(f"{gate.RESULTS}/sha256.json", sealed)
        self.f.commit_and_push("bad seal")
        self.assertRefused("B2-01")

    def test_an_unsealed_oracle_run_is_refused(self) -> None:
        sealed = json.loads((self.f.root / gate.RESULTS / "sha256.json").read_text())
        del sealed["files"][f"{gate.RESULTS}/oracle-run-1.json"]
        self.f.write_json(f"{gate.RESULTS}/sha256.json", sealed)
        self.f.commit_and_push("unseal an oracle run")
        self.assertIn("oracle-run-1.json is not sealed", self.assertRefused("B2-01"))

    def test_an_oracle_run_that_the_manifest_does_not_name_is_refused(self) -> None:
        self.f.write_json(f"{gate.RESULTS}/oracle-run-2.json", {"valid": True, "edited": True})
        self.f.seal(gate.RESULTS, lf=False)
        self.f.commit_and_push("swap the reconciled oracle run")
        self.assertIn("oracleResultSha256 does not match", self.assertRefused("B2-01"))

    def test_label_that_does_not_deny_correctness_is_refused(self) -> None:
        self.f.results["label"] = "Calor is better."
        self.f.publish_results("overclaim")
        self.assertRefused("B2-11")

    def test_the_gate_has_no_override_option(self) -> None:
        help_text = run(REPO_ROOT, sys.executable, "scripts/benchmark_publication_gate.py", "check", "--help").stdout
        self.assertEqual({"--commit", "--regenerated", "--repo", "--report", "--help"},
                         set(re.findall(r"(--[a-z-]+)", help_text)))


class WorkflowTests(unittest.TestCase):
    """The workflow's own step scripts, executed, plus its structure."""

    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp(prefix="b2-wf-"))
        self.f = Fixture(self.tmp)
        self.output = self.tmp / "github-output"
        self.output.write_text("")
        (self.tmp / "runner").mkdir()
        shutil.copytree(self.f.regen, self.tmp / "runner" / "b1-regen")
        self.env = dict(os.environ, RUNNER_TEMP=str(self.tmp / "runner"), GITHUB_OUTPUT=str(self.output),
                        GITHUB_SERVER_URL="https://github.com", GITHUB_REPOSITORY="o/r", GITHUB_RUN_ID="1")

    def tearDown(self) -> None:
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_step(self, name: str) -> subprocess.CompletedProcess:
        script = self.tmp / "step.sh"
        script.write_text(step_script(name))
        # GitHub's default run shell is `bash -e {0}`.
        return run(self.f.root, "bash", "-e", str(script), env=self.env)

    def test_refusal_fails_the_gate_step_and_sets_no_report(self) -> None:
        self.f.manifest["pairs"][3]["included"] = True
        self.f.publish_results("include an unclassified pair")
        shutil.rmtree(self.tmp / "runner" / "b1-regen")
        shutil.copytree(self.f.regen, self.tmp / "runner" / "b1-regen")
        result = self.run_step(GATE_STEP)
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("REFUSED", result.stderr)
        self.assertEqual("", self.output.read_text(), "a refused gate must not hand a report to the PR step")
        self.assertIsNone(self.f.headline())

    def test_diagnostic_outputs_are_discarded_and_the_gate_passes(self) -> None:
        self.f.write("website/public/data/benchmark-results.json", '{"overallAdvantage": 1.5}\n')
        self.f.write("website/public/data/llm-results.json", "{}\n")
        self.assertEqual(0, self.run_step(RESTORE_STEP).returncode)
        result = self.run_step(GATE_STEP)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("report<<BENCHMARK_GATE_EOF\n0.24 B2 (#1422) benchmark publication gate\nOK:", self.output.read_text())
        self.assertEqual(sorted([gate.HEADLINE, gate.STAMP_INDEX]), self.f.changed())

    def test_without_the_restore_step_a_diagnostic_file_is_refused(self) -> None:
        self.f.write("website/public/data/benchmark-results.json", '{"overallAdvantage": 1.5}\n')
        self.assertNotEqual(0, self.run_step(GATE_STEP).returncode)

    def test_the_masking_pattern_r0_found_would_have_passed(self) -> None:
        """Negative control: the #1157 step piped the checker through tee without pipefail."""
        self.assertEqual(0, run(self.tmp, "bash", "-e", "-c", "false 2>&1 | tee /dev/null").returncode)
        script = step_script(GATE_STEP)
        self.assertTrue(script.startswith("set -euo pipefail\n"))
        self.assertNotIn("| tee", script)
        self.assertNotIn("||", script)

    def _run_cleanup(self, gh_script: str) -> tuple:
        bin_dir = self.tmp / "bin"
        bin_dir.mkdir(exist_ok=True)
        log = self.tmp / "gh.log"
        (bin_dir / "gh").write_text(f"#!/usr/bin/env bash\necho \"$*\" >> '{log}'\n{gh_script}\n")
        (bin_dir / "gh").chmod(0o755)
        self.env["PATH"] = f"{bin_dir}{os.pathsep}{self.env['PATH']}"
        result = self.run_step("Close stale publication PRs after a refusal (#1422)")
        return result, log.read_text() if log.exists() else ""

    def test_cleanup_closes_every_open_publication_pr(self) -> None:
        result, log = self._run_cleanup('[ "$1" = "api" ] && printf "12\\n34\\n"; exit 0')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(2, log.count("pr close"))
        self.assertIn("pr close 12 ", log)
        self.assertIn("pr close 34 ", log)
        self.assertIn("api --paginate repos/o/r/pulls?state=open", log)
        cleanup = "Close stale publication PRs after a refusal (#1422)"
        self.assertEqual(["if: failure()"], re.findall(r"(?m)^        (if: .+)$", steps()[cleanup]))
        benchmark_steps = WORKFLOW.read_text().split("\n  regression-check:", 1)[0].split("\n      - name: ")
        self.assertTrue(benchmark_steps[-1].startswith(cleanup), "cleanup must be the benchmark job's last step")

    def test_cleanup_fails_when_the_pr_listing_fails(self) -> None:
        result, log = self._run_cleanup('[ "$1" = "api" ] && exit 1; exit 0')
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("pr close", log)

    def test_cleanup_attempts_every_close_and_then_fails(self) -> None:
        result, log = self._run_cleanup('[ "$1" = "api" ] && printf "12\\n34\\n"; [ "$3" = "12" ] && exit 1; exit 0')
        self.assertNotEqual(0, result.returncode)
        self.assertIn("pr close 34 ", log)

    def test_no_dispatch_input_is_interpolated_into_a_shell_script(self) -> None:
        for name in steps():
            if "        run: |\n" in steps()[name]:
                with self.subTest(step=name):
                    self.assertNotRegex(step_script(name), r"\$\{\{\s*(github\.event\.)?inputs\.")
        self.assertIn("concurrency:\n      group: benchmark-publication\n      cancel-in-progress: false",
                      WORKFLOW.read_text())
        # The workflow runs B1's C# validators, so they are gate inputs that must be fresh too.
        self.assertIn("BenchmarkResultsTests", step_script("Regenerate the B1 results with the registered generator (#1422)"))
        # #1422 PR 2: the validator files are inputs one by one, not the whole directory, and every
        # test class the workflow's filter runs is one of them.
        self.assertNotIn(gate.VALIDATOR, gate.INPUT_PATHS)
        regenerate = step_script("Regenerate the B1 results with the registered generator (#1422)")
        for test_class in re.findall(r"FullyQualifiedName~(\w+)", regenerate):
            with self.subTest(validator=test_class):
                self.assertIn(f"{gate.VALIDATOR}/{test_class}.cs", gate.INPUT_PATHS)
        push_paths = WORKFLOW.read_text().split("  push:\n", 1)[1].split("\njobs:", 1)[0]
        self.assertNotIn(f"'{gate.VALIDATOR}/**'", push_paths)
        patterns = re.findall(r"(?m)^      - '([^']+)'$", push_paths)
        for path in gate.INPUT_PATHS:
            with self.subTest(gate_input=path):
                # GitHub glob: '**' crosses directories, '*' does not.
                self.assertTrue(any(re.fullmatch(re.escape(p).replace(r"\*\*", ".*").replace(r"\*", "[^/]*"), probe)
                                    for p in patterns for probe in (path, f"{path}/x")), path)

    def test_publication_steps_run_only_after_the_gate_succeeds(self) -> None:
        names = list(steps())
        gate_index = names.index(GATE_STEP)
        self.assertNotIn("continue-on-error", steps()[GATE_STEP])
        self.assertIn("        id: methodology\n", steps()[GATE_STEP])
        benchmark_job = WORKFLOW.read_text().split("\n  benchmark:\n", 1)[1].split("\n  regression-check:", 1)[0]
        self.assertNotIn("continue-on-error", benchmark_job.split("- name: Setup Node.js")[1])
        publishing = [n for n, block in steps().items()
                      if re.search(r"create-pull-request|gh pr create|gh pr merge|git push|git commit", block)]
        self.assertTrue(publishing)
        for name in publishing:
            with self.subTest(step=name):
                self.assertGreater(names.index(name), gate_index)
                condition = " ".join(re.findall(r"(?m)^        if: (.+)$", steps()[name]))
                self.assertNotRegex(condition, r"always\(\)|failure\(\)|cancelled\(\)")

    def test_no_push_to_main_and_no_bypass_input(self) -> None:
        text = WORKFLOW.read_text()
        for line in text.splitlines():
            if "git push" in line:
                self.assertIn("HEAD:refs/heads/", line)
                self.assertNotIn("refs/heads/main", line)
        inputs = re.findall(r"(?m)^      ([a-z_]+):\n        description:", text)
        self.assertLessEqual(set(inputs), {"statistical_runs", "skip_llm", "agent_refactoring", "adjudication_identity"})
        for path in (WORKFLOW, REPO_ROOT / "scripts/check-benchmark-methodology.js"):
            self.assertNotIn("allow_weaker_methodology", path.read_text())
            self.assertNotIn("includes('--allow-weaker')", path.read_text())

    def test_agent_refactoring_job_fails_instead_of_recording_zero(self) -> None:
        job = WORKFLOW.read_text().split("\n  agent-refactoring-benchmark:\n", 1)[1]
        self.assertIn("contents: read", job)
        self.assertNotIn("contents: write", job)
        self.assertNotRegex(job, r'\|\| echo "0"|\|\| 0 \}\}|continue-on-error|git push|git commit')
        for name in ("Run Calor refactoring benchmark", "Run C# refactoring benchmark", "Generate agent benchmark results JSON"):
            self.assertTrue(step_script(name).startswith("set -euo pipefail\n"), name)
        # Executed: a colored pass-rate line is read; output without one fails instead of becoming 0.
        extract = [l for l in step_script("Run Calor refactoring benchmark").splitlines() if "CALOR_PASS_RATE" in l][:2]
        script = ("set -euo pipefail\n" + "\n".join(extract).replace("/tmp/calor-results.txt", str(self.tmp / "out.txt"))
                  + '\necho "rate=$CALOR_PASS_RATE"\n')
        (self.tmp / "out.txt").write_text("Total: 20\nPass Rate: \x1b[1m95%\x1b[0m\n")
        passed = run(self.tmp, "bash", "-c", script)
        self.assertEqual((0, "rate=95"), (passed.returncode, passed.stdout.strip()), passed.stderr)
        (self.tmp / "out.txt").write_text("Agent Task Test Summary\nTotal: 0\n")
        failed = run(self.tmp, "bash", "-c", script)
        self.assertNotEqual(0, failed.returncode)
        self.assertNotIn("rate=", failed.stdout)

    def test_historical_numbers_on_the_website_are_labeled(self) -> None:
        components = REPO_ROOT / "website/src/components"
        label = (components / "benchmarks/HistoricalMethodLabel.tsx").read_text()
        self.assertIn("Historical, not comparable under the 0.24 method", label)
        for relative in ("benchmarks/BenchmarkDashboard.tsx", "benchmarks/BenchmarkSummaryTable.tsx",
                         "benchmarks/AgentRefactoringCard.tsx", "benchmarks/AgentBenchmarkDashboard.tsx",
                         "landing/BenchmarkChart.tsx"):
            with self.subTest(component=relative):
                self.assertIn("<HistoricalMethodLabel", (components / relative).read_text())


class RealPacketTests(unittest.TestCase):
    """The committed B1 packet passes every content check, and the headline is its projection."""

    def test_committed_b1_packet_is_publishable(self) -> None:
        root, findings = REPO_ROOT, []
        gate.check_seals(root, findings)
        manifest = gate.load(root, f"{gate.RESULTS}/pair-manifest.json")
        results = gate.load(root, f"{gate.RESULTS}/results.json")
        registered = gate.load(root, f"{gate.REGISTRATION}/pairs.json")["pairs"]
        prior = gate.prior_authorizations(root)
        gate.check_registration_unchanged(root, manifest, prior, findings)
        included = gate.check_pairs(root, registered, manifest, results, findings)
        key, key_hash, amendments = gate.check_method(root, results, prior, findings)
        gate.check_headline_shape(results, findings)
        # Regeneration needs the .NET build; the workflow runs it, and docs/plans/evidence/b2-1422
        # records a local byte-identical run.
        self.assertEqual([], findings)
        self.assertEqual(17, len(included))
        self.assertEqual({"EQUIVALENT": 17, "EXCLUDED-PRE-REGISTERED": 9, "NOT-EQUIVALENT": 192, "UNCLASSIFIED": 8},
                         results["byDisposition"])
        self.assertEqual([], amendments, "the B1 key is the registered one; no amendment is needed or cited")

    def test_every_headline_input_exists(self) -> None:
        """A renamed input would be absent at both the candidate and main and so never differ; it must
        be renamed in INPUT_PATHS too. Every registered pair file is an input."""
        paths = gate.input_paths(REPO_ROOT)
        for path in paths:
            with self.subTest(input=path):
                self.assertTrue((REPO_ROOT / path).exists(), path)
        sealed = {p for seal in gate.SEALS for p in gate.load(REPO_ROOT, seal)["files"]}
        self.assertTrue({"docs/plans/v0.24-evidence-contract.md", "docs/plans/b1-1276-benchmark-registration.md",
                         "docs/plans/evidence/evidence-contract-1407/artifact-inventory.json"} <= set(paths))
        self.assertEqual(len(set(gate.INPUT_PATHS) | sealed) + 2 * 226, len(paths), "226 registered pairs, two files each")
        # Every input outside the corpus directory re-runs the workflow on push.
        push = WORKFLOW.read_text().split("  push:\n", 1)[1].split("\njobs:", 1)[0]
        patterns = re.findall(r"(?m)^      - '([^']+)'$", push)
        for path in [p for p in paths if p not in gate.INPUT_PATHS]:
            with self.subTest(push_path=path):
                self.assertTrue(any(re.fullmatch(re.escape(g).replace(r"\*\*", ".*").replace(r"\*", "[^/]*"), path)
                                    for g in patterns), path)

    def test_a_committed_headline_is_the_projection_of_the_committed_packet(self) -> None:
        """However a headline reaches main, it must be what the gate writes from the packet."""
        path = REPO_ROOT / gate.HEADLINE
        if not path.exists():
            return  # nothing is published yet; the workflow's first passing run writes it
        published = json.loads(path.read_text(encoding="utf-8"))
        results = gate.load(REPO_ROOT, f"{gate.RESULTS}/results.json")
        self.assertEqual(results["metric"], published["headline"])
        self.assertEqual(gate.key_sha256(results["comparability"]), published["comparabilityKeySha256"])
        self.assertEqual(results["label"], published["label"])
        self.assertRegex(published["provenance"]["commit"], r"^[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main(verbosity=1)
