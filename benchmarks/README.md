# Bug-detection subjects

These seven subjects contain 25 deliberately vulnerable cases and 25 safe controls.
They are analysis inputs, not programs to execute against real services: security
cases include shell execution, file writes, and SQL sinks. SQL runtime regression
tests use only an injected recording provider; do not run the security subjects
against a real shell, filesystem, or database.

Run their executable regression coverage with:

```sh
dotnet test tests/Calor.Compiler.Tests/ --filter FullyQualifiedName~BenchmarkSubjectRegressionTests
```

The tests read the actual files, require successful parsing and binding, and check
every function's unchanged `EXPECTED` declaration at its source location. There
are no parse-error early returns or missing-file skips. "No warnings" means no
finding in that subject's bug category, not an absence of unrelated diagnostics
(for example, unchecked division can also overflow).

All **25 vulnerable cases produce their declared diagnostic**. Twenty safe
controls have no finding in their category. The remaining five are explicitly
pinned false-positive limitations, not successful safety proofs:

| Subject | Safe control | Current finding | Limitation |
|---|---|---|---|
| command-injection | SafeEscapedCommand | Calor0982 | Taint propagation does not prove POSIX single-quote escaping safe. |
| path-traversal | SafeSanitizedPath | Calor0983 | The analyzer does not prove that basename extraction plus rejection of `.` and `..` confines the path. This assumes trusted parent directories, not protection against symlink attacks. |
| overflow | SafeSmallValues | Calor0923 | Numeric precondition ranges are not imported into the typed CFG state. |
| overflow | SafeBoundedShift | Calor0923 | Same precondition-range limitation. Both operand and shift count must be bounded; bounding only the shift count was not safe. |
| bounds-violation | SafeWithPrecondition | Calor0921 | Array-length inequalities in preconditions are not imported into the typed CFG state. |

`SafeCaseGaps` in the regression test names these exact controls. Removing a
warning requires removing its gap entry; an unexpected warning or a missing
seeded diagnostic fails the test. These gaps mean #901 is **not fully resolved**.
No positive expectation is waived or changed to accommodate missing analysis.
Absence of a finding, especially for clamping or interop, is not a verified
safety verdict.

## Compilation and API modeling

With effect enforcement disabled (these tests target bug/taint detection), all
seven subjects produce Roslyn-clean C#, with zero compilation errors required by
the actual-file tests. The SQL subject declares a native injected `db` interface
for `execute`, `execute_param`, and `query`, preserving the analyzer's resolved
database sink identities without bundling or stubbing a database provider.
`execute_param` must bind its value separately from the query text.

The native `SqlEscaping.Escape` helper doubles apostrophes using `string.Replace`.
This contract assumes standard SQL single-quoted literals, without dialect-specific
backslash escapes; it is not a general sanitizer for identifiers or arbitrary SQL
contexts. The regression harness explicitly registers **only this bound function's
symbol ID** as a SQL sanitizer through `TaintAnalysisOptions.AdditionalSanitizers`.
Runtime tests check its actual quote-escaping output and all six subjects' query,
operation, and separate parameter delivery to a recording provider. Default
analysis intentionally does not infer a sanitizer from a local helper's name or
implementation: without the explicit contract, `SafeEscapedInput` is also flagged,
as a separate regression asserts. Thus the 25/25 seeded findings and 20/25 safe
controls above describe the benchmark's explicit API model, not an unconditional
default-analysis proof of SQL escaping.

Default effect enforcement can additionally reject unresolved external effects,
including calls through `ProcessStartInfo.ArgumentList` and dictionary helpers.

The security parameter names use the analyzer's explicit API-boundary source
manifest (`user_input`, `user_data`, `user_path`). They remain externally supplied
values; constants are not substituted for vulnerable inputs. Native indexing,
nullable receiver/member access, Option unwrap, BCL path operations, and BCL
process calls replace obsolete pseudo-operations. Shell commands are passed as
one `-c` argument; quoting for .NET's argument splitting is separate from shell
escaping. The safe array-execution control passes user input through
`ArgumentList` with `UseShellExecute=false`, rather than treating a generic
`sanitize` call as a shell sanitizer.

Loops use Calor's inclusive upper endpoint. The safe array loop stops at
`length - 1`; the off-by-one subject still exceeds the array bounds. Accumulators
and SQL builders use mutable bindings and assignments rather than accidental
shadow declarations. The bounded-shift control now states the missing operand
bounds, and the whitelist control selects actual constant paths rather than
assuming every basename is safe.

## Corpus coverage

All seven subjects belong to the F-2 binding-totality denominator. The six
remaining parse exclusions were removed: 517 parsed files / nine failures /
4,826 bound-expression visits became 523 / three / 5,191, with zero incomplete
bindings. These are binding visits, not a bug-detection success percentage.
Historical experiment records are not regenerated by this repair.

The live effect-key ledger measures all seven subjects (previously one),
adding 17 bound-origin and 64 string-origin keys. The live higher-order-demand
ledger recovers all seven subjects into its effect-pass denominator: 59 files
failing before that pass become 52; the demand total remains 3,123. Both are
regenerated measurements, not edits to historical experiment transcripts.
