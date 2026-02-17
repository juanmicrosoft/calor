using Calor.LanguageServer.Documentation;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class HoverHandlerTests
{
    private readonly WorkspaceState _workspace = new();
    private readonly HoverHandler _handler;

    public HoverHandlerTests()
    {
        _handler = new HoverHandler(_workspace);
    }
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

    #region Tag Documentation Hover Tests

    [Fact]
    public async Task Handle_TagHover_NEW_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§NEW{User}(name)§/NEW";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test_new.calr");
        var doc = _workspace.GetOrCreate(uri, source);
        Assert.NotNull(doc); // Verify document was created

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1) // On 'N' of §NEW
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("New Instance", content);
    }

    [Fact]
    public async Task Handle_TagHover_F_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§F{f001:Test}§/F";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1) // On 'F' of §F
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Function", content);
    }

    [Fact]
    public async Task Handle_TagHover_L_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§L{i:0..10} body §/L";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Loop", content);
    }

    [Fact]
    public async Task Handle_TagHover_IF_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§IF{x > 0} true §/IF";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 2) // On 'I' of §IF
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("If", content);
    }

    [Fact]
    public async Task Handle_TagHover_ClosingTag_ReturnsOpeningTagDocAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§NEW{User}()§/NEW";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 14) // On '/' of §/NEW
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("New Instance", content);
    }

    [Fact]
    public async Task Handle_TagHover_AWAIT_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§AWAIT expr §/AWAIT";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 3) // On 'A' of §AWAIT
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Await", content);
    }

    [Fact]
    public async Task Handle_TagHover_TR_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§TR{} body §CA{} handler §/TR";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 2)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Try", content);
    }

    [Fact]
    public async Task Handle_TagHover_E_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§E{cw,fs:r}";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Effects", content);
    }

    [Fact]
    public async Task Handle_TagHover_Q_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§Q (x > 0)";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Precondition", content);
    }

    [Fact]
    public async Task Handle_TagHover_S_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§S ($result > 0)";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Postcondition", content);
    }

    [Fact]
    public async Task Handle_NoTagOrSymbol_ReturnsNullAsync()
    {
        // Use whitespace-only content where no symbol or tag should be found
        var source = "   ";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test_whitespace.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1) // In the middle of whitespace
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_UnknownDocument_ReturnsNullAsync()
    {
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///unknown.calr")),
            Position = new Position(0, 0)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TagHover_IncludesCSharpEquivalentAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§NEW{User}(name)§/NEW";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("C# equivalent", content);
        Assert.Contains("csharp", content); // Code block
    }

    [Fact]
    public async Task Handle_TagHover_IncludesSyntaxExampleAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "§L{i:0..10}§/L";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("```calor", content); // Has syntax example
    }

    #endregion
}
