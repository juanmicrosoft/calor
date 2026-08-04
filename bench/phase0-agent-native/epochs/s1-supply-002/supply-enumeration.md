# v0.12 S1 — expressible-stratum supply enumeration (pre-pass)

**Conversion-only.** No builds, no test runs, no recovery, no bundles, and no
eligibility evaluation — which is what permits this to run *before* the A-1.5 freeze
(plan §3 constraint (a)).

**Upper bound, not realized supply.** Build and `RecoverBuildAsync` are skipped, so a
file that converted but does not compile is still counted native here — recovery is
precisely what would revert it. Every later eligibility clause only removes candidates,
so these figures bound eligibility from above. That is the correct direction for a
"is there conceivably enough supply?" go/no-go, and the wrong number to quote as supply.

## Supply by project

| Project | Sites (all) | Lost to conversion | Supply (native) | Supply (with-loss) | with-loss / native |
|---|---:|---:|---:|---:|---:|
| MediatR | 46 | 39 | **5** | 2 | 0.40 |
| Serilog | 177 | 65 | **93** | 19 | 0.20 |
| FluentValidation | 296 | 143 | **105** | 48 | 0.46 |
| **TOTAL** | **519** | 247 | **203** | 69 | 0.34 |

**Read the first two columns before the third.** "Sites (all)" is the corpus-side
supply the frozen operator set can enumerate anywhere; "lost to conversion" is the part
the converter could not reach. A low native figure with a high lost figure is a
**converter-fidelity** result, not a corpus result — opposite remedies. Any downstream
use of the native number as "the corpus's supply ceiling" must account for both.

## D-5 — region-granularity clause (a)

Pre-committed rule (S1 kickoff): accept site-level clause (a) iff with-loss/native ≥ 0.50.

**SPLIT — pooled ratio 0.34 says REJECT, but that is carried by 'EffectViolation' (its own ratio 0.32); excluding it the ratio is 2.00 → ACCEPT. The pre-committed threshold does not settle this; decide explicitly and record why.**

Per-project ratios are in the table above; a pooled figure must not conceal a split.

Per-operator, because a pooled ratio can be carried by a single operator:

| Operator | With-loss | Native | ratio |
|---|---:|---:|---:|
| EffectViolation | 63 | 200 | 0.32 |
| NullDeref | 6 | 3 | 2.00 |

## Candidates by operator

### MediatR

| Operator | Native | With-loss |
|---|---:|---:|
| EffectViolation | 5 | 2 |

### Serilog

| Operator | Native | With-loss |
|---|---:|---:|
| EffectViolation | 93 | 13 |
| NullDeref | 0 | 6 |

### FluentValidation

| Operator | Native | With-loss |
|---|---:|---:|
| EffectViolation | 102 | 48 |
| NullDeref | 3 | 0 |

## Clustering (native candidates by file)

The probe's `--max-candidates` cap takes a **lexicographic prefix**, not a sample, so
candidates concentrated in few files mean the probe's effective *n* is far below its
nominal *n* (S1 kickoff D-3). This table is what makes that visible before the probe runs.

### MediatR — 4 native file(s) carrying candidates

- `src/MediatR/MicrosoftExtensionsDI/ServiceCollectionExtensions.cs` — 2
- `src/MediatR/INotificationHandler.cs` — 1
- `src/MediatR/NotificationPublishers/TaskWhenAllPublisher.cs` — 1
- `src/MediatR/Wrappers/NotificationHandlerWrapper.cs` — 1

### Serilog — 29 native file(s) carrying candidates

- `src/Serilog/Configuration/LoggerSinkConfiguration.cs` — 13
- `src/Serilog/Settings/KeyValuePairs/SurrogateConfigurationMethods.cs` — 10
- `src/Serilog/Configuration/LoggerDestructuringConfiguration.cs` — 9
- `src/Serilog/Context/LogContext.cs` — 8
- `src/Serilog/Configuration/LoggerEnrichmentConfiguration.cs` — 7
- `src/Serilog/Data/LogEventPropertyValueRewriter.cs` — 5
- `src/Serilog/Configuration/LoggerAuditSinkConfiguration.cs` — 4
- `src/Serilog/Configuration/LoggerFilterConfiguration.cs` — 4
- `src/Serilog/Core/Pipeline/ByReferenceStringComparer.cs` — 4
- `src/Serilog/Capturing/DepthLimiter.cs` — 3
- `src/Serilog/Configuration/LoggerSettingsConfiguration.cs` — 3
- `src/Serilog/Events/EventProperty.cs` — 3
- `src/Serilog/Formatting/Display/LevelOutputFormat.cs` — 3
- `src/Serilog/Events/MessageTemplate.cs` — 2
- `src/Serilog/Context/EnricherStack.cs` — 1
- `src/Serilog/Core/Filters/DelegateFilter.cs` — 1
- `src/Serilog/Core/Pipeline/MessageTemplateCache.cs` — 1
- `src/Serilog/Data/LogEventPropertyValueVisitor.cs` — 1
- `src/Serilog/Events/LogEventPropertyValue.cs` — 1
- `src/Serilog/Guard.cs` — 1
- `src/Serilog/LoggerExtensions.cs` — 1
- `src/Serilog/Parsing/TextToken.cs` — 1
- `src/Serilog/Policies/DelegateDestructuringPolicy.cs` — 1
- `src/Serilog/Policies/EnumScalarConversionPolicy.cs` — 1
- `src/Serilog/Policies/PrimitiveScalarConversionPolicy.cs` — 1
- `src/Serilog/Policies/ProjectedDestructuringPolicy.cs` — 1
- `src/Serilog/Policies/ReflectionTypesScalarDestructuringPolicy.cs` — 1
- `src/Serilog/Policies/SimpleScalarConversionPolicy.cs` — 1
- `src/Serilog/Rendering/Casing.cs` — 1

### FluentValidation — 35 native file(s) carrying candidates

- `src/FluentValidation/Internal/ValidationStrategy.cs` — 11
- `src/FluentValidation/AssemblyScanner.cs` — 7
- `src/FluentValidation/Internal/RuleBuilder.cs` — 7
- `src/FluentValidation/ValidatorDescriptor.cs` — 6
- `src/FluentValidation/TestHelper/TestValidationResult.cs` — 5
- `src/FluentValidation/Validators/NotEqualValidator.cs` — 5
- `src/FluentValidation/Internal/AccessorCache.cs` — 4
- `src/FluentValidation/Internal/MessageFormatter.cs` — 4
- `src/FluentValidation/Internal/RuleComponent.cs` — 4
- `src/FluentValidation/Internal/TrackingCollection.cs` — 4
- `src/FluentValidation/Validators/ChildValidatorAdaptor.cs` — 4
- `src/FluentValidation/Validators/EnumValidator.cs` — 4
- `src/FluentValidation/Validators/RangeValidator.cs` — 4
- `src/FluentValidation/Internal/MemberNameValidatorSelector.cs` — 3
- `src/FluentValidation/Validators/EmptyValidator.cs` — 3
- `src/FluentValidation/Validators/RegularExpressionValidator.cs` — 3
- `src/FluentValidation/Internal/RulesetValidatorSelector.cs` — 2
- `src/FluentValidation/Resources/LanguageManager.cs` — 2
- `src/FluentValidation/ValidatorFactoryBase.cs` — 2
- `src/FluentValidation/Validators/AbstractComparisonValidator.cs` — 2
- `src/FluentValidation/Validators/AsyncPredicateValidator.cs` — 2
- `src/FluentValidation/Validators/GreaterThanValidator.cs` — 2
- `src/FluentValidation/Validators/LessThanValidator.cs` — 2
- `src/FluentValidation/Validators/StringEnumValidator.cs` — 2
- `src/FluentValidation/AsyncValidatorInvokedSynchronouslyException.cs` — 1
- `src/FluentValidation/Internal/CompositeValidatorSelector.cs` — 1
- `src/FluentValidation/Internal/DefaultValidatorSelector.cs` — 1
- `src/FluentValidation/Internal/Extensions.cs` — 1
- `src/FluentValidation/Internal/MessageBuilderContext.cs` — 1
- `src/FluentValidation/Internal/RuleComponentForNullableStruct.cs` — 1
- `src/FluentValidation/Results/ValidationFailure.cs` — 1
- `src/FluentValidation/Validators/AsyncPropertyValidator.cs` — 1
- `src/FluentValidation/Validators/ComparableComparer.cs` — 1
- `src/FluentValidation/Validators/ExclusiveBetweenValidator.cs` — 1
- `src/FluentValidation/Validators/PropertyValidator.cs` — 1

