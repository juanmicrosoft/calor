"""Preserve raw product evidence bytes; gzip changes storage, not observations."""
import argparse
import gzip
import hashlib
import json
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("work", type=Path)
parser.add_argument("observations", type=Path)
parser.add_argument("summary", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
manifest = []


def save(source, relative):
    data = source.read_bytes()
    packed = gzip.compress(data, mtime=0)
    destination = args.output / (str(relative) + ".gz")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(packed)
    manifest.append({
        "path": str(destination.relative_to(args.output)), "source": str(source),
        "rawSha256": hashlib.sha256(data).hexdigest(), "rawBytes": len(data),
        "gzipSha256": hashlib.sha256(packed).hexdigest(), "gzipBytes": len(packed)
    })


for source in sorted((args.work / "reports").glob("*.json")):
    save(source, Path("corpus") / source.name)
for source in sorted((args.work / "logs").glob("*.log")):
    save(source, Path("logs") / source.name)
for source in sorted(args.observations.glob("*.json")):
    save(source, Path("observations") / source.name)
for source in sorted(args.summary.glob("*.json")):
    save(source, Path("observations") / source.name)
for source in sorted((args.work / "tests").glob("*.trx")):
    save(source, Path("tests") / source.name)
save(args.work / "run-exits.tsv", Path("run-exits.tsv"))
for report_path in sorted((args.work / "reports").glob("*-roundtrip.json")):
    report = json.loads(report_path.read_text())
    for attempt in report["evidence"]["TestAttempts"]:
        for index, trx in enumerate(attempt["Result"]["TrxFiles"]):
            path = Path(trx)
            if not path.is_absolute():
                path = Path(attempt["Result"]["WorkingDirectory"]) / path
            if not path.exists():
                manifest.append({
                    "source": str(path), "leg": attempt["Leg"], "attempt": attempt["Attempt"],
                    "unavailable": True, "reason": "Harness attempt has embedded test records but its original TRX was cleaned between attempts"
                })
                continue
            save(path, Path("tests") / report["project"] / (
                attempt["Leg"] + "-" + str(attempt["Attempt"]) + "-" + str(index) + ".trx"))
root = Path.cwd()
binary_paths = [
    root / "docs/plans/evidence/nominal-policy-1400/reproduce/bin/Debug/net10.0/Probe.dll",
    root / "docs/plans/evidence/nominal-policy-1400/reproduce/bin/Debug/net10.0/calor.dll",
    root / "docs/plans/evidence/nominal-policy-1400/reproduce/bin/Debug/net10.0/Calor.RoundTrip.Harness.dll",
    root / "docs/plans/evidence/nominal-policy-1400/reproduce/bin/Debug/net10.0/Microsoft.CodeAnalysis.CSharp.dll"
]
provenance = {
    "observerSource": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "observerTrackedDiffSha256": hashlib.sha256(subprocess.check_output(["git", "diff", "--binary"])).hexdigest(),
    "binaries": [{"path": str(p.relative_to(root)), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                 for p in binary_paths],
    "e1Source": "39348d8c7262bac90120c6d30c01c4476e97acde",
    "scope": "Product D1 pre-T1 measurement; no enforcement or paid research",
    "artifacts": manifest
}
(args.output / "manifest.json").write_text(json.dumps(provenance, indent=2) + "\n")
print("Archived", len(manifest), "artifacts without normalization.")
