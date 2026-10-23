# #1135 G3 PR 2 (#1500) — adversarial review round 2 (Codex)

**Reviewer:** Codex CLI (same invocation as round 1). **Reviewed commit:** `8094199e`.
**Verdict:** APPROVE. No new findings.

All three round-1 dispositions were accepted:

1. **MAJOR (pruning).** The forbidden-character set matches the upload library's validation. The
   reviewer confirmed that main's `workflow_problems` (`scripts/determinism_protocol.py`) fixes the
   workflow's step order and bodies. It also confirmed that `test.yml` runs main's validator
   unconditionally on the PR tree, so an added upload step would fail `D013`. The residual (a
   removal that fails) is documented, and the disposition is sound.
2. **MINOR (setup time).** The text now separates 1.2–3.0 minutes of setup from about 3.1 minutes
   per attempt. The longest completed invocations, 201.2 s and 43.2 s, match the text.
3. **MINOR (cut invocations).** The text now separates retained passing observations from
   harness-generated values. All 1,580 `DISAGREE` cases contain a cut-generated value, and the
   translator fixture is `INCOMPLETE`.

Other checks by the reviewer:
- Every registered record, TRX, cell, and generated-file path, and each of its ancestors, has a
  safe name, so pruning cannot select one.
- The `Path.unlink` monkeypatch reaches the injected failure and is restored.
- Protocol validation passes, and 39 read-only controls pass. The new filesystem-writing control
  was inspected but not run in the read-only sandbox; it passes locally (54 OK).
- Both ledger hashes match, the recorded result reproduces exactly, and execution 2 passes the
  plan checks.
