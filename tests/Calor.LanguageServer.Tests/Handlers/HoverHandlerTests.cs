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
        var source = "module Test\n  func Run (int:value) → int\n    return value\n" +
            $"  raw member {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n" +
            "  func After () → void\n";
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
        Assert.Contains("set target expression", provider.GetTagDocumentation("set")!.Syntax);
        Assert.Contains("this[int:index]", provider.GetTagDocumentation("this")!.Syntax);
        Assert.Null(provider.GetTagDocumentation("§ASSIGN"));
        Assert.Null(provider.GetTagDocumentation("§ARR"));
        Assert.Null(provider.GetTagDocumentation("§D"));
    }

    private readonly WorkspaceState _workspace = new();
    private readonly HoverHandler _handler;

    public HoverHandlerTests()
    {
        _handler = new HoverHandler(_workspace);
    }

    [Theory]
    [InlineData("module Demo", "module")]
    [InlineData("func Add () → int", "func")]
    [InlineData("class Person", "class")]
    [InlineData("let int:value = 1", "let")]
    [InlineData("var int:value = 1", "var")]
    [InlineData("for i from 0 to 2 step 1", "for")]
    [InlineData("each item in items", "each")]

    [InlineData("=> result > 0", "=>")]
    [InlineData("! cw", "!")]
    [InlineData("func Add () → int", "→")]
    [InlineData("new Person()", "new")]
    [InlineData("items[0..2]", "..")]
    [InlineData("value ?? fallback", "??")]
    [InlineData("value?.Name", "?.")]
    public async Task NativeSyntaxHover_UsesCapturedTokenAndExactUtf16RangeAsync(string statement, string syntax)
    {
        var source = "// \u03c0\U0001f600\r\n  " + statement;
        var start = source.IndexOf(syntax, source.IndexOf('\n') + 1, StringComparison.Ordinal);
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///native-syntax-hover.calr");
        _workspace.GetOrCreate(uri, source);
        var (line, column) = LspTestHarness.GetLineColumn(source, start);
        for (var index = 0; index < syntax.Length; index++)
        {
            var hover = await _handler.Handle(new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1 + index)
            }, CancellationToken.None);
            Assert.NotNull(hover);
            Assert.Equal(TagDocumentationProvider.Instance.GetTagDocumentation(syntax)!.ToMarkdown(),
                hover.Contents.MarkupContent!.Value);
            Assert.Equal(new Position(line - 1, column - 1), hover.Range!.Start);
            Assert.Equal(new Position(line - 1, column - 1 + syntax.Length), hover.Range.End);
        }
    }

    [Theory]
    [InlineData("// func New", "func")]
    [InlineData("/// func New", "func")]
    [InlineData("§F{Run:pub}", "§F")]
    [InlineData("§!{Run}", "!")]
    public void NativeSyntaxHover_DoesNotTeachCommentOrRetiredTokens(string source, string marker)
    {
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///not-native-hover.calr");
        var snapshot = _workspace.GetOrCreate(uri, source).Snapshot;
        var (line, column) = LspTestHarness.GetLineColumn(source,
            source.IndexOf(marker, StringComparison.Ordinal));
        Assert.Null(HoverHandler.BuildHover(snapshot, new Position(line - 1, column - 1)));
    }

    [Theory]
    [InlineData("module Demo\n  func Identity<T> (T:value) → T\n    !\n    return value",
        "Identity", "func Identity<T> (T:value) → T")]
    [InlineData("module Demo\n  class Person\n    field string:name",
        "Person", "class Person")]
    [InlineData("module Demo\n  class Person\n    field string:name",
        "name", "field string:name")]
    [InlineData("module Demo\n  class Person\n    func Add (int:x) → int\n      !\n      return x",
        "Add", "func Add (int:x) → int")]
    [InlineData("module Demo\n  class Person\n    property int:Count\n      get\n        return 1",
        "Count", "property int:Count")]
    [InlineData("module Demo\n  interface IShape\n    func Area () → double\n      !",
        "IShape", "interface IShape")]
    [InlineData("module Demo\n  enum Color\n    Red",
        "Color", "enum Color")]
    [InlineData("module Demo\n  func Identity (\n    int:value\n  ) → int\n    !\n    return value",
        "Identity", "func Identity (\n    int:value\n  ) → int")]
    [InlineData("module Demo\n  func Text (\n    string:value = \"(value)\"\n  ) → string\n    !\n    return value",
        "Text", "func Text (\n    string:value = \"(value)\"\n  ) → string")]
    public void NativeSymbolHover_RetainsExactAuthoredDeclarationWithoutSourceIds(
        string source, string name, string expectedHeader)
    {
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///native-symbol-hover.calr");
        var snapshot = _workspace.GetOrCreate(uri, source).Snapshot;
        Assert.NotNull(snapshot.Ast);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.IsError);
        var (line, column) = LspTestHarness.GetLineColumn(source,
            source.IndexOf(name, StringComparison.Ordinal));
        var hover = HoverHandler.BuildHover(snapshot, new Position(line - 1, column - 1));
        Assert.NotNull(hover);
        var content = hover.Contents.MarkupContent!.Value;
        Assert.Contains(expectedHeader, content);
        Assert.DoesNotContain("§", content);
        Assert.DoesNotContain("return value", content);
    }

    [Fact]
    public void BuildHover_Function_CountsParameterRefinementsAsPreconditions()
    {
        // #1618: a parameter refinement is the precondition; the hover counts it.
        const string source = """
            module TestModule
              func Divide (int:a, int:b | (# != 0), int:c | (# > 0)) → int
                => (result >= 0)
                return a / b
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
            "file:///refined-hover.calr");
        var state = _workspace.GetOrCreate(uri, source, version: 1);
        var (line, column) = LspTestHarness.GetLineColumn(
            source, source.IndexOf("Divide", StringComparison.Ordinal));

        var hover = HoverHandler.BuildHover(state.Snapshot, new Position(line - 1, column - 1));

        Assert.NotNull(hover);
        var markup = Assert.IsType<MarkupContent>(hover.Contents.MarkupContent);
        Assert.Contains("**Preconditions:** 2", markup.Value, StringComparison.Ordinal);
        Assert.Contains("**Postconditions:** 1", markup.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHover_UsesOneCapturedAnalysisSnapshot()
    {
        const string original = """
            module TestModule
              func Original () → int
                return 1
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
            "file:///snapshot-hover.calr");
        var state = _workspace.GetOrCreate(uri, original, version: 1);
        var captured = state.Snapshot;
        var offset = original.IndexOf("Original", StringComparison.Ordinal);
        var (line, column) = LspTestHarness.GetLineColumn(original, offset);
        _workspace.Update(
            uri,
            "module Changed\n  func New () → int\n    return 2\n",
            version: 2);

        var hover = HoverHandler.BuildHover(
            captured,
            new Position(line - 1, column - 1));

        Assert.NotNull(hover);
        var markup = Assert.IsType<MarkupContent>(hover.Contents.MarkupContent);
        Assert.Contains("Original", markup.Value, StringComparison.Ordinal);
        Assert.Equal(1, captured.Version);
        Assert.Equal(2, state.Version);
        Assert.Contains("func Original () → int", markup.Value);
        Assert.DoesNotContain("§", markup.Value);
    }

    [Fact]
    public void NativeSymbolHover_DoesNotConfuseEscapedIdentifiersOrStringValuesWithKeywords()
    {
        const string source = """
            module Demo
              func Read (string:@func) → string
                !
                let string:text = "func"
                return @func
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///escaped-hover.calr");
        var snapshot = _workspace.GetOrCreate(uri, source).Snapshot;
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.IsError);
        foreach (var offset in new[] { source.LastIndexOf("@func", StringComparison.Ordinal) + 1,
            source.IndexOf("\"func\"", StringComparison.Ordinal) + 1 })
        {
            var (line, column) = LspTestHarness.GetLineColumn(source, offset);
            var hover = HoverHandler.BuildHover(snapshot, new Position(line - 1, column - 1));
            Assert.NotNull(hover);
            Assert.DoesNotContain("## Function", hover.Contents.MarkupContent!.Value);
        }
    }

    [Fact]
    public void NativeSyntaxHover_DoesNotInspectNativeLookingRawPayload()
    {
        const string payload = "new func if";
        var source = $"module Demo\n  raw stmt {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///raw-native-hover.calr");
        var snapshot = _workspace.GetOrCreate(uri, source).Snapshot;
        var payloadStart = source.IndexOf(payload, StringComparison.Ordinal);
        for (var index = 0; index < payload.Length; index++)
        {
            var (line, column) = LspTestHarness.GetLineColumn(source, payloadStart + index);
            Assert.Null(HoverHandler.BuildHover(snapshot, new Position(line - 1, column - 1)));
        }
    }

    [Fact]
    public void Hover_Function_ReturnsSymbolInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            module TestModule
              func /*cursor*/Add (int:a, int:b) → int
                return a + b
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
            module TestModule
              func Test (int:/*cursor*/value) → int
                return value
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("value", result.Name);
        Assert.Equal("parameter", result.Kind);
        Assert.Equal("int", result.Type);
    }

    [Fact]
    public void Hover_Class_ReturnsClassInfo()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            module TestModule
              class /*cursor*/Person
                field string:name
                field int:age
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
            module TestModule
              class Person
                field string:/*cursor*/name
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
            module TestModule
              class Calculator
                func /*cursor*/Add (int:a, int:b) → int
                  return a + b
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
            module TestModule
              interface /*cursor*/IShape
                func GetArea () → double
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
            module TestModule
              enum /*cursor*/Color
                Red
                Green
                Blue
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
            module TestModule
              enum Color
                /*cursor*/Red
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
            module TestModule
              func Test () → int
                let int:/*cursor*/x = 42
                return x
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
            module /*cursor*/TestModule
              func Test () → int
                return 0
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

        var source = "new User(name)";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test_new.calr");
        var doc = _workspace.GetOrCreate(uri, source);
        Assert.NotNull(doc); // Verify document was created

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
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

        var source = "func Test () → void";
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
        Assert.Contains("Function", content);
    }

    [Fact]
    public async Task Handle_TagHover_L_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "for i from 0 to 10 step 1";
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

        var source = "if true";
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
        Assert.Contains("If", content);
    }

    [Fact]
    public async Task Handle_TagHover_RetiredCloser_DoesNotAdvertiseSyntaxAsync()
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

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TagHover_AWAIT_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "await expr";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 3)
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

        var source = "try\n  Run()\ncatch\n  Recover()";
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

        var source = "! cw,fs:r";
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 0)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? "";
        Assert.Contains("Effects", content);
    }

    [Fact]
    public async Task Handle_TagHover_RetiredRequires_HasNoPreconditionDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        // #1618: `requires` is retired (a parse error); a precondition is a parameter refinement,
        // so the keyword has no hover documentation of its own.
        var source = "requires x > 0";
        Assert.Null(TagDocumentationProvider.ExtractTagAtPosition(source, 1));
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///test.calr");
        _workspace.GetOrCreate(uri, source);

        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, 1)
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        Assert.DoesNotContain("Precondition", result?.Contents.MarkupContent?.Value ?? "");
    }

    [Fact]
    public async Task Handle_TagHover_S_ReturnsDocumentationAsync()
    {
        if (!TagDocumentationProvider.Instance.IsLoaded) return;

        var source = "=> result > 0";
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

        var source = "new User(name)";
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

        var source = "for i from 0 to 10 step 1";
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
