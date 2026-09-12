# N4 corpus measurement publication selection

Evidence root: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/n4-corpus-evidence`

This selection separates corrected observations from historical initial attempts. Do not publish generated `obj/` trees or `tmp/` generated projects.

## Corrected primary report and summaries

- `N4-corpus-delta-classification.md`
- `delta-summary.json`
- `PUBLICATION-SELECTION.md`

## Accepted baseline and candidate golden snapshots

- `baseline/binder-incomplete-baseline.actual8f.json`
- `baseline/binder-source-coverage.actual8f.json`
- `baseline/calor0425-corpus-ledger.actual8f.json`
- `candidate-pre/binder-incomplete-baseline.json`
- `candidate-pre/binder-source-coverage.json`
- `candidate-pre/calor0425-corpus-ledger.json`
- `candidate-post/binder-incomplete-baseline.json`
- `candidate-post/binder-source-coverage.json`
- `candidate-post/calor0425-corpus-ledger.json`

## Corrected strict corpus diagnostic/effect probe

- `probe/CorpusProbe.csproj`
- `probe/Program.cs.txt`
- `probe-results-v2/corpus-probes-v2-summary.json`
- `probe-results-v2/baseline-LogEventProperty-v2.json`
- `probe-results-v2/candidate-LogEventProperty-v2.json`
- `probe-results-v2/baseline-JsonValueFormatter-v2.json`
- `probe-results-v2/candidate-JsonValueFormatter-v2.json`
- `logs/baseline-LogEventProperty-probe-v2.log`
- `logs/candidate-LogEventProperty-probe-v2.log`
- `logs/baseline-JsonValueFormatter-probe-v2.log`
- `logs/candidate-JsonValueFormatter-probe-v2.log`

## Corrected native boundary controls

- `native-controls/NativeBoundaryProbe.csproj`
- `native-controls/Program.cs.txt`
- `control-results-v2/native-boundary-controls-v2-summary.json`
- `control-results-v2/baseline-native-boundary-controls-v2.json`
- `control-results-v2/candidate-native-boundary-controls-v2.json`
- `logs/baseline-native-boundary-controls-v2.log`
- `logs/candidate-native-boundary-controls-v2.log`

## Metadata reference evidence

Manifest-only profiles, retained only with the README label:

- `metadata/README.md`
- `metadata/baseline-metadata-reference-profile.json`
- `metadata/candidate-metadata-reference-profile.json`

Actual realized reference probe and outputs:

- `metadata-realized-probe/MetadataRealizedProbe.csproj`
- `metadata-realized-probe/Program.cs.txt`
- `metadata-realized/metadata-realized-summary.json`
- `metadata-realized/baseline-metadata-realized-references.json`
- `metadata-realized/candidate-metadata-realized-references.json`
- `logs/baseline-metadata-realized-references.log`
- `logs/candidate-metadata-realized-references.log`

## Corpus regeneration and ratchet logs already captured

- `logs/n4-baseline-regenerate.log`
- `logs/n4-candidate-regenerate.log`
- `logs/n4-moved-pins-final.log`
- `logs/n4-corpus-final.log`
- `logs/n4-baseline-LogEventProperty-cli.log`
- `logs/n4-candidate-LogEventProperty-cli.log`

`logs/n4-corpus-final.log` is pre-parent exact-pin update evidence and should be labeled accordingly if cited. Do not rerun corpus generators solely for the later 326→327 pin update.

## Parent final validation artifacts outside this evidence root

These are parent-owned validation outputs under `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/n4-final-validation/`:

- `final-ratchets.log` — final targeted ordinary ratchets passed 25/25, 0 skips.
- `n4-final-ratchets.trx` — TRX for the final targeted ordinary ratchets.
- `full-compiler.log` — full ordinary compiler-project run establishing a9102-test inventory (9099 executed,3 skipped), with one failure from the now-corrected ledger index stamp.
- `n4-full.trx` — TRX for that full run; published losslessly as `validation/n4-full.trx.gz`. Retain as failed solely for the now-corrected index, not as a full pass.

Do not publish `n4-final-validation/tmp/**`.


## Historical initial attempts to retain explicitly as historical

Publish this folder only under its historical label; do not mix these outputs with corrected observations.

- `historical-initial-attempts-20260912T0625/corpus-probe-initial/CorpusProbe.csproj`
- `historical-initial-attempts-20260912T0625/corpus-probe-initial/Program.cs.txt`
- `historical-initial-attempts-20260912T0625/native-controls-initial/NativeBoundaryProbe.csproj`
- `historical-initial-attempts-20260912T0625/native-controls-initial/Program.cs.txt`
- `historical-initial-attempts-20260912T0625/probe-results-initial/baseline-LogEventProperty.json`
- `historical-initial-attempts-20260912T0625/probe-results-initial/candidate-LogEventProperty.json`
- `historical-initial-attempts-20260912T0625/probe-results-initial/baseline-JsonValueFormatter.json`
- `historical-initial-attempts-20260912T0625/probe-results-initial/candidate-JsonValueFormatter.json`
- `historical-initial-attempts-20260912T0625/control-results-initial/native-boundary-controls-summary.json`
- `historical-initial-attempts-20260912T0625/control-results-initial/baseline-native-boundary-controls.json`
- `historical-initial-attempts-20260912T0625/control-results-initial/candidate-native-boundary-controls.json`
- `historical-initial-attempts-20260912T0625/manifest-only-metadata-initial/baseline-metadata-reference-profile.json`
- `historical-initial-attempts-20260912T0625/manifest-only-metadata-initial/candidate-metadata-reference-profile.json`

## Repo golden files for parent inspection/staging

Candidate worktree: `/Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/native-string-1383`

- `bench/phase0-agent-native/binder-source-coverage.json`
- `bench/phase0-agent-native/calor0425-corpus-ledger.json`

## Explicitly excluded from publication

- `probe/obj/**`
- `native-controls/obj/**`
- `metadata-realized-probe/obj/**`
- `tmp/**`
- top-level `probe-results/**` and `control-results/**`; their initial contents are preserved under `historical-initial-attempts-20260912T0625/**` instead.
