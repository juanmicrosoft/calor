# Task bundle: serilog-injectedmutation-cand11-EffectViolation

- Project: **Serilog**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts ToString's return (returns default instead of the computed value)` (EffectViolation)
- Mutated region: `src/Serilog/Events/LogEventPropertyValue.cs` line 52, col 19

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.Contains() Failure: Sub-string not found
>
> Subject hint: LoggerConfiguration
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForDictionaryWithMoreKeysThanLimit` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedObject` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumCollectionCountIsApplied` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthIsEffective` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForObject` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Capturing.PropertyValueConverterTests.MaximumDepthIsEffectiveAndThreadSafe` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForString` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthDefaultIsEffective` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValue` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForDictionaryWithAsManyKeysAsLimit` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForArrayAsLongAsLimit` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Events.LogEventPropertyValueTests.WhenDestructuringAKnownLiteralTypeIsScalar` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormat` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringWithCustomExtensionMethodIsApplied` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedObject` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumDepthIsApplied` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Formatting.Display.MessageTemplateTextFormatterTests.AnEmptyPropertiesTokenIsAnEmptyStructureValueWithDefaultFormatting` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForArrayThanLimit` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormatProvider` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedString` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumStringLengthIsApplied` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedString` (assembly `serilog.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForDictionaryWithMoreKeysThanLimit&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedObject&FullyQualifiedName!~Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumCollectionCountIsApplied&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthIsEffective&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForObject&FullyQualifiedName!~Serilog.Tests.Capturing.PropertyValueConverterTests.MaximumDepthIsEffectiveAndThreadSafe&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthNOTEffectiveForString&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumDestructuringDepthDefaultIsEffective&FullyQualifiedName!~Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValue&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForDictionaryWithAsManyKeysAsLimit&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountNotEffectiveForArrayAsLongAsLimit&FullyQualifiedName!~Serilog.Tests.Events.LogEventPropertyValueTests.WhenDestructuringAKnownLiteralTypeIsScalar&FullyQualifiedName!~Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormat&FullyQualifiedName!~Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringWithCustomExtensionMethodIsApplied&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedObject&FullyQualifiedName!~Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumDepthIsApplied&FullyQualifiedName!~Serilog.Tests.Formatting.Display.MessageTemplateTextFormatterTests.AnEmptyPropertiesTokenIsAnEmptyStructureValueWithDefaultFormatting&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumCollectionCountEffectiveForArrayThanLimit&FullyQualifiedName!~Serilog.Tests.Events.LogEventPropertyValueTests.AScalarValueToStringRendersTheValueUsingFormatProvider&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForStringifiedString&FullyQualifiedName!~Serilog.Tests.Settings.KeyValuePairSettingsTests.DestructuringToMaximumStringLengthIsApplied&FullyQualifiedName!~Serilog.Tests.Configuration.LoggerConfigurationTests.MaximumStringLengthEffectiveForCapturedString`
- Regression-net project: `test/Serilog.Tests/Serilog.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/Serilog/Events/LogEventPropertyValue.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.Contains()`, Calor=`Assert.Contains()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 40.0%

## Defect stratum: **Expressible**

- Verification-addressable: the mutation makes Calor's **Calor0410** fire on the 
  converted arm — a signal the C# compiler has no equivalent of. The C# arm's agent may ship the 
  defect; the Calor arm's agent is confronted by the diagnostic. Calor0410 is INTRODUCED by the mutation (fires on the mutated conversion, absent on the clean one) — verification-addressable.

  > **Papering-over residual (preserved by design):** the agent can clear the Calor build by 
  > REMOVING the injected effect (correct → held-out passes → caught) OR by DECLARING it in §E 
  > (papers over → the bug still ships → held-out fails → escaped). Which path the agent takes IS 
  > the measurement; both remain possible.
