# WS-W4 Slice C — task-generation run report

Mutate-then-convert task generation with the D-W4.1 eligibility predicate and D-W4.3 fidelity gate. 
This run produces the substrate the Slice-E dry-run consumes; it does NOT run agents.

> **Scope of this demonstration (recorded honestly).** This report is the live end-to-end
> proof of the Slice-C machinery, run against the two **in-repo synthetic subjects**
> (`Synthetic`, `Synthetic2`) because the OSS corpus submodules (MediatR/Serilog/FluentValidation)
> are not checked out in this environment and running their full suites offline is out of
> Slice-C scope. The generator is corpus-agnostic — it takes any `RoundTripConfig`, so
> `gen-tasks MediatR Serilog …` runs the identical pipeline once the submodules are present
> (Slice E). Consequently **every eligible task here is an injected-mutation task**; the
> revert-upstream-bugfix path (the gold-standard primary source) is built and unit-tested
> (`BugfixMiner`: fix-shape heuristic + `git log --name-status` parser + revert-candidate
> selection) but its live mining needs a checked-out submodule history and is a Slice-E
> activity. Regenerate with: `calor-roundtrip gen-tasks --synthetic --max-candidates 12 --target 0`.

## Definition of done

- Eligible bundles: **17** (target ≥ 3)
- Projects with eligible tasks that pass the fidelity gate: **2** (target ≥ 2)
- **DoD met: True**

## Fidelity gate (D-W4.3)

- Bar: NativeFraction ≥ 0.70 (PROVISIONAL — pending A-1.4 tranche-2 freeze)
- PP-W2 adjudicable: 2 project(s) pass the fidelity gate.

| Project | NativeFraction | Passes gate | Reason |
|---|---:|:---:|---|
| Synthetic | 100.0% | yes | passes: NativeFraction 100.0% ≥ bar 70.0%. |
| Synthetic2 | 100.0% | yes | passes: NativeFraction 100.0% ≥ bar 70.0%. |

## Exclusion accounting (D-W4.1 — every candidate counted, no silent shrinkage)

| Project | Considered | Eligible | Excl (a) | Excl (b) | Excl attribution | Excl no-covering-test | Eligibility rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| Synthetic | 12 | 10 | 0 | 2 | 0 | 0 | 83% |
| Synthetic2 | 12 | 7 | 0 | 5 | 0 | 0 | 58% |
| **TOTAL** | **24** | **17** | **0** | **7** | **0** | **0** | **71%** |

### Per-candidate dispositions

**Synthetic** (NativeFraction 100.0%, 5 native of 5 files)

| Candidate | File | Operator | Verdict | Reason | Explanation |
|---|---|---|:---:|---|---|
| Arithmetic-L7C18 | SyntheticLib/Calculator.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Arithmetic-L12C18 | SyntheticLib/Calculator.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Arithmetic-L17C18 | SyntheticLib/Calculator.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| OffByOne-L22C18 | SyntheticLib/Calculator.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Arithmetic-L24C18 | SyntheticLib/Calculator.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Boundary-L29C15 | SyntheticLib/Calculator.cs | Boundary | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| OffByOne-L29C17 | SyntheticLib/Calculator.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Boundary-L31C15 | SyntheticLib/Calculator.cs | Boundary | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| OffByOne-L31C18 | SyntheticLib/Calculator.cs | OffByOne | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| OffByOne-L32C20 | SyntheticLib/Calculator.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| OffByOne-L33C22 | SyntheticLib/Calculator.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| OffByOne-L34C22 | SyntheticLib/Calculator.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |

**Synthetic2** (NativeFraction 100.0%, 1 native of 1 files)

| Candidate | File | Operator | Verdict | Reason | Explanation |
|---|---|---|:---:|---|---|
| Arithmetic-L12C22 | GeoLib/Grid.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| OffByOne-L17C16 | GeoLib/Grid.cs | OffByOne | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Arithmetic-L17C18 | GeoLib/Grid.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Arithmetic-L17C27 | GeoLib/Grid.cs | Arithmetic | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Boundary-L22C15 | GeoLib/Grid.cs | Boundary | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| OffByOne-L22C17 | GeoLib/Grid.cs | OffByOne | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| Boundary-L22C24 | GeoLib/Grid.cs | Boundary | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| OffByOne-L22C26 | GeoLib/Grid.cs | OffByOne | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| SwapReturn-L23C20 | GeoLib/Grid.cs | SwapReturn | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Boundary-L24C15 | GeoLib/Grid.cs | Boundary | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |
| Boundary-L24C29 | GeoLib/Grid.cs | Boundary | excluded | NoObservableDefect | clause (b): the mutation compiles but breaks no previously-passing test — no observable defect. |
| SwapReturn-L25C20 | GeoLib/Grid.cs | SwapReturn | ELIGIBLE | None | clause (a) native + clause (b) survives identically on both arms; failure attributed to the mutation. |

## Eligible task bundles

- `synthetic-injectedmutation-cand1-Arithmetic` — InjectedMutation `+ → -` in `SyntheticLib/Calculator.cs`:7; held-out: SyntheticLib.Tests.CalculatorTests.Add_NegativeNumbers, SyntheticLib.Tests.CalculatorTests.Add_ReturnsSum; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand2-Arithmetic` — InjectedMutation `- → +` in `SyntheticLib/Calculator.cs`:12; held-out: SyntheticLib.Tests.CalculatorTests.Subtract_ReturnsDifference; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand3-Arithmetic` — InjectedMutation `* → /` in `SyntheticLib/Calculator.cs`:17; held-out: SyntheticLib.Tests.CalculatorTests.Multiply_ReturnsProduct; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand4-OffByOne` — InjectedMutation `0 → 1` in `SyntheticLib/Calculator.cs`:22; held-out: SyntheticLib.Tests.CalculatorTests.Divide_ByZero_Throws; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand5-Arithmetic` — InjectedMutation `/ → *` in `SyntheticLib/Calculator.cs`:24; held-out: SyntheticLib.Tests.CalculatorTests.Divide_ReturnsQuotient; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand6-Boundary` — InjectedMutation `< → <=` in `SyntheticLib/Calculator.cs`:29; held-out: SyntheticLib.Tests.CalculatorTests.Factorial_ReturnsCorrectValue; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand7-OffByOne` — InjectedMutation `0 → 1` in `SyntheticLib/Calculator.cs`:29; held-out: SyntheticLib.Tests.CalculatorTests.Factorial_ReturnsCorrectValue; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand10-OffByOne` — InjectedMutation `1 → 2` in `SyntheticLib/Calculator.cs`:32; held-out: SyntheticLib.Tests.CalculatorTests.Factorial_ReturnsCorrectValue; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand11-OffByOne` — InjectedMutation `1 → 2` in `SyntheticLib/Calculator.cs`:33; held-out: SyntheticLib.Tests.CalculatorTests.Factorial_ReturnsCorrectValue; native=True, attribution=AttributedToMutation
- `synthetic-injectedmutation-cand12-OffByOne` — InjectedMutation `2 → 3` in `SyntheticLib/Calculator.cs`:34; held-out: SyntheticLib.Tests.CalculatorTests.Factorial_ReturnsCorrectValue; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand1-Arithmetic` — InjectedMutation `* → /` in `GeoLib/Grid.cs`:12; held-out: GeoLib.Tests.GridTests.Area_Multiplies; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand2-OffByOne` — InjectedMutation `2 → 3` in `GeoLib/Grid.cs`:17; held-out: GeoLib.Tests.GridTests.Perimeter_Sums; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand3-Arithmetic` — InjectedMutation `* → /` in `GeoLib/Grid.cs`:17; held-out: GeoLib.Tests.GridTests.Perimeter_Sums; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand4-Arithmetic` — InjectedMutation `+ → -` in `GeoLib/Grid.cs`:17; held-out: GeoLib.Tests.GridTests.Perimeter_Sums; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand9-SwapReturn` — InjectedMutation `return false → return true` in `GeoLib/Grid.cs`:23; held-out: GeoLib.Tests.GridTests.InBounds_Negative_False; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand10-Boundary` — InjectedMutation `>= → >` in `GeoLib/Grid.cs`:24; held-out: GeoLib.Tests.GridTests.InBounds_OnUpperEdge_False; native=True, attribution=AttributedToMutation
- `synthetic2-injectedmutation-cand12-SwapReturn` — InjectedMutation `return false → return true` in `GeoLib/Grid.cs`:25; held-out: GeoLib.Tests.GridTests.InBounds_OnUpperEdge_False; native=True, attribution=AttributedToMutation

## Interpretation note (recorded)

The eligibility rate is itself a Slice-E signal: too low a rate on the OSS corpus means insufficient 
native-eligible surface to yield a decidable dry-run. The fidelity bar is PROVISIONAL (pending A-1.4 
tranche-2). The Calor arm works on machine-converted §-syntax vs the C# arm's idiomatic original — a bias 
AGAINST Calor, so a PP-W2 win is conservative and a loss is confounded with conversion idiom.
