"""#1422 PR 2 local end-to-end: the real repository, candidate = this branch's head, main simulated.

Scenarios (each in a throwaway clone under the scratchpad, origin is a local bare repository):
  A  main == candidate                                   -> OK, bytes A
  B  main == candidate + an unrelated EvidenceContract test (the C2 case) -> OK, bytes == A
  B' same as B with the pre-PR-2 gate (origin/main's script)           -> B2-08 (the C2 refusal)
  C  main == candidate + a change under tests/Calor.Evaluation          -> REFUSED B2-08
  D  main == candidate + a registered pair file changed                  -> REFUSED B2-08
  E  main == candidate + A's headline merged (publication PR merged)     -> OK, bytes == A
"""
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

SRC = Path(sys.argv[1])          # the worktree (source of objects)
CANDIDATE = sys.argv[2]          # full SHA
REGEN = Path(sys.argv[3])        # regenerated packet directory
OLD_GATE_REV = sys.argv[4]       # origin/main sha holding the pre-PR-2 gate
WORK = Path(sys.argv[5])
HEADLINE = "website/public/data/benchmark-headline.json"
INDEX = "bench/phase0-agent-native/commit-stamp-index.json"


def sh(cwd, *args, check=True):
    r = subprocess.run(list(args), cwd=cwd, capture_output=True, text=True)
    if check and r.returncode:
        raise SystemExit(f"{args}: {r.stderr}")
    return r


shutil.rmtree(WORK, ignore_errors=True)
WORK.mkdir(parents=True)
origin = WORK / "origin.git"
sh(WORK, "git", "init", "-q", "--bare", str(origin))
sh(SRC, "git", "push", "-q", str(origin), f"{CANDIDATE}:refs/heads/main")
sh(SRC, "git", "push", "-q", str(origin), f"{OLD_GATE_REV}:refs/heads/old-gate")


def clone(name):
    root = WORK / name
    sh(WORK, "git", "clone", "-q", f"file://{origin}", str(root))
    for k, v in (("user.email", "e2e@example.com"), ("user.name", "e2e"), ("commit.gpgsign", "false")):
        sh(root, "git", "config", k, v)
    return root


def advance_main(edit):
    """Commit `edit` on top of the candidate as main's new head."""
    root = clone("mover")
    sh(root, "git", "reset", "-q", "--hard", CANDIDATE)
    edit(root)
    sh(root, "git", "add", "-A")
    sh(root, "git", "commit", "-q", "-m", "main moves on")
    sh(root, "git", "push", "-q", "-f", "origin", "HEAD:refs/heads/main")
    shutil.rmtree(root)


def gate(name, script=None):
    root = clone(name)
    sh(root, "git", "checkout", "-q", "--detach", CANDIDATE)
    if script:
        (WORK / "old_gate.py").write_text(script)
    r = sh(root, "python3", str(WORK / "old_gate.py") if script else "scripts/benchmark_publication_gate.py",
           "check", "--commit", CANDIDATE, "--regenerated", str(REGEN), "--repo", str(root), check=False)
    out = {p: hashlib.sha256((root / p).read_bytes()).hexdigest()
           for p in (HEADLINE, INDEX)} if r.returncode == 0 else None
    print(f"== {name}: exit {r.returncode}")
    print((r.stdout + r.stderr).strip().splitlines()[1] if r.returncode == 0 else (r.stdout + r.stderr).strip())
    if out:
        for p, h in out.items():
            print(f"   {p} {h}")
    return root, out


rootA, a = gate("A-main-at-candidate")
a_bytes = {p: (rootA / p).read_bytes() for p in (HEADLINE, INDEX)}
rootA2, a2 = gate("A2-second-run")
assert a == a2, "two runs differ"

advance_main(lambda r: (r / "tests/Calor.Compiler.Tests/EvidenceContract/UnrelatedAfterCandidateTests.cs")
             .write_text("// an unrelated test added on main after the candidate\n"))
_, b = gate("B-unrelated-test-on-main")
assert b == a, "unrelated main change altered the bytes"
old = sh(SRC, "git", "show", f"{OLD_GATE_REV}:scripts/benchmark_publication_gate.py").stdout
_, bold = gate("B-prime-pre-PR-2-gate", script=old)
assert bold is None, "the old gate should refuse (B2-08)"

advance_main(lambda r: (r / "tests/Calor.Evaluation/Equivalence/PairResultsCommand.cs").open("a").write("// changed\n"))
_, c = gate("C-generator-changed-on-main")
assert c is None

advance_main(lambda r: (r / "tests/TestData/Benchmarks/TokenEconomics/HelloWorld.calr").open("a").write("\n"))
_, d = gate("D-registered-pair-changed-on-main")
assert d is None


def merge_headline(r):
    for p, data in a_bytes.items():
        (r / p).write_bytes(data)


advance_main(merge_headline)
_, e = gate("E-candidate-headline-already-on-main")
assert e == a, "re-run after merge must write the same bytes"
_, eold = gate("E-prime-pre-PR-2-gate", script=old)
assert eold is not None and eold[HEADLINE] != a[HEADLINE], "the old gate's bytes follow main's later state"
print("ALL SCENARIOS AS EXPECTED")
