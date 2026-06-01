#!/usr/bin/env python3
"""Phase 3 H4 — ERROR-RECOVERY study.

When an agent makes a structural mistake in Calor (missing closer in closer
form, wrong indent level in indent form), can it SELF-RECOVER given the
compiler's error message?

This is one of the failure modes most often cited against indent-form
languages: "if the indent is wrong, the error is unhelpful and the agent
can't fix it." Test this directly.

Method:
1. Generate a corrupted file in each form (12 corruption types per form)
2. Run calor compile to capture the actual error message
3. Give the agent: corrupted file + compiler error + instruction "fix it"
4. Score: does the fixed file compile?

Corruption types (mapped per-form so each tests the same underlying issue):
  closer form                      indent form
  -----------                      -----------
  missing §/F closer               missing dedent (extra indent at end)
  missing §/I closer               §EL at wrong indent level
  wrong-id closer §/F{xxx}         off-by-one indent on a body line
  swapped closer order             mixed tabs+spaces
  missing §/M                      §IF/§EI desync indent
  extra closer §/F                 stray indent on first line

Total: 12 corruption scenarios × 2 arms × N=3 trials = 72 trials.
"""
from __future__ import annotations

import json
import math
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from calor_indent_xform import from_indent, load_openers, to_indent  # type: ignore
from multi_model_runner import run_model  # type: ignore

REPO = Path(__file__).resolve().parent.parent
CALOR_EXE = REPO / 'src/Calor.Compiler/bin/Debug/net10.0/calor.exe'
FIXTURE = REPO / 'scripts/h2_edit_fixture.calr'
MODEL = 'claude-haiku-4-5'
TRIALS_PER_CELL = 3
BUDGET_USD = 3.0

# ---------------------------------------------------------------------------
# Corruption recipes. Each yields a (corrupted_text, label) pair.
# ---------------------------------------------------------------------------
def corruption_closer_missing_F(src: str) -> str:
    # Remove the first §/F{...} closer.
    return re.sub(r'§/F\{[^}]+\}\n', '', src, count=1)

def corruption_closer_missing_I(src: str) -> str:
    # Remove the §/I{i01} closer in the Sign function.
    return src.replace('  §/I{i01}\n', '', 1)

def corruption_closer_wrong_id(src: str) -> str:
    # Change §/F{f001} to §/F{fXXX}.
    return src.replace('§/F{f001}', '§/F{fXXX}', 1)

def corruption_closer_swap(src: str) -> str:
    # Swap §/F{f001} and §/F{f002}.
    return (src.replace('§/F{f001}', '__TMP__', 1)
              .replace('§/F{f002}', '§/F{f001}', 1)
              .replace('__TMP__', '§/F{f002}', 1))

def corruption_closer_missing_M(src: str) -> str:
    return src.replace('§/M{m001}\n', '', 1)

def corruption_closer_extra_F(src: str) -> str:
    return src.replace('§/F{f001}', '§/F{f001}\n§/F{f001}', 1)

def corruption_indent_extra_indent(src_indent: str) -> str:
    # Add 2 spaces in front of the §R line of Add (overindent).
    return src_indent.replace('  §R (+ a b)', '    §R (+ a b)', 1)

def corruption_indent_EL_wrong(src_indent: str) -> str:
    # Move §EL to wrong indent (extra 2 spaces).
    return src_indent.replace('  §EL\n    §R 0', '    §EL\n      §R 0', 1)

def corruption_indent_offbyone(src_indent: str) -> str:
    # Drop one space from the §R inside Subtract body.
    return src_indent.replace('  §R (- a b)', ' §R (- a b)', 1)

def corruption_indent_mixed_tabs(src_indent: str) -> str:
    # Replace 2 spaces with a tab on one line.
    return src_indent.replace('  §R (* x y)', '\t§R (* x y)', 1)

def corruption_indent_EI_desync(src_indent: str) -> str:
    # Move the §EI down one indent level so it doesn't align with §IF.
    return src_indent.replace('  §EI (< n 0)', '    §EI (< n 0)', 1)

def corruption_indent_stray_first(src_indent: str) -> str:
    # Add 2 leading spaces to the §M line.
    return '  ' + src_indent


CLOSER_CORRUPTIONS = [
    ('missing_F_closer', corruption_closer_missing_F),
    ('missing_I_closer', corruption_closer_missing_I),
    ('wrong_id_closer', corruption_closer_wrong_id),
    ('swapped_closers', corruption_closer_swap),
    ('missing_M_closer', corruption_closer_missing_M),
    ('extra_F_closer', corruption_closer_extra_F),
]

INDENT_CORRUPTIONS = [
    ('extra_indent', corruption_indent_extra_indent),
    ('EL_wrong_indent', corruption_indent_EL_wrong),
    ('offbyone_indent', corruption_indent_offbyone),
    ('mixed_tabs_spaces', corruption_indent_mixed_tabs),
    ('EI_desync', corruption_indent_EI_desync),
    ('stray_first_line', corruption_indent_stray_first),
]

# Reuse SYSTEM prompts identical to H2 cross-model (same teaching basis)
SHARED_HEADER = """You are a Calor language expert. Calor is a DSL with section markers like §F (function), §M (module), §B (binding), §IF (conditional), §L (loop).

Function header: §F{id:Name:visibility} — example: §F{f001:Add:pub}
Inputs/Outputs:  §I{type:name}  §O{type}  — e.g. §I{i32:x}  §O{bool}
Return:          §R expression  — e.g. §R (+ a b)

CHAIN STATEMENTS — IF/ELSE-IF/ELSE (CRITICAL):
  §IF ALWAYS requires an {id} attribute. §EI/§EL are continuations.
  CORRECT:
    §IF{i01} (< x 0)
      §R (- 0 x)
    §EL
      §R x
"""

DELIM_CLOSER = """BLOCK STRUCTURE (closer form):
  Every block opener has a matching explicit closer with the SAME id:
    §F{f001:Foo:pub}
      §I{i32:x}
      §R x
    §/F{f001}
  Functions close with §/F{id}, modules with §/M{id}, IF blocks with §/I{id}.
"""

DELIM_INDENT = """BLOCK STRUCTURE (indent form, Python-style):
  Block bodies are indented under their opener with 2-space increments.
  Dedent ends the block — NO §/F, §/M, §/I.
    §F{f001:Foo:pub}
      §I{i32:x}
      §R x
  Chain continuations (§EI, §EL) sit at the SAME indent as their parent §IF.
"""

SHARED_FOOTER = """
You will be given a Calor file that has a compilation error, plus the compiler's error message. Return the FIXED file content inside a single ```calor code block. No explanation."""

SYSTEM_CLOSER = SHARED_HEADER + DELIM_CLOSER + SHARED_FOOTER
SYSTEM_INDENT = SHARED_HEADER + DELIM_INDENT + SHARED_FOOTER


@dataclass
class Trial:
    corruption: str
    arm: str
    trial: int
    duration_ms: int = 0
    cost_usd: float = 0.0
    raw_response: str = ''
    extracted: str = ''
    rt_converted: str = ''
    original_error: str = ''
    fix_compiles: bool = False
    fix_error: str = ''
    error: str = ''


def extract_code_block(text: str) -> str:
    m = re.search(r'```(?:calor)?\s*\n(.*?)```', text, re.DOTALL)
    if m:
        return m.group(1).strip('\n')
    return text.strip()


def compile_calor(src: str, tmpdir: Path) -> tuple[bool, str]:
    src_f = tmpdir / 'Fix.calr'
    out_f = tmpdir / 'Fix.g.cs'
    src_f.write_text(src, encoding='utf-8')
    try:
        r = subprocess.run([str(CALOR_EXE), '--input', str(src_f), '-o', str(out_f)],
                           capture_output=True, text=True, timeout=60, encoding='utf-8')
    except subprocess.TimeoutExpired:
        return False, 'compile timeout'
    if r.returncode == 0:
        return True, ''
    return False, (r.stdout + r.stderr)[:600]


def run_trial(corruption_name: str, corruption_fn, arm: str, trial_idx: int,
              openers: dict) -> Trial:
    t = Trial(corruption=corruption_name, arm=arm, trial=trial_idx)
    src = FIXTURE.read_text(encoding='utf-8')

    if arm == 'closer':
        body = src
        system = SYSTEM_CLOSER
    else:
        body = to_indent(src, openers)
        system = SYSTEM_INDENT

    corrupted = corruption_fn(body)

    # Capture the actual compiler error for the corrupted file.
    with tempfile.TemporaryDirectory(prefix='calor-h4-cap-') as td:
        if arm == 'indent':
            try:
                compile_input = from_indent(corrupted, openers)
            except Exception as e:  # noqa: BLE001
                compile_input = corrupted  # let the compiler complain
                _ = e
        else:
            compile_input = corrupted
        ok0, err0 = compile_calor(compile_input, Path(td))
    t.original_error = err0
    if ok0:
        # corruption didn't actually break compile — skip
        t.error = 'no_compile_error'
        return t

    user_prompt = (
        f"This Calor file fails to compile:\n\n"
        f"```calor\n{corrupted}```\n\n"
        f"COMPILER ERROR:\n{err0}\n\n"
        f"Fix the file and return the complete corrected content in a single ```calor code block."
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

    with tempfile.TemporaryDirectory(prefix='calor-h4-fix-') as td:
        ok, err = compile_calor(to_compile, Path(td))
    t.fix_compiles = ok
    t.fix_error = err
    return t


def fisher_exact_2x2(a: int, b: int, c: int, d: int) -> float:
    n = a + b + c + d
    if n == 0:
        return 1.0
    row1, row2 = a + b, c + d
    col1, col2 = a + c, b + d
    def lf(x): return math.lgamma(x + 1)
    def hp(k):
        if k < 0 or k > col1 or (row1 - k) < 0 or (row1 - k) > col2:
            return -math.inf
        return (lf(row1) + lf(row2) + lf(col1) + lf(col2) - lf(n)
                - lf(k) - lf(row1 - k) - lf(col1 - k) - lf(row2 - col1 + k))
    olp = hp(a)
    p = 0.0
    for k in range(0, min(row1, col1) + 1):
        lp = hp(k)
        if lp <= olp + 1e-9:
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

    arm_corruptions = {
        'closer': CLOSER_CORRUPTIONS,
        'indent': INDENT_CORRUPTIONS,
    }
    n_total = sum(len(v) for v in arm_corruptions.values()) * TRIALS_PER_CELL

    print('Phase 3 H4 — ERROR-RECOVERY study')
    print(f'  Corruptions per arm: closer={len(CLOSER_CORRUPTIONS)}  indent={len(INDENT_CORRUPTIONS)}')
    print(f'  Trials per cell: {TRIALS_PER_CELL}  Total: {n_total} invocations')
    print(f'  Budget cap: ${BUDGET_USD:.2f}  Model: {MODEL}\n')

    for arm, corruptions in arm_corruptions.items():
        print(f'\n=== ARM: {arm} ===')
        for cname, cfn in corruptions:
            for ti in range(TRIALS_PER_CELL):
                if total_cost > BUDGET_USD:
                    print(f'\nBUDGET EXCEEDED (${total_cost:.4f}), stopping')
                    break
                print(f'  {cname:24s}  {ti + 1}/{TRIALS_PER_CELL} ...', end=' ', flush=True)
                tr = run_trial(cname, cfn, arm, ti, openers)
                trials.append(tr)
                total_cost += tr.cost_usd
                if tr.error == 'no_compile_error':
                    tag = 'SKIP(no err)'
                elif tr.error:
                    tag = f'ERR ({tr.error[:40]})'
                elif tr.fix_compiles:
                    tag = 'FIXED'
                else:
                    tag = 'UNFIXED'
                print(f'{tag}  ${tr.cost_usd:.4f}  {tr.duration_ms / 1000:.1f}s')
        out_json = REPO / 'scripts' / 'phase3-h4-recovery-results.json'
        out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')

    print('\n--- AGGREGATE ---')
    by_arm = {arm: [t for t in trials if t.arm == arm and t.error != 'no_compile_error']
              for arm in ('closer', 'indent')}
    cp = sum(1 for t in by_arm['closer'] if t.fix_compiles)
    cn = len(by_arm['closer'])
    ip = sum(1 for t in by_arm['indent'] if t.fix_compiles)
    iN = len(by_arm['indent'])
    print(f'  closer recovery rate: {cp}/{cn}  ({cp/cn*100 if cn else 0:.1f}%)')
    print(f'  indent recovery rate: {ip}/{iN}  ({ip/iN*100 if iN else 0:.1f}%)')
    pp = (ip/iN - cp/cn) * 100 if (iN and cn) else 0
    print(f'  Δ recovery rate: {pp:+.1f}pp')
    p = fisher_exact_2x2(cp, cn-cp, ip, iN-ip)
    print(f'  Fisher exact p (two-sided): {p:.4f}  {"(significant)" if p < 0.05 else "(NOT significant)"}')

    print('\n--- PER-CORRUPTION ---')
    for arm in ('closer', 'indent'):
        for cname, _ in arm_corruptions[arm]:
            cells = [t for t in trials if t.arm == arm and t.corruption == cname]
            fixed = sum(1 for t in cells if t.fix_compiles)
            print(f'  {arm:6s}  {cname:24s}  {fixed}/{len(cells)}')

    print(f'\nTotal cost: ${total_cost:.3f}')
    out_json = REPO / 'scripts' / 'phase3-h4-recovery-results.json'
    out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2), encoding='utf-8')
    print(f'Raw: {out_json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
