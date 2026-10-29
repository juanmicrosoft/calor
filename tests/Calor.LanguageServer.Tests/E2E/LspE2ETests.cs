using System.Diagnostics;
using System.Text.Json;
using Calor.LanguageServer.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E;

public class LspE2ETests
{
    private readonly ITestOutputHelper _output;

    public LspE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Initialize_AdvertisesRealClientCapabilitiesAsync()
    {
        await using var client = await LspClient.StartAsync(_output);
        var initialize = await client.InitializeAsync();
        var capabilities = initialize.GetProperty("capabilities");

        Assert.True(capabilities.TryGetProperty("textDocumentSync", out _));
        Assert.True(capabilities.TryGetProperty("hoverProvider", out _));
        Assert.True(capabilities.TryGetProperty("completionProvider", out _));
        Assert.True(capabilities.TryGetProperty("definitionProvider", out _));
        Assert.True(capabilities.TryGetProperty("referencesProvider", out _));
        Assert.True(capabilities.TryGetProperty("renameProvider", out _));

        await client.ShutdownAsync();
        AssertProcessExited(client.ProcessId);
    }

    [Theory]
    [InlineData(
        """{"jsonrpc":"2.0","id":1,"method":"workspace/configuration","params":{}}""",
        "Request")]
    [InlineData(
        """{"jsonrpc":"2.0","id":"server-1","method":"workspace/configuration","params":{}}""",
        "Request")]
    [InlineData(
        """{"jsonrpc":"2.0","method":"window/logMessage","params":{}}""",
        "Notification")]
    [InlineData(
        """{"jsonrpc":"2.0","id":1,"result":null}""",
        "Response")]
    public void JsonRpcDispatch_ClassifiesMethodBeforeCollidingResponseId(
        string json,
        string expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            expected,
            LspClient.ClassifyMessage(document.RootElement).ToString());
    }

    [Theory]
    [InlineData("""1""")]
    [InlineData("\"server-request-1\"")]
    public void JsonRpcServerResponse_PreservesNumericAndStringIds(string idJson)
    {
        using var id = JsonDocument.Parse(idJson);
        var response = LspClient.CreateServerResponse(
            id.RootElement,
            result: null);

        Assert.Equal(
            id.RootElement.GetRawText(),
            response.GetProperty("id").GetRawText());
    }

    [Fact]
    public async Task CoreCapabilityFlow_IsReadyBoundedAndLeakFreeAsync()
    {
        const string uri = "file:///calor-lsp-e2e/main.calr";
        const string source = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R 42
              §F{f002:Use:pub} () -> i32
                §R §C{Compute} §/C
            """;
        await using var client = await LspClient.StartAsync(_output);
        await client.InitializeAsync();

        await client.OpenAsync(uri, source, version: 1);
        var initialDiagnostics = await client.WaitForDiagnosticsAsync(uri, version: 1);
        Assert.Equal(0, initialDiagnostics.GetArrayLength());

        var declaration = PositionOf(source, "Compute");
        var reference = PositionOf(source, "Compute", occurrence: 2);
        var hover = await client.RequestResultAsync(
            "textDocument/hover",
            new
            {
                textDocument = new { uri },
                position = declaration,
            });
        Assert.NotEqual(JsonValueKind.Null, hover.ValueKind);
        Assert.Contains("Compute", hover.GetRawText(), StringComparison.Ordinal);

        var definition = await client.RequestResultAsync(
            "textDocument/definition",
            new
            {
                textDocument = new { uri },
                position = reference,
            });
        Assert.Contains("main.calr", definition.GetRawText(), StringComparison.Ordinal);

        var references = await client.RequestResultAsync(
            "textDocument/references",
            new
            {
                textDocument = new { uri },
                position = declaration,
                context = new { includeDeclaration = true },
            });
        Assert.Equal(2, references.GetArrayLength());

        var rename = await client.RequestResultAsync(
            "textDocument/rename",
            new
            {
                textDocument = new { uri },
                position = declaration,
                newName = "Calculate",
            });
        Assert.Contains("Calculate", rename.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(
            2,
            rename.GetProperty("documentChanges")[0]
                .GetProperty("edits")
                .GetArrayLength());

        const string changedSource = """
            §M{m001:TestModule}
              §F{f001:Calculate:pub} () -> i32
                §R 42
              §F{f002:Use:pub} () -> i32
                §R §C{Calculate} §/C
            """;
        await client.ChangeAsync(uri, changedSource, version: 2);
        var changedDiagnostics = await client.WaitForDiagnosticsAsync(uri, version: 2);
        Assert.Equal(0, changedDiagnostics.GetArrayLength());

        const string completionUri = "file:///calor-lsp-e2e/completion.calr";
        await client.OpenAsync(completionUri, "§", version: 1);
        await client.WaitForDiagnosticsAsync(completionUri, version: 1);
        var completion = await client.RequestResultAsync(
            "textDocument/completion",
            new
            {
                textDocument = new { uri = completionUri },
                position = new { line = 0, character = 1 },
            });
        var completionItems = completion.ValueKind == JsonValueKind.Array
            ? completion
            : completion.GetProperty("items");
        Assert.Contains(
            completionItems.EnumerateArray(),
            item => item.GetProperty("label").GetString() == "§M");

        const string diagnosticUri = "file:///calor-lsp-e2e/diagnostics.calr";
        await client.OpenAsync(
            diagnosticUri,
            "§M{m003:Broken}\n  §UNKNOWN_TOKEN_XYZ",
            version: 1);
        var errors = await client.WaitForDiagnosticsAsync(diagnosticUri, version: 1);
        Assert.Contains(errors.EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("severity").GetInt32() == 1);
        await client.ChangeAsync(
            diagnosticUri,
            "§M{m003:Fixed}\n",
            version: 2);
        var cleared = await client.WaitForDiagnosticsAsync(diagnosticUri, version: 2);
        Assert.Equal(0, cleared.GetArrayLength());

        await client.ShutdownAsync();
        AssertProcessExited(client.ProcessId);
    }

    private static object PositionOf(
        string source,
        string value,
        int occurrence = 1)
    {
        var offset = -1;
        for (var index = 0; index < occurrence; index++)
        {
            offset = source.IndexOf(value, offset + 1, StringComparison.Ordinal);
            if (offset < 0)
                throw new InvalidOperationException($"'{value}' occurrence {occurrence} not found.");
        }

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

    private static void AssertProcessExited(int processId)
    {
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
    }
}
