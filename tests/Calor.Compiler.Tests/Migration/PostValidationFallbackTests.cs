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
/// emission "unparseable" and drive the fallback deterministically. The final output is
/// then parsed for real to prove the fallback produces genuinely valid Calor.</para>
/// </summary>
public class PostValidationFallbackTests
{
    private const string TwoClasses =
        "namespace N { public class Keepme { public int A() => 1; } " +
        "public class Breakme { public int B() => 2; } }";

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
            // Pretend any Calor that mentions "Breakme" is unparseable.
            ParseValidatorOverride = calor => !calor.Contains("Breakme"),
        };

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);

        // The whole file parses for real — the fallback produced valid Calor.
        Assert.True(ParsesForReal(result.CalorSource!),
            "fallback output must be valid Calor:\n" + result.CalorSource);

        // The offending member is preserved verbatim inside a §CSHARP interop block.
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.Contains("class Breakme", result.CalorSource); // original C# kept
        Assert.Equal(1, result.Context!.Stats.InteropBlocksEmitted);

        // The other member still converted to a real Calor class (not wrapped).
        Assert.Contains("Keepme", result.CalorSource);
        Assert.Contains("§CL", result.CalorSource);
    }

    [Fact]
    public void UnparseableMember_IsNotWrapped_WhenNotPreservingCSharp()
    {
        // Default mode (no passthrough / not Interop): the fallback is gated off, so the
        // output is left as the converter produced it (here real-valid) and no §CSHARP is
        // emitted. Guards that the fallback does not change default-mode behavior.
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = false })
        {
            ParseValidatorOverride = calor => !calor.Contains("Breakme"),
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
        // No override: the real parse check passes, so the fallback path is never entered
        // and both classes convert normally.
        var converter = new CSharpToCalorConverter(new ConversionOptions { PassthroughOnError = true });

        var result = converter.Convert(TwoClasses);

        Assert.True(result.Success);
        Assert.True(ParsesForReal(result.CalorSource!));
        Assert.Equal(0, result.Context!.Stats.InteropBlocksEmitted);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }
}
