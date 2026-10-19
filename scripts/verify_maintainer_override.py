#!/usr/bin/env python3
"""Maintainer-override release check for Calor 0.24.0 only (ungated; outside the #1410 gate).

The #1408 terminal record is MILESTONE-FAILED, so no successful adjudication identity exists and
scripts/verify_release_adjudication.py (the #1410 gate) refuses every 0.24.0 surface. On
2026-10-10 the maintainer authorized releasing 0.24.0 anyway, as a recorded override. This script
is the check for that override. It does not adjudicate anything and does not change the gate.

It runs only when a dispatch passes `maintainer_override: <version>` and no adjudication identity
(`--select-mode` enforces exactly one of the two). Any version but OVERRIDE_VERSION (0.24.0) fails
with O014, in mode selection and in the check, whatever the committed records say (#1548). It
passes only when:

  * the candidate commit is on fetched protected main and declares <Version>VERSION</Version>;
  * the candidate contains the override record the adjudication map names for 0.24.0
    (eng/release-adjudication-map.json at fetched protected main, #1567), naming VERSION,
    `gated: false`, the maintainer's verbatim authorization, and the map's terminal-record path
    and its sha256;
  * the candidate contains that terminal record with exactly that sha256, and its outcome is
    MILESTONE-FAILED (a successful record must use the gate, not this override);
  * every surface passed on the command line is consistent with the candidate: notes equal the
    candidate's CHANGELOG section and carry the record's required text; packages are exactly the
    two VERSION packages built from the candidate; metadata names the candidate and the package
    hashes; any package nuget.org already serves matches entry by entry; and the #1410 wording
    scan (G012) finds nothing.

What it cannot check, because no successful record exists: the adjudicated hashes of notes,
packages, metadata, and website (G011/G013), the contract packet (G005), and the row outcomes
(G006-G010). Anything missing, malformed, or unverifiable fails; there is no warning-only mode.
Tests: tests/Calor.Compiler.Tests/ReleaseGate/MaintainerOverrideTests.cs.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import verify_release_adjudication as r2  # noqa: E402  (the #1410 gate; reused, not changed)

# #1548: the override exists for 0.24.0 alone. Every other version must release through the #1410
# gate. The restriction lives here, in the script main runs, not in the committed record, so a
# record edited (or added) for another version cannot reopen the override; changing it takes a
# reviewed change to this file. 0.24.0 is already on nuget.org, so the override can only
# re-verify or re-deploy those bytes.
OVERRIDE_VERSION = "0.24.0"
assert OVERRIDE_VERSION == r2.OVERRIDE_RELEASE  # the map accepts maintainerOverride on this release only
OVERRIDE_SCHEMA = "calor.maintainer-override/1"
AUTHORIZATION = "I authorize changing the release gate"
FAILED = "MILESTONE-FAILED"
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
TRAILER_RE = re.compile(r"^<!-- calor-maintainer-override: v1:([0-9a-f]{40}):([0-9a-f]{64}) -->$")


def select_mode(identity: str, override: str) -> tuple[str | None, str | None]:
    identity, override = identity.strip(), override.strip()
    if identity and override:
        return None, "O001: both adjudication_identity and maintainer_override were given; pass exactly one"
    if not identity and not override:
        return None, "O001: neither adjudication_identity nor maintainer_override was given; pass exactly one"
    if override and VERSION_RE.match(override) and override != OVERRIDE_VERSION:
        return None, "O014: " + closed_message(override)
    return ("identity" if identity else "override"), None


def closed_message(version: str) -> str:
    return (f"the maintainer override is closed to {version}; it applies to {OVERRIDE_VERSION} only. "
            "Release through the #1410 adjudication gate")


# One rendering for both checks: the #1410 gate's.
changelog_section = r2.changelog_section


def load_json(gate: r2.Gate, code: str, data: bytes | None, what: str) -> dict | None:
    if data is None:
        gate.fail(code, f"no {what}")
        return None
    try:
        value = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        gate.fail(code, f"{what} is not valid JSON: {error}")
        return None
    if not isinstance(value, dict):
        gate.fail(code, f"{what} is not a JSON object")
        return None
    return value


def override_entry(gate: r2.Gate) -> dict:
    """The map's 0.24.0 entry at fetched main (#1567): its override, terminal record, and A1 issue."""
    entry = r2.map_entry(r2.load_map(gate), OVERRIDE_VERSION)
    if entry is None or entry["version"] != OVERRIDE_VERSION or "maintainerOverride" not in entry:
        raise r2.MapError(f"{r2.MAP_FILE} does not map {OVERRIDE_VERSION} to a maintainer override")
    return entry


def check_record(gate: r2.Gate, args: argparse.Namespace, version: str) -> tuple[str, dict, str] | None:
    if gate.git("rev-parse", "--is-shallow-repository").stdout.decode().strip() != "false":
        gate.fail("O003", "shallow clone; the check needs full history")
        return None
    if gate.resolve(r2.MAIN_REF) is None:
        gate.fail("O003", f"{r2.MAIN_REF} is not fetched; fetch protected main first")
        return None
    tag = f"v{version}"
    if args.candidate_tag:
        cand = gate.resolve(f"refs/tags/{tag}")
        if cand is None:
            gate.fail("O003", f"tag {tag} does not exist")
            return None
    else:
        cand = (args.candidate or "").strip()
        if not r2.SHA40.match(cand) or gate.resolve(cand) != cand:
            gate.fail("O003", f"candidate {cand!r} is not a full commit SHA present in this clone")
            return None
    if not gate.is_ancestor(cand, r2.MAIN_REF):
        gate.fail("O003", f"candidate {cand} is not on protected main")
        return None
    try:
        entry = override_entry(gate)
    except r2.MapError as error:
        gate.fail("O006", f"adjudication map: {error}")
        return None
    override_path, record_path, a1 = entry["maintainerOverride"], entry["terminalRecord"], entry["a1Issue"]

    raw = gate.blob(cand, override_path)
    override = load_json(gate, "O004", raw, f"maintainer override record at {cand}:{override_path}")
    if override is None:
        return None
    if override.get("schema") != OVERRIDE_SCHEMA:
        gate.fail("O004", f"override record schema must be {OVERRIDE_SCHEMA}")
    if override.get("version") != version or override.get("tag") != tag:
        gate.fail("O005", f"override record names version {override.get('version')!r}, not the dispatched {version}")
    if override.get("gated") is not False:
        gate.fail("O004", "override record must state gated: false")
    if override.get("authorization") != AUTHORIZATION:
        gate.fail("O004", "override record must carry the maintainer's verbatim authorization")
    if override.get("publishCommit") != "dispatch-ref":
        gate.fail("O004", "override record must set publishCommit to 'dispatch-ref'")
    if not re.match(r"^\d{4}-\d{2}-\d{2}$", str(override.get("decisionDate"))):
        gate.fail("O004", "override record must carry a YYYY-MM-DD decisionDate")
    if not isinstance(override.get("statement"), str) or "ungated" not in override["statement"]:
        gate.fail("O004", "override record must state that the release is ungated")
    if override.get("benchmarkPublication") != "none":
        gate.fail("O004", "override record must state benchmarkPublication: none")

    named = override.get("terminalRecord")
    named = named if isinstance(named, dict) else {}
    if named.get("path") != record_path or not r2.SHA64.match(str(named.get("sha256"))):
        gate.fail("O006", f"override record must name {record_path} and its 64-hex sha256")
        return None
    data = gate.blob(cand, record_path)
    if data is None:
        gate.fail("O006", f"no terminal record at {cand}:{record_path}")
        return None
    if r2.sha256(data) != named["sha256"]:
        gate.fail("O006", f"terminal record hash {r2.sha256(data)} does not match the override record {named['sha256']}")
        return None
    record = load_json(gate, "O006", data, "terminal record")
    if record is None:
        return None
    if record.get("schema") != r2.RECORD_SCHEMA or record.get("issue") != a1:
        gate.fail("O006", f"terminal record is not the #{a1} terminal record")
    if record.get("outcome") != FAILED or named.get("outcome") != FAILED:
        gate.fail("O007", f"terminal outcome is {record.get('outcome')!r}; the override applies only to {FAILED}")

    props = gate.blob(cand, "Directory.Build.props")
    found = re.search(rb"<Version>([^<]+)</Version>", props or b"")
    if not found or found.group(1).decode() != version:
        gate.fail("O005", f"Directory.Build.props at {cand} does not declare version {version}")
    if args.expect_head and gate.resolve("HEAD") != cand:
        gate.fail("O008", f"checked-out HEAD {gate.resolve('HEAD')} is not the candidate {cand}")
    tagged = gate.resolve(f"refs/tags/{tag}")
    if tagged is not None and tagged != cand:
        gate.fail("O008", f"tag {tag} points at {tagged}, not the candidate {cand}")
    if args.require_tag and tagged is None:
        gate.fail("O008", f"tag {tag} does not exist")
    return cand, override, r2.sha256(raw or b"")


def check_notes(gate: r2.Gate, label: str, text: str, expected: str, override: dict) -> None:
    if r2.text_digest(text) != r2.text_digest(expected):
        gate.fail("O009", f"{label} differs from the candidate's CHANGELOG section")
    flat = re.sub(r"\s+", " ", text)
    required = override.get("requiredNoteText")
    for needle in required if isinstance(required, list) and required else ["<requiredNoteText missing>"]:
        if str(needle) not in flat:
            gate.fail("O009", f"{label} does not say {needle!r}")
    r2.scan_text(gate, label, text)


def nuspec_metadata(data: bytes) -> dict[str, str]:
    """id and version from <package><metadata>, ignoring the XML namespace and comments."""
    try:
        root = ElementTree.fromstring(data)
    except ElementTree.ParseError:
        return {}
    local = lambda tag: tag.rsplit("}", 1)[-1]  # noqa: E731
    metadata = [child for child in root if local(child.tag) == "metadata"]
    if local(root.tag) != "package" or len(metadata) != 1:
        return {}
    found: dict[str, str] = {}
    for child in metadata[0]:
        if local(child.tag) in ("id", "version"):
            found[local(child.tag)] = "<duplicate>" if local(child.tag) in found else (child.text or "").strip()
    return found


def check_metadata(gate: r2.Gate, root: Path, cand: str, hashes: dict[str, str]) -> None:
    """scripts/generate-release-metadata.py output: one SBOM and one provenance statement, each
    naming exactly the packages about to be pushed (name -> sha256) and the candidate commit."""
    files = {p.name: p for p in root.glob("*.json")}
    sboms = [n for n in files if n.endswith(".sbom.spdx.json")]
    provs = [n for n in files if n.endswith(".provenance.json")]
    if not hashes or len(files) != 2 or len(sboms) != 1 or len(provs) != 1:
        gate.fail("O012", f"metadata must be one .sbom.spdx.json and one .provenance.json for --nuget-dir, found {sorted(files)}")
        return
    try:
        sbom = json.loads(files[sboms[0]].read_text(encoding="utf-8"))
        prov = json.loads(files[provs[0]].read_text(encoding="utf-8"))
        # Lists, not dicts: a duplicate name (a wrong entry beside the right one) must fail.
        sbom_files = sorted((f["fileName"], tuple((c["algorithm"], c["checksumValue"]) for c in f["checksums"]))
                            for f in sbom["files"])
        subjects = sorted((s["name"], s["digest"]["sha256"], len(s["digest"])) for s in prov["subject"])
        commit = prov["predicate"]["buildDefinition"]["externalParameters"]["commit"]
        namespace = re.fullmatch(r"https://github\.com/juanmicrosoft/calor/sbom/([0-9a-f]{40})/[A-Za-z0-9._%-]+",
                                 str(sbom["documentNamespace"]))
    except (ValueError, KeyError, IndexError, TypeError) as error:
        gate.fail("O012", f"release metadata does not have the generated shape: {error!r}")
        return
    if sbom_files != sorted((n, (("SHA256", h),)) for n, h in hashes.items()):
        gate.fail("O012", f"{sboms[0]} does not list exactly the packages, each with one SHA256")
    if subjects != sorted((n, h, 1) for n, h in hashes.items()):
        gate.fail("O012", f"{provs[0]} subjects are not exactly the packages and their sha256")
    if commit != cand or not namespace or namespace.group(1) != cand:
        gate.fail("O012", f"release metadata does not name the candidate {cand}")
    for name, path in sorted(files.items()):
        r2.scan_text(gate, name, path.read_text(encoding="utf-8"))


def check_surfaces(gate: r2.Gate, args: argparse.Namespace, cand: str, version: str,
                   override: dict, override_sha: str) -> None:
    tag = f"v{version}"
    section = changelog_section((gate.blob(cand, "CHANGELOG.md") or b"").decode("utf-8", "replace"), version)
    if (args.release_notes or args.release_body) and not section.strip():
        gate.fail("O009", f"CHANGELOG.md at {cand} has no [{version}] section")
    if args.release_notes:
        check_notes(gate, "release notes", Path(args.release_notes).read_text(encoding="utf-8"), section, override)
    for flag, value in (("tag", args.release_tag), ("title", args.release_title)):
        if value is not None and value != tag:
            gate.fail("O010", f"GitHub release {flag} {value!r} is not {tag!r}")
    if args.release_body:
        lines = Path(args.release_body).read_text(encoding="utf-8").replace("\r\n", "\n").rstrip().split("\n")
        trailer = TRAILER_RE.match(lines[-1].strip()) if lines else None
        if not trailer or trailer.groups() != (cand, override_sha):
            gate.fail("O010", f"GitHub release body does not end with <!-- calor-maintainer-override: v1:{cand}:{override_sha} -->")
        else:
            check_notes(gate, "release body", "\n".join(lines[:-1]), section, override)

    hashes: dict[str, str] = {}
    if args.nuget_dir:
        # The packages carry no source commit (the nuspec repository element has none), so the
        # binding to the candidate is --expect-head in the job that packs, plus the metadata check.
        local = {p.name: p for p in Path(args.nuget_dir).glob("*.nupkg")}
        wanted = {f"calor.{version}.nupkg": "calor", f"calor.sdk.{version}.nupkg": "calor.sdk"}
        if {name.lower() for name in local} != set(wanted) or len(local) != 2:
            gate.fail("O011", f"package directory must hold exactly {sorted(wanted)}, found {sorted(local)}")
        for name, path in sorted(local.items()):
            hashes[name] = r2.sha256(path.read_bytes())
            with zipfile.ZipFile(path) as archive:
                specs = [e for e in archive.namelist() if e.endswith(".nuspec") and "/" not in e]
                if len(specs) != 1:
                    gate.fail("O011", f"{name}: expected exactly one root .nuspec, found {len(specs)}")
                else:
                    meta = nuspec_metadata(archive.read(specs[0]))
                    if meta.get("id", "").lower() != wanted.get(name.lower()) or meta.get("version") != version:
                        gate.fail("O011", f"{name}: nuspec id/version {meta.get('id')!r}/{meta.get('version')!r} "
                                          f"is not {wanted.get(name.lower())!r}/{version!r}")
                for entry in archive.namelist():
                    if entry.endswith(".nuspec") or Path(entry).suffix.lower() in r2.TEXT_SUFFIXES:
                        r2.scan_text(gate, f"{name}/{entry}", archive.read(entry).decode("utf-8", "replace"))
        if args.registry_dir:
            # Same rule as the gate: an existing nuget.org version must equal the package about to
            # be pushed, entry by entry, ignoring only nuget.org's repository signature.
            by_lower = {name.lower(): path for name, path in local.items()}
            for existing in sorted(Path(args.registry_dir).glob("*.nupkg")):
                mine = by_lower.get(existing.name.lower())
                theirs = {k: v for k, v in r2.zip_entries(existing).items() if k != ".signature.p7s"}
                if mine is None or r2.zip_entries(mine) != theirs:
                    gate.fail("O011", f"nuget.org already serves {existing.name} with content other than this package")
    if args.metadata_dir:
        check_metadata(gate, Path(args.metadata_dir), cand, hashes)
    if args.website_dir:
        root = Path(args.website_dir)
        pages = [p for p in sorted(root.rglob("*")) if p.is_file()] if root.is_dir() else []
        if not pages:
            gate.fail("O013", "website build is missing or empty")
        for path in pages:
            if path.suffix in r2.TEXT_SUFFIXES:
                r2.scan_text(gate, path.relative_to(root).as_posix(), path.read_text(encoding="utf-8", errors="replace"))


def run(args: argparse.Namespace) -> int:
    if args.select_mode:
        mode, error = select_mode(args.identity, args.override)
        if error:
            print(f"::error::release mode {error}")
            return 1
        print(f"mode={mode}")
        if args.github_output:
            with open(args.github_output, "a", encoding="utf-8") as handle:
                handle.write(f"mode={mode}\n")
        return 0

    gate = r2.Gate(Path(args.repo).resolve())
    outputs: dict[str, str] = {}
    try:
        mode, error = select_mode(args.identity, args.override)
        version = args.override.strip()
        if error:
            gate.errors.append(error)
        elif mode != "override":
            gate.fail("O001", "an adjudication identity was given; use scripts/verify_release_adjudication.py")
        elif not VERSION_RE.match(version):
            gate.fail("O002", f"maintainer_override must be MAJOR.MINOR.PATCH, got {version!r}")
        elif version != OVERRIDE_VERSION:
            gate.fail("O014", closed_message(version))  # unreachable after select_mode; kept as a second lock
        else:
            checked = check_record(gate, args, version)
            if checked:
                cand, override, override_sha = checked
                check_surfaces(gate, args, cand, version, override, override_sha)
                outputs = {"candidate": cand, "version": version, "tag": f"v{version}",
                           "prerelease": "true" if version.startswith("0.") else "false",
                           "override_sha256": override_sha}
    except Exception as error:  # noqa: BLE001 - any failure to verify is a failed check
        gate.fail("O000", f"override check could not complete: {error}")

    if gate.errors:
        for error in gate.errors:
            print(f"::error::maintainer override {error}")
        print(f"MAINTAINER OVERRIDE: FAIL ({len(gate.errors)} violation(s)); nothing may be published.")
        return 1
    print(f"MAINTAINER OVERRIDE: PASS {outputs['version']} at {outputs['candidate']} (UNGATED; outside #1410)")
    for key, value in outputs.items():
        print(f"{key}={value}")
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as handle:
            for key, value in outputs.items():
                handle.write(f"{key}={value}\n")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--override", default="", help="maintainer_override dispatch input (a version)")
    parser.add_argument("--identity", default="", help="adjudication_identity dispatch input; must be empty")
    parser.add_argument("--select-mode", action="store_true", help="only decide identity vs override mode")
    parser.add_argument("--repo", default=".")
    where = parser.add_mutually_exclusive_group()
    where.add_argument("--candidate", help="full SHA of the commit to publish (the dispatch ref)")
    where.add_argument("--candidate-tag", action="store_true", help="the candidate is the commit tag v<version> names")
    parser.add_argument("--expect-head", action="store_true", help="HEAD must be the candidate")
    parser.add_argument("--require-tag", action="store_true", help="tag v<version> must exist at the candidate")
    parser.add_argument("--release-notes", help="rendered notes file to compare and scan")
    parser.add_argument("--release-body", help="body of the GitHub release")
    parser.add_argument("--release-tag", help="tag name of the GitHub release; must be v<version>")
    parser.add_argument("--release-title", help="title of the GitHub release; must be v<version>")
    parser.add_argument("--nuget-dir", help="directory holding exactly the .nupkg files to push")
    parser.add_argument("--registry-dir", help="packages nuget.org already serves for this version")
    parser.add_argument("--metadata-dir", help="directory holding the release metadata .json files")
    parser.add_argument("--website-dir", help="built website tree about to be deployed")
    parser.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))
    return run(parser.parse_args(argv))


if __name__ == "__main__":
    sys.exit(main())
