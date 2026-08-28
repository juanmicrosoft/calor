using System.Diagnostics;
using Microsoft.Build.Utilities;
using Xunit;

namespace Calor.Tasks.Tests;

/// <summary>
/// <c>CalorPermissiveEffects</c> — the MSBuild form of the CLI's <c>--permissive-effects</c>
/// (roadmap v0.16 §4.1: the PP-W-rows pre-rows control arm runs the agent's own
/// <c>dotnet build</c> through Calor.Tasks, so the policy must reach
/// <see cref="Calor.Compiler.Effects.UnknownCallPolicy"/> from MSBuild, not only from the CLI).
///
/// <para><b>What the waiver covers, exactly</b> — the tests below pin both halves.
/// <c>Calor0410</c> (uses an effect it does not declare) is demoted to a warning, in the
/// per-file pass and in the cross-module pass alike. <c>Calor0411</c> (unknown external
/// call) and <c>Calor0425</c> ("cannot be decided") are <b>suppressed</b>, not demoted:
/// <c>ReportUnknownCall</c> reports only when the policy is Strict or
/// <c>StrictEffects</c> is on (<c>EffectEnforcementPass.cs:4101</c>), and the
/// cross-module pass returns early under Permissive
/// (<c>CrossModuleEffectEnforcementPass.cs:237</c>). NB <c>Calor0411</c> is a warning
/// under MSBuild even at the strict default — the task exposes no <c>StrictEffects</c>
/// parameter — so there is no demotion of it to describe. What is never waived, by this
/// flag or any other: <c>Calor0424</c> (a row that does not fit) and <c>Calor0420</c>/
/// <c>Calor0421</c> (an override or interface implementation that BROADENS the effects
/// it inherited; narrowing is legal).</para>
/// </summary>
public sealed class PermissiveEffectsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _outputDir;

    // A pure function that prints: `error Calor0410` strict, `warning Calor0410` permissive.
    // The v0.16 W1 harness compiles this same three-line program as its pre-rows canary
    // before any paid run (bench/phase0-agent-native/, W1 branch).
    private const string LaunderingSource = """
        §M{m001:PermissiveCanary}
          §F{f001:Quiet:pub} () -> void
            §E{}
            §P "laundered"

        """;

    // Declares what it does: compiles clean under BOTH policies.
    private const string CleanSource = """
        §M{m001:CleanModule}
          §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (+ a b)

        """;

    // A lambda whose body writes but whose BINDING declares pure: Calor0424
    // (EffectRowLambdaTests.LambdaOmittedRow_InferredRowIsCheckedAtTheBindingSite).
    // "An effect row that does not fit is never waived" — not by this property either.
    private const string RowMismatchSource = """
        §M{m001:M}
          §F{f001:Run:pub} () -> void
            §E{cw}
            §B{f:Func<i32,i32>} §E{} §LAM{lam1:x:i32}
              §P x
              §R x
            §/LAM{lam1}
            §P INT:1

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
        => CreateTask(permissive, enforce: true, outputDirectory: _outputDir, sources: sources);

    private CompileCalor CreateTask(bool? permissive, bool enforce, string outputDirectory, params string[] sources)
    {
        var task = new CompileCalor
        {
            BuildEngine = new TestBuildEngine(),
            SourceFiles = sources.Select(p => (Microsoft.Build.Framework.ITaskItem)new TaskItem(Path.GetFullPath(p))).ToArray(),
            OutputDirectory = outputDirectory,
            ProjectDirectory = _projectDir,
            ImplicitUsings = "enable",
            EnforceEffects = enforce,
            // Load-bearing for the warm-build probes: only under Verbose does the task
            // log "skipping (up-to-date)", which is how a replayed build is told from a
            // recompile.
            Verbose = true
        };
        if (permissive is { } value)
            task.PermissiveEffects = value;
        return task;
    }

    private static TestBuildEngine Engine(CompileCalor task) => (TestBuildEngine)task.BuildEngine;

    // ---------------------------------------------------------------------
    // Defaults and the MSBuild surface
    // ---------------------------------------------------------------------

    /// <summary>
    /// A SHAPE pin, stated as such: the task's default must equal the compiler's.
    /// It is explicitly NOT the discriminator — an "always Strict" mutation keeps this
    /// green (both sides are Strict). <see cref="PropertyTrue_Calor0410_IsAWarning_AndTheBuildSucceeds"/>
    /// and <see cref="MsBuildProperty_ReachesTheTask_EndToEnd"/> are what fail then.
    /// </summary>
    [Fact]
    public void TaskDefault_MatchesCompilationOptionsDefault_ShapeOnly()
    {
        var task = new CompileCalor();
        var compilerOptions = new Calor.Compiler.CompilationOptions();

        Assert.False(task.PermissiveEffects);
        Assert.Equal(Calor.Compiler.Effects.UnknownCallPolicy.Strict, compilerOptions.UnknownCallPolicy);
    }

    /// <summary>
    /// The text half of the MSBuild wiring: the seeded default and the task-parameter
    /// spelling. A regex over the file would pass on a dead invocation, so
    /// <see cref="MsBuildProperty_ReachesTheTask_EndToEnd"/> below runs a real build —
    /// this one only localizes the failure when the text is what broke.
    /// </summary>
    [Fact]
    public void SdkTargets_SeedsFalse_AndPassesThePropertyToTheTask()
    {
        var targets = File.ReadAllText(SdkFile("Sdk.targets"));
        var seed = System.Text.RegularExpressions.Regex.Match(
            targets,
            @"<CalorPermissiveEffects Condition=""'\$\(CalorPermissiveEffects\)' == ''"">(\w+)</CalorPermissiveEffects>");
        Assert.True(seed.Success, "Sdk.targets must seed a CalorPermissiveEffects default");
        Assert.Equal("false", seed.Groups[1].Value);
        Assert.Contains("PermissiveEffects=\"$(CalorPermissiveEffects)\"", targets);
    }

    /// <summary>
    /// P0 — the link the paid control arm rides on: an MSBuild PROPERTY set in a project
    /// file must reach the task and change the verdict. Generates a project that imports
    /// the real <c>Sdk.targets</c> with <c>CalorTasksAssembly</c> pointed at the built
    /// task assembly and runs real <c>dotnet build</c> twice. A dead invocation, a
    /// <c>Condition="false"</c> PropertyGroup or a misspelled task parameter (MSB4064)
    /// fails here and cannot fail in the in-process tests above.
    /// </summary>
    [Fact]
    public void MsBuildProperty_ReachesTheTask_EndToEnd()
    {
        var strictBuild = RunMsBuild(permissive: false, out var strictOut);
        // MSBuild-level causes first: a misspelled task parameter (MSB4064) or a bad
        // property value (MSB4030) also fails the build, and must not be reported as
        // "the diagnostic was missing".
        Assert.DoesNotContain("MSB4064", strictOut);   // unknown task parameter
        Assert.DoesNotContain("MSB4030", strictOut);   // bad property value
        Assert.False(strictBuild, "strict build must fail:\n" + strictOut);
        Assert.Contains("error Calor0410", strictOut);

        var permissiveBuild = RunMsBuild(permissive: true, out var permissiveOut);
        Assert.True(permissiveBuild, "permissive build must succeed:\n" + permissiveOut);
        Assert.Contains("warning Calor0410", permissiveOut);
        Assert.DoesNotContain("error Calor0410", permissiveOut);
    }

    // ---------------------------------------------------------------------
    // What the waiver does, and what it does not
    // ---------------------------------------------------------------------

    [Fact]
    public void PropertyAbsent_Calor0410_IsAnError()
    {
        var task = CreateTask(permissive: null, Write("Canary.calr", LaunderingSource));

        Assert.False(task.Execute(), "strict build must fail on a laundered effect");

        var engine = Engine(task);
        Assert.Contains(engine.Errors, e => e.Contains("Quiet") && e.Contains("cw"));
        Assert.DoesNotContain(engine.Warnings, w => w.Contains("does not declare it"));
    }

    [Fact]
    public void PropertyTrue_Calor0410_IsAWarning_AndTheBuildSucceeds()
    {
        var task = CreateTask(permissive: true, Write("Canary.calr", LaunderingSource));

        var engine = Engine(task);
        Assert.True(task.Execute(), "permissive build must succeed; errors: " + string.Join("; ", engine.Errors));
        Assert.Single(task.GeneratedFiles);
        Assert.Contains(engine.Warnings, w => w.Contains("Quiet") && w.Contains("cw"));
        Assert.DoesNotContain(engine.Errors, e => e.Contains("does not declare it"));
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
        Assert.Contains(Engine(strict).Errors,
            e => e.Contains("HandleRequest") && e.Contains("SaveOrder") && e.Contains("db:w"));

        var permissive = CreateTask(permissive: true, enforce: true,
            outputDirectory: Path.Combine(_tempDir, "out-cross-permissive"), sources: [callee, caller]);
        var engine = Engine(permissive);
        Assert.True(permissive.Execute(), "permissive cross-module build must succeed; errors: " + string.Join("; ", engine.Errors));
        Assert.Contains(engine.Warnings, w => w.Contains("HandleRequest") && w.Contains("SaveOrder") && w.Contains("db:w"));
        Assert.DoesNotContain(engine.Errors, e => e.Contains("db:w"));
    }

    /// <summary>
    /// P5, the NEGATIVE pin. The waiver is bounded: a row that does not fit is
    /// <c>Calor0424</c> and is "never waived, at any site, by any flag"
    /// (<c>Diagnostic.cs</c>). If this ever goes green as a warning, the docs and the
    /// CHANGELOG are wrong and so is the control arm's premise.
    /// </summary>
    [Fact]
    public void PropertyTrue_DoesNotWaive_Calor0424_RowMismatch()
    {
        var task = CreateTask(permissive: true, Write("RowMismatch.calr", RowMismatchSource));

        var engine = Engine(task);
        Assert.False(task.Execute(), "Calor0424 is never waived; the build must still fail");
        Assert.Contains(engine.Errors, e => e.Contains("Extra effect(s): cw") || e.Contains("never waived"));
        Assert.DoesNotContain(engine.Warnings, w => w.Contains("Extra effect(s): cw"));
    }

    /// <summary>P3 — with enforcement off there is nothing to waive; the policy is inert.</summary>
    [Fact]
    public void EnforceEffectsFalse_MakesThePolicyANoOp()
    {
        var source = Write("Canary.calr", LaunderingSource);

        var strictOff = CreateTask(permissive: null, enforce: false,
            outputDirectory: Path.Combine(_tempDir, "out-off-strict"), sources: [source]);
        Assert.True(strictOff.Execute(), "errors: " + string.Join("; ", Engine(strictOff).Errors));

        var permissiveOff = CreateTask(permissive: true, enforce: false,
            outputDirectory: Path.Combine(_tempDir, "out-off-permissive"), sources: [source]);
        Assert.True(permissiveOff.Execute(), "errors: " + string.Join("; ", Engine(permissiveOff).Errors));

        foreach (var engine in new[] { Engine(strictOff), Engine(permissiveOff) })
        {
            Assert.DoesNotContain(engine.Errors, e => e.Contains("does not declare it"));
            Assert.DoesNotContain(engine.Warnings, w => w.Contains("does not declare it"));
        }
    }

    /// <summary>
    /// P1 — the control arm's premise: the policy changes DIAGNOSTICS, never the
    /// generated code. If permissive emitted different C#, arm A would be measuring a
    /// codegen difference and PP-W-rows' leg B would be uninterpretable.
    /// </summary>
    [Fact]
    public void Policy_DoesNotChangeGeneratedCode()
    {
        var source = Write("Clean.calr", CleanSource);

        var strictOut = Path.Combine(_tempDir, "gen-strict");
        var permissiveOut = Path.Combine(_tempDir, "gen-permissive");
        var strict = CreateTask(permissive: null, enforce: true, outputDirectory: strictOut, sources: [source]);
        var permissive = CreateTask(permissive: true, enforce: true, outputDirectory: permissiveOut, sources: [source]);

        Assert.True(strict.Execute(), string.Join("; ", Engine(strict).Errors));
        Assert.True(permissive.Execute(), string.Join("; ", Engine(permissive).Errors));

        var strictFiles = Directory.GetFiles(strictOut, "*.g.cs").OrderBy(p => p, StringComparer.Ordinal).ToList();
        var permissiveFiles = Directory.GetFiles(permissiveOut, "*.g.cs").OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(strictFiles);
        Assert.Equal(strictFiles.Select(Path.GetFileName), permissiveFiles.Select(Path.GetFileName));
        foreach (var (a, b) in strictFiles.Zip(permissiveFiles))
            Assert.Equal(File.ReadAllText(a), File.ReadAllText(b));
    }

    // ---------------------------------------------------------------------
    // The build cache
    // ---------------------------------------------------------------------

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
    public void FlippingPermissiveToStrict_InvalidatesTheWarmCache()
    {
        var source = Write("Canary.calr", LaunderingSource);

        var permissive = CreateTask(permissive: true, source);
        Assert.True(permissive.Execute());

        // Same source, strict policy: the cached (permissive) verdict must not be served.
        var strict = CreateTask(permissive: null, source);
        Assert.False(strict.Execute(), "the warm cache served the permissive verdict to a strict build");
        Assert.Contains(Engine(strict).Errors, e => e.Contains("does not declare it"));
        Assert.DoesNotContain(Engine(strict).Messages, m => m.Contains("skipping (up-to-date)"));
    }

    /// <summary>
    /// P4 — the other direction, which is the one the control arm takes. The source must
    /// SUCCEED under strict: only then is a cache entry written that a policy-blind warm
    /// build could wrongly serve, and only then is the "did not skip" assertion load-bearing.
    /// (With a source that fails under strict, nothing is cached, the second build recompiles
    /// whatever the policy, and the assertion is vacuous — it stays green with the policy
    /// removed from the options fingerprint.) The same-policy leg is the control that proves
    /// this build WOULD have skipped.
    /// </summary>
    [Fact]
    public void FlippingStrictToPermissive_Recompiles_RatherThanServingTheStrictVerdict()
    {
        var source = Write("Clean.calr", CleanSource);   // SUCCEEDS under strict, so a cache entry exists to be wrongly served

        var strict = CreateTask(permissive: null, source);
        Assert.True(strict.Execute(), "errors: " + string.Join("; ", Engine(strict).Errors));

        var warmSame = CreateTask(permissive: null, source);            // control: same policy DOES skip
        Assert.True(warmSame.Execute());
        Assert.Contains(Engine(warmSame).Messages, m => m.Contains("skipping (up-to-date)"));

        var permissive = CreateTask(permissive: true, source);
        Assert.True(permissive.Execute(), "errors: " + string.Join("; ", Engine(permissive).Errors));
        Assert.DoesNotContain(Engine(permissive).Messages, m => m.Contains("skipping (up-to-date)"));
    }

    /// <summary>
    /// P2 — a warm build under the SAME policy must still surface the demoted warning
    /// (the task replays cached diagnostics). Without this the control arm's second and
    /// later builds would look clean and the epoch would under-count launderings.
    /// </summary>
    [Fact]
    public void WarmSamePolicyBuild_ReplaysTheDemotedWarning()
    {
        var source = Write("Canary.calr", LaunderingSource);

        var cold = CreateTask(permissive: true, source);
        Assert.True(cold.Execute());
        Assert.Contains(Engine(cold).Warnings, w => w.Contains("Quiet") && w.Contains("cw"));

        var warm = CreateTask(permissive: true, source);
        Assert.True(warm.Execute());
        Assert.Contains(Engine(warm).Messages, m => m.Contains("skipping (up-to-date)"));
        Assert.Contains(Engine(warm).Warnings, w => w.Contains("Quiet") && w.Contains("cw"));
    }

    // ---------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Writes a project that imports the repository's real Sdk.targets, sets
    /// CalorPermissiveEffects as an ordinary MSBuild property, and builds it with the
    /// dotnet CLI. Returns success; the combined stdout+stderr comes back in
    /// <paramref name="output"/>.
    /// </summary>
    private bool RunMsBuild(bool permissive, out string output)
    {
        var root = RepoPaths.Root;
        var dir = Path.Combine(_tempDir, "e2e-" + (permissive ? "permissive" : "strict"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "canary.calr"), LaunderingSource);
        File.WriteAllText(Path.Combine(dir, "E2E.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <CalorTasksAssembly>{typeof(CompileCalor).Assembly.Location}</CalorTasksAssembly>
                <CalorOutputDirectory>$(MSBuildThisFileDirectory)obj/calor/</CalorOutputDirectory>
                <CalorEnforceEffects>true</CalorEnforceEffects>
                <CalorPermissiveEffects>{(permissive ? "true" : "false")}</CalorPermissiveEffects>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{Path.Combine(root, "src", "Calor.Runtime", "Calor.Runtime.csproj")}" />
              </ItemGroup>
              <ItemGroup>
                <CalorCompile Include="**/*.calr" />
              </ItemGroup>
              <Import Project="{Path.Combine(root, "src", "Calor.Sdk", "Sdk", "Sdk.targets")}" />
            </Project>
            """);

        // Restored once, then built with --no-restore so the build itself stays off the
        // network and off NuGet's lock.
        RunDotnet(dir, ["restore", "E2E.csproj", "--nologo"], out var restoreOut, mustSucceed: true);
        var ok = RunDotnet(dir, ["build", "E2E.csproj", "--nologo", "-v", "q", "--no-restore"],
                           out var buildOut, mustSucceed: false);
        output = restoreOut + buildOut;
        return ok;
    }

    /// <summary>
    /// Runs `dotnet` in <paramref name="dir"/>. Both pipes are drained CONCURRENTLY —
    /// reading stdout to the end first can block forever on a child that fills the stderr
    /// pipe — and WaitForExit's return value is honoured: on a timeout the process tree is
    /// terminated and the test fails with the output, because reading ExitCode on a live
    /// process throws and would hide the real story.
    /// </summary>
    private static bool RunDotnet(string dir, string[] args, out string output, bool mustSucceed)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(milliseconds: 300_000);
        output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            Assert.Fail($"dotnet {string.Join(' ', args)} did not exit within 300 s:\n{output}");
        }

        var succeeded = process.ExitCode == 0;
        if (mustSucceed)
            Assert.True(succeeded, $"dotnet {string.Join(' ', args)} failed:\n{output}");
        return succeeded;
    }

    private static string SdkFile(string name) => RepoPaths.SdkFile(name);
}
