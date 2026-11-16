using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public sealed class FormattingHandlerTests
{

    [Fact]
    public async Task CompilerErrors_ReturnNoEditsAndLogReasonAsync()
    {
        const string source =
            "module Formatting Formatting block\n" +
            "  func Main () → void\n" +
            "    !\n" +
            "    Console.WriteLine(\"missing effect\")\n";
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
