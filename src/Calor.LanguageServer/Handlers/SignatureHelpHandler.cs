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

        // #1618: parameter refinements are the preconditions.
        var preconditions = RefinementPreconditions.Effective(func.Parameters, func.Preconditions);
        if (preconditions.Count > 0)
        {
            sb.AppendLine("**Preconditions:**");
            foreach (var pre in preconditions)
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

/// <summary>
/// The native call that encloses a cursor: the callee as written (for example <c>Add</c> or
/// <c>Math.Max</c>) and the zero-based index of the argument the cursor is in.
/// </summary>
internal sealed record NativeCallContext(string Callee, int ArgumentIndex)
{
    /// <summary>The callee's last name segment (<c>Max</c> for <c>Math.Max</c>).</summary>
    public string Name => Callee[(Callee.LastIndexOf('.') + 1)..];

    // Words that can sit directly before '(' without being a call.
    private static readonly HashSet<string> NotCallees = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "each", "do", "match", "case", "when", "requires", "proof",
        "invariant", "return", "yield", "throw", "lock", "using", "set", "let", "var", "in",
        "else", "not", "and", "or", "is", "as", "await", "func", "module", "class", "new",
    };

    /// <summary>
    /// Finds the innermost unclosed call around <paramref name="offset"/>. Native calls are
    /// written <c>name(args)</c> with no space before the parenthesis, so a parenthesis after
    /// whitespace (an <c>if (...)</c> condition or a <c>func Name (...)</c> header) or after a
    /// keyword is grouping, not a call. String and character literals and line comments are
    /// skipped. Returns null when the cursor is not inside a call's argument list.
    /// </summary>
    public static NativeCallContext? Find(string source, int offset)
    {
        offset = Math.Clamp(offset, 0, source.Length);
        var frames = new Stack<(char Open, string? Callee, int Commas)>();

        for (var i = 0; i < offset; i++)
        {
            var c = source[i];
            switch (c)
            {
                case '/' when i + 1 < source.Length && source[i + 1] == '/':
                    i = SkipTo(source, i, '\n', offset) - 1;
                    break;
                case '"' or '\'':
                    i = SkipLiteral(source, i, c, offset);
                    break;
                case '(':
                    frames.Push(('(', CalleeBefore(source, i), 0));
                    break;
                case '[' or '{':
                    frames.Push((c, null, 0));
                    break;
                case ')' or ']' or '}':
                    var open = c switch { ')' => '(', ']' => '[', _ => '{' };
                    // Pop to the matching opener; an unbalanced closer is left alone.
                    if (frames.Any(frame => frame.Open == open))
                    {
                        while (frames.Pop().Open != open) { }
                    }
                    break;
                case ',' when frames.Count > 0 && frames.Peek().Open == '(':
                    var top = frames.Pop();
                    frames.Push(top with { Commas = top.Commas + 1 });
                    break;
            }
        }

        foreach (var frame in frames)
        {
            if (frame.Callee != null)
                return new NativeCallContext(frame.Callee, frame.Commas);
        }
        return null;
    }

    private static int SkipTo(string source, int start, char end, int limit)
    {
        var index = source.IndexOf(end, start);
        return index < 0 || index > limit ? limit : index;
    }

    /// <summary>Returns the index of the literal's closing quote (or the limit when unclosed).</summary>
    private static int SkipLiteral(string source, int start, char quote, int limit)
    {
        for (var i = start + 1; i < limit; i++)
        {
            if (source[i] == '\\')
                i++;
            else if (source[i] == quote || source[i] == '\n')
                return i;
        }
        return limit;
    }

    /// <summary>The callee written directly before the '(' at <paramref name="paren"/>, if any.</summary>
    private static string? CalleeBefore(string source, int paren)
    {
        var end = paren;
        // Explicit generic arguments: Name<T, U>(...)
        if (end > 0 && source[end - 1] == '>')
        {
            var depth = 0;
            var i = end - 1;
            for (; i >= 0; i--)
            {
                if (source[i] == '>') depth++;
                else if (source[i] == '<' && --depth == 0) break;
                else if (source[i] is '\n' or '(' or ')') return null;
            }
            if (i < 0) return null;
            end = i;
        }

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] is '_' or '.' or '@'
                   || (source[start - 1] == '?' && source[start] == '.')))
            start--;
        if (start == end)
            return null;

        var callee = source[start..end].Replace("@", "").Replace("?.", ".");
        if (callee.StartsWith('.') || callee.EndsWith('.') || char.IsDigit(callee[0]) || NotCallees.Contains(callee))
            return null;
        return callee;
    }
}
