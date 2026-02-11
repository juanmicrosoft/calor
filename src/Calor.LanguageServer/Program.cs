using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Calor.LanguageServer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var workspace = new WorkspaceState();

        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .WithServices(services =>
                {
                    services.AddSingleton(workspace);
                })
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<FormattingHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<SignatureHelpHandler>()
                .OnInitialize((server, request, token) =>
                {
                    // Register TextDocumentSyncHandler which needs the server reference
                    server.Register(opts =>
                    {
                        opts.AddHandler(new TextDocumentSyncHandler(workspace, server));
                    });
                    return Task.CompletedTask;
                })
        ).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);

        return 0;
    }
}
