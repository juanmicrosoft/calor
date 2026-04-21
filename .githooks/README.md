# Git Hooks

Client-side hooks for fast local feedback. **CI is the authoritative layer** —
`--no-verify` skips these hooks but not CI.

## Available hooks

| File | Purpose |
|---|---|
| `pre-commit-registry` (bash) | Enforces append-only invariant on `docs/experiments/registry.json` per §5.0f of the type-system research plan |
| `pre-commit-registry.ps1` (PowerShell) | Windows equivalent of the above |

## One-time install

### Linux / macOS / Git Bash

Option A — point git at this directory:

```bash
git config core.hooksPath .githooks
chmod +x .githooks/pre-commit-registry
ln -sf pre-commit-registry .githooks/pre-commit
```

Option B — install a single hook:

```bash
cp .githooks/pre-commit-registry .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
```

### Windows PowerShell

```powershell
git config core.hooksPath .githooks
# .githooks/pre-commit must invoke the .ps1 — see repo-specific wrapper if needed.
```

## How they run

The hook is a no-op unless the commit touches `docs/experiments/registry.json`.
When it does, it:

1. Extracts the base version (last committed) and the staged (about-to-be-committed) version.
2. Invokes `calor evaluation registry-validate` on the two files.
3. Exits non-zero if any existing entry's fields were modified or any entry was removed.

## Bypassing

`git commit --no-verify` skips these hooks. That's allowed for legitimate cases
(e.g., the hook is broken locally). The CI workflow
`experiment-registry-tamper-check.yml` applies the same validation on every PR,
so bypassing the local hook does not bypass the rule.
