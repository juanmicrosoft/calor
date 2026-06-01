#!/usr/bin/env python3
"""Regenerate website MDX mirrors from the docs/ source of truth."""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# (md_path, mdx_path, mdx_frontmatter, jekyll_title)
PAGES = [
    ('docs/syntax-reference/structure-tags.md',
     'website/content/syntax-reference/structure-tags.mdx',
     '---\ntitle: "Structure Tags"\nsection: "syntax-reference"\norder: 1\n---\n\n',
     'Structure Tags'),
    ('docs/syntax-reference/index.md',
     'website/content/syntax-reference/index.mdx',
     '---\ntitle: "Syntax Reference"\nsection: "syntax-reference"\norder: 0\nhasChildren: true\n---\n\n\nComplete reference for Calor syntax. Calor uses Lisp-style expressions for all operations.\n\n> **Why this syntax?** Calor\'s notation is optimized for AI agents, not human aesthetics. You don\'t need to learn this syntax—AI coding agents write Calor, not humans. This reference exists to help you understand what the AI generates and verify that contracts match your intent. Each design choice serves verification: stable IDs enable precise references, indentation eliminates scope ambiguity, and Lisp-style expressions allow direct AST manipulation.\n\n---\n\n',
     'Syntax Reference'),
]


def regen(md_path, mdx_path, new_front, title):
    src = (ROOT / md_path).read_text(encoding='utf-8')
    # Strip Jekyll frontmatter + duplicate title H1
    fm_pattern = re.compile(
        rf"^---\n.*?\n---\n\n# {re.escape(title)}\n\n",
        re.DOTALL,
    )
    src = fm_pattern.sub(new_front, src, count=1)
    # Adjust Jekyll-style links
    src = src.replace('](/calor/', '](/')
    (ROOT / mdx_path).write_text(src, encoding='utf-8', newline='\n')
    print(f"regenerated {mdx_path} ({len(src)} bytes)")


if __name__ == '__main__':
    for md, mdx, fm, title in PAGES:
        regen(md, mdx, fm, title)
