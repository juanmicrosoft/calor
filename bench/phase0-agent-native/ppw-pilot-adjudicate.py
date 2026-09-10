#!/usr/bin/env python3
"""Read-only integration of the frozen PP-W stage-1 method; never collects runs."""
import argparse
from fractions import Fraction
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import sys


BENCH = Path(__file__).resolve().parent
METHOD_ROOT = BENCH / "registrations/ppw-rows-stage1"
MANIFEST = METHOD_ROOT / "analysis-registration.json"
METHOD = "registrations/ppw-rows-stage1/registration.json"
MODEL = "registrations/ppw-rows-stage1/model-registration.json"
TASKS = "registrations/ppw-redesign-task-supersession.json"
PILOT_PINS = "epochs/w-rows-pilot-001/pins.json"
GUARDED_PROJECTION = "registrations/ppw-rows-stage1/guarded-analysis-projection.json"
SPENDING_AMENDMENT = "registrations/ppw-rows-stage1/spending-instrument-amendment.json"
PRE_PROJECTION_MANIFEST = "registrations/ppw-rows-stage1/analysis-registration.pre-projection-1403.json"
ARTIFACTS = (
    "ppw-pilot-adjudicate.py", "registrations/ppw-rows-stage1/precision.py",
    "ppw-instrument.py", "ppw-registration.py", "ppw-source-assembly.py",
    "ppw-source-inspection.py", "harness-capture.py", "token-usage.py",
    "telemetry-helpers.py", "ppw-pins.schema.json", "effect-rows-benefit-ledger.json",
    METHOD, MODEL, TASKS, PILOT_PINS,
    GUARDED_PROJECTION, SPENDING_AMENDMENT, PRE_PROJECTION_MANIFEST,
)
CELL_FIELDS = {
    "pair", "arm", "plannedRuns", "validRuns", "invalidRuns", "censoredRuns",
    "escapes", "shapeRealized", "didNotBuildAtDeclaredDone",
    "namedTestFailuresWithoutEffect", "outputTokens", "unscorableHeldoutRuns",
    "invalidReasons", "optionsHashes", "changedPublicApiRuns",
    "unscorablePublicApiRuns", "unscorableShapeRuns", "escapeRate", "shapeRealizedRate",
}


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result
    return json.loads(Path(path).read_text(encoding="utf-8"), object_pairs_hook=unique)


def module(name, relative):
    spec = importlib.util.spec_from_file_location(name, BENCH / relative)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def validate_analysis_registration():
    manifest = load(MANIFEST)
    require(manifest.get("schemaVersion") == 1
            and manifest.get("kind") == "pp-w-rows-stage1-analysis-registration",
            "unrecognized pilot analysis registration")
    expected = manifest.get("artifacts", {})
    require(set(expected) == set(ARTIFACTS), "analysis artifact inventory differs")
    for relative, sha in expected.items():
        require(digest(BENCH / relative) == sha, "analysis artifact changed: " + relative)
    require(manifest.get("supersedes") == {
        "path": PRE_PROJECTION_MANIFEST, "sha256": expected[PRE_PROJECTION_MANIFEST]},
        "prior analysis registration must be explicitly preserved")
    require(manifest.get("guardedExecutionProjection") == {
        "path": GUARDED_PROJECTION, "sha256": expected[GUARDED_PROJECTION]},
        "guarded execution projection is not registered")
    projection = load(BENCH / GUARDED_PROJECTION)
    require(projection.get("schemaVersion") == 1
            and projection.get("kind") == "pp-w-rows-guarded-analysis-projection"
            and projection.get("stage") == "pilot"
            and projection.get("collectionAuthorized") is False,
            "unrecognized guarded analysis projection")
    require(projection.get("allowedDataKinds") == ["synthetic"]
            and projection.get("empiricalAdmissionRegistered") is False,
            "this inactive projection does not register empirical admission")
    require(projection.get("baselinePins") == {
        "path": PILOT_PINS, "sha256": expected[PILOT_PINS]},
        "guarded projection changed the preserved baseline")
    proof = {"path": SPENDING_AMENDMENT, "sha256": expected[SPENDING_AMENDMENT]}
    require(projection.get("instrumentAmendment") == proof
            and manifest.get("instrumentAmendment") == proof,
            "guarded projection does not bind the registered instrument amendment")
    amendment = load(BENCH / SPENDING_AMENDMENT)
    require(amendment.get("schemaVersion") == 1
            and amendment.get("kind") == "pp-w-prospective-spending-instrument-amendment"
            and amendment.get("stage") == "pilot"
            and amendment.get("collectionAuthorized") is False
            and amendment.get("supersededHarnessArtifacts")
            == load(BENCH / PILOT_PINS)["harnessArtifacts"],
            "instrument amendment does not preserve the historical execution inventory")
    return manifest


def execution_projection(pins, selected, epoch=None):
    baseline = load(BENCH / PILOT_PINS)
    if "instrumentAmendment" not in selected and "spendingPlan" not in selected:
        require(pins.get("harnessArtifacts") == baseline["harnessArtifacts"],
                "collection execution artifacts differ from the prospective pins")
        return {"id": "preserved-eleven-artifact-baseline",
                "authority": {"path": PILOT_PINS, "sha256": digest(BENCH / PILOT_PINS)}}

    projection = load(BENCH / GUARDED_PROJECTION)
    proof = selected.get("instrumentAmendment", {})
    require(isinstance(proof, dict) and set(proof) == {"path", "sha256"},
            "guarded execution requires a pinned instrument amendment")
    relative = proof["path"]
    require(isinstance(relative, str) and relative and all(
            not path.is_absolute() and not path.drive and not path.root and ".." not in path.parts
            for path in (PurePosixPath(relative), PureWindowsPath(relative))),
            "guarded instrument amendment requires a relative evidence path")
    require(proof["sha256"] == projection["instrumentAmendment"]["sha256"],
            "guarded execution selects an unregistered instrument amendment")
    evidence = None
    if epoch is not None:
        evidence = Path(epoch)
        require(not evidence.is_symlink(), "linked guarded evidence root")
        for part in Path(relative).parts:
            evidence /= part
            require(not evidence.is_symlink(), "linked guarded instrument amendment evidence")
    if evidence is not None and evidence.exists():
        require(evidence.is_file() and digest(evidence) == proof["sha256"],
                "archive-local instrument amendment evidence differs from its registered hash")
        resolution = "verified-archive-local-file"
    else:
        require(proof == projection["instrumentAmendment"],
                "noncanonical instrument amendment requires verified archive-local evidence")
        resolution = "exact-committed-authority-reference"
    amendment = load(BENCH / SPENDING_AMENDMENT)
    require(pins.get("harnessArtifacts") == amendment["replacementHarnessArtifacts"],
            "guarded execution inventory differs; no downgrade or mixed source inventories")
    require(pins["dataKind"] in projection["allowedDataKinds"],
            "guarded empirical analysis is not activated by this prospective projection")
    return {"id": projection["id"],
            "authority": {"path": GUARDED_PROJECTION, "sha256": digest(BENCH / GUARDED_PROJECTION)},
            "instrumentAmendment": projection["instrumentAmendment"],
            "selectedInstrumentAmendment": proof, "evidenceResolution": resolution}


def validate_scope(pins, registration, method, epoch_id, stage, epoch=None):
    instrument = module("pilot_scope_instrument", "ppw-instrument.py")
    require(stage == "pilot", "pilot analysis cannot select a confirmatory stage")
    instrument.validate_pins(pins, registration, stage, epoch_id)
    require(pins["suite"] == method["tasks"], "task mixture differs from the registered method")
    require(pins["runsPerArm"] == method["runsPerArm"], "allocation differs from the registered method")
    require(pins["modelPin"] == method["prospectiveModelPin"]
            and pins["agentVersion"] == method["prospectiveAgentVersion"],
            "model/client differs from the registered method")
    require(pins["compiler"]["commit"] == method["compilerCommit"],
            "compiler differs from the registered method")
    selected = registration["stages"]["pilot"]
    for key, relative in (("stageRegistration", METHOD), ("modelRegistration", MODEL)):
        proof = selected.get(key, {})
        instrument.local(Path("."), proof.get("path"))
        require(proof.get("sha256") == digest(BENCH / relative),
                key + " does not bind the registered method/model evidence")
    task_proof = registration.get("taskSupersession", {})
    instrument.local(Path("."), task_proof.get("path"))
    require(task_proof.get("sha256") == digest(BENCH / TASKS), "task supersession proof differs")
    frozen = load(BENCH / TASKS)
    for key in ("tasks", "artifacts", "sourceInspections", "compilerCommit",
                "supersededPins", "replacementPins"):
        require(registration.get(key) == frozen[key], "frozen task registration differs: " + key)
    prospective = load(BENCH / PILOT_PINS)
    require(pins["compiler"] == prospective["compiler"], "prospective shared product pins differ")
    execution_projection(pins, selected, epoch)
    require(re.fullmatch(r"[0-9a-f]{40}", pins.get("harnessCommit", "")),
            "collection commit identity is missing")
    return instrument


def run_ids(cell, name, n):
    values = cell[name]
    require(isinstance(values, list) and all(type(v) is int and 1 <= v <= n for v in values)
            and len(set(values)) == len(values), "invalid or duplicate run IDs: " + name)
    return set(values)


def aggregate(report, pins, method):
    """Use integer counts, never the instrument's rounded display rates."""
    require(report.get("epoch") == pins["epochId"], "cross-epoch cell report")
    require(report.get("stage") == pins["stage"] == "pilot", "wrong-stage cell report")
    require(report.get("dataKind") == pins["dataKind"]
            and pins["dataKind"] in ("empirical", "synthetic")
            and type(report.get("empirical")) is bool
            and report["empirical"] is (pins["dataKind"] == "empirical"),
            "synthetic/empirical report identity differs")
    require(report.get("registrationSha256") == pins["registrationSha256"]
            and report.get("compiler") == pins["compiler"] and report.get("arms") == pins["arms"],
            "cell report provenance differs from its epoch pins")
    expected = {(task, arm) for task in method["tasks"] for arm in ("A", "B")}
    cells = report.get("perCell")
    require(isinstance(cells, list) and len(cells) == len(expected), "missing or extra cells")
    seen, shape_cells, escape_cells, bookkeeping = set(), [], [], []
    for cell in cells:
        require(isinstance(cell, dict) and set(cell) == CELL_FIELDS, "cell fields differ from instrument")
        key = (cell["pair"], cell["arm"])
        require(key in expected and key not in seen, "foreign or duplicate task/arm cell")
        seen.add(key)
        n = pins["runsPerArm"]
        for name in ("plannedRuns", "validRuns", "invalidRuns", "censoredRuns",
                     "escapes", "shapeRealized", "didNotBuildAtDeclaredDone"):
            require(type(cell[name]) is int and 0 <= cell[name] <= n, "invalid count: " + name)
        valid, invalid = cell["validRuns"], cell["invalidRuns"]
        require(cell["plannedRuns"] == n == valid + invalid, "scheduled attempts were lost or replaced")
        require(invalid <= cell["censoredRuns"] <= n, "invalid attempts are not retained as censored")
        require(cell["didNotBuildAtDeclaredDone"] <= valid, "nonbuilding count exceeds eligibility")
        reasons = cell["invalidReasons"]
        require(isinstance(reasons, list) and len(reasons) == invalid
                and all(isinstance(v, dict) and set(v) == {"run", "reason"}
                        and type(v["run"]) is int and 1 <= v["run"] <= n
                        and isinstance(v["reason"], str) and v["reason"].strip() for v in reasons),
                "invalid attempt accounting is incomplete")
        invalid_ids = {v["run"] for v in reasons}
        require(len(invalid_ids) == invalid, "duplicate invalid attempt")
        lists = {name: run_ids(cell, name, n) for name in (
            "unscorableShapeRuns", "unscorableHeldoutRuns", "unscorablePublicApiRuns",
            "changedPublicApiRuns", "namedTestFailuresWithoutEffect")}
        require(all(not ids.intersection(invalid_ids) for ids in lists.values()),
                "invalid attempts entered eligible outcome accounting")
        shape_unknown = lists["unscorableShapeRuns"]
        escape_unknown = lists["unscorableHeldoutRuns"] | lists["unscorablePublicApiRuns"]
        require(len(escape_unknown) <= valid - cell["didNotBuildAtDeclaredDone"],
                "unscorable escape outcomes exceed built runs")
        changed = lists["changedPublicApiRuns"]
        require(not changed.intersection(lists["unscorablePublicApiRuns"] | shape_unknown),
                "public API cannot be both changed and unscorable")
        require(cell["shapeRealized"] <= valid - len(shape_unknown), "shape success exceeds scorable runs")
        require(cell["escapes"] <= valid - len(escape_unknown | changed)
                and cell["escapes"] <= valid - cell["didNotBuildAtDeclaredDone"],
                "escape success exceeds eligible built unchanged-contract runs")
        shape_cells.append((cell["shapeRealized"], valid, len(shape_unknown)))
        if cell["arm"] == "A":
            escape_cells.append((cell["escapes"], valid, len(escape_unknown)))
        bookkeeping.append({
            "task": cell["pair"], "arm": cell["arm"], "scheduled": n,
            "eligible": valid, "invalid": invalid, "censored": cell["censoredRuns"],
            "shapeSuccesses": cell["shapeRealized"],
            "armAEscapeSuccesses": cell["escapes"] if cell["arm"] == "A" else None,
            "nonbuilding": cell["didNotBuildAtDeclaredDone"], "changedPublicApi": len(changed),
            "unscorableShape": len(shape_unknown), "unscorableEscape": len(escape_unknown),
        })
    require(seen == expected, "incomplete fixed task/arm mixture")
    precision = module("pilot_registered_precision", "registrations/ppw-rows-stage1/precision.py")
    error = method["precision"]["familyErrorProbability"]
    shape = precision.fixed_mixture_band(shape_cells, 2 * len(method["tasks"]), error)
    escape = precision.fixed_mixture_band(escape_cells, len(method["tasks"]), error)

    def exact(value):
        ratio = value["exactEstimate"]
        return Fraction(ratio["numerator"], ratio["denominator"]) if ratio else None

    shape_point, escape_point = exact(shape), exact(escape)
    rules = method["offRamps"]
    require(rules["shapeRealizationBelow"] == 0.5 and rules["armAEscapeExactly"] == 0
            and rules["confidenceBoundsReplacePointRules"] is False,
            "the frozen point-estimate stopping rules changed")
    shape_stop = None if shape_point is None else shape_point < Fraction(1, 2)
    escape_stop = None if escape_point is None else escape_point == 0
    consequences = []
    if shape_stop:
        consequences.append("STOP_STAGE2_PROTOCOL_DEFECT_R6")
    if escape_stop:
        consequences.append("STOP_STAGE2_REDESIGN_UNSUCCESSFUL_R1_R2")
    evaluated = ("STOP_STAGE2" if consequences else "UNIDENTIFIED"
                 if shape_stop is None or escape_stop is None else "NO_REGISTERED_POINT_STOP")
    return {
        "estimands": {"shapeRealizationRate": shape, "armAEscapeRate": escape},
        "stoppingRules": {"shapeRealizationBelowHalf": shape_stop, "armAEscapeExactlyZero": escape_stop},
        "decision": {
            "evaluatedStatus": evaluated,
            "status": evaluated if report["empirical"] else "SYNTHETIC_ONLY",
            "appliedToEmpiricalData": report["empirical"],
            "prescribedConsequences": consequences,
            "stage2Authorized": False, "confirmatoryVerdict": None,
        },
        "accounting": sorted(bookkeeping, key=lambda row: (row["task"], row["arm"])),
    }


def inventory(epoch, instrument):
    instrument.isolated_tree(epoch)
    values = {path.relative_to(epoch).as_posix(): digest(path)
              for path in sorted(epoch.rglob("*")) if path.is_file()}
    encoded = json.dumps(values, sort_keys=True, separators=(",", ":")).encode()
    return {"files": len(values), "sha256": hashlib.sha256(encoded).hexdigest()}


def reject_synthetic_markers(epoch, pins):
    if pins["dataKind"] != "empirical":
        return
    for result in (epoch / "runs").rglob("result.json"):
        for path in (result, result.parent / "agent.json"):
            if path.is_file():
                require(load(path).get("synthetic") is not True,
                        "declared synthetic fixture cannot be analyzed as empirical")


def adjudicate(epochs_root, epoch_id, stage="pilot"):
    manifest = validate_analysis_registration()
    method = load(BENCH / METHOD)
    instrument = module("pilot_readonly_instrument", "ppw-instrument.py")
    instrument.identifier(epoch_id)
    require(stage == "pilot", "pilot analysis cannot select a confirmatory stage")
    epoch = instrument.local(epochs_root, epoch_id)
    before = inventory(epoch, instrument)
    pins, registration = load(epoch / "pins.json"), load(epoch / "registration.json")
    validate_scope(pins, registration, method, epoch_id, stage, epoch)
    reject_synthetic_markers(epoch, pins)
    report = instrument.analyze(epochs_root, epoch_id, stage)
    result = aggregate(report, pins, method)
    require(inventory(epoch, instrument) == before, "epoch changed during read-only analysis")
    require(validate_analysis_registration() == manifest, "analysis registration changed while reading")
    return {
        "schemaVersion": 1, "kind": "pp-w-rows-stage1-adjudication",
        "epoch": epoch_id, "stage": stage, "dataKind": report["dataKind"],
        "empirical": report["empirical"], "collectionAuthorized": False,
        "pilotPoolingAllowed": False, "stage2N": None, "stage2Delta": None,
        "precision": method["precision"], **result,
        "provenance": {
            "epochInventory": before, "epochPinsSha256": digest(epoch / "pins.json"),
            "epochRegistrationSha256": digest(epoch / "registration.json"),
            "analysisRegistrationSha256": digest(MANIFEST),
            "analysisArtifacts": manifest["artifacts"],
            "collectionHarnessCommit": pins["harnessCommit"],
            "collectionHarnessArtifacts": pins["harnessArtifacts"],
            "collectionExecutionProjection": execution_projection(
                pins, registration["stages"]["pilot"], epoch),
            "modelPin": pins["modelPin"], "agentVersion": pins["agentVersion"],
            "compiler": pins["compiler"],
            "countsSource": "recomputed from this epoch's raw archive by ppw-instrument.analyze",
            "savedDescriptiveLedgerUsedAsAuthority": False,
        },
    }


class SingleOption(argparse.Action):
    def __call__(self, parser, namespace, values, option_string=None):
        if getattr(namespace, self.dest, None) is not None:
            parser.error("%s must occur exactly once" % option_string)
        setattr(namespace, self.dest, values)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--epoch-id", required=True, action=SingleOption)
    parser.add_argument("--stage", required=True, choices=("pilot",), action=SingleOption)
    parser.add_argument("--epochs-root", default=str(BENCH / "epochs"))
    parser.add_argument("--out", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.out:
            require(not any(args.out.resolve().is_relative_to(root.resolve())
                            for root in (Path(args.epochs_root), BENCH / "epochs")),
                    "analysis output must be outside immutable epoch roots")
            require(not args.out.exists(), "analysis output already exists; never overwrite")
        result = adjudicate(args.epochs_root, args.epoch_id, args.stage)
        text = json.dumps(result, indent=2, sort_keys=True) + "\n"
        if args.out:
            with args.out.open("x", encoding="utf-8") as stream:
                stream.write(text)
        else:
            print(text, end="")
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(2, "ERROR: %s\n" % error)
    return 0


if __name__ == "__main__":
    sys.exit(main())
