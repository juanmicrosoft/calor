import itertools
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

root = Path(sys.argv[1]).resolve()
case_dir = root / ".n0-evidence" / "cases"
out = root / ".n0-evidence" / "cli"
out.mkdir(exist_ok=True)
dll = root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
cases = json.loads((case_dir / "api-lsp-baseline.json").read_text())["rows"]
core = {
    "bind-bcl-nullable", "return-bcl-nullable", "argument-bcl-expression",
    "bind-literal", "bind-literal-nullable-target", "active-duplicate",
    "semver-1.0.0",
}
records = []
for case in cases:
    identifier = case["Id"]
    combinations = list(itertools.product((False, True), repeat=4)) if identifier in core else [(False,) * 4]
    modes = [
        (
            f"verify={int(v)};typeoff={int(t)};transpile={int(u)};permissive={int(p)}",
            (["--verify"] if v else []) + (["--no-type-check"] if t else [])
            + (["--transpile-only"] if u else []) + (["--permissive-effects"] if p else []),
            False,
        )
        for v, t, u, p in combinations
    ]
    if identifier in core:
        modes.append(("CALOR_NO_TYPE_CHECK=1", [], True))
    for index, (mode, flags, environment_optout) in enumerate(modes):
        command = ["dotnet", str(dll), "-i", str(case_dir / (identifier + ".calr")),
                   "-o", str(out / (identifier + ".g.cs")), "--format", "json",
                   "--no-cache", "--no-telemetry", *flags]
        environment = os.environ.copy()
        environment.pop("CALOR_NO_TYPE_CHECK", None)
        environment["CALOR_TELEMETRY"] = "0"
        environment["TMPDIR"] = str(root / ".n0-evidence/tmp")
        if environment_optout:
            environment["CALOR_NO_TYPE_CHECK"] = "1"
        result = subprocess.run(command, cwd=root, env=environment,
                                capture_output=True, text=True, timeout=120)
        stdout_path = out / f"{identifier}-{index}.stdout.json"
        stderr_path = out / f"{identifier}-{index}.stderr.txt"
        stdout_path.write_text(result.stdout)
        stderr_path.write_text(result.stderr)
        try:
            diagnostic_document = json.loads(result.stdout)
            json_error = None
        except json.JSONDecodeError as error:
            diagnostic_document = None
            json_error = str(error)
        records.append({
            "case": identifier, "mode": mode,
            "command": [arg.replace(str(root), "<REPO>") for arg in command],
            "environment": {"CALOR_NO_TYPE_CHECK": "1" if environment_optout else None,
                            "CALOR_TELEMETRY": "0", "TMPDIR": "<REPO>/.n0-evidence/tmp"},
            "exit": result.returncode,
            "document": diagnostic_document,
            "jsonError": json_error,
            "stdout": str(stdout_path.relative_to(root)),
            "stderr": str(stderr_path.relative_to(root)),
        })
    print(identifier, len(modes), flush=True)
(out / "matrix.json").write_text(json.dumps({
    "capturedAtUtc": datetime.now(timezone.utc).isoformat(),
    "rows": records
}, indent=2) + "\n")
if any(row["jsonError"] for row in records):
    raise SystemExit("Some CLI outputs were not valid JSON; inspect the explicitly recorded failures.")
print("Captured", len(records), "CLI invocations.")
