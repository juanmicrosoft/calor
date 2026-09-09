# `ExternalBase` — the breakdown R17 asked for, and what it turned out to be

**Date:** 2026-09-08
**Venue:** roadmap-v0.18 §3.2 S2 — *"the per-subject breakdown R17:§6 asked for and never got, and a
fix or a registered trigger — not a fourth carry."*
**Measured at:** `74ba4973` (reachable on `main`; per #1159 this document cites a commit that
resolves rather than a ledger stamp that does not).
**Instrument:** in-process conversion of the three A-1.5.3 subjects at their pinned submodule
commits, `Fidelity=Lossy`, then `EffectEnforcementPass` under `UnknownCallPolicy.Strict`, built-in
manifests only — the same configuration `calor0425-corpus-ledger.json` schema 4 records.

---

## 1. The breakdown

`ExternalBase` is the arm of `CheckEffectVariance` that fires when an override's base class — or the
class satisfying an `§IMPL` — is not visible in the module being enforced. The base's row is then
Unknown, variance cannot be decided, and the site reports `Calor0425`.

| subject | Calor0425 | of which `ExternalBase` | share | modules with diagnostics | modules enforced |
|---|---:|---:|---:|---:|---:|
| MediatR | 3 | **0** | 0 % | 3 | 34 |
| serilog | 39 | **20** | 51 % | 15 | 97 |
| FluentValidation | 71 | **41** | 58 % | 28 | 193 |
| **aggregate** | **113** | **61** | **54 %** | 46 | 324 |

The rest of the surface, for context — the whole of it, so the 54 % is not read against an
unstated denominator:

| cause | count | share |
|---|---:|---:|
| `ExternalBase` | 61 | 54.0 % |
| `InvocationRowless` | 39 | 34.5 % |
| `RowlessDestination` | 13 | 11.5 % |
| `UnknownSource`, `Assumed`, `InvocationUndetermined`, `InvocationAssumed` | 0 | 0 % |

R17 carried *53 of 90 (59 %)*. The absolute count rose and the fraction fell, because 0.17's reach
work enlarged the denominator (304 → 324 modules enforced) faster than it touched this group.

## 2. What the group actually is, and it is not what the name says

Every `ExternalBase` site was resolved to the base class it names, and that name checked against
every type declared anywhere in the same subject:

| | sites |
|---|---:|
| base class **declared in the corpus**, in another file | **60** |
| base class genuinely outside the corpus (`System.IO.StringWriter`) | **1** |

The twelve distinct bases behind the 60: `PropertyValidator` (23), `LogEventPropertyValueVisitor`
(9), `AbstractComparisonValidator` (8), `LogEventPropertyValue` (6), `MessageTemplateToken` (4),
`AsyncPropertyValidator` (2), `RangeValidator` (2), `RuleComponent` (2), `InlineValidator` (1),
`NoopPropertyValidator` (1), `PropertyRule` (1), `ValidatorFactoryBase` (1).

**So "external" here means "in another module", not "outside the program".** Conversion is
per-file and each file becomes one Calor module, so a base class one directory over is external to
the module that derives from it. 98 % of the largest group in the effect-row diagnostic surface is
this.

## 3. It is not a measurement artifact either

The obvious next thought — that enforcing one module at a time is the whole problem, and the real
multi-file pipeline resolves these — is wrong, and it was checked rather than assumed.

`serilog`'s `LogEventPropertyValue.cs` (the base) and `ScalarValue.cs` (the derived) were converted
and compiled **together**, in one invocation:

```
dotnet calor.dll -i LogEventPropertyValue.calr -i ScalarValue.calr
→ ScalarValue.calr(20,7): warning Calor0425: Override 'ScalarValue.Render' overrides a member of
  external base class 'LogEventPropertyValue', which is not visible in this module …
```

Three sites, unchanged. Passing the base's own module to the same compilation does not help.

**The mechanism.** `CrossModuleEffectEnforcementPass` propagates effects along *calls* — it walks
each module's `EffectSummary.Callers` against a `CrossModuleEffectRegistry`. It has no notion of a
base class or an interface, and `CheckEffectVariance` (sites 4 and 5) never consults it: it resolves
bases through `FindBaseMethod` over `_classesByName`, which is built from **one module**. So the
base is visible to the *compilation* and invisible to the *pass*.

That is a gap in the compiler, not in the ledger's measurement rule.

## 4. Disposition

**Not a fourth carry, and not a fix in 0.18 either.** The fix is cross-module variance: the driver
would have to carry each module's class and interface declarations — or at least their members'
declared rows, keyed by type name — into the registry, and sites 4 and 5 would have to resolve a
base through it before falling back to the external arm. That is a feature with its own design
surface (what identifies a type across modules; what happens when two modules declare the same name;
whether a row read from a summary is trusted or re-derived), and it is not something to slip in
under a SHOULD tier.

**Registered as #1180**, with this measurement as its evidence, and a trigger in roadmap-v0.18 §6:
the group is now *diagnosed*, so the next release inherits a named mechanism and a fix sketch rather
than a percentage.

**What this changes about the number.** 54 % of the Calor0425 surface is one mechanism with one
cause, and the honest way to report it is that the compiler cannot see across module boundaries at
sites 4 and 5 — not that a corpus derives from a lot of external types. The second reading is what
three releases of carrying the figure invited, and it is wrong by 60 sites to 1.
