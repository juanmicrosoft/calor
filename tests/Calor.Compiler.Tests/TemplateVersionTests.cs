using Xunit;
using Calor.Compiler.Init;

namespace Calor.Compiler.Tests;

public class TemplateVersionTests
{
    [Fact]
    public void ParseTemplateVersion_ValidVersion_ReturnsVersion()
    {
        var section = "<!-- BEGIN CalorC SECTION -->\n<!-- calor-template-version: 1 -->\n## Content";
        Assert.Equal(1, ClaudeInitializer.ParseTemplateVersion(section));
    }

    [Fact]
    public void ParseTemplateVersion_HigherVersion_ReturnsVersion()
    {
        var section = "<!-- calor-template-version: 42 -->";
        Assert.Equal(42, ClaudeInitializer.ParseTemplateVersion(section));
    }

    [Fact]
    public void ParseTemplateVersion_NoMarker_ReturnsZero()
    {
        var section = "<!-- BEGIN CalorC SECTION -->\n## Content\n<!-- END CalorC SECTION -->";
        Assert.Equal(0, ClaudeInitializer.ParseTemplateVersion(section));
    }

    [Fact]
    public void ParseTemplateVersion_MalformedMarker_ReturnsZero()
    {
        var section = "<!-- calor-template-version: abc -->";
        Assert.Equal(0, ClaudeInitializer.ParseTemplateVersion(section));
    }

    [Fact]
    public void CurrentTemplateVersion_IsPositive()
    {
        Assert.True(ClaudeInitializer.CurrentTemplateVersion >= 1);
    }

    [Fact]
    public void Template_ContainsVersionMarker()
    {
        var template = EmbeddedResourceHelper.ReadTemplate("CLAUDE.md.template");
        var version = ClaudeInitializer.ParseTemplateVersion(template);
        Assert.Equal(ClaudeInitializer.CurrentTemplateVersion, version);
    }
}
