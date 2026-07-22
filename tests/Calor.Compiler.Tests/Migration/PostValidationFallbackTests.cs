using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Post-conversion §CSHARP fallback (#717): when the emitted Calor for a top-level
/// member does not parse and the converter is in a C#-preserving mode, that member is
/// re-emitted as a §CSHARP interop block carrying its original C#, so the output is
/// always valid Calor rather than silently-broken text.
///
/// <para>Natural inputs that emit unparseable Calor are effectively unreachable — the
/// visitor's own §CSHARP wrapping already handles unsupported features (~65 exotic
/// constructs probed, none broke) — so these tests inject <see
/// cref="CSharpToCalorConverter.ParseValidatorOverride"/> to declare a chosen member's
/// emission "unparseable" and drive the fallback deterministically. The override
/// condemns the marker only when it appears OUTSIDE a §CSHARP block, mirroring a real
/// parser: once the offending member is preserved inside §CSHARP its content is inert,
/// so the converter's post-rewrap re-validation legitimately passes. The final output is
/// also parsed for real to prove the fallback yields valid Calor.</para>
/// </summary>
public class PostValidationFallbackTests
{
    private const string TwoClasses =
        "namespace N { public class Keepme { public int A() => 1; } " +
        "public class Breakme { public int B() => 2; } }";

    // Everything outside §CSHARP{...}§/CSHARP blocks — what a real parser reads as Calor.
    private static string OutsideCSharp(string calor) =>
        Regex.Replace(calor, @"§CSHARP\{.*?\}§/CSHARP", "", RegexOptions.Singleline);

    private static Func<string, bool> CondemnOutsideCSharp(string marker) =>
        calor => !OutsideCSharp(calor).Contains(marker);

    private static bool ParsesForReal(string calor)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(calor, diagnostics).TokenizeAllForParser();
        if (diagnostics.HasErrors) return false;
        _ = new Parser(tokens, diagnostics).Parse();
        return !diagnostics.HasErrors;
    }

    [Fact]
    public void UnparseableMember_IsWrappedInCSharpBlock_UnderPassthrough()
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = true })
        {
            ParseValidatorOverride = CondemnOutsideCSharp("Breakme"),
        };

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        Assert.True(ParsesForReal(result.CalorSource!),
            "fallback output must be valid Calor:\n" + result.CalorSource);

        // The offending member is preserved verbatim inside a §CSHARP interop block.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("class Breakme", result.CalorSource);
        Assert.Equal(1, result.Context!.Stats.InteropBlocksEmitted);

        // The other member still converted to a real Calor class (not wrapped).
        Assert.Contains("Keepme", result.CalorSource);
        Assert.Contains("§CL", result.CalorSource);
    }

    [Fact]
    public void CrossNamespaceSameNameCollision_DoesNotDuplicateTheHealthyType()
    {
        // Two distinct classes both named Foo in different namespaces: N1.Foo is
        // "unparseable", N2.Foo is healthy. The ambiguous kind/name must NOT drag the
        // healthy N2.Foo source into the interop block (which would emit it twice —
        // CS0101 duplicate type). The ambiguous case is refused, so N2.Foo appears once.
        const string src =
            "namespace N1 { public class Foo { public int A() => 1; } } " +
            "namespace N2 { public class Foo { public int B() => 2; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = true })
        {
            // Condemn only the N1 variant (its body returns A()); N2 returns B().
            ParseValidatorOverride = calor => !OutsideCSharp(calor).Contains("A"),
        };

        var result = converter.Convert(src);

        // Refused to rewrap (ambiguous) → no duplication. Under passthrough the still-
        // unparseable output is surfaced as a failure, not silently shipped.
        Assert.False(result.Success);
        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void UnrewrappableOutput_IsSurfaced_NotSilentlyShipped()
    {
        // The original #717 bug must not survive inside its own fix. Top-level statements
        // become a synthetic Main function with no single C# type source, so they cannot
        // be rewrapped. With everything declared unparseable, the output stays invalid —
        // and passthrough must return failure WITH a warning, never Success=true + broken
        // text (which is exactly the pre-#717 behavior).
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = true })
        {
            ParseValidatorOverride = _ => false,
        };

        var result = converter.Convert("System.Console.WriteLine(\"hi\");");

        Assert.False(result.Success);
        Assert.True(result.HasWarnings);
        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
    }

    [Fact]
    public void UnparseableMember_IsNotWrapped_WhenNotPreservingCSharp()
    {
        // Default mode (no passthrough / not Interop): the fallback is gated off, so the
        // output is left as the converter produced it (here real-valid) and no §CSHARP is
        // emitted. Guards that the fallback does not change default-mode behavior.
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = false })
        {
            ParseValidatorOverride = CondemnOutsideCSharp("Breakme"),
        };

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }

    [Fact]
    public void CleanConversion_TriggersNoFallback()
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = true });

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.True(ParsesForReal(result.CalorSource!));
        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }
}
