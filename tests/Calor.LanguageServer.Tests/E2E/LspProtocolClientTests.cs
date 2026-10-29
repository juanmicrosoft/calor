using Calor.LanguageServer.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.E2E;

public sealed class LspProtocolClientTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CancelledRequest_DoesNotBlockSubsequentRequestsAsync()
    {
        await using var client = await LspClient.StartAsync(output);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RequestResultAsync("initialize", cancellationToken: cancellation.Token));

        var initialized = await client.InitializeAsync();
        Assert.True(initialized.TryGetProperty("capabilities", out _));
        await client.ShutdownAsync();
    }
}
