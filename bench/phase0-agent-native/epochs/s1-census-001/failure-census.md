# WS-S1 failure-cause census

The D-S1.1 loss ledger covers 4.7% of the non-native gap; 95.3% is files that never converted
or compiled. This buckets those by **cause**, which is what the replacement gate decides on.

**Pre-committed rule** (gates A-1.6(b), substrate plan §10): top-3 causes ≥ 
50% → continue WS-S1; **otherwise → PP-S1 = miss**. Exhaustive by construction.

## Verdict: PP-S1 = MISS — top-3 causes cover 38% < 50%: long tail, not a work-list

- Failures classified: **141**
- Top-3 share: **38.3%**   ·   top-10 share: 68.1%
- Distinct causes: 38

### Failures by project

- FluentValidation: 64
- Serilog: 64
- MediatR: 13

### Causes, ranked

| # | Cause | Files | Share | Example |
|---:|---|---:|---:|---|
| 1 | `Reverted:CS0103` | 23 | 16.3% | `FluentValidation/src/FluentValidation/Internal/AccessorCache.cs` |
| 2 | `Reverted:CS0246` | 20 | 14.2% | `FluentValidation/src/FluentValidation/DefaultValidatorExtensions_Validate.cs` |
| 3 | `CompileError:Binding '<id>' has no type annotation and no initializer. Add either '<id>' (e.g` | 11 | 7.8% | `FluentValidation/src/FluentValidation/AbstractValidator.cs` |
| 4 | `CompileError:Expected EXT, METHOD, PROP, IXER, or END_IFACE but found Class` | 10 | 7.1% | `FluentValidation/src/FluentValidation/Internal/IncludeRule.cs` |
| 5 | `EmitSyntaxError:Unexpected token '<id>'` | 7 | 5.0% | `FluentValidation/src/FluentValidation/ValidationException.cs` |
| 6 | `Reverted:CS1503` | 6 | 4.3% | `FluentValidation/src/FluentValidation/Internal/CompositeValidatorSelector.cs` |
| 7 | `Reverted:CS8917` | 6 | 4.3% | `MediatR/src/MediatR/Wrappers/NotificationHandlerWrapper.cs` |
| 8 | `Reverted:CS0738` | 5 | 3.5% | `FluentValidation/src/FluentValidation/Internal/PropertyRule.cs` |
| 9 | `CompileError:Dedent to column 4 does not match any enclosing indent level.` | 4 | 2.8% | `FluentValidation/src/FluentValidation/DefaultValidatorExtensions.cs` |
| 10 | `EmitSyntaxError:Identifier expected` | 4 | 2.8% | `MediatR/src/MediatR/Internal/ObjectDetails.cs` |
| 11 | `Reverted:CS0106` | 4 | 2.8% | `FluentValidation/src/FluentValidation/AssemblyScanner.cs` |
| 12 | `Reverted:CS1729` | 4 | 2.8% | `Serilog/src/Serilog/Capturing/MessageTemplateProcessor.cs` |
| 13 | `CompileError:Expected EXT, METHOD, PROP, IXER, or END_IFACE but found Interface` | 3 | 2.1% | `FluentValidation/src/FluentValidation/Syntax.cs` |
| 14 | `CompileError:Expected statement but found Identifier` | 3 | 2.1% | `Serilog/src/Serilog/Capturing/PropertyValueConverter.cs` |
| 15 | `EmitSyntaxError:Invalid expression term '<id>'` | 3 | 2.1% | `FluentValidation/src/FluentValidation/Internal/CollectionPropertyRule.cs` |
| 16 | `CompileError:'<id>' is not aligned with any open §IF — the if-chain already closed at a shall` | 2 | 1.4% | `MediatR/src/MediatR/Internal/HandlersOrderer.cs` |
| 17 | `Reverted:CS0535` | 2 | 1.4% | `FluentValidation/src/FluentValidation/ValidatorFactoryBase.cs` |
| 18 | `Reverted:CS0759` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/MemberNameValidatorSelector.cs` |
| 19 | `Reverted:CS1061` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/Extensions.cs` |
| 20 | `Reverted:CS1929` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/DefaultValidatorSelector.cs` |
| 21 | `CompileError:Dedent to column 2 does not match any enclosing indent level.` | 1 | 0.7% | `Serilog/src/Serilog/Filters/Matching.cs` |
| 22 | `CompileError:Dedent to column 6 does not match any enclosing indent level.` | 1 | 0.7% | `Serilog/src/Serilog/Settings/KeyValuePairs/KeyValuePairSettings.cs` |
| 23 | `CompileError:Expected EXT, METHOD, PROP, IXER, or END_IFACE but found Enum` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Validators/EmailValidator.cs` |
| 24 | `CompileError:Expected statement but found Arg` | 1 | 0.7% | `Serilog/src/Serilog/Settings/KeyValuePairs/CallableConfigurationMethodFinder.cs` |
| 25 | `EmitSyntaxError:; expected` | 1 | 0.7% | `FluentValidation/src/FluentValidation/TestHelper/ValidatorTestExtensions.cs` |
| 26 | `EmitSyntaxError:A new expression requires an argument list or (), [], or {} after type` | 1 | 0.7% | `FluentValidation/src/FluentValidation/IValidationContext.cs` |
| 27 | `EmitSyntaxError:Syntax error, '<id>' expected` | 1 | 0.7% | `MediatR/src/MediatR/MicrosoftExtensionsDI/MediatrServiceConfiguration.cs` |
| 28 | `Reverted:CS0019` | 1 | 0.7% | `Serilog/src/Serilog/Core/Sinks/AggregateSink.cs` |
| 29 | `Reverted:CS0030` | 1 | 0.7% | `Serilog/src/Serilog/Events/MessageTemplate.cs` |
| 30 | `Reverted:CS0115` | 1 | 0.7% | `Serilog/src/Serilog/Rendering/ReusableStringWriter.cs` |
| 31 | `Reverted:CS0128` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Validators/EnumValidator.cs` |
| 32 | `Reverted:CS0225` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Internal/ValidationStrategy.cs` |
| 33 | `Reverted:CS0266` | 1 | 0.7% | `Serilog/src/Serilog/Formatting/Display/LevelOutputFormat.cs` |
| 34 | `Reverted:CS0274` | 1 | 0.7% | `Serilog/src/Serilog/Context/LogContext.cs` |
| 35 | `Reverted:CS0411` | 1 | 0.7% | `MediatR/src/MediatR/NotificationPublishers/TaskWhenAllPublisher.cs` |
| 36 | `Reverted:CS0508` | 1 | 0.7% | `MediatR/src/MediatR/Wrappers/StreamRequestHandlerWrapper.cs` |
| 37 | `Reverted:CS1620` | 1 | 0.7% | `Serilog/src/Serilog/Core/PropertiesInlineArray.cs` |
| 38 | `Reverted:CS1643` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Validators/RegularExpressionValidator.cs` |

A cause key is `Status:CS####` where a compiler code was recoverable, else a normalized
message shape (paths, positions and quoted identifiers collapsed) so the same defect buckets
together. Reverted files carry the build diagnostics attributed to them during recovery.
