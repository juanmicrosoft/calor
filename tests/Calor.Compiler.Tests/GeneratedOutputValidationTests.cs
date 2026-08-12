using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class GeneratedOutputValidationTests
{
    private const string TypeInvalidSource = """
        §M{m001:Validation}
          §F{f001:Main:pub} () -> void
            §B{x:i32} STR:"not an int"
        """;

    [Fact]
    public void Compile_TypeInvalidGeneratedCSharp_IsAnError()
    {
        var result = Program.Compile(
            TypeInvalidSource,
            "validation.calr",
            new CompilationOptions { EnableTypeChecking = false });

        var diagnostic = Assert.Single(
            result.Diagnostics.Where(item =>
                item.Code == DiagnosticCode.CodeGenCompilationError));
        Assert.True(result.HasErrors);
        Assert.Equal("validation.calr", diagnostic.FilePath);
        Assert.Contains("CS0029", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_UnsafeTranspileOnly_ExplicitlySkipsValidation()
    {
        var result = Program.Compile(
            TypeInvalidSource,
            "validation.calr",
            new CompilationOptions
            {
                UnsafeTranspileOnly = true,
            });

        Assert.False(result.HasErrors);
        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Code == DiagnosticCode.CodeGenCompilationError);
        Assert.Contains("int x = \"not an int\";", result.GeneratedCode);
    }

    [Fact]
    public void GeneratedCompiler_ResolvesExternalProjectReference()
    {
        var referenceTree = CSharpSyntaxTree.ParseText(
            "namespace External.Library; public static class Api { public static int Value => 42; }");
        var referenceCompilation = CSharpCompilation.Create(
            "External.Library",
            [referenceTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(
            Path.GetTempPath(),
            $"calor-reference-{Guid.NewGuid():N}.dll");

        try
        {
            var emit = referenceCompilation.Emit(path);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(
                    "public static class Consumer { public static int Read() => External.Library.Api.Value; }",
                    "consumer.g.cs")],
                [path]);

            Assert.True(
                validation.CompilationSuccess,
                string.Join(Environment.NewLine, validation.CompilationErrors));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GeneratedCompiler_ResolvesCalorRuntime()
    {
        var validation = GeneratedCSharpCompiler.Validate(
            "public static class Consumer { public static Calor.Runtime.ContractKind Kind => Calor.Runtime.ContractKind.Requires; }");

        Assert.True(
            validation.CompilationSuccess,
            string.Join(Environment.NewLine, validation.CompilationErrors));
    }
}
