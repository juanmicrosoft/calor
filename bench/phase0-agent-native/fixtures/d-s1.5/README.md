# D-S1.5 fixture registry — PP-S4's instrument

Registered at **A-1.5.7** (`docs/plans/agent-native-gates.md`): *"a blocker with no measurement
path is unfalsifiable."* Carried out of Call S as explicit debt ("the first `SupportLevel`
promotion re-arms PP-S4 with no CI to enforce it") and discharged by roadmap v0.13 §2.5 gate 6.

**What PP-S4 guards:** M-S4 = 0 — no converter or harness change may raise a support or fidelity
metric *by making converted code less faithful* (reclassification instead of implementation).
The registry is the measurement path: a `SupportLevel` promotion or a loss-ledger removal is
only mergeable when a fixture demonstrates the claimed fidelity on real values.

## Schema (frozen at A-1.5.7 — one directory per registered feature)

```
d-s1.5/<feature-key>/
  fixture.json     { "featureKey": "<FeatureSupport key>",
                     "lossKindCertifiedAbsent": "<ConversionLossKind name or null>" }
  input.cs         the C# input exercising the feature
  expected.calr    the expected converted Calor (exact match against `calor migrate` output)
  test.calr        the value-asserting test: a Calor program using the converted construct
                   whose contracts/assertions fail if the conversion is value-wrong
```

## CI entry point (frozen at A-1.5.7): the `d-s1.5-fixtures` job

`scripts/check-d-s1.5-fixtures.sh` runs on every PR and asserts, for the diff under test:

1. **Every `SupportLevel` promotion toward `Full`** in `Migration/FeatureSupport.cs` (including
   a new entry born above `NotSupported` — the same maneuver in one step) has a registry
   directory whose `fixture.json` names that feature key.
2. **Every net loss-kind removal** (a `ConversionLossKind.<Kind>` reference removed from
   `Migration/` sources) has a registry fixture naming that kind as certified-absent.
3. Registered fixtures are **green** (`DS15FixtureRegistryTests` — structure, conversion match,
   Calor compilation). **Indeterminate (a fixture that will not build) counts as failing**,
   conservatively, per the registration.

The job runs the script's **`--self-test` first on every invocation**: a synthetic fixture-less
promotion must make the check fail, and a no-change diff must pass. This is the roadmap gate 6
discriminating pin, executed continuously rather than demonstrated once.

## Initial contents

Empty, per the registration: frozen by location, schema, and entry point — not by content. The
first fixtures are expected from roadmap §3.2's nullable-idiom migration work, which is blocked
on this gate being green (roadmap §3.2 sequencing constraint). Value-assertion *execution* of
`test.calr` (beyond compile validation) lands with the first fixture; the disclosure exists so
the gap is a decision on record, not an omission.
