#!/usr/bin/env python3
"""
byte_preservation_check.py — RFC §5.7.3 byte-preservation verifier.

Given an original `.calr` file and a `migration.log.json` produced by
the migrator, this script verifies the migrator only removed the byte
ranges recorded in the log — everything outside those ranges is
byte-identical to the original.

Without a log (Phase 0 self-test), the script verifies the trivial
identity: file == file.

Exit codes:
    0  byte-preservation holds
    1  byte-preservation violated
    2  bad arguments / file missing

Usage:
    python3 scripts/byte_preservation_check.py \\
        <original.calr> <migrated.calr> [--log <log.json>]
    python3 scripts/byte_preservation_check.py --self-test
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path


def reconstruct_from_log(
    migrated_bytes: bytes,
    log: list[dict],
) -> bytes:
    """Reverse the migrator: re-insert each removed byte range.

    Log schema (per RFC §5.7):
        [
          {
            "file": "<rel path>",
            "removed_offset": <int byte offset in ORIGINAL>,
            "removed_length": <int bytes removed>,
            "removed_bytes_base64": "<base64 of removed bytes>"
          },
          ...
        ]
    Entries MUST be sorted by removed_offset ASCENDING.
    """
    import base64

    out = bytearray()
    pos_in_migrated = 0
    last_orig_offset = 0
    for entry in log:
        offset = int(entry["removed_offset"])
        length = int(entry["removed_length"])
        removed = base64.b64decode(entry["removed_bytes_base64"])
        assert len(removed) == length, f"log entry length mismatch: {entry}"
        chunk_len = offset - last_orig_offset
        # Copy the surviving bytes from migrated.
        out += migrated_bytes[pos_in_migrated:pos_in_migrated + chunk_len]
        pos_in_migrated += chunk_len
        out += removed
        last_orig_offset = offset + length
    # Trailing bytes.
    out += migrated_bytes[pos_in_migrated:]
    return bytes(out)


def check_pair(
    original_path: Path,
    migrated_path: Path,
    log_path: Path | None,
) -> int:
    if not original_path.is_file():
        print(f"byte_preservation: original missing: {original_path}",
              file=sys.stderr)
        return 2
    if not migrated_path.is_file():
        print(f"byte_preservation: migrated missing: {migrated_path}",
              file=sys.stderr)
        return 2

    original = original_path.read_bytes()
    migrated = migrated_path.read_bytes()

    if log_path is None:
        if original != migrated:
            print(
                f"byte_preservation FAIL (no log given, but files differ): "
                f"{original_path}",
                file=sys.stderr,
            )
            return 1
        print(f"byte_preservation OK (identity): {original_path}")
        return 0

    if not log_path.is_file():
        print(f"byte_preservation: log missing: {log_path}", file=sys.stderr)
        return 2

    log_data = json.loads(log_path.read_text(encoding="utf-8"))
    entries = log_data.get("entries", log_data) if isinstance(log_data, dict) else log_data
    # Filter to the entries pertaining to this file.
    rel = str(original_path)
    matching = [
        e for e in entries
        if Path(str(e.get("file", ""))).name == original_path.name
        or str(e.get("file", "")) == rel
    ]
    matching.sort(key=lambda e: int(e["removed_offset"]))
    reconstructed = reconstruct_from_log(migrated, matching)
    if reconstructed != original:
        print(
            f"byte_preservation FAIL: reconstruction != original for "
            f"{original_path} (got {len(reconstructed)} bytes, expected "
            f"{len(original)} bytes)",
            file=sys.stderr,
        )
        return 1
    print(f"byte_preservation OK: {original_path}")
    return 0


def self_test() -> int:
    """T-5.7-a..e from RFC §5.7.6 — synthetic positive/negative cases."""
    import base64
    import tempfile

    failures = 0

    with tempfile.TemporaryDirectory() as td:
        td_path = Path(td)
        # T-5.7-a: trivial identity (no migration applied).
        orig_a = td_path / "a.calr"
        orig_a.write_bytes(b"\xc2\xa7M{m_abc:Foo}\nbody\n\xc2\xa7/M{m_abc}\n")
        if check_pair(orig_a, orig_a, None) != 0:
            print("T-5.7-a FAIL", file=sys.stderr)
            failures += 1
        else:
            print("T-5.7-a PASS (identity)")

        # T-5.7-b: a single removed range, faithfully reconstructible.
        orig_b = td_path / "b.calr"
        orig_b.write_bytes(b"\xc2\xa7M{m_xyz:Foo}\nbody\n\xc2\xa7/M{m_xyz}\n")
        migrated_b = td_path / "b.migrated.calr"
        # Remove the "{m_xyz:Foo}" block. § is 2 UTF-8 bytes (\xc2\xa7) +
        # 'M' = 3 bytes prefix, so the {...} block starts at offset 3 and
        # is 12 bytes long ("{m_xyz:Foo}").
        orig_bytes = orig_b.read_bytes()
        block_start = orig_bytes.index(b"{m_xyz:Foo}")
        block_len = len(b"{m_xyz:Foo}")
        removed = orig_bytes[block_start:block_start + block_len]
        migrated_b.write_bytes(
            orig_bytes[:block_start] + orig_bytes[block_start + block_len:]
        )
        log_b = td_path / "b.log.json"
        log_b.write_text(json.dumps({
            "entries": [{
                "file": "b.calr",
                "removed_offset": block_start,
                "removed_length": block_len,
                "removed_bytes_base64": base64.b64encode(removed).decode(),
            }],
        }), encoding="utf-8")
        if check_pair(orig_b, migrated_b, log_b) != 0:
            print("T-5.7-b FAIL", file=sys.stderr)
            failures += 1
        else:
            print("T-5.7-b PASS (single range)")

        # T-5.7-c: multiple disjoint ranges.
        orig_c = td_path / "c.calr"
        orig_c.write_bytes(
            b"\xc2\xa7F{f1:Foo:pub}\n  body\n\xc2\xa7/F{f1}\n"
            b"\xc2\xa7F{f2:Bar:pub}\n  body2\n\xc2\xa7/F{f2}\n"
        )
        migrated_c = td_path / "c.migrated.calr"
        ob = orig_c.read_bytes()
        # Two removed ranges: {f1:Foo:pub} (offset 2..14, len 12), {f1} (offset
        # of "{f1}" inside §/F{f1}), {f2:Bar:pub}, {f2}. Compute by find().
        ranges = []
        for marker in [b"{f1:Foo:pub}", b"{f1}", b"{f2:Bar:pub}", b"{f2}"]:
            off = ob.find(marker)
            ranges.append((off, len(marker)))
        ranges.sort()
        # Build migrated (in reverse to preserve offsets while editing).
        migrated_bytes = ob
        for off, length in reversed(ranges):
            migrated_bytes = migrated_bytes[:off] + migrated_bytes[off + length:]
        migrated_c.write_bytes(migrated_bytes)
        log_c = td_path / "c.log.json"
        log_c.write_text(json.dumps({
            "entries": [
                {
                    "file": "c.calr",
                    "removed_offset": off,
                    "removed_length": length,
                    "removed_bytes_base64": base64.b64encode(
                        ob[off:off + length]
                    ).decode(),
                }
                for off, length in ranges
            ],
        }), encoding="utf-8")
        if check_pair(orig_c, migrated_c, log_c) != 0:
            print("T-5.7-c FAIL", file=sys.stderr)
            failures += 1
        else:
            print("T-5.7-c PASS (multiple ranges)")

        # T-5.7-d: NEGATIVE — migrated file differs OUTSIDE the recorded ranges.
        # Reuse case b's setup but corrupt the migrated file post-hoc.
        migrated_d = td_path / "d.migrated.calr"
        migrated_d.write_bytes(migrated_b.read_bytes() + b"EXTRA\n")
        rc = check_pair(orig_b, migrated_d, log_b)
        if rc == 0:
            print("T-5.7-d FAIL — corruption not detected", file=sys.stderr)
            failures += 1
        else:
            print("T-5.7-d PASS (corruption detected)")

        # T-5.7-e: NEGATIVE — log claims a wrong byte range.
        log_e = td_path / "e.log.json"
        log_e.write_text(json.dumps({
            "entries": [{
                "file": "b.calr",
                "removed_offset": block_start,
                "removed_length": block_len,
                "removed_bytes_base64": base64.b64encode(b"X" * block_len).decode(),
            }],
        }), encoding="utf-8")
        rc = check_pair(orig_b, migrated_b, log_e)
        if rc == 0:
            print("T-5.7-e FAIL — wrong-bytes log not detected",
                  file=sys.stderr)
            failures += 1
        else:
            print("T-5.7-e PASS (wrong-bytes log detected)")

    if failures:
        print(f"\nbyte_preservation self-test: {failures} failure(s)",
              file=sys.stderr)
        return 1
    print("\nbyte_preservation self-test: all 5 cases PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("original", nargs="?", help="Original .calr path.")
    p.add_argument("migrated", nargs="?", help="Migrated .calr path.")
    p.add_argument("--log", default=None,
                   help="Path to migration.log.json (optional).")
    p.add_argument("--self-test", action="store_true",
                   help="Run T-5.7-a..e synthetic checks.")
    args = p.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.original or not args.migrated:
        p.error("original and migrated are required (or use --self-test)")
    return check_pair(
        Path(args.original),
        Path(args.migrated),
        Path(args.log) if args.log else None,
    )


if __name__ == "__main__":
    sys.exit(main())
