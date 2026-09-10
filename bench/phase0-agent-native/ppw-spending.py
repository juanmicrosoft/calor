#!/usr/bin/env python3
"""Conservative PP-W spending reservations; estimated CLI costs are not hard caps."""
import argparse
from contextlib import contextmanager
from decimal import Decimal, InvalidOperation, ROUND_CEILING
import hashlib
import importlib.util
import json
from pathlib import Path
import secrets
import sqlite3
import subprocess
import sys

SCALE = 1_000_000
CONTROL = "claude-code-max-budget-usd"
GATEWAY = "pp-w-request-gateway-v1"
ROOT = Path(__file__).resolve().parent
COLLECTION_ARTIFACTS = (
    "run-pair.sh", "harness-capture.py", "ppw-instrument.py", "ppw-registration.py",
    "ppw-spending.py", "ppw-source-assembly.py", "ppw-source-inspection.py",
    "source-inspection/Program.cs", "source-inspection/PpwSourceInspector.csproj",
    "token-usage.py", "token-usage.sh", "telemetry-helpers.py", "ppw-pins.schema.json",
    "templates/calor-arm/CalorArm.csproj.template", "templates/calor-arm/policy-canary.calr.txt",
)
GATEWAY_ARTIFACTS = COLLECTION_ARTIFACTS + (
    "ppw-gateway-budget.py", "ppw-budget-gateway.py", "ppw-gateway-client.py",
    "ppw-run-observer.py",
    "gateway-tools/python3",
    "templates/calor-arm/CalorArm.Gateway.csproj.template",
    "ppw-test-host.py", "test-host/Program.cs", "test-host/PpwXunitHost.csproj",
    "ppw-gateway-registration.py",
)


def require(condition, message):
    if not condition:
        raise ValueError("PP-W spending: " + message)


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def artifact_manifest(mechanism=CONTROL):
    require(mechanism in (CONTROL, GATEWAY), "unknown collector mechanism")
    return {name: digest(ROOT / name) for name in
            (GATEWAY_ARTIFACTS if mechanism == GATEWAY else COLLECTION_ARTIFACTS)}


def module(filename):
    spec = importlib.util.spec_from_file_location(filename.replace("-", "_"), ROOT / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def gateway_ledger_location(binding):
    require(isinstance(binding, dict) and binding.get("anchor") == "git-common-dir"
            and binding.get("relativePath") == "ppw-budget/epic1254-pilot.sqlite3",
            "fixed project-wide pilot ledger binding required")
    result = subprocess.run(["git", "-C", str(ROOT.parent.parent), "rev-parse", "--git-common-dir"],
                            text=True, capture_output=True, check=True)
    common = Path(result.stdout.strip())
    if not common.is_absolute():
        common = ROOT.parent.parent / common
    common = common.resolve(strict=True)
    require(hashlib.sha256(str(common).encode()).hexdigest() == binding.get("anchorSha256"),
            "another clone/state root cannot reset the authorized pilot budget")
    return common / binding["relativePath"]


def pinned_document(directory, proof, name):
    require(isinstance(proof, dict), "pinned %s reference required" % name)
    relative = proof.get("path")
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute()
            and ".." not in Path(relative).parts, "a pinned relative %s is required" % name)
    path = Path(directory) / relative
    require(not any(p.is_symlink() for p in (path, *path.parents))
            and path.is_file() and digest(path) == proof.get("sha256"),
            "%s is missing, linked, or changed" % name)
    return path, json.loads(path.read_text(encoding="utf-8"))


def units(value, *, observed=False):
    require(type(value) in (str, int, float, Decimal), "USD value must be numeric, not Boolean")
    try:
        amount = Decimal(str(value))
    except InvalidOperation as error:
        raise ValueError("PP-W spending: invalid USD value") from error
    require(amount.is_finite() and amount >= 0, "USD value must be finite and nonnegative")
    scaled = amount * SCALE
    rounded = scaled.to_integral_value(rounding=ROUND_CEILING)
    require(observed or rounded == scaled, "supplied USD values require at most six decimal places")
    require(rounded <= 2 ** 63 - 1, "USD value exceeds accounting range")
    return int(rounded)


def dollars(value):
    return format(Decimal(value) / SCALE, "f")


def protocol_identity(registration, selected, stage, epoch_id):
    value = {
        "epochId": epoch_id, "stage": stage, "compilerCommit": registration["compilerCommit"],
        "tasks": registration["tasks"], "artifacts": registration["artifacts"],
        "runsPerArm": selected["runsPerArm"], "modelPin": selected["modelPin"],
        "agentVersion": selected["agentVersion"],
        "stageRegistration": selected["stageRegistration"],
        "modelRegistration": selected["modelRegistration"],
        "instrumentAmendment": selected["instrumentAmendment"],
        "policies": {"A": ["--permissive-effects"], "B": []},
    }
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def verified_upper_bound(control):
    """Only implemented, evidenced bounds may authorize a financial reservation.

    A caller's JSON assertion cannot promote a locally estimated threshold into
    a provider-enforced upper bound. Adding an actual bounded provider adapter
    requires a separately reviewed implementation and prospective registration.
    """
    require(control.get("kind") == CONTROL, "unsupported client spending control")
    require(units(control.get("limitUsd")) > 0, "client spending limit must be positive")
    return None


def validate_forecast(plan, ceiling, slot_count):
    forecast = plan.get("forecast", {})
    require(isinstance(forecast, dict) and forecast.get("status") == "registered"
            and type(forecast.get("plannedInvocations")) is int
            and forecast["plannedInvocations"] == slot_count
            and type(forecast.get("experimentalObservations")) is int
            and forecast["experimentalObservations"] == 0
            and isinstance(forecast.get("method"), str) and forecast["method"].strip(),
            "the independent, prospective full-pilot cost forecast is not registered")
    require(0 < units(forecast.get("estimatedFullPilotUsd")) <= ceiling,
            "the prospective full-pilot point forecast does not fit the authorized ceiling")


def admit(registration, selected, authorization, directory, epoch_id, stage):
    require(stage == "pilot", "this implementation admits pilot-only scope, not stage 2")
    path, plan = pinned_document(directory, selected.get("spendingPlan", {}), "spendingPlan")
    require(isinstance(plan, dict) and plan.get("schemaVersion") == 1
            and plan.get("kind") == "pp-w-pilot-spending-plan", "structured spending plan required")
    require(plan.get("stage") == stage and plan.get("epochId") == epoch_id,
            "spending plan belongs to another stage or epoch")
    require(plan.get("authorizationSha256") == selected["spendAuthorization"]["sha256"],
            "spending plan does not bind the supplied authorization")
    _, amendment = pinned_document(directory, selected.get("instrumentAmendment", {}),
                                   "instrumentAmendment")
    require(isinstance(amendment, dict) and amendment.get("schemaVersion") == 1
            and amendment.get("kind") == "pp-w-prospective-spending-instrument-amendment"
            and amendment.get("stage") == "pilot", "prospective instrument amendment required")
    control = plan.get("clientControl", {})
    require(isinstance(control, dict), "client control must be explicit")
    harness = artifact_manifest(control.get("kind", CONTROL))
    require(amendment.get("replacementHarnessArtifacts") == harness,
            "instrument source differs from the prospectively registered artifact manifest")
    require(plan.get("protocolSha256") == protocol_identity(registration, selected, stage, epoch_id),
            "spending plan changes or omits frozen protocol/model/task identities")
    ceiling = units(authorization["spendingCeilingUsd"])
    require(ceiling > 0 and units(plan.get("ceilingUsd")) == ceiling,
            "spending plan ceiling differs from the supplied authorization")
    require(plan.get("costBasis") == "both-list-price-study-cost-and-actual-spend",
            "subscription usage does not exempt study cost from the ceiling")
    slots = [
        {"id": "%s/%s/%d" % (task, arm, run), "task": task, "arm": arm, "run": run}
        for run in range(1, selected["runsPerArm"] + 1)
        for task in registration["tasks"] for arm in ("calor-permissive", "calor-strict")
    ]
    require(type(plan.get("plannedInvocations")) is int and plan["plannedInvocations"] == len(slots),
            "full unchanged registered slot inventory is required, not a budget-sized subset")
    if control.get("kind") == GATEWAY:
        validate_forecast(plan, ceiling, len(slots))
        policy = module("ppw-gateway-budget.py")
        isolation = module("ppw-gateway-client.py")
        require(selected["modelPin"] == policy.MODEL
                and selected["agentVersion"] == isolation.CLIENT_VERSION, "gateway model/client differs")
        _, prices = pinned_document(directory, control.get("priceContract", {}), "priceContract")
        require(prices == policy.price_contract(), "price contract differs from implemented bounds")
        require(control.get("isolation") == isolation.ISOLATION, "unsupported gateway isolation")
        require(plan.get("ledgerBinding") == authorization.get("ledgerBinding"),
                "authorization and plan must bind the same non-resettable ledger")
        isolation.validate_platform()
        client = isolation.validate_client(control.get("clientExecutable"))
        shell = isolation.validate_shell(control.get("shellExecutable"), control.get("shellSha256"))
        isolation.validate_runtime(control.get("executionRuntime"))
        source_inspector = module("ppw-source-inspection.py")
        source_inspector.validate_runtime(control.get("sourceInspector"))
        ledger = gateway_ledger_location(plan["ledgerBinding"])
        policy.RequestLedger(ledger)
        return {
            "mechanism": GATEWAY, "epochId": epoch_id, "stage": stage, "ceilingUnits": ceiling,
            "authorizationSha256": selected["spendAuthorization"]["sha256"],
            "protocolSha256": plan["protocolSha256"], "planSha256": digest(path),
            "ledgerPath": str(ledger), "costBasis": plan["costBasis"], "harnessArtifacts": harness,
            "clientExecutable": str(client), "priceSha256": policy.price_identity(),
            "shellExecutable": str(shell), "shellSha256": control["shellSha256"],
            "runtimeSha256": control.get("runtimeSha256"), "slots": slots,
            "testHost": control.get("testHost"),
            "sourceInspector": control.get("sourceInspector"),
            "executionRuntime": control.get("executionRuntime"),
        }
    ledger = plan.get("ledgerPath")
    require(isinstance(ledger, str) and Path(ledger).is_absolute(),
            "one pinned absolute shared ledger path is required across output roots")
    require(ledger == authorization.get("spendingLedgerPath"),
            "the authorization must bind the same shared ledger; alternate plans cannot reset spending")
    Ledger(ledger)
    maximum = verified_upper_bound(control)
    require(maximum is not None,
            "--max-budget-usd is a locally estimated threshold; no trustworthy upper bound "
            "covering invoice/study cost, in-flight requests, retries and descendants is available. "
            "Collection refused before any invocation.")
    require(type(maximum) is int and maximum > 0 and maximum * len(slots) <= ceiling,
            "the full unchanged pilot cannot fit the authorized ceiling under safe bounds")
    return {
        "epochId": epoch_id, "stage": stage, "ceilingUnits": ceiling,
        "authorizationSha256": selected["spendAuthorization"]["sha256"],
        "protocolSha256": plan["protocolSha256"], "planSha256": digest(path),
        "ledgerPath": ledger, "costBasis": plan["costBasis"],
        "harnessArtifacts": harness,
        "clientLimitUnits": units(control["limitUsd"]), "maximumUnits": maximum, "slots": slots,
    }


class Ledger:
    """Single-use scope ledger. No reservation is refunded from a cost estimate."""
    def __init__(self, path):
        self.path = Path(path)
        require(self.path.is_absolute() and ".." not in self.path.parts,
                "ledger path must be absolute and canonical")
        require(not any(p.is_symlink() for p in (self.path, *self.path.parents)),
                "linked ledger path is forbidden")
        require(not self.path.exists() or self.path.stat().st_nlink == 1,
                "hard-linked ledger is forbidden")

    @contextmanager
    def transaction(self):
        connection = sqlite3.connect(str(self.path), timeout=15, isolation_level=None)
        connection.row_factory = sqlite3.Row
        try:
            connection.execute("PRAGMA synchronous=FULL")
            connection.execute("BEGIN IMMEDIATE")
            yield connection
            connection.execute("COMMIT")
        except BaseException:
            if connection.in_transaction:
                connection.execute("ROLLBACK")
            raise
        finally:
            connection.close()

    def initialize(self, admission):
        require(str(self.path) == admission["ledgerPath"], "alternate ledger path is forbidden")
        ceiling, maximum = admission["ceilingUnits"], admission["maximumUnits"]
        slots = admission["slots"]
        require(type(ceiling) is int and 0 < ceiling <= 2 ** 63 - 1
                and type(maximum) is int and maximum > 0
                and slots and maximum * len(slots) <= ceiling,
                "full planned liabilities do not fit the ceiling")
        require(type(admission["clientLimitUnits"]) is int
                and 0 < admission["clientLimitUnits"] <= maximum,
                "supported client threshold must fit its verified maximum liability")
        require(len({slot["id"] for slot in slots}) == len(slots), "duplicate planned slots")
        self.path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        with self.transaction() as db:
            db.execute("CREATE TABLE IF NOT EXISTS scope "
                       "(id INTEGER PRIMARY KEY CHECK(id=1), plan TEXT NOT NULL, "
                       "state TEXT NOT NULL, owner TEXT, reason TEXT)")
            db.execute("CREATE TABLE IF NOT EXISTS attempts "
                       "(slot TEXT PRIMARY KEY, ordinal INTEGER UNIQUE NOT NULL, details TEXT NOT NULL, "
                       "state TEXT NOT NULL, token TEXT UNIQUE, maximum INTEGER NOT NULL, "
                       "client_limit INTEGER NOT NULL, estimate INTEGER, exit_code INTEGER)")
            db.execute("CREATE TABLE IF NOT EXISTS events "
                       "(sequence INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, "
                       "slot TEXT, details TEXT NOT NULL, "
                       "created_utc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')))")
            existing = db.execute("SELECT plan FROM scope WHERE id=1").fetchone()
            if existing:
                require(existing["plan"] == canonical(admission), "ledger scope/pins/ceiling changed")
                return
            db.execute("INSERT INTO scope VALUES (1,?,'ready',NULL,NULL)", (canonical(admission),))
            for ordinal, slot in enumerate(slots):
                db.execute("INSERT INTO attempts VALUES (?,?,?,'unattempted',NULL,?,?,NULL,NULL)",
                           (slot["id"], ordinal, canonical(slot), maximum, admission["clientLimitUnits"]))
            self.event(db, "initialized", None, {"ceilingUnits": ceiling, "plannedSlots": len(slots)})

    @staticmethod
    def event(db, kind, slot, details):
        db.execute("INSERT INTO events(kind,slot,details) VALUES (?,?,?)",
                   (kind, slot, canonical(details)))

    def start(self):
        owner = secrets.token_hex(32)
        with self.transaction() as db:
            scope = db.execute("SELECT state FROM scope WHERE id=1").fetchone()
            require(scope and scope["state"] == "ready",
                    "scope is already active/completed/interrupted; no duplicate collection or reset")
            db.execute("UPDATE scope SET state='collecting',owner=? WHERE id=1", (owner,))
            self.event(db, "collection-started", None, {})
        return owner

    def reserve(self, owner, slot):
        token = secrets.token_hex(32)
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope WHERE id=1").fetchone()
            require(scope and scope["state"] == "collecting" and scope["owner"] == owner,
                    "collection is not active under this owner")
            attempt = db.execute("SELECT * FROM attempts WHERE slot=?", (slot,)).fetchone()
            require(attempt and attempt["state"] == "unattempted",
                    "unregistered, duplicate or retry invocation refused")
            exposure = db.execute(
                "SELECT COALESCE(SUM(MAX(maximum,COALESCE(estimate,0))),0) AS value "
                "FROM attempts WHERE state!='unattempted'").fetchone()["value"]
            require(exposure + attempt["maximum"] <= json.loads(scope["plan"])["ceilingUnits"],
                    "safe reservation cannot fit remaining authorized budget")
            db.execute("UPDATE attempts SET state='reserved',token=? WHERE slot=?", (token, slot))
            self.event(db, "reserved", slot, {"maximumUnits": attempt["maximum"]})
        return {"ledgerPath": str(self.path), "slot": slot, "token": token}

    def claim(self, ticket, task, arm, run):
        require(ticket.get("ledgerPath") == str(self.path), "ticket names a different ledger")
        with self.transaction() as db:
            scope = db.execute("SELECT state,plan FROM scope WHERE id=1").fetchone()
            require(scope and scope["state"] == "collecting", "budget scope is stopped")
            require(json.loads(scope["plan"])["harnessArtifacts"] == artifact_manifest(),
                    "instrument source changed before invocation")
            attempt = db.execute("SELECT * FROM attempts WHERE slot=?", (ticket.get("slot"),)).fetchone()
            require(attempt and attempt["state"] == "reserved"
                    and attempt["token"] == ticket.get("token"), "ticket is missing, consumed or invalid")
            details = json.loads(attempt["details"])
            require((details["task"], details["arm"], details["run"]) == (task, arm, run),
                    "ticket is for another task/arm/run")
            db.execute("UPDATE attempts SET state='in-flight' WHERE slot=?", (ticket["slot"],))
            self.event(db, "invocation-claimed", ticket["slot"], {})
            return dollars(attempt["client_limit"])

    def settle(self, owner, ticket, report, exit_code):
        estimate = None
        if isinstance(report, dict) and "total_cost_usd" in report:
            try:
                estimate = units(report["total_cost_usd"], observed=True)
            except ValueError:
                pass
        budget_stop = isinstance(report, dict) and report.get("subtype") == "error_max_budget_usd"
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope WHERE id=1").fetchone()
            require(scope and scope["owner"] == owner, "settlement owner mismatch")
            attempt = db.execute("SELECT * FROM attempts WHERE slot=?", (ticket.get("slot"),)).fetchone()
            require(attempt and attempt["token"] == ticket.get("token")
                    and attempt["state"] in ("reserved", "in-flight"), "duplicate/unknown settlement")
            reason = ("invocation-not-claimed" if attempt["state"] != "in-flight"
                      else "unknown-cost-or-interruption" if estimate is None or exit_code < 0
                      or exit_code == 124 or exit_code >= 128
                      else "upper-bound-contradicted" if estimate > attempt["maximum"]
                      else "client-budget-exhausted" if budget_stop
                      else "client-limit-exceeded" if estimate > attempt["client_limit"] else None)
            state = "uncertain" if reason else "completed" if exit_code == 0 else "failed"
            db.execute("UPDATE attempts SET state=?,estimate=?,exit_code=? WHERE slot=?",
                       (state, estimate, exit_code, ticket["slot"]))
            self.event(db, "settled", ticket["slot"],
                       {"state": state, "estimatedUnits": estimate, "exitCode": exit_code, "reason": reason})
            if reason:
                db.execute("UPDATE scope SET state='incomplete',reason=? WHERE id=1", (reason,))
        return reason

    def stop(self, owner, reason):
        with self.transaction() as db:
            scope = db.execute("SELECT owner,state FROM scope WHERE id=1").fetchone()
            require(scope and scope["owner"] == owner, "stop owner mismatch")
            if scope["state"] != "complete":
                db.execute("UPDATE scope SET state='incomplete',reason=COALESCE(reason,?) WHERE id=1",
                           (reason,))
                self.event(db, "collection-stopped", None, {"reason": reason})

    def complete(self, owner):
        with self.transaction() as db:
            scope = db.execute("SELECT * FROM scope WHERE id=1").fetchone()
            require(scope and scope["owner"] == owner and scope["state"] == "collecting",
                    "incomplete budget scope cannot complete")
            require(db.execute("SELECT COUNT(*) AS n FROM attempts "
                               "WHERE state NOT IN ('completed','failed')").fetchone()["n"] == 0,
                    "all unchanged planned slots must complete")
            db.execute("UPDATE scope SET state='complete' WHERE id=1")
            self.event(db, "all-planned-attempts-complete", None, {})

    def snapshot(self):
        with self.transaction() as db:
            scope = dict(db.execute("SELECT * FROM scope WHERE id=1").fetchone())
            attempts = [dict(row) for row in db.execute(
                "SELECT slot,state,maximum,client_limit,estimate,exit_code FROM attempts ORDER BY ordinal")]
            events = [dict(row) for row in db.execute(
                "SELECT sequence,kind,slot,details,created_utc FROM events ORDER BY sequence")]
        plan = json.loads(scope["plan"])
        exposure = sum(max(row["maximum"], row["estimate"] or 0)
                       for row in attempts if row["state"] != "unattempted")
        return {"kind": "pp-w-conservative-spend-ledger", "epochId": plan["epochId"],
                "stage": plan["stage"], "state": scope["state"], "reason": scope["reason"],
                "ceilingUsd": dollars(plan["ceilingUnits"]), "reservedExposureUsd": dollars(exposure),
                "reportedCostsAreEstimates": True, "reservationsRefunded": False,
                "plannedInvocations": len(attempts), "attempts": attempts,
                "events": events,
                "protocolSha256": plan["protocolSha256"], "planSha256": plan["planSha256"],
                "authorizationSha256": plan["authorizationSha256"], "verdict": None}


def main():
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("operation", choices=("claim",))
    parser.add_argument("--ticket", required=True)
    parser.add_argument("--task", required=True)
    parser.add_argument("--arm", required=True)
    parser.add_argument("--run", type=int, required=True)
    args = parser.parse_args()
    ticket = json.loads(Path(args.ticket).read_text(encoding="utf-8"))
    print(Ledger(ticket["ledgerPath"]).claim(ticket, args.task, args.arm, args.run))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, TypeError, sqlite3.Error) as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
