"""Reduce completed D1 measurements into deterministic, reviewable artifacts."""

from __future__ import annotations

import argparse
import collections
import gzip
import hashlib
import io
import json
import shutil
import tarfile
from pathlib import Path
from typing import Any

MODES = ("current", "shadow-annotated", "shadow-conservative")
SUBJECTS = ("Synthetic", "Synthetic2", "MediatR", "Serilog", "FluentValidation")
SOURCE_COMMIT = "8f9891a1a07f786a76a293017959c3edf28412bf"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("raw_root", type=Path)
    parser.add_argument("output", type=Path)
    return parser.parse_args()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def canonical_bytes(value: Any) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def stable_id(prefix: str, value: Any) -> str:
    return f"{prefix}-{sha256(canonical_bytes(value))[:20]}"


class Archive:
    def __init__(self, output: Path) -> None:
        self.output = output
        self.artifacts: list[dict[str, Any]] = []

    def save_bytes(
        self,
        relative: Path,
        raw: bytes,
        *,
        source: str,
        compress: bool = True,
    ) -> Path:
        destination = self.output / relative
        if compress:
            raw_relative = relative
            destination = destination.with_name(destination.name + ".gz")
            packed = gzip.compress(raw, mtime=0)
        else:
            raw_relative = relative
            packed = raw
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(packed)
        self.artifacts.append(
            {
                "path": str(destination.relative_to(self.output)),
                "source": source,
                "rawSha256": sha256(raw),
                "rawBytes": len(raw),
                "storedSha256": sha256(packed),
                "storedBytes": len(packed),
                "compression": "gzip-mtime-0" if compress else "none",
                "logicalPath": str(raw_relative),
            }
        )
        return destination

    def save_file(self, source: Path, relative: Path) -> Path:
        return self.save_bytes(relative, source.read_bytes(), source=str(source))

    def save_json(self, value: Any, relative: Path, *, source: str) -> Path:
        return self.save_bytes(relative, canonical_bytes(value), source=source)

    def save_capture_bundle(self, source: Path, relative: Path) -> Path:
        members = []
        tar_buffer = io.BytesIO()
        with tarfile.open(fileobj=tar_buffer, mode="w", format=tarfile.PAX_FORMAT) as tar:
            for path in sorted(source.glob("*.json")):
                raw = path.read_bytes()
                info = tarfile.TarInfo(path.name)
                info.size = len(raw)
                info.mtime = 0
                info.mode = 0o644
                info.uid = 0
                info.gid = 0
                info.uname = ""
                info.gname = ""
                tar.addfile(info, io.BytesIO(raw))
                members.append(
                    {
                        "path": path.name,
                        "rawSha256": sha256(raw),
                        "rawBytes": len(raw),
                    }
                )
        packed = gzip.compress(tar_buffer.getvalue(), mtime=0)
        destination = self.output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(packed)
        self.artifacts.append(
            {
                "path": str(destination.relative_to(self.output)),
                "source": str(source),
                "storedSha256": sha256(packed),
                "storedBytes": len(packed),
                "compression": "deterministic-tar+gzip-mtime-0",
                "members": members,
            }
        )
        return destination


def normalized_source_context(capture: dict[str, Any]) -> dict[str, Any]:
    if capture["sourceContext"] is None:
        return {"availability": capture["sourceContextAvailability"]}
    context = dict(capture["sourceContext"])
    source = context.pop("source", None)
    if source is not None:
        context["capturedSourceSha256"] = sha256(source.encode())
        context["capturedSourceBytes"] = len(source.encode())
    return context


def capture_row_id(
    mode: str,
    capture: dict[str, Any],
    category: str,
    index: int,
    row: dict[str, Any],
) -> str:
    identity = {
        "mode": mode,
        "sourceIdentity": capture["sourceIdentity"],
        "sourceSha256": (
            capture["sourceContext"]["sourceSha256"]
            if capture["sourceContext"] is not None
            else None
        ),
        "invocationSequence": capture["invocationSequence"],
        "category": category,
        "index": index,
        "row": row,
    }
    return stable_id("d1", identity)


def collect_captures(
    raw_root: Path,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], dict[str, Any]]:
    boundaries: list[dict[str, Any]] = []
    references: list[dict[str, Any]] = []
    counts: dict[str, Any] = {}

    for mode in MODES:
        mode_counts: collections.Counter[str] = collections.Counter()
        profile_states: collections.Counter[str] = collections.Counter()
        source_identities: set[str] = set()
        for path in sorted((raw_root / mode / "e1-run1" / "captures").glob("*.json")):
            capture = read_json(path)
            assert capture["acceptedSourceCommit"] == SOURCE_COMMIT
            assert capture["measurementMode"] == mode
            source_identities.add(capture["sourceIdentity"])
            profile_states[capture["privateMetadataProfile"]["state"]] += 1
            base = {
                "mode": mode,
                "captureFile": path.name,
                "captureRawSha256": sha256(path.read_bytes()),
                "invocationId": capture["invocationId"],
                "invocationSequence": capture["invocationSequence"],
                "sourceIdentity": capture["sourceIdentity"],
                "sourceContextAvailability": capture["sourceContextAvailability"],
                "sourceContext": normalized_source_context(capture),
                "module": capture["module"],
                "compiler": capture["compiler"],
                "policy": capture["policy"],
            }
            for category, key in (
                ("predicate", "predicateAttempts"),
                ("call-argument", "callArgumentValidations"),
                ("census", "boundTreeCensus"),
                ("diagnostic", "diagnostics"),
            ):
                for index, row in enumerate(capture[key]):
                    boundaries.append(
                        {
                            "rowId": capture_row_id(mode, capture, category, index, row),
                            "category": category,
                            **base,
                            "row": row,
                        }
                    )
                    mode_counts[category] += 1
            profile = capture["privateMetadataProfile"]
            references.append(
                {
                    "rowId": capture_row_id(mode, capture, "private-profile", 0, profile),
                    "category": "private-profile",
                    **base,
                    "row": profile,
                }
            )
            mode_counts["private-profile"] += 1
            for index, row in enumerate(capture["bclResolutionAttempts"]):
                references.append(
                    {
                        "rowId": capture_row_id(mode, capture, "bcl-resolution", index, row),
                        "category": "bcl-resolution",
                        **base,
                        "row": row,
                    }
                )
                mode_counts["bcl-resolution"] += 1
        counts[mode] = {
            "captureFiles": sum(1 for _ in (raw_root / mode / "e1-run1" / "captures").glob("*.json")),
            "distinctSourceIdentities": len(source_identities),
            "rows": dict(sorted(mode_counts.items())),
            "privateProfileStates": dict(sorted(profile_states.items())),
        }
    return boundaries, references, counts


def file_id(subject: str, row: dict[str, Any]) -> str:
    return f"{subject}:{row['path']}"


def summarize_e1(raw_root: Path) -> tuple[dict[str, Any], dict[str, dict[str, str]]]:
    result: dict[str, Any] = {}
    statuses: dict[str, dict[str, str]] = {}
    for mode in MODES:
        reports: list[dict[str, Any]] = []
        statuses[mode] = {}
        for subject in SUBJECTS:
            report_path = raw_root / mode / "e1-run1" / "reports" / f"{subject}-roundtrip.json"
            report = read_json(report_path)
            assert report["evidence"]["DeclaredTestAttemptsPerLeg"] == 2
            subject_rows = []
            for row in report["file_detail"]:
                row_id = file_id(subject, row)
                statuses[mode][row_id] = row["status"]
                subject_rows.append(
                    {
                        "rowId": row_id,
                        "status": row["status"],
                        "errors": row["errors"],
                        "lossCount": row["loss_count"],
                        "lossKinds": row["loss_kinds"],
                    }
                )
            attempts = []
            for attempt in report["evidence"]["TestAttempts"]:
                attempt_result = attempt["Result"]
                valid = (
                    bool(attempt_result["Results"])
                    and not attempt_result["ParseErrors"]
                    and not attempt_result["UsedConsoleFallback"]
                )
                attempts.append(
                    {
                        "leg": attempt["Leg"],
                        "ordinal": attempt["Attempt"],
                        "exitCode": attempt_result["ExitCode"],
                        "validDenominator": valid,
                        "total": attempt_result["TotalTests"] if valid else None,
                        "passed": attempt_result["Passed"] if valid else None,
                        "failed": attempt_result["Failed"] if valid else None,
                        "skipped": attempt_result["Skipped"] if valid else None,
                        "trxCount": len(attempt_result["TrxFiles"]),
                    }
                )
            reports.append(
                {
                    "subject": subject,
                    "verdict": report["verdict"],
                    "inconclusive": report["inconclusive"],
                    "inconclusiveReason": report.get("inconclusive_reason"),
                    "files": report["files"],
                    "evidenceCounts": report["evidence_counts"],
                    "baseline": report["baseline"],
                    "roundTrip": report.get("round_trip"),
                    "gateFailures": report["gate_failures"],
                    "attempts": attempts,
                    "rows": subject_rows,
                }
            )
        result[mode] = reports
    return result, statuses


def status_delta(
    before: str,
    after: str,
    statuses: dict[str, dict[str, str]],
) -> dict[str, Any]:
    accepted = {"Replaced", "EmitCompilationError", "EmitSyntaxError"}
    rows = []
    for row_id in sorted(statuses[before]):
        old = statuses[before][row_id]
        new = statuses[after][row_id]
        if old != new:
            rows.append(
                {
                    "rowId": row_id,
                    "before": old,
                    "after": new,
                    "newlyRejected": old in accepted and new not in accepted,
                }
            )
    return {
        "before": before,
        "after": after,
        "changedRows": rows,
        "newlyRejectedRowIds": [row["rowId"] for row in rows if row["newlyRejected"]],
    }


def summarize_api(raw_root: Path) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for mode in MODES:
        api = read_json(raw_root / mode / "api.json")
        rows = []
        for observation in api["observations"]:
            rows.append(
                {
                    "rowId": f"{observation['id']}:{observation['api']}",
                    "id": observation["id"],
                    "api": observation["api"],
                    "declaredOrigin": observation["declaredOrigin"],
                    "enforceEffects": observation["enforceEffects"],
                    "accepted": observation["accepted"],
                    "diagnosticCodes": [row["Code"] for row in observation["diagnostics"]],
                    "generatedCodeSha256": observation["generatedCodeSha256"],
                }
            )
        result[mode] = {
            "compilerSha256": api["compilerSha256"],
            "warmSourceSha256": api["warmSourceSha256"],
            "discovery": api["discovery"],
            "privateProfileCount": len(api["privateProfile"]),
            "privateProfileSha256": sha256(canonical_bytes(api["privateProfile"])),
            "rows": rows,
        }
    return result


def api_delta(before: str, after: str, api: dict[str, Any]) -> dict[str, Any]:
    old = {row["rowId"]: row for row in api[before]["rows"]}
    new = {row["rowId"]: row for row in api[after]["rows"]}
    changed = []
    for row_id in sorted(old):
        if (
            old[row_id]["accepted"] != new[row_id]["accepted"]
            or old[row_id]["diagnosticCodes"] != new[row_id]["diagnosticCodes"]
            or old[row_id]["generatedCodeSha256"] != new[row_id]["generatedCodeSha256"]
        ):
            changed.append(
                {
                    "rowId": row_id,
                    "beforeAccepted": old[row_id]["accepted"],
                    "afterAccepted": new[row_id]["accepted"],
                    "beforeDiagnosticCodes": old[row_id]["diagnosticCodes"],
                    "afterDiagnosticCodes": new[row_id]["diagnosticCodes"],
                }
            )
    return {"before": before, "after": after, "changedRows": changed}


def summarize_source_inventory(raw_root: Path) -> dict[str, Any]:
    inventory = read_json(raw_root / "source-inventory.json")
    subjects = []
    value_declaration_kinds = {
        "MethodDeclaration",
        "Parameter",
        "PropertyDeclaration",
        "VariableDeclarator",
    }
    for observation in inventory["observations"]:
        declaration_counts: collections.Counter[str] = collections.Counter()
        none_row_ids: list[str] = []
        contexts = []
        for context in observation["contexts"]:
            measured = context["measuredE1Context"]
            contexts.append(
                {
                    "projectFile": measured["ProjectFile"],
                    "targetFramework": measured["TargetFramework"],
                    "nullable": context["evaluatedNullable"],
                    "completeOriginalBuild": context["completeOriginalBuild"],
                    "fileCount": len(context["files"]),
                    "referenceCount": len(context["references"]),
                    "diagnosticCount": len(context["diagnostics"]),
                }
            )
            for file in context["files"]:
                for declaration in file["declarations"]:
                    if not declaration["isNamedNonGenericReference"]:
                        continue
                    key = (
                        f"{declaration['nullableAnnotation']}|"
                        f"{declaration['nullableContext']}|{declaration['nodeKind']}"
                    )
                    declaration_counts[key] += 1
                    if (
                        declaration["nullableAnnotation"] == "None"
                        and declaration["nodeKind"] in value_declaration_kinds
                    ):
                        none_row_ids.append(
                            stable_id(
                                "source",
                                {
                                    "subject": observation["subject"],
                                    "fileSha256": file["sha256"],
                                    "nodeKind": declaration["nodeKind"],
                                    "start": declaration["start"],
                                    "length": declaration["length"],
                                    "declarationId": declaration["declarationId"],
                                    "type": declaration["type"],
                                },
                            )
                        )
        subjects.append(
            {
                "subject": observation["subject"],
                "reportSha256": observation["reportSha256"],
                "originalInputCount": observation["originalInputCount"],
                "originalInputHashesMatched": observation["originalInputHashesMatched"],
                "contexts": contexts,
                "unavailableContextFileCount": len(observation["unavailableContextFiles"]),
                "standaloneNullablePropertyQuery": observation["standaloneNullablePropertyQuery"],
                "namedNonGenericReferenceDeclarations": dict(sorted(declaration_counts.items())),
                "noneDeclarationRowIds": sorted(none_row_ids),
            }
        )
    return {
        "schema": inventory["schema"],
        "sourceCommit": inventory["sourceCommit"],
        "subjects": subjects,
    }


def summarize_migration(raw_root: Path) -> dict[str, Any]:
    rows = []
    for mode in MODES:
        migration = read_json(raw_root / mode / "migration.json")
        rows.append(
            {
                "mode": mode,
                "overall": migration["Overall"],
                "hardFailures": migration["HardFailures"],
                "originalRoslyn": migration["OriginalRoslyn"],
                "originalRuntime": migration["OriginalRuntime"],
                "currentConverterClassification": migration["CurrentConverterClassification"],
                "currentVariant": migration["CurrentVariant"],
                "identityGate": migration["IdentityGate"],
                "faithfulCandidate": migration["FaithfulCandidate"],
                "faithfulVariant": migration["FaithfulVariant"],
                "conversion": migration["Conversion"],
                "sourceContext": migration["SourceContext"],
                "referenceProfile": migration["ReferenceProfile"],
            }
        )
    return {"rows": rows}


def summarize_neutrality(raw_root: Path) -> dict[str, Any]:
    uninstrumented_path = raw_root / "uninstrumented-api.json"
    instrumented_path = raw_root / "current" / "api.json"
    uninstrumented = read_json(uninstrumented_path)
    instrumented = read_json(instrumented_path)

    def observable_rows(api: dict[str, Any]) -> list[dict[str, Any]]:
        return [
            {
                "id": row["id"],
                "api": row["api"],
                "accepted": row["accepted"],
                "diagnostics": row["diagnostics"],
                "generatedCode": row["generatedCode"],
            }
            for row in api["observations"]
        ]

    result = {
        "schema": "d1-instrumentation-neutrality-v1",
        "acceptedSourceCommit": SOURCE_COMMIT,
        "uninstrumentedCompilerSha256": uninstrumented["compilerSha256"],
        "instrumentedCurrentCompilerSha256": instrumented["compilerSha256"],
        "discoveryEqual": uninstrumented["discovery"] == instrumented["discovery"],
        "privateProfileEqual": (
            uninstrumented["privateProfile"] == instrumented["privateProfile"]
        ),
        "warmSourceEqual": uninstrumented["warmSource"] == instrumented["warmSource"],
        "observationCount": len(uninstrumented["observations"]),
        "diagnosticsAcceptanceAndEmittedBytesEqual": (
            observable_rows(uninstrumented) == observable_rows(instrumented)
        ),
        "uninstrumentedApiSha256": sha256(uninstrumented_path.read_bytes()),
        "instrumentedCurrentApiSha256": sha256(instrumented_path.read_bytes()),
    }
    assert result["discoveryEqual"]
    assert result["privateProfileEqual"]
    assert result["warmSourceEqual"]
    assert result["diagnosticsAcceptanceAndEmittedBytesEqual"]
    return result


def archive_raw_inputs(raw_root: Path, archive: Archive) -> None:
    for mode in MODES:
        mode_root = raw_root / mode
        archive.save_capture_bundle(
            mode_root / "e1-run1" / "captures",
            Path("attempts") / mode / "capture-json.tar.gz",
        )
        for path in sorted((mode_root / "e1-run1" / "reports").glob("*")):
            if path.is_file():
                archive.save_file(path, Path("e1") / mode / path.name)
        for name in ("completed.json", "provenance.json"):
            path = mode_root / "e1-run1" / name
            archive.save_file(path, Path("attempts") / mode / name)
        for path in sorted((mode_root / "e1-run1").glob("exit-*.json")):
            archive.save_file(path, Path("attempts") / mode / path.name)
        for path in sorted((mode_root / "e1-run1" / "captures" / "roundtrip-attempts").rglob("*")):
            if path.is_file():
                relative = path.relative_to(mode_root / "e1-run1" / "captures")
                archive.save_file(path, Path("attempts") / mode / relative)
        for name in (
            "build.log",
            "api-build.log",
            "api-build-2.log",
            "api.log",
            "api-attempt1-failed.log",
            "migration-build.log",
            "migration.log",
            "materialization.json",
        ):
            path = mode_root / name
            if path.exists():
                archive.save_file(path, Path("attempts") / mode / name)
        archive.save_file(mode_root / "api.json", Path("controls") / f"{mode}-api.json")
        archive.save_capture_bundle(
            mode_root / "api-captures",
            Path("controls") / mode / "api-capture-json.tar.gz",
        )
        archive.save_file(mode_root / "migration.json", Path("migration") / f"{mode}.json")
        failed_api_captures = mode_root / "api-captures-attempt1-failed"
        if failed_api_captures.exists():
            for path in sorted(failed_api_captures.glob("*.json")):
                archive.save_file(
                    path,
                    Path("development-failures") / mode / "api-captures" / path.name,
                )
    for path in sorted((raw_root / "cli-controls-1").rglob("*")):
        if path.is_file():
            archive.save_file(
                path,
                Path("controls") / "cli" / path.relative_to(raw_root / "cli-controls-1"),
            )
    archive.save_file(raw_root / "cli-controls-1.log", Path("controls") / "cli-controls.log")
    archive.save_file(
        raw_root / "uninstrumented-api.json",
        Path("controls") / "uninstrumented-api.json",
    )
    archive.save_file(
        raw_root / "instrumentation-neutrality.json",
        Path("controls") / "instrumentation-neutrality.json",
    )
    archive.save_file(raw_root / "source-inventory.json", Path("controls") / "source-inventory.json")
    for name in (
        "source-inventory-attempt1-failed.log",
        "source-inventory-build.log",
        "source-inventory-build2.log",
        "source-inventory-run2.log",
    ):
        path = raw_root / name
        if path.exists():
            archive.save_file(path, Path("attempts") / "source-inventory" / name)
    for path in sorted((raw_root / "development-unmodified").rglob("*")):
        if path.is_file():
            archive.save_file(
                path,
                Path("development-failures") / path.relative_to(raw_root / "development-unmodified"),
            )


def main() -> None:
    args = parse_args()
    raw_root = args.raw_root.resolve()
    output = args.output.resolve()
    assert raw_root.is_dir()
    readme = (output / "README.md").read_bytes() if (output / "README.md").exists() else None
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    if readme is not None:
        (output / "README.md").write_bytes(readme)
    archive = Archive(output)

    boundaries, references, capture_counts = collect_captures(raw_root)
    e1, statuses = summarize_e1(raw_root)
    api = summarize_api(raw_root)
    neutrality = summarize_neutrality(raw_root)
    source_inventory = summarize_source_inventory(raw_root)
    migration = summarize_migration(raw_root)

    archive.save_json(
        {"schema": "d1-boundaries-v1", "rows": boundaries},
        Path("boundaries.json"),
        source="derived from all per-invocation E1 capture JSON",
    )
    archive.save_json(
        {"schema": "d1-references-v1", "rows": references},
        Path("references.json"),
        source="derived from all per-invocation E1 capture JSON",
    )
    archive.save_json(
        migration,
        Path("migration.json"),
        source="derived from current, shadow-annotated and shadow-conservative migration.json",
    )

    source_none_ids = [
        row_id
        for subject in source_inventory["subjects"]
        for row_id in subject["noneDeclarationRowIds"]
    ]
    e1_delta_annotated = status_delta("current", "shadow-annotated", statuses)
    e1_delta_conservative = status_delta(
        "shadow-annotated", "shadow-conservative", statuses
    )
    api_delta_annotated = api_delta("current", "shadow-annotated", api)
    api_delta_conservative = api_delta(
        "shadow-annotated", "shadow-conservative", api
    )
    summary = {
        "schema": "d1-current-candidate-summary-v1",
        "acceptedSourceCommit": SOURCE_COMMIT,
        "scope": (
            "Non-shipping D1 current/Annotated-only/conservative comparison; "
            "not Stage B activation or a whole-program nullability claim."
        ),
        "captureCounts": capture_counts,
        "e1": e1,
        "e1Deltas": {
            "currentToShadowAnnotated": e1_delta_annotated,
            "shadowAnnotatedToShadowConservative": e1_delta_conservative,
        },
        "api": api,
        "apiDeltas": {
            "currentToShadowAnnotated": api_delta_annotated,
            "shadowAnnotatedToShadowConservative": api_delta_conservative,
        },
        "instrumentationNeutrality": neutrality,
        "sourceInventory": source_inventory,
        "sourceNoneDeclarationRowIds": sorted(source_none_ids),
        "migration": migration,
        "decisionProposal": {
            "result": "revise-scope",
            "adoptedCandidate": (
                "Treat identity-proven nominal Oblivious values from the actual "
                "private metadata profile as possibly null at the measured direct "
                "native-return boundary."
            ),
            "withheldScope": (
                "Initialization and selected method-input behavior for actual "
                "private-metadata Oblivious producers, plus source-declaration "
                "Roslyn None/nullable-disabled values, remain withheld until #1401 "
                "preserves evaluated nullable context and declaration identity and "
                "#1402 supplies direct boundary controls and reruns the implemented "
                "candidate. Unresolved identities, annotation-transfer loss, "
                "arrays/generics outside their owned matrices, mutation, ref/out/in "
                "and unobserved generator contexts remain excluded."
            ),
            "evidence": {
                "incrementalCorpusConservativeRejectionRowIds": (
                    e1_delta_conservative["newlyRejectedRowIds"]
                ),
                "incrementalApiConservativeRows": [
                    row["rowId"] for row in api_delta_conservative["changedRows"]
                ],
                "annotatedOnlyCorpusRejectionRowIds": (
                    e1_delta_annotated["newlyRejectedRowIds"]
                ),
                "sourceNoneDeclarationRowIds": sorted(source_none_ids),
                "serilogUnavailableRowIds": [
                    f"Serilog:{row['path']}"
                    for row in read_json(
                        raw_root
                        / "current"
                        / "e1-run1"
                        / "reports"
                        / "Serilog-roundtrip.json"
                    )["file_detail"]
                ],
            },
            "limitations": [
                (
                    "The attempted E1 set contains no predicate row whose source is "
                    "both Oblivious and an identity-proven known nominal reference; "
                    "therefore zero incremental corpus rejections is an observed "
                    "empty affected set, not proof that source-origin widening is safe."
                ),
                (
                    "Serilog retained 112 configured files: 6 exclusions and 106 "
                    "not attempted after symmetric parse-context restore failure."
                ),
                (
                    "The migration result is fixture-specific. It demonstrates a "
                    "faithful nullable-reference route without default/throw/unwrap, "
                    "but does not implement or validate corpus-wide conversion."
                ),
                (
                    "Current conversion loses nullable-disabled declaration context; "
                    "the source None declaration IDs above are inventory, not "
                    "compiler rejection rows."
                ),
            ],
        },
    }
    archive.save_bytes(
        Path("summary.json"),
        canonical_bytes(summary),
        source="deterministic reduction",
        compress=False,
    )
    archive_raw_inputs(raw_root, archive)

    manifest = {
        "schema": "d1-current-candidate-manifest-v1",
        "acceptedSourceCommit": SOURCE_COMMIT,
        "runCompletedUtc": {
            mode: read_json(raw_root / mode / "e1-run1" / "completed.json")[
                "completedUtc"
            ]
            for mode in MODES
        },
        "materializations": {
            mode: read_json(raw_root / mode / "materialization.json")
            for mode in MODES
        },
        "provenance": {
            mode: read_json(raw_root / mode / "e1-run1" / "provenance.json")
            for mode in MODES
        },
        "unavailable": [
            {
                "subject": "Serilog",
                "files": 106,
                "reason": (
                    "Symmetric InvalidOperationException restoring the parse-context "
                    "graph after generated project.assets.json referenced an absent "
                    "Serilog.Sinks.XUnit package directory. Two declared test attempts "
                    "per leg were retained; no valid denominator was invented."
                ),
            }
        ],
        "artifacts": sorted(archive.artifacts, key=lambda row: row["path"]),
    }
    manifest_bytes = canonical_bytes(manifest)
    (output / "manifest.json").write_bytes(manifest_bytes)
    print(
        json.dumps(
            {
                "output": str(output),
                "artifactCount": len(archive.artifacts),
                "manifestSha256": sha256(manifest_bytes),
                "boundaryRows": len(boundaries),
                "referenceRows": len(references),
                "sourceNoneRows": len(source_none_ids),
                "currentToAnnotatedNewRejections": len(
                    e1_delta_annotated["newlyRejectedRowIds"]
                ),
                "annotatedToConservativeNewRejections": len(
                    e1_delta_conservative["newlyRejectedRowIds"]
                ),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
