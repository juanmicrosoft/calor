using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E.Helpers;

/// <summary>Retains unmatched notifications per method for later document/version waits.</summary>
internal sealed class LspTestClient : LspProtocolClient
{
    private LspTestClient(Process process, ITestOutputHelper output)
        : base(process, output, TimeSpan.FromSeconds(30))
    {
        StartReader();
    }

    public static Task<LspTestClient> StartAsync(ITestOutputHelper output) =>
        Task.FromResult(new LspTestClient(StartServer(), output));

    public Task<JsonElement> InitializeAsync(string workspaceRoot) =>
        InitializeAsync(new Uri(workspaceRoot).AbsoluteUri, "workspace-root",
            new
            {
                workspace = new
                {
                    workspaceFolders = true,
                    configuration = false,
                },
                textDocument = new
                {
                    synchronization = new
                    {
                        dynamicRegistration = false,
                        didSave = true,
                    },
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
                    semanticTokens = new
                    {
                        dynamicRegistration = false,
                        requests = new { full = true },
                        tokenTypes = Array.Empty<string>(),
                        tokenModifiers = Array.Empty<string>(),
                        formats = new[] { "relative" },
                    },
                },
            });

    private readonly Dictionary<
        string, List<JsonElement>> _notificationBuffer = new(StringComparer.Ordinal);
    private readonly Channel<(string Method, JsonElement Params)> _notificationSignal =
        Channel.CreateUnbounded<(string Method, JsonElement Params)>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true,
            });

    private readonly object _bufferGate = new();

    protected override async Task<JsonElement> WaitForNotificationAsync(
        string method, Func<JsonElement, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        while (true)
        {
            lock (_bufferGate)
            {
                if (_notificationBuffer.TryGetValue(method, out var buffered))
                {
                    for (var index = 0; index < buffered.Count; index++)
                    {
                        var parameters = buffered[index];
                        if (predicate(parameters))
                        {
                            buffered.RemoveAt(index);
                            return parameters;
                        }
                    }
                }
            }

            await _notificationSignal.Reader.ReadAsync(timeout.Token);
        }
    }

    protected override ValueTask ReceiveNotificationAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        lock (_bufferGate)
        {
            if (!_notificationBuffer.TryGetValue(method, out var buffer))
            {
                buffer = new List<JsonElement>();
                _notificationBuffer.Add(method, buffer);
            }
            buffer.Add(parameters);
        }
        return _notificationSignal.Writer.WriteAsync((method, parameters), cancellationToken);
    }

    protected override void CompleteNotifications(Exception? error) =>
        _notificationSignal.Writer.TryComplete(error);
}
