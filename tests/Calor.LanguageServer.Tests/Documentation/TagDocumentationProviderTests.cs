using Calor.LanguageServer.Documentation;
using Xunit;

namespace Calor.LanguageServer.Tests.Documentation;

public class TagDocumentationProviderTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        var instance1 = TagDocumentationProvider.Instance;
        var instance2 = TagDocumentationProvider.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void IsLoaded_ReturnsTrue_WhenDocumentationAvailable()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.True(provider.IsLoaded);
        Assert.NotEmpty(provider.GetAllTags());
    }

    [Fact]
    public void GetTagDocumentation_NEW_ReturnsDocumentation()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return; // Skip if docs not loaded

        var doc = provider.GetTagDocumentation("§NEW");

        Assert.NotNull(doc);
        Assert.Equal("§NEW", doc.Tag);
        Assert.Contains("instance", doc.Description.ToLower());
        Assert.NotEmpty(doc.Syntax);
        Assert.NotEmpty(doc.CSharpEquivalent);
    }

    [Fact]
    public void GetTagDocumentation_F_ReturnsDocumentation()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§F");

        Assert.NotNull(doc);
        Assert.Equal("§F", doc.Tag);
        Assert.Contains("function", doc.Description.ToLower());
    }

    [Fact]
    public void GetTagDocumentation_L_ReturnsDocumentation()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§L");

        Assert.NotNull(doc);
        Assert.Equal("§L", doc.Tag);
        Assert.Contains("loop", doc.Description.ToLower());
    }

    [Fact]
    public void GetTagDocumentation_IF_ReturnsDocumentation()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§IF");

        Assert.NotNull(doc);
        Assert.Equal("§IF", doc.Tag);
    }

    [Fact]
    public void GetTagDocumentation_ClosingTag_ReturnsOpeningTagDoc()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§/NEW");

        Assert.NotNull(doc);
        Assert.Equal("§NEW", doc.Tag);
    }

    [Fact]
    public void GetTagDocumentation_UnknownTag_ReturnsNull()
    {
        var provider = TagDocumentationProvider.Instance;

        var doc = provider.GetTagDocumentation("§UNKNOWNTAG");

        Assert.Null(doc);
    }

    [Theory]
    [InlineData("§SW")]
    [InlineData("§ENUM")]
    [InlineData("§WR")]
    [InlineData("§ADD")]
    public void GetTagDocumentation_RemovedAlias_ReturnsNull(string tag)
    {
        Assert.Null(TagDocumentationProvider.Instance.GetTagDocumentation(tag));
    }

    [Fact]
    public void GetTagDocumentation_NullOrEmpty_ReturnsNull()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.Null(provider.GetTagDocumentation(null!));
        Assert.Null(provider.GetTagDocumentation(""));
    }

    [Fact]
    public void ToMarkdown_IncludesAllSections()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§NEW");
        Assert.NotNull(doc);

        var markdown = doc.ToMarkdown();

        Assert.Contains("##", markdown); // Has header
        Assert.Contains("```calor", markdown); // Has syntax block
        Assert.Contains("```csharp", markdown); // Has C# equivalent
        Assert.Contains("C# equivalent", markdown);
    }

    #region ExtractTagAtPosition Tests

    [Fact]
    public void ExtractTagAtPosition_AtTagStart_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 0);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_AtCharIndex1_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        // Index 1 should be 'N' of NEW
        Assert.Equal('N', text[1]);
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 1);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_AtCharIndex2_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        // Index 2 should be 'E' of NEW
        Assert.Equal('E', text[2]);
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 2);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_AtCharIndex3_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        // Index 3 should be 'W' of NEW
        Assert.Equal('W', text[3]);
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 3);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_InTagName_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 2);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_AfterTagName_ReturnsTag()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 3);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_ClosingTag_ReturnsOpeningTag()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 13);

        Assert.Equal("§NEW", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_FunctionTag_ReturnsTag()
    {
        var text = "§F{Add:pub}";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 1);

        Assert.Equal("§F", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_LoopTag_ReturnsTag()
    {
        var text = "§L{i:0..10} body §/L";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 0);

        Assert.Equal("§L", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_IfTag_ReturnsTag()
    {
        var text = "§IF §EI{x < 0} §EL §/IF";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 0);

        Assert.Equal("§IF", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_ElseIfTag_ReturnsTag()
    {
        var text = "§IF §EI{x < 0} §EL §/IF";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, text.IndexOf("§EI", StringComparison.Ordinal) + 1);

        Assert.Equal("§EI", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_ElseTag_ReturnsTag()
    {
        var text = "§IF §EI{x < 0} §EL §/IF";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, text.IndexOf("§EL", StringComparison.Ordinal) + 1);

        Assert.Equal("§EL", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_NotOnTag_ReturnsNull()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 5); // On 'User'

        Assert.Null(tag);
    }

    [Fact]
    public void ExtractTagAtPosition_EmptyText_ReturnsNull()
    {
        var tag = TagDocumentationProvider.ExtractTagAtPosition("", 0);

        Assert.Null(tag);
    }

    [Fact]
    public void ExtractTagAtPosition_NullText_ReturnsNull()
    {
        var tag = TagDocumentationProvider.ExtractTagAtPosition(null!, 0);

        Assert.Null(tag);
    }

    [Fact]
    public void ExtractTagAtPosition_NegativeOffset_ReturnsNull()
    {
        var text = "§NEW{User}()§/NEW";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, -1);

        Assert.Null(tag);
    }

    [Fact]
    public void ExtractTagAtPosition_OffsetBeyondEnd_ReturnsNull()
    {
        var text = "§NEW{User}";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 100);

        Assert.Null(tag);
    }

    [Fact]
    public void ExtractTagAtPosition_ExternalCallTag_ReturnsTag()
    {
        var text = "§!{Console.WriteLine}(msg)§/!";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 1);

        Assert.Equal("§!", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_NullCoalescing_ReturnsTag()
    {
        var text = "x §??{default}";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 3);

        Assert.Equal("§??", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_NullConditional_ReturnsTag()
    {
        var text = "§?{user.Name}§/?";
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, 1);

        Assert.Equal("§?", tag);
    }

    [Fact]
    public void ExtractTagAtPosition_MultiLineSource_FindsTag()
    {
        var text = """
            §M{Test}
            §F{Add}
            §I{i32:a}
            """;
        // Position at §F
        var fIndex = text.IndexOf("§F");
        var tag = TagDocumentationProvider.ExtractTagAtPosition(text, fIndex);

        Assert.Equal("§F", tag);
    }

    #endregion

    #region Tag Coverage Tests

    [Theory]
    [InlineData("§NEW", "New Instance")]
    [InlineData("§B", "Binding")]
    [InlineData("§F", "Function")]
    [InlineData("§MT", "Method")]
    [InlineData("§CL", "Class")]
    [InlineData("§L", "Loop")]
    [InlineData("§WH", "While")]
    [InlineData("§IF", "If")]
    [InlineData("§R", "Return")]
    [InlineData("§E", "Effects")]
    [InlineData("§Q", "Precondition")]
    [InlineData("§S", "Postcondition")]
    [InlineData("§TR", "Try")]
    [InlineData("§CA", "Catch")]
    [InlineData("§AWAIT", "Await")]
    [InlineData("§PROP", "Property")]
    [InlineData("§FLD", "Field")]
    [InlineData("§CTOR", "Constructor")]
    [InlineData("§EN", "Enum")]
    [InlineData("§IFACE", "Interface")]
    [InlineData("§LAM", "Lambda")]
    public void GetTagDocumentation_CommonTags_HaveDocumentation(string tag, string expectedNamePart)
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation(tag);

        Assert.NotNull(doc);
        Assert.Contains(expectedNamePart, doc.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
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
    public void GetTagDocumentation_RetiredMetadataAndModifierTags_AreNotTaught(string tag)
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.True(provider.IsLoaded);
        Assert.Null(provider.GetTagDocumentation(tag));
    }

    [Theory]
    [InlineData("§DP", "Deprecated")]
    [InlineData("§AS", "Assume")]
    public void GetTagDocumentation_CurrentAnnotationTags_HaveDocumentation(string tag, string expectedNamePart)
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation(tag);

        Assert.NotNull(doc);
        Assert.Contains(expectedNamePart, doc.Name, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Modifier Tag Tests

    [Fact]
    public void GetTagDocumentation_VirtualModifier_IsDocumentedOnMethods()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.Null(provider.GetTagDocumentation("§VR"));
        var doc = provider.GetTagDocumentation("§MT");

        Assert.NotNull(doc);
        Assert.Contains("virt", doc.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("virtual", doc.CSharpEquivalent, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTagDocumentation_OverrideModifier_IsDocumentedOnMethods()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.Null(provider.GetTagDocumentation("§OV"));
        var doc = provider.GetTagDocumentation("§MT");

        Assert.NotNull(doc);
        Assert.Contains("ov", doc.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("override", doc.CSharpEquivalent, StringComparison.Ordinal);
    }

    [Fact]
    public void GetTagDocumentation_AbstractModifier_IsDocumentedOnMethods()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.Null(provider.GetTagDocumentation("§AB"));
        var doc = provider.GetTagDocumentation("§MT");

        Assert.NotNull(doc);
        Assert.Contains("abs", doc.Description, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Documentation Comment Tag Tests

    [Fact]
    public void GetTagDocumentation_RetiredTodoTag_IsNotTaughtAsSyntax()
    {
        var provider = TagDocumentationProvider.Instance;

        Assert.True(provider.IsLoaded);
        Assert.Null(provider.GetTagDocumentation("§TD"));
    }

    [Fact]
    public void GetTagDocumentation_DeprecatedTag_HasCSharpEquivalent()
    {
        var provider = TagDocumentationProvider.Instance;
        if (!provider.IsLoaded) return;

        var doc = provider.GetTagDocumentation("§DP");

        Assert.NotNull(doc);
        Assert.Contains("Obsolete", doc.CSharpEquivalent);
    }

    #endregion
}
