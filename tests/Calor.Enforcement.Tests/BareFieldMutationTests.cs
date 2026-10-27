using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class BareFieldMutationTests
{
    [Theory]
    [InlineData("§ASSIGN n 5", false, false)]
    [InlineData("§ASSIGN n 5", true, false)]
    [InlineData("§ASSIGN n 5", false, true)]
    [InlineData("§C{Bump} §A{ref} n §/C", false, false)]
    [InlineData("§C{Bump} §A{ref} n §/C", true, false)]
    [InlineData("§C{Bump} §A{ref} n §/C", false, true)]
    [InlineData("§C{Set} §A{out} n §/C", false, false)]
    [InlineData("§C{Set} §A{out} n §/C", true, false)]
    [InlineData("§C{Set} §A{out} n §/C", false, true)]
    public void BareFieldWrites_RequireMutation(string statement, bool isStatic, bool inherited)
    {
        var source = Source(statement, isStatic, inherited);
        var rejected = Compile(source);
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal));

        var accepted = Compile(source.Replace("§E{}", "§E{mut}", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join(Environment.NewLine, accepted.Diagnostics));
    }

    [Theory]
    [InlineData("§ASSIGN n 5")]
    [InlineData("§C{Bump} §A{ref} n §/C")]
    [InlineData("§C{Set} §A{out} n §/C")]
    public void ShadowingLocalOrParameter_DoesNotChargeFieldMutation(string statement)
    {
        foreach (var parameter in new[] { false, true })
        {
            var source = Source(statement, false, false)
                .Replace("() -> void", parameter ? "(i32:n) -> void" : "() -> void",
                    StringComparison.Ordinal);
            if (!parameter)
                source = source.Replace("      §E{}", "      §E{}\n      §B{~n:i32} 0",
                    StringComparison.Ordinal);
            var result = Compile(source);
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        }
    }

    [Fact]
    public void NestedLocalShadow_DoesNotHideAFieldWriteAfterItsScope()
    {
        var source = Source("""
            §IF{i1} true
              §B{~n:i32} 0
              §ASSIGN n 1
            §ASSIGN n 5
            """.Replace("\n", "\n      ", StringComparison.Ordinal), false, false);
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadingAFieldByInArgument_RemainsPure()
    {
        var result = Compile(Source("§C{Read} §A{in} n §/C", false, false));
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static CompilationResult Compile(string source) =>
        Program.Compile(source, "bare-field.calr", new CompilationOptions
        {
            VerifyContracts = false,
            StatusWriter = TextWriter.Null
        });

    private static string Source(string statement, bool isStatic, bool inherited) => $$"""
        §M{m1:FieldEffects}
          §F{f1:Bump:pub} (i32:value:ref) -> void
            §ASSIGN value (+ value 1)
          §F{f2:Set:pub} (i32:value:out) -> void
            §ASSIGN value 5
          §F{f3:Read:pub} (i32:value:in) -> void
            §R
          §CL{c1:Parent:pub}
            §FLD{i32:n:pub{{(isStatic ? ":stat" : "")}}}
          {{(inherited ? "§CL{c2:Child:pub}\n    §EXT{Parent}" : "")}}
            §MT{mt1:Write:pub{{(isStatic ? ":stat" : "")}}} () -> void
              §E{}
              {{statement}}
        """;
}
