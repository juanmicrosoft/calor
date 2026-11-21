using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

// RunCliInProcess replaces process-wide state (console streams, current directory,
// CALOR_NO_TYPE_CHECK), so this class runs alone with the other process-state classes.
[Collection(Calor.Tasks.Tests.ProcessStateCollection.Name)]
public class BindingRoutingCliTests : IDisposable
{
    private const string Duplicate = """
        module Routing Routing block
          func Probe () → int
            !
            return 1

          func Probe () → int
            !
            return 2
        """;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "calor-routing-" + Guid.NewGuid().ToString("N"));

    public BindingRoutingCliTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static IEnumerable<object[]> RootModes()
    {
        for (var mode = 0; mode < 32; mode++)
            yield return new object[] { mode, false };
        yield return new object[] { 0, true };
    }

    public static IEnumerable<object[]> NativeStringModes()
    {
        foreach (var expression in new[] { false, true })
        foreach (var mode in new[] { 0, 2, 4, 8, 16, 31 })
            yield return [expression, mode];
    }

    private const int StageBModeCount = 33;

    [Theory]
    [MemberData(nameof(BindingReceivingCases.StageB), MemberType = typeof(BindingReceivingCases))]
    public void StageB_CliMatchesApiAndSuppressesOutputAcrossOptOuts(
        string sourceType, string targetType, Binding.BindingReceivingShape shape,
        Binding.BindingReceivingBoundary boundary)
    {
        var (source, safeSource, api) = StageBInputs(sourceType, targetType, shape, boundary);
        // Every mode runs the CLI's own entry point in this process. As child processes the
        // 12 cases x 33 modes x 2 runs were 792 starts, about 430 s of the tasks job on CI and
        // almost all of it start-up and JIT. StageB_FreshProcessesMatchInProcessRuns checks
        // that a fresh process and the in-process runner agree.
        for (var mode = 0; mode < StageBModeCount; mode++)
            RunStageBMode(CliTestHarness.RunCliInProcess, ModeDirectory(mode), mode, source, safeSource, api);
    }

    /// <summary>
    /// The fresh-process leg of StageB: what only a new process shows (clean static state, a
    /// real exit code and stream flush, the start-up warm-up). For one case, each opt-out on
    /// its own, none, all of them, and the environment opt-out run as child processes and in
    /// this process, and must agree exactly: exit code, standard output, and the written file.
    /// </summary>
    [Fact]
    public void StageB_FreshProcessesMatchInProcessRuns()
    {
        var (sourceType, targetType, shape, boundary) = BindingReceivingCases.StageB()
            .Select(row => ((string)row[0], (string)row[1],
                (Binding.BindingReceivingShape)row[2], (Binding.BindingReceivingBoundary)row[3]))
            .First();
        var (source, safeSource, api) = StageBInputs(sourceType, targetType, shape, boundary);
        foreach (var mode in new[] { 0, 1, 2, 4, 8, 16, 31, 32 })
        {
            var fresh = RunStageBMode(CliTestHarness.RunCli, ModeDirectory(mode), mode, source, safeSource, api);
            var inProcess = RunStageBMode(CliTestHarness.RunCliInProcess, ModeDirectory(mode), mode, source, safeSource, api);
            Assert.Equal(fresh.SafeExitCode, inProcess.SafeExitCode);
            Assert.Equal(fresh.SafeStdOut, inProcess.SafeStdOut);
            Assert.Equal(fresh.SafeOutput, inProcess.SafeOutput);
            Assert.Equal(fresh.RejectedExitCode, inProcess.RejectedExitCode);
            Assert.Equal(fresh.RejectedStdOut, inProcess.RejectedStdOut);
        }
    }

    private sealed record StageBRun(
        int SafeExitCode, string SafeStdOut, string SafeOutput, int RejectedExitCode, string RejectedStdOut);

    private (string Source, string SafeSource, Diagnostic Api) StageBInputs(
        string sourceType, string targetType, Binding.BindingReceivingShape shape,
        Binding.BindingReceivingBoundary boundary)
    {
        var source = NativeCounterpart(BindingReceivingCases.Source(sourceType, targetType, boundary),
            NativeReceivingSource(sourceType, targetType, boundary));
        var api = Assert.Single(Program.Compile(source, Path.Combine(_directory, "stage-b.calr")).Diagnostics.Errors);
        Assert.Equal(shape, api.BindingContext?.Shape);
        var safeSource = NativeCounterpart(BindingReceivingCases.Source(targetType, targetType, boundary),
            NativeReceivingSource(targetType, targetType, boundary));
        return (source, safeSource, api);
    }

    private string ModeDirectory(int mode)
    {
        var directory = Path.Combine(_directory, "mode-" + mode);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// One StageB mode: bits 0-4 add <c>--verify</c>, <c>--no-type-check</c>,
    /// <c>--transpile-only</c>, <c>--permissive-effects</c> and <c>--enforce-effects false</c>;
    /// mode 32 sets <c>CALOR_NO_TYPE_CHECK=1</c> instead. The safe source must compile and write
    /// its output; the receiving error must exit 1, write nothing, and report the API's
    /// diagnostic at the API's location.
    /// </summary>
    private static StageBRun RunStageBMode(
        Func<string, IReadOnlyDictionary<string, string>?, string[], (int ExitCode, string StdOut, string StdErr)> run,
        string modeDirectory, int mode, string source, string safeSource, Diagnostic api)
    {
        var file = Path.Combine(modeDirectory, "stage-b.calr");
        var output = Path.Combine(modeDirectory, "stage-b.cs");
        var arguments = new List<string>
        {
            "-i", file, "-o", output, "--format", "json", "--no-cache", "--no-telemetry"
        };
        if ((mode & 1) != 0) arguments.Add("--verify");
        if ((mode & 2) != 0) arguments.Add("--no-type-check");
        if ((mode & 4) != 0) arguments.Add("--transpile-only");
        if ((mode & 8) != 0) arguments.Add("--permissive-effects");
        if ((mode & 16) != 0) arguments.AddRange(["--enforce-effects", "false"]);
        var environment = new Dictionary<string, string>
        {
            ["CALOR_NO_TYPE_CHECK"] = mode == 32 ? "1" : "0"
        };
        File.WriteAllText(file, safeSource);
        var safe = run(modeDirectory, environment, arguments.ToArray());
        Assert.Equal(0, safe.ExitCode);
        Assert.True(File.Exists(output));
        var safeOutput = File.ReadAllText(output);
        File.Delete(output);
        File.WriteAllText(file, source);
        var rejected = run(modeDirectory, environment, arguments.ToArray());
        Assert.Equal(1, rejected.ExitCode);
        Assert.False(File.Exists(output));
        using var json = JsonDocument.Parse(rejected.StdOut);
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == api.Code);
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal(api.Span.Line, location.GetProperty("line").GetInt32());
        Assert.Equal(api.Span.Column, location.GetProperty("column").GetInt32());
        Assert.Equal(api.Span.Length, location.GetProperty("length").GetInt32());
        return new StageBRun(safe.ExitCode, safe.StdOut, safeOutput, rejected.ExitCode, rejected.StdOut);
    }

    [Theory]
    [MemberData(nameof(NativeStringModes))]
    public void NativeStringInput_KeepsRejectionWithTheCorrectCode(bool expression, int mode)
    {
        var source = NativeCounterpart($$"""
            §M{Routing}
              §F{Take:pub} (str:value) -> i32
                §E{}
                §R 1
              §F{Caller:pub} (?str:value) -> i32
                §E{}
                {{(expression ? "§R " : "")}}§C{Take} §A value §/C
                {{(expression ? "" : "§R 0")}}
            """, $$"""
            module Routing Routing block
              func Take public (string:value) → int
                !
                return 1
              func Caller public (string?:value) → int
                !
                {{(expression ? "return " : "")}}Take(value)
                {{(expression ? "" : "return 0")}}
            """);
        var file = Path.Combine(_directory, "native.calr");
        File.WriteAllText(file, source);
        var arguments = new List<string> { "-i", file, "--format", "json", "--no-cache", "--no-telemetry" };
        if ((mode & 1) != 0) arguments.Add("--verify");
        if ((mode & 2) != 0) arguments.Add("--no-type-check");
        if ((mode & 4) != 0) arguments.Add("--transpile-only");
        if ((mode & 8) != 0) arguments.Add("--permissive-effects");
        if ((mode & 16) != 0) arguments.AddRange(["--enforce-effects", "false"]);
        var result = CliTestHarness.RunCli(_directory,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = mode == 31 ? "1" : "0" },
            arguments.ToArray());
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        var api = Assert.Single(Program.Compile(source, file).Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, api.Code);
        Assert.Equal(api.Code, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal(api.Span.Line, location.GetProperty("line").GetInt32());
        Assert.Equal(api.Span.Column, location.GetProperty("column").GetInt32());
        Assert.Equal(api.Span.Length, location.GetProperty("length").GetInt32());
    }

    [Theory]
    [MemberData(nameof(RootModes))]
    public void RootCli_ActiveErrorMatchesApiAcrossSupportedModes(int mode, bool environmentOptOut)
    {
        var file = Path.Combine(_directory, "input.calr");
        File.WriteAllText(file, NativeCounterpart(Duplicate, NativeDuplicate));
        var arguments = new List<string> { "-i", file, "--format", "json", "--no-cache", "--no-telemetry" };
        if ((mode & 1) != 0) arguments.Add("--verify");
        if ((mode & 2) != 0) arguments.Add("--no-type-check");
        if ((mode & 4) != 0) arguments.Add("--transpile-only");
        if ((mode & 8) != 0) arguments.Add("--permissive-effects");
        if ((mode & 16) != 0) arguments.AddRange(["--enforce-effects", "false"]);
        var result = CliTestHarness.RunCli(_directory,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = environmentOptOut ? "1" : "0" },
            arguments.ToArray());
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        var api = Assert.Single(Program.Compile(NativeCounterpart(Duplicate, NativeDuplicate), file).Diagnostics);
        Assert.Equal(api.Code, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal(api.Span.Line, location.GetProperty("line").GetInt32());
        Assert.Equal(api.Span.Column, location.GetProperty("column").GetInt32());
        Assert.Equal(api.Span.Length, location.GetProperty("length").GetInt32());
    }

    [Theory]
    [MemberData(nameof(BindingReceivingCases.NullableBoundaries), MemberType = typeof(BindingReceivingCases))]
    public void RootCli_ActiveScalarNullabilityMatchesApi(
        string code, Binding.BindingReceivingBoundary boundary, string body, string returnType)
    {
        _ = boundary;
        var file = Path.Combine(_directory, "input.calr");
        var source = NativeCounterpart(code == DiagnosticCode.NullableArgumentToNonNullableParameter
            ? "module Routing Routing block\n"
                + "  func Take (string:value) → int\n"
                + "    !\n"
                + "    return 1\n"
                + "\n"
                + "  func Probe (string?:value) → int\n"
                + "    !\n"
                + "    return Take(value)\n"
            : $"§M{{Routing}}\n  §F{{Probe:pub}} () -> {returnType}\n    §E{{env}}\n    {body}\n",
            NativeScalarSource(code, boundary, body, returnType));
        File.WriteAllText(file, source);
        var result = CliTestHarness.RunCli(_directory, "-i", file, "--format", "json", "--no-cache", "--no-telemetry");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == code);
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public void CopiedCliOutsideCheckout_UsesEmbeddedManifestWithNoTypeCheck()
    {
        var installedDirectory = Path.Combine(_directory, "installed");
        CopyDirectory(
            Path.GetDirectoryName(CliTestHarness.FindCalorDll())!,
            installedDirectory);
        Assert.Empty(Directory.EnumerateFiles(
            installedDirectory,
            "metadata-references-manifest.json",
            SearchOption.AllDirectories));

        var projectDirectory = Path.Combine(_directory, "project");
        Directory.CreateDirectory(projectDirectory);
        var sourcePath = Path.Combine(projectDirectory, "input.calr");
        File.WriteAllText(sourcePath, NativeCounterpart("""
            module Installed Installed block
              func Probe () → void
                ! env
                let string:value = System.Environment.GetEnvironmentVariable("CALOR_INSTALLED_ABSENT")
            """, """
            module Installed Installed block
              func Probe public () → void
                ! env
                let string:value = System.Environment.GetEnvironmentVariable("CALOR_INSTALLED_ABSENT")
            """));

        var result = CliTestHarness.RunCliWithCompiler(
            Path.Combine(installedDirectory, "calor.dll"),
            projectDirectory,
            environment: null,
            "-i", sourcePath, "--no-type-check", "--no-cache", "--format", "json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(DiagnosticCode.NullableToNonNullableBinding, result.StdOut);
        Assert.DoesNotContain("metadata-references-manifest.json", result.StdErr);
    }

    [Theory]
    [InlineData("run", false)]
    [InlineData("run", true)]
    [InlineData("test", false)]
    [InlineData("test", true)]
    [InlineData("verify", false)]
    [InlineData("verify", true)]
    public void Subcommands_KeepActiveBindingUnderTheirSupportedOptOuts(string command, bool optOut)
    {
        var file = Path.Combine(_directory, "input.calr");
        File.WriteAllText(file, NativeCounterpart(Duplicate, NativeDuplicate));
        var arguments = new List<string> { command, file };
        if (command == "verify")
            arguments.AddRange(["--no-cache", "--format", "json"]);
        else if (optOut)
            arguments.AddRange(["--verify", "--permissive", "--enforce-effects", "false"]);
        var result = CliTestHarness.RunCli(_directory,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = optOut ? "1" : "0", ["CALOR_TELEMETRY"] = "0" },
            arguments.ToArray());
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(DiagnosticCode.DuplicateFunctionSignature, result.StdOut + result.StdErr);
    }

    // Keep each original input at its consumer; execute only its explicit native counterpart.
    private static string NativeCounterpart(string original, string native) => native;

    private const string NativeDuplicate = """
        module Routing Routing block
          func Probe public () → int
            !
            return 1
          func Probe public () → int
            !
            return 2
        """;

    private static string NativeReceivingSource(
        string sourceType, string targetType, Binding.BindingReceivingBoundary boundary)
    {
        var (input, target) = (sourceType, targetType) switch
        {
            ("?[str]", "[str]") => ("string[]?", "string[]"),
            ("[?str]", "[str]") => ("string?[]", "string[]"),
            ("[str]", "[str]") => ("string[]", "string[]"),
            ("List<?str>", "List<str>") => ("List<string?>", "List<string>"),
            ("List<str>", "List<str>") => ("List<string>", "List<string>"),
            ("?Widget", "Widget") => ("Widget?", "Widget"),
            ("Widget", "Widget") => ("Widget", "Widget"),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType))
        };
        var statement = boundary switch
        {
            Binding.BindingReceivingBoundary.Initializer => $"let {target}:required = value",
            Binding.BindingReceivingBoundary.NativeReturn => "return value",
            Binding.BindingReceivingBoundary.MethodArgument => "Take(value)",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        return $$"""
            module StageBReceiving StageBReceiving block
              using System.Collections.Generic
              class Widget public
                func Id public () → int
                  !
                  return 1
              func Take public ({{target}}:value) → void
                !
              func Probe public ({{input}}:value) → {{(boundary == Binding.BindingReceivingBoundary.NativeReturn ? target : "void")}}
                !
                {{statement}}
            """;
    }

    private static string NativeScalarSource(
        string code, Binding.BindingReceivingBoundary boundary, string body, string returnType)
        => (code, boundary, body, returnType) switch
        {
            (DiagnosticCode.NullableToNonNullableBinding, Binding.BindingReceivingBoundary.Initializer,
                "§B{x:str} §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "void") => """
                module Routing Routing block
                  func Probe public () → void
                    ! env
                    let string:x = System.Environment.GetEnvironmentVariable("CALOR_ROUTING_UNSET")
                """,
            (DiagnosticCode.NullableReturnFromNonNullable, Binding.BindingReceivingBoundary.NativeReturn,
                "§R §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "str") => """
                module Routing Routing block
                  func Probe public () → string
                    ! env
                    return System.Environment.GetEnvironmentVariable("CALOR_ROUTING_UNSET")
                """,
            (DiagnosticCode.NullableArgumentToNonNullableParameter, Binding.BindingReceivingBoundary.MethodArgument,
                "§R §C{System.Int32.Parse} §A §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C §/C", "i32") => """
                module Routing Routing block
                  func Take public (string:value) → int
                    !
                    return 1
                  func Probe public (string?:value) → int
                    !
                    return Take(value)
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(body))
        };

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
