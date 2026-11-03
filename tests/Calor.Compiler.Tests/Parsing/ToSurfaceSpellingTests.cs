using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// <see cref="AttributeHelper.ToSurfaceSpelling"/> inverts <see cref="AttributeHelper.ExpandType"/>
/// so diagnostics echo canonical surface syntax, not internal spellings.
/// </summary>
public class ToSurfaceSpellingTests
{
    [Theory]
    [InlineData("int", true, true, "0")]
    [InlineData("FLOAT[bits=32]", true, true, "0f")]
    [InlineData("i64", true, true, "0L")]
    [InlineData("uint", true, true, "0U")]
    [InlineData("ulong", true, true, "0UL")]
    [InlineData("STRING", true, false, "\"\"")]
    [InlineData("dOuBlE", false, false, "default")]
    [InlineData("STR", false, false, "default")]
    public void TypeClassificationAndDefaults_KeepExactNames(
        string type, bool primitive, bool numeric, string defaultValue)
    {
        Assert.Equal(primitive, Migration.TypeMapper.IsPrimitiveType(type));
        Assert.Equal(numeric, Migration.TypeMapper.IsNumericType(type));
        Assert.Equal(defaultValue, Migration.TypeMapper.GetDefaultValueLiteral(type));
    }

    [Theory]
    [InlineData("INT", "int")]
    [InlineData("STRING", "string")]
    [InlineData("BOOL", "bool")]
    [InlineData("FLOAT", "double")]
    [InlineData("FLOAT[bits=32]", "float")]
    [InlineData("INT[bits=8][signed=true]", "sbyte")]
    [InlineData("INT[bits=64][signed=false]", "ulong")]
    [InlineData("CHAR", "char")]
    [InlineData("ARRAY[element=STRING]", "string[]")]
    [InlineData("NULLABLE[inner=INT]", "int?")]
    [InlineData("List<STRING>", "List<string>")]
    [InlineData("Dictionary<STRING, INT>", "Dictionary<string, int>")]
    [InlineData("STRING[]", "string[]")]
    [InlineData("ARRAY[element=NULLABLE[inner=STRING]]", "string?[]")]
    [InlineData("NULLABLE[inner=STRING][]", "string?[]")]
    [InlineData("NULLABLE[inner=STRING[]]", "string[]?")]
    [InlineData("NULLABLE[inner=STRING][]?", "string?[]?")]
    [InlineData("Customer", "Customer")] // unknown/user type passes through
    public void MapsExpandedToSurface(string expanded, string expected)
        => Assert.Equal(expected, AttributeHelper.ToSurfaceSpelling(expanded));

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("string[]")]
    [InlineData("List<string>")]
    [InlineData("string?[]?[,]")]
    [InlineData("MyType<OtherType?>[,][]?")]
    public void IsIdempotentOnAlreadySurfaceForms(string surface)
        => Assert.Equal(surface, AttributeHelper.ToSurfaceSpelling(surface));

    [Fact]
    public void RoundTripsExpandType()
    {
        foreach (var compact in new[] { "int", "sbyte", "ulong", "string", "bool", "double", "float", "string[]", "int?", "List<string>" })
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
        Assert.Equal(csharp, AttributeHelper.ToSurfaceSpelling(expanded));
        Assert.Equal(AttributeHelper.ExpandType(csharp),
            AttributeHelper.ExpandType(AttributeHelper.ToSurfaceSpelling(expanded)));
        Assert.Equal(csharp, Migration.TypeMapper.CalorToCSharp(expanded));
    }

    [Theory]
    [InlineData("int", "INT")]
    [InlineData("long", "INT[bits=64][signed=true]")]
    [InlineData("short", "INT[bits=16][signed=true]")]
    [InlineData("sbyte", "INT[bits=8][signed=true]")]
    [InlineData("byte", "INT[bits=8][signed=false]")]
    [InlineData("ushort", "INT[bits=16][signed=false]")]
    [InlineData("uint", "INT[bits=32][signed=false]")]
    [InlineData("ulong", "INT[bits=64][signed=false]")]
    [InlineData("float", "FLOAT[bits=32]")]
    [InlineData("double", "FLOAT")]
    [InlineData("decimal", "decimal")]
    [InlineData("nint", "nint")]
    [InlineData("nuint", "nuint")]
    [InlineData("string?[]?[,]", "NULLABLE[inner=NULLABLE[inner=STRING][]][,]")]
    [InlineData("MyType<OtherType?>[]", "MyType<NULLABLE[inner=OtherType]>[]")]
    public void MeasuredTypes_ExpandAndEmitWithoutChangingIdentity(string surface, string expanded)
    {
        Assert.Equal(expanded, AttributeHelper.ExpandType(surface));
        Assert.Equal(surface, AttributeHelper.ToSurfaceSpelling(expanded));
        Assert.Equal(surface, Migration.TypeMapper.ToSurfaceType(expanded));
        Assert.Equal(surface, Migration.TypeMapper.CalorToCSharp(expanded));
    }

    [Theory]
    [InlineData("iNt")]
    [InlineData("sTr")]
    [InlineData("dOuBlE")]
    [InlineData("iNt32")]
    [InlineData("INT32")]
    [InlineData("sYsTeM.Int32")]
    [InlineData("MyGeneric<MyTYPE>")]
    public void UnknownTypeCasing_IsNeverFolded(string type)
    {
        Assert.Equal(type, AttributeHelper.ExpandType(type));
        Assert.Equal(type, Migration.TypeMapper.ToSurfaceType(type));
        Assert.Equal(type, Migration.TypeMapper.CalorToCSharp(type));
    }

    [Theory]
    [InlineData("Int32", "int")]
    [InlineData("Int64", "long")]
    [InlineData("Int16", "short")]
    [InlineData("Byte", "byte")]
    [InlineData("SByte", "sbyte")]
    [InlineData("UInt32", "uint")]
    [InlineData("UInt64", "ulong")]
    [InlineData("UInt16", "ushort")]
    [InlineData("Single", "float")]
    [InlineData("Double", "double")]
    [InlineData("Decimal", "decimal")]
    [InlineData("Boolean", "bool")]
    [InlineData("String", "string")]
    [InlineData("Char", "char")]
    [InlineData("Void", "void")]
    [InlineData("Object", "object")]
    public void ClrPrimitiveAliases_UseExplicitCaseSensitiveMappings(string name, string expected)
    {
        foreach (var type in new[] { name, $"System.{name}" })
        {
            Assert.Equal(expected, Migration.TypeMapper.CalorToCSharp(type));
            Assert.Equal(expected, Migration.TypeMapper.ToSurfaceType(type));
        }
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("byte")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("nint")]
    [InlineData("nuint")]
    [InlineData("string?")]
    [InlineData("int?[]")]
    [InlineData("string?[]?[]")]
    [InlineData("int[,][]")]
    [InlineData("int[][,]")]
    [InlineData("List<string?>[]?")]
    [InlineData("MyType<OtherType?>[,][]?")]
    public void MeasuredTypes_ParserFormatterAndCSharpAgree(string type)
    {
        var source = $$"""
            §M{Types}
              §CL{OtherType:pub}
              §CL{MyType<T>:pub}
              §F{Echo:pub} ({{type}}:value) -> {{type}}
                §R value
            """;
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var formatted = new Migration.CalorEmitter().Emit(module);
        Assert.Contains($"({type}:value)", formatted.Replace(", ", ","));
        Assert.Contains($"-> {type}", formatted.Replace(", ", ","));
        var compiled = Program.Compile(formatted, "canonical-types.calr",
            new CompilationOptions { EnforceEffects = false, EnableTypeChecking = true });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Contains($"{type} Echo({type} value)", compiled.GeneratedCode);
    }
}
