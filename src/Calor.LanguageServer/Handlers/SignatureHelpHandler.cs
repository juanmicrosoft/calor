using Calor.Compiler.Ast;
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
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state?.Ast == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        var offset = PositionConverter.ToOffset(request.Position, state.Source);

        // Find if we're inside a function call
        var callContext = FindCallContext(state.Source, offset);
        if (callContext == null)
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        // Find the function definition
        var func = SymbolFinder.FindFunction(state.Ast, callContext.FunctionName);
        if (func == null)
        {
            // Try to find a method in classes
            var method = FindMethodInClasses(state.Ast, callContext.FunctionName);
            if (method != null)
            {
                return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(method, callContext.ArgumentIndex));
            }
            return Task.FromResult<SignatureHelp?>(null);
        }

        return Task.FromResult<SignatureHelp?>(BuildSignatureHelp(func, callContext.ArgumentIndex));
    }

    private static SignatureHelp BuildSignatureHelp(FunctionNode func, int activeParameter)
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
            Label = BuildSignatureLabel(func),
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

    private static SignatureHelp BuildSignatureHelp(MethodNode method, int activeParameter)
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
            Label = BuildSignatureLabel(method),
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

    private static string BuildSignatureLabel(FunctionNode func)
    {
        var parameters = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = func.Output?.TypeName ?? "void";
        return $"{func.Name}({parameters}) -> {returnType}";
    }

    private static string BuildSignatureLabel(MethodNode method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = method.Output?.TypeName ?? "void";
        return $"{method.Name}({parameters}) -> {returnType}";
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

    private static CallContext? FindCallContext(string source, int offset)
    {
        // Look backwards for §C{FunctionName}
        var searchStart = Math.Max(0, offset - 200);
        var searchText = source.Substring(searchStart, offset - searchStart);

        // Find the last §C{ pattern
        var callIndex = searchText.LastIndexOf("§C{", StringComparison.Ordinal);
        if (callIndex < 0)
            return null;

        // Extract function name
        var afterCall = searchText.Substring(callIndex + 3);
        var closeBrace = afterCall.IndexOf('}');
        if (closeBrace < 0)
            return null;

        var functionName = afterCall.Substring(0, closeBrace);

        // Count §A markers to determine which argument we're on
        var afterFunctionName = afterCall.Substring(closeBrace + 1);
        var argumentIndex = CountArgumentMarkers(afterFunctionName);

        return new CallContext(functionName, argumentIndex);
    }

    private static int CountArgumentMarkers(string text)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("§A", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 2;
        }
        return count;
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            TriggerCharacters = new Container<string>("{", " "),
            RetriggerCharacters = new Container<string>("§")
        };
    }

    private sealed class CallContext
    {
        public string FunctionName { get; }
        public int ArgumentIndex { get; }

        public CallContext(string functionName, int argumentIndex)
        {
            FunctionName = functionName;
            ArgumentIndex = argumentIndex;
        }
    }
}
