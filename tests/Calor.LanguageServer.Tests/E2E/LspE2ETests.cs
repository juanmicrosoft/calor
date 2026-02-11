using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods - these are test methods

namespace Calor.LanguageServer.Tests.E2E;

/// <summary>
/// End-to-end tests that start the actual LSP server process and communicate via JSON-RPC.
/// </summary>
public class LspE2ETests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Process? _serverProcess;
    private StreamWriter? _serverInput;
    private StreamReader? _serverOutput;
    private int _messageId = 0;

    public LspE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task StartServerAsync()
    {
        // Find the LSP server DLL
        var solutionDir = FindSolutionDirectory();
        var lspServerPath = Path.Combine(solutionDir, "src", "Calor.LanguageServer", "bin", "Debug", "net8.0", "calor-lsp.dll");

        if (!File.Exists(lspServerPath))
        {
            throw new InvalidOperationException($"LSP server not found at {lspServerPath}. Please build the solution first.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{lspServerPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _serverProcess = Process.Start(startInfo);
        if (_serverProcess == null)
        {
            throw new InvalidOperationException("Failed to start LSP server process");
        }

        _serverInput = _serverProcess.StandardInput;
        _serverOutput = _serverProcess.StandardOutput;

        // Capture stderr for debugging
        _serverProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _output.WriteLine($"Server stderr: {e.Data}");
            }
        };
        _serverProcess.BeginErrorReadLine();

        // Give the server time to start - increased for reliability
        await Task.Delay(2000);
    }

    private static string FindSolutionDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.sln")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find solution directory");
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object? @params = null)
    {
        var id = Interlocked.Increment(ref _messageId);
        var message = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };

        var json = JsonSerializer.Serialize(message);
        var contentLength = Encoding.UTF8.GetByteCount(json);
        var header = $"Content-Length: {contentLength}\r\n\r\n";

        _output.WriteLine($"Content-Length: {contentLength}, JSON length: {json.Length}");
        _output.WriteLine($"Sending: {json}");

        await _serverInput!.WriteAsync(header);
        await _serverInput!.WriteAsync(json);
        await _serverInput!.FlushAsync();

        // Read responses until we get one with matching ID (skip notifications)
        var maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var response = await ReadResponseAsync();
            _output.WriteLine($"Received: {response}");

            var doc = JsonDocument.Parse(response);

            // Check if this is a response (has id) or a notification (no id)
            if (doc.RootElement.TryGetProperty("id", out var responseId))
            {
                // It's a response - check if it matches our request
                if (responseId.GetInt32() == id)
                {
                    return doc;
                }
            }
            // If it's a notification (no id), continue reading
        }

        throw new InvalidOperationException($"Did not receive response for request {id} after {maxAttempts} attempts");
    }

    private async Task SendNotificationAsync(string method, object? @params = null)
    {
        var message = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };

        var json = JsonSerializer.Serialize(message);
        var contentLength = Encoding.UTF8.GetByteCount(json);
        var header = $"Content-Length: {contentLength}\r\n\r\n";

        _output.WriteLine($"Sending notification: {json}");

        await _serverInput!.WriteAsync(header);
        await _serverInput!.WriteAsync(json);
        await _serverInput!.FlushAsync();
    }

    private async Task<string> ReadResponseAsync(int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            // Read headers
            var headerLine = await _serverOutput!.ReadLineAsync(cts.Token);
            if (headerLine == null)
            {
                throw new InvalidOperationException("Server closed connection");
            }

            // Parse Content-Length
            var contentLengthPrefix = "Content-Length: ";
            if (!headerLine.StartsWith(contentLengthPrefix))
            {
                throw new InvalidOperationException($"Expected Content-Length header, got: {headerLine}");
            }

            var contentLength = int.Parse(headerLine.Substring(contentLengthPrefix.Length));

            // Read empty line
            await _serverOutput.ReadLineAsync(cts.Token);

            // Read content
            var buffer = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await _serverOutput.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cts.Token);
                if (read == 0)
                {
                    throw new InvalidOperationException("Server closed connection while reading content");
                }
                totalRead += read;
            }

            return new string(buffer);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Timed out after {timeoutMs}ms waiting for server response");
        }
    }

    [Fact]
    public async Task InitializeAsync_ReturnsCapabilities()
    {
        await StartServerAsync();

        // Note: Using empty capabilities due to OmniSharp library issue with textDocument processing
        var response = await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = "file:///test",
            capabilities = new { }
        });

        Assert.True(response.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("capabilities", out var capabilities));
        Assert.True(capabilities.TryGetProperty("textDocumentSync", out _));
    }

    [Fact]
    public async Task TextDocumentOpenAsync_DoesNotCrash()
    {
        await StartServerAsync();

        // Initialize first (using empty capabilities due to OmniSharp issue)
        await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = "file:///test",
            capabilities = new { }
        });

        // Send initialized notification
        await SendNotificationAsync("initialized", new { });

        // Open a document
        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = "file:///test/test.calr",
                languageId = "calor",
                version = 1,
                text = @"§M{m001:TestModule}
§F{f001:Test}
§R 0
§/F{f001}
§/M{m001}"
            }
        });

        // Give it time to process
        await Task.Delay(500);

        // Server should still be running
        Assert.False(_serverProcess!.HasExited);
    }

    [Fact(Skip = "Requires proper client capabilities which cause OmniSharp to hang")]
    public async Task HoverAsync_ReturnsInfo()
    {
        await StartServerAsync();

        // Initialize (using empty capabilities due to OmniSharp issue with textDocument processing)
        // Note: With empty capabilities, hover capability won't be advertised, but protocol still works
        var initResponse = await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = "file:///test",
            capabilities = new { }
        });
        _output.WriteLine($"Init result: {initResponse.RootElement}");
        await SendNotificationAsync("initialized", new { });

        // Open document
        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = "file:///test/test.calr",
                languageId = "calor",
                version = 1,
                text = @"§M{m001:TestModule}
§F{f001:Add}
§I{i32:a}
§O{i32}
§R a
§/F{f001}
§/M{m001}"
            }
        });

        await Task.Delay(500);

        // Request hover
        var response = await SendRequestAsync("textDocument/hover", new
        {
            textDocument = new { uri = "file:///test/test.calr" },
            position = new { line = 1, character = 8 } // Should be on "Add"
        });

        Assert.True(response.RootElement.TryGetProperty("result", out var result));
        // Result might be null if hover position doesn't match, but server shouldn't error
    }

    [Fact(Skip = "Requires proper client capabilities which cause OmniSharp to hang")]
    public async Task CompletionAsync_ReturnsItems()
    {
        await StartServerAsync();

        // Initialize (using empty capabilities due to OmniSharp issue)
        await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = "file:///test",
            capabilities = new { }
        });
        await SendNotificationAsync("initialized", new { });

        // Open document
        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = "file:///test/test.calr",
                languageId = "calor",
                version = 1,
                text = "§"
            }
        });

        await Task.Delay(500);

        // Request completion
        var response = await SendRequestAsync("textDocument/completion", new
        {
            textDocument = new { uri = "file:///test/test.calr" },
            position = new { line = 0, character = 1 } // Right after §
        });

        Assert.True(response.RootElement.TryGetProperty("result", out var result));
        // Should return completion items for tags
    }

    [Fact]
    public async Task ShutdownAsync_ExitsGracefully()
    {
        await StartServerAsync();

        // Initialize (using empty capabilities due to OmniSharp issue)
        await SendRequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = "file:///test",
            capabilities = new { }
        });
        await SendNotificationAsync("initialized", new { });

        // Shutdown
        var response = await SendRequestAsync("shutdown");
        Assert.True(response.RootElement.TryGetProperty("result", out _));

        // Exit
        await SendNotificationAsync("exit");

        // Wait for process to exit
        var exited = _serverProcess!.WaitForExit(5000);
        Assert.True(exited);
        Assert.Equal(0, _serverProcess.ExitCode);
    }

    public void Dispose()
    {
        try
        {
            _serverInput?.Dispose();
            _serverOutput?.Dispose();
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess.Dispose();
            }
        }
        catch
        {
            // Ignore dispose errors
        }
    }
}
