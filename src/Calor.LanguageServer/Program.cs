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
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .WithServices(services =>
                {
                    services.AddSingleton(provider => new WorkspaceState(
                        logger: provider.GetRequiredService<ILogger<WorkspaceState>>()));
                })
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<FormattingHandler>()
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<RenameHandler>()
                .WithHandler<WorkspaceSymbolHandler>()
                .WithHandler<SemanticTokensHandler>()
                .OnInitialize(async (server, request, token) =>
                {
                    var workspace =
                        server.Services.GetRequiredService<WorkspaceState>();
                    var workspaceFolders = request.WorkspaceFolders?
                        .Select(folder => folder.Uri.ToUri())
                        .ToArray();
                    if (workspaceFolders is { Length: > 0 })
                    {
                        await workspace.ConfigureWorkspaceRootsAsync(
                            workspaceFolders,
                            token).ConfigureAwait(false);
                    }
                    else if (request.RootUri is { } rootUri)
                    {
                        await workspace.ConfigureWorkspaceRootAsync(
                            rootUri.ToUri(),
                            token).ConfigureAwait(false);
                    }

                    // Register TextDocumentSyncHandler which needs the server reference
                    server.Register(opts =>
                    {
                        opts.AddHandler(new TextDocumentSyncHandler(workspace, server));
                    });
                });
        }).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);

        return 0;
    }
}
