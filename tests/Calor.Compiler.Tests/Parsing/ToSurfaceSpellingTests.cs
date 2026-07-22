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
    [InlineData("OPTION[inner=INT]", "?i32")]
    [InlineData("RESULT[ok=INT][err=STRING]", "i32!str")]
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
}
