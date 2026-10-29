using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E.Helpers;

/// <summary>Shared process lifetime and JSON-RPC transport; subclasses own notification retention.</summary>
internal abstract class LspProtocolClient : IAsyncDisposable
{
    internal enum JsonRpcMessageKind
    {
        Invalid,
        Request,
        Notification,
        Response,
    }

    private readonly ITestOutputHelper _output;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _outputStream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<
        string,
        TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _readerCancellation = new();
    private Task _readerTask = Task.CompletedTask;
    private int _messageId;
    private bool _shutdown;

    protected LspProtocolClient(Process process, ITestOutputHelper output, TimeSpan requestTimeout)
    {
        RequestTimeout = requestTimeout;
        _process = process;
        _output = output;
        _input = process.StandardInput.BaseStream;
        _outputStream = process.StandardOutput.BaseStream;
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                output.WriteLine($"server stderr: {eventArgs.Data}");
        };
        process.BeginErrorReadLine();
    }

    public int ProcessId => _process.Id;

    protected static Process StartServer()
    {
        var serverPath = Path.Combine(AppContext.BaseDirectory, "calor-lsp.dll");
        if (!File.Exists(serverPath))
        {
            throw new InvalidOperationException(
                $"LSP server not found at '{serverPath}'. Build the test project first.");
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { serverPath },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start the LSP server.");
        return process;
    }

    protected async Task<JsonElement> InitializeAsync(
        string rootUri, string workspaceName, object capabilities)
    {
        var result = await RequestResultAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri,
            capabilities,
            workspaceFolders = new[] { new { uri = rootUri, name = workspaceName } },
        });
        await SendNotificationAsync("initialized", new { });
        return result;
    }

    public Task OpenAsync(string uri, string text, int version) =>
        SendNotificationAsync(
            "textDocument/didOpen",
            new
            {
                textDocument = new
                {
                    uri,
                    languageId = "calor",
                    version,
                    text,
                },
            });

    public Task ChangeAsync(string uri, string text, int version) =>
        SendNotificationAsync(
            "textDocument/didChange",
            new
            {
                textDocument = new { uri, version },
                contentChanges = new[] { new { text } },
            });

    public async Task<JsonElement> WaitForDiagnosticsAsync(
        string uri,
        int version)
    {
        var message = await WaitForNotificationAsync(
            "textDocument/publishDiagnostics",
            parameters =>
                parameters.GetProperty("uri").GetString() == uri
                && parameters.TryGetProperty(
                    "version",
                    out var publishedVersion)
                && publishedVersion.GetInt32() == version);
        return message.GetProperty("diagnostics");
    }

    public async Task<JsonElement> RequestResultAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _messageId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var idKey = $"n:{id}";
        if (!_pending.TryAdd(idKey, completion))
            throw new InvalidOperationException($"Duplicate JSON-RPC id {id}.");

        try
        {
            await WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters,
            }, cancellationToken);
            var response = await completion.Task.WaitAsync(RequestTimeout, cancellationToken);
            if (response.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} failed: {error.GetRawText()}");
            }
            return response.TryGetProperty("result", out var result)
                ? result.Clone()
                : default;
        }
        finally
        {
            _pending.TryRemove(idKey, out _);
        }
    }

    public Task SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default) =>
        WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
        }, cancellationToken);

    public async Task ShutdownAsync()
    {
        if (_shutdown)
            return;
        _shutdown = true;
        await RequestResultAsync("shutdown");
        await SendNotificationAsync("exit");
        await _process.WaitForExitAsync().WaitAsync(RequestTimeout);
        Assert.Equal(0, _process.ExitCode);
        await _readerTask.WaitAsync(RequestTimeout);
    }

    protected TimeSpan RequestTimeout { get; }

    protected void StartReader() => _readerTask = ReadMessagesAsync();

    protected abstract Task<JsonElement> WaitForNotificationAsync(
        string method, Func<JsonElement, bool> predicate);

    protected abstract ValueTask ReceiveNotificationAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken);

    protected abstract void CompleteNotifications(Exception? error);

    private async Task ReadMessagesAsync()
    {
        Exception? readerError = null;
        try
        {
            while (!_readerCancellation.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(_readerCancellation.Token);
                if (message == null)
                    break;
                using (message)
                {
                    var root = message.RootElement;
                    switch (ClassifyMessage(root))
                    {
                        case JsonRpcMessageKind.Request:
                            var requestId = root.GetProperty("id");
                            var methodName = root.GetProperty("method").GetString()
                                ?? throw new InvalidDataException(
                                    "JSON-RPC request method was not a string.");
                            await WriteMessageAsync(CreateServerResponse(
                                requestId,
                                GetServerRequestResult(methodName)));
                            break;

                        case JsonRpcMessageKind.Notification:
                            var method = root.GetProperty("method").GetString()
                                ?? throw new InvalidDataException(
                                    "JSON-RPC notification method was not a string.");
                            var parameters = root.TryGetProperty(
                                "params",
                                out var parameterElement)
                                ? parameterElement.Clone()
                                : JsonSerializer.SerializeToElement(new { });
                            await ReceiveNotificationAsync(
                                method, parameters,
                                _readerCancellation.Token);
                            break;

                        case JsonRpcMessageKind.Response:
                            var responseId = root.GetProperty("id");
                            var responseKey = GetIdKey(responseId);
                            if (responseKey != null
                                && _pending.TryGetValue(
                                    responseKey,
                                    out var pending))
                            {
                                pending.TrySetResult(root.Clone());
                            }
                            break;

                        default:
                            throw new InvalidDataException(
                                $"Invalid JSON-RPC message: {root.GetRawText()}");
                    }
                }
            }
        }

        catch (OperationCanceledException) when (_readerCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            readerError = exception;
            _output.WriteLine($"LSP reader failed: {exception}");
            foreach (var pending in _pending.Values)
                pending.TrySetException(exception);
        }
        finally
        {
            CompleteNotifications(readerError);
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(
                    new EndOfStreamException("The LSP server closed its output."));
            }
        }
    }

    internal static JsonRpcMessageKind ClassifyMessage(JsonElement message)
    {
        if (message.TryGetProperty("method", out var method)
            && method.ValueKind == JsonValueKind.String)
        {
            return message.TryGetProperty("id", out _)
                ? JsonRpcMessageKind.Request
                : JsonRpcMessageKind.Notification;
        }

        return message.TryGetProperty("id", out _)
            ? JsonRpcMessageKind.Response
            : JsonRpcMessageKind.Invalid;
    }

    internal static JsonElement CreateServerResponse(
        JsonElement id,
        object? result) =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id.Clone(),
                ["result"] = result,
            });

    private static string? GetIdKey(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.Number => $"n:{id.GetRawText()}",
        JsonValueKind.String => $"s:{id.GetString()}",
        _ => null,
    };

    private static object? GetServerRequestResult(string method) =>
        method == "workspace/configuration"
            ? Array.Empty<object?>()
            : null;

    private async Task<JsonDocument?> ReadMessageAsync(
        CancellationToken cancellationToken)
    {
        int? contentLength = null;
        while (true)
        {
            var header = await ReadLineAsync(_outputStream, cancellationToken);
            if (header == null)
                return null;
            if (header.Length == 0)
                break;
            const string prefix = "Content-Length:";
            if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(
                    header[prefix.Length..].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (contentLength is not > 0)
            throw new InvalidDataException("JSON-RPC message omitted Content-Length.");
        var bytes = new byte[contentLength.Value];
        await _outputStream.ReadExactlyAsync(bytes, cancellationToken);
        return JsonDocument.Parse(bytes);
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {bytes.Length}\r\n\r\n");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _readerCancellation.Token);
        timeout.CancelAfter(RequestTimeout);
        await _writeGate.WaitAsync(timeout.Token);
        try
        {
            await _input.WriteAsync(header, timeout.Token);
            await _input.WriteAsync(bytes, timeout.Token);
            await _input.FlushAsync(timeout.Token);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task<string?> ReadLineAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return bytes.Length == 0
                    ? null
                    : Encoding.ASCII.GetString(bytes.ToArray());
            if (buffer[0] == (byte)'\n')
            {
                var line = bytes.ToArray();
                var length = line.Length > 0 && line[^1] == (byte)'\r'
                    ? line.Length - 1
                    : line.Length;
                return Encoding.ASCII.GetString(line, 0, length);
            }
            bytes.WriteByte(buffer[0]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_shutdown && !_process.HasExited)
            {
                try
                {
                    await ShutdownAsync();
                }
                catch (Exception exception)
                {
                    _output.WriteLine($"Graceful LSP shutdown failed: {exception.Message}");
                }
            }
        }
        finally
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(RequestTimeout);
            }
            await _readerCancellation.CancelAsync();
            try
            {
                await _readerTask.WaitAsync(RequestTimeout);
            }
            catch (Exception exception)
            {
                _output.WriteLine($"LSP reader cleanup failed: {exception.Message}");
            }
            _readerCancellation.Dispose();
            _writeGate.Dispose();
            _input.Dispose();
            _outputStream.Dispose();
            _process.Dispose();
        }
    }
}
