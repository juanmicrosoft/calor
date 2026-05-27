#!/usr/bin/env python3
"""
run_phase_2_gate.py — Tier 3 statistical gate driver (RFC §10).

Drives the 30 tasks × 10 seeds × 3 arms = 900 agent runs over a 4–5 day
window. Records arm SHAs at start, writes them into the pre-registration
document, runs pre-flight checks, and dispatches the agent harness.

PER v6 §4.3 — Arm SHAs are frozen at gate kickoff, not at PR merge.
PER v6 §4.4 — Pinned model with fallback list.
PER v6 §3.3 — Pre-flight + 4-hour scheduled polling.

This driver is NOT a statistical analysis. It produces:
    results/runs.jsonl — per-run JSON records consumed by
                          scripts/analyze_gate_results.py

Usage:
    python3 scripts/run_phase_2_gate.py \\
        --pre-reg docs/plans/phase-2-measurement-protocol.md \\
        --output-dir results/ \\
        [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import shlex
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
AGENT_TASKS_DIR = REPO_ROOT / "tests" / "E2E" / "agent-tasks"
TASKS_DIR = AGENT_TASKS_DIR / "tasks"
FIXTURE_DIR = AGENT_TASKS_DIR / "fixtures"
TEMPLATE_DIR = AGENT_TASKS_DIR / "templates" / "path-2-gate"
TASK_MANIFEST = AGENT_TASKS_DIR / "phase-2-gate-tasks.txt"
DEFAULT_SEEDS = list(range(1, 11))  # seeds 1..10
ARMS = ("A", "B", "C")
PRE_FLIGHT_DISK_BYTES = 10 * 1024 * 1024 * 1024  # 10 GiB
EXPECTED_TRIAL_COUNT = 30


class Trial:
    """One row of the pre-registered task manifest.

    ``trial_id`` is the stable identifier the analyser sees on each
    record. ``kind`` is either ``"task"`` (driven by a
    ``tasks/<cat>/<task>/task.json`` paired with a workspace fixture)
    or ``"template"`` (a self-contained path-2-gate template with its
    own task.md/setup/expected/acceptance.sh).

    For ``task`` trials, ``task_dir`` is the directory containing
    ``task.json`` and ``fixture_dir`` is the resolved workspace
    template referenced by the task's ``fixture`` field.

    For ``template`` trials, ``task_dir`` is the template directory
    itself and ``fixture_dir`` is ``None`` (the template owns its
    workspace).
    """

    __slots__ = ("trial_id", "kind", "task_dir", "fixture_dir")

    def __init__(
        self,
        trial_id: str,
        kind: str,
        task_dir: Path,
        fixture_dir: Path | None,
    ) -> None:
        self.trial_id = trial_id
        self.kind = kind
        self.task_dir = task_dir
        self.fixture_dir = fixture_dir

    def __repr__(self) -> str:  # pragma: no cover — debug aid only
        return (
            f"Trial(id={self.trial_id!r}, kind={self.kind!r}, "
            f"task_dir={self.task_dir}, fixture_dir={self.fixture_dir})"
        )


def utcnow_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _rel_to_repo(p: Path) -> str:
    """Return ``p`` relative to the repo root when possible; otherwise the
    absolute path. ``Path.relative_to`` raises when ``p`` is outside the
    repo (e.g. an output-dir under ``$TMPDIR``); fall back to the
    absolute path so dry-run smoke tests work cross-platform.
    """
    try:
        return str(p.resolve().relative_to(REPO_ROOT))
    except ValueError:
        return str(p.resolve())


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, capture_output=True, text=True, **kwargs)


def _resolve_bash() -> str:
    """Return the path to a working bash interpreter.

    On Linux/macOS this is just ``bash`` (resolved from PATH). On Windows
    the first ``bash.exe`` on PATH is typically the WSL bridge
    (``C:\\Windows\\system32\\bash.exe``), which fails if no WSL distro is
    installed — turning every gate trial into a phantom ``harness_crash``.
    Prefer the Git for Windows shell when present, falling back to PATH
    if not.

    Operators running the real gate should run it from Linux or macOS;
    the Windows-aware fallback exists to support local plumbing tests
    (``--dry-run``) on developer laptops.
    """
    override = os.environ.get("CALOR_GATE_BASH")
    if override:
        return override
    if os.name == "nt":
        for candidate in (
            r"C:\Program Files\Git\bin\bash.exe",
            r"C:\Program Files\Git\usr\bin\bash.exe",
            r"C:\Program Files (x86)\Git\bin\bash.exe",
        ):
            if Path(candidate).exists():
                return candidate
    return "bash"


def git_head_sha(branch: str) -> str:
    cp = run(["git", "rev-parse", branch], cwd=REPO_ROOT)
    if cp.returncode != 0:
        raise RuntimeError(
            f"git rev-parse {branch} failed: {cp.stderr.strip()}"
        )
    return cp.stdout.strip()


def collect_fixtures() -> list[Path]:
    """Legacy v1 substrate enumeration. Kept for backwards-compat with
    callers that haven't migrated to ``collect_trials``. Do not use in
    new code — it returns workspace fixtures, most of which lack a
    runnable task contract (see protocol v2 §1 substrate-gap notes)."""
    existing = sorted(p for p in FIXTURE_DIR.iterdir() if p.is_dir())
    new = (
        sorted(p for p in TEMPLATE_DIR.iterdir() if p.is_dir())
        if TEMPLATE_DIR.exists()
        else []
    )
    return existing + new


def collect_trials(manifest_path: Path = TASK_MANIFEST) -> list[Trial]:
    """Read the pre-registered task manifest and resolve each line to a
    fully populated :class:`Trial`.

    Raises ``RuntimeError`` on the first malformed entry — the
    pre-registration discipline (RFC §10.5) treats a broken manifest as
    a kickoff-blocking error rather than something to paper over with
    placeholder records. The dry-run pipeline reuses this so smoke
    tests catch protocol drift early.
    """
    if not manifest_path.exists():
        raise RuntimeError(f"trial manifest not found: {manifest_path}")
    trials: list[Trial] = []
    for line_no, raw in enumerate(
        manifest_path.read_text(encoding="utf-8").splitlines(), start=1
    ):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if ":" not in line:
            raise RuntimeError(
                f"{manifest_path}:{line_no}: missing 'kind:' prefix in "
                f"{line!r}"
            )
        kind, _, body = line.partition(":")
        kind = kind.strip()
        body = body.strip()
        if kind == "task":
            task_dir = TASKS_DIR / body
            task_json_path = task_dir / "task.json"
            if not task_json_path.exists():
                raise RuntimeError(
                    f"{manifest_path}:{line_no}: task.json not found at "
                    f"{task_json_path}"
                )
            data = json.loads(task_json_path.read_text(encoding="utf-8"))
            fixture_name = data.get("fixture")
            if not fixture_name:
                raise RuntimeError(
                    f"{manifest_path}:{line_no}: task {body!r} has no "
                    f"'fixture' field in task.json (github-sourced tasks "
                    f"are not eligible for the gate; remove from manifest)"
                )
            fixture_dir = FIXTURE_DIR / fixture_name
            if not fixture_dir.is_dir():
                raise RuntimeError(
                    f"{manifest_path}:{line_no}: task {body!r} references "
                    f"missing fixture workspace {fixture_dir}"
                )
            trial_id = f"task:{body}"
        elif kind == "template":
            tpl_dir = TEMPLATE_DIR / body
            if not tpl_dir.is_dir():
                raise RuntimeError(
                    f"{manifest_path}:{line_no}: template dir not found "
                    f"at {tpl_dir}"
                )
            task_dir = tpl_dir
            fixture_dir = None
            trial_id = f"template:{body}"
        else:
            raise RuntimeError(
                f"{manifest_path}:{line_no}: unknown kind {kind!r}; "
                f"expected 'task' or 'template'"
            )
        trials.append(Trial(trial_id, kind, task_dir, fixture_dir))
    return trials


def pre_flight(args, trials: list[Trial]) -> None:
    issues: list[str] = []
    # Disk space — skip for dry-run since we write tiny placeholders.
    if not args.dry_run and hasattr(os, "statvfs"):
        st = os.statvfs(args.output_dir)
        free = st.f_bavail * st.f_frsize
        if free < PRE_FLIGHT_DISK_BYTES:
            issues.append(f"disk: {free} bytes free < {PRE_FLIGHT_DISK_BYTES}")
    # Trials: the manifest is the contract; require the pre-registered count.
    if not args.dry_run and len(trials) != EXPECTED_TRIAL_COUNT:
        issues.append(
            f"trials: expected {EXPECTED_TRIAL_COUNT} from manifest, "
            f"found {len(trials)}"
        )
    for t in trials:
        if not t.task_dir.is_dir():
            issues.append(f"trial {t.trial_id}: task_dir missing: {t.task_dir}")
        if t.kind == "task" and (t.fixture_dir is None or not t.fixture_dir.is_dir()):
            issues.append(
                f"trial {t.trial_id}: fixture workspace missing: "
                f"{t.fixture_dir}"
            )
    # Arm SHAs resolvable.
    if not args.dry_run:
        for arm, branch in (
            ("A", args.arm_a_ref),
            ("B", args.arm_b_ref),
            ("C", args.arm_c_ref),
        ):
            try:
                git_head_sha(branch)
            except RuntimeError as exc:
                issues.append(f"arm {arm}: {exc}")
    # Model sanity ping is provider-specific; placeholder.
    if not args.skip_model_ping and not args.dry_run:
        issues.append(
            "model sanity ping not implemented; supply --skip-model-ping "
            "to bypass after manually verifying availability"
        )
    if issues:
        print("pre-flight FAILED:", file=sys.stderr)
        for i in issues:
            print(f"  - {i}", file=sys.stderr)
        sys.exit(2)
    print("pre-flight OK")


def freeze_arm_shas(args) -> dict[str, str]:
    if args.dry_run:
        # Synthesise stable, recognisable placeholders.
        return {
            "A": "0" * 40,
            "B": "1" * 40,
            "C": "2" * 40,
        }
    shas = {
        "A": git_head_sha(args.arm_a_ref),
        "B": git_head_sha(args.arm_b_ref),
        "C": git_head_sha(args.arm_c_ref),
    }
    print(f"arm SHAs frozen at gate kickoff:")
    for arm, sha in shas.items():
        print(f"  Arm {arm} ({getattr(args, f'arm_{arm.lower()}_ref')}): {sha}")
    return shas


def write_shas_into_prereg(pre_reg_path: Path, shas: dict[str, str], dry_run: bool = False) -> None:
    if dry_run:
        # Don't pollute the pre-reg document with synthetic SHAs.
        return
    if not pre_reg_path.exists():
        print(f"warning: pre-reg path not found: {pre_reg_path}",
              file=sys.stderr)
        return
    text = pre_reg_path.read_text(encoding="utf-8")
    marker = "Arm SHAs frozen at gate kickoff:\n"
    block = (
        marker
        + f"  Arm A: {shas['A']}\n"
        + f"  Arm B: {shas['B']}\n"
        + f"  Arm C: {shas['C']}\n"
        + f"  Recorded: {utcnow_iso()}\n"
    )
    # Append at end, do not edit existing prose.
    pre_reg_path.write_text(text + "\n" + block, encoding="utf-8")
    print(f"appended arm SHAs to {pre_reg_path}")


def dispatch_run(
    trial: Trial,
    arm: str,
    seed: int,
    model: str,
    output_dir: Path,
    dry_run: bool,
    simulate: str = "trivial",
    sim_seed: int = 42,
) -> dict:
    started = utcnow_iso()
    # Use a filesystem-safe slug for the per-trial log subtree.
    slug = trial.trial_id.replace(":", "__").replace("/", "_")
    raw_log = output_dir / arm / slug / f"seed-{seed}" / "log.jsonl"
    raw_log.parent.mkdir(parents=True, exist_ok=True)

    if dry_run:
        record = _synth_record(
            trial, arm, seed, model, started, raw_log, simulate, sim_seed)
        return record

    # Real invocation — delegate to the existing harness if present.
    harness_sh = REPO_ROOT / "tests" / "E2E" / "agent-tasks" / "run.sh"
    if not harness_sh.exists():
        return {
            "task_id": trial.trial_id,
            "arm": arm,
            "seed": seed,
            "model": model,
            "started_at": started,
            "ended_at": utcnow_iso(),
            "success": False,
            "turn_count": 0,
            "total_output_tokens": 0,
            "identity_preservation_errors": 0,
            "edit_correctness_errors": 0,
            "harness_crash": True,
            "raw_log_path": _rel_to_repo(raw_log),
            "harness_error": "run.sh missing",
        }
    cmd = [
        _resolve_bash(),
        str(harness_sh),
        "--trial-id",
        trial.trial_id,
        "--kind",
        trial.kind,
        "--task-dir",
        str(trial.task_dir),
    ]
    if trial.fixture_dir is not None:
        cmd.extend(["--fixture-dir", str(trial.fixture_dir)])
    cmd.extend([
        "--arm",
        arm,
        "--seed",
        str(seed),
        "--model",
        model,
        "--log",
        str(raw_log),
    ])
    cp = subprocess.run(cmd, capture_output=True, text=True, cwd=REPO_ROOT)
    ended = utcnow_iso()
    if cp.returncode != 0:
        return {
            "task_id": trial.trial_id,
            "arm": arm,
            "seed": seed,
            "model": model,
            "started_at": started,
            "ended_at": ended,
            "success": False,
            "turn_count": 0,
            "total_output_tokens": 0,
            "identity_preservation_errors": 0,
            "edit_correctness_errors": 0,
            "harness_crash": True,
            "raw_log_path": _rel_to_repo(raw_log),
            "harness_stderr": cp.stderr[-2000:],
        }
    # Expect the harness to write a one-line summary as its stdout final line.
    summary = {}
    for line in reversed(cp.stdout.splitlines()):
        line = line.strip()
        if not line:
            continue
        try:
            summary = json.loads(line)
            break
        except json.JSONDecodeError:
            continue
    record = {
        "task_id": trial.trial_id,
        "arm": arm,
        "seed": seed,
        "model": model,
        "started_at": started,
        "ended_at": ended,
        "success": bool(summary.get("success", False)),
        "turn_count": int(summary.get("turn_count", 0)),
        "total_output_tokens": int(summary.get("total_output_tokens", 0)),
        "identity_preservation_errors": int(
            summary.get("identity_preservation_errors", 0)
        ),
        "edit_correctness_errors": int(
            summary.get("edit_correctness_errors", 0)
        ),
        "harness_crash": False,
        "raw_log_path": _rel_to_repo(raw_log),
    }
    # Propagate any harness_error tag the adapter emitted (e.g.
    # "no_task_contract", "dry_run", "agent_cli_missing"). The analyser
    # relies on this to distinguish task-substrate gaps from genuine
    # agent failures; dropping it silently would corrupt criterion 1.
    if "harness_error" in summary:
        record["harness_error"] = str(summary["harness_error"])
    return record


def _synth_record(
    trial: Trial,
    arm: str,
    seed: int,
    model: str,
    started: str,
    raw_log: Path,
    simulate: str,
    sim_seed: int,
) -> dict:
    """Generate a deterministic plausible-looking dry-run record.

    ``simulate`` selects the regime:
      * ``trivial``   — all-zero placeholder (back-compat default).
      * ``gate-pass`` — Arms B and C show 15% turn / 20% token reduction
        over Arm A with ≥95% success and ≤1% identity errors.
      * ``gate-fail`` — no improvement and elevated identity errors so
        the analyzer reports a failed gate.

    The RNG seed combines ``sim_seed``, the per-run ``seed``, and the
    arm/task identifiers, so two invocations with the same arguments
    produce identical records — useful for CI reproducibility.
    """
    import hashlib
    import random

    base = f"{sim_seed}|{simulate}|{arm}|{trial.trial_id}|{seed}"
    digest = hashlib.sha256(base.encode("utf-8")).digest()
    rng = random.Random(int.from_bytes(digest[:8], "big"))

    if simulate == "trivial":
        return {
            "task_id": trial.trial_id,
            "arm": arm,
            "seed": seed,
            "model": model,
            "started_at": started,
            "ended_at": utcnow_iso(),
            "success": False,
            "turn_count": 0,
            "total_output_tokens": 0,
            "identity_preservation_errors": 0,
            "edit_correctness_errors": 0,
            "harness_crash": False,
            "raw_log_path": _rel_to_repo(raw_log),
            "dry_run": True,
        }

    # Arm A is the baseline; Arms B and C improve under gate-pass and
    # match-or-regress under gate-fail.
    if simulate == "gate-pass":
        a_turns_mean = 12
        b_turns_mean = 10  # ~17% reduction
        c_turns_mean = 9   # ~25% reduction
        a_tokens_mean = 6000
        b_tokens_mean = 5000  # ~17% reduction
        c_tokens_mean = 4500  # ~25% reduction
        success_rate = 0.97
        id_err_rate = 0.005
    elif simulate == "gate-fail":
        a_turns_mean = 12
        b_turns_mean = 13
        c_turns_mean = 13
        a_tokens_mean = 6000
        b_tokens_mean = 6200
        c_tokens_mean = 6100
        success_rate = 0.70
        id_err_rate = 0.05
    else:
        raise ValueError(f"unknown --simulate mode: {simulate!r}")

    turns = {"A": a_turns_mean, "B": b_turns_mean, "C": c_turns_mean}[arm]
    tokens = {"A": a_tokens_mean, "B": b_tokens_mean, "C": c_tokens_mean}[arm]

    # Add a touch of per-run noise so the analyzer's stats are non-trivial.
    turn_count = max(1, int(round(rng.gauss(turns, max(1.0, turns * 0.10)))))
    total_output_tokens = max(
        100, int(round(rng.gauss(tokens, max(50.0, tokens * 0.08)))))
    success = rng.random() < success_rate
    identity_preservation_errors = 1 if rng.random() < id_err_rate else 0
    edit_correctness_errors = 0 if success else 1

    return {
        "task_id": trial.trial_id,
        "arm": arm,
        "seed": seed,
        "model": model,
        "started_at": started,
        "ended_at": utcnow_iso(),
        "success": success,
        "turn_count": turn_count,
        "total_output_tokens": total_output_tokens,
        "identity_preservation_errors": identity_preservation_errors,
        "edit_correctness_errors": edit_correctness_errors,
        "harness_crash": False,
        "raw_log_path": _rel_to_repo(raw_log),
        "dry_run": True,
        "simulate_mode": simulate,
    }


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument(
        "--pre-reg",
        default="docs/plans/phase-2-measurement-protocol.md",
        help="Path to the pre-registration document.",
    )
    p.add_argument("--output-dir", default="results", help="Output directory.")
    p.add_argument(
        "--arm-a-ref", default="main",
        help="Git ref for Arm A (today/baseline).",
    )
    p.add_argument(
        "--arm-b-ref", default="release/0.x+1",
        help="Git ref for Arm B (Phase 1 only).",
    )
    p.add_argument(
        "--arm-c-ref", default="release/0.x+1",
        help="Git ref for Arm C (Phase 1 + Phase 2).",
    )
    p.add_argument("--model", default="<pinned-string-required>")
    p.add_argument("--seeds", default=",".join(str(s) for s in DEFAULT_SEEDS),
                   help="Comma-separated seed list.")
    p.add_argument("--dry-run", action="store_true",
                   help="Skip real agent invocations; emit placeholder records.")
    p.add_argument(
        "--simulate",
        choices=("trivial", "gate-pass", "gate-fail"),
        default="trivial",
        help=("With --dry-run, the regime to simulate: trivial = zeros, "
              "gate-pass = seeded plausible records that pass the gate, "
              "gate-fail = seeded records that fail it."),
    )
    p.add_argument("--simulate-seed", type=int, default=42,
                   help="Seed for the dry-run record generator.")
    p.add_argument("--skip-model-ping", action="store_true",
                   help="Skip the pre-flight model availability check.")
    p.add_argument(
        "--tasks-manifest",
        default=str(TASK_MANIFEST),
        help=(
            "Path to the trial manifest (one `kind:value` per line). "
            "v2 §10.1.b makes this part of the pre-registration: "
            "specify it explicitly at kickoff so the chosen manifest is "
            "recorded in shell history and CI logs even though it has a "
            "sensible default."
        ),
    )
    args = p.parse_args(argv)

    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    seeds = [int(s) for s in args.seeds.split(",") if s.strip()]
    manifest_path = Path(args.tasks_manifest).resolve()
    trials = collect_trials(manifest_path)
    print(
        f"discovered {len(trials)} trials from manifest "
        f"({manifest_path.relative_to(REPO_ROOT) if manifest_path.is_relative_to(REPO_ROOT) else manifest_path})"
    )

    pre_flight(args, trials)
    shas = freeze_arm_shas(args)
    write_shas_into_prereg(Path(args.pre_reg), shas, dry_run=args.dry_run)

    runs_jsonl = output_dir / "runs.jsonl"
    n_total = len(trials) * len(seeds) * len(ARMS)
    print(
        f"starting {n_total} runs ({len(trials)} trials × "
        f"{len(seeds)} seeds × {len(ARMS)} arms)"
    )
    with runs_jsonl.open("a", encoding="utf-8") as out:
        i = 0
        for trial in trials:
            for arm in ARMS:
                for seed in seeds:
                    i += 1
                    rec = dispatch_run(
                        trial=trial,
                        arm=arm,
                        seed=seed,
                        model=args.model,
                        output_dir=output_dir,
                        dry_run=args.dry_run,
                        simulate=args.simulate,
                        sim_seed=args.simulate_seed,
                    )
                    out.write(json.dumps(rec) + "\n")
                    out.flush()
                    print(
                        f"  [{i}/{n_total}] {trial.trial_id} arm={arm} "
                        f"seed={seed} success={rec.get('success')}"
                    )
    print(f"wrote {n_total} run records to {runs_jsonl}")
    print("next: python3 scripts/analyze_gate_results.py "
          f"--runs {runs_jsonl}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
