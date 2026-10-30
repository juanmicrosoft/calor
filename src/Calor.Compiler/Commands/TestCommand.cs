using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Ids;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3.Cache;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command that compiles Calor sources and runs tests against them:
/// <c>calor test file.calr</c> or <c>calor test dir/</c>. The sources are
/// materialized as a temporary class library; when the target directory has a
/// tests/ subdirectory of C# files (the benchmark-pair layout: src .calr +
/// tests/*.cs), a generated xUnit project referencing the library runs them via
/// <c>dotnet test</c>. When no tests are found, the library is still built to
/// validate the generated C#, and the command exits with
/// <see cref="NoTestsExitCode"/>.
/// </summary>
public static class TestCommand
{
    /// <summary>
    /// Exit code when the target compiled and built but no tests were found —
    /// distinct from test failure (non-zero from dotnet test) and usage errors (2).
    /// </summary>
    public const int NoTestsExitCode = 3;

    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "A .calr file, or a directory containing .calr files (with an optional tests/ subdirectory of *.cs xUnit tests; bin/, obj/ and reference/ subdirectories are excluded)");

        var command = new Command("test", "Compile Calor sources and run tests against them (no project file required)")
        {
            pathArgument
        };

        var bindSettings = ExecutionWorkspace.AddCommonOptions(command);

        command.SetHandler((InvocationContext ctx) =>
        {
            var path = ctx.ParseResult.GetValueForArgument(pathArgument);
            ctx.ExitCode = Execute(path, bindSettings(ctx));
        });

        return command;
    }

    public static Command CreateLoop()
    {
        var path = new Argument<string>("path", "Calor source file or directory with tests/*.cs xUnit tests");
        var format = new Option<string>("--format", () => "text", "Output format: text or json");
        format.FromAmong("text", "json");
        var timeout = new Option<int>("--timeout", () => 600, "Timeout per build/test process in seconds");
        var solverTimeout = new Option<int>("--verification-timeout", () => 5000, "Solver timeout per contract in milliseconds");
        foreach (var option in new[] { timeout, solverTimeout })
            option.AddValidator(r => { if (r.GetValueOrDefault<int>() <= 0) r.ErrorMessage = "Timeout must be positive."; });
        var command = new Command("check", "Build, test and verify one source snapshot; return only failures")
        {
            path, format, timeout, solverTimeout
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            try
            {
                var result = await RunLoopAsync(
                    context.ParseResult.GetValueForArgument(path),
                    context.ParseResult.GetValueForOption(timeout),
                    context.ParseResult.GetValueForOption(solverTimeout),
                    context.GetCancellationToken());
                Console.WriteLine(context.ParseResult.GetValueForOption(format) == "json"
                    ? JsonSerializer.Serialize(result, LoopJsonOptions)
                    : result.ToText());
                context.ExitCode = result.ExitCode;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Validation cancelled.");
                context.ExitCode = 130;
            }
        });
        return command;
    }

    internal static readonly JsonSerializerOptions LoopJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal sealed record LoopStage(string Name, string Status, int? ExitCode = null);
    internal sealed record LoopFailure(string Stage, string Message, EnvelopeDiagnostic? Diagnostic = null);
    internal sealed class LoopResult
    {
        public string Version => JsonDiagnosticFormatter.SchemaVersion;
        public bool Success => ExitCode == 0;
        public int ExitCode { get; set; }
        public List<LoopStage> Stages { get; } = [];
        public List<LoopFailure> Failures { get; } = [];
        public int TestsRun { get; set; }
        public int ContractsChecked { get; set; }

        internal string ToText() => Success
            ? $"Validation passed: build succeeded, {TestsRun} tests passed, {ContractsChecked} contracts proven."
            : string.Join(Environment.NewLine, Failures.Select(f => f.Diagnostic is { } d
                ? $"{d.Location.File}:{d.Location.Line}:{d.Location.Column} {d.Code} {d.Severity}: {d.Message.ReplaceLineEndings(" ")}"
                    + (d.Verification?.Counterexample is { } counterexample ? $" {counterexample.Rendered}" : "")
                : $"{f.Stage}: {f.Message.ReplaceLineEndings(" ")}"));
    }

    // Both surfaces use the same frozen inputs, compilation and proof outcomes.
    // No original source, project file or test is written or repaired.
    internal static async Task<LoopResult> RunLoopAsync(
        string path, int timeoutSeconds = 600, int verificationTimeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = new LoopResult();
        var activeStage = "compile";
        string? workspace = null;
        try
        {
            if (timeoutSeconds <= 0 || timeoutSeconds > 600 || verificationTimeoutMs <= 0)
                throw new ArgumentException("Process timeout must be 1..600 seconds and verification timeout must be positive.");
            var sources = ExecutionWorkspace.ResolveSources(path, out var error);
            if (sources == null)
            {
                output.ExitCode = 2;
                output.Failures.Add(new("compile", error));
                output.Stages.Add(new("compile", "failed", 2));
                return Finish();
            }

            workspace = ExecutionWorkspace.CreateWorkspace("calor-check");
            var root = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path))!;
            var originals = new Dictionary<string, string>(StringComparer.Ordinal);
            var snapshots = new List<FileInfo>();
            foreach (var source in sources)
            {
                var snapshot = Path.Combine(workspace, "snapshot", Path.GetRelativePath(root, source.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                await File.WriteAllBytesAsync(snapshot, await File.ReadAllBytesAsync(source.FullName, cancellationToken), cancellationToken);
                originals.Add(snapshot, source.FullName);
                snapshots.Add(new FileInfo(snapshot));
            }
            // Capture tests before compilation: an edit during build cannot change this run.
            var testFiles = FindTestFiles(path);
            var testProject = testFiles.Count > 0
                ? WriteTestProject(Path.Combine(workspace, "tests"), testFiles)
                : null;
            var diagnostics = new DiagnosticBag();
            var resolver = new DeclarationIdResolver();
            var options = new List<CompilationOptions>();
            var units = new List<ExecutionWorkspace.CompiledUnit>();
            var compiled = CompilationDriver.CompileAll(snapshots, file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = new CompilationOptions
                {
                    EnforceEffects = true,
                    UnknownCallPolicy = UnknownCallPolicy.Strict,
                    VerifyContracts = true,
                    VerificationTimeoutMs = (uint)verificationTimeoutMs,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
                    CancellationToken = cancellationToken,
                    ProjectDirectory = Path.GetDirectoryName(originals[file.FullName])
                };
                options.Add(settings);
                return settings;
            }, crossModuleEnforcement: true, crossModulePolicy: UnknownCallPolicy.Strict,
                diagnosticSink: diagnostics,
                onAst: (file, source, ast) => resolver.AddFile(file.FullName, source, ast),
                onCompiled: (_, result) => units.Add(new($"source{units.Count}.g.cs", result.GeneratedCode)));
            cancellationToken.ThrowIfCancellationRequested();
            var entries = DiagnosticEnvelope.Build(diagnostics, resolver);
            foreach (var entry in entries.Where(d => d.Severity == "error"))
                output.Failures.Add(new("compile", entry.Message, Remap(entry)));
            output.Stages.Add(new("compile", compiled.AnyErrors ? "failed" : "passed", compiled.AnyErrors ? 1 : 0));
            var verificationFailures = entries.Where(d => IsProofFailure(d.Verification)).ToList();
            foreach (var entry in verificationFailures.Where(d => d.Severity != "error"))
                output.Failures.Add(new("verify", entry.Message, Remap(entry)));
            output.ContractsChecked = options.Sum(o => o.VerificationResults?.GetSummary().Total ?? 0);
            var verificationComplete = options.Count == snapshots.Count && options.All(o => o.VerificationResults != null);
            // Unavailable Z3 is a module-level diagnostic, including modules without contracts.
            var unavailable = entries.Where(d => d.Code == DiagnosticCode.Z3Unavailable).ToList();
            foreach (var entry in unavailable)
                output.Failures.Add(new("verify", entry.Message, Remap(entry,
                    DiagnosticEnvelope.BuildVerification(ProofOutcome.Assign(ProofEvidence.SolverUnavailable(entry.Message))))));
            var verificationPassed = verificationComplete && verificationFailures.Count == 0 && unavailable.Count == 0;
            var verificationStage = new LoopStage("verify",
                verificationPassed ? "passed" : verificationComplete ? "failed" : "incomplete",
                verificationComplete ? verificationPassed ? 0 : 1 : null);
            if (compiled.AnyErrors)
            {
                output.ExitCode = 1;
                output.Stages.Add(new("build", "skipped"));
                output.Stages.Add(new("test", "skipped"));
                output.Stages.Add(verificationStage);
                return output;
            }
            activeStage = "build";
            var dotnet = ExecutionWorkspace.FindDotnet();
            if (dotnet == null)
                throw new InvalidOperationException(ExecutionWorkspace.DotnetMissingMessage);
            var project = ExecutionWorkspace.WriteProject(Path.Combine(workspace, "src"), "CalorSrc", false, units);
            var build = await ExecutionWorkspace.CaptureProcessAsync(dotnet,
                ["build", testProject ?? project, "--nologo", "-v", "quiet", "-clp:ErrorsOnly;NoSummary"],
                workspace, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            output.Stages.Add(new("build", build.TimedOut ? "timeout" : build.ExitCode == 0 ? "passed" : "failed", build.ExitCode));
            if (build.ExitCode != 0)
            {
                output.ExitCode = build.ExitCode;
                output.Failures.Add(new("build", $"Build exited with code {build.ExitCode}. {build.Output.Trim()}"));
                output.Stages.Add(new("test", "skipped"));
            }
            else if (testProject == null)
            {
                output.ExitCode = NoTestsExitCode;
                output.Stages.Add(new("test", "no_tests", NoTestsExitCode));
                output.Failures.Add(new("test", "No tests found: expected tests/*.cs in the target directory."));
            }
            else
            {
                activeStage = "test";
                var resultsDir = Path.Combine(workspace, "results");
                var test = await ExecutionWorkspace.CaptureProcessAsync(dotnet,
                    ["test", testProject, "--no-build", "--no-restore", "--nologo", "-v", "quiet",
                        "--logger", "trx;LogFileName=validation.trx", "--results-directory", resultsDir],
                    workspace, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
                var trxPath = Path.Combine(resultsDir, "validation.trx");
                var status = test.TimedOut ? "timeout" : "failed";
                output.ExitCode = test.ExitCode;
                if (!test.TimedOut && File.Exists(trxPath))
                {
                    var trx = XDocument.Load(trxPath);
                    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                    var results = trx.Descendants(ns + "UnitTestResult").ToList();
                    output.TestsRun = results.Count(r => (string?)r.Attribute("outcome") != "NotExecuted");
                    foreach (var failed in results.Where(r => (string?)r.Attribute("outcome") != "Passed")
                                 .OrderBy(r => (string?)r.Attribute("testName"), StringComparer.Ordinal))
                        output.Failures.Add(new("test", $"{failed.Attribute("testName")?.Value}: {failed.Attribute("outcome")?.Value}: "
                            + failed.Element(ns + "Output")?.Value.Trim()));
                    if (results.Count == 0)
                    {
                        output.ExitCode = test.ExitCode == 0 ? NoTestsExitCode : test.ExitCode;
                        status = "no_tests";
                        output.Failures.Add(new("test", "No tests were executed."));
                    }
                    else if (test.ExitCode == 0 && results.All(r => (string?)r.Attribute("outcome") == "Passed"))
                        status = "passed";
                    else if (test.ExitCode == 0)
                        output.ExitCode = 1;
                    if (status == "failed" && output.Failures.All(f => f.Stage != "test"))
                        output.Failures.Add(new("test", $"Test runner exited with code {test.ExitCode}. {test.Output.Trim()}"));
                }
                else
                {
                    if (output.ExitCode == 0) output.ExitCode = 2;
                    output.Failures.Add(new("test", test.TimedOut ? test.Output : $"Test runner did not produce results. {test.Output.Trim()}"));
                }
                output.Stages.Add(new("test", status, output.ExitCode));
            }
            output.Stages.Add(verificationStage);
            if (!verificationPassed && output.ExitCode == 0) output.ExitCode = 1;
            return output;

            EnvelopeDiagnostic Remap(EnvelopeDiagnostic d, EnvelopeVerification? verification = null)
            {
                string? Original(string? value) => value != null && originals.TryGetValue(value, out var original) ? original : value;
                return new EnvelopeDiagnostic
                {
                    Code = d.Code, Message = d.Message, Severity = d.Severity, DeclarationId = d.DeclarationId,
                    Verification = verification ?? d.Verification, Suggestion = d.Suggestion,
                    Location = new EnvelopeLocation
                    {
                        File = Original(d.Location.File), Line = d.Location.Line,
                        Column = d.Location.Column, Length = d.Location.Length
                    },
                    Fix = d.Fix == null ? null : new EnvelopeFix
                    {
                        Description = d.Fix.Description, IsSafeToApply = d.Fix.IsSafeToApply,
                        Edits = d.Fix.Edits.Select(e => new EnvelopeEdit
                        {
                            FilePath = Original(e.FilePath)!, StartLine = e.StartLine, StartColumn = e.StartColumn,
                            EndLine = e.EndLine, EndColumn = e.EndColumn, NewText = e.NewText
                        }).ToList()
                    }
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Xml.XmlException)
        {
            output.ExitCode = 2;
            output.Failures.Add(new(activeStage, ex.Message));
            if (output.Stages.All(s => s.Name != activeStage))
                output.Stages.Add(new(activeStage, "failed", 2));
            return Finish();
        }
        finally
        {
            if (workspace != null) ExecutionWorkspace.FinishWorkspace(workspace, keepTemp: false);
        }

        LoopResult Finish()
        {
            foreach (var stage in new[] { "compile", "build", "test", "verify" })
                if (output.Stages.All(s => s.Name != stage))
                    output.Stages.Add(new(stage, "skipped"));
            return output;
        }
    }

    internal static bool IsProofFailure(EnvelopeVerification? proof)
        => proof != null && (proof.Status != "proven" || proof.Vacuous == true);

    private static int Execute(string path, ExecutionWorkspace.ExecutionSettings settings)
    {
        var prepared = ExecutionWorkspace.Prepare(path, settings, out var exitCode);
        if (prepared == null)
        {
            return exitCode;
        }

        var testFiles = FindTestFiles(path);

        var workspace = ExecutionWorkspace.CreateWorkspace("calor-test");
        try
        {
            var srcProjectPath = ExecutionWorkspace.WriteProject(
                Path.Combine(workspace, "src"), "CalorSrc", executable: false, prepared.Units);

            if (testFiles.Count == 0)
            {
                // No tests to run: still build the library so the generated C#
                // is validated, then report the distinct "no tests" exit code.
                var buildExit = ExecutionWorkspace.RunProcessCaptured(prepared.Dotnet,
                    ["build", srcProjectPath, "--nologo", "-v", settings.Verbose ? "minimal" : "quiet"],
                    workspace,
                    stream: settings.Verbose,
                    settings.Timeout);
                if (buildExit != 0)
                {
                    Console.Error.WriteLine("Error: build of the generated C# project failed (see output above).");
                    return buildExit;
                }

                Console.Error.WriteLine(
                    "No tests found: no tests/ directory with .cs files next to the sources. " +
                    "The generated C# builds successfully.");
                return NoTestsExitCode;
            }

            var testProjectPath = WriteTestProject(Path.Combine(workspace, "tests"), testFiles);

            return ExecutionWorkspace.RunProcess(prepared.Dotnet,
                ["test", testProjectPath, "--nologo", "-v", settings.Verbose ? "normal" : "minimal"],
                workspace,
                settings.Timeout);
        }
        finally
        {
            ExecutionWorkspace.FinishWorkspace(workspace, settings.KeepTemp);
        }
    }

    /// <summary>
    /// Finds test sources for the v1 benchmark-pair layout: a tests/ subdirectory
    /// with top-level *.cs files, plus an optional tests/shims/TestShim.calor.cs
    /// (the Calor-arm shim, copied in as TestShim.cs).
    /// </summary>
    private static List<(string SourcePath, string TargetName)> FindTestFiles(string path)
    {
        var files = new List<(string SourcePath, string TargetName)>();
        if (!Directory.Exists(path))
        {
            return files;
        }

        var testsDir = Path.Combine(path, "tests");
        if (!Directory.Exists(testsDir))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            files.Add((file, Path.GetFileName(file)));
        }

        var calorShim = Path.Combine(testsDir, "shims", "TestShim.calor.cs");
        if (File.Exists(calorShim))
        {
            files.Add((calorShim, "TestShim.cs"));
        }

        return files;
    }

    private static string WriteTestProject(string projectDir, List<(string SourcePath, string TargetName)> testFiles)
    {
        Directory.CreateDirectory(projectDir);
        foreach (var (sourcePath, targetName) in testFiles)
        {
            File.Copy(sourcePath, Path.Combine(projectDir, targetName), overwrite: true);
        }

        // Package versions match the phase0 benchmark harness pins
        // (bench/phase0-agent-native/run-pair.sh) so both execution paths
        // resolve identical test-stack versions from the local NuGet cache.
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
                <PackageReference Include="xunit" Version="2.9.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
              </ItemGroup>

              <ItemGroup>
                <ProjectReference Include="../src/CalorSrc.csproj" />
              </ItemGroup>

            </Project>
            """;

        var csprojPath = Path.Combine(projectDir, "CalorTests.csproj");
        File.WriteAllText(csprojPath, csproj);
        return csprojPath;
    }
}
