#!/usr/bin/env python3
"""Inspect or apply the single explicitly activated #1436 liability disposition."""
import argparse
import importlib.util
import json
from pathlib import Path


BENCH = Path(__file__).resolve().parent


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), BENCH / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def require(condition, message):
    if not condition:
        raise ValueError("PP-W disposition operator: " + message)


def canonical_inputs():
    return module("ppw-gateway-disposition-registration.py").canonical_inputs()


def inspect():
    values, registered = canonical_inputs()
    observed = module("ppw-gateway-disposition.py").inspect_disposition(**values)
    require(observed == registered, "current evidence differs from the reviewed disposition proof")
    return observed


def apply(confirmed_proof_sha256):
    values, registered = canonical_inputs()
    require(confirmed_proof_sha256 == registered.get("proofSha256"),
            "confirmation must name the registered proof SHA-256")
    return module("ppw-gateway-disposition.py").apply_disposition(
        **values, confirmed_proof_sha256=confirmed_proof_sha256)


class SingleOption(argparse.Action):
    def __call__(self, parser, namespace, value, option_string=None):
        if getattr(namespace, self.dest, None) is not None:
            parser.error("%s must occur exactly once" % option_string)
        setattr(namespace, self.dest, value)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    commands = parser.add_subparsers(dest="operation", required=True)
    commands.add_parser(
        "inspect", help="read-only comparison; exact final approvals and metadata activation required")
    apply_parser = commands.add_parser("apply", help="apply once; never starts collection")
    apply_parser.add_argument("--confirmed-proof-sha256", required=True, action=SingleOption)
    args = parser.parse_args(argv)
    try:
        result = inspect() if args.operation == "inspect" else apply(args.confirmed_proof_sha256)
        print(json.dumps(result, indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
