#!/usr/bin/env python3
"""Maintainer-override release check for Calor 0.24.0 (ungated; outside the #1410 gate).

The #1408 terminal record is MILESTONE-FAILED, so no successful adjudication identity exists and
scripts/verify_release_adjudication.py (the #1410 gate) refuses every 0.24.0 surface. On
2026-10-10 the maintainer authorized releasing 0.24.0 anyway, as a recorded override. This script
is the check for that override. It does not adjudicate anything and does not change the gate.

It runs only when a dispatch passes `maintainer_override: <version>` and no adjudication identity
(`--select-mode` enforces exactly one of the two). It passes only when:

  * the candidate commit is on fetched protected main and declares <Version>VERSION</Version>;
  * the candidate contains OVERRIDE_PATH, naming VERSION, `gated: false`, the maintainer's
    verbatim authorization, and the terminal record's path and sha256;
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

sys.path.insert(0, str(Path(__file__).resolve().parent))
import verify_release_adjudication as r2  # noqa: E402  (the #1410 gate; reused, not changed)

OVERRIDE_PATH = "docs/plans/evidence/adjudication-1408/maintainer-override.json"
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
    return ("identity" if identity else "override"), None


def changelog_section(changelog: str, version: str) -> str:
    """The `## [VERSION]` section, as publish-nuget's awk renders it."""
    out, inside = [], False
    for line in changelog.replace("\r\n", "\n").split("\n"):
        if line.startswith(f"## [{version}]"):
            inside = True
        elif inside and line.startswith("## ["):
            break
        if inside:
            out.append(line)
    return "\n".join(out)


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

    raw = gate.blob(cand, OVERRIDE_PATH)
    override = load_json(gate, "O004", raw, f"maintainer override record at {cand}:{OVERRIDE_PATH}")
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
    if named.get("path") != r2.RECORD_PATH or not r2.SHA64.match(str(named.get("sha256"))):
        gate.fail("O006", f"override record must name {r2.RECORD_PATH} and its 64-hex sha256")
        return None
    data = gate.blob(cand, r2.RECORD_PATH)
    if data is None:
        gate.fail("O006", f"no terminal record at {cand}:{r2.RECORD_PATH}")
        return None
    if r2.sha256(data) != named["sha256"]:
        gate.fail("O006", f"terminal record hash {r2.sha256(data)} does not match the override record {named['sha256']}")
        return None
    record = load_json(gate, "O006", data, "terminal record")
    if record is None:
        return None
    if record.get("schema") != r2.RECORD_SCHEMA or record.get("issue") != 1408:
        gate.fail("O006", "terminal record is not the #1408 terminal record")
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
        local = {p.name: p for p in Path(args.nuget_dir).glob("*.nupkg")}
        wanted = {f"calor.{version}.nupkg", f"calor.sdk.{version}.nupkg"}
        if {name.lower() for name in local} != wanted or len(local) != 2:
            gate.fail("O011", f"package directory must hold exactly {sorted(wanted)}, found {sorted(local)}")
        for name, path in sorted(local.items()):
            hashes[name] = r2.sha256(path.read_bytes())
            with zipfile.ZipFile(path) as archive:
                for entry in archive.namelist():
                    if entry.endswith(".nuspec"):
                        nuspec = archive.read(entry).decode("utf-8", "replace")
                        found = re.search(r"<version>([^<]+)</version>", nuspec)
                        if not found or found.group(1) != version:
                            gate.fail("O011", f"{name}: nuspec version is not {version}")
                        commit = re.search(r'<repository\b[^>]*\bcommit="([^"]*)"', nuspec)
                        if commit and commit.group(1) != cand:
                            gate.fail("O011", f"{name}: built from {commit.group(1)}, not the candidate {cand}")
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
        files = sorted(Path(args.metadata_dir).glob("*.json"))
        if not files or not hashes:
            gate.fail("O012", "metadata needs a non-empty directory and --nuget-dir")
        for path in files:
            text = path.read_text(encoding="utf-8")
            try:
                json.loads(text)
            except ValueError:
                gate.fail("O012", f"{path.name} is not valid JSON")
            missing = [n for n, h in hashes.items() if h not in text] + ([cand] if cand not in text else [])
            if missing:
                gate.fail("O012", f"{path.name} does not name {', '.join(missing)}")
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
