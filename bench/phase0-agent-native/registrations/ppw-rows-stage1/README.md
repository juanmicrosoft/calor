# Unfunded prospective stage-1 method

This is the machine-readable companion to
[redesign §9](../../../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md#9-unfunded-prospective-stage-1-registration-1261).
The separately reviewed #1261 amendment registers it on merge.
It is not the instrument's schema-version-2 task/epoch registration,
an epoch scaffold, funding approval, or observed pilot data.

The fixed scope is three tasks representing only shape 7. Two estimands
are registered: equal-cell-weight shape realization across both arms and
equal-task-weight arm-A escape. Non-building eligible runs contribute
zero escape. Unknown outcomes are not zero, not discarded, and not
replaced after inspecting results.

`precision.py` verifies the prior-free sizing derivation:

```bash
python3 bench/phase0-agent-native/registrations/ppw-rows-stage1/precision.py
python3 -m unittest discover -s bench/phase0-agent-native/tests \
  -p test_ppw_stage1_precision.py
```

The CLI prints prospective quantities only. It cannot invoke a model,
accept arbitrary epoch data, change a ledger, or authorize spending.
Its helper for future instrument integration uses exact rational point
estimates and weighted Hoeffding bands. The tests' in-memory counts are
explicitly synthetic mathematical examples, not observations.

The minimal allocation is **74 runs per task per arm, 444 total slots**,
for two jointly 95%-covered, nominal ten-percentage-point half-width
targets under independent fresh-run assumptions. It is not a power
calculation, rare-event guarantee, or confidence statement over a sampled
population of tasks. Actual attrition widens the band; an unscorable
required outcome leaves the affected estimate unidentified.

`agent-provenance.json` records a real `claude --version` observation and
the local executable hash. It does not claim a provider-weight hash,
complete environment identity, paid model invocation, or installed
historical client version. The deliberate model/agent choice is carried
into actual epoch pins by #1265.

The task freeze, validated operational indicator, preserved API contract,
product/pin admission, and one-epoch collection/analysis path are still
required. This calculator does not silently replace the shared analyzer;
#1264's owner integrates the reviewed method.

No spending ceiling or separate null-result acceptance has been recorded.
#1259 blocks every paid run. Stage-2 Δ/N and its outcome are deliberately
absent; they require actual pilot data and separate registration.
