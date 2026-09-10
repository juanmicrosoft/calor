#!/usr/bin/env python3
"""Re-execute the retained standard-spelling sources; no task selection or collection."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
RELEASE = "514f538024df990af86054af25975b756ba42ab1"
TABLE = "tests/Calor.Enforcement.Tests/RowEscapeTableTests.cs"


def run(compiler_root, output):
    compiler_root, output = compiler_root.resolve(), output.resolve()
    if not output.is_relative_to(REPO) or output == REPO or output.exists():
        raise ValueError("Output must be a new directory inside this worktree")
    head = subprocess.check_output(
        ["git", "-C", str(compiler_root), "rev-parse", "HEAD"], text=True).strip()
    dirty = subprocess.check_output(
        ["git", "-C", str(compiler_root), "status", "--porcelain", "--untracked-files=no"],
        text=True).strip()
    if head != RELEASE or dirty:
        raise ValueError("Expected the clean frozen compiler checkout")
    source_record = json.loads((HERE / "standard-spellings.json").read_text())
    compiler = compiler_root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll"
    compiler_hash = hashlib.sha256(compiler.read_bytes()).hexdigest()
    output.mkdir(parents=True)
    env = {**os.environ, "CALOR_TELEMETRY": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    report = {
        "kind": "unregistered-standard-spelling-survey",
        "compilerCommit": head,
        "compilerSha256": compiler_hash,
        "tableSource": TABLE,
        "tableBlob": subprocess.check_output(
            ["git", "-C", str(compiler_root), "rev-parse", f"HEAD:{TABLE}"], text=True).strip(),
        "source": source_record["source"],
        "shapes": [],
    }
    for item in source_record["shapes"]:
        row = item["row"]
        source = output / f"row-{row:02}.calr"
        source.write_text(item["source"])
        record = {key: item[key] for key in ("row", "name", "source")}
        record.update(sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(), arms={})
        for arm in ("A", "B"):
            command = [
                "dotnet", str(compiler), "-i", str(source),
                "-o", str(output / f"row-{row:02}-{arm}.g.cs"), "--no-telemetry",
            ] + (["--permissive-effects"] if arm == "A" else [])
            result = subprocess.run(command, cwd=REPO, env=env, text=True, capture_output=True)

            def normalize(text):
                return text.replace(str(output), "<survey-work>").replace(
                    str(compiler_root), "<compiler-root>").replace(str(REPO), "<repo>")

            record["arms"][arm] = {
                "command": [normalize(value) for value in command],
                "exitCode": result.returncode,
                "stdout": normalize(result.stdout),
                "stderr": normalize(result.stderr),
            }
        report["shapes"].append(record)
    if hashlib.sha256(compiler.read_bytes()).hexdigest() != compiler_hash:
        raise RuntimeError("Compiler changed during observation")
    (output / "standard-spellings.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    run(args.compiler_root, args.output)
