#!/usr/bin/env python3
"""Phase 3 H2 — Cross-Model EDIT-WORKLOAD study.

Replicates the H2 single-model study (Claude haiku) across multiple models
to test cross-model robustness of the indent-form decision.

Models tested:
- claude-haiku-4-5  (baseline from initial H2)
- gpt-5.2            (general GPT)
- gpt-5.3-codex      (code-specialized GPT)

For each (model, task, arm) cell we run N=3 trials. With 3 models × 7 tasks ×
2 arms × 3 trials = 126 trials, this is a focused cross-model replication
rather than a deep dive. Goal: confirm tie persists, OR identify a model
that materially diverges.
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
FIXTURE = REPO / 'scripts/h2_edit_fixture.calr'
BUDGET_USD = 8.0
TRIALS_PER_CELL = 3

MODELS = ['claude-haiku-4-5', 'gpt-5.2', 'gpt-5.3-codex']

# Reuse H2 task set verbatim
TASKS = [
    {'id': 'edit-sig-001', 'kind': 'signature',
     'prompt':
        "Modify the Add function so it takes THREE i32 parameters (a, b, c) instead "
        "of two, and returns the sum of all three. Leave every other function "
        "exactly as it is.",
     'must_contain': ['§F{f001:Add:pub}', '§I{i32:c}'],
     'must_not_contain': []},
    {'id': 'edit-body-001', 'kind': 'body',
     'prompt':
        "Modify the Subtract function so its body returns (a - b) but only when "
        "a >= b; otherwise it returns 0. Use a §IF chain. Add the id {i02} to the "
        "new §IF. Leave every other function exactly as it is.",
     'must_contain': ['§F{f002:Subtract:pub}', '§IF{i02}'],
     'must_not_contain': []},
    {'id': 'edit-pre-001', 'kind': 'contract',
     'prompt':
        "Add a §Q precondition to the Divide function ensuring b is not zero. "
        "Leave every other function exactly as it is, and do not change Divide's "
        "body or signature.",
     'must_contain': ['§F{f004:Divide:pub}', '§Q', '!='],
     'must_not_contain': []},
    {'id': 'edit-post-001', 'kind': 'contract',
     'prompt':
        "Add a §S postcondition to the Multiply function ensuring the result equals "
        "(x * y). Leave every other function exactly as it is, and do not change "
        "Multiply's body or signature.",
     'must_contain': ['§F{f003:Multiply:pub}', '§S'],
     'must_not_contain': []},
    {'id': 'edit-type-001', 'kind': 'type',
     'prompt':
        "Change the Add function so it takes and returns i64 instead of i32. "
        "Update both parameter types and the return type. Leave every other "
        "function exactly as it is.",
     'must_contain': ['§F{f001:Add:pub}', '§I{i64:a}', '§I{i64:b}', '§O{i64}'],
     'must_not_contain': []},
    {'id': 'edit-chain-001', 'kind': 'chain',
     'prompt':
        "Modify the Sign function so the §EI branch (when n < 0) returns -2 instead "
        "of -1. Do not change any other branch. Leave every other function exactly "
        "as it is.",
     'must_contain': ['§F{f005:Sign:pub}', '§IF{i01}', '§EI', '§EL'],
     'must_not_contain': ['(- 0 1)']},
    {'id': 'edit-del-001', 'kind': 'delete',
     'prompt':
        "Delete the Multiply function entirely. Leave every other function exactly "
        "as it is, including Add, Subtract, Divide, and Sign.",
     'must_contain': ['§F{f001:Add:pub}', '§F{f002:Subtract:pub}',
                      '§F{f004:Divide:pub}', '§F{f005:Sign:pub}'],
     'must_not_contain': ['Multiply']},
]

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

CHAIN STATEMENTS — IF/ELSE-IF/ELSE (CRITICAL):
  §IF ALWAYS requires an {id} attribute. The condition follows in parentheses.
  §EI (else-if) and §EL (else) are continuation keywords — they do NOT take {id}.
  The "else" keyword in Calor is §EL — there is no §K or §ELSE.

  CORRECT:
    §IF{i01} (< x 0)
      §R (- 0 x)
    §EL
      §R x

    §IF{i02} (< value min)
      §R min
    §EI (> value max)
      §R max
    §EL
      §R value

  WRONG:
    §IF (< x 0)          ← missing {id}
    §K                    ← no such keyword; use §EL
    §ELSE                 ← no such keyword; use §EL
    §IF{i01}{x < 0}       ← condition goes in (parens), not braces

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
      §IF{i01} (>= x 0)
        §R x
      §EL
        §R 0
      §/I{i01}
    §/F{f001}
  Functions close with §/F{id}, modules with §/M{id}, IF blocks with §/I{id}.
  Indentation is purely cosmetic; the closer is what ends the block.
"""

DELIM_INDENT = """BLOCK STRUCTURE (indent form, Python-style):
  Block bodies are indented under their opener with 2-space increments.
  Dedent ends the block — NO §/F, §/M, §/I, etc.
    §F{f001:Foo:pub}
      §I{i32:x}
      §IF{i01} (>= x 0)
        §R x
      §EL
        §R 0
  Chain continuations (§EI, §EL) sit at the SAME indent as their parent §IF.
  Note that §IF still requires {id} in indent form — the id rule is universal.
"""

SHARED_FOOTER = """
When asked to modify a Calor file, return ONLY the complete updated file content inside a single ```calor code block. No explanation, no commentary, no extra text before or after the code block."""

SYSTEM_CLOSER = SHARED_HEADER + DELIM_CLOSER + SHARED_FOOTER
SYSTEM_INDENT = SHARED_HEADER + DELIM_INDENT + SHARED_FOOTER


@dataclass
class Trial:
    model: str
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
    src_f = tmpdir / 'EditTarget.calr'
    out_f = tmpdir / 'EditTarget.g.cs'
    src_f.write_text(src, encoding='utf-8')
    try:
        r = subprocess.run([str(CALOR_EXE), '--input', str(src_f), '-o', str(out_f)],
                           capture_output=True, text=True, timeout=60, encoding='utf-8')
    except subprocess.TimeoutExpired:
        return False, 'compile timeout'
    if r.returncode == 0:
        return True, ''
    return False, (r.stdout + r.stderr)[:600]


def run_trial(model: str, task: dict, arm: str, trial_idx: int, openers: dict) -> Trial:
    t = Trial(model=model, task_id=task['id'], task_kind=task['kind'],
              arm=arm, trial=trial_idx)
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

    res = run_model(model, system, user_prompt)
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

    with tempfile.TemporaryDirectory(prefix='calor-h2-xm-') as td:
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
    for c in (CALOR_EXE.exists(), FIXTURE.exists(), shutil.which('claude'),
              shutil.which('copilot')):
        if not c:
            print('missing prereq', file=sys.stderr)
            return 1

    openers = load_openers()
    trials: list[Trial] = []
    total_cost = 0.0
    n_cells = len(MODELS) * len(TASKS) * 2

    print('Phase 3 H2 CROSS-MODEL — edit-workload replication across 3 models')
    print(f'  Models: {MODELS}')
    print(f'  Tasks:  {len(TASKS)}  Arms: 2  Trials/cell: {TRIALS_PER_CELL}')
    print(f'  Total invocations: {n_cells * TRIALS_PER_CELL}')
    print(f'  Budget cap: ${BUDGET_USD:.2f}\n')

    for model in MODELS:
        print(f'\n=== MODEL: {model} ===')
        for task in TASKS:
            for arm in ('closer', 'indent'):
                for ti in range(TRIALS_PER_CELL):
                    if total_cost > BUDGET_USD:
                        print(f'\nBUDGET EXCEEDED (${total_cost:.4f}), stopping')
                        break
                    print(f'  {task["id"]:16s}  {arm:6s}  {ti + 1}/{TRIALS_PER_CELL} ...', end=' ', flush=True)
                    tr = run_trial(model, task, arm, ti, openers)
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
                # save incrementally so partial runs are recoverable
                out_json = REPO / 'scripts' / 'phase3-h2-crossmodel-results.json'
                out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2),
                                    encoding='utf-8')

    print('\n--- AGGREGATE BY MODEL ---')
    for model in MODELS:
        by_arm = {arm: [t for t in trials if t.model == model and t.arm == arm]
                  for arm in ('closer', 'indent')}
        cp = sum(1 for t in by_arm['closer'] if is_pass(t))
        cn = len(by_arm['closer'])
        ip = sum(1 for t in by_arm['indent'] if is_pass(t))
        iN = len(by_arm['indent'])
        cc = sum(t.cost_usd for t in by_arm['closer'])
        ic = sum(t.cost_usd for t in by_arm['indent'])
        pp = (ip / iN - cp / cn) * 100 if (iN and cn) else 0
        p = fisher_exact_2x2(cp, cn - cp, ip, iN - ip)
        cost_delta = (ic - cc) / cc * 100 if cc > 0 else 0
        print(f'  {model:20s}  closer {cp}/{cn} ({cp/cn*100:.0f}%)  '
              f'indent {ip}/{iN} ({ip/iN*100:.0f}%)  '
              f'Δ={pp:+.1f}pp  Δ$={cost_delta:+.1f}%  p={p:.3f}')

    print('\n--- AGGREGATE ACROSS MODELS ---')
    all_closer = [t for t in trials if t.arm == 'closer']
    all_indent = [t for t in trials if t.arm == 'indent']
    cp = sum(1 for t in all_closer if is_pass(t))
    cn = len(all_closer)
    ip = sum(1 for t in all_indent if is_pass(t))
    iN = len(all_indent)
    cc = sum(t.cost_usd for t in all_closer)
    ic = sum(t.cost_usd for t in all_indent)
    pp = (ip / iN - cp / cn) * 100 if (iN and cn) else 0
    p = fisher_exact_2x2(cp, cn - cp, ip, iN - ip)
    print(f'  closer: {cp}/{cn}  ({cp/cn*100:.1f}%)  ${cc:.3f}')
    print(f'  indent: {ip}/{iN}  ({ip/iN*100:.1f}%)  ${ic:.3f}')
    print(f'  Δ pass: {pp:+.1f}pp   Fisher p={p:.4f}')

    print(f'\nTotal cost: ${total_cost:.3f}')
    out_json = REPO / 'scripts' / 'phase3-h2-crossmodel-results.json'
    out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')
    print(f'Raw: {out_json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
