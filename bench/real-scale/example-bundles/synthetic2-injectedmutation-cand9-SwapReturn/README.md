# Task bundle: synthetic2-injectedmutation-cand9-SwapReturn

- Project: **Synthetic2**
- Mutation source: **InjectedMutation**
- Operator: `return false → return true` (SwapReturn)
- Mutated region: `GeoLib/Grid.cs` line 23, col 20

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.False() Failure
>
> Subject hint: Grid
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `GeoLib.Tests.GridTests.InBounds_Negative_False` (assembly `geolib.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!=GeoLib.Tests.GridTests.InBounds_Negative_False`
- Regression-net project: `GeoLib.Tests/GeoLib.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `GeoLib/Grid.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.False()`, Calor=`Assert.False()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 100.0%
