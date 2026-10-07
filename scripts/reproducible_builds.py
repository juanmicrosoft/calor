#!/usr/bin/env python3
"""Prove that Calor's publication artifacts are byte-reproducible.

The release gate (#1410, scripts/verify_release_adjudication.py) rebuilds the NuGet
packages, the release metadata, and the website at publication and compares their
SHA-256 with the adjudicated hashes. That only works if two builds of one commit,
in different directories, produce the same bytes. This script checks exactly that.

  clone <dest> [--ref REF] [--z3 download|copy]
        Fresh `git clone --no-local` of this repository at REF into <dest>, with Z3
        bootstrapped by the owned script (download) or copied from this checkout's
        already-verified assets (copy); either way verify-z3-assets.py checks them.
  hash <build-out> --json FILE
        Hash every published file a reproducible-build.sh output directory holds:
        each .nupkg (and each zip entry with its timestamp), each release-metadata
        file, and each website file plus the gate's tree digest.
  compare A.json B.json [--markdown FILE]
        Exit 1 unless every hash in A equals the one in B (and no file is missing).
  run --workdir DIR [--evidence DIR] [--targets all|packages|website] [--z3 download|copy]
        The full proof: two fresh clones at different paths, each built with its own
        HOME, NuGet cache, npm cache and time zone, then hash and compare.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_release_adjudication import tree_digest  # noqa: E402  (the gate's own digest)

BUILD_SCRIPT = REPO_ROOT / "scripts/reproducible-build.sh"
Z3_DIRS = ("src/Calor.Compiler/z3", "src/Calor.Compiler/runtimes")
SCHEMA = "calor/reproducible-build-hashes/1"

# Two deliberately different environments for the two builds. Paths differ in depth
# and name; the time zone differs so a wall-clock or local-time leak shows up.
BUILDS = (
    {"name": "build-1", "tree": "a/calor", "tz": "UTC"},
    {"name": "build-2", "tree": "second-build/nested/elsewhere/calor-clone", "tz": "Pacific/Kiritimati"},
)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run(cmd: list[str], cwd: Path | None = None, env: dict | None = None, log: Path | None = None) -> None:
    print("+", " ".join(cmd), flush=True)
    if log is None:
        subprocess.run(cmd, cwd=cwd, env=env, check=True)
        return
    with log.open("ab") as stream:
        result = subprocess.run(cmd, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
    if result.returncode != 0:
        tail = log.read_text(encoding="utf-8", errors="replace").splitlines()[-40:]
        sys.exit(f"command failed ({result.returncode}): {' '.join(cmd)}\n" + "\n".join(tail))


# --------------------------------------------------------------------------- clone

def clone(dest: Path, ref: str, z3: str, log: Path | None = None) -> str:
    commit = subprocess.check_output(["git", "-C", str(REPO_ROOT), "rev-parse", f"{ref}^{{commit}}"], text=True).strip()
    if dest.exists():
        sys.exit(f"{dest} already exists; clone wants a fresh directory")
    dest.parent.mkdir(parents=True, exist_ok=True)
    run(["git", "clone", "--quiet", "--no-local", "--no-checkout", str(REPO_ROOT), str(dest)], log=log)
    run(["git", "-C", str(dest), "-c", "advice.detachedHead=false", "checkout", "--quiet", "--detach", commit], log=log)
    if z3 == "download":
        run(["bash", str(dest / "src/Calor.Compiler/scripts/download-z3.sh")], cwd=dest, log=log)
    else:
        for rel in Z3_DIRS:
            source = REPO_ROOT / rel
            if not source.is_dir():
                sys.exit(f"--z3 copy needs verified assets in {source}; bootstrap this checkout first")
            shutil.copytree(source, dest / rel)
    run([sys.executable, str(dest / "scripts/verify-z3-assets.py")], cwd=dest, log=log)
    return commit


# ---------------------------------------------------------------------------- hash

def hash_build(out: Path) -> dict:
    result: dict = {"schema": SCHEMA, "packages": {}, "releaseMetadata": {}, "website": None}
    for nupkg in sorted((out / "nupkg").glob("*.nupkg")):
        entries = {}
        with zipfile.ZipFile(nupkg) as archive:
            for info in archive.infolist():
                if info.is_dir():
                    continue
                entries[info.filename] = {
                    "sha256": sha256_bytes(archive.read(info)),
                    "size": info.file_size,
                    "zipDateTime": list(info.date_time),
                }
        result["packages"][nupkg.name] = {
            "sha256": sha256_file(nupkg),
            "size": nupkg.stat().st_size,
            "entries": dict(sorted(entries.items())),
        }
    for meta in sorted((out / "release-metadata").glob("*.json")):
        result["releaseMetadata"][meta.name] = sha256_file(meta)
    site = out / "website"
    if site.is_dir():
        files = {p.relative_to(site).as_posix(): sha256_file(p) for p in site.rglob("*") if p.is_file()}
        result["website"] = {"treeSha256": tree_digest(site), "fileCount": len(files), "files": dict(sorted(files.items()))}
    return result


# ------------------------------------------------------------------------- compare

def flatten(hashes: dict) -> dict[str, str]:
    """Every compared value under a stable key. Zip timestamps count: they are bytes."""
    flat: dict[str, str] = {}
    for name, pkg in hashes.get("packages", {}).items():
        flat[f"nupkg/{name}"] = pkg["sha256"]
        for entry, info in pkg["entries"].items():
            flat[f"nupkg/{name}!{entry}"] = f"{info['sha256']} @{tuple(info['zipDateTime'])}"
    for name, digest in hashes.get("releaseMetadata", {}).items():
        flat[f"release-metadata/{name}"] = digest
    site = hashes.get("website")
    if site:
        flat["website/<tree>"] = site["treeSha256"]
        for rel, digest in site["files"].items():
            flat[f"website/{rel}"] = digest
    return flat


def compare(a: dict, b: dict) -> list[str]:
    fa, fb = flatten(a), flatten(b)
    problems = []
    if not fa and not fb:
        return ["nothing was hashed in either build"]
    for key in sorted(set(fa) | set(fb)):
        if key not in fa:
            problems.append(f"only in B: {key}")
        elif key not in fb:
            problems.append(f"only in A: {key}")
        elif fa[key] != fb[key]:
            problems.append(f"differs: {key}: {fa[key]} != {fb[key]}")
    return problems


def markdown(a: dict, b: dict, labels: tuple[str, str], problems: list[str]) -> str:
    lines = [
        f"| Published file | {labels[0]} SHA-256 | {labels[1]} SHA-256 | Same |",
        "|---|---|---|---|",
    ]

    def row(name: str, x: str | None, y: str | None) -> None:
        lines.append(f"| `{name}` | `{x or 'missing'}` | `{y or 'missing'}` | {'yes' if x and x == y else '**no**'} |")

    for name in sorted(set(a["packages"]) | set(b["packages"])):
        row(name, a["packages"].get(name, {}).get("sha256"), b["packages"].get(name, {}).get("sha256"))
    for name in sorted(set(a["releaseMetadata"]) | set(b["releaseMetadata"])):
        row(name, a["releaseMetadata"].get(name), b["releaseMetadata"].get(name))
    if a.get("website") or b.get("website"):
        sa, sb = a.get("website") or {}, b.get("website") or {}
        row(f"website tree ({sa.get('fileCount')} / {sb.get('fileCount')} files)", sa.get("treeSha256"), sb.get("treeSha256"))
    entries = sum(len(p["entries"]) for p in a["packages"].values())
    lines += [
        "",
        f"Package entries compared (content hash and zip timestamp): {entries}.",
        f"Differences: {len(problems)}.",
    ]
    lines += [f"- {p}" for p in problems[:200]]
    return "\n".join(lines) + "\n"


# ----------------------------------------------------------------------------- run

def tool_versions(env: dict) -> dict:
    def out(cmd: list[str]) -> str:
        try:
            return subprocess.check_output(cmd, env=env, text=True, stderr=subprocess.STDOUT).strip()
        except (OSError, subprocess.CalledProcessError) as error:
            return f"unavailable: {error}"
    return {"dotnet": out(["dotnet", "--version"]), "node": out(["node", "--version"]),
            "npm": out(["npm", "--version"]), "python": sys.version.split()[0]}


def run_proof(args: argparse.Namespace) -> int:
    work = Path(args.workdir).resolve()
    if work.exists() and any(work.iterdir()):
        sys.exit(f"{work} is not empty; use a fresh directory")
    evidence = Path(args.evidence).resolve() if args.evidence else work / "evidence"
    evidence.mkdir(parents=True, exist_ok=True)
    results = {}
    for build in BUILDS:
        root = work / build["name"]
        tree = work / build["tree"]
        home = root / "home"
        home.mkdir(parents=True, exist_ok=True)
        log = evidence / f"{build['name']}.log"
        log.write_text("", encoding="utf-8")
        env = {
            "PATH": os.environ["PATH"],
            "HOME": str(home),
            "TMPDIR": str(root / "tmp"),
            "TZ": build["tz"],
            "LANG": "C.UTF-8",
            "NUGET_PACKAGES": str(home / "nuget-packages"),
            "DOTNET_CLI_HOME": str(home),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "npm_config_cache": str(home / "npm-cache"),
            "NEXT_TELEMETRY_DISABLED": "1",
        }
        (root / "tmp").mkdir(parents=True, exist_ok=True)
        commit = clone(tree, args.ref, args.z3, log=log)
        out = root / "out"
        run(["bash", str(tree / "scripts/reproducible-build.sh"), str(tree), str(out), args.targets], env=env, log=log)
        hashes = hash_build(out)
        hashes.update({"build": build["name"], "commit": commit, "tree": str(tree), "timeZone": build["tz"],
                       "toolchain": tool_versions(env)})
        (evidence / f"{build['name']}.hashes.json").write_text(json.dumps(hashes, indent=1) + "\n", encoding="utf-8")
        results[build["name"]] = hashes
    a, b = results["build-1"], results["build-2"]
    problems = compare(a, b)
    (evidence / "comparison.md").write_text(markdown(a, b, ("build-1", "build-2"), problems), encoding="utf-8")
    print(f"{len(problems)} differences; see {evidence / 'comparison.md'}")
    return 1 if problems else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("clone")
    p.add_argument("dest")
    p.add_argument("--ref", default="HEAD")
    p.add_argument("--z3", choices=("download", "copy"), default="copy")
    p = sub.add_parser("hash")
    p.add_argument("out")
    p.add_argument("--json", required=True)
    p = sub.add_parser("compare")
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("--markdown")
    p = sub.add_parser("run")
    p.add_argument("--workdir", required=True)
    p.add_argument("--evidence")
    p.add_argument("--ref", default="HEAD")
    p.add_argument("--targets", choices=("all", "packages", "website"), default="all")
    p.add_argument("--z3", choices=("download", "copy"), default="download")
    args = parser.parse_args()

    if args.command == "clone":
        print(clone(Path(args.dest).resolve(), args.ref, args.z3))
        return 0
    if args.command == "hash":
        Path(args.json).write_text(json.dumps(hash_build(Path(args.out)), indent=1) + "\n", encoding="utf-8")
        return 0
    if args.command == "compare":
        a = json.loads(Path(args.a).read_text(encoding="utf-8"))
        b = json.loads(Path(args.b).read_text(encoding="utf-8"))
        problems = compare(a, b)
        if args.markdown:
            Path(args.markdown).write_text(markdown(a, b, ("A", "B"), problems), encoding="utf-8")
        for problem in problems[:200]:
            print(problem)
        print(f"{len(problems)} differences")
        return 1 if problems else 0
    return run_proof(args)


if __name__ == "__main__":
    sys.exit(main())
