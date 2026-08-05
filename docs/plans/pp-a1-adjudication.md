# PP-A1 — CI adoption gates: **PASS**

**Adjudicated 2026-08-05** against the list frozen at [`wedge-w1-prereqs.md`](wedge-w1-prereqs.md) §3
("frozen now, before any results exist"), carried unchanged into v0.12 by the fold-forward decision.
PP-A1 is a **v0.12.0 release gate**, orthogonal to Call S.

## Item-by-item

| # | Requirement | Evidence | |
|---|---|---|---|
| 1 | Functional published `Calor.Sdk`; **M-A1 green in CI** | `sdk-package-consumer` job in `.github/workflows/test.yml`, green on every recent run | ✅ |
| 2 | Unmasked gates; **publish workflow test-gated** | `publish-nuget.yml` `publish` job declares `needs: [test, sdk-consumer]` — publishing cannot run unless both pass | ✅ |
| 3 | **Checksummed natives** on every publish path | `scripts/z3-upstream-4.15.7.sha256` + `verify_archive` invoked at 3 sites in `download-z3.sh`; verification precedes extraction on every branch, fail-closed on missing manifest / missing entry / hash mismatch | ✅ |
| 4 | Complete **options hash** + fail-closed enforcement | `BuildStateCache.ComputeOptionsHash(string)`, consumed at `CompilationDriver.cs:113`; documented contract that diagnostics-affecting options must be folded in so an option flip invalidates skipped files, presentation-only options excluded | ✅ |
| 5 | **Telemetry opt-in** default, stripped payloads, documented | `CalorTelemetry` activates **only** on `CALOR_TELEMETRY=1`; `--no-telemetry` / `CALOR_TELEMETRY_OPTOUT=1` force off; `AnonymizingTelemetryInitializer` strips payloads; `docs/telemetry.md` | ✅ |
| 6 | **Slice-1 soundness batch** — T1 trio, D8, D6/D7 adjudicated, T2 stopgap, D1 recorded | `484d6c70` (W1 Slice 1). Dispositions recorded in the `verification-modeled-forms.md` divergence table: **D6** adjudicated with why-not (path unreachable from the elision-relevant surface), **D7** closed by refusal (unregistered fields → `Unsupported`), **D8** closed (divisor `≠0` **and** signed-`MinValue/−1` side conditions; demote to `assumed`; conditional-position divisors `Unsupported`), **D9** `string.Replace` closed by refusal | ✅ |
| 7 | **T3 containment** — three surfaces gated | `format --write` → runtime refusal, `Calor1346`, citing #793/#760. LSP **formatting** and **rename** register **only** under `CALOR_LSP_EXPERIMENTAL=1` (`Calor.LanguageServer/Program.cs`), with every read-only handler unaffected | ✅ |
| 8 | **#770 eject-contract** — documented degradation spec | `adoption-playbook.md` §"The eject story (tested)" — a per-construct degradation table (`§S` → runtime check with the same exception shape; interop blocks eject as original C#; elision only inside verified SDK builds on a non-vacuous ∀-proof) | ✅ |
| 9 | **#761 flip stance** — `EnableTypeChecking` default-on with a CHANGELOG note | Recorded in the frozen list as having a slot, **explicitly "not gate-blocking for W1 exit"**. `EnableTypeChecking` remains default-off; the item's requirement is that the flip be *scheduled*, not shipped | ✅ (by its own terms) |

**All nine satisfied. PP-A1 = PASS.**

## A defect found by the audit, fixed here

Item 7's containment is implemented correctly in code, but **`docs/cli/format.md` documented `--write`
as an ordinary option** — an example, a table row, and a "Use `--write` to format files in place"
section, with no mention of the gate. A reader following the docs would hit `Calor1346` with no
warning, and would reasonably read a deliberate release-policy containment as a bug.

The gate is now disclosed at all three places, naming the diagnostic code, the policy issues, and the
sibling `CALOR_LSP_EXPERIMENTAL` containment. **The containment itself was already correct; only its
documentation was not.**

## A near-miss worth recording

An intermediate step of this audit read `Program.cs`'s `WithHandler<FormattingHandler>()` /
`WithHandler<RenameHandler>()` lines *without their enclosing `if (experimentalWriteHandlers)` block*
and concluded item 7 was unmet — i.e. that a release blocker had failed. It had not. Grep-level
evidence about whether something is *gated* is unreliable by construction, because the gate is the
enclosing context, not the line. Recorded because the same shape would produce a false release-blocking
claim in any future audit run the same way.

## Scope

- **PASS is against the frozen list only.** Items explicitly excluded there — coverage ratchets,
  mutation testing, LSP E2E revival, SBOM/hermetic builds, #782/#781 full fixes, #764 full lowering,
  D1/D2 numeric-semantics fixes — remain out and are not implied by this pass.
- **Item 9 passes by its own terms**, which require a scheduled slot rather than a shipped flip. If a
  reader expects `EnableTypeChecking` to be default-on at v0.12.0, it is not.
- **PP-W5 is the other release gate and is NOT adjudicated here.** It requires a spend-authorized
  parity epoch (frozen A-1.4 tranche 1, restated additively at A-1.5.6) and is a separate maintainer
  decision.
