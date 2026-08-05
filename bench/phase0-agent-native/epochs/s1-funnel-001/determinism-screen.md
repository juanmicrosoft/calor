# D-S5.1 determinism screen

Gates §0.2: held-out tests must be deterministic, verified by **5 consecutive green runs against
the reference solution** — applied per plan D-S5.1 **on both arms**. The two arms have different
references: the C# arm's is the pristine corpus; the Calor arm's is the **converted, unmutated**
program. **M-S3 counts only screened tasks**, so a task failing here is not epoch-eligible however
real its defect is.

**8 of 8 tasks pass.**

| Task | Project | C# reference | Calor reference | Mutated (reported) | Verdict |
|---|---|---|---|---|---|
| `fluentvalidation-injectedmutation-cand10-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `fluentvalidation-injectedmutation-cand4-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `fluentvalidation-injectedmutation-cand5-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `fluentvalidation-injectedmutation-cand6-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `fluentvalidation-injectedmutation-cand8-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `serilog-injectedmutation-cand11-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `serilog-injectedmutation-cand13-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |
| `serilog-injectedmutation-cand4-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | manifested on all runs | PASS |

Reference columns: ✓ = the held-out set was green (required on **both**, 5/5).
Mutated column is reported only — §0.2 constrains the reference, not the defect's manifestation rate.

## Power of this instrument, stated

Detection at per-run flake rate *p* is 1−(1−p)^5: ~97% at p=0.5, **41% at p=0.1, 10% at p=0.02**.
Five runs bound *gross* flakiness only. The filtered runs are also far smaller than a full-suite
pass, so they carry little of the xUnit parallel-collection interleaving — the very mechanism that
produced a real nondeterminism defect in this epoch's own instrument (`ArmsDiverge` 3→0). A pass
here is not a claim that the held-out set is deterministic under all conditions.

## Provenance

- Harness commit: `8fc5bc7153ff621ebf2b4e5ccf196278069d5c30`
- FluentValidation: `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c`
- MediatR: `fb309026775ef953a64fb5339d074426c1ad2c37`
- Serilog: `0597ddfbd4ec594d9c42edd745fe728a2198bad9`

Per-run test counts and durations are in `determinism-screen.json`.
