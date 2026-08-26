# E1 slice 2c — working notes (resume from here)

Branch: `feat/v0.15-e1-slice-2c` off `d41765cc`.

## Goal
Roadmap §4.2 E1 exit pin (c): no `EffectResolver.Resolve(string, string, …)` overload remains.
`EffectResolver` keys on symbol identity via an `EffectResolverKey` type (added to an EXISTING
src file — Calor-first guard forbids new `src/*.cs`).

## Status
- [x] branch created
- [ ] EffectResolverKey type
- [ ] Resolve(key) / manifests parsed to keys once / ILEffectAnalyzer.TryResolve(key)
- [ ] delete string overloads + architecture reflection Fact
- [ ] key ledger bench/phase0-agent-native/effect-resolver-key-ledger.json
- [ ] "?" sentinel decision
- [ ] ChainWalkCouldChargeEffects FIXME(E2)
- [ ] docs (design §8.1/§8.4, roadmap §4.2, CHANGELOG)
- [ ] suites

## Key facts gathered
- `src/Calor.Compiler/Effects/EffectResolver.cs` — `Resolve(string,string,params string[])` :48,
  cache key :59 (`m:` prefix disjointness), six-step order :5-14/:175-221, `ResolveExtension` :72,
  hardcoded Linq receiver list :103-117, `TryILAnalysis` :314-317.
- Callers of the string API (src): `Evaluation/Metrics/InteropEffectCoverageCalculator.cs:41`,
  `Commands/EffectsCommand.cs:158,484`, `Effects/IL/TransitiveEffectPropagator.cs:214`,
  `Effects/Manifests/PackageManifestGenerator.cs:359`, `Effects/EffectEnforcementPass.cs`
  (895, 1234, 1245, 1251, 1262, 1268, 1976, 1982, 2280, 2352, 3062),
  `Migration/RoslynSyntaxVisitor.cs:12514,12517`.
- Tests calling the string API: Enforcement.Tests (EffectResolverTests, EffectsSuggestTests,
  Issue785ClosureTests), ILAnalysis.Tests, Compiler.Tests/ConversionCampaignFixTests,
  tests/Calor.Evaluation/Metrics/InteropEffectCoverageCalculator.cs.
