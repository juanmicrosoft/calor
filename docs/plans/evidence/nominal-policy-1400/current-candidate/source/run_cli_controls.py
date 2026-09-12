#!/usr/bin/env python3
"""Fixed CLI controls; defaults and effects-disabled are separate observations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--current", required=True, type=Path)
parser.add_argument("--annotated", required=True, type=Path)
parser.add_argument("--conservative", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=False)
inputs = args.output / "inputs"
inputs.mkdir()
sources = Path(__file__).parent / "controls"
cases = ["nullable-return", "safe-constructor"]
for case in cases:
    (inputs / (case + ".calr")).write_bytes((sources / (case + ".calr.txt")).read_bytes())
rows = []
for mode, root in [("current", args.current), ("shadow-annotated", args.annotated),
                   ("shadow-conservative", args.conservative)]:
    compiler = root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
    for effects in ["default", "effects-disabled"]:
        for case in cases:
            output = args.output / mode / effects / case
            output.mkdir(parents=True)
            source = inputs / (case + ".calr")
            generated = output / "generated.cs"
            invocation = ["dotnet", str(compiler), "-i", str(source), "-o", str(generated),
                          "--format", "json", "--no-telemetry"]
            if effects == "effects-disabled":
                invocation.append("--no-enforce-effects")
            environment = os.environ.copy()
            environment.pop("CALOR_NO_TYPE_CHECK", None)
            environment["CALOR_TELEMETRY"] = "0"
            environment["D1_CAPTURE_DIR"] = str(output / "captures")
            with (output / "stdout.json").open("xb") as stdout, (output / "stderr.log").open("xb") as stderr:
                result = subprocess.run(invocation, cwd=root, env=environment, stdout=stdout, stderr=stderr)
            expected = 0 if mode == "current" or case == "safe-constructor" else 1
            rows.append({
                "mode": mode, "effects": effects, "case": case, "command": invocation,
                "compilerSha256": hashlib.sha256(compiler.read_bytes()).hexdigest(),
                "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                "exitCode": result.returncode, "expectedExitCode": expected,
                "outputPresent": generated.exists(),
                "outputSha256": hashlib.sha256(generated.read_bytes()).hexdigest() if generated.exists() else None,
            })
with (args.output / "results.json").open("x", encoding="utf-8") as stream:
    json.dump(rows, stream, indent=2)
print(json.dumps(rows, indent=2))
raise SystemExit(0 if all(row["exitCode"] == row["expectedExitCode"] for row in rows) else 1)
