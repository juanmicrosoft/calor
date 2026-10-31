#!/usr/bin/env python3
"""Compare equivalent syntax payloads using Anthropic's count_tokens API (#1597).

No dependencies, generation, proxy tokenizer, or credential-store lookup.
ANTHROPIC_API_KEY must be supplied by the caller. Only api.anthropic.com is used.
The requested display name is resolved through GET /v1/models, never guessed
from an app alias. Counts include identical single-user-message API framing;
they are API estimates, not source-only counts or agent-evaluation results.

Manifest (paths relative to the manifest; UTF-8 bytes/newlines preserved):
  {"schema_version": 1, "source_commit": "<40 lowercase hex>",
   "target_model": "Claude Opus 5.5", "baseline": "current",
   "population": {"primer": 1, "converted": 20, "agent": 20},
   "samples": [{"id": "primer", "population": "primer",
                "source": "repo/path",
                "variants": {"current": "current/primer.txt",
                             "infix": "infix/primer.txt"}}]}
Every sample must have the same variants. Optional `probes` use the same sample
shape but are reported separately, never pooled with the 41-file population.

  python3 scripts/count_tokens.py manifest.json --validate-only
  python3 scripts/count_tokens.py manifest.json --output results.json
  python3 scripts/count_tokens.py manifest.json --output results.json --table results.md
Results are written only after every request succeeds. No offline counts exist.

Payload paths are plain relative paths (no "..", "." or empty parts) to regular,
non-symlink files under the manifest directory; with --payload-root the manifest
and every directory from that root down must also be non-symlinks. --max-requests
caps every provider HTTP attempt (model pages, counts and retries). --validate-only prints an
inputs_sha256 digest over the manifest bytes and every payload's relative path,
SHA-256 and size; pass it back with --expect-inputs-sha256 so a measurement can
only run on exactly the reviewed bytes (.github/workflows/syntax-token-count.yml).
"""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPRedirectHandler, Request, build_opener


API_ROOT = "https://api.anthropic.com"
API_VERSION = "2023-06-01"
RETRY_STATUSES = {429, 500, 502, 503, 504, 529}
MAX_ATTEMPTS = 6
MAX_RETRY_DELAY = 60.0
COMMIT = re.compile(r"[0-9a-f]{40}")


class MeasurementError(Exception):
    """An invalid experiment or incomplete provider measurement."""


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise MeasurementError("Anthropic redirected a request; refusing credential forwarding")


class Anthropic:
    def __init__(self, key: str, max_attempts: int | None = None):
        self.key = key
        self.opener = build_opener(NoRedirects())
        self.sleep = time.sleep
        self.requests = 0
        self.max_attempts = max_attempts

    def request(self, path: str, payload: dict | None = None) -> dict:
        # Bounded retries only for rate limits, overload and transport failures; the
        # same idempotent count/metadata request is resent, never a different endpoint.
        for attempt in range(1, MAX_ATTEMPTS + 1):
            try:
                return self.request_once(path, payload)
            except Retryable as exc:
                if attempt == MAX_ATTEMPTS:
                    raise MeasurementError(f"{exc} after {MAX_ATTEMPTS} attempts; "
                                           "no complete results written") from None
                self.sleep(exc.delay if exc.delay is not None
                           else min(2.0 ** attempt, MAX_RETRY_DELAY))
        raise AssertionError("unreachable")

    def request_once(self, path: str, payload: dict | None) -> dict:
        if self.max_attempts is not None and self.requests >= self.max_attempts:
            raise MeasurementError(f"HTTP attempt budget of {self.max_attempts} exhausted; "
                                   "no complete results written")
        self.requests += 1
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
        request = Request(API_ROOT + path, data=body, headers={
            "x-api-key": self.key,
            "anthropic-version": API_VERSION,
            "Content-Type": "application/json",
        })
        try:
            with self.opener.open(request, timeout=60) as response:
                result = json.load(response)
        except HTTPError as exc:
            # Provider bodies can echo request data; never print them or headers.
            code, delay = exc.code, retry_after(exc.headers)
            exc.close()
            if code in RETRY_STATUSES:
                raise Retryable(f"Anthropic HTTP {code}", delay) from None
            raise MeasurementError(f"Anthropic HTTP {code}; no complete results written") from None
        except (URLError, TimeoutError, OSError):
            raise Retryable("Anthropic transport failed", None) from None
        except (ValueError, UnicodeError):
            raise MeasurementError("Anthropic returned invalid JSON") from None
        if not isinstance(result, dict):
            raise MeasurementError("Anthropic returned a non-object response")
        return result

    def resolve_model(self, display_name: str) -> dict:
        models: dict[str, dict] = {}
        cursor = None
        cursors: set[str] = set()
        while True:
            query = {"limit": 100}
            if cursor is not None:
                query["after_id"] = cursor
            page = self.request("/v1/models?" + urlencode(query))
            if not isinstance(page.get("data"), list) or type(page.get("has_more")) is not bool:
                raise MeasurementError("Malformed Anthropic model list")
            for model in page["data"]:
                if (not isinstance(model, dict) or not isinstance(model.get("id"), str)
                        or not model["id"] or not isinstance(model.get("display_name"), str)):
                    raise MeasurementError("Malformed Anthropic model metadata")
                if model["id"] in models:
                    raise MeasurementError("Duplicate model ID in Anthropic metadata")
                models[model["id"]] = model
            if not page["has_more"]:
                break
            cursor = page.get("last_id")
            if not isinstance(cursor, str) or not cursor or cursor in cursors or not page["data"]:
                raise MeasurementError("Invalid Anthropic model pagination")
            cursors.add(cursor)
        normalize = lambda value: " ".join(value.casefold().split())
        matches = [model for model in models.values()
                   if normalize(model["display_name"]) == normalize(display_name)]
        if len(matches) != 1:
            raise MeasurementError(
                f"Requested model display name {display_name!r} has {len(matches)} exact metadata "
                "matches; cannot resolve a unique provider ID. No substitute model selected."
            )
        return matches[0]

    def count(self, model_id: str, text: str) -> int:
        result = self.request("/v1/messages/count_tokens", {
            "model": model_id,
            "messages": [{"role": "user", "content": text}],
        })
        count = result.get("input_tokens")
        if type(count) is not int or count < 0:
            raise MeasurementError("Anthropic returned invalid input_tokens")
        return count


class Retryable(Exception):
    def __init__(self, message: str, delay: float | None):
        super().__init__(message)
        self.delay = delay


def retry_after(headers) -> float | None:
    try:
        value = float(headers.get("retry-after")) if headers is not None else None
    except (TypeError, ValueError):
        return None
    return None if value is None or value != value or value < 0 else min(value, MAX_RETRY_DELAY)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def inputs_digest(manifest: dict, samples: list[dict]) -> str:
    payloads = sorted([sample["id"], variant, payload["path"], payload["sha256"], payload["utf8_bytes"]]
                      for sample in samples for variant, payload in sample["variants"].items())
    canonical = json.dumps({"manifest_sha256": manifest["manifest_sha256"], "payloads": payloads},
                           ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return digest(canonical.encode("utf-8"))


def request_count(samples: list[dict]) -> int:
    """Minimum attempts: one model page plus one count per payload, before retries."""
    return 1 + sum(len(sample["variants"]) for sample in samples)


def counter(client, name: str) -> int | None:
    value = getattr(client, name, None)
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def plain_parts(relative: str) -> list[str]:
    parts = relative.split("/")
    if (not relative or relative.startswith("/") or "\\" in relative
            or any(part in ("", ".", "..") for part in parts)):
        raise MeasurementError(f"{relative!r}: payload path must be plain and relative")
    return parts


def no_symlink(path: Path) -> os.stat_result:
    try:
        info = os.lstat(path)
    except FileNotFoundError:
        raise MeasurementError(f"{path.name}: payload input does not exist") from None
    if stat.S_ISLNK(info.st_mode):
        raise MeasurementError(f"{path.name}: symlinks are not allowed in payload inputs")
    return info


def read_regular(root: Path, parts: list[str]) -> bytes:
    path = root
    for part in parts[:-1]:
        path = path / part
        if not stat.S_ISDIR(no_symlink(path).st_mode):
            raise MeasurementError(f"{part}: payload parent must be a directory")
    path = path / parts[-1]
    if not stat.S_ISREG(no_symlink(path).st_mode):
        raise MeasurementError(f"{parts[-1]}: payload must be a regular file")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as handle:
        if not stat.S_ISREG(os.fstat(handle.fileno()).st_mode):
            raise MeasurementError(f"{parts[-1]}: payload must be a regular file")
        return handle.read()


def check_manifest_location(path: Path, payload_root: Path | None) -> None:
    if payload_root is None:
        return
    if not stat.S_ISDIR(no_symlink(payload_root).st_mode):
        raise MeasurementError("payload root must be a directory")
    try:
        parts = path.relative_to(payload_root).parts
    except ValueError:
        raise MeasurementError("manifest must be inside the payload root") from None
    plain_parts("/".join(parts))
    read_regular(payload_root, list(parts))


def load_manifest(path: Path, payload_root: Path | None = None) -> tuple[dict, list[dict]]:
    check_manifest_location(path, payload_root)
    root = path.parent
    if not stat.S_ISDIR(no_symlink(root).st_mode):
        raise MeasurementError("manifest directory must be a directory")
    raw = read_regular(root, [path.name])
    manifest = json.loads(raw)
    if (not isinstance(manifest, dict) or type(manifest.get("schema_version")) is not int
            or manifest["schema_version"] != 1):
        raise MeasurementError("Expected manifest schema_version 1")
    if not re.fullmatch(r"[0-9a-f]{40}", str(manifest.get("source_commit", ""))):
        raise MeasurementError("source_commit must identify an exact 40-character Git commit")
    if not isinstance(manifest.get("target_model"), str) or not manifest["target_model"].strip():
        raise MeasurementError("target_model must be an exact provider display name")
    expected = manifest.get("population")
    if (expected != {"primer": 1, "converted": 20, "agent": 20}
            or any(type(value) is not int for value in expected.values())):
        raise MeasurementError("Required population is one primer, 20 converted files, 20 agent files")
    samples = manifest.get("samples")
    probes = manifest.get("probes", [])
    if not isinstance(samples, list) or not isinstance(probes, list):
        raise MeasurementError("samples and probes must be lists")
    loaded = []
    seen: set[str] = set()
    sources: set[str] = set()
    variants = None
    counts: Counter = Counter()
    baseline = manifest.get("baseline")
    if not isinstance(baseline, str) or not baseline.strip():
        raise MeasurementError("baseline must name a nonempty variant")
    for kind, entries in (("population", samples), ("probe", probes)):
        for sample in entries:
            if not isinstance(sample, dict):
                raise MeasurementError("Sample must be an object")
            name, group, source = (sample.get(key) for key in ("id", "population", "source"))
            if any(not isinstance(value, str) or not value.strip() for value in (name, group, source)):
                raise MeasurementError("Each sample needs nonempty id, population, source")
            if name in seen:
                raise MeasurementError(f"Duplicate sample ID: {name}")
            seen.add(name)
            if kind == "population":
                if source in sources:
                    raise MeasurementError(f"Duplicate population source: {source}")
                sources.add(source)
                counts[group] += 1
            paths = sample.get("variants")
            if (not isinstance(paths, dict) or not paths or baseline not in paths
                    or any(not isinstance(key, str) or not key.strip() for key in paths)):
                raise MeasurementError(f"{name}: variants must include baseline")
            if kind == "population":
                if variants is None:
                    variants = set(paths)
                elif set(paths) != variants:
                    raise MeasurementError(f"{name}: incomplete variant coverage")
            texts = {}
            for variant, relative in paths.items():
                if not isinstance(relative, str):
                    raise MeasurementError(f"{name}/{variant}: expected payload path")
                data = read_regular(root, plain_parts(relative))
                text = data.decode("utf-8")
                if not text.strip():
                    raise MeasurementError(f"{name}/{variant}: empty payload")
                texts[variant] = {
                    "path": relative, "sha256": digest(data),
                    "utf8_bytes": len(data), "text": text,
                }
            loaded.append({"id": name, "population": group, "source": source,
                           "kind": kind, "variants": texts,
                           **({"note": sample["note"]} if "note" in sample else {})})
    if dict(counts) != expected:
        raise MeasurementError(f"Population mismatch: expected {expected}, found {dict(counts)}")
    manifest = dict(manifest, manifest_sha256=digest(raw))
    manifest["inputs_sha256"] = inputs_digest(manifest, loaded)
    return manifest, loaded


def measure(client: Anthropic, manifest: dict, samples: list[dict],
            attestation: dict | None = None) -> dict:
    model = client.resolve_model(manifest["target_model"])
    results = []
    totals: Counter = Counter()
    baselines: Counter = Counter()
    for sample in samples:
        values = {}
        for variant, payload in sample["variants"].items():
            count = client.count(model["id"], payload["text"])
            values[variant] = {key: value for key, value in payload.items() if key != "text"}
            values[variant]["input_tokens"] = count
            totals[sample["kind"], sample["population"], variant] += count
        base = values[manifest["baseline"]]["input_tokens"]
        for value in values.values():
            value["delta_tokens"] = value["input_tokens"] - base
        for variant in values:
            baselines[sample["kind"], sample["population"], variant] += base
        results.append({**sample, "variants": values})
    table = []
    for (kind, population, variant), count in sorted(totals.items()):
        base = baselines[kind, population, variant]
        table.append({
            "kind": kind, "population": population, "variant": variant,
            "files": sum(sample["kind"] == kind and sample["population"] == population
                         and variant in sample["variants"] for sample in samples),
            "input_tokens": count, "baseline_tokens": base,
            "delta_tokens": count - base,
            "delta_percent": 100 * (count - base) / base if base else None,
        })
    return {
        "status": "measured", "provider": API_ROOT, "api_version": API_VERSION,
        "measured_at": datetime.now(timezone.utc).isoformat(),
        "source_commit": manifest["source_commit"],
        "manifest_sha256": manifest["manifest_sha256"],
        "inputs_sha256": manifest["inputs_sha256"],
        "attestation": attestation or {},
        "planned_min_requests": request_count(samples),
        "http_attempts": counter(client, "requests"),
        "max_http_attempts": counter(client, "max_attempts"),
        "tool_sha256": digest(Path(__file__).read_bytes()),
        "target_model": manifest["target_model"], "resolved_model": model,
        "baseline": manifest["baseline"],
        "experiment": {key: value for key, value in manifest.items()
                       if key not in ("samples", "probes", "manifest_sha256", "inputs_sha256")},
        "count_scope": "sum of independent single-user-message input_tokens; includes API framing",
        "samples": results, "table": table,
    }


def markdown(report: dict) -> str:
    lines = [
        f"Source commit: `{report['source_commit']}`; provider model: "
        f"`{report['resolved_model']['id']}` ({report['target_model']}).",
        f"Manifest SHA-256: `{report['manifest_sha256']}`; inputs SHA-256: "
        f"`{report['inputs_sha256']}`.",
        report["count_scope"] + ". Negative deltas mean fewer tokens; savings overlap.",
    ]
    for key in ("selection", "comparison", "semantic_status"):
        if key in report["experiment"]:
            lines.append(str(report["experiment"][key]))
    lines.extend(["", "| Kind | Population | Variant | Files | Input tokens | Delta | Delta % |",
                  "|---|---|---|---:|---:|---:|---:|"])
    for row in report["table"]:
        percent = f"{row['delta_percent']:+.2f}" if row["delta_percent"] is not None else "N/A"
        escape = lambda value: str(value).replace("|", "\\|").replace("\n", " ")
        lines.append(f"| {escape(row['kind'])} | {escape(row['population'])} | "
                     f"{escape(row['variant'])} | {row['files']} | {row['input_tokens']} | "
                     f"{row['delta_tokens']:+} | {percent} |")
    return "\n".join(lines) + "\n"


def write_atomic(path: Path, text: str) -> None:
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent,
                                     delete=False) as handle:
        temporary = Path(handle.name)
        try:
            handle.write(text)
            handle.close()
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--validate-only", action="store_true",
                        help="Check population and payloads locally; never output token counts")
    parser.add_argument("--output", type=Path, help="Complete measured JSON report")
    parser.add_argument("--table", type=Path, help="One comparable-results Markdown table")
    parser.add_argument("--expect-inputs-sha256", help="Fail unless the reviewed inputs digest matches")
    parser.add_argument("--expect-target-model", help="Fail unless the manifest names this model")
    parser.add_argument("--max-requests", type=int,
                        help="Hard cap on provider HTTP attempts, including model pages and retries")
    parser.add_argument("--payload-root", type=Path,
                        help="Directory the manifest must sit in without symlinks from here down")
    parser.add_argument("--attest-tool-commit", help="40-hex commit of this tool, recorded in results")
    parser.add_argument("--attest-payload-commit", help="40-hex commit holding the payloads")
    args = parser.parse_args(argv)
    try:
        if args.max_requests is not None and args.max_requests < 1:
            raise MeasurementError("--max-requests must be positive")
        manifest, samples = load_manifest(args.manifest, args.payload_root)
        expected = args.expect_inputs_sha256
        if expected is not None and expected != manifest["inputs_sha256"]:
            raise MeasurementError("Inputs digest mismatch; payloads differ from the reviewed set")
        if (args.expect_target_model is not None
                and args.expect_target_model != manifest["target_model"]):
            raise MeasurementError("Manifest target_model differs from the expected model")
        if args.max_requests is not None and request_count(samples) > args.max_requests:
            raise MeasurementError(f"Planned minimum of {request_count(samples)} HTTP attempts "
                                   "exceeds --max-requests")
        attestation = {}
        for key, value in (("tool_commit", args.attest_tool_commit),
                           ("payload_commit", args.attest_payload_commit)):
            if value is not None:
                if not COMMIT.fullmatch(value):
                    raise MeasurementError(f"{key} must be a 40-character lowercase commit")
                attestation[key] = value
        root = args.manifest.resolve().parent
        inputs = {args.manifest.resolve()} | {
            (root / payload["path"]).resolve()
            for sample in samples for payload in sample["variants"].values()
        }
        outputs = [path.resolve() for path in (args.output, args.table) if path is not None]
        if len(set(outputs)) != len(outputs) or any(path in inputs for path in outputs):
            raise MeasurementError("Output paths must be distinct and cannot overwrite inputs")
        if args.validate_only:
            print(f"Validated 41 population files, {len(manifest.get('probes', []))} probes; "
                  f"target {manifest['target_model']}; NOT MEASURED")
            print(f"inputs_sha256={manifest['inputs_sha256']}")
            print(f"manifest_sha256={manifest['manifest_sha256']}")
            print(f"planned_min_requests={request_count(samples)}")
            return 0
        if args.output is None:
            raise MeasurementError("--output is required for measurement")
        if not all(path.parent.is_dir() for path in outputs):
            raise MeasurementError("Output parent directory does not exist")
        key = os.environ.get("ANTHROPIC_API_KEY", "").strip()
        if not key:
            raise MeasurementError("ANTHROPIC_API_KEY is absent; target model unresolved, "
                                   "NOT MEASURED. Configure Anthropic access and rerun.")
        report = measure(Anthropic(key, args.max_requests), manifest, samples, attestation)
        write_atomic(args.output, json.dumps(report, ensure_ascii=False, indent=2) + "\n")
        if args.table:
            write_atomic(args.table, markdown(report))
        print(f"Measured with provider model {report['resolved_model']['id']} in "
              f"{report['http_attempts']} HTTP attempts; wrote {args.output}")
        return 0
    except (MeasurementError, OSError, ValueError, UnicodeError) as exc:
        print(f"count_tokens: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
