using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

public class OpaqueInteropOverloadTests
{
    [Fact]
    public void PreprocessorInteropMethod_KeepsSameNameCallOpaque()
    {
        const string source = """
            §M{m001:TestModule}
              §CL{c001:Value:pub}
                §PP{true}
                  §CSHARP{public string Format(string? format, object? provider) => "";}§/CSHARP
                §/PP{true}
                §MT{m001:Format:pub} () -> string
                  §R §C{Format} §A null §A null §/C
            """;

        var result = Program.Compile(
            source,
            "interop-overload.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                DeferGeneratedOutputValidation = true,
            });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.NoMatchingOverload or DiagnosticCode.AmbiguousOverload);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(diagnostic => diagnostic.ToString())));
        Assert.Contains("Format(null, null)", result.GeneratedCode);
        Assert.Contains("Format(string? format, object? provider)", result.GeneratedCode);
        var validation = GeneratedCSharpCompiler.Validate(result.GeneratedCode);
        Assert.True(validation.SyntaxSuccess && validation.CompilationSuccess,
            string.Join("\n", validation.SyntaxErrors.Concat(validation.CompilationErrors)));
    }

    [Theory]
    [InlineData("Format")]
    [InlineData("this.Format")]
    public void ExplicitInterfaceInteropMethod_DoesNotHideNoMatchingOverload(string target)
    {
        var source = $$"""
            §M{m001:TestModule}
              §CL{c001:Value:pub}
                §CSHARP{string IFoo.Format(string value) => value;}§/CSHARP
                §MT{m001:Format:pub} () -> string
                  §R ""
                §MT{m002:Probe:pub} () -> string
                  §R §C{ {{target}} } §A "value" §/C
            """;

        var result = Program.Compile(
            source,
            "explicit-interface-overload.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                DeferGeneratedOutputValidation = true,
            });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }
}
