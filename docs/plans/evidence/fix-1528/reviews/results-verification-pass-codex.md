1. **VERIFIED — standalone default widths.** The new rows expect `Single:0` and `Decimal:0` ([file:29–30](tests/Calor.Compiler.Tests/FloatLiteralConversionTests.cs#L29)). They run with `typed: true` (line 44). `Probe` returns `object` (line 123), and the oracle includes `value.GetType().Name` (line 164), so substituting integer `0` produces `Int32:0` and fails the generated-program assertion (line 142).

2. **VERIFIED — invariant culture and restoration.** Both programs use `Observe` (lines 141–142). It saves the caller’s culture and sets invariant culture before invoking `Probe` ([file:157–162](tests/Calor.Compiler.Tests/FloatLiteralConversionTests.cs#L157)); `finally` restores the saved culture on success or exception (lines 166–168). `CurrentCulture` is scoped to the execution context, so this introduces no culture leak across parallel tests.

No new findings. Verification was by inspection; no files were modified.

APPROVE