# PR A (#1545, #1548), Codex round 2

Reviewing 280eea0bd. Raw final message: `pr-a-round-2-codex.raw.txt`.

**Verdict: REQUEST-CHANGES** (1 MAJOR, 2 MINOR). Codex confirmed the round 1 fixes (pagination,
the queued-run note, and the guard running first and failing closed) and again found no way past
the 0.24.0 override pin.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | `environment: NUGET-RELEASE`, `secrets.nuget_release_api_key`, and the YAML escape `"nuget-release"` all produced CLOSED. Environment and secret names are case-insensitive. | Fixed by changing approach. Without a YAML parser (PyYAML is not available), interpreting syntax cannot be complete. So environment declarations are now **allowlisted**: the publish job's line must be exactly `    environment: nuget-release`, and every other declaration must match `OTHER_ENVIRONMENTS` verbatim. Case changes, escapes, quoted keys, flow-style jobs (`W008`), anchors, aliases, and merge keys (`W009`) therefore fail. Secret-name mentions are compared after case folding and escape decoding. New cases in the Python tests and in `ReleaseWorkflowGateTests` (+3). |
| 2 | MINOR | A quoted `"if": false` or `'continue-on-error': true` on the guard step passed. | Fixed. The guard step may carry only the keys `name`, `env`, and `run`, quoted or not. Two more mutants were added. |
| 3 | MINOR | The README called deleting tags a sufficient remediation. Reruns replay the original ref, and branch copies can still request the repository secret. | Fixed. The README now says only secret removal plus environment scoping plus key revocation closes the gap. |
