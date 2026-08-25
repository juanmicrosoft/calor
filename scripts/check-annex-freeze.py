#!/usr/bin/env python3
"""Append-only freeze guard for the A-annex (docs/plans/agent-native-gates.md).

Roadmap v0.13-v0.15 §4.3 (i): the annex is where proof points (PPs) are
pre-registered, and until A-1.10 it had no mechanical tamper guard — the
append-only check in experiment-registry-tamper-check.yml covered only
docs/experiments/registry.json. This script is the annex half of that guard.
It compares the base-branch annex against the PR head and enforces:

  1. FROZEN ROWS. Every table row in §A.2 whose first cell names a proof point
     (ASCII `PP-<LETTERS><DIGITS>`) is keyed by that id. A row present in base
     must be present in head byte-for-byte. Rows may only be added. A §A.2 row
     whose first cell merely LOOKS like a PP id (non-ASCII letters or digits —
     Cyrillic `Р`, fullwidth `５` — or a case/spacing variant such as `pp-W5`)
     is rejected outright, so a frozen row cannot be shadowed by a homoglyph.
  2. FROZEN TABLE HEADERS. Each §A.2 table's header line and `|---|` separator
     line are byte-identical to base; tables may only be appended.
  3. FROZEN LOG ENTRIES. §A.3 is split into entries at every line that starts
     with `**A-1.` (e.g. `**A-1.9 — ...`, `**A-1.4 tranche 2 — ...`,
     `**A-1.3.1 (...)`). An entry runs to the next entry start, the next `##`/
     `###` heading, or end of file; its key is its first line. Every base entry
     must be present in head with identical text (trailing blank lines
     ignored). Entries may only be added. Versions are dotted tuples: a new
     TOP-LEVEL counter (`A-1.N`) may not sit below the newest base counter
     (history cannot be back-filled), while a sibling of an existing counter
     (`A-1.4 tranche 2`) or a sub-version whose parent entry exists
     (`A-1.3.1` under `A-1.3`) is allowed.
  4. EVERY ANNEX CHANGE IS LOGGED. If any byte of the annex region (from
     `## Annex A` to end of file) differs, head must contain at least one §A.3
     entry that base does not.
  5. POINTER CONSISTENCY. The `**Annex version: A-1.N` pointer at the head of
     the annex must name the highest top-level counter present in §A.3.
  6. FAIL-CLOSED PARSE. An empty or unparseable BASE (missing `## Annex A`,
     `### A.2` or `### A.3` headings, no or several version pointers,
     duplicate PP keys) is a hard error, as is the same defect in head.

What it does NOT guard (by design — keep the instrument simple):
  - The main document (§0–§8, everything before `## Annex A`). Its own §7
    supersession rule is a written-analysis discipline, not a byte freeze.
  - §A.1 metric-definition prose and §A.2 prose outside table rows. Edits
    there are allowed but must be accompanied by a new §A.3 entry (rule 4).
  - Proof points with no §A.2 row (PP-A1, PP-A2, PP-G1, PP-G2, PP-L6, PP-W2
    today) are frozen only through the §A.3 entry bodies that register them.
  - The *content* of a new entry or row. The guard checks that a change is
    recorded, not that the record is honest; that stays with review.
  - The `Annex version` pointer paragraph text beyond its `A-1.N` number.
  - The guard itself: CI runs the BASE-branch copy of this script, and refuses
    a PR that changes the annex together with the guard or its workflow.

Usage:
  scripts/check-annex-freeze.py --base-file BASE.md --head-file HEAD.md
  scripts/check-annex-freeze.py --base-ref origin/main [--head-file HEAD.md]
  scripts/check-annex-freeze.py --self-test [--head-file HEAD.md]
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
POINTER_RE = re.compile(r"^\*\*Annex version: A-1\.(\d+)\b", re.ASCII)
ENTRY_RE = re.compile(r"^\*\*A-1\.(\d+(?:\.\d+)*)\b", re.ASCII)
PP_ID_RE = re.compile(r"\bPP-[A-Z]+[0-9]+\b", re.ASCII)
PP_LOOKALIKE_RE = re.compile(r"(?i)\bp\s*p\s*-", re.ASCII)
SEPARATOR_RE = re.compile(r"^\|[\s:\-|]+\|\s*$")
NO_ROW_PPS = ("PP-A1", "PP-A2", "PP-G1", "PP-G2", "PP-L6", "PP-W2")

Version = tuple[int, ...]


@dataclass
class Annex:
    text: str = ""
    pointer_version: int | None = None
    rows: dict[str, str] = field(default_factory=dict)
    table_headers: list[str] = field(default_factory=list)
    entries: dict[str, str] = field(default_factory=dict)
    entry_versions: dict[str, Version] = field(default_factory=dict)
    problems: list[str] = field(default_factory=list)

    @property
    def max_top_level(self) -> int | None:
        return max((v[0] for v in self.entry_versions.values()), default=None)


def _find_line(lines: list[str], prefix: str, start: int = 0) -> int:
    for index in range(start, len(lines)):
        if lines[index].startswith(prefix):
            return index
    return -1


def _is_heading(line: str) -> bool:
    return line.startswith("## ") or line.startswith("### ")


def _non_ascii_alnum(text: str) -> str:
    return "".join(sorted({c for c in text if ord(c) > 127 and (c.isalpha() or c.isdigit())}))


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

    a2_lines = lines[a2 + 1 : a3]
    for index, line in enumerate(a2_lines):
        if not line.startswith("|"):
            continue
        if index + 1 < len(a2_lines) and SEPARATOR_RE.match(a2_lines[index + 1]):
            annex.table_headers.append(line + "\n" + a2_lines[index + 1])
            continue
        if SEPARATOR_RE.match(line):
            continue
        cells = line.split("|")
        first_cell = cells[1] if len(cells) > 1 else ""
        match = PP_ID_RE.search(first_cell)
        shadow = _non_ascii_alnum(first_cell)
        if shadow:
            annex.problems.append(
                f"§A.2 row first cell contains non-ASCII letters/digits {shadow!r} (homoglyph shadow of a PP id?): "
                f"{first_cell.strip()[:60]!r}"
            )
            continue
        if not match:
            if PP_LOOKALIKE_RE.search(first_cell):
                annex.problems.append(
                    f"§A.2 row first cell looks like a PP id but is not ASCII `PP-<LETTERS><DIGITS>`: "
                    f"{first_cell.strip()[:60]!r}"
                )
            continue
        key = match.group(0)
        if key in annex.rows:
            annex.problems.append(f"duplicate frozen row key {key} in §A.2")
            continue
        annex.rows[key] = line

    a3_end = next((i for i in range(a3 + 1, len(lines)) if _is_heading(lines[i])), len(lines))
    entry_lines = lines[a3 + 1 : a3_end]
    starts = [index for index, line in enumerate(entry_lines) if ENTRY_RE.match(line)]
    for position, start in enumerate(starts):
        end = starts[position + 1] if position + 1 < len(starts) else len(entry_lines)
        key = entry_lines[start]
        body = "\n".join(entry_lines[start:end]).rstrip()
        if key in annex.entries:
            annex.problems.append(f"duplicate revision-log entry first line: {key[:80]!r}")
            continue
        annex.entries[key] = body
        match = ENTRY_RE.match(key)
        assert match is not None
        annex.entry_versions[key] = tuple(int(part) for part in match.group(1).split("."))
    return annex


def _first_difference(base: str, head: str, context: int = 40) -> str:
    limit = min(len(base), len(head))
    offset = next((i for i in range(limit) if base[i] != head[i]), limit)
    lo = max(0, offset - context)
    return (
        f"first difference at char {offset}: "
        f"base …{base[lo:offset + context]!r}… / head …{head[lo:offset + context]!r}…"
    )


def _version_text(version: Version) -> str:
    return "A-1." + ".".join(str(part) for part in version)


def _new_version_allowed(version: Version, base: Annex, head: Annex) -> bool:
    base_versions = set(base.entry_versions.values())
    base_tops = {v[0] for v in base_versions}
    base_max = base.max_top_level
    assert base_max is not None
    if len(version) == 1:
        return version[0] in base_tops or version[0] >= base_max
    parent = version[:-1]
    if parent in base_versions or parent in set(head.entry_versions.values()):
        return True
    return version[0] >= base_max


def check(base_document: str, head_document: str, *, quiet: bool = False) -> list[str]:
    """Return the list of violations (empty means the head honours the freeze)."""
    errors: list[str] = []
    head = parse_annex(head_document)
    for problem in head.problems:
        errors.append(f"head annex is malformed (fail-closed): {problem}")
    if head.problems:
        return errors

    if not base_document.strip():
        return ["base annex is empty (fail-closed): refusing to treat the whole head as additions"]
    base = parse_annex(base_document)
    for problem in base.problems:
        errors.append(f"base annex is malformed (fail-closed): {problem}")
    if base.problems:
        return errors
    if not base.entries:
        return ["base §A.3 contains no `**A-1.N` revision-log entries (fail-closed)"]

    for key, base_row in base.rows.items():
        head_row = head.rows.get(key)
        if head_row is None:
            errors.append(f"frozen §A.2 row {key} was removed")
        elif head_row != base_row:
            errors.append(f"frozen §A.2 row {key} was modified — {_first_difference(base_row, head_row)}")

    for index, base_header in enumerate(base.table_headers):
        head_header = head.table_headers[index] if index < len(head.table_headers) else None
        if head_header is None:
            errors.append(f"frozen §A.2 table header/separator #{index + 1} was removed")
        elif head_header != base_header:
            errors.append(
                f"frozen §A.2 table header/separator #{index + 1} was modified — "
                f"{_first_difference(base_header, head_header)}"
            )

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

    for key in new_entries:
        version = head.entry_versions[key]
        if not _new_version_allowed(version, base, head):
            errors.append(
                f"new §A.3 entry {key[:80]!r} carries version {_version_text(version)}, below the newest "
                f"base entry A-1.{base.max_top_level} with no parent/sibling entry (history may not be back-filled)"
            )

    head_max = head.max_top_level
    if head_max is None:
        errors.append("head §A.3 contains no `**A-1.N` revision-log entries")
    elif head.pointer_version != head_max:
        errors.append(
            f"annex version pointer says A-1.{head.pointer_version} but the newest §A.3 entry is A-1.{head_max}"
        )

    if not errors and not quiet:
        print(
            f"annex-freeze: OK — {len(base.rows)} frozen rows, {len(base.table_headers)} frozen table headers "
            f"and {len(base.entries)} frozen entries intact; +{len(new_rows)} rows, +{len(new_entries)} entries; "
            f"pointer A-1.{head.pointer_version}"
        )
    return errors


# --------------------------------------------------------------------------- #
# Self-test: discriminating pins on a synthetic annex, plus a mandatory shape
# pin on the real document.
# --------------------------------------------------------------------------- #

SYNTHETIC_BASE = """# Gate Thresholds

## 4. Phase gate thresholds

Main-document prose. Not guarded by the annex freeze.

## 8. Revision log

**v1 → v2.** Main-document log.

---

## Annex A — Instrument metrics

**Annex version: A-1.2 (additive A-1.2; thresholds frozen at A-1.0).** Prose.

### A.1 Instrument metric definitions

- **M-X1** — a metric definition.

### A.2 Frozen instrument proof-point thresholds

| Proof point | Threshold (frozen) | Basis |
|:---|:---|:---|
| PP-X1 (alpha) | **≥ 3/9** on the set | dry-run |
| **PP-X2** — beta (**blocker**) | M-X1 = 0 | fixture registry |

Prose between the tables.

### A.3 Annex revision log

**A-1.2 (2026-01-02).** Additive clarification, no threshold changes.
Second line of the entry.

**A-1.0 (2026-01-01).** Initial freeze.
"""

_ROW_X1 = "| PP-X1 (alpha) | **≥ 3/9** on the set | dry-run |"
_ROW_X2 = "| **PP-X2** — beta (**blocker**) | M-X1 = 0 | fixture registry |"
_HEADER = "| Proof point | Threshold (frozen) | Basis |"
_POINTER = "A-1.2 (additive A-1.2; thresholds frozen at A-1.0)"
_ENTRY_1_2 = "**A-1.2 (2026-01-02).** Additive clarification, no threshold changes.\nSecond line of the entry."
_A1_PROSE = "- **M-X1** — a metric definition."
_MAIN_PROSE = "Main-document prose. Not guarded by the annex freeze."


def _synthetic(
    *,
    row_x1: str | None = _ROW_X1,
    extra_row: str = "",
    header: str = _HEADER,
    pointer: str = _POINTER,
    entry_1_2: str | None = _ENTRY_1_2,
    new_entry: str = "",
    a1_prose: str = _A1_PROSE,
    main_prose: str = _MAIN_PROSE,
    drop_a3_heading: bool = False,
    trailer: str = "",
) -> str:
    text = SYNTHETIC_BASE
    text = text.replace(_ROW_X1, row_x1 or "<<DROP>>")
    text = text.replace(_ROW_X2, _ROW_X2 + ("\n" + extra_row if extra_row else ""))
    text = text.replace(_HEADER, header)
    text = text.replace(_POINTER, pointer)
    text = text.replace(_ENTRY_1_2, entry_1_2 or "<<DROP>>")
    text = text.replace("### A.3 Annex revision log\n", "### A.3 Annex revision log\n" + (new_entry + "\n\n" if new_entry else ""))
    text = text.replace(_A1_PROSE, a1_prose)
    text = text.replace(_MAIN_PROSE, main_prose)
    if drop_a3_heading:
        text = text.replace("### A.3 Annex revision log", "### Renamed log")
    text = "\n".join(line for line in text.split("\n") if line != "<<DROP>>")
    return text + trailer


def _real_annex(head_file: Path | None) -> Path:
    candidates = [head_file] if head_file else []
    candidates += [Path.cwd() / ANNEX_PATH, Path(__file__).resolve().parent.parent / ANNEX_PATH]
    for candidate in candidates:
        if candidate is not None and candidate.is_file():
            return candidate
    raise SystemExit(
        f"SELF-TEST FAILED: {ANNEX_PATH} is unreachable (tried {', '.join(str(c) for c in candidates)}); "
        "the real-document shape pin is mandatory — run from the repo root or pass --head-file"
    )


def self_test(head_file: Path | None = None) -> None:
    base = _synthetic()
    logged = dict(new_entry="**A-1.3 (2026-01-03).** Additive.", pointer="A-1.3 (additive A-1.3)")
    counts = {"pass": 0, "fail": 0}

    def expect(name: str, head: str, *, fails: bool, needle: str | None = None, base_doc: str = base) -> None:
        errors = check(base_doc, head, quiet=True)
        if fails and not errors:
            raise SystemExit(f"SELF-TEST FAILED: {name} was accepted")
        if not fails and errors:
            raise SystemExit(f"SELF-TEST FAILED: {name} was rejected: {errors}")
        if needle and not any(needle in error for error in errors):
            raise SystemExit(f"SELF-TEST FAILED: {name} did not report {needle!r}: {errors}")
        counts["fail" if fails else "pass"] += 1

    # Pass pins.
    expect("no-change diff", base, fails=False)
    expect("new PP row + new entry + pointer bump", _synthetic(extra_row="| PP-X3 (gamma) | ≤ 1.25 | derivation |", **logged), fails=False)
    expect("A.1 prose edit with a new entry", _synthetic(a1_prose="- **M-X1** — reworded.", **logged), fails=False)
    expect("sub-version A-1.2.1 (parent is newest) without pointer bump", _synthetic(new_entry="**A-1.2.1 (2026-01-03).** Amendment."), fails=False)
    expect("sub-version A-1.0.1 below the newest, parent A-1.0 in base (the A-1.3.1 pattern)", _synthetic(new_entry="**A-1.0.1 (2026-01-03).** Amendment to A-1.0."), fails=False)
    expect("sibling of an existing counter (the A-1.4 tranche 2 pattern)", _synthetic(new_entry="**A-1.0 exclusion note (2026-01-03).** Additive record."), fails=False)
    expect("new top-level entry + its sub-entry in one PR", _synthetic(new_entry="**A-1.3 (2026-01-03).** Additive.\n\n**A-1.3.1 (2026-01-03).** Amendment.", pointer="A-1.3 (additive A-1.3)"), fails=False)
    expect("main-document edit without any entry (documented non-guard)", _synthetic(main_prose="Edited main prose."), fails=False)
    expect("appended ### A.4 section after the log + new entry (A-1.0 not absorbed)", _synthetic(trailer="\n### A.4 Future section\n\nNew prose.\n", **logged), fails=False)

    # Fail pins.
    expect("mutated frozen row (with entry + pointer)", _synthetic(row_x1="| PP-X1 (alpha) | **≥ 2/9** on the set | dry-run |", **logged), fails=True, needle="row PP-X1 was modified")
    expect("removed frozen row (with entry + pointer)", _synthetic(row_x1=None, **logged), fails=True, needle="row PP-X1 was removed")
    expect("new PP row without a log entry", _synthetic(extra_row="| PP-X3 (gamma) | ≤ 1.25 | derivation |"), fails=True, needle="no new revision-log entry")
    expect("renamed table column (with entry + pointer)", _synthetic(header="| Proof point | Threshold (draft) | Basis |", **logged), fails=True, needle="table header/separator #1 was modified")
    expect("fullwidth-digit shadow row PP-X１ (with entry + pointer)", _synthetic(row_x1="| PP-X１ (alpha) | **≥ 2/9** on the set | dry-run |\n" + _ROW_X1, **logged), fails=True, needle="non-ASCII letters/digits")
    expect("Cyrillic-Р shadow row РP-X1 (with entry + pointer)", _synthetic(extra_row="| РP-X1 (alpha) | **≥ 2/9** on the set | dry-run |", **logged), fails=True, needle="non-ASCII letters/digits")
    expect("lower-case lookalike row pp-X9 (with entry + pointer)", _synthetic(extra_row="| pp-X9 (alpha) | 1 | 2 |", **logged), fails=True, needle="looks like a PP id")
    expect("edited existing entry body (with new entry + pointer)", _synthetic(entry_1_2="**A-1.2 (2026-01-02).** Additive clarification, no threshold changes.\nSecond line, thresholds RAISED.", **logged), fails=True, needle="entry was modified")
    expect("edited first line of an existing entry (with new entry + pointer)", _synthetic(entry_1_2="**A-1.2 (2026-01-02, restated).** Additive clarification, no threshold changes.\nSecond line of the entry.", **logged), fails=True, needle="entry was removed")
    expect("removed entry (with new entry + pointer)", _synthetic(entry_1_2=None, **logged), fails=True, needle="entry was removed")
    expect("new entry without pointer bump", _synthetic(new_entry="**A-1.3 (2026-01-03).** Additive."), fails=True, needle="pointer says A-1.2 but the newest")
    expect("pointer bumped without a matching entry", _synthetic(pointer="A-1.3 (additive A-1.3)"), fails=True, needle="no new revision-log entry")
    expect("back-filled top-level A-1.1 below the newest base counter", _synthetic(new_entry="**A-1.1 (2026-01-03).** Retroactive note."), fails=True, needle="history may not be back-filled")
    expect("back-filled sub-version A-1.1.1 with no parent anywhere", _synthetic(new_entry="**A-1.1.1 (2026-01-03).** Retroactive note."), fails=True, needle="history may not be back-filled")
    expect("appended ### A.4 section without a log entry", _synthetic(trailer="\n### A.4 Future section\n\nNew prose.\n"), fails=True, needle="no new revision-log entry")
    expect("missing A.3 heading", _synthetic(drop_a3_heading=True, **logged), fails=True, needle="malformed")
    expect("duplicate PP key", _synthetic(extra_row="| PP-X2 (dup) | 1 | 2 |", **logged), fails=True, needle="duplicate frozen row key PP-X2")
    expect("empty base document (/dev/null)", base, fails=True, needle="base annex is empty", base_doc="")
    expect("base document without an annex", base, fails=True, needle="base annex is malformed", base_doc="# Not the annex\n\nprose\n")

    # Mandatory shape pin on the real annex: it must parse, its pointer must
    # match its newest entry, and the rows this guard was built for exist.
    real = _real_annex(head_file)
    document = real.read_text(encoding="utf-8")
    annex = parse_annex(document)
    if annex.problems:
        raise SystemExit(f"SELF-TEST FAILED: {real} does not parse: {annex.problems}")
    for key in ("PP-W1", "PP-W5", "PP-S4"):
        if key not in annex.rows:
            raise SystemExit(f"SELF-TEST FAILED: frozen row {key} not found in {real} §A.2")
    for key in NO_ROW_PPS:
        if key in annex.rows:
            raise SystemExit(f"SELF-TEST FAILED: {key} now has a §A.2 row; update NO_ROW_PPS and the A-1.10 disclosure")
    if "**A-1.0 (2026-07-24).** Initial freeze. Definitions from loop plan §4;" not in annex.entries:
        raise SystemExit(f"SELF-TEST FAILED: A-1.0 entry not found in {real} §A.3")
    if len(annex.table_headers) < 2:
        raise SystemExit(f"SELF-TEST FAILED: expected at least two §A.2 tables in {real}, found {len(annex.table_headers)}")
    if check(document, document, quiet=True):
        raise SystemExit(f"SELF-TEST FAILED: {real} is not self-consistent")
    print(
        f"annex-freeze self-test: {real} parses — {len(annex.rows)} frozen rows, "
        f"{len(annex.table_headers)} frozen table headers, {len(annex.entries)} frozen entries, "
        f"pointer A-1.{annex.pointer_version}"
    )
    total = counts["pass"] + counts["fail"]
    print(f"annex-freeze self-test: all {total} pins hold ({counts['pass']} pass pins, {counts['fail']} fail pins) plus the real-document shape pin.")


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
    parser.add_argument("--head-file", type=Path, default=None, help=f"head version (default {ANNEX_PATH})")
    parser.add_argument("--self-test", action="store_true", help="run the discriminating pins and exit")
    args = parser.parse_args()

    if args.self_test:
        self_test(args.head_file)
        return 0
    if (args.base_file is None) == (args.base_ref is None):
        parser.error("exactly one of --base-file or --base-ref is required")

    base_document = (
        args.base_file.read_text(encoding="utf-8") if args.base_file else _git_show(args.base_ref, ANNEX_PATH)
    )
    head_document = (args.head_file or Path(ANNEX_PATH)).read_text(encoding="utf-8")
    errors = check(base_document, head_document)
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        print(f"annex-freeze: FAILED with {len(errors)} violation(s) — frozen rows/entries are append-only")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
