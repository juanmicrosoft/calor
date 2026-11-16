using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles signature help requests for function parameter hints.
/// </summary>
public sealed class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly WorkspaceState _workspace;

    public SignatureHelpHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
    {
        var workspace = _workspace.CaptureSnapshot();
        var document = workspace.GetDocument(request.TextDocument.Uri);
        var snapshot = document?.Analysis;
        if (document == null || snapshot?.Ast == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        var offset = PositionConverter.ToOffset(request.Position, snapshot.Source);
        if (Calor.Compiler.Parsing.RawCSharpFrame.FindAtOffset(snapshot.Source, offset) != null)
            return Task.FromResult<SignatureHelp?>(null);

        // Find the native call (name(args)) around the cursor.
        var callContext = NativeCallContext.Find(
            Calor.Compiler.Parsing.RawCSharpFrame.MaskForAnalysis(snapshot.Source), offset);
        if (callContext == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        var resolvedSymbol = SymbolFinder.FindResolvedCall(
            snapshot.BoundModule,
            offset);
        if (resolvedSymbol != null
            && string.Equals(resolvedSymbol.Name, callContext.Name, StringComparison.Ordinal))
        {
            return Task.FromResult<SignatureHelp?>(
                BuildSignatureHelp(resolvedSymbol, callContext.ArgumentIndex));
        }

        var boundCall = SymbolFinder.FindBoundCallAtOffset(
            snapshot.BoundModule,
            offset,
            callContext.Callee);
        var projectCall = _workspace.ResolveProjectCall(
            workspace,
            document,
            boundCall);
        if (projectCall.Symbol != null)
        {
            return Task.FromResult<SignatureHelp?>(
                BuildSignatureHelp(projectCall.Symbol, callContext.ArgumentIndex));
        }

        if (boundCall != null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        // Fall back to declarations in this file, by name.
        var func = SymbolFinder.FindFunction(snapshot.Ast, callContext.Name);
        if (func == null)
        {
            var method = FindMethodInClasses(snapshot.Ast, callContext.Name);
            if (method != null)
            {
                return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(method, callContext.ArgumentIndex, snapshot.Ast));
            }
            return Task.FromResult<SignatureHelp?>(null);
        }

        return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(func, callContext.ArgumentIndex, snapshot.Ast));
    }

    private static SignatureHelp BuildSignatureHelp(FunctionNode func, int activeParameter, ModuleNode module)
    {
        var parameters = func.Parameters.Select(p => new ParameterInformation
        {
            Label = $"{p.Name}: {p.TypeName}",
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"**{p.Name}**: `{p.TypeName}`"
            })
        }).ToList();

        var signature = new SignatureInformation
        {
            Label = BuildSignatureLabel(func, module),
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = BuildFunctionDocumentation(func)
            }),
            Parameters = new Container<ParameterInformation>(parameters),
            ActiveParameter = Math.Min(activeParameter, parameters.Count - 1)
        };

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signature),
            ActiveSignature = 0,
            ActiveParameter = activeParameter
        };
    }

    private static SignatureHelp BuildSignatureHelp(
        FunctionSymbol function,
        int activeParameter)
    {
        var parameters = function.Parameters.Select(parameter => new ParameterInformation
        {
            Label = $"{parameter.Name}: {parameter.TypeName}",
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"**{parameter.Name}**: `{parameter.TypeName}`"
            })
        }).ToList();

        var signature = new SignatureInformation
        {
            // Each parameter label must be a substring of the signature label so the client
            // can highlight the active parameter.
            Label = $"{function.DisplaySignature[..function.DisplaySignature.IndexOf('(')]}("
                + string.Join(", ", function.Parameters.Select(parameter => $"{parameter.Name}: {parameter.TypeName}"))
                + $") → {function.ReturnType}",
            Parameters = new Container<ParameterInformation>(parameters),
            ActiveParameter = parameters.Count == 0
                ? 0
                : Math.Min(activeParameter, parameters.Count - 1)
        };

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signature),
            ActiveSignature = 0,
            ActiveParameter = activeParameter
        };
    }

    private static SignatureHelp BuildSignatureHelp(MethodNode method, int activeParameter, ModuleNode module)
    {
        var parameters = method.Parameters.Select(p => new ParameterInformation
        {
            Label = $"{p.Name}: {p.TypeName}",
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = $"**{p.Name}**: `{p.TypeName}`"
            })
        }).ToList();

        var signature = new SignatureInformation
        {
            Label = BuildSignatureLabel(method, module),
            Documentation = new StringOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = BuildMethodDocumentation(method)
            }),
            Parameters = new Container<ParameterInformation>(parameters),
            ActiveParameter = Math.Min(activeParameter, parameters.Count - 1)
        };

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signature),
            ActiveSignature = 0,
            ActiveParameter = activeParameter
        };
    }

    private static string BuildSignatureLabel(FunctionNode func, ModuleNode module)
    {
        var parameters = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = Compiler.Analysis.ReturnShape.GetAsyncReturnTypes(
            func.Output?.TypeName ?? "void", func.IsAsync, module,
            Compiler.Analysis.ReturnShape.GetCallableTypeParameters(func, module)
                .Select(parameter => parameter.Name)).CallableType;
        return $"{func.Name}({parameters}) → {returnType}";
    }

    private static string BuildSignatureLabel(MethodNode method, ModuleNode module)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = Compiler.Analysis.ReturnShape.GetAsyncReturnTypes(
            method.Output?.TypeName ?? "void", method.IsAsync, module,
            Compiler.Analysis.ReturnShape.GetCallableTypeParameters(method, module)
                .Select(parameter => parameter.Name)).CallableType;
        return $"{method.Name}({parameters}) → {returnType}";
    }

    private static string BuildFunctionDocumentation(FunctionNode func)
    {
        var sb = new System.Text.StringBuilder();

        if (func.IsAsync)
        {
            sb.AppendLine("*Async function*\n");
        }

        if (func.Preconditions.Count > 0)
        {
            sb.AppendLine("**Preconditions:**");
            foreach (var pre in func.Preconditions)
            {
                sb.AppendLine("- Contract condition");
            }
        }

        if (func.Postconditions.Count > 0)
        {
            sb.AppendLine("**Postconditions:**");
            foreach (var post in func.Postconditions)
            {
                sb.AppendLine("- Contract condition");
            }
        }

        if (func.Effects?.Effects.Count > 0)
        {
            sb.AppendLine("**Effects:**");
            foreach (var effect in func.Effects.Effects)
            {
                sb.AppendLine($"- {effect.Key}: {effect.Value}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildMethodDocumentation(MethodNode method)
    {
        var sb = new System.Text.StringBuilder();

        var modifiers = new List<string>();
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAsync) modifiers.Add("async");

        if (modifiers.Count > 0)
        {
            sb.AppendLine($"*{string.Join(", ", modifiers)}*\n");
        }

        return sb.ToString().TrimEnd();
    }

    private static MethodNode? FindMethodInClasses(ModuleNode ast, string methodName)
    {
        foreach (var cls in ast.Classes)
        {
            var method = cls.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method != null)
                return method;
        }
        return null;
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            TriggerCharacters = new Container<string>("(", ","),
            RetriggerCharacters = new Container<string>(")")
        };
    }
}
