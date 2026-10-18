#!/usr/bin/env python3
"""Render the release notes: the `## [VERSION]` section of CHANGELOG.md (#1546).

publish-nuget.yml renders the notes with this script, and scripts/verify_release_adjudication.py
checks the candidate's section with the same function (changelog_section), so the notes the
workflow publishes and the section the gate binds to the identity (G014) cannot differ.
The version is matched literally. A missing section, or a version header that appears more than
once, is an error.

usage: render_release_notes.py VERSION CHANGELOG.md > notes.md
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_release_adjudication import changelog_section, version_headers  # noqa: E402


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__.strip().splitlines()[-1], file=sys.stderr)
        return 2
    version, path = argv
    text = Path(path).read_bytes().decode("utf-8")
    if version_headers(text, version) != 1:
        print(f"::error::{path} must hold exactly one '## [{version}]' header", file=sys.stderr)
        return 1
    section = changelog_section(text, version)
    sys.stdout.write(section.rstrip() + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
