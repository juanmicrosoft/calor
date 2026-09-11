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
    args = parser.parse_args(argv)
    if args.refresh_unexecuted_proposal and (not args.write or args.contract):
        parser.error("--refresh-unexecuted-proposal requires --write without --contract")
    try:
        helper = registration()
        if args.contract:
            print(json.dumps(helper.registration_contract(), indent=2, sort_keys=True))
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
            "outputs": helper.document_identities(documents),
        }, indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
