# Task bundle: synthetic2-injectedmutation-cand2-Arithmetic

- Project: **Synthetic2**
- Mutation source: **InjectedMutation**
- Operator: `+ → -` (Arithmetic)
- Mutated region: `GeoLib/Grid.cs` line 14, col 22

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.Equal() Failure: Values differ
>
> Subject hint: Grid
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `GeoLib.Tests.GridTests.SumOfSquares_Theory` (assembly `geolib.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~GeoLib.Tests.GridTests.SumOfSquares_Theory`
- Regression-net project: `GeoLib.Tests/GeoLib.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `GeoLib/Grid.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.Equal()`, Calor=`Assert.Equal()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 100.0%
