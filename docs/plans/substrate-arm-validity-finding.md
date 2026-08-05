# The epoch's Calor arm cannot present the Calor signal — consequences for the record and for PP-W2

**Status:** Finding, 2026-08-05. Not an adjudication.
**Provenance — read this first.** The underlying fact is **already on `main`** and is not discovered
here. `bench/phase0-agent-native/epochs/s1-funnel-001/FINDING.md:110–113` (merged `76f932f3`) records:

> **The "calor arm" contains no Calor.** Bundles ship round-tripped **C#** (0 `.calr` files, identical
> `.csproj`) … `Calor0410` is established by an out-of-band single-file probe and **is not reachable
> by an agent working the bundle**.

An earlier draft of this document framed that as a question newly answered. It was not. **What is new
here is the consequence:** three merged records describe the arm in terms the implementation does not
carry, one of them a *registered* arm definition; the misstatement is generated into every task
bundle; and PP-W2's claim needs re-scoping. The fact is old, the record's exposure is new.

## The verified mechanics

Independently re-verified: 0 `.calr` files across all 8 bundles (3,515 `.cs`); `diff -rq` shows no
differing `.csproj`/`.props`/`.targets` between arms; `run-bundle.sh` contains no invocation of the
Calor compiler, sources nothing, and its dotnet PATH shim is a pass-through (`"$real_dotnet" "$@"`)
with no MSBuild property or targets injection. There is no path by which `Calor0410` reaches the
agent loop. It is established by `VerificationAddressability.Probe`
(`VerificationAddressability.cs:165`, `EnforceEffects = true`) at generation time, over a single
converted file, on the generator's machine.

The runner is candid about this in its own header (`run-bundle.sh:15`, *"both plain `.cs` — the calor
arm is round-tripped C#"*) and telemetry (`:511`, *"Calor arm = machine-converted round-tripped C#;
C# arm = idiomatic original. Bias is against Calor."*).

## Three records that describe an arm the implementation does not carry

This is the part that matters, and it is larger than the bundle README an earlier draft named.

1. **A registered arm definition.** `wedge-plan-v0.11.md:95` (D-W5.2) registers the real-scale epoch's
   *"Calor arm (v0.11 native config: **enforcement on** per D-W2.5, **verify gate on** per A-1.3's
   instrument …)"*. The implementation carries neither: no Calor build, so nothing to enforce and no
   gate to run. **A divergence from a registered arm definition is a stronger defect than a
   documentation error.**
2. **A merged epoch record.** `w4-dryrun-001/VERDICT.md:11–14` describes the arm as *"machine-converted
   **Calor working copy** + WS-W2 effect enforcement + `calor import` annotations"*. That record is
   cited by `substrate-plan-v0.12.md:153` as the basis for **M-S3's power derivation**, so it is
   load-bearing for a live plan.
3. **Every generated bundle.** `TaskGenReportWriter.cs:68–70` emits, into each task's README:
   *"the Calor arm's agent is confronted by the diagnostic."* Present in 8/8 bundles. And the block
   goes further at `:74`, asserting the agent *"can clear the Calor build … by DECLARING it in `§E`"*
   — with zero `.calr` files there is no `§E` to declare in and no Calor build to clear. **The
   correction owed is the whole stratum block, not one sentence.**

In fairness the README is **self-contradictory rather than uniformly misleading**: two sections
earlier it discloses *"`calor-arm/` — mutate-then-convert output (converted §-syntax round-tripped to
C#)"* and records the presentation asymmetry.

## What an epoch on these bundles would measure

Both arms carry the same defect; both are C#; one is machine-converted. So the differential measures
the **conversion penalty** — plus, arm-symmetrically and unaffected by any of this, two things the
program does value:

- **D-W4.4 ceiling-recurrence** — C#-arm escaped-bug incidence, a single-arm existence result.
- **PP-A2 day-one product truth** — which the dry-run disposition explicitly retained.

The honest statement is: *conversion penalty + the arm-symmetric ceiling leg, neither of which is the
verification-depth thesis.*

For the logic stratum this was already concluded. `w4-dryrun-001/VERDICT.md:43–48`, verbatim:

> **The mechanical-only arm has no verification signal for LOGIC bugs.** … On this task class the
> Calor arm's only differentiator is the conversion penalty. So this configuration measures the
> conversion penalty **+ enforcement value**, **not** the verification-depth thesis.

*(An earlier draft of this document rendered that as a quotation it is not, dropped the "for LOGIC
bugs" restriction — which is the whole reason the expressible stratum exists — and omitted "+
enforcement value", the very channel at issue. Both distortions flattered this document's thesis and
are corrected here.)*

The expressible stratum was chartered to fix exactly this: `VERDICT.md:65–69` asks for *"a
deterministic **Calor build-block** vs no C#-arm signal"*. A build-block is by definition in-loop.
**The stratum delivers it in the addressability probe, not in the epoch.**

## What this means for WS-S1 — weakened justification, not a gate

An earlier draft claimed this "gates" WS-S1 and that funding it first would be "the most expensive
available ordering." **That was overstated, and the plan already carries the controls it demanded:**

- `substrate-plan-v0.12.md:57`/`:131` — WS-S0.5's cheap funnel probe runs **first**, and WS-S1 is
  scoped from its result, adopted precisely to avoid committing an L on thin evidence.
- `:73` (D-S1.1a) — an **early abort** before D-S1.2 is funded.
- `:196`/`:198` — PP-S1 miss → *"Authorize anyway"*; *"supply, not fidelity, decides the venue."*

And converter fidelity has value the epoch does not confer: `NativeFraction` is the quality metric of
the **shipped** `calor import` migration path, and D-S1.4/D-S1.5/M-S5 are anti-gaming devices with
standalone worth. **WS-S1 should proceed.** What is weakened is the *justification framing* — "supply
the epoch so PP-W2 can be measured" — not the work.

## A second instrument threat, recorded here because it is worse for the control arm

`s1-funnel-001/FINDING.md:114–116` also records that the injected defect is **greppable in both arms**
(`__calorTaint`). Verified present in the mutated source of both arms in every bundle. That collapses
the **C# arm's** difficulty — the control — and therefore threatens every leg of an epoch built on
these bundles, including the ceiling-recurrence check this finding otherwise leaves standing.

## Options, stated without choosing

1. **Make the Calor arm actually Calor** — ship `.calr` and a Calor build so `Calor0410` fires in the
   loop. Worth noting precisely: because `Calor0410` is an **enforcement** diagnostic requiring no
   contracts, this leg does **not** depend on the authored-contract overlay (WS-S4), which is off the
   critical path. It is therefore cheaper than the overlay work and not blocked by it.
2. **Restate what the epoch measures** and demote PP-W2 under the D-G3.1 restate-or-demote precedent.
3. **Two-channel design** — agent-facing arms for the outcome measurement, the build-time catch
   reported separately as a clearly-labelled *compiler-level* result.

## Corrections owed regardless of the option chosen

- `TaskGenReportWriter.cs:68–74` — stop generating both the "confronted by the diagnostic" claim and
  the `§E` papering-over note.
- `w4-dryrun-001/VERDICT.md:11–14` — annotate the arm description as describing an intended
  configuration the runner does not implement.
- `wedge-plan-v0.11.md:95` — record the divergence from the registered arm definition.

## What this finding does not claim

- **Not a discovery.** See the provenance note above.
- **Not that the arms are mislabelled by accident** — the asymmetry is deliberate and disclosed in the
  runner. What is wrong is the three records above and the inference that supply alone unblocks PP-W2.
- **Not that the expressible stratum is worthless.** The `Calor0410` differential is real and measured
  (100% on candidates that reach the clause). It is a **compiler-level** result about what Calor's
  checkers catch — not evidence about agents.
- **No adjudication** of PP-W2, PP-S1, PP-S3, or the §4 go/no-go.
