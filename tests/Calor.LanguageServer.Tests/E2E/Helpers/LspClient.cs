using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E.Helpers;

/// <summary>Single-consumer policy: unmatched notifications are discarded.</summary>
internal sealed class LspClient : LspProtocolClient
{
    private LspClient(Process process, ITestOutputHelper output)
        : base(process, output, TimeSpan.FromSeconds(10))
    {
        StartReader();
    }

    public static Task<LspClient> StartAsync(ITestOutputHelper output) =>
        Task.FromResult(new LspClient(StartServer(), output));

    public Task<JsonElement> InitializeAsync() =>
        InitializeAsync("file:///calor-lsp-e2e", "calor-lsp-e2e",
            new
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
            });

    private readonly Channel<(string Method, JsonElement Params)> _notifications =
        Channel.CreateUnbounded<(string Method, JsonElement Params)>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

    protected override async Task<JsonElement> WaitForNotificationAsync(
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

    protected override ValueTask ReceiveNotificationAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken) =>
        _notifications.Writer.WriteAsync((method, parameters), cancellationToken);

    protected override void CompleteNotifications(Exception? error) =>
        _notifications.Writer.TryComplete(error);
}
