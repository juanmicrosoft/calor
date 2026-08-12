#!/usr/bin/env python3
"""Verify the complete local Z3 closure against committed size and hash pins."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys


ASSET_PATHS = {
    "Microsoft.Z3.dll": "src/Calor.Compiler/z3/Microsoft.Z3.dll",
    "libz3-linux-arm64.so": "src/Calor.Compiler/runtimes/linux-arm64/native/libz3.so",
    "libz3-linux-x64.so": "src/Calor.Compiler/runtimes/linux-x64/native/libz3.so",
    "libz3-osx-arm64.dylib": "src/Calor.Compiler/runtimes/osx-arm64/native/libz3.dylib",
    "libz3-win-arm64.dll": "src/Calor.Compiler/runtimes/win-arm64/native/libz3.dll",
    "libz3-win-x64.dll": "src/Calor.Compiler/runtimes/win-x64/native/libz3.dll",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_manifest(path: Path) -> dict[str, tuple[str, int]]:
    entries: dict[str, tuple[str, int]] = {}
    for line_number, raw_line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split()
        if len(parts) != 3 or len(parts[0]) != 64 or not parts[2].isdigit():
            raise ValueError(f"{path}:{line_number}: expected '<sha256> <name> <size>'")
        entries[parts[1]] = (parts[0].lower(), int(parts[2]))
    return entries


def verify(repo_root: Path, write_provenance: bool) -> None:
    manifest_path = repo_root / ".github/z3-binaries-4.15.7.sha256"
    upstream_path = repo_root / "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256"
    manifest = read_manifest(manifest_path)
    if not set(ASSET_PATHS).issubset(manifest):
        missing = sorted(set(ASSET_PATHS) - set(manifest))
        raise ValueError(f"Z3 asset manifest is missing required outputs: {missing}")

    verified: list[dict[str, object]] = []
    for name, relative_path in ASSET_PATHS.items():
        path = repo_root / relative_path
        if not path.is_file():
            raise FileNotFoundError(
                f"missing Z3 asset {relative_path}; run "
                "src/Calor.Compiler/scripts/download-z3.sh (or .ps1) explicitly"
            )
        expected_hash, expected_size = manifest[name]
        actual_size = path.stat().st_size
        if actual_size != expected_size:
            raise ValueError(
                f"unexpected size for {relative_path}: "
                f"expected {expected_size}, got {actual_size}"
            )
        actual_hash = sha256(path)
        if actual_hash != expected_hash:
            raise ValueError(
                f"checksum mismatch for {relative_path}: "
                f"expected {expected_hash}, got {actual_hash}"
            )
        verified.append(
            {
                "name": name,
                "path": relative_path,
                "sha256": actual_hash,
                "size": actual_size,
            }
        )

    if write_provenance:
        provenance_path = repo_root / "src/Calor.Compiler/z3/.provenance.json"
        provenance = {
            "schemaVersion": 1,
            "z3Version": "4.15.7",
            "source": "https://github.com/Z3Prover/z3/releases/tag/z3-4.15.7",
            "upstreamManifest": {
                "path": str(upstream_path.relative_to(repo_root)),
                "sha256": sha256(upstream_path),
            },
            "assetManifest": {
                "path": str(manifest_path.relative_to(repo_root)),
                "sha256": sha256(manifest_path),
            },
            "assets": verified,
        }
        provenance_path.write_text(
            json.dumps(provenance, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
    )
    parser.add_argument("--write-provenance", action="store_true")
    args = parser.parse_args()
    try:
        verify(args.repo_root.resolve(), args.write_provenance)
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("Verified Z3 managed and native assets against committed size/hash pins.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
