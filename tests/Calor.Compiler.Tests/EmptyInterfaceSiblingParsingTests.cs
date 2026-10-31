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
    [InlineData("§CL{Impl:pub}\n    §IMPL{IMarker}")]
    [InlineData("§IFACE{IOther}\n    §MT{Run} () -> void")]
    [InlineData("§EN{Mode:pub}\n    Fast\n    Slow")]
    [InlineData("§DEL{Handler:pub}\n    §I{i32:x}\n    §O{bool}")]
    [InlineData("§CSHARP{public class Sibling { }}§/CSHARP")]
    public void EmptyInterface_FollowedBySameColumnSibling_Parses(string sibling)
    {
        var source = "§M{Markers}\n  §IFACE{IMarker}\n  " + sibling + "\n";

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
            §M{Markers}
              §IFACE{IMarker}
              §CL{Impl:pub}
                §IMPL{IMarker}
                §MT{Run:pub} () -> void
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
            §M{Markers}
              §IFACE{IA}
              §IFACE{IB:IA}
              §IFACE{IC}
                §PROP{Name:str:pub:get}
              §IFACE{ID}
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
            §M{Markers}
              §CL{Outer:pub}
                §IFACE{IInner}
                §CL{Inner:pub}
                  §IMPL{IInner}
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var outer = Assert.Single(module.Classes);
        Assert.Single(outer.NestedInterfaces);
        Assert.Single(outer.NestedClasses);
    }

    /// <summary>
    /// Review M2: the sibling rule is a SAME-COLUMN rule. An indented type opener
    /// inside an interface body is not a sibling — accepting it would silently
    /// reparent it to the enclosing scope, a nesting the language does not have.
    /// It must still report Calor0100, exactly as before #903 cluster 2.
    /// </summary>
    [Theory]
    [InlineData("    §CL{Nested:pub}")]
    [InlineData("    §IFACE{INested}")]
    [InlineData("    §EN{Nested:pub}\n      Fast")]
    public void EmptyInterface_WithIndentedTypeOpener_IsStillAnError(string indentedOpener)
    {
        var source = "§M{Markers}\n  §IFACE{IMarker}\n" + indentedOpener + "\n";

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
            §M{Markers}
              §CL{Outer:pub}
                §IFACE{IInner}
                  §CL{TooDeep:pub}
            """;

        var (_, diagnostics) = Parse(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == "Calor0100");
    }

    [Fact]
    public void EmptyInterface_AtEndOfModule_StillParses()
    {
        var (module, diagnostics) = Parse("§M{Markers}\n  §IFACE{IMarker}\n");

        AssertNoErrors(diagnostics);
        Assert.Single(module.Interfaces);
    }

    [Fact]
    public void Interface_WithMembers_ThenSibling_MembersStayOnTheInterface()
    {
        const string source = """
            §M{Markers}
              §IFACE{IShape}
                §MT{Area} () -> f64
              §CL{Square:pub}
                §IMPL{IShape}
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var shape = Assert.Single(module.Interfaces);
        Assert.Single(shape.Methods);
        Assert.Single(module.Classes);
    }
}
