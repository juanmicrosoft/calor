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

        // Extract the full chained expression before the dot (e.g., "a.b.c" from "a.b.c.")
        var end = dotIndex;
        var start = end - 1;

        // Work backwards through the chained expression
        while (start >= 0)
        {
            // Handle potential closing characters (for expressions like arr[0]. or func(). )
            if (source[start] == ']' || source[start] == ')')
            {
                // Skip over bracketed/parenthesized expression
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
                // Continue to get the identifier before the bracket
                continue;
            }

            // Extract identifier characters
            if (char.IsLetterOrDigit(source[start]) || source[start] == '_')
            {
                start--;
                continue;
            }

            // Handle dot for chained access - continue backwards
            if (source[start] == '.')
            {
                start--;
                continue;
            }

            // Stop at any other character
            break;
        }

        start++; // Move back to the first character of the expression

        if (start >= end)
            return null;

        var result = source.Substring(start, end - start).Trim();

        // Remove any leading dots
        return result.TrimStart('.');
    }

    private static string? ResolveExpressionType(string expression, ModuleNode ast, int offset, WorkspaceState workspace)
    {
        // Handle chained expressions like "a.b.c"
        if (expression.Contains('.'))
        {
            return ResolveChainedExpressionType(expression, ast, offset, workspace);
        }

        // Handle method call expressions like "GetName()"
        if (expression.EndsWith(")"))
        {
            var methodName = ExtractMethodNameFromCall(expression);
            if (methodName != null)
            {
                return ResolveMethodReturnType(methodName, ast, offset, workspace, null);
            }
        }

        return ResolveSingleIdentifierType(expression, ast, offset, workspace);
    }

    private static string? ResolveSingleIdentifierType(string expression, ModuleNode ast, int offset, WorkspaceState workspace)
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

    private static string? ResolveChainedExpressionType(string expression, ModuleNode ast, int offset, WorkspaceState workspace)
    {
        // Split into parts, handling potential method calls
        var parts = SplitChainedExpression(expression);
        if (parts.Count == 0)
            return null;

        // Resolve the first part
        var currentType = ResolveSingleIdentifierType(parts[0].Name, ast, offset, workspace);

        // If first part is a method call, get its return type
        if (parts[0].IsMethodCall && currentType == null)
        {
            currentType = ResolveMethodReturnType(parts[0].Name, ast, offset, workspace, null);
        }

        if (currentType == null)
        {
            // Maybe the first part is a type name for static access
            currentType = parts[0].Name;
        }

        // Handle index access on first part
        if (parts[0].IsIndexAccess && currentType != null)
        {
            currentType = ResolveIndexAccessType(currentType);
        }

        // Resolve each subsequent part
        for (int i = 1; i < parts.Count; i++)
        {
            if (currentType == null)
                return null;

            var part = parts[i];
            var nextType = ResolveMemberType(currentType, part.Name, part.IsMethodCall, ast, workspace);

            if (nextType == null)
                return currentType; // Return what we have so far

            currentType = nextType;

            // Handle index access on this part
            if (part.IsIndexAccess && currentType != null)
            {
                currentType = ResolveIndexAccessType(currentType);
            }
        }

        return currentType;
    }

    /// <summary>
    /// Resolves the element type when accessing a collection by index.
    /// For List&lt;Person&gt;[0], returns Person.
    /// </summary>
    private static string? ResolveIndexAccessType(string collectionType)
    {
        var (baseType, typeArgs) = ParseGenericType(collectionType);

        // Handle generic collections
        if (baseType is "List" or "LIST" or "list" or "Array" or "ARRAY")
        {
            return typeArgs.Count > 0 ? typeArgs[0] : "object";
        }

        // Handle array syntax: Person[]
        if (collectionType.EndsWith("[]"))
        {
            return collectionType.Substring(0, collectionType.Length - 2);
        }

        // Handle dictionaries - index access returns value type
        if (baseType is "Dict" or "DICT" or "dict")
        {
            return typeArgs.Count > 1 ? typeArgs[1] : "object";
        }

        // Handle strings - index access returns char
        if (baseType is "str" or "STRING" or "string")
        {
            return "char";
        }

        return null;
    }

    private static List<(string Name, bool IsMethodCall, bool IsIndexAccess)> SplitChainedExpression(string expression)
    {
        var parts = new List<(string Name, bool IsMethodCall, bool IsIndexAccess)>();
        var current = new System.Text.StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;

        for (int i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == '[') bracketDepth++;
            else if (c == ']') bracketDepth--;

            if (c == '.' && parenDepth == 0 && bracketDepth == 0)
            {
                if (current.Length > 0)
                {
                    var part = current.ToString();
                    var isMethod = part.Contains('(');
                    var isIndex = part.Contains('[');
                    parts.Add((ExtractIdentifier(part), isMethod, isIndex));
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        // Add the last part
        if (current.Length > 0)
        {
            var part = current.ToString();
            var isMethod = part.Contains('(');
            var isIndex = part.Contains('[');
            parts.Add((ExtractIdentifier(part), isMethod, isIndex));
        }

        return parts;
    }

    private static string ExtractIdentifier(string part)
    {
        // Remove method call parentheses and index access brackets
        var parenIndex = part.IndexOf('(');
        var bracketIndex = part.IndexOf('[');

        if (parenIndex >= 0 && (bracketIndex < 0 || parenIndex < bracketIndex))
            return part.Substring(0, parenIndex);
        if (bracketIndex >= 0)
            return part.Substring(0, bracketIndex);
        return part;
    }

    private static string? ExtractMethodNameFromCall(string expression)
    {
        var parenIndex = expression.LastIndexOf('(');
        if (parenIndex > 0)
            return expression.Substring(0, parenIndex);
        return null;
    }

    private static string? ResolveMemberType(string typeName, string memberName, bool isMethodCall, ModuleNode ast, WorkspaceState workspace)
    {
        // Handle generic types - extract base type name
        var (baseTypeName, typeArgs) = ParseGenericType(typeName);

        // Check local classes with inheritance chain
        var cls = FindClassByName(baseTypeName, ast, workspace);
        if (cls != null)
        {
            var result = ResolveMemberTypeInClassHierarchy(cls, memberName, isMethodCall, ast, workspace);
            if (result != null)
                return result;
        }

        // Check interfaces
        var iface = FindInterfaceByName(baseTypeName, ast, workspace);
        if (iface != null && isMethodCall)
        {
            var method = iface.Methods.FirstOrDefault(m => m.Name == memberName);
            if (method != null)
                return method.Output?.TypeName;
        }

        // Handle built-in types
        if (baseTypeName is "str" or "STRING" or "string")
        {
            return ResolveBuiltInStringMember(memberName, isMethodCall);
        }

        if (baseTypeName is "List" or "LIST" or "list" ||
            typeName.StartsWith("List<") || typeName.StartsWith("LIST<") || typeName.StartsWith("list<"))
        {
            return ResolveBuiltInListMember(typeName, memberName, isMethodCall, typeArgs);
        }

        if (baseTypeName is "Dict" or "DICT" or "dict" ||
            typeName.StartsWith("Dict<") || typeName.StartsWith("DICT<") || typeName.StartsWith("dict<"))
        {
            return ResolveBuiltInDictMember(typeName, memberName, isMethodCall, typeArgs);
        }

        if (baseTypeName is "Array" or "ARRAY" or "array" ||
            typeName.EndsWith("[]"))
        {
            return ResolveBuiltInArrayMember(typeName, memberName, isMethodCall, typeArgs);
        }

        return null;
    }

    private static string? ResolveMemberTypeInClassHierarchy(ClassDefinitionNode cls, string memberName, bool isMethodCall, ModuleNode ast, WorkspaceState workspace)
    {
        // Check current class
        if (isMethodCall)
        {
            var method = cls.Methods.FirstOrDefault(m => m.Name == memberName);
            if (method != null)
                return method.Output?.TypeName;
        }
        else
        {
            var field = cls.Fields.FirstOrDefault(f => f.Name == memberName);
            if (field != null)
                return field.TypeName;

            var prop = cls.Properties.FirstOrDefault(p => p.Name == memberName);
            if (prop != null)
                return prop.TypeName;
        }

        // Check base class if exists
        if (!string.IsNullOrEmpty(cls.BaseClass))
        {
            var baseClass = FindClassByName(cls.BaseClass, ast, workspace);
            if (baseClass != null)
            {
                return ResolveMemberTypeInClassHierarchy(baseClass, memberName, isMethodCall, ast, workspace);
            }
        }

        // Check implemented interfaces for method signatures
        if (isMethodCall)
        {
            foreach (var ifaceName in cls.ImplementedInterfaces)
            {
                var iface = FindInterfaceByName(ifaceName, ast, workspace);
                if (iface != null)
                {
                    var method = iface.Methods.FirstOrDefault(m => m.Name == memberName);
                    if (method != null)
                        return method.Output?.TypeName;
                }
            }
        }

        return null;
    }

    private static ClassDefinitionNode? FindClassByName(string name, ModuleNode ast, WorkspaceState workspace)
    {
        // Check local AST
        var cls = ast.Classes.FirstOrDefault(c => c.Name == name);
        if (cls != null)
            return cls;

        // Check other documents
        foreach (var doc in workspace.GetAllDocuments())
        {
            if (doc.Ast == null) continue;
            cls = doc.Ast.Classes.FirstOrDefault(c => c.Name == name);
            if (cls != null)
                return cls;
        }

        return null;
    }

    private static InterfaceDefinitionNode? FindInterfaceByName(string name, ModuleNode ast, WorkspaceState workspace)
    {
        // Check local AST
        var iface = ast.Interfaces.FirstOrDefault(i => i.Name == name);
        if (iface != null)
            return iface;

        // Check other documents
        foreach (var doc in workspace.GetAllDocuments())
        {
            if (doc.Ast == null) continue;
            iface = doc.Ast.Interfaces.FirstOrDefault(i => i.Name == name);
            if (iface != null)
                return iface;
        }

        return null;
    }

    /// <summary>
    /// Parses a generic type like "List&lt;Person&gt;" into base type "List" and type args ["Person"].
    /// </summary>
    private static (string BaseType, List<string> TypeArgs) ParseGenericType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return (typeName ?? "", new List<string>());

        // Handle array syntax: Person[] -> (Array, [Person])
        if (typeName.EndsWith("[]"))
        {
            var elementType = typeName.Substring(0, typeName.Length - 2);
            return ("Array", new List<string> { elementType });
        }

        var angleBracketIndex = typeName.IndexOf('<');
        if (angleBracketIndex < 0)
            return (typeName, new List<string>());

        var baseType = typeName.Substring(0, angleBracketIndex);
        var argsString = typeName.Substring(angleBracketIndex + 1);

        // Remove trailing '>'
        if (argsString.EndsWith(">"))
            argsString = argsString.Substring(0, argsString.Length - 1);

        // Parse comma-separated type arguments, handling nested generics
        var typeArgs = new List<string>();
        var depth = 0;
        var currentArg = new System.Text.StringBuilder();

        foreach (var c in argsString)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;

            if (c == ',' && depth == 0)
            {
                typeArgs.Add(currentArg.ToString().Trim());
                currentArg.Clear();
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
            typeArgs.Add(currentArg.ToString().Trim());

        return (baseType, typeArgs);
    }

    private static string? ResolveBuiltInStringMember(string memberName, bool isMethodCall)
    {
        if (!isMethodCall && memberName == "Length")
            return "i32";

        if (isMethodCall)
        {
            return memberName switch
            {
                "ToUpper" or "ToLower" or "Trim" or "Substring" or "Replace" => "str",
                "Contains" or "StartsWith" or "EndsWith" => "bool",
                "IndexOf" => "i32",
                "Split" => "List<str>",
                _ => null
            };
        }

        return null;
    }

    private static string? ResolveBuiltInListMember(string listType, string memberName, bool isMethodCall, List<string> typeArgs)
    {
        var elementType = typeArgs.Count > 0 ? typeArgs[0] : "object";

        if (!isMethodCall)
        {
            return memberName switch
            {
                "Count" => "i32",
                _ => null
            };
        }

        return memberName switch
        {
            "Contains" => "bool",
            "IndexOf" => "i32",
            "Remove" => "bool",
            "First" or "Last" or "FirstOrDefault" or "LastOrDefault" => elementType,
            "Find" or "FindLast" => elementType,
            _ => null
        };
    }

    private static string? ResolveBuiltInDictMember(string dictType, string memberName, bool isMethodCall, List<string> typeArgs)
    {
        var keyType = typeArgs.Count > 0 ? typeArgs[0] : "object";
        var valueType = typeArgs.Count > 1 ? typeArgs[1] : "object";

        if (!isMethodCall)
        {
            return memberName switch
            {
                "Count" => "i32",
                "Keys" => $"List<{keyType}>",
                "Values" => $"List<{valueType}>",
                _ => null
            };
        }

        return memberName switch
        {
            "ContainsKey" or "ContainsValue" or "Remove" or "TryGetValue" => "bool",
            "GetValueOrDefault" => valueType,
            _ => null
        };
    }

    private static string? ResolveBuiltInArrayMember(string arrayType, string memberName, bool isMethodCall, List<string> typeArgs)
    {
        var elementType = typeArgs.Count > 0 ? typeArgs[0] : "object";

        if (!isMethodCall)
        {
            return memberName switch
            {
                "Length" => "i32",
                _ => null
            };
        }

        return memberName switch
        {
            "First" or "Last" => elementType,
            _ => null
        };
    }

    private static string? ResolveMethodReturnType(string methodName, ModuleNode ast, int offset, WorkspaceState workspace, string? containingType)
    {
        // If we have a containing type, search for methods on that type
        if (containingType != null)
        {
            return ResolveMemberType(containingType, methodName, true, ast, workspace);
        }

        // Search for standalone functions
        var func = ast.Functions.FirstOrDefault(f => f.Name == methodName);
        if (func != null)
            return func.Output?.TypeName;

        // Search for methods in the containing class
        var containingMethod = FindContainingMethod(ast, offset);
        if (containingMethod.HasValue)
        {
            var (cls, _) = containingMethod.Value;
            var method = cls.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method != null)
                return method.Output?.TypeName;
        }

        return null;
    }

    private static IEnumerable<CompletionItem> GetMembersForType(string typeName, ModuleNode ast, WorkspaceState workspace, DocumentState currentDoc)
    {
        var items = new List<CompletionItem>();

        // Parse generic type to get base type name
        var (baseTypeName, typeArgs) = ParseGenericType(typeName);

        // Check for classes with inheritance
        var cls = FindClassByName(baseTypeName, ast, workspace);
        if (cls != null)
        {
            items.AddRange(GetClassMembersWithInheritance(cls, ast, workspace));
            return items;
        }

        // Check local interfaces
        var iface = FindInterfaceByName(baseTypeName, ast, workspace);
        if (iface != null)
        {
            items.AddRange(GetInterfaceMembers(iface));
            return items;
        }

        // Check local enums
        var en = ast.Enums.FirstOrDefault(e => e.Name == baseTypeName);
        if (en != null)
        {
            items.AddRange(GetEnumMembers(en));

            // Also include extension methods for enums
            var ext = ast.EnumExtensions.FirstOrDefault(e => e.EnumName == baseTypeName);
            if (ext != null)
            {
                items.AddRange(GetExtensionMethods(ext));
            }
            return items;
        }

        // Check other documents for enums
        foreach (var doc in workspace.GetAllDocuments())
        {
            if (doc.Uri == currentDoc.Uri || doc.Ast == null) continue;

            var otherEnum = doc.Ast.Enums.FirstOrDefault(e => e.Name == baseTypeName);
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
        if (baseTypeName is "str" or "STRING" or "string")
        {
            items.AddRange(GetStringMembers());
        }

        // Built-in collection methods
        if (baseTypeName is "List" or "LIST" or "list" ||
            typeName.StartsWith("List<") || typeName.StartsWith("LIST<") || typeName.StartsWith("list<"))
        {
            items.AddRange(GetListMembers());
        }

        if (baseTypeName is "Dict" or "DICT" or "dict" ||
            typeName.StartsWith("Dict<") || typeName.StartsWith("DICT<") || typeName.StartsWith("dict<"))
        {
            items.AddRange(GetDictMembers());
        }

        if (baseTypeName is "Array" or "ARRAY" || typeName.EndsWith("[]"))
        {
            items.AddRange(GetArrayMembers());
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetClassMembersWithInheritance(ClassDefinitionNode cls, ModuleNode ast, WorkspaceState workspace)
    {
        var items = new List<CompletionItem>();
        var visitedClasses = new HashSet<string>();

        CollectClassMembersRecursive(cls, ast, workspace, items, visitedClasses, isInherited: false);

        return items;
    }

    private static void CollectClassMembersRecursive(ClassDefinitionNode cls, ModuleNode ast, WorkspaceState workspace,
        List<CompletionItem> items, HashSet<string> visitedClasses, bool isInherited)
    {
        if (visitedClasses.Contains(cls.Name))
            return;
        visitedClasses.Add(cls.Name);

        var inheritedPrefix = isInherited ? "(inherited) " : "";

        // Fields
        foreach (var field in cls.Fields)
        {
            // Skip if already added (override scenario)
            if (items.Any(i => i.Label == field.Name))
                continue;

            items.Add(new CompletionItem
            {
                Label = field.Name,
                Kind = CompletionItemKind.Field,
                Detail = $"{inheritedPrefix}(field) {field.Name}: {field.TypeName}",
                InsertText = field.Name
            });
        }

        // Properties
        foreach (var prop in cls.Properties)
        {
            if (items.Any(i => i.Label == prop.Name))
                continue;

            items.Add(new CompletionItem
            {
                Label = prop.Name,
                Kind = CompletionItemKind.Property,
                Detail = $"{inheritedPrefix}(property) {prop.Name}: {prop.TypeName}",
                InsertText = prop.Name
            });
        }

        // Methods
        foreach (var method in cls.Methods)
        {
            if (items.Any(i => i.Label == method.Name))
                continue;

            var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
            items.Add(new CompletionItem
            {
                Label = method.Name,
                Kind = CompletionItemKind.Method,
                Detail = $"{inheritedPrefix}(method) {method.Name}({paramList}): {method.Output?.TypeName ?? "void"}",
                InsertText = method.Name
            });
        }

        // Recurse into base class
        if (!string.IsNullOrEmpty(cls.BaseClass))
        {
            var baseClass = FindClassByName(cls.BaseClass, ast, workspace);
            if (baseClass != null)
            {
                CollectClassMembersRecursive(baseClass, ast, workspace, items, visitedClasses, isInherited: true);
            }
        }

        // Add interface methods (for reference/documentation)
        foreach (var ifaceName in cls.ImplementedInterfaces)
        {
            var iface = FindInterfaceByName(ifaceName, ast, workspace);
            if (iface != null)
            {
                foreach (var method in iface.Methods)
                {
                    if (items.Any(i => i.Label == method.Name))
                        continue;

                    var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
                    items.Add(new CompletionItem
                    {
                        Label = method.Name,
                        Kind = CompletionItemKind.Method,
                        Detail = $"(interface {ifaceName}) {method.Name}({paramList}): {method.Output?.TypeName ?? "void"}",
                        InsertText = method.Name
                    });
                }
            }
        }
    }

    private static IEnumerable<CompletionItem> GetArrayMembers()
    {
        return new[]
        {
            new CompletionItem { Label = "Length", Kind = CompletionItemKind.Property, Detail = "(property) Length: INT", InsertText = "Length" },
            new CompletionItem { Label = "Clone", Kind = CompletionItemKind.Method, Detail = "(method) Clone(): Array", InsertText = "Clone" },
            new CompletionItem { Label = "CopyTo", Kind = CompletionItemKind.Method, Detail = "(method) CopyTo(dest: Array, index: INT): void", InsertText = "CopyTo" },
        };
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
                return items;
            }

            // Check if we're in a constructor
            var containingCtor = FindContainingConstructor(ast, offset);
            if (containingCtor.HasValue)
            {
                items.AddRange(GetConstructorVariables(containingCtor.Value.Item1, containingCtor.Value.Item2, offset));
                return items;
            }

            // Check if we're in a property accessor (getter/setter/init)
            var containingAccessor = FindContainingPropertyAccessor(ast, offset);
            if (containingAccessor.HasValue)
            {
                items.AddRange(GetPropertyAccessorVariables(
                    containingAccessor.Value.Item1,
                    containingAccessor.Value.Item2,
                    containingAccessor.Value.Item3,
                    offset));
                return items;
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

        // Walk statements before cursor to collect all visible variables (bindings, loops, catch)
        foreach (var variable in CollectVisibleVariables(containingFunc.Body, offset))
        {
            var kindLabel = variable.Kind switch
            {
                "binding" => variable.IsMutable ? "var" : "let",
                "loop" => "for",
                "foreach" => "foreach",
                "catch" => "catch",
                _ => "var"
            };

            items.Add(new CompletionItem
            {
                Label = variable.Name,
                Kind = variable.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({kindLabel}) {variable.Name}: {variable.TypeName ?? "inferred"}",
                InsertText = variable.Name,
                SortText = "1" + variable.Name // Sort local variables after parameters
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

    private static (ClassDefinitionNode, ConstructorNode)? FindContainingConstructor(ModuleNode ast, int offset)
    {
        foreach (var cls in ast.Classes)
        {
            foreach (var ctor in cls.Constructors)
            {
                // Check constructor span
                if (offset >= ctor.Span.Start && offset < ctor.Span.End)
                {
                    return (cls, ctor);
                }
                // Also check body statements (constructor body may have separate spans)
                foreach (var stmt in ctor.Body)
                {
                    if (offset >= stmt.Span.Start && offset < stmt.Span.End)
                    {
                        return (cls, ctor);
                    }
                }
            }
        }
        return null;
    }

    private static (ClassDefinitionNode, PropertyNode, PropertyAccessorNode)? FindContainingPropertyAccessor(ModuleNode ast, int offset)
    {
        foreach (var cls in ast.Classes)
        {
            foreach (var prop in cls.Properties)
            {
                // Check getter - include body statements in span check
                if (prop.Getter != null && IsOffsetInAccessor(prop.Getter, offset))
                {
                    return (cls, prop, prop.Getter);
                }
                // Check setter - include body statements in span check
                if (prop.Setter != null && IsOffsetInAccessor(prop.Setter, offset))
                {
                    return (cls, prop, prop.Setter);
                }
                // Check init accessor - include body statements in span check
                if (prop.Initer != null && IsOffsetInAccessor(prop.Initer, offset))
                {
                    return (cls, prop, prop.Initer);
                }
            }
        }
        return null;
    }

    private static bool IsOffsetInAccessor(PropertyAccessorNode accessor, int offset)
    {
        // Check if in the accessor tag span itself
        if (offset >= accessor.Span.Start && offset < accessor.Span.End)
            return true;

        // Check if in any body statement (accessor body may have separate spans)
        foreach (var stmt in accessor.Body)
        {
            if (offset >= stmt.Span.Start && offset < stmt.Span.End)
                return true;
        }

        return false;
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

        // Add local variables (bindings, loops, catch variables)
        foreach (var variable in CollectVisibleVariables(method.Body, offset))
        {
            var kindLabel = variable.Kind switch
            {
                "binding" => variable.IsMutable ? "var" : "let",
                "loop" => "for",
                "foreach" => "foreach",
                "catch" => "catch",
                _ => "var"
            };

            items.Add(new CompletionItem
            {
                Label = variable.Name,
                Kind = variable.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({kindLabel}) {variable.Name}: {variable.TypeName ?? "inferred"}",
                InsertText = variable.Name,
                SortText = "2" + variable.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetConstructorVariables(ClassDefinitionNode cls, ConstructorNode ctor, int offset)
    {
        var items = new List<CompletionItem>();

        // Add 'this' keyword
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

        // Add constructor parameters
        foreach (var param in ctor.Parameters)
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

        // Add local variables from constructor body
        foreach (var variable in CollectVisibleVariables(ctor.Body, offset))
        {
            var kindLabel = variable.Kind switch
            {
                "binding" => variable.IsMutable ? "var" : "let",
                "loop" => "for",
                "foreach" => "foreach",
                "catch" => "catch",
                _ => "var"
            };

            items.Add(new CompletionItem
            {
                Label = variable.Name,
                Kind = variable.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({kindLabel}) {variable.Name}: {variable.TypeName ?? "inferred"}",
                InsertText = variable.Name,
                SortText = "2" + variable.Name
            });
        }

        return items;
    }

    private static IEnumerable<CompletionItem> GetPropertyAccessorVariables(ClassDefinitionNode cls, PropertyNode prop, PropertyAccessorNode accessor, int offset)
    {
        var items = new List<CompletionItem>();

        // Add 'this' keyword
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

        // Add other class properties (not the current one to avoid self-reference in getter)
        foreach (var otherProp in cls.Properties.Where(p => p.Name != prop.Name))
        {
            items.Add(new CompletionItem
            {
                Label = otherProp.Name,
                Kind = CompletionItemKind.Property,
                Detail = $"(property) {otherProp.Name}: {otherProp.TypeName}",
                InsertText = otherProp.Name,
                SortText = "0" + otherProp.Name
            });
        }

        // Add 'value' keyword for setters and init accessors
        if (accessor.Kind == PropertyAccessorNode.AccessorKind.Set || accessor.Kind == PropertyAccessorNode.AccessorKind.Init)
        {
            items.Add(new CompletionItem
            {
                Label = "value",
                Kind = CompletionItemKind.Keyword,
                Detail = $"(value) {prop.TypeName}",
                InsertText = "value",
                SortText = "1value"
            });
        }

        // Add local variables from accessor body
        foreach (var variable in CollectVisibleVariables(accessor.Body, offset))
        {
            var kindLabel = variable.Kind switch
            {
                "binding" => variable.IsMutable ? "var" : "let",
                "loop" => "for",
                "foreach" => "foreach",
                "catch" => "catch",
                _ => "var"
            };

            items.Add(new CompletionItem
            {
                Label = variable.Name,
                Kind = variable.IsMutable ? CompletionItemKind.Variable : CompletionItemKind.Constant,
                Detail = $"({kindLabel}) {variable.Name}: {variable.TypeName ?? "inferred"}",
                InsertText = variable.Name,
                SortText = "2" + variable.Name
            });
        }

        return items;
    }

    /// <summary>
    /// Represents a variable visible in scope (either from a binding, loop, or catch).
    /// </summary>
    private sealed record ScopeVariable(string Name, string? TypeName, bool IsMutable, string Kind);

    private static IEnumerable<BindStatementNode> CollectVisibleBindings(IReadOnlyList<StatementNode> statements, int offset)
    {
        return CollectVisibleVariables(statements, offset)
            .Where(v => v.Kind == "binding")
            .Select(v => statements
                .OfType<BindStatementNode>()
                .FirstOrDefault(b => b.Name == v.Name))
            .Where(b => b != null)!;
    }

    private static IEnumerable<ScopeVariable> CollectVisibleVariables(IReadOnlyList<StatementNode> statements, int offset)
    {
        var variables = new List<ScopeVariable>();

        foreach (var stmt in statements)
        {
            // Only include bindings that appear before the cursor
            if (stmt.Span.Start >= offset)
                break;

            if (stmt is BindStatementNode bind)
            {
                variables.Add(new ScopeVariable(bind.Name, bind.TypeName, bind.IsMutable, "binding"));
            }
            else if (stmt is ForStatementNode forStmt && offset >= forStmt.Span.Start && offset < forStmt.Span.End)
            {
                // Add loop variable
                variables.Add(new ScopeVariable(forStmt.VariableName, "i32", false, "loop"));
                variables.AddRange(CollectVisibleVariables(forStmt.Body, offset));
            }
            else if (stmt is WhileStatementNode whileStmt && offset >= whileStmt.Span.Start && offset < whileStmt.Span.End)
            {
                variables.AddRange(CollectVisibleVariables(whileStmt.Body, offset));
            }
            else if (stmt is IfStatementNode ifStmt && offset >= ifStmt.Span.Start && offset < ifStmt.Span.End)
            {
                // Determine which branch we're in
                var inThen = ifStmt.ThenBody.Any(s => offset >= s.Span.Start && offset < s.Span.End);
                if (inThen)
                {
                    variables.AddRange(CollectVisibleVariables(ifStmt.ThenBody, offset));
                }
                else if (ifStmt.ElseBody != null)
                {
                    var inElse = ifStmt.ElseBody.Any(s => offset >= s.Span.Start && offset < s.Span.End);
                    if (inElse)
                    {
                        variables.AddRange(CollectVisibleVariables(ifStmt.ElseBody, offset));
                    }
                }
            }
            else if (stmt is ForeachStatementNode foreachStmt && offset >= foreachStmt.Span.Start && offset < foreachStmt.Span.End)
            {
                // Add foreach iteration variable
                variables.Add(new ScopeVariable(foreachStmt.VariableName, foreachStmt.VariableType, false, "foreach"));
                variables.AddRange(CollectVisibleVariables(foreachStmt.Body, offset));
            }
            else if (stmt is TryStatementNode tryStmt && offset >= tryStmt.Span.Start && offset < tryStmt.Span.End)
            {
                // Check if we're in a catch clause
                var inCatch = false;
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    if (offset >= catchClause.Span.Start && offset < catchClause.Span.End)
                    {
                        inCatch = true;
                        // Add catch variable
                        if (!string.IsNullOrEmpty(catchClause.VariableName))
                        {
                            variables.Add(new ScopeVariable(catchClause.VariableName, catchClause.ExceptionType, false, "catch"));
                        }
                        variables.AddRange(CollectVisibleVariables(catchClause.Body, offset));
                        break;
                    }
                }
                if (!inCatch)
                {
                    variables.AddRange(CollectVisibleVariables(tryStmt.TryBody, offset));
                }
            }
            else if (stmt is DoWhileStatementNode doWhileStmt && offset >= doWhileStmt.Span.Start && offset < doWhileStmt.Span.End)
            {
                variables.AddRange(CollectVisibleVariables(doWhileStmt.Body, offset));
            }
            else if (stmt is DictionaryForeachNode dictForeach && offset >= dictForeach.Span.Start && offset < dictForeach.Span.End)
            {
                // Add key and value variables
                variables.Add(new ScopeVariable(dictForeach.KeyName, null, false, "dictkey"));
                variables.Add(new ScopeVariable(dictForeach.ValueName, null, false, "dictvalue"));
                variables.AddRange(CollectVisibleVariables(dictForeach.Body, offset));
            }
            else if (stmt is MatchStatementNode matchStmt && offset >= matchStmt.Span.Start && offset < matchStmt.Span.End)
            {
                // Find which case we're in and add pattern variables
                foreach (var caseNode in matchStmt.Cases)
                {
                    if (offset >= caseNode.Span.Start && offset < caseNode.Span.End)
                    {
                        CollectPatternVariables(caseNode.Pattern, variables);
                        variables.AddRange(CollectVisibleVariables(caseNode.Body, offset));
                        break;
                    }
                }
            }
        }

        return variables;
    }

    private static void CollectPatternVariables(PatternNode? pattern, List<ScopeVariable> variables)
    {
        if (pattern == null) return;

        switch (pattern)
        {
            case VariablePatternNode varPat:
                variables.Add(new ScopeVariable(varPat.Name, null, false, "pattern"));
                break;

            case VarPatternNode vPat:
                variables.Add(new ScopeVariable(vPat.Name, null, false, "pattern"));
                break;

            case SomePatternNode somePat:
                CollectPatternVariables(somePat.InnerPattern, variables);
                break;

            case OkPatternNode okPat:
                CollectPatternVariables(okPat.InnerPattern, variables);
                break;

            case ErrPatternNode errPat:
                CollectPatternVariables(errPat.InnerPattern, variables);
                break;

            case PositionalPatternNode posPat:
                foreach (var inner in posPat.Patterns)
                {
                    CollectPatternVariables(inner, variables);
                }
                break;

            case PropertyPatternNode propPat:
                foreach (var match in propPat.Matches)
                {
                    CollectPatternVariables(match.Pattern, variables);
                }
                break;

            case ListPatternNode listPat:
                foreach (var inner in listPat.Patterns)
                {
                    CollectPatternVariables(inner, variables);
                }
                if (listPat.SlicePattern != null)
                {
                    CollectPatternVariables(listPat.SlicePattern, variables);
                }
                break;
        }
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
