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
    /// (tests/TestData/EditScripts) found that disagreement.
    ///
    /// <para>The rule is <see cref="GeneratedValidationScope"/>: outputs that
    /// REFERENCE a failed file's module are dropped from the validation set and
    /// everything else is validated, so cold and warm agree without hiding an
    /// unrelated file's genuine Calor1002 (review round 2, C1 — the first fix
    /// skipped validation whenever anything failed, and did hide it).</para>
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

    /// <summary>
    /// Review round 2's Probe 1: a genuine Calor1002 in a file that has NOTHING
    /// to do with the failing one must still be reported in the same run. The
    /// first cascade fix skipped validation whenever any file failed, which hid
    /// exactly this — a real emitter defect, invisible for as long as some other
    /// file in the project was red.
    ///
    /// <para><c>broken.calr</c> fails effect enforcement; <c>interop.calr</c>
    /// compiles but emits C# that Roslyn rejects. They share no symbol, so
    /// <c>interop.calr</c> is not cascade-suppressed and its Calor1002 stands.</para>
    /// </summary>
    [Fact]
    public void Probe1_GenuineCalor1002_InAnUnrelatedFile_IsHiddenByAFailingSibling()
    {
        var workspace = CreateWorkspace();
        try
        {
            File.WriteAllText(Path.Combine(workspace, "broken.calr"), """
                §M{m001:Broken}
                  §F{f001:Boom:pub} () -> i32
                    §E{}
                    §P "x"
                    §R INT:1
                """);
            File.WriteAllText(Path.Combine(workspace, "interop.calr"), """
                §M{m002:Interop}
                  §F{f001:Use:pub} () -> void
                    §E{}
                    §B{x:i32} STR:"not an int"
                """);

            var result = DriveAll(workspace, clearFirst: true, enableTypeChecking: false);

            Assert.Contains(DiagnosticCode.ForbiddenEffect, result.Codes);
            Assert.True(
                result.Codes.Contains(DiagnosticCode.CodeGenCompilationError),
                "the unrelated file's genuine Calor1002 must survive a failing sibling; "
                    + $"reported [{string.Join(", ", result.Codes)}]");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// The publication half of the cascade rule (review round 2, C1): an output
    /// EXCLUDED from validation because it calls a failed file was never
    /// checked, so it must not be written, cached, or reported compiled. Before
    /// this, the driver published and claimed success for a file whose generated
    /// C# does not compile.
    /// </summary>
    [Fact]
    public void CompileAll_CascadeSuppressedOutput_IsNotPublishedOrCached()
    {
        var workspace = CreateWorkspace();
        try
        {
            File.WriteAllText(Path.Combine(workspace, "callee.calr"), """
                §M{m001:Lib}
                  §F{f001:Ping:pub} () -> i32
                    §E{}
                    §P "ping"
                    §R INT:1
                """);
            File.WriteAllText(Path.Combine(workspace, "caller.calr"), """
                §M{m002:App}
                  §F{f001:Main:pub} () -> i32
                    §E{}
                    §R §C{Ping} §/C
                """);

            var result = DriveAll(workspace, clearFirst: true);

            Assert.Equal(new[] { DiagnosticCode.ForbiddenEffect }, result.Codes);
            Assert.True(result.AnyErrors);
            Assert.Empty(result.CompiledFiles);
            Assert.False(File.Exists(Path.Combine(workspace, "caller.g.cs")));
            Assert.Contains("caller.calr", result.FailedFiles);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// The warm-path variant: with the caller already cached and only the
    /// now-failing callee recompiled, the cached caller's output is still the
    /// validation set's business — and still suppressed, so warm and cold agree
    /// on diagnostics AND on what is published.
    /// </summary>
    [Fact]
    public void CompileAll_CascadeSuppression_AgreesBetweenWarmAndColdRuns()
    {
        var workspace = CreateWorkspace();
        try
        {
            var callee = Path.Combine(workspace, "callee.calr");
            File.WriteAllText(callee, """
                §M{m001:Lib}
                  §F{f001:Ping:pub} () -> i32
                    §E{}
                    §R INT:1
                """);
            File.WriteAllText(Path.Combine(workspace, "caller.calr"), """
                §M{m002:App}
                  §F{f001:Main:pub} () -> i32
                    §E{}
                    §R §C{Ping} §/C
                """);

            var clean = DriveAll(workspace, clearFirst: true);
            Assert.Empty(clean.Codes);

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
            Assert.Equal(cold.CompiledFiles, warm.CompiledFiles);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// A file the LEXER rejects (Calor0006). <c>Program.Compile</c> returns
    /// before parsing, so its <c>Ast</c> is null and nothing can be known about
    /// what it owns — the parse-failure branch of
    /// <see cref="GeneratedValidationScope"/>. A file that merely fails to PARSE
    /// still carries an AST, so it does not reach that branch.
    /// </summary>
    private const string UnlexableSource = """
        §M{m001:Broken}
          §ZZQ{bogus}
            §E{}
        """;

    /// <summary>
    /// Review round 3's Probe A — the parse-failure branch, at the DRIVER level.
    /// A file that does not parse yields no module, so nothing in the run can be
    /// validated. The round-2 fix stopped there, which was permissive, not
    /// conservative: nothing was checked, and yet every other file was written,
    /// cached and reported successful, with its genuine Calor1002 gone. Both
    /// halves are now required — validate nothing, claim nothing.
    ///
    /// <para>The helper's unit test below only observes <c>scopeIsComplete ==
    /// false</c>; this observes what the driver does with it.</para>
    /// </summary>
    [Fact]
    public void ProbeA_ParseFailedSibling_StillHidesAGenuineCalor1002_AndPublishesIt()
    {
        var workspace = CreateWorkspace();
        try
        {
            // Lexes badly, so Program.Compile returns a NULL Ast (Calor0006) —
            // the branch where the compiler cannot see what the failed file owns.
            File.WriteAllText(Path.Combine(workspace, "broken.calr"), UnlexableSource);
            File.WriteAllText(Path.Combine(workspace, "interop.calr"), """
                §M{m002:Interop}
                  §F{f001:Use:pub} () -> void
                    §E{}
                    §B{x:i32} STR:"not an int"
                """);

            var result = DriveAll(workspace, clearFirst: true, enableTypeChecking: false);

            Assert.True(result.AnyErrors);
            // Nothing was validated, so nothing may be published or cached and
            // nothing may be reported compiled.
            Assert.Empty(result.CompiledFiles);
            Assert.False(File.Exists(Path.Combine(workspace, "interop.g.cs")));
            Assert.Contains("interop.calr", result.FailedFiles);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// The same branch end-to-end through the CLI: no <c>.g.cs</c> on disk, no
    /// build-state entry, and no success line for a file nothing validated.
    /// </summary>
    [Fact]
    public void Cli_ParseFailedSibling_WritesNoOutputAndNoCacheEntry()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"calor-cli-parsefail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var broken = Path.Combine(directory, "broken.calr");
            var interop = Path.Combine(directory, "interop.calr");
            File.WriteAllText(broken, UnlexableSource);
            File.WriteAllText(interop, """
                §M{m002:Interop}
                  §F{f001:Use:pub} () -> void
                    §E{}
                    §B{x:i32} STR:"not an int"
                """);

            var run = CliTestHarness.RunCli(
                directory,
                "--input", broken,
                "--input", interop,
                "--no-type-check");

            Assert.Equal(1, run.ExitCode);
            Assert.DoesNotContain("Compilation successful", run.StdOut + run.StdErr);
            Assert.False(File.Exists(Path.Combine(directory, "interop.g.cs")));
            Assert.False(File.Exists(Path.Combine(directory, ".calor-build-state.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The scope helper's own unit test: "references a failed module" is one
    /// rule in one place, read by the driver and by the MSBuild task alike.
    /// </summary>
    [Fact]
    public void GeneratedValidationScope_MatchesWholeIdentifiersOnly()
    {
        var module = Program.Compile(
            """
            §M{m001:Lib}
              §F{f001:Map:pub} () -> i32
                §E{}
                §R INT:1
            """,
            "lib.calr",
            new CompilationOptions { UnsafeTranspileOnly = true }).Ast;
        Assert.NotNull(module);

        var owned = GeneratedValidationScope.OwnedIdentifiers([module], out var complete);
        Assert.True(complete);
        Assert.Contains("Map", owned);
        Assert.Contains("Lib", owned);

        Assert.True(GeneratedValidationScope.References("var x = Lib.LibModule.Map();", owned));
        Assert.False(GeneratedValidationScope.References("var x = MapReduce(Libs);", owned));
        Assert.False(GeneratedValidationScope.References("var x = 1;", owned));

        // A file that did not parse yields no module: the scope is incomplete
        // and its caller must validate nothing.
        GeneratedValidationScope.OwnedIdentifiers([module, null], out var partial);
        Assert.False(partial);
    }

    private sealed record DriveOutcome(
        string[] Codes,
        bool AnyErrors,
        int Skipped,
        string[] CompiledFiles,
        string[] FailedFiles);

    private static DriveOutcome DriveAll(
        string workspace,
        bool clearFirst,
        bool enableTypeChecking = true)
    {
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path))
            .ToList();
        var sink = new DiagnosticBag();
        var failed = new List<string>();
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
                file => Path.ChangeExtension(file.FullName, ".g.cs")),
            onFailed: file => failed.Add(Path.GetFileName(file.FullName)));

        return new DriveOutcome(
            [.. sink.Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal)],
            result.AnyErrors,
            result.Skipped.Count,
            [.. result.Compiled
                .Select(item => Path.GetFileName(item.File.FullName))
                .OrderBy(name => name, StringComparer.Ordinal)],
            [.. failed.OrderBy(name => name, StringComparer.Ordinal)]);
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
