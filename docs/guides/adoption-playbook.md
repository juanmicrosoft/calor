# Calor Adoption Playbook

This is the adopter-side guide to putting Calor modules inside an existing
C# solution, one module at a time — and to leaving again. It is deliberately
honest about what you get, what is unproven, and what it costs to exit.

Read this first; the step-by-step mechanics live in
[Adding Calor to Existing Projects](adding-calor-to-existing-projects.md).

## Read this before adopting (disclosures)

- **Pre-1.0.** Calor is pre-1.0 software. Syntax and semantics are versioned
  (`§SEMVER`) and breaking changes are documented per release, but no
  compatibility promise beyond that exists yet. Check `Directory.Build.props`
  in the repo for the current version; do not trust version numbers written
  in prose.
- **Bus factor 1.** Calor has a single maintainer. The mitigation is
  structural, not aspirational: the eject story below is a tested feature —
  the generated C# is readable, buildable, and yours. If the project stops,
  you `calor convert` your modules to C# and delete the dependency.
- **Refinement types are NOT runtime-enforced (#782).** Refinement-type
  obligations are compile-time analysis only; no runtime guard is emitted
  for them. The review packet states this on every run. Contracts (`§Q`/`§S`)
  DO get runtime checks — refinements do not.
- **Explicit waivers void guarantees.** Building with `--permissive-effects`
  voids the effect guarantee (unknown calls are assumed pure); building with
  `--contract-mode off` voids the contract guarantee (no runtime checks).
  Any review packet produced under either says so on its first line.

## The per-module flow

Calor adoption is per-module: a `.calr` file beside your `.cs` files,
compiled by the MSBuild SDK into C# your solution consumes normally. The
wedge is **pure/contract-dense modules plus first-order effect-checked
code** — start there, not with your most delegate-heavy service layer.

### 1. Install the SDK

The consumer shape below is the exact shape CI tests against the packed
artifact (see `tests/SdkConsumer/`):

```xml
<Project Sdk="Microsoft.NET.Sdk;Calor.Sdk/<version>">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <CalorVerify>true</CalorVerify>
  </PropertyGroup>
</Project>
```

`.calr` files in the project compile to C# during build; `CalorVerify=true`
turns on the Z3 verify gate so contract refutations surface at build time.

`Calor.Sdk` is a self-contained MSBuild SDK: the package bundles
`Calor.Tasks`, the compiler/runtime managed closure, and Z3 native assets for
Linux x64/arm64, macOS arm64, and Windows x64/arm64. CI restores from a local
feed, repeats restore offline, and performs clean Debug/Release, incremental,
design-time, and runtime consumer checks on every supported RID.
Windows x86 is not supported because .NET 10 does not publish an x86 SDK.
Intel macOS is not supported because upstream Z3 does not provide a valid x64 binary.

### 2. Write the first module

```calor
§M{m001:Pricing}
  §F{f001:ClampToCap:pub} (i32:amount, i32:cap) -> i32
    §Q (>= cap 0)
    §S (<= result cap)
    §IF{if1} (> amount cap)
      §R cap
    §R amount
```

- `§Q` preconditions and `§S` postconditions are proven with Z3 where the
  forms are modeled, and kept as runtime checks where they are not.
- `§E{...}` declares effects; `§E{}` means pure and is enforced — an
  undeclared effect is a build error with the full call chain.
- Syntax reference: [syntax docs](../index.md) and the quick reference in
  the repo `CLAUDE.md`; effect codes in
  [effect-manifests.md](effect-manifests.md).

### 3. Import your dependencies' effects

Your module calls third-party packages; the effect system needs to know
what those calls do. `calor import` generates a manifest for a package's
public surface:

```bash
calor import Serilog --project .
calor import path/to/Some.Assembly.dll
```

Three tiers, honestly labeled:

- **Derived (Tier A)** — IL analysis resolved the ENTIRE concrete call
  chain with no unverified assumptions. Emitted with provenance `inferred`.
  A chain that touches a callee missing from the loaded assemblies, a
  bodiless declaration, or a delegate invocation is never emitted as
  derived — it goes to unresolved instead, naming the assumption.
- **Curated (Tier B)** — already covered by the compiler's reviewed
  interface-level manifests (`ILogger`, `DbContext`, `IConfiguration`,
  `IMediator`, Serilog, FluentValidation, Polly, caching, …). Not re-emitted.
- **Unresolved (Tier C)** — dynamic dispatch or analysis limits. Surfaced
  loudly in the report and NOT written into the manifest — an unresolved
  member is never silently treated as pure. Fill these in by hand
  (`calor effects suggest` generates templates).

`calor import` also synthesizes mechanical contract facts from assembly
metadata (non-null parameters from NRT annotations, non-negativity from
unsigned types) into a `.calor-contracts.json` sidecar. **These carry
`assumed` provenance and are annotation-only**: verification never consumes
them as trusted, they can never produce `Proven`, and they never remove a
runtime check.

### 4. Review changes with the packet

```bash
calor review-packet src/pricing.calr --baseline-ref origin/main
```

The packet leads with the **unproven remainder** — every contract that is
not cleanly proven, with its status (`refuted | assumed | unknown | timeout
| unsupported | unavailable`), assumption lists, vacuity flags, and
counterexamples — then the per-module interop fraction, waiver disclosures,
and the direct callers of what changed (resolved across all files in the
invocation). `--json` emits the same content in the CLI envelope for
tooling. `--baseline-ref` approximation: a declaration's extent runs to the
next declaration's start, so an edit between declarations attributes to the
preceding one — conservative over-inclusion, never omission.

The point of the packet is what it does NOT claim: `Proven` means proven
under the modeled semantics; everything else stays visible with its reason.

## The eject story (tested)

You can leave at any time. `calor convert` on a `.calr` file produces the
standalone C# — the same C# the SDK builds — and the degradation semantics
are documented and covered by a dedicated test suite
(`tests/Calor.Conversion.Tests/EjectStoryTests.cs`):

| Calor construct | After eject (C#) |
|---|---|
| `§Q` precondition | Runtime check: `if (!(cond)) throw ContractViolationException(...)` — always retained (never elided, even when proven) |
| `§S` postcondition | Runtime check on return, same exception shape. `calor convert` does not run verification, so ejected output retains every check — elision only ever happens inside verified SDK builds on a non-vacuous ∀-proof |
| `§S` on a body with early/nested returns | Every structured return targets one generated exit, so the return expression is evaluated once and the check runs once after `finally`/`using` cleanup. Exceptional exits skip the check |
| `§S` on an iterator | Rejected with `Calor1004` until deferred iterator-completion semantics are defined |
| Contracts with `--contract-mode off` | Stripped entirely (the waiver you chose) |
| `§E` effect declarations | No runtime footprint — effects were compile-time discipline; the enforcement disappears, the behavior does not change |
| Refinement-type obligations | Comments / retained checks per obligation status (they were never runtime-enforced — #782) |
| Option/Result types | Ordinary `Calor.Runtime` generic types; keep the (small, MIT-licensed) `Calor.Runtime` package or inline the types |
| Unsupported constructs during C#→Calor migration | Were preserved verbatim as interop blocks — they eject as the original C# |

What ejecting costs you: the proofs, the effect discipline, and the
contract vocabulary — the generated runtime checks and behavior stay. That
is the trade, stated plainly.

## Roadmap note (not built, deliberately)

A human-recorded `Justified(who, why, when)` disposition for assumed/unknown
proof results (strategy §5.1) is designed but **adopter-gated**: it ships
when a named adopter with a non-maintainer reviewer exists to consume it
(wedge plan D-W3.5). A persisted semantic index with `calor query` and
index-backed blast radius is likewise deferred (wedge plan §9); the review
packet currently uses the per-build in-memory call graph.

## Links

- [Adding Calor to Existing Projects](adding-calor-to-existing-projects.md) — step-by-step mechanics, troubleshooting
- [Effect manifests](effect-manifests.md) — manifest format, layering, priorities
- [Verify command](../cli/verify.md) — seven-status vocabulary, JSON envelope
- [Structured output](../cli/structured-output.md) — envelope schema, CLI diagnostic codes
