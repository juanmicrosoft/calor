"""Multi-model runner for Phase 3 H1/H2 studies.

Wraps both Claude CLI and GitHub Copilot CLI behind a single `run_model(model, ...)`
interface, returning a normalized dict with `_duration_ms`, `cost_proxy_usd`,
and `result` text.

Cost normalization (rough — different units across providers):
- Claude:    `total_cost_usd` from the JSON envelope
- Copilot:   `premiumRequests` * $0.0033 (1 request ≈ $0.0033 GitHub Copilot pricing)
  This is a placeholder — adjust if your tenant pricing differs.

The copilot CLI does NOT support a separate system prompt, so we concatenate
the system message and user prompt with a SYSTEM/USER delimiter.
"""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path

# Rough USD per Copilot premium request. Used only for relative comparisons.
COPILOT_USD_PER_REQUEST = 0.0033

CLAUDE_MODELS = {'claude-haiku-4-5', 'claude-sonnet-4-5', 'claude-opus-4-5'}
COPILOT_MODELS = {'gpt-5.2', 'gpt-5.3-codex', 'gpt-5.4', 'gpt-5.4-mini', 'gpt-5-mini'}


def _build_combined_prompt(system: str, user: str) -> str:
    """Copilot CLI lacks a system-prompt flag. Inline it with a delimiter."""
    return (
        "=== SYSTEM CONTEXT ===\n"
        f"{system}\n"
        "=== END SYSTEM CONTEXT ===\n\n"
        f"{user}"
    )


def _run_claude(model: str, system: str, user: str, max_cost: float) -> dict:
    with tempfile.TemporaryDirectory(prefix='claude-mm-') as td:
        cmd = [
            'claude', '--print',
            '--output-format', 'json',
            '--model', model,
            '--system-prompt', system,
            '--max-budget-usd', str(max_cost),
            '--dangerously-skip-permissions',
            user,
        ]
        t0 = time.time()
        try:
            r = subprocess.run(cmd, cwd=td, capture_output=True, text=True,
                               timeout=300, encoding='utf-8')
        except subprocess.TimeoutExpired:
            return {'_error': 'timeout', '_duration_ms': int((time.time() - t0) * 1000)}
        dur = int((time.time() - t0) * 1000)
        if r.returncode != 0:
            return {'_error': f'claude exit {r.returncode}: {r.stderr[:300]}',
                    '_duration_ms': dur}
        try:
            j = json.loads(r.stdout)
        except json.JSONDecodeError:
            return {'_error': f'bad json: {r.stdout[:300]}', '_duration_ms': dur}
        return {
            '_duration_ms': dur,
            'cost_proxy_usd': float(j.get('total_cost_usd', 0.0)),
            'result': j.get('result', ''),
            '_provider': 'claude',
            '_model': model,
        }


def _run_copilot(model: str, system: str, user: str) -> dict:
    """Invoke `copilot --prompt` and parse NDJSON event stream."""
    with tempfile.TemporaryDirectory(prefix='copilot-mm-') as td:
        prompt = _build_combined_prompt(system, user)
        cmd = [
            'copilot',
            '--model', model,
            '--output-format', 'json',
            '--allow-all-tools',
            '-C', td,
            '--prompt', prompt,
        ]
        t0 = time.time()
        try:
            r = subprocess.run(cmd, capture_output=True, text=True,
                               timeout=300, encoding='utf-8')
        except subprocess.TimeoutExpired:
            return {'_error': 'timeout', '_duration_ms': int((time.time() - t0) * 1000)}
        dur = int((time.time() - t0) * 1000)
        if r.returncode != 0:
            return {'_error': f'copilot exit {r.returncode}: {r.stderr[:300]}',
                    '_duration_ms': dur}

        assistant_text = ''
        premium_requests = 0
        for line in r.stdout.splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                ev = json.loads(line)
            except json.JSONDecodeError:
                continue
            et = ev.get('type')
            if et == 'assistant.message':
                content = ev.get('data', {}).get('content', '')
                if content:
                    assistant_text = content
            elif et == 'result':
                usage = ev.get('usage', {}) or {}
                premium_requests = int(usage.get('premiumRequests', 0))

        return {
            '_duration_ms': dur,
            'cost_proxy_usd': premium_requests * COPILOT_USD_PER_REQUEST,
            'result': assistant_text,
            '_provider': 'copilot',
            '_model': model,
            'premium_requests': premium_requests,
        }


def run_model(model: str, system: str, user: str, max_cost: float = 0.10) -> dict:
    """Dispatch on model name. Returns dict with keys:
       _duration_ms, _error (optional), cost_proxy_usd, result, _provider, _model
    """
    if model in CLAUDE_MODELS or model.startswith('claude-'):
        return _run_claude(model, system, user, max_cost)
    if model in COPILOT_MODELS or model.startswith('gpt-'):
        return _run_copilot(model, system, user)
    return {'_error': f'unknown model: {model}', '_duration_ms': 0}


if __name__ == '__main__':
    # Quick sanity check
    for m in ('claude-haiku-4-5', 'gpt-5.2', 'gpt-5.3-codex'):
        print(f'\n--- {m} ---')
        res = run_model(m, 'You answer in one word only.', 'What color is the sky?')
        print(f'  duration: {res["_duration_ms"]}ms')
        print(f'  cost:     ${res.get("cost_proxy_usd", 0):.4f}')
        print(f'  error:    {res.get("_error", "—")}')
        print(f'  result:   {res.get("result", "")[:80]!r}')
