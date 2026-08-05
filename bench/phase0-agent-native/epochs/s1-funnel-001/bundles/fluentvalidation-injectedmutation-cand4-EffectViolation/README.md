# Task bundle: fluentvalidation-injectedmutation-cand4-EffectViolation

- Project: **FluentValidation**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts GetDefaultMessageTemplate's return (returns default instead of the computed value)` (EffectViolation)
- Mutated region: `src/FluentValidation/Validators/AsyncPredicateValidator.cs` line 47, col 28

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: System.ArgumentNullException : Value cannot be null. (Parameter 'input')
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_when_ruleleveldefault_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_globaldefault_both_Stop_and_ruleleveloverride_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_rule_level_failure_when_globaldefault_rule_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ComplexValidationTester.Multiple_rules_in_chain_with_childvalidator_shouldnt_reuse_accessor_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RuleDependencyTests.TestAsyncWithDependentRules_SyncEntry` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_to_first_failing_validator_then_stops_in_all_rules_when_first_validator_succeeds_and_globaldefault_rule_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_model_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_failure_when_globaldefault_class_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ForEachRuleTests.Overrides_indexer_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CollectionValidatorWithParentTests.Validates_collection_asynchronously` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RulesetTests.Includes_combination_of_rulesets_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_globaldefault_rule_stop_and_ruleleveloverride_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_inheritance_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RulesetTests.Combines_rulesets_and_explicit_properties_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.TestValidate_runs_async_throws` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Cascade_mode_can_be_set_after_validator_instantiated_async_legacy` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_failure_when_classlevel_Continue_and_ruleleveldefault_Continue_and_ruleleveloverride_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_Failure_when_globaldefault_both_Continue_and_ruleleveloverride_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.AbstractValidatorTester.WhenPreValidationReturnsTrue_ValidatorsGetHit_ValidateAsync` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_classlevel_Stop_and_ruleleveldefault_Stop_and_ruleleveloverride_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_Only_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_when_first_rule_fails_and_globaldefault_class_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_accepting_derived_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_failure_when_globaldefault_class_stop_and_ruleleveloverride_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_to_second_validator_when_first_validator_succeeds_and_globaldefault_both_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_collection_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.TestValidate_runs_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldNotHaveValidationError_async_model_throws` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RuleDependencyTests.Async_inside_dependent_rules` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_Only_async_throws` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_ruleleveldefault_Stop_and_ruleleveloverride_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RuleDependencyTests.Async_inside_dependent_rules_when_parent_rule_not_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RulesetTests.Includes_all_rulesets_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ForEachRuleTests.When_runs_outside_RuleForEach_loop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.ValidatorTesterTester.ShouldNotHaveValidationError_async_throws` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_continues_when_classlevel_Continue_and_ruleleveldefault_Continue_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.RuleDependencyTests.TestAsyncWithDependentRules_AsyncEntry` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.InheritanceValidatorTest.Validates_ruleset_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_when_classlevel_Stop_and_ruleleveldefault_Stop_async` (assembly `fluentvalidation.tests.dll`)
- `FluentValidation.Tests.CascadingFailuresTester.Cascade_mode_can_be_set_after_validator_instantiated_async` (assembly `fluentvalidation.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_when_ruleleveldefault_Stop_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_globaldefault_both_Stop_and_ruleleveloverride_Continue_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_rule_level_failure_when_globaldefault_rule_Stop_async&FullyQualifiedName!~FluentValidation.Tests.ComplexValidationTester.Multiple_rules_in_chain_with_childvalidator_shouldnt_reuse_accessor_async&FullyQualifiedName!~FluentValidation.Tests.RuleDependencyTests.TestAsyncWithDependentRules_SyncEntry&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_to_first_failing_validator_then_stops_in_all_rules_when_first_validator_succeeds_and_globaldefault_rule_Stop_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_model_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_failure_when_globaldefault_class_Stop_async&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_async&FullyQualifiedName!~FluentValidation.Tests.ForEachRuleTests.Overrides_indexer_async&FullyQualifiedName!~FluentValidation.Tests.CollectionValidatorWithParentTests.Validates_collection_asynchronously&FullyQualifiedName!~FluentValidation.Tests.RulesetTests.Includes_combination_of_rulesets_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_globaldefault_rule_stop_and_ruleleveloverride_Continue_async&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_inheritance_async&FullyQualifiedName!~FluentValidation.Tests.RulesetTests.Combines_rulesets_and_explicit_properties_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.TestValidate_runs_async_throws&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Cascade_mode_can_be_set_after_validator_instantiated_async_legacy&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_failure_when_classlevel_Continue_and_ruleleveldefault_Continue_and_ruleleveloverride_Stop_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_Failure_when_globaldefault_both_Continue_and_ruleleveloverride_Stop_async&FullyQualifiedName!~FluentValidation.Tests.AbstractValidatorTester.WhenPreValidationReturnsTrue_ValidatorsGetHit_ValidateAsync&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_classlevel_Stop_and_ruleleveldefault_Stop_and_ruleleveloverride_Continue_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_Only_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_when_first_rule_fails_and_globaldefault_class_Stop_async&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_accepting_derived_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_after_first_rule_failure_when_globaldefault_class_stop_and_ruleleveloverride_Continue_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_to_second_validator_when_first_validator_succeeds_and_globaldefault_both_Stop_async&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_collection_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.TestValidate_runs_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldNotHaveValidationError_async_model_throws&FullyQualifiedName!~FluentValidation.Tests.RuleDependencyTests.Async_inside_dependent_rules&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_Only_async_throws&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_on_failure_when_ruleleveldefault_Stop_and_ruleleveloverride_Continue_async&FullyQualifiedName!~FluentValidation.Tests.RuleDependencyTests.Async_inside_dependent_rules_when_parent_rule_not_async&FullyQualifiedName!~FluentValidation.Tests.RulesetTests.Includes_all_rulesets_async&FullyQualifiedName!~FluentValidation.Tests.ForEachRuleTests.When_runs_outside_RuleForEach_loop_async&FullyQualifiedName!~FluentValidation.Tests.ValidatorTesterTester.ShouldNotHaveValidationError_async_throws&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_continues_when_classlevel_Continue_and_ruleleveldefault_Continue_async&FullyQualifiedName!~FluentValidation.Tests.RuleDependencyTests.TestAsyncWithDependentRules_AsyncEntry&FullyQualifiedName!~FluentValidation.Tests.InheritanceValidatorTest.Validates_ruleset_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Validation_stops_on_first_failure_when_classlevel_Stop_and_ruleleveldefault_Stop_async&FullyQualifiedName!~FluentValidation.Tests.CascadingFailuresTester.Cascade_mode_can_be_set_after_validator_instantiated_async`
- Regression-net project: `src/FluentValidation.Tests/FluentValidation.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/FluentValidation/Validators/AsyncPredicateValidator.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
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
