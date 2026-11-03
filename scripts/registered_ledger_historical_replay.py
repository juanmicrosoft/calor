#!/usr/bin/env python3
"""Replay registered regression artifacts with their original producer trees."""

from __future__ import annotations

import argparse
import hashlib
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
    "binder": (
        "7daf2bc99ec98937d489cf2b8bb69388fcb930d5",
        "7daf2bc99ec98937d489cf2b8bb69388fcb930d5",
        "bench/phase0-agent-native/binder-source-coverage.json",
        "Calor.Compiler.Tests.BinderIncompleteRatchetTests.ConversionLeg_IncompleteCount_MatchesBaseline",
    ),
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
    "CALOR_UPDATE_BINDER_BASELINE",
    "CALOR_REGENERATE_HIGHER_ORDER_DEMAND_LEDGER",
    "CALOR_REGENERATE_EFFECT_RESOLVER_KEY_LEDGER",
    "CALOR_REGENERATE_CALOR0425_LEDGER",
    "CALOR_REGENERATE_PPE1_LEDGER",
)
TRANSLATOR_PRODUCER = "452afeb29e7bfbd5aff892336de722e79ca3c157"
TRANSLATOR_TEST = "Calor.Verification.Tests.ContractTranslatorSemanticsVersionGuardTests.TranslatorOutputMatchesCommittedBaseline"
TRANSLATOR_GUARD = "tests/Calor.Verification.Tests/ContractTranslatorSemanticsVersionGuardTests.cs"
DETERMINISM_CASES = "tests/TestData/DeterminismProtocol/cases.json"


def verify_result(path: Path, test: str, *, strict: bool = False) -> None:
    document = ET.parse(path)
    results = document.findall(
        ".//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult"
    )
    if len(results) != 1 or results[0].get("testName") != test or results[0].get("outcome") != "Passed":
        raise ValueError(f"historical replay requires exactly one executed passing {test}; no skips/empty runs")
    if strict:
        summary = document.find("./{*}ResultSummary")
        counters = document.find("./{*}ResultSummary/{*}Counters")
        if summary is None or summary.get("outcome") != "Completed" or counters is None \
                or any(counters.get(key) != "1" for key in ("total", "executed", "passed")) \
                or any(counters.get(key) != "0" for key in ("failed", "error", "timeout", "notExecuted")) \
                or any(value != "0" for key, value in counters.attrib.items()
                       if key not in ("total", "executed", "passed")):
            raise ValueError("historical replay requires exact 1 pass / 0 skips / 0 failures counters")


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


def replay_translator(record: Path) -> None:
    """Run the original v2 guard, retaining observed bytes even when acceptance fails."""
    from determinism_runner import check_isolation, invocation_env

    if any(key.upper().startswith(("CALOR_UPDATE_", "CALOR_REGENERATE_")) and value
           for key, value in os.environ.items()):
        raise ValueError("translator historical replay forbids regeneration flags")
    require_commit(TRANSLATOR_PRODUCER)
    original_cases = json.loads(git(
        "show", f"{TRANSLATOR_PRODUCER}:docs/plans/evidence/g2-1421/cases.json",
    ))
    artifact = next(a for a in original_cases["artifacts"] if a["name"] == "translator-fixture")
    registry = (ROOT / DETERMINISM_CASES).read_bytes()
    if artifact not in json.loads(registry)["artifacts"]:
        raise ValueError("original translator registration changed")
    original_guard = git("show", f"{TRANSLATOR_PRODUCER}:{TRANSLATOR_GUARD}")
    if any(part.encode() not in original_guard for part in artifact["expected"].split("|")):
        raise ValueError("original translator guard does not match the registered expectation")
    record = record.resolve()
    if record.exists():
        raise ValueError("translator replay record directory must be fresh")
    verifier = ROOT / "scripts/verify-z3-assets.py"
    subprocess.run([sys.executable, str(verifier)], check=True, timeout=60)
    with tempfile.TemporaryDirectory(prefix="calor-translator-history-") as temporary:
        scratch = Path(temporary)
        archive = scratch / "source.tar"
        git("archive", "--format=tar", f"--output={archive}", TRANSLATOR_PRODUCER)
        source = scratch / "source"
        extract_archive(archive, source)
        inputs = {path: hashlib.sha256(path.read_bytes()).digest()
                  for path in source.rglob("*") if path.is_file()}
        for manifest in (
            ".github/z3-binaries-4.15.7.sha256",
            "src/Calor.Compiler/scripts/z3-upstream-4.15.7.sha256",
        ):
            if (ROOT / manifest).read_bytes() != (source / manifest).read_bytes():
                raise ValueError(f"{manifest}: assets differ from original translator producer")
        for relative in runpy.run_path(str(verifier))["ASSET_PATHS"].values():
            destination = source / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, destination)
        subprocess.run(
            [sys.executable, str(source / "scripts/verify-z3-assets.py")], check=True, timeout=60,
        )
        invocation = scratch / "invocation"
        invocation.mkdir()
        environment = invocation_env(
            os.environ, invocation, os.environ.get("CALOR_Z3_EXPECTED_RID", ""),
            os.environ.get("NUGET_PACKAGES", str(scratch / "packages")),
        )
        environment["CALOR_DETERMINISM_RECORD_DIR"] = str(record)
        check_isolation(environment, invocation)
        Path(environment["HOME"]).mkdir()
        Path(environment["TEMP"]).mkdir(parents=True)
        environment["LC_ALL"] = "C"
        environment["CALOR_HOME"] = str(scratch / "empty-calor-home")
        Path(environment["CALOR_HOME"]).mkdir()
        project = "tests/Calor.Verification.Tests/Calor.Verification.Tests.csproj"
        print(f"translator historical exact replay: producer {TRANSLATOR_PRODUCER}", flush=True)
        run_dotnet(["restore", project, "--locked-mode"], source, environment, timeout=300)
        run_dotnet(
            ["test", project, "-c", "Release", "--no-restore",
             "--filter", f"FullyQualifiedName={TRANSLATOR_TEST}",
             "--logger", "trx;LogFileName=historical.trx",
             "--results-directory", str(record)],
            source, environment, timeout=600,
        )
        result = record / "historical.trx"
        verify_result(result, TRANSLATOR_TEST, strict=True)
        generated = Path(environment["CALOR_DETERMINISM_RECORD_DIR"]) / "generated/translator-fixture"
        actual = generated.read_bytes()
        if actual != artifact["expected"].encode("utf-8"):
            raise ValueError("original translator computation differs from its registered bytes")
        for path, digest in inputs.items():
            if hashlib.sha256(path.read_bytes()).digest() != digest:
                raise ValueError(f"{path.relative_to(source)}: replay changed an immutable producer input")
        subprocess.run(
            [sys.executable, str(source / "scripts/verify-z3-assets.py")], check=True, timeout=60,
        )
        if (ROOT / DETERMINISM_CASES).read_bytes() != registry:
            raise ValueError("translator registration changed during replay")


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
    if name not in ("formatter", "binder"):
        recorded = json.loads(final_ledger)
        identity_field = "MeasuredCommit" if name == "corpus" else "measuredCommit"
        if recorded[identity_field] != producer:
            raise ValueError("recorded producer differs from the original compiler selected for replay")
    artifacts = {ledger_path: final_ledger}
    if name == "binder":
        for relative, role in (
            ("bench/phase0-agent-native/binder-incomplete-baseline.json", "conversion baseline"),
            ("bench/phase0-agent-native/binder-source-carrier-evidence.json", "source-carrier evidence"),
        ):
            original = git("show", f"{landing}:{relative}")
            if (ROOT / relative).read_bytes() != original:
                raise ValueError(f"{relative}: immutable {role} differs from original landing {landing}")
            artifacts[relative] = original
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
        for relative, expected in artifacts.items():
            if name == "binder" and (source / relative).read_bytes() != expected:
                raise ValueError(f"{relative}: original producer artifact differs from final landing")
            (source / relative).write_bytes(expected)
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
        for relative, expected in artifacts.items():
            if (source / relative).read_bytes() != expected or (ROOT / relative).read_bytes() != expected:
                raise ValueError(f"{relative}: historical replay changed its immutable input")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ledger", choices=[*REPLAYS, "translator"])
    parser.add_argument("--record-dir", type=Path)
    args = parser.parse_args()
    try:
        if args.ledger == "translator":
            if args.record_dir is None:
                raise ValueError("translator replay requires --record-dir")
            replay_translator(args.record_dir)
        else:
            if args.record_dir is not None:
                raise ValueError("--record-dir is only valid for translator replay")
            replay(args.ledger)
    except (OSError, ValueError, subprocess.SubprocessError, ET.ParseError, tarfile.TarError) as error:
        print(f"ERROR: registered ledger historical replay failed: {error}", file=sys.stderr)
        raise SystemExit(1)
