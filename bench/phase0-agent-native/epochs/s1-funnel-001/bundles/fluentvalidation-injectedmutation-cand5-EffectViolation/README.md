# Task bundle: fluentvalidation-injectedmutation-cand5-EffectViolation

- Project: **FluentValidation**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts Compare's return (+1)` (EffectViolation)
- Mutated region: `src/FluentValidation/Validators/ComparableComparer.cs` line 31, col 13

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.Throws() Failure
>
> Subject hint: ExclusiveBetweenValidator
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_pass` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_pass_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass_for_strings` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set` (assembly `fluentvalidation.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw_for_strings&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_to_is_smaller_than_the_from_then_the_validator_should_throw_for_strings&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set_for_strings&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_pass&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_pass_for_strings&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail_for_strings&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail&FullyQualifiedName!~FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass_for_strings&FullyQualifiedName!~FluentValidation.Tests.InclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set`
- Regression-net project: `src/FluentValidation.Tests/FluentValidation.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/FluentValidation/Validators/ComparableComparer.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.Throws()`, Calor=`Assert.Throws()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 53.2%

## Defect stratum: **Expressible**

- Verification-addressable **at generation time**: compiling the mutated file as Calor makes 
  **Calor0410** fire, and it does not fire on the clean conversion — a signal the 
  C# compiler has no equivalent of. Calor0410 is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable.

  > **This bundle does NOT present that diagnostic to an agent.** Both arms ship plain `.cs` — the 
  > calor arm is round-tripped C# — and the runner never invokes the Calor compiler, so the check 
  > cannot fire in the loop and neither arm's build fails. The differential above is a 
  > **compiler-level** property, established out-of-band by the addressability probe. An epoch over 
  > these arms measures the **conversion penalty** (plus the arm-symmetric ceiling-recurrence leg), 
  > NOT the verification-depth thesis. See `docs/plans/substrate-arm-validity-finding.md`.

  > **Papering-over residual — a property of the DEFECT, not of this bundle:** were the arm a real 
  > Calor build, the agent could clear it by REMOVING the injected effect (correct → caught) or by 
  > DECLARING it in §E (papers over → the bug still ships → escaped). With no `.calr` sources and 
  > no Calor build present, **neither path is exercisable here** and that choice is not measured.
