#!/usr/bin/env python3
"""
Phase 5 — Documentation migration to indent-only Calor syntax.

Walks `docs/` and `website/content/` (and optionally agent-task CLAUDE.md
fixtures), finds fenced code blocks (``` ... ```) that contain `§` tags,
and rewrites them from closer-form (`§F{...} ... §/F`) to indent-only
form using `calor_indent_xform.to_indent`.

Default: dry-run. Pass `--apply` to write files in place.

Excludes:
  - docs/plans/** (historical RFCs / research notes)
  - benchmark snapshots in tests/Calor.Conversion.Tests/Snapshots/**

Special-cases:
  - Repairs the historical MDX corruption where `}` was replaced with `]`
    inside fenced code blocks (only affects 2 website MDX files).
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'scripts'))

from calor_indent_xform import to_indent, load_openers  # noqa: E402

OPENERS_MAP = load_openers()

INCLUDE_DIRS = [
    'docs',
    'website/content',
    'tests/E2E/agent-tasks/fixtures',
]

EXCLUDE_PATTERNS = [
    re.compile(r'(?:^|[/\\])docs[/\\]plans[/\\]'),
    re.compile(r'(?:^|[/\\])Snapshots[/\\]'),
    re.compile(r'(?:^|[/\\])bin[/\\]'),
    re.compile(r'(?:^|[/\\])obj[/\\]'),
]

# Languages on a fenced block that mean "this is NOT Calor source", skip.
NON_CALOR_LANGS = {
    'csharp', 'cs', 'c#', 'python', 'py', 'bash', 'shell', 'sh',
    'json', 'yaml', 'yml', 'xml', 'html', 'js', 'ts', 'javascript',
    'typescript', 'sql', 'powershell', 'ps1', 'toml', 'ini',
    'output', 'text', 'plaintext', 'console', 'mermaid', 'go', 'rust',
    'java', 'kotlin', 'cmd', 'env',
}

FENCE_RE = re.compile(r'^(?P<indent>[ \t]*)```(?P<lang>[\w#+.-]*)[ \t]*$', re.MULTILINE)
CLOSER_LIKE_RE = re.compile(r'§/')
SECTION_TAG_RE = re.compile(r'§[A-Z]')
BROKEN_BRACE_RE = re.compile(r'(§[A-Z][A-Z0-9_]*\{[^}\]]*?)\]')


def should_skip(path: Path) -> bool:
    s = str(path)
    return any(p.search(s) for p in EXCLUDE_PATTERNS)


def repair_mdx_braces(block: str) -> tuple[str, int]:
    """Fix `]` → `}` corruption inside § tags (historical MDX bug)."""
    fixed = block
    count = 0
    while True:
        new_fixed, n = BROKEN_BRACE_RE.subn(lambda m: m.group(1) + '}', fixed)
        if n == 0:
            break
        fixed = new_fixed
        count += n
    return fixed, count


def transform_block(block: str) -> tuple[str, str]:
    """Return (new_block, status) where status is one of:
       'transformed', 'already-indent', 'no-closers', 'skip-no-tags',
       'skip-failed'."""
    if not SECTION_TAG_RE.search(block):
        return block, 'skip-no-tags'

    repaired, n_repaired = repair_mdx_braces(block)

    if not CLOSER_LIKE_RE.search(repaired):
        if n_repaired > 0:
            return repaired, 'repaired-no-closers'
        return block, 'already-indent'

    try:
        new_block = to_indent(repaired, OPENERS_MAP)
    except Exception as e:
        sys.stderr.write(f"  ! transform failed: {e}\n")
        return block, 'skip-failed'

    if new_block.rstrip() == block.rstrip():
        return block, 'no-change'

    return new_block, 'transformed' if n_repaired == 0 else 'transformed+repaired'


def process_file(path: Path, apply: bool) -> dict:
    text = path.read_text(encoding='utf-8')
    out_lines: list[str] = []
    lines = text.splitlines(keepends=True)
    i = 0
    n = len(lines)
    stats = {'blocks': 0, 'transformed': 0, 'repaired': 0, 'skipped': 0, 'failed': 0}

    while i < n:
        line = lines[i]
        m = FENCE_RE.match(line.rstrip('\n'))
        if not m:
            out_lines.append(line)
            i += 1
            continue

        lang = (m.group('lang') or '').lower().strip()
        fence_indent = m.group('indent')
        # Find matching closing fence
        j = i + 1
        body: list[str] = []
        while j < n:
            close_match = re.match(r'^[ \t]*```\s*$', lines[j].rstrip('\n'))
            if close_match and lines[j].startswith(fence_indent):
                break
            body.append(lines[j])
            j += 1

        if j >= n:
            # Unclosed fence — pass through verbatim
            out_lines.append(line)
            i += 1
            continue

        # j is the closing fence
        if lang in NON_CALOR_LANGS:
            out_lines.append(line)
            out_lines.extend(body)
            out_lines.append(lines[j])
            i = j + 1
            continue

        stats['blocks'] += 1
        block_text = ''.join(body)
        # Dedent block to col 0 for the transformer, then re-indent
        if fence_indent:
            indent_len = len(fence_indent.expandtabs())
            dedented_lines = []
            for bl in body:
                stripped = bl
                if bl.startswith(fence_indent):
                    stripped = bl[len(fence_indent):]
                dedented_lines.append(stripped)
            dedented = ''.join(dedented_lines)
        else:
            indent_len = 0
            dedented = block_text

        new_block, status = transform_block(dedented)
        if status == 'transformed' or status == 'transformed+repaired':
            stats['transformed'] += 1
            if 'repaired' in status:
                stats['repaired'] += 1
        elif status.startswith('repaired'):
            stats['repaired'] += 1
            new_block = new_block
        elif status == 'skip-failed':
            stats['failed'] += 1
            new_block = dedented
        else:
            stats['skipped'] += 1
            new_block = dedented

        # Re-indent
        if fence_indent and new_block:
            new_block_lines = new_block.splitlines(keepends=True)
            new_block = ''.join(
                (fence_indent + ln if ln.strip() else ln) for ln in new_block_lines
            )

        out_lines.append(line)
        out_lines.append(new_block)
        if not new_block.endswith('\n') and len(new_block) > 0:
            out_lines.append('\n')
        out_lines.append(lines[j])
        i = j + 1

    new_text = ''.join(out_lines)
    if apply and new_text != text:
        path.write_text(new_text, encoding='utf-8')

    stats['changed'] = (new_text != text)
    return stats


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--apply', action='store_true', help='write changes in place')
    ap.add_argument('--only', help='regex of file paths to include')
    ap.add_argument('--include-dirs', nargs='+', default=INCLUDE_DIRS)
    args = ap.parse_args()

    only_re = re.compile(args.only) if args.only else None

    total = {'files': 0, 'changed': 0, 'blocks': 0, 'transformed': 0,
             'repaired': 0, 'skipped': 0, 'failed': 0}

    for d in args.include_dirs:
        base = ROOT / d
        if not base.exists():
            continue
        for ext in ('*.md', '*.mdx'):
            for path in base.rglob(ext):
                if should_skip(path):
                    continue
                rel = path.relative_to(ROOT).as_posix()
                if only_re and not only_re.search(rel):
                    continue
                stats = process_file(path, apply=args.apply)
                total['files'] += 1
                if stats['changed']:
                    total['changed'] += 1
                total['blocks'] += stats['blocks']
                total['transformed'] += stats['transformed']
                total['repaired'] += stats['repaired']
                total['skipped'] += stats['skipped']
                total['failed'] += stats['failed']
                if stats['transformed'] or stats['repaired'] or stats['failed']:
                    print(f"{rel}: blocks={stats['blocks']} "
                          f"xform={stats['transformed']} "
                          f"repaired={stats['repaired']} "
                          f"failed={stats['failed']} "
                          f"changed={stats['changed']}")

    mode = 'APPLY' if args.apply else 'DRY-RUN'
    print(f"\n[{mode}] files={total['files']} changed={total['changed']} "
          f"blocks={total['blocks']} xform={total['transformed']} "
          f"repaired={total['repaired']} skipped={total['skipped']} "
          f"failed={total['failed']}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
