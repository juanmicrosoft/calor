"""Render structured compiler results in the archived documentary layout.

The transcripts pin diagnostic semantics, not today's default text presentation.
Do not regenerate historical measurements when the CLI's presentation changes.
"""
import json


def transcript_output(proc, output_path):
    envelope = json.loads(proc.stdout)
    diagnostics = envelope["diagnostics"]
    if any(d["severity"] not in ("error", "warning", "info") for d in diagnostics):
        raise ValueError("Unrecognized diagnostic severity")
    if (proc.returncode != 0) != any(d["severity"] == "error" for d in diagnostics):
        raise ValueError(f"Compiler exit and diagnostics disagree: {proc.stderr}")

    lines = []
    stderr = proc.stderr
    if proc.returncode == 0:
        status = "Compilation successful: 1 compiled, 0 cached\n"
        if not stderr.startswith(status):
            raise ValueError(f"Unexpected clean-build status: {stderr}")
        stderr = stderr[len(status):]
        lines.append(f"Compilation successful: {output_path}")
    for diagnostic in diagnostics:
        location = diagnostic["location"]
        lines.append(
            f'{location["file"]}({location["line"]},{location["column"]}): '
            f'{diagnostic["severity"]} {diagnostic["code"]}: {diagnostic["message"]}')
    if stderr:
        lines.append(stderr.rstrip("\n"))
    return "\n".join(lines)
