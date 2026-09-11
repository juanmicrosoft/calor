import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
base = root / ".n0-evidence"
folder = base / "tmp/default-layout-cache"
folder.mkdir(parents=True, exist_ok=True)
path = folder / "Input.calr"
env = os.environ.copy()
env.pop("CALOR_NO_TYPE_CHECK", None)
env["CALOR_TELEMETRY"] = "0"
env["TMPDIR"] = str(base / "tmp")
rows = []
for index, (identifier, flags) in enumerate([
    ("bind-bcl-nullable", ["--clear-cache"]), ("bind-bcl-nullable", []),
    ("bind-bcl-nullable", ["--no-type-check"]),
    ("active-duplicate", ["--no-type-check"]),
    ("active-duplicate", ["--transpile-only"]),
    ("bind-literal-nullable-target", ["--no-type-check"]),
    ("bind-literal-nullable-target", []),
    ("bind-literal-nullable-target", ["--clear-cache"]),
    ("bind-literal", []), ("bind-literal", []),
]):
    source = (base / "cases" / (identifier + ".calr")).read_text()
    if not path.exists() or path.read_text() != source:
        path.write_text(source)
    command = ["dotnet", str(root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"),
               "-i", str(path), "--cache", "--verbose", "--format", "json", "--no-telemetry", *flags]
    result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True, timeout=120)
    cache = folder / ".calor-build-state.json"
    rows.append({
        "index": index, "case": identifier,
        "command": [arg.replace(str(root), "<REPO>") for arg in command],
        "exit": result.returncode, "stdout": result.stdout.replace(str(root), "<REPO>"),
        "stderr": result.stderr.replace(str(root), "<REPO>"),
        "cacheExists": cache.exists(),
        "cache": json.loads(cache.read_text()) if cache.exists() else None,
    })
    print(index, identifier, result.returncode, "cached" in result.stderr, flush=True)
(base / "cache-default-layout.json").write_text(json.dumps({
    "candidate": "1ea8fe0bfddfb4bddac85fe5698d750c2a1939c4",
    "reason": "Explicit -o intentionally disables root incremental cache; this supplemental run uses the supported default output layout.",
    "rows": rows
}, indent=2) + "\n")
