#!/usr/bin/env python3
"""Update agent-task CLAUDE.md fixtures to teach indent-only Calor."""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / 'tests' / 'E2E' / 'agent-tasks' / 'fixtures'

INDENT_PREAMBLE = """## Calor Syntax Reference

This is a Calor project. Write code in `.calr` files.

### Block Structure (Indentation)

Calor blocks are delimited by **indentation** (like Python), with the
default of **2 spaces per nesting level**. There are **no `§/X` closing
tags** to add; a block ends at the next line that dedents back to (or
past) the parent column. Stable IDs are **optional** on structural
openers — provide one (`§F{f001:Name:pub}`) only if you need a handle
for external tooling. Otherwise `§F{Name:pub}` is fine and the parser
auto-assigns an ID.

### Function Syntax
```
§F{Name:pub}
  §I{type:name}      // Input parameter
  §O{type}           // Output/return type
  §Q (condition)     // Precondition (requires)
  §S (condition)     // Postcondition (ensures)
  §E{effects}        // Effects declaration
  §R expression      // Return
```
"""

RENAMES = [
    # ID-required prose → ID-optional prose
    (
        r'- Unique IDs \(f001, v001, etc\.\) should NOT change during rename\n- Only the human-readable name changes\n',
        '- If a function/variable has an explicit stable ID, do NOT change it during rename\n'
        '- Only the human-readable name changes; the auto-assigned ID (when present) is invisible to the file\n',
    ),
    (
        r'Each function has a unique ID \(e\.g\., f001, f002\)\. When extracting new functions:\n'
        r'- Original functions keep their IDs\n'
        r'- New extracted functions should use the next available ID \(f005, f006, etc\.\)\n'
        r'- IDs enable stable references across refactorings\n',
        'Stable IDs are **optional** on structural openers. When extracting new functions:\n'
        '- Existing functions keep any explicit ID they already have\n'
        '- New extracted functions may omit the ID (`§F{Name:pub}`) and the parser will auto-assign one\n'
        '- Add `§F{f005:Name:pub}` only if you want an external-tooling handle\n',
    ),
    (
        r'1\. Don\'t change the function ID\n',
        "1. If the function has an explicit ID, don't change it\n",
    ),
]


def fix(p: Path) -> bool:
    text = p.read_text(encoding='utf-8')
    orig = text

    # Drop §F{id:Name:pub} → §F{Name:pub} everywhere (id: is now optional)
    text = re.sub(r'§F\{id:Name:pub\}', '§F{Name:pub}', text)
    text = re.sub(r'§F\{id:([A-Za-z_][A-Za-z0-9_]*):([a-z]+)\}',
                  r'§F{\1:\2}', text)

    # Replace the header + Function Syntax block with the new preamble.
    # Match from "## Calor Syntax Reference" up to and including the
    # function-syntax fenced block.
    syntax_block_re = re.compile(
        r'^## Calor Syntax Reference\n.*?### Function Syntax\n```\n.*?```\n',
        re.DOTALL,
    )
    if syntax_block_re.search(text):
        text = syntax_block_re.sub(INDENT_PREAMBLE, text, count=1)

    for pat, repl in RENAMES:
        text = re.sub(pat, repl, text)

    if text != orig:
        p.write_text(text, encoding='utf-8', newline='\n')
        return True
    return False


def main():
    changed = 0
    for p in FIXTURES.rglob('CLAUDE.md'):
        # Only calor fixtures
        if 'csharp' in p.parent.name:
            continue
        if fix(p):
            print(f"updated {p.relative_to(ROOT).as_posix()}")
            changed += 1
    print(f"\n{changed} files updated")


if __name__ == '__main__':
    main()
