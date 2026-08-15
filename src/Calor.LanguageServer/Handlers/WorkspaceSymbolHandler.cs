using Calor.Compiler.Ast;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles workspace symbol search requests (Ctrl+T / Cmd+T).
/// </summary>
public sealed class WorkspaceSymbolHandler : WorkspaceSymbolsHandlerBase
{
    private readonly WorkspaceState _workspace;

    public WorkspaceSymbolHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
    {
        var query = request.Query?.Trim() ?? "";
        var symbols = new List<WorkspaceSymbol>();

        var workspace = _workspace.CaptureSnapshot();
        foreach (var doc in workspace.Documents)
        {
            if (doc.Analysis.Ast == null) continue;

            var docSymbols = CollectWorkspaceSymbols(doc, query);
            symbols.AddRange(docSymbols);
        }

        // Sort by relevance (exact matches first, then prefix matches, then contains)
        symbols = symbols
            .OrderByDescending(s => GetRelevanceScore(s.Name, query))
            .ThenBy(s => s.Name)
            .Take(100) // Limit results
            .ToList();

        return Task.FromResult<Container<WorkspaceSymbol>?>(new Container<WorkspaceSymbol>(symbols));
    }

    private static IEnumerable<WorkspaceSymbol> CollectWorkspaceSymbols(
        WorkspaceDocumentSnapshot doc,
        string query)
    {
        if (doc.Analysis.Ast == null) yield break;

        var uri = DocumentUri.From(doc.Document.Uri);
        var ast = doc.Analysis.Ast;
        var source = doc.Analysis.Source;

        // Functions
        foreach (var func in ast.Functions)
        {
            if (MatchesQuery(func.Name, query))
            {
                var range = PositionConverter.ToLspRange(func.Span, source);
                yield return new WorkspaceSymbol
                {
                    Name = func.Name,
                    Kind = SymbolKind.Function,
                    Location = new Location { Uri = uri, Range = range },
                    ContainerName = ast.Name
                };
            }

            // Parameters (if specific enough query)
            if (query.Length >= 2)
            {
                foreach (var param in func.Parameters)
                {
                    if (MatchesQuery(param.Name, query))
                    {
                        var range = PositionConverter.ToLspRange(param.Span, source);
                        yield return new WorkspaceSymbol
                        {
                            Name = param.Name,
                            Kind = SymbolKind.Variable,
                            Location = new Location { Uri = uri, Range = range },
                            ContainerName = $"{ast.Name}.{func.Name}"
                        };
                    }
                }
            }
        }

        // Classes
        foreach (var cls in ast.Classes)
        {
            if (MatchesQuery(cls.Name, query))
            {
                var range = PositionConverter.ToLspRange(cls.Span, source);
                yield return new WorkspaceSymbol
                {
                    Name = cls.Name,
                    Kind = SymbolKind.Class,
                    Location = new Location { Uri = uri, Range = range },
                    ContainerName = ast.Name
                };
            }

            // Class members
            foreach (var field in cls.Fields)
            {
                if (MatchesQuery(field.Name, query))
                {
                    var range = PositionConverter.ToLspRange(field.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = field.Name,
                        Kind = SymbolKind.Field,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{cls.Name}"
                    };
                }
            }

            foreach (var prop in cls.Properties)
            {
                if (MatchesQuery(prop.Name, query))
                {
                    var range = PositionConverter.ToLspRange(prop.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = prop.Name,
                        Kind = SymbolKind.Property,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{cls.Name}"
                    };
                }
            }

            foreach (var method in cls.Methods)
            {
                if (MatchesQuery(method.Name, query))
                {
                    var range = PositionConverter.ToLspRange(method.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = method.Name,
                        Kind = SymbolKind.Method,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{cls.Name}"
                    };
                }
            }

            foreach (var ctor in cls.Constructors)
            {
                if (MatchesQuery(cls.Name, query) || MatchesQuery("constructor", query))
                {
                    var range = PositionConverter.ToLspRange(ctor.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = $"{cls.Name}()",
                        Kind = SymbolKind.Constructor,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{cls.Name}"
                    };
                }
            }
        }

        // Interfaces
        foreach (var iface in ast.Interfaces)
        {
            if (MatchesQuery(iface.Name, query))
            {
                var range = PositionConverter.ToLspRange(iface.Span, source);
                yield return new WorkspaceSymbol
                {
                    Name = iface.Name,
                    Kind = SymbolKind.Interface,
                    Location = new Location { Uri = uri, Range = range },
                    ContainerName = ast.Name
                };
            }

            foreach (var method in iface.Methods)
            {
                if (MatchesQuery(method.Name, query))
                {
                    var range = PositionConverter.ToLspRange(method.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = method.Name,
                        Kind = SymbolKind.Method,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{iface.Name}"
                    };
                }
            }
        }

        // Enums
        foreach (var enumDef in ast.Enums)
        {
            if (MatchesQuery(enumDef.Name, query))
            {
                var range = PositionConverter.ToLspRange(enumDef.Span, source);
                yield return new WorkspaceSymbol
                {
                    Name = enumDef.Name,
                    Kind = SymbolKind.Enum,
                    Location = new Location { Uri = uri, Range = range },
                    ContainerName = ast.Name
                };
            }

            foreach (var member in enumDef.Members)
            {
                if (MatchesQuery(member.Name, query))
                {
                    var range = PositionConverter.ToLspRange(member.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = member.Name,
                        Kind = SymbolKind.EnumMember,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{enumDef.Name}"
                    };
                }
            }
        }

        // Enum extensions
        foreach (var enumExt in ast.EnumExtensions)
        {
            foreach (var method in enumExt.Methods)
            {
                if (MatchesQuery(method.Name, query))
                {
                    var range = PositionConverter.ToLspRange(method.Span, source);
                    yield return new WorkspaceSymbol
                    {
                        Name = method.Name,
                        Kind = SymbolKind.Method,
                        Location = new Location { Uri = uri, Range = range },
                        ContainerName = $"{ast.Name}.{enumExt.EnumName}Extensions"
                    };
                }
            }
        }

        // Delegates
        foreach (var del in ast.Delegates)
        {
            if (MatchesQuery(del.Name, query))
            {
                var range = PositionConverter.ToLspRange(del.Span, source);
                yield return new WorkspaceSymbol
                {
                    Name = del.Name,
                    Kind = SymbolKind.Function, // No delegate kind, use function
                    Location = new Location { Uri = uri, Range = range },
                    ContainerName = ast.Name
                };
            }
        }
    }

    private static bool MatchesQuery(string name, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetRelevanceScore(string name, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;

        // Exact match (case-insensitive)
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 100;

        // Prefix match
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 75;

        // Camel case match (e.g., "gvs" matches "GetVisibleSymbols")
        if (MatchesCamelCase(name, query)) return 50;

        // Contains
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 25;

        return 0;
    }

    private static bool MatchesCamelCase(string name, string query)
    {
        // Extract uppercase letters from name
        var initials = new string(name.Where(char.IsUpper).ToArray());
        return initials.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
        WorkspaceSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new WorkspaceSymbolRegistrationOptions();
    }
}
