using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Native (0.30) declaration headers whose rarer shapes had no test: qualified and
/// generic constraint types, alias-qualified and generic C# attributes, and invalid
/// enum header fields. Each case is written the way the converter or a user writes it.
/// </summary>
public sealed class NativeHeaderEdgeCaseTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        return new Parser(tokens, diagnostics).Parse();
    }

    private static string Errors(DiagnosticBag diagnostics) =>
        string.Join("\n", diagnostics.Where(d => d.IsError).Select(d => $"{d.Code} {d.Message}"));

    [Theory]
    [InlineData("IDictionary<string, List<int>>")]
    [InlineData("IEquatable<int?>")]
    [InlineData("IComparer<int[]>")]
    [InlineData("global::System.IDisposable")]
    [InlineData("System.Collections.Generic.IComparer<System.Collections.Generic.List<T>>")]
    [InlineData("Outer<int>.Inner<string>")]
    public void ConstraintTypeNamesKeepTheirFullCSharpSpelling(string constraint)
    {
        var module = Parse($"""
            module Generics
              class Holder<T>
                where T : {constraint}
                field T:Value
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors, Errors(diagnostics));
        var parsed = Assert.Single(Assert.Single(Assert.Single(module.Classes).TypeParameters).Constraints);
        Assert.Equal(TypeConstraintKind.TypeName, parsed.Kind);
        Assert.Equal(constraint.Replace(" ", ""), parsed.TypeName!.Replace(" ", ""));
    }

    [Fact]
    public void ConstraintWithoutATypeNameIsReported()
    {
        Parse("""
            module Generics
              class Holder<T>
                where T : 42
                field T:Value
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.IsError && d.Message.Contains("constraint type name"));
    }

    [Theory]
    [InlineData("[@JSMarshalAs<JSType.Number>]", "JSMarshalAs<JSType.Number>")]
    [InlineData("[@global::System.Obsolete]", "global::System.Obsolete")]
    public void AttributeNamesMayBeGenericOrAliasQualified(string attribute, string expectedName)
    {
        var module = Parse($"""
            module Attributes
              class Widget {attribute}
                field int:Size
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors, Errors(diagnostics));
        var parsed = Assert.Single(Assert.Single(module.Classes).CSharpAttributes);
        Assert.Equal(expectedName, parsed.Name.Replace(" ", ""));
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("global::System.String", "global::System.String")]
    [InlineData("Holder<int>.Default", "Holder<int>.Default")]
    [InlineData("Access.Level.Admin", "Access.Level.Admin")]
    public void AttributeArgumentsKeepFloatsAndQualifiedNames(string argument, string expected)
    {
        var module = Parse($"""
            module Attributes
              class Widget [@Marker({argument})]
                field int:Size
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors, Errors(diagnostics));
        var parsed = Assert.Single(Assert.Single(module.Classes).CSharpAttributes);
        var value = Assert.Single(parsed.Arguments).Value;
        Assert.Equal(expected, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!.Replace(" ", ""));
    }

    [Fact]
    public void AttributeWithoutANameIsReportedAndSkipped()
    {
        var module = Parse("""
            module Attributes
              class Widget [@42][@Serializable]
                field int:Size
            """, out var diagnostics);

        Assert.Contains(diagnostics, d => d.IsError && d.Message.Contains("attribute name"));
        Assert.Equal("Widget", Assert.Single(module.Classes).Name);
    }

    [Theory]
    [InlineData("enum Color public float", "'float'", "an integral enum underlying type")]
    [InlineData("enum Color float", "'float'", "an integral enum underlying type")]
    // Canonical modifiers after the name are one comma-joined field.
    [InlineData("enum Color publik long", "'publik,long'", "an integral enum underlying type")]
    public void InvalidEnumHeaderFieldsNameTheFieldAndWhatWasExpected(string header, string field, string expected)
    {
        Parse($"""
            module Colors
              {header}
                Red
            """, out var diagnostics);

        var error = Assert.Single(diagnostics, d => d.IsError && d.Message.StartsWith("Invalid header field", StringComparison.Ordinal));
        Assert.True(error.Message.Contains(field, StringComparison.Ordinal), error.Message);
        Assert.True(error.Message.Contains(expected, StringComparison.Ordinal), error.Message);
    }
}
