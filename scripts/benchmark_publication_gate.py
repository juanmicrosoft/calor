#!/usr/bin/env python3
"""0.24 B2 (#1422): refuse to publish an incomparable or non-equivalent benchmark headline.

The only publishable benchmark headline is a projection of the B1 (#1276) results packet, which
the registered results generator ``b1-1276-results-generator-v1`` wrote. This gate re-checks that
packet against its registration and the working tree, then writes the headline candidate and its
durable provenance identity (contract section 6). It refuses (exit 1, nothing written) when:

  B2-01  a sealed registration, results, or contract file differs from its seal;
  B2-02  the registration changed after its merge commit, or that commit is not on main;
  B2-03  a registered pair's Calor or C# file differs from its registered SHA-256;
  B2-04  the pair manifest does not carry every registered pair and field unchanged;
  B2-05  an included pair is not EQUIVALENT, or the counts do not follow from the manifest;
  B2-06  the comparability key is neither the registered one nor authorized by a #1407
         amendment merged before the results (a method change: metric set, run count,
         aggregation, generator, ...);
  B2-07  the headline published at the candidate has another key and no prior amendment
         supersedes it, or it has the same key and different numbers; or origin/main's published
         headline (or its stamp-index entry) changed since the candidate to anything other than
         exactly the bytes this run writes;
  B2-08  the provenance commit is not a full SHA, not HEAD, not on fetched origin/main, or the
         clone is shallow; or a headline input (INPUT_PATHS, every file the three seals name,
         and every registered pair file) differs between the candidate and origin/main;
  B2-09  the working tree has changes other than the gate's own outputs;
  B2-10  the regenerated packet (pair-metrics twice, then pair-results) is missing or differs;
  B2-11  the results packet does not carry the registered population, sampling unit, and label.

There is no override option. A method change is authorized only by a contract.json amendment-log
entry with a structured ``benchmarkMethodAuthorization`` {comparabilityKeySha256,
supersedesComparabilityKeySha256: [...], registrationCommit?}, present in the contract on main
before the first-parent commit that landed results.json there, and still present and not withdrawn.
Mentions of a hash in prose authorize nothing.

Freshness (#1422 PR 2). The checked-out HEAD is the candidate (the workflow checks out the
adjudicated candidate). It may publish while origin/main has moved on, but only if main changed
none of the headline's inputs since the candidate: the registered B1 packet and results packet,
the contract that authorizes methods, the three seals and every file they name (B2-01), every
registered pair file and the benchmark corpus, the
registered generator (tests/Calor.Evaluation), B1's C# validator files, and this gate. Other files
(for example unrelated tests next to the validator) do not count. The written bytes are a function
of the candidate alone: the comparison is with the headline published at the candidate, and the
stamp index is the candidate's plus this entry. Nothing depends on the clock or on main's later
state, so an adjudicated hash taken at the candidate matches a later publish-time run. Exit 2 is a
usage error.

  python3 scripts/benchmark_publication_gate.py check --commit <40-hex> --regenerated <dir>
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

REGISTRATION = "docs/plans/evidence/b1-1276/registration"
RESULTS = "docs/plans/evidence/b1-1276/results"
CONTRACT = "docs/plans/evidence/evidence-contract-1407/contract.json"
CONTRACT_SEAL = "docs/plans/evidence/evidence-contract-1407/sha256.json"
HEADLINE = "website/public/data/benchmark-headline.json"
STAMP_INDEX = "bench/phase0-agent-native/commit-stamp-index.json"
MAIN_REF = "refs/remotes/origin/main"
GENERATOR = "b1-1276-results-generator-v1"
REGENERATED_FILES = ("metrics-run-1.json", "metrics-run-2.json", "pair-manifest.json", "results.json")
DISPOSITIONS = {"EQUIVALENT", "NOT-EQUIVALENT", "UNCLASSIFIED", "EXCLUDED-PRE-REGISTERED"}
KEY_FIELDS = ("pairManifestSha256", "metricSetSha256", "metricImplementationVersion", "aggregationMethod",
              "samplingUnit", "runCount", "generatorVersion", "exclusionsSha256")
PAIR_FIELDS = ("pairId", "source", "calorPath", "calorSha256", "csharpPath", "csharpSha256", "taskStatement",
               "taskStatementSha256", "inputSet", "expectedOutputs", "failureBehavior")
FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
AUTHORIZATION = "benchmarkMethodAuthorization"
VALIDATOR = "tests/Calor.Compiler.Tests/EvidenceContract"
# The inputs that determine or validate the headline (#1422 PR 2). Each must be the same at the
# candidate (HEAD) and on origin/main; every registered pair path is added at run time. The B1
# validator is the workflow's BenchmarkResultsTests|BenchmarkRegistrationTests filter: those two
# classes, the partial validator they call, the helpers they use (EvidenceContractTests.RepoRoot,
# Contract), and the project file that compiles them. Other files in that directory (C1's candidate
# tests, for example) are not benchmark inputs. src/ is not an input: the metric is computed by
# tests/Calor.Evaluation alone, B2-10 re-runs the generator at the candidate, and the headline
# records the candidate's src tree hash.
SEALS = (f"{REGISTRATION}/sha256.json", f"{RESULTS}/sha256.json", CONTRACT_SEAL)
INPUT_PATHS = (REGISTRATION, RESULTS, CONTRACT, CONTRACT_SEAL, "tests/TestData/Benchmarks", "tests/Calor.Evaluation",
               f"{VALIDATOR}/BenchmarkRegistrationTests.cs", f"{VALIDATOR}/BenchmarkRegistrationValidator.cs",
               f"{VALIDATOR}/BenchmarkResultsTests.cs", f"{VALIDATOR}/BenchmarkResultsValidator.cs",
               f"{VALIDATOR}/EvidenceContractValidator.cs", f"{VALIDATOR}/EvidenceContractTests.cs",
               "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj",
               "scripts/benchmark_publication_gate.py")
STAMP_KEY = (HEADLINE, "/provenance/commit")
# The B1 registration merge commit (PR #1473, contract section 5). Any other registration needs a
# prior amendment whose benchmarkMethodAuthorization names its registrationCommit.
ACCEPTED_REGISTRATION_COMMITS = ("f0e0eb682a8658364170ad49aff2d2c433d570f8",)

LIMITATIONS = [
    "Corpus only: the corpus is fixed and author-built. It is not a sample of real-world code, tasks, "
    "or developers, and no figure here generalizes beyond these files.",
    "EQUIVALENT means the two arms showed no observable difference on the registered finite inputs "
    "under the registered oracle. It is not a proof of equivalence and not a claim that either arm "
    "is correct with respect to its task statement.",
    "{included} pairs are included of the {sent} pairs sent to the oracle ({denominator} registered, "
    "{excluded} excluded before registration). The figure describes those {included} pairs, not the corpus.",
    "r is a static source-size ratio of two committed files. It is not a coding-agent outcome and "
    "not a measured language, productivity, correctness, or safety advantage.",
    "The interval is a corpus-resampling interval for this fixed corpus, not a confidence interval "
    "for any population.",
]


class Refusal(Exception):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def lf_sha256(data: bytes) -> str:
    return sha256(data.replace(b"\r\n", b"\n"))


def key_sha256(key: dict) -> str:
    canonical = json.dumps({f: key.get(f) for f in KEY_FIELDS}, sort_keys=True, separators=(",", ":"),
                           ensure_ascii=False)
    return sha256(canonical.encode("utf-8"))


def load(root: Path, relative: str):
    path = root / relative
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise Refusal(f"cannot read {relative}: {error}") from error


def git(root: Path, *args: str, check: bool = False) -> str | None:
    run = subprocess.run(["git", *args], cwd=root, capture_output=True, text=True)
    if run.returncode != 0:
        if check:
            raise Refusal(f"git {' '.join(args)} failed: {run.stderr.strip()}")
        return None
    return run.stdout.strip()


def blob(root: Path, spec: str) -> bytes | None:
    """The exact bytes of a committed file (None when absent)."""
    run = subprocess.run(["git", "cat-file", "blob", spec], cwd=root, capture_output=True)
    return run.stdout if run.returncode == 0 else None


def check_seals(root: Path, findings: list) -> None:
    """B2-01: every file each seal lists has the sealed bytes (results raw; the others LF-normalized)."""
    for seal_path, normalize in zip(SEALS, (lf_sha256, sha256, lf_sha256)):
        seal = load(root, seal_path)
        files = seal.get("files") or {}
        if not files:
            findings.append(("B2-01", f"{seal_path} lists no files"))
        for relative, expected in sorted(files.items()):
            path = root / relative
            actual = normalize(path.read_bytes()) if path.is_file() else "missing"
            if actual != expected:
                findings.append(("B2-01", f"{relative} is {actual}, sealed as {expected} in {seal_path}"))
    sealed = set(load(root, f"{RESULTS}/sha256.json").get("files") or {})
    present = {f"{RESULTS}/{p.name}" for p in (root / RESULTS).iterdir() if p.name != "sha256.json"}
    for name in ("results.json", "pair-manifest.json", "metrics-run-1.json", "metrics-run-2.json",
                 "oracle-run-1.json", "oracle-run-2.json"):
        present.add(f"{RESULTS}/{name}")
    for relative in sorted(present - sealed):
        findings.append(("B2-01", f"{relative} is not sealed; every results file, including the oracle runs, must be"))
    manifest = load(root, f"{RESULTS}/pair-manifest.json")
    for field, name in (("firstOracleResultSha256", "oracle-run-1.json"), ("oracleResultSha256", "oracle-run-2.json")):
        path = root / RESULTS / name
        if not path.is_file() or manifest.get(field) != sha256(path.read_bytes()):
            findings.append(("B2-01", f"pair manifest {field} does not match {RESULTS}/{name}"))
    if CONTRACT not in (load(root, CONTRACT_SEAL).get("files") or {}):
        findings.append(("B2-01", f"{CONTRACT} is not sealed"))


def check_registration_unchanged(root: Path, manifest: dict, prior: list, findings: list) -> None:
    """B2-02: the registration is an accepted one, merged on main, and its tree is unchanged since."""
    commit = manifest.get("registrationCommit") or ""
    if not FULL_SHA.match(commit):
        findings.append(("B2-02", f"pair manifest registrationCommit '{commit}' is not a full SHA"))
        return
    accepted = set(ACCEPTED_REGISTRATION_COMMITS) | {a.get("registrationCommit") for a in prior}
    if commit not in accepted:
        findings.append(("B2-02", f"registration commit {commit} is not the accepted B1 registration and no "
                                  "prior amendment authorizes it"))
    if git(root, "merge-base", "--is-ancestor", commit, MAIN_REF) is None:
        findings.append(("B2-02", f"registration commit {commit} is not an ancestor of {MAIN_REF}"))
    then, now = git(root, "rev-parse", f"{commit}:{REGISTRATION}"), git(root, "rev-parse", f"HEAD:{REGISTRATION}")
    if then is None or then != now:
        findings.append(("B2-02", f"{REGISTRATION} at HEAD ({now}) is not the tree merged at {commit} ({then}); "
                                  "a changed registration is a #1407 amendment and a new run"))


def check_pairs(root: Path, registered: list, manifest: dict, results: dict, findings: list) -> list:
    """B2-03/B2-04/B2-05. Returns the included pair rows."""
    for pair in registered:
        for side in ("calor", "csharp"):
            path = root / pair.get(f"{side}Path", "")
            actual = sha256(path.read_bytes()) if path.is_file() else "missing"
            if actual != pair.get(f"{side}Sha256"):
                findings.append(("B2-03", f"{pair.get('pairId')}: {pair.get(f'{side}Path')} is {actual}, "
                                          f"registered as {pair.get(f'{side}Sha256')}"))
    rows = manifest.get("pairs") or []
    if [r.get("pairId") for r in rows] != [p.get("pairId") for p in registered]:
        findings.append(("B2-04", "the pair manifest does not list every registered pair in registered order"))
    for row, pair in zip(rows, registered):
        changed = [f for f in PAIR_FIELDS if row.get(f) != pair.get(f)]
        if changed:
            findings.append(("B2-04", f"{pair.get('pairId')}: registered fields changed: {', '.join(changed)}"))
    if manifest.get("pairCount") != len(rows) or manifest.get("generatorVersion") != GENERATOR:
        findings.append(("B2-04", "the pair manifest's pairCount or generatorVersion is wrong"))
    registration_sha = lf_sha256((root / REGISTRATION / "pairs.json").read_bytes())
    for doc, name in ((manifest, "pair manifest"), (results, "results.json")):
        if doc.get("registrationPairManifestSha256") != registration_sha:
            findings.append(("B2-04", f"{name} names registration pairs {doc.get('registrationPairManifestSha256')}, "
                                      f"not {registration_sha}"))
    manifest_sha = lf_sha256((root / RESULTS / "pair-manifest.json").read_bytes())
    if (results.get("comparability") or {}).get("pairManifestSha256") != manifest_sha:
        findings.append(("B2-04", f"results.json comparability names pair manifest "
                                  f"{(results.get('comparability') or {}).get('pairManifestSha256')}, not {manifest_sha}"))

    included = []
    for row in rows:
        disposition = row.get("disposition")
        if disposition not in DISPOSITIONS:
            findings.append(("B2-05", f"{row.get('pairId')}: disposition '{disposition}' is not in the vocabulary"))
        if row.get("included") is True:
            included.append(row)
            if disposition != "EQUIVALENT":
                findings.append(("B2-05", f"{row.get('pairId')}: included with disposition {disposition}; only "
                                          "EQUIVALENT pairs enter a headline"))
        elif row.get("included") is not False:
            findings.append(("B2-05", f"{row.get('pairId')}: 'included' is not a boolean"))
        elif disposition == "EQUIVALENT" and row.get("source") == "manifest.benchmarks":
            findings.append(("B2-05", f"{row.get('pairId')}: EQUIVALENT benchmarks pair left out of the population"))
    counts: dict = {}
    for row in rows:
        counts[row.get("disposition")] = counts.get(row.get("disposition"), 0) + 1
    if results.get("byDisposition") != counts or results.get("denominator") != len(rows):
        findings.append(("B2-05", f"results.json denominator/byDisposition {results.get('denominator')}/"
                                  f"{results.get('byDisposition')} do not follow from the manifest {len(rows)}/{counts}"))
    overall = ((results.get("metric") or {}).get("overall") or {})
    if overall.get("pairs") != len(included) or sum((results.get("includedPerCategory") or {}).values()) != len(included):
        findings.append(("B2-05", f"the metric covers {overall.get('pairs')} pairs; the manifest includes {len(included)}"))
    return included


def _authorizations(text: str | None) -> list:
    try:
        contract = json.loads(text) if text else {}
    except ValueError:
        return []
    return [entry[AUTHORIZATION] for entry in contract.get("amendmentLog") or []
            if isinstance(entry, dict) and isinstance(entry.get(AUTHORIZATION), dict) and entry.get("version")
            and not entry.get("withdrawn")]


def results_landing(root: Path) -> str | None:
    """The first-parent commit of origin/main that landed the current results.json bytes."""
    path = f"{RESULTS}/results.json"
    current = git(root, "rev-parse", f"HEAD:{path}")
    for commit in (git(root, "log", "--first-parent", "--format=%H", MAIN_REF, "--", path) or "").split():
        if git(root, "rev-parse", f"{commit}:{path}") == current and git(root, "rev-parse", f"{commit}^1:{path}") != current:
            return commit
    return None


def prior_authorizations(root: Path) -> list:
    """Authorizations merged to main before the results landed there, and not withdrawn since."""
    landing = results_landing(root)
    before = _authorizations(git(root, "show", f"{landing}^1:{CONTRACT}")) if landing else []
    now = _authorizations((root / CONTRACT).read_text(encoding="utf-8"))
    return [a for a in before if a in now]


def check_method(root: Path, results: dict, prior: list, findings: list) -> tuple:
    """B2-06: the key is the registered one, or a prior amendment authorizes it. Returns (key, hash, authorizations)."""
    key = results.get("comparability") or {}
    missing = [f for f in KEY_FIELDS if f not in key]
    if missing:
        findings.append(("B2-06", f"comparability key lacks {', '.join(missing)}"))
    key_hash = key_sha256(key)
    registered = load(root, f"{REGISTRATION}/registration.json").get("comparability") or {}
    differs = [f for f in KEY_FIELDS if f != "pairManifestSha256" and key.get(f) != registered.get(f)]
    if key.get("generatorVersion") != results.get("generatorVersion"):
        differs.append("generatorVersion (results.json vs its comparability key)")
    amendments = [a for a in prior if a.get("comparabilityKeySha256") == key_hash]
    if differs and not amendments:
        findings.append(("B2-06", f"the method differs from the registration in {', '.join(differs)}, and no "
                                  f"#1407 amendment merged before these results authorizes comparabilityKeySha256 "
                                  f"{key_hash}; a method change needs that amendment first, never a workflow input"))
    return key, key_hash, amendments


def check_headline_shape(results: dict, findings: list) -> None:
    """B2-11: the packet states the registered population, sampling unit, and limits."""
    metric = results.get("metric") or {}
    expected = {"samplingUnit": "program-pair",
                "population": "registered manifest.benchmarks pairs dispositioned EQUIVALENT by the reconciled oracle result"}
    for field, value in expected.items():
        if metric.get(field) != value:
            findings.append(("B2-11", f"metric.{field} is '{metric.get(field)}', registered '{value}'"))
    label = results.get("label") or ""
    if "not a correctness claim" not in label or "not a coding-agent outcome" not in label:
        findings.append(("B2-11", "results.json label does not deny correctness and agent-outcome readings"))
    if "not a confidence interval" not in (metric.get("intervalLabel") or ""):
        findings.append(("B2-11", "metric.intervalLabel does not say the interval is corpus resampling only"))


def check_regenerated(root: Path, regenerated: Path, findings: list) -> None:
    """B2-10: the registered generator, re-run here, reproduces the committed packet byte for byte."""
    for name in REGENERATED_FILES:
        fresh = regenerated / name
        if not fresh.is_file():
            findings.append(("B2-10", f"regenerated {name} is missing from {regenerated}"))
        elif fresh.read_bytes() != (root / RESULTS / name).read_bytes():
            findings.append(("B2-10", f"regenerated {name} differs from the committed {RESULTS}/{name}"))


def check_provenance(root: Path, commit: str, findings: list) -> None:
    """B2-08: contract section 6 identity: full SHA, HEAD, on fetched main, full clone."""
    if not FULL_SHA.match(commit or ""):
        findings.append(("B2-08", f"provenance commit '{commit}' is not a full lowercase 40-hex SHA"))
        return
    if git(root, "rev-parse", "--is-shallow-repository") != "false":
        findings.append(("B2-08", "the clone is shallow or not a repository; provenance fails rather than skips"))
        return
    if git(root, "rev-parse", "--verify", "--quiet", MAIN_REF) is None:
        findings.append(("B2-08", f"{MAIN_REF} is not fetched"))
        return
    if git(root, "rev-parse", "HEAD") != commit:
        findings.append(("B2-08", f"provenance commit {commit} is not the checked-out HEAD"))
    if git(root, "merge-base", "--is-ancestor", commit, MAIN_REF) is None:
        findings.append(("B2-08", f"{commit} is not an ancestor of {MAIN_REF}"))
    check_inputs_fresh(root, findings)


def input_paths(root: Path) -> list:
    """INPUT_PATHS, every file the candidate's three seals name (B2-01 checks them: the B1 registration
    document, the contract document, the artifact inventory, ...), and every registered pair file."""
    paths = list(INPUT_PATHS)
    for seal in SEALS:
        try:
            files = json.loads(git(root, "show", f"HEAD:{seal}") or "{}").get("files") or {}
        except (ValueError, AttributeError):
            files = {}
        for path in sorted(files) if isinstance(files, dict) else []:
            if path not in paths:
                paths.append(path)
    try:
        pairs = json.loads(git(root, "show", f"HEAD:{REGISTRATION}/pairs.json") or "{}").get("pairs") or []
    except (ValueError, AttributeError):
        pairs = []
    for pair in pairs if isinstance(pairs, list) else []:
        for side in ("calorPath", "csharpPath"):
            if isinstance(pair, dict) and isinstance(pair.get(side), str) and pair[side] not in paths:
                paths.append(pair[side])
    for path in partial_validator_files(root):
        if path not in paths:
            paths.append(path)
    return paths


# Verification pass: a new file declaring another part of a validator class can change which
# overload an unchanged caller binds to (C# picks the better overload across all parts), so every
# file declaring a part of these classes, at HEAD or on main, is an input. One added only on main
# is absent at HEAD and therefore differs.
VALIDATOR_PARTIALS = r"partial[[:space:]]+(class|struct|record)[[:space:]]+(EvidenceContractValidator|EvidenceContractTests|BenchmarkResultsTests|BenchmarkRegistrationTests)([^A-Za-z0-9_]|$)"
VALIDATOR_PROJECT = "tests/Calor.Compiler.Tests"


def partial_validator_files(root: Path) -> list:
    """Every .cs file under the validator's project, at HEAD or on main, declaring a part of a validator class."""
    found = set()
    for rev in ("HEAD", MAIN_REF):
        out = git(root, "grep", "-l", "-E", VALIDATOR_PARTIALS, rev, "--", f"{VALIDATOR_PROJECT}/*.cs")
        for line in (out or "").splitlines():
            found.add(line.split(":", 1)[1] if line.startswith(f"{rev}:") else line)
    return sorted(found)


def check_inputs_fresh(root: Path, findings: list) -> None:
    """B2-08 freshness: main changed no headline input since the candidate (HEAD).

    The whole tree entry (mode, type, object id) is compared, so a file or directory counts as
    changed if any byte, mode, or entry differs (a regular file replaced by a symlink with the same
    bytes included), and an input missing on one side differs from one present on the other.
    RealPacketTests requires every input to exist, so none is absent on both sides by a rename."""
    def entry(rev: str, path: str) -> str | None:
        return git(root, "ls-tree", "--full-tree", rev, "--", path)

    for path in input_paths(root):
        if entry("HEAD", path) != entry(MAIN_REF, path):
            findings.append(("B2-08", f"{path} at HEAD differs from {MAIN_REF}; main changed a headline input "
                                      "since the candidate, so the candidate's headline would be stale"))


def check_worktree(root: Path, outputs: tuple, findings: list) -> None:
    """B2-09: nothing but the gate's own outputs may differ from HEAD (no stray published file)."""
    status = subprocess.run(["git", "status", "--porcelain", "-z", "--untracked-files=all"], cwd=root,
                            capture_output=True, text=True)
    if status.returncode != 0:
        findings.append(("B2-09", "git status failed"))
        return
    entries = status.stdout.split("\0")
    paths = []
    while entries:
        entry = entries.pop(0)
        if len(entry) > 3:
            paths.append(entry[3:])
            if entry[0] in "RC" and entries:
                paths.append(entries.pop(0))  # rename/copy source
    for path in paths:
        if path not in outputs:
            findings.append(("B2-09", f"{path} differs from HEAD; only the B1-derived headline may be published"))


def check_published(root: Path, key: dict, key_hash: str, amendments: list, metric: dict, findings: list) -> dict:
    """B2-07: compare with the headline published at the candidate (HEAD), so the written bytes do
    not depend on main's later state; check_main_publication fences main. No delta across keys."""
    text = git(root, "show", f"HEAD:{HEADLINE}")
    if text is None:
        return {"comparableWithPublished": None,
                "comparison": "no headline is published yet; nothing is compared"}
    try:
        published = json.loads(text)
    except ValueError:
        findings.append(("B2-07", f"the published {HEADLINE} is not JSON"))
        return {}
    published_key = published.get("comparability") or {}
    published_hash = key_sha256(published_key)
    if [f for f in KEY_FIELDS if f not in published_key] or published.get("comparabilityKeySha256") != published_hash:
        findings.append(("B2-07", f"the published {HEADLINE} carries no valid comparability key"))
        return {}
    if published_hash == key_hash:
        if published.get("headline") != metric:
            findings.append(("B2-07", "same comparability key as the published headline but different numbers; "
                                      "the registered generator is deterministic, so something else changed"))
        return {"comparableWithPublished": True,
                "comparison": f"comparable with the published headline (key {key_hash}); values unchanged"}
    if not any(isinstance(a.get("supersedesComparabilityKeySha256"), list)
               and published_hash in a["supersedesComparabilityKeySha256"] for a in amendments):
        findings.append(("B2-07", f"the published headline has key {published_hash} and this candidate has "
                                  f"{key_hash}; no prior #1407 amendment authorizes this key superseding that one"))
        return {}
    return {"comparableWithPublished": False,
            "comparison": f"INCOMPARABLE with the published headline (key {published_hash}); the method changed "
                          f"by amendment, so no delta, ratio change, or advantage movement is reported"}


def identity(root: Path, commit: str) -> dict:
    trees = {"/": git(root, "rev-parse", f"{commit}^{{tree}}", check=True)}
    for path in ("src", "tests/Calor.Evaluation", "tests/TestData/Benchmarks", "docs/plans/evidence/b1-1276",
                 "scripts/benchmark_publication_gate.py"):
        trees[path] = git(root, "rev-parse", f"{commit}:{path}", check=True)
    contents = {}
    for path in (f"{RESULTS}/results.json", f"{RESULTS}/pair-manifest.json", f"{REGISTRATION}/pairs.json", CONTRACT):
        blob = subprocess.run(["git", "cat-file", "blob", f"{commit}:{path}"], cwd=root, capture_output=True, check=True)
        contents[path] = sha256(blob.stdout)
    return {"status": "complete", "commit": commit, "resolvedVia": MAIN_REF, "treeHashes": trees,
            "inputContentHashes": contents}


def build_candidate(root: Path, commit: str, key: dict, key_hash: str, results: dict, manifest: dict,
                    included: list, comparison: dict) -> dict:
    counts = results["byDisposition"]
    excluded = counts.get("EXCLUDED-PRE-REGISTERED", 0)
    numbers = {"included": len(included), "sent": results["denominator"] - excluded,
               "denominator": results["denominator"], "excluded": excluded}
    durable = identity(root, commit)
    return {
        "schemaVersion": 1,
        "kind": "benchmark-headline",
        "gate": "0.24 B2 (#1422)",
        "generatorVersion": results["generatorVersion"],
        "source": {"results": f"{RESULTS}/results.json", "pairManifest": f"{RESULTS}/pair-manifest.json",
                   "registrationCommit": manifest["registrationCommit"]},
        "comparability": {f: key[f] for f in KEY_FIELDS},
        "comparabilityKeySha256": key_hash,
        **comparison,
        "denominator": {"registered": numbers["denominator"], "excludedPreRegistered": excluded,
                        "sentToOracle": numbers["sent"], "included": numbers["included"],
                        "byDisposition": counts},
        "headline": results["metric"],
        "label": results["label"],
        "limitations": [text.format(**numbers) for text in LIMITATIONS],
        "provenance": {"commit": commit, "resolvedVia": MAIN_REF, "treeHashes": durable["treeHashes"],
                       "inputContentHashes": durable["inputContentHashes"]},
    }


def index_entry(root: Path, commit: str) -> dict:
    return {
        "path": HEADLINE,
        "stampPointer": "/provenance/commit",
        "measuredCommit": commit,
        "basis": "identical-src-tree",
        "measuredTreeHashes": {"src": git(root, "rev-parse", f"{commit}:src", check=True)},
        "note": "#1422: written by scripts/benchmark_publication_gate.py from the B1 results packet at this "
                "commit, which is on main; the headline is a projection of results.json, not a new measurement.",
        "durableIdentity": identity(root, commit),
    }


def render(doc: dict) -> bytes:
    return (json.dumps(doc, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def stamp_entries(index_text: str | None) -> object:
    """Every headline entry in a stamp index, in order ([] when the index is absent). An index that is
    not a JSON object with a publicationStamps list is the marker "<unreadable>", which equals no
    readable index, so it can never pass as unchanged or as this run's entry."""
    if index_text is None:
        return []
    try:
        stamps = json.loads(index_text).get("publicationStamps")
    except (ValueError, AttributeError):
        return "<unreadable>"
    if not isinstance(stamps, list):
        return "<unreadable>"
    return [s for s in stamps if not isinstance(s, dict) or (s.get("path"), s.get("stampPointer")) == STAMP_KEY]


def render_index(root: Path, entry: dict) -> bytes:
    """The candidate's committed stamp index (never the work tree) with this entry replacing the headline's."""
    index = json.loads(git(root, "show", f"HEAD:{STAMP_INDEX}", check=True))
    stamps = [s for s in index["publicationStamps"] if (s.get("path"), s.get("stampPointer")) != STAMP_KEY]
    index["publicationStamps"] = stamps + [entry]
    return render(index)


def check_main_publication(root: Path, outputs: dict, findings: list) -> None:
    """B2-07: main's published headline and its stamp entry are unchanged since the candidate, or are
    exactly what this run writes (this candidate's headline already merged). Anything else means a
    different headline reached main after the candidate, and publishing this one would be stale."""
    head, main = blob(root, f"HEAD:{HEADLINE}"), blob(root, f"{MAIN_REF}:{HEADLINE}")
    if main != head and main != outputs[HEADLINE]:
        findings.append(("B2-07", f"{HEADLINE} on {MAIN_REF} changed since the candidate and is not this run's "
                                  "headline; a newer publication reached main, so this candidate is stale"))
    head_entries = stamp_entries(git(root, "show", f"HEAD:{STAMP_INDEX}"))
    main_entries = stamp_entries(git(root, "show", f"{MAIN_REF}:{STAMP_INDEX}"))
    ours_entries = stamp_entries(outputs[STAMP_INDEX].decode("utf-8"))
    if main_entries != head_entries and main_entries != ours_entries:
        findings.append(("B2-07", f"the {HEADLINE} entries of {STAMP_INDEX} on {MAIN_REF} changed since the "
                                  "candidate (added, removed, duplicated, or unreadable) and are not this run's entry"))


def report(findings: list, candidate: dict | None) -> str:
    lines = ["0.24 B2 (#1422) benchmark publication gate"]
    if findings:
        lines.append(f"REFUSED: {len(findings)} problem(s). No headline is written and nothing may be published.")
        lines += [f"  {code} {message}" for code, message in findings]
        return "\n".join(lines) + "\n"
    overall = candidate["headline"]["overall"]
    d = candidate["denominator"]
    lines += [
        f"OK: comparabilityKeySha256 {candidate['comparabilityKeySha256']}",
        f"provenance: {candidate['provenance']['commit']} ({MAIN_REF})",
        f"denominator: {d['registered']} registered, {d['excludedPreRegistered']} excluded before registration, "
        f"{d['sentToOracle']} sent to the oracle, {d['included']} included (EQUIVALENT); by disposition {d['byDisposition']}",
        f"{candidate['headline']['metric']}: geometric mean r {overall['geometricMeanR']} over {overall['pairs']} pairs, "
        f"corpus-resampling interval {overall['interval95']}; {candidate['headline'].get('perPairValue')}",
        candidate["comparison"],
        "limitations:",
    ] + [f"  - {text}" for text in candidate["limitations"]]
    return "\n".join(lines) + "\n"


def run_check(root: Path, commit: str, regenerated: Path) -> tuple:
    findings: list = []
    check_worktree(root, (HEADLINE, STAMP_INDEX), findings)
    check_provenance(root, commit, findings)
    check_seals(root, findings)
    manifest = load(root, f"{RESULTS}/pair-manifest.json")
    results = load(root, f"{RESULTS}/results.json")
    prior = prior_authorizations(root)
    registered = load(root, f"{REGISTRATION}/pairs.json").get("pairs") or []
    check_registration_unchanged(root, manifest, prior, findings)
    included = check_pairs(root, registered, manifest, results, findings)
    key, key_hash, amendments = check_method(root, results, prior, findings)
    check_headline_shape(results, findings)
    check_regenerated(root, regenerated, findings)
    comparison = check_published(root, key, key_hash, amendments, results.get("metric"), findings)
    if findings:
        return findings, None, None
    candidate = build_candidate(root, commit, key, key_hash, results, manifest, included, comparison)
    outputs = {HEADLINE: render(candidate), STAMP_INDEX: render_index(root, index_entry(root, commit))}
    check_main_publication(root, outputs, findings)
    if findings:
        return findings, None, None
    return findings, candidate, outputs


def main(argv: list | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    check = sub.add_parser("check", help="refuse or write the headline candidate")
    check.add_argument("--commit", required=True, help="full SHA of the checked-out commit (provenance)")
    check.add_argument("--regenerated", required=True, type=Path,
                       help="directory holding pair-metrics x2 and pair-results output from this run")
    check.add_argument("--repo", type=Path, default=Path.cwd())
    check.add_argument("--report", type=Path, help="also write the report here")
    args = parser.parse_args(argv)

    root = args.repo.resolve()
    try:
        findings, candidate, outputs = run_check(root, args.commit, args.regenerated)
    except (Refusal, OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        findings, candidate, outputs = [("B2-00", f"the packet could not be checked: {error}")], None, None
    text = report(findings, candidate)
    if candidate is not None:
        for relative, data in outputs.items():
            (root / relative).write_bytes(data)
    if args.report:
        args.report.write_text(text, encoding="utf-8")
    (sys.stderr if findings else sys.stdout).write(text)
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
