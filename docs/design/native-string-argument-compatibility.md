# Native STRING argument compatibility (#1383)

N4 starts from accepted N3 main `8f9891a1a07f786a76a293017959c3edf28412bf`.
The implementation checkpoint is `f642b11aa5249dc1f9369195af25dcc881ce9f43`.
This is a diagnostic-ownership repair, not general Stage A activation or release
acceptance. Final evidence, two non-author reviews and parent adjudication
remain required.

## Matching and receiving checks

Ordinary native statement and expression calls may match concrete scalar
`STRING` and `STRING?` irrespective of the reference annotation. `str` and
`string` aliases canonicalize consistently. The selected overload's original
formal, argument names/modifiers, source order, optional arguments, params
mapping and effective parameter types remain authoritative. Reference
annotation compatibility has no conversion cost; existing underlying
conversion and optional/params scoring remains unchanged.

`TypeIdentity` equality and generic unification are not globally rewritten.
The internal resolver option is enabled only for ordinary calls, not constructor
creation/initializers or explicit synthetic constructor targets. The original
public Scope overload retains legacy matching. Ref/out/in parameters, generic
payloads (including generic parameters named like builtin string aliases),
nullable values, arrays in normal form and runtime Option retain their existing
applicability. Expanded params arguments use their actual scalar element
target, as required by N3. This is not array-container or element-transfer
completion; #1444 remains required.

After selection, the existing shared `NullabilityChecker` and severity helper
report0274 on the actual offending argument in either call form. No source
rewrite, unwrap, default, coalesce or throw is inserted. A non-null argument
and a nullable-accepting STRING parameter are compatible. New genuinely
ambiguous/incompatible calls retain0207/0208 rather than inventing a winner.

## Preserving the old public rejection

Before general Stage A, replacing a blocking208 with ordinary analysis-only274
would silently accept an unsafe input. N4 therefore replays the actual old
accessible overload resolver when the new winner has an annotation-different
scalar input. The same candidates, names, modifiers, explicit type arguments,
conversions and unresolved/invisible-argument suppression rules are used.

Only an actual old `NoMatch` or `Ambiguous` result that would have been reported
sets `ReplacesNativeOverloadError`. The ambiguity case matters: two previously
tied OBJECT overloads with optional parameters can lose to the newly applicable
STRING overload. It was not a previously accepted call.

Only0274 at `MethodArgument`/`ScalarString` with that provenance is a compilation
error. The ordinary eighteen receiving rules stay analysis-only. This is not
code-only activation: a previously applicable OBJECT overload or suppressed
unknown argument does not gain the override. The receiving shape, producer
annotation and actual old rejection are distinct facts.

The source-site golden now explicitly represents the one context-dependent
site and its receiving override. Existing API, CLI, verification and editor
consumers reuse `BindingDiagnosticPolicy`; no parallel routing allowlist is
introduced. API type/transpile opt-outs and tested CLI effect/typing modes
retain the existing rejection. LSP labels distinguish the active handoff
(`calor`) from ordinary analysis (`calor (analysis only)`).

## Evidence boundaries

Controls cover native and actual BCL nullable producers, aliases, both call
forms, named/reordered inputs through two inferred locals, expanded params,
OBJECT competition, old ambiguity, unknown-argument suppression, true
incompatibility, Option Some/None, generic identity and by-reference/constructor
exclusions. Runtime controls preserve null/non-null values at nullable targets
and the existing CLR STRING-over-OBJECT selection. The latter is deliberately
not evidence that all nullable STRING inputs are rejected before Stage A.

The initial native-producer fixture used bare `return null` and additionally
reported raw0200 for `null`; it did not isolate the intended receiving check.
The final fixture returns an actual nullable BCL result through a native
nullable return. No bare-return-null repair or guarantee is claimed here.

Corpus changes and private metadata reference selection must be individually
measured and classified; an absent diagnostic or preserved visit count is not
a safety result. Non-null-to-nullable-accepting compatibility may remove an old
false-positive208 without adding274. Such changes are not equivalent to an
unsafe formerly rejected input becoming accepted.

The [N4 evidence record](../plans/evidence/native-string-1383/README.md)
classifies the two changed source rows, distinguishes propagated diagnostics
from affected-module counts, and retains actual default API/CLI controls,
realized-versus-manifest reference profiles and failed attempts.

Stage A activation and shipped-CLI acceptance remain #1385/#1386. D1, Stage B,
real arrays, general writes/flow, proof-demotion/runtime-guard changes, public
release claims and the paused research experiment are not completed here.
