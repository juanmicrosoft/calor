using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class MethodGroupEscapeTests
{
    [Theory]
    [InlineData("§B{chosen:Func<i32>} (?? fallback Loud)", "")]
    [InlineData("§B{chosen:Func<i32>} (? condition fallback Loud)", "")]
    [InlineData("§B{created:Func<i32>[]} §ARR{Func<i32>} Loud", "alloc")]
    [InlineData("§ASSIGN holder.Saved Loud", "mut")]
    [InlineData("§ASSIGN §IDX items 0 Loud", "mut")]
    [InlineData("§B{chosen:Func<i32>} (?? fallback Helpers.LoudStatic)", "")]
    [InlineData("§ASSIGN holder.Saved Helpers.LoudStatic", "mut")]
    [InlineData("§B{chosen:Func<i32>} (?? fallback holder.LoudInstance)", "")]
    [InlineData("§ASSIGN holder.Saved holder.LoudInstance", "mut")]
    public void UncheckedMethodGroupValues_ChargeTheirEffects(string statement, string otherEffects)
    {
        var source = Source(statement, otherEffects);
        var rejected = Compile(source);
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));

        var accepted = Compile(Source(statement, otherEffects.Length == 0 ? "cw" : otherEffects + ",cw"));
        Assert.False(accepted.HasErrors, string.Join(Environment.NewLine, accepted.Diagnostics));
    }

    [Fact]
    public void DirectBinding_StillCarriesItsRowWithoutInvokingTheMethod()
    {
        var result = Compile(Source("§B{chosen:Func<i32>} §E{cw} Loud", ""));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void LocalShadowInNestedExpression_IsNotMistakenForAMethodGroup()
    {
        var result = Compile(Source("""
            §B{Loud:Func<i32>} §E{} fallback
            §B{chosen:Func<i32>} (?? fallback Loud)
            """.Replace("\n", "\n    ", StringComparison.Ordinal), ""));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodBody_UsesItsOwnMethodGroupBeforeAModuleFunction(bool shadowsModuleFunction)
    {
        var source = $$"""
            §M{MethodScope}
              §F{Loud:pub} () -> i32
                §E{cw}
                §P "loud"
                §R 1
              §CL{Owner:pub}
                §MT{ {{(shadowsModuleFunction ? "Loud" : "Quiet")}}:pub} () -> i32
                  §E{}
                  §R 2
                §MT{Choose:pub} (Func<i32>:fallback) -> void
                  §E{}
                  §B{chosen:Func<i32>} (?? fallback Loud)
            """;
        var result = Compile(source);
        if (shadowsModuleFunction)
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        else
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
    }

    private static CompilationResult Compile(string source) =>
        Program.Compile(source, "method-group.calr", new CompilationOptions
        {
            VerifyContracts = false,
            StatusWriter = TextWriter.Null
        });

    private static string Source(string statement, string effects) => $$"""
        §M{MethodGroups}
          §F{Loud:pub} () -> i32
            §E{cw}
            §P "loud"
            §R 1
          §CL{Holder:pub}
            §FLD{Func<i32>:Saved:pub} §E{}
            §MT{LoudInstance:pub} () -> i32
              §E{cw}
              §P "loud"
              §R 1
          §CL{Helpers:pub}
            §MT{LoudStatic:pub:stat} () -> i32
              §E{cw}
              §P "loud"
              §R 1
          §F{Make:pub} (Func<i32>:fallback, bool:condition, Holder:holder, Func<i32>[]:items) -> void
            §E{ {{effects}} }
            {{statement}}
        """;
}
