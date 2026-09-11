# Diagnostic-routing process supplement

Measured production candidate:
`b9770669506a13d30d4f0255d57786de46551ba8`, based on accepted N0 merge
`7f2e495de2f8949cf27e5d6198c7980765b5fb6e`.
Subsequent changes do not relabel this candidate. Review remediation later moves
the identical policy definitions back into their original `Scope.cs` maintenance
surface and preserves binder provenance in code-action diagnostics. The latter
is a real LanguageServer source change, covered by focused handler/editor tests;
these earlier process outputs are not claimed as a new-head measurement.
`surfaces.json` records actual SDK version, compiler binary hash, timestamp,
source strings, argv, environment overrides, outputs and cache snapshots.

- Ten default-output root-cache steps: opt-out success at step5 is followed by
  default typing0202/exit1 at step6; clearing cache at step7 also rejects.
  Nullable binder0272 remains inactive. Safe warm hits remain possible.
- Thirty actual `dotnet build` invocations through local SDK props/targets and
  `Calor.Tasks.dll`, not an invented SDK API.
- Three real filesystem-watch processes (default, environment type-off,
  permissive effects), each with four observed source states. All stayed responsive,
  reported0206 only at the duplicate state, cleared it on the next safe edit, and
  exited0 after measurement sent SIGINT.
- Six additional explicit-output CLI controls. These are intentionally **not**
  cache-hit evidence: explicit `-o` disables root incremental caching.

## Preserve the different SDK configurations

The first15 SDK builds inherited the repository's warnings-as-errors settings
because the initial supplement omitted the N0 recipe's neutral scratch ancestors.
They are retained in `sdkInheritedRepositorySettings`, not discarded. Nullable
cases rejected through generated-C# validation1002 wrapping CS8600/CS8603/CS8604;
an unused local similarly rejected with CS0219. This was not0272/0273/0274
activation. Active0206 and default TypeChecker0202 also rejected as expected.

The second15 applied the already documented N0 consumer-isolation recipe only
inside the owned scratch directory: `<Project />` in `Directory.Build.props`
and `Directory.Build.targets`, and central package management disabled in
`Directory.Packages.props`. No repository/source gate changed. These results
are separately named `sdkNeutralScratchAncestors`. Nullable negatives compile
with ordinary generated-C# warnings,0206 rejects all five tested profiles,
and0202 rejects default typing but not environment opt-out. The two configurations
demonstrate why "binder analysis-only" does not imply that every nullable program
builds under every host's warning policy.

## Reproduction and scope

Reuse the committed N0 `reproduce/cache.py`, `msbuild.py` and `surfaces.py` in an
isolated checkout of the measured candidate. The supplement copied the same
40-case inputs; all six inputs actually used here are embedded in `surfaces.json`.
The owned scratch base was `.routing-evidence`, not the original N0 workspace.
Changes to the copied drivers were:

| Driver | Deliberate difference from N0 |
|---|---|
| `cache.py` | Scratch base changed; candidate field set to the measured b9770669 SHA |
| `msbuild.py` | Scratch base changed; invocation-only `-p:RestoreSources=https://api.nuget.org/v3/index.json -p:NuGetAudit=false`, after the known normal restore failure |
| `surfaces.py` | Scratch base changed; `cases = []` skips its50 run/test and10 verify invocations. Its real watch sequence and six explicit-output controls still run. New C# subprocess tests cover active run/test/verify routing |

Build `src/Calor.Tasks` first so the local task and compiler are current.
Run the cache and watch drivers; run the SDK driver first without, then with the
three neutral ancestor files. Preserve both SDK matrices. Inherited
`CALOR_NO_TYPE_CHECK` is cleared and each driver's recorded override is applied;
telemetry is off. Temporary fixture source settings are not production defaults.
The evidence does not include paid model calls, research fixtures or a new corpus
run, and does not replace N0's preserved Serilog/runtime/reference limitations.

Targeted C# runs on this implementation included307 compiler/nullability/cache
cases,111 task/cache/SDK-agreement cases,47 editor cases,92 retained soundness
cases, and a later68-case focused pass after expanding the CLI combinations.
The first review-head source-scanner pass had11 cases; helper-source canaries
added two during review remediation. These overlapping runs are not summed as
unique coverage. Initial static test-quality/AST checks passed, but actual full CI
correctly rejected stale exact-count pins (compiler8736 vs8668, editor491 vs486,
tasks127 vs124) and the unnecessary new product C# path. Remediation keeps policy
in the existing file and updates exact counts, including the two additional
compiler canaries and one code-action case, to8738/492/127. Skip expectations and
gate implementations are unchanged. Final review and CI are recorded on the PR.
