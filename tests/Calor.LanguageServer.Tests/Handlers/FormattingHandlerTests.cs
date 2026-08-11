using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public sealed class FormattingHandlerTests
{
    [Fact]
    public async Task AppliedFormattingEdit_PreservesSourceAndRecompilesAsync()
    {
        const string source = """
            §M{m001:Formatting}
                §F{f001:Main:pub} () -> void
                    §E{cw}
                    §B{user001} STR:"secret"
                    // user001 must survive.
                    §P user001
            """;
        var uri = DocumentUri.From("file:///formatting.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var handler = new FormattingHandler(workspace);

        var edits = await handler.Handle(
            new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Options = new FormattingOptions
                {
                    InsertSpaces = true,
                    TabSize = 2
                }
            },
            CancellationToken.None);

        var edit = Assert.Single(edits!);
        Assert.Equal(new Position(0, 0), edit.Range.Start);
        var applied = edit.NewText;
        Assert.Contains("§B{user001}", applied);
        Assert.Contains("§P user001", applied);
        Assert.Contains("// user001 must survive.", applied);

        var before = Calor.Compiler.Program.Compile(source, "formatting.calr");
        var after = Calor.Compiler.Program.Compile(applied, "formatting.calr");
        Assert.False(before.HasErrors, string.Join("\n", before.Diagnostics.Errors));
        Assert.False(after.HasErrors, string.Join("\n", after.Diagnostics.Errors));
        Assert.Equal(before.GeneratedCode, after.GeneratedCode);
    }
}
