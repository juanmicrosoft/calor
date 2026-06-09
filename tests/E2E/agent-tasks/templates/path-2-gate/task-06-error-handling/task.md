# task-06: Add try/catch around an effectful call chain

## Prompt to the agent

The function `FetchAndStore` in `setup/Pipeline.calr` calls
`§C{FetchRemote}` (which may throw `NetworkError`) followed by
`§C{WriteRecord}` (which may throw `IoError`). Wrap the entire call
chain in a single `§TR` (try) block that catches `NetworkError` and
`IoError` separately and returns an error code:

- Catch `NetworkError`: return `-1`.
- Catch `IoError`: return `-2`.
- Add a `§FI` (finally) block that always sets the binding
  `~closed` to `true`.

The binding `~closed` already exists (initialized to `false` at the
top of the function). Do not change the signatures of `FetchRemote`
or `WriteRecord` in `setup/Remote.calr`.

## Acceptance

- `FetchAndStore` body contains a `§TR{...}` block.
- The try block contains both `§C{FetchRemote}` and `§C{WriteRecord}`.
- Two `§CA` clauses: one for `NetworkError` returning `-1`, one for
  `IoError` returning `-2`.
- One `§FI` clause containing an assignment to `~closed` with value
  `BOOL:true`.

## Why this stresses the ID system

New structural blocks added; Phase 1 stresses positional vs. ID-based
block identification. The try-block wraps multiple existing call
sites, exercising the rewrite-around-existing-code pattern.

## Multi-edit qualifier

1 new try-catch-finally block wrapping 2 calls in 1 file, plus
verifying 1 other file unchanged = 4 sites, 2 files.
