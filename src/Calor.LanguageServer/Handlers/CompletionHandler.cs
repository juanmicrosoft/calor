using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles completion requests for intelligent autocomplete.
/// </summary>
public sealed class CompletionHandler : CompletionHandlerBase
{
    private readonly WorkspaceState _workspace;

    // Common Calor tags for completion
    private static readonly (string Tag, string Description, CompletionItemKind Kind)[] CommonTags =
    {
        ("M", "Module definition", CompletionItemKind.Module),
        ("F", "Function definition", CompletionItemKind.Function),
        ("AF", "Async function definition", CompletionItemKind.Function),
        ("I", "Input parameter", CompletionItemKind.Variable),
        ("O", "Output (return type)", CompletionItemKind.TypeParameter),
        ("E", "Effects declaration", CompletionItemKind.Event),
        ("B", "Variable binding", CompletionItemKind.Variable),
        ("C", "Function call", CompletionItemKind.Method),
        ("R", "Return statement", CompletionItemKind.Keyword),
        ("P", "Print (Console.WriteLine)", CompletionItemKind.Function),
        ("Pf", "PrintF (Console.Write)", CompletionItemKind.Function),
        ("L", "For loop", CompletionItemKind.Keyword),
        ("WH", "While loop", CompletionItemKind.Keyword),
        ("DO", "Do-while loop", CompletionItemKind.Keyword),
        ("EACH", "Foreach loop", CompletionItemKind.Keyword),
        ("IF", "If conditional", CompletionItemKind.Keyword),
        ("EI", "Else-if", CompletionItemKind.Keyword),
        ("EL", "Else", CompletionItemKind.Keyword),
        ("W", "Match/switch expression", CompletionItemKind.Keyword),
        ("K", "Case in match", CompletionItemKind.Keyword),
        ("BK", "Break", CompletionItemKind.Keyword),
        ("CN", "Continue", CompletionItemKind.Keyword),
        ("Q", "Requires (precondition)", CompletionItemKind.Property),
        ("S", "Ensures (postcondition)", CompletionItemKind.Property),
        ("CL", "Class definition", CompletionItemKind.Class),
        ("IFACE", "Interface definition", CompletionItemKind.Interface),
        ("EN", "Enum definition", CompletionItemKind.Enum),
        ("EXT", "Enum extension methods", CompletionItemKind.Method),
        ("MT", "Method definition", CompletionItemKind.Method),
        ("AMT", "Async method definition", CompletionItemKind.Method),
        ("PROP", "Property definition", CompletionItemKind.Property),
        ("CTOR", "Constructor definition", CompletionItemKind.Constructor),
        ("FLD", "Field definition", CompletionItemKind.Field),
        ("GET", "Property getter", CompletionItemKind.Property),
        ("SET", "Property setter", CompletionItemKind.Property),
        ("IMPL", "Implements interface", CompletionItemKind.Interface),
        ("VR", "Virtual modifier", CompletionItemKind.Keyword),
        ("OV", "Override modifier", CompletionItemKind.Keyword),
        ("AB", "Abstract modifier", CompletionItemKind.Keyword),
        ("SD", "Sealed modifier", CompletionItemKind.Keyword),
        ("THIS", "This reference", CompletionItemKind.Keyword),
        ("BASE", "Base class reference", CompletionItemKind.Keyword),
        ("NEW", "New instance", CompletionItemKind.Keyword),
        ("TR", "Try block", CompletionItemKind.Keyword),
        ("CA", "Catch block", CompletionItemKind.Keyword),
        ("FI", "Finally block", CompletionItemKind.Keyword),
        ("TH", "Throw exception", CompletionItemKind.Keyword),
        ("LAM", "Lambda expression", CompletionItemKind.Function),
        ("DEL", "Delegate definition", CompletionItemKind.Function),
        ("EVT", "Event declaration", CompletionItemKind.Event),
        ("SUB", "Subscribe to event", CompletionItemKind.Method),
        ("UNSUB", "Unsubscribe from event", CompletionItemKind.Method),
        ("ASYNC", "Async modifier", CompletionItemKind.Keyword),
        ("AWAIT", "Await expression", CompletionItemKind.Keyword),
        ("ARR", "Array declaration", CompletionItemKind.Struct),
        ("LIST", "List declaration", CompletionItemKind.Struct),
        ("DICT", "Dictionary declaration", CompletionItemKind.Struct),
        ("HSET", "HashSet declaration", CompletionItemKind.Struct),
        ("PUSH", "Add to collection", CompletionItemKind.Method),
        ("PUT", "Put in dictionary", CompletionItemKind.Method),
        ("REM", "Remove from collection", CompletionItemKind.Method),
        ("INS", "Insert at index", CompletionItemKind.Method),
        ("HAS", "Contains check", CompletionItemKind.Method),
        ("CLR", "Clear collection", CompletionItemKind.Method),
        ("CNT", "Count property", CompletionItemKind.Property),
        ("IDX", "Index access", CompletionItemKind.Method),
        ("LEN", "Length property", CompletionItemKind.Property),
        ("D", "Record definition", CompletionItemKind.Struct),
        ("V", "Variant definition", CompletionItemKind.EnumMember),
        ("T", "Type definition", CompletionItemKind.TypeParameter),
        ("SM", "Some (Option)", CompletionItemKind.Value),
        ("NN", "None (Option)", CompletionItemKind.Value),
        ("OK", "Ok (Result)", CompletionItemKind.Value),
        ("ERR", "Err (Result)", CompletionItemKind.Value),
        ("U", "Using directive", CompletionItemKind.Reference),
    };

    // Type keywords for completion
    private static readonly (string Type, string Description)[] TypeKeywords =
    {
        ("i32", "32-bit signed integer"),
        ("i64", "64-bit signed integer"),
        ("i8", "8-bit signed integer"),
        ("i16", "16-bit signed integer"),
        ("u8", "8-bit unsigned integer"),
        ("u16", "16-bit unsigned integer"),
        ("u32", "32-bit unsigned integer"),
        ("u64", "64-bit unsigned integer"),
        ("f32", "32-bit float"),
        ("f64", "64-bit float"),
        ("str", "String type"),
        ("bool", "Boolean type"),
        ("void", "Void type"),
        ("char", "Character type"),
        ("byte", "Byte type"),
        ("decimal", "Decimal type"),
        ("object", "Object type"),
    };

    public CompletionHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        // Resolve additional details for a completion item if needed
        return Task.FromResult(request);
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state == null)
        {
            return Task.FromResult(new CompletionList());
        }

        var items = new List<CompletionItem>();
        var offset = PositionConverter.ToOffset(request.Position, state.Source);

        // Determine context
        var context = GetCompletionContext(state.Source, offset);

        switch (context)
        {
            case CompletionContext.AfterSectionMarker:
                // After § - suggest tags
                items.AddRange(GetTagCompletions());
                break;

            case CompletionContext.InType:
                // In type position - suggest types
                items.AddRange(GetTypeCompletions(state.Ast));
                items.AddRange(GetCrossFileTypeCompletions(_workspace, state));
                break;

            case CompletionContext.AfterDot:
                // After a dot - suggest members
                items.AddRange(GetMemberCompletions(state, offset, _workspace));
                break;

            case CompletionContext.InExpression:
                // In expression - suggest variables and functions
                items.AddRange(GetExpressionCompletions(state, offset));
                items.AddRange(GetCrossFileSymbolCompletions(_workspace, state));
                break;

            default:
                // General context - provide all completions
                items.AddRange(GetTagCompletions());
                items.AddRange(GetTypeCompletions(state.Ast));
                items.AddRange(GetExpressionCompletions(state, offset));
                items.AddRange(GetCrossFileSymbolCompletions(_workspace, state));
                break;
        }

        return Task.FromResult(new CompletionList(items));
    }

    private static IEnumerable<CompletionItem> GetCrossFileTypeCompletions(WorkspaceState workspace, DocumentState currentDoc)
    {
        var items = new List<CompletionItem>();

        foreach (var (doc, name, kind, type) in workspace.GetAllPublicSymbols())
        {
            // Skip symbols from the current document
            if (doc.Uri == currentDoc.Uri) continue;

            // Only include types (classes, interfaces, enums)
            if (kind is "class" or "interface" or "enum")
            {
                items.Add(new CompletionItem
                {
                    Label = name,
                    Kind = kind switch
                    {
                        "class" => CompletionItemKind.Class,
                        "interface" => CompletionItemKind.Interface,
                        "enum" => CompletionItemKind.Enum,
                        _ => CompletionItemKind.TypeParameter
                    },
                    Detail = $"[{GetFileName(doc.Uri)}] {kind} {name}",
                    InsertText = name,
                    SortText = "z" + name // Sort after local completions
                });
            }
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetCrossFileSymbolCompletions(WorkspaceState workspace, DocumentState currentDoc)
    {
        var items = new List<CompletionItem>();

        foreach (var (doc, name, kind, type) in workspace.GetAllPublicSymbols())
        {
            // Skip symbols from the current document
            if (doc.Uri == currentDoc.Uri) continue;

            items.Add(new CompletionItem
            {
                Label = name,
                Kind = kind switch
                {
                    "function" => CompletionItemKind.Function,
                    "class" => CompletionItemKind.Class,
                    "interface" => CompletionItemKind.Interface,
                    "enum" => CompletionItemKind.Enum,
                    "delegate" => CompletionItemKind.Function,
                    _ => CompletionItemKind.Reference
                },
                Detail = $"[{GetFileName(doc.Uri)}] {kind}{(type != null ? ": " + type : "")}",
                InsertText = name,
                SortText = "z" + name // Sort after local completions
            });
        }

        return items;
    }

    private static string GetFileName(Uri uri)
    {
        return System.IO.Path.GetFileName(uri.LocalPath);
    }

    private static CompletionContext GetCompletionContext(string source, int offset)
    {
        if (offset <= 0 || offset > source.Length)
            return CompletionContext.General;

        // Look back for § marker
        var lookback = Math.Min(offset, 10);
        var before = source.Substring(offset - lookback, lookback);

        // After § character
        if (before.EndsWith("§"))
            return CompletionContext.AfterSectionMarker;

        // After § followed by partial tag
        var lastSection = before.LastIndexOf('§');
        if (lastSection >= 0 && !before.Substring(lastSection).Contains('{'))
            return CompletionContext.AfterSectionMarker;

        // After type indicators (like in §I{, §O{, etc.)
        if (before.Contains("§I{") || before.Contains("§O{") || before.EndsWith(":"))
            return CompletionContext.InType;

        // After a dot - member access
        if (before.TrimEnd().EndsWith("."))
            return CompletionContext.AfterDot;

        return CompletionContext.InExpression;
    }

    private static IEnumerable<CompletionItem> GetTagCompletions()
    {
        return CommonTags.Select((t, i) => new CompletionItem
        {
            Label = $"§{t.Tag}",
            Kind = t.Kind,
            Detail = t.Description,
            InsertText = $"§{t.Tag}",
            SortText = i.ToString("D3"),
            FilterText = t.Tag
        });
    }

    private static IEnumerable<CompletionItem> GetTypeCompletions(ModuleNode? ast)
    {
        var items = new List<CompletionItem>();

        // Primitive types
        items.AddRange(TypeKeywords.Select(t => new CompletionItem
        {
            Label = t.Type,
            Kind = CompletionItemKind.TypeParameter,
            Detail = t.Description,
            InsertText = t.Type
        }));

        // User-defined types from AST
        if (ast != null)
        {
            // Classes
            items.AddRange(ast.Classes.Select(c => new CompletionItem
            {
                Label = c.Name,
                Kind = CompletionItemKind.Class,
                Detail = $"class {c.Name}",
                InsertText = c.Name
            }));

            // Interfaces
            items.AddRange(ast.Interfaces.Select(i => new CompletionItem
            {
                Label = i.Name,
                Kind = CompletionItemKind.Interface,
                Detail = $"interface {i.Name}",
                InsertText = i.Name
            }));

            // Enums
            items.AddRange(ast.Enums.Select(e => new CompletionItem
            {
                Label = e.Name,
                Kind = CompletionItemKind.Enum,
                Detail = $"enum {e.Name}",
                InsertText = e.Name
            }));
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetExpressionCompletions(DocumentState state, int offset)
    {
        var items = new List<CompletionItem>();
        var ast = state.Ast;

        if (ast == null)
            return items;

        // Functions
        items.AddRange(ast.Functions.Select(f => new CompletionItem
        {
            Label = f.Name,
            Kind = CompletionItemKind.Function,
            Detail = $"({string.Join(", ", f.Parameters.Select(p => p.TypeName))}) -> {f.Output?.TypeName ?? "void"}",
            InsertText = f.Name
        }));

        // Add variables in scope at the current position
        items.AddRange(GetVariablesInScope(ast, offset));

        // Boolean literals
        items.Add(new CompletionItem { Label = "true", Kind = CompletionItemKind.Constant, InsertText = "true" });
        items.Add(new CompletionItem { Label = "false", Kind = CompletionItemKind.Constant, InsertText = "false" });

        // Special variables
        items.Add(new CompletionItem { Label = "result", Kind = CompletionItemKind.Variable, Detail = "Postcondition result value", InsertText = "result" });

        return items;
    }

    private static IEnumerable<CompletionItem> GetMemberCompletions(DocumentState state, int offset, WorkspaceState workspace)
    {
        var items = new List<CompletionItem>();
        var ast = state.Ast;

        if (ast == null)
            return items;

        // Find the expression before the dot
        var source = state.Source;
        var exprBeforeDot = ExtractExpressionBeforeDot(source, offset);

        if (string.IsNullOrEmpty(exprBeforeDot))
            return items;

        // Try to determine the type of the expression
        var typeName = ResolveExpressionType(exprBeforeDot, ast, offset, workspace);

        if (string.IsNullOrEmpty(typeName))
        {
            // If we couldn't determine the type, check if the expression itself is a type name (static access)
            typeName = exprBeforeDot;
        }

        // Find members for this type
        items.AddRange(GetMembersForType(typeName, ast, workspace, state));

        return items;
    }

    private static string? ExtractExpressionBeforeDot(string source, int offset)
    {
        // Find the position of the dot (should be right before offset or at offset-1)
        var dotIndex = offset - 1;
        while (dotIndex >= 0 && char.IsWhiteSpace(source[dotIndex]))
            dotIndex--;

        if (dotIndex < 0 || source[dotIndex] != '.')
            return null;

        // Extract the identifier before the dot
        var end = dotIndex;
        var start = end - 1;

        // Handle potential closing characters (for expressions like arr[0]. or func(). )
        if (start >= 0)
        {
            if (source[start] == ']' || source[start] == ')')
            {
                // For complex expressions, just get the base identifier
                var bracketCount = 1;
                var bracket = source[start];
                var openBracket = bracket == ']' ? '[' : '(';
                start--;
                while (start >= 0 && bracketCount > 0)
                {
                    if (source[start] == bracket) bracketCount++;
                    else if (source[start] == openBracket) bracketCount--;
                    start--;
                }
            }

            // Now extract the identifier
            while (start >= 0 && (char.IsLetterOrDigit(source[start]) || source[start] == '_'))
                start--;

            start++; // Move back to the first character of the identifier
        }

        if (start >= end)
            return null;

        return source.Substring(start, end - start).Trim();
    }

    private static string? ResolveExpressionType(string expression, ModuleNode ast, int offset, WorkspaceState workspace)
    {
        // Handle 'this' keyword
        if (expression == "this")
        {
            var containingClass = FindContainingClass(ast, offset);
            return containingClass?.Name;
        }

        // Handle 'base' keyword
        if (expression == "base")
        {
            var containingClass = FindContainingClass(ast, offset);
            return containingClass?.BaseClass;
        }

        // Try to find the variable in scope and get its type
        var containingFunc = FindContainingFunction(ast, offset);
        if (containingFunc != null)
        {
            // Check parameters
            var param = containingFunc.Parameters.FirstOrDefault(p => p.Name == expression);
            if (param != null)
                return param.TypeName;

            // Check bindings
            foreach (var binding in CollectVisibleBindings(containingFunc.Body, offset))
            {
                if (binding.Name == expression)
                    return binding.TypeName;
            }
        }

        // Check method parameters and fields
        var containingMethod = FindContainingMethod(ast, offset);
        if (containingMethod.HasValue)
        {
            var (cls, method) = containingMethod.Value;

            // Check method parameters
            var param = method.Parameters.FirstOrDefault(p => p.Name == expression);
            if (param != null)
                return param.TypeName;

            // Check class fields
            var field = cls.Fields.FirstOrDefault(f => f.Name == expression);
            if (field != null)
                return field.TypeName;

            // Check class properties
            var prop = cls.Properties.FirstOrDefault(p => p.Name == expression);
            if (prop != null)
                return prop.TypeName;

            // Check local bindings
            foreach (var binding in CollectVisibleBindings(method.Body, offset))
            {
                if (binding.Name == expression)
                    return binding.TypeName;
            }
        }

        // Check if it's a type name for static access
        if (ast.Classes.Any(c => c.Name == expression) ||
            ast.Interfaces.Any(i => i.Name == expression) ||
            ast.Enums.Any(e => e.Name == expression))
        {
            return expression;
        }

        return null;
    }

    private static IEnumerable<CompletionItem> GetMembersForType(string typeName, ModuleNode ast, WorkspaceState workspace, DocumentState currentDoc)
    {
        var items = new List<CompletionItem>();

        // Check local classes
        var cls = ast.Classes.FirstOrDefault(c => c.Name == typeName);
        if (cls != null)
        {
            items.AddRange(GetClassMembers(cls));
            return items;
        }

        // Check local interfaces
        var iface = ast.Interfaces.FirstOrDefault(i => i.Name == typeName);
        if (iface != null)
        {
            items.AddRange(GetInterfaceMembers(iface));
            return items;
        }

        // Check local enums
        var en = ast.Enums.FirstOrDefault(e => e.Name == typeName);
        if (en != null)
        {
            items.AddRange(GetEnumMembers(en));

            // Also include extension methods for enums
            var ext = ast.EnumExtensions.FirstOrDefault(e => e.EnumName == typeName);
            if (ext != null)
            {
                items.AddRange(GetExtensionMethods(ext));
            }
            return items;
        }

        // Check other open documents
        foreach (var doc in workspace.GetAllDocuments())
        {
            if (doc.Uri == currentDoc.Uri || doc.Ast == null) continue;

            var otherCls = doc.Ast.Classes.FirstOrDefault(c => c.Name == typeName);
            if (otherCls != null)
            {
                items.AddRange(GetClassMembers(otherCls).Select(i => new CompletionItem
                {
                    Label = i.Label,
                    Kind = i.Kind,
                    Detail = $"[{GetFileName(doc.Uri)}] {i.Detail}",
                    InsertText = i.InsertText
                }));
                return items;
            }

            var otherIface = doc.Ast.Interfaces.FirstOrDefault(i => i.Name == typeName);
            if (otherIface != null)
            {
                items.AddRange(GetInterfaceMembers(otherIface).Select(i => new CompletionItem
                {
                    Label = i.Label,
                    Kind = i.Kind,
                    Detail = $"[{GetFileName(doc.Uri)}] {i.Detail}",
                    InsertText = i.InsertText
                }));
                return items;
            }

            var otherEnum = doc.Ast.Enums.FirstOrDefault(e => e.Name == typeName);
            if (otherEnum != null)
            {
                items.AddRange(GetEnumMembers(otherEnum).Select(i => new CompletionItem
                {
                    Label = i.Label,
                    Kind = i.Kind,
                    Detail = $"[{GetFileName(doc.Uri)}] {i.Detail}",
                    InsertText = i.InsertText
                }));
                return items;
            }
        }

        // Built-in string methods
        if (typeName is "str" or "STRING" or "string")
        {
            items.AddRange(GetStringMembers());
        }

        // Built-in collection methods
        if (typeName != null && (typeName.StartsWith("List<") || typeName.StartsWith("LIST<") || typeName.StartsWith("list<")))
        {
            items.AddRange(GetListMembers());
        }

        if (typeName != null && (typeName.StartsWith("Dict<") || typeName.StartsWith("DICT<") || typeName.StartsWith("dict<")))
        {
            items.AddRange(GetDictMembers());
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetClassMembers(ClassDefinitionNode cls)
    {
        var items = new List<CompletionItem>();

        // Fields
        foreach (var field in cls.Fields)
        {
            items.Add(new CompletionItem
            {
                Label = field.Name,
                Kind = CompletionItemKind.Field,
                Detail = $"(field) {field.Name}: {field.TypeName}",
                InsertText = field.Name
            });
        }

        // Properties
        foreach (var prop in cls.Properties)
        {
            items.Add(new CompletionItem
            {
                Label = prop.Name,
                Kind = CompletionItemKind.Property,
                Detail = $"(property) {prop.Name}: {prop.TypeName}",
                InsertText = prop.Name
            });
        }

        // Methods
        foreach (var method in cls.Methods)
        {
            var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
            items.Add(new CompletionItem
            {
                Label = method.Name,
                Kind = CompletionItemKind.Method,
                Detail = $"(method) {method.Name}({paramList}): {method.Output?.TypeName ?? "void"}",
                InsertText = method.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetInterfaceMembers(InterfaceDefinitionNode iface)
    {
        var items = new List<CompletionItem>();

        foreach (var method in iface.Methods)
        {
            var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
            items.Add(new CompletionItem
            {
                Label = method.Name,
                Kind = CompletionItemKind.Method,
                Detail = $"(method) {method.Name}({paramList}): {method.Output?.TypeName ?? "void"}",
                InsertText = method.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetEnumMembers(EnumDefinitionNode en)
    {
        var items = new List<CompletionItem>();

        foreach (var member in en.Members)
        {
            items.Add(new CompletionItem
            {
                Label = member.Name,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"(enum member) {en.Name}.{member.Name}" + (member.Value != null ? $" = {member.Value}" : ""),
                InsertText = member.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetExtensionMethods(EnumExtensionNode ext)
    {
        var items = new List<CompletionItem>();

        foreach (var method in ext.Methods)
        {
            var paramList = string.Join(", ", method.Parameters.Skip(1).Select(p => $"{p.Name}: {p.TypeName}")); // Skip 'self' param
            items.Add(new CompletionItem
            {
                Label = method.Name,
                Kind = CompletionItemKind.Method,
                Detail = $"(extension) {method.Name}({paramList}): {method.Output?.TypeName ?? "void"}",
                InsertText = method.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetRecordMembers(RecordDefinitionNode record)
    {
        var items = new List<CompletionItem>();

        foreach (var field in record.Fields)
        {
            items.Add(new CompletionItem
            {
                Label = field.Name,
                Kind = CompletionItemKind.Field,
                Detail = $"(field) {field.Name}: {field.TypeName}",
                InsertText = field.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetStringMembers()
    {
        return new[]
        {
            new CompletionItem { Label = "Length", Kind = CompletionItemKind.Property, Detail = "(property) Length: INT", InsertText = "Length" },
            new CompletionItem { Label = "ToUpper", Kind = CompletionItemKind.Method, Detail = "(method) ToUpper(): STRING", InsertText = "ToUpper" },
            new CompletionItem { Label = "ToLower", Kind = CompletionItemKind.Method, Detail = "(method) ToLower(): STRING", InsertText = "ToLower" },
            new CompletionItem { Label = "Trim", Kind = CompletionItemKind.Method, Detail = "(method) Trim(): STRING", InsertText = "Trim" },
            new CompletionItem { Label = "Substring", Kind = CompletionItemKind.Method, Detail = "(method) Substring(start: INT, length: INT): STRING", InsertText = "Substring" },
            new CompletionItem { Label = "Contains", Kind = CompletionItemKind.Method, Detail = "(method) Contains(value: STRING): BOOL", InsertText = "Contains" },
            new CompletionItem { Label = "StartsWith", Kind = CompletionItemKind.Method, Detail = "(method) StartsWith(value: STRING): BOOL", InsertText = "StartsWith" },
            new CompletionItem { Label = "EndsWith", Kind = CompletionItemKind.Method, Detail = "(method) EndsWith(value: STRING): BOOL", InsertText = "EndsWith" },
            new CompletionItem { Label = "Replace", Kind = CompletionItemKind.Method, Detail = "(method) Replace(old: STRING, new: STRING): STRING", InsertText = "Replace" },
            new CompletionItem { Label = "Split", Kind = CompletionItemKind.Method, Detail = "(method) Split(separator: STRING): LIST<STRING>", InsertText = "Split" },
            new CompletionItem { Label = "IndexOf", Kind = CompletionItemKind.Method, Detail = "(method) IndexOf(value: STRING): INT", InsertText = "IndexOf" },
        };
    }

    private static IEnumerable<CompletionItem> GetListMembers()
    {
        return new[]
        {
            new CompletionItem { Label = "Count", Kind = CompletionItemKind.Property, Detail = "(property) Count: INT", InsertText = "Count" },
            new CompletionItem { Label = "Add", Kind = CompletionItemKind.Method, Detail = "(method) Add(item: T): void", InsertText = "Add" },
            new CompletionItem { Label = "Remove", Kind = CompletionItemKind.Method, Detail = "(method) Remove(item: T): BOOL", InsertText = "Remove" },
            new CompletionItem { Label = "RemoveAt", Kind = CompletionItemKind.Method, Detail = "(method) RemoveAt(index: INT): void", InsertText = "RemoveAt" },
            new CompletionItem { Label = "Insert", Kind = CompletionItemKind.Method, Detail = "(method) Insert(index: INT, item: T): void", InsertText = "Insert" },
            new CompletionItem { Label = "Clear", Kind = CompletionItemKind.Method, Detail = "(method) Clear(): void", InsertText = "Clear" },
            new CompletionItem { Label = "Contains", Kind = CompletionItemKind.Method, Detail = "(method) Contains(item: T): BOOL", InsertText = "Contains" },
            new CompletionItem { Label = "IndexOf", Kind = CompletionItemKind.Method, Detail = "(method) IndexOf(item: T): INT", InsertText = "IndexOf" },
        };
    }

    private static IEnumerable<CompletionItem> GetDictMembers()
    {
        return new[]
        {
            new CompletionItem { Label = "Count", Kind = CompletionItemKind.Property, Detail = "(property) Count: INT", InsertText = "Count" },
            new CompletionItem { Label = "Keys", Kind = CompletionItemKind.Property, Detail = "(property) Keys: ICollection<K>", InsertText = "Keys" },
            new CompletionItem { Label = "Values", Kind = CompletionItemKind.Property, Detail = "(property) Values: ICollection<V>", InsertText = "Values" },
            new CompletionItem { Label = "Add", Kind = CompletionItemKind.Method, Detail = "(method) Add(key: K, value: V): void", InsertText = "Add" },
            new CompletionItem { Label = "Remove", Kind = CompletionItemKind.Method, Detail = "(method) Remove(key: K): BOOL", InsertText = "Remove" },
            new CompletionItem { Label = "Clear", Kind = CompletionItemKind.Method, Detail = "(method) Clear(): void", InsertText = "Clear" },
            new CompletionItem { Label = "ContainsKey", Kind = CompletionItemKind.Method, Detail = "(method) ContainsKey(key: K): BOOL", InsertText = "ContainsKey" },
            new CompletionItem { Label = "ContainsValue", Kind = CompletionItemKind.Method, Detail = "(method) ContainsValue(value: V): BOOL", InsertText = "ContainsValue" },
            new CompletionItem { Label = "TryGetValue", Kind = CompletionItemKind.Method, Detail = "(method) TryGetValue(key: K, out value: V): BOOL", InsertText = "TryGetValue" },
        };
    }

    private static ClassDefinitionNode? FindContainingClass(ModuleNode ast, int offset)
    {
        foreach (var cls in ast.Classes)
        {
            if (offset >= cls.Span.Start && offset < cls.Span.End)
            {
                return cls;
            }
        }
        return null;
    }

    private static IEnumerable<CompletionItem> GetVariablesInScope(ModuleNode ast, int offset)
    {
        var items = new List<CompletionItem>();

        // Find the containing function
        var containingFunc = FindContainingFunction(ast, offset);
        if (containingFunc == null)
        {
            // Check if we're in a class method
            var containingMethod = FindContainingMethod(ast, offset);
            if (containingMethod.HasValue)
            {
                items.AddRange(GetMethodVariables(containingMethod.Value.Item1, containingMethod.Value.Item2, offset));
            }
            return items;
        }

        // Add parameters
        foreach (var param in containingFunc.Parameters)
        {
            items.Add(new CompletionItem
            {
                Label = param.Name,
                Kind = CompletionItemKind.Variable,
                Detail = $"(parameter) {param.Name}: {param.TypeName}",
                InsertText = param.Name,
                SortText = "0" + param.Name // Sort parameters first
            });
        }

        // Walk statements before cursor to collect bindings
        foreach (var binding in CollectVisibleBindings(containingFunc.Body, offset))
        {
            items.Add(new CompletionItem
            {
                Label = binding.Name,
                Kind = binding.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({(binding.IsMutable ? "var" : "let")}) {binding.Name}: {binding.TypeName ?? "inferred"}",
                InsertText = binding.Name,
                SortText = "1" + binding.Name // Sort local variables after parameters
            });
        }

        return items;
    }

    private static FunctionNode? FindContainingFunction(ModuleNode ast, int offset)
    {
        foreach (var func in ast.Functions)
        {
            if (offset >= func.Span.Start && offset < func.Span.End)
            {
                return func;
            }
        }
        return null;
    }

    private static (ClassDefinitionNode, MethodNode)? FindContainingMethod(ModuleNode ast, int offset)
    {
        foreach (var cls in ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                if (offset >= method.Span.Start && offset < method.Span.End)
                {
                    return (cls, method);
                }
            }
        }
        return null;
    }

    private static IEnumerable<CompletionItem> GetMethodVariables(ClassDefinitionNode cls, MethodNode method, int offset)
    {
        var items = new List<CompletionItem>();

        // Add 'this' keyword for instance methods
        items.Add(new CompletionItem
        {
            Label = "this",
            Kind = CompletionItemKind.Keyword,
            Detail = $"(this) {cls.Name}",
            InsertText = "this",
            SortText = "0this"
        });

        // Add class fields
        foreach (var field in cls.Fields)
        {
            items.Add(new CompletionItem
            {
                Label = field.Name,
                Kind = CompletionItemKind.Field,
                Detail = $"(field) {field.Name}: {field.TypeName}",
                InsertText = field.Name,
                SortText = "0" + field.Name
            });
        }

        // Add class properties
        foreach (var prop in cls.Properties)
        {
            items.Add(new CompletionItem
            {
                Label = prop.Name,
                Kind = CompletionItemKind.Property,
                Detail = $"(property) {prop.Name}: {prop.TypeName}",
                InsertText = prop.Name,
                SortText = "0" + prop.Name
            });
        }

        // Add method parameters
        foreach (var param in method.Parameters)
        {
            items.Add(new CompletionItem
            {
                Label = param.Name,
                Kind = CompletionItemKind.Variable,
                Detail = $"(parameter) {param.Name}: {param.TypeName}",
                InsertText = param.Name,
                SortText = "1" + param.Name
            });
        }

        // Add local bindings
        foreach (var binding in CollectVisibleBindings(method.Body, offset))
        {
            items.Add(new CompletionItem
            {
                Label = binding.Name,
                Kind = binding.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({(binding.IsMutable ? "var" : "let")}) {binding.Name}: {binding.TypeName ?? "inferred"}",
                InsertText = binding.Name,
                SortText = "2" + binding.Name
            });
        }

        return items;
    }

    private static IEnumerable<BindStatementNode> CollectVisibleBindings(IReadOnlyList<StatementNode> statements, int offset)
    {
        var bindings = new List<BindStatementNode>();

        foreach (var stmt in statements)
        {
            // Only include bindings that appear before the cursor
            if (stmt.Span.Start >= offset)
                break;

            if (stmt is BindStatementNode bind)
            {
                bindings.Add(bind);
            }
            else if (stmt is ForStatementNode forStmt && offset >= forStmt.Span.Start && offset < forStmt.Span.End)
            {
                // Add loop variable and recurse into body
                // Note: ForStatementNode doesn't inherit BindStatementNode, but we can handle it specially
                bindings.AddRange(CollectVisibleBindings(forStmt.Body, offset));
            }
            else if (stmt is WhileStatementNode whileStmt && offset >= whileStmt.Span.Start && offset < whileStmt.Span.End)
            {
                bindings.AddRange(CollectVisibleBindings(whileStmt.Body, offset));
            }
            else if (stmt is IfStatementNode ifStmt && offset >= ifStmt.Span.Start && offset < ifStmt.Span.End)
            {
                // Determine which branch we're in
                var inThen = ifStmt.ThenBody.Any(s => offset >= s.Span.Start && offset < s.Span.End);
                if (inThen)
                {
                    bindings.AddRange(CollectVisibleBindings(ifStmt.ThenBody, offset));
                }
                else if (ifStmt.ElseBody != null)
                {
                    var inElse = ifStmt.ElseBody.Any(s => offset >= s.Span.Start && offset < s.Span.End);
                    if (inElse)
                    {
                        bindings.AddRange(CollectVisibleBindings(ifStmt.ElseBody, offset));
                    }
                }
            }
            else if (stmt is ForeachStatementNode foreachStmt && offset >= foreachStmt.Span.Start && offset < foreachStmt.Span.End)
            {
                bindings.AddRange(CollectVisibleBindings(foreachStmt.Body, offset));
            }
            else if (stmt is TryStatementNode tryStmt && offset >= tryStmt.Span.Start && offset < tryStmt.Span.End)
            {
                bindings.AddRange(CollectVisibleBindings(tryStmt.TryBody, offset));
                // Note: catch variables are scoped to their catch block
            }
        }

        return bindings;
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            TriggerCharacters = new Container<string>("§", ".", ":"),
            ResolveProvider = false
        };
    }

    private enum CompletionContext
    {
        General,
        AfterSectionMarker,
        InType,
        InExpression,
        AfterDot
    }
}
