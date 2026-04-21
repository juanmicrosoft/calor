# Micro-validation Framework

Home of the **micro-validation** test convention for the Calor-native type-system
research plan (§5.0e of `docs/plans/calor-native-type-system-v2.md`). Each
experimental hypothesis ships with a `.calr` program set exercising its positive,
negative, and edge cases; this project provides the framework that loads and runs
those sets uniformly.

## The convention in one line

```
tests/Calor.Experimental.Tests/TIERxY-<name>/{manifest.json, positive/*.calr, negative/*.calr, edge/*.calr}
```

- **Positive** (≥ 40%): programs the feature should flag / handle as a true positive.
- **Negative** (≥ 30%): programs the feature should *not* flag — false-positive prevention.
- **Edge** (≥ 10%): nested, generic, recursive, or boundary-condition programs.
- **Size** total: 15 ≤ programs ≤ 50.

The ratios and absolute sizes are enforced by `CoverageAudit.Evaluate()` at test time.
Sets that fail the audit block the gate for their hypothesis.

## Directory layout

```
tests/Calor.Experimental.Tests/
├── Framework/                                   # The framework itself (this PR)
│   ├── MicroValidationManifest.cs
│   ├── MicroValidationSet.cs                    # Loads a TIERxY directory from disk
│   ├── CoverageAudit.cs                         # Enforces size + ratio quality bar
│   └── MicroValidationRunner.cs                 # Compiles a program with the flag, checks the expected diagnostic
│
├── TIER1A-<name>/                               # Per-hypothesis set (populated per feature)
│   ├── manifest.json
│   ├── positive/p_001.calr, p_002.calr, …
│   ├── negative/n_001.calr, n_002.calr, …
│   ├── edge/e_001.calr, …
│   └── Tier1AMicroValidationTests.cs            # xUnit tests that reference the framework
│
├── TIER1B-<name>/                               # Another hypothesis, same shape
└── …
```

## Manifest schema

`manifest.json` per TIER directory:

```json
{
  "hypothesis_id": "TIER1A-flow-option-tracking",
  "experimental_flag": "flow-option-tracking",
  "expected_diagnostic_code": "Calor1300",
  "description": "Detects unwrap-before-check on Option<T>."
}
```

- `hypothesis_id` must match the entry in `docs/experiments/registry.json`.
- `experimental_flag` is what the compiler expects via `--experimental <name>` /
  `<CalorExperimentalFlags>`.
- `expected_diagnostic_code` is what positive cases must emit when the flag is on.

## How a hypothesis author uses the framework

1. At **Stage 1 pre-registration** (before implementing anything), commit:
   - `manifest.json`
   - The `positive/`, `negative/`, `edge/` directories with ≥ 15 pre-written `.calr` programs.
   - A test class `TierXYMicroValidationTests.cs` (see below).
2. The micro-validation directory is frozen by the Stage 1 pre-registration;
   tests added later during implementation go into a separate `implementation-tests/`
   directory and are **not** part of the gate.
3. CI runs `dotnet test --filter TIERxY` — micro-validation must pass before
   the benchmark can run.

## Example test class (template to paste into a TIERxY directory)

```csharp
using Calor.Experimental.Tests.Framework;
using Xunit;

namespace Calor.Experimental.Tests.TIER1A;

public class Tier1AMicroValidationTests
{
    private static readonly MicroValidationSet Set =
        MicroValidationSet.Load(Path.Combine(AppContext.BaseDirectory, "TIER1A-flow-option-tracking"));

    [Fact]
    public void QualityBar_CoverageAuditPasses()
    {
        var audit = CoverageAudit.Evaluate(Set);
        Assert.True(audit.IsValid, string.Join("; ", audit.Violations));
    }

    public static IEnumerable<object[]> Positive() =>
        Set.PositivePrograms.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Positive))]
    public void Positive_EmitsDiagnosticWhenFlagOn(string programPath)
    {
        var outcome = MicroValidationRunner.Run(Set.Manifest, programPath);
        Assert.True(outcome.DiagnosticEmittedWithFlag);
        Assert.False(outcome.DiagnosticEmittedWithoutFlag);
    }

    public static IEnumerable<object[]> Negative() =>
        Set.NegativePrograms.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Negative))]
    public void Negative_DoesNotEmitDiagnostic(string programPath)
    {
        var outcome = MicroValidationRunner.Run(Set.Manifest, programPath);
        Assert.False(outcome.DiagnosticEmittedWithFlag);
    }
}
```

## Anti-gaming

Per §5.0e: **the micro-validation test set must be pre-registered at Stage 1,
before the feature implementation begins.** Tests added during implementation to
"fix" failing cases are considered implementation tests and do not contribute to
the gate evaluation.

A non-author reviewer confirms the set composition meets the coverage categories
before benchmark evaluation runs — this prevents the proposer from quietly shifting
the audit boundary by reclassifying programs after seeing results.

## Running locally

```bash
# Run all micro-validation tests:
dotnet test tests/Calor.Experimental.Tests

# Run one hypothesis's micro-validation:
dotnet test tests/Calor.Experimental.Tests --filter TIER1A
```

Target feedback time: **under 30 seconds** for a single TIERxY filter. If your
micro-validation runs take longer than that, the set is too large or tests are
doing too much per program.
