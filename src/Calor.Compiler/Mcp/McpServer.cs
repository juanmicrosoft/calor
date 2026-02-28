using System.Text;
using System.Text.Json;

namespace Calor.Compiler.Mcp;

/// <summary>
/// MCP server that communicates over stdio using the JSON-RPC 2.0 protocol.
/// </summary>
public sealed class McpServer
{
    private readonly McpMessageHandler _handler;
    private readonly TextReader _reader;
    private readonly Stream _output;
    private readonly TextWriter? _log;
    private readonly bool _verbose;

    /// <summary>
    /// Creates an MCP server with the given streams.
    /// The input stream is wrapped in a StreamReader internally.
    /// </summary>
    public McpServer(Stream input, Stream output, bool verbose = false, TextWriter? log = null)
        : this(new StreamReader(input ?? throw new ArgumentNullException(nameof(input)), Encoding.UTF8, leaveOpen: true),
               output, verbose, log)
    {
    }

    /// <summary>
    /// Creates an MCP server with a TextReader for input.
    /// Prefer this constructor when a blocking TextReader is available (e.g., Console.In).
    /// </summary>
    public McpServer(TextReader reader, Stream output, bool verbose = false, TextWriter? log = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _verbose = verbose;
        _log = log;
        _handler = new McpMessageHandler(verbose, log);
    }

    /// <summary>
    /// Creates an MCP server using standard input/output.
    /// Uses Console.In which properly blocks when no data is available,
    /// unlike raw Console.OpenStandardInput() which can spin on empty reads.
    /// </summary>
    public static McpServer CreateStdio(bool verbose = false)
    {
        var output = Console.OpenStandardOutput();
        var log = verbose ? Console.Error : null;
        return new McpServer(Console.In, output, verbose, log);
    }

    /// <summary>
    /// Runs the server message loop until the input stream is closed.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log("MCP server starting...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await ReadMessageAsync(cancellationToken);
                if (message == null)
                {
                    Log("End of input stream, shutting down");
                    break;
                }

                var request = ParseRequest(message);
                if (request == null)
                {
                    Log("Failed to parse request");
                    await SendErrorAsync(null, JsonRpcError.ParseError, "Failed to parse request");
                    continue;
                }

                var response = await _handler.HandleRequestAsync(request);

                // Notifications don't get responses
                if (response != null)
                {
                    await SendResponseAsync(response);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Server cancelled");
                break;
            }
            catch (Exception ex)
            {
                Log($"Error in message loop: {ex.Message}");
                await SendErrorAsync(null, JsonRpcError.InternalError, $"Server error: {ex.Message}");
            }
        }

        Log("MCP server stopped");
    }

    /// <summary>
    /// Reads a message from the input stream.
    /// MCP stdio uses newline-delimited JSON (NDJSON) - each message is a single line.
    /// </summary>
    private async Task<string?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await _reader.ReadLineAsync(cancellationToken);

            if (line == null)
                return null;

            if (!string.IsNullOrWhiteSpace(line))
            {
                Log($"Read message: {line.Length} bytes");
                return line;
            }
        }
    }

    /// <summary>
    /// Parses a JSON-RPC request from a string.
    /// </summary>
    private JsonRpcRequest? ParseRequest(string message)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonRpcRequest>(message, McpJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            Log($"JSON parse error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends a JSON-RPC response to the output stream.
    /// </summary>
    private async Task SendResponseAsync(JsonRpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, McpJsonOptions.Default);
        await WriteMessageAsync(json);
    }

    /// <summary>
    /// Sends a JSON-RPC error response.
    /// </summary>
    private async Task SendErrorAsync(JsonElement? id, int code, string message)
    {
        var response = JsonRpcResponse.Failure(id, code, message);
        await SendResponseAsync(response);
    }

    /// <summary>
    /// Writes a message to the output stream.
    /// MCP stdio uses newline-delimited JSON (NDJSON) - each message is a single line.
    /// </summary>
    private async Task WriteMessageAsync(string content)
    {
        // MCP uses newline-delimited JSON - write message followed by newline
        var message = content + "\n";
        var bytes = Encoding.UTF8.GetBytes(message);

        Log($"Sending response: {content.Length} bytes");

        await _output.WriteAsync(bytes);
        await _output.FlushAsync();
    }

    private void Log(string message)
    {
        if (_verbose)
        {
            _log?.WriteLine($"[MCP Server] {message}");
        }
    }
}
