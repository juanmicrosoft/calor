#!/usr/bin/env bash
# ============================================================================
# validate-telemetry.sh <journal.jsonl> [more.jsonl ...]
#
# Validates every line of the given journal file(s) against the normative
# loop-telemetry/2 schema (loop-telemetry.schema.json, loop plan D4.1/D4.2).
#
# Strategy (no new dependencies assumed):
#   1. python3 + jsonschema installed  -> full JSON Schema validation
#   2. python3 only                    -> hand-rolled check of required
#      fields, enums, types, and the additionalProperties allowlist
#   3. no python3                      -> clear failure message, exit 3
#
# v1 records (no "schema" field) are rejected: this tool asserts a v2-only
# journal. Exit 0 = all lines valid; 1 = at least one invalid line.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCHEMA="$SCRIPT_DIR/loop-telemetry.schema.json"

[[ $# -ge 1 ]] || { echo "Usage: validate-telemetry.sh <journal.jsonl> [more.jsonl ...]" >&2; exit 2; }
[[ -f "$SCHEMA" ]] || { echo "Schema not found: $SCHEMA" >&2; exit 2; }
for f in "$@"; do
    [[ -f "$f" ]] || { echo "No such file: $f" >&2; exit 2; }
done

if ! command -v python3 >/dev/null 2>&1; then
    echo "validate-telemetry.sh: python3 is required for validation and was not found on PATH." >&2
    echo "Install python3 (any recent version; the 'jsonschema' package is optional but preferred)." >&2
    exit 3
fi

python3 - "$SCHEMA" "$@" <<'PYEOF'
import json
import sys

schema_path = sys.argv[1]
files = sys.argv[2:]

with open(schema_path, "r", encoding="utf-8") as f:
    schema = json.load(f)

try:
    import jsonschema  # type: ignore
    validator = jsonschema.Draft202012Validator(schema)
    mode = "jsonschema (full schema validation)"

    def check(record):
        return [e.message for e in validator.iter_errors(record)]
except ImportError:
    mode = "hand-rolled (python3 stdlib; install 'jsonschema' for full validation)"
    ALLOWED = set(schema["properties"].keys())
    REQUIRED = schema["required"]
    CMD_ENUM = {"build", "test", "run"}
    MECH_ENUM = {"raw", "mcp-file", "mcp-node", "unknown"}
    VERDICT_ENUM = {"applied", "rejected", None}

    def check(record):  # noqa: C901 - deliberately exhaustive
        errs = []
        if not isinstance(record, dict):
            return ["record is not a JSON object"]
        for k in REQUIRED:
            if k not in record:
                errs.append(f"missing required field: {k}")
        for k in record:
            if k not in ALLOWED:
                errs.append(f"unexpected field (additionalProperties=false): {k}")
        if errs:
            return errs
        if record["schema"] != "loop-telemetry/2":
            errs.append(f"schema must be loop-telemetry/2, got {record['schema']!r}")
        for k in ("ts", "pair", "arm", "src_tree_hash"):
            if not isinstance(record[k], str) or not record[k]:
                errs.append(f"{k} must be a non-empty string")
        if isinstance(record.get("src_tree_hash"), str) and len(record["src_tree_hash"]) < 8:
            errs.append("src_tree_hash must be at least 8 chars")
        if not isinstance(record["run"], int) or isinstance(record["run"], bool) or record["run"] < 1:
            errs.append("run must be an integer >= 1")
        it = record.get("iteration")
        if it is not None and (not isinstance(it, int) or isinstance(it, bool) or it < 1):
            errs.append("iteration must be null or an integer >= 1")
        if record["cmd"] not in CMD_ENUM:
            errs.append(f"cmd must be one of {sorted(CMD_ENUM)}")
        if not isinstance(record["exit"], int) or isinstance(record["exit"], bool):
            errs.append("exit must be an integer")
        if not isinstance(record["edited"], bool):
            errs.append("edited must be a boolean")
        for k in ("feedback_latency_ms", "heldout_pass", "heldout_fail"):
            v = record[k]
            if not isinstance(v, int) or isinstance(v, bool) or v < 0:
                errs.append(f"{k} must be an integer >= 0")
        if record["edit_mechanism"] not in MECH_ENUM:
            errs.append(f"edit_mechanism must be one of {sorted(MECH_ENUM)}")
        ids = record.get("edit_target_ids", [])
        if not isinstance(ids, list) or any(not isinstance(i, str) or not i for i in ids):
            errs.append("edit_target_ids must be an array of non-empty strings")
        diags = record.get("diagnostics", [])
        if not isinstance(diags, list):
            errs.append("diagnostics must be an array")
        else:
            if len(diags) > 50:
                errs.append("diagnostics exceeds maxItems 50")
            for d in diags:
                if (not isinstance(d, dict) or "code" not in d
                        or not isinstance(d["code"], str)
                        or set(d) - {"code", "declarationId"}):
                    errs.append(f"bad diagnostics entry: {d!r}")
                    continue
                import re
                if not re.fullmatch(r"Calor[0-9]{4}", d["code"]):
                    errs.append(f"diagnostic code does not match ^Calor[0-9]{{4}}$: {d['code']!r}")
                if "declarationId" in d and (not isinstance(d["declarationId"], str) or not d["declarationId"]):
                    errs.append("diagnostic declarationId must be a non-empty string")
        if "diagnostics_truncated" in record and not isinstance(record["diagnostics_truncated"], bool):
            errs.append("diagnostics_truncated must be a boolean")
        ev = record.get("envelope_valid")
        if ev is not None and not isinstance(ev, bool):
            errs.append("envelope_valid must be a boolean or null")
        if record.get("apply_verdict") not in VERDICT_ENUM:
            errs.append("apply_verdict must be applied|rejected|null")
        re_ = record.get("rejected_edit")
        if re_ is not None:
            if (not isinstance(re_, dict)
                    or set(re_) != {"snapshotRef", "payloadPath"}
                    or not isinstance(re_.get("snapshotRef"), str) or len(re_["snapshotRef"]) < 8
                    or not isinstance(re_.get("payloadPath"), str) or not re_["payloadPath"]):
                errs.append("rejected_edit must be null or {snapshotRef(>=8 chars), payloadPath}")
        return errs

total = 0
bad = 0
for path in files:
    with open(path, "r", encoding="utf-8") as f:
        for lineno, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            total += 1
            try:
                record = json.loads(line)
            except ValueError as e:
                bad += 1
                print(f"{path}:{lineno}: not valid JSON: {e}")
                continue
            errors = check(record)
            if errors:
                bad += 1
                for err in errors:
                    print(f"{path}:{lineno}: {err}")

print(f"validate-telemetry: mode={mode}")
print(f"validate-telemetry: {total} record(s), {total - bad} valid, {bad} invalid")
sys.exit(1 if bad else 0)
PYEOF
