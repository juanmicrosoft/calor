using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class HoverHandlerTests
{
    [Fact]
    public void Hover_Function_ReturnsSymbolInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:/*cursor*/Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("function", result.Kind);
    }

    [Fact]
    public void Hover_Parameter_ReturnsTypeInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §I{i32:/*cursor*/value}
            §O{i32}
            §R value
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("value", result.Name);
        Assert.Equal("parameter", result.Kind);
        Assert.Equal("INT", result.Type);
    }

    [Fact]
    public void Hover_Class_ReturnsClassInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:/*cursor*/Person}
            §FLD{str:name}
            §FLD{i32:age}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Person", result.Name);
        Assert.Equal("class", result.Kind);
    }

    [Fact]
    public void Hover_Field_ReturnsFieldInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:/*cursor*/name}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("name", result.Name);
        Assert.Equal("field", result.Kind);
    }

    [Fact]
    public void Hover_Method_ReturnsMethodInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:/*cursor*/Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("method", result.Kind);
    }

    [Fact]
    public void Hover_Interface_ReturnsInterfaceInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §IFACE{i001:/*cursor*/IShape}
            §MT{m001:GetArea}
            §O{f64}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("IShape", result.Name);
        Assert.Equal("interface", result.Kind);
    }

    [Fact]
    public void Hover_Enum_ReturnsEnumInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §EN{e001:/*cursor*/Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Color", result.Name);
        Assert.Equal("enum", result.Kind);
    }

    [Fact]
    public void Hover_EnumMember_ReturnsMemberInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{/*cursor*/Red}
            §/EN{e001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Red", result.Name);
        Assert.Equal("enum member", result.Kind);
    }

    [Fact]
    public void Hover_LocalVariable_ReturnsVariableInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:TestModule}
            §F{f001:Test}
            §B{/*cursor*/x:i32} 42
            §R x
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("x", result.Name);
        Assert.Contains("variable", result.Kind);
    }

    [Fact]
    public void Hover_Module_ReturnsModuleInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{m001:/*cursor*/TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("TestModule", result.Name);
        Assert.Equal("module", result.Kind);
    }
}
