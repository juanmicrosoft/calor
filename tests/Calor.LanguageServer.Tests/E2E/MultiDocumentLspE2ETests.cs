using System.Text;
using System.Text.Json;
using Calor.LanguageServer.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E;

/// <summary>
/// Multi-document end-to-end coverage for the Calor LSP server. Exercises workspace
/// rename, references, diagnostics, and semantic tokens across 5 open .calr documents
/// where one declares a symbol and the others call it. Sibling to <c>LspE2ETests</c>
/// which only covers single-document scenarios.
/// </summary>
public class MultiDocumentLspE2ETests
{
    private readonly ITestOutputHelper _output;

    public MultiDocumentLspE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Diagnostics_BufferedForOtherDocuments_ArePreservedAsync()
    {
        const string firstUri = "file:///calor-lsp-e2e/first.calr";
        const string secondUri = "file:///calor-lsp-e2e/second.calr";
        await using var client = await LspTestClient.StartAsync(_output);
        await client.InitializeAsync(Path.Combine(Path.GetTempPath(), $"calor-lsp-buffer-{Guid.NewGuid():N}"));
        await client.OpenAsync(firstUri, BuildTargetSource("First"), version: 1);
        await client.OpenAsync(secondUri, BuildCallerSource(1, "First"), version: 1);

        await AssertNoErrorsAsync(client, secondUri, expectedVersion: 1);
        await AssertNoErrorsAsync(client, firstUri, expectedVersion: 1);
        await client.ShutdownAsync();
    }

    [Fact]
    public async Task Rename_TargetSymbol_UpdatesAllDocuments_And_References_ConsistentAsync()
    {
        const string oldName = "f_target";
        const string newName = "f_renamed";
        const int callerCount = 4;

        // Fixture: 5 documents in a shared workspace root. Doc 1 declares f_target;
        // Docs 2-5 import TargetModule and each call f_target once.
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"calor-lsp-multi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var targetPath = Path.Combine(workspaceRoot, "target.calr");
            var targetSource = BuildTargetSource(oldName);
            File.WriteAllText(targetPath, targetSource);
            var targetUri = new Uri(targetPath).AbsoluteUri;

            var callerSources = new Dictionary<string, string>(StringComparer.Ordinal);
            var callerUris = new List<string>();
            for (var index = 1; index <= callerCount; index++)
            {
                var callerPath = Path.Combine(
                    workspaceRoot,
                    $"caller{index}.calr");
                var callerSource = BuildCallerSource(index, oldName);
                File.WriteAllText(callerPath, callerSource);
                var callerUri = new Uri(callerPath).AbsoluteUri;
                callerSources[callerUri] = callerSource;
                callerUris.Add(callerUri);
            }

            await using var client = await LspTestClient.StartAsync(_output);
            await client.InitializeAsync(workspaceRoot);

            // Open all 5 documents and drain their initial diagnostics.
            await client.OpenAsync(targetUri, targetSource, version: 1);
            await AssertNoErrorsAsync(client, targetUri, expectedVersion: 1);
            foreach (var callerUri in callerUris)
            {
                await client.OpenAsync(callerUri, callerSources[callerUri], version: 1);
                await AssertNoErrorsAsync(client, callerUri, expectedVersion: 1);
            }

            // Issue rename on the declaration in document 1.
            var declarationPosition = PositionOf(targetSource, oldName);
            var renameResult = await client.RequestResultAsync(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = targetUri },
                    position = declarationPosition,
                    newName,
                });

            Assert.NotEqual(JsonValueKind.Null, renameResult.ValueKind);
            var documentChanges = renameResult.GetProperty("documentChanges");

            // Per-URI edit inventory: adversarial review flagged that asserting
            // just the URI set is hollow — a regression that emitted duplicate
            // edits, misspelled the newText, or ranged into the wrong bytes
            // would still slip through. Assert every edit's shape.
            var editsByUri = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            foreach (var change in documentChanges.EnumerateArray())
            {
                var uri = change.GetProperty("textDocument").GetProperty("uri").GetString()!;
                var edits = change.GetProperty("edits").EnumerateArray().ToList();
                editsByUri[uri] = edits;
            }

            // Structural invariants across the whole workspace edit:
            //   * Exactly 5 documents touched (target + 4 callers).
            //   * Target doc has exactly 1 edit (the declaration).
            //   * Each caller doc has exactly 1 edit (the call site).
            //   * Every edit uses the newName as newText.
            //   * Every edit's range covers exactly oldName.Length characters.
            Assert.Equal(1 + callerCount, editsByUri.Count);
            Assert.Contains(targetUri, editsByUri.Keys);
            foreach (var callerUri in callerUris)
                Assert.Contains(callerUri, editsByUri.Keys);

            foreach (var (uri, edits) in editsByUri)
            {
                Assert.True(edits.Count == 1,
                    $"Expected exactly 1 edit for {uri}, got {edits.Count}. Duplicated edits from the rename handler would be a real regression.");
                var edit = edits[0];
                Assert.Equal(newName, edit.GetProperty("newText").GetString());
                var range = edit.GetProperty("range");
                var startChar = range.GetProperty("start").GetProperty("character").GetInt32();
                var endChar = range.GetProperty("end").GetProperty("character").GetInt32();
                var startLine = range.GetProperty("start").GetProperty("line").GetInt32();
                var endLine = range.GetProperty("end").GetProperty("line").GetInt32();
                Assert.True(startLine == endLine,
                    $"Rename edit on {uri} spans multiple lines ({startLine}→{endLine}) — an identifier rename must be single-line.");
                Assert.Equal(oldName.Length, endChar - startChar);
            }

            // Apply the workspace edit in-memory and push the updated text back to
            // the server as didChange notifications, then verify diagnostics remain
            // clean on all 5 documents.
            var updatedSources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [targetUri] = ApplyEditsForUri(
                    targetSource,
                    documentChanges,
                    targetUri),
            };
            foreach (var callerUri in callerUris)
            {
                updatedSources[callerUri] = ApplyEditsForUri(
                    callerSources[callerUri],
                    documentChanges,
                    callerUri);
            }

            // Sanity: the new name must appear post-edit, the old name must not.
            foreach (var (uri, updated) in updatedSources)
            {
                Assert.Contains(newName, updated);
                Assert.DoesNotContain(oldName, updated);
                _ = uri; // silence unused warning
            }

            await client.ChangeAsync(targetUri, updatedSources[targetUri], version: 2);
            await AssertNoErrorsAsync(client, targetUri, expectedVersion: 2);
            foreach (var callerUri in callerUris)
            {
                await client.ChangeAsync(callerUri, updatedSources[callerUri], version: 2);
                await AssertNoErrorsAsync(client, callerUri, expectedVersion: 2);
            }

            // Re-issue references on the renamed declaration; must still find all
            // sites: 1 declaration + 4 callers = 5.
            var updatedTargetSource = updatedSources[targetUri];
            var references = await client.RequestResultAsync(
                "textDocument/references",
                new
                {
                    textDocument = new { uri = targetUri },
                    position = PositionOf(updatedTargetSource, newName),
                    context = new { includeDeclaration = true },
                });
            Assert.Equal(
                1 + callerCount,
                references.GetArrayLength());
            var referenceUris = references.EnumerateArray()
                .Select(location => location.GetProperty("uri").GetString())
                .Where(uri => uri != null)
                .Select(uri => uri!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains(targetUri, referenceUris);
            foreach (var callerUri in callerUris)
                Assert.Contains(callerUri, referenceUris);

            // Semantic tokens on the target document must include a token at
            // the EXACT declaration span of the new name — not merely "somewhere
            // in the document a token slices to the identifier spelling."
            // Adversarial review flagged the previous assertion as
            // tautological because it walked the server's token stream but
            // then verified the slice against the test's own edit-applied
            // source, so any correctly-shaped token stream would pass as long
            // as its (line, char, length) triples happened to land on some
            // occurrence of the string. The strengthened check pins the
            // declaration span explicitly.
            var tokens = await client.RequestResultAsync(
                "textDocument/semanticTokens/full",
                new { textDocument = new { uri = targetUri } });
            Assert.NotEqual(JsonValueKind.Null, tokens.ValueKind);
            Assert.True(tokens.TryGetProperty("data", out var tokenData));
            Assert.True(
                tokenData.GetArrayLength() > 0,
                "Expected non-empty semantic tokens data array for target document.");

            // Declaration span: the FIRST occurrence of newName in the target
            // document's updated source (immediately after the §F{...:) is the
            // declaration site emitted by the rename edit.
            var (declLine, declChar) = LineCharacterOf(updatedTargetSource, newName);
            Assert.True(
                DecodedTokensContainSpan(tokenData, declLine, declChar, newName.Length),
                $"Semantic tokens for target must include a token at declaration span line={declLine} char={declChar} length={newName.Length} — proves the server recognises the renamed declaration as a semantic token, not just that a token happens to spell '{newName}'.");
            Assert.False(
                DecodedTokensCoverIdentifier(tokenData, updatedTargetSource, oldName),
                $"Semantic tokens for target must not still reference '{oldName}'.");

            await client.ShutdownAsync();
        }
        finally
        {
            try
            {
                if (Directory.Exists(workspaceRoot))
                    Directory.Delete(workspaceRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; do not mask a real assertion failure.
            }
        }
    }

    private static string BuildTargetSource(string symbolName) =>
        $$"""
        §M{m001:TargetModule}
          §F{f001:{{symbolName}}:pub} () -> i32
            §R INT:42
        """;

    // Note (adversarial review): `§U{TargetModule}` was previously included on the
    // caller-module source. The parser treats §U payload as a .NET namespace path
    // (System.Text style); a Calor `§M{id:Name}` module is not registered as an
    // importable namespace, so `§U{TargetModule}` was a no-op — cross-module
    // symbol resolution goes through WorkspaceState in the language server, which
    // merges symbols across open documents regardless of using directives.
    // Removed to keep the fixture honest about what's actually being tested
    // (multi-document workspace-state resolution, not `§U` import).
    private static string BuildCallerSource(int index, string symbolName) =>
        $$"""
        §M{m{{index + 1:D3}}:CallerModule{{index}}}
          §F{f{{index + 100:D3}}:Run:pub} () -> i32
            §R §C{{{symbolName}}} §/C
        """;

    private static async Task AssertNoErrorsAsync(
        LspTestClient client,
        string uri,
        int expectedVersion)
    {
        var diagnostics = await client.WaitForDiagnosticsAsync(uri, expectedVersion);
        var errors = diagnostics.EnumerateArray()
            .Where(diagnostic =>
                diagnostic.TryGetProperty("severity", out var severity)
                && severity.GetInt32() == 1)
            .ToArray();
        Assert.Empty(errors);
    }

    private static string ApplyEditsForUri(
        string source,
        JsonElement documentChanges,
        string uri)
    {
        foreach (var change in documentChanges.EnumerateArray())
        {
            var changeUri = change.GetProperty("textDocument")
                .GetProperty("uri").GetString();
            if (!string.Equals(changeUri, uri, StringComparison.Ordinal))
                continue;

            // Server already emits edits sorted descending by span start so that
            // applying them in order does not invalidate later offsets. Re-sort
            // defensively in case that ever changes.
            var edits = change.GetProperty("edits")
                .EnumerateArray()
                .Select(edit => new
                {
                    Start = PositionToOffset(source, edit.GetProperty("range")
                        .GetProperty("start")),
                    End = PositionToOffset(source, edit.GetProperty("range")
                        .GetProperty("end")),
                    Text = edit.GetProperty("newText").GetString() ?? string.Empty,
                })
                .OrderByDescending(edit => edit.Start)
                .ToArray();

            var builder = new StringBuilder(source);
            foreach (var edit in edits)
            {
                builder.Remove(edit.Start, edit.End - edit.Start);
                builder.Insert(edit.Start, edit.Text);
            }

            return builder.ToString();
        }

        return source;
    }

    private static int PositionToOffset(string source, JsonElement position)
    {
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();
        var offset = 0;
        var currentLine = 0;
        while (currentLine < line && offset < source.Length)
        {
            if (source[offset] == '\n')
                currentLine++;
            offset++;
        }
        return Math.Min(offset + character, source.Length);
    }

    /// <summary>
    /// Walks the LSP delta-encoded semantic-tokens stream and returns true iff
    /// there is a token at exactly (targetLine, targetChar) with the given
    /// length. Unlike <see cref="DecodedTokensCoverIdentifier"/> — which
    /// merely proves the server emitted SOME token whose slice matches an
    /// identifier's spelling — this pins the SPECIFIC span, catching a
    /// regression where the server emits tokens for the wrong spans (e.g.,
    /// the whole `§F{...}` header instead of the identifier).
    /// </summary>
    private static bool DecodedTokensContainSpan(
        JsonElement tokenData,
        int targetLine,
        int targetChar,
        int targetLength)
    {
        var array = tokenData.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var currentLine = 0;
        var currentChar = 0;
        for (var offset = 0; offset + 5 <= array.Length; offset += 5)
        {
            var deltaLine = array[offset];
            var deltaChar = array[offset + 1];
            var length = array[offset + 2];
            if (deltaLine > 0)
            {
                currentLine += deltaLine;
                currentChar = deltaChar;
            }
            else
            {
                currentChar += deltaChar;
            }

            if (currentLine == targetLine && currentChar == targetChar && length == targetLength)
                return true;
        }
        return false;
    }

    private static bool DecodedTokensCoverIdentifier(
        JsonElement tokenData,
        string source,
        string identifier)
    {
        // Semantic tokens are encoded as a flat array of 5-tuples relative to the
        // previous token: [deltaLine, deltaChar, length, tokenType, tokenModifiers].
        var array = tokenData.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var lines = source.Split('\n');
        var currentLine = 0;
        var currentChar = 0;
        for (var offset = 0; offset + 5 <= array.Length; offset += 5)
        {
            var deltaLine = array[offset];
            var deltaChar = array[offset + 1];
            var length = array[offset + 2];
            if (deltaLine > 0)
            {
                currentLine += deltaLine;
                currentChar = deltaChar;
            }
            else
            {
                currentChar += deltaChar;
            }

            if (currentLine >= lines.Length)
                continue;

            var line = lines[currentLine];
            if (currentChar < 0 || currentChar + length > line.Length)
                continue;

            var slice = line.Substring(currentChar, length);
            if (string.Equals(slice, identifier, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static (int Line, int Character) LineCharacterOf(string source, string value)
    {
        var offset = source.IndexOf(value, StringComparison.Ordinal);
        if (offset < 0)
            throw new InvalidOperationException($"'{value}' not found in source.");
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }
        return (line, character);
    }

    private static object PositionOf(string source, string value)
    {
        var offset = source.IndexOf(value, StringComparison.Ordinal);
        if (offset < 0)
            throw new InvalidOperationException($"'{value}' not found in source.");

        var line = 0;
        var character = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return new { line, character };
    }
}
