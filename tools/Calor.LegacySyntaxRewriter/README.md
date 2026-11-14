# Calor.LegacySyntaxRewriter

One-off 0.30 migration tool: rewrites legacy `§`-sigil Calor (rejected by the lexer since
`9d467400b`) into today's native syntax. It is not part of `Calor.sln`, is not packed, and
nothing in the product references it.

## How it works

```
legacy text ──(vendored pre-cutover Lexer/Parser, Legacy/)──▶ today's AST
            ──(today's Migration.CalorEmitter)──────────────▶ native text
```

`Legacy/` holds `Lexer.cs`, `Parser.cs`, `Token.cs`, `AttributeHelper.cs`,
`OperatorSuggestions.cs` and `SectionMarkerSuggestions.cs` from commit `0397bd3a0` (the last
commit before the cutover), moved to the namespace `Calor.Compiler.LegacySyntax` and compiled
against today's `Calor.Compiler` AST. Changes to the vendored files are limited to: the
namespace, dropping the enums/records that the AST now owns (`IntegerLiteral*`,
`FloatLiteralInfo`, `InterpolationPartIntent`), and `LegacyDiagnosticShims.cs` (today's
`DiagnosticBag.ReportUnexpectedToken` takes today's `TokenKind`). The compiler grants
`InternalsVisibleTo` to this assembly because the AST is built through internal members.

Before printing, the legacy module gets an explicit namespace scope
(`module Name Name block`): the legacy C# emitter wrapped a module in a block namespace
named after it, while a native header without a namespace means the global namespace. The
un-normalized tree is tried as a fallback. Two textual variants of each printed text are
tried: as printed, and with the printer's `System.Console.Write[Line](` and
`new System.Collections.Generic.` qualifications removed (legacy `§P` emitted
`Console.WriteLine` under `using System;`).

## The oracle (mandatory; nothing unverified is ever written)

A candidate native text is accepted only if all of these hold:

1. It lexes and parses with today's compiler with **zero diagnostics** (including raw C#
   interop reason validation).
2. **C# equivalence.** Today's `CSharpEmitter` is run on a fresh legacy AST and on the native
   AST. The two outputs must have the same Roslyn token sequence (kind and exact text, so every
   literal is compared exactly; directive trivia included). Whitespace and comments are
   ignored, empty namespace declarations are dropped, and contract-violation metadata that
   necessarily changes is masked: the function id argument (legacy ids such as `f001` cannot be
   written in native source; tooling owns identities) and the `startOffset/length/line/column`
   source position.
3. **Effect equivalence.** Every effect row in both trees (callable rows and type rows,
   traversal order) must match. The only tolerated difference is a public function/method
   with no legacy row versus native `!`: today's enforcement reads both as Pure, and native
   syntax requires the explicit row on a boundary.
4. Every plain `//` comment in the legacy text still appears in the native text (the AST
   does not carry plain comments, so files with them are skipped rather than stripped).

If the oracle fails, the input is left byte-for-byte unchanged and logged with the reason.

## Usage

Every command is a dry run unless `--write` is passed. `--log` writes a TSV of every input
considered (status, path, line, reason, detail).

```bash
dotnet build tools/Calor.LegacySyntaxRewriter
R="dotnet tools/Calor.LegacySyntaxRewriter/bin/Debug/net10.0/Calor.LegacySyntaxRewriter.dll"

# .calr files (recursive). Registered ActiveSyntax originals are skipped unless
# --include-registered is given.
$R calr --log calr.tsv tests
$R calr --write --include-registered samples

# Legacy Calor inside C# string literals (test sources)
$R cs --log cs.tsv tests/Calor.Enforcement.Tests
$R cs --write tests/Calor.Enforcement.Tests

# Debugging one input
$R probe file.calr          # native text, or the oracle failure (REWRITER_DUMP=dir saves both C# texts)
$R legacy-cs file.calr      # C# from the legacy parse
$R native-cs file.calr      # C# from today's parse
```

### C# literal mode

Roslyn finds regular, verbatim and raw (single- and multi-line) string literals whose value
contains `§`. Each value is rewritten and re-encoded in the same literal kind (raw literals keep
their closing-delimiter indentation and quote count; the value's newline convention and
trailing newline are preserved) and the encoded literal is decoded again and compared with the
intended value. A `+` chain of plain string literals is rewritten as one unit and re-emitted
one line per literal with the original operator placement. Values that are not complete
modules go through `FragmentRewriter` (wrap in a minimal module, rewrite, unwrap), still under
the full oracle. Skipped and logged: interpolated strings, chains with non-literal operands,
and every literal in a member that tests legacy rejection (its text mentions `Calor0003`,
`Calor0006`, `Calor0007`, `Calor0830`, the corresponding diagnostic names, "Legacy section
marker", "is removed", "retired", "backtick" or "legacy"; for a field or property, the members
that use it are checked too).

### Never touched

`bench/phase0-agent-native/`, `docs/plans/`, `docs/design/spikes/`,
`tests/TestData/ActiveSyntax/`, `tests/**/Snapshots/*.approved.calr`, `bin/`, `obj/`, and
(unless `--include-registered`) every path listed in
`tests/TestData/ActiveSyntax/inputs.json` `originalPaths`.
