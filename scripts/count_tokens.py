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
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPRedirectHandler, Request, build_opener


API_ROOT = "https://api.anthropic.com"
API_VERSION = "2023-06-01"


class MeasurementError(Exception):
    """An invalid experiment or incomplete provider measurement."""


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise MeasurementError("Anthropic redirected a request; refusing credential forwarding")


class Anthropic:
    def __init__(self, key: str):
        self.key = key
        self.opener = build_opener(NoRedirects())

    def request(self, path: str, payload: dict | None = None) -> dict:
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
            exc.close()
            raise MeasurementError(f"Anthropic HTTP {exc.code}; no complete results written") from None
        except (URLError, TimeoutError, OSError):
            raise MeasurementError("Anthropic transport failed; no complete results written") from None
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


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def load_manifest(path: Path) -> tuple[dict, list[dict]]:
    raw = path.read_bytes()
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
                if not isinstance(relative, str) or not relative:
                    raise MeasurementError(f"{name}/{variant}: expected payload path")
                payload_path = (path.parent / relative).resolve()
                data = payload_path.read_bytes()
                text = data.decode("utf-8")
                if not text.strip():
                    raise MeasurementError(f"{name}/{variant}: empty payload")
                texts[variant] = {
                    "path": str(payload_path), "sha256": digest(data),
                    "utf8_bytes": len(data), "text": text,
                }
            loaded.append({"id": name, "population": group, "source": source,
                           "kind": kind, "variants": texts,
                           **({"note": sample["note"]} if "note" in sample else {})})
    if dict(counts) != expected:
        raise MeasurementError(f"Population mismatch: expected {expected}, found {dict(counts)}")
    manifest = dict(manifest, manifest_sha256=digest(raw))
    return manifest, loaded


def measure(client: Anthropic, manifest: dict, samples: list[dict]) -> dict:
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
        "tool_sha256": digest(Path(__file__).read_bytes()),
        "target_model": manifest["target_model"], "resolved_model": model,
        "baseline": manifest["baseline"],
        "experiment": {key: value for key, value in manifest.items()
                       if key not in ("samples", "probes")},
        "count_scope": "sum of independent single-user-message input_tokens; includes API framing",
        "samples": results, "table": table,
    }


def markdown(report: dict) -> str:
    lines = [
        f"Source commit: `{report['source_commit']}`; provider model: "
        f"`{report['resolved_model']['id']}` ({report['target_model']}).",
        f"Manifest SHA-256: `{report['manifest_sha256']}`.",
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
    args = parser.parse_args(argv)
    try:
        manifest, samples = load_manifest(args.manifest)
        inputs = {args.manifest.resolve()} | {
            Path(payload["path"]) for sample in samples for payload in sample["variants"].values()
        }
        outputs = [path.resolve() for path in (args.output, args.table) if path is not None]
        if len(set(outputs)) != len(outputs) or any(path in inputs for path in outputs):
            raise MeasurementError("Output paths must be distinct and cannot overwrite inputs")
        if args.validate_only:
            print(f"Validated 41 population files, {len(manifest.get('probes', []))} probes; "
                  f"target {manifest['target_model']}; NOT MEASURED")
            return 0
        if args.output is None:
            raise MeasurementError("--output is required for measurement")
        if not all(path.parent.is_dir() for path in outputs):
            raise MeasurementError("Output parent directory does not exist")
        key = os.environ.get("ANTHROPIC_API_KEY", "").strip()
        if not key:
            raise MeasurementError("ANTHROPIC_API_KEY is absent; target model unresolved, "
                                   "NOT MEASURED. Configure Anthropic access and rerun.")
        report = measure(Anthropic(key), manifest, samples)
        write_atomic(args.output, json.dumps(report, ensure_ascii=False, indent=2) + "\n")
        if args.table:
            write_atomic(args.table, markdown(report))
        print(f"Measured with provider model {report['resolved_model']['id']}; wrote {args.output}")
        return 0
    except (MeasurementError, OSError, ValueError, UnicodeError) as exc:
        print(f"count_tokens: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
