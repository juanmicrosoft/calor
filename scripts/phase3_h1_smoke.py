#!/usr/bin/env python3
"""Phase 3 H1 micro-smoke: agent write-task pass rate, Arm 0 vs Arm I.

For each task:
  - Arm 0: prompt with ORIGINAL closer-form fixture; agent returns full new file
  - Arm I: prompt with INDENT-form fixture; agent returns full new file in
           indent form, which we then convert back via from_indent
  - Compile result with calor.exe; pass iff exit code == 0

Configuration: 3 tasks * 2 arms * 3 trials = 18 invocations on claude-haiku-4-5.
Estimated cost: ~$0.20-0.40.
"""
from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import from_indent, load_openers, to_indent  # type: ignore

REPO = Path(__file__).resolve().parent.parent
CALOR_EXE = REPO / 'src/Calor.Compiler/bin/Debug/net10.0/calor.exe'
FIXTURE = REPO / 'tests/E2E/agent-tasks/fixtures/basic-calor-project/Calculator.calr'
MODEL = 'claude-haiku-4-5'

# Three small tasks all using basic-calor-project/Calculator.calr
TASKS = [
    {
        'id': 'basic-001',
        'prompt': "Add a new public function called Multiply to Calculator.calr that takes two i32 parameters (a and b) and returns the product (i32). Use Calor section syntax.",
        # success: file compiles + has Multiply
        'must_contain': ['Multiply'],
    },
    {
        'id': 'contract-001',
        'prompt': "Add a new public function called SquareRoot to Calculator.calr that takes one i32 parameter x and returns i32. Add a §Q precondition requiring x >= 0. For the body, just return x.",
        'must_contain': ['SquareRoot', '§Q'],
    },
    {
        'id': 'logic-003',
        'prompt': "Add a new public function called IsPositive to Calculator.calr that takes one i32 parameter n and returns bool. Add a §S postcondition asserting that result is true if and only if n > 0. Implement the body as (> n 0).",
        'must_contain': ['IsPositive', '§S'],
    },
]

SYSTEM_PROMPT_CLOSER = """You are a Calor language expert. Calor uses section markers like §F for functions and explicit closers like §/F for end-of-block.

When asked to modify a Calor file, return ONLY the complete updated file content inside a single ```calor code block. No explanation, no commentary, no extra text before or after the code block."""

SYSTEM_PROMPT_INDENT = """You are a Calor language expert. Calor in INDENT FORM uses Python-style indentation: section markers like §F open blocks; their body is indented under them with 2-space increments. NO explicit closer tags like §/F — dedent ends the block. Chain continuations like §EI/§EL appear at the SAME indent as their parent §IF.

When asked to modify a Calor file, return ONLY the complete updated file content inside a single ```calor code block. Maintain consistent 2-space indentation. No explanation, no commentary, no extra text."""


@dataclass
class Trial:
    task_id: str
    arm: str
    trial: int
    duration_ms: int = 0
    cost_usd: float = 0.0
    raw_response: str = ''
    extracted: str = ''
    rt_converted: str = ''
    compile_ok: bool = False
    compile_stderr: str = ''
    must_contain_ok: bool = False
    error: str = ''


def extract_code_block(text: str) -> str:
    """Extract content of first ```calor fenced block; fall back to any
    fenced block; fall back to raw text."""
    m = re.search(r'```(?:calor)?\s*\n(.*?)```', text, re.DOTALL)
    if m:
        return m.group(1).strip('\n')
    return text.strip()


def run_claude(prompt: str, system_prompt: str, max_cost: float = 0.10) -> dict:
    """Invoke claude --print with a clean cwd; return parsed json."""
    with tempfile.TemporaryDirectory(prefix='claude-smoke-') as td:
        cmd = [
            'claude', '--print',
            '--output-format', 'json',
            '--model', MODEL,
            '--system-prompt', system_prompt,
            '--max-budget-usd', str(max_cost),
            '--dangerously-skip-permissions',
            prompt,
        ]
        t0 = time.time()
        try:
            r = subprocess.run(
                cmd, cwd=td, capture_output=True, text=True,
                timeout=300, encoding='utf-8',
            )
        except subprocess.TimeoutExpired:
            return {'_error': 'timeout', '_duration_ms': int((time.time() - t0) * 1000)}
        dur_ms = int((time.time() - t0) * 1000)
        if r.returncode != 0:
            return {'_error': f'claude exit {r.returncode}: {r.stderr[:500]}', '_duration_ms': dur_ms}
        try:
            j = json.loads(r.stdout)
        except json.JSONDecodeError:
            return {'_error': f'bad json: {r.stdout[:500]}', '_duration_ms': dur_ms}
        j['_duration_ms'] = dur_ms
        return j


def compile_calor(src: str, tmpdir: Path) -> tuple[bool, str]:
    src_f = tmpdir / 'Calculator.calr'
    out_f = tmpdir / 'Calculator.g.cs'
    src_f.write_text(src, encoding='utf-8')
    try:
        r = subprocess.run(
            [str(CALOR_EXE), '--input', str(src_f), '-o', str(out_f)],
            capture_output=True, text=True, timeout=60, encoding='utf-8',
        )
    except subprocess.TimeoutExpired:
        return False, 'compile timeout'
    if r.returncode == 0:
        return True, ''
    return False, (r.stdout + r.stderr)[:800]


def run_trial(task: dict, arm: str, trial_idx: int, openers: dict) -> Trial:
    t = Trial(task_id=task['id'], arm=arm, trial=trial_idx)
    fixture_src = FIXTURE.read_text(encoding='utf-8')

    if arm == 'closer':
        prompt_body = fixture_src
        system = SYSTEM_PROMPT_CLOSER
    else:  # indent
        prompt_body = to_indent(fixture_src, openers)
        system = SYSTEM_PROMPT_INDENT

    prompt = (
        f"Here is the current Calculator.calr file:\n\n"
        f"```calor\n{prompt_body}```\n\n"
        f"TASK: {task['prompt']}\n\n"
        f"Return the complete updated file in a single ```calor code block."
    )

    result = run_claude(prompt, system, max_cost=0.05)
    t.duration_ms = result.get('_duration_ms', 0)
    if '_error' in result:
        t.error = result['_error']
        return t

    t.cost_usd = float(result.get('total_cost_usd', 0.0))
    t.raw_response = result.get('result', '')
    t.extracted = extract_code_block(t.raw_response)

    if arm == 'closer':
        to_compile = t.extracted
    else:
        try:
            to_compile = from_indent(t.extracted, openers)
            t.rt_converted = to_compile
        except Exception as e:  # noqa: BLE001
            t.error = f'from_indent: {e!r}'
            return t

    with tempfile.TemporaryDirectory(prefix='calor-compile-') as td:
        ok, err = compile_calor(to_compile, Path(td))
    t.compile_ok = ok
    t.compile_stderr = err
    t.must_contain_ok = all(s in to_compile for s in task['must_contain'])
    return t


def main() -> int:
    if not CALOR_EXE.exists():
        print(f'calor.exe missing: {CALOR_EXE}', file=sys.stderr)
        return 1
    if not FIXTURE.exists():
        print(f'fixture missing: {FIXTURE}', file=sys.stderr)
        return 1
    if not shutil.which('claude'):
        print('claude not on PATH', file=sys.stderr)
        return 1

    openers = load_openers()
    trials: list[Trial] = []
    n_trials = 3

    print(f'Running {len(TASKS)} tasks * 2 arms * {n_trials} trials = '
          f'{len(TASKS) * 2 * n_trials} invocations')
    print(f'Model: {MODEL}\n')

    for task in TASKS:
        for arm in ('closer', 'indent'):
            for t_idx in range(n_trials):
                print(f'  {task["id"]:14s}  {arm:7s}  trial {t_idx + 1}/{n_trials} ...', end=' ', flush=True)
                tr = run_trial(task, arm, t_idx, openers)
                trials.append(tr)
                tag = 'OK' if tr.compile_ok and tr.must_contain_ok else (
                    'COMPILE_FAIL' if not tr.compile_ok else 'MISSING')
                if tr.error:
                    tag = f'ERR ({tr.error[:60]})'
                print(f'{tag}  ${tr.cost_usd:.4f}  {tr.duration_ms / 1000:.1f}s')

    # Summary
    print('\n--- RESULTS ---')
    by_arm: dict[str, list[Trial]] = {'closer': [], 'indent': []}
    for tr in trials:
        by_arm[tr.arm].append(tr)
    for arm, ts in by_arm.items():
        passes = sum(1 for t in ts if t.compile_ok and t.must_contain_ok)
        compiles = sum(1 for t in ts if t.compile_ok)
        cost = sum(t.cost_usd for t in ts)
        dur = sum(t.duration_ms for t in ts) / 1000
        print(f'  Arm {arm:7s}: {passes}/{len(ts)} pass  '
              f'({compiles}/{len(ts)} compile)  ${cost:.3f}  {dur:.1f}s')

    out = REPO / 'scripts' / 'phase3-h1-smoke-results.json'
    out.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')
    print(f'\nDetailed results: {out}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
