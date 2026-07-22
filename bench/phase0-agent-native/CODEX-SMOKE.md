# Codex configuration smoke test

## 2026-07-22 runtime-hook acceptance run: FAILED (option 1)

Codex completed the W1-001 benchmark, used the Calor MCP server, built
successfully, and passed all held-out tests, but the generated write-validation
hooks did not intercept Codex file changes. Option 1 therefore retains the
hooks as best-effort guidance and relies on the repository/CI Calor-first
backstop for merge enforcement.

### Environment

- Repository base: `663bab32`
- Calor: `0.7.0`, built from the issue #709 working tree
- Codex CLI: `0.144.6`
- Model: `gpt-5.6-sol`, low reasoning effort
- Pair: `W1-001-temperature-converter`, Calor arm
- Codex invocation: `codex exec --dangerously-bypass-hook-trust --sandbox workspace-write --json`
- Hook trust bypass was used only in the isolated, vetted smoke workspace.
- The benchmark reference and held-out tests were absent until Codex stopped.

### Results

| Acceptance check | Result | Evidence |
| --- | --- | --- |
| Project config discovered | PASS | Calor MCP resources and tools were available. |
| MCP consumed | PASS | Codex read `calor://primer`, called `calor_help`, and called `calor_compile` with verification. |
| Pre-write hook blocks C# | **FAIL** | Codex added `Probe.cs`; no denial surfaced. |
| Post-write lint feedback | **FAIL** | Codex added invalid `Probe.calr`; no lint feedback surfaced. |
| Repository Calor-first backstop | PASS | `scripts/check-calor-first-diff.sh` is wired into CI. |
| Benchmark authored by Codex | PASS | Codex refactored the clamp and added `KelvinToCelsius` in `TempConvert.calr`. |
| Clean build | PASS | `dotnet build --no-restore`: 0 warnings, 0 errors. |
| Held-out tests | PASS | `calor test . --verify --contract-mode debug`: 24 passed, 0 failed. |

### Hook diagnosis

The raw Codex session recorded patches as `file_change` events. The underlying
model tool call used Codex's `exec` code host to invoke `apply_patch`; neither
the generated `^(apply_patch|Edit|Write)$` matcher nor a direct-tool retry with
`code_mode_host` disabled intercepted the write.

Additional isolation established that this was not a trust, discovery, PATH,
or Calor-parser failure:

1. Codex reported that hook-trust bypass was active.
2. Adding the same hook inline to `.codex/config.toml` caused Codex to warn that
   both the inline hook and `.codex/hooks.json` had been discovered.
3. An explicit absolute `commandWindows` still did not intercept the patch.
4. A `.*` matcher with an unconditional Windows command that exits `2` still
   did not block an allowed `.txt` patch.

This is consistent with Codex's documented limitation that specialized tool
paths may opt out of the default hook path. The current configuration therefore
cannot claim enforcement for the actual file-change path exercised by Codex
CLI 0.144.6 in this environment.

### Captured artifacts

- `codex-smoke-709/evidence.json` — sanitized, minimal evidence containing the
  version, hook configuration, relevant observations, commands, and verdict

Raw Codex transcripts are intentionally not committed. They contain prompts,
permission metadata, local paths, session identifiers, and other sensitive or
irrelevant data. The sanitized evidence is sufficient to reproduce the verdict
without retaining that material.

The repository backstop can be exercised independently in the same workspace:

```bash
bash scripts/check-calor-first-diff.sh --working-tree
```

This gate is intentionally separate from Codex lifecycle hooks: it catches a
new non-generated `.cs` file beside a `.calr` source even when a specialized
Codex file-change path does not invoke hooks.

## Reproduction

1. Build the current `calor` CLI.
2. Materialize an isolated workspace containing only the W1-001 Calor fixture
   and `spec.md`.
3. Run `calor init --ai codex` and confirm `.codex/config.toml`,
   `.codex/hooks.json`, and `AGENTS.md` exist.
4. Trust the hooks with `/hooks`, or use `--dangerously-bypass-hook-trust` only
   in an isolated automation workspace whose hook sources were vetted.
5. Ask Codex to use `apply_patch` to add a `.cs` probe and an invalid `.calr`
   probe before implementing the benchmark.
6. Capture the event stream privately and require visible pre- and post-hook output;
   commit only sanitized evidence like `codex-smoke-709/evidence.json`.
7. After Codex stops, attach the arm-shared held-out tests and run the clean
   build and test suite.

Merely parsing generated JSON is not acceptance evidence. A future green run
must observe both hook phases, MCP consumption, a successful build, and green
held-out tests.
