#!/usr/bin/env python3
"""Warn on suspicious .approved.calr snapshot diffs.

Snapshot tests under ``tests/TestData/`` (and adjacent
``tests/*/Snapshots/``) use byte-equal comparison against 768
``.approved.calr`` golden files. A bulk snapshot update via
``CALOR_UPDATE_SNAPSHOTS=1`` could silently degrade fidelity by:

* dropping comments (``// ...`` lines),
* removing ``§CSHARP`` interop blocks, or
* shrinking the raw C# inside those blocks.

This script computes cheap heuristics per changed snapshot, compares
the ``old`` and ``new`` blobs across a git ref range, and emits a
Markdown table of warnings suitable for posting as a PR comment. It
NEVER fails the CI job — the human reviewer still decides.

Run the built-in unit tests with::

    python3 -m unittest scripts.check_snapshot_diffs
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import unittest
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


# Regex for a comment line: leading whitespace then ``//`` (Calor and C#
# style single-line comment marker used inside the snapshots).
COMMENT_RE = re.compile(r"^\s*//")

# Regex for the paired ``§CSHARP{...}§/CSHARP`` block. The interop body
# can contain newlines and (importantly) balanced ``{...}`` — but there
# are no known snapshots with nested unbalanced braces inside the
# interop, so a non-greedy match up to the closing tag is sufficient
# for the heuristic. This does NOT need to be a real parser.
CSHARP_BLOCK_RE = re.compile(
    r"§CSHARP\{(?P<body>.*?)\}§/CSHARP", re.DOTALL
)

# ``§CSHARP`` block starts, whether or not they have a matching closer.
# Used as a fallback count so a truncated closer doesn't inflate the
# apparent drop artificially.
CSHARP_START_RE = re.compile(r"§CSHARP\{")

# Shrink threshold for §CSHARP interop bytes.
INTEROP_SHRINK_THRESHOLD = 0.20


@dataclass(frozen=True)
class SnapshotMetrics:
    """Metrics extracted from a single snapshot blob."""

    comment_count: int
    interop_block_count: int
    interop_body_bytes: int

    @classmethod
    def from_text(cls, text: str) -> "SnapshotMetrics":
        comment_count = sum(1 for line in text.splitlines() if COMMENT_RE.match(line))
        bodies = CSHARP_BLOCK_RE.findall(text)
        # Prefer the paired-block count. If the file has orphan starts
        # (rare — a snapshot in flight) fall back to the wider count
        # so removal of a block still registers.
        paired = len(bodies)
        starts = len(CSHARP_START_RE.findall(text))
        interop_block_count = max(paired, starts)
        interop_body_bytes = sum(len(body) for body in bodies)
        return cls(
            comment_count=comment_count,
            interop_block_count=interop_block_count,
            interop_body_bytes=interop_body_bytes,
        )


@dataclass(frozen=True)
class Warning:
    """A single suspicious change flagged on one snapshot file."""

    path: str
    kind: str
    old_value: int
    new_value: int

    def format_delta(self) -> str:
        delta = self.new_value - self.old_value
        sign = "+" if delta > 0 else ""
        return f"{self.old_value} -> {self.new_value} ({sign}{delta})"


def diff_snapshot(
    path: str, old_text: str, new_text: str
) -> list[Warning]:
    """Return warnings for a single snapshot file's before/after pair."""

    old = SnapshotMetrics.from_text(old_text)
    new = SnapshotMetrics.from_text(new_text)
    warnings: list[Warning] = []

    if new.comment_count < old.comment_count:
        warnings.append(
            Warning(path, "comments dropped", old.comment_count, new.comment_count)
        )

    if new.interop_block_count < old.interop_block_count:
        warnings.append(
            Warning(
                path,
                "§CSHARP blocks removed",
                old.interop_block_count,
                new.interop_block_count,
            )
        )

    if old.interop_body_bytes > 0:
        shrink = (old.interop_body_bytes - new.interop_body_bytes) / old.interop_body_bytes
        if shrink > INTEROP_SHRINK_THRESHOLD:
            warnings.append(
                Warning(
                    path,
                    f"§CSHARP body shrank {shrink * 100:.1f}%",
                    old.interop_body_bytes,
                    new.interop_body_bytes,
                )
            )

    return warnings


def format_report(warnings: Iterable[Warning]) -> str:
    """Render warnings as a Markdown block, or an empty string."""

    warnings = list(warnings)
    if not warnings:
        return ""

    lines = [
        "<!-- calor-snapshot-diff-heuristic -->",
        "### Snapshot fidelity warnings",
        "",
        "The following `.approved.calr` snapshot changes look suspicious.",
        "This is a **soft warning** — the reviewer should confirm the changes",
        "are intentional (e.g., regenerated via `CALOR_UPDATE_SNAPSHOTS=1`).",
        "",
        "| Snapshot | Signal | Before → After |",
        "| --- | --- | --- |",
    ]
    for warning in warnings:
        lines.append(
            f"| `{warning.path}` | {warning.kind} | {warning.format_delta()} |"
        )
    lines.append("")
    return "\n".join(lines)


def _git(*args: str, cwd: Path | None = None) -> str:
    result = subprocess.run(
        ["git", *args],
        check=True,
        capture_output=True,
        text=True,
        cwd=cwd,
    )
    return result.stdout


def _list_changed_snapshots(base: str, head: str, cwd: Path | None = None) -> list[str]:
    output = _git("diff", "--name-only", f"{base}...{head}", cwd=cwd)
    return [
        line.strip()
        for line in output.splitlines()
        if line.strip().endswith(".approved.calr")
    ]


def _blob_at(rev: str, path: str, cwd: Path | None = None) -> str:
    try:
        return _git("show", f"{rev}:{path}", cwd=cwd)
    except subprocess.CalledProcessError:
        # File didn't exist on that side (added or deleted); treat as empty.
        return ""


def scan(base: str, head: str, cwd: Path | None = None) -> list[Warning]:
    """Run the heuristic across every changed snapshot in a git range."""

    warnings: list[Warning] = []
    for path in _list_changed_snapshots(base, head, cwd=cwd):
        old_text = _blob_at(base, path, cwd=cwd)
        new_text = _blob_at(head, path, cwd=cwd)
        warnings.extend(diff_snapshot(path, old_text, new_text))
    return warnings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Warn on suspicious .approved.calr snapshot diffs."
    )
    parser.add_argument(
        "--base",
        default="origin/main",
        help="Git base ref (default: origin/main).",
    )
    parser.add_argument(
        "--head",
        default="HEAD",
        help="Git head ref (default: HEAD).",
    )
    parser.add_argument(
        "--self-test",
        action="store_true",
        help="Run the built-in unit tests and exit.",
    )
    args = parser.parse_args(argv)

    if args.self_test:
        loader = unittest.TestLoader()
        suite = loader.loadTestsFromModule(sys.modules[__name__])
        runner = unittest.TextTestRunner(verbosity=2)
        result = runner.run(suite)
        return 0 if result.wasSuccessful() else 1

    warnings = scan(args.base, args.head)
    report = format_report(warnings)
    if report:
        sys.stdout.write(report)
        if not report.endswith("\n"):
            sys.stdout.write("\n")
    return 0  # NEVER fail — soft warning only.


# ---------------------------------------------------------------------------
# Unit tests. Runnable via ``python3 -m unittest scripts.check_snapshot_diffs``
# or ``python3 scripts/check_snapshot_diffs.py --self-test``.
# ---------------------------------------------------------------------------


class MetricsTests(unittest.TestCase):
    def test_counts_comments_and_interop(self) -> None:
        text = (
            "§M{m001:Sample}\n"
            "  // top comment\n"
            "  §CSHARP{public void Foo() { }}§/CSHARP\n"
            "  // trailing comment\n"
        )
        metrics = SnapshotMetrics.from_text(text)
        self.assertEqual(metrics.comment_count, 2)
        self.assertEqual(metrics.interop_block_count, 1)
        self.assertEqual(metrics.interop_body_bytes, len("public void Foo() { }"))

    def test_multiline_interop_body(self) -> None:
        body = "public int X { get; set; }\npublic int Y { get; set; }"
        text = f"§CSHARP{{{body}}}§/CSHARP\n"
        metrics = SnapshotMetrics.from_text(text)
        self.assertEqual(metrics.interop_block_count, 1)
        self.assertEqual(metrics.interop_body_bytes, len(body))

    def test_indented_comment_is_counted(self) -> None:
        text = "    // indented comment\ncode // not a comment start\n"
        metrics = SnapshotMetrics.from_text(text)
        self.assertEqual(metrics.comment_count, 1)


class DiffTests(unittest.TestCase):
    def test_no_warnings_when_stable(self) -> None:
        text = "// keep\n§CSHARP{keep me}§/CSHARP\n"
        self.assertEqual(diff_snapshot("x.approved.calr", text, text), [])

    def test_comment_drop_warns(self) -> None:
        old = "// one\n// two\ncode\n"
        new = "code\n"
        warnings = diff_snapshot("x.approved.calr", old, new)
        self.assertEqual(len(warnings), 1)
        self.assertEqual(warnings[0].kind, "comments dropped")
        self.assertEqual(warnings[0].old_value, 2)
        self.assertEqual(warnings[0].new_value, 0)

    def test_interop_block_drop_warns(self) -> None:
        old = "§CSHARP{a}§/CSHARP\n§CSHARP{b}§/CSHARP\n"
        new = "§CSHARP{a}§/CSHARP\n"
        warnings = diff_snapshot("y.approved.calr", old, new)
        kinds = [w.kind for w in warnings]
        self.assertIn("§CSHARP blocks removed", kinds)

    def test_interop_shrink_over_threshold_warns(self) -> None:
        old_body = "x" * 100
        new_body = "x" * 50  # 50% shrink
        old = f"§CSHARP{{{old_body}}}§/CSHARP\n"
        new = f"§CSHARP{{{new_body}}}§/CSHARP\n"
        warnings = diff_snapshot("z.approved.calr", old, new)
        self.assertTrue(any("shrank" in w.kind for w in warnings))

    def test_interop_shrink_under_threshold_ok(self) -> None:
        # 10% shrink is below the 20% threshold and should NOT warn.
        old_body = "x" * 100
        new_body = "x" * 90
        old = f"§CSHARP{{{old_body}}}§/CSHARP\n"
        new = f"§CSHARP{{{new_body}}}§/CSHARP\n"
        warnings = diff_snapshot("z.approved.calr", old, new)
        self.assertEqual(warnings, [])

    def test_interop_growth_is_never_a_warning(self) -> None:
        old = "§CSHARP{a}§/CSHARP\n"
        new = "§CSHARP{aaaaa}§/CSHARP\n"
        self.assertEqual(diff_snapshot("g.approved.calr", old, new), [])

    def test_comment_added_is_never_a_warning(self) -> None:
        old = "code\n"
        new = "// added\ncode\n"
        self.assertEqual(diff_snapshot("g.approved.calr", old, new), [])


class ReportTests(unittest.TestCase):
    def test_empty_report_when_no_warnings(self) -> None:
        self.assertEqual(format_report([]), "")

    def test_report_includes_marker_and_rows(self) -> None:
        warnings = [
            Warning("a.approved.calr", "comments dropped", 5, 2),
            Warning("b.approved.calr", "§CSHARP blocks removed", 3, 1),
        ]
        report = format_report(warnings)
        self.assertIn("<!-- calor-snapshot-diff-heuristic -->", report)
        self.assertIn("`a.approved.calr`", report)
        self.assertIn("`b.approved.calr`", report)
        self.assertIn("5 -> 2", report)


class GitScanTests(unittest.TestCase):
    """End-to-end scan against a synthetic git repo in a tmp dir."""

    def test_scan_detects_dropped_comments_across_commits(self) -> None:
        import os
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            env = {
                **os.environ,
                "GIT_AUTHOR_NAME": "t",
                "GIT_AUTHOR_EMAIL": "t@t",
                "GIT_COMMITTER_NAME": "t",
                "GIT_COMMITTER_EMAIL": "t@t",
            }

            def run(*args: str) -> None:
                subprocess.run(
                    ["git", *args], cwd=repo, check=True, env=env, capture_output=True
                )

            run("init", "-q", "-b", "main")
            snap = repo / "tests" / "TestData" / "x.approved.calr"
            snap.parent.mkdir(parents=True)
            snap.write_text(
                "// alpha\n// beta\n§CSHARP{keep body}§/CSHARP\n",
                encoding="utf-8",
            )
            run("add", ".")
            run("commit", "-q", "-m", "initial")
            base = subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=repo, check=True, capture_output=True, text=True,
            ).stdout.strip()

            # Drop comments AND remove the interop block: two warnings.
            snap.write_text("code only\n", encoding="utf-8")
            run("add", ".")
            run("commit", "-q", "-m", "regenerate snapshots")

            warnings = scan(base, "HEAD", cwd=repo)
            kinds = [w.kind for w in warnings]
            self.assertIn("comments dropped", kinds)
            self.assertIn("§CSHARP blocks removed", kinds)
            # The body went from 9 bytes to 0 — a 100% shrink, well past
            # the 20% threshold.
            self.assertTrue(any("shrank" in k for k in kinds))

            report = format_report(warnings)
            self.assertIn("`tests/TestData/x.approved.calr`", report)


if __name__ == "__main__":
    raise SystemExit(main())
