#!/usr/bin/env python3
"""Generate deterministic SPDX SBOM and SLSA-style provenance for release files."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from urllib.parse import quote


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def git_value(repo_root: Path, *args: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(repo_root), *args],
        text=True,
        stderr=subprocess.DEVNULL,
    ).strip()


def source_timestamp(repo_root: Path, commit: str) -> str:
    try:
        return git_value(repo_root, "show", "-s", "--format=%cI", commit)
    except subprocess.CalledProcessError:
        return "1970-01-01T00:00:00Z"


def release_files(paths: list[Path], output: Path) -> list[Path]:
    files: set[Path] = set()
    for path in paths:
        if path.is_file():
            files.add(path.resolve())
        elif path.is_dir():
            files.update(
                candidate.resolve()
                for candidate in path.rglob("*")
                if candidate.is_file() and output.resolve() not in candidate.parents
            )
        else:
            raise FileNotFoundError(path)
    if not files:
        raise ValueError("no release artifacts were found")
    return sorted(files)


def dependencies(repo_root: Path) -> list[tuple[str, str, str]]:
    resolved: set[tuple[str, str, str]] = set()
    for lock_path in sorted(repo_root.rglob("packages.lock.json")):
        relative = lock_path.relative_to(repo_root)
        if relative.parts[:2] == ("bench", "corpus") or (
            relative.parts[:2] == ("bench", "phase0-agent-native")
        ):
            continue
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        for framework in lock.get("dependencies", {}).values():
            for name, details in framework.items():
                version = details.get("resolved")
                if version:
                    resolved.add(("nuget", name, version))
    for lock_path in sorted(repo_root.rglob("package-lock.json")):
        if "node_modules" in lock_path.parts:
            continue
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        for package_path, details in lock.get("packages", {}).items():
            if not package_path or "node_modules/" not in package_path:
                continue
            name = details.get("name") or package_path.rsplit("node_modules/", 1)[-1]
            version = details.get("version")
            if name and version:
                resolved.add(("npm", name, version))
    return sorted(resolved, key=lambda item: (item[0], item[1].lower(), item[2]))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--name", required=True)
    parser.add_argument("--artifacts", type=Path, action="append", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
    )
    parser.add_argument("--repository")
    parser.add_argument("--commit")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    repository = args.repository or git_value(repo_root, "config", "--get", "remote.origin.url")
    commit = args.commit or git_value(repo_root, "rev-parse", "HEAD")
    artifacts = release_files(args.artifacts, output)
    subjects = [
        {
            "name": path.name,
            "digest": {"sha256": sha256(path)},
            "size": path.stat().st_size,
        }
        for path in artifacts
    ]
    packages = dependencies(repo_root)
    file_stem = "".join(
        character if character.isalnum() or character in "-._" else "-"
        for character in args.name
    )

    namespace_name = quote(args.name, safe="")
    sbom = {
        "spdxVersion": "SPDX-2.3",
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": args.name,
        "documentNamespace": f"https://github.com/juanmicrosoft/calor/sbom/{commit}/{namespace_name}",
        "creationInfo": {
            "created": source_timestamp(repo_root, commit),
            "creators": ["Tool: calor/scripts/generate-release-metadata.py"],
        },
        "documentDescribes": [
            *[f"SPDXRef-Artifact-{index}" for index in range(len(subjects))],
            *[f"SPDXRef-Package-{index}" for index in range(len(packages))],
        ],
        "files": [
            {
                "fileName": subject["name"],
                "SPDXID": f"SPDXRef-Artifact-{index}",
                "checksums": [
                    {
                        "algorithm": "SHA256",
                        "checksumValue": subject["digest"]["sha256"],
                    }
                ],
            }
            for index, subject in enumerate(subjects)
        ],
        "packages": [
            {
                "name": name,
                "SPDXID": f"SPDXRef-Package-{index}",
                "versionInfo": version,
                "downloadLocation": "NOASSERTION",
                "filesAnalyzed": False,
                "externalRefs": [
                    {
                        "referenceCategory": "PACKAGE-MANAGER",
                        "referenceType": "purl",
                        "referenceLocator": f"pkg:{ecosystem}/{quote(name, safe='')}@{quote(version, safe='')}",
                    }
                ],
            }
            for index, (ecosystem, name, version) in enumerate(packages)
        ],
    }
    provenance = {
        "_type": "https://in-toto.io/Statement/v1",
        "subject": subjects,
        "predicateType": "https://slsa.dev/provenance/v1",
        "predicate": {
            "buildDefinition": {
                "buildType": "https://github.com/juanmicrosoft/calor/release-workflow/v1",
                "externalParameters": {
                    "repository": repository,
                    "commit": commit,
                    "artifactSet": args.name,
                },
                "resolvedDependencies": [
                    {
                        "uri": f"pkg:{ecosystem}/{quote(name, safe='')}@{quote(version, safe='')}",
                    }
                    for ecosystem, name, version in packages
                ],
            },
            "runDetails": {
                "builder": {"id": "https://github.com/juanmicrosoft/calor/actions"},
                "metadata": {"invocationId": commit},
            },
        },
    }

    (output / f"{file_stem}.sbom.spdx.json").write_text(
        json.dumps(sbom, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    (output / f"{file_stem}.provenance.json").write_text(
        json.dumps(provenance, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Generated release metadata for {len(subjects)} artifact(s) and {len(packages)} package(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
