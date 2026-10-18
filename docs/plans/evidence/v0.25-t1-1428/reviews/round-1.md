# T1 (#1428) Codex review, round 1 (invocation 1 of at most 4)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: adversarial
review of `git diff origin/main...HEAD` at `6b2c710f9` (scope amendment 1.3.0 and S014, the T1 register
and validator, the shared controls, the aggregator). **Verdict:** CHANGES-REQUESTED (5 MAJOR, 2 MINOR).
Reduced independence (R0 §9).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R1-1 | MAJOR | T009 never binds evidence paths, fixture hashes or the results hash to an approval: a hash can be swapped (or F1's evidence replaced by F6's) without a register amendment | Fixed: `approvalSeal` over every approved path and hash must equal the latest amendment's (or, with none, the 1.0.0 baseline pinned in `T1RegisterTests`); evidence must live under the family's own `v0.25-f<n>-<issue>/` directory |
| R1-2 | MAJOR | S014 accepts dropping a release gap or two of the five re-proof checks from both copies | Fixed: the three gaps and five check ids are frozen in the validator as a floor (a later amendment may add, never remove) |
| R1-3 | MAJOR | `aggregate.py` discharges gaps by filename only (a `.failed` witness file counts) and misses validly named records | Fixed: it parses each candidate record and checks candidate, case, requirement digest, every cell and `passed` (witness: candidate, repair, witness list, `passed`), and also follows the paths a scope readiness record cites |
| R1-4 | MAJOR | X-INT-07 fixture does not return an expression-position `new int[,]` from a native local function | Fixed: `Grid(a)` returns `new int[,] {...}` directly and the caller indexes it |
| R1-5 | MAJOR | Any compile error on emitted output counts as `rejected`, which the handler and rescue controls allow | Fixed: emitted output that fails a non-effect compile is `invalid-output`, never allowed; `rejected` is only a refusal with no output |
| R1-6 | MINOR | T1-INTERP-FORMATTABLE logs the same tag for every hole, so reordering is invisible | Fixed: each hole logs its own tag |
| R1-7 | MINOR | Compilation reads `~/.calor/manifests` (in-process and in CLI children) | Fixed: in-process compiles use a built-in-only `EffectResolver`; CLI children run with `HOME`/`USERPROFILE` set to the test's temp directory |
