#!/usr/bin/env python3
"""Release checks on the shipped bits (#1582). publish-nuget.yml runs every subcommand; each one
exits nonzero on any problem, and the workflow never masks that exit.

  preflight --version V    The dispatch names the version to release. It must equal the one
                           <Version> in Directory.Build.props, CHANGELOG.md must hold exactly one
                           non-empty `## [V]` section, and a local tag vV must be absent or point
                           at HEAD. Writes candidate, version, tag, prerelease to $GITHUB_OUTPUT.
  bundle --version V --nuget-dir D
                           D holds exactly calor.V.nupkg and Calor.Sdk.V.nupkg.
  verify-bundle --version V --dir D --sums-sha256 H
                           The downloaded bundle D is exactly the package job's: SHA256SUMS hashes
                           to H, each listed file matches, and nothing else is there.
  wording PATH...          The forbidden-wording scan (formerly gate code G012): release notes,
                           package text entries, or a built site must not claim the release
                           evidence was independently adjudicated or verified.
  registry --version V --nuget-dir D --mode existing|served
                           existing: any version nuget.org already serves must equal the local
                           package entry by entry; a 404 means absent; any other answer fails.
                           served: poll until nuget.org serves both packages, then compare them.
                           Only `.signature.p7s` may differ (nuget.org adds a repository
                           signature, so a plain file hash always differs). Without this,
                           `dotnet nuget push --skip-duplicate` would accept a different package.

--dry-run true (preflight, registry --mode existing) reports "already released" findings (a tag at
another commit, a different package already on nuget.org) as warnings, so a dry run of the version
already on main still exercises every other check. A real release passes no flag and fails on them.

changelog_section, version_headers, and tree_digest live here; render_release_notes.py,
reproducible_builds.py, and (until #1583 retires it) verify_release_adjudication.py import them.
Tests: scripts/test_release_checks.py.
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
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from typing import Callable

VERSION_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$")
PACKAGE_IDS = ("calor", "calor.sdk")
REGISTRY = "https://api.nuget.org/v3-flatcontainer"
SIGNATURE = ".signature.p7s"

# The forbidden wording (formerly gate code G012). A phrase is allowed only when it is negated.
FORBIDDEN_RE = re.compile(
    r"independently\s+(?:adjudicated|verified)|independent\s+(?:adjudication|verification)",
    re.IGNORECASE,
)
NEGATION_RE = re.compile(r"\b(?:not|no|without)\s+$", re.IGNORECASE)
TEXT_SUFFIXES = {".html", ".htm", ".txt", ".md", ".mdx", ".json", ".xml", ".rss"}


# Marks a finding that means "this version was already released from another commit". A real
# release fails on it. A dry run (--dry-run true) publishes nothing, so it reports it as a warning:
# a dry run of the version already on main must still exercise every other check.
ALREADY_RELEASED = "already released: "


class CheckError(Exception):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


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


# ---- wording ---------------------------------------------------------------------------------

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


def wording_hits(label: str, text: str) -> list[str]:
    """Forbidden phrases the text, as any reader would see it, still asserts. A negation counts
    only where it is literal in the source: one that appears only after markup is removed (a
    hidden "not ") leaves the rendered claim standing."""
    hits: set[str] = set()
    for source in sources(label, text):
        _, literal_negations = forbidden_wording(html.unescape(source))
        for variant in renderings(source):
            found, negated = forbidden_wording(variant)
            hits.update(found)
            if negated > literal_negations:
                hits.add("negation supplied by markup")
    return sorted(hits)


def scan_paths(paths: list[Path]) -> list[str]:
    """Problems for every file, directory tree, or .nupkg (nuspec and text entries) given."""
    problems: list[str] = []

    def scan(label: str, text: str) -> None:
        for hit in wording_hits(label, text):
            problems.append(f"{label}: forbidden wording '{hit}'; release evidence is not independently adjudicated")

    for path in paths:
        if path.is_dir():
            for file in sorted(path.rglob("*")):
                if file.is_file() and file.suffix.lower() in TEXT_SUFFIXES:
                    scan(file.as_posix(), file.read_text(encoding="utf-8", errors="replace"))
        elif path.suffix.lower() == ".nupkg":
            with zipfile.ZipFile(path) as archive:
                for entry in archive.namelist():
                    if entry.endswith(".nuspec") or Path(entry).suffix.lower() in TEXT_SUFFIXES:
                        scan(f"{path.name}/{entry}", archive.read(entry).decode("utf-8", "replace"))
        elif path.is_file():
            scan(path.as_posix(), path.read_text(encoding="utf-8", errors="replace"))
        else:
            problems.append(f"{path}: does not exist")
    return problems


# ---- packages --------------------------------------------------------------------------------

def zip_entries(path: Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        return {i.filename: sha256(archive.read(i)) for i in archive.infolist() if not i.is_dir()}


def bundle_problems(nuget_dir: Path, version: str) -> list[str]:
    """The directory holds exactly the two VERSION packages, nothing else."""
    found = sorted(p.name for p in nuget_dir.iterdir()) if nuget_dir.is_dir() else []
    expected = sorted(f"{pid}.{version}.nupkg" for pid in ("calor", "Calor.Sdk"))
    if [f.lower() for f in found] != [e.lower() for e in expected]:
        return [f"{nuget_dir} must hold exactly {expected}, found {found}"]
    return []


BUNDLE_SUMS = "SHA256SUMS"
BUNDLE_PATH_RE = re.compile(r"^(?:nupkg/[A-Za-z0-9._-]+\.nupkg|release-metadata/[A-Za-z0-9._-]+\.json|release-notes/notes\.md)$")


def bundle_inventory_problems(root: Path, version: str, sums_sha256: str) -> list[str]:
    """The downloaded release bundle is exactly what the package job recorded: SHA256SUMS hashes to
    the package job's output, every listed file has its listed hash, and no other file, directory
    entry, or symlink exists. The bundle lives outside the checkout, so it can never replace a
    script the release jobs run (Codex round 2)."""
    sums = root / BUNDLE_SUMS
    if not sums.is_file() or sums.is_symlink():
        return [f"{sums} is missing"]
    if sha256(sums.read_bytes()) != sums_sha256:
        return [f"{BUNDLE_SUMS} does not hash to the package job's record {sums_sha256}"]
    problems: list[str] = []
    listed: dict[str, str] = {}
    for line in sums.read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  (?:\./)?(\S+)", line)
        if not match or not BUNDLE_PATH_RE.fullmatch(match.group(2)) or match.group(2) in listed:
            problems.append(f"{BUNDLE_SUMS} has an unexpected line: {line!r}")
            continue
        listed[match.group(2)] = match.group(1)
    present = {}
    for path in root.rglob("*"):
        rel = path.relative_to(root).as_posix()
        if path.is_symlink():
            problems.append(f"bundle holds a symlink: {rel}")
        elif path.is_file() and rel != BUNDLE_SUMS:
            present[rel] = path
    for rel in sorted(set(listed) | set(present)):
        if rel not in present:
            problems.append(f"bundle lacks the recorded {rel}")
        elif rel not in listed:
            problems.append(f"bundle holds {rel}, which the package job did not record")
        elif sha256(present[rel].read_bytes()) != listed[rel]:
            problems.append(f"bundle file {rel} differs from the package job's record")
    if not problems:
        problems += bundle_problems(root / "nupkg", version)
        if "release-notes/notes.md" not in listed:
            problems.append("bundle lacks the release notes")
    return problems


def local_package(nuget_dir: Path, package_id: str, version: str) -> Path:
    wanted = f"{package_id}.{version}.nupkg".lower()
    matches = [p for p in nuget_dir.glob("*.nupkg") if p.name.lower() == wanted]
    if len(matches) != 1:
        raise CheckError(f"{nuget_dir} has no single {wanted}")
    return matches[0]


def same_package(local: Path, served: Path) -> bool:
    """Entry by entry; only nuget.org's repository signature may differ."""
    theirs = {k: v for k, v in zip_entries(served).items() if k != SIGNATURE}
    return zip_entries(local) == theirs


# fetch(url, dest) -> HTTP status (200 writes dest). Raises OSError when there is no answer.
Fetch = Callable[[str, Path], int]


def http_fetch(url: str, dest: Path) -> int:
    request = urllib.request.Request(url, headers={"User-Agent": "calor-release-checks"})
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            dest.write_bytes(response.read())
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def registry_problems(version: str, nuget_dir: Path, mode: str, work: Path, fetch: Fetch = http_fetch,
                      base: str = REGISTRY, attempts: int = 60, interval: float = 30.0,
                      sleep: Callable[[float], None] = time.sleep) -> list[str]:
    problems: list[str] = []
    work.mkdir(parents=True, exist_ok=True)
    for package_id in PACKAGE_IDS:
        local = local_package(nuget_dir, package_id, version)
        url = f"{base}/{package_id}/{version}/{package_id}.{version}.nupkg"
        dest = work / f"{package_id}.{version}.nupkg"
        if mode == "existing":
            try:
                code = fetch(url, dest)
            except OSError as error:
                problems.append(f"cannot tell whether nuget.org has {package_id} {version}: {error}")
                continue
            if code == 404:
                print(f"nuget.org does not have {package_id} {version}")
                continue
            if code != 200:
                problems.append(f"cannot tell whether nuget.org has {package_id} {version} (HTTP {code})")
                continue
            print(f"nuget.org already has {package_id} {version}; comparing")
        else:
            code = 0
            for attempt in range(attempts):
                try:
                    code = fetch(url, dest)
                except OSError:
                    code = 0
                if code == 200:
                    break
                if attempt + 1 < attempts:
                    sleep(interval)
            if code != 200:
                problems.append(f"nuget.org does not serve {package_id} {version} after {attempts} attempts")
                continue
        if not same_package(local, dest):
            problems.append(f"{ALREADY_RELEASED if mode == 'existing' else ''}nuget.org serves {package_id} {version} "
                            "with content other than the package built here")
    return problems


# ---- preflight -------------------------------------------------------------------------------

def git(repo: Path, *args: str) -> str | None:
    result = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else None


def preflight(repo: Path, version: str) -> tuple[list[str], dict[str, str]]:
    problems: list[str] = []
    if not VERSION_RE.fullmatch(version):
        return [f"version {version!r} is not MAJOR.MINOR.PATCH[-PRERELEASE]"], {}
    props = (repo / "Directory.Build.props").read_text(encoding="utf-8")
    declared = re.findall(r"<Version>([^<]*)</Version>", props)
    if declared != [version]:
        problems.append(f"Directory.Build.props declares {declared}, not exactly [{version!r}]")
    changelog = (repo / "CHANGELOG.md").read_text(encoding="utf-8")
    if version_headers(changelog, version) != 1:
        problems.append(f"CHANGELOG.md must hold exactly one '## [{version}]' header")
    else:
        _, _, body = changelog_section(changelog, version).partition("\n")
        if not body.strip():
            problems.append(f"CHANGELOG.md section [{version}] is empty")
    head = git(repo, "rev-parse", "--verify", "HEAD^{commit}")
    if head is None:
        problems.append("cannot resolve HEAD")
        return problems, {}
    tag = f"v{version}"
    tagged = git(repo, "rev-parse", "--verify", "--quiet", f"refs/tags/{tag}^{{commit}}")
    if tagged is not None and tagged != head:
        problems.append(f"{ALREADY_RELEASED}tag {tag} already points at {tagged}, not HEAD {head}")
    prerelease = "true" if version.startswith("0.") or "-" in version else "false"
    return problems, {"candidate": head, "version": version, "tag": tag, "prerelease": prerelease}


# ---- CLI -------------------------------------------------------------------------------------

def report(problems: list[str], what: str, dry_run: bool = False) -> int:
    if dry_run:
        for problem in [p for p in problems if p.startswith(ALREADY_RELEASED)]:
            print(f"::warning::{what} (dry run; a release would fail here): {problem}")
        problems = [p for p in problems if not p.startswith(ALREADY_RELEASED)]
    for problem in problems:
        print(f"::error::{what}: {problem}")
    print(f"{what}: {'FAIL' if problems else 'PASS'}")
    return 1 if problems else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("preflight")
    p.add_argument("--version", required=True)
    p.add_argument("--repo", default=".")
    p.add_argument("--github-output", default=os.environ.get("GITHUB_OUTPUT"))
    p.add_argument("--dry-run", choices=("true", "false"), default="false")
    b = sub.add_parser("bundle")
    b.add_argument("--version", required=True)
    b.add_argument("--nuget-dir", required=True)
    v = sub.add_parser("verify-bundle")
    v.add_argument("--version", required=True)
    v.add_argument("--dir", required=True)
    v.add_argument("--sums-sha256", required=True)
    w = sub.add_parser("wording")
    w.add_argument("paths", nargs="+")
    r = sub.add_parser("registry")
    r.add_argument("--version", required=True)
    r.add_argument("--nuget-dir", required=True)
    r.add_argument("--mode", choices=("existing", "served"), required=True)
    r.add_argument("--work-dir", default=None)
    r.add_argument("--attempts", type=int, default=60)
    r.add_argument("--interval", type=float, default=30.0)
    r.add_argument("--dry-run", choices=("true", "false"), default="false")
    args = parser.parse_args(argv)
    try:
        if args.command == "preflight":
            problems, outputs = preflight(Path(args.repo), args.version)
            dry = args.dry_run == "true"
            if outputs and all(dry and p.startswith(ALREADY_RELEASED) for p in problems):
                for key, value in outputs.items():
                    print(f"{key}={value}")
                if args.github_output:
                    with open(args.github_output, "a", encoding="utf-8") as handle:
                        handle.writelines(f"{k}={v}\n" for k, v in outputs.items())
            return report(problems, "release preflight", dry)
        if args.command == "bundle":
            return report(bundle_problems(Path(args.nuget_dir), args.version), "release bundle")
        if args.command == "verify-bundle":
            return report(bundle_inventory_problems(Path(args.dir), args.version, args.sums_sha256), "release bundle")
        if args.command == "wording":
            return report(scan_paths([Path(p) for p in args.paths]), "release wording")
        work = Path(args.work_dir or os.environ.get("RUNNER_TEMP", ".")) / f"registry-{args.mode}"
        problems = registry_problems(args.version, Path(args.nuget_dir), args.mode, work,
                                     attempts=args.attempts, interval=args.interval)
        return report(problems, f"nuget.org {args.mode}-version check", args.dry_run == "true" and args.mode == "existing")
    except Exception as error:  # noqa: BLE001 - a check that cannot complete fails
        return report([f"could not complete: {error}"], f"release check {args.command}")


if __name__ == "__main__":
    sys.exit(main())
