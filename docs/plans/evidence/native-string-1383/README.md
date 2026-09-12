# Native STRING compatibility evidence (#1383)

This is N4 evidence for PR #1455, not Stage A/B or release acceptance.
Accepted baseline: `8f9891a1a07f786a76a293017959c3edf28412bf`.
Production implementation: `f642b11aa5249dc1f9369195af25dcc881ce9f43`.
Corpus measurement checkpoint: `bde9ee88627acb1fcdf62494b701b24856fa5881`.
The later `7698718b1d65471c07b4e459e29335fc6a84a90c` commit updates only the
measured goldens, exact current pin, provenance index and historical attribution.
It does not change the implementation.

The implementation author is the parent Copilot/GPT-6 Astra context, session
`cea41f9d-a634-40dd-ad65-279680749d85`. Measurement contributor
`72b476d6-c608-4073-9c06-47a367ff6cb4` used requested/reported GPT-5.5 with high
effort. The contributor authored these observations and is not either final
non-author reviewer. Final review/adjudication records belong to the PR.

## Read the current evidence first

- [Implementation and boundary contract](../../../design/native-string-argument-compatibility.md).
- [Corpus classification](N4-corpus-delta-classification.md): two source rows
  change, with all diagnostic and effect-denominator consequences named.
- [Strict native controls](control-results-v2/): raw binder, actual default
  `Program.Compile`, default CLI, selected maps, source hashes and binary hashes
  are separate observations.
- [Strict corpus probes](probe-results-v2/): exact diagnostic identities/maps
  for the two changed files. These probe processes did not individually record
  their loaded compiler binary; do not attribute the separate metadata/native
  binary observations to them.
- [Realized references](metadata-realized/): a separately instantiated default
  `MetadataContext.Create()` captured168 references from this probe's184-entry
  generated-code reference pool. Binder uses the same factory at
  `Binding/Binder.cs:155`; this is not a reflection capture of a particular
  Binder instance, nor a claim that every host has an184-entry pool.
- [Manifest-only records](metadata/):167 pinned entries generated on10.0.10.
  These are not the realized reference selection on the measured10.0.11 host.

Across the364 source records, raw binding errors remain4925 and propagated
binding diagnostics move109 to108. Bind-stopped *modules* separately move38
to37. Calor0425 remains117 diagnostics in47 affected modules; its enforced
denominator moves326 to327. These distinct counts are not interchangeable.
The newly reached module still fails existing effect checks.

For `JsonValueFormatter.cs`, the full normalized C# source SHA-256 is
`3f6fa1f940815933c4aa948d41eac450a783530453bc31af1ee1a2a76e3e9d87`.
The source-coverage record's selected/preprocessed source hash is
`84be8c16764d9f5a982d8aa51e182e2254e4a2adf40612d02176e8140b77a769`.
They describe different representations, not mismatched baseline/candidate
inputs.

The probe JSON contains compiler-generated Calor translations and excerpts of
Serilog's `LogEventProperty.cs` (Copyright2013-2015 Serilog Contributors) and
`JsonValueFormatter.cs` (Copyright2016 Serilog Contributors), from pinned
submodule `0597ddfbd4ec594d9c42edd745fe728a2198bad9`. These are transformed
measurement outputs, not unmodified upstream C# files. They remain under the
[Apache License2.0](Serilog-LICENSE), supplied alongside the evidence, without
warranties or conditions of any kind.

## Validation and retained failures

At the frozen implementation, targeted runs passed511 compiler,466 conversion
and22 editor cases. These overlapping selections are not additive totals.
The ordinary full compiler run has an actual inventory of9102:9098 passed,
1 failed,3 inherited skips (9099 executed). Its sole failure was the stale
Calor0425 provenance-index stamp. After the exact index correction, the selected
normal corpus/provenance ratchets passed25/25, with no skips. The full failed
attempt is not retroactively a pass; final PR CI supplies its own result.

The full TRX is losslessly retained as
[`validation/n4-full.trx.gz`](validation/n4-full.trx.gz). Its uncompressed
SHA-256 and copy-time artifact hashes are in
[`publication-copy-manifest.json`](publication-copy-manifest.json).
The final25-case TRX is [`validation/n4-final-ratchets.trx`](validation/n4-final-ratchets.trx).
Inherited skips are two network initialization tests and the unrun effect-row
epoch reporting assertion; no corpus-submodule skip is hidden.

The initial4-case command ran in the old root checkout and is not baseline
evidence. The corrected baseline first failed missing Z3 assets; assets were
restored and checked against committed hashes before its14-case pass. A broader
347-pass/3-failure run exposed old intended-behavior pins. A later411-case run
had407 passes and4 fixture failures: bare `return null` also produced raw0200.
The final fixture uses a real nullable BCL producer through a native return.
No bare-return-null repair is claimed. The corpus62-case attempt had61 passes
and the old326 pin failure; it remains in the retained logs.

Original probes/results are preserved under
[`historical-initial-attempts-20260912T0625/`](historical-initial-attempts-20260912T0625/).
Their raw-only, manifest-only and broad-catch limits were corrected by later
probes. They are not default API results or final reference evidence. Generated
`bin/`, `obj/` and scratch project trees are deliberately not published.
Retained raw build logs include the original whitespace-only lines. They are
measurement artifacts, not reformatted source files.

## Reproduction

`CalorRepoRoot` points to an isolated checkout of the selected source commit.
The small probe projects are evidence drivers, not solution test projects.
Copy the selected driver directory outside every repository/worktree before
running it, so inherited build/central-package settings cannot change its host.
Keep baseline and candidate output directories separate. Restore required Z3
assets only after an actual missing-dependency failure and verify their pins.

Run from the selected checkout so manifest discovery has the same context:

```bash
dotnet run --project "$PROBE/NativeBoundaryProbe.csproj" \
  /p:CalorRepoRoot="$REPO" -- "$REPO" "$OUT/native.json" "$OUT/scratch"
dotnet run --project "$METADATA/MetadataRealizedProbe.csproj" \
  /p:CalorRepoRoot="$REPO" -- "$REPO" "$OUT/metadata.json"
dotnet run --project "$CORPUS/CorpusProbe.csproj" \
  /p:CalorRepoRoot="$REPO" -- "$REPO" \
  bench/corpus/serilog/src/Serilog/Events/LogEventProperty.cs "$OUT/log-event.json"
```

These parameterized reproduction commands are not historical shell transcripts.
Native result JSON records each actual CLI invocation, working directory,
compiler hash, exit code and output. An empty successful probe log does not
itself retain its invoking command.

N4 preserves an actual prior overload rejection with a provenance-specific0274
handoff. Already-applicable OBJECT alternatives and unresolved/invisible
suppression remain analysis-only. General Stage A/#1385 and its production
adjudication/#1386, D1/Stage B, real arrays/#1444, mutable flow, proof/runtime
mitigation changes and release claims remain separate. Research stays paused.
