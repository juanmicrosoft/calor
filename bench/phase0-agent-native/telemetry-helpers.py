#!/usr/bin/env python3
"""Helpers for loop-telemetry v2 emission (loop plan D4.2).

Invoked by the dotnet PATH shim written by run-pair.sh. Two subcommands:

  edit-targets <prev_file|-> <cur_file> <ids_json>
      Prints a JSON array of declaration IDs whose content region changed
      between <prev_file> and <cur_file>. <ids_json> is the output of
      `calor ids index <cur_file> -o <ids_json>`: an array of
      {id, kind, name, file, line} entries for the CURRENT file.
      Pass "-" as <prev_file> when there is no previous copy (new file):
      every ID in the file is reported.

      Precision limits (documented per D4.2): attribution is line-range
      based, not AST-diff based. A declaration's span is approximated as
      [its own line, the line before the next indexed declaration] (the
      last declaration extends to EOF). Consequences:
        - edits in whitespace/comments between declarations attribute to
          the preceding declaration;
        - nested declarations (e.g. a module containing functions)
          attribute to the innermost declaration whose span covers the
          changed lines, because inner declaration lines shadow the outer
          span under this scheme;
        - pure deletions attribute to the declaration containing the
          deletion point in the current file;
        - a renamed/re-ID'd declaration is reported under its NEW id.
      This over-approximates slightly but never silently drops a changed
      declaration, which is the property M-L3 joins need.

  envelope <envelope_json_file>
      Reads a `calor --format json` output document and prints one JSON
      object: {"diagnostics": [{code, declarationId?} ...] (<=50),
      "diagnostics_truncated": bool, "envelope_valid": bool}.
      envelope_valid is the minimal D4.2 check: the document parses as
      JSON and carries a top-level "version" field (envelope schema v1.1
      discriminator). Malformed/empty input yields envelope_valid=false
      with empty diagnostics.
"""
import difflib
import json
import sys

MAX_DIAGNOSTICS = 50


def _read_lines(path):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def edit_targets(prev_path, cur_path, ids_json_path):
    try:
        with open(ids_json_path, "r", encoding="utf-8") as f:
            entries = json.load(f)
    except (OSError, ValueError):
        print("[]")
        return 0

    # Entries for the current file only, sorted by line (1-based).
    decls = sorted(
        [e for e in entries if isinstance(e.get("line"), int) and e.get("id")],
        key=lambda e: e["line"],
    )
    if not decls:
        print("[]")
        return 0

    cur_lines = _read_lines(cur_path)

    if prev_path == "-":
        changed_ranges = [(1, max(len(cur_lines), 1))]
    else:
        try:
            prev_lines = _read_lines(prev_path)
        except OSError:
            prev_lines = []
        sm = difflib.SequenceMatcher(a=prev_lines, b=cur_lines, autojunk=False)
        changed_ranges = []
        for tag, _i1, _i2, j1, j2 in sm.get_opcodes():
            if tag == "equal":
                continue
            if j1 == j2:
                # Pure deletion: attribute to the deletion point in the
                # current file (clamped to a real line).
                line = min(max(j1, 0) + 1, max(len(cur_lines), 1))
                changed_ranges.append((line, line))
            else:
                changed_ranges.append((j1 + 1, j2))  # 1-based inclusive

    if not changed_ranges:
        print("[]")
        return 0

    # Approximate declaration spans: this decl's line .. next decl's line - 1;
    # the last declaration extends to EOF.
    changed_ids = []
    for idx, decl in enumerate(decls):
        start = decl["line"]
        end = decls[idx + 1]["line"] - 1 if idx + 1 < len(decls) else max(len(cur_lines), start)
        if end < start:
            end = start
        for lo, hi in changed_ranges:
            if lo <= end and hi >= start:
                if decl["id"] not in changed_ids:
                    changed_ids.append(decl["id"])
                break

    print(json.dumps(changed_ids))
    return 0


def envelope(envelope_path):
    result = {"diagnostics": [], "diagnostics_truncated": False, "envelope_valid": False}
    try:
        with open(envelope_path, "r", encoding="utf-8", errors="replace") as f:
            doc = json.load(f)
    except (OSError, ValueError):
        print(json.dumps(result))
        return 0

    if isinstance(doc, dict) and "version" in doc:
        result["envelope_valid"] = True

    diags = doc.get("diagnostics") if isinstance(doc, dict) else None
    if isinstance(diags, list):
        for d in diags[:MAX_DIAGNOSTICS]:
            if not isinstance(d, dict) or not isinstance(d.get("code"), str):
                continue
            entry = {"code": d["code"]}
            decl_id = d.get("declarationId")
            if isinstance(decl_id, str) and decl_id:
                entry["declarationId"] = decl_id
            result["diagnostics"].append(entry)
        if len(diags) > MAX_DIAGNOSTICS:
            result["diagnostics_truncated"] = True

    print(json.dumps(result))
    return 0


def main(argv):
    if len(argv) >= 5 and argv[1] == "edit-targets":
        return edit_targets(argv[2], argv[3], argv[4])
    if len(argv) >= 3 and argv[1] == "envelope":
        return envelope(argv[2])
    sys.stderr.write(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
