# Task bundle: serilog-injectedmutation-cand4-EffectViolation

- Project: **Serilog**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts IsEnabled's return (boolean flip)` (EffectViolation)
- Mutated region: `src/Serilog/Core/Filters/DelegateFilter.cs` line 26, col 17

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.False() Failure
>
> Subject hint: Matching
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `Serilog.Tests.Filters.MatchingTests.SourceFiltersWorkOnNamespaces` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.AFilterPreventsMatchedEventsFromPassingToTheSink` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Filters.MatchingTests.SourceFiltersSkipNonNamespaces` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Filters.MatchingTests.EventsCanBeExcludedBySource` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Filters.MatchingTests.EventsCanBeExcludedByPredicate` (assembly `serilog.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~Serilog.Tests.Filters.MatchingTests.SourceFiltersWorkOnNamespaces&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.AFilterPreventsMatchedEventsFromPassingToTheSink&FullyQualifiedName!~Serilog.Tests.Filters.MatchingTests.SourceFiltersSkipNonNamespaces&FullyQualifiedName!~Serilog.Tests.Filters.MatchingTests.EventsCanBeExcludedBySource&FullyQualifiedName!~Serilog.Tests.Filters.MatchingTests.EventsCanBeExcludedByPredicate`
- Regression-net project: `test/Serilog.Tests/Serilog.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/Serilog/Core/Filters/DelegateFilter.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.Contains()`, Calor=`Assert.Contains()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 40.0%

## Defect stratum: **Expressible**

- Verification-addressable: the mutation makes Calor's **Calor0410** fire on the 
  converted arm — a signal the C# compiler has no equivalent of. The C# arm's agent may ship the 
  defect. Calor0410 is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable.

  > **This bundle does NOT present that diagnostic to an agent.** Both arms ship plain `.cs` — the
  > calor arm is round-tripped C# — and the runner never invokes the Calor compiler, so the check
  > cannot fire in the loop and neither arm's build fails. The differential above is a
  > **compiler-level** property, established out-of-band by the addressability probe. An epoch over
  > these arms measures the **conversion penalty** (plus the arm-symmetric ceiling-recurrence leg),
  > NOT the verification-depth thesis. See `docs/plans/substrate-arm-validity-finding.md`.

  > **Papering-over residual — a property of the DEFECT, not of this bundle (neither path is
  > exercisable here: no `.calr` sources, no Calor build):** were the arm a real Calor build, the agent could clear it by 
  > REMOVING the injected effect (correct → held-out passes → caught) OR by DECLARING it in §E 
  > (papers over → the bug still ships → held-out fails → escaped). Which path the agent takes IS 
  > the measurement; both remain possible.
