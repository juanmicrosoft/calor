#!/usr/bin/env python3
"""Verify an explicit PP-W registration supersession, never generate one.

Old values are read from the preserved A-1.12 ledger, whose own C# tests
recompute and pin it. Replacement values are checked against frozen task
bytes. This tool supplies no task selection, sample size or approval.
"""
import hashlib
import json
from pathlib import Path

BENCH = Path(__file__).resolve().parent


def historical_pins():
    ledger = json.loads((BENCH / "effect-rows-benefit-ledger.json").read_text(encoding="utf-8"))
    return {
        "registration": ledger["registration"],
        "starterFreezeCommit": ledger["starterFreezeCommit"],
        "arms": ledger["arms"],
        "pairCounts": {"tasks": len(ledger["pairs"]),
                       "blind": len(ledger["registeredBlindPairs"]),
                       "warningVsError": len(ledger["registeredWarningVsErrorPairs"]),
                       "legB": len(ledger["registeredLegBPairs"])},
        "starterBlobs": {pair["id"]: pair["starterBlobs"] for pair in ledger["pairs"]},
    }


def blob_sha(path):
    content = Path(path).read_bytes()
    return hashlib.sha1(b"blob " + str(len(content)).encode("ascii") + b"\0" + content).hexdigest()


def check_supersession(registration, tasks_root):
    """Require exact old/new pin tables with a cause; do not rewrite history."""
    def require(condition, reason):
        if not condition:
            raise ValueError("registration supersession: " + reason)

    require(registration.get("supersedes") == "A-1.12" and registration.get("cause"),
            "written cause and explicit A-1.12 predecessor required")
    require(registration.get("supersededPins") == historical_pins(),
            "old starter slots, pair counts or arm identities were omitted/rewritten")
    replacement = registration.get("replacementPins")
    require(isinstance(replacement, dict), "replacement pin table required after actual task freeze")
    require(set(replacement) == {"compilerCommit", "policies", "pairCounts", "starterBlobs"},
            "replacement must record all four pin groups explicitly")
    require(replacement["compilerCommit"] == registration["compilerCommit"],
            "replacement compiler differs from shared compiler registration")
    require(replacement["policies"] == {"A": ["--permissive-effects"], "B": []},
            "replacement arms must use the same compiler and only the policy flag differs")
    tasks = registration["tasks"]
    root = Path(tasks_root).resolve()
    expected_blobs = []
    counts = {"tasks": len(tasks), "blind": 0, "warningVsError": 0, "legB": 0}
    for task in tasks:
        directory = root / task
        require(directory.resolve().is_relative_to(root) and not directory.is_symlink(),
                "task path escapes frozen root")
        pair = json.loads((directory / "pair.json").read_text(encoding="utf-8"))
        require(pair.get("class") in ("blind", "warning-vs-error"),
                "each frozen task must state its cell class")
        require(type(pair.get("legB")) is bool, "each frozen task must state leg-B membership")
        counts["blind" if pair["class"] == "blind" else "warningVsError"] += 1
        counts["legB"] += int(pair["legB"])
        for arm in ("A", "B"):
            fixture = directory / ("starter-" + arm.lower())
            sources = sorted(path for path in fixture.rglob("*")
                             if path.is_file() and str(path).endswith((".calr", ".calr.inc")))
            require(sources, "missing starter source for %s/%s" % (task, arm))
            for source in sources:
                require(not source.is_symlink() and source.resolve().is_relative_to(root),
                        "starter path escapes frozen root")
                expected_blobs.append({"task": task, "arm": arm,
                                       "path": source.relative_to(root).as_posix(),
                                       "blobSha": blob_sha(source)})
    require(isinstance(replacement["pairCounts"], dict)
            and all(type(value) is int for value in replacement["pairCounts"].values())
            and replacement["pairCounts"] == counts,
            "new pair counts differ from frozen task classes")
    require(replacement["starterBlobs"] == expected_blobs,
            "new starter git-blob pins differ, are missing, duplicated, or reordered")
    return {"supersedes": "A-1.12", "pairCounts": counts,
            "starterSlots": len(expected_blobs), "compilerCommit": replacement["compilerCommit"]}
