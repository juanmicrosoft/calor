# D1 current-candidate measurement at `8f9891a1`

This run compares the accepted compiler behavior with an Annotated-only shadow
and a conservative nominal-Oblivious shadow. All three configurations were
materialized from
`8f9891a1a07f786a76a293017959c3edf28412bf`. The patches are non-shipping
measurement instruments; this directory contains evidence, not production
compiler changes.

## Decision proposal

**Revise the Stage B scope.** Treat identity-proven nominal Oblivious values
from the actual private metadata profile as possibly null at the measured direct
native-return boundary. Do not yet widen initialization or selected
method-input boundaries for those producers, or source-declaration
`NullableAnnotation.None` values. #1401 must first preserve evaluated nullable
context and declaration identity through conversion, and #1402 must add direct
boundary controls and rerun the implemented candidate before activation.

This is narrower than treating every Roslyn `None`, bare Calor nominal spelling,
or unresolved symbol as nullable. It excludes unresolved identities,
annotation-transfer loss, mutation, `ref`/`out`/`in`, unobserved generator
contexts, and array/generic shapes outside their separately accepted matrices.

## Measured result

| Configuration | Inventory | Attempted | Accepted | Rejected | Not reached | Excluded |
|---|---:|---:|---:|---:|---:|---:|
| current | 289 | 177 | 155 | 22 | 106 | 6 |
| shadow-annotated | 289 | 177 | 154 | 23 | 106 | 6 |
| shadow-conservative | 289 | 177 | 154 | 23 | 106 | 6 |

`current` to `shadow-annotated` newly rejects one file:
`MediatR:src/MediatR/Internal/ObjectDetails.cs`. Its six `Calor0274`
observations are explicitly Annotated arguments; they are not Oblivious
fallout. `shadow-annotated` to `shadow-conservative` changes no corpus file.
The attempted corpus contains no predicate row whose source is both Oblivious
and an identity-proven known nominal reference. The zero incremental corpus
set is therefore an observed empty affected set, not proof that source-origin
widening is safe.

The API controls provide the positive conservative-policy evidence. Bounded
discovery inspected 48,558 methods and selected two actual
`NullableAnnotation.None` private-metadata returns:
`JSMarshalerType.Action()` and `JSMarshalerType.Task()`. With effects disabled,
both compile under current and Annotated-only behavior and reject with
`Calor0273` under the conservative shadow. With default effect enforcement,
the current/Annotated-only controls stop first at `Calor0410`/`Calor0411`;
the conservative shadow instead stops at `Calor0273`. These are four changed
control rows, but only two effects-disabled acceptance changes.
The 168-entry API private-reference profile has the same canonical SHA-256
(`efc142856af02bf0d78cc4fb39bd4b83b7b4f2c11bae031cd88bc409d7f10ed6`)
in all three modes; only the deliberately different instrumented compiler
binaries differ.

The required observer-neutrality control was also replayed against an
uninstrumented compiler built from the same accepted source pin. All 30 API
observations matched instrumented-current acceptance, diagnostics, and emitted
bytes. Private-profile discovery, the 168-reference profile, and the warm-up
source also matched exactly. The two compiler binaries intentionally have
different hashes because one contains the non-shipping observer.

The separate CLI controls agree with the API boundary: the explicit nullable
return succeeds under current behavior and rejects under both shadow modes,
while the known-safe constructor succeeds with byte-identical generated output
in every mode and under both effect settings.

## Coverage and retained failures

Each configuration captured 571 compiler invocations over 178 distinct source
identities:

- 6,236 predicate rows;
- 5,017 selected method-input rows;
- 17,004 bound-tree census rows;
- 4,620 diagnostic rows; and
- 571 private-profile rows, including 89 instantiated actual profiles.

Synthetic, Synthetic2, MediatR, and FluentValidation completed both declared
test attempts on baseline and candidate legs in every configuration. Serilog
remains inconclusive in all three configurations: its 112-file inventory
contains 6 configured exclusions and 106 files not attempted after the same
parse-context restore failure. The failure and all archived attempts are
retained; no denominator or zero fallout is inferred.

The source-context inventory matches all original input hashes. Synthetic,
Synthetic2, and MediatR evaluate with nullable enabled. FluentValidation
evaluates with nullable disabled and contains 874 named, nongeneric reference
value-declaration rows with annotation `None`. Serilog's standalone project
query reports nullable enabled, but all 112 per-file contexts remain
unavailable after the restore failure. The 874 FluentValidation rows are
source inventory, not compiler rejections: current conversion loses their
nullable-disabled declaration context and the measured Calor captures do not
retain them as identity-proven Oblivious sources.

The four available source inventories are declaration reconstructions from the
measured project contexts, not complete successful original builds; their
retained Roslyn diagnostics remain in `controls/source-inventory.json.gz`.

## Migration result

All three migration runs select the same
`faithful-fixture-specific-nullable-reference-representation` route without a
hard failure. The original `#nullable disable` fixture proves null pass-through,
present-object identity, fresh construction, and the downstream
`NullReferenceException`. Current conversion emits bare `Item` and loses that
contract. The identity/context-gated candidate changes only `Echo` and
`Receive` declarations to `?Item` and preserves the measured runtime behavior.

This is fixture-only evidence, not a corpus-wide converter implementation. An
independent binding pass still records the unrelated retained `Calor0200` for
`value.Label`; normal `Program.Compile` accepts and emits the runnable C#.
No default, throw, unwrap, coalesce, blanket question mark, or `Option<T>`
substitution was introduced. Explicit raw interop remains the fallback when
#1401 cannot represent a source faithfully.
The current conversion SHA-256
(`9f81d8619461a3fe718f6e3500ca1f2fc76d856201c79a9d345add9545159e74`)
and faithful-candidate SHA-256
(`f98fd526db73dc20fb53c5fc41a9a793bb3edfdad0f397c704e87f789ec6a860`)
are identical across all three configurations.

## Artifacts

- `manifest.json` records the source pin, materializations, unavailable items,
  and raw/stored SHA-256 hashes for every archived artifact.
- `summary.json` contains derived counts and every contributing file,
  source-declaration, control, and unavailable row ID.
- `boundaries.json.gz` contains 98,631 predicate, selected-input, census, and
  diagnostic rows with source hashes, compiler identity, policy, and context.
- `references.json.gz` contains 1,713 per-invocation private-profile rows.
- `e1/` preserves every original JSON and Markdown report byte-for-byte under
  deterministic gzip.
- `attempts/` preserves the original per-invocation capture JSON in deterministic
  tarballs, completion records, exits, logs, and archived TRX attempts,
  including the symmetric Serilog failure.
- `controls/` preserves the uninstrumented neutrality control, all three
  successful API capture bundles with private-reference membership evidence,
  API/CLI results, and source-context evidence.
- `migration.json.gz` and `migration/` preserve the reduced and original
  migration records.
- `development-failures/` keeps failed setup attempts separate from measured
  outcomes.

Run `python3 ../../source/reduce.py <raw-root> .` from this directory to
regenerate the artifact set. The reducer deletes and recreates only its output
directory, uses gzip timestamp zero, validates source/mode pins, and produces a
byte-stable manifest.
