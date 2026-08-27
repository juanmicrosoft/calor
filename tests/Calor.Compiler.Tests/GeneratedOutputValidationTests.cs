using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
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

    /// <summary>
    /// The driver's generated-output validation covers this run's clean outputs
    /// plus every cached output, as one Roslyn compilation. When a file FAILED,
    /// its output is absent from that set, and validating the rest reported
    /// cascade Calor1002s ("name does not exist") on every caller of the failed
    /// file — on a cold build only, because a warm build whose sole uncached
    /// file was the failing one skipped validation (nothing pending). ES-08
    /// (tests/TestData/EditScripts) found that disagreement; the rule now is
    /// that validation is skipped whenever any file in the run failed, so cold
    /// and warm agree and the only reported error is the real one.
    /// </summary>
    [Fact]
    public void CompileAll_WhenAFileFails_ReportsNoCascadeCalor1002_ColdOrWarm()
    {
        var workspace = CreateWorkspace();
        try
        {
            var callee = Path.Combine(workspace, "callee.calr");
            var caller = Path.Combine(workspace, "caller.calr");
            File.WriteAllText(callee, """
                §M{m001:Lib}
                  §F{f001:Ping:pub} () -> i32
                    §E{}
                    §R INT:1
                """);
            File.WriteAllText(caller, """
                §M{m002:App}
                  §F{f001:Main:pub} () -> i32
                    §E{}
                    §R §C{Ping} §/C
                """);

            // Warm the cache with a clean state: both files compile, no findings.
            var clean = DriveAll(workspace, clearFirst: true);
            Assert.Empty(clean.Codes);
            Assert.False(clean.AnyErrors);

            // The callee now fails effect enforcement; the caller is untouched.
            File.WriteAllText(callee, """
                §M{m001:Lib}
                  §F{f001:Ping:pub} () -> i32
                    §E{}
                    §P "ping"
                    §R INT:1
                """);

            var warm = DriveAll(workspace, clearFirst: false);
            var cold = DriveAll(workspace, clearFirst: true);

            Assert.Equal(new[] { DiagnosticCode.ForbiddenEffect }, warm.Codes);
            Assert.Equal(new[] { DiagnosticCode.ForbiddenEffect }, cold.Codes);
            Assert.True(warm.AnyErrors);
            Assert.True(cold.AnyErrors);
            Assert.Equal(1, warm.Skipped);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// The two-sided half of the rule above: skipping validation is tied to a
    /// FAILED file, not to the presence of diagnostics or to the driver path.
    /// With every Calor file compiling, a generated output that Roslyn rejects
    /// still surfaces as Calor1002 through the driver.
    /// </summary>
    [Fact]
    public void CompileAll_WhenNoFileFails_StillReportsCalor1002ForInvalidOutput()
    {
        var workspace = CreateWorkspace();
        try
        {
            File.WriteAllText(Path.Combine(workspace, "ok.calr"), """
                §M{m001:Ok}
                  §F{f001:One:pub} () -> i32
                    §E{}
                    §R INT:1
                """);
            File.WriteAllText(Path.Combine(workspace, "bad.calr"), TypeInvalidSource);

            var result = DriveAll(workspace, clearFirst: true, enableTypeChecking: false);

            Assert.Contains(DiagnosticCode.CodeGenCompilationError, result.Codes);
            Assert.True(result.AnyErrors);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "calor-genvalidation-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (string[] Codes, bool AnyErrors, int Skipped) DriveAll(
        string workspace,
        bool clearFirst,
        bool enableTypeChecking = true)
    {
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path))
            .ToList();
        var sink = new DiagnosticBag();
        var result = CompilationDriver.CompileAll(
            sources,
            _ => new CompilationOptions
            {
                EnforceEffects = true,
                EnableTypeChecking = enableTypeChecking,
            },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compileResult) => File.WriteAllText(
                Path.ChangeExtension(file.FullName, ".g.cs"),
                compileResult.GeneratedCode),
            diagnosticSink: sink,
            cache: new CompilationDriver.DriverCacheSettings(
                workspace,
                "effects-on",
                clearFirst,
                file => Path.ChangeExtension(file.FullName, ".g.cs")));

        return (
            sink.Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray(),
            result.AnyErrors,
            result.Skipped.Count);
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
