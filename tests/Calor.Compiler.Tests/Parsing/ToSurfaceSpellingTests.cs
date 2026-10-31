using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// <see cref="AttributeHelper.ToSurfaceSpelling"/> inverts <see cref="AttributeHelper.ExpandType"/>
/// so diagnostics echo the compact syntax agents write, not internal spellings (#739/#741).
/// </summary>
public class ToSurfaceSpellingTests
{
    [Theory]
    [InlineData("INT", "i32")]
    [InlineData("STRING", "str")]
    [InlineData("BOOL", "bool")]
    [InlineData("FLOAT", "f64")]
    [InlineData("FLOAT[bits=32]", "f32")]
    [InlineData("INT[bits=8][signed=true]", "i8")]
    [InlineData("INT[bits=64][signed=false]", "u64")]
    [InlineData("CHAR", "char")]
    [InlineData("ARRAY[element=STRING]", "[str]")]
    [InlineData("NULLABLE[inner=INT]", "?i32")]
    [InlineData("List<STRING>", "List<str>")]
    [InlineData("Dictionary<STRING, INT>", "Dictionary<str, i32>")]
    [InlineData("STRING[]", "str[]")]
    [InlineData("Customer", "Customer")] // unknown/user type passes through
    public void MapsExpandedToSurface(string expanded, string expected)
        => Assert.Equal(expected, AttributeHelper.ToSurfaceSpelling(expanded));

    [Theory]
    [InlineData("i32")]
    [InlineData("str")]
    [InlineData("[str]")]
    [InlineData("List<str>")]
    public void IsIdempotentOnAlreadySurfaceForms(string surface)
        => Assert.Equal(surface, AttributeHelper.ToSurfaceSpelling(surface));

    [Fact]
    public void RoundTripsExpandType()
    {
        foreach (var compact in new[] { "i32", "i8", "u64", "str", "bool", "f64", "f32", "[str]", "?i32", "List<str>" })
        {
            Assert.Equal(compact, AttributeHelper.ToSurfaceSpelling(AttributeHelper.ExpandType(compact)));
        }
    }

    [Theory]
    [InlineData(
        "System.Func<i32, i32[,]>",
        "System.Func<INT, INT[,]>",
        "System.Func<int, int[,]>")]
    [InlineData(
        "System.Func<(int, string), i32[,]>",
        "System.Func<(int, string), INT[,]>",
        "System.Func<(int, string), int[,]>")]
    [InlineData(
        "System.Tuple<System.Func<i32[,], i32[,,]>, str[]>",
        "System.Tuple<System.Func<INT[,], INT[,,]>, STRING[]>",
        "System.Tuple<System.Func<int[,], int[,,]>, string[]>")]
    [InlineData(
        "System.Collections.Generic.Dictionary<str, i64[,][]>",
        "System.Collections.Generic.Dictionary<STRING, INT[bits=64][signed=true][,][]>",
        "System.Collections.Generic.Dictionary<string, long[,][]>")]
    [InlineData(
        "System.Func<[i32], i32[,]>",
        "System.Func<ARRAY[element=INT], INT[,]>",
        "System.Func<int[], int[,]>")]
    [InlineData(
        "System.Func<i32[,], (int, string)>",
        "System.Func<INT[,], (int, string)>",
        "System.Func<int[,], (int, string)>")]
    public void GenericArguments_PreserveNestedCommasAcrossExpansionAndSurfaceRoundTrip(
        string surface,
        string expanded,
        string csharp)
    {
        Assert.Equal(expanded, AttributeHelper.ExpandType(surface));
        Assert.Equal(surface, AttributeHelper.ToSurfaceSpelling(expanded));
        Assert.Equal(expanded,
            AttributeHelper.ExpandType(AttributeHelper.ToSurfaceSpelling(expanded)));
        Assert.Equal(csharp, Migration.TypeMapper.CalorToCSharp(expanded));
    }
}
