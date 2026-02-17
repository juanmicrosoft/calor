using System.Text;
using System.Text.Json;

namespace Calor.Compiler.Mcp;

/// <summary>
/// MCP server that communicates over stdio using the JSON-RPC 2.0 protocol.
/// </summary>
public sealed class McpServer
{
    private readonly McpMessageHandler _handler;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter? _log;
    private readonly bool _verbose;

    /// <summary>
    /// Creates an MCP server with the given streams.
    /// </summary>
    public McpServer(Stream input, Stream output, bool verbose = false, TextWriter? log = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _verbose = verbose;
        _log = log;
        _handler = new McpMessageHandler(verbose, log);
    }

    /// <summary>
    /// Creates an MCP server using standard input/output.
    /// </summary>
    public static McpServer CreateStdio(bool verbose = false)
    {
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        var log = verbose ? Console.Error : null;
        return new McpServer(input, output, verbose, log);
    }

    /// <summary>
    /// Runs the server message loop until the input stream is closed.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log("MCP server starting...");

        using var reader = new StreamReader(_input, Encoding.UTF8, leaveOpen: true);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await ReadMessageAsync(reader, cancellationToken);
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
    /// Reads a message from the input stream using Content-Length header.
    /// </summary>
    private async Task<string?> ReadMessageAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        // Read headers until empty line
        int contentLength = -1;
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrEmpty(line))
            {
                // Empty line signals end of headers
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var lengthStr = line["Content-Length:".Length..].Trim();
                if (int.TryParse(lengthStr, out var length))
                {
                    contentLength = length;
                }
            }
            // Ignore other headers (e.g., Content-Type)
        }

        // Check for end of stream
        if (line == null)
        {
            return null;
        }

        if (contentLength <= 0)
        {
            Log("No Content-Length header found");
            return null;
        }

        Log($"Reading message body: {contentLength} bytes");

        // Read the message body
        var buffer = new char[contentLength];
        var totalRead = 0;
        while (totalRead < contentLength)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
            if (read == 0)
            {
                Log("Unexpected end of stream while reading body");
                return null;
            }
            totalRead += read;
        }

        return new string(buffer);
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
    /// Writes a message with Content-Length header to the output stream.
    /// </summary>
    private async Task WriteMessageAsync(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        Log($"Sending response: {bytes.Length} bytes");

        await _output.WriteAsync(headerBytes);
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
