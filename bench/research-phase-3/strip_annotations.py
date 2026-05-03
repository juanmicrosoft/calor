#!/usr/bin/env python3
"""Strip Phase 0 annotation comments from .cs files in a directory tree.

Removes lines like:
    // PURE: ...
    // EFFECTS: ...
    // PRECONDITION: ...
    // POSTCONDITION: ...
along with the immediately preceding blank line if present.
Same logic as bench/research-phase-0/csharp-bare strip pattern.
"""
import re
import sys
from pathlib import Path

ANNOTATION_RE = re.compile(
    r"^\s*//\s*(PURE|EFFECTS|PRECONDITION|POSTCONDITION):.*$"
)

def strip(text: str) -> str:
    lines = text.splitlines(keepends=True)
    out = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if ANNOTATION_RE.match(line):
            # If previous emitted line was blank, drop it as well
            if out and out[-1].strip() == "":
                out.pop()
            # Skip this annotation line
            i += 1
            # Drop any continuation comment lines that look like part of the same block
            while i < len(lines) and ANNOTATION_RE.match(lines[i]):
                i += 1
            continue
        out.append(line)
        i += 1
    return "".join(out)

def main():
    root = Path(sys.argv[1])
    count = 0
    for p in root.rglob("*.cs"):
        if "/bin/" in str(p).replace("\\", "/") or "/obj/" in str(p).replace("\\", "/"):
            continue
        text = p.read_text(encoding="utf-8")
        stripped = strip(text)
        if stripped != text:
            p.write_text(stripped, encoding="utf-8")
            count += 1
            print(f"stripped {p}")
    print(f"\n{count} file(s) modified")

if __name__ == "__main__":
    main()
