#!/usr/bin/env python3
"""0.24 G2 (#1421): build cases.json once, at registration, from dotnet test --list-tests output.

usage: freeze_cases.py --verification-list V.txt --compiler-list C.txt --compiler-class <FQ class> ...
The output is frozen by sha256.json; changing it after merge is a protocol amendment.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[4] / "scripts"))
from determinism_protocol import (CASES, INVENTORY, ORACLE_REPORT, PROTOCOL, ROOT, derive_cells, load,  # noqa: E402
                                  profile_by_id)



def listed_names(path: Path) -> list[str]:
    lines = path.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")
    return [line[4:] for line in lines if line.startswith("    ") and line.strip()]


def group_entry(project: str, names: list[str], classes=None) -> dict:
    counts: dict[str, int] = {}
    for name in names:
        counts[name] = counts.get(name, 0) + 1
    entry = {"project": project, "count": len(names)}
    if classes is not None:
        entry["classes"] = classes
    entry["tests"] = [{"name": n, "multiplicity": counts[n]} for n in sorted(counts, key=lambda s: s.encode("utf-8"))]
    return entry


def cmd_freeze_cases(args) -> int:
    protocol = load(ROOT, PROTOCOL)
    profiles = profile_by_id(protocol)
    classes = sorted(args.compiler_class)
    compiler = [n for n in listed_names(Path(args.compiler_list)) if n.startswith(tuple(c + "." for c in classes))]
    inv = next(a for a in load(ROOT, INVENTORY)["artifacts"] if a["id"] == "verifier-runtime-differential")
    cells = derive_cells(load(ROOT, ORACLE_REPORT))
    document = {
        "schemaVersion": 1, "issue": 1421, "protocolVersion": protocol["protocolVersion"],
        "derivation": {
            "tests": "dotnet test <project> -c Release --no-build --list-tests at registrationBase.commit plus the G2 change (osx-arm64, SDK 10.0.401). The listed names, as a multiset, equal the testName fields of the ordinary-CI TRX files of run 37137354219 (linux-x64) for the same projects, before G2 added one test.",
            "compilerClasses": "Calor.Compiler.Tests classes in files that construct the verifier or Z3 directly (Z3ContextFactory, VerifyContracts = true, ContractVerificationPass, ObligationSolver, Z3ImplicationProver, Z3.IsAvailable) at the registration base.",
            "cells": "derive_cells over bench/phase0-agent-native/verifier-runtime-differential.json: applicable forms in report order x positions (precondition, postcondition, obligation) x depth 1..3 x polarity (provable, refutable), numbered case-000001 upward exactly as DifferentialGate.GenerateCases numbers them.",
        },
        "groups": {
            "verification": group_entry(profiles["verification-full"]["project"], listed_names(Path(args.verification_list))),
            "compiler-verifier": group_entry(profiles["compiler-verifier"]["project"], compiler, classes),
        },
        "cells": {"source": ORACLE_REPORT, "count": len(cells), "ids": cells},
        "artifacts": [{"name": Path(p).name, "committedPath": p} for p in inv["paths"]],
    }
    (ROOT / CASES).write_bytes((json.dumps(document, indent=1, ensure_ascii=False) + "\n").encode("utf-8"))
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--verification-list", required=True)
    parser.add_argument("--compiler-list", required=True)
    parser.add_argument("--compiler-class", action="append", required=True)
    sys.exit(cmd_freeze_cases(parser.parse_args()))
