#!/usr/bin/env python3
"""Emitter-spike artifact generator (design doc §12.3).

For every artifact named in MANIFEST it compiles ``before/<id>.calr`` and
``after/<id>.calr`` with the worktree-built compiler and writes, next to the
sources:

  <side>/<id>.g.cs.txt         the emitted C#, stored with a .txt suffix because
                               the Calor-first guard rejects new .cs paths anywhere
                               in the tree and this is a transcript, not a source
  <side>/<id>.diagnostics.txt  the compiler's full diagnostic list

Discipline, matching ``run.py``/``compile53.py`` in this directory:

* **deterministic** — diagnostics are sorted by (line, column, code, text) with
  ``LC_ALL=C`` collation, so filesystem and locale order cannot leak in;
* **no absolute paths** — every path in an output file is repo-relative;
* **scratch outside the repository** — the emitted C# is written to a temp
  directory first and only the bytes are copied in, and no ``.calr`` is ever
  created under ``docs/`` (``HigherOrderDemandLedgerTests`` walks the filesystem
  for ``.calr`` files and counts anything it finds as corpus);
* **re-runnable** — running it twice with no compiler change is a no-op.

Usage::

    python3 docs/design/spikes/effect-rows/experiments/spike_artifacts.py
    python3 docs/design/spikes/effect-rows/experiments/spike_artifacts.py A1 A2
"""
import json
import os
import re
import subprocess
import sys
import tempfile

os.environ.setdefault("LC_ALL", "C")
os.environ.setdefault("LANG", "C")

EXP = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(EXP)
ROOT = os.path.abspath(os.path.join(SPIKE, "..", "..", "..", ".."))

# id -> (compile args used when the plain compile does not emit, note)
MANIFEST = {
    "A1": ["--permissive-effects"],
    "A2": ["--permissive-effects"],
    "A3-map": ["--permissive-effects"],
    "A3-match": ["--permissive-effects"],
    "A3-middleware": ["--permissive-effects"],
    "A3-callback": ["--permissive-effects"],
}

# after-only fixtures: R2 / alpha-equivalence evidence, not part of R1's four
AFTER_ONLY = {
    "A2-broadening": ["--permissive-effects"],
    "A3-middleware-broadening": [],
    "A3-middleware-alpha": [],
}

DIAGNOSTIC = re.compile(r"^(?P<path>.*?)\((?P<line>\d+),(?P<col>\d+)\): "
                        r"(?P<sev>error|warning|info) (?P<code>Calor\d+): (?P<text>.*)$")


def find_dll():
    override = os.environ.get("CALOR_DLL")
    if override:
        return override
    for cfg in ("Debug", "Release"):
        path = os.path.join(ROOT, "src/Calor.Compiler/bin", cfg, "net10.0/calor.dll")
        if os.path.exists(path):
            return path
    raise SystemExit(
        "calor.dll not found under src/Calor.Compiler/bin/{Debug,Release}/net10.0/. "
        "Run: dotnet build src/Calor.Compiler")


DLL = find_dll()


def compile_once(source, output, args):
    """Runs the compiler and returns (exit code, sorted diagnostic lines)."""
    proc = subprocess.run(
        ["dotnet", DLL, "-i", source, "-o", output] + args,
        capture_output=True, text=True, cwd=ROOT)
    raw = (proc.stdout + proc.stderr).replace(ROOT + os.sep, "").replace(ROOT + "/", "")

    diagnostics = []
    trailer = []
    for line in raw.splitlines():
        line = line.rstrip()
        if not line:
            continue
        match = DIAGNOSTIC.match(line)
        if match:
            diagnostics.append((
                int(match.group("line")),
                int(match.group("col")),
                match.group("code"),
                match.group("sev"),
                line,
            ))
        else:
            # Strip the scratch output path out of "Compilation successful: ...".
            trailer.append(re.sub(r"(Compilation successful): .*", r"\1", line))

    diagnostics.sort(key=lambda d: (d[0], d[1], d[2], d[3], d[4]))
    return proc.returncode, [d[4] for d in diagnostics], sorted(trailer)


def relative(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def emit(identifier, side, args, work):
    source = os.path.join(SPIKE, side, identifier + ".calr")
    if not os.path.exists(source):
        return None

    # The AFTER artifacts were produced by the SPIKE PROTOTYPE, which is not on
    # main (see spike-verdict.json's `prototype` block). Re-running this script
    # against a compiler without row support would "regenerate" them into parse
    # errors and delete the emitted C#, destroying recorded evidence. So the
    # after/ side is read-only unless the caller says otherwise.
    if side == "after" and os.environ.get("CALOR_WRITE_SPIKE_AFTER") != "1":
        return {"recorded": True, "regenerated": False}

    scratch = os.path.join(work, f"{side}-{identifier}.g.cs")
    code, diagnostics, trailer = compile_once(source, scratch, [])
    used = []

    if not os.path.exists(scratch) and args:
        # The plain compile did not emit. Re-run with the flags today's
        # converted code needs, so the artifact set is complete; the
        # DIAGNOSTICS above are still the plain ones.
        used = list(args)
        compile_once(source, scratch, used)

    emitted = os.path.exists(scratch)
    target_cs = os.path.join(SPIKE, side, identifier + ".g.cs.txt")
    if emitted:
        with open(scratch, "r", encoding="utf-8") as handle:
            generated = handle.read()
        # The emitter writes ABSOLUTE paths into #line directives. Two
        # normalisations, both required:
        #   1. no absolute path may be committed (harness discipline);
        #   2. the before/ and after/ sources differ only in directory, and
        #      G-CODEGEN must not read that as a codegen difference.
        generated = generated.replace(os.path.abspath(source), "<source>.calr")
        generated = generated.replace(ROOT + os.sep, "").replace(ROOT + "/", "")
        with open(target_cs, "w", encoding="utf-8") as handle:
            handle.write(generated)
    elif os.path.exists(target_cs):
        os.remove(target_cs)

    header = [
        f"# {relative(source)}",
        f"# exit: {code}",
        f"# emitted: {'yes' if emitted else 'no'}",
        f"# emit args: {' '.join(used) if used else '(none)'}",
        f"# diagnostics: {len(diagnostics)}",
        "",
    ]
    with open(os.path.join(SPIKE, side, identifier + ".diagnostics.txt"),
              "w", encoding="utf-8") as handle:
        handle.write("\n".join(header + diagnostics + trailer) + "\n")

    return {
        "exit": code,
        "emitted": emitted,
        "emitArgs": used,
        "diagnostics": diagnostics,
    }


def main():
    wanted = sys.argv[1:]
    work = os.environ.get("CALOR_EXPERIMENT_WORKDIR") or tempfile.mkdtemp(
        prefix="calor-spike-artifacts-")
    os.makedirs(work, exist_ok=True)

    summary = {}
    for identifier in sorted(MANIFEST):
        if wanted and identifier not in wanted:
            continue
        summary[identifier] = {
            "before": emit(identifier, "before", MANIFEST[identifier], work),
            "after": emit(identifier, "after", MANIFEST[identifier], work),
        }

    for identifier in sorted(AFTER_ONLY):
        if wanted and identifier not in wanted:
            continue
        summary[identifier] = {
            "before": None,
            "after": emit(identifier, "after", AFTER_ONLY[identifier], work),
        }

    print(json.dumps(summary, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
