# Task bundle: fluentvalidation-injectedmutation-cand10-EffectViolation

- Project: **FluentValidation**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts GetDefaultMessageTemplate's return (returns default instead of the computed value)` (EffectViolation)
- Mutated region: `src/FluentValidation/Validators/LessThanValidator.cs` line 47, col 28

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: System.ArgumentNullException : Value cannot be null. (Parameter 'input')
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_equal_to_input` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Should_set_default_validation_message_when_validation_fails` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_WithPropertyName_Only_throws` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_property` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_greater_than_input` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Validates_nullable_with_nullable_property` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property` (assembly `fluentvalidation.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_equal_to_input&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Should_set_default_validation_message_when_validation_fails&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_WithPropertyName_Only_throws&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_property&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_greater_than_input&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Validates_nullable_with_nullable_property&FullyQualifiedName!~FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property`
- Regression-net project: `src/FluentValidation.Tests/FluentValidation.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/FluentValidation/Validators/LessThanValidator.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`System.ArgumentNullException`, Calor=`System.ArgumentNullException`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 53.2%

## Defect stratum: **Expressible**

- Verification-addressable: the mutation makes Calor's **Calor0410** fire on the 
  converted arm — a signal the C# compiler has no equivalent of. The C# arm's agent may ship the 
  defect; the Calor arm's agent is confronted by the diagnostic. Calor0410 is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable.

  > **Papering-over residual (preserved by design):** the agent can clear the Calor build by 
  > REMOVING the injected effect (correct → held-out passes → caught) OR by DECLARING it in §E 
  > (papers over → the bug still ships → held-out fails → escaped). Which path the agent takes IS 
  > the measurement; both remain possible.
