#!/usr/bin/env python3
"""
Phase 3 OFF-PROTOCOL comprehension micro-smoke.

NOT the validation plan's Tier 1.5. This is a cheap real-LLM signal that
upgrades the Tier 0 mechanical measurement with actual model behavior.
Measures whether Claude can read CLOSER-form vs INDENT-form Calor source
with equal accuracy and equal token cost.

Arm 0 = original closer-form .calr files
Arm I = same files with closer lines stripped (mechanical indent form)

For each (file, arm, repeat) tuple we make ONE claude --print invocation
asking 3 structural questions about the file, then score the answers
against a fixed key.

Usage:
    python scripts/phase3_smoke_comprehension.py [--dry-run] [--repeats N]

Output: scripts/phase3-smoke-comprehension-results.{json,md}
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

ROOT = Path(__file__).resolve().parent.parent

CLOSER_LINE_RE = re.compile(r"^\s*§/[A-Z][A-Z0-9_]*(?:\{[^}]*\})?\s*(?://.*)?$")


def strip_closer_lines(src: str) -> str:
    """Mechanical Arm-I transform: drop lines that are pure closing tags."""
    return "\n".join(
        line for line in src.splitlines() if not CLOSER_LINE_RE.match(line)
    )


@dataclass
class Question:
    text: str
    expected: str  # canonical answer (lowercased, trimmed for comparison)


@dataclass
class FileSpec:
    relpath: str
    questions: list[Question]


SPECS: list[FileSpec] = [
    FileSpec(
        relpath="samples/Verification/violation-detected.calr",
        questions=[
            Question("How many top-level functions are defined in this file?", "3"),
            Question(
                "What is the name of the second function defined (in source order)?",
                "impossiblecontract",
            ),
            Question(
                "How many input parameters does the third function defined accept?",
                "2",
            ),
        ],
    ),
    FileSpec(
        relpath="samples/Contracts/contracts.calr",
        questions=[
            Question("How many top-level functions are defined in this file?", "3"),
            Question(
                "Which function declares the cw effect? Reply with just the function name.",
                "main",
            ),
            Question(
                "In the Divide function's precondition, the parameter b is compared "
                "to which integer? Reply with just the integer.",
                "0",
            ),
        ],
    ),
    FileSpec(
        relpath="samples/Verification/mixed-contracts.calr",
        questions=[
            Question("How many top-level functions are defined in this file?", "4"),
            Question(
                "Which function contains an if/else block (uses §IF and §EL)? "
                "Reply with just the function name.",
                "clamppositive",
            ),
            Question(
                "How many input parameters does the SumArray function accept?",
                "1",
            ),
        ],
    ),
    FileSpec(
        relpath="samples/Verification/proven-contracts.calr",
        questions=[
            Question("How many top-level functions are defined in this file?", "5"),
            Question(
                "How many functions have BOTH a precondition (§Q) AND a postcondition (§S)? "
                "Reply with just an integer.",
                "2",
            ),
            Question(
                "Which function contains TWO §IF blocks (i.e. uses §IF twice in its body)? "
                "Reply 'none' if no function does, otherwise reply with the function name.",
                "none",
            ),
        ],
    ),
    FileSpec(
        relpath="samples/PatternMatching/matching.calr",
        questions=[
            Question(
                "How many distinct §W (switch / match) blocks does the Main function contain? "
                "Reply with just an integer.",
                "5",
            ),
            Question(
                "How many top-level functions are defined in this file? Reply with just an integer.",
                "1",
            ),
            Question(
                "In the temperature switch (sw5), which string is returned for the case '§PREL{lt} 20'? "
                "Reply with just the lowercase string content.",
                "cool",
            ),
        ],
    ),
]


PROMPT_TEMPLATE = """\
Below is a Calor source file. Read it carefully, then answer the questions.

File: {label}
```
{body}
```

Questions:
1. {q1}
2. {q2}
3. {q3}

Reply in this EXACT format, one answer per line, with no extra commentary:
1: <answer>
2: <answer>
3: <answer>
"""


@dataclass
class Trial:
    file_relpath: str
    arm: str  # "closer" or "indent"
    repeat: int
    raw_response: str = ""
    answers: list[str] = field(default_factory=list)
    correct: list[bool] = field(default_factory=list)
    num_turns: int = 0
    input_tokens: int = 0
    output_tokens: int = 0
    cache_creation_tokens: int = 0
    cache_read_tokens: int = 0
    cost_usd: float = 0.0
    duration_ms: int = 0
    is_error: bool = False
    error: str = ""


def build_prompt(spec: FileSpec, body: str, label: str) -> str:
    return PROMPT_TEMPLATE.format(
        label=label,
        body=body,
        q1=spec.questions[0].text,
        q2=spec.questions[1].text,
        q3=spec.questions[2].text,
    )


ANSWER_LINE_RE = re.compile(r"^\s*([123])\s*[:\.\)]\s*(.+?)\s*$")


def parse_answers(response: str) -> list[str]:
    out = ["", "", ""]
    for line in response.splitlines():
        m = ANSWER_LINE_RE.match(line)
        if m:
            idx = int(m.group(1)) - 1
            if 0 <= idx < 3 and not out[idx]:
                out[idx] = m.group(2).strip()
    return out


def normalize_for_match(s: str) -> str:
    s = s.strip().lower()
    # Strip trailing punctuation and a few stop-words
    s = re.sub(r"[.,;!?\"'`]+$", "", s)
    s = re.sub(r"\s+", " ", s)
    return s


def score(answers: list[str], spec: FileSpec) -> list[bool]:
    out = []
    for ans, q in zip(answers, spec.questions):
        norm = normalize_for_match(ans)
        # Allow the expected token to appear as a whole word inside the answer
        exp = q.expected.lower()
        if norm == exp:
            out.append(True)
        elif re.search(rf"\b{re.escape(exp)}\b", norm):
            out.append(True)
        else:
            out.append(False)
    return out


def run_claude_once(prompt: str, tmpdir: Path, model: str, budget: float) -> dict:
    """Invoke claude --print --output-format json from a clean cwd."""
    p = subprocess.run(
        [
            "claude",
            "--print",
            "--output-format",
            "json",
            "--model",
            model,
            "--max-budget-usd",
            str(budget),
            "--system-prompt",
            "You are a careful code reader. Answer concisely in the requested format.",
        ],
        input=prompt,
        cwd=str(tmpdir),
        text=True,
        capture_output=True,
        encoding="utf-8",
        errors="replace",
    )
    stdout = (p.stdout or "").strip()
    if not stdout:
        return {"is_error": True, "error_text": p.stderr.strip()[:500] or "no stdout"}
    try:
        return json.loads(stdout)
    except json.JSONDecodeError as e:
        return {"is_error": True, "error_text": f"json parse error: {e}: {stdout[:300]}"}


def run_trial(spec: FileSpec, arm: str, repeat: int, tmpdir: Path, model: str,
              budget: float) -> Trial:
    src = (ROOT / spec.relpath).read_text(encoding="utf-8")
    body = src if arm == "closer" else strip_closer_lines(src)
    prompt = build_prompt(spec, body, spec.relpath)

    t = Trial(file_relpath=spec.relpath, arm=arm, repeat=repeat)
    j = run_claude_once(prompt, tmpdir, model=model, budget=budget)

    if j.get("is_error"):
        t.is_error = True
        t.error = (
            j.get("error_text")
            or j.get("subtype")
            or j.get("error")
            or "unknown"
        )[:300]
        # Some "is_error: true" still carry usage stats (e.g. budget hit)
        usage = j.get("usage") or {}
        t.input_tokens = int(usage.get("input_tokens") or 0)
        t.output_tokens = int(usage.get("output_tokens") or 0)
        t.cache_creation_tokens = int(usage.get("cache_creation_input_tokens") or 0)
        t.cache_read_tokens = int(usage.get("cache_read_input_tokens") or 0)
        t.cost_usd = float(j.get("total_cost_usd") or 0.0)
        t.duration_ms = int(j.get("duration_ms") or 0)
        return t

    t.raw_response = (j.get("result") or "").strip()
    t.num_turns = int(j.get("num_turns") or 0)
    usage = j.get("usage") or {}
    t.input_tokens = int(usage.get("input_tokens") or 0)
    t.output_tokens = int(usage.get("output_tokens") or 0)
    t.cache_creation_tokens = int(usage.get("cache_creation_input_tokens") or 0)
    t.cache_read_tokens = int(usage.get("cache_read_input_tokens") or 0)
    t.cost_usd = float(j.get("total_cost_usd") or 0.0)
    t.duration_ms = int(j.get("duration_ms") or 0)
    t.answers = parse_answers(t.raw_response)
    t.correct = score(t.answers, spec)
    return t


def aggregate(trials: list[Trial], arm: str) -> dict:
    arm_trials = [t for t in trials if t.arm == arm and not t.is_error]
    if not arm_trials:
        return {"arm": arm, "n_trials": 0, "n_errors": sum(1 for t in trials if t.arm == arm)}
    n_q = sum(len(t.correct) for t in arm_trials)
    n_c = sum(sum(t.correct) for t in arm_trials)
    n = len(arm_trials)
    # Effective "prompt body" tokens = the new content the model had to ingest
    # for this trial. cache_read_input_tokens is the cached CLI system prompt
    # shared across all trials, so it's not a per-arm cost signal.
    body_tokens = [t.input_tokens + t.cache_creation_tokens for t in arm_trials]
    return {
        "arm": arm,
        "n_trials": n,
        "n_errors": sum(1 for t in trials if t.arm == arm and t.is_error),
        "n_questions": n_q,
        "n_correct": n_c,
        "accuracy": n_c / n_q if n_q else 0.0,
        "mean_input_tokens": sum(t.input_tokens for t in arm_trials) / n,
        "mean_cache_creation_tokens":
            sum(t.cache_creation_tokens for t in arm_trials) / n,
        "mean_cache_read_tokens":
            sum(t.cache_read_tokens for t in arm_trials) / n,
        "mean_body_tokens": sum(body_tokens) / n,  # input + cache_creation
        "mean_output_tokens": sum(t.output_tokens for t in arm_trials) / n,
        "mean_cost_usd": sum(t.cost_usd for t in arm_trials) / n,
        "total_cost_usd": sum(t.cost_usd for t in arm_trials),
        "mean_duration_ms": sum(t.duration_ms for t in arm_trials) / n,
    }


def write_outputs(trials: list[Trial], agg_closer: dict, agg_indent: dict) -> None:
    out_json = ROOT / "scripts" / "phase3-smoke-comprehension-results.json"
    out_md = ROOT / "scripts" / "phase3-smoke-comprehension-results.md"

    payload = {
        "kind": "off-protocol comprehension micro-smoke",
        "warning": "NOT validation plan Tier 1.5. Directional signal only.",
        "model": "claude-haiku-4-5",
        "n_files": len(SPECS),
        "trials": [asdict(t) for t in trials],
        "aggregate": {"arm_closer": agg_closer, "arm_indent": agg_indent},
    }
    out_json.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    def fmt(d: dict, key: str, default: str = "n/a") -> str:
        v = d.get(key)
        if v is None:
            return default
        if isinstance(v, float):
            return f"{v:,.4f}" if v < 1 else f"{v:,.2f}"
        return str(v)

    md_lines = [
        "# Phase 3 — Off-protocol comprehension micro-smoke",
        "",
        "**NOT validation plan Tier 1.5.** Directional real-LLM signal that",
        "upgrades Tier 0's mechanical measurement with actual model behavior.",
        "",
        f"- Model: `{payload['model']}`",
        f"- Files: {len(SPECS)}",
        "- Questions per file: 3",
        "- Arms: closer (original), indent (closer lines stripped via regex)",
        f"- Repeats per (file × arm): {1 + max((t['repeat'] for t in payload['trials']), default=-1)}",
        f"- Total trials: {len(payload['trials'])} (={len(SPECS)} files × 2 arms × {1 + max((t['repeat'] for t in payload['trials']), default=-1)} repeats)",
        "",
        "## Aggregate",
        "",
        "`mean_body_tokens` = `input_tokens + cache_creation_tokens` "
        "(the actual NEW content the model ingested per trial; "
        "`cache_read` is the shared CLI baseline ~24K tokens that's identical for both arms).",
        "",
        "| Arm | n_trials | n_errors | accuracy | mean_body_tokens | mean_output_tokens | mean_cost_usd | total_cost_usd |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
        f"| closer | {fmt(agg_closer,'n_trials')} | {fmt(agg_closer,'n_errors')} | "
        f"{fmt(agg_closer,'accuracy')} | {fmt(agg_closer,'mean_body_tokens')} | "
        f"{fmt(agg_closer,'mean_output_tokens')} | {fmt(agg_closer,'mean_cost_usd')} | "
        f"{fmt(agg_closer,'total_cost_usd')} |",
        f"| indent | {fmt(agg_indent,'n_trials')} | {fmt(agg_indent,'n_errors')} | "
        f"{fmt(agg_indent,'accuracy')} | {fmt(agg_indent,'mean_body_tokens')} | "
        f"{fmt(agg_indent,'mean_output_tokens')} | {fmt(agg_indent,'mean_cost_usd')} | "
        f"{fmt(agg_indent,'total_cost_usd')} |",
        "",
        "**Body-token delta (indent vs closer):** "
        f"{(agg_indent.get('mean_body_tokens',0) - agg_closer.get('mean_body_tokens',0)):+.1f} tokens "
        f"({(agg_indent.get('mean_body_tokens',1) / max(agg_closer.get('mean_body_tokens',1),1) - 1) * 100:+.2f}%)",
        "",
        "**Output-token delta (indent vs closer):** "
        f"{(agg_indent.get('mean_output_tokens',0) - agg_closer.get('mean_output_tokens',0)):+.1f} tokens "
        f"({(agg_indent.get('mean_output_tokens',1) / max(agg_closer.get('mean_output_tokens',1),1) - 1) * 100:+.2f}%)",
        "",
        "## Per-trial detail",
        "",
        "| File | Arm | Repeat | Correct | Answers | body_tokens | out_tokens | Cost | Err |",
        "|---|---|---:|---:|---|---:|---:|---:|---|",
    ]
    for t in trials:
        correct_str = f"{sum(t.correct)}/3" if t.correct else "-"
        # Escape pipes in answers so MD table parses correctly
        ans = " / ".join((a or "-").replace("|", "\\|") for a in (t.answers or ["-", "-", "-"]))
        body = t.input_tokens + t.cache_creation_tokens
        err = "yes" if t.is_error else ""
        md_lines.append(
            f"| {Path(t.file_relpath).name} | {t.arm} | {t.repeat} | "
            f"{correct_str} | {ans} | {body} | {t.output_tokens} | ${t.cost_usd:.4f} | {err} |"
        )
    out_md.write_text("\n".join(md_lines) + "\n", encoding="utf-8")
    print(f"Wrote {out_json}")
    print(f"Wrote {out_md}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="Print prompts and exit; no claude calls")
    ap.add_argument("--repeats", type=int, default=2,
                    help="Repeats per (file × arm). Default 2 (12 total calls).")
    ap.add_argument("--model", default="haiku")
    ap.add_argument("--budget", type=float, default=0.05,
                    help="Per-call max budget in USD. Default $0.05.")
    args = ap.parse_args()

    if args.dry_run:
        for spec in SPECS:
            src = (ROOT / spec.relpath).read_text(encoding="utf-8")
            for arm, body in (("closer", src), ("indent", strip_closer_lines(src))):
                print(f"\n===== {spec.relpath} | arm={arm} =====")
                print(build_prompt(spec, body, spec.relpath))
        return 0

    if shutil.which("claude") is None:
        print("ERROR: claude CLI not on PATH", file=sys.stderr)
        return 2

    tmpdir = Path(tempfile.mkdtemp(prefix="claude-smoke-"))
    print(f"Using clean cwd: {tmpdir}")
    trials: list[Trial] = []
    try:
        # Order: alternate arms to keep cache warm across both arms equally.
        for spec in SPECS:
            for repeat in range(args.repeats):
                for arm in ("closer", "indent"):
                    print(
                        f"  trial: {Path(spec.relpath).name} arm={arm} rep={repeat}...",
                        flush=True,
                    )
                    t0 = time.time()
                    t = run_trial(spec, arm, repeat, tmpdir,
                                  model=args.model, budget=args.budget)
                    elapsed = time.time() - t0
                    trials.append(t)
                    status = (
                        f"err({t.error[:40]})" if t.is_error
                        else f"correct={sum(t.correct)}/3"
                    )
                    print(
                        f"    -> {status} tok={t.input_tokens}/{t.output_tokens} "
                        f"cost=${t.cost_usd:.4f} wall={elapsed:.1f}s"
                    )
    finally:
        shutil.rmtree(tmpdir, ignore_errors=True)

    agg_c = aggregate(trials, "closer")
    agg_i = aggregate(trials, "indent")
    print("\n=== aggregate ===")
    print("closer:", json.dumps(agg_c, indent=2))
    print("indent:", json.dumps(agg_i, indent=2))
    write_outputs(trials, agg_c, agg_i)
    return 0


if __name__ == "__main__":
    sys.exit(main())
