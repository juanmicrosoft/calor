# hardness-check-002 — §3.6 verdict: FAILED (second consecutive)

24/24 valid runs, 4 wave-3 modification-under-constraint pairs x 2 arms
x 3. Every pair-arm perfect: 100% task success, zero escaped bugs,
iterations-to-green 1.0 across the board. C#-arm wedge success 100% vs
the 70-90% ceiling. ~164k output tokens.

Cumulative across all epochs: ~165 valid live runs, zero escaped bugs
from either arm, ever. Three escalating hardness models (edge-case
density, interaction traps, modification-under-constraint with
cross-function invariants) all saturated by frontier agents at
fixture scales up to 16 functions.

Durable positive finding: Calor reached FULL iteration parity (1.0x)
on every modification task, vs 2.7x on wave-2 green-field authoring.
The syntax tax concentrates on writing fresh Calor; modifying existing
Calor (the per-module-adoption workflow) costs nothing extra — the
fixture acts as in-context syntax reference.

Strategic implication: the escaped-bugs signal the 2a gate requires
does not exist at authorable-fixture scale at current model capability.
Per the strategy doc's own decision framework, the options are
(a) re-scope the wedge to real-codebase scale (hundreds of functions,
where agent slippage is documented), which exceeds the pair-authoring
model, or (b) invoke the kill criterion for this task class at current
capability: park the gate, bank the findings, reassess at the next
model generation.
