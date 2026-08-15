using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E;

public class LspE2ETests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
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

    private sealed class LspClient : IAsyncDisposable
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
        private readonly Channel<(string Method, JsonElement Params)> _notifications =
            Channel.CreateUnbounded<(string Method, JsonElement Params)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                });
        private readonly CancellationTokenSource _readerCancellation = new();
        private readonly Task _readerTask;
        private int _messageId;
        private bool _shutdown;

        private LspClient(Process process, ITestOutputHelper output)
        {
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
            _readerTask = ReadMessagesAsync();
        }

        public int ProcessId => _process.Id;

        public static Task<LspClient> StartAsync(ITestOutputHelper output)
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
            return Task.FromResult(new LspClient(process, output));
        }

        public async Task<JsonElement> InitializeAsync()
        {
            var result = await RequestResultAsync(
                "initialize",
                new
                {
                    processId = Environment.ProcessId,
                    rootUri = "file:///calor-lsp-e2e",
                    capabilities = new
                    {
                        workspace = new
                        {
                            workspaceFolders = true,
                            configuration = false,
                            symbol = new { dynamicRegistration = false },
                        },
                        textDocument = new
                        {
                            synchronization = new
                            {
                                dynamicRegistration = false,
                                didSave = true,
                            },
                            hover = new
                            {
                                dynamicRegistration = false,
                                contentFormat = new[] { "markdown", "plaintext" },
                            },
                            completion = new
                            {
                                dynamicRegistration = false,
                                completionItem = new
                                {
                                    snippetSupport = true,
                                    documentationFormat = new[] { "markdown", "plaintext" },
                                },
                            },
                            definition = new { dynamicRegistration = false },
                            references = new { dynamicRegistration = false },
                            rename = new
                            {
                                dynamicRegistration = false,
                                prepareSupport = false,
                            },
                            publishDiagnostics = new
                            {
                                relatedInformation = true,
                                versionSupport = true,
                            },
                        },
                    },
                    workspaceFolders = new[]
                    {
                        new
                        {
                            uri = "file:///calor-lsp-e2e",
                            name = "calor-lsp-e2e",
                        },
                    },
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
            object? parameters = null)
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
                });
                var response = await completion.Task.WaitAsync(RequestTimeout);
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
            object? parameters = null) =>
            WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
            });

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

        private async Task<JsonElement> WaitForNotificationAsync(
            string method,
            Func<JsonElement, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            await foreach (var notification in _notifications.Reader.ReadAllAsync(
                               timeout.Token))
            {
                if (notification.Method == method
                    && predicate(notification.Params))
                {
                    return notification.Params;
                }
            }

            throw new InvalidOperationException(
                $"Notification '{method}' channel completed before a match arrived.");
        }

        private async Task ReadMessagesAsync()
        {
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
                                await _notifications.Writer.WriteAsync(
                                    (method, parameters),
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
                _output.WriteLine($"LSP reader failed: {exception}");
                foreach (var pending in _pending.Values)
                    pending.TrySetException(exception);
            }
            finally
            {
                _notifications.Writer.TryComplete();
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

        private async Task WriteMessageAsync(object message)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            var header = Encoding.ASCII.GetBytes(
                $"Content-Length: {bytes.Length}\r\n\r\n");
            await _writeGate.WaitAsync().WaitAsync(RequestTimeout);
            try
            {
                await _input.WriteAsync(header).AsTask().WaitAsync(RequestTimeout);
                await _input.WriteAsync(bytes).AsTask().WaitAsync(RequestTimeout);
                await _input.FlushAsync().WaitAsync(RequestTimeout);
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
                catch (Exception)
                {
                }
                _readerCancellation.Dispose();
                _writeGate.Dispose();
                _input.Dispose();
                _outputStream.Dispose();
                _process.Dispose();
            }
        }
    }
}
