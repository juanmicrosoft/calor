# Unregistered PP-W-rows task candidates (#1256)

**Authoring only. No final task count, task freeze, registration, collection
authorization, or agent observation.** Three candidate workflows currently
survive the author's checks under the source-available abstraction premise
of frozen redesign §1.4. All three use **one** registered compiler shape:
#1136 row 7, direct invocation of a `this.`-qualified delegate field.
They are not three shapes or independent mechanisms.

The [frozen redesign](../../../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md)
and [reviewed buildability spike](../../buildability/1255/README.md) govern this
work. The existing A-1.12 pairs, registrations, epochs, and benefit ledger
remain unchanged.

## Gate status

- #1256 requires the final task count to be selected **with #1261 sizing**.
  No approved sizing amendment exists. Three is the current candidate
  inventory, not an adopted sample size or an assertion that the floor alone
  makes a sound study. Repeated-shape dependence and limited coverage must
  enter that decision.
- #1266's [independent AI second-reader checkpoint](second-reader-1266.md)
  is complete for this current inventory. All three candidates are retained;
  all six earlier rejections stand. The issue imposes no human qualification,
  and this review does not claim a human methods countersignature.
- #1257 has not frozen these suites. They remain editable authoring inputs.
- #1264 owns collection layout and execution. These directories follow its
  neutral per-task structure; source-fragment assembly and shape-control
  integration must be verified before claiming end-to-end readiness.
- No monetary ceiling, null-result acceptance, participant approval,
  model selection, run count, or registered epoch is supplied here.
  Unfunded prospective planning/registration is authorized work; missing
  funding prevents collection, not that preparatory work.

## Candidate inventory

| Candidate | Work to implement | Shape | Honest alternative |
|---|---|---:|---|
| [Quota adapter](C-001-quota-adapter/spec.md) | Apply a clamped quota schedule | 7 | Discover the dependency's calculation function |
| [Shipping comparison](C-002-shipping-quote/spec.md) | Compare quotes whose cheaper zone changes with weight | 7 | Use the underlying calculation for both zones |
| [Ordered fingerprint](C-003-frame-fingerprint/spec.md) | Normalize two keys separately and combine them in order | 7 | Discover the underlying normalizer |

These are deliberately hand-authored, reduced engineering fixtures, not
sampled production tasks or collected agent solutions. Shipping exercises
both ordering branches and the tie; fingerprint exercises asymmetric inputs,
normalization boundaries, and order; quota exercises both clamp boundaries.
Repeated requests reuse the same instance.

Each task has byte-identical A/B starters and corresponding seed variants.
`dependency.calr.inc`, a newline, and `task.calr.inc` concatenate into one
complete Calor compilation unit. They are **not separate modules**. The
dependency and editable method remain separate agent-facing files.
The `.calr.inc` suffix avoids registering incomplete fragments as standalone
formatter-corpus inputs.

The neutral policies are `calor-permissive` and `calor-strict`, using the
same actual v0.18.0 release, commit
`514f538024df990af86054af25975b756ba42ab1`. Only A passes
`--permissive-effects`; B passes no effect-policy flag. This is an unresolved
call's `unknown` treatment, **not** propagation of the dependency's named
`mut` row or evidence about a separate polymorphic-row mechanism.

`smoke/` contains only numeric and repeatability assertions. `tests/` contains
the held-out state observers. They assert the numeric value first; only an
actual subsequent before/after inequality emits
`HELDOUT_EFFECT:state-change`. A numeric failure does not qualify.
The tiny required shim files contain no adapter implementation: tests call
the compiler-generated public API directly.

The laundering seeds are shape-positive. Starter and honest seeds are
shape-negative. **An honest seed must not be mislabeled as a positive shape
control merely because a historical instrument called its control `clean`.**
The declared source patterns recognize the direct call, not just the field's
continued presence in the dependency.

## Scope of R1 and the information surface

The convenient route is the library's documented standard callback. The
honest route needs discovery of an underlying pure implementation in the
separate dependency. Its extra step is **inspection and substitution**, not
more keystrokes. The spec, visible names/assertions, actual A diagnostics,
and passing-test output do not identify the dependency mutation.

The dependency's source and row, and the caller's empty row, remain readable.
This is the frozen §1.4 abstraction premise, not physical secrecy, proof of
agent ignorance, or a measured claim about which file an agent will open.
If that premise is rejected, these candidates do not become valid by hiding
warnings or suppressing output. The same limitation was explicit in #1255.

No agent has been given these files. Committed held-out suites support
reproduction; publication is not an isolated acceptance service. Any future
agent workspace must exclude `pair.json`, author/reviewer notes, `seeded/`,
the held-out suite, and observation artifacts from its delivered task
surface. Source-available library code is a different boundary.

## Checks and rejections

Each task's `author-checks.md` records R1–R8 individually. The separate
[second reader](second-reader-1266.md) has now examined every candidate and
rejection; this is still not a frozen task-selection list.
The [rejection record](rejections.md) retains all six rejected #1255
candidates, the standard-spelling survey, and the authoring corrections.

The committed [local observations](evidence/results.json) use the current
source bytes, neutral per-task paths, and compatible state marker. They
produced 18 compiler invocations:
starters and honest seeds compile cleanly on both arms; laundering compiles
cleanly on A and rejects on B with the intended `unknown` diagnostic.
The three laundering A seeds passed 26 visible checks and failed six
state assertions after correct values; honest A/B seeds passed 64 checks.
The starter negative controls contributed 10 passing and 54 failing numeric
checks, with no state-failure signature. In total, 160 runtime checks actually
executed, alongside all 26 frozen row-table tests. These are deterministic
test executions, not agent trials. Rejected B laundering was not executed.

Every CLI invocation retains full stdout/stderr; each runtime suite also
retains raw TRX. The source hashes, exact binary hash, SDK, resolved package
versions/content hashes, disabled ancestor imports, and normal console logger
are recorded and cross-checked by tests. No manually translated C# or paid
runner was used. This local assembler is not the collection harness;
end-to-end #1264 workspace integration remains outstanding.

From the repository root, using an already-built, clean frozen checkout:

```bash
python3 bench/phase0-agent-native/task-candidates/1256/verify.py \
  --compiler-root ../ppw-v018 --output .candidate-verification
python3 bench/phase0-agent-native/task-candidates/1256/survey.py \
  --compiler-root ../ppw-v018 --output .candidate-spelling-survey
python3 -m unittest discover -s bench/phase0-agent-native/tests \
  -p test_ppw_candidates.py
```

Output directories must be new and inside the worktree. `verify.py` does not
rebuild the shared compiler. It checks the release, clean tracked source,
binary parity with the existing row-table test product, version, and Z3
assets, then uses `--no-build` for that existing instrument.

These executable prerequisites do not freeze #1257's suites or finalize
#1258's selected-task denominator. Final selection and formal freeze remain separate work. No collection
authority or benefit result is inferred from this authoring inventory.
