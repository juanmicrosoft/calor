#!/usr/bin/env python3
"""Replay registered regression artifacts with their original producer trees."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET

from ppe1_historical_replay import ROOT, git, run_dotnet


REPLAYS = {
    "formatter": (
        "ab53adc3737bab92ff8637cb32235629c64ece9d",
        "ab53adc3737bab92ff8637cb32235629c64ece9d",
        "tests/TestData/Formatting/formatter-corpus-baseline.json",
        "Calor.Compiler.Tests.LosslessFormattingTests.CheckedInCalorCorpus_MatchesBaselineAndExercisesSafeTransformations",
    ),
    "demand": (
        "76270627daf26d4de30c1a98e078b2e2bd53fc8e",
        "01158abf6686378fd75f03e4778406be87ebe437",
        "bench/phase0-agent-native/higher-order-demand-ledger.json",
        "Calor.Compiler.Tests.HigherOrderDemandLedgerTests.DA_CalorNative_MatchesLedgerExactly",
    ),
    "key": (
        "76270627daf26d4de30c1a98e078b2e2bd53fc8e",
        "01158abf6686378fd75f03e4778406be87ebe437",
        "bench/phase0-agent-native/effect-resolver-key-ledger.json",
        "Calor.Compiler.Tests.EffectResolverKeyLedgerTests.KeyOrigins_OverCommittedCalorCorpus_MatchLedgerExactly",
    ),
    "corpus": (
        "4e87980e16225ca0a32e032aa746358e6bcecaa7",
        "c9105a72656030a3cbd00bdee4533d6a64956c24",
        "bench/phase0-agent-native/calor0425-corpus-ledger.json",
        "Calor.Compiler.Tests.Calor0425CorpusLedgerTests.Calor0425CorpusLedgerMatchesRecomputation",
    ),
}
REGENERATION_FLAGS = (
    "CALOR_REGENERATE_HIGHER_ORDER_DEMAND_LEDGER",
    "CALOR_REGENERATE_EFFECT_RESOLVER_KEY_LEDGER",
    "CALOR_REGENERATE_CALOR0425_LEDGER",
    "CALOR_REGENERATE_PPE1_LEDGER",
)


def verify_result(path: Path, test: str) -> None:
    results = ET.parse(path).findall(
        ".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult"
    )
    if len(results) != 1 or results[0].get("testName") != test or results[0].get("outcome") != "Passed":
        raise ValueError(f"historical replay requires exactly one executed passing {test}; no skips/empty runs")


def require_commit(commit: str) -> None:
    try:
        actual = git("rev-parse", "--verify", f"{commit}^{{commit}}").decode().strip()
    except subprocess.CalledProcessError as error:
        raise ValueError(f"historical commit {commit} unavailable; fetch full history before replay") from error
    if actual != commit:
        raise ValueError(f"historical commit identity mismatch: {commit}")


def extract_archive(archive: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as tree:
        tree.extractall(destination, filter="data")

def index_original_tree(producer: str, source: Path) -> None:
    # The original formatter test uses git ls-files. Its isolated index contains
    # exactly the producer's paths, never build outputs or today's active copies.
    subprocess.run(["git", "init", "--quiet", str(source)], check=True, timeout=60)
    subprocess.run(
        ["git", "-C", str(source), "update-index", "-z", "--index-info"],
        input=git("ls-tree", "-r", "-z", producer), check=True, timeout=60,
    )
    expected = git("ls-tree", "-r", "--name-only", "-z", producer)
    actual = subprocess.check_output(["git", "-C", str(source), "ls-files", "-z"], timeout=60)
    if actual != expected:
        raise ValueError("isolated tracked-file index differs from original producer tree")


def archive_submodules(producer: str, source: Path, scratch: Path) -> None:
    entries = git("ls-tree", producer, "bench/corpus/").decode().splitlines()
    subjects = [entry.split() for entry in entries if entry.startswith("160000 commit ")]
    if {entry[3] for entry in subjects} != {
        "bench/corpus/MediatR", "bench/corpus/serilog", "bench/corpus/FluentValidation",
    }:
        raise ValueError("recorded corpus must contain exactly the three registered submodule pins")
    for _, _, commit, relative in subjects:
        module = ROOT / relative
        try:
            top = subprocess.check_output(
                ["git", "-C", str(module), "rev-parse", "--show-toplevel"], timeout=60
            ).decode().strip()
            if Path(top).resolve() != module.resolve():
                raise ValueError(f"{relative}: submodule is not initialized; initialize it before replay")
            subprocess.run(
                ["git", "-C", str(module), "cat-file", "-e", f"{commit}^{{commit}}"],
                check=True, timeout=60,
            )
        except subprocess.CalledProcessError as error:
            raise ValueError(
                f"{relative}: original submodule commit {commit} unavailable; initialize its object store before replay"
            ) from error
        archive = scratch / f"{module.name}.tar"
        subprocess.run(
            ["git", "-C", str(module), "archive", "--format=tar", f"--output={archive}", commit],
            check=True, timeout=60,
        )
        extract_archive(archive, source / relative)


def replay(name: str) -> None:
    for flag in REGENERATION_FLAGS:
        if os.environ.get(flag):
            raise ValueError(f"historical replay forbids {flag}")
    producer, landing, ledger_path, test = REPLAYS[name]
    require_commit(producer)
    require_commit(landing)
    final_ledger = git("show", f"{landing}:{ledger_path}")
    if (ROOT / ledger_path).read_bytes() != final_ledger:
        raise ValueError(f"{ledger_path}: immutable final ledger differs from original landing {landing}")
    if name != "formatter":
        recorded = json.loads(final_ledger)
        identity_field = "MeasuredCommit" if name == "corpus" else "measuredCommit"
        if recorded[identity_field] != producer:
            raise ValueError("recorded producer differs from the original compiler selected for replay")
    verifier = ROOT / "scripts/verify-z3-assets.py"
    subprocess.run([sys.executable, str(verifier)], check=True, timeout=60)
    with tempfile.TemporaryDirectory(prefix=f"calor-{name}-history-") as temporary:
        scratch = Path(temporary)
        archive = scratch / "source.tar"
        git("archive", "--format=tar", f"--output={archive}", producer)
        source = scratch / "source"
        extract_archive(archive, source)
        if name == "formatter":
            index_original_tree(producer, source)
        archive_submodules(producer, source, scratch)
        for manifest in (
            ".github/z3-binaries-4.15.7.sha256",
            "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256",
        ):
            if (ROOT / manifest).read_bytes() != (source / manifest).read_bytes():
                raise ValueError(f"{manifest}: native asset manifest differs from recorded producer {producer}")
        for relative in runpy.run_path(str(verifier))["ASSET_PATHS"].values():
            destination = source / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, destination)
        subprocess.run(
            [sys.executable, str(source / "scripts/verify-z3-assets.py")], check=True, timeout=60,
        )
        (source / ledger_path).write_bytes(final_ledger)
        environment = os.environ.copy()
        environment["LC_ALL"] = "C"
        environment["CALOR_HOME"] = str(scratch / "empty-calor-home")
        Path(environment["CALOR_HOME"]).mkdir()
        project = "tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj"
        print(f"{name} historical exact replay: compiler {producer}; ledger landing {landing}", flush=True)
        run_dotnet(["restore", project, "--locked-mode"], source, environment, timeout=300)
        run_dotnet(
            [
                "test", project, "--no-restore", "--filter", f"FullyQualifiedName={test}",
                "--logger", "trx;LogFileName=historical.trx",
                "--results-directory", str(scratch / "results"),
            ],
            source, environment, timeout=600,
        )
        verify_result(scratch / "results/historical.trx", test)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ledger", choices=REPLAYS)
    args = parser.parse_args()
    try:
        replay(args.ledger)
    except (OSError, ValueError, subprocess.SubprocessError, ET.ParseError, tarfile.TarError) as error:
        print(f"ERROR: registered ledger historical replay failed: {error}", file=sys.stderr)
        raise SystemExit(1)
