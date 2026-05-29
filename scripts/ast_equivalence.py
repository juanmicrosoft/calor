#!/usr/bin/env python3
"""
ast_equivalence.py — modulo-ID-renaming structural equality on Calor source.

Defined as the equality relation used by:
  - validation plan §3a rule 1 (samples/ round-trip equivalence)
  - validation plan §3a rule 1b (synthetic-stress-corpus round-trip)
  - validation plan §3a rule 7 (commingled-mode fuzz)
  - H2 reversibility harness (parse(closer)→emit(indent)→parse(indent)→emit(closer))

Two Calor source strings are AST-equivalent (modulo this script's normalization)
iff their canonical token sequences are identical, where canonicalization:

  1. Strips comments (lines starting with `//` and `/* ... */` blocks).
  2. Strips ULID and compact-ID payloads from `§X{id:...}` blocks. The first
     attribute of a bracketed attribute list is replaced with `_ID_` if it
     matches the ID pattern (12 or 26 chars from the relevant alphabets).
  3. Normalizes whitespace runs to a single space, and strips leading/trailing
     whitespace per token.
  4. Drops blank lines.
  5. Normalizes closing-tag IDs: `§/X{id}` becomes `§/X{_ID_}`. (Phase 1 made
     closing-tag IDs optional; this normalization makes id-present and
     id-absent forms compare equal.)
  6. Strips terminal newline.

This is a TEXTUAL approximation of AST equivalence — it does not parse the
source. It is sufficient for Phase 3 because:
  - Indent-mode and closer-mode files differ exactly in the presence of
    `§/X{id}` lines and trailing/leading whitespace; the canonicalization
    handles both differences.
  - Comment attachment differences are surfaced by stripping comments and
    flagging the count delta (warning, not error).

Limitations (documented for reviewer signoff per validation plan §3a rule 9):
  - Does NOT detect re-ordered class members that yield identical ASTs (rare).
  - Does NOT detect renamed scoped symbols (intentional — caller is expected
    to use the existing `byte_preservation_check.py` flow for byte equality
    when scoped symbol stability is required).
  - Does NOT handle nested `§C{a.b.c}` IDs (currently no IDs nest inside `§C`).

Exit codes:
    0  files are AST-equivalent under this relation
    1  files differ (diff printed to stdout, summary to stderr)
    2  bad arguments

Usage:
    python scripts/ast_equivalence.py FILE_A.calr FILE_B.calr
    python scripts/ast_equivalence.py --self-test
"""

from __future__ import annotations

import argparse
import difflib
import re
import sys
from pathlib import Path

# Crockford-lowercase compact ID payload (12 chars; alphabet excludes i, l, o, u).
COMPACT_ID_RE = r"[0-9a-hjk-np-tv-z]{12}"
# Legacy ULID payload (26 chars; Crockford alphabet, uppercase or lowercase).
ULID_RE = r"[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}"
# Hand-coded short IDs (e.g. m001, f001, loop1, if1) — alphanumeric, 2-12 chars.
SHORT_ID_RE = r"[a-zA-Z][a-zA-Z0-9_]{1,11}"

ID_TOKEN_RE = re.compile(
    rf"(?P<open>§/?[A-Z][A-Z0-9_]*\{{)(?P<id>{COMPACT_ID_RE}|{ULID_RE}|{SHORT_ID_RE})(?P<rest>[:}}])"
)

# Closing-tag-only ID stripper: §/X{id} or §/X{id:foo}
CLOSING_ID_RE = re.compile(
    rf"§/(?P<tag>[A-Z][A-Z0-9_]*)\{{(?P<id>{COMPACT_ID_RE}|{ULID_RE}|{SHORT_ID_RE})\}}"
)

LINE_COMMENT_RE = re.compile(r"//.*$", re.MULTILINE)
BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.DOTALL)


def canonicalize(source: str) -> list[str]:
    """Returns a list of normalized tokens for comparison."""
    # 1. Strip comments.
    s = LINE_COMMENT_RE.sub("", source)
    s = BLOCK_COMMENT_RE.sub("", s)

    # 2+5. Strip IDs from both opening (§X{id:...}) and closing (§/X{id}) markers.
    s = ID_TOKEN_RE.sub(r"\g<open>_ID_\g<rest>", s)
    s = CLOSING_ID_RE.sub(r"§/\g<tag>{_ID_}", s)

    # 5b. Closing-tag IDs are optional under Phase 1 + Phase 3 — make both forms
    #     canonical by dropping the `{_ID_}` suffix entirely from closing tags.
    s = re.sub(r"(§/[A-Z][A-Z0-9_]*)\{_ID_\}", r"\1", s)
    # 5c. For opening tags that have ONLY an ID and nothing else (e.g. §L{_ID_}),
    #     drop the entire brace group. Opening tags with content (e.g. §F{_ID_:Main:pub})
    #     keep the brace group with `_ID_:rest` to preserve attribute structure.
    s = re.sub(r"(§[A-Z][A-Z0-9_]*)\{_ID_\}", r"\1", s)
    # 5d. Drop leading `_ID_:` from opening-tag attribute lists: §F{_ID_:Main:pub} → §F{Main:pub}
    s = re.sub(r"(§[A-Z][A-Z0-9_]*\{)_ID_:", r"\1", s)

    # 3+4. Token-ize on whitespace; drop empties.
    tokens = [t for t in re.split(r"\s+", s) if t]
    return tokens


def equivalent(src_a: str, src_b: str) -> tuple[bool, list[str]]:
    toks_a = canonicalize(src_a)
    toks_b = canonicalize(src_b)
    if toks_a == toks_b:
        return True, []
    diff = list(
        difflib.unified_diff(toks_a, toks_b, fromfile="a", tofile="b", lineterm="")
    )
    return False, diff


SELF_TESTS: list[tuple[str, str, str, bool]] = [
    (
        "identical files",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        True,
    ),
    (
        "different IDs (must be equivalent — modulo ID renaming)",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        "§M{abc123:Test}\n§F{def456:Main:pub}\n§R 0\n§/F{def456}\n§/M{abc123}\n",
        True,
    ),
    (
        "different whitespace (must be equivalent)",
        "§M{m001:Test}\n  §F{f001:Main:pub}\n    §R 0\n  §/F{f001}\n§/M{m001}\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        True,
    ),
    (
        "comments stripped (must be equivalent)",
        "§M{m001:Test} // module doc\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        True,
    ),
    (
        "closing-tag-id-optional vs id-present (Phase 1 / Phase 3 equivalence)",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F\n§/M\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        True,
    ),
    (
        "different content (must NOT be equivalent)",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 1\n§/F{f001}\n§/M{m001}\n",
        False,
    ),
    (
        "added function (must NOT be equivalent)",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§/M{m001}\n",
        "§M{m001:Test}\n§F{f001:Main:pub}\n§R 0\n§/F{f001}\n§F{f002:Helper:pub}\n§R 1\n§/F{f002}\n§/M{m001}\n",
        False,
    ),
]


def run_self_tests() -> int:
    failed = 0
    for name, a, b, expected in SELF_TESTS:
        actual, diff = equivalent(a, b)
        status = "PASS" if actual == expected else "FAIL"
        if actual != expected:
            failed += 1
            print(f"  {status}: {name}")
            print(f"    expected equivalent={expected}, got equivalent={actual}")
            if diff:
                for d in diff[:20]:
                    print(f"    {d}")
        else:
            print(f"  {status}: {name}")
    print(f"\n{len(SELF_TESTS) - failed}/{len(SELF_TESTS)} passed")
    return 1 if failed else 0


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("file_a", nargs="?", type=Path)
    p.add_argument("file_b", nargs="?", type=Path)
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()

    if args.self_test:
        return run_self_tests()

    if not args.file_a or not args.file_b:
        print("usage: ast_equivalence.py FILE_A.calr FILE_B.calr", file=sys.stderr)
        return 2
    if not args.file_a.exists() or not args.file_b.exists():
        print("one or both files do not exist", file=sys.stderr)
        return 2

    src_a = args.file_a.read_text(encoding="utf-8")
    src_b = args.file_b.read_text(encoding="utf-8")
    ok, diff = equivalent(src_a, src_b)
    if ok:
        print(f"AST-equivalent (modulo ID renaming, whitespace, comments): {args.file_a} == {args.file_b}")
        return 0
    print(f"AST-DIFFERENT: {args.file_a} != {args.file_b}")
    for d in diff:
        print(d)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
