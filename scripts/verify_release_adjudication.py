#!/usr/bin/env python3
"""Fail-closed release gate for milestone 0.24 (#1410).

Every public release surface (NuGet packages, the GitHub release and its notes,
release metadata, the website, benchmark publication, and the installed-tool
check) runs this script before it acts. It accepts exactly one adjudication
identity:

    calor-adjudication:v1:<adjudication commit, 40 hex>:<record SHA-256, 64 hex>

The identity names the #1408 terminal record committed at RECORD_PATH in a
commit on protected main, and the SHA-256 of that blob's exact bytes. The script
exits 0 only when the record is the one named, the record is a successful
adjudication under the frozen #1407 contract, the checkout being published is
the adjudicated candidate, and every surface artifact passed on the command
line matches the hash the record adjudicated. Anything missing, malformed,
changed, or unverifiable is a failure; there is no warning-only mode and no
override flag.

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

RECORD_PATH = "docs/plans/evidence/adjudication-1408/terminal-record.json"
PACKET_DIR = "docs/plans/evidence/evidence-contract-1407"
RECORD_SCHEMA = "calor.adjudication-terminal-record/1"
MAIN_REF = "refs/remotes/origin/main"
IDENTITY_RE = re.compile(r"^calor-adjudication:v1:([0-9a-f]{40}):([0-9a-f]{64})$")
TRAILER_RE = re.compile(r"^<!-- calor-adjudication: (\S+) -->$")
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
FILE_SURFACES = ("nuget-packages", "release-metadata", "benchmark-results")
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
        gate.fail("G012", f"{label}: forbidden wording '{hit}'; 0.24 evidence is not independently adjudicated")


def load_record(gate: Gate, identity: str) -> tuple[str, dict] | None:
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

    data = gate.blob(commit, RECORD_PATH)
    if data is None:
        gate.fail("G004", f"no terminal record at {commit}:{RECORD_PATH}")
        return None
    if sha256(data) != digest:
        gate.fail("G004", f"terminal record hash {sha256(data)} does not match the identity {digest}")
        return None
    try:
        record = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        gate.fail("G004", f"terminal record is not valid JSON: {error}")
        return None
    if not isinstance(record, dict) or record.get("schema") != RECORD_SCHEMA:
        gate.fail("G004", f"terminal record schema must be {RECORD_SCHEMA}")
        return None
    return commit, record


def check_contract(gate: Gate, commit: str, record: dict) -> tuple[dict, dict] | None:
    raw_contract = gate.blob(commit, f"{PACKET_DIR}/contract.json")
    raw_inventory = gate.blob(commit, f"{PACKET_DIR}/artifact-inventory.json")
    raw_hashes = gate.blob(commit, f"{PACKET_DIR}/sha256.json")
    if raw_contract is None or raw_inventory is None or raw_hashes is None:
        gate.fail("G005", "the #1407 contract packet is missing at the adjudication commit")
        return None
    contract = json.loads(raw_contract)
    inventory = json.loads(raw_inventory)
    files = json.loads(raw_hashes).get("files")
    if contract.get("status") != "FROZEN" or contract.get("gateStatus") != "MET":
        gate.fail("G005", "the #1407 contract is not FROZEN with R0 MET")
    if not isinstance(files, dict) or not files:
        gate.fail("G005", "sha256.json lists no packet files")
        return None
    for path, expected in files.items():
        blob = gate.blob(commit, path)
        actual = None if blob is None else sha256(blob.replace(b"\r\n", b"\n"))
        if actual != expected:
            gate.fail("G005", f"packet file {path} does not match sha256.json")
    declared = record.get("contract")
    if not isinstance(declared, dict):
        gate.fail("G005", "record has no contract block")
    else:
        if declared.get("version") != contract.get("contractVersion"):
            gate.fail("G005", "record contract version differs from the committed contract")
        if declared.get("files") != files:
            gate.fail("G005", "record contract hashes differ from sha256.json; the contract changed after adjudication")
    return contract, inventory


def claim_registry(gate: Gate, commit: str, record: dict) -> set[str]:
    """The #1424 claim registry the record names: {path, sha256} of {"claims": ["claim:<id>", ...]}."""
    ref = record.get("claimRegistry")
    blob = gate.blob(commit, str(ref.get("path"))) if isinstance(ref, dict) else None
    if blob is None or sha256(blob) != ref.get("sha256"):
        gate.fail("G008", "record must name the committed #1424 claim registry by path and sha256")
        return set()
    claims = json.loads(blob).get("claims")
    if not isinstance(claims, list) or not all(
        isinstance(c, str) and c.startswith("claim:") and len(c) > 6 for c in claims
    ):
        gate.fail("G008", "claim registry must list 'claim:<id>' strings")
        return set()
    return set(claims)


def check_terminal(gate: Gate, commit: str, record: dict, contract: dict, inventory: dict) -> None:
    if record.get("issue") != 1408:
        gate.fail("G006", "record is not the #1408 terminal record")
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
    required |= {f"gate:#{c['issue']}" for c in contract.get("children", []) if c.get("issue") != 1408}
    required |= claim_registry(gate, commit, record)
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


def check_candidate(gate: Gate, commit: str, record: dict, args: argparse.Namespace) -> str | None:
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
    packet_at_candidate = gate.git("rev-parse", f"{cand}:{PACKET_DIR}", check=False).stdout.strip()
    packet_at_record = gate.git("rev-parse", f"{commit}:{PACKET_DIR}", check=False).stdout.strip()
    if not packet_at_candidate or packet_at_candidate != packet_at_record:
        gate.fail("G005", "the #1407 contract packet changed between the candidate and the adjudication")
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
            commit, record = loaded
            packet = check_contract(gate, commit, record)
            if packet:
                check_terminal(gate, commit, record, *packet)
            cand = check_candidate(gate, commit, record, args)
            check_surfaces(gate, args.identity.strip(), record, args)
            if cand:
                version = record["candidate"]["version"]
                outputs = {"candidate": cand, "version": version, "tag": f"v{version}",
                           "prerelease": "true" if version.startswith("0.") or "-" in version else "false"}
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
