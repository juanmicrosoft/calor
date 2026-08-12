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
    public void Compile_DefaultNamedBindingReferencesEscapedIdentifier()
    {
        var result = Program.Compile(
            """
            §M{m001:Defaults}
              §F{f001:Read:pub} () -> i32
                §B{default:i32} INT:42
                §R default
            """,
            "defaults.calr");

        Assert.False(
            result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors));
        Assert.Contains("int @default = 42;", result.GeneratedCode);
        Assert.Contains("return @default;", result.GeneratedCode);
    }

    [Fact]
    public void Compile_UnsafeBlock_UsesConfiguredUnsafeSetting()
    {
        const string source = """
            §M{m001:UnsafeBlock}
              §CL{c001:Worker:pub}
                §MT{m001:Run:pub} () -> void
                  §E{unsafe}
                  §UNSAFE{u1}
                    §B{~x:i32} INT:42
                  §/UNSAFE{u1}
            """;

        var accepted = Program.Compile(source, "unsafe.calr");
        Assert.False(
            accepted.HasErrors,
            string.Join("; ", accepted.Diagnostics.Errors.Select(error => error.Message)));

        var rejected = Program.Compile(
            source,
            "unsafe.calr",
            new CompilationOptions { AllowUnsafeCode = false });
        Assert.Contains(
            rejected.Diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.CodeGenCompilationError &&
                diagnostic.Message.Contains("CS0227", StringComparison.Ordinal));
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

    [Fact]
    public void GeneratedCompiler_RunsProjectSourceGenerators()
    {
        var generatorTree = CSharpSyntaxTree.ParseText(
            """
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Text;
            using System.Linq;
            using System.Text;

            [Generator]
            public sealed class TestGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }

                public void Execute(GeneratorExecutionContext context)
                {
                    if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                            "build_property.TestValue", out var value) ||
                        value != "enabled")
                    {
                        return;
                    }
                    var additional = context.AdditionalFiles.SingleOrDefault();
                    if (additional is null ||
                        !context.AnalyzerConfigOptions.GetOptions(additional).TryGetValue(
                            "build_metadata.AdditionalFiles.Kind", out var kind) ||
                        kind != "api")
                    {
                        return;
                    }
                    context.AddSource(
                        "GeneratedApi.g.cs",
                        SourceText.From("public sealed class GeneratedApi { }", Encoding.UTF8));
                }
            }
            """);
        var generatorReferences = GeneratedCSharpCompiler.References
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location)
            ])
            .DistinctBy(reference =>
                (reference as PortableExecutableReference)?.FilePath,
                StringComparer.OrdinalIgnoreCase);
        var generatorCompilation = CSharpCompilation.Create(
            "TestGenerator",
            [generatorTree],
            generatorReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generatorPath = Path.Combine(
            Path.GetTempPath(),
            $"calor-generator-{Guid.NewGuid():N}.dll");
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"calor-generator-{Guid.NewGuid():N}.editorconfig");
        var additionalPath = Path.Combine(
            Path.GetTempPath(),
            $"calor-generator-{Guid.NewGuid():N}.txt");

        try
        {
            var emit = generatorCompilation.Emit(generatorPath);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            File.WriteAllText(additionalPath, "input");
            File.WriteAllText(
                configPath,
                "is_global = true\n" +
                "build_property.TestValue = enabled\n" +
                $"[{additionalPath.Replace('\\', '/')}]\n" +
                "build_metadata.AdditionalFiles.Kind = api\n");

            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(
                    "public sealed class Consumer { public GeneratedApi Create() => new(); }",
                    "consumer.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    AnalyzerPaths = [generatorPath],
                    AnalyzerConfigPath = configPath,
                    AdditionalFilePaths = [additionalPath]
                });

            Assert.True(
                validation.CompilationSuccess,
                string.Join(Environment.NewLine, validation.CompilationErrors));
        }
        finally
        {
            File.Delete(generatorPath);
            File.Delete(configPath);
            File.Delete(additionalPath);
        }
    }

    [Fact]
    public void GeneratedCompiler_HonorsNullableWarningsAsErrors()
    {
        var validation = GeneratedCSharpCompiler.Validate(
            [new GeneratedCSharpSource(
                "#nullable enable\npublic sealed class Consumer { public string Read() => null; }",
                "consumer.g.cs")],
            new GeneratedCSharpCompilationContext
            {
                NullableContextOptions = NullableContextOptions.Enable,
                TreatWarningsAsErrors = true
            });

        Assert.Contains(
            validation.CompilationErrors,
            diagnostic => diagnostic.Id == "CS8603");
    }

    [Fact]
    public void Cli_ReferenceOption_ValidatesAgainstExternalAssembly()
    {
        var referenceTree = CSharpSyntaxTree.ParseText(
            "namespace External.Library; public static class Api { public static int Value => 42; }");
        var referenceCompilation = CSharpCompilation.Create(
            "External.Library",
            [referenceTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-cli-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var referencePath = Path.Combine(directory, "External.Library.dll");
        var sourcePath = Path.Combine(directory, "consumer.calr");

        try
        {
            var emit = referenceCompilation.Emit(referencePath);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            File.WriteAllText(
                sourcePath,
                """
                §M{m001:Consumer}
                  §CL{c001:Reader:pub}
                    §CSHARP{
                      public int Read() => External.Library.Api.Value;
                    }§/CSHARP
                """);

            var (exitCode, stdout, stderr) = CliTestHarness.RunCli(
                directory,
                "--input", sourcePath,
                "--reference", referencePath);

            Assert.Equal(0, exitCode);
            Assert.Contains("Compilation successful", stdout + stderr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Cli_GeneratedValidationFailureDeletesStaleOutput()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-cli-stale-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "validation.calr");
        var outputPath = Path.ChangeExtension(sourcePath, ".g.cs");

        try
        {
            File.WriteAllText(
                sourcePath,
                """
                §M{m001:Validation}
                  §F{f001:Read:pub} () -> i32
                    §R INT:42
                """);
            var successful = CliTestHarness.RunCli(directory, "--input", sourcePath);
            Assert.Equal(0, successful.ExitCode);
            Assert.True(File.Exists(outputPath));

            File.WriteAllText(sourcePath, TypeInvalidSource);
            var failed = CliTestHarness.RunCli(
                directory, "--input", sourcePath, "--no-type-check");

            Assert.Equal(1, failed.ExitCode);
            Assert.False(File.Exists(outputPath));
            Assert.Contains("Calor1002", failed.StdErr + failed.StdOut);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
