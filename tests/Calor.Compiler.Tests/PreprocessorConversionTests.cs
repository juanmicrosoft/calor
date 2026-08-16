using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for preprocessor directive (#if/#else/#endif) conversion from C# to Calor.
/// Validates that trivia-based preprocessor regions are correctly wrapped in §PP blocks.
/// </summary>
public class PreprocessorConversionTests
{
    private static string ConvertToCalor(string csharpSource)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    private static string CompileCalorToCSharp(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}"));
    }

    [Fact]
    public void Converter_SimpleIfEndif_ProducesPreprocessorBlock()
    {
        var csharp = @"
public class Test
{
    public void M()
    {
#if DEBUG
        var x = 1;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
    }

    [Fact]
    public void Converter_IfElseEndif_PreservesBothBranches()
    {
        var csharp = @"
public class Test
{
    public void M()
    {
#if DEBUG
        var x = 1;
#else
        var x = 2;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§PPE", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
    }

    [Fact]
    public void Converter_CustomCondition_PreservesConditionText()
    {
        var csharp = @"
public class Test
{
    public void M()
    {
#if NET8_0_OR_GREATER
        var x = 1;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET8_0_OR_GREATER}", calor);
    }

    [Fact]
    public void Converter_PreprocessorDirective_RoundTripsCalorToCSharp()
    {
        // Verify the Calor parser and C# emitter can round-trip preprocessor blocks
        var calor = @"
§M{m1:Test}
    §CL{c1:Test}
        §MT{mt1:M:pub}
            §O{void}
            §PP{DEBUG}
              §B{x} INT:1
            §/PP{DEBUG}
";
        var csharp = CompileCalorToCSharp(calor);
        Assert.Contains("#if DEBUG", csharp);
        Assert.Contains("#endif", csharp);
    }

    [Fact]
    public void Converter_FullyDisabledIf_RecoverBodyFromTrivia()
    {
        // When no symbol is defined and there's no #else, the entire #if body
        // is DisabledTextTrivia — verify it's recovered as a §PP block
        var csharp = @"
public class Test
{
    public void M()
    {
#if SOME_UNDEFINED_SYMBOL
        var x = 42;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{SOME_UNDEFINED_SYMBOL}", calor);
        Assert.Contains("§/PP{SOME_UNDEFINED_SYMBOL}", calor);
        // The body should contain a reference to x (may be raw text or bind depending on trivia recovery)
        Assert.True(calor.Contains("§B{x}") || calor.Contains("var x") || calor.Contains("x = 42"),
            $"Expected disabled block to contain x declaration. Output: {calor}");
    }

    [Fact]
    public void Converter_NestedIf_OuterRegionContainsInnerContent()
    {
        // Nested #if inside outer #if — the outer region should encompass the inner
        // Without symbols defined, the entire thing is disabled text on the close brace
        var csharp = @"
public class Test
{
    public void M()
    {
#if NET8_0_OR_GREATER
        var x = 1;
#if DEBUG
        var y = 2;
#endif
        var z = 3;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        // Should have an outer PP block for NET8_0_OR_GREATER
        Assert.Contains("§PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET8_0_OR_GREATER}", calor);
    }

    [Fact]
    public void Converter_IfElseEndif_ElseBranchPreservesStatements()
    {
        // Verify that the #else branch body content is actually preserved
        var csharp = @"
public class Test
{
    public int M()
    {
#if DEBUG
        return 1;
#else
        return 2;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§PPE", calor);
        var generated = CompileCalorToCSharp(calor);
        Assert.Contains("return 1", generated);
        Assert.Contains("return 2", generated);
    }

    [Fact]
    public void Converter_IfElifEndif_ProducesNestedPreprocessorBlocks()
    {
        // #if/#elif/#endif → nested §PP blocks
        var csharp = @"
public class Test
{
    public int M()
    {
#if DEBUG
        return 1;
#elif TRACE
        return 2;
#else
        return 3;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        // Should have outer §PP{DEBUG} with nested §PP{TRACE} in its else
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§PP{TRACE}", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
        Assert.Contains("§/PP{TRACE}", calor);
    }

    [Fact]
    public void Converter_IfElifElseEndif_PreservesAllBranches()
    {
        // #if/#elif/#else/#endif → nested §PP with all 3 branches preserved
        var csharp = @"
public class Test
{
    public int M()
    {
#if NET8_0_OR_GREATER
        return 1;
#elif NET6_0_OR_GREATER
        return 2;
#else
        return 3;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§PP{NET6_0_OR_GREATER}", calor);
        var generated = CompileCalorToCSharp(calor);
        Assert.Contains("return 1", generated);
        Assert.Contains("return 2", generated);
        Assert.Contains("return 3", generated);
    }

    [Fact]
    public void Converter_IfElifElseEndif_RoundTripsCalorToCSharp()
    {
        // Verify nested §PP round-trips back to C# #if/#else/#endif
        var calor = @"
§M{m1:Test}
    §CL{c1:Test}
        §MT{mt1:M:pub}
            §O{i32}
            §PP{NET8_0_OR_GREATER}
              §R INT:1
            §PPE
              §PP{NET6_0_OR_GREATER}
                §R INT:2
              §PPE
                §R INT:3
              §/PP{NET6_0_OR_GREATER}
            §/PP{NET8_0_OR_GREATER}
";
        var csharp = CompileCalorToCSharp(calor);
        // The C# output should have the nested #if structure
        Assert.Contains("#if NET8_0_OR_GREATER", csharp);
        Assert.Contains("#if NET6_0_OR_GREATER", csharp);
        // Count #endif — should have at least 2
        var endifCount = csharp.Split("#endif").Length - 1;
        Assert.True(endifCount >= 2, $"Expected at least 2 #endif, got {endifCount}");
    }

    // ========================
    // Member-level preprocessor tests
    // ========================

    [Fact]
    public void Converter_MemberLevelIf_ProducesPreprocessorBlock()
    {
        var csharp = @"
public class Test
{
#if DEBUG
    public void DebugMethod() { }
#endif
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
    }

    [Fact]
    public void Converter_MemberLevelIfElse_PreservesBothBranches()
    {
        var csharp = @"
public class Test
{
#if DEBUG
    private int _debugField;
#else
    private int _releaseField;
#endif
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§PPE", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
    }

    [Fact]
    public void Converter_MemberLevelMultipleMembers_GroupedInBlock()
    {
        var csharp = @"
public class Test
{
#if DEBUG
    private int _debugField;
    public void DebugMethod() { }
#endif
    public void AlwaysPresent() { }
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
        // The always-present method should NOT be inside the PP block
        Assert.Contains("AlwaysPresent", calor);
    }

    [Fact]
    public void Converter_MemberLevelIfElif_ProducesNestedBlocks()
    {
        var csharp = @"
public class Test
{
#if NET8_0_OR_GREATER
    public void NetEight() { }
#elif NET6_0_OR_GREATER
    public void NetSix() { }
#else
    public void Legacy() { }
#endif
}";
        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
        var calor = new CalorEmitter().Emit(result.Ast!);
        Assert.Contains("§PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§PP{NET6_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET6_0_OR_GREATER}", calor);
    }

    [Fact]
    public void Converter_MemberLevelDisabledRecovery_ParsesMembers()
    {
        // When no symbol is defined, the #if body is all disabled text
        // The converter should re-parse it and recover the members
        var csharp = @"
public class Test
{
#if SOME_UNDEFINED_SYMBOL
    public void DisabledMethod() { }
#endif
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{SOME_UNDEFINED_SYMBOL}", calor);
        Assert.Contains("§/PP{SOME_UNDEFINED_SYMBOL}", calor);
    }

    [Fact]
    public void Converter_MemberLevelPP_RoundTripsCalorToCSharp()
    {
        var calor = @"
§M{m1:Test}
    §CL{c1:Test}
        §PP{DEBUG}
          §MT{mt1:DebugMethod:pub}
              §O{void}
        §/PP{DEBUG}
";
        var csharp = CompileCalorToCSharp(calor);
        Assert.Contains("#if DEBUG", csharp);
        Assert.Contains("#endif", csharp);
        Assert.Contains("DebugMethod", csharp);
    }

    [Fact]
    public void Converter_StructMemberLevelIf_Works()
    {
        var csharp = @"
public struct TestStruct
{
#if DEBUG
    public int DebugValue;
#endif
    public int AlwaysValue;
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("§/PP{DEBUG}", calor);
        Assert.Contains("AlwaysValue", calor);
    }

    [Fact]
    public void Converter_MixedStatementAndMemberPP_BothWork()
    {
        var csharp = @"
public class Test
{
#if DEBUG
    private int _debugField;
#endif
    public int M()
    {
#if DEBUG
        return 1;
#else
        return 2;
#endif
    }
}";
        var calor = ConvertToCalor(csharp);
        // Should have PP blocks at both member and statement level
        var ppCount = calor.Split("§PP{DEBUG}").Length - 1;
        Assert.True(ppCount >= 2, $"Expected at least 2 §PP{{DEBUG}} blocks, got {ppCount}. Output:\n{calor}");
    }

    [Fact]
    public void Converter_MemberLevelActiveBranch_PreservesActiveMembers()
    {
        // Use #if true so Roslyn parses the #if body as active (real parsed members)
        // This exercises the path where ActiveStart < ActiveEnd in ExtractMemberPreprocessorRegions
        var csharp = @"
public class Test
{
#if true
    public int ActiveField;
    public void ActiveMethod() { }
#else
    public int InactiveField;
#endif
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{true}", calor);
        Assert.Contains("§PPE", calor);
        Assert.Contains("§/PP{true}", calor);
        // Active members should be present
        Assert.Contains("ActiveField", calor);
        Assert.Contains("ActiveMethod", calor);
        // Inactive member should also be recovered from disabled text
        Assert.Contains("InactiveField", calor);
    }

    [Fact]
    public void Converter_MemberLevelActiveBranchMultiple_GroupsCorrectly()
    {
        // #if true wrapping 2 members + an unconditional member after
        var csharp = @"
public class Test
{
#if true
    public int ConditionalField;
#endif
    public int AlwaysField;
}";
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{true}", calor);
        Assert.Contains("§/PP{true}", calor);
        Assert.Contains("ConditionalField", calor);
        Assert.Contains("AlwaysField", calor);
    }

    [Fact]
    public void Converter_ActiveMemberBranchBeforeFollowingMember_PreservesInactiveBranch()
    {
        const string csharp = """
            public class Test
            {
            #if true
                public int ActiveField;
            #else
                public int InactiveField;
            #endif
                public int TailField;
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "MemberTrailing.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("ActiveField", result.CalorSource);
        Assert.Contains("InactiveField", result.CalorSource);
        Assert.Contains("TailField", result.CalorSource);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.True(
            generated.IndexOf("#if true", StringComparison.Ordinal)
            < generated.IndexOf("TailField", StringComparison.Ordinal));
    }

    [Fact]
    public void Converter_RealisticHumanizerPattern_MemberLevelPP_FullRoundTrip()
    {
        // Realistic pattern from Humanizer-style code: platform-specific implementations
        // with properties, fields, and methods guarded by #if
        var csharp = @"
using System;

public static class NumberToWordsExtension
{
    private static readonly string[] UnitsMap = { ""zero"", ""one"", ""two"" };

#if NET6_0_OR_GREATER
    public static string ToWords(this int number)
    {
        return UnitsMap[number];
    }

    public static ReadOnlySpan<char> ToWordsSpan(this int number)
    {
        return UnitsMap[number].AsSpan();
    }
#else
    public static string ToWords(this int number)
    {
        return UnitsMap[number];
    }
#endif

    public static string Format(int value)
    {
        return value.ToString();
    }
}";
        // Step 1: Convert C# to Calor
        var calor = ConvertToCalor(csharp);

        // Should have member-level PP block
        Assert.Contains("§PP{NET6_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET6_0_OR_GREATER}", calor);
        Assert.Contains("§PPE", calor);

        // Unconditional members should still be present
        Assert.Contains("UnitsMap", calor);
        Assert.Contains("Format", calor);

        // Step 2: Round-trip back to C#
        var csharpOutput = CompileCalorToCSharp(calor);

        // C# output should have the #if structure
        Assert.Contains("#if NET6_0_OR_GREATER", csharpOutput);
        Assert.Contains("#endif", csharpOutput);
        Assert.Contains("Format", csharpOutput);
    }

    [Fact(Timeout = 10_000)]
    public void Converter_AdjacentIfBlocks_DoesNotHang()
    {
        // Adjacent #if blocks where the second starts immediately after the first ends.
        // This previously caused an infinite loop when endIdx == i in ExtractPreprocessorRegions.
        var csharp = @"
public class Test
{
    public void M()
    {
#if DEBUG
        var x = 1;
#endif
#if TRACE
        var y = 2;
#endif
    }
}";
        // The primary assertion: conversion completes without hanging (10s timeout)
        var calor = ConvertToCalor(csharp);
        // At least the first #if should be captured
        Assert.Contains("§PP{DEBUG}", calor);
    }

    [Fact]
    public void Converter_ActiveStatementBranchBeforeFollowingStatement_PreservesInactiveBranch()
    {
        const string csharp = """
            public class Test
            {
                public int Value()
                {
            #if true
                    int value = 1;
            #else
                    int value = 2;
            #endif
                    return value;
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "StatementTrailing.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Contains("value = 1", generated);
        Assert.Contains("value = 2", generated);
        Assert.True(
            generated.IndexOf("#endif", StringComparison.Ordinal)
            < generated.IndexOf("return value", StringComparison.Ordinal));
    }

    [Fact(Timeout = 10_000)]
    public void Converter_AdjacentMemberLevelIfBlocks_DoesNotHang()
    {
        // Adjacent #if blocks at member level — previously could stall
        // in ExtractMemberPreprocessorRegions.
        var csharp = @"
public class Test
{
#if DEBUG
    public void DebugOnly() { }
#endif
#if RELEASE
    public void ReleaseOnly() { }
#endif
    public void Always() { }
}";
        // The primary assertion: conversion completes without hanging (10s timeout)
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{DEBUG}", calor);
        Assert.Contains("Always", calor);
    }

    [Fact(Timeout = 10_000)]
    public void Converter_IfInsideSwitchCase_DoesNotHang()
    {
        // #if inside a switch/case — a pattern found in Newtonsoft.Json that caused hangs.
        // The switch itself may be emitted as a CSharpInteropBlock, but conversion must complete.
        var csharp = @"
public class Test
{
    public int M(int x)
    {
        switch (x)
        {
            case 1:
#if DEBUG
                return 10;
#else
                return 11;
#endif
            default:
                return 0;
        }
    }
}";
        // The primary assertion: conversion completes without hanging (10s timeout)
        var calor = ConvertToCalor(csharp);
        Assert.NotNull(calor);
    }

    [Fact]
    public void Converter_NestedMemberAndStatementPP_BothLevelsCompose()
    {
        // Member-level #if wrapping a method whose body contains a statement-level #if.
        // Both levels should produce independent §PP blocks that compose correctly.
        var csharp = @"
public class Test
{
#if NET6_0_OR_GREATER
    public int Compute(int x)
    {
#if DEBUG
        return x * 2;
#else
        return x;
#endif
    }
#endif
}";
        var calor = ConvertToCalor(csharp);

        // Nested conditional ownership is conservatively retained in one complete
        // type boundary rather than detached nested blocks.
        Assert.Contains("§CSHARP", calor);
        Assert.Contains("#if NET6_0_OR_GREATER", calor);
        Assert.Contains("#if DEBUG", calor);

        // Round-trip: parse Calor back to C#
        var csharpOutput = CompileCalorToCSharp(calor);
        Assert.Contains("#if NET6_0_OR_GREATER", csharpOutput);
        Assert.Contains("#endif", csharpOutput);
    }

    [Fact]
    public void Converter_DefaultOptions_PreserveFalseAndElseBranches()
    {
        const string csharp = """
            public class Test
            {
                public int Value()
                {
            #if false
                    return 1;
            #else
                    return 2;
            #endif
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Default.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal(PreprocessorConversionMode.PreserveAllBranches, result.Metadata.PreprocessorMode);
        Assert.Contains("§PP{false}", result.CalorSource);
        Assert.Contains("§PPE", result.CalorSource);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Contains("return 1", generated);
        Assert.Contains("return 2", generated);
        Assert.DoesNotContain(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.PreprocessorStripped);
    }

    [Fact]
    public void Converter_ParseOptionsAndSymbols_AreAppliedAndRecorded()
    {
        const string csharp = """
            #if BASE && EXTRA
            public class Enabled { }
            #else
            public class Disabled { }
            #endif
            """;
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp13,
            DocumentationMode.Diagnose,
            SourceCodeKind.Regular,
            ["BASE"]);

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            ParseOptions = parseOptions,
            DefinedSymbols = ["EXTRA"],
            Configuration = "Custom"
        }).Convert(csharp, "Symbols.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal("CSharp13", result.Metadata.LanguageVersion);
        Assert.Equal("Diagnose", result.Metadata.DocumentationMode);
        Assert.Equal("Regular", result.Metadata.SourceCodeKind);
        Assert.Equal("Custom", result.Metadata.Configuration);
        Assert.Equal(["BASE", "EXTRA"], result.Metadata.DefinedSymbols);
        Assert.Contains("Enabled", result.CalorSource);
        Assert.Contains("Disabled", result.CalorSource);
    }

    [Fact]
    public void Converter_SelectedBranchLossy_UsesRoslynAndRecordsEveryRemovedDirective()
    {
        const string csharp = """
            #if FEATURE
            public class Selected { }
            #elif OTHER
            public class Other { }
            #else
            public class Fallback { }
            #endif
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            DefinedSymbols = ["FEATURE"]
        }).Convert(csharp, "Selected.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("Selected", result.CalorSource);
        Assert.DoesNotContain("Other", result.CalorSource);
        Assert.DoesNotContain("Fallback", result.CalorSource);
        Assert.DoesNotContain("§PP", result.CalorSource);
        Assert.Equal(4, result.Losses.Count(
            loss => loss.Kind == ConversionLossKind.PreprocessorStripped));
        Assert.Equal(
            PreprocessorConversionMode.SelectActiveBranchLossy,
            result.Metadata.PreprocessorMode);
    }

    [Fact]
    public void Converter_SelectedBranchMode_RequiresLossyFidelity()
    {
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert("#if true\nclass C { }\n#endif");

        Assert.False(result.Success);
        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "preprocessor-selected-branch");
    }

    [Fact]
    public void Converter_SelectedBranchCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode =
                PreprocessorConversionMode.SelectActiveBranchLossy
        });

        Assert.ThrowsAny<OperationCanceledException>(
            () => converter.Convert(
                "#if true\nclass C { }\n#endif",
                "Cancelled.cs",
                cts.Token));
    }

    [Fact]
    public void Converter_ConditionalPartialDeclarations_PreserveBothApiShapes()
    {
        const string csharp = """
            #if FEATURE
            public partial class PartialApi
            {
                public int FeatureValue() => 1;
            }
            #else
            public partial class PartialApi
            {
                public int FallbackValue() => 2;
            }
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Partial.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);

        var feature = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "feature.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                PreprocessorSymbols = ["FEATURE"]
            });
        var fallback = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "fallback.g.cs")],
            new GeneratedCSharpCompilationContext());

        Assert.True(feature.CompilationSuccess, string.Join("\n", feature.FormattedCompilationErrors));
        Assert.True(fallback.CompilationSuccess, string.Join("\n", fallback.FormattedCompilationErrors));
        Assert.Contains("FeatureValue", generated);
        Assert.Contains("FallbackValue", generated);
    }

    [Fact]
    public void Converter_ConditionalInterfaceMembers_PreserveBothBranches()
    {
        const string csharp = """
            public interface IConditional
            {
            #if FEATURE
                int FeatureValue();
            #else
                string FallbackValue();
            #endif
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Interface.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("§PP{FEATURE}", result.CalorSource);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Contains("FeatureValue", generated);
        Assert.Contains("FallbackValue", generated);
        var feature = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "interface-feature.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                PreprocessorSymbols = ["FEATURE"]
            });
        var fallback = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "interface-fallback.g.cs")],
            new GeneratedCSharpCompilationContext());
        Assert.True(feature.CompilationSuccess, string.Join("\n", feature.FormattedCompilationErrors));
        Assert.True(fallback.CompilationSuccess, string.Join("\n", fallback.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_PragmaWarningScope_IsPreservedForWarningsAsErrors()
    {
        const string csharp = """
            #pragma warning disable CS0169
            public class WarningScope
            {
                private int _unused;
            }
            #pragma warning restore CS0169
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Pragma.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "pragma.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                TreatWarningsAsErrors = true
            });

        Assert.Contains("#pragma warning disable CS0169", generated);
        Assert.Contains("#pragma warning restore CS0169", generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_DirectiveInsideConditional_RemainsConditionalAndIsNotDuplicated()
    {
        const string csharp = """
            #if FEATURE
            #error feature-only
            #endif
            public class DirectiveHost { }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "ConditionalDirective.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal(1, result.CalorSource!.Split("#error feature-only").Length - 1);
        var generated = CompileCalorToCSharp(result.CalorSource);
        Assert.Equal(1, generated.Split("#error feature-only").Length - 1);
        Assert.True(
            generated.IndexOf("#if FEATURE", StringComparison.Ordinal)
            < generated.IndexOf("#error feature-only", StringComparison.Ordinal));
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "conditional-directive.g.cs")],
            new GeneratedCSharpCompilationContext());
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Theory]
    [InlineData(
        """
        public class DirectiveHost
        {
        #if FEATURE
        #error member-feature-only
        #endif
            public int Value;
        }
        """,
        "#error member-feature-only")]
    [InlineData(
        """
        public class DirectiveHost
        {
            public void Run()
            {
        #if FEATURE
        #error statement-feature-only
        #endif
            }
        }
        """,
        "#error statement-feature-only")]
    public void Converter_DirectiveInsideMemberOrStatementConditional_DoesNotEscape(
        string csharp,
        string directive)
    {
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "ScopedConditionalDirective.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        var directiveIndex = generated.IndexOf(directive, StringComparison.Ordinal);
        var ifIndex = generated.LastIndexOf(
            "#if FEATURE",
            directiveIndex,
            StringComparison.Ordinal);
        var endIndex = generated.IndexOf(
            "#endif",
            directiveIndex,
            StringComparison.Ordinal);
        Assert.True(ifIndex >= 0 && ifIndex < directiveIndex);
        Assert.True(directiveIndex < endIndex);
        Assert.Equal(1, generated.Split(directive).Length - 1);
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "scoped-directive.g.cs")],
            new GeneratedCSharpCompilationContext());
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_ConditionalMembersAndDeclarations_RetainSourceOrder()
    {
        const string csharp = """
            public class Before { public static int Value = 0; }
            #if FEATURE
            public class Conditional { public static int Value = 1; }
            #endif
            public class After { public static int Value = 2; }

            public class FieldOrder
            {
                public static int BeforeField = 0;
            #if FEATURE
                public static int ConditionalField = 1;
            #endif
                public static int AfterField = 2;
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Order.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);

        Assert.True(
            generated.IndexOf("class Before", StringComparison.Ordinal)
            < generated.IndexOf("#if FEATURE", StringComparison.Ordinal));
        Assert.True(
            generated.IndexOf("#if FEATURE", StringComparison.Ordinal)
            < generated.IndexOf("class After", StringComparison.Ordinal));
        Assert.True(
            generated.IndexOf("BeforeField", StringComparison.Ordinal)
            < generated.IndexOf("ConditionalField", StringComparison.Ordinal));
        Assert.True(
            generated.IndexOf("ConditionalField", StringComparison.Ordinal)
            < generated.IndexOf("AfterField", StringComparison.Ordinal));
    }

    [Fact]
    public void ConversionOptions_RetainsDeprecatedStripPreprocessorCompatibilityProperty()
    {
        Assert.NotNull(typeof(ConversionOptions).GetProperty("StripPreprocessor"));
    }

    [Fact]
    public void ConversionOptions_LegacyStripPreprocessor_RemainsAcceptedAndUsesRoslyn()
    {
#pragma warning disable CS0618
        var options = new ConversionOptions { StripPreprocessor = true };
#pragma warning restore CS0618
        var result = new CSharpToCalorConverter(options).Convert(
            "#if false\npublic class Dead { }\n#else\npublic class Live { }\n#endif");

        Assert.Equal(ConversionFidelity.Lossy, options.Fidelity);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("Live", result.CalorSource);
        Assert.DoesNotContain("Dead", result.CalorSource);
    }

    [Fact]
    public void Converter_ConditionalDefine_UsesWholeUnitWithoutGeneratedTokens()
    {
        const string csharp = """
            #if FEATURE
            #define INNER
            #endif
            #if INNER
            public class InnerEnabled { }
            #else
            public class InnerDisabled { }
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(csharp, "Define.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);

        Assert.StartsWith("#if FEATURE", generated);
        Assert.DoesNotContain("// <auto-generated>", generated);
        Assert.Contains("#define INNER", generated);
        var enabled = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "define-enabled.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                PreprocessorSymbols = ["FEATURE"]
            });
        var disabled = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "define-disabled.g.cs")],
            new GeneratedCSharpCompilationContext());
        Assert.True(enabled.CompilationSuccess, string.Join("\n", enabled.FormattedCompilationErrors));
        Assert.True(disabled.CompilationSuccess, string.Join("\n", disabled.FormattedCompilationErrors));
    }

    [Theory]
    [InlineData("#nullable disable")]
    [InlineData("#warning preserved-warning")]
    [InlineData("#line 200 \"mapped.cs\"")]
    public void Converter_NonconditionalDirective_IsPreservedVerbatim(string directive)
    {
        var result = new CSharpToCalorConverter().Convert(
            $"{directive}\npublic class DirectiveHost {{ }}",
            "Directive.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains(directive, result.CalorSource);
        Assert.Contains(directive, CompileCalorToCSharp(result.CalorSource!));
        Assert.Contains(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.InteropPreserved);
    }

    [Fact]
    public void Converter_ErrorDirective_IsPreservedEvenThoughCompilationFails()
    {
        const string directive = "#error preserved-error";
        var result = new CSharpToCalorConverter().Convert(
            $"{directive}\npublic class DirectiveHost {{ }}",
            "ErrorDirective.cs");

        Assert.False(result.Success);
        Assert.Contains(directive, result.CalorSource);
        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "active-error-directive");
    }

    [Fact]
    public async Task ProjectMigration_UsesMsBuildConfigurationSymbolsAndRecordsMetadata()
    {
        var debug = await MigrateConfigurationAsync("Debug", "DEBUG_BRANCH");
        var release = await MigrateConfigurationAsync("Release", "RELEASE_BRANCH");

        Assert.Contains("§PP{DEBUG_BRANCH}", debug.Calor);
        Assert.Contains("§PP{DEBUG_BRANCH}", release.Calor);
        Assert.Contains("DEBUG_BRANCH", debug.Metadata.DefinedSymbols);
        Assert.Contains("RELEASE_BRANCH", release.Metadata.DefinedSymbols);
        Assert.Equal("Debug", debug.Metadata.Configuration);
        Assert.Equal("Release", release.Metadata.Configuration);

        static async Task<(string Calor, ConversionMetadata Metadata)> MigrateConfigurationAsync(
            string configuration,
            string expectedSymbol)
        {
            var directory = Path.Combine(
                AppContext.BaseDirectory,
                $"issue772-project-{configuration}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var projectPath = Path.Combine(directory, "Conditional.csproj");
            var sourcePath = Path.Combine(directory, "Conditional.cs");
            var outputPath = Path.ChangeExtension(sourcePath, ".calr");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <DefineConstants Condition="'$(Configuration)' == 'Debug'">$(DefineConstants);DEBUG_BRANCH</DefineConstants>
                    <DefineConstants Condition="'$(Configuration)' == 'Release'">$(DefineConstants);RELEASE_BRANCH</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                sourcePath,
                """
                #if DEBUG_BRANCH
                public class ConditionalApi { public int Value() => 1; }
                #elif RELEASE_BRANCH
                public class ConditionalApi { public int Value() => 2; }
                #else
                public class ConditionalApi { public int Value() => 3; }
                #endif
                """);
            await RestoreAsync(projectPath);

            var plan = new MigrationPlan
            {
                ProjectPath = directory,
                ProjectFilePath = projectPath,
                Direction = MigrationDirection.CSharpToCalor,
                Entries =
                [
                    new MigrationPlanEntry
                    {
                        SourcePath = sourcePath,
                        OutputPath = outputPath,
                        Convertibility = FileConvertibility.Full,
                        FileSizeBytes = new FileInfo(sourcePath).Length
                    }
                ]
            };

            try
            {
                var report = await new ProjectMigrator(new MigrationPlanOptions
                {
                    Configuration = configuration,
                    Parallel = false,
                    MergePartialClasses = false
                }).ExecuteAsync(plan);
                var file = Assert.Single(report.FileResults);
                Assert.True(
                    file.Status is FileMigrationStatus.Success or FileMigrationStatus.Partial,
                    string.Join(Environment.NewLine, file.Issues));
                Assert.Contains(expectedSymbol, file.Metadata!.DefinedSymbols);
                return (await File.ReadAllTextAsync(outputPath), file.Metadata);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        static async Task RestoreAsync(string projectPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--ignore-failed-sources");
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
    }

    [Fact]
    public void PartialClassMerger_KeepsMergedClassInsideDirectiveScope()
    {
        var first = new CSharpToCalorConverter().Convert(
            """
            #pragma warning disable CS0169
            namespace Demo;
            public partial class PartialType { private int _first; }
            #pragma warning restore CS0169
            """,
            "First.cs");
        var second = new CSharpToCalorConverter().Convert(
            """
            namespace Demo;
            public partial class PartialType { private int _second; }
            """,
            "Second.cs");
        Assert.True(first.Success, GetErrorMessage(first));
        Assert.True(second.Success, GetErrorMessage(second));

        var merged = new PartialClassMerger().Merge([first.Ast!, second.Ast!]);
        var target = merged[0];
        var disableIndex = target.Items.ToList().FindIndex(item =>
            item is CSharpInteropBlockNode interop
            && interop.CSharpCode.Contains("disable CS0169", StringComparison.Ordinal));
        var classIndex = target.Items.ToList().FindIndex(item =>
            item is ClassDefinitionNode);
        var restoreIndex = target.Items.ToList().FindIndex(item =>
            item is CSharpInteropBlockNode interop
            && interop.CSharpCode.Contains("restore CS0169", StringComparison.Ordinal));

        Assert.True(disableIndex >= 0 && disableIndex < classIndex);
        Assert.True(classIndex < restoreIndex);
        Assert.Equal(2, Assert.Single(target.Classes).Fields.Count);
    }

    [Theory]
    [InlineData(
        """
        public class ConditionalExpression
        {
            private static int Choose(int value) => value;
            public int Value() => Choose(
        #if FEATURE
                1
        #else
                2
        #endif
            );
        }
        """)]
    [InlineData(
        """
        public class ConditionalAccessor
        {
            public int Value
            {
        #if FEATURE
                get => 1;
        #else
                get => 2;
        #endif
            }
        }
        """)]
    [InlineData(
        """
        using System;
        public class ConditionalLambda
        {
            public int Value()
            {
                Func<int> get = () =>
                {
        #if FEATURE
                    return 1;
        #else
                    return 2;
        #endif
                };
                return get();
            }
        }
        """)]
    [InlineData(
        """
        public class ConditionalInitializer
        {
            public int Value()
            {
                int value =
        #if FEATURE
                    1
        #else
                    2
        #endif
                    ;
                return value;
            }
        }
        """)]
    [InlineData(
        """
        public class ConditionalSwitchArm
        {
            public int Value(int input)
            {
                return input switch
                {
        #if FEATURE
                    1 => 10,
        #else
                    1 => 20,
        #endif
                    _ => 0
                };
            }
        }
        """)]
    [InlineData(
        """
        using System.Linq;
        public class ConditionalQuery
        {
            public int[] Value(int[] values)
            {
                var query =
                    from value in values
        #if FEATURE
                    where value > 0
        #else
                    where value < 0
        #endif
                    select value;
                return query.ToArray();
            }
        }
        """)]
    [InlineData(
        """
        public class ConditionalInterpolation
        {
            public string Value()
            {
                string value =
        #if FEATURE
                    $"{1}"
        #else
                    $"{2}"
        #endif
                    ;
                return value;
            }
        }
        """)]
    public void Converter_UnmodeledConditionalPlacement_PreservesWholeMember(
        string csharp)
    {
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "UnmodeledConditional.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("#if FEATURE", result.CalorSource);
        Assert.Contains("#else", result.CalorSource);
        Assert.Contains(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.InteropPreserved
                && (loss.Feature is "conditional-unmodeled-placement"
                    or "conditional-expression-fragment"));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "unmodeled.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    PreprocessorSymbols = symbols
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void Converter_ConditionalTopLevelStatements_PreservesWholeCompilationUnit()
    {
        const string csharp = """
            global using Text = System.Text;
            using System;
            #if FEATURE
            Console.WriteLine("feature");
            #else
            Console.WriteLine("fallback");
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "TopLevel.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal("_global", result.Ast!.Name);
        Assert.Contains("feature", result.CalorSource);
        Assert.Contains("fallback", result.CalorSource);
        Assert.Contains(
            result.Losses,
            loss => loss.Feature == "conditional-top-level-statement");
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.StartsWith(
            "global using Text = System.Text;",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain("// <auto-generated>", generated);
        Assert.DoesNotContain("#nullable enable", generated);
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "top-level.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    OutputKind = OutputKind.ConsoleApplication,
                    PreprocessorSymbols = symbols
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void PreservedConversion_ContainsNoInternalMarkersAndVerifiesNormally()
    {
        const string csharp = """
            #if FEATURE
            public record ConditionalApi(int Value);
            #else
            public sealed class ConditionalApi
            {
                public ConditionalApi(int value) { Value = value; }
                public int Value { get; }
            }
            #endif
            """;

        var conversion = new CSharpToCalorConverter()
            .Convert(csharp, "ConditionalApi.cs");

        Assert.True(conversion.Success, GetErrorMessage(conversion));
        Assert.DoesNotContain("§AP", conversion.CalorSource);
        Assert.DoesNotContain(
            "__CALOR_OPAQUE_BOUNDARY__",
            conversion.CalorSource);
        Assert.Contains(
            conversion.Losses,
            loss => loss.Kind
                == ConversionLossKind.InteropPreserved);
        Assert.Equal(
            SupportLevel.Partial,
            FeatureSupport.GetSupportLevel(
                "conditional-declaration"));
        var compilation = Program.Compile(
            conversion.CalorSource!,
            "ConditionalApi.calr",
            new CompilationOptions
            {
                DeferGeneratedOutputValidation = true,
                EnforceEffects = false
            });
        Assert.False(
            compilation.HasErrors,
            string.Join("\n", compilation.Diagnostics));
    }

    [Fact]
    public void Converter_NestedMalformedInactiveBranch_PreservesOriginalRemainder()
    {
        const string csharp = """
            #if ACTIVE
            public class Good { public int Value() => 1; }
            #else
            public class Broken { this is deliberately malformed
            #if INNER
            still malformed inner content
            #else
            other malformed inner content
            #endif
            }
            #endif
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            DefinedSymbols = ["ACTIVE"]
        }).Convert(csharp, "MalformedInactive.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("this is deliberately malformed", result.CalorSource);
        Assert.Contains("#if INNER", result.CalorSource);
        Assert.Contains("other malformed inner content", result.CalorSource);
        Assert.Contains(
            result.Losses,
            loss => loss.Feature is "preprocessor-ownership-fallback"
                or "preprocessor-unparsed-remainder");
        var generated = CompileCalorToCSharp(result.CalorSource!);
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "malformed-inactive.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                PreprocessorSymbols = ["ACTIVE"]
            });
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_NestedInactiveConditional_RetainsEnclosingDeclarationOwnership()
    {
        const string csharp = """
            #if ACTIVE
            public class Good { }
            #else
            public class Owner
            {
            #if INNER
                public int InnerValue;
            #else
                public int FallbackValue;
            #endif
            }
            #endif
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            DefinedSymbols = ["ACTIVE"]
        }).Convert(csharp, "NestedOwnership.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var outer = Assert.Single(result.Ast!.TypePreprocessorBlocks);
        var inactive = outer.ElseBranch!;
        Assert.Empty(inactive.NestedBlocks);
        var owner = Assert.Single(inactive.Classes);
        Assert.Equal("Owner", owner.Name);
        Assert.Single(owner.PreprocessorBlocks);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Contains("class Owner", generated);
        Assert.Contains("#if INNER", generated);
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(generated, "owner.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                PreprocessorSymbols = ["ACTIVE"]
            });
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_MalformedInactiveStatementBranch_IsNeverReparsedOrDropped()
    {
        const string csharp = """
            public class StatementHost
            {
                public int Value()
                {
            #if true
                    return 1;
            #else
                    deliberately malformed statement ???
            #if INNER
                    more malformed content
            #endif
            #endif
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "MalformedStatement.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("deliberately malformed statement", result.CalorSource);
        Assert.Contains("#if INNER", result.CalorSource);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        var validation = GeneratedCSharpCompiler.Validate(generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_SymbolDirectivesAndConditionals_RetainLexicalOrder()
    {
        const string csharp = """
            #define FIRST
            #if FIRST
            #undef FIRST
            #define SECOND
            #endif
            #if SECOND
            public class Selected { }
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "SymbolOrder.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);

        var defineFirst = generated.IndexOf("#define FIRST", StringComparison.Ordinal);
        var firstIf = generated.IndexOf("#if FIRST", StringComparison.Ordinal);
        var undefFirst = generated.IndexOf("#undef FIRST", StringComparison.Ordinal);
        var defineSecond = generated.IndexOf("#define SECOND", StringComparison.Ordinal);
        var secondIf = generated.IndexOf("#if SECOND", StringComparison.Ordinal);
        Assert.True(defineFirst < firstIf);
        Assert.True(firstIf < undefFirst);
        Assert.True(undefFirst < defineSecond);
        Assert.True(defineSecond < secondIf);
    }

    [Fact]
    public void Converter_InactiveErrorBeforeLaterDefine_RetainsCompletePreambleOrder()
    {
        const string csharp = """
            #if NEVER
            #error inactive-error
            #endif
            #define LATER
            #if LATER
            public class LaterSelected { }
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "PreambleOrder.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        var firstIf = generated.IndexOf("#if NEVER", StringComparison.Ordinal);
        var error = generated.IndexOf("#error inactive-error", StringComparison.Ordinal);
        var firstEnd = generated.IndexOf("#endif", error, StringComparison.Ordinal);
        var define = generated.IndexOf("#define LATER", StringComparison.Ordinal);
        Assert.True(firstIf >= 0 && firstIf < error);
        Assert.True(error < firstEnd);
        Assert.True(firstEnd < define);
        var validation = GeneratedCSharpCompiler.Validate(generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_ConditionalDefine_UsesWholeUnitPassthrough()
    {
        const string csharp = """
            #if FEATURE
            public class FeatureType { }
            #else
            #define FALLBACK
            #endif
            #if FALLBACK
            public class FallbackType { }
            #endif
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "ConditionalDefine.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.StartsWith("#if FEATURE", generated);
        Assert.DoesNotContain("// <auto-generated>", generated);
        Assert.Contains("#define FALLBACK", generated);
        var validation = GeneratedCSharpCompiler.Validate(generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));
    }

    [Fact]
    public void Converter_SharedSuffixConditionalStatement_PreservesWholeMember()
    {
        const string csharp = """
            public class SharedSuffix
            {
                public int Value()
                {
            #if FEATURE
                    return 1
            #else
                    return 2
            #endif
                    ;
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "SharedSuffix.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("return 1", result.CalorSource);
        Assert.Contains("return 2", result.CalorSource);
        var generated = CompileCalorToCSharp(result.CalorSource!);
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "shared-suffix.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    PreprocessorSymbols = symbols
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void Converter_AttributeBeforeConditionalInterop_DoesNotEmitDanglingDirective()
    {
        const string csharp = """
            [System.Obsolete]
            #if FEATURE
            public record FeatureRecord(int Value);
            #else
            public record FallbackRecord(int Value);
            #endif
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "AttributedConditional.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Equal(
            generated.Split("#if FEATURE").Length - 1,
            generated.Split("#endif").Length - 1);
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "attributed-conditional.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    PreprocessorSymbols = symbols
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void Converter_ConditionalNamespaces_PreserveWrappersAtCompilationUnitScope()
    {
        const string csharp = """
            #if FEATURE
            namespace FeatureNs { public class Selected { public int Value => 1; } }
            #else
            namespace FallbackNs { public class Selected { public int Value => 2; } }
            #endif
            """;

        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "ConditionalNamespace.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Contains(
            "namespace FeatureNs { public class Selected",
            generated);
        Assert.Contains(
            "namespace FallbackNs { public class Selected",
            generated);
        Assert.DoesNotContain(
            "namespace FeatureNs\n{\n    namespace FeatureNs",
            generated);
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "namespace.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    PreprocessorSymbols = symbols
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void PartialClassMerger_CanonicalizesLegacyAndExplicitItems()
    {
        var span = TextSpan.Empty;
        var legacyField = new ClassFieldNode(
            span, "Legacy", "i32", Visibility.Private,
            MethodModifiers.None, null, new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());
        var explicitField = new ClassFieldNode(
            span, "Explicit", "i32", Visibility.Private,
            MethodModifiers.None, null, new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());
        var secondField = new ClassFieldNode(
            span, "Second", "i32", Visibility.Private,
            MethodModifiers.None, null, new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());
        var unrelated = EmptyClass("Unrelated", isPartial: false);
        var firstPartial = EmptyClass(
            "Mixed",
            isPartial: true,
            fields: [legacyField, explicitField],
            items: [explicitField]);
        var secondPartial = EmptyClass(
            "Mixed",
            isPartial: true,
            fields: [secondField]);
        var firstModule = ModuleWithClasses(
            "Demo",
            [unrelated, firstPartial],
            items: [unrelated, firstPartial]);
        var secondModule = ModuleWithClasses("Demo", [secondPartial]);

        var merged = new PartialClassMerger().Merge(
            [firstModule, secondModule]);
        var target = merged[0];
        var mixed = Assert.Single(
            target.Classes,
            cls => cls.Name == "Mixed");

        Assert.Equal(
            ["Explicit", "Legacy", "Second"],
            mixed.Fields.Select(field => field.Name).OrderBy(name => name));
        Assert.Equal(
            3,
            mixed.Items.OfType<ClassFieldNode>()
                .Select(field => field.Name)
                .Distinct()
                .Count());
        Assert.Contains(target.Items, item => ReferenceEquals(item, unrelated));
        Assert.Contains(target.Items, item => ReferenceEquals(item, mixed));

        static ClassDefinitionNode EmptyClass(
            string name,
            bool isPartial,
            IReadOnlyList<ClassFieldNode>? fields = null,
            IReadOnlyList<AstNode>? items = null)
            => new(
                TextSpan.Empty, $"c{name}", name,
                false, false, isPartial, false, null,
                Array.Empty<string>(), Array.Empty<TypeParameterNode>(),
                fields ?? Array.Empty<ClassFieldNode>(),
                Array.Empty<PropertyNode>(),
                Array.Empty<ConstructorNode>(),
                Array.Empty<MethodNode>(),
                Array.Empty<EventDefinitionNode>(),
                Array.Empty<OperatorOverloadNode>(),
                new AttributeCollection(),
                Array.Empty<CalorAttributeNode>(),
                items: items);

        static ModuleNode ModuleWithClasses(
            string name,
            IReadOnlyList<ClassDefinitionNode> classes,
            IReadOnlyList<AstNode>? items = null)
            => new(
                TextSpan.Empty, $"m{name}", name,
                Array.Empty<UsingDirectiveNode>(),
                Array.Empty<InterfaceDefinitionNode>(),
                classes,
                Array.Empty<EnumDefinitionNode>(),
                Array.Empty<EnumExtensionNode>(),
                Array.Empty<DelegateDefinitionNode>(),
                Array.Empty<FunctionNode>(),
                new AttributeCollection(),
                Array.Empty<IssueNode>(),
                Array.Empty<AssumeNode>(),
                Array.Empty<InvariantNode>(),
                Array.Empty<DecisionNode>(),
                null,
                items: items);
    }

    [Fact]
    public void NestedType_RestoresOuterEmitterAndParserContext()
    {
        const string calor = """
            §M{m001:NestedContext}
              §CL{c001:Outer:pub}
                §CL{c002:Inner:pub}
                  §FLD{i32:Value:pub}
                §CTOR{ctor001:pub}
                §FLD{i32:Value:pub}
                §MT{m001:Get:pub} () -> i32
                  §R Value
            """;

        var module = ParseCalor(calor);
        var outer = Assert.Single(module.Classes);
        Assert.Single(outer.NestedClasses);
        Assert.Single(outer.Constructors);
        Assert.Single(outer.Methods);
        var generated = new CSharpEmitter().Emit(module);

        Assert.Contains("public Outer()", generated);
        Assert.Contains("int Get()", generated);
        var validation = GeneratedCSharpCompiler.Validate(generated);
        Assert.True(
            validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors));

        static ModuleNode ParseCalor(string source)
        {
            var diagnostics = new DiagnosticBag();
            var parser = new Parser(
                new Lexer(source, diagnostics).TokenizeAllForParser(),
                diagnostics);
            var module = parser.Parse();
            Assert.False(
                diagnostics.HasErrors,
                string.Join("\n", diagnostics.Select(d => d.Message)));
            return module;
        }
    }

    [Fact]
    public void Converter_SelectedMode_RemovesInactiveCompilerDirectivesWithDistinctLosses()
    {
        const string csharp = """
            #if FEATURE
            public class Selected { }
            #else
            #nullable disable
            #pragma warning disable CS0169
            #line 200 "inactive.cs"
            #error inactive-error
            public class Fallback { private int _unused; }
            #endif
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode =
                PreprocessorConversionMode.SelectActiveBranchLossy,
            DefinedSymbols = ["FEATURE"]
        }).Convert(csharp, "SelectedDirectives.cs");

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.DoesNotContain("#nullable", result.CalorSource);
        Assert.DoesNotContain("#pragma", result.CalorSource);
        Assert.DoesNotContain("#line", result.CalorSource);
        Assert.DoesNotContain("#error", result.CalorSource);
        var removed = result.Losses
            .Where(loss => loss.Kind == ConversionLossKind.DirectiveRemoved)
            .ToList();
        Assert.Contains(removed, loss => loss.Feature == "nullable-directive");
        Assert.Contains(removed, loss => loss.Feature == "pragma");
        Assert.Contains(removed, loss => loss.Feature == "line-directive");
        Assert.Contains(removed, loss => loss.Feature == "error-directive");
    }

    [Fact]
    public void Converter_ActiveAndInactiveError_UseEffectiveSymbols()
    {
        const string csharp = """
            #if FEATURE
            #error feature-error
            #endif
            public class ErrorHost { }
            """;

        var inactive = new CSharpToCalorConverter().Convert(
            csharp,
            "InactiveError.cs");
        Assert.True(inactive.Success, GetErrorMessage(inactive));

        var active = new CSharpToCalorConverter(new ConversionOptions
        {
            DefinedSymbols = ["FEATURE"]
        }).Convert(csharp, "ActiveError.cs");
        Assert.False(active.Success);
        Assert.Contains("#error feature-error", active.CalorSource);
        Assert.Contains(
            active.Issues,
            issue => issue.Feature == "active-error-directive");

        var selectedActive = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode =
                PreprocessorConversionMode.SelectActiveBranchLossy,
            DefinedSymbols = ["FEATURE"]
        }).Convert(csharp, "SelectedActiveError.cs");
        Assert.False(selectedActive.Success);
        Assert.Contains(
            selectedActive.Issues,
            issue => issue.Feature == "active-error-directive");
    }

    [Fact]
    public async Task ProjectSelectedMode_RejectsAmbiguousTfmAndUsesExplicitTfm()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-multitfm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Multi.csproj");
        var sourcePath = Path.Combine(directory, "Conditional.cs");
        var outputPath = Path.ChangeExtension(sourcePath, ".calr");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks Condition="'$(Configuration)' == 'Debug'">net10.0;netstandard2.1</TargetFrameworks>
                <TargetFramework Condition="'$(Configuration)' == 'Release'">net10.0</TargetFramework>
                <DefineConstants Condition="'$(TargetFramework)' == 'net10.0'">$(DefineConstants);NET10</DefineConstants>
                <DefineConstants Condition="'$(TargetFramework)' == 'netstandard2.1'">$(DefineConstants);NETSTANDARD</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #if NET10
            public class Selected { public int Value() => 10; }
            #elif NETSTANDARD
            public class Selected { public int Value() => 21; }
            #else
            public class Selected { public int Value() => 0; }
            #endif
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            ProjectFilePath = projectPath,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var preserveAmbiguous = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                }).ExecuteAsync(plan);
            var preserveAmbiguousFile = Assert.Single(
                preserveAmbiguous.FileResults);
            Assert.Equal(
                FileMigrationStatus.Failed,
                preserveAmbiguousFile.Status);
            Assert.Contains(
                preserveAmbiguousFile.Issues,
                issue => issue.Feature == "preprocessor-project-selection"
                    && issue.Message.Contains(
                        "never unioned",
                        StringComparison.OrdinalIgnoreCase));

            var ambiguous = await new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                MergePartialClasses = false,
                Fidelity = ConversionFidelity.Lossy,
                PreprocessorMode =
                    PreprocessorConversionMode.SelectActiveBranchLossy
            }).ExecuteAsync(plan);
            var ambiguousFile = Assert.Single(ambiguous.FileResults);
            Assert.Equal(FileMigrationStatus.Failed, ambiguousFile.Status);
            Assert.Contains(
                ambiguousFile.Issues,
                issue => issue.Feature == "preprocessor-project-selection"
                    && issue.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));

            var selected = await new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                MergePartialClasses = false,
                Fidelity = ConversionFidelity.Lossy,
                PreprocessorMode =
                    PreprocessorConversionMode.SelectActiveBranchLossy,
                TargetFramework = "net10.0"
            }).ExecuteAsync(plan);
            var selectedFile = Assert.Single(selected.FileResults);
            Assert.True(
                selectedFile.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", selectedFile.Issues));
            Assert.Equal("net10.0", selectedFile.Metadata!.TargetFramework);
            Assert.Contains("NET10", selectedFile.Metadata.DefinedSymbols);
            var calor = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("§R 10", calor);
            Assert.DoesNotContain("§PP", calor);

            var preserveSelected = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false,
                    TargetFramework = "net10.0"
                }).ExecuteAsync(plan);
            var preserveSelectedFile = Assert.Single(
                preserveSelected.FileResults);
            Assert.True(
                preserveSelectedFile.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", preserveSelectedFile.Issues));
            Assert.Equal(
                ["NET10"],
                preserveSelectedFile.Metadata!.DefinedSymbols
                    .Where(symbol => symbol is "NET10" or "NETSTANDARD"));

            var releaseSelected = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false,
                    Fidelity = ConversionFidelity.Lossy,
                    PreprocessorMode =
                        PreprocessorConversionMode.SelectActiveBranchLossy,
                    Configuration = "Release"
                }).ExecuteAsync(plan);
            var releaseFile = Assert.Single(releaseSelected.FileResults);
            Assert.True(
                releaseFile.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", releaseFile.Issues));
            Assert.Equal("net10.0", releaseFile.Metadata!.TargetFramework);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static async Task RestoreAsync(string projectPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--ignore-failed-sources");
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
    }

    [Fact]
    public async Task ProjectValidation_IncludesCallerDefinedSymbols()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-project-define-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Defined.csproj");
        var sourcePath = Path.Combine(directory, "Defined.cs");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            #if FEATURE
            public class Selected { }
            #else
            #error feature-required
            #endif
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            ProjectFilePath = projectPath,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                MergePartialClasses = false,
                DefinedSymbols = ["FEATURE"]
            }).ExecuteAsync(plan);
            var file = Assert.Single(report.FileResults);
            Assert.True(
                file.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", file.Issues));
            Assert.Contains("FEATURE", file.Metadata!.DefinedSymbols);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static async Task RestoreAsync(string projectPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--ignore-failed-sources");
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
    }

    [Fact]
    public async Task ProjectValidation_UsesCallerLanguageVersionOverride()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-project-lang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Language.csproj");
        var sourcePath = Path.Combine(directory, "Language.cs");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>11</LangVersion>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            public class LanguageFeature
            {
                public int[] Values() => [1, 2, 3];
            }
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            ProjectFilePath = projectPath,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                MergePartialClasses = false,
                LanguageVersion = LanguageVersion.CSharp12
            }).ExecuteAsync(plan);
            var file = Assert.Single(report.FileResults);
            Assert.True(
                file.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", file.Issues));
            Assert.Equal("CSharp12", file.Metadata!.LanguageVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static async Task RestoreAsync(string projectPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--ignore-failed-sources");
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
    }

    [Fact]
    public async Task ProjectValidation_PreservesExternAliasReferenceMetadata()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-project-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "External.dll");
        var projectPath = Path.Combine(directory, "Alias.csproj");
        var sourcePath = Path.Combine(directory, "AliasConsumer.cs");
        var referenceCompilation = CSharpCompilation.Create(
            "External",
            [CSharpSyntaxTree.ParseText(
                """
                namespace ExternalLib;
                public sealed class Value { public int Number => 42; }
                """)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using (var stream = File.Create(referencePath))
        {
            var emit = referenceCompilation.Emit(stream);
            Assert.True(
                emit.Success,
                string.Join("\n", emit.Diagnostics));
        }
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <Reference Include="External">
                  <HintPath>External.dll</HintPath>
                  <Aliases>External</Aliases>
                </Reference>
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            extern alias External;
            public class AliasConsumer
            {
                public External::ExternalLib.Value Create() => new();
            }
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            ProjectFilePath = projectPath,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                MergePartialClasses = false
            }).ExecuteAsync(plan);
            var file = Assert.Single(report.FileResults);
            Assert.True(
                file.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", file.Issues));
            var calor = await File.ReadAllTextAsync(
                Path.ChangeExtension(sourcePath, ".calr"));
            Assert.Contains("extern alias External;", calor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static async Task RestoreAsync(string projectPath)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("restore");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--ignore-failed-sources");
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
    }

    [Fact]
    public void Converter_StandaloneAliasReferences_ValidateConflictingTypes()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-api-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "First.dll");
        var secondPath = Path.Combine(directory, "Second.dll");
        try
        {
            EmitAssembly(firstPath, "First", 1);
            EmitAssembly(secondPath, "Second", 2);
            var result = new CSharpToCalorConverter(
                new ConversionOptions
                {
                    References =
                    [
                        new ConversionReference(
                            firstPath,
                            ["FirstAlias"]),
                        new ConversionReference(
                            secondPath,
                            ["SecondAlias"])
                    ]
                }).Convert(
                    """
                    extern alias FirstAlias;
                    extern alias SecondAlias;
                    public static class AliasHarness
                    {
                        public static int Get()
                            => new FirstAlias::Shared.Value().Number
                             + new SecondAlias::Shared.Value().Number;
                    }
                    """,
                    "AliasHarness.cs");
            Assert.True(result.Success, GetErrorMessage(result));
            Assert.Equal(2, result.Metadata.References.Count);
            var generated = CompileCalorToCSharp(result.CalorSource!);
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, "AliasHarness.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    References = result.Metadata.References.Select(reference =>
                        new GeneratedCSharpReference(
                            reference.Path,
                            reference.Aliases))
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static void EmitAssembly(
            string path,
            string assemblyName,
            int value)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(
                    $"namespace Shared; public sealed class Value {{ public int Number => {value}; }}")],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var stream = File.Create(path);
            var emit = compilation.Emit(stream);
            Assert.True(
                emit.Success,
                string.Join("\n", emit.Diagnostics));
        }
    }

    [Fact]
    public void Converter_ScriptKind_PreservesWholeUnitAndFullParseMetadata()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"issue772-script-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "Referenced.dll");
        var loadedPath = Path.Combine(directory, "loaded.csx");
        var scriptPath = Path.Combine(directory, "Script.csx");
        try
        {
            var referenceCompilation = CSharpCompilation.Create(
                "Referenced",
                [CSharpSyntaxTree.ParseText(
                    "public static class Referenced { public static int Value => 41; }")],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = File.Create(referencePath))
            {
                var emit = referenceCompilation.Emit(stream);
                Assert.True(
                    emit.Success,
                    string.Join("\n", emit.Diagnostics));
            }
            File.WriteAllText(
                loadedPath,
                "public static class Loaded { public static int Value => 1; }");
            var script = """
                #r "Referenced.dll"
                #load "loaded.csx"
                #if FEATURE
                System.Console.WriteLine(Loaded.Value + Referenced.Value);
                #endif
                """;
            File.WriteAllText(scriptPath, script);
            var parseOptions = new CSharpParseOptions(
                    LanguageVersion.Preview,
                    DocumentationMode.Diagnose,
                    SourceCodeKind.Script,
                    ["FEATURE"])
                .WithFeatures(
                    new Dictionary<string, string>
                    {
                        ["test_feature"] = "enabled"
                    });
            var result = new CSharpToCalorConverter(new ConversionOptions
            {
                ParseOptions = parseOptions
            }).Convert(script, scriptPath);

            Assert.True(result.Success, GetErrorMessage(result));
            Assert.Equal("Script", result.Metadata.SourceCodeKind);
            Assert.Equal("Diagnose", result.Metadata.DocumentationMode);
            Assert.Equal("Preview", result.Metadata.LanguageVersion);
            Assert.Contains("FEATURE", result.Metadata.DefinedSymbols);
            Assert.Equal("enabled", result.Metadata.Features["test_feature"]);
            Assert.Equal(
                ConversionDirection.CSharpToCalor,
                CSharpToCalorConverter.DetectDirection(scriptPath));
            var generated = CompileCalorToCSharp(result.CalorSource!);
            Assert.StartsWith("#r \"Referenced.dll\"", generated);
            Assert.Contains("#load \"loaded.csx\"", generated);
            Assert.DoesNotContain("// <auto-generated>", generated);
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(generated, scriptPath)],
                new GeneratedCSharpCompilationContext
                {
                    LanguageVersion = LanguageVersion.Preview,
                    DocumentationMode = DocumentationMode.Diagnose,
                    SourceCodeKind = SourceCodeKind.Script,
                    PreprocessorSymbols = ["FEATURE"],
                    Features = result.Metadata.Features
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join("\n", validation.FormattedCompilationErrors));

            var inferred = new CSharpToCalorConverter(
                new ConversionOptions
                {
                    ParseOptions = new CSharpParseOptions(
                            LanguageVersion.Preview,
                            DocumentationMode.Diagnose,
                            SourceCodeKind.Regular,
                            ["FEATURE"])
                        .WithFeatures(result.Metadata.Features)
                }).Convert(script, scriptPath);
            Assert.True(inferred.Success, GetErrorMessage(inferred));
            Assert.Equal("Script", inferred.Metadata.SourceCodeKind);
            Assert.Equal(
                "enabled",
                inferred.Metadata.Features["test_feature"]);

            var missing = new CSharpToCalorConverter(new ConversionOptions
            {
                ParseOptions = parseOptions
            }).Convert(
                "#load \"missing.csx\"\nSystem.Console.WriteLine(1);",
                Path.Combine(directory, "Missing.csx"));
            Assert.False(missing.Success);
            Assert.Contains(
                missing.Issues,
                issue => issue.Message.Contains(
                    "missing.csx",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectDiscovery_IncludesCsxFiles()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Directory.GetParent(root)!.FullName;
        var directory = Path.Combine(
            root,
            "artifacts",
            $"issue772-csx-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "script.csx");
        await File.WriteAllTextAsync(
            scriptPath,
            "System.Console.WriteLine(42);");
        try
        {
            var plan = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                }).CreatePlanAsync(
                directory,
                MigrationDirection.CSharpToCalor);
            Assert.Contains(
                plan.Entries,
                entry => entry.SourcePath == scriptPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ScriptValidation_ValidatesEverySourceIndependently()
    {
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            $"script-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var firstPath = Path.Combine(directory, "First.csx");
            var secondPath = Path.Combine(directory, "Second.csx");
            await File.WriteAllTextAsync(
                firstPath,
                "System.Console.WriteLine(1);");
            await File.WriteAllTextAsync(
                secondPath,
                "#load \"missing.csx\"\nSystem.Console.WriteLine(2);");

            var validation = GeneratedCSharpCompiler.Validate(
                [
                    new GeneratedCSharpSource(
                        await File.ReadAllTextAsync(firstPath),
                        firstPath),
                    new GeneratedCSharpSource(
                        await File.ReadAllTextAsync(secondPath),
                        secondPath)
                ],
                new GeneratedCSharpCompilationContext
                {
                    SourceCodeKind = SourceCodeKind.Script
                });

            Assert.Contains(
                validation.CompilationErrors,
                diagnostic => diagnostic.Id == "CS1504"
                    && diagnostic.Location.SourceTree?.FilePath == secondPath);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_ValidatesCsxIndependentlyWithFullParseOptions()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Directory.GetParent(root)!.FullName;
        var directory = Path.Combine(
            root,
            "artifacts",
            $"issue772-csx-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var loadedPath = Path.Combine(directory, "loaded.csx");
        var scriptPath = Path.Combine(directory, "script.csx");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Scripts.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup><Compile Include="*.csx" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            loadedPath,
            "public static class Loaded { public static int Value => 42; }");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            #load "loaded.csx"
            #if FEATURE
            System.Console.WriteLine(Loaded.Value);
            #endif
            """);
        var restore = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        restore.ArgumentList.Add("restore");
        restore.ArgumentList.Add(Path.Combine(directory, "Scripts.csproj"));
        restore.ArgumentList.Add("--ignore-failed-sources");
        using (var process = System.Diagnostics.Process.Start(restore)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(
                process.ExitCode == 0,
                $"{await output}{Environment.NewLine}{await error}");
        }
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            DefinedSymbols = ["FEATURE"],
            DocumentationMode = DocumentationMode.Diagnose,
            LanguageVersion = LanguageVersion.Preview,
            ParseFeatures = new Dictionary<string, string>
            {
                ["test_feature"] = "enabled"
            }
        });

        try
        {
            var plan = await migrator.CreatePlanAsync(
                Path.Combine(directory, "Scripts.csproj"),
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var script = Assert.Single(
                report.FileResults,
                result => result.SourcePath == scriptPath);
            Assert.True(
                script.Status is FileMigrationStatus.Success
                    or FileMigrationStatus.Partial,
                string.Join("\n", script.Issues));
            Assert.Equal("Script", script.Metadata!.SourceCodeKind);
            Assert.Equal("Diagnose", script.Metadata.DocumentationMode);
            Assert.Equal("enabled", script.Metadata.Features["test_feature"]);
            Assert.Contains("FEATURE", script.Metadata.DefinedSymbols);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_UsesAggregateSiblingValidationOnly()
    {
        var directory = Path.Combine(
            TestArtifactsRoot(),
            $"aggregate-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Aggregate.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "A.cs"),
                "namespace Shared; public static class A { public static int Value() => B.Value(); }");
            await File.WriteAllTextAsync(
                Path.Combine(directory, "B.cs"),
                "namespace Shared; public static class B { public static int Value() => 42; }");
            await RestoreProject(project);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = true,
                    MergePartialClasses = false
                });
            var plan = await migrator.CreatePlanAsync(
                project,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(
                report.FileResults,
                result => Assert.True(
                    result.Status is FileMigrationStatus.Success
                        or FileMigrationStatus.Partial,
                    string.Join("\n", result.Issues)));
            Assert.True(File.Exists(Path.Combine(directory, "A.calr")));
            Assert.True(File.Exists(Path.Combine(directory, "B.calr")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectDiscovery_UsesAuthoritativeCompileItemsIncludingNestedLinks()
    {
        var directory = Path.Combine(
            TestArtifactsRoot(),
            $"evaluated-project-{Guid.NewGuid():N}");
        var sibling = Path.Combine(directory, "Sibling");
        Directory.CreateDirectory(sibling);
        try
        {
            var project = Path.Combine(directory, "Root.csproj");
            await File.WriteAllTextAsync(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Included.cs" />
                    <Compile Include="Removed.cs" />
                    <Compile Remove="Removed.cs" />
                    <Compile Include="Sibling/Linked.cs" Link="Linked.cs" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Included.cs"),
                "public class Included { }");
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Removed.cs"),
                "public class Removed { }");
            await File.WriteAllTextAsync(
                Path.Combine(sibling, "Sibling.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(sibling, "Linked.cs"),
                "public class LinkedType { }");
            await RestoreProject(project);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                });
            var plan = await migrator.CreatePlanAsync(
                project,
                MigrationDirection.CSharpToCalor);

            Assert.Equal(2, plan.Entries.Count);
            Assert.Contains(
                plan.Entries,
                entry => entry.SourcePath
                    == Path.Combine(directory, "Included.cs"));
            Assert.Contains(
                plan.Entries,
                entry => entry.SourcePath
                    == Path.Combine(sibling, "Linked.cs"));
            Assert.DoesNotContain(
                plan.Entries,
                entry => entry.SourcePath
                    == Path.Combine(directory, "Removed.cs"));
            var report = await migrator.ExecuteAsync(plan);
            Assert.All(
                report.FileResults,
                migrated => Assert.True(
                    migrated.Status is FileMigrationStatus.Success
                        or FileMigrationStatus.Partial,
                    string.Join("\n", migrated.Issues)));
            var excludedReport = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                }).ExecuteAsync(new MigrationPlan
                {
                    ProjectPath = directory,
                    ProjectFilePath = project,
                    Direction =
                        MigrationDirection.CSharpToCalor,
                    Entries =
                    [
                        new MigrationPlanEntry
                        {
                            SourcePath = Path.Combine(
                                directory,
                                "Removed.cs"),
                            OutputPath = Path.Combine(
                                directory,
                                "Removed.calr"),
                            Convertibility =
                                FileConvertibility.Full
                        }
                    ]
                });
            Assert.Equal(
                FileMigrationStatus.Skipped,
                Assert.Single(excludedReport.FileResults)
                    .Status);
            var loose = await new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                }).CreatePlanAsync(
                    directory,
                    MigrationDirection.CSharpToCalor);
            Assert.Contains(
                loose.Entries,
                entry => entry.SourcePath
                    == Path.Combine(directory, "Included.cs"));
            Assert.Contains(
                loose.Entries,
                entry => entry.SourcePath
                    == Path.Combine(directory, "Removed.cs"));
            Assert.DoesNotContain(
                loose.Entries,
                entry => entry.SourcePath
                    == Path.Combine(sibling, "Linked.cs"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectMigration_UsesEffectiveSymbolsForErrorDirective(bool active)
    {
        var directory = Path.Combine(
            TestArtifactsRoot(),
            $"error-symbol-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, "Error.csproj");
            await File.WriteAllTextAsync(
                project,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <DefineConstants>{(active ? "ACTIVE" : "INACTIVE")}</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Error.cs"),
                """
                #if ACTIVE
                #error active-error
                #endif
                public class ErrorHost { }
                """);
            await RestoreProject(project);
            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    MergePartialClasses = false
                });
            var plan = await migrator.CreatePlanAsync(
                project,
                MigrationDirection.CSharpToCalor);
            Assert.DoesNotContain(
                plan.Entries,
                entry => entry.Convertibility == FileConvertibility.Skip);

            var report = await migrator.ExecuteAsync(plan);
            var result = Assert.Single(report.FileResults);
            if (active)
            {
                Assert.Equal(FileMigrationStatus.Failed, result.Status);
                Assert.Contains(
                    result.Issues,
                    issue => issue.Message.Contains(
                        "active-error",
                        StringComparison.Ordinal));
            }
            else
            {
                Assert.True(
                    result.Status is FileMigrationStatus.Success
                        or FileMigrationStatus.Partial,
                    string.Join("\n", result.Issues));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_DetectsParallelCsCsxOutputCollisionBeforeWrites()
    {
        var directory = Path.Combine(
            TestArtifactsRoot(),
            $"output-collision-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Foo.cs"),
                "public class Foo { }");
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Foo.csx"),
                "System.Console.WriteLine(1);");
            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = true,
                    MaxParallelism = 4,
                    MergePartialClasses = false
                });
            var plan = await migrator.CreatePlanAsync(
                directory,
                MigrationDirection.CSharpToCalor);
            Assert.Equal(
                2,
                plan.Entries.Count);

            var report = await migrator.ExecuteAsync(plan);

            Assert.Equal(2, report.FileResults.Count);
            Assert.All(
                report.FileResults,
                result =>
                {
                    Assert.Equal(FileMigrationStatus.Failed, result.Status);
                    Assert.Contains(
                        result.Issues,
                        issue => issue.Feature == "migration-output-collision");
                });
            Assert.False(File.Exists(Path.Combine(directory, "Foo.calr")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RestoreProject(string project)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"{await output}{Environment.NewLine}{await error}");
    }

    private static string TestArtifactsRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(
                   root,
                   "Directory.Build.props")))
            root = Directory.GetParent(root)!.FullName;
        return Path.Combine(root, "artifacts");
    }

    [Fact]
    public void Converter_MalformedUnmatchedDirective_FailsExplicitly()
    {
        var result = new CSharpToCalorConverter().Convert(
            "#endif\npublic class Broken { }",
            "BrokenDirective.cs");

        Assert.False(result.Success);
        Assert.Contains(
            result.Issues,
            issue => issue.Message.Contains(
                "CS1028",
                StringComparison.Ordinal)
                || issue.Message.Contains(
                    "Unexpected preprocessor directive",
                    StringComparison.OrdinalIgnoreCase));

        var selected = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode =
                PreprocessorConversionMode.SelectActiveBranchLossy
        }).Convert(
            "#endif\npublic class Broken { }",
            "BrokenSelected.cs");
        Assert.False(selected.Success);
        Assert.Contains(
            selected.Issues,
            issue => issue.Feature
                == "malformed-preprocessor-directive");

        var unterminatedSelected = new CSharpToCalorConverter(
            new ConversionOptions
            {
                Fidelity = ConversionFidelity.Lossy,
                PreprocessorMode =
                    PreprocessorConversionMode.SelectActiveBranchLossy
            }).Convert(
                "#if true\npublic class Unterminated { }",
                "UnterminatedSelected.cs");
        Assert.False(unterminatedSelected.Success);
        Assert.Contains(
            unterminatedSelected.Issues,
            issue => issue.Feature
                == "malformed-preprocessor-directive");
    }

    [Fact]
    public void Converter_CompilationUnitAttributesAndExterns_UseWholeUnitPassthrough()
    {
        const string attributed = """
            [assembly: System.CLSCompliant(true)]
            namespace Attributed { public class C { } }
            """;
        var attributeResult = new CSharpToCalorConverter().Convert(
            attributed,
            "Attributed.cs");
        Assert.True(attributeResult.Success, GetErrorMessage(attributeResult));
        var attributeGenerated = CompileCalorToCSharp(
            attributeResult.CalorSource!);
        Assert.StartsWith("[assembly:", attributeGenerated);
        Assert.DoesNotContain("// <auto-generated>", attributeGenerated);

        const string externSource = """
            extern alias External;
            public class UsesExtern { }
            """;
        var externResult = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false
        }).Convert(externSource, "Extern.cs");
        Assert.True(externResult.Success, GetErrorMessage(externResult));
        var externGenerated = CompileCalorToCSharp(externResult.CalorSource!);
        Assert.StartsWith("extern alias External;", externGenerated);
    }

    [Fact]
    public void Converter_NullableDirective_DoesNotReceiveGeneratedNullableOverride()
    {
        const string csharp = """
            #nullable disable
            public class NullableContext
            {
                public string Value;
            }
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "NullableContext.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.Equal(1, generated.Split("#nullable").Length - 1);
        Assert.Contains("#nullable disable", generated);
        Assert.DoesNotContain("#nullable enable", generated);
    }

    [Fact]
    public void Converter_LineDirective_UsesWholeUnitPassthroughForExactPosition()
    {
        const string csharp = """
            #line 200 "mapped.cs"
            public class Mapped { }
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "Mapped.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        var generated = CompileCalorToCSharp(result.CalorSource!);
        Assert.StartsWith("#line 200 \"mapped.cs\"", generated);
        Assert.DoesNotContain("// <auto-generated>", generated);
    }

    [Fact]
    public void Converter_DirectiveBetweenAttributeAndDeclaration_PreservesDeclarationVerbatim()
    {
        const string csharp = """
            [System.Obsolete]
            #nullable disable
            public class AttributedNullable { }
            """;
        var result = new CSharpToCalorConverter().Convert(
            csharp,
            "AttributedNullable.cs");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("[System.Obsolete]", result.CalorSource);
        Assert.Contains("#nullable disable", result.CalorSource);
        Assert.Contains(
            result.Losses,
            loss => loss.Feature == "conditional-unmodeled-placement"
                || loss.Kind == ConversionLossKind.InteropPreserved);
    }
}
