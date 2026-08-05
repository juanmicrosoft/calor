# Task bundle: serilog-injectedmutation-cand13-EffectViolation

- Project: **Serilog**
- Mutation source: **InjectedMutation**
- Operator: `inject undeclared `fs` effect (using-nested Directory.Exists) that corrupts Equals's return (boolean flip)` (EffectViolation)
- Mutated region: `src/Serilog/Parsing/TextToken.cs` line 58, col 26

## Failing-behavior report (BOTH arms receive this — the symptom, NOT the test)

> Observed incorrect behavior: Assert.Equal() Failure: Collections differ
>
> Subject hint: MessageTemplateParser
>
> Symptom derived mechanically from a removed covering test and scrubbed of the test's identity. The removed test is held out; the full suite remains as the regression net.

## Arms

- `csharp-arm/` — idiomatic original C# carrying the mutation.
- `calor-arm/`  — mutate-then-convert output (converted §-syntax round-tripped to C#) carrying the same mutation.

Presentation asymmetry (recorded): Calor arm = machine-converted round-tripped C# (from §-syntax); C# arm = idiomatic original. Bias is against Calor.

## Held-out test(s) — removed from the visible suite, kept as the regression net

- `Serilog.Tests.Parsing.MessageTemplateParserTests.MultipleTokensHasCorrectIndexes` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAreParsedAsASingleBracket` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracket` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithoutPropertiesIsASingleTextToken` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DestructureWithInvalidHintsIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracketInsideText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithAMalformedPropertyTagIsParsedAsManyTextTokens` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.AMalformedPropertyTagIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DestructuringWithEmptyPropertyNameIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.EmptyAlignmentIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.NonNumberAlignmentIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.AlignmentWithPositiveSignParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAfterOneLeftIsParsedAPropertyTokenAndATextToken` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledBracketsAreParsedAsASingleBracket` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.MissingRightBracketIsParsedAsText` (assembly `serilog.tests.dll`)
- `Serilog.Tests.Parsing.MessageTemplateParserTests.ATrailingUnmatchedBracketIsParsedAsText` (assembly `serilog.tests.dll`)

- Visible-suite filter: `FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.MultipleTokensHasCorrectIndexes&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAreParsedAsASingleBracket&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracket&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithoutPropertiesIsASingleTextToken&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DestructureWithInvalidHintsIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledLeftBracketsAreParsedAsASingleBracketInsideText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.AMessageWithAMalformedPropertyTagIsParsedAsManyTextTokens&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.AMalformedPropertyTagIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DestructuringWithEmptyPropertyNameIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.EmptyAlignmentIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.NonNumberAlignmentIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.AlignmentWithPositiveSignParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledRightBracketsAfterOneLeftIsParsedAPropertyTokenAndATextToken&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.DoubledBracketsAreParsedAsASingleBracket&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.MissingRightBracketIsParsedAsText&FullyQualifiedName!~Serilog.Tests.Parsing.MessageTemplateParserTests.ATrailingUnmatchedBracketIsParsedAsText`
- Regression-net project: `test/Serilog.Tests/Serilog.Tests.csproj` (full suite; escaped bug = held-out failure at declared-done)

## Native-eligibility proof (D-W4.1)

- Clause (a): mutated file `src/Serilog/Parsing/TextToken.cs` — ConvertedNative=**True** (Status=Replaced, LossCount=0)
- Clause (b): held-out test outcome — C# arm=**Failed**, Calor arm=**Failed**
  - failure signatures — C#=`Assert.Equal()`, Calor=`Assert.Equal()`
- D-W4.3 attribution: **AttributedToMutation**
- Project NativeFraction at generation: 40.0%

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
