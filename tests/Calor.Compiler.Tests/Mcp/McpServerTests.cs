using System.CommandLine;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class McpServerTests
{
    [Fact]
    public void McpCommand_Create_ReturnsCommandWithCorrectName()
    {
        var command = McpCommand.Create();

        Assert.Equal("mcp", command.Name);
    }

    [Fact]
    public void McpCommand_Create_ReturnsCommandWithCorrectDescription()
    {
        var command = McpCommand.Create();

        Assert.Contains("MCP", command.Description);
        Assert.Contains("AI", command.Description);
    }

    [Fact]
    public void McpCommand_Create_HasStdioOption()
    {
        var command = McpCommand.Create();

        var stdioOption = command.Options.FirstOrDefault(o => o.Name == "stdio");
        Assert.NotNull(stdioOption);
    }

    [Fact]
    public void McpCommand_Create_HasVerboseOption()
    {
        var command = McpCommand.Create();

        var verboseOption = command.Options.FirstOrDefault(o => o.Name == "verbose");
        Assert.NotNull(verboseOption);
    }

    [Fact]
    public void McpCommand_Create_HasHandler()
    {
        var command = McpCommand.Create();

        Assert.NotNull(command.Handler);
    }

    [Fact]
    public void McpCommand_IsRegisteredInRootCommand()
    {
        var rootCommand = new RootCommand("Test");
        rootCommand.AddCommand(McpCommand.Create());

        var mcpCommand = rootCommand.Subcommands.FirstOrDefault(c => c.Name == "mcp");

        Assert.NotNull(mcpCommand);
        Assert.Equal("mcp", mcpCommand.Name);
    }

    [Fact]
    public async Task McpMessageHandler_HandleInitialize_ReturnsServerInfo()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("1").RootElement,
            Method = "initialize",
            Params = JsonDocument.Parse("""
                {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": { "name": "test", "version": "1.0" }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("calor", json);
        Assert.Contains("protocolVersion", json);
        Assert.Contains("capabilities", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleInitialized_ReturnsNull()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Method = "initialized"
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.Null(response);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsList_ReturnsAllTools()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("2").RootElement,
            Method = "tools/list"
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("calor_compile", json);
        Assert.Contains("calor_verify", json);
        Assert.Contains("calor_analyze", json);
        Assert.Contains("calor_convert", json);
        Assert.Contains("calor_syntax_help", json);
        Assert.Contains("calor_syntax_lookup", json);
        Assert.Contains("calor_assess", json);
        Assert.Contains("calor_typecheck", json);
        Assert.Contains("calor_verify_contracts", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_CompileTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("3").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_compile",
                    "arguments": {
                        "source": "§M{m001:Test}\n§F{f001:Add:pub}\n§I{i32:a}\n§I{i32:b}\n§O{i32}\n§R (+ a b)\n§/F{f001}\n§/M{m001}"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("success", json);
        Assert.Contains("true", json.ToLower());
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_CompileTool_Error()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("4").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_compile",
                    "arguments": {
                        "source": "invalid calor code §§§"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("diagnostics", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_ConvertTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("5").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_convert",
                    "arguments": {
                        "source": "public class Test { public int Add(int a, int b) => a + b; }",
                        "moduleName": "TestModule"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("success", json);
        Assert.Contains("calorSource", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_SyntaxHelpTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("6").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_syntax_help",
                    "arguments": {
                        "feature": "contracts"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("feature", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_SyntaxLookupTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("12").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_syntax_lookup",
                    "arguments": {
                        "query": "object instantiation"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("found", json);
        Assert.Contains("true", json.ToLower());
        // Check for NEW tag (JSON escapes § as \u00a7)
        Assert.Contains("NEW", json);
        Assert.Contains("examples", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_SyntaxLookupTool_FuzzyMatch()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("13").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_syntax_lookup",
                    "arguments": {
                        "query": "for loop"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("found", json);
        Assert.Contains("true", json.ToLower());
        // Check for loop syntax (JSON escapes § as \u00a7, and contains L{)
        Assert.Contains("for loop", json.ToLower());
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_SyntaxLookupTool_NotFound()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("14").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_syntax_lookup",
                    "arguments": {
                        "query": "nonexistent xyz abc"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("found", json);
        Assert.Contains("false", json.ToLower());
        Assert.Contains("availableConstructs", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_UnknownTool_ReturnsError()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("7").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "unknown_tool",
                    "arguments": {}
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response.Error);
        Assert.Contains("unknown_tool", response.Error.Message.ToLower());
    }

    [Fact]
    public async Task McpMessageHandler_HandleUnknownMethod_ReturnsError()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("8").RootElement,
            Method = "unknown/method"
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.NotNull(response.Error);
        Assert.Equal(JsonRpcError.MethodNotFound, response.Error.Code);
    }

    [Fact]
    public async Task McpMessageHandler_HandlePing_ReturnsEmptyObject()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("9").RootElement,
            Method = "ping"
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public async Task McpServer_ProcessMessage_InitializeFlow()
    {
        var inputMessage = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
            """;
        var inputBytes = Encoding.UTF8.GetBytes(inputMessage);
        var fullInput = $"Content-Length: {inputBytes.Length}\r\n\r\n{inputMessage}";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(fullInput));
        using var output = new MemoryStream();

        var server = new McpServer(input, output);

        // Run server (it will stop when input stream ends)
        await server.RunAsync();

        output.Position = 0;
        var response = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("Content-Length:", response);
        Assert.Contains("jsonrpc", response);
        Assert.Contains("calor", response);
    }

    [Fact]
    public async Task McpServer_ProcessMessage_ToolsListFlow()
    {
        var inputMessage = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";
        var inputBytes = Encoding.UTF8.GetBytes(inputMessage);
        var fullInput = $"Content-Length: {inputBytes.Length}\r\n\r\n{inputMessage}";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(fullInput));
        using var output = new MemoryStream();

        var server = new McpServer(input, output);
        await server.RunAsync();

        output.Position = 0;
        var response = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("calor_compile", response);
        Assert.Contains("calor_verify", response);
        Assert.Contains("calor_analyze", response);
        Assert.Contains("calor_convert", response);
        Assert.Contains("calor_syntax_help", response);
        Assert.Contains("calor_syntax_lookup", response);
        Assert.Contains("calor_assess", response);
        Assert.Contains("calor_typecheck", response);
        Assert.Contains("calor_verify_contracts", response);
    }

    [Fact]
    public async Task McpServer_ProcessMessage_MultipleMessages()
    {
        var msg1 = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";
        var msg2 = """{"jsonrpc":"2.0","method":"initialized"}""";
        var msg3 = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

        var fullInput = new StringBuilder();
        foreach (var msg in new[] { msg1, msg2, msg3 })
        {
            var bytes = Encoding.UTF8.GetBytes(msg);
            fullInput.Append($"Content-Length: {bytes.Length}\r\n\r\n{msg}");
        }

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(fullInput.ToString()));
        using var output = new MemoryStream();

        var server = new McpServer(input, output);
        await server.RunAsync();

        output.Position = 0;
        var response = Encoding.UTF8.GetString(output.ToArray());

        // Should have responses for initialize and tools/list (initialized is a notification)
        Assert.Contains("protocolVersion", response);
        Assert.Contains("calor_compile", response);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_AssessTool_Success()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("10").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_assess",
                    "arguments": {
                        "source": "public class Test { public async Task<int> GetValueAsync() { return await Task.FromResult(42); } }"
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("success", json);
        Assert.Contains("summary", json);
        Assert.Contains("files", json);
        Assert.Contains("AsyncPotential", json);
    }

    [Fact]
    public async Task McpMessageHandler_HandleToolsCall_AssessTool_MultiFile()
    {
        var handler = new McpMessageHandler();
        var request = new JsonRpcRequest
        {
            Id = JsonDocument.Parse("11").RootElement,
            Method = "tools/call",
            Params = JsonDocument.Parse("""
                {
                    "name": "calor_assess",
                    "arguments": {
                        "files": [
                            { "path": "Service.cs", "source": "public class Service { public async Task RunAsync() { await Task.Delay(1); } }" },
                            { "path": "Query.cs", "source": "using System.Linq; public class Query { public int[] Filter(int[] data) => data.Where(x => x > 0).ToArray(); }" }
                        ]
                    }
                }
                """).RootElement
        };

        var response = await handler.HandleRequestAsync(request);

        Assert.NotNull(response);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);

        var json = JsonSerializer.Serialize(response.Result, McpJsonOptions.Default);
        Assert.Contains("success", json);
        Assert.Contains("totalFiles", json);
        Assert.Contains("Service.cs", json);
        Assert.Contains("Query.cs", json);
    }

    [Fact]
    public async Task McpServer_ProcessMessage_AssessToolFlow()
    {
        var inputMessage = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"calor_assess","arguments":{"source":"public class Test { public void Method() { } }"}}}""";
        var inputBytes = Encoding.UTF8.GetBytes(inputMessage);
        var fullInput = $"Content-Length: {inputBytes.Length}\r\n\r\n{inputMessage}";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(fullInput));
        using var output = new MemoryStream();

        var server = new McpServer(input, output);
        await server.RunAsync();

        output.Position = 0;
        var response = Encoding.UTF8.GetString(output.ToArray());

        Assert.Contains("success", response);
        Assert.Contains("dimensions", response);
        Assert.Contains("ContractPotential", response);
        Assert.Contains("AsyncPotential", response);
        Assert.Contains("LinqPotential", response);
    }
}
