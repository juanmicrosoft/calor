# D-S5.1 determinism screen

Gates §0.2: held-out tests must be deterministic, verified by **5 consecutive green runs
against the reference solution** — here, the pristine (unmutated) corpus. **M-S3 counts only
screened tasks**, so a task failing this screen is not epoch-eligible however real its defect is.

**8 of 8 tasks pass.**

| Task | Project | Reference runs | Mutated runs (reported) | Verdict |
|---|---|---|---|---|
| `fluentvalidation-injectedmutation-cand10-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `fluentvalidation-injectedmutation-cand4-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `fluentvalidation-injectedmutation-cand5-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `fluentvalidation-injectedmutation-cand6-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `fluentvalidation-injectedmutation-cand8-EffectViolation` | FluentValidation | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `serilog-injectedmutation-cand11-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `serilog-injectedmutation-cand13-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |
| `serilog-injectedmutation-cand4-EffectViolation` | Serilog | ✓✓✓✓✓ | ✓✓✓✓✓ | PASS |

Reference column: ✓ = held-out set green on the pristine reference (required).
Mutated column: ✓ = the defect manifested (held-out set failed). Reported only — gates §0.2
constrains the reference, not the defect's manifestation rate.
