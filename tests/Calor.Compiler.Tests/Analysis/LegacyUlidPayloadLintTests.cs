using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for the opt-in Calor0821 lint
/// (<see cref="LegacyUlidPayloadLint"/>).
///
/// The lint finds 26-char Crockford-uppercase ULID payloads paired
/// with one of the canonical declaration prefixes, which the Phase 2
/// migrator (<c>calor fix --compact-ids</c>) can rewrite.
/// </summary>
public class LegacyUlidPayloadLintTests
{
    private static string GenerateUlid() => Ulid.NewUlid().ToString();

    [Fact]
    public void FlagsLegacyModuleId()
    {
        var ulid = GenerateUlid();
        var src = $"§M{{Calc}} m_{ulid}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "a.calr");

        var f = Assert.Single(findings);
        Assert.Equal("a.calr", f.File);
        Assert.Equal("m_", f.Prefix);
        Assert.Equal(ulid, f.Payload);
        Assert.Equal(26 + 2, f.Length);
    }

    [Fact]
    public void FlagsLegacyFunctionId()
    {
        var ulid = GenerateUlid();
        var src = $"f_{ulid}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "b.calr");

        var f = Assert.Single(findings);
        Assert.Equal("f_", f.Prefix);
        Assert.Equal(ulid, f.Payload);
    }

    [Fact]
    public void FlagsAllRecognisedPrefixes()
    {
        var prefixes = new[]
        {
            IdGenerator.ModulePrefix,
            IdGenerator.FunctionPrefix,
            IdGenerator.ClassPrefix,
            IdGenerator.InterfacePrefix,
            IdGenerator.PropertyPrefix,
            IdGenerator.MethodPrefix,
            IdGenerator.ConstructorPrefix,
            IdGenerator.EnumPrefix,
            IdGenerator.OperatorOverloadPrefix,
        };

        var lines = string.Join("\n", prefixes.Select(p => p + GenerateUlid()));

        var findings = LegacyUlidPayloadLint.Scan(lines, "c.calr");

        Assert.Equal(prefixes.Length, findings.Count);
        for (int i = 0; i < prefixes.Length; i++)
        {
            Assert.Equal(prefixes[i], findings[i].Prefix);
        }
    }

    [Fact]
    public void RejectsCompactPayloads()
    {
        var compact = CompactIdGenerator.Generate(IdKind.Module);
        var src = $"§M{{Calc}} {compact}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "d.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void RejectsRunsLongerThan26Chars()
    {
        // 27 valid Crockford-upper chars after the prefix — must not
        // be flagged because the trailing word-boundary check fails.
        var src = "m_0123456789ABCDEFGHJKMNPQRSTV\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "e.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void RejectsSuffixOfLongerIdentifier()
    {
        // Prefix preceded by another identifier character must not
        // count: "xm_" is not a "m_" boundary.
        var ulid = GenerateUlid();
        var src = $"xm_{ulid}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "f.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DistinguishesCtorFromClass()
    {
        var ulid = GenerateUlid();
        var src = $"ctor_{ulid}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "g.calr");

        var f = Assert.Single(findings);
        Assert.Equal("ctor_", f.Prefix);
    }

    [Fact]
    public void ProducesAccurateLineAndColumn()
    {
        var ulid = GenerateUlid();
        var src = $"// header\n  f_{ulid}\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "h.calr");

        var f = Assert.Single(findings);
        Assert.Equal(2, f.Line);
        Assert.Equal(3, f.Column);
    }

    [Fact]
    public void RejectsInvalidCrockfordChars()
    {
        // Contains 'I', 'L', 'O', 'U' which are not in the ULID
        // alphabet — must not match.
        var src = "m_ILOU56789ABCDEFGHJKMNPQRS\n";

        var findings = LegacyUlidPayloadLint.Scan(src, "i.calr");

        Assert.Empty(findings);
    }

    [Fact]
    public void DiagnosticCodeIsRegistered()
    {
        Assert.Equal("Calor0821", DiagnosticCode.LegacyUlidPayload);
    }
}
