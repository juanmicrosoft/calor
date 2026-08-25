#!/usr/bin/env python3
import os, subprocess, re
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))
os.chdir(ROOT)
# Locale pinned for the same reason as facts.py: CI is Linux, the doc was
# authored on macOS, and collation differs.
_ENV = {**os.environ, "LC_ALL": "C", "LANG": "C"}


def sh(c):
    return subprocess.run(
        c, shell=True, capture_output=True, text=True, env=_ENV).stdout.rstrip()

print("### BoundTypeTests DisplayString exact-equality pins")
print(sh("grep -n 'DisplayString' tests/Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs"))
print("--- FunctionBoundType Equals tests?")
print(sh("grep -n 'FunctionBoundType' tests/Calor.Compiler.Tests/Binding/BoundTypes/BoundTypeTests.cs"))

print("\n### calor-direction.md")
for ln in (23, 33, 57, 112, 114):
    print(f":{ln}: " + sh(f"sed -n '{ln}p' docs/design/calor-direction.md")[:200])

print("\n### roadmap gate 1 text")
print(sh("sed -n '/1. \\*\\*Effect laundering, closed classes/,/^2\\. \\*\\*Higher-order/p' docs/plans/roadmap-v0.13-v0.15.md"))

print("\n### files with two-line §O then §E (23 files)")
files = subprocess.run(["git","ls-files","*.calr"],capture_output=True,text=True).stdout.split()
hits=[]
for f in files:
    L=open(f,encoding="utf-8",errors="replace").read().split("\n")
    n=sum(1 for i,l in enumerate(L) if re.search(r"§O\{[^}]*\}\s*$", l) and i+1<len(L) and re.match(r"\s*§E\{", L[i+1]))
    if n: hits.append((f,n))
for f,n in sorted(hits, key=lambda t:(-t[1], t[0])):
    print(f"  {n:3d}  {f}")
print("total files:", len(hits), " total occurrences:", sum(n for _,n in hits))

print("\n### conversion snapshots with function-typed shapes, per file")
for f in sorted(subprocess.run(["git","ls-files","tests/Calor.Conversion.Tests/Snapshots/*.calr"],capture_output=True,text=True).stdout.split()):
    t=open(f,encoding="utf-8",errors="replace").read()
    shapes=re.findall(r"Func<[^>]*>|Action<[^>]*>|Predicate<[^>]*>|Action[}:]", t)
    lam=t.count("§LAM"); dele=t.count("§DEL")
    if shapes or lam or dele:
        print(f"  {os.path.basename(f):28s} shapes={len(shapes)} §LAM={lam} §DEL={dele}  {shapes[:3]}")

print("\n### §LAM / §DEL across the committed corpus, per file")
for f in files:
    t=open(f,encoding="utf-8",errors="replace").read()
    if "§LAM" in t or "§DEL" in t:
        print(f"  {f}  §LAM={t.count('§LAM')} §DEL={t.count('§DEL')}")

print("\n### PR #1085 / F-3 supersession")
# Deliberately NOT `git log`: CI checks out shallow, so history-derived facts
# are absent there and present locally -- exactly the drift that broke the
# first version of this transcript. Instead probe the pinned object and print
# a fixed marker either way; both branches are deterministic, and the doc
# cites the SHA rather than the subject line.
_present = subprocess.run(
    ["git", "cat-file", "-e", "b5d61e18^{commit}"],
    capture_output=True, text=True, env=_ENV).returncode == 0
print("b5d61e18 reachable in this checkout:", _present,
      "(shallow clones report False; the doc cites the SHA, not the subject)")
