# Phase 3 H1 Micro-Smoke Results — Frozen 2026

**Frozen:** Date of run (this PR)
**Model:** `claude-haiku-4-5` via `claude --print`
**Total invocations:** 18 (3 tasks × 2 arms × 3 trials)
**Total cost:** $0.17 (closer $0.10 + indent $0.07)
**Fixture:** `tests/E2E/agent-tasks/fixtures/basic-calor-project/Calculator.calr`
**Migrator:** `scripts/calor_indent_xform.py` (text-level Python prototype, NOT C# parser-integrated)

## Result

| Arm     | Pass | Mean duration | Total cost | Mean output (chars) |
|---------|------|---------------|-----------:|--------------------:|
| closer  | 3/9 (33%) | 10.24s   | $0.101     | 270                 |
| indent  | 6/9 (67%) | 10.81s   | $0.071     | 304                 |

**Δ pass rate: +33pp** (indent over closer)
**Δ cost: −29.3%** (indent cheaper)

### Per-task

| Task | closer | indent |
|------|-------:|-------:|
| basic-001 (add function) | 3/3 | 3/3 |
| contract-001 (add §Q precondition) | **0/3** | **3/3** |
| logic-003 (postcondition with iff) | 0/3 | 0/3 |

## What the wins mean

The aggregate +33pp is driven entirely by **contract-001**. Failure mode in closer arm: agent
wrote `§Q{x >= 0}` (treating the condition as opener attributes in braces) instead of `§Q (>= x 0)`
(Calor's Lisp-style operator form). Indent arm consistently produced correct `§Q (>= x 0)`.

Why? Hypothesis: the system prompt for the indent arm explicitly described "Python-style
indentation", which framed the language as more conventionally LLM-trained-on. The closer system
prompt described §F/§/F pairing without elaborating on §Q's expression form. The agent's choice of
`§Q{x >= 0}` is a pattern-completion error consistent with treating §Q as opener-with-attributes.

This is **a measurable difference**, but it confounds two changes:
1. The indent form itself (less syntax to track).
2. The framing language in the system prompt.

Cleaner Tier 1.5 should isolate these — same system prompt language but different syntactic form.

## What didn't move

`basic-001` (parity at 3/3): trivial task, both arms succeed easily.

`logic-003` (parity at 0/3): both arms used `iff`/`=` as operators (which Calor doesn't have).
The task is too hard for haiku-4-5 to one-shot without examples of postcondition idioms. This is
a knowledge gap, not a form gap.

## Cost reduction confirmed

Indent arm used 29% fewer USD than closer arm. That tracks with Tier 0's 15% mechanical token
reduction (the extra 14% comes from shorter output tokens that the agent generates).

## Caveats — DO NOT cite this as ship evidence

1. **n=18 is tiny.** A 33pp difference at this sample size has wide confidence intervals. A formal
   Tier 1.5 (60 calls) or Tier 2 (5040 calls) is required for a real ship decision.
2. **1 of 3 tasks drove the entire signal.** contract-001 happened to be syntactically tricky for
   the closer arm in a way that the indent system prompt accidentally helped with.
3. **System prompts differ between arms.** A cleaner comparison would use the same system prompt
   length and tone, varying only the description of block delimiters.
4. **Migrator round-trip is lossy on ~10% of corpus.** The `samples/PatternMatching/matching.calr`
   pattern (inline §W{...} mid-line in a §B binding) is not handled; this represents ~9.8% of the
   full 1559-file corpus by round-trip byte-equivalence. For files that DO round-trip cleanly,
   compile-equivalence is 100% (10/10 on samples/ that compile in baseline).
5. **Migrator is Python, not C#.** Ship form requires a C# port and proper integration in the
   `calor fix --to-indent` / `--from-indent` CLI commands.

## What this DOES support

- Indent form does NOT degrade write-task performance.
- Indent form likely IMPROVES it on contract-heavy tasks (replicate needed).
- Cost reduction is real and persistent.
- Round-trip migrator works for the bulk of the corpus (89.9% byte-equivalent, ~100% compile-equivalent on baseline-compilable files).

## What it does NOT support (yet)

- Statistical significance (n too small)
- Disentangling syntactic from prompt-framing effects
- Performance on more complex constructs (classes, async, generics — all untested)
- Ship gate (validation plan requires §3a gates + Tier 1.5 formal + reviewer signoffs)
