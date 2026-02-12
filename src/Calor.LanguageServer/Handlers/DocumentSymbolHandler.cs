using Calor.Compiler.Ast;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles document symbol requests for outline view and navigation.
/// </summary>
public sealed class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly WorkspaceState _workspace;

    public DocumentSymbolHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request,
        CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state?.Ast == null)
        {
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
        }

        var symbols = new List<SymbolInformationOrDocumentSymbol>();
        var ast = state.Ast;
        var source = state.Source;

        // Module symbol
        var moduleSymbol = new DocumentSymbol
        {
            Name = ast.Name,
            Detail = $"module {ast.Id}",
            Kind = SymbolKind.Module,
            Range = PositionConverter.ToLspRange(ast.Span, source),
            SelectionRange = PositionConverter.ToLspRange(ast.Span, source),
            Children = new Container<DocumentSymbol>(GetModuleChildren(ast, source))
        };

        symbols.Add(new SymbolInformationOrDocumentSymbol(moduleSymbol));

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    private static IEnumerable<DocumentSymbol> GetModuleChildren(ModuleNode ast, string source)
    {
        var children = new List<DocumentSymbol>();

        // Functions
        foreach (var func in ast.Functions)
        {
            var funcChildren = new List<DocumentSymbol>();

            // Parameters as children
            foreach (var param in func.Parameters)
            {
                funcChildren.Add(new DocumentSymbol
                {
                    Name = param.Name,
                    Detail = param.TypeName,
                    Kind = SymbolKind.Variable,
                    Range = PositionConverter.ToLspRange(param.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(param.Span, source)
                });
            }

            var detail = BuildFunctionSignature(func);
            children.Add(new DocumentSymbol
            {
                Name = func.Name,
                Detail = detail,
                Kind = func.IsAsync ? SymbolKind.Event : SymbolKind.Function,
                Range = PositionConverter.ToLspRange(func.Span, source),
                SelectionRange = PositionConverter.ToLspRange(func.Span, source),
                Children = funcChildren.Count > 0 ? new Container<DocumentSymbol>(funcChildren) : null
            });
        }

        // Classes
        foreach (var cls in ast.Classes)
        {
            var classChildren = new List<DocumentSymbol>();

            // Fields
            foreach (var field in cls.Fields)
            {
                classChildren.Add(new DocumentSymbol
                {
                    Name = field.Name,
                    Detail = field.TypeName,
                    Kind = SymbolKind.Field,
                    Range = PositionConverter.ToLspRange(field.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(field.Span, source)
                });
            }

            // Properties
            foreach (var prop in cls.Properties)
            {
                classChildren.Add(new DocumentSymbol
                {
                    Name = prop.Name,
                    Detail = prop.TypeName,
                    Kind = SymbolKind.Property,
                    Range = PositionConverter.ToLspRange(prop.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(prop.Span, source)
                });
            }

            // Constructors
            foreach (var ctor in cls.Constructors)
            {
                classChildren.Add(new DocumentSymbol
                {
                    Name = cls.Name,
                    Detail = "constructor",
                    Kind = SymbolKind.Constructor,
                    Range = PositionConverter.ToLspRange(ctor.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(ctor.Span, source)
                });
            }

            // Methods
            foreach (var method in cls.Methods)
            {
                classChildren.Add(new DocumentSymbol
                {
                    Name = method.Name,
                    Detail = BuildMethodSignature(method),
                    Kind = SymbolKind.Method,
                    Range = PositionConverter.ToLspRange(method.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(method.Span, source)
                });
            }

            var classDetail = BuildClassDetail(cls);
            children.Add(new DocumentSymbol
            {
                Name = cls.Name,
                Detail = classDetail,
                Kind = SymbolKind.Class,
                Range = PositionConverter.ToLspRange(cls.Span, source),
                SelectionRange = PositionConverter.ToLspRange(cls.Span, source),
                Children = classChildren.Count > 0 ? new Container<DocumentSymbol>(classChildren) : null
            });
        }

        // Interfaces
        foreach (var iface in ast.Interfaces)
        {
            var ifaceChildren = new List<DocumentSymbol>();

            foreach (var method in iface.Methods)
            {
                ifaceChildren.Add(new DocumentSymbol
                {
                    Name = method.Name,
                    Detail = BuildMethodSignatureDetail(method),
                    Kind = SymbolKind.Method,
                    Range = PositionConverter.ToLspRange(method.Span, source),
                    SelectionRange = PositionConverter.ToLspRange(method.Span, source)
                });
            }

            children.Add(new DocumentSymbol
            {
                Name = iface.Name,
                Detail = "interface",
                Kind = SymbolKind.Interface,
                Range = PositionConverter.ToLspRange(iface.Span, source),
                SelectionRange = PositionConverter.ToLspRange(iface.Span, source),
                Children = ifaceChildren.Count > 0 ? new Container<DocumentSymbol>(ifaceChildren) : null
            });
        }

        // Enums
        foreach (var enumDef in ast.Enums)
        {
            var enumChildren = enumDef.Members.Select(m => new DocumentSymbol
            {
                Name = m.Name,
                Detail = m.Value ?? "",
                Kind = SymbolKind.EnumMember,
                Range = PositionConverter.ToLspRange(m.Span, source),
                SelectionRange = PositionConverter.ToLspRange(m.Span, source)
            }).ToList();

            children.Add(new DocumentSymbol
            {
                Name = enumDef.Name,
                Detail = enumDef.UnderlyingType != null ? $"enum : {enumDef.UnderlyingType}" : "enum",
                Kind = SymbolKind.Enum,
                Range = PositionConverter.ToLspRange(enumDef.Span, source),
                SelectionRange = PositionConverter.ToLspRange(enumDef.Span, source),
                Children = enumChildren.Count > 0 ? new Container<DocumentSymbol>(enumChildren) : null
            });
        }

        // Delegates
        foreach (var del in ast.Delegates)
        {
            children.Add(new DocumentSymbol
            {
                Name = del.Name,
                Detail = "delegate",
                Kind = SymbolKind.Function,
                Range = PositionConverter.ToLspRange(del.Span, source),
                SelectionRange = PositionConverter.ToLspRange(del.Span, source)
            });
        }

        return children;
    }

    private static string BuildFunctionSignature(FunctionNode func)
    {
        var parameters = string.Join(", ", func.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = func.Output?.TypeName ?? "void";
        var asyncPrefix = func.IsAsync ? "async " : "";
        return $"{asyncPrefix}({parameters}) -> {returnType}";
    }

    private static string BuildMethodSignature(MethodNode method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = method.Output?.TypeName ?? "void";
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsAsync) modifiers.Add("async");
        var modifierStr = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";
        return $"{modifierStr}({parameters}) -> {returnType}";
    }

    private static string BuildMethodSignatureDetail(MethodSignatureNode method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
        var returnType = method.Output?.TypeName ?? "void";
        return $"({parameters}) -> {returnType}";
    }

    private static string BuildClassDetail(ClassDefinitionNode cls)
    {
        var parts = new List<string>();
        if (cls.IsAbstract) parts.Add("abstract");
        if (cls.IsSealed) parts.Add("sealed");
        if (cls.IsStatic) parts.Add("static");
        if (cls.IsPartial) parts.Add("partial");
        parts.Add("class");
        if (cls.BaseClass != null) parts.Add($": {cls.BaseClass}");
        return string.Join(" ", parts);
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor")
        };
    }
}
