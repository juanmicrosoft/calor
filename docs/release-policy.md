---
layout: default
title: Release Safety Policy
nav_order: 12
---

# Release Safety Policy

## Formatter write-path gate

`calor format --write`, `calor lint --fix`, and LSP formatting may ship enabled
only while the issue #760 gates remain green:

- lossless preservation of comments, documentation comments, blank-line intent,
  strings, raw C#, user identifiers, types, and member targets;
- exact semantic-token equivalence and formatter idempotence across every
  checked-in parseable `.calr` fixture;
- byte-identical generated C#, Roslyn-clean compilation, and identical public
  API for supported inputs;
- original encoding/BOM and per-line newline preservation;
- same-directory temporary write, revalidation, flush, and atomic replacement;
- failure injection proving the original remains byte-identical;
- an applied LSP formatting edit that recompiles.

No formatter may use a generic regex to infer or rewrite identifiers. Structural
ID migrations belong to dedicated commands that explicitly classify structural
marker kinds, such as `calor fix --compact-ids`.

If any gate regresses, CLI and LSP writes must fail closed. Parseable inputs that
already have semantic or generated-C# errors may use the documented conservative
fallback: report unsupported, change no bytes, and return non-zero.
