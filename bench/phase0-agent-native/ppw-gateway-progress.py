#!/usr/bin/env python3
"""Outcome-blind, read-only accounting progress; never opens a mutable ledger."""
import argparse
import importlib.util
import json
from pathlib import Path
import sqlite3


BENCH = Path(__file__).resolve().parent


def module(filename):
    spec = importlib.util.spec_from_file_location(
        "progress_" + filename.replace("-", "_"), BENCH / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def read_progress(path):
    core = module("ppw-gateway-disposition.py")
    path = core._absolute(path, "progress ledger", True)
    core._regular_single_link(path, "progress ledger")
    connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro",
                                 uri=True, isolation_level=None, timeout=5)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA query_only=ON")
        connection.execute("BEGIN")
        scope, requests, events = core._database_snapshot(connection)
        snapshot = core._snapshot(scope, requests, events)
        context = module("ppw-gateway-budget.py").RequestLedger._disposition_context(connection)
        replay = None
        if context is not None:
            authorization, authorization_sha256 = context
            replay = core.validate_history(
                snapshot, authorization=authorization,
                authorization_sha256=authorization_sha256,
                target_binding=snapshot["binding"])
            if scope["owner"] is not None:
                core.validate_owner_binding(
                    scope["owner"], snapshot["binding"], replay["proofSha256"])
        connection.execute("COMMIT")
    finally:
        connection.close()
    slots = snapshot["binding"]["plannedSlots"]
    core._require(isinstance(slots, list) and len(slots) == 444,
                  "progress requires the original 444-slot population")
    core._validate_slot_order(slots)
    accounted = set()
    for event in events:
        detail = core._event_detail(event)
        if event["kind"] == "zero-request-recovery":
            accounted.update(detail["preservedAttemptedSlots"])
        elif event["kind"] == core.DISPOSITION_EVENT:
            accounted.update(item["slot"] for item in detail["authorization"]["preservedAttempts"])
        elif event["kind"] in ("slot-complete", "slot-terminal-invalid"):
            core._require(detail["slot"] not in accounted, "duplicate progress terminal")
            accounted.add(detail["slot"])
    core._require(accounted <= set(slots)
                  and len(accounted) == snapshot["accountedSlots"],
                  "progress accounting inventory differs")
    historical_ids = set(replay["historicalUnknownRequestIds"]) if replay else set()
    unknown = [row for row in requests if row["state"] == "unknown"]
    reserved = [row for row in requests if row["state"] == "reserved"]
    bearing = {row["slot"] for row in requests}
    core._require(bearing <= set(slots)
                  and all(type(row["charge"]) is int and row["charge"] >= 0 for row in requests)
                  and 0 <= snapshot["exposureMicroUsd"] <= snapshot["ceilingMicroUsd"],
                  "progress liability or request inventory differs")
    return {
        "schemaVersion": 1,
        "kind": "pp-w-outcome-blind-accounting-progress-v1",
        "outcomeBlind": True,
        "state": snapshot["state"],
        "epochId": snapshot["binding"]["epochId"],
        "plannedSlots": len(slots),
        "accountedSlots": len(accounted),
        "validTerminalSlots": snapshot["validCompletedSlots"],
        "ordinaryInvalidTerminalSlots": snapshot["invalidTerminalSlots"],
        "historicalRawInvalidDispositionSlots": 1 if replay else 0,
        "historicalDispositionVerified": replay is not None,
        "remainingNonterminalSlots": len(slots) - len(accounted),
        "requestBearingSlots": len(bearing),
        "requestBearingNonterminalSlots": len(bearing - accounted),
        "unknownRequestCount": len(unknown),
        "historicalUnknownRequestCount": len(historical_ids),
        "liveUnknownRequestCount": sum(row["id"] not in historical_ids for row in unknown),
        "inflightReservedRequestCount": len(reserved),
        "permanentUnknownMicroUsd": replay["permanentUnknownMicroUsd"] if replay else 0,
        "retainedUnreconciledExposureMicroUsd": sum(row["charge"] for row in unknown + reserved),
        "reconciledUsageChargeMicroUsd": sum(
            row["charge"] for row in requests if row["state"] == "reconciled"),
        "exposureMicroUsd": snapshot["exposureMicroUsd"],
        "ceilingMicroUsd": snapshot["ceilingMicroUsd"],
        "headroomMicroUsd": snapshot["ceilingMicroUsd"] - snapshot["exposureMicroUsd"],
        "historicalActualCost": None,
        "accountingComplete": snapshot["state"] == "complete" and len(accounted) == len(slots),
        "terminalCountersDoNotReportTaskSuccess": True,
        "remainingNonterminalDoesNotMeanUntouched": True,
        "stage2Authorized": False,
    }


def main():
    argparse.ArgumentParser(description=__doc__, allow_abbrev=False).parse_args()
    registration = module("ppw-gateway-disposition-registration.py")
    spending = module("ppw-spending.py")
    if registration.digest(registration.OLD_AUTHORIZATION) != registration.EXPECTED_OLD_AUTHORIZATION_SHA256:
        raise ValueError("historical funding anchor differs")
    funding = registration.load(registration.OLD_AUTHORIZATION)
    path = spending.gateway_ledger_location(funding["ledgerBinding"])
    print(json.dumps(read_progress(path), sort_keys=True, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
