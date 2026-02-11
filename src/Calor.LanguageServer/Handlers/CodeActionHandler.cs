using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspTextEdit = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextEdit;
using LspDocumentUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles code action requests (quick fixes).
/// </summary>
public sealed class CodeActionHandler : CodeActionHandlerBase
{
    private readonly WorkspaceState _workspace;

    public CodeActionHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
    {
        // Resolve additional details for a code action if needed
        return Task.FromResult(request);
    }

    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state == null)
        {
            return Task.FromResult<CommandOrCodeActionContainer?>(null);
        }

        var codeActions = new List<CommandOrCodeAction>();

        // Check if there are any diagnostics with fixes at the requested range
        foreach (var diagnostic in state.DiagnosticsWithFixes)
        {
            // Check if the diagnostic overlaps with the requested range
            var diagRange = PositionConverter.ToLspRange(diagnostic.Span, state.Source);
            if (!RangesOverlap(diagRange, request.Range))
                continue;

            // Create a code action for each fix
            var edits = diagnostic.Fix.Edits.Select(e => new LspTextEdit
            {
                Range = new LspRange(
                    new Position(e.StartLine - 1, e.StartColumn - 1),
                    new Position(e.EndLine - 1, e.EndColumn - 1)
                ),
                NewText = e.NewText
            }).ToList();

            var workspaceEdit = new WorkspaceEdit
            {
                Changes = new Dictionary<LspDocumentUri, IEnumerable<LspTextEdit>>
                {
                    [request.TextDocument.Uri] = edits
                }
            };

            var codeAction = new CodeAction
            {
                Title = diagnostic.Fix.Description,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<LspDiagnostic>(
                    DiagnosticConverter.ToLspDiagnostic(
                        new Calor.Compiler.Diagnostics.Diagnostic(
                            diagnostic.Code,
                            diagnostic.Message,
                            diagnostic.Span,
                            diagnostic.Severity,
                            diagnostic.FilePath),
                        state.Source)),
                Edit = workspaceEdit,
                IsPreferred = true
            };

            codeActions.Add(new CommandOrCodeAction(codeAction));
        }

        // Add source actions even without diagnostics
        codeActions.AddRange(GetSourceActions(state, request));

        return Task.FromResult<CommandOrCodeActionContainer?>(
            codeActions.Count > 0 ? new CommandOrCodeActionContainer(codeActions) : null);
    }

    private static IEnumerable<CommandOrCodeAction> GetSourceActions(DocumentState state, CodeActionParams request)
    {
        var actions = new List<CommandOrCodeAction>();

        // Add "Organize imports" action if there are using directives
        if (state.Ast?.Usings.Count > 0)
        {
            // Could add action to sort using directives
        }

        return actions;
    }

    private static bool RangesOverlap(LspRange a, LspRange b)
    {
        // Check if ranges overlap
        if (a.End.Line < b.Start.Line || (a.End.Line == b.Start.Line && a.End.Character <= b.Start.Character))
            return false;
        if (b.End.Line < a.Start.Line || (b.End.Line == a.Start.Line && b.End.Character <= a.Start.Character))
            return false;
        return true;
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix,
                CodeActionKind.Source
            )
        };
    }
}
