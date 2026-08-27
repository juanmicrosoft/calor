using Microsoft.Build.Utilities;
using Xunit;

namespace Calor.Tasks.Tests;

/// <summary>
/// <c>CalorPermissiveEffects</c> — the MSBuild form of the CLI's <c>--permissive-effects</c>
/// (roadmap v0.16 §4.1: the PP-W-rows pre-rows control arm runs the agent's own
/// <c>dotnet build</c> through Calor.Tasks, so the policy must reach
/// <see cref="Calor.Compiler.Effects.UnknownCallPolicy"/> from MSBuild, not only from the CLI).
/// Pins: property absent → strict (Calor0410 is an ERROR); true → the same code as a
/// WARNING and the build succeeds; explicitly false → strict; the policy is part of the
/// build-state options fingerprint so a warm cache never serves the other policy's verdict.
/// </summary>
public sealed class PermissiveEffectsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _outputDir;

    // A pure function that prints: `error Calor0410` strict, `warning Calor0410` permissive
    // (the same canary bench/phase0-agent-native/templates/calor-arm/permissive-canary.calr uses).
    private const string LaunderingSource = """
        §M{m001:PermissiveCanary}
          §F{f001:Quiet:pub} () -> void
            §E{}
            §P "laundered"

        """;

    private const string CalleeSource = """
        §M{m001:OrderService}
          §F{f001:SaveOrder:pub}
              §O{void}
              §E{db:w}
        """;

    private const string CallerSource = """
        §M{m002:Handler}
          §F{f001:HandleRequest:pub}
              §O{void}
              §C{SaveOrder}
              §/C
        """;

    public PermissiveEffectsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-permissive-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _projectDir = Path.Combine(_tempDir, "project");
        _outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_projectDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private CompileCalor CreateTask(bool? permissive, params string[] sources)
    {
        var task = new CompileCalor
        {
            BuildEngine = new TestBuildEngine(),
            SourceFiles = sources.Select(p => (Microsoft.Build.Framework.ITaskItem)new TaskItem(Path.GetFullPath(p))).ToArray(),
            OutputDirectory = _outputDir,
            ProjectDirectory = _projectDir,
            ImplicitUsings = "enable"
        };
        if (permissive is { } value)
            task.PermissiveEffects = value;
        return task;
    }

    [Fact]
    public void Default_IsStrict_AndAgreesWithCompilationOptions()
    {
        var task = new CompileCalor();
        var compilerOptions = new Calor.Compiler.CompilationOptions();

        Assert.False(task.PermissiveEffects);
        Assert.Equal(Calor.Compiler.Effects.UnknownCallPolicy.Strict, compilerOptions.UnknownCallPolicy);
    }

    [Fact]
    public void SdkTargets_SeedsFalse_AndPassesThePropertyToTheTask()
    {
        var targets = File.ReadAllText(FindSdkFile("Sdk.targets"));
        var seed = System.Text.RegularExpressions.Regex.Match(
            targets,
            @"<CalorPermissiveEffects Condition=""'\$\(CalorPermissiveEffects\)' == ''"">(\w+)</CalorPermissiveEffects>");
        Assert.True(seed.Success, "Sdk.targets must seed a CalorPermissiveEffects default");
        Assert.Equal("false", seed.Groups[1].Value);
        Assert.Contains("PermissiveEffects=\"$(CalorPermissiveEffects)\"", targets);
    }

    [Fact]
    public void PropertyAbsent_Calor0410_IsAnError()
    {
        var task = CreateTask(permissive: null, Write("Canary.calr", LaunderingSource));

        Assert.False(task.Execute(), "strict build must fail on a laundered effect");

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Contains(engine.Errors, e => e.Contains("Quiet") && e.Contains("cw"));
        Assert.DoesNotContain(engine.Warnings, w => w.Contains("does not declare it"));
    }

    [Fact]
    public void PropertyTrue_Calor0410_IsAWarning_AndTheBuildSucceeds()
    {
        var task = CreateTask(permissive: true, Write("Canary.calr", LaunderingSource));

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.True(task.Execute(), "permissive build must succeed; errors: " + string.Join("; ", engine.Errors));
        Assert.Single(task.GeneratedFiles);
        Assert.Contains(engine.Warnings, w => w.Contains("Quiet") && w.Contains("cw"));
        Assert.DoesNotContain(engine.Errors, e => e.Contains("does not declare it"));
    }

    [Fact]
    public void PropertyExplicitlyFalse_IsStrict()
    {
        var task = CreateTask(permissive: false, Write("Canary.calr", LaunderingSource));

        Assert.False(task.Execute());

        var engine = (TestBuildEngine)task.BuildEngine;
        Assert.Contains(engine.Errors, e => e.Contains("does not declare it"));
        Assert.DoesNotContain(engine.Warnings, w => w.Contains("does not declare it"));
    }

    [Fact]
    public void PropertyTrue_CrossModuleViolation_IsAWarning_LikeTheCli()
    {
        // The CLI's --permissive-effects also demotes the cross-module pass
        // (Program.cs `crossModulePolicy`); the MSBuild form must match it.
        var callee = Write("Callee.calr", CalleeSource);
        var caller = Write("Caller.calr", CallerSource);

        var strict = CreateTask(permissive: null, callee, caller);
        Assert.False(strict.Execute());
        Assert.Contains(((TestBuildEngine)strict.BuildEngine).Errors,
            e => e.Contains("HandleRequest") && e.Contains("SaveOrder") && e.Contains("db:w"));

        var permissive = CreateTask(permissive: true, callee, caller);
        var engine = (TestBuildEngine)permissive.BuildEngine;
        Assert.True(permissive.Execute(), "permissive cross-module build must succeed; errors: " + string.Join("; ", engine.Errors));
        Assert.Contains(engine.Warnings, w => w.Contains("HandleRequest") && w.Contains("SaveOrder") && w.Contains("db:w"));
        Assert.DoesNotContain(engine.Errors, e => e.Contains("does not declare it"));
    }

    [Fact]
    public void Policy_IsPartOfTheBuildStateOptionsFingerprint()
    {
        // The build state records optionsHash (not the options themselves); the policy
        // must move it so (a) a warm cache never serves the other policy's verdict and
        // (b) the harness can attest, per arm, that a different policy built the code.
        var strict = new CompileCalor { ProjectDirectory = _projectDir }.ComputeCacheInputs();
        var permissive = new CompileCalor { ProjectDirectory = _projectDir, PermissiveEffects = true }.ComputeCacheInputs();

        Assert.Contains("permissiveEffects", strict.Serialize());
        Assert.NotEqual(strict.Serialize(), permissive.Serialize());
        Assert.NotEqual(
            BuildStateCache.ComputeOptionsHash(strict.Serialize()),
            BuildStateCache.ComputeOptionsHash(permissive.Serialize()));
    }

    [Fact]
    public void FlippingThePolicy_InvalidatesTheWarmCache()
    {
        var source = Write("Canary.calr", LaunderingSource);

        var permissive = CreateTask(permissive: true, source);
        Assert.True(permissive.Execute());

        // Same source, strict policy: the cached (permissive) verdict must not be served.
        var strict = CreateTask(permissive: null, source);
        Assert.False(strict.Execute(), "the warm cache served the permissive verdict to a strict build");
        Assert.Contains(((TestBuildEngine)strict.BuildEngine).Errors, e => e.Contains("does not declare it"));
    }

    private static string FindSdkFile(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Calor.Sdk", "Sdk", name);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("src/Calor.Sdk/Sdk/" + name + " not found above " + AppContext.BaseDirectory);
    }
}
