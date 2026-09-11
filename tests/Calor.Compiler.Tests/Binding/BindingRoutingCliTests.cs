using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

public class BindingRoutingCliTests : IDisposable
{
    private const string Duplicate = """
        §M{m1:Routing}
          §F{f1:Probe:pub} () -> i32
            §R 1
          §F{f2:Probe:pub} () -> i32
            §R 2
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

    [Theory]
    [MemberData(nameof(RootModes))]
    public void RootCli_ActiveErrorMatchesApiAcrossSupportedModes(int mode, bool environmentOptOut)
    {
        var file = Path.Combine(_directory, "input.calr");
        File.WriteAllText(file, Duplicate);
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
        var api = Assert.Single(Program.Compile(Duplicate, file).Diagnostics);
        Assert.Equal(api.Code, diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        var location = diagnostic.GetProperty("location");
        Assert.Equal(api.Span.Line, location.GetProperty("line").GetInt32());
        Assert.Equal(api.Span.Column, location.GetProperty("column").GetInt32());
        Assert.Equal(api.Span.Length, location.GetProperty("length").GetInt32());
    }

    [Theory]
    [MemberData(nameof(BindingDiagnosticPolicyTests.NullableBoundaries), MemberType = typeof(BindingDiagnosticPolicyTests))]
    public void RootCli_PlannedNullabilityRemainsAnalysisOnly(
        string code, Binding.BindingReceivingBoundary boundary, string body, string returnType)
    {
        _ = boundary;
        var file = Path.Combine(_directory, "input.calr");
        File.WriteAllText(file, $"§M{{m1:Routing}}\n  §F{{f1:Probe:pub}} () -> {returnType}\n    §E{{env}}\n    {body}\n");
        var result = CliTestHarness.RunCli(_directory, "-i", file, "--format", "json", "--no-cache", "--no-telemetry");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StdOut);
        Assert.DoesNotContain(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == code);
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
        File.WriteAllText(file, Duplicate);
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
}
