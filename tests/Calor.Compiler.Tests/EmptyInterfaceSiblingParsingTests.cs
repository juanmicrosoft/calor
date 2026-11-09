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
    [InlineData("class Impl public\n    implements IMarker")]
    [InlineData("interface IOther\n    func Run () → void")]
    [InlineData("enum Mode public\n    Fast\n    Slow")]
    [InlineData("delegate Handler (int:x) → bool")]
    [InlineData("raw member 24\npublic class Sibling { }\n")]
    public void EmptyInterface_FollowedBySameColumnSibling_Parses(string sibling)
    {
        var source = "module Markers\n  interface IMarker\n  " + sibling + "\n";

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var marker = Assert.Single(module.Interfaces, i => i.Name == "IMarker");
        Assert.Empty(marker.Methods);
        Assert.Empty(marker.Properties);
        if (sibling.StartsWith("raw ", StringComparison.Ordinal))
            Assert.Equal("public class Sibling { }", Assert.Single(module.InteropBlocks).CSharpCode);
    }

    [Fact]
    public void EmptyInterface_FollowedBySiblingClass_SiblingIsNotNested()
    {
        const string source = """
            module Markers
              interface IMarker
              class Impl public
                implements IMarker
                func Run public () → void
                  return
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
            module Markers
              interface IA
              interface IB
                extends IA
              interface IC
                property string:Name public
                  get
              interface ID
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
            module Markers
              class Outer public
                interface IInner
                class Inner public
                  implements IInner
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
    [InlineData("    class Nested public")]
    [InlineData("    interface INested")]
    [InlineData("    enum Nested public\n      Fast")]
    public void EmptyInterface_WithIndentedTypeOpener_IsStillAnError(string indentedOpener)
    {
        var source = "module Markers\n  interface IMarker\n" + indentedOpener + "\n";

        var (_, diagnostics) = Parse(source);

        Assert.True(diagnostics.HasErrors,
            "An indented type opener inside an interface body must not be silently reparented.");
        Assert.Contains(diagnostics.Errors, d => d.Code == "Calor0100");
        Assert.DoesNotContain(diagnostics.Errors, d => d.Code is "Calor0003" or "Calor0006" or "Calor0007");
    }

    /// <summary>
    /// The same-column rule holds when the interface is nested one level deeper:
    /// the comparison is against the §IFACE's own column, not a fixed indent.
    /// </summary>
    [Fact]
    public void EmptyInterface_InsideClass_IndentedOpener_IsStillAnError()
    {
        const string source = """
            module Markers
              class Outer public
                interface IInner
                  class TooDeep public
            """;

        var (_, diagnostics) = Parse(source);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, d => d.Code == "Calor0100");
        Assert.DoesNotContain(diagnostics.Errors, d => d.Code is "Calor0003" or "Calor0006" or "Calor0007");
    }

    [Fact]
    public void EmptyInterface_AtEndOfModule_StillParses()
    {
        var (module, diagnostics) = Parse("module Markers\n  interface IMarker\n");

        AssertNoErrors(diagnostics);
        Assert.Single(module.Interfaces);
    }

    [Fact]
    public void Interface_WithMembers_ThenSibling_MembersStayOnTheInterface()
    {
        const string source = """
            module Markers
              interface IShape
                func Area () → double
              class Square public
                implements IShape
            """;

        var (module, diagnostics) = Parse(source);

        AssertNoErrors(diagnostics);
        var shape = Assert.Single(module.Interfaces);
        Assert.Single(shape.Methods);
        Assert.Single(module.Classes);
    }
}
