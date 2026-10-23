import json
import os
from pathlib import Path
import selectors
import signal
import subprocess
import sys
import time

root = Path(sys.argv[1]).resolve()
base = root / ".n0-evidence"
output = base / "surfaces"
output.mkdir(exist_ok=True)
compiler = ["dotnet", str(root / "src/Calor.Compiler/bin/Debug/net10.0/calor.dll")]
environment = os.environ.copy()
environment.pop("CALOR_NO_TYPE_CHECK", None)
environment["CALOR_TELEMETRY"] = "0"
environment["TMPDIR"] = str(base / "tmp")
records = []

def invoke(identifier, command, extra=None):
    env = environment | (extra or {})
    result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True, timeout=180)
    (output / (identifier + ".stdout")).write_text(result.stdout)
    (output / (identifier + ".stderr")).write_text(result.stderr)
    records.append({
        "id": identifier,
        "command": [str(arg).replace(str(root), "<REPO>") for arg in command],
        "environmentOverrides": extra or {},
        "exit": result.returncode,
        "stdout": result.stdout.replace(str(root), "<REPO>"),
        "stderr": result.stderr.replace(str(root), "<REPO>"),
    })
    print(identifier, result.returncode, flush=True)

cases = ["bind-bcl-nullable", "return-bcl-nullable", "argument-bcl-expression", "bind-literal", "active-duplicate"]
for identifier in cases:
    folder = base / "tmp" / "execution" / identifier
    (folder / "tests").mkdir(parents=True, exist_ok=True)
    source = (base / "cases" / (identifier + ".calr")).read_text()
    (folder / "Input.calr").write_text(
        source + "  §F{f9:Main:pub} () -> void\n    §E{}\n    §R\n")
    (folder / "tests/Smoke.cs").write_text(
        "using Xunit;\npublic class Smoke { [Fact] public void ExecutesMain() { N0.N0Module.Main(); } }\n")
    for mode, flags, extra in [
        ("default", [], {}),
        ("verify", ["--verify"], {}),
        ("env", [], {"CALOR_NO_TYPE_CHECK": "1"}),
        ("permissive", ["--permissive"], {}),
        ("combined", ["--verify", "--permissive"], {"CALOR_NO_TYPE_CHECK": "1"}),
    ]:
        for command in ["run", "test"]:
            invoke(f"{command}-{identifier}-{mode}",
                   compiler + [command, str(folder), "--timeout", "120", *flags], extra)
    for mode, extra in [("default", {}), ("env", {"CALOR_NO_TYPE_CHECK": "1"})]:
        invoke(f"verify-{identifier}-{mode}",
               compiler + ["verify", str(folder / "Input.calr"), "--format", "json", "--no-cache", "--no-telemetry"], extra)

watch_records = []
for mode, flags, extra in [
    ("default", [], {}),
    ("env", [], {"CALOR_NO_TYPE_CHECK": "1"}),
    ("permissive", ["--permissive-effects"], {}),
]:
    folder = base / "tmp" / "watch" / mode
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / "Input.calr"
    sequence = ["bind-bcl-nullable", "bind-literal", "active-duplicate", "bind-literal"]
    path.write_text((base / "cases" / (sequence[0] + ".calr")).read_text())
    command = compiler + ["watch", str(path), "--format", "json", "--debounce-ms", "50", "--no-telemetry", *flags]
    with (output / ("watch-" + mode + ".stderr")).open("w") as stderr:
        process = subprocess.Popen(command, cwd=root, env=environment | extra,
                                   stdout=subprocess.PIPE, stderr=stderr, text=True)
        selector = selectors.DefaultSelector()
        selector.register(process.stdout, selectors.EVENT_READ)
        envelopes = []
        try:
            for index, identifier in enumerate(sequence):
                if index:
                    path.write_text((base / "cases" / (identifier + ".calr")).read_text())
                deadline = time.monotonic() + 45
                while True:
                    if process.poll() is not None:
                        raise RuntimeError(f"watch {mode} exited before observation: {process.returncode}")
                    if time.monotonic() >= deadline:
                        raise TimeoutError(f"watch {mode} did not respond to {identifier}")
                    if selector.select(timeout=1):
                        line = process.stdout.readline()
                        if not line:
                            continue
                        envelope = json.loads(line)
                        envelopes.append({"case": identifier, "envelope": envelope})
                        break
            watch_records.append({
                "mode": mode, "command": [arg.replace(str(root), "<REPO>") for arg in command],
                "environmentOverrides": extra, "responsiveWhileRunning": process.poll() is None,
                "observations": envelopes, "termination": "measurement sends SIGINT after fourth envelope"
            })
        finally:
            selector.close()
            if process.poll() is None:
                process.send_signal(signal.SIGINT)
            process.communicate(timeout=15)
        watch_records[-1]["processExitAfterMeasurement"] = process.returncode

# Root incremental compilation: shared path, cold/warm, option and source changes.
folder = base / "tmp" / "cli-cache"
folder.mkdir(parents=True, exist_ok=True)
path = folder / "Input.calr"
for index, (identifier, flags) in enumerate([
    ("bind-bcl-nullable", []), ("bind-bcl-nullable", []),
    ("bind-bcl-nullable", ["--no-type-check"]),
    ("active-duplicate", ["--no-type-check"]),
    ("active-duplicate", ["--transpile-only"]),
    ("bind-literal", []),
]):
    path.write_text((base / "cases" / (identifier + ".calr")).read_text())
    invoke(f"cache-{index}-{identifier}",
           compiler + ["-i", str(path), "-o", str(folder / "Input.g.cs"),
                       "--cache", "--format", "json", "--verbose", "--no-telemetry", *flags])

(output / "matrix.json").write_text(json.dumps({"commands": records, "watch": watch_records}, indent=2) + "\n")
