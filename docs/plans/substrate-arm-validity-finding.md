# The epoch's Calor arm cannot present the Calor signal — an instrument-validity finding

**Status:** Finding, 2026-08-05. Not an adjudication. Raised because it **gates the value of WS-S1**,
the L-sized workstream v0.12 was about to fund.
**Scope:** the real-scale bundle epoch (`run-bundle.sh`), the expressible-defect stratum, and what
PP-W2 could measure with the tasks S1 has now supplied.

## The question

S1 established that converter fidelity is the binding constraint on task **supply**, and that WS-S1
is the lever. Before funding an L-sized workstream to supply the epoch, one question needed
answering: **what does the epoch's Calor arm actually present to the agent?**

## The evidence chain

1. **The bundles contain no Calor.** Each bundle ships `csharp-arm/` and `calor-arm/`, both plain
   `.cs`. Zero `.calr` files; the `.csproj` is byte-identical between arms.
2. **The runner says so in its own header** (`run-bundle.sh:15`): *"both plain `.cs` — the calor arm
   is round-tripped C#"*.
3. **The Calor compiler is never invoked.** Grepping `run-bundle.sh` for any invocation of the
   compiler — `calor build|compile|check|verify`, `CALOR_CLI`, `calor.dll` — returns **nothing**. The
   only per-arm difference is which directory of plain C# the agent works in.
4. **The runner's own telemetry records the asymmetry** (`run-bundle.sh:511`):
   > `presentationAsymmetry: "Calor arm = machine-converted round-tripped C#; C# arm = idiomatic original. Bias is against Calor."`

**Therefore `Calor0410` cannot fire during the agent loop.** It is established entirely out-of-band,
by `VerificationAddressability.Probe`, which converts the mutated file *in isolation* and compiles it
with `EnforceEffects=true` — at task-generation time, on the generator's machine, never in front of
an agent.

## The contradiction

Every bundle ships a `README.md` stating (`:45–47`):

> Verification-addressable: the mutation makes Calor's **Calor0410** fire … **the Calor arm's agent is
> confronted by the diagnostic.**

**That is false as shipped.** The agent is never confronted by the diagnostic: it works round-tripped
C#, the Calor compiler never runs, and the build does not fail. The claim is in the artifact that
travels with each task, so an epoch run on these bundles would carry it into its own record.

## What an epoch on these bundles would actually measure

Both arms contain the *same* defect. Both are C#. One is idiomatic, one is machine-converted. So the
measurement is: **does an agent fix a defect better in idiomatic C# or in machine-converted C#?**

That is the **conversion penalty** — precisely what `w4-dryrun-001` concluded the *logic* stratum
measured: *"the mechanical Calor arm has ZERO verification signal for them → measures conversion
penalty NOT thesis."* The expressible stratum was built to fix that. **It fixes it in the
addressability probe, not in the epoch.**

Note this holds under either reading of how Calor is supposed to "catch" the defect:

- **Agent-facing reading** — the agent sees `Calor0410` and acts on it. Impossible: no Calor source,
  no Calor compiler.
- **Build-blocking reading** — the Calor arm's *build* fails with `Calor0410`, so the defect can never
  ship. Also impossible: the arm builds as ordinary C# and succeeds.

## Why this gates WS-S1

WS-S1 is an L-sized box whose purpose is to raise supply toward M-S3 (70 screened tasks). Supply feeds
an epoch. **If the epoch cannot present the verification signal, more supply does not make it able to
measure the thesis** — it makes a larger, better-powered measurement of the conversion penalty.

The supply arithmetic still holds (~177 candidates are recoverable from conversion failures, which is
the only lever that plausibly reaches 70). But it is arithmetic in service of an instrument whose
validity is now in question. **Funding the L before resolving that is the most expensive available
ordering.**

## The options, stated without choosing

1. **Make the Calor arm actually Calor** — ship `.calr` sources and a Calor build so `Calor0410` fires
   in the loop. This is what the bundle README already claims. Largest change; it is the only option
   under which PP-W2 measures the thesis as stated.
2. **Restate what the epoch measures** — conversion penalty plus an out-of-band addressability rate,
   and demote PP-W2 accordingly, under the D-G3.1 restate-or-demote precedent this program owns.
   Cheapest and honest; it concedes that the real-scale venue does not test the headline claim.
3. **Two-channel design** — keep the agent-facing arms as they are for the outcome measurement, and
   report the build-time catch as a separate, clearly-labelled compiler-level result rather than an
   agent-level one.

## Immediate correction owed regardless of which option is taken

The bundle `README.md` template must stop asserting that the agent is confronted by the diagnostic.
That sentence is generated into every task bundle, so the longer it stands the more records carry it.

## What this finding does not claim

- **Not that the arms are mislabelled by accident.** The asymmetry is deliberate and disclosed in the
  runner's header and telemetry; what is wrong is the *bundle README's* claim about the diagnostic and
  the inference that supply alone unblocks PP-W2.
- **Not that the expressible stratum is worthless.** The `Calor0410` differential is real and measured
  — 100% on the candidates that reach the clause. It is a **compiler-level** result, established
  out-of-band, and it is evidence for what Calor's checkers catch. It is not evidence about agents.
- **No adjudication of PP-W2, PP-S1, PP-S3, or the §4 go/no-go.**
