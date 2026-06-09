#!/usr/bin/env python3
"""Phase 3 H3 — DEEPLY-NESTED edit study.

The H2 fixture had 25 lines with a single 2-deep §IF chain. Indent forms
historically degrade as nesting depth grows because each level adds 2 spaces
of dedent-counting work for the agent.

H3 uses h2_deep_fixture.calr — 79 lines, 5 functions, includes:
  - 3-deep §IF nesting (Classify function)
  - 2-deep loop nesting (NestedLoop function) 
  - Loop + conditional inside (CountPositive, NestedLoop)
  - 3-way §IF/§EI/§EI/§EI/§EL chain (Classify)

Tasks specifically target the deep structures to force the agent to navigate
nested indent levels (or matching nested closers).

H0: deep-nesting pass rate is the same for indent and closer.
H1: indent has materially lower pass rate (>10pp) due to dedent counting errors.

N=3/cell × 6 tasks × 2 arms = 36 trials. Claude haiku only (cheap baseline).
If indent fails here, cross-model H3 + larger fixture is the immediate followup.
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
from multi_model_runner import run_model  # type: ignore

REPO = Path(__file__).resolve().parent.parent
CALOR_EXE = REPO / 'src/Calor.Compiler/bin/Debug/net10.0/calor.exe'
FIXTURE = REPO / 'scripts/h2_deep_fixture.calr'
MODEL = 'claude-haiku-4-5'
BUDGET_USD = 3.0
TRIALS_PER_CELL = 3

TASKS = [
    # 1. Edit at the DEEPEST point — innermost branch of 3-level §IF
    {'id': 'deep-innermost-001', 'kind': 'deep-if',
     'prompt':
        "In the Classify function, modify the §IF{i04} branch so it returns 6 instead "
        "of 5 when score == 100. Do not change anything else.",
     'must_contain': ['§F{f003:Classify:pub}', '§IF{i04}', '§R 6'],
     'must_not_contain': ['§R 5']},

    # 2. Add a new branch in the middle of an existing chain
    {'id': 'deep-chain-extend-001', 'kind': 'deep-if',
     'prompt':
        "In the Classify function, add a new §EI branch between the (== score 0) "
        "branch and the (< score 50) branch that catches (< score 10) and returns 0. "
        "Keep all other branches unchanged.",
     'must_contain': ['§F{f003:Classify:pub}', '(< score 10)'],
     'must_not_contain': []},

    # 3. Edit inside nested loops — change inner loop's IF return
    {'id': 'deep-loop-001', 'kind': 'deep-loop',
     'prompt':
        "In the NestedLoop function, modify the §IF{i06} so the §EL branch decrements "
        "acc by 2 instead of 1. Use (- acc 2) instead of (- acc 1). Keep everything "
        "else unchanged.",
     'must_contain': ['§F{f005:NestedLoop:pub}', '§IF{i06}', '(- acc 2)'],
     'must_not_contain': []},

    # 4. Add a precondition to a deeply nested function (preserving deep body)
    {'id': 'deep-pre-001', 'kind': 'deep-contract',
     'prompt':
        "Add a §Q precondition to the NestedLoop function ensuring n >= 0. "
        "Do not change the function body. Do not change any other function.",
     'must_contain': ['§F{f005:NestedLoop:pub}', '§Q', '>='],
     'must_not_contain': []},

    # 5. Add a NEW branch into a single-level IF inside a loop
    {'id': 'deep-loop-cond-001', 'kind': 'deep-cond',
     'prompt':
        "In the CountPositive function, replace the §IF{i05} so it counts only when "
        "i is BOTH positive AND even. Use (&& (> i 0) (== (% i 2) 0)) as the condition. "
        "Keep everything else unchanged.",
     'must_contain': ['§F{f004:CountPositive:pub}', '§IF{i05}',
                      '(&& (> i 0) (== (% i 2) 0))'],
     'must_not_contain': []},

    # 6. Delete a function with deep nesting — must preserve all others
    {'id': 'deep-del-001', 'kind': 'deep-delete',
     'prompt':
        "Delete the Classify function entirely. Keep all other functions "
        "(Sum, Max, CountPositive, NestedLoop) exactly as they are.",
     'must_contain': ['§F{f001:Sum:pub}', '§F{f002:Max:pub}',
                      '§F{f004:CountPositive:pub}', '§F{f005:NestedLoop:pub}'],
     'must_not_contain': ['Classify']},
]

SHARED_HEADER = """You are a Calor language expert. Calor is a DSL with section markers like §F (function), §M (module), §B (binding), §IF (conditional), §L (loop).

Function header: §F{id:Name:visibility} — example: §F{f001:Add:pub}
Inputs/Outputs:  §I{type:name}  §O{type}  — e.g. §I{i32:x}  §O{bool}
Bindings:        §B{name} value — e.g. §B{x} 5
Loops:           §L{id:var:from:to:step}
Print:           §P "literal"  or  §P variable
Return:          §R expression  — e.g. §R (+ a b)

CONTRACT SYNTAX (CRITICAL):
  §Q is a precondition. It takes a Lisp-style expression in parentheses.
  §S is a postcondition. Same form.
  CORRECT:    §Q (>= x 0)   §S (== result a)   §S (&& (>= result a) (>= result b))
  WRONG:      §Q{x >= 0}    §S{result >= 0}    §S (= result ...)
  The condition is NEVER inside {...} braces — only function/binding NAMES go in braces.

CHAIN STATEMENTS — IF/ELSE-IF/ELSE (CRITICAL):
  §IF ALWAYS requires an {id} attribute. The condition follows in parentheses.
  §EI (else-if) and §EL (else) are continuation keywords — they do NOT take {id}.
  The "else" keyword in Calor is §EL — there is no §K or §ELSE.

  CORRECT:
    §IF{i01} (< x 0)
      §R (- 0 x)
    §EL
      §R x

  WRONG:
    §IF (< x 0)          ← missing {id}
    §K                    ← no such keyword; use §EL
    §ELSE                 ← no such keyword; use §EL
    §IF{i01}{x < 0}       ← condition goes in (parens), not braces

EXPRESSIONS use Lisp prefix form:
  arithmetic:  (+ a b)  (- a b)  (* a b)  (/ a b)  (% a b)
  comparison:  (== x y)  (!= x y)  (< x y)  (<= x y)  (> x y)  (>= x y)
  logical:     (&& a b)  (|| a b)  (! a)
"""

DELIM_CLOSER = """BLOCK STRUCTURE (closer form):
  Every block opener has a matching explicit closer with the SAME id:
    §F{f001:Foo:pub}
      §IF{i01} (>= x 0)
        §IF{i02} (> x 10)
          §R 100
        §EL
          §R x
        §/I{i02}
      §EL
        §R 0
      §/I{i01}
    §/F{f001}
  Functions close with §/F{id}, modules with §/M{id}, IF blocks with §/I{id},
  loops with §/L{id}. Indentation is purely cosmetic; the closer is what ends
  the block.
"""

DELIM_INDENT = """BLOCK STRUCTURE (indent form, Python-style):
  Block bodies are indented under their opener with 2-space increments.
  Dedent ends the block — NO §/F, §/M, §/I, §/L, etc.
    §F{f001:Foo:pub}
      §IF{i01} (>= x 0)
        §IF{i02} (> x 10)
          §R 100
        §EL
          §R x
      §EL
        §R 0
  Chain continuations (§EI, §EL) sit at the SAME indent as their parent §IF.
  Note that §IF still requires {id} in indent form — the id rule is universal.
  Deep nesting just means more indent — count spaces carefully.
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
    must_not_contain_ok: bool = False
    error: str = ''


def extract_code_block(text: str) -> str:
    m = re.search(r'```(?:calor)?\s*\n(.*?)```', text, re.DOTALL)
    if m:
        return m.group(1).strip('\n')
    return text.strip()


def compile_calor(src: str, tmpdir: Path) -> tuple[bool, str]:
    src_f = tmpdir / 'Deep.calr'
    out_f = tmpdir / 'Deep.g.cs'
    src_f.write_text(src, encoding='utf-8')
    try:
        r = subprocess.run([str(CALOR_EXE), '--input', str(src_f), '-o', str(out_f)],
                           capture_output=True, text=True, timeout=60, encoding='utf-8')
    except subprocess.TimeoutExpired:
        return False, 'compile timeout'
    if r.returncode == 0:
        return True, ''
    return False, (r.stdout + r.stderr)[:600]


def run_trial(task: dict, arm: str, trial_idx: int, openers: dict) -> Trial:
    t = Trial(task_id=task['id'], task_kind=task['kind'], arm=arm, trial=trial_idx)
    src = FIXTURE.read_text(encoding='utf-8')

    if arm == 'closer':
        body, system = src, SYSTEM_CLOSER
    else:
        body, system = to_indent(src, openers), SYSTEM_INDENT

    user_prompt = (
        f"Here is the current Calor file:\n\n"
        f"```calor\n{body}```\n\n"
        f"TASK: {task['prompt']}\n\n"
        f"Return the complete updated file in a single ```calor code block."
    )

    res = run_model(MODEL, system, user_prompt)
    t.duration_ms = res.get('_duration_ms', 0)
    if '_error' in res:
        t.error = res['_error']
        return t
    t.cost_usd = float(res.get('cost_proxy_usd', 0.0))
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

    with tempfile.TemporaryDirectory(prefix='calor-h3-') as td:
        ok, err = compile_calor(to_compile, Path(td))
    t.compile_ok = ok
    t.compile_stderr = err
    t.must_contain_ok = all(s in to_compile for s in task['must_contain'])
    t.must_not_contain_ok = all(s not in to_compile for s in task.get('must_not_contain', []))
    return t


def fisher_exact_2x2(a: int, b: int, c: int, d: int) -> float:
    n = a + b + c + d
    if n == 0:
        return 1.0
    row1, row2 = a + b, c + d
    col1, col2 = a + c, b + d

    def log_factorial(x: int) -> float:
        return math.lgamma(x + 1)

    def hypergeom_logpmf(k: int) -> float:
        if k < 0 or k > col1 or (row1 - k) < 0 or (row1 - k) > col2:
            return -math.inf
        return (log_factorial(row1) + log_factorial(row2)
                + log_factorial(col1) + log_factorial(col2)
                - log_factorial(n)
                - log_factorial(k) - log_factorial(row1 - k)
                - log_factorial(col1 - k) - log_factorial(row2 - col1 + k))

    observed_lp = hypergeom_logpmf(a)
    p = 0.0
    for k in range(0, min(row1, col1) + 1):
        lp = hypergeom_logpmf(k)
        if lp <= observed_lp + 1e-9:
            p += math.exp(lp)
    return min(p, 1.0)


def is_pass(t: Trial) -> bool:
    return t.compile_ok and t.must_contain_ok and t.must_not_contain_ok


def main() -> int:
    for c in (CALOR_EXE.exists(), FIXTURE.exists(), shutil.which('claude')):
        if not c:
            print('missing prereq', file=sys.stderr)
            return 1

    openers = load_openers()
    trials: list[Trial] = []
    total_cost = 0.0
    n_cells = len(TASKS) * 2

    print('Phase 3 H3 — DEEP-NESTING edit study')
    print(f'  Fixture: {FIXTURE.name} (79 lines, 3-deep IF, 2-deep loop)')
    print(f'  Tasks: {len(TASKS)}  Arms: 2  Trials/cell: {TRIALS_PER_CELL}  Total: {n_cells * TRIALS_PER_CELL} invocations')
    print(f'  Budget cap: ${BUDGET_USD:.2f}  Model: {MODEL}\n')

    for task in TASKS:
        for arm in ('closer', 'indent'):
            for ti in range(TRIALS_PER_CELL):
                if total_cost > BUDGET_USD:
                    print(f'\nBUDGET EXCEEDED (${total_cost:.4f}), stopping')
                    break
                print(f'  {task["id"]:24s}  {arm:6s}  {ti + 1}/{TRIALS_PER_CELL} ...', end=' ', flush=True)
                tr = run_trial(task, arm, ti, openers)
                trials.append(tr)
                total_cost += tr.cost_usd
                if tr.error:
                    tag = f'ERR ({tr.error[:50]})'
                elif is_pass(tr):
                    tag = 'PASS'
                elif tr.compile_ok:
                    tag = 'MISMATCH'
                else:
                    tag = 'FAIL'
                print(f'{tag}  ${tr.cost_usd:.4f}  {tr.duration_ms / 1000:.1f}s')
        # incremental save
        out_json = REPO / 'scripts' / 'phase3-h3-deep-results.json'
        out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')

    print('\n--- AGGREGATE ---')
    by_arm = {'closer': [t for t in trials if t.arm == 'closer'],
              'indent': [t for t in trials if t.arm == 'indent']}
    cp = sum(1 for t in by_arm['closer'] if is_pass(t))
    cn = len(by_arm['closer'])
    ip = sum(1 for t in by_arm['indent'] if is_pass(t))
    iN = len(by_arm['indent'])
    cc = sum(t.cost_usd for t in by_arm['closer'])
    ic = sum(t.cost_usd for t in by_arm['indent'])
    print(f'  closer  : {cp}/{cn}  ({cp / cn * 100:.1f}%)  ${cc:.3f}')
    print(f'  indent  : {ip}/{iN}  ({ip / iN * 100:.1f}%)  ${ic:.3f}')
    pp = ip / iN * 100 - cp / cn * 100 if (iN and cn) else 0
    print(f'  Δ pass rate: {pp:+.1f}pp')
    if cc > 0:
        print(f'  Δ cost:     {(ic - cc) / cc * 100:+.1f}%')
    p = fisher_exact_2x2(cp, cn - cp, ip, iN - ip)
    print(f'  Fisher exact p (two-sided): {p:.4f}  {"(significant)" if p < 0.05 else "(NOT significant)"}')

    print('\n--- PER-TASK ---')
    for task in TASKS:
        c = [t for t in trials if t.task_id == task['id'] and t.arm == 'closer']
        i = [t for t in trials if t.task_id == task['id'] and t.arm == 'indent']
        cpx = sum(1 for x in c if is_pass(x))
        ipx = sum(1 for x in i if is_pass(x))
        print(f'  {task["id"]:24s} ({task["kind"]:13s})  closer {cpx}/{len(c)}   indent {ipx}/{len(i)}')

    out_json = REPO / 'scripts' / 'phase3-h3-deep-results.json'
    out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')
    print(f'\nRaw: {out_json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
