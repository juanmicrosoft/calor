#!/usr/bin/env python3
"""Phase 3 H1 REPLICATION smoke (v2): controlled prompts, more tasks, more trials.

Differences from phase3_h1_smoke.py:
  - Equalized system prompts (same length/tone/Calor reference; differs ONLY
    in the one paragraph describing block delimiters)
  - 7 tasks instead of 3 (1 trivial control + 6 contract-heavy)
  - 5 trials instead of 3 (better statistical power)
  - Fisher exact test on per-trial pass/fail
  - Hard budget cap ($5)
"""
from __future__ import annotations

import json
import math
import re
import shutil
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import from_indent, load_openers, to_indent  # type: ignore

REPO = Path(__file__).resolve().parent.parent
CALOR_EXE = REPO / 'src/Calor.Compiler/bin/Debug/net10.0/calor.exe'
FIXTURE = REPO / 'tests/E2E/agent-tasks/fixtures/basic-calor-project/Calculator.calr'
MODEL = 'claude-haiku-4-5'
BUDGET_USD = 5.0
TRIALS_PER_CELL = 5

# 7 tasks: 1 trivial control + 6 contract-heavy.
TASKS = [
    {
        'id': 'basic-001',
        'prompt': "Add a new public function called Multiply to Calculator.calr that takes two i32 parameters (a and b) and returns the product (i32).",
        'must_contain': ['Multiply'],
        'kind': 'control',
    },
    {
        'id': 'contract-001',
        'prompt': "Add a new public function called SquareRoot to Calculator.calr that takes one i32 parameter x and returns i32. Add a §Q precondition requiring x >= 0. For the body, just return x.",
        'must_contain': ['SquareRoot', '§Q'],
        'kind': 'contract',
    },
    {
        'id': 'contract-002',
        'prompt': "Add a new public function called Abs to Calculator.calr that takes an i32 parameter x and returns its absolute value (i32). Add a §S postcondition ensuring the result is >= 0. Implement: return the negation of x if x < 0, otherwise return x.",
        'must_contain': ['Abs', '§S'],
        'kind': 'contract',
    },
    {
        'id': 'contract-004',
        'prompt': "Add a new public function called SafeDivide to Calculator.calr that takes two i32 parameters (a and b) and returns i32. Add a §Q precondition requiring b != 0. Add a §S postcondition ensuring (result * b) == a. Return a divided by b.",
        'must_contain': ['SafeDivide', '§Q', '§S'],
        'kind': 'contract',
    },
    {
        'id': 'contract-005',
        'prompt': "Add a new public function called SumToN to Calculator.calr that takes an i32 parameter n and returns the sum of integers from 0 to n (i32). Add a §Q precondition requiring n >= 0. Use the Gauss formula: n * (n + 1) / 2. Add a §S postcondition ensuring the result is >= 0.",
        'must_contain': ['SumToN', '§Q', '§S'],
        'kind': 'contract',
    },
    {
        'id': 'logic-002',
        'prompt': "Add a public function called Clamp to Calculator.calr that takes three i32 parameters (value, min, max) and returns i32. Add a §Q precondition requiring min <= max. Add §S postconditions ensuring result >= min AND result <= max. Return min if value < min, max if value > max, else value.",
        'must_contain': ['Clamp', '§Q', '§S'],
        'kind': 'contract',
    },
    {
        'id': 'logic-004',
        'prompt': "Add a public function called Max to Calculator.calr that takes two i32 parameters (a and b) and returns the maximum (i32). Add §S postconditions: result >= a, result >= b, and (result == a OR result == b). Return a if a >= b, else b.",
        'must_contain': ['Max', '§S'],
        'kind': 'contract',
    },
]

# Shared body. ONLY the block-delimiter paragraph differs.
SHARED_HEADER = """You are a Calor language expert. Calor is a DSL with section markers like §F (function), §M (module), §B (binding), §IF (conditional), §L (loop).

Function header: §F{id:Name:visibility} — example: §F{f001:Add:pub}
Inputs/Outputs:  §I{type:name}  §O{type}  — e.g. §I{i32:x}  §O{bool}
Bindings:        §B{name} value — e.g. §B{x} 5
Print:           §P "literal"  or  §P variable
Return:          §R expression  — e.g. §R (+ a b)

CONTRACT SYNTAX (CRITICAL):
  §Q is a precondition. It takes a Lisp-style expression in parentheses.
  §S is a postcondition. Same form.
  CORRECT:    §Q (>= x 0)   §S (== result a)   §S (&& (>= result a) (>= result b))
  WRONG:      §Q{x >= 0}    §S{result >= 0}    §S (= result ...)
  The condition is NEVER inside {...} braces — only function/binding NAMES go in braces.

EXPRESSIONS use Lisp prefix form:
  arithmetic:  (+ a b)  (- a b)  (* a b)  (/ a b)  (% a b)
  comparison:  (== x y)  (!= x y)  (< x y)  (<= x y)  (> x y)  (>= x y)
  logical:     (&& a b)  (|| a b)  (! a)
  The result variable inside §S refers to the function's return value.
"""

DELIM_CLOSER = """BLOCK STRUCTURE (closer form):
  Every block opener has a matching explicit closer with the SAME id:
    §F{f001:Foo:pub}
      §I{i32:x}
      §R x
    §/F{f001}
  IF blocks close with §/I{id}, modules with §/M{id}, loops with §/L{id}.
  Indentation is purely cosmetic; the closer is what ends the block.
"""

DELIM_INDENT = """BLOCK STRUCTURE (indent form, Python-style):
  Block bodies are indented under their opener with 2-space increments.
  Dedent ends the block — NO §/F, §/M, §/I, etc.
    §F{f001:Foo:pub}
      §I{i32:x}
      §R x
  Chain continuations (§EI, §EL, §K, §CA, §FI) sit at the SAME indent as
  their parent (§IF / §W / §TR).
"""

SHARED_FOOTER = """
When asked to modify a Calor file, return ONLY the complete updated file content inside a single ```calor code block. No explanation, no commentary, no extra text before or after the code block."""

SYSTEM_CLOSER = SHARED_HEADER + DELIM_CLOSER + SHARED_FOOTER
SYSTEM_INDENT = SHARED_HEADER + DELIM_INDENT + SHARED_FOOTER


@dataclass
class Trial:
    task_id: str
    task_kind: str
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
    m = re.search(r'```(?:calor)?\s*\n(.*?)```', text, re.DOTALL)
    if m:
        return m.group(1).strip('\n')
    return text.strip()


def run_claude(prompt: str, system: str, max_cost: float = 0.05) -> dict:
    with tempfile.TemporaryDirectory(prefix='claude-smoke-') as td:
        cmd = [
            'claude', '--print',
            '--output-format', 'json',
            '--model', MODEL,
            '--system-prompt', system,
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
        dur = int((time.time() - t0) * 1000)
        if r.returncode != 0:
            return {'_error': f'claude exit {r.returncode}: {r.stderr[:300]}', '_duration_ms': dur}
        try:
            j = json.loads(r.stdout)
        except json.JSONDecodeError:
            return {'_error': f'bad json: {r.stdout[:300]}', '_duration_ms': dur}
        j['_duration_ms'] = dur
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
    return False, (r.stdout + r.stderr)[:600]


def run_trial(task: dict, arm: str, trial_idx: int, openers: dict) -> Trial:
    t = Trial(task_id=task['id'], task_kind=task['kind'], arm=arm, trial=trial_idx)
    src = FIXTURE.read_text(encoding='utf-8')

    if arm == 'closer':
        body = src
        system = SYSTEM_CLOSER
    else:
        body = to_indent(src, openers)
        system = SYSTEM_INDENT

    prompt = (
        f"Here is the current Calculator.calr file:\n\n"
        f"```calor\n{body}```\n\n"
        f"TASK: {task['prompt']}\n\n"
        f"Return the complete updated file in a single ```calor code block."
    )

    res = run_claude(prompt, system)
    t.duration_ms = res.get('_duration_ms', 0)
    if '_error' in res:
        t.error = res['_error']
        return t
    t.cost_usd = float(res.get('total_cost_usd', 0.0))
    t.raw_response = res.get('result', '')
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


def fisher_exact_2x2(a: int, b: int, c: int, d: int) -> float:
    """Two-sided Fisher exact p-value for 2x2 contingency table:
        | pass | fail |
    A   |  a   |  b   |
    B   |  c   |  d   |
    Returns p-value. Pure-Python (no scipy)."""
    n = a + b + c + d
    row1, row2 = a + b, c + d
    col1, col2 = a + c, b + d

    def log_factorial(n: int) -> float:
        return math.lgamma(n + 1)

    def hypergeom_logpmf(k: int) -> float:
        if k < 0 or k > col1 or (row1 - k) < 0 or (row1 - k) > col2:
            return -math.inf
        return (
            log_factorial(row1) + log_factorial(row2)
            + log_factorial(col1) + log_factorial(col2)
            - log_factorial(n)
            - log_factorial(k) - log_factorial(row1 - k)
            - log_factorial(col1 - k) - log_factorial(row2 - col1 + k)
        )

    observed_lp = hypergeom_logpmf(a)
    p = 0.0
    for k in range(0, min(row1, col1) + 1):
        lp = hypergeom_logpmf(k)
        if lp <= observed_lp + 1e-9:
            p += math.exp(lp)
    return min(p, 1.0)


def main() -> int:
    for c in (CALOR_EXE.exists(), FIXTURE.exists(), shutil.which('claude')):
        if not c:
            print('missing prereq', file=sys.stderr)
            return 1

    openers = load_openers()
    trials: list[Trial] = []
    total_cost = 0.0
    n_cells = len(TASKS) * 2

    print(f'Phase 3 H1 REPLICATION (v2)')
    print(f'  Tasks: {len(TASKS)} ({len([t for t in TASKS if t["kind"] == "control"])} control, {len([t for t in TASKS if t["kind"] == "contract"])} contract)')
    print(f'  Arms: 2  Trials/cell: {TRIALS_PER_CELL}  Total: {n_cells * TRIALS_PER_CELL} invocations')
    print(f'  Budget cap: ${BUDGET_USD:.2f}  Model: {MODEL}\n')

    for task in TASKS:
        for arm in ('closer', 'indent'):
            for ti in range(TRIALS_PER_CELL):
                if total_cost > BUDGET_USD:
                    print(f'\nBUDGET EXCEEDED (${total_cost:.4f} > ${BUDGET_USD:.2f}), stopping')
                    break
                print(f'  {task["id"]:14s}  {arm:6s}  {ti + 1}/{TRIALS_PER_CELL} ...', end=' ', flush=True)
                tr = run_trial(task, arm, ti, openers)
                trials.append(tr)
                total_cost += tr.cost_usd
                if tr.error:
                    tag = f'ERR ({tr.error[:50]})'
                elif tr.compile_ok and tr.must_contain_ok:
                    tag = 'PASS'
                elif tr.compile_ok:
                    tag = 'MISSING'
                else:
                    tag = 'FAIL'
                print(f'{tag}  ${tr.cost_usd:.4f}  {tr.duration_ms / 1000:.1f}s')

    # Summary
    print('\n--- AGGREGATE ---')
    by_arm = {'closer': [t for t in trials if t.arm == 'closer'],
              'indent': [t for t in trials if t.arm == 'indent']}
    closer_pass = sum(1 for t in by_arm['closer'] if t.compile_ok and t.must_contain_ok)
    closer_n = len(by_arm['closer'])
    indent_pass = sum(1 for t in by_arm['indent'] if t.compile_ok and t.must_contain_ok)
    indent_n = len(by_arm['indent'])
    closer_cost = sum(t.cost_usd for t in by_arm['closer'])
    indent_cost = sum(t.cost_usd for t in by_arm['indent'])

    print(f'  closer  : {closer_pass}/{closer_n}  ({closer_pass / closer_n * 100:.1f}%)  ${closer_cost:.3f}')
    print(f'  indent  : {indent_pass}/{indent_n}  ({indent_pass / indent_n * 100:.1f}%)  ${indent_cost:.3f}')
    pp = indent_pass / indent_n * 100 - closer_pass / closer_n * 100 if (indent_n and closer_n) else 0
    print(f'  Δ pass rate: {pp:+.1f}pp')
    if closer_cost > 0:
        print(f'  Δ cost:     {(indent_cost - closer_cost) / closer_cost * 100:+.1f}%')
    p = fisher_exact_2x2(closer_pass, closer_n - closer_pass, indent_pass, indent_n - indent_pass)
    print(f'  Fisher exact p-value (two-sided): {p:.4f}  {"(significant at 0.05)" if p < 0.05 else "(NOT significant at 0.05)"}')

    print('\n--- PER-TASK ---')
    for task in TASKS:
        c = [t for t in trials if t.task_id == task['id'] and t.arm == 'closer']
        i = [t for t in trials if t.task_id == task['id'] and t.arm == 'indent']
        cp = sum(1 for x in c if x.compile_ok and x.must_contain_ok)
        ip = sum(1 for x in i if x.compile_ok and x.must_contain_ok)
        print(f'  {task["id"]:14s} ({task["kind"]:8s})  closer {cp}/{len(c)}   indent {ip}/{len(i)}')

    out_json = REPO / 'scripts' / 'phase3-h1-replication-results.json'
    out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')
    print(f'\nRaw: {out_json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
