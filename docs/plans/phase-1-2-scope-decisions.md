# Phase 1/2 — Scope Decisions Not Captured in v6 Implementation Plan

This document records two small scope decisions taken during
implementation of `feature/compact-ids-v6` that were left open in
`path-2-drop-ids-v6-implementation.md`. Each decision references the
concrete code/test paths it affects so a future reader can verify the
reasoning.

---

## §1 — PR-1e / PR-1f: samples and tests are already v6-compatible

**Decision:** No corpus migration is required for `samples/` or for
the `*.calr`/`*.calor` fixtures under `tests/`. PR-1e and PR-1f, as
described in v5/v6 plans ("apply the Phase 1 migrator across the
sample corpus / the test corpus"), are **no-ops on this repository at
the time of writing**.

### Why

The Phase 1 dropper (`StructuralIdDropper`) only removes
`{<prefix>_<payload>}` ID blocks where the `payload` portion looks
like a real production ID. The detector
(`AttributeHelper.LooksLikeId`) accepts exactly two payload shapes:

1. 26 characters (legacy ULID), or
2. 12 characters drawn from the compact Crockford-lowercase alphabet
   (`0123456789abcdefghjkmnpqrstvwxyz`).

Short test IDs in the codebase — e.g. `m001`, `f001`, `f1`, `l1` — do
not match either shape, so the migrator does not touch them. The same
test IDs are also accepted by the v6 parser's compact-opener path:
they are not stripped, but they are not invalid either.

### Verification

Restricted to file types the migrator actually targets:

```
$ rg --pcre2 -l '\{(m|f|c|i|p|mt|ctor|e|op)_[0-9A-HJKMNP-TV-Z]{26}\}' \
     -g '*.calr' -g '*.calor' samples tests
# empty
```

No `.calr` / `.calor` files in `samples/` or `tests/` carry a
ULID-shaped structural-ID block for the Phase 1 dropper to remove or
a 26-char ULID payload for the Phase 2 migrator to rewrite. Some C#
test sources (notably `tests/Calor.Ids.Tests/*.cs`) embed ULID-shaped
strings as input to unit tests that exercise the legacy
`IdAssigner`/`IdValidator` paths — those strings are test fixtures
for the legacy code path, not migration targets, and the migrator
correctly does not touch `.cs` files.

### Implication

If a future contributor lands a sample or test with a production-shape
ID (e.g. by pasting in real generator output), PR-1e/1f should be
re-evaluated against the new corpus. Until then they remain no-ops
and are not blockers for shipping Phase 1 or for kicking off the §10
gate. The opt-in `Calor0820` lint (`LegacyStructuralIdLint`) will
surface any such IDs that slip in.

---

## §2 — PR-2f: revert lives in `CompactIdMigrator`, not a standalone file

**Decision:** The Phase 2 migrator's revert path is implemented as a
static method on `CompactIdMigrator` (in `Migration/CompactIdMigrator.cs`)
rather than as a separate `CompactIdReverter.cs` file as suggested by
v6 §2.3's filename hint.

### Why

The forward path and the reverse path share the same source-scanner
data structures and the same mapping-log format. Splitting the
reverter into a separate file would either:

- duplicate the scanner code, or
- expose internal scanner helpers as a public API solely so the
  reverter could call them.

Both are worse than the current arrangement, which keeps mapping-log
producer and consumer in one file. The test surface
(`EndToEndMigrationTests` exercises the round-trip end-to-end) is
identical to what a split would provide.

### Implication

If `CompactIdMigrator.cs` grows beyond ~500 lines, or if a third path
(say, an "apply mapping log without rescanning source") is added, the
split becomes justified. Until then, the deviation from v6 §2.3 is
intentional and recorded here.

The CLI surface (`calor fix --compact-ids --revert --log <file>`) is
unchanged either way; this is purely a file-layout decision.
