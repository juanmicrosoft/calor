#!/usr/bin/env python3
"""Inspect or apply the single reviewed #1434 invalid-preserving gateway recovery."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys


BENCH = Path(__file__).resolve().parent


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(condition, message):
    if not condition:
        raise ValueError("PP-W recovery operator: " + message)


def canonical_inputs():
    adjudication = module("ppw-pilot-adjudicate.py")
    manifest = adjudication.validate_analysis_registration()
    require(manifest.get("recoveryStatus") == "terminal-semantics-registered",
            "the reviewed #1434 terminal recovery registration is not active")
    registration = module("ppw-gateway-registration.py")
    spending = module("ppw-spending.py")
    evidence, documents = registration.load_recovery_evidence()
    authorization = documents["recoveryAuthorization"]
    proof = documents["inspectionProof"]
    ledger = spending.gateway_ledger_location(authorization["ledgerBinding"])
    failed_archive = BENCH / authorization["failedArchivePath"]
    protected = ledger.parent
    backup = protected / authorization["backupName"]
    values = {
        "ledger_path": ledger,
        "failed_archive": failed_archive,
        "protected_root": protected,
        "backup_path": backup,
        "expected_old_binding": authorization["oldBinding"],
        "target_binding": evidence["targetBinding"],
        "expected_ledger_sha256": authorization["failedLedgerSha256"],
        "expected_archive_inventory_sha256":
            authorization["failedArchiveInventorySha256"],
        "recovery_registration_sha256": evidence["recoveryAuthorization"]["sha256"],
    }
    return values, proof


def inspect():
    recovery = module("ppw-gateway-recovery.py")
    values, registered = canonical_inputs()
    observed = recovery.inspect_zero_request_recovery(**values)
    require(observed == registered,
            "current read-only proof differs from the registered reviewed proof")
    return observed


def apply(confirmed_proof_sha256):
    recovery = module("ppw-gateway-recovery.py")
    values, registered = canonical_inputs()
    require(confirmed_proof_sha256 == registered.get("proofSha256"),
            "explicit confirmation must name the registered proof SHA-256")
    return recovery.apply_zero_request_recovery(
        **values, confirmed_proof_sha256=confirmed_proof_sha256)


class SingleOption(argparse.Action):
    def __call__(self, parser, namespace, values, option_string=None):
        if getattr(namespace, self.dest, None) is not None:
            parser.error("%s must occur exactly once" % option_string)
        setattr(namespace, self.dest, values)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    subparsers = parser.add_subparsers(dest="operation", required=True)
    subparsers.add_parser(
        "inspect", help="read and compare the live ledger/archive with reviewed evidence")
    apply_parser = subparsers.add_parser(
        "apply", help="apply only the already reviewed proof; never starts collection")
    apply_parser.add_argument(
        "--confirmed-proof-sha256", required=True, action=SingleOption)
    args = parser.parse_args(argv)
    try:
        result = inspect() if args.operation == "inspect" else apply(
            args.confirmed_proof_sha256)
        print(json.dumps(result, indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    sys.exit(main())
