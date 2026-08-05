# WS-S1 failure-cause census

The D-S1.1 loss ledger covers 4.7% of the non-native gap; 95.3% is files that never converted
or compiled. This buckets those by **cause**, which is what the replacement gate decides on.

**Pre-committed rule** (gates A-1.6(b), substrate plan §10): top-3 causes ≥ 
50.0% → continue WS-S1; **otherwise → PP-S1 = miss**. Exhaustive by construction.

## Verdict: PP-S1 = MISS — top-3 causes cover 40.4% < 50.0%: long tail, not a work-list

- Failures classified: **141**
- Top-3 share: **40.4%**   ·   top-10 share: 72.3%
- Distinct causes: 33

### Failures by project

- FluentValidation: 64
- Serilog: 64
- MediatR: 13

### Causes, ranked

| # | Cause | Files | Share | Example |
|---:|---|---:|---:|---|
| 1 | `Reverted:CS0103` | 23 | 16.3% | `FluentValidation/src/FluentValidation/Internal/AccessorCache.cs` |
| 2 | `Reverted:CS0246` | 20 | 14.2% | `FluentValidation/src/FluentValidation/DefaultValidatorExtensions_Validate.cs` |
| 3 | `CompileError:Expected EXT, METHOD, PROP, IXER, or END_IFACE but found <tok>` | 14 | 9.9% | `FluentValidation/src/FluentValidation/Internal/IncludeRule.cs` |
| 4 | `CompileError:Binding '<id>' has no type annotation and no initializer. Add either '<id>' (e.g` | 11 | 7.8% | `FluentValidation/src/FluentValidation/AbstractValidator.cs` |
| 5 | `EmitSyntaxError:Unexpected token '<id>'` | 7 | 5.0% | `FluentValidation/src/FluentValidation/ValidationException.cs` |
| 6 | `CompileError:Dedent to column <n> does not match any enclosing indent level.` | 6 | 4.3% | `FluentValidation/src/FluentValidation/DefaultValidatorExtensions.cs` |
| 7 | `Reverted:CS1503` | 6 | 4.3% | `FluentValidation/src/FluentValidation/Internal/CompositeValidatorSelector.cs` |
| 8 | `Reverted:CS8917` | 6 | 4.3% | `MediatR/src/MediatR/Wrappers/NotificationHandlerWrapper.cs` |
| 9 | `Reverted:CS0738` | 5 | 3.5% | `FluentValidation/src/FluentValidation/Internal/PropertyRule.cs` |
| 10 | `CompileError:Expected statement but found <tok>` | 4 | 2.8% | `Serilog/src/Serilog/Capturing/PropertyValueConverter.cs` |
| 11 | `EmitSyntaxError:Identifier expected` | 4 | 2.8% | `MediatR/src/MediatR/Internal/ObjectDetails.cs` |
| 12 | `Reverted:CS0106` | 4 | 2.8% | `FluentValidation/src/FluentValidation/AssemblyScanner.cs` |
| 13 | `Reverted:CS1729` | 4 | 2.8% | `Serilog/src/Serilog/Capturing/MessageTemplateProcessor.cs` |
| 14 | `EmitSyntaxError:Invalid expression term '<id>'` | 3 | 2.1% | `FluentValidation/src/FluentValidation/Internal/CollectionPropertyRule.cs` |
| 15 | `CompileError:'<id>' is not aligned with any open §IF — the if-chain already closed at a shall` | 2 | 1.4% | `MediatR/src/MediatR/Internal/HandlersOrderer.cs` |
| 16 | `Reverted:CS0535` | 2 | 1.4% | `FluentValidation/src/FluentValidation/ValidatorFactoryBase.cs` |
| 17 | `Reverted:CS0759` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/MemberNameValidatorSelector.cs` |
| 18 | `Reverted:CS1061` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/Extensions.cs` |
| 19 | `Reverted:CS1929` | 2 | 1.4% | `FluentValidation/src/FluentValidation/Internal/DefaultValidatorSelector.cs` |
| 20 | `EmitSyntaxError:; expected` | 1 | 0.7% | `FluentValidation/src/FluentValidation/TestHelper/ValidatorTestExtensions.cs` |
| 21 | `EmitSyntaxError:A new expression requires an argument list or (), [], or {} after type` | 1 | 0.7% | `FluentValidation/src/FluentValidation/IValidationContext.cs` |
| 22 | `EmitSyntaxError:Syntax error, '<id>' expected` | 1 | 0.7% | `MediatR/src/MediatR/MicrosoftExtensionsDI/MediatrServiceConfiguration.cs` |
| 23 | `Reverted:CS0019` | 1 | 0.7% | `Serilog/src/Serilog/Core/Sinks/AggregateSink.cs` |
| 24 | `Reverted:CS0030` | 1 | 0.7% | `Serilog/src/Serilog/Events/MessageTemplate.cs` |
| 25 | `Reverted:CS0115` | 1 | 0.7% | `Serilog/src/Serilog/Rendering/ReusableStringWriter.cs` |
| 26 | `Reverted:CS0128` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Validators/EnumValidator.cs` |
| 27 | `Reverted:CS0225` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Internal/ValidationStrategy.cs` |
| 28 | `Reverted:CS0266` | 1 | 0.7% | `Serilog/src/Serilog/Formatting/Display/LevelOutputFormat.cs` |
| 29 | `Reverted:CS0274` | 1 | 0.7% | `Serilog/src/Serilog/Context/LogContext.cs` |
| 30 | `Reverted:CS0411` | 1 | 0.7% | `MediatR/src/MediatR/NotificationPublishers/TaskWhenAllPublisher.cs` |
| 31 | `Reverted:CS0508` | 1 | 0.7% | `MediatR/src/MediatR/Wrappers/StreamRequestHandlerWrapper.cs` |
| 32 | `Reverted:CS1620` | 1 | 0.7% | `Serilog/src/Serilog/Core/PropertiesInlineArray.cs` |
| 33 | `Reverted:CS1643` | 1 | 0.7% | `FluentValidation/src/FluentValidation/Validators/RegularExpressionValidator.cs` |

A cause key is `Status:CS####` where a compiler code was recoverable, else a normalized
message shape (paths, positions and quoted identifiers collapsed) so the same defect buckets
together. Reverted files carry the build diagnostics attributed to them during recovery.
