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
  strip-heldout <bundle_dir> <ws_src_dir>
      PHYSICALLY REMOVE each held-out test method (identified by its FQN) from
      the agent-visible test sources, so the agent can neither read nor run the
      oracle. Fail-loud: if a held-out method cannot be located/uniquely removed,
      exit 1 (a silent miss = the leak persists = a fabricated measurement).
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


# ---------------------------------------------------------------------------
# Held-out method stripping (the oracle-leak fix).
#
# A tiny string/comment/char-aware scanner walks C# source so brace/paren
# matching for a method body never miscounts a delimiter that lives inside a
# string or comment (including verbatim/interpolated strings and //, /* */).
# ---------------------------------------------------------------------------
def _skip_string(text, i):
    """i points at the opening quote of a "..." / @"..." / $"..." literal
    (any '@'/'$' prefixes already consumed by the caller). Return index just
    past the closing quote. `verbatim` doubles "" to escape; regular uses \\."""
    # Detect verbatim by looking back for '@'.
    verbatim = i > 0 and (text[i - 1] == '@' or (i > 1 and text[i - 2:i] in ('@$', '$@')))
    n = len(text)
    j = i + 1
    while j < n:
        c = text[j]
        if verbatim:
            if c == '"':
                if j + 1 < n and text[j + 1] == '"':
                    j += 2
                    continue
                return j + 1
            j += 1
        else:
            if c == '\\':
                j += 2
                continue
            if c == '"':
                return j + 1
            j += 1
    return n


def _next_significant(text, i):
    """Advance past whitespace and comments from i; return index of the next
    significant char (or len)."""
    n = len(text)
    while i < n:
        c = text[i]
        if c in " \t\r\n":
            i += 1
        elif c == '/' and i + 1 < n and text[i + 1] == '/':
            j = text.find('\n', i)
            i = n if j < 0 else j + 1
        elif c == '/' and i + 1 < n and text[i + 1] == '*':
            j = text.find('*/', i + 2)
            i = n if j < 0 else j + 2
        else:
            return i
    return n


def _match_delimiter(text, open_i, open_ch, close_ch):
    """Brace/paren-match from an opening delimiter at open_i (string/comment
    aware). Return index just past the matching close, or -1."""
    n = len(text)
    depth = 0
    i = open_i
    while i < n:
        c = text[i]
        if c == '"' or ((c in '@$') and i + 1 < n and text[i + 1] == '"') \
                or (c in '@$' and i + 2 < n and text[i + 1] in '@$' and text[i + 2] == '"'):
            # position i at the actual quote
            q = i
            while text[q] in '@$':
                q += 1
            i = _skip_string(text, q)
            continue
        if c == "'":
            j = i + 1
            while j < n:
                if text[j] == '\\':
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                j += 1
            i = j
            continue
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            j = text.find('\n', i)
            i = n if j < 0 else j + 1
            continue
        if c == '/' and i + 1 < n and text[i + 1] == '*':
            j = text.find('*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == open_ch:
            depth += 1
        elif c == close_ch:
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return -1


def _class_body_span(text, class_short):
    """Return (open_brace_index, end_index) of `class <class_short>`'s body, or None."""
    import re
    for m in re.finditer(r'\b(?:class|struct|record)\s+' + re.escape(class_short) + r'\b', text):
        brace = text.find('{', m.end())
        if brace < 0:
            continue
        end = _match_delimiter(text, brace, '{', '}')
        if end > 0:
            return (brace, end)
    return None


def _remove_method(text, method, class_short):
    """Remove the method named `method` (with its attributes/doc-comment trivia)
    from within class `class_short`. Return (new_text, True) or (text, False)."""
    import re
    span = _class_body_span(text, class_short)
    if span is None:
        return text, False
    body_start, body_end = span
    for m in re.finditer(r'\b' + re.escape(method) + r'\s*\(', text):
        if not (body_start < m.start() < body_end):
            continue
        paren = text.index('(', m.start())
        after_params = _match_delimiter(text, paren, '(', ')')
        if after_params < 0:
            continue
        sig = _next_significant(text, after_params)
        if sig >= len(text):
            continue
        if text[sig] == '{':
            end = _match_delimiter(text, sig, '{', '}')
        elif text[sig:sig + 2] == '=>':
            semi = text.find(';', sig)
            end = semi + 1 if semi >= 0 else -1
        else:
            continue  # not a method declaration (e.g. abstract/interface) — skip
        if end < 0:
            continue
        # Expand start backward over attribute/doc-comment trivia lines.
        line_start = text.rfind('\n', 0, m.start()) + 1
        lines_before = text[:line_start].split('\n')
        idx = len(lines_before) - 1  # index of the (empty) element after last \n
        start_line_no = idx  # 0-based line holding the signature is idx
        cut = line_start
        k = idx - 1
        while k >= 0:
            s = lines_before[k].strip()
            if s.startswith('[') or s.endswith(']') or s.startswith('///') \
                    or s.startswith('//') or s == '' \
                    or (s.startswith('*') or s.startswith('/*') or s.endswith('*/')):
                k -= 1
            else:
                break
        # cut at the first trivia line that is NON-blank (keep separating blanks)
        j = idx - 1
        first_nonblank_trivia = idx
        while j > k:
            if lines_before[j].strip() != '':
                first_nonblank_trivia = j
            j -= 1
        cut = len(('\n'.join(lines_before[:first_nonblank_trivia])) + ('\n' if first_nonblank_trivia > 0 else ''))
        # Extend end to end of line (consume trailing newline).
        nl = text.find('\n', end)
        end_cut = len(text) if nl < 0 else nl + 1
        new_text = text[:cut] + text[end_cut:]
        return new_text, True
    return text, False


def cmd_strip_heldout(bundle_dir, ws_src):
    prov = load_prov(bundle_dir)
    held = prov.get("HeldOut", [])
    if not held:
        print("ERROR: no held-out tests in provenance", file=sys.stderr)
        return 1
    cs_files = []
    for dp, _d, files in os.walk(ws_src):
        if os.sep + "obj" in dp or os.sep + "bin" in dp:
            continue
        for f in files:
            if f.endswith(".cs"):
                cs_files.append(os.path.join(dp, f))

    ok = True
    for h in held:
        fqn = h.get("FilterName") or f"{h['ClassName']}.{h['TestName']}"
        method = fqn.split("(")[0].rsplit(".", 1)[-1]
        class_fqn = fqn.split("(")[0].rsplit(".", 1)[0]
        class_short = class_fqn.rsplit(".", 1)[-1]
        removed_in = []
        for path in cs_files:
            with open(path, encoding="utf-8") as fh:
                text = fh.read()
            if class_short not in text or method not in text:
                continue
            new_text, did = _remove_method(text, method, class_short)
            if did:
                # Verify the method declaration is gone from this file.
                import re
                if re.search(r'\b' + re.escape(method) + r'\s*\(', new_text) and \
                   _class_body_span(new_text, class_short) and \
                   _remove_method(new_text, method, class_short)[1]:
                    print(f"ERROR: held-out method '{method}' still declared after strip in {path}",
                          file=sys.stderr)
                    ok = False
                with open(path, "w", encoding="utf-8") as fh:
                    fh.write(new_text)
                removed_in.append(os.path.relpath(path, ws_src))
        if len(removed_in) == 0:
            print(f"ERROR: held-out method '{class_short}.{method}' NOT FOUND in agent test sources "
                  f"(cannot strip -> would leak the oracle)", file=sys.stderr)
            ok = False
        elif len(removed_in) > 1:
            print(f"ERROR: held-out method '{class_short}.{method}' removed from MULTIPLE files "
                  f"{removed_in} (ambiguous)", file=sys.stderr)
            ok = False
        else:
            print(f"OK: stripped held-out '{class_short}.{method}' from {removed_in[0]}")
    return 0 if ok else 1


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    cmd = argv[1]
    if cmd == "heldout-filter" and len(argv) == 3:
        return cmd_heldout_filter(argv[2])
    if cmd == "apply-fix" and len(argv) == 7:
        return cmd_apply_fix(argv[2], argv[3], argv[4], argv[5], argv[6])
    if cmd == "strip-heldout" and len(argv) == 4:
        return cmd_strip_heldout(argv[2], argv[3])
    print(f"ERROR: bad usage for '{cmd}'", file=sys.stderr)
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
