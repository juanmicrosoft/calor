using System.Diagnostics;
using System.Security;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Tasks.Tests;

[Xunit.Collection(ProcessStateCollection.Name)]
public sealed class ImplicitSystemContextTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"calor-system-sdk-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OriginalAndGenerated081_ActuallyBuildInTheSelectedSdkContext(bool implicitSystem, bool generated)
    {
        var source = Read081();
        if (generated)
        {
            var converted = new CSharpToCalorConverter().Convert(source, "081.cs");
            var compiled = Compiler.Program.Compile(converted.CalorSource!, "081.calr",
                new Compiler.CompilationOptions { DeferGeneratedOutputValidation = true });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(source, compiled.GeneratedCode);
            source = compiled.GeneratedCode;
        }
        var project = WriteProject(implicitSystem);
        File.WriteAllText(Path.Combine(_directory, generated ? "generated081.cs" : "original081.cs"), source);
        var (exitCode, text) = await Build(project);
        Assert.Equal(implicitSystem, exitCode == 0);
        if (!implicitSystem)
            Assert.Contains("CS0103", text);
        output.WriteLine($"MSBuild 081 {(generated ? "generated" : "original")} / " +
            $"{(implicitSystem ? "System-project" : "standalone")}: exit={exitCode}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SdkTaskAndDownstreamCSharpCompiler_ShareSystemPolicy(bool implicitSystem)
    {
        var project = WriteProject(implicitSystem, calor: true);
        var converted = new CSharpToCalorConverter().Convert(Read081(), "081.cs");
        File.WriteAllText(Path.Combine(_directory, "081.calr"), converted.CalorSource);
        var (exitCode, text) = await Build(project);
        Assert.Equal(implicitSystem, exitCode == 0);
        if (implicitSystem)
        {
            var generated = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "obj"), "081.g.cs", SearchOption.AllDirectories));
            Assert.Equal(Read081(), File.ReadAllText(generated));
            Assert.True(File.Exists(Path.Combine(_directory, "bin/Debug/net10.0/SystemContext.dll")));
        }
        else
            Assert.Contains("CS0103", text);
    }

    [Fact]
    public async Task SdkTask_PreservesAnEvaluatedGlobalAlias()
    {
        var project = WriteProject(true, calor: true,
            additionalItems: """<Using Include="System.Action" Alias="Callback" />""");
        const string source = """
            namespace Host
            {
                using System.Threading.Tasks;
                public class Api { public Callback Value => () => {}; }
            }
            """;
        File.WriteAllText(Path.Combine(_directory, "081.calr"),
            new CSharpToCalorConverter().Convert(source, "081.cs").CalorSource);
        var result = await Build(project);
        Assert.True(result.ExitCode == 0, result.Text);
        var generated = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "obj"), "081.g.cs", SearchOption.AllDirectories));
        Assert.Equal(source, File.ReadAllText(generated));
    }

    [Fact]
    public async Task SdkValidation_RespectsRemovedSdkImportsRatherThanGuessingAPreamble()
    {
        var project = WriteProject(true, calor: true,
            additionalItems: """<Using Remove="System.Threading.Tasks" />""");
        File.WriteAllText(project, File.ReadAllText(project)
            .Replace("<ImplicitUsings>disable</ImplicitUsings>", "<ImplicitUsings>enable</ImplicitUsings>"));
        var source = Read081().Replace("using System.Threading.Tasks;", "using System.Collections.Generic;");
        var conversion = new CSharpToCalorConverter().Convert(source, "081.cs");
        File.WriteAllText(Path.Combine(_directory, "081.calr"), conversion.CalorSource);
        var removed = await Build(project);
        Assert.NotEqual(0, removed.ExitCode);
        Assert.Contains("CS0246", removed.Text);
        File.WriteAllText(project, File.ReadAllText(project)
            .Replace("""<Using Remove="System.Threading.Tasks" />""", ""));
        var restored = await Build(project);
        Assert.Equal(0, restored.ExitCode);
        var generated = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "obj"), "081.g.cs", SearchOption.AllDirectories));
        Assert.Equal(source, File.ReadAllText(generated));
    }

    [Fact]
    public async Task WarmSdkTaskCache_DoesNotReuseTheOtherProfile()
    {
        var project = WriteProject(true, calor: true);
        File.WriteAllText(Path.Combine(_directory, "081.calr"),
            new CSharpToCalorConverter().Convert(Read081(), "081.cs").CalorSource);
        var first = await Build(project);
        Assert.Equal(0, first.ExitCode);
        WriteProject(false, calor: true);
        var second = await Build(project);
        Assert.NotEqual(0, second.ExitCode);
        Assert.Contains("CS0103", second.Text);
    }

    [Fact]
    public async Task EvaluatedContext_PreservesProjectUsingAliasAndSystemOptOut()
    {
        var project = WriteProject(false, additionalItems: """<Using Include="System.Action" Alias="Callback" />""");
        var (exitCode, text) = await Build(project);
        Assert.Equal(0, exitCode);
        var context = await GeneratedCSharpCompiler.ResolveProjectContextAsync(project);
        Assert.False(context.IncludeImplicitSystemUsing);
        var accepted = GeneratedCSharpCompiler.Validate(
            [new("public class Host { public Callback Value => () => {}; }", "host.cs")], context);
        Assert.True(accepted.CompilationSuccess, string.Join("\n", accepted.FormattedCompilationErrors));
        var rejected = GeneratedCSharpCompiler.Validate(
            [new("public class Host { public void Run() => Console.WriteLine(); }", "host.cs")], context);
        Assert.Contains(rejected.CompilationErrors, error => error.Id == "CS0103");
        File.WriteAllText(project, File.ReadAllText(project).Replace("Alias=\"Callback\"", "Alias=\"FreshCallback\""));
        var refreshed = await GeneratedCSharpCompiler.ResolveProjectContextAsync(project);
        var changedAlias = GeneratedCSharpCompiler.Validate(
            [new("public class Host { public FreshCallback Value => () => {}; }", "host.cs")], refreshed);
        Assert.True(changedAlias.CompilationSuccess, string.Join("\n", changedAlias.FormattedCompilationErrors));
    }

    private static string Read081() => File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
        "tests/TestData/CSharpImport/Level5_Advanced/081_AsyncMethod.cs"));

    private string WriteProject(bool implicitSystem, bool calor = false, string additionalItems = "")
    {
        Directory.CreateDirectory(_directory);
        var targets = Path.Combine(CliTestHarness.FindRepoRoot(), "src/Calor.Sdk/Sdk/Sdk.targets");
        var project = Path.Combine(_directory, "SystemContext.csproj");
        File.WriteAllText(project, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <CalorImplicitSystemUsing>{implicitSystem.ToString().ToLowerInvariant()}</CalorImplicitSystemUsing>
                <CalorTasksAssembly>{SecurityElement.Escape(typeof(CompileCalor).Assembly.Location)}</CalorTasksAssembly>
                <CalorSdkImportRuntime>false</CalorSdkImportRuntime>
              </PropertyGroup>
              <ItemGroup>
                {(calor ? """<CalorCompile Include="081.calr" />""" : "")}
                {additionalItems}
              </ItemGroup>
              <Import Project="{SecurityElement.Escape(targets)}" />
            </Project>
            """);
        return project;
    }

    private static async Task<(int ExitCode, string Text)> Build(string project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "build", project, "--nologo", "--verbosity", "quiet", "-nodeReuse:false", "-p:UseSharedCompilation=false" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
