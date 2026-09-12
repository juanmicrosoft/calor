# issue1400 / PR1450 migration feasibility source artifact

Nonshipping source only. These archived files intentionally use `.cs.txt` and
`.csproj.txt`; do not add them to the solution and do not treat them as product
implementation for #1401.

## Files

- `MigrationFeasibilityFixture.csproj.txt` — standalone executable fixture
  project. It references an explicitly supplied scratch Calor compiler root and
  existing Roslyn/compiler dependencies only.
- `MigrationFeasibilityDriver.cs.txt` — embeds the nullable-disabled C# fixture,
  runs conversion, parse, bind, default `Program.Compile`, optional labelled
  effects-disabled compile, in-memory C# compile/runtime probes, identity capture,
  a fixture-specific AST nullable-reference candidate, and explicit raw interop
  fallback if the faithful route is not proven.

## Materialize and run later

From an isolated scratch directory inside the measurement workspace, after the
parent pins the source/compiler root:

```bash
mkdir -p artifacts/issue1400-migration-fixture
cp docs/plans/evidence/nominal-policy-1400/current-candidate/source/migration/MigrationFeasibilityFixture.csproj.txt artifacts/issue1400-migration-fixture/MigrationFeasibilityFixture.csproj
cp docs/plans/evidence/nominal-policy-1400/current-candidate/source/migration/MigrationFeasibilityDriver.cs.txt artifacts/issue1400-migration-fixture/MigrationFeasibilityDriver.cs
dotnet run --project artifacts/issue1400-migration-fixture/MigrationFeasibilityFixture.csproj -p:CalorRoot=/absolute/path/to/scratch/calor -- --calor-root /absolute/path/to/scratch/calor --configuration-label current --output artifacts/issue1400-migration-fixture/current.json
```

For source pin guards, add repeated arguments such as:

```bash
--expect-file-sha src/Calor.Compiler/Migration/CSharpToCalorConverter.cs=<sha256>
```

Run the same materialized source under parent-supplied `current`,
`shadow-annotated`, and `shadow-conservative` scratch compiler roots. The JSON
records the configuration label but makes no default API claim from an
effects-disabled compile.

## Expected limitations

The nullable-reference candidate is deliberately fixture-specific. It only
rewrites the proven `Item` parameter/return declaration sites on `Echo` and
`Receive` after Roslyn proves `#nullable disable`, unannotated `Item`, and
matching declaration identity. It does not add defaults, throws, unwraps,
coalesces, `Option<T>`, broad `?` replacement, or production converter changes.
If that route fails, the driver emits a real `§CSHARP` fallback result with
native coverage loss instead of reporting fallback as native success.

The null/present caller harness is separate C# test code, not converted library
code. Its source/hash are explicit inputs on both runtime legs. The first
all-in-one fixture hit the retained direct null-literal native applicability
limit and is preserved as a failed development attempt. A raw passthrough
attempt also failed with duplicated declarations; it is not reported as
successful fallback coverage. The revised fixture gates only Echo/Receive on
actual reference identity and default compilation, retaining the independent
raw `Calor0200` finding on `value.Label` as unsupported member evidence.

Source preparation: `d1-migration-artifacts`,
`5ab0fac2-b888-43ba-84e6-9088be71aa84`, requested/registry model `gpt-5.5`.
This is an implementation helper, not an independent final reviewer.
