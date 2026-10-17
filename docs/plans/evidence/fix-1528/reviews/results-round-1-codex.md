No production regression found in the changed paths. Two MINOR test issues:

- **MINOR — Default-width tests can mask integer substitution.** [FloatLiteralConversionTests.cs:26](tests/Calor.Compiler.Tests/FloatLiteralConversionTests.cs#L26): emitting integer `0` for both defaults still passes because `0 + 2f` produces `Single` and `0 + 2m` produces `Decimal`. Add standalone `return default(float);` and `return default(decimal);` probes to verify the boxed runtime types directly.

- **MINOR — Interpolation case depends on the host culture.** [FloatLiteralConversionTests.cs:33](tests/Calor.Compiler.Tests/FloatLiteralConversionTests.cs#L33): under `fr-FR`, the original program returns `"2,5"`, failing the hardcoded `"String:2.5"` expectation. The invariant conversion at line 154 cannot change an already formatted string. Scope this case to invariant culture or use explicit invariant interpolation.

Executable validation was blocked by the read-only environment. No files were modified.

APPROVE WITH MINOR