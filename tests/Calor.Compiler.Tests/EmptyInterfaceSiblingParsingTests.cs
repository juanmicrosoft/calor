using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #903 cluster 2 (v0.16 W3(a)): an EMPTY interface body — a marker interface —
/// followed by a sibling type at the same column. The lexer emits no Dedent for a
/// same-column line, so the sibling's opener was read as an interface member and
/// rejected with Calor0100 "Expected EXT, METHOD, PROP, IXER, or END_IFACE".
/// Interfaces cannot nest types, so a type opener can only be a sibling; the
/// parser now ends the empty body there. Each case would be red on the parser
/// before this change.
/// </summary>
public class EmptyInterfaceSiblingParsingTests
{
    private static (ModuleNode Module, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        return (parser.Parse(), diagnostics);
    }

    private static void AssertNoErrors(DiagnosticBag diagnostics)
        => Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));

    [Theory]
    [InlineData("§CL{c002:Impl:pub}\n    §IMPL{IMarker}")]
    [InlineData("§IFACE{i002:IOther}\n    §MT{m003:Run} () -> void")]
    [InlineData("§EN{e002:Mode:pub}\n    Fast\n    Slow")]
    [InlineData("§DEL{d002:Handler:pub}\n    §I{i32:x}\n    §O{bool}")]
    [InlineData("§CSHARP{public class Sibling { }}§/CSHARP")]
    public void EmptyInterface_FollowedBySameColumnSibling_Parses(string sibling)
    {
        var source = "§M{m001:Markers}\n  §IFACE{i001:IMarker}\n  " + sibling + "\n";

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var marker = Assert.Single(module.Interfaces, i => i.Name == "IMarker");
        Assert.Empty(marker.Methods);
        Assert.Empty(marker.Properties);
    }

    [Fact]
    public void EmptyInterface_FollowedBySiblingClass_SiblingIsNotNested()
    {
        const string source = """
            §M{m001:Markers}
              §IFACE{i001:IMarker}
              §CL{c002:Impl:pub}
                §IMPL{IMarker}
                §MT{m003:Run:pub} () -> void
                  §R
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        Assert.Single(module.Interfaces);
        var impl = Assert.Single(module.Classes);
        Assert.Equal("Impl", impl.Name);
        Assert.Single(impl.Methods);
    }

    [Fact]
    public void ConsecutiveEmptyInterfaces_EachParsesAsASibling()
    {
        const string source = """
            §M{m001:Markers}
              §IFACE{i001:IA}
              §IFACE{i002:IB:IA}
              §IFACE{i003:IC}
                §PROP{p004:Name:str:pub:get}
              §IFACE{i004:ID}
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        Assert.Equal(4, module.Interfaces.Count);
        Assert.Equal(new[] { "IA", "IB", "IC", "ID" }, module.Interfaces.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { "IA" }, module.Interfaces[1].BaseInterfaces.ToArray());
        Assert.Single(module.Interfaces[2].Properties);
    }

    [Fact]
    public void EmptyInterface_InsideClass_FollowedByNestedSiblingType_Parses()
    {
        const string source = """
            §M{m001:Markers}
              §CL{c001:Outer:pub}
                §IFACE{i002:IInner}
                §CL{c003:Inner:pub}
                  §IMPL{IInner}
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var outer = Assert.Single(module.Classes);
        Assert.Single(outer.NestedInterfaces);
        Assert.Single(outer.NestedClasses);
    }

    /// <summary>
    /// Review m6: <c>§D</c> (record) is in the sibling set too. A record type
    /// declaration has no module-level parse yet — <c>§D</c> at type position is
    /// reported by the MODULE parser — so what this pins is that the interface
    /// body ENDS at the same-column <c>§D</c>: the interface parses empty and the
    /// "Expected EXT, METHOD, PROP, IXER, or END_IFACE" error is gone.
    /// </summary>
    [Fact]
    public void EmptyInterface_FollowedBySameColumnRecord_EndsTheInterfaceBody()
    {
        const string source = """
            §M{m001:Markers}
              §IFACE{i001:IMarker}
              §D{d002:Point}
            """;

        var (module, diagnostics) = Parse(source);

        var marker = Assert.Single(module.Interfaces);
        Assert.Empty(marker.Methods);
        Assert.DoesNotContain(diagnostics.Errors,
            d => d.Message.Contains("EXT, METHOD, PROP, IXER", StringComparison.Ordinal));
    }

    /// <summary>
    /// Review M2: the sibling rule is a SAME-COLUMN rule. An indented type opener
    /// inside an interface body is not a sibling — accepting it would silently
    /// reparent it to the enclosing scope, a nesting the language does not have.
    /// It must still report Calor0100, exactly as before #903 cluster 2.
    /// </summary>
    [Theory]
    [InlineData("    §CL{c002:Nested:pub}")]
    [InlineData("    §IFACE{i002:INested}")]
    [InlineData("    §EN{e002:Nested:pub}\n      Fast")]
    public void EmptyInterface_WithIndentedTypeOpener_IsStillAnError(string indentedOpener)
    {
        var source = "§M{m001:Markers}\n  §IFACE{i001:IMarker}\n" + indentedOpener + "\n";

        var (_, diagnostics) = Parse(source);

        Assert.True(diagnostics.HasErrors,
            "An indented type opener inside an interface body must not be silently reparented.");
        Assert.Contains(diagnostics.Errors, d => d.Code == "Calor0100");
    }

    /// <summary>
    /// The same-column rule holds when the interface is nested one level deeper:
    /// the comparison is against the §IFACE's own column, not a fixed indent.
    /// </summary>
    [Fact]
    public void EmptyInterface_InsideClass_IndentedOpener_IsStillAnError()
    {
        const string source = """
            §M{m001:Markers}
              §CL{c001:Outer:pub}
                §IFACE{i002:IInner}
                  §CL{c003:TooDeep:pub}
            """;

        var (_, diagnostics) = Parse(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == "Calor0100");
    }

    [Fact]
    public void EmptyInterface_AtEndOfModule_StillParses()
    {
        var (module, diagnostics) = Parse("§M{m001:Markers}\n  §IFACE{i001:IMarker}\n");

        AssertNoErrors(diagnostics);
        Assert.Single(module.Interfaces);
    }

    [Fact]
    public void Interface_WithMembers_ThenSibling_MembersStayOnTheInterface()
    {
        const string source = """
            §M{m001:Markers}
              §IFACE{i001:IShape}
                §MT{m002:Area} () -> f64
              §CL{c003:Square:pub}
                §IMPL{IShape}
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var shape = Assert.Single(module.Interfaces);
        Assert.Single(shape.Methods);
        Assert.Single(module.Classes);
    }
}
