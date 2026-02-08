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
    public void Generate_ReturnsCorrectLength(IdKind kind)
    {
        var id = IdGenerator.Generate(kind);
        var prefix = IdGenerator.GetPrefix(kind);

        // ID should be prefix + 26 chars ULID
        Assert.Equal(prefix.Length + 26, id.Length);
    }

    [Theory]
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
    public void ExtractUlid_ReturnsUlidPortion()
    {
        var id = "f_01J5X7K9M2NPQRSTABWXYZ1234";

        var ulid = IdGenerator.ExtractUlid(id);

        Assert.Equal("01J5X7K9M2NPQRSTABWXYZ1234", ulid);
    }

    [Fact]
    public void ExtractUlid_ReturnsNullForInvalidId()
    {
        var ulid = IdGenerator.ExtractUlid("invalid");

        Assert.Null(ulid);
    }
}
