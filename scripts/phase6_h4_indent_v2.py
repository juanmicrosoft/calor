"""Phase 6 — H4 indent re-run, redesigned corruption set.

The original H4 indent corruptions were designed for a strict Python-style
indent parser. The Phase 4d Calor parser is more permissive (it normalises
the base indent level, accepts variable indents within a block, etc.) so most
of those corruptions don't actually trigger compile errors.

This redesigned set produces corruptions that DEFINITELY fail under the
Phase 4d strict compiler, so we can measure honest recovery rate.

Tests across three models per user request:
  - claude-haiku-4-5  (matches the closer baseline model)
  - gpt-5.3-codex     (Codex variant via GitHub Copilot CLI)
  - gpt-5-mini        (general GPT via GitHub Copilot CLI)

6 corruptions × 3 trials × 3 models = 54 invocations.
Budget cap: $3.00 total across all models.
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / 'scripts'))

from multi_model_runner import run_model  # noqa: E402

CALOR_EXE = REPO / 'src/Calor.Compiler/bin/Debug/net10.0/calor.exe'
FIXTURE = REPO / 'scripts/h2_edit_fixture.calr'  # already in indent form post Phase 4

MODELS = [
    'claude-haiku-4-5',
    'gpt-5.3-codex',
    'gpt-5-mini',
]
TRIALS_PER_CELL = 3
BUDGET_USD = 3.0


# ---------------------------------------------------------------------------
# Redesigned corruption recipes — each MUST trigger a compile error.
# ---------------------------------------------------------------------------
def corruption_mixed_tabs(src: str) -> str:
    """Replace 2 spaces with a tab on one body line. → Calor0099."""
    return src.replace('  §R (* x y)', '\t§R (* x y)', 1)


def corruption_legacy_closer(src: str) -> str:
    """Inject a legacy §/F closer. → Calor0830 LegacyCloserForm under Phase 4d."""
    return src.replace('  §F{f002:Subtract:pub}', '§/F{f001}\n  §F{f002:Subtract:pub}', 1)


def corruption_wrong_section(src: str) -> str:
    """Typo: §FF instead of §F. → Calor0001-ish unknown marker."""
    return src.replace('§F{f003:Multiply:pub}', '§FF{f003:Multiply:pub}', 1)


def corruption_unindented_body(src: str) -> str:
    """Remove body indent so §R is at column 0, sibling to §M not child of §F."""
    return src.replace('    §R (- a b)', '§R (- a b)', 1)


def corruption_orphan_EI(src: str) -> str:
    """Insert an §EI at function-body level (not chained to any §IF)."""
    return src.replace('  §F{f005:Sign:pub} (i32:n) -> i32\n    §IF{i01} (> n 0)',
                       '  §F{f005:Sign:pub} (i32:n) -> i32\n    §EI (= n 5)\n      §R 5\n    §IF{i01} (> n 0)', 1)


def corruption_bad_expression(src: str) -> str:
    """Mangled Lisp expression — unbalanced parens."""
    return src.replace('§R (+ a b)', '§R (+ a b', 1)


CORRUPTIONS = [
    ('mixed_tabs',         corruption_mixed_tabs),
    ('legacy_closer',      corruption_legacy_closer),
    ('wrong_section',      corruption_wrong_section),
    ('unindented_body',    corruption_unindented_body),
    ('orphan_EI',          corruption_orphan_EI),
    ('bad_expression',     corruption_bad_expression),
]


SYSTEM_INDENT = """You are a Calor language expert. Calor is a DSL with section markers like §F (function), §M (module), §B (binding), §IF (conditional), §L (loop).

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

BLOCK STRUCTURE (indent form, Python-style):
  Block bodies are indented under their opener with 2-space increments.
  Dedent ends the block — NO §/F, §/M, §/I (closer-form was removed).
    §F{f001:Foo:pub}
      §I{i32:x}
      §R x
  Chain continuations (§EI, §EL) sit at the SAME indent as their parent §IF.

You will be given a Calor file that has a compilation error, plus the compiler's error message. Return the FIXED file content inside a single ```calor code block. No explanation."""


@dataclass
class Trial:
    corruption: str
    arm: str
    trial: int
    duration_ms: int = 0
    cost_usd: float = 0.0
    raw_response: str = ''
    extracted: str = ''
    original_error: str = ''
    fix_compiles: bool = False
    fix_error: str = ''
    error: str = ''


def extract_code_block(text: str) -> str:
    import re
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


def run_trial(cname: str, cfn, trial_idx: int, model: str) -> Trial:
    t = Trial(corruption=cname, arm=f'indent[{model}]', trial=trial_idx)
    src = FIXTURE.read_text(encoding='utf-8')
    corrupted = cfn(src)

    with tempfile.TemporaryDirectory(prefix='p6-h4-cap-') as td:
        ok0, err0 = compile_calor(corrupted, Path(td))
    t.original_error = err0
    if ok0:
        t.error = 'no_compile_error'
        return t

    user_prompt = (
        "This Calor file fails to compile:\n\n"
        f"```calor\n{corrupted}```\n\n"
        f"COMPILER ERROR:\n{err0}\n\n"
        "Fix the file and return the complete corrected content in a single ```calor code block."
    )

    res = run_model(model, SYSTEM_INDENT, user_prompt)
    t.duration_ms = res.get('_duration_ms', 0)
    if '_error' in res:
        t.error = res['_error']
        return t
    t.cost_usd = float(res.get('cost_proxy_usd', 0.0))
    t.raw_response = res.get('result', '')
    t.extracted = extract_code_block(t.raw_response)

    with tempfile.TemporaryDirectory(prefix='p6-h4-fix-') as td:
        ok, err = compile_calor(t.extracted, Path(td))
    t.fix_compiles = ok
    t.fix_error = err
    return t


def main() -> int:
    if not CALOR_EXE.exists():
        print(f'missing: {CALOR_EXE}', file=sys.stderr); return 1
    if not FIXTURE.exists():
        print(f'missing: {FIXTURE}', file=sys.stderr); return 1
    for cli in ('claude', 'copilot'):
        if not shutil.which(cli):
            print(f'missing CLI: {cli}', file=sys.stderr); return 1

    # Validate every corruption actually breaks compile (with NO LLM call).
    print('Pre-flight: verifying corruptions actually break compile...')
    src = FIXTURE.read_text(encoding='utf-8')
    for cname, cfn in CORRUPTIONS:
        corrupted = cfn(src)
        with tempfile.TemporaryDirectory(prefix='p6-h4-pre-') as td:
            ok, err = compile_calor(corrupted, Path(td))
        if ok:
            print(f'  [BAD] {cname}: did NOT break compile - skipping from study')
        else:
            head = err.split('\n')[0][:120]
            print(f'  [OK] {cname}: breaks compile  ({head})')

    trials: list[Trial] = []
    total_cost = 0.0
    t_start = time.time()

    n_total = len(CORRUPTIONS) * TRIALS_PER_CELL * len(MODELS)
    print(f'\nPhase 6 H4 — INDENT-ARM (Phase 4d strict)')
    print(f'  Corruptions: {len(CORRUPTIONS)}  Trials/cell: {TRIALS_PER_CELL}  Models: {len(MODELS)}')
    print(f'  Total invocations: {n_total}  Budget: ${BUDGET_USD:.2f}\n')

    out_json = REPO / 'scripts' / 'phase6-h4-indent-rerun-results.json'

    for model in MODELS:
        print(f'\n=== MODEL: {model} ===')
        for cname, cfn in CORRUPTIONS:
            for ti in range(TRIALS_PER_CELL):
                if total_cost > BUDGET_USD:
                    print(f'\nBUDGET EXCEEDED (${total_cost:.4f}), stopping')
                    break
                print(f'  {cname:20s}  {ti + 1}/{TRIALS_PER_CELL} ...', end=' ', flush=True)
                try:
                    tr = run_trial(cname, cfn, ti, model)
                except Exception as exc:  # noqa: BLE001
                    tr = Trial(corruption=cname, arm=f'indent[{model}]', trial=ti,
                               error=f'exception: {exc!r}')
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
                out_json.write_text(json.dumps([t.__dict__ for t in trials], indent=2),
                                    encoding='utf-8')

    print('\n--- AGGREGATE (per model) ---')
    for model in MODELS:
        arm_tag = f'indent[{model}]'
        cells = [t for t in trials if t.arm == arm_tag and t.error != 'no_compile_error']
        fixed = sum(1 for t in cells if t.fix_compiles)
        cost = sum(t.cost_usd for t in cells)
        avg_cost = cost / max(len(cells), 1)
        avg_dur = sum(t.duration_ms for t in cells) / max(len(cells), 1) / 1000
        print(f'  {model:22s}  {fixed}/{len(cells)} '
              f'({fixed/max(len(cells),1)*100:.0f}%)  '
              f'avg ${avg_cost:.4f}  {avg_dur:.1f}s/trial  total ${cost:.3f}')

    print('\n--- BASELINE (historical closer arm) ---')
    print('  claude-haiku-4-5:  18/18 (100%)  avg $0.0113/trial')

    print(f'\nTotal cost: ${total_cost:.3f}  wall: {(time.time() - t_start)/60:.1f}m')
    print(f'Raw: {out_json}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
