"""#1422 PR 2 mutation check: each new guard disabled or weakened; the suite must fail."""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(sys.argv[1])
GATE = ROOT / "scripts/benchmark_publication_gate.py"
WORKFLOW = ROOT / ".github/workflows/benchmark.yml"

MUTATIONS = [
    ("P01 registered pair paths not inputs", GATE,
     '            if isinstance(pair, dict) and isinstance(pair.get(side), str) and pair[side] not in paths:',
     '            if False:'),
    ("P02 freshness disabled", GATE,
     '        if entry("HEAD", path) != entry(MAIN_REF, path):',
     '        if False:'),
    ("P21 object ids only (mode ignored)", GATE,
     '        if entry("HEAD", path) != entry(MAIN_REF, path):',
     '        if git(root, "rev-parse", f"HEAD:{path}") != git(root, "rev-parse", f"{MAIN_REF}:{path}"):'),
    ("P03 a validator file dropped from the inputs", GATE,
     ' f"{VALIDATOR}/BenchmarkResultsValidator.cs",', ''),
    ("P04 generator dropped from the inputs", GATE,
     '"tests/TestData/Benchmarks", "tests/Calor.Evaluation",', '"tests/TestData/Benchmarks",'),
    ("P05 whole EvidenceContract directory is an input again", GATE,
     'INPUT_PATHS = (REGISTRATION, RESULTS, CONTRACT,', 'INPUT_PATHS = (REGISTRATION, RESULTS, CONTRACT, VALIDATOR,'),
    ("P06 comparison against main's headline again", GATE,
     '    text = git(root, "show", f"HEAD:{HEADLINE}")', '    text = git(root, "show", f"{MAIN_REF}:{HEADLINE}")'),
    ("P07 main headline fence disabled", GATE,
     '    if main != head and main != outputs[HEADLINE]:', '    if False:'),
    ("P08 main headline must equal the candidate's (no idempotent re-run)", GATE,
     '    if main != head and main != outputs[HEADLINE]:', '    if main != head:'),
    ("P09 stamp entry fence disabled", GATE,
     '    if main_entries != head_entries and main_entries != ours_entries:', '    if False:'),
    ("P10 whole stamp index compared instead of the headline entry", GATE,
     '    main_entries = stamp_entries(git(root, "show", f"{MAIN_REF}:{STAMP_INDEX}"))',
     '    main_entries = git(root, "show", f"{MAIN_REF}:{STAMP_INDEX}")'),
    ("P11 stamp index read from the work tree", GATE,
     '    index = json.loads(git(root, "show", f"HEAD:{STAMP_INDEX}", check=True))',
     '    index = json.loads((root / STAMP_INDEX).read_text(encoding="utf-8"))'),
    ("P12 a run timestamp in the headline", GATE,
     '        "schemaVersion": 1,',
     '        "schemaVersion": 1, "generatedAt": __import__("time").time_ns(),'),
    ("P13 the gate's own script not an input", GATE,
     '               "scripts/benchmark_publication_gate.py")\nSTAMP_KEY', '               )\nSTAMP_KEY'),
    ("P14 push paths: whole EvidenceContract directory again", WORKFLOW,
     "      - 'tests/Calor.Compiler.Tests/EvidenceContract/Benchmark*.cs'",
     "      - 'tests/Calor.Compiler.Tests/EvidenceContract/**'"),
    ("P15 push paths: project file trigger removed", WORKFLOW,
     "      - 'tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj'\n", ""),
    ("P16 files the seals name are not inputs", GATE,
     '        for path in sorted(files) if isinstance(files, dict) else []:',
     '        for path in []:'),
    ("P17 the contract seal is not an input", GATE,
     'INPUT_PATHS = (REGISTRATION, RESULTS, CONTRACT, CONTRACT_SEAL,', 'INPUT_PATHS = (REGISTRATION, RESULTS, CONTRACT,'),
    ("P18 only the first headline stamp entry is compared", GATE,
     '    return [s for s in stamps if not isinstance(s, dict) or (s.get("path"), s.get("stampPointer")) == STAMP_KEY]',
     '    return [s for s in stamps if not isinstance(s, dict) or (s.get("path"), s.get("stampPointer")) == STAMP_KEY][:1]'),
    ("P19 an unreadable index counts as absent", GATE,
     '        return "<unreadable>"\n    if not', '        return []\n    if not'),
    ("P20 push paths: sealed contract document trigger removed", WORKFLOW,
     "      - 'docs/plans/v0.24-evidence-contract.md'\n", ""),
]

ONLY = set(sys.argv[2:])
for name, path, old, new in MUTATIONS:
    if ONLY and name.split()[0] not in ONLY:
        continue
    original = path.read_text()
    assert original.count(old) == 1, (name, original.count(old))
    path.write_text(original.replace(old, new))
    try:
        run = subprocess.run([sys.executable, "scripts/test_benchmark_publication_gate.py"], cwd=ROOT,
                             capture_output=True, text=True)
    finally:
        path.write_text(original)
    failed = sorted(set(re.findall(r"^(?:FAIL|ERROR): (\w+)", run.stderr, re.M)))
    verdict = "KILLED" if run.returncode else "SURVIVED"
    print(f"{name}: {verdict} :: {', '.join(failed)}", flush=True)
