#!/usr/bin/env python3
"""Recompute PP-E1 with its recorded compiler, never with translated sources."""

from __future__ import annotations

import hashlib
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent.parent
COMMIT = "72d060f400b5787ba4ec11cb4dd8860c95173da3"
LEDGER = "bench/phase0-agent-native/effect-rows-probe-ledger.json"
LEDGER_BLOB = "dbed938313c3cc90d5f49fd0c015c595f1cec173"
TEST = "Calor.Compiler.Tests.EffectRowsProbeLedgerTests.PpE1LedgerMatchesRecomputation"
FROZEN_INPUTS = [
    f"docs/design/spikes/effect-rows/after/{name}.calr"
    for name in ("A2", "A2-broadening", "A3-map", "A3-match", "A3-middleware", "A3-callback")
] + [
    "docs/design/spikes/effect-rows/spike-verdict.json",
    "bench/phase0-agent-native/epochs/e1-rows-parity-001/ppe1-analysis.json",
]


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), *args], timeout=60)


def require_bytes(path: Path, expected: bytes) -> None:
    if path.read_bytes() != expected:
        raise ValueError(f"{path}: frozen replay input differs from compiler commit {COMMIT}")


def verify_result(path: Path) -> None:
    document = ET.parse(path)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    results = document.findall(".//t:UnitTestResult", ns)
    if len(results) != 1 or results[0].get("testName") != TEST or results[0].get("outcome") != "Passed":
        raise ValueError("historical replay must execute exactly the named test once and pass (no skips)")


def run_dotnet(arguments: list[str], source: Path, environment: dict[str, str], timeout: int) -> None:
    environment = environment.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
    subprocess.run(
        ["dotnet", *arguments, "--disable-build-servers", "-nr:false", "-p:UseSharedCompilation=false"],
        cwd=source, env=environment, check=True, timeout=timeout,
    )


def replay() -> None:
    if os.environ.get("CALOR_REGENERATE_PPE1_LEDGER"):
        raise ValueError("historical replay forbids CALOR_REGENERATE_PPE1_LEDGER")
    try:
        resolved = git("rev-parse", "--verify", f"{COMMIT}^{{commit}}").decode().strip()
    except subprocess.CalledProcessError as error:
        raise ValueError(
            f"recorded compiler {COMMIT} unavailable; fetch full repository history"
        ) from error
    if resolved != COMMIT:
        raise ValueError("recorded compiler commit unavailable; fetch full repository history")
    ledger = (ROOT / LEDGER).read_bytes()
    header = f"blob {len(ledger)}\0".encode("ascii")
    if hashlib.sha1(header + ledger).hexdigest() != LEDGER_BLOB:
        raise ValueError("PP-E1 historical ledger bytes changed; replay does not regenerate or amend them")
    for relative in FROZEN_INPUTS:
        require_bytes(ROOT / relative, git("show", f"{COMMIT}:{relative}"))

    # Reuse only independently verified dependencies, never an existing compiler binary.
    verifier = ROOT / "scripts/verify-z3-assets.py"
    subprocess.run([sys.executable, str(verifier)], check=True, timeout=60)
    with tempfile.TemporaryDirectory(prefix="calor-ppe1-history-") as temporary:
        scratch = Path(temporary)
        archive = scratch / "source.tar"
        git("archive", "--format=tar", f"--output={archive}", COMMIT)
        source = scratch / "source"
        source.mkdir()
        with tarfile.open(archive) as tree:
            tree.extractall(source, filter="data")
        for manifest in (
            ".github/z3-binaries-4.15.7.sha256",
            "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256",
        ):
            require_bytes(ROOT / manifest, (source / manifest).read_bytes())
        for relative in runpy.run_path(str(verifier))["ASSET_PATHS"].values():
            destination = source / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, destination)
        subprocess.run(
            [sys.executable, str(source / "scripts/verify-z3-assets.py")], check=True, timeout=60
        )

        # Release #1111 changed only measuredCommit after this compiler commit.
        # Test that final immutable ledger, not the pre-release ledger in the archive.
        (source / LEDGER).write_bytes(ledger)
        environment = os.environ.copy()
        environment["LC_ALL"] = "C"
        environment["CALOR_HOME"] = str(scratch / "empty-calor-home")
        Path(environment["CALOR_HOME"]).mkdir()
        environment.pop("CALOR_REGENERATE_PPE1_LEDGER", None)
        project = "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj"
        print(f"PP-E1 historical exact replay: compiler {COMMIT}", flush=True)
        run_dotnet(
            ["restore", project, "--locked-mode"],
            source, environment, timeout=300,
        )
        run_dotnet(
            [
                "test", project, "-c", "Release", "--no-restore",
                "--filter", f"FullyQualifiedName={TEST}",
                "--logger", "trx;LogFileName=historical.trx",
                "--results-directory", str(scratch / "results"),
            ],
            source, environment, timeout=600,
        )
        verify_result(scratch / "results/historical.trx")


if __name__ == "__main__":
    try:
        replay()
    except (OSError, ValueError, subprocess.SubprocessError, ET.ParseError) as error:
        print(f"ERROR: PP-E1 historical replay failed: {error}", file=sys.stderr)
        raise SystemExit(1)
