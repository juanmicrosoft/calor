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

        // W1 Slice 2 (T3, kickoff §1.4): the #793 release policy holds LSP
        // formatting and rename disabled — formatting applies the CalorFormatter's
        // id-rewriting machinery as whole-document edits (#760), and rename lacks
        // exact-span indexing (#765). Both register only under an explicit
        // experimental opt-in; every read-only handler stays available.
        var experimentalWriteHandlers =
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
                .WithHandler<SignatureHelpHandler>()
                .WithHandler<ReferencesHandler>()
                .WithHandler<WorkspaceSymbolHandler>()
                .WithHandler<SemanticTokensHandler>()
                .OnInitialize((server, request, token) =>
                {
                    // Register TextDocumentSyncHandler which needs the server reference
                    server.Register(opts =>
                    {
                        opts.AddHandler(new TextDocumentSyncHandler(workspace, server));
                    });
                    return Task.CompletedTask;
                });

            if (experimentalWriteHandlers)
            {
                options
                    .WithHandler<FormattingHandler>()
                    .WithHandler<RenameHandler>();
            }
        }).ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);

        return 0;
    }
}
