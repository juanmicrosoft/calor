# F2 #906 — Codex adversarial review, round 2

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced.
- Input: `git diff origin/main...HEAD` at `840dd3f1` (round-1 fixes).
- **Verdict: NOT ACCEPTABLE** (3 BLOCKING, 1 SHOULD-FIX).

## Findings and dispositions

| # | Severity | Finding (Codex) | Disposition |
|---|---|---|---|
| R2-1 | BLOCKING | `int n=2; FormattableString f=$"{1}\|{n}";` generated `$"${{1}}\|{n}"`: a digit-only hole `${1}` reads back as a literal `{0}`-style placeholder, so one FormattableString argument is lost. `FormattableString f=$"{1}";` failed with CS0030. | Fixed. The Calor emitter writes a digit-only int hole as `${INT:1}` (alignment and format suffixes still parse). The same placeholder rule applies to `string` targets, which the witness also covers (not separately measured on `origin/main`). Witness: shapes case 6 (`{1}`, `{3,4}`, `{7:D3}` through FormattableString and string). |
| R2-2 | BLOCKING | `$"{Kind(1.5m)}"` with `Kind(decimal)`/`Kind(double)`: the Calor emitter writes `DEC:1.5`, which reads as a C# named argument inside a C#-form call, so the double overload ran (or CS1739). | Fixed. In holes, numeric literals are native only as `int` or as a `double` with a fractional part; decimal, float, long, unsigned and integral-double literals keep the hole as C# (`§CS{...}`). Witness: shapes case 7 (`Num(1.5m)`, `Num(2.0)`, `Num(3L)`, `{4.0}`, `x2(2.0)`). |
| R2-3 | BLOCKING | Native Calor `"${String.Concat("\x41")}"`: lifting produced `String.Concat("\\x41")` (four characters), while the raw C# call had read `"A"`. | Fixed. A literal argument is lifted only when Calor and C# read the same value (string, char, int, bool); prefix-unary arguments only over a name or an int literal. Witness: `NativeHole_StringArgumentWithACSharpOnlyEscape_StaysRaw`. |
| R2-4 | SHOULD-FIX | `string-interpolation-formattable` claimed `Full` though hole-free targets are preserved. | Fixed. It is `Partial`, with the hole-free and hole-subset limits named; CHANGELOG and website say "with at least one hole". |

## Discovered outside F2 (not fixed here)

While checking R2-2, the implementer found that the converter writes an integral `double`
literal as an int: C# `double r = x / 2.0;` converts to `§B{f64:r} (/ x 2)` and compiles to
`double r = x / 2;` (integer division), with no loss reported. `CalorEmitter.Visit(FloatLiteralNode)`
uses `double.ToString(InvariantCulture)`, which drops `.0`. This is a silent semantic change
outside interpolation. Inside holes, F2 avoids it by keeping integral doubles as C#. It needs
its own issue.
