# Edit-script corpus (F-3)

The registered denominator for the **full-vs-incremental identity gate**
(roadmap-v0.13-v0.15 §2.5 gate 2, unconditional diagnostics leg).

The claim under test: **compiling an edit sequence incrementally produces
byte-identical diagnostics to compiling each of its states from scratch** — and
does so while actually reusing the cache, not by quietly rebuilding everything.

Harness: `tests/Calor.Compiler.Tests/EditScriptIdentityTests.cs`.

## Layout

```
ES-NN-name/
  script.json                 registration: id, title, what it pins, ordered steps
  step-00-<label>/            the COMPLETE file set at this step
  step-01-<label>/            ...
```

Each step directory is the whole workspace at that point, not a patch. A file
that disappears between steps is a deletion; one that appears is an addition;
one whose bytes differ is an edit. Deletions and additions are therefore
expressible without a patch format, and every state is directly readable and
diffable.

`script.json` carries an `options` profile per step, resolved by the harness:

| profile | compiler options |
|---------|------------------|
| `effects-on` | `EnforceEffects = true` |
| `effects-off` | `EnforceEffects = false` |
| `docs-required` | `EnforceEffects = true, RequireDocs = true` |

Option profiles are what make a *non-file* input change expressible, which is
what ES-05 needs. `docs-required` additionally gives ES-07 a **per-file**
diagnostic (`Calor0601` on an undocumented public function), which behaves
differently from a cross-module one under caching.

## What each script pins

| id | shape | the failure it would catch |
|----|-------|---------------------------|
| ES-01 | local edit | an edit leaking into an unrelated file's diagnostics |
| ES-02 | file added | a new file's findings not reaching the report |
| ES-03 | file deleted | a removed file's findings surviving in the cache |
| ES-04 | callee's effects change | the **unedited** caller's cross-module violation (Calor0410) served stale from a cached effect summary — including the disappearing direction |
| ES-05 | option flipped | the #788 / #883 shape: a diagnostics-affecting option outside the options token, so a warm cache answers for the previous option set |
| ES-06 | identical rewrite | diagnostics moving when nothing changed; also the anti-vacuity check that "identity" is not bought by rebuilding everything |
| ES-07 | finding on an unedited file | a **per-file** diagnostic vanishing on warm builds, if diagnostic-bearing files were ever cached |

ES-04 and ES-07 look similar and are not. Cross-module diagnostics (ES-04) are
recomputed from cached effect summaries on every run, so they survive a file
being skipped by construction; per-file diagnostics (ES-07) are emitted only by
an actual compile, so they disappear if the file is skipped. Both directions
need a script.

## Reuse expectations

Each script registers `expectsReuse`, asserted **two-sided**: a script that
should reuse the cache must, and a script that should rebuild everything must
not. Three scripts rebuild everything by design, and that is worth knowing:

- **any change to the file set** (ES-02, ES-03) moves the cross-module function
  map hash, which is compared unconditionally (#823 review m1), so adding or
  deleting one file invalidates the whole cache;
- **any change to the options token** (ES-05) invalidates globally (#788).

Registering those as expectations turns them into tested claims: if global
invalidation ever narrows, the affected script fails and says so, rather than
the gate quietly passing on a weaker guarantee.

## Adding a script

Add a directory and a `script.json`; the harness enumerates the corpus, so no
test code changes. Two rules:

1. **A new script must state, in `pins`, the failure it would catch.** A script
   that cannot fail adds runtime, not coverage.
2. **Scripts are not removed to make the gate pass.** `RegisteredScriptIdsAreStable`
   pins the id set, so shrinking the denominator is a deliberate, reviewable edit
   rather than a quiet one.
