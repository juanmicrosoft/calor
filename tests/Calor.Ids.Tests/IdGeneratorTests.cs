using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

public class IdGeneratorTests
{
    [Theory]
    [InlineData(IdKind.Module, "m_")]
    [InlineData(IdKind.Function, "f_")]
    [InlineData(IdKind.Class, "c_")]
    [InlineData(IdKind.Interface, "i_")]
    [InlineData(IdKind.Property, "p_")]
    [InlineData(IdKind.Method, "mt_")]
    [InlineData(IdKind.Constructor, "ctor_")]
    [InlineData(IdKind.Enum, "e_")]
    [InlineData(IdKind.EnumExtension, "ext_")]
    [InlineData(IdKind.OperatorOverload, "op_")]
    [InlineData(IdKind.RefinementType, "rt_")]
    [InlineData(IdKind.ProofObligation, "po_")]
    [InlineData(IdKind.IndexedType, "it_")]
    [InlineData(IdKind.Indexer, "ix_")]
    public void Generate_ReturnsCorrectPrefix(IdKind kind, string expectedPrefix)
    {
        var id = IdGenerator.Generate(kind);

        Assert.StartsWith(expectedPrefix, id);
    }

    [Fact]
    public void Generate_ReturnsUniqueIds()
    {
        var ids = new HashSet<string>();

        for (int i = 0; i < 1000; i++)
        {
            var id = IdGenerator.Generate(IdKind.Function);
            Assert.True(ids.Add(id), $"Duplicate ID generated: {id}");
        }
    }

    [Theory]
    [InlineData(IdKind.Module)]
    [InlineData(IdKind.Function)]
    [InlineData(IdKind.Class)]
    [InlineData(IdKind.Method)]
    [InlineData(IdKind.EnumExtension)]
    [InlineData(IdKind.OperatorOverload)]
    [InlineData(IdKind.RefinementType)]
    [InlineData(IdKind.ProofObligation)]
    [InlineData(IdKind.IndexedType)]
    [InlineData(IdKind.Indexer)]
    public void Generate_ReturnsCompactLength(IdKind kind)
    {
        var id = IdGenerator.Generate(kind);
        var prefix = IdGenerator.GetPrefix(kind);

        // v6: ID is prefix + 12 chars compact payload.
        Assert.Equal(prefix.Length + 12, id.Length);
    }

    [Theory]
    [InlineData(IdKind.Module)]
    [InlineData(IdKind.Function)]
    [InlineData(IdKind.Class)]
    [InlineData(IdKind.Method)]
    public void GenerateUlid_ReturnsLegacyUlidLength(IdKind kind)
    {
        var id = IdGenerator.GenerateUlid(kind);
        var prefix = IdGenerator.GetPrefix(kind);

        // Legacy: prefix + 26 chars ULID.
        Assert.Equal(prefix.Length + 26, id.Length);
    }

    [Fact]
    public void Generate_ProducesCanonicalCompactId()
    {
        var id = IdGenerator.Generate(IdKind.Function);

        Assert.True(IdValidator.IsCanonicalId(id), $"Generated id '{id}' should be canonical.");
        Assert.True(IdValidator.IsCompactId(id), $"Generated id '{id}' should be compact form.");
        Assert.False(IdValidator.IsLegacyUlidId(id), $"Generated id '{id}' should not be legacy ULID form.");
    }

    [Fact]
    public void GenerateUlid_ProducesLegacyUlidId()
    {
        var id = IdGenerator.GenerateUlid(IdKind.Function);

        Assert.True(IdValidator.IsCanonicalId(id), $"Generated id '{id}' should be canonical (legacy form still accepted).");
        Assert.True(IdValidator.IsLegacyUlidId(id), $"Generated id '{id}' should be legacy ULID form.");
        Assert.False(IdValidator.IsCompactId(id), $"Generated id '{id}' should not be compact form.");
    }

    [Theory]
    // v6 compact form (lowercase, 12 chars):
    [InlineData("f_abc123def456", IdKind.Function)]
    [InlineData("m_0123456789ab", IdKind.Module)]
    [InlineData("c_xyzwvtsrqpnm", IdKind.Class)]
    [InlineData("ext_abc123def456", IdKind.EnumExtension)]
    [InlineData("po_abc123def456", IdKind.ProofObligation)]
    [InlineData("it_abc123def456", IdKind.IndexedType)]
    [InlineData("ix_abc123def456", IdKind.Indexer)]
    [InlineData("rt_abc123def456", IdKind.RefinementType)]
    // Legacy ULID form (uppercase, 26 chars):
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Function)]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Module)]
    [InlineData("c_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Class)]
    [InlineData("i_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Interface)]
    [InlineData("p_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Property)]
    [InlineData("mt_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Method)]
    [InlineData("ctor_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Constructor)]
    [InlineData("e_01J5X7K9M2NPQRSTABWXYZ1234", IdKind.Enum)]
    public void GetKindFromId_ReturnsCorrectKind(string id, IdKind expectedKind)
    {
        var kind = IdGenerator.GetKindFromId(id);

        Assert.Equal(expectedKind, kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("x_01J5X7K9M2NPQRSTABWXYZ1234")]
    public void GetKindFromId_ReturnsNullForInvalidId(string? id)
    {
        var kind = IdGenerator.GetKindFromId(id!);

        Assert.Null(kind);
    }

    [Fact]
    public void ExtractPayload_ReturnsCompactPayload()
    {
        var id = "f_abc123def456";

        var payload = IdGenerator.ExtractPayload(id);

        Assert.Equal("abc123def456", payload);
    }

    [Fact]
    public void ExtractPayload_ReturnsUlidPayload()
    {
        var id = "f_01J5X7K9M2NPQRSTABWXYZ1234";

        var payload = IdGenerator.ExtractPayload(id);

        Assert.Equal("01J5X7K9M2NPQRSTABWXYZ1234", payload);
    }

    [Fact]
    public void ExtractUlid_ReturnsUlidPortion()
    {
        var id = "f_01J5X7K9M2NPQRSTABWXYZ1234";

        var ulid = IdGenerator.ExtractUlid(id);

        Assert.Equal("01J5X7K9M2NPQRSTABWXYZ1234", ulid);
    }

    [Fact]
    public void ExtractUlid_ReturnsNullForCompactPayload()
    {
        // Compact payloads are not ULIDs.
        var id = "f_abc123def456";

        var ulid = IdGenerator.ExtractUlid(id);

        Assert.Null(ulid);
    }

    [Fact]
    public void ExtractUlid_ReturnsNullForInvalidId()
    {
        var ulid = IdGenerator.ExtractUlid("invalid");

        Assert.Null(ulid);
    }
}
