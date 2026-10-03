#!/usr/bin/env python3
"""Verify the Z3 assets inside a packed .nupkg against the committed pins (#1420).

Packages are Z3 consumers too: the calor tool carries Z3 under
tools/net10.0/any and Calor.Sdk under tasks/net10.0. This check ties the bytes
a package ships to .github/z3-binaries-4.15.7.sha256, through the RID set in
eng/z3-consumers.json:

  * the managed wrapper and each required RID's native are present and match
    their pinned SHA-256 and size;
  * every libz3 entry in the package sits at a supported RID's
    runtimes/<rid>/native path and matches its pin, so an unsupported RID
    (osx-x64, win-x86) or an output-root copy cannot ride along.

Usage:
  check-packaged-z3.py <nupkg> --prefix tools/net10.0/any --rid osx-arm64
  check-packaged-z3.py <nupkg> --prefix tasks/net10.0 --all-rids
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
import zipfile


REPO_ROOT = Path(__file__).resolve().parent.parent


def load_registry(repo_root: Path) -> tuple[dict, dict[str, tuple[str, int]]]:
    registry = json.loads((repo_root / "eng/z3-consumers.json").read_text(encoding="utf-8"))
    pins: dict[str, tuple[str, int]] = {}
    pin_path = repo_root / registry["pins"]["assets"]
    for raw in pin_path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        digest, name, size = line.split()
        pins[name] = (digest.lower(), int(size))
    return registry, pins


def check(nupkg: Path, prefix: str, rids: list[str] | None, repo_root: Path) -> list[str]:
    registry, pins = load_registry(repo_root)
    supported = {entry["rid"]: entry for entry in registry["supportedRids"]}
    required_rids = list(supported) if rids is None else rids
    errors: list[str] = []
    for rid in required_rids:
        if rid not in supported:
            errors.append(f"{rid} is not a supported Z3 RID in eng/z3-consumers.json")
    if errors:
        return errors

    prefix = prefix.rstrip("/")
    allowed = {
        f"{prefix}/runtimes/{rid}/native/{entry['native']}": entry["asset"]
        for rid, entry in supported.items()
    }
    managed = f"{prefix}/Microsoft.Z3.dll"
    required = {managed: registry["managed"]["asset"]}
    for rid in required_rids:
        native = f"{prefix}/runtimes/{rid}/native/{supported[rid]['native']}"
        required[native] = supported[rid]["asset"]

    with zipfile.ZipFile(nupkg) as package:
        names = set(package.namelist())

        def matches_pin(entry: str, asset: str) -> bool:
            digest, size = pins[asset]
            data = package.read(entry)
            return len(data) == size and hashlib.sha256(data).hexdigest() == digest

        for entry, asset in sorted(required.items()):
            if entry not in names:
                errors.append(f"missing {entry}")
            elif not matches_pin(entry, asset):
                errors.append(f"{entry} does not match pinned asset {asset}")

        for entry in sorted(names):
            if not entry.rsplit("/", 1)[-1].lower().startswith("libz3"):
                continue
            if entry not in allowed:
                errors.append(f"unexpected Z3 native {entry} (not a supported RID path)")
            elif entry not in required and not matches_pin(entry, allowed[entry]):
                errors.append(f"{entry} does not match pinned asset {allowed[entry]}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("nupkg", type=Path)
    parser.add_argument("--prefix", required=True)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--rid", action="append")
    selection.add_argument("--all-rids", action="store_true")
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    args = parser.parse_args()

    errors = check(args.nupkg, args.prefix, None if args.all_rids else args.rid, args.repo_root)
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        return 1
    print(f"OK: Z3 assets in {args.nupkg.name} match the committed pins.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
