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

        // Rename remains experimental (#765). Formatting is registered
        // unconditionally because its whole-document edit now passes the same
        // lossless semantic and generated-C# gates as CLI writes (#760).
        var experimentalRename =
            Environment.GetEnvironmentVariable("CALOR_LSP_EXPERIMENTAL") is "1" or "true";

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
                    services.AddSingleton(workspace);
                })
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<CompletionHandler>()
                .WithHandler<CodeActionHandler>()
                .WithHandler<FormattingHandler>()
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<WorkspaceSymbolHandler>()
                .WithHandler<SemanticTokensHandler>()
                .OnInitialize((server, request, token) =>
                {
                    var workspaceFolders = request.WorkspaceFolders?
                        .Select(folder => folder.Uri.ToUri())
                        .ToArray();
                    if (workspaceFolders is { Length: > 0 })
                    {
                        workspace.ConfigureWorkspaceRoots(workspaceFolders);
                    }
                    else if (request.RootUri is { } rootUri)
                    {
                        workspace.ConfigureWorkspaceRoot(rootUri.ToUri());
                    }

                    // Register TextDocumentSyncHandler which needs the server reference
                    server.Register(opts =>
                    {
                        opts.AddHandler(new TextDocumentSyncHandler(workspace, server));
                    });
                    return Task.CompletedTask;
                });

            if (experimentalRename)
            {
                options.WithHandler<RenameHandler>();
            }
        }).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);

        return 0;
    }
}
