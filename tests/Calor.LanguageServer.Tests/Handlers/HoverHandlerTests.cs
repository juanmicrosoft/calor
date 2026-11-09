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
    [Fact]
    public async Task RawFraming_HeaderHoverAndOpaquePayloadDoNotLeakCalorAssistanceAsync()
    {
        const string payload = "// \u03c0\U0001f600 §F{Fake}\r\n// §C{Run} §A value";
        var source = "§M{Test}\n  §F{Run:pub} (i32:value) -> i32\n    §R value\n" +
            $"  raw member {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n" +
            "  §F{After:pub} () -> void\n";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///raw-hover.calr");
        var state = _workspace.GetOrCreate(uri, source, version: 1);
        Assert.NotNull(state.Snapshot.Ast);
        var header = source.IndexOf("raw ", StringComparison.Ordinal);
        var (headerLine, headerColumn) = LspTestHarness.GetLineColumn(source, header);
        var headerPosition = new Position(headerLine - 1, headerColumn - 1);
        var hover = HoverHandler.BuildHover(state.Snapshot, headerPosition);
        Assert.NotNull(hover);
        Assert.Contains("UTF-8", hover.Contents.MarkupContent!.Value);
        var completion = new CompletionHandler(_workspace);
        var signature = new SignatureHelpHandler(_workspace);
        foreach (var marker in new[] { "§F{Fake}", "§C{Run}" })
        {
            var (line, column) = LspTestHarness.GetLineColumn(source, source.IndexOf(marker, StringComparison.Ordinal));
            var position = new Position(line - 1, column - 1);
            Assert.Null(HoverHandler.BuildHover(state.Snapshot, position));
            var items = await completion.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = position
            }, CancellationToken.None);
            Assert.Empty(items.Items);
            Assert.Null(await signature.Handle(new SignatureHelpParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = position
            }, CancellationToken.None));
        }
        var (endLine, endColumn) = LspTestHarness.GetLineColumn(source, source.Length);
        Assert.Null(await signature.Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(endLine - 1, endColumn - 1)
        }, CancellationToken.None));
    }

    [Fact]
    public void PackagedTeachingMetadata_MatchesEveryHoverEntry()
    {
        var provider = TagDocumentationProvider.Instance;
        Assert.True(provider.IsLoaded);
        using var source = System.Text.Json.JsonDocument.Parse(
            Calor.Compiler.Init.EmbeddedResourceHelper.ReadResource(
                "Calor.Compiler.Resources.calor-syntax-documentation.json"));
        var tags = source.RootElement.GetProperty("tags");
        Assert.Equal(tags.EnumerateObject().Select(tag => tag.Name).Order(),
            provider.GetAllTags().Order());
        foreach (var tag in tags.EnumerateObject())
        {
            var hover = provider.GetTagDocumentation(tag.Name);
            Assert.NotNull(hover);
            Assert.Equal(tag.Value.GetProperty("syntax").GetString(), hover.Syntax);
            Assert.Equal(tag.Value.GetProperty("description").GetString(), hover.Description);
            Assert.Equal(tag.Value.GetProperty("csharpEquivalent").GetString(), hover.CSharpEquivalent);
            Assert.False(string.IsNullOrWhiteSpace(hover.Name));
        }
        Assert.Contains("§ASSIGN", provider.GetTagDocumentation("§ASSIGN")!.Syntax);
        Assert.Contains("§ARR{type}", provider.GetTagDocumentation("§ARR")!.Syntax);
        Assert.Null(provider.GetTagDocumentation("§D"));
    }

    private readonly WorkspaceState _workspace = new();
    private readonly HoverHandler _handler;

    public HoverHandlerTests()
    {
        _handler = new HoverHandler(_workspace);
    }

    [Fact]
    public void BuildHover_UsesOneCapturedAnalysisSnapshot()
    {
        const string original = """
            §M{TestModule}
              §F{Original:pub} () -> i32
                §R 1
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
            "file:///snapshot-hover.calr");
        var state = _workspace.GetOrCreate(uri, original, version: 1);
        var captured = state.Snapshot;
        var offset = original.IndexOf("Original", StringComparison.Ordinal);
        var (line, column) = LspTestHarness.GetLineColumn(original, offset);
        _workspace.Update(
            uri,
            "§M{Changed}\n  §F{New:pub} () -> i32\n    §R 2\n",
            version: 2);

        var hover = HoverHandler.BuildHover(
            captured,
            new Position(line - 1, column - 1));

        Assert.NotNull(hover);
        var markup = Assert.IsType<MarkupContent>(hover.Contents.MarkupContent);
        Assert.Contains("Original", markup.Value, StringComparison.Ordinal);
        Assert.Equal(1, captured.Version);
        Assert.Equal(2, state.Version);
    }

    [Fact]
    public void Hover_Function_ReturnsSymbolInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{/*cursor*/Add}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R a + b
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
            §M{TestModule}
              §F{Test}
                §I{i32:/*cursor*/value}
                §O{i32}
                §R value
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
            §M{TestModule}
              §CL{/*cursor*/Person}
                §FLD{str:name}
                §FLD{i32:age}
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
            §M{TestModule}
              §CL{Person}
                §FLD{str:/*cursor*/name}
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
            §M{TestModule}
              §CL{Calculator}
                §MT{/*cursor*/Add}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R a + b
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
            §M{TestModule}
              §IFACE{/*cursor*/IShape}
                §MT{GetArea}
                  §O{f64}
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
            §M{TestModule}
              §EN{/*cursor*/Color}
              §EM{Red}
              §EM{Green}
              §EM{Blue}
              §/EN
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
            §M{TestModule}
              §EN{Color}
              §EM{/*cursor*/Red}
              §/EN
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
            §M{TestModule}
              §F{Test}
                §B{/*cursor*/x:i32} 42
                §R x
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
            §M{/*cursor*/TestModule}
              §F{Test}
                §R 0
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

        var source = "§F{Test}§/F";
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

        var source = "§IF true §/IF";
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

        var source = "§TR body §CA{} handler §/TR";
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
