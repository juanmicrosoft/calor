# Task bundle: fluentvalidation-injectedmutation-cand6-EffectViolation

- Project: **FluentValidation**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts HasError's return (boolean flip)` (EffectViolation)
- Mutated region: `src/FluentValidation/Validators/ExclusiveBetweenValidator.cs` line 33, col 26

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.False() Failure
>
> Subject hint: ExclusiveBetweenValidator
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_by_icomparer_then_the_validator_should_pass` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_larger_than_the_range_by_icomparer_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_by_icomparer_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.Validates_with_nullable_when_property_not_null` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set_for_strings` (assembly `fluentvalidation.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_by_icomparer_then_the_validator_should_pass&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_larger_than_the_range_by_icomparer_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_by_icomparer_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.Validates_with_nullable_when_property_not_null&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set_for_strings`
- Regression-net project: `src/FluentValidation.Tests/FluentValidation.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/FluentValidation/Validators/ExclusiveBetweenValidator.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.False()`, Calor=`Assert.False()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 53.2%

## Defect stratum: **Expressible**

- Verification-addressable: the mutation makes Calor's **Calor0410** fire on the 
  converted arm — a signal the C# compiler has no equivalent of. The C# arm's agent may ship the 
  defect. Calor0410 is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable.

  > **This bundle does NOT present that diagnostic to an agent.** Both arms ship plain `.cs` — the
  > calor arm is round-tripped C# — and the runner never invokes the Calor compiler, so the check
  > cannot fire in the loop and neither arm's build fails. The differential above is a
  > **compiler-level** property, established out-of-band by the addressability probe. An epoch over
  > these arms measures the **conversion penalty** (plus the arm-symmetric ceiling-recurrence leg),
  > NOT the verification-depth thesis. See `docs/plans/substrate-arm-validity-finding.md`.

  > **Papering-over residual — a property of the DEFECT, not of this bundle (neither path is
  > exercisable here: no `.calr` sources, no Calor build):** were the arm a real Calor build, the agent could clear it by 
  > REMOVING the injected effect (correct → held-out passes → caught) OR by DECLARING it in §E 
  > (papers over → the bug still ships → held-out fails → escaped). Which path the agent takes IS 
  > the measurement; both remain possible.
