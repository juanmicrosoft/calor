#!/usr/bin/env python3
"""Produce R8 CLI observations from the exact SDK-validated Calor input."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--product-root", type=Path, required=True)
    parser.add_argument("--suite-evidence", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    product = args.product_root.resolve()
    suite = args.suite_evidence.resolve()
    output = args.output.resolve()
    if output.exists() or not output.is_relative_to(repo):
        parser.error("output must be a new directory inside the worktree")
    report = json.loads((suite / "results.json").read_text())
    release = subprocess.check_output(["git", "-C", str(product), "rev-parse", "HEAD"], text=True).strip()
    if release != report["compilerCommit"] or release != "514f538024df990af86054af25975b756ba42ab1":
        parser.error("wrong frozen release")
    if subprocess.check_output(["git", "-C", str(product), "status", "--porcelain",
                                "--untracked-files=no"], text=True).strip():
        parser.error("product source has tracked changes")
    compiler = product / "src/Calor.Compiler/bin/Release/net10.0/calor.dll"
    hashes = [value for key, value in report["inputSha256"].items()
              if key.endswith("/Calor.Compiler/bin/Release/net10.0/calor.dll")]
    if len(hashes) != 1 or sha(compiler) != hashes[0]:
        parser.error("compiler DLL differs from SDK validation")
    output.mkdir()
    environment = {**os.environ, "CALOR_TELEMETRY": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    observations = []
    for task in report["tasks"]:
        name = task["id"]
        source_a = suite / name / "laundering/a/source.calr.txt"
        source_b = suite / name / "laundering/b/source.calr.txt"
        if source_a.read_bytes() != source_b.read_bytes():
            raise ValueError("arm inputs are not identical")
        if sha(source_a) != task["variants"]["laundering"]["a"]["sourceSha256"]:
            raise ValueError("SDK source artifact changed")
        directory = output / name
        directory.mkdir()
        source = directory / "source.calr"
        source.write_bytes(source_a.read_bytes())
        observation = {
            "kind": "deterministic-frozen-release-arm-discrimination",
            "task": name, "registeredShape": 7,
            "compilerCommit": release, "compilerConfiguration": "Release",
            "compilerSha256": hashes[0], "sourceSha256": sha(source),
            "agentInvoked": False, "suiteReexecuted": False, "arms": {},
        }
        for arm in ("A", "B"):
            flags = ["--permissive-effects"] if arm == "A" else []
            command = ["dotnet", str(compiler), "-i", str(source),
                       "-o", str(directory / f"{arm}.generated.cs"), "--no-telemetry", *flags]
            completed = subprocess.run(command, cwd=repo, env=environment,
                                       text=True, capture_output=True)
            recorded = {
                "command": command, "policyFlags": flags, "exitCode": completed.returncode,
                "stdout": completed.stdout, "stderr": completed.stderr,
            }
            (directory / f"{arm}.json").write_text(json.dumps(recorded, indent=2) + "\n")
            observation["arms"][arm] = recorded
        a, b = observation["arms"]["A"], observation["arms"]["B"]
        if a["exitCode"] != 0 or re.search(r"(?:warning|error) Calor\d+", a["stdout"] + a["stderr"]):
            raise ValueError(f"{name}: permissive arm was not diagnostic-free")
        if (b["exitCode"] != 1 or "unknown" not in b["stderr"]
                or set(re.findall(r"error Calor(\d+)", b["stderr"])) != {"0410"}):
            raise ValueError(f"{name}: unexpected strict-arm verdict")
        observation["verdictDifference"] = "A accepts without diagnostics; B rejects unknown with Calor0410."
        (directory / "observation.json").write_text(json.dumps(observation, indent=2) + "\n")
        observations.append(observation)
        print(name, "A accepts without diagnostics; B rejects unknown")
    if sha(compiler) != hashes[0]:
        raise ValueError("compiler changed while producing observations")
    (output / "index.json").write_text(json.dumps({
        "kind": "R8-observations-not-agent-data", "tasks": len(observations),
        "distinctShapes": [7], "observations": observations,
    }, indent=2) + "\n")


if __name__ == "__main__":
    main()
