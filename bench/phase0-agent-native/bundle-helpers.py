#!/usr/bin/env python3
"""
Slice-C bundle adapter helpers for run-bundle.sh (WS-W4 dry-run runner).

The authored-pair runner (run-pair.sh) ships a per-arm reference/ solution the
null-agent applies to prove the plumbing. A gen-tasks bundle does NOT ship one,
so this helper DERIVES the reference (the un-mutated source = the fix) from the
bundle's provenance + the SHA-pinned corpus:

  * csharp arm  -> the reference is EXACT: the pristine corpus file (the C# arm
                   is the idiomatic original + one point mutation), copied over
                   the mutated file.
  * calor  arm  -> the reference is derived by reversing the single mutated line
                   in the round-tripped copy. provenance Line/Column address the
                   C# arm only (the converter renumbers lines), so we anchor on
                   the mutated line's CONTENT (from the C#-arm/corpus diff) and
                   fail loud unless it matches exactly once.

Fail-loud is deliberate: a mis-derived reference would fabricate a CAUGHT/ESCAPED
verdict, exactly the hazard the null-agent verification exists to rule out.

Subcommands:
  heldout-filter <bundle_dir>
      Print the held-out dotnet --filter expression (OR of FullyQualifiedName~).
  apply-fix <bundle_dir> <arm> <ws_src_dir> <corpus_root> <calor_root>
      Derive and apply the reference fix into <ws_src_dir>. Prints "OK: ..." on
      success (exit 0) or "ERROR: ..." (exit 1).
"""
import json
import os
import sys


# ProjectName -> the working-copy root that holds the pristine, un-mutated source
# (mirrors ProjectConfigs.cs OriginalProjectPath; the corpus dir casing matters).
def subject_root(project_name, corpus_root, calor_root):
    key = project_name.lower()
    if key == "fluentvalidation":
        return os.path.join(corpus_root, "FluentValidation")
    if key == "serilog":
        return os.path.join(corpus_root, "serilog")
    if key == "mediatr":
        return os.path.join(corpus_root, "MediatR")
    if key == "synthetic":
        return os.path.join(calor_root, "tests", "Calor.RoundTrip.Synthetic")
    if key == "synthetic2":
        return os.path.join(calor_root, "tests", "Calor.RoundTrip.Synthetic2")
    return None


def load_prov(bundle_dir):
    with open(os.path.join(bundle_dir, "provenance.json")) as f:
        return json.load(f)


def cmd_heldout_filter(bundle_dir):
    prov = load_prov(bundle_dir)
    names = [h.get("FilterName") or f"{h['ClassName']}.{h['TestName']}"
             for h in prov.get("HeldOut", [])]
    names = [n for n in names if n]
    if not names:
        print("ERROR: no held-out tests in provenance", file=sys.stderr)
        return 1
    print("|".join(f"FullyQualifiedName~{n}" for n in names))
    return 0


def diff_single_line(original_text, mutated_text):
    """Return (orig_line, mut_line) for the single line that changed, or None
    if the change is not a clean one-line point mutation."""
    o = original_text.splitlines()
    m = mutated_text.splitlines()
    if len(o) != len(m):
        return None
    diffs = [(ol, ml) for ol, ml in zip(o, m) if ol != ml]
    if len(diffs) != 1:
        return None
    return diffs[0]


def cmd_apply_fix(bundle_dir, arm, ws_src, corpus_root, calor_root):
    prov = load_prov(bundle_dir)
    project = prov["ProjectName"]
    rel = prov["Provenance"]["MutatedFileRelPath"]
    root = subject_root(project, corpus_root, calor_root)
    if root is None:
        print(f"ERROR: no pristine-source root known for project {project}", file=sys.stderr)
        return 1
    pristine_path = os.path.join(root, rel)
    if not os.path.isfile(pristine_path):
        print(f"ERROR: pristine corpus file missing: {pristine_path} "
              f"(corpus submodule not checked out?)", file=sys.stderr)
        return 1
    cs_mut_path = os.path.join(bundle_dir, "csharp-arm", rel)
    if not os.path.isfile(cs_mut_path):
        print(f"ERROR: csharp-arm mutated file missing: {cs_mut_path}", file=sys.stderr)
        return 1

    with open(pristine_path, encoding="utf-8") as f:
        pristine = f.read()
    with open(cs_mut_path, encoding="utf-8") as f:
        cs_mut = f.read()

    diff = diff_single_line(pristine, cs_mut)
    if diff is None:
        print("ERROR: csharp-arm file is not a clean single-line point mutation "
              "vs the pristine corpus file; cannot derive reference", file=sys.stderr)
        return 1
    orig_line, mut_line = diff

    target = os.path.join(ws_src, rel)
    if not os.path.isfile(target):
        print(f"ERROR: workspace file missing: {target}", file=sys.stderr)
        return 1

    if arm == "csharp":
        # Exact reference: the pristine original source.
        with open(target, "w", encoding="utf-8") as f:
            f.write(pristine)
        print(f"OK: csharp reference applied (pristine {rel}); "
              f"reverted '{mut_line.strip()}' -> '{orig_line.strip()}'")
        return 0

    # calor arm: reverse the mutated line by content match (line numbers diverge
    # post-conversion). Anchor on the trimmed mutated-line content; require a
    # unique match so we never revert the wrong occurrence.
    with open(target, encoding="utf-8") as f:
        lines = f.read().splitlines(keepends=True)
    needle = mut_line.strip()
    repl = orig_line.strip()
    hits = [i for i, ln in enumerate(lines) if ln.strip() == needle]
    if len(hits) == 0:
        print(f"ERROR: calor-arm reference underivable: mutated line '{needle}' "
              f"not found in {rel} (converter reshaped it)", file=sys.stderr)
        return 1
    if len(hits) > 1:
        print(f"ERROR: calor-arm reference ambiguous: mutated line '{needle}' "
              f"occurs {len(hits)} times in {rel}; refusing to guess", file=sys.stderr)
        return 1
    i = hits[0]
    ln = lines[i]
    indent = ln[:len(ln) - len(ln.lstrip())]
    eol = "\r\n" if ln.endswith("\r\n") else ("\n" if ln.endswith("\n") else "")
    lines[i] = f"{indent}{repl}{eol}"
    with open(target, "w", encoding="utf-8") as f:
        f.write("".join(lines))
    print(f"OK: calor reference applied ({rel} line {i + 1}); "
          f"reverted '{needle}' -> '{repl}'")
    return 0


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    cmd = argv[1]
    if cmd == "heldout-filter" and len(argv) == 3:
        return cmd_heldout_filter(argv[2])
    if cmd == "apply-fix" and len(argv) == 7:
        return cmd_apply_fix(argv[2], argv[3], argv[4], argv[5], argv[6])
    print(f"ERROR: bad usage for '{cmd}'", file=sys.stderr)
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
