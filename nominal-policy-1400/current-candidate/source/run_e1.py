#!/usr/bin/env python3
"""Run fixed E1 product subjects using a previously materialized D1 compiler."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

BASE = "8f9891a1a07f786a76a293017959c3edf28412bf"
SUBJECTS = ["Synthetic", "Synthetic2", "MediatR", "Serilog", "FluentValidation"]


def command(args, cwd=None):
    return subprocess.check_output(args, cwd=cwd, text=True).strip()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path, value):
    with path.open("x", encoding="utf-8") as output:
        json.dump(value, output, indent=2)
        output.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compiler-root", required=True, type=Path)
    parser.add_argument("--materialization", required=True, type=Path)
    parser.add_argument("--corpus", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = args.compiler_root.resolve()
    out = args.output.resolve()
    materialization = json.loads(args.materialization.read_text())
    assert command(["git", "rev-parse", "HEAD"], root) == BASE
    assert materialization["acceptedSourceCommit"] == BASE
    mode = materialization["mode"]
    assert mode in {"current", "shadow-annotated", "shadow-conservative"}
    for row in materialization["transformations"]:
        assert digest(root / row["path"]) == row["afterSha256"], row["path"]
    for row in materialization["addedSources"]:
        assert digest(root / row["path"]) == row["sha256"], row["path"]
    script_root = Path(command(["git", "rev-parse", "--show-toplevel"], Path(__file__).parent))
    subprocess.run(["git", "diff", "--exit-code", "HEAD", "--", str(Path(__file__).resolve())],
                   cwd=script_root, check=True, stdout=subprocess.DEVNULL)
    harness = root / "tools/Calor.RoundTrip.Harness/bin/Debug/net10.0/Calor.RoundTrip.Harness.dll"
    compiler = harness.parent / "calor.dll"
    assert harness.is_file() and compiler.is_file()
    corpus_pins = {}
    for name in ["MediatR", "serilog", "FluentValidation"]:
        subject = args.corpus.resolve() / name
        revision = command(["git", "rev-parse", "HEAD"], subject)
        gitlink = command(["git", "ls-tree", BASE, "--", "bench/corpus/" + name], root).split()[2]
        assert revision == gitlink, (name, revision, gitlink)
        assert not command(["git", "status", "--porcelain"], subject), name
        corpus_pins[name] = revision
    out.mkdir(parents=True, exist_ok=False)
    for directory in ["logs", "reports", "captures", "runtime"]:
        (out / directory).mkdir()
    # Neutral parent files prevent unrelated checkout-level MSBuild inheritance.
    for name, content in {
        "Directory.Build.props": "<Project />\n",
        "Directory.Build.targets": "<Project />\n",
        "Directory.Packages.props": (
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false"
            "</ManagePackageVersionsCentrally></PropertyGroup></Project>\n"
        ),
    }.items():
        (out / "runtime" / name).write_text(content, encoding="utf-8")
    environment = os.environ.copy()
    environment.pop("CALOR_NO_TYPE_CHECK", None)
    environment.update({
        "D1_CAPTURE_DIR": str(out / "captures"),
        "TMPDIR": str(out / "runtime"),
        "DOTNET_ROLL_FORWARD": "Major",
        "CALOR_TELEMETRY": "0",
    })
    common = ["--dotnet", "dotnet", "--projects-dir", str(args.corpus.resolve()),
              "--capture-binding-analysis", "--test-attempts", "2", "--output", str(out / "reports")]
    write_json(out / "provenance.json", {
        "acceptedCompilerSource": BASE,
        "sourceArtifactCommit": command(["git", "rev-parse", "HEAD"], script_root),
        "runnerSha256": digest(Path(__file__)),
        "mode": mode,
        "startedUtc": datetime.now(timezone.utc).isoformat(),
        "compiler": {"path": str(compiler), "sha256": digest(compiler)},
        "harness": {"path": str(harness), "sha256": digest(harness)},
        "materialization": materialization,
        "dotnetInfo": command(["dotnet", "--info"]),
        "corpusPins": corpus_pins,
        "subjects": SUBJECTS,
        "declaredAttemptsPerE1Leg": 2,
        "declaredSuiteSlots": 20,
        "compilerApi": {
            "enforceEffects": False, "contractMode": "Off",
            "deferGeneratedOutputValidation": True,
            "isDefaultCompilationEvidence": False,
        },
        "environment": {key: environment.get(key) for key in [
            "D1_CAPTURE_DIR", "TMPDIR", "DOTNET_ROLL_FORWARD", "CALOR_TELEMETRY",
            "CALOR_NO_TYPE_CHECK", "DOTNET_ROOT", "NUGET_PACKAGES",
        ]},
        "parentIsolationFiles": {
            path.name: digest(path) for path in sorted((out / "runtime").iterdir())
        },
        "commonArguments": common,
        "retryRule": "Two fixed complete suite attempts per E1 leg; no retry to green.",
    })
    outcomes = []
    for subject in SUBJECTS:
        invocation = ["dotnet", str(harness), "run", subject] + common
        started = datetime.now(timezone.utc).isoformat()
        with (out / "logs" / (subject + ".log")).open("xb") as log:
            result = subprocess.run(invocation, cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
        row = {
            "subject": subject, "command": invocation, "startedUtc": started,
            "completedUtc": datetime.now(timezone.utc).isoformat(), "exitCode": result.returncode,
        }
        outcomes.append(row)
        write_json(out / ("exit-" + subject + ".json"), row)
        print(json.dumps(row), flush=True)
    write_json(out / "completed.json", {
        "mode": mode, "outcomes": outcomes,
        "completedUtc": datetime.now(timezone.utc).isoformat(),
        "allHarnessInvocationsSucceeded": all(row["exitCode"] == 0 for row in outcomes),
    })
    return 0 if all(row["exitCode"] == 0 for row in outcomes) else 1


if __name__ == "__main__":
    sys.exit(main())
