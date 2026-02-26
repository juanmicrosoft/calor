using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
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
        var tokens = lexer.TokenizeAll();
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
    §/MT{mt1}
  §/CL{c1}
§/M{m1}
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
        // The body should contain a bind for x
        Assert.Contains("§B{x}", calor);
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
        // Both branches should contain a return
        // Count the returns in the output
        var returnCount = calor.Split("§R ").Length - 1;
        Assert.True(returnCount >= 2, $"Expected at least 2 return statements, got {returnCount}. Output:\n{calor}");
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
        // All three branches should have return statements
        var returnCount = calor.Split("§R ").Length - 1;
        Assert.True(returnCount >= 3, $"Expected at least 3 return statements, got {returnCount}. Output:\n{calor}");
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
    §/MT{mt1}
  §/CL{c1}
§/M{m1}
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
        var calor = ConvertToCalor(csharp);
        Assert.Contains("§PP{NET8_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET8_0_OR_GREATER}", calor);
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
      §/MT{mt1}
    §/PP{DEBUG}
  §/CL{c1}
§/M{m1}
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
    public void Converter_RealisticHumanizerPattern_MemberLevelPP_FullRoundTrip()
    {
        // Realistic pattern from Humanizer-style code: platform-specific implementations
        // with properties, fields, and methods guarded by #if
        var csharp = @"
using System;

public class NumberToWordsExtension
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

    public string Format(int value)
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

        // Member-level PP
        Assert.Contains("§PP{NET6_0_OR_GREATER}", calor);
        Assert.Contains("§/PP{NET6_0_OR_GREATER}", calor);

        // Round-trip: parse Calor back to C#
        var csharpOutput = CompileCalorToCSharp(calor);
        Assert.Contains("#if NET6_0_OR_GREATER", csharpOutput);
        Assert.Contains("#endif", csharpOutput);
    }
}
