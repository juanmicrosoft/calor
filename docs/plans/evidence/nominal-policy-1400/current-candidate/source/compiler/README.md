# D1 #1400 compiler source artifacts

Non-shipping source artifacts for PR1450 current-candidate measurement on
accepted source pin `8f9891a1a07f786a76a293017959c3edf28412bf`.

## Files

- `D1NominalPolicyCapture.cs.txt` — compiler capture hook source. The
  materializer substitutes a compile-time `MeasurementMode` constant so
  `current`, `shadow-annotated`, and `shadow-conservative` produce distinct
  source and binary hashes.
- `D1RoundTripAttemptArchive.cs.txt` — neutral harness TRX archival hook. It
  copies only TRX files already listed by `TestRunResult.TrxFiles`.
- `materialize.py` — pinned exact source transform. It modifies only an
  explicitly supplied detached scratch worktree.

## Materialization commands

Run from this directory or pass the absolute script path. Do not run against
the main owned worktree. Start from a clean detached scratch worktree for each
mode; reset or create separate scratch worktrees between commands.

```bash
python3 materialize.py --worktree /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/d1-shadow-8f --mode current
python3 materialize.py --worktree /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/d1-shadow-8f --mode shadow-annotated
python3 materialize.py --worktree /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/d1-shadow-8f --mode shadow-conservative
```

If HEAD is not exactly `source8f`, use
`--accept-verified8f-file-hashes` only after independently verifying the
pinned compiler/harness file hashes printed in `materialize.py`.

Build/run command for parent after committing these source artifacts and
materializing a detached scratch copy:

```bash
D1_CAPTURE_DIR=/path/to/d1-capture dotnet build
```

Then use the existing E1 harness commands symmetrically for all three modes.

## Transform summary

The materializer asserts exact pre-image SHA-256 hashes for:

- `src/Calor.Compiler/Binding/Binder.cs`
- `src/Calor.Compiler/Binding/NullabilityChecker.cs`
- `src/Calor.Compiler/Binding/Scope.cs`
- `tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs`

It adds:

- `src/Calor.Compiler/Binding/D1NominalPolicyCapture.cs`
- `tools/Calor.RoundTrip.Harness/D1RoundTripAttemptArchive.cs`

It patches only exact text anchors:

- `Binder.Bind` begin/completed capture.
- actual `TryResolveBclCall` result capture.
- actual `ValidateCallArguments` for statement and expression call sites.
- shared `NullabilityChecker.IsPossiblyNullAssignedTo` predicate capture.
- `BindingDiagnosticPolicy.GetRule` nominal-only shadow override.
- `RoundTripPipeline.RunTestAttemptsAsync` raw TRX archival hook.

`shadow-conservative` additionally widens only the actual nominal
identity-gated branch from `Annotated` to `Annotated OR Oblivious`.

## JSON shape

Each binder invocation writes one immutable JSON file to `D1_CAPTURE_DIR`:

```json
{
  "schema": "calor.d1.nominal-policy.compiler-capture.v1",
  "acceptedSourceCommit": "8f9891a1a07f786a76a293017959c3edf28412bf",
  "measurementMode": "current|shadow-annotated|shadow-conservative",
  "invocationId": "d1-<sha-prefix>-<sequence>",
  "compiler": {"assemblyLocation": "...", "sha256": "..."},
  "privateMetadataProfile": {
    "state": "NotInstantiated|Instantiated",
    "profileHash": "...",
    "references": []
  },
  "predicateAttempts": [],
  "bclResolutionAttempts": [],
  "callArgumentValidations": [],
  "boundTreeCensus": [],
  "diagnostics": []
}
```

TRX archival writes `roundtrip-attempts/<attempt-id>/manifest.json` plus
copied TRX bytes when `D1_CAPTURE_DIR` is set.

## Known gaps

- No build, materializer run, or E1 run was performed here by instruction.
- BCL failed resolution captures `MetadataBinderResult.UnresolvedReason`, but
  Roslyn candidate symbol sets are not exposed by the accepted API.
- Origin classification is intentionally not reduced in-source. The JSON keeps
  raw ingredients; parent reduction must retain `OriginUnobservable` and
  transfer-loss rows rather than filtering them.
- Source preparation: `d1-compiler-shadow-artifacts`,
  `ae77764f-32f5-4913-9291-61451d1b129e`, requested/registry model `gpt-5.5`.
  This is an implementation helper, not an independent final reviewer.
