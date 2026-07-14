# hardness-check-001 — §3.6 verdict: FAILED (pairs too easy)

30/30 valid runs, 5 wave-2 pairs x 2 arms x 3. Zero escaped bugs in
either arm on every pair; C#-arm wedge-category success 100% vs the
70-90% ceiling envelope. Wave-2 wedge pairs cannot power the 2a gate.

Secondary finding (consistent, informative): Calor pays iterations
where training prior is thin — csv parsing 2.7x, fs-journal 2.7x —
and matches C# at 1.0x on contract/arithmetic pairs (W1-002, W2-003,
W2-004). Zero invalid runs (auto-detection active). ~194k output tokens.

Implication for wave 3: hardness must come from scale and interaction,
not edge-case density — larger starting fixtures, cross-function
invariants, modification-under-constraint tasks. Until the C# arm
ships nonzero escaped bugs, the strategy's benefit half is unmeasured.
