# Stage A scalar activation adjudication — 2026-09-15

Issue [#1386](https://github.com/juanmicrosoft/calor/issues/1386) adjudicates
the scalar `STRING` activation merged by
[PR #1458](https://github.com/juanmicrosoft/calor/pull/1458). This is a bounded
release gate for Stage A only. It does not accept Stage B, change public docs,
bump a version, or authorize a release.

## Decision: PASS

The final candidate activates the three intended scalar receiving boundaries,
preserves the explicitly tested safe controls, and passes the Stage A
compatibility gate. Repair PR #1459 resolves both blockers found in the first
adjudication:

1. `src/Serilog/Events/LogEventPropertyValue.cs` remains `Replaced`. A
   same-name current-class method preserved in C# interop now makes an
   otherwise unproven no-match or ambiguity opaque; genuine no-match calls and
   explicit-interface-only methods remain diagnostic controls.
2. `src/Serilog/Formatting/Json/JsonValueFormatter.cs` again reaches its
   unrelated pre-existing generated-project failure. C# null-forgiving
   expressions are preserved verbatim because Calor has no equivalent flow
   assertion; a side-effect control proves preservation does not evaluate the
   operand twice.

The equal-input final comparison restores Serilog to 57/112 converted files
and contains no newly rejected supported file. Its only two file-status deltas
from the Stage A base are non-blocking improvements: one FluentValidation file
becomes `Replaced`, and one already-failed Serilog file advances from
`CompileError` to generated-project validation.

## Exact source and output provenance

| Role | Revision | Tree | Meaning |
|---|---|---|---|
| Historical N0 | `080ed5a7482612130fa7572dfebe0f3c58a87ea2` | see N0 | Original 40-case scope and counterexamples |
| Structured E1 | `954aef5f0ec135d96eeac1977bf46649437a343f` | see E1 | Report schema and retained-failure precedent |
| Stage A base | `894c591e9baf86958a6d7476f9793a08fe6cfb50` | `0b952b23ccca7a530415ecb23441cfb0c1ed0c9d` | Merged PR #1457 |
| Base CI merge | `5eb07272b5d14812abf11683288adf78bcadf414` | `0b952b23ccca7a530415ecb23441cfb0c1ed0c9d` | Run `34889695512`; tree-identical to Stage A base |
| PR #1459 head | `562259c528494ace7d5130dd6382f46b3b3f4a29` | `cc8caed0d4a897ca8bda3c7979b28e1fb8baa23b` | Final repair implementation commit |
| Candidate CI merge | `915bfb37181f0b18aa990ef5cc2c75b166c5b2ae` | `cc8caed0d4a897ca8bda3c7979b28e1fb8baa23b` | Final green run `34930956008`; tree-identical to merged candidate |
| Merged candidate | `e3a84766dcc9f34c104445d1150552499c3d3ce3` | `cc8caed0d4a897ca8bda3c7979b28e1fb8baa23b` | Revision adjudicated here |

The base and candidate round-trip reports are therefore an exact tree bridge
from the merged base to the merged candidate. They used the same Ubuntu
24.04.5 x64 host, .NET SDK 10.0.401/runtime 10.0.12, Roslyn binary
`54142770a00e594942ee8923726ea49126303d0c523932886c2417eb66dc07f8`,
subject inputs, corpus revisions, reference capture behavior, thresholds and
harness options. Compiler and harness hashes differ because those assemblies
contain the candidate changes. See
[roundtrip-comparison.json](roundtrip-comparison.json).

The final PR head passed 36/36 checks across Tests run `34930956008`
(31/31), Website regressions run `34930955964` (3/3), and Experiment Registry
Tamper Check run `34930955947` (2/2). The Tests run's `roundtrip-reports`
artifact is ID `10381548516`; the base artifact is ID `10365614707`. The source
JSON hashes are recorded in `roundtrip-comparison.json`.

## Active shape and diagnostic matrix

The shared routing catalog has 18 ordinary combinations: three boundaries
(`Initializer`, `NativeReturn`, `MethodArgument`) × six shapes (`None`,
`ScalarString`, `Array`, `Generic`, `Nominal`, `Unsupported`). Exactly the
three `ScalarString` rows are `CompilationError`, owned by #1385. The other
15 rows remain `AnalysisOnly`, owned by #1402. The separately marked
`MethodArgument`/`ScalarString` native-overload replacement remains active
under #1383.

Local candidate execution covered 33 root-CLI configurations per boundary:
all 32 combinations of `--verify`, `--no-type-check`, `--transpile-only`,
`--permissive-effects`, and `--enforce-effects false`, plus the default argv
with `CALOR_NO_TYPE_CHECK=1`. All 99 invocations exited 1, emitted exactly one
expected Error, and produced no requested `.cs` output:

| Boundary | Code | CLI location (1-based) | LSP range (0-based, end-exclusive) |
|---|---|---|---|
| Initializer | `Calor0272` Error | line 4, column 15, length 74 | `3:14–3:88` |
| Native return | `Calor0273` Error | line 4, column 8, length 74 | `3:7–3:81` |
| Method argument | `Calor0274` Error | line 4, column 34, length 74 | `3:33–3:107` |

`--transpile-only` printed its documented unsafe-mode warning to stderr but
did not suppress the binding error or emit output. Program API coverage ran all
four type-check/transpile combinations per boundary. LSP tests matched binder
and Program API code, Error severity and span, labeled active diagnostics as
`calor`, and cleared them after a safe edit.

`run`, `test`, and `verify` were each executed with default and
`CALOR_NO_TYPE_CHECK=1`; supported verification/permissive variants were also
executed for `run` and `test`. All 14 invocations rejected the active scalar
fixture with `Calor0272`. Unsupported flag/command pairs were not invented.
Warm root-cache, watch transition, shared API context, copied CLI, and
MSBuild-task paths also retained active rejection. For each active boundary, a
successful cached source was edited in place to the nullable violation; the
next run recompiled, exited 1 with the expected code, did not report a cache
hit, and removed the prior `Input.g.cs`. The 15 local MSBuild rows cover all
three boundaries under default, type-off, transpile-only, permissive+verify,
and environment type-off settings; each build failed with the expected code
and no Calor `Input.g.cs` output.

The 99 root-CLI rows and 14 entrypoint rows were replayed against repaired
Release compiler SHA-256
`2f2553aa9afad80e873f7967802208914f4e45480c3c7db08b68c332cbef1f49`.
The 105-test Tasks/MSBuild final-candidate replay also passed. Compact
command-level observations are in
[surface-matrix.json](surface-matrix.json). Test evidence is in
[validation.json](validation.json).

## Safe controls and runtime boundary

The candidate's targeted tests execute, rather than infer, these controls:

| Control | Observed behavior |
|---|---|
| `input ?? "fallback"` at bind/return/argument boundaries | Null returns `"fallback"`; present returns `"value"`; CLI succeeds |
| `input ?? throw` at the same boundaries | Null throws the expected exception/message; present returns `"value"`; CLI succeeds |
| Typed string pattern | String returns `"value"`; null and non-string values return `"fallback"` |
| Converted `default(string)` | Null remains null; it is not mislabeled a safe fallback |
| Literal and resolved non-null string controls | No 0272/0273/0274 |
| Nominal/array/generic receiving shapes | Findings remain analysis-only, not Stage A blockers |

The mutable-assignment exclusion remains real on the merged candidate. The
retained source declares mutable `x:str`, assigns
`Environment.GetEnvironmentVariable("CALOR_N0_UNSET")`, returns `x`, and has a
`Main` that prints `NULL` when the result is null. With that key unset,
`calor run` exited 0, printed `NULL`, and emitted no diagnostic. Stage A
therefore does not prove that null can never inhabit `str`.

Verification controls also remain intact. The selected 106-test local run
included:

- string-model and method-postcondition cases that must be `Assumed` and keep
  their runtime guards; and
- the numeric square control that must be `Proven` and elide its guard.

All 106 passed. This is not permission to lift D3/D12/D14 or close #875.

## Structured corpus comparison

The final five-subject outcomes are:

| Subject | Inventory / attempted | accepted / rejected / skipped / crashed / reverted | Native + losses / failed / excluded | Baseline → candidate tests | Verdict |
|---|---:|---:|---:|---:|---|
| Synthetic | 5 / 5 | 5 / 0 / 0 / 0 / 0 | 5 + 0 / 0 / 0 | 52/52 → 52/52 | pass |
| Synthetic2 | 1 / 1 | 1 / 0 / 0 / 0 / 0 | 1 + 0 / 0 / 0 | 20/20 → 20/20 | pass |
| MediatR | 32 / 32 | 30 / 2 / 0 / 0 / 0 | 18 + 6 / 8 / 0 | 155 pass, 2 skip → same | pass |
| Serilog | 112 / 106 | 96 / 10 / 6 / 0 / 0 | 44 + 13 / 49 / 6 | 811/811 → 811/811 | pass |
| FluentValidation | 139 / 139 | 120 / 19 / 0 / 0 / 0 | 93 + 4 / 42 / 0 | 865 pass, 1 skip → same | pass |

Every report has zero test regressions and zero retained recovery reverts.
Semantic resolution remains explicitly unassessed for every attempted file.
The exact two file-status deltas and three additional diagnostic identities
inside two already-rejected FluentValidation files are classified in
`roundtrip-comparison.json`. The diagnostic-only deltas do not change accepted,
rejected, failed, or coverage denominators; they remain visible as nominal and
nullable-any conversion limitations rather than being hidden by status-only
accounting.

The report generator embeds an inherited sentence claiming
`Calor0272/0273/0274 retain AnalysisOnly routing`. That sentence is stale for
scalar `STRING` on this candidate. The routing matrix and production tests are
authoritative; this adjudication does not silently rewrite the immutable CI
artifact.

## Flake policy

Both base and candidate declared exactly one full-suite attempt per leg before
execution. The retained rule forbids early stop on green and makes any failed,
missing, extra-skipped, or incomplete attempt blocking. All ten subject legs
completed on the first declared attempt. MediatR retains the historical
`ShouldThrowExceptionWhenTimeoutOccurs` allowlist, but it did not fail on any
leg and ignored-flake counts are zero. No rerun was used to turn a red result
green.

## Test evidence

Final Tests run `34930956008` passed 31/31 jobs; the same PR head passed 36/36
checks across all three workflows. Uploaded TRX counts include:

- Compiler: 9,733 passed, 3 skipped, 0 failed across two shards.
- Conversion: 470 passed.
- Semantics: 51 passed.
- Language Server: 519 passed.
- Verification: 406 passed.
- Round-trip harness: 291 passed.

Local final-tree validation used .NET SDK 10.0.400 with .NET 10.0.11 and
8.0.31 runtimes on macOS ARM64. It passed all 9,736 compiler tests (9,733
passed, 3 skipped), all 470 Conversion tests, and final MediatR, Serilog, and
FluentValidation round trips with their expected 155/157, 811/811, and 865/866
results. An initial local `--all` attempt before the required .NET 8 runtime
was installed produced empty MediatR and FluentValidation TRX files on both
legs; it is retained as environment setup evidence and is not the formal
one-attempt comparison. The formal comparison is the clean one-attempt Ubuntu
CI artifact above.

## Limitations and scope

- The technical adjudication is **PASS**. Maintainer adjudication remains
  outstanding, so this packet is not yet an issue-closure claim.
- The round-trip reports are structured and retain candidate data, but their
  `Acceptance` field remains `Unadjudicated`; this document supplies the
  issue-level decision without mutating the CI artifacts.
- Generated-validation references are captured pools, not proof that every
  private metadata lookup selected every listed assembly.
- Absence of a diagnostic is not proof of semantic resolution or null safety.
- Two AI review contexts are recorded after the evidence implementation is
  final. They are independent invocations, not human review or statistical
  independence.
- The evidence owner is GitHub Copilot CLI session
  `183563e9-b822-44f0-843f-4ffee2c11e19`; the runtime does not expose its exact
  model build. PRs #1458 and #1459 were authored and merged by
  `@juanmicrosoft`. No maintainer adjudication of this updated packet was
  obtained; the technical decision is PASS, while issue closure still requires
  the evidence PR to pass and the maintainer to accept the adjudication.
- Stage B, public documentation, versioning, packaging and release remain out
  of scope.

Review prompts, context IDs, findings and dispositions are in
[review-records.md](review-records.md).

## Reuse rule

A later Stage A candidate may reuse this packet only by replacing every
candidate/base/CI pin, replaying the surface and cache matrices, rerunning the
five subjects with the same declared flake policy on both legs, and resolving
or explicitly carrying forward every file-level delta. A green aggregate
round-trip verdict does not override a newly rejected supported safe control.
