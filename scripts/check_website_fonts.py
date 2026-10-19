#!/usr/bin/env python3
"""The website serves only self-hosted fonts and its build fetches nothing from Google Fonts (#1548).

Checks, each failing closed:

  F001  a source file names next/font/google, fonts.googleapis.com, or fonts.gstatic.com
        (website/src, website/content, website/public, and the build configuration; the
        provenance URLs in website/src/fonts/fonts.json are exempt);
  F002  a vendored font file is missing, extra, or differs from website/src/fonts/fonts.json;
  F003  a vendored font family has no OFL.txt beside it;
  F004  (with --out) a built HTML, CSS, RSC payload, or data file names a Google Fonts host;
  F005  (with --out) the built CSS declares no @font-face, or one whose src is not a relative
        /_next/static/media/ woff2 that exists in the build.

Next.js's own runtime chunks contain the two host names as constants (its Google Fonts <link>
optimization); they act only on a <link> to those hosts, which F004 rules out, so built .js is
not scanned. That the build itself makes no request is shown separately: website-tests.yml builds
with both hosts unresolvable.

Usage: scripts/check_website_fonts.py [--website website] [--out website/out]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

HOSTS = re.compile(r"next/font/google|fonts\.googleapis\.com|fonts\.gstatic\.com", re.IGNORECASE)
SOURCE_DIRS = ("src", "content", "public")
SOURCE_FILES = ("next.config.js", "tailwind.config.ts", "postcss.config.js", "package.json")
TEXT = {".ts", ".tsx", ".js", ".mjs", ".cjs", ".jsx", ".css", ".md", ".mdx", ".json", ".html", ".txt", ".xml"}
BUILT_TEXT = {".html", ".htm", ".css", ".txt", ".json", ".xml", ".rss", ".webmanifest"}
FONT_FACE = re.compile(r"@font-face\s*\{([^}]*)\}")
FACE_URL = re.compile(r"url\(([^)]+)\)")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_source(website: Path) -> list[str]:
    problems = []
    files = [website / name for name in SOURCE_FILES if (website / name).is_file()]
    for name in SOURCE_DIRS:
        files += [p for p in sorted((website / name).rglob("*")) if p.is_file() and p.suffix in TEXT]
    for path in files:
        if path == website / "src" / "fonts" / "fonts.json":
            continue  # the vendored files' provenance: the URLs they were fetched from
        for line_no, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            if HOSTS.search(line):
                problems.append(f"F001: {path.relative_to(website)}:{line_no} names {HOSTS.search(line).group(0)}")
    fonts = website / "src" / "fonts"
    manifest_path = fonts / "fonts.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))["files"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        return problems + [f"F002: cannot read {manifest_path.relative_to(website)}: {error}"]
    expected = {entry["file"]: entry["sha256"] for entry in manifest}
    actual = {p.relative_to(website).as_posix(): sha256(p) for p in sorted(fonts.rglob("*.woff2"))}
    for name in sorted(set(expected) | set(actual)):
        if name not in actual:
            problems.append(f"F002: {name} is listed in fonts.json but missing")
        elif name not in expected:
            problems.append(f"F002: {name} is not listed in fonts.json")
        elif actual[name] != expected[name]:
            problems.append(f"F002: {name} has sha256 {actual[name]}, fonts.json says {expected[name]}")
    for family in sorted({Path(name).parent for name in expected}):
        if not (website / family / "OFL.txt").is_file():
            problems.append(f"F003: {family} has no OFL.txt")
    return problems


def check_out(out: Path) -> list[str]:
    if not out.is_dir():
        return [f"F005: no build at {out}"]
    problems, faces = [], 0
    for path in sorted(out.rglob("*")):
        if not path.is_file() or path.suffix not in BUILT_TEXT:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        found = HOSTS.search(text)
        if found:
            problems.append(f"F004: {path.relative_to(out)} names {found.group(0)}")
        if path.suffix != ".css":
            continue
        for face in FONT_FACE.findall(text):
            faces += 1
            urls = FACE_URL.findall(face)
            if not urls and "local(" in face:
                continue  # next/font's metric-adjusted fallback face: src: local("Arial")
            for url in urls or ["<none>"]:
                url = url.strip("'\"")
                # A relative path under the (optional) base path, e.g. /calor/_next/static/media/x.woff2.
                local = re.fullmatch(r"(?:/[A-Za-z0-9_-]+)?/(_next/static/media/[A-Za-z0-9._-]+\.woff2)", url)
                if not local:
                    problems.append(f"F005: {path.relative_to(out)} @font-face src {url} is not a self-hosted woff2")
                elif not (out / local.group(1)).is_file():
                    problems.append(f"F005: {path.relative_to(out)} @font-face src {url} is missing from the build")
    if faces == 0:
        problems.append("F005: the built CSS declares no @font-face")
    return problems


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--website", default=str(Path(__file__).resolve().parent.parent / "website"))
    parser.add_argument("--out", help="built site (website/out) to check as well")
    args = parser.parse_args(argv)
    problems = check_source(Path(args.website))
    if args.out:
        problems += check_out(Path(args.out))
    for problem in problems:
        print(f"::error::website fonts (#1548) {problem}")
    if problems:
        print(f"WEBSITE FONTS: FAIL ({len(problems)} problem(s))")
        return 1
    print("WEBSITE FONTS: PASS (self-hosted only" + (", source and build)" if args.out else ", source)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
