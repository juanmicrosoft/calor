#!/usr/bin/env python3
"""Append-only freeze guard for the A-annex (docs/plans/agent-native-gates.md).

Roadmap v0.13-v0.15 §4.3 (i): the annex is where proof points (PPs) are
pre-registered, and until A-1.10 it had no mechanical tamper guard — the
append-only check in experiment-registry-tamper-check.yml covered only
docs/experiments/registry.json. This script is the annex half of that guard.
It compares the base-branch annex against the PR head and enforces:

  1. FROZEN ROWS. Every table row in §A.2 whose first cell names a proof point
     (`PP-<letters><digits>`) is keyed by that id. A row present in base must be
     present in head byte-for-byte. Rows may only be added.
  2. FROZEN LOG ENTRIES. §A.3 is split into entries at every line that starts
     with `**A-1.` (e.g. `**A-1.9 — ...`, `**A-1.4 tranche 2 — ...`,
     `**A-1.3.1 (...)`). An entry runs to the next entry start; its key is its
     first line. Every base entry must be present in head with identical text
     (trailing blank lines ignored). Entries may only be added, and a new entry
     may not carry a version counter below the newest base entry.
  3. EVERY ANNEX CHANGE IS LOGGED. If any byte of the annex region (from
     `## Annex A` to end of file) differs, head must contain at least one §A.3
     entry that base does not.
  4. POINTER CONSISTENCY. The `**Annex version: A-1.N` pointer at the head of
     the annex must name the highest version counter present in §A.3.
  5. FAIL-CLOSED PARSE. If head lacks the `## Annex A`, `### A.2` or `### A.3`
     headings, has no (or several) version pointers, or has duplicate PP keys
     in §A.2, the check fails.

What it does NOT guard (by design — keep the instrument simple):
  - The main document (§0–§8, everything before `## Annex A`). Its own §7
    supersession rule is a written-analysis discipline, not a byte freeze.
  - §A.1 metric-definition prose and §A.2 non-row prose/table headers. Edits
    there are allowed but must be accompanied by a new §A.3 entry (rule 3).
  - The *content* of a new entry or row. The guard checks that a change is
    recorded, not that the record is honest; that stays with review.
  - The `Annex version` pointer paragraph text beyond its `A-1.N` number.

Usage:
  scripts/check-annex-freeze.py --base-file BASE.md --head-file HEAD.md
  scripts/check-annex-freeze.py --base-ref origin/main [--head-file HEAD.md]
  scripts/check-annex-freeze.py --self-test
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

ANNEX_PATH = "docs/plans/agent-native-gates.md"
ANNEX_HEADING_PREFIX = "## Annex A"
A2_HEADING_PREFIX = "### A.2 "
A3_HEADING_PREFIX = "### A.3 "
POINTER_RE = re.compile(r"^\*\*Annex version: A-1\.(\d+)\b")
ENTRY_RE = re.compile(r"^\*\*A-1\.(\d+)\b")
PP_ID_RE = re.compile(r"\bPP-[A-Z]+\d+\b")


@dataclass
class Annex:
    text: str = ""
    pointer_version: int | None = None
    rows: dict[str, str] = field(default_factory=dict)
    entries: dict[str, str] = field(default_factory=dict)
    entry_versions: dict[str, int] = field(default_factory=dict)
    problems: list[str] = field(default_factory=list)

    @property
    def max_entry_version(self) -> int | None:
        return max(self.entry_versions.values()) if self.entry_versions else None


def _find_line(lines: list[str], prefix: str, start: int = 0) -> int:
    for index in range(start, len(lines)):
        if lines[index].startswith(prefix):
            return index
    return -1


def parse_annex(document: str) -> Annex:
    """Parse the annex region of the document. Structural defects are recorded in
    `problems` rather than raised, so the caller can decide how to fail."""
    annex = Annex()
    lines = document.split("\n")
    annex_start = _find_line(lines, ANNEX_HEADING_PREFIX)
    if annex_start < 0:
        annex.problems.append(f"missing '{ANNEX_HEADING_PREFIX}' heading")
        return annex
    annex.text = "\n".join(lines[annex_start:])

    a2 = _find_line(lines, A2_HEADING_PREFIX, annex_start)
    a3 = _find_line(lines, A3_HEADING_PREFIX, annex_start)
    if a2 < 0:
        annex.problems.append(f"missing '{A2_HEADING_PREFIX}' heading after the annex heading")
    if a3 < 0:
        annex.problems.append(f"missing '{A3_HEADING_PREFIX}' heading after the annex heading")
    if a2 >= 0 and a3 >= 0 and not a2 < a3:
        annex.problems.append("'### A.2' must precede '### A.3'")
    if annex.problems:
        return annex

    pointers = [POINTER_RE.match(line) for line in lines[annex_start:a2]]
    pointers = [match for match in pointers if match]
    if len(pointers) != 1:
        annex.problems.append(
            f"expected exactly one '**Annex version: A-1.N' pointer before '### A.2', found {len(pointers)}"
        )
    else:
        annex.pointer_version = int(pointers[0].group(1))

    for line in lines[a2 + 1 : a3]:
        if not line.startswith("|"):
            continue
        cells = line.split("|")
        first_cell = cells[1] if len(cells) > 1 else ""
        match = PP_ID_RE.search(first_cell)
        if not match:
            continue
        key = match.group(0)
        if key in annex.rows:
            annex.problems.append(f"duplicate frozen row key {key} in §A.2")
            continue
        annex.rows[key] = line

    entry_lines = lines[a3 + 1 :]
    starts = [index for index, line in enumerate(entry_lines) if ENTRY_RE.match(line)]
    for position, start in enumerate(starts):
        end = starts[position + 1] if position + 1 < len(starts) else len(entry_lines)
        key = entry_lines[start]
        body = "\n".join(entry_lines[start:end]).rstrip()
        if key in annex.entries:
            annex.problems.append(f"duplicate revision-log entry first line: {key[:80]!r}")
            continue
        annex.entries[key] = body
        annex.entry_versions[key] = int(ENTRY_RE.match(key).group(1))  # type: ignore[union-attr]
    return annex


def _first_difference(base: str, head: str, context: int = 40) -> str:
    limit = min(len(base), len(head))
    offset = next((i for i in range(limit) if base[i] != head[i]), limit)
    lo = max(0, offset - context)
    return (
        f"first difference at char {offset}: "
        f"base …{base[lo:offset + context]!r}… / head …{head[lo:offset + context]!r}…"
    )


def check(base_document: str, head_document: str, *, quiet: bool = False) -> list[str]:
    """Return the list of violations (empty means the head honours the freeze)."""
    errors: list[str] = []
    head = parse_annex(head_document)
    for problem in head.problems:
        errors.append(f"head annex is malformed (fail-closed): {problem}")
    if head.problems:
        return errors

    base = parse_annex(base_document) if base_document.strip() else Annex()
    for problem in base.problems:
        # A malformed base cannot be repaired by any PR if it blocks every PR;
        # report it and check against whatever was parseable.
        print(f"WARNING: base annex is malformed: {problem}", file=sys.stderr)

    for key, base_row in base.rows.items():
        head_row = head.rows.get(key)
        if head_row is None:
            errors.append(f"frozen §A.2 row {key} was removed")
        elif head_row != base_row:
            errors.append(f"frozen §A.2 row {key} was modified — {_first_difference(base_row, head_row)}")

    for key, base_body in base.entries.items():
        head_body = head.entries.get(key)
        if head_body is None:
            errors.append(f"frozen §A.3 entry was removed: {key[:80]!r}")
        elif head_body != base_body:
            errors.append(
                f"frozen §A.3 entry was modified: {key[:80]!r} — {_first_difference(base_body, head_body)}"
            )

    new_entries = [key for key in head.entries if key not in base.entries]
    new_rows = [key for key in head.rows if key not in base.rows]

    if head.text != base.text and not new_entries:
        errors.append(
            "the annex changed but §A.3 gained no new revision-log entry "
            "(every annex change must be recorded as a new `**A-1.N` entry)"
        )

    base_max = base.max_entry_version
    if base_max is not None:
        for key in new_entries:
            if head.entry_versions[key] < base_max:
                errors.append(
                    f"new §A.3 entry {key[:80]!r} carries version A-1.{head.entry_versions[key]}, "
                    f"below the newest base entry A-1.{base_max} (history may not be back-filled)"
                )

    head_max = head.max_entry_version
    if head_max is None:
        errors.append("head §A.3 contains no `**A-1.N` revision-log entries")
    elif head.pointer_version != head_max:
        errors.append(
            f"annex version pointer says A-1.{head.pointer_version} but the newest §A.3 entry is A-1.{head_max}"
        )

    if not errors and not quiet:
        print(
            f"annex-freeze: OK — {len(base.rows)} frozen rows and {len(base.entries)} frozen entries "
            f"intact; +{len(new_rows)} rows, +{len(new_entries)} entries; pointer A-1.{head.pointer_version}"
        )
    return errors


# --------------------------------------------------------------------------- #
# Self-test: discriminating pins on a synthetic annex, plus a shape pin on the
# real document when it is reachable from this script's location.
# --------------------------------------------------------------------------- #

SYNTHETIC_BASE = """# Gate Thresholds

## 4. Phase gate thresholds

Main-document prose. Not guarded by the annex freeze.

## 8. Revision log

**v1 → v2.** Main-document log.

---

## Annex A — Instrument metrics

**Annex version: A-1.1 (additive A-1.1; thresholds frozen at A-1.0).** Prose.

### A.1 Instrument metric definitions

- **M-X1** — a metric definition.

### A.2 Frozen instrument proof-point thresholds

| Proof point | Threshold (frozen) | Basis |
|:---|:---|:---|
| PP-X1 (alpha) | **≥ 3/9** on the set | dry-run |
| **PP-X2** — beta (**blocker**) | M-X1 = 0 | fixture registry |

Prose between the tables.

### A.3 Annex revision log

**A-1.1 (2026-01-02).** Additive clarification, no threshold changes.
Second line of the entry.

**A-1.0 (2026-01-01).** Initial freeze.
"""


def _synthetic(
    *,
    row_x1: str | None = "| PP-X1 (alpha) | **≥ 3/9** on the set | dry-run |",
    extra_row: str = "",
    pointer: str = "A-1.1 (additive A-1.1; thresholds frozen at A-1.0)",
    entry_1_1: str | None = "**A-1.1 (2026-01-02).** Additive clarification, no threshold changes.\nSecond line of the entry.",
    new_entry: str = "",
    a1_prose: str = "- **M-X1** — a metric definition.",
    main_prose: str = "Main-document prose. Not guarded by the annex freeze.",
    drop_a3_heading: bool = False,
) -> str:
    text = SYNTHETIC_BASE
    text = text.replace("| PP-X1 (alpha) | **≥ 3/9** on the set | dry-run |", row_x1 or "<<DROP>>")
    text = text.replace(
        "| **PP-X2** — beta (**blocker**) | M-X1 = 0 | fixture registry |",
        "| **PP-X2** — beta (**blocker**) | M-X1 = 0 | fixture registry |" + ("\n" + extra_row if extra_row else ""),
    )
    text = text.replace("A-1.1 (additive A-1.1; thresholds frozen at A-1.0)", pointer)
    text = text.replace(
        "**A-1.1 (2026-01-02).** Additive clarification, no threshold changes.\nSecond line of the entry.", entry_1_1 or "<<DROP>>"
    )
    text = text.replace("### A.3 Annex revision log\n", "### A.3 Annex revision log\n" + (new_entry + "\n\n" if new_entry else ""))
    text = text.replace("- **M-X1** — a metric definition.", a1_prose)
    text = text.replace("Main-document prose. Not guarded by the annex freeze.", main_prose)
    if drop_a3_heading:
        text = text.replace("### A.3 Annex revision log", "### Renamed log")
    return "\n".join(line for line in text.split("\n") if line != "<<DROP>>")


def self_test() -> None:
    base = _synthetic()
    logged = dict(new_entry="**A-1.2 (2026-01-03).** Additive.", pointer="A-1.2 (additive A-1.2)")

    def expect(name: str, head: str, *, fails: bool, needle: str | None = None) -> None:
        errors = check(base, head, quiet=True)
        if fails and not errors:
            raise SystemExit(f"SELF-TEST FAILED: {name} was accepted")
        if not fails and errors:
            raise SystemExit(f"SELF-TEST FAILED: {name} was rejected: {errors}")
        if needle and not any(needle in error for error in errors):
            raise SystemExit(f"SELF-TEST FAILED: {name} did not report {needle!r}: {errors}")

    # Pass pins.
    expect("no-change diff", base, fails=False)
    expect(
        "new PP row + new entry + pointer bump",
        _synthetic(extra_row="| PP-X3 (gamma) | ≤ 1.25 | derivation |", **logged),
        fails=False,
    )
    expect("A.1 prose edit with a new entry", _synthetic(a1_prose="- **M-X1** — reworded.", **logged), fails=False)
    expect("sub-version entry A-1.1.1 without pointer bump", _synthetic(new_entry="**A-1.1.1 (2026-01-03).** Amendment."), fails=False)
    expect("main-document edit without any entry (documented non-guard)", _synthetic(main_prose="Edited main prose."), fails=False)

    # Fail pins.
    expect(
        "mutated frozen row (with entry + pointer)",
        _synthetic(row_x1="| PP-X1 (alpha) | **≥ 2/9** on the set | dry-run |", **logged),
        fails=True,
        needle="row PP-X1 was modified",
    )
    expect("removed frozen row (with entry + pointer)", _synthetic(row_x1=None, **logged), fails=True, needle="row PP-X1 was removed")
    expect(
        "new PP row without a log entry",
        _synthetic(extra_row="| PP-X3 (gamma) | ≤ 1.25 | derivation |"),
        fails=True,
        needle="no new revision-log entry",
    )
    expect(
        "edited existing entry (with new entry + pointer)",
        _synthetic(entry_1_1="**A-1.1 (2026-01-02).** Additive clarification, no threshold changes.\nSecond line, thresholds RAISED.", **logged),
        fails=True,
        needle="entry was modified",
    )
    expect(
        "edited first line of an existing entry (with new entry + pointer)",
        _synthetic(entry_1_1="**A-1.1 (2026-01-02, restated).** Additive clarification, no threshold changes.\nSecond line of the entry.", **logged),
        fails=True,
        needle="entry was removed",
    )
    expect("removed entry (with new entry + pointer)", _synthetic(entry_1_1=None, **logged), fails=True, needle="entry was removed")
    expect(
        "new entry without pointer bump",
        _synthetic(new_entry="**A-1.2 (2026-01-03).** Additive."),
        fails=True,
        needle="pointer says A-1.1 but the newest",
    )
    expect(
        "pointer bumped without a matching entry",
        _synthetic(pointer="A-1.2 (additive A-1.2)"),
        fails=True,
        needle="no new revision-log entry",
    )
    expect(
        "back-filled entry below the newest base version",
        _synthetic(new_entry="**A-1.0.1 (2026-01-03).** Retroactive note."),
        fails=True,
        needle="history may not be back-filled",
    )
    expect("missing A.3 heading", _synthetic(drop_a3_heading=True, **logged), fails=True, needle="malformed")
    expect(
        "duplicate PP key",
        _synthetic(extra_row="| PP-X2 (dup) | 1 | 2 |", **logged),
        fails=True,
        needle="duplicate frozen row key PP-X2",
    )

    # Shape pin on the real annex, when reachable: it must parse, its pointer
    # must match its newest entry, and the rows this guard was built for exist.
    real = Path(__file__).resolve().parent.parent / ANNEX_PATH
    if real.is_file():
        document = real.read_text(encoding="utf-8")
        annex = parse_annex(document)
        if annex.problems:
            raise SystemExit(f"SELF-TEST FAILED: {ANNEX_PATH} does not parse: {annex.problems}")
        for key in ("PP-W1", "PP-W5", "PP-S4"):
            if key not in annex.rows:
                raise SystemExit(f"SELF-TEST FAILED: frozen row {key} not found in {ANNEX_PATH} §A.2")
        if "**A-1.0 (2026-07-24).** Initial freeze. Definitions from loop plan §4;" not in annex.entries:
            raise SystemExit(f"SELF-TEST FAILED: A-1.0 entry not found in {ANNEX_PATH} §A.3")
        if check(document, document, quiet=True):
            raise SystemExit(f"SELF-TEST FAILED: {ANNEX_PATH} is not self-consistent")
        print(
            f"annex-freeze self-test: {ANNEX_PATH} parses — {len(annex.rows)} frozen rows, "
            f"{len(annex.entries)} frozen entries, pointer A-1.{annex.pointer_version}"
        )
    print("annex-freeze self-test: all 17 pins hold (5 pass pins, 12 fail pins).")


def _git_show(ref: str, path: str) -> str:
    result = subprocess.run(
        ["git", "show", f"{ref}:{path}"], capture_output=True, text=True, encoding="utf-8"
    )
    if result.returncode != 0:
        raise SystemExit(f"ERROR: git show {ref}:{path} failed (fail-closed): {result.stderr.strip()}")
    return result.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-file", type=Path, help="base-branch version of the annex")
    parser.add_argument("--base-ref", help="git ref to read the base annex from (e.g. origin/main)")
    parser.add_argument("--head-file", type=Path, default=Path(ANNEX_PATH), help=f"head version (default {ANNEX_PATH})")
    parser.add_argument("--self-test", action="store_true", help="run the discriminating pins and exit")
    args = parser.parse_args()

    if args.self_test:
        self_test()
        return 0
    if (args.base_file is None) == (args.base_ref is None):
        parser.error("exactly one of --base-file or --base-ref is required")

    base_document = (
        args.base_file.read_text(encoding="utf-8") if args.base_file else _git_show(args.base_ref, ANNEX_PATH)
    )
    head_document = args.head_file.read_text(encoding="utf-8")
    errors = check(base_document, head_document)
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        print(f"annex-freeze: FAILED with {len(errors)} violation(s) — frozen rows/entries are append-only")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
