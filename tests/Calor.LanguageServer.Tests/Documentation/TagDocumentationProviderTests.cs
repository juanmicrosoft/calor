using Calor.LanguageServer.Documentation;
using Xunit;

namespace Calor.LanguageServer.Tests.Documentation;

public class TagDocumentationProviderTests
{
    [Fact]
    public void Instance_IsLoadedSingleton()
    {
        var provider = TagDocumentationProvider.Instance;
        Assert.Same(provider, TagDocumentationProvider.Instance);
        Assert.True(provider.IsLoaded);
        Assert.NotEmpty(provider.GetAllTags());
        Assert.All(provider.GetAllTags(), tag => Assert.DoesNotContain("§", tag));
    }

    [Theory]
    [InlineData("new", "New Instance")]
    [InlineData("let", "Binding")]
    [InlineData("var", "Binding")]
    [InlineData("func", "Function")]
    [InlineData("class", "Class")]
    [InlineData("for", "Loop")]
    [InlineData("while", "While")]
    [InlineData("if", "If")]
    [InlineData("return", "Return")]
    [InlineData("!", "Effects")]

    [InlineData("=>", "Postcondition")]
    [InlineData("try", "Try")]
    [InlineData("catch", "Catch")]
    [InlineData("await", "Await")]
    [InlineData("property", "Property")]
    [InlineData("field", "Field")]
    [InlineData("ctor", "Constructor")]
    [InlineData("enum", "Enum")]
    [InlineData("interface", "Interface")]
    [InlineData("/// deprecated", "Deprecated")]
    [InlineData("assume", "Assume")]
    public void GetTagDocumentation_NativeSyntax_HasDocumentation(string tag, string expectedNamePart)
    {
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation(tag);
        Assert.NotNull(doc);
        Assert.Equal(tag, doc.Tag);
        Assert.Contains(expectedNamePart, doc.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(doc.Syntax);
        Assert.NotEmpty(doc.Description);
        Assert.NotEmpty(doc.CSharpEquivalent);
        Assert.DoesNotContain("§", doc.Syntax);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("§UNKNOWNTAG")]
    [InlineData("§NEW")]
    [InlineData("§/NEW")]
    [InlineData("§F")]
    [InlineData("§L")]
    [InlineData("§IF")]
    [InlineData("§SW")]
    [InlineData("§ENUM")]
    [InlineData("§WR")]
    [InlineData("§ADD")]
    [InlineData("§VR")]
    [InlineData("§OV")]
    [InlineData("§AB")]
    [InlineData("§SD")]
    [InlineData("§TD")]
    [InlineData("§FX")]
    [InlineData("§HK")]
    [InlineData("§XP")]
    [InlineData("§SB")]
    [InlineData("§LK")]
    [InlineData("§CX")]
    [InlineData("§DC")]
    [InlineData("§CT")]
    [InlineData("§HD")]
    [InlineData("§AU")]
    [InlineData("§US")]
    [InlineData("§T")]
    [InlineData("§DP")]
    [InlineData("§AS")]
    public void GetTagDocumentation_UnknownOrRetiredSyntax_ReturnsNull(string? tag)
    {
        Assert.Null(TagDocumentationProvider.Instance.GetTagDocumentation(tag!));
    }

    [Fact]
    public void ToMarkdown_IncludesAllSections()
    {
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation("new");
        Assert.NotNull(doc);
        var markdown = doc.ToMarkdown();
        Assert.Contains("##", markdown);
        Assert.Contains("```calor", markdown);
        Assert.Contains("```csharp", markdown);
        Assert.Contains("C# equivalent", markdown);
        Assert.Contains("new ClassName", markdown);
    }

    [Theory]
    [InlineData("virtual")]
    [InlineData("override")]
    [InlineData("abstract")]
    public void MethodModifiers_AreDocumentedOnNativeFunctions(string modifier)
    {
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation("func");
        Assert.NotNull(doc);
        Assert.Contains(modifier, doc.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void DeprecatedDocumentation_RetainsObsoleteEquivalent()
    {
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation("/// deprecated");
        Assert.NotNull(doc);
        Assert.Contains("Obsolete", doc.CSharpEquivalent);
    }

    public static IEnumerable<object[]> NativeSyntaxOffsets()
    {
        foreach (var syntax in new[]
        {
            "new", "func", "for", "if", "else", "await", "let", "var",
            "return", "!", "=>", "→", ".."
        })
        {
            for (var offset = 0; offset < syntax.Length; offset++)
                yield return new object[] { syntax + " ", offset, syntax };
        }
    }

    [Theory]
    [MemberData(nameof(NativeSyntaxOffsets))]
    public void ExtractTagAtPosition_UsesExactNativeToken(string source, int offset, string expected)
    {
        Assert.Equal(expected, TagDocumentationProvider.ExtractTagAtPosition(source, offset));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("new User()", -1)]
    [InlineData("new User()", 100)]
    [InlineData("new User()", 3)]
    [InlineData("new User()", 5)]
    [InlineData("newer", 1)]
    [InlineData("@new", 1)]
    [InlineData("\"new\"", 2)]
    [InlineData("'f'", 1)]
    [InlineData("// new", 4)]
    [InlineData("/// since 1.0.0", 5)]
    [InlineData("§NEW{User}§/NEW", 1)]
    [InlineData("§NEW{User}§/NEW", 11)]
    [InlineData("§F{Add:pub}", 1)]
    [InlineData("§L{i:0..10}", 1)]
    [InlineData("§IF true", 1)]
    [InlineData("§EI false", 1)]
    [InlineData("§EL", 1)]
    [InlineData("§!{Run}", 1)]
    [InlineData("§??{default}", 2)]
    [InlineData("§?{value}", 1)]
    public void ExtractTagAtPosition_DoesNotGuessSyntax(string? source, int offset)
    {
        Assert.Null(TagDocumentationProvider.ExtractTagAtPosition(source!, offset));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ExtractTagAtPosition_MultilineSourceRetainsOffsets(string newline)
    {
        var source = "module Demo" + newline + "  func Add () → int" + newline + "    return 1";
        var start = source.IndexOf("func", StringComparison.Ordinal);
        Assert.Equal("func", TagDocumentationProvider.ExtractTagAtPosition(source, start + 2));
        Assert.Null(TagDocumentationProvider.ExtractTagAtPosition(source, start + 4));
    }

    [Fact]
    public void ExtractTagAtPosition_RawPayloadRemainsOpaque()
    {
        const string payload = "new func if";
        var source = $"raw stmt {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        foreach (var syntax in new[] { "new", "func", "if" })
            Assert.Null(TagDocumentationProvider.ExtractTagAtPosition(source,
                source.IndexOf(syntax, StringComparison.Ordinal)));
    }
}
