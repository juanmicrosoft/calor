#!/usr/bin/env python3
"""0.25 W1 (#1429) wording scan. From the repository root:
    python3 docs/plans/evidence/v0.25-w1-1429/reproduce/wording_scan.py [paths...]
Runs R2's G012 scan (scripts/verify_release_adjudication.py scan_text) over the [Unreleased]
CHANGELOG section and every file passed, and also rejects the review-independence claim R0 §9 forbids. Exits 1 on
any hit. With no paths it scans the W1 pages and records."""
import glob
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
import verify_release_adjudication as gate  # noqa: E402

DEFAULT = [
    "website/content/guides/csharp-interoperability.mdx",
    "website/content/guides/adding-calor-to-existing-projects.mdx",
    "website/content/guides/index.mdx",
    "website/content/cli/convert.mdx",
    "website/content/cli/migrate.mdx",
    "website/content/cli/mcp.mdx",
    "website/content/cli/feature-check.mdx",
    "website/content/cli/self-check.mdx",
    "website/content/cli/structured-output.mdx",
    "website/content/syntax-reference/calls.mdx",
    "website/content/syntax-reference/string-operations.mdx",
    "docs/cli/feature-check.md",
] + sorted(os.path.relpath(p, ROOT) for p in glob.glob(os.path.join(ROOT, "docs/plans/evidence/v0.25-w1-1429/**/*.*"), recursive=True)
           if p.endswith((".md", ".json")))
REVIEWED = re.compile(r"(?<!not )(?<!never )independently\s+reviewed", re.IGNORECASE)


def main() -> int:
    g = gate.Gate(gate.Path(ROOT))
    changelog = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
    unreleased = changelog.split("## [Unreleased]", 1)[1].split("\n## [", 1)[0]
    targets = [("CHANGELOG.md [Unreleased]", unreleased)]
    for path in sys.argv[1:] or DEFAULT:
        targets.append((path, open(os.path.join(ROOT, path), encoding="utf-8").read()))
    hits = 0
    for label, text in targets:
        gate.scan_text(g, label, text)
        for m in REVIEWED.finditer(text):
            print(f"{label}: '{m.group(0)}'")
            hits += 1
    for error in g.errors:
        print(error)
        hits += 1
    print(f"scanned {len(targets)} texts; {hits} finding(s)")
    return 1 if hits else 0


if __name__ == "__main__":
    sys.exit(main())
