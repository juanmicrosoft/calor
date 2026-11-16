using System.Text;
using System.Text.Json;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class LosslessConversionContractTests
{
    [Fact]
    public async Task NamespaceSplitting_SingleScopedImportUsesOneEvaluatedOutputAndRefreshes()
    {
        var directory = Directory.CreateTempSubdirectory("calor-scoped-import-project-").FullName;
        var project = Path.Combine(directory, "Context.csproj");
        var source = Path.Combine(directory, "Imports.cs");
        const string original = """
            using Choice = Picks.Item;
            namespace Witness
            {
                using Choice = Picks.Item;
                public static class Probe
                {
                    public static Choice Echo(Choice value) { return value; }
                }
            }
            """;
        const string siblings = """
            namespace Picks { public class Item { public const int Value = 7; } }
            namespace Witness.Picks { public class Item { public const int Value = 42; } }
            """;
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, original);
        await File.WriteAllTextAsync(Path.Combine(directory, "Sibling.cs"), siblings);
        try
        {
            await RestoreAsync(project);
            var migrator = new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false });
            var plan = await migrator.CreatePlanAsync(project, MigrationDirection.CSharpToCalor);
            foreach (var run in new[] { 0, 1 })
            {
                var report = await migrator.ExecuteAsync(plan);
                Assert.Equal(2, report.Summary.SourceFiles);
                Assert.Equal(3, report.Summary.TotalFiles);
                Assert.Equal(0, report.Summary.FailedFiles);
                var imported = Assert.Single(report.FileResults.Where(result => result.SourcePath == source));
                Assert.True(imported.OutputWritten, string.Join("\n", imported.Issues));
                Assert.Null(imported.NamespaceIndex);
                Assert.Equal(Path.ChangeExtension(source, ".calr"), imported.OutputPath);
                Assert.True(imported.Losses.Count == 0,
                    string.Join("\n", imported.Issues.Concat<object>(imported.Losses)));
                Assert.Equal("verified", imported.ConversionPaths!.RoundTrip);
                Assert.Contains("namespace using Choice = Picks.Item", File.ReadAllText(imported.OutputPath!));
                Assert.True(File.Exists(imported.OutputPath + ".conversion.json"));
                Assert.Equal(original, File.ReadAllText(source));
                var generated = report.FileResults.Select(result =>
                    new Calor.Compiler.CodeGen.GeneratedCSharpSource(result.GeneratedCSharp!, result.OutputPath!));
                var validation = Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(generated,
                    new Calor.Compiler.CodeGen.GeneratedCSharpCompilationContext());
                Assert.True(validation.CompilationSuccess,
                    string.Join("\n", validation.CompilationErrors.Select(error => error.ToString())));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceSplitting_ProjectOutputsCompileAndRunWithOriginalOwners(bool parallel)
    {
        var directory = Directory.CreateTempSubdirectory("calor-native-split-").FullName;
        const string original = """
            namespace Alpha
            {
                public class Item { public int V() => 11; }
                public static class Probe { public static int Run() => new Item().V(); }
            }
            namespace Beta
            {
                public class Item { public int V() => 22; }
                public class Item<T> { public T V(T value) => value; }
                public static class Probe { public static int Run() => new Item().V(); }
            }
            """;
        var source = Path.Combine(directory, "Owners.cs");
        await File.WriteAllTextAsync(source, original);
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            SplitNamespaces = true, Parallel = parallel, MergePartialClasses = false
        });
        try
        {
            var report = await migrator.ExecuteAsync(SplitPlan(directory, source));
            Assert.Equal(1, report.Summary.SourceFiles);
            Assert.Equal(2, report.Summary.TotalFiles);
            Assert.All(report.FileResults, result =>
            {
                Assert.Equal(FileMigrationStatus.Partial, result.Status);
                Assert.True(result.OutputWritten);
                Assert.Empty(result.Losses);
                Assert.Equal("verified", result.ConversionPaths!.RoundTrip);
                Assert.True(File.Exists(result.OutputPath + ".conversion.json"));
                var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
                var parsed = new Calor.Compiler.Parsing.Parser(
                    new Calor.Compiler.Parsing.Lexer(File.ReadAllText(result.OutputPath!), diagnostics)
                        .TokenizeAllForParser(), diagnostics).Parse();
                Assert.False(diagnostics.HasErrors);
                Assert.Single(parsed.NamespaceScopes);
                var probe = parsed.Classes.Single(type => type.Name == "Probe");
                var run = probe.Methods.Single(method => method.Name == "Run");
                var nodes = new Stack<Calor.Compiler.Ast.AstNode>();
                nodes.Push(run);
                var creations = new List<Calor.Compiler.Ast.NewExpressionNode>();
                while (nodes.TryPop(out var node))
                {
                    if (node is Calor.Compiler.Ast.NewExpressionNode creation)
                        creations.Add(creation);
                    foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
                        nodes.Push(child);
                }
                var graph = Calor.Compiler.Analysis.CallGraphAnalysis.Build(parsed);
                Assert.Same(parsed.Classes.Single(type => type.Name == "Item" && type.TypeParameters.Count == 0),
                    graph.SourceCreationType($"{probe.Name}.{run.Id}", Assert.Single(creations).Span));
                Assert.Equal(File.ReadAllText(result.OutputPath!), new CalorEmitter().Emit(parsed));
            });
            Assert.Equal([0, 1], report.FileResults.Select(result => result.NamespaceIndex));
            var files = report.FileResults.Select(result => new FileInfo(result.OutputPath!)).ToArray();
            Assert.False(File.Exists(Path.ChangeExtension(source, ".calr")));
            var expected = SplitRuntime([original]);
            Assert.Equal(new[] { "Alpha.Item|0|False|False", "Alpha.Probe|0|True|True", "Beta.Item|0|False|False",
                "Beta.Item`1|1|False|False", "Beta.Probe|0|True|True",
                "Alpha.Probe.Run=11", "Beta.Probe.Run=22" }, expected);
            foreach (var typed in new[] { false, true })
            {
                var generated = new List<string>();
                var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
                var result = CompilationDriver.CompileAll(files,
                    _ => new CompilationOptions { EnableTypeChecking = typed, StatusWriter = TextWriter.Null },
                    crossModuleEnforcement: true, crossModulePolicy: Calor.Compiler.Effects.UnknownCallPolicy.Strict,
                    onCompiled: (_, compiled) => generated.Add(compiled.GeneratedCode),
                    diagnosticSink: diagnostics);
                Assert.False(result.AnyErrors, string.Join("\n", diagnostics.Errors));
                Assert.Equal(expected, SplitRuntime(generated));
            }
            var repeated = await migrator.ExecuteAsync(SplitPlan(directory, source));
            Assert.All(repeated.FileResults, result => Assert.Equal(FileMigrationStatus.Partial, result.Status));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("namespace A { class C {} } namespace A { class D {} }")]
    [InlineData("namespace A { class C {} } namespace \\u0041 { class D {} }")]
    [InlineData("namespace A { namespace B { class C {} } } namespace Z { class D {} }")]
    [InlineData("namespace A { using X = System.String; class C {} } namespace B { class D {} }")]
    [InlineData("using X = System.String; namespace A { class C {} } namespace B { class D {} }")]
    [InlineData("#if true\nnamespace A { class C {} }\n#endif\nnamespace B { class D {} }")]
    [InlineData("namespace A { class C { int x = System.Math.Abs(-1); } } namespace B { class D {} }")]
    [InlineData("namespace A { class C : System.Exception {} } namespace B { class D {} }")]
    public void NamespaceSplitting_UnsupportedTopologiesKeepTheExactWholeFile(string original)
    {
        var result = new CSharpToCalorConverter().ConvertProject(original, "Boundary.cs");
        Assert.True(result.Success, string.Join("\n", result.Outputs.SelectMany(output => output.Conversion.Issues)));
        Assert.False(result.IsSplit);
        var output = Assert.Single(result.Outputs).Conversion;
        Assert.True(output.InteropPreservationCount > 0);
        var compiled = Program.Compile(output.CalorSource!, null);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(original, compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("namespace A { class C { Missing Value; } } namespace B { class D {} }")]
    [InlineData("namespace A { class C { int V() => \"invalid\"; } } namespace B { class D {} }")]
    [InlineData("namespace A { class C { } namespace B { class D {} }")]
    public void NamespaceSplitting_InvalidSourceNeverGainsSuccessfulSemanticContext(string original)
    {
        var project = new CSharpToCalorConverter().ConvertProject(original, "Invalid.cs");
        Assert.False(project.Success);
        Assert.False(project.IsSplit);
        Assert.Contains(project.Outputs.SelectMany(output => output.Conversion.Issues),
            issue => issue.Severity == ConversionIssueSeverity.Error);
    }

    [Fact]
    public void NamespaceSplitting_MissingSuppliedReferenceIsNotSilentlyDropped()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Missing.dll");
        var converter = new CSharpToCalorConverter(new()
        {
            References = [new ConversionReference(missing, [])]
        });
        var result = converter.ConvertProject("namespace A { class C {} } namespace B { class D {} }", "Missing.cs");
        Assert.False(result.Success);
        Assert.False(result.IsSplit);
        Assert.Contains(result.Outputs.SelectMany(output => output.Conversion.Issues),
            issue => issue.Severity == ConversionIssueSeverity.Error && issue.Message.Contains(missing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceSplitting_CollisionsAndUnwritableDestinationsCommitNothing(bool destinationDirectory)
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-collision-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        await File.WriteAllTextAsync(source, "namespace A { public class C {} } namespace B { public class D {} }");
        var plan = SplitPlan(directory, source);
        if (destinationDirectory)
            Directory.CreateDirectory(Path.Combine(directory, "Owners.namespace2.calr"));
        else
        {
            var other = Path.Combine(directory, "Other.cs");
            await File.WriteAllTextAsync(other, "public class Other {}");
            plan.Entries.Add(new MigrationPlanEntry
            {
                SourcePath = other, OutputPath = Path.Combine(directory, "Owners.namespace2.calr"),
                Convertibility = FileConvertibility.Full
            });
        }
        try
        {
            var report = await new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false })
                .ExecuteAsync(plan);
            Assert.True(report.Summary.FailedFiles > 0);
            Assert.All(report.FileResults, result => Assert.False(result.OutputWritten));
            Assert.Empty(Directory.GetFiles(directory, "*.calr"));
            Assert.Empty(Directory.GetFiles(directory, "*.conversion.json"));
            Assert.False(Directory.Exists(Path.Combine(directory, ".calor")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task NamespaceSplitting_CancellationAfterConversionCommitsNothing()
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-cancel-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        await File.WriteAllTextAsync(source, "namespace A { public class C {} } namespace B { public class D {} }");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false })
                    .ExecuteAsync(SplitPlan(directory, source), progress: new SplitProgress(_ => cancellation.Cancel()),
                        cancellationToken: cancellation.Token));
            Assert.Empty(Directory.GetFiles(directory, "*.calr"));
            Assert.Empty(Directory.GetFiles(directory, "*.conversion.json"));
            Assert.False(Directory.Exists(Path.Combine(directory, ".calor")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task NamespaceSplitting_EvaluatedProjectIncludesEveryOutputAndKeepsSiblingContext()
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-context-").FullName;
        var project = Path.Combine(directory, "Context.csproj");
        var source = Path.Combine(directory, "Owners.cs");
        const string original = """
            namespace Alpha
            {
                public static class Probe
                {
                    public static int Run() => 11;
                    public static SharedContext.Dependency Echo(SharedContext.Dependency value) => value;
                }
            }
            namespace Beta { public static class Probe { public static int Run() => 22; } }
            """;
        const string sibling = "namespace SharedContext; public sealed class Dependency {}";
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>disable</ImplicitUsings></PropertyGroup>
              <ItemGroup><Compile Remove="Excluded.cs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, original);
        await File.WriteAllTextAsync(Path.Combine(directory, "Sibling.cs"), sibling);
        await File.WriteAllTextAsync(Path.Combine(directory, "Excluded.cs"), "this is deliberately invalid C#");
        try
        {
            Assert.False(new CSharpToCalorConverter().ConvertProject(original, source).Success);
            await RestoreAsync(project);
            var migrator = new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false });
            var plan = await migrator.CreatePlanAsync(project, MigrationDirection.CSharpToCalor);
            Assert.Equal(2, plan.Entries.Count);
            var report = await migrator.ExecuteAsync(plan);
            Assert.Equal(2, report.Summary.SourceFiles);
            Assert.Equal(3, report.Summary.TotalFiles);
            Assert.Equal(0, report.Summary.FailedFiles);
            Assert.All(report.FileResults, result =>
            {
                Assert.True(result.OutputWritten, string.Join("\n", result.Issues));
                Assert.Empty(result.Losses);
                Assert.Equal("verified", result.ConversionPaths!.RoundTrip);
            });
            Assert.Equal(SplitRuntime([original, sibling]),
                SplitRuntime(report.FileResults.Select(result => result.GeneratedCSharp!).ToArray()));
            var generated = report.FileResults.Select(result =>
                new Calor.Compiler.CodeGen.GeneratedCSharpSource(result.GeneratedCSharp!, result.OutputPath!));
            var context = await Calor.Compiler.CodeGen.GeneratedCSharpCompiler.ResolveProjectContextAsync(project);
            var validation = Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(generated,
                context with { AdditionalSources = null });
            Assert.True(validation.CompilationSuccess, string.Join("\n", validation.CompilationErrors));
            foreach (var typed in new[] { false, true })
            {
                var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
                var projectSources = new List<Calor.Compiler.CodeGen.GeneratedCSharpSource>();
                var compiled = CompilationDriver.CompileAll(
                    report.FileResults.Select(result => new FileInfo(result.OutputPath!)).ToArray(),
                    _ => new CompilationOptions { EnableTypeChecking = typed, StatusWriter = TextWriter.Null },
                    crossModuleEnforcement: true, crossModulePolicy: Calor.Compiler.Effects.UnknownCallPolicy.Strict,
                    onCompiled: (file, result) => projectSources.Add(new(result.GeneratedCode, file.FullName)),
                    diagnosticSink: diagnostics);
                Assert.False(compiled.AnyErrors, string.Join("\n", diagnostics.Errors));
                var checkedProject = Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(projectSources,
                    context with { AdditionalSources = null });
                Assert.True(checkedProject.CompilationSuccess, string.Join("\n", checkedProject.CompilationErrors));
                Assert.Equal(SplitRuntime([original, sibling]),
                    SplitRuntime(projectSources.Select(source => source.Text).ToArray()));
            }
            var bytes = report.FileResults.Select(result => File.ReadAllBytes(result.OutputPath!)).ToArray();
            await File.WriteAllTextAsync(report.FileResults.Last().OutputPath!, "edited output");
            var refused = await migrator.ExecuteAsync(plan);
            Assert.Equal(0, refused.FileResults.Count(result => result.OutputWritten));
            Assert.True(refused.Summary.FailedFiles > 0);
            Assert.Equal(bytes[0], File.ReadAllBytes(report.FileResults[0].OutputPath!));
            Assert.Equal("edited output", File.ReadAllText(report.FileResults.Last().OutputPath!));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("int value = System.Math.Abs(-1);")]
    [InlineData("int Value { get; } = System.Math.Abs(-1);")]
    [InlineData("static int value = System.Math.Abs(-1);")]
    [InlineData("static int Value { get; } = System.Math.Abs(-1);")]
    [InlineData("public Item() { System.Console.WriteLine(\"constructor\"); }")]
    public void NamespaceSplitting_ConstructorEffectsCannotBorrowAnEqualNameOrGenericArity(string declaration)
    {
        var original = $$"""
            namespace Alpha
            {
                public class Item { {{declaration}} }
                public class Item<T> {}
                public static class Probe { public static Item Run() => new Item(); }
            }
            namespace Beta { public class Marker {} }
            """;
        var project = new CSharpToCalorConverter().ConvertProject(original, "Constructors.cs");
        Assert.True(project.Success);
        Assert.False(project.IsSplit);
        var preserved = Assert.Single(project.Outputs).Conversion;
        Assert.True(preserved.InteropPreservationCount > 0);
        Assert.Equal(original, Program.Compile(preserved.CalorSource!, null).GeneratedCode);
    }

    // 0.30: the instance-initializer rows make Run's strict effects Unknown (the selected
    // constructor runs initializers without an effect contract), so the converter declares
    // ! unknown on Run and they compile honestly instead of failing. The static initializer
    // and effectful constructor rows have no effect-contract surface and still fail closed.
    [Theory]
    [InlineData("int value = System.Math.Abs(-1);", true)]
    [InlineData("int Value { get; } = System.Math.Abs(-1);", true)]
    [InlineData("static int value = System.Math.Abs(-1);", false)]
    [InlineData("static int Value { get; } = System.Math.Abs(-1);", false)]
    [InlineData("public Item() { System.Console.WriteLine(\"constructor\"); }", false)]
    public void NamespaceSplitting_SelectedConstructorFailsClosedOnRealParsedNativeCalor(string declaration,
        bool declaredUnknown)
    {
        var source = $$"""
            namespace Alpha
            {
                public class Item { {{declaration}} }
                public class Item<T> {}
                public static class Probe { public static Item Run() => new Item(); }
            }
            """;
        var native = new CSharpToCalorConverter(new() { ValidateRoundTripCSharp = false }).Convert(source);
        Assert.True(native.Success);
        Assert.Equal(0, native.InteropPreservationCount);
        foreach (var typed in new[] { false, true })
        {
            var compiled = Program.Compile(native.CalorSource!, null,
                new CompilationOptions { EnableTypeChecking = typed, DeferGeneratedOutputValidation = true });
            if (declaredUnknown)
            {
                Assert.Contains("func Run () → Item\n      ! unknown\n",
                    native.CalorSource!.Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
                continue;
            }
            Assert.True(compiled.HasErrors);
            Assert.Contains(compiled.Diagnostics.Errors,
                error => error.Code is Calor.Compiler.Diagnostics.DiagnosticCode.ForbiddenEffect
                    or Calor.Compiler.Diagnostics.DiagnosticCode.ConstructorEffectContractUnavailable);
        }
    }

    [Fact]
    public void NamespaceSplitting_StaticConstructorCannotBeOmittedWhenNamesHaveDifferentArities()
    {
        const string original = """
            namespace Alpha
            {
                public class Item { static Item() { System.Console.WriteLine("static constructor"); } }
                public class Item<T> {}
                public static class Probe { public static Item Run() => new Item(); }
            }
            namespace Beta { public class Marker {} }
            """;
        var result = new CSharpToCalorConverter().ConvertProject(original, "StaticConstructor.cs");
        Assert.True(result.Success);
        Assert.False(result.IsSplit);
        var preserved = Assert.Single(result.Outputs).Conversion;
        Assert.True(preserved.InteropPreservationCount > 0);
        Assert.Equal(original, Program.Compile(preserved.CalorSource!, null).GeneratedCode);
    }

    [Fact]
    public void NamespaceSplitting_ExactDs15InputRetainsEmptyStaticOwnerAndRuntime42()
    {
        const string relative = "bench/phase0-agent-native/fixtures/d-s1.5/namespace-topology/input.cs";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var original = File.ReadAllText(Path.Combine(directory.FullName, relative));
        var result = new CSharpToCalorConverter().ConvertProject(original, "input.cs");
        Assert.True(result.Success);
        Assert.True(result.IsSplit);
        Assert.Equal(2, result.Outputs.Count);
        var generated = new List<string>();
        foreach (var output in result.Outputs)
        {
            Assert.Empty(output.Conversion.Losses);
            Assert.Equal(0, output.Conversion.InteropPreservationCount);
            var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
            var module = new Calor.Compiler.Parsing.Parser(
                new Calor.Compiler.Parsing.Lexer(output.Conversion.CalorSource!, diagnostics)
                    .TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors);
            Assert.Single(module.NamespaceScopes);
            var reemitted = new CalorEmitter().Emit(module);
            foreach (var source in new[] { output.Conversion.CalorSource!, reemitted })
            foreach (var typed in new[] { false, true })
            {
                var compiled = Program.Compile(source, null,
                    new CompilationOptions { EnableTypeChecking = typed, DeferGeneratedOutputValidation = true });
                Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
            }
            generated.Add(Program.Compile(reemitted, null,
                new CompilationOptions { DeferGeneratedOutputValidation = true }).GeneratedCode);
        }
        var expected = SplitRuntime([original], ["Beta.Value"], "Get");
        Assert.Equal(new[] { "Alpha.Marker|0|True|True", "Beta.Value|0|True|True", "Beta.Value.Get=42" }, expected);
        Assert.Equal(expected, SplitRuntime(generated, ["Beta.Value"], "Get"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceSplitting_ChangedInputOrCombinedOwnerConflictCommitsNothing(bool conflictingOwners)
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-validation-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        await File.WriteAllTextAsync(source, "namespace A { public class C {} } namespace B { public class D {} }");
        var plan = SplitPlan(directory, source);
        if (conflictingOwners)
        {
            var other = Path.Combine(directory, "Other.cs");
            await File.WriteAllTextAsync(other, "namespace A { public class C {} } namespace Z { public class E {} }");
            plan.Entries.Add(new MigrationPlanEntry
            {
                SourcePath = other, OutputPath = Path.ChangeExtension(other, ".calr"),
                Convertibility = FileConvertibility.Full
            });
        }
        try
        {
            var report = await new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false })
                .ExecuteAsync(plan, progress: new SplitProgress(progress =>
                {
                    if (!conflictingOwners && progress.ProcessedFiles == progress.TotalFiles)
                        File.WriteAllText(source, "namespace Changed { public class Retained {} }");
                }));
            Assert.True(report.Summary.FailedFiles > 0);
            Assert.All(report.FileResults, result => Assert.False(result.OutputWritten));
            Assert.Empty(Directory.GetFiles(directory, "*.calr"));
            Assert.Empty(Directory.GetFiles(directory, "*.conversion.json"));
            Assert.False(Directory.Exists(Path.Combine(directory, ".calor")));
            if (!conflictingOwners)
                Assert.Equal("namespace Changed { public class Retained {} }", File.ReadAllText(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("lock")]
    [InlineData("input")]
    [InlineData("ancestor")]
    public async Task NamespaceSplitting_AllArtifactAndOriginalInputCollisionsAreCheckedBeforeWriting(string collision)
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-artifacts-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        var other = Path.Combine(directory, "Other.cs");
        await File.WriteAllTextAsync(source, "namespace A { class C {} } namespace B { class D {} }");
        await File.WriteAllTextAsync(other, "public class Other {}");
        var plan = SplitPlan(directory, source);
        var secondOutput = Path.Combine(directory, "Owners.namespace2.calr");
        plan.Entries.Add(new MigrationPlanEntry
        {
            SourcePath = other, Convertibility = FileConvertibility.Full,
            OutputPath = collision switch
            {
                "receipt" => secondOutput + ".conversion.json",
                "lock" => secondOutput + ".conversion.lock",
                "input" => source,
                _ => Path.Combine(secondOutput, "Nested.calr")
            }
        });
        try
        {
            var report = await new ProjectMigrator(new() { SplitNamespaces = true, Parallel = true })
                .ExecuteAsync(plan);
            Assert.True(report.Summary.FailedFiles > 0);
            Assert.All(report.FileResults, result => Assert.False(result.OutputWritten));
            Assert.Empty(Directory.GetFiles(directory, "*.calr"));
            Assert.Empty(Directory.GetFiles(directory, "*.conversion.json"));
            Assert.False(Directory.Exists(secondOutput));
            Assert.Equal("public class Other {}", File.ReadAllText(other));
            Assert.Equal("namespace A { class C {} } namespace B { class D {} }", File.ReadAllText(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task NamespaceSplitting_RefreshDoesNotLeaveObsoleteNativeOutputsActive()
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-membership-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        var firstOutput = Path.Combine(directory, "Owners.namespace1.calr");
        var secondOutput = Path.Combine(directory, "Owners.namespace2.calr");
        const string two = "namespace A { class C {} } namespace B { class D {} }";
        await File.WriteAllTextAsync(source, two);
        var migrator = new ProjectMigrator(new() { SplitNamespaces = true, Parallel = false });
        try
        {
            var initial = await migrator.ExecuteAsync(SplitPlan(directory, source));
            Assert.Equal(0, initial.Summary.FailedFiles);
            var first = File.ReadAllBytes(firstOutput);
            var second = File.ReadAllBytes(secondOutput);
            await File.WriteAllTextAsync(source, "namespace A { class C {} }");
            var refused = await migrator.ExecuteAsync(SplitPlan(directory, source));
            Assert.True(refused.Summary.FailedFiles > 0);
            Assert.Equal(first, File.ReadAllBytes(firstOutput));
            Assert.Equal(second, File.ReadAllBytes(secondOutput));
            Assert.False(File.Exists(Path.ChangeExtension(source, ".calr")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task NamespaceSplitting_DryRunAndCliUseTheSameOptInOutputSet()
    {
        var directory = Directory.CreateTempSubdirectory("calor-split-cli-").FullName;
        var source = Path.Combine(directory, "Owners.cs");
        await File.WriteAllTextAsync(source, "namespace A { public class C {} } namespace B { public class D {} }");
        try
        {
            var dry = await new ProjectMigrator(new() { SplitNamespaces = true })
                .ExecuteAsync(SplitPlan(directory, source), dryRun: true);
            Assert.Equal(2, dry.FileResults.Count);
            Assert.Equal(0, dry.Summary.FailedFiles);
            Assert.Equal(new[] { "Owners.cs" }, Directory.GetFiles(directory).Select(Path.GetFileName));
            var cli = CliTestHarness.RunCli(directory, "migrate", directory, "--split-namespaces", "--skip-analyze",
                "--skip-verify", "--parallel", "false");
            Assert.True(cli.ExitCode == 0, cli.StdOut + cli.StdErr);
            Assert.Equal(2, Directory.GetFiles(directory, "*.calr").Length);
            Assert.Equal(2, Directory.GetFiles(directory, "*.conversion.json").Length);
            Assert.False(File.Exists(Path.ChangeExtension(source, ".calr")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class SplitProgress(Action<MigrationProgress> action) : IProgress<MigrationProgress>
    {
        public void Report(MigrationProgress value) => action(value);
    }

    private static MigrationPlan SplitPlan(string directory, string source) => new()
    {
        ProjectPath = directory, Direction = MigrationDirection.CSharpToCalor,
        Entries = [new MigrationPlanEntry
        {
            SourcePath = source, OutputPath = Path.ChangeExtension(source, ".calr"),
            Convertibility = FileConvertibility.Full
        }]
    };

    private static string[] SplitRuntime(
        IReadOnlyList<string> sources, IReadOnlyList<string>? owners = null, string method = "Run")
    {
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Split" + Guid.NewGuid().ToString("N"),
            sources.Select(source => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)),
            Calor.Compiler.CodeGen.GeneratedCSharpCompiler.References,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        stream.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type => $"{type.FullName}|{type.GetGenericArguments().Length}|{type.IsAbstract}|{type.IsSealed}")
                .Concat((owners ?? ["Alpha.Probe", "Beta.Probe"]).Select(owner =>
                    $"{owner}.{method}={assembly.GetType(owner)!.GetMethod(method)!.Invoke(null, null)}")).ToArray();
        }
        finally { context.Unload(); }
    }

    [Theory]
    [InlineData("#nullable enable\npublic static class Scalar { public static int Take(string value) => 1; public static int Call(string? value) => Take(value); }")]
    [InlineData("#nullable enable\npublic static class Arrays { public static int Take(string[,] values) => 1; public static int Call(string[,]? values) => Take(values); }")]
    [InlineData("#nullable enable\npublic class Box { public Box(string? value) { } public static Box Create(string value) => new Box(value); }")]
    public void DefaultPreservation_DoesNotRelaxNativeReceivingChecks(string original)
    {
        Assert.Empty(Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(original).CompilationErrors);
        var native = new CSharpToCalorConverter(new() { ValidateRoundTripCSharp = false }).Convert(original);
        Assert.True(native.Success);
        Assert.Equal(0, native.InteropPreservationCount);
        Assert.True(Program.Compile(native.CalorSource!, "native-receiving.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true }).HasErrors);

        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(1, converted.InteropPreservationCount);
        Assert.DoesNotContain(converted.Losses, loss => loss.IsSemanticLoss);
        var compiled = Program.Compile(converted.CalorSource!, "preserved-receiving.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true });
        Assert.False(compiled.HasErrors);
        Assert.Contains(original.Split('\n', 2)[1], compiled.GeneratedCode);
        Assert.Empty(Calor.Compiler.CodeGen.GeneratedCSharpCompiler.Validate(compiled.GeneratedCode!).CompilationErrors);
    }

    [Fact]
    public void DefaultPreservation_DoesNotAcceptInvalidIteratorParameters()
    {
        const string original = """
            using System.Collections.Generic;
            public class IteratorParameters
            {
                public void Outer()
                {
                    IEnumerable<int> Local(ref int value)
                    {
                        yield return value;
                    }
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.False(converted.Success);
        Assert.Contains(converted.Issues, issue => issue.Severity == ConversionIssueSeverity.Error
            && issue.Message.Contains("local function 'Local'", StringComparison.Ordinal)
            && issue.Message.Contains("modifier 'ref'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DefaultCli_WholeFilePreservation_ReproducesUtf8BytesAndRuntime(bool bom, bool eofComment)
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-exact-").FullName;
        const string code = """"
            [assembly: System.Reflection.AssemblyMetadata("oracle", "original")]
            #line 20 "original-location.cs"
            public static class Oracle
            {
                public static string Run() => """
                    exact §CSHARP text
                      raw indentation
                    """ + typeof(Oracle).Assembly.GetCustomAttributes(
                        typeof(System.Reflection.AssemblyMetadataAttribute), false).Length;
            }
            """";
        var source = (bom ? "\uFEFF" : "") + code.Replace("\n", "\r\n")
            + (eofComment ? "// original EOF }§/CSHARP" : "\r\n");
        var input = Path.Combine(directory, "Oracle.cs");
        File.WriteAllBytes(input, Encoding.UTF8.GetBytes(source));
        try
        {
            var converted = CliTestHarness.RunCli(directory, "convert", input);
            Assert.True(converted.ExitCode == 0, converted.StdOut + converted.StdErr);
            var calor = Path.ChangeExtension(input, ".calr");
            var generated = CliTestHarness.RunCli(directory, "convert", calor);
            Assert.True(generated.ExitCode == 0, generated.StdOut + generated.StdErr);
            var csharp = Path.Combine(directory, "Oracle.g.cs");
            Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(csharp));
            Assert.Equal("exact §CSHARP text\r\n  raw indentation1", Run(source));
            Assert.Equal(Run(source), Run(Encoding.UTF8.GetString(File.ReadAllBytes(csharp))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static string Run(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator).Select(path =>
                    Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path));
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Oracle" + Guid.NewGuid().ToString("N"),
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
                references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            return (string)assembly.GetType("Oracle")!.GetMethod("Run")!.Invoke(null, null)!;
        }
    }

    [Fact]
    public void DefaultCli_MalformedMissingAndWriteFailure_DoNotBlockOtherSources()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-controls-").FullName;
        File.WriteAllText(Path.Combine(directory, "Valid.cs"),
            "public class Valid { public int Value() => 7; }");
        File.WriteAllText(Path.Combine(directory, "Malformed.cs"),
            "public class Malformed { public int Value( }");
        File.WriteAllText(Path.Combine(directory, "Unwritable.cs"),
            "public class Unwritable { }");
        File.WriteAllText(Path.Combine(directory, "InvalidType.cs"),
            "public class InvalidType { public int Value() => \"not an int\"; }");
        File.WriteAllText(Path.Combine(directory, "InvalidWithMissingContext.cs"),
            "public class InvalidWithMissingContext { MissingContext field; public int Value() => \"not an int\"; }");
        File.WriteAllBytes(Path.Combine(directory, "Unreadable.cs"), [0xFF, 0xFF, 0xFF]);
        Directory.CreateDirectory(Path.Combine(directory, "Unwritable.calr"));
        try
        {
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Missing.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "migrate", "missing").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Malformed.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "InvalidType.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "InvalidWithMissingContext.cs").ExitCode);
            Assert.Equal(1, CliTestHarness.RunCli(directory, "convert", "Unreadable.cs").ExitCode);
            var reportPath = Path.Combine(directory, "report.json");
            var migration = CliTestHarness.RunCli(directory, "migrate", directory, "--report", reportPath);
            Assert.Equal(1, migration.ExitCode);
            Assert.True(File.Exists(Path.Combine(directory, "Valid.calr")),
                migration.StdOut + migration.StdErr
                    + (File.Exists(reportPath) ? File.ReadAllText(reportPath) : ""));
            Assert.False(File.Exists(Path.Combine(directory, "Malformed.calr")));
            Assert.True(Directory.Exists(Path.Combine(directory, "Unwritable.calr")));
            var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data");
            Assert.Equal(6, report.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(5, report.GetProperty("summary").GetProperty("failedFiles").GetInt32());
            Assert.Contains(report.GetProperty("fileResults").EnumerateArray(), file =>
                file.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("feature").GetString() == "migration-write-failure"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultCli_Csproj_WritesEveryInputAndRetainsNativeAndPreservedOutcomes()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-project-").FullName;
        var project = Path.Combine(directory, "Original.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "Native.cs"),
            "public class Native { public int Value() => 7; }");
        const string original = "[assembly: System.Reflection.AssemblyMetadata(\"oracle\", \"exact\")]\r\npublic record Preserved(int Value);\r\n";
        File.WriteAllText(Path.Combine(directory, "Preserved.cs"), original);
        try
        {
            await RestoreAsync(project);
            var reportPath = Path.Combine(directory, "report.json");
            var migrated = CliTestHarness.RunCli(directory, "migrate", project, "--report", reportPath);
            Assert.True(migrated.ExitCode == 0, migrated.StdOut + migrated.StdErr);
            Assert.Equal(2, Directory.GetFiles(directory, "*.calr").Length);
            var files = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data").GetProperty("fileResults").EnumerateArray().ToList();
            Assert.Contains(files, file =>
                file.GetProperty("conversionPaths").GetProperty("outcome").GetString() == "native");
            Assert.Contains(files, file =>
                file.GetProperty("conversionPaths").GetProperty("outcome").GetString() == "preserved");
            Assert.All(files, file => Assert.Equal("verified",
                file.GetProperty("conversionPaths").GetProperty("roundTrip").GetString()));
            var compiled = Program.Compile(File.ReadAllText(Path.Combine(directory, "Preserved.calr")),
                "Preserved.calr", new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors);
            Assert.Equal(original, compiled.GeneratedCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultCli_Csproj_MissingGeneratedContextWritesExactOriginalAsUnverified()
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-generated-context-").FullName;
        var project = Path.Combine(directory, "Original.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <Target Name="GenerateRequiredType" BeforeTargets="CoreCompile">
                <WriteLinesToFile File="$(IntermediateOutputPath)Generated.cs"
                  Lines="public class GeneratedDependency { }" Overwrite="true" />
                <ItemGroup><Compile Include="$(IntermediateOutputPath)Generated.cs" /></ItemGroup>
              </Target>
            </Project>
            """);
        const string original = "[assembly: System.Reflection.AssemblyMetadata(\"oracle\", \"generated\")]\r\npublic class Consumer { public GeneratedDependency Create() => new GeneratedDependency(); }\r\n";
        var source = Path.Combine(directory, "Consumer.cs");
        File.WriteAllText(source, original);
        try
        {
            await RestoreAsync(project);
            await RunProjectCommandAsync(project, "build");
            var reportPath = Path.Combine(directory, "report.json");
            var migrated = CliTestHarness.RunCli(directory, "migrate", project, "--report", reportPath);
            Assert.True(migrated.ExitCode == 0, migrated.StdOut + migrated.StdErr);
            var report = JsonDocument.Parse(File.ReadAllText(reportPath))
                .RootElement.GetProperty("data");
            Assert.Equal(1, report.GetProperty("summary").GetProperty("totalFiles").GetInt32());
            Assert.Equal(0, report.GetProperty("summary").GetProperty("failedFiles").GetInt32());
            var file = Assert.Single(report.GetProperty("fileResults").EnumerateArray());
            Assert.Equal("unverified", file.GetProperty("conversionPaths").GetProperty("outcome").GetString());
            Assert.Equal("failed", file.GetProperty("conversionPaths").GetProperty("roundTrip").GetString());
            Assert.Contains(file.GetProperty("issues").EnumerateArray(), issue =>
                issue.GetProperty("feature").GetString() == "roundtrip-csharp-validation"
                && issue.GetProperty("message").GetString()!.Contains("CS0246", StringComparison.Ordinal));
            var compiled = Program.Compile(File.ReadAllText(Path.ChangeExtension(source, ".calr")),
                "Consumer.calr", new CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors);
            Assert.Equal(File.ReadAllBytes(source), Encoding.UTF8.GetBytes(compiled.GeneratedCode!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliPolicy_DefaultsToLossless()
    {
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false,
            verbose: false,
            explain: false,
            noFallback: false,
            passthrough: false);

        Assert.Equal(ConversionFidelity.Lossless, options.Fidelity);
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("migrate")]
    public void DefaultCli_HelpAndParser_DoNotExposeDestructiveFlags(string command)
    {
        var directory = Directory.CreateTempSubdirectory("calor-default-help-").FullName;
        try
        {
            var help = CliTestHarness.RunCli(directory, command, "--help");
            Assert.Equal(0, help.ExitCode);
            Assert.DoesNotContain("--lossy", help.StdOut + help.StdErr);
            Assert.DoesNotContain("--select-active-preprocessor-branch-lossy", help.StdOut + help.StdErr);
            foreach (var flag in new[] { "--lossy", "--select-active-preprocessor-branch-lossy" })
            {
                var rejected = CliTestHarness.RunCli(directory, command, directory, flag);
                Assert.NotEqual(0, rejected.ExitCode);
                Assert.Contains(flag, rejected.StdOut + rejected.StdErr);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliPolicy_SelectedBranch_IsExplicitLossyAndCarriesParseMetadata()
    {
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false,
            verbose: false,
            explain: false,
            noFallback: false,
            passthrough: false,
            selectActivePreprocessorBranchLossy: true,
            definedSymbols: ["FEATURE"],
            configuration: "Release",
            targetFramework: "net10.0",
            languageVersion: "preview",
            documentationMode: "diagnose",
            sourceKind: "script",
            features: ["test_feature=enabled"],
            references: ["reference.dll=One,Two"]);

        Assert.Equal(ConversionFidelity.Lossy, options.Fidelity);
        Assert.Equal(
            PreprocessorConversionMode.SelectActiveBranchLossy,
            options.PreprocessorMode);
        Assert.Contains("FEATURE", options.DefinedSymbols);
        Assert.Equal("Release", options.Configuration);
        Assert.Equal("net10.0", options.TargetFramework);
        Assert.Equal(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            options.ParseOptions!.LanguageVersion);
        Assert.Equal(
            Microsoft.CodeAnalysis.DocumentationMode.Diagnose,
            options.ParseOptions.DocumentationMode);
        Assert.Equal(
            Microsoft.CodeAnalysis.SourceCodeKind.Script,
            options.ParseOptions.Kind);
        Assert.Equal(
            "enabled",
            options.ParseOptions.Features
                .ToDictionary(
                    feature => feature.Key,
                    feature => feature.Value)["test_feature"]);
        var reference = Assert.Single(options.References);
        Assert.Equal(["One", "Two"], reference.Aliases);
    }

    [Fact]
    public void UnsupportedDescendant_IsPreservedAtMemberBoundary()
    {
        const string source = """
            public class Example
            {
                public int Get(int k)
                {
                    // 0.25 F3 (#847): capturing, so outside the native slice.
                    int Local() => 42 + k;
                    return Local();
                }
            }
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossless
        }).Convert(source, "Example.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        RawFrameAssertions.AssertFramed(result.CalorSource);
        Assert.Contains("int Local() => 42 + k;", result.CalorSource);
        Assert.Equal(1, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
    }

    [Fact]
    public void LossyMode_PreservesUnsupportedInterfaceWithLocation()
    {
        const string source = """
            public interface IEvents
            {
                event System.EventHandler Changed;
            }
            """;

        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy
        }).Convert(source, "IEvents.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        var loss = Assert.Single(result.Losses, item =>
            item.Kind == ConversionLossKind.InteropPreserved
            && item.Feature == "interface-member");
        Assert.Equal("IEvents.cs", loss.File);
        Assert.True(loss.Line > 0);
        Assert.Equal(0, result.DropCount);
        Assert.Equal(1, result.InteropPreservationCount);
    }

    [Fact]
    public void NativeMarkerTextInsideStringDoesNotCreateFallbackLoss()
    {
        const string source = """
            public class Example
            {
                public string Get() => "§RAW §CS{ §CSHARP{";
            }
            """;

        var result = new CSharpToCalorConverter().Convert(source, "Example.cs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
        Assert.DoesNotContain(
            result.Losses,
            loss => loss.Kind == ConversionLossKind.EmitterFallback);
    }

    [Fact]
    public void GeneratedCalorValidation_IsMandatoryInLossyMode()
    {
        var converter = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy
        })
        {
            ParseValidatorOverride = _ => false
        };

        var result = converter.Convert("public class Example { public int Get() => 1; }");

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Feature == "generated-calor-validation");
    }

    [Fact]
    public async Task AtomicWrite_CancellationLeavesExistingDestinationUnchanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-lossless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "output.calr");
        await File.WriteAllTextAsync(path, "original", Encoding.UTF8);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ConversionFileWriter.WriteAtomicAsync(path, "replacement", cancellationToken: cts.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ProjectMigration_LosslessMixedFilesPreservesUnsupportedMembers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-lossless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var nativePath = Path.Combine(directory, "Native.cs");
        var interopPath = Path.Combine(directory, "Interop.cs");
        await File.WriteAllTextAsync(nativePath, "public class Native { public int Get() => 1; }");
        await File.WriteAllTextAsync(
            interopPath,
            "public class Interop { public int Get(int k) { int Local() => k; return Local(); } }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(nativePath),
                Entry(interopPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(
                report.FileResults,
                result => Assert.Contains(
                    result.Status,
                    new[] { FileMigrationStatus.Success, FileMigrationStatus.Partial }));
            var interop = Assert.Single(report.FileResults, result => result.SourcePath == interopPath);
            Assert.Contains(interop.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
            RawFrameAssertions.AssertFramed(await File.ReadAllTextAsync(Path.ChangeExtension(interopPath, ".calr")));
        }

        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CallerCancellation_PropagatesInsteadOfTimingOut()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            $"calor-project-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Cancelled.cs");
        await File.WriteAllTextAsync(
            sourcePath,
            "public class Cancelled { }");
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries = [Entry(sourcePath)]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => migrator.ExecuteAsync(
                    plan,
                    cancellationToken: cts.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_LosslessValidatesCrossFileReferencesTogether()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-cross-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sharedPath = Path.Combine(directory, "Shared.cs");
        var consumerPath = Path.Combine(directory, "Consumer.cs");
        await File.WriteAllTextAsync(sharedPath, "namespace Demo; public class Shared { }");
        await File.WriteAllTextAsync(
            consumerPath,
            "namespace Demo; public class Consumer { public Shared Create() => new Shared(); }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(sharedPath),
                Entry(consumerPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(report.FileResults, result => Assert.Equal(FileMigrationStatus.Success, result.Status));
            Assert.True(File.Exists(Path.ChangeExtension(sharedPath, ".calr")));
            Assert.True(File.Exists(Path.ChangeExtension(consumerPath, ".calr")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_MissingLooseReferencesPreservesSourceAndReportsFailedValidation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-invalid-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var validPath = Path.Combine(directory, "Valid.cs");
        var invalidPath = Path.Combine(directory, "Invalid.cs");
        await File.WriteAllTextAsync(validPath, "namespace Demo; public class Valid { }");
        await File.WriteAllTextAsync(
            invalidPath,
            "namespace Demo; public class Invalid { public Missing Create() => new Missing(); }");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                Entry(validPath),
                Entry(invalidPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            Fidelity = ConversionFidelity.Lossless
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(report.FileResults, result =>
            {
                Assert.Equal(FileMigrationStatus.Partial, result.Status);
                Assert.Equal("unverified", result.ConversionPaths!.Outcome);
                Assert.Equal("failed", result.ConversionPaths.RoundTrip);
                Assert.Equal(File.ReadAllText(result.SourcePath), result.GeneratedCSharp);
            });
            Assert.Equal(0, report.Summary.SuccessfulFiles);
            Assert.Equal(0, report.Summary.FailedFiles);
            Assert.True(report.Summary.TotalWarnings > 0);
            Assert.True(File.Exists(Path.ChangeExtension(validPath, ".calr")));
            Assert.True(File.Exists(Path.ChangeExtension(invalidPath, ".calr")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".calr"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpValidatesCrossFileReferencesTogether()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-reverse-cross-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var libraryPath = Path.Combine(directory, "Library.calr");
        var consumerPath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            libraryPath,
            """
            module Library Library
              func Square public (int:x) → int
                !
                return x * x
            """);
        await File.WriteAllTextAsync(
            consumerPath,
            """
            module Consumer Consumer
              func UseSquare public (int:x) → int
                !
                return Square(x)
            """);

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                Entry(libraryPath),
                Entry(consumerPath)
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.All(
                report.FileResults,
                result => Assert.Equal(FileMigrationStatus.Success, result.Status));
            Assert.True(File.Exists(Path.ChangeExtension(libraryPath, ".g.cs")));
            Assert.True(File.Exists(Path.ChangeExtension(consumerPath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static MigrationPlanEntry Entry(string sourcePath) => new()
        {
            SourcePath = sourcePath,
            OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
            Convertibility = FileConvertibility.Full,
            FileSizeBytes = new FileInfo(sourcePath).Length
        };
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpUsesExistingProjectSources()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-existing-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Sample.csproj");
        var sourcePath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net10.0</TargetFrameworks>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Existing.cs"),
            "public sealed class Existing { }");
        const string original = """
            namespace Consumer
            {
                public class Factory
                {
                    public Existing Create() => new Existing();
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.True(converted.InteropPreservationCount > 0);
        Assert.NotNull(converted.CalorSource);
        await File.WriteAllTextAsync(sourcePath, converted.CalorSource);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            Assert.Empty(Assert.Single(report.FileResults).Issues
                .Where(issue => issue.Severity == ConversionIssueSeverity.Error));
            Assert.Equal(FileMigrationStatus.Success, Assert.Single(report.FileResults).Status);
            Assert.True(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
            Assert.Contains("public Existing Create() => new Existing();",
                await File.ReadAllTextAsync(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_AmbiguousDirectoryProjectContextFailsExplicitly()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-ambiguous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Consumer.calr");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "First.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Second.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        await File.WriteAllTextAsync(
            sourcePath,
            """
            module Consumer Consumer
              func Read public () → int
                !
                return 42
            """);
        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            var result = Assert.Single(report.FileResults);
            Assert.Equal(FileMigrationStatus.Failed, result.Status);
            Assert.Contains(
                result.Issues,
                issue => issue.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_CalorToCSharpHonorsUnsafeProjectSetting()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"calor-project-unsafe-setting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "Sample.csproj");
        var sourcePath = Path.Combine(directory, "Unsafe.calr");
        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            module Unsafe Unsafe
              class Worker public
                func Run public () → void
                  ! unsafe
                  unsafe
                    var int:x = 42
            """);
        await RestoreAsync(projectPath);
        var plan = new MigrationPlan
        {
            ProjectPath = projectPath,
            Direction = MigrationDirection.CalorToCSharp,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = Path.ChangeExtension(sourcePath, ".g.cs"),
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };

        try
        {
            var report = await new ProjectMigrator(
                new MigrationPlanOptions { Parallel = false }).ExecuteAsync(plan);

            var result = Assert.Single(report.FileResults);
            Assert.Equal(FileMigrationStatus.Failed, result.Status);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("CS0227"));
            Assert.False(File.Exists(Path.ChangeExtension(sourcePath, ".g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigration_TimedOutLossyConversionCannotWriteLater()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"calor-project-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Slow.cs");
        var outputPath = Path.ChangeExtension(sourcePath, ".calr");
        await File.WriteAllTextAsync(sourcePath, "public class Item { }");
        await File.WriteAllTextAsync(outputPath, "original");

        var plan = new MigrationPlan
        {
            ProjectPath = directory,
            Direction = MigrationDirection.CSharpToCalor,
            Entries =
            [
                new MigrationPlanEntry
                {
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Convertibility = FileConvertibility.Full,
                    FileSizeBytes = new FileInfo(sourcePath).Length
                }
            ]
        };
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false,
            Fidelity = ConversionFidelity.Lossy,
            PerFileTimeoutSeconds = 0
        });

        try
        {
            var report = await migrator.ExecuteAsync(plan);

            Assert.Equal(FileMigrationStatus.TimedOut, Assert.Single(report.FileResults).Status);
            Assert.Equal("original", await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }

    private static Task RestoreAsync(string projectPath)
        => RunProjectCommandAsync(projectPath, "restore");

    private static async Task RunProjectCommandAsync(string projectPath, string command)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add(command == "restore" ? "--ignore-failed-sources" : "--no-restore");
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"{await stdout}{Environment.NewLine}{await stderr}");
    }
}
