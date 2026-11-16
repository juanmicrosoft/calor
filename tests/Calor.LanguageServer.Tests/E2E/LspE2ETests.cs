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
