#!/usr/bin/env bash
# ============================================================================
# validate-telemetry.sh [--expect <discriminator>] <journal.jsonl> [more.jsonl ...]
#
# Validates every line of the given telemetry file(s), dispatching on each
# record's "schema" discriminator (loop plan D4.1/D4.2; latency streams per
# WS3 D3.2):
#   loop-telemetry/2            -> loop-telemetry.schema.json
#   mcp-write/1 | mcp-write/2   -> mcp-write.schema.json
#   watch-rebuild/1             -> watch-rebuild.schema.json
#
# --expect <discriminator> additionally asserts stream purity: every record
# must carry exactly that discriminator (restores the old tool's "v2-only
# journal" guarantee — a misrouted record type in a single-stream file is
# an error, not a valid line).
#
# Strategy (no new dependencies assumed):
#   1. python3 + jsonschema installed  -> full JSON Schema validation
#   2. python3 only                    -> hand-rolled check: full field
#      semantics for loop-telemetry/2; generic required/allowed/type/enum
#      checks (including const-discriminated if/then/else conditionals)
#      derived from the schema file for the flat record types
#   3. no python3                      -> clear failure message, exit 3
#
# Records with no "schema" field (v1) or an unknown discriminator are
# rejected. Exit 0 = all lines valid; 1 = at least one invalid line.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

EXPECT=""
if [[ "${1:-}" == "--expect" ]]; then
    [[ $# -ge 2 ]] || { echo "--expect needs a discriminator" >&2; exit 2; }
    EXPECT="$2"
    shift 2
fi

[[ $# -ge 1 ]] || { echo "Usage: validate-telemetry.sh [--expect <discriminator>] <journal.jsonl> [more.jsonl ...]" >&2; exit 2; }
for s in loop-telemetry.schema.json mcp-write.schema.json watch-rebuild.schema.json; do
    [[ -f "$SCRIPT_DIR/$s" ]] || { echo "Schema not found: $SCRIPT_DIR/$s" >&2; exit 2; }
done
for f in "$@"; do
    [[ -f "$f" ]] || { echo "No such file: $f" >&2; exit 2; }
done

if ! command -v python3 >/dev/null 2>&1; then
    echo "validate-telemetry.sh: python3 is required for validation and was not found on PATH." >&2
    echo "Install python3 (any recent version; the 'jsonschema' package is optional but preferred)." >&2
    exit 3
fi

python3 - "$SCRIPT_DIR" "$EXPECT" "$@" <<'PYEOF'
import json
import re
import sys

script_dir = sys.argv[1]
expect = sys.argv[2] or None
files = sys.argv[3:]


def load(name):
    with open(f"{script_dir}/{name}", "r", encoding="utf-8") as f:
        return json.load(f)


SCHEMAS = {
    "loop-telemetry/2": load("loop-telemetry.schema.json"),
    "mcp-write/1": load("mcp-write.schema.json"),
    "mcp-write/2": load("mcp-write.schema.json"),
    "watch-rebuild/1": load("watch-rebuild.schema.json"),
}

try:
    import jsonschema  # type: ignore
    validators = {k: jsonschema.Draft202012Validator(v) for k, v in SCHEMAS.items()}
    mode = "jsonschema (full schema validation)"

    def check(record, discriminator):
        return [e.message for e in validators[discriminator].iter_errors(record)]
except ImportError:
    mode = "hand-rolled (python3 stdlib; install 'jsonschema' for full validation)"
    CMD_ENUM = {"build", "test", "run"}
    MECH_ENUM = {"raw", "mcp-file", "mcp-node", "unknown"}
    VERDICT_ENUM = {"applied", "rejected", None}

    def _is_integer(v):
        # JSON Schema semantics: an integer-valued float satisfies
        # "integer" (draft 2020-12) — the fallback must agree with the
        # jsonschema path so a file cannot pass on one machine and fail
        # on another.
        if isinstance(v, bool):
            return False
        return isinstance(v, int) or (isinstance(v, float) and v.is_integer())

    def check_generic(record, schema):
        """Required/allowed/type/enum checks for flat record schemas
        (mcp-write, watch-rebuild) derived from the schema document,
        including const-discriminated if/then/else conditionals."""
        errs = []
        allowed = set(schema["properties"].keys())
        required = list(schema["required"])
        forbidden = set()

        # if/then/else limited to the shape our schemas use: `if` matching
        # a const on one property; `then` adding required fields; `else`
        # forbidding fields via `"field": false`.
        cond = schema.get("if")
        if cond:
            matches = all(
                record.get(k) == spec.get("const")
                for k, spec in cond.get("properties", {}).items())
            branch = schema.get("then") if matches else schema.get("else")
            if branch:
                required += branch.get("required", [])
                forbidden |= {k for k, spec in branch.get("properties", {}).items()
                              if spec is False}

        for k in required:
            if k not in record:
                errs.append(f"missing required field: {k}")
        for k in record:
            if k not in allowed:
                errs.append(f"unexpected field (additionalProperties=false): {k}")
            elif k in forbidden:
                errs.append(f"field not allowed for this schema version: {k}")
        if errs:
            return errs
        for k, v in record.items():
            spec = schema["properties"][k]
            if "const" in spec and v != spec["const"]:
                errs.append(f"{k} must be {spec['const']!r}, got {v!r}")
            if "enum" in spec and v not in spec["enum"]:
                errs.append(f"{k} must be one of {spec['enum']}, got {v!r}")
            types = spec.get("type")
            if types is not None:
                types = types if isinstance(types, list) else [types]
                ok = any(
                    (t == "string" and isinstance(v, str))
                    or (t == "integer" and _is_integer(v))
                    or (t == "boolean" and isinstance(v, bool))
                    or (t == "null" and v is None)
                    for t in types)
                if not ok:
                    errs.append(f"{k} must have type {types}, got {type(v).__name__}")
                    continue
            if isinstance(v, str) and "minLength" in spec and len(v) < spec["minLength"]:
                errs.append(f"{k} shorter than minLength {spec['minLength']}")
            if _is_integer(v) and "minimum" in spec and v < spec["minimum"]:
                errs.append(f"{k} below minimum {spec['minimum']}")
        return errs

    def check_loop_telemetry(record, schema):  # noqa: C901 - deliberately exhaustive
        errs = []
        for k in schema["required"]:
            if k not in record:
                errs.append(f"missing required field: {k}")
        for k in record:
            if k not in set(schema["properties"].keys()):
                errs.append(f"unexpected field (additionalProperties=false): {k}")
        if errs:
            return errs
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

    def check(record, discriminator):
        schema = SCHEMAS[discriminator]
        if discriminator == "loop-telemetry/2":
            return check_loop_telemetry(record, schema)
        return check_generic(record, schema)

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
            if not isinstance(record, dict):
                bad += 1
                print(f"{path}:{lineno}: record is not a JSON object")
                continue
            discriminator = record.get("schema")
            if discriminator not in SCHEMAS:
                bad += 1
                print(f"{path}:{lineno}: unknown or missing schema discriminator: {discriminator!r}")
                continue
            if expect is not None and discriminator != expect:
                bad += 1
                print(f"{path}:{lineno}: stream purity: expected {expect!r}, got {discriminator!r}")
                continue
            errors = check(record, discriminator)
            if errors:
                bad += 1
                for err in errors:
                    print(f"{path}:{lineno}: {err}")

print(f"validate-telemetry: mode={mode}")
print(f"validate-telemetry: {total} record(s), {total - bad} valid, {bad} invalid")
sys.exit(1 if bad else 0)
PYEOF
