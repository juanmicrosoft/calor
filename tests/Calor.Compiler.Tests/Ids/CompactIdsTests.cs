using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Unit tests for PR-2a (<see cref="CompactIdGenerator"/>),
/// PR-2b (<see cref="IdRegistry"/>), and PR-2c (format-aware
/// <see cref="IdGenerator.ExtractIdPortion"/> /
/// <see cref="IdGenerator.IsCompactId"/> /
/// <see cref="IdGenerator.IsLegacyUlid"/>).
/// </summary>
public class CompactIdsTests
{
    [Fact]
    public void GeneratedPayloadIs12CharsFromAlphabet()
    {
        for (int i = 0; i < 100; i++)
        {
            var id = CompactIdGenerator.Generate(IdKind.Function);
            Assert.StartsWith("f_", id);
            var payload = id.Substring(2);
            Assert.Equal(12, payload.Length);
            Assert.True(CompactIdGenerator.IsValidPayload(payload),
                $"invalid payload chars: {payload}");
        }
    }

    [Fact]
    public void AlphabetExcludesVisuallyAmbiguousChars()
    {
        Assert.DoesNotContain('i', CompactIdGenerator.Alphabet);
        Assert.DoesNotContain('l', CompactIdGenerator.Alphabet);
        Assert.DoesNotContain('o', CompactIdGenerator.Alphabet);
        Assert.DoesNotContain('u', CompactIdGenerator.Alphabet);
        Assert.Equal(32, CompactIdGenerator.Alphabet.Length);
    }

    [Fact]
    public void GenerateWithPrefixHonoursPrefix()
    {
        var id = CompactIdGenerator.GenerateWithPrefix("mt_");
        Assert.StartsWith("mt_", id);
        Assert.Equal(12, id.Substring(3).Length);
    }

    [Fact]
    public void IsValidPayloadRejectsOutOfAlphabet()
    {
        Assert.False(CompactIdGenerator.IsValidPayload("01234567890i")); // 'i' excluded
        Assert.False(CompactIdGenerator.IsValidPayload(null));
        Assert.False(CompactIdGenerator.IsValidPayload(""));
        Assert.False(CompactIdGenerator.IsValidPayload("0123456789ab1")); // 13 chars
    }

    [Fact]
    public void RegistryDetectsCollision()
    {
        var r = new IdRegistry();
        Assert.True(r.TryRegister("f_aaaaaaaaaaaa"));
        Assert.False(r.TryRegister("f_aaaaaaaaaaaa"));
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void RegistryGenerateAndRegisterReturnsUniqueIds()
    {
        var r = new IdRegistry();
        var seen = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            var id = r.GenerateAndRegister(IdKind.Method);
            Assert.True(seen.Add(id), $"duplicate id at iteration {i}: {id}");
        }
        Assert.Equal(1000, r.Count);
    }

    [Fact]
    public void IsCompactIdTrueForFreshlyGenerated()
    {
        var id = CompactIdGenerator.Generate(IdKind.Module);
        Assert.True(IdGenerator.IsCompactId(id));
        Assert.False(IdGenerator.IsLegacyUlid(id));
    }

    [Fact]
    public void IsLegacyUlidTrueFor26CharPayload()
    {
        var id = IdGenerator.Generate(IdKind.Module);
        Assert.True(IdGenerator.IsLegacyUlid(id));
        Assert.False(IdGenerator.IsCompactId(id));
    }

    [Fact]
    public void ExtractIdPortionWorksForBothFormats()
    {
        var compact = CompactIdGenerator.Generate(IdKind.Function);
        var ulid = IdGenerator.Generate(IdKind.Function);

        Assert.Equal(compact.Substring(2), IdGenerator.ExtractIdPortion(compact));
        Assert.Equal(ulid.Substring(2), IdGenerator.ExtractIdPortion(ulid));
    }

    [Fact]
    public void ExtractIdPortionRejectsUnknownPrefix()
    {
        Assert.Null(IdGenerator.ExtractIdPortion("zzz_0123456789ab"));
        Assert.Null(IdGenerator.ExtractIdPortion(""));
    }

    [Fact]
    public void RegisterFromSourceReturnsTrueOnFirstInsert()
    {
        var r = new IdRegistry();
        var bag = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var ok = r.RegisterFromSource(
            "f_aaaaaaaaaaaa", bag, "a.calr", 1, 1, IdKind.Function);

        Assert.True(ok);
        Assert.Empty(bag);
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void RegisterFromSourceEmitsCalor0822OnCollision()
    {
        var r = new IdRegistry();
        var bag = new Calor.Compiler.Diagnostics.DiagnosticBag();
        Assert.True(r.RegisterFromSource(
            "f_aaaaaaaaaaaa", bag, "a.calr", 1, 1, IdKind.Function));

        var ok = r.RegisterFromSource(
            "f_aaaaaaaaaaaa", bag, "b.calr", 7, 3, IdKind.Function);

        Assert.False(ok);
        var diag = Assert.Single(bag);
        Assert.Equal("Calor0822", diag.Code);
        Assert.Equal(Calor.Compiler.Diagnostics.DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("b.calr", diag.FilePath);
        Assert.Contains("a.calr", diag.Message);
    }
}
