using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class MethodGroupEscapeTests
{
    [Theory]
    [InlineData("let Func<int>:chosen = fallback ?? Loud", "")]
    [InlineData("let Func<int>:chosen = condition ? fallback : Loud", "")]
    [InlineData("let Func<int>[]:created = new Func<int>[] { Loud }", "alloc")]
    [InlineData("set holder.Saved Loud", "mut")]
    [InlineData("set items[0] Loud", "mut")]
    [InlineData("let Func<int>:chosen = fallback ?? Helpers.LoudStatic", "")]
    [InlineData("set holder.Saved Helpers.LoudStatic", "mut")]
    [InlineData("let Func<int>:chosen = fallback ?? holder.LoudInstance", "")]
    [InlineData("set holder.Saved holder.LoudInstance", "mut")]
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
        var result = Compile(Source("let Func<int>:chosen ! cw = Loud", ""));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void LocalShadowInNestedExpression_IsNotMistakenForAMethodGroup()
    {
        var result = Compile(Source("""
            let Func<int>:Loud ! = fallback
            let Func<int>:chosen = fallback ?? Loud
            """.Replace("\n", "\n    ", StringComparison.Ordinal), ""));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodBody_UsesItsOwnMethodGroupBeforeAModuleFunction(bool shadowsModuleFunction)
    {
        var source = $$"""
            module MethodScope
              func Loud () → int
                ! cw
                Console.WriteLine("loud")
                return 1
              class Owner
                func {{(shadowsModuleFunction ? "Loud" : "Quiet")}} () → int
                  !
                  return 2
                func Choose (Func<int>:fallback) → void
                  !
                  let Func<int>:chosen = fallback ?? Loud
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
        module MethodGroups
          func Loud () → int
            ! cw
            Console.WriteLine("loud")
            return 1
          class Holder
            field Func<int>:Saved !
            func LoudInstance () → int
              ! cw
              Console.WriteLine("loud")
              return 1
          class Helpers
            func LoudStatic static () → int
              ! cw
              Console.WriteLine("loud")
              return 1
          func Make (Func<int>:fallback, bool:condition, Holder:holder, Func<int>[]:items) → void
            ! {{effects}}
            {{statement}}
        """;
}
