#!/usr/bin/env python3
"""
Phase 3 — `calor fix --to-indent` + `--from-indent` PROTOTYPES (Python).

These are the Arm-I migrator scripts the validation plan §3a rules 1, 1b, 2
require. They operate at the text level (no AST) for now; an AST-based
re-implementation in C# is the eventual ship form.

TWO transforms:

  to_indent(closer_src) -> indent_src
    Re-indent the source so every block body is at parent_col + INDENT_UNIT,
    drop closer lines, preserve chain continuations (EI/EL/K/CA/FI/WHEN).

  from_indent(indent_src) -> closer_src
    Walk the indent structure, push openers on opener-line encountered,
    pop+emit `§/<closer>` on dedent below an open frame.

Round-trip:   from_indent(to_indent(X)) == X    modulo whitespace + ID payloads
              to_indent(from_indent(Y)) == Y    same modulo

This is the §3a rule 2 round-trip equivalence test.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OPENERS_JSON = ROOT / 'scripts' / 'phase3-openers.json'
INDENT_UNIT = 2  # spaces per nesting level in indent form

# Chain continuations: marker → required parent opener kw
CHAIN_CONTINUATIONS = {
    'EI':   'IF',
    'EL':   'IF',
    'K':    'W',
    'WHEN': 'W',
    'CA':   'TR',
    'FI':   'TR',
}

CHAIN_PARENTS = {'IF', 'W', 'TR'}

LINE_OPENER_RE = re.compile(r'^§([A-Z][A-Z0-9_]*)')
LINE_CLOSER_RE = re.compile(r'^§/([A-Z][A-Z0-9_]*)')
OPENER_ID_RE = re.compile(r'^§[A-Z][A-Z0-9_]*\{([^:}]+)(?::|\})')
INLINE_ARROW_RE = re.compile(r'→')


def extract_opener_id(line: str) -> str | None:
    """Pull the ID from an opener line: `§F{f001:Main:pub}` → 'f001'.
    Returns None if no ID present."""
    m = OPENER_ID_RE.match(line.lstrip())
    if not m:
        return None
    return m.group(1).strip()


def is_block_opener(kw: str, openers_map: dict[str, str], has_arrow: bool) -> bool:
    """A line-leading §<KW> is a BLOCK opener if it's in the dedent-closed
    set. Inline-arrow openers are NOT block-pushed UNLESS they're chain
    parents (IF/W/TR) — chain parents can host §EI/§EL siblings even when
    their own body uses →, and they're closed by §/I/§/W/§/TR on dedent."""
    if kw not in openers_map:
        return False
    if kw in CHAIN_PARENTS:
        return True
    return not has_arrow


def load_openers() -> dict[str, str]:
    """Return {opener_kw: closer_kw_without_slash}."""
    data = json.loads(OPENERS_JSON.read_text(encoding='utf-8'))
    return {
        o['opener']: o['closer'].lstrip('/')
        for o in data
        if o['treatment'] == 'dedent-closed'
    }


def closer_to_opener_map(openers_map: dict[str, str]) -> dict[str, str]:
    """Reverse: closer_kw -> opener_kw (the special case is /I → IF)."""
    rev: dict[str, str] = {}
    for op_kw, cl_kw in openers_map.items():
        rev[cl_kw] = op_kw
    return rev


def is_blank_or_comment(line: str) -> bool:
    s = line.strip()
    return not s or s.startswith('//')


def leading_col(line: str) -> int:
    n = 0
    for ch in line:
        if ch in (' ', '\t'):
            n += 1
        else:
            break
    return n


_CLOSER_NORMALIZE = {'IF': 'I'}  # opener-name-as-closer → canonical closer


def strip_id_payload(s: str) -> str:
    """For comparison:
    - `§/F{f001}` → `§/F`  (strip closer ID braces)
    - `§/IF` → `§/I`        (normalize long-form closer to canonical)
    """
    s = re.sub(r'(§/[A-Z][A-Z0-9_]*)\{[^}]*\}', r'\1', s)
    for long_kw, short_kw in _CLOSER_NORMALIZE.items():
        s = re.sub(r'§/' + long_kw + r'(?![A-Z0-9_])', '§/' + short_kw, s)
    return s


def canonical(src: str) -> str:
    """Normalize for round-trip comparison.
    - Strip ALL leading whitespace from each line (indent is structural,
      it gets rebuilt by the round-trip; we care about line CONTENT).
    - Collapse runs of blank lines to a single blank line.
    - Strip closer ID payloads (`§/F{f001}` ≡ `§/F`).
    - Trim leading/trailing blank lines.
    """
    lines = []
    prev_blank = False
    for raw in src.splitlines():
        s = strip_id_payload(raw).strip()
        if not s:
            if not prev_blank:
                lines.append('')
            prev_blank = True
        else:
            lines.append(s)
            prev_blank = False
    while lines and not lines[0].strip():
        lines.pop(0)
    while lines and not lines[-1].strip():
        lines.pop()
    return '\n'.join(lines)


def _next_nonblank_line(lines: list[str], start: int) -> tuple[int, str] | tuple[None, None]:
    for j in range(start + 1, len(lines)):
        if not is_blank_or_comment(lines[j]):
            return j, lines[j]
    return None, None


def _chain_continues(
    lines: list[str],
    idx: int,
    own_col: int,
    parent_kind: str,
) -> bool:
    """True if the next non-blank line at SAME col as own is a chain
    continuation belonging to `parent_kind`."""
    _, nxt = _next_nonblank_line(lines, idx)
    if nxt is None:
        return False
    if leading_col(nxt) != own_col:
        return False
    m = LINE_OPENER_RE.match(nxt.lstrip())
    if not m:
        return False
    kw = m.group(1)
    return CHAIN_CONTINUATIONS.get(kw) == parent_kind


def to_indent(closer_src: str, openers_map: dict[str, str]) -> str:
    """Reindent + drop closers."""
    out: list[str] = []
    stack: list[tuple[str, int]] = []
    lines = closer_src.splitlines()

    for idx, line in enumerate(lines):
        if is_blank_or_comment(line):
            out.append(line.rstrip())
            continue

        stripped = line.lstrip()
        own_col = leading_col(line)

        m_close = LINE_CLOSER_RE.match(stripped)
        if m_close:
            kw = m_close.group(1)
            matches_canonical = any(c == kw for c in openers_map.values())
            matches_self = kw in openers_map
            if matches_canonical or matches_self:
                found = False
                for i in range(len(stack) - 1, -1, -1):
                    okw = stack[i][0]
                    if openers_map.get(okw) == kw or okw == kw:
                        del stack[i:]
                        found = True
                        break
                if found:
                    continue
                # No matching opener on stack: this is an orphan closer
                # (e.g., §/I after a truly-inline §IF). Preserve as body so
                # it survives the round-trip.
            body_col = stack[-1][1] + INDENT_UNIT if stack else 0
            out.append(' ' * body_col + stripped.rstrip())
            continue

        m_open = LINE_OPENER_RE.match(stripped)
        if m_open:
            kw = m_open.group(1)
            if kw in CHAIN_CONTINUATIONS:
                parent_kind = CHAIN_CONTINUATIONS[kw]
                parent_col = 0
                for okw, ocol in reversed(stack):
                    if okw == parent_kind:
                        parent_col = ocol
                        break
                out.append(' ' * parent_col + stripped.rstrip())
                continue
            has_arrow = bool(INLINE_ARROW_RE.search(line))
            push = is_block_opener(kw, openers_map, has_arrow)
            # Chain-parent with inline arrow: only push if next sibling is
            # chain continuation. Otherwise treat as truly inline.
            if push and has_arrow and kw in CHAIN_PARENTS:
                if not _chain_continues(lines, idx, own_col, kw):
                    push = False
            if push:
                parent_col = stack[-1][1] + INDENT_UNIT if stack else 0
                out.append(' ' * parent_col + stripped.rstrip())
                stack.append((kw, parent_col))
                continue

        body_col = stack[-1][1] + INDENT_UNIT if stack else 0
        out.append(' ' * body_col + stripped.rstrip())

    return '\n'.join(out) + ('\n' if closer_src.endswith('\n') else '')


def from_indent(indent_src: str, openers_map: dict[str, str]) -> str:
    """Inject closers at dedents.

    Blank lines are BUFFERED and flushed AFTER any closers emitted by the
    next non-blank line, so that source like
        body
        <blank>
        next-sibling-opener
    round-trips to
        body
        §/F
        <blank>
        next-sibling-opener
    matching closer-form convention (closer hugs its block, blank separates
    siblings).
    """
    out: list[str] = []
    # stack frames: (opener_kw, indent_col, closer_kw, opener_id)
    stack: list[tuple[str, int, str, str | None]] = []
    pending_blanks: list[str] = []
    lines = indent_src.splitlines()

    def emit_close_for(top: tuple[str, int, str, str | None]) -> None:
        _, col, cl_kw, oid = top
        if oid:
            out.append(' ' * col + f'§/{cl_kw}{{{oid}}}')
        else:
            out.append(' ' * col + f'§/{cl_kw}')

    def flush_blanks() -> None:
        out.extend(pending_blanks)
        pending_blanks.clear()

    for idx, raw_line in enumerate(lines):
        if is_blank_or_comment(raw_line):
            pending_blanks.append(raw_line.rstrip())
            continue

        col = leading_col(raw_line)
        stripped = raw_line.lstrip()
        m_open = LINE_OPENER_RE.match(stripped)

        if m_open and m_open.group(1) in CHAIN_CONTINUATIONS:
            while stack and stack[-1][1] > col:
                emit_close_for(stack.pop())
            flush_blanks()
            out.append(raw_line.rstrip())
            continue

        while stack and stack[-1][1] >= col:
            emit_close_for(stack.pop())
        flush_blanks()

        if m_open:
            kw = m_open.group(1)
            has_arrow = bool(INLINE_ARROW_RE.search(raw_line))
            push = is_block_opener(kw, openers_map, has_arrow)
            if push and has_arrow and kw in CHAIN_PARENTS:
                if not _chain_continues(lines, idx, col, kw):
                    push = False
            if push:
                oid = extract_opener_id(stripped)
                stack.append((kw, col, openers_map[kw], oid))

        out.append(raw_line.rstrip())

    # EOF drain: pop all closes deeper than col 0, THEN flush buffered
    # blank lines (so trailing blanks land between sibling closes at col 0
    # — typically between §/F and §/M, matching closer-form convention),
    # THEN drain the col-0 closes.
    while stack and stack[-1][1] > 0:
        emit_close_for(stack.pop())
    flush_blanks()
    while stack:
        emit_close_for(stack.pop())

    return '\n'.join(out) + ('\n' if indent_src.endswith('\n') else '')


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', choices=['to-indent', 'from-indent'], required=True)
    ap.add_argument('input')
    ap.add_argument('-o', '--output')
    args = ap.parse_args()

    src = sys.stdin.read() if args.input == '-' else Path(args.input).read_text(encoding='utf-8')
    openers_map = load_openers()
    if args.mode == 'to-indent':
        out = to_indent(src, openers_map)
    else:
        out = from_indent(src, openers_map)

    if args.output:
        Path(args.output).write_text(out, encoding='utf-8')
    else:
        sys.stdout.write(out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
