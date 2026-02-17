using System.Text;
using Calor.Compiler.Mcp;
using Xunit;

namespace Calor.Compiler.Tests.Mcp.Tools;

/// <summary>
/// Integration tests that verify the SyntaxLookupTool works through the full MCP protocol.
/// </summary>
public class SyntaxLookupMcpIntegrationTests
{
    [Fact]
    public async Task McpServer_SyntaxLookup_ObjectInstantiation_ReturnsResult()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_syntax_lookup","arguments":{"query":"object instantiation"}}}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("found", response);
        Assert.Contains("true", response.ToLower());
        Assert.Contains("NEW", response);
        Assert.Contains("examples", response);
    }

    [Fact]
    public async Task McpServer_SyntaxLookup_ForLoop_ReturnsResult()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_syntax_lookup","arguments":{"query":"for loop"}}}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("found", response);
        Assert.Contains("true", response.ToLower());
        Assert.Contains("loop", response.ToLower());
    }

    [Fact]
    public async Task McpServer_SyntaxLookup_AsyncAwait_ReturnsResult()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_syntax_lookup","arguments":{"query":"async await"}}}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("found", response);
        Assert.Contains("true", response.ToLower());
        Assert.Contains("async", response.ToLower());
    }

    [Fact]
    public async Task McpServer_SyntaxLookup_TryCatch_ReturnsResult()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_syntax_lookup","arguments":{"query":"try catch"}}}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("found", response);
        Assert.Contains("true", response.ToLower());
        Assert.Contains("TR", response);
    }

    [Fact]
    public async Task McpServer_SyntaxLookup_Contracts_ReturnsResult()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_syntax_lookup","arguments":{"query":"precondition"}}}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("found", response);
        Assert.Contains("true", response.ToLower());
        Assert.Contains("precondition", response.ToLower());
    }

    [Fact]
    public async Task McpServer_ToolsList_IncludesSyntaxLookup()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

        var response = await ExecuteMcpRequestAsync(request);

        Assert.Contains("calor_syntax_lookup", response);
        Assert.Contains("C#", response);
    }

    /// <summary>
    /// Executes an MCP request through the full server pipeline using Content-Length framing.
    /// </summary>
    private static async Task<string> ExecuteMcpRequestAsync(string jsonRequest)
    {
        var requestBytes = Encoding.UTF8.GetBytes(jsonRequest);
        var framedRequest = $"Content-Length: {requestBytes.Length}\r\n\r\n{jsonRequest}";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(framedRequest));
        using var output = new MemoryStream();

        var server = new McpServer(input, output);
        await server.RunAsync();

        output.Position = 0;
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
