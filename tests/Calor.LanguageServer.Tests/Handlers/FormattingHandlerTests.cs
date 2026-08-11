using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public sealed class FormattingHandlerTests
{
    [Theory]
    [InlineData("\n", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r\n", false)]
    public async Task AppliedFormattingEdits_PreserveSourceAndRecompileAsync(
        string newline,
        bool trailingNewline)
    {
        var source = string.Join(
            newline,
            "§M{m001:Formatting}   ",
            "    §F{f001:Main:pub} () -> void",
            "        §E{cw}",
            "        §B{user001} STR:\"secret\"",
            "        // user001 must survive.",
            "        §P user001")
            + (trailingNewline ? newline : string.Empty);
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

        Assert.NotNull(edits);
        var applied = ApplyEdits(source, edits!);
        Assert.Contains("§B{user001}", applied);
        Assert.Contains("§P user001", applied);
        Assert.Contains("// user001 must survive.", applied);
        Assert.Equal(trailingNewline, applied.EndsWith(newline, StringComparison.Ordinal));
        Assert.Equal(Count(source, newline), Count(applied, newline));

        var before = Calor.Compiler.Program.Compile(source, "formatting.calr");
        var after = Calor.Compiler.Program.Compile(applied, "formatting.calr");
        Assert.False(before.HasErrors, string.Join("\n", before.Diagnostics.Errors));
        Assert.False(after.HasErrors, string.Join("\n", after.Diagnostics.Errors));
        Assert.Equal(before.GeneratedCode, after.GeneratedCode);
    }

    [Fact]
    public async Task CompilerErrors_ReturnNoEditsAndLogReasonAsync()
    {
        const string source =
            "§M{m001:Formatting}\n" +
            "  §F{f001:Main:pub} () -> void\n" +
            "    §P \"missing effect\"\n";
        var uri = DocumentUri.From("file:///formatting-errors.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var logger = new RecordingLogger();
        var handler = new FormattingHandler(workspace, logger);

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

        Assert.Null(edits);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("returned no edits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CapturedSnapshot_UsesOriginalSourceAfterWorkspaceUpdate()
    {
        const string original =
            "§M{m001:Original}   \r\n" +
            "    §F{f001:Main:pub} () -> void   \r\n";
        const string updated =
            "§M{m002:Updated}\n" +
            "    §F{f002:Different:pub} () -> void\n" +
            "        §B{broken\n";
        var uri = DocumentUri.From("file:///formatting-snapshot.calr");
        var workspace = new WorkspaceState();
        var state = workspace.GetOrCreate(uri, original);
        var captured = state.Snapshot;
        workspace.Update(uri, updated, version: 1);

        var edits = FormattingHandler.FormatSnapshot(
            captured,
            state.Uri,
            uri,
            new RecordingLogger());

        Assert.NotNull(edits);
        var applied = ApplyEdits(original, edits!);
        Assert.Contains("§M{m001:Original}", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated", applied, StringComparison.Ordinal);
        Assert.Equal(new Position(2, 0), Assert.Single(edits!).Range.End);
        Assert.Equal(updated, state.Source);
    }

    private static string ApplyEdits(string source, IEnumerable<TextEdit> edits)
    {
        var replacements = edits
            .Select(edit => (
                Start: GetOffset(source, edit.Range.Start),
                End: GetOffset(source, edit.Range.End),
                edit.NewText))
            .OrderByDescending(edit => edit.Start)
            .ToArray();

        var result = source;
        foreach (var replacement in replacements)
        {
            Assert.InRange(replacement.Start, 0, result.Length);
            Assert.InRange(replacement.End, replacement.Start, result.Length);
            result = result[..replacement.Start]
                + replacement.NewText
                + result[replacement.End..];
        }
        return result;
    }

    private static int GetOffset(string source, Position position)
    {
        var line = 0;
        var offset = 0;
        while (line < position.Line && offset < source.Length)
        {
            if (source[offset] == '\r')
            {
                offset++;
                if (offset < source.Length && source[offset] == '\n')
                {
                    offset++;
                }
                line++;
            }
            else if (source[offset] == '\n')
            {
                offset++;
                line++;
            }
            else
            {
                offset++;
            }
        }

        Assert.Equal(position.Line, line);
        var lineEnd = offset;
        while (lineEnd < source.Length
            && source[lineEnd] is not '\r' and not '\n')
        {
            lineEnd++;
        }
        Assert.InRange(position.Character, 0, lineEnd - offset);
        return offset + position.Character;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private sealed class RecordingLogger : ILogger<FormattingHandler>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
