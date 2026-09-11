#!/usr/bin/env python3
"""Build prospective #1436 registrations without applying a disposition or running a slot."""
import argparse
import importlib.util
import json
from pathlib import Path


BENCH = Path(__file__).resolve().parent


def registration():
    spec = importlib.util.spec_from_file_location(
        "ppw_disposition_registration_generator",
        BENCH / "ppw-gateway-disposition-registration.py")
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--contract", action="store_true")
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--refresh-unexecuted-proposal", action="store_true")
    parser.add_argument("--activate", action="store_true",
                        help="prospective metadata only; requires three exact final approvals")
    parser.add_argument("--confirmed-record-set-sha256")
    args = parser.parse_args(argv)
    if args.refresh_unexecuted_proposal and (not args.write or args.contract):
        parser.error("--refresh-unexecuted-proposal requires --write without --contract")
    if args.activate and (args.contract or args.write or args.refresh_unexecuted_proposal):
        parser.error("--activate cannot be combined with proposal or contract options")
    if args.activate != bool(args.confirmed_record_set_sha256):
        parser.error("--activate requires --confirmed-record-set-sha256 (activation only)")
    try:
        helper = registration()
        if args.contract:
            print(json.dumps(helper.registration_contract(), indent=2, sort_keys=True))
            return 0
        if args.activate:
            print(json.dumps(helper.activate(args.confirmed_record_set_sha256),
                             indent=2, sort_keys=True))
            return 0
        documents = helper.build_documents()
        if args.write:
            helper.write_documents(
                documents, refresh_unexecuted=args.refresh_unexecuted_proposal)
        print(json.dumps({
            "kind": "pp-w-prospective-disposition-registration",
            "written": args.write,
            "operationalStateWritten": False,
            "collectionStarted": False,
            "activated": False,
            "finalApprovalsRequired": list(helper.FINAL_SUBJECTS),
            "outputs": helper.document_identities(documents),
        }, indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
