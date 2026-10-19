#!/usr/bin/env python3
"""Fail-closed release gate (#1410), for every release in the adjudication map (#1567).

Every public release surface (NuGet packages, the GitHub release and its notes,
release metadata, the website, benchmark publication, and the installed-tool
check) runs this script before it acts. It accepts exactly one adjudication
identity:

    calor-adjudication:v1:<adjudication commit, 40 hex>:<record SHA-256, 64 hex>

The identity names a terminal record committed in a commit on protected main, and
the SHA-256 of that blob's exact bytes. Which A1 issue, record path, contract packet,
and claim registry apply to a release comes from the committed, reviewed map
eng/release-adjudication-map.json (MAP_FILE), read from fetched protected main, never
from the checkout being published (#1567). The script exits 0 only when the record is the
one named and sits at the path the map gives its version, the map marks that release
adjudicated, the record is a successful adjudication under the release's frozen
contract (0.24.0: #1408 under #1407), the checkout being published is
the adjudicated candidate, and every surface artifact passed on the command
line matches the hash the record adjudicated. Anything missing, malformed,
changed, or unverifiable is a failure; there is no warning-only mode and no
override flag.

The release PR (#1546) is not a separate surface: it prepares the candidate, and every run checks
its products (the candidate's version and CHANGELOG section) against the record (G009, G014).
Benchmark publication (#1547) also needs the record's explicit approval (G015).

Design and record schema: docs/plans/evidence/r2-1410/release-gate.md.
Negative controls: tests/Calor.Compiler.Tests/ReleaseGate/.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

# #1567: the per-release A1 issue, record path, contract packet, and claim registry live in this
# map. It is read from fetched protected main (MAIN_REF), so the checkout being published, which
# may be a candidate frozen before its A1 record and map change landed, cannot change it.
MAP_FILE = "eng/release-adjudication-map.json"
MAP_SCHEMA = "calor.release-adjudication-map/1"
ADJUDICATED, NOT_YET_ADJUDICATED = "adjudicated", "not-yet-adjudicated"
# The contract-packet formats this gate checks: #1407's contract.json, artifact-inventory.json, and
# sha256.json seal (0.24.0); and #1426's frozen R0 scope packet with the T1 register and W1 records
# (0.25.x, maintainer decision D-GATE-SCOPE-PACKET, scope amendment 1.4.0). Another format fails (G016)
# until a reviewed change teaches the gate to read it.
PACKET_FORMAT_1407 = "calor.evidence-contract-packet/1407"
PACKET_FORMAT_1426 = "calor.v0.25-scope-packet/1426"
SCOPE_SCHEMA_1426 = "v0.25-interop-scope/1"
T1_REGISTER_1426 = "docs/plans/evidence/v0.25-t1-1428/register.json"
W1_RECORDS_1426 = "docs/plans/evidence/v0.25-w1-1429/records.json"
T1_SCHEMA_1426 = "calor.v0.25.t1-register/1"
W1_SCHEMA_1426 = "calor.v0.25.w1-family-records/1"
PRODUCERS_1426 = ("reproduce/reproduce.py", "reproduce/ProbeRunner.cs.txt")
# The re-proof checks scope amendment 1.3.0 registered (a floor; InteropScopeValidator.RequiredReproofChecks),
# and the SHA-256 of that amendment's acceptancePreconditions as JsonNode.ToJsonString() writes it.
REPROOF_CHECKS_1426 = (
    ("proof-s2-witnesses", "proof", 1413, "docs/plans/evidence/s2-1413/dispositions.json"),
    ("provenance-candidate-manifest", "provenance", 1423, "docs/plans/v0.24-c1-candidate.md"),
    ("provenance-regeneration", "provenance", 1424, "docs/plans/evidence/c2-1424/regeneration.md"),
    ("provenance-determinism", "provenance", 1421, "scripts/determinism_protocol.py"),
    ("release-gate", "release", 1410, "docs/plans/evidence/r2-1410/release-gate.md"),
)
PRECONDITIONS_DIGEST_130 = "82c07b93a4b9784a9776a41b515d02fb1e34c4017d7b352d103c4b9fa255d89b"
APPROVAL_URL_RE = re.compile(r"^https://github\.com/juanmicrosoft/calor/pull/\d+#pullrequestreview-\d+$")
RELEASE_GAPS_1426 = [1545, 1546, 1547]
OUTCOMES_1426 = ("ACCEPTED", "PARTIAL_READY")
# The only release the 0.24.0 maintainer override may name (see scripts/verify_maintainer_override.py).
OVERRIDE_RELEASE = "0.24.0"
VERSION_KEY_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+|x)$")
VERSION_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$")
REPO_PATH_RE = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9._-]*(?:/[A-Za-z0-9_][A-Za-z0-9._-]*)*$")
RECORD_SCHEMA = "calor.adjudication-terminal-record/1"
MAIN_REF = "refs/remotes/origin/main"
IDENTITY_RE = re.compile(r"^calor-adjudication:v1:([0-9a-f]{40}):([0-9a-f]{64})$")
TRAILER_RE = re.compile(r"^<!-- calor-adjudication: (\S+) -->$")
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
FILE_SURFACES = ("nuget-packages", "release-metadata", "benchmark-results")
# #1422 (B2) publishes exactly these two files: the headline written by
# scripts/benchmark_publication_gate.py and its commit-stamp-index entry. No other benchmark
# file (benchmark-results.json and the rest are diagnostic only) may be adjudicated for publication.
BENCHMARK_PUBLICATION_FILES = (
    "website/public/data/benchmark-headline.json",
    "bench/phase0-agent-native/commit-stamp-index.json",
)
MANIFEST_ROLES = ("candidate-manifest", "raw-artifact-freeze")
ROW_INDEPENDENCE = "reduced-maintainer-adjudicated"
# The contract (section 9) forbids calling 0.24 evidence independently adjudicated or verified.
# A phrase is allowed only when it is negated, as in the required limitation text itself.
FORBIDDEN_RE = re.compile(
    r"independently\s+(?:adjudicated|verified)|independent\s+(?:adjudication|verification)",
    re.IGNORECASE,
)
NEGATION_RE = re.compile(r"\b(?:not|no|without)\s+$", re.IGNORECASE)
TEXT_SUFFIXES = {".html", ".htm", ".txt", ".md", ".mdx", ".json", ".xml", ".rss"}


class GateError(Exception):
    pass


class MapError(Exception):
    """The adjudication map is missing or malformed; every identity fails (G016)."""


ENTRY_FIELDS = {"version", "status", "a1Issue", "terminalRecord", "contractPacket", "claimRegistry",
                "maintainerOverride", "note"}
PACKET_FIELDS = {"issue", "format", "dir", "contract", "inventory", "seal"}


def _repo_path(value: object, what: str) -> str:
    if not isinstance(value, str) or not REPO_PATH_RE.match(value):
        raise MapError(f"{what} must be a relative repository path, got {value!r}")
    return value


def _issue(value: object, what: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise MapError(f"{what} must be a positive issue number, got {value!r}")
    return value


def _unique_members(pairs: list[tuple[str, object]]) -> dict:
    keys = [k for k, _ in pairs]
    if len(set(keys)) != len(keys):
        raise MapError(f"duplicate JSON member(s) {sorted({k for k in keys if keys.count(k) > 1})}")
    return dict(pairs)


def load_map(gate: Gate) -> list[dict]:
    """The release entries of the map at fetched protected main, validated strictly. Raises MapError."""
    raw = gate.blob(MAIN_REF, MAP_FILE)
    if raw is None:
        raise MapError(f"no {MAP_FILE} at {MAIN_REF}")
    return parse_map(raw)


def parse_map(raw: bytes) -> list[dict]:
    try:
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_members)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise MapError(f"{MAP_FILE} is not valid JSON: {error}") from error
    if not isinstance(data, dict) or data.get("schema") != MAP_SCHEMA:
        raise MapError(f"{MAP_FILE} schema must be {MAP_SCHEMA}")
    entries = data.get("releases")
    if not isinstance(entries, list) or not entries:
        raise MapError(f"{MAP_FILE} lists no releases")
    keys: set[str] = set()
    records: set[str] = set()
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) - ENTRY_FIELDS:
            raise MapError(f"release entry has unknown fields: {sorted(set(entry) - ENTRY_FIELDS) if isinstance(entry, dict) else entry!r}")
        key = entry.get("version")
        if not isinstance(key, str) or not VERSION_KEY_RE.match(key):
            raise MapError(f"release key must be MAJOR.MINOR.PATCH or MAJOR.MINOR.x, got {key!r}")
        if key in keys:
            raise MapError(f"release {key} is listed twice")
        keys.add(key)
        if entry.get("status") not in (ADJUDICATED, NOT_YET_ADJUDICATED):
            raise MapError(f"release {key}: status must be '{ADJUDICATED}' or '{NOT_YET_ADJUDICATED}'")
        _issue(entry.get("a1Issue"), f"release {key}: a1Issue")
        record = _repo_path(entry.get("terminalRecord"), f"release {key}: terminalRecord")
        if record in records:
            raise MapError(f"release {key}: terminal record {record} is named by another release")
        records.add(record)
        _repo_path(entry.get("claimRegistry"), f"release {key}: claimRegistry")
        packet = entry.get("contractPacket")
        if not isinstance(packet, dict) or set(packet) != PACKET_FIELDS:
            raise MapError(f"release {key}: contractPacket must have exactly {sorted(PACKET_FIELDS)}")
        _issue(packet.get("issue"), f"release {key}: contractPacket.issue")
        if not isinstance(packet.get("format"), str) or not packet["format"]:
            raise MapError(f"release {key}: contractPacket.format must be named")
        for field in ("dir", "contract", "seal"):
            _repo_path(packet.get(field), f"release {key}: contractPacket.{field}")
        if packet.get("inventory") is not None:
            _repo_path(packet.get("inventory"), f"release {key}: contractPacket.inventory")
        if "maintainerOverride" in entry:
            if key != OVERRIDE_RELEASE:
                raise MapError(f"release {key}: only {OVERRIDE_RELEASE} may name a maintainer override (#1548)")
            _repo_path(entry["maintainerOverride"], f"release {key}: maintainerOverride")
    # An exact key inside a MAJOR.MINOR.x key would make a version resolve to two releases.
    wildcards = {k[:-2] for k in keys if k.endswith(".x")}
    for key in keys:
        if not key.endswith(".x") and key.rsplit(".", 1)[0] in wildcards:
            raise MapError(f"release {key} overlaps {key.rsplit('.', 1)[0]}.x")
    return entries


def map_entry(entries: list[dict], version: object) -> dict | None:
    """The one entry whose key covers this version, or None."""
    found = VERSION_RE.match(version) if isinstance(version, str) else None
    if not found:
        return None
    major, minor, patch = found.groups()
    for entry in entries:
        k_major, k_minor, k_patch = VERSION_KEY_RE.match(entry["version"]).groups()
        if (k_major, k_minor) == (major, minor) and (k_patch == "x" or (k_patch == patch and version == entry["version"])):
            return entry
    return None


class Gate:
    def __init__(self, repo: Path) -> None:
        self.repo = repo
        self.errors: list[str] = []

    def fail(self, code: str, message: str) -> None:
        self.errors.append(f"{code}: {message}")

    def git(self, *args: str, check: bool = True) -> subprocess.CompletedProcess:
        result = subprocess.run(
            ["git", "-C", str(self.repo), *args], capture_output=True
        )
        if check and result.returncode != 0:
            raise GateError(
                f"git {' '.join(args)} failed: {result.stderr.decode(errors='replace').strip()}"
            )
        return result

    def blob(self, commit: str, path: str) -> bytes | None:
        result = self.git("cat-file", "blob", f"{commit}:{path}", check=False)
        return result.stdout if result.returncode == 0 else None

    def is_ancestor(self, older: str, newer: str) -> bool:
        return self.git("merge-base", "--is-ancestor", older, newer, check=False).returncode == 0

    def resolve(self, ref: str) -> str | None:
        result = self.git("rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}", check=False)
        return result.stdout.decode().strip() if result.returncode == 0 else None


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def text_digest(text: str) -> str:
    """Hash of release-note text: CRLF -> LF, trailing whitespace trimmed, one final newline."""
    normalized = text.replace("\r\n", "\n").rstrip() + "\n"
    return sha256(normalized.encode("utf-8"))


def tree_digest(root: Path) -> str:
    """SHA-256 over sorted '<file sha256>  <relative posix path>' lines (sha256sum format)."""
    files = {p.relative_to(root).as_posix(): p for p in root.rglob("*") if p.is_file()}
    lines = [f"{sha256(files[rel].read_bytes())}  {rel}\n" for rel in sorted(files)]
    return sha256("".join(lines).encode("utf-8"))


def version_headers(changelog: str, version: str) -> int:
    """How many `## [VERSION]` headers CHANGELOG.md holds (matched literally)."""
    return sum(1 for line in changelog.replace("\r\n", "\n").split("\n") if line.startswith(f"## [{version}]"))


def changelog_section(changelog: str, version: str) -> str:
    """The `## [VERSION]` section of CHANGELOG.md, the version matched literally. publish-nuget
    renders the notes with this function (scripts/render_release_notes.py)."""
    out, inside = [], False
    for line in changelog.replace("\r\n", "\n").split("\n"):
        if line.startswith(f"## [{version}]"):
            inside = True
        elif inside and line.startswith("## ["):
            break
        if inside:
            out.append(line)
    return "\n".join(out)


def forbidden_wording(text: str) -> tuple[list[str], int]:
    """Unnegated forbidden phrases, and the number of negated ones."""
    hits, negated = [], 0
    for match in FORBIDDEN_RE.finditer(text):
        if NEGATION_RE.search(text[max(0, match.start() - 12): match.start()]):
            negated += 1
        else:
            hits.append(match.group(0))
    return hits, negated


def json_strings(value: object) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, dict):
        return [s for k, v in value.items() for s in json_strings(k) + json_strings(v)]
    if isinstance(value, list):
        return [s for v in value for s in json_strings(v)]
    return []


def sources(label: str, text: str) -> list[str]:
    """The text as written and, for JSON, its decoded string values."""
    found = [text]
    if label.endswith(".json"):
        try:
            found.append("\n".join(json_strings(json.loads(text))))
        except ValueError:
            pass
    return found


def renderings(source: str) -> list[str]:
    """One source as written, entity-decoded, with markup replaced by a space and removed, and
    with Markdown emphasis removed from each of those."""
    decoded = html.unescape(source)
    variants = [source, decoded]
    if "<" in decoded:
        variants.append(re.sub(r"<[^>]+>", " ", decoded))
        variants.append(re.sub(r"<[^>]+>", "", decoded))
    return variants + [re.sub(r"[*_`~]", "", v) for v in variants]


def scan_text(gate: Gate, label: str, text: str) -> None:
    # A negation counts only where it is literal in the source. If removing markup produces a
    # negated phrase the source does not literally contain (for example a hidden "not "), the
    # negation came from markup and the rendered page still makes the claim.
    hits: set[str] = set()
    for source in sources(label, text):
        _, literal_negations = forbidden_wording(html.unescape(source))
        for variant in renderings(source):
            found, negated = forbidden_wording(variant)
            hits.update(found)
            if negated > literal_negations:
                hits.add("negation supplied by markup")
    for hit in sorted(hits):
        gate.fail("G012", f"{label}: forbidden wording '{hit}'; release evidence is not independently adjudicated")


def load_record(gate: Gate, identity: str) -> tuple[str, dict, dict, list[dict]] | None:
    match = IDENTITY_RE.match(identity)
    if not match:
        gate.fail("G001", "adjudication identity must be calor-adjudication:v1:<40-hex commit>:<64-hex sha256>")
        return None
    commit, digest = match.groups()

    if gate.git("rev-parse", "--is-shallow-repository").stdout.decode().strip() != "false":
        gate.fail("G002", "shallow clone; the gate needs full history and fails rather than skips")
        return None
    if gate.resolve(MAIN_REF) is None:
        gate.fail("G002", f"{MAIN_REF} is not fetched; fetch protected main before running the gate")
        return None
    if gate.resolve(commit) != commit:
        gate.fail("G003", f"adjudication commit {commit} is not present in this clone")
        return None
    if not gate.is_ancestor(commit, MAIN_REF):
        gate.fail("G003", f"adjudication commit {commit} is not on protected main")
        return None
    entries = load_map(gate)

    # #1567: the record is the blob, at one of the map's terminal-record paths, that the identity
    # hashes. Its release entry then decides everything else the gate reads.
    present = {e["terminalRecord"]: (e, gate.blob(commit, e["terminalRecord"])) for e in entries}
    present = {path: pair for path, pair in present.items() if pair[1] is not None}
    named = [pair for pair in present.values() if sha256(pair[1]) == digest]
    if not present:
        gate.fail("G004", f"no terminal record at {commit} under any path in {MAP_FILE}: "
                          f"{sorted(e['terminalRecord'] for e in entries)}")
        return None
    if not named:
        found = ", ".join(f"{path} has {sha256(pair[1])}" for path, pair in sorted(present.items()))
        gate.fail("G004", f"no terminal record at {commit} matches the identity {digest} ({found})")
        return None
    if len(named) != 1:
        gate.fail("G016", "the identity's record bytes sit at more than one mapped record path")
        return None
    entry, data = named[0]
    if entry["status"] != ADJUDICATED:
        gate.fail("G016", f"release {entry['version']} (#{entry['a1Issue']}) is {entry['status']} in "
                          f"{MAP_FILE}; no identity passes until a reviewed change marks it {ADJUDICATED}")
        return None
    packet = entry["contractPacket"]
    if not ((packet["format"] == PACKET_FORMAT_1407 and packet["inventory"] is not None)
            or (packet["format"] == PACKET_FORMAT_1426 and packet["inventory"] is None and packet["seal"] == packet["contract"])):
        gate.fail("G016", f"release {entry['version']}: the gate cannot check contract packet format "
                          f"{packet['format']!r}; only {PACKET_FORMAT_1407} and {PACKET_FORMAT_1426}")
        return None
    try:
        record = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        gate.fail("G004", f"terminal record is not valid JSON: {error}")
        return None
    if not isinstance(record, dict) or record.get("schema") != RECORD_SCHEMA:
        gate.fail("G004", f"terminal record schema must be {RECORD_SCHEMA}")
        return None
    candidate = record.get("candidate")
    version = candidate.get("version") if isinstance(candidate, dict) else None
    if isinstance(version, str) and VERSION_RE.match(version):  # otherwise G009 reports it
        resolved = map_entry(entries, version)
        if resolved is None:
            gate.fail("G009", f"candidate version {version} is not in {MAP_FILE}; no release is mapped for it")
        elif resolved is not entry:
            gate.fail("G016", f"the record at {entry['terminalRecord']} adjudicates {version}, which "
                              f"{MAP_FILE} maps to release {resolved['version']}, not {entry['version']}")
            return None
    return commit, record, entry, entries


def check_contract(gate: Gate, commit: str, record: dict, entry: dict) -> tuple[dict, dict] | None:
    packet = entry["contractPacket"]
    raw_contract = gate.blob(commit, packet["contract"])
    raw_inventory = gate.blob(commit, packet["inventory"])
    raw_hashes = gate.blob(commit, packet["seal"])
    if raw_contract is None or raw_inventory is None or raw_hashes is None:
        gate.fail("G005", f"the #{packet['issue']} contract packet is missing at the adjudication commit")
        return None
    contract = json.loads(raw_contract)
    inventory = json.loads(raw_inventory)
    files = json.loads(raw_hashes).get("files")
    if contract.get("status") != "FROZEN" or contract.get("gateStatus") != "MET":
        gate.fail("G005", f"the #{packet['issue']} contract is not FROZEN with R0 MET")
    if not isinstance(files, dict) or not files:
        gate.fail("G005", "sha256.json lists no packet files")
        return None
    for path, expected in files.items():
        blob = gate.blob(commit, path)
        actual = None if blob is None else sha256(blob.replace(b"\r\n", b"\n"))
        if actual != expected:
            gate.fail("G005", f"packet file {path} does not match {packet['seal']}")
    declared = record.get("contract")
    if not isinstance(declared, dict):
        gate.fail("G005", "record has no contract block")
    else:
        if declared.get("version") != contract.get("contractVersion"):
            gate.fail("G005", "record contract version differs from the committed contract")
        if declared.get("files") != files:
            gate.fail("G005", "record contract hashes differ from sha256.json; the contract changed after adjudication")
    return contract, inventory


def claim_registry(gate: Gate, commit: str, record: dict, entry: dict) -> set[str]:
    """The claim registry the record names: {path, sha256} of {"claims": ["claim:<id>", ...]}. The
    path must be the one the map gives the release (0.24.0: #1424's regeneration-3 registry)."""
    ref = record.get("claimRegistry")
    if not isinstance(ref, dict) or ref.get("path") != entry["claimRegistry"]:
        gate.fail("G008", f"record must name the claim registry {entry['claimRegistry']} that "
                          f"{MAP_FILE} gives release {entry['version']}")
        return set()
    blob = gate.blob(commit, str(ref.get("path")))
    if blob is None or sha256(blob) != ref.get("sha256"):
        gate.fail("G008", "record must name the committed claim registry by path and sha256")
        return set()
    claims = json.loads(blob).get("claims")
    if not isinstance(claims, list) or not all(
        isinstance(c, str) and c.startswith("claim:") and len(c) > 6 for c in claims
    ):
        gate.fail("G008", "claim registry must list 'claim:<id>' strings")
        return set()
    return set(claims)


def check_terminal(gate: Gate, commit: str, record: dict, entry: dict, contract: dict, inventory: dict) -> None:
    a1 = entry["a1Issue"]
    if record.get("issue") != a1:
        gate.fail("G006", f"record is not the #{a1} terminal record")
    if record.get("outcome") != "MILESTONE-SUCCEEDED":
        gate.fail("G006", f"terminal outcome is {record.get('outcome')!r}, not MILESTONE-SUCCEEDED")

    independence = contract.get("authorityCapacity", {}).get("independence", {})
    deviation = independence.get("deviation") is not False
    if deviation:
        if record.get("adjudicationIndependence") != "reduced":
            gate.fail("G007", "adjudicationIndependence must be 'reduced' under the independence deviation")
        limitation = independence.get("publishedLimitation")
        if not limitation or record.get("limitation") != limitation:
            gate.fail("G007", "record must carry the published limitation verbatim")
        if record.get("epicIndependentAdjudicationMet") is not False:
            gate.fail("G007", "record must state epicIndependentAdjudicationMet = false")

    outcomes = set(contract.get("adjudicationOutcomes", []))
    classes = {a["id"]: a.get("classification") for a in inventory.get("artifacts", [])}
    for artifact, cls in sorted(classes.items()):
        if cls == "stale":
            gate.fail("G008", f"{artifact}: inventory still classifies it stale; a stale artifact can only be BLOCKED")
    required = set(classes)
    required |= {f"gate:#{c['issue']}" for c in contract.get("children", []) if c.get("issue") != a1}
    required |= claim_registry(gate, commit, record, entry)
    rows = record.get("adjudications")
    if not isinstance(rows, list) or not rows:
        gate.fail("G008", "record has no adjudication rows")
        return
    seen: set[str] = set()
    for row in rows:
        subject = row.get("subject") if isinstance(row, dict) else None
        name = subject or "?"
        if not isinstance(subject, str) or subject in seen:
            gate.fail("G008", f"{name}: missing or duplicate subject")
            continue
        seen.add(subject)
        if subject not in required:
            gate.fail("G008", f"{name}: unknown or unregistered subject")
        outcome = row.get("outcome")
        if outcome not in outcomes:
            gate.fail("G008", f"{name}: unknown adjudication outcome {outcome!r}")
        elif outcome == "BLOCKED":
            gate.fail("G008", f"{name}: BLOCKED; a blocked release-critical row cannot be released")
        elif outcome == "SUPPORTED" and deviation:
            gate.fail("G008", f"{name}: SUPPORTED is unavailable under the independence deviation")
        elif outcome == "HISTORICAL-ONLY" and classes.get(subject) != "historical-only":
            gate.fail("G008", f"{name}: HISTORICAL-ONLY is valid only for a historical-only artifact")
        if deviation and row.get("independence") != ROW_INDEPENDENCE:
            gate.fail("G008", f"{name}: row must record independence = {ROW_INDEPENDENCE}")
    for missing in sorted(required - seen):
        gate.fail("G008", f"{missing}: required subject is not adjudicated")


def lf_sha256(blob: bytes | None) -> str | None:
    return None if blob is None else sha256(blob.replace(b"\r\n", b"\n"))


def json_blob(gate: Gate, commit: str, path: str) -> dict | None:
    blob = gate.blob(commit, path)
    try:
        value = json.loads(blob) if blob is not None else None
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    return value if isinstance(value, dict) else None


def dotnet_json(value: object) -> str:
    """System.Text.Json's JsonNode.ToJsonString(): compact, with every non-ASCII, control and HTML-sensitive
    character escaped as uppercase \\uXXXX (the encoding InteropScopeValidator hashes)."""
    if isinstance(value, dict):
        return "{" + ",".join(dotnet_json(k) + ":" + dotnet_json(v) for k, v in value.items()) + "}"
    if isinstance(value, list):
        return "[" + ",".join(dotnet_json(v) for v in value) + "]"
    if value is None or isinstance(value, bool):
        return {None: "null", True: "true", False: "false"}[value]
    if not isinstance(value, str):
        return json.dumps(value)
    out = []
    for ch in value:
        code = ord(ch)
        if ch == "\\":
            out.append("\\\\")
        elif ch in "\n\r\t\b\f":
            out.append({"\n": "\\n", "\r": "\\r", "\t": "\\t", "\b": "\\b", "\f": "\\f"}[ch])
        elif code < 0x20 or code > 0x7E or ch in "\"&'+<>`":
            units = [code] if code <= 0xFFFF else [0xD800 + ((code - 0x10000) >> 10), 0xDC00 + ((code - 0x10000) & 0x3FF)]
            out.extend(f"\\u{u:04X}" for u in units)
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def check_scope_packet(gate: Gate, commit: str, record: dict, entry: dict) -> tuple[dict, dict] | None:
    """#1426 (0.25.x): the frozen R0 scope packet, its evidence seal, the 1.3.0 re-proof preconditions (as S014 checks
    them), and the T1 register and W1 records that consume it, at the adjudication commit (G005)."""
    packet = entry["contractPacket"]
    scope = json_blob(gate, commit, packet["contract"])
    register = json_blob(gate, commit, T1_REGISTER_1426)
    w1 = json_blob(gate, commit, W1_RECORDS_1426)
    if scope is None or register is None or w1 is None:
        gate.fail("G005", f"the #{packet['issue']} scope packet, T1 register or W1 records are missing at the adjudication commit")
        return None
    version = scope.get("scopeVersion")
    if scope.get("schema") != SCOPE_SCHEMA_1426 or scope.get("status") != "FROZEN" or scope.get("gateStatus") != "MET":
        gate.fail("G005", f"the #{packet['issue']} scope packet is not FROZEN with R0 MET")
    seal, root = scope.get("evidenceSeal") or {}, packet["dir"].rstrip("/") + "/"
    producers = seal.get("producers") if isinstance(seal.get("producers"), dict) else {}
    generated = sorted(p for p in gate.git("ls-tree", "-r", "--name-only", commit, "--", root + "generated/").stdout.decode().splitlines() if p)
    digest = sha256("\n".join(f"{p} {lf_sha256(gate.blob(commit, p))}" for p in generated).encode())
    if (set(producers) != {root + p for p in PRODUCERS_1426} or seal.get("baselineResults") != lf_sha256(gate.blob(commit, root + "baseline-results.json"))
            or any(lf_sha256(gate.blob(commit, p)) != h for p, h in producers.items()) or not generated or seal.get("generated") != digest):
        gate.fail("G005", "the scope packet's evidenceSeal does not match its baseline results, both registered producers or generated outputs")
    for problem in preconditions_problems(gate, commit, scope):
        gate.fail("G005", f"acceptancePreconditions: {problem}")
    families = {f.get("id"): f.get("issue") for f in scope.get("families", []) if isinstance(f, dict)}
    if register.get("schema") != T1_SCHEMA_1426 or register.get("independence") != "reduced" \
            or {f.get("id"): f.get("issue") for f in register.get("families", []) if isinstance(f, dict)} != families \
            or (register.get("consumes") or {}).get("scope") != packet["contract"] or (register.get("consumes") or {}).get("scopeVersion") != version:
        gate.fail("G005", f"the T1 register must be {T1_SCHEMA_1426} with reduced independence, cover the six families, and consume scope version {version}")
    if w1.get("schema") != W1_SCHEMA_1426 or w1.get("independence") != "reduced" or w1.get("scopeVersion") != version \
            or {f.get("family"): f.get("issue") for f in w1.get("families", []) if isinstance(f, dict)} != families:
        gate.fail("G005", f"the W1 records must be {W1_SCHEMA_1426} with reduced independence, cover the six families, and consume scope version {version}")
    lines = []
    for family in register.get("families", []):
        for ev in family.get("evidence", []):
            lines.append((f"evidence {family.get('id')} {ev.get('path')} {ev.get('sha256')}", ev.get("path"), ev.get("sha256")))
    for control in register.get("controls", []):
        lines.append((f"fixture {control.get('id')} {control.get('fixture')} {control.get('fixtureSha256')}", control.get("fixture"), control.get("fixtureSha256")))
    results, counts = register.get("results") or {}, register.get("corpusCounts") or {}
    lines.append((f"results {results.get('path')} {results.get('sha256')}", results.get("path"), results.get("sha256")))
    approved = ((register.get("amendments") or [register])[-1]).get("approvalSeal")
    corpus = f"corpus {counts.get('source')} {json.dumps(counts.get('counts'), separators=(',', ':'))}"
    recomputed = sha256("\n".join(sorted([line for line, _, _ in lines] + [corpus])).encode())
    if any(not isinstance(p, str) or lf_sha256(gate.blob(commit, p)) != h for _, p, h in lines) or recomputed != approved:
        gate.fail("G005", "the T1 register's approved evidence, fixtures or results differ from its approval seal")
    files = {p: lf_sha256(gate.blob(commit, p)) for p in (packet["contract"], T1_REGISTER_1426, W1_RECORDS_1426)}
    declared = record.get("contract")
    if not isinstance(declared, dict):
        gate.fail("G005", "record has no contract block")
    elif declared.get("version") != version or declared.get("files") != files:
        gate.fail("G005", "record contract version or hashes differ from the committed scope packet, T1 register and W1 records")
    return scope, register


def preconditions_problems(gate: Gate, commit: str, scope: dict) -> list[str]:
    """InteropScopeValidator S014 for the gate: the amendment copy, its decision, the replaced #1408 record, A1's
    blockers, the registered re-proof checks with retained sources, and the pinned 1.3.0 decision text."""
    problems = []
    amendments = [a for a in scope.get("amendments", []) if isinstance(a, dict)]
    recorded = next((a for a in reversed(amendments) if isinstance(a.get("acceptancePreconditions"), dict)), None)
    pre = scope.get("acceptancePreconditions")
    if recorded is None or not isinstance(pre, dict) or pre != recorded["acceptancePreconditions"]:
        return ["they must equal the copy the latest amendment that records them registered"]
    decision = next((d for d in recorded.get("maintainerDecisions", []) if isinstance(d, dict) and d.get("id") == pre.get("decision")), None)
    if pre.get("amendment") != recorded.get("version") or not decision or not decision.get("decision") or not decision.get("by") \
            or not re.match(r"^\d{4}-\d\d-\d\d", str(decision.get("date", ""))):
        problems.append("they must cite their amendment and a recorded maintainer decision")
    replaced = pre.get("replaces") or {}
    terminal = json_blob(gate, commit, str(replaced.get("terminalRecord", ""))) if replaced.get("terminalRecord") else None
    if replaced.get("issue") != 1408 or not terminal or terminal.get("issue") != 1408 or not terminal.get("outcome") \
            or terminal.get("outcome") != replaced.get("outcome"):
        problems.append("the replaced #1408 precondition must cite the retained #1408 terminal record and restate its outcome")
    a1 = next((s for s in scope.get("supportingIssues", []) if isinstance(s, dict) and s.get("issue") == 1430), None)
    blockers = set(a1.get("blockers", [])) if a1 else set()
    gaps = [g.get("issue") for g in pre.get("releaseGaps", []) if isinstance(g, dict)]
    if a1 is None or 1408 in blockers or any(g not in gaps for g in RELEASE_GAPS_1426) or any(g not in blockers for g in gaps):
        problems.append("A1 (#1430) must wait on every listed release gap (at least #1545, #1546, #1547) instead of #1408")
    reproof = pre.get("reproof") or {}
    checks = [c for c in reproof.get("checks", []) if isinstance(c, dict)]
    if reproof.get("onCandidate") != "own-sha" or any(
            not any((c.get("id"), c.get("kind"), c.get("gate"), c.get("source")) == required for c in checks) for required in REPROOF_CHECKS_1426) \
            or any(not c.get("id") or not c.get("requirement") or gate.blob(commit, str(c.get("source", ""))) is None for c in checks):
        problems.append("the re-proof must run on the candidate's own SHA with the registered checks, each citing a retained source")
    first = next((a.get("acceptancePreconditions") for a in amendments if a.get("version") == "1.3.0"), None)
    if first is None or sha256(dotnet_json(first).encode()) != PRECONDITIONS_DIGEST_130:
        problems.append("amendment 1.3.0's recorded preconditions differ from the pinned decision text")
    return problems


def ready_problems(gate: Gate, commit: str, candidate: str, family: str, ready: object, scope: dict, register: dict) -> list[str]:
    """InteropScopeValidator S007 for a READY record kept under the family's evidence directory (D-READY-EVIDENCE-DIRS)."""
    if not isinstance(ready, dict):
        return ["the READY record is not a JSON object"]
    problems = []
    fam = next((f for f in scope.get("families", []) if isinstance(f, dict) and f.get("id") == family), {})
    versions = {"1.0.0"} | {a.get("version") for a in scope.get("amendments", []) if isinstance(a, dict)}
    if ready.get("family") != family or ready.get("scopeVersion") not in versions:
        problems.append("it must name its family and the registered scope version it was assessed under")
    if (ready.get("candidate") or {}).get("commit") != candidate if isinstance(ready.get("candidate"), dict) else True:
        problems.append("candidate.commit must be the adjudicated candidate")
    cells = {c.get("id"): c.get("cells") or [] for c in scope.get("cases", []) if isinstance(c, dict)}
    registered = next((f.get("cases", []) for f in register.get("families", []) if isinstance(f, dict) and f.get("id") == family), [])
    rows = {r.get("id"): r for r in ready.get("cases", []) if isinstance(r, dict)}
    for case in (c for c in registered if isinstance(c, dict)):
        cid, row = case.get("id"), rows.get(case.get("id"))
        record = json_blob(gate, commit, str(row.get("evidence", ""))) if row and str(row.get("evidence", "")).startswith("docs/plans/evidence/") else None
        if not row or row.get("result") != "passed" or not record or record.get("candidate") != candidate or record.get("caseId") != cid \
                or record.get("requirementSha256") != case.get("requirementSha256") or record.get("result") != "passed" \
                or any((record.get("cells") or {}).get(cell) != "passed" for cell in cells.get(cid, [])):
            problems.append(f"case {cid} needs a passed result and a retained passed result record for this candidate, case, requirement and cells")
    tests = ready.get("tests")
    if not isinstance(tests, list) or not tests or any(not isinstance(t, dict) or not t.get("project") or not isinstance(t.get("total"), int)
                                                       or t["total"] <= 0 or t.get("failed") != 0 for t in tests):
        problems.append("tests must name each project with a positive total and zero failed")
    examples = ready.get("websiteExamples")
    if not isinstance(examples, list) or not examples or any(not str(e).startswith("website/content/") or gate.blob(commit, str(e).split("#")[0]) is None for e in examples):
        problems.append("websiteExamples must list website pages that exist")
    reviews = ready.get("reviews")
    if not isinstance(reviews, list) or not reviews or any(not isinstance(r, dict) or not r.get("reviewer") or "/reviews/" not in str(r.get("record", ""))
                                                           or not str(r.get("record")).startswith("docs/plans/evidence/") or gate.blob(commit, str(r.get("record"))) is None for r in reviews):
        problems.append("reviews must name each reviewer and a retained review record")
    approver = ready.get("approver") if isinstance(ready.get("approver"), dict) else {}
    if not approver.get("login") or not APPROVAL_URL_RE.match(str(approver.get("approvalUrl", ""))):
        problems.append("approver needs a login and a GitHub approval URL")
    if ready.get("independence") != "reduced":
        problems.append("independence must be 'reduced' while the deviation stands")
    resolved = [b for b in ready.get("blockersResolved", []) if isinstance(b, dict)]
    for blocker in fam.get("blockers", []):
        if not any(b.get("issue") == blocker and b.get("reference") for b in resolved):
            problems.append(f"blocker #{blocker} has no recorded resolution")
    for mapped in ((scope.get("mappedBlockers") or {}).get("applicable") or []):
        if family not in mapped.get("families", []):
            continue
        res = next((b for b in resolved if b.get("issue") == mapped.get("pr")), {})
        run = json_blob(gate, commit, str(res.get("witnessRun", ""))) if str(res.get("witnessRun", "")).startswith("docs/plans/evidence/") else None
        if res.get("mergeCommit") != mapped.get("mergeCommit") or not run or run.get("candidate") != candidate or run.get("repair") != mapped.get("repair") \
                or run.get("witnesses") != mapped.get("regressionWitness") or run.get("result") != "passed":
            problems.append(f"mapped blocker {mapped.get('repair')} (#{mapped.get('pr')}) needs its merge commit and a retained passing witness run on this candidate")
    return problems


def check_terminal_1426(gate: Gate, commit: str, record: dict, entry: dict, scope: dict, register: dict) -> None:
    """#1430's terminal record: ACCEPTED or PARTIAL_READY, reduced independence, and one row per family, re-proof
    check, release gap and registered claim. A READY family cites its READY record under the family's evidence
    directory's ready/ (D-READY-EVIDENCE-DIRS), committed at the adjudication commit and complete under S007."""
    a1 = entry["a1Issue"]
    if record.get("issue") != a1:
        gate.fail("G006", f"record is not the #{a1} terminal record")
    outcome = record.get("outcome")
    if outcome not in OUTCOMES_1426:
        gate.fail("G006", f"terminal outcome is {outcome!r}, not one of {', '.join(OUTCOMES_1426)}")
    independence = scope.get("independence") or {}
    deviation = independence.get("deviation") is not False
    if deviation:
        if record.get("adjudicationIndependence") != "reduced":
            gate.fail("G007", "adjudicationIndependence must be 'reduced' under the independence deviation")
        if not independence.get("statement") or record.get("limitation") != independence.get("statement"):
            gate.fail("G007", "record must carry the scope's independence statement verbatim")
        if record.get("epicIndependentAdjudicationMet") is not False:
            gate.fail("G007", "record must state epicIndependentAdjudicationMet = false")
    families = {f.get("id"): (f.get("readiness") or {}).get("evidence") for f in scope.get("families", []) if isinstance(f, dict)}
    pre = scope.get("acceptancePreconditions") or {}
    expected = {f"family:{f}": "family" for f in families}
    expected |= {f"reproof:{c.get('id')}": "reproof" for c in (pre.get("reproof") or {}).get("checks", []) if isinstance(c, dict)}
    expected |= {f"gap:#{g.get('issue')}": "gap" for g in pre.get("releaseGaps", []) if isinstance(g, dict)}
    expected |= {claim: "claim" for claim in claim_registry(gate, commit, record, entry)}
    allowed = {"family": {"READY", "PENDING"}, "reproof": {"PASSED"}, "gap": {"CLOSED"}, "claim": {"BOUNDED"}}
    rows = record.get("adjudications")
    if not isinstance(rows, list) or not rows:
        gate.fail("G008", "record has no adjudication rows")
        return
    candidate = (record.get("candidate") or {}).get("commit") if isinstance(record.get("candidate"), dict) else None
    seen, ready = set(), 0
    for row in rows:
        subject = row.get("subject") if isinstance(row, dict) else None
        name = subject or "?"
        if not isinstance(subject, str) or subject in seen:
            gate.fail("G008", f"{name}: missing or duplicate subject")
            continue
        seen.add(subject)
        kind = expected.get(subject)
        if kind is None:
            gate.fail("G008", f"{name}: unknown or unregistered subject")
            continue
        if row.get("outcome") not in allowed[kind]:
            gate.fail("G008", f"{name}: outcome {row.get('outcome')!r} is not one of {sorted(allowed[kind])}")
        if row.get("independence") != ROW_INDEPENDENCE:
            gate.fail("G008", f"{name}: row must record independence = {ROW_INDEPENDENCE}")
        if kind == "family" and row.get("outcome") == "PENDING" and outcome != "PARTIAL_READY":
            gate.fail("G008", f"{name}: PENDING is valid only in a PARTIAL_READY record")
        if kind == "family" and row.get("outcome") == "READY":
            ready += 1
            family = subject[len("family:"):]
            ref = row.get("readyRecord") if isinstance(row.get("readyRecord"), dict) else {}
            path, evidence = str(ref.get("path", "")), families.get(family)
            home = (evidence.rsplit("/", 1)[0] + "/ready/") if isinstance(evidence, str) and "/" in evidence else None
            blob = gate.blob(commit, path) if home and path.startswith(home) and REPO_PATH_RE.match(path) else None
            if blob is None or sha256(blob) != ref.get("sha256"):
                gate.fail("G008", f"{name}: READY must cite a committed READY record under {home or 'the family evidence directory ready/'} by path and sha256")
                continue
            try:
                named = json.loads(blob)
            except (UnicodeDecodeError, json.JSONDecodeError):
                named = None
            for problem in ready_problems(gate, commit, candidate, family, named, scope, register):
                gate.fail("G008", f"{name}: READY record {path}: {problem}")
    if outcome == "PARTIAL_READY" and ready == 0:
        gate.fail("G008", "a PARTIAL_READY record needs at least one READY family")
    for missing in sorted(set(expected) - seen):
        gate.fail("G008", f"{missing}: required subject is not adjudicated")


def check_candidate(gate: Gate, commit: str, record: dict, entry: dict, args: argparse.Namespace) -> str | None:
    candidate = record.get("candidate")
    if not isinstance(candidate, dict):
        gate.fail("G009", "record has no candidate block")
        return None
    cand = candidate.get("commit")
    version = candidate.get("version")
    if not isinstance(cand, str) or not SHA40.match(cand):
        gate.fail("G009", "candidate commit must be a full 40-hex SHA")
        return None
    if not isinstance(version, str) or not re.match(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", version):
        gate.fail("G009", "candidate version must be MAJOR.MINOR.PATCH")
        return None
    if gate.resolve(cand) != cand or not gate.is_ancestor(cand, commit):
        gate.fail("G009", f"candidate {cand} is not an ancestor of the adjudication commit")
        return None
    props = gate.blob(cand, "Directory.Build.props")
    found = re.search(rb"<Version>([^<]+)</Version>", props or b"")
    if not found or found.group(1).decode() != version:
        gate.fail("G009", f"Directory.Build.props at the candidate does not declare version {version}")
    else:
        check_release_pr_products(gate, cand, version, record)
    packet = entry["contractPacket"]
    packet_at_candidate = gate.git("rev-parse", f"{cand}:{packet['dir']}", check=False).stdout.strip()
    packet_at_record = gate.git("rev-parse", f"{commit}:{packet['dir']}", check=False).stdout.strip()
    if not packet_at_candidate or packet_at_candidate != packet_at_record:
        gate.fail("G005", f"the #{packet['issue']} contract packet changed between the candidate and the adjudication")
    if packet["format"] == PACKET_FORMAT_1426 and any(
            gate.blob(cand, p) is None or gate.blob(cand, p) != gate.blob(commit, p) for p in (T1_REGISTER_1426, W1_RECORDS_1426)):
        gate.fail("G005", "the T1 register or W1 records changed between the candidate and the adjudication")
    if args.expect_head and gate.resolve("HEAD") != cand:
        gate.fail("G009", f"checked-out HEAD {gate.resolve('HEAD')} is not the adjudicated candidate {cand}")
    if args.version is not None and args.version != version:
        gate.fail("G009", f"requested version {args.version} is not the adjudicated version {version}")
    tag = f"v{version}"
    tagged = gate.resolve(f"refs/tags/{tag}")
    if tagged is not None and tagged != cand:
        gate.fail("G009", f"tag {tag} points at {tagged}, not the candidate {cand}")
    if args.require_tag and tagged is None:
        gate.fail("G009", f"tag {tag} does not exist")

    manifests = record.get("evidenceManifests")
    manifests = manifests if isinstance(manifests, list) else []
    for role in MANIFEST_ROLES:
        named = [m for m in manifests if isinstance(m, dict) and m.get("role") == role]
        if len(named) != 1 or not SHA64.match(str(named[0].get("sha256", ""))):
            gate.fail("G010", f"record must name exactly one '{role}' evidence manifest as {{role, path, sha256}}")
            continue
        blob = gate.blob(commit, str(named[0].get("path")))
        if blob is None or sha256(blob) != named[0]["sha256"]:
            gate.fail("G010", f"{role} manifest {named[0].get('path')} is missing or changed")
        elif manifest_candidate(blob) != cand:
            gate.fail("G010", f"{role} manifest {named[0].get('path')} does not bind the candidate {cand}")
    return cand


def check_release_pr_products(gate: Gate, cand: str, version: str, record: dict) -> None:
    """#1546: the release PR is not a publication surface. It prepares the candidate, and its only
    products are bytes of the candidate: the version in Directory.Build.props (checked above) and
    the CHANGELOG.md section the notes are rendered from. The identity names the candidate, so
    every surface consumes those products here, on every invocation, whether or not the caller
    passes --release-notes: the section must be the adjudicated notes and pass the wording scan."""
    notes_hash = surface(record, "release-notes").get("sha256")
    if not SHA64.match(str(notes_hash or "")):
        return  # the publication schema check reports the missing hash (G011)
    text = (gate.blob(cand, "CHANGELOG.md") or b"").decode("utf-8", "replace")
    if version_headers(text, version) > 1:
        gate.fail("G014", f"CHANGELOG.md at the candidate has more than one [{version}] header")
        return
    section = changelog_section(text, version)
    if not section.strip():
        gate.fail("G014", f"CHANGELOG.md at the candidate has no [{version}] section; the release PR's notes are missing")
        return
    if text_digest(section) != notes_hash:
        gate.fail("G014", f"the candidate's CHANGELOG.md [{version}] section (the release PR's notes) is not the adjudicated notes")
    scan_text(gate, f"CHANGELOG.md [{version}] at the candidate", section)


def manifest_candidate(blob: bytes) -> str | None:
    """The candidate a manifest binds: top-level "candidate" as a SHA or as {"commit": SHA}."""
    try:
        value = json.loads(blob).get("candidate")
    except (ValueError, AttributeError):
        return None
    return value.get("commit") if isinstance(value, dict) else value


def surface(record: dict, name: str) -> dict:
    block = (record.get("publication") or {}).get(name)
    return block if isinstance(block, dict) else {}


def check_publication_schema(gate: Gate, record: dict) -> None:
    """Every surface is adjudicated with well-formed hashes, whichever surface is being published."""
    for name, key in (("release-notes", "sha256"), ("website", "treeSha256")):
        if not SHA64.match(str(surface(record, name).get(key, ""))):
            gate.fail("G011", f"record must adjudicate '{name}' with a 64-hex {key}")
    for name in FILE_SURFACES:
        files = surface(record, name).get("files")
        if not isinstance(files, dict) or not files or not all(
            isinstance(k, str) and k and SHA64.match(str(v)) for k, v in files.items()
        ):
            gate.fail("G011", f"record must adjudicate '{name}' as a non-empty {{file: sha256}} map")
    benchmark = surface(record, "benchmark-results").get("files")
    if isinstance(benchmark, dict) and benchmark and set(benchmark) != set(BENCHMARK_PUBLICATION_FILES):
        gate.fail("G011", "record must adjudicate exactly the #1422 benchmark outputs "
                  f"{list(BENCHMARK_PUBLICATION_FILES)} under 'benchmark-results', got {sorted(benchmark)}")


def zip_entries(path: Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        return {i.filename: sha256(archive.read(i)) for i in archive.infolist() if not i.is_dir()}


def check_files(gate: Gate, name: str, expected: object, actual: dict[str, str]) -> None:
    if not isinstance(expected, dict) or not expected:
        gate.fail("G011", f"record lists no {name} files")
        return
    for path in sorted(set(expected) | set(actual)):
        if path not in expected:
            gate.fail("G011", f"{name}: {path} is not adjudicated")
        elif path not in actual:
            gate.fail("G011", f"{name}: adjudicated {path} is missing")
        elif actual[path] != expected[path]:
            gate.fail("G011", f"{name}: {path} hash {actual[path]} differs from the adjudicated hash")


def check_surfaces(gate: Gate, identity: str, record: dict, args: argparse.Namespace) -> None:
    check_publication_schema(gate, record)
    notes_hash = surface(record, "release-notes").get("sha256")
    if args.release_notes:
        text = Path(args.release_notes).read_text(encoding="utf-8")
        if text_digest(text) != notes_hash:
            gate.fail("G011", "release notes differ from the adjudicated notes")
        scan_text(gate, args.release_notes, text)
    canonical = f"v{(record.get('candidate') or {}).get('version')}"
    for flag, value in (("--release-tag", args.release_tag), ("--release-title", args.release_title)):
        if value is not None and value != canonical:
            gate.fail("G013", f"GitHub release {flag[10:]} {value!r} is not the canonical {canonical!r}")
    if args.release_body:
        lines = Path(args.release_body).read_text(encoding="utf-8").replace("\r\n", "\n").rstrip().split("\n")
        trailer = TRAILER_RE.match(lines[-1].strip()) if lines else None
        if not trailer or trailer.group(1) != identity:
            gate.fail("G013", "GitHub release body does not end with this adjudication identity")
        else:
            body = "\n".join(lines[:-1])
            if text_digest(body) != notes_hash:
                gate.fail("G013", "GitHub release body differs from the adjudicated notes")
            scan_text(gate, "release body", body)
    if args.nuget_dir:
        local = {p.name: p for p in Path(args.nuget_dir).glob("*.nupkg")}
        packages = {name: sha256(p.read_bytes()) for name, p in local.items()}
        check_files(gate, "nuget-packages", surface(record, "nuget-packages").get("files"), packages)
        for name, path in sorted(local.items()):
            with zipfile.ZipFile(path) as archive:
                # The nuspec and every text entry, including the README nuget.org renders.
                for entry in archive.namelist():
                    if entry.endswith(".nuspec") or Path(entry).suffix.lower() in TEXT_SUFFIXES:
                        scan_text(gate, f"{name}/{entry}", archive.read(entry).decode("utf-8", "replace"))
        if args.registry_dir:
            # nuget.org adds a repository signature, so an existing version is compared entry by
            # entry, ignoring only .signature.p7s; any other difference fails.
            by_lower = {name.lower(): path for name, path in local.items()}
            for existing in sorted(Path(args.registry_dir).glob("*.nupkg")):
                mine = by_lower.get(existing.name.lower())
                theirs = {k: v for k, v in zip_entries(existing).items() if k != ".signature.p7s"}
                if mine is None or zip_entries(mine) != theirs:
                    gate.fail("G011", f"nuget.org already serves {existing.name} with content other than the adjudicated package")
    if args.metadata_dir:
        metadata = {p.name: sha256(p.read_bytes()) for p in Path(args.metadata_dir).glob("*.json")}
        check_files(gate, "release-metadata", surface(record, "release-metadata").get("files"), metadata)
    if args.website_dir:
        root = Path(args.website_dir)
        if not root.is_dir() or tree_digest(root) != surface(record, "website").get("treeSha256"):
            gate.fail("G011", "website build differs from the adjudicated tree")
        for path in sorted(root.rglob("*")):
            if path.is_file() and path.suffix in TEXT_SUFFIXES:
                scan_text(gate, path.relative_to(root).as_posix(), path.read_text(encoding="utf-8", errors="replace"))
    if args.benchmark_worktree:
        # #1547: adjudicating the benchmark hashes is not approval to publish them. Publication
        # needs the record's explicit approval as well as the separate dispatch input.
        if surface(record, "benchmark-results").get("approved") is not True:
            gate.fail("G015", "the record does not approve benchmark publication "
                              "(publication.benchmark-results.approved is not true)")
        expected = surface(record, "benchmark-results").get("files")
        status = gate.git("status", "--porcelain=v1", "-z", "--untracked-files=all").stdout.decode()
        changed = {entry[3:] for entry in status.split("\0") if len(entry) > 3}
        actual = {}
        for path in sorted(changed | set(expected or {})):
            file = gate.repo / path
            actual[path] = sha256(file.read_bytes()) if file.is_file() else "<missing>"
        check_files(gate, "benchmark-results", expected, actual)
        for path in sorted(changed):
            file = gate.repo / path
            if file.is_file() and file.suffix in TEXT_SUFFIXES:
                scan_text(gate, path, file.read_text(encoding="utf-8", errors="replace"))


def run(args: argparse.Namespace) -> int:
    gate = Gate(Path(args.repo).resolve())
    outputs: dict[str, str] = {}
    try:
        loaded = load_record(gate, args.identity.strip())
        if loaded:
            commit, record, entry, entries = loaded
            if args.version is not None and map_entry(entries, args.version) is None:
                gate.fail("G009", f"requested version {args.version} is not in {MAP_FILE}")
            if entry["contractPacket"]["format"] == PACKET_FORMAT_1426:
                packet = check_scope_packet(gate, commit, record, entry)
                if packet:
                    check_terminal_1426(gate, commit, record, entry, *packet)
            else:
                packet = check_contract(gate, commit, record, entry)
                if packet:
                    check_terminal(gate, commit, record, entry, *packet)
            cand = check_candidate(gate, commit, record, entry, args)
            check_surfaces(gate, args.identity.strip(), record, args)
            if cand:
                version = record["candidate"]["version"]
                outputs = {"candidate": cand, "version": version, "tag": f"v{version}",
                           "prerelease": "true" if version.startswith("0.") or "-" in version else "false"}
    except MapError as error:
        gate.fail("G016", f"{MAP_FILE}: {error}")
    except Exception as error:  # noqa: BLE001 - any failure to verify is a failed gate
        gate.fail("G000", f"gate could not complete: {error}")

    if gate.errors:
        for error in gate.errors:
            print(f"::error::release gate (#1410) {error}")
        print(f"RELEASE GATE: FAIL ({len(gate.errors)} violation(s)); nothing may be published.")
        return 1
    print(f"RELEASE GATE: PASS {args.identity.strip()}")
    for key, value in outputs.items():
        print(f"{key}={value}")
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as handle:
            for key, value in outputs.items():
                handle.write(f"{key}={value}\n")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--identity", required=True, help="calor-adjudication:v1:<commit>:<sha256>")
    parser.add_argument("--repo", default=".")
    parser.add_argument("--expect-head", action="store_true", help="HEAD must be the adjudicated candidate")
    parser.add_argument("--version", help="version the caller intends to publish")
    parser.add_argument("--require-tag", action="store_true", help="tag v<version> must exist at the candidate")
    parser.add_argument("--release-notes", help="rendered notes file to compare and scan")
    parser.add_argument("--release-body", help="body of the existing GitHub release")
    parser.add_argument("--release-tag", help="tag name of the GitHub release; must be v<version>")
    parser.add_argument("--release-title", help="title of the GitHub release; must be v<version>")
    parser.add_argument("--nuget-dir", help="directory holding exactly the .nupkg files to push")
    parser.add_argument("--registry-dir", help="packages already on nuget.org for this version (with --nuget-dir)")
    parser.add_argument("--metadata-dir", help="directory holding exactly the release metadata .json files")
    parser.add_argument("--website-dir", help="built website tree about to be deployed")
    parser.add_argument("--benchmark-worktree", action="store_true",
                        help="every changed file in the work tree must be an adjudicated benchmark file")
    parser.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))
    return run(parser.parse_args(argv))


if __name__ == "__main__":
    sys.exit(main())
