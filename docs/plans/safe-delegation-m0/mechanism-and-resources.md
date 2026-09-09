# Safe delegation M0 mechanism and resource screen

**Recorded:** 2026-09-09. **Issue:** #1281. **Authority:** repository-evidence
review only under [authorization.md](authorization.md); no funded methods
inquiry, prototype, service build, or collection.

For this record, arm A is ordinary C#, arm B is strong protected C#, and arm C
is protected Calor. No mechanism below is a measured advantage or an effect-size
prior.

## Falsifiable cost mechanisms

**C versus B.** Protected Calor could cost less than strong protected C# if its
compiler-enforced contracts, effects, and refusal behavior prevent invalid
changes before independent review, thereby removing enough manual
requirement-boundary inspection, repair, and replacement execution to exceed
the cost of Calor translation, unsupported-form handling, onboarding, compiler
integration, and lower model familiarity. This mechanism is falsified if
protected C# catches the same defects at comparable review/repair cost, if
Calor's added translation and refusal costs dominate, or if accepted Calor
changes do not preserve the claimed properties under the independent oracle.

**B versus A.** Strong protected C# could cost less than ordinary C# if an
B-specific analyzers, guards, requirement restrictions, and fail-closed
enforcement expose defects earlier or provide stronger review evidence,
preventing enough manual inspection, escalation, and repair to outweigh B's
service integration, policy maintenance, reviewer training, and recurring
enforcement overhead. This mechanism is falsified if ordinary review already
achieves nearly the same correctly accepted fraction and serious-defect rate,
if B does not change reviewer work, or if B's setup and recurring enforcement
costs exceed its saved review and repair.

The independent held-out oracle is arm-shared and runs after each arm's normal
review/repair workflow. It measures outcomes and cannot be credited as a
B-specific source of review savings. Oracle authoring and execution remain
separate study/R&D costs.

The illustrative change from 90% to 100% correct acceptance improves the
denominator by only `1 / 0.9 = 1.11`. It is not observed data and cannot, by
itself, support a 2x cost claim. Either comparison must include failed slots,
repairs, replacement executions, and the least-favorable approved setup and
human-rate inputs.

## Named reuse entry points

The inspected legacy path is
`bench/phase0-agent-native/run-pair.sh` plus
`bench/phase0-agent-native/harness-capture.py`.

### What can be reused as a reference

- `run-pair.sh` text-counts literal `[Fact]` and `[InlineData` occurrences in a
  task's `tests/*.cs` and rejects a task when that textual count is zero. The
  grep can count comments or strings and misses other theory data sources. It
  is not a semantic discovery of xUnit cases.
- It copies tests and an arm-specific shim into a directory outside the source
  working tree, creates a separate `HeldOut.csproj`, and references the built
  source assembly rather than the source project.
- After a successful source build, it invokes the held-out project with
  `dotnet test`, archives `.ho_final.txt`, and records final-build and held-out
  summaries in `result.json`.
- `harness-capture.py`'s `heldout-final` command recognizes three restricted
  textual result formats, then separates recognized failures matching
  hard-coded silence strings from those containing the single hard-coded
  `"Values differ"` value signature. Recognized failures with other details
  remain only in `failedTests`; an unrecognized but readable log can produce
  empty failure lists with `source: "log"`.

These are useful implementation examples for workspace separation, captured
logs, and deterministic result fields.

### Why this is not an established isolated evaluator

The directory placement is not an access-isolation boundary. `run-pair.sh`
launches the agent with `--dangerously-skip-permissions`, exposes the generated
shim directory through `PATH`, and writes absolute output/held-out paths into
that shim. An agent can inspect the shim and can potentially discover, read, or
modify evaluator assets. The legacy design relies on protocol behavior, not OS
or service-level separation.

The boundary is also embedded in an experiment runner that resolves arm
configuration from `pair.json`, materializes task-specific source/shims,
launches the agent, counts xUnit syntax with text matching, and constructs the
result record. It is not a separately authenticated acceptance service and
does not establish:

- immutable requirement/oracle ownership independent of the runner operator;
- authorization and role separation for submissions, evaluation, and release;
- a general artifact protocol for three language/workflow arms;
- tamper-evident submission sealing or accepted-artifact publication;
- evaluator versioning and compatibility outside the legacy pair layout;
- deterministic classification of every build/test infrastructure failure;
- production integration, privacy handling, or adopter support.

The parser also returns `source: "missing"` with empty failure lists when the
log is absent, including when the declared-done source did not build. The
runner carries `finalBuild.ok` beside that result to disambiguate it. Reusing
the parser alone would therefore create a success-shaped ambiguity.

**Reuse conclusion:** a candidate execution seam exists between the final
source build, the external held-out project, and the archived result fields.
No suitable isolated acceptance service has been established. Building one is
outside this screen.

## Resource worksheet

No resources below are approved. `TBD` means the required owner, quantity, or
rate is absent; it does not mean zero cost.

| Cost class | Work item | Engineering | Independent human | Compute | Owner | Dependency / reuse | Contingency and exit support |
|---|---|---:|---:|---:|---|---|---|
| Adopter setup | Domain, requirements, integration, onboarding | TBD | TBD | TBD | Adopter lead | No adopter or accepted matrix | Stop if scope/authority is absent; preserve exclusions |
| Adopter operating | Per-change specification, translation, review, repair, escalation | TBD per task | TBD per task | TBD per task | Adopter workflow owners | No measured workload or duration | Count failed slots and replacements; retain support through accepted-change closeout |
| B setup | Strong protected-C# design, integration, and training | TBD | TBD | TBD | B implementation owner | No approved comparator design | Remove no gate; archive integration and training obligations |
| B operating | Per-change enforcement, policy maintenance, and protected workflow execution | TBD per task | TBD per task | TBD per task | B workflow owner | No measured execution or maintenance load | Count enforcement failures, repairs, and recurring support |
| C setup | Protected-Calor integration, initial translation, and training | TBD | TBD | TBD | C implementation owner | Released compiler is a starting point, not a protected workflow | Include unsupported forms, onboarding, and model familiarity |
| C operating | Per-change translation, compiler/solver execution, refusal handling, and workflow maintenance | TBD per task | TBD per task | TBD per task | C workflow owner | No measured execution or maintenance load | Count translation repair, refusals, and recurring support |
| Study/R&D | Shared acceptance service | TBD | TBD | TBD | Independent service owner | Legacy held-out seam is reference only | Security, isolation, privacy, versioning, and support remain separate |
| Study/R&D | Mechanism and false-established work | TBD | TBD | TBD | Correctness owner | Existing tests and #1311 may supply bounded evidence | Counterexamples stop affected claims; zero findings do not prove soundness |
| Study/R&D | Task inventory, hidden oracle, severity rubric | TBD | TBD | TBD | Oracle owner | Historical supply unverified | Preserve cluster identity and pilot/final reservations |
| Study/R&D | Pilot, estimators, and joint sizing | TBD | TBD | TBD | Methods reviewer | No approved method or reviewer | Invalid/underpowered results stop; no favorable redesign |
| Study operations | Final collection and timed review | TBD | TBD | TBD | Study operator + review owner | No model pin, schedule, or capacity | Include replacements, invalid slots, and collection closeout |
| Handoff/support | Independent interpretation and adopter handoff | TBD | TBD | TBD | Independent reviewer + adopter | Both roles vacant | Publish limits and discharge adopter/participant support on stop |

The earlier 41-47 engineering-day decomposition, 30-day candidate ceiling,
80 non-maintainer hours, and 41-47-day planning range are proposals or critique
estimates. They are not booked capacity. No v0.18 funds or subscription
list-price equivalents are available to borrow.

## Review-slot and task-capacity sensitivity

The roadmap illustration treats one three-arm triplet as three timed reviews.
Before pilot reservations and other duties:

| Review minutes per arm | Triplets from 40 review hours | Triplets from 80 review hours |
|---:|---:|---:|
| 10 | 80 | 160 |
| 20 | 40 | 80 |
| 30 | 26 | 53 |
| 60 | 13 | 26 |

These are arithmetic scenarios, not available sample sizes. Pilot reservations
must be subtracted from both source-task supply and reviewer capacity. Repeated
reviews of a few source clusters do not create independent tasks.

Required task-count fields remain unavailable:

| Field | Current record |
|---|---|
| Eligible distinct historical source clusters | Unverified |
| Pilot task clusters and repetitions | Not registered |
| Final task clusters and repetitions | Not registered |
| Pilot reservations removed from final supply | Cannot calculate |
| Pilot reviewer hours reserved | Cannot calculate |
| Final reviewer hours available | Cannot calculate |

Adoption-volume cost reporting is also unevaluable until arm setup,
per-change operating cost, and correctly accepted fractions are measured:

| Required view | Current record |
|---|---|
| First-change cost | Cannot calculate |
| Unamortized total cost | Cannot calculate |
| Cost at 10 assigned changes | Cannot calculate |
| Primary cost at 50 assigned changes | Cannot calculate |
| Cost at 100 assigned changes | Cannot calculate |
| Observed break-even volume | Cannot calculate |

No setup bands, role rates, task inventory, review duration distribution, or
cost-variation ranges have been approved. Therefore no least-favorable
permitted combination can be evaluated. A formal inquiry must freeze those
ranges and pass each required C/B and B/A comparison at its least-favorable
combination; selecting central values is not an acceptable substitute.

## Reconciliation

Actual approved scientific resources are **none**. The repository screen found
limited reusable mechanics but no established authority boundary or isolated
evaluator. The cost mechanisms are plausible and falsifiable, but their
magnitudes remain unknown. The resource result is therefore an explicit gap,
not a favorable cost assumption and not a scientific NOT FEASIBLE verdict.
