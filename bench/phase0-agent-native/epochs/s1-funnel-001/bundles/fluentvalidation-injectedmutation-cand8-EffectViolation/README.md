# Task bundle: fluentvalidation-injectedmutation-cand8-EffectViolation

- Project: **FluentValidation**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts GetDefaultMessageTemplate's return (returns default instead of the computed value)` (EffectViolation)
- Mutated region: `src/FluentValidation/Validators/GreaterThanValidator.cs` line 48, col 28

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: System.ArgumentNullException : Value cannot be null. (Parameter 'input')
>
> Subject hint: InheritanceValidator
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_less_than_input` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RulesetTests.Combines_rulesets_and_explicit_properties` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_collection` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_inheritance_hierarchy` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RulesetTests.Includes_all_rulesets` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_accepting_derived` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorSelectorTests.Executes_correct_rule_when_using_property_with_nested_includes` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_property` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CollectionValidatorWithParentTests.Creates_validator_using_context_from_property_value` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Should_set_default_error_when_validation_fails` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_equal_to_input` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_nested_collection` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_with_an_unmatched_rule_and_multiple_errors_should_throw_an_exception` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RuleBuilderTests.Result_should_use_custom_property_name_when_no_property_name_can_be_determined` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorSelectorTests.Only_validates_child_property_for_single_item_in_collection` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Cascade_set_to_stop_in_child_validator_with_RuleForEach_in_parent` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_ruleset` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ChildRulesTests.Can_define_nested_rules_for_collection` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_collection` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.GreaterThanValidatorTester.Validates_nullable_with_nullable_property` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorSelectorTests.Validates_nullable_property_with_overriden_name_when_selected` (assembly `fluentvalidation.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_less_than_input&FullyQualifiedName!~FluentValidation.Tests.RulesetTests.Combines_rulesets_and_explicit_properties&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_collection&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_inheritance_hierarchy&FullyQualifiedName!~FluentValidation.Tests.RulesetTests.Includes_all_rulesets&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_accepting_derived&FullyQualifiedName!~FluentValidation.Tests.ValidatorSelectorTests.Executes_correct_rule_when_using_property_with_nested_includes&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_property&FullyQualifiedName!~FluentValidation.Tests.CollectionValidatorWithParentTests.Creates_validator_using_context_from_property_value&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Should_set_default_error_when_validation_fails&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_equal_to_input&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property&FullyQualifiedName!~FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_nested_collection&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_with_an_unmatched_rule_and_multiple_errors_should_throw_an_exception&FullyQualifiedName!~FluentValidation.Tests.RuleBuilderTests.Result_should_use_custom_property_name_when_no_property_name_can_be_determined&FullyQualifiedName!~FluentValidation.Tests.ValidatorSelectorTests.Only_validates_child_property_for_single_item_in_collection&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Cascade_set_to_stop_in_child_validator_with_RuleForEach_in_parent&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_ruleset&FullyQualifiedName!~FluentValidation.Tests.ChildRulesTests.Can_define_nested_rules_for_collection&FullyQualifiedName!~FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_collection&FullyQualifiedName!~FluentValidation.Tests.GreaterThanValidatorTester.Validates_nullable_with_nullable_property&FullyQualifiedName!~FluentValidation.Tests.ValidatorSelectorTests.Validates_nullable_property_with_overriden_name_when_selected`
- Regression-net project: `src/FluentValidation.Tests/FluentValidation.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/FluentValidation/Validators/GreaterThanValidator.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`System.ArgumentNullException`, Calor=`System.ArgumentNullException`
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
