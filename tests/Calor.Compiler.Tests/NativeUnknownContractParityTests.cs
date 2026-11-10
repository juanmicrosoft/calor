using System.Text.Json.Nodes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeUnknownContractParityTests
{
    public static IEnumerable<object[]> InvocationCases()
    {
        foreach (var suffix in new[] { "", " ! unknown", " !", " ! cw" })
        foreach (var permissive in new[] { false, true })
        foreach (var strictEffects in new[] { false, true })
            yield return [suffix, permissive, strictEffects];
    }

    [Theory]
    [MemberData(nameof(InvocationCases))]
    public void SourceApiAndCliAgreeOnUnknownPureAndKnownInvocation(
        string suffix, bool permissive, bool strictEffects)
    {
        var source = $$"""
            module Contracts
              func Run (Func<int>:callback{{suffix}}) → int
                !
                return callback()
            """;
        var expectedFailure = suffix == " ! cw" || suffix is "" or " ! unknown" && !permissive;
        AssertParity(source, permissive, strictEffects, expectedFailure);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(" ! unknown", false)]
    [InlineData(" ! unknown", true)]
    public void TransportedUnknownRemainsUncertainRatherThanExecuting(string suffix, bool permissive)
    {
        var source = $$"""
            module Contracts
              func Keep (Func<int>:callback{{suffix}}) → Func<int>{{suffix}}
                !
                return callback
            """;
        var result = AssertParity(source, permissive, strictEffects: true, expectedFailure: !permissive);
        Assert.Equal(!permissive, result.Diagnostics.Any(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(" ! unknown", false)]
    [InlineData(" ! unknown", true)]
    public void UnknownAliasChainsStayFailClosedAcrossSourceApiAndCli(string suffix, bool permissive)
    {
        var source = $$"""
            module Contracts
              func Run (Func<int>:callback{{suffix}}) → int
                ! cw
                let Func<int>:first = callback
                let Func<int>:second = first
                let Func<int>:third = second
                return third()
            """;
        var result = AssertParity(source, permissive, strictEffects: true, expectedFailure: !permissive);
        if (!permissive)
        {
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("'unknown'"));
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(" ! unknown", false)]
    [InlineData(" ! unknown", true)]
    public void MetadataAliasCannotNarrowUnknownToPureWithoutAnUncertaintyError(string suffix, bool permissive)
    {
        var source = $$"""
            module Contracts
              using Callback = System.Func<int>
              func Run (Callback:callback{{suffix}}) → int
                !
                let Callback:first = callback
                let Callback:second = first
                let Callback:third ! = second
                return third()
            """;
        var result = AssertParity(source, permissive, strictEffects: true, expectedFailure: !permissive);
        Assert.Equal(!permissive, result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.EffectRowUnknown));
    }

    private static CompilationResult AssertParity(
        string source, bool permissive, bool strictEffects, bool expectedFailure)
    {
        var options = new CompilationOptions
        {
            UnknownCallPolicy = permissive ? UnknownCallPolicy.Permissive : UnknownCallPolicy.Strict,
            StrictEffects = strictEffects,
            StatusWriter = TextWriter.Null
        };
        var result = Program.Compile(source, "contracts.calr", options);
        Assert.Equal(expectedFailure, result.HasErrors);

        var directory = Path.Combine(Path.GetTempPath(), "calor-unknown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "contracts.calr"), source);
            var arguments = new List<string>
            {
                "--input", "contracts.calr", "--output", "contracts.g.cs", "--format", "json"
            };
            if (permissive)
                arguments.Add("--permissive-effects");
            if (strictEffects)
                arguments.Add("--strict-effects");
            var cli = CliTestHarness.RunCli(directory, arguments.ToArray());
            Assert.Equal(expectedFailure ? 1 : 0, cli.ExitCode);
            var actual = JsonNode.Parse(cli.StdOut)!["diagnostics"]!.AsArray()
                .Select(diagnostic => (
                    Code: diagnostic!["code"]!.GetValue<string>(),
                    Severity: diagnostic["severity"]!.GetValue<string>()))
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray();
            var expected = result.Diagnostics.Select(diagnostic => (
                    diagnostic.Code, diagnostic.Severity.ToString().ToLowerInvariant()))
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(!expectedFailure, File.Exists(Path.Combine(directory, "contracts.g.cs")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
        return result;
    }
}
