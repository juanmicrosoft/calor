#!/usr/bin/env python3
"""M-L1 measurement driver (loop plan WS3 / M4 kickoff §3 PR 5).

Measures edit→envelope wall time over the committed D3.3 fixture and edit
script, on two surfaces per the kickoff's priced decision:

  mcp   (adjudicating) — every edit goes through calor_file_write in a warm
        session; the latency measure is the tool's own mcp-write/2
        `latencyMs` (tool-call receipt → verdict record). The driver also
        records client-observed round-trip for reference.
  watch (reported)     — safe edits are applied as raw file writes under
        `calor watch --format json`; latency is watch-rebuild/1 `latencyMs`
        (rebuild start → envelope written; excludes the configured
        debounce, which is recorded alongside).

The fixture is copied to a scratch workdir first — the committed fixture is
never mutated. PP-L1 adjudicates P50 ≤ 300 ms and P99 ≤ 1 s on the mcp
surface (plan §5).

Usage:
  measure-ml1.py --calor <path/to/calor.dll> --out ml1-001 [--surface mcp|watch|both]
"""
import argparse
import json
import os
import select
import shutil
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
FIXTURE = HERE / "fixture-10k" / "src"
EDITS = HERE / "edits" / "edit-script.jsonl"


def pctl(vals, p):
    vals = sorted(vals)
    k = (len(vals) - 1) * p / 100
    lo, hi = int(k), min(int(k) + 1, len(vals) - 1)
    return vals[lo] + (vals[hi] - vals[lo]) * (k - lo) if lo != hi else vals[lo]


class McpClient:
    """Minimal newline-delimited JSON-RPC client for `calor mcp`."""

    def __init__(self, calor_dll, root, write_log):
        env = dict(os.environ, CALOR_MCP_WRITE_LOG=str(write_log))
        self.proc = subprocess.Popen(
            ["dotnet", str(calor_dll), "mcp", "--root", str(root)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, env=env, text=True, bufsize=1)
        self._id = 0
        self.request("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "measure-ml1", "version": "1"},
        })
        self.notify("notifications/initialized", {})

    # Per-request deadline: the server's parse-error and catch-all paths
    # respond with id:null, and oversized request lines get no response at
    # all — without a deadline those would hang readline() forever with no
    # diagnostic. The select() poll watches the underlying fd, which is
    # blind to data already sitting in the text wrapper's buffer; that is
    # safe ONLY because this server emits exactly one response per request
    # and nothing else on stdout (no notifications), and the driver is
    # strictly sequential — a second message buffered behind the first
    # cannot exist. Revisit if the server ever gains stdout notifications.
    # (select on pipes is POSIX-only; this driver targets darwin/linux.)
    REQUEST_TIMEOUT_S = 120

    def request(self, method, params):
        self._id += 1
        self.proc.stdin.write(json.dumps(
            {"jsonrpc": "2.0", "id": self._id, "method": method, "params": params}) + "\n")
        self.proc.stdin.flush()
        deadline = time.monotonic() + self.REQUEST_TIMEOUT_S
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError(f"{method}: no response within {self.REQUEST_TIMEOUT_S}s")
            ready, _, _ = select.select([self.proc.stdout], [], [], remaining)
            if not ready:
                raise TimeoutError(f"{method}: no response within {self.REQUEST_TIMEOUT_S}s")
            line = self.proc.stdout.readline()
            if not line:
                raise RuntimeError("MCP server closed stdout")
            msg = json.loads(line)
            if "error" in msg and msg.get("id") is None:
                # Server-level protocol failure (never ours to match by id).
                raise RuntimeError(f"{method}: server protocol error: {msg['error']}")
            if msg.get("id") == self._id:
                if "error" in msg:
                    raise RuntimeError(f"{method}: {msg['error']}")
                return msg["result"]

    def notify(self, method, params):
        self.proc.stdin.write(json.dumps(
            {"jsonrpc": "2.0", "method": method, "params": params}) + "\n")
        self.proc.stdin.flush()

    def call_tool(self, name, arguments):
        result = self.request("tools/call", {"name": name, "arguments": arguments})
        content = result.get("content") or []
        text = content[0].get("text") if content else None
        if result.get("isError"):
            raise RuntimeError(f"{name}: {text or '<error result with empty content>'}")
        if text is None:
            raise RuntimeError(f"{name}: success result with empty content")
        return json.loads(text)

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=10)
        except Exception:
            self.proc.kill()


def load_edits():
    return [json.loads(l) for l in EDITS.read_text(encoding="utf-8").splitlines() if l.strip()]


def apply_replace_all(workdir, edit):
    """Returns the file's post-edit content (replace-ALL per the fixture
    README) without writing it — the write goes through the surface under
    measurement."""
    p = workdir / edit["path"]
    text = p.read_text(encoding="utf-8")
    if edit["find"] not in text:
        raise RuntimeError(f"seq {edit['seq']}: find not present: {edit['find']!r}")
    return p, text.replace(edit["find"], edit["replace"])


def measure_mcp(calor_dll, out_dir):
    work = Path(tempfile.mkdtemp(prefix="ml1-mcp-"))
    shutil.copytree(FIXTURE, work / "src")
    write_log = out_dir / "mcp-writes.jsonl"
    write_log.unlink(missing_ok=True)

    client = McpClient(calor_dll, work, write_log)
    try:
        t0 = time.monotonic()
        session = client.call_tool("calor_session_open", {"directory": str(work / "src")})
        open_ms = round((time.monotonic() - t0) * 1000)
        session_id = session["sessionId"]

        rows = []
        for edit in load_edits():
            path, content = apply_replace_all(work, edit)
            t0 = time.monotonic()
            result = client.call_tool("calor_file_write", {
                "path": str(path), "content": content, "sessionId": session_id})
            rtt_ms = round((time.monotonic() - t0) * 1000)
            if result["applied"] != edit["expectApplied"]:
                raise RuntimeError(
                    f"seq {edit['seq']}: applied={result['applied']}, "
                    f"expected {edit['expectApplied']} (verdict {result['verdict']})")
            if result.get("healApplied"):
                # A heal would write different bytes than the driver computed;
                # the shared workdir would silently drift from the edit chain
                # and later finds would fail far from the cause. Fail here.
                raise RuntimeError(f"seq {edit['seq']}: server healed the content — "
                                   "fixture/heal drift; fixture must never trigger heal")
            rows.append({"seq": edit["seq"], "kind": edit["kind"],
                         "applied": result["applied"], "rtt_ms": rtt_ms})
    finally:
        client.close()
        shutil.rmtree(work, ignore_errors=True)

    records = [json.loads(l) for l in write_log.read_text(encoding="utf-8").splitlines()]
    if len(records) != len(rows):
        raise RuntimeError(f"mcp-writes.jsonl has {len(records)} records for {len(rows)} edits")
    for row, rec in zip(rows, records):
        row["latency_ms"] = rec["latencyMs"]
        row["refresh_ms"] = rec["refreshMs"]
        row["check_ms"] = rec["checkMs"]
        row["apply_ms"] = rec["applyMs"]

    lat = [r["latency_ms"] for r in rows]
    return {
        "surface": "mcp",
        "edits": len(rows),
        "sessionOpenMs": open_ms,
        "p50_ms": round(pctl(lat, 50), 2), "p90_ms": round(pctl(lat, 90), 2),
        "p99_ms": round(pctl(lat, 99), 2), "max_ms": max(lat),
        "meanRttOverheadMs": round(
            statistics.mean(r["rtt_ms"] - r["latency_ms"] for r in rows), 1),
        "rows": rows,
    }


def measure_watch(calor_dll, out_dir, debounce_ms=200):
    work = Path(tempfile.mkdtemp(prefix="ml1-watch-"))
    shutil.copytree(FIXTURE, work / "src")
    rebuild_log = out_dir / "watch-rebuilds.jsonl"
    rebuild_log.unlink(missing_ok=True)

    env = dict(os.environ, CALOR_WATCH_REBUILD_LOG=str(rebuild_log))
    proc = subprocess.Popen(
        ["dotnet", str(calor_dll), "watch", str(work / "src"), "--format", "json"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env)

    def records():
        if not rebuild_log.exists():
            return []
        return [json.loads(l) for l in rebuild_log.read_text(encoding="utf-8").splitlines()]

    def wait_for(n, timeout=120):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            recs = records()
            if len(recs) >= n:
                return recs
            time.sleep(0.05)
        raise RuntimeError(f"watch: timed out waiting for rebuild record {n}")

    try:
        wait_for(1)  # initial compile
        expected = 1
        # Watch measures the incremental raw-edit surface: safe edits only
        # (breaking edits are an MCP reject concept; raw writes always land).
        safe = [e for e in load_edits() if e["kind"] == "safe"]
        for edit in safe:
            path, content = apply_replace_all(work, edit)
            path.write_text(content, encoding="utf-8")
            expected += 1
            wait_for(expected)
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
        shutil.rmtree(work, ignore_errors=True)

    recs = records()
    # Integrity: exactly one record per write plus the initial compile, and
    # every rebuild is a genuine 1-file incremental. A debounce-split or a
    # spurious FSW rebuild would pollute the percentiles with no-op rebuild
    # latencies; fail loudly instead of publishing contaminated numbers.
    if len(recs) != 1 + len(safe):
        raise RuntimeError(f"watch: {len(recs)} records for {len(safe)} writes + initial")
    if not recs[0]["initial"]:
        raise RuntimeError("watch: first record is not the initial compile")
    bad = [r for r in recs[1:] if r["initial"] or r["compiled"] != 1]
    if bad:
        raise RuntimeError(f"watch: {len(bad)} rebuild record(s) are not "
                           f"1-file incrementals (first: {bad[0]})")
    initial = recs[0]
    rebuilds = recs[1:]
    lat = [r["latencyMs"] for r in rebuilds]
    return {
        "surface": "watch",
        "edits": len(rebuilds),
        "initialCompileMs": initial["latencyMs"],
        "debounceMs": rebuilds[0]["debounceMs"] if rebuilds else debounce_ms,
        "p50_ms": round(pctl(lat, 50), 2), "p90_ms": round(pctl(lat, 90), 2),
        "p99_ms": round(pctl(lat, 99), 2), "max_ms": max(lat),
        "anyErrors": any(r["anyErrors"] for r in rebuilds),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--calor", type=Path, required=True, help="path to calor.dll")
    ap.add_argument("--out", type=Path, required=True, help="result directory (e.g. ml1-001)")
    ap.add_argument("--surface", choices=["mcp", "watch", "both"], default="both")
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    commit = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True,
                            text=True, cwd=HERE).stdout.strip()
    tree = subprocess.run(["git", "rev-parse", "HEAD^{tree}"], capture_output=True,
                          text=True, cwd=HERE).stdout.strip()
    # A commit stamp proves nothing about a dirty tree; refuse to publish a
    # record whose provenance can't be checked out. (Rebases can orphan the
    # commit SHA; the tree hash survives as a content anchor.) Tracked
    # modifications anywhere are disqualifying (the measured build comes
    # from the tree); untracked files matter only under the latency dir,
    # where the fixture copy and edit script are read from the working
    # tree — unrelated untracked files elsewhere must not block a run.
    repo_root = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"],
                                    capture_output=True, text=True, cwd=HERE).stdout.strip())
    out_resolved = args.out.resolve()
    dirty = subprocess.run(["git", "status", "--porcelain", "--untracked-files=no"],
                           capture_output=True, text=True, cwd=HERE).stdout.strip()
    untracked = [
        line for line in subprocess.run(
            ["git", "status", "--porcelain", "--untracked-files=all", "--", str(HERE)],
            capture_output=True, text=True, cwd=HERE).stdout.strip().splitlines()
        # The out dir is the run's own product; everything else under the
        # latency dir is a contamination risk.
        if not (repo_root / line.split(maxsplit=1)[1]).resolve().is_relative_to(out_resolved)
    ]
    blocking = "\n".join(x for x in (dirty, "\n".join(untracked)) if x)
    if blocking:
        raise SystemExit("refusing to run with a dirty tree — commit first:\n" + blocking)
    dotnet = subprocess.run(["dotnet", "--version"], capture_output=True,
                            text=True).stdout.strip()
    result = {"commit": commit, "treeHash": tree, "dotnetSdk": dotnet,
              "calorDll": str(args.calor),
              "editScript": str(EDITS.relative_to(HERE))}

    if args.surface in ("mcp", "both"):
        print("measuring: mcp surface (adjudicating)...")
        result["mcp"] = measure_mcp(args.calor, args.out)
        m = result["mcp"]
        print(f"  {m['edits']} edits: P50 {m['p50_ms']:.0f} ms, "
              f"P90 {m['p90_ms']:.0f} ms, P99 {m['p99_ms']:.0f} ms, max {m['max_ms']} ms "
              f"(session open {m['sessionOpenMs']} ms)")

    if args.surface in ("watch", "both"):
        print("measuring: watch surface (reported)...")
        result["watch"] = measure_watch(args.calor, args.out)
        w = result["watch"]
        print(f"  {w['edits']} rebuilds: P50 {w['p50_ms']:.0f} ms, "
              f"P90 {w['p90_ms']:.0f} ms, P99 {w['p99_ms']:.0f} ms "
              f"(initial {w['initialCompileMs']} ms, debounce {w['debounceMs']} ms excluded)")

    if "mcp" in result:
        m = result["mcp"]
        result["ppL1"] = {
            "thresholds": {"p50_ms": 300, "p99_ms": 1000},
            "p50Pass": m["p50_ms"] <= 300,
            "p99Pass": m["p99_ms"] <= 1000,
            "verdict": "PASS" if m["p50_ms"] <= 300 and m["p99_ms"] <= 1000 else "MISS",
        }
        print(f"PP-L1: {result['ppL1']['verdict']} "
              f"(P50 {m['p50_ms']:.0f}/300 ms, P99 {m['p99_ms']:.0f}/1000 ms)")

    (args.out / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {args.out / 'result.json'}")


if __name__ == "__main__":
    main()
