#!/usr/bin/env python3
"""Regenerate the canonical transcripts the harness test diffs against.

Run after an intentional compiler change, review the diff, and commit it —
the same discipline as CALOR_REGENERATE_S5_LEDGER for the metadata ledger.
"""
import os, subprocess, sys

EXP = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = ["run.py", "run2.py", "run3.py", "facts.py", "facts2.py", "compile53.py"]
OUT = os.path.join(EXP, "transcripts")

os.makedirs(OUT, exist_ok=True)
for s in SCRIPTS:
    proc = subprocess.run([sys.executable, os.path.join(EXP, s)],
                          capture_output=True, text=True, cwd=EXP)
    body = proc.stdout + proc.stderr
    path = os.path.join(OUT, s.replace(".py", ".txt"))
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(body)
    print(f"{s:16s} -> {len(body.splitlines()):5d} lines (exit {proc.returncode})")
