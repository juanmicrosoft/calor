# Conditional access shape: native positive control

This fixture retains `Partial` support and requires **zero conversion losses**.
It certifies `InteropPreserved` absent for positional conditional invocation;
conditional indexing, named/ref arguments, and generic conditional calls are
not certified by this fixture.

`Run` first invokes `target?.M(Argument())` on null. The result must be null and
the argument must not run. It then repeats the invocation on a non-null holder:
the argument and method must each run exactly once, returning 7.
The final value is **117**: one argument call, one method call, and result 7.
The counters reset on each run, so repeated execution cannot hide state leakage.

`expected.calr` is generated with the exact registry conversion options and the
filename `conditional-access-shape.cs`. Both invocations remain structured
conditional calls with their argument nested inside the call. `test.calr`
repeats that converted program with a `Run` postcondition requiring 117.
The manifest also executes the converted output. Original C#, generated C#,
and the Calor value test were each executed twice during fixture validation.

This is a runtime-fidelity control, not an effect-verification claim. The
registry's runtime replay disables effect enforcement. With the default CLI
settings, the expression-valued conditional target has unresolved effects:
`Run` reports `Calor0410` for an undeclared `unknown` effect and `Calor0419`
marks its effects as assumed. The formatter therefore preserves both Calor
artifacts unchanged as explicitly listed semantic fallbacks; it does not
silently format them as verified programs.
