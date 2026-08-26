# E1 slice 2c — working notes (resume from here)

Branch: `feat/v0.15-e1-slice-2c` off `d41765cc`.

## Goal
Roadmap §4.2 E1 exit pin (c): no `EffectResolver.Resolve(string, string, …)` overload remains.
`EffectResolver` keys on symbol identity via an `EffectResolverKey` type (added to an EXISTING
src file — Calor-first guard forbids new `src/*.cs`).

## Status
- [x] branch created
- [x] EffectResolverKey type + EffectMemberKind, FromStrings (the ONE string factory,
      FromStringFallback=true) / FromBoundReceiver / ForManifestEntry — appended to
      `Effects/EffectResolver.cs` (no new src file: facts.py transcript pins the
      `Effects/*.cs` file list at 10)
  NOTE: heredocs are refused by the worktree guard — use the Edit/Write tools, not
  `python3 - <<EOF`. Run `src/Calor.Compiler/scripts/download-z3.sh` once per fresh clone.
- [x] Resolve(key) is the SINGLE entry point (Method/Extension/Getter/Setter/Constructor by
      `EffectMemberKind`); manifests parsed into keys once in `BuildTypeCache`; four per-type
      dictionaries collapsed to one `Members` dict; `ILEffectAnalyzer.TryResolve(key)`
- [x] string overloads DELETED (Resolve/ResolveExtension/ResolveGetter/ResolveSetter/
      ResolveConstructor) + architecture pin
      `ArchitectureTests.EffectResolver_ExposesNoStringTypeNameResolveOverload`
      (appended at END of the file so `facts.py`'s `ArchitectureTests.cs:158` pin does not shift)
- [x] all src + test + bench callers migrated; Enforcement suite 358/358 green
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
