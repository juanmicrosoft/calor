# WS-W4 Slice C — task-generation run report

Mutate-then-convert task generation with the D-W4.1 eligibility predicate and D-W4.3 fidelity gate. 
This run produces the substrate the Slice-E dry-run consumes; it does NOT run agents.

## Definition of done

- Eligible bundles: **5** (target ≥ 3)
- Projects with eligible tasks that pass the fidelity gate: **0** (target ≥ 2)
- **DoD met: False**

## Fidelity gate (D-W4.3)

- Bar: NativeFraction ≥ 0.70 (PROVISIONAL — pending A-1.4 tranche-2 freeze)
- PP-W2 NOT ADJUDICATED: only 0 project(s) pass the fidelity gate (< 2 required). Reported only — the measurement half returns to substrate engineering (wedge §6.1).

| Project | NativeFraction | Passes gate | Reason |
|---|---:|:---:|---|
| MediatR | 46.9% | no | excluded: NativeFraction 46.9% < bar 70.0% — disclosed, contributes no tasks. |
| Serilog | 40.0% | no | excluded: NativeFraction 40.0% < bar 70.0% — disclosed, contributes no tasks. |
| FluentValidation | 53.2% | no | excluded: NativeFraction 53.2% < bar 70.0% — disclosed, contributes no tasks. |

## Exclusion accounting (D-W4.1 — every candidate counted, no silent shrinkage)

| Project | Enumerated | Considered | Eligible | Excl (a) | Excl (b) | Excl attribution | Excl leak | Excl no-cover | Excl no-compile | Excl multi-src | Excl inseparable | Eligibility rate |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| MediatR | 2 | 2 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0% |
| Serilog | 14 | 14 | 2 | 6 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 14% |
| FluentValidation | 10 | 10 | 3 | 1 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 30% |
| **TOTAL** | **26** | **26** | **5** | **8** | **13** | **0** | **0** | **0** | **0** | **0** | **0** | **19%** |

'Excl multi-src' and 'Excl inseparable' are revert-source SUPPLY exclusions (a mined fix commit 
touching >1 source file, or whose source hunk could not be cleanly reverse-applied onto the pinned 
tree). They are recorded before any build/test cost and are 0 for injected-only runs.

Eligibility rate is Eligible/Considered (Considered = evaluated candidates; Enumerated is the 
full sited set before the per-project cap / early-stop, so the rate is honest about truncation).

### Per-candidate dispositions

**MediatR** (NativeFraction 46.9%, 15 native of 32 files)

| Candidate | File | Stratum | Operator | Expected check | Addressable | Verdict | Reason |
|---|---|---|---|---|:---:|:---:|---|
| EffectViolation-L26C38 | src/MediatR/MicrosoftExtensionsDI/ServiceCollectionExtensions.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L42C38 | src/MediatR/MicrosoftExtensionsDI/ServiceCollectionExtensions.cs | Expressible | EffectViolation | Calor0410 | no | excluded | NoObservableDefect |

**Serilog** (NativeFraction 40.0%, 44 native of 110 files)

| Candidate | File | Stratum | Operator | Expected check | Addressable | Verdict | Reason |
|---|---|---|---|---|:---:|:---:|---|
| EffectViolation-L34C32 | src/Serilog/Configuration/LoggerSettingsConfiguration.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L51C32 | src/Serilog/Configuration/LoggerSettingsConfiguration.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L65C25 | src/Serilog/Configuration/LoggerSettingsConfiguration.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L26C17 | src/Serilog/Core/Filters/DelegateFilter.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | ArmsDiverge |
| EffectViolation-L25C21 | src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L30C16 | src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L35C17 | src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L40C16 | src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L40C28 | src/Serilog/Core/Pipeline/MessageTemplateCache.cs | Expressible | EffectViolation | Calor0410 | no | excluded | NoObservableDefect |
| EffectViolation-L45C31 | src/Serilog/Data/LogEventPropertyValueVisitor.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L52C19 | src/Serilog/Events/LogEventPropertyValue.cs | Expressible | EffectViolation | Calor0410 | yes | ELIGIBLE | None |
| EffectViolation-L20C27 | src/Serilog/LoggerExtensions.cs | Expressible | EffectViolation | Calor0410 | no | excluded | MutatedFileReverted |
| EffectViolation-L58C26 | src/Serilog/Parsing/TextToken.cs | Expressible | EffectViolation | Calor0410 | yes | ELIGIBLE | None |
| EffectViolation-L24C26 | src/Serilog/Rendering/Casing.cs | Expressible | EffectViolation | Calor0410 | no | excluded | NotNativeRegion |

**FluentValidation** (NativeFraction 53.2%, 74 native of 139 files)

| Candidate | File | Stratum | Operator | Expected check | Addressable | Verdict | Reason |
|---|---|---|---|---|:---:|:---:|---|
| EffectViolation-L40C24 | src/FluentValidation/AsyncValidatorInvokedSynchronouslyException.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L91C25 | src/FluentValidation/Results/ValidationFailure.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L43C29 | src/FluentValidation/Validators/AsyncPredicateValidator.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | MutatedFileReverted |
| EffectViolation-L47C28 | src/FluentValidation/Validators/AsyncPredicateValidator.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | ArmsDiverge |
| EffectViolation-L31C13 | src/FluentValidation/Validators/ComparableComparer.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | ArmsDiverge |
| EffectViolation-L33C26 | src/FluentValidation/Validators/ExclusiveBetweenValidator.cs | Expressible | EffectViolation | Calor0410 | yes | ELIGIBLE | None |
| EffectViolation-L39C23 | src/FluentValidation/Validators/GreaterThanValidator.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L48C28 | src/FluentValidation/Validators/GreaterThanValidator.cs | Expressible | EffectViolation | Calor0410 | yes | ELIGIBLE | None |
| EffectViolation-L38C23 | src/FluentValidation/Validators/LessThanValidator.cs | Expressible | EffectViolation | Calor0410 | yes | excluded | NoObservableDefect |
| EffectViolation-L47C28 | src/FluentValidation/Validators/LessThanValidator.cs | Expressible | EffectViolation | Calor0410 | yes | ELIGIBLE | None |

## Verification-addressability (expressible stratum) — base-rate honesty

A defect is *expressible* (verification-addressable) only if the differential probe confirms Calor's 
expected check is INTRODUCED by the mutation on the converted arm (fires on the mutated conversion, 
absent on the clean one). The base rate below **bounds how often real defects of these shapes would be 
Calor-catchable** and MUST be read alongside any escaped-bug claim so the claim is not overstated.

- Expressible candidates considered: **26**
- Probed & determinable: **26** (indeterminable — a conversion did not compile: 0)
- Verification-addressable (check introduced by the mutation): **17**
- **Verification-addressability base rate: 65%** (addressable / probed-determinable)

| Expected check | Operator class | Probed-determinable | Addressable | Rate |
|---|---|---:|---:|---:|
| Calor0410 | EffectViolation | 26 | 17 | 65% |

## Eligible task bundles

- `serilog-injectedmutation-cand11-EffectViolation` — [Expressible] InjectedMutation `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts ToString's return (returns default instead of the computed value)` in `src/Serilog/Events/LogEventPropertyValue.cs`:52; held-out: Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumCollectionCountIsApplied, Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumStringLengthIsApplied, Serilog.Tests.Capturing.PropertyValueConverterTests.MaximumDepthIsEffectiveAndThreadSafe, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthDefaultIsEffective, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthIsEffective, Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormat, Serilog.Tests.Events.LogEventPropertyValueTests.WhenDestructuringAKnownLiteralTypeIsScalar, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedObject(text: "123", textAfter: "123", limit: 3), Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForArrayAsLongAsLimit, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedString, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForString, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForDictionaryWithMoreKeysThanLimit, Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValue, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForDictionaryWithAsManyKeysAsLimit, Serilog.Tests.Formatting.Display.MessageTemplateTextFormatterTests.AnEmptyPropertiesTokenIsAnEmptyStructureValueWithDefaultFormatting, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedObject, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedString, Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringWithCustomExtensionMethodIsApplied, Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormatProvider, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForObject, Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForArrayThanLimit, Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumDepthIsApplied; native=True, attribution=AttributedToMutation, fires **Calor0410**
- `serilog-injectedmutation-cand13-EffectViolation` — [Expressible] InjectedMutation `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts Equals's return (boolean flip)` in `src/Serilog/Parsing/TextToken.cs`:58; held-out: Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAfterOneLeftIsParsedAPropertyTokenAndATextToken, Serilog.Tests.Parsing.MessageTemplateParserTests.AMalformedPropertyTagIsParsedAsText(template: "{0a}"), Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithAMalformedPropertyTagIsParsedAsManyTextTokens, Serilog.Tests.Parsing.MessageTemplateParserTests.NonNumberAlignmentIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.DestructureWithInvalidHintsIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithoutPropertiesIsASingleTextToken, Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAreParsedAsASingleBracket, Serilog.Tests.Parsing.MessageTemplateParserTests.EmptyAlignmentIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.ATrailingUnmatchedBracketIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.MissingRightBracketIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.AlignmentWithPositiveSignParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracketInsideText, Serilog.Tests.Parsing.MessageTemplateParserTests.DestructuringWithEmptyPropertyNameIsParsedAsText, Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracket, Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledBracketsAreParsedAsASingleBracket, Serilog.Tests.Parsing.MessageTemplateParserTests.MultipleTokensHasCorrectIndexes; native=True, attribution=AttributedToMutation, fires **Calor0410**
- `fluentvalidation-injectedmutation-cand6-EffectViolation` — [Expressible] InjectedMutation `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts HasError's return (boolean flip)` in `src/FluentValidation/Validators/ExclusiveBetweenValidator.cs`:33; held-out: FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_by_icomparer_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass, FluentValidation.Tests.ExclusiveBetweenValidatorTests.Validates_with_nullable_when_property_not_null, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_lower_bound_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_larger_than_the_range_by_icomparer_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_then_the_validator_should_pass_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_text_is_larger_than_the_range_then_the_validator_should_fail, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_smaller_than_the_range_then_the_validator_should_fail_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_validator_fails_the_error_message_should_be_set, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_exactly_the_size_of_the_upper_bound_then_the_validator_should_fail_for_strings, FluentValidation.Tests.ExclusiveBetweenValidatorTests.When_the_value_is_between_the_range_specified_by_icomparer_then_the_validator_should_pass; native=True, attribution=AttributedToMutation, fires **Calor0410**
- `fluentvalidation-injectedmutation-cand8-EffectViolation` — [Expressible] InjectedMutation `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts GetDefaultMessageTemplate's return (returns default instead of the computed value)` in `src/FluentValidation/Validators/GreaterThanValidator.cs`:48; held-out: FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_less_than_input, FluentValidation.Tests.CascadingFailuresTester.Cascade_set_to_stop_in_child_validator_with_RuleForEach_in_parent, FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null, FluentValidation.Tests.RulesetTests.Includes_all_rulesets, FluentValidation.Tests.CollectionValidatorWithParentTests.Creates_validator_using_context_from_property_value, FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback_accepting_derived, FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_collection, FluentValidation.Tests.InheritanceValidatorTest.Validates_with_callback, FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property, FluentValidation.Tests.RuleBuilderTests.Result_should_use_custom_property_name_when_no_property_name_can_be_determined, FluentValidation.Tests.InheritanceValidatorTest.Validates_inheritance_hierarchy, FluentValidation.Tests.GreaterThanValidatorTester.Validates_nullable_with_nullable_property, FluentValidation.Tests.ValidatorSelectorTests.Only_validates_single_child_property_of_all_elements_in_nested_collection, FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationError_with_an_unmatched_rule_and_multiple_errors_should_throw_an_exception, FluentValidation.Tests.ChildRulesTests.Can_define_nested_rules_for_collection, FluentValidation.Tests.ValidatorSelectorTests.Validates_nullable_property_with_overriden_name_when_selected, FluentValidation.Tests.RulesetTests.Combines_rulesets_and_explicit_properties, FluentValidation.Tests.ValidatorSelectorTests.Executes_correct_rule_when_using_property_with_nested_includes, FluentValidation.Tests.GreaterThanValidatorTester.Should_set_default_error_when_validation_fails, FluentValidation.Tests.ValidatorSelectorTests.Only_validates_child_property_for_single_item_in_collection, FluentValidation.Tests.GreaterThanValidatorTester.Validates_with_nullable_property, FluentValidation.Tests.GreaterThanValidatorTester.Should_fail_when_equal_to_input, FluentValidation.Tests.InheritanceValidatorTest.Validates_ruleset, FluentValidation.Tests.InheritanceValidatorTest.Validates_collection; native=True, attribution=AttributedToMutation, fires **Calor0410**
- `fluentvalidation-injectedmutation-cand10-EffectViolation` — [Expressible] InjectedMutation `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts GetDefaultMessageTemplate's return (returns default instead of the computed value)` in `src/FluentValidation/Validators/LessThanValidator.cs`:47; held-out: FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_property, FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null, FluentValidation.Tests.LessThanValidatorTester.Should_set_default_validation_message_when_validation_fails, FluentValidation.Tests.LessThanValidatorTester.Validates_nullable_with_nullable_property, FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_equal_to_input, FluentValidation.Tests.LessThanValidatorTester.Validates_with_nullable_when_property_not_null_cross_property, FluentValidation.Tests.ValidatorTesterTester.ShouldHaveValidationErrorFor_WithPropertyName_Only_throws, FluentValidation.Tests.LessThanValidatorTester.Should_fail_when_greater_than_input; native=True, attribution=AttributedToMutation, fires **Calor0410**

## Interpretation note (recorded)

The eligibility rate is itself a Slice-E signal: too low a rate on the OSS corpus means insufficient 
native-eligible surface to yield a decidable dry-run. **This synthetic rate is an UPPER BOUND**: both 
synthetic projects are 100%-native, so clause-(a) exclusions are 0 here, whereas on OSS (Slice-B 
NativeFraction 0.40–0.53) clause (a) will exclude heavily — the OSS rate will be materially lower. 
The fidelity bar is PROVISIONAL (pending A-1.4 tranche-2). The Calor arm works on machine-converted 
§-syntax vs the C# arm's idiomatic original — a bias AGAINST Calor, so a PP-W2 win is conservative and 
a loss is confounded with conversion idiom.
