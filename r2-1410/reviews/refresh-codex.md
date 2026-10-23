# R2 #1410 — refresh onto main (2026-10-06), integration review (Codex)

Reviewer: codex-cli, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, one pass.
Input on stdin: the new #1474 verifier commit (`df532179`), the `--cc` diff of the #1475 merge
resolutions (`40ca26d8`), the post-merge fix (`f45c6bed`), the resulting `benchmark.yml`, the
`publish-nuget.yml` header, and the full workflow diff against `origin/main` (`aa23d589`).
Scope (hostile): every publication surface still requires the adjudication identity and, for
benchmarks, B2's (#1422) refusal gate; no main-side protection (B2, G1 #1471, P1 #1472) was
dropped; B2's cleanup step stays last; the benchmark allow-list admits exactly B2's two outputs;
`determinism-protocol.yml` (G2), `tier2.yml`, and `test.yml` contain no ungated publication.

## What the refresh changed

- `eng/test-manifest.json`: resolved with `resolve_manifest.py` both times. The script carries
  only per-project counts and notes, so it dropped R2's `releaseGates` entry `adjudication-gate`;
  that one line was restored by hand after each run. Calor.Compiler.Tests: main 12790 -> 12873
  (#1474: +81 original, +2 below) -> 12894 (#1475: +21).
- `publish-nuget.yml`: R2's top-level `permissions`/`concurrency`/`env` block kept; G1's removal
  of `Z3_VERSION` kept (nothing references it). G1 `bootstrap-z3` and the packaged-Z3 check, and
  P1's full-history checkout and "Fetch protected main and tags" steps, merged in cleanly into
  R2's candidate-checkout jobs.
- `benchmark.yml`: R2's dispatch-only identity gate and candidate checkout come first. B2's
  diagnostic discard, B1 regeneration, refusal gate (`methodology`, `report` output), and
  candidate upload follow unchanged. R2's verified publishing step replaces B2's
  `create-pull-request` step, is dispatch-only (push runs never publish), runs only after the
  refusal gate passed, and uses B2's PR-body wording plus the gate report. B2's `failure()`
  cleanup stays the job's last step. B2 removed `allow_weaker_methodology` (R2's env-var form of
  it is dropped) and the agent-refactoring commit step (R2's gate inside it is dropped with it;
  the job is read-only). R2's `fetch-depth: 0` in the agent job was dropped as no longer needed.
- Verifier (#1474, `df532179`): the record's `benchmark-results` surface must name exactly
  `website/public/data/benchmark-headline.json` and
  `bench/phase0-agent-native/commit-stamp-index.json` (`G011` otherwise). The fixture models
  those two files. Two new negative controls (extra file with a matching hash; stamp index
  missing), both mutation-checked.
- `f45c6bed`: R2's comment above the publishing step mentioned `create-pull-request`; B2's
  structural test splits steps on `- name:` and attributed it to the `always()` upload step.
  Comment reworded; no test changed.

## Codex output (final message, verbatim)

**MAJOR — A successful benchmark publication makes the same adjudication identity fail on retry.** B2 reads the headline from `origin/main` and embeds `comparableWithPublished` and `comparison` in its generated output (`scripts/benchmark_publication_gate.py:334`, `:352`, `:393`). Before the first headline merges, these describe “no headline”; afterward, they describe an unchanged, comparable headline.

R2 always regenerates from the detached candidate (`.github/workflows/benchmark.yml:92`, `:274`) and checks the output against the frozen adjudicated hash (`:316`; `scripts/verify_release_adjudication.py:409`, `:472`). Therefore, after the publication PR merges, redispatching the **same identity** fails with `G011`, despite identical candidate, provenance, results, and freshness inputs. The no-op/retry handling is never reached; failure cleanup runs instead.

A read-only probe reproduced different headline hashes with only main’s published-headline state changed. Keep mutable comparison information in the gate report, or reuse the frozen publication bytes while separately checking current B2 conditions.

The remaining requested checks passed:

- **Publication ordering:** NuGet, attestation, release creation/assets, Pages deployment, and installed-tool verification retain adjudication gates. Benchmark publication follows both gates and uses ordinary success-dependent execution (`publish-nuget.yml:392`, `:469`, `:478`, `:506`; `nextjs-gh-pages.yml:126`, `:138`; `verify-release.yml:47`, `:63`; `benchmark.yml:270`, `:306`).
- **Main protections preserved:** diagnostic reset/clean, B1 regeneration, refusal/report, concurrency, validated statistical inputs, and the read-only agent job remain (`benchmark.yml:58`, `:116`, `:241`, `:248`, `:270`, `:415`). Z3 bootstrap and packaged checks remain (`publish-nuget.yml:418`, `:443`). P1 fetch steps and full history remain (`publish-nuget.yml:117`, `:121`, `:279`, `:283`).
- **Cleanup is last:** `benchmark.yml:366–378`, immediately before the next job.
- **Exactly two permitted outputs:** schema validation rejects additional or missing keys (`verify_release_adjudication.py:389`). B2 rejects untracked extras and rename sources before publication (`benchmark_publication_gate.py:312–329`); focused probes confirmed both.
- **Other workflows:** `determinism-protocol.yml`, `tier2.yml`, and `test.yml` are unchanged versus main and contain no release publication path. Actions evidence uploads are explicitly excluded from publication by the contract (`docs/plans/v0.24-evidence-contract.md:336`).
- **Candidate interaction otherwise holds:** B2 accepts an ancestor checkout when its freshness inputs equal main’s (`benchmark_publication_gate.py:302–309`). Reset/clean targets the candidate’s `HEAD`, preserving that identity (`benchmark.yml:244–245`).

Seven read-only checks passed. Full .NET and temporary-repository suites were not run in the read-only workspace.

VERDICT: 0 BLOCKING, 1 MAJOR, 0 MINOR.

## Disposition

- **MAJOR (re-dispatch after a successful benchmark publication fails `G011`): accepted as
  fail-closed; not fixed here.** B2's headline embeds `comparableWithPublished`/`comparison`,
  which depend on the headline already on `origin/main`. After the first adjudicated headline PR
  merges, regenerating for the same identity yields different bytes, so R2's hash check refuses.
  Nothing publishes wrongly; the run fails red, and B2's cleanup finds no open
  `benchmark-results*` PR to close. This is a case of the byte-equality open item already in
  `release-gate.md` ("Byte equality"): #1423/#1424 must adjudicate the headline bytes as they
  will be generated against `main` at publication time, or retain them. Changing B2's output
  format or R2's comparison is a design change outside a refresh, so it is recorded for the
  maintainer rather than made here.
- All other checks passed (ordering, main-side protections, cleanup last, exact two-file
  allow-list, no publication in `determinism-protocol.yml`, `tier2.yml`, `test.yml`).

**Result: 0 BLOCKING open.** One pass, per the refresh brief; no second pass was needed.
