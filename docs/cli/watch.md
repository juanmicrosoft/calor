---
layout: default
title: watch
parent: CLI Reference
nav_order: 7
permalink: /cli/watch/
---

# `calor watch` — Recompile on Change

`calor watch` compiles the given sources, then watches them and recompiles on
every change. Rebuilds are **incremental**: unchanged files are skipped via the
build-state cache, so a one-file edit in a large tree recompiles one file.
Compile-only in v1 (no `--run`).

```bash
calor watch src/                 # watch a directory tree
calor watch a.calr b.calr        # watch specific files
calor watch src/ --format json   # machine-readable rebuild stream (NDJSON)
```

Directories are scanned recursively for `*.calr` (excluding `bin/`, `obj/`, and
`reference/` subdirectories) and re-enumerated on every rebuild, so created,
deleted, and renamed files are picked up. Effect manifests
(`*.calor-effects.json`) are watched recursively too — a manifest edit
invalidates the cache and triggers a full recompile.

## Options

| Option | Description |
|:-------|:------------|
| `--format`, `-f` | Per-rebuild diagnostic output: `text` (default, stderr) or `json` (one compact JSON document **per line** on stdout — see below) |
| `--debounce-ms` | Quiet period before a change burst triggers a rebuild (default: 200) |
| `--no-cache` | Disable the incremental cache (every rebuild recompiles everything) |
| `--clear-cache` | Delete `.calor-build-state.json` before the initial compile |
| `--verbose`, `-v` | Per-file status lines (`Compiled:` / `Up-to-date (cached):`) |
| `--strict-api`, `--require-docs`, `--enforce-effects`, `--strict-effects`, `--permissive-effects`, `--contract-mode` | Same semantics as the compile command |

Exit: Ctrl+C or SIGTERM stops the watch cleanly (exit code 0).

## Incremental cache

Watch persists `.calor-build-state.json` at the common ancestor directory of
the watch roots (next to the generated `.g.cs` outputs). A file is skipped only
when **all** of these hold:

- its content hash matches the bytes that were actually compiled last time
  (with an mtime/size fast path);
- its recorded output `.g.cs` still exists **and** its content hash matches —
  a deleted or corrupted output forces a recompile;
- its cached per-module effect summary is present (skipped files still
  participate in cross-module effect enforcement through these summaries);
- no global invalidation applies (compiler upgrade, option change, or effect
  manifest change).

Only diagnostic-clean files are cached: files with warnings/info always
recompile so their diagnostics reappear on every rebuild. When sources are
deleted or renamed, their orphaned `.g.cs` outputs are removed on the next
rebuild.

Add the state file to your `.gitignore` (done automatically by `calor init`):

```gitignore
.calor-build-state.json
```

### Output-shape difference

A cache hit produces no per-file output by default (with `--verbose` it prints
`Up-to-date (cached): <path>` instead of `Compiled: <path>`). Tools that scrape
per-file compile messages should use `--format json` or `--no-cache` instead.

### Options-hash ping-pong with `--verify`

The cache folds all diagnostics-affecting options into an options hash, and
`calor watch` does not run Z3 verification (its hash always records
`verify:false`). If you alternate `calor --cache --verify …` runs with
`calor watch` over the same sources, each invocation invalidates the other's
cache — every switch is a full rebuild. Keep verification runs on a separate
output tree (`--output`) or accept the rebuild cost.

## `--format json`: NDJSON rebuild stream

In json mode stdout is a stream of **one compact JSON document per line, one
line per rebuild** (including the initial compile), each using the
[unified diagnostic schema](/calor/cli/structured-output/). Consumers split on
newlines and parse each line independently:

```bash
calor watch src/ --format json | while read -r line; do
  echo "$line" | jq .summary
done
```

A document is emitted for every rebuild — clean, failing, or crashed — so the
stream position always corresponds to the rebuild count. Human-readable status
("Change detected…", "Rebuild #N: …") goes to stderr.

## MSBuild task

The MSBuild task (`Calor.Tasks`) has its own incremental cache under the
project's output directory; `calor watch` does not share state with it.
