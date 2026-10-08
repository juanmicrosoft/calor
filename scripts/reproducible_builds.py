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
  compare A.json B.json --targets all|packages|website [--markdown FILE]
        Exit 1 unless both outputs hold everything the target publishes and every hash
        in A equals the one in B (no file missing on either side).
  run --workdir DIR [--evidence DIR] [--targets all|packages|website] [--z3 download|copy]
        The full proof: two fresh clones at different paths, each built with its own
        HOME, NuGet cache, npm cache, time zone, locale and ambient timestamp variables,
        then hash and compare.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_release_adjudication import tree_digest  # noqa: E402  (the gate's own digest)

BUILD_SCRIPT = REPO_ROOT / "scripts/reproducible-build.sh"
Z3_REGISTRY = REPO_ROOT / "eng/z3-consumers.json"
SCHEMA = "calor/reproducible-build-hashes/2"

# Two deliberately different environments for the two builds. Paths differ in depth
# and name; the time zone, locale and the timestamp variables NuGet and the SDK know
# about differ, so a wall-clock, local-time, locale or ambient-epoch leak shows up.
BUILDS = (
    {"name": "build-1", "tree": "a/calor", "env": {"TZ": "UTC", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8"}},
    {"name": "build-2", "tree": "second-build/nested/elsewhere/calor-clone",
     "env": {"TZ": "Pacific/Kiritimati", "LANG": "tr_TR.UTF-8", "LC_ALL": "tr_TR.UTF-8",
             "SOURCE_DATE_EPOCH": "1700000000", "DeterministicTimestamp": "2025-02-03T00:00:00Z"}},
)

# What each target must produce. An empty, partial or hollow output is a failure, not a
# match. (reproducible-build.sh also runs the publish job's own package inspections.)
EXPECTED = {
    "packages": {
        "packages": {
            "calor.": ("calor.nuspec", "tools/net10.0/any/calor.dll", "tools/net10.0/any/DotnetToolSettings.xml",
                       "tools/net10.0/any/Microsoft.Z3.dll"),
            "Calor.Sdk.": ("Calor.Sdk.nuspec", "Sdk/Sdk.props", "Sdk/Sdk.targets", "tasks/net10.0/Calor.Tasks.dll",
                           "tasks/net10.0/calor.dll", "tasks/net10.0/Microsoft.Z3.dll"),
        },
        "releaseMetadata": (".provenance.json", ".sbom.spdx.json"),
    },
    "website": {"websiteFiles": ("index.html", "404.html", "sitemap.xml", "search-index.json")},
}
# Assets index.html loads; every one must be in the tree (a site without its scripts is hollow).
NEXT_ASSET = re.compile(r'(?:src|href)="[^"]*?/(_next/static/[^"?#]+)"')


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
        copy_z3_assets(REPO_ROOT, dest)
    run([sys.executable, str(dest / "scripts/verify-z3-assets.py"), "--write-provenance"], cwd=dest, log=log)
    return commit


def z3_asset_paths() -> list[str]:
    """The registered (eng/z3-consumers.json) and pinned Z3 assets, nothing else."""
    registry = json.loads(Z3_REGISTRY.read_text(encoding="utf-8"))
    return [registry["managed"]["path"], *(entry["path"] for entry in registry["supportedRids"])]


def copy_z3_assets(source_root: Path, dest: Path) -> None:
    # Only the registered, pinned files: never whole ignored directories, which could
    # carry unverified files (a stray .cs file there would even be compiled).
    for rel in z3_asset_paths():
        source = source_root / rel
        if not source.is_file():
            sys.exit(f"--z3 copy needs verified assets ({source} is missing); bootstrap this checkout first")
        (dest / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest / rel)


# ---------------------------------------------------------------------------- hash

def hash_build(out: Path, toolchain: dict | None = None) -> dict:
    result: dict = {"schema": SCHEMA, "packages": {}, "releaseMetadata": {}, "website": None,
                    "toolchain": toolchain or {}}
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
        index = site / "index.html"
        assets = sorted(set(NEXT_ASSET.findall(index.read_text(encoding="utf-8", errors="replace")))) if index.is_file() else []
        result["website"] = {"treeSha256": tree_digest(site), "fileCount": len(files),
                             "indexAssets": assets, "files": dict(sorted(files.items()))}
    return result


# ------------------------------------------------------------------------- compare

def flatten(hashes: dict, target: str = "all") -> dict[str, str]:
    """Every compared value of the target under a stable key. Zip timestamps count: they are bytes."""
    flat: dict[str, str] = {}
    if target == "website":
        hashes = {"website": hashes.get("website")}
    elif target == "packages":
        hashes = {k: v for k, v in hashes.items() if k != "website"}
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


def inventory_problems(hashes: dict, target: str, label: str) -> list[str]:
    """An output that lacks what the target publishes cannot count as reproduced."""
    problems = []
    targets = ("packages", "website") if target == "all" else (target,)
    if "packages" in targets:
        names = list(hashes.get("packages", {}))
        for prefix, required in EXPECTED["packages"]["packages"].items():
            matches = [n for n in names if n.startswith(prefix) and n.endswith(".nupkg")]
            if len(matches) != 1:
                problems.append(f"{label}: expected exactly one {prefix}*.nupkg, got {sorted(names)}")
                continue
            entries = hashes["packages"][matches[0]].get("entries") or {}
            for entry in required:
                if entry not in entries:
                    problems.append(f"{label}: {matches[0]} has no {entry}")
        metadata = list(hashes.get("releaseMetadata", {}))
        for suffix in EXPECTED["packages"]["releaseMetadata"]:
            if len([n for n in metadata if n.endswith(suffix)]) != 1:
                problems.append(f"{label}: expected exactly one *{suffix} release-metadata file, got {sorted(metadata)}")
    if "website" in targets:
        site = hashes.get("website") or {}
        files = site.get("files") or {}
        if not files:
            problems.append(f"{label}: the website output is missing or empty")
        for required in EXPECTED["website"]["websiteFiles"]:
            if files and required not in files:
                problems.append(f"{label}: the website output has no {required}")
        assets = site.get("indexAssets") or []
        if files and not any(a.endswith(".js") for a in assets):
            problems.append(f"{label}: index.html loads no _next/static script")
        for asset in assets:
            if asset not in files:
                problems.append(f"{label}: index.html loads {asset}, which the output lacks")
    return problems


def toolchain_notes(a: dict, b: dict) -> list[str]:
    ta, tb = a.get("toolchain") or {}, b.get("toolchain") or {}
    return [f"toolchain differs: {k}: {ta.get(k)} != {tb.get(k)}"
            for k in sorted(set(ta) | set(tb)) if ta.get(k) != tb.get(k)]


def compare(a: dict, b: dict, target: str = "all") -> list[str]:
    problems = inventory_problems(a, target, "A") + inventory_problems(b, target, "B")
    fa, fb = flatten(a, target), flatten(b, target)
    for key in sorted(set(fa) | set(fb)):
        if key not in fa:
            problems.append(f"only in B: {key}")
        elif key not in fb:
            problems.append(f"only in A: {key}")
        elif fa[key] != fb[key]:
            problems.append(f"differs: {key}: {fa[key]} != {fb[key]}")
    return problems


def markdown(a: dict, b: dict, labels: tuple[str, str], problems: list[str], target: str = "all") -> str:
    lines = [
        f"| Published file | {labels[0]} SHA-256 | {labels[1]} SHA-256 | Same |",
        "|---|---|---|---|",
    ]

    def row(name: str, x: str | None, y: str | None) -> None:
        lines.append(f"| `{name}` | `{x or 'missing'}` | `{y or 'missing'}` | {'yes' if x and x == y else '**no**'} |")

    if target != "website":
        for name in sorted(set(a["packages"]) | set(b["packages"])):
            row(name, a["packages"].get(name, {}).get("sha256"), b["packages"].get(name, {}).get("sha256"))
        for name in sorted(set(a["releaseMetadata"]) | set(b["releaseMetadata"])):
            row(name, a["releaseMetadata"].get(name), b["releaseMetadata"].get(name))
    if target != "packages" and (a.get("website") or b.get("website")):
        sa, sb = a.get("website") or {}, b.get("website") or {}
        row(f"website tree ({sa.get('fileCount')} / {sb.get('fileCount')} files)", sa.get("treeSha256"), sb.get("treeSha256"))
    entries = 0 if target == "website" else sum(len(p["entries"]) for p in a["packages"].values())
    lines += [
        "",
        f"Package entries compared (content hash and zip timestamp): {entries}.",
        f"Differences: {len(problems)}.",
    ]
    lines += [f"- {p}" for p in problems[:200]]
    notes = toolchain_notes(a, b)
    lines += ["", "Toolchains:", f"- {labels[0]}: {json.dumps(a.get('toolchain') or {}, sort_keys=True)}",
              f"- {labels[1]}: {json.dumps(b.get('toolchain') or {}, sort_keys=True)}"]
    lines += [f"- note: {n}" for n in notes]
    return "\n".join(lines) + "\n"


# ----------------------------------------------------------------------------- run

def tool_versions(env: dict, cwd: Path | None = None) -> dict:
    """The toolchain decides the bytes: record it next to every hash set."""
    def out(cmd: list[str]) -> str:
        try:
            return subprocess.check_output(cmd, env=env, cwd=cwd, text=True, stderr=subprocess.STDOUT).strip()
        except (OSError, subprocess.CalledProcessError) as error:
            return f"unavailable: {error}"
    dotnet = out(["dotnet", "--version"])
    return {"os": f"{platform.system()} {platform.machine()}", "dotnet": dotnet,
            "dotnetSdkBuild": sdk_fingerprint(out(["dotnet", "--list-sdks"]), dotnet),
            "node": out(["node", "--version"]), "npm": out(["npm", "--version"])}


# The version number does not identify the SDK build: Homebrew's source-built 10.0.401
# and Microsoft's 10.0.401 produce different package bytes (ci-comparison.md). These
# files carry that difference (the compiler and NuGet's packer).
SDK_FINGERPRINT_FILES = ("Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll", "NuGet.Packaging.dll")


def sdk_fingerprint(list_sdks: str, version: str) -> str:
    """sha256 over the SDK's compiler and NuGet packer assemblies, or why it is unknown."""
    for line in list_sdks.splitlines():
        parts = line.strip().split(" [", 1)
        if len(parts) == 2 and parts[0] == version:
            sdk = Path(parts[1].rstrip("]")) / version
            digest = hashlib.sha256()
            for rel in SDK_FINGERPRINT_FILES:
                path = sdk / rel
                if not path.is_file():
                    return f"unknown: {path} missing"
                digest.update(f"{rel}\0{sha256_file(path)}\n".encode())
            return f"sha256:{digest.hexdigest()}"
    return f"unknown: SDK {version} not listed"


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
            **build["env"],
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
        hashes = hash_build(out, tool_versions(env, cwd=tree))
        hashes.update({"build": build["name"], "commit": commit, "tree": str(tree), "environment": build["env"]})
        (evidence / f"{build['name']}.hashes.json").write_text(json.dumps(hashes, indent=1) + "\n", encoding="utf-8")
        results[build["name"]] = hashes
    a, b = results["build-1"], results["build-2"]
    problems = compare(a, b, args.targets)
    (evidence / "comparison.md").write_text(markdown(a, b, ("build-1", "build-2"), problems, args.targets), encoding="utf-8")
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
    p.add_argument("--targets", choices=("all", "packages", "website"), required=True,
                   help="what both outputs must contain; an empty or partial output fails")
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
        hashes = hash_build(Path(args.out), tool_versions(dict(os.environ)))
        Path(args.json).write_text(json.dumps(hashes, indent=1) + "\n", encoding="utf-8")
        return 0
    if args.command == "compare":
        a = json.loads(Path(args.a).read_text(encoding="utf-8"))
        b = json.loads(Path(args.b).read_text(encoding="utf-8"))
        problems = compare(a, b, args.targets)
        for note in toolchain_notes(a, b):
            print(f"note: {note}")
        if args.markdown:
            Path(args.markdown).write_text(markdown(a, b, ("A", "B"), problems, args.targets), encoding="utf-8")
        for problem in problems[:200]:
            print(problem)
        print(f"{len(problems)} differences")
        return 1 if problems else 0
    return run_proof(args)


if __name__ == "__main__":
    sys.exit(main())
