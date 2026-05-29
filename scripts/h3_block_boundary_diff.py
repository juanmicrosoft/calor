#!/usr/bin/env python3
"""
h3_block_boundary_diff.py — H3 silent-bug instrument per validation plan §5.2.5.

For a paired (Arm I output, matched-seed Arm 0 output) computes:
  - block-scope-boundary differences (statements with different enclosing block chain)
  - severity classification (execution-affecting vs cosmetic-only)
  - per-trial counts to feed §5.2.5's median / p90 aggregates

A **block-scope-boundary difference** is any statement that has a different
chain of enclosing block-markers in ast_I vs ast_0 (after canonicalization).

**Severity classification** (validation plan §5.2.5):
  - `execution-affecting`: the differing enclosing block is a control-flow
    construct whose body membership changes semantics — function (§F, §AF,
    §AMT, §MT), loop (§L, §EACH, §EACHKV, §DO, §W), if-arm (§IF, §EI, §EL),
    try-block (§TR, §CA, §FI), method (§CTOR, §GET, §SET, §INIT, §OP),
    while (§WH). **Zero tolerance.**
  - `cosmetic-only`: the differing enclosing block is a structural-only
    container (§M module, §CL class, §IFACE, §EN enum, etc.) — or the
    difference is in comment-attachment, blank-line, or trailing-whitespace
    only.

Algorithm:
  1. For each file, build a list of (statement_signature, block_chain) pairs.
     Statement signature uses the canonicalization from `ast_equivalence.py`
     (strips IDs / comments / whitespace).
     Block chain is the sequence of OPENED-but-not-yet-CLOSED markers above
     this statement in the file, in source order. For closer-mode files,
     blocks open at §X{...} and close at §/X. For indent-mode files,
     a future iteration will add indent-based block tracking; v1 of this
     script handles closer-mode only and reports `indent-mode-not-supported`
     if it sees more dedent-implied blocks than explicit closers.
  2. Align statement lists across files via signature matching (a statement
     in file A is paired with the statement in file B that has the same
     signature and appears in the same ordinal position among
     same-signature statements).
  3. For each aligned statement pair, compare block chains. If different,
     classify based on the FIRST differing block's kind.

Limitations (per validation plan §3a rule 9 — to be flagged in reviewer signoff):
  - Statement-signature matching is heuristic. Re-ordered same-signature
    statements may produce false-positive boundary differences. Mitigation:
    paired (trial, seed) outputs come from the same agent + same seed; the
    only expected differences are arm-induced (closer-mode vs indent-mode).
  - Indent-mode parsing is delegated to a future iteration. v1 detects
    indent-mode input and exits with code 3 (`indent-mode-not-supported`).
  - Does not detect re-ordered class members across `§CL` boundaries
    (intentional — `§CL` is a structural-only container; member re-ordering
    is `cosmetic-only` per §5.2.5).

Exit codes:
  0   no boundary differences detected
  1   boundary differences detected — see JSON output for severity counts
  2   bad arguments
  3   one or both inputs use indent-mode (not yet supported by v1)

Usage:
  python scripts/h3_block_boundary_diff.py FILE_I.calr FILE_0.calr [--json out.json]
  python scripts/h3_block_boundary_diff.py --self-test
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

# Import canonicalization from ast_equivalence.py via relative path.
_SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(_SCRIPT_DIR))
from ast_equivalence import canonicalize, LINE_COMMENT_RE, BLOCK_COMMENT_RE  # noqa: E402

# Tags whose bodies are control-flow / execution-affecting.
EXECUTION_AFFECTING_TAGS = {
    # Functions
    "F", "AF", "AMT", "MT",
    # Loops
    "L", "EACH", "EACHKV", "DO", "W", "WH",
    # Conditionals (the chain marker §IF opens an if-block; the body is
    # everything until the matching §/I)
    "IF",
    # Try / Catch / Finally
    "TR",
    # Methods & accessors
    "CTOR", "GET", "SET", "INIT", "OP", "IXER",
    # Async-method-task wrappers
    "SYNC", "USE", "WITH",
}

# Tags whose bodies are structural-only containers — body-membership changes
# at this level are NOT statement-execution-affecting.
STRUCTURAL_TAGS = {
    "M", "CL", "IFACE", "EN", "EVT", "ITYPE", "RTYPE", "DEL", "EEXT",
    "PROP", "ARR", "ARR2D", "DICT", "LIST", "HSET", "FIXED", "UNSAFE",
    "HD", "VS", "CT", "DC", "SALLOC", "EADD", "EREM",
}

# Mid-block chain continuations — do NOT open a new block, do NOT close one.
CHAIN_CONTINUATIONS = {"EI", "EL", "CA", "FI", "K", "WHEN"}

# Opening-tag pattern: §X{...} or §X (no brace). Captures the tag name only.
# Closing-tag pattern: §/X{...} or §/X.
OPEN_TAG_RE = re.compile(r"§(?P<tag>[A-Z][A-Z0-9_]*)(?:\{[^}]*\})?")
CLOSE_TAG_RE = re.compile(r"§/(?P<tag>[A-Z][A-Z0-9_]*)(?:\{[^}]*\})?")


@dataclass
class StatementOccurrence:
    signature: str
    block_chain: tuple[str, ...]
    line_number: int


@dataclass
class BoundaryDiff:
    signature: str
    chain_a: tuple[str, ...]
    chain_b: tuple[str, ...]
    differing_tag: str
    severity: str  # "execution-affecting" or "cosmetic-only"


@dataclass
class Result:
    execution_affecting: list[BoundaryDiff] = field(default_factory=list)
    cosmetic_only: list[BoundaryDiff] = field(default_factory=list)
    unmatched_a: list[str] = field(default_factory=list)
    unmatched_b: list[str] = field(default_factory=list)


def _is_indent_mode(source: str) -> bool:
    """Heuristic: indent-mode is detected by counting §X{...} openers (body-bearing)
    against §/X closers. If there are markedly more openers than closers AND the
    deficit lines after openers are non-empty / non-comment (i.e., a body is
    present), the file is in indent-mode.
    """
    s = LINE_COMMENT_RE.sub("", source)
    s = BLOCK_COMMENT_RE.sub("", s)
    opens = 0
    closes = 0
    for m in OPEN_TAG_RE.finditer(s):
        tag = m.group("tag")
        if tag in CHAIN_CONTINUATIONS:
            continue
        # Skip likely-expression-context opens (heuristic: §C, §D, §I one-liners,
        # §THIS, §BASE, §NEW, §ANON, §INTERP, §LAM, §US, §UB)
        if tag in {"C", "D", "I", "THIS", "BASE", "NEW", "ANON", "INTERP", "LAM", "US", "UB", "T"}:
            continue
        # Skip if this match is actually a closer (handled separately).
        opens += 1
    closes = len(CLOSE_TAG_RE.findall(s))
    # Heuristic: closer-mode files have closes >= opens - K where K is small
    # (allowing for some niche markers). Indent-mode would have closes ~= 0.
    if opens > 5 and closes < opens // 3:
        return True
    return False


def _process_line_for_chain(
    line: str,
    chain: list[str],
    indent_mode: bool,
) -> tuple[bool, str | None]:
    """Walk all tag-tokens in a line in order and update `chain` in-place.
    Returns (is_pure_marker_line, error). `is_pure_marker_line` is True if the
    line has no non-marker / non-whitespace content.
    """
    if indent_mode:
        return False, "indent-mode-not-supported"

    # Strip comments from the line for marker scanning.
    s = LINE_COMMENT_RE.sub("", line)

    # Process tags in source order. We can't reuse OPEN_TAG_RE then CLOSE_TAG_RE
    # because they don't preserve order; scan with combined regex instead.
    combined_re = re.compile(
        r"§(?P<slash>/)?(?P<tag>[A-Z][A-Z0-9_]*)(?:\{[^}]*\})?"
    )
    has_marker = False
    leftover = s
    pos = 0
    for m in combined_re.finditer(s):
        has_marker = True
        tag = m.group("tag")
        is_close = m.group("slash") == "/"
        # Strip out the matched text from leftover for "pure marker line" check.
        if is_close:
            # Close: pop the matching tag from the chain.
            # Special case: §/I closes the §IF chain.
            close_target = "IF" if tag == "I" else tag
            # Pop until we find the matching open (handles intermediate chain
            # continuations that were not pushed).
            if close_target in chain:
                while chain and chain[-1] != close_target:
                    chain.pop()
                if chain:
                    chain.pop()
        else:
            # Open: push only if this is a body-bearing block (not a chain
            # continuation, not an expression-form tag).
            if tag in CHAIN_CONTINUATIONS:
                continue
            if tag in EXECUTION_AFFECTING_TAGS or tag in STRUCTURAL_TAGS:
                chain.append(tag)
            # else: expression-form / inline / vestigial tag — ignore

    # Remove all marker substrings to test "pure marker line".
    leftover_no_markers = combined_re.sub("", s).strip()
    is_pure_marker = has_marker and not leftover_no_markers
    return is_pure_marker, None


def build_occurrences(source: str) -> tuple[list[StatementOccurrence], str | None]:
    if _is_indent_mode(source):
        return [], "indent-mode-not-supported"

    occurrences: list[StatementOccurrence] = []
    chain: list[str] = []
    for ln, raw_line in enumerate(source.splitlines(), start=1):
        is_pure_marker, err = _process_line_for_chain(raw_line, chain, False)
        if err:
            return [], err

        # If line has non-marker content, treat that content as a statement
        # occurrence inside the current block chain.
        no_comments = LINE_COMMENT_RE.sub("", raw_line)
        combined_re = re.compile(
            r"§(?P<slash>/)?(?P<tag>[A-Z][A-Z0-9_]*)(?:\{[^}]*\})?"
        )
        leftover = combined_re.sub("", no_comments).strip()
        if leftover:
            # Canonicalize the leftover statement text.
            sig_tokens = canonicalize(leftover)
            if sig_tokens:
                sig = " ".join(sig_tokens)
                occurrences.append(
                    StatementOccurrence(
                        signature=sig,
                        block_chain=tuple(chain),
                        line_number=ln,
                    )
                )
    return occurrences, None


def diff_occurrences(
    occ_a: list[StatementOccurrence],
    occ_b: list[StatementOccurrence],
) -> Result:
    """Align by (signature, ordinal-within-signature) and diff block chains."""
    result = Result()

    # Group by signature; align by index.
    from collections import defaultdict
    grouped_a: dict[str, list[StatementOccurrence]] = defaultdict(list)
    grouped_b: dict[str, list[StatementOccurrence]] = defaultdict(list)
    for o in occ_a:
        grouped_a[o.signature].append(o)
    for o in occ_b:
        grouped_b[o.signature].append(o)

    all_sigs = set(grouped_a) | set(grouped_b)
    for sig in all_sigs:
        list_a = grouped_a.get(sig, [])
        list_b = grouped_b.get(sig, [])
        n = min(len(list_a), len(list_b))
        for i in range(n):
            chain_a = list_a[i].block_chain
            chain_b = list_b[i].block_chain
            if chain_a == chain_b:
                continue
            # Find the first differing tag.
            differing_tag = None
            for ta, tb in zip(chain_a, chain_b):
                if ta != tb:
                    differing_tag = ta if ta else tb
                    break
            if differing_tag is None:
                # One chain is a prefix of the other; use the first extra tag.
                if len(chain_a) > len(chain_b):
                    differing_tag = chain_a[len(chain_b)]
                else:
                    differing_tag = chain_b[len(chain_a)]
            severity = (
                "execution-affecting"
                if differing_tag in EXECUTION_AFFECTING_TAGS
                else "cosmetic-only"
            )
            bd = BoundaryDiff(
                signature=sig,
                chain_a=chain_a,
                chain_b=chain_b,
                differing_tag=differing_tag,
                severity=severity,
            )
            if severity == "execution-affecting":
                result.execution_affecting.append(bd)
            else:
                result.cosmetic_only.append(bd)
        # Unmatched extras.
        for extra in list_a[n:]:
            result.unmatched_a.append(extra.signature)
        for extra in list_b[n:]:
            result.unmatched_b.append(extra.signature)
    return result


SELF_TESTS: list[tuple[str, str, str, int, int]] = [
    # (name, src_a, src_b, expected_execution_affecting, expected_cosmetic)
    (
        "identical files — 0 diffs",
        "§M{m1:T}\n§F{f1:Main:pub}\n  print 1\n§/F{f1}\n§/M{m1}\n",
        "§M{m1:T}\n§F{f1:Main:pub}\n  print 1\n§/F{f1}\n§/M{m1}\n",
        0, 0,
    ),
    (
        "statement moved out of loop (execution-affecting)",
        "§M{m1:T}\n§F{f1:Main:pub}\n§L{l1:i:0:10}\n  print x\n§/L{l1}\n§/F{f1}\n§/M{m1}\n",
        "§M{m1:T}\n§F{f1:Main:pub}\n§L{l1:i:0:10}\n§/L{l1}\nprint x\n§/F{f1}\n§/M{m1}\n",
        1, 0,
    ),
    (
        "statement moved out of class (cosmetic-only)",
        "§M{m1:T}\n§CL{c1:Foo:pub}\n  bar = 0\n§/CL{c1}\n§/M{m1}\n",
        "§M{m1:T}\n§CL{c1:Foo:pub}\n§/CL{c1}\nbar = 0\n§/M{m1}\n",
        0, 1,
    ),
    (
        "if-arm body change (execution-affecting)",
        "§M{m1:T}\n§F{f1:Main:pub}\n§IF{if1} (x > 0)\n  print yes\n§/I{if1}\n§/F{f1}\n§/M{m1}\n",
        "§M{m1:T}\n§F{f1:Main:pub}\n§IF{if1} (x > 0)\n§/I{if1}\nprint yes\n§/F{f1}\n§/M{m1}\n",
        1, 0,
    ),
]


def run_self_tests() -> int:
    failed = 0
    for name, a, b, exp_exec, exp_cos in SELF_TESTS:
        occ_a, err_a = build_occurrences(a)
        occ_b, err_b = build_occurrences(b)
        if err_a or err_b:
            print(f"  FAIL: {name} — error: {err_a or err_b}")
            failed += 1
            continue
        r = diff_occurrences(occ_a, occ_b)
        actual_exec = len(r.execution_affecting)
        actual_cos = len(r.cosmetic_only)
        ok = actual_exec == exp_exec and actual_cos == exp_cos
        status = "PASS" if ok else "FAIL"
        print(f"  {status}: {name}")
        if not ok:
            failed += 1
            print(f"    expected exec={exp_exec} cosmetic={exp_cos}; "
                  f"got exec={actual_exec} cosmetic={actual_cos}")
            for bd in r.execution_affecting + r.cosmetic_only:
                print(f"    [{bd.severity}] sig={bd.signature!r} "
                      f"chain_a={bd.chain_a} chain_b={bd.chain_b}")
    print(f"\n{len(SELF_TESTS) - failed}/{len(SELF_TESTS)} passed")
    return 1 if failed else 0


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("file_i", nargs="?", type=Path, help="Arm I output (.calr)")
    p.add_argument("file_0", nargs="?", type=Path, help="Arm 0 output (.calr)")
    p.add_argument("--json", type=Path, help="emit JSON report to file")
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()

    if args.self_test:
        return run_self_tests()

    if not args.file_i or not args.file_0:
        print("usage: h3_block_boundary_diff.py FILE_I.calr FILE_0.calr [--json OUT]",
              file=sys.stderr)
        return 2
    if not args.file_i.exists() or not args.file_0.exists():
        print("one or both files do not exist", file=sys.stderr)
        return 2

    src_i = args.file_i.read_text(encoding="utf-8")
    src_0 = args.file_0.read_text(encoding="utf-8")

    occ_i, err_i = build_occurrences(src_i)
    occ_0, err_0 = build_occurrences(src_0)
    if err_i == "indent-mode-not-supported" or err_0 == "indent-mode-not-supported":
        print("indent-mode input detected; v1 of this script supports closer-mode only",
              file=sys.stderr)
        return 3
    if err_i or err_0:
        print(f"build error: {err_i or err_0}", file=sys.stderr)
        return 2

    r = diff_occurrences(occ_i, occ_0)
    report = {
        "file_i": str(args.file_i),
        "file_0": str(args.file_0),
        "execution_affecting_count": len(r.execution_affecting),
        "cosmetic_only_count": len(r.cosmetic_only),
        "unmatched_a_count": len(r.unmatched_a),
        "unmatched_b_count": len(r.unmatched_b),
        "execution_affecting": [
            {"signature": d.signature, "chain_a": list(d.chain_a),
             "chain_b": list(d.chain_b), "differing_tag": d.differing_tag}
            for d in r.execution_affecting
        ],
        "cosmetic_only": [
            {"signature": d.signature, "chain_a": list(d.chain_a),
             "chain_b": list(d.chain_b), "differing_tag": d.differing_tag}
            for d in r.cosmetic_only
        ],
    }
    if args.json:
        args.json.write_text(json.dumps(report, indent=2), encoding="utf-8")
    else:
        print(json.dumps(report, indent=2))

    total = len(r.execution_affecting) + len(r.cosmetic_only)
    return 0 if total == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
